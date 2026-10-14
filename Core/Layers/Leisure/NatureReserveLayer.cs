// Nature reserve / forest: dense natural planting (trees in clumps of mixed ages, an understorey of
// bushes), narrow meandering trails only, a few clearings with a bench each, a lookout beside the
// trail at the highest point, wildlife spawners and painted forest floor. No lamps, few benches.

using System.Collections.Generic;
using Placemaker.Core.Layers.Common;
using Placemaker.Core.Layers.Park;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Leisure
{
    [AreaGenerator(54, Group = "nature")]
    public sealed class NatureReserveLayer : LeisureLayer
    {
        protected override string Area => "Reserve";
        public override string Id => "natureReserve";
        public override string Icon => "Media/Game/Icons/Trees.svg";
        public override float MinArea => 2000f;

        protected override List<FeatureInfo> BuildFeatures() => new List<FeatureInfo>
        {
            Feature("rocks", ("clusters", 4.0), ("size", "large")), Feature("picnic", ("groups", 2.0)), Feature("restrooms", ("byArea", true), ("haEach", 8.0)),
            Feature("decals", ("theme", "leaves"), ("density", 1.0)), Feature("terrain", ("shape", "hills"), ("height", 3.0)),
        };

        protected override void DefineControls(ControlSet c)
        {
            c.Slider("treeDensity", 220, 60, 450, 10, "perHa", ControlTier.Essential, "planting").Tip();
            c.Slider("bushDensity", 140, 0, 400, 10, "perHa", ControlTier.Essential, "planting");
            c.Slider("clearings", 2, 0, 8, 1, "count", ControlTier.Essential, "trails").Tip();
            c.Toggle("lookout", true, ControlTier.Essential, "trails").Tip();
            c.Asset("lookoutAsset", AssetCategories.Lookout, ControlTier.More, "trails").When("lookout=true");
            c.Asset("trailAsset", AssetCategories.Pathway, ControlTier.More, "trails");
            c.Slider("benches", 1, 0, 5, 0.5, "per100m", ControlTier.More, "trails");
            c.Toggle("animals", true, ControlTier.More, "planting").Tip();
            c.Toggle("forestFloor", true, ControlTier.More, "planting").Tip();
            c.Slider("floorLayer", 1, 1, 4, 1, "count", ControlTier.More, "planting").When("forestFloor=true");
            c.AssetMulti("treeSpecies", AssetCategories.Tree, ControlTier.More, "planting");
            c.AssetMulti("bushSpecies", AssetCategories.Bush, ControlTier.More, "planting");
            EntranceMarkers(c);
            DefineStreetShopControls(c);
            c.RandomRange("treeDensity", 120, 300);
            c.Fixed("forestFloor");
        }

        protected override void DefinePresets()
        {
            Preset("forest", ("treeDensity", 300.0), ("bushDensity", 120.0), ("clearings", 2.0));
            Preset("heath", ("treeDensity", 80.0), ("bushDensity", 300.0), ("clearings", 4.0));
            Preset("wildwood", ("treeDensity", 420.0), ("bushDensity", 200.0), ("clearings", 1.0), ("benches", 0.5));
        }

        protected override void PlanCore(LayerContext ctx)
        {
            var region = ctx.LayerRegion;
            var occ = new Occupancy(region, ctx.Input.Reserved, ctx.Site);
            var gates = Gates(ctx);
            ctx.Surface(region.Ring, SurfaceRole.Grass, AssetMix.One(""));
            int loops = math.max(1, ParkPathPlanner.LoopsByArea(region.Area));
            NaturalPaths(ctx, occ, gates, loops, 7f, 15f, "narrow", AssetMix.One(ctx.S("trailAsset")), region.Area > 20000f);
            ctx.Check();

            // Lookout beside the trail at the highest point.
            if (ctx.B("lookout"))
            {
                string cat = LeisureKit.Category(ctx, ctx.S("lookoutAsset"), AssetCategories.Lookout, AssetCategories.Shelter);
                if (cat != null)
                {
                    var asset = AssetMix.One(ctx.S("lookoutAsset"));
                    float r = AssetMix.FootprintRadius(ctx.Site, asset, 4f);
                    var anchors = PathAnchors.Beside(occ, r, 8f, 1.2f);
                    int best = -1; float hi = float.MinValue;
                    for (int i = 0; i < anchors.Count; i++)
                    {
                        float h = ctx.H(anchors[i].Position) + region.BoundaryDistance(anchors[i].Position, 30f) * 0.01f;
                        if (h > hi) { hi = h; best = i; }
                    }
                    if (best >= 0)
                        LeisureKit.Put(ctx, occ, anchors[best].Position, LeisureKit.Yaw(anchors[best].Position, anchors[best].Toward), ObjectRole.Furniture, asset, cat, r,
                            Hashing.Mix(ctx.Seed, 0x100Cu), 0.5f, 0.3f, 0);
                }
            }

            // Clearings: open meadows with a bench, kept free of trees.
            var clearings = new List<(float2 c, float r)>();
            var bench = AssetMix.One("");
            for (int i = 0; i < ctx.I("clearings"); i++)
            {
                float r = 9f + 6f * Hashing.Unit(Hashing.Mix(ctx.Seed, (uint)i + 0xC1Eu));
                var avoid = new List<float2>();
                foreach (var cl in clearings) avoid.Add(cl.c);
                if (!Spots.Find(occ, r, Spots.Preference.NearPaths, Hashing.Mix(ctx.Seed, 0xC1E0u + (uint)i), out var c, avoid, r * 3f, 2f, budget: 1500)) break;
                clearings.Add((c, r));
                var toPath = occ.Paths.Nearest(c, 300f, out var q) >= 0 ? q : region.Centroid;
                var b = c + math.normalizesafe(toPath - c) * (r * 0.5f);
                ctx.Object(b, LeisureKit.Yaw(b, c), ObjectRole.Bench, bench, Hashing.Mix(ctx.Seed, 0xBE0u + (uint)i));
                occ.Take(c, r, 200);
            }
            PathBenches(ctx, occ, 100f / math.max(0.25f, ctx.F("benches")), bench, 0xBE20u);
            ctx.Check();

            // Dense planting: trees in clumps of mixed age, an understorey of bushes.
            var treeIds = ctx.L("treeSpecies"); var bushIds = ctx.L("bushSpecies");
            float ha = region.Area / 10000f;
            int trees = math.min(12000, (int)(ha * ctx.F("treeDensity")));
            int bushes = math.min(12000, (int)(ha * ctx.F("bushDensity")));
            if (trees > 0 && AssetAvailability.Require(ctx.Plan, AssetCategories.Tree, treeIds, Keys.Category(AssetCategories.Tree)))
                LeisureKit.Plant(ctx, occ, region, trees, ObjectRole.Tree, treeIds, 2.2f, 26f, 0.45f, 0xF0Eu, null, 0, 3);
            ctx.Check();
            if (bushes > 0 && AssetAvailability.Require(ctx.Plan, AssetCategories.Bush, bushIds, Keys.Category(AssetCategories.Bush)))
                LeisureKit.Plant(ctx, occ, region, bushes, ObjectRole.Bush, bushIds, 0.9f, 10f, 0.3f, 0xB05u);
            ctx.Check();

            if (ctx.B("animals"))
            {
                int n = math.clamp((int)(ha * 1.5f), 1, 12);
                foreach (var s in Spread(occ, 3f, n, 0x5A3u, 30f))
                    ctx.Object(s, 0f, ObjectRole.AnimalSpawner, new AssetRef("", "Forest Animal Spawner"), Hashing.Mix(ctx.Seed, (uint)(s.x * 3f)));
            }
            if (ctx.B("forestFloor"))
            {
                // Paint the woodland floor in the big gaps between clearings (interior only).
                var inset = region.InsetVertices(math.min(12f, math.sqrt(region.Area) * 0.08f));
                if (Geometry.Poly.IsSimple(inset) && Geometry.Poly.SignedArea(inset) > 200f) TerrainPaint.Patch(ctx, inset, ctx.I("floorLayer"), 0.7f, 6f);
            }

            // Clearings are the regions features use (core = the largest).
            if (clearings.Count > 0)
            {
                clearings.Sort((a, b) => b.r.CompareTo(a.r));
                for (int i = 0; i < clearings.Count; i++)
                    if (i == 0) ctx.AddRegion(LayerRegion.Circle(clearings[i].c, clearings[i].r, 20), "green:0", RegionTags.Green, RegionTags.Core);
                    else ctx.AddRegion(LayerRegion.Circle(clearings[i].c, clearings[i].r, 20), "green:" + i, RegionTags.Green);
            }
            else EmitGreenRegions(ctx, occ);
            CommonStats(ctx);
            ctx.Stat(Keys.StatBushes, Count(ctx.Plan, ObjectRole.Bush), Keys.UnitCount);
            ctx.Plan.Facts["clearings"] = clearings.Count;
        }

        private static List<float2> Spread(Occupancy occ, float radius, int count, uint seed, float minDistance)
        {
            var r = new List<float2>();
            for (int i = 0; i < count; i++)
            {
                if (!Spots.Find(occ, radius, Spots.Preference.Open, Hashing.Mix(seed, (uint)i), out var s, r, minDistance, budget: 1500)) break;
                r.Add(s);
            }
            return r;
        }
    }
}
