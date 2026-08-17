using Godot;

/// Burning ground: a handful of circles that damage whatever stands in them
/// until they go out.
///
/// Fixed capacity and no allocation after construction, like every other pool
/// here. There are never many — a player carries two or three molotovs — but a
/// system that allocates per throw allocates during exactly the fight the throw
/// was for.
public sealed class HazardField
{
    public readonly int Capacity;

    public readonly Vector3[] Position;
    public readonly float[] Radius;
    public readonly float[] DamagePerSecond;
    public readonly float[] Remaining;

    public int Count { get; private set; }

    public HazardField(int capacity)
    {
        Capacity = capacity;
        Position = new Vector3[capacity];
        Radius = new float[capacity];
        DamagePerSecond = new float[capacity];
        Remaining = new float[capacity];
    }

    /// Drops the oldest patch when full rather than refusing the throw. An item
    /// the player spent has to do something; silently doing nothing is the one
    /// outcome worth ruling out.
    public void Add(Vector3 position, float radius, float damagePerSecond, float seconds)
    {
        int index = Count < Capacity ? Count++ : OldestIndex();

        Position[index] = position;
        Radius[index] = radius;
        DamagePerSecond[index] = damagePerSecond;
        Remaining[index] = seconds;
    }

    private int OldestIndex()
    {
        int oldest = 0;
        for (int i = 1; i < Count; i++)
        {
            if (Remaining[i] < Remaining[oldest])
                oldest = i;
        }

        return oldest;
    }

    public void Step(float delta)
    {
        for (int i = Count - 1; i >= 0; i--)
        {
            Remaining[i] -= delta;
            if (Remaining[i] > 0.0f)
                continue;

            int last = --Count;
            if (i == last)
                continue;

            Position[i] = Position[last];
            Radius[i] = Radius[last];
            DamagePerSecond[i] = DamagePerSecond[last];
            Remaining[i] = Remaining[last];
        }
    }

    /// Total damage per second at a point, summed over overlapping patches.
    /// Overlap stacking is deliberate: two molotovs on the same doorway should
    /// be worth throwing the second one.
    public float DamageAt(Vector3 point)
    {
        float total = 0.0f;

        for (int i = 0; i < Count; i++)
        {
            float dx = point.X - Position[i].X;
            float dz = point.Z - Position[i].Z;
            if (dx * dx + dz * dz < Radius[i] * Radius[i])
                total += DamagePerSecond[i];
        }

        return total;
    }

    public void Clear() => Count = 0;
}
