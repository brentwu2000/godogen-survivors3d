using Godot;

/// A searchable crate. The player has to stand still next to it while a timer
/// fills — that stationary window is the cost, because the horde does not stop.
public partial class LootContainer : Node3D
{
    [Export] public float SearchRadius { get; set; } = 1.8f;
    [Export] public float SearchSeconds { get; set; } = 2.5f;
    [Export] public int RollCount { get; set; } = 3;

    /// Multiplies an item's draw weight once per rarity step. 1 is the flat
    /// table; above it, the rare tail gets heavier. Set by the level from how
    /// far out the crate sits, so walking deeper is worth the walk.
    [Export] public float RarityBias { get; set; } = 1.0f;

    /// Progress resets rather than pauses when the player steps away, so a
    /// contested search cannot be done in safe nibbles.
    [Export] public bool ResetOnLeave { get; set; } = true;

    /// Named Emptied rather than Looted: the source generator turns a signal into
    /// a member of the same name, which would collide with the Looted state flag.
    [Signal] public delegate void EmptiedEventHandler(int value);

    public float Progress { get; private set; }
    public bool Looted { get; private set; }
    public bool PlayerInRange { get; private set; }

    private Player? _player;
    private ItemResource[] _table = System.Array.Empty<ItemResource>();
    private float _weightTotal;
    private ulong _rng;

    public override void _Ready()
    {
        _player = GetTree().Root.FindChild("Player", recursive: true, owned: false) as Player;

        // Seeded from the spawn position so each crate rolls differently but the
        // same crate rolls the same way on a replay.
        _rng = (ulong)(Position.X * 7919.0f) ^ ((ulong)(Position.Z * 104729.0f) << 21) ^ 0x2545F4914F6CDD1DUL;
        if (_rng == 0)
            _rng = 0x9E3779B97F4A7C15UL;

        LoadTable();
    }

    private void LoadTable()
    {
        using var dir = DirAccess.Open("res://resources/items");
        if (dir == null)
        {
            GD.PushWarning("LootContainer: res://resources/items missing — run BuildItems.cs");
            return;
        }

        string[] files = dir.GetFiles();
        var loaded = new System.Collections.Generic.List<ItemResource>(files.Length);
        foreach (string file in files)
        {
            // Exported projects rewrite .tres to .remap; strip it or nothing
            // loads outside the editor.
            string name = file.EndsWith(".remap") ? file[..^6] : file;
            if (!name.EndsWith(".tres"))
                continue;

            var item = GD.Load<ItemResource>($"res://resources/items/{name}");
            if (item != null)
                loaded.Add(item);
        }

        _table = loaded.ToArray();

        // Biased weights are computed once here rather than per roll: the bias
        // never changes after the level places the crate, and PickWeighted runs
        // three times for every search.
        _weights = new float[_table.Length];
        for (int i = 0; i < _table.Length; i++)
        {
            _weights[i] = _table[i].Weight * Mathf.Pow(RarityBias, (int)_table[i].Rarity);
            _weightTotal += _weights[i];
        }
    }

    private float[] _weights = System.Array.Empty<float>();
    private float _valueScale = 1.0f;

    public override void _PhysicsProcess(double delta)
    {
        if (Looted || _player == null || !_player.IsAlive)
            return;

        PlayerInRange = GlobalPosition.DistanceTo(_player.GlobalPosition)
                        <= SearchRadius + _player.Mods.SearchRadiusBonus;

        if (!PlayerInRange)
        {
            if (ResetOnLeave)
                Progress = 0.0f;
            return;
        }

        Progress += (float)delta * _player.SearchSpeed / Mathf.Max(0.01f, SearchSeconds);
        if (Progress < 1.0f)
            return;

        Progress = 1.0f;
        Looted = true;
        // Read once, here, rather than inside the roll: the value multiplier is
        // the player's and the roll only knows about items.
        _valueScale = _player.Mods.LootValueScale;
        EmitSignal(SignalName.Emptied, RollInto(_player.Backpack));
    }

    /// Returns the value actually taken — a full backpack means the crate is
    /// still emptied but the loot is left behind, which is the honest outcome.
    private int RollInto(Inventory backpack)
    {
        if (_table.Length == 0 || _weightTotal <= 0.0f)
            return 0;

        int gained = 0;
        for (int roll = 0; roll < RollCount; roll++)
        {
            ItemResource item = PickWeighted();
            int stack = item.MinStack + (int)(NextFloat() * (item.MaxStack - item.MinStack + 1));
            stack = Mathf.Clamp(stack, item.MinStack, item.MaxStack);
            gained += Mathf.RoundToInt(backpack.TryAdd(item, stack) * item.Value * _valueScale);
        }

        return gained;
    }

    private ItemResource PickWeighted()
    {
        float pick = NextFloat() * _weightTotal;
        for (int i = 0; i < _table.Length; i++)
        {
            pick -= _weights[i];
            if (pick <= 0.0f)
                return _table[i];
        }

        return _table[^1];
    }

    private float NextFloat()
    {
        _rng ^= _rng << 13;
        _rng ^= _rng >> 7;
        _rng ^= _rng << 17;
        return (_rng >> 40) / 16777216.0f;
    }
}
