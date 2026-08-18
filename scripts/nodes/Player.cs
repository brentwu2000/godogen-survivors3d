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

    /// Something left the backpack and did something. Two events rather than one
    /// because spending a medkit and throwing a pipe bomb are different answers
    /// to different questions, and a contract asking "did you get out without
    /// healing" must not be satisfied by never having thrown anything.
    ///
    /// Carries the item name rather than the resource: the only readers are a log
    /// and a readout, and a name is what both of them want.
    public event System.Action<string>? ItemUsed;

    /// The item's name and how many the throw killed outright. The count rides
    /// along because the alternative is the log counting deaths in a window after
    /// a throw, which would credit the grenade with whatever the rifle did during
    /// the same second.
    public event System.Action<string, int>? ItemThrown;

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

    /// What this run's upgrades have changed, for everything that reads a rule
    /// rather than a number. Lives on the player because a run is a player;
    /// read by the weapon, the horde and the loot containers at the point of use.
    public RunModifiers Mods { get; } = new();

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

    /// Which implementation is installed. Only a probe asks — and it has to,
    /// because "the touch layer is present" and "the touch layer is connected"
    /// looked identical for sixteen phases, and the second one was false.
    public string InputSourceName => _input?.GetType().Name ?? "none";

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
        ItemUsed?.Invoke(chosen.ItemName);
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

        int killed = 0;

        switch (chosen.Effect)
        {
            case ItemEffect.Explosive:
                killed = _horde.Detonate(landing, chosen.EffectRadius * Mods.AreaScale, chosen.EffectAmount);
                GD.Print($"threw {chosen.ItemName}: {killed} killed");
                break;

            case ItemEffect.Incendiary:
                // Zero, and honestly so. Burning ground kills over seven seconds
                // and the horde walks through it the whole time, so any number
                // attributed here would be "kills that happened afterwards"
                // wearing the throw's name.
                _horde.Hazards.Add(landing, chosen.EffectRadius * Mods.AreaScale,
                                   chosen.EffectAmount, chosen.EffectDuration);
                break;

            default:
                return 0;
        }

        Backpack.RemoveOne(best);
        ItemThrown?.Invoke(chosen.ItemName, killed);
        return chosen.Value;
    }

    /// Whether spending something would currently do anything at all. Asked by
    /// the touch controls to dim the use button — the same question TryUseBest
    /// answers by returning zero, but asked before the tap rather than after.
    public bool HasUsableItem
    {
        get
        {
            for (int i = 0; i < Backpack.EntryCount; i++)
            {
                ItemResource item = Backpack.ItemAt(i);
                if (item.IsUsable && WouldHelp(item))
                    return true;
            }

            return false;
        }
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

    /// The rules the gear grants before the first level-up.
    ///
    /// Added to whatever the run already holds rather than assigned, and applied
    /// in the same pass as the stats. `RunModifiers` has a `Reset()` that nothing
    /// currently calls; if anything ever does, it has to happen before this and
    /// not after — the failure mode is a piece of equipment that works until the
    /// player takes their first upgrade and then quietly stops, which no exit
    /// code would report.
    ///
    /// Every argument is what the gear *adds*, so zero is the neutral value for
    /// all six — including area, whose modifier is a multiplier neutral at 1.
    /// Passing a RunModifiers here instead was the first shape and it made three
    /// pieces granting nothing add up to a triple-size blast.
    public void ApplyGearRules(int pierce, float area, float thorns, float regen,
                               float knockback, float dodge)
    {
        Mods.Pierce += pierce;
        Mods.AreaScale += area;
        Mods.Thorns += thorns;
        Mods.Regen += regen;
        Mods.Knockback += knockback;
        Mods.Dodge += dodge;
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
    ///
    /// Dodge is rolled per tick rather than per hit, for the same reason: there
    /// are no hits, only a rate. A tenth of dodge therefore removes a tenth of
    /// the damage over any window long enough to matter, which is what the card
    /// promises and what a per-hit roll would only approximate.
    public void TakeContactDamage(float damagePerSecond, float delta)
    {
        if (Mods.Dodge > 0.0f && NextFloat() < Mods.Dodge)
            return;

        ApplyDamage(Mitigate(damagePerSecond) * delta);
    }

    private ulong _rng = 0x9E3779B97F4A7C15UL;

    private float NextFloat()
    {
        _rng ^= _rng << 13;
        _rng ^= _rng >> 7;
        _rng ^= _rng << 17;
        return (_rng >> 40) / 16777216.0f;
    }

    /// Twenty percent always gets through. Armour that can reach zero turns the
    /// weakest variant into scenery, and a horde you can stand in is not a horde.
    private float Mitigate(float amount) =>
        amount <= 0.0f ? 0.0f : Mathf.Max(amount - Armour, amount * 0.2f);

    /// Damage taken since somebody last asked, cleared by asking.
    ///
    /// A signal per application would fire sixty times a second: contact damage
    /// arrives as a per-tick slice of a rate, so "was I hit" is not an event the
    /// player character can answer — only "how much, lately". The reader decides
    /// what is worth reacting to, which keeps the threshold next to the feedback
    /// rather than buried in here.
    ///
    /// Reading clears it, so it has exactly one owner: `SoundDirector`. Anything
    /// else that wants to react to damage watches Health instead — a second
    /// consumer would take turns with the first and each would see about half of
    /// what happened, which is a bug that presents as feedback that sometimes
    /// works.
    private float _damageSincePoll;

    public float ConsumeDamageTaken()
    {
        float taken = _damageSincePoll;
        _damageSincePoll = 0.0f;
        return taken;
    }

    private void ApplyDamage(float amount)
    {
        if (!IsAlive || amount <= 0.0f)
            return;

        Health = Mathf.Max(0.0f, Health - amount);
        _damageSincePoll += amount;

        if (Health <= 0.0f)
            EmitSignal(SignalName.Died);
    }

    public void Heal(float amount) => Health = Mathf.Min(MaxHealth, Health + amount);

    public override void _PhysicsProcess(double delta)
    {
        _input.Update(GlobalPosition);

        if (AdrenalineRemaining > 0.0f)
            AdrenalineRemaining = Mathf.Max(0.0f, AdrenalineRemaining - (float)delta);

        if (Mods.Regen > 0.0f && IsAlive)
            Heal(Mods.Regen * (float)delta);

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
