using Godot;

/// Checks the air.
///
///   godot --headless --script test/DustProbe.cs
///
/// The failure this exists for renders. `CUSTOM0` is a *mesh vertex* attribute
/// and `INSTANCE_CUSTOM` is the per-instance block; a MultiMesh fills the second
/// and every instance shares the first. Read the wrong one and all four hundred
/// motes get the same value, every position collapses to the same point, and the
/// field draws as a single speck — with no error, no warning, and a shader that
/// compiles cleanly.
///
/// A headless test cannot run the shader, so it checks both halves separately:
/// that the data going in is distinct per mote, and that the shader source asks
/// for the block that actually receives it.
public partial class DustProbe : SceneTree
{
    private AirDust? _dust;
    private Player? _player;

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

        // Not the developer's save file. See `Fresh`.
        Fresh.Profile(scene);

        GetRoot().AddChild(scene);
    }

    public override bool _PhysicsProcess(double delta)
    {
        if (_stage == 0 && _stageTick == 0)
        {
            Node scene = GetRoot().GetChild(GetRoot().GetChildCount() - 1);
            _dust = scene.GetNodeOrNull<AirDust>("AirDust");
            _player = scene.GetNodeOrNull<Player>("Player");

            if (_dust == null || _player == null)
            {
                GD.PushError($"PROBE FAILED — dust={_dust != null} player={_player != null}");
                Quit(1);
                return true;
            }

            scene.GetNodeOrNull<Horde>("Horde")?.SetPhysicsProcess(false);
            scene.GetNodeOrNull<RunDirector>("RunDirector")?.SetPhysicsProcess(false);
        }

        _stageTick++;

        switch (_stage)
        {
            case 0: return RunStage(StageShaderReadsTheRightBlock, "the shader reads INSTANCE_CUSTOM, not CUSTOM0");
            case 1: return RunStage(StageEveryMoteIsSomewhereElse, "every mote has a home of its own");
            case 2: return RunStage(StageBoundsCoverTheSlab, "the field is not culled the moment the origin leaves frame");
            case 3: return RunStage(StageFollowsPositionOnly, "the slab rides the player's position and ignores their facing");
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

    /// Read off the source, because the symptom is invisible to everything else.
    ///
    /// A text check, and it earns its place: this is a one-word difference that
    /// compiles, renders, and produces a field of one dot. There is no runtime
    /// state to inspect and no picture a headless run can take.
    private bool? StageShaderReadsTheRightBlock(int tick)
    {
        using FileAccess file = FileAccess.Open("res://assets/shaders/motes.gdshader", FileAccess.ModeFlags.Read);
        if (file == null)
        {
            GD.PushError("  motes.gdshader did not open");
            return false;
        }

        // Comments stripped first. The shader explains this exact trap in its
        // own header, so a plain text search finds `CUSTOM0` in the paragraph
        // warning against it and fails a file that is correct — a check that
        // cannot survive its own documentation is not a check.
        string source = StripComments(file.GetAsText());

        bool usesInstance = source.Contains("INSTANCE_CUSTOM");
        bool usesVertexAttribute = source.Contains("CUSTOM0");

        GD.Print($"  INSTANCE_CUSTOM present = {usesInstance}, CUSTOM0 present = {usesVertexAttribute}");

        if (!usesInstance)
            GD.PushError("  the shader never reads INSTANCE_CUSTOM — the per-instance data is unused");

        if (usesVertexAttribute)
        {
            GD.PushError("  the shader reads CUSTOM0, which is a mesh vertex attribute — every mote " +
                         "shares one mesh, so the whole field collapses to a single point");
        }

        return usesInstance && !usesVertexAttribute;
    }

    /// Line comments removed, so a search reads code rather than prose.
    private static string StripComments(string source)
    {
        var kept = new System.Text.StringBuilder();

        foreach (string line in source.Split('\n'))
        {
            int comment = line.IndexOf("//", System.StringComparison.Ordinal);
            kept.AppendLine(comment >= 0 ? line[..comment] : line);
        }

        return kept.ToString();
    }

    /// Distinct data going in is the other half of the same question.
    private bool? StageEveryMoteIsSomewhereElse(int tick)
    {
        var instance = _dust!.GetNodeOrNull<MultiMeshInstance3D>("Motes");
        if (instance?.Multimesh is not { } mesh)
        {
            GD.PushError("  no MultiMesh — the field was never built");
            return false;
        }

        if (!mesh.UseCustomData)
        {
            GD.PushError("  UseCustomData is off, so the custom block is never uploaded and " +
                         "every mote reads zero");
            return false;
        }

        // The raw buffer, not `GetInstanceCustomData`.
        //
        // That getter does not reflect a wholesale `Buffer` assignment: it
        // returns (0, 0, 0, 1) for every instance while the buffer holds exactly
        // the right floats. Which reads as "four hundred motes share one home" —
        // precisely the symptom of the `CUSTOM0` bug above — and cost two rounds
        // of fixing code that was already correct.
        //
        // Twelve floats of transform then four of custom data, so the homes start
        // at offset 12 of every sixteen.
        float[] buffer = mesh.Buffer;
        const int stride = 16;
        const int customAt = 12;

        if (buffer.Length != mesh.InstanceCount * stride)
        {
            GD.PushError($"  the buffer is {buffer.Length} floats for {mesh.InstanceCount} instances, " +
                         $"expected {mesh.InstanceCount * stride} — nothing was uploaded");
            return false;
        }

        var seen = new System.Collections.Generic.HashSet<string>();
        for (int i = 0; i < mesh.InstanceCount; i++)
        {
            int at = i * stride + customAt;
            seen.Add($"{buffer[at]:F4},{buffer[at + 1]:F4},{buffer[at + 2]:F4}");
        }

        GD.Print($"  {mesh.InstanceCount} motes, {seen.Count} distinct homes");

        // Not every single one — two of four hundred colliding in four decimal
        // places is arithmetic, not a bug. Anything near one is the collapse.
        bool spread = seen.Count > mesh.InstanceCount * 0.9f;
        if (!spread)
            GD.PushError($"  only {seen.Count} distinct homes across {mesh.InstanceCount} motes");

        return spread && mesh.InstanceCount == _dust.Count;
    }

    private bool? StageBoundsCoverTheSlab(int tick)
    {
        var instance = _dust!.GetNodeOrNull<MultiMeshInstance3D>("Motes");
        if (instance == null)
            return false;

        Aabb bounds = instance.CustomAabb;
        Vector3 slab = _dust.SlabSize;

        GD.Print($"  custom AABB {bounds.Size} against a slab of {slab}");

        // The mesh is one three-centimetre cube at the origin. Without a custom
        // AABB the renderer measures the bounds from that and culls all four
        // hundred the moment the node's origin leaves the frustum — which, under
        // a camera that turns, is most of the time.
        bool covers = bounds.Size.X >= slab.X && bounds.Size.Y >= slab.Y && bounds.Size.Z >= slab.Z;
        if (!covers)
            GD.PushError("  the AABB is smaller than the slab — the field will cull as a unit");

        return covers;
    }

    /// Position, never rotation.
    private bool? StageFollowsPositionOnly(int tick)
    {
        if (tick == 1)
        {
            _player!.GlobalPosition = new Vector3(21.0f, 0.0f, -13.0f);
            _player.Rotation = new Vector3(0.0f, 1.1f, 0.0f);
            return null;
        }

        // `_Process`, so a physics tick alone is not enough.
        if (tick < 6)
            return null;

        Vector3 at = _dust!.GlobalPosition;
        float away = new Vector2(at.X - _player!.GlobalPosition.X, at.Z - _player.GlobalPosition.Z).Length();
        float turned = Mathf.Abs(_dust.GlobalRotation.Y);

        GD.Print($"  slab is {away:F2} m from the player and turned {turned:F3} rad " +
                 $"while the player faces {_player.Rotation.Y:F2}");

        bool follows = away < 0.01f;

        // A slab that turned with the player would drag the whole field around
        // them every time they looked somewhere else, which is the one thing dust
        // must never do — it would read as the world spinning.
        bool level = turned < 0.001f;

        if (!follows)
            GD.PushError($"  the slab is {away:F2} m from the player — it is not following");
        if (!level)
            GD.PushError($"  the slab turned {turned:F3} rad with the player — the dust would sweep " +
                         "around them as they look about");

        return follows && level;
    }
}
