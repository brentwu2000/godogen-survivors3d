using Godot;

/// One-off visual comparison of the two billboard modes under the real camera
/// framing. Writes screenshots/billboard_compare.png and quits.
///
///   godot --script test/BillboardCompare.cs
///
/// Not headless — the null rendering driver has nothing to capture.
public partial class BillboardCompare : SceneTree
{
    private const float CameraTiltDegrees = -52.0f;
    private const float CameraDistance = 24.0f;
    private const float OrthoSize = 18.0f;
    private const float SpriteHeightMeters = 2.2f;

    // A few frames of slack: the first drawn frame can land before the sprites'
    // billboard transforms have been resolved.
    private const int WarmupFrames = 5;

    private int _frame;

    public override void _Initialize()
    {
        // A comment saying "not headless" is not a check. See test/Display.cs:
        // without one, running this headless does not fail — it spins a core
        // forever, silently, and looks from outside exactly like a slow test.
        if (!Display.Required(this, "BillboardCompare"))
            return;

        var texture = GD.Load<Texture2D>("res://assets/sprites/player.png");
        if (texture == null)
        {
            GD.PushError("Missing res://assets/sprites/player.png");
            Quit(1);
            return;
        }

        Window root = GetRoot();

        float tilt = Mathf.DegToRad(-CameraTiltDegrees);
        root.AddChild(new Camera3D
        {
            Projection = Camera3D.ProjectionType.Orthogonal,
            Size = OrthoSize,
            Position = new Vector3(0.0f, CameraDistance * Mathf.Sin(tilt), CameraDistance * Mathf.Cos(tilt)),
            RotationDegrees = new Vector3(CameraTiltDegrees, 0.0f, 0.0f),
        });

        root.AddChild(new DirectionalLight3D
        {
            RotationDegrees = new Vector3(-55.0f, -35.0f, 0.0f),
        });

        // Checkerboard of tiles so the ground plane's perspective is visible and
        // each sprite's contact point can be judged against it.
        for (int x = -6; x <= 6; x++)
        for (int z = -6; z <= 6; z++)
        {
            if (((x + z) & 1) != 0)
                continue;
            root.AddChild(new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(2.0f, 0.02f, 2.0f) },
                Position = new Vector3(x * 2.0f, 0.0f, z * 2.0f),
            });
        }

        AddSprite(root, texture, new Vector3(-4.0f, 0.0f, 0.0f), BaseMaterial3D.BillboardModeEnum.Enabled);
        AddSprite(root, texture, new Vector3(4.0f, 0.0f, 0.0f), BaseMaterial3D.BillboardModeEnum.FixedY);

        // Same pair further back: billboard modes diverge more the further a
        // sprite sits from the camera's focal row.
        AddSprite(root, texture, new Vector3(-4.0f, 0.0f, -6.0f), BaseMaterial3D.BillboardModeEnum.Enabled);
        AddSprite(root, texture, new Vector3(4.0f, 0.0f, -6.0f), BaseMaterial3D.BillboardModeEnum.FixedY);
    }

    private static void AddSprite(Node parent, Texture2D texture, Vector3 position,
                                  BaseMaterial3D.BillboardModeEnum mode)
    {
        parent.AddChild(new Sprite3D
        {
            Texture = texture,
            PixelSize = SpriteHeightMeters / texture.GetHeight(),
            Billboard = mode,
            AlphaCut = SpriteBase3D.AlphaCutMode.Discard,
            AlphaScissorThreshold = 0.5f,
            Shaded = false,
            Offset = new Vector2(0.0f, texture.GetHeight() * 0.5f),
            Position = position,
        });
    }

    public override bool _Process(double delta)
    {
        if (++_frame < WarmupFrames)
            return false;

        Image image = GetRoot().GetTexture().GetImage();
        string path = ProjectSettings.GlobalizePath("res://screenshots/billboard_compare.png");
        Error err = image.SavePng(path);
        if (err != Error.Ok)
            GD.PushError($"SavePng failed: {err}");
        else
            GD.Print($"Wrote {path}");

        return true;
    }
}
