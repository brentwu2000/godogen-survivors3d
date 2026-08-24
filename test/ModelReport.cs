using Godot;

/// Reports what is actually inside an imported model.
///
///   godot --headless --script test/ModelReport.cs -- res://assets/models/whatever.glb
///
/// Written to answer "is this asset usable here", which is a question with a
/// specific set of answers in this project and no way to see them from a file
/// listing. A `.glb` handed over with a README saying "4,548 vertices, 31 bones"
/// is a claim; what matters is what Godot's importer made of it, whether it can
/// go anywhere near a `MultiMesh`, and how tall it stands next to the bodies
/// already in the game.
///
/// Headless on purpose. This says nothing about whether the thing looks right —
/// `test/BodyShot.cs` is for that — only whether it can be used at all.
public partial class ModelReport : SceneTree
{
    public override void _Initialize()
    {
        string[] args = OS.GetCmdlineUserArgs();
        if (args.Length == 0)
        {
            GD.PushError("usage: ModelReport.cs -- res://path/to/model.glb");
            Quit(1);
            return;
        }

        foreach (string path in args)
            Report(path);

        Quit();
    }

    private void Report(string path)
    {
        var scene = GD.Load<PackedScene>(path);
        if (scene == null)
        {
            GD.PushError($"{path} did not load — is it imported?");
            return;
        }

        Node root = scene.Instantiate();
        GD.Print($"--- {path}");
        GD.Print($"root: {root.Name} ({root.GetType().Name})");

        var counts = new Counts();
        Walk(root, root as Node3D, ref counts);

        GD.Print($"meshes            {counts.Meshes}");
        GD.Print($"triangles         {counts.Triangles}");
        GD.Print($"surfaces          {counts.Surfaces}");
        GD.Print($"skeletons         {counts.Skeletons}  ({counts.Bones} bones)");
        GD.Print($"skinned meshes    {counts.Skinned}");
        GD.Print($"animation players {counts.Players}  ({string.Join(", ", counts.Animations)})");

        if (counts.Bounds.HasValue)
        {
            Aabb box = counts.Bounds.Value;
            GD.Print($"bounds            {box.Size.X:F2} x {box.Size.Y:F2} x {box.Size.Z:F2} m, "
                   + $"base at y={box.Position.Y:F2}");
        }

        // The two rules this project has already paid for, checked rather than
        // remembered. A skinned mesh cannot go in a `MultiMesh` at all, and an
        // imported mesh in one is lost on pack/save (godot.md:46) — so anything
        // that answers yes here can only ever be drawn as its own node, which
        // means it can be the player and cannot be the horde.
        GD.Print(counts.Skinned > 0
            ? "verdict           skinned: one node per instance, never a MultiMesh"
            : "verdict           unskinned: could be instanced, but an imported mesh in a "
              + "MultiMesh is still lost on pack/save");

        root.Free();
        GD.Print("");
    }

    private struct Counts
    {
        public int Meshes;
        public int Surfaces;
        public int Triangles;
        public int Skeletons;
        public int Bones;
        public int Skinned;
        public int Players;
        public System.Collections.Generic.List<string> Animations;
        public Aabb? Bounds;
    }

    private static void Walk(Node node, Node3D? root, ref Counts counts)
    {
        counts.Animations ??= new System.Collections.Generic.List<string>();

        if (node is Skeleton3D skeleton)
        {
            counts.Skeletons++;
            counts.Bones += skeleton.GetBoneCount();
        }

        if (node is AnimationPlayer player)
        {
            counts.Players++;
            foreach (string name in player.GetAnimationList())
            {
                Animation? clip = player.GetAnimation(name);
                counts.Animations.Add($"{name} {clip?.Length ?? 0.0f:F1}s");
            }
        }

        if (node is MeshInstance3D { Mesh: not null } instance)
        {
            counts.Meshes++;
            counts.Surfaces += instance.Mesh.GetSurfaceCount();

            if (instance.Skin != null || !instance.Skeleton.IsEmpty)
                counts.Skinned++;

            for (int surface = 0; surface < instance.Mesh.GetSurfaceCount(); surface++)
            {
                var array = instance.Mesh.SurfaceGetArrays(surface);
                var indices = array[(int)Mesh.ArrayType.Index].AsInt32Array();
                var vertices = array[(int)Mesh.ArrayType.Vertex].AsVector3Array();

                counts.Triangles += (indices.Length > 0 ? indices.Length : vertices.Length) / 3;
            }

            // Measured in the root's space, so the number is the height the thing
            // would stand at in a scene rather than the height of whichever part
            // of it happens to be furthest from its own origin.
            Aabb local = instance.Mesh.GetAabb();
            if (root != null)
                local = Relative(instance, root) * local;

            counts.Bounds = counts.Bounds.HasValue ? counts.Bounds.Value.Merge(local) : local;
        }

        foreach (Node child in node.GetChildren())
            Walk(child, root, ref counts);
    }

    /// A node's transform relative to the instantiated root.
    ///
    /// Composed by walking up rather than read from `GlobalTransform`, which on a
    /// node outside the tree prints an error and returns identity — measuring
    /// every part of the model as if it sat at the origin.
    private static Transform3D Relative(Node3D node, Node3D root)
    {
        Transform3D transform = node.Transform;

        for (Node? parent = node.GetParent(); parent is Node3D parent3D && parent != root;
             parent = parent.GetParent())
        {
            transform = parent3D.Transform * transform;
        }

        return transform;
    }
}
