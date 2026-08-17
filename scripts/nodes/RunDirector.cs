using Godot;

public enum RunState
{
    Running,
    Extracted,
    Died,
    TimedOut,
}

/// Owns the run: the clock, the escalating horde, and how the run ends.
///
/// Escalation is a curve over the run clock rather than a kill count, so the
/// pressure to leave is the same whether the player fights or hides — which is
/// what makes the extraction decision a real one.
public partial class RunDirector : Node3D
{
    /// A kiting player dies somewhere past the four minute mark, so a longer
    /// deadline than this is fiction — the clock has to be a real constraint
    /// rather than a number that never arrives.
    [Export] public float RunSeconds { get; set; } = 300.0f;

    [Export] public float StartSpawnRate { get; set; } = 2.0f;
    [Export] public float EndSpawnRate { get; set; } = 12.0f;

    [Export] public float EndSpeedScale { get; set; } = 1.6f;

    /// Payout multiplier at the deadline. Loot alone gives no reason to stay past
    /// the first minute — every crate is empty by then — so the reward for
    /// staying has to come from the clock instead.
    [Export] public float MaxExtractionMultiplier { get; set; } = 3.0f;

    /// Fraction of the run before extraction opens. Leaving instantly would make
    /// looting optional.
    [Export] public float ExtractionOpensAt { get; set; } = 0.15f;

    /// Enemies appear this far from the player — beyond the visible area, so
    /// they walk into frame rather than popping into it.
    [Export] public float SpawnDistanceMin { get; set; } = 26.0f;
    [Export] public float SpawnDistanceMax { get; set; } = 34.0f;

    [Export] public float ArenaExtent { get; set; } = 55.0f;

    [Signal] public delegate void RunEndedEventHandler(int state, int bankedValue);

    public RunState State { get; private set; } = RunState.Running;
    public float Elapsed { get; private set; }
    public float Remaining => Mathf.Max(0.0f, RunSeconds - Elapsed);
    public int BankedValue { get; private set; }

    /// 0 at the start of the run, 1 at the deadline.
    public float Intensity => Mathf.Clamp(Elapsed / Mathf.Max(1.0f, RunSeconds), 0.0f, 1.0f);

    /// What the backpack is worth if extracted right now.
    public float ExtractionMultiplier => Mathf.Lerp(1.0f, MaxExtractionMultiplier, Intensity);

    private Horde? _horde;
    private Player? _player;
    private ExtractionZone? _extraction;
    private float _spawnCredit;
    private ulong _rng = 0x853C49E6748FEA9BUL;

    public override void _Ready()
    {
        _horde = GetParent().GetNodeOrNull<Horde>("Horde");
        _player = GetParent().GetNodeOrNull<Player>("Player");
        _extraction = GetParent().GetNodeOrNull<ExtractionZone>("ExtractionZone");

        if (_player != null)
            _player.Died += OnPlayerDied;

        if (_extraction != null)
        {
            _extraction.Open = ExtractionOpensAt <= 0.0f;
            _extraction.Extracted += OnExtracted;
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (State != RunState.Running)
            return;

        float step = (float)delta;
        Elapsed += step;

        if (_extraction != null && !_extraction.Open && Intensity >= ExtractionOpensAt)
            _extraction.Open = true;

        if (_horde != null)
        {
            _horde.SpeedScale = Mathf.Lerp(1.0f, EndSpeedScale, Intensity);

            // Fractional credit, so a rate below one per second still spawns
            // instead of rounding to nothing every tick.
            _spawnCredit += Mathf.Lerp(StartSpawnRate, EndSpawnRate, Intensity) * step;
            while (_spawnCredit >= 1.0f)
            {
                _spawnCredit -= 1.0f;
                if (!_horde.Spawn(SpawnPoint()))
                    break;   // pool full; drop the credit rather than spinning
            }
        }

        if (Elapsed >= RunSeconds)
            End(RunState.TimedOut, SafeBoxValue);
    }

    private Vector3 SpawnPoint()
    {
        Vector3 around = _player?.GlobalPosition ?? Vector3.Zero;
        float angle = NextFloat() * Mathf.Tau;
        float distance = Mathf.Lerp(SpawnDistanceMin, SpawnDistanceMax, NextFloat());

        return new Vector3(
            Mathf.Clamp(around.X + Mathf.Cos(angle) * distance, -ArenaExtent, ArenaExtent),
            0.0f,
            Mathf.Clamp(around.Z + Mathf.Sin(angle) * distance, -ArenaExtent, ArenaExtent));
    }

    private int SafeBoxValue => _player?.SafeBox.TotalValue ?? 0;

    /// Dying banks only what was secured. That asymmetry is the whole point of
    /// the loop — the backpack is worth something only once it is carried out,
    /// and the safe box is the hedge the player paid seconds for.
    private void OnPlayerDied() => End(RunState.Died, SafeBoxValue);

    /// The multiplier applies to everything carried out, safe box included —
    /// walking out late is what earned it. Dying pays the safe box at face
    /// value, so securing loot is a hedge and never a way to farm the bonus.
    private void OnExtracted()
    {
        int carried = (_player?.Backpack.TotalValue ?? 0) + SafeBoxValue;
        End(RunState.Extracted, Mathf.RoundToInt(carried * ExtractionMultiplier));
    }

    private void End(RunState state, int banked)
    {
        if (State != RunState.Running)
            return;

        State = state;
        BankedValue = banked;
        GD.Print($"run ended: {state} after {Elapsed:F1}s, banked {banked}");
        EmitSignal(SignalName.RunEnded, (int)state, banked);
    }

    private float NextFloat()
    {
        _rng ^= _rng << 13;
        _rng ^= _rng >> 7;
        _rng ^= _rng << 17;
        return (_rng >> 40) / 16777216.0f;
    }
}
