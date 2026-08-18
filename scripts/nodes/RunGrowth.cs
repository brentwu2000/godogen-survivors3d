using Godot;

/// Every in-run upgrade. Order is the pick's identity — it indexes the taken and
/// cap tables — so new entries go on the end.
public enum GrowthOption
{
    WeaponLevel,
    MaxHealth,
    Armour,
    MoveSpeed,
    SearchSpeed,

    // Rules rather than numbers. These are what a run's identity is made of: a
    // deck of five options meant every run ended the same shape, because the
    // only question was which order to buy the same five things in.
    Pierce,
    Crit,
    FireRate,
    Area,
    Knockback,
    Ignite,
    Detonate,
    Lifesteal,
    Regen,
    Dodge,
    Thorns,
    Reach,
    Fortune,
}

/// How common a pick is in the deck, and therefore how much it is allowed to do.
public enum GrowthRarity
{
    Common,
    Rare,
}

/// In-run growth: kills buy levels, levels buy a choice, and gear decides how
/// many of each choice there are to make.
///
/// Everything here is lost when the run ends. What survives is the loot, the
/// practice, and the gear that came back — so the climb has to be re-made every
/// run, from a starting point the meta layer moved.
///
/// The offer does not pause the game. Choosing under pressure is the same design
/// as the search timer: the cost of a decision is the seconds it takes while the
/// horde keeps walking.
///
/// The pool used to be five options, all of them a number going up, and that is
/// the thing a survivors-like cannot be short of: with five, every run is the
/// same run in a different order, and the offer stops being a decision by the
/// third level. Seventeen with real rules in them — pierce, crit, ignite, thorns
/// — is what makes two runs on the same gear different runs.
public partial class RunGrowth : Node
{
    /// Kills for the first level, and how much each level adds to the price.
    /// Tuned so a weapon-focused player reaches their ceiling around 60% of the
    /// run — early enough that the last stretch is the horde growing alone,
    /// late enough that the climb is most of the run.
    [Export] public float BaseLevelCost { get; set; } = 12.0f;
    [Export] public float LevelCostStep { get; set; } = 5.0f;

    [Export] public float HealthPerPick { get; set; } = 12.0f;
    [Export] public float ArmourPerPick { get; set; } = 1.0f;
    [Export] public float MoveSpeedPerPick { get; set; } = 0.08f;
    [Export] public float SearchSpeedPerPick { get; set; } = 0.15f;

    [Export] public int OfferSize { get; set; } = 3;

    /// How much likelier a common pick is than a rare one. Rare picks do more
    /// per stack, so seeing one is the run's good news rather than its baseline.
    [Export] public float CommonWeight { get; set; } = 1.0f;
    [Export] public float RareWeight { get; set; } = 0.34f;

    public int Level { get; private set; }
    public float Experience { get; private set; }
    public float ExperienceForNext => BaseLevelCost + Level * LevelCostStep;

    /// Picks earned but not yet spent. Ignoring an offer stacks rather than
    /// wasting it — the player is allowed to decide that staying alive matters
    /// more this second than choosing well.
    public int PendingPicks { get; private set; }

    public GrowthOption[] Offer { get; private set; } = System.Array.Empty<GrowthOption>();
    public bool HasOffer => Offer.Length > 0;

    private static readonly int OptionCount = System.Enum.GetValues<GrowthOption>().Length;

    /// Picks taken per option, so a cap can be checked without asking the player
    /// what its stats used to be.
    private readonly int[] _taken = new int[OptionCount];
    private readonly int[] _caps = new int[OptionCount];

    private Horde? _horde;
    private Player? _player;
    private WeaponHandler? _weapons;
    private ulong _rng = 0xB5026F5AA96619E9UL;

    public override void _Ready()
    {
        _horde = GetParent().GetNodeOrNull<Horde>("Horde");
        _player = GetParent().GetNodeOrNull<Player>("Player");
        _weapons = _player?.GetNodeOrNull<WeaponHandler>("WeaponHandler");

        // Every option that gear does not gate gets its own ceiling here, so a
        // run cannot stack one rule into the only thing that matters. Gear
        // overwrites the four it owns in SetCaps.
        foreach (GrowthOption option in System.Enum.GetValues<GrowthOption>())
            _caps[(int)option] = DefaultCap(option);

        if (_horde != null)
            _horde.EnemyKilled += OnEnemyKilled;
        else
            GD.PushWarning("RunGrowth: no Horde sibling — nothing will grant experience");
    }

    public override void _ExitTree()
    {
        if (_horde != null)
            _horde.EnemyKilled -= OnEnemyKilled;
    }

