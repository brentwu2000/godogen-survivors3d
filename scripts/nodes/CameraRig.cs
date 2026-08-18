using Godot;

/// Holds the tilted orthographic camera and trails the player across the ground
/// plane. The rig moves; the camera's local offset and angle never change, so
/// the framing stays identical no matter where the player is.
public partial class CameraRig : Node3D
{
    [Export] public NodePath? TargetPath { get; set; }

    /// Higher is tighter. Low values read as a lazy camera, which hides enemies
    /// entering from the direction of travel.
    [Export] public float FollowRate { get; set; } = 8.0f;

    /// Metres of displacement at full shake. Deliberately small: this camera is
    /// orthographic and top-down, so a shake big enough to be dramatic is a shake
    /// big enough to lose the player's own sprite in a crowd.
    [Export] public float ShakeMetres { get; set; } = 0.55f;

    /// How fast a shake dies, in units per second. Under a fifth of a second —
    /// long enough to feel like an impact, short enough that two explosions in a
    /// row are two impacts rather than one long wobble.
    [Export] public float ShakeFade { get; set; } = 6.0f;

    /// How far away an explosion still moves the camera.
    [Export] public float ShakeRange { get; set; } = 18.0f;

    private Node3D? _target;
    private Player? _player;
    private Horde? _horde;

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
