using Godot;

/// A place on the map that fights back, and pays.
///
/// Dormant until the player walks in, then it runs its own encounter and spends
/// itself. Enemies come from the zone's own perimeter rather than from a ring
/// around the player, which is the difference that makes it a place: back out
/// through the edge you came in by and the pressure is behind you, not around
/// you.
///
/// Three of these replace an escalating spawn rate. The rate made threat a
/// property of the clock — the same pressure wherever the player stood and
/// whatever they did, with no decision in it beyond whether to keep moving. A
/// zone is a decision: it is worth a cache and a magazine, it costs a hard
/// minute, and walking past it is allowed.
public partial class DangerZone : Node3D
{
    [Export] public Vector2 HalfExtent { get; set; } = new(13.0f, 10.0f);
    [Export] public int Kind { get; set; }
    [Export] public int Tier { get; set; }
    [Export] public float HoldSeconds { get; set; } = 45.0f;
    [Export] public int PurgeKills { get; set; } = 18;
    [Export] public int Rolls { get; set; } = 3;
    [Export] public int Rounds { get; set; } = 60;
    [Export] public float SpawnRate { get; set; } = 2.4f;
    [Export] public int OpeningBurst { get; set; } = 5;
    [Export] public string Title { get; set; } = "Zone";

    /// How far outside the boundary the zone keeps producing, in metres.
    ///
    /// It has to be well past the edge, or a zone could be emptied a metre at a
    /// time by standing outside the line and shooting in — which is not a
    /// decision, it is a chore.
    ///
    /// And it has to be finite. Without this a zone woken in passing spawns
    /// forever: the bot crossed one on its way to extraction, walked out the far
    /// side, and arrived at the pad with 111 enemies behind it and a zone still
    /// filling the map from forty metres away. An abandoned encounter should
    /// stop, not follow you home. Progress is kept — walking back in resumes it.
    [Export] public float AttentionMargin { get; set; } = 18.0f;

    /// Fired when the player first steps in, and again when it is finished.
    [Signal] public delegate void ZoneStartedEventHandler(string title);
    [Signal] public delegate void ZoneClearedEventHandler(string title, int rounds);

    public enum ZoneState
    {
        Dormant,
        Running,
        Cleared,
    }

    public ZoneState State { get; private set; } = ZoneState.Dormant;

    /// How far through, 0 to 1. What the readout draws.
    public float Progress { get; private set; }

    public bool PlayerInside { get; private set; }

    private Player? _player;
    private Horde? _horde;
    private RunDirector? _director;
    private MeshInstance3D? _marker;

    private float _held;
    private int _killed;
    private float _spawnCredit;
    private ulong _rng;

    public override void _Ready()
    {
        Node? parent = GetParent()?.GetParent();
        _player = parent?.GetNodeOrNull<Player>("Player");
        _horde = parent?.GetNodeOrNull<Horde>("Horde");
        _director = parent?.GetNodeOrNull<RunDirector>("RunDirector");
        _marker = GetNodeOrNull<MeshInstance3D>("Marker");

        // Seeded from the position, so two zones on one map do not spawn in
        // lockstep and the same zone on the same seed is the same encounter.
        _rng = 0x9E3779B97F4A7C15UL ^ (ulong)Mathf.RoundToInt(GlobalPosition.X * 977.0f)
                                   ^ ((ulong)Mathf.RoundToInt(GlobalPosition.Z * 131.0f) << 21);
        if (_rng == 0)
            _rng = 1;

        if (_horde != null)
            _horde.EnemyKilled += OnEnemyKilled;
    }

    /// The horde's event is a plain C# delegate and holds a strong reference to
    /// this node — leaving it connected past a scene change is a call into a
    /// freed object.
    public override void _ExitTree()
    {
        if (_horde != null)
            _horde.EnemyKilled -= OnEnemyKilled;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_player == null || State == ZoneState.Cleared)
            return;

        var step = (float)delta;
        PlayerInside = Contains(_player.GlobalPosition);

        if (State == ZoneState.Dormant)
        {
            if (PlayerInside)
                Start();

            return;
        }

