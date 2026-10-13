// Park layer: paths, lawn, planting, furniture, optional fence / lake / wildlife.
// Planners ported from ParkManager (tomkpunkt, GPL-3.0); vegetation scattering merged from
// ParkingLotTool (KruemelGames, GPL-3.0). See the individual files for details.
//
// Regions for children:
//   green:N  lawns between paths (largest first), tags {green}
//   green:0  additionally tagged {core} — the largest lawn, which holds a protected clearing
//   edge:N   boundary belt strips, tags {edge}

using System;
using System.Collections.Generic;
using Placemaker.Core.Geometry;
using Placemaker.Core.Layers.Common;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Park
{
    [AreaGenerator(30)]
    public sealed class ParkLayer : LayerBase
    {
        public const string LayerId = "park";
        public const string EntranceKind = "entrance";
        public const float BeltDepth = 8f;

        protected override string Area => "Park";
        public override string Id => LayerId;
        public override string Icon => "Media/Game/Icons/ParksAndRecreation.svg";
        public override LayerKind Kind => LayerKind.Fill;
        public override string[] Emits => new[] { RegionTags.Green, RegionTags.Core, RegionTags.Edge };
        public override float MinArea => 150f;

        protected override void DefineControls(ControlSet c)
        {
            // Essentials (DESIGN.md §2: style preset, path style, planting density, furniture).
            c.Choice("style", "natural", ControlTier.Essential, "layout", "natural", "formal", "woodland", "meadow", "pocket");
            c.Choice("pathWidth", "mixed", ControlTier.Essential, "layout", "narrow", "mixed", "wide").Tip();
            c.Slider("treeDensity", 26, 0, 150, 1, "perHa", ControlTier.Essential, "planting").Tip();
            c.Slider("furniture", 3, 0, 10, 0.5, "per100m", ControlTier.Essential, "furniture").Tip();
            c.Asset("surface", AssetCategories.SurfaceGrass, ControlTier.Essential, "ground");
            c.RandomRange("treeDensity", 10, 60);
            c.RandomRange("furniture", 1.5, 5);

            // More options.
            c.Choice("paths", "auto", ControlTier.More, "layout", "auto", "none", "minimal", "loop", "network").Tip();
            c.Toggle("curvedPaths", true, ControlTier.More, "layout");
            c.Asset("pathAsset", AssetCategories.Pathway, ControlTier.More, "layout");
            c.Markers("entrances", ControlTier.More, EntranceKind).Tip();
            c.Slider("bushDensity", 43, 0, 250, 1, "perHa", ControlTier.More, "planting");
            c.AssetMulti("treeSpecies", AssetCategories.Tree, ControlTier.More, "planting");
            c.AssetMulti("bushSpecies", AssetCategories.Bush, ControlTier.More, "planting");
            c.Toggle("belt", true, ControlTier.More, "planting").Tip();
            c.Toggle("lamps", true, ControlTier.More, "furniture");
            c.Toggle("bins", true, ControlTier.More, "furniture");
            c.Asset("benchAsset", AssetCategories.Bench, ControlTier.More, "furniture");
            c.Asset("lampAsset", AssetCategories.Lamp, ControlTier.More, "furniture").When("lamps=true");
            c.Asset("binAsset", AssetCategories.Bin, ControlTier.More, "furniture").When("bins=true");
            c.Toggle("groundSurface", true, ControlTier.More, "ground");
            c.Toggle("fence", false, ControlTier.More, "boundary");
            c.Asset("fenceAsset", AssetCategories.Fence, ControlTier.More, "boundary").When("fence=true");
            c.Toggle("animals", false, ControlTier.More, "planting").Tip();
            c.Toggle("lake", false, ControlTier.More, "water").Tip();
            c.Slider("lakeSize", 70, 20, 100, 5, "%", ControlTier.More, "water").When("lake=true");
            c.Chance("fence", 0.2f);
            c.Fixed("lake");            // permanent terrain change: never switched on by a dice roll
            c.Fixed("lakeSize");
            c.Chance("animals", 0.3f);
            c.Chance("lamps", 0.85f);
            c.Chance("bins", 0.9f);
            c.Chance("belt", 0.8f);
            c.Chance("curvedPaths", 0.8f);
            c.Chance("groundSurface", 0.95f);
            c.RandomRange("bushDensity", 15, 90);
        }

        protected override void DefinePresets()
        {
            Preset("natural", ("style", "natural"), ("treeDensity", 26.0), ("bushDensity", 43.0), ("furniture", 3.0));
            Preset("formal", ("style", "formal"), ("pathWidth", "wide"), ("curvedPaths", false), ("treeDensity", 30.0),
                ("bushDensity", 60.0), ("furniture", 4.5), ("paths", "network"));
            Preset("woodland", ("style", "woodland"), ("pathWidth", "narrow"), ("treeDensity", 80.0), ("bushDensity", 90.0),
                ("furniture", 1.5), ("animals", true), ("lamps", false));
            Preset("meadow", ("style", "meadow"), ("pathWidth", "narrow"), ("treeDensity", 8.0), ("bushDensity", 20.0),
                ("furniture", 2.0), ("paths", "minimal"));
            Preset("pocket", ("style", "pocket"), ("treeDensity", 40.0), ("bushDensity", 60.0), ("furniture", 6.0),
                ("paths", "minimal"));
        }

        public override List<EdgeMarker> SuggestMarkers(AreaInput input)
        {
            var region = LayerRegion.From(input?.Shape?.Ring);
            if (region == null) return new List<EdgeMarker>();
            return Entrances.Suggest(input, region, EntranceKind, Entrances.WantedByArea(region.Area));
        }

        /// <summary>Always ready: without player entrances the plan uses suggested ones (never block the player).</summary>
        public override Readiness CheckReady(AreaInput input) => new Readiness { Ready = true };

        protected override void PlanCore(LayerContext ctx)
        {
            var region = ctx.LayerRegion;
            var style = ParkStyle.Get(ctx.S("style"));
            var s = new ParkSite
            {
                LayerRegion = region, Occ = new Occupancy(region, ctx.Input.Reserved, ctx.Site),
                Seed = ctx.Seed, Cancel = ctx.Cancel,
            };

            // 1. Entrances and paths.
            string pathMode = ctx.S("paths");
            if (pathMode != "none")
            {
                s.Gates = Entrances.Resolve(ctx.Input, region, EntranceKind, Entrances.WantedByArea(region.Area));
                var gatePoints = new List<float2>();
                foreach (var g in s.Gates) gatePoints.Add(g.Position);
                var options = new ParkPathPlanner.Options { Loops = LoopsFor(pathMode, region.Area), StretchLoop = pathMode == "network" || pathMode == "auto" && region.Area > 12000f };
                s.Graph = ParkPathPlanner.PlanBest(region, gatePoints, options, Hashing.Mix(ctx.Seed, 0xA11u), ctx.Cancel);
                // Formal parks are axial by definition; every other style follows the toggle.
                bool curved = ctx.B("curvedPaths") && !style.Formal;
                s.Curves = ParkPaths.Build(s.Graph, region, curved, ctx.S("pathWidth"));
                ParkPaths.Index(s.Curves, s.Occ.Corridors, 100);
            }
            ctx.Check();

            // 2. Optional lake (permanent terraform, off by default).
            if (ctx.B("lake"))
            {
                s.Lake = ParkLakePlanner.Generate(region, s.Occ.Corridors, s.Gates, ctx.F("lakeSize") / 100f, Hashing.Mix(ctx.Seed, 0x1A4Eu));
                if (s.Lake.Length >= 3) EmitLake(ctx, s.Lake);
                else ctx.Issue(IssueSeverity.Info, "Placemaker.Park.Issue.NoRoomForLake");
            }
            ctx.Check();

            // 3. Lawns between paths (regions) and the protected clearing on the largest one.
            var lawns = Lawns(ctx, s);
            if (lawns.Count > 0 && style.ClearingFactor > 0f)
            {
                var largest = LayerRegion.From(lawns[0]);
                if (largest != null)
                {
                    var centre = largest.DeepestPoint(out float inscribed, null, 16);
                    float radius = math.min(math.min(inscribed - 2f, math.sqrt(largest.Area) * 0.3f), 30f) * style.ClearingFactor;
                    if (radius >= 6f) { s.ClearingCenter = centre; s.ClearingRadius = radius; }
                }
            }
            ctx.Check();

            // 4. Furniture first so planting keeps clear of it (ParkManager ordering).
            float per100 = ctx.F("furniture");
            if (per100 > 0f && s.Curves.Count > 0)
            {
                float benchInterval = 100f / per100;
                ParkPlanting.PathFurniture(s, ParkItemKind.Bench, benchInterval, ParkPlanting.BenchRadius, 0x7f4a7c15u);
                if (ctx.B("lamps")) ParkPlanting.PathFurniture(s, ParkItemKind.Lamp, benchInterval * 0.68f, ParkPlanting.LampRadius, 0x94d049bbu);
                if (ctx.B("bins")) ParkPlanting.Bins(s, per100 / 3f);
            }
            ctx.Check();

            // 5. Vegetation.
            float plantedHa = math.max(0f, region.Area - (s.Lake.Length >= 3 ? Poly.Area(s.Lake) : 0f)) / 10000f;
            int trees = math.min(4000, (int)math.round(plantedHa * ctx.F("treeDensity")));
            int bushes = math.min(6000, (int)math.round(plantedHa * ctx.F("bushDensity")));
            bool belt = ctx.B("belt");
            var treeIds = ctx.L("treeSpecies"); var bushIds = ctx.L("bushSpecies");
            bool canTrees = trees > 0 && AssetAvailability.Require(ctx.Plan, AssetCategories.Tree, treeIds, Keys.Category(AssetCategories.Tree));
            bool canBushes = bushes > 0 && AssetAvailability.Require(ctx.Plan, AssetCategories.Bush, bushIds, Keys.Category(AssetCategories.Bush));
            if (canTrees || canBushes) ParkPlanting.BuildGroves(s, style);
            if (canTrees) ParkPlanting.Vegetation(s, style, trees, true, belt);
            ctx.Check();
            if (canBushes) ParkPlanting.Vegetation(s, style, bushes, false, belt);
            if (ctx.B("animals")) ParkPlanting.AnimalSpawners(s);
            ctx.Check();

            // 6. Emit everything.
            if (ctx.B("groundSurface")) ctx.Surface(region.Ring, SurfaceRole.Grass, AssetMix.One(ctx.S("surface")));
            float pathLength = ParkPaths.Emit(ctx, s.Curves, s.Gates, AssetMix.One(ctx.S("pathAsset")));
            if (ctx.B("fence"))
            {
                var fence = AssetMix.One(ctx.S("fenceAsset"));
                foreach (var run in Fences.Runs(region, s.Gates, 3.75f))
                    ctx.StraightNet(run.c0, run.c1, NetRole.Fence, fence);
            }
            EmitItems(ctx, s, treeIds, bushIds);
            foreach (var g in s.Gates)
                if (g.Suggested) ctx.Plan.Facts["suggestedEntrances"] = (ctx.Plan.Facts.TryGetValue("suggestedEntrances", out var n) ? n : 0) + 1;

            // 7. Regions for children.
            for (int i = 0; i < lawns.Count; i++)
                if (i == 0) ctx.AddRegion(lawns[i], "green:0", RegionTags.Green, RegionTags.Core);
                else ctx.AddRegion(lawns[i], "green:" + i, RegionTags.Green);
            var strips = region.BoundaryStrips(BeltDepth, math.clamp((int)math.round(region.Perimeter / 250f), 2, 6));
            for (int i = 0; i < strips.Count; i++) ctx.AddRegion(strips[i], "edge:" + i, RegionTags.Edge);

            // 8. Summary.
            ctx.Stat(Keys.StatArea, Math.Round(region.Area), Keys.UnitM2);
            ctx.Stat(Keys.StatPathLength, Math.Round(pathLength), Keys.UnitM);
            ctx.Stat(Keys.StatTrees, s.Count(ParkItemKind.Tree), Keys.UnitCount);
            ctx.Stat(Keys.StatBushes, s.Count(ParkItemKind.Bush), Keys.UnitCount);
            ctx.Stat(Keys.StatBenches, s.Count(ParkItemKind.Bench), Keys.UnitCount);
            ctx.Stat(Keys.StatLamps, s.Count(ParkItemKind.Lamp), Keys.UnitCount);
            ctx.Stat(Keys.StatBins, s.Count(ParkItemKind.Bin), Keys.UnitCount);
            ctx.Plan.Facts["parkArea"] = region.Area;
            ctx.Plan.Facts["entrances"] = s.Gates.Count;
        }

        private static int LoopsFor(string mode, float area)
        {
            switch (mode)
            {
                case "minimal": return 0;
                case "loop": return 1;
                case "network": return math.max(2, ParkPathPlanner.LoopsByArea(area));
                default: return ParkPathPlanner.LoopsByArea(area);
            }
        }

        private static List<float2[]> Lawns(LayerContext ctx, ParkSite s)
        {
            if (s.Curves.Count == 0 && s.Lake.Length < 3)
                return new List<float2[]> { s.LayerRegion.Ring };
            var corridors = s.Occ.Corridors;
            var lake = s.Lake;
            return FreeSpace.Pieces(s.LayerRegion, p =>
                    corridors.Overlaps(p, 0.75f) || lake.Length >= 3 && (Poly.Contains(lake, p) || ParkSite.MinDistToRing(lake, p) < 2f),
                2f, 120f, ctx.Cancel);
        }

        private static void EmitLake(LayerContext ctx, float2[] lake)
        {
            // Water must sit below the lowest bank, otherwise it spills: level = lowest shore − 0.5 m.
            float shore = float.MaxValue;
            foreach (var p in lake) shore = math.min(shore, ctx.H(p));
            if (!math.isfinite(shore)) shore = 0f;
            float level = shore - 0.5f;
            var anchor = ParkLakePlanner.WaterAnchor(lake, out _);
            ctx.Plan.Terraform.Add(new PlannedTerraform
            {
                Ring = Poly.To3D(lake, ctx.H), Kind = TerraformKind.Lower, TargetHeight = level - 3f,
                Permanent = true, WaterLevel = level, WaterAnchor = new float3(anchor.x, level, anchor.y),
            });
            ctx.Surface(lake, SurfaceRole.Water, new AssetRef("", Array.Empty<string>()));
            ctx.Issue(IssueSeverity.Warning, "Placemaker.Park.Issue.LakePermanent");
            ctx.Stat(Keys.StatLakeArea, Math.Round(Poly.Area(lake)), Keys.UnitM2);
        }

        private static void EmitItems(LayerContext ctx, ParkSite s, string[] treeIds, string[] bushIds)
        {
            var bench = AssetMix.One(ctx.S("benchAsset"));
            var lamp = AssetMix.One(ctx.S("lampAsset"));
            var bin = AssetMix.One(ctx.S("binAsset"));
            foreach (var it in s.Items)
            {
                switch (it.Kind)
                {
                    case ParkItemKind.Tree: ctx.Object(it.Position, it.Yaw, ObjectRole.Tree, AssetMix.Pick(treeIds, it.Variant), it.Variant, it.Age); break;
                    case ParkItemKind.Bush: ctx.Object(it.Position, it.Yaw, ObjectRole.Bush, AssetMix.Pick(bushIds, it.Variant), it.Variant); break;
                    case ParkItemKind.Bench: ctx.Object(it.Position, it.Yaw, ObjectRole.Bench, bench, it.Variant); break;
                    case ParkItemKind.Lamp: ctx.Object(it.Position, it.Yaw, ObjectRole.Lamp, lamp, it.Variant); break;
                    case ParkItemKind.Bin: ctx.Object(it.Position, it.Yaw, ObjectRole.Bin, bin, it.Variant); break;
                    case ParkItemKind.Spawner:
                        ctx.Object(it.Position, 0f, ObjectRole.AnimalSpawner, new AssetRef("", "Forest Animal Spawner"), it.Variant); break;
                }
            }
        }
    }
}
