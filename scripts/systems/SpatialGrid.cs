using Godot;

/// Uniform grid over the XZ plane, rebuilt from scratch every tick.
///
/// Rebuilding is a two-pass counting sort — O(n) with no allocation and no
/// hashing — which is cheaper than incrementally maintaining buckets for
/// entities that all move every frame anyway.
///
/// This exists instead of PhysicsServer3D bodies for the horde. Real bodies
/// would pay broadphase insertion plus a solver iteration each, per zombie, to
/// answer a question the game only asks one way: "who is near me". A grid
/// answers exactly that, and separation becomes a single pass instead of a
/// constraint solve. Static level geometry still uses real physics — this
/// replaces enemy-vs-enemy only.
public sealed class SpatialGrid
{
    private readonly int _width;
    private readonly int _depth;
    private readonly float _cellSize;
    private readonly Vector2 _origin;

    private readonly int[] _cellStart;  // length cells + 1
    private readonly int[] _entries;    // length capacity
    private readonly int[] _cursor;     // scratch for the scatter pass

    /// <param name="center">World-space centre of the covered area.</param>
    /// <param name="extent">Half-width of the covered area.</param>
    public SpatialGrid(Vector2 center, float extent, float cellSize, int capacity)
    {
        // Indices are computed from the minimum corner; see FlowField for the
        // same correction. A centre-as-origin grid silently piles the whole
        // negative half of the world into column zero, which reads as enemies
        // separating from strangers.
        _origin = center - Vector2.One * extent;
        _cellSize = cellSize;
        _width = Mathf.CeilToInt(extent * 2.0f / cellSize);
        _depth = _width;

        _cellStart = new int[_width * _depth + 1];
        _cursor = new int[_width * _depth];
        _entries = new int[capacity];
    }

    public void Rebuild(Vector3[] positions, int count)
    {
        System.Array.Clear(_cellStart, 0, _cellStart.Length);

        // Pass 1: histogram. Offset by one so the prefix sum lands directly on
        // each cell's start index without a second shift.
        for (int i = 0; i < count; i++)
            _cellStart[CellOf(positions[i]) + 1]++;

        for (int c = 0; c < _width * _depth; c++)
            _cellStart[c + 1] += _cellStart[c];

        System.Array.Copy(_cellStart, _cursor, _width * _depth);

        // Pass 2: scatter.
        for (int i = 0; i < count; i++)
            _entries[_cursor[CellOf(positions[i])]++] = i;
    }

    /// Fills a caller-owned buffer with every entity in the 3x3 cell block around
    /// a position and returns how many were written. Cell size should be at least
    /// the interaction radius so one ring of neighbours is enough.
    ///
    /// Takes a scratch buffer rather than a callback on purpose — a delegate here
    /// would allocate once per query, several hundred times a tick, which is the
    /// exact cost this whole system exists to avoid.
    public int QueryNear(Vector3 position, int[] result)
    {
        int cx = ClampX(Mathf.FloorToInt((position.X - _origin.X) / _cellSize));
        int cz = ClampZ(Mathf.FloorToInt((position.Z - _origin.Y) / _cellSize));
        int written = 0;

        for (int dz = -1; dz <= 1; dz++)
        {
            int z = cz + dz;
            if (z < 0 || z >= _depth)
                continue;

            for (int dx = -1; dx <= 1; dx++)
            {
                int x = cx + dx;
                if (x < 0 || x >= _width)
                    continue;

                int cell = z * _width + x;
                int end = _cellStart[cell + 1];
                for (int e = _cellStart[cell]; e < end && written < result.Length; e++)
                    result[written++] = _entries[e];
            }
        }

        return written;
    }

    private int CellOf(Vector3 position)
    {
        int x = ClampX(Mathf.FloorToInt((position.X - _origin.X) / _cellSize));
        int z = ClampZ(Mathf.FloorToInt((position.Z - _origin.Y) / _cellSize));
        return z * _width + x;
    }

    private int ClampX(int x) => x < 0 ? 0 : (x >= _width ? _width - 1 : x);
    private int ClampZ(int z) => z < 0 ? 0 : (z >= _depth ? _depth - 1 : z);
}
