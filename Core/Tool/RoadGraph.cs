// Placemaker — road-graph helpers for perimeter and multi-block selection.
//
// Block picking itself is Core/Geometry/PlanarGraph.PickFace. This file keeps what the selection
// needs on top of it, working on an ON-DEMAND graph (IRoadGraph reads the ECS lazily, so perimeter
// path fill never builds a whole-map graph): Dijkstra fill between clicked nodes (idea from Grid
// Road Generator, Ti4goc, MIT; links carry curve tangents and edge ids so parallel edges stay
// distinct), the union of several picked blocks, and stitching per-edge polylines into a ring.
//
// Ids are opaque longs (NetRefs.Encode of the entity).

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


    public static class RoadGraphAlgorithms
    {
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
