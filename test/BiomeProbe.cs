using Godot;

/// Checks that a second place to fight in is a second question, not a reskin.
///
///   godot --headless --script test/BiomeProbe.cs
///
/// Exit code is the verdict. Two things can go wrong here and only one of them
/// is obvious. The obvious one is that the biomes are not actually different —
/// easy to see, easy to fix. The quiet one is that a dense layout seals the map:
/// cover generation and the flow field have collided before (Phase 9, where a
/// probe passed because the enemy it was watching could not move at all), and
/// "no enemy reached the player" is indistinguishable from "the enemies were
/// walled in" unless something asks.
public partial class BiomeProbe : SceneTree
{
    private Node? _scene;
    private LevelGenerator? _level;
    private Horde? _horde;
    private Player? _player;

    private int _stage;
    private int _stageTick;
    private bool _failed;

    /// One seed for every measurement. The comparison is between biomes, so
    /// anything else that could differ has to be held still — with a free seed
    /// the two layouts differ for two reasons and neither number means anything.
    private const ulong Seed = 0x51E5D0A7UL;

    public override void _Initialize()
    {
        var scene = GD.Load<PackedScene>("res://scenes/Main.tscn")?.Instantiate();
        if (scene == null)
        {
            GD.PushError("Missing res://scenes/Main.tscn");
            Quit(1);
            return;
        }

        var meta = scene.GetNodeOrNull<MetaManager>("MetaManager");
        if (meta != null)
            meta.Ephemeral = true;

        var level = scene.GetNodeOrNull<LevelGenerator>("Level");
        if (level != null)
            level.Seed = Seed;

        GameSession.LaunchedFromBase = false;
        GetRoot().AddChild(scene);
        _scene = scene;
    }

    public override bool _PhysicsProcess(double delta)
    {
        if (_stage == 0 && _stageTick == 0)
        {
            _level = _scene?.GetNodeOrNull<LevelGenerator>("Level");
            _horde = _scene?.GetNodeOrNull<Horde>("Horde");
            _player = _scene?.GetNodeOrNull<Player>("Player");

            if (_level == null || _horde == null || _player == null)
            {
                GD.PushError("PROBE FAILED - scene is missing a required node");
                Quit(1);
                return true;
            }

            _scene?.GetNodeOrNull<RunDirector>("RunDirector")?.SetPhysicsProcess(false);
            _player.GetNode<WeaponHandler>("WeaponHandler").HoldFire = true;
        }

        _stageTick++;

        switch (_stage)
        {
            case 0: return RunStage(StageTableIsSane, "every biome loads and none of them is a difficulty setting");
            case 1: return RunStage(StageDenseIsDense, "one place has cover everywhere and the other has sight lines");
            case 2: return RunStage(StageLootTrades, "the emptier place pays better for the walk");
            case 3: return RunStage(StageDenseIsNotSealed, "the crowd still gets through the dense layout");
            case 4: return RunStage(StagePlayerCanCross, "and so can the player, to every pad");
            default:
                GD.Print(_failed ? "PROBE FAILED" : "PROBE OK");
                Quit(_failed ? 1 : 0);
                return true;
        }
    }

    private bool RunStage(System.Func<int, bool?> stage, string label)
    {
        bool? verdict = stage(_stageTick);
        if (verdict == null)
            return false;

        GD.Print($"{label}: {(verdict.Value ? "ok" : "FAILED")}");
        _failed |= !verdict.Value;
        _stage++;
        _stageTick = 0;
        return false;
    }

