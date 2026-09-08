using Godot;

/// Turns a `BakedBodyResource` into a mesh the horde can draw.
///
/// The other half of the bake. `BakeBody` reads a skinned `.glb` and writes the
/// numbers; this rebuilds the `ArrayMesh` from them at runtime, which is what
/// keeps the result out of the `MultiMesh` pack/save trap — see
/// `BakedBodyResource`.
///
/// Deliberately the same shape as `BodyMeshLibrary.Build3D`, and deliberately not
/// merged with it. That one composes a body from primitives and knows what a hip
/// is; this one copies arrays and knows nothing. A single function doing both
/// would have to carry two ideas of where a rig channel comes from.
public static class BakedBody
{
    public static ArrayMesh? Build(BakedBodyResource? baked)
    {
        if (baked == null || !baked.Sound)
        {
            GD.PushError($"BakedBody: {baked?.Source ?? "null"} is not a sound bake — "
                       + "the arrays are empty or disagree in length");
            return null;
        }

        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);

        arrays[(int)Mesh.ArrayType.Vertex] = baked.Vertices;
        arrays[(int)Mesh.ArrayType.Normal] = baked.Normals;
        arrays[(int)Mesh.ArrayType.Color] = baked.Colours;

        // The two UV sets are the rig. `body.gdshader` reads swing and pivot from
        // UV and phase and bob from UV2; nothing here is a texture coordinate and
        // no texture is ever sampled.
        arrays[(int)Mesh.ArrayType.TexUV] = baked.Rig;
        arrays[(int)Mesh.ArrayType.TexUV2] = baked.Rig2;

        if (baked.Indices.Length > 0)
            arrays[(int)Mesh.ArrayType.Index] = baked.Indices;

        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }

    /// Joins two vertex-rigged meshes into one draw surface.
    ///
    /// **Indexed or not is a property of the input, and reading it wrong is
    /// silent until the frame is drawn.** This joined five arrays and never the
    /// index buffer, which is correct for two non-indexed meshes and produces
    /// nothing at all for anything else: the vertices survive, no triangle
    /// references them, and Godot refuses the surface with "vertex amount must be
    /// a multiple of 3" once per frame forever. The player is then invisible,
    /// holding an invisible weapon, in a game that otherwise runs.
    ///
    /// It went unseen because the only two meshes that ever met here were
    /// non-indexed: `BodyMeshLibrary` builds triangle soup, and every survivor
    /// bake before this one was thrown away for looking wrong rather than for
    /// failing to draw. An authored model is indexed — that is most of why it is
    /// 17,087 vertices and not 70,890 — so the first good one lit this up.
    ///
    /// Sequential indices are what "non-indexed" means, so a mesh without them
    /// can be given them and the mixed case stops being a case.
    public static ArrayMesh Append(ArrayMesh body, ArrayMesh addition)
    {
        if (addition.GetSurfaceCount() == 0)
            return body;

        Godot.Collections.Array a = body.SurfaceGetArrays(0);
        Godot.Collections.Array b = addition.SurfaceGetArrays(0);

        var firstVertices = a[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        var secondVertices = b[(int)Mesh.ArrayType.Vertex].AsVector3Array();

        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = Join(firstVertices, secondVertices);
        arrays[(int)Mesh.ArrayType.Normal] = Join(a[(int)Mesh.ArrayType.Normal].AsVector3Array(), b[(int)Mesh.ArrayType.Normal].AsVector3Array());
        arrays[(int)Mesh.ArrayType.Color] = Join(a[(int)Mesh.ArrayType.Color].AsColorArray(), b[(int)Mesh.ArrayType.Color].AsColorArray());
        arrays[(int)Mesh.ArrayType.TexUV] = Join(a[(int)Mesh.ArrayType.TexUV].AsVector2Array(), b[(int)Mesh.ArrayType.TexUV].AsVector2Array());
        arrays[(int)Mesh.ArrayType.TexUV2] = Join(a[(int)Mesh.ArrayType.TexUV2].AsVector2Array(), b[(int)Mesh.ArrayType.TexUV2].AsVector2Array());

        var firstIndices = a[(int)Mesh.ArrayType.Index].AsInt32Array();
        var secondIndices = b[(int)Mesh.ArrayType.Index].AsInt32Array();

        if (firstIndices.Length > 0 || secondIndices.Length > 0)
            arrays[(int)Mesh.ArrayType.Index] = Join(
                Indices(firstIndices, firstVertices.Length, 0),
                Indices(secondIndices, secondVertices.Length, firstVertices.Length));

        var combined = new ArrayMesh();
        combined.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return combined;
    }

    /// The mesh's own indices shifted into the joined buffer, or the sequential
    /// ones it was drawing with implicitly.
    private static int[] Indices(int[] indices, int vertices, int offset)
    {
        var shifted = new int[indices.Length > 0 ? indices.Length : vertices];

        for (int i = 0; i < shifted.Length; i++)
            shifted[i] = offset + (indices.Length > 0 ? indices[i] : i);

        return shifted;
    }

    private static T[] Join<T>(T[] first, T[] second)
    {
        var joined = new T[first.Length + second.Length];
        System.Array.Copy(first, joined, first.Length);
        System.Array.Copy(second, 0, joined, first.Length, second.Length);
        return joined;
    }
}
