// Plaza layer: walkable paved square with centerpiece(s), furniture arrangement, access points.
// Planner ported from ParkManager (tomkpunkt, GPL-3.0), Geometry/PlazaPlanner.cs and
// Geometry/PlazaVariantRoller.cs (randomisation, now respecting locked controls).
//
// The whole polygon is walkable: a pedestrian navigation area (vanilla "Pedestrian Area") plus
// "Pedestrian Access Location" markers where people enter; no hidden path ring (ParkManager).
//
// Regions: paved (whole plaza), core (disc around the centre), edge:N (4 m frontage strips).

using System;
using System.Collections.Generic;
using Placemaker.Core.Layers.Common;
using Unity.Mathematics;
using Random = Unity.Mathematics.Random;

namespace Placemaker.Core.Layers.Plaza
{
    [AreaGenerator(20)]
    public sealed class PlazaLayer : LayerBase, IHasFeatures
    {
        public const string LayerId = "plaza";
        public const string AccessKind = "access";
        public const int MaxArrangementItems = 5;

        /// <summary>Arrangement tokens the editor offers, in display order.</summary>
        public static readonly string[] ItemKinds = { "bench", "lamp", "bin", "tree", "bush", "planter", "flowerbed", "kiosk" };

        protected override string Area => "Plaza";
        public override string Id => LayerId;
        public override string Icon => "Media/Game/Icons/Pathways.svg";
        public override LayerKind Kind => LayerKind.Fill;
        public override string[] Emits => new[] { RegionTags.Paved, RegionTags.Core, RegionTags.Edge, StreetFrontage.ShopsTag };
        protected override bool HostsStreetShops => true;
        public override float MinArea => 60f;

        private static readonly FeatureInfo[] FeatureList =
        {
            new FeatureInfo { Layer = "plazatrees", LabelKey = "Placemaker.Plaza.Feature.trees", TooltipKey = "Placemaker.Plaza.Feature.trees.Tooltip" },
            new FeatureInfo { Layer = "playground", LabelKey = "Placemaker.Plaza.Feature.playground", Preset = Park.ParkLayer.Bag(("size", "small"), ("fence", true)) },
            new FeatureInfo
            {
                Layer = "fountain", LabelKey = "Placemaker.Plaza.Feature.fountain", TooltipKey = "Placemaker.Plaza.Feature.fountain.Tooltip",
                Preset = Park.ParkLayer.Bag(("size", "large"), ("benches", 8.0), ("planters", true)),
            },
            new FeatureInfo { Layer = "kiosk", LabelKey = "Placemaker.Plaza.Feature.kiosk" },
            new FeatureInfo { Layer = "flowerbeds", LabelKey = "Placemaker.Plaza.Feature.flowerbeds", Preset = Park.ParkLayer.Bag(("density", 30.0), ("nearPaths", false)) },
            new FeatureInfo { Layer = "shops", LabelKey = "Placemaker.Feature.shops", TooltipKey = "Placemaker.Feature.shops.Tooltip", Preset = Park.ParkLayer.Bag(("count", 3.0), ("where", "edges")) },
            StreetShopsFeature("Plaza"),
            new FeatureInfo { Layer = "decals", LabelKey = "Placemaker.Feature.decals", Preset = Park.ParkLayer.Bag(("theme", "cracks"), ("ground", "paved"), ("density", 0.8)) },
            new FeatureInfo { Layer = "terrain", LabelKey = "Placemaker.Feature.flatten", TooltipKey = "Placemaker.Feature.terrain.Tooltip", Preset = Park.ParkLayer.Bag(("shape", "flatten")) },
        };

        public IReadOnlyList<FeatureInfo> Features => FeatureList;

        /// <summary>Clear radius the plaza keeps at its centre for a fountain feature (large fountain + seating ring).</summary>
        public const float FountainClearRadius = 12.5f;

