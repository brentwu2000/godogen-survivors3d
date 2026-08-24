using Godot;

public enum PropKind
{
    Container,
    Barrier,
    Rubble,
    Wall,
    Dumpster,

    /// Landmarks. Tall enough to be seen across the arena, and never used as
    /// ordinary cover.
    WaterTower,
    Billboard,

    // --- The city ----------------------------------------------------------
    //
    // A street, rather than a yard. The rail yard's furniture is industrial and
    // stackable and sits in open ground; this is what is left standing on a road
    // — longer, lower, and arranged along an axis rather than in a heap.

    Hoarding,
    BusShell,
    CarWreck,
    TrafficBarrier,
    Kiosk,
    TowerBlock,
    Overpass,

    // --- The laboratory ----------------------------------------------------
    //
    // An interior, and the first one. Everything above is weathered outdoor
    // material — concrete, rust, tar — and this is the opposite: painted panel,
    // stainless steel, glass, and equipment that was clean until recently. The
    // contrast is most of what will make it read as inside rather than as a
    // differently-coloured yard.

    Partition,
    ServerRack,
    CeilingFall,
    LabBench,
    SpecimenTank,
    VentStack,
    Gantry,
}

/// What a piece of cover is *for*, as opposed to what it is.
///
/// The layout generator decides shape and position long before anything knows
/// which biome it is standing in — a long thin footprint wants a barricade, a
/// squarish three-metre one wants something you cannot see over, and those are
/// facts about the fight rather than about the scenery. Roles name that decision
/// so a biome can answer it with its own furniture.
///
/// **The point of the indirection is that the layout does not change.** The
/// generator's rolls and thresholds were tuned over several phases against the
/// three existing biomes; picking a role with exactly those rolls and letting the
/// biome substitute a kind means a laboratory has the same sight lines and the
/// same cover density as the rail yard it replaced, and differs only in what the
/// player is looking at. A per-biome weight table would have changed both at
/// once and made neither measurable.
///
/// Adding a role is expensive — every biome has to answer it — so there are five
/// for cover and two for scenery, and that is meant to stay small.
public enum PropRole
{
    /// Long, thin, and taller than a person. The thing that cuts a sight line.
    Wall,

    /// Roughly square in plan, about three metres up. Hard cover you go around.
    Bulk,

    /// A collapsed heap. Broken outline, partial cover, waist to shoulder.
    Heap,

    /// Waist high. Stops a charge, stops nothing else.
    Low,

    /// The odd one, for texture rather than for tactics. Every place has some
    /// object that is only there because places have objects in them.
    Odd,

    /// Scenery. Ten metres and up, placed outside the arena, never cover.
    Tall,

    /// Scenery again, and deliberately a second one: a single landmark shape
    /// repeated around the edge reads as a texture rather than as a skyline.
    Sign,
}

/// The arena's cover, built out of boxes at startup.
///
/// Authored in a unit footprint — X and Z run from -0.5 to 0.5, Y starts at 0 —
/// so a piece of cover is placed by scaling the instance to the footprint the
/// level generator already decided on. Y is left near its authored height, since
/// stretching a container to twice its height reads as a mistake in a way that
/// stretching it along its length does not.
///
/// Colour is per-vertex rather than per-material. One material serves every prop
/// that way, which is what keeps the whole set inside a handful of draw calls —
/// and a material per surface would have put the count back where using imported
/// models would have.
public static class PropLibrary
{
    // Pitched against the ground, not in the abstract. The first pass used values
    // around 0.6 and the props came out near-white on an asphalt floor sitting
    // around 0.35 — the cover read as polystyrene. Everything here is a little
    // above the floor and no more, so a container is a solid object on a road
    // rather than a cut-out laid over one.
    private static readonly Color Steel = new(0.30f, 0.32f, 0.34f);
    private static readonly Color Concrete = new(0.46f, 0.45f, 0.43f);
    private static readonly Color ConcreteDark = new(0.34f, 0.335f, 0.32f);
    private static readonly Color Rust = new(0.38f, 0.21f, 0.14f);
    private static readonly Color PaintRed = new(0.42f, 0.17f, 0.14f);
    private static readonly Color PaintBlue = new(0.17f, 0.25f, 0.33f);
    private static readonly Color PaintGreen = new(0.19f, 0.27f, 0.20f);
    private static readonly Color Tar = new(0.12f, 0.12f, 0.13f);
    private static readonly Color Board = new(0.55f, 0.52f, 0.45f);

    // The city's additions, pitched into the same band as everything above. The
    // temptation with a street is to reach for saturated paint — a yellow bus, a
    // red bus shelter — and it is the same mistake the props made the first time:
    // anything brighter than about 0.55 stops looking like a painted object under
    // this sun and starts looking like a light source.
    private static readonly Color Glass = new(0.10f, 0.13f, 0.16f);
    private static readonly Color PaintYellow = new(0.44f, 0.36f, 0.13f);
    private static readonly Color PaintOrange = new(0.52f, 0.26f, 0.08f);
    private static readonly Color Chalk = new(0.54f, 0.53f, 0.50f);
    private static readonly Color Brick = new(0.34f, 0.22f, 0.18f);

    // The laboratory. Cooler and cleaner than anything above, which is the whole
    // job: an interior that borrows the outdoor palette reads as a yard with the
    // lights off. Still inside the same band — a white lab is a lab made of paper.
    private static readonly Color Panel = new(0.50f, 0.52f, 0.50f);
    private static readonly Color PanelTrim = new(0.29f, 0.33f, 0.34f);
    private static readonly Color Fluid = new(0.20f, 0.44f, 0.43f);
    private static readonly Color Cable = new(0.13f, 0.12f, 0.14f);
    private static readonly Color Amber = new(0.50f, 0.34f, 0.10f);

    /// One shared material for every prop. Vertex colour is the albedo, so the
    /// palette lives in the geometry and this never has to grow a variant.
    public static StandardMaterial3D Material() => new()
    {
        VertexColorUseAsAlbedo = true,
        Roughness = 0.92f,
        Metallic = 0.0f,

        // Boxes are closed and correctly wound, so back faces are never needed —
        // and leaving culling on is what keeps the shadow pass honest.
        CullMode = BaseMaterial3D.CullModeEnum.Back,
    };

    public static ArrayMesh Build(PropKind kind) => kind switch
    {
        PropKind.Container => Container(),
        PropKind.Barrier => Barrier(),
        PropKind.Rubble => Rubble(),
        PropKind.Wall => Wall(),
        PropKind.Dumpster => Dumpster(),
        PropKind.WaterTower => WaterTower(),
        PropKind.Billboard => Billboard(),

        PropKind.Hoarding => Hoarding(),
        PropKind.BusShell => BusShell(),
        PropKind.CarWreck => CarWreck(),
        PropKind.TrafficBarrier => TrafficBarrier(),
        PropKind.Kiosk => Kiosk(),
        PropKind.TowerBlock => TowerBlock(),
        PropKind.Overpass => Overpass(),

        PropKind.Partition => Partition(),
        PropKind.ServerRack => ServerRack(),
        PropKind.CeilingFall => CeilingFall(),
        PropKind.LabBench => LabBench(),
        PropKind.SpecimenTank => SpecimenTank(),
        PropKind.VentStack => VentStack(),
        PropKind.Gantry => Gantry(),

        _ => Container(),
    };

