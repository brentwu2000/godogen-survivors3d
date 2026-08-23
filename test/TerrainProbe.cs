using Godot;

/// Checks that the ground has shape and that nothing else noticed.
///
///   godot --headless --script test/TerrainProbe.cs
///
/// `Terrain` is a deliberately one-sided change: the floor gets a metre and a
/// half of relief, and the simulation is told nothing about it. That split is the
/// whole design (see `Terrain`), and it is also the whole risk — every bug this
/// phase can produce is a case of the two sides disagreeing, and none of them
/// throw.
///
/// So this asks both questions. Is the ground actually not flat, and did the
/// simulation stay flat anyway? A probe that only asked the first would pass on a
/// build where the flow field had quietly started routing around hills; one that
/// only asked the second would pass on a build where `Terrain.Height` returned
/// zero everywhere, which is exactly what a mis-set `Offset` or a clamped
/// amplitude looks like.
public partial class TerrainProbe : SceneTree
{
    private Horde? _horde;
    private int _stage;
    private int _stageTick;
    private bool _failed;

    /// Half a millimetre. The drawn positions are computed from the same
    /// function this probe calls, so the only difference should be float
    /// round-trips through a `float[]` buffer.
    private const float Epsilon = 0.0005f;

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

        // Not the developer's save file. See `Fresh`.
        Fresh.Profile(scene);