    /// Caps come from the equipped gear, summed. Called by the meta layer once
    /// the loadout is known; until then every character option is unavailable,
    /// which is the honest state rather than a default nobody chose.
    public void SetCaps(int health, int armour, int speed, int search)
    {
        _caps[(int)GrowthOption.MaxHealth] = health;
        _caps[(int)GrowthOption.Armour] = armour;
        _caps[(int)GrowthOption.MoveSpeed] = speed;
        _caps[(int)GrowthOption.SearchSpeed] = search;
    }

    /// How many times a rule may be stacked.
    ///
    /// Low, and lower for the rules that compound. Four stacks of crit is a
    /// weapon that crits half the time; a fifth would make the number the whole
    /// build. The point of a ceiling here is the same as the weapon's: an option
    /// the player watches leave the deck is one they can plan around.
    private static int DefaultCap(GrowthOption option) => option switch
    {
        GrowthOption.WeaponLevel => 0,          // the weapon's own ceiling decides
        GrowthOption.MaxHealth or GrowthOption.Armour
            or GrowthOption.MoveSpeed or GrowthOption.SearchSpeed => 0,   // gear decides
        GrowthOption.Pierce => 3,
        GrowthOption.Crit => 4,
        GrowthOption.FireRate => 4,
        GrowthOption.Area => 3,
        GrowthOption.Knockback => 3,
        GrowthOption.Ignite => 2,
        GrowthOption.Detonate => 2,
        GrowthOption.Lifesteal => 3,
        GrowthOption.Regen => 3,
        GrowthOption.Dodge => 3,
        GrowthOption.Thorns => 3,
        GrowthOption.Reach => 3,
        GrowthOption.Fortune => 3,
        _ => 3,
    };

    public static GrowthRarity RarityOf(GrowthOption option) => option switch
    {
        GrowthOption.Crit or GrowthOption.Ignite or GrowthOption.Detonate
            or GrowthOption.Lifesteal or GrowthOption.Dodge or GrowthOption.Fortune => GrowthRarity.Rare,
        _ => GrowthRarity.Common,
    };

    private void OnEnemyKilled(int type, Vector3 position)
    {
        if (_horde == null || type < 0 || type >= _horde.Types.Length)
            return;

        Experience += _horde.Types[type].ExperienceValue;

        while (Experience >= ExperienceForNext)
        {
            Experience -= ExperienceForNext;
            Level++;
            PendingPicks++;
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!HasOffer && PendingPicks > 0)
            Offer = BuildOffer();

        if (!HasOffer)
            return;

        for (int i = 0; i < Offer.Length; i++)
        {
            if (Input.IsActionJustPressed($"pick_{i + 1}"))
            {
                Choose(i);
                return;
            }
        }
    }

    /// Takes the offered option at `index`. Public so a play-test and a tapped
    /// card drive the same path the keyboard does rather than reaching past it.
    public bool Choose(int index)
    {
        if (index < 0 || index >= Offer.Length)
            return false;

        Apply(Offer[index]);
        _taken[(int)Offer[index]]++;
        PendingPicks--;
        Offer = System.Array.Empty<GrowthOption>();
        return true;
    }

    private void Apply(GrowthOption option)
    {
        RunModifiers? mods = _player?.Mods;

        switch (option)
        {
            case GrowthOption.WeaponLevel: _weapons?.AddRunUpgrade(); break;
            case GrowthOption.MaxHealth: _player?.AddMaxHealth(HealthPerPick); break;
            case GrowthOption.Armour: _player?.AddArmour(ArmourPerPick); break;
            case GrowthOption.MoveSpeed: _player?.AddMoveSpeedFraction(MoveSpeedPerPick); break;
            case GrowthOption.SearchSpeed: _player?.AddSearchSpeedFraction(SearchSpeedPerPick); break;

            case GrowthOption.Pierce: if (mods != null) mods.Pierce += 1; break;
            case GrowthOption.Crit: if (mods != null) mods.CritChance += 0.12f; break;
            case GrowthOption.FireRate: if (mods != null) mods.AttackDelayScale *= 0.88f; break;
            case GrowthOption.Area: if (mods != null) mods.AreaScale *= 1.18f; break;
            case GrowthOption.Knockback: if (mods != null) mods.Knockback += 0.5f; break;
            case GrowthOption.Ignite: if (mods != null) mods.IgniteChance += 0.10f; break;
            case GrowthOption.Detonate: if (mods != null) mods.DetonateChance += 0.09f; break;
            case GrowthOption.Lifesteal: if (mods != null) mods.Lifesteal += 0.5f; break;
            case GrowthOption.Regen: if (mods != null) mods.Regen += 0.8f; break;
            case GrowthOption.Dodge: if (mods != null) mods.Dodge += 0.10f; break;
            case GrowthOption.Thorns: if (mods != null) mods.Thorns += 4.0f; break;
            case GrowthOption.Reach: if (mods != null) mods.SearchRadiusBonus += 0.8f; break;
            case GrowthOption.Fortune: if (mods != null) mods.LootValueScale += 0.15f; break;
        }
    }

