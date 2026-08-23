using Godot;

/// Draws the horde as solid bodies — one MultiMesh per variant.
///
/// Per variant rather than one for everything, because a MultiMesh is one mesh
/// and N transforms and a brute is not a scaled walker. Six draw calls instead of
/// one is the price, and it is the right trade: the alternative is either one
/// mesh containing every variant's geometry with the unused parts scaled to zero
/// — paying for all six bodies on every instance — or a MeshInstance3D per enemy,
/// which is the draw-call budget this project has defended since Phase 2.
///
/// Sixteen floats per instance: twelve of transform, four of custom data. No
/// colour block. The billboard renderer needs one because all four of its custom
/// floats were spoken for; here the four are exactly enough — pace and phase
/// packed together, hue shift, hit flash, brightness jitter — and turning the
/// colour block on would add four floats per instance per frame to carry nothing.
public sealed class BodyRenderer
{
    private const int FloatsPerInstance = 16;

    /// Metres per second per integer unit of the packed pace. Must match
    /// `PACE_QUANTUM` in body.gdshader.
    private const float PaceQuantum = 0.25f;

    /// Largest pace the integer part will hold, in quanta. Sixty-three is 15.75
    /// m/s, four times anything in the game, and it leaves eighteen bits of
    /// mantissa for the fraction — about a quarter of a millionth of a stride,
    /// where a thousandth would already be invisible.
    private const float MaxPaceUnits = 63.0f;

    /// Strides per metre walked. A body covers about 1.4 m per full cycle of two
    /// steps, which is a person's gait; anything much faster reads as skittering
    /// and much slower as moonwalking, and both are more obvious than the number
    /// suggests.
    private const float StridesPerMetre = 1.0f / 1.4f;

    private readonly MultiMesh[] _multiMeshes;
    private readonly MultiMeshInstance3D[] _nodes;
    private readonly float[][] _buffers;
    private readonly int[] _counts;

    public Node3D Node { get; }

