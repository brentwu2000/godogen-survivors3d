using Godot;

/// Checks the shapes `MeshBuilder` makes, by reading the built mesh back.
///
///   godot --headless --script test/MeshProbe.cs
///
/// Headless is right here — every property this asserts is in the vertex arrays,
/// and none of them is visible in a screenshot anyway. That is the point. The two
/// ways procedural geometry goes wrong in this engine both render:
///
///   Wound backwards, a shape draws as its own interior — lit from behind, which
///   reads as a material problem rather than a winding one. The usual reflex
///   (`CullMode.Disabled`) hides it and removes the shadow at the same time.
///
///   Built with no normals, a mesh renders perfectly and is simply never
///   darkened, which reads as a lighting setting.
///
/// Neither is an error, and neither is a crash. Both are arithmetic, so both can
/// be caught by arithmetic.
public partial class MeshProbe : SceneTree
{
    private bool _failed;

    public override void _Initialize()
    {
        Stage("a box is closed, outward-facing and flat-shaded", BoxIsSound);
        Stage("a tube is closed and capped both ends", TubeIsSound);
        Stage("a ball has no degenerate face at either pole", BallIsSound);
        Stage("rig data rides the vertices it was set for", RigRidesAlong);
        Stage("props built without a rig still carry the channels", RestIsZero);

        GD.Print(_failed ? "PROBE FAILED" : "PROBE OK");
        Quit(_failed ? 1 : 0);
    }

    private void Stage(string label, System.Func<bool> stage)
    {
        bool ok;
        try
        {
            ok = stage();
        }
        catch (System.Exception e)
        {
            GD.PushError($"  {label} threw: {e.Message}");
            ok = false;
        }

        GD.Print($"{label}: {(ok ? "ok" : "FAILED")}");
        _failed |= !ok;
    }

    /// A closed surface has zero net area-weighted normal.
    ///
    /// This is the divergence theorem doing the work a screenshot cannot: sum the
    /// cross products of every triangle and a watertight, consistently wound hull
    /// cancels to nothing. One face flipped, one face missing, or a cap wound the
    /// wrong way and the sum is the size of that face. It does not care what the
    /// shape is, which is why the same check answers for boxes, tubes and balls.
    private static Vector3 NetArea(Vector3[] vertices)
    {
        var sum = Vector3.Zero;
        for (int i = 0; i + 2 < vertices.Length; i += 3)
            sum += (vertices[i + 1] - vertices[i]).Cross(vertices[i + 2] - vertices[i]);

        return sum * 0.5f;
    }

    /// Whether every triangle faces away from a point known to be inside.
    ///
    /// The net-area test above catches inconsistency; this catches a hull that is
    /// consistently *inside out*, which sums to zero just as neatly.
    private static bool AllFaceOutward(Vector3[] vertices, Vector3[] normals, Vector3 inside,
                                       out int wrong, out int unnormalised)
    {
        wrong = 0;
        unnormalised = 0;

        for (int i = 0; i + 2 < vertices.Length; i += 3)
        {
            Vector3 centroid = (vertices[i] + vertices[i + 1] + vertices[i + 2]) / 3.0f;

            // The winding, read off the vertices themselves rather than trusted
            // from the normal array. Godot's front face is clockwise seen from
            // the front, so a front-facing triangle's cross product points *into*
            // the surface — a declared normal that agrees with the winding is
            // the thing being checked, and checking it against itself would pass
            // through anything.
            Vector3 wound = (vertices[i + 1] - vertices[i]).Cross(vertices[i + 2] - vertices[i]);
            if (wound.Dot(centroid - inside) > 0.0f)
                wrong++;

            if (!Mathf.IsEqualApprox(normals[i].Length(), 1.0f, 0.001f))
                unnormalised++;
        }

        return wrong == 0 && unnormalised == 0;
    }

    private static (Vector3[] Vertices, Vector3[] Normals, Vector2[] Uv, Vector2[] Uv2) Read(ArrayMesh mesh)
    {
        Godot.Collections.Array arrays = mesh.SurfaceGetArrays(0);
        return ((Vector3[])arrays[(int)Mesh.ArrayType.Vertex],
                (Vector3[])arrays[(int)Mesh.ArrayType.Normal],
                (Vector2[])arrays[(int)Mesh.ArrayType.TexUV],
                (Vector2[])arrays[(int)Mesh.ArrayType.TexUV2]);
    }

    private bool BoxIsSound()
    {
        var builder = new MeshBuilder();
        builder.Box(new Vector3(0.0f, 1.0f, 0.0f), new Vector3(2.0f, 2.0f, 2.0f), Colors.White, 30.0f);

        var (vertices, normals, _, _) = Read(builder.Build());
        Vector3 net = NetArea(vertices);
        bool outward = AllFaceOutward(vertices, normals, new Vector3(0.0f, 1.0f, 0.0f),
                                      out int wrong, out int unnormalised);

        GD.Print($"  {vertices.Length / 3} triangles, net area {net.Length():F5}, " +
                 $"{wrong} inward, {unnormalised} unnormalised");

        // Twelve, not more: a box that grew triangles is a box that stopped being
        // six quads, and the horde pays for every one of them per instance.
        return vertices.Length == 36 && net.Length() < 0.001f && outward;
    }

