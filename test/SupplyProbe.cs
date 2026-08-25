using Godot;

/// Checks that the run restocks itself, and that everything downstream sees it.
///
///   godot --headless --script test/SupplyProbe.cs
///
/// Exit code is the verdict. A crate that arrives after the run has started is a
/// different thing from a crate the level placed, and the difference is invisible
/// from inside any single system: the log took its census of `LootContainers` in
/// `_Ready`, the sound director subscribed to the crates it found there, the HUD
/// compass cached the list, and the play-test bot captured it once. Every one of
/// those is correct for a map that never changes.
///
/// The boss cache has been dropping into that node since Phase 20 and its
/// contents were never counted by anything. Nothing reported it, because a run in
/// which the player did not open it looks exactly the same.
public partial class SupplyProbe : SceneTree
{
    private Node? _scene;
    private RunDirector? _director;
    private RunLog? _log;
    private Player? _player;

    private int _stage;
    private int _stageTick;
    private bool _failed;

    public override void _Initialize()
    {
        var scene = GD.Load<PackedScene>("res://scenes/Main.tscn")?.Instantiate();
        if (scene == null)
        {
            GD.PushError("Missing res://scenes/Main.tscn");
            Quit(1);
            return;
        }

        var meta = scene.GetNodeOrNull<MetaManager>("MetaManager");
        if (meta != null)
            meta.Ephemeral = true;

        var level = scene.GetNodeOrNull<LevelGenerator>("Level");
        if (level != null)
            level.Seed = 0x51E5D0A7UL;

        GameSession.LaunchedFromBase = false;
        GetRoot().AddChild(scene);
        _scene = scene;
    }

    public override bool _PhysicsProcess(double delta)
    {
        if (_stage == 0 && _stageTick == 0)
        {
            _director = _scene?.GetNodeOrNull<RunDirector>("RunDirector");
            _log = _scene?.GetNodeOrNull<RunLog>("RunLog");
            _player = _scene?.GetNodeOrNull<Player>("Player");

            if (_director == null || _log == null || _player == null)
            {
                GD.PushError("PROBE FAILED - scene is missing a required node");
                Quit(1);
                return true;
            }

            _director.SetPhysicsProcess(false);
            _player.GetNode<WeaponHandler>("WeaponHandler").HoldFire = true;
            _scene?.GetNodeOrNull<Horde>("Horde")?.Pool.Clear();
        }

        _stageTick++;

        switch (_stage)
        {
            case 0: return RunStage(StageDropsLandOnSchedule, "supplies land on the clock, once each");
            case 1: return RunStage(StageDropsAreSupplies, "a supply drop supplies things you can spend");
            case 2: return RunStage(StageLateCrateIsCounted, "a crate that arrives mid-run is counted when it is emptied");
            default:
                GD.Print(_failed ? "PROBE FAILED" : "PROBE OK");
                Quit(_failed ? 1 : 0);
                return true;
        }
    }

    private bool RunStage(System.Func<int, bool?> stage, string label)
    {
        bool? verdict = stage(_stageTick);
        if (verdict == null)
            return false;

        GD.Print($"{label}: {(verdict.Value ? "ok" : "FAILED")}");
        _failed |= !verdict.Value;
        _stage++;
        _stageTick = 0;
        return false;
    }

