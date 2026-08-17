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

    /// How many of the pads will actually open. Fewer than PadCount is the
    /// point: the walk home is a decision only when some of the exits are shut.
    [Export] public int OpenPadCount { get; set; } = 2;

    /// Cleared radius around the spawn, so a run never starts inside a wall.
    [Export] public float SpawnClearance { get; set; } = 8.0f;

    /// Multiplies the weight of rarer items once per rarity step, scaled by how
    /// far the crate is from the spawn. Depth has to pay, or the whole map past
    /// the first ring is risk with no reason.
    [Export] public float DepthRarityBias { get; set; } = 1.9f;

    /// Blocks removed by the last generation to open a sealed objective. Zero on
    /// most seeds; a probe watches it to confirm the rescue is not dead code.
    public int CarvedLastRun { get; private set; }

    private ulong _rng;

    public override void _Ready() => Generate();

    /// Public so a probe can regenerate in place and compare: reproducing a
    /// layout from its seed is a property worth testing, not an implementation
    /// detail.
    public void Generate()
    {
        if (Seed == 0)
            Seed = (ulong)Time.GetTicksUsec() | 1UL;

        _rng = Seed;

        Node3D obstacles = Container("Obstacles");
        Node3D crates = Container("LootContainers");
        Node3D pads = Container("ExtractionZones");

        Clear(obstacles);
        Clear(crates);
        Clear(pads);

        var blocks = new System.Collections.Generic.List<(Vector2 Center, Vector2 Half)>();
        BuildCover(obstacles, blocks);
        BuildPads(pads, blocks);
        BuildCrates(crates, blocks);

        CarvedLastRun = EnsureReachable(obstacles, blocks, pads, crates);

        GD.Print($"level seed {Seed}: {blocks.Count} blocks, {crates.GetChildCount()} crates, " +
                 $"{OpenPadCount}/{pads.GetChildCount()} pads will open, {CarvedLastRun} carved");
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
    private int EnsureReachable(Node3D obstacleParent,
                                System.Collections.Generic.List<(Vector2 Center, Vector2 Half)> blocks,
                                Node3D pads, Node3D crates)
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
                int removed = CarveTowards(obstacleParent, blocks, target);

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
    private bool Reachable(System.Collections.Generic.List<(Vector2 Center, Vector2 Half)> blocks, Vector2 target)
    {
        var horde = GetParent().GetNodeOrNull<Horde>("Horde");
        float extent = horde?.ArenaExtent ?? 60.0f;
        float inflate = horde?.SeparationRadius ?? 0.75f;

        var field = new FlowField(Vector2.Zero, extent, 1.5f);
        foreach ((Vector2 center, Vector2 half) in blocks)
            field.BlockBox(center, half + Vector2.One * inflate);

        field.Rebuild(new Vector3(target.X, 0.0f, target.Y));

        // Zero at the spawn means the sweep never reached it. Sampling one cell
        // is enough because the field only assigns a direction to cells it
        // actually visited.
        return field.Sample(Vector3.Zero) != Vector2.Zero;
    }

    /// Removes every block the straight line from spawn to the target passes
    /// through. Returns how many went.
    private int CarveTowards(Node3D parent,
                             System.Collections.Generic.List<(Vector2 Center, Vector2 Half)> blocks,
                             Vector2 target)
    {
        // Wide enough to survive being narrowed twice. The field inflates every
        // obstacle by the enemy radius and then rounds the footprint outward to
        // whole cells, so a corridor cleared to the width a body needs arrives
        // at the field as no corridor at all. Two clear cells after both, or the
        // rescue reports success and the pad is still sealed.
        const float corridorHalfWidth = 2.6f;

        var survivors = new System.Collections.Generic.List<(Vector2, Vector2)>(blocks.Count);
        var doomed = new System.Collections.Generic.List<Vector2>();

        foreach ((Vector2 center, Vector2 half) in blocks)
        {
            if (SegmentHitsBox(Vector2.Zero, target, center, half + Vector2.One * corridorHalfWidth))
                doomed.Add(center);
            else
                survivors.Add((center, half));
        }

        if (doomed.Count == 0)
            return 0;

        // Matched by position, not by name. Names carry the index a block had
        // when it was created, and the list is compacted by every carve — after
        // the first pass those two numbers are different, which would delete
        // whatever block happened to inherit the name.
        foreach (Vector2 center in doomed)
        {
            foreach (Node child in parent.GetChildren())
            {
                if (child is not Node3D node)
                    continue;

                if (Mathf.Abs(node.Position.X - center.X) > 0.001f ||
                    Mathf.Abs(node.Position.Z - center.Y) > 0.001f)
                {
                    continue;
                }

                parent.RemoveChild(node);
                node.QueueFree();
                break;
            }
        }

        blocks.Clear();
        blocks.AddRange(survivors);
        return doomed.Count;
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

    private Node3D Container(string name)
    {
        var existing = GetParent().GetNodeOrNull<Node3D>(name);
        if (existing != null)
            return existing;

        var created = new Node3D { Name = name };
        GetParent().AddChild(created);
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

    /// One tile per grid cell, skipping the cell the player starts in. Cover is
    /// what makes the flow field worth having and what a player retreats behind,
    /// so an empty map is not a simpler map — it is a different game.
    private void BuildCover(Node3D parent, System.Collections.Generic.List<(Vector2, Vector2)> blocks)
    {
        float cell = Extent * 2.0f / GridSize;

        for (int gz = 0; gz < GridSize; gz++)
        for (int gx = 0; gx < GridSize; gx++)
        {
            var center = new Vector2(
                -Extent + cell * (gx + 0.5f),
                -Extent + cell * (gz + 0.5f));

            if (center.Length() < SpawnClearance + cell * 0.5f)
                continue;

            switch ((int)(NextFloat() * 4.0f))
            {
                case 0:
                    break;   // open ground; a map with no gaps has no routes

                case 1:
                    Cluster(parent, blocks, center, cell, count: 3, size: 4.0f);
                    break;

                case 2:
                    Corridor(parent, blocks, center, cell);
                    break;

                default:
                    Cluster(parent, blocks, center, cell, count: 5, size: 2.0f);
                    break;
            }
        }
    }

    private void Cluster(Node3D parent, System.Collections.Generic.List<(Vector2, Vector2)> blocks,
                         Vector2 center, float cell, int count, float size)
    {
        for (int i = 0; i < count; i++)
        {
            var offset = new Vector2(
                (NextFloat() - 0.5f) * (cell - size),
                (NextFloat() - 0.5f) * (cell - size));

            var half = new Vector2(size * 0.5f * (0.6f + NextFloat() * 0.8f),
                                   size * 0.5f * (0.6f + NextFloat() * 0.8f));

            AddBlock(parent, blocks, center + offset, half);
        }
    }

    /// A long wall with a gap in it. The gap is the whole point — a solid wall
    /// is a boundary, and a wall with a way through is a decision.
    private void Corridor(Node3D parent, System.Collections.Generic.List<(Vector2, Vector2)> blocks,
                          Vector2 center, float cell)
    {
        bool horizontal = NextFloat() < 0.5f;
        float length = cell * 0.38f;
        float thickness = 1.2f;
        float gap = 3.0f;

        for (int side = -1; side <= 1; side += 2)
        {
            Vector2 offset = horizontal
                ? new Vector2(side * (length * 0.5f + gap * 0.5f), 0.0f)
                : new Vector2(0.0f, side * (length * 0.5f + gap * 0.5f));

            Vector2 half = horizontal
                ? new Vector2(length * 0.5f, thickness * 0.5f)
                : new Vector2(thickness * 0.5f, length * 0.5f);

            AddBlock(parent, blocks, center + offset, half);
        }
    }

    private static void AddBlock(Node3D parent, System.Collections.Generic.List<(Vector2, Vector2)> blocks,
                                 Vector2 center, Vector2 half)
    {
        const float height = 3.0f;
        var size = new Vector3(half.X * 2.0f, height, half.Y * 2.0f);

        var body = new StaticBody3D
        {
            Name = $"Block{blocks.Count}",
            Position = new Vector3(center.X, height * 0.5f, center.Y),
        };
        body.AddChild(new MeshInstance3D { Name = "Mesh", Mesh = new BoxMesh { Size = size } });
        body.AddChild(new CollisionShape3D { Name = "Collision", Shape = new BoxShape3D { Size = size } });

        parent.AddChild(body);
        blocks.Add((center, half));
    }

    /// Pads sit far out and in different directions, so which one opens changes
    /// where the run ends. They are hidden until the director reveals them —
    /// knowing the way out from the first second would make the map a corridor.
    private void BuildPads(Node3D parent, System.Collections.Generic.List<(Vector2, Vector2)> blocks)
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
                Position = new Vector3(spot.X, 0.0f, spot.Y),
                Open = false,
                Visible = false,
                WillOpen = (i - firstOpen + PadCount) % PadCount < opening,
            };

            var material = new StandardMaterial3D
            {
                AlbedoColor = new Color(0.2f, 0.85f, 0.4f, 0.55f),
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                DepthDrawMode = BaseMaterial3D.DepthDrawModeEnum.Disabled,
            };

            zone.AddChild(new MeshInstance3D
            {
                Name = "Pad",
                Mesh = new CylinderMesh { TopRadius = 3.0f, BottomRadius = 3.0f, Height = 0.05f },
                MaterialOverride = material,
                Position = new Vector3(0.0f, 0.03f, 0.0f),
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            });

            parent.AddChild(zone);
        }
    }

    /// Crates get better the further out they are. Bias is applied per rarity
    /// step, so the far corners are where the serum actually lives.
    private void BuildCrates(Node3D parent, System.Collections.Generic.List<(Vector2, Vector2)> blocks)
    {
        for (int i = 0; i < CrateCount; i++)
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
                Position = new Vector3(spot.X, 0.0f, spot.Y),
                RarityBias = Mathf.Lerp(1.0f, DepthRarityBias, depth),
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

    private static bool InsideAnyBlock(Vector2 point, System.Collections.Generic.List<(Vector2 Center, Vector2 Half)> blocks,
                                       float margin)
    {
        foreach ((Vector2 center, Vector2 half) in blocks)
        {
            if (Mathf.Abs(point.X - center.X) < half.X + margin &&
                Mathf.Abs(point.Y - center.Y) < half.Y + margin)
            {
                return true;
            }
        }

        return false;
    }

    /// Nudges a point clear of whatever it landed in. Rejection sampling alone
    /// can fail on a crowded map, and a crate inside a wall is unreachable
    /// rather than merely awkward.
    private static Vector2 PushOutOfBlocks(Vector2 point, System.Collections.Generic.List<(Vector2 Center, Vector2 Half)> blocks,
                                           float margin)
    {
        for (int pass = 0; pass < 8; pass++)
        {
            bool moved = false;

            foreach ((Vector2 center, Vector2 half) in blocks)
            {
                float dx = point.X - center.X;
                float dz = point.Y - center.Y;
                float overlapX = half.X + margin - Mathf.Abs(dx);
                float overlapZ = half.Y + margin - Mathf.Abs(dz);

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