    /// Data checks, reported as data problems rather than as a strange layout
    /// three stages later.
    private bool? StageTableIsSane(int tick)
    {
        BiomeResource[] all = BiomeBook.All;
        if (all.Length < 2)
        {
            GD.PushError($"  only {all.Length} biome(s) — run BuildBiomes.cs");
            return false;
        }

        bool ok = true;
        var names = new System.Collections.Generic.List<string>();

        foreach (BiomeResource biome in all)
        {
            names.Add(biome.BiomeName);

            if (biome.WeightTotal <= 0.0f)
            {
                GD.PushError($"  {biome.BiomeName} has no tile weights — every cell would be open ground");
                ok = false;
            }

            if (biome.CrateCount <= 0)
            {
                GD.PushError($"  {biome.BiomeName} has no crates — nothing to walk out with");
                ok = false;
            }

            if (string.IsNullOrEmpty(biome.Blurb))
            {
                // The base screen prints this. A biome that cannot say what it
                // costs you is a name, and a name is not a choice.
                GD.PushError($"  {biome.BiomeName} has no blurb");
                ok = false;
            }
        }

        GD.Print($"  {string.Join(", ", names)}");
        return ok;
    }

    /// The phase's actual claim, measured two ways.
    ///
    /// Block count alone is not enough: a biome could have many blocks that are
    /// all tiny and still leave every line of fire open. So the second number is
    /// how far a shot travels before it hits something, averaged over rays from
    /// the middle — which is the thing a pierce build actually cares about and
    /// the thing a thorns build is hiding from.
    private bool? StageDenseIsDense(int tick)
    {
        (int Blocks, float Sight, int Crates) dense = Measure("Old Town");
        (int Blocks, float Sight, int Crates) open = Measure("The Flats");

        GD.Print($"  Old Town:  {dense.Blocks} blocks, {dense.Sight:F1} m average line of fire");
        GD.Print($"  The Flats: {open.Blocks} blocks, {open.Sight:F1} m average line of fire");

        return dense.Blocks > open.Blocks * 1.5f && open.Sight > dense.Sight * 1.4f;
    }

    /// Neither place is simply better. The dense one is loot-rich and short on
    /// shooting room; the open one has fewer crates that are worth more, further
    /// away, across ground with nothing to hide behind.
    private bool? StageLootTrades(int tick)
    {
        (int Blocks, float Sight, int Crates) dense = Measure("Old Town");
        float denseBias = BiomeNamed("Old Town").DepthRarityBias;

        (int Blocks, float Sight, int Crates) open = Measure("The Flats");
        float openBias = BiomeNamed("The Flats").DepthRarityBias;

        GD.Print($"  Old Town {dense.Crates} crates at x{denseBias:F1} depth bias, " +
                 $"The Flats {open.Crates} at x{openBias:F1}");

        return dense.Crates > open.Crates && openBias > denseBias;
    }

    /// The Phase 9 failure, asked directly.
    ///
    /// A wall of cover that seals the arena would make the densest biome the
    /// safest one, and every symptom of it looks like good news: fewer hits, more
    /// survival time, a calmer frame. So this puts enemies out at the ring and
    /// asserts they close — on the layout most able to trap them.
    private bool? StageDenseIsNotSealed(int tick)
    {
        if (tick == 1)
        {
            Measure("Old Town");
            _horde!.Pool.Clear();

            _player!.GlobalPosition = Vector3.Zero;

            // A ring, so no single unlucky corner decides the verdict.
            for (int i = 0; i < 24; i++)
            {
                float angle = i * Mathf.Tau / 24.0f;
                _horde.Spawn(new Vector3(Mathf.Cos(angle), 0.0f, Mathf.Sin(angle)) * 34.0f, 0);
            }

            _startDistance = AverageDistance();
            return null;
        }

        // Twenty seconds. A walker does 2.4 m/s in the open, so 34 m is fourteen
        // seconds of unobstructed approach — the first version of this stage gave
        // it four and concluded the map was sealed, which is a statement about
        // arithmetic rather than about the map. Dense cover is *supposed* to
        // roughly halve that; the question is whether it stops it.
        if (tick < 1200)
            return null;

        float now = AverageDistance();
        int close = 0;
        for (int i = 0; i < _horde!.Pool.Count; i++)
        {
            if (_horde.Pool.Position[i].Length() < 12.0f)
                close++;
        }

        GD.Print($"  24 walkers from 34 m through Old Town, 20 s: average {_startDistance:F1} m -> " +
                 $"{now:F1} m, {close} of {_horde.Pool.Count} within 12 m");

        // Closed by half and at least a quarter of them arrived. A sealed map
        // fails both; a merely slow one fails neither.
        return now < _startDistance * 0.5f && close >= _horde.Pool.Count / 4;
    }

