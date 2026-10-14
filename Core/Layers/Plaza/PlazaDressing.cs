// Plaza depth (playtest 2: "just a concrete slab with a thing in the middle"). Everything here is
// composed from existing assets by category with graceful degradation (a missing category = an Info
// issue and that element skipped). Category per element: docs/proposals/parkplaza.md.
//
//   Levels       terraced up / down, sunken, raised (TerrainShaper, permanent, blended at the outline);
//                ramps = short paths from each access point inward; planters line the level edges.
//   Amphitheatre stage at the middle of the longest edge, bench rows on arcs facing it, aisles,
//                rows optionally raised (bowl terraform limited to the seating sector).
//   Green        lawn islands (grass surface blobs with a bush rim), flowerbed clusters, planters
//                along the edges, trees (edge ring / grid / groves), grass or gravel border strips.
//   Water        reflecting pool (water-look surface; real water only on flat ground, opt-in,
//                permanent), splash jets.
//   Shade        shelters (pergola, canopy, gazebo).
//   Activity     cafe table clusters, market stall grid, games corner, performance spot.
//   Identity     centrepiece kind, paving pattern decals, feature lighting, bollards at road edges.

using System;
using System.Collections.Generic;
using Placemaker.Core.Geometry;
using Placemaker.Core.Layers.Common;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Plaza
{
    internal static class PlazaDressing
    {
        private const string K = "Placemaker.Plaza.";

        // ───────────── levels ─────────────

        public static void Levels(LayerContext ctx, Occupancy occ, LayerRegion region, List<Gate> gates)
        {
            string mode = ctx.S("levels");
            if (mode == "flat" || string.IsNullOrEmpty(mode)) return;
            region.DeepestPoint(out float inscribed, null, 12);
            int count = ctx.I("levelCount");
            float falloff = math.clamp(inscribed * 0.25f, 4f, 16f);
            var spec = new TerrainSpec
            {
                Mode = mode == "sunken" ? TerrainMode.Sunken : mode == "raised" ? TerrainMode.Raised : TerrainMode.Terraces,
                Down = mode == "terracedDown", Height = ctx.F("levelHeight"), Levels = count, MaxSlopePct = 35f, Falloff = falloff,
                Seed = Hashing.Mix(ctx.Seed, 0x1E7Eu),
            };
            var result = TerrainShaper.Shape(ctx, region, spec);
            if (result.Cells == 0) return;
            // Ramps: a short path from each access point inward across the level edges.
            var ramp = AssetMix.One("");
            foreach (var g in gates)
            {
                var inward = region.InwardNormal(g.Segment);
                float len = math.min(inscribed * 0.7f, 28f);
                var end = g.Position + inward * len;
                if (!region.ContainsDisc(end, 1f)) continue;
                LeisureKit.Path(ctx, occ, g.Position, end, NetRole.PathwayWide, ramp);
            }
            if (!ctx.B("levelPlanters")) return;
            // Planters line each level edge (inset rings), leaving the ramps open.
            int levels = spec.Mode == TerrainMode.Terraces ? count : 2;
            float band = math.max(4f, (inscribed - falloff * 0.5f) / levels);
            string cat = LeisureKit.Category(ctx, ctx.S("planterAsset"), AssetCategories.Planter, AssetCategories.Bush);
            if (cat == null) return;
            var asset = AssetMix.One(ctx.S("planterAsset"));
            var role = cat == AssetCategories.Bush ? ObjectRole.Bush : ObjectRole.Planter;
            int placed = 0;
            for (int k = 1; k < levels; k++)
            {
                float d = falloff * 0.5f + band * k;
                if (d > inscribed - 2f) break;
                var ring = region.InsetVertices(d);
                if (!Poly.IsSimple(ring) || Poly.SignedArea(ring) <= 0f) continue;
                var r = LayerRegion.From(ring);
                if (r == null) continue;
                for (float s = 0; s < r.Perimeter; s += 5f)
                {
                    var p = r.PointAt(s, out var t, out _, out _);
                    if (LeisureKit.Put(ctx, occ, p, math.atan2(t.x, t.y), role, asset, cat, 0.9f, Hashing.Mix(ctx.Seed, 0x1E00u + (uint)placed), 0.5f, 0.6f))
                        placed++;
                }
            }
        }

        // ───────────── amphitheatre & performance ─────────────

        public static void Amphitheatre(LayerContext ctx, Occupancy occ, LayerRegion region, bool keepCentre, float centreClear)
        {
            if (!ctx.B("amphitheatre")) return;
            int best = 0; float len = 0;
            for (int i = 0; i < region.Ring.Length; i++)
            {
                float l = math.distance(region.Ring[i], region.Ring[(i + 1) % region.Ring.Length]);
                if (l > len) { len = l; best = i; }
            }
            var inward = region.InwardNormal(best);
            var focal = math.lerp(region.Ring[best], region.Ring[(best + 1) % region.Ring.Length], 0.5f) + inward * 5f;
            if (!region.ContainsDisc(focal, 3f)) { ctx.Issue(IssueSeverity.Info, K + "Issue.NoAmphitheatre"); return; }
            var centre = region.Centroid;
            string stageCat = LeisureKit.Category(ctx, ctx.S("stageAsset"), AssetCategories.Stage, AssetCategories.Shelter, AssetCategories.Centerpiece);
            float stageR = 3.5f;
            if (stageCat != null)
            {
                var role = stageCat == AssetCategories.Centerpiece ? ObjectRole.Centerpiece : ObjectRole.Furniture;
                LeisureKit.Put(ctx, occ, focal, math.atan2(inward.x, inward.y), role, AssetMix.One(ctx.S("stageAsset")), stageCat, stageR, Hashing.Mix(ctx.Seed, 0x57A6u), 0.5f, 0.3f, 0);
            }
            int rows = ctx.I("rows");
            float r0 = stageR + 5f, spacing = 3.4f;
            float baseAngle = math.atan2(inward.y, inward.x);
            const float halfSpan = 1.15f;   // ≈ ±66°
            var bench = AssetMix.One(ctx.S("benchAsset"));
            int benches = 0;
            float rMax = r0;
            for (int row = 0; row < rows; row++)
            {
                float r = r0 + row * spacing;
                float step = 2.9f / r;
                for (float a = -halfSpan; a <= halfSpan + 1e-3f; a += step)
                {
                    // Radial aisles: centre and both thirds.
                    if (math.abs(a) < 0.08f || math.abs(math.abs(a) - halfSpan * 0.55f) < 0.06f) continue;
                    var p = focal + new float2(math.cos(baseAngle + a), math.sin(baseAngle + a)) * r;
                    if (keepCentre && math.distance(p, centre) < centreClear) continue;
                    if (LeisureKit.Put(ctx, occ, p, LeisureKit.Yaw(p, focal), ObjectRole.Bench, bench, null, 1.2f,
                        Hashing.Mix(ctx.Seed, 0xA4F0u + (uint)benches), 0.8f, 0.15f, 1)) { benches++; rMax = r; }
                }
            }
            if (benches == 0) { ctx.Issue(IssueSeverity.Info, K + "Issue.NoAmphitheatre"); return; }
            ctx.Stat("Placemaker.Stat.Seats", benches, Keys.UnitCount);
            if (!ctx.B("amphiRaise")) return;
            float reach = rMax + 2f;
            TerrainShaper.Shape(ctx, region, new TerrainSpec
            {
                Mode = TerrainMode.Bowl, Focal = focal, RowSpacing = spacing, Height = math.min(4f, rows * 0.6f), MaxSlopePct = 40f, Falloff = 4f,
                Skip = p =>
                {
                    var d = p - focal; float dist = math.length(d);
                    if (dist > reach || dist < r0 - 2f) return true;
                    float ang = math.atan2(d.y, d.x) - baseAngle;
                    while (ang > math.PI) ang -= 2f * math.PI;
                    while (ang < -math.PI) ang += 2f * math.PI;
                    return math.abs(ang) > halfSpan + 0.15f;
                },
            });
        }

        public static void PerformanceSpot(LayerContext ctx, Occupancy occ, LayerRegion region)
        {
            if (!ctx.B("performance")) return;
            if (!Spots.Find(occ, 8f, Spots.Preference.NearBoundary, Hashing.Mix(ctx.Seed, 0x9E7Fu), out var c, null, 0f, 1f, budget: 1500)) return;
            var b = region.ClosestOnBoundary(c, out _, out _);
            var toEdge = math.normalizesafe(b - c, new float2(0, 1));
            var stage = c + toEdge * 4f;
            string cat = LeisureKit.Category(ctx, ctx.S("stageAsset"), AssetCategories.Stage, AssetCategories.Centerpiece);
            if (cat != null)
                LeisureKit.Put(ctx, occ, stage, LeisureKit.Yaw(stage, c), cat == AssetCategories.Centerpiece ? ObjectRole.Centerpiece : ObjectRole.Furniture,
                    AssetMix.One(ctx.S("stageAsset")), cat, 2.5f, Hashing.Mix(ctx.Seed, 0x9E80u), 0.3f, 0.2f, 0);
            var bench = AssetMix.One(ctx.S("benchAsset"));
            float baseA = math.atan2(-toEdge.y, -toEdge.x);
            for (int i = -2; i <= 2; i++)
            {
                var p = stage + new float2(math.cos(baseA + i * 0.42f), math.sin(baseA + i * 0.42f)) * 7f;
                LeisureKit.Put(ctx, occ, p, LeisureKit.Yaw(p, stage), ObjectRole.Bench, bench, null, 1.2f, Hashing.Mix(ctx.Seed, 0x9E90u + (uint)(i + 3)));
            }
        }

        // ───────────── water ─────────────

        public static void Pool(LayerContext ctx, Occupancy occ, LayerRegion region, float2 avoid, float avoidRadius)
        {
            if (!ctx.B("pool")) return;
            LeisureKit.Frame(region, out var centre, out var axis, out _, out float major, out float minor);
            float halfL = math.clamp(major * 0.18f, 4f, 22f), halfW = math.clamp(minor * 0.08f, 1.5f, 5f);
            OrientedRect best = default; bool found = false;
            foreach (float shift in new[] { 0.22f, -0.22f, 0.35f, -0.35f, 0f })
            {
                var r = new OrientedRect { Center = centre + axis * (major * shift), Axis = axis, HalfLength = halfL, HalfWidth = halfW };
                if (avoidRadius > 0f && math.distance(r.Center, avoid) < avoidRadius + halfL) continue;
                if (!LeisureKit.RectFits(region, occ, r, 2f)) continue;
                best = r; found = true; break;
            }
            if (!found) { ctx.Issue(IssueSeverity.Info, K + "Issue.NoPool"); return; }
            LeisureKit.TakeRect(occ, best);
            bool real = ctx.B("realWater");
            if (real)
            {
                float lo = float.MaxValue, hi = float.MinValue;
                foreach (var q in best.Ring) { float h = ctx.H(q); lo = math.min(lo, h); hi = math.max(hi, h); }
                if (hi - lo > 0.5f) { real = false; ctx.Issue(IssueSeverity.Info, K + "Issue.PoolNotFlat"); }
                else
                {
                    float level = lo - 0.2f;
                    ctx.Plan.Terraform.Add(new PlannedTerraform
                    {
                        Ring = Poly.To3D(best.Ring, ctx.H), Kind = TerraformKind.Lower, TargetHeight = level - 0.6f, Permanent = true,
                        WaterLevel = level, WaterAnchor = new float3(best.Center.x, level, best.Center.y),
                    });
                    ctx.Issue(IssueSeverity.Warning, K + "Issue.PoolPermanent");
                }
            }
            if (!real)
            {
                string cat = LeisureKit.Category(ctx, ctx.S("poolSurface"), AssetCategories.SurfaceWater, AssetCategories.SurfacePaving);
                if (cat != null) ctx.Surface(best.Ring, SurfaceRole.Decorative, AssetMix.One(ctx.S("poolSurface")), cat);
            }
            // Benches along both long sides, looking at the water.
            var bench = AssetMix.One(ctx.S("benchAsset"));
            int i = 0;
            foreach (int side in new[] { -1, 1 })
                for (float u = -halfL + 3f; u <= halfL - 3f; u += 8f)
                {
                    var p = best.Local(u, side * (halfW + 2.2f));
                    LeisureKit.Put(ctx, occ, p, LeisureKit.Yaw(p, best.Local(u, 0f)), ObjectRole.Bench, bench, null, 1.2f, Hashing.Mix(ctx.Seed, 0x9001u + (uint)i++));
                }
        }

        public static void Jets(LayerContext ctx, Occupancy occ)
        {
            if (!ctx.B("jets")) return;
            if (!AssetAvailability.Require(ctx.Plan, AssetCategories.WaterJets, new[] { ctx.S("jetAsset") }, Keys.Category(AssetCategories.WaterJets))) return;
            if (!Spots.Find(occ, 6f, Spots.Preference.Open, Hashing.Mix(ctx.Seed, 0x5E75u), out var c, null, 0f, 1f, budget: 1500)) return;
            var asset = AssetMix.One(ctx.S("jetAsset"));
            int n = 0;
            for (float y = -4.5f; y <= 4.5f; y += 1.8f)
                for (float x = -4.5f; x <= 4.5f; x += 1.8f)
                {
                    var p = c + new float2(x, y);
                    if (math.length(new float2(x, y)) > 4.6f) continue;
                    ctx.Object(p, 0f, ObjectRole.Furniture, asset, Hashing.Mix(ctx.Seed, (uint)n + 0x5E00u), -1, AssetCategories.WaterJets);
                    n++;
                }
            occ.Take(c, 5f, 2);
        }

        // ───────────── green ─────────────

        public static void Lawns(LayerContext ctx, Occupancy occ, LayerRegion region)
        {
            float pct = ctx.F("lawns");
            if (pct <= 0f) return;
            float target = region.Area * pct / 100f, made = 0f;
            var bush = AssetMix.One(ctx.S("bushAsset"));
            int i = 0;
            while (made < target && i < 24)
            {
                float r = math.clamp(math.sqrt((target - made) / math.PI), 4f, 11f);
                if (!Spots.Find(occ, r + 1f, Spots.Preference.Open, Hashing.Mix(ctx.Seed, 0x1A00u + (uint)i), out var c, null, 0f, 2f, budget: 1500))
                {
                    if (r <= 4.5f) break;
                    r = 4f;
                    if (!Spots.Find(occ, r + 1f, Spots.Preference.Open, Hashing.Mix(ctx.Seed, 0x1A80u + (uint)i), out c, null, 0f, 2f, budget: 1500)) break;
                }
                uint v = Hashing.Mix(ctx.Seed, 0x1B00u + (uint)i);
                ctx.Surface(LeisureKit.Blob(c, r, v, 18, 0.12f), SurfaceRole.Grass, AssetMix.One(""), AssetCategories.SurfaceGrass);
                // A bush rim on part of the island and a tree in the bigger ones.
                for (int k = 0; k < 6; k++)
                {
                    float a = Hashing.Unit(Hashing.Mix(v, (uint)k)) * 6.283f;
                    var p = c + new float2(math.cos(a), math.sin(a)) * (r - 1.2f);
                    ctx.Object(p, a, ObjectRole.Bush, bush, Hashing.Mix(v, (uint)k + 9u));
                }
                if (r >= 7f) ctx.Object(c, 0f, ObjectRole.Tree, AssetMix.One(ctx.S("treeAsset")), v, 3);
                occ.Take(c, r, 2);
                made += math.PI * r * r;
                i++;
            }
        }

        public static void Beds(LayerContext ctx, Occupancy occ)
        {
            int clusters = ctx.I("beds");
            if (clusters <= 0) return;
            string cat = LeisureKit.Category(ctx, ctx.S("flowerbedAsset"), AssetCategories.Flowerbed, AssetCategories.Planter);
            if (cat == null) return;
            var asset = AssetMix.One(ctx.S("flowerbedAsset"));
            var spots = new List<float2>();
            for (int i = 0; i < clusters; i++)
            {
                if (!Spots.Find(occ, 3.2f, Spots.Preference.NearBoundary, Hashing.Mix(ctx.Seed, 0xBED0u + (uint)i), out var c, spots, 9f, 1f, budget: 1500)) break;
                spots.Add(c);
                for (int k = 0; k < 4; k++)
                {
                    float a = k * 1.5708f + Hashing.Unit(ctx.Seed) * 0.5f;
                    var p = c + new float2(math.cos(a), math.sin(a)) * 1.8f;
                    LeisureKit.Put(ctx, occ, p, a, ObjectRole.Planter, asset, cat, 1.1f, Hashing.Mix(ctx.Seed, (uint)(i * 8 + k) + 0xBE00u), 0.3f, 0.1f);
                }
            }
        }

        public static void EdgePlanters(LayerContext ctx, Occupancy occ, LayerRegion region, List<Gate> gates)
        {
            int count = ctx.I("planters");
            if (count <= 0) return;
            string cat = LeisureKit.Category(ctx, ctx.S("planterAsset"), AssetCategories.Planter, AssetCategories.Bush);
            if (cat == null) return;
            var asset = AssetMix.One(ctx.S("planterAsset"));
            float step = region.Perimeter / count;
            int placed = 0;
            for (int i = 0; i < count; i++)
            {
                var b = region.PointAt(step * (i + 0.5f), out var t, out var inward, out _);
                if (NearGate(b, gates, 5f)) continue;
                var p = b + inward * 2.2f;
                if (LeisureKit.Put(ctx, occ, p, math.atan2(t.x, t.y), cat == AssetCategories.Bush ? ObjectRole.Bush : ObjectRole.Planter, asset, cat, 0.9f,
                    Hashing.Mix(ctx.Seed, 0xED00u + (uint)i), 0.5f, 0.2f)) placed++;
            }
        }

        public static void Trees(LayerContext ctx, Occupancy occ, LayerRegion region, List<Gate> gates, float2 centre, float coreRadius)
        {
            string layout = ctx.S("treeLayout");
            if (layout == "none" || string.IsNullOrEmpty(layout)) return;
            if (!AssetAvailability.Require(ctx.Plan, AssetCategories.Tree, new[] { ctx.S("treeAsset") }, Keys.Category(AssetCategories.Tree))) return;
            var tree = AssetMix.One(ctx.S("treeAsset"));
            int n = 0;
            void Tree(float2 p)
            {
                if (math.distance(p, centre) < coreRadius + 2f || NearGate(p, gates, 6f)) return;
                if (LeisureKit.Put(ctx, occ, p, Hashing.Unit(Hashing.Mix(ctx.Seed, (uint)n)) * 6.28f, ObjectRole.Tree, tree, null, 2.2f,
                    Hashing.Mix(ctx.Seed, 0x7E00u + (uint)n), 1f, 0.3f, 3, 3)) n++;
            }
            switch (layout)
            {
                case "edge":
                    for (float s = 4f; s < region.Perimeter; s += 9f)
                    {
                        var b = region.PointAt(s, out _, out var inward, out _);
                        Tree(b + inward * 4f);
                    }
                    break;
                case "grid":
                {
                    LeisureKit.Frame(region, out var c, out var axis, out var side, out _, out _);
                    var size = region.Max - region.Min;
                    float reach = math.length(size);
                    for (float u = -reach; u <= reach; u += 8f)
                        for (float v = -reach; v <= reach; v += 8f)
                        {
                            var p = c + axis * u + side * v;
                            if (!region.Contains(p) || region.BoundaryDistance(p, 20f) > math.max(14f, coreRadius * 0.5f)) continue;
                            Tree(p);
                        }
                    break;
                }
                default:   // groves
                    for (int g = 0; g < 4; g++)
                    {
                        if (!Spots.Find(occ, 7f, Spots.Preference.NearBoundary, Hashing.Mix(ctx.Seed, 0x6200u + (uint)g), out var c, null, 0f, 1f, budget: 1500)) break;
                        for (int k = 0; k < 5; k++)
                        {
                            float a = k * 1.2566f + g;
                            Tree(c + new float2(math.cos(a), math.sin(a)) * (k == 0 ? 0f : 4.5f));
                        }
                    }
                    break;
            }
            ctx.Plan.Facts["plazaTrees"] = n;
        }

        public static void Border(LayerContext ctx, LayerRegion region)
        {
            string border = ctx.S("border");
            if (border == "none" || string.IsNullOrEmpty(border)) return;
            string cat = border == "grass" ? AssetCategories.SurfaceGrass : AssetCategories.SurfaceNatural;
            var role = border == "grass" ? SurfaceRole.Grass : SurfaceRole.Decorative;
            foreach (var strip in region.BoundaryStrips(3f, math.clamp((int)(region.Perimeter / 120f), 2, 8), ctx.Cancel))
                ctx.Surface(strip, role, AssetMix.One(""), cat);
        }

        // ───────────── shade & activity ─────────────

        public static void Shade(LayerContext ctx, Occupancy occ)
        {
            int n = ctx.I("shade");
            if (n <= 0) return;
            string cat = LeisureKit.Category(ctx, ctx.S("shelterAsset"), AssetCategories.Shelter);
            if (cat == null) return;
            var asset = AssetMix.One(ctx.S("shelterAsset"));
            float r = AssetMix.FootprintRadius(ctx.Site, asset, 4f);
            var spots = new List<float2>();
            for (int i = 0; i < n; i++)
            {
                if (!Spots.Find(occ, r + 1f, Spots.Preference.NearBoundary, Hashing.Mix(ctx.Seed, 0x5AD0u + (uint)i), out var c, spots, r * 4f, 1f, budget: 1500)) break;
                spots.Add(c);
                var b = occ.LayerRegion.ClosestOnBoundary(c, out _, out _);
                LeisureKit.Put(ctx, occ, c, LeisureKit.Yaw(b, c), ObjectRole.Furniture, asset, cat, r, Hashing.Mix(ctx.Seed, 0x5A00u + (uint)i), 0.5f, 0.3f, 0);
            }
        }

        public static void Cafe(LayerContext ctx, Occupancy occ)
        {
            int n = ctx.I("cafe");
            if (n <= 0) return;
            string cat = LeisureKit.Category(ctx, ctx.S("cafeAsset"), AssetCategories.CafeSeating, AssetCategories.Picnic);
            if (cat == null) return;
            var asset = AssetMix.One(ctx.S("cafeAsset"));
            var spots = new List<float2>();
            for (int i = 0; i < n; i++)
            {
                if (!Spots.Find(occ, 4.5f, Spots.Preference.NearBoundary, Hashing.Mix(ctx.Seed, 0xCAF0u + (uint)i), out var c, spots, 12f, 1f, budget: 1500)) break;
                spots.Add(c);
                for (int k = 0; k < 4; k++)
                {
                    var p = c + new float2(k % 2 == 0 ? -1.9f : 1.9f, k < 2 ? -1.9f : 1.9f);
                    LeisureKit.Put(ctx, occ, p, Hashing.Unit(Hashing.Mix(ctx.Seed, (uint)(i * 4 + k))) * 1.57f, ObjectRole.Furniture, asset, cat, 1.2f,
                        Hashing.Mix(ctx.Seed, 0xCA00u + (uint)(i * 4 + k)), 0.5f, 0.15f);
                }
            }
        }

        public static void Stalls(LayerContext ctx, Occupancy occ, LayerRegion region)
        {
            int n = ctx.I("stalls");
            if (n <= 0) return;
            string cat = LeisureKit.Category(ctx, ctx.S("stallAsset"), AssetCategories.MarketStall, AssetCategories.Kiosk);
            if (cat == null) return;
            var asset = AssetMix.One(ctx.S("stallAsset"));
            LeisureKit.Frame(region, out var c, out var axis, out var side, out _, out _);
            // Rows of stalls back to back, aisles (5 m) between the pairs: the stalls face the aisles.
            var rects = LeisureKit.Pack(region, occ, c, axis, 4f, 3f, 1.2f, 0.4f, n, 1.5f);
            if (rects.Count == 0) { ctx.Issue(IssueSeverity.Info, K + "Issue.NoStalls"); return; }
            for (int i = 0; i < rects.Count; i++)
            {
                var r = rects[i];
                var d = r.Center - c;
                int row = (int)math.round(math.dot(d, side) / 3.4f);
                float yaw = math.atan2(side.x, side.y) + ((row & 1) == 0 ? 0f : math.PI);
                ctx.Object(r.Center, yaw, ObjectRole.Furniture, asset, Hashing.Mix(ctx.Seed, 0x57A0u + (uint)i), -1, cat);
            }
            ctx.Stat("Placemaker.Stat.Stalls", rects.Count, Keys.UnitCount);
        }

        public static void Games(LayerContext ctx, Occupancy occ)
        {
            if (!ctx.B("games")) return;
            // Round 5: real chess / game tables first (games.chess), then cafe tables, then picnic tables.
            string cat = LeisureKit.Category(ctx, "", AssetCategories.GamesChess, AssetCategories.CafeSeating, AssetCategories.Picnic);
            if (cat == null) return;
            if (!Spots.Find(occ, 5f, Spots.Preference.NearBoundary, Hashing.Mix(ctx.Seed, 0x6A3Eu), out var c, null, 0f, 1f, budget: 1500)) return;
            for (int k = 0; k < 4; k++)
            {
                var p = c + new float2(k % 2 == 0 ? -2.3f : 2.3f, k < 2 ? -2.3f : 2.3f);
                LeisureKit.Put(ctx, occ, p, 0f, ObjectRole.Furniture, AssetMix.One(""), cat, 1.1f, Hashing.Mix(ctx.Seed, 0x6A00u + (uint)k), 0.5f, 0.2f);
            }
        }

        // ───────────── identity ─────────────

        public static string CenterCategory(string kind) =>
            kind == "fountain" ? AssetCategories.CenterpieceFountain : kind == "statue" ? AssetCategories.CenterpieceStatue : null;

        public static void Lighting(LayerContext ctx, Occupancy occ, LayerRegion region, List<Gate> gates, float2 centre, float coreRadius)
        {
            if (ctx.S("lighting") != "feature") return;
            var lamp = AssetMix.One(ctx.S("lampAsset"));
            int n = 0;
            float ring = math.max(5f, coreRadius + 2.5f);
            for (int i = 0; i < 8; i++)
            {
                float a = i * math.PI / 4f + 0.2f;
                var p = centre + new float2(math.cos(a), math.sin(a)) * ring;
                if (LeisureKit.Put(ctx, occ, p, a, ObjectRole.Lamp, lamp, null, 0.5f, Hashing.Mix(ctx.Seed, 0x1160u + (uint)i), 0.5f, 0.2f)) n++;
            }
            for (float s = 6f; s < region.Perimeter; s += 15f)
            {
                var b = region.PointAt(s, out _, out var inward, out _);
                if (NearGate(b, gates, 3f)) continue;
                if (LeisureKit.Put(ctx, occ, b + inward * 1.5f, 0f, ObjectRole.Lamp, lamp, null, 0.5f, Hashing.Mix(ctx.Seed, 0x1170u + (uint)n), 0.5f, 0.2f)) n++;
            }
        }

        public static void Bollards(LayerContext ctx, Occupancy occ, LayerRegion region, List<Gate> gates)
        {
            if (!ctx.B("bollards")) return;
            var edges = StreetFrontage.Edges(region.Ring, StreetFrontage.Roads(ctx.Input));
            if (edges.Count == 0) { ctx.Issue(IssueSeverity.Info, K + "Issue.NoRoadEdge"); return; }
            if (!AssetAvailability.Require(ctx.Plan, AssetCategories.Bollard, new[] { ctx.S("bollardAsset") }, Keys.Category(AssetCategories.Bollard))) return;
            var asset = AssetMix.One(ctx.S("bollardAsset"));
            int n = 0;
            foreach (var (a, b) in edges)
            {
                var dir = math.normalizesafe(b - a);
                var inward = new float2(-dir.y, dir.x);
                foreach (var p0 in LeisureKit.Along(a, b, 2.5f))
                {
                    var p = p0 + inward * 0.8f;
                    if (LeisureKit.Put(ctx, occ, p, math.atan2(dir.x, dir.y), ObjectRole.Furniture, asset, AssetCategories.Bollard, 0.3f,
                        Hashing.Mix(ctx.Seed, 0xB011u + (uint)n), 0.3f, 0.2f)) n++;
                }
            }
        }

        public static void Pattern(LayerContext ctx, Occupancy occ, LayerRegion region)
        {
            string pattern = ctx.S("pattern");
            if (pattern == "none" || string.IsNullOrEmpty(pattern)) return;
            if (!AssetAvailability.Require(ctx.Plan, AssetCategories.DecalPaving, null, Keys.Category(AssetCategories.DecalPaving))) return;
            LeisureKit.Frame(region, out var c, out var axis, out var side, out _, out _);
            float yaw = math.atan2(axis.x, axis.y);
            int n = 0;
            void Decal(float2 p, float y)
            {
                if (n >= 1500 || !region.ContainsDisc(p, 0.5f) || occ.Discs.Overlaps(p, 0.6f, 0f)) return;
                ctx.Object(p, y, ObjectRole.Decal, AssetMix.One(""), Hashing.Mix(ctx.Seed, 0xDEC0u + (uint)n), -1, AssetCategories.DecalPaving);
                n++;
            }
            switch (pattern)
            {
                case "grid":
                {
                    float reach = math.length(region.Max - region.Min);
                    for (float u = -reach; u <= reach; u += 5f)
                        for (float v = -reach; v <= reach; v += 5f)
                            Decal(c + axis * u + side * v, yaw);
                    break;
                }
                case "border":
                    for (float s = 0; s < region.Perimeter; s += 2.5f)
                    {
                        var b = region.PointAt(s, out var t, out var inward, out _);
                        Decal(b + inward * 2f, math.atan2(t.x, t.y));
                    }
                    break;
                default:   // scatter
                    foreach (var p in Scatter.Candidates(region, 4f, Hashing.Mix(ctx.Seed, 0xDE5Cu), math.min(1500, (int)(region.Area / 30f)), ctx.Cancel))
                        Decal(p, yaw + (Hashing.Mix(ctx.Seed, (int)p.x, (int)p.y) % 4u) * 1.5708f);
                    break;
            }
        }

        private static bool NearGate(float2 p, List<Gate> gates, float d)
        {
            foreach (var g in gates) if (math.distancesq(g.Position, p) < d * d) return true;
            return false;
        }
    }
}
