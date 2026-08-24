using Godot;

/// One authored body, flattened into arrays the runtime can rebuild a mesh from.
///
/// **Data, not a mesh, and that is the whole point of the type existing.** The
/// horde is drawn by a `MultiMesh`, and a `MultiMesh` loses an imported mesh on
/// pack/save (godot.md:46) — it comes back with the right instance count and no
/// mesh at all, drawing nothing and reporting nothing. Saving a baked `ArrayMesh`
/// to a `.res` and pointing a `MultiMesh` at it walks straight into the same
/// trap, because the mesh would again be a resource owned by another file.
///
/// So the bake stops one step short. This holds the numbers; `BakedBody.Build`
/// constructs the `ArrayMesh` at runtime, exactly as `BodyMeshLibrary` does for
/// the procedural variants, and the mesh is owned by nobody and survives
/// everything.
///
/// The rig channels are the ones `body.gdshader` reads, in the two UV sets
/// `MeshBuilder` writes them to. See `BakeBody` for how they are derived from a
/// skinned model's joints and weights.
[GlobalClass]
public partial class BakedBodyResource : Resource
{
    /// What this was baked from, for a probe to report and a human to find again.
    [Export] public string Source { get; set; } = string.Empty;

    /// The height the body stands at, measured off the baked vertices.
    ///
    /// Stored rather than recomputed because it is what the enemy table balances
    /// against, and a body whose declared height and actual height disagree is
    /// the failure `BodyProbe` exists to catch.
    [Export] public float StandingHeight { get; set; }

    [Export] public Vector3[] Vertices { get; set; } = System.Array.Empty<Vector3>();
    [Export] public Vector3[] Normals { get; set; } = System.Array.Empty<Vector3>();
    [Export] public Color[] Colours { get; set; } = System.Array.Empty<Color>();

    /// `(swing, pivotY)` per vertex — radians of fore-and-aft swing at reference
    /// pace, and the height it turns about.
    [Export] public Vector2[] Rig { get; set; } = System.Array.Empty<Vector2>();

    /// `(phase, bob)` per vertex — the offset in turns that makes one leg the
    /// opposite of the other, and the rise on each footfall.
    [Export] public Vector2[] Rig2 { get; set; } = System.Array.Empty<Vector2>();

    [Export] public int[] Indices { get; set; } = System.Array.Empty<int>();

    public int Triangles => (Indices.Length > 0 ? Indices.Length : Vertices.Length) / 3;

    /// True when every array is the same length and there is something in them.
    ///
    /// Checked rather than assumed: a bake that half-succeeded produces arrays of
    /// different lengths, and `AddSurfaceFromArrays` accepts them and renders
    /// something wrong rather than failing.
    public bool Sound =>
        Vertices.Length > 0
        && Normals.Length == Vertices.Length
        && Colours.Length == Vertices.Length
        && Rig.Length == Vertices.Length
        && Rig2.Length == Vertices.Length;
}