    /// An option is available until its ceiling is reached, and then it stops
    /// being offered. That is the point: a ceiling the player watches empty out
    /// of the deck is one they can plan around, unlike a number in a formula.
    public bool IsAvailable(GrowthOption option) => option == GrowthOption.WeaponLevel
        ? _weapons is { Weapon: not null } && !_weapons.AtCeiling
        : _taken[(int)option] < _caps[(int)option];

    public int TakenCount(GrowthOption option) => _taken[(int)option];

    /// Applies an option directly, as taking it would. For probes: the effect of
    /// a card and the deck that offers it are separate questions, and testing the
    /// first through the second means every measurement waits on a random draw.
    public void GrantForTesting(GrowthOption option)
    {
        Apply(option);
        _taken[(int)option]++;
    }

    /// One offer, without spending a pick. For probes measuring the shape of the
    /// deck rather than what a card does.
    public GrowthOption[] PeekOffer() => BuildOffer();

    private GrowthOption[] BuildOffer()
    {
        System.Span<GrowthOption> pool = stackalloc GrowthOption[OptionCount];
        System.Span<float> weights = stackalloc float[OptionCount];
        int count = 0;
        float total = 0.0f;

        for (int i = 0; i < OptionCount; i++)
        {
            var option = (GrowthOption)i;
            if (!IsAvailable(option))
                continue;

            float weight = RarityOf(option) == GrowthRarity.Rare ? RareWeight : CommonWeight;
            pool[count] = option;
            weights[count] = weight;
            total += weight;
            count++;
        }

        if (count == 0)
        {
            // Everything is capped. Drop the pick rather than holding a choice
            // that can never be made — otherwise the HUD advertises an offer the
            // player cannot dismiss.
            PendingPicks = 0;
            return System.Array.Empty<GrowthOption>();
        }

        int size = Mathf.Min(OfferSize, count);
        var offer = new GrowthOption[size];

        // Weighted draw without replacement: pick by weight, then swap the taken
        // entry to the front and shrink the range. Rejection sampling would work
        // too and would spin when the deck is nearly empty, which is exactly when
        // the player is watching it.
        for (int i = 0; i < size; i++)
        {
            float roll = NextFloat() * total;
            int pick = i;

            for (int n = i; n < count; n++)
            {
                roll -= weights[n];
                if (roll <= 0.0f)
                {
                    pick = n;
                    break;
                }
            }

            (pool[i], pool[pick]) = (pool[pick], pool[i]);
            (weights[i], weights[pick]) = (weights[pick], weights[i]);
            total -= weights[i];
            offer[i] = pool[i];
        }

        return offer;
    }

    public string Describe(GrowthOption option) => option switch
    {
        GrowthOption.WeaponLevel => $"weapon +1 ({_weapons?.Level ?? 0}/{_weapons?.MaxLevel ?? 0})",
        GrowthOption.MaxHealth => $"+{HealthPerPick:F0} max HP",
        GrowthOption.Armour => $"+{ArmourPerPick:F0} armour",
        GrowthOption.MoveSpeed => $"+{MoveSpeedPerPick * 100.0f:F0}% speed",
        GrowthOption.SearchSpeed => $"+{SearchSpeedPerPick * 100.0f:F0}% search",
        GrowthOption.Pierce => "+1 pierce",
        GrowthOption.Crit => "+12% crit",
        GrowthOption.FireRate => "+12% fire rate",
        GrowthOption.Area => "+18% area",
        GrowthOption.Knockback => "+knockback",
        GrowthOption.Ignite => "10% kills ignite",
        GrowthOption.Detonate => "9% kills detonate",
        GrowthOption.Lifesteal => "+0.5 HP per kill",
        GrowthOption.Regen => "+0.8 HP/s",
        GrowthOption.Dodge => "+10% dodge",
        GrowthOption.Thorns => "+4 thorns",
        GrowthOption.Reach => "+0.8 m search reach",
        GrowthOption.Fortune => "+15% loot value",
        _ => option.ToString(),
    };

    private float NextFloat()
    {
        _rng ^= _rng << 13;
        _rng ^= _rng >> 7;
        _rng ^= _rng << 17;
        return (_rng >> 40) / 16777216.0f;
    }
}