        protected override void DefineControls(ControlSet c)
        {
            c.Asset("centerpiece", AssetCategories.Centerpiece, ControlTier.Essential, "centre");
            c.Choice("centerMode", "single", ControlTier.Essential, "centre", "single", "mirrored", "axis", "none");
            c.Asset("surface", AssetCategories.SurfacePaving, ControlTier.Essential, "ground");
            c.Slider("furniture", 8, 0, 24, 1, "count", ControlTier.Essential, "furniture").Tip();
            c.RandomRange("furniture", 2, 16);

            c.Slider("centerSize", 5, 2, 20, 0.5, "m", ControlTier.More, "centre").Tip();
            c.Slider("centerSpacing", 20, 5, 60, 1, "m", ControlTier.More, "centre");
            c.Arrangement("arrangement", "", new[] { "bench", "lamp", "bench" }, ControlTier.More, "furniture").Tip();
            c.Choice("arrangementMode", "center", ControlTier.More, "furniture", "center", "edges");
            c.Slider("arrangementGap", 4, 0, 20, 0.5, "m", ControlTier.More, "furniture");
            c.Asset("benchAsset", AssetCategories.Bench, ControlTier.More, "assets");
            c.Asset("lampAsset", AssetCategories.Lamp, ControlTier.More, "assets");
            c.Asset("binAsset", AssetCategories.Bin, ControlTier.More, "assets");
            c.Asset("treeAsset", AssetCategories.Tree, ControlTier.More, "assets");
            c.Asset("bushAsset", AssetCategories.Bush, ControlTier.More, "assets");
            c.Asset("planterAsset", AssetCategories.Planter, ControlTier.More, "assets");
            c.Asset("flowerbedAsset", AssetCategories.Flowerbed, ControlTier.More, "assets");
            c.Asset("kioskAsset", AssetCategories.Kiosk, ControlTier.More, "assets");
            c.Toggle("walkable", true, ControlTier.More, "ground").Tip();
            c.Toggle("paving", true, ControlTier.More, "ground");
            c.Markers("access", ControlTier.More, AccessKind).Tip();
            c.Toggle("fence", false, ControlTier.More, "boundary");
            c.Asset("fenceAsset", AssetCategories.Fence, ControlTier.More, "boundary").When("fence=true");
            c.Fixed("centerSize");
            c.Fixed("walkable");
            c.Fixed("paving");
            c.Chance("fence", 0.35f);   // ParkManager PlazaVariantRoller.FenceChance

            // Depth (round 4): levels, amphitheatre, green, water, shade, activity, identity. See PlazaDressing.
            c.Choice("centerKind", "any", ControlTier.More, "identity", "any", "fountain", "statue").Tip();
            c.Choice("pattern", "none", ControlTier.More, "identity", "none", "grid", "border", "scatter").Tip();
            c.Choice("lighting", "standard", ControlTier.More, "identity", "standard", "feature").Tip();
            c.Toggle("bollards", false, ControlTier.More, "identity").Tip();
            c.Asset("bollardAsset", AssetCategories.Bollard, ControlTier.More, "identity").When("bollards=true");
            c.Choice("levels", "flat", ControlTier.More, "levels", "flat", "terracedUp", "terracedDown", "sunken", "raised").Tip();
            c.Slider("levelCount", 3, 2, 4, 1, "count", ControlTier.More, "levels");
            c.Slider("levelHeight", 1.5, 0.5, 4, 0.5, "m", ControlTier.More, "levels").Tip();
            c.Toggle("levelPlanters", true, ControlTier.More, "levels");
            c.Toggle("amphitheatre", false, ControlTier.More, "amphitheatre").Tip();
            c.Slider("rows", 4, 2, 8, 1, "count", ControlTier.More, "amphitheatre").When("amphitheatre=true");
            c.Toggle("amphiRaise", true, ControlTier.More, "amphitheatre").When("amphitheatre=true").Tip();
            c.Asset("stageAsset", AssetCategories.Stage, ControlTier.More, "amphitheatre");
            c.Slider("lawns", 0, 0, 40, 5, "%", ControlTier.More, "green").Tip();
            c.Slider("beds", 0, 0, 12, 1, "count", ControlTier.More, "green").Tip();
            c.Slider("planters", 0, 0, 30, 1, "count", ControlTier.More, "green");
            c.Choice("treeLayout", "none", ControlTier.More, "green", "none", "edge", "grid", "groves").Tip();
            c.Choice("border", "none", ControlTier.More, "green", "none", "grass", "gravel").Tip();
            c.Toggle("pool", false, ControlTier.More, "water").Tip();
            c.Toggle("realWater", false, ControlTier.More, "water").When("pool=true").Tip();
            c.Asset("poolSurface", AssetCategories.SurfaceWater, ControlTier.More, "water").When("pool=true");
            c.Toggle("jets", false, ControlTier.More, "water").Tip();
            c.Asset("jetAsset", AssetCategories.WaterJets, ControlTier.More, "water").When("jets=true");
            c.Slider("shade", 0, 0, 6, 1, "count", ControlTier.More, "shade").Tip();
            c.Asset("shelterAsset", AssetCategories.Shelter, ControlTier.More, "shade");
            c.Slider("cafe", 0, 0, 8, 1, "count", ControlTier.More, "activity").Tip();
            c.Asset("cafeAsset", AssetCategories.CafeSeating, ControlTier.More, "activity");
            c.Slider("stalls", 0, 0, 24, 1, "count", ControlTier.More, "activity").Tip();
            c.Asset("stallAsset", AssetCategories.MarketStall, ControlTier.More, "activity");
            c.Toggle("games", false, ControlTier.More, "activity").Tip();
            c.Toggle("performance", false, ControlTier.More, "activity").Tip();
            DefineStreetShopControls(c);
            foreach (var id in new[] { "levels", "levelCount", "levelHeight", "amphitheatre", "rows", "pool", "realWater", "jets", "stalls", "performance", "games", "bollards", "pattern", "lighting", "centerKind", "border", "treeLayout" })
                c.Fixed(id);
            c.RandomRange("lawns", 0, 20);
            c.RandomRange("beds", 0, 4);
            c.RandomRange("planters", 0, 12);
            c.RandomRange("shade", 0, 2);
            c.RandomRange("cafe", 0, 2);
        }

