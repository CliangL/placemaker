// Placemaker — our roads zone only where the build has shops (core, GPL-3.0).
//
// The user (playtest 12:12, docs/teardown/00): "the shops got put on the wrong side ... what if the streets themselves in whatever
// you're touching could turn off their zoning". CS2 1.6 has that flag built in: CompositionFlags.Side.ZonesDisabled in the edge's
// vanilla Upgraded component (the "Road Zones" upgrade). Game.Zones.ZoneToggleJob blocks every cell of a disabled side's blocks
// before the overlap pass, so they claim nothing (docs/teardown/08 §4.1). It is plain vanilla data: saved with the city, works
// without the mod, removable with the vanilla tool.
//
// This pass gives every road of the plan that carries zone blocks the flag on each side where no shop plot of the build faces it:
// the lot side of a ring lane, the park side of a plaza lane, both sides of a pure access lane. Only sides whose blocks lie mostly
// inside the build (the side of a lane along the outline that faces the neighbours is left alone), and only in plans that zone
// plots at all. Loose builds (road grids, which pre-zone on purpose) are never touched.
//
// Side frame (ZoneToggleJob): x = edge start→end, a block on the side where Right(x) = (x.y, -x.x) points away from it is "left"
// (m_Left), the other "right" (m_Right). Our edges keep the course direction A→D; a point p is on the right when
// dot(p − curve, Right(tangent)) > 0.

using System;
using System.Collections.Generic;
using Placemaker.Core.Geometry;
using Unity.Mathematics;

namespace Placemaker.Core.Build
{
    public static class LaneSides
    {
        public const string LeftOff = "left:ZonesDisabled";
        public const string RightOff = "right:ZonesDisabled";
        /// <summary>Zone block depth (6 cells of 8 m).</summary>
        public const float BlockDepth = 48f;
        /// <summary>A side is switched off only when at least this share of its block band lies inside the build's footprint.</summary>
        public const float InsideShare = 0.5f;

        /// <summary>Per net: which sides were switched off (for the log and tests).</summary>
        public struct Result
        {
            public int Nets, LeftOff, RightOff, Kept;
        }

        private static float2 Right(float2 d) => new float2(d.y, -d.x);

        /// <summary>The closest point of a planned net to <paramref name="p"/>: its distance and the side (+1 right, −1 left).</summary>
        public static float SideOf(PlannedNet n, float2 p, out int side)
        {
            float best = float.MaxValue, bt = 0f;
            for (int k = 0; k <= 32; k++)
            {
                float t = k / 32f;
                float d = math.distancesq(Composer.Bezier(n.A, n.B, n.C, n.D, t).xz, p);
                if (d < best) { best = d; bt = t; }
            }
            var at = Composer.Bezier(n.A, n.B, n.C, n.D, bt).xz;
            float e = 1e-3f;
            var tan = Composer.Bezier(n.A, n.B, n.C, n.D, math.min(1f, bt + e)).xz - Composer.Bezier(n.A, n.B, n.C, n.D, math.max(0f, bt - e)).xz;
            if (math.lengthsq(tan) < 1e-12f) tan = n.D.xz - n.A.xz;
            side = math.dot(p - at, Right(math.normalizesafe(tan))) >= 0f ? 1 : -1;
            return math.sqrt(best);
        }

        private static float NetDistance(float3 a, float3 b, float3 c, float3 d, float2 p)
        {
            float best = float.MaxValue;
            for (int k = 0; k <= 16; k++) best = math.min(best, math.distance(Composer.Bezier(a, b, c, d, k / 16f).xz, p));
            return best;
        }

