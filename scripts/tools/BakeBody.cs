using Godot;

/// Bakes a skinned `.glb` into a `BakedBodyResource` the horde can draw.
///
///   godot --headless --script scripts/tools/BakeBody.cs -- \
///         res://assets/models/thing.glb res://resources/bodies/thing.res 2.4
///
/// The third argument is the height the body should stand at, in metres. It is
/// required rather than inferred: the enemy table is what the game balances
/// against, and a model authored at whatever scale its generator felt like is not
/// a design decision.
///
/// ## Why this exists
///
/// An authored model cannot enter the horde as it arrives. The horde is one
/// `MultiMesh` per variant, a `MultiMesh` loses an imported mesh on pack/save
/// (godot.md:46), and a skinned mesh cannot go in one at all. The bodies that do
/// work are built by `MeshBuilder` at runtime and carry their animation in two UV
/// sets that `body.gdshader` reads:
///
///     UV  = (swing radians, pivot height)
///     UV2 = (phase in turns, bob metres)
///
/// A `.glb` carries `JOINTS_0` and `WEIGHTS_0` instead — the same information, in
/// a form the shader cannot read and a `MultiMesh` cannot carry. This converts
/// one into the other, once, offline.
///
/// ## How the rig is derived
///
/// Per vertex, take the joint with the largest weight and look at what its bone
/// is called. A thigh, a shin or a foot swings about the hip; an upper arm, a
/// forearm or a hand swings about the shoulder; everything else is torso and only
/// bobs. Which side it is on comes from the bone name too, and sets the half-turn
/// phase offset that stops a walk reading as a march.
///
/// This is cruder than skinning and that is the trade being made deliberately: a
/// hundred and eighty animated bodies in one draw call, against a per-bone
/// transform for each of them. The result is the same gait the procedural bodies
/// already walk with, on geometry somebody authored.
public partial class BakeBody : SceneTree
{
    public override void _Initialize()
    {
        string[] args = OS.GetCmdlineUserArgs();
        if (args.Length < 3 || !float.TryParse(args[2], out float height))
        {
            GD.PushError("usage: BakeBody.cs -- <source.glb> <out.res> <height metres> "
                       + "[swing] [armSwing] [bob]");
            Quit(1);
            return;
        }

        // Defaults matched to the walker, which is the variant everything else is
        // read against. A bake that wants a different gait says so.
        float legSwing = args.Length > 3 && float.TryParse(args[3], out float a) ? a : 0.55f;
        float armSwing = args.Length > 4 && float.TryParse(args[4], out float b) ? b : 0.30f;
        float bob = args.Length > 5 && float.TryParse(args[5], out float c) ? c : 0.035f;

        Quit(Bake(args[0], args[1], height, legSwing, armSwing, bob) ? 0 : 1);
    }

    private static bool Bake(string source, string destination, float height,
                             float legSwing, float armSwing, float bob)
    {
        var packed = GD.Load<PackedScene>(source);
        if (packed == null)
        {
            GD.PushError($"{source} did not load — is it imported?");
            return false;
        }

        Node root = packed.Instantiate();

        MeshInstance3D? instance = FindMesh(root);
        Skeleton3D? skeleton = FindSkeleton(root);

        if (instance?.Mesh == null)
        {
            GD.PushError($"{source} has no mesh to bake");
            root.Free();
            return false;
        }

        if (skeleton == null)
        {
            GD.PushError($"{source} has no Skeleton3D — there is nothing to derive a rig from. "
                       + "An unskinned model can be used as a landmark or a prop; the horde needs "
                       + "to know which vertices are legs.");
            root.Free();
            return false;
        }

        var baked = new BakedBodyResource { Source = source };

        // The mesh node's own transform, which is not identity and cannot be
        // ignored. Godot's glTF importer puts the Y-up conversion on the node, so
        // the arrays inside the mesh are in whatever space the exporter used — on
        // the first model baked that made the legs the highest thing in the body
        // and the hip come out above the shoulder.
        var toRoot = Transform3D.Identity;
        if (root is Node3D root3D)
            toRoot = Relative(instance, root3D);

        if (!Convert(instance, skeleton, baked, height, legSwing, armSwing, bob, toRoot))
        {
            root.Free();
            return false;
        }

        root.Free();

        Error saved = ResourceSaver.Save(baked, destination);
        if (saved != Error.Ok)
        {
            GD.PushError($"could not write {destination}: {saved}");
            return false;
        }

        GD.Print($"baked {source}");
        GD.Print($"  {baked.Triangles} triangles, {baked.Vertices.Length} vertices, "
               + $"standing {baked.StandingHeight:F2} m");
        GD.Print($"  -> {destination}");
        return true;
    }

