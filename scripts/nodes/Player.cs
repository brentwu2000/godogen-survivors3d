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

    /// Flat mitigation, subtracted from an incoming rate or amount. Never scales
    /// it: armour is the answer to a crowd of weak contacts and never the answer
    /// to a brute, and that asymmetry is what makes it worth picking over damage.
    public float Armour { get; private set; }

    /// Multiplies how fast a container fills. One place so the loot container
    /// does not have to know what a player upgrade is.
    public float SearchSpeed { get; private set; } = 1.0f;

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
    private WeaponHandler? _weapons;
    private Horde? _horde;

    /// Lets the HUD install the touch source once the sticks exist. Without a
    /// call, the player falls back to keyboard and mouse.
    public void SetInputSource(IInputSource source) => _input = source;

    public override void _Ready()
    {
        _sprite = GetNode<Sprite3D>("Sprite");
        _weapons = GetNodeOrNull<WeaponHandler>("WeaponHandler");
        _horde = GetParent()?.GetNodeOrNull<Horde>("Horde");
        _input ??= new KeyboardMouseInput(GetViewport().GetCamera3D());
        Health = MaxHealth;
        Backpack = new Inventory(CarryCapacity);
        SafeBox = new Inventory(SafeBoxCapacity);
    }

    /// Speed multiplier while adrenaline is running.
    [Export] public float AdrenalineBoost { get; set; } = 0.35f;

    public float AdrenalineRemaining { get; private set; }
    public bool AdrenalineActive => AdrenalineRemaining > 0.0f;

    /// Spends the cheapest carried item that would do something right now, and
    /// returns what it cost — which is exactly its extraction value, because the
    /// backpack holds health and money in the same slots.
    ///
    /// Cheapest first, and only if it helps: the tinned food goes before the
    /// medkit, and neither is spent at full health. Nothing here ever reaches
    /// for the serum, because pure cargo is not usable at any price.
    public int TryUseBest()
    {
        int best = -1;
        int bestValue = int.MaxValue;

        for (int i = 0; i < Backpack.EntryCount; i++)
        {
            ItemResource item = Backpack.ItemAt(i);
            if (!item.IsUsable || item.Value >= bestValue || !WouldHelp(item))
                continue;

            best = i;
            bestValue = item.Value;
        }

        if (best < 0)
            return 0;

        ItemResource chosen = Backpack.ItemAt(best);
        if (!Use(chosen))
            return 0;

        Backpack.RemoveOne(best);
        return chosen.Value;
    }

    /// How far a throw lands, along the direction the player is facing. Fixed
    /// rather than aimed at the nearest crowd: a thrown item the player cannot
    /// predict the landing of is one they will not spend.
    [Export] public float ThrowRange { get; set; } = 8.0f;

    /// Throws the cheapest thing in the bag that acts on the world, and returns
    /// what it cost. Its own verb because a heal and a grenade on one key is a
    /// grenade thrown at the wrong moment.
    public int TryThrow()
    {
        int best = -1;
        int bestValue = int.MaxValue;

        for (int i = 0; i < Backpack.EntryCount; i++)
        {
            ItemResource item = Backpack.ItemAt(i);
            if (!item.IsThrowable || item.Value >= bestValue)
                continue;

            best = i;
            bestValue = item.Value;
        }

        if (best < 0 || _horde == null)
            return 0;

        ItemResource chosen = Backpack.ItemAt(best);
        Vector2 aim = Facing == Vector2.Zero ? Vector2.Down : Facing;
        Vector3 landing = GlobalPosition + new Vector3(aim.X, 0.0f, aim.Y) * ThrowRange;
        landing.Y = 0.0f;

        switch (chosen.Effect)
        {
            case ItemEffect.Explosive:
                int killed = _horde.Detonate(landing, chosen.EffectRadius, chosen.EffectAmount);
                GD.Print($"threw {chosen.ItemName}: {killed} killed");
                break;

            case ItemEffect.Incendiary:
                _horde.Hazards.Add(landing, chosen.EffectRadius, chosen.EffectAmount, chosen.EffectDuration);
                break;

            default:
                return 0;
        }

        Backpack.RemoveOne(best);
        return chosen.Value;
    }

    /// How many throwables are in the bag, for the readout. A tactical item the
    /// player has to open a menu to count is one they forget they have.
    public int ThrowableCount
    {
        get
        {
            int total = 0;
            for (int i = 0; i < Backpack.EntryCount; i++)
            {
                if (Backpack.ItemAt(i).IsThrowable)
                    total += Backpack.CountAt(i);
            }

            return total;
        }
    }

    private bool WouldHelp(ItemResource item) => item.Effect switch
    {
        ItemEffect.Heal => Health < MaxHealth,
        ItemEffect.Ammo => _weapons?.WantsAmmo ?? false,
        ItemEffect.Adrenaline => !AdrenalineActive,
        _ => false,
    };

    private bool Use(ItemResource item)
    {
        switch (item.Effect)
        {
            case ItemEffect.Heal:
                Heal(item.EffectAmount);
                return true;

            case ItemEffect.Ammo:
                return _weapons?.AddReserve(Mathf.RoundToInt(item.EffectAmount)) > 0;

            case ItemEffect.Adrenaline:
                AdrenalineRemaining = item.EffectAmount;
                return true;

            default:
                return false;
        }
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

    /// Applies the gear the player walked in with. Called before the run starts,
    /// while the inventories are still empty — they are rebuilt here because a
    /// backpack's size is something gear decides, not the scene.
    public void ApplyGear(float health, float armour, float speed, int carry, int safeBox)
    {
        MaxHealth += health;
        Armour += armour;
        MoveSpeed += speed;
        CarryCapacity += carry;
        SafeBoxCapacity += safeBox;

        Health = MaxHealth;
        Backpack = new Inventory(CarryCapacity);
        SafeBox = new Inventory(SafeBoxCapacity);
    }

    /// In-run upgrades. Health is granted as current as well as maximum: a pick
    /// that only raises the ceiling is worth nothing at the moment it is offered,
    /// which is exactly when the player is deciding whether it saves them.
    public void AddMaxHealth(float amount)
    {
        MaxHealth += amount;
        Health = Mathf.Min(MaxHealth, Health + amount);
    }

    public void AddArmour(float amount) => Armour += amount;
    public void AddMoveSpeedFraction(float fraction) => MoveSpeed *= 1.0f + fraction;
    public void AddSearchSpeedFraction(float fraction) => SearchSpeed += fraction;

    /// Damage is ignored once dead, so a horde landing several hits in the same
    /// tick cannot emit Died more than once.
    public void TakeDamage(float amount) => ApplyDamage(Mitigate(amount));

    /// Contact arrives as a rate, so armour is subtracted from the rate rather
    /// than from each tick's slice — otherwise mitigation would depend on the
    /// physics tick rate, which is not a thing the player can see or choose.
    public void TakeContactDamage(float damagePerSecond, float delta) =>
        ApplyDamage(Mitigate(damagePerSecond) * delta);

    /// Twenty percent always gets through. Armour that can reach zero turns the
    /// weakest variant into scenery, and a horde you can stand in is not a horde.
    private float Mitigate(float amount) =>
        amount <= 0.0f ? 0.0f : Mathf.Max(amount - Armour, amount * 0.2f);

    private void ApplyDamage(float amount)
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

        if (AdrenalineRemaining > 0.0f)
            AdrenalineRemaining = Mathf.Max(0.0f, AdrenalineRemaining - (float)delta);

        Vector2 move = _input.Move;
        float speed = AdrenalineActive ? MoveSpeed * (1.0f + AdrenalineBoost) : MoveSpeed;
        var desired = new Vector3(move.X, 0.0f, move.Y) * speed;

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

        if (_input.UsePressed)
            TryUseBest();

        if (_input.ThrowPressed)
            TryThrow();

        if (_input.SwapPressed)
            _weapons?.SwapWeapon();
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
