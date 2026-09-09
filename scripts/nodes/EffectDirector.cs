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
/// crowd. Three MultiMeshes, three materials, two fixed pools, and a minimum gap
/// on the effects that can arrive a dozen times a second.
///
/// **Three, because a blend mode is not a per-instance property.** Additive is
/// right for anything that emits and cannot express anything that occludes, and
/// the difference only became visible when the game moved to daylight: smoke
/// drawn additively over a ground at 0.5 lightens it, so every plume in the game
/// was haze. The puffs are split across an additive pass and a blending one from
/// a single pool; the third is the marks left on the floor, which are not
/// billboarded at all.
public partial class EffectDirector : Node3D
{
    [Export] public int Capacity { get; set; } = 224;

    /// How many stains the floor holds at once.
    ///
    /// Deliberately low. A three-hundred-second run kills several hundred things,
    /// and a mark per kill with a generous lifetime paves the arena — at which
    /// point the floor is a texture again and the marks have said nothing. Ninety
    /// six at eight seconds is about eleven a second before the oldest starts
    /// going, which is roughly the rate a good fight actually kills at.
    [Export] public int MarkCapacity { get; set; } = 96;

    /// Shortest gap between two impact puffs, in seconds. A wide melee arc lands
    /// five hits in one frame; five overlapping flashes at the same instant are
    /// one bright blob that says nothing about how many.
    [Export] public float ImpactInterval { get; set; } = 0.035f;

    /// Shortest gap between two blood marks. Kills arrive in bursts — a blast
    /// takes six at once — and six stains inside one metre is a puddle rather
    /// than six deaths.
    [Export] public float MarkInterval { get; set; } = 0.12f;

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

    /// The spatter a kill throws, on the blending channel.
    ///
    /// **A kill used to be four additive puffs and it went white.** Additive over
    /// a daylit field saturates, so the horde's green survives only in the brief
    /// bright core and everything around it climbs toward the paper colour of the
    /// sky — twenty kills in a second rendered as a field of pale discs that read
    /// as lens dirt rather than as bodies coming apart. One bright pop stays
    /// additive because a kill has to *flash*; the matter that leaves the body
    /// blends, so it is green, and so it is visibly thrown rather than glowing.
    private static readonly Color Spatter = new(0.05f, 0.09f, 0.02f, 0.55f);
    private static readonly Color Blast = new(1.0f, 0.58f, 0.22f, 0.5f);

    /// Smoke is on the blending channel, so its colour is a colour rather than an
    /// amount to add — and that, not darkness, is what the channel buys.
    ///
    /// **The first reason written down for splitting the channels was wrong, and
    /// measuring it is what said so.** The claim was that smoke has to be darker
    /// than the daylit ground. It does not, and at these values it cannot be:
    /// instance colours are *linear*, and this arena's grass measures 0.058 linear
    /// (0.27 as a pixel). Anything a person would type as "dark grey" is several
    /// times brighter than the floor it is drawn over, so a plume that reads as
    /// smoke here is a pale one — which is what powder smoke looks like anyway.
    ///
    /// What blending fixes is the ceiling. Additive has none: a pale plume over
    /// the bright half of the frame climbs to white and reads as lens flare, which
    /// is the haze this whole split was opened to remove. Blended, the same plume
    /// converges *to this colour* — it lightens a dark floor and darkens a bright
    /// sky, and it occludes what is behind it either way.
    private static readonly Color Smoke = new(0.20f, 0.19f, 0.18f, 0.44f);

    /// What a body leaves. Green rather than red: the horde is infected and its
    /// own colour language is the sick green of `Gore`, and a bright red pool is
    /// the one thing on screen that would read as a different game.
    ///
    /// **Both of these had to go an order of magnitude darker than they read on
    /// the page, and the reason is the same one the vertex colours have.** These
    /// are linear values. The grass they lie on measures 0.058 linear — a pixel of
    /// 0.27 — so a stain authored at 0.20, which anybody would call dark brown,
    /// blended over it and came out *brighter* and warmer: a worn patch of bare
    /// earth, indistinguishable from the tan tiles the ground shader already
    /// draws. Measured against the same frame with nothing staged it lifted the
    /// floor from (68, 86, 58) to (86, 109, 62), which is the opposite of a
    /// stain.
    ///
    /// Under 0.03 the arithmetic goes the other way and both do what they are
    /// named. The hue is what keeps them apart from each other and from the
    /// arena: the splat stays inside the horde's green, the scorch has no hue at
    /// all.
    private static readonly Color Stain = new(0.020f, 0.028f, 0.010f, 0.72f);
    private static readonly Color Scorch = new(0.006f, 0.006f, 0.006f, 0.88f);

    // The kit cards, in colours nothing else on screen uses.
    //
    // Every other effect here is a warm firearm colour — muzzle, spark, blast —
    // because they all come from shooting. The cards are things the *player*
    // bought and the player is blue, so they answer in the cold half of the
    // wheel. That separation is doing real work: in a crowded frame the question
    // "was that my card or my gun" has to be answerable without reading a number.
    private static readonly Color PulseTint = new(0.62f, 0.86f, 1.0f, 0.55f);
    private static readonly Color ChainTint = new(0.70f, 0.92f, 1.0f, 0.75f);

