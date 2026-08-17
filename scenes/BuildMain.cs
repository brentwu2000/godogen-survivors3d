using Godot;

/// Builds scenes/Main.tscn. Run after BuildPlayer — leaf scenes first, parents
/// after, or the instance here resolves to nothing.
///
///   godot --headless --script scenes/BuildMain.cs
///
/// Builders construct the hierarchy, set properties, attach scripts, pack, and
/// quit. No runtime logic belongs here — no _Ready/_Process, signals, or state.
public partial class BuildMain : SceneTree
{
    // Orthographic framing: the camera sits on a 24m arc and tilts down 52°,
    // which puts the horizon out of frame and keeps ground distances readable.
    private const float CameraTiltDegrees = -52.0f;
    private const float CameraDistance = 24.0f;

    /// Vertical extent of the view in world units. 18 keeps a 2.2m character
    /// around 130px on a 1080p viewport while still showing roughly 32x18m of
    /// ground — enough room for a horde to close in from off-screen.
    private const float OrthoSize = 18.0f;

    private const float GroundSize = 120.0f;

    public override void _Initialize() => SceneBuildUtil.Run(this, Build);

    private static bool Build()
    {
        var root = new Node3D { Name = "Main" };

        root.AddChild(new DirectionalLight3D
        {
            Name = "Sun",
            RotationDegrees = new Vector3(-55.0f, -35.0f, 0.0f),
            ShadowEnabled = true,
        });

        root.AddChild(BuildGround(GroundSize));

        var player = GD.Load<PackedScene>("res://scenes/Player.tscn")?.Instantiate();
        if (player == null)
        {
            GD.PushError("Missing res://scenes/Player.tscn — run BuildPlayer.cs first.");
            return false;
        }
        player.Name = "Player";
        root.AddChild(player);

        root.AddChild(BuildCameraRig());

        // Empty containers, filled at runtime. The layout is a per-run decision
        // and cannot live in a packed scene; what the scene owes it is somewhere
        // to put the results, in the tree, before anything goes looking.
        root.AddChild(new Node3D { Name = "Obstacles" });
        root.AddChild(new Node3D { Name = "LootContainers" });
        root.AddChild(new Node3D { Name = "ExtractionZones" });

        // Before the horde, whose flow field bakes obstacles once in _Ready.
        // Generating after that bake gives enemies a map they walk straight
        // through while the screen shows walls.
        var level = new Node3D { Name = "Level" };
        root.AddChild(SceneBuildUtil.AttachScriptToRoot(level, "res://scripts/nodes/LevelGenerator.cs"));

        // Added last so Player, Obstacles and the zones are already in the tree
        // when the horde and the director look for them in _Ready.
        root.AddChild(BuildHorde());
        root.AddChild(BuildRunDirector());

        // After the horde, whose kill event it subscribes to, and before the meta
        // manager, which hands it the caps the equipped gear allows.
        var growth = new Node { Name = "RunGrowth" };
        root.AddChild(SceneBuildUtil.AttachScriptToRoot(growth, "res://scripts/nodes/RunGrowth.cs"));

        // After the director so its RunEnded signal exists to connect to, and
        // after the player so the loadout has something to equip.
        var meta = new Node { Name = "MetaManager" };
        root.AddChild(SceneBuildUtil.AttachScriptToRoot(meta, "res://scripts/nodes/MetaManager.cs"));

        root.AddChild(BuildHud());

        Node scripted = SceneBuildUtil.AttachScriptToRoot(root, "res://scripts/nodes/GameRoot.cs");
        bool ok = SceneBuildUtil.PackAndSave(scripted, "res://scenes/Main.tscn");
        scripted.Free();
        return ok;
    }

    private static Node3D BuildCameraRig()
    {
        var rig = new Node3D { Name = "CameraRig" };

        float tilt = Mathf.DegToRad(-CameraTiltDegrees);
        rig.AddChild(new Camera3D
        {
            Name = "Camera",
            Projection = Camera3D.ProjectionType.Orthogonal,
            Size = OrthoSize,
            Position = new Vector3(0.0f, CameraDistance * Mathf.Sin(tilt), CameraDistance * Mathf.Cos(tilt)),
            RotationDegrees = new Vector3(CameraTiltDegrees, 0.0f, 0.0f),
        });

        // Script last, once the hierarchy is complete — and only through the
        // helper, which owns the re-fetch that SetScript's disposal forces.
        var live = (CameraRig)SceneBuildUtil.AttachScriptToRoot(rig, "res://scripts/nodes/CameraRig.cs");
        live.TargetPath = new NodePath("../Player");
        return live;
    }

    private static Node3D BuildHorde()
    {
        var horde = new Node3D { Name = "Horde" };
        var live = (Horde)SceneBuildUtil.AttachScriptToRoot(horde, "res://scripts/nodes/Horde.cs");

        // A standing presence at spawn, with everything after it coming from the
        // run director's escalation curve rather than from a fixed pile.
        live.InitialSpawn = 24;
        return live;
    }

    private static Node3D BuildRunDirector()
    {
        var director = new Node3D { Name = "RunDirector" };
        return (RunDirector)SceneBuildUtil.AttachScriptToRoot(director, "res://scripts/nodes/RunDirector.cs");
    }

    private static CanvasLayer BuildHud()
    {
        var hud = new CanvasLayer { Name = "Hud" };

        hud.AddChild(new Label
        {
            Name = "Status",
            Position = new Vector2(24.0f, 18.0f),
            Size = new Vector2(680.0f, 150.0f),
        });

        hud.AddChild(new Label
        {
            Name = "Prompt",
            Position = new Vector2(0.0f, 900.0f),
            Size = new Vector2(1920.0f, 48.0f),
        });

        hud.AddChild(new Label
        {
            Name = "Banner",

            // Upper third, not centre: the player sprite sits dead centre, and a
            // banner over it hides the moment it is announcing.
            Position = new Vector2(0.0f, 170.0f),
            Size = new Vector2(1920.0f, 200.0f),
        });

        return (Hud)SceneBuildUtil.AttachScriptToRoot(hud, "res://scripts/nodes/Hud.cs");
    }

    private static StaticBody3D BuildGround(float size)
    {
        var body = new StaticBody3D { Name = "Ground" };

        body.AddChild(new MeshInstance3D
        {
            Name = "Mesh",
            Mesh = new PlaneMesh { Size = new Vector2(size, size) },
        });

        // A plane collider has no thickness for fast movers to miss, so the floor
        // is a thin box sunk to put its top face at y = 0.
        const float thickness = 1.0f;
        body.AddChild(new CollisionShape3D
        {
            Name = "Collision",
            Shape = new BoxShape3D { Size = new Vector3(size, thickness, size) },
            Position = new Vector3(0.0f, -thickness * 0.5f, 0.0f),
        });

        return body;
    }
}