    public BodyRenderer(Shader shader, EnemyTypeResource[] types, int capacityPerVariant,
                        float arenaExtent, float tallest)
    {
        int variants = types.Length;
        _multiMeshes = new MultiMesh[variants];
        _nodes = new MultiMeshInstance3D[variants];
        _buffers = new float[variants][];
        _counts = new int[variants];

        Node = new Node3D { Name = "Bodies" };

        for (int i = 0; i < variants; i++)
        {
            ArrayMesh mesh = BodyMeshLibrary.Build3D(
                BodyMeshLibrary.ForVariant(types[i].TypeName, types[i].DesignHeightMeters));

            mesh.SurfaceSetMaterial(0, new ShaderMaterial { Shader = shader });

            _multiMeshes[i] = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                UseCustomData = true,
                Mesh = mesh,
                InstanceCount = capacityPerVariant,
                VisibleInstanceCount = 0,
            };

            _buffers[i] = new float[capacityPerVariant * FloatsPerInstance];

            _nodes[i] = new MultiMeshInstance3D
            {
                Name = $"Body_{types[i].TypeName}",
                Multimesh = _multiMeshes[i],

                // Instances roam the whole arena while the mesh's own bounds are
                // one body. Without a custom AABB the renderer culls every
                // instance of a variant the moment the origin leaves the frustum
                // — which looks like a whole species blinking out at once.
                CustomAabb = new Aabb(
                    new Vector3(-arenaExtent, -1.0f, -arenaExtent),
                    new Vector3(arenaExtent * 2.0f, tallest + 2.0f, arenaExtent * 2.0f)),
            };

            Node.AddChild(_nodes[i]);
        }
    }

    /// Advances every body's stride by how far it walked.
    ///
    /// By intended speed rather than by measured displacement. The two differ
    /// exactly where it matters: a body pressed against a wall or held by contact
    /// has a velocity and no displacement, and integrating displacement would
    /// leave it standing frozen mid-step. Knockback is displacement with no
    /// velocity, and integrating that would make a shotgun blast moonwalk its
    /// target.
    ///
    /// Separate from `Sync` because it is the only part that is a function of
    /// time. Calling it twice in a frame would double every gait; calling `Sync`
    /// twice costs nothing but the work.
    public static void Advance(EnemyPool pool, float delta)
    {
        for (int i = 0; i < pool.Count; i++)
        {
            float speed = pool.Velocity[i].Length();
            if (speed <= 0.001f)
                continue;

            pool.Stride[i] = AdvanceStride(pool.Stride[i], speed, delta);

            // Only while there is a direction. Velocity is zeroed on contact, and
            // a body that snapped to face north the moment it started biting
            // would be worse than one that never turned at all.
            pool.Yaw[i] = Mathf.Atan2(-pool.Velocity[i].X, -pool.Velocity[i].Y);
        }
    }

    public void Sync(EnemyPool pool, EnemyTypeResource[] types)
    {
        System.Array.Clear(_counts);

        for (int i = 0; i < pool.Count; i++)
        {
            int variant = pool.Type[i];
            if (variant >= _buffers.Length)
                continue;

            int slot = _counts[variant];
            if (slot * FloatsPerInstance + FloatsPerInstance > _buffers[variant].Length)
                continue;

            _counts[variant] = slot + 1;

            EnemyTypeResource type = types[variant];
            byte elite = pool.Elite[i];
            // Scaled by how far out of the ground it is. Cosmetic only — the
            // flow field, the collider and the damage all treat a half-risen
            // enemy as fully present, because a body that could be walked through
            // while it grew is a rule the player learns by being killed by
            // something they took for scenery.
            float scale = type.SpriteScale * Elites.ScaleBonus(elite)
                          * Horde.EmergeScale(pool.Emerge[i]);

            // Planted for drawing only. The pool's Y stays zero — the flow
            // field, the collider and every distance test in the horde are flat,
            // and a body whose simulated position had a height would disagree
            // with all of them.
            Vector3 at = Terrain.Plant(pool.Position[i]);

            Write(_buffers[variant], slot, at, pool.Yaw[i], scale,
                  Pack(pool.Velocity[i].Length(), pool.Stride[i]),
                  HueShift(elite), pool.HitFlash[i], Jitter(pool.Phase[i]));
        }

        for (int variant = 0; variant < _multiMeshes.Length; variant++)
        {
            _multiMeshes[variant].VisibleInstanceCount = _counts[variant];

            // An empty MultiMesh still costs a draw call; hiding it gives that
            // back. With six of them and a horde that is mostly walkers early on,
            // this is usually four of the six.
            _nodes[variant].Visible = _counts[variant] > 0;
            if (_counts[variant] == 0)
                continue;

            // The whole buffer, not a slice of it. `InstanceCount` is the
            // capacity, and the setter expects a buffer for all of them —
            // `VisibleInstanceCount` above is what decides how many are drawn.
            // Handing it a right-sized slice would mean allocating one per
            // variant per frame to save writing floats nothing reads.
            _multiMeshes[variant].Buffer = _buffers[variant];
        }
    }

    /// One body's stride, moved on by how far it walked.
    ///
    /// Shared with `SoloBody` rather than copied into it. A player whose gait ran
    /// at a different rate from the horde's would look wrong in a way nobody
    /// could name — the same walk cycle is what makes them all the same kind of
    /// thing, which is exactly the impression a survivors game wants.
    public static float AdvanceStride(float stride, float speed, float delta) =>
        speed <= 0.001f ? stride : Mathf.PosMod(stride + speed * delta * StridesPerMetre, 1.0f);

    /// Pace in the integer part, phase in the fraction.
    ///
    /// One float because there are four and six things that wanted one. The
    /// quantisation is deliberate and generous: a quarter of a metre per second
    /// is far finer than the eye separates in a swing amplitude, and spending it
    /// buys a phase resolution nothing can perceive the end of.
    /// Public because it is half of a contract whose other half is in a shader,
    /// and the shader cannot be unit-tested. A probe that checks this round-trips
    /// is checking the only part of the packing that can be checked at all.
    public static float Pack(float speed, float stride)
    {
        float units = Mathf.Floor(Mathf.Min(speed / PaceQuantum, MaxPaceUnits));

        // Clamped strictly below 1. A phase of exactly 1.0 would carry into the
        // integer part and make a stationary body sprint for one frame.
        return units + Mathf.Clamp(stride, 0.0f, 0.9999f);
    }

    /// Hue rotation for an elite mark, in turns.
    ///
    /// A rotation rather than a multiply. `Elites.Tint` returns a colour meant to
    /// be multiplied into a sprite's texture, which works on a mostly-grey PNG
    /// and fails on saturated vertex colours — a red tint on the green bloater
    /// gives black, so the most distinctive variant loses its silhouette exactly
    /// when it becomes the most dangerous one.
    private static float HueShift(byte elite) => elite == 0 ? 0.0f : 0.12f + elite * 0.17f;

    /// A small, stable per-body brightness offset in [-1, 1].
    ///
    /// From the spawn phase, which is already random and already per-instance, so
    /// this costs no storage and never changes for a given body. A crowd of
    /// identically lit bodies reads as a texture rather than as a crowd.
    private static float Jitter(float phase) => phase * 2.0f - 1.0f;

    /// One instance: a Y-rotated, uniformly scaled basis, then the origin, then
    /// the four custom floats.
    ///
    /// Written row-major as three rows of four, which is the layout
    /// `MultiMesh.Buffer` expects for Transform3D — the translation is the fourth
    /// column, not a trailing triple. Getting that wrong puts every body at the
    /// origin with a sheared basis, which reads as the mesh being broken rather
    /// than the packing.
    private static void Write(float[] buffer, int slot, Vector3 position, float yaw, float scale,
                              float pacePhase, float hue, float flash, float jitter)
    {
        int at = slot * FloatsPerInstance;

        float c = Mathf.Cos(yaw) * scale;
        float s = Mathf.Sin(yaw) * scale;

        buffer[at + 0] = c;     buffer[at + 1] = 0.0f;  buffer[at + 2] = s;     buffer[at + 3] = position.X;
        buffer[at + 4] = 0.0f;  buffer[at + 5] = scale; buffer[at + 6] = 0.0f;  buffer[at + 7] = position.Y;
        buffer[at + 8] = -s;    buffer[at + 9] = 0.0f;  buffer[at + 10] = c;    buffer[at + 11] = position.Z;

        buffer[at + 12] = pacePhase;
        buffer[at + 13] = hue;
        buffer[at + 14] = flash;
        buffer[at + 15] = jitter;
    }

    /// How many of each variant were written last sync. Only a probe asks — and
    /// it has to, because "the bodies are drawn" and "the bodies are drawn in the
    /// right buckets" looked identical for as long as there was nothing checking.
    public int VisibleCount(int variant) =>
        variant >= 0 && variant < _counts.Length ? _counts[variant] : 0;

    public int VariantCount => _counts.Length;

    /// The mesh a variant draws, for anything measuring what is actually on
    /// screen rather than what the table says should be.
    public Mesh? MeshFor(int variant) =>
        variant >= 0 && variant < _multiMeshes.Length ? _multiMeshes[variant].Mesh : null;
}
