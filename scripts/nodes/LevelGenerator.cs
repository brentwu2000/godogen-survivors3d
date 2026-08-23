using Godot;

/// Lays out the arena for one run: cover, crates, and which pads will open.
///
/// Runs before the horde, because the flow field bakes obstacles once at
/// startup. Generating after that bake produces a level with walls the enemies
/// walk straight through — the most convincing kind of wrong, because the
/// screen looks right and only the pathing disagrees.
///
/// Everything here comes from one seed and nothing else, so a run can be
/// replayed exactly. The seed is printed on generation for that reason: an
/// interesting layout is worth being able to get back to.
public partial class LevelGenerator : Node3D
{
    /// 0 picks a fresh layout from the clock. Set it to replay one.
    [Export] public ulong Seed { get; set; }

    /// Half-width of the playable square. Matches the horde's arena, because a
    /// crate outside it is a crate the flow field cannot route to.
    [Export] public float Extent { get; set; } = 55.0f;

    [Export] public int GridSize { get; set; } = 5;
    [Export] public int CrateCount { get; set; } = 8;
    [Export] public int PadCount { get; set; } = 3;

    /// Danger zones per map. Three: one of each kind, so a run always offers a
    /// choice between two ways of being paid rather than one.
    [Export] public int ZoneCount { get; set; } = 3;

    /// How many of the pads will actually open. Fewer than PadCount is the
    /// point: the walk home is a decision only when some of the exits are shut.
    [Export] public int OpenPadCount { get; set; } = 2;

    /// Cleared radius around the spawn, so a run never starts inside a wall.
    [Export] public float SpawnClearance { get; set; } = 8.0f;

    /// Multiplies the weight of rarer items once per rarity step, scaled by how
    /// far the crate is from the spawn. Depth has to pay, or the whole map past
    /// the first ring is risk with no reason.
    [Export] public float DepthRarityBias { get; set; } = 1.9f;

    /// Where this seed put its danger zones. Read by anything that needs to
    /// know about them without walking the scene tree for nodes.
    public System.Collections.Generic.IReadOnlyList<ZonePlan> Zones => _zones;

    private ZonePlan[] _zones = System.Array.Empty<ZonePlan>();

    /// Blocks removed by the last generation to open a sealed objective. Zero on
    /// most seeds; a probe watches it to confirm the rescue is not dead code.
    public int CarvedLastRun { get; private set; }

    /// The tile each grid cell drew, row-major. Exposed so a probe can check that
    /// the ground the player is standing on agrees with the cover around them —
    /// the tint and the cover pool are chosen in one place and consumed in two,
    /// and a disagreement would be a map that lies about what is in it.
    public int[] TileMap => _tiles;

    /// What a tile tints its ground. Multiplied over the shared asphalt, so the
    /// tints read as what happened to that ground rather than as three materials:
    /// oil and shade around the containers, dust where a building came down, bare
    /// concrete along the walls.
    public static Color TintFor(int tile) => TileTints[Mathf.Clamp(tile, 0, TileTints.Length - 1)];

    /// One piece of cover: where it is, how much ground it takes, and what it
    /// looks like. The footprint and the prop travel together because the two
    /// have to agree — a container drawn across a footprint it does not fill is
    /// cover the player can see through and walk into.
    private readonly struct Block
    {
        public readonly Vector2 Center;
        public readonly Vector2 Half;
        public readonly PropKind Kind;
        public readonly float Yaw;
        public readonly float HeightScale;

        public Block(Vector2 center, Vector2 half, PropKind kind, float yaw, float heightScale)
        {
            Center = center;
            Half = half;
            Kind = kind;
            Yaw = yaw;
            HeightScale = heightScale;
        }
    }

    private ulong _rng;
    private PropRenderer? _props;
    private ScatterField? _scatter;

    /// Ankle-high decoration per map, across all three kinds.
    ///
    /// Nine hundred, which sounds like a lot and is three MultiMeshes. Cover is
    /// what the player walks around and the ground shader is what they walk on;
    /// between the two there was nothing, so the arena had no object small enough
    /// to measure itself against and read as large and empty at every distance.
    [Export] public int ScatterCount { get; set; } = 900;

    /// The tile kind each grid cell drew, for the ground to tint by. Written by
    /// BuildCover and read by the material — the layout the generator chose is
    /// only a decision if the player can see it from the ground they are standing
    /// on.
    private int[] _tiles = System.Array.Empty<int>();

    /// Which place this is. Set from `GameSession.Biome` before generating, so
    /// the base screen's choice survives the scene change without a node in the
    /// new scene having to be ready in time to be asked.
    public BiomeResource Biome { get; set; } = new();

