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

    // Compressed pacing, for the camera only.
    private const int OpeningHorde = 44;

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
    /// Just under the brute's unlock at 0.45, so the opening crowd is walkers,
    /// runners and spitters and the brutes *arrive* — the run's own escalation
    /// crosses 0.45 around eighteen seconds in, which is better film than having
    /// them there from the first frame anyway.
    ///
    /// It has been down twice. At 0.65 the bot died at 23.6 s, eight frames after
    /// the old cut, so the take that looked like it was about to extract was
    /// about to end in a death. At 0.5 it survived — until Phase 14 put real
    /// cover on the map and the same settings killed it at 15 s. That is not a
    /// camera problem: cover makes the horde pile up, which is exactly the
    /// balance question Phase 15 opens with.
    private const float OpeningIntensity = 0.4f;

    private Player _player = null!;
    private CameraRig? _rig;
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
        director.EndSpawnRate = 8.5f;
        director.SpawnDistanceMin = 20.0f;
        director.SpawnDistanceMax = 28.0f;

        // Same rule as the other capture tools: filming a run does not bank it.
        var meta = scene.GetNodeOrNull<MetaManager>("MetaManager");
        if (meta != null)
            meta.Ephemeral = true;

        // End on the debrief rather than on the banner. The report is the payoff
        // the whole run is for, and it waits for a key — which this script never
        // presses, so it simply stays up and the last seconds of the film are
        // what the run was worth instead of a static banner over a finished
        // arena. The profile is ephemeral, so nothing is spent to say so.
        GameSession.LaunchedFromBase = true;

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
            _rig = scene.GetNodeOrNull<CameraRig>("CameraRig");
            _horde = scene.GetNode<Horde>("Horde");
            _director = scene.GetNode<RunDirector>("RunDirector");
            _extraction = scene.GetNode<RunDirector>("RunDirector").PrimaryPad!;
            _growth = scene.GetNode<RunGrowth>("RunGrowth");

            // One crate, not two.
            //
            // Two was right when the bot walked in straight lines: the detour
            // filled the middle of the clip. Routing around real cover made those
            // same two legs long enough that the run had not reached the pad by
            // frame 900, and a film of the loop that stops before the loop closes
            // is not a film of the loop.
            _crates = new[] { scene.GetNode<LootContainer>("LootContainers/Crate0") };

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

        // See `BotDrive`: the horizontal keys turn the view now, so the old
        // four-key decomposition steered this straight into a spin.
        BotDrive.Steer(Navigate(target), _rig?.Yaw ?? 0.0f);
        return false;
    }

    /// Direction to walk toward `target`, routed around cover.
    ///
    /// This walked in a straight line until Phase 14 put real cover on the map,
    /// and then the bot spent the film pressed against a container while the
    /// horde ate it — the run died at fifteen seconds having banked the same 98
    /// every take. AutoPlay learned exactly this in Phase 10 and got a flow
    /// field; the capture script did not, because at the time the arena was
    /// five grey boxes and a straight line was fine.
    ///
    /// Its own field rather than the horde's: the horde rebuilds around the
    /// player every few ticks, so borrowing it would have the bot and the enemies
    /// fighting over which way the arrows point.
    private Vector2 Navigate(Vector3 target)
    {
        Vector3 delta = target - _player.GlobalPosition;
        var straight = new Vector2(delta.X, delta.Z).Normalized();

        if (_navField == null)
        {
            _navField = new FlowField(Vector2.Zero, _horde.ArenaExtent, 1.5f);

            // Inflated by roughly a body's radius, so the route is one the player
            // can physically walk rather than one that scrapes every corner and
            // catches on the collision shape.
            Node? obstacles = _player.GetParent()?.GetNodeOrNull("Obstacles");
            if (obstacles != null)
            {
                foreach (Node child in obstacles.GetChildren())
                {
                    if (child is not Node3D body ||
                        body.GetNodeOrNull<CollisionShape3D>("Collision")?.Shape is not BoxShape3D box)
                    {
                        continue;
                    }

                    _navField.BlockBox(
                        new Vector2(body.Position.X, body.Position.Z),
                        new Vector2(box.Size.X * 0.5f + 0.9f, box.Size.Z * 0.5f + 0.9f));
                }
            }
        }

        if (target.DistanceSquaredTo(_navTarget) > 0.01f)
        {
            _navTarget = target;
            _navField.Rebuild(target);
        }

        Vector2 flow = _navField.Sample(_player.GlobalPosition);
        return flow == Vector2.Zero ? straight : flow;
    }

    private FlowField? _navField;
    private Vector3 _navTarget = new(float.MaxValue, 0.0f, float.MaxValue);

    private static void Release() => BotDrive.Release();
}
