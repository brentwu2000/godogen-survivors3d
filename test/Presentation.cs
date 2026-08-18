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

    /// Short enough that the roster actually changes on camera.
    ///
    /// The clip is 23 s long and the horde's composition is a function of how far
    /// into the run it is, so a 110 s run only ever reaches an intensity of about
    /// 0.2 — which is walkers, a few runners, and none of the three variants that
    /// were the whole point of drawing them. Forty seconds puts the brute (0.45)
    /// and the bloater (0.6) inside the window, and still leaves the run far from
    /// timing out before the extraction stage.
    private const float RunSeconds = 40.0f;
    private const float SpawnRingMin = 9.0f;
    private const float SpawnRingMax = 24.0f;

    /// Run intensity the opening crowd is drawn from.
    ///
    /// 0.65 put every variant on camera and killed the bot at 23.6 s, eight
    /// frames before the previous cut — so the clip that looked like it was about
    /// to extract was in fact about to end in a death. Half is past the brute's
    /// unlock at 0.45, which is the variant worth seeing, and leaves the run
    /// survivable long enough to finish the loop the film exists to show.
    private const float OpeningIntensity = 0.5f;

    private Player _player = null!;
    private Horde _horde = null!;
    private RunDirector _director = null!;
    private ExtractionZone _extraction = null!;
    private LootContainer[] _crates = System.Array.Empty<LootContainer>();

    private int _stage;
    private int _crateIndex;
    private int _tick;
    private bool _bound;
    private RunGrowth _growth = null!;
    private int _pickHeldFor;

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
        director.EndSpawnRate = 11.0f;
        director.SpawnDistanceMin = 20.0f;
        director.SpawnDistanceMax = 28.0f;

        // Same rule as the other capture tools: filming a run does not bank it.
        var meta = scene.GetNodeOrNull<MetaManager>("MetaManager");
        if (meta != null)
            meta.Ephemeral = true;

        // A fixed layout, set before the scene enters the tree because the
        // generator runs in _Ready. Without it every run of this script would
        // face a different map, and a number that changes for reasons the test
        // did not choose is not a measurement.
        var level = scene.GetNodeOrNull<LevelGenerator>("Level");
        if (level != null)
            level.Seed = 0xC17E4A9BUL;

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
            _extraction = scene.GetNode<RunDirector>("RunDirector").PrimaryPad!;
            _growth = scene.GetNode<RunGrowth>("RunGrowth");

            // Two crates on opposite sides of the arena: the detour is what makes
            // the walk back to the pad worth filming.
            _crates = new[]
            {
                scene.GetNode<LootContainer>("LootContainers/Crate0"),
                scene.GetNode<LootContainer>("LootContainers/Crate2"),
            };

            // Repopulate the opening horde from the late roster.
            //
            // Horde.InitialSpawn only ever makes walkers, and composition is a
            // function of how far into the run it is — so even at forty seconds
            // the brute and the bloater arrive in the last five, which is most of
            // a clip spent showing one variant. Seeding the opening crowd at a
            // mid-run intensity puts the whole roster on camera from the first
            // frame while leaving the escalation intact: the director keeps
            // driving SpawnIntensity from the clock as usual after this.
            //
            // Capture-only compression, exactly like the shortened run and the
            // raised spawn rate. Nothing in Horde or RunDirector changes.
            _horde.Pool.Clear();
            _horde.SpawnIntensity = OpeningIntensity;
            for (int i = 0; i < OpeningHorde; i++)
            {
                float angle = i * 0.61f;
                float radius = SpawnRingMin + (i % 9) * ((SpawnRingMax - SpawnRingMin) / 9.0f);
                _horde.SpawnByIntensity(_player.GlobalPosition + new Vector3(
                    Mathf.Cos(angle) * radius, 0.0f, Mathf.Sin(angle) * radius));
            }

            _bound = true;
        }

        _tick++;
        AnswerTheOffer();

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

    /// Takes a level-up card a moment after it appears.
    ///
    /// Not optional for a capture. The offer stays on screen until it is answered
    /// — that is the design, it does not pause and it does not expire — so a
    /// script that never presses a key leaves three cards sitting over the lower
    /// third for the entire clip, which reads as a stuck interface rather than as
    /// a choice nobody made. It is the same failure as the stale hold bar, and
    /// like that one no exit-code probe can see it.
    ///
    /// Held for a few frames first so the cards are legible before they vanish.
    private void AnswerTheOffer()
    {
        if (!_growth.HasOffer)
        {
            _pickHeldFor = 0;
            return;
        }

        if (++_pickHeldFor < 45)
            return;

        _pickHeldFor = 0;
        _growth.Choose(0);
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