    private float _startDistance;

    /// The half the enemy stage cannot answer.
    ///
    /// Enemies are not physics bodies — they follow a flow field and pass through
    /// nothing, so "the crowd gets through" says only that a route exists on the
    /// grid. The player is a `CharacterBody3D` that collides, and reaching the
    /// extraction pad is the one thing a run cannot do without.
    ///
    /// This is the stage the phase actually needed: the dense biome shipped with
    /// a 2.2 m corridor gap and the play-test bot reported "could not reach
    /// extraction, still 49 m away". The geometry was fine — the player's body is
    /// 0.35 m — but every pathfinder over the 1.5 m navigation grid inflates
    /// obstacles, and a doorway a cell and a half wide is one some of them decide
    /// is not there. A map whose exit is unreachable is not a hard map.
    private bool? StagePlayerCanCross(int tick)
    {
        if (tick == 1)
        {
            Measure("Old Town");
            _horde!.Pool.Clear();
            return null;
        }

        // One frame for the level's bodies to enter the physics world; a route
        // computed against a world that has not settled would find open ground
        // where the walls are.
        if (tick < 3)
            return null;

        Node? pads = _scene?.GetNodeOrNull("ExtractionZones");
        if (pads == null || pads.GetChildCount() == 0)
        {
            GD.PushError("  no extraction pads");
            return false;
        }

        // Inflated by the player's own radius and a margin, not by the enemy
        // separation radius: this asks whether a body of that size fits, and
        // asking with a different number answers a different question.
        var boxes = new System.Collections.Generic.List<(Vector2, Vector2)>();
        Node? obstacles = _scene?.GetNodeOrNull("Obstacles");
        if (obstacles != null)
        {
            foreach (Node child in obstacles.GetChildren())
            {
                if (child is not Node3D body ||
                    body.GetNodeOrNull<CollisionShape3D>("Collision")?.Shape is not BoxShape3D box)
                {
                    continue;
                }

                boxes.Add((new Vector2(body.Position.X, body.Position.Z),
                           new Vector2(box.Size.X * 0.5f + 0.55f, box.Size.Z * 0.5f + 0.55f)));
            }
        }

        int reached = 0;
        int wanted = 0;
        var unreachable = new System.Collections.Generic.List<string>();

        foreach (Node child in pads.GetChildren())
        {
            // Only the ones that will open. Some pads are decoys and the
            // generator never promises a route to those — asserting all three
            // would be the probe demanding a guarantee the game does not make,
            // which fails on correct maps and teaches nothing when it does.
            if (child is not ExtractionZone { WillOpen: true } pad)
                continue;

            wanted++;

            var field = new FlowField(Vector2.Zero, _horde!.ArenaExtent, 1.5f);
            foreach ((Vector2 center, Vector2 half) in boxes)
                field.BlockBox(center, half);

            field.Rebuild(pad.GlobalPosition);

            if (field.Sample(Vector3.Zero) != Vector2.Zero)
                reached++;
            else
                unreachable.Add(pad.Name);
        }

        GD.Print($"  a 0.35 m body routes from spawn to {reached} of {wanted} pads that open " +
                 $"({pads.GetChildCount()} placed), through {boxes.Count} blocks" +
                 (unreachable.Count > 0 ? $" (unreachable: {string.Join(", ", unreachable)})" : ""));

        return wanted > 0 && reached == wanted;
    }

    private float AverageDistance()
    {
        if (_horde!.Pool.Count == 0)
            return 0.0f;

        float total = 0.0f;
        for (int i = 0; i < _horde.Pool.Count; i++)
            total += _horde.Pool.Position[i].Length();

        return total / _horde.Pool.Count;
    }

