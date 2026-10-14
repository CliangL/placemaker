// Waterfront (s20 redesign): a designed edge to the water, read off the real terrain first.
//
//   survey      ShoreGround: the real waterline (terrain below the water level next to water), bank kind along
//               the shore (low / bank / cliff) and slope classes. Cliff stretches stay natural and get a cliff-top
//               walk with lookouts; a shore that is mostly cliff says so (Design issue) and builds only that.
//   edge        what meets the water, by preset: a WIDE QUAY (harbour: 8 m pedestrian quay along the waterline, deck
//               above the water, the land behind filled to deck level so the wall stands in the water), a WALKWAY QUAY
//               (3 m, a crisp low edge), a timber BOARDWALK just off the bank on piles with short stubs to land, or a
//               natural BANK graded into the water with riprap
//   embankment  one smooth graded slope (≤ ~25 %) from the edge up to the promenade; a graded bank behind it back to
//               the original ground. Sculpted (sloped patches), never terraced; cut/fill limited
//   promenade   ONE walk parallel to the water on a level bench (smoothed, ≤ 6 % along it), paved terrace either side;
//               benches on the water side facing the water in rhythm, lamps on the land side between them, bins
//   piers       start from a paved landing on the promenade, a graded ramp down to the edge, then the deck out over
//               the water (T-heads optional); river steps are paved flights cut down to the walkway quay
//   planting    a tree row or avenue behind the promenade, planted beds in the bays between lamps, a park behind
//   fences      only where the ground beside a walk really drops (> 1 m within 3 m); quays bring their own railing
// Everything stands on the final ground (Resnap); furniture never on slopes steeper than 12 %.

