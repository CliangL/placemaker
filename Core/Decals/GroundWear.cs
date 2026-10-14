// Placemaker — ground wear painted into the terrain texture (s28, phase 2 of the decal pass). GPL-3.0.
//
// User idea: instead of (or as well as) decals, use the terrain "Paint texture" materials (s13 PaintRaster:
// 1 Dry grass, 2 Lush grass, 3 Dirt, 4 Sand; soft feathered edges, per-point strength) as ground dressing:
//   Verges     faint dirt feathered 1–2 m along path edges (gradient from the path centreline)
//   Desire     faint dirt lines where people cut corners between path arms, and from the nearest path across the
//              lawn to benches, play items and picnic tables standing off the path
//   Patches    worn ground at benches, play items, picnic tables and entrances
//   Edges      speckled dry-grass / dirt variation just inside the outline, blending the build into its surroundings
// Paint shows only where the terrain itself shows: never under a planned surface (a park's grass surface, paving,
// sand…), a net or water — those spots are skipped (see Visible). Granularity is one splatmap texel (≈ 3.5 m on
// vanilla maps, smoothed by the shader), so everything here is faint and wide rather than crisp; a 1 m desire line
// reads as a soft trodden band. Amount: "Ground wear" (dressPaintWear) × style (Clean 0.6, Lived-in 1, Gritty 1.4);
// desire lines also need "Desire paths" (dressDesirePaths). Paint is permanent (TerrainPaint.PermanentKey warning,
// listed under Paint in the permanent-changes summary). Deterministic: every decision is a hash of the step seed
// and the host's position. Independent of decal packs.

using System;
using System.Collections.Generic;
using Placemaker.Core.Build;
using Placemaker.Core.Geometry;
using Placemaker.Core.Layers.Common;
using Unity.Mathematics;

namespace Placemaker.Core.Decals
{
    public static class GroundWear
    {
        /// <summary>Paints per fill step at most (keeps the raster and the permanent area small).</summary>
        public const int MaxPaints = 360;
        public const string StatKey = "Placemaker.Stat.GroundWear";

        public sealed class Report { public int Verges, Desire, Patches, Edges, Skipped; public int Total => Verges + Desire + Patches + Edges; }

        internal static float StyleK(DressStyle s) => s == DressStyle.Clean ? 0.6f : s == DressStyle.Gritty ? 1.4f : 1f;

        internal static Report Apply(ComposedPlan plan, DressingWorld w, SiteContext site, IReadOnlyDictionary<AreaPlan, DressSettings> dress)
        {
            var report = new Report();
            if (plan?.Layers == null || w?.Root == null || dress == null) return report;
            // Group the layers by the fill step's settings (features share their parent's instance).
            var groups = new Dictionary<DressSettings, List<AreaPlan>>();
            var order = new List<DressSettings>();
            foreach (var l in plan.Layers)
            {
                if (l == null || l.GeneratorId == Cutouts.ZoningLayer || !dress.TryGetValue(l, out var s) || s == null || s.PaintWear <= 0f) continue;
                if (!groups.TryGetValue(s, out var list)) { groups[s] = list = new List<AreaPlan>(); order.Add(s); }
                list.Add(l);
            }
            foreach (var s in order)
            {
                var layers = groups[s];
                AreaPlan host = layers.Find(l => l.GeneratorId == s.LayerId) ?? layers[0];
                var region = (s.Ring != null && s.Ring.Length >= 3 ? LayerRegion.From(s.Ring) : null) ?? w.Root;
                var g = new Group(w, site, host, region, s, Hashing.Mix(host.Seed == 0 ? 1u : host.Seed, 0x6A0Bu), report);
                g.Verges(layers);
                if (s.DesirePaths) g.Desire(layers);
                g.Patches(layers);
                g.Edges();
                if (g.Count > 0)
                {
                    bool warned = false;
                    foreach (var i in host.Issues) if (i.Key == TerrainPaint.PermanentKey) { warned = true; break; }
                    if (!warned) host.Issues.Add(new PlanIssue { Severity = IssueSeverity.Warning, Key = TerrainPaint.PermanentKey });
                }
            }
            return report;
        }

