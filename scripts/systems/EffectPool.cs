using Godot;

/// The three things a puff can be drawn as, which is the axis the pool never had.
///
/// One texture drew everything, so a muzzle flash, a blood burst and a plume of
/// smoke differed only in size and colour — and a soft radial ball at any size
/// and any colour reads as a soft radial ball. `Flash` has a hot core and four
/// spikes, which is the shape that says "a gun went off" before the player has
/// read anything; `Smoke` is lumpy and edge-soft with no core, which is the shape
/// that says "this is not light".
public enum EffectShape : byte
{
    Ball = 0,
    Flash = 1,
    Smoke = 2,
}

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

    /// Which of the three drawn shapes this is — see `EffectShape`. Size, colour
    /// and lifetime were the only things separating a muzzle flash from a lump of
    /// smoke, and they are all magnitudes: the same soft ball at every size is
    /// still the same soft ball, which is why a busy frame read as lens dirt
    /// rather than as gunfire.
    public readonly byte[] Shape;

    /// True to draw on the blending channel instead of the additive one.
    ///
    /// Additive cannot make anything darker than what is behind it, so smoke over
    /// a daylit field lightened it. Two MultiMeshes rather than two pools: which
    /// channel a puff belongs on is a property of the puff, and splitting it at
    /// upload keeps one lifetime, one capacity and one spawn path.
    public readonly bool[] Soft;

    /// In-plane rotation, radians. A four-pointed flash drawn at the same angle
    /// every shot reads as a decal stuck to the muzzle.
    public readonly float[] Spin;

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
        Shape = new byte[capacity];
        Soft = new bool[capacity];
        Spin = new float[capacity];
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

    /// `shape`, `soft` and `spin` default to the soft additive ball drawn
    /// upright, which is what every puff in the game looked like before there was
    /// a choice — so a caller that does not care keeps exactly what it had.
    public void Spawn(Vector3 position, float startSize, float endSize, Color tint, float seconds,
                      Vector2 drift, EffectShape shape = EffectShape.Ball, bool soft = false,
                      float spin = 0.0f)
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
        Shape[i] = (byte)shape;
        Soft[i] = soft;
        Spin[i] = spin;
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
        Shape[index] = Shape[last];
        Soft[index] = Soft[last];
        Spin[index] = Spin[last];
    }

    public void Clear() => Count = 0;
}
