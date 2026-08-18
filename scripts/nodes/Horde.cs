using Godot;

/// Owns the enemy simulation: pool, spatial grid, flow field and renderer.
///
/// The three costs that scale with enemy count are pathfinding, neighbour
/// queries and draw calls. Each is handled by a structure whose cost does not:
/// one flow field read by everyone, one grid rebuild per tick, one draw call.
public partial class Horde : Node3D
{
    [Export] public int Capacity { get; set; } = 512;
    [Export] public float ArenaExtent { get; set; } = 60.0f;

    /// World height of a variant at scale 1. Each variant multiplies it.
    [Export] public float SpriteHeight { get; set; } = 2.0f;

    /// Enemies closer than this to the player get separation and a full-rate
    /// update. Beyond it they only follow the field, on a strided schedule.
    [Export] public float ActiveRadius { get; set; } = 15.0f;

    /// Enemies stop closing at this distance so they surround the player instead
    /// of converging onto a single point.
    [Export] public float ContactRadius { get; set; } = 0.7f;

    /// Radius within which a zero flow reading is trusted as "you have arrived"
    /// rather than "this cell is blocked".
    [Export] public float FieldFallbackRadius { get; set; } = 3.0f;

    /// Global speed multiplier, raised by the run director as the horde enrages.
    [Export] public float SpeedScale { get; set; } = 1.0f;

    /// Run progress, 0 to 1, pushed in by the run director. Gates which variants
    /// may spawn, so escalation changes what arrives and not only how much.
    [Export] public float SpawnIntensity { get; set; }

    [Export] public int EnemyProjectileCapacity { get; set; } = 128;
    [Export] public float EnemyProjectileHeight { get; set; } = 0.3f;

    /// How close a spitter's shot has to pass to count as a hit.
    [Export] public float EnemyProjectileRadius { get; set; } = 0.6f;

    [Export] public float SeparationRadius { get; set; } = 0.75f;
    [Export] public float SeparationStrength { get; set; } = 8.0f;

    /// Ticks between flow field rebuilds. The field only has to be as fresh as
    /// the player's movement, and a stale frame costs a few centimetres of aim.
    [Export] public int FieldRebuildInterval { get; set; } = 8;

    /// Enemies placed at startup, in a ring around the origin. A wave spawner
    /// replaces this once the run timer exists.
    [Export] public int InitialSpawn { get; set; } = 200;
    [Export] public float SpawnRingMin { get; set; } = 12.0f;
    [Export] public float SpawnRingMax { get; set; } = 40.0f;

    /// Variant order. Drives three things at once — the .tres to load, the sprite
    /// stacked at that layer, and the byte stored per instance — so they cannot
    /// drift apart the way three separate lists would.
    private static readonly string[] TypeNames = { "walker", "runner", "brute", "bloater", "spitter" };

    /// A kill, for anything that wants to count them. A plain C# event rather
    /// than a Godot signal: both ends are C#, and EmitSignal marshals a Variant
    /// array per call — at a few dozen kills a second that is allocation this
    /// architecture spent five phases avoiding.
    public event System.Action<int, Vector3>? EnemyKilled;

    /// Something went off, and where. A bloater bursting and a thrown charge are
    /// the same event to anything watching — both are a radius of damage the
    /// player has to read off the screen in the moment it happens.
    public event System.Action<Vector3>? Exploded;

    public EnemyPool Pool { get; private set; } = null!;
    public EnemyTypeResource[] Types { get; private set; } = System.Array.Empty<EnemyTypeResource>();

    /// Spitter shots in flight. Damage to the player, so they live here rather
    /// than in the weapon handler's pool, which only ever hurts enemies.
    public ProjectilePool EnemyShots { get; private set; } = null!;

    /// Burning ground from thrown incendiaries. Owned here because the horde is
    /// what walks into it — the thing that ticks damage should be the thing that
    /// already iterates every enemy once a frame.
    public HazardField Hazards { get; private set; } = null!;

    [Export] public int HazardCapacity { get; set; } = 8;

    private SpatialGrid _grid = null!;
    private FlowField _field = null!;
    private HordeRenderer _renderer = null!;
    private HordeRenderer? _shotRenderer;
    private Node3D? _player;
    private int[] _neighbours = null!;
    private int _tick;
    private ulong _rng = 0x9E3779B97F4A7C15UL;

