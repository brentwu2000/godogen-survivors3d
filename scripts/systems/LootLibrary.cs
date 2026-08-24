using Godot;

/// What a crate is, so the arena stops being decorated with untextured cubes.
///
/// `LevelGenerator` gave every crate a `BoxMesh` with **no material at all** and
/// `RunDirector` did the same for the supply cache. That is the white cube in
/// every screenshot ever taken of this game — including the ones used to judge
/// the ground shader, the fog, the bodies and every biome. It survived that long
/// because no probe asks what a thing looks like, and because a cube in a
/// screenshot reads as "placeholder for something", which is a category the eye
/// skips over.
///
/// Two shapes, because the two are found in different ways and should not be
/// confused across an arena: a **crate** is scavenged, was already here, and is
/// what the layout scatters; a **cache** is dropped for you mid-run and lands
/// where the director says. The cache is bigger, has a chute harness and a
/// beacon panel, and is the one the player is supposed to run toward.
///
/// Built with `MeshBuilder` and drawn with vertex colour on one shared material,
/// the same as `PropLibrary` — so the whole loot set costs one material and no
/// textures, and a crate is the same kind of object as the cover around it.
public static class LootLibrary
{
    public enum Look
    {
        /// Scattered by the layout. Small, wooden, banded.
        Crate,

        /// Dropped by the director or by a boss. Bigger, harnessed, marked.
        Cache,
    }

    /// How many tiers of value a crate can advertise.
    public const int Tiers = 3;

    private static readonly Color Timber = new(0.42f, 0.33f, 0.22f);
    private static readonly Color TimberDark = new(0.29f, 0.22f, 0.15f);
    private static readonly Color Steel = new(0.30f, 0.32f, 0.34f);
    private static readonly Color Strap = new(0.20f, 0.18f, 0.16f);
    private static readonly Color Canvas = new(0.34f, 0.36f, 0.30f);
    private static readonly Color Shell = new(0.26f, 0.30f, 0.28f);

    /// What the stencil says, by tier.
    ///
    /// The rarity bias already rises with distance from the spawn — a far crate
    /// really is worth more — and until now the player had to take that on trust,
    /// because the far crate and the near one were the same white cube. This is
    /// that number, painted on the box, so the walk is a decision made with
    /// information rather than a rule learned from a wiki.
    ///
    /// Deliberately not a rainbow. Three steps, and the top one is the only warm
    /// colour on a loot container anywhere — so "that one is worth crossing the
    /// map for" is a thing you notice rather than a thing you decode.
    private static readonly Color[] TierMark =
    {
        new(0.44f, 0.45f, 0.43f),
        new(0.52f, 0.40f, 0.13f),
        new(0.55f, 0.20f, 0.15f),
    };

    /// Which tier a bias belongs to.
    ///
    /// The bias runs from 1.0 at the spawn to the biome's `DepthRarityBias` at
    /// the rim, which is between 1.3 and 3.0 depending on the place — so the
    /// thresholds are absolute rather than fractions of the range. A crate that
    /// multiplies rare weights by two is worth the same walk in Cold Storage as
    /// in The Flats, and a tier that meant something different per biome would
    /// teach the player nothing they could carry between runs.
    ///
    /// The consequence, worked out rather than discovered later:
    ///
    ///   The Flats     x3.0   all three tiers
    ///   Ash District  x2.1   0 and 1
    ///   Rail Yard     x1.9   0 and 1
    ///   Old Town      x1.4   0 only
    ///   Cold Storage  x1.3   0 only
    ///
    /// Two places show one tier and their crates are all the same grey. That is
    /// not the mark failing — it is the mark telling the truth. Old Town is
    /// loot-rich with a short walk between crates by design, and the honest thing
    /// for it to say is "none of these is worth crossing the map for". The top
    /// tier existing in exactly one biome is likewise a feature: it is the only
    /// warm colour on a loot container anywhere, and it should be rare enough
    /// that seeing one means something.
    public static int TierFor(float rarityBias) =>
        rarityBias >= 2.2f ? 2 : rarityBias >= 1.5f ? 1 : 0;

    /// One material for every crate in the arena, built once.
    ///
    /// Vertex colour is the albedo, so the palette lives in the geometry. Cached
    /// rather than returned fresh because the meshes below are cached too and
    /// shared between every crate on the map — a material per container would be
    /// twelve materials all assigned to the same six surfaces, of which only the
    /// last would ever be used.
    private static StandardMaterial3D? _material;

    public static StandardMaterial3D Material() => _material ??= new StandardMaterial3D
    {
        VertexColorUseAsAlbedo = true,
        Roughness = 0.88f,
        Metallic = 0.0f,
        CullMode = BaseMaterial3D.CullModeEnum.Back,
    };

