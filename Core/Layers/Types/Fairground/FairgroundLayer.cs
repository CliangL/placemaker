// Fairground / festival site. Real structure instead of a scatter:
//   * a stage at one end facing an open crowd field (kept open: a "place" no feature may fill),
//     with a fenced backstage yard behind or beside it, a crowd barrier, a front-of-house tent;
//   * circulation by layout — a main avenue with side streets, a loop promenade round the arena,
//     a market grid of aisles, or a central dining court ringed by food trucks;
//   * stalls (market stalls, tents or food trucks) lining both sides of every street, facing it,
//     with a festoon pole every few pitches;
//   * a food court (paved, tables, kiosks, string lights), rides on paved pads, a landmark
//     (big wheel, Christmas tree, centrepiece), portable toilet blocks, light towers;
//   * temporary fencing round the outline with gates at the entrances, worn-ground paint along
//     the streets and over the field, a shade belt of trees at the edge.
// Every category falls back (food truck → kiosk, light tower → floodlight → lamp…) and missing
// optional extras are skipped quietly.

using System;
using System.Collections.Generic;
using Placemaker.Core.Geometry;
using Placemaker.Core.Layers.Common;
using Placemaker.Core.Layers.Leisure;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Types.Events
{
    [AreaGenerator(80, Group = "events")]
    public sealed class FairgroundLayer : LeisureLayer
    {
        public const string FieldPlace = Composer.PlacePrefix + "field";
        private const string K = "Placemaker.Fairground.";

        protected override string Area => "Fairground";
        public override string Id => "fairground";
        public override string Icon => "Media/Game/Icons/Entertainment.svg";
        public override float MinArea => 1500f;

        protected override List<FeatureInfo> BuildFeatures() => new List<FeatureInfo>
        {
            Feature(LightTowersAddOn.LayerId), Feature(FestivalCampingAddOn.LayerId), Feature("restrooms", ("count", 1.0)),
            Feature("shops", ("count", 2.0), ("where", "entrances")), Feature("picnic", ("groups", 2.0)), BigTrees(),
            StreetShopsFeature(Area), Feature("decals", ("theme", "dirt"), ("ground", "lawn"), ("density", 1.0)), Feature("terrain", ("shape", "flatten")),
        };

        /// <summary>Big trees feature with the park's existing label.</summary>
        internal static FeatureInfo BigTrees()
        {
            var f = Feature("bigtrees");
            f.LabelKey = "Placemaker.Park.Feature.bigtrees";
            return f;
        }

        protected override int WantedEntrances(LayerRegion region) => math.clamp(Entrances.WantedByArea(region.Area), 1, 4);

        protected override void DefineControls(ControlSet c)
        {
            c.Choice("style", "fairground", ControlTier.Essential, "fairLayout", "fairground", "festival", "funfair", "christmas", "foodTrucks").Tip();
            c.Choice("layout", "avenue", ControlTier.Essential, "fairLayout", "avenue", "loop", "grid", "court").Tip();
            c.Slider("stalls", 24, 0, 160, 1, "count", ControlTier.Essential, "fairLayout");
            c.Toggle("stage", true, ControlTier.Essential, "fairLayout").Tip();
            c.Slider("field", 25, 5, 60, 5, "%", ControlTier.Essential, "fairLayout").When("stage=true").Tip();

            c.Choice("stallKind", "stalls", ControlTier.More, "fairStalls", "stalls", "tents", "trucks").Tip();
            c.Slider("streetWidth", 7, 4, 14, 0.5, "m", ControlTier.More, "fairStalls").Tip();
            c.Slider("sideStreets", 3, 0, 8, 1, "count", ControlTier.More, "fairStalls").Tip();
            c.Slider("stallPitch", 5, 3, 10, 0.5, "m", ControlTier.More, "fairStalls").Tip();
            c.Asset("stallAsset", AssetCategories.MarketStall, ControlTier.More, "fairStalls");
            c.Slider("rides", 2, 0, 24, 1, "count", ControlTier.More, "fairStalls").Tip();
            c.AssetMulti("rideAssets", AssetCategories.EventRide, ControlTier.More, "fairStalls");
            c.Toggle("landmark", false, ControlTier.More, "fairStalls").Tip();
            c.Toggle("foodCourt", true, ControlTier.More, "fairStalls").Tip();
            c.Slider("tables", 10, 2, 40, 1, "count", ControlTier.More, "fairStalls").When("foodCourt=true");

            c.Choice("stageSize", "medium", ControlTier.More, "fairStage", "small", "medium", "large");
            c.Choice("stageEnd", "far", ControlTier.More, "fairStage", "far", "near", "side").Tip();
            c.Toggle("backstage", true, ControlTier.More, "fairStage").Tip();
            c.Toggle("barrier", true, ControlTier.More, "fairStage").Tip();
            c.Toggle("foh", true, ControlTier.More, "fairStage").Tip();
            c.Asset("stageAsset", AssetCategories.Stage, ControlTier.More, "fairStage");

            c.Slider("toilets", 8, 0, 40, 1, "count", ControlTier.More, "fairService").Tip();
            c.Asset("toiletAsset", AssetCategories.EventToilet, ControlTier.More, "fairService");
            c.Slider("lightSpacing", 40, 0, 100, 5, "m", ControlTier.More, "fairService").Tip();
            c.Asset("lightAsset", AssetCategories.EventLightTower, ControlTier.More, "fairService");
            c.Toggle("festoons", true, ControlTier.More, "fairService").Tip();

            c.Toggle("fence", true, ControlTier.More, "fairSite").Tip();
            c.Slider("gateWidth", 7, 3, 14, 1, "m", ControlTier.More, "fairSite").When("fence=true");
            c.Asset("fenceAsset", AssetCategories.Fence, ControlTier.More, "fairSite").When("fence=true");
            c.Choice("ground", "grass", ControlTier.More, "fairSite", "grass", "gravel", "paved");
            c.Toggle("worn", true, ControlTier.More, "fairSite").Tip();
            c.Slider("paintLayer", 1, 1, 4, 1, "count", ControlTier.More, "fairSite").When("worn=true").Tip();
            c.Slider("treeDensity", 12, 0, 60, 2, "perHa", ControlTier.More, "fairSite").Tip();
            c.AssetMulti("treeSpecies", AssetCategories.Tree, ControlTier.More, "fairSite");

            c.Asset("pathAsset", AssetCategories.Pathway, ControlTier.More, "assets");
            c.Asset("benchAsset", AssetCategories.Bench, ControlTier.More, "assets");
            c.Asset("lampAsset", AssetCategories.Lamp, ControlTier.More, "assets");
            c.Markers("entrances", ControlTier.More, EntranceKind);
            DefineStreetShopControls(c);

            c.RandomRange("stalls", 12, 48);
            c.RandomRange("rides", 0, 6);
            c.RandomRange("toilets", 4, 16);
            c.Fixed("style");
            c.Fixed("layout");
            c.Fixed("ground");
        }

        protected override void DefinePresets()
        {
            Preset("fairground", ("style", "fairground"), ("layout", "avenue"), ("stalls", 30.0), ("rides", 3.0), ("stage", true), ("field", 20.0),
                ("stageSize", "medium"), ("foodCourt", true), ("toilets", 6.0), ("festoons", true), ("landmark", false), ("ground", "grass"));
            Preset("festival", ("style", "festival"), ("layout", "loop"), ("stalls", 24.0), ("rides", 0.0), ("stage", true), ("field", 45.0),
                ("stageSize", "large"), ("stallKind", "tents"), ("toilets", 24.0), ("lightSpacing", 30.0), ("backstage", true), ("barrier", true),
                ("foh", true), ("foodCourt", true), ("tables", 16.0), ("worn", true), ("treeDensity", 6.0));
            Preset("funfair", ("style", "funfair"), ("layout", "avenue"), ("stalls", 20.0), ("rides", 12.0), ("stage", false), ("landmark", true),
                ("sideStreets", 2.0), ("streetWidth", 9.0), ("festoons", true), ("ground", "gravel"), ("lightSpacing", 35.0), ("foodCourt", true), ("tables", 8.0));
            Preset("christmas", ("style", "christmas"), ("layout", "grid"), ("stalls", 40.0), ("stallPitch", 4.0), ("streetWidth", 5.0), ("rides", 1.0),
                ("stage", true), ("stageSize", "small"), ("field", 10.0), ("landmark", true), ("festoons", true), ("ground", "paved"), ("worn", false),
                ("treeDensity", 6.0), ("fence", false), ("toilets", 4.0), ("backstage", false), ("barrier", false), ("foh", false), ("lightSpacing", 0.0));
            Preset("foodTrucks", ("style", "foodTrucks"), ("layout", "court"), ("stallKind", "trucks"), ("stalls", 14.0), ("stallPitch", 8.0),
                ("foodCourt", true), ("tables", 24.0), ("stage", true), ("stageSize", "small"), ("field", 10.0), ("rides", 0.0), ("festoons", true),
                ("ground", "gravel"), ("fence", false), ("backstage", false), ("barrier", false), ("foh", false), ("toilets", 6.0), ("sideStreets", 1.0));
        }

        // ───────────────────────────── planning ─────────────────────────────

        private sealed class Street { public float2 A, B; public NetRole Role; }

        private struct StageSite
        {
            public bool Ok, HasField;
            public OrientedRect Stage, Field;
            public float2 Dir;   // from the field towards the stage
        }

        protected override void PlanCore(LayerContext ctx)
        {
            var region = ctx.LayerRegion;
            var occ = new Occupancy(region, ctx.Input.Reserved, ctx.Site);
            var gates = Gates(ctx);
            string style = ctx.S("style"), layout = ctx.S("layout");
            var pathAsset = AssetMix.One(ctx.S("pathAsset"));
            Ground(ctx, region);
            LeisureKit.Frame(region, out var centre, out var axis, out var side, out _, out float minor);
            var mainGate = gates.Count > 0 ? gates[0].Position : region.Ring[0];
            float g = math.dot(mainGate - centre, axis);
            var inDir = g > 0f ? -axis : axis;   // from the entrance end towards the far end

            // Loop inset first: the stage and field then go inside the loop.
            float loopInset = math.clamp(minor * 0.2f, 10f, 28f);
            float2[] loop = null;
            if (layout == "loop")
            {
                loop = region.InsetVertices(loopInset);
                bool ok = loop.Length >= 3 && Poly.IsSimple(loop) && Poly.SignedArea(loop) > 600f;
                if (ok) foreach (var v in loop) if (!region.ContainsDisc(v, loopInset * 0.5f)) { ok = false; break; }
                if (!ok) { loop = null; ctx.Issue(IssueSeverity.Info, K + "Issue.LoopFallback"); }
            }
            ctx.Check();

            // 1. Stage and crowd field.
            var st = new StageSite();
            if (ctx.B("stage"))
            {
                float inset = loop != null ? loopInset + ctx.F("streetWidth") * 0.5f + 8f : 3f;
                st = PlanStage(ctx, occ, region, centre, inDir, side, mainGate, inset);
            }
            ctx.Check();

            // 2. Streets by layout (planned against the stage/field claims, then emitted and indexed).
            OrientedRect court = default; bool hasCourt = false;
            if (layout == "court")
            {
                hasCourt = PlanCourtRect(ctx, region, occ, centre, axis, inDir, out court);
                if (hasCourt) EventKit.TakeRectTight(occ, court, 6);
            }
            var streets = PlanStreets(ctx, region, occ, centre, inDir, side, loop, layout);
            foreach (var s in streets) LeisureKit.Path(ctx, occ, s.A, s.B, s.Role, pathAsset);
            var spine = new List<(float2 a, float2 b)>();
            foreach (var s in streets) spine.Add((s.A, s.B));
            if (hasCourt) foreach (var e in Edges(court, 3.5f)) spine.Add(e);
            Connect(ctx, occ, region, gates, spine, pathAsset);
            ctx.Check();

            // 3. Food court (dining court for the court layout).
            int tables = 0;
            if (hasCourt) tables = FurnishCourt(ctx, occ, court, style, true);
            else if (ctx.B("foodCourt"))
            {
                if (FindFoodCourt(ctx, region, occ, streets, out court))
                {
                    hasCourt = true;
                    EventKit.TakeRectTight(occ, court, 6);
                    tables = FurnishCourt(ctx, occ, court, style, false);
                }
                else ctx.Issue(IssueSeverity.Info, K + "Issue.NoFoodCourt");
            }
            ctx.Check();

            // 4. Landmark and rides (prime spots by the paths, before the stalls fill the frontage).
            if (ctx.B("landmark")) Landmark(ctx, occ, style);
            int rides = Rides(ctx, occ);
            ctx.Check();

            // 5. Stalls.
            int stalls = Stalls(ctx, region, occ, streets, court, hasCourt && layout == "court", centre, axis, layout == "grid", style);
            ctx.Check();

            // 6. Service: toilets, light towers, bins, benches; fencing.
            int toilets = Toilets(ctx, occ, st);
            Lights(ctx, occ, st);
            // Bins beside the streets.
            {
                int n = 0;
                foreach (var a in PathAnchors.Pick(PathAnchors.Beside(occ, 0.5f, 8f, 0.5f), math.clamp(streets.Count * 2 + 2, 2, 30), 25f, false))
                    if (LeisureKit.Put(ctx, occ, a.Position, LeisureKit.Yaw(a.Position, a.Toward), ObjectRole.Bin, AssetMix.One(""), null, 0.5f, Hashing.Mix(ctx.Seed, 0xB100u + (uint)n), 0.4f, 0.2f)) n++;
            }
            PathBenches(ctx, occ, 45f, AssetMix.One(ctx.S("benchAsset")), 0xBE70u);
            if (ctx.B("fence")) Fence(ctx, gates, AssetMix.One(ctx.S("fenceAsset")), ctx.F("gateWidth") * 0.5f);
            ctx.Check();

            // 7. Worn ground and the shade belt.
            if (ctx.B("worn") && ctx.S("ground") != "paved")
            {
                int layer = ctx.I("paintLayer");
                float hw = ctx.F("streetWidth") * 0.5f + 1.5f;
                foreach (var s in streets)
                {
                    var d = math.normalizesafe(s.B - s.A);
                    var r = new OrientedRect { Center = (s.A + s.B) * 0.5f, Axis = d, HalfLength = math.distance(s.A, s.B) * 0.5f + 1f, HalfWidth = hw };
                    EventKit.Wear(ctx, region, r.Ring, layer, 0.7f);
                }
                if (st.HasField) EventKit.Wear(ctx, region, st.Field.Ring, layer, 0.45f);
            }
            int trees = (int)(region.Area / 10000f * ctx.F("treeDensity"));
            if (trees > 0 && AssetAvailability.Require(ctx.Plan, AssetCategories.Tree, ctx.L("treeSpecies"), Keys.Category(AssetCategories.Tree)))
                LeisureKit.Plant(ctx, occ, region, trees, ObjectRole.Tree, ctx.L("treeSpecies"), 2.5f, 20f, 0.45f, 0x7F00u,
                    p => region.BoundaryDistance(p, 30f) > 14f || (st.HasField && EventKit.InRect(st.Field, p, 2f)));
            ctx.Check();

            // 8. Regions: the crowd field stays open (a place), the court is paved, the rest is green.
            if (st.HasField) ctx.AddRegion(st.Field.Ring, "field", FieldPlace, RegionTags.Green, RegionTags.Core);
            if (hasCourt) ctx.AddRegion(court.Ring, "court", RegionTags.Paved);
            var field = st;
            var pieces = FreeSpace.Pieces(region, p => occ.Corridors.Overlaps(p, 0.75f) || occ.HitsPolygon(p, 0.5f) || (field.HasField && EventKit.InRect(field.Field, p, 1f)),
                2.5f, 150f, ctx.Cancel);
            for (int i = 0; i < pieces.Count && i < 24; i++)
            {
                if (i == 0 && !st.HasField) ctx.AddRegion(pieces[i], "green:0", RegionTags.Green, RegionTags.Core);
                else ctx.AddRegion(pieces[i], "green:" + i, RegionTags.Green);
            }
            if (pieces.Count == 0 && !st.HasField) ctx.AddRegion(region.Ring, "green:0", RegionTags.Green, RegionTags.Core);

            CommonStats(ctx);
            ctx.Stat("Placemaker.Stat.Stalls", stalls, Keys.UnitCount);
            ctx.Stat("Placemaker.Stat.Rides", rides, Keys.UnitCount);
            ctx.Stat("Placemaker.Stat.Toilets", toilets, Keys.UnitCount);
            if (st.HasField) ctx.Stat("Placemaker.Stat.FieldArea", Math.Round(st.Field.HalfLength * st.Field.HalfWidth * 4f), Keys.UnitM2);
            ctx.Plan.Facts["stalls"] = stalls;
            ctx.Plan.Facts["rides"] = rides;
            ctx.Plan.Facts["tables"] = tables;
            ctx.Plan.Facts["streets"] = streets.Count;
        }

        private static void Ground(LayerContext ctx, LayerRegion region)
        {
            var asset = AssetMix.One("");
            switch (ctx.S("ground"))
            {
                case "paved":
                    ctx.NavigationArea(region.Ring, AreaRole.PedestrianArea, new AssetRef("", "Pedestrian Area", "Walking Area"));
                    ctx.Surface(region.Ring, SurfaceRole.Plaza, asset, AssetCategories.SurfacePaving);
                    break;
                case "gravel":
                    ctx.Surface(region.Ring, SurfaceRole.Ground, asset, AssetCategories.SurfaceNatural);
                    break;
                default:
                    ctx.Surface(region.Ring, SurfaceRole.Grass, asset);
                    break;
            }
        }

        // ───────────── stage, backstage, field ─────────────

        private StageSite PlanStage(LayerContext ctx, Occupancy occ, LayerRegion region, float2 centre, float2 inDir, float2 side, float2 mainGate, float inset)
        {
            var st = new StageSite();
            float2 dir;
            switch (ctx.S("stageEnd"))
            {
                case "near": dir = -inDir; break;
                case "side": dir = math.dot(mainGate - centre, side) > 0f ? -side : side; break;
                default: dir = inDir; break;
            }
            float sd, sw;
            switch (ctx.S("stageSize"))
            {
                case "small": sd = 8f; sw = 10f; break;
                case "large": sd = 16f; sw = 26f; break;
                default: sd = 12f; sw = 16f; break;
            }
            if (!LeisureKit.Chord(region, centre, dir, 0f, out var ca, out var cb)) { ctx.Issue(IssueSeverity.Info, K + "Issue.NoStage"); return st; }
            var end = math.dot(cb - ca, dir) >= 0f ? cb : ca;
            float chord = math.distance(ca, cb);
            var across = new float2(-dir.y, dir.x);
            bool found = false;
            OrientedRect stage = default;
            for (float t = inset + sd * 0.5f; t < chord * 0.7f && !found; t += 2f)
                for (float shift = 0f; shift <= 24f && !found; shift += 8f)
                    for (int sgn = -1; sgn <= 1 && !found; sgn += 2)
                    {
                        if (shift == 0f && sgn > 0) continue;
                        var c = end - dir * t + across * (shift * sgn);
                        var r = new OrientedRect { Center = c, Axis = across, HalfLength = sw * 0.5f, HalfWidth = sd * 0.5f };
                        if (LeisureKit.RectFits(region, occ, r, 1f)) { stage = r; found = true; }
                    }
            if (!found) { ctx.Issue(IssueSeverity.Info, K + "Issue.NoStage"); return st; }
            st.Ok = true; st.Stage = stage; st.Dir = dir;
            EventKit.TakeRectTight(occ, stage, 0);
            ctx.Surface(stage.Ring, SurfaceRole.Paving, AssetMix.One(""), AssetCategories.SurfacePaving);
            float facing = math.atan2(-dir.x, -dir.y);
            string stageCat = LeisureKit.Category(ctx, ctx.S("stageAsset"), AssetCategories.Stage, AssetCategories.Shelter, AssetCategories.Centerpiece);
            if (stageCat != null) EventKit.Place(ctx, stage.Center, facing, stageCat, AssetMix.One(ctx.S("stageAsset")), Hashing.Mix(ctx.Seed, 0x57A6u));

            // Crowd field in front of the stage, shrunk until it fits.
            float want = region.Area * ctx.F("field") / 100f;
            float w0 = math.max(sw + 8f, math.sqrt(want * 1.25f)), l0 = math.max(10f, want / w0);
            foreach (float s in new[] { 1f, 0.85f, 0.7f, 0.55f, 0.4f, 0.28f })
            {
                float w = math.max(sw + 4f, w0 * math.sqrt(s)), l = math.max(8f, l0 * math.sqrt(s));
                var fc = stage.Center - dir * (sd * 0.5f + 2f + l * 0.5f);
                var fr = new OrientedRect { Center = fc, Axis = across, HalfLength = w * 0.5f, HalfWidth = l * 0.5f };
                if (!LeisureKit.RectFits(region, occ, fr, 1.5f)) continue;
                st.Field = fr; st.HasField = true;
                break;
            }
            if (!st.HasField) ctx.Issue(IssueSeverity.Info, K + "Issue.SmallField");
            else EventKit.TakeRectTight(occ, st.Field, 0);

            // Speakers and screens beside the stage front (optional props, quiet when missing).
            string speaker = EventKit.Optional("", AssetCategories.EventSpeaker);
            string screen = EventKit.Optional("", AssetCategories.EventScreen);
            for (int k = -1; k <= 1; k += 2)
            {
                if (speaker != null) EventKit.Put(ctx, occ, stage.Center + across * (k * (sw * 0.5f + 1.5f)) - dir * (sd * 0.35f), facing, speaker, AssetMix.One(""), 1f, Hashing.Mix(ctx.Seed, 0x5E00u + (uint)(k + 2)), 0.5f, 0.1f);
                if (screen != null) EventKit.Put(ctx, occ, stage.Center + across * (k * (sw * 0.5f + 4.5f)), facing, screen, AssetMix.One(""), 1.8f, Hashing.Mix(ctx.Seed, 0x5C00u + (uint)(k + 2)), 0.5f, 0.1f);
            }

            // Backstage yard: behind the stage, else beside it. Gravel, fenced, a gate towards the stage.
            if (ctx.B("backstage"))
            {
                var cands = new[]
                {
                    stage.Center + dir * (sd * 0.5f + 1.5f + 5f),
                    stage.Center + across * (sw * 0.5f + 2f + 7f), stage.Center - across * (sw * 0.5f + 2f + 7f),
                };
                bool placed = false;
                foreach (var c in cands)
                {
                    var r = new OrientedRect { Center = c, Axis = across, HalfLength = 7f, HalfWidth = 5f };
                    if (!LeisureKit.RectFits(region, occ, r, 0.8f)) continue;
                    EventKit.TakeRectTight(occ, r, 0);
                    Backstage(ctx, r, stage.Center);
                    placed = true;
                    break;
                }
                if (!placed) ctx.Issue(IssueSeverity.Info, K + "Issue.NoBackstage");
            }

            if (st.HasField)
            {
                if (ctx.B("barrier"))
                {
                    var b0 = stage.Center - dir * (sd * 0.5f + 1.2f);
                    float half = math.min(sw * 0.5f + 2f, st.Field.HalfLength - 0.5f);
                    ctx.StraightNet(b0 - across * half, b0 + across * half, NetRole.Fence, AssetMix.One(ctx.S("fenceAsset")));
                }
                if (ctx.B("foh") && st.Field.HalfWidth > 9f)
                {
                    string tent = EventKit.Optional("", AssetCategories.EventTent, AssetCategories.Shelter);
                    if (tent != null)
                    {
                        var p = st.Field.Center - dir * (st.Field.HalfWidth * 0.25f);
                        EventKit.Place(ctx, p, math.atan2(dir.x, dir.y), tent, AssetMix.One(""), Hashing.Mix(ctx.Seed, 0xF0Fu));
                    }
                }
            }
            return st;
        }

        private static void Backstage(LayerContext ctx, OrientedRect r, float2 stage)
        {
            ctx.Surface(r.Ring, SurfaceRole.Ground, AssetMix.One(""), AssetCategories.SurfaceNatural);
            var ring = r.Ring;
            var fence = AssetMix.One(ctx.S("fenceAsset"));
            int gateEdge = 0; float best = float.MaxValue;
            for (int k = 0; k < 4; k++)
            {
                float d = math.distance(math.lerp(ring[k], ring[(k + 1) % 4], 0.5f), stage);
                if (d < best) { best = d; gateEdge = k; }
            }
            for (int k = 0; k < 4; k++)
            {
                float2 a = ring[k], b = ring[(k + 1) % 4];
                if (k == gateEdge)
                {
                    var mid = math.lerp(a, b, 0.5f); var d = math.normalizesafe(b - a);
                    ctx.StraightNet(a, mid - d * 2f, NetRole.Fence, fence);
                    ctx.StraightNet(mid + d * 2f, b, NetRole.Fence, fence);
                }
                else ctx.StraightNet(a, b, NetRole.Fence, fence);
            }
            string props = EventKit.Optional("", AssetCategories.EventBackstage, AssetCategories.GardenShed);
            if (props == null) return;
            int n = 0;
            foreach (var (u, v) in new[] { (-4f, 2.5f), (0f, 2.5f), (4f, 2.5f), (-4f, -2.5f) })
            {
                var p = r.Local(u, v);
                EventKit.Place(ctx, p, r.Yaw, props, AssetMix.One(""), Hashing.Mix(ctx.Seed, 0xBA00u + (uint)n++));
            }
        }

        // ───────────── streets ─────────────

        private List<Street> PlanStreets(LayerContext ctx, LayerRegion region, Occupancy occ, float2 centre, float2 inDir, float2 side, float2[] loop, string layout)
        {
            var streets = new List<Street>();
            Func<float2, bool> blocked = p => occ.Discs.Overlaps(p, 2f) || occ.Corridors.Overlaps(p, 2f) || occ.HitsPolygon(p, 2f);
            if (loop != null)
            {
                for (int i = 0; i < loop.Length; i++)
                {
                    var a = loop[i]; var b = loop[(i + 1) % loop.Length];
                    if (math.distance(a, b) < 4f) continue;
                    if (EventKit.Trim(a, b, math.lerp(a, b, 0.5f), blocked, 4f, out var ta, out var tb))
                        streets.Add(new Street { A = ta, B = tb, Role = NetRole.PathwayWide });
                }
                return streets;
            }

            // Main avenue along the long axis from the entrance end; it stops where the field begins.
            if (!LeisureKit.Chord(region, centre, inDir, 2.5f, out var ma, out var mb)) return streets;
            if (math.dot(ma - centre, inDir) > 0f) { var t = ma; ma = mb; mb = t; }
            float2 a0 = default, b0 = default; bool main = false;
            foreach (float f in new[] { 0.5f, 0.35f, 0.2f, 0.65f, 0.08f })
                if (EventKit.Trim(ma, mb, math.lerp(ma, mb, f), blocked, 12f, out a0, out b0)) { main = true; break; }
            if (!main) return streets;
            streets.Add(new Street { A = a0, B = b0, Role = NetRole.PathwayWide });
            if (layout == "court") return streets;
            int k = layout == "grid" ? math.min(1, ctx.I("sideStreets")) : ctx.I("sideStreets");
            for (int i = 1; i <= k; i++)
            {
                var p = math.lerp(a0, b0, (float)i / (k + 1));
                if (!LeisureKit.Chord(region, p, side, 3f, out var sa, out var sb)) continue;
                if (EventKit.Trim(sa, sb, p, blocked, 10f, out var ta, out var tb))
                    streets.Add(new Street { A = ta, B = tb, Role = NetRole.Pathway });
            }
            return streets;
        }

        /// <summary>Gate connections that never run through the stage, field or court (sampled before the path end).</summary>
        private static void Connect(LayerContext ctx, Occupancy occ, LayerRegion region, List<Gate> gates, List<(float2 a, float2 b)> spine, AssetRef asset)
        {
            foreach (var g in gates)
            {
                var feet = new List<(float d, float2 q)>();
                foreach (var (a, b) in spine)
                {
                    var q = SegmentIndex.ClosestPoint(g.Position, a, b);
                    feet.Add((math.distance(q, g.Position), q));
                }
                feet.Sort((x, y) => x.d.CompareTo(y.d));
                foreach (var (d, q) in feet)
                {
                    if (d <= 1f) break;
                    if (region.CrossesBoundary(g.Position, q, true)) continue;
                    bool clear = true;
                    var dir = (q - g.Position) / d;
                    for (float t = 1.5f; t < d - 3f; t += 1.5f)
                    {
                        var p = g.Position + dir * t;
                        if (occ.Discs.Overlaps(p, 1.6f) || occ.Corridors.Overlaps(p, 1.6f)) { clear = false; break; }
                    }
                    if (!clear) continue;
                    LeisureKit.Path(ctx, occ, g.Position, q, NetRole.Pathway, asset);
                    break;
                }
            }
        }

        private static List<(float2 a, float2 b)> Edges(OrientedRect r, float grow)
        {
            var g = new OrientedRect { Center = r.Center, Axis = r.Axis, HalfLength = r.HalfLength + grow, HalfWidth = r.HalfWidth + grow };
            var ring = g.Ring;
            var e = new List<(float2, float2)>();
            for (int i = 0; i < 4; i++) e.Add((ring[i], ring[(i + 1) % 4]));
            return e;
        }

        // ───────────── courts ─────────────

        private static float CourtArea(LayerContext ctx) => 140f + ctx.I("tables") * 14f;

        private static bool PlanCourtRect(LayerContext ctx, LayerRegion region, Occupancy occ, float2 centre, float2 axis, float2 inDir, out OrientedRect court)
        {
            court = default;
            float area = CourtArea(ctx);
            // Room round the court for the truck ring (≈ 9 m).
            foreach (float s in new[] { 1f, 0.8f, 0.6f, 0.45f })
            {
                float l = math.sqrt(area * s * 1.4f), w = area * s / l;
                foreach (float shift in new[] { 0f, 0.12f, -0.12f, 0.25f })
                {
                    var c = centre + inDir * (shift * math.sqrt(region.Area));
                    var r = new OrientedRect { Center = c, Axis = axis, HalfLength = l * 0.5f, HalfWidth = w * 0.5f };
                    if (LeisureKit.RectFits(region, occ, r, 10f)) { court = r; return true; }
                }
            }
            return false;
        }

        private static bool FindFoodCourt(LayerContext ctx, LayerRegion region, Occupancy occ, List<Street> streets, out OrientedRect court)
        {
            court = default;
            float area = CourtArea(ctx);
            float off0 = ctx.F("streetWidth") * 0.5f + 1.5f;
            foreach (float s in new[] { 1f, 0.75f, 0.55f })
            {
                float l = math.sqrt(area * s * 1.5f), w = area * s / l;
                foreach (var st in streets)
                {
                    var d = math.normalizesafe(st.B - st.A);
                    var n = new float2(-d.y, d.x);
                    float len = math.distance(st.A, st.B);
                    if (len < l) continue;
                    foreach (float f in new[] { 0.55f, 0.75f, 0.35f, 0.9f, 0.15f })
                        for (int sg = 1; sg >= -1; sg -= 2)
                        {
                            var c = math.lerp(st.A, st.B, f) + n * sg * (off0 + w * 0.5f + Composer.DefaultHalfWidth(st.Role));
                            var r = new OrientedRect { Center = c, Axis = d, HalfLength = l * 0.5f, HalfWidth = w * 0.5f };
                            if (LeisureKit.RectFits(region, occ, r, 1f)) { court = r; return true; }
                        }
                }
            }
            // No street frontage that fits: the most open spot.
            float l2 = math.sqrt(area * 0.6f * 1.5f), w2 = area * 0.6f / l2;
            if (Spots.Find(occ, math.sqrt(l2 * l2 + w2 * w2) * 0.5f + 1f, Spots.Preference.NearPaths, Hashing.Mix(ctx.Seed, 0xC0u), out var spot, null, 0f, 1f, 1500))
            {
                LeisureKit.Frame(region, out _, out var axis, out _, out _, out _);
                court = new OrientedRect { Center = spot, Axis = axis, HalfLength = l2 * 0.5f, HalfWidth = w2 * 0.5f };
                return LeisureKit.RectFits(region, occ, court, 0.5f);
            }
            return false;
        }

        /// <summary>Paved court with table rows, kiosks along one long side (or a truck ring round it), string lights at the corners.</summary>
        private static int FurnishCourt(LayerContext ctx, Occupancy occ, OrientedRect court, string style, bool truckRing)
        {
            ctx.Surface(court.Ring, SurfaceRole.Paving, AssetMix.One(""), AssetCategories.SurfacePaving);
            int tables = 0;
            int wanted = ctx.I("tables");
            string tableCat = LeisureKit.Category(ctx, "", AssetCategories.Picnic, AssetCategories.CafeSeating, AssetCategories.Bench);
            bool kioskSide = !truckRing && court.HalfWidth > 4.5f;
            float vMax = court.HalfWidth - (kioskSide ? 4.5f : 2f), uMax = court.HalfLength - 2f;
            if (tableCat != null)
                for (float v = -vMax; v <= vMax + 0.01f && tables < wanted; v += 4.2f)
                    for (float u = -uMax; u <= uMax + 0.01f && tables < wanted; u += 4.5f)
                    {
                        var p = court.Local(u, v);
                        EventKit.Place(ctx, p, court.Yaw + ((tables & 1) == 0 ? 0f : math.PI * 0.5f), tableCat, AssetMix.One(""), Hashing.Mix(ctx.Seed, 0x7AB0u + (uint)tables));
                        tables++;
                    }
            string vendor = style == "foodTrucks" || ctx.S("stallKind") == "trucks"
                ? LeisureKit.Category(ctx, "", AssetCategories.EventFoodTruck, AssetCategories.Kiosk)
                : LeisureKit.Category(ctx, "", AssetCategories.Kiosk);
            if (vendor != null && kioskSide)
            {
                int n = math.max(1, (int)(court.HalfLength * 2f / 6f));
                for (int i = 0; i < n; i++)
                {
                    float u = -court.HalfLength + (i + 0.5f) * court.HalfLength * 2f / n;
                    var p = court.Local(u, court.HalfWidth - 2f);
                    EventKit.Place(ctx, p, LeisureKit.Yaw(p, court.Local(u, 0f)), vendor, AssetMix.One(""), Hashing.Mix(ctx.Seed, 0x6100u + (uint)i));
                }
            }
            string lights = ctx.B("festoons") ? EventKit.Optional("", AssetCategories.EventFestoon, AssetCategories.Lamp) : null;
            if (lights != null)
                foreach (var q in court.Ring)
                {
                    var p = q + math.normalizesafe(court.Center - q) * 1f;
                    EventKit.Place(ctx, p, 0f, lights, AssetMix.One(ctx.S("lampAsset")), Hashing.Mix(ctx.Seed, (uint)(p.x * 7f) ^ 0x1F00u));
                }
            return tables;
        }

        // ───────────── landmark, rides ─────────────

        private static void Landmark(LayerContext ctx, Occupancy occ, string style)
        {
            string cat; float r;
            switch (style)
            {
                case "funfair": cat = LeisureKit.Category(ctx, "", AssetCategories.EventRide, AssetCategories.Centerpiece); r = 10f; break;
                case "christmas": cat = LeisureKit.Category(ctx, "", AssetCategories.EventChristmas, AssetCategories.Tree, AssetCategories.Centerpiece); r = 4.5f; break;
                default: cat = LeisureKit.Category(ctx, "", AssetCategories.Centerpiece); r = 5f; break;
            }
            if (cat == null) return;
            if (!Spots.Find(occ, r + 1.5f, Spots.Preference.NearPaths, Hashing.Mix(ctx.Seed, 0x1A4Du), out var p, null, 0f, 1f, 2000))
            { ctx.Issue(IssueSeverity.Info, K + "Issue.NoLandmark"); return; }
            ctx.Surface(LayerRegion.Circle(p, r + 1f, 18), SurfaceRole.Paving, AssetMix.One(""), AssetCategories.SurfacePaving);
            var o = EventKit.Place(ctx, p, Hashing.Unit(Hashing.Mix(ctx.Seed, 0x1A4Eu)) * 6.283f, cat, AssetMix.One(""), Hashing.Mix(ctx.Seed, 0x1A4Fu));
            if (o.Role == ObjectRole.Tree) o.Age = 3;
            occ.Take(p, r + 1f, 0);
            ctx.Plan.Facts["landmark"] = 1;
        }

        private static int Rides(LayerContext ctx, Occupancy occ)
        {
            int wanted = ctx.I("rides");
            if (wanted <= 0) return 0;
            var ids = ctx.L("rideAssets");
            bool chosen = false;
            foreach (var id in ids) if (!string.IsNullOrEmpty(id)) chosen = true;
            string cat = LeisureKit.Category(ctx, chosen ? "x" : "", AssetCategories.EventRide, AssetCategories.Playground);
            if (cat == null) return 0;
            var spots = new List<float2>();
            int n = 0;
            for (int i = 0; i < wanted; i++)
            {
                uint v = Hashing.Mix(ctx.Seed, 0x21D0u + (uint)i);
                float r = (i % 4 == 0) ? 8f : 5f + Hashing.Unit(v) * 1.5f;
                if (!Spots.Find(occ, r + 1.2f, Spots.Preference.NearPaths, v, out var p, spots, r * 2f + 3f, 1f, 1500)) break;
                ctx.Surface(LayerRegion.Circle(p, r + 0.8f, 16), SurfaceRole.Paving, AssetMix.One(""), AssetCategories.SurfacePaving);
                EventKit.Place(ctx, p, Hashing.Unit(v) * 6.283f, cat, AssetMix.Pick(ids, v), v);
                occ.Take(p, r + 0.8f, 0);
                spots.Add(p);
                n++;
            }
            if (n < wanted) ctx.Issue(IssueSeverity.Info, K + "Issue.FewerRides", n, wanted);
            return n;
        }

        // ───────────── stalls ─────────────

        private static string StallCategory(LayerContext ctx, string style)
        {
            string chosen = ctx.S("stallAsset");
            string kind = ctx.S("stallKind");
            if (style == "foodTrucks" && kind == "stalls") kind = "trucks";
            switch (kind)
            {
                case "tents": return LeisureKit.Category(ctx, chosen, AssetCategories.EventTent, AssetCategories.MarketStall, AssetCategories.Shelter, AssetCategories.Kiosk);
                case "trucks": return LeisureKit.Category(ctx, chosen, AssetCategories.EventFoodTruck, AssetCategories.Kiosk);
                default: return LeisureKit.Category(ctx, chosen, AssetCategories.MarketStall, AssetCategories.Kiosk);
            }
        }

        private static int Stalls(LayerContext ctx, LayerRegion region, Occupancy occ, List<Street> streets, OrientedRect court, bool truckRing,
            float2 centre, float2 axis, bool grid, string style)
        {
            int wanted = ctx.I("stalls");
            if (wanted <= 0) return 0;
            string cat = StallCategory(ctx, style);
            if (cat == null) return 0;
            var asset = AssetMix.One(ctx.S("stallAsset"));
            bool trucks = cat == AssetCategories.EventFoodTruck || ctx.S("stallKind") == "trucks";
            float stallR = trucks ? 2.6f : cat == AssetCategories.EventTent ? 2.2f : 1.8f;
            float pitch = math.max(ctx.F("stallPitch"), stallR * 2f + 0.4f);
            string festoon = ctx.B("festoons") ? EventKit.Optional("", AssetCategories.EventFestoon, AssetCategories.Lamp) : null;
            var lamp = AssetMix.One(ctx.S("lampAsset"));
            int stalls = 0, slot = 0;

            // Food-truck ring round the dining court, facing in.
            if (truckRing)
            {
                var ring = new OrientedRect { Center = court.Center, Axis = court.Axis, HalfLength = court.HalfLength + 3.5f + stallR, HalfWidth = court.HalfWidth + 3.5f + stallR }.Ring;
                for (int e = 0; e < 4 && stalls < wanted; e++)
                {
                    float2 a = ring[e], b = ring[(e + 1) % 4];
                    foreach (var p in LeisureKit.Along(a, b, pitch))
                    {
                        if (stalls >= wanted) break;
                        if (EventKit.Put(ctx, occ, p, LeisureKit.Yaw(p, court.Center), cat, asset, stallR, Hashing.Mix(ctx.Seed, 0x5700u + (uint)stalls), 0.8f, 0.3f)) stalls++;
                    }
                }
            }

            // Market grid: back-to-back pairs facing the aisles (christmas market).
            if (grid)
            {
                var pairs = LeisureKit.Pack(region, occ, centre, axis, pitch - 0.6f, stallR * 4f + 0.6f, 0.6f, ctx.F("streetWidth"), (wanted - stalls + 1) / 2, 1.5f);
                for (int i = 0; i < pairs.Count && stalls < wanted; i++)
                {
                    var r = pairs[i];
                    for (int k = -1; k <= 1 && stalls < wanted; k += 2)
                    {
                        var p = r.Local(0f, k * (stallR + 0.3f));
                        EventKit.Place(ctx, p, math.atan2(r.Side.x * k, r.Side.y * k), cat, asset, Hashing.Mix(ctx.Seed, 0x5800u + (uint)stalls));
                        stalls++;
                    }
                    if (festoon != null && i % 3 == 0)
                        EventKit.Put(ctx, occ, r.Local(r.HalfLength + 0.6f, r.HalfWidth + 0.9f), 0f, festoon, lamp, 0.4f, Hashing.Mix(ctx.Seed, 0x1E00u + (uint)i), 0.5f, 0.1f);
                }
            }

            // Rows lining both sides of every street (main avenue first), facing it; a festoon pole every fifth pitch.
            float streetHalf = ctx.F("streetWidth") * 0.5f;
            foreach (var s in streets)
            {
                if (stalls >= wanted) break;
                float len = math.distance(s.A, s.B);
                var d = (s.B - s.A) / math.max(0.01f, len);
                var n = new float2(-d.y, d.x);
                float off = math.max(Composer.DefaultHalfWidth(s.Role) + 0.6f + stallR, streetHalf + stallR);
                for (float u = pitch * 0.5f + 2f; u < len - 2f && stalls < wanted; u += pitch)
                    for (int sg = -1; sg <= 1 && stalls < wanted; sg += 2)
                    {
                        var p = s.A + d * u + n * (sg * off);
                        float yaw = math.atan2(-n.x * sg, -n.y * sg);
                        if (festoon != null && slot++ % 5 == 4)
                        {
                            EventKit.Put(ctx, occ, s.A + d * u + n * (sg * (Composer.DefaultHalfWidth(s.Role) + 1f)), yaw, festoon, lamp, 0.4f, Hashing.Mix(ctx.Seed, 0x1E80u + (uint)slot), 0.5f, 0.1f);
                            continue;
                        }
                        if (EventKit.Put(ctx, occ, p, yaw, cat, asset, stallR, Hashing.Mix(ctx.Seed, 0x5900u + (uint)stalls), 0.8f, 0.3f)) stalls++;
                    }
            }
            if (stalls < wanted) ctx.Issue(IssueSeverity.Info, K + "Issue.FewerStalls", stalls, wanted);
            return stalls;
        }

        // ───────────── service ─────────────

        private static int Toilets(LayerContext ctx, Occupancy occ, StageSite st)
        {
            int wanted = ctx.I("toilets");
            if (wanted <= 0) return 0;
            string cat = LeisureKit.Category(ctx, ctx.S("toiletAsset"), AssetCategories.EventToilet, AssetCategories.Restroom);
            if (cat == null) return 0;
            var asset = AssetMix.One(ctx.S("toiletAsset"));
            bool cabins = cat == AssetCategories.EventToilet || !string.IsNullOrEmpty(ctx.S("toiletAsset"));
            int blocks = cabins ? (wanted + 7) / 8 : math.clamp((wanted + 7) / 8, 1, 3);
            var centres = new List<float2>();
            if (st.HasField) centres.Add(st.Field.Center);   // first block: away from the field middle
            int n = 0;
            for (int b = 0; b < blocks; b++)
            {
                int count = cabins ? math.min(8, wanted - b * 8) : 1;
                float pitch = cabins ? 1.6f : 6f;
                int placed = EventKit.CabinRow(ctx, occ, cat, asset, count, pitch, Hashing.Mix(ctx.Seed, 0x7010u + (uint)b), centres, st.HasField && b == 0 ? 10f : 30f);
                if (placed == 0) break;
                n += placed;
                var last = ctx.Plan.Objects[ctx.Plan.Objects.Count - 1].Position.xz;
                centres.Add(last);
            }
            return n;
        }

        private static void Lights(LayerContext ctx, Occupancy occ, StageSite st)
        {
            float spacing = ctx.F("lightSpacing");
            if (spacing <= 0f) return;
            string cat = LeisureKit.Category(ctx, ctx.S("lightAsset"), AssetCategories.EventLightTower, AssetCategories.FloodLight, AssetCategories.Lamp);
            if (cat == null) return;
            var asset = AssetMix.One(ctx.S("lightAsset"));
            int n = 0;
            if (st.HasField)
                foreach (var (u, v) in new[] { (-1f, -1f), (1f, -1f), (1f, 1f), (-1f, 1f) })
                {
                    var p = st.Field.Local(u * (st.Field.HalfLength + 1.5f), v * (st.Field.HalfWidth + 1.5f));
                    if (EventKit.Put(ctx, occ, p, LeisureKit.Yaw(p, st.Field.Center), cat, asset, 0.8f, Hashing.Mix(ctx.Seed, 0x1700u + (uint)n), 0.5f, 0.1f)) n++;
                }
            float total = 0f;
            foreach (var s in occ.Paths.Segments) total += math.distance(s.A, s.B);
            int wanted = math.clamp((int)(total / spacing), 0, 60);
            foreach (var a in PathAnchors.Pick(PathAnchors.Beside(occ, 0.8f, 6f, 0.4f), wanted, spacing * 0.8f, false))
                if (EventKit.Put(ctx, occ, a.Position, LeisureKit.Yaw(a.Position, a.Toward), cat, asset, 0.8f, Hashing.Mix(ctx.Seed, 0x1780u + (uint)n), 0.5f, 0.1f)) n++;
            ctx.Plan.Facts["lightTowers"] = n;
        }
    }
}
