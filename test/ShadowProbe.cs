using Godot;

/// Checks the blob shadows, and with them the billboard path they belong to.
///
///   godot --headless --script test/ShadowProbe.cs
///
/// **This is the only thing that runs the fallback.** `SolidBodies` is on
/// everywhere else, so the sprite renderer, its shader, its texture array and now
/// these shadows are code that ships and is never executed — the state a fallback
/// is in when it is finally needed and turns out not to work. It is switched off
/// here before the scene enters the tree, because `Horde` reads it in `_Ready`.
///
/// The shadows themselves are worth three assertions and no more: they exist,
/// they are on the ground, and there is exactly one ground contact per enemy on
/// whichever path the horde took. That last one is the phase's actual risk. Every
/// enemy in the game was drawn twice for months because a renderer decided its
/// own visibility once a frame and overwrote the switch that was meant to turn it
/// off, and this adds a second renderer with the same shape.
public partial class ShadowProbe : SceneTree
{
    private Horde? _horde;
    private int _stage;
    private int _stageTick;
    private bool _failed;

    public override void _Initialize()
    {
        var scene = GD.Load<PackedScene>("res://scenes/Main.tscn")?.Instantiate();
        if (scene == null)
        {
            GD.PushError("Missing res://scenes/Main.tscn");
            Quit(1);
            return;
        }

        var level = scene.GetNodeOrNull<LevelGenerator>("Level");
        if (level != null)
            level.Seed = 0x51E5D0A7UL;

        // Before the tree, because `Horde._Ready` branches on it and builds a
        // different set of renderers either way. Assigning it afterwards changes
        // a flag that nothing reads again.
        var horde = scene.GetNodeOrNull<Horde>("Horde");
        if (horde != null)
            horde.SolidBodies = false;

        // Not the developer's save file. See `Fresh`.
        Fresh.Profile(scene);

        GetRoot().AddChild(scene);
    }

    public override bool _PhysicsProcess(double delta)
    {
        if (_stageTick == 0 && _stage == 0)
        {
            Node scene = GetRoot().GetChild(GetRoot().GetChildCount() - 1);
            _horde = scene.GetNodeOrNull<Horde>("Horde");

            if (_horde == null)
            {
                GD.PushError("PROBE FAILED — no Horde");
                Quit(1);
                return true;
            }

            scene.GetNodeOrNull<RunDirector>("RunDirector")?.SetPhysicsProcess(false);
        }

        _stageTick++;

        switch (_stage)
        {
            case 0: return RunStage(StageFallbackIsWhatRan, "the horde really is on the billboard path");
            case 1: return RunStage(StageShadowsDraw, "one blob per enemy inside the fog, none past it");
            case 2: return RunStage(StageShadowsSitOnTheGround, "every blob is on the terrain under its enemy");
            case 3: return RunStage(StageExactlyOneContact, "the sprites draw and the blobs draw; nothing draws twice");
            case 4: return RunStage(StageTheShaderCanSeeTheColour, "the blob shader forwards the per-instance colour");
            default:
                GD.Print(_failed ? "PROBE FAILED" : "PROBE OK");
                Quit(_failed ? 1 : 0);
                return true;
        }
    }

    private bool RunStage(System.Func<int, bool?> stage, string label)
    {
        bool? verdict = stage(_stageTick);
        if (verdict == null)
            return false;

        GD.Print($"{label}: {(verdict.Value ? "ok" : "FAILED")}");
        _failed |= !verdict.Value;
        _stage++;
        _stageTick = 0;
        return false;
    }

    /// The switch took.
    ///
    /// Without this the other three stages pass vacuously on a horde that quietly
    /// stayed on the solid bodies: no shadows drawn is consistent with "muted, as
    /// designed" from every angle except this one.
    private bool? StageFallbackIsWhatRan(int tick)
    {
        Horde horde = _horde!;

        GD.Print($"  SolidBodies {horde.SolidBodies}, "
               + $"body renderer {(horde.Bodies == null ? "absent" : "PRESENT")}, "
               + $"shadow renderer {(horde.Shadows == null ? "MISSING" : "present")}");

        bool inTree = horde.Shadows?.Node.IsInsideTree() ?? false;
        GD.Print($"  shadow node in tree: {inTree}, parent {horde.Shadows?.Node.GetParent()?.Name}");

        return !horde.SolidBodies && horde.Bodies == null && horde.Shadows != null && inTree;
    }

