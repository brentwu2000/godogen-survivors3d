using Godot;

/// Capture script for the proof video. Plays one compressed run — fight, loot,
/// secure, retreat, extract — entirely from script.
///
///   godot --write-movie screenshots/result/frame.png --fixed-fps 30 --quit-after 1200 \
///         --script test/Presentation.cs
///
/// --fixed-fps decouples simulated time from render speed, so the clip is the
/// same length and the same motion on any machine. 1200 frames at 30 is 40 s.
///
/// **`--quit-after` counts rendered frames; `_tick` counts physics ticks.** The
/// project runs physics at 60 Hz and the capture renders at 30, so the 1200-frame
/// clip is 2400 ticks and every cue below is written in ticks at sixty to the
/// second. Reading one as the other puts the boss on screen at the sixteen-second
/// mark, or off the end of the clip entirely, depending on which way round the
/// mistake is made.
///
/// The run is deliberately tuned tighter than the shipping numbers: a real run
/// spends its first minute nearly empty, which would be 60 seconds of an empty
/// field on camera.
public partial class Presentation : SceneTree
{
    private const float ArriveDistance = 1.2f;

    // Compressed pacing, for the camera only.
    private const int OpeningHorde = 36;

    /// Short enough that the roster actually changes on camera.
    ///
    /// The clip is 23 s long and the horde's composition is a function of how far
    /// into the run it is, so a 110 s run only ever reaches an intensity of about
    /// 0.2 — which is walkers, a few runners, and none of the three variants that
    /// were the whole point of drawing them. Forty seconds puts the brute (0.45)
    /// and the bloater (0.6) inside the window, and still leaves the run far from
    /// timing out before the extraction stage.
    private const float RunSeconds = 40.0f;
    /// A hard ceiling on the field, for the camera only.
    ///
    /// **This is what decides whether the take ends in an extraction or a
    /// death**, and it took four takes to find because every other dial looked
    /// more likely. The shipping cap is 160 and a compressed run reaches it: the
    /// bot arrived at the pad with 67 hp and 156 enemies on the field, and lost
    /// all 67 in the 4.3 seconds before the five-second hold finished. Twice.
    ///
    /// Lowering the opening crowd does not help — the director refills it within
    /// ten seconds. Lengthening the run to soften the escalation moved the death
    /// by 2.6 seconds. The number that matters is how many bodies are touching
    /// the player while it stands still, and that is this one.
    ///
    /// Ninety still fills the screen at this camera height. It is a horde in the
    /// shot and a survivable one to stand in, which is the only combination that
    /// produces a film of the loop closing.
    private const int FilmedEnemyCap = 90;

    private const float SpawnRingMin = 9.0f;
    private const float SpawnRingMax = 24.0f;

    /// Run intensity the opening crowd is drawn from.
    ///
    /// Just under the brute's unlock at 0.45, so the opening crowd is walkers,
    /// runners and spitters and the brutes *arrive* — the run's own escalation
    /// crosses 0.45 around eighteen seconds in, which is better film than having
    /// them there from the first frame anyway.
    ///
    /// It has been down four times. At 0.65 the bot died at 23.6 s, eight frames
    /// after the old cut, so the take that looked like it was about to extract was
    /// about to end in a death. At 0.5 it survived — until Phase 14 put real
    /// cover on the map and the same settings killed it at 15 s. That is not a
    /// camera problem: cover makes the horde pile up, which is exactly the
    /// balance question Phase 15 opens with.
    ///
    /// 0.4 with 44 in the opening crowd died at 31.0 s standing on the pad, with
    /// the hold 0.8 s from finishing. Terrain and landmarks had lengthened the
    /// route between the crate and the pad from about nine seconds to thirteen,
    /// which is four more seconds of being chewed on and is the whole of the
    /// difference.
    private const float OpeningIntensity = 0.4f;

    /// When the three elite marks arrive, in physics ticks at 60 Hz.
    ///
    /// Eight seconds: late enough that the opening crowd has read as a crowd
    /// first, early enough that an armoured walker has time to reach the camera
    /// and be shot at. They are spawned rather than rolled because `RollElite`
    /// is a probability — a take that happens not to produce a volatile is a take
    /// with a third of the feature missing, and nothing about the clip says so.
    private const int EliteCueTick = 8 * 60;

    /// The boss arrives when the bot reaches the pad, or at this tick if it
    /// never does.
    ///
    /// **Tied to the stage rather than to the clock, and that is the fix.** The
    /// director sends the boss on its own at 40% intensity, thirty metres out in
    /// a random direction; in a compressed forty-second run that lands around
    /// thirteen seconds, and a boss with twenty-seven seconds to cross
    /// twenty-two metres at 1.15 m/s arrives and kills the take.
    ///
    /// Moving it to a fixed late tick trades that for the opposite failure. The
    /// run ends when the extraction completes, and where that lands depends on
    /// how long the route took — which changed when the map got cover, and again
    /// when it got terrain. A boss cued at 32 s against an extraction that
    /// finishes at 32 s is a boss that spawns into a run which has already ended.
    /// It did exactly that on the first take.
    ///
    /// On the pad, the bot is standing still for the five-second hold with the
    /// camera pointed outward. The boss closes about six metres of eighteen in
    /// that time: on screen, visibly coming, and unable to arrive.
    private const int BossCueLatestTick = 30 * 60;

