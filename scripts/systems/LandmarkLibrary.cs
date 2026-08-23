using Godot;

/// The three imported landmarks.
public enum LandmarkKind
{
    /// A lattice transmission tower, 12.7 m. The tallest thing in the arena and
    /// the only one visible over the fog from across it.
    Pylon,

    /// A ribbed silo with a conical roof, 10.6 m. Reads as a place rather than
    /// as a piece of cover.
    Silo,

    /// A crushed service coach, 8.6 m long and under three tall. The only one of
    /// the three that is cover before it is a beacon.
    Coach,
}

/// Loads, measures and instantiates the glTF landmarks.
///
/// These are the only imported meshes in the game — everything else is built by
/// `MeshBuilder` at runtime. They exist because three shapes were worth having
/// that `MeshBuilder` cannot make: a tapering lattice, a cone, and a body panel
/// that has been dented. `art-src/models/build.mjs` authors them in three.js and
/// writes the `.glb` files; nothing at runtime knows that happened.
///
/// **Never a MultiMesh.** An imported mesh in a `MultiMesh` is lost the moment
/// the scene is packed and saved: the packer walks the tree for resources owned
/// by the scene, an imported mesh is owned by its own `.glb`, and what comes back
/// after a round trip is a `MultiMesh` with the right instance count and no mesh
/// at all. There are three landmarks on a map. Three nodes is not a budget
/// problem.
///
/// **Never a trimesh collider.** `ConcavePolygonShape3D` from a 564-triangle
/// lattice takes the frame under a second on generation and then makes every
/// raycast that touches it unreliable (godot.md:39). The collider is a box, the
/// flow field sees a rectangle, and the player walks around the outline rather
/// than through the legs — which at this scale is what they would do anyway.
public static class LandmarkLibrary
{
    private static readonly string[] Paths =
    {
        "res://assets/models/pylon.glb",
        "res://assets/models/silo.glb",
        "res://assets/models/coach.glb",
    };

    private static readonly Aabb[] Bounds = new Aabb[Paths.Length];
    private static readonly bool[] Measured = new bool[Paths.Length];

    /// Half-extents on the ground, in metres.
    ///
    /// Measured off the instantiated model rather than written down. A number in
    /// a table beside a mesh is a number that stops being true the first time the
    /// mesh is edited, and the failure is a landmark whose collider is a metre
    /// narrower than it is — which reads as the player clipping into it.
    public static Vector2 Footprint(LandmarkKind kind)
    {
        Aabb bounds = Measure(kind);
        return new Vector2(bounds.Size.X * 0.5f, bounds.Size.Z * 0.5f);
    }

    public static float Height(LandmarkKind kind) => Measure(kind).Size.Y;

    /// The horizontal centre of the model, relative to its own origin.
    ///
    /// The coach is nine metres long and not symmetric about its origin once the
    /// crush has been applied. Siting it by its origin and colliding it by its
    /// bounds would put the box a foot to one side of the bus.
    public static Vector2 Centre(LandmarkKind kind)
    {
        Aabb bounds = Measure(kind);
        return new Vector2(
            bounds.Position.X + bounds.Size.X * 0.5f,
            bounds.Position.Z + bounds.Size.Z * 0.5f);
    }

    /// A drawable instance. Visual only — the caller owns the collider.
    public static Node3D? Instantiate(LandmarkKind kind)
    {
        var scene = GD.Load<PackedScene>(Paths[(int)kind]);
        if (scene == null)
        {
            GD.PushError($"LandmarkLibrary: missing {Paths[(int)kind]} — run art-src/models/build.mjs");
            return null;
        }

        var node = scene.Instantiate<Node3D>();
        node.Name = kind.ToString();

        // No shadow from the pylon. It is a lattice: the shadow map resolves it
        // as a grey smear the size of the tower, which is worse than no shadow
        // and costs the most of the three.
        if (kind == LandmarkKind.Pylon)
            SetShadows(node, GeometryInstance3D.ShadowCastingSetting.Off);

        return node;
    }

    private static void SetShadows(Node node, GeometryInstance3D.ShadowCastingSetting setting)
    {
        if (node is GeometryInstance3D geometry)
            geometry.CastShadow = setting;

        foreach (Node child in node.GetChildren())
            SetShadows(child, setting);
    }

    /// Instantiates once, measures, and throws the instance away.
    ///
    /// `PackedScene.GetState()` could be walked without instantiating, and it
    /// would be reading the scene's serialised properties to reconstruct
    /// something the engine will happily compute. Three instantiations, once per
    /// process, at startup.
    private static Aabb Measure(LandmarkKind kind)
    {
        int index = (int)kind;
        if (Measured[index])
            return Bounds[index];

        Measured[index] = true;
        Bounds[index] = new Aabb(Vector3.Zero, new Vector3(2.0f, 2.0f, 2.0f));

        var scene = GD.Load<PackedScene>(Paths[index]);
        if (scene == null)
        {
            GD.PushError($"LandmarkLibrary: missing {Paths[index]} — falling back to a 2 m cube");
            return Bounds[index];
        }

        var root = scene.Instantiate<Node3D>();
        Aabb? total = null;
        Accumulate(root, root.Transform.Inverse(), ref total);
        root.Free();

        if (total.HasValue)
            Bounds[index] = total.Value;

        return Bounds[index];
    }

    private static void Accumulate(Node node, Transform3D toRoot, ref Aabb? total)
    {
        if (node is MeshInstance3D { Mesh: not null } mesh)
        {
            Aabb local = (toRoot * mesh.GlobalTransformOrLocal()) * mesh.Mesh.GetAabb();
            total = total.HasValue ? total.Value.Merge(local) : local;
        }

        foreach (Node child in node.GetChildren())
            Accumulate(child, toRoot, ref total);
    }
}

internal static class LandmarkTransformExtension
{
    /// The node's transform relative to the instantiated root.
    ///
    /// `GlobalTransform` is unavailable: the instance is not in the tree and
    /// asking for it prints an error and returns identity, which would measure
    /// every part of the model as if it sat at the origin — a silo whose bounds
    /// are the size of its own roof.
    public static Transform3D GlobalTransformOrLocal(this Node3D node)
    {
        Transform3D transform = node.Transform;

        for (Node? parent = node.GetParent(); parent is Node3D parent3D; parent = parent.GetParent())
            transform = parent3D.Transform * transform;

        return transform;
    }
}
