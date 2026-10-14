// Beach: a shore laid out in bands from the water inland.
//
//   waterline  wet sand (boats pulled up, rocks, tide pools on a rocky shore)
//   sand       lifeguard towers, umbrella + lounger sets in rows or loose groups, volleyball courts,
//              fire pits; boardwalk access paths cross it with a shower at their end
//   back path  parallel to the water: benches facing the sea, bins, lamps; changing huts, kiosks
//              on the landward side
//   backdrop   dunes (raised, marram grass, sand fences, pines behind), a lawn, woods, or nothing
//
// The water side comes from WaterDepthAt (or the "Water side" choice, see Shoreline). The ground is
// carved into a gentle slope down to the waterline (only lowered, never raised) and dunes are raised
// behind the sand, all blended back to the original ground at the outline away from the water.
// Styles change the composition, not only densities: rocky (pebble paint, rock clusters along the
// waterline, tide-pool hollows), river (narrow, low bank, lawn and boats), cove (rocky headlands at
// both ends, woods, fire pits), volleyball (a row of courts).

using System.Collections.Generic;
using Placemaker.Core.Layers.Common;
using Placemaker.Core.Layers.Leisure;
using Placemaker.Core.Layers.Park;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Types.Beach
{
    [AreaGenerator(70, Group = "water")]
    public sealed class BeachLayer : LeisureLayer
    {
        public static readonly string[] Styles = { "sandy", "rocky", "river", "cove", "volleyball" };
        private static readonly List<Gate> NoGates = new List<Gate>();

        protected override string Area => "Beach";
        public override string Id => "beach";
        public override string Icon => "Media/Game/Icons/CityBeach.svg";
        public override float MinArea => 1500f;

        protected override List<FeatureInfo> BuildFeatures() => new List<FeatureInfo>
        {
            // Area-wide features run on the dry land only ("core" = the largest free piece of shore.Land):
            // on the whole outline they planted picnic shelters and trees in the water (art pass a3).
            Feature(BeachBarAddOn.LayerId), Feature("restrooms"), Feature("shops", ("count", 2.0), ("where", "paths")), StreetShopsFeature(Area),
            CoreFeature("playground", ("size", "small")), CoreFeature("picnic"), CoreFeature("rocks", ("size", "medium")),
            CoreFeature("bigtrees"), CoreFeature("decals", ("theme", "dirt")),
        };

        /// <summary>Restrooms only: picnic shelters and dirt decals made the sand read as a lawn (art pass a3).</summary>
        protected override string[] DefaultFeatures => new[] { "restrooms" };

        protected override void DefineControls(ControlSet c)
        {
            c.Choice("style", "sandy", ControlTier.Essential, "beach", Styles).Tip();
            c.Choice("waterSide", "auto", ControlTier.Essential, "beach", Shoreline.Sides).Tip();
            c.Slider("beachDepth", 60, 12, 80, 2, "m", ControlTier.Essential, "beach").Layout(8).Tip();
            c.Slider("umbrellas", 35, 0, 120, 5, "perHa", ControlTier.Essential, "beach").Tip();
            c.Choice("layout", "scattered", ControlTier.Essential, "beach", "scattered", "rows").Tip();

            c.Toggle("carve", true, ControlTier.More, "shore").Tip();
            c.Slider("slope", 5, 2, 15, 1, "%", ControlTier.More, "shore").When("carve=true").Tip();
            c.Slider("wetBand", 6, 2, 15, 1, "m", ControlTier.More, "shore").Tip();
            c.Toggle("sandSurface", true, ControlTier.More, "shore").Tip();
            c.Asset("sandAsset", AssetCategories.SurfaceSand, ControlTier.More, "shore").When("sandSurface=true");
            c.Toggle("paint", true, ControlTier.More, "shore").Tip();
            c.Slider("paintLayer", 2, 1, 4, 1, "count", ControlTier.More, "shore").When("paint=true").Tip();

            c.Choice("backdrop", "dunes", ControlTier.More, "backdrop", "dunes", "lawn", "woods", "none").Tip();
            c.Slider("duneHeight", 2, 0.5, 5, 0.5, "m", ControlTier.More, "backdrop").When("backdrop=dunes");
            c.Slider("duneDepth", 20, 8, 50, 2, "m", ControlTier.More, "backdrop").Layout(6).Tip();
            c.Toggle("duneFence", true, ControlTier.More, "backdrop").When("backdrop=dunes").Tip();
            c.Slider("grassDensity", 60, 0, 100, 5, "%", ControlTier.More, "backdrop").Tip();
            c.Slider("treeDensity", 15, 0, 100, 5, "perHa", ControlTier.More, "backdrop");

            c.Toggle("backPath", true, ControlTier.More, "backPath").Tip();
            c.Choice("pathWidth", "narrow", ControlTier.More, "backPath", "narrow", "wide");
            c.Slider("accessSpacing", 60, 20, 200, 10, "m", ControlTier.More, "backPath").Layout(10).Tip();
            c.Slider("benchSpacing", 25, 0, 80, 5, "m", ControlTier.More, "backPath").Layout(5).Tip();
            c.Toggle("lamps", true, ControlTier.More, "backPath");

            c.Toggle("showers", true, ControlTier.More, "amenities").Tip();
            c.Choice("huts", "few", ControlTier.More, "amenities", "none", "few", "row").Tip();
            c.Slider("lifeguards", 0, 0, 8, 1, "count", ControlTier.More, "amenities");
            c.Slider("kiosks", 1, 0, 6, 1, "count", ControlTier.More, "amenities");
            c.Slider("courts", 0, 0, 8, 1, "count", ControlTier.More, "amenities").Tip();
            c.Toggle("boats", false, ControlTier.More, "amenities").Tip();
            c.Toggle("fires", false, ControlTier.More, "amenities").Tip();

            c.Slider("rockClusters", 0, 0, 20, 1, "count", ControlTier.More, "rocks").Tip();
            c.Slider("tidePools", 0, 0, 12, 1, "count", ControlTier.More, "rocks").Tip();

            c.Asset("umbrellaAsset", AssetCategories.BeachUmbrella, ControlTier.More, "assets");
            c.Asset("loungerAsset", AssetCategories.BeachLounger, ControlTier.More, "assets");
            c.Asset("hutAsset", AssetCategories.BeachHut, ControlTier.More, "assets");
            c.Asset("pathAsset", AssetCategories.Pathway, ControlTier.More, "assets");
            c.AssetMulti("treeSpecies", AssetCategories.Tree, ControlTier.More, "assets");
            c.AssetMulti("grassSpecies", AssetCategories.Bush, ControlTier.More, "assets");
            EntranceMarkers(c);
            DefineStreetShopControls(c);

            c.Fixed("waterSide");
            c.Fixed("carve");
            c.Fixed("paintLayer");
            c.RandomRange("beachDepth", 36, 70);
            c.RandomRange("umbrellas", 10, 60);
            c.RandomRange("courts", 0, 2);
            c.RandomRange("rockClusters", 0, 4);
            c.RandomRange("tidePools", 0, 2);
            c.Chance("boats", 0.3f);
            c.Chance("fires", 0.3f);
        }

        protected override void DefinePresets()
        {
            // Art pass a3: the sand is the subject, so every style gets a deep beach (beachDepth is a layout distance:
            // 60 → 30 m at the default layout size). No lifeguards/huts: no vanilla asset exists for either.
            Preset("sandy", ("style", "sandy"), ("beachDepth", 60.0), ("umbrellas", 45.0), ("layout", "scattered"), ("backdrop", "dunes"), ("huts", "none"),
                ("lifeguards", 0.0), ("duneDepth", 24.0), ("grassDensity", 70.0), ("treeDensity", 40.0), ("kiosks", 1.0));
            Preset("resort", ("style", "sandy"), ("beachDepth", 56.0), ("umbrellas", 70.0), ("layout", "rows"), ("backdrop", "lawn"), ("huts", "none"), ("pathWidth", "wide"),
                ("benchSpacing", 15.0), ("kiosks", 2.0), ("lifeguards", 0.0), ("slope", 3.0), ("treeDensity", 30.0));
            Preset("rocky", ("style", "rocky"), ("umbrellas", 5.0), ("sandSurface", false), ("paintLayer", 3.0), ("backdrop", "none"), ("huts", "none"),
                ("lifeguards", 0.0), ("kiosks", 0.0), ("rockClusters", 8.0), ("tidePools", 5.0), ("beachDepth", 36.0), ("slope", 8.0), ("treeDensity", 30.0),
                ("benchSpacing", 30.0));
            Preset("river", ("style", "river"), ("beachDepth", 30.0), ("wetBand", 3.0), ("slope", 3.0), ("umbrellas", 10.0), ("backdrop", "lawn"),
                ("treeDensity", 35.0), ("boats", true), ("lifeguards", 0.0), ("huts", "none"), ("accessSpacing", 40.0));
            Preset("cove", ("style", "cove"), ("beachDepth", 44.0), ("umbrellas", 6.0), ("backdrop", "woods"), ("treeDensity", 75.0), ("fires", true),
                ("lifeguards", 0.0), ("huts", "none"), ("kiosks", 0.0), ("lamps", false), ("rockClusters", 3.0), ("boats", true));
            Preset("volleyball", ("style", "volleyball"), ("beachDepth", 70.0), ("courts", 4.0), ("umbrellas", 25.0), ("accessSpacing", 120.0), ("lifeguards", 0.0), ("kiosks", 2.0),
                ("huts", "none"), ("pathWidth", "wide"), ("backdrop", "lawn"), ("treeDensity", 30.0));
        }

        protected override int WantedEntrances(LayerRegion region) => math.min(4, Entrances.WantedByArea(region.Area));

        // ───────────── planning ─────────────

        private sealed class Work
        {
            public LayerContext Ctx;
            public Shoreline Shore;
            public Occupancy Occ;
            public string Style;
            public float Wet, D, Hw;
            public bool HasPath;
            public List<float2[]> Lines = new List<float2[]>();
            public List<(float2 top, float2 bottom)> Access = new List<(float2, float2)>();
            public TerrainResult Terrain;
            public int Umbrellas, Loungers, Showers, Huts, Guards, Courts, Rocks, Pools, Boats, Fires, Kiosks;
        }

        protected override void PlanCore(LayerContext ctx)
        {
            var shore = Shoreline.Analyze(ctx, ctx.LayerRegion, ctx.S("waterSide"));
            if (shore == null) { ctx.Issue(IssueSeverity.Warning, Keys.NoSpace, NameKey); return; }
            ctx.LayerRegion = shore.Land;
            var b = new Work { Ctx = ctx, Shore = shore, Occ = new Occupancy(shore.Land, ctx.Input.Reserved, ctx.Site), Style = ctx.S("style") };
            b.Wet = math.min(ctx.F("wetBand"), math.max(2f, shore.MaxDepth * 0.2f));
            b.D = math.min(ctx.F("beachDepth"), math.max(b.Wet + 6f, shore.MaxDepth - 6f));
            var pathRole = ctx.S("pathWidth") == "wide" ? NetRole.PathwayWide : NetRole.Pathway;
            b.Hw = Composer.DefaultHalfWidth(pathRole);
            b.HasPath = ctx.B("backPath") && shore.MaxDepth > b.D + b.Hw + 2f;
            ctx.Plan.Facts["beachDepth"] = b.D;

            Terrain(b);
            ctx.Check();
            Ground(b);
            Paths(b, pathRole);
            ctx.Check();
            Courts(b);
            Rocks(b);
            TidePools(b);
            Lifeguards(b);
            Showers(b);
            Boats(b);
            ctx.Check();
            UmbrellaSets(b);
            FirePits(b);
            BackPathFurniture(b);
            HutsAndKiosks(b);
            ctx.Check();
            Backdrop(b);

            EmitGreenRegions(ctx, b.Occ);
            CommonStats(ctx);
            ctx.Stat("Placemaker.Stat.Shoreline", System.Math.Round(shore.Length), Keys.UnitM);
            ctx.Stat("Placemaker.Stat.Umbrellas", b.Umbrellas, Keys.UnitCount);
            ctx.Stat("Placemaker.Stat.Loungers", b.Loungers, Keys.UnitCount);
            ctx.Stat("Placemaker.Stat.Showers", b.Showers, Keys.UnitCount);
            ctx.Stat("Placemaker.Stat.Huts", b.Huts, Keys.UnitCount);
            ctx.Stat("Placemaker.Stat.Lifeguards", b.Guards, Keys.UnitCount);
            if (b.Courts > 0) ctx.Stat("Placemaker.Stat.Courts", b.Courts, Keys.UnitCount);
            if (b.Rocks > 0) ctx.Stat("Placemaker.Stat.Rocks", b.Rocks, Keys.UnitCount);
            ctx.Plan.Facts["tidePools"] = b.Pools;
            ctx.Plan.Facts["boats"] = b.Boats;
            ctx.Plan.Facts["firePits"] = b.Fires;
            ctx.Plan.Facts["kiosks"] = b.Kiosks;
            ctx.Plan.Facts["accessPaths"] = b.Access.Count;
        }

        // ───────────── terrain, ground ─────────────

        private static void Terrain(Work b)
        {
            var ctx = b.Ctx; var shore = b.Shore;
            bool carve = ctx.B("carve");
            bool dunes = ctx.S("backdrop") == "dunes" && ctx.F("duneHeight") > 0f;
            if (!carve && !dunes) return;
            float g = ctx.F("slope") / 100f;
            float D = b.D, bank = math.max(8f, D * 0.4f);
            float duneStart = D + (b.HasPath ? b.Hw + 2.5f : 2f), duneEnd = duneStart + ctx.F("duneDepth");
            float duneH = ctx.F("duneHeight");
            uint noise = Hashing.Mix(ctx.Seed, 0xD00Eu);
            float water = shore.WaterLevel;
            b.Terrain = ShoreTerrain.Shape(ctx, shore, (p, h0, d) =>
            {
                float t = h0;
                if (carve)
                {
                    var q = shore.Nearest(p, out _, out _);
                    float hs = ctx.H(q);
                    if (math.isfinite(water)) hs = math.min(hs, water + 0.3f);
                    float plane = hs + g * d;
                    if (d <= D) t = math.min(h0, plane);
                    else if (d <= D + bank) t = math.min(h0, math.lerp(plane, h0, ShoreTerrain.Smooth(0f, 1f, (d - D) / bank)));
                }
                if (dunes && d > duneStart && d < duneEnd)
                {
                    float u = (d - duneStart) / (duneEnd - duneStart);
                    float ridge = math.pow(math.sin(math.PI * u), 1.3f);
                    float hummock = ValueNoise.Clumps(p, 16f, noise);
                    t += duneH * ridge * (0.45f + 0.8f * hummock);
                }
                return t;
            }, 14f, 28f);
        }

        private static void Ground(Work b)
        {
            var ctx = b.Ctx; var shore = b.Shore;
            float top = b.HasPath ? b.D : b.D + 2f;
            var band = shore.Band(0f, top);
            if (ctx.B("paint"))
            {
                foreach (var ring in band) TerrainPaint.Patch(ctx, ring, ctx.I("paintLayer"), 1f, 3f);
                // Dunes are sand too: lighter paint fading into the vegetation behind.
                if (ctx.S("backdrop") == "dunes")
                    foreach (var ring in shore.Band(top + b.Hw + 0.5f, top + b.Hw + 2.5f + ctx.F("duneDepth")))
                        TerrainPaint.Patch(ctx, ring, ctx.I("paintLayer"), 0.6f, 4f);
            }
            if (ctx.B("sandSurface") && AssetAvailability.Require(ctx.Plan, AssetCategories.SurfaceSand, new[] { ctx.S("sandAsset") }, Keys.Category(AssetCategories.SurfaceSand)))
                foreach (var ring in band) ctx.Surface(ring, SurfaceRole.Ground, AssetMix.Pick(BeachArt.Or(ctx.S("sandAsset"), BeachArt.Sand), 0u), AssetCategories.SurfaceSand);
            if (ctx.S("backdrop") == "lawn")
                foreach (var ring in shore.Band(top + b.Hw + 0.8f, shore.MaxDepth + 20f))
                    ctx.Surface(ring, SurfaceRole.Grass, AssetMix.One(""));
        }

        // ───────────── circulation ─────────────

        private void Paths(Work b, NetRole role)
        {
            var ctx = b.Ctx; var shore = b.Shore; var occ = b.Occ;
            var asset = AssetMix.One(ctx.S("pathAsset"));
            var spine = new List<(float2, float2)>();
            if (b.HasPath)
                foreach (var line in shore.Offset(b.D, b.Hw + 1f, 24f))
                {
                    var curves = Shoreline.Curves(shore.Land, line, role);
                    if (curves.Count == 0) continue;
                    ParkPaths.Index(curves, occ.Corridors, 100);
                    ParkPaths.Index(curves, occ.Paths, 100);
                    ParkPaths.Emit(ctx, curves, NoGates, asset);
                    foreach (var c in curves)
                        for (int i = 1; i < c.Samples.Length; i++) spine.Add((c.Samples[i - 1], c.Samples[i]));
                    b.Lines.Add(line);
                }
            b.HasPath = b.Lines.Count > 0;

            // Entrances from the land side (never across the sand).
            var all = Gates(ctx);
            var gates = new List<Gate>();
            foreach (var g in all) if (shore.Depth(g.Position) > b.D * 0.6f) gates.Add(g);
            if (gates.Count == 0) gates = all;
            if (spine.Count > 0) ConnectGates(ctx, occ, gates, spine, NetRole.Pathway, asset);

            // Boardwalks across the sand down to the wet band.
            float spacing = ctx.F("accessSpacing");
            if (b.HasPath)
                foreach (var line in b.Lines)
                {
                    float len = Shoreline.LengthOf(line);
                    int n = math.max(1, (int)math.floor(len / spacing));
                    for (int k = 0; k < n; k++)
                    {
                        var top = Shoreline.PointOn(line, (k + 0.5f) * len / n, out _);
                        TryAccess(b, top, asset);
                    }
                }
            else
                foreach (var g in gates)
                {
                    shore.Nearest(g.Position, out var inw, out _);
                    var from = g.Position;
                    if (shore.Depth(from) < b.Wet + 8f) continue;
                    TryAccess(b, from, asset);
                }
        }

        private static void TryAccess(Work b, float2 top, AssetRef asset)
        {
            var shore = b.Shore; var occ = b.Occ;
            shore.Nearest(top, out var inw, out _);
            float run = shore.Depth(top) - b.Wet - 3f;
            if (run < 6f) return;
            var bottom = top - inw * run;
            if (!shore.Land.ContainsDisc(bottom, 1.5f) || shore.Land.CrossesBoundary(top, bottom, true)) return;
            float len = math.distance(top, bottom);
            for (float s = b.Hw + 2f; s < len; s += 1.5f)
            {
                var q = math.lerp(top, bottom, s / len);
                if (occ.Corridors.Overlaps(q, 1.2f, 0.3f) || occ.Discs.Overlaps(q, 1.2f, 0.2f) || occ.HitsPolygon(q, 1.2f)) return;
            }
            foreach (var (t0, b0) in b.Access) if (math.distance(t0, top) < 15f) return;
            LeisureKit.Path(b.Ctx, occ, top, bottom, NetRole.Pathway, asset);
            b.Access.Add((top, bottom));
        }

        // ───────────── on the sand ─────────────

        private static float Yaw(float2 dir) => math.atan2(dir.x, dir.y);

        private static void Courts(Work b)
        {
            var ctx = b.Ctx; var shore = b.Shore;
            int wanted = ctx.I("courts");
            if (wanted == 0 && b.Style == "volleyball") wanted = math.clamp((int)(shore.Length / 40f), 2, 6);
            if (wanted <= 0) return;
            string cat = LeisureKit.Category(ctx, "", AssetCategories.BeachVolleyball, AssetCategories.Sports);
            if (cat == null) return;
            float dc = math.clamp((b.Wet + b.D) * 0.5f, b.Wet + 6f, b.D - 6f);
            if (dc < b.Wet + 5f) { ctx.Issue(IssueSeverity.Info, "Placemaker.Beach.Issue.NoCourtSpace"); return; }
            int tries = wanted * 8;   // fine stations: courts are 18 m long, coarse ones skipped every other gap (art pass a3)
            var stations = shore.Stations(tries, 12f);
            for (int i = 0; i < stations.Count && b.Courts < wanted; i++)
            {
                var c0 = shore.At(stations[i], out var inw, out var tan);
                var rect = new OrientedRect { Center = c0 + inw * dc, Axis = tan, HalfLength = 9f, HalfWidth = 5f };
                if (!LeisureKit.RectFits(shore.Land, b.Occ, rect, 0.5f)) continue;
                LeisureKit.TakeRect(b.Occ, rect);
                uint v = Hashing.Mix(ctx.Seed, 0xC0u + (uint)i);
                // No vanilla volleyball net: the tennis net at 0.7 scale (9.2 m) spans the 10 m court, facing
                // along it (yaw = court axis puts the net's long local x across the court). Art pass a3.
                var net = ctx.Object(rect.Center, rect.Yaw, ObjectRole.Furniture, new AssetRef("", "TennisNet01"), v, -1, cat);
                net.Scale = 0.7f;
                if (!ctx.B("sandSurface") && AssetAvailability.Has(AssetCategories.SurfaceSand))
                    ctx.Surface(rect.Ring, SurfaceRole.Ground, AssetMix.One(""), AssetCategories.SurfaceSand);
                b.Courts++;
            }
            if (b.Courts < wanted) ctx.Issue(IssueSeverity.Info, "Placemaker.Beach.Issue.FewerCourts", b.Courts, wanted);
        }

        private static void Rocks(Work b)
        {
            var ctx = b.Ctx; var shore = b.Shore;
            int wanted = ctx.I("rockClusters");
            if (wanted == 0 && b.Style == "rocky") wanted = math.clamp((int)(shore.Length / 30f), 4, 12);
            bool headlands = b.Style == "cove" && !shore.Closed;
            if (wanted <= 0 && !headlands) return;
            if (!AssetAvailability.Require(ctx.Plan, AssetCategories.Rock, null, Keys.Category(AssetCategories.Rock))) return;
            var spots = new List<(float2 c, float r, int n)>();
            if (headlands)
                foreach (float s in new[] { math.min(8f, shore.Length * 0.1f), shore.Length - math.min(8f, shore.Length * 0.1f) })
                {
                    var p = shore.At(s, out var inw, out _);
                    spots.Add((p + inw * math.min(6f, b.D * 0.3f), 7.5f, 16));
                }
            var stations = shore.Stations(wanted, 6f, 0.3f);
            for (int i = 0; i < stations.Count; i++)
            {
                uint v = Hashing.Mix(ctx.Seed, 0x40C0u + (uint)i);
                var p = shore.At(stations[i], out var inw, out var tan);
                bool rocky = b.Style == "rocky";
                // Rocky shores: clusters straddle the waterline; elsewhere a few on the upper sand.
                float d = rocky ? 1.5f + Hashing.Unit(v) * (b.Wet + 3f) : b.Wet + Hashing.Unit(v) * math.max(2f, b.D - b.Wet - 4f);
                float r = rocky ? 2.5f + Hashing.Unit(Hashing.Mix(v, 1u)) * 3f : 2f + Hashing.Unit(Hashing.Mix(v, 1u)) * 2f;
                spots.Add((p + inw * d + tan * (Hashing.Unit(Hashing.Mix(v, 2u)) - 0.5f) * 8f, r, rocky ? 9 : 5));
            }
            const float golden = 2.39996323f;
            for (int k = 0; k < spots.Count; k++)
            {
                var (c, R, n) = spots[k];
                uint cs = Hashing.Mix(ctx.Seed, 0x40D0u + (uint)k * 31u);
                int placed = 0;
                for (int i = 0; i < n * 2 && placed < n; i++)
                {
                    float rr = R * math.sqrt((i + 0.5f) / (n * 1.4f));
                    if (rr > R) break;
                    var p = c + new float2(math.cos(i * golden + cs % 7), math.sin(i * golden + cs % 7)) * rr;
                    uint v = Hashing.Mix(cs, (uint)i);
                    float scale = 0.5f + 1.1f * Hashing.Unit(v) * (1f - rr / (R + 0.01f) * 0.6f);
                    float fr = 1.1f * scale;
                    if (!b.Occ.Free(p, fr, 0.5f, 0.1f)) continue;
                    var o = ctx.Object(p, Hashing.Unit(Hashing.Mix(v, 5u)) * 6.283f, ObjectRole.Furniture, AssetMix.One(""), v, -1, AssetCategories.Rock);
                    o.Scale = scale;
                    b.Occ.Take(p, fr, 2);
                    placed++;
                }
                b.Rocks += placed;
            }
        }

        private static void TidePools(Work b)
        {
            var ctx = b.Ctx; var shore = b.Shore;
            int wanted = ctx.I("tidePools");
            if (wanted == 0 && b.Style == "rocky") wanted = math.clamp((int)(shore.Length / 40f), 2, 6);
            if (wanted <= 0) return;
            bool rocks = AssetAvailability.Has(AssetCategories.Rock);
            var stations = shore.Stations(wanted * 2, 10f, 0.7f);
            for (int i = 0; i < stations.Count && b.Pools < wanted; i++)
            {
                uint v = Hashing.Mix(ctx.Seed, 0x7D00u + (uint)i);
                var p = shore.At(stations[i], out var inw, out _);
                float r = 2.2f + Hashing.Unit(v) * 2f;
                var c = p + inw * (b.Wet * 0.5f + r + 1f);
                if (shore.EdgeDistance(c) < r + 8f || !b.Occ.Free(c, r + 0.6f, 1f, 0.2f)) continue;
                float h = b.Terrain != null ? b.Terrain.HeightAfter(c) : ctx.H(c);
                ShoreTerrain.Hollow(ctx, LeisureKit.Blob(c, r, v, 12, 0.25f), LeisureKit.Blob(c, r * 0.55f, Hashing.Mix(v, 9u), 10, 0.2f), h - 0.35f, h - 0.75f);
                b.Occ.Take(c, r, 2);
                b.Pools++;
                if (!rocks) continue;
                int n = 3 + (int)(Hashing.Unit(Hashing.Mix(v, 3u)) * 3f);
                for (int k = 0; k < n; k++)
                {
                    float a = Hashing.Unit(Hashing.Mix(v, (uint)k + 20u)) * 6.283f;
                    var q = c + new float2(math.cos(a), math.sin(a)) * (r + 1.1f);
                    if (!b.Occ.Free(q, 0.7f, 0.5f, 0.05f)) continue;
                    var o = ctx.Object(q, a, ObjectRole.Furniture, AssetMix.One(""), Hashing.Mix(v, (uint)k + 40u), -1, AssetCategories.Rock);
                    o.Scale = 0.5f + 0.5f * Hashing.Unit(Hashing.Mix(v, (uint)k + 60u));
                    b.Occ.Take(q, 0.7f, 2);
                    b.Rocks++;
                }
            }
        }

        private static void Lifeguards(Work b)
        {
            var ctx = b.Ctx; var shore = b.Shore;
            int wanted = ctx.I("lifeguards");
            if (wanted <= 0) return;
            string cat = LeisureKit.Category(ctx, "", AssetCategories.BeachLifeguard);
            if (cat == null) return;
            foreach (float s in shore.Stations(wanted, 10f))
            {
                for (int k = 0; k < 5; k++)
                {
                    float off = (k == 0 ? 0 : (k % 2 == 1 ? 1 : -1) * ((k + 1) / 2) * 5f);
                    var p = shore.At(s + off, out var inw, out _);
                    var q = p + inw * (b.Wet + 3f);
                    if (!LeisureKit.Put(ctx, b.Occ, q, Yaw(-inw), ObjectRole.Furniture, AssetMix.One(""), cat, 1.8f, Hashing.Mix(ctx.Seed, 0x6A00u + (uint)b.Guards), 0.8f, 0.4f)) continue;
                    b.Guards++;
                    break;
                }
            }
        }

        private static void Showers(Work b)
        {
            var ctx = b.Ctx;
            if (!ctx.B("showers") || b.Access.Count == 0) return;
            string cat = LeisureKit.Category(ctx, "", AssetCategories.BeachShower);
            if (cat == null) return;
            foreach (var (top, bottom) in b.Access)
            {
                var dir = math.normalizesafe(bottom - top);
                var side = new float2(-dir.y, dir.x);
                foreach (float sgn in new[] { 1f, -1f })
                {
                    var p = bottom - dir * 1.5f + side * sgn * 2.4f;
                    if (LeisureKit.Put(ctx, b.Occ, p, Yaw(-side * sgn), ObjectRole.Furniture, AssetMix.One(""), cat, 0.6f, Hashing.Mix(ctx.Seed, 0x5400u + (uint)b.Showers), 0.5f, 0.2f))
                    { b.Showers++; break; }
                }
            }
        }

        private static void Boats(Work b)
        {
            var ctx = b.Ctx; var shore = b.Shore;
            if (!ctx.B("boats")) return;
            string cat = LeisureKit.Category(ctx, "", AssetCategories.BeachBoat);
            if (cat == null) return;
            int groups = math.clamp((int)(shore.Length / 50f), 1, 6);
            foreach (float s in shore.Stations(groups, 8f, 0.25f))
            {
                uint v = Hashing.Mix(ctx.Seed, 0xB0A7u + (uint)b.Boats);
                int n = 2 + (int)(Hashing.Unit(v) * 3f);
                for (int k = 0; k < n; k++)
                {
                    var p = shore.At(s + (k - (n - 1) * 0.5f) * 2.6f, out var inw, out _);
                    var q = p + inw * math.clamp(b.Wet * 0.6f, 2.5f, b.Wet + 1f);
                    float yaw = Yaw(-inw) + (Hashing.Unit(Hashing.Mix(v, (uint)k)) - 0.5f) * 0.4f;
                    if (LeisureKit.Put(ctx, b.Occ, q, yaw, ObjectRole.Furniture, AssetMix.One(""), cat, 1.2f, Hashing.Mix(v, (uint)k + 7u), 0.5f, 0.1f)) b.Boats++;
                }
            }
        }

        private static void UmbrellaSets(Work b)
        {
            var ctx = b.Ctx; var shore = b.Shore; var occ = b.Occ;
            float density = ctx.F("umbrellas");
            if (density <= 0f) return;
            string umbrella = LeisureKit.Category(ctx, ctx.S("umbrellaAsset"), AssetCategories.BeachUmbrella, AssetCategories.CafeSeating);
            if (umbrella == null) return;
            string lounger = LeisureKit.Category(ctx, ctx.S("loungerAsset"), AssetCategories.BeachLounger, AssetCategories.Bench);
            var uIds = BeachArt.Or(ctx.S("umbrellaAsset"), BeachArt.Umbrellas);
            var lIds = BeachArt.Or(ctx.S("loungerAsset"), BeachArt.Loungers);
            uint setPick = Hashing.Mix(ctx.Seed, 0x5E75u);
            float d0 = b.Wet + 2.5f, d1 = b.D - (b.HasPath ? b.Hw + 2.5f : 1.5f);
            if (d1 <= d0) return;
            int wanted = (int)math.round(density * shore.Length * (d1 - d0) / 10000f);
            if (wanted <= 0) return;

            bool Set(float2 p, uint v, bool tidy)
            {
                shore.Nearest(p, out var inw, out _);
                float dp = shore.Depth(p);
                if (dp < d0 || dp > d1) return false;
                var toWater = -inw;
                var tan = new float2(inw.y, -inw.x);
                float jitter = tidy ? 0f : (Hashing.Unit(Hashing.Mix(v, 3u)) - 0.5f) * 0.6f;
                if (!occ.Free(p, 1.2f, 0.6f, 0.3f)) return false;
                int loungers = tidy ? 2 : 1 + (int)(Hashing.Unit(Hashing.Mix(v, 4u)) * 2.99f);
                var spots = new List<float2>();
                for (int k = 0; k < loungers; k++)
                {
                    float side = loungers == 1 ? (Hashing.Unit(v) < 0.5f ? 1f : -1f) : (k == 0 ? -1f : k == 1 ? 1f : 0f);
                    var q = p + tan * side * 1.4f + toWater * (k == 2 ? 2f : 0.6f);
                    if (lounger != null && occ.Free(q, 0.8f, 0.4f, 0.1f)) spots.Add(q);
                }
                // One umbrella colour and one lounger model per set; sets vary along the beach.
                var uAsset = AssetMix.Pick(uIds, Hashing.Mix(setPick, v)); var lAsset = AssetMix.Pick(lIds, Hashing.Mix(setPick, v + 1u));
                ctx.Object(p, Yaw(toWater) + jitter, ObjectRole.Furniture, uAsset, v, -1, umbrella);
                occ.Take(p, 1.2f, 1);
                b.Umbrellas++;
                for (int k = 0; k < spots.Count; k++)
                {
                    var role = lounger == AssetCategories.Bench ? ObjectRole.Bench : ObjectRole.Furniture;
                    ctx.Object(spots[k], Yaw(toWater) + jitter, role, lAsset, Hashing.Mix(v, (uint)k + 11u), -1, role == ObjectRole.Bench ? null : lounger);
                    occ.Take(spots[k], 0.8f, 1);
                    b.Loungers++;
                }
                return true;
            }

            if (ctx.S("layout") == "rows")
            {
                const float rowGap = 6.5f;
                int rows = math.max(1, (int)math.floor((d1 - d0) / rowGap) + 1);
                float spacing = math.max(4.8f, rows * shore.Length / math.max(1, wanted));
                for (int r = 0; r < rows && b.Umbrellas < wanted; r++)
                {
                    float d = d0 + 0.5f + r * rowGap;
                    if (d > d1) break;
                    foreach (var line in shore.Offset(d, 1.5f, 6f, 2f))
                    {
                        float len = Shoreline.LengthOf(line);
                        for (float s = (r & 1) == 0 ? spacing * 0.5f : spacing; s < len && b.Umbrellas < wanted; s += spacing)
                            Set(Shoreline.PointOn(line, s, out _), Hashing.Mix(ctx.Seed, 0x0B00u + (uint)(r * 997) + (uint)(s * 10f)), true);
                    }
                }
            }
            else
            {
                uint clump = Hashing.Mix(ctx.Seed, 0x0C10u);
                int i = 0;
                foreach (var p in Scatter.Candidates(shore.Land, 3.4f, Hashing.Mix(ctx.Seed, 0x0C11u), math.min(40000, wanted * 60 + 400), ctx.Cancel))
                {
                    if (b.Umbrellas >= wanted) break;
                    float chance = Scatter.Clustering(p, 24f, clump, 0.25f);
                    if (Hashing.Unit(Hashing.Mix(clump, (int)(p.x * 13f), (int)(p.y * 7f))) > chance) continue;
                    Set(p, Hashing.Mix(ctx.Seed, 0x0C20u + (uint)i++), false);
                }
            }
        }

        private static void FirePits(Work b)
        {
            var ctx = b.Ctx; var shore = b.Shore;
            if (!ctx.B("fires")) return;
            // No vanilla fire pit, and the camping fallback is makeshift tents: build the ring from small rocks instead.
            if (!AssetAvailability.Has(AssetCategories.Rock)) return;
            int wanted = math.clamp((int)(shore.Length / 60f), 1, 4);
            float d = math.clamp((b.Wet + b.D) * 0.55f, b.Wet + 4f, b.D - 4f);
            foreach (float s in shore.Stations(wanted, 10f, 0.75f))
            {
                var p = shore.At(s, out var inw, out _);
                var c = p + inw * d;
                if (!b.Occ.Free(c, 3.6f, 0.8f, 0.3f)) continue;
                // A ring of small beach stones, rough campground benches around it.
                uint v = Hashing.Mix(ctx.Seed, 0xF1E0u + (uint)b.Fires);
                for (int k = 0; k < 7; k++)
                {
                    float a = k * 0.8976f + Hashing.Unit(Hashing.Mix(v, (uint)k + 50u)) * 0.3f;
                    var o = ctx.Object(c + new float2(math.cos(a), math.sin(a)) * 0.85f, a, ObjectRole.Furniture,
                        new AssetRef("", "Rock05", "Rock12", "Rock21"), Hashing.Mix(v, (uint)k + 60u), -1, AssetCategories.Rock);
                    o.Scale = 0.55f;
                }
                b.Occ.Take(c, 1.3f, 1);
                float phase = Hashing.Unit(v) * 6.283f;
                for (int k = 0; k < 3; k++)
                {
                    float a = phase + k * 2.094f;
                    var q = c + new float2(math.cos(a), math.sin(a)) * 2.7f;
                    LeisureKit.Put(ctx, b.Occ, q, LeisureKit.Yaw(q, c), ObjectRole.Bench, new AssetRef("", "CampgroundBench01", "CampgroundBench02"), null, 1.1f, Hashing.Mix(v, (uint)k), 0.5f, 0.1f);
                }
                b.Fires++;
            }
        }

        // ───────────── back path & behind ─────────────

        private static void BackPathFurniture(Work b)
        {
            var ctx = b.Ctx; var shore = b.Shore;
            if (!b.HasPath) return;
            float benchSpacing = ctx.F("benchSpacing");
            bool lamps = ctx.B("lamps");
            int benches = 0, lampsN = 0;
            for (int li = 0; li < b.Lines.Count; li++)
            {
                var line = b.Lines[li];
                float len = Shoreline.LengthOf(line);
                if (benchSpacing > 0f)
                    for (float s = benchSpacing * 0.5f; s < len; s += benchSpacing)
                    {
                        var p = Shoreline.PointOn(line, s, out _);
                        shore.Nearest(p, out var inw, out _);
                        var q = p - inw * (b.Hw + 1.7f);
                        uint v = Hashing.Mix(ctx.Seed, 0xBE00u + (uint)benches + (uint)li * 1000u);
                        if (!LeisureKit.Put(ctx, b.Occ, q, Yaw(-inw), ObjectRole.Bench, AssetMix.Pick(BeachArt.Benches, 0u), null, 1.3f, v, 0.5f, 0.2f)) continue;
                        benches++;
                        if ((benches & 1) == 0)
                        {
                            var tan = new float2(inw.y, -inw.x);
                            LeisureKit.Put(ctx, b.Occ, q + tan * 2.3f, 0f, ObjectRole.Bin, AssetMix.Pick(BeachArt.Bins, 0u), null, 0.5f, Hashing.Mix(v, 1u), 0.4f, 0.2f);
                        }
                    }
                if (lamps)
                    for (float s = 12f; s < len; s += 28f)
                    {
                        var p = Shoreline.PointOn(line, s, out _);
                        shore.Nearest(p, out var inw, out _);
                        if (LeisureKit.Put(ctx, b.Occ, p + inw * (b.Hw + 1f), 0f, ObjectRole.Lamp, AssetMix.Pick(BeachArt.Lamps, 0u), null, 0.5f, Hashing.Mix(ctx.Seed, 0x1A00u + (uint)lampsN), 0.4f, 0.2f))
                            lampsN++;
                    }
            }
        }

        private static void HutsAndKiosks(Work b)
        {
            if (!b.HasPath) return;
            Kiosks(b);   // first: a long hut row would otherwise take every spot behind the path
            Huts(b);
        }

        private static void Kiosks(Work b)
        {
            var ctx = b.Ctx; var shore = b.Shore;
            int kiosks = ctx.I("kiosks");
            if (kiosks <= 0) return;
            string kcat = LeisureKit.Category(ctx, "", AssetCategories.Kiosk);
            if (kcat == null) return;
            var tops = new List<float2>();
            foreach (var (top, _) in b.Access) tops.Add(top);
            if (tops.Count == 0) foreach (var line in b.Lines) tops.Add(Shoreline.PointOn(line, Shoreline.LengthOf(line) * 0.5f, out _));
            for (int i = 0; i < kiosks * 2 && b.Kiosks < kiosks; i++)
            {
                // Spread over the access junctions, then try the remaining ones.
                int idx = i < kiosks ? (int)((i + 0.5f) * tops.Count / kiosks) % tops.Count : (i * 3 + 1) % tops.Count;
                var p = tops[idx];
                shore.Nearest(p, out var inw, out _);
                var tan = new float2(inw.y, -inw.x);
                foreach (float along in new[] { 6f, -6f, 12f, -12f })
                {
                    var q = p + inw * (b.Hw + 4f) + tan * along;
                    if (LeisureKit.Put(ctx, b.Occ, q, Yaw(-inw), ObjectRole.Furniture, AssetMix.Pick(BeachArt.Kiosks, (uint)b.Kiosks), kcat, 2.5f, Hashing.Mix(ctx.Seed, 0x6105u + (uint)b.Kiosks), 0.8f, 0.3f))
                    { b.Kiosks++; break; }
                }
            }
        }

        private static void Huts(Work b)
        {
            var ctx = b.Ctx; var shore = b.Shore;
            string mode = ctx.S("huts");
            string hut = mode == "none" ? null : LeisureKit.Category(ctx, ctx.S("hutAsset"), AssetCategories.BeachHut, AssetCategories.GardenShed);
            if (hut == null) return;
            var hutAsset = AssetMix.One(ctx.S("hutAsset"));
            float hd = b.Hw + 2.8f;
            foreach (var line in b.Lines)
            {
                float len = Shoreline.LengthOf(line);
                var places = new List<float>();
                if (mode == "row")
                    for (float s = 4f; s < len - 4f; s += 3.6f) places.Add(s);
                else
                {
                    int groups = math.max(1, (int)(len / 70f));
                    for (int g = 0; g < groups; g++)
                    {
                        float c = (g + 0.5f) * len / groups + 9f;
                        for (int k = -1; k <= 1; k++) places.Add(c + k * 3.6f);
                    }
                }
                foreach (float s in places)
                {
                    if (s < 0f || s > len) continue;
                    var p = Shoreline.PointOn(line, s, out _);
                    shore.Nearest(p, out var inw, out _);
                    var q = p + inw * hd;
                    if (LeisureKit.Put(ctx, b.Occ, q, Yaw(-inw), ObjectRole.Furniture, hutAsset, hut, 1.5f, Hashing.Mix(ctx.Seed, 0x4070u + (uint)b.Huts), 0.6f, 0.15f))
                        b.Huts++;
                }
            }
        }

        private static void Backdrop(Work b)
        {
            var ctx = b.Ctx; var shore = b.Shore; var occ = b.Occ;
            string backdrop = ctx.S("backdrop");
            float behind = b.D + (b.HasPath ? b.Hw + 2.2f : 1.5f);
            float duneEnd = behind + ctx.F("duneDepth");
            var trees = BeachArt.Or(ctx.L("treeSpecies"), BeachArt.Trees(b.Style, backdrop));
            var grass = BeachArt.Or(ctx.L("grassSpecies"), backdrop == "dunes" ? BeachArt.DuneGrass : BeachArt.Shrubs);
            bool canTrees = trees.Length > 0 || AssetAvailability.Has(AssetCategories.Tree);
            float treeDensity = ctx.F("treeDensity");

            if (backdrop == "dunes")
            {
                // Marram grass in clumps over the dunes, densest on the crests.
                float pct = ctx.F("grassDensity") / 100f;
                foreach (var ring in shore.Band(behind, duneEnd))
                {
                    var band = LayerRegion.From(ring);
                    if (band == null) continue;
                    int wanted = (int)(band.Area / 10000f * 450f * pct);
                    LeisureKit.Plant(ctx, occ, band, wanted, ObjectRole.Bush, grass, 0.9f, 9f, 0.2f, 0xD6A5u, null);
                }
                if (ctx.B("duneFence")) SandFences(b, behind - 0.6f);
                if (canTrees && treeDensity > 0f)
                    foreach (var ring in shore.Band(duneEnd, shore.MaxDepth + 20f))
                    {
                        var band = LayerRegion.From(ring);
                        if (band == null) continue;
                        LeisureKit.Plant(ctx, occ, band, (int)(band.Area / 10000f * treeDensity * 1.5f), ObjectRole.Tree, trees, 2.5f, 20f, 0.3f, 0x7E55u, null, 1, 3);
                    }
                return;
            }
            if (backdrop == "none" && treeDensity <= 0f) return;
            float factor = backdrop == "woods" ? 1.8f : 1f;
            float from = backdrop == "woods" ? behind : behind + 3f;
            if (canTrees && treeDensity > 0f)
                foreach (var ring in shore.Band(from, shore.MaxDepth + 20f))
                {
                    var band = LayerRegion.From(ring);
                    if (band == null) continue;
                    LeisureKit.Plant(ctx, occ, band, (int)(band.Area / 10000f * treeDensity * factor), ObjectRole.Tree, trees, 2.5f,
                        backdrop == "lawn" ? 26f : 16f, backdrop == "lawn" ? 0.15f : 0.35f, 0x7E56u, null, 1, 3);
                }
            if (backdrop == "woods" || backdrop == "lawn")
            {
                float pct = ctx.F("grassDensity") / 100f;
                foreach (var ring in shore.Band(behind, behind + 6f))
                {
                    var band = LayerRegion.From(ring);
                    if (band == null) continue;
                    LeisureKit.Plant(ctx, occ, band, (int)(band.Area / 10000f * 120f * pct), ObjectRole.Bush, grass, 1f, 10f, 0.2f, 0xB0B5u, null);
                }
            }
        }

        /// <summary>Sand fences along the dune foot: short runs with gaps where paths cross.</summary>
        private static void SandFences(Work b, float d)
        {
            var ctx = b.Ctx; var occ = b.Occ;
            foreach (var line in b.Shore.Offset(d, 1f, 10f, 3f))
            {
                float len = Shoreline.LengthOf(line);
                const float run = 16f, gap = 5f;
                for (float s = 2f; s + 4f < len; s += run + gap)
                {
                    var a = Shoreline.PointOn(line, s, out _);
                    var e = Shoreline.PointOn(line, math.min(len, s + run), out _);
                    if (math.distance(a, e) < 4f) continue;
                    bool blocked = false;
                    for (float t = 0f; t <= 1.001f; t += 0.125f)
                    {
                        var q = math.lerp(a, e, t);
                        if (occ.Paths.Overlaps(q, 0.6f, 0.5f) || occ.Discs.Overlaps(q, 0.3f, 0.1f)) { blocked = true; break; }
                    }
                    if (blocked || b.Shore.Land.CrossesBoundary(a, e, false)) continue;
                    ctx.StraightNet(a, e, NetRole.Fence, AssetMix.One(""));
                    occ.TakeCorridor(a, e, 0.3f, 5);
                }
            }
        }
    }
}
