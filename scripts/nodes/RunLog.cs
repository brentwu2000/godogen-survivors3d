using Godot;

/// Watches the run and remembers what happened. Subscribes to everything and
/// changes nothing.
///
/// It exists because the run already produces every fact the debrief and the
/// contracts need, and then throws all of them away: `EnemyKilled` carries a
/// variant nobody counts, `Emptied` carries a value nobody totals, and the
/// closest the player came to dying is only ever a number that briefly appeared
/// on a bar. A run of three hundred seconds currently collapses into one integer.
///
/// One log rather than statistics gathered where they are needed. Two counts of
/// the same thing drift, and a contract that disagrees with the screen reporting
/// it is a contract the player is right to distrust.
public partial class RunLog : Node
{
    private Horde? _horde;
    private Player? _player;
    private RunDirector? _director;
    private WeaponHandler? _weapons;

    private int[] _killsByType = System.Array.Empty<int>();
    private int _cratesLooted;
    private int _lootValue;
    private int _itemsUsed;
    private int _itemsThrown;
    private float _lowestHealth = float.MaxValue;

    public override void _Ready()
    {
        Node? root = GetParent();
        _horde = root?.GetNodeOrNull<Horde>("Horde");
        _player = root?.GetNodeOrNull<Player>("Player");
        _director = root?.GetNodeOrNull<RunDirector>("RunDirector");
        _weapons = _player?.GetNodeOrNull<WeaponHandler>("WeaponHandler");

        _killsByType = new int[_horde?.Types.Length ?? 0];

        if (_horde != null)
            _horde.EnemyKilled += OnEnemyKilled;

        if (_player != null)
        {
            _player.ItemUsed += OnItemUsed;
            _player.ItemThrown += OnItemThrown;
        }

        Node? crates = root?.GetNodeOrNull("LootContainers");
        if (crates != null)
        {
            foreach (Node child in crates.GetChildren())
            {
                if (child is LootContainer container)
                    container.Emptied += OnCrateEmptied;
            }
        }
    }

    /// The horde's and the player's events are plain C# delegates, which hold a
    /// strong reference to this node — a subscription outliving the scene is a
    /// call into a freed object.
    public override void _ExitTree()
    {
        if (_horde != null)
            _horde.EnemyKilled -= OnEnemyKilled;

        if (_player != null)
        {
            _player.ItemUsed -= OnItemUsed;
            _player.ItemThrown -= OnItemThrown;
        }
    }

    /// Sampled rather than event-driven, for the same reason the hit sound is:
    /// contact damage is a rate, so there is no moment at which health "became"
    /// its lowest — only a series of ticks, one of which was the worst.
    public override void _PhysicsProcess(double delta)
    {
        if (_director is { State: not RunState.Running } || _player == null)
            return;

        if (_player.Health < _lowestHealth)
            _lowestHealth = _player.Health;
    }

    private void OnEnemyKilled(int type, Vector3 position)
    {
        if (type >= 0 && type < _killsByType.Length)
            _killsByType[type]++;
    }

    private void OnCrateEmptied(int value)
    {
        _cratesLooted++;
        _lootValue += value;
    }

    /// Only what the player spent on themselves. A thrown charge is counted
    /// separately because the two answer different questions — one is "did you
    /// need help staying alive", the other is "did you use the bag as a weapon".
    private void OnItemUsed(string itemName) => _itemsUsed++;

    private void OnItemThrown(string itemName) => _itemsThrown++;

    /// Assembles the record. Everything the log could not see — what the meta
    /// layer decided to bank, what practice it granted, what death took — is
    /// passed in, because those are decisions made after the run ended and the
    /// log has no business guessing at them.
    public RunRecord Freeze(RunState outcome, int banked, int[] proficiencyGained, string[] lostEquipment)
    {
        var kills = new int[_killsByType.Length];
        System.Array.Copy(_killsByType, kills, kills.Length);

        return new RunRecord
        {
            Outcome = outcome,
            Seconds = _director?.Elapsed ?? 0.0f,
            Banked = banked,
            Multiplier = _director?.ExtractionMultiplier ?? 1.0f,
            BackpackValue = _player?.Backpack.TotalValue ?? 0,
            SafeBoxValue = _player?.SafeBox.TotalValue ?? 0,
            KillsByType = kills,
            CratesLooted = _cratesLooted,
            LootValue = _lootValue,

            // A run that ended before the player was ever touched has no minimum
            // to report, so it reports full health rather than float.MaxValue.
            LowestHealth = _lowestHealth == float.MaxValue ? (_player?.MaxHealth ?? 0.0f) : _lowestHealth,
            MaxHealth = _player?.MaxHealth ?? 0.0f,
            ItemsUsed = _itemsUsed,
            ItemsThrown = _itemsThrown,
            ProficiencyGained = proficiencyGained,
            LostEquipment = lostEquipment,
        };
    }

    /// Variant names for the readout, in table order. Here rather than in the
    /// screen so the log stays the single source of what a run contained.
    public string TypeName(int index) =>
        _horde != null && index >= 0 && index < _horde.Types.Length
            ? _horde.Types[index].TypeName
            : index.ToString();

    public int TypeCount => _killsByType.Length;
}
