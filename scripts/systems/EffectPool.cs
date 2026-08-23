using Godot;

/// Short-lived visual puffs, stored the way everything else in this project is.
///
/// Fixed capacity, structure-of-arrays, swap-remove, nothing allocated after
/// construction. Effects are spawned at exactly the moments the game is busiest —
/// a magazine going into a crowd is a dozen a second — so an effect system that
/// allocates is one that stutters during the only scenes it exists for.
///
/// Oldest-out rather than dropped when full: an explosion arriving during a
/// firefight should be visible, and the thing it would replace is a spark from
/// two frames ago.
public sealed class EffectPool
{
    public readonly int Capacity;

    public readonly Vector3[] Position;

    /// Metres across at birth and at death. Growth is most of what separates a
    /// spark from a blast; without it every effect is the same event at
    /// different colours.
    public readonly float[] StartSize;
    public readonly float[] EndSize;

    public readonly Color[] Tint;
    public readonly float[] Life;
    public readonly float[] MaxLife;

    /// Metres per second, in the ground plane. Debris that drifts reads as having
    /// been thrown by something; debris that sits reads as a decal.
    public readonly Vector2[] Drift;

    public int Count { get; private set; }

    public EffectPool(int capacity)
    {
        Capacity = capacity;
        Position = new Vector3[capacity];
        StartSize = new float[capacity];
        EndSize = new float[capacity];
        Tint = new Color[capacity];
        Life = new float[capacity];
        MaxLife = new float[capacity];
        Drift = new Vector2[capacity];
    }

    /// How many puffs have ever been spawned, and how big they were.
    ///
    /// Counted rather than sampled, because a puff lives for a tenth of a second
    /// and a probe reading `Count` a few ticks later sees whatever happens to
    /// still be alive. What a probe wants to know is what the shot *emitted*,
    /// which is a running total.
    public int TotalSpawned { get; private set; }

    public float TotalStartSize { get; private set; }

    /// Forgets the totals. Only a probe calls this; the game never needs it.
    public void ForgetTotals()
    {
        TotalSpawned = 0;
        TotalStartSize = 0.0f;
    }

    public void Spawn(Vector3 position, float startSize, float endSize, Color tint, float seconds, Vector2 drift)
    {
        TotalSpawned++;
        TotalStartSize += startSize;

        int i;
        if (Count < Capacity)
        {
            i = Count++;
        }
        else
        {
            // The one with the least life left. Replacing the newest would make a
            // burst of effects erase itself.
            i = 0;
            for (int n = 1; n < Count; n++)
            {
                if (Life[n] < Life[i])
                    i = n;
            }
        }

        // Planted here, once, rather than at each of the eight call sites.
        //
        // `position.Y` is kept and added on top: callers pass a *height above the
        // ground* — 0.9 for a hit spark at chest height, 0.15 for a muzzle flash
        // — and every one of them is derived from something the simulation holds
        // flat. The one caller that starts from an already-planted position is
        // `EffectDirector.OnFired`, which flattens first for exactly this reason.
        Position[i] = new Vector3(
            position.X,
            Terrain.Height(position.X, position.Z) + position.Y,
            position.Z);

        StartSize[i] = startSize;
        EndSize[i] = endSize;
        Tint[i] = tint;
        Life[i] = seconds;
        MaxLife[i] = seconds;
        Drift[i] = drift;
    }

    public void Step(float delta)
    {
        for (int i = Count - 1; i >= 0; i--)
        {
            Life[i] -= delta;
            if (Life[i] <= 0.0f)
            {
                DespawnAt(i);
                continue;
            }

            Position[i] = new Vector3(
                Position[i].X + Drift[i].X * delta,
                Position[i].Y,
                Position[i].Z + Drift[i].Y * delta);
        }
    }

    /// 0 at birth, 1 at death.
    public float Age(int index) => 1.0f - Life[index] / Mathf.Max(0.0001f, MaxLife[index]);

    private void DespawnAt(int index)
    {
        if (index < 0 || index >= Count)
            return;

        int last = --Count;
        if (index == last)
            return;

        Position[index] = Position[last];
        StartSize[index] = StartSize[last];
        EndSize[index] = EndSize[last];
        Tint[index] = Tint[last];
        Life[index] = Life[last];
        MaxLife[index] = MaxLife[last];
        Drift[index] = Drift[last];
    }

    public void Clear() => Count = 0;
}
