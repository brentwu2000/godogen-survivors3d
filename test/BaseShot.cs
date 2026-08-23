using Godot;

/// Photographs the base screen. Its own script rather than an argument to
/// Screenshot.cs, because that one loads the run and this is the other scene.
///
///   godot --script test/BaseShot.cs
///   godot --script test/BaseShot.cs -- rich   (credits to see the shop working)
///
/// Not headless — the null rendering driver has nothing to capture. The profile
/// on disk is backed up and restored: a screenshot does not spend a save.
public partial class BaseShot : SceneTree
{
    private const string ProfilePath = "user://profile.json";
    private const string OutputPath = "res://screenshots/base.png";

    private string? _backup;
    private int _frame;

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

        if (System.Array.IndexOf(OS.GetCmdlineUserArgs(), "rich") >= 0)
        {
            var profile = new Profile { Credits = 2600, RunsSurvived = 4, RunsLost = 1 };
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
        Fresh.Profile(scene);

        GetRoot().AddChild(scene);
    }

    public override bool _Process(double delta)
    {
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