    private bool? StageShadowsDraw(int tick)
    {
        Horde horde = _horde!;

        if (tick == 1)
        {
            Spread(horde);
            return null;
        }

        if (tick < 20)
            return null;

        int near = 0;
        for (int i = 0; i < horde.Pool.Count; i++)
        {
            Vector3 at = horde.Pool.Position[i];
            if (new Vector2(at.X, at.Z).Length() <= ShadowRenderer.CullDistance)
                near++;
        }

        ShadowRenderer shadows = horde.Shadows!;
        GD.Print($"  {horde.Pool.Count} enemies, {near} within {ShadowRenderer.CullDistance} m, "
               + $"{shadows.Count} blobs drawn");

        // The crowd is laid out to straddle the cull distance on purpose, so a
        // renderer that ignored it would read as "one blob per enemy" and pass.
        if (near >= horde.Pool.Count)
        {
            GD.PushError("  nothing was placed past the cull distance — the range is untested");
            return false;
        }

        return shadows.Count == near && near > 0;
    }

    /// On the ground, under the enemy, and the right size.
    ///
    /// Read back out of the MultiMesh buffer rather than recomputed. The transform
    /// a blob is written with is a quad turned on its side and laid flat, written
    /// straight into a row-major buffer — the one place in this file where getting
    /// a sign wrong produces a shadow standing on its edge, which from directly
    /// above is a shadow that looks perfectly fine.
    private bool? StageShadowsSitOnTheGround(int tick)
    {
        Horde horde = _horde!;
        MultiMesh multi = horde.Shadows!.Node.Multimesh!;
        float[] buffer = multi.Buffer;

        if (multi.VisibleInstanceCount == 0)
        {
            GD.PushError("  nothing to read");
            return false;
        }

        int stride = buffer.Length / Mathf.Max(1, multi.InstanceCount);
        float worstHeight = 0.0f;
        float smallest = float.MaxValue;
        float largest = 0.0f;
        int upright = 0;

        for (int i = 0; i < multi.VisibleInstanceCount; i++)
        {
            int b = i * stride;

            float x = buffer[b + 3];
            float y = buffer[b + 7];
            float z = buffer[b + 11];

            worstHeight = Mathf.Max(worstHeight,
                Mathf.Abs(y - (Terrain.Height(x, z) + ShadowRenderer.GroundClearance)));

            // Column 2 of the basis is the quad's own normal. Laid flat it is
            // +Y; left alone it is +Z, and the blob is a black line.
            if (Mathf.Abs(buffer[b + 6]) > Mathf.Abs(buffer[b + 2])
                && Mathf.Abs(buffer[b + 6]) > Mathf.Abs(buffer[b + 10]))
            {
                upright++;
            }

            float diameter = buffer[b + 0];
            smallest = Mathf.Min(smallest, diameter);
            largest = Mathf.Max(largest, diameter);
        }

        GD.Print($"  {multi.VisibleInstanceCount} blobs, {upright} facing up, "
               + $"{smallest:F2}–{largest:F2} m across, worst height error {worstHeight:F4} m");

        return worstHeight < 0.001f
            && upright == multi.VisibleInstanceCount
            && smallest > 0.5f
            && largest < 6.0f;
    }

    /// One ground contact and one silhouette, and both of them on.
    ///
    /// The mirror of `BodyProbe`'s stage 7. There, the bodies are on and the
    /// billboards must be off; here the billboards are on and must be visible,
    /// and the blobs with them — a `Muted` flag wired to the wrong side of the
    /// branch would hide exactly the thing the fallback exists to draw, and every
    /// other stage in this file would still pass.
    private bool? StageExactlyOneContact(int tick)
    {
        Horde horde = _horde!;

        bool sprites = horde.Billboards.Node.Visible;
        bool blobs = horde.Shadows!.Node.Visible;

        GD.Print($"  billboards {(sprites ? "visible" : "HIDDEN")}, "
               + $"blobs {(blobs ? "visible" : "HIDDEN")}, bodies {(horde.Bodies == null ? "none" : "PRESENT")}");

        return sprites && blobs && horde.Bodies == null;
    }

