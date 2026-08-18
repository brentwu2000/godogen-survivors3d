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

    /// A maxed weapon clears roughly three a second against the late roster, so
    /// anything past about twice that is escalation the player cannot read: the
    /// field is already growing without bound at six, and every rate above it
    /// only changes how fast the number climbs. Eight keeps the curve visible —
    /// four times the opening — while leaving the last stretch somewhere skill
    /// still moves the outcome.
    [Export] public float EndSpawnRate { get; set; } = 8.0f;

    [Export] public float EndSpeedScale { get; set; } = 1.6f;

    /// Most enemies alive at once. Spawning stops here and resumes as they die.
    ///
    /// Without it the field grows without bound, and a twenty-run sweep found the
    /// wall between one and two minutes: every layout survived a sixty-second
    /// linger at near-full health with a peak around a hundred, and nothing at all
    /// survived a hundred and eighty, with peaks of three and four hundred. A
    /// three-hundred-second deadline nobody has ever seen the second half of is
    /// the same as no deadline.
    ///
    /// A ceiling rather than a slower rate, for the reason Phase 8 cut the end
    /// rate from twelve to eight: what the player reads is density, and density
    /// saturates. Past the point where the screen is full, more of them only
    /// changes how fast a number they cannot count climbs — while the escalation
    /// they *can* read, the roster turning into brutes and bloaters and everything
    /// moving 1.6x faster, carries on unaffected.
    ///
    /// 160 rather than a number picked to make the sweep pass: the mobile budget
    /// has been 150-200 concurrent enemies since before any code existed, and
    /// nothing had ever enforced it — the field simply grew until somebody died.
    /// The design number and the performance number are now the same number.
    [Export] public int MaxLiveEnemies { get; set; } = 160;

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
    private ExtractionZone[] _extractions = System.Array.Empty<ExtractionZone>();
    private bool _padsRevealed;
    private float _spawnCredit;
    private ulong _rng = 0x853C49E6748FEA9BUL;

    /// The pads this run will offer, whether or not they are open yet. The HUD
    /// needs them to point somewhere once they are revealed.
    public System.Collections.Generic.IReadOnlyList<ExtractionZone> Pads => _extractions;

    /// The pad this run is going to offer, known before it is revealed. Probes
    /// and capture scripts need somewhere definite to walk to; the player finds
    /// out when the director says so.
    public ExtractionZone? PrimaryPad
    {
        get
        {
            foreach (ExtractionZone pad in _extractions)
            {
                if (pad.WillOpen)
                    return pad;
            }

            return _extractions.Length > 0 ? _extractions[0] : null;
        }
    }

    public override void _Ready()
    {
        _horde = GetParent().GetNodeOrNull<Horde>("Horde");
        _player = GetParent().GetNodeOrNull<Player>("Player");
        _extractions = FindPads();

        if (_player != null)
            _player.Died += OnPlayerDied;

        foreach (ExtractionZone pad in _extractions)
            pad.Extracted += OnExtracted;

        if (ExtractionOpensAt <= 0.0f)
            RevealPads();
    }

    /// Every pad under the container, or the single legacy node if a scene still
    /// has one. Scanning rather than naming, because how many exits a run has is
    /// the level's decision and not the director's.
    private ExtractionZone[] FindPads()
    {
        var found = new System.Collections.Generic.List<ExtractionZone>();

        Node? container = GetParent().GetNodeOrNull("ExtractionZones");
        if (container != null)
        {
            foreach (Node child in container.GetChildren())
            {
                if (child is ExtractionZone zone)
                    found.Add(zone);
            }
        }

        var single = GetParent().GetNodeOrNull<ExtractionZone>("ExtractionZone");
        if (single != null)
            found.Add(single);

        if (found.Count == 0)
            GD.PushWarning("RunDirector: no extraction pads — the run can only end on the clock");

        return found.ToArray();
    }

    /// Opens the pads the level chose and makes them visible. Until this moment
    /// the player does not know where the run ends, which is what turns the map
    /// into a decision rather than a corridor.
    private void RevealPads()
    {
        if (_padsRevealed)
            return;

        _padsRevealed = true;
        int opened = 0;

        foreach (ExtractionZone pad in _extractions)
        {
            if (!pad.WillOpen)
                continue;

            pad.Open = true;
            pad.Visible = true;
            opened++;
        }

        // A run with no way out is a bug, not a difficulty setting. Falling back
        // to the nearest pad beats ending every run on the clock.
        if (opened == 0 && _extractions.Length > 0)
        {
            _extractions[0].Open = true;
            _extractions[0].Visible = true;
            GD.PushWarning("RunDirector: no pad was flagged to open; forcing the first");
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (State != RunState.Running)
            return;

        float step = (float)delta;
        Elapsed += step;

        if (!_padsRevealed && Intensity >= ExtractionOpensAt)
            RevealPads();

        if (_horde != null)
        {
            _horde.SpeedScale = Mathf.Lerp(1.0f, EndSpeedScale, Intensity);

            // The horde picks its own composition; it only needs to be told how
            // far into the run it is. Escalation is then one curve driving both
            // how many arrive and which ones.
            _horde.SpawnIntensity = Intensity;

            // Fractional credit, so a rate below one per second still spawns
            // instead of rounding to nothing every tick.
            _spawnCredit += Mathf.Lerp(StartSpawnRate, EndSpawnRate, Intensity) * step;

            // Credit is discarded at the ceiling rather than banked. Banked, the
            // moment the player clears a gap the whole backlog arrives at once —
            // which is the pile-up the ceiling exists to prevent, delivered as a
            // single wave instead of gradually.
            if (_horde.Pool.Count >= MaxLiveEnemies)
                _spawnCredit = 0.0f;

            while (_spawnCredit >= 1.0f && _horde.Pool.Count < MaxLiveEnemies)
            {
                _spawnCredit -= 1.0f;
                if (!_horde.SpawnByIntensity(SpawnPoint()))
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
