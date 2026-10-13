// Ported from ParkingLotTool (KruemelGames, GPL-3.0) — the idea of the cell engine
// (Geometry/Zellen: Zerlegung, Polygonteiler, Vereinigung, Lochtrennung): "material is a label per
// cell instead of a clipping result; neighbouring cells share identical points, so no tolerance and
// no repair is needed". PLT measured the old clip-and-repair path at 12.5 minutes on a slanted
// L-shape; the cell path built the same shape in 15.4 ms with 0.00 % uncovered.
//
// Implementation (new code): the lot is cut into convex cells. Zone contours (setback line, ring
// road edges) and convex asphalt pieces are subtracted by successive half-plane splits, which keeps
// every cell convex and exact. Cells are then welded, T-junctions resolved, slivers that CS2 would
// drop are relabelled to their neighbour's material, and same-material cells are grown greedily
// into simple, hole-free rings (CS2 areas cannot have holes).

using System;
using System.Collections.Generic;
using System.Threading;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Parking
{
    internal static class SurfaceCells
    {
        /// <summary>Zone bit index (matches LayoutEngine.Z* masks: 1 &lt;&lt; zone).</summary>
        private const int ZoneRing = 2;

        private sealed class Cell
        {
            public List<double2> P;
            public int Zone;
            public bool Asphalt;
            public double2 Min, Max;
            public void UpdateBounds() { PGeo.Bounds(P, out Min, out Max); }
        }

        /// <summary>Upper bound on the vertices of one merged ring (keeps CS2's ear clipping cheap).</summary>
        private const int MaxRingVertices = 240;
        /// <summary>Cells thinner than this (2·area/perimeter) take their neighbour's material.</summary>
        private const double SliverWidth = 0.4;

        public static List<SurfacePoly> Build(List<double2> lot, List<(List<double2> Ring, int Zone)> zones,
            List<AsphaltPiece> pieces, bool allAsphalt, CancellationToken cancel)
        {
            var result = new List<SurfacePoly>();
            if (allAsphalt)
            {
                var cells0 = new List<List<double2>>();
                foreach (var c in PGeo.ConvexDecompose(lot)) cells0.Add(c);
                result.Add(new SurfacePoly { Ring = new List<double2>(lot), Asphalt = true, Cells = cells0 });
                return result;
            }

            var cells = new List<Cell>();
            foreach (var part in PGeo.ConvexDecompose(lot))
            {
                var c = new Cell { P = part, Zone = 0 };
                c.UpdateBounds();
                cells.Add(c);
            }
            if (cells.Count == 0) return result;

            foreach (var z in zones)
            {
                cancel.ThrowIfCancellationRequested();
                foreach (var part in PGeo.ConvexDecompose(z.Ring))
                    Subtract(cells, part, -1, c => c.Zone = z.Zone);
            }
            foreach (var c in cells) if (c.Zone == ZoneRing) c.Asphalt = true;

            cancel.ThrowIfCancellationRequested();
            foreach (var piece in pieces)
            {
                if (piece.Poly == null || piece.Poly.Count < 3) continue;
                var poly = new List<double2>(piece.Poly);
                PGeo.EnsureCcw(poly);
                if (PGeo.Area(poly) < 1e-6) continue;
                Subtract(cells, poly, piece.ZoneMask, c => c.Asphalt = true);
            }
            cancel.ThrowIfCancellationRequested();
            return Merge(cells, cancel);
        }

        /// <summary>
        /// Splits every cell overlapping the convex polygon q into its inside part (passed to onInside)
        /// and convex outside fragments. Only cells whose zone bit is in zoneMask take part (-1 = all).
        /// </summary>
        private static void Subtract(List<Cell> cells, List<double2> q, int zoneMask, Action<Cell> onInside)
        {
            PGeo.Bounds(q, out var qmin, out var qmax);
            int count = cells.Count;
            for (int i = 0; i < count; i++)
            {
                var c = cells[i];
                if (zoneMask != -1 && (zoneMask & (1 << c.Zone)) == 0) continue;
                if (c.Max.x <= qmin.x || c.Min.x >= qmax.x || c.Max.y <= qmin.y || c.Min.y >= qmax.y) continue;
                var inside = c.P;
                var fragments = new List<List<double2>>();
                bool empty = false;
                for (int e = 0; e < q.Count && inside != null; e++)
                {
                    var a = q[e]; var b = q[(e + 1) % q.Count];
                    if (PGeo.Len(b - a) < 1e-12) continue;
                    PGeo.SplitConvex(inside, a, b - a, out var left, out var right);
                    if (right != null && PGeo.Area(right) > 1e-9) fragments.Add(right);
                    inside = left;
                    if (inside == null) empty = true;
                }
                if (empty || inside == null || PGeo.Area(inside) < 1e-9) continue;
                int oldZone = c.Zone; bool oldAsphalt = c.Asphalt;
                if (fragments.Count > 0) { c.P = inside; c.UpdateBounds(); }
                onInside(c);
                foreach (var f in fragments)
                {
                    var nc = new Cell { P = f, Zone = oldZone, Asphalt = oldAsphalt };
                    nc.UpdateBounds();
                    cells.Add(nc);
                }
            }
        }

        // ───────────────────────────── merge ─────────────────────────────

        private static List<SurfacePoly> Merge(List<Cell> cells, CancellationToken cancel)
        {
            // weld vertices (1e-5 m grid, neighbours within 2e-5 m merge)
            var verts = new List<double2>();
            var grid = new Dictionary<long, List<int>>();
            const double q = 1e-5;
            long Key(long x, long y) => (x * 73856093L) ^ (y * 19349663L);
            int Weld(double2 p)
            {
                long gx = (long)Math.Round(p.x / q), gy = (long)Math.Round(p.y / q);
                for (long dx = -2; dx <= 2; dx++)
                    for (long dy = -2; dy <= 2; dy++)
                        if (grid.TryGetValue(Key(gx + dx, gy + dy), out var list))
                            foreach (int id in list)
                                if (math.distance(verts[id], p) <= 2e-5) return id;
                int nid = verts.Count;
                verts.Add(p);
                long k = Key(gx, gy);
                if (!grid.TryGetValue(k, out var l2)) grid[k] = l2 = new List<int>();
                l2.Add(nid);
                return nid;
            }

            var polys = new List<List<int>>();
            var mats = new List<bool>();
            foreach (var c in cells)
            {
                var ids = new List<int>(c.P.Count);
                foreach (var p in c.P)
                {
                    int id = Weld(p);
                    if (ids.Count == 0 || ids[ids.Count - 1] != id) ids.Add(id);
                }
                while (ids.Count > 1 && ids[0] == ids[ids.Count - 1]) ids.RemoveAt(ids.Count - 1);
                if (ids.Count < 3) continue;
                polys.Add(ids);
                mats.Add(c.Asphalt);
            }
            cancel.ThrowIfCancellationRequested();

            // resolve T-junctions: vertices lying on another cell's edge are inserted into it
            const double bucket = 2.0;
            var vgrid = new Dictionary<long, List<int>>();
            for (int i = 0; i < verts.Count; i++)
            {
                long k = Key((long)Math.Floor(verts[i].x / bucket), (long)Math.Floor(verts[i].y / bucket));
                if (!vgrid.TryGetValue(k, out var l)) vgrid[k] = l = new List<int>();
                l.Add(i);
            }
            for (int pi = 0; pi < polys.Count; pi++)
            {
                var ring = polys[pi];
                var outRing = new List<int>(ring.Count + 4);
                for (int e = 0; e < ring.Count; e++)
                {
                    int u = ring[e], v = ring[(e + 1) % ring.Count];
                    outRing.Add(u);
                    var a = verts[u]; var b = verts[v];
                    var ab = b - a; double l2 = PGeo.Dot(ab, ab);
                    if (l2 < 1e-12) continue;
                    var hits = new List<(double T, int Id)>();
                    long x0 = (long)Math.Floor(Math.Min(a.x, b.x) / bucket), x1 = (long)Math.Floor(Math.Max(a.x, b.x) / bucket);
                    long y0 = (long)Math.Floor(Math.Min(a.y, b.y) / bucket), y1 = (long)Math.Floor(Math.Max(a.y, b.y) / bucket);
                    if ((x1 - x0 + 1) * (y1 - y0 + 1) > 4000) continue;
                    for (long gx = x0; gx <= x1; gx++)
                        for (long gy = y0; gy <= y1; gy++)
                            if (vgrid.TryGetValue(Key(gx, gy), out var list))
                                foreach (int w in list)
                                {
                                    if (w == u || w == v) continue;
                                    var p = verts[w];
                                    double t = PGeo.Dot(p - a, ab) / l2;
                                    if (t <= 1e-9 || t >= 1 - 1e-9) continue;
                                    if (PGeo.Len(a + ab * t - p) > 3e-5) continue;
                                    hits.Add((t, w));
                                }
                    if (hits.Count == 0) continue;
                    hits.Sort((h1, h2) => h1.T.CompareTo(h2.T));
                    int last = -1;
                    foreach (var h in hits) if (h.Id != last) { outRing.Add(h.Id); last = h.Id; }
                }
                polys[pi] = outRing;
            }
            cancel.ThrowIfCancellationRequested();

            // edge → cells adjacency
            var edgeOwner = new Dictionary<long, int>();   // directed edge → cell
            long EKey(int a, int b) => ((long)a << 32) | (uint)b;
            for (int pi = 0; pi < polys.Count; pi++)
            {
                var r = polys[pi];
                for (int e = 0; e < r.Count; e++) edgeOwner[EKey(r[e], r[(e + 1) % r.Count])] = pi;
            }
            List<int> Neighbours(int pi)
            {
                var r = polys[pi]; var ns = new List<int>();
                for (int e = 0; e < r.Count; e++)
                    if (edgeOwner.TryGetValue(EKey(r[(e + 1) % r.Count], r[e]), out int o) && o != pi && !ns.Contains(o)) ns.Add(o);
                return ns;
            }

            // slivers take the material of the neighbour they share the longest edge with
            for (int pass = 0; pass < 3; pass++)
            {
                bool changed = false;
                for (int pi = 0; pi < polys.Count; pi++)
                {
                    var pts = Points(polys[pi], verts);
                    double area = PGeo.Area(pts), per = PGeo.Perimeter(pts);
                    if (per <= 0 || 2 * area / per >= SliverWidth) continue;
                    var r = polys[pi];
                    double same = 0, other = 0;
                    for (int e = 0; e < r.Count; e++)
                    {
                        if (!edgeOwner.TryGetValue(EKey(r[(e + 1) % r.Count], r[e]), out int o) || o == pi) continue;
                        double len = PGeo.Len(verts[r[(e + 1) % r.Count]] - verts[r[e]]);
                        if (mats[o] == mats[pi]) same += len; else other += len;
                    }
                    if (other > same) { mats[pi] = !mats[pi]; changed = true; }
                }
                if (!changed) break;
            }

            // greedy growth into simple hole-free rings
            var group = new int[polys.Count];
            for (int i = 0; i < group.Length; i++) group[i] = -1;
            var result = new List<SurfacePoly>();
            for (int seed = 0; seed < polys.Count; seed++)
            {
                if (group[seed] >= 0) continue;
                cancel.ThrowIfCancellationRequested();
                int gid = result.Count;
                var boundary = new HashSet<long>();
                var vcount = new Dictionary<int, int>();
                var members = new List<int>();
                bool TryAdd(int pi)
                {
                    var r = polys[pi];
                    int n = r.Count;
                    var shared = new bool[n];
                    int sharedCount = 0;
                    for (int e = 0; e < n; e++)
                        if (boundary.Contains(EKey(r[(e + 1) % n], r[e]))) { shared[e] = true; sharedCount++; }
                    if (members.Count > 0)
                    {
                        if (sharedCount == 0 || sharedCount == n) return false;
                        int transitions = 0;
                        for (int e = 0; e < n; e++) if (shared[e] && !shared[(e + 1) % n]) transitions++;
                        if (transitions != 1) return false;
                        // vertices not on the shared chain must be new to the boundary (else a pinch/hole)
                        for (int e = 0; e < n; e++)
                        {
                            int v = r[e];
                            bool onChain = shared[e] || shared[PGeo.Mod(e - 1, n)];
                            if (!onChain && vcount.TryGetValue(v, out int cnt) && cnt > 0) return false;
                        }
                        if (boundary.Count + n - 2 * sharedCount > MaxRingVertices) return false;
                    }
                    for (int e = 0; e < n; e++)
                    {
                        int a = r[e], b = r[(e + 1) % n];
                        if (shared[e])
                        {
                            boundary.Remove(EKey(b, a));
                            Dec(vcount, a); Dec(vcount, b);
                        }
                        else
                        {
                            boundary.Add(EKey(a, b));
                            Inc(vcount, a); Inc(vcount, b);
                        }
                    }
                    members.Add(pi);
                    group[pi] = gid;
                    return true;
                }
                TryAdd(seed);
                var queue = new List<int>(Neighbours(seed));
                var tries = new Dictionary<int, int>();
                for (int qi = 0; qi < queue.Count; qi++)
                {
                    int c = queue[qi];
                    if (group[c] >= 0 || mats[c] != mats[seed]) continue;
                    if (TryAdd(c))
                    {
                        foreach (int nb in Neighbours(c)) if (group[nb] < 0 && mats[nb] == mats[seed]) queue.Add(nb);
                    }
                    else
                    {
                        tries.TryGetValue(c, out int t);
                        if (t < 3) { tries[c] = t + 1; queue.Add(c); }
                    }
                    if (queue.Count > 20 * polys.Count + 100) break;
                }
                var ring = TraceRing(boundary, verts);
                var cellPts = new List<List<double2>>();
                foreach (int m in members) cellPts.Add(Points(polys[m], verts));
                if (ring == null)
                {
                    foreach (var cp in cellPts)
                        result.Add(new SurfacePoly { Ring = cp, Asphalt = mats[seed], Cells = new List<List<double2>> { cp } });
                    continue;
                }
                result.Add(new SurfacePoly { Ring = ring, Asphalt = mats[seed], Cells = cellPts });
            }
            return result;
        }

        private static void Inc(Dictionary<int, int> d, int k) { d.TryGetValue(k, out int v); d[k] = v + 1; }
        private static void Dec(Dictionary<int, int> d, int k) { d.TryGetValue(k, out int v); d[k] = v - 1; }

        private static List<double2> Points(List<int> ids, List<double2> verts)
        {
            var r = new List<double2>(ids.Count);
            foreach (int i in ids) r.Add(verts[i]);
            return r;
        }

        private static List<double2> TraceRing(HashSet<long> boundary, List<double2> verts)
        {
            if (boundary.Count < 3) return null;
            var next = new Dictionary<int, int>();
            foreach (long k in boundary)
            {
                int a = (int)(k >> 32), b = (int)(uint)k;
                if (next.ContainsKey(a)) return null;
                next[a] = b;
            }
            int start = int.MaxValue;
            foreach (var k in next.Keys) if (k < start) start = k;
            var ids = new List<int>();
            int cur = start;
            for (int guard = 0; guard <= next.Count; guard++)
            {
                ids.Add(cur);
                if (!next.TryGetValue(cur, out cur)) return null;
                if (cur == start) break;
            }
            if (ids.Count != next.Count) return null;
            var pts = Points(ids, verts);
            var simple = PGeo.Simplify(pts, 1e-6, 1e-7);
            if (simple.Count < 3) return null;
            PGeo.EnsureCcw(simple);
            return simple;
        }
    }
}