    /// The shader reads the instance colour where it exists.
    ///
    /// **This stage exists because every other stage in this file passed while the
    /// blobs drew nothing at all.** A MultiMesh writes its per-instance colour into
    /// `COLOR` in the *vertex* stage; `COLOR` in the fragment stage is the
    /// interpolated vertex-colour attribute, and a `QuadMesh` has none. Reading it
    /// there gave an alpha of zero. Everything a probe can reach — the count, the
    /// transforms, the heights, the node's visibility — was correct, and the screen
    /// was empty. Turning the opacity to 1.0 and the size to 3x did not help,
    /// because the fault was not in any number.
    ///
    /// So this asks the only question left, of the source: is the colour forwarded
    /// through a varying, or read straight out of `fragment()`?
    ///
    /// Comments are stripped first. The paragraph above says `COLOR` in
    /// `fragment()` several times, and a check that matched its own explanation
    /// would be the third probe in this repository to pass by reading a comment.
    private bool? StageTheShaderCanSeeTheColour(int tick)
    {
        const string path = "res://assets/shaders/blob.gdshader";

        var shader = GD.Load<Shader>(path);
        if (shader == null)
        {
            GD.PushError($"  {path} did not load");
            return false;
        }

        string code = StripComments(shader.Code);

        int vertexAt = code.IndexOf("void vertex()", System.StringComparison.Ordinal);
        int fragmentAt = code.IndexOf("void fragment()", System.StringComparison.Ordinal);

        if (vertexAt < 0 || fragmentAt < 0 || fragmentAt < vertexAt)
        {
            GD.PushError("  the shader has no vertex stage before its fragment stage");
            return false;
        }

        string vertex = code[vertexAt..fragmentAt];
        string fragment = code[fragmentAt..];

        bool declaresVarying = code.Contains("varying", System.StringComparison.Ordinal);
        bool vertexReadsColour = vertex.Contains("COLOR", System.StringComparison.Ordinal);
        bool fragmentReadsColour = fragment.Contains("COLOR", System.StringComparison.Ordinal);

        GD.Print($"  varying declared {declaresVarying}, vertex reads COLOR {vertexReadsColour}, "
               + $"fragment reads COLOR {fragmentReadsColour}");

        if (fragmentReadsColour)
            GD.PushError("  fragment() reads COLOR directly — on a QuadMesh that is an alpha of zero");

        return declaresVarying && vertexReadsColour && !fragmentReadsColour;
    }

    /// Line and block comments out, so a text check cannot match its own prose.
    private static string StripComments(string code)
    {
        var kept = new System.Text.StringBuilder(code.Length);
        bool inLine = false;
        bool inBlock = false;

        for (int i = 0; i < code.Length; i++)
        {
            if (inLine)
            {
                if (code[i] == '\n')
                {
                    inLine = false;
                    kept.Append('\n');
                }

                continue;
            }

            if (inBlock)
            {
                if (code[i] == '*' && i + 1 < code.Length && code[i + 1] == '/')
                {
                    inBlock = false;
                    i++;
                }

                continue;
            }

            if (code[i] == '/' && i + 1 < code.Length && code[i + 1] == '/')
            {
                inLine = true;
                i++;
                continue;
            }

            if (code[i] == '/' && i + 1 < code.Length && code[i + 1] == '*')
            {
                inBlock = true;
                i++;
                continue;
            }

            kept.Append(code[i]);
        }

        return kept.ToString();
    }

    /// A crowd that straddles the cull distance, so the range is a claim the
    /// probe can check rather than one it takes on trust.
    private static void Spread(Horde horde)
    {
        horde.Pool.Clear();

        for (int i = 0; i < 24; i++)
        {
            float angle = Mathf.Tau * i / 24.0f;

            // Half comfortably inside the fog, half comfortably past it. Nothing
            // near the boundary: a body drifting across it between the spawn and
            // the reading would make this fail once every few runs for a reason
            // that has nothing to do with shadows.
            float radius = i % 2 == 0 ? 8.0f + i * 0.3f : ShadowRenderer.CullDistance + 6.0f;

            horde.Spawn(new Vector3(Mathf.Cos(angle) * radius, 0.0f, Mathf.Sin(angle) * radius),
                        i % horde.Types.Length);
        }
    }
}
