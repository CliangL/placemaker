// Shoreline analysis shared by the water types (beach, waterfront).
//
// Everything along a shore is laid out by "how far from the water": sand bands, the back path,
// the promenade, railings, dunes. So the first job is finding the water side of the area:
//   * Inside — water lies inside the outline (the player drew over the waterline): the land is the
//              dry part (grid of WaterDepthAt samples, grown back to the outline), the shore is the
//              piece of its outline that touches the water. Piers may reach out over the water.
//   * Edge   — the outline runs along water outside it: outline edges whose outward probes are wet.
//   * Assumed — no water found (or the player chose a side): edges facing the chosen direction
//              ("away from the road" when roads are known, else the longest edge).
// The shore is then one ordered polyline (the longest contiguous run of shore edges, small gaps
// closed) with smoothed inward normals; Depth(p) = distance to it. Offsets of that polyline give
// lines parallel to the water (paths, rows, fences) and bands between two depths (sand, promenade).

using System;
using System.Collections.Generic;
using Placemaker.Core.Geometry;
using Placemaker.Core.Layers.Common;
using Placemaker.Core.Layers.Park;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Types.Beach
{
    public enum ShoreMode { Edge = 0, Inside = 1, Assumed = 2 }

    public sealed class Shoreline
    {
        public const string NoWaterKey = "Placemaker.Shore.Issue.NoWater";
        public const string WaterInsideKey = "Placemaker.Shore.Issue.WaterInside";
        public const float WetDepth = 0.05f;
        public static readonly string[] Sides = { "auto", "awayFromRoad", "north", "east", "south", "west" };

        public ShoreMode Mode;
        /// <summary>The dry part the type builds on (the whole region unless water lies inside it).</summary>
        public LayerRegion Land;
        /// <summary>The original region (piers may reach out over water inside it).</summary>
        public LayerRegion Outline;
        /// <summary>Ordered points along the land outline at the water (land on the left).</summary>
        public float2[] Chain;
        /// <summary>Smoothed unit normals pointing inland, one per chain point.</summary>
        public float2[] Inward;
        /// <summary>Arc length at each chain point.</summary>
        public float[] Along;
        public float Length;
        public float2 WaterDir;
        /// <summary>Water surface height when water was found, else NaN.</summary>
        public float WaterLevel = float.NaN;
        /// <summary>Largest distance from the shore inside the land (sampled).</summary>
        public float MaxDepth;
        public bool Closed;

        private readonly SegmentIndex _chain = new SegmentIndex(8f);
        private readonly SegmentIndex _other = new SegmentIndex(8f);
        private Func<float2, float> _depthAt = _ => 0f;

        /// <summary>Distance from the shore polyline (capped).</summary>
        public float Depth(float2 p, float cap = 2000f) => _chain.Clearance(p, cap);

        /// <summary>Distance to the outline away from the water (where terrain must blend back), capped.</summary>
        public float EdgeDistance(float2 p, float cap = 2000f) => _other.Count == 0 ? cap : _other.Clearance(p, cap);

        public bool IsWet(float2 p) => _depthAt(p) > WetDepth;

        // ───────────── analysis ─────────────

        /// <summary>
        /// The dry part of <paramref name="region"/> when the player drew over existing water (more than 2 % wet),
        /// else the region itself. For nature types that lay out their own paths and planting (wetland, trails):
        /// nothing of theirs belongs in a lake they were drawn across (art pass a3).
        /// </summary>
        public static LayerRegion DryLand(LayerContext ctx, LayerRegion region)
        {
            var depth = ctx.Site?.WaterDepthAt;
            if (depth == null || region == null) return region;
            var size = region.Max - region.Min;
            float step = math.max(3f, math.sqrt(size.x * size.y / 2500f));
            int wet = 0, total = 0;
            for (float y = region.Min.y + step * 0.5f; y < region.Max.y; y += step)
                for (float x = region.Min.x + step * 0.5f; x < region.Max.x; x += step)
                {
                    var p = new float2(x, y);
                    if (!region.Contains(p)) continue;
                    total++;
                    if (depth(p) > WetDepth) wet++;
                }
            if (wet == 0 || wet < total * 0.02f) return region;
            var land = DryPart(ctx, region, depth, step);
            return land != null && land.Area < region.Area * 0.98f ? land : region;
        }

        public static Shoreline Analyze(LayerContext ctx, LayerRegion region, string side)
        {
            var s = new Shoreline { Outline = region, Land = region };
            if (ctx.Site?.WaterDepthAt != null) s._depthAt = ctx.Site.WaterDepthAt;
            var depth = s._depthAt;
            bool auto = string.IsNullOrEmpty(side) || side == "auto";

            // 1. Water inside the outline: keep the dry part.
            var size = region.Max - region.Min;
            float step = math.max(3f, math.sqrt(size.x * size.y / 2500f));
            int wet = 0, total = 0; double level = 0;
            for (float y = region.Min.y + step * 0.5f; y < region.Max.y; y += step)
            {
                ctx.Check();
                for (float x = region.Min.x + step * 0.5f; x < region.Max.x; x += step)
                {
                    var p = new float2(x, y);
                    if (!region.Contains(p)) continue;
                    total++;
                    float d = depth(p);
                    if (d > WetDepth) { wet++; level += ctx.H(p) + d; }
                }
            }
            if (wet > 0) s.WaterLevel = (float)(level / wet);
            if (wet > 0 && wet >= total * 0.02f)
            {
                var land = DryPart(ctx, region, depth, step);
                if (land != null && land.Area < region.Area * 0.98f)
                {
                    s.Land = land;
                    s.Mode = ShoreMode.Inside;
                    if (wet >= total * 0.5f) ctx.Issue(IssueSeverity.Info, WaterInsideKey);
                }
            }

            // 2. Shore edges of the land outline.
            var pts = Poly.Resample(s.Land.Ring, 3f, true);
            int n = pts.Length;
            if (n < 3) return null;
            var outN = new float2[n];
            var len = new float[n];
            var wetEdge = new bool[n];
            int wetCount = 0; double wetLevel = 0; float wetLen = 0;
            for (int i = 0; i < n; i++)
            {
                var a = pts[i]; var b = pts[(i + 1) % n];
                var dir = math.normalizesafe(b - a, new float2(1, 0));
                outN[i] = new float2(dir.y, -dir.x);
                len[i] = math.distance(a, b);
                var m = (a + b) * 0.5f;
                foreach (float reach in new[] { 1.5f, 5f, 12f })
                {
                    var q = m + outN[i] * reach;
                    float d = depth(q);
                    if (d > WetDepth) { wetEdge[i] = true; wetCount++; wetLevel += ctx.H(q) + d; wetLen += len[i]; break; }
                }
            }
            if (wetCount > 0 && float.IsNaN(s.WaterLevel)) s.WaterLevel = (float)(wetLevel / wetCount);

            bool[] flags;
            bool compass = !auto && side != "awayFromRoad";
            if (s.Mode == ShoreMode.Inside || (!compass && wetLen >= 8f))
            {
                flags = wetEdge;
                if (s.Mode != ShoreMode.Inside) s.Mode = ShoreMode.Edge;
            }
            else
            {
                var w = Direction(ctx, s.Land, side, out bool fromRoad);
                flags = new bool[n];
                int bestI = 0; float bestDot = float.MinValue;
                for (int i = 0; i < n; i++)
                {
                    float dot = math.dot(outN[i], w);
                    flags[i] = dot > 0.4f;
                    if (dot > bestDot) { bestDot = dot; bestI = i; }
                }
                flags[bestI] = true;
                s.Mode = wetLen >= 8f ? ShoreMode.Edge : ShoreMode.Assumed;
                if (auto || (side == "awayFromRoad" && !fromRoad)) ctx.Issue(IssueSeverity.Info, NoWaterKey);
            }
            CloseGaps(flags, len, 10f);

            // 3. Longest run of shore edges (cyclic).
            int start = 0, count = 0;
            bool all = true;
            foreach (var f in flags) if (!f) { all = false; break; }
            if (all) { s.Closed = true; start = 0; count = n; }
            else
            {
                float best = -1f;
                for (int i = 0; i < n; i++)
                {
                    if (!flags[i] || flags[(i - 1 + n) % n]) continue;
                    float l = 0; int c = 0;
                    for (int k = i; flags[k % n] && c < n; k++) { l += len[k % n]; c++; }
                    if (l > best) { best = l; start = i; count = c; }
                }
                if (count == 0) { start = 0; count = 1; }
            }
            var inChain = new bool[n];
            var chain = new float2[count + 1];
            var inward = new float2[count + 1];
            var waterDir = float2.zero;
            for (int k = 0; k < count; k++)
            {
                int e = (start + k) % n;
                inChain[e] = true;
                chain[k] = pts[e];
                waterDir += outN[e] * len[e];
            }
            chain[count] = pts[(start + count) % n];
            for (int k = 0; k <= count; k++)
            {
                int ePrev = (start + k - 1 + n) % n, eNext = (start + k) % n;
                float2 nrm = float2.zero;
                if (k > 0 || s.Closed) nrm -= outN[ePrev];
                if (k < count || s.Closed) nrm -= outN[eNext];
                inward[k] = math.normalizesafe(nrm, -outN[eNext % n]);
            }
            // Smooth the normals so offsets of a jagged painted outline stay calm.
            for (int pass = 0; pass < 6; pass++)
            {
                var tmp = (float2[])inward.Clone();
                for (int k = 0; k <= count; k++)
                {
                    int a = k - 1, b = k + 1;
                    if (s.Closed) { a = (a + count) % count; b = b % count; }
                    else { a = math.max(0, a); b = math.min(count, b); }
                    tmp[k] = math.normalizesafe(inward[a] * 0.25f + inward[k] * 0.5f + inward[b] * 0.25f, inward[k]);
                }
                inward = tmp;
            }
            s.Chain = chain;
            s.Inward = inward;
            s.Along = new float[count + 1];
            for (int k = 1; k <= count; k++) s.Along[k] = s.Along[k - 1] + math.distance(chain[k - 1], chain[k]);
            s.Length = s.Along[count];
            s.WaterDir = math.normalizesafe(waterDir, new float2(0, -1));
            for (int k = 0; k < count; k++) s._chain.Add(chain[k], chain[k + 1], 0f, k);
            for (int i = 0; i < n; i++) if (!inChain[i]) s._other.Add(pts[i], pts[(i + 1) % n], 0f, i);

            // Deepest point from the shore (grid sample).
            float md = 0f;
            float st = math.max(2f, math.sqrt(s.Land.Area / 1500f));
            for (float y = s.Land.Min.y + st * 0.5f; y < s.Land.Max.y; y += st)
                for (float x = s.Land.Min.x + st * 0.5f; x < s.Land.Max.x; x += st)
                {
                    var p = new float2(x, y);
                    if (s.Land.Contains(p)) md = math.max(md, s.Depth(p));
                }
            s.MaxDepth = md;
            ctx.Plan.Facts["shoreLength"] = s.Length;
            ctx.Plan.Facts["shoreMode"] = (int)s.Mode;
            return s;
        }

        /// <summary>
        /// Region minus the wet cells. The water is traced on a slightly enlarged outline (the raster
        /// keeps 1.5 cells off its own border), so the wet pieces are exact at the waterline and reach
        /// past the real outline; the land keeps its real edges everywhere else.
        /// </summary>
        private static LayerRegion DryPart(LayerContext ctx, LayerRegion region, Func<float2, float> depth, float step)
        {
            try
            {
                float cell = math.max(1.5f, step * 0.5f);
                var grownOutline = Poly.Offset(region.Ring, -cell * 2f);
                var traced = grownOutline.Count > 0 ? LayerRegion.From(grownOutline[0]) : null;
                if (traced == null) return null;
                var wetPieces = FreeSpace.Pieces(traced, p => depth(p) <= WetDepth, cell, 30f, ctx.Cancel);
                if (wetPieces.Count == 0) return null;
                var wet = new List<IReadOnlyList<float2>>();
                foreach (var w in wetPieces) wet.Add(w);
                var dry = Poly.Boolean(new[] { region.Ring }, wet, BoolOp.Difference);
                LayerRegion best = null;
                foreach (var r in dry)
                    foreach (var ring in Poly.CutHoles(r, 1f))
                    {
                        var lr = LayerRegion.From(ring);
                        if (lr != null && (best == null || lr.Area > best.Area)) best = lr;
                    }
                return best != null && best.Area > 300f ? best : null;
            }
            catch (Exception) { return null; }
        }

        /// <summary>Direction towards the (assumed) water: compass choice, away from the road, else the longest edge.</summary>
        private static float2 Direction(LayerContext ctx, LayerRegion land, string side, out bool fromRoad)
        {
            fromRoad = false;
            switch (side)
            {
                case "north": return new float2(0, 1);
                case "east": return new float2(1, 0);
                case "south": return new float2(0, -1);
                case "west": return new float2(-1, 0);
            }
            var roads = Entrances.RoadSegments(ctx.Input);
            var c = land.Centroid;
            if (roads.Count > 0 && roads.Nearest(c, 1e5f, out var q) >= 0 && math.distancesq(q, c) > 1f)
            {
                fromRoad = true;
                return math.normalize(c - q);
            }
            int best = 0; float bestLen = -1f;
            for (int i = 0; i < land.Ring.Length; i++)
            {
                float l = math.distance(land.Ring[i], land.Ring[(i + 1) % land.Ring.Length]);
                if (l > bestLen + 0.01f) { bestLen = l; best = i; }
            }
            return -land.InwardNormal(best);
        }

        /// <summary>Flags short unflagged runs between flagged edges (a jetty notch, a sampling miss).</summary>
        private static void CloseGaps(bool[] flags, float[] len, float maxGap)
        {
            int n = flags.Length;
            bool any = false, none = true;
            foreach (var f in flags) { any |= f; none &= f; }
            if (!any || none) return;
            for (int i = 0; i < n; i++)
            {
                if (flags[i] || !flags[(i - 1 + n) % n]) continue;
                float l = 0; int c = 0;
                while (!flags[(i + c) % n] && c < n) { l += len[(i + c) % n]; c++; }
                if (l <= maxGap) for (int k = 0; k < c; k++) flags[(i + k) % n] = true;
            }
        }

        // ───────────── queries ─────────────

        /// <summary>Closest shore point, its inland normal and arc length.</summary>
        public float2 Nearest(float2 p, out float2 inward, out float along)
        {
            float best = float.MaxValue; float2 bestP = Chain[0]; inward = Inward[0]; along = 0f;
            for (int k = 0; k + 1 < Chain.Length; k++)
            {
                var a = Chain[k]; var b = Chain[k + 1];
                var ab = b - a; float l2 = math.lengthsq(ab);
                float t = l2 > 1e-9f ? math.saturate(math.dot(p - a, ab) / l2) : 0f;
                var q = a + ab * t;
                float d = math.distancesq(p, q);
                if (d < best)
                {
                    best = d; bestP = q;
                    inward = math.normalizesafe(math.lerp(Inward[k], Inward[k + 1], t), Inward[k]);
                    along = Along[k] + (Along[k + 1] - Along[k]) * t;
                }
            }
            return bestP;
        }

        /// <summary>Shore point at arc length s with its inland normal and unit tangent (land on the left).</summary>
        public float2 At(float s, out float2 inward, out float2 tangent)
        {
            s = math.clamp(s, 0f, Length);
            int k = 0;
            while (k < Chain.Length - 2 && Along[k + 1] < s) k++;
            float seg = math.max(1e-5f, Along[k + 1] - Along[k]);
            float t = math.saturate((s - Along[k]) / seg);
            inward = math.normalizesafe(math.lerp(Inward[k], Inward[k + 1], t), Inward[k]);
            tangent = new float2(inward.y, -inward.x);
            return math.lerp(Chain[k], Chain[k + 1], t);
        }

        /// <summary>Arc lengths of <paramref name="count"/> evenly spread stations, <paramref name="endInset"/> metres from the ends.</summary>
        public List<float> Stations(int count, float endInset, float phase = 0.5f)
        {
            var r = new List<float>();
            float usable = Length - (Closed ? 0f : 2f * endInset);
            if (count <= 0 || usable <= 0f) return r;
            float o = Closed ? 0f : endInset;
            for (int i = 0; i < count; i++) r.Add(o + usable * (i + phase) / count);
            return r;
        }

        /// <summary>
        /// Lines parallel to the shore at depth <paramref name="d"/>: pieces that stay inside the land
        /// (margin) and are not closer to another part of the shore; smoothed, resampled at ~spacing.
        /// </summary>
        public List<float2[]> Offset(float d, float margin, float minLength, float spacing = 4f)
        {
            var result = new List<float2[]>();
            var run = new List<float2>();
            void Flush()
            {
                if (run.Count >= 2)
                {
                    var line = Smooth(run, 2);
                    float l = 0; for (int i = 1; i < line.Length; i++) l += math.distance(line[i - 1], line[i]);
                    if (l >= minLength) result.Add(Poly.ResamplePolyline(line, spacing));
                }
                run.Clear();
            }
            float stepS = math.max(1.5f, math.min(spacing, 3f));
            int steps = math.max(1, (int)math.ceil(Length / stepS));
            float2 prev = default; bool havePrev = false;
            for (int i = 0; i <= steps; i++)
            {
                var c = At(Length * i / steps, out var inw, out _);
                var q = c + inw * d;
                bool ok = Land.ContainsDisc(q, margin) && Depth(q, d + 2f) >= d - 1.2f;
                if (ok && havePrev && (math.distance(prev, q) > stepS * 3f + 2f || Land.CrossesBoundary(prev, q, false))) Flush();
                if (ok) { run.Add(q); prev = q; havePrev = true; }
                else { Flush(); havePrev = false; }
            }
            Flush();
            return result;
        }

        /// <summary>Simple CCW pieces of the land between depths d0 and d1 (d0 ≤ 0 reaches the shore).</summary>
        public List<float2[]> Band(float d0, float d1, float fromAlong = 0f, float toAlong = -1f)
        {
            var result = new List<float2[]>();
            if (toAlong < 0f) toAlong = Length;
            if (d1 <= d0 + 0.5f || toAlong - fromAlong < 1f) return result;
            if (d0 <= 0.01f) d0 = -3f;
            int steps = math.max(2, (int)math.ceil((toAlong - fromAlong) / 2f));
            var ring = new List<float2>(steps * 2 + 2);
            var inner = new List<float2>(steps + 1);
            for (int i = 0; i <= steps; i++)
            {
                var c = At(fromAlong + (toAlong - fromAlong) * i / steps, out var inw, out _);
                ring.Add(c + inw * d0);
                inner.Add(c + inw * d1);
            }
            for (int i = inner.Count - 1; i >= 0; i--) ring.Add(inner[i]);
            try
            {
                foreach (var r in Poly.Boolean(new[] { (IReadOnlyList<float2>)ring }, new[] { (IReadOnlyList<float2>)Land.Ring }, BoolOp.Intersection, FillRule.Positive))
                    foreach (var piece in Poly.CutHoles(r, 0.5f))
                    {
                        var clean = Poly.Clean(piece, 0.3f);
                        if (clean.Length >= 3 && Poly.Area(clean) > 8f) result.Add(clean);
                    }
            }
            catch (Exception) { }
            return result;
        }

        private static float2[] Smooth(List<float2> pts, int passes)
        {
            var a = pts.ToArray();
            for (int p = 0; p < passes; p++)
            {
                var b = (float2[])a.Clone();
                for (int i = 1; i + 1 < a.Length; i++) b[i] = a[i - 1] * 0.25f + a[i] * 0.5f + a[i + 1] * 0.25f;
                a = b;
            }
            return a;
        }

        // ───────────── polylines → paths ─────────────

        /// <summary>Point at arc length s along a polyline, with its unit tangent.</summary>
        public static float2 PointOn(float2[] line, float s, out float2 tangent)
        {
            for (int i = 1; i < line.Length; i++)
            {
                float l = math.distance(line[i - 1], line[i]);
                if (s <= l || i == line.Length - 1)
                {
                    tangent = math.normalizesafe(line[i] - line[i - 1], new float2(1, 0));
                    return line[i - 1] + tangent * math.clamp(s, 0f, l);
                }
                s -= l;
            }
            tangent = new float2(1, 0);
            return line[0];
        }

        public static float LengthOf(float2[] line)
        {
            float l = 0; for (int i = 1; i < line.Length; i++) l += math.distance(line[i - 1], line[i]);
            return l;
        }

        /// <summary>
        /// Smooth Bézier courses through a polyline (knots every ~knotSpacing m, Catmull-Rom tangents).
        /// A course that strays from the polyline or the land falls back to straight pieces.
        /// </summary>
        public static List<PathCurve> Curves(LayerRegion land, float2[] line, NetRole role, float knotSpacing = 24f)
        {
            var curves = new List<PathCurve>();
            if (line == null || line.Length < 2) return curves;
            float hw = Composer.DefaultHalfWidth(role);
            float avg = LengthOf(line) / math.max(1, line.Length - 1);
            int stride = math.max(1, (int)math.round(knotSpacing / math.max(0.5f, avg)));
            var knots = new List<int>();
            for (int i = 0; i < line.Length - 1; i += stride) knots.Add(i);
            if (knots[knots.Count - 1] != line.Length - 1)
            {
                if (line.Length - 1 - knots[knots.Count - 1] < stride / 3 && knots.Count > 1) knots[knots.Count - 1] = line.Length - 1;
                else knots.Add(line.Length - 1);
            }
            float2 Tan(int i)
            {
                int a = math.max(0, i - 1), b = math.min(line.Length - 1, i + 1);
                return math.normalizesafe(line[b] - line[a], new float2(1, 0));
            }
            for (int k = 0; k + 1 < knots.Count; k++)
            {
                int i0 = knots[k], i1 = knots[k + 1];
                var a = line[i0]; var d = line[i1];
                float l = math.distance(a, d);
                if (l < 0.8f) continue;
                var c = new PathCurve { A = a, B = a + Tan(i0) * l / 3f, C = d - Tan(i1) * l / 3f, D = d, Role = role, HalfWidth = hw };
                ParkPaths.Sample(c);
                if (Follows(land, c, line, i0, i1)) { curves.Add(c); continue; }
                // Straight pieces through the original points.
                for (int i = i0; i < i1; i++)
                {
                    var p = line[i]; var q = line[i + 1];
                    if (math.distance(p, q) < 0.5f) continue;
                    var s = new PathCurve { A = p, B = math.lerp(p, q, 1f / 3f), C = math.lerp(p, q, 2f / 3f), D = q, Role = role, HalfWidth = hw };
                    ParkPaths.Sample(s);
                    curves.Add(s);
                }
            }
            return curves;
        }

        private static bool Follows(LayerRegion land, PathCurve c, float2[] line, int i0, int i1)
        {
            for (int i = 0; i < c.Samples.Length; i++)
            {
                var p = c.Samples[i];
                if (!land.Contains(p)) return false;
                if (i > 0 && land.CrossesBoundary(c.Samples[i - 1], p, false)) return false;
                float best = float.MaxValue;
                for (int k = i0; k < i1; k++) best = math.min(best, SegmentIndex.DistSq(p, line[k], line[k + 1]));
                if (best > 1.2f * 1.2f) return false;
            }
            return true;
        }
    }
}