    public override void _Ready()
    {
        var shader = GD.Load<Shader>("res://assets/shaders/horde_billboard.gdshader");
        if (shader == null || !LoadTypes())
        {
            GD.PushError("Horde: missing billboard shader or variant table");
            SetPhysicsProcess(false);
            return;
        }

        var sprites = new string[TypeNames.Length];
        for (int i = 0; i < TypeNames.Length; i++)
            sprites[i] = $"res://assets/sprites/enemies/{TypeNames[i]}.png";

        Texture2DArray? texture = HordeRenderer.LoadArray(sprites);
        if (texture == null)
        {
            SetPhysicsProcess(false);
            return;
        }

        Pool = new EnemyPool(Capacity);
        EnemyShots = new ProjectilePool(EnemyProjectileCapacity);
        Hazards = new HazardField(HazardCapacity);
        BuildHazardDecals();
        _grid = new SpatialGrid(Vector2.Zero, ArenaExtent, SeparationRadius * 2.0f, Capacity);
        _field = new FlowField(Vector2.Zero, ArenaExtent, 1.5f);
        _neighbours = new int[Capacity];

        float maxScale = 1.0f;
        foreach (EnemyTypeResource type in Types)
            maxScale = Mathf.Max(maxScale, type.SpriteScale);

        _renderer = new HordeRenderer(texture, shader, SpriteHeight, Capacity, ArenaExtent,
                                      maxScale: maxScale, useColours: true);
        AddChild(_renderer.Node);

        Texture2DArray? shotTexture = HordeRenderer.LoadArray(new[] { "res://assets/sprites/bolt.png" });
        if (shotTexture != null)
        {
            _shotRenderer = new HordeRenderer(
                shotTexture, shader, EnemyProjectileHeight, EnemyProjectileCapacity, ArenaExtent,
                groundAnchored: false, bobAmplitude: 0.0f);
            AddChild(_shotRenderer.Node);
        }

        _player = GetParent().GetNodeOrNull<Node3D>("Player");
        if (_player == null)
            GD.PushWarning("Horde: no sibling named Player — enemies will idle");

        BakeObstacles();

        for (int i = 0; i < InitialSpawn; i++)
            Spawn(RandomRingPosition(SpawnRingMin, SpawnRingMax));
    }

    /// Loads the variant table in TypeNames order and checks each row agrees
    /// about which layer it draws from. A silently mismatched SpriteLayer shows
    /// up as brutes wearing the runner's sprite — a data bug that reads as a
    /// rendering one.
    private bool LoadTypes()
    {
        var types = new EnemyTypeResource[TypeNames.Length];

        for (int i = 0; i < TypeNames.Length; i++)
        {
            string path = $"res://resources/enemies/{TypeNames[i]}.tres";
            var type = GD.Load<EnemyTypeResource>(path);
            if (type == null)
            {
                GD.PushError($"Horde: missing {path} — run scripts/tools/BuildEnemyTypes.cs");
                return false;
            }

            if (type.SpriteLayer != i)
            {
                GD.PushError($"Horde: {path} declares layer {type.SpriteLayer} but is stacked at {i}");
                return false;
            }

            types[i] = type;
        }

        Types = types;
        return true;
    }

    /// Reads static level geometry into the flow field. Done here rather than in
    /// the builder because the field is a runtime structure — the .tscn only has
    /// to carry the colliders, which it needs for the player anyway.
    private void BakeObstacles()
    {
        var obstacles = GetParent().GetNodeOrNull<Node3D>("Obstacles");
        if (obstacles == null)
            return;

        foreach (Node child in obstacles.GetChildren())
        {
            if (child is not Node3D body)
                continue;

            var shapeNode = body.GetNodeOrNull<CollisionShape3D>("Collision");
            if (shapeNode?.Shape is not BoxShape3D box)
                continue;

            Vector3 size = box.Size * body.Scale;
            Vector3 center = body.Position;

            // Inflate by the enemy's own radius: a field that only blocks the
            // box itself steers enemies into paths their bodies do not fit.
            var half = new Vector2(size.X * 0.5f + SeparationRadius, size.Z * 0.5f + SeparationRadius);
            _field.BlockBox(new Vector2(center.X, center.Z), half);
        }
    }