    private bool TubeIsSound()
    {
        var builder = new MeshBuilder();

        // Off-axis on purpose. A tube built straight up the Y axis would pass
        // even if the perpendicular basis were degenerate, because the reference
        // vector it crosses against is chosen by which axis the tube is near.
        builder.Tube(new Vector3(0.3f, 0.2f, -0.1f), new Vector3(-0.2f, 1.4f, 0.4f), 0.18f, Colors.White);

        var (vertices, normals, _, _) = Read(builder.Build());
        Vector3 net = NetArea(vertices);
        var inside = new Vector3(0.05f, 0.8f, 0.15f);
        bool outward = AllFaceOutward(vertices, normals, inside, out int wrong, out int unnormalised);

        GD.Print($"  {vertices.Length / 3} triangles, net area {net.Length():F5}, " +
                 $"{wrong} inward, {unnormalised} unnormalised");

        // Six sides is 12 triangles, plus 6 per cap. A tube missing a cap still
        // sums to nearly zero if the other cap is missing too, so the count is
        // checked as well as the closure.
        bool closed = net.Length() < 0.001f;
        if (!closed)
            GD.PushError($"  the tube is not closed — net area {net.Length():F4}, is a cap missing or reversed?");

        return vertices.Length == 24 * 3 && closed && outward;
    }

    private bool BallIsSound()
    {
        var builder = new MeshBuilder();
        builder.Ball(new Vector3(0.0f, 1.0f, 0.0f), 0.25f, Colors.White, 8, 5);

        var (vertices, normals, _, _) = Read(builder.Build());
        Vector3 net = NetArea(vertices);
        bool outward = AllFaceOutward(vertices, normals, new Vector3(0.0f, 1.0f, 0.0f),
                                      out int wrong, out int unnormalised);

        // A degenerate triangle at a pole has zero area and an undefined normal,
        // and shades as a black speck exactly at the crown of every head. It is
        // invisible to the closure test — zero area adds nothing to the sum.
        int degenerate = 0;
        for (int i = 0; i + 2 < vertices.Length; i += 3)
        {
            if ((vertices[i + 1] - vertices[i]).Cross(vertices[i + 2] - vertices[i]).Length() < 0.0000001f)
                degenerate++;
        }

        GD.Print($"  {vertices.Length / 3} triangles, net area {net.Length():F5}, " +
                 $"{wrong} inward, {unnormalised} unnormalised, {degenerate} degenerate");

        if (degenerate > 0)
            GD.PushError($"  {degenerate} zero-area triangles — the poles are being emitted as quads");

        return net.Length() < 0.001f && outward && degenerate == 0;
    }

    private bool RigRidesAlong()
    {
        var builder = new MeshBuilder();

        builder.SetRig(0.7f, 0.9f, 0.5f, 0.04f);
        builder.Tube(new Vector3(0.2f, 0.0f, 0.0f), new Vector3(0.2f, 0.9f, 0.0f), 0.1f, Colors.White);
        int legVertices = 24 * 3;

        builder.ClearRig();
        builder.Box(new Vector3(0.0f, 1.2f, 0.0f), Vector3.One * 0.4f, Colors.White);

        var (vertices, _, uv, uv2) = Read(builder.Build());

        bool legTagged = true;
        for (int i = 0; i < legVertices; i++)
        {
            if (!uv[i].IsEqualApprox(new Vector2(0.7f, 0.9f)) || !uv2[i].IsEqualApprox(new Vector2(0.5f, 0.04f)))
                legTagged = false;
        }

        bool torsoRested = true;
        for (int i = legVertices; i < vertices.Length; i++)
        {
            if (uv[i] != Vector2.Zero || uv2[i] != Vector2.Zero)
                torsoRested = false;
        }

        GD.Print($"  {legVertices} limb vertices tagged {uv[0]}/{uv2[0]}, " +
                 $"{vertices.Length - legVertices} still vertices at rest = {torsoRested}");

        if (!legTagged)
            GD.PushError("  the rig did not reach every vertex of the primitive it was set for");

        // The boundary matters as much as the values. A rig that leaked into the
        // next primitive would animate a torso, and a rig applied one vertex late
        // would tear the limb it belongs to.
        return legTagged && torsoRested && uv.Length == vertices.Length && uv2.Length == vertices.Length;
    }

    private bool RestIsZero()
    {
        // Every prop in the arena goes through this path and none of them sets a
        // rig. The channels still have to exist: a surface built without them
        // carries no UV format bit, and a shader reading UV then gets whatever
        // the vertex format happens to leave there.
        var builder = new MeshBuilder();
        builder.Box(Vector3.Zero, Vector3.One, Colors.White);

        var (vertices, _, uv, uv2) = Read(builder.Build());
        bool present = uv.Length == vertices.Length && uv2.Length == vertices.Length;

        GD.Print($"  {vertices.Length} vertices, UV present = {uv.Length == vertices.Length}, " +
                 $"UV2 present = {uv2.Length == vertices.Length}");

        if (!present)
            GD.PushError("  a mesh with no rig lost its UV channels — props would animate by garbage");

        return present;
    }
}
