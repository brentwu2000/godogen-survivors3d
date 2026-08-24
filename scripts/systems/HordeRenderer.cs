using Godot;

/// Draws the whole horde as one MultiMesh — one draw call regardless of count.
///
/// The quads are procedural, never GLB. That distinction matters: MultiMesh
/// silently loses an imported GLB mesh on pack/save (godot.md:46), so the
/// technique is only safe with a mesh built in code, which is exactly what
/// billboard sprites need anyway.
public sealed class HordeRenderer
{
    /// MultiMesh packs each instance as transform, then colour if colours are
    /// on, then custom data. 12 + 4, or 12 + 4 + 4 with colours.
    private readonly int _floatsPerInstance;

    /// Whether the colour block is present, and therefore where custom data
    /// starts. Only the horde turns it on: all four custom floats were already
    /// spoken for (flip, bob phase, spin, array layer) before hit flash needed
    /// somewhere to live, and the colour block is the channel the engine already
    /// provides for exactly this rather than a fifth value bit-packed into one of
    /// the four. Projectiles leave it off — nothing flashes a tracer.
    private readonly bool _useColours;

    private readonly MultiMesh _multiMesh;
    private readonly float[] _buffer;

    public MultiMeshInstance3D Node { get; }

    /// <param name="groundAnchored">
    /// True for characters: the quad is lifted so the instance position is where
    /// the feet are. False for projectiles, which are centred on their position
    /// so the shader's in-plane spin rotates about the middle of the sprite.
    /// </param>
    /// <param name="maxScale">
    /// Largest per-instance scale that will be written. Only affects the custom
    /// AABB — set it too low and the biggest variants vanish at the screen edge
    /// while the rest keep drawing, which reads as a culling glitch rather than
    /// a wrong number.
    /// </param>
    public HordeRenderer(Texture2DArray texture, Shader shader, float heightMeters, int capacity,
                         float arenaExtent, bool groundAnchored = true, float bobAmplitude = 0.06f,
                         float maxScale = 1.0f, bool useColours = false)
    {
        _useColours = useColours;
        _floatsPerInstance = useColours ? 20 : 16;

        float aspect = (float)texture.GetWidth() / texture.GetHeight();

        var material = new ShaderMaterial { Shader = shader };
        material.SetShaderParameter("albedo", texture);
        material.SetShaderParameter("bob_amplitude", bobAmplitude);
        material.SetShaderParameter("flash_enabled", useColours ? 1.0f : 0.0f);

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
            UseColors = useColours,
            UseCustomData = true,
            Mesh = quad,
            InstanceCount = capacity,
            VisibleInstanceCount = 0,
        };

