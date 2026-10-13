// Placemaker — paint-mode brush mask (cells painted by a round/square brush → one outline).
//
// Ported from Grid Road Generator Core/BrushMask.cs (Ti4goc, MIT). Differences: typed snapshot
// for History instead of an object tuple, outline via Placemaker.Core.Geometry.Poly. Named
// PaintMask so it never clashes with the geometry workstream's Core/Geometry/BrushMask once
// that lands (then this can be retired in favour of it).
//
// Storage is sparse 64×64 tiles of 4 m cells, so a 1000 m brush on a 14 km map stays cheap.

using System;
using System.Collections.Generic;
using Placemaker.Core.Geometry;
using Unity.Mathematics;

namespace Placemaker.Core.Tool
{
    public sealed class PaintMask
    {
        public const float Cell = 4f;
        public const float MinDiameter = 1f;
        public const float MaxDiameter = 1000f;
        private const int TileBits = 6;
        private const int TileSize = 1 << TileBits;
        /// <summary>Longest merged boundary run (cells) — keeps overlay dashes short enough to follow terrain.</summary>
        private const int MaxRun = 12;
        /// <summary>Hard cap on the outline workspace (cells) so a huge stroke cannot stall the main thread.</summary>
        private const int MaxOutlineCells = 4_000_000;

        public sealed class Snapshot
        {
            public Dictionary<long, bool[]> Tiles = new Dictionary<long, bool[]>();
            public int Count;
        }

        private readonly Dictionary<long, bool[]> _tiles = new Dictionary<long, bool[]>();
        private readonly HashSet<long> _edgeCells = new HashSet<long>();
        private readonly List<int2> _changed = new List<int2>();
        private long _lastTileKey = long.MinValue;
        private bool[] _lastTile;
        private int _count;

        public int Version { get; private set; }
        public bool IsEmpty => _count == 0;
        public float Area => _count * Cell * Cell;
        public int CellCount => _count;

        private bool[] Tile(int i, int j, bool create)
        {
            long key = Key(i >> TileBits, j >> TileBits);
            if (key == _lastTileKey) return _lastTile;
            if (!_tiles.TryGetValue(key, out bool[] tile))
            {
                if (!create) return null;
                _tiles[key] = tile = new bool[TileSize * TileSize];
            }
            _lastTileKey = key;
            _lastTile = tile;
            return tile;
        }

        public bool IsPainted(int i, int j)
        {
            bool[] tile = Tile(i, j, false);
            return tile != null && tile[((j & (TileSize - 1)) << TileBits) | (i & (TileSize - 1))];
        }

        public bool IsPaintedAt(float2 world) => IsPainted((int)math.floor(world.x / Cell), (int)math.floor(world.y / Cell));

        private bool Set(int i, int j, bool painted)
        {
            bool[] tile = Tile(i, j, painted);
            if (tile == null) return false;
            int index = ((j & (TileSize - 1)) << TileBits) | (i & (TileSize - 1));
            if (tile[index] == painted) return false;
            tile[index] = painted;
            _count += painted ? 1 : -1;
            return true;
        }

        private static long Key(int i, int j) => ((long)i << 32) ^ (uint)j;
        private static int2 FromKey(long k) => new int2((int)(k >> 32), (int)(uint)k);

        public Snapshot Save()
        {
            var s = new Snapshot { Count = _count };
            foreach (var pair in _tiles)
            {
                // Empty tiles are not worth keeping in history.
                bool any = false;
                foreach (bool b in pair.Value) if (b) { any = true; break; }
                if (any) s.Tiles[pair.Key] = (bool[])pair.Value.Clone();
            }
            return s;
        }

        public void Restore(Snapshot s)
        {
            _tiles.Clear();
            _edgeCells.Clear();
            _count = 0;
            _lastTileKey = long.MinValue;
            _lastTile = null;
            if (s != null)
            {
                foreach (var pair in s.Tiles)
                {
                    var copy = (bool[])pair.Value.Clone();
                    _tiles[pair.Key] = copy;
                    foreach (bool b in copy) if (b) _count++;
                }
                // Recompute boundary cells from scratch: cheaper than storing them and always consistent.
                foreach (var pair in _tiles)
                {
                    int2 origin = FromKey(pair.Key) * TileSize;
                    for (int index = 0; index < pair.Value.Length; index++)
                        if (pair.Value[index]) UpdateEdge(origin.x + (index & (TileSize - 1)), origin.y + (index >> TileBits));
                }
            }
            Version++;
        }

        public void Clear()
        {
            if (_count == 0 && _tiles.Count == 0) return;
            _tiles.Clear();
            _edgeCells.Clear();
            _lastTileKey = long.MinValue;
            _lastTile = null;
            _count = 0;
            Version++;
        }