    private Vector3 RandomRingPosition(float minRadius, float maxRadius)
    {
        float angle = NextFloat() * Mathf.Tau;
        float radius = Mathf.Lerp(minRadius, maxRadius, NextFloat());
        return new Vector3(Mathf.Cos(angle) * radius, 0.0f, Mathf.Sin(angle) * radius);
    }

    /// Obstacles are baked into the field once. They are static level geometry,
    /// so re-marking them on every rebuild would be pure waste.
    public void BlockBox(Vector2 center, Vector2 halfExtents) => _field.BlockBox(center, halfExtents);

    /// One flat disc per hazard slot, built once and moved as patches come and
    /// go. Creating a node per throw would allocate in the middle of the fight
    /// the throw was meant to win.
    private void BuildHazardDecals()
    {
        _hazardDecals = new MeshInstance3D[HazardCapacity];

        var shader = GD.Load<Shader>("res://assets/shaders/ground_marker.gdshader");

        for (int i = 0; i < HazardCapacity; i++)
        {
            Material material;
            if (shader != null)
            {
                // Its own material per patch, purely so each carries a different
                // seed. Eight fires animating in perfect unison read as one
                // effect drawn eight times, which is exactly what they were.
                var live = new ShaderMaterial { Shader = shader };
                live.SetShaderParameter("inner_colour", new Color(1.0f, 0.78f, 0.28f));
                live.SetShaderParameter("outer_colour", new Color(0.85f, 0.16f, 0.03f));
                live.SetShaderParameter("strength", 0.7f);
                live.SetShaderParameter("churn", 2.6f);
                live.SetShaderParameter("flicker", 0.45f);
                live.SetShaderParameter("seed", i * 2.399f);
                material = live;
            }
            else
            {
                GD.PushWarning("Horde: missing ground_marker.gdshader — burning ground will be a flat disc");
                material = new StandardMaterial3D
                {
                    AlbedoColor = new Color(1.0f, 0.32f, 0.04f, 0.6f),
                    ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                    Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                    DepthDrawMode = BaseMaterial3D.DepthDrawModeEnum.Disabled,
                };
            }

            // A flat quad rather than a cylinder: the sides were never visible
            // from this camera, and a quad has the clean 0..1 UV the shader needs
            // to know where the middle of the fire is.
            var decal = new MeshInstance3D
            {
                Name = $"Hazard{i}",
                Mesh = new QuadMesh { Size = new Vector2(2.0f, 2.0f) },
                MaterialOverride = material,
                RotationDegrees = new Vector3(-90.0f, 0.0f, 0.0f),
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                Visible = false,
            };

            AddChild(decal);
            _hazardDecals[i] = decal;
        }
    }

    private MeshInstance3D[] _hazardDecals = System.Array.Empty<MeshInstance3D>();

    /// Ticks the burning ground and shows where it is. Enemies only: the thrower
    /// picks the spot, and a patch they also have to avoid turns a tactical item
    /// into a way to kill yourself while being pushed backwards. The bloater
    /// already owns "your own kills can hurt you".
    private void StepHazards(float delta)
    {
        Hazards.Step(delta);

        for (int i = Pool.Count - 1; i >= 0; i--)
        {
            float dps = Hazards.DamageAt(Pool.Position[i]);

            // No flash. The hit flash is confirmation that a discrete shot
            // landed, and burning ground applies damage sixty times a second —
            // so it re-lit every enemy standing in it, every tick, and a crowd
            // caught in a molotov rendered as a group of solid white cut-outs
            // for as long as the fire burned. What tells the player they are
            // burning is the fire drawn over them.
            if (dps > 0.0f)
                Damage(i, dps * delta, Vector2.Zero, allowBlast: true, flash: false);
        }

        for (int i = 0; i < _hazardDecals.Length; i++)
        {
            bool active = i < Hazards.Count;
            _hazardDecals[i].Visible = active;
            if (!active)
                continue;

            _hazardDecals[i].Position = Hazards.Position[i] + new Vector3(0.0f, 0.02f, 0.0f);
            // X and Y, not X and Z. Scale is applied in local space before the
            // rotation that lays the quad flat, and a quad's extent is in its own
            // XY — the cylinder this replaced had its height on Y, which is why
            // the axes moved.
            _hazardDecals[i].Scale = new Vector3(Hazards.Radius[i], Hazards.Radius[i], 1.0f);
        }
    }