        protected override void DefinePresets()
        {
            Preset("civic", ("centerMode", "single"), ("centerSize", 4.0), ("furniture", 8.0), ("arrangement", new[] { "bench", "lamp", "bench" }),
                ("arrangementMode", "edges"), ("arrangementGap", 3.0), ("centerKind", "statue"), ("treeLayout", "edge"), ("pattern", "grid"),
                ("lighting", "feature"), ("bollards", true), ("planters", 8.0));
            Preset("fountain", ("centerMode", "single"), ("centerSize", 9.0), ("furniture", 12.0), ("arrangement", new[] { "bench", "planter", "bench" }),
                ("arrangementMode", "center"), ("arrangementGap", 4.0), ("centerKind", "fountain"), ("pattern", "border"), ("beds", 4.0), ("lighting", "feature"));
            Preset("market", ("centerMode", "none"), ("furniture", 6.0), ("arrangement", new[] { "bench", "bin", "bench" }),
                ("arrangementMode", "edges"), ("arrangementGap", 2.0), ("stalls", 14.0), ("cafe", 2.0), ("treeLayout", "edge"), ("shade", 1.0));
            Preset("garden", ("centerMode", "single"), ("centerSize", 5.0), ("furniture", 10.0),
                ("arrangement", new[] { "bush", "bench", "tree", "bench", "bush" }), ("arrangementMode", "edges"), ("arrangementGap", 2.5),
                ("lawns", 20.0), ("beds", 6.0), ("treeLayout", "groves"), ("border", "grass"));
            Preset("sunken", ("centerMode", "single"), ("centerSize", 4.0), ("furniture", 8.0), ("levels", "sunken"), ("levelHeight", 2.0),
                ("lawns", 25.0), ("beds", 6.0), ("planters", 10.0), ("treeLayout", "groves"), ("centerKind", "fountain"));
            Preset("amphitheatre", ("centerMode", "none"), ("furniture", 4.0), ("amphitheatre", true), ("rows", 5.0), ("shade", 2.0),
                ("treeLayout", "edge"), ("lighting", "feature"));
            Preset("terraced", ("centerMode", "single"), ("centerSize", 4.0), ("furniture", 8.0), ("levels", "terracedUp"), ("levelCount", 3.0),
                ("levelHeight", 2.5), ("beds", 4.0), ("treeLayout", "grid"), ("centerKind", "statue"));
            Preset("cafe", ("centerMode", "single"), ("centerSize", 3.0), ("furniture", 6.0), ("cafe", 5.0), ("shade", 3.0), ("planters", 12.0),
                ("pattern", "scatter"), ("games", true), ("jets", false), ("treeLayout", "edge"));
        }

        public override List<EdgeMarker> SuggestMarkers(AreaInput input)
        {
            var region = LayerRegion.From(input?.Shape?.Ring);
            if (region == null) return new List<EdgeMarker>();
            return Entrances.Suggest(input, region, AccessKind, WantedAccess(region));
        }

        public override Readiness CheckReady(AreaInput input) => new Readiness { Ready = true };

        private static int WantedAccess(LayerRegion r) => math.clamp((int)(r.Perimeter / 80f), 2, 8);

