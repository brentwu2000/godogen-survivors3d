using Godot;

/// Stands a biome's furniture in a row and photographs it.
///
///   godot --script test/PropShot.cs                 every kind there is
///   godot --script test/PropShot.cs -- set:city     one biome's set, in role order
///   godot --script test/PropShot.cs -- front        straight on, for proportions
///
/// The counterpart of `BodyShot`, and it exists for the same reason: every
/// judgement made about an asset viewed on its own has been wrong. A prop is
/// authored in a unit footprint and drawn at whatever size the layout decided, so
/// "does this read as a bus" is a question about the *set* — next to the wall it
/// shares a street with, at the height the role gives it, under the game's sun.
///
/// Scaled the way the arena scales them. A prop photographed at unit size is not
/// the prop the player sees; the generator stretches cover along its footprint,
/// and something that looks right as a cube can arrive as a smear.
public partial class PropShot : SceneTree
{
    private const string OutputPath = "res://screenshots/props.png";

    private const float Spacing = 6.0f;
    private const int WarmupFrames = 8;

    /// The arena's own tilt, so the foreshortening is the game's. A lineup shot
    /// flat-on flatters everything equally and tells you nothing about what a
    /// piece of cover looks like from where the player actually stands.
    private const float CameraTiltDegrees = 26.0f;
    private const float HorizontalHalfAngle = 32.0f;
    private const float VerticalHalfAngle = 20.0f;
    private const float Margin = 1.12f;

    private int _frame;
    private bool _front;
    private bool _landmarks;
    private string _set = string.Empty;
    private string _output = OutputPath;