        /// <summary>Paints (or erases) a round or square brush. Square brushes may be rotated (degrees).</summary>
        public void Stamp(float2 centre, float radius, bool erase, bool square = false, float angleDegrees = 0f)
        {
            radius = math.clamp(radius, 0.5f * Cell, MaxDiameter * 0.5f);
            if (square && math.abs(angleDegrees % 90f) > 0.01f)
            {
                StampRotatedSquare(centre, radius, erase, math.radians(angleDegrees));
                return;
            }
            int2 min = (int2)math.floor((centre - radius) / Cell), max = (int2)math.floor((centre + radius) / Cell);
            _changed.Clear();
            float2 local = centre / Cell - 0.5f;
            float r2 = radius * radius / (Cell * Cell);
            for (int i = min.x; i <= max.x; i++)
            {
                float dx = i - local.x, rest = r2 - dx * dx;
                if (!square && rest < 0f) continue;
                float half = square ? radius / Cell : math.sqrt(rest);
                int j0 = math.max(min.y, (int)math.ceil(local.y - half)), j1 = math.min(max.y, (int)math.floor(local.y + half));
                for (int j = j0; j <= j1; j++)
                    if (Set(i, j, !erase)) _changed.Add(new int2(i, j));
            }
            CommitChanges();
        }

        private void StampRotatedSquare(float2 centre, float radius, bool erase, float angle)
        {
            _changed.Clear();
            float2 axisX = new float2(math.cos(angle), math.sin(angle)), axisZ = new float2(-axisX.y, axisX.x);
            float reach = radius * math.SQRT2;
            int2 min = (int2)math.floor((centre - reach) / Cell), max = (int2)math.floor((centre + reach) / Cell);
            for (int i = min.x; i <= max.x; i++)
                for (int j = min.y; j <= max.y; j++)
                {
                    float2 d = (new float2(i, j) + 0.5f) * Cell - centre;
                    if (math.abs(math.dot(d, axisX)) > radius || math.abs(math.dot(d, axisZ)) > radius) continue;
                    if (Set(i, j, !erase)) _changed.Add(new int2(i, j));
                }
            CommitChanges();
        }

        /// <summary>Stamps along a mouse path so fast strokes leave no gaps.</summary>
        public void Stroke(float2 from, float2 to, float radius, bool erase, bool square = false, float angleDegrees = 0f)
        {
            float length = math.distance(from, to);
            if (length < 0.5f * Cell) { Stamp(to, radius, erase, square, angleDegrees); return; }
            int steps = math.max(1, (int)math.ceil(length / math.max(radius * 0.25f, Cell)));
            for (int k = 1; k <= steps; k++) Stamp(math.lerp(from, to, (float)k / steps), radius, erase, square, angleDegrees);
        }

        private void CommitChanges()
        {
            if (_changed.Count == 0) return;
            Version++;
            foreach (int2 c in _changed)
            {
                UpdateEdge(c.x, c.y);
                UpdateEdge(c.x + 1, c.y);
                UpdateEdge(c.x - 1, c.y);
                UpdateEdge(c.x, c.y + 1);
                UpdateEdge(c.x, c.y - 1);
            }
        }

        private void UpdateEdge(int i, int j)
        {
            bool edge = IsPainted(i, j) && (!IsPainted(i + 1, j) || !IsPainted(i - 1, j) || !IsPainted(i, j + 1) || !IsPainted(i, j - 1));
            if (edge) _edgeCells.Add(Key(i, j)); else _edgeCells.Remove(Key(i, j));
        }

        /// <summary>Cell-boundary segments of the painted region (overlay while painting), merged into short runs.</summary>
        public List<(float2 a, float2 b)> RawBoundary()
        {
            var horizontal = new Dictionary<int, List<int>>();
            var vertical = new Dictionary<int, List<int>>();
            void Add(Dictionary<int, List<int>> lines, int line, int at)
            {
                if (!lines.TryGetValue(line, out var list)) lines[line] = list = new List<int>();
                list.Add(at);
            }
            foreach (long k in _edgeCells)
            {
                int2 c = FromKey(k);
                // line keys: 2*y for "bottom" sides, 2*y+1 for "top" sides so the two never merge.
                if (!IsPainted(c.x, c.y - 1)) Add(horizontal, 2 * c.y, c.x);
                if (!IsPainted(c.x, c.y + 1)) Add(horizontal, 2 * (c.y + 1) + 1, c.x);
                if (!IsPainted(c.x - 1, c.y)) Add(vertical, 2 * c.x, c.y);
                if (!IsPainted(c.x + 1, c.y)) Add(vertical, 2 * (c.x + 1) + 1, c.y);
            }
            var result = new List<(float2, float2)>();
            void Merge(Dictionary<int, List<int>> lines, bool isHorizontal)
            {
                foreach (var pair in lines)
                {
                    int line = pair.Key >> 1;
                    List<int> at = pair.Value;
                    at.Sort();
                    int start = 0;
                    for (int n = 1; n <= at.Count; n++)
                    {
                        if (n < at.Count && at[n] == at[n - 1] + 1 && n - start < MaxRun) continue;
                        float from = at[start] * Cell, to = (at[n - 1] + 1) * Cell, fixedAt = line * Cell;
                        result.Add(isHorizontal ? (new float2(from, fixedAt), new float2(to, fixedAt)) : (new float2(fixedAt, from), new float2(fixedAt, to)));
                        start = n;
                    }
                }
            }
            Merge(horizontal, true);
            Merge(vertical, false);
            return result;
        }