    public override void _Ready()
    {
        Biome = BiomeBook.Load(GameSession.Biome);

        // The daily pins the layout as well as the place. Everyone getting the
        // same biome and a different map would make "the same run for everyone"
        // a claim about the ground colour.
        if (GameSession.IsDaily)
            Seed = GameSession.DailySeed;

        Generate();
    }

    /// Public so a probe can regenerate in place and compare: reproducing a
    /// layout from its seed is a property worth testing, not an implementation
    /// detail.
    public void Generate()
    {
        if (Seed == 0)
            Seed = (ulong)Time.GetTicksUsec() | 1UL;

        _rng = Seed;

        // Shifted per seed, before anything is placed. Every plant below reads
        // this, so it has to be settled first — a level generated against one
        // offset and drawn against another is a map whose crates float.
        //
        // Hashed from the seed rather than drawn from `_rng`, and that is not a
        // style preference. Two `NextFloat()` calls here shift every draw the
        // generator makes afterwards, so the same seed lays out a completely
        // different map — the cover moves, the crates move, and the pads move.
        // Nothing errors. What happened instead was that two probes started
        // failing with enemies that would not walk, because the layout they had
        // been written against no longer existed, and the terrain looked like the
        // last thing that could be responsible.
        ulong mix = Seed * 0x9E3779B97F4A7C15UL;
        mix ^= mix >> 29;
        mix *= 0xBF58476D1CE4E5B9UL;
        mix ^= mix >> 32;

        Terrain.Offset = new Vector2(
            (mix & 0xFFFFUL) / 65535.0f * 900.0f,
            ((mix >> 16) & 0xFFFFUL) / 65535.0f * 900.0f);

        // And the floor is rebuilt against it. `Ground` is ready long before this
        // node is, so the mesh it built in `_Ready` belongs to the previous
        // seed's offset — a floor that looks like perfectly good ground while
        // being a different landscape from everything standing on it.
        GetParent()?.GetNodeOrNull<GroundMesh>("Ground")?.Rebuild();

        Node3D obstacles = Container("Obstacles");
        Node3D crates = Container("LootContainers");
        Node3D pads = Container("ExtractionZones");
        Node3D zones = Container("DangerZones");

        Clear(obstacles);
        Clear(crates);
        Clear(pads);
        Clear(zones);

        var blocks = new System.Collections.Generic.List<Block>();
        BuildCover(blocks);
        BuildPads(pads, blocks);

        // Before the crates, so a crate is never sited on top of a zone marker.
        // After the pads, because a zone that overlapped the way out would let
        // the player finish it by standing where they were going anyway.
        BuildZones(zones, blocks);
        BuildCrates(crates, blocks);

        CarvedLastRun = EnsureReachable(blocks, pads, crates);

        // Bodies and props are created only once the layout is final. Carving
        // used to delete nodes it had already added, matching them back up by
        // position because their names had gone stale — none of which has to
        // exist if nothing is built until there is nothing left to remove.
        Emit(obstacles, blocks);

        GD.Print($"level seed {Seed}: {blocks.Count} blocks, {crates.GetChildCount()} crates, " +
                 $"{OpenPadCount}/{pads.GetChildCount()} pads will open, {CarvedLastRun} carved, " +
                 $"{_props?.Total ?? 0} props, {_scatter?.Total ?? 0} scatter");
    }

    /// Random cover can seal a corner off, and a run whose exit is behind a wall
    /// is not a hard run — it is a broken one. Rather than reject and reroll the
    /// whole layout, this opens the shortest way in: every block between the
    /// spawn and an unreachable target is removed, which reads as a street
    /// through the rubble rather than as a level that gave up.
    ///
    /// Reachability is decided on the same grid resolution and the same obstacle
    /// inflation the flow field uses, so "the generator thinks it is reachable"
    /// and "the enemies can get there" cannot disagree.
    private int EnsureReachable(System.Collections.Generic.List<Block> blocks, Node3D pads, Node3D crates)
    {
        var targets = new System.Collections.Generic.List<Vector2>();
        foreach (Node child in pads.GetChildren())
        {
            if (child is ExtractionZone { WillOpen: true } pad)
                targets.Add(new Vector2(pad.Position.X, pad.Position.Z));
        }

        foreach (Node child in crates.GetChildren())
        {
            if (child is Node3D crate)
                targets.Add(new Vector2(crate.Position.X, crate.Position.Z));
        }

        int carved = 0;

        foreach (Vector2 target in targets)
        {
            while (!Reachable(blocks, target))
            {
                int removed = CarveTowards(blocks, target);

                // Nothing left on the line and still no route means the corridor
                // is not the problem. Widening it further would eat the map, so
                // this target is left as it is and the sweep gets to report it.
                if (removed == 0)
                    break;

                carved += removed;
            }
        }

        return carved;
    }

