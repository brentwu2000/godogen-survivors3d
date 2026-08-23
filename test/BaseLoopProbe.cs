using Godot;

/// Drives the whole outer loop once: base screen, launch, die, come back.
///
///   godot --headless --script test/BaseLoopProbe.cs
///
/// Every other probe holds one scene still and measures inside it. This is the
/// only one that crosses between them, which is where a loop can be complete in
/// both halves and still not close — the run that never returns and the base
/// that never launches both look fine from inside.
///
/// The profile on disk is backed up and restored.
public partial class BaseLoopProbe : SceneTree
{
    private const string ProfilePath = "user://profile.json";

    /// Long enough to cover the meta layer's read-the-banner delay.
    private const int ReturnTimeoutTicks = 60 * 8;

    private string? _backup;
    private int _stage;
    private int _tick;
    private bool _failed;

    public override void _Initialize()
    {
        _backup = FileAccess.FileExists(ProfilePath)
            ? FileAccess.GetFileAsString(ProfilePath)
            : null;

        // A profile with something to lose, so the return trip has a result to
        // report rather than a row of zeroes.
        // `HasSeenBase` set, because this probe is testing the returning player's
        // loop. Without it the base screen does what it should for a new player —
        // launches straight into a run — and the probe never gets to press a key
        // on the screen it exists to drive.
        var profile = new Profile { Credits = 500, HasSeenBase = true };
        using (var file = FileAccess.Open(ProfilePath, FileAccess.ModeFlags.Write))
            file?.StoreString(profile.ToJson());

        EnterBase();
    }

    private void EnterBase()
    {
        var scene = GD.Load<PackedScene>("res://scenes/Base.tscn")?.Instantiate();
        if (scene == null)
        {
            GD.PushError("Missing res://scenes/Base.tscn — run scenes/BuildBase.cs first");
            Quit(1);
            return;
        }

        GetRoot().AddChild(scene);

        // ChangeSceneToFile swaps whatever CurrentScene is, and a --script tree
        // has none until it is told. Without this the launch quietly does
        // nothing and the probe times out with no clue why.
        CurrentScene = scene;
    }

    public override bool _PhysicsProcess(double delta)
    {
        _tick++;

        switch (_stage)
        {
            case 0: return Step(StageLaunch, "the base screen launches a run");
            case 1: return Step(StageDie, "the run ends");
            case 2: return Step(StageDebrief, "the debrief reports it and waits");
            case 3: return Step(StageReturn, "and hands control back to the base");
            default:
                Restore();
                GD.Print(_failed ? "PROBE FAILED" : "PROBE OK");
                Quit(_failed ? 1 : 0);
                return true;
        }
    }

    private bool Step(System.Func<int, bool?> stage, string label)
    {
        bool? verdict = stage(_tick);
        if (verdict == null)
            return false;

        GD.Print($"{label}: {(verdict.Value ? "ok" : "FAILED")}");
        _failed |= !verdict.Value;
        _stage++;
        _tick = 0;
        return false;
    }

    private bool? StageLaunch(int tick)
    {
        // Stand at the gate, then press the one verb key.
        //
        // This used to press `menu_launch`, which was a key that launched a run
        // from anywhere in the base. There is no such key now — the shelter's
        // whole design is that the verb is `[E]` and what it applies to is
        // wherever the player is standing, so a probe that launched without
        // walking to the gate would be testing a path the game does not have.
        //
        // The player is teleported rather than driven. Walking there is
        // `ShelterProbe`'s job; this one is about whether the loop closes.
        if (tick == 2)
        {
            var shelter = CurrentScene?.GetNodeOrNull<Shelter>("Shelter");
            var player = CurrentScene?.GetNodeOrNull<Player>("Player");

            if (shelter == null || player == null)
            {
                GD.PushError($"  no shelter={shelter == null} or player={player == null} in the base");
                return false;
            }

            player.GlobalPosition = shelter.Stations[Fitting.Gate];
            return null;
        }

        // A physics tick has to pass for the shelter to notice, and the screen
        // reads the focus rather than being told it.
        if (tick == 4)
        {
            var shelter = CurrentScene?.GetNodeOrNull<Shelter>("Shelter");
            GD.Print($"  standing at {shelter?.Focus}");

            if (shelter?.Focus != Fitting.Gate)
            {
                GD.PushError($"  standing on the gate reads as {shelter?.Focus}");
                return false;
            }

            Input.ActionPress("interact");
            return null;
        }

        if (tick == 5)
        {
            Input.ActionRelease("interact");
            return null;
        }

        if (tick < 32)
            return null;

        var director = CurrentScene?.GetNodeOrNull<RunDirector>("RunDirector");
        GD.Print($"  now in {CurrentScene?.Name}, run director present = {director != null}");
        return director != null;
    }

