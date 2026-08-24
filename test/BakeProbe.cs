using Godot;

/// Checks the baked-body pipeline: the resource, the rebuild, and the rig.
///
///   godot --headless --script test/BakeProbe.cs
///
/// `BakeBody` converts an authored, skinned `.glb` into the two UV channels
/// `body.gdshader` animates from, so the horde can draw geometry somebody
/// authored without a `MultiMesh` losing it on pack/save. Everything it can get
/// wrong produces a body that renders perfectly:
///
///   - reading bone names through the skeleton's index instead of the skin's
///     bind list scrambles the classification, and legs swing from the neck
///   - taking the pivot as the maximum leg height lets one stray-weighted hair
///     vertex put the hip above the shoulder
///   - reading the mesh arrays without the node's transform bakes the model in
///     whatever space the exporter used, which for a Y-up conversion means the
///     body is lying down
///
/// All three happened on the first model put through it, in that order, and the
/// only reason any of them were caught is that the baker checks its own answer.
/// This makes that check permanent and adds the ones a bake alone cannot make.
public partial class BakeProbe : SceneTree
{
    private bool _failed;

    public override void _Initialize()
    {
        Stage("an unsound resource is refused rather than half-drawn", RefusesUnsound);
        Stage("a sound resource rebuilds into a mesh", RebuildsSound);
        Stage("every baked body on disk is sound", BakesOnDiskAreSound);

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
        catch (System.Exception error)
        {
            GD.PushError($"  {error.Message}");
            ok = false;
        }

        GD.Print($"{label}: {(ok ? "ok" : "FAILED")}");
        _failed |= !ok;
    }

    /// Arrays of different lengths are the shape a half-finished bake takes.
    ///
    /// `AddSurfaceFromArrays` accepts them. It does not throw, it does not warn,
    /// and it renders something wrong — so the check has to happen before, which
    /// is what `BakedBodyResource.Sound` is for.
    private bool RefusesUnsound()
    {
        var ragged = new BakedBodyResource
        {
            Source = "synthetic",
            Vertices = new[] { Vector3.Zero, Vector3.Up, Vector3.Right },
            Normals = new[] { Vector3.Up },
            Colours = new[] { Colors.White, Colors.White, Colors.White },
            Rig = new[] { Vector2.Zero, Vector2.Zero, Vector2.Zero },
            Rig2 = new[] { Vector2.Zero, Vector2.Zero, Vector2.Zero },
        };

        var empty = new BakedBodyResource { Source = "synthetic" };

        bool raggedRefused = !ragged.Sound && BakedBody.Build(ragged) == null;
        bool emptyRefused = !empty.Sound && BakedBody.Build(empty) == null;

        GD.Print($"  ragged arrays refused {raggedRefused}, empty refused {emptyRefused}");
        return raggedRefused && emptyRefused;
    }

    /// And a sound one has to actually build, or the stage above passes by
    /// refusing everything.
    private bool RebuildsSound()
    {
        BakedBodyResource triangle = Triangle();
        ArrayMesh? mesh = BakedBody.Build(triangle);

        if (mesh == null || mesh.GetSurfaceCount() != 1)
        {
            GD.PushError("  a sound resource did not rebuild");
            return false;
        }

        Godot.Collections.Array arrays = mesh.SurfaceGetArrays(0);
        var vertices = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        var uv = arrays[(int)Mesh.ArrayType.TexUV].AsVector2Array();
        var uv2 = arrays[(int)Mesh.ArrayType.TexUV2].AsVector2Array();

        // The rig has to survive the round trip. It travels in the UV channels,
        // which is the one part of this that looks like a mistake and is not —
        // nothing here is a texture coordinate and no texture is ever sampled.
        bool kept = vertices.Length == 3
                 && uv.Length == 3
                 && uv2.Length == 3
                 && Mathf.Abs(uv[1].X - 0.55f) < 0.001f
                 && Mathf.Abs(uv2[1].X - 0.5f) < 0.001f;

        GD.Print($"  rebuilt {vertices.Length} vertices, rig survived {kept}");
        return kept;
    }

    private static BakedBodyResource Triangle() => new()
    {
        Source = "synthetic",
        StandingHeight = 1.0f,
        Vertices = new[] { Vector3.Zero, Vector3.Up, Vector3.Right },
        Normals = new[] { Vector3.Back, Vector3.Back, Vector3.Back },
        Colours = new[] { Colors.White, Colors.White, Colors.White },
        Rig = new[] { Vector2.Zero, new Vector2(0.55f, 0.9f), Vector2.Zero },
        Rig2 = new[] { Vector2.Zero, new Vector2(0.5f, 0.035f), Vector2.Zero },
    };

