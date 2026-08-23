using Godot;

/// One soft dark ellipse under each enemy, for the billboard path only.
///
/// A sprite standing on a floor has nothing that says where it is standing. An
/// enemy walking toward the camera reads as an enemy floating toward it, and two
/// at different depths look like one behind the other. This is the contact — not
/// a shadow so much as a foot.
///
/// **Off on the solid-body path**, where the bodies cast real shadows into the
/// shadow map. Drawing this underneath them as well would give every enemy two
/// ground contacts, which is exactly the mistake the billboards themselves were
/// making until the proof video caught it — and the trap is the same one, so
/// `Muted` works the same way here: `Upload` decides visibility every frame, and a
/// `Visible = false` written once at startup lasts exactly one tick.
public sealed class ShadowRenderer
{
    /// Blob diameter per metre of enemy height.
    ///
    /// Slightly under one. A shadow as wide as the thing is tall reads as a pool
    /// the enemy is standing in; a little narrower reads as the enemy.
    public const float DiameterPerMetre = 0.90f;

    /// How far off the ground the quad sits.
    ///
    /// Two centimetres, and it is enough because the quad does not write depth —
    /// this is only about beating the floor's own z-fighting, not about resolving
    /// two shadows against each other.
    public const float GroundClearance = 0.02f;

    /// Beyond this, nothing is drawn.
    ///
    /// The depth fog closes about twenty-four metres out, so a shadow at
    /// twenty-six is under an enemy nobody can see. It is also where the count
    /// stops growing with the horde: a two-hundred-strong field has most of
    /// itself in the dark.
    public const float CullDistance = 26.0f;

    /// Twelve of transform, four of colour. `UseColors`, because the only
    /// per-instance value is opacity and the colour block is the channel the
    /// engine already provides for it.
    private const int FloatsPerInstance = 16;

    private readonly MultiMesh _multiMesh;
    private readonly float[] _buffer;

    public MultiMeshInstance3D Node { get; }

    /// Keeps this renderer's node hidden no matter what it has to draw.
    ///
    /// **The setter hides the node itself.** Leaving that to `Upload` works only
    /// for as long as `Upload` runs every frame, and once a muted renderer stopped
    /// being synced at all the node kept whatever visibility it was built with —
    /// which is `true` — and every enemy in the game was drawn twice again, from a
    /// different cause, three commits after the first one was fixed. `BodyProbe`
    /// caught it inside a minute.
    public bool Muted
    {
        get => _muted;
        set
        {
            _muted = value;
            if (value)
                Node.Visible = false;
        }
    }

    private bool _muted;

    public int Count { get; private set; }

    public ShadowRenderer(Shader? shader, int capacity, float arenaExtent)
    {
        Material material;
        if (shader != null)
        {
            material = new ShaderMaterial { Shader = shader };
        }
        else
        {
            GD.PushWarning("ShadowRenderer: missing blob.gdshader — shadows will be hard discs");
            material = new StandardMaterial3D
            {
                AlbedoColor = new Color(0.0f, 0.0f, 0.0f, 0.45f),
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                DepthDrawMode = BaseMaterial3D.DepthDrawModeEnum.Disabled,
            };
        }

        // A unit quad: the instance basis carries the diameter, so a blob is
        // scaled rather than rebuilt, and the shader's 0..1 UV means the middle
        // of the mask is the middle of the mesh at every size.
        var quad = new QuadMesh { Size = Vector2.One };

        _multiMesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = true,
            Mesh = quad,
            InstanceCount = capacity,
            VisibleInstanceCount = 0,
        };

        // Assigning the buffer is what allocates it. Setting `InstanceCount`
        // alone leaves `Buffer.Length` at zero and every per-instance setter a
        // silent no-op (godot.md).
        _buffer = new float[capacity * FloatsPerInstance];
        _multiMesh.Buffer = _buffer;