    /// How tall each kind stands when its instance is not scaled vertically.
    /// Cover is around three metres, which is the height the arena was blocked
    /// out at and the height the camera framing assumes.
    public static float Height(PropKind kind) => kind switch
    {
        PropKind.Barrier => 1.15f,
        PropKind.Dumpster => 1.7f,
        PropKind.Rubble => 1.9f,
        PropKind.WaterTower => 14.0f,
        PropKind.Billboard => 11.0f,

        // Matched to the kind each one stands in for, not to what the real object
        // measures. A biome swaps furniture and must not swap the fight: cover
        // the player can see over in one place and not in another would be a
        // balance change wearing a set change's clothes.
        PropKind.TrafficBarrier => 1.15f,
        PropKind.CarWreck => 1.9f,
        PropKind.Kiosk => 2.4f,
        PropKind.TowerBlock => 22.0f,
        PropKind.Overpass => 9.0f,

        PropKind.LabBench => 1.15f,
        PropKind.CeilingFall => 1.9f,
        PropKind.SpecimenTank => 2.4f,
        PropKind.VentStack => 16.0f,
        PropKind.Gantry => 10.0f,

        // Full height, and this is the number that makes Cold Storage an
        // interior rather than a blue field at night.
        //
        // A ceiling was the obvious answer and it does not work in this camera.
        // The eye sits 5.7 m up tilted 26 degrees down with a 60 degree field,
        // so it never looks more than about four degrees above horizontal — a
        // roof at eight metres is only in shot beyond forty-odd metres, which is
        // well past where the fog has gone black. The roof is *there*, it does
        // occlude the sky, and it is indistinguishable from the sky it occludes.
        //
        // What the camera can see is what stands between the player and the fog.
        // A three-metre partition in a room with an eight-metre ceiling reads as
        // a cubicle in a field; one that runs floor to ceiling reads as a wall,
        // breaks the horizon line, and closes the arena down to the room the
        // player is standing in. It costs nothing mechanically: shots and sight
        // lines are resolved in two dimensions, so prop height is cosmetic.
        PropKind.Partition => 7.6f,

        _ => 3.0f,
    };

    /// Whether a kind is scenery rather than cover. Landmarks are placed by hand
    /// on the grid and are not drawn from the cover pool.
    public static bool IsLandmark(PropKind kind) =>
        RoleOf(kind) is PropRole.Tall or PropRole.Sign;

    /// What each kind is for.
    ///
    /// Held here rather than only in the biome table so that "is this scenery?"
    /// has one answer. The table maps role to kind per biome; this maps back, and
    /// a kind that appeared in the wrong slot of some biome's table would be
    /// caught by `BiomeProbe` rather than by a landmark quietly being used as
    /// cover and standing fourteen metres tall in the middle of the arena.
    public static PropRole RoleOf(PropKind kind) => kind switch
    {
        PropKind.Wall => PropRole.Wall,
        PropKind.Container => PropRole.Bulk,
        PropKind.Rubble => PropRole.Heap,
        PropKind.Barrier => PropRole.Low,
        PropKind.Dumpster => PropRole.Odd,
        PropKind.WaterTower => PropRole.Tall,
        PropKind.Billboard => PropRole.Sign,

        PropKind.Hoarding => PropRole.Wall,
        PropKind.BusShell => PropRole.Bulk,
        PropKind.CarWreck => PropRole.Heap,
        PropKind.TrafficBarrier => PropRole.Low,
        PropKind.Kiosk => PropRole.Odd,
        PropKind.TowerBlock => PropRole.Tall,
        PropKind.Overpass => PropRole.Sign,

        PropKind.Partition => PropRole.Wall,
        PropKind.ServerRack => PropRole.Bulk,
        PropKind.CeilingFall => PropRole.Heap,
        PropKind.LabBench => PropRole.Low,
        PropKind.SpecimenTank => PropRole.Odd,
        PropKind.VentStack => PropRole.Tall,
        PropKind.Gantry => PropRole.Sign,

        _ => PropRole.Heap,
    };

    /// The set every biome falls back to, in role order.
    ///
    /// This is the rail yard's furniture, and it is the default because it is
    /// what every biome used before biomes could own one — so a `.tres` written
    /// before this existed keeps drawing exactly what it drew.
    public static readonly PropKind[] DefaultSet =
    {
        PropKind.Wall,
        PropKind.Container,
        PropKind.Rubble,
        PropKind.Barrier,
        PropKind.Dumpster,
        PropKind.WaterTower,
        PropKind.Billboard,
    };

    private static ArrayMesh Container()
    {
        var mesh = new MeshBuilder();
        Color paint = PaintBlue;

        mesh.Box(new Vector3(0.0f, 1.5f, 0.0f), new Vector3(1.0f, 2.8f, 0.94f), paint);

        // Corrugation. Seven ribs down the long side is enough to read as a
        // container at a hundred and twenty pixels and cheap enough to not care.
        for (int i = 0; i < 7; i++)
        {
            float x = -0.42f + i * 0.14f;
            mesh.Box(new Vector3(x, 1.5f, 0.0f), new Vector3(0.045f, 2.6f, 1.0f), Rust.Lerp(paint, 0.5f));
        }

        // Corner posts and the rails they carry, which is what makes the
        // silhouette read as stacked steel rather than as a painted crate.
        foreach (int sx in new[] { -1, 1 })
        {
            foreach (int sz in new[] { -1, 1 })
                mesh.Box(new Vector3(sx * 0.47f, 1.5f, sz * 0.47f), new Vector3(0.1f, 2.9f, 0.1f), Steel);
        }

        mesh.Box(new Vector3(0.0f, 2.92f, 0.0f), new Vector3(1.02f, 0.14f, 0.98f), Steel);
        mesh.Box(new Vector3(0.0f, 0.08f, 0.0f), new Vector3(1.02f, 0.16f, 0.98f), Steel);
        return mesh.Build();
    }

    private static ArrayMesh Barrier()
    {
        var mesh = new MeshBuilder();

        // Stepped profile, three boxes deep to fat bottom. A jersey barrier is
        // recognisable entirely by that taper.
        mesh.Box(new Vector3(0.0f, 0.16f, 0.0f), new Vector3(1.0f, 0.32f, 0.8f), Concrete);
        mesh.Box(new Vector3(0.0f, 0.55f, 0.0f), new Vector3(1.0f, 0.5f, 0.52f), Concrete);
        mesh.Box(new Vector3(0.0f, 0.98f, 0.0f), new Vector3(1.0f, 0.34f, 0.38f), ConcreteDark);

        // Hazard stripes, sunk a hair proud of the face so they do not z-fight.
        for (int i = 0; i < 4; i++)
        {
            float x = -0.36f + i * 0.24f;
            mesh.Box(new Vector3(x, 0.72f, 0.27f), new Vector3(0.1f, 0.5f, 0.02f), PaintRed);
        }

        return mesh.Build();
    }

