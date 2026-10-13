// Placemaker — planar road-graph algorithms for block picking and perimeter selection.
//
// Ideas from Grid Road Generator Core/NetworkGraph.cs (Ti4goc, MIT): Dijkstra fill between
// clicked nodes and face tracing by always taking the sharpest turn. Differences that matter for
// the player: (1) tracing starts from the half-edge on the CURSOR's side of the nearest road, so
// hovering inside any block of an X junction picks exactly that block (GRG's double-click could
// not choose); (2) links carry the curve tangent at the node, not the straight chord, so curved
// roads turn correctly; (3) parallel edges between the same two nodes are distinguished by edge
// id; (4) dead-end spurs inside a block are walked around and then removed from the loop.
//
// Ids are opaque longs (the game packs Entity index/version into them).

using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace Placemaker.Core.Tool
{
    public struct RoadLink
    {
        public long Edge;
        public long Other;
        /// <summary>Unit XZ direction in which the edge LEAVES the node (curve tangent at that end).</summary>
        public float2 Direction;
        public float Length;
    }

    public interface IRoadGraph
    {
        /// <summary>Edges incident to a node (only edges that may bound a block). Never null.</summary>
        IReadOnlyList<RoadLink> Links(long node);
    }

    public struct HalfEdge : IEquatable<HalfEdge>
    {
        public long Edge, From, To;
        public HalfEdge(long edge, long from, long to) { Edge = edge; From = from; To = to; }
        public HalfEdge Reversed => new HalfEdge(Edge, To, From);
        public bool Equals(HalfEdge o) => Edge == o.Edge && From == o.From && To == o.To;
        public override bool Equals(object obj) => obj is HalfEdge h && Equals(h);
        public override int GetHashCode() => unchecked(Edge.GetHashCode() * 397 ^ From.GetHashCode() * 31 ^ To.GetHashCode());
        public override string ToString() => From + "-[" + Edge + "]->" + To;
    }

    public enum FaceResult { Ok = 0, NoRoad = 1, TooLarge = 2, Open = 3, NotEnclosing = 4 }

    public static class RoadGraphAlgorithms
    {
        /// <summary>
        /// Walks the face lying to the LEFT of <paramref name="start"/> (counter-clockwise for a
        /// bounded face in XZ with x right, z up). At every node the walk takes the outgoing edge
        /// with the smallest clockwise angle from the way back, which keeps the face on the left.
        /// Dead ends are walked around (u-turn) and their spurs removed afterwards.
        /// </summary>
        public static FaceResult TraceLeftFace(IRoadGraph graph, HalfEdge start, int maxSteps, List<HalfEdge> loop)
        {
            loop.Clear();
            var cur = start;
            for (int step = 0; step < maxSteps; step++)
            {
                loop.Add(cur);
                var links = graph.Links(cur.To);
                if (links == null || links.Count == 0) return FaceResult.Open;

                // Direction at cur.To pointing back along the edge we arrived on.
                float2 back = float2.zero;
                bool foundBack = false;
                foreach (var l in links)
                    if (l.Edge == cur.Edge && l.Other == cur.From) { back = l.Direction; foundBack = true; break; }
                if (!foundBack) return FaceResult.Open;

                RoadLink chosen = default;
                bool any = false;
                float bestAngle = float.MaxValue;
                foreach (var l in links)
                {
                    if (l.Edge == cur.Edge && l.Other == cur.From) continue;
                    float a = ClockwiseAngle(back, l.Direction);
                    if (a < bestAngle) { bestAngle = a; chosen = l; any = true; }
                }
                var next = any ? new HalfEdge(chosen.Edge, cur.To, chosen.Other) : cur.Reversed;   // dead end: turn around
                if (next.Equals(start))
                {
                    RemoveSpurs(loop);
                    return loop.Count >= 2 ? FaceResult.Ok : FaceResult.Open;
                }
                cur = next;
            }
            loop.Clear();
            return FaceResult.TooLarge;
        }

        /// <summary>Removes half-edges walked in both directions (dead-end spurs, bridges) from a closed walk.</summary>
        public static void RemoveSpurs(List<HalfEdge> loop)
        {
            if (loop.Count == 0) return;
            var set = new HashSet<HalfEdge>(loop);
            loop.RemoveAll(h => set.Contains(h.Reversed));
        }

        /// <summary>
        /// Rotation (radians, [0, 2π)) turning <paramref name="from"/> clockwise onto <paramref name="to"/>,
        /// in the XZ plane seen from above (x right, z up). A u-turn (to == from) is 2π-ε, i.e. last.
        /// </summary>
        public static float ClockwiseAngle(float2 from, float2 to)
        {
            const float TwoPi = 2f * math.PI;
            float theta = math.atan2(from.y, from.x) - math.atan2(to.y, to.x);
            theta %= TwoPi;
            if (theta <= 1e-6f) theta += TwoPi;
            return theta;
        }

        /// <summary>
        /// Shortest path over the graph (Dijkstra with a binary heap; cost = edge length). Returns the
        /// half-edges from <paramref name="start"/> to <paramref name="end"/>; false when unreachable
        /// within <paramref name="maxVisited"/> nodes.
        /// </summary>
        public static bool ShortestPath(IRoadGraph graph, long start, long end, int maxVisited, List<HalfEdge> path)
        {
            path.Clear();
            if (start == end) return true;
            var dist = new Dictionary<long, float> { [start] = 0f };
            var via = new Dictionary<long, HalfEdge>();
            var done = new HashSet<long>();
            var heap = new MinHeap();
            heap.Push(0f, start);
            while (heap.Count > 0)
            {
                heap.Pop(out float d, out long node);
                if (!done.Add(node)) continue;
                if (node == end) break;
                if (done.Count > maxVisited) return false;
                foreach (var l in graph.Links(node))
                {
                    if (done.Contains(l.Other)) continue;
                    float nd = d + math.max(0.01f, l.Length);
                    if (dist.TryGetValue(l.Other, out float old) && old <= nd) continue;
                    dist[l.Other] = nd;
                    via[l.Other] = new HalfEdge(l.Edge, node, l.Other);
                    heap.Push(nd, l.Other);
                }
            }
            if (!done.Contains(end)) return false;
            for (long n = end; n != start;)
            {
                if (!via.TryGetValue(n, out var h)) { path.Clear(); return false; }
                path.Add(h);
                n = h.From;
            }
            path.Reverse();
            return true;
        }

        /// <summary>
        /// Boundary of the union of several faces (each a closed loop of half-edges, all CCW): the
        /// half-edges whose twin is not used by another selected face. Succeeds only when the result
        /// is ONE simple cycle — faces that touch at a corner only, or that would enclose an
        /// unselected hole, are refused (CS2 areas cannot have holes).
        /// </summary>
        public static bool UnionBoundary(IReadOnlyList<List<HalfEdge>> faces, List<HalfEdge> cycle)
        {
            cycle.Clear();
            var all = new HashSet<HalfEdge>();
            foreach (var f in faces) foreach (var h in f) all.Add(h);
            var remaining = new List<HalfEdge>();
            foreach (var f in faces) foreach (var h in f) if (!all.Contains(h.Reversed)) remaining.Add(h);
            if (remaining.Count == 0) return false;
            var byFrom = new Dictionary<long, HalfEdge>();
            foreach (var h in remaining)
            {
                if (byFrom.ContainsKey(h.From)) return false;     // pinch point: faces meet at a single node
                byFrom[h.From] = h;
            }
            var first = remaining[0];
            var cur = first;
            for (int guard = 0; guard <= remaining.Count; guard++)
            {
                cycle.Add(cur);
                if (!byFrom.TryGetValue(cur.To, out var next)) { cycle.Clear(); return false; }
                if (next.Equals(first)) break;
                cur = next;
            }
            if (cycle.Count != remaining.Count) { cycle.Clear(); return false; }   // several cycles (hole)
            return true;
        }

        /// <summary>True when two faces share at least one edge (adjacent blocks).</summary>
        public static bool SharesEdge(IReadOnlyList<HalfEdge> a, IReadOnlyList<HalfEdge> b)
        {
            var edges = new HashSet<long>();
            foreach (var h in a) edges.Add(h.Edge);
            foreach (var h in b) if (edges.Contains(h.Edge)) return true;
            return false;
        }

        /// <summary>Concatenates per-edge polylines into one ring, dropping duplicate joints.</summary>
        public static List<float3> Stitch(IEnumerable<IReadOnlyList<float3>> pieces, float minGap = 0.05f)
        {
            var ring = new List<float3>();
            foreach (var piece in pieces)
            {
                if (piece == null) continue;
                foreach (var p in piece)
                {
                    if (ring.Count > 0 && math.distance(ring[ring.Count - 1].xz, p.xz) < minGap) continue;
                    ring.Add(p);
                }
            }
            if (ring.Count > 1 && math.distance(ring[0].xz, ring[ring.Count - 1].xz) < minGap) ring.RemoveAt(ring.Count - 1);
            return ring;
        }

        private sealed class MinHeap
        {
            private readonly List<float> _keys = new List<float>();
            private readonly List<long> _values = new List<long>();
            public int Count => _keys.Count;

            public void Push(float key, long value)
            {
                _keys.Add(key); _values.Add(value);
                int i = _keys.Count - 1;
                while (i > 0)
                {
                    int p = (i - 1) / 2;
                    if (_keys[p] <= _keys[i]) break;
                    Swap(i, p);
                    i = p;
                }
            }

            public void Pop(out float key, out long value)
            {
                key = _keys[0]; value = _values[0];
                int last = _keys.Count - 1;
                _keys[0] = _keys[last]; _values[0] = _values[last];
                _keys.RemoveAt(last); _values.RemoveAt(last);
                int i = 0;
                while (true)
                {
                    int l = 2 * i + 1, r = l + 1, m = i;
                    if (l < _keys.Count && _keys[l] < _keys[m]) m = l;
                    if (r < _keys.Count && _keys[r] < _keys[m]) m = r;
                    if (m == i) break;
                    Swap(i, m);
                    i = m;
                }
            }

            private void Swap(int a, int b)
            {
                float k = _keys[a]; _keys[a] = _keys[b]; _keys[b] = k;
                long v = _values[a]; _values[a] = _values[b]; _values[b] = v;
            }
        }
    }
}
