using Godot;

/// Builds scenes/Base.tscn — the screen between runs.
///
///   godot --headless --script scenes/BuildBase.cs
///
/// Builders construct the hierarchy, set properties, attach scripts, pack, and
/// quit. No runtime logic belongs here.
public partial class BuildBase : SceneTree
{
    public override void _Initialize() => SceneBuildUtil.Run(this, Build);

    private static bool Build()
    {
        var root = new Control
        {
            Name = "Base",
            AnchorRight = 1.0f,
            AnchorBottom = 1.0f,
        };

        // A flat backdrop rather than the game's camera: the base is not a place
        // in the world, and rendering one would imply it is.
        root.AddChild(new ColorRect
        {
            Name = "Backdrop",
            Color = new Color(0.09f, 0.10f, 0.12f),
            AnchorRight = 1.0f,
            AnchorBottom = 1.0f,
        });

        root.AddChild(new Label
        {
            Name = "Screen",
            Position = new Vector2(64.0f, 48.0f),
            Size = new Vector2(1600.0f, 960.0f),
        });

        Node scripted = SceneBuildUtil.AttachScriptToRoot(root, "res://scripts/nodes/BaseScreen.cs");
        bool ok = SceneBuildUtil.PackAndSave(scripted, "res://scenes/Base.tscn");
        scripted.Free();
        return ok;
    }
}
