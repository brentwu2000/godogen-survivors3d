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
        _ => 3.0f,
    };

    /// Whether a kind is scenery rather than cover. Landmarks are placed by hand
    /// on the grid and are not drawn from the cover pool.
    public static bool IsLandmark(PropKind kind) =>
        kind is PropKind.WaterTower or PropKind.Billboard;

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

    private static float Next(ref ulong state)
    {
        state ^= state << 13;
        state ^= state >> 7;
        state ^= state << 17;
        return (state >> 40) / 16777216.0f;
    }
}
