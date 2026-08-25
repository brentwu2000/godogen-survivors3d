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

    /// The ambient trickle, in enemies per second at the start of a run.
    ///
    /// Halved from 2.0 when the danger zones arrived, and the halving is the
    /// point rather than a tuning nudge. This curve *was* the game's threat: it
    /// interpolated from here to `EndSpawnRate` on elapsed time alone, so the
    /// same pressure found the player wherever they stood and whatever they did.
    /// The only decision it offered was whether to keep moving.
    ///
    /// It is a background now — enough that the map is inhabited and standing
    /// still is never free, little enough that the dangerous places are the ones
    /// the player chose to walk into. See `ZonePlan`.
    [Export] public float StartSpawnRate { get; set; } = 1.0f;

    /// A maxed weapon clears roughly three a second against the late roster, so
    /// anything past about twice that is escalation the player cannot read: the
    /// field is already growing without bound at six, and every rate above it
    /// only changes how fast the number climbs. Eight keeps the curve visible —
    /// four times the opening — while leaving the last stretch somewhere skill
    /// still moves the outcome.
    [Export] public float EndSpawnRate { get; set; } = 4.0f;

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

    /// Announced rather than merely spawned. A boss the player only notices when
    /// their health starts dropping is a difficulty spike; one they are told
    /// about is a decision — leave now with what you have, or stay and take it.
    [Signal] public delegate void BossArrivedEventHandler();

    /// It went down. Separate from the horde's kill feed because the fact worth
    /// recording is "the thing the director placed is dead", and the kill feed
    /// only knows that a variant with sprite layer 5 died.
    [Signal] public delegate void BossKilledEventHandler();

    /// Fraction of the run at which it walks in.
    ///
    /// 0.62 was the design answer and the sweep threw it out: runs end between 83
    /// and 142 seconds, so a boss at 186 happened in one run out of twenty. A
    /// climax the run does not reach is not late, it is absent. 0.40 puts it
    /// inside the band where runs are still alive and still being decided.
    [Export] public float BossAt { get; set; } = 0.40f;

    [Export] public int BossType { get; set; } = 5;

    public bool BossSpawned { get; private set; }
    public bool BossAlive { get; private set; }

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
    /// Seeded from the level in `_Ready`, not left at this constant.
    ///
    /// **It was a fixed constant, so every run ever played drew the same
    /// sequence.** Supply drops landed on the same bearings in the same order in
    /// run one and run four hundred; only the player's own position moved them.
    /// A director meant to vary a run cannot do it from a stream that never
    /// varies.
    private ulong _rng = 0x853C49E6748FEA9BUL;

    /// When this run's events land, drawn once from the seed.
    ///
    /// **The schedule was fixed, and it is most of why the middle of a run is
    /// the same every time.** Pads at 45 s, supply at 75 s, boss at 120 s,
    /// supply at 174 s, and nothing whatever in the two minutes after that. A
    /// player four runs in knows the timetable, and a timetable is not a
    /// decision — there is nothing to read and nothing to be wrong about.
    ///
    /// The bands are deliberately narrow around the numbers that were tuned. The
    /// boss sits near 0.40 because the sweep showed runs ending between 83 and
    /// 142 seconds, so a boss at 186 happened once in twenty; the first supply
    /// sits near 0.25 because the bag is full at 60 s and empty at 120 s. None of
    /// that is being thrown away. The point is only that the player should not be
    /// able to set a watch by it.
    private float _bossAt;
    private float[] _supplyAt = System.Array.Empty<float>();

    /// Where the surge lands, or -1 for a run that has none.
    ///
    /// The third event, and the one that makes two runs differ in *kind* rather
    /// than in timing. A surge is one announced wave from one bearing — no new
    /// system, it is what a danger zone already does to fill its opening burst.
    /// Somewhat over half of runs have one.
    ///
    /// A run without a surge is not an easier run with something missing; it is a
    /// run in which the player who was holding a grenade back for it was wrong.
    /// That is the whole value of it being optional.
    private float _surgeAt = -1.0f;
    private bool _surgeSent;

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

        // The run's own schedule, off the level's seed.
        //
        // From the level rather than from the clock, so a replayed seed is a
        // replayed run: the balance sweep pins a seed and would otherwise be
        // measuring a different schedule on every pass, which is the difference
        // between a table and a rumour.
        //
        // Mixed rather than used raw. The generator hashes the same seed for its
        // own side streams, and two consumers stepping the same xorshift from the
        // same start would produce correlated draws — the boss time would move
        // with the terrain offset for no reason anybody could ever find.
        var level = GetParent().GetNodeOrNull<LevelGenerator>("Level");
        if (level != null && level.Seed != 0)
        {
            ulong mix = level.Seed ^ 0xC2B2AE3D27D4EB4FUL;
            mix ^= mix >> 29;
            mix *= 0x9E3779B97F4A7C15UL;
            mix ^= mix >> 32;

            // Zero is a fixed point of xorshift: the state stays zero forever and
            // every draw returns 0.0, which would put the boss at the bottom of
            // its band on every run that happened to hash there.
            _rng = mix | 1UL;
        }

        PlanTheRun();

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

        if (!BossSpawned && Intensity >= _bossAt)
            SendTheBoss();

        if (!_surgeSent && _surgeAt > 0.0f && Intensity >= _surgeAt)
            SendTheSurge();

        CheckSupplyDrops();

        SpawnTick(step);

        if (Elapsed >= RunSeconds)
            End(RunState.TimedOut, SafeBoxValue);
    }

    /// The ambient arrivals for one step.
    ///
    /// Its own method so a probe can drive it. `TickForTesting` runs the *events*
    /// — pads, boss, surge, supply — and ran nothing here, which is correct for
    /// what it was written for and meant a probe asking about the spawn loop got
    /// a field of zero enemies and no error.
    private void SpawnTick(float step)
    {
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

                // Some of this run's arrivals come as a knot rather than as one
                // more body from one more bearing. See `_knotShare`.
                if (_knotShare > 0.0f && NextFloat() < _knotShare)
                {
                    SpawnAKnot();
                    continue;
                }

                if (!_horde.SpawnByIntensity(SpawnPoint()))
                    break;   // pool full; drop the credit rather than spinning
            }
        }
    }

    /// Drives one step of the ambient arrivals, on the path the game uses.
    public void SpawnTickForTesting(float step) => SpawnTick(step);

    /// One boss, once, well outside the ring the ordinary spawns use — it has to
    /// be seen coming, and at its speed that is most of the encounter.
    private void SendTheBoss()
    {
        BossSpawned = true;

        if (_horde == null || BossType < 0 || BossType >= _horde.Types.Length)
            return;

        Vector3 around = _player?.GlobalPosition ?? Vector3.Zero;
        float angle = NextFloat() * Mathf.Tau;
        var spot = new Vector3(
            Mathf.Clamp(around.X + Mathf.Cos(angle) * 30.0f, -ArenaExtent, ArenaExtent),
            0.0f,
            Mathf.Clamp(around.Z + Mathf.Sin(angle) * 30.0f, -ArenaExtent, ArenaExtent));

        if (!_horde.Spawn(spot, BossType))
            return;

        BossAlive = true;
        _horde.EnemyKilled += OnEnemyKilled;
        GD.Print($"boss arrives at {Elapsed:F0}s");
        EmitSignal(SignalName.BossArrived);
    }

    /// Killing it is worth something the player can carry: a crate where it fell,
    /// biased hard toward the rare tail. A boss that pays in experience alone
    /// pays in a currency that evaporates when the run ends.
    private void OnEnemyKilled(int type, Vector3 position)
    {
        if (!BossAlive || type != BossType)
            return;

        BossAlive = false;
        if (_horde != null)
            _horde.EnemyKilled -= OnEnemyKilled;

        EmitSignal(SignalName.BossKilled);

        if (DropCache("BossCache", position, bias: 3.2f, rolls: 5, seconds: 1.6f))
            GD.Print($"boss down at {Elapsed:F0}s — cache dropped");
    }

    /// A crate that was not on the map when the run started.
    ///
    /// Shared by the boss reward, the supply drops and the danger zones, because
    /// they are the same object with different numbers and three copies of it
    /// would drift the first time one of them changed. Public for the zones,
    /// which are children of the level rather than of this node.
    public bool DropCache(string name, Vector3 at, float bias, int rolls, float seconds)
    {
        Node? crates = GetParent().GetNodeOrNull("LootContainers");
        if (crates == null)
            return false;

        var cache = new LootContainer
        {
            Name = name,
            Position = new Vector3(at.X, 0.0f, at.Z),
            RarityBias = bias,
            RollCount = rolls,
            SearchSeconds = seconds,

            // A payout owes ammunition, not collectibles. See
            // `LootContainer.Curiosities`: the first zone cache after the sets
            // shipped paid four set pieces and three supplies, which is a reward
            // that makes the next five minutes harder.
            Curiosities = false,

            // The other shape. A cache is packed and dropped rather than
            // scavenged — moulded shell, chute harness still attached, a beacon
            // panel on top where it clears cover. The player is meant to run
            // toward this one, so it must not be mistaken at fifty metres for a
            // crate that was always there.
            Look = LootLibrary.Look.Cache,
        };

        crates.AddChild(cache);
        return true;
    }

    /// Fractions of the run at which a supply cache lands.
    ///
    /// The second minute of a run currently costs more than it earns. That is
    /// measured, not felt: the backpack holds 528 at 60 s and 40 at 120 s,
    /// because every valuable thing in it is also the thing that keeps you alive,
    /// and a 1.56 extraction multiplier cannot buy back ninety percent of a bag.
    /// Loot is fuel, and the horde's growth outruns what the map was stocked with.
    ///
    /// The answer is supply, not arithmetic. Raising the multiplier to cover a
    /// spent bag would need it somewhere past 3x, which makes leaving late simply
    /// correct and deletes the decision it exists to create. A cache that lands
    /// while the run is going gives the second half something to be *for*, and it
    /// is the same object the boss already drops.
    /// 0.25 and 0.58 — 75 s and 174 s of a 300 s run.
    ///
    /// Placed against the measurement rather than spaced evenly. The bag is full
    /// at 60 s and empty at 120 s, so the first drop lands inside the window where
    /// the initial haul is being spent, not after it. The first values were 0.46
    /// and 0.72, which is tidy and arrives at 138 s — comfortably after the
    /// problem it was written to solve, and a run that ends at 130 s never saw one
    /// at all.
    [Export] public float[] SupplyDropsAt { get; set; } = { 0.25f, 0.58f };

    /// How far out they land. Well outside the ring the player is likely to be
    /// standing in: a cache underfoot is a reward for waiting, and the point is
    /// to make the second half a journey rather than a longer wait.
    [Export] public float SupplyDropRange { get; set; } = 26.0f;

    [Signal] public delegate void SupplyDroppedEventHandler(Vector3 at);

    private int _suppliesDropped;

    private void CheckSupplyDrops()
    {
        if (_supplyAt.Length == 0 || _suppliesDropped >= _supplyAt.Length)
            return;

        if (Intensity < _supplyAt[_suppliesDropped])
            return;

        _suppliesDropped++;

        Vector3 around = _player?.GlobalPosition ?? Vector3.Zero;
        float angle = NextFloat() * Mathf.Tau;
        var at = new Vector3(
            Mathf.Clamp(around.X + Mathf.Cos(angle) * SupplyDropRange, -ArenaExtent, ArenaExtent),
            0.0f,
            Mathf.Clamp(around.Z + Mathf.Sin(angle) * SupplyDropRange, -ArenaExtent, ArenaExtent));

        // Many common rolls, not a few rare ones — and this is the correction to
        // the version that shipped an hour ago.
        //
        // `RarityBias` multiplies an item's draw weight once per rarity step, so
        // a cache at 2.4 is a treasure chest: it rolls serums and circuit boards,
        // the two things in the table with no use at all. It raised the payout
        // curve beautifully and did nothing whatsoever for the problem, because
        // the run was not short of *value* — the bot died at 144 s holding 640
        // credits of unusable loot, dry since 69 s.
        //
        // At 1.0 the table is its own flat weights, where rifle rounds and canned
        // food are the heaviest entries. Seven of those is a supply drop. The
        // boss cache keeps its 3.2, because that one is a reward and this one is
        // a resupply, and naming a thing "supply" does not make it one.
        // 1.4, between the two versions that were tried and each measured well on
        // a different axis. At 1.0 the table is its own flat weights and a cache
        // is rounds and canned food; at 2.4 it is serums and circuit boards, and
        // the run does not need more *value*. 1.4 keeps rounds and food as the
        // heaviest entries while lifting medkits and adrenaline — the uncommons
        // that are also consumable — to something a second-minute run can rely on.
        if (!DropCache($"Supply{_suppliesDropped}", at, bias: 1.4f, rolls: 7, seconds: 2.0f))
            return;

        GD.Print($"supply drop {_suppliesDropped} at {Elapsed:F0}s, " +
                 $"{around.DistanceTo(at):F0}m out");
        EmitSignal(SignalName.SupplyDropped, at);
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

    /// Moves the clock without running the run.
    ///
    /// For probes measuring something that reads `Intensity` — the music mix, a
    /// spawn table, a payout curve. The alternative is stepping physics until the
    /// clock arrives, which for a point at 85% of a five-minute run means four
    /// minutes of simulation to check one number, and brings along every spawn
    /// and death in between as noise.
    public void SetElapsedForTesting(float seconds) => Elapsed = seconds;

    /// Forces this run's delivery shape, so a probe can hold one against the
    /// other. The draw itself is checked separately, over many seeds.
    public void SetKnotShareForTesting(float share)
    {
        _knotShare = share;
        _knotsSent = 0;

        // Credit too, or a probe comparing two delivery shapes over the same
        // window starts the second one holding whatever the first left banked —
        // which is two different experiments wearing one comparison.
        _spawnCredit = 0.0f;
    }

    /// Re-draws the schedule from a given seed, without a scene.
    ///
    /// The draw is the thing worth checking across sixty seeds rather than on
    /// one — "did this run knot" is a coin, and "do some runs knot and some not"
    /// is the design. Standing up sixty scenes to ask that would take minutes;
    /// this asks the director alone, on the same code path a real run uses.
    public void PlanForTesting(ulong seed)
    {
        _rng = seed == 0 ? 0x9E3779B97F4A7C15UL : seed;
        PlanTheRun();
    }

    /// Runs one director decision without stepping physics.
    ///
    /// For probes checking something the director does *on a schedule* — the
    /// boss, the supply drops, the pads opening. Calling the spawn directly would
    /// test the spawn and pass just as happily against a director that never
    /// looks at the clock, which is the one way a scheduled event fails while
    /// looking whole.
    public void TickForTesting()
    {
        if (!_padsRevealed && Intensity >= ExtractionOpensAt)
            RevealPads();

        if (!BossSpawned && Intensity >= _bossAt)
            SendTheBoss();

        if (!_surgeSent && _surgeAt > 0.0f && Intensity >= _surgeAt)
            SendTheSurge();

        CheckSupplyDrops();
    }

    /// Draws this run's schedule.
    ///
    /// Called once, before anything reads a time. Every draw comes off the seeded
    /// stream, so a replayed seed replays the same run and the balance sweep
    /// measures a schedule rather than noise.
    private void PlanTheRun()
    {
        _bossAt = BossAt + (NextFloat() - 0.5f) * 0.14f;

        // The first supply stays close to its tuned value because it is
        // load-bearing: it has to land while the opening haul is being spent
        // rather than after it. The second is free to wander, because a run that
        // reaches it is a long one by definition.
        var supplies = new float[SupplyDropsAt.Length];
        for (int i = 0; i < supplies.Length; i++)
            supplies[i] = SupplyDropsAt[i] + (NextFloat() - 0.5f) * (i == 0 ? 0.06f : 0.16f);

        System.Array.Sort(supplies);
        _supplyAt = supplies;

        // Away from the boss, so the two are separate events rather than one long
        // bad minute — before it if the boss is late, after it if it is early.
        _surgeAt = NextFloat() < 0.55f
            ? (NextFloat() < 0.5f ? _bossAt - 0.12f : _bossAt + 0.14f)
            : -1.0f;

        // Whether this run's crowd arrives in knots, and how much of it does.
        //
        // A third of runs, at a share between a fifth and a half. Three states
        // rather than a dial the player has to estimate: a run is scattered, or
        // it knots occasionally, or it knots often — and the difference between
        // the first and the last is what an Ordnance build is hoping for.
        //
        // Not announced. The surge is announced because it is one moment that
        // wants a decision in the seconds before it lands; this is the texture of
        // the whole run and the player reads it by looking at what is walking
        // toward them, which is the correct place to read it from.
        _knotShare = NextFloat() < 0.34f ? 0.14f + NextFloat() * 0.18f : 0.0f;

        GD.Print($"run plan: boss {_bossAt:F2}, supplies "
               + string.Join("/", System.Array.ConvertAll(_supplyAt, f => f.ToString("F2")))
               + (_surgeAt > 0.0f ? $", surge {_surgeAt:F2}" : ", no surge")
               + (_knotShare > 0.0f ? $", knots {_knotShare:P0}" : ", scattered"));
    }

    /// Several bodies at one point, arriving as a mass.
    ///
    /// **This is a delivery shape, not an event.** The surge is the event: one
    /// announced wave, once, from a bearing the player can turn away from or fire
    /// into. A knot is what a run's *ordinary* arrivals look like, some runs and
    /// not others — the same number of enemies over the same clock, shaped
    /// differently.
    ///
    /// Which is the whole point of it. The assessment that started Half H said
    /// the game rewards being fast, carrying more, dealing more area damage and
    /// waiting longer, and that replayability stays poor until different runs make
    /// one of those goals wrong. A knot run makes area damage *right* and precise
    /// single-target fire wrong; a scattered run does the reverse. Neither is
    /// harder, and a player who bought into Ordnance and drew a scattered run has
    /// had a decision go against them — which is what H1 does with the map and H3
    /// does with the deck, applied to the thing that arrives.
    ///
    /// Tight rather than merely nearby. The horde separates bodies within 15 m, so
    /// a knot loosens as it walks and a spread of 2.2 m is what still reads as one
    /// mass by the time it arrives. It is deliberately *not* the surge's 0.9-radian
    /// wedge over eight metres of range, which is a direction rather than an
    /// object.
    private void SpawnAKnot()
    {
        Vector3 around = _player?.GlobalPosition ?? Vector3.Zero;
        float angle = NextFloat() * Mathf.Tau;
        float distance = Mathf.Lerp(SpawnDistanceMin, SpawnDistanceMax, NextFloat());

        var centre = new Vector3(
            around.X + Mathf.Cos(angle) * distance, 0.0f,
            around.Z + Mathf.Sin(angle) * distance);

        int size = KnotMin + (int)(NextFloat() * (KnotMax - KnotMin + 1));

        for (int i = 0; i < size && _horde!.Pool.Count < MaxLiveEnemies; i++)
        {
            // Spent against the same credit the ordinary path spends, so a knot
            // run does not simply receive more enemies than a scattered one. The
            // first body is the credit already taken by the caller; the rest are
            // drawn forward, and the loop above will be that much shorter later.
            if (i > 0)
                _spawnCredit -= 1.0f;

            float spread = 2.2f;
            var at = new Vector3(
                Mathf.Clamp(centre.X + (NextFloat() - 0.5f) * spread, -ArenaExtent, ArenaExtent),
                0.0f,
                Mathf.Clamp(centre.Z + (NextFloat() - 0.5f) * spread, -ArenaExtent, ArenaExtent));

            if (!_horde.SpawnByIntensity(at))
                break;
        }

        _knotsSent++;
    }

    /// How many bodies a knot holds.
    ///
    /// Four is the floor because three arriving together is indistinguishable
    /// from three arriving separately by luck, and the shape has to be legible or
    /// it is not a decision anyone can read. Seven is the ceiling because a knot
    /// is a thing to deal with rather than a wave to survive — that is the surge,
    /// which sends fourteen and up.
    [Export] public int KnotMin { get; set; } = 4;
    [Export] public int KnotMax { get; set; } = 5;

    /// The share of this run's ordinary arrivals that come as a knot, drawn once
    /// in `PlanTheRun`. Zero on a run that does not do this at all.
    private float _knotShare;
    private int _knotsSent;

    /// What this run drew, for the readout and the sweep. Zero means scattered.
    public float PlannedKnotShare => _knotShare;
    public int KnotsSent => _knotsSent;

    /// One wave, from one bearing, announced.
    ///
    /// Announced for the same reason the boss is: a spike the player notices only
    /// when their health starts dropping is difficulty, and one they are told
    /// about is a decision. A few seconds is enough to move, spend something, or
    /// leave.
    private void SendTheSurge()
    {
        _surgeSent = true;

        if (_horde == null)
            return;

        Vector3 around = _player?.GlobalPosition ?? Vector3.Zero;
        float bearing = NextFloat() * Mathf.Tau;

        // Scaled with the run, so a surge late is worse than a surge early — the
        // same reading of `Intensity` everything else in this file uses.
        int count = 14 + Mathf.RoundToInt(Intensity * 16.0f);
        int sent = 0;

        for (int i = 0; i < count; i++)
        {
            // A wedge, not a ring. A ring is the ordinary spawn pattern with more
            // of it; a wedge is a *direction*, which is something the player can
            // turn away from or fire into.
            float angle = bearing + (NextFloat() - 0.5f) * 0.9f;
            float range = 24.0f + NextFloat() * 8.0f;

            var at = new Vector3(
                Mathf.Clamp(around.X + Mathf.Cos(angle) * range, -ArenaExtent, ArenaExtent),
                0.0f,
                Mathf.Clamp(around.Z + Mathf.Sin(angle) * range, -ArenaExtent, ArenaExtent));

            if (_horde.SpawnByIntensity(at))
                sent++;
        }

        GD.Print($"surge at {Elapsed:F0}s: {sent} from one side");
        EmitSignal(SignalName.SurgeArrived, sent);
    }

    /// A wave is coming, and how many.
    [Signal] public delegate void SurgeArrivedEventHandler(int count);

    /// What this run drew, for a probe and for the readout. -1 means no surge.
    public float PlannedBossAt => _bossAt;
    public float PlannedSurgeAt => _surgeAt;
    public float[] PlannedSupplyAt => _supplyAt;

    /// Ends the run from a probe. Same path the game uses, so a probe cannot
    /// leave the director in a state a real run could never reach.
    public void EndForTesting(RunState state) => End(state, 0);

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
