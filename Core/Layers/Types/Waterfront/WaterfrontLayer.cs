// Waterfront: a linear promenade along the shore edge.
//
//   edge        railings (gaps at steps and piers), or mooring bollards on a harbour quay, or riprap
//               rocks on a fishing bank; lamps and benches facing the water just behind
//   steps       quay steps cut down towards the water (stepped Level terraform + paving), at a few
//               spots or along the whole edge (river ghats)
//   promenade   paved/gravel band with a walk parallel to the water; piers reach out over the water
//               as paths when the area was drawn over it
//   land side   kiosks with cafe tables, nautical props, a tree row or avenue, then a lawn or park
//
// The water side comes from WaterDepthAt or the "Water side" choice (Shoreline). Everything else is
// laid out by the distance from the water, so a curved shore gets a curved promenade.

using System.Collections.Generic;
using Placemaker.Core.Geometry;
using Placemaker.Core.Layers.Common;
using Placemaker.Core.Layers.Leisure;
using Placemaker.Core.Layers.Park;
using Placemaker.Core.Layers.Types.Beach;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Types.Waterfront
{
    [AreaGenerator(72, Group = "water")]
    public sealed class WaterfrontLayer : LeisureLayer
    {
        public static readonly string[] Styles = { "promenade", "pier", "harbor", "riverSteps", "fishing" };
        private static readonly List<Gate> NoGates = new List<Gate>();
        private const float StepRun = 1.5f;

        protected override string Area => "Waterfront";
        public override string Id => "waterfront";
        public override string Icon => "Media/Game/Icons/LeisurePier01Medium.svg";
        public override float MinArea => 800f;

        protected override List<FeatureInfo> BuildFeatures() => new List<FeatureInfo>
        {
            new FeatureInfo
            {
                Layer = BeachBarAddOn.LayerId, LabelKey = "Placemaker.Feature.cafeTerrace",
                Preset = ParkLayer.Bag(("tables", 8.0), ("umbrellas", false)),
            },
            Feature("restrooms"), Feature("shops", ("count", 2.0), ("where", "paths")), StreetShopsFeature(Area),
            CoreFeature("playground", ("size", "small")), Feature("picnic"), Feature("fountain"), Feature("flowerbeds"),
            Feature("bigtrees"), Feature("decals", ("theme", "puddles")),
        };

        protected override void DefineControls(ControlSet c)
        {
            c.Choice("style", "promenade", ControlTier.Essential, "waterfront", Styles).Tip();
            c.Choice("waterSide", "auto", ControlTier.Essential, "waterfront", Shoreline.Sides).Tip();
            c.Slider("promWidth", 12, 6, 30, 1, "m", ControlTier.Essential, "waterfront").Tip();
            c.Toggle("railings", true, ControlTier.Essential, "waterfront").Tip();

            c.Choice("surface", "paved", ControlTier.More, "promenade", "paved", "gravel", "grass", "none").Tip();
            c.Asset("surfaceAsset", AssetCategories.SurfacePaving, ControlTier.More, "promenade").When("surface=paved");
            c.Choice("pathWidth", "wide", ControlTier.More, "promenade", "wide", "narrow");
            c.Slider("lampSpacing", 20, 0, 60, 5, "m", ControlTier.More, "promenade").Layout(5).Tip();
            c.Slider("benchSpacing", 16, 0, 60, 2, "m", ControlTier.More, "promenade").Layout(5).Tip();
            c.Slider("kiosks", 1, 0, 6, 1, "count", ControlTier.More, "promenade");

            c.Asset("railingAsset", AssetCategories.Fence, ControlTier.More, "edge").When("railings=true");
            c.Toggle("moorings", false, ControlTier.More, "edge").Tip();
            c.Slider("mooringSpacing", 8, 4, 30, 1, "m", ControlTier.More, "edge").Layout(3).When("moorings=true");
            c.Toggle("riprap", false, ControlTier.More, "edge").Tip();

            c.Choice("steps", "spots", ControlTier.More, "steps", "none", "spots", "continuous").Tip();
            c.Slider("stepCount", 2, 1, 8, 1, "count", ControlTier.More, "steps").When("steps=spots");
            c.Slider("stepWidth", 12, 4, 40, 1, "m", ControlTier.More, "steps").When("steps=spots");
            c.Slider("stepLevels", 3, 2, 6, 1, "count", ControlTier.More, "steps").Tip();

            c.Slider("piers", 1, 0, 6, 1, "count", ControlTier.More, "piers").Tip();
            c.Slider("pierLength", 30, 8, 120, 2, "m", ControlTier.More, "piers");
            c.Toggle("pierHeads", false, ControlTier.More, "piers").Tip();

            c.Toggle("fishing", false, ControlTier.More, "harbor").Tip();
            c.Slider("fishingSpacing", 20, 8, 60, 2, "m", ControlTier.More, "harbor").Layout(5).When("fishing=true");
            c.Toggle("nautical", false, ControlTier.More, "harbor").Tip();

            c.Choice("trees", "row", ControlTier.More, "planting", "none", "row", "double").Tip();
            c.Slider("treeSpacing", 12, 6, 30, 1, "m", ControlTier.More, "planting").Layout(6);
            c.Choice("backdrop", "lawn", ControlTier.More, "planting", "lawn", "park", "none").Tip();
            c.AssetMulti("treeSpecies", AssetCategories.Tree, ControlTier.More, "planting");

            c.Asset("pathAsset", AssetCategories.Pathway, ControlTier.More, "assets");
            c.Asset("benchAsset", AssetCategories.Bench, ControlTier.More, "assets");
            c.Asset("lampAsset", AssetCategories.Lamp, ControlTier.More, "assets");
            EntranceMarkers(c);
            DefineStreetShopControls(c);

            c.Fixed("waterSide");
            c.RandomRange("promWidth", 8, 18);
            c.RandomRange("piers", 0, 3);
            c.Chance("moorings", 0.3f);
            c.Chance("riprap", 0.2f);
            c.Chance("fishing", 0.4f);
            c.Chance("nautical", 0.4f);
        }

        protected override void DefinePresets()
        {
            Preset("promenade", ("style", "promenade"), ("promWidth", 12.0), ("surface", "paved"), ("steps", "spots"), ("stepCount", 2.0), ("trees", "row"), ("piers", 1.0));
            Preset("pier", ("style", "pier"), ("promWidth", 9.0), ("pathWidth", "narrow"), ("surface", "none"), ("steps", "none"), ("piers", 3.0), ("pierLength", 50.0),
                ("pierHeads", true), ("nautical", true), ("trees", "none"), ("benchSpacing", 12.0), ("backdrop", "lawn"));
            Preset("harbor", ("style", "harbor"), ("promWidth", 18.0), ("railings", false), ("moorings", true), ("nautical", true), ("kiosks", 3.0),
                ("lampSpacing", 15.0), ("trees", "row"), ("treeSpacing", 16.0), ("piers", 2.0), ("steps", "spots"), ("stepCount", 1.0), ("backdrop", "none"));
            Preset("riverSteps", ("style", "riverSteps"), ("promWidth", 14.0), ("railings", false), ("steps", "continuous"), ("stepLevels", 4.0), ("trees", "double"),
                ("backdrop", "park"), ("lampSpacing", 25.0), ("piers", 0.0));
            Preset("fishing", ("style", "fishing"), ("promWidth", 8.0), ("surface", "gravel"), ("pathWidth", "narrow"), ("railings", false), ("riprap", true),
                ("fishing", true), ("fishingSpacing", 14.0), ("lampSpacing", 0.0), ("benchSpacing", 0.0), ("trees", "none"), ("backdrop", "park"), ("kiosks", 0.0),
                ("steps", "none"), ("piers", 2.0), ("pierLength", 16.0));
        }

        protected override int WantedEntrances(LayerRegion region) => math.min(4, Entrances.WantedByArea(region.Area));

        // ───────────── planning ─────────────

        private sealed class Work
        {
            public LayerContext Ctx;
            public Shoreline Shore;
            public Occupancy Occ;
            public float P, DPath, Hw;
            public readonly List<float2> Gaps = new List<float2>();   // along-shore intervals (from, to) kept open at the edge
            public readonly List<float2[]> Lines = new List<float2[]>();
            public int Steps, Piers, Benches, Lamps, Moorings, Fishing, Kiosks, Rocks, Railings;
            public float PierLength;

            public bool InGap(float s, float pad = 0f)
            {
                foreach (var g in Gaps) if (s >= g.x - pad && s <= g.y + pad) return true;
                return false;
            }
        }

        protected override void PlanCore(LayerContext ctx)
        {
            var shore = Shoreline.Analyze(ctx, ctx.LayerRegion, ctx.S("waterSide"));
            if (shore == null) { ctx.Issue(IssueSeverity.Warning, Keys.NoSpace, NameKey); return; }
            ctx.LayerRegion = shore.Land;
            var w = new Work { Ctx = ctx, Shore = shore, Occ = new Occupancy(shore.Land, ctx.Input.Reserved, ctx.Site) };
            var role = ctx.S("pathWidth") == "narrow" ? NetRole.Pathway : NetRole.PathwayWide;
            w.Hw = Composer.DefaultHalfWidth(role);
            w.P = math.min(ctx.F("promWidth"), math.max(4f, shore.MaxDepth - 2f));
            // Benches between railing and walk need ~6.6 m; narrower promenades put the walk in the middle.
            w.DPath = w.P >= 11f ? math.max(w.P * 0.5f, 6.6f + w.Hw - 2.5f + (role == NetRole.Pathway ? 0.5f : 0f)) : w.P * 0.5f + 0.5f;
            w.DPath = math.clamp(w.DPath, w.Hw + 1.2f, math.max(w.Hw + 1.2f, w.P - 0.5f));
            if (ctx.S("steps") == "continuous")
            {
                // Ghats: the walk runs along the top step, the promenade widens to hold it.
                w.DPath = math.max(w.DPath, ctx.I("stepLevels") * StepRun + w.Hw + 0.4f);
                w.P = math.min(math.max(w.P, w.DPath + w.Hw + 0.5f), math.max(w.P, shore.MaxDepth - 2f));
            }
            ctx.Plan.Facts["promenadeWidth"] = w.P;

            Steps(w);
            ctx.Check();
            Ground(w);
            Promenade(w, role);
            Piers(w);
            ctx.Check();
            FishingSpots(w);
            Edge(w);
            WaterSide(w);
            ctx.Check();
            LandSide(w);
            Backdrop(w);

            EmitGreenRegions(ctx, w.Occ);
            CommonStats(ctx);
            ctx.Stat("Placemaker.Stat.Shoreline", System.Math.Round(shore.Length), Keys.UnitM);
            ctx.Stat(Keys.StatLamps, w.Lamps, Keys.UnitCount);
            if (w.Piers > 0) ctx.Stat("Placemaker.Stat.Piers", w.Piers, Keys.UnitCount);
            if (w.Steps > 0) ctx.Stat("Placemaker.Stat.Steps", w.Steps, Keys.UnitCount);
            if (w.Fishing > 0) ctx.Stat("Placemaker.Stat.FishingSpots", w.Fishing, Keys.UnitCount);
            ctx.Plan.Facts["piers"] = w.Piers;
            ctx.Plan.Facts["pierLength"] = w.PierLength;
            ctx.Plan.Facts["steps"] = w.Steps;
            ctx.Plan.Facts["moorings"] = w.Moorings;
            ctx.Plan.Facts["railings"] = w.Railings;
            ctx.Plan.Facts["kiosks"] = w.Kiosks;
        }

        private static float Yaw(float2 dir) => math.atan2(dir.x, dir.y);

        // ───────────── steps down to the water ─────────────

        private static void Steps(Work w)
        {
            var ctx = w.Ctx; var shore = w.Shore;
            string mode = ctx.S("steps");
            if (mode == "none") return;
            int levels = ctx.I("stepLevels");
            float depth = levels * StepRun;
            if (shore.MaxDepth < depth + 4f) return;
            var windows = new List<float2>();
            float end = math.min(8f, shore.Length * 0.1f);
            if (mode == "continuous")
            {
                float usable = shore.Length - 2f * end;
                int n = math.max(1, (int)math.round(usable / 16f));
                for (int i = 0; i < n; i++) windows.Add(new float2(end + usable * i / n, end + usable * (i + 1) / n));
            }
            else
            {
                float half = ctx.F("stepWidth") * 0.5f;
                foreach (float s in shore.Stations(ctx.I("stepCount"), half + end))
                    windows.Add(new float2(s - half, s + half));
            }
            float water = shore.WaterLevel;
            bool paved = AssetAvailability.Has(AssetCategories.SurfacePaving);
            var placed = new List<float2>();
            foreach (var win in windows)
            {
                float mid = (win.x + win.y) * 0.5f;
                var c = shore.At(mid, out var inw, out _);
                // Never cut steps where they would meet the outline away from the water (no cliffs at the border).
                if (shore.EdgeDistance(c + inw * depth * 0.5f) < (win.y - win.x) * 0.5f + 4f) continue;
                float top = ctx.H(c + inw * (depth + 1f));
                float drop = math.isfinite(water) ? math.clamp(top - (water - 0.3f), levels * 0.3f, levels * 0.9f) : levels * 0.35f;
                float bottom = top - drop;
                for (int k = 0; k < levels; k++)
                    foreach (var ring in shore.Band(k * StepRun, (k + 1) * StepRun, win.x, win.y))
                        ctx.Plan.Terraform.Add(new PlannedTerraform
                        {
                            Ring = Poly.To3D(ring, ctx.H), Kind = TerraformKind.Level, TargetHeight = bottom + drop * k / levels, Permanent = true,
                        });
                if (paved)
                    foreach (var ring in shore.Band(0f, depth, win.x, win.y)) ctx.Surface(ring, SurfaceRole.Paving, AssetMix.One(""), AssetCategories.SurfacePaving);
                // Keep the steps clear of furniture.
                for (float s = win.x; s < win.y; s += 3f)
                {
                    var a = shore.At(s, out var ia, out _); var b = shore.At(math.min(win.y, s + 3f), out var ib, out _);
                    w.Occ.TakeCorridor(a + ia * depth * 0.5f, b + ib * depth * 0.5f, depth * 0.5f, 6);
                }
                w.Gaps.Add(win);
                w.Steps++;
            }
            if (w.Steps > 0) ShoreTerrain.Warn(ctx);
        }

        // ───────────── ground, promenade, piers ─────────────

        private static void Ground(Work w)
        {
            var ctx = w.Ctx; var shore = w.Shore;
            switch (ctx.S("surface"))
            {
                case "paved":
                    if (AssetAvailability.Require(ctx.Plan, AssetCategories.SurfacePaving, new[] { ctx.S("surfaceAsset") }, Keys.Category(AssetCategories.SurfacePaving)))
                        foreach (var ring in shore.Band(0f, w.P)) ctx.Surface(ring, SurfaceRole.Paving, AssetMix.One(ctx.S("surfaceAsset")), AssetCategories.SurfacePaving);
                    break;
                case "gravel":
                    if (AssetAvailability.Require(ctx.Plan, AssetCategories.SurfaceNatural, null, Keys.Category(AssetCategories.SurfaceNatural)))
                        foreach (var ring in shore.Band(0f, w.P)) ctx.Surface(ring, SurfaceRole.Ground, AssetMix.One(""), AssetCategories.SurfaceNatural);
                    break;
                case "grass":
                    foreach (var ring in shore.Band(0f, w.P)) ctx.Surface(ring, SurfaceRole.Grass, AssetMix.One(""));
                    break;
            }
            if (ctx.S("backdrop") != "none")
                foreach (var ring in shore.Band(w.P + 0.3f, shore.MaxDepth + 20f)) ctx.Surface(ring, SurfaceRole.Grass, AssetMix.One(""));
        }

        private void Promenade(Work w, NetRole role)
        {
            var ctx = w.Ctx; var shore = w.Shore; var occ = w.Occ;
            var asset = AssetMix.One(ctx.S("pathAsset"));
            var spine = new List<(float2, float2)>();
            foreach (var line in shore.Offset(w.DPath, w.Hw + 0.5f, 16f))
            {
                var curves = Shoreline.Curves(shore.Land, line, role);
                if (curves.Count == 0) continue;
                ParkPaths.Index(curves, occ.Corridors, 100);
                ParkPaths.Index(curves, occ.Paths, 100);
                ParkPaths.Emit(ctx, curves, NoGates, asset);
                foreach (var c in curves)
                    for (int i = 1; i < c.Samples.Length; i++) spine.Add((c.Samples[i - 1], c.Samples[i]));
                w.Lines.Add(line);
            }
            var all = Gates(ctx);
            var gates = new List<Gate>();
            foreach (var g in all) if (shore.Depth(g.Position) > w.P * 0.5f) gates.Add(g);
            if (gates.Count == 0) gates = all;
            if (spine.Count > 0) ConnectGates(ctx, occ, gates, spine, NetRole.Pathway, asset);
            else NaturalPaths(ctx, occ, gates, 0, 0f, 30f, "narrow", asset);
        }

        private static void Piers(Work w)
        {
            var ctx = w.Ctx; var shore = w.Shore;
            int wanted = ctx.I("piers");
            if (wanted <= 0) return;
            if (shore.Mode != ShoreMode.Inside)
            {
                ctx.Issue(IssueSeverity.Info, "Placemaker.Waterfront.Issue.PiersNeedWater");
                return;
            }
            var asset = AssetMix.One(ctx.S("pathAsset"));
            float maxLen = ctx.F("pierLength");
            bool heads = ctx.B("pierHeads");
            float deckAbove = 1.2f;
            var stations = shore.Stations(wanted, 10f);
            foreach (float s in stations)
            {
                float at = s;
                if (w.InGap(at, 4f)) continue;
                var p0 = shore.At(at, out var inw, out var tan);
                var dir = -inw;
                // Out over the water while it stays inside the outline (the traced waterline may sit a
                // metre or two off the real one, so the first few metres may still be dry).
                float len = 0f; bool wet = false;
                for (float l = 2f; l <= maxLen; l += 2f)
                {
                    var q = p0 + dir * l;
                    bool isWet = shore.IsWet(q);
                    if (!w.Shore.Outline.ContainsDisc(q, 2f) || (!isWet && l > 5f)) break;
                    wet |= isWet;
                    len = l;
                }
                if (len < 8f || !wet) continue;
                var end = p0 + dir * len;
                float deck = math.max(ctx.H(p0), (math.isfinite(shore.WaterLevel) ? shore.WaterLevel : ctx.H(p0)) + deckAbove);
                // Land stub from the promenade walk to the pier root.
                var walk = p0 + inw * w.DPath;
                if (w.Shore.Land.Contains(walk)) LeisureKit.Path(ctx, w.Occ, walk, p0 + inw * 0.3f, NetRole.Pathway, asset);
                Deck(ctx, p0 + inw * 0.3f, end, deck, asset);
                float total = len;
                if (heads)
                    foreach (float sgn in new[] { 1f, -1f })
                    {
                        var h = end + tan * sgn * 7f;
                        if (w.Shore.Outline.ContainsDisc(h, 1.5f) && shore.IsWet(h)) { Deck(ctx, end, h, deck, asset); total += 7f; }
                    }
                w.Gaps.Add(new float2(at - 2.5f, at + 2.5f));
                w.Piers++;
                w.PierLength += total;
            }
        }

        /// <summary>A pier course over the water: level deck, not following the seabed.</summary>
        private static void Deck(LayerContext ctx, float2 a, float2 b, float y, AssetRef asset)
        {
            ctx.Plan.Nets.Add(new PlannedNet
            {
                A = new float3(a.x, y, a.y), B = new float3(math.lerp(a, b, 1f / 3f).x, y, math.lerp(a, b, 1f / 3f).y),
                C = new float3(math.lerp(a, b, 2f / 3f).x, y, math.lerp(a, b, 2f / 3f).y), D = new float3(b.x, y, b.y),
                Role = NetRole.Pathway, Asset = asset, FollowTerrain = false,
            });
        }

        // ───────────── edge and water side ─────────────

        private static void Edge(Work w)
        {
            var ctx = w.Ctx; var shore = w.Shore; var occ = w.Occ;
            if (ctx.B("railings"))
            {
                var asset = AssetMix.One(ctx.S("railingAsset"));
                var run = new List<float2>();
                void Flush()
                {
                    if (run.Count >= 2)
                    {
                        var simple = Poly.SimplifyPolyline(run, 0.25f);
                        for (int i = 1; i < simple.Length; i++)
                        {
                            var a = simple[i - 1]; var b = simple[i];
                            float l = math.distance(a, b);
                            int pieces = math.max(1, (int)math.ceil(l / Fences.MaxCourse));
                            for (int k = 0; k < pieces; k++)
                            {
                                var p = math.lerp(a, b, (float)k / pieces); var q = math.lerp(a, b, (float)(k + 1) / pieces);
                                if (math.distance(p, q) < 1f) continue;
                                ctx.StraightNet(p, q, NetRole.Fence, asset);
                                occ.TakeCorridor(p, q, 0.3f, 5);
                                w.Railings++;
                            }
                        }
                    }
                    run.Clear();
                }
                for (float s = 0.5f; s <= shore.Length - 0.5f; s += 2f)
                {
                    var c = shore.At(s, out var inw, out _);
                    var q = c + inw * 0.7f;
                    bool ok = !w.InGap(s, 0.5f) && shore.Land.ContainsDisc(q, 0.35f) && shore.Depth(q, 2f) > 0.5f;
                    if (ok && run.Count > 0 && shore.Land.CrossesBoundary(run[run.Count - 1], q, false)) Flush();
                    if (ok) run.Add(q); else Flush();
                }
                Flush();
            }
            if (ctx.B("moorings"))
            {
                string cat = LeisureKit.Category(ctx, "", AssetCategories.WaterfrontMooring, AssetCategories.Bollard);
                if (cat != null)
                    for (float s = 3f; s < shore.Length - 3f; s += ctx.F("mooringSpacing"))
                    {
                        if (w.InGap(s, 1.5f)) continue;
                        var c = shore.At(s, out var inw, out _);
                        if (LeisureKit.Put(ctx, occ, c + inw * 0.9f, Yaw(-inw), ObjectRole.Furniture, AssetMix.One(""), cat, 0.4f, Hashing.Mix(ctx.Seed, 0x3009u + (uint)w.Moorings), 0.4f, 0.1f))
                            w.Moorings++;
                    }
            }
            if (ctx.B("riprap") && AssetAvailability.Require(ctx.Plan, AssetCategories.Rock, null, Keys.Category(AssetCategories.Rock)))
                for (float s = 1.5f; s < shore.Length - 1.5f; s += 2.2f)
                {
                    if (w.InGap(s, 1.5f)) continue;
                    uint v = Hashing.Mix(ctx.Seed, 0x21F0u + (uint)(s * 10f));
                    var c = shore.At(s + (Hashing.Unit(v) - 0.5f) * 1.2f, out var inw, out _);
                    float scale = 0.6f + 0.7f * Hashing.Unit(Hashing.Mix(v, 1u));
                    var q = c + inw * (0.6f + Hashing.Unit(Hashing.Mix(v, 2u)) * 1.2f);
                    if (!occ.Free(q, 0.7f * scale, 0.3f, 0.05f)) continue;
                    var o = ctx.Object(q, Hashing.Unit(Hashing.Mix(v, 3u)) * 6.283f, ObjectRole.Furniture, AssetMix.One(""), v, -1, AssetCategories.Rock);
                    o.Scale = scale;
                    occ.Take(q, 0.7f * scale, 2);
                    w.Rocks++;
                }
        }

        private static void WaterSide(Work w)
        {
            var ctx = w.Ctx; var shore = w.Shore; var occ = w.Occ;
            bool wide = w.DPath - w.Hw >= 4.05f;   // room for benches between the edge and the walk
            var bench = AssetMix.One(ctx.S("benchAsset"));
            var lamp = AssetMix.One(ctx.S("lampAsset"));
            float benchSpacing = ctx.F("benchSpacing"), lampSpacing = ctx.F("lampSpacing");
            if (benchSpacing > 0f)
                for (float s = benchSpacing * 0.5f; s < shore.Length; s += benchSpacing)
                {
                    bool gap = w.InGap(s, 3.5f);   // steps or pier root: only the land side of the walk
                    var c = shore.At(s, out var inw, out var tan);
                    // Between the edge and the walk when there is room, else on the land side of the walk (still facing the water).
                    var land = c + inw * (w.DPath + w.Hw + 1.6f);
                    uint v = Hashing.Mix(ctx.Seed, 0xBE70u + (uint)w.Benches);
                    if (!(wide && !gap && LeisureKit.Put(ctx, occ, c + inw * 2.6f, Yaw(-inw), ObjectRole.Bench, bench, null, 1.3f, v, 0.4f, 0.2f)) &&
                        !LeisureKit.Put(ctx, occ, land, Yaw(-inw), ObjectRole.Bench, bench, null, 1.3f, v, 0.4f, 0.2f)) continue;
                    w.Benches++;
                    if (w.Benches % 3 == 0)
                        LeisureKit.Put(ctx, occ, c + inw * (w.DPath + w.Hw + 1f) + tan * 1.5f, 0f, ObjectRole.Bin, AssetMix.One(""), null, 0.5f, Hashing.Mix(v, 1u), 0.4f, 0.2f);
                }
            if (lampSpacing > 0f)
                for (float s = lampSpacing * 0.25f; s < shore.Length; s += lampSpacing)
                {
                    bool gap = w.InGap(s, 2.5f);
                    var c = shore.At(s, out var inw, out _);
                    var q = !gap && w.DPath - w.Hw >= 2.5f ? c + inw * 1.8f : c + inw * (w.DPath + w.Hw + 0.9f);
                    if (LeisureKit.Put(ctx, occ, q, Yaw(-inw), ObjectRole.Lamp, lamp, null, 0.5f, Hashing.Mix(ctx.Seed, 0x1A70u + (uint)w.Lamps), 0.3f, 0.2f))
                        w.Lamps++;
                }
            if (ctx.B("nautical"))
            {
                string cat = LeisureKit.Category(ctx, "", AssetCategories.WaterfrontNautical, AssetCategories.Decoration);
                if (cat != null)
                    for (float s = 18f; s < shore.Length - 6f; s += 45f)
                    {
                        if (w.InGap(s, 3f)) continue;
                        var c = shore.At(s, out var inw, out var tan);
                        // A life ring by the edge, an anchor and crates on the land side of the walk.
                        LeisureKit.Put(ctx, occ, c + inw * 1.6f, Yaw(-inw), ObjectRole.Furniture, AssetMix.One(""), cat, 0.5f, Hashing.Mix(ctx.Seed, 0xA9C0u + (uint)(s * 3f)), 0.3f, 0.15f);
                        var land = c + inw * (w.DPath + w.Hw + 2.5f);
                        LeisureKit.Put(ctx, occ, land, Yaw(tan), ObjectRole.Furniture, AssetMix.One(""), cat, 1.4f, Hashing.Mix(ctx.Seed, 0xA9D0u + (uint)(s * 3f)), 0.5f, 0.2f);
                        if (AssetAvailability.Has(AssetCategories.Decoration))
                            LeisureKit.Put(ctx, occ, land + tan * 3f, Yaw(-inw), ObjectRole.Furniture, AssetMix.One(""), AssetCategories.Decoration, 0.9f, Hashing.Mix(ctx.Seed, 0xA9E0u + (uint)(s * 3f)), 0.5f, 0.2f);
                    }
            }
        }

        private static void FishingSpots(Work w)
        {
            var ctx = w.Ctx; var shore = w.Shore; var occ = w.Occ;
            if (!ctx.B("fishing")) return;
            string cat = LeisureKit.Category(ctx, "", AssetCategories.WaterfrontFishing);
            float spacing = ctx.F("fishingSpacing");
            for (float s = spacing * 0.75f; s < shore.Length - 2f; s += spacing)
            {
                if (w.InGap(s, 3.5f)) continue;
                var c = shore.At(s, out var inw, out var tan);
                uint v = Hashing.Mix(ctx.Seed, 0xF150u + (uint)w.Fishing);
                bool any = false;
                // The rod at the edge, a seat beside it facing the water, a bin every other spot.
                if (cat != null) any |= LeisureKit.Put(ctx, occ, c + inw * 1.5f + tan * 0.9f, Yaw(-inw), ObjectRole.Furniture, AssetMix.One(""), cat, 0.6f, v, 0.3f, 0.1f);
                any |= LeisureKit.Put(ctx, occ, c + inw * 1.7f - tan * 1.1f, Yaw(-inw), ObjectRole.Bench, AssetMix.One(ctx.S("benchAsset")), null, 1.1f, Hashing.Mix(v, 1u), 0.3f, 0.1f);
                if ((w.Fishing & 1) == 0) LeisureKit.Put(ctx, occ, c + inw * 1.6f + tan * 2.9f, 0f, ObjectRole.Bin, AssetMix.One(""), null, 0.5f, Hashing.Mix(v, 2u), 0.3f, 0.1f);
                if (!any) continue;
                w.Fishing++;
                w.Gaps.Add(new float2(s - 2.5f, s + 3.8f));   // open edge: no railing or riprap in front of the angler
            }
        }

        // ───────────── land side ─────────────

        private static void LandSide(Work w)
        {
            var ctx = w.Ctx; var shore = w.Shore; var occ = w.Occ;
            float dLand = math.max(w.P, w.DPath + w.Hw + 0.5f);
            int kiosks = ctx.I("kiosks");
            if (kiosks > 0)
            {
                string cat = LeisureKit.Category(ctx, "", AssetCategories.Kiosk);
                string seats = AssetAvailability.Has(AssetCategories.CafeSeating) ? AssetCategories.CafeSeating : null;
                if (cat != null)
                    foreach (float s in shore.Stations(kiosks, 10f, 0.5f))
                    {
                        foreach (float off in new[] { 0f, 8f, -8f, 16f, -16f })
                        {
                            var c = shore.At(s + off, out var inw, out var tan);
                            var k = c + inw * (dLand + 4f);
                            uint v = Hashing.Mix(ctx.Seed, 0x6170u + (uint)w.Kiosks);
                            if (!LeisureKit.Put(ctx, occ, k, Yaw(-inw), ObjectRole.Furniture, AssetMix.One(""), cat, 2.5f, v, 0.8f, 0.3f)) continue;
                            w.Kiosks++;
                            if (seats != null)
                                for (int t = 0; t < 4; t++)
                                {
                                    var q = k - inw * 3.6f + tan * ((t - 1.5f) * 2.3f);
                                    LeisureKit.Put(ctx, occ, q, Yaw(-inw), ObjectRole.Furniture, AssetMix.One(""), seats, 0.9f, Hashing.Mix(v, (uint)t + 1u), 0.4f, 0.15f);
                                }
                            break;
                        }
                    }
            }
            string trees = ctx.S("trees");
            var ids = ctx.L("treeSpecies");
            if (trees != "none" && (ids.Length > 0 || AssetAvailability.Has(AssetCategories.Tree)))
            {
                float spacing = ctx.F("treeSpacing");
                int rows = trees == "double" ? 2 : 1;
                int n = 0;
                for (int r = 0; r < rows; r++)
                {
                    float d = dLand + 3f + r * 6f;   // crown (2.4) + gap clear of the walk edge
                    foreach (var line in shore.Offset(d, 2.5f, 8f, 2f))
                    {
                        float len = Shoreline.LengthOf(line);
                        for (float s = (r == 0 ? 0.5f : 1f) * spacing; s < len; s += spacing)
                        {
                            var p = Shoreline.PointOn(line, s, out _);
                            uint v = Hashing.Mix(ctx.Seed, 0x7EE0u + (uint)n);
                            if (LeisureKit.Put(ctx, occ, p, Hashing.Unit(v) * 6.283f, ObjectRole.Tree, AssetMix.Pick(ids, v), null, 2.4f, v, 1f, 0.3f, 3, 2 + (int)(v % 2u)))
                                n++;
                        }
                    }
                }
            }
        }

        private static void Backdrop(Work w)
        {
            var ctx = w.Ctx; var shore = w.Shore;
            if (ctx.S("backdrop") != "park") return;
            var ids = ctx.L("treeSpecies");
            if (ids.Length == 0 && !AssetAvailability.Has(AssetCategories.Tree)) return;
            float from = math.max(w.P, w.DPath + w.Hw) + 14f;
            foreach (var ring in shore.Band(from, shore.MaxDepth + 20f))
            {
                var band = LayerRegion.From(ring);
                if (band == null) continue;
                LeisureKit.Plant(ctx, w.Occ, band, (int)(band.Area / 10000f * 30f), ObjectRole.Tree, ids, 2.5f, 24f, 0.25f, 0x7EF0u, null, 1, 3);
                LeisureKit.Plant(ctx, w.Occ, band, (int)(band.Area / 10000f * 40f), ObjectRole.Bush, null, 1f, 14f, 0.2f, 0xB0F0u, null);
            }
        }
    }
}