        /// <summary>
        /// ParkManager's variant roller, per control: centre mode (15 % none), spacing, arrangement
        /// mode/gap, density and a mirror-symmetric arrangement — but never a locked control.
        /// </summary>
        protected override bool RandomizeControl(ControlSchema control, ref Random rng, SettingsBag into)
        {
            switch (control.Id)
            {
                case "centerMode":
                    into.Values["centerMode"] = rng.NextFloat() < 0.15f ? "none" : new[] { "single", "mirrored", "axis" }[rng.NextInt(0, 3)];
                    return true;
                case "arrangement":
                {
                    int length = rng.NextInt(1, MaxArrangementItems + 1);
                    int half = (length + 1) / 2;
                    // Benches, lamps, bins, trees and bushes always exist in vanilla; the rest only when chosen.
                    var pool = new[] { "bench", "lamp", "bin", "tree", "bush" };
                    var kinds = new string[half];
                    for (int i = 0; i < half; i++) kinds[i] = pool[rng.NextInt(0, pool.Length)];
                    var result = new string[length];
                    for (int i = 0; i < length; i++) result[i] = kinds[math.min(i, length - 1 - i)];
                    into.Values["arrangement"] = result;
                    return true;
                }
            }
            return false;
        }

        protected override void PlanCore(LayerContext ctx)
        {
            var region = ctx.LayerRegion;
            var occ = new Occupancy(region, ctx.Input.Reserved, ctx.Site);
            // Inside a place (a park's plaza) people arrive along the parent's paths: access where they end.
            var gates = InPlace(ctx.Input) ? PathEnds(ctx.Input, region) : new List<Gate>();
            if (gates.Count == 0) gates = Entrances.Resolve(ctx.Input, region, AccessKind, WantedAccess(region));
            var gatePoints = new List<float2>();
            foreach (var g in gates) gatePoints.Add(g.Position);

            // Ground: walkable navigation area + paving surface.
            if (ctx.B("walkable")) ctx.NavigationArea(region.Ring, AreaRole.PedestrianArea, new AssetRef("", "Pedestrian Area", "Walking Area"));
            if (ctx.B("paving")) ctx.Surface(region.Ring, SurfaceRole.Plaza, AssetMix.One(ctx.S("surface"), "Pavement Surface 01"));
            ctx.Check();
            bool fountainChild = ctx.Input.HasChild("fountain");

            // Big elements first (they claim space the furniture arrangement then works around).
            PlazaDressing.Border(ctx, region);
            PlazaDressing.Levels(ctx, occ, region, gates);
            PlazaDressing.Amphitheatre(ctx, occ, region, fountainChild || ctx.S("centerMode") != "none", math.max(FountainClearRadius, ctx.F("centerSize")) + 3f);
            PlazaDressing.Pool(ctx, occ, region, region.Centroid, fountainChild || ctx.S("centerMode") != "none" ? math.max(FountainClearRadius, ctx.F("centerSize")) : 0f);
            PlazaDressing.Stalls(ctx, occ, region);
            PlazaDressing.Lawns(ctx, occ, region);
            ctx.Check();

            // Centre + arrangement.
            var mode = ParseCenterMode(ctx.S("centerMode"));
            var centerAsset = AssetMix.One(ctx.S("centerpiece"));
            bool centerAvailable = mode == CenterMode.None || AssetAvailability.Require(ctx.Plan, AssetCategories.Centerpiece,
                new[] { ctx.S("centerpiece") }, Keys.Category(AssetCategories.Centerpiece));
            if (!centerAvailable) mode = CenterMode.None;
            float centerRadius = math.max(ctx.F("centerSize"), AssetMix.FootprintRadius(ctx.Site, centerAsset, 0f));
            // A fountain feature takes the centre: keep it clear (the add-on places the fountain itself).
            bool fountainFeature = ctx.Input.HasChild("fountain");
            if (fountainFeature)
            {
                mode = CenterMode.Single;
                region.DeepestPoint(out float inscribed, null, 12);
                centerRadius = math.max(centerRadius, math.min(FountainClearRadius, inscribed * 0.6f));
            }
            var items = ParseArrangement(ctx);
            var layout = PlazaPlanner.Generate(region, occ, gatePoints, new PlazaPlanner.Options
            {
                CenterMode = mode, CenterRadius = centerRadius, CenterSpacing = ctx.F("centerSpacing"),
                ArrangementMode = ctx.S("arrangementMode") == "edges" ? ArrangementMode.AlongEdges : ArrangementMode.AroundCenter,
                ArrangementGap = ctx.F("arrangementGap"), Groups = ctx.I("furniture"), Arrangement = items,
            });
            if (layout.CenterpieceDropped && !fountainFeature) ctx.Issue(IssueSeverity.Info, "Placemaker.Plaza.Issue.CenterpieceDoesNotFit");
            ctx.Check();

            // Emit.
            uint variant = Hashing.Mix(ctx.Seed, 0xCE47u);
            string centerCat = PlazaDressing.CenterCategory(ctx.S("centerKind"));
            if (centerCat != null && string.IsNullOrEmpty(ctx.S("centerpiece")) && !AssetAvailability.Has(centerCat)) centerCat = null;
            if (!fountainFeature)
                foreach (var c in layout.Centerpieces)
                {
                    ctx.Object(c.Position, 0f, ObjectRole.Centerpiece, centerAsset, variant, -1, centerCat);   // one design for all: a mirrored pair matches
                    occ.Take(c.Position, c.Radius, 0);
                }
            else occ.Take(layout.Center, centerRadius, 0);
            foreach (var f in layout.Furniture)
            {
                uint v = Hashing.Mix(variant, (uint)f.ArrangementId * 31u + (uint)Array.IndexOf(ItemKinds, f.Item.Kind));
                ctx.Object(f.Position, f.Yaw, f.Item.Role, f.Item.Asset, v, f.Item.Role == ObjectRole.Tree ? 3 : -1, f.Item.Category);
                occ.Take(f.Position, f.Item.FootprintRadius, 1);
            }
            ctx.Check();

            // Smaller dressing into what is left.
            PlazaDressing.Trees(ctx, occ, region, gates, layout.Center, layout.CoreRadius);
            PlazaDressing.Beds(ctx, occ);
            PlazaDressing.EdgePlanters(ctx, occ, region, gates);
            PlazaDressing.Shade(ctx, occ);
            PlazaDressing.Cafe(ctx, occ);
            PlazaDressing.Games(ctx, occ);
            PlazaDressing.PerformanceSpot(ctx, occ, region);
            PlazaDressing.Jets(ctx, occ);
            PlazaDressing.Lighting(ctx, occ, region, gates, layout.Center, layout.CoreRadius);
            PlazaDressing.Bollards(ctx, occ, region, gates);
            PlazaDressing.Pattern(ctx, occ, region);
            foreach (var g in gates)
            {
                var inward = region.InwardNormal(g.Segment);
                ctx.Object(g.Position, math.atan2(inward.x, inward.y), ObjectRole.AccessPoint, new AssetRef("", "Pedestrian Access Location"), 0);
            }
            if (ctx.B("fence"))
            {
                var fence = AssetMix.One(ctx.S("fenceAsset"));
                foreach (var run in Fences.Runs(region, gates, 3f))
                    ctx.StraightNet(run.c0, run.c1, NetRole.Fence, fence);
            }

            // Regions.
            ctx.AddRegion(region.Ring, "paved", RegionTags.Paved);
            float coreRadius = layout.CoreRadius;
            if (coreRadius >= 4f) ctx.AddRegion(LayerRegion.Circle(layout.Center, coreRadius, 24), "core", RegionTags.Core, RegionTags.Paved);
            var strips = region.BoundaryStrips(4f, math.clamp((int)math.round(region.Perimeter / 200f), 2, 6));
            for (int i = 0; i < strips.Count; i++) ctx.AddRegion(strips[i], "edge:" + i, RegionTags.Edge, RegionTags.Paved);

            // Summary.
            ctx.Stat(Keys.StatArea, Math.Round(region.Area), Keys.UnitM2);
            ctx.Stat(Keys.StatFountains, fountainFeature ? 0 : layout.Centerpieces.Count, Keys.UnitCount);
            ctx.Stat(Keys.StatBenches, CountRole(ctx.Plan, ObjectRole.Bench), Keys.UnitCount);
            ctx.Stat(Keys.StatLamps, CountRole(ctx.Plan, ObjectRole.Lamp), Keys.UnitCount);
            ctx.Stat(Keys.StatTrees, CountRole(ctx.Plan, ObjectRole.Tree), Keys.UnitCount);
            ctx.Plan.Facts["accessPoints"] = gates.Count;
        }

