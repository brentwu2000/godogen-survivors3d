using Godot;

/// Holds the camera, trails the player across the ground plane, and owns the
/// direction the world is being looked at from.
///
/// The camera's local offset and angle never change — the rig moves and turns,
/// and the camera rides it. That is what makes `Yaw` the single definition of
/// "forward" in this game: the view direction, the direction `[W]` advances in,
/// and the direction the player faces are all read off this one number rather
/// than kept in step with each other.
///
/// **The projection is perspective now.** It was a 52° orthographic camera 24 m
/// up, which is a good way to read a crowd and a bad way to look at anything —
/// nothing is nearer than anything else, so solid geometry reads as a diagram of
/// itself. Perspective at a shallower tilt costs some of that readability and
/// buys the only depth cue that works at every distance at once.
public partial class CameraRig : Node3D
{
    [Export] public NodePath? TargetPath { get; set; }

    /// Degrees per second the view turns under `[Z]`/`[X]`.
    ///
    /// The same rate `Player` uses for `[A]`/`[D]`, and it is stored here rather
    /// than there so the two cannot drift: a player turning with one hand on the
    /// movement keys and one on the view keys must not get two different speeds
    /// out of what feels like one control.
    [Export] public float TurnRateDegrees { get; set; } = 150.0f;

    /// Radians of turn per pixel of right-drag. A hundred pixels is about 34°.
    [Export] public float DragRadiansPerPixel { get; set; } = 0.006f;

    /// Higher is tighter. Low values read as a lazy camera, which hides enemies
    /// entering from the direction of travel.
    [Export] public float FollowRate { get; set; } = 8.0f;

    /// Metres of displacement at full shake.
    ///
    /// Was 0.55 under the top-down orthographic camera, where a shake big enough
    /// to be dramatic was a shake big enough to lose the player in a crowd. From
    /// behind and near the ground the same displacement covers far more of the
    /// screen, so this is smaller now and reads as more.
    [Export] public float ShakeMetres { get; set; } = 0.35f;

    /// How fast a shake dies, in units per second. Under a fifth of a second —
    /// long enough to feel like an impact, short enough that two explosions in a
    /// row are two impacts rather than one long wobble.
    [Export] public float ShakeFade { get; set; } = 6.0f;

    /// How far away an explosion still moves the camera.
    [Export] public float ShakeRange { get; set; } = 18.0f;

    private Node3D? _target;
    private Player? _player;
    private Horde? _horde;

    /// Where the view is looking, in radians about +Y. Zero looks along −Z,
    /// which is Godot's own forward and the direction this game was built facing
    /// before it could turn at all — so a yaw of zero reproduces the old camera
    /// exactly, and every probe that predates turning still means what it meant.
    public float Yaw { get; private set; }

    /// Accumulated right-drag, in radians, spent on the next frame.
    ///
    /// Buffered rather than applied in the event handler because mouse motion
    /// arrives several times per frame and the rig's transform should be written
    /// once, next to the follow and the shake, rather than partially updated
    /// three times between them.
    private float _dragYaw;

    private float _shake;
    private float _shownHealth = -1.0f;
    private ulong _rng = 0x8EBC6AF09C88C6E3UL;

    public override void _Ready()
    {
        _target = TargetPath != null ? GetNodeOrNull<Node3D>(TargetPath) : null;
        _player = _target as Player ?? GetParent()?.GetNodeOrNull<Player>("Player");
        _horde = GetParent()?.GetNodeOrNull<Horde>("Horde");

        if (_horde != null)
            _horde.Exploded += OnExploded;

        if (_target != null)
            GlobalPosition = Flatten(_target.GlobalPosition);
    }

    /// The horde's event is a plain C# delegate, so it holds a strong reference to
    /// this node — leaving it connected past a scene change is a call into a freed
    /// object.
    public override void _ExitTree()
    {
        if (_horde != null)
            _horde.Exploded -= OnExploded;
    }

    /// Adds to whatever is already shaking rather than replacing it, so a bloater
    /// chain reads as heavier than one bloater. Clamped, because the sum of a
    /// pile of them should still leave the arena readable.
    public void Shake(float amount) => _shake = Mathf.Min(1.0f, _shake + amount);

    /// Turns the view. Positive is counter-clockwise seen from above.
    ///
    /// Wrapped rather than clamped or left to grow: the yaw is read every frame
    /// by the player's movement and by every driver, and an angle that has been
    /// accumulating for a ten-minute run is an angle whose sine costs precision
    /// it did not have to.
    public void Turn(float radians) => Yaw = Mathf.Wrap(Yaw + radians, -Mathf.Pi, Mathf.Pi);