    /// Asks a real FlowField, built the way the horde builds its own.
    ///
    /// Writing a second reachability test here was a mistake worth recording: it
    /// agreed with itself and disagreed with the game. The field blocks a cell
    /// with floor() on one edge and ceil() on the other, so a copy that floors
    /// both is a fraction more optimistic than the thing it is standing in for —
    /// enough to call a sealed corner open. One implementation, no drift.
    private bool Reachable(System.Collections.Generic.List<Block> blocks, Vector2 target)
    {
        var horde = GetParent().GetNodeOrNull<Horde>("Horde");
        float extent = horde?.ArenaExtent ?? 60.0f;
        float inflate = horde?.SeparationRadius ?? 0.75f;

        var field = new FlowField(Vector2.Zero, extent, 1.5f);
        foreach (Block block in blocks)
            field.BlockBox(block.Center, block.Half + Vector2.One * inflate);

        field.Rebuild(new Vector3(target.X, 0.0f, target.Y));

        // Zero at the spawn means the sweep never reached it. Sampling one cell
        // is enough because the field only assigns a direction to cells it
        // actually visited.
        return field.Sample(Vector3.Zero) != Vector2.Zero;
    }

    /// Removes every block the straight line from spawn to the target passes
    /// through. Returns how many went.
    ///
    /// A list operation and nothing more. It used to also delete the matching
    /// scene nodes, matched back up by position because a compacted list makes
    /// every name after the first carve refer to a different block — a hazard
    /// that stopped existing once the bodies were built after the last carve
    /// rather than before the first.
    private int CarveTowards(System.Collections.Generic.List<Block> blocks, Vector2 target)
    {
        // Wide enough to survive being narrowed twice. The field inflates every
        // obstacle by the enemy radius and then rounds the footprint outward to
        // whole cells, so a corridor cleared to the width a body needs arrives
        // at the field as no corridor at all. Two clear cells after both, or the
        // rescue reports success and the pad is still sealed.
        const float corridorHalfWidth = 2.6f;

        var survivors = new System.Collections.Generic.List<Block>(blocks.Count);
        int removed = 0;

        foreach (Block block in blocks)
        {
            if (SegmentHitsBox(Vector2.Zero, target, block.Center, block.Half + Vector2.One * corridorHalfWidth))
                removed++;
            else
                survivors.Add(block);
        }

        if (removed == 0)
            return 0;

        blocks.Clear();
        blocks.AddRange(survivors);
        return removed;
    }

    /// Sampled rather than solved: the segment is short and the boxes are small,
    /// so stepping along it is both simpler and sufficient.
    private static bool SegmentHitsBox(Vector2 from, Vector2 to, Vector2 center, Vector2 half)
    {
        int steps = Mathf.Max(8, Mathf.CeilToInt(from.DistanceTo(to)));

        for (int i = 0; i <= steps; i++)
        {
            Vector2 point = from.Lerp(to, (float)i / steps);
            if (Mathf.Abs(point.X - center.X) < half.X && Mathf.Abs(point.Y - center.Y) < half.Y)
                return true;
        }

        return false;
    }

    /// The scene's container for one kind of generated thing.
    ///
    /// Creating one is a fallback for a scene that predates it, and it does not
    /// work from `_Ready` — which is the only place this is ever called from.
    /// Godot refuses `add_child()` while a parent is still setting up its
    /// children, prints "Parent node is busy setting up children", and carries
    /// on. Everything after that constructs correctly into a subtree that is not
    /// in the tree: no exception, no missing reference, and nothing on screen.
    ///
    /// So the fallback warns. `BuildMain` is the fix, and the warning is what
    /// says to go and add the node there.
    private Node3D Container(string name)
    {
        var existing = GetParent().GetNodeOrNull<Node3D>(name);
        if (existing != null)
            return existing;

        GD.PushWarning($"LevelGenerator: no '{name}' in the scene — add it to BuildMain. " +
                       "Creating one from _Ready is refused and everything put in it will be invisible.");

        var created = new Node3D { Name = name };
        GetParent().CallDeferred(Node.MethodName.AddChild, created);
        return created;
    }

    private static void Clear(Node3D container)
    {
        foreach (Node child in container.GetChildren())
        {
            container.RemoveChild(child);
            child.QueueFree();
        }
    }

    /// Which tile a grid cell drew. An enum rather than a loose int so the ground
    /// tint and the cover pool cannot disagree about what a cell is.
    private enum Tile
    {
        Open,
        Yard,       // a few big pieces: containers, tumbled slabs
        Corridor,   // a wall with a gap in it
        Rubble,     // many small pieces: barriers, bins, debris
    }