        private static bool InPlace(AreaInput input)
        {
            foreach (var m in input.Markers) if (m.Kind == AccessKind) return false;   // the player placed access points
            if (input.RegionTags == null) return false;
            foreach (var t in input.RegionTags) if (t != null && t.StartsWith(Composer.PlacePrefix, StringComparison.Ordinal)) return true;
            return false;
        }

        /// <summary>Access points where reserved paths of the parent end on this region's outline.</summary>
        public static List<Gate> PathEnds(AreaInput input, LayerRegion region)
        {
            var gates = new List<Gate>();
            if (input.Reserved == null) return gates;
            foreach (var c in input.Reserved.Corridors)
            {
                if (c.Tag != nameof(NetRole.Pathway) && c.Tag != nameof(NetRole.PathwayWide)) continue;
                foreach (var end in new[] { c.A, c.B })
                {
                    if (region.BoundaryDistance(end, 1.6f) > 1.5f) continue;
                    var p = region.ClosestOnBoundary(end, out int seg, out float t);
                    bool near = false;
                    foreach (var g in gates) if (math.distancesq(g.Position, p) < 16f) { near = true; break; }
                    if (!near) gates.Add(new Gate { Position = p, Segment = seg, T = t, AttachId = -1 });
                }
            }
            gates.Sort((a, b) => a.Segment != b.Segment ? a.Segment.CompareTo(b.Segment) : a.T.CompareTo(b.T));
            return gates;
        }

