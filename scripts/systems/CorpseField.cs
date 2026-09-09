using Godot;

/// The bodies that are still on the ground after the fight moved on.
///
/// **A kill used to be a body ceasing to exist.** Everything else about it was
/// answered — a spray, a dark cloud, a stain on the floor, a sound — and the one
/// object the player was actually looking at vanished between two frames. That is
/// the single loudest artificial thing left in a fight: fifty of them a minute,
/// each one a hole in the picture at the exact moment the player's attention is
/// on it.
///
/// This is not a ragdoll and deliberately not. The horde is a MultiMesh — one
/// mesh, N transforms, no skeleton and no per-instance geometry — so what a body
/// can do after it dies is exactly what a transform can express. It can fall
/// over. Pivoting about the feet, accelerating like something that has stopped
/// holding itself up, and then lying there is most of what a death looks like
/// from ten metres, and it costs one more instance in a buffer that has three
/// hundred spare.
///
/// Ends by sinking rather than by fading, because an opaque instance has no alpha
/// to fade: the body is translated down through the floor over the last stretch
/// of its life and the ground closes over it. That reads as the arena taking it
/// back, which is a stylised answer rather than a workaround, and it is the only
/// one available to a renderer with no blending.
public sealed class CorpseField
{
    public readonly int Capacity;

    public readonly byte[] Variant;
    public readonly byte[] Elite;
    public readonly Vector3[] Position;

    /// The way the body was facing when it died.
    public readonly float[] Yaw;

    /// Which way it goes down, as an angle in the ground plane. Taken from the
    /// shove that killed it, so a body knocked back falls away from the shot and
    /// a body that bled out drops where it stood.
    public readonly float[] FallYaw;

    public readonly float[] Scale;
    public readonly float[] Life;
    public readonly float[] MaxLife;

    /// Per-body brightness jitter, carried over from the living instance so a
    /// corpse is the same shade as the thing that was standing there a frame ago.
    public readonly float[] Phase;

    public int Count { get; private set; }

    public int TotalSpawned { get; private set; }

    public CorpseField(int capacity)
    {
        Capacity = capacity;
        Variant = new byte[capacity];
        Elite = new byte[capacity];
        Position = new Vector3[capacity];
        Yaw = new float[capacity];
        FallYaw = new float[capacity];
        Scale = new float[capacity];
        Life = new float[capacity];
        MaxLife = new float[capacity];
        Phase = new float[capacity];
    }

    /// How long a body takes to go down, how long it lies there, and how long the
    /// ground takes to close over it.
    ///
    /// The fall is fast because a body that folds slowly reads as a puppet being
    /// lowered. The rest is the number that decides what a cleared area looks
    /// like: too short and the corpse is another flash, too long and forty of them
    /// carpet the spawn. Five seconds is about how long the player stays in one
    /// place before moving on to the next crate.
    public const float FallSeconds = 0.38f;
    public const float SinkSeconds = 0.8f;
    public const float RestSeconds = 4.2f;

    public void Spawn(int variant, byte elite, Vector3 at, float yaw, float fallYaw,
                      float scale, float phase)
    {
        TotalSpawned++;

        int i;
        if (Count < Capacity)
        {
            i = Count++;
        }
        else
        {
            // The one closest to sinking anyway. Replacing the newest would make a
            // blast erase the six bodies it just made.
            i = 0;
            for (int n = 1; n < Count; n++)
            {
                if (Life[n] < Life[i])
                    i = n;
            }
        }

        Variant[i] = (byte)variant;
        Elite[i] = elite;
        Position[i] = at;
        Yaw[i] = yaw;
        FallYaw[i] = fallYaw;
        Scale[i] = scale;
        Life[i] = FallSeconds + RestSeconds + SinkSeconds;
        MaxLife[i] = Life[i];
        Phase[i] = phase;
    }

    public void Step(float delta)
    {
        for (int i = Count - 1; i >= 0; i--)
        {
            Life[i] -= delta;
            if (Life[i] <= 0.0f)
                DespawnAt(i);
        }
    }

    /// How far over the body is, in radians. Zero standing, a right angle flat.
    ///
    /// Eased on a square, which is the shape of something falling rather than
    /// something being rotated: slow off the vertical, quick into the ground.
    public float Tilt(int index)
    {
        float fallen = MaxLife[index] - Life[index];
        float t = Mathf.Clamp(fallen / FallSeconds, 0.0f, 1.0f);
        return Mathf.Pi * 0.5f * t * t;
    }

    /// Metres the body has gone into the ground. Zero until the last stretch.
    public float Sunk(int index) =>
        Life[index] >= SinkSeconds
            ? 0.0f
            : (1.0f - Life[index] / SinkSeconds);

    private void DespawnAt(int index)
    {
        int last = --Count;
        if (index == last)
            return;

        Variant[index] = Variant[last];
        Elite[index] = Elite[last];
        Position[index] = Position[last];
        Yaw[index] = Yaw[last];
        FallYaw[index] = FallYaw[last];
        Scale[index] = Scale[last];
        Life[index] = Life[last];
        MaxLife[index] = MaxLife[last];
        Phase[index] = Phase[last];
    }

    public void Clear() => Count = 0;
}
