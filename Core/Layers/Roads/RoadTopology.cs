// Placemaker roads layer (s34) — junction and bend geometry.
//
// Playtest: "corners look like a car cut off something". A street that turns at a node without a curve (a polyline
// corner from the generators, a perimeter tie-in, a link added to a dead end) is drawn by CS2 as a sharp, clipped
// node; side streets meeting at shallow angles make long thin corner wedges; junctions a few metres apart overlap.
// Here: (1) junctions closer than the junction gap merge, (2) bends at plain nodes get a real curve (centre-line radius
// by road class), (3) side streets meeting another road at less than ~60° are bent to meet it squarely.

using System;
using System.Collections.Generic;
using Placemaker.Core.Geometry;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Roads
{
    internal static class RoadTopology
    {
        /// <summary>Bends sharper than this (degrees of turn) at a plain node get a fillet.</summary>
        public const float FilletMinTurn = 14f;
        /// <summary>Side streets are bent so they meet other roads at least at this angle (degrees).</summary>
        public const float MinJunctionAngle = 62f;

        /// <summary>Centre-line radius of a bend by road class (m): wide enough for the carriageway not to fold.</summary>
        public static float BendRadius(NetRole role, float halfWidth)
        {
            switch (role)
            {
                case NetRole.Avenue: return math.max(45f, halfWidth * 3.5f);
                case NetRole.Alley: return math.max(10f, halfWidth * 2.5f);
                case NetRole.Pathway: case NetRole.PathwayWide: return 6f;
                default: return math.max(22f, halfWidth * 2.8f);
            }
        }

        /// <summary>
        /// Merges junctions joined by a car edge shorter than <paramref name="gap"/>: the less connected node moves onto the
        /// other (attached nodes never move). Returns the number merged.
        /// </summary>
        public static int MergeCloseJunctions(RoadNetwork net, float gap)
        {
            int merged = 0;
            for (int pass = 0; pass < 6; pass++)
            {
                var inc = RoadGraph.Incidence(net, RoadGraph.Car);
                bool any = false;
                foreach (var e in net.Edges)
                {
                    if (!RoadGraph.Car(e) || e.N0 == e.N1) continue;
                    if (inc[e.N0].Count < 3 || inc[e.N1].Count < 3) continue;
                    if (e.Role == NetRole.Roundabout) continue;
                    if (math.distance(e.Curve.A.xz, e.Curve.D.xz) >= gap) continue;
                    var a = net.Nodes[e.N0]; var b = net.Nodes[e.N1];
                    bool fa = RoadConnectivity.Attached(a), fb = RoadConnectivity.Attached(b);
                    if (fa && fb) continue;
                    int keep = fa ? e.N0 : fb ? e.N1 : (inc[e.N0].Count >= inc[e.N1].Count ? e.N0 : e.N1);
                    int drop = keep == e.N0 ? e.N1 : e.N0;
                    float3 kp = net.Nodes[keep].Position;
                    e.Discarded = true;
                    foreach (var o in net.Edges)
                    {
                        if (o.Discarded || o == e) continue;
                        if (o.N0 == drop) { o.Curve = o.Curve.WithEnds(kp, o.Curve.D); o.N0 = keep; }
                        if (o.N1 == drop) { o.Curve = o.Curve.WithEnds(o.Curve.A, kp); o.N1 = keep; }
                        if (o.N0 == o.N1) o.Discarded = true;
                    }
                    net.Nodes[drop].Position = kp;
                    merged++;
                    any = true;
                    break;
                }
                if (!any) break;
                net.DiscardDuplicates();
                net.Weld();
            }
            return merged;
        }

        /// <summary>
        /// Rounds the bends at plain (degree-2, unattached) nodes of car roads: both edges are trimmed by R·tan(θ/2) and joined
        /// by a curve tangent to both. R by class, limited so a trim never eats more than 45 % of an edge.
        /// </summary>
        public static int FilletBends(RoadNetwork net, Func<NetRole, float> halfWidth)
        {
            int count = 0;
            for (int pass = 0; pass < 3; pass++)
            {
                var inc = RoadGraph.Incidence(net, RoadGraph.Any);
                var done = new HashSet<int>();
                bool any = false;
                for (int n = 0; n < net.Nodes.Count; n++)
                {
                    if (inc[n].Count != 2 || RoadConnectivity.Attached(net.Nodes[n])) continue;
                    int i0 = inc[n][0], i1 = inc[n][1];
                    if (done.Contains(i0) || done.Contains(i1)) continue;
                    var e0 = net.Edges[i0]; var e1 = net.Edges[i1];
                    if (e0.IsCar != e1.IsCar || e0.Role == NetRole.Roundabout || e1.Role == NetRole.Roundabout) continue;
                    float2 d0 = RoadGraph.Leaving(e0, n), d1 = RoadGraph.Leaving(e1, n);
                    float turn = 180f - RoadGraph.Angle(d0, d1);
                    if (turn < FilletMinTurn || turn > 150f) continue;
                    var role = Rank(e0.Role) >= Rank(e1.Role) ? e0.Role : e1.Role;
                    float r = BendRadius(role, halfWidth(role));
                    float trim = r * math.tan(math.radians(turn) * 0.5f);
                    float l0 = RoadGraph.Length(e0), l1 = RoadGraph.Length(e1);
                    trim = math.min(trim, 0.45f * math.min(l0, l1));
                    if (trim < 3f) continue;
                    // Cut both edges `trim` from the node; the node-side pieces go, a curve joins the new ends.
                    float3 p0 = Trim(net, i0, n, trim, out float2 t0);
                    float3 p1 = Trim(net, i1, n, trim, out float2 t1);
                    var arc = RoadCurve.Fit(p0, new float3(-t0.x, 0f, -t0.y), new float3(t1.x, 0f, t1.y), p1);
                    var edge = new RoadEdge(arc, role) { AssetOverride = e0.AssetOverride, Category = e0.Category, Walk = e0.Walk, Trail = e0.Trail };
                    net.Edges.Add(edge);
                    done.Add(i0); done.Add(i1);
                    count++;
                    any = true;
                }
                if (!any) break;
                net.Weld();
            }
            return count;
        }

        /// <summary>
        /// Cuts edge <paramref name="ei"/> <paramref name="dist"/> metres from <paramref name="node"/>: the node side is dropped.
        /// Returns the new end point and the edge's direction there, pointing away from the node.
        /// </summary>
        private static float3 Trim(RoadNetwork net, int ei, int node, float dist, out float2 awayDir)
        {
            var e = net.Edges[ei];
            bool atStart = e.N0 == node;
            float t = RoadGraph.ParamAt(e.Curve, dist, fromEnd: !atStart);
            e.Curve.Split(math.clamp(t, 0.001f, 0.999f), out var left, out var right);
            if (atStart)
            {
                e.Curve = right;
                awayDir = right.TangentXZ(0f);
                return right.A;
            }
            e.Curve = left;
            awayDir = -left.TangentXZ(1f);
            return left.D;
        }

        private static int Rank(NetRole r)
        {
            switch (r)
            {
                case NetRole.Avenue: return 5;
                case NetRole.PerimeterRoad: return 4;
                case NetRole.Street: return 3;
                case NetRole.CulDeSac: return 2;
                case NetRole.Alley: return 1;
                default: return 0;
            }
        }

        /// <summary>
        /// Side streets meeting another road at less than <see cref="MinJunctionAngle"/>: the end of the lesser edge is turned
        /// to the middle of the free angle around the node (its first control point moves), when the free angle allows a
        /// square meeting and the bent curve stays clear of everything else.
        /// </summary>
        public static int SquareJunctions(RoadNetwork net)
        {
            int fixedCount = 0;
            var inc = RoadGraph.Incidence(net, RoadGraph.Car);
            var index = new RoadGraph.EdgeIndex(net, RoadGraph.Any);
            for (int n = 0; n < net.Nodes.Count; n++)
            {
                if (inc[n].Count < 3) continue;
                var list = inc[n];
                // Directions around the node, sorted by angle.
                var dirs = new List<(float ang, int ei)>();
                foreach (int ei in list)
                {
                    float2 d = RoadGraph.Leaving(net.Edges[ei], n);
                    dirs.Add((math.atan2(d.y, d.x), ei));
                }
                dirs.Sort((a, b) => a.ang.CompareTo(b.ang));
                int m = dirs.Count;
                for (int k = 0; k < m; k++)
                {
                    int ei = dirs[k].ei;
                    var e = net.Edges[ei];
                    if (e.Role == NetRole.Roundabout || e.Role == NetRole.Avenue) continue;
                    float prev = dirs[(k - 1 + m) % m].ang, next = dirs[(k + 1) % m].ang, cur = dirs[k].ang;
                    float gapPrev = Wrap(cur - prev), gapNext = Wrap(next - cur);
                    float minGap = math.degrees(math.min(gapPrev, gapNext));
                    if (minGap >= MinJunctionAngle) continue;
                    float free = gapPrev + gapNext;   // the sector between the two neighbours
                    if (math.degrees(free) < 2f * MinJunctionAngle) continue;
                    // The lesser road bends: never a road that outranks both neighbours.
                    int rankNeighbours = math.max(Rank(net.Edges[dirs[(k - 1 + m) % m].ei].Role), Rank(net.Edges[dirs[(k + 1) % m].ei].Role));
                    if (Rank(e.Role) > rankNeighbours) continue;
                    float target = prev + free * 0.5f;
                    float2 want = new float2(math.cos(target), math.sin(target));
                    float len = RoadGraph.Length(e);
                    if (len < 30f) continue;
                    var old = e.Curve;
                    float arm = math.clamp(len * 0.25f, 10f, 24f);
                    bool atStart = e.N0 == n;
                    if (atStart) e.Curve = new RoadCurve(e.Curve.A, e.Curve.A + new float3(want.x, 0f, want.y) * arm, e.Curve.C, e.Curve.D);
                    else e.Curve = new RoadCurve(e.Curve.A, e.Curve.B, e.Curve.D + new float3(want.x, 0f, want.y) * arm, e.Curve.D);
                    var line = RoadGraph.Sample(e.Curve, 3f);
                    var ignore = new HashSet<int>(inc[e.N0]);
                    foreach (int k2 in inc[e.N1]) ignore.Add(k2);
                    if (index.Crosses(line, 2f, ignore) || SelfFolds(e.Curve))
                    {
                        e.Curve = old;
                        continue;
                    }
                    dirs[k] = (target, ei);
                    fixedCount++;
                }
            }
            return fixedCount;
        }

        private static float Wrap(float a)
        {
            while (a < 0f) a += 2f * math.PI;
            while (a >= 2f * math.PI) a -= 2f * math.PI;
            return a;
        }

        /// <summary>A curve that turns more than 120° in total (a hook) is not a street.</summary>
        private static bool SelfFolds(RoadCurve c)
        {
            float total = 0f;
            float2 prev = c.TangentXZ(0f);
            for (int i = 1; i <= 16; i++)
            {
                float2 d = c.TangentXZ(i / 16f);
                total += RoadGraph.Angle(prev, d);
                prev = d;
            }
            return total > 120f;
        }
    }
}
