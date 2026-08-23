using Godot;

/// A corner map that only shows where the player has been.
///
/// The arena is 110 metres across and the fog stops the view at 35, so for most
/// of a run the player can see about a tenth of the map. That is the intended
/// tension and it produced an unintended one: with no record of where they had
/// walked, the only navigable strategy was to keep moving outward and hope, and
/// two crates in opposite directions were the same decision as one.
///
/// **Explored, not revealed.** The whole map would answer the question the fog
/// exists to ask. What this remembers is only what has already been seen, so it
/// turns a run into a route rather than a search — and the dark part of it is
/// still the dark part of the map.
public partial class Minimap : TextureRect
{
    /// Cells across. Sixty-four over 110 metres is about 1.7 m a pixel, which is
    /// coarse enough that a wall is a smudge rather than a floor plan — the map
    /// is for orientation, not for navigation around individual cover.
    [Export] public int Cells { get; set; } = 64;

    /// How far being somewhere puts it on the map, in metres.
    ///
    /// Deliberately much shorter than the fog's 35 m reach, and the difference is
    /// the point: seeing something now and having it on your map are not the same
    /// thing. The fog decides what is on screen; this decides what has been
    /// *learned*, and ground glimpsed once through the murk at thirty metres has
    /// not been learned.
    ///
    /// It was 30 m, matched to the fog, and that gave the whole thing away. The
    /// map remembers a full disc regardless of which way the player is facing, so
    /// a 30 m radius at the spawn covered all three danger zones on the first
    /// frame — the map answered "where is everything" before the player had
    /// taken a step. Sixteen is about a fifteenth of the arena, so the map fills
    /// in as a trail behind the route rather than as a circle that arrives with
    /// you.
    [Export] public float SightMetres { get; set; } = 16.0f;

    /// Physics ticks between redraws. The map is a memory, not an instrument;
    /// ten a second is far more than enough and costs a fraction of rebuilding
    /// a 64 by 64 image every frame.
    [Export] public int RedrawInterval { get; set; } = 6;

    private LevelGenerator? _level;
    private Player? _player;
    private RunDirector? _director;
    private Node3D? _obstacles;
    private Node3D? _crates;
    private Node3D? _zones;

    private Image? _image;
    private ImageTexture? _texture;

    /// One byte a cell: 0 never seen, 255 fully remembered.
    ///
    /// Separate from the image because the image is rebuilt from scratch on every
    /// redraw — the world under it moves, crates get looted, zones wake — and the
    /// one thing that must survive that rebuild is what has been walked.
    private byte[] _seen = System.Array.Empty<byte>();

    private int _tick;
    private float _extent = 55.0f;

    public override void _Ready()
    {
        Node? root = GetParent()?.GetParent();
        _level = root?.GetNodeOrNull<LevelGenerator>("Level");
        _player = root?.GetNodeOrNull<Player>("Player");
        _director = root?.GetNodeOrNull<RunDirector>("RunDirector");
        _obstacles = root?.GetNodeOrNull<Node3D>("Obstacles");
        _crates = root?.GetNodeOrNull<Node3D>("LootContainers");
        _zones = root?.GetNodeOrNull<Node3D>("DangerZones");

        _extent = _level?.Extent ?? 55.0f;
        _seen = new byte[Cells * Cells];

        _image = Image.CreateEmpty(Cells, Cells, false, Image.Format.Rgba8);
        _texture = ImageTexture.CreateFromImage(_image);
        Texture = _texture;

        // Nearest, deliberately. A 64-pixel image scaled to 220 and filtered
        // smoothly is a blurred smear with no cell edges; unfiltered it reads as
        // a map made of blocks, which is what it is.
        TextureFilter = CanvasItem.TextureFilterEnum.Nearest;

        Redraw();
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_player == null)
            return;

        Remember(_player.GlobalPosition);

        if (++_tick < RedrawInterval)
            return;