        GetRoot().AddChild(scene);
    }

    public override bool _PhysicsProcess(double delta)
    {
        if (_stageTick == 0 && _stage == 0)
        {
            Node scene = GetRoot().GetChild(GetRoot().GetChildCount() - 1);
            _horde = scene.GetNodeOrNull<Horde>("Horde");
            if (_horde == null)
            {
                GD.PushError("PROBE FAILED — no Horde");
                Quit(1);
                return true;
            }
        }

        _stageTick++;

        switch (_stage)
        {
            case 0: return RunStage(StageGroundHasShape, "the ground is provably not flat");
            case 1: return RunStage(StageDeterministic, "a point is the same height every time it is asked");
            case 2: return RunStage(StageFlatAtSpawn, "the spawn is flat and the far ground is not");
            case 3: return RunStage(StageSeedsDiffer, "two seeds are two different landscapes");
            case 4: return RunStage(StagePoolStaysFlat, "the enemy pool's Y stays zero over rough ground");
            case 5: return RunStage(StageBodiesAreDrawnPlanted, "every drawn body sits on the ground it stands on");
            case 6: return RunStage(StageQueriesIgnoreTheDrop, "NearestWithin measures the floor plan, not the terrain");
            case 7: return RunStage(StageGroundMeshFacesUp, "the floor mesh is wound to be seen from above");
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

    /// Asserts presence, not absence.
    ///
    /// The cheap version of this test is "the ground is not identically zero",
    /// which passes on a field with one bump in it. What the phase is for is
    /// relief the player can see, so this measures the actual spread over the
    /// arena and requires most of the designed amplitude to be there.
    private bool? StageGroundHasShape(int tick)
    {
        float low = float.MaxValue;
        float high = float.MinValue;

        for (int gx = -20; gx <= 20; gx++)
        {
            for (int gz = -20; gz <= 20; gz++)
            {
                // Sampled from 20 m out, past `FlatFadeEnd`, so the flattened
                // spawn does not drag the range down and hide a dead field.
                float x = gx * 2.5f;
                float z = gz * 2.5f;
                if (Mathf.Sqrt(x * x + z * z) < Terrain.FlatFadeEnd)
                    continue;

                float h = Terrain.Height(x, z);
                low = Mathf.Min(low, h);
                high = Mathf.Max(high, h);
            }
        }

        float spread = high - low;
        GD.Print($"  {low:F2} m to {high:F2} m — {spread:F2} m of relief over 100 m");

        // Two thirds of the peak-to-peak the amplitude allows. Value noise rarely
        // reaches its own extremes, so requiring the full 2x would fail on a
        // perfectly good field.
        return spread > Terrain.Amplitude * 1.3f;
    }

    /// The same point, twice, and from a fresh call order.
    ///
    /// A hash that involved anything but the coordinates — a frame counter, a
    /// static accumulator, the order of calls — would give ground that crawls
    /// under the player while every single frame of it looked correct.
    private bool? StageDeterministic(int tick)
    {
        bool ok = true;

        for (int i = 0; i < 64; i++)
        {
            float x = -48.0f + i * 1.5f;
            float z = 17.0f - i * 0.9f;

            float first = Terrain.Height(x, z);
            for (int n = 0; n < 7; n++)
                Terrain.Height(x + n, z - n);

            float second = Terrain.Height(x, z);
            if (Mathf.Abs(first - second) > Epsilon)
            {
                GD.PushError($"  ({x:F1}, {z:F1}) was {first:F4} then {second:F4}");
                ok = false;
            }
        }

        return ok;
    }

    /// The flattened spawn, and the fact that it ends.
    ///
    /// Both halves matter. Without the flat disc the player starts standing in a
    /// slope the collider does not have; without the fade ending, `FlatFadeEnd`
    /// set to something absurd would flatten the whole map and every other stage
    /// here would still pass.
    private bool? StageFlatAtSpawn(int tick)
    {
        bool ok = true;
        float worstInside = 0.0f;

        for (int i = 0; i < 48; i++)
        {
            float angle = Mathf.Tau * i / 48.0f;
            for (float r = 0.0f; r <= Terrain.FlatRadius; r += 1.0f)
                worstInside = Mathf.Max(worstInside,
                    Mathf.Abs(Terrain.Height(Mathf.Cos(angle) * r, Mathf.Sin(angle) * r)));
        }

        if (worstInside > Epsilon)
        {
            GD.PushError($"  the spawn disc is {worstInside:F3} m from flat");
            ok = false;
        }

        // And the ground beyond the fade is not.
        float outside = 0.0f;
        for (int i = 0; i < 96; i++)
        {
            float angle = Mathf.Tau * i / 96.0f;
            outside = Mathf.Max(outside,
                Mathf.Abs(Terrain.Height(Mathf.Cos(angle) * 30.0f, Mathf.Sin(angle) * 30.0f)));
        }

        GD.Print($"  flat to {worstInside:F4} m inside {Terrain.FlatRadius} m, {outside:F2} m at 30 m");
        return ok && outside > 0.25f;
    }

    /// The offset is what makes a seed a landscape.
    ///
    /// Restored afterwards, because every other stage in this file and every
    /// object already placed in the running scene were built against the live
    /// value — a probe that left it shifted would draw the crates underground for
    /// the rest of its own run.
    private bool? StageSeedsDiffer(int tick)
    {
        Vector2 original = Terrain.Offset;
        int differing = 0;

        try
        {
            var samples = new float[32];
            for (int i = 0; i < samples.Length; i++)
                samples[i] = Terrain.Height(20.0f + i * 3.0f, -25.0f + i * 2.0f);

            Terrain.Offset = original + new Vector2(413.0f, 271.0f);

            for (int i = 0; i < samples.Length; i++)
            {
                float now = Terrain.Height(20.0f + i * 3.0f, -25.0f + i * 2.0f);
                if (Mathf.Abs(now - samples[i]) > 0.05f)
                    differing++;
            }
        }
        finally
        {
            Terrain.Offset = original;
        }

        GD.Print($"  {differing}/32 samples moved when the offset did");
        return differing >= 28;
    }

    /// The assertion the whole design rests on.
    ///
    /// Run live rather than on a synthetic pool: the enemies have been walking,
    /// separating, being knocked back and being recycled for these ticks, and it
    /// is the recycle and the knockback that would introduce a height if anything
    /// did. A single non-zero Y here means some renderer wrote back into the
    /// simulation, and from that point the flow field, the separation grid and
    /// every distance test are reading a different world than they were written
    /// for.
    private bool? StagePoolStaysFlat(int tick)
    {
        if (tick < 90)
            return null;

        EnemyPool pool = _horde!.Pool;
        float worst = 0.0f;
        int rough = 0;

        for (int i = 0; i < pool.Count; i++)
        {
            worst = Mathf.Max(worst, Mathf.Abs(pool.Position[i].Y));
            if (Mathf.Abs(Terrain.Height(pool.Position[i].X, pool.Position[i].Z)) > 0.2f)
                rough++;
        }

        GD.Print($"  {pool.Count} enemies, {rough} of them over ground that is not flat, worst Y {worst:F4}");

        // The second half is what stops this passing vacuously: over a dead
        // terrain every Y would be zero and so would every height.
        return worst <= Epsilon && rough > pool.Count / 4;
    }

    /// And the other side of it: the drawing did move.
    ///
    /// Read out of the MultiMesh buffers rather than recomputed from the pool,
    /// because recomputing would be asking `Terrain` whether it agrees with
    /// itself. What is being checked is that `BodyRenderer.Sync` actually plants
    /// what it writes — the version of this that did not was invisible, because
    /// bodies at zero over ground at zero-point-eight look like bodies on a
    /// slightly different floor.
    private bool? StageBodiesAreDrawnPlanted(int tick)
    {
        BodyRenderer? bodies = _horde!.Bodies;
        if (bodies == null)
        {
            GD.Print("  the horde is on the sprite path — nothing to check");
            return true;
        }

        int checkedInstances = 0;
        int planted = 0;
        float worst = 0.0f;

        foreach (Node child in bodies.Node.GetChildren())
        {
            if (child is not MultiMeshInstance3D instance || instance.Multimesh == null)
                continue;

            MultiMesh multi = instance.Multimesh;
            float[] buffer = multi.Buffer;
            int stride = buffer.Length / Mathf.Max(1, multi.InstanceCount);

            for (int i = 0; i < multi.VisibleInstanceCount; i++)
            {
                int b = i * stride;
                float x = buffer[b + 3];
                float y = buffer[b + 7];
                float z = buffer[b + 11];

                float expected = Terrain.Height(x, z);
                worst = Mathf.Max(worst, Mathf.Abs(y - expected));
                checkedInstances++;

                if (Mathf.Abs(expected) > 0.2f)
                    planted++;
            }
        }

        if (checkedInstances == 0)
        {
            GD.PushError("  no visible bodies to read — nothing was proven");
            return false;
        }

        GD.Print($"  {checkedInstances} drawn bodies, {planted} standing on relief, worst error {worst:F4} m");
        return worst <= 0.01f && planted > 0;
    }

    /// The named case from the plan: twelve metres out, across half a metre of
    /// drop, found at thirteen and missed at eleven.
    ///
    /// The drop is the point. A `NearestWithin` that had been quietly switched to
    /// a three-dimensional distance would still find the target at thirteen — the
    /// slant range is 12.01 — and the failure would only appear on a steep hill
    /// with a long-range weapon, which is the last place anyone looks. Pinning
    /// both ends means the reach is exactly the number the weapon's stat block
    /// says, on ground of any shape.
    private bool? StageQueriesIgnoreTheDrop(int tick)
    {
        Horde horde = _horde!;
        EnemyPool pool = horde.Pool;

        // A clear field, so "nearest" is unambiguous. Emptied and not restored —
        // `EnemyPool` has no way to put two hundred entries back, and this is the
        // last stage that needs live enemies. The horde refills on its own.
        int previous = pool.Count;
        pool.Clear();

        {
            // A pair of points 12 m apart with real ground under them, searched
            // for rather than assumed: the terrain is offset per seed, so no
            // fixed coordinate is guaranteed to have a drop across it.
            Vector3 from = Vector3.Zero;
            Vector3 to = Vector3.Zero;
            float drop = 0.0f;

            for (int i = 0; i < 512 && drop < 0.5f; i++)
            {
                float angle = Mathf.Tau * i / 512.0f;
                var candidate = new Vector3(Mathf.Cos(angle) * 26.0f, 0.0f, Mathf.Sin(angle) * 26.0f);
                var partner = new Vector3(
                    candidate.X + Mathf.Cos(angle + 1.3f) * 12.0f, 0.0f,
                    candidate.Z + Mathf.Sin(angle + 1.3f) * 12.0f);

                float difference = Mathf.Abs(Terrain.Height(candidate.X, candidate.Z)
                                           - Terrain.Height(partner.X, partner.Z));
                if (difference > drop)
                {
                    drop = difference;
                    from = candidate;
                    to = partner;
                }
            }

            if (drop < 0.5f)
            {
                GD.PushError($"  no pair 12 m apart with 0.5 m between them — best was {drop:F2} m");
                return false;
            }

            if (!horde.Spawn(to))
            {
                GD.PushError("  could not spawn the target");
                return false;
            }

            // Spawn plants nothing, and this is the assertion: the thing the
            // query measures against is a flat position.
            if (Mathf.Abs(pool.Position[0].Y) > Epsilon)
            {
                GD.PushError($"  the spawned enemy has a height of {pool.Position[0].Y:F3}");
                return false;
            }

            int atThirteen = horde.NearestWithin(from, 13.0f);
            int atEleven = horde.NearestWithin(from, 11.0f);

            float flat = new Vector2(to.X - from.X, to.Z - from.Z).Length();
            GD.Print($"  cleared {previous} enemies; {flat:F2} m apart across {drop:F2} m of drop — "
                   + $"range 13 finds {atThirteen}, range 11 finds {atEleven}");

            return atThirteen == 0 && atEleven == -1;
        }
    }

    /// The floor is wound to be seen from above.
    ///
    /// This is the only defect in the phase with no numeric symptom at all: the
    /// mesh builds, the vertex count is right, every height is correct, and the
    /// screen is black. Godot's front face is the one whose engine normal —
    /// `(v0 - v2) × (v0 - v1)`, the negative of the right-hand rule — points at
    /// the camera, so a floor is visible from above when the right-hand normal of
    /// its winding points down. It was authored the other way round first and
    /// cost a rendering session to spot.
    private bool? StageGroundMeshFacesUp(int tick)
    {
        Node scene = GetRoot().GetChild(GetRoot().GetChildCount() - 1);
        var ground = scene.GetNodeOrNull<MeshInstance3D>("Ground/Mesh");
        if (ground?.Mesh is not ArrayMesh mesh)
        {
            GD.PushError("  Ground/Mesh is not an ArrayMesh — GroundMesh did not build");
            return false;
        }

        var vertices = (Vector3[])mesh.SurfaceGetArrays(0)[(int)Mesh.ArrayType.Vertex];
        var normals = (Vector3[])mesh.SurfaceGetArrays(0)[(int)Mesh.ArrayType.Normal];

        if (vertices.Length < 6000)
        {
            GD.PushError($"  {vertices.Length} vertices — the floor is a placeholder plane");
            return false;
        }

        int wrongWinding = 0;
        int wrongNormal = 0;
        int notOnTerrain = 0;

        for (int i = 0; i + 2 < vertices.Length; i += 3)
        {
            Vector3 a = vertices[i];
            Vector3 b = vertices[i + 1];
            Vector3 c = vertices[i + 2];

            // The engine's own convention, spelled out rather than assumed.
            if ((a - c).Cross(a - b).Y <= 0.0f)
                wrongWinding++;

            if (normals[i].Y <= 0.0f)
                wrongNormal++;

            if (Mathf.Abs(a.Y - Terrain.Height(a.X, a.Z)) > Epsilon)
                notOnTerrain++;
        }

        GD.Print($"  {vertices.Length / 3} triangles, {wrongWinding} facing down, "
               + $"{wrongNormal} lit from below, {notOnTerrain} off the height field");

        return wrongWinding == 0 && wrongNormal == 0 && notOnTerrain == 0;
    }
}