        private sealed class Group
        {
            private readonly DressingWorld W;
            private readonly SiteContext Site;
            private readonly AreaPlan Host;
            private readonly LayerRegion Region;
            private readonly DressSettings S;
            private readonly uint Seed;
            private readonly Report R;
            private readonly float K;
            public int Count;

            public Group(DressingWorld w, SiteContext site, AreaPlan host, LayerRegion region, DressSettings s, uint seed, Report r)
            {
                W = w; Site = site; Host = host; Region = region; S = s; Seed = seed; R = r;
                K = math.min(3f, s.PaintWear) * StyleK(s.Style);
            }

            private uint H(int a, int b = 0, int c = 0) => Hashing.Mix(Hashing.Mix(Seed, a, b), unchecked((uint)c * 0x9E3779B1u + 11u));
            private static uint H2(uint h, int k) => Hashing.Mix(h, unchecked((uint)k + 0x632BE5ABu));
            private static float U(uint h) => Hashing.Unit(h);

            /// <summary>Bare terrain shows here: inside the step, not water, not under a surface or a net, off cutouts.</summary>
            private readonly Dictionary<long, bool> _visible = new Dictionary<long, bool>();

            /// <summary>Cheapest tests first; the water test (terrain vs water level) last and memoised per 1 m cell.</summary>
            public bool Visible(float2 p)
            {
                if (!Region.Contains(p) || Region.BoundaryDistance(p, 1.2f) < 1f) return false;
                if (W.Covered(p) || W.InPoly(p, GroundKind.Paved) || W.InPoly(p, GroundKind.Sand)) return false;
                if (W.Cutouts.Count > 0 && W.NearCutout(p, 1.5f)) return false;
                long key = ((long)(int)math.floor(p.x) << 32) ^ (uint)(int)math.floor(p.y);
                if (!_visible.TryGetValue(key, out bool dry)) _visible[key] = dry = !W.IsWater(p);
                return dry;
            }

            private float HeightAt(float2 p)
            {
                try
                {
                    if (Host.HeightAfter != null) return Host.HeightAfter(p);
                    return Site?.HeightAt != null ? Site.HeightAt(p) : 0f;
                }
                catch (Exception) { return 0f; }
            }

            private bool Add(float2[] ring, int layer, float strength, float feather, float noise, float2[] gradient = null, float full = 0f, float fade = 0f)
            {
                if (Count >= MaxPaints || ring == null || ring.Length < 3 || !(strength > 0.01f)) return false;
                var p = new PlannedPaint
                {
                    Ring = Poly.To3D(ring, HeightAt), Layer = layer, Strength = math.saturate(strength),
                    Feather = math.max(0.5f, feather), Noise = noise, NoiseScale = 4f, Seed = Hashing.Mix(Seed, (uint)(Count + 1)),
                };
                if (gradient != null)
                {
                    p.GradientFrom = gradient;
                    p.GradientFull = full;
                    p.GradientFade = math.max(0.5f, fade);
                    p.GradientMin = 0f;
                }
                Host.Paint.Add(p);
                Count++;
                return true;
            }

            private static bool IsPath(NetRole r) => r == NetRole.Pathway || r == NetRole.PathwayWide;

            private static float2[] Polyline(PlannedNet n, int steps = 8)
            {
                var pts = new float2[steps + 1];
                for (int i = 0; i <= steps; i++) pts[i] = Composer.Bezier(n.A, n.B, n.C, n.D, i / (float)steps).xz;
                return pts;
            }