    private static ArrayMesh Rubble()
    {
        var mesh = new MeshBuilder();
        ulong rng = 0x2545F4914F6CDD1DUL;

        // Slabs dropped at odd angles. The tumble is the whole silhouette, so the
        // yaw matters more than the count.
        for (int i = 0; i < 9; i++)
        {
            float x = (Next(ref rng) - 0.5f) * 0.8f;
            float z = (Next(ref rng) - 0.5f) * 0.8f;
            float y = 0.12f + Next(ref rng) * 1.4f;
            var size = new Vector3(0.25f + Next(ref rng) * 0.4f,
                                   0.16f + Next(ref rng) * 0.22f,
                                   0.25f + Next(ref rng) * 0.4f);

            mesh.Box(new Vector3(x, y, z), size,
                     Concrete.Lerp(ConcreteDark, Next(ref rng)),
                     Next(ref rng) * 90.0f);
        }

        // Twisted rebar, so the heap reads as a building rather than as gravel.
        for (int i = 0; i < 4; i++)
        {
            float x = (Next(ref rng) - 0.5f) * 0.6f;
            float z = (Next(ref rng) - 0.5f) * 0.6f;
            mesh.Box(new Vector3(x, 1.0f + Next(ref rng) * 0.6f, z),
                     new Vector3(0.05f, 1.2f, 0.05f), Rust, Next(ref rng) * 40.0f);
        }

        return mesh.Build();
    }

    private static ArrayMesh Wall()
    {
        var mesh = new MeshBuilder();

        mesh.Box(new Vector3(0.0f, 1.3f, 0.0f), new Vector3(1.0f, 2.6f, 0.36f), Concrete);
        mesh.Box(new Vector3(0.0f, 0.15f, 0.0f), new Vector3(1.02f, 0.3f, 0.5f), ConcreteDark);

        // A broken top rather than a flat one: an unbroken parapet reads as a
        // placeholder no matter how good the material is.
        ulong rng = 0xBF58476D1CE4E5B9UL;
        for (int i = 0; i < 6; i++)
        {
            float x = -0.42f + i * 0.168f;
            float extra = Next(ref rng) * 0.5f;
            mesh.Box(new Vector3(x, 2.6f + extra * 0.5f, 0.0f),
                     new Vector3(0.15f, extra, 0.34f), ConcreteDark);
        }

        return mesh.Build();
    }

    private static ArrayMesh Dumpster()
    {
        var mesh = new MeshBuilder();

        mesh.Box(new Vector3(0.0f, 0.75f, 0.0f), new Vector3(1.0f, 1.2f, 0.7f), PaintGreen);
        mesh.Box(new Vector3(0.0f, 1.42f, 0.0f), new Vector3(1.04f, 0.16f, 0.76f), PaintGreen.Lerp(Tar, 0.35f));
        mesh.Box(new Vector3(0.0f, 1.3f, 0.0f), new Vector3(1.06f, 0.08f, 0.74f), Steel);

        foreach (int sx in new[] { -1, 1 })
        {
            mesh.Box(new Vector3(sx * 0.42f, 0.12f, 0.0f), new Vector3(0.12f, 0.24f, 0.72f), Tar);
            mesh.Box(new Vector3(sx * 0.52f, 0.9f, 0.0f), new Vector3(0.06f, 0.5f, 0.5f), Steel);
        }

        return mesh.Build();
    }

    /// Visible from anywhere in the arena. With a fixed orthographic camera on a
    /// flat plane and repeating cover, a player crossing fifty metres has nothing
    /// telling them they moved — these are the only parallax the arena has.
    private static ArrayMesh WaterTower()
    {
        var mesh = new MeshBuilder();

        foreach (int sx in new[] { -1, 1 })
        {
            foreach (int sz in new[] { -1, 1 })
            {
                mesh.Box(new Vector3(sx * 0.3f, 4.5f, sz * 0.3f), new Vector3(0.14f, 9.0f, 0.14f), Steel);
                mesh.Box(new Vector3(sx * 0.22f, 4.6f, 0.0f), new Vector3(0.08f, 0.08f, 0.9f), Steel);
            }
        }

        mesh.Box(new Vector3(0.0f, 11.0f, 0.0f), new Vector3(1.0f, 3.8f, 1.0f), Rust);
        mesh.Box(new Vector3(0.0f, 9.0f, 0.0f), new Vector3(1.1f, 0.3f, 1.1f), Steel);
        mesh.Box(new Vector3(0.0f, 13.3f, 0.0f), new Vector3(0.7f, 1.4f, 0.7f), Steel);
        return mesh.Build();
    }

    private static ArrayMesh Billboard()
    {
        var mesh = new MeshBuilder();

        foreach (int sx in new[] { -1, 1 })
            mesh.Box(new Vector3(sx * 0.35f, 3.5f, 0.0f), new Vector3(0.16f, 7.0f, 0.16f), Steel);

        mesh.Box(new Vector3(0.0f, 8.6f, 0.0f), new Vector3(1.0f, 4.4f, 0.16f), Board);
        mesh.Box(new Vector3(0.0f, 8.6f, -0.1f), new Vector3(1.04f, 4.6f, 0.08f), Steel);

        // Peeling paper, which is what dates the board and therefore the world.
        mesh.Box(new Vector3(-0.15f, 9.4f, 0.09f), new Vector3(0.55f, 2.0f, 0.02f), PaintRed);
        mesh.Box(new Vector3(0.28f, 7.7f, 0.09f), new Vector3(0.3f, 1.2f, 0.02f), PaintBlue);
        return mesh.Build();
    }

    /// The city's set, in role order.
    public static readonly PropKind[] CitySet =
    {
        PropKind.Hoarding,
        PropKind.BusShell,
        PropKind.CarWreck,
        PropKind.TrafficBarrier,
        PropKind.Kiosk,
        PropKind.TowerBlock,
        PropKind.Overpass,
    };

    /// The laboratory's set, in role order.
    public static readonly PropKind[] LabSet =
    {
        PropKind.Partition,
        PropKind.ServerRack,
        PropKind.CeilingFall,
        PropKind.LabBench,
        PropKind.SpecimenTank,
        PropKind.VentStack,
        PropKind.Gantry,
    };

    // --- The city ----------------------------------------------------------

    /// Plywood site hoarding. The city's answer to a concrete wall.
    private static ArrayMesh Hoarding()
    {
        var mesh = new MeshBuilder();
        ulong rng = 0x9E3779B97F4A7C15UL;

        // Panels rather than one face, with the gaps between them left open at
        // the top. A hoarding is a fence pretending to be a wall, and the seams
        // are the only thing that says so at this distance.
        for (int i = 0; i < 5; i++)
        {
            float x = -0.4f + i * 0.2f;
            float top = 2.3f + Next(ref rng) * 0.3f;
            mesh.Box(new Vector3(x, top * 0.5f, 0.0f), new Vector3(0.185f, top, 0.1f),
                     Board.Lerp(ConcreteDark, 0.25f + Next(ref rng) * 0.3f));
        }

        // Posts behind, and a rail across them. Without the rail the panels read
        // as five separate boards standing up on their own.
        foreach (int sx in new[] { -1, 0, 1 })
            mesh.Box(new Vector3(sx * 0.42f, 1.3f, -0.09f), new Vector3(0.09f, 2.6f, 0.09f), Steel);

        mesh.Box(new Vector3(0.0f, 2.05f, -0.09f), new Vector3(1.0f, 0.08f, 0.07f), Steel);
        mesh.Box(new Vector3(0.0f, 0.55f, -0.09f), new Vector3(1.0f, 0.08f, 0.07f), Steel);

        // Fly-posting, peeling. Paper is what dates a street.
        mesh.Box(new Vector3(-0.22f, 1.55f, 0.055f), new Vector3(0.3f, 0.8f, 0.02f), PaintRed);
        mesh.Box(new Vector3(0.18f, 1.2f, 0.055f), new Vector3(0.22f, 0.55f, 0.02f), PaintBlue);
        mesh.Box(new Vector3(0.36f, 1.7f, 0.055f), new Vector3(0.16f, 0.4f, 0.02f), Chalk);

        // A kicked-in panel at the bottom, which is the detail that makes it
        // abandoned rather than merely closed.
        mesh.Box(new Vector3(0.02f, 0.28f, 0.02f), new Vector3(0.24f, 0.5f, 0.05f), Tar);
        return mesh.Build();
    }

