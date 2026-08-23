using Godot;

/// Checks the three imported landmarks: the files, the colliders, and whether
/// anything walks through them.
///
///   godot --headless --script test/LandmarkProbe.cs
///
/// These are the only meshes in the game that were not built by `MeshBuilder`,
/// and almost everything that can go wrong with an imported mesh goes wrong
/// quietly. A `.glb` that failed to import instantiates as an empty node. A model
/// authored around its own centre sinks half into the ground and reads as a
/// shorter landmark. A footprint written down beside the mesh instead of measured
/// off it stops being true the first time the mesh is edited, and the symptom is
/// a player clipping into a silo. None of those print anything.
///
/// The one that would print something is the trimesh collider, and only if
/// someone were watching the frame time on generation.
public partial class LandmarkProbe : SceneTree
{
    private Horde? _horde;
    private Player? _player;
    private LevelGenerator? _level;
    private Node3D? _obstacles;

    private int _stage;
    private int _stageTick;
    private bool _failed;

    public override void _Initialize()
    {
        var scene = GD.Load<PackedScene>("res://scenes/Main.tscn")?.Instantiate();
        if (scene == null)
        {
            GD.PushError("Missing res://scenes/Main.tscn");
            Quit(1);
            return;
        }

        var level = scene.GetNodeOrNull<LevelGenerator>("Level");
        if (level != null)
            level.Seed = 0x51E5D0A7UL;

        GetRoot().AddChild(scene);
    }