    /// How far in front of the camera a cued enemy appears.
    ///
    /// **In front of the camera, not in front of the player.** The rig sits
    /// thirteen metres behind the body with its own yaw, so "ahead of the player"
    /// is a direction that has nothing to do with what is on screen. `Player`,
    /// `BotDrive` and this all read `CameraRig.Forward` for exactly that reason.
    ///
    /// **Measured from the player and framed from the camera**, which are thirteen
    /// metres apart. The fog closes about twenty-four metres from the *camera*, so
    /// anything cued eighteen metres in front of the player is thirty-one metres
    /// from the lens and arrives as a black shape or as nothing. The boss was cued
    /// at eighteen and did not appear in the take at all.
    ///
    /// Nine metres for the elites puts them at twenty-two from the lens, inside
    /// the fog and lit, and they close the last of it on their own.
    ///
    /// The boss keeps sixteen, and that is not the same trade. It is six metres
    /// tall; at ten it filled the frame from below and read as two brown columns
    /// beside the player, and it reached the pad and took the player from 90 hp to
    /// 32 during the hold. At sixteen it stands at the edge of the fog as a
    /// silhouette against the sky, which is what a thing that big should look like
    /// arriving, and 1.15 m/s over sixteen metres cannot beat a five-second hold.
    private const float EliteCueRange = 9.0f;
    private const float BossCueRange = 16.0f;

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
    private int _reportedStage = -1;

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

            // The director's own boss is turned off, not moved — there is no
            // way to place it. `BossAt` is an intensity threshold and the run only
            // ever reaches 1.0, so anything above that is "never".
            _director.BossAt = 9.0f;
            _director.MaxLiveEnemies = FilmedEnemyCap;

            _bound = true;
        }

        _tick++;
        AnswerTheOffer();
        Cue();

        if (_stage != _reportedStage)
        {
            // The timeline, printed. A take that ends in a death instead of an
            // extraction is a take whose route took too long, and without this
            // there is no way to tell which leg ate the clip — the film only
            // shows the last five seconds of the answer.
            GD.Print($"  stage {_stage} at {_tick / 60.0f:F1}s, {_player.Health:F0} hp, {_horde.Pool.Count} enemies");
            _reportedStage = _stage;
        }

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

    /// Puts the things worth filming where the camera is pointing.
    ///
    /// Everything here is capture-only, exactly like the shortened run and the
    /// seeded opening crowd. Nothing in `Horde`, `RunDirector` or `Elites`
    /// changes; this only decides where and when, which is a camera decision.
    ///
    /// The alternative is to film whatever the run happens to produce, and what
    /// it produces in forty seconds is walkers. A clip of the game that does not
    /// contain the boss is not evidence that the boss works.
    private void Cue()
    {
        if (_tick == EliteCueTick)
        {
            // One of each mark, fanned across the view so they are three
            // silhouettes rather than one stack. Armoured is the big one and goes
            // in the middle.
            SpawnAhead(EliteCueRange, -0.34f, 0, EliteKind.Swift);
            SpawnAhead(EliteCueRange, 0.0f, 3, EliteKind.Armoured);
            SpawnAhead(EliteCueRange, 0.34f, 0, EliteKind.Volatile);

            GD.Print($"cue: three elite marks at {_tick / 60.0f:F0}s");
        }

        if (!_bossCued && (_stage >= 3 || _tick >= BossCueLatestTick))
        {
            _bossCued = true;
            SpawnAhead(BossCueRange, 0.0f, _director.BossType, EliteKind.None);
            GD.Print($"cue: the boss at {_tick / 60.0f:F1}s");
        }
    }

    private bool _bossCued;

    /// Spawns one enemy `range` metres along the camera's heading, turned by
    /// `offset` radians.
    private void SpawnAhead(float range, float offset, int type, EliteKind elite)
    {
        float yaw = (_rig?.Yaw ?? 0.0f) + offset;
        Vector2 forward = CameraRig.Forward(yaw);

        Vector3 at = _player.GlobalPosition
                   + new Vector3(forward.X, 0.0f, forward.Y) * range;

        // Flat, because the pool is flat — the renderer plants it. A cued enemy
        // handed the player's own Y would stand a metre and a half over the
        // ground on a crest, which on camera is the only place it would show.
        if (!_horde.Spawn(new Vector3(at.X, 0.0f, at.Z), type, elite))
            GD.PushWarning($"cue: no room in the pool for {(elite == EliteKind.None ? $"type {type}" : elite.ToString())}");
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