    /// A gutted bus. Fills the container's role: three metres of hard cover you
    /// go around rather than over.
    private static ArrayMesh BusShell()
    {
        var mesh = new MeshBuilder();

        // Skirt, body, window band, roof — four bands up, which is the whole
        // reason a bus reads as a bus at a hundred pixels. A single box in the
        // same colour is a shipping container lying down.
        mesh.Box(new Vector3(0.0f, 0.42f, 0.0f), new Vector3(1.0f, 0.56f, 0.8f), PaintYellow.Lerp(Tar, 0.45f));
        mesh.Box(new Vector3(0.0f, 1.08f, 0.0f), new Vector3(1.0f, 0.78f, 0.86f), PaintYellow);
        mesh.Box(new Vector3(0.0f, 1.82f, 0.0f), new Vector3(1.02f, 0.7f, 0.88f), Glass);
        mesh.Box(new Vector3(0.0f, 2.32f, 0.0f), new Vector3(0.98f, 0.34f, 0.84f), PaintYellow.Lerp(Rust, 0.3f));

        // Pillars breaking the window band into windows. Six is enough; the eye
        // counts "several" and stops.
        for (int i = 0; i < 6; i++)
        {
            float x = -0.42f + i * 0.168f;
            mesh.Box(new Vector3(x, 1.82f, 0.0f), new Vector3(0.05f, 0.72f, 0.9f), PaintYellow);
        }

        // Wheels, sunk so only the arch shows. A bus on visible round wheels is a
        // toy; a bus with dark gaps under it is parked.
        foreach (int sx in new[] { -1, 1 })
        {
            foreach (int sz in new[] { -1, 1 })
                mesh.Box(new Vector3(sx * 0.33f, 0.16f, sz * 0.38f), new Vector3(0.15f, 0.32f, 0.12f), Tar);
        }

        // Roof hatches and a destination blind, so the silhouette is not a slab.
        mesh.Box(new Vector3(-0.2f, 2.54f, 0.0f), new Vector3(0.2f, 0.1f, 0.4f), Chalk.Lerp(Tar, 0.4f));
        mesh.Box(new Vector3(0.24f, 2.54f, 0.0f), new Vector3(0.2f, 0.1f, 0.4f), Chalk.Lerp(Tar, 0.4f));
        mesh.Box(new Vector3(0.0f, 2.62f, 0.0f), new Vector3(0.6f, 0.16f, 0.2f), Steel);
        return mesh.Build();
    }

    /// A pile-up. Stands in for rubble, and fills the same footprint.
    ///
    /// **A prop has to fill its block.** The collider is the layout's footprint,
    /// not the mesh's bounds, so anything that leaves a corner empty gives the
    /// player an invisible wall to walk into — which is why this is three cars
    /// rather than the one flattened one it started as, and why the kiosk is a
    /// stall with its stock around it rather than a narrow booth.
    private static ArrayMesh CarWreck()
    {
        var mesh = new MeshBuilder();
        ulong rng = 0xD1342543DE82EF95UL;

        // Two on the ground, nose to tail across the long axis, and one dropped
        // across them. The one on top is what makes it a wreck rather than a car
        // park, so it sits at a real angle and overhangs.
        Car(mesh, new Vector3(-0.26f, 0.0f, -0.04f), 6.0f, PaintBlue.Lerp(Rust, 0.5f));
        Car(mesh, new Vector3(0.27f, 0.0f, 0.06f), -9.0f, Chalk.Lerp(Rust, 0.55f));
        Car(mesh, new Vector3(0.02f, 0.86f, -0.02f), 34.0f, PaintRed.Lerp(Rust, 0.35f));

        // A door and a bonnet thrown clear, filling the corners the cars miss.
        mesh.Box(new Vector3(-0.42f, 0.3f, 0.34f), new Vector3(0.1f, 0.6f, 0.34f), Steel, 26.0f);
        mesh.Box(new Vector3(0.40f, 0.08f, -0.38f), new Vector3(0.32f, 0.08f, 0.26f), PaintBlue.Lerp(Tar, 0.5f), 42.0f);

        // Glass and grit underfoot, which is the only thing that ties three
        // separate objects into one heap.
        for (int i = 0; i < 6; i++)
        {
            float x = (Next(ref rng) - 0.5f) * 0.94f;
            float z = (Next(ref rng) - 0.5f) * 0.94f;
            mesh.Box(new Vector3(x, 0.03f, z), new Vector3(0.16f, 0.06f, 0.14f),
                     Tar.Lerp(Glass, Next(ref rng)), Next(ref rng) * 90.0f);
        }

        return mesh.Build();

        static void Car(MeshBuilder mesh, Vector3 at, float yaw, Color paint)
        {
            // Half a unit long and a third wide, so two sit side by side in the
            // footprint with the third across them and the whole thing reaches
            // the 1.9 m the Heap role owns.
            mesh.Box(at + new Vector3(0.0f, 0.24f, 0.0f), new Vector3(0.52f, 0.34f, 0.42f), paint, yaw);
            mesh.Box(at + new Vector3(-0.04f, 0.55f, 0.0f), new Vector3(0.3f, 0.3f, 0.38f), Glass, yaw);
            mesh.Box(at + new Vector3(-0.04f, 0.68f, 0.0f), new Vector3(0.28f, 0.12f, 0.34f), paint, yaw);

            // One crumpled end. A car is recognisable and a bent car is the point.
            mesh.Box(at + new Vector3(0.22f, 0.28f, 0.02f), new Vector3(0.16f, 0.24f, 0.36f),
                     paint.Lerp(Tar, 0.4f), yaw + 17.0f);

            foreach (int sx in new[] { -1, 1 })
            {
                foreach (int sz in new[] { -1, 1 })
                {
                    mesh.Box(at + new Vector3(sx * 0.18f, 0.1f, sz * 0.2f),
                             new Vector3(0.1f, 0.2f, 0.08f), Tar, yaw);
                }
            }
        }
    }

