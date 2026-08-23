using Godot;

/// Fixed-capacity enemy storage, structure-of-arrays.
///
/// Nothing here allocates after construction. That is the entire point: a horde
/// game that instantiates a node per zombie spends more time in the allocator
/// and the scene tree than in gameplay, and the cost shows up as frame spikes
/// exactly when the wave gets interesting.
///
/// Slots are kept dense with swap-remove, so iteration is contiguous and there
/// is no free list to walk. The cost is that an index is only valid within a
/// single tick — anything that needs to remember an enemy across ticks must
/// re-query by position rather than hold an index.
public sealed class EnemyPool
{
    public readonly int Capacity;

    public readonly Vector3[] Position;
    public readonly Vector2[] Velocity;
    public readonly float[] Health;

    /// Per-instance animation offset, so a crowd does not bob in lockstep.
    public readonly float[] Phase;

    /// Where in its stride this body is, in turns, wrapped to [0, 1).
    ///
    /// Advanced by distance walked, and stored rather than computed — which is
    /// the whole point. Deriving a stride phase from world position is the
    /// obvious shortcut and it is wrong in three ways at once: a crowd standing
    /// on the same spot stands in identical poses, a body knocked backwards walks
    /// backwards, and anything held still by contact freezes mid-step with one
    /// foot in the air instead of standing.
    ///
    /// Seeded from `Phase` on spawn so two enemies that walk the same distance
    /// are still out of step with each other.
    public readonly float[] Stride;

    /// How far out of the ground this one is, 0 to 1.
    ///
    /// Purely cosmetic. The flow field, the collider and the damage all treat a
    /// half-risen enemy as fully present — a body that could be walked through
    /// while it grew would be a rule the player has to learn from being killed by
    /// something they thought was scenery.
    public readonly float[] Emerge;

    /// Which way the body is facing, in radians about +Y.
    ///
    /// Kept here rather than recomputed from velocity every frame because
    /// velocity is zeroed on contact — a body that reached the player would
    /// otherwise snap to face north at the moment it started biting. It only
    /// turns while there is a direction to turn toward, and holds it otherwise.
    public readonly float[] Yaw;

    /// Index into the horde's variant table. A byte rather than a reference:
    /// this array is walked every tick, and the whole point of the layout is
    /// that a pass over it stays inside cache.
    public readonly byte[] Type;

    /// Seconds until a ranged variant may shoot again. Unused by chasers, which
    /// is cheaper than a second pool for the handful that do shoot.
    public readonly float[] AttackCooldown;

    /// How brightly this enemy is still lit from being hit, 1 down to 0.
    ///
    /// Purely cosmetic, and the only confirmation the player gets that a shot
    /// connected with something that did not die. Without it a rifle emptying
    /// into a brute is indistinguishable from a rifle missing it — the brute
    /// keeps walking either way, and sixty rounds later the player has learned
    /// nothing about whether the weapon works.
    public readonly float[] HitFlash;

    /// Damage per second still to be applied, and how long it has left. A knife
    /// that bleeds rewards touching many things once rather than one thing many
    /// times, and that only works if the wound outlives the swing.
    public readonly float[] Bleed;
    public readonly float[] BleedRemaining;

    /// Which elite modifier this one carries, or None. A byte on the same
    /// structure-of-arrays as everything else rather than a subclass: an elite is
    /// an ordinary enemy with one rule bent, and a hundred of them must cost what
    /// a hundred ordinary ones cost.
    public readonly byte[] Elite;

    public int Count { get; private set; }

    public EnemyPool(int capacity)
    {
        Capacity = capacity;
        Position = new Vector3[capacity];
        Velocity = new Vector2[capacity];
        Health = new float[capacity];
        Phase = new float[capacity];
        Stride = new float[capacity];
        Yaw = new float[capacity];
        Emerge = new float[capacity];
        Type = new byte[capacity];
        AttackCooldown = new float[capacity];
        HitFlash = new float[capacity];
        Bleed = new float[capacity];
        BleedRemaining = new float[capacity];
        Elite = new byte[capacity];
    }

    public bool TrySpawn(Vector3 position, byte type, float health, float phase)
    {
        if (Count >= Capacity)
            return false;

        int i = Count++;
        Position[i] = position;
        Velocity[i] = Vector2.Zero;
        Health[i] = health;
        Phase[i] = phase;
        Stride[i] = phase;
        Yaw[i] = 0.0f;
        Emerge[i] = 0.0f;
        Type[i] = type;
        AttackCooldown[i] = 0.0f;
        HitFlash[i] = 0.0f;
        Bleed[i] = 0.0f;
        BleedRemaining[i] = 0.0f;
        Elite[i] = 0;
        return true;
    }

    /// Swap-remove. Invalidates the index of whichever enemy was last, so callers
    /// iterating downward are the ones that stay correct.
    public void DespawnAt(int index)
    {
        // Refusing rather than trusting. The failure this prevents is not a
        // crash at the call site — it is Count drifting below zero and the crash
        // landing on the next spawn, in a different system, with nothing in the
        // stack trace pointing at whoever despawned a slot that was not live.
        if (index < 0 || index >= Count)
            return;

        int last = --Count;
        if (index != last)
        {
            Position[index] = Position[last];
            Velocity[index] = Velocity[last];
            Health[index] = Health[last];
            Phase[index] = Phase[last];
            Stride[index] = Stride[last];
            Yaw[index] = Yaw[last];
            Emerge[index] = Emerge[last];
            Type[index] = Type[last];
            AttackCooldown[index] = AttackCooldown[last];
            HitFlash[index] = HitFlash[last];
            Bleed[index] = Bleed[last];
            BleedRemaining[index] = BleedRemaining[last];
            Elite[index] = Elite[last];
        }
    }

    public void Clear() => Count = 0;
}
