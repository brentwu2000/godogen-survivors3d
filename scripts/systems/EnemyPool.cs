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

    /// Index into the horde's variant table. A byte rather than a reference:
    /// this array is walked every tick, and the whole point of the layout is
    /// that a pass over it stays inside cache.
    public readonly byte[] Type;

    /// Seconds until a ranged variant may shoot again. Unused by chasers, which
    /// is cheaper than a second pool for the handful that do shoot.
    public readonly float[] AttackCooldown;

    public int Count { get; private set; }

    public EnemyPool(int capacity)
    {
        Capacity = capacity;
        Position = new Vector3[capacity];
        Velocity = new Vector2[capacity];
        Health = new float[capacity];
        Phase = new float[capacity];
        Type = new byte[capacity];
        AttackCooldown = new float[capacity];
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
        Type[i] = type;
        AttackCooldown[i] = 0.0f;
        return true;
    }

    /// Swap-remove. Invalidates the index of whichever enemy was last, so callers
    /// iterating downward are the ones that stay correct.
    public void DespawnAt(int index)
    {
        int last = --Count;
        if (index != last)
        {
            Position[index] = Position[last];
            Velocity[index] = Velocity[last];
            Health[index] = Health[last];
            Phase[index] = Phase[last];
            Type[index] = Type[last];
            AttackCooldown[index] = AttackCooldown[last];
        }
    }

    public void Clear() => Count = 0;
}