        _buffer = new float[capacity * _floatsPerInstance];

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
                new Vector3(arenaExtent * 2.0f, heightMeters * maxScale + 2.0f, arenaExtent * 2.0f)),
        };
    }

    /// Stacks sprites into the array the shader samples. Returns null and reports
    /// why on any failure — a half-built array would draw the wrong variant for
    /// every layer after the missing one, which looks like a data bug three
    /// systems away from the actual cause.
    public static Texture2DArray? LoadArray(string[] paths)
    {
        var images = new Godot.Collections.Array<Image>();
        int width = 0, height = 0;

        foreach (string path in paths)
        {
            var texture = GD.Load<Texture2D>(path);
            Image? image = texture?.GetImage();
            if (image == null)
            {
                GD.PushError($"HordeRenderer: cannot read {path}");
                return null;
            }

            // A VRAM-compressed import yields a block format the array will not
            // stack. Decompressing here keeps the code independent of an import
            // setting that nothing else in the project pins down.
            if (image.IsCompressed())
                image.Decompress();
            image.Convert(Image.Format.Rgba8);

            if (width == 0)
            {
                width = image.GetWidth();
                height = image.GetHeight();
            }
            else if (image.GetWidth() != width || image.GetHeight() != height)
            {
                GD.PushError(
                    $"HordeRenderer: {path} is {image.GetWidth()}x{image.GetHeight()}, " +
                    $"expected {width}x{height} — every layer of a Texture2DArray must match");
                return null;
            }

            images.Add(image);
        }

        var array = new Texture2DArray();
        Error err = array.CreateFromImages(images);
        if (err != Error.Ok)
        {
            GD.PushError($"HordeRenderer: Texture2DArray creation failed: {err}");
            return null;
        }

        return array;
    }

    /// Uploads the whole instance buffer in one assignment. Per-instance setter
    /// calls cost a marshalled call each, which is what turns the render sync
    /// into the frame's hot spot once the count climbs.
    public void Sync(EnemyPool pool, EnemyTypeResource[] types)
    {
        for (int i = 0; i < pool.Count; i++)
        {
            EnemyTypeResource type = types[pool.Type[i]];
            byte elite = pool.Elite[i];

            // Scaled by how far out of the ground it is. Cosmetic only — the
            // flow field and the damage already treat it as fully present.
            Write(i, Terrain.Plant(pool.Position[i]),
                  type.SpriteScale * Elites.ScaleBonus(elite) * Horde.EmergeScale(pool.Emerge[i]),
                  pool.Velocity[i].X < 0.0f ? 1.0f : 0.0f, pool.Phase[i], 0.0f, type.SpriteLayer,
                  pool.HitFlash[i], Elites.Tint(elite));
        }

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
            Write(i, new Vector3(p.X, Terrain.Height(p.X, p.Z) + p.Y + height * 0.5f, p.Z),
                  pool.Scale[i], 0.0f, 0.0f, spin, 0, flash: 0.0f, tint: pool.Tint[i]);
        }

        Upload(pool.Count);
    }

    private void Write(int index, Vector3 position, float scale, float flip, float phase, float spin,
                       int layer, float flash = 0.0f, Color tint = default)
    {
        int b = index * _floatsPerInstance;

        // Scaled identity basis: the shader rebuilds orientation from the camera
        // and reads the scale back off column 0, so the basis carries size and
        // nothing else. The quad's CenterOffset scales with it, which is what
        // keeps a 1.5x variant standing on the ground rather than sunk into it.
        _buffer[b + 0] = scale; _buffer[b + 1] = 0.0f; _buffer[b + 2] = 0.0f; _buffer[b + 3] = position.X;
        _buffer[b + 4] = 0.0f; _buffer[b + 5] = scale; _buffer[b + 6] = 0.0f; _buffer[b + 7] = position.Y;
        _buffer[b + 8] = 0.0f; _buffer[b + 9] = 0.0f; _buffer[b + 10] = scale; _buffer[b + 11] = position.Z;

        // The colour block, when present, sits between the transform and the
        // custom data — so writing custom data at a fixed offset of 12 is the
        // silent corruption waiting to happen if colours are ever turned on for
        // a renderer that assumed they were off.
        int c = b + 12;
        if (_useColours)
        {
            // Red is the hit flash and green/blue are the elite tint. They share
            // the block because they have to be able to happen at once: an
            // armoured elite being shot is both, and one channel could only say
            // one of them.
            _buffer[c + 0] = flash;
            _buffer[c + 1] = tint.G;
            _buffer[c + 2] = tint.B;
            _buffer[c + 3] = 1.0f;
            c += 4;
        }

        _buffer[c + 0] = flip;
        _buffer[c + 1] = phase;
        _buffer[c + 2] = spin;
        _buffer[c + 3] = layer;
    }

    /// Keeps this renderer's node hidden no matter what it has to draw.
    ///
    /// For the enemy billboards on the solid-body path. They are still synced
    /// every frame on purpose — a fallback that has not run since startup is not
    /// a fallback — but they must not be *seen*, and `Upload` used to decide that
    /// on its own.
    ///
    /// The failure was silent and total: `Horde._Ready` set `Node.Visible = false`
    /// once, and the first `Sync` set it back to `count > 0`. Every enemy in the
    /// game was drawn twice, a low-poly body and a pixel-art billboard standing in
    /// the same place, for as long as both renderers have existed. It survived
    /// every probe, because no probe asks what is on the screen, and it survived
    /// every screenshot, because at the distance a screenshot is framed at the two
    /// silhouettes overlap into one slightly odd shape. What found it was a single
    /// frame of the proof video with a runner close to the camera.
    public bool Muted
    {
        get => _muted;
        set
        {
            _muted = value;

            // Hidden here, not left to `Upload`. That worked only while a muted
            // renderer was still synced every frame; the moment it stopped being,
            // the node kept the visibility it was built with — `true` — and every
            // enemy was drawn twice again, three commits after the first fix.
            if (value)
                Node.Visible = false;
        }
    }

    private bool _muted;

    private void Upload(int count)
    {
        _multiMesh.Buffer = _buffer;
        _multiMesh.VisibleInstanceCount = count;

        // An empty MultiMesh still costs a draw call; hiding it gives that back
        // during the stretches when nothing is in flight.
        Node.Visible = !Muted && count > 0;
    }
}
