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

    /// Jumps left to a new target after a hit. Distinct from Pierce: piercing
    /// carries straight on through whoever is in line, bouncing turns to face
    /// somebody else.
    public readonly int[] Bounces;

    /// Metres this shot detonates for where it connects. Zero for everything
    /// that does not, which is almost everything.
    public readonly float[] Blast;

    /// What the shot looks like in flight.
    ///
    /// **Every projectile in the game was the same sprite at the same size.** An
    /// arrow, a rifle round and an explosive bolt were one white streak, so the
    /// half of "which weapon am I holding" that happens between the muzzle and
    /// the target said nothing at all — the muzzle flash, the report and the kick
    /// were made to differ and then the thing actually crossing the screen was
    /// identical.
    ///
    /// Per projectile rather than per weapon because a projectile outlives the
    /// shot: swapping weapons mid-flight would otherwise recolour arrows already
    /// in the air.
    public readonly Color[] Tint;

    public readonly float[] Scale;

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
        Bounces = new int[capacity];
        Blast = new float[capacity];
        Tint = new Color[capacity];
        Scale = new float[capacity];
    }

    /// `tint` defaults to white and `scale` to one, which is what every shot
    /// looked like before there was a choice — so a caller that does not care
    /// keeps exactly the appearance it had.
    public bool TrySpawn(Vector3 position, Vector2 velocity, float damage, float knockback, float life,
                         int pierce, int bounces = 0, float blast = 0.0f,
                         Color tint = default, float scale = 1.0f)
    {
        if (Count >= Capacity)
            return false;

        int i = Count++;
        Tint[i] = tint.A <= 0.0f ? Colors.White : tint;
        Scale[i] = scale;
        Bounces[i] = bounces;
        Position[i] = position;
        Velocity[i] = velocity;
        Damage[i] = damage;
        Knockback[i] = knockback;
        Life[i] = life;
        Pierce[i] = pierce;
        Blast[i] = blast;
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
        Bounces[index] = Bounces[last];
        Tint[index] = Tint[last];
        Scale[index] = Scale[last];
        Blast[index] = Blast[last];
    }

    public void Clear() => Count = 0;
}