    /// Water-filled plastic barriers with a downed signal across them.
    private static ArrayMesh TrafficBarrier()
    {
        var mesh = new MeshBuilder();

        // Two interlocking segments rather than one, because the join is the
        // silhouette: a single moulded block is a jersey barrier in orange.
        foreach (int sx in new[] { -1, 1 })
        {
            float x = sx * 0.25f;
            mesh.Box(new Vector3(x, 0.12f, 0.0f), new Vector3(0.5f, 0.24f, 0.44f), PaintOrange.Lerp(Tar, 0.3f));
            mesh.Box(new Vector3(x, 0.5f, 0.0f), new Vector3(0.46f, 0.54f, 0.3f), PaintOrange);
            mesh.Box(new Vector3(x, 0.83f, 0.0f), new Vector3(0.5f, 0.14f, 0.34f), Chalk);
        }

        // The interlock pin, which is a small thing and the one that sells it.
        mesh.Box(new Vector3(0.0f, 0.52f, 0.0f), new Vector3(0.06f, 0.8f, 0.24f), PaintOrange.Lerp(Chalk, 0.3f));

        // A traffic signal down across the top. Nothing else in the set says
        // "this used to be a junction".
        mesh.Box(new Vector3(0.05f, 0.98f, 0.05f), new Vector3(0.9f, 0.07f, 0.07f), Steel, 12.0f);
        mesh.Box(new Vector3(0.4f, 1.05f, 0.14f), new Vector3(0.14f, 0.32f, 0.12f), Tar, 12.0f);
        mesh.Box(new Vector3(0.4f, 1.14f, 0.2f), new Vector3(0.07f, 0.07f, 0.02f), PaintRed, 12.0f);
        return mesh.Build();
    }

    /// A shuttered street stall with its stock still stacked beside it.
    ///
    /// The first version was a narrow booth with a canopy, and at the width the
    /// arena draws cover it came out as a stack of horizontal trays — four bands
    /// of different colours, none of them tall enough to dominate. A prop is
    /// scaled in X and Z and left alone in Y, so anything authored with strong
    /// horizontal banding reads as a cake when it arrives three metres wide.
    /// The fix is a shape with one dominant vertical mass and the rest low.
    private static ArrayMesh Kiosk()
    {
        var mesh = new MeshBuilder();

        // The booth: two thirds of the footprint, and nearly all of the height.
        mesh.Box(new Vector3(-0.16f, 1.16f, 0.0f), new Vector3(0.66f, 2.32f, 0.82f), PaintGreen.Lerp(Tar, 0.35f));

        // Shutter, on the lower half of the front only, with a frame around it.
        // Slats across the whole face were most of why it banded.
        for (int i = 0; i < 5; i++)
        {
            float y = 0.32f + i * 0.17f;
            mesh.Box(new Vector3(-0.16f, y, 0.42f), new Vector3(0.5f, 0.13f, 0.04f), Steel.Lerp(Rust, 0.4f));
        }

        mesh.Box(new Vector3(-0.16f, 1.66f, 0.43f), new Vector3(0.56f, 0.72f, 0.05f), Glass);
        mesh.Box(new Vector3(-0.16f, 2.05f, 0.45f), new Vector3(0.6f, 0.1f, 0.06f), Board);

        // A hand-painted sign board up the side, vertical, which is the one
        // element that says "shop" from across a street.
        mesh.Box(new Vector3(-0.5f, 1.5f, 0.2f), new Vector3(0.06f, 1.5f, 0.3f), PaintRed.Lerp(Board, 0.35f));

        // The rest of the footprint: crates and a fallen awning frame. Low, so
        // they do not compete with the booth, and present, so the block is full.
        mesh.Box(new Vector3(0.34f, 0.3f, -0.24f), new Vector3(0.28f, 0.6f, 0.4f), Board.Lerp(Tar, 0.35f), 8.0f);
        mesh.Box(new Vector3(0.36f, 0.72f, -0.2f), new Vector3(0.24f, 0.28f, 0.32f), Board.Lerp(Rust, 0.3f), 22.0f);
        mesh.Box(new Vector3(0.3f, 0.22f, 0.28f), new Vector3(0.34f, 0.44f, 0.36f), PaintBlue.Lerp(Tar, 0.45f), 34.0f);
        mesh.Box(new Vector3(0.26f, 0.62f, 0.3f), new Vector3(0.5f, 0.05f, 0.05f), Steel, 12.0f);
        return mesh.Build();
    }

    /// A leaning tower block. Twenty-two metres, and the only thing in the city
    /// set tall enough to be a landmark.
    private static ArrayMesh TowerBlock()
    {
        var mesh = new MeshBuilder();
        ulong rng = 0xA24BAED4963EE407UL;

        // Two slabs slightly out of true with each other. A single upright box
        // twenty metres tall is a monolith; two leaning against the same axis is
        // a building that has moved.
        mesh.Box(new Vector3(0.0f, 8.0f, 0.0f), new Vector3(1.0f, 16.0f, 0.72f), Concrete.Lerp(Tar, 0.35f));
        mesh.Box(new Vector3(0.12f, 18.0f, 0.04f), new Vector3(0.88f, 5.0f, 0.66f), Concrete.Lerp(Tar, 0.45f), 4.0f);

        // Window grid, dark. Every third one is boarded, which is what stops the
        // grid from reading as a texture swatch.
        for (int row = 0; row < 11; row++)
        {
            for (int col = 0; col < 5; col++)
            {
                float y = 1.6f + row * 1.7f;
                float x = -0.36f + col * 0.18f;
                bool boarded = Next(ref rng) < 0.28f;

                mesh.Box(new Vector3(x, y, 0.37f), new Vector3(0.12f, 0.9f, 0.03f),
                         boarded ? Board.Lerp(Tar, 0.45f) : Glass);
            }
        }

        // A collapsed corner near the top, and the floor slabs it exposed.
        mesh.Box(new Vector3(-0.42f, 19.4f, 0.0f), new Vector3(0.3f, 0.28f, 0.7f), ConcreteDark, 8.0f);
        mesh.Box(new Vector3(-0.38f, 20.6f, 0.06f), new Vector3(0.34f, 0.24f, 0.6f), ConcreteDark, 14.0f);
        mesh.Box(new Vector3(-0.5f, 20.0f, -0.1f), new Vector3(0.05f, 1.6f, 0.05f), Rust, 20.0f);

        // Brick at the base where the cladding has gone.
        mesh.Box(new Vector3(0.0f, 0.9f, 0.38f), new Vector3(0.9f, 1.8f, 0.04f), Brick);
        return mesh.Build();
    }

