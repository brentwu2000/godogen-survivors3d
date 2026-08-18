using Godot;

/// Draws every piece of cover in the arena as one MultiMesh per kind.
///
/// The arena holds fifty to seventy pieces of cover. As individual nodes that is
/// fifty to seventy draw calls on a frame that has spent five phases staying
/// near twenty — and the platform with the tighter budget is the one this was
/// always sized for. Grouped by kind it is one call each, no matter how many the
/// seed decided to place.
///
/// This is the same technique the horde uses and it is legal for the same reason:
/// the meshes are procedural. MultiMesh loses an *imported* mesh on pack/save
/// (godot.md:46), which is why the props are built from boxes rather than sourced
/// as models.
public sealed class PropRenderer
{
    private const int FloatsPerInstance = 12;

    private readonly MultiMesh[] _multi;
    private readonly float[][] _buffers;
    private readonly int[] _counts;

    /// Holds one MultiMeshInstance3D per kind. Added to the scene by the caller.
    public Node3D Node { get; }

    public PropRenderer(int capacityPerKind, float arenaExtent)
    {
        var kinds = System.Enum.GetValues<PropKind>();
        _multi = new MultiMesh[kinds.Length];
        _buffers = new float[kinds.Length][];
        _counts = new int[kinds.Length];

        Node = new Node3D { Name = "Props" };
        StandardMaterial3D material = PropLibrary.Material();

        foreach (PropKind kind in kinds)
        {
            int index = (int)kind;

            // Landmarks are placed one at a time and there are never many, but a
            // separate path for them would be a second way to get the AABB wrong.
            int capacity = PropLibrary.IsLandmark(kind) ? 8 : capacityPerKind;

            ArrayMesh mesh = PropLibrary.Build(kind);
            mesh.SurfaceSetMaterial(0, material);

            _multi[index] = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                Mesh = mesh,
                InstanceCount = capacity,
                VisibleInstanceCount = 0,
            };

            _buffers[index] = new float[capacity * FloatsPerInstance];

            Node.AddChild(new MultiMeshInstance3D
            {
                Name = kind.ToString(),
                Multimesh = _multi[index],

                // Landmarks do not cast. They stand outside the arena and are ten
                // to fourteen metres tall, so their shadows fall across play space
                // they are not in — a bar of darkness with no visible cause, which
                // reads as a rendering fault rather than as a water tower. It also
                // buys back two draw calls: every MultiMesh costs one in the main
                // pass and one in the shadow pass, which is where the cover budget
                // actually went.
                CastShadow = PropLibrary.IsLandmark(kind)
                    ? GeometryInstance3D.ShadowCastingSetting.Off
                    : GeometryInstance3D.ShadowCastingSetting.On,

                // Instances are scattered across the whole arena while the mesh's
                // own bounds are one prop. Without a custom AABB the renderer
                // culls every piece of cover the moment the origin leaves the
                // frustum — which, on a camera that follows the player, is most
                // of the time.
                CustomAabb = new Aabb(
                    new Vector3(-arenaExtent, -1.0f, -arenaExtent),
                    new Vector3(arenaExtent * 2.0f, PropLibrary.Height(kind) * 2.0f + 2.0f, arenaExtent * 2.0f)),
            });
        }
    }

    public void Clear()
    {
        for (int i = 0; i < _counts.Length; i++)
            _counts[i] = 0;
    }

    /// Places one prop. `footprint` is the X/Z size it should cover; the height
    /// is the kind's own, scaled by `heightScale` for variety.
    public void Add(PropKind kind, Vector2 centre, Vector2 footprint, float yawDegrees, float heightScale = 1.0f)
    {
        int index = (int)kind;
        int slot = _counts[index];
        if (slot * FloatsPerInstance >= _buffers[index].Length)
            return;

        float yaw = Mathf.DegToRad(yawDegrees);
        float cos = Mathf.Cos(yaw), sin = Mathf.Sin(yaw);
        float[] buffer = _buffers[index];
        int b = slot * FloatsPerInstance;

        // Row-major 3x4: the basis rows first, then the translation in the last
        // column of each row. Scale is baked into the basis, which is what lets a
        // container stretch along a long footprint without stretching its height.
        buffer[b + 0] = cos * footprint.X;  buffer[b + 1] = 0.0f;        buffer[b + 2] = -sin * footprint.Y;  buffer[b + 3] = centre.X;
        buffer[b + 4] = 0.0f;               buffer[b + 5] = heightScale; buffer[b + 6] = 0.0f;                buffer[b + 7] = 0.0f;
        buffer[b + 8] = sin * footprint.X;  buffer[b + 9] = 0.0f;        buffer[b + 10] = cos * footprint.Y;  buffer[b + 11] = centre.Y;

        _counts[index] = slot + 1;
    }

    /// Uploads every kind's buffer in one assignment each. Per-instance setters
    /// cost a marshalled call apiece, which is the thing this whole class exists
    /// to avoid paying seventy times.
    public void Commit()
    {
        foreach (PropKind kind in System.Enum.GetValues<PropKind>())
        {
            int index = (int)kind;
            _multi[index].Buffer = _buffers[index];
            _multi[index].VisibleInstanceCount = _counts[index];

            // An empty MultiMesh still costs a draw call; hiding it gives that
            // back on the seeds that never place a given kind.
            Node.GetChild<MultiMeshInstance3D>(index).Visible = _counts[index] > 0;
        }
    }

    public int Count(PropKind kind) => _counts[(int)kind];

    public int Total
    {
        get
        {
            int total = 0;
            foreach (int count in _counts)
                total += count;
            return total;
        }
    }
}
