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

    /// Whether this container can hold curiosities.
    ///
    /// True for crates the level placed, false for anything dropped during a run.
    /// A cache is a *payout* — it owes ammunition and consumables, because the
    /// player earned it by fighting and is still fighting. The first zone cache
    /// after the curiosities shipped handed over four collectibles and three
    /// supplies, which is a reward that makes the next five minutes harder.
    ///
    /// Set-piece items are also the wrong shape for a reward: at two bulk each
    /// they fill a backpack the player needs for the rest of the run, and the
    /// bounty they lead to arrives at the base rather than now.
    [Export] public bool Curiosities { get; set; } = true;

    /// Progress resets rather than pauses when the player steps away, so a
    /// contested search cannot be done in safe nibbles.
    [Export] public bool ResetOnLeave { get; set; } = true;

    /// Named Emptied rather than Looted: the source generator turns a signal into
    /// a member of the same name, which would collide with the Looted state flag.
    ///
    /// Two arguments. `finished` is false when the backpack filled and the crate
    /// still holds something — the run log has to value every visit and count the
    /// crate once, and with one argument it could do only one of those.
    [Signal] public delegate void EmptiedEventHandler(int value, bool finished);

    public float Progress { get; private set; }
    public bool Looted { get; private set; }
    public bool PlayerInRange { get; private set; }

    /// Which of the two shapes this is. The level scatters crates; the director
    /// and the boss drop caches.
    [Export] public LootLibrary.Look Look { get; set; } = LootLibrary.Look.Crate;

    private Node3D? _lid;

    /// What was rolled and would not fit.
    ///
    /// The crate keeps it. Previously a full backpack emptied the container and
    /// destroyed the overflow, which made carrying capacity a silent tax rather
    /// than a decision — the player never learned what they had lost, so there
    /// was nothing to weigh and no reason to drop anything.
    ///
    /// Rolled once. Coming back must not re-roll: a crate that rerolled would let
    /// a player with a full bag farm one container for the item they wanted.
    private Inventory? _remains;

    /// Bulk still waiting in this crate, and what it is worth. The readout asks,
    /// because "your bag is full" is only actionable next to "and this is what is
    /// sitting here".
    public int RemainingBulk => _remains?.UsedBulk ?? 0;
    public int RemainingValue => _remains?.TotalValue ?? 0;

    /// Builds the box and the lid that will open on it.
    ///
    /// Here rather than at the two call sites, each of which hand-rolled a
    /// `BoxMesh` with **no material on it at all** — that is the white cube in
    /// every screenshot ever taken of this game, including the ones used to
    /// judge the ground shader, the fog, the bodies and every biome. It survived
    /// because no probe asks what a thing looks like, and because a cube reads
    /// as "placeholder for something", which is a category the eye skips.
    ///
    /// Two call sites meant two cubes and a third would have been a third, so
    /// the container owns its own appearance now. Called from `_Ready` rather
    /// than from a constructor because the tier is read off `RarityBias`, and
    /// the generator assigns that before the node enters the tree.
    private void BuildBody()
    {
        // A container built with `new` for a test never enters the tree and
        // never gets here, which is correct — `RollIntoForTesting` wants a table
        // and not a mesh.
        // The meshes arrive with their material already on them and are shared
        // between every container of the same look and tier — six meshes for the
        // whole game rather than two per crate rebuilt on every regeneration,
        // which the base screen triggers on each biome change.
        int tier = LootLibrary.TierFor(RarityBias);

        AddChild(new MeshInstance3D { Name = "Mesh", Mesh = LootLibrary.Body(Look, tier) });

        // The lid hangs on its own node at the hinge, so opening it is a
        // rotation rather than a swap to a second mesh.
        _lid = new Node3D { Name = "Lid", Position = LootLibrary.Hinge(Look) };
        _lid.AddChild(new MeshInstance3D { Name = "Mesh", Mesh = LootLibrary.Lid(Look, tier) });
        AddChild(_lid);

        if (Looted)
            ShowOpen();
    }

    /// Stands the lid open.
    ///
    /// An emptied crate looked exactly like a full one, from anywhere, for as
    /// long as crates have existed — nothing in this file ever touched its mesh.
    /// The minimap knew, and the minimap is a nine-centimetre square in the
    /// corner of the screen. This puts the same information where the player is
    /// already looking, which is the difference between an arena you are working
    /// through and an arena you are wandering around.
    private void ShowOpen()
    {
        if (_lid != null)
            _lid.Rotation = new Vector3(LootLibrary.OpenAngle, 0.0f, 0.0f);
    }

    private Player? _player;
    private ItemResource[] _table = System.Array.Empty<ItemResource>();
    private float _weightTotal;
    private ulong _rng;

    public override void _Ready()
    {
        BuildBody();

        _player = GetTree().Root.FindChild("Player", recursive: true, owned: false) as Player;

        // Seeded from the spawn position so each crate rolls differently but the
        // same crate rolls the same way on a replay.
        Seed();

        LoadTable();
    }

    /// The item table, read from disk once per process rather than once per
    /// crate. Eleven crates in the densest biome meant eleven directory scans and
    /// eleven loads of the same nine resources during level generation — cached
    /// by the engine after the first, and still eleven trips through the file
    /// system on a frame the player is waiting on.
    ///
    /// Static because the table is the same for every crate; the *weights* are
    /// not, because those depend on how far out the crate sits.
    private static ItemResource[]? _sharedTable;

    private static ItemResource[] SharedTable()
    {
        if (_sharedTable != null)
            return _sharedTable;

        using var dir = DirAccess.Open("res://resources/items");
        if (dir == null)
        {
            GD.PushWarning("LootContainer: res://resources/items missing — run BuildItems.cs");
            return System.Array.Empty<ItemResource>();
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

        _sharedTable = loaded.ToArray();
        return _sharedTable;
    }

    private void LoadTable()
    {
        _table = SharedTable();

        // Reset, not accumulated. `_weightTotal` is a running sum below, and
        // this method is called from `_Ready` and now also on demand — called
        // twice it would double every weight total and halve the effective
        // rarity bias, silently.
        _weightTotal = 0.0f;

        // Biased weights are computed once here rather than per roll: the bias
        // never changes after the level places the crate, and PickWeighted runs
        // three times for every search.
        _weights = new float[_table.Length];
        for (int i = 0; i < _table.Length; i++)
        {
            // Weight zero rather than a filtered table, so the shared table stays
            // shared. Building a second array per crate would undo the caching
            // this method exists to do.
            _weights[i] = Curiosities || CollectionBook.SetOf(_table[i].ItemName) < 0
                ? _table[i].Weight * Mathf.Pow(RarityBias, (int)_table[i].Rarity)
                : 0.0f;

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

        // Read once, here, rather than inside the roll: the value multiplier is
        // the player's and the roll only knows about items.
        _valueScale = _player.Mods.LootValueScale;

        // Rolled on the first search only. Afterwards the crate is a pile with a
        // known content, and coming back moves what fits.
        _remains ??= RollAll();

        int taken = Transfer(_remains, _player.Backpack);
        AnnounceCuriosities();
        bool wasLooted = Looted;
        Looted = _remains.EntryCount == 0;

        // The moment it empties, not a frame later. `Looted` is also read by
        // `_PhysicsProcess` to stop searching, so this is the only place the
        // transition happens.
        if (Looted && !wasLooted)
            ShowOpen();

        // Reset rather than held at 1. Held, the search completes again on the
        // very next tick against a bag that is still full, and the crate emits a
        // zero-value haul sixty times a second for as long as the player stands
        // near it.
        Progress = 0.0f;

        // Silent when nothing moved. A player standing on a crate with a full bag
        // completes the search over and over, and announcing a haul of zero each
        // time would chime, log and total nothing repeatedly — the probe saw
        // fourteen payouts for one crate. The crate is still *searchable*; it just
        // has nothing to say until the player makes room.
        if (taken > 0 || Looted)
            EmitSignal(SignalName.Emptied, taken, Looted);
    }

    /// Calls out any piece of a collection that just arrived in the bag.
    ///
    /// **The moment it matters is the pickup, not the debrief.** A curiosity was
    /// recorded when it reached the stash and reported on the base screen an hour
    /// later, so during the run it was an item with a name and a low value among
    /// twenty others — nothing marked it, nothing said which set it belonged to,
    /// and the decision the collection is supposed to create ("do I carry this or
    /// the ammunition?") was made without the player knowing there was one.
    ///
    /// Compared against the bag before and after, rather than against what the
    /// crate rolled: a piece the bag had no room for was never picked up, and
    /// announcing it would be announcing something the player does not have.
    private void AnnounceCuriosities()
    {
        // The HUD is a sibling of this crate's parent rather than of the crate,
        // and it is looked up lazily because the crate is created by the level
        // generator before the HUD exists.
        _hud ??= GetParent()?.GetParent()?.GetNodeOrNull<Hud>("Hud");

        if (_player == null || _hud == null)
            return;

        Inventory bag = _player.Backpack;

        for (int i = 0; i < bag.EntryCount; i++)
        {
            ItemResource item = bag.ItemAt(i);
            string name = item.ItemName;

            if (_announced.Contains(name) || !_player.StillNeeded(item))
                continue;

            _announced.Add(name);

            (int found, int total, string set) = _player.CollectionProgress(name);

            // The count includes this piece, which is not yet recorded anywhere:
            // the record is written at the door. Saying "1/3" while holding the
            // second piece would be technically true and read as a bug.
            _hud.Announce($"CURIOSITY — {name.ToUpper()}  ({set} {found + 1}/{total})", 3.2f);
        }
    }

    /// Pieces already called out this run, so a crate searched twice does not
    /// announce the same ring again.
    private readonly System.Collections.Generic.HashSet<string> _announced = new();

    private Hud? _hud;

    /// Moves as much as fits, and returns what it was worth.
    private int Transfer(Inventory from, Inventory into)
    {
        int gained = 0;

        // Backwards, because a fully-moved entry collapses by swapping the last
        // one into its slot — a forward walk would skip whatever took its place.
        for (int i = from.EntryCount - 1; i >= 0; i--)
        {
            ItemResource item = from.ItemAt(i);
            int moved = into.TryAdd(item, from.CountAt(i));

            for (int n = 0; n < moved; n++)
                from.RemoveOne(i);

            gained += Mathf.RoundToInt(moved * item.Value * _valueScale);
        }

        return gained;
    }

    /// Rolls this crate's table into a bag without emptying it.
    ///
    /// For probes asking what a crate *contains* rather than what a run happened
    /// to pick up. A stage that read the rarity bias instead would be reading the
    /// input to the question — which is exactly how a cache named "supply" shipped
    /// full of jewellery.
    public void RollIntoForTesting(Inventory bag)
    {
        // Loads its own table. A container built with `new` never enters the
        // tree, so `_Ready` never runs and the table is empty — every roll
        // produces nothing, which reads as "this crate contains no curiosities"
        // rather than as "this crate contains nothing at all". The stage that
        // caught it was the one asserting an *ordinary* crate still produces them.
        LoadTable();
        Transfer(RollAll(), bag);
    }

    /// Everything this crate holds, in an inventory of its own.
    ///
    /// Capacity is effectively unbounded: the crate is not carrying it anywhere
    /// and a roll refused for bulk would be loot the player never learns existed,
    /// which is the failure this whole change is undoing.
    private Inventory RollAll()
    {
        var contents = new Inventory(int.MaxValue / 2);
        if (_table.Length == 0 || _weightTotal <= 0.0f)
            return contents;

        for (int roll = 0; roll < RollCount; roll++)
        {
            ItemResource item = PickWeighted();
            int stack = item.MinStack + (int)(NextFloat() * (item.MaxStack - item.MinStack + 1));
            contents.TryAdd(item, Mathf.Clamp(stack, item.MinStack, item.MaxStack));
        }

        return contents;
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

    /// Deterministic from where the crate stands, so a seed reproduces a run.
    ///
    /// Two crates at the same position would roll identically, which never
    /// happens on a generated map and does happen to anything built by hand at
    /// the origin — hence the fallback.
    private void Seed()
    {
        _rng = (ulong)(Position.X * 7919.0f) ^ ((ulong)(Position.Z * 104729.0f) << 21)
               ^ 0x2545F4914F6CDD1DUL;

        if (_rng == 0)
            _rng = 0x9E3779B97F4A7C15UL;
    }

    private float NextFloat()
    {
        // Seeded here as well as in _Ready, because zero is a fixed point of
        // xorshift: the state stays zero forever and every draw returns exactly
        // 0.0, so the weighted pick takes the first entry every time. A container
        // built with `new` never runs _Ready, and forty rolls produced forty
        // adrenaline shots — a table that looked empty of everything else.
        if (_rng == 0)
            Seed();

        _rng ^= _rng << 13;
        _rng ^= _rng >> 7;
        _rng ^= _rng << 17;
        return (_rng >> 40) / 16777216.0f;
    }
}
