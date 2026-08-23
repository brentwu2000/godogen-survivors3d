using Godot;

/// A lineup of every body, side by side, mid-stride, under the game camera.
///
///   godot --script test/BodyShot.cs
///   godot --script test/BodyShot.cs -- still front
///
/// Needs a real display — it exists to be looked at.
///
/// The bodies are the one part of this game no probe can review. `BodyProbe`
/// proves they are closed, correctly scaled, in the right buckets and rigged; it
/// cannot say whether they read as people. Every attempt to judge that from a
/// gameplay screenshot has failed the same way: the horde walks into the lens and
/// what fills the frame is one enemy's shoulder from forty centimetres, or the
/// only bodies at a sensible distance are behind the fog.
///
/// So this puts them in a row on flat ground with nothing else in the scene, at
/// the camera's real tilt and field of view. `mid` is the default because a body
/// standing still hides exactly the thing the articulation was added for.
public partial class BodyShot : SceneTree
{
    private const string OutputPath = "res://screenshots/bodies.png";

    /// The game's own camera, copied from `BuildMain`. Being wrong here is
    /// self-announcing: these bodies have known heights, so a camera that does
    /// not match the game produces a picture that disagrees with the game.
    private const float CameraTiltDegrees = -26.0f;
    private const float CameraFov = 52.0f;

    /// Framed to fit rather than placed by hand.
    ///
    /// The first version put the camera at a fixed 11.5 m and cut the boss in
    /// half — it is 5.5 m tall and stands at the end of the row, so it left the
    /// frame in both directions at once. A lineup that cannot show its largest
    /// member is worse than no lineup, because the one it omits is the one whose
    /// proportions are hardest to get right.
    ///
    /// Half-angles for a 52° vertical field at 16:9. The horizontal one is
    /// `atan(tan(26°) * 16/9)`, which is where 40.9 comes from.
    private const float VerticalHalfAngle = 26.0f;
    private const float HorizontalHalfAngle = 40.9f;

    /// Room around the subjects, as a fraction. Without it the outermost body
    /// touches the frame edge, which reads as a cropping accident even when it
    /// is not one.
    private const float Margin = 1.22f;

    private const float Spacing = 2.3f;
    private const int WarmupFrames = 6;

    /// Walking, at a pace that puts the legs somewhere useful.
    ///
    /// The stride is a function of distance travelled, so a still body has its
    /// legs together and its arms down — which is the pose in which a jointed
    /// limb and a straight one look identical. `still` is available for checking
    /// proportions; the default is the one that shows the work.
    private const float WalkSpeed = 3.2f;

    private readonly System.Collections.Generic.List<SoloBody> _bodies = new();
    private float _stride;
    private bool _still;
    private bool _front;
    private int _frame;

    public override void _Initialize()
    {
        if (!Display.Required(this, "BodyShot"))
            return;

        foreach (string argument in OS.GetCmdlineUserArgs())
        {
            _still |= argument == "still";
            _front |= argument == "front";
        }

        var shader = GD.Load<Shader>("res://assets/shaders/body.gdshader");
        if (shader == null)
        {
            GD.PushError("Missing res://assets/shaders/body.gdshader");
            Quit(1);
            return;
        }

        var root = new Node3D { Name = "Lineup" };
        GetRoot().AddChild(root);

        // A plain lit environment rather than the game's. The arena is
        // deliberately dark with fog closing at twenty-four metres, which is
        // correct for playing and useless for looking at a model.
        var light = new DirectionalLight3D
        {
            RotationDegrees = new Vector3(-52.0f, -38.0f, 0.0f),
            LightEnergy = 1.15f,
        };
        root.AddChild(light);

        var environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color,
            BackgroundColor = new Color(0.20f, 0.21f, 0.24f),
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = new Color(0.42f, 0.44f, 0.50f),
            AmbientLightEnergy = 0.75f,
        };

        root.AddChild(new WorldEnvironment { Environment = environment });

