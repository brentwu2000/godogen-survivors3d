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
    [Export] public float MoveSpeed { get; set; } = 2.4f;
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

    /// Damage each touching enemy deals per second. Applied continuously rather
    /// than as discrete bites so being surrounded scales smoothly with how many
    /// actually reached you.
    [Export] public float ContactDamagePerSecond { get; set; } = 6.0f;

    /// Global speed multiplier, raised by the run director as the horde enrages.
    [Export] public float SpeedScale { get; set; } = 1.0f;

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

    /// Far enemies are updated on one tick in this many, spread by index.
    private const int FarStride = 4;

    public EnemyPool Pool { get; private set; } = null!;

    private SpatialGrid _grid = null!;
    private FlowField _field = null!;
    private HordeRenderer _renderer = null!;
    private Node3D? _player;
    private int[] _neighbours = null!;
    private int _tick;
    private ulong _rng = 0x9E3779B97F4A7C15UL;

    public override void _Ready()
    {
        var texture = GD.Load<Texture2D>("res://assets/sprites/zombie.png");
        var shader = GD.Load<Shader>("res://assets/shaders/horde_billboard.gdshader");
        if (texture == null || shader == null)
        {
            GD.PushError("Horde: missing zombie texture or billboard shader");
            SetPhysicsProcess(false);
            return;
        }

        Pool = new EnemyPool(Capacity);
        _grid = new SpatialGrid(Vector2.Zero, ArenaExtent, SeparationRadius * 2.0f, Capacity);
        _field = new FlowField(Vector2.Zero, ArenaExtent, 1.5f);
        _neighbours = new int[Capacity];

        _renderer = new HordeRenderer(texture, shader, SpriteHeight, Capacity, ArenaExtent);
        AddChild(_renderer.Node);

        _player = GetParent().GetNodeOrNull<Node3D>("Player");
        if (_player == null)
            GD.PushWarning("Horde: no sibling named Player — enemies will idle");

        BakeObstacles();

        for (int i = 0; i < InitialSpawn; i++)
            Spawn(RandomRingPosition(SpawnRingMin, SpawnRingMax));
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

    public bool Spawn(Vector3 position) =>
        Pool.TrySpawn(position, 10.0f, NextFloat() * Mathf.Tau);

    public override void _PhysicsProcess(double delta)
    {
        if (_player == null || Pool.Count == 0)
        {
            _renderer.Sync(Pool);
            return;
        }

        Vector3 playerPosition = _player.GlobalPosition;

        if (_tick % FieldRebuildInterval == 0)
            _field.Rebuild(playerPosition);

        _grid.Rebuild(Pool.Position, Pool.Count);

        float step = (float)delta;
        float activeSqr = ActiveRadius * ActiveRadius;
        var playerFlat = new Vector2(playerPosition.X, playerPosition.Z);
        int touching = 0;

        for (int i = 0; i < Pool.Count; i++)
        {
            Vector3 position = Pool.Position[i];
            var flat = new Vector2(position.X, position.Z);
            float toPlayerSqr = flat.DistanceSquaredTo(playerFlat);
            bool near = toPlayerSqr < activeSqr;

            // Far enemies run at a quarter rate with a quadruple step. Spreading
            // them by index keeps the per-tick cost flat instead of spiking every
            // fourth tick.
            if (!near && (i & (FarStride - 1)) != (_tick & (FarStride - 1)))
                continue;

            float scaledStep = near ? step : step * FarStride;

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

            if (toPlayerSqr < ContactRadius * ContactRadius)
            {
                desired = Vector2.Zero;
                touching++;
            }

            Vector2 velocity = desired * MoveSpeed * SpeedScale;

            if (near)
                velocity += Separation(i, position) * SeparationStrength;

            Pool.Velocity[i] = velocity;
            Pool.Position[i] = new Vector3(
                position.X + velocity.X * scaledStep,
                0.0f,
                position.Z + velocity.Y * scaledStep);
        }

        // Only enemies inside ActiveRadius are stepped every tick, and contact
        // requires being much closer than that, so the count is never stale.
        if (touching > 0 && _player is Player player)
            player.TakeDamage(touching * ContactDamagePerSecond * step);

        _tick++;
        _renderer.Sync(Pool);
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
    public bool Damage(int index, float amount, Vector2 knockback)
    {
        Pool.Health[index] -= amount;
        if (Pool.Health[index] > 0.0f)
        {
            Vector3 p = Pool.Position[index];
            Pool.Position[index] = new Vector3(p.X + knockback.X, 0.0f, p.Z + knockback.Y);
            return false;
        }

        Pool.DespawnAt(index);
        return true;
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
