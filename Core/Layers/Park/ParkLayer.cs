// Park layer: paths, lawn, planting, furniture, optional fence / wildlife, and built-in features
// (central plaza, pond, playground, picnic, restrooms, fountain, big trees, flowerbeds).
// Planners ported from ParkManager (tomkpunkt, GPL-3.0); vegetation scattering merged from
// ParkingLotTool (KruemelGames, GPL-3.0). See the individual files for details.
//
// Regions for children:
//   green:N      lawns between paths (largest first), tags {green}
//   green:0      additionally tagged {core} — the largest lawn, which holds a protected clearing
//   edge:N       boundary belt strips, tags {edge}
//   place:plaza  the plaza place (only when a child targets it), tags {place:plaza, paved}
//   place:pond   the pond basin (only when a child targets it), tags {place:pond, water}

using System;
using System.Collections.Generic;
using Placemaker.Core.Geometry;
using Placemaker.Core.Layers.Common;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Park
{
    [AreaGenerator(30)]
    public sealed class ParkLayer : LayerBase, IHasFeatures
    {
        public const string LayerId = "park";
        public const string EntranceKind = "entrance";
        public const float BeltDepth = 8f;
        public const string PlazaPlace = "place:plaza";
        public const string PondPlace = "place:pond";
        /// <summary>Hard limits of the plaza place (characteristic size, metres).</summary>
        public const float PlazaMinM = 12f, PlazaMaxM = 90f;

        protected override string Area => "Park";
        public override string Id => LayerId;
        public override string Icon => "Media/Game/Icons/ParksAndRecreation.svg";
        public override LayerKind Kind => LayerKind.Fill;
        public override string[] Emits => new[] { RegionTags.Green, RegionTags.Core, RegionTags.Edge, PlazaPlace, PondPlace };
        public override float MinArea => 150f;

        private static readonly FeatureInfo[] FeatureList =
        {
            new FeatureInfo
            {
                Layer = "plaza", Target = PlazaPlace, LabelKey = "Placemaker.Park.Feature.plaza",
                TooltipKey = "Placemaker.Park.Feature.plaza.Tooltip",
                Options = new[] { "plazaShape", "plazaAt", "plazaSize" },
                Preset = Bag(("centerMode", "single"), ("arrangementMode", "center"), ("furniture", 6.0), ("fence", false)),
            },
            new FeatureInfo { Layer = "playground", LabelKey = "Placemaker.Park.Feature.playground" },
            new FeatureInfo
            {
                Layer = "pond", Target = PondPlace, LabelKey = "Placemaker.Park.Feature.pond",
                TooltipKey = "Placemaker.Park.Feature.pond.Tooltip", Options = new[] { "lakeSize" },
            },
            new FeatureInfo { Layer = "picnic", LabelKey = "Placemaker.Park.Feature.picnic", Preset = Bag(("groups", 2.0)) },
            new FeatureInfo { Layer = "restrooms", LabelKey = "Placemaker.Park.Feature.restrooms" },
            new FeatureInfo { Layer = "fountain", LabelKey = "Placemaker.Park.Feature.fountain", Preset = Bag(("size", "medium")) },
            new FeatureInfo { Layer = "bigtrees", LabelKey = "Placemaker.Park.Feature.bigtrees", DefaultOn = true },
            new FeatureInfo { Layer = "flowerbeds", LabelKey = "Placemaker.Park.Feature.flowerbeds" },
        };

        public IReadOnlyList<FeatureInfo> Features => FeatureList;

        internal static SettingsBag Bag(params (string key, object value)[] values)
        {
            var b = new SettingsBag();
            foreach (var (k, v) in values) b.Values[k] = v;
            return b;
        }

        protected override void DefineControls(ControlSet c)
        {
            // Essentials first (DESIGN.md §2: style, paths, planting, furniture).
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
            c.Toggle("animals", false, ControlTier.More, "planting").Tip();
            c.Toggle("lamps", true, ControlTier.More, "furniture");
            c.Toggle("bins", true, ControlTier.More, "furniture");
            c.Asset("benchAsset", AssetCategories.Bench, ControlTier.More, "furniture");
            c.Asset("lampAsset", AssetCategories.Lamp, ControlTier.More, "furniture").When("lamps=true");
            c.Asset("binAsset", AssetCategories.Bin, ControlTier.More, "furniture").When("bins=true");
            c.Toggle("groundSurface", true, ControlTier.More, "ground");
            c.Toggle("fence", false, ControlTier.More, "boundary");
            c.Asset("fenceAsset", AssetCategories.Fence, ControlTier.More, "boundary").When("fence=true");

            // Feature options (shown under their feature toggle: see Features[].Options).
            c.Choice("plazaShape", "circle", ControlTier.More, "plaza", "circle", "square", "rectangle");
            c.Choice("plazaAt", "centre", ControlTier.More, "plaza", "centre", "corner", "edge", "entrance").Tip();
            c.Slider("plazaSize", 15, 5, 40, 1, "%", ControlTier.More, "plaza").Tip();
            c.Slider("lakeSize", 70, 20, 100, 5, "%", ControlTier.More, "pond").Tip();

            c.Chance("fence", 0.2f);
            c.Fixed("lakeSize");
            c.Fixed("plazaShape");
            c.Fixed("plazaAt");
            c.Fixed("plazaSize");
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

        /// <summary>The plaza rule described by the park's plaza options.</summary>
        public static PlaceRule PlazaRule(SettingsBag s) => new PlaceRule
        {
            Shape = PlaceRule.ParseShape(s.S("plazaShape", "circle")),
            Anchor = PlaceRule.ParseAnchor(s.S("plazaAt", "centre")),
            SizePct = (float)s.Num("plazaSize", 15), MinM = PlazaMinM, MaxM = PlazaMaxM, Aspect = 1.6f, Margin = 3f,
        };

        protected override void PlanCore(LayerContext ctx)
        {
            var region = ctx.LayerRegion;
            var style = ParkStyle.Get(ctx.S("style"));
            string pathMode = ctx.S("paths");
            int wantedGates = Entrances.WantedByArea(region.Area);
            var gates = Entrances.Resolve(ctx.Input, region, EntranceKind, wantedGates);

            // 0. Places carved out for features (only when a child will fill them).
            float2[] plaza = Array.Empty<float2>();
            if (ctx.Input.HasChildTarget(PlazaPlace))
            {
                var toward = new List<float2>();
                foreach (var g in gates) toward.Add(g.Position);
                var placed = PlaceSolver.Solve(region, ctx.Input.Reserved?.Polygons, PlazaRule(ctx.Settings), toward, Hashing.Mix(ctx.Seed, 0x91A2u));
                if (placed.Ok)
                {
                    plaza = placed.Ring;
                    if (placed.Shrunk) ctx.Issue(IssueSeverity.Info, "Placemaker.Park.Issue.PlazaSmaller");
                }
                else ctx.Issue(IssueSeverity.Warning, "Placemaker.Park.Issue.NoRoomForPlaza");
            }
            var reserved = ctx.Input.Reserved?.Clone() ?? new ReservedSpace();
            if (plaza.Length >= 3) reserved.Polygons.Add(plaza);
            var s = new ParkSite
            {
                LayerRegion = region, Occ = new Occupancy(region, reserved, ctx.Site),
                Seed = ctx.Seed, Cancel = ctx.Cancel,
            };
            if (plaza.Length >= 3)
            {
                // The plaza (outline and interior) is an obstacle for the lake planner and the belt/edge logic too.
                for (int i = 0; i < plaza.Length; i++) s.Occ.Corridors.Add(plaza[i], plaza[(i + 1) % plaza.Length], 0.5f, 101);
                foreach (var run in Poly.FillRuns(plaza, 3f)) s.Occ.Corridors.Add(run.a, run.b, 2f, 101);
            }

            // 1. Entrances and paths (paths end at the plaza's edge instead of crossing it).
            if (pathMode != "none")
            {
                s.Gates = gates;
                var gatePoints = new List<float2>();
                foreach (var g in s.Gates) gatePoints.Add(g.Position);
                var options = new ParkPathPlanner.Options { Loops = LoopsFor(pathMode, region.Area), StretchLoop = pathMode == "network" || pathMode == "auto" && region.Area > 12000f };
                s.Graph = ParkPathPlanner.PlanBest(region, gatePoints, options, Hashing.Mix(ctx.Seed, 0xA11u), ctx.Cancel);
                // Formal parks are axial by definition; every other style follows the toggle.
                bool curved = ctx.B("curvedPaths") && !style.Formal;
                s.Curves = ParkPaths.Build(s.Graph, region, curved, ctx.S("pathWidth"));
                if (plaza.Length >= 3)
                {
                    s.Curves = ParkPlaces.ClipAndConnect(s.Curves, plaza, region, out int joins);
                    ctx.Plan.Facts["plazaJoins"] = joins;
                }
                ParkPaths.Index(s.Curves, s.Occ.Corridors, 100);
            }
            ctx.Check();

            // 2. Pond (permanent terraform): a "pond" feature fills the place; the old "lake" setting emits it here.
            bool pondFeature = ctx.Input.HasChildTarget(PondPlace);
            if (pondFeature || ctx.B("lake"))
            {
                var lake = ParkLakePlanner.Generate(region, s.Occ.Corridors, s.Gates, ctx.F("lakeSize") / 100f, Hashing.Mix(ctx.Seed, 0x1A4Eu));
                if (lake.Length >= 3 && plaza.Length >= 3 && (Poly.Contains(plaza, Poly.Centroid(lake)) || Poly.Contains(lake, Poly.Centroid(plaza))))
                    lake = Array.Empty<float2>();
                s.Lake = lake;
                if (s.Lake.Length < 3) ctx.Issue(IssueSeverity.Info, "Placemaker.Park.Issue.NoRoomForLake");
                else if (pondFeature) ctx.Plan.Facts["pondArea"] = Poly.Area(s.Lake);
                else EmitLake(ctx, s.Lake);
            }
            ctx.Check();

            // 3. Lawns between paths (regions) and the protected clearing on the largest one.
            var lawns = Lawns(ctx, s, plaza);
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
            float carved = (s.Lake.Length >= 3 ? Poly.Area(s.Lake) : 0f) + (plaza.Length >= 3 ? Poly.Area(plaza) : 0f);
            float plantedHa = math.max(0f, region.Area - carved) / 10000f;
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
                foreach (var run in Fences.Runs(region, gates, 3.75f))
                    ctx.StraightNet(run.c0, run.c1, NetRole.Fence, fence);
            }
            EmitItems(ctx, s, treeIds, bushIds);
            foreach (var g in gates)
                if (g.Suggested) ctx.Plan.Facts["suggestedEntrances"] = (ctx.Plan.Facts.TryGetValue("suggestedEntrances", out var n) ? n : 0) + 1;

            // 7. Regions for children.
            if (plaza.Length >= 3) ctx.AddRegion(plaza, PlazaPlace, PlazaPlace, RegionTags.Paved);
            if (pondFeature && s.Lake.Length >= 3) ctx.AddRegion(s.Lake, PondPlace, PondPlace, "water");
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
            ctx.Plan.Facts["entrances"] = gates.Count;
            if (plaza.Length >= 3) ctx.Plan.Facts["plazaArea"] = Poly.Area(plaza);
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

        private static List<float2[]> Lawns(LayerContext ctx, ParkSite s, float2[] plaza)
        {
            bool hasPlaza = plaza.Length >= 3;
            if (s.Curves.Count == 0 && s.Lake.Length < 3 && !hasPlaza)
                return new List<float2[]> { s.LayerRegion.Ring };
            var corridors = s.Occ.Corridors;
            var lake = s.Lake;
            return FreeSpace.Pieces(s.LayerRegion, p =>
                    corridors.Overlaps(p, 0.75f) || lake.Length >= 3 && (Poly.Contains(lake, p) || ParkSite.MinDistToRing(lake, p) < 2f)
                    || hasPlaza && (Poly.Contains(plaza, p) || ParkSite.MinDistToRing(plaza, p) < 1f),
                2f, 120f, ctx.Cancel);
        }

        /// <summary>Lake terraform + water + permanent warning. Shared with the pond feature layer.</summary>
        public static void EmitLake(LayerContext ctx, float2[] lake, float depth = 3f)
        {
            // Water must sit below the lowest bank, otherwise it spills: level = lowest shore − 0.5 m.
            float shore = float.MaxValue;
            foreach (var p in lake) shore = math.min(shore, ctx.H(p));
            if (!math.isfinite(shore)) shore = 0f;
            float level = shore - 0.5f;
            var anchor = ParkLakePlanner.WaterAnchor(lake, out _);
            ctx.Plan.Terraform.Add(new PlannedTerraform
            {
                Ring = Poly.To3D(lake, ctx.H), Kind = TerraformKind.Lower, TargetHeight = level - depth,
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
