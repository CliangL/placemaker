// Transit plaza: the paved place where people change between bus, taxi, bike, car and foot.
// Composition, from the street inwards:
//   1. the kerb lane along the road frontage: one asphalt lay-by strip with bus bays, a taxi rank and
//      kiss-and-ride spaces marked by marking decals (bays are paved areas + markings, not roads),
//   2. the platform behind it: waiting shelters at each bus bay, timetable/taxi signs at the bay
//      heads, bollards between the lay-by and the plaza,
//   3. optionally a park & ride car park at one end of the frontage (the Parking layer's planner),
//   4. the forecourt: paving + pedestrian area, bike racks in rows (under shelters at a bike hub),
//      a focal centrepiece, trees in a grid / rows / islands, benches, map boards, kiosks, lamps.
// Subtypes are presets: park & ride, bus interchange, bike hub, taxi rank, station forecourt.

using System.Collections.Generic;
using Placemaker.Core.Geometry;
using Placemaker.Core.Layers.Common;
using Placemaker.Core.Layers.Leisure;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Types.Transport
{
    [AreaGenerator(91, Group = "transport")]
    public sealed class TransitPlazaLayer : LeisureLayer
    {
        protected override string Area => "TransitPlaza";
        public override string Id => "transitPlaza";
        public override string Icon => "Media/Game/Icons/BusShelter.svg";
        public override float MinArea => 600f;
        public override string[] Emits => new[] { RegionTags.Paved, RegionTags.Core };
        protected override bool HostsStreetShops => false;

        public static readonly string[] Styles = { "parkRide", "busPlaza", "bikeHub", "taxiRank", "forecourt" };
        public const string StatBusBays = "Placemaker.TransitPlaza.Stat.BusBays";
        public const string StatTaxiSpaces = "Placemaker.TransitPlaza.Stat.TaxiSpaces";
        public const string StatDropOff = "Placemaker.TransitPlaza.Stat.DropOff";
        public const string StatBikeRacks = "Placemaker.TransitPlaza.Stat.BikeRacks";

        protected override List<FeatureInfo> BuildFeatures() => new List<FeatureInfo>
        {
            new FeatureInfo { Layer = "plazatrees", Target = RegionTags.Paved, LabelKey = "Placemaker.Plaza.Feature.trees" },
            new FeatureInfo { Layer = "kiosk", Target = RegionTags.Paved, LabelKey = "Placemaker.Plaza.Feature.kiosk" },
            CoreFeature("fountain", ("size", "small")),
            Feature("flowerbeds", ("density", 20.0), ("nearPaths", false)),
            Feature("shops", ("count", 2.0), ("where", "edges")),
            Feature("decals", ("theme", "cracks"), ("ground", "paved"), ("density", 0.6)),
            Feature("terrain", ("shape", "flatten")),
        };

        protected override void DefineControls(ControlSet c)
        {
            c.Choice("style", "busPlaza", ControlTier.Essential, "layout", Styles).Tip();
            c.Slider("lotShare", 0, 0, 75, 5, "%", ControlTier.Essential, "layout").Tip();
            c.Slider("busBays", 3, 0, 8, 1, "count", ControlTier.Essential, "bays");
            c.Slider("taxis", 2, 0, 12, 1, "count", ControlTier.Essential, "bays").Tip();
            c.Slider("dropOff", 2, 0, 12, 1, "count", ControlTier.Essential, "bays").Tip();
            c.Slider("bayLength", 15, 12, 20, 1, "m", ControlTier.More, "bays").Tip();
            c.Toggle("bayShelters", true, ControlTier.More, "bays");
            c.Toggle("bollards", true, ControlTier.More, "bays").Tip();

            c.Choice("stallAngle", "90", ControlTier.More, "spaces", "90", "60", "45");
            c.Slider("evShare", 4, 0, 30, 1, "%", ControlTier.More, "spaces");

            c.Slider("bikeRacks", 6, 0, 60, 1, "count", ControlTier.More, "bikes").Tip();
            c.Toggle("bikeShelters", false, ControlTier.More, "bikes").Tip();
            c.Toggle("bikeService", false, ControlTier.More, "bikes").Tip();

            c.Slider("benches", 10, 0, 40, 1, "count", ControlTier.More, "furniture");
            c.Slider("signs", 2, 0, 8, 1, "count", ControlTier.More, "furniture").Tip();
            c.Slider("kiosks", 1, 0, 4, 1, "count", ControlTier.More, "furniture");
            c.Toggle("centrepiece", false, ControlTier.More, "furniture").Tip();

            c.Choice("treeLayout", "grid", ControlTier.More, "planting", "grid", "rows", "islands", "none").Tip();
            c.Slider("treeSpacing", 10, 6, 20, 1, "m", ControlTier.More, "planting").When("treeLayout=grid");
            c.Toggle("planters", false, ControlTier.More, "planting").Tip();
            c.AssetMulti("treeSpecies", AssetCategories.Tree, ControlTier.More, "planting");

            c.Toggle("lamps", true, ControlTier.More, "lighting");
            c.Slider("lampSpacing", 16, 10, 30, 1, "m", ControlTier.More, "lighting").When("lamps=true");

            c.Asset("pavingAsset", AssetCategories.SurfacePaving, ControlTier.More, "assets");
            c.Asset("shelterAsset", AssetCategories.TransportShelter, ControlTier.More, "assets");
            c.Asset("bikeRackAsset", AssetCategories.BikeRack, ControlTier.More, "assets");
            c.Asset("signAsset", AssetCategories.Wayfinding, ControlTier.More, "assets");
            c.Asset("benchAsset", AssetCategories.Bench, ControlTier.More, "assets");
            c.Asset("lampAsset", AssetCategories.Lamp, ControlTier.More, "assets");
            c.Asset("bollardAsset", AssetCategories.Bollard, ControlTier.More, "assets");
            c.Markers("entrances", ControlTier.More, EntranceKind);

            c.RandomRange("lotShare", 0, 40);
            c.RandomRange("bikeRacks", 0, 20);
            c.Fixed("style");
        }

        protected override void DefinePresets()
        {
            Preset("parkRide", ("style", "parkRide"), ("lotShare", 55.0), ("busBays", 2.0), ("taxis", 0.0), ("dropOff", 4.0), ("bikeRacks", 10.0),
                ("treeLayout", "rows"), ("benches", 6.0));
            Preset("busPlaza", ("style", "busPlaza"), ("busBays", 6.0), ("taxis", 2.0), ("dropOff", 0.0), ("benches", 14.0), ("signs", 4.0),
                ("kiosks", 2.0), ("treeLayout", "islands"));
            Preset("bikeHub", ("style", "bikeHub"), ("busBays", 1.0), ("taxis", 0.0), ("dropOff", 2.0), ("bikeRacks", 40.0), ("bikeShelters", true),
                ("bikeService", true), ("treeLayout", "rows"), ("benches", 6.0));
            Preset("taxiRank", ("style", "taxiRank"), ("busBays", 0.0), ("taxis", 10.0), ("dropOff", 6.0), ("signs", 3.0), ("benches", 8.0),
                ("treeLayout", "grid"), ("treeSpacing", 12.0));
            Preset("forecourt", ("style", "forecourt"), ("busBays", 1.0), ("taxis", 4.0), ("dropOff", 4.0), ("centrepiece", true),
                ("treeLayout", "rows"), ("benches", 16.0), ("signs", 3.0), ("bikeRacks", 8.0), ("kiosks", 2.0));
        }

        private enum Bay { Bus = 0, Taxi = 1, DropOff = 2 }

        protected override void PlanCore(LayerContext ctx)
        {
            var full = ctx.LayerRegion;
            string style = ctx.S("style");
            var gates = Gates(ctx);

            // Frontage: the longest road-facing outline piece, else the first entrance's edge, else the longest edge.
            Frontage(ctx, full, gates, out var fa, out var fb);
            TransportKit.EdgeFrame(full, fa, fb, out var t, out var n, out float uMin, out float uMax, out float vMax);
            float fLen = math.distance(fa, fb);

            // ── Park & ride car park at one end of the frontage ──
            SubLot lot = null;
            float share = ctx.F("lotShare") / 100f;
            float ru0 = uMin, ru1 = uMax;
            if (share > 0f)
            {
                float gateU = gates.Count > 0 ? math.dot(gates[0].Position - fa, t) : uMin;
                bool atEnd = gateU < (uMin + uMax) * 0.5f;
                float lotLen = math.max(30f, (uMax - uMin) * share);
                float u0 = atEnd ? uMax - lotLen : uMin - 3f, u1 = atEnd ? uMax + 3f : uMin + lotLen;
                var cut = TransportKit.FrameRect(fa, t, n, u0, u1, -3f, vMax + 3f);
                float e0 = math.max(u0, 0f), e1 = math.min(u1, fLen);
                float uE = e1 - e0 > 10f ? (e0 + e1) * 0.5f : (math.max(u0, uMin) + math.min(u1, uMax)) * 0.5f;
                var entrance = full.ClosestOnBoundary(fa + t * uE - n, out _, out _);
                lot = TransportKit.PlanLot(ctx, cut, entrance, TransportKit.Bag(("stallAngle", ctx.S("stallAngle")), ("evShare", (double)ctx.F("evShare")),
                    ("greenery", "edges"), ("lanterns", ctx.B("lamps"))), 0xB1u);
                if (lot != null)
                {
                    var rest = TransportKit.Subtract(full.Ring, cut, MinArea * 0.5f);
                    var restRegion = rest == null ? null : LayerRegion.From(rest);
                    if (restRegion != null)
                    {
                        ctx.LayerRegion = restRegion;
                        if (atEnd) ru1 = u0; else ru0 = u1;
                    }
                }
            }
            var region = ctx.LayerRegion;
            var occ = new Occupancy(region, ctx.Input.Reserved, ctx.Site);
            if (lot != null) TransportKit.Occupy(occ, ctx.Plan);
            ctx.Check();

            // ── Kerb lane with bays ──
            int bus = ctx.I("busBays"), taxis = ctx.I("taxis"), drop = ctx.I("dropOff");
            float kerbDepth = bus > 0 ? 3.6f : 2.8f;
            float k0 = math.max(0f, ru0) + 1.5f, k1 = math.min(fLen, ru1) - 1.5f;
            float2[] plazaRing = region.Ring;
            int placedBus = 0, placedTaxi = 0, placedDrop = 0;
            if (bus + taxis + drop > 0 && k1 - k0 > 8f)
            {
                var band = TransportKit.FrameRect(fa, t, n, k0, k1, -3f, kerbDepth);
                var kerb = TransportKit.Intersect(region.Ring, band, 20f);
                var plaza = TransportKit.Subtract(region.Ring, band, MinArea * 0.3f);
                if (kerb != null && plaza != null && ctx.Surface(kerb, SurfaceRole.Asphalt, AssetMix.One(""), AssetCategories.SurfaceAsphalt))
                {
                    plazaRing = plaza;
                    Bays(ctx, occ, fa, t, n, k0, k1, kerbDepth, style, ref placedBus, ref placedTaxi, ref placedDrop);
                    occ.TakeCorridor(fa + t * k0 + n * (kerbDepth * 0.5f - 0.5f), fa + t * k1 + n * (kerbDepth * 0.5f - 0.5f), kerbDepth * 0.5f + 0.5f, 2);
                }
            }
            ctx.Check();

            // ── Forecourt paving ──
            var pavingAsset = AssetMix.One(ctx.S("pavingAsset"), "Pavement Surface 01");
            ctx.NavigationArea(plazaRing, AreaRole.PedestrianArea, new AssetRef("", "Pedestrian Area", "Walking Area"));
            ctx.Surface(plazaRing, SurfaceRole.Plaza, pavingAsset, AssetCategories.SurfacePaving);
            var plazaRegion = LayerRegion.From(plazaRing) ?? region;

            // Focal point on the axis from the middle of the kerb inwards.
            float uc = (k0 + k1) * 0.5f;
            var focal = fa + t * uc + n * (kerbDepth + math.max(8f, (vMax - kerbDepth) * 0.45f));
            if (!plazaRegion.ContainsDisc(focal, 6f) || occ.Clearance(focal, 6f) < 5f) focal = plazaRegion.DeepestPoint(out _, (q, cap) => occ.Clearance(q, cap), 14);
            bool centre = ctx.B("centrepiece") || style == "forecourt";
            if (centre)
            {
                string cat = LeisureKit.Category(ctx, "", AssetCategories.CenterpieceStatue, AssetCategories.Centerpiece);
                if (cat != null) LeisureKit.Put(ctx, occ, focal, LeisureKit.Yaw(focal, focal - n), ObjectRole.Centerpiece, AssetMix.One(""), cat, 2.5f, Hashing.Mix(ctx.Seed, 0xCE1u), 1f, 0.3f, 0);
            }
            occ.Take(focal, 5f, 2);   // the focal space stays open (the fountain feature lands here)
            ctx.Check();

            // ── Bikes ──
            int racks = BikeRows(ctx, occ, region, fa, t, n, uc, kerbDepth);
            ctx.Check();

            // ── Trees ──
            var trees = new List<float2>();
            Trees(ctx, occ, plazaRegion, region, fa, t, n, trees);
            ctx.Check();

            // ── Benches, signs, kiosks, lamps, bins ──
            int benches = Benches(ctx, occ, fa, t, n, k0, k1, kerbDepth, trees, focal);
            Signs(ctx, occ, gates, focal, t);
            Kiosks(ctx, occ);
            if (ctx.B("lamps")) Lamps(ctx, occ, region, fa, t, n);

            ctx.AddRegion(plazaRing, "paved", RegionTags.Paved);
            ctx.AddRegion(LayerRegion.Circle(focal, 5f, 20), "core", RegionTags.Core, RegionTags.Paved);
            ctx.Stat(Keys.StatArea, System.Math.Round(full.Area), Keys.UnitM2);
            ctx.Stat(Keys.StatTrees, Count(ctx.Plan, ObjectRole.Tree), Keys.UnitCount);
            ctx.Stat(Keys.StatBenches, benches, Keys.UnitCount);
            ctx.Stat(StatBusBays, placedBus, Keys.UnitCount);
            ctx.Stat(StatTaxiSpaces, placedTaxi, Keys.UnitCount);
            ctx.Stat(StatDropOff, placedDrop, Keys.UnitCount);
            ctx.Stat(StatBikeRacks, racks, Keys.UnitCount);
            ctx.Plan.Facts["busBays"] = placedBus;
            ctx.Plan.Facts["taxiSpaces"] = placedTaxi;
            ctx.Plan.Facts["dropOff"] = placedDrop;
            ctx.Plan.Facts["bikeRacks"] = racks;
            ctx.Plan.Facts["lot"] = lot != null ? 1 : 0;
        }

        private static void Frontage(LayerContext ctx, LayerRegion full, List<Gate> gates, out float2 a, out float2 b)
        {
            var roads = StreetFrontage.Roads(ctx.Input);
            var edges = StreetFrontage.Edges(full.Ring, roads);
            float best = -1f; a = b = full.Ring[0];
            foreach (var (p, q) in edges)
            {
                float len = math.distance(p, q);
                if (len > best) { best = len; a = p; b = q; }
            }
            if (best >= 12f) return;
            if (gates.Count > 0)
            {
                int s = gates[0].Segment;
                a = full.Ring[s]; b = full.Ring[(s + 1) % full.Ring.Length];
                if (math.distance(a, b) >= 12f) return;
            }
            best = -1f;
            for (int i = 0; i < full.Ring.Length; i++)
            {
                var p = full.Ring[i]; var q = full.Ring[(i + 1) % full.Ring.Length];
                float len = math.distance(p, q);
                if (len > best) { best = len; a = p; b = q; }
            }
        }

        // ───────────── kerb lane ─────────────

        private static void Bays(LayerContext ctx, Occupancy occ, float2 fa, float2 t, float2 n, float k0, float k1, float depth, string style,
            ref int placedBus, ref int placedTaxi, ref int placedDrop)
        {
            var region = ctx.LayerRegion;
            var seq = new List<(Bay kind, float len)>();
            float busLen = ctx.F("bayLength");
            void AddRun(Bay kind, int count, float len) { for (int i = 0; i < count; i++) seq.Add((kind, len)); }
            if (style == "taxiRank") { AddRun(Bay.Taxi, ctx.I("taxis"), 6f); AddRun(Bay.DropOff, ctx.I("dropOff"), 6.5f); AddRun(Bay.Bus, ctx.I("busBays"), busLen); }
            else { AddRun(Bay.Bus, ctx.I("busBays"), busLen); AddRun(Bay.Taxi, ctx.I("taxis"), 6f); AddRun(Bay.DropOff, ctx.I("dropOff"), 6.5f); }
            float total = 0f;
            foreach (var s in seq) total += s.len + (s.kind == Bay.Bus ? 3f : 0.5f);
            float u = k0 + math.max(0f, (k1 - k0 - total) * 0.5f);
            var yaw = LeisureKit.Yaw(float2.zero, t);
            var facing = LeisureKit.Yaw(float2.zero, -n);   // platform furniture faces the kerb
            string marks = AssetAvailability.Has(AssetCategories.DecalMarkings) ? AssetCategories.DecalMarkings : null;
            if (marks == null) AssetAvailability.Require(ctx.Plan, AssetCategories.DecalMarkings, null, Keys.Category(AssetCategories.DecalMarkings));
            string shelter = ctx.B("bayShelters") ? LeisureKit.Category(ctx, ctx.S("shelterAsset"), AssetCategories.TransportShelter, AssetCategories.Shelter) : null;
            string sign = LeisureKit.Category(ctx, ctx.S("signAsset"), AssetCategories.Wayfinding);
            var bench = AssetMix.One(ctx.S("benchAsset"));
            uint seed = Hashing.Mix(ctx.Seed, 0xBA70u);
            var prev = (Bay)(-1);
            int index = 0;
            foreach (var (kind, len) in seq)
            {
                float gap = kind == Bay.Bus ? 3f : 0.5f;
                if (kind != prev && prev != (Bay)(-1)) u += 2f;
                if (u + len > k1) break;
                float mid = u + len * 0.5f;
                var c = fa + t * mid + n * (depth * 0.5f);
                var e0 = fa + t * u + n * (depth * 0.5f); var e1 = fa + t * (u + len) + n * (depth * 0.5f);
                if (!region.Contains(e0) || !region.Contains(e1)) { u += len + gap; continue; }
                uint v = Hashing.Mix(seed, (uint)index++);
                if (marks != null)
                {
                    ctx.Object(c, yaw, ObjectRole.Decal, AssetMix.One(""), v, -1, marks);
                    if (kind == Bay.Bus)
                    {
                        ctx.Object(e0 + t * 1f, yaw, ObjectRole.Decal, AssetMix.One(""), Hashing.Mix(v, 1u), -1, marks);
                        ctx.Object(e1 - t * 1f, yaw, ObjectRole.Decal, AssetMix.One(""), Hashing.Mix(v, 2u), -1, marks);
                    }
                }
                var platform = fa + t * mid + n * (depth + 2.2f);
                switch (kind)
                {
                    case Bay.Bus:
                        placedBus++;
                        if (shelter != null)
                            LeisureKit.Put(ctx, occ, platform, facing, ObjectRole.Furniture, AssetMix.One(ctx.S("shelterAsset")), shelter, 2.0f, Hashing.Mix(v, 3u), 0.3f, 0.2f, 0);
                        else LeisureKit.Put(ctx, occ, platform, facing, ObjectRole.Bench, bench, null, 1.0f, Hashing.Mix(v, 3u));
                        if (sign != null) LeisureKit.Put(ctx, occ, fa + t * (u + len - 1.2f) + n * (depth + 0.9f), facing, ObjectRole.Furniture, AssetMix.One(ctx.S("signAsset")), sign, 0.4f, Hashing.Mix(v, 4u), 0.2f, 0.2f);
                        break;
                    case Bay.Taxi:
                        placedTaxi++;
                        if (prev != Bay.Taxi)
                        {
                            if (sign != null) LeisureKit.Put(ctx, occ, fa + t * (u + 0.8f) + n * (depth + 0.9f), facing, ObjectRole.Furniture, AssetMix.One(ctx.S("signAsset")), sign, 0.4f, Hashing.Mix(v, 5u), 0.2f, 0.2f);
                            if (shelter != null && ctx.I("taxis") >= 4)
                                LeisureKit.Put(ctx, occ, fa + t * (u + 3f) + n * (depth + 2.2f), facing, ObjectRole.Furniture, AssetMix.One(ctx.S("shelterAsset")), shelter, 2.0f, Hashing.Mix(v, 6u), 0.3f, 0.2f, 0);
                        }
                        break;
                    default:
                        placedDrop++;
                        if (prev != Bay.DropOff) LeisureKit.Put(ctx, occ, platform, facing, ObjectRole.Bench, bench, null, 1.0f, Hashing.Mix(v, 7u));
                        break;
                }
                prev = kind;
                u += len + gap;
            }

            // Bollards along the platform edge keep cars on the lay-by.
            if (ctx.B("bollards"))
            {
                string bol = LeisureKit.Category(ctx, ctx.S("bollardAsset"), AssetCategories.Bollard);
                if (bol != null)
                {
                    int i = 0;
                    for (float b = k0 + 0.5f; b <= k1 - 0.5f; b += 2.5f)
                        LeisureKit.Put(ctx, occ, fa + t * b + n * (depth + 0.45f), 0f, ObjectRole.Furniture, AssetMix.One(ctx.S("bollardAsset")), bol, 0.25f, Hashing.Mix(seed, 0x800u + (uint)i++), 0.1f, 0.15f);
                }
            }
        }

        // ───────────── bikes ─────────────

        private static int BikeRows(LayerContext ctx, Occupancy occ, LayerRegion region, float2 fa, float2 t, float2 n, float uc, float kerbDepth)
        {
            int want = ctx.I("bikeRacks");
            if (want <= 0) return 0;
            string cat = LeisureKit.Category(ctx, ctx.S("bikeRackAsset"), AssetCategories.BikeRack);
            if (cat == null) return 0;
            const int perRow = 5;
            int rows = (want + perRow - 1) / perRow;
            // Rows start at the kerb side so bikes are parked where people arrive.
            var centre = fa + t * uc + n * (kerbDepth + 6f);
            var packed = LeisureKit.Pack(region, occ, centre, t, 7f, 2.6f, 2.5f, 3f, rows, 0.8f, false, true);
            string shelter = ctx.B("bikeShelters") ? LeisureKit.Category(ctx, ctx.S("shelterAsset"), AssetCategories.Shelter) : null;
            var asset = AssetMix.One(ctx.S("bikeRackAsset"));
            int placed = 0, row = 0;
            foreach (var r in packed)
            {
                uint v = Hashing.Mix(ctx.Seed, 0xB1C0u + (uint)row++);
                if (shelter != null) ctx.Object(r.Center, r.Yaw, ObjectRole.Furniture, AssetMix.One(ctx.S("shelterAsset")), v, -1, shelter);
                for (int k = 0; k < perRow && placed < want; k++)
                {
                    ctx.Object(r.Local(-2.6f + k * 1.3f, 0f), r.Yaw + 1.5708f, ObjectRole.Furniture, asset, Hashing.Mix(v, (uint)k), -1, cat);
                    placed++;
                }
            }
            if (ctx.B("bikeService") && packed.Count > 0)
            {
                string svc = LeisureKit.Category(ctx, "", AssetCategories.Kiosk);
                var r0 = packed[0];
                if (svc != null)
                    foreach (float side in new[] { 1f, -1f })
                        if (LeisureKit.Put(ctx, occ, r0.Local(side * (r0.HalfLength + 2.6f), 0f), r0.Yaw, ObjectRole.Furniture, AssetMix.One(""), svc, 1.8f, Hashing.Mix(ctx.Seed, 0xB5EFu), 0.3f, 0.3f, 0)) break;
            }
            return placed;
        }

        // ───────────── trees ─────────────

        private static void Trees(LayerContext ctx, Occupancy occ, LayerRegion plaza, LayerRegion region, float2 fa, float2 t, float2 n, List<float2> placed)
        {
            string layout = ctx.S("treeLayout");
            if (layout == "none") return;
            bool planters = ctx.B("planters");
            var ids = ctx.L("treeSpecies");
            if (!planters && !AssetAvailability.Has(AssetCategories.Tree) && ids.Length == 0)
            {
                AssetAvailability.Require(ctx.Plan, AssetCategories.Tree, null, Keys.Category(AssetCategories.Tree));
                return;
            }
            var role = planters ? ObjectRole.Planter : ObjectRole.Tree;
            float r = planters ? 1.2f : 2.2f;
            void Try(float2 p, uint salt)
            {
                if (!plaza.Contains(p)) return;
                uint v = Hashing.Mix(ctx.Seed, salt);
                if (LeisureKit.Put(ctx, occ, p, Hashing.Unit(v) * 6.283f, role, planters ? AssetMix.One("") : AssetMix.Pick(ids, v), null, r, v, 1.2f, 0.4f, 3, planters ? -1 : 2))
                    placed.Add(p);
            }
            if (layout == "islands")
            {
                int count = math.clamp((int)(plaza.Area / 700f), 1, 40);
                var avoid = new List<float2>();
                for (int i = 0; i < count; i++)
                {
                    if (!Spots.Find(occ, 5f, Spots.Preference.Open, Hashing.Mix(ctx.Seed, 0x15A0u + (uint)i), out var c, avoid, 14f, 1f, budget: 900)) break;
                    avoid.Add(c);
                    float ph = Hashing.Unit(Hashing.Mix(ctx.Seed, 0x15B0u + (uint)i)) * 6.283f;
                    for (int k = 0; k < 3; k++)
                        Try(c + new float2(math.cos(ph + k * 2.094f), math.sin(ph + k * 2.094f)) * 2.8f, 0x15C0u + (uint)(i * 4 + k));
                    // A bench ring around each island.
                    for (int k = 0; k < 2; k++)
                    {
                        float a = ph + 1.05f + k * 3.14f;
                        var p = c + new float2(math.cos(a), math.sin(a)) * 5.6f;
                        LeisureKit.Put(ctx, occ, p, LeisureKit.Yaw(c, p), ObjectRole.Bench, AssetMix.One(ctx.S("benchAsset")), null, 1.0f, Hashing.Mix(ctx.Seed, 0x15D0u + (uint)(i * 2 + k)));
                    }
                }
                return;
            }
            float along = layout == "rows" ? 8f : ctx.F("treeSpacing");
            float across = layout == "rows" ? 18f : ctx.F("treeSpacing");
            // Lattice in the frontage frame; rows (allées) run away from the street.
            float uMin = float.MaxValue, uMax = float.MinValue, vMax = 0f;
            foreach (var p in plaza.Ring)
            {
                float u = math.dot(p - fa, t), v = math.dot(p - fa, n);
                uMin = math.min(uMin, u); uMax = math.max(uMax, u); vMax = math.max(vMax, v);
            }
            float uc = (uMin + uMax) * 0.5f;
            int i0 = (int)math.floor((uMin - uc) / across) - 1, i1 = (int)math.ceil((uMax - uc) / across) + 1;
            int count2 = 0;
            for (int iu = i0; iu <= i1; iu++)
                for (float v = along * 0.5f + 4f; v < vMax; v += along)
                {
                    if (count2++ > 4000) return;
                    float u = uc + (iu + 0.5f) * across;
                    Try(fa + t * u + n * v, 0x7700u + (uint)count2);
                }
        }

        // ───────────── furniture ─────────────

        private static int Benches(LayerContext ctx, Occupancy occ, float2 fa, float2 t, float2 n, float k0, float k1, float depth, List<float2> trees, float2 focal)
        {
            int want = ctx.I("benches");
            var bench = AssetMix.One(ctx.S("benchAsset"));
            int n0 = Count(ctx.Plan, ObjectRole.Bench), placed = 0;
            uint seed = Hashing.Mix(ctx.Seed, 0xBE70u);
            var bin = AssetMix.One("");
            // Facing the focal point, round it.
            for (int i = 0; i < 6 && placed < want; i++)
            {
                float a = i * 1.047f;
                var p = focal + new float2(math.cos(a), math.sin(a)) * 7f;
                if (LeisureKit.Put(ctx, occ, p, LeisureKit.Yaw(p, focal), ObjectRole.Bench, bench, null, 1.0f, Hashing.Mix(seed, (uint)i))) placed++;
            }
            // Beside the trees, back to the trunk.
            foreach (var tr in trees)
            {
                if (placed >= want) break;
                var p = tr + t * 2.9f;
                if (LeisureKit.Put(ctx, occ, p, LeisureKit.Yaw(tr, p), ObjectRole.Bench, bench, null, 1.0f, Hashing.Mix(seed, 100u + (uint)placed)))
                {
                    placed++;
                    if ((placed & 3) == 0) LeisureKit.Put(ctx, occ, tr - t * 2.6f, 0f, ObjectRole.Bin, bin, null, 0.4f, Hashing.Mix(seed, 200u + (uint)placed), 0.2f, 0.1f);
                }
            }
            // Waiting benches along the platform.
            for (float u = k0 + 2f; u < k1 - 2f && placed < want; u += 5f)
                if (LeisureKit.Put(ctx, occ, fa + t * u + n * (depth + 5.2f), LeisureKit.Yaw(float2.zero, -n), ObjectRole.Bench, bench, null, 1.0f, Hashing.Mix(seed, 300u + (uint)placed))) placed++;
            return n0 + placed;
        }

        private static void Signs(LayerContext ctx, Occupancy occ, List<Gate> gates, float2 focal, float2 t)
        {
            int want = ctx.I("signs");
            if (want <= 0) return;
            string cat = LeisureKit.Category(ctx, ctx.S("signAsset"), AssetCategories.Wayfinding);
            if (cat == null) return;
            var asset = AssetMix.One(ctx.S("signAsset"));
            int placed = 0;
            var spots = new List<float2> { focal + t * 8f, focal - t * 8f };
            foreach (var g in gates)
            {
                var inward = math.normalizesafe(ctx.LayerRegion.Centroid - g.Position);
                spots.Insert(0, g.Position + inward * 3f);
            }
            for (int i = 0; i < spots.Count && placed < want; i++)
                if (LeisureKit.Put(ctx, occ, spots[i], LeisureKit.Yaw(spots[i], focal), ObjectRole.Furniture, asset, cat, 0.6f, Hashing.Mix(ctx.Seed, 0x5160u + (uint)i), 0.5f, 0.3f))
                    placed++;
        }

        private static void Kiosks(LayerContext ctx, Occupancy occ)
        {
            int want = ctx.I("kiosks");
            if (want <= 0) return;
            string cat = LeisureKit.Category(ctx, "", AssetCategories.Kiosk);
            if (cat == null) return;
            var avoid = new List<float2>();
            for (int i = 0; i < want; i++)
            {
                if (!Spots.Find(occ, 2.4f, Spots.Preference.NearBoundary, Hashing.Mix(ctx.Seed, 0x4100u + (uint)i), out var p, avoid, 20f, 1f, budget: 900)) break;
                avoid.Add(p);
                LeisureKit.Put(ctx, occ, p, LeisureKit.Yaw(p, ctx.LayerRegion.Centroid), ObjectRole.Furniture, AssetMix.One(""), cat, 2.2f, Hashing.Mix(ctx.Seed, 0x4200u + (uint)i), 0.5f, 0.3f, 0);
            }
        }

        private static void Lamps(LayerContext ctx, Occupancy occ, LayerRegion region, float2 fa, float2 t, float2 n)
        {
            float s = ctx.F("lampSpacing");
            var lamp = AssetMix.One(ctx.S("lampAsset"));
            float uMin = float.MaxValue, uMax = float.MinValue, vMax = 0f;
            foreach (var p in region.Ring)
            {
                float u = math.dot(p - fa, t), v = math.dot(p - fa, n);
                uMin = math.min(uMin, u); uMax = math.max(uMax, u); vMax = math.max(vMax, v);
            }
            int i = 0;
            for (float v = 2f; v < vMax; v += s)
                for (float u = uMin + s * 0.5f; u < uMax; u += s)
                {
                    if (i++ > 3000) return;
                    var p = fa + t * u + n * v;
                    // Nudge off taken spots: try the point and two neighbours.
                    foreach (var q in new[] { p, p + t * 1.5f, p - t * 1.5f })
                        if (LeisureKit.Put(ctx, occ, q, 0f, ObjectRole.Lamp, lamp, null, 0.4f, Hashing.Mix(ctx.Seed, 0x1A70u + (uint)i), 0.5f, 0.3f)) break;
                }
        }
    }
}
