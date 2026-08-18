using Godot;

/// Checks the generated arena: that a seed reproduces it, that nothing is placed
/// somewhere it cannot be reached, and that the horde is routing around the
/// walls the screen is showing.
///
///   godot --headless --script test/LevelProbe.cs
///
/// Exit code is the verdict. The last of those is the one worth having: the
/// flow field bakes obstacles once at startup, so a level generated after that
/// bake looks completely correct and lets every enemy walk through every wall.
public partial class LevelProbe : SceneTree
{
    private const ulong SeedA = 0x51E5D0A7UL;
    private const ulong SeedB = 0x9E3779B9UL;

    private Node? _scene;
    private LevelGenerator? _level;
    private Horde? _horde;
    private Player? _player;
    private RunDirector? _director;

    private int _stage;
    private int _stageTick;
    private bool _failed;

    public override void _Initialize()
    {
        string[] args = OS.GetCmdlineUserArgs();
        ulong seed = args.Length > 0 && args[0] == "b" ? SeedB : SeedA;

        var scene = GD.Load<PackedScene>("res://scenes/Main.tscn")?.Instantiate();
        if (scene == null)
        {
            GD.PushError("Missing res://scenes/Main.tscn");
            Quit(1);
            return;
        }

        var level = scene.GetNodeOrNull<LevelGenerator>("Level");
        if (level != null)
            level.Seed = seed;

        var meta = scene.GetNodeOrNull<MetaManager>("MetaManager");
        if (meta != null)
            meta.Ephemeral = true;

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
            _director = _scene?.GetNodeOrNull<RunDirector>("RunDirector");

            if (_level == null || _horde == null || _player == null || _director == null)
            {
                GD.PushError($"PROBE FAILED — level={_level != null} horde={_horde != null} " +
                             $"player={_player != null} director={_director != null}");
                Quit(1);
                return true;
            }

            _director.SetPhysicsProcess(false);
            _player.GetNodeOrNull<WeaponHandler>("WeaponHandler")?.SetPhysicsProcess(false);
        }

        _stageTick++;