    /// The rig channels a bone maps to.
    private enum Limb
    {
        Torso,
        Leg,
        Arm,
    }

    private static bool Convert(MeshInstance3D instance, Skeleton3D skeleton, BakedBodyResource baked,
                                float height, float legSwing, float armSwing, float bob,
                                Transform3D toRoot)
    {
        Godot.Collections.Array arrays = instance.Mesh.SurfaceGetArrays(0);

        var vertices = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        var normals = arrays[(int)Mesh.ArrayType.Normal].AsVector3Array();
        var colours = arrays[(int)Mesh.ArrayType.Color].AsColorArray();
        var bones = arrays[(int)Mesh.ArrayType.Bones].AsInt32Array();
        var weights = arrays[(int)Mesh.ArrayType.Weights].AsFloat32Array();
        var indices = arrays[(int)Mesh.ArrayType.Index].AsInt32Array();

        if (vertices.Length == 0)
        {
            GD.PushError("  surface 0 has no vertices");
            return false;
        }

        if (bones.Length == 0 || weights.Length == 0)
        {
            GD.PushError("  the mesh has no joints or weights — it is not skinned, so there is "
                       + "no way to tell a leg from a chest");
            return false;
        }

        // Four influences per vertex is the glTF norm and what Godot hands back.
        int perVertex = bones.Length / vertices.Length;
        if (perVertex <= 0)
        {
            GD.PushError($"  {bones.Length} bone indices for {vertices.Length} vertices");
            return false;
        }

        // Scaled so the tallest vertex lands at the requested height, and dropped
        // so the lowest sits at zero. Both matter: the enemy table is balanced
        // against a height, and a body whose feet are not at its origin is planted
        // through the floor by everything that draws it.
        // Into the model root's space first. Everything below — the extents, the
        // pivots, the placed vertices — is measured there, because that is the
        // space the game will draw the body in.
        for (int i = 0; i < vertices.Length; i++)
            vertices[i] = toRoot * vertices[i];

        for (int i = 0; i < normals.Length; i++)
            normals[i] = (toRoot.Basis * normals[i]).Normalized();

        float low = float.MaxValue;
        float high = float.MinValue;
        foreach (Vector3 vertex in vertices)
        {
            low = Mathf.Min(low, vertex.Y);
            high = Mathf.Max(high, vertex.Y);
        }

        float span = Mathf.Max(0.0001f, high - low);
        float scale = height / span;

        // Every vertex classified before any of them are written, because the
        // pivots are measured from the classification.
        var limbs = new Limb[vertices.Length];
        var phases = new float[vertices.Length];

        // The joint index a vertex carries is an index into the *skin's* bind
        // list, not into the skeleton's bones.
        //
        // They are often the same and there is no reason they have to be. Reading
        // bone names straight off the vertex index scrambled the classification on
        // the first model tried: it put the hip at 1.89 m and the shoulder at
        // 1.92 m on a two-metre body, which is a rig whose arms and legs turn
        // about a line above its own head. Nothing errors, and the body renders
        // perfectly and folds in half when it walks.
        string[] jointNames = JointNames(instance, skeleton);

        var perBone = new System.Collections.Generic.Dictionary<string, int>();

        for (int i = 0; i < vertices.Length; i++)
        {
            int dominant = Dominant(bones, weights, i, perVertex);
            string bone = dominant >= 0 && dominant < jointNames.Length
                ? jointNames[dominant]
                : string.Empty;

            (limbs[i], phases[i]) = Classify(bone);

            perBone.TryGetValue(bone, out int seen);
            perBone[bone] = seen + 1;
        }

        // Where the limbs turn about, measured off the *vertices* rather than off
        // the skeleton's rest pose.
        //
        // The rest pose was the obvious source and gave a hip and a shoulder at
        // exactly the same height on the first model tried — some exporters leave
        // the bone rests flat and put the bind pose in the skin, and a rig whose
        // arms and legs turn about one line is a body that folds in half.
        //
        // The geometry cannot lie about this in the same way: the hip is the top
        // of the leg vertices and the shoulder is the top of the arm vertices,
        // which is not an approximation of the definition, it is the definition.
        float hipY = TopOf(vertices, limbs, Limb.Leg, low, scale, height * 0.46f);
        float shoulderY = TopOf(vertices, limbs, Limb.Arm, low, scale, height * 0.80f);

        var rig = new Vector2[vertices.Length];
        var rig2 = new Vector2[vertices.Length];
        var placed = new Vector3[vertices.Length];
        var tint = new Color[vertices.Length];

        int legs = 0, arms = 0;

        for (int i = 0; i < vertices.Length; i++)
        {
            placed[i] = new Vector3(vertices[i].X * scale,
                                    (vertices[i].Y - low) * scale,
                                    vertices[i].Z * scale);

            tint[i] = i < colours.Length ? colours[i] : Colors.White;

            switch (limbs[i])
            {
                case Limb.Leg:
                    rig[i] = new Vector2(legSwing, hipY);
                    legs++;
                    break;

                case Limb.Arm:
                    rig[i] = new Vector2(armSwing, shoulderY);
                    arms++;
                    break;

                default:
                    // No swing, and no pivot either — a pivot with a zero swing
                    // does nothing, and leaving a stale one in would make the
                    // channel unreadable to anyone checking a bake by eye.
                    rig[i] = Vector2.Zero;
                    break;
            }

            // Bob is on every vertex including the torso, so the body rises as one
            // piece. Applying it to the limbs alone lifts the legs off the hips on
            // every footfall — four centimetres of gap that reads as a body coming
            // apart.
            rig2[i] = new Vector2(phases[i], bob);
        }

        if (legs == 0)
        {
            GD.PushError("  no vertex was classified as a leg. The bone names do not look like "
                       + "anything this knows: expected Thigh/Shin/Foot and UpperArm/LowerArm/Hand, "
                       + $"got names like '{FirstNames(skeleton)}'");
            return false;
        }

        if (arms == 0)
            GD.PushWarning("  no vertex was classified as an arm — the body will walk without swinging");

        baked.Vertices = placed;
        baked.Normals = normals.Length == vertices.Length ? normals : RecomputeNormals(placed, indices);
        baked.Colours = tint;
        baked.Rig = rig;
        baked.Rig2 = rig2;
        baked.Indices = indices;
        baked.StandingHeight = height;

        GD.Print($"  hip at {hipY:F2} m, shoulder at {shoulderY:F2} m; "
               + $"{legs} leg vertices, {arms} arm vertices, "
               + $"{vertices.Length - legs - arms} torso");

        // What each bone claimed, largest first. This is the readout that says
        // whether the name matching worked, and it is printed every bake rather
        // than on failure: a rig whose bones are called something unexpected
        // classifies *most* vertices correctly and a few wrongly, which is a body
        // with one arm that swings from its ankle and no error anywhere.
        var ranked = new System.Collections.Generic.List<(string Bone, int Count)>();
        foreach ((string bone, int count) in perBone)
            ranked.Add((bone, count));

        ranked.Sort((a, b) => b.Count.CompareTo(a.Count));

        var summary = new System.Collections.Generic.List<string>();
        for (int i = 0; i < Mathf.Min(8, ranked.Count); i++)
        {
            (Limb limb, float _) = Classify(ranked[i].Bone);
            summary.Add($"{ranked[i].Bone}={ranked[i].Count}:{limb}");
        }

        GD.Print($"  bones: {string.Join("  ", summary)}");

        // How much of the declared height is actually creature.
        //
        // The scale is set by the topmost vertex, which is the honest reading of
        // "make this two metres tall" and is wrong the moment something floats
        // above the head. The first model baked carries a halo ring: it measured
        // 2.00 m and the body inside it was about 1.2, so a variant asked for at
        // the bloater's 2.4 m would have arrived the size of a walker.
        //
        // Not an error, because a tall crest or a raised tail is a legitimate
        // silhouette and the operator may want exactly that. It is a warning
        // because the alternative is finding out from a screenshot, and every
        // judgement made about an asset without one has been wrong so far.
        var bodyHeights = new System.Collections.Generic.List<float>();
        for (int i = 0; i < placed.Length; i++)
            bodyHeights.Add(placed[i].Y);

        bodyHeights.Sort();
        float bulk = bodyHeights[Mathf.RoundToInt((bodyHeights.Count - 1) * 0.98f)];

        GD.Print($"  98% of vertices are below {bulk:F2} m of {height:F2} m");

        if (bulk < height * 0.85f)
        {
            GD.PushWarning($"  something above the body is setting the scale: 98% of the mesh "
                         + $"is under {bulk:F2} m of a declared {height:F2} m. The creature will "
                         + $"read as roughly {bulk:F2} m tall. Ask for "
                         + $"{height * height / Mathf.Max(0.01f, bulk):F1} m instead, or bake a "
                         + "model without the ornament.");
        }

        // The two things a usable rig cannot get wrong. A hip above a shoulder,
        // or a hip in the upper half of the body, is a bake that will render
        // perfectly and animate like a folding chair.
        if (hipY >= shoulderY)
        {
            GD.PushError($"  the hip ({hipY:F2} m) is not below the shoulder ({shoulderY:F2} m) \u2014 "
                       + "the limb classification is wrong");
            return false;
        }

        if (hipY > height * 0.65f)
        {
            GD.PushError($"  the hip is at {hipY:F2} m of a {height:F2} m body, which is not a hip. "
                       + "Leg vertices are reaching too far up: check the bone readout above");
            return false;
        }

        return true;
    }

