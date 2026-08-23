using Godot;

/// Walks the extraction loop end to end: search a crate, take contact damage,
/// watch the horde enrage, then extract and bank the backpack.
///
///   godot --headless --script test/RunLoopProbe.cs
///
/// The player is teleported rather than driven, so each stage starts from an
/// exact position instead of from wherever pathing happened to leave them.
public partial class RunLoopProbe : SceneTree
{
    private Player _player = null!;
    private Horde _horde = null!;
    private RunDirector _director = null!;
    private ExtractionZone _extraction = null!;
    private LootContainer _crate = null!;

    private int _stage;
    private int _tick;
    private bool _failed;

    private int _endedState = -1;
    private int _endedBanked;
    private float _healthBefore;
    private float _lootProgressPeak;

    public override void _Initialize()
    {
        var scene = GD.Load<PackedScene>("res://scenes/Main.tscn")?.Instantiate();
        if (scene == null)
        {
            GD.PushError("Missing res://scenes/Main.tscn");
            Quit(1);
            return;
        }

        // A fixed layout, set before the scene enters the tree because the
        // generator runs in _Ready. Without it every run of this script would
        // face a different map, and a number that changes for reasons the test
        // did not choose is not a measurement.
        var level = scene.GetNodeOrNull<LevelGenerator>("Level");
        if (level != null)
            level.Seed = 0x51E5D0A7UL;

        // Not the developer's save file. See `Fresh`.
        Fresh.Profile(scene);

        GetRoot().AddChild(scene);
    }

    public override bool _PhysicsProcess(double delta)
    {
        if (_stage == 0 && _tick == 0 && !Bind())
        {
            Quit(1);
            return true;
        }

        _tick++;

        switch (_stage)
        {
            case 0: return Advance(StageExtractionGate, "extraction starts closed");
            case 1: return Advance(StageLoot, "crate search fills the backpack");
            case 2: return Advance(StageLootReset, "leaving resets search progress");
            case 3: return Advance(StageContactDamage, "contact damage");
            case 4: return Advance(StageEscalation, "horde enrages over the run");
            case 5: return Advance(StageExtract, "extraction banks the backpack");
            default:
                GD.Print(_failed ? "PROBE FAILED" : "PROBE OK");
                Quit(_failed ? 1 : 0);
                return true;
        }
    }

    private bool Bind()
    {
        Node scene = GetRoot().GetChild(GetRoot().GetChildCount() - 1);
        Player? player = scene.GetNodeOrNull<Player>("Player");
        Horde? horde = scene.GetNodeOrNull<Horde>("Horde");
        RunDirector? director = scene.GetNodeOrNull<RunDirector>("RunDirector");
        LootContainer? crate = scene.GetNodeOrNull<LootContainer>("LootContainers/Crate0");
        ExtractionZone? extraction = director?.PrimaryPad;

        if (player == null || horde == null || director == null || extraction == null || crate == null)
        {
            GD.PushError($"PROBE FAILED — player={player != null} horde={horde != null} " +
                         $"director={director != null} extraction={extraction != null} crate={crate != null}");
            return false;
        }

        _player = player;
        _horde = horde;
        _director = director;
        _extraction = director.PrimaryPad!;
        _crate = crate;

        // The rifle would clear the arena and skew the damage stage; the loop is
        // what is under test here, not combat.
        _player.GetNodeOrNull<WeaponHandler>("WeaponHandler")?.SetPhysicsProcess(false);
        _director.RunEnded += (state, banked) => { _endedState = state; _endedBanked = banked; };

        return true;
    }

    private bool Advance(System.Func<int, bool?> stage, string label)
    {
        bool? verdict = stage(_tick);
        if (verdict == null)
            return false;

        GD.Print($"{label}: {(verdict.Value ? "ok" : "FAILED")}");
        _failed |= !verdict.Value;
        _stage++;
        _tick = 0;
        return false;
    }

    /// The default opens extraction at 15% of the run, so at t≈0 it must be shut.
    private bool? StageExtractionGate(int tick)
    {
        bool closed = !_extraction.Open;
        GD.Print($"  at {_director.Elapsed:F2}s extraction open = {_extraction.Open}");

        // Open it for the rest of the probe rather than waiting out 15% of a
        // ten-minute run.
        _director.ExtractionOpensAt = 0.0f;
        return closed;
    }