        /// <summary>
        /// Outline of the largest painted island as a simple, smoothed ring (XZ; y = 0). Holes are
        /// filled (CS2 areas cannot have holes). Empty list when nothing usable is painted.
        /// </summary>
        public List<float2> Outline()
        {
            var empty = new List<float2>();
            if (_count == 0) return empty;
            int2 min = new int2(int.MaxValue), max = new int2(int.MinValue);
            var painted = new List<int2>(_count);
            foreach (var pair in _tiles)
            {
                int2 origin = FromKey(pair.Key) * TileSize;
                bool[] tile = pair.Value;
                for (int index = 0; index < tile.Length; index++)
                {
                    if (!tile[index]) continue;
                    var c = origin + new int2(index & (TileSize - 1), index >> TileBits);
                    painted.Add(c);
                    min = math.min(min, c);
                    max = math.max(max, c);
                }
            }
            if (painted.Count == 0) return empty;
            min -= 2;
            max += 2;
            int nx = max.x - min.x + 1, nz = max.y - min.y + 1;
            if ((long)nx * nz > MaxOutlineCells) return empty;
            var grid = new bool[nx * nz];
            foreach (int2 c in painted)
            {
                int2 q = c - min;
                grid[q.y * nx + q.x] = true;
            }

            // Keep the largest 4-connected island only.
            var label = new int[nx * nz];
            int best = 0, bestSize = 0, next = 0;
            var stack = new List<int>();
            for (int s = 0; s < grid.Length; s++)
            {
                if (!grid[s] || label[s] != 0) continue;
                next++;
                int size = 0;
                stack.Add(s);
                label[s] = next;
                while (stack.Count > 0)
                {
                    int q = stack[stack.Count - 1];
                    stack.RemoveAt(stack.Count - 1);
                    size++;
                    int qx = q % nx, qz = q / nx;
                    if (qx > 0) Visit(q - 1);
                    if (qx < nx - 1) Visit(q + 1);
                    if (qz > 0) Visit(q - nx);
                    if (qz < nz - 1) Visit(q + nx);
                }
                if (size > bestSize) { bestSize = size; best = next; }
            }
            void Visit(int n)
            {
                if (grid[n] && label[n] == 0) { label[n] = next; stack.Add(n); }
            }
            for (int s = 0; s < grid.Length; s++) grid[s] = label[s] == best;

            // Diagonal-only contacts make a non-manifold boundary: bridge them.
            for (bool changed = true; changed;)
            {
                changed = false;
                for (int z = 0; z + 1 < nz; z++)
                    for (int x = 0; x + 1 < nx; x++)
                    {
                        int a = z * nx + x, b = a + 1, c = a + nx, d = c + 1;
                        if (grid[a] && grid[d] && !grid[b] && !grid[c]) { grid[b] = true; changed = true; }
                        else if (grid[b] && grid[c] && !grid[a] && !grid[d]) { grid[a] = true; changed = true; }
                    }
            }

            // Fill holes: everything not reachable from the border is inside.
            var outside = new bool[nx * nz];
            stack.Add(0);
            outside[0] = true;
            while (stack.Count > 0)
            {
                int q = stack[stack.Count - 1];
                stack.RemoveAt(stack.Count - 1);
                int qx = q % nx, qz = q / nx;
                if (qx > 0) Out(q - 1);
                if (qx < nx - 1) Out(q + 1);
                if (qz > 0) Out(q - nx);
                if (qz < nz - 1) Out(q + nx);
            }
            void Out(int n)
            {
                if (!grid[n] && !outside[n]) { outside[n] = true; stack.Add(n); }
            }
            for (int s = 0; s < grid.Length; s++) grid[s] = !outside[s];

            bool In(int x, int z) => x >= 0 && z >= 0 && x < nx && z < nz && grid[z * nx + x];
            var outgoing = new Dictionary<long, List<int2>>();
            void AddEdge(int2 a, int2 b)
            {
                long key = Key(a.x, a.y);
                if (!outgoing.TryGetValue(key, out var list)) outgoing[key] = list = new List<int2>();
                list.Add(b);
            }
            int2 start = default;
            bool found = false;
            for (int z = 0; z < nz; z++)
                for (int x = 0; x < nx; x++)
                {
                    if (!In(x, z)) continue;
                    if (!In(x, z - 1)) AddEdge(new int2(x, z), new int2(x + 1, z));
                    if (!In(x + 1, z)) AddEdge(new int2(x + 1, z), new int2(x + 1, z + 1));
                    if (!In(x, z + 1)) AddEdge(new int2(x + 1, z + 1), new int2(x, z + 1));
                    if (!In(x - 1, z)) AddEdge(new int2(x, z + 1), new int2(x, z));
                    if (!found) { found = true; start = new int2(x, z); }
                }
            if (!found) return empty;

            var loop = new List<float2>();
            int2 cur = start, dir = new int2(1, 0);
            for (int guard = 0; guard < 4 * nx * nz; guard++)
            {
                loop.Add(cur);
                if (!outgoing.TryGetValue(Key(cur.x, cur.y), out var options) || options.Count == 0) return empty;
                int2 chosen = options[0];
                if (options.Count > 1)
                {
                    int2 left = new int2(dir.y, -dir.x);
                    foreach (int2 o in options) if (math.all(o - cur == left)) chosen = o;
                }
                options.Remove(chosen);
                dir = chosen - cur;
                cur = chosen;
                if (math.all(cur == start)) break;
            }

            var corners = new List<float2>();
            for (int i = 0; i < loop.Count; i++)
            {
                float2 a = loop[(i + loop.Count - 1) % loop.Count], b = loop[i], c = loop[(i + 1) % loop.Count];
                float2 d1 = b - a, d2 = c - b;
                if (d1.x * d2.y - d1.y * d2.x != 0f) corners.Add((b + (float2)min) * Cell);
            }
            if (corners.Count < 4) return empty;
            List<float2> even = Resample(corners, Cell);
            for (int passes = 6; passes >= 0; passes -= 2)
            {
                List<float2> smooth = SmoothLoop(even, passes, 3);
                List<float2> simple = Simplify(smooth, 0.6f);
                if (simple.Count >= 3 && Poly.IsSimple(simple))
                    return new List<float2>(Poly.EnsureCcw(simple.ToArray()));
            }
            return empty;
        }

