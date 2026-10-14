// Botanical garden: winding narrow paths; every lawn between them becomes one "collection" with
// its own planting character — flower beds, shrubbery, an arboretum of spaced specimen trees, or a
// grass/meadow bed on natural ground — so the garden reads as themed rooms. A glasshouse sits by
// the main path, benches are frequent, lamps optional.

using System;
using System.Collections.Generic;
using Placemaker.Core.Geometry;
using Placemaker.Core.Layers.Common;
using Placemaker.Core.Layers.Park;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Leisure
{
    [AreaGenerator(50)]
    public sealed class BotanicalGardenLayer : LeisureLayer
    {
        protected override string Area => "Botanical";
        public override string Id => "botanicalGarden";
        public override string Icon => "Media/Game/Icons/PropsPlants.svg";
        public override float MinArea => 1500f;

        private static readonly string[] Collections = { "flowers", "shrubs", "trees", "meadow" };

        protected override List<FeatureInfo> BuildFeatures() => new List<FeatureInfo>
        {
            Feature("restrooms"), Feature("fountain", ("size", "medium")), Feature("shops", ("count", 1.0)), CoreFeature("playground", ("size", "small")),
            StreetShopsFeature(Area), Feature("rocks", ("size", "small")), Feature("decals", ("theme", "leaves")), Feature("terrain", ("shape", "mounds"), ("height", 1.5)),
        };

        protected override void DefineControls(ControlSet c)
        {
            c.Choice("theme", "mixed", ControlTier.Essential, "collections", "mixed", "flowers", "trees", "shrubs").Tip();
            c.Slider("bedDensity", 90, 20, 250, 5, "perHa", ControlTier.Essential, "collections").Tip();
            c.Toggle("glasshouse", true, ControlTier.Essential, "collections").Tip();
            c.Slider("benches", 5, 0, 12, 0.5, "per100m", ControlTier.Essential, "furniture");
            c.Asset("glasshouseAsset", AssetCategories.Glasshouse, ControlTier.More, "collections").When("glasshouse=true");
            c.AssetMulti("flowerAssets", AssetCategories.Flowerbed, ControlTier.More, "collections");
            c.AssetMulti("treeSpecies", AssetCategories.Tree, ControlTier.More, "collections");
            c.AssetMulti("bushSpecies", AssetCategories.Bush, ControlTier.More, "collections");
            c.Choice("pathWidth", "narrow", ControlTier.More, "layout", "narrow", "mixed", "wide");
            c.Asset("pathAsset", AssetCategories.Pathway, ControlTier.More, "layout");
            c.Markers("entrances", ControlTier.More, EntranceKind);
            c.Toggle("lamps", true, ControlTier.More, "furniture");
            c.Asset("benchAsset", AssetCategories.Bench, ControlTier.More, "furniture");
            c.Asset("lampAsset", AssetCategories.Lamp, ControlTier.More, "furniture").When("lamps=true");
            c.Asset("surface", AssetCategories.SurfaceGrass, ControlTier.More, "ground");
            c.Toggle("fence", true, ControlTier.More, "boundary");
            c.Asset("fenceAsset", AssetCategories.Fence, ControlTier.More, "boundary").When("fence=true");
            DefineStreetShopControls(c);
            c.RandomRange("bedDensity", 50, 150);
            c.Fixed("glasshouse");
        }

        protected override void DefinePresets()
        {
            Preset("mixed", ("theme", "mixed"), ("bedDensity", 90.0));
            Preset("flowerShow", ("theme", "flowers"), ("bedDensity", 160.0), ("benches", 7.0));
            Preset("arboretum", ("theme", "trees"), ("bedDensity", 60.0), ("glasshouse", false));
            Preset("shrubbery", ("theme", "shrubs"), ("bedDensity", 140.0));
        }

        protected override void PlanCore(LayerContext ctx)
        {
            var region = ctx.LayerRegion;
            var occ = new Occupancy(region, ctx.Input.Reserved, ctx.Site);
            var gates = Gates(ctx);
            int loops = math.max(1, ParkPathPlanner.LoopsByArea(region.Area) + 1);
            NaturalPaths(ctx, occ, gates, loops, 5.5f, 16f, ctx.S("pathWidth"), AssetMix.One(ctx.S("pathAsset")), true);
            ctx.Check();

            // Glasshouse beside the main path, near the middle.
            if (ctx.B("glasshouse"))
            {
                string cat = LeisureKit.Category(ctx, ctx.S("glasshouseAsset"), AssetCategories.Glasshouse, AssetCategories.Shelter);
                if (cat != null)
                {
                    var asset = AssetMix.One(ctx.S("glasshouseAsset"));
                    float r = AssetMix.FootprintRadius(ctx.Site, asset, 8f);
                    if (Spots.Find(occ, r + 1f, Spots.Preference.NearPaths, Hashing.Mix(ctx.Seed, 0x6145u), out var spot, null, 0f, 2f, budget: 1500))
                    {
                        var toward = occ.Paths.Nearest(spot, 300f, out var q) >= 0 ? q : region.Centroid;
                        LeisureKit.Put(ctx, occ, spot, LeisureKit.Yaw(spot, toward), ObjectRole.Furniture, asset, cat, r, Hashing.Mix(ctx.Seed, 0x6146u), 1f, 0.5f, 0);
                    }
                    else ctx.Issue(IssueSeverity.Info, "Placemaker.Botanical.Issue.NoGlasshouse");
                }
            }

            // Furniture before planting (benches every N metres, lamps).
            PathBenches(ctx, occ, 100f / math.max(0.5f, ctx.F("benches")), AssetMix.One(ctx.S("benchAsset")), 0xBE70u);
            if (ctx.B("lamps")) PathLamps(ctx, occ, 22f, AssetMix.One(ctx.S("lampAsset")), 0x1A70u);
            ctx.Check();

            // Collections: one theme per lawn.
            var lawns = FreeSpace.Pieces(region, p => occ.Corridors.Overlaps(p, 0.75f), 2f, 80f, ctx.Cancel);
            string theme = ctx.S("theme");
            float density = ctx.F("bedDensity");
            var flowers = ctx.L("flowerAssets"); var trees = ctx.L("treeSpecies"); var bushes = ctx.L("bushSpecies");
            bool canFlowers = AssetAvailability.Has(AssetCategories.Flowerbed) || flowers.Length > 0;
            var counts = new Dictionary<string, int>();
            for (int i = 0; i < lawns.Count; i++)
            {
                var lawn = LayerRegion.From(lawns[i]);
                if (lawn == null) continue;
                string kind = theme == "mixed" ? Collections[(int)(Hashing.Mix(ctx.Seed, (uint)i + 0xC0u) % (uint)Collections.Length)] : theme;
                if (theme != "mixed" && i % 4 == 3) kind = "meadow";   // even a themed garden breathes
                if (kind == "flowers" && !canFlowers) kind = "shrubs";
                float ha = lawn.Area / 10000f;
                uint salt = 0x1000u + (uint)i * 16u;
                int n = 0;
                switch (kind)
                {
                    case "flowers":
                        n = LeisureKit.Plant(ctx, occ, lawn, (int)(ha * density * 1.6f), ObjectRole.Planter, flowers, 1.1f, 6f, 0.5f, salt, null, 1, 3, AssetCategories.Flowerbed);
                        LeisureKit.Plant(ctx, occ, lawn, (int)(ha * density * 0.4f), ObjectRole.Bush, bushes, 1f, 4f, 0.3f, salt + 1u,
                            p => lawn.BoundaryDistance(p, 4f) > 3f);
                        break;
                    case "shrubs":
                        n = LeisureKit.Plant(ctx, occ, lawn, (int)(ha * density * 2f), ObjectRole.Bush, bushes, 1f, 8f, 0.35f, salt);
                        LeisureKit.Plant(ctx, occ, lawn, (int)(ha * 8f), ObjectRole.Tree, trees, 2.5f, 20f, 0.6f, salt + 1u);
                        break;
                    case "trees":
                        // Arboretum: specimen trees with room around each (radius 5), mature.
                        n = LeisureKit.Plant(ctx, occ, lawn, (int)math.max(1f, ha * density * 0.5f), ObjectRole.Tree, trees, 5f, 30f, 0.9f, salt, null, 2, 3);
                        break;
                    default:
                    {
                        var c = lawn.DeepestPoint(out float ins, null, 10);
                        if (ins > 4f) ctx.Surface(LeisureKit.Blob(c, math.min(ins - 1.5f, 14f), Hashing.Mix(ctx.Seed, salt), 16, 0.18f), SurfaceRole.Decorative,
                            AssetMix.One(""), AssetCategories.SurfaceNatural);
                        n = LeisureKit.Plant(ctx, occ, lawn, (int)(ha * density * 0.5f), ObjectRole.Bush, bushes, 0.9f, 5f, 0.2f, salt);
                        break;
                    }
                }
                counts[kind] = (counts.TryGetValue(kind, out var k) ? k : 0) + 1;
                ctx.Check();
            }
            foreach (var kv in counts) ctx.Plan.Facts["collection." + kv.Key] = kv.Value;

            ctx.Surface(region.Ring, SurfaceRole.Grass, AssetMix.One(ctx.S("surface")));
            if (ctx.B("fence")) Fence(ctx, gates, AssetMix.One(ctx.S("fenceAsset")));
            EmitGreenRegions(ctx, occ);
            CommonStats(ctx);
            ctx.Stat(Keys.StatFlowerbeds, Count(ctx.Plan, ObjectRole.Planter), Keys.UnitCount);
            ctx.Stat(Keys.StatBushes, Count(ctx.Plan, ObjectRole.Bush), Keys.UnitCount);
        }
    }
}
