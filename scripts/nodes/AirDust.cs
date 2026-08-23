using Godot;

/// Dust hanging in the air around the player.
///
/// The arena had a floor, cover, a horizon and nothing in between them. Every
/// object in the frame was either on the ground or in the sky, so the space
/// between the camera and the far wall was empty in a way that reads as a
/// diagram rather than as a place — and the fog, which is the thing that makes
/// distance dangerous, had nothing to catch on.
///
/// Four hundred two-centimetre motes in a slab that rides with the player. One
/// buffer upload at startup and nothing per frame: every mote's home and phase
/// live in its per-instance custom data and `motes.gdshader` does the drifting,
/// so this costs a draw call and no CPU at all.
public partial class AirDust : Node3D
{
    [Export] public int Count { get; set; } = 400;

    /// Metres across, up and deep. Wider than the fog reaches, so a mote is never
    /// seen to wink out at the edge of the field — the fog swallows them first.
    [Export] public Vector3 SlabSize { get; set; } = new(34.0f, 7.0f, 34.0f);

    /// Three centimetres. Small enough to be a speck at any distance the camera
    /// sits, which is what lets them be cubes rather than billboards — an object
    /// this size has no silhouette to get wrong.
    ///
    /// The slab surrounds the *player* and the camera sits thirteen metres behind
    /// them, so some motes are always between the lens and the character. At five
    /// centimetres those near ones read as flakes of snow rather than as dust,
    /// and they are the only ones the eye actually resolves.
    [Export] public float Size { get; set; } = 0.03f;

    private Node3D? _player;
    private MultiMesh? _motes;

    public override void _Ready()
    {
        _player = GetParent()?.GetNodeOrNull<Node3D>("Player");

        var shader = GD.Load<Shader>("res://assets/shaders/motes.gdshader");
        if (shader == null)
        {
            GD.PushWarning("AirDust: motes.gdshader missing — the air will be empty");
            return;
        }

        var material = new ShaderMaterial { Shader = shader };
        material.SetShaderParameter("slab", SlabSize);

        var builder = new MeshBuilder();
        builder.Box(Vector3.Zero, Vector3.One * Size, Colors.White);
        ArrayMesh mesh = builder.Build();
        mesh.SurfaceSetMaterial(0, material);

        _motes = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseCustomData = true,
            Mesh = mesh,
            InstanceCount = Count,

            // Zero here, raised after the buffer is assigned. Every other
            // MultiMesh in this project is built in that order, and it is not
            // decoration: see below.
            VisibleInstanceCount = 0,
        };

        // Written once, through the per-instance setters rather than the raw
        // buffer.
        //
        // Four hundred calls at startup and none afterwards, so the cost is
        // nothing and the layout is not this file's problem. A hand-packed
        // `Buffer` has to know the stride, which depends on the transform format
        // and on whether colours are enabled — get it wrong and Godot neither
        // errors nor warns: every custom block reads (0, 0, 0, 1), every mote
        // computes the same position, and the field draws as one speck. Which is
        // exactly what the first version of this did.
        //
        // Every transform is the identity. The shader places the mote from its
        // custom data, so there is nothing here for the CPU to update when it
        // drifts — the alternative is writing four hundred transforms a frame for
        // the same picture.
        // Twelve floats of transform, four of custom data, assigned as one array.
        //
        // Assigning `Buffer` is also what allocates it: setting `InstanceCount`
        // alone leaves `Buffer.Length` at zero, and the per-instance setters
        // against that store nothing and report nothing.
        //
        // Do not verify this with `GetInstanceCustomData`. That getter does not
        // reflect a wholesale `Buffer` assignment — it returns (0, 0, 0, 1) for
        // every instance while `Buffer` holds exactly the right floats, which
        // reads as four hundred motes sharing one home and is the same symptom
        // as the `CUSTOM0` mistake this shader is built to avoid. Read the raw
        // buffer, which is what actually goes to the GPU.
        //
        // Every transform is the identity: the shader places the mote from its
        // custom data, so there is nothing for the CPU to update as it drifts.
        var buffer = new float[Count * 16];
        ulong rng = 0x9E3779B97F4A7C15UL;

        for (int i = 0; i < Count; i++)
        {
            int at = i * 16;
            buffer[at + 0] = 1.0f; buffer[at + 5] = 1.0f; buffer[at + 10] = 1.0f;

            buffer[at + 12] = Next(ref rng);
            buffer[at + 13] = Next(ref rng);
            buffer[at + 14] = Next(ref rng);
            buffer[at + 15] = Next(ref rng);
        }

        _motes.Buffer = buffer;
        _motes.VisibleInstanceCount = Count;

        AddChild(new MultiMeshInstance3D
        {
            Name = "Motes",
            Multimesh = _motes,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,

            // The slab, plus a margin for the drift. Without a custom AABB the
            // renderer measures the bounds from the mesh — one two-centimetre
            // cube at the origin — and culls the whole field the moment the node
            // leaves the frustum, which under a camera that turns is constantly.
            CustomAabb = new Aabb(-SlabSize, SlabSize * 2.0f),
        });
    }

    /// Rides with the player, and only with their position.
    ///
    /// `_Process` rather than `_PhysicsProcess`: this is scenery, and following
    /// at the physics rate would make the dust jitter against a camera that
    /// interpolates. Rotation is deliberately ignored — a slab that turned with
    /// the player would drag the entire field around them every time they looked
    /// somewhere else, which is the one thing dust must never do.
    public override void _Process(double delta)
    {
        if (_player == null)
            return;

        GlobalPosition = new Vector3(
            _player.GlobalPosition.X,
            SlabSize.Y * 0.5f - 1.0f,
            _player.GlobalPosition.Z);
    }

    private static float Next(ref ulong state)
    {
        state ^= state << 13;
        state ^= state >> 7;
        state ^= state << 17;
        return (state >> 40) / 16777216.0f;
    }

    /// How many motes are drawn. Only a probe asks.
    public int Drawn => _motes?.VisibleInstanceCount ?? 0;
}
