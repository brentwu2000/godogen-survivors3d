using Godot;

public enum GrowthOption
{
    WeaponLevel,
    MaxHealth,
    Armour,
    MoveSpeed,
    SearchSpeed,
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

    public int Level { get; private set; }
    public float Experience { get; private set; }
    public float ExperienceForNext => BaseLevelCost + Level * LevelCostStep;

    /// Picks earned but not yet spent. Ignoring an offer stacks rather than
    /// wasting it — the player is allowed to decide that staying alive matters
    /// more this second than choosing well.
    public int PendingPicks { get; private set; }

    public GrowthOption[] Offer { get; private set; } = System.Array.Empty<GrowthOption>();
    public bool HasOffer => Offer.Length > 0;

    /// Picks taken per option, so a cap can be checked without asking the player
    /// what its stats used to be.
    private readonly int[] _taken = new int[5];
    private readonly int[] _caps = new int[5];

    private Horde? _horde;
    private Player? _player;
    private WeaponHandler? _weapons;
    private ulong _rng = 0xB5026F5AA96619E9UL;

    public override void _Ready()
    {
        _horde = GetParent().GetNodeOrNull<Horde>("Horde");
        _player = GetParent().GetNodeOrNull<Player>("Player");
        _weapons = _player?.GetNodeOrNull<WeaponHandler>("WeaponHandler");

        if (_horde != null)
            _horde.EnemyKilled += OnEnemyKilled;
        else
            GD.PushWarning("RunGrowth: no Horde sibling — nothing will grant experience");
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

    /// Takes the offered option at `index`. Public so a play-test can drive the
    /// same path the player does rather than reaching past it.
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
        switch (option)
        {
            case GrowthOption.WeaponLevel: _weapons?.AddRunUpgrade(); break;
            case GrowthOption.MaxHealth: _player?.AddMaxHealth(HealthPerPick); break;
            case GrowthOption.Armour: _player?.AddArmour(ArmourPerPick); break;
            case GrowthOption.MoveSpeed: _player?.AddMoveSpeedFraction(MoveSpeedPerPick); break;
            case GrowthOption.SearchSpeed: _player?.AddSearchSpeedFraction(SearchSpeedPerPick); break;
        }
    }

    /// An option is available until its ceiling is reached, and then it stops
    /// being offered. That is the point: a ceiling the player watches empty out
    /// of the deck is one they can plan around, unlike a number in a formula.
    public bool IsAvailable(GrowthOption option) => option == GrowthOption.WeaponLevel
        ? _weapons is { Weapon: not null } && !_weapons.AtCeiling
        : _taken[(int)option] < _caps[(int)option];

    private GrowthOption[] BuildOffer()
    {
        System.Span<GrowthOption> pool = stackalloc GrowthOption[5];
        int count = 0;

        for (int i = 0; i < 5; i++)
        {
            var option = (GrowthOption)i;
            if (IsAvailable(option))
                pool[count++] = option;
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

        // Partial Fisher-Yates over the pool: distinct options, no rejection
        // loop, and deterministic so a capture reproduces the same deck.
        for (int i = 0; i < size; i++)
        {
            int pick = i + (int)(NextFloat() * (count - i));
            pick = Mathf.Clamp(pick, i, count - 1);
            (pool[i], pool[pick]) = (pool[pick], pool[i]);
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