    /// Which limb a bone belongs to, and the phase that goes with its side.
    ///
    /// Matched on substrings rather than on an exact table, because bone naming is
    /// whatever the person who rigged it chose. The names checked here are the
    /// ones the humanoid convention uses and the ones the generator has produced
    /// so far; a rig that calls a thigh something else fails loudly in `Convert`
    /// rather than quietly baking a body with no legs.
    private static (Limb Limb, float Phase) Classify(string bone)
    {
        string name = bone.ToLowerInvariant();

        bool right = name.EndsWith(".r", System.StringComparison.Ordinal)
                  || name.EndsWith("_r", System.StringComparison.Ordinal)
                  || name.Contains("right", System.StringComparison.Ordinal);

        if (name.Contains("thigh", System.StringComparison.Ordinal)
            || name.Contains("shin", System.StringComparison.Ordinal)
            || name.Contains("calf", System.StringComparison.Ordinal)
            || name.Contains("leg", System.StringComparison.Ordinal)
            || name.Contains("foot", System.StringComparison.Ordinal)
            || name.Contains("toe", System.StringComparison.Ordinal))
        {
            return (Limb.Leg, right ? 0.5f : 0.0f);
        }

        if (name.Contains("arm", System.StringComparison.Ordinal)
            || name.Contains("hand", System.StringComparison.Ordinal)
            || name.Contains("shoulder", System.StringComparison.Ordinal)
            || name.Contains("clavicle", System.StringComparison.Ordinal))
        {
            // Counter-phased against the leg on the same side, which is what stops
            // a walk reading as a march.
            return (Limb.Arm, right ? 0.0f : 0.5f);
        }

        return (Limb.Torso, 0.0f);
    }