    /// The direction the view is looking, flattened to the ground plane.
    ///
    /// `(−sin, −cos)` because a basis rotated by `Yaw` about +Y sends Godot's
    /// forward `(0, 0, −1)` there. This is the one place that conversion is
    /// written down; `Player` and `BotDrive` both call it rather than repeating
    /// the trigonometry, because two copies of a sign convention is one copy and
    /// a bug waiting for the first time somebody turns past 90°.
    public static Vector2 Forward(float yaw) => new(-Mathf.Sin(yaw), -Mathf.Cos(yaw));

    public Vector2 Forward() => Forward(Yaw);

    /// Right-drag turns the view, which is the mouse's whole job now.
    ///
    /// It used to aim: the cursor was projected onto the ground plane and the
    /// player faced it. That works under a camera that cannot turn and stops
    /// working under one that can — the world sweeps beneath a stationary cursor
    /// as the view comes round, so the player spins while the hand holding the
    /// mouse is still. Turning to face something is the control scheme now, and
    /// the mouse is one of the three ways to do it.
    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventMouseMotion motion
            && (motion.ButtonMask & MouseButtonMask.Right) != 0)
        {
            _dragYaw -= motion.Relative.X * DragRadiansPerPixel;
        }
    }

    private void OnExploded(Vector3 position)
    {
        if (_player == null)
            return;

        float distance = Flatten(position).DistanceTo(Flatten(_player.GlobalPosition));
        Shake(0.9f * Mathf.Clamp(1.0f - distance / ShakeRange, 0.0f, 1.0f));
    }

    public override void _Process(double delta)
    {
        if (_target == null)
            return;

        float step = (float)delta;
        WatchForDamage(step);

        // `[Z]`/`[X]` and the drag, before the transform is written. `[A]`/`[D]`
        // arrive through `Player.Steer` on the physics tick instead — turning is
        // steering when it comes from the movement keys, and the player is what
        // owns steering.
        float keys = Input.GetActionStrength("view_right") - Input.GetActionStrength("view_left");
        Turn(-keys * Mathf.DegToRad(TurnRateDegrees) * step + _dragYaw);
        _dragYaw = 0.0f;

        // Rotation is set every frame rather than only when the yaw changes.
        // Nothing else writes this node's rotation, and a conditional here would
        // be an invitation for something later to.
        Rotation = new Vector3(0.0f, Yaw, 0.0f);

        float t = 1.0f - Mathf.Exp(-FollowRate * step);
        Vector3 settled = GlobalPosition.Lerp(Flatten(_target.GlobalPosition), t);

        _shake = Mathf.Max(0.0f, _shake - ShakeFade * step);
        if (_shake <= 0.0f)
        {
            GlobalPosition = settled;
            return;
        }

        // Squared, so the tail of a shake falls away quickly instead of turning
        // into a slow drift the player reads as the camera being broken.
        //
        // Added to `GlobalPosition`, which is deliberately not affected by the
        // rotation set above — a shake in the rig's local space would change
        // direction as the view came round, so an explosion to the north would
        // rattle the screen differently depending on which way you were looking.
        float amplitude = ShakeMetres * _shake * _shake;
        GlobalPosition = settled + new Vector3(Bipolar() * amplitude, 0.0f, Bipolar() * amplitude);
    }

    /// Shake on being hurt, read off health rather than from the damage
    /// accumulator — that one is cleared by reading and already has an owner
    /// (see Player.ConsumeDamageTaken).
    private void WatchForDamage(float step)
    {
        if (_player == null)
            return;

        if (_shownHealth < 0.0f)
        {
            _shownHealth = _player.Health;
            return;
        }

        float lost = _shownHealth - _player.Health;
        _shownHealth = _player.Health;

        // A threshold, because contact damage arrives as a per-tick slice: without
        // one, standing in a crowd would shake the camera every frame forever,
        // which stops being a signal and becomes the way the game looks.
        float maxHealth = Mathf.Max(1.0f, _player.MaxHealth);
        if (lost > maxHealth * 0.04f)
            Shake(Mathf.Min(0.6f, lost / (maxHealth * 0.25f)));
    }

    private static Vector3 Flatten(Vector3 position) => new(position.X, 0.0f, position.Z);

    /// Deterministic and allocation-free, like every other generator here, so a
    /// capture run reproduces frame for frame.
    private float Bipolar()
    {
        _rng ^= _rng << 13;
        _rng ^= _rng >> 7;
        _rng ^= _rng << 17;
        return (_rng >> 40) / 8388608.0f - 1.0f;
    }
}
