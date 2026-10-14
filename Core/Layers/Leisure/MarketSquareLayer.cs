// Town / market square: a paved, walkable square (the plaza's ground) with a ring of trees just
// inside the outline and benches between them, a civic centrepiece (statue, fountain or clock) in
// the middle, and a grid of market stalls — back-to-back pairs facing the aisles — filling the
// space between. Lamps along the aisles, cafe tables under the trees, bollards on the road edges.

using System.Collections.Generic;
using Placemaker.Core.Geometry;
using Placemaker.Core.Layers.Common;
using Placemaker.Core.Layers.Plaza;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Leisure
{
    [AreaGenerator(57, Group = "squares")]
    public sealed class MarketSquareLayer : LeisureLayer, ILinkedControls
    {
        /// <summary>(s18) Ground palette: picking one clears the per-role surface picks; a role pick reads Custom.</summary>
        public string[] OnValueSet(SettingsBag settings, string controlId) => GroundPalettes.OnValueSet(settings, controlId, "surface");
        public void Derive(SettingsBag settings) => GroundPalettes.Derive(settings, "surface");

        /// <summary>A square with access points (not park entrances): never blocks Build.</summary>
        protected override bool TypedEntrances => false;

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
            c.Slider("treeSpacing", 8, 5, 14, 1, "m", ControlTier.More, "market").Layout(7).When("treeRing=true");
            c.Slider("cafe", 2, 0, 8, 1, "count", ControlTier.More, "market");
            c.Asset("cafeAsset", AssetCategories.CafeSeating, ControlTier.More, "market");
            c.Toggle("bollards", true, ControlTier.More, "market").Tip();
            c.Asset("bollardAsset", AssetCategories.Bollard, ControlTier.More, "market").When("bollards=true");
            c.Choice("pattern", "none", ControlTier.More, "market", "none", "grid", "border", "scatter");
            GroundPalettes.DefineControls(c, "ground", "surface");
            c.Asset("surface", AssetCategories.SurfacePaving, ControlTier.More, "ground");
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
            foreach (var g in gates)
            {
                var inward = region.InwardNormal(g.Segment);
                ctx.Object(g.Position, math.atan2(inward.x, inward.y), ObjectRole.AccessPoint, new AssetRef("", "Pedestrian Access Location"), 0);
                occ.TakeCorridor(g.Position, g.Position + inward * 6f, 1.5f, 6);   // keep the way in clear
            }
            LeisureKit.Frame(region, out var centre, out _, out _, out _, out _);
            var axis = PlazaDressing.SquareAxis(region);
            region.DeepestPoint(out float ins, null, 12);
            // Art pass a2: an old-town square in setts (a fountain square in warm sandstone), framed in dark
            // flags, the market floor in a lighter stone, trees in pits — one paving tiling (PavingKit).
            string kind = ctx.S("centerKind");
            // s18: the ground palette (Automatic: old-town cobbles, sandstone round a fountain — never the ochre look).
            var palette = GroundPalettes.Resolve(ctx.Settings, kind == "fountain" ? GroundPalettes.Sandstone : GroundPalettes.Cobble, "surface");
            var pave = new PavingPlan { Axis = axis };
            var pits = new List<float2>();

            // Centrepiece on its own rondel: the market cross the stalls gather round.
            float coreR = 0f, rondel = 0f;
            if (kind != "none" && ins > 10f)
            {
                string sub = PlazaDressing.CenterCategory(kind);
                string cat = LeisureKit.Category(ctx, ctx.S("centerpiece"), sub ?? AssetCategories.Centerpiece, AssetCategories.Centerpiece);
                if (cat != null)
                {
                    var asset = PlazaDressing.CenterAsset(ctx, kind == "any" ? "fountain" : kind);
                    coreR = math.clamp(ins * 0.16f, 5f, 9f);
                    rondel = coreR;
                    ctx.Object(centre, math.atan2(axis.x, axis.y), ObjectRole.Centerpiece, asset, Hashing.Mix(ctx.Seed, 0xCE0u), -1, cat);
                    occ.Take(centre, coreR + 1.5f, 0);
                    // Benches on the rondel's diagonals, looking at it.
                    for (int k = 0; k < 4; k++)
                    {
                        float a = math.atan2(axis.y, axis.x) + math.PI * 0.25f + k * math.PI * 0.5f;
                        var dir = new float2(math.cos(a), math.sin(a));
                        var p = centre + dir * (coreR - 1.2f);
                        ctx.Object(p, math.atan2(-dir.x, -dir.y), ObjectRole.Bench, PlazaArt.Or(ctx, "benchAsset", PlazaArt.Benches, 0), Hashing.Mix(ctx.Seed, 0xCE10u + (uint)k));
                    }
                }
            }

            // Tree ring just inside the frame, a bench between every other pair looking into the square.
            float fw = PlazaDressing.FrameWidth(region);
            if (ctx.B("treeRing") && AssetAvailability.Require(ctx.Plan, AssetCategories.Tree, new[] { ctx.S("treeAsset") }, Keys.Category(AssetCategories.Tree)))
            {
                var bench = PlazaArt.Or(ctx, "benchAsset", PlazaArt.Benches, 0);
                var ids = string.IsNullOrEmpty(ctx.S("treeAsset")) ? PlazaArt.ChestnutTrees : new[] { ctx.S("treeAsset") };
                float spacing = math.max(7f, ctx.Raw("treeSpacing"));
                var inner = region.InsetVertices(fw + 2.6f);
                var ringRegion = Poly.IsSimple(inner) && Poly.SignedArea(inner) > 0f ? LayerRegion.From(inner) : null;
                int t = 0;
                if (ringRegion != null)
                {
                    int count = math.max(1, (int)math.round(ringRegion.Perimeter / spacing));
                    float step = ringRegion.Perimeter / count;
                    float2 prev = default; bool havePrev = false;
                    for (int i = 0; i < count; i++)
                    {
                        var p = ringRegion.PointAt(step * (i + 0.5f), out _, out var inward, out _);
                        bool ok = !PlazaDressing.NearGate(p, gates, 6f) &&
                            LeisureKit.Put(ctx, occ, p, 0f, ObjectRole.Tree, AssetMix.Pick(ids, 0), null, 2.2f, Hashing.Mix(ctx.Seed, 0x7100u + (uint)t), 1f, 0.2f, 3, 3);
                        if (ok)
                        {
                            pits.Add(p);
                            if (havePrev && (t & 1) == 1 && math.distance(prev, p) < spacing * 1.4f)
                            {
                                var mid = (prev + p) * 0.5f;
                                LeisureKit.Put(ctx, occ, mid + inward * 0.6f, math.atan2(inward.x, inward.y), ObjectRole.Bench, bench, null, 1.1f, Hashing.Mix(ctx.Seed, 0x7200u + (uint)t), 0.5f, 0.1f);
                            }
                            t++;
                        }
                        prev = p; havePrev = ok;
                    }
                }
                ctx.Plan.Facts["ringTrees"] = t;
            }
            ctx.Check();

            // The market block: stalls back to back round the centre, facing their aisles, on a lighter floor.
            int stalls = 0;
            int wanted = ctx.I("stalls");
            if (wanted > 0 && PlazaArt.Available(ctx, "stallAsset", AssetCategories.MarketStall, AssetCategories.Kiosk))
            {
                stalls = PlazaDressing.StallBlock(ctx, occ, region, centre, axis, wanted, ctx.F("aisle"), "stallAsset", 0x5700u, out var block);
                if (stalls > 0)
                    pave.Add(new OrientedRect { Center = block.Center, Axis = block.Axis, HalfLength = block.HalfLength + 2.5f, HalfWidth = block.HalfWidth + 1.5f }.Ring,
                        SurfaceRole.Plaza, palette.Accent, AssetCategories.SurfacePaving);
                if (stalls < wanted) ctx.Issue(IssueSeverity.Info, "Placemaker.Market.Issue.Fewer", stalls, wanted);
            }
            ctx.Check();

            // Shared plaza dressing (same controls: cafe/cafeAsset, bollards/bollardAsset, pattern).
            PlazaDressing.Cafe(ctx, occ);
            PlazaDressing.Bollards(ctx, occ, region, gates);
            PlazaDressing.Lighting(ctx, occ, region, gates, centre, 0f, PlazaArt.ParkLamp);
            PlazaDressing.Ground(ctx, pave, region, gates, centre, rondel, palette, pits, axis, false);
            pave.Emit(ctx, region.Ring, SurfaceRole.Plaza, palette.Field, AssetCategories.SurfacePaving);

            ctx.AddRegion(region.Ring, "paved", RegionTags.Paved);
            if (coreR > 0f) ctx.AddRegion(LayerRegion.Circle(centre, coreR, 20), "core", RegionTags.Core, RegionTags.Paved);
            CommonStats(ctx);
            ctx.Stat("Placemaker.Stat.Stalls", stalls, Keys.UnitCount);
        }
    }
}