    /// A broken section of elevated road on its columns.
    private static ArrayMesh Overpass()
    {
        var mesh = new MeshBuilder();

        // Columns and pier caps. The taper is what makes it civil engineering
        // rather than two posts.
        foreach (int sx in new[] { -1, 1 })
        {
            mesh.Box(new Vector3(sx * 0.28f, 3.4f, 0.0f), new Vector3(0.22f, 6.8f, 0.24f), Concrete.Lerp(Tar, 0.3f));
            mesh.Box(new Vector3(sx * 0.28f, 6.95f, 0.0f), new Vector3(0.34f, 0.4f, 0.36f), ConcreteDark);
            mesh.Box(new Vector3(sx * 0.28f, 0.2f, 0.0f), new Vector3(0.36f, 0.4f, 0.38f), ConcreteDark);
        }

        // Deck, kerbs, and a parapet along one side only — the other side is
        // where it broke.
        mesh.Box(new Vector3(0.0f, 7.45f, 0.0f), new Vector3(1.3f, 0.5f, 0.9f), Concrete);
        mesh.Box(new Vector3(0.0f, 7.72f, 0.0f), new Vector3(1.3f, 0.06f, 0.86f), Tar);
        mesh.Box(new Vector3(0.0f, 8.1f, -0.42f), new Vector3(1.3f, 0.8f, 0.12f), ConcreteDark);

        // The break: the deck stops short and the rebar does not.
        mesh.Box(new Vector3(0.58f, 7.3f, 0.1f), new Vector3(0.3f, 0.4f, 0.5f), ConcreteDark, 18.0f);
        for (int i = 0; i < 5; i++)
        {
            float z = -0.3f + i * 0.16f;
            mesh.Box(new Vector3(0.74f + i * 0.02f, 7.5f, z), new Vector3(0.34f, 0.04f, 0.04f), Rust, i * 7.0f);
        }

        // Lamp standards, which is what gives the deck a top edge against the sky.
        foreach (int sx in new[] { -1, 1 })
        {
            mesh.Box(new Vector3(sx * 0.34f, 8.6f, -0.42f), new Vector3(0.06f, 1.6f, 0.06f), Steel);
            mesh.Box(new Vector3(sx * 0.34f, 9.35f, -0.34f), new Vector3(0.08f, 0.08f, 0.24f), Steel);
        }

        return mesh.Build();
    }

    // --- The laboratory ----------------------------------------------------

    /// A full-height clean-room partition, part of it gone.
    ///
    /// Seven and a half metres, which is nearly to the ceiling and looks absurd
    /// written down next to a 1.8 m survivor. It is the single change that made
    /// Cold Storage read as an interior.
    ///
    /// The obvious answer was a ceiling, and a ceiling does not work in this
    /// camera: the eye sits 5.7 m up tilted 26 degrees down, so it never looks
    /// more than about four degrees above horizontal, and a roof at eight metres
    /// is only in shot past forty metres — well beyond where the fog has gone
    /// black. The roof is built, it does occlude the sky, and it is
    /// indistinguishable from the sky it occludes.
    ///
    /// What the camera *can* see is whatever stands between the player and the
    /// fog. A three-metre screen in a room with an eight-metre ceiling is a
    /// cubicle in a field; a wall that runs floor to ceiling breaks the horizon
    /// line and closes the arena down to the room you are standing in. Height is
    /// free here — shots and sight lines resolve in two dimensions.
    private static ArrayMesh Partition()
    {
        var mesh = new MeshBuilder();
        ulong rng = 0x3C79AC492BA7B653UL;

        const float Top = 7.6f;

        // Floor and ceiling channel. A partition is a thing that was installed,
        // and the trim at both ends is the only part that says so.
        mesh.Box(new Vector3(0.0f, 0.07f, 0.0f), new Vector3(1.0f, 0.14f, 0.3f), PanelTrim);
        mesh.Box(new Vector3(0.0f, Top - 0.1f, 0.0f), new Vector3(1.0f, 0.24f, 0.34f), PanelTrim);

        // Panels in four columns, each broken into three courses with a joint
        // between them. Vertical division alone at this height reads as a fence;
        // the horizontal courses are what make it a built wall.
        for (int col = 0; col < 4; col++)
        {
            float x = -0.375f + col * 0.25f;

            for (int course = 0; course < 3; course++)
            {
                float low = 0.14f + course * 2.44f;
                float tall = course == 1 ? 1.7f : 2.3f;

                mesh.Box(new Vector3(x, low + tall * 0.5f, 0.0f),
                         new Vector3(0.235f, tall, 0.22f),
                         Panel.Lerp(PanelTrim, 0.06f + Next(ref rng) * 0.12f));
            }

            // The stile between courses.
            mesh.Box(new Vector3(x, 2.6f, 0.0f), new Vector3(0.24f, 0.16f, 0.24f), PanelTrim);
            mesh.Box(new Vector3(x, 5.05f, 0.0f), new Vector3(0.24f, 0.16f, 0.24f), PanelTrim);
        }

        // The glazed strip, at head height where a person would look through it.
        mesh.Box(new Vector3(0.0f, 3.35f, 0.0f), new Vector3(1.0f, 1.4f, 0.16f), Glass);
        mesh.Box(new Vector3(0.0f, 2.62f, 0.0f), new Vector3(1.0f, 0.1f, 0.24f), PanelTrim);
        mesh.Box(new Vector3(0.0f, 4.08f, 0.0f), new Vector3(1.0f, 0.1f, 0.24f), PanelTrim);

        // Mullions across the glass, so it is a run of windows rather than one
        // long slot.
        for (int i = 0; i < 3; i++)
            mesh.Box(new Vector3(-0.25f + i * 0.25f, 3.35f, 0.0f), new Vector3(0.05f, 1.44f, 0.2f), PanelTrim);

        // A panel torn out near the top and hanging, with the studwork behind it
        // showing. Without this it is a wall in a building that still works.
        mesh.Box(new Vector3(0.3f, 6.2f, 0.15f), new Vector3(0.22f, 1.5f, 0.05f), Panel.Lerp(Tar, 0.4f), 18.0f);
        foreach (int i in new[] { 0, 1 })
            mesh.Box(new Vector3(0.24f + i * 0.14f, 6.3f, 0.0f), new Vector3(0.04f, 2.6f, 0.12f), Steel);

        // Cable tray running along the top, under the channel. Every long wall in
        // a building this size has one, and it gives the top edge a profile
        // instead of a line.
        mesh.Box(new Vector3(0.0f, Top - 0.55f, 0.14f), new Vector3(1.0f, 0.16f, 0.18f), Steel.Lerp(PanelTrim, 0.4f));
        for (int i = 0; i < 5; i++)
            mesh.Box(new Vector3(-0.4f + i * 0.2f, Top - 0.42f, 0.14f), new Vector3(0.06f, 0.1f, 0.2f), Cable);

        // A door at the base of one bay, which is the only element that gives the
        // wall a human scale from across the room.
        mesh.Box(new Vector3(-0.34f, 1.05f, 0.13f), new Vector3(0.34f, 2.1f, 0.05f), Panel.Lerp(PanelTrim, 0.35f));
        mesh.Box(new Vector3(-0.34f, 2.14f, 0.13f), new Vector3(0.38f, 0.09f, 0.07f), PanelTrim);
        mesh.Box(new Vector3(-0.2f, 1.0f, 0.17f), new Vector3(0.05f, 0.05f, 0.05f), Steel);

        // Hazard stripe along the base, the one warm colour in the set.
        mesh.Box(new Vector3(0.0f, 0.2f, 0.16f), new Vector3(1.0f, 0.1f, 0.02f), Amber);
        return mesh.Build();
    }

