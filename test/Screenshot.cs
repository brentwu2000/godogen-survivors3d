using Godot;

/// Renders the main scene and writes a still. Reused whenever a change needs
/// visual confirmation rather than a passing build.
///
///   godot --script test/Screenshot.cs
///   godot --script test/Screenshot.cs -- 0 0 mixed throw
///
/// Not headless — the null rendering driver has nothing to capture.
///
/// "mixed" fills the field from the late-run roster. At the start of a run the
/// horde is walkers only by design, so a default shot cannot show whether the
/// variants render at all — and no exit-code probe will ever tell you they don't.
public partial class Screenshot : SceneTree
{
    private const string ScenePath = "res://scenes/Main.tscn";
    private const string OutputPath = "res://screenshots/main.png";

    /// Long enough for the camera rig to settle onto the player; its follow is a
    /// lerp, so frame one still shows it at the origin. Override with "frames:N"
    /// to photograph something that needs the run to get going first — a level-up
    /// offer, say, which cannot appear until enough has been killed to earn it.
    private const int WarmupFrames = 30;
    private int _warmup = WarmupFrames;

    private int _frame;
    private Vector3? _teleport;
    private bool _mixed;
    private bool _boss;
    private bool _throw;
    private bool _flash;
    private bool _fx;
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

        // Capture tools do not spend the player's save. A run that ends while
        // being photographed still banks, and these had been writing credits and
        // practice into the real profile as a side effect of taking a picture.
        var meta = scene.GetNodeOrNull<MetaManager>("MetaManager");
        if (meta != null)
            meta.Ephemeral = true;

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

        foreach (string arg in args)
        {
            _mixed |= arg == "mixed";
            _boss |= arg == "boss";
            _throw |= arg == "throw";
            _flash |= arg == "flash";
            _fx |= arg == "fx";
            if (arg.StartsWith("frames:") && int.TryParse(arg[7..], out int frames))
                _warmup = Mathf.Max(1, frames);
        }
    }

    public override bool _Process(double delta)
    {
        // Not in _Initialize: nodes are not inside the tree yet, so setting a
        // global transform there silently does nothing.
        if (_frame == 0 && _scene != null)
        {
            var player = _scene.GetNodeOrNull<Node3D>("Player");
            if (_teleport.HasValue && player != null)
                player.GlobalPosition = _teleport.Value;

            var horde = _scene.GetNodeOrNull<Horde>("Horde");
            if (_mixed && horde != null && player != null)
            {
                horde.Pool.Clear();
                horde.SpawnIntensity = 1.0f;
                for (int i = 0; i < 60; i++)
                {
                    float angle = i * 0.61f;
                    float radius = 4.0f + (i % 7);
                    horde.SpawnByIntensity(player.GlobalPosition + new Vector3(
                        Mathf.Cos(angle) * radius, 0.0f, Mathf.Sin(angle) * radius));
                }
            }

            // The boss, and one of each mark next to it. Both are things no
            // exit-code probe can judge: whether the tint is legible at a glance
            // and whether 5.5 m reads as enormous rather than as broken scale are
            // questions only a picture answers.
            if (_boss && horde != null && player != null)
            {
                Vector3 at = player.GlobalPosition;
                // Up-screen but not off it. The quad is ground-anchored, so a
                // 5.5 m boss placed at the same distance as a 3 m brute puts its
                // head above the top edge — the first version of this shot
                // photographed a pair of feet.
                horde.Spawn(at + new Vector3(0.0f, 0.0f, -6.0f), 5);
                horde.Spawn(at + new Vector3(-6.0f, 0.0f, -3.5f), 2, EliteKind.Armoured);
                horde.Spawn(at + new Vector3(-3.0f, 0.0f, 1.5f), 0, EliteKind.Swift);
                horde.Spawn(at + new Vector3(3.0f, 0.0f, -2.0f), 3, EliteKind.Volatile);
                horde.Spawn(at + new Vector3(6.0f, 0.0f, -3.5f), 2);
            }

            // After the crowd, not before. Thrown items resolve instantly and the
            // fire burns out, so the only way to photograph either is to spend
            // one on the frame being taken — and spending it on an empty field
            // photographs nothing.
            if (_throw && player is Player thrower)
            {
                foreach (string name in new[] { "molotov", "pipe_bomb" })
                {
                    var item = GD.Load<ItemResource>($"res://resources/items/{name}.tres");
                    if (item != null)
                        thrower.Backpack.TryAdd(item, 1);
                }

                thrower.TryThrow();
                thrower.TryThrow();
            }
        }

        // Hit feedback lasts about a tenth of a second, so photographing it by
        // firing and hoping is not a plan. Lighting alternate instances is the
        // only way to check that the flash channel survives a change to the
        // instance buffer — and getting that layout wrong shows up as sprites
        // that are subtly, permanently the wrong colour rather than as an error.
        //
        // Every frame, not just the last one: the horde uploads its buffer from
        // _PhysicsProcess and the viewport this reads is the frame already drawn,
        // so a value written on the capture frame is a value nothing has rendered
        // yet.
        if (_flash && _scene?.GetNodeOrNull<Horde>("Horde") is { } lit)
        {
            for (int i = 0; i < lit.Pool.Count; i++)
                lit.Pool.HitFlash[i] = i % 2 == 0 ? 1.0f : 0.0f;
        }

        // Combat effects last between a twentieth and a second, so a still that
        // happens to land on one is luck rather than a test. This drives the real
        // paths — kills and a detonation — a few frames before the shutter, which
        // is the only way to photograph them at the size they actually are.
        if (_fx && _frame == _warmup - 4 && _scene != null)
        {
            var horde = _scene.GetNodeOrNull<Horde>("Horde");
            var player = _scene.GetNodeOrNull<Player>("Player");

            if (horde != null && player != null)
            {
                for (int i = 0; i < 10 && horde.Pool.Count > 0; i++)
                    horde.Damage(horde.Pool.Count - 1, 9999.0f, Vector2.Zero);

                horde.Detonate(player.GlobalPosition + new Vector3(5.0f, 0.0f, -3.0f), 4.5f, 55.0f);
                horde.Hazards.Add(player.GlobalPosition + new Vector3(-6.0f, 0.0f, 2.0f), 3.5f, 22.0f, 7.0f);
            }
        }

        if (++_frame < _warmup)
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
