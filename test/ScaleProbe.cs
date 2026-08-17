using Godot;

/// Puts a horde quad, a player sprite and a 2m reference pole side by side under
/// the game camera, so sprite world-height can be read off against a known bar
/// instead of guessed from a screenshot.
///
///   godot --script test/ScaleProbe.cs
public partial class ScaleProbe : SceneTree
{
    private const float CameraTiltDegrees = -52.0f;
    private const float CameraDistance = 24.0f;
    private const float OrthoSize = 8.0f;
    private const float ReferenceHeight = 2.0f;

    private const int WarmupFrames = 5;

    private int _frame;

    public override void _Initialize()
    {
        var zombie = GD.Load<Texture2D>("res://assets/sprites/zombie.png");
        var player = GD.Load<Texture2D>("res://assets/sprites/player.png");
        var shader = GD.Load<Shader>("res://assets/shaders/horde_billboard.gdshader");
        if (zombie == null || player == null || shader == null)
        {
            GD.PushError("ScaleProbe: missing textures or shader");
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

        root.AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-55.0f, -35.0f, 0.0f) });

        // Ground so the bases are visibly coincident.
        root.AddChild(new MeshInstance3D
        {
            Mesh = new PlaneMesh { Size = new Vector2(40.0f, 40.0f) },
        });

        // Reference poles at exactly 2m, one beside each sprite.
        AddPole(root, new Vector3(-1.6f, 0.0f, 0.0f));
        AddPole(root, new Vector3(1.6f, 0.0f, 0.0f));

        // Horde quad through the real renderer, so this measures the shipping
        // path rather than a hand-built stand-in.
        var horde = new HordeRenderer(zombie, shader, 2.0f, 1, 20.0f);
        root.AddChild(horde.Node);
        var pool = new EnemyPool(1);
        pool.TrySpawn(new Vector3(-2.6f, 0.0f, 0.0f), 1.0f, 0.0f);
        horde.Sync(pool);

        root.AddChild(new Sprite3D
        {
            Texture = player,
            PixelSize = 2.2f / player.GetHeight(),
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            AlphaCut = SpriteBase3D.AlphaCutMode.Discard,
            AlphaScissorThreshold = 0.5f,
            Shaded = false,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Offset = new Vector2(0.0f, player.GetHeight() * 0.5f),
            Position = new Vector3(2.6f, 0.0f, 0.0f),
        });

        GD.Print($"zombie texture {zombie.GetWidth()}x{zombie.GetHeight()}, quad height 2.00m");
        GD.Print($"player texture {player.GetWidth()}x{player.GetHeight()}, quad height 2.20m");
        GD.Print($"reference poles {ReferenceHeight:F2}m");
    }

    private static void AddPole(Node parent, Vector3 position)
    {
        var material = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.9f, 0.15f, 0.15f),
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        };

        parent.AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(0.08f, ReferenceHeight, 0.08f) },
            MaterialOverride = material,
            Position = position + new Vector3(0.0f, ReferenceHeight * 0.5f, 0.0f),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
    }

    public override bool _Process(double delta)
    {
        if (++_frame < WarmupFrames)
            return false;

        Image image = GetRoot().GetTexture().GetImage();
        image.SavePng(ProjectSettings.GlobalizePath("res://screenshots/scale_probe.png"));
        GD.Print("Wrote screenshots/scale_probe.png");
        return true;
    }
}
