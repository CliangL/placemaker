// rf-shops3 — the ring drive: a loop lane that follows the site's own outline (inset, corners softened), the answer to
// round and odd sites. The user, on the baseline circle: "it looked like a park ... basically no parking". A layout laid
// out from one straight frontage keeps only the rectangle inscribed in a circle (64 % of it) and plants the crescents;
// a ring follows the rim: stores round the outside of the loop facing in, the middle for the lot (strip mall, big box),
// the park (mall park) or the lanes (mall district), spokes from every street to the loop. On a rectangle the same ring
// is the block with rounded corners.

using System;
using System.Collections.Generic;
using Placemaker.Core.Geometry;
using Placemaker.Core.Layers.Common;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Commercial
{
    public sealed partial class Mall
    {
        /// <summary>
        /// The site's outline inset by <paramref name="inset"/> m and simplified to a drivable loop: edges at least
        /// <paramref name="minEdge"/> m (so a row of plots fits along them), corners sharper than ~40° cut by a chamfer of
        /// <paramref name="round"/> m (a soft corner, its wedge planted). CCW. Null when too small.
        /// </summary>
        public float2[] RingLine(float inset, float round, float minEdge)
        {
            var inner = Region.InsetRing(inset);
            if (inner == null || inner.Length < 3 || Poly.Area(inner) < 900f) return null;
            var simp = new List<float2>(Poly.EnsureCcw(Poly.Simplify(inner, 2.5f)));
            // Merge short edges: drop the vertex whose removal loses the least area while any edge is shorter than minEdge.
            for (int guard = 0; guard < 64 && simp.Count > 4; guard++)
            {
                int n = simp.Count, worst = -1; float shortest = float.MaxValue;
                for (int i = 0; i < n; i++)
                {
                    float l = math.distance(simp[i], simp[(i + 1) % n]);
                    if (l < shortest) { shortest = l; worst = i; }
                }
                if (shortest >= minEdge) break;
                // Of the edge's two ends, remove the one whose triangle (with its neighbours) is smaller.
                int a = worst, b = (worst + 1) % n;
                float TriArea(int k) { var p = simp[(k + n - 1) % n]; var q = simp[k]; var r = simp[(k + 1) % n]; return math.abs(Poly.Cross(q - p, r - p)) * 0.5f; }
                simp.RemoveAt(TriArea(a) <= TriArea(b) ? a : b);
            }
            if (simp.Count < 3) return null;
            // Soften the corners: every turn sharper than 40° becomes a chamfer (two points `round` m back along both edges).
            var res = new List<float2>();
            int m = simp.Count;
            for (int i = 0; i < m; i++)
            {
                var p = simp[(i + m - 1) % m]; var q = simp[i]; var r = simp[(i + 1) % m];
                var d1 = math.normalizesafe(q - p); var d2 = math.normalizesafe(r - q);
                float turn = math.degrees(math.acos(math.clamp(math.dot(d1, d2), -1f, 1f)));
                float c = math.min(round, 0.3f * math.min(math.distance(p, q), math.distance(q, r)));
                if (turn > 40f && c >= 3f) { res.Add(q - d1 * c); res.Add(q + d2 * c); }
                else res.Add(q);
            }
            var ring = Poly.EnsureCcw(res.ToArray());
            return Poly.IsSimple(ring) && Poly.Area(ring) >= 900f ? ring : null;
        }

        /// <summary>
        /// A closed loop lane along <paramref name="ring"/> (CCW), starting in the middle of its longest edge. Null when its
        /// corridors are not free or a corner leaves the site.
        /// </summary>
        public Lane RingLoop(float2[] ring, NetRole role, AssetRef asset, float h)
        {
            if (ring == null || ring.Length < 3) return null;
            int n = ring.Length, best = 0; float len = 0f;
            for (int i = 0; i < n; i++) { float l = math.distance(ring[i], ring[(i + 1) % n]); if (l > len) { len = l; best = i; } }
            var start = math.lerp(ring[best], ring[(best + 1) % n], 0.5f);
            var corners = new List<float2>();
            for (int k = 1; k <= n; k++) corners.Add(ring[(best + k) % n]);
            return Loop(start, corners, role, asset, h);
        }

        /// <summary>
        /// Spokes from the streets to a loop: one from the middle of every frontage run at least <paramref name="minRun"/> m long
        /// (two on a run over 200 m), straight in to the loop, kept <paramref name="clear"/> m off the loop's corners. The main
        /// run (index 0) always gets one when it can. Returns the spokes made.
        /// </summary>
        public List<Lane> Spokes(Lane loop, List<StreetShops.Run> runs, NetRole role, AssetRef asset, float h, float minRun = 40f, float clear = 9f, int max = 4)
        {
            var made = new List<Lane>();
            if (loop == null) return made;
            var poly = loop.Points;
            foreach (var r in runs)
            {
                if (made.Count >= max) break;
                if (r.Length < minRun) continue;
                int k = r.Length > 200f ? 2 : 1;
                for (int j = 0; j < k && made.Count < max; j++)
                {
                    float mid = math.lerp(r.From, r.To, (j + 0.5f) / k);
                    // Slide along the run (±12 m) until the spoke meets an edge of the loop away from its corners.
                    Lane got = null;
                    foreach (float shift in new[] { 0f, 6f, -6f, 12f, -12f })
                    {
                        float s = mid + shift;
                        if (s < r.From + CornerClear || s > r.To - CornerClear) continue;
                        var a = r.At(s, 0f);
                        if (!RayHit(poly, a, r.In, out var hit, out int seg)) continue;
                        if (math.distance(hit, poly[seg]) < clear || math.distance(hit, poly[seg + 1]) < clear) continue;
                        bool near = false;
                        foreach (var o in made) if (math.distance(o.Points[o.Points.Count - 1], hit) < 30f) near = true;
                        if (near) continue;
                        got = Connect(End.Street(a, -r.In), null, End.OnLane(loop, hit), role, asset, h);
                        if (got != null) break;
                    }
                    if (got != null) made.Add(got);
                }
            }
            return made;
        }

        /// <summary>The first crossing of the ray from <paramref name="a"/> along <paramref name="dir"/> with the polyline.</summary>
        private static bool RayHit(List<float2> line, float2 a, float2 dir, out float2 hit, out int seg)
        {
            hit = a; seg = -1;
            float best = float.MaxValue;
            var b = a + dir * 2000f;
            for (int i = 0; i + 1 < line.Count; i++)
            {
                if (!Poly.SegmentIntersection(a, b, line[i], line[i + 1], out var p, out float t, out _)) continue;
                float d = t * 2000f;
                if (d > 0.5f && d < best) { best = d; hit = p; seg = i; }
            }
            return seg >= 0;
        }

        /// <summary>
        /// (rf-shops3) How round or odd the site is for a layout drawn from straight frontages: the largest rectangle square to
        /// the main run inside the site, as a share of the site's area (a rectangle ~0.9+, an L or a pinch ~0.6, a circle 0.64).
        /// </summary>
        public float Squareness(StreetShops.Run r)
        {
            var ring = Region.Ring;
            float sMin = float.MaxValue, sMax = float.MinValue, tMin = float.MaxValue, tMax = float.MinValue;
            foreach (var p in ring)
            {
                float s = math.dot(p - r.A, r.Dir), t = math.dot(p - r.A, r.In);
                sMin = math.min(sMin, s); sMax = math.max(sMax, s); tMin = math.min(tMin, t); tMax = math.max(tMax, t);
            }
            // Columns every 4 m: the stretch of each inside the site; the best rectangle over consecutive columns.
            var cols = new List<(float a, float b)>();
            for (float x = sMin + 2f; x <= sMax - 2f; x += 4f)
            {
                float a = float.NaN, b = float.NaN;
                for (float t = tMin - 1f; t <= tMax + 1f; t += 2f)
                    if (Region.Contains(r.At(x, t))) { if (float.IsNaN(a)) a = t; b = t; }
                    else if (!float.IsNaN(a)) break;
                cols.Add((a, b));
            }
            float best = 0f;
            for (int i = 0; i < cols.Count; i++)
            {
                if (float.IsNaN(cols[i].a)) continue;
                float lo = cols[i].a, hi = cols[i].b;
                for (int j = i; j < cols.Count; j++)
                {
                    if (float.IsNaN(cols[j].a)) break;
                    lo = math.max(lo, cols[j].a); hi = math.min(hi, cols[j].b);
                    if (hi <= lo) break;
                    best = math.max(best, (j - i + 1) * 4f * (hi - lo));
                }
            }
            return Region.Area > 1f ? best / Region.Area : 1f;
        }
    }
}
