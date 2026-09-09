using Godot;

/// Photographs the base screen. Its own script rather than an argument to
/// Screenshot.cs, because that one loads the run and this is the other scene.
///
///   godot --script test/BaseShot.cs
///   godot --script test/BaseShot.cs -- rich   (credits to see the shop working)
///   godot --script test/BaseShot.cs -- roster[:2]   (the survivor select)
///   godot --script test/BaseShot.cs -- roster:2 locale:zh_TW
///   godot --script test/BaseShot.cs -- at:Console            (a fitting's page)
///
/// Not headless — the null rendering driver has nothing to capture. The profile
/// on disk is backed up and restored: a screenshot does not spend a save.
public partial class BaseShot : SceneTree
{
    private const string ProfilePath = "user://profile.json";
    private const string OutputPath = "res://screenshots/base.png";

    private string? _backup;
    private int _frame;
    private bool _roster;

    /// Which fitting to stand at, for the pages that are not the shop.
    ///
    /// The shelter decides the focus from where the player *is*, so a capture of
    /// the settings page would otherwise have to walk there — which is a capture
    /// script testing the shelter. Same reasoning as `ShowRoster` being public.
    private string _at = "";
    private int _pick;
    private Node? _scene;

    public override void _Initialize()
    {
        // A comment saying "not headless" is not a check. See test/Display.cs:
        // without one, running this headless does not fail — it spins a core
        // forever, silently, and looks from outside exactly like a slow test.
        if (!Display.Required(this, "BaseShot"))
            return;

        _backup = FileAccess.FileExists(ProfilePath)
            ? FileAccess.GetFileAsString(ProfilePath)
            : null;

        string[] args = OS.GetCmdlineUserArgs();

        foreach (string argument in args)
        {
            if (argument.StartsWith("at:", System.StringComparison.Ordinal))
                _at = argument["at:".Length..];
        }

        // Ten extractions, so three survivors are open and two are not. A
        // roster shot with everything unlocked does not show the locked state,
        // and the locked state is half of what the screen is for.
        foreach (string argument in args)
        {
            if (!argument.StartsWith("roster", System.StringComparison.Ordinal))
                continue;

            _roster = true;
            int at = argument.IndexOf(':');
            if (at > 0 && int.TryParse(argument[(at + 1)..], out int pick))
                _pick = pick;
        }

        if (System.Array.IndexOf(args, "rich") >= 0 || _roster)
        {
            var profile = new Profile
            {
                Credits = 2600,
                RunsSurvived = _roster ? 10 : 4,
                RunsLost = 1,

                // **Without this the capture photographs the run.** A profile
                // that has never seen the base is sent straight into a game by
                // `BaseScreen._Ready` — deliberately, because everything on that
                // screen is an answer to a question a new player has not been
                // asked. `ChangeSceneToFile` is deferred, so the first frames
                // still draw the base and the shot came out as the roster screen
                // with a run's HUD over it and a NullReferenceException
                // underneath.
                HasSeenBase = true,
            };
            profile.AddToStash("Circuit Board", 2);
            profile.AddToStash("Antiviral Serum", 1);
            profile.Proficiency[(int)WeaponCategory.Firearm] = 6;
            profile.Proficiency[(int)WeaponCategory.MeleeLong] = 2;

            using var file = FileAccess.Open(ProfilePath, FileAccess.ModeFlags.Write);
            file?.StoreString(profile.ToJson());
        }

        var scene = GD.Load<PackedScene>("res://scenes/Base.tscn")?.Instantiate();
        if (scene == null)
        {
            GD.PushError("Missing res://scenes/Base.tscn — run scenes/BuildBase.cs first");
            Quit(1);
            return;
        }

        // Not the developer's save file. See `Fresh`.
        if (!_roster)
            Fresh.Profile(scene);

        GetRoot().AddChild(scene);
        _scene = scene;
    }

    public override bool _Process(double delta)
    {
        // Opened on the first frame rather than straight after `AddChild`, and
        // the difference is that `_Ready` has run by then. `_Initialize` is
        // before the root window is set up, so a node added there has entered
        // the tree and is not yet *ready* — `BaseScreen._Ready` had not built the
        // portrait panel, and `ShowRoster` dereferenced a null `TextureRect`
        // while the screenshot came out looking almost right.
        if (_frame == 1 && _roster)
            _scene?.GetNode<BaseScreen>("Panel").ShowRoster(_pick);

        if (_frame == 1 && _at.Length > 0
            && System.Enum.TryParse(_at, ignoreCase: true, out Fitting fitting))
        {
            _scene?.GetNodeOrNull<Shelter>("Shelter")?.StandAt(fitting);
        }

        if (++_frame < 20)
            return false;

        Image image = GetRoot().GetTexture().GetImage();
        Error err = image.SavePng(ProjectSettings.GlobalizePath(OutputPath));
        GD.Print(err == Error.Ok ? $"Wrote {OutputPath}" : $"SavePng failed: {err}");

        Restore();
        return true;
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
