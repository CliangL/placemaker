// Town / market square: a paved, walkable square (the plaza's ground) with a ring of trees just
// inside the outline and benches between them, a civic centrepiece (statue, fountain or clock) in
// the middle, and a grid of market stalls — back-to-back pairs facing the aisles — filling the
// space between. Lamps along the aisles, cafe tables under the trees, bollards on the road edges.

using System.Collections.Generic;
using Placemaker.Core.Layers.Common;
using Placemaker.Core.Layers.Plaza;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Leisure
{
    [AreaGenerator(57, Group = "squares")]
    public sealed class MarketSquareLayer : LeisureLayer
    {
        public const string AccessKind = PlazaLayer.AccessKind;

        protected override string Area => "Market";
        public override string Id => "marketSquare";
        public override string Icon => "Media/Game/Icons/PropsCommercial.svg";
        public override string[] Emits => new[] { RegionTags.Paved, RegionTags.Core, StreetFrontage.ShopsTag };
        public override float MinArea => 400f;

        protected override List<FeatureInfo> BuildFeatures() => new List<FeatureInfo>
        {
            Feature("shops", ("count", 3.0), ("where", "edges")), StreetShopsFeature(Area), Feature("restrooms", ("count", 1.0)),
            Feature("decals", ("theme", "paving"), ("ground", "paved"), ("density", 0.6)), Feature("terrain", ("shape", "flatten")),
        };

        public override List<EdgeMarker> SuggestMarkers(AreaInput input)
        {
            var region = LayerRegion.From(input?.Shape?.Ring);
            return region == null ? new List<EdgeMarker>() : Entrances.Suggest(input, region, AccessKind, math.clamp((int)(region.Perimeter / 80f), 2, 8));
        }

        protected override void DefineControls(ControlSet c)
        {
            c.Slider("stalls", 16, 0, 60, 1, "count", ControlTier.Essential, "market");
            c.Slider("aisle", 4.5, 3, 8, 0.5, "m", ControlTier.Essential, "market").Tip();
            c.Toggle("treeRing", true, ControlTier.Essential, "market").Tip();
            c.Choice("centerKind", "statue", ControlTier.Essential, "market", "none", "any", "fountain", "statue");
            c.Asset("stallAsset", AssetCategories.MarketStall, ControlTier.More, "market");
            c.Slider("treeSpacing", 8, 5, 14, 1, "m", ControlTier.More, "market").Layout(5).When("treeRing=true");
            c.Slider("cafe", 2, 0, 8, 1, "count", ControlTier.More, "market");
            c.Asset("cafeAsset", AssetCategories.CafeSeating, ControlTier.More, "market");
            c.Toggle("bollards", true, ControlTier.More, "market").Tip();
            c.Asset("bollardAsset", AssetCategories.Bollard, ControlTier.More, "market").When("bollards=true");
            c.Choice("pattern", "none", ControlTier.More, "market", "none", "grid", "border", "scatter");
            c.Asset("surface", AssetCategories.SurfacePaving, ControlTier.More, "assets");
            c.Asset("centerpiece", AssetCategories.Centerpiece, ControlTier.More, "assets");
            c.Asset("treeAsset", AssetCategories.Tree, ControlTier.More, "assets");
            c.Asset("benchAsset", AssetCategories.Bench, ControlTier.More, "assets");
            c.Asset("lampAsset", AssetCategories.Lamp, ControlTier.More, "assets");
            c.Markers("access", ControlTier.More, AccessKind);
            DefineStreetShopControls(c);
            c.RandomRange("stalls", 8, 24);
            c.Fixed("centerKind");
        }

        protected override void DefinePresets()
        {
            Preset("weekly", ("stalls", 16.0), ("aisle", 4.5), ("treeRing", true), ("centerKind", "statue"));
            Preset("bigMarket", ("stalls", 44.0), ("aisle", 4.0), ("treeRing", false), ("centerKind", "none"), ("cafe", 0.0));
            Preset("farmers", ("stalls", 10.0), ("aisle", 5.5), ("treeRing", true), ("cafe", 3.0), ("centerKind", "fountain"), ("pattern", "border"));
        }

        protected override void PlanCore(LayerContext ctx)
        {
            var region = ctx.LayerRegion;
            var occ = new Occupancy(region, ctx.Input.Reserved, ctx.Site);
            var gates = Entrances.Resolve(ctx.Input, region, AccessKind, math.clamp((int)(region.Perimeter / 80f), 2, 8));
            ctx.NavigationArea(region.Ring, AreaRole.PedestrianArea, new AssetRef("", "Pedestrian Area", "Walking Area"));
            ctx.Surface(region.Ring, SurfaceRole.Plaza, AssetMix.One(ctx.S("surface"), "Pavement Surface 01"));
            foreach (var g in gates)
            {
                var inward = region.InwardNormal(g.Segment);
                ctx.Object(g.Position, math.atan2(inward.x, inward.y), ObjectRole.AccessPoint, new AssetRef("", "Pedestrian Access Location"), 0);
                occ.TakeCorridor(g.Position, g.Position + inward * 6f, 1.5f, 6);   // keep the way in clear
            }
            LeisureKit.Frame(region, out var centre, out var axis, out var side, out _, out _);
            region.DeepestPoint(out float ins, null, 12);

            // Centrepiece.
            string kind = ctx.S("centerKind");
            float coreR = 0f;
            if (kind != "none")
            {
                string sub = PlazaDressing.CenterCategory(kind);
                string cat = LeisureKit.Category(ctx, ctx.S("centerpiece"), sub ?? AssetCategories.Centerpiece, AssetCategories.Centerpiece);
                if (cat != null && ins > 10f)
                {
                    coreR = math.clamp(ins * 0.18f, 5f, 10f);
                    ctx.Object(centre, math.atan2(axis.x, axis.y), ObjectRole.Centerpiece, AssetMix.One(ctx.S("centerpiece")), Hashing.Mix(ctx.Seed, 0xCE0u), -1, cat);
                    occ.Take(centre, coreR, 0);
                }
            }

            // Tree ring with benches between the trees.
            if (ctx.B("treeRing") && AssetAvailability.Require(ctx.Plan, AssetCategories.Tree, new[] { ctx.S("treeAsset") }, Keys.Category(AssetCategories.Tree)))
            {
                var tree = AssetMix.One(ctx.S("treeAsset"));
                var bench = AssetMix.One(ctx.S("benchAsset"));
                float spacing = ctx.F("treeSpacing");
                int t = 0;
                for (float s = spacing * 0.5f; s < region.Perimeter; s += spacing)
                {
                    var b = region.PointAt(s, out var tan, out var inward, out _);
                    var p = b + inward * 3.5f;
                    if (LeisureKit.Put(ctx, occ, p, 0f, ObjectRole.Tree, tree, null, 2f, Hashing.Mix(ctx.Seed, 0x7100u + (uint)t), 2f, 0.2f, 3, 3))
                    {
                        t++;
                        var q = region.PointAt(s + spacing * 0.5f, out var tan2, out var in2, out _) + in2 * 3.5f;
                        LeisureKit.Put(ctx, occ, q, LeisureKit.Yaw(q, q + in2), ObjectRole.Bench, bench, null, 1.2f, Hashing.Mix(ctx.Seed, 0x7200u + (uint)t), 1f);
                    }
                }
                ctx.Plan.Facts["ringTrees"] = t;
            }
            ctx.Check();

            // Stall grid: back-to-back pairs (8 m deep incl. the back gap) with aisles between the pairs.
            int stalls = 0;
            int wanted = ctx.I("stalls");
            if (wanted > 0)
            {
                string cat = LeisureKit.Category(ctx, ctx.S("stallAsset"), AssetCategories.MarketStall, AssetCategories.Kiosk);
                if (cat != null)
                {
                    var asset = AssetMix.One(ctx.S("stallAsset"));
                    var pairs = LeisureKit.Pack(region, occ, centre, axis, 4f, 6.4f, 0.8f, ctx.F("aisle"), (wanted + 1) / 2, 1.5f);
                    var lamp = AssetMix.One(ctx.S("lampAsset"));
                    for (int i = 0; i < pairs.Count && stalls < wanted; i++)
                    {
                        var r = pairs[i];
                        for (int k = -1; k <= 1 && stalls < wanted; k += 2)
                        {
                            var p = r.Local(0f, k * 1.6f);
                            float yaw = math.atan2(r.Side.x * k, r.Side.y * k);   // facing its aisle
                            ctx.Object(p, yaw, ObjectRole.Furniture, asset, Hashing.Mix(ctx.Seed, 0x5700u + (uint)stalls), -1, cat);
                            stalls++;
                        }
                        if (i % 3 == 0) LeisureKit.Put(ctx, occ, r.Local(r.HalfLength + 0.6f, r.HalfWidth + 0.8f), 0f, ObjectRole.Lamp, lamp, null, 0.4f, Hashing.Mix(ctx.Seed, 0x1A00u + (uint)i), 0.5f, 0.1f);
                    }
                    if (stalls < wanted) ctx.Issue(IssueSeverity.Info, "Placemaker.Market.Issue.Fewer", stalls, wanted);
                }
            }
            ctx.Check();

            // Shared plaza dressing (same controls: cafe/cafeAsset, bollards/bollardAsset, pattern).
            PlazaDressing.Cafe(ctx, occ);
            PlazaDressing.Bollards(ctx, occ, region, gates);
            PlazaDressing.Pattern(ctx, occ, region);

            ctx.AddRegion(region.Ring, "paved", RegionTags.Paved);
            if (coreR > 0f) ctx.AddRegion(LayerRegion.Circle(centre, coreR, 20), "core", RegionTags.Core, RegionTags.Paved);
            CommonStats(ctx);
            ctx.Stat("Placemaker.Stat.Stalls", stalls, Keys.UnitCount);
        }
    }
}