        _tick = 0;
        Redraw();
    }

    /// Marks everything within sight of a point as seen.
    ///
    /// Accumulated rather than set, so the edge of what has been walked fades
    /// out instead of ending on a hard circle — a map whose explored region is a
    /// union of hard discs looks like a rendering error rather than like memory.
    private void Remember(Vector3 at)
    {
        float perCell = _extent * 2.0f / Cells;
        int radius = Mathf.CeilToInt(SightMetres / perCell);
        (int cx, int cz) = ToCell(at.X, at.Z);

        for (int z = Mathf.Max(0, cz - radius); z <= Mathf.Min(Cells - 1, cz + radius); z++)
        {
            for (int x = Mathf.Max(0, cx - radius); x <= Mathf.Min(Cells - 1, cx + radius); x++)
            {
                float distance = Mathf.Sqrt((x - cx) * (x - cx) + (z - cz) * (z - cz)) * perCell;
                if (distance > SightMetres)
                    continue;

                // Full brightness well inside, tapering at the edge of sight.
                int strength = Mathf.RoundToInt(255.0f * Mathf.Clamp(
                    1.0f - (distance / SightMetres - 0.55f) / 0.45f, 0.0f, 1.0f));

                int index = z * Cells + x;
                if (strength > _seen[index])
                    _seen[index] = (byte)strength;
            }
        }
    }

    private (int X, int Z) ToCell(float worldX, float worldZ) => (
        Mathf.Clamp((int)((worldX / _extent + 1.0f) * 0.5f * Cells), 0, Cells - 1),
        Mathf.Clamp((int)((worldZ / _extent + 1.0f) * 0.5f * Cells), 0, Cells - 1));

    private void Redraw()
    {
        if (_image == null || _texture == null)
            return;

        var unseen = new Color(0.03f, 0.03f, 0.04f, 0.72f);

        for (int z = 0; z < Cells; z++)
        {
            for (int x = 0; x < Cells; x++)
            {
                float memory = _seen[z * Cells + x] / 255.0f;
                if (memory <= 0.0f)
                {
                    _image.SetPixel(x, z, unseen);
                    continue;
                }

                // The tile's own tint, darkened. The tints are near-white
                // multipliers meant for a lit floor, so used raw the map would be
                // four shades of off-white and read as one.
                Color ground = LevelGenerator.TintFor(TileAt(x, z));
                var shown = new Color(ground.R * 0.30f, ground.G * 0.29f, ground.B * 0.27f, 0.86f);

                _image.SetPixel(x, z, unseen.Lerp(shown, memory));
            }
        }

        Paint(_obstacles, new Color(0.44f, 0.43f, 0.42f), onlySeen: true);
        PaintZones();
        Paint(_crates, new Color(0.95f, 0.82f, 0.35f), onlySeen: true);
        PaintPads();
        PaintPlayer();

        _texture.Update(_image);
    }

    private int TileAt(int x, int z)
    {
        int[] tiles = _level?.TileMap ?? System.Array.Empty<int>();
        int grid = _level?.GridSize ?? 0;

        if (grid <= 0 || tiles.Length < grid * grid)
            return 0;

        int gx = Mathf.Clamp(x * grid / Cells, 0, grid - 1);
        int gz = Mathf.Clamp(z * grid / Cells, 0, grid - 1);
        return tiles[gz * grid + gx];
    }

    /// Draws one pixel per child of a container.
    ///
    /// `onlySeen` is what keeps the map honest: a crate the player has not walked
    /// past is not on it. Without that the map would answer "where is the loot",
    /// which is the question the run is about.
    private void Paint(Node3D? container, Color colour, bool onlySeen)
    {
        foreach (Node child in container?.GetChildren() ?? new Godot.Collections.Array<Node>())
        {
            if (child is not Node3D piece)
                continue;

            if (child is LootContainer { Looted: true })
                continue;

            (int x, int z) = ToCell(piece.GlobalPosition.X, piece.GlobalPosition.Z);
            if (onlySeen && _seen[z * Cells + x] == 0)
                continue;

            _image!.SetPixel(x, z, colour);
        }
    }

    /// Zones are drawn as their whole rectangle, and only once woken.
    ///
    /// A dormant zone the player has walked past is on the map because they saw
    /// it; a dormant one they have not is not. A *running* one is drawn wherever
    /// they are, because a fight they started and walked away from is something
    /// they know about.
    private void PaintZones()
    {
        foreach (Node child in _zones?.GetChildren() ?? new Godot.Collections.Array<Node>())
        {
            if (child is not DangerZone zone)
                continue;

            if (!WouldDraw(zone))
                continue;

            (int cx, int cz) = ToCell(zone.GlobalPosition.X, zone.GlobalPosition.Z);

            Color colour = zone.State switch
            {
                DangerZone.ZoneState.Running => new Color(0.95f, 0.42f, 0.18f),
                DangerZone.ZoneState.Cleared => new Color(0.35f, 0.70f, 0.45f),
                _ => new Color(0.45f, 0.52f, 0.68f),
            };

            float perCell = _extent * 2.0f / Cells;
            int halfX = Mathf.Max(1, Mathf.RoundToInt(zone.HalfExtent.X / perCell));
            int halfZ = Mathf.Max(1, Mathf.RoundToInt(zone.HalfExtent.Y / perCell));

            // The outline only. Filled, a 26 by 20 metre rectangle is a quarter
            // of the visible map and buries everything under it.
            for (int x = cx - halfX; x <= cx + halfX; x++)
            {
                Plot(x, cz - halfZ, colour);
                Plot(x, cz + halfZ, colour);
            }

            for (int z = cz - halfZ; z <= cz + halfZ; z++)
            {
                Plot(cx - halfX, z, colour);
                Plot(cx + halfX, z, colour);
            }
        }
    }

    /// Pads appear when the director reveals them, seen or not.
    ///
    /// The way out is the one thing the game tells the player rather than making
    /// them find — it is announced on the readout at the same moment, and a map
    /// that disagreed with the announcement would be worse than no map.
    private void PaintPads()
    {
        foreach (ExtractionZone pad in _director?.Pads ?? System.Array.Empty<ExtractionZone>())
        {
            if (!pad.Visible)
                continue;

            (int x, int z) = ToCell(pad.GlobalPosition.X, pad.GlobalPosition.Z);
            var colour = pad.Open ? new Color(0.45f, 1.0f, 0.60f) : new Color(0.30f, 0.55f, 0.38f);

            Plot(x, z, colour);
            Plot(x + 1, z, colour);
            Plot(x - 1, z, colour);
            Plot(x, z + 1, colour);
            Plot(x, z - 1, colour);
        }
    }

    private void PaintPlayer()
    {
        if (_player == null)
            return;

        (int x, int z) = ToCell(_player.GlobalPosition.X, _player.GlobalPosition.Z);
        var colour = new Color(1.0f, 1.0f, 1.0f);

        Plot(x, z, colour);
        Plot(x + 1, z, colour);
        Plot(x - 1, z, colour);
        Plot(x, z + 1, colour);
        Plot(x, z - 1, colour);
    }

    private void Plot(int x, int z, Color colour)
    {
        if (x < 0 || z < 0 || x >= Cells || z >= Cells)
            return;

        _image!.SetPixel(x, z, colour);
    }

    /// Whether a world point has ever been within sight.
    ///
    /// The map's own rule, exposed rather than reimplemented in the probe — a
    /// test that recomputed "has this been seen" would agree with itself and
    /// prove nothing about what is drawn.
    public bool HasSeen(Vector3 at)
    {
        (int x, int z) = ToCell(at.X, at.Z);
        return _seen[z * Cells + x] > 0;
    }

    /// Whether this zone would appear on the map right now.
    ///
    /// Same condition `PaintZones` uses, and called by it, so the two cannot
    /// drift. A running or cleared zone is known wherever the player is — a fight
    /// they started and walked away from is something they know about.
    public bool WouldDraw(DangerZone zone) =>
        zone.State != DangerZone.ZoneState.Dormant || HasSeen(zone.GlobalPosition);

    /// How much of the map has been walked, 0 to 1. Only a probe asks — and it
    /// has to, because "the map is drawn" and "the map is remembering anything"
    /// look identical from outside.
    public float Explored
    {
        get
        {
            int seen = 0;
            foreach (byte cell in _seen)
            {
                if (cell > 0)
                    seen++;
            }

            return _seen.Length == 0 ? 0.0f : seen / (float)_seen.Length;
        }
    }
}
