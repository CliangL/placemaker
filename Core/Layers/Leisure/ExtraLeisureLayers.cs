// Optional round-4 types: skate park, cemetery grounds, greenway (linear park).

using System.Collections.Generic;
using Placemaker.Core.Layers.Common;
using Placemaker.Core.Layers.Park;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Leisure
{
    /// <summary>
    /// Skate park: a paved bowl area with a cluster of ramps/rails in the middle (sports.skate),
    /// spectator benches round it, graffiti decals at the edges, lamps and a few trees outside.
    /// </summary>
    [AreaGenerator(58, Group = "sports")]
    public sealed class SkateParkLayer : LeisureLayer
    {
        protected override string Area => "Skate";
        public override string Id => "skatePark";
        public override string Icon => "Media/Game/Icons/Attractions.svg";
        public override float MinArea => 300f;
        protected override int WantedEntrances(LayerRegion region) => 1;

        protected override List<FeatureInfo> BuildFeatures() => new List<FeatureInfo>
        {
            Feature("shops", ("count", 1.0), ("where", "edges")), StreetShopsFeature(Area),
            Feature("decals", ("theme", "graffiti"), ("ground", "paved"), ("density", 1.0)), Feature("terrain", ("shape", "bowl"), ("height", 1.5)),
        };

        protected override void DefineControls(ControlSet c)
        {
            c.Slider("ramps", 8, 2, 24, 1, "count", ControlTier.Essential, "skate");
            c.Slider("paved", 70, 30, 100, 5, "%", ControlTier.Essential, "skate").Tip();
            c.Toggle("graffiti", true, ControlTier.Essential, "skate");
            c.AssetMulti("rampAssets", AssetCategories.SportsSkate, ControlTier.More, "skate");
            c.Asset("surface", AssetCategories.SurfacePaving, ControlTier.More, "skate");
            c.Slider("benches", 6, 0, 16, 1, "count", ControlTier.More, "skate");
            c.Toggle("lamps", true, ControlTier.More, "skate");
            c.Markers("entrances", ControlTier.More, EntranceKind);
            DefineStreetShopControls(c);
        }

        protected override void DefinePresets()
        {
            Preset("street", ("ramps", 10.0), ("paved", 90.0), ("graffiti", true));
            Preset("bowl", ("ramps", 5.0), ("paved", 60.0), ("graffiti", false));
        }

        protected override void PlanCore(LayerContext ctx)
        {
            var region = ctx.LayerRegion;
            var occ = new Occupancy(region, ctx.Input.Reserved, ctx.Site);
            var gates = Gates(ctx);
            ctx.Surface(region.Ring, SurfaceRole.Grass, AssetMix.One(""));
            var c = region.DeepestPoint(out float ins, null, 14);
            float r = math.max(6f, ins * math.sqrt(ctx.F("paved") / 100f));
            var pad = region.InsetVertices(math.max(1f, ins - r));
            if (!Geometry.Poly.IsSimple(pad) || Geometry.Poly.SignedArea(pad) < 50f) pad = LayerRegion.Circle(c, math.min(r, ins - 1f), 24);
            string padCat = AssetCategories.SurfacePaving;
            ctx.Surface(pad, SurfaceRole.Paving, AssetMix.One(ctx.S("surface")), padCat);
            var padRegion = LayerRegion.From(pad) ?? region;
            foreach (var g in gates) LeisureKit.Path(ctx, occ, g.Position, padRegion.ClosestOnBoundary(g.Position, out _, out _), NetRole.Pathway, AssetMix.One(""));

            var ids = ctx.L("rampAssets");
            int ramps = 0;
            if (AssetAvailability.Require(ctx.Plan, AssetCategories.SportsSkate, ids, Keys.Category(AssetCategories.SportsSkate)))
            {
                int wanted = ctx.I("ramps");
                const float golden = 2.39996323f;
                for (int i = 0; i < wanted * 3 && ramps < wanted; i++)
                {
                    float rr = 5f * math.sqrt(i + 0.5f);
                    var p = c + new float2(math.cos(i * golden), math.sin(i * golden)) * rr;
                    if (!padRegion.ContainsDisc(p, 2.5f)) continue;
                    uint v = Hashing.Mix(ctx.Seed, 0x5CA7u + (uint)i);
                    if (LeisureKit.Put(ctx, occ, p, (v % 8u) * 0.785f, ObjectRole.Furniture, AssetMix.Pick(ids, v), AssetCategories.SportsSkate, 2.2f, v, 0.5f, 1.2f)) ramps++;
                }
            }
            var bench = AssetMix.One("");
            int b = 0;
            for (float s = 0; s < padRegion.Perimeter && b < ctx.I("benches"); s += padRegion.Perimeter / math.max(1, ctx.I("benches")))
            {
                var e = padRegion.PointAt(s, out _, out var inward, out _);
                var p = e - inward * 2f;
                if (LeisureKit.Put(ctx, occ, p, LeisureKit.Yaw(p, c), ObjectRole.Bench, bench, null, 1.2f, Hashing.Mix(ctx.Seed, 0xBE5u + (uint)b))) b++;
            }
            if (ctx.B("lamps"))
                for (int i = 0; i < 6; i++)
                {
                    var e = padRegion.PointAt(padRegion.Perimeter * (i + 0.25f) / 6f, out _, out var inward, out _);
                    LeisureKit.Put(ctx, occ, e - inward * 0.8f, 0f, ObjectRole.Lamp, AssetMix.One(""), null, 0.4f, Hashing.Mix(ctx.Seed, 0x1A5u + (uint)i), 0.3f, 0.1f);
                }
            if (ctx.B("graffiti") && AssetAvailability.Has(AssetCategories.DecalGraffiti))
                for (float s = 1f; s < padRegion.Perimeter; s += 7f)
                {
                    var e = padRegion.PointAt(s, out var t, out var inward, out _);
                    ctx.Object(e + inward * 1.2f, math.atan2(t.x, t.y), ObjectRole.Decal, AssetMix.One(""), Hashing.Mix(ctx.Seed, (uint)s), -1, AssetCategories.DecalGraffiti);
                }
            LeisureKit.Plant(ctx, occ, region, (int)(region.Area / 10000f * 30f), ObjectRole.Tree, ctx.L("x"), 2.4f, 12f, 0.5f, 0x7E5u, p => padRegion.Contains(p));
            EmitGreenRegions(ctx, occ, 60f);
            CommonStats(ctx);
            ctx.Stat("Placemaker.Stat.Equipment", ramps, Keys.UnitCount);
        }
    }

    /// <summary>
    /// Cemetery grounds: straight solemn paths on a grid, sections of graves in rows (all facing the
    /// same way), hedges round each section, a chapel / memorial at the crossing, cypress-like tree rows.
    /// </summary>
    [AreaGenerator(59, Group = "parks")]
    public sealed class CemeteryGroundsLayer : LeisureLayer
    {
        protected override string Area => "Cemetery";
        public override string Id => "cemeteryGrounds";
        public override string Icon => "Media/Game/Icons/Deathcare.svg";
        public override float MinArea => 1000f;
        protected override int WantedEntrances(LayerRegion region) => 1;

        protected override List<FeatureInfo> BuildFeatures() => new List<FeatureInfo>
        {
            Feature("restrooms"), StreetShopsFeature(Area), Feature("decals", ("theme", "leaves")), Feature("terrain", ("shape", "flatten")),
        };

        protected override void DefineControls(ControlSet c)
        {
            c.Slider("sectionSize", 24, 12, 40, 2, "m", ControlTier.Essential, "sections").Layout(12).Tip();
            c.Slider("graveSpacing", 2.5, 1.8, 4, 0.1, "m", ControlTier.Essential, "sections");
            c.Toggle("hedges", true, ControlTier.Essential, "sections");
            c.Toggle("chapel", true, ControlTier.Essential, "sections").Tip();
            c.AssetMulti("graveAssets", AssetCategories.Grave, ControlTier.More, "sections");
            c.Asset("chapelAsset", AssetCategories.CenterpieceStatue, ControlTier.More, "sections").When("chapel=true");
            c.Asset("treeAsset", AssetCategories.Tree, ControlTier.More, "sections");
            c.Toggle("fence", true, ControlTier.More, "boundary");
            c.Markers("entrances", ControlTier.More, EntranceKind);
            DefineStreetShopControls(c);
        }

        protected override void DefinePresets()
        {
            Preset("churchyard", ("sectionSize", 16.0), ("graveSpacing", 2.2), ("hedges", true));
            Preset("lawn", ("sectionSize", 32.0), ("graveSpacing", 3.0), ("hedges", false));
        }

        protected override void PlanCore(LayerContext ctx)
        {
            var region = ctx.LayerRegion;
            var occ = new Occupancy(region, ctx.Input.Reserved, ctx.Site);
            var gates = Gates(ctx);
            ctx.Surface(region.Ring, SurfaceRole.Grass, AssetMix.One(""));
            var graph = ParkPathStyles.Formal(region, LeisureKit.Points(gates), p => region.ContainsDisc(p, 2f));
            var curves = ParkPaths.Build(graph, region, false, "narrow");
            ParkPaths.Index(curves, occ.Corridors, 100);
            ParkPaths.Index(curves, occ.Paths, 100);
            ParkPaths.Emit(ctx, curves, gates, AssetMix.One(""));
            LeisureKit.Frame(region, out var centre, out var axis, out var side, out _, out _);

            if (ctx.B("chapel"))
            {
                string cat = LeisureKit.Category(ctx, ctx.S("chapelAsset"), AssetCategories.CenterpieceStatue, AssetCategories.Shelter, AssetCategories.Centerpiece);
                if (cat != null) LeisureKit.Put(ctx, occ, centre + axis * 8f + side * 8f, math.atan2(axis.x, axis.y), cat == AssetCategories.Shelter ? ObjectRole.Furniture : ObjectRole.Centerpiece,
                    AssetMix.One(ctx.S("chapelAsset")), cat, 4f, Hashing.Mix(ctx.Seed, 0xC4Au), 1f, 0.5f, 0);
            }
            float size = ctx.F("sectionSize");
            var sections = LeisureKit.Pack(region, occ, centre, axis, size, size * 0.7f, 3.5f, 3.5f, 400, 2f);
            var ids = ctx.L("graveAssets");
            bool graves = AssetAvailability.Require(ctx.Plan, AssetCategories.Grave, ids, Keys.Category(AssetCategories.Grave));
            string hedgeCat = ctx.B("hedges") ? LeisureKit.Category(ctx, "", AssetCategories.Hedge, AssetCategories.Bush) : null;
            float sp = ctx.F("graveSpacing");
            int count = 0;
            for (int i = 0; i < sections.Count && count < 6000; i++)
            {
                var r = sections[i];
                uint v = Hashing.Mix(ctx.Seed, 0x6A7Eu + (uint)i);
                if (graves)
                    for (float u = -r.HalfLength + 1.2f; u <= r.HalfLength - 1.2f; u += sp)
                        for (float w = -r.HalfWidth + 1.5f; w <= r.HalfWidth - 1.5f; w += sp * 1.4f)
                        {
                            uint gv = Hashing.Mix(v, (uint)(u * 10f) * 977u + (uint)(w * 10f));
                            ctx.Object(r.Local(u, w), r.Yaw, ObjectRole.Furniture, AssetMix.Pick(ids, gv), gv, -1, AssetCategories.Grave);
                            count++;
                        }
                if (hedgeCat != null)
                {
                    var ring = r.Ring;
                    for (int k = 0; k < 4; k++)
                        foreach (var p in LeisureKit.Along(ring[k], ring[(k + 1) % 4], 1.8f))
                            ctx.Object(p, 0f, ObjectRole.Bush, AssetMix.One(""), v, -1, hedgeCat);
                }
            }
            PathLamps(ctx, occ, 30f, AssetMix.One(""), 0x1A6u);
            PathBenches(ctx, occ, 60f, AssetMix.One(""), 0xBE6u);
            LeisureKit.Plant(ctx, occ, region, (int)(region.Area / 10000f * 25f), ObjectRole.Tree, ctx.S("treeAsset").Length > 0 ? new[] { ctx.S("treeAsset") } : null,
                2.2f, 10f, 0.5f, 0x7C6u, p => region.BoundaryDistance(p, 10f) > 6f, 3, 3);
            if (ctx.B("fence")) Fence(ctx, gates, AssetMix.One(""), 3f);
            EmitGreenRegions(ctx, occ, 80f);
            CommonStats(ctx);
            ctx.Stat("Placemaker.Stat.Graves", count, Keys.UnitCount);
        }
    }

    /// <summary>
    /// Greenway / linear park: one main walk (wide, meandering gently) along the long axis of a strip,
    /// a narrow parallel path where the strip is wide enough, planting along both sides, and rest stops
    /// (bench pair, bin, shade tree) every N metres.
    /// </summary>
    [AreaGenerator(60, Group = "nature")]
    public sealed class GreenwayParkLayer : LeisureLayer
    {
        protected override string Area => "Greenway";
        public override string Id => "greenwayPark";
        public override string Icon => "Media/Game/Icons/PedestrianPathWithBikeLanes.svg";
        public override float MinArea => 600f;
        protected override int WantedEntrances(LayerRegion region) => 2;

        protected override List<FeatureInfo> BuildFeatures() => new List<FeatureInfo>
        {
            Feature("restrooms"), Feature("shops", ("count", 2.0), ("where", "paths")), CoreFeature("playground", ("size", "small")), StreetShopsFeature(Area),
            Feature("decals", ("theme", "leaves")), Feature("terrain", ("shape", "berms"), ("height", 1.5)),
        };

        protected override void DefineControls(ControlSet c)
        {
            c.Slider("restStops", 60, 25, 200, 5, "m", ControlTier.Essential, "layout").Layout(20).Tip();
            c.Slider("treeDensity", 60, 0, 200, 5, "perHa", ControlTier.Essential, "planting");
            c.Toggle("secondPath", true, ControlTier.Essential, "layout").Tip();
            c.Slider("bushDensity", 80, 0, 250, 5, "perHa", ControlTier.More, "planting");
            c.Asset("pathAsset", AssetCategories.Pathway, ControlTier.More, "layout");
            c.Toggle("lamps", true, ControlTier.More, "layout");
            c.Markers("entrances", ControlTier.More, EntranceKind);
            DefineStreetShopControls(c);
        }

        protected override void DefinePresets()
        {
            Preset("leafy", ("treeDensity", 110.0), ("bushDensity", 120.0));
            Preset("open", ("treeDensity", 30.0), ("bushDensity", 40.0), ("secondPath", false));
        }

        protected override void PlanCore(LayerContext ctx)
        {
            var region = ctx.LayerRegion;
            var occ = new Occupancy(region, ctx.Input.Reserved, ctx.Site);
            var gates = Gates(ctx);
            ctx.Surface(region.Ring, SurfaceRole.Grass, AssetMix.One(""));
            NaturalPaths(ctx, occ, gates, 0, 4f, 40f, "narrow", AssetMix.One(ctx.S("pathAsset")));
            LeisureKit.Frame(region, out var centre, out var axis, out var side, out _, out float minor);
            if (ctx.B("secondPath") && minor > 28f && LeisureKit.Chord(region, centre + side * (minor * 0.28f), axis, 4f, out var a, out var b))
                LeisureKit.Path(ctx, occ, a, b, NetRole.Pathway, AssetMix.One(ctx.S("pathAsset")));
            // Rest stops.
            var bench = AssetMix.One(""); int stops = 0;
            foreach (var an in PathAnchors.Pick(PathAnchors.Beside(occ, 3f, 8f, 0.6f), 200, ctx.F("restStops"), false))
            {
                var t = an.Tangent;
                bool ok = LeisureKit.Put(ctx, occ, an.Position + t * 1.4f, LeisureKit.Yaw(an.Position, an.Toward), ObjectRole.Bench, bench, null, 1.2f, Hashing.Mix(ctx.Seed, 0x5700u + (uint)stops));
                LeisureKit.Put(ctx, occ, an.Position - t * 1.4f, LeisureKit.Yaw(an.Position, an.Toward), ObjectRole.Bench, bench, null, 1.2f, Hashing.Mix(ctx.Seed, 0x5780u + (uint)stops));
                LeisureKit.Put(ctx, occ, an.Position - t * 3.2f, 0f, ObjectRole.Bin, AssetMix.One(""), null, 0.5f, Hashing.Mix(ctx.Seed, 0x5800u + (uint)stops));
                var away = math.normalizesafe(an.Position - an.Toward);
                LeisureKit.Put(ctx, occ, an.Position + away * 4f, 0f, ObjectRole.Tree, AssetMix.One(""), null, 2.4f, Hashing.Mix(ctx.Seed, 0x5880u + (uint)stops), 1f, 0.3f, 3, 3);
                if (ok) stops++;
            }
            if (ctx.B("lamps")) PathLamps(ctx, occ, 25f, AssetMix.One(""), 0x1A7u);
            float ha = region.Area / 10000f;
            LeisureKit.Plant(ctx, occ, region, (int)(ha * ctx.F("treeDensity")), ObjectRole.Tree, null, 2.4f, 18f, 0.35f, 0x7E7u);
            LeisureKit.Plant(ctx, occ, region, (int)(ha * ctx.F("bushDensity")), ObjectRole.Bush, null, 1f, 8f, 0.3f, 0xB07u);
            EmitGreenRegions(ctx, occ, 80f);
            CommonStats(ctx);
            ctx.Plan.Facts["restStops"] = stops;
        }
    }
}