        Node = new MultiMeshInstance3D
        {
            Name = "Shadows",
            Multimesh = _multiMesh,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Visible = false,

            // On the node, not on the mesh.
            //
            // `SurfaceSetMaterial` is the call every other renderer here uses and
            // it does nothing on a `PrimitiveMesh` — a `QuadMesh` carries its
            // material in `Material`, and the surface setter is `ArrayMesh`'s.
            // It compiles, it does not warn, and the blobs came out with no
            // material at all: correctly placed, correctly sized, correctly
            // oriented, and invisible. The probe passed the whole time, because
            // everything it reads lives in the buffer.
            MaterialOverride = material,

            // Its own AABB, because every instance is written into the buffer
            // rather than moved as a node — Godot has no way to work out where
            // this ends up otherwise, and the whole field vanishes the moment the
            // origin leaves the view.
            CustomAabb = new Aabb(
                new Vector3(-arenaExtent, -2.0f, -arenaExtent),
                new Vector3(arenaExtent * 2.0f, 6.0f, arenaExtent * 2.0f)),
        };
    }

    /// Writes one blob per living enemy within `CullDistance` of `viewer`.
    public void Sync(EnemyPool pool, EnemyTypeResource[] types, Vector3 viewer)
    {
        int written = 0;
        float cullSquared = CullDistance * CullDistance;

        for (int i = 0; i < pool.Count; i++)
        {
            if ((written + 1) * FloatsPerInstance > _buffer.Length)
                break;

            Vector3 at = pool.Position[i];

            float dx = at.X - viewer.X;
            float dz = at.Z - viewer.Z;
            float distanceSquared = dx * dx + dz * dz;
            if (distanceSquared > cullSquared)
                continue;

            int variant = pool.Type[i];
            if (variant < 0 || variant >= types.Length)
                continue;

            EnemyTypeResource type = types[variant];

            // The same three factors the body is scaled by, so a half-risen
            // spawn has a half-sized shadow and an armoured elite a wider one.
            // A blob that stayed full size while the enemy grew out of the floor
            // would announce the spawn before the spawn arrives.
            float emerge = Horde.EmergeScale(pool.Emerge[i]);
            float diameter = type.DesignHeightMeters * DiameterPerMetre
                           * Elites.ScaleBonus(pool.Elite[i]) * emerge;

            if (diameter <= 0.0f)
                continue;

            // Faded out over the last quarter of the range rather than cut. A
            // hard cull pops a disc into existence under an enemy that was
            // already on screen, which is more noticeable than the shadow.
            float distance = Mathf.Sqrt(distanceSquared);
            float fade = Mathf.Clamp((CullDistance - distance) / (CullDistance * 0.25f), 0.0f, 1.0f);
            float alpha = 0.5f * fade * emerge;

            Write(written++, at, diameter, alpha);
        }

        Count = written;
        Upload(written);
    }

    /// One instance: a unit quad laid flat, scaled, and planted.
    ///
    /// The basis is the -90° turn about X that `QuadMesh` needs to face upward,
    /// written out rather than composed from a `Transform3D`. A `QuadMesh` is
    /// authored in the XY plane facing +Z, so its own Y axis has to become -Z and
    /// its Z axis +Y; the buffer is row-major 3x4, which is why the rotation
    /// appears transposed against every diagram of it.
    private void Write(int slot, Vector3 at, float diameter, float alpha)
    {
        int b = slot * FloatsPerInstance;
        float d = diameter;

        _buffer[b + 0] = d;    _buffer[b + 1] = 0.0f; _buffer[b + 2] = 0.0f;
        _buffer[b + 3] = at.X;

        _buffer[b + 4] = 0.0f; _buffer[b + 5] = 0.0f; _buffer[b + 6] = d;
        _buffer[b + 7] = Terrain.Height(at.X, at.Z) + GroundClearance;

        _buffer[b + 8] = 0.0f; _buffer[b + 9] = -d;   _buffer[b + 10] = 0.0f;
        _buffer[b + 11] = at.Z;

        // Black at `alpha`, and the alpha is linear on purpose — it is an opacity,
        // not a colour, so the sRGB conversion the tinted renderers do would be
        // wrong here.
        _buffer[b + 12] = 0.0f;
        _buffer[b + 13] = 0.0f;
        _buffer[b + 14] = 0.0f;
        _buffer[b + 15] = alpha;
    }

    private void Upload(int count)
    {
        _multiMesh.Buffer = _buffer;
        _multiMesh.VisibleInstanceCount = count;
        Node.Visible = !Muted && count > 0;
    }
}
