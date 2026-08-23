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

        Recolour(new Color(1.0f, 0.42f, 0.20f), new Color(0.75f, 0.10f, 0.04f), 1.0f);
        EmitSignal(SignalName.ZoneStarted, Title);
        GD.Print($"{Title} woke — {OpeningBurst} arriving, tier {Tier}");
    }

    private void Advance(float step)
    {
        _spawnCredit += SpawnRate * step;

        while (_spawnCredit >= 1.0f)
        {
            _spawnCredit -= 1.0f;
            if (!SpawnOnPerimeter())
                break;
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

        Recolour(new Color(0.55f, 1.0f, 0.68f), new Color(0.12f, 0.62f, 0.30f), 0.45f);
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
            if (_horde.SpawnByIntensity(PerimeterPoint()))
                return true;
        }

        return false;
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
    private void Recolour(Color inner, Color outer, float strength)
    {
        if (_marker?.MaterialOverride is not ShaderMaterial material)
            return;

        material.SetShaderParameter("inner_colour", inner);
        material.SetShaderParameter("outer_colour", outer);
        material.SetShaderParameter("strength", strength);
    }

    private float NextFloat()
    {
        _rng ^= _rng << 13;
        _rng ^= _rng >> 7;
        _rng ^= _rng << 17;
        return (_rng >> 40) / 16777216.0f;
    }
}
