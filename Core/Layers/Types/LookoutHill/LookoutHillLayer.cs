// Lookout hill: a big landform raised out of the area and blended back to the original ground at the
// outline (HillField), a path that climbs it at a walkable grade (HillPaths: spiral or switchbacks),
// and a summit viewpoint — paved plaza, benches looking out, telescopes, a tower, pavilion or cairn.
// Trees cover the lower slopes up to a tree line so the top stays open for the view.
// Subtypes: hilltop lookout, sledding hill (a long open run on the far side, straw bales at the
// run-out), viewpoint mound, observation terraces (stepped levels with benches on every terrace edge)
// and kite hill (an open dome with kites and a windsock, picnics on the flanks).
// Every object, path and surface sits on the planned (raised) ground height.

using System;
using System.Collections.Generic;
using Placemaker.Core.Geometry;
using Placemaker.Core.Layers.Common;
using Placemaker.Core.Layers.Leisure;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Types.LookoutHill
{
    [AreaGenerator(208, Group = "nature")]
    public sealed class LookoutHillLayer : LeisureLayer
    {
        public const string SummitTag = "summit";

        protected override string Area => "LookoutHill";
        public override string Id => "lookoutHill";
        public override string Icon => "Media/Game/Icons/TerrainHeight.svg";
        public override float MinArea => 1500f;
        public override string[] Emits => new[] { RegionTags.Green, RegionTags.Core, StreetFrontage.ShopsTag, SummitTag };

        public static readonly string[] Subtypes = { "hilltop", "sledding", "viewpointMound", "terraces", "kiteHill" };

        protected override int WantedEntrances(LayerRegion region) => math.min(2, Entrances.WantedByArea(region.Area));

        /// <summary>A feature labelled with its layer name (no Placemaker.Feature.* key of its own).</summary>
        private static FeatureInfo Plain(string layer, params (string key, object value)[] preset)
        {
            var f = Feature(layer, preset);
            f.LabelKey = "";
            return f;
        }

        protected override List<FeatureInfo> BuildFeatures()
        {
            var view = Feature(ViewpointAddOn.LayerId);
            view.Target = SummitTag;
            view.LabelKey = "Placemaker.LookoutHill.Feature.viewpoint";
            view.TooltipKey = "Placemaker.LookoutHill.Feature.viewpoint.Tooltip";
            return new List<FeatureInfo>
            {
                view, Feature("picnic", ("groups", 2.0)), Feature("restrooms"), Feature("shops", ("count", 1.0), ("where", "entrances")),
                StreetShopsFeature(Area), Feature("rocks", ("clusters", 4.0), ("size", "large")), Plain("bigtrees", ("density", 1.5)),
                Feature("decals", ("theme", "leaves"), ("density", 1.5)),
            };
        }

        protected override void DefineControls(ControlSet c)
        {
            c.Choice("subtype", "hilltop", ControlTier.Essential, "hill", Subtypes).Tip();
            c.Slider("height", 16, 3, 40, 1, "m", ControlTier.Essential, "hill").Tip();
            c.Choice("shape", "mound", ControlTier.Essential, "hill", "mound", "cone", "dome", "mesa", "terraced").Tip();
            c.Choice("summitAt", "centre", ControlTier.More, "hill", "centre", "far", "highest").Tip();
            c.Slider("plateau", 8, 3, 30, 1, "m", ControlTier.More, "hill").Layout(3).Tip();
            c.Slider("maxSlope", 30, 10, 45, 5, "%", ControlTier.More, "hill").Tip();
            c.Slider("roughness", 30, 0, 100, 10, "%", ControlTier.More, "hill").Tip();
            c.Slider("levels", 3, 2, 5, 1, "count", ControlTier.More, "hill").When("shape=terraced");

            c.Choice("pathStyle", "spiral", ControlTier.Essential, "path", "spiral", "switchback", "direct", "none").Tip();
            c.Slider("grade", 9, 4, 20, 1, "%", ControlTier.More, "path").Tip();
            c.Choice("pathWidth", "narrow", ControlTier.More, "path", "narrow", "wide");
            c.Slider("restSpots", 4, 0, 12, 1, "count", ControlTier.More, "path").Tip();
            c.Toggle("lamps", false, ControlTier.More, "path");
            c.Asset("pathAsset", AssetCategories.Pathway, ControlTier.More, "path");

            c.Choice("viewpoint", "tower", ControlTier.Essential, "summit", "plaza", "tower", "pavilion", "cairn", "none").Tip();
            c.Slider("summitBenches", 6, 0, 16, 1, "count", ControlTier.More, "summit");
            c.Slider("telescopes", 2, 0, 6, 1, "count", ControlTier.More, "summit").Tip();
            c.Toggle("summitPaint", true, ControlTier.More, "summit").Tip();
            c.Slider("paintLayer", 1, 1, 4, 1, "count", ControlTier.More, "summit").When("summitPaint=true");

            c.Slider("treeDensity", 90, 0, 400, 10, "perHa", ControlTier.Essential, "planting").Tip();
            c.Slider("treeLine", 65, 10, 100, 5, "%", ControlTier.More, "planting").Tip();
            c.Slider("bushDensity", 40, 0, 300, 10, "perHa", ControlTier.More, "planting");
            c.AssetMulti("treeSpecies", AssetCategories.Tree, ControlTier.More, "planting");

            c.Slider("runWidth", 26, 10, 60, 2, "m", ControlTier.Essential, "sledding").When("subtype=sledding").Tip();
            c.Toggle("runOut", true, ControlTier.More, "sledding").When("subtype=sledding").Tip();
            c.Slider("kites", 5, 0, 12, 1, "count", ControlTier.Essential, "kites").When("subtype=kiteHill").Tip();
            c.Markers("entrances", ControlTier.More, EntranceKind);
            DefineStreetShopControls(c);

            c.Fixed("subtype");
            c.RandomRange("height", 6, 24);
            c.RandomRange("treeDensity", 30, 180);
            c.RandomRange("grade", 6, 12);
            c.Chance("lamps", 0.3f);
        }

        protected override void DefinePresets()
        {
            Preset("hilltop", ("subtype", "hilltop"), ("height", 18.0), ("shape", "mound"), ("summitAt", "centre"), ("plateau", 9.0),
                ("pathStyle", "spiral"), ("viewpoint", "tower"), ("treeDensity", 120.0), ("treeLine", 65.0));
            Preset("sledding", ("subtype", "sledding"), ("height", 14.0), ("shape", "dome"), ("summitAt", "far"), ("plateau", 8.0), ("maxSlope", 30.0),
                ("pathStyle", "switchback"), ("viewpoint", "pavilion"), ("treeDensity", 70.0), ("treeLine", 100.0), ("summitBenches", 4.0), ("telescopes", 0.0));
            Preset("viewpointMound", ("subtype", "viewpointMound"), ("height", 8.0), ("shape", "cone"), ("plateau", 5.0), ("pathStyle", "spiral"),
                ("viewpoint", "plaza"), ("telescopes", 3.0), ("summitBenches", 6.0), ("treeDensity", 40.0), ("treeLine", 30.0), ("roughness", 10.0));
            Preset("terraces", ("subtype", "terraces"), ("height", 16.0), ("shape", "terraced"), ("levels", 4.0), ("plateau", 12.0),
                ("pathStyle", "switchback"), ("pathWidth", "wide"), ("viewpoint", "plaza"), ("summitBenches", 10.0), ("treeDensity", 50.0), ("lamps", true));
            Preset("kiteHill", ("subtype", "kiteHill"), ("height", 12.0), ("shape", "dome"), ("plateau", 14.0), ("pathStyle", "direct"),
                ("viewpoint", "cairn"), ("treeDensity", 30.0), ("treeLine", 25.0), ("kites", 6.0), ("summitBenches", 4.0), ("roughness", 0.0));
        }

        protected override void PlanCore(LayerContext ctx)
        {
            var region = ctx.LayerRegion;
            var occ = new Occupancy(region, ctx.Input.Reserved, ctx.Site);
            var gates = Gates(ctx);
            string sub = ctx.S("subtype");
            region.DeepestPoint(out float inscribed);
            inscribed = math.max(3f, inscribed);

            // ── Summit & landform ──
            var summit = Summit(ctx, region, gates, inscribed);
            float plateau = math.min(ctx.F("plateau"), inscribed * 0.45f);
            var spec = new HillSpec
            {
                Height = ctx.F("height"), Shape = HillSpec.Parse(ctx.S("shape")), Summit = summit, Plateau = plateau,
                MaxSlope = ctx.F("maxSlope") / 100f, Roughness = ctx.F("roughness") / 100f, Levels = ctx.I("levels"),
                Seed = Hashing.Mix(ctx.Seed, 0x4111u),
            };
            // Obstacles inside the outline keep their ground: the hill flattens out around them.
            Func<float2, float> skip = null;
            bool anyReserved = ctx.Input.Reserved.Discs.Count + ctx.Input.Reserved.Corridors.Count + ctx.Input.Reserved.Polygons.Count > 0
                || (ctx.Site?.Obstacles != null && ctx.Site.Obstacles.Count > 0);
            if (anyReserved) skip = p => region.BoundaryDistance(p, 8f) >= 6f && occ.Clearance(p, 8f) < 6f ? 0f : float.MaxValue;
            var field = HillField.Build(ctx, region, spec, skip);
            field.Emit(ctx, region);
            if (field.Peak < spec.Height * 0.8f) ctx.Issue(IssueSeverity.Info, "Placemaker.LookoutHill.Issue.Lower", Math.Round(field.Peak, 1), Math.Round(spec.Height));
            ctx.Check();

            float summitR = math.max(4f, plateau * 0.85f);
            bool plaza = ctx.S("viewpoint") != "none";
            occ.Take(summit, summitR + 0.5f, 90);

            // ── Sledding run: claimed before the path so the path keeps off it ──
            float2 runA = default, runB = default; bool run = false;
            if (sub == "sledding") run = SledRun(ctx, occ, region, summit, summitR, gates, out runA, out runB);
            ctx.Check();

            // ── The way up ──
            float pathLen = ClimbPaths(ctx, occ, region, field, gates, summit, summitR);
            ctx.Check();

            // ── Summit ──
            if (plaza)
            {
                ctx.Surface(LayerRegion.Circle(summit, summitR, 24), SurfaceRole.Paving, AssetMix.One(""), AssetCategories.SurfacePaving);
                SummitDressing(ctx, occ, new Occupancy(region, ctx.Input.Reserved, ctx.Site), summit, summitR);
            }
            if (ctx.B("summitPaint"))
                TerrainPaint.Patch(ctx, LeisureKit.Blob(summit, summitR + 7f, Hashing.Mix(ctx.Seed, 0x5A1u), 16, 0.2f), ctx.I("paintLayer"), 0.7f, 3f);
            ctx.Check();

            // ── Subtype extras ──
            if (run) RunDressing(ctx, occ, region, runA, runB);
            if (spec.Shape == HillShape.Terraced) TerraceBenches(ctx, occ, field, spec);
            if (sub == "kiteHill") Kites(ctx, occ, field, summit);
            if (ctx.I("restSpots") > 0 && pathLen > 0f) PathBenches(ctx, occ, pathLen / ctx.I("restSpots"), AssetMix.One(""), 0xBE60u);
            if (ctx.B("lamps")) PathLamps(ctx, occ, 22f, AssetMix.One(""), 0x1A60u);
            ctx.Check();

            // ── Planting below the tree line ──
            float treeTop = field.Peak * ctx.F("treeLine") / 100f + 0.01f;
            Func<float2, bool> reject = p => field.OffsetAt(p) > treeTop || math.distance(p, summit) < summitR + 4f;
            float ha = region.Area / 10000f;
            var treeIds = ctx.L("treeSpecies");
            int trees = math.min(10000, (int)(ha * ctx.F("treeDensity")));
            if (trees > 0 && AssetAvailability.Require(ctx.Plan, AssetCategories.Tree, treeIds, Keys.Category(AssetCategories.Tree)))
                LeisureKit.Plant(ctx, occ, region, trees, ObjectRole.Tree, treeIds, 2.5f, 24f, 0.35f, 0x7F00u, reject, 1, 3);
            int bushes = math.min(10000, (int)(ha * ctx.F("bushDensity")));
            if (bushes > 0 && AssetAvailability.Require(ctx.Plan, AssetCategories.Bush, null, Keys.Category(AssetCategories.Bush)))
                LeisureKit.Plant(ctx, occ, region, bushes, ObjectRole.Bush, null, 1.1f, 12f, 0.3f, 0xB600u, reject);
            ctx.Check();

            // ── Regions, heights, stats ──
            ctx.AddRegion(LayerRegion.Circle(summit, summitR, 20), SummitTag, SummitTag, RegionTags.Paved);
            EmitGreenRegions(ctx, occ);
            Drape(ctx.Plan, field);
            CommonStats(ctx);
            ctx.Stat("Placemaker.Stat.HillHeight", Math.Round(field.Peak, 1), Keys.UnitM);
            ctx.Plan.Facts["peak"] = field.Peak;
            ctx.Plan.Facts["hillCells"] = field.Cells;
            ctx.Plan.Facts["summitX"] = summit.x;
            ctx.Plan.Facts["summitZ"] = summit.y;
        }

        private static float2 Summit(LayerContext ctx, LayerRegion region, List<Gate> gates, float inscribed)
        {
            var occ0 = new Occupancy(region, ctx.Input.Reserved, ctx.Site);
            var deepest = region.DeepestPoint(out _, (p, c) => occ0.Clearance(p, c));
            string mode = ctx.S("summitAt");
            if (mode == "centre") return deepest;
            var g0 = gates.Count > 0 ? gates[0].Position : region.Centroid;
            float2 best = deepest; float bestScore = float.MinValue;
            const int grid = 24;
            var step = (region.Max - region.Min) / grid;
            for (int y = 0; y <= grid; y++)
                for (int x = 0; x <= grid; x++)
                {
                    var p = region.Min + step * new float2(x, y);
                    if (!region.Contains(p)) continue;
                    float d = occ0.Clearance(p, inscribed + 1f);
                    if (d < inscribed * 0.6f) continue;
                    float score = mode == "far" ? math.distance(p, g0) + d * 0.5f : ctx.H(p) * 10f + d * 0.05f;
                    if (score > bestScore) { bestScore = score; best = p; }
                }
            return best;
        }

        private float ClimbPaths(LayerContext ctx, Occupancy occ, LayerRegion region, HillField field, List<Gate> gates, float2 summit, float summitR)
        {
            string style = ctx.S("pathStyle");
            if (style == "none" || gates.Count == 0) return 0f;
            var role = ctx.S("pathWidth") == "wide" ? NetRole.PathwayWide : NetRole.Pathway;
            float hw = Composer.DefaultHalfWidth(role);
            var asset = AssetMix.One(ctx.S("pathAsset"));
            float total = 0f;
            var main = new List<float2>();
            for (int gi = 0; gi < gates.Count; gi++)
            {
                var g = gates[gi];
                var inward = region.InwardNormal(g.Segment);
                // A second entrance joins the first path when it can; otherwise it climbs on its own.
                if (gi > 0 && main.Count > 1)
                {
                    int best = -1; float bd = float.MaxValue;
                    for (int i = 0; i < main.Count; i++)
                    {
                        float d = math.distance(main[i], g.Position);
                        if (d < bd && Clear(region, occ, g.Position, main[i], hw)) { bd = d; best = i; }
                    }
                    if (best >= 0 && bd < 120f)
                    {
                        LeisureKit.Path(ctx, occ, g.Position, main[best], role, asset);
                        total += bd;
                        continue;
                    }
                }
                var spec = new ClimbSpec
                {
                    Grade = ctx.F("grade") / 100f, Style = style, HalfWidth = hw, Spacing = hw * 2f + 3f,
                    LegLength = math.clamp(math.sqrt(region.Area) * 0.3f, 24f, 70f),
                    Summit = summit, SummitRadius = summitR, Seed = Hashing.Mix(ctx.Seed, (uint)gi + 0xC11u),
                };
                var pts = HillPaths.Climb(field, region, occ, g.Position, g.Position + inward * (hw + 2.5f), spec);
                if (pts.Count < 3) continue;
                if (!HillPaths.Reached(pts, spec)) ctx.Issue(IssueSeverity.Info, "Placemaker.LookoutHill.Issue.PathShort");
                var simple = Poly.SimplifyPolyline(pts, 0.6f);
                for (int i = 0; i + 1 < simple.Length; i++)
                {
                    // Pieces are added to the occupancy one by one; the last piece onto the plaza is fine.
                    ctx.StraightNet(simple[i], simple[i + 1], role, asset);
                    occ.TakeCorridor(simple[i], simple[i + 1], hw, 100);
                    occ.Paths.Add(simple[i], simple[i + 1], hw, 100);
                    total += math.distance(simple[i], simple[i + 1]);
                }
                if (main.Count == 0) main.AddRange(simple);
            }
            return total;
        }

        private static bool Clear(LayerRegion region, Occupancy occ, float2 a, float2 b, float hw)
        {
            if (region.CrossesBoundary(a, b, true)) return false;
            int n = math.max(1, (int)(math.distance(a, b) / 2f));
            for (int i = 1; i < n; i++)
            {
                var p = math.lerp(a, b, i / (float)n);
                if (!region.Contains(p) || occ.Discs.Overlaps(p, hw, 0.2f) || occ.HitsPolygon(p, hw)) return false;
                if (i < n - 1 && occ.Corridors.Overlaps(p, hw, 0.2f)) return false;
            }
            return true;
        }

        private void SummitDressing(LayerContext ctx, Occupancy occ, Occupancy res, float2 summit, float r)
        {
            uint v = Hashing.Mix(ctx.Seed, 0x5077u);
            // Centrepiece: tower, pavilion or cairn.
            string[] chain;
            switch (ctx.S("viewpoint"))
            {
                case "tower": chain = new[] { AssetCategories.Lookout, AssetCategories.Shelter }; break;
                case "pavilion": chain = new[] { AssetCategories.Shelter, AssetCategories.Stage }; break;
                case "cairn": chain = new[] { AssetCategories.CenterpieceStatue, AssetCategories.Rock }; break;
                default: chain = null; break;
            }
            float centreR = 0f;
            if (chain != null)
            {
                string cat = LeisureKit.Category(ctx, "", chain);
                if (cat != null && res.Free(summit, math.min(r * 0.45f, 3.5f), 0.5f, 0.2f) && !occ.Paths.Overlaps(summit, math.min(r * 0.45f, 3.5f), 0.2f))
                {
                    centreR = math.min(r * 0.45f, 3.5f);
                    ctx.Object(summit, Hashing.Unit(v) * 6.283f, ObjectRole.Furniture, AssetMix.One(""), v, -1, cat);
                }
            }
            // Benches on the rim looking out, telescopes between them.
            int benches = ctx.I("summitBenches"), scopes = ctx.I("telescopes");
            float rim = math.max(centreR + 1.8f, r - 1.3f);
            float phase = Hashing.Unit(Hashing.Mix(v, 1u)) * 6.283f;
            var placedAt = new List<float2>();
            for (int i = 0; i < benches; i++)
            {
                float a = phase + i * 2f * math.PI / benches;
                var p = summit + new float2(math.cos(a), math.sin(a)) * rim;
                if (Spot(ctx, occ, res, p, LeisureKit.Yaw(summit, p), ObjectRole.Bench, null, 1.0f, Hashing.Mix(v, 10u + (uint)i))) placedAt.Add(p);
            }
            string scope = scopes > 0 ? LeisureKit.Category(ctx, "", AssetCategories.HillTelescope) : null;
            if (scope != null)
                for (int i = 0; i < scopes; i++)
                {
                    float a = phase + (i + 0.5f) * 2f * math.PI / math.max(1, scopes);
                    var p = summit + new float2(math.cos(a), math.sin(a)) * math.max(centreR + 1.2f, r - 0.8f);
                    Spot(ctx, occ, res, p, LeisureKit.Yaw(summit, p), ObjectRole.Furniture, scope, 0.5f, Hashing.Mix(v, 40u + (uint)i));
                }
        }

        /// <summary>Puts an object on the summit plaza (inside the reserved plaza disc, clear of what is already there).</summary>
        private static bool Spot(LayerContext ctx, Occupancy occ, Occupancy res, float2 p, float yaw, ObjectRole role, string cat, float radius, uint v)
        {
            foreach (var o in ctx.Plan.Objects)
                if (math.distancesq(o.Position.xz, p) < (radius + 1.1f) * (radius + 1.1f)) return false;
            if (occ.Paths.Overlaps(p, radius, 0.2f) || !res.Free(p, radius, radius, 0.2f)) return false;
            ctx.Object(p, yaw, role, AssetMix.One(""), v, -1, cat);
            return true;
        }

        private bool SledRun(LayerContext ctx, Occupancy occ, LayerRegion region, float2 summit, float summitR, List<Gate> gates, out float2 a, out float2 b)
        {
            a = b = summit;
            float hw = ctx.F("runWidth") * 0.5f;
            var g0 = gates.Count > 0 ? gates[0].Position : summit;
            var toGate = math.normalizesafe(g0 - summit);
            float best = 0f; float2 bestDir = default;
            for (int k = 0; k < 24; k++)
            {
                float ang = k * math.PI / 12f;
                var d = new float2(math.cos(ang), math.sin(ang));
                float len = 0f;
                for (float s = summitR + 2f; s < 2000f; s += 3f)
                {
                    if (!region.ContainsDisc(summit + d * s, hw * 0.6f + 6f)) break;
                    len = s;
                }
                // Long runs win; never straight at the entrance the path starts from.
                float score = len * (math.dot(d, toGate) > 0.6f ? 0.3f : 1f);
                if (score > best) { best = score; bestDir = d; }
            }
            if (best < summitR + 25f) { ctx.Issue(IssueSeverity.Info, "Placemaker.LookoutHill.Issue.NoRun"); return false; }
            a = summit + bestDir * (summitR + 1.5f);
            float runLen = 0f;
            for (float s = summitR + 2f; s < 2000f; s += 3f) { if (!region.ContainsDisc(summit + bestDir * s, hw * 0.6f + 6f)) break; runLen = s; }
            b = summit + bestDir * runLen;
            occ.TakeCorridor(a, b, hw, 95);
            var rect = new OrientedRect { Center = math.lerp(a, b, 0.5f), Axis = bestDir, HalfLength = math.distance(a, b) * 0.5f, HalfWidth = hw };
            var clipped = LeisureKit.Intersect(region, rect.Ring);
            if (clipped != null) ctx.Surface(clipped.Ring, SurfaceRole.Grass, AssetMix.One(""), AssetCategories.SurfaceGrass);
            ctx.Plan.Facts["runLength"] = math.distance(a, b);
            return true;
        }

        private void RunDressing(LayerContext ctx, Occupancy occ, LayerRegion region, float2 a, float2 b)
        {
            var d = math.normalizesafe(b - a); var side = new float2(-d.y, d.x);
            float hw = ctx.F("runWidth") * 0.5f;
            if (ctx.B("runOut"))
            {
                string bale = AssetAvailability.Has(AssetCategories.HillBale) ? AssetCategories.HillBale : null;
                int n = math.max(3, (int)(hw * 2f / 2.6f));
                int placed = 0;
                if (bale != null)
                    for (int i = 0; i < n; i++)
                    {
                        float u = (i / (float)(n - 1) - 0.5f) * 2f * hw;
                        var p = b + d * (2.5f + 0.02f * u * u) + side * u;
                        if (LeisureKit.Put(ctx, occ, p, LeisureKit.Yaw(p, a), ObjectRole.Furniture, AssetMix.One(""), bale, 0.9f,
                            Hashing.Mix(ctx.Seed, 0xBA1u + (uint)i), 0.5f, 0.1f)) placed++;
                    }
                if (placed == 0)
                {
                    // No bales installed: a run-out fence across the bottom instead.
                    var f0 = b + d * 3f - side * hw; var f1 = b + d * 3f + side * hw;
                    if (region.Contains(f0) && region.Contains(f1) && !region.CrossesBoundary(f0, f1, false)) ctx.StraightNet(f0, f1, NetRole.Fence, AssetMix.One(""));
                }
            }
            // Parents' benches beside the run-out.
            for (int e = -1; e <= 1; e += 2)
            {
                var p = b - d * 4f + side * (e * (hw + 3f));
                LeisureKit.Put(ctx, occ, p, LeisureKit.Yaw(p, b - d * 4f), ObjectRole.Bench, AssetMix.One(""), null, 1.3f, Hashing.Mix(ctx.Seed, 0xBE8u + (uint)(e + 1)), 0.5f, 0.3f);
            }
        }

        private void TerraceBenches(LayerContext ctx, Occupancy occ, HillField field, HillSpec spec)
        {
            int levels = math.clamp(spec.Levels, 2, 6);
            int placed = 0;
            for (int k = 1; k < levels; k++)
            {
                float level = field.Peak * k / levels;
                var pts = field.Contour(level, math.max(0.4f, field.Cell * 0.12f));
                var chosen = new List<float2>();
                for (int i = 0; i < pts.Count && chosen.Count < 6; i++)
                {
                    int idx = (int)(Hashing.Mix(ctx.Seed, (uint)(k * 1000 + i)) % (uint)pts.Count);
                    var p = pts[idx];
                    bool far = true;
                    foreach (var q in chosen) if (math.distance(q, p) < 16f) { far = false; break; }
                    if (!far) continue;
                    var down = -math.normalizesafe(field.OffsetGradient(p));
                    if (LeisureKit.Put(ctx, occ, p, LeisureKit.Yaw(p, p + down), ObjectRole.Bench, AssetMix.One(""), null, 1.3f,
                        Hashing.Mix(ctx.Seed, 0x7E2u + (uint)placed), 1f, 0.3f)) { chosen.Add(p); placed++; }
                }
            }
            ctx.Plan.Facts["terraceBenches"] = placed;
        }

        private void Kites(LayerContext ctx, Occupancy occ, HillField field, float2 summit)
        {
            int n = ctx.I("kites");
            if (n <= 0) return;
            string kite = LeisureKit.Category(ctx, "", AssetCategories.HillKite);
            if (kite == null) return;
            var pts = new List<float2>();
            for (float lvl = 0.75f; lvl >= 0.35f && pts.Count < 200; lvl -= 0.1f) pts.AddRange(field.Contour(field.Peak * lvl, field.Peak * 0.05f));
            var chosen = new List<float2>();
            for (int i = 0; i < pts.Count && chosen.Count < n; i++)
            {
                var p = pts[(int)(Hashing.Mix(ctx.Seed, (uint)i + 0x417Eu) % (uint)pts.Count)];
                bool far = true;
                foreach (var q in chosen) if (math.distance(q, p) < 12f) { far = false; break; }
                if (far && LeisureKit.Put(ctx, occ, p, LeisureKit.Yaw(summit, p), ObjectRole.Furniture, AssetMix.One(""), kite, 1f,
                    Hashing.Mix(ctx.Seed, 0x41E0u + (uint)chosen.Count), 1f, 0.5f)) chosen.Add(p);
            }
        }

        /// <summary>Puts everything the layer planned onto the raised ground.</summary>
        private static void Drape(AreaPlan plan, HillField field)
        {
            foreach (var o in plan.Objects) o.Position.y = field.HeightAt(o.Position.xz);
            foreach (var n in plan.Nets)
            {
                n.A.y = field.HeightAt(n.A.xz); n.B.y = field.HeightAt(n.B.xz);
                n.C.y = field.HeightAt(n.C.xz); n.D.y = field.HeightAt(n.D.xz);
            }
            foreach (var s in plan.Surfaces)
                for (int i = 0; i < s.Ring.Length; i++) s.Ring[i].y = field.HeightAt(s.Ring[i].xz);
            foreach (var r in plan.Regions)
                for (int i = 0; i < r.Ring.Length; i++) r.Ring[i].y = field.HeightAt(r.Ring[i].xz);
        }
    }
}
