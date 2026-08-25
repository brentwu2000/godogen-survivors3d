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
            ArrayMesh mesh = MeshFor(types[i]);
            var material = new ShaderMaterial { Shader = shader };
            material.SetShaderParameter("surface_detail",
                GD.Load<Texture2D>(DetailTextureFor(types[i].TypeName)));
            mesh.SurfaceSetMaterial(0, material);

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

    /// A body category owns a painted surface, not merely a tint. Fast infected
    /// read as torn skin and cloth; the heavy roster reads as layered hide and
    /// calcified armour. Keeping this on the material preserves one draw call per
    /// variant and avoids spending either animation UV channel on texture data.
    private static string DetailTextureFor(string typeName) => typeName switch
    {
        "brute" or "bloater" or "bulwark" or "boss" or "lantern" =>
            "res://assets/textures/mutant_handpainted.png",
        _ => "res://assets/textures/infected_handpainted.png",
    };

    /// The mesh for one variant: a baked body if it names one, procedural if not.
    ///
    /// Both arrive carrying the rig in the same two UV channels, so nothing
    /// downstream can tell them apart — which is what makes `BakeBody` worth
    /// having rather than a second renderer.
    ///
    /// The height check is not ceremony. `BodyProbe` asserts every variant stands
    /// at its designed height because the enemy table is what the game balances
    /// against, and a bake made against a different number is a variant that hits
    /// for a brute's damage at a walker's size. Caught here rather than there,
    /// because here it can still fall back to something correct.
    /// Everything that scales one drawn body, in one place.
    ///
    /// Factored out so `EnemyTypeProbe` can ask what an ordinary enemy is drawn
    /// at without standing up a horde — and so that putting `SpriteScale` back in
    /// here fails a probe instead of silently doubling every variant whose art
    /// does not fill its frame. See the note at the call site.
    public static float InstanceScale(byte elite, float emerge) =>
        Elites.ScaleBonus(elite) * Horde.EmergeScale(emerge);

    /// How tall a variant is actually drawn, at rest and un-elite.
    ///
    /// The mesh's own bounds times whatever scales it, which is the number the
    /// player sees and the number nothing was checking. It should equal
    /// `DesignHeightMeters` exactly — that is what the field means.
    public static float DrawnHeight(EnemyTypeResource type) =>
        MeshFor(type).GetAabb().Size.Y * InstanceScale(0, 1.0f);

    private static ArrayMesh MeshFor(EnemyTypeResource type)
    {
        ArrayMesh Procedural() => BodyMeshLibrary.Build3D(
            BodyMeshLibrary.ForVariant(type.TypeName, type.DesignHeightMeters));

        if (string.IsNullOrEmpty(type.BakedBodyPath))
            return Procedural();

        var baked = ResourceLoader.Load<BakedBodyResource>(type.BakedBodyPath);
        if (baked == null)
        {
            GD.PushWarning($"BodyRenderer: {type.TypeName} names {type.BakedBodyPath} and it did "
                         + "not load — drawing it procedurally");
            return Procedural();
        }

        if (Mathf.Abs(baked.StandingHeight - type.DesignHeightMeters) > 0.05f)
        {
            GD.PushWarning($"BodyRenderer: {type.TypeName} is designed at "
                         + $"{type.DesignHeightMeters:F2} m and its bake stands at "
                         + $"{baked.StandingHeight:F2} m — drawing it procedurally. Re-bake with "
                         + "the design height.");
            return Procedural();
        }

        ArrayMesh? mesh = BakedBody.Build(baked);
        if (mesh != null)
            return mesh;

        GD.PushWarning($"BodyRenderer: {type.TypeName}'s bake did not rebuild — "
                     + "drawing it procedurally");

        return Procedural();
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
            // **No `SpriteScale` here, and its absence is the correction.**
            //
            // `SpriteScale` exists to cancel a *sprite's fill fraction*: the
            // billboard quad is one size for every layer, so a brute painting
            // that fills 71.5% of its frame needs 2.098 to come out three metres
            // tall. A mesh has no fill fraction. `MeshFor` builds it at
            // `DesignHeightMeters` and a bake is refused unless it stands at that
            // height — so the body is already the right size before anything
            // multiplies it.
            //
            // Multiplying anyway meant every variant whose art did not fill its
            // frame was drawn at the wrong size on the solid path, which is the
            // path the game actually ships: the brute at 6.3 m instead of 3.0,
            // the bloater at 3.8, and the boss at **seventeen metres** instead of
            // five and a half. It never looked like a bug because a boss is
            // supposed to be enormous and there was nothing on screen to measure
            // it against — the walker and the spitter, the two the eye calibrates
            // on, both fill their frames and have a scale of 1.0.
            //
            // `EnemyTypeProbe` measured the sprite path and reported 3.00 m for
            // the brute, correctly, for as long as this was wrong.
            float scale = InstanceScale(elite, pool.Emerge[i]);

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

    /// How hard the elite mark is pushed, 0 for none.
    ///
    /// A rotation rather than a multiply. `Elites.Tint` returns a colour meant to
    /// be multiplied into a sprite's texture, which works on a mostly-grey PNG
    /// and fails on saturated vertex colours — a red tint on the green bloater
    /// gives black, so the most distinctive variant loses its silhouette exactly
    /// when it becomes the most dangerous one.
    ///
    /// **The scale is much smaller than it was.** This returned 0.29 to 0.63
    /// turns and the shader spent all of it on hue, which on a muted palette
    /// produced lavender and mint — the most dangerous bodies in a crowd came out
    /// as the prettiest. The shader now reads this as a *strength*, spending a
    /// twelfth of a turn on hue and the rest on saturating and darkening, so an
    /// elite is the same creature deeper and dirtier rather than a different
    /// colour. See `body.gdshader`.
    ///
    /// Still one number per kind, and still separated enough that three kinds are
    /// three marks rather than a gradient.
    private static float HueShift(byte elite) => elite == 0 ? 0.0f : 0.45f + elite * 0.28f;

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