    /// Re-reads level geometry into the field. Obstacles are static within a
    /// run, so this is not needed in play — but a level regenerated underneath a
    /// live field leaves the field describing the previous map, which is exactly
    /// the failure the generation order exists to prevent.
    public void RebakeObstacles()
    {
        _field?.ClearBlocked();
        BakeObstacles();
    }

    /// Points the field at somewhere other than the player. Exposed so a level
    /// can be asked "is this reachable" using the game's own pathing rather than
    /// a second implementation written next to the generator — the two would
    /// agree with each other and not with the enemies.
    public void RebuildFieldAround(Vector3 target) => _field?.Rebuild(target);

    /// Direction the field would send someone standing here, or zero where it
    /// has no route.
    public Vector2 SampleField(Vector3 position) => _field?.Sample(position) ?? Vector2.Zero;

    /// Spawns the baseline variant. Probes that want a known quantity call this;
    /// the run director calls SpawnByIntensity instead.
    public bool Spawn(Vector3 position) => Spawn(position, 0);

    public bool Spawn(Vector3 position, int type)
    {
        if (type < 0 || type >= Types.Length)
            return false;

        return Pool.TrySpawn(position, (byte)type, Types[type].MaxHealth, NextFloat() * Mathf.Tau);
    }

    /// Picks a variant by weight among those the run has unlocked. Late in a run
    /// the composition is what changed, not just the rate — the same 300 seconds
    /// asks a different question at the end than at the start.
    public bool SpawnByIntensity(Vector3 position)
    {
        float total = 0.0f;
        for (int i = 0; i < Types.Length; i++)
        {
            if (SpawnIntensity >= Types[i].UnlockIntensity)
                total += Types[i].SpawnWeight;
        }

        if (total <= 0.0f)
            return Spawn(position, 0);

        float roll = NextFloat() * total;
        for (int i = 0; i < Types.Length; i++)
        {
            if (SpawnIntensity < Types[i].UnlockIntensity)
                continue;

            roll -= Types[i].SpawnWeight;
            if (roll <= 0.0f)
                return Spawn(position, i);
        }

        // Only reachable on floating-point slack at the very end of the range.
        return Spawn(position, 0);
    }

    /// How fast a hit flash fades, in units per second. About a tenth of a second
    /// of white: long enough to register at sixty frames, short enough that a
    /// weapon firing three times a second does not leave the target permanently
    /// lit and therefore permanently uninformative.
    [Export] public float HitFlashFade { get; set; } = 9.0f;