    /// The bone name for each joint slot a vertex can reference.
    ///
    /// Through the skin's bind list when there is one, because that is what the
    /// vertex indices actually address. `GetBindBoneIndex` returns -1 when the
    /// skin binds by name instead, which is why both are tried.
    private static string[] JointNames(MeshInstance3D instance, Skeleton3D skeleton)
    {
        Skin? skin = instance.Skin;

        if (skin == null)
        {
            var direct = new string[skeleton.GetBoneCount()];
            for (int i = 0; i < direct.Length; i++)
                direct[i] = skeleton.GetBoneName(i);

            return direct;
        }

        var names = new string[skin.GetBindCount()];

        for (int i = 0; i < names.Length; i++)
        {
            int bone = skin.GetBindBone(i);
            if (bone >= 0 && bone < skeleton.GetBoneCount())
            {
                names[i] = skeleton.GetBoneName(bone);
                continue;
            }

            string bound = skin.GetBindName(i);
            names[i] = string.IsNullOrEmpty(bound) ? string.Empty : bound;
        }

        return names;
    }

    /// Where a kind of limb stops, in placed space.
    ///
    /// The top of the limb, not its average: a leg pivots at the hip, which is
    /// where the leg geometry ends. Averaging the thigh, the shin and the foot
    /// would put the pivot at the knee and make every enemy walk like a puppet.
    ///
    /// **A percentile rather than the maximum, and one stray vertex is why.** Four
    /// influences per vertex means a hair or halo vertex can come out dominated by
    /// a thigh bone with a weight of 0.3, and the maximum is then that vertex. On
    /// the first model baked it put the hip at 1.92 m of a 2 m body — above the
    /// shoulder — from a handful of vertices out of four and a half thousand. The
    /// ninety-fifth percentile ignores them and lands on the same answer for a
    /// clean rig, because in a clean rig the top five per cent of leg vertices are
    /// all at the hip anyway.
    private const float PivotPercentile = 0.95f;

