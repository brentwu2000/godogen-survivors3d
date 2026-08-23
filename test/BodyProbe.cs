using Godot;

/// Checks the solid-body horde: the meshes, the buckets, and the gait.
///
///   godot --headless --script test/BodyProbe.cs
///
/// Nothing here looks at a picture, and most of what it asserts would not be
/// visible in one anyway. A body drawn at the wrong height is only wrong next to
/// a body drawn at the right one; a stride that advances from world position
/// looks perfectly good until two enemies stand on the same tile; and a variant
/// written into the wrong bucket draws the wrong species entirely, which reads as
/// a spawn bug three systems from the cause.
public partial class BodyProbe : SceneTree
{
    private Horde? _horde;
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
            if (_horde == null)
            {
                GD.PushError("PROBE FAILED — no Horde");
                Quit(1);
                return true;
            }

            scene.GetNodeOrNull<RunDirector>("RunDirector")?.SetPhysicsProcess(false);
        }

        _stageTick++;

        switch (_stage)
        {
            case 0: return RunStage(StageBodiesExist, "every variant has a solid body");
            case 1: return RunStage(StageMeshesAreSound, "every body is closed and outward-facing");
            case 2: return RunStage(StageHeightsMatchTable, "every body stands at its designed height");
            case 3: return RunStage(StageBuckets, "each variant is drawn from its own MultiMesh");
            case 4: return RunStage(StagePackRoundTrips, "pace and phase survive sharing one float");
            case 5: return RunStage(StageStrideFollowsWalking, "the stride advances by walking, not by being somewhere");
            case 6: return RunStage(StagePlayerHasABody, "the player is a body too, and not one of theirs");
            case 7: return RunStage(StageBillboardsStayOff, "the billboards stay off while the bodies are on");
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

    /// One enemy, one silhouette.
    ///
    /// Asked after a hundred ticks of a live horde rather than at startup, and
    /// that is the entire point of the stage. `Horde._Ready` hides the billboard
    /// node when the solid bodies are on, and `HordeRenderer.Upload` used to
    /// assign `Node.Visible = count > 0` on every single sync — so the node came
    /// back on the first tick and every enemy in the game was drawn twice, a
    /// low-poly body and a pixel-art billboard standing in the same place.
    ///
    /// It survived every probe here, because none of them asked what was on the
    /// screen, and it survived every screenshot, because at the distance those
    /// are framed at the two silhouettes overlap into one slightly odd shape. It
    /// was found in a single frame of the proof video, with a runner close enough
    /// to the camera to see both.
    private bool? StageBillboardsStayOff(int tick)
    {
        Horde horde = _horde!;

        if (tick == 1)
        {
            // A crowd of this probe's own, because the stages above leave the
            // pool however they left it and "0 enemies, billboards hidden" is a
            // pass that proves nothing.
            for (int i = 0; i < 12; i++)
            {
                float angle = Mathf.Tau * i / 12.0f;
                horde.Spawn(new Vector3(Mathf.Cos(angle) * 6.0f, 0.0f, Mathf.Sin(angle) * 6.0f),
                            i % horde.Types.Length);
            }
        }

        if (tick < 100)
            return null;

        bool solid = horde.Bodies != null;
        bool showing = horde.Billboards.Node.Visible;

        GD.Print($"  {horde.Pool.Count} enemies, bodies {(solid ? "on" : "off")}, "
               + $"billboards {(showing ? "VISIBLE" : "hidden")} after {tick} ticks");

        // Only one of the two may draw, and which one depends on which path the
        // horde took. Requiring "hidden" outright would fail on the fallback,
        // where the billboards are the only thing there is.
        if (!solid)
            return showing || horde.Pool.Count == 0;

        return !showing && horde.Pool.Count > 0;
    }

    private bool? StageBodiesExist(int tick)
    {
        BodyRenderer? bodies = _horde!.Bodies;
        if (bodies == null)
        {
            GD.PushError("  the horde is on the sprite path — SolidBodies is off, or body.gdshader is missing");
            return false;
        }

        bool ok = bodies.VariantCount == _horde.Types.Length;
        GD.Print($"  {bodies.VariantCount} body meshes for {_horde.Types.Length} variants");
        return ok;
    }

    /// The same divergence argument `MeshProbe` uses, applied to the shipping
    /// bodies rather than to a test shape.
    ///
    /// Worth repeating on the real meshes: `MeshProbe` proves the primitives are
    /// sound, and this proves nobody assembled sound primitives into something
    /// with a hole in it. A limb built with a zero-length axis returns silently
    /// and leaves a body with one arm.
    private bool? StageMeshesAreSound(int tick)
    {
        BodyRenderer bodies = _horde!.Bodies!;
        bool ok = true;

        for (int variant = 0; variant < bodies.VariantCount; variant++)
        {
            if (bodies.MeshFor(variant) is not ArrayMesh mesh)
            {
                GD.PushError($"  {_horde.Types[variant].TypeName} has no mesh");
                ok = false;
                continue;
            }

            var vertices = (Vector3[])mesh.SurfaceGetArrays(0)[(int)Mesh.ArrayType.Vertex];
            var uvs = (Vector2[])mesh.SurfaceGetArrays(0)[(int)Mesh.ArrayType.TexUV];

            var net = Vector3.Zero;
            for (int i = 0; i + 2 < vertices.Length; i += 3)
                net += (vertices[i + 1] - vertices[i]).Cross(vertices[i + 2] - vertices[i]);

            // Something has to actually swing. A body whose rig is all zeroes
            // renders perfectly and stands rigid while it slides across the
            // floor, which is a bug nothing else here would catch.
            int moving = 0;
            foreach (Vector2 uv in uvs)
            {
                if (uv.X != 0.0f)
                    moving++;
            }

            bool closed = net.Length() * 0.5f < 0.01f;
            bool rigged = moving > 0 && moving < uvs.Length;

            GD.Print($"  {_horde.Types[variant].TypeName,-8} {vertices.Length / 3,4} triangles, " +
                     $"net area {net.Length() * 0.5f:F4}, {moving}/{uvs.Length} vertices rigged to swing");

            if (!closed)
                GD.PushError($"  {_horde.Types[variant].TypeName} is not closed — a limb failed to build");
            if (!rigged)
                GD.PushError($"  {_horde.Types[variant].TypeName} has {moving} swinging vertices of {uvs.Length}");

            ok &= closed && rigged;
        }

        return ok;
    }

    /// Measured off the mesh, not assumed from the spec.
    ///
    /// The billboard path had the same check and it had to be there: the quad was
    /// one size for every layer, so a variant that did not fill its frame drew
    /// shorter than its scale suggested. Solid bodies remove that particular gap
    /// and open another — the proportions here are fractions of height, and a
    /// head placed at 0.93 with a radius of 0.07 comes to exactly 1.0 only while
    /// nobody edits either number.
    private bool? StageHeightsMatchTable(int tick)
    {
        BodyRenderer bodies = _horde!.Bodies!;
        bool ok = true;

        for (int variant = 0; variant < bodies.VariantCount; variant++)
        {
            if (bodies.MeshFor(variant) is not ArrayMesh mesh)
                continue;

            var vertices = (Vector3[])mesh.SurfaceGetArrays(0)[(int)Mesh.ArrayType.Vertex];

            float top = 0.0f, bottom = 0.0f;
            foreach (Vector3 vertex in vertices)
            {
                top = Mathf.Max(top, vertex.Y);
                bottom = Mathf.Min(bottom, vertex.Y);
            }

            float designed = _horde.Types[variant].DesignHeightMeters;
            BodyMeshLibrary.Build spec =
                BodyMeshLibrary.ForVariant(_horde.Types[variant].TypeName, designed);
            float predicted = BodyMeshLibrary.StandingHeight(spec);

            // Two links, checked separately. The mesh must be exactly as tall as
            // the library says it will be — that catches a head at the wrong
            // fraction or a limb that failed to build. And the library's answer
            // must be close to the table — that catches proportions drifting away
            // from the numbers the game is balanced against.
            //
            // A single blanket tolerance would have to be wide enough for the
            // runner's 26-degree lean, which shortens it by 5%, and a tolerance
            // that wide admits real errors.
            bool asBuilt = Mathf.Abs(top - predicted) < 0.005f;
            bool asDesigned = Mathf.Abs(predicted - designed) < designed * 0.08f;

            GD.Print($"  {_horde.Types[variant].TypeName,-8} stands {top:F2} m " +
                     $"(predicted {predicted:F2}, designed {designed:F2}" +
                     (spec.LeanDegrees != 0.0f ? $", leaning {spec.LeanDegrees:F0}°" : "") + ")" +
                     (bottom < -0.001f ? $" and {bottom:F2} m below the floor" : ""));

            if (!asBuilt)
                GD.PushError($"  {_horde.Types[variant].TypeName} built {top:F3} m where the library predicts {predicted:F3}");
            if (!asDesigned)
                GD.PushError($"  {_horde.Types[variant].TypeName} stands {predicted:F2} m against a table saying {designed:F2}");

            bool right = asBuilt && asDesigned;

            // Nothing may reach below the floor. The instance position is where
            // the feet are, so a body modelled below zero is a body sunk into the
            // ground on every tile in the game.
            if (bottom < -0.001f)
                GD.PushError($"  {_horde.Types[variant].TypeName} extends {bottom:F3} m below its own feet");

            ok &= right && bottom >= -0.001f;
        }

        return ok;
    }

    private bool? StageBuckets(int tick)
    {
        // Spawn a known mix and check each lands in its own bucket. One of each
        // rather than a random draw: this is asking whether the routing is right,
        // and a distribution would only tell us it is right on average.
        if (tick == 1)
        {
            _horde!.Pool.Clear();
            for (byte type = 0; type < _horde.Types.Length; type++)
            {
                for (int n = 0; n <= type; n++)
                    _horde.Spawn(new Vector3(2.0f + type * 3.0f, 0.0f, n * 2.0f), type);
            }

            return null;
        }

        BodyRenderer bodies = _horde!.Bodies!;
        bool ok = true;

        for (int variant = 0; variant < bodies.VariantCount; variant++)
        {
            int drawn = bodies.VisibleCount(variant);
            int expected = variant + 1;
            if (drawn != expected)
            {
                GD.PushError($"  {_horde.Types[variant].TypeName}: {drawn} drawn, {expected} spawned");
                ok = false;
            }
        }

        int total = 0;
        for (int variant = 0; variant < bodies.VariantCount; variant++)
            total += bodies.VisibleCount(variant);

        GD.Print($"  {total} bodies across {bodies.VariantCount} buckets, one more of each variant than the last");
        return ok && total == _horde.Pool.Count;
    }

    /// Two numbers in one float, and the fraction is the one that must not drift.
    ///
    /// The shader recovers pace with `floor` and phase with `fract`, so anything
    /// that pushes the phase to exactly 1.0 carries into the integer part — and a
    /// standing body would sprint for a single frame, once, unreproducibly.
    private bool? StagePackRoundTrips(int tick)
    {
        bool ok = true;
        float worst = 0.0f;

        foreach (float speed in new[] { 0.0f, 0.4f, 2.4f, 4.6f, 11.0f, 40.0f })
        {
            foreach (float phase in new[] { 0.0f, 0.25f, 0.5f, 0.999f, 1.0f, 1.5f })
            {
                float packed = BodyRenderer.Pack(speed, phase);
                float pace = Mathf.Floor(packed) * 0.25f;
                float recovered = packed - Mathf.Floor(packed);

                // Quantised on purpose, so pace comes back rounded down to the
                // nearest quarter — and never above the speed that went in.
                if (pace > speed + 0.0001f || pace < Mathf.Min(speed, 15.75f) - 0.25f)
                {
                    GD.PushError($"  speed {speed:F2} came back as {pace:F2}");
                    ok = false;
                }

                if (recovered < 0.0f || recovered >= 1.0f)
                {
                    GD.PushError($"  phase {phase:F3} came back as {recovered:F5}, outside [0, 1)");
                    ok = false;
                }

                if (phase < 1.0f)
                    worst = Mathf.Max(worst, Mathf.Abs(recovered - phase));
            }
        }

        GD.Print($"  worst phase error across the speed range: {worst:F7} of a stride");

        // A thousandth of a stride is already invisible. This should be far
        // under it, and if it is not, the pace quantum is eating the mantissa.
        if (worst > 0.0005f)
        {
            GD.PushError($"  phase lost {worst:F6} to the packing — is the pace range too wide?");
            ok = false;
        }

        return ok;
    }

    /// The player is solid, faces the view, and is not coloured like the horde.
    ///
    /// A player left as a billboard among solid enemies is the single most
    /// obvious thing that can be wrong with this phase, and it is exactly the
    /// kind of wrong that a passing horde probe says nothing about.
    private bool? StagePlayerHasABody(int tick)
    {
        Node scene = GetRoot().GetChild(GetRoot().GetChildCount() - 1);
        var player = scene.GetNodeOrNull<Player>("Player");
        if (player == null)
        {
            GD.PushError("  no Player");
            return false;
        }

        if (player.Body == null)
        {
            GD.PushError("  the player is still a sprite — SolidBody is off, or body.gdshader is missing");
            return false;
        }

        // In the tree, not merely constructed. The first version of this stage
        // asked only whether the object existed, and it passed while the node was
        // being refused by add_child every run — a body that updates every frame,
        // holds a correct transform, and draws nothing.
        if (!player.Body.Node.IsInsideTree())
        {
            GD.PushError("  the player body exists but is not in the tree — it will never draw");
            return false;
        }

        // Hue is the only channel with any bandwidth left in a crowd, so the
        // player has to hold a corner of it alone. Compared against every horde
        // torso rather than against a remembered colour: the check should fail
        // when somebody recolours an enemy, which is when it matters.
        Color playerTorso = BodyMeshLibrary.ForPlayer(2.2f).Torso;
        float closest = 999.0f;
        string nearest = "";

        foreach (EnemyTypeResource type in _horde!.Types)
        {
            Color torso = BodyMeshLibrary.ForVariant(type.TypeName, type.DesignHeightMeters).Torso;
            float distance = Mathf.Sqrt(
                (torso.R - playerTorso.R) * (torso.R - playerTorso.R) +
                (torso.G - playerTorso.G) * (torso.G - playerTorso.G) +
                (torso.B - playerTorso.B) * (torso.B - playerTorso.B));

            if (distance < closest)
            {
                closest = distance;
                nearest = type.TypeName;
            }
        }

        GD.Print($"  the player draws solid; nearest horde colour is {nearest} at {closest:F3} away");

        bool distinct = closest > 0.12f;
        if (!distinct)
            GD.PushError($"  the player is only {closest:F3} from the {nearest} — they will be confused in a crowd");

        return distinct;
    }

    /// The stride must come from walking, and nothing else.
    ///
    /// Three separate wrongs live here, and deriving the phase from world
    /// position commits all of them: a crowd standing on one tile poses
    /// identically, a body knocked backwards walks backwards, and anything held
    /// still by contact freezes mid-step with a foot in the air.
    private bool? StageStrideFollowsWalking(int tick)
    {
        EnemyPool pool = _horde!.Pool;

        if (tick == 1)
        {
            // The horde has to stop simulating first. It chases the player every
            // tick, which gives both bodies a velocity and advances both strides
            // — the first run of this stage measured 0.457 where it expected
            // 0.429, and saw the *stopped* body walk 0.062 of a stride, because
            // the horde had helpfully started it moving between two of these
            // ticks. Owning the clock is the whole point of the stage.
            _horde.SetPhysicsProcess(false);
            pool.Clear();
            _horde.Spawn(new Vector3(6.0f, 0.0f, 0.0f), 0);
            _horde.Spawn(new Vector3(6.0f, 0.0f, 2.0f), 0);
            return null;
        }

        if (tick == 2)
        {
            // Two bodies in the same place, one walking and one stopped.
            pool.Position[0] = new Vector3(6.0f, 0.0f, 0.0f);
            pool.Position[1] = new Vector3(6.0f, 0.0f, 0.0f);
            pool.Stride[0] = 0.0f;
            pool.Stride[1] = 0.0f;
            pool.Velocity[0] = new Vector2(2.4f, 0.0f);
            pool.Velocity[1] = Vector2.Zero;
            pool.Yaw[1] = 1.25f;

            BodyRenderer.Advance(pool, 0.25f);
            return null;
        }

        float walked = pool.Stride[0];
        float stood = pool.Stride[1];
        float heldYaw = pool.Yaw[1];

        // A quarter second at 2.4 m/s is 0.6 m, which is 0.43 of a 1.4 m stride.
        float expected = 2.4f * 0.25f / 1.4f;

        GD.Print($"  same tile, quarter second: the walker advanced {walked:F3} of a stride " +
                 $"(expected {expected:F3}), the stopped one {stood:F3}");
        GD.Print($"  the stopped body kept facing {heldYaw:F2} rad rather than snapping to 0");

        bool advanced = Mathf.Abs(walked - expected) < 0.01f;
        bool held = stood == 0.0f;
        bool keptFacing = Mathf.IsEqualApprox(heldYaw, 1.25f);

        if (!advanced)
            GD.PushError($"  the walker advanced {walked:F3} of a stride, expected {expected:F3}");
        if (!held)
            GD.PushError($"  a body with no velocity advanced its stride by {stood:F4} — is phase coming from position?");
        if (!keptFacing)
            GD.PushError($"  a body with no velocity turned to {heldYaw:F2} — it should hold its heading");

        // And backwards travel must not run the gait backwards past zero into a
        // negative phase, which `fract` in the shader would read as almost a full
        // stride and show as a leg snapping.
        pool.Stride[0] = 0.02f;
        pool.Velocity[0] = new Vector2(-2.4f, 0.0f);
        BodyRenderer.Advance(pool, 0.25f);

        bool wrapped = pool.Stride[0] >= 0.0f && pool.Stride[0] < 1.0f;
        GD.Print($"  walking backwards from 0.02 left the stride at {pool.Stride[0]:F3}");
        if (!wrapped)
            GD.PushError($"  the stride left [0, 1) at {pool.Stride[0]:F3}");

        return advanced && held && keptFacing && wrapped;
    }
}
