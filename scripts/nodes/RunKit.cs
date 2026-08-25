using Godot;

/// The things a run picks up that fight on their own.
///
/// The growth deck had two kinds of card — a bigger number, or a rule about how
/// the weapon resolves — and both of them were still the weapon. Every run
/// therefore ended the same shape: a rifle with adjectives. These fight without
/// being aimed, so a build can be about something the weapon does not do, and a
/// dry magazine stops being the end of the run.
///
/// **Beside the player in the tree, not under it.** A child inherits the body's
/// yaw, and the player's body turns to face the view — so a parented ring would
/// spin with the character and the blades would appear to stand still relative
/// to whatever the player was looking at. The ring has to be in world space; it
/// follows the player's position and ignores their rotation.
public partial class RunKit : Node3D
{
    // --- orbit --------------------------------------------------------------

    /// How far out the blades circle.
    ///
    /// One and a half metres, and the number is the whole card. Enemies stop at
    /// the horde's 0.7 m contact radius to bite, so at 2.3 m a ring with a 0.7 m
    /// bite sweeps ground a walker crosses once on the way in and then never
    /// occupies — it would hit each enemy exactly once, on approach, and then do
    /// nothing while they ate the player. At 1.5 the blades sweep where the crowd
    /// actually stands.
    [Export] public float OrbitRadius { get; set; } = 1.5f;

    /// How thick the ring is, in metres either side of the radius.
    ///
    /// A thickness rather than a proximity: the blades sweep an arc between one
    /// damage tick and the next, so what matters is whether an enemy is inside
    /// the band the ring occupies, not how close it happened to be to a blade at
    /// the instant the damage was sampled.
    [Export] public float OrbitBite { get; set; } = 1.0f;

    [Export] public float OrbitDamage { get; set; } = 7.0f;

    /// Seconds between damage ticks. The ring is continuous; its damage is not,
    /// or standing in a crowd would be a per-frame kill rate that changes with
    /// the tick rate.
    [Export] public float OrbitInterval { get; set; } = 0.3f;

    [Export] public float OrbitSpin { get; set; } = 3.6f;

    /// What a pass leaves behind, per second and for how long.
    ///
    /// 2.5 for 2 s against the Combat Knife's 4 for 3. Deliberately below it: the
    /// knife is the whole of a Sidearm slot and the blades are one card among
    /// twenty-two, so a ring that bled harder than the weapon would make the
    /// weapon the worse way to do the thing it exists for.
    [Export] public float OrbitBleed { get; set; } = 2.5f;
    [Export] public float OrbitBleedSeconds { get; set; } = 2.0f;

    /// Ceiling on drawn blades. Five is the growth cap; the extra three are for
    /// gear that adds them.
    [Export] public int MaxBlades { get; set; } = 8;

    // --- shockwave ----------------------------------------------------------

    [Export] public float PulseInterval { get; set; } = 5.0f;
    [Export] public float PulseIntervalPerStack { get; set; } = 0.45f;
    [Export] public float PulseIntervalFloor { get; set; } = 0.8f;
    [Export] public float PulseRadius { get; set; } = 5.0f;
    [Export] public float PulseRadiusPerStack { get; set; } = 0.5f;
    [Export] public float PulseDamage { get; set; } = 20.0f;
    [Export] public float PulseDamagePerStack { get; set; } = 6.0f;
    [Export] public float PulseKnockback { get; set; } = 1.5f;

    private Player? _player;
    private Horde? _horde;
    private MultiMeshInstance3D? _blades;
    private MultiMesh? _bladeMesh;

    private float _spin;
    private float _sinceBite;
    private float _sincePulse;
    private int[] _touched = System.Array.Empty<int>();

    public override void _Ready()
    {
        _player = GetParent()?.GetNodeOrNull<Player>("Player");
        _horde = GetParent()?.GetNodeOrNull<Horde>("Horde");
        _touched = new int[256];

        BuildBlades();
        BuildFrost();
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_player is not { IsAlive: true } || _horde == null)
        {
            if (_blades != null)
                _blades.Visible = false;

            return;
        }

        var step = (float)delta;
        GlobalPosition = new Vector3(_player.GlobalPosition.X, 0.0f, _player.GlobalPosition.Z);

