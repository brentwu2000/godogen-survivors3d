using Godot;

/// The small stuff on the floor: shards, tufts and rubble.
public enum ScatterKind
{
    /// A flat fragment lying where it fell. The commonest, because a floor with
    /// nothing flat on it reads as swept.
    Shard,

    /// A clump of dry growth pushing through the surface. The only thing in the
    /// set that stands up, so it is what breaks the horizontal.
    Tuft,

    /// A little pile. Reads as somewhere something came apart.
    Rubble,
}

/// Ankle-high decoration, in three MultiMeshes.
///
/// Cover is what the player walks around and the ground shader is what they walk
/// on; between those two there was nothing at all. A floor that is a texture
/// meeting a wall that is a box has no scale to it — the arena read as large and
/// empty because there was no object small enough to measure it against.
///
/// None of it collides and none of it can be picked up. That is the whole point:
/// it is the cheapest possible thing to add, so there can be a thousand.
///
/// Per-instance colour, unlike `PropRenderer`, which is why this is a separate
/// class rather than another kind in that one. Sixteen floats per instance
/// instead of twelve, on a thousand instances, to buy scatter that agrees with
/// whichever zone of the map it is lying in — a uniform brown speckle over four
/// differently-tinted zones would fight the one thing the ground shader is for.
public sealed class ScatterField
{
    /// Twelve of transform, four of colour. `UseColors` rather than custom data:
    /// the colour block is the channel the engine already provides for exactly
    /// this, and nothing here needs the custom floats for anything else.
    private const int FloatsPerInstance = 16;

    private readonly MultiMesh[] _multi;
    private readonly float[][] _buffers;
    private readonly int[] _counts;

    public Node3D Node { get; }

    public ScatterField(int capacityPerKind, float arenaExtent)
    {
        var kinds = System.Enum.GetValues<ScatterKind>();
        _multi = new MultiMesh[kinds.Length];
        _buffers = new float[kinds.Length][];
        _counts = new int[kinds.Length];

        Node = new Node3D { Name = "Scatter" };

        foreach (ScatterKind kind in kinds)
        {
            int index = (int)kind;

            ArrayMesh mesh = Build(kind);
            mesh.SurfaceSetMaterial(0, PropLibrary.Material());

            _multi[index] = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                UseColors = true,
                Mesh = mesh,
                InstanceCount = capacityPerKind,
                VisibleInstanceCount = 0,
            };

            _buffers[index] = new float[capacityPerKind * FloatsPerInstance];

            Node.AddChild(new MultiMeshInstance3D
            {
                Name = kind.ToString(),
                Multimesh = _multi[index],

                // No shadows. A thousand ankle-high objects each casting into the
                // shadow map is the whole shadow budget spent on things nobody
                // looks at, and at this size the shadow is two pixels.
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,

                CustomAabb = new Aabb(
                    new Vector3(-arenaExtent, -0.5f, -arenaExtent),
                    new Vector3(arenaExtent * 2.0f, 2.0f, arenaExtent * 2.0f)),
            });
        }
    }

    public void Clear() => System.Array.Clear(_counts);

    /// Places one piece.
    ///
    /// `tint` is converted here, once. A per-instance colour is handed to the
    /// shader as-is and treated as linear, unlike a texture, which is decoded
    /// from sRGB first — so a tint written as 0.5 and not converted arrives as
    /// roughly its own square root, and every piece of scatter comes out visibly
    /// paler than the ground it is lying on. The same conversion `MeshBuilder`
    /// does for vertex colours, for the same reason.
    public void Add(ScatterKind kind, Vector2 at, float yaw, float scale, Color tint, float height = 0.0f)
    {
        int index = (int)kind;
        int slot = _counts[index];
        float[] buffer = _buffers[index];

        if ((slot + 1) * FloatsPerInstance > buffer.Length)
            return;

        _counts[index] = slot + 1;
        int write = slot * FloatsPerInstance;

        float c = Mathf.Cos(yaw) * scale;
        float s = Mathf.Sin(yaw) * scale;

        buffer[write + 0] = c;     buffer[write + 1] = 0.0f;  buffer[write + 2] = s;     buffer[write + 3] = at.X;
        buffer[write + 4] = 0.0f;  buffer[write + 5] = scale; buffer[write + 6] = 0.0f;  buffer[write + 7] = height;
        buffer[write + 8] = -s;    buffer[write + 9] = 0.0f;  buffer[write + 10] = c;    buffer[write + 11] = at.Y;

        Color linear = tint.SrgbToLinear();
        buffer[write + 12] = linear.R;
        buffer[write + 13] = linear.G;
        buffer[write + 14] = linear.B;
        buffer[write + 15] = 1.0f;
    }

    public void Commit()
    {
        for (int index = 0; index < _multi.Length; index++)
        {
            _multi[index].Buffer = _buffers[index];
            _multi[index].VisibleInstanceCount = _counts[index];
            ((MultiMeshInstance3D)Node.GetChild(index)).Visible = _counts[index] > 0;
        }
    }

    public int Count(ScatterKind kind) => _counts[(int)kind];

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

    /// The three shapes, authored around the origin at roughly a metre so the
    /// instance scale reads as metres.
    ///
    /// White, because the colour arrives per instance. A vertex colour here would
    /// multiply into the instance colour and every piece would come out darker
    /// than asked for, which is the kind of thing that gets fixed by brightening
    /// the tint until it looks right and is then wrong everywhere else.
    private static ArrayMesh Build(ScatterKind kind)
    {
        var mesh = new MeshBuilder();

        switch (kind)
        {
            case ScatterKind.Shard:
                mesh.Box(new Vector3(0.0f, 0.02f, 0.0f), new Vector3(0.42f, 0.04f, 0.30f), Colors.White);
                mesh.Box(new Vector3(0.18f, 0.03f, -0.12f), new Vector3(0.22f, 0.06f, 0.16f), Colors.White, 24.0f);
                break;

            case ScatterKind.Tuft:
                for (int i = 0; i < 5; i++)
                {
                    float angle = Mathf.Tau * i / 5.0f;
                    float lean = 0.10f;

                    mesh.Tube(
                        new Vector3(Mathf.Cos(angle) * 0.04f, 0.0f, Mathf.Sin(angle) * 0.04f),
                        new Vector3(Mathf.Cos(angle) * lean, 0.30f + i * 0.03f, Mathf.Sin(angle) * lean),
                        0.018f, Colors.White, sides: 3);
                }

                break;

            case ScatterKind.Rubble:
                mesh.Box(new Vector3(0.0f, 0.06f, 0.0f), new Vector3(0.26f, 0.12f, 0.22f), Colors.White, 12.0f);
                mesh.Box(new Vector3(0.16f, 0.04f, 0.10f), new Vector3(0.18f, 0.08f, 0.16f), Colors.White, -31.0f);
                mesh.Box(new Vector3(-0.13f, 0.03f, -0.09f), new Vector3(0.14f, 0.06f, 0.12f), Colors.White, 47.0f);
                break;
        }

        return mesh.Build();
    }
}