        // Spawning continues while the player is outside. A zone that switched
        // off the moment they stepped over the line would let anyone farm it a
        // metre at a time from the edge, which is not a decision, it is a chore.
        Advance(step);
    }

    private void Start()
    {
        State = ZoneState.Running;
        _held = 0.0f;
        _killed = 0;
        _spawnCredit = 0.0f;

        for (int i = 0; i < OpeningBurst; i++)
            SpawnOnPerimeter();

        Recolour(new Color(1.0f, 0.52f, 0.22f), new Color(0.75f, 0.18f, 0.05f), 0.72f, 2.6f);
        EmitSignal(SignalName.ZoneStarted, Title);
        GD.Print($"{Title} woke — {OpeningBurst} arriving, tier {Tier}");
    }

    private void Advance(float step)
    {
        if (Attending)
        {
            _spawnCredit += SpawnRate * step;

            // The same ceiling the director respects. Without it a zone alone can
            // fill the pool, and every other source of enemies in the game —
            // including the next zone — silently stops working.
            int ceiling = _director?.MaxLiveEnemies ?? int.MaxValue;

            while (_spawnCredit >= 1.0f && (_horde?.Pool.Count ?? 0) < ceiling)
            {
                _spawnCredit -= 1.0f;
                if (!SpawnOnPerimeter())
                    break;
            }
        }
        else
        {
            // Discarded rather than banked, for the reason the director discards
            // its own: banked, the moment the player comes back the whole absence
            // arrives at once.
            _spawnCredit = 0.0f;
        }

        Progress = (ZoneKind)Kind switch
        {
            // Only while inside. Leaving pauses the clock rather than resetting
            // it: a fight that punishes repositioning has exactly one correct
            // answer, and the answer is to stand still, which is the least
            // interesting thing this game can ask for.
            ZoneKind.Hold => Advance(ref _held, PlayerInside ? step : 0.0f, HoldSeconds),

            ZoneKind.Purge => Mathf.Clamp(_killed / (float)Mathf.Max(1, PurgeKills), 0.0f, 1.0f),

            // A Breach is finished when its burst is dead, wherever they died.
            // The cache was the trigger; surviving what came out of it is the
            // encounter.
            ZoneKind.Breach => Mathf.Clamp(_killed / (float)Mathf.Max(1, OpeningBurst), 0.0f, 1.0f),

            _ => 0.0f,
        };

        if (Progress >= 1.0f)
            Clear();
    }

    private static float Advance(ref float accumulated, float step, float target)
    {
        accumulated += step;
        return Mathf.Clamp(accumulated / Mathf.Max(0.001f, target), 0.0f, 1.0f);
    }

    private void Clear()
    {
        State = ZoneState.Cleared;
        Progress = 1.0f;
        SetPhysicsProcess(false);

        if (_horde != null)
            _horde.EnemyKilled -= OnEnemyKilled;

        // Ammunition first, because it is the reward that decides whether the
        // *next* zone is attemptable. Loot that cannot be spent on staying alive
        // makes the second zone strictly harder than the first however well the
        // player did the first.
        int taken = _player?.GetNodeOrNull<WeaponHandler>("WeaponHandler")?.AddReserve(Rounds) ?? 0;

        _director?.DropCache($"{Name}Cache", GlobalPosition, bias: 2.2f + Tier * 0.8f,
                             rolls: Rolls, seconds: 1.4f);

        Recolour(new Color(0.55f, 1.0f, 0.68f), new Color(0.12f, 0.62f, 0.30f), 0.22f, 0.5f);
        EmitSignal(SignalName.ZoneCleared, Title, taken);
        GD.Print($"{Title} cleared — {Rolls} rolls and {taken} rounds");
    }

    private void OnEnemyKilled(int type, Vector3 position)
    {
        if (State != ZoneState.Running)
            return;

        // Inside the zone, for Purge — killing things elsewhere does not empty a
        // nest. A Breach counts its own burst wherever it dies, because the burst
        // chases the player out and finishing it in the open is the intended way
        // through.
        if ((ZoneKind)Kind == ZoneKind.Breach || Contains(position))
            _killed++;
    }

    /// Whether the player is close enough for this zone to be doing anything.
    ///
    /// Chebyshev against the boundary plus a margin, so the region is the
    /// rectangle grown outward rather than a circle around its centre — a circle
    /// on a 26 by 20 zone is either short of the long edges or far past the short
    /// ones.
    public bool Attending
    {
        get
        {
            if (_player == null)
                return false;

            float dx = Mathf.Abs(_player.GlobalPosition.X - GlobalPosition.X) - HalfExtent.X;
            float dz = Mathf.Abs(_player.GlobalPosition.Z - GlobalPosition.Z) - HalfExtent.Y;
            return Mathf.Max(dx, dz) <= AttentionMargin;
        }
    }

    public bool Contains(Vector3 position) =>
        Mathf.Abs(position.X - GlobalPosition.X) <= HalfExtent.X
        && Mathf.Abs(position.Z - GlobalPosition.Z) <= HalfExtent.Y;

    /// A point on the zone's own boundary.
    ///
    /// The perimeter, not a ring around the player, and that is the whole
    /// mechanic. Ring spawns surround whoever they are aimed at, so retreating
    /// only ever means running into more of them; a perimeter has a far side, so
    /// backing out the way you came in is a move that works. It is also what
    /// makes the rectangle on the ground mean something — the edge is where they
    /// come from, and standing on it is a choice.
    private bool SpawnOnPerimeter()
    {
        if (_horde == null)
            return false;

        // Several tries, at different points on the boundary.
        //
        // The horde refuses a spawn inside a wall, which is correct and means a
        // zone with a building against one edge quietly under-delivers every
        // wave. One try produced seven of an eight-enemy opening burst — not
        // enough to notice while playing, and enough to make a Purge quota that
        // counts kills take longer for reasons the player cannot see. Four edges,
        // so six tries reaches all of them.
        for (int attempt = 0; attempt < 6; attempt++)
        {
            if (_horde.SpawnByIntensity(OutOfSight(PerimeterPoint())))
                return true;
        }

        return false;
    }

    /// How close a spawn has to be before the player could see it arrive.
    ///
    /// Twenty-two metres. The fog reaches full at 35 and the eye picks up
    /// movement well before that, so anything nearer than this is somewhere an
    /// arrival is genuinely watchable.
    [Export] public float SightlineMetres { get; set; } = 22.0f;

    /// How far off the view direction still counts as "in front of".
    [Export] public float SightlineDegrees { get; set; } = 60.0f;

    /// Moves a spawn to the far side if the player is looking at it.
    ///
    /// A zone spawns on its own perimeter, which is the point — but the player
    /// may be standing on that perimeter looking straight at it, and no amount of
    /// emerge ramp makes a body appearing six metres in front of you read as
    /// something that walked there. The fog handles this everywhere else by
    /// putting the spawn ring past the horizon; a zone has no such distance to
    /// hide behind.
    ///
    /// Only when the opposite side is not itself close. On a 26 by 20 rectangle
    /// the far edge can be twenty metres away or two, depending on where the
    /// player is standing, and flipping into a second sightline is worse than
    /// staying put — a rule that has to be right in both directions is a rule
    /// that needs the second check.
    private Vector3 OutOfSight(Vector3 at)
    {
        if (_player == null)
            return at;

        Vector3 toSpawn = at - _player.GlobalPosition;
        var flat = new Vector2(toSpawn.X, toSpawn.Z);
        float distance = flat.Length();

        if (distance > SightlineMetres || distance < 0.01f)
            return at;

        Vector2 facing = _player.Facing;
        if (facing.LengthSquared() < 0.0001f)
            return at;

        float offAxis = Mathf.RadToDeg(Mathf.Acos(
            Mathf.Clamp(flat.Normalized().Dot(facing.Normalized()), -1.0f, 1.0f)));

        if (offAxis > SightlineDegrees)
            return at;

        // Mirrored through the zone's centre, which is the far side of whichever
        // edge it came from.
        var mirrored = new Vector3(
            GlobalPosition.X * 2.0f - at.X,
            0.0f,
            GlobalPosition.Z * 2.0f - at.Z);

        Vector3 toMirror = mirrored - _player.GlobalPosition;
        float mirrorDistance = new Vector2(toMirror.X, toMirror.Z).Length();

        // No better. Staying put beats swapping one sightline for another.
        return mirrorDistance > distance ? mirrored : at;
    }

    /// A point just outside the zone's boundary.
    ///
    /// Weighted by edge length, so a long side is not as thinly manned as a
    /// short one — with a 13 by 10 rectangle the long edges are 57% of the
    /// perimeter and should get 57% of the arrivals.
    private Vector3 PerimeterPoint()
    {
        float along = NextFloat() * 2.0f * (HalfExtent.X + HalfExtent.Y);

        Vector2 offset;
        if (along < HalfExtent.X * 2.0f)
            offset = new Vector2(along - HalfExtent.X, -HalfExtent.Y);
        else if ((along -= HalfExtent.X * 2.0f) < HalfExtent.Y * 2.0f)
            offset = new Vector2(HalfExtent.X, along - HalfExtent.Y);
        else if ((along -= HalfExtent.Y * 2.0f) < HalfExtent.X * 2.0f)
            offset = new Vector2(HalfExtent.X - along, HalfExtent.Y);
        else
            offset = new Vector2(-HalfExtent.X, HalfExtent.Y - (along - HalfExtent.X * 2.0f));

        // A little outside, so they walk in rather than materialising on the line
        // the player can see.
        offset *= 1.08f;

        return new Vector3(GlobalPosition.X + offset.X, 0.0f, GlobalPosition.Z + offset.Y);
    }

    /// Recolours the ground marker in place.
    ///
    /// The zone is the only thing on the map whose appearance is its state, so
    /// the marker has to change: a rectangle that looks identical dormant, live
    /// and spent is a rectangle the player learns to ignore.
    private void Recolour(Color edge, Color fill, float strength, float pulseSpeed)
    {
        if (_marker?.MaterialOverride is not ShaderMaterial material)
            return;

        material.SetShaderParameter("edge_colour", edge);
        material.SetShaderParameter("fill_colour", fill);
        material.SetShaderParameter("strength", strength);

        // The pulse carries the state as much as the colour does. A live zone
        // breathes fast and a spent one barely at all, which is legible from far
        // enough away that the colour is still two orange pixels.
        material.SetShaderParameter("pulse_speed", pulseSpeed);
    }

    private float NextFloat()
    {
        _rng ^= _rng << 13;
        _rng ^= _rng >> 7;
        _rng ^= _rng << 17;
        return (_rng >> 40) / 16777216.0f;
    }
}
