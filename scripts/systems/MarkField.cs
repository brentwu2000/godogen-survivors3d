using Godot;

/// Which of the drawn marks a stain is. Kept separate from `EffectShape` because
/// these are flat, slow and measured in seconds rather than in frames — nothing
/// about the two lists wants to stay in step.
public enum MarkShape : byte
{
    Splat = 0,
    Scorch = 1,
}

/// What a fight leaves behind on the floor.
///
/// Every piece of combat feedback in this game was gone inside a third of a
/// second. A player could stand on ground they had killed forty things over and
/// read nothing from it — which makes a cleared field and an untouched one the
/// same picture, and quietly removes the only record of where the fighting has
/// already happened.
///
/// One MultiMesh, one material, a fixed pool, oldest-out: the same rule as the
/// puffs, for the same reason. Marks are the cheapest possible persistence — no
/// lights, no projectors, no second pass — and the ceiling is deliberately low
/// enough that a three-hundred-second run cannot pave the arena.
public sealed class MarkField
{
    public readonly int Capacity;

    public readonly Vector3[] Position;

    /// The ground's own normal where the mark landed.
    ///
    /// Not billboarded and not flat. The arena has a metre and a half of relief,
    /// and a horizontal quad on a slope either sinks into the hill or hangs off
    /// it — the same defect that made every flat marker in the game wrong before
    /// `Terrain.Drape` existed. A mark is small enough that one normal is the
    /// whole answer, so it gets a basis rather than a mesh.
    public readonly Vector3[] Normal;

    public readonly float[] Size;
    public readonly float[] Spin;
    public readonly Color[] Tint;
    public readonly float[] Life;
    public readonly float[] MaxLife;
    public readonly byte[] Shape;

    public int Count { get; private set; }

    /// Running total, for a probe. Marks live for several seconds, so unlike the
    /// puffs a probe can usually see them — but a probe that runs a fight and then
    /// asks a question two seconds later should not have to care.
    public int TotalSpawned { get; private set; }

    public MarkField(int capacity)
    {
        Capacity = capacity;
        Position = new Vector3[capacity];
        Normal = new Vector3[capacity];
        Size = new float[capacity];
        Spin = new float[capacity];
        Tint = new Color[capacity];
        Life = new float[capacity];
        MaxLife = new float[capacity];
        Shape = new byte[capacity];
    }

    public void Spawn(Vector3 at, float size, Color tint, float seconds, MarkShape shape, float spin)
    {
        TotalSpawned++;

        int i;
        if (Count < Capacity)
        {
            i = Count++;
        }
        else
        {
            // The oldest, which here means the one closest to fading out anyway.
            i = 0;
            for (int n = 1; n < Count; n++)
            {
                if (Life[n] < Life[i])
                    i = n;
            }
        }

        float floor = Terrain.Height(at.X, at.Z);

        // Six centimetres up. Enough to clear the ground mesh's own tessellation
        // without reading as a sheet hovering over it, and the same order as the
        // hazard decals so a scorch inside a burning patch does not fight it.
        Position[i] = new Vector3(at.X, floor + 0.06f, at.Z);
        Normal[i] = Terrain.Normal(at.X, at.Z);
        Size[i] = size;
        Spin[i] = spin;
        Tint[i] = tint;
        Life[i] = seconds;
        MaxLife[i] = seconds;
        Shape[i] = (byte)shape;
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

    /// 0 at birth, 1 at death.
    public float Age(int index) => 1.0f - Life[index] / Mathf.Max(0.0001f, MaxLife[index]);

    /// Opaque for most of its life, then out.
    ///
    /// A mark that starts fading immediately is a mark that spends its whole
    /// existence half there, which is the same lingering-smudge failure the puffs
    /// had — except that a mark lives for eight seconds rather than a tenth of
    /// one, so the smudge is what the player would actually see. It also grows
    /// for the first fraction of a second, because a stain that arrives at full
    /// size arrives as a texture rather than as something that just happened.
    public float Opacity(int index)
    {
        float age = Age(index);
        return age < 0.72f ? 1.0f : 1.0f - (age - 0.72f) / 0.28f;
    }

    public float DrawnSize(int index)
    {
        float grow = Mathf.Min(1.0f, Age(index) * 8.0f);
        return Size[index] * (0.45f + 0.55f * grow * (2.0f - grow));
    }

    private void DespawnAt(int index)
    {
        int last = --Count;
        if (index == last)
            return;

        Position[index] = Position[last];
        Normal[index] = Normal[last];
        Size[index] = Size[last];
        Spin[index] = Spin[last];
        Tint[index] = Tint[last];
        Life[index] = Life[last];
        MaxLife[index] = MaxLife[last];
        Shape[index] = Shape[last];
    }

    public void Clear() => Count = 0;
}
