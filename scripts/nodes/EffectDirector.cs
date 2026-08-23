using Godot;

/// Everything the run makes a flash about.
///
/// The complaint this answers is that firing and killing looked like nothing:
/// a rifle emptying into a crowd produced a hit flash on the target and a tracer
/// that was already gone, and a kill made a sprite vanish. Every system was
/// correct and the screen said almost none of it.
///
/// Same shape as SoundDirector, deliberately — one node, subscribed to the same
/// events, with the same rule that the cost of a crowd must not scale with the
/// crowd. One MultiMesh, one material, a fixed pool, and a minimum gap on the
/// effects that can arrive a dozen times a second.
public partial class EffectDirector : Node3D
{
    [Export] public int Capacity { get; set; } = 192;

    /// Shortest gap between two impact puffs, in seconds. A wide melee arc lands
    /// five hits in one frame; five overlapping flashes at the same instant are
    /// one bright blob that says nothing about how many.
    [Export] public float ImpactInterval { get; set; } = 0.035f;

    // Both size and alpha were found by overshooting in each direction and
    // measuring. Additive blending saturates, so the first pass — six-metre puffs
    // near full alpha — was not a bright explosion but a flat orange disc over a
    // quarter of the screen. The correction went too far the other way: counting
    // bright pixels across the captured run found about sixty per frame out of
    // two million, which is an effect system that technically runs.
    //
    // The scale that matters is the character: 2.2 m of player is about 130 px,
    // so a metre is roughly sixty pixels and anything under half a metre is a
    // speck. These are sized in metres against that, not against taste.
    private static readonly Color Muzzle = new(1.0f, 0.84f, 0.46f, 0.7f);
    private static readonly Color Spark = new(1.0f, 0.92f, 0.72f, 0.5f);
    private static readonly Color Gore = new(0.62f, 0.80f, 0.34f, 0.45f);
    private static readonly Color Blast = new(1.0f, 0.58f, 0.22f, 0.5f);
    private static readonly Color Smoke = new(0.30f, 0.26f, 0.24f, 0.28f);

    private EffectPool _pool = null!;
    private MultiMesh _multi = null!;
    private float[] _buffer = System.Array.Empty<float>();

    private Horde? _horde;
    private Player? _player;
    private WeaponHandler? _weapons;

    private float _clock;
    private float _lastImpact = float.NegativeInfinity;
    private int _hazardCount;
    private ulong _rng = 0x9E3779B97F4A7C15UL;

    /// 12 floats of transform plus 4 of colour: the colour block carries the tint
    /// and the fade, which is the whole reason this is a separate renderer from
    /// the horde's rather than another layer of it.
    private const int FloatsPerInstance = 16;

    public override void _Ready()
    {
        _pool = new EffectPool(Capacity);
        _buffer = new float[Capacity * FloatsPerInstance];

        var shader = GD.Load<Shader>("res://assets/shaders/effect.gdshader");
        if (shader == null)
        {
            GD.PushWarning("EffectDirector: missing effect.gdshader — the run will be silent to look at");
            return;
        }

        var material = new ShaderMaterial { Shader = shader };
        material.SetShaderParameter("puff", Puff());

        _multi = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = true,
            Mesh = new QuadMesh { Size = Vector2.One, Material = material },
            InstanceCount = Capacity,
            VisibleInstanceCount = 0,
        };

        AddChild(new MultiMeshInstance3D
        {
            Name = "Puffs",
            Multimesh = _multi,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,

            // Same trap as the horde: instances are scattered across the arena
            // while the mesh's own bounds are one quad, so without a custom AABB
            // the renderer culls every effect the moment the origin leaves frame.
            CustomAabb = new Aabb(new Vector3(-70.0f, -2.0f, -70.0f), new Vector3(140.0f, 20.0f, 140.0f)),
        });

        Node? root = GetParent();
        _horde = root?.GetNodeOrNull<Horde>("Horde");
        _player = root?.GetNodeOrNull<Player>("Player");
        _weapons = _player?.GetNodeOrNull<WeaponHandler>("WeaponHandler");

        if (_horde != null)
        {
            _horde.EnemyKilled += OnEnemyKilled;
            _horde.Exploded += OnExploded;
            _hazardCount = _horde.Hazards.Count;
        }

