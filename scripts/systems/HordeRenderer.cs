using Godot;

/// Draws the whole horde as one MultiMesh — one draw call regardless of count.
///
/// The quads are procedural, never GLB. That distinction matters: MultiMesh
/// silently loses an imported GLB mesh on pack/save (godot.md:46), so the
/// technique is only safe with a mesh built in code, which is exactly what
/// billboard sprites need anyway.
public sealed class HordeRenderer
{
    /// 12 floats of transform plus 4 of custom data, matching MultiMesh's buffer
    /// layout when colours are off and custom data is on.
    private const int FloatsPerInstance = 16;

    private readonly MultiMesh _multiMesh;
    private readonly float[] _buffer;

    public MultiMeshInstance3D Node { get; }

    /// <param name="groundAnchored">
    /// True for characters: the quad is lifted so the instance position is where
    /// the feet are. False for projectiles, which are centred on their position
    /// so the shader's in-plane spin rotates about the middle of the sprite.
    /// </param>
    public HordeRenderer(Texture2D texture, Shader shader, float heightMeters, int capacity,
                         float arenaExtent, bool groundAnchored = true, float bobAmplitude = 0.06f)
    {
        float aspect = (float)texture.GetWidth() / texture.GetHeight();

        var material = new ShaderMaterial { Shader = shader };
        material.SetShaderParameter("albedo", texture);
        material.SetShaderParameter("bob_amplitude", bobAmplitude);

        var quad = new QuadMesh
        {
            Size = new Vector2(heightMeters * aspect, heightMeters),
            CenterOffset = groundAnchored
                ? new Vector3(0.0f, heightMeters * 0.5f, 0.0f)
                : Vector3.Zero,
            Material = material,
        };

        _multiMesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseCustomData = true,
            Mesh = quad,
            InstanceCount = capacity,
            VisibleInstanceCount = 0,
        };

        _buffer = new float[capacity * FloatsPerInstance];

        Node = new MultiMeshInstance3D
        {
            Name = "Horde",
            Multimesh = _multiMesh,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,

            // Instances roam the whole arena while the mesh's own bounds are one
            // quad. Without a custom AABB the renderer culls the entire horde the
            // moment the origin leaves the frustum.
            CustomAabb = new Aabb(
                new Vector3(-arenaExtent, -1.0f, -arenaExtent),
                new Vector3(arenaExtent * 2.0f, heightMeters + 2.0f, arenaExtent * 2.0f)),
        };
    }

    /// Uploads the whole instance buffer in one assignment. Per-instance setter
    /// calls cost a marshalled call each, which is what turns the render sync
    /// into the frame's hot spot once the count climbs.
    public void Sync(EnemyPool pool)
    {
        for (int i = 0; i < pool.Count; i++)
            Write(i, pool.Position[i], pool.Velocity[i].X < 0.0f ? 1.0f : 0.0f, pool.Phase[i], 0.0f);

        Upload(pool.Count);
    }

    /// Projectiles carry an in-plane rotation instead of a mirror flag, so an
    /// arrow points where it is going rather than always drawing upright.
    public void Sync(ProjectilePool pool, float height)
    {
        for (int i = 0; i < pool.Count; i++)
        {
            Vector2 velocity = pool.Velocity[i];

            // Screen-space angle, not world: the quad is billboarded, so the spin
            // has to be expressed in the plane the camera sees. Negating Y maps
            // world +Z (away from camera) onto screen down.
            float spin = Mathf.Atan2(-velocity.Y, velocity.X);

            Vector3 p = pool.Position[i];
            Write(i, new Vector3(p.X, p.Y + height * 0.5f, p.Z), 0.0f, 0.0f, spin);
        }

        Upload(pool.Count);
    }

    private void Write(int index, Vector3 position, float flip, float phase, float spin)
    {
        int b = index * FloatsPerInstance;

        // Identity basis: the shader rebuilds orientation from the camera, so
        // only the translation column carries information here. Scale stays at 1
        // and the shader picks it up from column 0 if it ever varies.
        _buffer[b + 0] = 1.0f; _buffer[b + 1] = 0.0f; _buffer[b + 2] = 0.0f; _buffer[b + 3] = position.X;
        _buffer[b + 4] = 0.0f; _buffer[b + 5] = 1.0f; _buffer[b + 6] = 0.0f; _buffer[b + 7] = position.Y;
        _buffer[b + 8] = 0.0f; _buffer[b + 9] = 0.0f; _buffer[b + 10] = 1.0f; _buffer[b + 11] = position.Z;

        _buffer[b + 12] = flip;
        _buffer[b + 13] = phase;
        _buffer[b + 14] = spin;
        _buffer[b + 15] = 0.0f;
    }

    private void Upload(int count)
    {
        _multiMesh.Buffer = _buffer;
        _multiMesh.VisibleInstanceCount = count;

        // An empty MultiMesh still costs a draw call; hiding it gives that back
        // during the stretches when nothing is in flight.
        Node.Visible = count > 0;
    }
}
