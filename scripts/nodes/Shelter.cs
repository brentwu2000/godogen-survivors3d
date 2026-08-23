using Godot;

/// Which fitting the player is standing at.
///
/// Ordered as they are met walking in: the gate is behind you, the armoury is
/// the wall you face, and the map table is what you cross to reach it.
public enum Fitting
{
    None,
    Armoury,
    Locker,
    Records,
    Board,
    Map,
    Gate,
}

/// The room between runs, walked rather than scrolled.
///
/// This replaces a fifteen-row text screen with eight verb keys. The screen
/// worked and was legible, and it had one problem nothing in it could fix:
/// everything cost the same effort. Selling the stash, changing terrain, taking
/// a contract and launching the run were four keys on one screen, so the shape of
/// the between-runs loop was flat — a list of equally-weighted options rather
/// than a place with a route through it.
///
/// **Standing somewhere is the input.** Walking to the armoury is what selects
/// the armoury; there is no key for it. That leaves exactly one verb key, `[E]`,
/// and a second, `[C]`, for the three fittings that have a second thing to do —
/// which is the whole keyboard, against the eight the screen needed.
///
/// Built in code rather than modelled, for the same reason everything else here
/// is: it is one seed's worth of decisions, and a hand-authored room is a set of
/// dimensions nothing records the reason for.
public partial class Shelter : Node3D
{
    /// Half the room, across and deep. Eleven and a half by eight is small
    /// enough to cross in a few seconds and big enough that six fittings do not
    /// crowd each other — the walk between them is the pacing, and a room twice
    /// this size would make routine errands tedious.
    [Export] public float HalfWidth { get; set; } = 11.5f;
    [Export] public float HalfDepth { get; set; } = 8.0f;

    [Export] public float WallHeight { get; set; } = 3.4f;

    /// How close counts as standing at a fitting.
    ///
    /// Generous. The alternative is a player who is obviously at the counter and
    /// whose prompt will not appear, which reads as the game being broken rather
    /// than as being fifteen centimetres out.
    [Export] public float ReachRadius { get; set; } = 2.6f;

    /// Fired when the player arrives at or leaves a fitting.
    [Signal] public delegate void FocusChangedEventHandler(int fitting);

    public Fitting Focus { get; private set; } = Fitting.None;

    /// Where each fitting stands, in room space. Public so a probe can walk to
    /// them without knowing how the room was laid out.
    public System.Collections.Generic.IReadOnlyDictionary<Fitting, Vector3> Stations => _stations;

    private readonly System.Collections.Generic.Dictionary<Fitting, Vector3> _stations = new();
    private Player? _player;

    /// The armoury counter runs along the south wall rather than being a point.
    ///
    /// It is the fitting the player spends most time at, and a counter you stand
    /// anywhere along is a different object from a lectern you stand exactly at.
    /// The reach test uses the segment, so the whole counter is the station.
    [Export] public float SlotsAlongX { get; set; } = 7.0f;

    public override void _Ready()
    {
        _player = GetParent()?.GetNodeOrNull<Player>("Player");

        Build();
        Place();
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_player == null)
            return;

        Fitting nearest = Nearest(_player.GlobalPosition);
        if (nearest == Focus)
            return;