    /// A crit answers in white, on the one shape nothing else uses at impact.
    ///
    /// Crit was a card the player could buy and never see. It multiplies damage,
    /// and damage already scales the impact puff — so the only evidence of the
    /// twelve percent was that a target occasionally died a shot early, which is
    /// indistinguishable from having aimed at a weaker one. The roll happens once
    /// per attack, so the flash is per attack too and a wide swing that crits
    /// says so once rather than five times.
    private static readonly Color CritTint = new(1.0f, 0.98f, 0.90f, 0.95f);

    private EffectPool _pool = null!;
    private MarkField _marks = null!;

    private MultiMesh _add = null!;
    private MultiMesh _mix = null!;
    private MultiMesh _markMesh = null!;

    private float[] _addBuffer = System.Array.Empty<float>();
    private float[] _mixBuffer = System.Array.Empty<float>();
    private float[] _markBuffer = System.Array.Empty<float>();

    private Horde? _horde;
    private Player? _player;
    private WeaponHandler? _weapons;

    private float _clock;
    private float _lastImpact = float.NegativeInfinity;
    private float _lastMark = float.NegativeInfinity;
    private int _hazardCount;
    private ulong _rng = 0x9E3779B97F4A7C15UL;

    /// Where the burning-body scan resumes.
    ///
    /// A molotov can leave forty things alight at once and the licks are a few
    /// per body per second, so the scan is a rotating window rather than a pass
    /// over the whole pool — the cost of the effect must not scale with the
    /// crowd, which is the rule the rest of this file is built on.
    private int _burnCursor;

    /// 12 floats of transform, 4 of colour, 4 of custom data.
    ///
    /// The colour block carries the tint and the fade; the custom data carries
    /// the shape and the spin. Both are why this is a separate renderer from the
    /// horde's rather than another layer of it.
    private const int FloatsPerInstance = 20;

    public override void _Ready()
    {
        _pool = new EffectPool(Capacity);
        _marks = new MarkField(MarkCapacity);

        _addBuffer = new float[Capacity * FloatsPerInstance];
        _mixBuffer = new float[Capacity * FloatsPerInstance];
        _markBuffer = new float[MarkCapacity * FloatsPerInstance];

        var additive = GD.Load<Shader>("res://assets/shaders/effect.gdshader");
        var soft = GD.Load<Shader>("res://assets/shaders/effect_soft.gdshader");
        var mark = GD.Load<Shader>("res://assets/shaders/ground_stain.gdshader");

        if (additive == null || soft == null || mark == null)
        {
            GD.PushWarning("EffectDirector: missing an effect shader — the run will be silent to look at");
            return;
        }

        Texture2DArray shapes = Shapes();
        _add = BuildPuffMesh("Puffs", additive, shapes);
        _mix = BuildPuffMesh("Smoke", soft, shapes);

        var markMaterial = new ShaderMaterial { Shader = mark };
        markMaterial.SetShaderParameter("marks", Stains());

        _markMesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = true,
            UseCustomData = true,
            Mesh = new QuadMesh { Size = Vector2.One, Material = markMaterial },
            InstanceCount = MarkCapacity,
            VisibleInstanceCount = 0,
        };

        AddChild(new MultiMeshInstance3D
        {
            Name = "Marks",
            Multimesh = _markMesh,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            CustomAabb = new Aabb(new Vector3(-70.0f, -4.0f, -70.0f), new Vector3(140.0f, 12.0f, 140.0f)),
        });

        Node? root = GetParent();
        _horde = root?.GetNodeOrNull<Horde>("Horde");
        _player = root?.GetNodeOrNull<Player>("Player");
        _weapons = _player?.GetNodeOrNull<WeaponHandler>("WeaponHandler");

        if (_horde != null)
        {
            // `KillDetail` rather than `EnemyKilled`, because the shove that
            // killed the body is the direction the body should come apart in.
            // Both events report the same kill; this one reports what it was.
            _horde.KillDetail += OnEnemyKilled;
            _horde.Exploded += OnExploded;
            _hazardCount = _horde.Hazards.Count;
        }

        if (_weapons != null)
        {
            _weapons.Fired += OnFired;
            _weapons.Hit += OnHit;
            _weapons.Chained += OnChained;
        }