    /// A server rack, doors off. The lab's hard cover.
    private static ArrayMesh ServerRack()
    {
        var mesh = new MeshBuilder();
        ulong rng = 0x76E15D3EFEFDCBBFUL;

        // Frame first: four uprights and a plinth. Everything else hangs in it.
        foreach (int sx in new[] { -1, 1 })
        {
            foreach (int sz in new[] { -1, 1 })
                mesh.Box(new Vector3(sx * 0.46f, 1.45f, sz * 0.4f), new Vector3(0.08f, 2.9f, 0.08f), PanelTrim);
        }

        mesh.Box(new Vector3(0.0f, 0.06f, 0.0f), new Vector3(1.0f, 0.12f, 0.86f), Tar);
        mesh.Box(new Vector3(0.0f, 1.5f, -0.42f), new Vector3(0.94f, 2.7f, 0.08f), PanelTrim);

        // Blades. Thin horizontal slots are the whole silhouette of a rack, and
        // the gaps where two have been pulled are what makes it looted.
        for (int i = 0; i < 14; i++)
        {
            float y = 0.24f + i * 0.185f;
            if (Next(ref rng) < 0.18f)
                continue;

            mesh.Box(new Vector3(0.0f, y, 0.0f), new Vector3(0.86f, 0.14f, 0.76f), Steel.Lerp(Tar, 0.3f));
            mesh.Box(new Vector3(0.0f, y, 0.39f), new Vector3(0.8f, 0.08f, 0.03f), Glass);
        }

        // Cable loom spilling out of the top and down the back. A rack without
        // cable is a filing cabinet.
        mesh.Box(new Vector3(0.0f, 2.94f, -0.1f), new Vector3(0.9f, 0.14f, 0.6f), PanelTrim);
        for (int i = 0; i < 5; i++)
        {
            float x = -0.3f + i * 0.15f;
            mesh.Box(new Vector3(x, 2.4f, -0.5f), new Vector3(0.05f, 1.2f, 0.05f), Cable, i * 9.0f);
        }

        mesh.Box(new Vector3(0.2f, 3.02f, -0.3f), new Vector3(0.5f, 0.1f, 0.24f), Cable, 14.0f);
        return mesh.Build();
    }

    /// The ceiling, on the floor. The lab's rubble.
    private static ArrayMesh CeilingFall()
    {
        var mesh = new MeshBuilder();
        ulong rng = 0x2545F4914F6CDD1DUL;

        // Tiles, dropped flat and at angles. Flat panels rather than the yard's
        // tumbled slabs — a suspended ceiling comes down in sheets.
        for (int i = 0; i < 10; i++)
        {
            float x = (Next(ref rng) - 0.5f) * 0.9f;
            float z = (Next(ref rng) - 0.5f) * 0.9f;
            float y = 0.05f + Next(ref rng) * 0.9f;

            mesh.Box(new Vector3(x, y, z),
                     new Vector3(0.3f + Next(ref rng) * 0.24f, 0.06f, 0.3f + Next(ref rng) * 0.24f),
                     Panel.Lerp(ConcreteDark, 0.2f + Next(ref rng) * 0.5f),
                     Next(ref rng) * 90.0f);
        }

        // A length of rectangular duct across the heap, which is the piece that
        // gives it height and stops it reading as a pile of paper.
        mesh.Box(new Vector3(-0.05f, 1.05f, 0.06f), new Vector3(0.9f, 0.42f, 0.42f), Steel.Lerp(Panel, 0.4f), 12.0f);
        mesh.Box(new Vector3(0.3f, 1.28f, 0.1f), new Vector3(0.3f, 0.36f, 0.38f), Steel.Lerp(Tar, 0.3f), 34.0f);

        // The grid it hung from, bent, and the cable still holding some of it up.
        for (int i = 0; i < 4; i++)
        {
            float x = -0.34f + i * 0.23f;
            mesh.Box(new Vector3(x, 0.7f, -0.3f + Next(ref rng) * 0.6f),
                     new Vector3(0.6f, 0.04f, 0.04f), PanelTrim, 20.0f + i * 25.0f);
        }

        mesh.Box(new Vector3(-0.3f, 1.4f, -0.24f), new Vector3(0.04f, 0.9f, 0.04f), Cable, 8.0f);
        mesh.Box(new Vector3(-0.32f, 1.82f, -0.22f), new Vector3(0.36f, 0.1f, 0.14f), Glass);
        return mesh.Build();
    }

    /// A run of laboratory bench, cleared out. The lab's waist-high cover.
    private static ArrayMesh LabBench()
    {
        var mesh = new MeshBuilder();

        // Worktop and the cabinet run under it. Stainless over painted carcass is
        // the entire read, and the toe recess at the bottom is what makes it
        // furniture rather than a block.
        mesh.Box(new Vector3(0.0f, 0.45f, 0.0f), new Vector3(0.96f, 0.66f, 0.56f), Panel.Lerp(PanelTrim, 0.35f));
        mesh.Box(new Vector3(0.0f, 0.09f, 0.0f), new Vector3(0.9f, 0.18f, 0.44f), Tar);
        mesh.Box(new Vector3(0.0f, 0.83f, 0.0f), new Vector3(1.0f, 0.1f, 0.64f), Steel.Lerp(Panel, 0.5f));

        // Doors and handles.
        for (int i = 0; i < 4; i++)
        {
            float x = -0.36f + i * 0.24f;
            mesh.Box(new Vector3(x, 0.47f, 0.29f), new Vector3(0.22f, 0.6f, 0.02f), Panel);
            mesh.Box(new Vector3(x, 0.72f, 0.31f), new Vector3(0.14f, 0.03f, 0.02f), Steel);
        }

        // A shelf on uprights above, half its contents still on it. The upright
        // is what takes this past the jersey barrier it stands in for — the same
        // height, a completely different outline.
        foreach (int sx in new[] { -1, 1 })
            mesh.Box(new Vector3(sx * 0.42f, 1.0f, -0.2f), new Vector3(0.05f, 0.34f, 0.05f), Steel);

        mesh.Box(new Vector3(0.0f, 1.13f, -0.2f), new Vector3(0.94f, 0.05f, 0.26f), Steel.Lerp(Panel, 0.5f));
        mesh.Box(new Vector3(-0.22f, 1.2f, -0.2f), new Vector3(0.2f, 0.12f, 0.16f), Glass);
        mesh.Box(new Vector3(0.14f, 1.22f, -0.18f), new Vector3(0.14f, 0.16f, 0.14f), Fluid);

        // A tray gone over, and its contents across the top.
        mesh.Box(new Vector3(0.3f, 0.92f, 0.1f), new Vector3(0.28f, 0.06f, 0.2f), Steel, 26.0f);
        mesh.Box(new Vector3(0.12f, 0.9f, 0.18f), new Vector3(0.08f, 0.08f, 0.08f), Glass, 40.0f);
        return mesh.Build();
    }