            /// <summary>A band of half width <paramref name="half"/> around a polyline (left side forward, right side back).</summary>
            internal static float2[] Band(IReadOnlyList<float2> line, float half)
            {
                int n = line.Count;
                if (n < 2) return null;
                var left = new float2[n]; var right = new float2[n];
                for (int i = 0; i < n; i++)
                {
                    var a = line[math.max(0, i - 1)]; var b = line[math.min(n - 1, i + 1)];
                    var t = math.normalizesafe(b - a, new float2(1, 0));
                    var nrm = new float2(-t.y, t.x);
                    var p = line[i];
                    if (i == 0) p -= t * half * 0.5f;
                    if (i == n - 1) p += t * half * 0.5f;
                    left[i] = p + nrm * half; right[i] = p - nrm * half;
                }
                var ring = new float2[2 * n];
                for (int i = 0; i < n; i++) { ring[i] = left[i]; ring[2 * n - 1 - i] = right[i]; }
                return ring;
            }

            internal static float2[] Circle(float2 c, float r, int n = 12)
            {
                var ring = new float2[n];
                for (int i = 0; i < n; i++) { float a = i * math.PI * 2f / n; ring[i] = c + new float2(math.cos(a), math.sin(a)) * r; }
                return ring;
            }

            // ───────────────────────── verges ─────────────────────────

            public void Verges(List<AreaPlan> layers)
            {
                int ni = 0;
                foreach (var l in layers)
                    foreach (var n in l.Nets)
                    {
                        ni++;
                        if (n == null || !IsPath(n.Role)) continue;
                        float hw = DressingWorld.HalfWidth(n, Site);
                        var line = Polyline(n);
                        // Only where the verge is bare terrain on at least half its length.
                        int seen = 0, ok = 0;
                        for (int i = 0; i < line.Length; i++)
                        {
                            var a = line[math.max(0, i - 1)]; var b = line[math.min(line.Length - 1, i + 1)];
                            var t = math.normalizesafe(b - a, new float2(1, 0)); var nrm = new float2(-t.y, t.x);
                            for (int side = -1; side <= 1; side += 2) { seen++; if (Visible(line[i] + nrm * side * (hw + 1.2f))) ok++; }
                        }
                        if (ok * 2 < seen) { R.Skipped++; continue; }
                        float len = 0f; for (int i = 1; i < line.Length; i++) len += math.distance(line[i - 1], line[i]);
                        if (len < 3f) continue;
                        uint h = H(ni, 1);
                        float strength = (0.12f + 0.06f * U(h)) * K;   // faint: the band runs the whole path
                        if (Add(Band(line, hw + 2.4f), TerrainMaterials.Dirt, math.min(0.4f, strength), 1.0f, 1.0f, line, hw + 0.1f, 1.8f)) R.Verges++;
                    }
            }

            // ───────────────────────── desire paths ─────────────────────────

            private bool LineClear(float2 a, float2 b, float endSlack)
            {
                float len = math.distance(a, b);
                if (len < 3f) return false;
                int steps = (int)math.ceil(len / 1.5f);
                for (int i = 0; i <= steps; i++)
                {
                    var p = math.lerp(a, b, i / (float)steps);
                    float d = math.min(i, steps - i) * len / steps;
                    if (d < endSlack) continue;
                    if (!Visible(p)) return false;
                    if (W.NetClearance(p, 1f, out _) < 0.6f) return false;
                    if (W.NearestTree(p, 1.2f, out _) < 1.0f) return false;
                }
                return true;
            }

            private void DesireLine(float2 a, float2 b, uint h)
            {
                var line = new[] { a, b };
                float strength = math.min(0.5f, (0.24f + 0.08f * U(H2(h, 7))) * K);
                if (Add(Band(line, 1.7f), TerrainMaterials.Dirt, strength, 0.8f, 0.4f, line, 0.25f, 1.2f)) R.Desire++;
            }