    /// One tile per grid cell, skipping the cell the player starts in. Cover is
    /// what makes the flow field worth having and what a player retreats behind,
    /// so an empty map is not a simpler map — it is a different game.
    ///
    /// The tile is recorded per cell as well, because until now all three kinds
    /// looked identical from the ground: the generator was making a decision the
    /// player could not read, which is the same as not making one.
    private void BuildCover(System.Collections.Generic.List<Block> blocks)
    {
        float cell = Extent * 2.0f / GridSize;
        _tiles = new int[GridSize * GridSize];

        for (int gz = 0; gz < GridSize; gz++)
        for (int gx = 0; gx < GridSize; gx++)
        {
            var center = new Vector2(
                -Extent + cell * (gx + 0.5f),
                -Extent + cell * (gz + 0.5f));

            if (center.Length() < SpawnClearance + cell * 0.5f)
            {
                _tiles[gz * GridSize + gx] = (int)Tile.Open;
                continue;
            }

            Tile tile = RollTile();
            _tiles[gz * GridSize + gx] = (int)tile;

            // Counts and sizes are scaled and then floored at one. A biome that
            // asked for 0.55x of three pieces would otherwise place zero, and an
            // empty "yard" tile is indistinguishable from open ground — which
            // makes the tile weights a lie about what the map contains.
            switch (tile)
            {
                case Tile.Open:
                    break;   // open ground; a map with no gaps has no routes

                case Tile.Yard:
                    Cluster(blocks, center, cell, Scaled(3), 4.0f * Biome.ClusterSizeScale, tile);
                    break;

                case Tile.Corridor:
                    Corridor(blocks, center, cell);
                    break;

                default:
                    Cluster(blocks, center, cell, Scaled(5), 2.0f * Biome.ClusterSizeScale, tile);
                    break;
            }
        }
    }

    private int Scaled(int count) => Mathf.Max(1, Mathf.RoundToInt(count * Biome.ClusterCountScale));

    /// A weighted draw over the four tile kinds.
    ///
    /// Weights, not a fixed rotation: a biome should be a tendency the player
    /// learns to expect, and a map should still be a map. Old Town rolls rubble
    /// most of the time and open ground sometimes, which is what keeps the
    /// occasional clear line worth noticing.
    private Tile RollTile()
    {
        float total = Biome.WeightTotal;
        if (total <= 0.0f)
            return (Tile)(int)(NextFloat() * 4.0f);

        float pick = NextFloat() * total;
        for (int i = 0; i < 4; i++)
        {
            pick -= Biome.WeightOf(i);
            if (pick <= 0.0f)
                return (Tile)i;
        }

        return Tile.Open;
    }

    private void Cluster(System.Collections.Generic.List<Block> blocks,
                         Vector2 center, float cell, int count, float size, Tile tile)
    {
        for (int i = 0; i < count; i++)
        {
            var offset = new Vector2(
                (NextFloat() - 0.5f) * (cell - size),
                (NextFloat() - 0.5f) * (cell - size));

            var half = new Vector2(size * 0.5f * (0.6f + NextFloat() * 0.8f),
                                   size * 0.5f * (0.6f + NextFloat() * 0.8f));

            blocks.Add(new Block(center + offset, half, PickProp(tile, half),
                                 Yaw(half), 0.85f + NextFloat() * 0.3f));
        }
    }

    /// A long wall with a gap in it. The gap is the whole point — a solid wall
    /// is a boundary, and a wall with a way through is a decision.
    private void Corridor(System.Collections.Generic.List<Block> blocks, Vector2 center, float cell)
    {
        bool horizontal = NextFloat() < 0.5f;
        float length = cell * 0.38f;
        float thickness = 1.2f;
        float gap = Biome.CorridorGap;

        for (int side = -1; side <= 1; side += 2)
        {
            Vector2 offset = horizontal
                ? new Vector2(side * (length * 0.5f + gap * 0.5f), 0.0f)
                : new Vector2(0.0f, side * (length * 0.5f + gap * 0.5f));

            Vector2 half = horizontal
                ? new Vector2(length * 0.5f, thickness * 0.5f)
                : new Vector2(thickness * 0.5f, length * 0.5f);

            blocks.Add(new Block(center + offset, half, PropKind.Wall, Yaw(half), 1.0f));
        }
    }

    /// What a footprint should be drawn as. Shape first, then the tile: a long
    /// thin footprint is a wall whatever cell it is in, because a container drawn
    /// across it arrives stretched into something nobody recognises.
    private PropKind PickProp(Tile tile, Vector2 half)
    {
        float longest = Mathf.Max(half.X, half.Y);
        float shortest = Mathf.Max(0.01f, Mathf.Min(half.X, half.Y));

        if (longest / shortest > 2.6f)
            return PropKind.Wall;

        float roll = NextFloat();

        return tile switch
        {
            Tile.Yard => roll < 0.62f ? PropKind.Container : PropKind.Rubble,
            Tile.Rubble => roll < 0.4f ? PropKind.Barrier
                : roll < 0.72f ? PropKind.Rubble
                : PropKind.Dumpster,
            _ => roll < 0.5f ? PropKind.Rubble : PropKind.Barrier,
        };
    }

