using Godot;

public partial class Player : CharacterBody3D
{
    [Export] public float MoveSpeed { get; set; } = 6.0f;

    /// Higher settles onto the target velocity faster. This is a rate, not a
    /// per-tick fraction — see the damping note in _PhysicsProcess.
    [Export] public float AccelerationRate { get; set; } = 14.0f;

    [Export] public float MaxHealth { get; set; } = 100.0f;

    /// Backpack slots. Small enough that a full run forces a choice about what to
    /// carry out.
    [Export] public int CarryCapacity { get; set; } = 20;

    [Signal] public delegate void DiedEventHandler();

    /// Bulk the safe box holds. Deliberately tiny: it is a hedge against a bad
    /// death, not a second backpack.
    [Export] public int SafeBoxCapacity { get; set; } = 4;

    public float Health { get; private set; }
    public bool IsAlive => Health > 0.0f;
    public Inventory Backpack { get; private set; } = null!;

    /// Survives death. Everything in the backpack does not.
    public Inventory SafeBox { get; private set; } = null!;

    /// Last non-zero heading, in world XZ. Weapons fall back to this when there
    /// is nothing to aim at, so a swing at empty air still goes somewhere sensible.
    public Vector2 Facing { get; private set; } = Vector2.Down;

    // Both are assigned in _Ready, which always runs before anything reads them.
    private IInputSource _input = null!;
    private Sprite3D _sprite = null!;

    /// Lets the HUD install the touch source once the sticks exist. Without a
    /// call, the player falls back to keyboard and mouse.
    public void SetInputSource(IInputSource source) => _input = source;

    public override void _Ready()
    {
        _sprite = GetNode<Sprite3D>("Sprite");
        _input ??= new KeyboardMouseInput(GetViewport().GetCamera3D());
        Health = MaxHealth;
        Backpack = new Inventory(CarryCapacity);
        SafeBox = new Inventory(SafeBoxCapacity);
    }

    /// Moves one unit of the most valuable backpack item into the safe box.
    /// Returns what it was worth, or 0 if nothing moved.
    ///
    /// One unit per press on purpose: securing a haul costs real seconds while
    /// the horde closes, which is the decision the safe box exists to create.
    public int TrySecureBest()
    {
        int index = Backpack.MostValuableIndex();
        if (index < 0)
            return 0;

        ItemResource item = Backpack.ItemAt(index);
        if (SafeBox.TryAdd(item, 1) == 0)
            return 0;

        Backpack.RemoveOne(index);
        return item.Value;
    }

    /// Damage is ignored once dead, so a horde landing several hits in the same
    /// tick cannot emit Died more than once.
    public void TakeDamage(float amount)
    {
        if (!IsAlive || amount <= 0.0f)
            return;

        Health = Mathf.Max(0.0f, Health - amount);
        if (Health <= 0.0f)
            EmitSignal(SignalName.Died);
    }

    public void Heal(float amount) => Health = Mathf.Min(MaxHealth, Health + amount);

    public override void _PhysicsProcess(double delta)
    {
        _input.Update(GlobalPosition);

        Vector2 move = _input.Move;
        var desired = new Vector3(move.X, 0.0f, move.Y) * MoveSpeed;

        // Frame-rate-independent damping (godot.md:50): exponential decay toward
        // the target, never a fixed fraction per tick — the latter changes feel
        // whenever the tick rate does.
        float t = 1.0f - Mathf.Exp(-AccelerationRate * (float)delta);
        Velocity = new Vector3(
            Mathf.Lerp(Velocity.X, desired.X, t),
            0.0f,
            Mathf.Lerp(Velocity.Z, desired.Z, t));

        MoveAndSlide();
        UpdateFacing();

        if (_input.SecurePressed)
            TrySecureBest();
    }

    /// One sprite direction, mirrored at runtime. Generators cannot reliably draw
    /// a specific facing (SKILL.md:109), so paying for a mirrored set buys
    /// nothing a flip does not.
    private void UpdateFacing()
    {
        Vector2 facing = _input.Aim != Vector2.Zero ? _input.Aim : _input.Move;
        if (facing == Vector2.Zero)
            return;

        Facing = facing.Normalized();
        if (Mathf.Abs(facing.X) > 0.05f)
            _sprite.FlipH = facing.X < 0.0f;
    }
}