    public override void _PhysicsProcess(double delta)
    {
        float step = (float)delta;

        // Every enemy, not only the near ones. The movement loop below skips
        // distant instances on a stride, and a flash that decayed on the same
        // schedule would leave anything shot at range glowing until it wandered
        // close enough to be updated.
        float fade = HitFlashFade * step;
        for (int i = 0; i < Pool.Count; i++)
        {
            if (Pool.HitFlash[i] > 0.0f)
                Pool.HitFlash[i] = Mathf.Max(0.0f, Pool.HitFlash[i] - fade);
        }

        // Bleeding, walked backwards because it kills and a kill swap-removes.
        // No flash and no blast: a wound ticking is not a hit landing, and a
        // bleed that could set off a bloater would make the knife a bomb.
        for (int i = Pool.Count - 1; i >= 0; i--)
        {
            if (Pool.BleedRemaining[i] <= 0.0f)
                continue;

            Pool.BleedRemaining[i] -= step;
            Damage(i, Pool.Bleed[i] * step, Vector2.Zero, allowBlast: false, flash: false);
        }

        if (_player == null || Pool.Count == 0)
        {
            StepEnemyShots(step);
            StepHazards(step);
            _renderer.Sync(Pool, Types);
            return;
        }

        Vector3 playerPosition = _player.GlobalPosition;

        if (_tick % FieldRebuildInterval == 0)
            _field.Rebuild(playerPosition);

        _grid.Rebuild(Pool.Position, Pool.Count);

        float activeSqr = ActiveRadius * ActiveRadius;
        var playerFlat = new Vector2(playerPosition.X, playerPosition.Z);
        float contactDamage = 0.0f;

        for (int i = 0; i < Pool.Count; i++)
        {
            EnemyTypeResource type = Types[Pool.Type[i]];
            Vector3 position = Pool.Position[i];
            var flat = new Vector2(position.X, position.Z);
            float toPlayerSqr = flat.DistanceSquaredTo(playerFlat);
            bool near = toPlayerSqr < activeSqr;

            // Far enemies run at a reduced rate with a proportionally longer
            // step, spread by index so the per-tick cost stays flat rather than
            // spiking every nth tick. The stride comes from the variant, because
            // a fast one stepped at quarter rate covers its distance in visible
            // jumps — the saving is real but it is not free at any speed.
            int stride = near ? 1 : type.FarStride;
            if (stride > 1 && (i & (stride - 1)) != (_tick & (stride - 1)))
                continue;

            float scaledStep = step * stride;

            Vector2 desired = _field.Sample(position);
            if (desired == Vector2.Zero)
            {
                // Zero is legitimate at the target cell itself; anywhere else it
                // means blocked or unreachable, and a straight line at the player
                // would walk into the very wall the field is routing around.
                desired = toPlayerSqr < FieldFallbackRadius * FieldFallbackRadius
                    ? (playerFlat - flat).Normalized()
                    : Pool.Velocity[i].Normalized();
            }

            if (type.Behavior == EnemyBehavior.Ranged)
            {
                if (StepRanged(i, type, position, playerFlat, flat, toPlayerSqr, scaledStep))
                    desired = Vector2.Zero;
            }
            else if (toPlayerSqr < ContactRadius * ContactRadius)
            {
                desired = Vector2.Zero;
                contactDamage += type.ContactDamagePerSecond;
            }

            Vector2 velocity = desired * type.MoveSpeed * SpeedScale;

            if (near)
                velocity += Separation(i, position) * SeparationStrength;

            Pool.Velocity[i] = velocity;
            Pool.Position[i] = new Vector3(
                position.X + velocity.X * scaledStep,
                0.0f,
                position.Z + velocity.Y * scaledStep);
        }

        // Summed per variant rather than counted: a brute leaning on you is
        // worth more than three walkers, and the smooth accumulation is what
        // makes being surrounded scale with who actually reached you.
        if (contactDamage > 0.0f && _player is Player player)
        {
            player.TakeContactDamage(contactDamage, step);

            // Thorns pays back whoever is actually touching, not the crowd.
            // Being surrounded is what the card answers, so it has to scale with
            // how surrounded you are — the same shape the damage coming in has.
            float thorns = player.Mods.Thorns;
            if (thorns > 0.0f)
            {
                var at = new Vector2(player.GlobalPosition.X, player.GlobalPosition.Z);
                for (int i = Pool.Count - 1; i >= 0; i--)
                {
                    Vector3 q = Pool.Position[i];
                    float dx = q.X - at.X, dz = q.Z - at.Y;
                    if (dx * dx + dz * dz < ContactRadius * ContactRadius)
                        Damage(i, thorns * step, Vector2.Zero, allowBlast: true, flash: false);
                }
            }
        }

        StepEnemyShots(step);
        StepHazards(step);

        _tick++;
        _renderer.Sync(Pool, Types);
    }

    /// Ranged behaviour: hold at standoff and shoot. Returns true when the
    /// enemy should stop closing.
    ///
    /// The cooldown ticks by the enemy's own scaled step, not the frame's, or a
    /// strided spitter would fire at a quarter rate purely because it is far
    /// away — a distance the standoff already decided it is happy with.
    private bool StepRanged(int index, EnemyTypeResource type, Vector3 position,
                            Vector2 playerFlat, Vector2 flat, float toPlayerSqr, float scaledStep)
    {
        if (toPlayerSqr > type.StandoffDistance * type.StandoffDistance)
            return false;

        Pool.AttackCooldown[index] -= scaledStep;
        if (Pool.AttackCooldown[index] > 0.0f)
            return true;

        Pool.AttackCooldown[index] = type.AttackInterval;

        Vector2 aim = playerFlat - flat;
        if (aim.LengthSquared() > 0.0001f)
        {
            EnemyShots.TrySpawn(
                new Vector3(position.X, 0.0f, position.Z),
                aim.Normalized() * type.ProjectileSpeed,
                type.ProjectileDamage,
                0.0f,
                type.StandoffDistance * 1.5f / type.ProjectileSpeed,
                1);
        }

        return true;
    }