    /// Props are drawn along their footprint, so a footprint deeper than it is
    /// wide wants the prop turned a quarter. Anything else draws a container
    /// across the short axis of the ground it occupies.
    private static float Yaw(Vector2 half) => half.Y > half.X ? 90.0f : 0.0f;

    /// Builds the bodies and the props, once the layout can no longer change.
    private void Emit(Node3D obstacles, System.Collections.Generic.List<Block> blocks)
    {
        _props ??= CreateProps();
        _props.Clear();

        for (int i = 0; i < blocks.Count; i++)
        {
            Block block = blocks[i];
            float height = PropLibrary.Height(block.Kind) * block.HeightScale;
            var size = new Vector3(block.Half.X * 2.0f, height, block.Half.Y * 2.0f);

            // Collision only. The visual is one instance in a MultiMesh, so a
            // mesh here would be the same cover drawn twice and the draw-call
            // budget spent for nothing.
            var body = new StaticBody3D
            {
                Name = $"Block{i}",
                // Planted, then raised by half its height. The collider is a box
                // and the simulation is flat, so this only moves what is drawn and
                // what the player walks into — the flow field still sees a
                // rectangle on a plane.
                Position = new Vector3(block.Center.X,
                                       Terrain.Height(block.Center.X, block.Center.Y) + height * 0.5f,
                                       block.Center.Y),
            };
            body.AddChild(new CollisionShape3D { Name = "Collision", Shape = new BoxShape3D { Size = size } });
            obstacles.AddChild(body);

            // Footprints are half-extents and the props are authored in a unit
            // square, so the instance scale is the full width and depth. The yaw
            // is applied to the prop only: the collider stays axis-aligned, which
            // is what the flow field's box test assumes.
            Vector2 footprint = block.Yaw == 0.0f
                ? block.Half * 2.0f
                : new Vector2(block.Half.Y * 2.0f, block.Half.X * 2.0f);

            _props.Add(block.Kind, block.Center, footprint, block.Yaw, block.HeightScale);
        }

        PlaceLandmarks();
        _props.Commit();
        Scatter(blocks);
        PaintGround();
    }

    /// Sprinkles the floor.
    ///
    /// Rejection-sampled against the blocks with a small margin, so nothing sits
    /// half inside a wall — visible from a metre away and impossible to explain,
    /// because the object it is inside was never something the player could see
    /// through. A fixed number of *attempts* rather than a fixed number of
    /// placements: on a crowded map some are refused, and a loop that insisted on
    /// the count would spin against a layout that has no room for it.
    ///
    /// Tinted by the tile it lands on, so a piece of rubble in the oil-stained
    /// yard is a different colour from one on bleached open ground. Uniform
    /// scatter over four differently-tinted zones would fight the one thing the
    /// ground shader exists to do.
    private void Scatter(System.Collections.Generic.List<Block> blocks)
    {
        _scatter ??= new ScatterField(ScatterCount, Extent + 6.0f);
        _scatter.Clear();

        if (_scatter.Node.GetParent() == null)
            Container("Ground").AddChild(_scatter.Node);

        var kinds = System.Enum.GetValues<ScatterKind>();

        for (int i = 0; i < ScatterCount; i++)
        {
            var spot = new Vector2((NextFloat() - 0.5f) * 2.0f * Extent,
                                   (NextFloat() - 0.5f) * 2.0f * Extent);

            // Half a metre, which is less than the crates get. Scatter touching a
            // wall looks like it collected there; scatter *inside* one does not.
            if (InsideAnyBlock(spot, blocks, 0.5f))
                continue;

            var kind = (ScatterKind)(int)(NextFloat() * kinds.Length);

            Color tile = TintFor(TileAt(spot));

            // One factor for all three channels, not three.
            //
            // Rolling each channel separately does not vary the brightness, it
            // varies the *hue* — and independently, so the results are spread
            // over the whole colour wheel. The first version of this produced
            // pink, green and orange debris on a sand-coloured floor, which reads
            // as litter from another game. Brightness is the only axis that
            // should move: the tile tints are near-white multipliers rather than
            // colours, so they are dimmed to something a piece of debris could
            // plausibly be made of and left the colour the ground is.
            float shade = Mathf.Lerp(0.28f, 0.48f, NextFloat());
            var tint = new Color(tile.R * shade, tile.G * shade, tile.B * shade);

            _scatter.Add(kind, spot, NextFloat() * Mathf.Tau,
                         Mathf.Lerp(0.55f, 1.35f, NextFloat()), tint,
                         Terrain.Height(spot.X, spot.Y));
        }

        _scatter.Commit();
    }

