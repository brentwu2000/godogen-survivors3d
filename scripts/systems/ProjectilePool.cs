using Godot;

/// Arrows and bolts in flight. Same shape as EnemyPool for the same reason:
/// dense arrays, swap-remove, no allocation once constructed.
public sealed class ProjectilePool
{
    public readonly int Capacity;

    public readonly Vector3[] Position;
    public readonly Vector2[] Velocity;
    public readonly float[] Damage;
    public readonly float[] Knockback;

    /// Seconds left before the shot expires. Range is enforced by lifetime
    /// rather than distance so a faster arrow really does reach further.
    public readonly float[] Life;

    /// Enemies this shot can still pass through, including the next one.
    public readonly int[] Pierce;

    public int Count { get; private set; }

    public ProjectilePool(int capacity)
    {
        Capacity = capacity;
        Position = new Vector3[capacity];
        Velocity = new Vector2[capacity];
        Damage = new float[capacity];
        Knockback = new float[capacity];
        Life = new float[capacity];
        Pierce = new int[capacity];
    }

    public bool TrySpawn(Vector3 position, Vector2 velocity, float damage, float knockback, float life, int pierce)
    {
        if (Count >= Capacity)
            return false;

        int i = Count++;
        Position[i] = position;
        Velocity[i] = velocity;
        Damage[i] = damage;
        Knockback[i] = knockback;
        Life[i] = life;
        Pierce[i] = pierce;
        return true;
    }

    public void DespawnAt(int index)
    {
        int last = --Count;
        if (index == last)
            return;

        Position[index] = Position[last];
        Velocity[index] = Velocity[last];
        Damage[index] = Damage[last];
        Knockback[index] = Knockback[last];
        Life[index] = Life[last];
        Pierce[index] = Pierce[last];
    }

    public void Clear() => Count = 0;
}
