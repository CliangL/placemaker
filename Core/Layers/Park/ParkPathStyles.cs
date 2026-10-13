// Path styles for the park (playtest 2, item 3: "paths need character, not minimum spanning"):
//   auto      the ParkManager planner (natural network by area)
//   garden    the same network, meandering: every edge is broken into gentle, irregular bends
//   curvy     the planner's network with strong smooth curves (lighter meander, long wavelength)
//   formal    straight axes: the long axis and the cross axis through the centre, each entrance
//             joined straight to the nearest axis — no curves
//   lakeside  a loop around the pond (needs the pond), entrances joined to it
//   scenic    one route from entrance to entrance that passes every feature spot (playground,
//             picnic, fountain…), meandering between them
// Every style returns a ParkPathGraph so curves, furniture and bins work unchanged.

using System;
using System.Collections.Generic;
using Placemaker.Core.Geometry;
using Placemaker.Core.Layers.Common;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Park
{
    public static class ParkPathStyles
    {
        public static readonly string[] Styles = { "auto", "garden", "curvy", "formal", "lakeside", "scenic" };

        /// <summary>Breaks every edge into bends displaced sideways by value noise. <paramref name="ok"/> rejects bad points.</summary>
        public static ParkPathGraph Meander(ParkPathGraph g, IReadOnlyList<float2> gates, float amplitude, float wavelength, uint seed, Func<float2, bool> ok)
        {
            if (g == null || g.IsEmpty) return g;
            var segs = new List<float2>();
            foreach (var e in g.Edges)
            {
                var a = g.Nodes[e.A].Position; var b = g.Nodes[e.B].Position;
                AddMeander(segs, a, b, amplitude, wavelength, Hashing.Mix(seed, (uint)e.Id), ok);
            }
            var r = ParkPathGraph.FromSegments(segs, gates);
            return r.IsEmpty ? g : r;
        }

        private static void AddMeander(List<float2> segs, float2 a, float2 b, float amplitude, float wavelength, uint seed, Func<float2, bool> ok)
        {
            float len = math.distance(a, b);
            int pieces = math.max(1, (int)math.round(len / math.max(8f, wavelength)));
            var dir = (b - a) / math.max(0.001f, len);
            var n = new float2(-dir.y, dir.x);
            float amp = math.min(amplitude, len * 0.18f);
            var prev = a;
            for (int i = 1; i <= pieces; i++)
            {
                float2 p;
                if (i == pieces) p = b;
                else
                {
                    var basePt = math.lerp(a, b, (float)i / pieces);
                    // Alternate sides with noisy amplitude so bends read as designed, not random.
                    float side = (i & 1) == 0 ? 1f : -1f;
                    float k = 0.5f + 0.5f * Hashing.Unit(Hashing.Mix(seed, (uint)i));
                    p = basePt + n * side * amp * k;
                    if (ok != null && !ok(p))
                    {
                        p = basePt + n * side * amp * k * 0.4f;
                        if (!ok(p)) p = basePt;
                    }
                }
                segs.Add(prev); segs.Add(p);
                prev = p;
            }
        }

        /// <summary>Long axis + cross axis through the centre; entrances join the nearest axis straight.</summary>
        public static ParkPathGraph Formal(LayerRegion region, IReadOnlyList<float2> gates, Func<float2, bool> ok)
        {
            LeisureKit.Frame(region, out var centre, out var axis, out var side, out _, out _);
            var axes = new List<(float2 a, float2 b)>();
            if (LeisureKit.Chord(region, centre, axis, 4f, out var a0, out var b0)) axes.Add((a0, b0));
            if (LeisureKit.Chord(region, centre, side, 4f, out var a1, out var b1)) axes.Add((a1, b1));
            if (axes.Count == 0) return ParkPathGraph.Empty();
            var stops = new List<List<float>>();
            foreach (var ax in axes) stops.Add(new List<float> { 0f, 1f });
            // The two axes cross: split both there.
            if (axes.Count == 2 && Intersect(axes[0].a, axes[0].b, axes[1].a, axes[1].b, out float t0, out float t1))
            { stops[0].Add(t0); stops[1].Add(t1); }
            var segs = new List<float2>();
            foreach (var gp in gates)
            {
                int bestAx = -1; float bestT = 0f, bestD = float.MaxValue;
                for (int i = 0; i < axes.Count; i++)
                {
                    var (a, b) = axes[i];
                    var q = SegmentIndex.ClosestPoint(gp, a, b);
                    float d = math.distance(gp, q);
                    if (d < bestD && !region.CrossesBoundary(gp, q, true)) { bestD = d; bestAx = i; bestT = math.dot(q - a, b - a) / math.max(1e-4f, math.lengthsq(b - a)); }
                }
                if (bestAx < 0) continue;
                var (aa, bb) = axes[bestAx];
                var foot = math.lerp(aa, bb, bestT);
                if (bestD > 0.5f) { segs.Add(gp); segs.Add(foot); }
                stops[bestAx].Add(bestT);
            }
            for (int i = 0; i < axes.Count; i++)
            {
                var ts = stops[i]; ts.Sort();
                var (a, b) = axes[i];
                for (int k = 1; k < ts.Count; k++)
                {
                    if (ts[k] - ts[k - 1] < 1e-3f) continue;
                    segs.Add(math.lerp(a, b, ts[k - 1])); segs.Add(math.lerp(a, b, ts[k]));
                }
            }
            return ParkPathGraph.FromSegments(segs, gates);
        }

        /// <summary>A loop around the lake (offset outward), entrances joined to its nearest vertex. Empty when it does not fit.</summary>
        public static ParkPathGraph Lakeside(LayerRegion region, float2[] lake, IReadOnlyList<float2> gates, float offset = 5f)
        {
            if (lake == null || lake.Length < 3) return ParkPathGraph.Empty();
            List<float2[]> rings;
            try { rings = Poly.Offset(lake, -offset); } catch (Exception) { return ParkPathGraph.Empty(); }
            float2[] loop = null; float area = 0f;
            foreach (var r in rings) { float a = Poly.Area(r); if (a > area) { area = a; loop = r; } }
            if (loop == null) return ParkPathGraph.Empty();
            loop = Poly.Resample(loop, 14f);
            foreach (var p in loop) if (!region.ContainsDisc(p, 2.5f)) return ParkPathGraph.Empty();
            var segs = new List<float2>();
            for (int i = 0; i < loop.Length; i++) { segs.Add(loop[i]); segs.Add(loop[(i + 1) % loop.Length]); }
            foreach (var g in gates)
            {
                int best = -1; float bd = float.MaxValue;
                for (int i = 0; i < loop.Length; i++)
                {
                    float d = math.distance(g, loop[i]);
                    if (d < bd && !CrossesRing(g, loop[i], lake) && !region.CrossesBoundary(g, loop[i], true)) { bd = d; best = i; }
                }
                if (best >= 0) { segs.Add(g); segs.Add(loop[best]); }
            }
            return ParkPathGraph.FromSegments(segs, gates);
        }

        /// <summary>
        /// One route that passes every destination (rim points), entrance to entrance (or a loop back to a
        /// single entrance); other entrances join the nearest route point. Legs meander.
        /// </summary>
        public static ParkPathGraph Scenic(LayerRegion region, IReadOnlyList<float2> gates, IReadOnlyList<(float2 c, float r)> stops,
            uint seed, Func<float2, bool> ok)
        {
            if (gates.Count == 0 || stops.Count == 0) return ParkPathGraph.Empty();
            var left = new List<(float2 c, float r)>(stops);
            var route = new List<float2> { gates[0] };
            var cur = gates[0];
            if (gates.Count == 1)
            {
                var g0 = region.ClosestOnBoundary(gates[0], out int seg, out _);
                cur = g0 + region.InwardNormal(seg) * 8f;
                route.Add(cur);
            }
            while (left.Count > 0)
            {
                int best = 0; float bd = float.MaxValue;
                for (int i = 0; i < left.Count; i++) { float d = math.distance(cur, left[i].c); if (d < bd) { bd = d; best = i; } }
                var s = left[best]; left.RemoveAt(best);
                var rim = s.c + math.normalizesafe(cur - s.c, new float2(1, 0)) * (s.r + 2.5f);
                if (!region.ContainsDisc(rim, 2f)) continue;
                route.Add(rim); cur = rim;
            }
            if (gates.Count >= 2) route.Add(gates[gates.Count - 1]);
            else route.Add(route[1]);   // loop back to the entry node
            var segs = new List<float2>();
            for (int i = 1; i < route.Count; i++)
            {
                if (region.CrossesBoundary(route[i - 1], route[i], true)) continue;
                AddMeander(segs, route[i - 1], route[i], 7f, 22f, Hashing.Mix(seed, (uint)i), ok);
            }
            for (int gi = 1; gi < gates.Count - 1; gi++)
            {
                int best = -1; float bd = float.MaxValue;
                for (int i = 1; i < segs.Count; i++)
                {
                    float d = math.distance(gates[gi], segs[i]);
                    if (d < bd && !region.CrossesBoundary(gates[gi], segs[i], true)) { bd = d; best = i; }
                }
                if (best >= 0) { segs.Add(gates[gi]); segs.Add(segs[best]); }
            }
            return ParkPathGraph.FromSegments(segs, gates);
        }

        private static bool CrossesRing(float2 a, float2 b, float2[] ring)
        {
            for (int i = 0; i < ring.Length; i++)
                if (Poly.SegmentsIntersect(a, b, ring[i], ring[(i + 1) % ring.Length])) return true;
            return false;
        }

        private static bool Intersect(float2 p, float2 p2, float2 q, float2 q2, out float t, out float u)
        {
            var r = p2 - p; var s = q2 - q;
            float den = Poly.Cross(r, s);
            t = u = 0f;
            if (math.abs(den) < 1e-6f) return false;
            t = Poly.Cross(q - p, s) / den;
            u = Poly.Cross(q - p, r) / den;
            return t > 0.01f && t < 0.99f && u > 0.01f && u < 0.99f;
        }
    }
}