        private static List<float2> Resample(List<float2> loop, float step)
        {
            var result = new List<float2>();
            int n = loop.Count;
            float carry = 0f;
            for (int i = 0; i < n; i++)
            {
                float2 a = loop[i], b = loop[(i + 1) % n];
                float length = math.distance(a, b);
                float t = carry;
                while (t < length)
                {
                    result.Add(math.lerp(a, b, t / length));
                    t += step;
                }
                carry = t - length;
            }
            return result;
        }

        private static List<float2> SmoothLoop(List<float2> loop, int passes, int half)
        {
            var result = new List<float2>(loop);
            int n = result.Count;
            for (int pass = 0; pass < passes; pass++)
            {
                var next = new List<float2>(n);
                for (int k = 0; k < n; k++)
                {
                    float2 sum = float2.zero;
                    for (int d = -half; d <= half; d++) sum += result[((k + d) % n + n) % n];
                    next.Add(sum / (2 * half + 1));
                }
                result = next;
            }
            return result;
        }

        // Douglas–Peucker on a closed loop, split at the two farthest-apart points.
        private static List<float2> Simplify(List<float2> loop, float tolerance)
        {
            int n = loop.Count;
            var keep = new bool[n];
            int far = 0;
            for (int i = 1; i < n; i++) if (math.distancesq(loop[i], loop[0]) > math.distancesq(loop[far], loop[0])) far = i;
            keep[0] = keep[far] = true;
            var work = new Stack<int2>();
            work.Push(new int2(0, far));
            work.Push(new int2(far, 0));
            while (work.Count > 0)
            {
                var span = work.Pop();
                int from = span.x, to = span.y;
                int count = (to - from + n) % n;
                if (count < 2) continue;
                float2 a = loop[from], b = loop[to], ab = b - a;
                float lengthSq = math.max(math.lengthsq(ab), 1e-6f);
                int worst = -1;
                float worstDistance = tolerance;
                for (int s = 1; s < count; s++)
                {
                    int i = (from + s) % n;
                    float t = math.saturate(math.dot(loop[i] - a, ab) / lengthSq);
                    float d = math.distance(loop[i], a + t * ab);
                    if (d > worstDistance) { worstDistance = d; worst = i; }
                }
                if (worst < 0) continue;
                keep[worst] = true;
                work.Push(new int2(from, worst));
                work.Push(new int2(worst, to));
            }
            var result = new List<float2>();
            for (int i = 0; i < n; i++) if (keep[i]) result.Add(loop[i]);
            return result;
        }
    }
}
