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

    // Kit rather than numbers or rules. The deck had two kinds of card: a bigger
    // number, or a rule about how the weapon resolves — and both of them were
    // still the weapon. These four fight on their own, so a run can be built
    // around something the weapon does not do, and a dry magazine stops being
    // the end of the run.
    Orbit,
    Shockwave,
    Chain,
    Chill,
}

/// How common a pick is in the deck, and therefore how much it is allowed to do.
public enum GrowthRarity
{
    Common,
    Rare,

    /// A piece of kit. Rarer than a rule, because one of these changes what the
    /// run *is* rather than how well it does the same thing — and because four
    /// new entries at common weight would have quietly halved how often the
    /// weapon itself came up.
    Kit,
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
    /// Experience for the first level, and how much each one adds.
    ///
    /// Was 12 + 5n, and the measurement said the deck was five times the size of
    /// the run: a median run reached **level 3, spent 3 picks, and left its
    /// weapon at 1 of 8** — against a deck of twenty-two options whose ceilings
    /// sum to about fifty. Not one run in five ever saw the top of its weapon
    /// curve.
    ///
    /// Nothing was wrong with the weights. The run was too short to spend them,
    /// so every run was the same three cards and the whole growth layer was
    /// decoration. At 6 + 1.2n the twelfth level costs 19 instead of 67.
    [Export] public float BaseLevelCost { get; set; } = 6.0f;
    [Export] public float LevelCostStep { get; set; } = 1.2f;

    [Export] public float HealthPerPick { get; set; } = 12.0f;
    [Export] public float ArmourPerPick { get; set; } = 1.0f;
    [Export] public float MoveSpeedPerPick { get; set; } = 0.08f;
    [Export] public float SearchSpeedPerPick { get; set; } = 0.15f;

    [Export] public int OfferSize { get; set; } = 3;

    /// How much likelier a common pick is than a rare one. Rare picks do more
    /// per stack, so seeing one is the run's good news rather than its baseline.
    [Export] public float CommonWeight { get; set; } = 1.0f;
    [Export] public float RareWeight { get; set; } = 0.34f;

    /// Kit is rarer than a rule and much rarer than a number.
    [Export] public float KitWeight { get; set; } = 0.9f;

    /// The weapon draws heavily, and has to.
    ///
    /// Every other option is a modifier on top of a weapon that has to be able to
    /// kill things; a deck where the weapon is one row of twenty-one produces
    /// runs with eight rules and a starting rifle. At 4.8 against eighteen other
    /// entries it is about a fifth of draws, which is where it was before the kit
    /// cards diluted it.
    [Export] public float WeaponWeight { get; set; } = 4.8f;

    public int Level { get; private set; }
    public float Experience { get; private set; }
    public float ExperienceForNext => BaseLevelCost + Level * LevelCostStep;

    /// Everything ever earned this run, never spent. `Experience` is the progress
    /// bar and goes down every time it fills, so it cannot answer "was that kill
    /// worth more than the last one" — the honest answer there is negative about
    /// once a minute.
    public float ExperienceEarned { get; private set; }

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

        // The baseline, so a run cannot stack one rule into the only thing that
        // matters. Written into _caps rather than into the gear layer, so a
        // MetaManager that has already spoken is not overwritten by this — the
        // two are readied in scene order and this node currently happens to come
        // first, which is not a thing to depend on.
        foreach (GrowthOption option in System.Enum.GetValues<GrowthOption>())
            _caps[(int)option] = DefaultCap(option);

        _meta = GetParent()?.GetNodeOrNull<MetaManager>("MetaManager");