    /// Driven by moving the clock, not by calling the drop.
    ///
    /// The thing under test is the schedule. A stage that invoked the spawn
    /// directly would pass just as happily against a director that never checks
    /// the time, which is the one way this feature can fail while looking whole.
    ///
    /// **Read from the plan, never from `SupplyDropsAt`.** That export is the
    /// tuned centre of a band, and since H2 the run draws its actual times around
    /// it — `_supplyAt`, published as `PlannedSupplyAt`. Reading the export means
    /// jumping the clock to a time the director was never going to use: this stage
    /// jumped to 0.59 against a run that had drawn 0.60, missed the second drop by
    /// one hundredth of a run, and reported a correct director as broken. The
    /// first drop passed on the same run only because that seed's jitter happened
    /// to fall the other way, which is worse than failing.
    private bool? StageDropsLandOnSchedule(int tick)
    {
        int before = CrateCount();

        float[] schedule = _director!.PlannedSupplyAt;
        if (schedule.Length == 0)
        {
            GD.PushError("  no supply drops are scheduled");
            return false;
        }

        // Just short of the first one: nothing yet.
        _director.SetElapsedForTesting(_director.RunSeconds * (schedule[0] - 0.02f));
        _director.TickForTesting();
        int early = CrateCount();

        _director.SetElapsedForTesting(_director.RunSeconds * (schedule[0] + 0.01f));
        _director.TickForTesting();
        int afterFirst = CrateCount();

        // Twenty more ticks at the same moment. The counter has to be what stops
        // it, not the fact that time has not moved — a check written as
        // "intensity is past the threshold" without consuming it drops a crate
        // every frame for the rest of the run.
        for (int i = 0; i < 20; i++)
            _director.TickForTesting();

        int stillOne = CrateCount();

        _director.SetElapsedForTesting(_director.RunSeconds * (schedule[^1] + 0.01f));
        _director.TickForTesting();
        int afterAll = CrateCount();

        GD.Print($"  crates {before} -> {early} just before the first drop -> {afterFirst} after it " +
                 $"-> {stillOne} after twenty more ticks -> {afterAll} past the last");

        return early == before
               && afterFirst == before + 1
               && stillOne == afterFirst
               && afterAll == before + schedule.Length;
    }

    /// The stage that would have caught the first version of this feature.
    ///
    /// It originally asserted the cache was *richer* than anything the map placed
    /// — a bias of 2.4 against the deep crates' 1.75 — and passed. The bias
    /// multiplies an item's draw weight once per rarity step, so 2.4 makes a
    /// treasure chest: it rolls serums and circuit boards, the only two entries in
    /// the table with no use at all. The payout curve went up beautifully and the
    /// bot still died at 144 s, dry since 69 s, holding 640 credits it could not
    /// spend on anything.
    ///
    /// So the property is not rarity. It is that what comes out can be *used*,
    /// and the only way to check that is to open one.
    ///
    /// **Forty of them, though, not one.** The first version opened a single cache
    /// and required a strict majority of its rows to be spendable. That is a claim
    /// about one draw and not about the table, and it passed for as long as the
    /// director's RNG was a hard-coded constant — every run in the game rolled the
    /// same cache, so the stage was re-asserting one lucky sequence. H2 seeded that
    /// stream from the level, the very next roll came out two spendable and two
    /// inert, and a probe that had never tested the loot table reported the loot
    /// table as broken. `RollIntoForTesting` advances the container's own xorshift
    /// without reseeding, so calling it repeatedly is a sample rather than a repeat.
    private bool? StageDropsAreSupplies(int tick)
    {
        LootContainer? cache = null;
        foreach (Node child in Crates())
        {
            if (child is LootContainer crate && crate.Name.ToString().StartsWith("Supply"))
                cache = crate;
        }

        if (cache == null)
        {
            GD.PushError("  no supply cache on the field");
            return false;
        }

        const int Samples = 40;

        int usableItems = 0, inertItems = 0, majorityCaches = 0;
        var first = new System.Collections.Generic.List<string>();

        for (int s = 0; s < Samples; s++)
        {
            // A bag of its own rather than the player's, so the stage measures the
            // cache's table and not whatever the run has been carrying. Capacity
            // well past a cache's seven rolls, or bulk would silently refuse the
            // heaviest entries — which are the spendable ones.
            var bag = new Inventory(400);
            cache.RollIntoForTesting(bag);

            int usable = 0, inert = 0;

            for (int i = 0; i < bag.EntryCount; i++)
            {
                ItemResource item = bag.ItemAt(i);
                int count = bag.CountAt(i);

                // Counted per item, not per row. A stack of three boxes of rounds
                // is three times the supply of one circuit board, and the inventory
                // stacks it into one row — so counting rows prices the two the same
                // and hides the exact property this stage is about.
                if (item.IsUsable || item.IsThrowable)
                    usable += count;
                else
                    inert += count;

                if (s == 0)
                    first.Add(count > 1 ? $"{item.ItemName} x{count}" : item.ItemName);
            }

            usableItems += usable;
            inertItems += inert;

            if (usable > inert)
                majorityCaches++;
        }

        float share = usableItems / (float)Mathf.Max(1, usableItems + inertItems);
        float carried = majorityCaches / (float)Samples;

        GD.Print($"  first cache: {string.Join(", ", first)}");
        GD.Print($"  over {Samples} caches: {share * 100.0f:F0}% of items spendable, "
               + $"{carried * 100.0f:F0}% of caches majority-spendable");

        // Both, because either alone passes on a table this stage exists to refuse.
        // A high item share with a low cache share is a table that occasionally
        // dumps a pile of rounds and is otherwise trinkets — the treasure chest,
        // wearing an average. A high cache share with a low item share is a table
        // whose spendable entries always turn up and never in useful quantity.
        return share > 0.5f && carried >= 0.7f;
    }

