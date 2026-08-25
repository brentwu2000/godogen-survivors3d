using Godot;

/// Checks the shape a run's ordinary arrivals come in.
///
///   godot --headless --script test/KnotProbe.cs
///
/// Exit code is the verdict.
///
/// A knot is four or five bodies arriving inside 2.2 m instead of one more from
/// one more bearing, on about a third of runs. It is a *delivery shape* rather
/// than an event — the surge is the event — so the thing that has to stay true is
/// that it changes what arrives without changing how much of it does.
///
/// The three ways this goes wrong are each a stage. It draws a share and sends
/// nothing, which is a feature that is configured and does not exist — the shape
/// of the shockwave nobody could see and the touch layer nobody had ever
/// executed. It sends knots that are not knots, arriving spread over enough
/// ground to be indistinguishable from the ordinary spawn. And it hands out extra
/// bodies rather than drawing them forward, which turns a texture into a
/// difficulty setting nobody chose.
public partial class KnotProbe : SceneTree
{
    private Node? _scene;
    private RunDirector? _director;
    private Horde? _horde;

    private int _stage;
    private int _stageTick;
    private bool _failed;

    public override void _Initialize()
    {
        var scene = GD.Load<PackedScene>("res://scenes/Main.tscn")?.Instantiate();
        if (scene == null)
        {
            GD.PushError("Missing res://scenes/Main.tscn");
            Quit(1);
            return;
        }

        var meta = scene.GetNodeOrNull<MetaManager>("MetaManager");
        if (meta != null)
            meta.Ephemeral = true;

        var level = scene.GetNodeOrNull<LevelGenerator>("Level");
        if (level != null)
            level.Seed = 0x51E5D0A7UL;

        GameSession.LaunchedFromBase = false;
        GetRoot().AddChild(scene);
        _scene = scene;
    }

    public override bool _PhysicsProcess(double delta)
    {
        if (_stage == 0 && _stageTick == 0)
        {
            _director = _scene?.GetNodeOrNull<RunDirector>("RunDirector");
            _horde = _scene?.GetNodeOrNull<Horde>("Horde");

            if (_director == null || _horde == null)
            {
                GD.PushError("PROBE FAILED — the scene is missing something this needs");
                Quit(1);
                return true;
            }

            // Driven a tick at a time from here, or the director's own process
            // adds arrivals the stages did not ask for.
            _director.SetPhysicsProcess(false);

            // The player shoots. Every stage below counts bodies.
            var weapons = _scene?.GetNodeOrNull<Player>("Player")?
                                 .GetNodeOrNull<WeaponHandler>("WeaponHandler");
            if (weapons != null)
                weapons.HoldFire = true;
        }

        _stageTick++;

        switch (_stage)
        {
            case 0: return RunStage(StageSomeRunsKnot, "some runs knot and some do not");
            case 1: return RunStage(StageAKnotIsAMass, "a knot arrives as a mass, not as a spread");
            case 2: return RunStage(StageCreditIsSpent, "a knot run receives no more than a scattered one");
            default:
                GD.Print(_failed ? "PROBE FAILED" : "PROBE OK");
                Quit(_failed ? 1 : 0);
                return true;
        }
    }

    private bool RunStage(System.Func<int, bool?> stage, string label)
    {
        bool? verdict = stage(_stageTick);
        if (verdict == null)
            return false;

        GD.Print($"{label}: {(verdict.Value ? "ok" : "FAILED")}");
        _failed |= !verdict.Value;
        _stage++;
        _stageTick = 0;
        return false;
    }

    /// Across seeds, not on one.
    ///
    /// One seed answers "did this run knot", which is a coin and not a design.
    /// The claim is that *some* runs do and *some* do not, and that a run which
    /// does draws a share inside the band it was tuned in — a draw that always
    /// came back zero would leave every stage below this one passing on a feature
    /// that never fires, and a draw that always came back high would have removed
    /// the variation the whole thing exists for.
    private bool? StageSomeRunsKnot(int tick)
    {
        const int Seeds = 60;
        int knotted = 0;
        float lowest = 1.0f, highest = 0.0f;

        for (int i = 0; i < Seeds; i++)
        {
            // The scene's own director, re-drawn. A fresh `RunDirector()` would
            // work and would also be sixty nodes nobody frees on a failed stage.
            _director!.PlanForTesting(0x9E3779B9UL * (ulong)(i + 1));

            float share = _director.PlannedKnotShare;
            if (share > 0.0f)
            {
                knotted++;
                lowest = Mathf.Min(lowest, share);
                highest = Mathf.Max(highest, share);
            }
        }

        GD.Print($"  {knotted} of {Seeds} runs knot, shares {lowest:F2}-{highest:F2}");

        // Wide bands. The draw is a third of runs at 0.14 to 0.32 and this is not
        // a test of the constants — it is a test that the draw varies at all and
        // stays inside the range the tuning assumed.
        return knotted > Seeds / 8
               && knotted < Seeds * 7 / 8
               && lowest >= 0.10f
               && highest <= 0.40f;
    }

