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
}