        // The shockwave, which has been invisible since the day it shipped.
        //
        // `RunKit.Pulsed` was declared, invoked, and documented as "an event
        // rather than the kit drawing it, because the effect director owns every
        // particle in the game" — and nothing ever subscribed. The card damaged,
        // knocked back, and produced no light at all. Everything about it was
        // correct except that the last line was never written, which is why it
        // read as a card that does nothing.
        //
        // From the scene root, not from the player: `RunKit` is a sibling. The
        // first version of this line asked the player for it, got null, and
        // would have left the shockwave exactly as invisible as it was before —
        // with a warning nobody had a reason to read yet.
        _kit = root?.GetNodeOrNull<RunKit>("RunKit");
        if (_kit != null)
            _kit.Pulsed += OnPulsed;
        else
            GD.PushWarning("EffectDirector: no RunKit — the shockwave will be invisible");
    }

    /// One of the two puff passes. They differ only in the shader, which is the
    /// point: same mesh, same texture array, same buffer layout, so which channel
    /// a puff lands on costs nothing but a branch at upload.
    private MultiMesh BuildPuffMesh(string name, Shader shader, Texture2DArray shapes)
    {
        var material = new ShaderMaterial { Shader = shader };
        material.SetShaderParameter("shapes", shapes);

        var multi = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = true,
            UseCustomData = true,
            Mesh = new QuadMesh { Size = Vector2.One, Material = material },
            InstanceCount = Capacity,
            VisibleInstanceCount = 0,
        };

        AddChild(new MultiMeshInstance3D
        {
            Name = name,
            Multimesh = multi,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,

            // Same trap as the horde: instances are scattered across the arena
            // while the mesh's own bounds are one quad, so without a custom AABB
            // the renderer culls every effect the moment the origin leaves frame.
            CustomAabb = new Aabb(new Vector3(-70.0f, -2.0f, -70.0f), new Vector3(140.0f, 20.0f, 140.0f)),
        });

        return multi;
    }

    private RunKit? _kit;

    /// The horde's and the weapon's events are plain C# delegates, so they hold a
    /// strong reference to this node — a subscription that outlives the scene is
    /// a call into a freed object.
    public override void _ExitTree()
    {
        if (_horde != null)
        {
            _horde.KillDetail -= OnEnemyKilled;
            _horde.Exploded -= OnExploded;
        }

        if (_weapons != null)
        {
            _weapons.Fired -= OnFired;
            _weapons.Hit -= OnHit;
            _weapons.Chained -= OnChained;
        }

        if (_kit != null)
            _kit.Pulsed -= OnPulsed;
    }

    public override void _Process(double delta)
    {
        float step = (float)delta;
        _clock += step;

        StepHazards();
        StepBurning();
        StepTrails();
        _pool.Step(step);
        _marks.Step(step);
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
            SwingArc(origin, direction, weapon);
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

        // Radians. Rolled per shot so the star is never twice at the same angle:
        // a fixed spike direction is what turns a flash into a sticker.
        float roll = NextFloat() * Mathf.Tau;

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

                // One star over the fan, so the shotgun still reads as a gunshot
                // and not only as a spray. Wide and brief.
                _pool.Spawn(at + new Vector3(0.0f, 0.22f, 0.0f), 1.9f, 1.1f,
                            new Color(1.0f, 0.72f, 0.34f, 0.85f), 0.055f, Vector2.Zero,
                            EffectShape.Flash, spin: roll);
                Breath(at, direction, 1.1f);

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

                _pool.Spawn(at + new Vector3(0.0f, 0.20f, 0.0f), 1.3f, 0.8f,
                            new Color(0.94f, 0.96f, 1.0f, 0.8f), 0.05f, Vector2.Zero,
                            EffectShape.Flash, spin: roll);
                Breath(at, direction, 0.7f);

                Kick(0.48f);
                break;
            }

            // A launched charge. Slow, heavy, and it leaves smoke — the only
            // firearm whose shot is worth watching travel.
            case WeaponTrait.Blast:
                _pool.Spawn(at + new Vector3(0.0f, 0.20f, 0.0f), 0.7f, 1.5f,
                            new Color(1.0f, 0.55f, 0.20f, 0.6f), 0.16f, direction * 2.0f);
                _pool.Spawn(at + new Vector3(0.0f, 0.20f, 0.0f), 1.7f, 1.0f,
                            new Color(1.0f, 0.66f, 0.28f, 0.9f), 0.06f, Vector2.Zero,
                            EffectShape.Flash, spin: roll);
                _pool.Spawn(at + new Vector3(0.0f, 0.28f, 0.0f), 0.6f, 2.1f,
                            Smoke, 0.75f, direction * 1.0f, EffectShape.Smoke, soft: true);
                Kick(0.60f);
                break;

            // Everything else, including the automatics. Small and quick,
            // because it happens six or seven times a second and anything larger
            // is a strobe rather than a gun.
            //
            // **The star is what made this readable at all.** Sized off the
            // damage the old ball was a fifteen-centimetre speck — nine pixels,
            // which is under the width of the HUD's own font — and growing it
            // enough to see turned it into a floating orange disc, because a soft
            // radial ball has no shape to grow into. A four-pointed flash is
            // mostly transparent, so it can be a metre across and still only put
            // a small bright core on the screen. It also lasts four frames rather
            // than five: shape buys legibility that duration used to have to.
            default:
                _pool.Spawn(at + new Vector3(0.0f, 0.15f, 0.0f), 0.85f + 0.75f * bite, 0.55f,
                            Muzzle, 0.06f, Vector2.Zero, EffectShape.Flash, spin: roll);
                Breath(at, direction, 0.35f + 0.5f * bite);
                Kick(0.16f * bite);
                break;
        }
    }

    /// The wisp a barrel leaves behind, on the blending channel.
    ///
    /// It is the half of a gunshot that is not light, and there was no way to
    /// draw it before: additive grey over a daylit field is a brightening, so
    /// every attempt at powder smoke came out as a pale bloom that made the flash
    /// look bigger rather than dirtier. Kept small and short — this is the thing
    /// that happens seven times a second, and smoke that outstays a magazine is
    /// a fog bank in front of the player.
    private void Breath(Vector3 at, Vector2 direction, float weight)
    {
        _pool.Spawn(at + new Vector3(0.0f, 0.22f, 0.0f), 0.35f * weight, 1.15f * weight,
                    new Color(Smoke.R, Smoke.G, Smoke.B, 0.20f), 0.34f,
                    direction * (1.6f * weight), EffectShape.Smoke, soft: true);
    }

    /// A swing, drawn as the arc it actually covers.
    ///
    /// The old version was one smear laid down the facing plus a second one for
    /// cleave, which said "something happened in front of you" and nothing about
    /// *where*. A melee weapon's whole identity is two numbers — reach and sweep
    /// — and neither of them was on screen: a knife at 1.6 m over 90° and a
    /// scythe at 3.4 m over 200° drew the same streak at different lengths.
    ///
    /// Stepped along the arc at the weapon's own radius, from one edge of the
    /// sweep to the other, with the puffs trailing outward. The count follows the
    /// angle so a wide sweep is a longer arc rather than the same arc drawn
    /// thinner, and each puff lives a hair longer than the one before it, which
    /// is what makes a static line read as a blade travelling.
    private void SwingArc(Vector3 origin, Vector2 direction, WeaponResource weapon)
    {
        float reach = Mathf.Max(1.0f, weapon.BaseRange);
        float half = Mathf.DegToRad(Mathf.Max(20.0f, weapon.SwingArcDegrees) * 0.5f);
        int steps = Mathf.Clamp(Mathf.RoundToInt(half * 7.0f), 4, 11);

        Vector3 at = new(origin.X, 0.0f, origin.Z);

        for (int i = 0; i <= steps; i++)
        {
            float t = i / (float)steps;
            Vector2 spoke = direction.Rotated(Mathf.Lerp(-half, half, t));
            Vector3 on = at + new Vector3(spoke.X, 0.0f, spoke.Y) * (reach * 0.72f);

            // Brightest in the middle of the sweep and dim at both ends, so the
            // arc has a leading edge rather than being a fence of equal marks.
            float weight = 0.45f + 0.55f * Mathf.Sin(t * Mathf.Pi);

            _pool.Spawn(on + new Vector3(0.0f, 0.55f, 0.0f), reach * 0.20f, reach * 0.30f,
                        new Color(0.84f, 0.90f, 1.0f, 0.30f * weight),
                        0.09f + 0.05f * t, spoke * (reach * 0.8f));
        }

        // Cleave hits everything in the arc, and one pass over the ground cannot
        // say that. A second, slower, warmer sweep behind the first is the trait
        // rather than a brighter version of the same swing.
        if (weapon.Trait == WeaponTrait.Cleave)
        {
            for (int i = 0; i <= steps; i++)
            {
                float t = i / (float)steps;
                Vector2 spoke = direction.Rotated(Mathf.Lerp(half, -half, t));
                Vector3 on = at + new Vector3(spoke.X, 0.0f, spoke.Y) * (reach * 0.55f);

                _pool.Spawn(on + new Vector3(0.0f, 0.75f, 0.0f), reach * 0.16f, reach * 0.40f,
                            new Color(0.95f, 0.82f, 0.58f, 0.20f), 0.20f, spoke * (reach * 0.4f));
            }
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

    /// The floor, for the same reason.
    public MarkField Marks => _marks;

    private void OnHit(Vector3 where, WeaponCategory category, float damage, bool crit)
    {
        // A crit is never rate-limited. It is the rarest thing the impact channel
        // has to say and the whole point of a card the player paid for, so it
        // jumps the queue that exists to stop five simultaneous ordinary hits
        // from becoming one blob.
        if (crit)
        {
            _pool.Spawn(where + new Vector3(0.0f, 1.0f, 0.0f), 2.1f, 1.2f, CritTint, 0.10f,
                        Vector2.Zero, EffectShape.Flash, spin: NextFloat() * Mathf.Tau);
            Kick(0.10f);
        }
        else if (_clock - _lastImpact < ImpactInterval)
        {
            return;
        }

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

    /// A ring of puffs on the wave front, drifting outward.
    ///
    /// A ring rather than a burst at the centre, because the radius is the whole
    /// of what the card does: the player has to learn how far it reaches, and a
    /// puff at their feet teaches them nothing. Placed *on* the circumference and
    /// pushed outward, so what is seen is where the damage was.
    ///
    /// The count rises with the radius. A fixed number spreads thinner as the
    /// card is stacked, so the effect would visibly weaken exactly as it got
    /// stronger.
    private void OnPulsed(Vector3 centre, float radius)
    {
        int count = Mathf.Clamp(Mathf.RoundToInt(radius * 4.0f), 12, 28);

        for (int i = 0; i < count; i++)
        {
            float angle = Mathf.Tau * i / count;
            var out2 = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));

            _pool.Spawn(centre + new Vector3(out2.X * radius, 0.35f, out2.Y * radius),
                        0.5f, 1.5f, PulseTint, 0.32f, out2 * 3.4f);
        }
    }

    /// The arc, as a line of puffs from the shot to whatever it jumped to.
    ///
    /// Stepped along the line rather than drawn as one stretched quad, because
    /// the effect pool draws billboarded squares and has no notion of a segment.
    /// Spacing is fixed and the count follows the distance, so a long jump is a
    /// longer line rather than the same line stretched thinner.
    private void OnChained(Vector3 from, Vector3 to)
    {
        Vector3 span = to - from;
        float length = span.Length();
        if (length < 0.05f)
            return;

        int steps = Mathf.Clamp(Mathf.RoundToInt(length / 0.55f), 2, 14);

        for (int i = 0; i <= steps; i++)
        {
            float t = (float)i / steps;

            // Bowed upward in the middle. A dead straight line between two
            // things on a flat plane reads as a rendering artefact; a sag is
            // what makes it look thrown.
            float lift = Mathf.Sin(t * Mathf.Pi) * 0.45f;

            _pool.Spawn(from.Lerp(to, t) + new Vector3(0.0f, 0.5f + lift, 0.0f),
                        0.34f, 0.05f, ChainTint, 0.16f, Vector2.Zero);
        }
    }

    /// A kill is the one event the player is trying to cause, so it is the one
    /// that has to be unmistakable.
    ///
    /// **It comes apart along the shove that killed it.** The burst used to
    /// scatter uniformly, which reads as a body deciding to stop existing — the
    /// shot that did it could have come from anywhere. Thrown along the knockback
    /// instead, the kill points away from the player, and a shotgun at two metres
    /// and a marksman round at thirty look like what they were.
    ///
    /// Three layers, and only two of them are light: a bright spray, a dark
    /// blooming cloud on the blending channel where the body was, and a stain on
    /// the floor that outlives both by eight seconds.
    private void OnEnemyKilled(int type, byte elite, Vector3 position, Vector2 impulse)
    {
        Vector3 at = position + new Vector3(0.0f, 0.8f, 0.0f);

        // Elites are 1.25x bigger and worth four times as much, and until now they
        // died exactly like a walker. Scale is the only channel that reads at
        // fifty bodies — the mark colours deliberately do not vary.
        float weight = elite == 0 ? 1.0f : 1.45f;

        Vector2 throwOut = impulse.LengthSquared() > 0.0001f
            ? impulse.Normalized()
            : Scatter(1.0f).Normalized();

        _pool.Spawn(at, 0.7f * weight, 1.8f * weight, Gore, 0.20f, throwOut * 3.2f);

        for (int i = 0; i < 2; i++)
        {
            // A cone about the shove rather than a line down it: a body thrown
            // by a shot spreads, and puffs in single file read as a second
            // projectile leaving the corpse.
            Vector2 spray = throwOut.Rotated((NextFloat() - 0.5f) * 1.5f);
            _pool.Spawn(at, 0.30f * weight, 0.75f * weight, Spatter, 0.22f + 0.12f * NextFloat(),
                        spray * (2.0f + NextFloat() * 3.5f), EffectShape.Smoke, soft: true);
        }

        _pool.Spawn(at, 0.55f * weight, 1.7f * weight, Smoke, 0.5f, throwOut * 0.6f,
                    EffectShape.Smoke, soft: true);

        if (_clock - _lastMark >= MarkInterval)
        {
            _lastMark = _clock;
            _marks.Spawn(position + new Vector3(throwOut.X * 0.4f, 0.0f, throwOut.Y * 0.4f),
                         (1.1f + NextFloat() * 0.5f) * weight, Stain, 8.0f, MarkShape.Splat,
                         NextFloat() * Mathf.Tau);
        }
    }

    private void OnExploded(Vector3 position)
    {
        Vector3 at = position + new Vector3(0.0f, 0.6f, 0.0f);
        _pool.Spawn(at, 1.0f, 3.4f, Blast, 0.28f, Vector2.Zero);
        _pool.Spawn(at, 2.6f, 1.6f, new Color(1.0f, 0.80f, 0.45f, 0.9f), 0.08f, Vector2.Zero,
                    EffectShape.Flash, spin: NextFloat() * Mathf.Tau);
        _pool.Spawn(at, 0.9f, 4.6f, Smoke, 0.9f, Vector2.Zero, EffectShape.Smoke, soft: true);

        // Thrown outward, so the blast has a direction rather than being a disc
        // that appears and disappears.
        for (int i = 0; i < 6; i++)
        {
            float angle = NextFloat() * Mathf.Tau;
            var out2 = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            _pool.Spawn(at, 0.55f, 1.3f, Blast, 0.32f, out2 * (5.0f + NextFloat() * 5.0f));
        }

        // The one mark that is not rate-limited. A blast is rare, loud and the
        // player's own doing, and the scorch is how they find out afterwards how
        // wide it actually was — which is a number no HUD reports.
        _marks.Spawn(position, 3.4f, Scorch, 11.0f, MarkShape.Scorch, NextFloat() * Mathf.Tau);
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

    /// **Ignite was an unlockable card with no picture.** It is one of the eight
    /// things in this game that cannot be bought — it opens by killing sixty in a
    /// run — and what it bought was a damage-over-time the horde carries in
    /// `Pool.Burn` and nothing drew. A burning enemy walked at you looking exactly
    /// like one that was not, right up until it fell over, so the player had no
    /// way to learn what the card did or to spend it deliberately.
    ///
    /// Deliberately *not* the hit flash: burn applies damage sixty times a second
    /// and `Horde.Damage` takes a `flash` flag precisely so the damage-over-time
    /// path can pass false — a crowd standing in a molotov rendered as a row of
    /// solid white cut-outs when it did not. Fire drawn over the body is what says
    /// burning, which is the same answer the burning ground already gives.
    ///
    /// A rotating window over the pool rather than a pass across it. A molotov can
    /// leave forty things alight and the rest of this file is built on the cost of
    /// an effect not scaling with the crowd.
    private void StepBurning()
    {
        if (_horde == null)
            return;

        int count = _horde.Pool.Count;
        if (count == 0)
            return;

        const int Window = 24;

        for (int n = 0; n < Window && n < count; n++)
        {
            int i = (_burnCursor + n) % count;
            if (_horde.Pool.Burn[i] <= 0.0f || NextFloat() > 0.5f)
                continue;

            Vector3 body = _horde.Pool.Position[i];
            _pool.Spawn(
                new Vector3(body.X + (NextFloat() - 0.5f) * 0.7f,
                            0.35f + NextFloat() * 0.9f,
                            body.Z + (NextFloat() - 0.5f) * 0.7f),
                0.42f, 0.10f, new Color(1.0f, 0.55f, 0.20f, 0.42f), 0.26f, Vector2.Zero);
        }

        _burnCursor = (_burnCursor + Window) % count;
    }

    /// The wake behind a shot that takes time to arrive.
    ///
    /// Travel time is the whole of what separates a projectile weapon from a
    /// hitscan one — a bow's shot has to be *led*, and leading something you can
    /// barely see is guesswork. One small quad crossing thirty metres is a fleck;
    /// a fleck with three metres of fading behind it is a trajectory, and a
    /// trajectory is what the player is actually aiming.
    ///
    /// **Only the shots that are real.** A hitscan tracer is a zero-damage
    /// projectile fired purely to be seen and it already is one; giving it a wake
    /// as well would put a streak behind every round of an automatic at seven a
    /// second, which is a beam weapon rather than a rifle. Zero damage is the
    /// existing marker for "cosmetic" — the collision stage skips on it too — so
    /// this needs no new field.
    ///
    /// Tinted from the projectile rather than from a constant, so the arrow's
    /// wake is the arrow's colour: the shot's own tint was the phase that made
    /// fifteen weapons stop crossing the screen identically, and a trail in one
    /// authored colour would undo half of it.
    private void StepTrails()
    {
        if (_weapons == null)
            return;

        ProjectilePool shots = _weapons.Projectiles;

        for (int i = 0; i < shots.Count; i++)
        {
            if (shots.Damage[i] <= 0.0f || NextFloat() > 0.55f)
                continue;

            Vector3 at = shots.Position[i];
            Color tint = shots.Tint[i];

            _pool.Spawn(new Vector3(at.X, at.Y + 0.45f, at.Z),
                        0.20f * shots.Scale[i], 0.03f,
                        new Color(tint.R, tint.G, tint.B, 0.28f), 0.15f, Vector2.Zero);
        }
    }

    /// Uploads both puff passes and the floor in one assignment each, like every
    /// other renderer here.
    ///
    /// One walk over the pool, writing each puff into whichever of the two buffers
    /// its channel names. Splitting at upload rather than holding two pools keeps
    /// one lifetime, one capacity and one oldest-out rule — the alternative is two
    /// budgets that run out independently, and a firefight that silently stops
    /// emitting smoke because the smoke pool filled.
    private void Sync()
    {
        if (_add == null || _mix == null)
            return;

        int additive = 0;
        int soft = 0;

        for (int i = 0; i < _pool.Count; i++)
        {
            float age = _pool.Age(i);
            float size = Mathf.Lerp(_pool.StartSize[i], _pool.EndSize[i], age);
            Vector3 p = _pool.Position[i];

            // Fades out on a curve rather than linearly: a linear fade spends
            // half its life at half brightness, which reads as a lingering smudge
            // where a flash should already be gone.
            //
            // Smoke fades the other way. It is the one thing here that is not a
            // flash — it should thicken a moment after it is born and then thin
            // out slowly, not be at its heaviest at the instant of the shot — so
            // it eases in rather than falling off a square.
            Color tint = _pool.Tint[i];
            bool wisp = _pool.Shape[i] == (byte)EffectShape.Smoke;
            float fade = wisp
                ? Mathf.Min(1.0f, age * 6.0f) * (1.0f - age)
                : (1.0f - age) * (1.0f - age);

            bool blended = _pool.Soft[i];
            float[] buffer = blended ? _mixBuffer : _addBuffer;
            int b = (blended ? soft++ : additive++) * FloatsPerInstance;

            // Scaled identity basis. The shader rebuilds the orientation from the
            // camera and reads the size back off column 0, so the basis carries
            // size and nothing else.
            buffer[b + 0] = size; buffer[b + 1] = 0.0f; buffer[b + 2] = 0.0f; buffer[b + 3] = p.X;
            buffer[b + 4] = 0.0f; buffer[b + 5] = size; buffer[b + 6] = 0.0f; buffer[b + 7] = p.Y;
            buffer[b + 8] = 0.0f; buffer[b + 9] = 0.0f; buffer[b + 10] = size; buffer[b + 11] = p.Z;

            buffer[b + 12] = tint.R;
            buffer[b + 13] = tint.G;
            buffer[b + 14] = tint.B;
            buffer[b + 15] = tint.A * fade;

            buffer[b + 16] = _pool.Shape[i];
            buffer[b + 17] = _pool.Spin[i];
            buffer[b + 18] = 0.0f;
            buffer[b + 19] = 0.0f;
        }

        _add.Buffer = _addBuffer;
        _add.VisibleInstanceCount = additive;
        _mix.Buffer = _mixBuffer;
        _mix.VisibleInstanceCount = soft;

        SyncMarks();
    }

    private void SyncMarks()
    {
        if (_markMesh == null)
            return;

        for (int i = 0; i < _marks.Count; i++)
        {
            float size = _marks.DrawnSize(i);
            Vector3 p = _marks.Position[i];
            Vector3 up = _marks.Normal[i];

            // The quad lies in its own XY plane facing +Z, so laying it on the
            // ground means putting the ground's normal in column 2 and any two
            // perpendiculars in the others. The cross with world right is safe
            // because the arena's steepest slope is nowhere near vertical.
            Vector3 across = Vector3.Right.Cross(up);
            if (across.LengthSquared() < 0.0001f)
                across = Vector3.Forward.Cross(up);
            across = across.Normalized();
            Vector3 ahead = up.Cross(across);

            float s = Mathf.Sin(_marks.Spin[i]);
            float c = Mathf.Cos(_marks.Spin[i]);
            Vector3 x = (across * c + ahead * s) * size;
            Vector3 y = (ahead * c - across * s) * size;
            Vector3 z = up * size;

            int b = i * FloatsPerInstance;
            _markBuffer[b + 0] = x.X; _markBuffer[b + 1] = y.X; _markBuffer[b + 2] = z.X; _markBuffer[b + 3] = p.X;
            _markBuffer[b + 4] = x.Y; _markBuffer[b + 5] = y.Y; _markBuffer[b + 6] = z.Y; _markBuffer[b + 7] = p.Y;
            _markBuffer[b + 8] = x.Z; _markBuffer[b + 9] = y.Z; _markBuffer[b + 10] = z.Z; _markBuffer[b + 11] = p.Z;

            Color tint = _marks.Tint[i];
            _markBuffer[b + 12] = tint.R;
            _markBuffer[b + 13] = tint.G;
            _markBuffer[b + 14] = tint.B;
            _markBuffer[b + 15] = tint.A * _marks.Opacity(i);

            _markBuffer[b + 16] = _marks.Shape[i];
            _markBuffer[b + 17] = 0.0f;
            _markBuffer[b + 18] = 0.0f;
            _markBuffer[b + 19] = 0.0f;
        }

        _markMesh.Buffer = _markBuffer;
        _markMesh.VisibleInstanceCount = _marks.Count;
    }

    /// The three shapes a puff can be drawn as, built once at startup.
    ///
    /// Generated rather than authored for the same reason the audio is: these are
    /// falloff curves, and a curve is easier to re-tune in code than to redraw.
    /// A `Texture2DArray` rather than an atlas for the same reason the horde uses
    /// one — under linear filtering an atlas bleeds one cell into the next at the
    /// exact moment a puff is smallest.
    private static Texture2DArray Shapes()
    {
        var layers = new Godot.Collections.Array<Image> { Ball(), Star(), Wisp() };
        var array = new Texture2DArray();
        array.CreateFromImages(layers);
        return array;
    }

    private const int ShapeSize = 64;

    private static Image Ball()
    {
        var image = Image.CreateEmpty(ShapeSize, ShapeSize, false, Image.Format.Rgba8);

        for (int y = 0; y < ShapeSize; y++)
        {
            for (int x = 0; x < ShapeSize; x++)
            {
                float d = Radius(x, y, out _);

                // Bright core, soft shoulder. A plain linear falloff reads as a
                // fuzzy circle; the extra power on the core is what makes it a
                // flash.
                float a = Mathf.Clamp(1.0f - d, 0.0f, 1.0f);
                a = a * a * (0.35f + 0.65f * a);

                image.SetPixel(x, y, new Color(1.0f, 1.0f, 1.0f, a));
            }
        }

        return image;
    }

    /// A hot core with four tapering spikes.
    ///
    /// The shape that says "a gun went off", and the reason a muzzle flash can now
    /// be a metre across without becoming a disc: nearly all of the quad is empty,
    /// so the drawn area grows far more slowly than the size does. The core is
    /// tight and near-opaque; the spikes are thin, long and dim, which is what
    /// separates a star from a plus sign.
    private static Image Star()
    {
        var image = Image.CreateEmpty(ShapeSize, ShapeSize, false, Image.Format.Rgba8);

        for (int y = 0; y < ShapeSize; y++)
        {
            for (int x = 0; x < ShapeSize; x++)
            {
                float d = Radius(x, y, out float angle);
                float falloff = Mathf.Clamp(1.0f - d, 0.0f, 1.0f);

                float core = Mathf.Pow(Mathf.Clamp(1.0f - d * 3.4f, 0.0f, 1.0f), 1.6f);

                // Four spikes at 90°, plus four shorter ones between them. The
                // second set is at a third of the length and is what stops the
                // first from reading as a crosshair.
                float long4 = Mathf.Pow(Mathf.Max(0.0f, Mathf.Cos(angle * 4.0f)), 26.0f);
                float short4 = Mathf.Pow(Mathf.Max(0.0f, -Mathf.Cos(angle * 4.0f)), 34.0f) * 0.34f;
                float spikes = (long4 + short4) * falloff * falloff;

                float a = Mathf.Clamp(core + spikes * 0.85f, 0.0f, 1.0f);
                image.SetPixel(x, y, new Color(1.0f, 1.0f, 1.0f, a));
            }
        }

        return image;
    }

    /// A lump with no core.
    ///
    /// Smoke is defined by not being light, so the one thing this must not have is
    /// a bright centre — and it must not be a circle, because a circle at any size
    /// is the tell that a particle system drew it. Three sine lobes push the edge
    /// in and out, which is enough irregularity at the sizes these are seen at.
    private static Image Wisp()
    {
        var image = Image.CreateEmpty(ShapeSize, ShapeSize, false, Image.Format.Rgba8);

        for (int y = 0; y < ShapeSize; y++)
        {
            for (int x = 0; x < ShapeSize; x++)
            {
                float d = Radius(x, y, out float angle);

                float lumpy = d * (1.0f
                    + 0.20f * Mathf.Sin(angle * 3.0f + 1.7f)
                    + 0.13f * Mathf.Sin(angle * 5.0f - 0.6f)
                    + 0.08f * Mathf.Sin(angle * 8.0f + 2.9f));

                // Flat through the middle then a wide soft shoulder: the opposite
                // profile to the ball, which is most of the difference.
                float a = Mathf.Clamp(1.0f - Mathf.SmoothStep(0.25f, 1.0f, lumpy), 0.0f, 1.0f);
                image.SetPixel(x, y, new Color(1.0f, 1.0f, 1.0f, a * 0.92f));
            }
        }

        return image;
    }

    /// The two things that stay on the floor.
    private static Texture2DArray Stains()
    {
        var layers = new Godot.Collections.Array<Image> { Splat(), Scorched() };
        var array = new Texture2DArray();
        array.CreateFromImages(layers);
        return array;
    }

    /// A body's worth, as a ragged blot with four satellites.
    ///
    /// The satellites are the whole reason this is not the smoke shape flattened:
    /// one blob on the ground reads as a shadow, and a blob with spatter around it
    /// reads as something having hit it hard.
    private static Image Splat()
    {
        var image = Image.CreateEmpty(ShapeSize, ShapeSize, false, Image.Format.Rgba8);

        for (int y = 0; y < ShapeSize; y++)
        {
            for (int x = 0; x < ShapeSize; x++)
            {
                float d = Radius(x, y, out float angle);

                float lumpy = d * (1.0f
                    + 0.26f * Mathf.Sin(angle * 3.0f + 0.9f)
                    + 0.16f * Mathf.Sin(angle * 7.0f - 2.1f));

                float body = 1.0f - Mathf.SmoothStep(0.34f, 0.62f, lumpy);

                // Four droplets thrown clear of the main blot, at a radius the
                // body never reaches.
                float drops = 0.0f;
                for (int n = 0; n < 4; n++)
                {
                    float a = 1.1f + n * 1.63f;
                    float r = 0.68f + 0.12f * ((n * 7) % 3);
                    float dx = (x + 0.5f) / ShapeSize - 0.5f - Mathf.Cos(a) * r * 0.5f;
                    float dy = (y + 0.5f) / ShapeSize - 0.5f - Mathf.Sin(a) * r * 0.5f;
                    float dd = Mathf.Sqrt(dx * dx + dy * dy) * 2.0f;
                    drops = Mathf.Max(drops, 1.0f - Mathf.SmoothStep(0.04f, 0.13f, dd));
                }

                image.SetPixel(x, y, new Color(1.0f, 1.0f, 1.0f,
                                               Mathf.Clamp(body + drops * 0.75f, 0.0f, 1.0f)));
            }
        }

        return image;
    }

    /// A blast's footprint: dark at the seat, thinning to a ragged edge.
    private static Image Scorched()
    {
        var image = Image.CreateEmpty(ShapeSize, ShapeSize, false, Image.Format.Rgba8);

        for (int y = 0; y < ShapeSize; y++)
        {
            for (int x = 0; x < ShapeSize; x++)
            {
                float d = Radius(x, y, out float angle);

                float ragged = d * (1.0f
                    + 0.10f * Mathf.Sin(angle * 6.0f + 0.4f)
                    + 0.07f * Mathf.Sin(angle * 11.0f - 1.9f));

                float a = 1.0f - Mathf.SmoothStep(0.10f, 0.98f, ragged);
                image.SetPixel(x, y, new Color(1.0f, 1.0f, 1.0f, a * a * 0.95f));
            }
        }

        return image;
    }

    /// Distance from the centre of the texture, 0 at the middle and 1 at the edge
    /// of the inscribed circle, with the angle out as a by-product. Every shape
    /// here is polar; only the profile differs.
    private static float Radius(int x, int y, out float angle)
    {
        float dx = (x + 0.5f) / ShapeSize - 0.5f;
        float dy = (y + 0.5f) / ShapeSize - 0.5f;
        angle = Mathf.Atan2(dy, dx);
        return Mathf.Sqrt(dx * dx + dy * dy) * 2.0f;
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