    /// A specimen tank, still full. The lab's oddity.
    ///
    /// Not emissive, and it should be. Emission is a property of the material and
    /// every prop shares one, so a glowing tank would cost the set a second draw
    /// call in both the main and the shadow pass — and in the arena's present
    /// lighting a glow would be invisible anyway. It is written down as E3's job
    /// because that is where the lab stops being lit by a sun it cannot see.
    private static ArrayMesh SpecimenTank()
    {
        var mesh = new MeshBuilder();

        // Plinth, cylinder, cap. Round is the point: it is the only curved thing
        // in any of the three sets, which is most of why it reads as equipment.
        mesh.Box(new Vector3(0.0f, 0.11f, 0.0f), new Vector3(0.9f, 0.22f, 0.9f), Tar);
        mesh.Box(new Vector3(0.0f, 0.3f, 0.0f), new Vector3(0.72f, 0.2f, 0.72f), PanelTrim);

        mesh.Tube(new Vector3(0.0f, 0.4f, 0.0f), new Vector3(0.0f, 2.0f, 0.0f), 0.34f, Fluid, 12);
        mesh.Tube(new Vector3(0.0f, 0.4f, 0.0f), new Vector3(0.0f, 0.52f, 0.0f), 0.38f, Steel, 12);
        mesh.Tube(new Vector3(0.0f, 1.88f, 0.0f), new Vector3(0.0f, 2.04f, 0.0f), 0.38f, Steel, 12);
        mesh.Tube(new Vector3(0.0f, 2.02f, 0.0f), new Vector3(0.0f, 2.3f, 0.0f), 0.24f, PanelTrim, 10);

        // Something in it. A tank of clear fluid is a water heater.
        mesh.Ball(new Vector3(0.0f, 1.1f, 0.0f), 0.17f, Fluid.Lerp(Tar, 0.55f), 8, 5);
        mesh.Box(new Vector3(0.0f, 0.85f, 0.0f), new Vector3(0.1f, 0.5f, 0.1f), Fluid.Lerp(Tar, 0.6f), 15.0f);

        // Frame, pipework and a control box, filling the corners the cylinder
        // leaves empty — the collider is the whole block either way.
        foreach (int sx in new[] { -1, 1 })
        {
            foreach (int sz in new[] { -1, 1 })
                mesh.Box(new Vector3(sx * 0.42f, 1.2f, sz * 0.42f), new Vector3(0.07f, 2.4f, 0.07f), Steel);
        }

        mesh.Box(new Vector3(0.0f, 2.36f, 0.0f), new Vector3(0.94f, 0.1f, 0.94f), PanelTrim);
        mesh.Box(new Vector3(0.44f, 1.1f, 0.0f), new Vector3(0.14f, 0.44f, 0.3f), Panel);
        mesh.Box(new Vector3(0.51f, 1.14f, 0.0f), new Vector3(0.03f, 0.16f, 0.18f), Amber);
        mesh.Box(new Vector3(-0.4f, 0.6f, 0.3f), new Vector3(0.06f, 1.0f, 0.06f), Cable, 10.0f);
        return mesh.Build();
    }

    /// An exhaust stack. The lab's tall landmark, and the only thing about the
    /// place visible from outside it.
    private static ArrayMesh VentStack()
    {
        var mesh = new MeshBuilder();

        mesh.Box(new Vector3(0.0f, 0.4f, 0.0f), new Vector3(1.0f, 0.8f, 1.0f), ConcreteDark);
        mesh.Tube(new Vector3(0.0f, 0.7f, 0.0f), new Vector3(0.0f, 14.6f, 0.0f), 0.3f, Steel.Lerp(Panel, 0.35f), 10);

        // Bands up the shaft. A plain cylinder fifteen metres tall has no scale
        // at all; the bands are what tell the eye how far away it is.
        for (int i = 0; i < 7; i++)
        {
            float y = 1.6f + i * 1.9f;
            mesh.Tube(new Vector3(0.0f, y, 0.0f), new Vector3(0.0f, y + 0.22f, 0.0f), 0.35f,
                      i % 2 == 0 ? PanelTrim : Amber.Lerp(PanelTrim, 0.5f), 10);
        }

        // A caged ladder up one side, and the cowl at the top.
        for (int i = 0; i < 13; i++)
        {
            float y = 1.0f + i * 1.05f;
            mesh.Box(new Vector3(0.4f, y, 0.0f), new Vector3(0.1f, 0.05f, 0.24f), Steel);
        }

        mesh.Box(new Vector3(0.45f, 7.5f, 0.0f), new Vector3(0.04f, 13.0f, 0.04f), Steel);
        mesh.Tube(new Vector3(0.0f, 14.4f, 0.0f), new Vector3(0.0f, 15.4f, 0.0f), 0.42f, PanelTrim, 10);
        mesh.Tube(new Vector3(0.0f, 15.3f, 0.0f), new Vector3(0.0f, 15.9f, 0.0f), 0.2f, Tar, 8);

        // Guy wires down to the plinth, which is what stops it looking planted in
        // the ground like a post.
        foreach (int sx in new[] { -1, 1 })
            mesh.Box(new Vector3(sx * 0.26f, 4.0f, 0.0f), new Vector3(0.03f, 7.0f, 0.03f), Cable, 0.0f);

        return mesh.Build();
    }

    /// An overhead gantry crane. The lab's second landmark.
    private static ArrayMesh Gantry()
    {
        var mesh = new MeshBuilder();

        // Two A-frames and the beam between them. The taper of the legs is what
        // makes it a crane and not a doorway.
        foreach (int sz in new[] { -1, 1 })
        {
            foreach (int sx in new[] { -1, 1 })
            {
                mesh.Box(new Vector3(sx * 0.44f, 3.9f, sz * 0.34f), new Vector3(0.14f, 7.8f, 0.14f), Steel);
                mesh.Box(new Vector3(sx * 0.44f, 0.2f, sz * 0.34f), new Vector3(0.28f, 0.4f, 0.28f), ConcreteDark);
            }

            // Cross-bracing. Diagonals are the whole visual language of a gantry.
            mesh.Box(new Vector3(0.0f, 2.4f, sz * 0.34f), new Vector3(0.94f, 0.08f, 0.1f), Steel, 0.0f);
            mesh.Box(new Vector3(0.0f, 5.4f, sz * 0.34f), new Vector3(0.94f, 0.08f, 0.1f), Steel, 0.0f);
        }

        mesh.Box(new Vector3(0.0f, 8.1f, 0.0f), new Vector3(1.2f, 0.5f, 0.24f), PanelTrim);
        mesh.Box(new Vector3(0.0f, 8.5f, 0.0f), new Vector3(1.24f, 0.16f, 0.5f), Steel);

        // Trolley and hook block, off centre and hanging. A crane parked dead
        // centre with its hook up reads as a diagram.
        mesh.Box(new Vector3(0.22f, 7.78f, 0.0f), new Vector3(0.3f, 0.3f, 0.34f), Amber.Lerp(PanelTrim, 0.4f));
        mesh.Box(new Vector3(0.22f, 6.5f, 0.0f), new Vector3(0.04f, 2.3f, 0.04f), Cable);
        mesh.Box(new Vector3(0.22f, 5.3f, 0.0f), new Vector3(0.18f, 0.34f, 0.18f), Steel, 18.0f);

        // A hazard chevron on the beam.
        for (int i = 0; i < 5; i++)
            mesh.Box(new Vector3(-0.4f + i * 0.2f, 8.1f, 0.13f), new Vector3(0.09f, 0.44f, 0.02f), Amber);

        return mesh.Build();
    }

    private static float Next(ref ulong state)
    {
        state ^= state << 13;
        state ^= state >> 7;
        state ^= state << 17;
        return (state >> 40) / 16777216.0f;
    }
}
