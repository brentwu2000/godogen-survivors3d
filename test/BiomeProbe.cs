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
            case 5: return RunStage(StageFurnitureStaysHome, "a place only ever puts out its own furniture");
            case 6: return RunStage(StageLightFollowsThePlace, "generating somewhere re-lights it, and the interior is one");
            case 7: return RunStage(StageNothingSpawnsInTheLens, "no place spawns the crowd inside the camera");
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

    /// A biome draws its own props and nothing else's.
    ///
    /// The mechanism is a role table: the layout picks a `PropRole` with the same
    /// rolls it always used and the biome names the `PropKind`. What that buys is
    /// that a laboratory is not a rail yard with a colour grade on it — and what
    /// it costs is a new way to be wrong, because the renderer allocates one
    /// MultiMesh per kind *the biome declared*. A prop placed outside that set is
    /// dropped: the collider is still there, so the player walks into a piece of
    /// cover that nothing draws.
    ///
    /// Two things are asserted, and the second is the one that matters:
    ///
    ///   - every biome's table is role-correct, so nothing lands in the wrong slot
    ///   - regenerating from one biome into another **replaces** the furniture
    ///
    /// The second was a real bug and not a hypothetical one. `_props` was created
    /// with `??=`, which is correct while every biome shares a set and becomes an
    /// empty arena the moment they do not — and the base screen switches biome
    /// without reloading the scene, so it is the ordinary path.
    private bool? StageFurnitureStaysHome(int tick)
    {
        bool ok = true;

        // The premise, stated so the stage cannot pass by having nothing to
        // compare. If every biome names the same furniture then "a prop stayed
        // home" is true of a system that does not work at all.
        var sets = new System.Collections.Generic.HashSet<string>();
        foreach (BiomeResource biome in BiomeBook.All)
            sets.Add(string.Join(",", biome.Kinds()));

        if (sets.Count < 2)
        {
            GD.PushError($"  all {BiomeBook.All.Length} biomes name the same props — "
                       + "this stage would pass whatever the code did");
            return false;
        }

        // Role correctness. `BiomeResource.Prop` already falls back rather than
        // returning a landmark where cover belongs, so the arena would survive
        // this — it would just quietly be the default set, which is the failure
        // that looks like success.
        foreach (BiomeResource biome in BiomeBook.All)
        {
            var roles = System.Enum.GetValues<PropRole>();
            for (int i = 0; i < roles.Length; i++)
            {
                if (biome.PropSet == null || i >= biome.PropSet.Length)
                    continue;

                var declared = (PropKind)biome.PropSet[i];
                if (PropLibrary.RoleOf(declared) == roles[i])
                    continue;

                GD.PushError($"  {biome.BiomeName} lists {declared} as its {roles[i]}, "
                           + $"but {declared} is a {PropLibrary.RoleOf(declared)}");
                ok = false;
            }
        }

        // And the arena actually swaps. Generated in each biome in turn, in the
        // same scene, which is the sequence the base screen produces.
        foreach (BiomeResource biome in BiomeBook.All)
        {
            _level!.Biome = biome;
            _level.Seed = Seed;
            _level.Generate();

            PropRenderer? props = _level.Props;
            if (props == null)
            {
                GD.PushError($"  {biome.BiomeName} generated without a prop renderer");
                ok = false;
                continue;
            }

            var owned = new System.Collections.Generic.HashSet<PropKind>(biome.Kinds());
            var strangers = new System.Collections.Generic.List<string>();
            int placed = 0;

            foreach (PropKind kind in System.Enum.GetValues<PropKind>())
            {
                int count = System.Array.IndexOf(props.Kinds, kind) >= 0 ? props.Count(kind) : 0;
                placed += count;

                if (count > 0 && !owned.Contains(kind))
                    strangers.Add($"{kind} x{count}");
            }

            // Nothing placed is the way this fails quietly: a renderer built for
            // the wrong set drops every `Add` and the arena is bare, which reads
            // on a screenshot as an open biome rather than as a broken one.
            if (placed == 0)
            {
                GD.PushError($"  {biome.BiomeName} placed no props at all");
                ok = false;
            }

            if (strangers.Count > 0)
            {
                GD.PushError($"  {biome.BiomeName} placed {string.Join(", ", strangers)}, "
                           + "which it does not own");
                ok = false;
            }

            // Something to navigate by, from either system.
            //
            // A biome can now opt out of the glTF landmarks — Cold Storage does,
            // because a transmission tower and a grain silo are outdoor objects
            // and its fog stops at twenty-four metres anyway. What it must not do
            // is end up with *nothing* tall: the landmarks are the only answer to
            // "which corner am I in" that does not involve reading the compass,
            // and an arena without one is a flat plane of repeating cover where
            // crossing fifty metres feels like standing still.
            //
            // Counted across both systems on purpose. The lab has no glTF
            // landmark and four gantry cranes, and that is a place with beacons.
            int beacons = _level.Landmarks.Count;
            foreach (PropKind kind in props.Kinds)
            {
                if (PropLibrary.IsLandmark(kind))
                    beacons += props.Count(kind);
            }

            if (beacons < 3)
            {
                GD.PushError($"  {biome.BiomeName} has {beacons} thing(s) tall enough to steer by "
                           + "— nothing says which corner of the arena you are in");
                ok = false;
            }

            GD.Print($"  {biome.BiomeName}: {placed} props from "
                   + $"{string.Join("/", System.Array.ConvertAll(biome.Kinds(), k => k.ToString()))}"
                   + $", {beacons} beacon(s)");
        }

        return ok;
    }

    /// Generating in a place applies that place's light, and the interior is
    /// actually interior.
    ///
    /// Two assertions, and the split matters. The first is mechanical: after
    /// `Generate`, the sun and the fog in the scene are the biome's numbers and
    /// not the ones `BuildMain` baked in. It is the kind of thing that fails
    /// silently — a `Relight` that never ran leaves a perfectly lit arena that
    /// happens to be the wrong arena's lighting, which is invisible in a
    /// screenshot and invisible in every other probe.
    ///
    /// The second is about content: `Cold Storage` has to be measurably enclosed.
    /// A biome resource that carries fog fields and sets them to the outdoor
    /// defaults would pass the first assertion completely and still be a field
    /// with partitions standing in it.
    private bool? StageLightFollowsThePlace(int tick)
    {
        Node? parent = _level?.GetParent();
        var sun = parent?.GetNodeOrNull<DirectionalLight3D>("Sun");
        var world = parent?.GetNodeOrNull<WorldEnvironment>("Environment");

        if (sun == null || world?.Environment is not Godot.Environment env)
        {
            GD.PushError("  the scene has no Sun or no Environment");
            return false;
        }

        bool ok = true;
        BiomeResource? interior = null;

        foreach (BiomeResource biome in BiomeBook.All)
        {
            _level!.Biome = biome;
            _level.Seed = Seed;
            _level.Generate();

            bool applied = Mathf.Abs(sun.LightEnergy - biome.SunEnergy) < 0.001f
                        && Mathf.Abs(sun.RotationDegrees.X - biome.SunAngleDegrees.X) < 0.01f
                        && Mathf.Abs(env.FogDepthEnd - biome.FogEnd) < 0.01f
                        && Mathf.Abs(env.AmbientLightEnergy - biome.AmbientEnergy) < 0.001f;

            if (!applied)
            {
                GD.PushError($"  {biome.BiomeName}: asked for sun {biome.SunEnergy:F2} at "
                           + $"{biome.SunAngleDegrees.X:F0}° and fog to {biome.FogEnd:F0} m, "
                           + $"got {sun.LightEnergy:F2} at {sun.RotationDegrees.X:F0}° "
                           + $"and {env.FogDepthEnd:F0} m");
                ok = false;
            }

            // The one that is meant to be indoors, found by name rather than by
            // index so reordering the book does not quietly test nothing.
            if (biome.BiomeName == "Cold Storage")
                interior = biome;

            GD.Print($"  {biome.BiomeName}: sun {biome.SunEnergy:F2} at {biome.SunAngleDegrees.X:F0}°, "
                   + $"fog {biome.FogBegin:F0}–{biome.FogEnd:F0} m");
        }

        if (interior == null)
        {
            GD.PushError("  no biome named Cold Storage — the interior half of this stage tested nothing");
            return false;
        }

        // What "indoors" has to mean, in numbers. Overhead sun, and you cannot
        // see as far as you can outdoors.
        float outdoorFog = 0.0f;
        foreach (BiomeResource biome in BiomeBook.All)
        {
            if (biome != interior)
                outdoorFog = Mathf.Max(outdoorFog, biome.FogEnd);
        }

        if (interior.SunAngleDegrees.X > -70.0f)
        {
            GD.PushError($"  Cold Storage's sun is at {interior.SunAngleDegrees.X:F0}° — "
                       + "a raking sun paints long shadows, which is a statement about a horizon");
            ok = false;
        }

        if (interior.FogEnd > outdoorFog * 0.8f)
        {
            GD.PushError($"  Cold Storage sees {interior.FogEnd:F0} m against {outdoorFog:F0} m "
                       + "outdoors — that is not a room");
            ok = false;
        }

        GD.Print($"  interior: sun at {interior.SunAngleDegrees.X:F0}°, sees {interior.FogEnd:F0} m "
               + $"against {outdoorFog:F0} m outdoors");

        return ok;
    }

    /// No biome can put an arrival behind the camera.
    ///
    /// **This was live for five phases and nothing saw it.** The spawn ring
    /// starts at twelve metres, the camera stands eleven and a half behind the
    /// player, and `SpawnRingScale` multiplies the first without knowing about
    /// the second — so Old Town at 0.78 has been spawning enemies 2.3 m inside
    /// the lens since it shipped. What it looks like is a two-metre body across
    /// the corner of the screen, over the HUD, with no indication of what it is;
    /// what it looked like in a screenshot is nothing, because every capture
    /// taken since was of the rail yard, where the scale is 1.0.
    ///
    /// Asked of the real `Horde` rather than of the numbers: `ApplyBiome` is
    /// called once per biome and the resulting ring is measured. That is the code
    /// the game runs, and a clamp that was written but never reached would pass a
    /// test of the formula and fail this one.
    private bool? StageNothingSpawnsInTheLens(int tick)
    {
        float standoff = _horde!.CameraStandoff();
        float floor = Horde.SpawnFloor(standoff);

        bool ok = true;
        int clamped = 0;

        foreach (BiomeResource biome in BiomeBook.All)
        {
            _horde.ApplyBiome(biome);

            if (_horde.SpawnRingMin < floor - 0.001f)
            {
                GD.PushError($"  {biome.BiomeName} spawns from {_horde.SpawnRingMin:F1} m with the "
                           + $"camera at {standoff:F1} m — inside the lens");
                ok = false;
            }

            // The ring has to stay a ring.
            if (_horde.SpawnRingMax <= _horde.SpawnRingMin)
            {
                GD.PushError($"  {biome.BiomeName}: ring {_horde.SpawnRingMin:F1}–"
                           + $"{_horde.SpawnRingMax:F1} m is inside out");
                ok = false;
            }

            // How many places the clamp actually saves. Reported rather than
            // asserted, because a table that never needed it is a fine table —
            // but see below.
            if (biome.SpawnRingScale < 1.0f)
                clamped++;

            GD.Print($"  {biome.BiomeName}: x{biome.SpawnRingScale:F2} -> "
                   + $"{_horde.SpawnRingMin:F1}–{_horde.SpawnRingMax:F1} m");
        }

        // The premise. If no biome asks for a ring tighter than the camera, this
        // whole stage passes on a clamp that is never reached — and it would go
        // on passing after somebody deleted it.
        if (clamped == 0)
        {
            GD.PushError("  no biome pulls the ring in at all, so nothing here exercised the clamp");
            ok = false;
        }

        GD.Print($"  camera at {standoff:F1} m, floor {floor:F1} m, {clamped} of "
               + $"{BiomeBook.All.Length} places needed it");

        // Put it back the way the scene had it, or every stage after this one is
        // measuring a horde that belongs to whichever biome happened to be last.
        _horde.ApplyBiome(BiomeBook.Load(GameSession.Biome));
        return ok;
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