    /// Whatever has actually been baked, checked against what a body has to be.
    ///
    /// Skipped rather than failed when the directory is empty: the pipeline is
    /// real before any content has gone through it, and a probe that demanded
    /// content would fail on a clean checkout. It says so, loudly, so an empty
    /// pass is never mistaken for a full one.
    private bool BakesOnDiskAreSound()
    {
        string[] paths = Bakes();

        if (paths.Length == 0)
        {
            GD.Print("  nothing in res://resources/bodies/ yet — nothing checked");
            return true;
        }

        bool ok = true;

        foreach (string path in paths)
        {
            var baked = GD.Load<BakedBodyResource>(path);
            if (baked == null)
            {
                GD.PushError($"  {path} did not load as a BakedBodyResource");
                ok = false;
                continue;
            }

            ok &= Check(path, baked);
        }

        return ok;
    }

    private static bool Check(string path, BakedBodyResource baked)
    {
        if (!baked.Sound)
        {
            GD.PushError($"  {path}: arrays are empty or disagree in length");
            return false;
        }

        bool ok = true;

        float low = float.MaxValue;
        float high = float.MinValue;
        foreach (Vector3 vertex in baked.Vertices)
        {
            low = Mathf.Min(low, vertex.Y);
            high = Mathf.Max(high, vertex.Y);
        }

        // Feet at the origin. Everything that draws a body plants it by setting
        // its Y, so a body whose lowest vertex is not zero is drawn buried or
        // floating and there is nothing in the scene to say so.
        if (Mathf.Abs(low) > 0.01f)
        {
            GD.PushError($"  {path}: lowest vertex at {low:F3}, not 0");
            ok = false;
        }

        if (Mathf.Abs((high - low) - baked.StandingHeight) > 0.02f)
        {
            GD.PushError($"  {path}: measures {high - low:F2} m against a declared "
                       + $"{baked.StandingHeight:F2} m");
            ok = false;
        }

        // The rig, which is the whole reason the bake exists. A body whose swing
        // is zero everywhere renders perfectly and stands rigid while it slides
        // across the floor.
        float hip = float.MaxValue, shoulder = float.MaxValue;
        int moving = 0;
        bool early = false, late = false;

        for (int i = 0; i < baked.Rig.Length; i++)
        {
            if (baked.Rig[i].X <= 0.0f)
                continue;

            moving++;

            // Legs pivot low and arms high; the two pivots in use are the hip and
            // the shoulder, so the lower of them is the hip.
            hip = Mathf.Min(hip, baked.Rig[i].Y);
            shoulder = Mathf.Max(shoulder == float.MaxValue ? 0.0f : shoulder, baked.Rig[i].Y);

            if (baked.Rig2[i].X < 0.25f)
                early = true;
            else
                late = true;
        }

        if (moving == 0)
        {
            GD.PushError($"  {path}: no vertex has any swing — the body will never move");
            ok = false;
        }

        if (!early || !late)
        {
            GD.PushError($"  {path}: every moving vertex is on the same phase — "
                       + "the body marches rather than walks");
            ok = false;
        }

        if (moving > 0 && hip >= shoulder)
        {
            GD.PushError($"  {path}: the lowest pivot ({hip:F2}) is not below the highest "
                       + $"({shoulder:F2}) — the limb classification is wrong");
            ok = false;
        }

        if (moving > 0 && hip > baked.StandingHeight * 0.65f)
        {
            GD.PushError($"  {path}: the lowest pivot is at {hip:F2} m of a "
                       + $"{baked.StandingHeight:F2} m body, which is not a hip");
            ok = false;
        }

        ArrayMesh? mesh = BakedBody.Build(baked);
        if (mesh == null)
        {
            GD.PushError($"  {path}: sound, and did not rebuild");
            ok = false;
        }

        GD.Print($"  {path}: {baked.Triangles} tris, {baked.StandingHeight:F2} m, "
               + $"{moving} moving vertices, pivots {hip:F2}–{shoulder:F2} m");

        return ok;
    }

    /// Every baked body on disk.
    ///
    /// `.res.remap` is what an exported build leaves in place of the source, and
    /// trimming it is not optional — a probe that misses the suffix finds nothing
    /// in an export and reports that the game has no bodies.
    private static string[] Bakes()
    {
        var found = new System.Collections.Generic.List<string>();

        using DirAccess dir = DirAccess.Open("res://resources/bodies");
        if (dir == null)
            return found.ToArray();

        foreach (string name in dir.GetFiles())
        {
            string file = name.EndsWith(".remap", System.StringComparison.Ordinal)
                ? name[..^6]
                : name;

            if ((file.EndsWith(".res", System.StringComparison.Ordinal)
                 || file.EndsWith(".tres", System.StringComparison.Ordinal))
                && !found.Contains(file))
            {
                found.Add(file);
            }
        }

        found.Sort(System.StringComparer.Ordinal);

        var paths = new string[found.Count];
        for (int i = 0; i < found.Count; i++)
            paths[i] = $"res://resources/bodies/{found[i]}";

        return paths;
    }
}