    private bool? StageDie(int tick)
    {
        var player = CurrentScene?.GetNodeOrNull<Player>("Player");
        if (player == null)
            return false;

        if (tick == 2)
        {
            player.TakeDamage(99999.0f);
            return null;
        }

        if (tick < 10)
            return null;

        var director = CurrentScene?.GetNodeOrNull<RunDirector>("RunDirector");
        GD.Print($"  run state {director?.State}");
        return director?.State == RunState.Died;
    }

    /// The report has to appear, has to say something, and has to still be there
    /// a second later.
    ///
    /// That last part is the assertion that matters. The screen it replaced was a
    /// three and a half second timer, and anything that dismisses itself is
    /// something the player learns to stop reading — so a debrief that vanished
    /// on its own would pass a test for "appeared" while failing at the only job
    /// it has.
    private bool? StageDebrief(int tick)
    {
        var debrief = CurrentScene?.GetNodeOrNull<DebriefScreen>("Debrief");
        if (debrief == null)
        {
            GD.Print("  no Debrief node in the run scene");
            return false;
        }

        if (tick < 90)
        {
            _debriefStayed &= debrief.Visible || tick < 5;
            return null;
        }

        var meta = CurrentScene?.GetNodeOrNull<MetaManager>("MetaManager");
        RunRecord? run = meta?.LastRun;

        GD.Print($"  debrief visible after 1.5s = {debrief.Visible} (never blinked = {_debriefStayed}); " +
                 $"record says {run?.Outcome} at {run?.Seconds:F1}s, banked {run?.Banked}");

        // Then dismiss it the way a player would.
        Input.ActionPress("ui_accept");
        return debrief.Visible && _debriefStayed && run != null && run.Outcome == RunState.Died;
    }

    private bool _debriefStayed = true;

    private bool? StageReturn(int tick)
    {
        if (tick == 2)
        {
            Input.ActionRelease("ui_accept");
            return null;
        }

        // The shelter is what makes a scene the base.
        //
        // This used to be `CurrentScene is Control`, with a comment explaining
        // that the base screen is a Control and the run is a Node3D. Both are
        // Node3D now — the base is a room — so that test quietly became "is this
        // scene not the base", and the stage failed while the loop it checks was
        // working perfectly.
        if (CurrentScene?.GetNodeOrNull<Shelter>("Shelter") != null)
        {
            Profile after = SaveSystem.Load();
            GD.Print($"  back at {CurrentScene.Name} after {tick / 60.0f:F1}s, " +
                     $"profile says {after.RunsLost} lost");
            return after.RunsLost == 1;
        }

        if (tick < ReturnTimeoutTicks)
            return null;

        GD.Print($"  still in {CurrentScene?.Name} after {tick / 60.0f:F1}s");
        return false;
    }

    private void Restore()
    {
        if (_backup == null)
        {
            if (FileAccess.FileExists(ProfilePath))
                DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(ProfilePath));
            return;
        }

        using var file = FileAccess.Open(ProfilePath, FileAccess.ModeFlags.Write);
        file?.StoreString(_backup);
    }
}
