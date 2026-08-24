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
    /// A hand-typed colour, in the space the shader wants.
    ///
    /// This is the one function in the file that decides whether a bake *looks*
    /// right, as opposed to being structurally sound. `body.gdshader` writes
    /// COLOR straight into ALBEDO and ALBEDO is linear; a hex code is not — it
    /// is what a colour picker shows, which is sRGB. `MeshBuilder.Box` converts
    /// once at build time for the procedural bodies, and a baked body that
    /// skipped the same conversion arrived about twice as bright as asked for.
    /// The first stalker rendered a washed near-white from a colour written as
    /// dark brown, through a bake that passed every soundness check there is.
    ///
    /// The model's own colours are *not* converted, and the asymmetry is correct
    /// rather than an oversight: glTF stores COLOR_0 and baseColorFactor linear
    /// by specification, so they already are what the shader wants. Only the
    /// hand-typed argument is sRGB.
    ///
    /// Public because `BakeProbe` holds it against `MeshBuilder` — the invariant
    /// worth keeping is not "this calls SrgbToLinear", which is a tautology, but
    /// "a baked body and a procedural body given the same hex come out the same
    /// colour". Those are two independent code paths and they drifted once.
    public static Color Tint(string html) => Color.FromHtml(html).SrgbToLinear();

    public override void _Initialize()
    {
        string[] args = OS.GetCmdlineUserArgs();
        if (args.Length < 3 || !float.TryParse(args[2], out float height))
        {
            GD.PushError("usage: BakeBody.cs -- <source.glb> <out.res> <height metres> "
                       + "[swing] [armSwing] [bob] [rrggbb,rrggbb,...]");
            Quit(1);
            return;
        }

        // Defaults matched to the walker, which is the variant everything else is
        // read against. A bake that wants a different gait says so.
        float legSwing = args.Length > 3 && float.TryParse(args[3], out float a) ? a : 0.55f;
        float armSwing = args.Length > 4 && float.TryParse(args[4], out float b) ? b : 0.30f;
        float bob = args.Length > 5 && float.TryParse(args[5], out float c) ? c : 0.035f;

        // An explicit colour beats whatever the model came with. Models arrive
        // white far more often than not — a generator asked for "no textures"
        // gives a single default material — and a horde variant that is the
        // brightest thing on a dark map is a variant that reads as the player.
        // Write the colour without a leading `#`. PowerShell treats an unquoted
        // `#` as the start of a comment, so `-- ... #6b5f52` arrives as no
        // argument at all, and the first stalker baked white while the command
        // line said otherwise. `Color.FromHtml` is happy either way; only the
        // shell cares. Both forms are accepted here — the point of the note is
        // the shell, not the parser.
        Color[]? tints = null;
        if (args.Length > 6)
        {
            // Refused rather than ignored. A colour that cannot be read is a
            // typo, and a bake that quietly keeps the model's white is the one
            // failure mode that survives every check in this file: the mesh is
            // sound, the rig is sound, and the thing is simply the wrong colour
            // on screen.
            // One colour per surface, in surface order.
            //
            // **The model's own material colours are not trustworthy and three
            // models in a row have proved it.** The walker came out of the
            // generator with three materials, exactly as asked, and Godot
            // reported them as a2aa9e / b1ada6 / cec8bc — the *sRGB* of the
            // linear values requested, which is roughly six times too bright.
            // The stalker before it arrived white twice for two different
            // reasons.
            //
            // Every trip between a modelling tool, a glTF, an importer and this
            // baker is a chance for one gamma conversion too many or too few,
            // and the failure is silent: a body that is the wrong brightness
            // looks exactly like a body somebody chose that brightness for.
            //
            // So the palette lives here, beside the palette the procedural
            // bodies use, and the model supplies geometry. Fewer colours than
            // surfaces repeats the last one, which is the common case of "one
            // colour for the whole creature".
            string[] written = args[6].Split(',');
            var chosen = new Color[written.Length];

            for (int i = 0; i < written.Length; i++)
            {
                string one = written[i].Trim();

                // Refused rather than ignored. A typo that silently keeps the
                // model's own colour is the one failure mode that survives every
                // other check in this file.
                if (!Color.HtmlIsValid(one))
                {
                    GD.PushError($"\"{one}\" is not a colour. Write it as rrggbb, "
                               + "or rrggbb,rrggbb,rrggbb for one per surface.");
                    Quit(1);
                    return;
                }

                chosen[i] = Tint(one);
            }

            tints = chosen;
        }

        Quit(Bake(args[0], args[1], height, legSwing, armSwing, bob, tints) ? 0 : 1);
    }

    private static bool Bake(string source, string destination, float height,
                             float legSwing, float armSwing, float bob, Color[]? tints)
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

        // Refused rather than half-baked.
        //
        // `FindMesh` returns the first `MeshInstance3D` it walks into, and the
        // baker merges every *surface* of that one node. A model exported as
        // separate nodes — body here, coat there, horns as a third — bakes
        // whichever the walk reached first and silently omits the rest. What
        // comes out is a sound bake: watertight, correctly scaled, correctly
        // rigged, and missing a coat. That is the same shape of failure as the
        // colour that never arrived and the ninety-six triangles read off
        // surface zero, and both of those were found by looking at a render
        // rather than by anything erroring.
        //
        // Merging them is not hard — each node has its own transform and its
        // own skin bind list, so it is the surface merge with two more lookups
        // — but it is not needed by anything in the project yet, and a bake
        // that is quietly wrong is worse than one that will not run. Refuse,
        // name the nodes, and let whoever hits it decide.
        var meshes = new System.Collections.Generic.List<MeshInstance3D>();
        CollectMeshes(root, meshes);

        if (meshes.Count > 1)
        {
            var names = new System.Collections.Generic.List<string>();
            foreach (MeshInstance3D found in meshes)
                names.Add(found.Name);

            GD.PushError($"{source} has {meshes.Count} mesh nodes ({string.Join(", ", names)}) and "
                       + "the baker reads one. Merge them into a single mesh on export, or teach "
                       + "Convert to walk the list — baking one of them would look correct and be "
                       + "missing the others.");
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

        if (!Convert(instance, skeleton, baked, height, legSwing, armSwing, bob, toRoot, tints))
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
                                Transform3D toRoot, Color[]? tints)
    {
        // Every surface, merged.
        //
        // **This read surface 0 and stopped, which silently threw away a third of
        // the first model that had more than one.** A glTF splits by material, so
        // a creature with a body material and a claw material arrives as two
        // surfaces and looks complete in every viewer — the bake was 328 triangles
        // of a 424-triangle model and the missing 96 were the parts that had a
        // different colour, which is exactly the parts somebody cared about.
        //
        // Merging is right rather than refusing: a baked body is one vertex-
        // coloured surface by construction, and each source surface contributes
        // its own material's albedo to the vertices that came from it.
        int surfaces = instance.Mesh.GetSurfaceCount();

        var allVertices = new System.Collections.Generic.List<Vector3>();
        var allNormals = new System.Collections.Generic.List<Vector3>();
        var allColours = new System.Collections.Generic.List<Color>();
        var allIndices = new System.Collections.Generic.List<int>();
        var allLimbs = new System.Collections.Generic.List<Limb>();
        var allPhases = new System.Collections.Generic.List<float>();
        var perBone = new System.Collections.Generic.Dictionary<string, int>();

        string[] jointNames = JointNames(instance, skeleton);

        for (int surface = 0; surface < surfaces; surface++)
        {
            Godot.Collections.Array arrays = instance.Mesh.SurfaceGetArrays(surface);

            var v = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            var n = arrays[(int)Mesh.ArrayType.Normal].AsVector3Array();
            var c = arrays[(int)Mesh.ArrayType.Color].AsColorArray();
            var b = arrays[(int)Mesh.ArrayType.Bones].AsInt32Array();
            var w = arrays[(int)Mesh.ArrayType.Weights].AsFloat32Array();
            var idx = arrays[(int)Mesh.ArrayType.Index].AsInt32Array();

            if (v.Length == 0)
                continue;

            if (b.Length == 0 || w.Length == 0)
            {
                GD.PushError($"  surface {surface} has no joints or weights — it is not skinned, "
                           + "so there is no way to tell a leg from a chest");
                return false;
            }

            // Four influences per vertex is the glTF norm and what Godot hands
            // back. Read per surface rather than once, because nothing promises
            // two surfaces of one mesh were authored the same way.
            int influences = b.Length / v.Length;
            if (influences <= 0)
            {
                GD.PushError($"  surface {surface}: {b.Length} bone indices for {v.Length} vertices");
                return false;
            }

            // Reported, because losing it is silent and has happened three
            // times.
            //
            // A surface whose material cannot be found bakes white, and white is
            // exactly what an untinted model looks like anyway — so "the colour
            // was lost" and "the artist chose white" are the same picture. The
            // whole reason a model has three surfaces is that somebody wanted
            // three colours, and the bake should say out loud which three it
            // found.
            Color? found = SurfaceAlbedo(instance, surface);

            // The chosen palette wins over whatever the model shipped with. See
            // the note where `tints` is parsed — fewer colours than surfaces
            // repeats the last one.
            Color? forced = tints is { Length: > 0 }
                ? tints[Mathf.Min(surface, tints.Length - 1)]
                : null;

            Color surfaceAlbedo = forced ?? found ?? Colors.White;
            int offset = allVertices.Count;

            GD.Print($"    surface {surface}: "
                   + (found.HasValue ? $"model says {found.Value.ToHtml(false)}" : "no material")
                   + (forced.HasValue
                        ? $", forced to {forced.Value.LinearToSrgb().ToHtml(false)}"
                        : string.Empty));

            for (int i = 0; i < v.Length; i++)
            {
                allVertices.Add(v[i]);
                allNormals.Add(i < n.Length ? n[i] : Vector3.Up);
                allColours.Add(i < c.Length ? c[i] : surfaceAlbedo);

                int dominant = Dominant(b, w, i, influences);
                string bone = dominant >= 0 && dominant < jointNames.Length
                    ? jointNames[dominant]
                    : string.Empty;

                (Limb limb, float phase) = Classify(bone);
                allLimbs.Add(limb);
                allPhases.Add(phase);

                perBone.TryGetValue(bone, out int seen);
                perBone[bone] = seen + 1;
            }

            // Indices, and the case that has no index buffer.
            //
            // The merge writes one global index array, so the moment *any*
            // surface is indexed every surface has to be. A non-indexed one
            // contributed its vertices and nothing pointing at them: the
            // geometry was in the buffer, correctly placed and correctly
            // coloured, and no triangle referenced it. It simply was not there.
            //
            // glTF exporters mix the two freely — a body mesh indexed and a
            // strap or a horn left flat is an ordinary export — so this is not
            // a hypothetical. Sequential indices are what "non-indexed" means.
            if (idx.Length > 0)
            {
                foreach (int index in idx)
                    allIndices.Add(index + offset);
            }
            else
            {
                for (int i = 0; i < v.Length; i++)
                    allIndices.Add(offset + i);
            }
        }

        Vector3[] vertices = allVertices.ToArray();
        Vector3[] normals = allNormals.ToArray();
        Color[] colours = allColours.ToArray();
        int[] indices = allIndices.ToArray();
        Limb[] limbs = allLimbs.ToArray();
        float[] phases = allPhases.ToArray();

        if (vertices.Length == 0)
        {
            GD.PushError("  no surface had any vertices");
            return false;
        }

        GD.Print($"  {surfaces} surface(s) merged into {vertices.Length} vertices, "
               + $"{indices.Length / 3} triangles");
        GD.Print($"  colour: {(tints is { Length: > 0 } ? $"{tints.Length} forced" : "taken from the model")}");

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

        // What the body is coloured, in order of preference: an explicit
        // argument, the mesh's own vertex colours, then the material's albedo.
        //
        // The albedo matters because a model exported without textures still has
        // a material, and reading it is the difference between a creature the
        // artist chose the colour of and a white one. White is the last resort and
        // is deliberately loud about it — nothing else in this game is white, so a
        // white variant on screen says the colour was lost rather than chosen.
        Color fallback = Albedo(instance) ?? Colors.White;

        var rig = new Vector2[vertices.Length];
        var rig2 = new Vector2[vertices.Length];
        var placed = new Vector3[vertices.Length];
        var colour = new Color[vertices.Length];

        int legs = 0, arms = 0;

        for (int i = 0; i < vertices.Length; i++)
        {
            placed[i] = new Vector3(vertices[i].X * scale,
                                    (vertices[i].Y - low) * scale,
                                    vertices[i].Z * scale);

            // `colours` is what the surface loop already resolved — the forced
            // palette when one was given, the model's own albedo otherwise — so
            // there is nothing left to choose here.
            colour[i] = i < colours.Length ? colours[i] : fallback;

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
        baked.Colours = colour;
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

        // Legs reach the ground. This is the test that actually catches a
        // misclassification, and unlike the one it replaced it does not care what
        // posture the creature has.
        //
        // The first version asked whether the hip was in the lower half of the
        // body, which is true of a biped and false of every quadruped ever born:
        // a four-legged creature's hips are at the *top* of it, just under the
        // spine. It refused a perfectly good stalker whose classification was
        // exactly right — 312 leg vertices, 312 arm vertices, the bone names all
        // matched. A rule that only holds for one body plan is not a rule.
        //
        // What is true of both is that a leg ends at the floor. Arms mistaken for
        // legs are the failure being guarded against, and arms do not.
        float lowestLeg = float.MaxValue;
        for (int i = 0; i < placed.Length; i++)
        {
            if (limbs[i] == Limb.Leg)
                lowestLeg = Mathf.Min(lowestLeg, placed[i].Y);
        }

        if (lowestLeg > height * 0.15f)
        {
            GD.PushError($"  the lowest leg vertex is at {lowestLeg:F2} m of a {height:F2} m body \u2014 "
                       + "these are not legs. Check the bone readout above");
            return false;
        }

        // And a note on posture rather than a rule about it. A hip above two
        // thirds of the height is what a quadruped looks like and is worth seeing
        // stated, because if the model was meant to stand upright it is also what
        // a scrambled classification looks like.
        if (hipY > height * 0.65f)
        {
            GD.Print($"  posture: the hip sits at {hipY / height * 100.0f:F0}% of the height, "
                   + "which is a quadruped. If this was meant to stand upright, the "
                   + "classification is wrong.");
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

    /// The albedo of the mesh's material, if it has one that carries a colour.
    ///
    /// Checked on the instance override first and the surface second, because a
    /// glTF import puts it on the surface and anything hand-assembled tends to
    /// put it on the node.
    private static Color? Albedo(MeshInstance3D instance) => SurfaceAlbedo(instance, 0);

    /// The albedo of one surface's material.
    ///
    /// Per surface rather than per mesh, because a glTF splits by material and
    /// the whole reason a model has two surfaces is that somebody wanted two
    /// colours. Baking them both to the mesh's first albedo would merge the
    /// geometry correctly and throw away the distinction that caused the split.
    private static Color? SurfaceAlbedo(MeshInstance3D instance, int surface)
    {
        if (instance.GetSurfaceOverrideMaterial(surface) is BaseMaterial3D over)
            return over.AlbedoColor;

        if (instance.MaterialOverride is BaseMaterial3D node)
            return node.AlbedoColor;

        if (instance.Mesh != null && surface < instance.Mesh.GetSurfaceCount()
            && instance.Mesh.SurfaceGetMaterial(surface) is BaseMaterial3D material)
        {
            return material.AlbedoColor;
        }

        return null;
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

    /// Every mesh node in the model, so the check above can name them.
    private static void CollectMeshes(Node node, System.Collections.Generic.List<MeshInstance3D> into)
    {
        if (node is MeshInstance3D { Mesh: not null } mesh)
            into.Add(mesh);

        foreach (Node child in node.GetChildren())
            CollectMeshes(child, into);
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