    /// Measured on the field, not on the constant.
    ///
    /// A knot's whole claim is that it is one object rather than several
    /// arrivals, and the number that carries that is how far apart the bodies are
    /// when they land. Asserting the spread constant would be asserting that a
    /// line of code says what it says; asking the horde where the bodies actually
    /// are is the version that fails when the spawn point is clamped, or the
    /// spread is applied to the wrong axis, or the knot quietly becomes a loop
    /// around `SpawnPoint`.
    private bool? StageAKnotIsAMass(int tick)
    {
        _horde!.Pool.Clear();
        _director!.SetElapsedForTesting(_director.RunSeconds * 0.5f);
        _director.SetKnotShareForTesting(1.0f);

        // One tick, which at a share of 1.0 is one knot and nothing else — as
        // long as there is credit for it. Ticked until the pool moves rather
        // than assuming a single tick carries a whole enemy of credit.
        for (int i = 0; i < 300 && _horde.Pool.Count == 0; i++)
            _director.SpawnTickForTesting(1.0f / 60.0f);

        int landed = _horde.Pool.Count;
        if (landed < 2)
        {
            GD.PushError($"  a forced knot put {landed} bodies on the field");
            return false;
        }

        Vector3 centre = Vector3.Zero;
        for (int i = 0; i < landed; i++)
            centre += _horde.Pool.Position[i];
        centre /= landed;

        float furthest = 0.0f;
        for (int i = 0; i < landed; i++)
            furthest = Mathf.Max(furthest, centre.DistanceTo(_horde.Pool.Position[i]));

        GD.Print($"  {landed} bodies, furthest {furthest:F2} m from the middle of them "
               + $"(the ring they would otherwise come from is {_director.SpawnDistanceMin:F0} m out)");

        // Generous against the 2.2 m the spread is authored at, and nowhere near
        // the 26-34 m ring an ordinary arrival is drawn from. What this refuses
        // is a "knot" whose bodies came from separate `SpawnPoint` calls.
        return furthest <= 4.0f;
    }

    /// The one that matters.
    ///
    /// A knot draws its bodies forward out of the same spawn credit the scattered
    /// path spends. Get that wrong — add the bodies instead of borrowing them —
    /// and a knot run is simply a run with more enemies in it, which is a
    /// difficulty setting nobody chose wearing the word texture. It would also be
    /// invisible: the field would be denser, which is what a knot is supposed to
    /// look like.
    private bool? StageCreditIsSpent(int tick)
    {
        int scattered = ArrivalsOver(0.0f);
        int knotted = ArrivalsOver(1.0f);

        float ratio = scattered > 0 ? knotted / (float)scattered : 0.0f;
        GD.Print($"  over the same clock: {scattered} scattered, {knotted} in knots "
               + $"(x{ratio:F2})");

        // A knot lands its whole size the moment it fires, so the two cannot be
        // equal to the body — the last knot of the window overshoots by up to
        // four. A quarter of slack over a window this long is that overshoot and
        // nothing else; twice as many would be the bug.
        return ratio > 0.75f && ratio < 1.25f;
    }

    /// Bodies delivered over a fixed slice of the run at a fixed share.
    private int ArrivalsOver(float share)
    {
        _horde!.Pool.Clear();
        _director!.SetKnotShareForTesting(share);

        // Well below the cap for the whole window, or the ceiling truncates the
        // faster-arriving side and the comparison measures the cap.
        int before = _horde.Pool.Count;

        for (int i = 0; i < 900; i++)
        {
            _director.SetElapsedForTesting(_director.RunSeconds * 0.35f);
            _director.SpawnTickForTesting(1.0f / 60.0f);

            if (_horde.Pool.Count >= _director.MaxLiveEnemies - 8)
                break;
        }

        return _horde.Pool.Count - before;
    }
}