    /// Which tile of the layout grid a world point falls in.
    private int TileAt(Vector2 point)
    {
        if (_tiles == null || _tiles.Length == 0)
            return 0;

        int x = Mathf.Clamp((int)((point.X / Extent + 1.0f) * 0.5f * GridSize), 0, GridSize - 1);
        int z = Mathf.Clamp((int)((point.Y / Extent + 1.0f) * 0.5f * GridSize), 0, GridSize - 1);
        return _tiles[z * GridSize + x];
    }

    /// Hands the ground shader one texel per grid cell.
    private static readonly Color[] TileTints =
    {
        new(1.00f, 0.97f, 0.90f),   // open — bare, sun-bleached
        new(0.70f, 0.73f, 0.78f),   // yard — oil-stained, cool
        new(0.86f, 0.85f, 0.82f),   // corridor — poured concrete
        new(0.93f, 0.81f, 0.66f),   // rubble — masonry dust
    };

    private void PaintGround()
    {
        var mesh = GetParent().GetNodeOrNull<MeshInstance3D>("Ground/Mesh");
        if (mesh?.MaterialOverride is not ShaderMaterial material)
        {
            GD.PushWarning("LevelGenerator: no ground shader — the layout will not be readable from the floor");
            return;
        }

        var image = Image.CreateEmpty(GridSize, GridSize, false, Image.Format.Rgb8);

        for (int gz = 0; gz < GridSize; gz++)
        {
            for (int gx = 0; gx < GridSize; gx++)
            {
                int tile = _tiles.Length > 0 ? _tiles[gz * GridSize + gx] : 0;

                // The biome tints the tile rather than replacing it. Multiplying
                // keeps the per-tile reading — the player still sees where the
                // rubble is from the floor colour — while the place as a whole
                // shifts, which is what makes a biome a parameter instead of a
                // second set of ground textures.
                image.SetPixel(gx, gz,
                               TileTints[Mathf.Clamp(tile, 0, TileTints.Length - 1)] * Biome.GroundTint);
            }
        }

        material.SetShaderParameter("zones", ImageTexture.CreateFromImage(image));
        material.SetShaderParameter("arena_extent", Extent);
    }

    private PropRenderer CreateProps()
    {
        var renderer = new PropRenderer(capacityPerKind: 128, arenaExtent: Extent + 24.0f);

        // Under this node rather than beside it. Generation happens in _Ready,
        // and the parent is still adding its own children at that point —
        // add_child on a parent that is mid-setup fails outright, and the only
        // symptom is an arena with no cover in it.
        AddChild(renderer.Node);
        return renderer;
    }

    /// A handful of tall things, well outside the play space.
    ///
    /// A fixed orthographic camera over a flat plane of repeating cover gives a
    /// player crossing fifty metres nothing that says they moved. These are the
    /// only parallax the arena has, and the only answer to "which corner am I in"
    /// that does not involve reading the compass.
    ///
    /// Outside the arena rather than in it, so they never become cover, never
    /// need a collider, and can never seal a route.
    private void PlaceLandmarks()
    {
        const int count = 5;
        float radius = Extent * 1.12f;
        float baseAngle = NextFloat() * Mathf.Tau;

        for (int i = 0; i < count; i++)
        {
            float angle = baseAngle + Mathf.Tau * i / count + (NextFloat() - 0.5f) * 0.3f;
            var spot = new Vector2(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius);

            PropKind kind = NextFloat() < 0.5f ? PropKind.WaterTower : PropKind.Billboard;
            float scale = 0.8f + NextFloat() * 0.5f;

            _props!.Add(kind, spot, new Vector2(4.0f, 4.0f) * scale,
                        Mathf.RadToDeg(angle) + 90.0f, scale);
        }
    }

