using Godot;

/// Builds the visible floor from `Terrain`, at startup.
///
/// In `_Ready` rather than in a `.tscn`, because a two hundred metre floor at two
/// and a half metre spacing is 6,561 vertices and a Godot scene file is text — a
/// packed mesh that size makes `Main.tscn` several megabytes of numbers that
/// nothing can review and every regeneration rewrites.
///
/// **Winding decides whether any of this is visible**, and the rule is not the
/// one that sounds right. Godot builds a triangle's normal as
/// `(v0 - v2) × (v0 - v1)` — the negative of the right-hand rule — so a floor is
/// front-facing from above when the right-hand normal of its vertex order points
/// *down*. Get it backwards and every triangle is culled from every angle the
/// camera can reach, and against the black depth fog that is indistinguishable
/// from a mesh that never built. This was authored backwards first, and the
/// screenshot was a void with the scatter floating in it.
///
/// Extends StaticBody3D rather than Node3D because the script is attached to the
/// Ground body itself, which owns the flat box collider the simulation still
/// uses. A script whose base class is narrower than the node it sits on does not
/// attach at all.
public partial class GroundMesh : StaticBody3D
{
    /// Metres across. Larger than the arena so the edge is always past the fog.
    [Export] public float Size { get; set; } = 200.0f;

    /// Metres between vertices.
    ///
    /// Two and a half against a coarse wavelength of eighteen is about seven
    /// samples per wave, which is enough for the silhouette to read as a curve
    /// rather than as facets. Halving it quadruples the vertex count for a
    /// difference nobody can see at this amplitude.
    [Export] public float Spacing { get; set; } = 2.5f;

    public override void _Ready() => Rebuild();

    /// Builds, or rebuilds, the floor against the current `Terrain.Offset`.
    ///
    /// Public because `_Ready` is too early. The offset is a per-seed value and
    /// `LevelGenerator` settles it while generating, which happens after every
    /// other node in the scene is ready — so the floor built in `_Ready` is the
    /// floor of whatever seed ran last. `LevelGenerator` calls this once the
    /// offset is fixed.
    ///
    /// The symptom of not doing it was not a visible landscape mismatch, which is
    /// what makes it worth a method: the ground still looked like ground, and
    /// every object planted on it still looked planted, because both were plausible
    /// surfaces. `TerrainProbe` reported 12,744 of 12,800 triangles off the
    /// height field — the only place the disagreement was legible.
    public void Rebuild()
    {
        var mesh = GetNodeOrNull<MeshInstance3D>("Mesh");
        if (mesh == null)
        {
            GD.PushWarning("GroundMesh: no Mesh child — the floor will stay flat");
            return;
        }

        mesh.Mesh = Build();

        // The plane it replaced had its own bounds; a generated mesh gets its
        // AABB from the vertices, which is correct here and worth not overriding.
    }

    private ArrayMesh Build()
    {
        int steps = Mathf.Max(2, Mathf.RoundToInt(Size / Spacing));
        float half = Size * 0.5f;
        float step = Size / steps;

        int quads = steps * steps;
        var vertices = new Vector3[quads * 6];
        var normals = new Vector3[quads * 6];

        int at = 0;

        for (int gz = 0; gz < steps; gz++)
        {
            for (int gx = 0; gx < steps; gx++)
            {
                float x0 = -half + gx * step;
                float z0 = -half + gz * step;
                float x1 = x0 + step;
                float z1 = z0 + step;

                Vector3 a = new(x0, Terrain.Height(x0, z0), z0);
                Vector3 b = new(x1, Terrain.Height(x1, z0), z0);
                Vector3 c = new(x1, Terrain.Height(x1, z1), z1);
                Vector3 d = new(x0, Terrain.Height(x0, z1), z1);

                // Counter-clockwise seen from above — a→b→c→d walks the quad in
                // increasing X then increasing Z.
                //
                // Godot's front face is the one whose *engine* normal points at
                // the camera, and the engine normal is the negative of the
                // right-hand-rule normal: `Plane(v0, v1, v2)` is built from
                // `(v0 - v2) × (v0 - v1)`. So a floor is front-facing from above
                // when its right-hand normal points *down*, which is this order
                // and not the other one.
                //
                // Reversed, the entire floor is back-facing from every angle the
                // camera can reach, and behind black depth fog that looks exactly
                // like a mesh that did not build. It was authored the wrong way
                // round first and rendered as an empty void with the scatter
                // floating in it.
                Emit(vertices, normals, ref at, a, b, c);
                Emit(vertices, normals, ref at, a, c, d);
            }
        }

        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices;
        arrays[(int)Mesh.ArrayType.Normal] = normals;

        var built = new ArrayMesh();
        built.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return built;
    }

    /// One triangle with a flat normal.
    ///
    /// Per-face rather than smoothed. `GenerateNormals()` on a mesh this size is
    /// slower than computing them here, and a mesh with no normals at all renders
    /// perfectly and is simply never darkened (godot.md:45) — which reads as a
    /// lighting setting rather than as missing data.
    private static void Emit(Vector3[] vertices, Vector3[] normals, ref int at,
                            Vector3 a, Vector3 b, Vector3 c)
    {
        // Negated, to match the winding above. The right-hand normal of a
        // front-facing floor triangle points into the ground; the shading normal
        // has to point at the sky, or the whole floor is lit from underneath and
        // comes out uniformly black — the same symptom as the culling bug, from a
        // different cause, which is worth knowing before diagnosing the next one.
        Vector3 normal = -(b - a).Cross(c - a).Normalized();

        // Upward, always. A degenerate triangle — three collinear points on flat
        // ground — gives a zero-length cross product and normalises to NaN, which
        // renders as a black speck that moves with the camera.
        if (normal.Y < 0.0f || !float.IsFinite(normal.Y))
            normal = Vector3.Up;

        vertices[at] = a; normals[at++] = normal;
        vertices[at] = b; normals[at++] = normal;
        vertices[at] = c; normals[at++] = normal;
    }
}
