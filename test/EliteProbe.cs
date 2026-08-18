using Godot;

/// Checks that a marked enemy is actually a different fight, and that the boss
/// arrives, can be killed, and pays.
///
///   godot --headless --script test/EliteProbe.cs
///
/// Exit code is the verdict. Every one of these rules is a number multiplied in
/// one place, which is the failure mode this probe exists for: a scale wired to
/// nothing produces an enemy that looks marked, is worth more experience, and
/// fights exactly like the thing next to it. That reads as balance, not as a
/// bug, and it would survive every other probe in the suite.
public partial class EliteProbe : SceneTree
{
    private Node? _scene;
    private Horde? _horde;
    private Player? _player;
    private RunDirector? _director;
    private RunGrowth? _growth;

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
            _horde = _scene?.GetNodeOrNull<Horde>("Horde");
            _player = _scene?.GetNodeOrNull<Player>("Player");
            _director = _scene?.GetNodeOrNull<RunDirector>("RunDirector");
            _growth = _scene?.GetNodeOrNull<RunGrowth>("RunGrowth");

            if (_horde == null || _player == null || _director == null || _growth == null)
            {
                GD.PushError("PROBE FAILED - scene is missing a required node");
                Quit(1);
                return true;
            }

            // The director is the thing under test in the last two stages, so it
            // is driven by hand rather than left to tick. Auto-fire off for the
            // same reason TraitProbe holds it: an unasked-for shot lands in the
            // middle of a measurement and the result is a number nobody chose.
            _director.SetPhysicsProcess(false);
            _player.GetNode<WeaponHandler>("WeaponHandler").HoldFire = true;
            _horde.Pool.Clear();
        }

        _stageTick++;

        switch (_stage)
        {
            case 0: return RunStage(StageArmourSoaks, "an armoured elite takes a fraction of the hit");
            case 1: return RunStage(StageSwiftMoves, "a swift elite outruns its own variant");
            case 2: return RunStage(StageVolatileBursts, "a volatile elite takes its neighbours with it");
            case 3: return RunStage(StageMarkIsVisible, "a mark survives the swap-remove that follows a death");
            case 4: return RunStage(StageEliteIsWorthMore, "a marked kill pays more than a plain one");
            case 5: return RunStage(StageBossArrives, "the boss arrives once, announced");
            case 6: return RunStage(StageBossReaches, "it shoots from where it cannot be touched, and keeps coming");
            case 7: return RunStage(StageBossPays, "killing it leaves a cache");
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

    /// Two brutes, same damage to each, one marked. The comparison is against a
    /// live plain enemy rather than against the table, because the table is what
    /// would still be right if nothing read it.
    private bool? StageArmourSoaks(int tick)
    {
        _horde!.Pool.Clear();

        Vector3 at = _player!.GlobalPosition;
        _horde.Spawn(at + new Vector3(20.0f, 0.0f, 0.0f), 2);
        _horde.Spawn(at + new Vector3(22.0f, 0.0f, 0.0f), 2, EliteKind.Armoured);

        float plainMax = _horde.Pool.Health[0];
        float eliteMax = _horde.Pool.Health[1];

        _horde.Damage(0, 20.0f, Vector2.Zero);
        _horde.Damage(1, 20.0f, Vector2.Zero);

        float plainLost = plainMax - _horde.Pool.Health[0];
        float eliteLost = eliteMax - _horde.Pool.Health[1];

        GD.Print($"  20 damage: plain brute lost {plainLost:F1}, armoured lost {eliteLost:F1} " +
                 $"(and starts at {eliteMax:F0} HP against {plainMax:F0})");

        return eliteLost < plainLost * 0.5f && eliteMax > plainMax * 1.5f;
    }

    /// Distance covered over a fixed window, both starting from the same place.
    /// Speed is the one elite rule that lives inside the movement loop rather
    /// than at a call site, so it is the one most likely to be silently skipped
    /// by the far-enemy stride.
    private bool? StageSwiftMoves(int tick)
    {
        if (tick == 1)
        {
            _horde!.Pool.Clear();
            Vector3 at = _player!.GlobalPosition;

            // Close in, so both are stepped every tick and the stride scheduler
            // cannot be the reason one of them moved further.
            _horde.Spawn(at + new Vector3(6.0f, 0.0f, -3.0f), 0);
            _horde.Spawn(at + new Vector3(6.0f, 0.0f, 3.0f), 0, EliteKind.Swift);

            _plainStart = _horde.Pool.Position[0];
            _swiftStart = _horde.Pool.Position[1];
            return null;
        }

        if (tick < 30)
            return null;

        float plain = _horde!.Pool.Position[0].DistanceTo(_plainStart);
        float swift = _horde.Pool.Position[1].DistanceTo(_swiftStart);

        GD.Print($"  half a second of walking: plain covered {plain:F2} m, swift {swift:F2} m");

        return swift > plain * 1.4f;
    }

    private Vector3 _plainStart;
    private Vector3 _swiftStart;

    /// A ring of bystanders around one marked walker. A plain walker's death has
    /// no blast at all, so anything the ring loses can only be the mark.
    private bool? StageVolatileBursts(int tick)
    {
        _horde!.Pool.Clear();

        // Far from the player so contact damage cannot be mistaken for the blast.
        var centre = new Vector3(30.0f, 0.0f, 30.0f);
        _horde.Spawn(centre, 0, EliteKind.Volatile);

        for (int i = 0; i < 6; i++)
        {
            float angle = i * Mathf.Tau / 6.0f;
            _horde.Spawn(centre + new Vector3(Mathf.Cos(angle), 0.0f, Mathf.Sin(angle)) * 2.5f, 2);
        }

        float before = 0.0f;
        for (int i = 1; i < _horde.Pool.Count; i++)
            before += _horde.Pool.Health[i];

        // Enough to kill it outright through the health multiplier.
        _horde.Damage(0, 500.0f, Vector2.Zero);

        float after = 0.0f;
        for (int i = 0; i < _horde.Pool.Count; i++)
            after += _horde.Pool.Health[i];

        GD.Print($"  volatile walker dies inside a ring of six brutes: " +
                 $"{before:F0} HP standing became {after:F0}");

        return after < before - 1.0f;
    }

    /// The one that catches an ordering bug rather than a missing multiplier.
    ///
    /// A death is a swap-remove: the last enemy takes the dead one's slot. If any
    /// of the mark's own consequences are read after the removal, they are read
    /// off whoever moved into the hole, so a volatile dying at the back of the
    /// array quietly detonates on behalf of a walker somewhere else, or does not
    /// detonate at all.
    private bool? StageMarkIsVisible(int tick)
    {
        _horde!.Pool.Clear();

        var centre = new Vector3(-30.0f, 0.0f, -30.0f);
        _horde.Spawn(centre, 0, EliteKind.Volatile);
        for (int i = 0; i < 4; i++)
            _horde.Spawn(centre + new Vector3(1.2f * (i + 1), 0.0f, 0.0f), 2);

        // Kill the marked one at index 0 while a plain enemy sits at the end of
        // the array, so the swap definitely happens and definitely brings an
        // unmarked enemy into index 0.
        _horde.Damage(0, 500.0f, Vector2.Zero);

        byte moved = _horde.Pool.Elite[0];
        GD.Print($"  after the marked enemy at slot 0 died, slot 0 now holds mark " +
                 $"{Elites.Name(moved)} ({_horde.Pool.Count} left)");

        return moved == (byte)EliteKind.None;
    }

    private bool? StageEliteIsWorthMore(int tick)
    {
        _horde!.Pool.Clear();

        // Read the lifetime total, not the bar. The bar is spent on level-ups, so
        // the first version of this stage measured a marked kill as being worth
        // minus eight — which is a true statement about the progress bar and says
        // nothing at all about what the kill was worth.
        float before = _growth!.ExperienceEarned;
        _horde.Spawn(new Vector3(35.0f, 0.0f, 0.0f), 0);
        _horde.Damage(0, 500.0f, Vector2.Zero);
        float plain = _growth.ExperienceEarned - before;

        before = _growth.ExperienceEarned;
        _horde.Spawn(new Vector3(35.0f, 0.0f, 0.0f), 0, EliteKind.Armoured);
        _horde.Damage(0, 500.0f, Vector2.Zero);
        float marked = _growth.ExperienceEarned - before;

        GD.Print($"  a plain walker paid {plain:F1}, a marked one {marked:F1} " +
                 $"(table says x{Elites.ExperienceScale((byte)EliteKind.Armoured):F1})");

        return plain > 0.0f
            && Mathf.IsEqualApprox(marked / plain, Elites.ExperienceScale((byte)EliteKind.Armoured));
    }

    /// Runs the director's own clock forward past the arrival point instead of
    /// calling the spawn directly. The thing being tested is the trigger, and a
    /// boss that is only ever spawned by a probe is a boss that never happens.
    private bool? StageBossArrives(int tick)
    {
        if (tick == 1)
        {
            _horde!.Pool.Clear();
            _director!.SetPhysicsProcess(true);

            // Shrink the run so the arrival point is a few seconds away rather
            // than a few minutes. The fraction is what is under test, not the
            // wall-clock time it corresponds to.
            _director.RunSeconds = 6.0f;
            _director.Connect(RunDirector.SignalName.BossArrived,
                              Callable.From(() => _announced++));
            return null;
        }

        // 6 s of run at 62% is ~3.7 s; give it a full five.
        if (tick < 300)
            return null;

        int live = 0;
        for (int i = 0; i < _horde!.Pool.Count; i++)
        {
            if (_horde.Pool.Type[i] == _director!.BossType)
                live++;
        }

        GD.Print($"  boss flag {_director!.BossSpawned}, announced {_announced}x, " +
                 $"{live} on the field at intensity {_director.Intensity:F2}");

        // Exactly one, and announced exactly once. Two would mean the guard is
        // on the wrong side of the flag; zero with the flag set would mean the
        // spawn failed silently, which looks the same from inside a run.
        return _director.BossSpawned && _announced == 1 && live == 1;
    }

    private int _announced;

    /// The stage that exists because the first boss failed it.
    ///
    /// Slow, melee and enormous was the design, and the balance sweep put one on
    /// the field for a full minute with every measured outcome unchanged to
    /// within a rounding error: the player simply walked away from it forever.
    /// So this asserts the two halves of the fix together — it fires from beyond
    /// arm's reach, and firing does not stop it closing. Either half alone gives
    /// back something that can be ignored.
    private bool? StageBossReaches(int tick)
    {
        int index = _horde!.FirstOfType(_director!.BossType);
        if (index < 0)
        {
            GD.PushError("  no boss on the field");
            return false;
        }

        if (tick == 1)
        {
            // Parked at 15 m: inside its firing range, nowhere near contact.
            _horde.EnemyShots.Clear();
            _horde.Pool.Position[index] = _player!.GlobalPosition + new Vector3(15.0f, 0.0f, 0.0f);
            _bossStart = _horde.Pool.Position[index];
            _shotsSeen = 0;
            return null;
        }

        // Enemy shots are removed on impact or expiry, so a poll that only looked
        // at the end would find an empty pool and call it a boss that never fired.
        _shotsSeen = Mathf.Max(_shotsSeen, _horde.EnemyShots.Count);

        // Two firing intervals at 1.5 s.
        if (tick < 200)
            return null;

        float closed = _bossStart.DistanceTo(_player!.GlobalPosition)
                       - _horde.Pool.Position[index].DistanceTo(_player.GlobalPosition);

        GD.Print($"  parked at 15 m: {_shotsSeen} shot(s) in the air at once, " +
                 $"closed {closed:F2} m while shooting");

        return _shotsSeen > 0 && closed > 1.0f;
    }

    private Vector3 _bossStart;
    private int _shotsSeen;

    private bool? StageBossPays(int tick)
    {
        if (tick == 1)
        {
            _director!.SetPhysicsProcess(false);

            _cratesBefore = 0;
            Node? crates = _scene?.GetNodeOrNull("LootContainers");
            if (crates != null)
                _cratesBefore = crates.GetChildCount();

            int index = _horde!.FirstOfType(_director.BossType);
            if (index < 0)
            {
                GD.PushError("  no boss on the field to kill");
                return false;
            }

            _horde.Damage(index, 5000.0f, Vector2.Zero);
            return null;
        }

        // The crate is added as a child during the kill, which is inside the
        // physics step; give the tree a frame to settle before counting.
        if (tick < 4)
            return null;

        Node? after = _scene?.GetNodeOrNull("LootContainers");
        int now = after?.GetChildCount() ?? 0;
        bool cache = after?.GetNodeOrNull("BossCache") != null;

        GD.Print($"  crates went {_cratesBefore} -> {now}, BossCache present: {cache}, " +
                 $"director still thinks it is alive: {_director!.BossAlive}");

        return now == _cratesBefore + 1 && cache && !_director.BossAlive;
    }

    private int _cratesBefore;
}