    /// The bug this probe was written for.
    ///
    /// Everything that cares about crates took its list once. A cache emptied
    /// after that raised nobody's count — not the log's, not the contract's, not
    /// the record book's — and every one of those stayed self-consistent, which
    /// is why it survived six phases.
    private bool? StageLateCrateIsCounted(int tick)
    {
        if (tick == 1)
        {
            _cratesBefore = _log!.Freeze(RunState.Running, 0, new int[4], new int[4],
                                         System.Array.Empty<string>()).CratesLooted;

            // A bag big enough that "emptied" is a fact about the crate.
            //
            // A cache is seven rolls and the Drifter carries twenty bulk, so a
            // heavy one does not fit — the player takes what they can, the crate
            // keeps the rest, `Looted` never flips and `CratesLooted` never moves
            // while `LootValue` climbs. That is the correct behaviour of a full
            // bag and it is indistinguishable, from here, from the bug this stage
            // was written to catch. It read as a pass for as long as stage one
            // left exactly one cache on the field and that cache happened to fit.
            // Through `ApplyGear`, which is the path a backpack is really sized
            // by, rather than by reaching past it.
            _player!.ApplyGear(0.0f, 0.0f, 0.0f, 400, 0);

            // Stage one has already put both caches on the field. Stand on the
            // first, so the stage does not depend on which of them was last.
            foreach (Node child in Crates())
            {
                if (child is LootContainer crate && crate.Name.ToString().StartsWith("Supply"))
                {
                    _player!.GlobalPosition = crate.GlobalPosition;
                    GD.Print($"  standing on {crate.Name}");
                    break;
                }
            }

            return null;
        }

        // The search takes two seconds and the player has to stand still for it.
        if (tick < 200)
            return null;

        RunRecord run = _log!.Freeze(RunState.Running, 0, new int[4], new int[4],
                                     System.Array.Empty<string>());

        GD.Print($"  stood on a mid-run cache: crates counted {_cratesBefore} -> {run.CratesLooted}, " +
                 $"loot value {run.LootValue}");

        return run.CratesLooted > _cratesBefore && run.LootValue > 0;
    }

    private int _cratesBefore;

    private Godot.Collections.Array<Node> Crates() =>
        _scene?.GetNodeOrNull("LootContainers")?.GetChildren() ?? new Godot.Collections.Array<Node>();

    private int CrateCount() => _scene?.GetNodeOrNull("LootContainers")?.GetChildCount() ?? 0;
}
