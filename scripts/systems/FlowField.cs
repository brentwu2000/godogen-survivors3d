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

    /// A way out of each blocked cell, in its own channel.
    ///
    /// **Not written into `_flow`.** The first version of this filled the blocked
    /// cells of the route field directly, on the reasoning that a zero there means
    /// nothing useful anyway. It does not: `Horde` reads a zero as "no route" and
    /// runs a deliberate fallback — straight at the player inside
    /// `FieldFallbackRadius`, carry on in the current direction outside it — and
    /// overwriting the zero took that branch away from every enemy at once.
    /// `LandmarkProbe` caught it immediately: a walker that had been going round a
    /// pylon in 1800 ticks stopped dead thirty-three metres out and stayed there.
    ///
    /// So the escape is a second answer to a second question, and only the caller
    /// that asked it gets it.
    private readonly Vector2[] _escape;

    /// Which blocked cells `BuildEscapes` has already given a way out. Kept as a
    /// field rather than allocated per rebuild — the field is rebuilt every few
    /// ticks for the whole run, and this is one array the size of the arena.
    private readonly bool[] _escapeSeen;

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
        _escape = new Vector2[cells];
        _escapeSeen = new bool[cells];
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

    /// Forgets every marked obstacle. Only a level that regenerates needs this;
    /// within a run the footprints never move.
    public void ClearBlocked() => System.Array.Clear(_blocked, 0, _blocked.Length);

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
    ///
    /// Unchanged by the escape channel, deliberately. `Horde` depends on the zero:
    /// it means "no route" and selects a fallback the horde has been tuned around.
    /// A caller that wants to be told how to get out of a wall asks `EscapeFrom`.
    public Vector2 Sample(Vector3 position) => _flow[CellOf(position)];

    /// The way out of an obstacle's inflated footprint, or Zero when the cell is
    /// already open.
    ///
    /// Obstacles are inflated by a body radius before they are marked, so the
    /// blocked band reaches about a metre past the collider anything can actually
    /// touch — and standing in that band is the ordinary result of walking up to a
    /// wall. `Sample` returns zero there, every caller reads zero as "no route",
    /// and `AutoPlay` substitutes the straight line to its target: pressed against
    /// the south face of an eight-metre wall with the pad on the north side, that
    /// line points *into the wall*. The bot leaned on it for sixty seconds, seven
    /// and a half metres from the extraction, and the sweep recorded the run as
    /// having no result at all.
    public Vector2 EscapeFrom(Vector3 position) => _escape[CellOf(position)];

    /// True where the cell containing `position` is inside an obstacle's inflated
    /// footprint. Only a probe asks.
    public bool IsBlockedAt(Vector3 position) => _blocked[CellOf(position)];

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

        BuildEscapes();
    }

    /// Fills `_escape` with a direction out of every blocked cell.
    ///
    /// **Without this a body inside an obstacle's footprint has no advice at all.**
    /// Obstacles are inflated by a body radius before they are marked, so the band
    /// of blocked cells extends about a metre past the collider the player can
    /// actually touch — and standing in that band is the ordinary result of
    /// walking up to a wall. `BuildFlow` leaves those cells at `Vector2.Zero`,
    /// every caller reads zero as "no route", and `AutoPlay` in particular
    /// substitutes the straight line to its target. Pressed against the south face
    /// of an eight-metre wall with the pad on the north side, that straight line
    /// points *into the wall*, and the bot leans on it until the leg times out.
    /// It did, at seven and a half metres from the extraction, for sixty seconds.
    ///
    /// A multi-source breadth-first search outward from the open cells, so the
    /// cost is one pass over the blocked cells rather than a spiral search from
    /// each of them. The direction stored is the one that steps toward whichever
    /// open cell is nearest, which is the shortest way back out of the band.
    ///
    /// Only *blocked* cells are filled. A cell that is open but unreachable keeps
    /// its zero, because there the answer really is "no route" and a caller that
    /// started guessing would walk into the sea.
    private void BuildEscapes()
    {
        int cells = _width * _depth;
        for (int i = 0; i < cells; i++)
        {
            _escapeSeen[i] = false;
            _escape[i] = Vector2.Zero;
        }

        int head = 0, tail = 0;

        // Seed: every blocked cell touching an open one, pointing at it.
        for (int z = 0; z < _depth; z++)
        for (int x = 0; x < _width; x++)
        {
            int cell = z * _width + x;
            if (!_blocked[cell])
                continue;

            for (int dz = -1; dz <= 1; dz++)
            for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dz == 0)
                    continue;

                int nx = x + dx, nz = z + dz;
                if (nx < 0 || nx >= _width || nz < 0 || nz >= _depth)
                    continue;

                if (_blocked[nz * _width + nx] || _escapeSeen[cell])
                    continue;

                _escape[cell] = new Vector2(dx, dz).Normalized();
                _escapeSeen[cell] = true;
                _queue[tail++] = cell;
            }
        }

        // Spread inward: a blocked cell with no open neighbour takes the
        // direction of the blocked neighbour that found the way out first, which
        // is the one nearest an edge.
        while (head < tail)
        {
            int cell = _queue[head++];
            int cx = cell % _width;
            int cz = cell / _width;
            Vector2 outward = _escape[cell];

            for (int dz = -1; dz <= 1; dz++)
            for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dz == 0)
                    continue;

                int nx = cx + dx, nz = cz + dz;
                if (nx < 0 || nx >= _width || nz < 0 || nz >= _depth)
                    continue;

                int neighbour = nz * _width + nx;
                if (!_blocked[neighbour] || _escapeSeen[neighbour])
                    continue;

                _escape[neighbour] = outward;
                _escapeSeen[neighbour] = true;
                _queue[tail++] = neighbour;
            }
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

    /// The nearest walkable point to somewhere that may not be, or the position
    /// itself if it already is.
    ///
    /// **A destination inside an inflated footprint has no route to it at all.**
    /// `Rebuild` seeds its search from the target's cell, so a target the margin
    /// has swallowed starts the flood from a blocked cell and the field comes back
    /// with nothing usable — every cell unreachable, and the caller left holding a
    /// direction that means nothing.
    ///
    /// That is not a hypothetical. One of the twelve layouts the balance sweep
    /// runs on put a crate 5.6 m from where the bot gave up, with the crate's own
    /// cell inside a margin: the run came back `Stuck` in **every arm, every
    /// weapon and every loadout**, so every median that file has ever printed was
    /// computed over eleven layouts and nothing said so.
    ///
    /// The margin is the point rather than the bug — it is 0.55 m of body radius
    /// so the route is one a body can walk — and a thing standing against cover is
    /// an ordinary thing for a generator to place. So the answer is not a thinner
    /// margin, which would put the bot back on the corners it was widened to
    /// clear; it is to walk to the closest place that *is* walkable and let the
    /// crate's own 1.8 m reach cover the rest.
    ///
    /// A ring search outward, so the first hit is the nearest. Returns the
    /// original position when nothing within `maxRadius` is open, because a caller
    /// with no walkable ground anywhere near its target has a generator problem
    /// and should say so rather than silently walk somewhere else.
    public Vector3 NearestOpen(Vector3 position, float maxRadius)
    {
        if (!_blocked[CellOf(position)])
            return position;

        int rings = Mathf.Max(1, Mathf.CeilToInt(maxRadius / _cellSize));
        int cx = ClampX(Mathf.FloorToInt((position.X - _origin.X) / _cellSize));
        int cz = ClampZ(Mathf.FloorToInt((position.Z - _origin.Y) / _cellSize));

        for (int r = 1; r <= rings; r++)
        {
            float best = float.MaxValue;
            Vector3 found = position;

            for (int dz = -r; dz <= r; dz++)
            {
                for (int dx = -r; dx <= r; dx++)
                {
                    // The ring only, not the filled square — the inside was
                    // covered by a smaller r and re-checking it would return a
                    // cell further away than one already rejected.
                    if (Mathf.Abs(dx) != r && Mathf.Abs(dz) != r)
                        continue;

                    int x = cx + dx, z = cz + dz;
                    if (x < 0 || x >= _width || z < 0 || z >= _depth)
                        continue;

                    if (_blocked[z * _width + x])
                        continue;

                    // The point in this cell closest to what was wanted, not the
                    // cell's centre.
                    //
                    // A centre is up to a cell's diagonal further away, and at
                    // 1.5 m cells that is the whole margin between a crate's
                    // 1.8 m reach and standing outside it. The bot stopped
                    // getting stuck and started standing 2 m from a crate it
                    // could not search, for a hundred seconds, until something
                    // killed it — which is the same run failing one step later.
                    //
                    // Inset by a tenth of a cell so the answer is inside the open
                    // cell rather than on the boundary it shares with the blocked
                    // one.
                    float inset = _cellSize * 0.1f;
                    float minX = _origin.X + x * _cellSize + inset;
                    float maxX = _origin.X + (x + 1) * _cellSize - inset;
                    float minZ = _origin.Y + z * _cellSize + inset;
                    float maxZ = _origin.Y + (z + 1) * _cellSize - inset;

                    var at = new Vector3(
                        Mathf.Clamp(position.X, minX, maxX), position.Y,
                        Mathf.Clamp(position.Z, minZ, maxZ));

                    float distance = at.DistanceSquaredTo(position);
                    if (distance < best)
                    {
                        best = distance;
                        found = at;
                    }
                }
            }

            if (best < float.MaxValue)
                return found;
        }

        return position;
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