    // Built once per (look, tier) and shared. Six meshes for the whole game, and
    // the alternative is rebuilding a hundred boxes every time the level
    // regenerates — which the base screen does on every biome change.
    private static readonly ArrayMesh?[,] Bodies = new ArrayMesh[2, Tiers];
    private static readonly ArrayMesh?[,] Lids = new ArrayMesh[2, Tiers];

    public static ArrayMesh Body(Look look, int tier)
    {
        tier = Mathf.Clamp(tier, 0, Tiers - 1);
        return Bodies[(int)look, tier] ??= Dressed(look == Look.Crate
            ? CrateBody(tier)
            : CacheBody(tier));
    }

    /// The material goes on at build time, once, with the mesh it belongs to.
    /// The caller gets something drawable and cannot forget — which is exactly
    /// how the white cube happened.
    private static ArrayMesh Dressed(ArrayMesh mesh)
    {
        mesh.SurfaceSetMaterial(0, Material());
        return mesh;
    }

    /// The lid, as its own mesh so it can be hinged.
    ///
    /// An emptied crate looked exactly like a full one, across a whole arena,
    /// for as long as crates have existed — `LootContainer` never touched its
    /// mesh. The minimap knows, and the minimap is a nine-centimetre square in
    /// the corner. A lid that stands open is the same information where the
    /// player is already looking.
    public static ArrayMesh Lid(Look look, int tier)
    {
        tier = Mathf.Clamp(tier, 0, Tiers - 1);
        return Lids[(int)look, tier] ??= Dressed(look == Look.Crate
            ? CrateLid(tier)
            : CacheLid(tier));
    }

    /// Where the lid turns, in the container's own space. The hinge is at the
    /// back edge, so an open lid leans away from the player rather than into the
    /// camera.
    public static Vector3 Hinge(Look look) =>
        look == Look.Crate ? new Vector3(0.0f, 0.62f, -0.42f) : new Vector3(0.0f, 0.92f, -0.62f);

    /// How far open an emptied lid stands, in radians. Past about this the lid
    /// disappears behind the box from the game's camera angle and the crate
    /// reads as closed again.
    public const float OpenAngle = -1.15f;

    // --- The scavenged crate -------------------------------------------------

    private static ArrayMesh CrateBody(int tier)
    {
        var mesh = new MeshBuilder();

        // Planks rather than a box. Five vertical boards with the grain gap
        // between them is the whole read at the size this is drawn — a single
        // brown box is the white cube with a coat of paint.
        for (int i = 0; i < 5; i++)
        {
            float x = -0.36f + i * 0.18f;
            mesh.Box(new Vector3(x, 0.31f, 0.0f), new Vector3(0.168f, 0.62f, 0.86f),
                     Timber.Lerp(TimberDark, i * 0.12f));
        }

        // Frame: rails top and bottom, and corner posts. This is what stops the
        // planks reading as a fence panel lying down.
        mesh.Box(new Vector3(0.0f, 0.07f, 0.0f), new Vector3(0.94f, 0.14f, 0.9f), TimberDark);
        mesh.Box(new Vector3(0.0f, 0.56f, 0.0f), new Vector3(0.94f, 0.1f, 0.9f), TimberDark);

        foreach (int sx in new[] { -1, 1 })
        {
            foreach (int sz in new[] { -1, 1 })
                mesh.Box(new Vector3(sx * 0.44f, 0.31f, sz * 0.42f), new Vector3(0.08f, 0.64f, 0.08f), Steel);
        }

        // Two steel bands round it, and the feet it stands on.
        mesh.Box(new Vector3(-0.22f, 0.31f, 0.0f), new Vector3(0.05f, 0.66f, 0.92f), Strap);
        mesh.Box(new Vector3(0.24f, 0.31f, 0.0f), new Vector3(0.05f, 0.66f, 0.92f), Strap);

        foreach (int sx in new[] { -1, 1 })
            mesh.Box(new Vector3(sx * 0.36f, 0.03f, 0.0f), new Vector3(0.16f, 0.06f, 0.94f), TimberDark);

        // The stencil. Face-on to the camera's default approach, proud of the
        // planks so it never z-fights with the grain.
        mesh.Box(new Vector3(0.02f, 0.34f, 0.44f), new Vector3(0.3f, 0.22f, 0.02f), TierMark[tier]);

        // And a second mark on the far side, because the player arrives from
        // wherever they happen to be. One stencil is a crate you have to walk
        // around to price.
        mesh.Box(new Vector3(0.02f, 0.34f, -0.44f), new Vector3(0.3f, 0.22f, 0.02f), TierMark[tier]);

        return mesh.Build();
    }