    /// Regenerates in the named biome, on the pinned seed, and reports what came
    /// out. Same scene throughout: two processes would differ in more than the
    /// biome, which is the thing being isolated.
    private (int Blocks, float Sight, int Crates) Measure(string name)
    {
        _level!.Biome = BiomeNamed(name);
        _level.Seed = Seed;
        _level.Generate();

        Node? obstacles = _scene?.GetNodeOrNull("Obstacles");
        Node? crates = _scene?.GetNodeOrNull("LootContainers");

        return (obstacles?.GetChildCount() ?? 0, AverageSightLine(), crates?.GetChildCount() ?? 0);
    }

    private static BiomeResource BiomeNamed(string name)
    {
        foreach (BiomeResource biome in BiomeBook.All)
        {
            if (biome.BiomeName == name)
                return biome;
        }

        GD.PushError($"  no biome named {name}");
        return new BiomeResource();
    }

    /// How far a shot gets, averaged over rays from points across the arena.
    ///
    /// **Not** from the middle. The first version fired 64 rays from the origin
    /// and measured 33.7 m in the dense biome against 39.5 m in the open one,
    /// with ten times the cover — because the origin sits in the spawn clearance,
    /// which is forced open in every biome, and rays radiating from a single
    /// point spread apart fast enough to miss almost everything. It was measuring
    /// the one place on the map guaranteed to look the same.
    ///
    /// Sampling from many origins is what the player experiences: they are
    /// somewhere in the arena, and the question is whether a shot from there
    /// reaches anything.
    ///
    /// Marched rather than raycast, because the obstacles are static bodies whose
    /// collision the physics server has not necessarily settled in the frame they
    /// were created — and a ray that reports "nothing there" because the world is
    /// one tick behind would call the densest map the most open one.
    private float AverageSightLine()
    {
        Node? obstacles = _scene?.GetNodeOrNull("Obstacles");
        if (obstacles == null)
            return 0.0f;

        var boxes = new System.Collections.Generic.List<(Vector2 Center, Vector2 Half)>();
        foreach (Node child in obstacles.GetChildren())
        {
            if (child is not Node3D body)
                continue;

            var shape = body.GetChildOrNull<CollisionShape3D>(0);
            if (shape?.Shape is not BoxShape3D box)
                continue;

            Vector3 size = box.Size;
            Vector3 at = body.GlobalPosition;
            boxes.Add((new Vector2(at.X, at.Z), new Vector2(size.X * 0.5f, size.Z * 0.5f)));
        }

        const int Origins = 24;
        const int Rays = 16;
        const float MaxRange = 40.0f;
        const float Step = 0.5f;

        // A fixed lattice rather than random points, so the number is the same
        // every run and a change in it means a change in the map.
        float total = 0.0f;
        int samples = 0;

        for (int o = 0; o < Origins; o++)
        {
            float spiral = o * 2.39996f;                       // golden angle
            float radius = 14.0f + (o / (float)Origins) * 30.0f;
            var from = new Vector2(Mathf.Cos(spiral) * radius, Mathf.Sin(spiral) * radius);

            // A sample point inside a block would report zero in every direction
            // and count the densest biome's cover twice.
            if (Inside(boxes, from))
                continue;

            for (int r = 0; r < Rays; r++)
            {
                float angle = r * Mathf.Tau / Rays;
                var direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                float travelled = MaxRange;

                for (float t = Step; t <= MaxRange; t += Step)
                {
                    if (Inside(boxes, from + direction * t))
                    {
                        travelled = t;
                        break;
                    }
                }

                total += travelled;
                samples++;
            }
        }

        return samples > 0 ? total / samples : 0.0f;
    }

    private static bool Inside(System.Collections.Generic.List<(Vector2 Center, Vector2 Half)> boxes,
                               Vector2 point)
    {
        foreach ((Vector2 center, Vector2 half) in boxes)
        {
            if (Mathf.Abs(point.X - center.X) <= half.X && Mathf.Abs(point.Y - center.Y) <= half.Y)
                return true;
        }

        return false;
    }
}
