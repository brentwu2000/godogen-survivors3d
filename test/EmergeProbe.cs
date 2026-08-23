using Godot;

/// Checks that arrivals rise rather than appear.
///
///   godot --headless --script test/EmergeProbe.cs
///
/// Asks the pool, never a screenshot. Godot runs up to eight physics ticks on the
/// first frame after a scene loads, and the ramp is a third of a second — twenty
/// ticks — so a picture taken on frame one may or may not catch it, and one taken
/// on frame two never will. A test that looked at pixels would pass or fail on
/// how long the level generator happened to take.
///
/// The subject is spawned far from the player, because the player shoots.
public partial class EmergeProbe : SceneTree
{
    private Horde? _horde;
    private Player? _player;

    private int _stage;
    private int _stageTick;
    private bool _failed;

    private float _atSpawn;
    private float _partWay;
    private float _healthWhileRising;

    public override void _Initialize()
    {
        var scene = GD.Load<PackedScene>("res://scenes/Main.tscn")?.Instantiate();
        if (scene == null)
        {
            GD.PushError("Missing res://scenes/Main.tscn");
            Quit(1);
            return;
        }

        var level = scene.GetNodeOrNull<LevelGenerator>("Level");
        if (level != null)
            level.Seed = 0x51E5D0A7UL;

        GetRoot().AddChild(scene);
    }

