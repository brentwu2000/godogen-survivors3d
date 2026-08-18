using Godot;

/// Checks that a brand-new player's first ninety seconds is a run, not a menu.
///
///   godot --headless --script test/FirstRunProbe.cs
///
/// Exit code is the verdict. This path happens exactly once per player and never
/// again, which makes it the least-exercised code in the game and the code the
/// most people will meet: everyone sees it, nobody sees it twice, and a developer
/// with a save file on disk cannot see it at all without deleting their profile.
///
/// The profile on disk is backed up and restored.
public partial class FirstRunProbe : SceneTree
{
    private const string ProfilePath = "user://profile.json";

    private string? _backup;
    private int _stage;
    private int _tick;
    private bool _failed;

    public override void _Initialize()
    {
        _backup = FileAccess.FileExists(ProfilePath)
            ? FileAccess.GetFileAsString(ProfilePath)
            : null;

        SaveSystem.Delete();
    }

    public override bool _Process(double delta)
    {
        switch (_stage)
        {
            case 0: return RunStage(StageFreshProfileHasNotSeenIt, "a fresh profile has not seen the base");
            case 1: return RunStage(StageOldSaveHasSeenIt, "a save from before this existed has");
            case 2: return RunStage(StageFlagSticks, "and once it is set it stays set across a save");
            case 3: return RunStage(StageBaseSendsThemStraightIn, "the base hands a new player to a run and marks them");
            case 4: return RunStage(StageSecondVisitStays, "and shows itself the second time");
            default:
                Restore();
                GD.Print(_failed ? "PROBE FAILED" : "PROBE OK");
                Quit(_failed ? 1 : 0);
                return true;
        }
    }

    /// Nullable, because two of these stages have to wait for a scene change and
    /// a plain bool cannot say "not yet" — the first version returned false to
    /// mean "still waiting" and this read it as failure, so both staged tests
    /// reported a failure on their very first frame.
    private bool RunStage(System.Func<bool?> stage, string label)
    {
        bool? verdict = stage();
        if (verdict == null)
            return false;

        GD.Print($"{label}: {(verdict.Value ? "ok" : "FAILED")}");
        _failed |= !verdict.Value;
        _stage++;
        _tick = 0;
        return false;
    }

    private bool? StageFreshProfileHasNotSeenIt()
    {
        var fresh = new Profile();
        GD.Print($"  a new Profile: seen the base = {fresh.HasSeenBase}");
        return !fresh.HasSeenBase;
    }

    /// The migration, and the reason the flag defaults the way it does.
    ///
    /// An absent key means a file written before this path existed, and those
    /// players have very much seen the base screen. Defaulting to false would
    /// drop every existing player straight into a run on their next launch, past
    /// the shop they were on their way to — a change that would look like the
    /// game having lost the menu.
    private bool? StageOldSaveHasSeenIt()
    {
        var veteran = new Profile { Credits = 4000, RunsSurvived = 12 };

        // The key is renamed regardless of the value it was written with. The
        // first version looked for `"seen_base": true` and this fixture writes
        // false — so nothing was stripped, the profile loaded its own honest
        // false, and the stage reported a broken migration that was not running.
        string json = veteran.ToJson().Replace("\"seen_base\"", "\"_removed\"");

        Profile? migrated = Profile.FromJson(json);
        if (migrated == null)
        {
            GD.PushError("  the migrated profile did not parse");
            return false;
        }

        GD.Print($"  a save with no seen_base key: seen the base = {migrated.HasSeenBase}");
        return migrated.HasSeenBase;
    }

    private bool? StageFlagSticks()
    {
        var profile = new Profile();
        profile.HasSeenBase = true;

        Profile? read = Profile.FromJson(profile.ToJson());
        bool kept = read?.HasSeenBase ?? false;

        // And the other direction: a profile that has genuinely not seen it must
        // survive a round trip still not having seen it, or the first run would
        // happen every launch until the player finished one.
        Profile? unseen = Profile.FromJson(new Profile().ToJson());
        bool stillUnseen = unseen is { HasSeenBase: false };

        GD.Print($"  set and reloaded: {kept}; unset and reloaded: {(stillUnseen ? "still unset" : "became set")}");
        return kept && stillUnseen;
    }

    /// The transition itself, driven through the real screen.
    ///
    /// Asserting the flag would only test the flag. What has to be true is that a
    /// player with a fresh profile who opens the game ends up in `Main.tscn`
    /// without pressing anything.
    private bool? StageBaseSendsThemStraightIn()
    {
        if (_tick == 0)
        {
            SaveSystem.Delete();

            var scene = GD.Load<PackedScene>("res://scenes/Base.tscn")?.Instantiate();
            if (scene == null)
            {
                GD.PushError("  missing res://scenes/Base.tscn");
                return false;
            }

            GetRoot().AddChild(scene);

            // ChangeSceneToFile swaps whatever CurrentScene is, and a --script
            // tree has none until told. Without this the launch quietly does
            // nothing and the stage fails for a reason that is not the feature.
            CurrentScene = scene;
        }

        _tick++;

        // The launch is deferred out of _Ready, then the scene change lands on
        // the frame after that.
        if (_tick < 12)
            return null;

        bool inRun = CurrentScene?.GetNodeOrNull<RunDirector>("RunDirector") != null;
        Profile saved = SaveSystem.Load();

        GD.Print($"  after opening the game on a fresh profile: in a run = {inRun}, " +
                 $"profile now marked = {saved.HasSeenBase}");

        // Marked *before* the run, not after it. A player who closes the game
        // mid-run and reopens it should get the base screen, not another
        // unexplained run.
        return inRun && saved.HasSeenBase;
    }

    private bool? StageSecondVisitStays()
    {
        if (_tick == 0)
        {
            foreach (Node child in GetRoot().GetChildren())
                child.QueueFree();
        }

        _tick++;
        if (_tick < 4)
            return null;

        // The profile still says seen, from the stage above. Opening the base
        // again has to leave the player on it.
        var scene = GD.Load<PackedScene>("res://scenes/Base.tscn")?.Instantiate();
        if (scene == null)
            return false;

        GetRoot().AddChild(scene);
        CurrentScene = scene;

        bool onTheScreen = scene.GetNodeOrNull<Label>("Screen") is { Text.Length: > 0 };

        GD.Print($"  opening it again with the flag set: the shop drew = {onTheScreen}");
        return onTheScreen;
    }

    private void Restore()
    {
        if (_backup == null)
        {
            SaveSystem.Delete();
            return;
        }

        using var file = FileAccess.Open(ProfilePath, FileAccess.ModeFlags.Write);
        file?.StoreString(_backup);
    }
}
