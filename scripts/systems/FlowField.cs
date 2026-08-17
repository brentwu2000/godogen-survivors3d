using Godot;

/// Grid flow field: one breadth-first sweep from the player produces a direction
/// for every cell, and every enemy just samples the cell it stands in.
///
/// This replaces per-agent pathfinding on purpose. Two hundred agents each
/// running their own search is the classic way a horde game dies — the cost
/// scales with agent count and spikes whenever the target moves. A flow field's
/// cost scales with map area instead, is completely independent of how many
/// enemies read it, and is amortised further by rebuilding only every few ticks.
public sealed class FlowField
{
    private const int Unreachable = int.MaxValue;

    private readonly int _width;
    private readonly int _depth;
    private readonly float _cellSize;
    private readonly Vector2 _origin;

    private readonly bool[] _blocked;
    private readonly int[] _distance;
    private readonly Vector2[] _flow;
    private readonly int[] _queue;

    /// <param name="center">World-space centre of the covered area.</param>
    /// <param name="extent">Half-width of the covered area.</param>
    public FlowField(Vector2 center, float extent, float cellSize)
    {
        // Cell indices are computed from the grid's minimum corner. Passing the
        // centre straight through would fold the entire negative half of the
        // world onto column zero.
        _origin = center - Vector2.One * extent;
        _cellSize = cellSize;
        _width = Mathf.CeilToInt(extent * 2.0f / cellSize);
        _depth = _width;

        int cells = _width * _depth;
        _blocked = new bool[cells];
        _distance = new int[cells];
        _flow = new Vector2[cells];
        _queue = new int[cells];
    }

    /// Marks an axis-aligned footprint impassable. Called once at build time;
    /// the field itself is rebuilt every few ticks but obstacles are static.
    public void BlockBox(Vector2 center, Vector2 halfExtents)
    {
        int minX = ClampX(Mathf.FloorToInt((center.X - halfExtents.X - _origin.X) / _cellSize));
        int maxX = ClampX(Mathf.CeilToInt((center.X + halfExtents.X - _origin.X) / _cellSize));
        int minZ = ClampZ(Mathf.FloorToInt((center.Y - halfExtents.Y - _origin.Y) / _cellSize));
        int maxZ = ClampZ(Mathf.CeilToInt((center.Y + halfExtents.Y - _origin.Y) / _cellSize));

        for (int z = minZ; z <= maxZ; z++)
        for (int x = minX; x <= maxX; x++)
            _blocked[z * _width + x] = true;
    }

    public void Rebuild(Vector3 target)
    {
        int cells = _width * _depth;
        for (int i = 0; i < cells; i++)
            _distance[i] = Unreachable;

        int start = CellOf(target);
        if (_blocked[start])
            start = NearestOpen(start);
        if (start < 0)
            return;

        int head = 0, tail = 0;
        _distance[start] = 0;
        _queue[tail++] = start;

        // Four-way BFS: the distance field it produces is what gets smoothed into
        // eight-way directions below. Doing the search itself eight-way would
        // let paths cut diagonally through the corner of a blocked cell.
        while (head < tail)
        {
            int cell = _queue[head++];
            int cx = cell % _width;
            int cz = cell / _width;
            int next = _distance[cell] + 1;

            TryVisit(cx - 1, cz, next, ref tail);
            TryVisit(cx + 1, cz, next, ref tail);
            TryVisit(cx, cz - 1, next, ref tail);
            TryVisit(cx, cz + 1, next, ref tail);
        }

        BuildFlow();
    }

    /// Direction to travel from a world position, or Zero where the target is
    /// unreachable — callers treat Zero as "hold position" rather than guessing.
    public Vector2 Sample(Vector3 position) => _flow[CellOf(position)];

    private void TryVisit(int x, int z, int distance, ref int tail)
    {
        if (x < 0 || x >= _width || z < 0 || z >= _depth)
            return;

        int cell = z * _width + x;
        if (_blocked[cell] || _distance[cell] != Unreachable)
            return;

        _distance[cell] = distance;
        _queue[tail++] = cell;
    }

    private void BuildFlow()
    {
        for (int z = 0; z < _depth; z++)
        for (int x = 0; x < _width; x++)
        {
            int cell = z * _width + x;
            if (_blocked[cell] || _distance[cell] == Unreachable)
            {
                _flow[cell] = Vector2.Zero;
                continue;
            }

            int best = _distance[cell];
            var bestDir = Vector2.Zero;

            for (int dz = -1; dz <= 1; dz++)
            for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dz == 0)
                    continue;

                int nx = x + dx, nz = z + dz;
                if (nx < 0 || nx >= _width || nz < 0 || nz >= _depth)
                    continue;

                int neighbour = nz * _width + nx;
                if (_blocked[neighbour] || _distance[neighbour] >= best)
                    continue;

                best = _distance[neighbour];
                bestDir = new Vector2(dx, dz).Normalized();
            }

            _flow[cell] = bestDir;
        }
    }

    /// Spiral outward for a passable cell. Only used when the player is standing
    /// inside an obstacle's footprint, which a fat collider makes possible at the
    /// edges even though the player cannot actually enter it.
    private int NearestOpen(int cell)
    {
        int cx = cell % _width;
        int cz = cell / _width;

        for (int radius = 1; radius < 8; radius++)
        for (int dz = -radius; dz <= radius; dz++)
        for (int dx = -radius; dx <= radius; dx++)
        {
            int x = cx + dx, z = cz + dz;
            if (x < 0 || x >= _width || z < 0 || z >= _depth)
                continue;

            int candidate = z * _width + x;
            if (!_blocked[candidate])
                return candidate;
        }

        return -1;
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