using System;
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
        /// <summary>(s20) What meets the water: automatic (by style), wide quay, walkway quay, boardwalk, natural bank.</summary>
        public static readonly string[] Edges = { "auto", "quay", "walkway", "boardwalk", "bank" };
        private static readonly List<Gate> NoGates = new List<Gate>();
        private const float StepRun = 1.5f;
        /// <summary>An edge element may sit this far past the drawn outline to reach the real water (m).</summary>
        public const float EdgeReach = 8f;

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
            // Area-wide features run on the dry land only ("core"): on the whole outline they planted picnic
            // shelters and trees in the water and puddle decals on it (art pass a3).
            Feature("restrooms"), Feature("shops", ("count", 2.0), ("where", "paths")), StreetShopsFeature(Area),
            CoreFeature("playground", ("size", "small")), CoreFeature("picnic"), CoreFeature("fountain"), CoreFeature("flowerbeds"),
            CoreFeature("bigtrees"), CoreFeature("decals", ("theme", "puddles")),
        };

        /// <summary>
        /// None: the layer plants its own beds and trees. The flower-bed feature scattered planter rows across the lawn
        /// (playtest 29) and big trees broke the avenue; both stay available as opt-in features.
        /// </summary>
        protected override string[] DefaultFeatures => new string[0];

        protected override void DefineControls(ControlSet c)
        {
            c.Choice("style", "promenade", ControlTier.Essential, "waterfront", Styles).Tip();
            c.Choice("waterSide", "auto", ControlTier.Essential, "waterfront", Shoreline.Sides).Tip();
            c.Slider("promWidth", 12, 6, 30, 1, "m", ControlTier.Essential, "waterfront").Tip();
            c.Toggle("railings", false, ControlTier.Essential, "waterfront").Tip();
            c.Choice("edge", "auto", ControlTier.Essential, "waterfront", Edges).Tip();   // s20

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
            c.Choice("shoreTexture", "auto", ControlTier.More, "edge", "auto", "sand", "dirt", "none").Tip();

            c.Choice("steps", "none", ControlTier.More, "steps", "none", "spots", "continuous").Tip();
            c.Slider("stepCount", 2, 1, 8, 1, "count", ControlTier.More, "steps").When("steps=spots");
            c.Slider("stepWidth", 10, 4, 40, 1, "m", ControlTier.More, "steps").When("steps=spots");
            c.Slider("stepLevels", 3, 2, 6, 1, "count", ControlTier.More, "steps").Tip();

            c.Slider("piers", 1, 0, 6, 1, "count", ControlTier.More, "piers").Tip();
            c.Slider("pierLength", 30, 8, 120, 2, "m", ControlTier.More, "piers");
            c.Toggle("pierHeads", false, ControlTier.More, "piers").Tip();

            c.Toggle("fishing", false, ControlTier.More, "harbor").Tip();
            c.Slider("fishingSpacing", 20, 8, 60, 2, "m", ControlTier.More, "harbor").Layout(5).When("fishing=true");
            c.Toggle("nautical", false, ControlTier.More, "harbor").Tip();

            c.Choice("trees", "row", ControlTier.More, "planting", "none", "row", "double").Tip();
            c.Slider("treeSpacing", 12, 6, 30, 1, "m", ControlTier.More, "planting").Layout(6);
            c.Choice("backdrop", "park", ControlTier.More, "planting", "lawn", "park", "none").Tip();
            c.AssetMulti("treeSpecies", AssetCategories.Tree, ControlTier.More, "planting");

            c.Asset("pathAsset", AssetCategories.Pathway, ControlTier.More, "assets");
            c.Asset("benchAsset", AssetCategories.Bench, ControlTier.More, "assets");
            c.Asset("lampAsset", AssetCategories.Lamp, ControlTier.More, "assets");
            EntranceMarkers(c);
            DefineStreetShopControls(c);
            // s20 (appended): the edge element, its height and assets, planted beds.
            c.Slider("deckHeight", 1.5, 0.6, 3, 0.1, "m", ControlTier.More, "edge").Tip();
            c.Asset("quayAsset", AssetCategories.NetQuay, ControlTier.More, "edge");
            c.Asset("boardwalkAsset", AssetCategories.NetPier, ControlTier.More, "edge");
            c.Toggle("beds", true, ControlTier.More, "planting").Tip();

            c.Fixed("waterSide");
            c.Fixed("edge");
            c.RandomRange("promWidth", 10, 18);
            c.RandomRange("piers", 0, 3);
            c.Chance("moorings", 0.3f);
            c.Chance("riprap", 0.2f);
            c.Chance("fishing", 0.4f);
            c.Chance("nautical", 0.4f);
        }

        protected override void DefinePresets()
        {
            // Preset ids are save format; labels and values changed in s20 (see Lang).
            // Promenade boardwalk: a timber boardwalk just off the bank, a lawn embankment up to a paved promenade
            // with benches facing the water, lamps between them, planted beds and a tree row, a park behind.
            Preset("promenade", ("style", "promenade"), ("edge", "boardwalk"), ("promWidth", 14.0), ("surface", "paved"), ("steps", "none"), ("trees", "row"),
                ("piers", 0.0), ("backdrop", "park"), ("benchSpacing", 14.0), ("lampSpacing", 21.0), ("beds", true), ("railings", false), ("deckHeight", 1.0));
            // Pier park: a walkway quay along the water, a promenade above a lawn bank, piers with T-heads starting
            // from paved landings on the promenade.
            Preset("pier", ("style", "pier"), ("edge", "walkway"), ("promWidth", 12.0), ("pathWidth", "wide"), ("surface", "paved"), ("steps", "none"), ("piers", 2.0),
                ("pierLength", 50.0), ("pierHeads", true), ("nautical", true), ("trees", "row"), ("benchSpacing", 12.0), ("backdrop", "park"), ("railings", false));
            // Harbour quay: a wide quay at the water, a paved apron at deck level with kiosks, lamps and benches, an avenue behind.
            Preset("harbor", ("style", "harbor"), ("edge", "quay"), ("promWidth", 22.0), ("railings", false), ("moorings", false), ("nautical", true), ("kiosks", 3.0),
                ("lampSpacing", 16.0), ("trees", "row"), ("treeSpacing", 14.0), ("piers", 1.0), ("steps", "none"), ("backdrop", "lawn"), ("deckHeight", 1.8));
            // River steps: a low walkway quay along the river, paved step flights down to it from a tree-lined promenade.
            Preset("riverSteps", ("style", "riverSteps"), ("edge", "walkway"), ("promWidth", 16.0), ("railings", false), ("steps", "spots"), ("stepCount", 3.0),
                ("stepWidth", 10.0), ("stepLevels", 4.0), ("trees", "double"), ("backdrop", "park"), ("lampSpacing", 22.0), ("piers", 0.0), ("deckHeight", 0.9));
            // Natural bank: no hard edge, a soft bank graded into the water with riprap, fishing spots on small level
            // landings at the water, a gravel walk set back.
            Preset("fishing", ("style", "fishing"), ("edge", "bank"), ("promWidth", 10.0), ("surface", "gravel"), ("pathWidth", "narrow"), ("railings", false),
                ("riprap", true), ("fishing", true), ("fishingSpacing", 16.0), ("lampSpacing", 0.0), ("benchSpacing", 0.0), ("trees", "none"), ("backdrop", "park"),
                ("kiosks", 0.0), ("steps", "none"), ("piers", 1.0), ("pierLength", 16.0));
        }

        protected override int WantedEntrances(LayerRegion region) => math.min(4, Entrances.WantedByArea(region.Area));

        // ───────────── planning ─────────────

        private sealed class Work
        {
            public LayerContext Ctx;
            public Shoreline Shore;
            public ShoreGround G;
            public Occupancy Occ;
            public string Style, Edge;
            /// <summary>Distances from the real waterline: edge element centre and its land side, the promenade centre.</summary>
            public float EdgeDw, EdgeIn, DwP, Hw, P, EdgeHw;
            /// <summary>Deck height of the edge (absolute), and whether there is water to measure from.</summary>
            public float Deck;
            public bool Water;
            public Func<float, float> Hp;   // planned promenade height by arc length
            public List<float2> Runs = new List<float2>(), CliffRuns = new List<float2>();
            public readonly List<float2> Gaps = new List<float2>();   // along-shore intervals kept open at the edge
            public readonly List<float2[]> Lines = new List<float2[]>();
            public readonly List<Func<float, float>> Profiles = new List<Func<float, float>>();
            public readonly List<float2[]> EdgeLines = new List<float2[]>();
            public readonly List<(float2 a, float2 b, float2 c, float2 d)> EdgePieces = new List<(float2, float2, float2, float2)>();
            public readonly List<List<PathCurve>> PromCurves = new List<List<PathCurve>>();
            public readonly List<float2[]> Pads = new List<float2[]>();
            public readonly List<(float2[] ring, float h)> Levels = new List<(float2[], float)>();
            public int Steps, Piers, Benches, Lamps, Moorings, Fishing, Kiosks, Rocks, Railings, Beds, Lookouts, EdgeNets;
            public float PierLength, EdgeLength;

            public bool InGap(float s, float pad = 0f)
            {
                foreach (var g in Gaps) if (s >= g.x - pad && s <= g.y + pad) return true;
                return false;
            }

            public bool InRun(float s)
            {
                foreach (var r in Runs) if (s >= r.x - 1f && s <= r.y + 1f) return true;
                return false;
            }
        }

        private static string EdgeOf(LayerContext ctx)
        {
            string e = ctx.S("edge");
            if (!string.IsNullOrEmpty(e) && e != "auto") return e;
            switch (ctx.S("style"))
            {
                case "harbor": return "quay";
                case "promenade": return "boardwalk";
                case "pier": case "riverSteps": return "walkway";
                default: return "bank";
            }
        }

        protected override void PlanCore(LayerContext ctx)
        {
            var shore = Shoreline.Analyze(ctx, ctx.LayerRegion, ctx.S("waterSide"));
            if (shore == null) { ctx.Issue(IssueSeverity.Warning, Keys.NoSpace, NameKey); return; }
            ctx.LayerRegion = shore.Land;
            var w = new Work { Ctx = ctx, Shore = shore, Style = ctx.S("style"), Edge = EdgeOf(ctx) };
            w.Water = shore.Mode != ShoreMode.Assumed && shore.HasWaterline;
            if (!w.Water && w.Edge != "bank") w.Edge = "bank";   // no water found: nothing for a quay or boardwalk to face
            var g = w.G = ShoreGround.Create(ctx, shore, w.Edge == "bank" ? 10f : w.Edge == "boardwalk" ? 4f : EdgeReach);
            w.Occ = new Occupancy(shore.Land, ctx.Input.Reserved, ctx.Site);
            w.Occ.GroundOk = (p, r) => g.CanStand(p, r);
            var role = ctx.S("pathWidth") == "narrow" ? NetRole.Pathway : NetRole.PathwayWide;
            w.Hw = Composer.DefaultHalfWidth(role);
            float landDepth = shore.MaxDepth + shore.GapAt(shore.Length * 0.5f);
            w.Runs = g.Runs(k => k != ShoreKind.Cliff, 12f);
            w.CliffRuns = g.Runs(k => k == ShoreKind.Cliff, 16f);
            float level = g.HasLevel ? g.Level : float.NaN;
            switch (w.Edge)
            {
                case "quay": w.EdgeHw = 4f; w.EdgeDw = 3.6f; break;
                case "walkway": w.EdgeHw = 1.5f; w.EdgeDw = 1.3f; break;
                case "boardwalk": w.EdgeHw = 1.5f; w.EdgeDw = -2.6f; break;
                default: w.EdgeHw = 0f; w.EdgeDw = 0f; break;
            }
            w.EdgeIn = w.Edge == "boardwalk" || w.Edge == "bank" ? 0f : w.EdgeDw + w.EdgeHw;
            float water0 = math.isfinite(level) ? level : g.Ref(shore.Length * 0.5f);
            w.Deck = water0 + (w.Edge == "bank" ? 0.6f : ctx.F("deckHeight"));
            if (w.Edge == "walkway" || w.Edge == "boardwalk")
            {
                // A walkway or boardwalk sits at the bank's own level (≥ 0.8 m above the water, ≤ Deck height): on a low shore it
                // is not a dike the promenade has to climb over.
                var gs = new List<float>();
                for (float s = 0f; s <= shore.Length; s += 8f) { var wp = shore.WaterAt(s, out var inw); gs.Add(ctx.H(wp + inw * math.max(2f, w.EdgeIn + 1f))); }
                gs.Sort();
                if (gs.Count > 0) w.Deck = math.clamp(gs[gs.Count / 2] + 0.15f, water0 + 0.8f, w.Deck);
            }
            w.P = math.max(ctx.F("promWidth"), w.Hw * 2f + 3f);
            // Harbour: the quay is the walk; the promenade runs along the back of the apron. Others: the promenade sits
            // above the embankment, its sea-side edge promWidth from the water.
            w.DwP = w.Edge == "quay" ? w.EdgeIn + w.P - w.Hw - 1.5f : math.max(w.EdgeIn, 0f) + w.P;
            w.DwP = math.min(w.DwP, math.max(w.EdgeIn + w.Hw + 2f, landDepth - w.Hw - 3f));
            ctx.Plan.Facts["promenadeWidth"] = w.P;
            ctx.Plan.Facts["edgeKind"] = Array.IndexOf(Edges, w.Edge);

            bool allCliff = shore.CliffShare >= ShoreGround.CliffShoreShare || w.Runs.Count == 0;
            if (shore.CliffShare > 0.05f)
                ctx.Issue(allCliff ? IssueSeverity.Warning : IssueSeverity.Info, ShoreGround.CliffKey, (int)math.round(shore.CliffShare * 100f));

            if (!allCliff)
            {
                Terrain(w);
                ctx.Check();
                EdgeElement(w);
                Promenade(w, role);
                ctx.Check();
                Piers(w);
                Steps(w);
                if (w.Steps == 0) EdgeLinks(w);   // river steps are the links down to the walkway
                FishingSpots(w);
                ctx.Check();
            }
            CliffWalks(w);
            ConnectEntrances(w);
            if (!allCliff)
            {
                Ground(w);
                EdgeDressing(w);
                PromenadeFurniture(w);
                ctx.Check();
                LandSide(w);
            }
            Backdrop(w);

            g.Finish(4.5f, 2.5f, 0.5f);
            // Step flights are deliberate levels: emitted after the sculpt so they win over it.
            foreach (var (ring, h) in w.Levels)
            {
                ctx.Plan.Terraform.Add(new PlannedTerraform { Ring = Poly.To3D(ring, _ => h), Kind = TerraformKind.Level, TargetHeight = h, Permanent = true });
                ctx.Ground.OverridePolygon(ring, h);
            }
            if (w.Levels.Count > 0) { Sculpt.Warn(ctx); Sculpt.Resnap(ctx); }

            BeachLayer.EmitGreenRegions(ctx, w.Occ, g);
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
            ctx.Plan.Facts["lookouts"] = w.Lookouts;
            ctx.Plan.Facts["edgeNets"] = w.EdgeNets;
            ctx.Plan.Facts["edgeLength"] = w.EdgeLength;
            ctx.Plan.Facts["beds"] = w.Beds;
            ctx.Plan.Facts["deck"] = w.Deck;
        }

        private static float Yaw(float2 dir) => math.atan2(dir.x, dir.y);

        // ───────────── terrain ─────────────

        /// <summary>
        /// The cross-section from the water: quays fill the land behind them to deck level; then one smooth embankment
        /// (smoothstep, never steeper than ~25 %) from the edge up to the promenade's level; the promenade bench; then a
        /// graded bank back to the original ground. A natural bank is graded into the water instead.
        /// </summary>
        private static void Terrain(Work w)
        {
            var ctx = w.Ctx; var shore = w.Shore; var g = w.G;
            float edgeTop = w.EdgeIn;                      // where the edge element's level ends on land
            float promFoot = w.DwP - w.Hw - 1.5f;          // the promenade terrace's sea-side edge
            float span = math.max(2f, promFoot - edgeTop);
            float baseLevel = w.Deck;
            // Promenade level per arc length: the original ground there, smoothed along the shore, kept between the
            // edge level and edge + 25 % of the embankment span (so the embankment never gets steeper than that).
            var stations = new List<(float s, float h)>();
            for (float s = 0f; s <= shore.Length; s += 6f)
            {
                var wp = shore.WaterAt(s, out var inw);
                float h = ctx.H(wp + inw * w.DwP);
                // A harbour apron is one level with the quay deck; elsewhere the promenade may sit up to a quarter of the span higher.
                // A natural bank only keeps the promenade above the water (and, with no water found, leaves it on the ground).
                // River steps: the promenade is an embankment at least 1.5 m above the walkway quay (something to step down).
                float low = w.Edge == "bank" ? (w.Water ? g.Ref(s) + 0.6f : float.MinValue) : baseLevel + (w.Style == "riverSteps" ? 1.5f : 0.1f);
                float high = (w.Edge == "bank" ? g.Ref(s) + 0.6f : baseLevel) + 0.25f * span + 0.3f;
                stations.Add((s, w.Edge == "quay" ? baseLevel + 0.05f : math.clamp(h, low, math.max(low, high))));
            }
            var smooth = new float[stations.Count];
            for (int i = 0; i < stations.Count; i++)
            {
                float sum = 0f, wsum = 0f;
                for (int j = 0; j < stations.Count; j++)
                {
                    float d = math.abs(stations[j].s - stations[i].s);
                    if (d > 30f) continue;
                    float k = 1f - d / 31f;
                    sum += stations[j].h * k; wsum += k;
                }
                smooth[i] = sum / wsum;
            }
            w.Hp = s =>
            {
                if (stations.Count == 1) return smooth[0];
                float f = math.clamp(s / 6f, 0f, stations.Count - 1.001f);
                int i = (int)f;
                return math.lerp(smooth[i], smooth[i + 1], f - i);
            };
            // Rise behind the promenade → width of the bank blending back to the original ground (≤ ~25 %).
            var rises = new List<float>();
            for (float s = 0f; s <= shore.Length; s += 8f)
            {
                var wp = shore.WaterAt(s, out var inw);
                rises.Add(math.abs(ctx.H(wp + inw * (w.DwP + w.Hw + 25f)) - w.Hp(s)));
            }
            rises.Sort();
            float rise = rises.Count > 0 ? rises[rises.Count / 2] : 0f;
            float bankW = math.clamp(rise / 0.22f, 8f, 45f);
            float behind = w.DwP + w.Hw + 1.5f;
            bool bank = w.Edge == "bank";
            float level = g.HasLevel ? g.Level : float.NaN;
            g.Profile((dw, along, h0, p) =>
            {
                float hp = w.Hp(along);
                if (bank)
                {
                    // Natural bank: graded from ~1.2 m under the water up to the promenade, never steeper than the span allows.
                    float r0 = g.Ref(along);
                    if (dw < 0f) return math.max(h0 - 0.8f, math.min(h0 + 0.8f, math.max(r0 - 1.4f, r0 + 0.12f * dw)));
                    if (dw <= promFoot) return math.lerp(r0 + 0.1f, hp, Sculpt.Smooth01(dw / math.max(1f, promFoot)));
                }
                else
                {
                    if (dw < edgeTop - 0.5f && w.Edge == "boardwalk") return float.NaN;   // the bed under a boardwalk stays
                    if (dw <= edgeTop) return dw < -0.5f ? float.NaN : baseLevel;      // quay fill to deck level (water side keeps its bed)
                    if (dw <= promFoot) return math.lerp(baseLevel, hp, Sculpt.Smooth01((dw - edgeTop) / span));
                }
                if (dw <= behind) return hp;
                float u = Sculpt.Smooth01((dw - behind) / bankW);
                return math.lerp(hp, h0, u);
            });
            g.Settle(4.5f, 2.5f, 0.5f, 8f);
        }

        // ───────────── edge element ─────────────

        /// <summary>
        /// Cubic Bézier pieces through a polyline (knots every ~<paramref name="knot"/> m, Catmull-Rom tangents), so a long
        /// edge is a handful of smooth segments sharing nodes, not a zig-zag of short ones.
        /// </summary>
        private static List<(float2 a, float2 b, float2 c, float2 d)> Beziers(float2[] line, float knot)
        {
            var result = new List<(float2, float2, float2, float2)>();
            if (line == null || line.Length < 2) return result;
            float avg = Shoreline.LengthOf(line) / math.max(1, line.Length - 1);
            int stride = math.max(1, (int)math.round(knot / math.max(0.5f, avg)));
            var knots = new List<int>();
            for (int i = 0; i < line.Length - 1; i += stride) knots.Add(i);
            if (line.Length - 1 - knots[knots.Count - 1] < stride / 2 && knots.Count > 1) knots[knots.Count - 1] = line.Length - 1;
            else knots.Add(line.Length - 1);
            float2 Tan(int i) => math.normalizesafe(line[math.min(line.Length - 1, i + 1)] - line[math.max(0, i - 1)], new float2(1, 0));
            for (int k = 0; k + 1 < knots.Count; k++)
            {
                var a = line[knots[k]]; var d = line[knots[k + 1]];
                float l = math.distance(a, d);
                if (l < 1f) continue;
                result.Add((a, a + Tan(knots[k]) * l / 3f, d - Tan(knots[k + 1]) * l / 3f, d));
            }
            return result;
        }

        private static void Deck(LayerContext ctx, (float2 a, float2 b, float2 c, float2 d) bz, float y, NetRole role, AssetRef asset, string category)
        {
            ctx.Plan.Nets.Add(new PlannedNet
            {
                A = new float3(bz.a.x, y, bz.a.y), B = new float3(bz.b.x, y, bz.b.y), C = new float3(bz.c.x, y, bz.c.y), D = new float3(bz.d.x, y, bz.d.y),
                Role = role, Asset = asset, FollowTerrain = false, Category = category,
            });
        }

        /// <summary>
        /// The quay / walkway quay / boardwalk as ONE smooth run per stretch of shore (Bézier pieces through the waterline
        /// offset), at a constant deck height. A quay must lie within the outline or at most EdgeReach m past the land
        /// (it is built at the water); a boardwalk only over water.
        /// </summary>
        private static void EdgeElement(Work w)
        {
            var ctx = w.Ctx; var shore = w.Shore; var g = w.G; var occ = w.Occ;
            if (w.Edge == "bank") return;
            bool board = w.Edge == "boardwalk";
            var role = w.Edge == "quay" ? NetRole.PathwayWide : NetRole.Pathway;
            string cat = board ? AssetCategories.NetPier : AssetCategories.NetQuay;
            var ids = board ? WaterfrontArt.Or(ctx.S("boardwalkAsset"), WaterfrontArt.Boardwalk)
                : WaterfrontArt.Or(ctx.S("quayAsset"), w.Edge == "quay" ? WaterfrontArt.QuayWide : WaterfrontArt.QuayNarrow);
            var asset = new AssetRef("", ids);
            foreach (var line in shore.OffsetFromWater(w.EdgeDw, -(EdgeReach + 2f), 16f, 3f, p =>
            {
                shore.Nearest(p, out _, out float a);
                if (!w.InRun(a) || g.OnCliff(p)) return true;
                if (board) return !shore.IsWet(p) || shore.IsWet(p) && !shore.Outline.Contains(p) && shore.Depth(p) > EdgeReach;
                return !shore.Outline.Contains(p) && shore.Depth(p) > EdgeReach;
            }))
            {
                var pieces = Beziers(line, 16f);
                if (pieces.Count == 0) continue;
                foreach (var bz in pieces) Deck(ctx, bz, w.Deck, role, asset, cat);
                w.EdgePieces.AddRange(pieces);
                w.EdgeNets += pieces.Count;
                w.EdgeLength += Shoreline.LengthOf(line);
                w.EdgeLines.Add(line);
                for (int i = 1; i < line.Length; i++)
                {
                    occ.TakeCorridor(line[i - 1], line[i], w.EdgeHw + 0.3f, 100);
                    if (!board) occ.Paths.Add(line[i - 1], line[i], w.EdgeHw, 100);
                }
            }
        }

        // ───────────── promenade ─────────────

        private void Promenade(Work w, NetRole role)
        {
            var ctx = w.Ctx; var shore = w.Shore; var occ = w.Occ; var g = w.G;
            var asset = AssetMix.One(ctx.S("pathAsset"));
            foreach (var raw in shore.OffsetFromWater(w.DwP, w.Hw + 1f, 16f, 3f, p =>
            {
                shore.Nearest(p, out _, out float a);
                return !w.InRun(a) || g.OnCliff(p);
            }))
            // ONE continuous walk on a level terrace: flat across (benches and lamps stand on it), ≤ 6 % along; it ends
            // where its level would have to climb to ground it may not change (the side borders, a cliff).
            foreach (var (line, profile) in g.Walk(raw, w.Hw + 2.4f, 4f, 18f, 0.06f, 0.8f, 16f))
            {
                var curves = Shoreline.Curves(shore.Land, line, role);
                if (curves.Count == 0) continue;
                ParkPaths.Index(curves, occ.Corridors, 100);
                ParkPaths.Index(curves, occ.Paths, 100);
                ParkPaths.Emit(ctx, curves, NoGates, asset);
                w.Lines.Add(line);
                w.Profiles.Add(profile);
                w.PromCurves.Add(curves);
            }
        }

        /// <summary>Cliff stretches: a walk on the cliff top with lookouts (the cliff face stays natural).</summary>
        private static void CliffWalks(Work w)
        {
            var ctx = w.Ctx;
            var bench = AssetMix.Pick(WaterfrontArt.Or(ctx.S("benchAsset"), WaterfrontArt.Benches), 0u);
            var lamp = AssetMix.Pick(WaterfrontArt.Or(ctx.S("lampAsset"), WaterfrontArt.Lamps), 0u);
            var rail = AssetMix.Pick(WaterfrontArt.Or(ctx.S("railingAsset"), WaterfrontArt.Railings), 0u);
            foreach (var run in w.CliffRuns)
            {
                var res = CliffTop.Plan(ctx, w.G, w.Occ, run.x, run.y, NetRole.Pathway, AssetMix.One(ctx.S("pathAsset")), bench, rail, lamp,
                    AssetMix.Pick(WaterfrontArt.Landing, 0u), 60f);
                w.Lookouts += res.Lookouts;
                w.Railings += res.Railings;
                w.Lines.AddRange(res.Lines);
                foreach (var _ in res.Lines) w.Profiles.Add(null);
            }
        }

        /// <summary>Entrances from the land side join the nearest walk on one constant-grade ramp each.</summary>
        private void ConnectEntrances(Work w)
        {
            var ctx = w.Ctx; var shore = w.Shore;
            var spine = new List<(float2, float2)>();
            foreach (var line in w.Lines)
                for (int i = 1; i < line.Length; i++) spine.Add((line[i - 1], line[i]));
            if (spine.Count == 0)
                foreach (var line in w.EdgeLines)
                    if (w.Edge != "boardwalk")
                        for (int i = 1; i < line.Length; i++) spine.Add((line[i - 1], line[i]));
            if (spine.Count == 0) return;
            var asset = AssetMix.One(ctx.S("pathAsset"));
            foreach (var gt in Gates(ctx))
                if (shore.FromWater(gt.Position) > w.DwP * 0.5f) w.G.Connect(w.Occ, gt.Position, spine, NetRole.Pathway, asset);
        }

        /// <summary>Closest point on a set of Bézier pieces (16 samples each, then refined on the best span).</summary>
        private static float2 ClosestOn(List<(float2 a, float2 b, float2 c, float2 d)> pieces, float2 p, out float dist)
        {
            float best = float.MaxValue; float2 bp = p;
            foreach (var z in pieces)
            {
                float2 prev = z.a;
                for (int i = 1; i <= 16; i++)
                {
                    float t = i / 16f, u = 1f - t;
                    var q = u * u * u * z.a + 3f * u * u * t * z.b + 3f * u * t * t * z.c + t * t * t * z.d;
                    var c = SegmentIndex.ClosestPoint(p, prev, q);
                    float d = math.distance(c, p);
                    if (d < best) { best = d; bp = c; }
                    prev = q;
                }
            }
            dist = best;
            return bp;
        }

        private static List<(float2 a, float2 b, float2 c, float2 d)> AsPieces(List<PathCurve> curves)
        {
            var list = new List<(float2, float2, float2, float2)>();
            foreach (var c in curves) list.Add((c.A, c.B, c.C, c.D));
            return list;
        }

        /// <summary>
        /// Links from the promenade down to the edge element every ~45 m: a straight ramp when the embankment is gentle,
        /// else one diagonal ramp along the slope at ≤ 10 % (never a zig-zag, never fenced). A boardwalk is reached by a
        /// short timber stub from the bank foot. Ends lie exactly on both walks (the composer splits them there).
        /// </summary>
        private static void EdgeLinks(Work w)
        {
            var ctx = w.Ctx; var shore = w.Shore; var g = w.G; var occ = w.Occ;
            if (w.Edge == "bank" || w.EdgePieces.Count == 0 || w.PromCurves.Count == 0) return;
            var prom = new List<(float2 a, float2 b, float2 c, float2 d)>();
            foreach (var cs in w.PromCurves) prom.AddRange(AsPieces(cs));
            var asset = AssetMix.One(ctx.S("pathAsset"));
            bool board = w.Edge == "boardwalk";
            var pierAsset = new AssetRef("", WaterfrontArt.Or(ctx.S("boardwalkAsset"), WaterfrontArt.Boardwalk));
            float spacing = w.Edge == "quay" ? 60f : 45f;
            int n = math.max(1, (int)math.floor(shore.Length / spacing));
            int k = 0;
            foreach (float s in shore.Stations(n, 12f))
            {
                if (!w.InRun(s) || w.InGap(s, 8f)) continue;
                var wp = shore.WaterAt(s, out var inw);
                var tan = new float2(inw.y, -inw.x);
                var top = ClosestOn(prom, wp + inw * w.DwP, out float dTop);
                if (dTop > 3f) continue;
                float hTop = w.Hp(s);
                float footDw = board ? 0.9f : w.EdgeDw;
                float rise = math.abs(hTop - w.Deck);
                float span = math.max(1f, math.distance(top, wp + inw * footDw));
                float run = rise / 0.10f;
                float shift = run > span ? math.sqrt(run * run - span * span) : 0f;
                if (shift > 45f) continue;
                const float sgn = 1f;   // parallel diagonals read as designed ramps; alternating ones met in Vs
                var footGuess = shore.WaterAt(s + sgn * shift, out var inw2) + inw2 * footDw;
                float2 foot;
                if (board) foot = footGuess;
                else
                {
                    foot = ClosestOn(w.EdgePieces, footGuess, out float dFoot);
                    if (dFoot > 3f) continue;
                }
                // Clear of piers, steps, kiosks and each other.
                bool blocked = false;
                float len = math.distance(top, foot);
                for (float t = 2f; t < len - 2f; t += 1.5f)
                {
                    var q = math.lerp(top, foot, t / len);
                    if (occ.Discs.Overlaps(q, 1.2f, 0.1f) || occ.HitsPolygon(q, 1.2f)) { blocked = true; break; }
                }
                if (blocked || !shore.Outline.Contains(foot) && shore.Depth(foot) > EdgeReach) continue;
                g.Ramp(top, hTop, foot, w.Deck, 1.6f, 2.5f);
                ctx.Plan.Nets.Add(new PlannedNet
                {
                    A = new float3(top.x, hTop, top.y), B = new float3(math.lerp(top, foot, 1f / 3f).x, math.lerp(hTop, w.Deck, 1f / 3f), math.lerp(top, foot, 1f / 3f).y),
                    C = new float3(math.lerp(top, foot, 2f / 3f).x, math.lerp(hTop, w.Deck, 2f / 3f), math.lerp(top, foot, 2f / 3f).y), D = new float3(foot.x, w.Deck, foot.y),
                    Role = NetRole.Pathway, Asset = asset, FollowTerrain = true,
                });
                occ.TakeCorridor(top, foot, 1.6f, 100);
                occ.Paths.Add(top, foot, 1.5f, 100);
                if (board)
                {
                    // Bank foot → boardwalk: a short timber stub at deck level on a small levelled landing.
                    g.Sculpt.PadDisc(foot, 1.6f, w.Deck, 2f);
                    var deckP = ClosestOn(w.EdgePieces, foot - inw2 * 2f, out float dd);
                    if (dd < 6f && math.distance(foot, deckP) > 0.8f)
                        Deck(ctx, (foot, math.lerp(foot, deckP, 1f / 3f), math.lerp(foot, deckP, 2f / 3f), deckP), w.Deck, NetRole.Pathway, pierAsset, AssetCategories.NetPier);
                }
                w.Gaps.Add(new float2(math.min(s, s + sgn * shift) - 2f, math.max(s, s + sgn * shift) + 2f));
                k++;
            }
            ctx.Plan.Facts["edgeLinks"] = k;
        }

        // ───────────── piers, steps, fishing ─────────────

        /// <summary>
        /// Piers start from a paved landing on the promenade's water side, ramp down the embankment to the edge (or join the
        /// quay), then run out over the water as a level deck while it stays inside the outline (T-heads optional).
        /// </summary>
        private static void Piers(Work w)
        {
            var ctx = w.Ctx; var shore = w.Shore; var g = w.G; var occ = w.Occ;
            int wanted = ctx.I("piers");
            if (wanted <= 0) return;
            if (shore.Mode != ShoreMode.Inside)
            {
                ctx.Issue(IssueSeverity.Info, "Placemaker.Waterfront.Issue.PiersNeedWater");
                return;
            }
            var pierAsset = new AssetRef("", WaterfrontArt.Or(ctx.S("boardwalkAsset"), WaterfrontArt.Pier));
            var pathAsset = AssetMix.One(ctx.S("pathAsset"));
            float maxLen = ctx.F("pierLength");
            bool heads = ctx.B("pierHeads");
            float deck = math.max(w.Deck, (g.HasLevel ? g.Level : w.Deck) + 1.2f);
            foreach (float at in shore.Stations(wanted, 14f))
            {
                if (!w.InRun(at) || w.InGap(at, 6f)) continue;
                var root = shore.WaterAt(at, out var inw);
                var dir = -inw;
                var tan = new float2(inw.y, -inw.x);
                // Out over the water while it stays inside the outline.
                float len = 0f;
                for (float l = 2f; l <= maxLen; l += 2f)
                {
                    var q = root + dir * l;
                    if (!shore.Outline.ContainsDisc(q, 3f) || !shore.IsWet(q)) break;
                    len = l;
                }
                if (len < 8f) continue;
                var end = root + dir * len;
                // Where the pier meets the edge: ON a quay / walkway (its deck, a junction the composer splits), else the bank foot.
                float2 start = root + inw * 0.6f, rampFoot = root + inw * 0.9f;
                bool onEdge = false;
                if (w.Edge != "bank" && w.EdgePieces.Count > 0)
                {
                    var q = ClosestOn(w.EdgePieces, root + inw * w.EdgeDw, out float dq);
                    if (dq < 3f) { start = q; onEdge = true; rampFoot = w.Edge == "boardwalk" ? root + inw * 0.9f : q; }
                }
                float footH = w.Edge == "bank" ? math.max(g.H(rampFoot), g.HasLevel ? g.Level + 0.4f : g.H(rampFoot)) : w.Deck;
                // The landing on the promenade's sea side, shifted along the shore when the embankment is too high for a
                // straight ≤ 10 % ramp (one diagonal ramp, as the edge links).
                float hp = w.Hp(at);
                float span = math.max(1f, w.DwP - w.Hw - 5.5f - math.distance(rampFoot, root) );
                float need = math.abs(hp - footH) / 0.10f;
                float shift = need > span ? math.sqrt(need * need - span * span) : 0f;
                if (shift > 40f) continue;
                var lw = shore.WaterAt(at + shift, out var linw);
                var ltan = new float2(linw.y, -linw.x);
                float2 landing = lw + linw * (w.DwP - w.Hw - 2.8f);
                var rect = new OrientedRect { Center = landing, Axis = ltan, HalfLength = 4f, HalfWidth = 2.5f };
                if (w.DwP - w.Hw - 2.8f > 3f && shore.Land.ContainsDisc(landing, 3f))
                {
                    g.Sculpt.Pad(rect.Ring, hp, 2.5f);
                    if (ctx.Surface(rect.Ring, SurfaceRole.Paving, AssetMix.Pick(WaterfrontArt.Landing, 0u), AssetCategories.SurfacePaving))
                    {
                        LeisureKit.TakeRect(occ, rect);
                        w.Pads.Add(rect.Ring);
                    }
                }
                var rampTop = landing - linw * 2.5f;
                if (math.distance(rampTop, rampFoot) > 1.5f)
                {
                    g.Ramp(rampTop, hp, rampFoot, footH, 1.8f, 2.5f);
                    LeisureKit.Path(ctx, occ, rampTop, rampFoot, NetRole.Pathway, pathAsset);
                }
                // The deck, level out over the water (a boardwalk stub first when the ramp ends on the bank).
                if (math.distance(rampFoot, start) > 0.8f)
                    Deck(ctx, (rampFoot, math.lerp(rampFoot, start, 1f / 3f), math.lerp(rampFoot, start, 2f / 3f), start), onEdge ? w.Deck : deck, NetRole.Pathway, pierAsset, AssetCategories.NetPier);
                float d0 = onEdge ? w.Deck : deck;
                var bz = (start, math.lerp(start, end, 1f / 3f), math.lerp(start, end, 2f / 3f), end);
                Deck(ctx, bz, d0, NetRole.Pathway, pierAsset, AssetCategories.NetPier);
                deck = d0;
                float total = len;
                if (heads)
                    foreach (float sgn in new[] { 1f, -1f })
                    {
                        var h = end + tan * sgn * 8f;
                        if (shore.Outline.ContainsDisc(h, 1.5f) && shore.IsWet(h))
                        {
                            Deck(ctx, (end, math.lerp(end, h, 1f / 3f), math.lerp(end, h, 2f / 3f), h), deck, NetRole.Pathway, pierAsset, AssetCategories.NetPier);
                            total += 8f;
                        }
                    }
                w.Gaps.Add(new float2(at - 4f, at + shift + 5f));
                occ.TakeCorridor(rampTop, rampFoot, 2.2f, 100);
                w.Piers++;
                w.PierLength += total;
            }
        }

        /// <summary>
        /// River steps: paved flights cut from the promenade terrace down to the walkway quay (or the water's edge), each
        /// a few deliberate level treads (crisp, paved) inside the smooth embankment; nothing else stands on them.
        /// </summary>
        private static void Steps(Work w)
        {
            var ctx = w.Ctx; var shore = w.Shore; var g = w.G;
            string mode = ctx.S("steps");
            if (mode == "none") return;
            int levels = math.max(2, ctx.I("stepLevels"));
            float top0 = w.EdgeIn + 0.3f, top1 = w.DwP - w.Hw - 0.6f;
            float run = top1 - top0;
            if (run < levels * 1.2f) return;
            var windows = new List<float2>();
            float end = math.min(10f, shore.Length * 0.1f);
            if (mode == "continuous")
            {
                float usable = shore.Length - 2f * end;
                int n = math.max(1, (int)math.round(usable / 22f));
                for (int i = 0; i < n; i++) windows.Add(new float2(end + usable * (i + 0.2f) / n, end + usable * (i + 0.8f) / n));
            }
            else
            {
                float half = ctx.F("stepWidth") * 0.5f;
                foreach (float s in shore.Stations(ctx.I("stepCount"), half + end)) windows.Add(new float2(s - half, s + half));
            }
            bool paved = AssetAvailability.Has(AssetCategories.SurfacePaving);
            float tread = run / levels;
            foreach (var win in windows)
            {
                float mid = (win.x + win.y) * 0.5f;
                if (!w.InRun(win.x) || !w.InRun(win.y) || w.InGap(mid, 6f)) continue;
                float hTop = w.Hp(mid), hBottom = w.Edge == "bank" ? (g.HasLevel ? g.Level + 0.25f : g.H(shore.WaterAt(mid, out _))) : w.Deck;
                if (hTop - hBottom < 0.8f) continue;   // nothing to step down: the embankment is a gentle lawn there
                var c = shore.WaterAt(mid, out var inw);
                if (shore.EdgeDistance(c + inw * (top0 + run * 0.5f)) < (win.y - win.x) * 0.5f + 4f) continue;
                for (int k = 0; k < levels; k++)
                {
                    float h = hTop - (hTop - hBottom) * (k + 1) / (levels + 1);
                    foreach (var ring in shore.StripFromWater(top1 - (k + 1) * tread, top1 - k * tread, win.x, win.y))
                        w.Levels.Add((ring, h));
                }
                if (paved)
                    foreach (var ring in shore.StripFromWater(top0, top1, win.x, win.y))
                        if (ctx.Surface(ring, SurfaceRole.Paving, AssetMix.Pick(WaterfrontArt.Paving(w.Style), 0u), AssetCategories.SurfacePaving)) w.Pads.Add(ring);
                for (float s = win.x; s < win.y; s += 3f)
                {
                    var a = shore.WaterAt(s, out var ia); var b = shore.WaterAt(math.min(win.y, s + 3f), out var ib);
                    w.Occ.TakeCorridor(a + ia * (top0 + run * 0.5f), b + ib * (top0 + run * 0.5f), run * 0.5f, 6);
                }
                w.Gaps.Add(win);
                w.Steps++;
            }
        }

        /// <summary>Natural bank: fishing spots at the water's edge, each on a small level landing with a seat and the rod.</summary>
        private static void FishingSpots(Work w)
        {
            var ctx = w.Ctx; var shore = w.Shore; var occ = w.Occ; var g = w.G;
            if (!ctx.B("fishing")) return;
            string cat = LeisureKit.Category(ctx, "", AssetCategories.WaterfrontFishing);
            float spacing = ctx.F("fishingSpacing");
            float d = math.max(1.6f, w.EdgeIn + 1.4f);
            for (float s = spacing * 0.75f; s < shore.Length - 2f; s += spacing)
            {
                if (!w.InRun(s) || w.InGap(s, 3.5f)) continue;
                var c = shore.WaterAt(s, out var inw);
                var tan = new float2(inw.y, -inw.x);
                var spot = c + inw * (d + 0.4f);
                if (!g.MakeLevel(spot, 2.2f, 1.2f, out _)) continue;
                uint v = Hashing.Mix(ctx.Seed, 0xF150u + (uint)w.Fishing);
                bool any = false;
                // The rod at the edge, a seat beside it facing the water, a bin every other spot.
                if (cat != null) any |= LeisureKit.Put(ctx, occ, c + inw * d + tan * 0.9f, Yaw(-inw), ObjectRole.Furniture, AssetMix.One(""), cat, 0.6f, v, 0.3f, 0.1f);
                any |= LeisureKit.Put(ctx, occ, c + inw * (d + 0.2f) - tan * 1.1f, Yaw(-inw), ObjectRole.Bench, AssetMix.Pick(WaterfrontArt.Or(ctx.S("benchAsset"), WaterfrontArt.AnglerBenches), 0u), null, 1.1f, Hashing.Mix(v, 1u), 0.3f, 0.1f);
                if ((w.Fishing & 1) == 0) LeisureKit.Put(ctx, occ, c + inw * (d + 0.1f) + tan * 2.9f, 0f, ObjectRole.Bin, AssetMix.One(""), null, 0.5f, Hashing.Mix(v, 2u), 0.3f, 0.1f);
                if (!any) continue;
                w.Fishing++;
                w.Gaps.Add(new float2(s - 2.5f, s + 3.8f));   // open edge: no riprap in front of the angler
            }
        }

        // ───────────── ground ─────────────

        /// <summary>
        /// Surfaces and paint: the paved promenade terrace (or the whole apron behind a harbour quay), lawn on the
        /// embankment and the park behind, a soft sand/dirt band at a natural bank's waterline (painted under the water).
        /// </summary>
        private static void Ground(Work w)
        {
            var ctx = w.Ctx; var shore = w.Shore;
            float t0 = w.Edge == "quay" ? w.EdgeIn - 0.2f : w.DwP - w.Hw - 2.4f, t1 = w.DwP + w.Hw + 2.4f;
            var pave = AssetMix.Pick(WaterfrontArt.Or(ctx.S("surfaceAsset"), WaterfrontArt.Paving(w.Style)), 0u);
            foreach (var run in w.Runs)
                switch (ctx.S("surface"))
                {
                    case "paved":
                        if (AssetAvailability.Require(ctx.Plan, AssetCategories.SurfacePaving, new[] { ctx.S("surfaceAsset") }, Keys.Category(AssetCategories.SurfacePaving)))
                            foreach (var ring in Band(shore, t0, t1, run)) ctx.Surface(ring, SurfaceRole.Paving, pave, AssetCategories.SurfacePaving);
                        break;
                    case "gravel":
                        if (AssetAvailability.Require(ctx.Plan, AssetCategories.SurfaceNatural, null, Keys.Category(AssetCategories.SurfaceNatural)))
                            foreach (var ring in Band(shore, w.DwP - w.Hw - 0.8f, w.DwP + w.Hw + 0.8f, run)) ctx.Surface(ring, SurfaceRole.Ground, new AssetRef("", "Forestry Surface 01"), AssetCategories.SurfaceNatural);
                        break;
                    case "grass":
                        foreach (var ring in Band(shore, t0, t1, run)) ctx.Surface(ring, SurfaceRole.Grass, AssetMix.One(""));
                        break;
                }
            if (ctx.S("backdrop") != "none")
                foreach (var ring in shore.BandFromWater(t1 + 0.3f, shore.MaxDepth + Shoreline.MaxGap + 20f)) ctx.Surface(ring, SurfaceRole.Grass, AssetMix.One(""));
            ShoreTexture(w);
        }

        private static List<float2[]> Band(Shoreline shore, float d0, float d1, float2 run)
        {
            var result = new List<float2[]>();
            foreach (var strip in shore.StripFromWater(d0, d1, run.x, run.y))
                try
                {
                    foreach (var r in Poly.Boolean(new[] { (IReadOnlyList<float2>)strip }, new[] { (IReadOnlyList<float2>)shore.Land.Ring }, BoolOp.Intersection, FillRule.Positive))
                        foreach (var piece in Poly.CutHoles(r, 0.5f))
                        {
                            var c = Poly.Clean(piece, 0.3f);
                            if (c.Length >= 3 && Poly.Area(c) > 8f) result.Add(c);
                        }
                }
                catch (Exception) { }
            return result;
        }

        /// <summary>
        /// A soft shore band in the terrain texture at a natural edge: sand (promenade, pier) or dirt (fishing bank) from a
        /// few metres under the water up the bank, fading inland with a natural edge. Quays are stone to the water.
        /// </summary>
        private static void ShoreTexture(Work w)
        {
            var ctx = w.Ctx; var shore = w.Shore;
            string mode = ctx.S("shoreTexture");
            if (mode == "auto")
                switch (w.Edge)
                {
                    case "bank": mode = w.Style == "fishing" ? "dirt" : "sand"; break;
                    case "boardwalk": mode = "sand"; break;
                    default: mode = "none"; break;
                }
            if (mode != "sand" && mode != "dirt" || !w.Water) return;
            int layer = mode == "sand" ? TerrainMaterials.Sand : TerrainMaterials.Dirt;
            float band = math.clamp(w.DwP * 0.4f, 3f, 7f);
            var edge = shore.WaterEdge();
            var clip = Poly.UnionAll(w.G.Clips);
            var rings = new List<IReadOnlyList<float2>>();
            foreach (var r in clip) if (r.Outer != null) rings.Add(r.Outer);
            foreach (var run in w.Runs)
                foreach (var strip in shore.StripFromWater(-6f, band + 4f, run.x, run.y))
                    try
                    {
                        foreach (var r in Poly.Boolean(new[] { (IReadOnlyList<float2>)strip }, rings, BoolOp.Intersection, FillRule.NonZero))
                            foreach (var piece in Poly.CutHoles(r, 0.5f))
                                TerrainPaint.Field(ctx, piece, layer, mode == "sand" ? 0.9f : 0.8f, 2.5f, 1.2f, edge, band * 0.6f, band, 0f, 0x5E0Au);
                    }
                    catch (Exception) { }
        }

        // ───────────── dressing ─────────────

        /// <summary>Riprap along a natural bank, mooring bollards and life rings along a quay edge.</summary>
        private static void EdgeDressing(Work w)
        {
            var ctx = w.Ctx; var shore = w.Shore; var occ = w.Occ; var g = w.G;
            if (ctx.B("moorings") && w.Edge != "boardwalk")
            {
                string cat = LeisureKit.Category(ctx, "", AssetCategories.WaterfrontMooring, AssetCategories.Bollard);
                // On the apron just behind the quay deck (objects on the net itself would collide with it).
                float d = w.EdgeIn + 0.8f;
                if (cat != null)
                    for (float s = 3f; s < shore.Length - 3f; s += ctx.F("mooringSpacing"))
                    {
                        if (!w.InRun(s) || w.InGap(s, 1.5f)) continue;
                        var c = shore.WaterAt(s, out var inw);
                        if (LeisureKit.Put(ctx, occ, c + inw * d, Yaw(-inw), ObjectRole.Furniture, AssetMix.Pick(WaterfrontArt.Moorings, 0u), cat, 0.4f, Hashing.Mix(ctx.Seed, 0x3009u + (uint)w.Moorings), 0.4f, 0.1f))
                            w.Moorings++;
                    }
            }
            if (ctx.B("riprap") && w.Edge == "bank" && AssetAvailability.Require(ctx.Plan, AssetCategories.Rock, null, Keys.Category(AssetCategories.Rock)))
            {
                var keep = occ.GroundOk;
                occ.GroundOk = (p, r) => !g.OnCliff(p);
                for (float s = 1.5f; s < shore.Length - 1.5f; s += 2.4f)
                {
                    if (!w.InRun(s) || w.InGap(s, 1.5f)) continue;
                    uint v = Hashing.Mix(ctx.Seed, 0x21F0u + (uint)(s * 10f));
                    var c = shore.WaterAt(s + (Hashing.Unit(v) - 0.5f) * 1.2f, out var inw);
                    float scale = 0.6f + 0.7f * Hashing.Unit(Hashing.Mix(v, 1u));
                    var q = c + inw * (0.3f + Hashing.Unit(Hashing.Mix(v, 2u)) * 1.4f);
                    if (!shore.Land.Contains(q) || !occ.Free(q, 0.7f * scale, 0.3f, 0.05f)) continue;
                    var o = ctx.Object(q, Hashing.Unit(Hashing.Mix(v, 3u)) * 6.283f, ObjectRole.Furniture, AssetMix.One(""), v, -1, AssetCategories.Rock);
                    o.Scale = scale;
                    occ.Take(q, 0.7f * scale, 2);
                    w.Rocks++;
                }
                occ.GroundOk = keep;
            }
            if (ctx.B("nautical") && w.Edge != "bank")
            {
                string cat = LeisureKit.Category(ctx, "", AssetCategories.WaterfrontNautical, AssetCategories.Decoration);
                if (cat != null)
                    for (float s = 18f; s < shore.Length - 6f; s += 45f)
                    {
                        if (!w.InRun(s) || w.InGap(s, 3f)) continue;
                        var c = shore.WaterAt(s, out var inw);
                        // A life ring at the edge: first free spot just behind the deck, a little along the shore if taken.
                        foreach (var (d, ds) in new[] { (0.9f, 0f), (0.9f, 3f), (0.9f, -3f), (2f, 0f), (2f, 4f) })
                        {
                            var q = shore.WaterAt(s + ds, out var qi) + qi * (w.EdgeIn + d);
                            if (LeisureKit.Put(ctx, occ, q, Yaw(-qi), ObjectRole.Furniture, AssetMix.Pick(WaterfrontArt.Nautical, 0u), cat, 0.5f, Hashing.Mix(ctx.Seed, 0xA9C0u + (uint)(s * 3f)), 0.3f, 0.1f)) break;
                        }
                    }
            }
        }

        /// <summary>
        /// The promenade's rhythm: benches on its water side facing the water every benchSpacing, a bin with every third,
        /// lamps on the land side half a bay off the benches; railings only where the ground beside the walk drops.
        /// </summary>
        private static void PromenadeFurniture(Work w)
        {
            var ctx = w.Ctx; var shore = w.Shore; var occ = w.Occ; var g = w.G;
            var bench = AssetMix.Pick(WaterfrontArt.Or(ctx.S("benchAsset"), WaterfrontArt.Benches), 0u);
            var lamp = AssetMix.Pick(WaterfrontArt.Or(ctx.S("lampAsset"), WaterfrontArt.Lamps), 0u);
            var rail = AssetMix.Pick(WaterfrontArt.Or(ctx.S("railingAsset"), WaterfrontArt.Railings), 0u);
            float benchSpacing = ctx.F("benchSpacing"), lampSpacing = ctx.F("lampSpacing");
            for (int li = 0; li < w.Lines.Count; li++)
            {
                var line = w.Lines[li];
                if (w.Profiles[li] == null) continue;   // cliff-top walks dress themselves
                float len = Shoreline.LengthOf(line);
                if (benchSpacing > 0f)
                    for (float s = benchSpacing * 0.5f; s < len; s += benchSpacing)
                    {
                        var p = Shoreline.PointOn(line, s, out _);
                        shore.Nearest(p, out var inw, out float along);
                        if (w.InGap(along, 4f)) continue;
                        var q = p - inw * (w.Hw + 1.6f);
                        uint v = Hashing.Mix(ctx.Seed, 0xBE70u + (uint)w.Benches);
                        if (!LeisureKit.Put(ctx, occ, q, Yaw(-inw), ObjectRole.Bench, bench, null, 0.9f, v, 0.3f, 0.1f)) continue;
                        w.Benches++;
                        if (w.Benches % 3 == 0)
                        {
                            var tan = new float2(inw.y, -inw.x);
                            LeisureKit.Put(ctx, occ, q + tan * 1.9f, 0f, ObjectRole.Bin, AssetMix.Pick(WaterfrontArt.Bins, 0u), null, 0.4f, Hashing.Mix(v, 1u), 0.3f, 0.1f);
                        }
                    }
                if (lampSpacing > 0f)
                {
                    float phase = benchSpacing > 0f ? (benchSpacing * 0.5f + lampSpacing * 0.5f) % lampSpacing : lampSpacing * 0.5f;
                    for (float s = phase; s < len; s += lampSpacing)
                    {
                        var p = Shoreline.PointOn(line, s, out _);
                        shore.Nearest(p, out var inw, out _);
                        if (LeisureKit.Put(ctx, occ, p + inw * (w.Hw + 0.8f), Yaw(-inw), ObjectRole.Lamp, lamp, null, 0.4f, Hashing.Mix(ctx.Seed, 0x1A70u + (uint)w.Lamps), 0.2f, 0.1f))
                            w.Lamps++;
                    }
                }
                if (ctx.B("railings")) Railings(w, line, rail);
            }
            // Harbour apron: lamps and benches facing the water along the quay's land side, in the same rhythm.
            if (w.Edge == "quay")
                foreach (var line in w.EdgeLines)
                {
                    float len = Shoreline.LengthOf(line);
                    if (lampSpacing > 0f)
                        for (float s = lampSpacing * 0.5f; s < len; s += lampSpacing)
                        {
                            var p = Shoreline.PointOn(line, s, out _);
                            shore.Nearest(p, out var inw, out float along);
                            if (w.InGap(along, 3f)) continue;
                            if (LeisureKit.Put(ctx, occ, p + inw * (w.EdgeHw + 1f), Yaw(-inw), ObjectRole.Lamp, lamp, null, 0.4f, Hashing.Mix(ctx.Seed, 0x1A90u + (uint)w.Lamps), 0.2f, 0.1f))
                                w.Lamps++;
                        }
                    if (benchSpacing > 0f)
                        for (float s = benchSpacing * 0.5f + (lampSpacing > 0f ? lampSpacing * 0.5f : 0f); s < len; s += benchSpacing)
                        {
                            var p = Shoreline.PointOn(line, s, out _);
                            shore.Nearest(p, out var inw, out float along);
                            if (w.InGap(along, 3f)) continue;
                            if (LeisureKit.Put(ctx, occ, p + inw * (w.EdgeHw + 1.6f), Yaw(-inw), ObjectRole.Bench, bench, null, 0.9f, Hashing.Mix(ctx.Seed, 0xBE90u + (uint)w.Benches), 0.3f, 0.1f))
                                w.Benches++;
                        }
                }
        }

        /// <summary>Railing runs on a walk's water side only where the ground beside it drops more than 1 m within 3 m.</summary>
        private static void Railings(Work w, float2[] line, AssetRef rail)
        {
            var ctx = w.Ctx; var shore = w.Shore; var g = w.G; var occ = w.Occ;
            float len = Shoreline.LengthOf(line);
            var run = new List<float2>();
            void Flush()
            {
                if (run.Count >= 3)
                {
                    var simple = Poly.SimplifyPolyline(run, 0.3f);
                    for (int i = 1; i < simple.Length; i++)
                        if (math.distance(simple[i - 1], simple[i]) >= 1.5f)
                        {
                            ctx.StraightNet(simple[i - 1], simple[i], NetRole.Fence, rail);
                            occ.TakeCorridor(simple[i - 1], simple[i], 0.3f, 5);
                            w.Railings++;
                        }
                }
                run.Clear();
            }
            for (float s = 0f; s <= len; s += 2f)
            {
                var p = Shoreline.PointOn(line, s, out _);
                shore.Nearest(p, out var inw, out float along);
                var q = p - inw * (w.Hw + 2.2f);
                bool drop = g.H(q - inw * 3f) < g.H(q) - CliffTop.DropForRailing;
                if (drop && !w.InGap(along, 2f) && shore.Land.ContainsDisc(q, 0.3f) && !occ.Discs.Overlaps(q, 0.3f, 0.05f)) run.Add(q); else Flush();
            }
            Flush();
        }

        // ───────────── land side ─────────────

        private static void LandSide(Work w)
        {
            var ctx = w.Ctx; var shore = w.Shore; var occ = w.Occ; var g = w.G;
            float dLand = w.DwP + w.Hw + 2.4f;
            int kiosks = ctx.I("kiosks");
            if (kiosks > 0)
            {
                string cat = LeisureKit.Category(ctx, "", AssetCategories.Kiosk);
                string seats = AssetAvailability.Has(AssetCategories.CafeSeating) ? AssetCategories.CafeSeating : null;
                if (cat != null)
                    foreach (float s in shore.Stations(kiosks, 12f, 0.5f))
                    {
                        foreach (float off in new[] { 0f, 8f, -8f, 16f, -16f })
                        {
                            if (!w.InRun(s + off)) continue;
                            var c = shore.WaterAt(s + off, out var inw);
                            var tan = new float2(inw.y, -inw.x);
                            // Harbour: on the apron facing the quay; else on a level pad just behind the promenade.
                            var k = w.Edge == "quay" ? c + inw * (w.EdgeIn + (w.DwP - w.Hw - w.EdgeIn) * 0.55f) : c + inw * (dLand + 2.8f);
                            if (w.Edge != "quay" && !g.MakeLevel(k, 2.5f, 1.4f, out _)) continue;
                            uint v = Hashing.Mix(ctx.Seed, 0x6170u + (uint)w.Kiosks);
                            if (!LeisureKit.Put(ctx, occ, k, Yaw(-inw), ObjectRole.Furniture, AssetMix.Pick(WaterfrontArt.Kiosks, (uint)w.Kiosks), cat, 2.5f, v, 0.8f, 0.3f)) continue;
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
            if (ctx.B("beds")) Beds(w, dLand, ctx.F("treeSpacing"));
            string trees = ctx.S("trees");
            // One species per row: an avenue reads as planted, not as a sample of the tree pool.
            bool picked = ctx.L("treeSpecies").Length > 0;
            var ids = WaterfrontArt.Or(ctx.L("treeSpecies"), WaterfrontArt.Trees(w.Style));
            float spacing = ctx.F("treeSpacing");
            if (trees != "none" && (ids.Length > 0 || AssetAvailability.Has(AssetCategories.Tree)))
            {
                int rows = trees == "double" ? 2 : 1;
                int n = 0;
                var keep = occ.GroundOk;
                occ.GroundOk = (p, r) => !g.OnCliff(p) && g.Sculpt.GradeAt(p, 1.5f) < 0.3f;
                for (int r = 0; r < rows; r++)
                {
                    float d = dLand + (ctx.B("beds") ? 4.6f : 2.2f) + r * 6f;
                    foreach (var line in shore.OffsetFromWater(d, 2.5f, 8f, 2f, p => g.OnCliff(p)))
                    {
                        float len = Shoreline.LengthOf(line);
                        for (float s = (r == 0 ? 0.5f : 1f) * spacing; s < len; s += spacing)
                        {
                            var p = Shoreline.PointOn(line, s, out _);
                            uint v = Hashing.Mix(ctx.Seed, 0x7EE0u + (uint)n);
                            if (LeisureKit.Put(ctx, occ, p, Hashing.Unit(v) * 6.283f, ObjectRole.Tree, AssetMix.Pick(ids, picked ? v : (uint)r), null, 2.2f, v, 1f, 0.3f, 3, 2 + (int)(v % 2u)))
                                n++;
                        }
                    }
                }
                occ.GroundOk = keep;
            }
        }

        /// <summary>
        /// Planted beds behind the promenade: low flowering shrubs in long soft beds (one kind per bed, alternating) in the
        /// bays between the lamps, with lush-grass paint under them — coherent planting, not planter rows in the meadow.
        /// </summary>
        private static void Beds(Work w, float dLand, float treeSpacing)
        {
            var ctx = w.Ctx; var shore = w.Shore; var occ = w.Occ; var g = w.G;
            float bay = math.max(10f, ctx.F("lampSpacing") > 0f ? ctx.F("lampSpacing") : treeSpacing * 1.5f);
            var keep = occ.GroundOk;
            occ.GroundOk = (p, r) => !g.OnCliff(p) && g.Sculpt.GradeAt(p, 1.2f) < 0.25f;
            int bed = 0;
            foreach (var line in shore.OffsetFromWater(dLand + 0.9f, 1.5f, 8f, 1.5f, p => g.OnCliff(p)))
            {
                float len = Shoreline.LengthOf(line);
                for (float s0 = bay * 0.5f; s0 + bay * 0.6f < len; s0 += bay * 2f)
                {
                    var ids = new[] { WaterfrontArt.BedShrubs[bed % WaterfrontArt.BedShrubs.Length] };
                    int placed = 0;
                    var pts = new List<float2>();
                    for (float s = s0; s < s0 + bay * 0.6f; s += 1.6f)
                    {
                        var p = Shoreline.PointOn(line, s, out var t);
                        var n = new float2(-t.y, t.x);
                        uint v = Hashing.Mix(ctx.Seed, 0xBED0u + (uint)(bed * 64 + placed));
                        var q = p + n * (Hashing.Unit(v) - 0.5f) * 0.8f;
                        if (!occ.Free(q, 1.0f, 0.3f, 0.05f)) continue;
                        ctx.Object(q, Hashing.Unit(Hashing.Mix(v, 1u)) * 6.283f, ObjectRole.Bush, AssetMix.Pick(ids, 0u), v);
                        occ.Take(q, 1.0f, 4);
                        pts.Add(q);
                        placed++;
                    }
                    if (placed >= 3)
                    {
                        w.Beds++;
                        bed++;
                        // A soft lush-grass bed under the shrubs.
                        var a = pts[0]; var b = pts[pts.Count - 1];
                        var dir = math.normalizesafe(b - a); var nn = new float2(-dir.y, dir.x);
                        var ring = new[] { a - dir * 1.2f - nn * 1.2f, b + dir * 1.2f - nn * 1.2f, b + dir * 1.2f + nn * 1.2f, a - dir * 1.2f + nn * 1.2f };
                        TerrainPaint.Field(ctx, ring, TerrainMaterials.Dirt, 0.45f, 0.9f, 0.3f, null, 0f, 0f, 0f, 0xBEDu + (uint)bed);
                    }
                }
            }
            occ.GroundOk = keep;
        }

        private static void Backdrop(Work w)
        {
            var ctx = w.Ctx; var shore = w.Shore; var g = w.G;
            if (ctx.S("backdrop") != "park") return;
            var ids = WaterfrontArt.Or(ctx.L("treeSpecies"), WaterfrontArt.Trees(w.Style));
            if (ids.Length == 0 && !AssetAvailability.Has(AssetCategories.Tree)) return;
            float from = w.DwP + w.Hw + 16f;
            var keep = w.Occ.GroundOk;
            w.Occ.GroundOk = (p, r) => !g.OnCliff(p);
            foreach (var ring in shore.BandFromWater(from, shore.MaxDepth + Shoreline.MaxGap + 20f))
            {
                var band = LayerRegion.From(ring);
                if (band == null) continue;
                LeisureKit.Plant(ctx, w.Occ, band, (int)(band.Area / 10000f * 28f), ObjectRole.Tree, ids, 2.5f, 26f, 0.15f, 0x7EF0u, null, 1, 3);
                LeisureKit.Plant(ctx, w.Occ, band, (int)(band.Area / 10000f * 30f), ObjectRole.Bush, WaterfrontArt.Shrubs, 1f, 18f, 0.12f, 0xB0F0u, null);
            }
            w.Occ.GroundOk = keep;
        }
    }
}
