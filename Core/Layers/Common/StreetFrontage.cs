// Street frontage of a region: the parts of its outline that face a road (existing nets near the
// area, or roads an earlier layer of the recipe planned). Shared by
//   * "Shops along the street" (parks, plazas, campgrounds, leisure types): the host carves a band of
//     whole 8 m zone cells along the frontage out of its own region before planning, and hands the
//     band to the edgeZoning feature as "shops" regions (entrances keep a clear gap);
//   * the terrain feature's berms along roads;
//   * plaza bollards at road edges.
// The frontage test is the one EdgeZoningAddOn uses (adapted from Grid Road Generator, Ti4goc).
// Parking keeps its own built-in zoning (Parking/Zoning.cs), which follows the same rules.

using System;
using System.Collections.Generic;
using Placemaker.Core.Geometry;
using Placemaker.Core.Layers.Roads;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Common
{
    public static class StreetFrontage
    {
        public const string ShopsTag = "shops";
        public const float Cell = 8f;
        /// <summary>Frontage = outline within road half-width + this of a road centre line.</summary>
        public const float Tolerance = 4f;
        /// <summary>Clear gap kept each side of an entrance through a shop band.</summary>
        public const float GapHalfWidth = 5f;

        public struct RoadLine { public float2 A, B; public float HalfWidth; }

        /// <summary>Road centre lines next to the region: nets reserved by earlier layers, and existing roads.</summary>
        public static List<RoadLine> Roads(AreaInput input)
        {
            var list = new List<RoadLine>();
            if (input?.Reserved != null)
                foreach (var c in input.Reserved.Corridors)
                    if (IsRoadTag(c.Tag)) list.Add(new RoadLine { A = c.A, B = c.B, HalfWidth = c.HalfWidth });
            if (input?.Site?.NearbyNets != null)
                foreach (var nn in input.Site.NearbyNets)
                {
                    if (nn.Category != NetCategory.Road) continue;
                    var curve = new RoadCurve(nn.A, nn.B, nn.C, nn.D);
                    float hw = nn.Width > 0f ? nn.Width * 0.5f : Composer.DefaultHalfWidth(NetRole.Street);
                    float2 prev = curve.A.xz;
                    for (int i = 1; i <= 8; i++)
                    {
                        float2 p = curve.Position(i / 8f).xz;
                        list.Add(new RoadLine { A = prev, B = p, HalfWidth = hw });
                        prev = p;
                    }
                }
            return list;
        }

        public static bool IsRoadTag(string tag) =>
            tag == nameof(NetRole.Street) || tag == nameof(NetRole.Avenue) || tag == nameof(NetRole.CulDeSac)
            || tag == nameof(NetRole.Roundabout) || tag == nameof(NetRole.PerimeterRoad);

        public static bool Fronts(float2 p, List<RoadLine> roads)
        {
            foreach (var r in roads)
                if (math.sqrt(SegmentIndex.DistSq(p, r.A, r.B)) <= r.HalfWidth + Tolerance) return true;
            return false;
        }

        /// <summary>Distance from p to the nearest road-facing part of the outline (cap when none).</summary>
        public static float DistanceToFrontage(float2 p, List<(float2 a, float2 b)> frontage, float cap)
        {
            float best = cap * cap;
            foreach (var f in frontage) best = math.min(best, SegmentIndex.DistSq(p, f.a, f.b));
            return math.sqrt(best);
        }

        /// <summary>Outline pieces (≈ 4 m resolution, merged) that face a road. CCW ring: inside on the left.</summary>
        public static List<(float2 a, float2 b)> Edges(float2[] ring, List<RoadLine> roads, float minLength = 4f)
        {
            var result = new List<(float2, float2)>();
            if (roads == null || roads.Count == 0 || ring == null || ring.Length < 3) return result;
            int n = ring.Length;
            for (int i = 0; i < n; i++)
            {
                float2 a = ring[i], b = ring[(i + 1) % n];
                float len = math.distance(a, b);
                if (len < 0.5f) continue;
                int steps = math.max(1, (int)math.ceil(len / 4f));
                int runStart = -1;
                for (int k = 0; k <= steps; k++)
                {
                    bool on = k < steps && Fronts(math.lerp(a, b, (k + 0.5f) / steps), roads);
                    if (on && runStart < 0) runStart = k;
                    if (!on && runStart >= 0)
                    {
                        var p0 = math.lerp(a, b, (float)runStart / steps);
                        var p1 = math.lerp(a, b, (float)k / steps);
                        if (math.distance(p0, p1) >= minLength) result.Add((p0, p1));
                        runStart = -1;
                    }
                }
            }
            return result;
        }

        /// <summary>
        /// Shop band quads along the frontage: whole cells deep (≤ <paramref name="depth"/>), inside the
        /// outline, with a gap at each entrance point. Pads mode keeps only short patches
        /// (<paramref name="padLength"/>) spaced along the frontage.
        /// </summary>
        public static List<float2[]> Bands(LayerRegion region, List<RoadLine> roads, float depth, IReadOnlyList<float2> gates,
            bool pads, float padLength = 24f, float padSpacing = 48f)
        {
            var quads = new List<float2[]>();
            depth = math.clamp(math.floor(depth / Cell) * Cell, Cell, 6 * Cell);
            foreach (var (a, b) in Edges(region.Ring, roads, 2 * Cell))
            {
                float len = math.distance(a, b);
                var dir = (b - a) / len;
                var inward = new float2(-dir.y, dir.x);
                // Free intervals along the edge (entrance gaps cut out).
                var free = new List<float2> { new float2(0f, len) };
                if (gates != null)
                    foreach (var g in gates)
                    {
                        float off = math.abs(math.dot(g - a, inward));
                        if (off > 2f) continue;
                        float s = math.dot(g - a, dir);
                        Cut(free, s - GapHalfWidth, s + GapHalfWidth);
                    }
                foreach (var iv in free)
                {
                    float from = iv.x, to = iv.y;
                    if (to - from < 2 * Cell) continue;
                    if (pads)
                    {
                        for (float s = from + (to - from - padLength) % padSpacing * 0.5f; s + padLength <= to + 0.01f; s += padSpacing)
                            AddQuad(region, quads, a + dir * s, a + dir * (s + padLength), inward, depth);
                    }
                    else
                    {
                        int pieces = math.max(1, (int)math.ceil((to - from) / (3 * Cell)));
                        for (int k = 0; k < pieces; k++)
                            AddQuad(region, quads, a + dir * math.lerp(from, to, (float)k / pieces), a + dir * math.lerp(from, to, (float)(k + 1) / pieces), inward, depth);
                    }
                }
            }
            return quads;
        }

        private static void Cut(List<float2> free, float c0, float c1)
        {
            for (int i = free.Count - 1; i >= 0; i--)
            {
                var iv = free[i];
                if (c1 <= iv.x || c0 >= iv.y) continue;
                free.RemoveAt(i);
                if (iv.x < c0) free.Insert(i, new float2(iv.x, c0));
                if (c1 < iv.y) free.Insert(i + (iv.x < c0 ? 1 : 0), new float2(c1, iv.y));
            }
        }

        private static void AddQuad(LayerRegion region, List<float2[]> into, float2 p0, float2 p1, float2 inward, float depth)
        {
            for (float d = depth; d >= Cell - 0.01f; d -= Cell)
            {
                var c0 = p0 + inward * d; var c1 = p1 + inward * d;
                if (!region.Contains(c0) || !region.Contains(c1) || !region.Contains(0.5f * (c0 + c1))) continue;
                if (region.CrossesBoundary(math.lerp(p0, c0, 0.05f), c0, false) || region.CrossesBoundary(math.lerp(p1, c1, 0.05f), c1, false)
                    || region.CrossesBoundary(c0, c1, false)) continue;
                into.Add(new[] { p0, p1, c1, c0 });
                return;
            }
        }

        /// <summary>
        /// The host's working outline after carving the shop bands: the largest piece of (outline − bands).
        /// Returns null when nothing sensible is left (the host then keeps its full outline).
        /// </summary>
        public static float2[] Carve(LayerRegion region, List<float2[]> bands, float minArea)
        {
            if (bands.Count == 0) return null;
            // Bands are shrunk a hair along their road edge so the difference stays one clean ring.
            var clip = new List<IReadOnlyList<float2>>();
            foreach (var q in bands)
            {
                var inward = math.normalizesafe(q[3] - q[0]);
                clip.Add(new[] { q[0] - inward * 1f, q[1] - inward * 1f, q[2], q[3] });
            }
            List<Region> pieces;
            try { pieces = Poly.Boolean(new[] { (IReadOnlyList<float2>)region.Ring }, clip, BoolOp.Difference); }
            catch (Exception) { return null; }
            float2[] best = null; float bestArea = minArea;
            foreach (var r in pieces)
            {
                if (r?.Outer == null || r.Outer.Length < 3) continue;
                float a = Poly.Area(r.Outer);
                if (a > bestArea) { bestArea = a; best = r.Outer; }
            }
            return best == null ? null : Poly.EnsureCcw(Poly.Clean(best, 0.05f));
        }
    }
}
