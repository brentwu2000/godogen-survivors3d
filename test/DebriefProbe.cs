using Godot;

/// Checks that the run's record is what actually happened, and that the screen
/// reporting it says the same thing.
///
///   godot --headless --script test/DebriefProbe.cs
///
/// Exit code is the verdict. Two halves, and the second is the one worth having:
/// a log that counts correctly and a screen that prints a different number is a
/// game the player will describe as lying to them, and they will be right even
/// though both halves are individually fine.
public partial class DebriefProbe : SceneTree
{
    private Horde? _horde;
    private Player? _player;
    private RunDirector? _director;
    private RunLog? _log;
    private MetaManager? _meta;
    private DebriefScreen? _debrief;
    private Node? _scene;

    private int _stage;
    private int _stageTick;
    private bool _failed;

    private int _crateValue;

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

        // A probe owns its tree. Leaving this set would have the meta layer swap
        // the scene out from under the measurement the moment the run ended.
        GameSession.LaunchedFromBase = false;

        GetRoot().AddChild(scene);
        _scene = scene;
    }

    public override bool _PhysicsProcess(double delta)
    {
        if (_stage == 0 && _stageTick == 0)
        {
            _horde = _scene?.GetNodeOrNull<Horde>("Horde");
            _player = _scene?.GetNodeOrNull<Player>("Player");
            _director = _scene?.GetNodeOrNull<RunDirector>("RunDirector");
            _log = _scene?.GetNodeOrNull<RunLog>("RunLog");
            _meta = _scene?.GetNodeOrNull<MetaManager>("MetaManager");
            _debrief = _scene?.GetNodeOrNull<DebriefScreen>("Debrief");

            if (_horde == null || _player == null || _director == null || _log == null
                || _meta == null || _debrief == null)
            {
                GD.PushError($"PROBE FAILED — horde={_horde != null} player={_player != null} " +
                             $"director={_director != null} log={_log != null} " +
                             $"meta={_meta != null} debrief={_debrief != null}");
                Quit(1);
                return true;
            }

            // Nothing arrives that a stage did not put there, and nothing shoots
            // the subjects mid-count. The director is stopped too — a stage that
            // stands still for five seconds on an extraction pad would otherwise
            // be measuring whether the bot survives the wave, not whether the log
            // counted correctly.
            _director.SetPhysicsProcess(false);
            _player.GetNode<WeaponHandler>("WeaponHandler").SetPhysicsProcess(false);
            _horde.Pool.Clear();
        }

        _stageTick++;

        switch (_stage)
        {
            case 0: return RunStage(StageKills, "kills are counted by variant");
            case 1: return RunStage(StageCrate, "a searched crate is counted, with what it paid");
            case 2: return RunStage(StageItems, "using and throwing are counted apart");
            case 3: return RunStage(StageLowestHealth, "the worst moment is remembered, not the last one");
            case 4: return RunStage(StageExtract, "the record matches the run that just happened");
            case 5: return RunStage(StageScreenAgrees, "the screen reports the record, not its own arithmetic");
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

    /// Five of one variant and two of another, killed outright. The breakdown is
    /// what makes a hunt contract checkable and the debrief worth reading — a
    /// single total would satisfy neither.
    private bool? StageKills(int tick)
    {
        if (tick < 2)
            return null;

        Spawn(1, 5);
        Spawn(3, 2);

        // Re-read the count every time rather than walking a captured range. Two
        // of these are bloaters, and a bloater's death blast removes whatever is
        // standing near it — so any index taken before the first kill can be past
        // the end by the second.
        while (_horde!.Pool.Count > 0)
            _horde.Damage(_horde.Pool.Count - 1, 9999.0f, Vector2.Zero);

        return true;
    }

    private bool? StageCrate(int tick)
    {
        if (tick == 1)
        {
            // Held, not re-found. FirstCrate() returns the first *unlooted* one,
            // so re-asking after the search succeeds hands back a different crate
            // the player is nowhere near — and the stage then waits out its
            // timeout on a container it never touched.
            _crate = FirstCrate();
            if (_crate == null)
                return false;

            _player!.GlobalPosition = _crate.GlobalPosition;
            return null;
        }

        // The search bar has to fill on its own; forcing the signal would test
        // the probe rather than the container.
        if (_crate is { Looted: false } && tick < 60 * 8)
            return null;

        _crateValue = _player!.Backpack.TotalValue;
        return _crate?.Looted ?? false;
    }

    private LootContainer? _crate;

    private bool? StageItems(int tick)
    {
        if (tick == 1)
        {
            var medkit = GD.Load<ItemResource>("res://resources/items/medkit.tres");
            var bomb = GD.Load<ItemResource>("res://resources/items/pipe_bomb.tres");
            if (medkit == null || bomb == null)
                return false;

            _player!.Backpack.TryAdd(medkit, 1);
            _player.Backpack.TryAdd(bomb, 1);
            _player.TakeDamage(50.0f);
            return null;
        }

        if (tick == 2)
        {
            _player!.TryUseBest();
            _player.TryThrow();
            return null;
        }

        // Null until the deadline, then the verdict. Returning `tick >= 4`
        // outright reports a failure on every tick before the fourth, because the
        // harness reads any non-null as the stage's answer.
        return tick < 4 ? null : true;
    }

    /// Damage, then heal all the way back. A log that sampled only at the end
    /// would report full health for a run the player nearly died in, which is the
    /// single most interesting number a survived run produces.
    private bool? StageLowestHealth(int tick)
    {
        if (tick == 1)
        {
            _player!.TakeDamage(_player.Health - 12.0f);
            return null;
        }

        if (tick < 5)
            return null;

        _player!.Heal(9999.0f);
        return tick < 8 ? null : true;
    }

    private bool? StageExtract(int tick)
    {
        ExtractionZone? pad = _director!.PrimaryPad;
        if (pad == null)
            return false;

        if (tick == 1)
        {
            pad.Open = true;
            pad.Visible = true;
            _player!.GlobalPosition = pad.GlobalPosition;
            return null;
        }

        if (_director.State == RunState.Running && tick < 60 * 12)
            return null;

        RunRecord? run = _meta!.LastRun;
        if (run == null)
        {
            GD.Print("  no record was frozen");
            return false;
        }

        // Sized from the live table rather than a literal. The count was written
        // out as 5 here and the row count grew by one when the boss landed, so
        // the probe failed on a breakdown that was entirely correct.
        bool kills = run.KillsByType.Length == _horde!.Types.Length
                     && run.KillsByType[1] == 5 && run.KillsByType[3] == 2 && run.Kills == 7;
        bool crate = run.CratesLooted == 1 && run.LootValue == _crateValue;
        bool items = run.ItemsUsed == 1 && run.ItemsThrown == 1;
        bool health = run.LowestHealth <= 12.5f && run.MaxHealth > run.LowestHealth;
        bool payout = run.Survived
                      && run.Banked == Mathf.RoundToInt((run.BackpackValue + run.SafeBoxValue) * run.Multiplier);

        GD.Print($"  kills {run.Kills} ({string.Join("/", run.KillsByType)}), crates {run.CratesLooted} " +
                 $"worth {run.LootValue} (bag showed {_crateValue}), used {run.ItemsUsed} threw {run.ItemsThrown}, " +
                 $"lowest health {run.LowestHealth:F0} of {run.MaxHealth:F0}, " +
                 $"banked {run.Banked} = ({run.BackpackValue}+{run.SafeBoxValue}) x{run.Multiplier:F2}");

        return kills && crate && items && health && payout;
    }

    /// The screen is composed from the record and nothing else. Asserted by
    /// reading the text it actually produced, because "the label says 7" is the
    /// only version of this the player can verify too.
    private bool? StageScreenAgrees(int tick)
    {
        RunRecord run = _meta!.LastRun!;
        _debrief!.Show(_meta, _log);

        string body = _debrief.GetNode<Label>("Body").Text;
        string title = _debrief.GetNode<Label>("Title").Text;

        bool shown = _debrief.Visible;
        bool saysOutcome = title == "EXTRACTED";
        bool saysKills = body.Contains($"killed {run.Kills}");
        bool saysBanked = body.Contains($"banked {run.Banked}");
        bool saysMultiplier = body.Contains($"x{run.Multiplier:F2}");
        bool saysCrates = body.Contains($"searched {run.CratesLooted} crates for {run.LootValue}");
        bool saysContract = body.Contains("contract");

        GD.Print($"  visible={shown} title=\"{title}\" kills={saysKills} banked={saysBanked} " +
                 $"multiplier={saysMultiplier} crates={saysCrates} contract={saysContract}");

        return shown && saysOutcome && saysKills && saysBanked && saysMultiplier && saysCrates && saysContract;
    }

    private void Spawn(int type, int count)
    {
        for (int i = 0; i < count; i++)
            _horde!.Spawn(_player!.GlobalPosition + new Vector3(20.0f + i, 0.0f, 20.0f), type);
    }

    private LootContainer? FirstCrate()
    {
        Node? crates = _scene?.GetNodeOrNull("LootContainers");
        if (crates == null)
            return null;

        foreach (Node child in crates.GetChildren())
        {
            if (child is LootContainer container && !container.Looted)
                return container;
        }

        return null;
    }
}
