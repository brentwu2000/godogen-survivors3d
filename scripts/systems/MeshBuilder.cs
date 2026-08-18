using Godot;

/// Accumulates boxes into one `ArrayMesh`.
///
/// Every prop in the arena is built from these. Boxes rather than an imported
/// model, for two reasons that are both about failure modes rather than taste:
///
///   MultiMesh silently loses an imported GLB on pack/save (godot.md:46), and
///   MultiMesh is the only way fifty pieces of cover stay inside the draw-call
///   budget this project has defended since Phase 2. A procedural mesh is the
///   combination that is actually allowed.
///
///   A GLB also drags in collision: the primitive shape has to be measured off
///   its AABB, because `CreateTrimeshShape()` on an imported mesh drops the frame
///   rate below one (godot.md:39). Cover that was boxes to begin with already has
///   its collider.
///
/// Normals are written per face as the boxes are emitted, never by calling
/// `GenerateNormals()` afterwards. A procedural mesh with no normals silently
/// fails to receive shadows (godot.md:45) — it renders perfectly and is simply
/// never darkened, which reads as a lighting setting rather than as missing data.
public sealed class MeshBuilder
{
    private readonly System.Collections.Generic.List<Vector3> _vertices = new();
    private readonly System.Collections.Generic.List<Vector3> _normals = new();
    private readonly System.Collections.Generic.List<Color> _colours = new();

    /// The six faces of a unit cube, as (normal, and the four corners in
    /// counter-clockwise order seen from outside).
    ///
    /// Winding is the thing to get right rather than to work around: turning on
    /// `CullMode.Disabled` to hide a backwards face also removes the shadow, so
    /// the "safety net" is what breaks the lighting.
    private static readonly (Vector3 Normal, Vector3 A, Vector3 B, Vector3 C, Vector3 D)[] Faces =
    {
        (Vector3.Up,      new(-1, 1, -1), new(-1, 1, 1), new(1, 1, 1), new(1, 1, -1)),
        (Vector3.Down,    new(-1, -1, 1), new(-1, -1, -1), new(1, -1, -1), new(1, -1, 1)),
        (Vector3.Forward, new(-1, -1, -1), new(-1, 1, -1), new(1, 1, -1), new(1, -1, -1)),
        (Vector3.Back,    new(1, -1, 1), new(1, 1, 1), new(-1, 1, 1), new(-1, -1, 1)),
        (Vector3.Left,    new(-1, -1, 1), new(-1, 1, 1), new(-1, 1, -1), new(-1, -1, -1)),
        (Vector3.Right,   new(1, -1, -1), new(1, 1, -1), new(1, 1, 1), new(1, -1, 1)),
    };

    /// A box, centred on `centre`, `size` across, optionally spun about Y.
    public void Box(Vector3 centre, Vector3 size, Color colour, float yawDegrees = 0.0f)
    {
        // Converted here, once, because vertex colours and texture pixels do not
        // mean the same thing. A PNG imported as `source_color` is decoded from
        // sRGB to linear before it is shaded; a vertex colour is handed to the
        // shader as-is and treated as linear already. Writing 0.46 in both places
        // therefore produces two visibly different greys — the props came out
        // roughly the square root of their intended value, which read as every
        // piece of cover being made of polystyrene next to an asphalt floor that
        // was correct.
        colour = colour.SrgbToLinear();

        Vector3 half = size * 0.5f;
        float yaw = Mathf.DegToRad(yawDegrees);
        float cos = Mathf.Cos(yaw), sin = Mathf.Sin(yaw);

        foreach ((Vector3 normal, Vector3 a, Vector3 b, Vector3 c, Vector3 d) in Faces)
        {
            // Emitted in reverse of the corner order above, because Godot treats
            // clockwise-from-the-front as front-facing. Wound the other way the
            // boxes still draw — as their own interiors, lit from behind, which
            // looks like every prop being made of black plastic rather than like
            // a culling bug.
            Vector3 n = Spin(normal, cos, sin);
            Triangle(Place(c), Place(b), Place(a), n, colour);
            Triangle(Place(d), Place(c), Place(a), n, colour);
        }

        Vector3 Place(Vector3 corner) => centre + Spin(corner * half, cos, sin);
    }

    private static Vector3 Spin(Vector3 v, float cos, float sin) =>
        new(v.X * cos - v.Z * sin, v.Y, v.X * sin + v.Z * cos);

    private void Triangle(Vector3 a, Vector3 b, Vector3 c, Vector3 normal, Color colour)
    {
        _vertices.Add(a); _vertices.Add(b); _vertices.Add(c);
        _normals.Add(normal); _normals.Add(normal); _normals.Add(normal);
        _colours.Add(colour); _colours.Add(colour); _colours.Add(colour);
    }

    public int TriangleCount => _vertices.Count / 3;

    public ArrayMesh Build()
    {
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = _vertices.ToArray();
        arrays[(int)Mesh.ArrayType.Normal] = _normals.ToArray();
        arrays[(int)Mesh.ArrayType.Color] = _colours.ToArray();

        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }

    /// The height of the tallest vertex, for anything that needs to know how far
    /// a prop sticks up without keeping a second copy of its dimensions.
    public float Height
    {
        get
        {
            float top = 0.0f;
            foreach (Vector3 vertex in _vertices)
                top = Mathf.Max(top, vertex.Y);

            return top;
        }
    }
}