        // A floor, so the feet have something to stand on and cast onto. Without
        // one every body floats in a void and its footing cannot be judged.
        var floor = new MeshInstance3D
        {
            Mesh = new PlaneMesh { Size = new Vector2(40.0f, 40.0f) },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.34f, 0.33f, 0.31f) },
        };
        root.AddChild(floor);

        string[] names = { "walker", "runner", "brute", "bloater", "spitter", "boss" };
        float[] heights = HeightsFor(names);

        float span = names.Length * Spacing;
        float x = -span * 0.5f;

        // The player first, on the left, because the question the lineup exists
        // to answer is whether it can be told from the horde at a glance.
        Add(shader, BodyMeshLibrary.ForPlayer(1.75f), root, x);
        x += Spacing;

        for (int i = 0; i < names.Length; i++)
        {
            Add(shader, BodyMeshLibrary.ForVariant(names[i], heights[i]), root, x);
            x += Spacing;
        }

        // Everything is placed; now find a camera that contains it.
        float tallest = 0.0f;
        foreach (BodyMeshLibrary.Build spec in _specs)
            tallest = Mathf.Max(tallest, BodyMeshLibrary.StandingHeight(spec));

        float halfSpan = span * 0.5f + Spacing * 0.5f;

        // Whichever axis needs the camera further away wins. The vertical extent
        // is measured about the aim point, so it is half the tallest body rather
        // than all of it.
        float forWidth = halfSpan / Mathf.Tan(Mathf.DegToRad(HorizontalHalfAngle));
        float forHeight = tallest * 0.5f / Mathf.Tan(Mathf.DegToRad(VerticalHalfAngle));
        float distance = Mathf.Max(forWidth, forHeight) * Margin;

        // Aimed at the middle of the tallest body, then backed off along the
        // tilt. The tilt is the game's, so the foreshortening is the game's —
        // that is the only reason to keep a downward angle in a lineup at all.
        float tilt = Mathf.DegToRad(CameraTiltDegrees);
        var aim = new Vector3(0.0f, tallest * 0.5f, 0.0f);

        root.AddChild(new Camera3D
        {
            Projection = Camera3D.ProjectionType.Perspective,
            Fov = CameraFov,
            Position = aim + new Vector3(0.0f, -Mathf.Sin(tilt) * distance, Mathf.Cos(tilt) * distance),
            RotationDegrees = new Vector3(CameraTiltDegrees, 0.0f, 0.0f),
            Current = true,
        });
    }

    private readonly System.Collections.Generic.List<BodyMeshLibrary.Build> _specs = new();

    /// Design heights from the enemy table, not from a list here.
    ///
    /// The table is what the game balances against, and a lineup drawn at heights
    /// this file invented would be a picture of bodies that do not exist.
    private static float[] HeightsFor(string[] names)
    {
        var heights = new float[names.Length];
        for (int i = 0; i < names.Length; i++)
        {
            var resource = GD.Load<EnemyTypeResource>($"res://resources/enemies/{names[i]}.tres");
            heights[i] = resource?.DesignHeightMeters ?? 1.8f;
        }

        return heights;
    }

    private void Add(Shader shader, BodyMeshLibrary.Build spec, Node3D root, float x)
    {
        var body = new SoloBody(shader, spec, 40.0f);
        root.AddChild(body.Node);
        _bodies.Add(body);
        _specs.Add(spec);

        _placements.Add(new Vector3(x, 0.0f, 0.0f));
    }

    private readonly System.Collections.Generic.List<Vector3> _placements = new();

    public override bool _Process(double delta)
    {
        // Every body on the same phase of the same stride, which is the only way
        // two silhouettes can be compared at all — bodies caught at different
        // points of a walk differ for a reason that has nothing to do with how
        // they were built.
        float speed = _still ? 0.0f : WalkSpeed;
        _stride = BodyRenderer.AdvanceStride(_stride, speed, (float)delta);

        // Three quarters turned, so the lineup shows a front three-quarter view:
        // straight on hides the lean and the arm swing, and side on hides the
        // shoulders. `front` overrides it for checking symmetry.
        float yaw = _front ? Mathf.Pi : Mathf.Pi * 0.78f;

        // Each body advances its own stride, and they stay in step because they
        // all start at zero and all get the same speed and the same delta. That
        // is worth stating: bodies caught at different points of a walk differ
        // for a reason that has nothing to do with how they were built, and a
        // lineup that let them drift apart would be comparing poses.
        for (int i = 0; i < _bodies.Count; i++)
            _bodies[i].Update(_placements[i], yaw, speed, (float)delta, 0.0f);

        if (++_frame < WarmupFrames)
            return false;

        RenderingServer.ForceDraw();

        Image image = GetRoot().GetTexture().GetImage();
        string path = ProjectSettings.GlobalizePath(OutputPath);
        image.SavePng(path);

        GD.Print($"Wrote {path}");
        Quit();
        return true;
    }
}
