using Godot;

/// Builds scenes/Base.tscn — the shelter between runs.
///
///   godot --headless --script scenes/BuildBase.cs
///
/// Builders construct the hierarchy, set properties, attach scripts, pack, and
/// quit. No runtime logic belongs here.
///
/// This was a flat `Control` with two columns of text, and the comment defending
/// that said "the base is not a place in the world, and rendering one would imply
/// it is". It is a place now, on purpose. The screen worked and had one problem
/// nothing on it could fix: eight verb keys on one page made selling the stash,
/// changing terrain and launching the run cost exactly the same, so the loop
/// between runs was a list rather than a route.
public partial class BuildBase : SceneTree
{
    /// Closer and steeper than the run camera. Eleven and a half metres of room
    /// is narrower than the 13 m the run camera sits back, so the same framing
    /// would put the far wall between the lens and the player.
    private const float CameraTiltDegrees = -34.0f;
    private const float CameraDistance = 8.5f;
    private const float CameraFov = 55.0f;

    public override void _Initialize() => SceneBuildUtil.Run(this, Build);

    private static bool Build()
    {
        var root = new Node3D { Name = "Base" };

        root.AddChild(BuildEnvironment());
        root.AddChild(new DirectionalLight3D
        {
            Name = "Sun",

            // Steep and from the side. The room has no windows, so this is
            // strip lighting rather than daylight — flat enough that the walls
            // read as walls and angled enough that the furniture casts something.
            RotationDegrees = new Vector3(-62.0f, -38.0f, 0.0f),
            LightEnergy = 0.9f,
            ShadowEnabled = true,
        });

        var shelter = new Node3D { Name = "Shelter" };
        root.AddChild(SceneBuildUtil.AttachScriptToRoot(shelter, "res://scripts/nodes/Shelter.cs"));

        // The same player as a run, with the same controls. That is the point of
        // the room: walking to the armoury uses the keys that walk to a crate, so
        // there is nothing to learn at the door.
        var player = GD.Load<PackedScene>("res://scenes/Player.tscn")?.Instantiate<Node3D>();
        if (player == null)
        {
            GD.PushError("BuildBase: scenes/Player.tscn did not load");
            return false;
        }

        // Just inside the gate, facing the room. Arriving with your back to the
        // way out is what makes the gate feel like the way out.
        player.Position = new Vector3(0.0f, 0.0f, -4.5f);
        root.AddChild(player);

        root.AddChild(BuildCameraRig());
        root.AddChild(BuildPanel());

        bool ok = SceneBuildUtil.PackAndSave(root, "res://scenes/Base.tscn");
        root.Free();
        return ok;
    }

    /// Indoors, so no sky and no depth fog.
    ///
    /// The run's environment exists to hide distance; there is no distance here,
    /// and fog in a room eleven metres across would grey out the far wall for no
    /// reason. Ambient is warmer than the run's cool sky bounce — the shelter
    /// should not look like the place outside it.
    private static WorldEnvironment BuildEnvironment()
    {
        var environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color,
            BackgroundColor = new Color(0.05f, 0.05f, 0.06f),

            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = new Color(0.52f, 0.46f, 0.40f),
            AmbientLightEnergy = 0.60f,

            TonemapMode = Godot.Environment.ToneMapper.Filmic,
            TonemapExposure = 1.05f,
            TonemapWhite = 2.2f,

            AdjustmentEnabled = true,
            AdjustmentContrast = 1.05f,
            AdjustmentSaturation = 1.06f,
            AdjustmentBrightness = 1.0f,
        };

        return new WorldEnvironment { Name = "Environment", Environment = environment };
    }

    private static Node3D BuildCameraRig()
    {
        var rig = new Node3D { Name = "CameraRig" };

        float tilt = Mathf.DegToRad(-CameraTiltDegrees);
        rig.AddChild(new Camera3D
        {
            Name = "Camera",
            Projection = Camera3D.ProjectionType.Perspective,
            Fov = CameraFov,
            Near = 0.15f,
            Far = 80.0f,
            Position = new Vector3(0.0f, CameraDistance * Mathf.Sin(tilt), CameraDistance * Mathf.Cos(tilt)),
            RotationDegrees = new Vector3(CameraTiltDegrees, 0.0f, 0.0f),
        });

        var live = (CameraRig)SceneBuildUtil.AttachScriptToRoot(rig, "res://scripts/nodes/CameraRig.cs");
        live.TargetPath = new NodePath("../Player");
        return live;
    }

    /// The panel, which is what is left of the old screen.
    ///
    /// A `Control` child of the 3D root rather than the root itself, so
    /// `BaseScreen` can find the shelter as a sibling. Two columns still: the
    /// left is the page for whatever fitting the player is standing at, the right
    /// is what the two keys do there.
    private static Node BuildPanel()
    {
        var panel = new Control
        {
            Name = "Panel",
            AnchorRight = 1.0f,
            AnchorBottom = 1.0f,

            // The room has to stay clickable-through — this is an overlay, not a
            // screen. Without it the Control eats the mouse and right-drag stops
            // turning the camera.
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };

        // A backing panel behind the text only, not across the whole screen.
        // Text drawn straight onto a lit room is unreadable wherever the room
        // happens to be pale, and a full-screen scrim would hide the room the
        // phase exists to show.
        panel.AddChild(new ColorRect
        {
            Name = "ScreenBack",
            Color = new Color(0.05f, 0.05f, 0.07f, 0.72f),
            Position = new Vector2(48.0f, 40.0f),
            Size = new Vector2(880.0f, 560.0f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        });

        panel.AddChild(new ColorRect
        {
            Name = "SideBack",
            Color = new Color(0.05f, 0.05f, 0.07f, 0.72f),
            Position = new Vector2(1440.0f, 40.0f),
            Size = new Vector2(430.0f, 400.0f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        });

        panel.AddChild(new Label
        {
            Name = "Screen",
            Position = new Vector2(64.0f, 52.0f),
            Size = new Vector2(850.0f, 540.0f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        });

        panel.AddChild(new Label
        {
            Name = "Side",
            Position = new Vector2(1456.0f, 52.0f),
            Size = new Vector2(400.0f, 380.0f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        });

        return SceneBuildUtil.AttachScriptToRoot(panel, "res://scripts/nodes/BaseScreen.cs");
    }
}