            public void Desire(List<AreaPlan> layers)
            {
                // Corners: path arms meeting at a junction or bend at 35–115°; people cut the corner ~7–11 m back.
                var arms = new List<(float2 at, float2[] line)>();
                var lines = new List<float2[]>();
                foreach (var l in layers)
                    foreach (var n in l.Nets)
                    {
                        if (n == null || !IsPath(n.Role)) continue;
                        var line = Polyline(n, 12);
                        lines.Add(line);
                        arms.Add((line[0], line));
                        var rev = (float2[])line.Clone(); Array.Reverse(rev);
                        arms.Add((rev[0], rev));
                    }
                var used = new bool[arms.Count];
                int corners = 0;
                for (int i = 0; i < arms.Count; i++)
                {
                    if (used[i]) continue;
                    var group = new List<int> { i };
                    used[i] = true;
                    for (int j = i + 1; j < arms.Count; j++)
                        if (!used[j] && math.distancesq(arms[i].at, arms[j].at) < 0.8f) { group.Add(j); used[j] = true; }
                    if (group.Count < 2) continue;
                    var J = arms[i].at;
                    uint hj = H((int)math.floor(J.x * 2f), (int)math.floor(J.y * 2f), 3);
                    for (int a = 0; a < group.Count; a++)
                        for (int b = a + 1; b < group.Count; b++)
                        {
                            float back = 7f + 4f * U(H2(hj, a * 8 + b));
                            if (!PointAlong(arms[group[a]].line, back, out var pa, out var ta) || !PointAlong(arms[group[b]].line, back, out var pb, out var tb)) continue;
                            float ang = math.degrees(math.acos(math.clamp(math.dot(math.normalizesafe(pa - J), math.normalizesafe(pb - J)), -1f, 1f)));
                            if (ang < 35f || ang > 115f) continue;
                            uint hc = H2(hj, 100 + a * 8 + b);
                            if (U(hc) > 0.55f * math.min(1.5f, K)) continue;
                            if (!LineClear(pa, pb, 1.6f)) continue;
                            DesireLine(pa, pb, hc);
                            corners++;
                        }
                }
                // Features off the path: a trodden line from the nearest path point to benches, play items and picnic tables.
                int oi = 0;
                foreach (var l in layers)
                    foreach (var o in l.Objects)
                    {
                        oi++;
                        if (o == null) continue;
                        string cat = RoleCategories.ForObject(o) ?? "";
                        if (!(cat.StartsWith(AssetCategories.Bench, StringComparison.Ordinal) || cat.StartsWith(AssetCategories.Playground, StringComparison.Ordinal)
                            || cat.StartsWith("play.", StringComparison.Ordinal) || cat.StartsWith(AssetCategories.Picnic, StringComparison.Ordinal))) continue;
                        var p = o.Position.xz;
                        float best = 30f; float2 q = default;
                        foreach (var line in lines)
                            for (int k = 1; k < line.Length; k++)
                            {
                                var c = Closest(line[k - 1], line[k], p);
                                float d = math.distance(c, p);
                                if (d < best) { best = d; q = c; }
                            }
                        if (best < 6f || best >= 30f) continue;
                        uint h = H(oi, 9);
                        if (U(h) > 0.35f * math.min(1.5f, K)) continue;
                        var dir = math.normalizesafe(p - q);
                        var start = q + dir * 1.5f;   // from the path edge, not its centre
                        var end = p - dir * 1.2f;     // stop short of the item (its own patch covers it)
                        if (!LineClear(start, end, 1.0f)) continue;
                        DesireLine(start, end, h);
                    }
            }

            private static float2 Closest(float2 a, float2 b, float2 p)
            {
                var ab = b - a; float l2 = math.lengthsq(ab);
                float t = l2 > 1e-6f ? math.saturate(math.dot(p - a, ab) / l2) : 0f;
                return a + ab * t;
            }

            private static bool PointAlong(float2[] line, float dist, out float2 p, out float2 t)
            {
                p = default; t = default;
                for (int i = 1; i < line.Length; i++)
                {
                    float seg = math.distance(line[i - 1], line[i]);
                    if (seg >= dist && seg > 1e-4f) { t = (line[i] - line[i - 1]) / seg; p = line[i - 1] + t * dist; return true; }
                    dist -= seg;
                }
                return false;
            }

            // ───────────────────────── patches ─────────────────────────