        Focus = nearest;
        EmitSignal(SignalName.FocusChanged, (int)Focus);
    }

    /// The fitting within reach, or None.
    ///
    /// Nearest rather than first-found, so two fittings whose reaches overlap
    /// resolve to the one actually being stood at instead of to whichever was
    /// declared earlier.
    public Fitting Nearest(Vector3 position)
    {
        Fitting best = Fitting.None;
        float bestDistance = ReachRadius;

        foreach ((Fitting fitting, Vector3 at) in _stations)
        {
            float distance = fitting == Fitting.Armoury
                ? DistanceToCounter(position, at)
                : Flat(position - at).Length();

            if (distance >= bestDistance)
                continue;

            bestDistance = distance;
            best = fitting;
        }

        return best;
    }

    /// Distance to the armoury counter, treated as a segment along X.
    private float DistanceToCounter(Vector3 position, Vector3 centre)
    {
        float half = SlotsAlongX * 0.5f;
        float x = Mathf.Clamp(position.X - centre.X, -half, half);
        return Flat(position - (centre + new Vector3(x, 0.0f, 0.0f))).Length();
    }

    private static Vector2 Flat(Vector3 v) => new(v.X, v.Z);

    /// Where everything stands.
    ///
    /// Against the walls rather than scattered, with the map table the one thing
    /// in the middle. A room whose furniture is all in the centre has no route
    /// through it — the walk from the locker to the gate should pass something.
    private void Place()
    {
        float wallGap = 1.6f;

        _stations[Fitting.Armoury] = new Vector3(0.0f, 0.0f, HalfDepth - wallGap);
        _stations[Fitting.Locker] = new Vector3(-HalfWidth + wallGap, 0.0f, 2.0f);
        _stations[Fitting.Records] = new Vector3(HalfWidth - wallGap, 0.0f, 2.0f);
        _stations[Fitting.Board] = new Vector3(-HalfWidth + wallGap, 0.0f, -3.5f);
        _stations[Fitting.Map] = new Vector3(0.0f, 0.0f, 0.0f);
        _stations[Fitting.Gate] = new Vector3(0.0f, 0.0f, -HalfDepth + wallGap);
    }

    /// Floor, four walls, and a piece of furniture per fitting.
    private void Build()
    {
        var room = new MeshBuilder();

        var floor = new Color(0.29f, 0.28f, 0.27f);
        var wall = new Color(0.35f, 0.34f, 0.33f);
        var trim = new Color(0.46f, 0.42f, 0.36f);

        // Slightly below zero and slightly oversized, so the player never sees
        // the seam where the floor ends and never stands on the join.
        room.Box(new Vector3(0.0f, -0.05f, 0.0f),
                 new Vector3(HalfWidth * 2.0f + 1.0f, 0.1f, HalfDepth * 2.0f + 1.0f), floor);

        float thickness = 0.5f;
        float height = WallHeight;

        room.Box(new Vector3(0.0f, height * 0.5f, -HalfDepth),
                 new Vector3(HalfWidth * 2.0f, height, thickness), wall);
        room.Box(new Vector3(0.0f, height * 0.5f, HalfDepth),
                 new Vector3(HalfWidth * 2.0f, height, thickness), wall);
        room.Box(new Vector3(-HalfWidth, height * 0.5f, 0.0f),
                 new Vector3(thickness, height, HalfDepth * 2.0f), wall);
        room.Box(new Vector3(HalfWidth, height * 0.5f, 0.0f),
                 new Vector3(thickness, height, HalfDepth * 2.0f), wall);

        // A rail at head height around the room, which is the cheapest thing that
        // stops four flat walls reading as a box. It catches the light at a
        // different angle from the wall behind it, so the room has an edge to it
        // even where nothing is standing.
        foreach (float z in new[] { -HalfDepth + thickness, HalfDepth - thickness })
        {
            room.Box(new Vector3(0.0f, 2.3f, z),
                     new Vector3(HalfWidth * 2.0f - 0.4f, 0.12f, 0.14f), trim);
        }

        // `PropLibrary.Material()` rather than a new one, and it is not
        // laziness: a `MeshInstance3D` with no material gets Godot's default,
        // which does not read vertex colours at all. The room built without it
        // rendered every wall, floor and stick of furniture in flat white — the
        // colours were all there in the mesh and nothing was looking at them.
        AddChild(new MeshInstance3D
        {
            Name = "Room",
            Mesh = room.Build(),
            MaterialOverride = PropLibrary.Material(),
        });

        AddChild(Furniture());
        AddChild(GateLight());
        AddChild(Collision());
    }

    /// One recognisable object per fitting.
    ///
    /// Shape rather than labels: the counter, the locker, the table and the gate
    /// are different silhouettes, so the room can be learned once and navigated
    /// afterwards without reading anything.
    private MeshInstance3D Furniture()
    {
        var pieces = new MeshBuilder();

        var metal = new Color(0.38f, 0.40f, 0.44f);
        var wood = new Color(0.42f, 0.32f, 0.22f);
        var paper = new Color(0.72f, 0.69f, 0.60f);
        var light = new Color(0.30f, 0.62f, 0.48f);

        // Armoury: a long counter with a rack behind it.
        pieces.Box(new Vector3(0.0f, 0.55f, HalfDepth - 0.9f), new Vector3(SlotsAlongX, 1.1f, 0.7f), metal);
        for (int i = 0; i < 5; i++)
        {
            float x = -SlotsAlongX * 0.4f + i * SlotsAlongX * 0.2f;
            pieces.Box(new Vector3(x, 1.6f, HalfDepth - 0.35f), new Vector3(0.14f, 1.0f, 0.14f), metal);
        }

        // Locker: a bank of tall doors.
        for (int i = 0; i < 3; i++)
        {
            pieces.Box(new Vector3(-HalfWidth + 0.6f, 1.0f, 1.0f + i * 1.0f),
                       new Vector3(0.8f, 2.0f, 0.9f), metal);
        }

        // Records: a cabinet with a shelf of files.
        pieces.Box(new Vector3(HalfWidth - 0.7f, 0.9f, 2.0f), new Vector3(0.9f, 1.8f, 2.4f), wood);
        pieces.Box(new Vector3(HalfWidth - 0.7f, 1.5f, 2.0f), new Vector3(0.95f, 0.3f, 2.0f), paper);

        // Board: a pinned notice board, angled off the wall.
        pieces.Box(new Vector3(-HalfWidth + 0.5f, 1.6f, -3.5f), new Vector3(0.16f, 1.6f, 2.6f), wood);
        pieces.Box(new Vector3(-HalfWidth + 0.62f, 1.6f, -3.5f), new Vector3(0.06f, 1.3f, 2.2f), paper);

        // Map: a low table in the middle, which is the one thing to walk around.
        pieces.Box(new Vector3(0.0f, 0.42f, 0.0f), new Vector3(3.2f, 0.14f, 2.2f), wood);
        foreach (int sx in new[] { -1, 1 })
        {
            foreach (int sz in new[] { -1, 1 })
            {
                pieces.Box(new Vector3(sx * 1.4f, 0.2f, sz * 0.9f),
                           new Vector3(0.16f, 0.4f, 0.16f), wood);
            }
        }

        // Gate: a frame in the north wall. The lit panel inside it is built
        // separately, below — it is the one surface in the room that has to give
        // off light rather than receive it, and the shared prop material has no
        // emission. Drawn with everything else it was a dark slab: the correct
        // colour, unlit, facing away from the only lamp in the room.
        pieces.Box(new Vector3(0.0f, 1.5f, -HalfDepth + 0.35f), new Vector3(3.0f, 3.0f, 0.2f), metal);
        _ = light;

        return new MeshInstance3D
        {
            Name = "Furniture",
            Mesh = pieces.Build(),
            MaterialOverride = PropLibrary.Material(),
        };
    }

    /// The lit panel in the gate.
    ///
    /// Its own node with its own material, because it is the only thing in the
    /// room that emits. Green, and nothing else in here is: the gate is the one
    /// fitting that ends the visit, and a room where the exit looks like the
    /// filing cabinet is a room the player has to read to leave.
    private MeshInstance3D GateLight()
    {
        var glow = new Color(0.32f, 0.72f, 0.54f);

        return new MeshInstance3D
        {
            Name = "GateLight",
            Mesh = new BoxMesh { Size = new Vector3(2.2f, 2.4f, 0.06f) },
            // In front of the frame, not behind it. The frame sits at
            // -HalfDepth + 0.35 and the room is on the +Z side of it, so a panel
            // at +0.24 is *further* from the player and completely occluded —
            // which is what the first version did, correctly and invisibly.
            Position = new Vector3(0.0f, 1.4f, -HalfDepth + 0.52f),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = glow,
                EmissionEnabled = true,
                Emission = glow,
                EmissionEnergyMultiplier = 1.6f,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            },
        };
    }

    /// Walls the player cannot walk through.
    ///
    /// Four boxes rather than a trimesh of the room. `CreateTrimeshShape()` on a
    /// generated mesh drops the frame rate below one (godot.md:39), and a room
    /// that is four rectangles to begin with already has its collider.
    private StaticBody3D Collision()
    {
        var body = new StaticBody3D { Name = "Walls" };
        float thickness = 0.5f;
        float height = WallHeight;

        void Wall(string name, Vector3 at, Vector3 size)
        {
            body.AddChild(new CollisionShape3D
            {
                Name = name,
                Position = at,
                Shape = new BoxShape3D { Size = size },
            });
        }

        Wall("North", new Vector3(0.0f, height * 0.5f, -HalfDepth),
             new Vector3(HalfWidth * 2.0f, height, thickness));
        Wall("South", new Vector3(0.0f, height * 0.5f, HalfDepth),
             new Vector3(HalfWidth * 2.0f, height, thickness));
        Wall("West", new Vector3(-HalfWidth, height * 0.5f, 0.0f),
             new Vector3(thickness, height, HalfDepth * 2.0f));
        Wall("East", new Vector3(HalfWidth, height * 0.5f, 0.0f),
             new Vector3(thickness, height, HalfDepth * 2.0f));

        return body;
    }

    /// What the fitting under the player does, for the prompt.
    ///
    /// Two verbs at most, and the second is empty for the three fittings that do
    /// not have one — which is what keeps the prompt honest about how much of the
    /// keyboard is in use.
    public static (string Title, string First, string Second) Prompt(Fitting fitting) => fitting switch
    {
        Fitting.Armoury => ("ARMOURY", "buy or equip", "sell it back"),
        Fitting.Locker => ("LOCKER", "sell the stash", ""),
        Fitting.Records => ("RECORDS", "", ""),
        Fitting.Board => ("CONTRACTS", "take the one selected", "reroll the board"),
        Fitting.Map => ("MAP TABLE", "change terrain", "play today's run"),
        Fitting.Gate => ("GATE", "launch", ""),
        _ => ("", "", ""),
    };
}
