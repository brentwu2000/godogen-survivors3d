using Godot;

/// Renders the main scene and writes a still. Reused whenever a change needs
/// visual confirmation rather than a passing build.
///
///   godot --script test/Screenshot.cs
///
/// Not headless — the null rendering driver has nothing to capture.
public partial class Screenshot : SceneTree
{
    private const string ScenePath = "res://scenes/Main.tscn";
    private const string OutputPath = "res://screenshots/main.png";

    /// Long enough for the camera rig to settle onto the player; its follow is a
    /// lerp, so frame one still shows it at the origin.
    private const int WarmupFrames = 30;

    private int _frame;
    private Vector3? _teleport;
    private Node? _scene;

    public override void _Initialize()
    {
        var scene = GD.Load<PackedScene>(ScenePath)?.Instantiate();
        if (scene == null)
        {
            GD.PushError($"Missing {ScenePath}");
            Quit(1);
            return;
        }

        GetRoot().AddChild(scene);
        _scene = scene;

        // Optional "-- x z" places the player somewhere worth photographing, so
        // prompts that only appear near a crate or the pad can be captured.
        string[] args = OS.GetCmdlineUserArgs();
        if (args.Length >= 2
            && float.TryParse(args[0], out float x)
            && float.TryParse(args[1], out float z))
        {
            _teleport = new Vector3(x, 0.0f, z);
        }
    }

    public override bool _Process(double delta)
    {
        // Not in _Initialize: nodes are not inside the tree yet, so setting a
        // global transform there silently does nothing.
        if (_frame == 0 && _teleport.HasValue && _scene != null)
        {
            var player = _scene.GetNodeOrNull<Node3D>("Player");
            if (player != null)
                player.GlobalPosition = _teleport.Value;
        }

        if (++_frame < WarmupFrames)
            return false;

        Image image = GetRoot().GetTexture().GetImage();
        string path = ProjectSettings.GlobalizePath(OutputPath);
        Error err = image.SavePng(path);
        if (err != Error.Ok)
            GD.PushError($"SavePng failed: {err}");
        else
            GD.Print($"Wrote {path}");

        return true;
    }
}
