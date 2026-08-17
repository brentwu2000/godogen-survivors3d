using Godot;

/// Capture script for the proof video. Plays one compressed run — fight, loot,
/// secure, retreat, extract — entirely from script.
///
///   godot --write-movie screenshots/result/frame.png --fixed-fps 30 --quit-after 700 \
///         --script test/Presentation.cs
///
/// --fixed-fps decouples simulated time from render speed, so the clip is the
/// same length and the same motion on any machine. 700 frames at 30 is 23.3s,
/// which lands the EXTRACTED banner with a couple of seconds to read it.
///
/// The run is deliberately tuned tighter than the shipping numbers: a real run
/// spends its first minute nearly empty, which would be 60 seconds of an empty
/// field on camera.
public partial class Presentation : SceneTree
{
    private const float ArriveDistance = 1.2f;
    private const float AxisDeadzone = 0.25f;

    // Compressed pacing, for the camera only.
    private const int OpeningHorde = 70;
    private const float RunSeconds = 110.0f;
    private const float SpawnRingMin = 9.0f;
    private const float SpawnRingMax = 24.0f;

    private Player _player = null!;
    private Horde _horde = null!;
    private RunDirector _director = null!;
    private ExtractionZone _extraction = null!;
    private LootContainer[] _crates = System.Array.Empty<LootContainer>();

    private int _stage;
    private int _crateIndex;
    private int _tick;
    private bool _bound;

    public override void _Initialize()
    {
        var scene = GD.Load<PackedScene>("res://scenes/Main.tscn")?.Instantiate();
        if (scene == null)
        {
            GD.PushError("Missing res://scenes/Main.tscn");
            Quit(1);
            return;
        }

        // Tuned before the scene enters the tree, so _Ready sees the values.
        // Afterwards the horde has already spawned its opening ring.
        var horde = scene.GetNode<Horde>("Horde");
        horde.InitialSpawn = OpeningHorde;
        horde.SpawnRingMin = SpawnRingMin;
        horde.SpawnRingMax = SpawnRingMax;

        var director = scene.GetNode<RunDirector>("RunDirector");
        director.RunSeconds = RunSeconds;
        director.ExtractionOpensAt = 0.0f;
        director.StartSpawnRate = 6.0f;
        director.EndSpawnRate = 16.0f;
        director.SpawnDistanceMin = 20.0f;
        director.SpawnDistanceMax = 28.0f;

        GetRoot().AddChild(scene);
    }

    public override bool _PhysicsProcess(double delta)
    {
        if (!_bound)
        {
            Node scene = GetRoot().GetChild(GetRoot().GetChildCount() - 1);
            _player = scene.GetNode<Player>("Player");
            _horde = scene.GetNode<Horde>("Horde");
            _director = scene.GetNode<RunDirector>("RunDirector");
            _extraction = scene.GetNode<ExtractionZone>("ExtractionZone");

            // Two crates on opposite sides of the arena: the detour is what makes
            // the walk back to the pad worth filming.
            _crates = new[]
            {
                scene.GetNode<LootContainer>("LootContainers/Crate0"),
                scene.GetNode<LootContainer>("LootContainers/Crate2"),
            };

            _bound = true;
        }

        _tick++;

        switch (_stage)
        {
            case 0:
                // Walk to the crate, rifle firing on its own the whole way.
                if (Approach(_crates[_crateIndex].GlobalPosition))
                    _stage++;
                break;

            case 1:
                // Stand still and search — the stationary window the horde is
                // meant to punish.
                Release();
                if (_crates[_crateIndex].Looted)
                {
                    _player.TrySecureBest();
                    _player.TrySecureBest();
                    _stage = ++_crateIndex < _crates.Length ? 0 : 2;
                }
                break;

            case 2:
                if (Approach(_extraction.GlobalPosition))
                    _stage++;
                break;

            default:
                Release();
                break;
        }

        return false;
    }

    private bool Approach(Vector3 target)
    {
        Vector3 delta = target - _player.GlobalPosition;
        var flat = new Vector2(delta.X, delta.Z);
        if (flat.Length() <= ArriveDistance)
        {
            Release();
            return true;
        }

        Vector2 direction = flat.Normalized();
        Set("move_right", direction.X > AxisDeadzone);
        Set("move_left", direction.X < -AxisDeadzone);
        Set("move_down", direction.Y > AxisDeadzone);
        Set("move_up", direction.Y < -AxisDeadzone);
        return false;
    }

    private static void Set(string action, bool pressed)
    {
        if (pressed)
        {
            if (!Input.IsActionPressed(action))
                Input.ActionPress(action);
        }
        else if (Input.IsActionPressed(action))
        {
            Input.ActionRelease(action);
        }
    }

    private static void Release()
    {
        foreach (string action in new[] { "move_up", "move_down", "move_left", "move_right" })
        {
            if (Input.IsActionPressed(action))
                Input.ActionRelease(action);
        }
    }
}
