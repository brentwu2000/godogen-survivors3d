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
            case 1: return RunStage(StageDropsAreWorthTheWalk, "a drop is richer than the map it lands on");
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
    private bool? StageDropsLandOnSchedule(int tick)
    {
        int before = CrateCount();

        float[] schedule = _director!.SupplyDropsAt;
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

    /// A drop nobody would walk to is scenery.
    ///
    /// The point of the cache is that the second half of the run has somewhere to
    /// go, and the bot decides where to go by rarity bias against distance — so a
    /// cache biased like an ordinary crate would be ignored by the same rule that
    /// makes the deep crates worth reaching.
    private bool? StageDropsAreWorthTheWalk(int tick)
    {
        float best = 0.0f;
        float dropBias = 0.0f;
        float furthest = 0.0f;

        foreach (Node child in Crates())
        {
            if (child is not LootContainer crate)
                continue;

            if (crate.Name.ToString().StartsWith("Supply"))
                dropBias = Mathf.Max(dropBias, crate.RarityBias);
            else
                best = Mathf.Max(best, crate.RarityBias);

            furthest = Mathf.Max(furthest, crate.Position.Length());
        }

        GD.Print($"  best placed crate is x{best:F2}, a supply drop is x{dropBias:F2} " +
                 $"(the map runs out to {furthest:F0}m)");

        return dropBias > best;
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

            // Under the player, so the search timer is the only thing between the
            // probe and the answer.
            _director!.SetElapsedForTesting(_director.RunSeconds * (_director.SupplyDropsAt[0] + 0.01f));
            _director.TickForTesting();

            foreach (Node child in Crates())
            {
                if (child is LootContainer crate && crate.Name.ToString().StartsWith("Supply"))
                    _player!.GlobalPosition = crate.GlobalPosition;
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