    private static ArrayMesh CrateLid(int tier)
    {
        var mesh = new MeshBuilder();

        // Authored about the hinge, so the node it hangs on can simply turn.
        mesh.Box(new Vector3(0.0f, 0.05f, 0.42f), new Vector3(0.96f, 0.1f, 0.88f), Timber.Lerp(TimberDark, 0.3f));
        mesh.Box(new Vector3(0.0f, 0.11f, 0.42f), new Vector3(0.98f, 0.04f, 0.2f), TimberDark);

        foreach (float x in new[] { -0.22f, 0.24f })
            mesh.Box(new Vector3(x, 0.11f, 0.42f), new Vector3(0.05f, 0.04f, 0.9f), Strap);

        // A latch at the front edge, which is the detail that says this opens.
        mesh.Box(new Vector3(0.0f, 0.02f, 0.84f), new Vector3(0.14f, 0.1f, 0.06f), Steel);
        return mesh.Build();
    }

    // --- The dropped cache ---------------------------------------------------

    private static ArrayMesh CacheBody(int tier)
    {
        var mesh = new MeshBuilder();

        // A moulded shell rather than boards: this was packed, not scavenged.
        mesh.Box(new Vector3(0.0f, 0.46f, 0.0f), new Vector3(1.3f, 0.92f, 1.2f), Shell);
        mesh.Box(new Vector3(0.0f, 0.08f, 0.0f), new Vector3(1.36f, 0.16f, 1.26f), Steel.Lerp(Shell, 0.4f));

        // Ribs down the sides. Horizontal on the long faces only, so the thing
        // has an obvious front.
        for (int i = 0; i < 4; i++)
        {
            float y = 0.2f + i * 0.2f;
            mesh.Box(new Vector3(0.0f, y, 0.61f), new Vector3(1.2f, 0.07f, 0.03f), Shell.Lerp(Steel, 0.5f));
            mesh.Box(new Vector3(0.0f, y, -0.61f), new Vector3(1.2f, 0.07f, 0.03f), Shell.Lerp(Steel, 0.5f));
        }

        // Chute harness, still attached, gathered at the corners. It is the one
        // element that says this arrived rather than was left.
        foreach (int sx in new[] { -1, 1 })
        {
            foreach (int sz in new[] { -1, 1 })
            {
                mesh.Box(new Vector3(sx * 0.6f, 0.5f, sz * 0.56f), new Vector3(0.07f, 1.0f, 0.07f), Strap);
                mesh.Box(new Vector3(sx * 0.52f, 0.96f, sz * 0.48f), new Vector3(0.24f, 0.08f, 0.24f), Canvas, 20.0f);
            }
        }

        // Spilled canopy on one side, so it is not a symmetrical box.
        mesh.Box(new Vector3(-0.86f, 0.06f, 0.3f), new Vector3(0.5f, 0.1f, 0.6f), Canvas, 24.0f);
        mesh.Box(new Vector3(-0.72f, 0.14f, -0.2f), new Vector3(0.34f, 0.12f, 0.42f), Canvas.Lerp(Shell, 0.3f), 52.0f);

        // Beacon panel, and its housing. The cache is a thing you are meant to
        // run toward, so its mark is bigger than a crate's and on the top where
        // it can be seen over cover.
        mesh.Box(new Vector3(0.0f, 0.95f, 0.34f), new Vector3(0.44f, 0.1f, 0.24f), TierMark[tier]);
        mesh.Box(new Vector3(0.0f, 0.5f, 0.62f), new Vector3(0.5f, 0.3f, 0.03f), TierMark[tier]);
        return mesh.Build();
    }

    private static ArrayMesh CacheLid(int tier)
    {
        var mesh = new MeshBuilder();

        mesh.Box(new Vector3(0.0f, 0.06f, 0.6f), new Vector3(1.32f, 0.12f, 1.22f), Shell.Lerp(Steel, 0.3f));
        mesh.Box(new Vector3(0.0f, 0.13f, 0.6f), new Vector3(1.34f, 0.04f, 0.3f), Steel);

        foreach (int sx in new[] { -1, 1 })
            mesh.Box(new Vector3(sx * 0.52f, 0.13f, 0.6f), new Vector3(0.08f, 0.05f, 1.24f), Strap);

        // Two catches, one each side of the front edge.
        foreach (int sx in new[] { -1, 1 })
            mesh.Box(new Vector3(sx * 0.3f, 0.02f, 1.18f), new Vector3(0.16f, 0.12f, 0.07f), Steel);

        return mesh.Build();
    }
}