    /// Pads sit far out and in different directions, so which one opens changes
    /// where the run ends. They are hidden until the director reveals them —
    /// knowing the way out from the first second would make the map a corridor.
    private void BuildPads(Node3D parent, System.Collections.Generic.List<Block> blocks)
    {
        float radius = Extent * 0.72f;
        float baseAngle = NextFloat() * Mathf.Tau;

        // Which ones open is decided here rather than by the director: the level
        // is one seed's worth of decisions, and this is one of them.
        int opening = Mathf.Clamp(OpenPadCount, 1, PadCount);
        int firstOpen = (int)(NextFloat() * PadCount);

        for (int i = 0; i < PadCount; i++)
        {
            float angle = baseAngle + Mathf.Tau * i / PadCount;
            var spot = new Vector2(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius);
            spot = PushOutOfBlocks(spot, blocks, 3.5f);

            // Built directly rather than assembled and re-scripted: nothing here
            // is packed, so the SetScript dance the builders need does not apply.
            var zone = new ExtractionZone
            {
                Name = $"Pad{i}",
                Position = Terrain.Plant(new Vector3(spot.X, 0.0f, spot.Y)),
                Open = false,
                Visible = false,
                WillOpen = (i - firstOpen + PadCount) % PadCount < opening,
            };

            // A slowly breathing ring rather than a filled disc. Filled, it hid
            // the ground the player is standing on and read as a flat sticker;
            // a ring is something to step inside, which is what it is for. The
            // same shader draws the burning ground — the difference between the
            // two is a colour and a speed, which is as it should be.
            var shader = GD.Load<Shader>("res://assets/shaders/ground_marker.gdshader");
            Material material;

            if (shader != null)
            {
                var live = new ShaderMaterial { Shader = shader };
                live.SetShaderParameter("inner_colour", new Color(0.55f, 1.0f, 0.68f));
                live.SetShaderParameter("outer_colour", new Color(0.12f, 0.62f, 0.30f));
                live.SetShaderParameter("strength", 0.6f);
                live.SetShaderParameter("churn", 0.7f);
                live.SetShaderParameter("flicker", 0.12f);
                live.SetShaderParameter("hollow", 0.62f);
                live.SetShaderParameter("seed", i * 1.31f);
                material = live;
            }
            else
            {
                material = new StandardMaterial3D
                {
                    AlbedoColor = new Color(0.2f, 0.85f, 0.4f, 0.55f),
                    ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                    Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                    DepthDrawMode = BaseMaterial3D.DepthDrawModeEnum.Disabled,
                };
            }

            zone.AddChild(new MeshInstance3D
            {
                Name = "Pad",
                Mesh = new QuadMesh { Size = new Vector2(6.4f, 6.4f) },
                MaterialOverride = material,
                RotationDegrees = new Vector3(-90.0f, 0.0f, 0.0f),
                Position = new Vector3(0.0f, 0.03f, 0.0f),
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            });

            parent.AddChild(zone);
        }
    }

    /// Crates get better the further out they are. Bias is applied per rarity
    /// step, so the far corners are where the serum actually lives.
    /// Sites the danger zones and builds a node for each.
    ///
    /// Here rather than in the run director because siting a thirteen-metre
    /// rectangle needs the map, and the map exists for one function inside this
    /// class. The director never learns where they are; the zones find the horde
    /// and the player themselves.
    private void BuildZones(Node3D parent, System.Collections.Generic.List<Block> blocks)
    {
        _zones = ZonePlan.Plan(ZoneCount, Extent, NextFloat);

        for (int i = 0; i < _zones.Length; i++)
        {
            ZonePlan plan = _zones[i];

            // Pushed clear by the zone's own half-extent, so the rectangle does
            // not swallow a wall. A zone with a building inside it is not
            // unfair, but it is a rectangle whose edge the player cannot walk,
            // and the edge is where the enemies come from.
            Vector2 centre = PushOutOfBlocks(plan.Centre, blocks,
                                             Mathf.Max(plan.HalfExtent.X, plan.HalfExtent.Y) * 0.5f);

            _zones[i] = plan with { Centre = centre };

            var zone = new DangerZone
            {
                Name = $"Zone{i}",
                Position = Terrain.Plant(new Vector3(centre.X, 0.0f, centre.Y)),
                HalfExtent = plan.HalfExtent,
                Kind = (int)plan.Kind,
                Tier = plan.Tier,
                HoldSeconds = plan.HoldSeconds,
                PurgeKills = plan.PurgeKills,
                Rolls = plan.Rolls,
                Rounds = plan.Rounds,
                SpawnRate = plan.SpawnRate,
                OpeningBurst = plan.OpeningBurst,
                Title = plan.Title,
            };

            zone.AddChild(BuildZoneMarker(plan));
            parent.AddChild(zone);
        }
    }

    /// The rectangle on the ground.
    ///
    /// `zone_marker.gdshader`, not `ground_marker`. The shared one is radial,
    /// which is correct for the extraction pads and the burning ground and
    /// produces a soft elliptical blob with no visible edge when stretched over
    /// 26 by 20 metres. That was the first version and it failed at the only job
    /// this has: a zone works by enemies arriving at the perimeter and the player
    /// choosing whether to cross it, so a boundary nobody can see is a harder
    /// fight with the information removed.
    ///
    /// Dormant colours are cold and dim — an unwoken zone should read as
    /// somewhere to consider, not somewhere already on fire.
    private static MeshInstance3D BuildZoneMarker(ZonePlan plan)
    {
        Material chosen = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.45f, 0.50f, 0.62f, 0.5f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        };