    private bool? StageLoot(int tick)
    {
        if (tick == 1)
        {
            _player.GlobalPosition = _crate.GlobalPosition;
            _horde.Pool.Clear();
            return null;
        }

        // SearchSeconds defaults to 2.5s; 240 ticks is four seconds of slack.
        if (tick < 240 && !_crate.Looted)
            return null;

        Inventory bag = _player.Backpack;
        GD.Print($"  crate looted={_crate.Looted}, bag {bag.UsedBulk}/{bag.Capacity} bulk, value {bag.TotalValue}");
        return _crate.Looted && bag.TotalValue > 0 && bag.UsedBulk > 0;
    }

    private bool? StageLootReset(int tick)
    {
        Node scene = GetRoot().GetChild(GetRoot().GetChildCount() - 1);
        var other = scene.GetNodeOrNull<LootContainer>("LootContainers/Crate1");
        if (other == null)
            return false;

        if (tick == 1)
        {
            _player.GlobalPosition = other.GlobalPosition;
            _lootProgressPeak = 0.0f;
            return null;
        }

        // Half the search, then step away.
        if (tick < 60)
        {
            _lootProgressPeak = Mathf.Max(_lootProgressPeak, other.Progress);
            return null;
        }

        if (tick == 60)
        {
            _player.GlobalPosition = new Vector3(40.0f, 0.0f, 40.0f);
            return null;
        }

        if (tick < 70)
            return null;

        GD.Print($"  progress reached {_lootProgressPeak:F2}, after leaving {other.Progress:F2}, looted={other.Looted}");
        return _lootProgressPeak > 0.1f && other.Progress == 0.0f && !other.Looted;
    }

    private bool? StageContactDamage(int tick)
    {
        if (tick == 1)
        {
            _player.GlobalPosition = new Vector3(30.0f, 0.0f, 30.0f);
            _horde.Pool.Clear();
            for (int i = 0; i < 3; i++)
                _horde.Spawn(_player.GlobalPosition + new Vector3(0.3f * i, 0.0f, 0.2f));

            _healthBefore = _player.Health;
            return null;
        }

        if (tick < 60)
            return null;

        float lost = _healthBefore - _player.Health;
        GD.Print($"  3 enemies in contact for ~1s -> {lost:F1} damage taken");

        _player.Heal(_player.MaxHealth);
        _horde.Pool.Clear();
        return lost > 1.0f;
    }

    private bool? StageEscalation(int tick)
    {
        if (tick == 1)
        {
            // Compress the run so the curve is observable without waiting out
            // ten minutes of wall clock.
            _director.RunSeconds = 30.0f;
            _player.GlobalPosition = new Vector3(45.0f, 0.0f, 45.0f);
            return null;
        }

        if (tick < 300)
            return null;

        float intensity = _director.Intensity;
        float speed = _horde.SpeedScale;
        GD.Print($"  after {_director.Elapsed:F1}s intensity {intensity:F2}, horde speed scale {speed:F2}, " +
                 $"enemies {_horde.Pool.Count}");

        return intensity > 0.0f && speed > 1.0f && _horde.Pool.Count > 0;
    }

    private bool? StageExtract(int tick)
    {
        if (tick == 1)
        {
            // Plenty of clock left for a five second hold.
            _director.RunSeconds = 600.0f;
            _player.GlobalPosition = _extraction.GlobalPosition;
            return null;
        }

        // Interrupt once the hold is underway, and confirm it resets.
        if (tick == 120)
        {
            if (_extraction.Progress <= 0.0f)
            {
                GD.Print("  extraction never started");
                return false;
            }
            _player.GlobalPosition = new Vector3(45.0f, 0.0f, 45.0f);
            return null;
        }

        if (tick == 130)
        {
            if (_extraction.Progress != 0.0f)
            {
                GD.Print($"  leaving did not reset extraction ({_extraction.Progress:F2})");
                return false;
            }
            _player.GlobalPosition = _extraction.GlobalPosition;
            return null;
        }

        if (tick < 600 && _endedState < 0)
            return null;

        int carried = _player.Backpack.TotalValue + _player.SafeBox.TotalValue;
        int expected = Mathf.RoundToInt(carried * _director.ExtractionMultiplier);

        GD.Print($"  run ended state={(RunState)_endedState}, banked {_endedBanked}, " +
                 $"carried {carried} x{_director.ExtractionMultiplier:F2} = {expected}");
        return _endedState == (int)RunState.Extracted && _endedBanked == expected && carried > 0;
    }
}