        private static int CountRole(AreaPlan plan, ObjectRole role)
        {
            int n = 0;
            foreach (var o in plan.Objects) if (o.Role == role) n++;
            return n;
        }

        public static CenterMode ParseCenterMode(string s)
        {
            switch (s)
            {
                case "mirrored": return CenterMode.Mirrored;
                case "axis": return CenterMode.Axis;
                case "none": return CenterMode.None;
                default: return CenterMode.Single;
            }
        }

        /// <summary>
        /// Arrangement tokens: "kind" or "kind:assetId" (kinds in <see cref="ItemKinds"/>), max 5, unknown
        /// tokens skipped. An item without asset uses the matching "&lt;kind&gt;Asset" control (automatic when empty).
        /// </summary>
        public static List<ArrangementItem> ParseArrangement(LayerContext ctx)
        {
            var result = new List<ArrangementItem>();
            foreach (var raw in ctx.L("arrangement"))
            {
                if (result.Count >= MaxArrangementItems) break;
                if (string.IsNullOrEmpty(raw)) continue;
                int colon = raw.IndexOf(':');
                string kind = colon < 0 ? raw : raw.Substring(0, colon);
                string asset = colon < 0 ? "" : raw.Substring(colon + 1);
                if (!Describe(kind, out var role, out var category, out float footprint)) continue;
                if (string.IsNullOrEmpty(asset)) asset = ctx.S(kind + "Asset");
                if (!AssetAvailability.Require(ctx.Plan, category, new[] { asset }, Keys.Category(category))) continue;
                var assetRef = AssetMix.One(asset);
                result.Add(new ArrangementItem
                {
                    Kind = kind, Role = role, Category = category, Asset = assetRef,
                    FootprintRadius = AssetMix.FootprintRadius(ctx.Site, assetRef, footprint),
                });
            }
            return result;
        }

        public static bool Describe(string kind, out ObjectRole role, out string category, out float footprint)
        {
            switch (kind)
            {
                case "bench": role = ObjectRole.Bench; category = AssetCategories.Bench; footprint = 1.35f; return true;
                case "lamp": role = ObjectRole.Lamp; category = AssetCategories.Lamp; footprint = 0.55f; return true;
                case "bin": role = ObjectRole.Bin; category = AssetCategories.Bin; footprint = 0.5f; return true;
                case "tree": role = ObjectRole.Tree; category = AssetCategories.Tree; footprint = 1.5f; return true;
                case "bush": role = ObjectRole.Bush; category = AssetCategories.Bush; footprint = 0.9f; return true;
                case "planter": role = ObjectRole.Planter; category = AssetCategories.Planter; footprint = 1.0f; return true;
                case "flowerbed": role = ObjectRole.Planter; category = AssetCategories.Flowerbed; footprint = 1.2f; return true;
                case "kiosk": role = ObjectRole.Furniture; category = AssetCategories.Kiosk; footprint = 2.5f; return true;
                default: role = ObjectRole.Furniture; category = ""; footprint = 1f; return false;
            }
        }
    }
}
