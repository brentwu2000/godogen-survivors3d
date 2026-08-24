using Godot;

/// One solid body, for the player.
///
/// A MultiMesh holding a single instance, which looks like a strange way to draw
/// one thing and is the honest one. `body.gdshader` reads `INSTANCE_CUSTOM` for
/// pace, phase, flash and jitter, and `INSTANCE_CUSTOM` is zero on an ordinary
/// `MeshInstance3D` — a player drawn that way stands rigid while it runs, and
/// nothing in the code would look wrong.
///
/// The alternatives were worse. A second shader for the player is two copies of
/// the walk to keep in step. A uniform fallback inside the one shader is a branch
/// evaluated per vertex for every body in the horde to serve exactly one of them.
/// A MultiMesh of one costs a draw call the player was already paying for.
public sealed class SoloBody
{
    private const int FloatsPerInstance = 16;

    /// Same gait as the horde. Shared through `BodyRenderer` rather than copied,
    /// because a player whose stride advanced at a different rate from the
    /// enemies' would look wrong in a way nobody could name.
    private readonly MultiMesh _multiMesh;
    private readonly float[] _buffer = new float[FloatsPerInstance];

    public MultiMeshInstance3D Node { get; }

    /// Where in its stride the body is, in turns. Public so a probe can read the
    /// gait without inferring it from a transform.
    public float Stride { get; private set; }

    /// A body built from a baked model rather than from a procedural spec.
    ///
    /// The mesh arrives already carrying the rig in its UV channels, so nothing
    /// downstream can tell the difference — which is the whole point of the bake.
    /// `height` is what the caller must supply because the bounds a `MultiMesh`
    /// needs cannot be read off a mesh whose instance transform is world space.
    public SoloBody(Shader shader, ArrayMesh mesh, float height, float arenaExtent)
        : this(shader, mesh, height, arenaExtent, fromSpec: false)
    {
    }

    public SoloBody(Shader shader, BodyMeshLibrary.Build spec, float arenaExtent)
        : this(shader, BodyMeshLibrary.Build3D(spec),
               BodyMeshLibrary.StandingHeight(spec), arenaExtent, fromSpec: true)
    {
    }

    private SoloBody(Shader shader, ArrayMesh mesh, float height, float arenaExtent, bool fromSpec)
    {
        mesh.SurfaceSetMaterial(0, new ShaderMaterial { Shader = shader });

        _multiMesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseCustomData = true,
            Mesh = mesh,
            InstanceCount = 1,
            VisibleInstanceCount = 1,
        };

        Node = new MultiMeshInstance3D
        {
            Name = "Body",
            Multimesh = _multiMesh,

            // The rig is a child of the player and moves with it, so the mesh's
            // own bounds would be right — except that the instance transform
            // inside a MultiMesh is world space, not local, so the node itself
            // never moves and its bounds have to cover everywhere the body can
            // go. Without this the player vanishes as soon as the origin leaves
            // the frustum, which under a camera that turns is most of the time.
            CustomAabb = new Aabb(
                new Vector3(-arenaExtent, -1.0f, -arenaExtent),
                new Vector3(arenaExtent * 2.0f, height + 2.0f, arenaExtent * 2.0f)),
        };
    }

    /// Places the body and advances its gait.
    ///
    /// `speed` is how fast the player is actually travelling, so the legs stop
    /// when the player does. `yaw` is where the body faces, which under
    /// turn-and-advance is the view direction rather than the direction of
    /// travel — the player can back away from something while still looking at
    /// it, and the body should too.
    public void Update(Vector3 position, float yaw, float speed, float delta, float flash)
    {
        Stride = BodyRenderer.AdvanceStride(Stride, speed, delta);

        float c = Mathf.Cos(yaw);
        float s = Mathf.Sin(yaw);

        _buffer[0] = c;     _buffer[1] = 0.0f; _buffer[2] = s;     _buffer[3] = position.X;
        _buffer[4] = 0.0f;  _buffer[5] = 1.0f; _buffer[6] = 0.0f;  _buffer[7] = position.Y;
        _buffer[8] = -s;    _buffer[9] = 0.0f; _buffer[10] = c;    _buffer[11] = position.Z;

        _buffer[12] = BodyRenderer.Pack(speed, Stride);

        // No hue shift and no jitter. The player is never an elite, and a player
        // whose brightness wandered would be the one body on screen whose
        // appearance meant nothing.
        _buffer[13] = 0.0f;
        _buffer[14] = flash;
        _buffer[15] = 0.0f;

        _multiMesh.Buffer = _buffer;
    }
}