        /// <summary>
        /// Adds "left:ZonesDisabled" / "right:ZonesDisabled" to the plan's zoning roads (see the file comment). Returns what it did.
        /// <paramref name="halfWidth"/> gives a net's half width (ZoneGuard.NetWidth / 2 in the planner).
        /// </summary>
        public static Result Apply(ComposedPlan plan, SiteContext site, Func<PlannedNet, float> halfWidth = null)
        {
            var res = new Result();
            if (plan?.Layers == null || plan.Footprints == null || plan.Footprints.Count == 0) return res;
            // The build's shop plots (the zoning quads as the layers planned them).
            var plots = new List<float2[]>();
            foreach (var l in plan.Layers)
                if (l?.Zoning != null)
                    foreach (var z in l.Zoning) if (z != null) plots.Add(Poly.EnsureCcw(new[] { z.A.xz, z.B.xz, z.C.xz, z.D.xz }));
            if (plots.Count == 0) return res;
            halfWidth = halfWidth ?? (n => ZoneGuard.NetWidth(n, site) * 0.5f);

            // Every road that can own a plot: the plan's zoning roads and the existing roads around.
            var ours = new List<(AreaPlan layer, PlannedNet net)>();
            foreach (var l in plan.Layers)
            {
                if (l?.Nets == null || GeneratorRegistry.IsLoose(l.GeneratorId)) continue;
                foreach (var n in l.Nets)
                {
                    if (n == null || !ZoneBlockPredictor.Zones(n.Role)) continue;
                    if (site?.Assets != null && !string.IsNullOrEmpty(n.Asset.Id) && site.Assets.TryGetValue(n.Asset.Id, out var ai) && ai.ZoneBlocks == 2) continue;
                    ours.Add((l, n));
                }
            }
            if (ours.Count == 0) return res;
            var city = new List<NearbyNet>();
            if (site?.NearbyNets != null) foreach (var n in site.NearbyNets) if (n.Category == NetCategory.Road) city.Add(n);

            // Which of our nets (and on which side) each plot faces. Exactly: the commercial kit plans a lane row on that lane's own
            // (predicted) cells, so a plot that is a rectangle of one side's cells belongs to it. Otherwise the nearest road edge, our
            // lane winning ties within a cell (the kit plans rows to face its lanes; a row backing onto a city street is the lane's).
            var cells = new List<(PlannedNet n, int side)>();
            var grids = new List<ZoneGrid>();
            foreach (var (_, n) in ours)
                foreach (var g in ZoneBlockPredictor.Blocks(n.A, n.B, n.C, n.D, halfWidth(n) * 2f))
                {
                    SideOf(n, g.Position, out int side);
                    cells.Add((n, side));
                    grids.Add(g);
                }
            var served = new Dictionary<PlannedNet, int>();   // bit 1 = right, bit 2 = left
            void Serve(PlannedNet n, int side)
            {
                served.TryGetValue(n, out int bits);
                served[n] = bits | (side > 0 ? 1 : 2);
            }
            foreach (var quad in plots)
            {
                int gi = ZoneGuard.AlignedGrid(quad, grids);
                if (gi >= 0) { Serve(cells[gi].n, cells[gi].side); continue; }
                var p = Poly.Centroid(quad);
                PlannedNet best = null; float bestD = float.MaxValue; int bestSide = 0;
                foreach (var (_, n) in ours)
                {
                    float d = SideOf(n, p, out int side) - halfWidth(n);
                    if (d < bestD) { bestD = d; best = n; bestSide = side; }
                }
                float cityD = float.MaxValue;
                foreach (var c in city) cityD = math.min(cityD, NetDistance(c.A, c.B, c.C, c.D, p) - c.Width * 0.5f);
                if (best == null || bestD > BlockDepth || bestD > cityD + ZoneGuard.Cell) continue;
                Serve(best, bestSide);
            }

            foreach (var (_, n) in ours)
            {
                res.Nets++;
                served.TryGetValue(n, out int bits);
                float hw = halfWidth(n);
                var add = new List<string>();
                if ((bits & 1) == 0 && MostlyInside(n, +1, hw, plan.Footprints)) add.Add(RightOff);
                if ((bits & 2) == 0 && MostlyInside(n, -1, hw, plan.Footprints)) add.Add(LeftOff);
                if (add.Count == 0) { res.Kept++; continue; }
                var list = new List<string>(n.Upgrades ?? Array.Empty<string>());
                foreach (var a in add)
                {
                    if (list.Contains(a)) continue;
                    list.Add(a);
                    if (a == RightOff) res.RightOff++; else res.LeftOff++;
                }
                n.Upgrades = list.ToArray();
            }
            return res;
        }

        /// <summary>Does the band of a side's zone blocks (half width … half width + 48 m) lie mostly inside the footprints?</summary>
        private static bool MostlyInside(PlannedNet n, int side, float hw, List<float2[]> feet)
        {
            int inside = 0, total = 0;
            for (int k = 1; k < 8; k++)
            {
                float t = k / 8f;
                var at = Composer.Bezier(n.A, n.B, n.C, n.D, t).xz;
                var tan = Composer.Bezier(n.A, n.B, n.C, n.D, math.min(1f, t + 0.01f)).xz - Composer.Bezier(n.A, n.B, n.C, n.D, math.max(0f, t - 0.01f)).xz;
                if (math.lengthsq(tan) < 1e-10f) continue;
                var r = Right(math.normalizesafe(tan)) * side;
                foreach (float off in new[] { hw + 4f, hw + 20f, hw + 36f })
                {
                    total++;
                    var p = at + r * off;
                    foreach (var f in feet) if (Poly.Contains(f, p)) { inside++; break; }
                }
            }
            return total > 0 && inside >= InsideShare * total;
        }
    }
}