        if (_weapons != null)
        {
            _weapons.Fired += OnFired;
            _weapons.Hit += OnHit;
        }
    }

    /// The horde's and the weapon's events are plain C# delegates, so they hold a
    /// strong reference to this node — a subscription that outlives the scene is
    /// a call into a freed object.
    public override void _ExitTree()
    {
        if (_horde != null)
        {
            _horde.EnemyKilled -= OnEnemyKilled;
            _horde.Exploded -= OnExploded;
        }

        if (_weapons != null)
        {
            _weapons.Fired -= OnFired;
            _weapons.Hit -= OnHit;
        }
    }

    public override void _Process(double delta)
    {
        float step = (float)delta;
        _clock += step;

        StepHazards();
        _pool.Step(step);
        Sync();
    }

    /// What a shot looks like, built from the weapon rather than from its
    /// category.
    ///
    /// **Nine weapons shared four effects, and three of those four were the same
    /// white puff.** A pump shotgun throwing eight pellets across twenty degrees
    /// and a marksman rifle putting one round through three bodies at thirty
    /// metres produced identical feedback, so the only evidence the player had
    /// that they were different weapons was the number in the corner. The traits
    /// were all implemented; none of them were *visible*.
    ///
    /// Every branch below reads off the weapon's own numbers, so a weapon added
    /// to `resources/weapons/` gets an appropriate flash without being listed
    /// here. The trait switch is for the ones whose character is a shape rather
    /// than a magnitude.
    private void OnFired(WeaponResource weapon, Vector3 origin, Vector2 direction)
    {
        WeaponCategory category = weapon.Category;
        // At the muzzle, not at the character. A flash centred on the player
        // reads as the player glowing; a metre out along the shot reads as a gun.
        // Flattened, because the player is planted and `EffectPool.Spawn`
        // plants what it is given. Passing the player's real Y would add the
        // ground height twice and hang every muzzle flash a metre and a half in
        // the air on a crest — while looking perfectly correct on the flat ground
        // around the spawn, which is where it would be checked.
        Vector3 at = new Vector3(origin.X, 0.0f, origin.Z)
                   + new Vector3(direction.X, 0.0f, direction.Y) * 0.75f;

        if (category is WeaponCategory.MeleeShort or WeaponCategory.MeleeLong)
        {
            // A swing has no flash. It gets a pale smear along the arc instead,
            // because a melee player whose weapon shows nothing cannot tell a
            // weapon on cooldown from one out of range.
            //
            // Sized off the weapon's reach, so a scythe sweeping 3.4 m draws a
            // wider arc than a knife at 1.6 m. Cleave gets a second, slower smear
            // behind the first: the trait hits everything in the arc, and one
            // streak cannot say that.
            float reach = Mathf.Max(1.0f, weapon.BaseRange);
            _pool.Spawn(at + new Vector3(0.0f, 0.4f, 0.0f), reach * 0.42f, reach * 0.92f,
                        new Color(0.80f, 0.86f, 0.95f, 0.24f), 0.12f, direction * (reach * 1.4f));

            if (weapon.Trait == WeaponTrait.Cleave)
            {
                _pool.Spawn(at + new Vector3(0.0f, 0.55f, 0.0f), reach * 0.30f, reach * 1.20f,
                            new Color(0.90f, 0.80f, 0.62f, 0.16f), 0.22f, direction * (reach * 0.7f));
            }

            Kick(0.05f);
            return;
        }

        // A bow has no powder, and the absence is the character.
        //
        // Giving it a muzzle flash was the single worst thing about the old
        // effect: a drawn string releasing is a quiet event, and lighting it up
        // like a rifle made the bow feel like a rifle that happened to be slow.
        if (category == WeaponCategory.BowCrossbow)
        {
            _pool.Spawn(at + new Vector3(0.0f, 0.30f, 0.0f), 0.22f, 0.06f,
                        new Color(0.86f, 0.84f, 0.74f, 0.30f), 0.06f, direction * 1.2f);
            Kick(0.16f);
            return;
        }

        // Firearms. The flash grows with the damage of one shot and spreads with
        // the weapon's own cone, which is what separates a rifle from a shotgun
        // without either of them being special-cased.
        float bite = Mathf.Clamp(weapon.BaseDamage / 34.0f, 0.25f, 1.0f);
        float spread = Mathf.DegToRad(weapon.BaseSpreadDegrees);

        switch (weapon.Trait)
        {
            // Eight pellets across twenty degrees. Drawn as a fan of small puffs
            // rather than one large one, because the *width* is the weapon: a
            // single wide flash reads as a bigger rifle, and a fan reads as a
            // shotgun before the player has looked at the numbers.
            case WeaponTrait.Spread:
            {
                int fingers = Mathf.Clamp(weapon.TraitCount, 3, 7);
                for (int i = 0; i < fingers; i++)
                {
                    float across = fingers == 1 ? 0.0f : (i / (float)(fingers - 1) - 0.5f) * 2.0f;
                    Vector2 fan = direction.Rotated(across * spread);

                    _pool.Spawn(at + new Vector3(0.0f, 0.18f, 0.0f), 0.34f * bite, 0.9f * bite,
                                new Color(1.0f, 0.62f, 0.22f, 0.55f), 0.10f, fan * 7.0f);
                }

                Kick(0.55f);
                break;
            }

            // One round, a long way, through several bodies. A thin white lance
            // laid down the shot rather than a bloom at the muzzle — the reach is
            // the weapon, so the effect should be long rather than bright.
            case WeaponTrait.Charge:
            {
                for (int i = 1; i <= 4; i++)
                {
                    _pool.Spawn(
                        at + new Vector3(direction.X, 0.0f, direction.Y) * (i * 1.6f)
                           + new Vector3(0.0f, 0.20f, 0.0f),
                        0.16f, 0.02f, new Color(0.94f, 0.96f, 1.0f, 0.5f), 0.09f, Vector2.Zero);
                }

                Kick(0.48f);
                break;
            }

            // A launched charge. Slow, heavy, and it leaves smoke — the only
            // firearm whose shot is worth watching travel.
            case WeaponTrait.Blast:
                _pool.Spawn(at + new Vector3(0.0f, 0.20f, 0.0f), 0.7f, 1.5f,
                            new Color(1.0f, 0.55f, 0.20f, 0.6f), 0.16f, direction * 2.0f);
                _pool.Spawn(at + new Vector3(0.0f, 0.28f, 0.0f), 0.5f, 1.9f,
                            new Color(0.42f, 0.40f, 0.38f, 0.35f), 0.55f, direction * 1.0f);
                Kick(0.60f);
                break;

            // Everything else, including the automatics. Small and quick,
            // because it happens six or seven times a second and anything larger
            // is a strobe rather than a gun.
            default:
                _pool.Spawn(at + new Vector3(0.0f, 0.15f, 0.0f), 0.5f * bite, 0.12f,
                            Muzzle, 0.075f, Vector2.Zero);
                Kick(0.16f * bite);
                break;
        }
    }

    /// A shove on the camera, per shot.
    ///
    /// Weight is the one thing a still image cannot carry, and it is most of why
    /// a slow heavy weapon feels slow and heavy.
    ///
    /// Through `CameraRig.Kick` rather than `Shake`. The first version used shake,
    /// which is tuned for impacts at a fade of six per second: the amounts a
    /// per-shot recoil wants are around 0.1, and 0.1 through that channel lasts a
    /// fifth of a tick and moves the camera a third of a millimetre. Every weapon
    /// measured a kick of exactly zero.
    ///
    /// The values accumulate, so an automatic at seven shots a second is not
    /// seven times a shotgun at one and a half. `RecoilFade` is nine per second,
    /// which puts a repeating 0.12 at a steady 0.09 — four centimetres of push,
    /// a buzz — against a shotgun's single 0.55, which is a twenty-seven
    /// centimetre punch that then lets go.
    ///
    /// Melee gets the least of anything: a swing that moved the camera would move
    /// it on every miss, and the hit is where a melee weapon should land.
    private void Kick(float amount)
    {
        _rig ??= GetParent()?.GetNodeOrNull<CameraRig>("CameraRig");
        _rig?.Kick(amount);
    }

    private CameraRig? _rig;

    /// The puffs, for a probe that needs to know what a shot emitted. The game
    /// never reaches in here.
    public EffectPool Effects => _pool;

    private void OnHit(Vector3 where, WeaponCategory category, float damage)
    {
        if (_clock - _lastImpact < ImpactInterval)
            return;

        _lastImpact = _clock;
        // Scaled by what actually landed. A knife tick and a thirty-four damage
        // marksman round used to throw the same flare, so the one piece of
        // feedback that could have said "that connected properly" said nothing at
        // all — and a chain jump, which does a fraction of the damage, announced
        // itself as loudly as the shot that caused it.
        float weight = Mathf.Clamp(damage / 30.0f, 0.35f, 1.6f);

        _pool.Spawn(where + new Vector3(0.0f, 0.9f, 0.0f),
                    0.4f * weight, 0.95f * weight, Spark, 0.13f, Scatter(2.0f * weight));

        // Melee lands with a thump the camera can feel. Ranged does not: the
        // player is metres away and a screen that jumped on every rifle hit would
        // never stop moving.
        if (category is WeaponCategory.MeleeShort or WeaponCategory.MeleeLong)
            Kick(Mathf.Min(0.22f, 0.018f * damage));
    }

    /// A kill is the one event the player is trying to cause, so it is the one
    /// that has to be unmistakable. Two puffs: a bright one that is gone almost
    /// at once, and a slower dark one that lingers where the body was.
    private void OnEnemyKilled(int type, Vector3 position)
    {
        Vector3 at = position + new Vector3(0.0f, 0.8f, 0.0f);
        _pool.Spawn(at, 0.7f, 1.8f, Gore, 0.24f, Scatter(1.4f));
        _pool.Spawn(at, 0.55f, 1.5f, Smoke, 0.5f, Scatter(0.6f));
    }

    private void OnExploded(Vector3 position)
    {
        Vector3 at = position + new Vector3(0.0f, 0.6f, 0.0f);
        _pool.Spawn(at, 1.0f, 3.4f, Blast, 0.28f, Vector2.Zero);
        _pool.Spawn(at, 0.9f, 4.2f, Smoke, 0.85f, Vector2.Zero);

        // Thrown outward, so the blast has a direction rather than being a disc
        // that appears and disappears.
        for (int i = 0; i < 6; i++)
        {
            float angle = NextFloat() * Mathf.Tau;
            var out2 = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            _pool.Spawn(at, 0.55f, 1.3f, Blast, 0.32f, out2 * (5.0f + NextFloat() * 5.0f));
        }
    }

    /// Burning ground has no event of its own — it is a patch that exists rather
    /// than a moment that happens — so the count going up is the moment, and
    /// after that it is fed from the field every frame.
    private void StepHazards()
    {
        if (_horde == null)
            return;

        _hazardCount = _horde.Hazards.Count;

        for (int i = 0; i < _horde.Hazards.Count; i++)
        {
            // A few licks per patch per frame at a low rate, which is enough for
            // fire once they overlap and cheap enough to not think about.
            if (NextFloat() > 0.35f)
                continue;

            float angle = NextFloat() * Mathf.Tau;
            float radius = _horde.Hazards.Radius[i] * Mathf.Sqrt(NextFloat());
            Vector3 at = _horde.Hazards.Position[i]
                       + new Vector3(Mathf.Cos(angle) * radius, 0.2f, Mathf.Sin(angle) * radius);

            _pool.Spawn(at, 0.7f, 0.18f, new Color(1.0f, 0.52f, 0.18f, 0.35f), 0.32f, Vector2.Zero);
        }
    }

    /// Uploads the whole buffer in one assignment, like every other renderer here.
    private void Sync()
    {
        if (_multi == null)
            return;

        for (int i = 0; i < _pool.Count; i++)
        {
            float age = _pool.Age(i);
            float size = Mathf.Lerp(_pool.StartSize[i], _pool.EndSize[i], age);
            Vector3 p = _pool.Position[i];
            int b = i * FloatsPerInstance;

            // Scaled identity basis. The shader rebuilds the orientation from the
            // camera and reads the size back off column 0, so the basis carries
            // size and nothing else.
            _buffer[b + 0] = size; _buffer[b + 1] = 0.0f; _buffer[b + 2] = 0.0f; _buffer[b + 3] = p.X;
            _buffer[b + 4] = 0.0f; _buffer[b + 5] = size; _buffer[b + 6] = 0.0f; _buffer[b + 7] = p.Y;
            _buffer[b + 8] = 0.0f; _buffer[b + 9] = 0.0f; _buffer[b + 10] = size; _buffer[b + 11] = p.Z;

            // Fades out on a curve rather than linearly: a linear fade spends
            // half its life at half brightness, which reads as a lingering smudge
            // where a flash should already be gone.
            Color tint = _pool.Tint[i];
            float fade = (1.0f - age) * (1.0f - age);

            _buffer[b + 12] = tint.R;
            _buffer[b + 13] = tint.G;
            _buffer[b + 14] = tint.B;
            _buffer[b + 15] = tint.A * fade;
        }

        _multi.Buffer = _buffer;
        _multi.VisibleInstanceCount = _pool.Count;
    }

    /// A soft radial blob, built once at startup.
    ///
    /// Generated rather than authored for the same reason the audio is: it is one
    /// falloff curve, and a curve is easier to re-tune in code than to redraw.
    private static ImageTexture Puff()
    {
        const int size = 64;
        var image = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f) / size - 0.5f;
                float dy = (y + 0.5f) / size - 0.5f;
                float d = Mathf.Sqrt(dx * dx + dy * dy) * 2.0f;

                // Bright core, soft shoulder. A plain linear falloff reads as a
                // fuzzy circle; the extra power on the core is what makes it a
                // flash.
                float a = Mathf.Clamp(1.0f - d, 0.0f, 1.0f);
                a = a * a * (0.35f + 0.65f * a);

                image.SetPixel(x, y, new Color(1.0f, 1.0f, 1.0f, a));
            }
        }

        return ImageTexture.CreateFromImage(image);
    }

    private Vector2 Scatter(float speed) =>
        new Vector2(NextFloat() - 0.5f, NextFloat() - 0.5f).Normalized() * speed * NextFloat();

    private float NextFloat()
    {
        _rng ^= _rng << 13;
        _rng ^= _rng >> 7;
        _rng ^= _rng << 17;
        return (_rng >> 40) / 16777216.0f;
    }
}