    private static float TopOf(Vector3[] vertices, Limb[] limbs, Limb limb,
                               float low, float scale, float fallback)
    {
        var heights = new System.Collections.Generic.List<float>();

        for (int i = 0; i < vertices.Length; i++)
        {
            if (limbs[i] == limb)
                heights.Add(vertices[i].Y);
        }

        if (heights.Count == 0)
            return fallback;

        heights.Sort();

        int at = Mathf.Clamp(Mathf.RoundToInt((heights.Count - 1) * PivotPercentile),
                             0, heights.Count - 1);

        return (heights[at] - low) * scale;
    }

    private static int Dominant(int[] bones, float[] weights, int vertex, int perVertex)
    {
        int best = -1;
        float bestWeight = -1.0f;

        for (int i = 0; i < perVertex; i++)
        {
            int at = vertex * perVertex + i;
            if (at >= weights.Length || weights[at] <= bestWeight)
                continue;

            bestWeight = weights[at];
            best = bones[at];
        }

        return best;
    }

    /// Flat normals, for a model that arrived without any.
    ///
    /// A mesh with no normals renders perfectly and is simply never darkened
    /// (godot.md:45), which reads as a lighting setting rather than as missing
    /// data — so this fills them in rather than letting the bake succeed with the
    /// array empty.
    private static Vector3[] RecomputeNormals(Vector3[] vertices, int[] indices)
    {
        var normals = new Vector3[vertices.Length];

        if (indices.Length > 0)
        {
            for (int i = 0; i + 2 < indices.Length; i += 3)
            {
                Vector3 face = Face(vertices[indices[i]], vertices[indices[i + 1]], vertices[indices[i + 2]]);
                normals[indices[i]] += face;
                normals[indices[i + 1]] += face;
                normals[indices[i + 2]] += face;
            }
        }
        else
        {
            for (int i = 0; i + 2 < vertices.Length; i += 3)
            {
                Vector3 face = Face(vertices[i], vertices[i + 1], vertices[i + 2]);
                normals[i] = face;
                normals[i + 1] = face;
                normals[i + 2] = face;
            }
        }

        for (int i = 0; i < normals.Length; i++)
        {
            normals[i] = normals[i].LengthSquared() > 0.0f
                ? normals[i].Normalized()
                : Vector3.Up;
        }

        return normals;
    }

    private static Vector3 Face(Vector3 a, Vector3 b, Vector3 c) => (a - c).Cross(a - b);

    private static string FirstNames(Skeleton3D skeleton)
    {
        var names = new System.Collections.Generic.List<string>();
        for (int i = 0; i < Mathf.Min(4, skeleton.GetBoneCount()); i++)
            names.Add(skeleton.GetBoneName(i));

        return string.Join(", ", names);
    }

    /// A node's transform relative to the instantiated root.
    ///
    /// Composed by walking up rather than read from `GlobalTransform`, which on a
    /// node outside the tree prints an error and returns identity — which would
    /// silently give exactly the un-transformed vertices this exists to avoid.
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

    private static MeshInstance3D? FindMesh(Node node)
    {
        if (node is MeshInstance3D { Mesh: not null } instance)
            return instance;

        foreach (Node child in node.GetChildren())
        {
            MeshInstance3D? found = FindMesh(child);
            if (found != null)
                return found;
        }

        return null;
    }

    private static Skeleton3D? FindSkeleton(Node node)
    {
        if (node is Skeleton3D skeleton)
            return skeleton;

        foreach (Node child in node.GetChildren())
        {
            Skeleton3D? found = FindSkeleton(child);
            if (found != null)
                return found;
        }

        return null;
    }
}