    public override bool _PhysicsProcess(double delta)
    {
        if (_stageTick == 0 && _stage == 0)
        {
            Node scene = GetRoot().GetChild(GetRoot().GetChildCount() - 1);
            _horde = scene.GetNodeOrNull<Horde>("Horde");
            _player = scene.GetNodeOrNull<Player>("Player");
            _level = scene.GetNodeOrNull<LevelGenerator>("Level");
            _obstacles = scene.GetNodeOrNull<Node3D>("Obstacles");

            if (_horde == null || _player == null || _level == null || _obstacles == null)
            {
                GD.PushError("PROBE FAILED — the scene is missing Horde, Player, Level or Obstacles");
                Quit(1);
                return true;
            }

            // The director would open pads, call zones and spawn waves through
            // the middle of the walking test.
            scene.GetNodeOrNull<RunDirector>("RunDirector")?.SetPhysicsProcess(false);

            // And the player would shoot the walker. Auto-fire reaches further
            // than the landmark is wide, so the first version of stage 6 reported
            // "the walker died or despawned before it got anywhere" — which is
            // exactly what happened, and had nothing to do with landmarks.
            var weapons = _player.GetNodeOrNull<WeaponHandler>("WeaponHandler");
            if (weapons != null)
                weapons.HoldFire = true;
        }

        _stageTick++;

        switch (_stage)
        {
            case 0: return RunStage(StageModelsLoad, "all three models import with geometry");
            case 1: return RunStage(StageModelsStandOnZero, "each model's base is its own origin");
            case 2: return RunStage(StageOnePerKind, "one of each landmark is on the map");
            case 3: return RunStage(StageCollidersAreBoxes, "every landmark collides as a box, not as its mesh");
            case 4: return RunStage(StageDrawnAsRealMeshes, "each landmark is drawn by a MeshInstance3D it owns");
            case 5: return RunStage(StagePlantedAndClear, "landmarks sit on the ground and off the spawn");
            case 6: return RunStage(StageTheHordeWalksRound, "the horde walks around a landmark rather than through it");
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

    /// The files exist, imported, and contain triangles.
    ///
    /// A missing or unimported `.glb` gives `GD.Load` a null and the landmark
    /// silently becomes an invisible box the player walks into. The triangle
    /// floor is what catches the other version: an import that succeeded and
    /// produced an empty scene.
    private bool? StageModelsLoad(int tick)
    {
        bool ok = true;

        foreach (LandmarkKind kind in System.Enum.GetValues<LandmarkKind>())
        {
            Node3D? model = LandmarkLibrary.Instantiate(kind);
            if (model == null)
            {
                GD.PushError($"  {kind} did not instantiate");
                ok = false;
                continue;
            }

            int triangles = CountTriangles(model);
            Vector2 footprint = LandmarkLibrary.Footprint(kind);
            float height = LandmarkLibrary.Height(kind);

            GD.Print($"  {kind}: {triangles} tris, "
                   + $"{footprint.X * 2.0f:F1} x {height:F1} x {footprint.Y * 2.0f:F1} m");

            if (triangles < 60)
            {
                GD.PushError($"  {kind} has {triangles} triangles — the import produced an empty scene");
                ok = false;
            }

            // Nothing here is a hero asset. Four figures means someone exported a
            // subdivided model by accident, and three of them on a map is the
            // whole triangle budget for the arena.
            if (triangles > 1500)
            {
                GD.PushError($"  {kind} has {triangles} triangles — far past what a landmark is for");
                ok = false;
            }

            if (height < 2.0f || footprint.X < 0.5f || footprint.Y < 0.5f)
            {
                GD.PushError($"  {kind} measures {footprint} x {height} — degenerate bounds");
                ok = false;
            }

            model.Free();
        }

        return ok;
    }

    /// The base of each model sits at y = 0 in its own space.
    ///
    /// `LevelGenerator` plants a landmark by setting its body's Y and then
    /// offsetting the model down by half the height. That arithmetic is only
    /// correct if the model's own bottom is its origin — a model centred on
    /// itself buries half of itself, and the only visible symptom is a landmark
    /// that looks shorter than it is.
    private bool? StageModelsStandOnZero(int tick)
    {
        bool ok = true;

        foreach (LandmarkKind kind in System.Enum.GetValues<LandmarkKind>())
        {
            Node3D? model = LandmarkLibrary.Instantiate(kind);
            if (model == null)
                continue;

            Aabb? bounds = null;
            Bounds(model, ref bounds);
            model.Free();

            if (!bounds.HasValue)
            {
                GD.PushError($"  {kind} has no geometry to measure");
                ok = false;
                continue;
            }

            float floor = bounds.Value.Position.Y;
            if (Mathf.Abs(floor) > 0.02f)
            {
                GD.PushError($"  {kind}'s base is at y={floor:F3}, not 0 — rerun art-src/models/build.mjs");
                ok = false;
            }
        }

        return ok;
    }

    private bool? StageOnePerKind(int tick)
    {
        var seen = new System.Collections.Generic.HashSet<LandmarkKind>();
        foreach ((LandmarkKind kind, Vector2 _) in _level!.Landmarks)
            seen.Add(kind);

        GD.Print($"  {_level.Landmarks.Count} sited: "
               + string.Join(", ", System.Linq.Enumerable.Select(_level.Landmarks,
                   l => $"{l.Kind} at ({l.Spot.X:F0}, {l.Spot.Y:F0})")));

        return _level.Landmarks.Count == 3 && seen.Count == 3;
    }

    /// Never a trimesh.
    ///
    /// `ConcavePolygonShape3D` from a 564-triangle lattice takes the frame under
    /// a second at generation and never errors, so the only trace it leaves is a
    /// hitch nobody is timing. It also makes raycasts against it unreliable
    /// (godot.md:39), which is a different bug entirely and would be found first.
    ///
    /// The box is also what the flow field bakes: `Horde.RefreshObstacles` only
    /// looks at children named "Collision" holding a `BoxShape3D`, so a landmark
    /// with any other shape is a landmark the horde walks straight through —
    /// which is what stage 6 would then catch, three stages later and looking
    /// like a pathing bug.
    private bool? StageCollidersAreBoxes(int tick)
    {
        int found = 0;
        bool ok = true;

        foreach (Node child in _obstacles!.GetChildren())
        {
            if (child is not Node3D body)
                continue;

            // A landmark body is the one with a model hung under it.
            if (FindMesh(body) == null)
                continue;

            found++;

            var shapeNode = body.GetNodeOrNull<CollisionShape3D>("Collision");
            if (shapeNode?.Shape is BoxShape3D box)
            {
                if (box.Size.X < 0.5f || box.Size.Y < 2.0f || box.Size.Z < 0.5f)
                {
                    GD.PushError($"  {body.Name} collides as {box.Size} — smaller than the model it hides");
                    ok = false;
                }

                continue;
            }

            GD.PushError($"  {body.Name} collides as {shapeNode?.Shape?.GetType().Name ?? "nothing"}, not a box");
            ok = false;
        }

        GD.Print($"  {found} landmark bodies, all boxed");
        return ok && found == 3;
    }

    /// Never a MultiMesh.
    ///
    /// An imported mesh inside a `MultiMesh` is lost the moment the scene is
    /// packed and saved — the packer keeps resources the scene owns, and an
    /// imported mesh is owned by its `.glb`. What survives is a `MultiMesh` with
    /// the right instance count and no mesh, which draws nothing and reports
    /// nothing. Three landmarks is not a draw-call problem worth risking that
    /// for.
    private bool? StageDrawnAsRealMeshes(int tick)
    {
        bool ok = true;
        int meshes = 0;

        foreach (Node child in _obstacles!.GetChildren())
        {
            if (child is not Node3D body)
                continue;

            if (HasMultiMesh(body))
            {
                GD.PushError($"  {body.Name} draws through a MultiMesh");
                ok = false;
            }

            MeshInstance3D? mesh = FindMesh(body);
            if (mesh != null)
                meshes++;
        }

        GD.Print($"  {meshes} landmark meshes in the tree, none of them instanced");
        return ok && meshes >= 3;
    }

    private bool? StagePlantedAndClear(int tick)
    {
        bool ok = true;

        foreach (Node child in _obstacles!.GetChildren())
        {
            if (child is not Node3D body || FindMesh(body) == null)
                continue;

            var shape = body.GetNodeOrNull<CollisionShape3D>("Collision")?.Shape as BoxShape3D;
            float half = (shape?.Size.Y ?? 0.0f) * 0.5f;

            // The body sits at the centre of its own box, so its base is half a
            // height below it — and that base is what has to be on the ground.
            float baseline = body.Position.Y - half;
            float ground = Terrain.Height(body.Position.X, body.Position.Z);

            if (Mathf.Abs(baseline - ground) > 0.02f)
            {
                GD.PushError($"  {body.Name} stands at {baseline:F2} m over ground at {ground:F2} m");
                ok = false;
            }
        }

        // And off the spawn, which is the one place on the map that must stay
        // clear: the run starts there, and a run that starts inside a silo is a
        // run that never starts.
        foreach ((LandmarkKind kind, Vector2 spot) in _level!.Landmarks)
        {
            if (spot.Length() >= _level.SpawnClearance)
                continue;

            GD.PushError($"  {kind} is {spot.Length():F1} m from the spawn");
            ok = false;
        }

        GD.Print($"  nearest to the spawn: "
               + $"{System.Linq.Enumerable.Min(System.Linq.Enumerable.Select(_level.Landmarks, l => l.Spot.Length())):F1} m");

        return ok;
    }

    // --- the behavioural stage ----------------------------------------------

    private int _target;
    private Vector3 _from;
    private Vector3 _to;
    private Vector2 _half;
    private float _startDistance;
    private bool _entered;
    private float _closest = float.MaxValue;

    /// Does the horde go round it?
    ///
    /// **Not asked of `FlowField.Sample`.** The obvious test is to sample the
    /// flow inside a landmark and assert it is blocked, and it fails on a correct
    /// field: `Sample` deliberately returns a neighbour's flow for a blocked cell
    /// so that a body which ends up inside an obstacle — knocked back, spawned on
    /// a seam — can walk out instead of standing there. A blocked cell therefore
    /// reads exactly like an open one, and the only honest question is
    /// behavioural: put an enemy on the far side and watch where it goes.
    ///
    /// One enemy, not a wave. A crowd separates, and a body shoved sideways by
    /// its neighbours crosses a footprint for reasons that have nothing to do
    /// with the field.
    private bool? StageTheHordeWalksRound(int tick)
    {
        Horde horde = _horde!;

        if (tick == 1)
        {
            // The widest landmark on the map: the more ground it covers, the less
            // the result can be an accident of one cell.
            _target = 0;
            float widest = 0.0f;

            for (int i = 0; i < _level!.Landmarks.Count; i++)
            {
                Vector2 footprint = LandmarkLibrary.Footprint(_level.Landmarks[i].Kind);
                float area = footprint.X * footprint.Y;
                if (area > widest)
                {
                    widest = area;
                    _target = i;
                }
            }

            (LandmarkKind kind, Vector2 spot) = _level.Landmarks[_target];
            _half = LandmarkLibrary.Footprint(kind);

            // Player one side, enemy the other, on the axis the landmark is
            // widest across — so the straight line between them runs the long way
            // through it and "walked straight at the player" is unmistakable.
            float reach = Mathf.Max(_half.X, _half.Y) + 9.0f;
            Vector2 axis = _half.X >= _half.Y ? new Vector2(0.0f, 1.0f) : new Vector2(1.0f, 0.0f);

            _from = new Vector3(spot.X - axis.X * reach, 0.0f, spot.Y - axis.Y * reach);
            _to = new Vector3(spot.X + axis.X * reach, 0.0f, spot.Y + axis.Y * reach);

            _player!.GlobalPosition = Terrain.Plant(_to);

            horde.Pool.Clear();
            if (!horde.Spawn(_from))
            {
                GD.PushError("  could not spawn the walker");
                return false;
            }

            _startDistance = Flat(_from - _to);
            horde.RebuildFieldAround(_player.GlobalPosition);

            GD.Print($"  {kind} at ({spot.X:F0}, {spot.Y:F0}), "
                   + $"{_half.X * 2.0f:F1} x {_half.Y * 2.0f:F1} m; "
                   + $"a walker starts {_startDistance:F1} m away on the far side");
            return null;
        }

        if (horde.Pool.Count == 0)
        {
            GD.PushError("  the walker died or despawned before it got anywhere");
            return false;
        }

        // The player does not move, so the field stays valid — but it is rebuilt
        // anyway, because the horde does that on its own schedule and a test that
        // depended on the schedule would be a test of the schedule.
        _player!.GlobalPosition = Terrain.Plant(_to);

        Vector3 at = horde.Pool.Position[0];
        Vector2 spotNow = _level!.Landmarks[_target].Spot;

        // Inside the footprint, with a margin off each edge. The field inflates
        // obstacles by the separation radius before blocking them, so a body
        // brushing the outer edge is expected; a body a metre inside is not.
        if (Mathf.Abs(at.X - spotNow.X) < _half.X - 0.6f
            && Mathf.Abs(at.Z - spotNow.Y) < _half.Y - 0.6f)
        {
            _entered = true;
        }

        _closest = Mathf.Min(_closest, Flat(at - _to));

        if (tick % 600 == 0)
            GD.Print($"    tick {tick}: at ({at.X:F1}, {at.Z:F1}), {Flat(at - _to):F1} m to go");

        if (tick < 1800)
            return null;

        float closed = 1.0f - _closest / _startDistance;
        GD.Print($"  after {tick} ticks it closed {closed * 100.0f:F0}% of the gap"
               + $" and {(_entered ? "walked through the landmark" : "never entered the footprint")}");

        // Both halves matter. "Never entered" alone passes on a walker that stood
        // still for thirty seconds, and "closed the gap" alone passes on one that
        // walked through the middle of a silo to do it.
        //
        // Thirty seconds because going round is slow, and that is the finding
        // rather than a tolerance: the walker first moves twelve metres *away*
        // from the player to clear the pylon and the cover beside it, and only
        // then turns. At 900 ticks it had closed 37% and looked stuck. It was not
        // stuck; it was going round, which is the whole thing being tested.
        return !_entered && closed > 0.8f;
    }

    // --- helpers ------------------------------------------------------------

    private static float Flat(Vector3 delta) => new Vector2(delta.X, delta.Z).Length();

    private static int CountTriangles(Node node)
    {
        int total = 0;

        if (node is MeshInstance3D { Mesh: not null } instance)
        {
            for (int surface = 0; surface < instance.Mesh.GetSurfaceCount(); surface++)
            {
                var vertices = instance.Mesh.SurfaceGetArrays(surface)[(int)Mesh.ArrayType.Vertex]
                    .AsVector3Array();
                total += vertices.Length / 3;
            }
        }

        foreach (Node child in node.GetChildren())
            total += CountTriangles(child);

        return total;
    }

    private static void Bounds(Node node, ref Aabb? total)
    {
        if (node is MeshInstance3D { Mesh: not null } instance)
        {
            Aabb local = instance.Mesh.GetAabb();
            total = total.HasValue ? total.Value.Merge(local) : local;
        }

        foreach (Node child in node.GetChildren())
            Bounds(child, ref total);
    }

    private static MeshInstance3D? FindMesh(Node node)
    {
        if (node is MeshInstance3D { Mesh: not null } instance)
            return instance;

        foreach (Node child in node.GetChildren())
        {
            MeshInstance3D? found = FindMesh(child);
            if (found != null)
                return found;
        }

        return null;
    }

    private static bool HasMultiMesh(Node node)
    {
        if (node is MultiMeshInstance3D)
            return true;

        foreach (Node child in node.GetChildren())
        {
            if (HasMultiMesh(child))
                return true;
        }

        return false;
    }
}