        var shader = GD.Load<Shader>("res://assets/shaders/zone_marker.gdshader");
        if (shader != null)
        {
            var live = new ShaderMaterial { Shader = shader };
            live.SetShaderParameter("edge_colour", new Color(0.62f, 0.72f, 0.95f));
            live.SetShaderParameter("fill_colour", new Color(0.20f, 0.26f, 0.40f));
            // Low, because the blend is additive: this value lands on top of a
            // ground already at about half brightness, and 0.55 clipped the line
            // to pure white — which reads as a light source rather than a
            // boundary, and loses the colour that says whether the zone is awake.
            live.SetShaderParameter("strength", 0.26f);
            live.SetShaderParameter("band", 0.055f);
            live.SetShaderParameter("fill", 0.08f);
            live.SetShaderParameter("pulse_speed", 0.9f);
            live.SetShaderParameter("seed", plan.Centre.X * 0.37f + plan.Centre.Y * 0.11f);
            chosen = live;
        }

        return new MeshInstance3D
        {
            Name = "Marker",
            Mesh = new PlaneMesh { Size = plan.HalfExtent * 2.0f },

            // Clear of the ground by a couple of centimetres. Coplanar with it
            // and the two fight for the same depth, which flickers per pixel as
            // the camera moves and reads as the marker being broken.
            Position = new Vector3(0.0f, 0.03f, 0.0f),
            MaterialOverride = chosen,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
    }

    private void BuildCrates(Node3D parent, System.Collections.Generic.List<Block> blocks)
    {
        for (int i = 0; i < Biome.CrateCount; i++)
        {
            Vector2 spot;
            int guard = 0;
            do
            {
                float angle = NextFloat() * Mathf.Tau;
                float distance = SpawnClearance + NextFloat() * (Extent * 0.85f - SpawnClearance);
                spot = new Vector2(Mathf.Cos(angle) * distance, Mathf.Sin(angle) * distance);
            }
            while (InsideAnyBlock(spot, blocks, 1.2f) && guard++ < 32);

            spot = PushOutOfBlocks(spot, blocks, 1.2f);

            float depth = Mathf.Clamp(spot.Length() / Extent, 0.0f, 1.0f);

            var crate = new LootContainer
            {
                Name = $"Crate{i}",
                Position = Terrain.Plant(new Vector3(spot.X, 0.0f, spot.Y)),
                RarityBias = Mathf.Lerp(1.0f, Biome.DepthRarityBias, depth),
            };

            crate.AddChild(new MeshInstance3D
            {
                Name = "Mesh",
                Mesh = new BoxMesh { Size = new Vector3(1.0f, 0.8f, 1.0f) },
                Position = new Vector3(0.0f, 0.4f, 0.0f),
            });

            parent.AddChild(crate);
        }
    }

    private static bool InsideAnyBlock(Vector2 point, System.Collections.Generic.List<Block> blocks, float margin)
    {
        foreach (Block block in blocks)
        {
            if (Mathf.Abs(point.X - block.Center.X) < block.Half.X + margin &&
                Mathf.Abs(point.Y - block.Center.Y) < block.Half.Y + margin)
            {
                return true;
            }
        }

        return false;
    }

    /// Nudges a point clear of whatever it landed in. Rejection sampling alone
    /// can fail on a crowded map, and a crate inside a wall is unreachable
    /// rather than merely awkward.
    private static Vector2 PushOutOfBlocks(Vector2 point, System.Collections.Generic.List<Block> blocks, float margin)
    {
        for (int pass = 0; pass < 8; pass++)
        {
            bool moved = false;

            foreach (Block block in blocks)
            {
                float dx = point.X - block.Center.X;
                float dz = point.Y - block.Center.Y;
                float overlapX = block.Half.X + margin - Mathf.Abs(dx);
                float overlapZ = block.Half.Y + margin - Mathf.Abs(dz);

                if (overlapX <= 0.0f || overlapZ <= 0.0f)
                    continue;

                // Out along the shallower axis: the shortest way into the open.
                if (overlapX < overlapZ)
                    point.X += overlapX * (dx < 0.0f ? -1.0f : 1.0f);
                else
                    point.Y += overlapZ * (dz < 0.0f ? -1.0f : 1.0f);

                moved = true;
            }

            if (!moved)
                break;
        }

        return point;
    }

    private float NextFloat()
    {
        _rng ^= _rng << 13;
        _rng ^= _rng >> 7;
        _rng ^= _rng << 17;
        return (_rng >> 40) / 16777216.0f;
    }
}
