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
        Stage("a baked colour matches a procedural one", ColourMatchesProcedural);
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

        // The rig has to survive the round trip. It travels in UV2 alone —
        // `(swing, floor(pivotY * 100) + phase)` — and UV is the body atlas
        // coordinate, which for a bake is the flat cell. Both channels are
        // checked because losing either is silent: a bake with no UV samples
        // texel zero of layer zero and draws in flat colour, and a bake with no
        // UV2 stands rigid while it walks.
        bool kept = vertices.Length == 3
                 && uv.Length == 3
                 && uv2.Length == 3
                 && Mathf.Abs(uv2[1].X - 0.55f) < 0.001f
                 && Mathf.Abs(uv2[1].Y - 90.5f) < 0.001f;

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
        // UV is the atlas and UV2 is the packed rig. A swing of 0.55 about a
        // pivot at 0.9 m with a half-turn of phase is `(0.55, 90.5)` — see
        // `MeshBuilder`, which packs `floor(pivotY * 100) + phase`.
        Rig = new[] { Vector2.Zero, Vector2.Zero, Vector2.Zero },
        Rig2 = new[] { Vector2.Zero, new Vector2(0.55f, 90.5f), Vector2.Zero },
    };

    /// The same hex has to come out the same colour down both paths.
    ///
    /// **The bug this exists for passed every other stage in this file.** A bake
    /// can be watertight, correctly scaled, correctly classified, rigged on both
    /// phases — and simply the wrong colour, because `body.gdshader` writes COLOR
    /// into ALBEDO and ALBEDO is linear while a hex code is sRGB. The baker did
    /// not convert; `MeshBuilder` did. A stalker written as a dark brown rendered
    /// a washed near-white, and nothing anywhere said so.
    ///
    /// Asserting that `BakeBody.Tint` calls `SrgbToLinear` would be a tautology —
    /// it would restate the implementation and pass whatever the implementation
    /// did. What has content is that the two paths *agree*: a procedural body and
    /// a baked body given the same colour are the same colour on screen. Those
    /// are independent pieces of code, and they drifted apart once already.
    private bool ColourMatchesProcedural()
    {
        bool ok = true;

        // Spread across the curve on purpose. sRGB and linear meet at both ends,
        // so a test that only tried black or white would pass with the conversion
        // deleted; the gap is widest in the middle, which is where a palette
        // lives.
        string[] samples = { "6b5f52", "1a1a1a", "d8d4c8", "3f6b2a", "ffffff", "000000" };

        // One step of an 8-bit channel, and a bit.
        //
        // `AddSurfaceFromArrays` quantises vertex colours to RGBA8, so a colour
        // read back off a mesh is never the float that went in — every sample
        // here lands within 1/255 of the baked value and *none* of them are
        // equal. A tolerance tighter than a quantisation step would fail on a
        // pipeline that is working perfectly, which is the kind of probe that
        // gets deleted rather than fixed.
        //
        // It is still far tighter than the bug: skipping the conversion moves a
        // mid grey by 0.28, seventy steps.
        const float Tolerance = 1.5f / 255.0f;

        float worst = 0.0f;

        foreach (string hex in samples)
        {
            Color asked = Color.FromHtml(hex);

            // Through the procedural path: what a body built by `MeshBuilder`
            // actually ends up storing per vertex, read back off the mesh rather
            // than recomputed. Recomputing it here would be the tautology again.
            var builder = new MeshBuilder();
            builder.Box(Vector3.Zero, Vector3.One, asked);
            ArrayMesh procedural = builder.Build();

            Color drawn = procedural.SurfaceGetArrays(0)[(int)Mesh.ArrayType.Color]
                                    .AsColorArray()[0];

            Color baked = BakeBody.Tint(hex);

            float gap = Mathf.Max(Mathf.Max(Mathf.Abs(drawn.R - baked.R),
                                            Mathf.Abs(drawn.G - baked.G)),
                                  Mathf.Abs(drawn.B - baked.B));

            worst = Mathf.Max(worst, gap);

            if (gap > Tolerance)
            {
                GD.PushError($"  {hex}: procedural {drawn.ToHtml(false)} against baked "
                           + $"{baked.ToHtml(false)} — the two paths disagree by {gap:F3}");
                ok = false;
            }
        }

        // And the conversion has to be doing something, or a pair of paths that
        // both skipped it would agree perfectly and pass.
        Color mid = BakeBody.Tint("808080");
        bool converted = mid.R < 0.30f;

        if (!converted)
        {
            GD.PushError($"  a mid grey baked to {mid.R:F3}, which is sRGB — "
                       + "both paths skipped the conversion together");
            ok = false;
        }

        GD.Print($"  {samples.Length} colours, worst disagreement {worst:F4} against a "
               + $"{Tolerance:F4} tolerance; mid grey baked to {mid.R:F3}");
        return ok;
    }

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

        for (int i = 0; i < baked.Rig2.Length; i++)
        {
            if (baked.Rig2[i].X <= 0.0f)
                continue;

            moving++;

            // Legs pivot low and arms high; the two pivots in use are the hip and
            // the shoulder, so the lower of them is the hip.
            // Unpacked the way the shader unpacks it: the rig lives in UV2
            // alone now, as `floor(pivotY * 100) + phase`.
            float pivot = Mathf.Floor(baked.Rig2[i].Y) * 0.01f;
            float phase = baked.Rig2[i].Y - Mathf.Floor(baked.Rig2[i].Y);

            hip = Mathf.Min(hip, pivot);
            shoulder = Mathf.Max(shoulder == float.MaxValue ? 0.0f : shoulder, pivot);

            if (phase < 0.25f)
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

        // Something that moves reaches the floor.
        //
        // This replaced "the lowest pivot is in the lower half of the body",
        // which is true of a biped and false of every quadruped ever born — a
        // four-legged creature's hips sit at the top of it, just under the spine.
        // That rule refused a stalker whose classification was exactly right, 312
        // leg vertices against 312 arm vertices with every bone name matched, and
        // it would have refused every animal added after it.
        //
        // What holds for both is that legs end at the ground. Arms mistaken for
        // legs are the failure worth catching, and arms do not touch the floor.
        float lowestMoving = float.MaxValue;
        for (int i = 0; i < baked.Rig2.Length; i++)
        {
            if (baked.Rig2[i].X > 0.0f)
                lowestMoving = Mathf.Min(lowestMoving, baked.Vertices[i].Y);
        }

        if (moving > 0 && lowestMoving > baked.StandingHeight * 0.15f)
        {
            GD.PushError($"  {path}: the lowest moving vertex is at {lowestMoving:F2} m of a "
                       + $"{baked.StandingHeight:F2} m body — nothing that swings reaches the "
                       + "ground, so these are not legs");
            ok = false;
        }

        // Posture is reported, not ruled on. A hip above two thirds of the height
        // is what a quadruped looks like; it is also what a scrambled
        // classification looks like on something meant to stand upright, and only
        // a person can tell those apart.
        string posture = hip > baked.StandingHeight * 0.65f ? "quadruped" : "upright";

        ArrayMesh? mesh = BakedBody.Build(baked);
        if (mesh == null)
        {
            GD.PushError($"  {path}: sound, and did not rebuild");
            ok = false;
        }

        GD.Print($"  {path}: {posture}, {baked.Triangles} tris, {baked.StandingHeight:F2} m, "
               + $"{moving} moving vertices, pivots {hip:F2}–{shoulder:F2} m");

        // The colour the body actually reads as, and how far that is from the
        // nearest thing in the horde.
        //
        // **Reported and not ruled on, and the reason is that the rule does not
        // transfer.** `PaletteProbe` stage 3 requires 0.35 of chroma between the
        // player and every horde torso, which is the right rule for three
        // authored constants and the wrong one for forty thousand sampled texels
        // of skin, black cloth and an ivory panel: the mean of any authored
        // character is near-grey, so a threshold here would reject every
        // textured body there will ever be. RIN reads 0.217 from the bulwark's
        // grey and is nonetheless the easiest figure to find on screen, on
        // silhouette and on value.
        //
        // So this is a number that appears in every sweep rather than a gate. A
        // survivor swap that halves the separation is then visible in the diff
        // of a probe's output, which is the only place it *can* be visible —
        // nothing else measures the drawn body at all, and `CHARACTERS.md`
        // carries the table this line is compared against.
        //
        // At 25 pixels a body is its own average colour, which is why the mean is
        // the right aggregate and not, say, the most saturated part of it.
        if (baked.Colours.Length > 0)
        {
            float r = 0.0f, g = 0.0f, b = 0.0f;
            foreach (Color one in baked.Colours)
            {
                r += one.R;
                g += one.G;
                b += one.B;
            }

            var mean = new Color(r / baked.Colours.Length,
                                 g / baked.Colours.Length,
                                 b / baked.Colours.Length);

            Vector2 chroma = Palette.Chroma(mean);
            float nearest = float.MaxValue;
            foreach (Color torso in Palette.HordeTorsos())
                nearest = Mathf.Min(nearest, chroma.DistanceTo(Palette.Chroma(torso)));

            // **The mean was the only number here and asking an artist to move it
            // was a badly specified request.** A body's mean is an average over
            // skin, black cloth and whatever colour it wears, and human skin is a
            // large desaturated area: five characters were given deliberately
            // expanded identity colour — a red jacket, a blue coat, a green
            // medical shell — and four of the five means moved *closer* to the
            // horde, because a saturated red averaged with skin is a desaturated
            // warm grey. The mean cannot reach 0.35 while a face is in frame, and
            // `CHARACTERS.md` had already said as much before the ask went out.
            //
            // So the second number is the one to read: **what share of the body
            // is genuinely far from every horde colour**. That is what an
            // identity colour is for — not to shift an average, but to put a
            // patch on screen that belongs to nothing else out there. It is
            // per-vertex rather than per-area, which slightly over-weights dense
            // geometry like a face, and that is the conservative direction.
            int distinct = 0;
            foreach (Color one in baked.Colours)
            {
                Vector2 here = Palette.Chroma(one);
                float far = float.MaxValue;

                foreach (Color torso in Palette.HordeTorsos())
                    far = Mathf.Min(far, here.DistanceTo(Palette.Chroma(torso)));

                if (far >= Palette.PlayerChromaSeparation)
                    distinct++;
            }

            float share = (float)distinct / baked.Colours.Length;

            GD.Print($"    reads as {mean.LinearToSrgb().ToHtml(false)}, "
                   + $"luminance {0.2126f * mean.R + 0.7152f * mean.G + 0.0722f * mean.B:F3}, "
                   + $"mean {nearest:F3} from the nearest horde torso, "
                   + $"{share * 100.0f:F1}% of it past {Palette.PlayerChromaSeparation:F2}");
        }

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
