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
    [AreaGenerator(50, Group = "gardens")]
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
            c.Slider("benches", 3, 0, 12, 0.5, "per100m", ControlTier.Essential, "furniture");
            c.Asset("glasshouseAsset", AssetCategories.Glasshouse, ControlTier.More, "collections").When("glasshouse=true");
            c.AssetMulti("flowerAssets", AssetCategories.Flowerbed, ControlTier.More, "collections");
            c.AssetMulti("treeSpecies", AssetCategories.Tree, ControlTier.More, "collections");
            c.AssetMulti("bushSpecies", AssetCategories.Bush, ControlTier.More, "collections");
            c.Choice("pathWidth", "narrow", ControlTier.More, "layout", "narrow", "mixed", "wide");
            c.Asset("pathAsset", AssetCategories.Pathway, ControlTier.More, "layout");
            EntranceMarkers(c);
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
            Preset("flowerShow", ("theme", "flowers"), ("bedDensity", 160.0), ("benches", 4.0));
            Preset("arboretum", ("theme", "trees"), ("bedDensity", 90.0), ("glasshouse", false), ("benches", 2.5));
            Preset("shrubbery", ("theme", "shrubs"), ("bedDensity", 140.0));
        }

        // ───────────── art direction (art pass a3) ─────────────
        // Explicit vanilla assets while the asset controls are automatic. An arboretum is a collection: every
        // vanilla broadleaf and conifer, one specimen each in turn. The fence pool is a 2 m residential fence, so
        // the boundary is a low hedge; the only vanilla glasshouses are small, so they stand as a range of three.

        private static readonly string[] Arboretum =
        {
            "OakTree01", "NA_LondonPlaneTree01", "EU_ChestnutTree01", "NA_HickoryTree01", "BirchTree01", "SpruceTree01", "NA_LindenTree01",
            "EU_AlderTree01", "PineTree01", "EU_PoplarTree01", "EuropeanSpruce01", "AppleTree01",
        };
        private static readonly string[] AccentTrees = { "NA_LindenTree01", "AppleTree01", "BirchTree01" };
        private static readonly string[] FlowerBeds = { "Flowerbed01", "Flowerbed02" };
        private static readonly string[] FlowerShrubs = { "FlowerBushCity01", "FlowerBushCity02", "FlowerBushCity03", "FlowerBushWild01", "FlowerBushWild02" };
        private static readonly string[] Shrubbery = { "GreenBushWild02", "GreenBushWildAdult01", "GreenBushWildElderly01", "FlowerBushWildElderly01", "GreenBushWild01" };
        private static readonly string[] MeadowFlowers = { "FlowerBushWild01", "FlowerBushWild02", "FlowerBushWildChild02", "GreenBushWildChild01" };
        private static readonly string[] Glasshouses = { "Greenhouse02", "Greenhouse03", "Greenhouse01" };
        private static readonly string[] GardenBenches = { "GardenBench01", "GardenBench02", "GardenBench03" };
        private static readonly string[] GardenLamps = { "EU_LightpolePark03", "LightpolePark03" };
        private static readonly string[] BoundaryHedge = { "HedgeLow01", "HedgeLow02", "EU_FenceResidentialLow03" };

        private static string[] Or(string[] picked, string[] curated) => picked != null && picked.Length > 0 ? picked : curated;
        private static AssetRef OneOr(string picked, string[] curated) => string.IsNullOrEmpty(picked) ? AssetMix.Pick(curated, 0u) : AssetMix.One(picked);

        protected override void PlanCore(LayerContext ctx)
        {
            var region = ctx.LayerRegion;
            var occ = new Occupancy(region, ctx.Input.Reserved, ctx.Site);
            var gates = Gates(ctx);
            int loops = math.max(2, ParkPathPlanner.LoopsByArea(region.Area) + 2);   // +2: garden rooms need paths on every side (art pass a3)
            NaturalPaths(ctx, occ, gates, loops, 5.5f, 16f, ctx.S("pathWidth"), AssetMix.One(ctx.S("pathAsset")), true);
            ctx.Check();

            // Glasshouse beside the main path, near the middle.
            if (ctx.B("glasshouse"))
            {
                string cat = LeisureKit.Category(ctx, ctx.S("glasshouseAsset"), AssetCategories.Glasshouse, AssetCategories.Shelter);
                if (cat != null)
                {
                    bool picked = !string.IsNullOrEmpty(ctx.S("glasshouseAsset"));
                    var asset = OneOr(ctx.S("glasshouseAsset"), Glasshouses);
                    float r = AssetMix.FootprintRadius(ctx.Site, asset, 8f);
                    // The vanilla glasshouses are garden-sized (~2.5 m): three side by side make a glasshouse range.
                    int range = picked ? 1 : 3;
                    float pitch = r * 1.6f + 0.6f, need = range == 1 ? r + 1f : pitch * 1.5f + r;
                    if (Spots.Find(occ, need, Spots.Preference.NearPaths, Hashing.Mix(ctx.Seed, 0x6145u), out var spot, null, 0f, 2f, budget: 1500))
                    {
                        var toward = occ.Paths.Nearest(spot, 300f, out var q) >= 0 ? q : region.Centroid;
                        var face = math.normalizesafe(toward - spot, new float2(0, 1));
                        var along = new float2(face.y, -face.x);
                        for (int k = 0; k < range; k++)
                        {
                            var p = spot + along * ((k - (range - 1) * 0.5f) * pitch);
                            var a = range == 1 ? asset : AssetMix.Pick(Glasshouses, (uint)k);
                            LeisureKit.Put(ctx, occ, p, LeisureKit.Yaw(p, p + face), ObjectRole.Furniture, a, cat, r, Hashing.Mix(ctx.Seed, 0x6146u + (uint)k), 1f, 0.3f, 0);
                        }
                    }
                    else ctx.Issue(IssueSeverity.Info, "Placemaker.Botanical.Issue.NoGlasshouse");
                }
            }

            // Furniture before planting (benches every N metres, lamps).
            PathBenches(ctx, occ, 100f / math.max(0.5f, ctx.F("benches")), OneOr(ctx.S("benchAsset"), GardenBenches), 0xBE70u);
            if (ctx.B("lamps")) PathLamps(ctx, occ, 22f, OneOr(ctx.S("lampAsset"), GardenLamps), 0x1A70u);
            ctx.Check();

            // Collections: one theme per lawn.
            var lawns = FreeSpace.Pieces(region, p => occ.Corridors.Overlaps(p, 0.75f), 2f, 80f, ctx.Cancel);
            string theme = ctx.S("theme");
            float density = ctx.F("bedDensity");
            var flowers = Or(ctx.L("flowerAssets"), AssetAvailability.Has(AssetCategories.Flowerbed) ? FlowerBeds : new string[0]);
            var pickedTrees = ctx.L("treeSpecies"); var pickedBushes = ctx.L("bushSpecies");
            bool vanillaTrees = AssetAvailability.Has(AssetCategories.Tree), vanillaBushes = AssetAvailability.Has(AssetCategories.Bush);
            string[] Trees(string[] curated) => pickedTrees.Length > 0 ? pickedTrees : vanillaTrees ? curated : pickedTrees;
            string[] Bushes(string[] curated) => pickedBushes.Length > 0 ? pickedBushes : vanillaBushes ? curated : pickedBushes;
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
                        LeisureKit.Plant(ctx, occ, lawn, (int)(ha * density * 0.6f), ObjectRole.Bush, Bushes(FlowerShrubs), 1f, 4f, 0.3f, salt + 1u,
                            p => lawn.BoundaryDistance(p, 4f) > 3f);
                        break;
                    case "shrubs":
                        n = LeisureKit.Plant(ctx, occ, lawn, (int)(ha * density * 2f), ObjectRole.Bush, Bushes(Shrubbery), 1f, 8f, 0.35f, salt);
                        LeisureKit.Plant(ctx, occ, lawn, (int)(ha * 8f), ObjectRole.Tree, Trees(AccentTrees), 2.5f, 20f, 0.6f, salt + 1u);
                        break;
                    case "trees":
                        // Arboretum: specimen trees with room around each (radius 4.5), mature, every species in turn.
                        n = LeisureKit.Plant(ctx, occ, lawn, (int)math.max(1f, ha * density * 0.8f), ObjectRole.Tree, Trees(Arboretum), 4.5f, 30f, 0.9f, salt, null, 2, 3);
                        break;
                    default:
                    {
                        var c = lawn.DeepestPoint(out float ins, null, 10);
                        // Wildflower meadow: a longer-grass bed (the natural-surface pool starts with sand) sown with wild flowers.
                        if (ins > 4f) ctx.Surface(LeisureKit.Blob(c, math.min(ins - 1.5f, 14f), Hashing.Mix(ctx.Seed, salt), 16, 0.18f), SurfaceRole.Decorative,
                            new AssetRef("", "Grass Surface 02"), AssetCategories.SurfaceGrass);
                        n = LeisureKit.Plant(ctx, occ, lawn, (int)(ha * density * 0.7f), ObjectRole.Bush, Bushes(MeadowFlowers), 0.9f, 5f, 0.2f, salt);
                        break;
                    }
                }
                counts[kind] = (counts.TryGetValue(kind, out var k) ? k : 0) + 1;
                ctx.Check();
            }
            foreach (var kv in counts) ctx.Plan.Facts["collection." + kv.Key] = kv.Value;

            Lawn.Cover(ctx, region.Ring, OneOr(ctx.S("surface"), new[] { "Grass Surface 01" }));
            if (ctx.B("fence")) Fence(ctx, gates, OneOr(ctx.S("fenceAsset"), BoundaryHedge));
            EmitGreenRegions(ctx, occ);
            CommonStats(ctx);
            ctx.Stat(Keys.StatFlowerbeds, Count(ctx.Plan, ObjectRole.Planter), Keys.UnitCount);
            ctx.Stat(Keys.StatBushes, Count(ctx.Plan, ObjectRole.Bush), Keys.UnitCount);
        }
    }
}