    public override bool _PhysicsProcess(double delta)
    {
        if (_stage == 0 && _stageTick == 0)
        {
            Node scene = GetRoot().GetChild(GetRoot().GetChildCount() - 1);
            _horde = scene.GetNodeOrNull<Horde>("Horde");
            _player = scene.GetNodeOrNull<Player>("Player");

            if (_horde == null || _player == null)
            {
                GD.PushError($"PROBE FAILED — horde={_horde != null} player={_player != null}");
                Quit(1);
                return true;
            }

            scene.GetNodeOrNull<RunDirector>("RunDirector")?.SetPhysicsProcess(false);
            _player.GetNodeOrNull<WeaponHandler>("WeaponHandler")?.SetPhysicsProcess(false);
        }

        _stageTick++;

        switch (_stage)
        {
            case 0: return RunStage(StageRises, "a new arrival rises over a third of a second");
            case 1: return RunStage(StageNeverZero, "a rising body is never a zero-scale transform");
            case 2: return RunStage(StageCosmeticOnly, "a half-risen enemy can still be hit and still moves");
            case 3: return RunStage(StageFarOnesRiseToo, "an arrival past the active radius rises smoothly");
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

    /// Twenty metres out, which is inside the arena and well beyond anything the
    /// player will reach for.
    private void SpawnSubject(float metres = 20.0f)
    {
        _horde!.Pool.Clear();
        _horde.Spawn(_player!.GlobalPosition + new Vector3(metres, 0.0f, 0.0f), 0);
    }

    private bool? StageRises(int tick)
    {
        if (tick == 1)
        {
            SpawnSubject();
            _atSpawn = _horde!.Pool.Emerge[0];
            return null;
        }

        // A third of the ramp.
        if (tick == 8)
        {
            _partWay = _horde!.Pool.Emerge[0];
            return null;
        }

        // Comfortably past it.
        if (tick < 30)
            return null;

        float finished = _horde!.Pool.Emerge[0];

        GD.Print($"  emerge {_atSpawn:F2} at spawn, {_partWay:F2} after 7 ticks, " +
                 $"{finished:F2} after 29 ({_horde.EmergeSeconds:F2}s is {_horde.EmergeSeconds * 60.0f:F0} ticks)");

        bool startsDown = _atSpawn < 0.05f;
        bool climbs = _partWay > _atSpawn && _partWay < 1.0f;
        bool arrives = Mathf.IsEqualApprox(finished, 1.0f);

        if (!startsDown)
            GD.PushError($"  a new arrival starts at {_atSpawn:F2} — it did not rise, it appeared");
        if (!climbs)
            GD.PushError($"  {_partWay:F2} part way through — the ramp is not advancing, or it is instant");
        if (!arrives)
            GD.PushError($"  {finished:F2} after half a second — the ramp never finishes");

        return startsDown && climbs && arrives;
    }

    /// Zero scale is not a small body, it is an invalid transform.
    private bool? StageNeverZero(int tick)
    {
        float atZero = Horde.EmergeScale(0.0f);
        float atHalf = Horde.EmergeScale(0.5f);
        float atOne = Horde.EmergeScale(1.0f);

        GD.Print($"  scale at 0.00 = {atZero:F3}, at 0.50 = {atHalf:F3}, at 1.00 = {atOne:F3}");

        bool floored = atZero > 0.0f;
        bool monotonic = atHalf > atZero && atOne > atHalf;
        bool full = Mathf.IsEqualApprox(atOne, 1.0f);

        if (!floored)
            GD.PushError("  scale is zero at the start — that is a basis with no determinant");
        if (!monotonic)
            GD.PushError($"  {atZero:F3} -> {atHalf:F3} -> {atOne:F3} is not a rise");
        if (!full)
            GD.PushError($"  a finished body draws at {atOne:F3}, not 1.0");

        // Eased rather than linear: at the halfway point it should already be
        // most of the way up, so the arrival settles instead of growing.
        bool eased = atHalf > 0.6f;
        if (!eased)
            GD.PushError($"  {atHalf:F3} at the halfway point — the ramp is linear, which reads as a " +
                         "scaling animation rather than as something arriving");

        return floored && monotonic && full && eased;
    }

    /// The ramp is cosmetic. Everything else treats it as fully there.
    private bool? StageCosmeticOnly(int tick)
    {
        if (tick == 1)
        {
            // Close enough to be inside the active radius so it walks.
            SpawnSubject(10.0f);
            return null;
        }

        if (tick == 3)
        {
            float rising = _horde!.Pool.Emerge[0];
            if (rising >= 1.0f)
            {
                GD.PushError("  the subject finished rising before it could be tested");
                return false;
            }

            _startedAt = _horde.Pool.Position[0];
            _healthWhileRising = _horde.Pool.Health[0];
            _horde.Damage(0, 5.0f, Vector2.Zero);
            _roseWhenHit = rising;
            return null;
        }

        if (tick < 12)
            return null;

        float took = _healthWhileRising - _horde!.Pool.Health[0];
        float walked = new Vector2(_horde.Pool.Position[0].X - _startedAt.X,
                                   _horde.Pool.Position[0].Z - _startedAt.Z).Length();

        GD.Print($"  hit at {_roseWhenHit:F2} risen: took {took:F1} damage and walked {walked:F2} m");

        bool hurt = took > 0.0f;
        bool moved = walked > 0.0f;

        if (!hurt)
            GD.PushError("  a half-risen enemy could not be damaged — the ramp is not cosmetic");
        if (!moved)
            GD.PushError("  a half-risen enemy did not move — the ramp is not cosmetic");

        return hurt && moved;
    }

    private Vector3 _startedAt;
    private float _roseWhenHit;

    /// The ramp has to advance for enemies the movement loop skips.
    ///
    /// The horde strides distant enemies — updated every few ticks and moved
    /// further to compensate. A ramp advanced inside that loop would rise in
    /// visible steps for exactly the enemies far enough away to be watched
    /// arriving, which is the whole audience for this feature.
    private bool? StageFarOnesRiseToo(int tick)
    {
        if (tick == 1)
        {
            // Past the active radius, where the stride kicks in.
            SpawnSubject(_horde!.ActiveRadius + 15.0f);
            _samples.Clear();
            return null;
        }

        if (tick <= 20)
        {
            _samples.Add(_horde!.Pool.Emerge[0]);
            return null;
        }

        // Every step the same size, within floating-point slack. A strided ramp
        // would show runs of identical values with jumps between them.
        float smallest = float.MaxValue, largest = 0.0f;
        for (int i = 1; i < _samples.Count; i++)
        {
            float delta = _samples[i] - _samples[i - 1];
            if (_samples[i] >= 1.0f)
                break;

            smallest = Mathf.Min(smallest, delta);
            largest = Mathf.Max(largest, delta);
        }

        GD.Print($"  {_samples.Count} samples past the active radius: steps between " +
                 $"{smallest:F4} and {largest:F4}");

        bool steady = smallest > 0.0f && largest - smallest < 0.002f;
        if (!steady)
        {
            GD.PushError($"  steps range {smallest:F4} to {largest:F4} — a distant arrival is rising " +
                         "in jumps, which is the stride leaking into the ramp");
        }

        return steady;
    }

    private readonly System.Collections.Generic.List<float> _samples = new();
}