        StepOrbit(step);
        StepShockwave(step);
        StepFrost();
    }

    // ------------------------------------------------------------------------

    private void StepOrbit(float step)
    {
        int blades = Mathf.Clamp(_player!.Mods.OrbitBlades, 0, MaxBlades);

        if (_blades != null)
            _blades.Visible = blades > 0;

        if (blades == 0)
            return;

        // Not wrapped. The swept arc below is a difference between two spins,
        // and wrapping makes that difference negative once a revolution — a
        // single tick where the ring hits nothing, once every 1.7 seconds, which
        // is precisely the kind of intermittent fault nobody can reproduce.
        _spin += OrbitSpin * step;
        DrawBlades(blades);

        // Damage on an interval, not per frame. A continuous ring that dealt
        // damage every tick would kill at a rate set by the physics rate, which
        // is a number the player cannot see and did not choose.
        _sinceBite += step;
        if (_sinceBite < OrbitInterval)
            return;

        float swept = Mathf.Min(_spin - _lastBite, Mathf.Tau);
        if (swept < 0.0f)
            swept += Mathf.Tau;   // the spin wrapped

        // Captured before _lastBite moves. The arc runs from where the blades
        // were to where they are, and reading the field after updating it would
        // make every arc start at its own end — zero width, nothing ever hit.
        float from = _lastBite;

        _sinceBite = 0.0f;
        _lastBite = _spin;

        // Everything in the annulus the ring sweeps, then which of them a blade
        // actually passed through since the last tick.
        //
        // Swept arcs rather than blade positions, and the difference is visible
        // from the first minute of play. Sampling where the blades *are* every
        // 0.3 s means a blade can be drawn passing straight through an enemy and
        // do nothing, because at the instant the damage was sampled it was
        // somewhere else — the visual and the effect disagree, and the player
        // reads that as the card being unreliable rather than as a sampling rate.
        // At three blades that was a 57% chance per tick of hitting the enemy
        // biting you, which is exactly the sort of number nobody can perceive as
        // anything but "sometimes it works".
        int count = _horde!.Within(GlobalPosition, OrbitRadius + OrbitBite, _touched);

        // Backwards: a kill swap-removes the last enemy into the dead one's slot,
        // and a forward walk would skip whoever took its place.
        for (int n = count - 1; n >= 0; n--)
        {
            int index = _touched[n];
            if (index >= _horde.Pool.Count)
                continue;

            Vector3 offset = _horde.Pool.Position[index] - GlobalPosition;
            var flat = new Vector2(offset.X, offset.Z);
            float distance = flat.Length();

            // Inside the ring as well as outside it. An enemy that has walked
            // past the blades is standing in the middle of them, not safe.
            if (distance < OrbitRadius - OrbitBite)
                continue;

            float at = Mathf.PosMod(Mathf.Atan2(flat.Y, flat.X), Mathf.Tau);

            int passes = 0;
            for (int i = 0; i < blades; i++)
            {
                float bladeFrom = Mathf.PosMod(from + Mathf.Tau * i / blades, Mathf.Tau);
                if (Mathf.PosMod(at - bladeFrom, Mathf.Tau) < swept)
                    passes++;
            }

            if (passes == 0)
                continue;

            // Once per blade that went through it. More blades is more passes,
            // which is what the card promises — not a wider ring.
            Vector2 away = flat.LengthSquared() > 0.0001f ? flat.Normalized() : Vector2.Right;
            _horde.Damage(index, OrbitDamage * passes, away * 0.2f);

            // The ring cuts, so it leaves a cut.
            //
            // **Retinue is the line whose things fight without you, and bleed is
            // damage that happens without you** — the two were the same sentence
            // and only one of them was written down. It also gives the line a
            // *status*, which is what the reaction system needs from it: three of
            // the five lines have to be able to open a reaction or the chemistry
            // belongs entirely to the shop.
            //
            // Weaker and shorter than a knife's, which is 4 a second for 3. A
            // blade you are holding should out-bleed a blade that is orbiting on
            // its own, or the Sidearm slot's one bleeding weapon is outclassed by
            // a card.
            _horde.ApplyBleed(index, OrbitBleed, OrbitBleedSeconds);
        }
    }

    /// The spin at the previous damage tick, so the arc between them is known.
    private float _lastBite;

    private void StepShockwave(float step)
    {
        int stacks = _player!.Mods.PulseStacks;
        if (stacks <= 0)
            return;

        float interval = Mathf.Max(PulseIntervalFloor, PulseInterval - PulseIntervalPerStack * stacks);

        _sincePulse += step;
        if (_sincePulse < interval)
            return;

        _sincePulse = 0.0f;

        float radius = (PulseRadius + PulseRadiusPerStack * stacks) * _player.Mods.AreaScale;
        float damage = PulseDamage + PulseDamagePerStack * stacks;

        int count = _horde!.Within(GlobalPosition, radius, _touched);
        for (int n = count - 1; n >= 0; n--)
        {
            int index = _touched[n];
            if (index >= _horde.Pool.Count)
                continue;

            Vector3 away = _horde.Pool.Position[index] - GlobalPosition;
            var push = new Vector2(away.X, away.Z);
            _horde.Damage(index, damage, (push.LengthSquared() > 0.0001f ? push.Normalized() : Vector2.Right)
                                         * PulseKnockback);
        }

        Pulsed?.Invoke(GlobalPosition, radius);
    }

    /// Fired when the shockwave goes off, with where and how wide.
    ///
    /// An event rather than the kit drawing it, because the effect director owns
    /// every particle in the game and a second sprinkler here would be a second
    /// budget nobody is watching. It carries the radius because a pulse that hit
    /// nothing must still be visible — otherwise the card looks broken exactly
    /// when the player has cleared the space around them, which is the moment
    /// they are most likely to be looking.
    public event System.Action<Vector3, float>? Pulsed;

    // ------------------------------------------------------------------------

    /// One MultiMesh for every blade the run can have.
    ///
    /// Small boxes rather than sprites: they are lit by the same sun as
    /// everything else and read as objects rather than as decals, which matters
    /// because the player has to believe the ring is *there* to fight inside it.
    private void BuildBlades()
    {
        var builder = new MeshBuilder();
        // Thin, long, and steel rather than white. At 0.10 by 0.42 in a pale
        // near-white they read as sheets of paper orbiting the player — big
        // enough to draw the eye away from the crowd, which is the one thing the
        // player has to be watching. Narrow along the direction of travel and
        // dark enough to sit in the palette.
        builder.Box(Vector3.Zero, new Vector3(0.05f, 0.26f, 0.60f), new Color(0.58f, 0.62f, 0.70f));

        // A brighter leading edge, so the direction of the spin is visible. A
        // symmetrical blade at this size is a smudge that could be going either
        // way, and which way the ring turns is information — it is where the next
        // hit lands.
        builder.Box(new Vector3(0.0f, 0.0f, 0.26f), new Vector3(0.06f, 0.28f, 0.08f),
                    new Color(0.88f, 0.90f, 0.96f));

        ArrayMesh mesh = builder.Build();
        mesh.SurfaceSetMaterial(0, PropLibrary.Material());

        _bladeMesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            Mesh = mesh,
            InstanceCount = MaxBlades,
            VisibleInstanceCount = 0,
        };

        _blades = new MultiMeshInstance3D
        {
            Name = "Blades",
            Multimesh = _bladeMesh,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Visible = false,
        };

        AddChild(_blades);
    }

    /// The chilled ground, as flat shards of ice around the player.
    ///
    /// **Chill had never been visible.** `Horde` slows anything inside
    /// `ChillRadius` on a gradient, and nothing anywhere drew where that was — so
    /// the card's whole effect was enemies moving at a speed the player could not
    /// account for, in an area they could not see the edge of. Of the four kit
    /// cards it is the one whose value depends most on knowing its extent: it is
    /// bought to make ground defensible, and ground you cannot identify is not
    /// ground you can choose to stand on.
    ///
    /// Shards laid flat rather than a disc. A solid circle on the floor reads as
    /// a decal or a selection marker — a UI element the player looks past — while
    /// broken plates read as something that happened to the ground. They also
    /// leave gaps, so the scatter and the slab seams stay visible through it
    /// instead of painting over the floor E4 just gave a scale to.
    ///
    /// **Built at the real radius, in metres.** The first version authored a unit
    /// ring and scaled the node by 7.5, which scales the shards too — every plate
    /// came out over two metres across and the effect read as sheets of blue
    /// paper dropped round the player. Position scales with the radius; size does
    /// not, and the only way to have both is to lay it out at full size.
    private void BuildFrost()
    {
        float radius = _horde?.ChillRadius ?? 7.5f;

        var builder = new MeshBuilder();
        ulong rng = 0x4F6CDD1D2545F491UL;

        // Dark enough to sit on the floor rather than on top of it. The first
        // pass was around 0.7 and the shards glowed like lit panels; ice in a
        // dusk arena is a cold *dark* thing with a pale edge.
        var pale = new Color(0.38f, 0.52f, 0.62f);
        var deep = new Color(0.17f, 0.26f, 0.35f);

        // Rings out to the edge, each with enough plates to keep the density
        // roughly even — the count follows the circumference, so the outer ring
        // is not visibly sparser than the inner one.
        //
        // Denser toward the middle even so, because the slow is a gradient and
        // the drawing should be one. An even scatter would say the edge bites as
        // hard as the centre, which is the one thing about the card that is not
        // true.
        const int Rings = 5;

        for (int ring = 0; ring < Rings; ring++)
        {
            float at = radius * (0.14f + ring * 0.21f);
            float density = 1.0f - ring * 0.12f;
            int count = Mathf.Max(4, Mathf.RoundToInt(at * 2.6f * density));

            for (int i = 0; i < count; i++)
            {
                float angle = Mathf.Tau * i / count + ring * 0.7f + Next(ref rng) * 0.5f;
                float out2 = at + (Next(ref rng) - 0.5f) * radius * 0.14f;

                // Plates, not chips. Around a third of a metre reads as broken
                // ice underfoot at this camera; much smaller and it is gravel,
                // much larger and it is board.
                float size = 0.26f + Next(ref rng) * 0.22f;

                builder.Box(new Vector3(Mathf.Cos(angle) * out2, 0.02f, Mathf.Sin(angle) * out2),
                            new Vector3(size, 0.03f, size * 0.72f),
                            deep.Lerp(pale, Next(ref rng)),
                            Next(ref rng) * 90.0f);
            }
        }

        ArrayMesh mesh = builder.Build();
        mesh.SurfaceSetMaterial(0, PropLibrary.Material());

        _frost = new MeshInstance3D
        {
            Name = "Frost",
            Mesh = mesh,

            // No shadow. Three centimetres thick and lying on the floor, so its
            // shadow is a dark copy of itself offset by nothing — which reads as
            // the ground being dirty rather than frozen.
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Visible = false,
        };

        AddChild(_frost);
    }

    /// Shows the chilled ground while the card is held.
    private void StepFrost()
    {
        if (_frost != null)
            _frost.Visible = (_player?.Mods.Chill ?? 0.0f) > 0.0f;
    }

    private MeshInstance3D? _frost;

    private static float Next(ref ulong state)
    {
        state ^= state << 13;
        state ^= state >> 7;
        state ^= state << 17;
        return (state >> 40) / 16777216.0f;
    }

    private void DrawBlades(int blades)
    {
        if (_bladeMesh == null)
            return;

        _bladeMesh.VisibleInstanceCount = blades;

        for (int i = 0; i < blades; i++)
        {
            float angle = _spin + Mathf.Tau * i / blades;

            // Positioned in the node's own space, which sits at the player's feet
            // and is never rotated — so the blades circle the world rather than
            // the character. Each is turned to face along its own travel, because
            // a blade broadside to its direction reads as a floating brick.
            var basis = new Basis(Vector3.Up, -angle);
            _bladeMesh.SetInstanceTransform(i, new Transform3D(basis, new Vector3(
                Mathf.Cos(angle) * OrbitRadius,
                0.55f,
                Mathf.Sin(angle) * OrbitRadius)));
        }
    }

    /// Seconds until the next pulse, and how far through the wait it is. The
    /// readout asks: a card whose whole value is a rhythm needs the rhythm shown.
    public float PulseProgress
    {
        get
        {
            int stacks = _player?.Mods.PulseStacks ?? 0;
            if (stacks <= 0)
                return 0.0f;

            float interval = Mathf.Max(PulseIntervalFloor, PulseInterval - PulseIntervalPerStack * stacks);
            return Mathf.Clamp(_sincePulse / interval, 0.0f, 1.0f);
        }
    }

    /// How many blades are drawn right now. Only a probe asks.
    public int BladeCount => _bladeMesh?.VisibleInstanceCount ?? 0;
}
