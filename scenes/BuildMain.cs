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

    /// The design resolution from project.godot. The stretch mode is
    /// canvas_items, so the readout is laid out once at this size and scaled to
    /// whatever the window is.
    private const float ScreenWidth = 1920.0f;
    private const float ScreenHeight = 1080.0f;

    /// Matches RunGrowth.OfferSize. The cards are built once and shown or hidden,
    /// because a scene that grows nodes mid-run allocates during the exact moment
    /// the offer exists to interrupt.
    private const int OfferCards = 3;

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

        // Last, because it subscribes to everything above it — including the loot
        // containers the level generator put in the tree during its own _Ready.
        var sound = new Node { Name = "Sound" };
        root.AddChild(SceneBuildUtil.AttachScriptToRoot(sound, "res://scripts/nodes/SoundDirector.cs"));

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

    /// The readout, as structure only. Every colour, string and width lives in
    /// Hud.cs — a builder that also styled would mean regenerating a scene to
    /// move a bar two pixels.
    ///
    /// Laid out for where the eye already is. Health and the backpack are the two
    /// things the player is trading against each other, so they share the top
    /// left corner; the clock and the payout-if-you-leave-now share the top
    /// centre, because that pair is the decision the whole run is about; the way
    /// out is top right, on its own, because it answers a different question.
    private static CanvasLayer BuildHud()
    {
        var hud = new CanvasLayer { Name = "Hud" };

        // Behind everything else, and never in the way of a click.
        hud.AddChild(new ColorRect
        {
            Name = "Vignette",
            Position = Vector2.Zero,
            Size = new Vector2(ScreenWidth, ScreenHeight),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        });

        AddBar(hud, "Health", new Vector2(24.0f, 22.0f), new Vector2(360.0f, 26.0f));
        AddBar(hud, "Bag", new Vector2(24.0f, 58.0f), new Vector2(360.0f, 18.0f));
        AddBar(hud, "Level", new Vector2(24.0f, 122.0f), new Vector2(360.0f, 10.0f));

        hud.AddChild(new Label
        {
            Name = "Arms",
            Position = new Vector2(24.0f, 84.0f),
            Size = new Vector2(560.0f, 36.0f),
        });

        hud.AddChild(new Label
        {
            Name = "Keys",
            Position = new Vector2(24.0f, 140.0f),
            Size = new Vector2(680.0f, 30.0f),
        });

        hud.AddChild(new Label
        {
            Name = "Clock",
            Position = new Vector2(ScreenWidth * 0.5f - 300.0f, 16.0f),
            Size = new Vector2(600.0f, 52.0f),
        });

        hud.AddChild(new Label
        {
            Name = "Payout",
            Position = new Vector2(ScreenWidth * 0.5f - 300.0f, 66.0f),
            Size = new Vector2(600.0f, 34.0f),
        });

        hud.AddChild(new Label
        {
            Name = "Exit",
            Position = new Vector2(ScreenWidth - 500.0f - 24.0f, 22.0f),
            Size = new Vector2(500.0f, 90.0f),
        });

        // Bottom centre, above the offer: a hold bar is the one thing on screen
        // that is counting while the player stands still, so it sits where they
        // are already looking — at their own character.
        AddBar(hud, "Hold", new Vector2(ScreenWidth * 0.5f - 240.0f, ScreenHeight - 108.0f),
               new Vector2(480.0f, 22.0f));

        for (int i = 0; i < OfferCards; i++)
        {
            const float cardWidth = 300.0f;
            const float gap = 18.0f;
            float total = OfferCards * cardWidth + (OfferCards - 1) * gap;
            float x = ScreenWidth * 0.5f - total * 0.5f + i * (cardWidth + gap);

            hud.AddChild(new ColorRect
            {
                Name = $"Card{i}",
                Position = new Vector2(x, ScreenHeight - 226.0f),
                Size = new Vector2(cardWidth, 96.0f),
                MouseFilter = Control.MouseFilterEnum.Ignore,
            });

            hud.AddChild(new Label
            {
                Name = $"Card{i}Text",
                Position = new Vector2(x, ScreenHeight - 210.0f),
                Size = new Vector2(cardWidth, 70.0f),
            });
        }

        hud.AddChild(new Label
        {
            Name = "Banner",

            // Upper third, not centre: the player sprite sits dead centre, and a
            // banner over it hides the moment it is announcing.
            Position = new Vector2(0.0f, 170.0f),
            Size = new Vector2(ScreenWidth, 200.0f),
        });

        return (Hud)SceneBuildUtil.AttachScriptToRoot(hud, "res://scripts/nodes/Hud.cs");
    }

    /// A bar is a track, a fill and a caption over the top. Three nodes rather
    /// than a ProgressBar because the fill has to change colour with its own
    /// meaning — health goes red, the backpack goes amber as it closes on full —
    /// and theming a ProgressBar per state is more code than moving a rectangle.
    private static void AddBar(CanvasLayer hud, string name, Vector2 position, Vector2 size)
    {
        hud.AddChild(new ColorRect
        {
            Name = $"{name}Back",
            Position = position,
            Size = size,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        });

        hud.AddChild(new ColorRect
        {
            Name = $"{name}Fill",
            Position = position + new Vector2(2.0f, 2.0f),
            Size = size - new Vector2(4.0f, 4.0f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        });

        hud.AddChild(new Label
        {
            Name = $"{name}Text",
            Position = position + new Vector2(8.0f, -1.0f),
            Size = size,
        });
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