    /// Moves spitter shots and resolves them against the player. Enemy shots
    /// ignore each other and the horde — friendly fire between variants would
    /// make a crowd of spitters kill itself, which is funny once.
    private void StepEnemyShots(float delta)
    {
        var player = _player as Player;

        for (int i = EnemyShots.Count - 1; i >= 0; i--)
        {
            EnemyShots.Life[i] -= delta;
            if (EnemyShots.Life[i] <= 0.0f)
            {
                EnemyShots.DespawnAt(i);
                continue;
            }

            Vector2 velocity = EnemyShots.Velocity[i];
            Vector3 position = EnemyShots.Position[i];
            position = new Vector3(position.X + velocity.X * delta, 0.0f, position.Z + velocity.Y * delta);
            EnemyShots.Position[i] = position;

            if (player == null || !player.IsAlive)
                continue;

            Vector3 toPlayer = player.GlobalPosition - position;
            float flatSqr = toPlayer.X * toPlayer.X + toPlayer.Z * toPlayer.Z;
            if (flatSqr > EnemyProjectileRadius * EnemyProjectileRadius)
                continue;

            player.TakeDamage(EnemyShots.Damage[i]);
            EnemyShots.DespawnAt(i);
        }

        _shotRenderer?.Sync(EnemyShots, EnemyProjectileHeight);
    }

    // --- Weapon queries -----------------------------------------------------
    //
    // These scan the pool linearly rather than going through SpatialGrid. The
    // grid's cells are sized for separation (about 1.5m), so a weapon reaching
    // 8m would have to walk dozens of cells; and weapons fire a few times a
    // second, not once per enemy per tick. A flat scan of a few hundred entries
    // is both simpler and faster at that rate.

    /// Nearest living enemy within radius, or -1.
    public int NearestWithin(Vector3 point, float radius)
    {
        float bestSqr = radius * radius;
        int best = -1;

        for (int i = 0; i < Pool.Count; i++)
        {
            float d = FlatDistanceSquared(Pool.Position[i], point);
            if (d < bestSqr)
            {
                bestSqr = d;
                best = i;
            }
        }

        return best;
    }

    /// Enemies inside a circular sector: the melee swing. A full 180-degree half
    /// angle degenerates to a circle, which is how "360 degree knockback" weapons
    /// are expressed.
    public int QueryArc(Vector3 origin, Vector2 forward, float radius, float halfAngleDegrees, int[] result)
    {
        float radiusSqr = radius * radius;
        float cosLimit = Mathf.Cos(Mathf.DegToRad(halfAngleDegrees));
        int written = 0;

        for (int i = 0; i < Pool.Count && written < result.Length; i++)
        {
            Vector3 delta3 = Pool.Position[i] - origin;
            var delta = new Vector2(delta3.X, delta3.Z);
            float distanceSqr = delta.LengthSquared();
            if (distanceSqr > radiusSqr)
                continue;

            // A target directly on top of the attacker has no direction; count it
            // as a hit rather than dropping it on a divide-by-zero.
            if (distanceSqr > 0.0001f && forward.Dot(delta / Mathf.Sqrt(distanceSqr)) < cosLimit)
                continue;

            result[written++] = i;
        }

        return written;
    }

    /// Enemies within `thickness` of a ray, ordered nearest first so penetration
    /// can consume them in the order the shot would actually meet them.
    public int QueryRay(Vector3 origin, Vector2 direction, float length, float thickness, int[] result)
    {
        int written = 0;

        for (int i = 0; i < Pool.Count && written < result.Length; i++)
        {
            Vector3 delta3 = Pool.Position[i] - origin;
            var delta = new Vector2(delta3.X, delta3.Z);

            float along = delta.Dot(direction);
            if (along < 0.0f || along > length)
                continue;

            float perpendicular = (delta - direction * along).Length();
            if (perpendicular > thickness)
                continue;

            result[written++] = i;
        }

        SortByDistanceAlong(result, written, origin, direction);
        return written;
    }

    /// Returns true when the hit killed the enemy. The index is invalidated by a
    /// kill (the pool swap-removes), so callers iterating a hit list must walk it
    /// backwards or re-query.
    public bool Damage(int index, float amount, Vector2 knockback) =>
        Damage(index, amount, knockback, allowBlast: true, flash: true);