        if (_horde != null)
            _horde.KillDetail += OnEnemyKilled;
        else
            GD.PushWarning("RunGrowth: no Horde sibling — nothing will grant experience");
    }

    public override void _ExitTree()
    {
        if (_horde != null)
            _horde.KillDetail -= OnEnemyKilled;
    }

    /// Caps come from the equipped gear, summed. Called by the meta layer once
    /// the loadout is known; until then every character option is unavailable,
    /// which is the honest state rather than a default nobody chose.
    /// Everything the equipped set has to say about ceilings, in one call.
    ///
    /// It clears first, so this is a complete statement rather than a delta.
    /// Two calls that each wrote only what they knew about left the previous
    /// loadout's opinions in place — a bandolier taken off still granted five
    /// pierce — and the reason that was invisible is that the real caller runs
    /// exactly once per run. It took a probe wearing two sets in one scene to
    /// see it, and a save-and-swap feature would have found it the same way.
    ///
    /// Only what the gear names is set. Everything else keeps `DefaultCap`,
    /// which is what makes a piece legible: a bandolier saying pierce goes to
    /// five and fortune goes to zero is a statement about two options, not a
    /// silent re-authoring of all eighteen.
    public void SetCaps(int health, int armour, int speed, int search,
                        System.Collections.Generic.Dictionary<GrowthOption, int> rules)
    {
        System.Array.Fill(_gearCaps, -1);

        _gearCaps[(int)GrowthOption.MaxHealth] = health;
        _gearCaps[(int)GrowthOption.Armour] = armour;
        _gearCaps[(int)GrowthOption.MoveSpeed] = speed;
        _gearCaps[(int)GrowthOption.SearchSpeed] = search;

        foreach (var pair in rules)
            _gearCaps[(int)pair.Key] = pair.Value;
    }

    /// What the gear said, where it said anything, and the baseline elsewhere.
    ///
    /// A second array rather than overwriting the first, because the two are
    /// filled by two nodes whose _Ready order is scene order. With one array the
    /// game is correct only while RunGrowth sits above MetaManager in Main.tscn,
    /// and the symptom of moving it is every gear ceiling silently reverting to
    /// the default — a run that plays almost right.
    public int CapFor(GrowthOption option) =>
        _gearCaps[(int)option] >= 0 ? _gearCaps[(int)option] : _caps[(int)option];

    private readonly int[] _gearCaps = Filled(-1);

    private static int[] Filled(int value)
    {
        var array = new int[OptionCount];
        System.Array.Fill(array, value);
        return array;
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

        // Kit caps are low. Each stack is a visible object in the world doing
        // something on its own, and five blades already draw a ring the player
        // fights inside — a tenth would be a build with no decisions left in it.
        GrowthOption.Orbit => 5,
        GrowthOption.Shockwave => 4,
        GrowthOption.Chain => 4,

        // Three, because chill compounds. A fourth stack would be most of the way
        // to stopping a walker dead, and an enemy that cannot move is not a
        // threat that has been managed, it is a free kill standing still.
        GrowthOption.Chill => 3,

        _ => 3,
    };

    public static GrowthRarity RarityOf(GrowthOption option) => option switch
    {
        GrowthOption.Orbit or GrowthOption.Shockwave
            or GrowthOption.Chain or GrowthOption.Chill => GrowthRarity.Kit,

        GrowthOption.Crit or GrowthOption.Ignite or GrowthOption.Detonate
            or GrowthOption.Lifesteal or GrowthOption.Dodge or GrowthOption.Fortune => GrowthRarity.Rare,

        _ => GrowthRarity.Common,
    };

    /// How heavily an option is drawn.
    ///
    /// The weapon is named separately rather than given a rarity of its own,
    /// because rarity here means "how much is this allowed to do" and the weapon
    /// is not allowed to do more than the others — it just has to keep turning up.
    public float WeightOf(GrowthOption option) => option == GrowthOption.WeaponLevel
        ? WeaponWeight
        : RarityOf(option) switch
        {
            GrowthRarity.Kit => KitWeight,
            GrowthRarity.Rare => RareWeight,
            _ => CommonWeight,
        };

    private void OnEnemyKilled(int type, byte elite, Vector3 position)
    {
        if (_horde == null || type < 0 || type >= _horde.Types.Length)
            return;

        // The elite multiplier is applied here rather than baked into the
        // variant row, because it is the mark that earned it and the same
        // walker is worth one or four depending on nothing else.
        float gained = _horde.Types[type].ExperienceValue * Elites.ExperienceScale(elite);
        Experience += gained;
        ExperienceEarned += gained;

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

            case GrowthOption.Orbit: if (mods != null) mods.OrbitBlades += 1; break;
            case GrowthOption.Shockwave: if (mods != null) mods.PulseStacks += 1; break;
            case GrowthOption.Chain: if (mods != null) mods.ChainChance += 0.18f; break;

            // Multiplicative, so stacking approaches a limit instead of crossing
            // it. Additive at 17% a stack, three stacks is 51% and a fourth would
            // be most of the way to stopping a walker dead — and an enemy that
            // cannot move is not a threat managed, it is a free kill standing
            // still.
            case GrowthOption.Chill:
                if (mods != null)
                    mods.Chill = 1.0f - (1.0f - mods.Chill) * 0.83f;

                break;
        }
    }

    /// An option is available until its ceiling is reached, and then it stops
    /// being offered. That is the point: a ceiling the player watches empty out
    /// of the deck is one they can plan around, unlike a number in a formula.
    public bool IsAvailable(GrowthOption option)
    {
        // Locked options are not in the deck at all rather than being offered and
        // refused. A card the player can see and cannot take teaches them the
        // condition, which is right in the shop where they are browsing — and
        // wrong mid-run, where the offer is three seconds long and they are being
        // chased.
        if (!Unlocked(option))
            return false;

        return option == GrowthOption.WeaponLevel
            ? _weapons is { Weapon: not null } && !_weapons.AtCeiling
            : _taken[(int)option] < CapFor(option);
    }

    /// The profile, when there is one. A probe that builds a RunGrowth without a
    /// MetaManager gets the whole deck, which is the right default: the thing
    /// being tested there is the deck, and half of it silently missing would look
    /// exactly like a broken draw.
    private bool Unlocked(GrowthOption option) =>
        _meta == null || UnlockBook.GrowthAllows(_meta.Profile, option);

    private MetaManager? _meta;

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

            float weight = WeightOf(option);
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
        GrowthOption.Orbit => "+1 orbiting blade",
        GrowthOption.Shockwave => "+1 shockwave stack",
        GrowthOption.Chain => "+18% chance a hit arcs",
        GrowthOption.Chill => "enemies near you slow down",
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