        switch (_stage)
        {
            case 0: return RunStage(StageDeterministic, "one seed, one layout");
            case 1: return RunStage(StagePlacement, "nothing is placed inside a wall");
            case 2: return RunStage(StagePads, "the level picks which exits open");
            case 3: return RunStage(StageReachable, "every open pad is reachable from spawn");
            case 4: return RunStage(StageWallsAreBaked, "the horde routes around the generated walls");
            case 5: return RunStage(StageSweep, "no seed produces a sealed objective");
            case 6: return RunStage(StageGroundReadsTheLayout, "the ground says which tile you are standing on");
            default:
                GD.Print(_failed ? "PROBE FAILED" : "PROBE OK");
                Quit(_failed ? 1 : 0);
                return true;
        }
    }

    /// The floor has to agree with what is standing on it.
    ///
    /// The generator picks a tile per cell and two things consume that choice: the
    /// cover pool and the ground tint. They are read in different places and
    /// nothing ties them together, so a map that places containers on dust — or,
    /// worse, tints every cell the same and quietly stops telling the player
    /// anything — is a defect with no symptom other than the map feeling flat.
    ///
    /// Checked against the texture actually handed to the shader, not against the
    /// array it was built from: a tint that never reached the material is exactly
    /// the failure this is for.
    private bool? StageGroundReadsTheLayout(int tick)
    {
        int[] tiles = _level!.TileMap;
        if (tiles.Length != _level.GridSize * _level.GridSize)
        {
            GD.Print($"  tile map is {tiles.Length}, expected {_level.GridSize * _level.GridSize}");
            return false;
        }

        var mesh = _level.GetParent().GetNodeOrNull<MeshInstance3D>("Ground/Mesh");
        if (mesh?.MaterialOverride is not ShaderMaterial material)
        {
            GD.Print("  the ground has no shader material");
            return false;
        }

        if (material.GetShaderParameter("zones").As<Texture2D>() is not { } zones)
        {
            GD.Print("  the ground shader was never given a zone texture");
            return false;
        }

        Image image = zones.GetImage();
        bool matches = image.GetWidth() == _level.GridSize && image.GetHeight() == _level.GridSize;
        var seen = new System.Collections.Generic.HashSet<int>();

        for (int gz = 0; gz < _level.GridSize && matches; gz++)
        {
            for (int gx = 0; gx < _level.GridSize; gx++)
            {
                int tile = tiles[gz * _level.GridSize + gx];
                seen.Add(tile);

                Color expected = LevelGenerator.TintFor(tile);
                Color actual = image.GetPixel(gx, gz);

                // Rgb8, so a channel is only accurate to about 1/255.
                if (Mathf.Abs(expected.R - actual.R) > 0.01f
                    || Mathf.Abs(expected.G - actual.G) > 0.01f
                    || Mathf.Abs(expected.B - actual.B) > 0.01f)
                {
                    GD.Print($"  cell {gx},{gz} is tile {tile} but painted {actual}");
                    matches = false;
                    break;
                }
            }
        }

        // More than one kind, or the tint is doing nothing whatever it is set to.
        GD.Print($"  {_level.GridSize}x{_level.GridSize} cells, {seen.Count} distinct tiles, " +
                 $"texture {image.GetWidth()}x{image.GetHeight()}, every texel matches = {matches}");

        return matches && seen.Count >= 2;
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

    /// Regenerating from the same seed has to produce the same arena, or a
    /// capture cannot be re-shot and a probe result cannot be trusted twice.
    private bool? StageDeterministic(int tick)
    {
        string first = Fingerprint();

        // Re-run the generator in place. Same seed, same everything. The seed is
        // read back from the generator rather than named here, so this stage
        // still checks the layout the run actually started with when the probe
        // is pointed at a different one.
        ulong seed = _level!.Seed;
        _level.Generate();
        string second = Fingerprint();

        _level.Seed = seed ^ 0xA5A5A5A5UL;
        _level.Generate();
        string other = Fingerprint();

        // Back to the layout the rest of the stages will inspect.
        _level.Seed = seed;
        _level.Generate();

        GD.Print($"  same seed matches = {first == second}; a different seed differs = {second != other}");

        return first == second && second != other;
    }

    private string Fingerprint()
    {
        var text = new System.Text.StringBuilder();

        foreach (string container in new[] { "Obstacles", "LootContainers", "ExtractionZones" })
        {
            Node? parent = _scene?.GetNodeOrNull(container);
            if (parent == null)
                continue;

            foreach (Node child in parent.GetChildren())
            {
                if (child is Node3D node)
                    text.Append($"{node.Name}:{node.Position.X:F2},{node.Position.Z:F2};");
            }
        }

        return text.ToString();
    }

    /// A crate inside a wall is not a hard crate, it is an absent one.
    private bool? StagePlacement(int tick)
    {
        var blocks = Blocks();
        int buried = 0;

        foreach (Node3D thing in Placed("LootContainers"))
        {
            if (Inside(new Vector2(thing.Position.X, thing.Position.Z), blocks, 0.6f))
                buried++;
        }

        foreach (Node3D thing in Placed("ExtractionZones"))
        {
            if (Inside(new Vector2(thing.Position.X, thing.Position.Z), blocks, 2.5f))
                buried++;
        }

        GD.Print($"  {blocks.Count} blocks, {Placed("LootContainers").Count} crates, " +
                 $"{Placed("ExtractionZones").Count} pads, {buried} buried");

        return buried == 0;
    }

    /// Some exits open and some do not, and none of them is visible before the
    /// director says so.
    private bool? StagePads(int tick)
    {
        var pads = new System.Collections.Generic.List<ExtractionZone>();
        foreach (Node3D node in Placed("ExtractionZones"))
        {
            if (node is ExtractionZone pad)
                pads.Add(pad);
        }

        int willOpen = 0;
        bool allHidden = true;
        bool allClosed = true;

        foreach (ExtractionZone pad in pads)
        {
            if (pad.WillOpen)
                willOpen++;

            allHidden &= !pad.Visible;
            allClosed &= !pad.Open;
        }

        GD.Print($"  {willOpen} of {pads.Count} will open; " +
                 $"hidden before reveal = {allHidden}, closed = {allClosed}");

        return pads.Count >= 2 && willOpen >= 1 && willOpen < pads.Count && allHidden && allClosed;
    }

    /// The flow field is the game's own answer to "can you get there", so asking
    /// it is a stronger check than any geometry test written alongside the
    /// generator — it shares no code with the thing under test.
    private bool? StageReachable(int tick)
    {
        int unreachable = 0;
        int checkedPads = 0;

        foreach (Node3D node in Placed("ExtractionZones"))
        {
            if (node is not ExtractionZone pad || !pad.WillOpen)
                continue;

            checkedPads++;
            if (!Walkable(_player!.GlobalPosition, pad.GlobalPosition))
            {
                unreachable++;
                GD.Print($"    {pad.Name} at ({pad.Position.X:F1},{pad.Position.Z:F1}) is sealed off");
            }
        }

        GD.Print($"  {checkedPads} opening pads, {unreachable} unreachable from spawn");
        return checkedPads > 0 && unreachable == 0;
    }

    /// Rebuilds the field around the goal and asks whether the sweep reached the
    /// start. The field assigns a direction only to cells it actually visited,
    /// so a direction at the spawn *is* a route.
    ///
    /// This started out as a step-by-step walk, which was the wrong instrument:
    /// the distance pass is four-directional but the gradient is read
    /// eight-directionally, so following it half a metre at a time can wedge
    /// into a corner cell the field considers connected — and which a real
    /// enemy slides off, because separation is pushing on it too. The walk was
    /// reporting a sealed map that was not sealed.
    private bool Walkable(Vector3 from, Vector3 to)
    {
        _horde!.RebuildFieldAround(to);
        return _horde.SampleField(from) != Vector2.Zero;
    }

    /// The trap this whole ordering exists for. An enemy parked on the far side
    /// of a generated wall must not come straight through it — if the field was
    /// baked before the level was generated it will, and everything else here
    /// still passes.
    private bool? StageWallsAreBaked(int tick)
    {
        var blocks = Blocks();
        if (blocks.Count == 0)
        {
            GD.Print("  no blocks generated — nothing to route around");
            return false;
        }

        // The widest block that has open ground on both sides of it.
        //
        // Taking the widest block outright is taking whatever the seed happened
        // to produce. This run it produced one packed into a corner, where the
        // enemy spawned with nowhere to go — and "it did not cross the wall" was
        // then satisfied by standing still, which is the assertion passing for
        // the wrong reason. Phase 10 learned the same thing about FlowFieldProbe:
        // a probe that leans on the layout is measuring the seed.
        (Vector2 center, Vector2 half) = (Vector2.Zero, Vector2.Zero);
        foreach ((Vector2 c, Vector2 h) in blocks)
        {
            if (h.X * h.Y <= half.X * half.Y)
                continue;

            var near = new Vector2(c.X, c.Y - h.Y - 4.0f);
            var far = new Vector2(c.X, c.Y + h.Y + 4.0f);
            if (Inside(near, blocks, 1.5f) || Inside(far, blocks, 1.5f) || far.Length() > 52.0f)
                continue;

            (center, half) = (c, h);
        }

        if (half == Vector2.Zero)
        {
            GD.Print("  no generated block has open ground on both sides of it");
            return false;
        }

        if (tick == 1)
        {
            _horde!.Pool.Clear();
            _player!.GlobalPosition = new Vector3(center.X, 0.0f, center.Y - half.Y - 4.0f);
            _behind = new Vector3(center.X, 0.0f, center.Y + half.Y + 4.0f);
            _horde.Spawn(_behind);
            return null;
        }

        if (tick < 90)
            return null;

        // Straight through would mean crossing the block's own footprint.
        bool crossed = false;
        for (int i = 0; i < _horde!.Pool.Count; i++)
        {
            Vector3 p = _horde.Pool.Position[i];
            if (Mathf.Abs(p.X - center.X) < half.X && Mathf.Abs(p.Z - center.Y) < half.Y)
                crossed = true;
        }

        Vector3 now = _horde.Pool.Count > 0 ? _horde.Pool.Position[0] : _behind;
        float sideways = Mathf.Abs(now.X - center.X);

        GD.Print($"  block {half.X * 2.0f:F1}x{half.Y * 2.0f:F1} at ({center.X:F0},{center.Y:F0}); " +
                 $"enemy moved {sideways - Mathf.Abs(_behind.X - center.X):F2}m sideways, " +
                 $"inside the wall = {crossed}");

        // It has to go around, which means moving off the straight line. Sitting
        // still would also avoid crossing, so the sideways motion is the proof.
        return !crossed && sideways > half.X * 0.5f;
    }

    private Vector3 _behind;

    /// One layout proves the generator can work; a hundred prove it cannot
    /// produce the run that has no way out. This is also the only thing that
    /// exercises the carving path, because most seeds never need it — a rescue
    /// that only runs on rare inputs is a rescue nobody has ever seen work.
    private bool? StageSweep(int tick)
    {
        (int sealedShipping, int carvedShipping) = Sweep(60, GridSize: 5, label: "shipping density");

        // The same sweep on a map packed far tighter than the game ships. The
        // rescue exists for the density nobody has tried yet, so the only way to
        // know it works is to generate the conditions that need it — a safety
        // net that has never caught anything is a guess.
        (int sealedDense, int carvedDense) = Sweep(40, GridSize: 11, label: "packed tight");

        int original = 5;
        _level!.GridSize = original;
        _level.Seed = SeedA;
        _level.Generate();
        _horde!.RebakeObstacles();

        return sealedShipping == 0 && sealedDense == 0 && carvedDense > 0;
    }

    private (int Sealed, int Carved) Sweep(int seeds, int GridSize, string label)
    {
        int sealedOff = 0;
        int carved = 0;
        int worstBlocks = 0;

        _level!.GridSize = GridSize;

        for (int i = 0; i < seeds; i++)
        {
            _level.Seed = 0x1000UL + (ulong)i * 0x9E3779B97F4A7C15UL;
            _level.Generate();

            worstBlocks = Mathf.Max(worstBlocks, Placed("Obstacles").Count);
            if (_level.CarvedLastRun > 0)
                carved++;

            // Rebaking the horde's field is what makes this a real check: the
            // generator's own verdict is not evidence about the generator.
            _horde!.RebakeObstacles();

            foreach (Node3D node in Placed("ExtractionZones"))
            {
                if (node is ExtractionZone { WillOpen: true } pad &&
                    !Walkable(Vector3.Zero, pad.GlobalPosition))
                {
                    sealedOff++;
                }
            }
        }

        GD.Print($"  {label}: {seeds} seeds, up to {worstBlocks} blocks, " +
                 $"{carved} needed carving, {sealedOff} sealed pads");

        return (sealedOff, carved);
    }

    private System.Collections.Generic.List<Node3D> Placed(string container)
    {
        var found = new System.Collections.Generic.List<Node3D>();
        Node? parent = _scene?.GetNodeOrNull(container);
        if (parent == null)
            return found;

        foreach (Node child in parent.GetChildren())
        {
            if (child is Node3D node)
                found.Add(node);
        }

        return found;
    }

    private System.Collections.Generic.List<(Vector2 Center, Vector2 Half)> Blocks()
    {
        var blocks = new System.Collections.Generic.List<(Vector2, Vector2)>();

        foreach (Node3D body in Placed("Obstacles"))
        {
            if (body.GetNodeOrNull<CollisionShape3D>("Collision")?.Shape is not BoxShape3D box)
                continue;

            blocks.Add((new Vector2(body.Position.X, body.Position.Z),
                        new Vector2(box.Size.X * 0.5f, box.Size.Z * 0.5f)));
        }

        return blocks;
    }

    private static bool Inside(Vector2 point, System.Collections.Generic.List<(Vector2 Center, Vector2 Half)> blocks,
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
}