    private bool Damage(int index, float amount, Vector2 knockback, bool allowBlast) =>
        Damage(index, amount, knockback, allowBlast, flash: true);

    private bool Damage(int index, float amount, Vector2 knockback, bool allowBlast, bool flash)
    {
        // A stale index is not a caller error here, it is the normal consequence
        // of a death blast: one hit can remove several enemies, so every index
        // captured before it — including the rest of a melee swing's own hit list,
        // walked backwards exactly as the contract says — can be past the end by
        // the time it is used.
        //
        // Without this, the damage lands on a dead slot whose leftover health may
        // still be at or below zero, and the pool despawns an entry that was
        // never live. Count goes down without anything leaving, and a few of those
        // drive it negative — at which point the next spawn writes to index -1 and
        // the crash is several seconds and one system away from the blast that
        // caused it.
        if (index < 0 || index >= Pool.Count)
            return false;

        EnemyTypeResource type = Types[Pool.Type[index]];

        Pool.Health[index] -= amount;
        if (Pool.Health[index] > 0.0f)
        {
            // The only confirmation a shot landed on something that lived. A
            // brute absorbing a magazine and a rifle missing it look identical
            // without this, because the brute keeps walking either way.
            if (flash)
                Pool.HitFlash[index] = 1.0f;

            // Resistance is a multiplier on displacement, not a threshold: a
            // knockback weapon should always do something to a brute, just far
            // less than it does to a walker.
            Vector3 p = Pool.Position[index];
            Pool.Position[index] = new Vector3(
                p.X + knockback.X * type.KnockbackScale,
                0.0f,
                p.Z + knockback.Y * type.KnockbackScale);
            return false;
        }

        Vector3 deathPosition = Pool.Position[index];
        Pool.DespawnAt(index);

        if (allowBlast && type.DeathBlastRadius > 0.0f)
            Blast(deathPosition, type.DeathBlastRadius, type.DeathBlastDamage);

        ApplyKillRules(deathPosition);
        EnemyKilled?.Invoke(type.SpriteLayer, deathPosition);
        return true;
    }

    /// Opens a wound. Refreshes rather than stacks: two knives should be twice
    /// the swings, not twice the bleed on the same body, and a stacking wound is
    /// a number that runs away in exactly the crowd it was designed for.
    public void ApplyBleed(int index, float damagePerSecond, float seconds)
    {
        if (index < 0 || index >= Pool.Count)
            return;

        Pool.Bleed[index] = Mathf.Max(Pool.Bleed[index], damagePerSecond);
        Pool.BleedRemaining[index] = Mathf.Max(Pool.BleedRemaining[index], seconds);
    }

    /// The nearest enemy to a point that is not `except`. For a ricochet picking
    /// its next target — it has to be somebody new, or the arrow bounces between
    /// the corpse it just made and itself.
    public int NearestExcept(Vector3 point, float radius, int except)
    {
        int best = -1;
        float bestSqr = radius * radius;

        for (int i = 0; i < Pool.Count; i++)
        {
            if (i == except)
                continue;

            float d = FlatDistanceSquared(Pool.Position[i], point);
            if (d < bestSqr)
            {
                bestSqr = d;
                best = i;
            }
        }

        return best;
    }

    /// A thrown charge going off. Enemies only, for the same reason the burning
    /// ground is: the player chose where it lands, and a grenade that can also
    /// kill them is a mistake generator on a map that is constantly pushing them
    /// backwards. Returns how many it killed, so the thrower can say so.
    ///
    /// Blast kills do not chain into bloaters. One thrown item resolving into a
    /// pile of secondary explosions is a depth nobody chose, exactly as it is
    /// when a bloater starts it.
    public int Detonate(Vector3 center, float radius, float damage)
    {
        int killed = 0;
        Exploded?.Invoke(center);

        for (int i = Pool.Count - 1; i >= 0; i--)
        {
            if (FlatDistanceSquared(Pool.Position[i], center) >= radius * radius)
                continue;

            Vector3 away = Pool.Position[i] - center;
            var push = new Vector2(away.X, away.Z).Normalized() * 0.4f;

            if (Damage(i, damage, push, allowBlast: false))
                killed++;
        }

        return killed;
    }