            public void Patches(List<AreaPlan> layers)
            {
                int oi = 0;
                foreach (var l in layers)
                    foreach (var o in l.Objects)
                    {
                        oi++;
                        if (o == null) continue;
                        string cat = RoleCategories.ForObject(o) ?? "";
                        float r; float2 c = o.Position.xz; float s;
                        if (cat.StartsWith(AssetCategories.Playground, StringComparison.Ordinal) || cat.StartsWith("play.", StringComparison.Ordinal)) { r = 3.2f; s = 0.3f; }
                        else if (cat.StartsWith(AssetCategories.Picnic, StringComparison.Ordinal)) { r = 2.6f; s = 0.24f; }
                        else if (cat.StartsWith(AssetCategories.Bench, StringComparison.Ordinal))
                        {
                            math.sincos(o.Yaw, out float sn, out float cs);
                            c += new float2(sn, cs) * 1.2f; r = 2.0f; s = 0.2f;
                        }
                        else continue;
                        if (!Visible(c)) continue;
                        uint h = H(oi, 21);
                        r *= 0.85f + 0.3f * U(h);
                        if (Add(Circle(c, r), TerrainMaterials.Dirt, math.min(0.5f, s * K), r * 0.6f, 0.7f)) R.Patches++;
                    }
                // Entrances: path ends at the outline get a trodden fan just inside.
                foreach (var l in layers)
                    foreach (var n in l.Nets)
                    {
                        if (n == null || !IsPath(n.Role)) continue;
                        foreach (var (e, inward) in new[] { (n.A.xz, n.B.xz - n.A.xz), (n.D.xz, n.C.xz - n.D.xz) })
                        {
                            if (Region.BoundaryDistance(e, 5f) > 4f) continue;
                            var dir = math.normalizesafe(inward, new float2(1, 0));
                            var nrm = new float2(-dir.y, dir.x);
                            float hw = DressingWorld.HalfWidth(n, Site);
                            uint h = H((int)math.floor(e.x * 2f), (int)math.floor(e.y * 2f), 23);
                            for (int side = -1; side <= 1; side += 2)
                            {
                                var c = e + dir * 2.5f + nrm * side * (hw + 1.4f);
                                if (!Visible(c) || U(H2(h, side + 3)) > 0.7f * math.min(1.4f, K)) continue;
                                if (Add(Circle(c, 2.2f), TerrainMaterials.Dirt, math.min(0.45f, 0.22f * K), 1.3f, 0.6f)) R.Patches++;
                            }
                        }
                    }
            }

            // ───────────────────────── edge variation ─────────────────────────

            public void Edges()
            {
                var ring = Region.Ring;
                if (ring == null || ring.Length < 3) return;
                float sign = Poly.SignedArea(ring) >= 0f ? 1f : -1f;   // CCW: inward normal is the left of each edge
                const float step = 11f;
                float carry = U(H(0, 31)) * step;
                for (int i = 0; i < ring.Length; i++)
                {
                    var a = ring[i]; var b = ring[(i + 1) % ring.Length];
                    float len = math.distance(a, b);
                    if (len < 1e-3f) continue;
                    var t = (b - a) / len; var inward = new float2(-t.y, t.x) * sign;
                    for (float d = carry; d < len; d += step)
                    {
                        uint h = H(i, (int)d, 33);
                        var c = a + t * d + inward * (3f + 3f * U(H2(h, 1)));
                        if (!Visible(c)) continue;
                        float r = 3.5f + 2.5f * U(H2(h, 2));
                        bool dirt = U(H2(h, 3)) < (S.Style == DressStyle.Gritty ? 0.45f : 0.25f);
                        float strength = (dirt ? 0.12f : 0.2f) * (0.7f + 0.6f * U(H2(h, 4))) * K;
                        if (Add(Circle(c, r, 10), dirt ? TerrainMaterials.Dirt : TerrainMaterials.Dry, math.min(0.4f, strength), r * 0.8f, 1.6f)) R.Edges++;
                    }
                    carry = (carry - len) % step; if (carry < 0f) carry += step;
                }
            }
        }
    }
}