    public override void _Initialize()
    {
        // The guard, first, before anything is built.
        //
        // Every capture script in this folder needs it and this one shipped
        // without it — which is precisely the failure `Display` was written for.
        // A capture script run headless does not fail: it spins, printing
        // nothing, and a probe that has hung looks exactly like a probe that is
        // slow. Two sweeps stalled at `MusicProbe`, the entry alphabetically
        // before this one, and I read it twice as "the long run probes are slow".
        if (!Display.Required(this, "PropShot"))
            return;

        foreach (string argument in OS.GetCmdlineUserArgs())
        {
            if (argument == "front")
                _front = true;

            if (argument == "landmarks")
                _landmarks = true;

            if (argument.StartsWith("set:"))
                _set = argument[4..];

            // Named so two sets can be compared side by side afterwards. A tool
            // that always writes the same file makes the comparison a matter of
            // remembering what the last run was.
            if (argument.StartsWith("out:"))
                _output = $"res://screenshots/{argument[4..]}.png";
        }

        // Cover and scenery are photographed apart, and that is not tidiness.
        //
        // A tower block is twenty-two metres and a traffic barrier is one. Framed
        // together, the camera backs off far enough for the landmark and every
        // piece of cover in the row becomes forty pixels of grey — which is the
        // exact question the tool exists to answer, rendered unanswerable. The
        // first city lineup was seven props of which five could not be read.
        PropKind[] kinds = System.Array.FindAll(Kinds(),
            kind => PropLibrary.IsLandmark(kind) == _landmarks);

        if (kinds.Length == 0)
        {
            GD.PushError($"nothing to show — that set has no {(_landmarks ? "landmarks" : "cover")}");
            Quit(1);
            return;
        }

        var root = new Node3D { Name = "PropShot" };
        GetRoot().AddChild(root);

        root.AddChild(new DirectionalLight3D
        {
            // The arena's key light, near enough. A lineup lit from the front
            // shows every face equally and hides the one thing a box prop lives
            // or dies by, which is whether its silhouette breaks up.
            Rotation = new Vector3(Mathf.DegToRad(-48.0f), Mathf.DegToRad(38.0f), 0.0f),
            LightEnergy = 1.15f,
            ShadowEnabled = true,
        });

        root.AddChild(new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = new Color(0.16f, 0.17f, 0.20f),
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = new Color(0.42f, 0.44f, 0.50f),
                AmbientLightEnergy = 0.55f,
            },
        });

        root.AddChild(new MeshInstance3D
        {
            Mesh = new PlaneMesh { Size = new Vector2(400.0f, 400.0f) },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.34f, 0.33f, 0.31f) },
        });

        StandardMaterial3D material = PropLibrary.Material();

        float span = kinds.Length * Spacing;
        float x = -span * 0.5f + Spacing * 0.5f;
        float tallest = 0.0f;

        foreach (PropKind kind in kinds)
        {
            ArrayMesh mesh = PropLibrary.Build(kind);
            mesh.SurfaceSetMaterial(0, material);

            // The footprint the arena would give it. Cover is stretched to the
            // block it fills and landmarks are placed at a fixed four metres, so
            // photographing everything at 1x would show two different products.
            float footprint = PropLibrary.IsLandmark(kind) ? 4.0f : 3.2f;

            root.AddChild(new MeshInstance3D
            {
                Name = kind.ToString(),
                Mesh = mesh,
                Position = new Vector3(x, 0.0f, 0.0f),
                Scale = new Vector3(footprint, 1.0f, footprint),
            });

            tallest = Mathf.Max(tallest, PropLibrary.Height(kind));
            x += Spacing;
        }

        // Framed to contain the row, the same arithmetic `BodyShot` uses. Written
        // out rather than shared because the two differ in what "tallest" means —
        // a landmark is twenty metres and a body is two, and a camera that backed
        // off far enough for a tower block would render the cover as specks.
        float halfSpan = span * 0.5f + Spacing * 0.25f;
        float forWidth = halfSpan / Mathf.Tan(Mathf.DegToRad(HorizontalHalfAngle));
        float forHeight = tallest * 0.5f / Mathf.Tan(Mathf.DegToRad(VerticalHalfAngle));
        float distance = Mathf.Max(forWidth, forHeight) * Margin;

        float tilt = _front ? 0.0f : Mathf.DegToRad(CameraTiltDegrees);
        var aim = new Vector3(0.0f, tallest * 0.42f, 0.0f);

        Vector3 eye = aim + new Vector3(0.0f,
                                        Mathf.Sin(tilt) * distance,
                                        Mathf.Cos(tilt) * distance);

        var camera = new Camera3D { Fov = 60.0f };
        root.AddChild(camera);

        // `LookAt` needs the node in the tree and this runs during `_Initialize`,
        // where it is not yet — it fails with "Node not inside tree" and leaves
        // the camera at the origin pointing down -Z, which photographs the floor.
        camera.LookAtFromPosition(eye, aim, Vector3.Up);

        GD.Print($"{kinds.Length} props, tallest {tallest:F1} m, camera at {distance:F1} m");
    }

    /// Which set to photograph. Named sets come from `PropLibrary` rather than
    /// from a list here, so a set added there appears in the tool without being
    /// added twice — the same rule the enemy variants ended up needing.
    private PropKind[] Kinds() => _set switch
    {
        "" or "all" => System.Enum.GetValues<PropKind>(),
        "default" or "yard" => PropLibrary.DefaultSet,
        "city" => PropLibrary.CitySet,
        _ => FromBiome(_set),
    };

    private static PropKind[] FromBiome(string name)
    {
        foreach (BiomeResource biome in BiomeBook.All)
        {
            if (biome.BiomeName.ToLower().Replace(" ", "_") == name.ToLower())
                return biome.Kinds();
        }

        GD.PushWarning($"no set or biome named {name} — showing everything");
        return System.Enum.GetValues<PropKind>();
    }

    public override bool _Process(double delta)
    {
        // A few frames of warm-up. The first is drawn before the shadow atlas has
        // anything in it, and a lineup with no shadows is a lineup of cut-outs.
        if (++_frame < WarmupFrames)
            return false;

        RenderingServer.ForceDraw();

        Image image = GetRoot().GetTexture().GetImage();
        string path = ProjectSettings.GlobalizePath(_output);
        image.SavePng(path);

        GD.Print($"Wrote {path}");
        Quit();
        return true;
    }
}