    /// What this run's upgrades do when something dies.
    ///
    /// Rolled here rather than by whoever landed the hit, because a kill is a
    /// kill however it happened — burning ground and a thrown charge earn the
    /// same rules a rifle does. Both effects are deliberately small: this is
    /// chip damage that chains through a crowd, not a second pipe bomb, and a
    /// chain that could start another chain is a depth nobody chose.
    private void ApplyKillRules(Vector3 at)
    {
        if (_player is not Player player)
            return;

        RunModifiers mods = player.Mods;

        if (mods.Lifesteal > 0.0f)
            player.Heal(mods.Lifesteal);

        if (mods.IgniteChance > 0.0f && NextFloat() < mods.IgniteChance)
            Hazards.Add(at, 2.2f * mods.AreaScale, 14.0f, 3.0f);

        if (mods.DetonateChance > 0.0f && NextFloat() < mods.DetonateChance)
        {
            Exploded?.Invoke(at);

            for (int i = Pool.Count - 1; i >= 0; i--)
            {
                float radius = 2.6f * mods.AreaScale;
                if (FlatDistanceSquared(Pool.Position[i], at) < radius * radius)
                    Damage(i, 18.0f, Vector2.Zero, allowBlast: false, flash: true);
            }
        }
    }

    /// A death blast, resolved one level deep — blast kills cannot blast in turn.
    /// A pile of bloaters is otherwise a chain reaction whose depth is however
    /// many happened to be standing together, which is both a frame spike and a
    /// balance number nobody chose.
    private void Blast(Vector3 center, float radius, float damage)
    {
        Exploded?.Invoke(center);

        if (_player is Player player)
        {
            float toPlayerSqr = FlatDistanceSquared(player.GlobalPosition, center);
            if (toPlayerSqr < radius * radius)
                player.TakeDamage(damage);
        }

        // Backwards: a kill swap-removes the last entry into the current slot,
        // which a forward walk would then skip.
        for (int i = Pool.Count - 1; i >= 0; i--)
        {
            if (FlatDistanceSquared(Pool.Position[i], center) < radius * radius)
                Damage(i, damage, Vector2.Zero, allowBlast: false);
        }
    }

    private void SortByDistanceAlong(int[] indices, int count, Vector3 origin, Vector2 direction)
    {
        // Insertion sort: hit lists are single digits in practice, where it beats
        // anything with a partitioning step.
        for (int i = 1; i < count; i++)
        {
            int value = indices[i];
            float key = AlongDistance(value);
            int j = i - 1;
            while (j >= 0 && AlongDistance(indices[j]) > key)
            {
                indices[j + 1] = indices[j];
                j--;
            }
            indices[j + 1] = value;
        }

        float AlongDistance(int index)
        {
            Vector3 delta3 = Pool.Position[index] - origin;
            return new Vector2(delta3.X, delta3.Z).Dot(direction);
        }
    }

    private static float FlatDistanceSquared(Vector3 a, Vector3 b)
    {
        float dx = a.X - b.X;
        float dz = a.Z - b.Z;
        return dx * dx + dz * dz;
    }

    /// Pushes away from neighbours inside SeparationRadius, weighted by how deep
    /// the overlap is. Without this the whole horde collapses onto one point and
    /// reads as a single sprite.
    private Vector2 Separation(int index, Vector3 position)
    {
        int count = _grid.QueryNear(position, _neighbours);
        var push = Vector2.Zero;

        for (int n = 0; n < count; n++)
        {
            int other = _neighbours[n];
            if (other == index)
                continue;

            Vector3 delta3 = position - Pool.Position[other];
            var delta = new Vector2(delta3.X, delta3.Z);
            float distanceSqr = delta.LengthSquared();
            if (distanceSqr >= SeparationRadius * SeparationRadius || distanceSqr < 0.0001f)
                continue;

            float distance = Mathf.Sqrt(distanceSqr);
            push += delta / distance * (1.0f - distance / SeparationRadius);
        }

        return push;
    }

    /// Deterministic, allocation-free, and independent of engine RNG state so a
    /// capture run reproduces frame for frame.
    private float NextFloat()
    {
        _rng ^= _rng << 13;
        _rng ^= _rng >> 7;
        _rng ^= _rng << 17;
        return (_rng >> 40) / 16777216.0f;
    }
}
