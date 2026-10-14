// Base for the round-4 leisure types (botanical garden, sports park, dog park, picnic grounds,
// nature reserve, formal garden, community garden, market square…). Each type is a Fill layer with
// its own planner; this base adds what they share: entrances, the common feature list (shops on the
// way, shops along the street, restrooms, decals, terrain, rocks…), "core"/"green" regions for
// features, and a few helpers. Ids contain "garden", "park", "reserve", "grounds", "square" so the
// UI groups their Make cards (Parks / Squares).

using System.Collections.Generic;
using Placemaker.Core.Layers.Common;
using Placemaker.Core.Layers.Park;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Leisure
{
    public abstract class LeisureLayer : LayerBase, IHasFeatures
    {
        public const string EntranceKind = ParkLayer.EntranceKind;

        public override LayerKind Kind => LayerKind.Fill;
        public override string[] Emits => new[] { RegionTags.Green, RegionTags.Core, StreetFrontage.ShopsTag };
        public override float MinArea => 400f;
        protected override bool HostsStreetShops => true;

        private IReadOnlyList<FeatureInfo> _features;
        public IReadOnlyList<FeatureInfo> Features => _features ?? (_features = BuildFeatures().AsReadOnly());

        /// <summary>The type's features; essentials first. Default: restrooms, shops, street shops, decals, terrain.</summary>
        protected virtual List<FeatureInfo> BuildFeatures() => new List<FeatureInfo>
        {
            Feature("restrooms"), Feature("shops", ("count", 2.0)), StreetShopsFeature(Area), Feature("decals"), Feature("terrain", ("shape", "mounds")),
        };

        protected static FeatureInfo Feature(string layer, params (string key, object value)[] preset) => new FeatureInfo
        {
            Layer = layer, LabelKey = "Placemaker.Feature." + layer,
            TooltipKey = layer == "shops" || layer == "terrain" ? "Placemaker.Feature." + layer + ".Tooltip" : "",
            Preset = ParkLayer.Bag(preset),
        };

        protected static FeatureInfo CoreFeature(string layer, params (string key, object value)[] preset)
        {
            var f = Feature(layer, preset);
            f.Target = RegionTags.Core;
            return f;
        }

        protected virtual int WantedEntrances(LayerRegion region) => math.min(3, Entrances.WantedByArea(region.Area));

        public override List<EdgeMarker> SuggestMarkers(AreaInput input)
        {
            var region = LayerRegion.From(input?.Shape?.Ring);
            return region == null ? new List<EdgeMarker>() : Entrances.Suggest(input, region, EntranceKind, WantedEntrances(region));
        }

        public override Readiness CheckReady(AreaInput input) => new Readiness { Ready = true };

        protected List<Gate> Gates(LayerContext ctx) => Entrances.Resolve(ctx.Input, ctx.LayerRegion, EntranceKind, WantedEntrances(ctx.LayerRegion));

        /// <summary>Lawns between everything taken (FreeSpace), largest tagged core; whole region when nothing is free.</summary>
        protected static void EmitGreenRegions(LayerContext ctx, Occupancy occ, float minArea = 150f)
        {
            var region = ctx.LayerRegion;
            var pieces = FreeSpace.Pieces(region, p => occ.Corridors.Overlaps(p, 0.75f) || occ.HitsPolygon(p, 0.5f), 2.5f, minArea, ctx.Cancel);
            if (pieces.Count == 0) { ctx.AddRegion(region.Ring, "green:0", RegionTags.Green, RegionTags.Core); return; }
            for (int i = 0; i < pieces.Count && i < 24; i++)
                if (i == 0) ctx.AddRegion(pieces[i], "green:0", RegionTags.Green, RegionTags.Core);
                else ctx.AddRegion(pieces[i], "green:" + i, RegionTags.Green);
        }

        /// <summary>Straight path from each gate to the nearest point of the given spine segments (when it stays inside).</summary>
        protected static void ConnectGates(LayerContext ctx, Occupancy occ, List<Gate> gates, List<(float2 a, float2 b)> spine, NetRole role, AssetRef asset)
        {
            foreach (var g in gates)
            {
                float best = float.MaxValue; float2 foot = g.Position;
                foreach (var (a, b) in spine)
                {
                    var q = SegmentIndex.ClosestPoint(g.Position, a, b);
                    float d = math.distance(q, g.Position);
                    if (d < best && !ctx.LayerRegion.CrossesBoundary(g.Position, q, true)) { best = d; foot = q; }
                }
                if (best < float.MaxValue && best > 1f) LeisureKit.Path(ctx, occ, g.Position, foot, role, asset);
            }
        }

        /// <summary>Benches beside the paths every <paramref name="interval"/> m (spread), facing the path.</summary>
        protected static int PathBenches(LayerContext ctx, Occupancy occ, float interval, AssetRef bench, uint salt)
        {
            if (interval <= 0f || occ.Paths.Count == 0) return 0;
            float total = 0f;
            foreach (var s in occ.Paths.Segments) total += math.distance(s.A, s.B);
            int wanted = math.clamp((int)(total / interval), 0, 200);
            var picks = PathAnchors.Pick(PathAnchors.Beside(occ, 1.3f, 6f, 0.4f), wanted, interval * 0.6f, false);
            int n = 0;
            foreach (var a in picks)
                if (LeisureKit.Put(ctx, occ, a.Position, LeisureKit.Yaw(a.Position, a.Toward), ObjectRole.Bench, bench, null, 1.3f, Hashing.Mix(ctx.Seed, salt + (uint)n))) n++;
            return n;
        }

        /// <summary>Lamps beside the paths every <paramref name="interval"/> m.</summary>
        protected static int PathLamps(LayerContext ctx, Occupancy occ, float interval, AssetRef lamp, uint salt)
        {
            int n = 0;
            if (interval <= 0f) return 0;
            foreach (var s in new List<SegmentIndex.Seg>(occ.Paths.Segments))
            {
                var dir = math.normalizesafe(s.B - s.A);
                var nrm = new float2(-dir.y, dir.x);
                foreach (var p in LeisureKit.Along(s.A, s.B, interval))
                {
                    var q = p + nrm * ((n & 1) == 0 ? 1f : -1f) * (s.HalfWidth + 0.8f);
                    if (LeisureKit.Put(ctx, occ, q, 0f, ObjectRole.Lamp, lamp, null, 0.5f, Hashing.Mix(ctx.Seed, salt + (uint)n), 0.4f, 0.2f)) n++;
                }
            }
            return n;
        }

        /// <summary>
        /// Natural path network (ParkManager planner) with meandering bends, emitted and indexed as
        /// corridors and walkable paths. Returns the curves.
        /// </summary>
        protected static List<PathCurve> NaturalPaths(LayerContext ctx, Occupancy occ, List<Gate> gates, int loops, float amplitude, float wavelength,
            string width, AssetRef asset, bool stretch = false)
        {
            var region = ctx.LayerRegion;
            var pts = LeisureKit.Points(gates);
            uint seed = Hashing.Mix(ctx.Seed, 0x9A75u);
            var graph = ParkPathPlanner.PlanBest(region, pts, new ParkPathPlanner.Options { Loops = loops, StretchLoop = stretch }, seed, ctx.Cancel);
            if (amplitude > 0f) graph = ParkPathStyles.Meander(graph, pts, amplitude, wavelength, seed, p => region.ContainsDisc(p, 3f) && !occ.HitsPolygon(p, 2f));
            var curves = ParkPaths.Build(graph, region, true, width);
            ParkPaths.Index(curves, occ.Corridors, 100);
            ParkPaths.Index(curves, occ.Paths, 100);
            ParkPaths.Emit(ctx, curves, gates, asset);
            return curves;
        }

        protected static void Fence(LayerContext ctx, List<Gate> gates, AssetRef asset, float gap = 3.75f)
        {
            foreach (var run in Fences.Runs(ctx.LayerRegion, gates, gap)) ctx.StraightNet(run.c0, run.c1, NetRole.Fence, asset);
        }

        protected static int Count(AreaPlan plan, ObjectRole role)
        {
            int n = 0;
            foreach (var o in plan.Objects) if (o.Role == role) n++;
            return n;
        }

        protected static void CommonStats(LayerContext ctx)
        {
            ctx.Stat(Keys.StatArea, System.Math.Round(ctx.LayerRegion.Area), Keys.UnitM2);
            ctx.Stat(Keys.StatTrees, Count(ctx.Plan, ObjectRole.Tree), Keys.UnitCount);
            ctx.Stat(Keys.StatBenches, Count(ctx.Plan, ObjectRole.Bench), Keys.UnitCount);
            float len = 0f;
            foreach (var n in ctx.Plan.Nets) if (n.Role == NetRole.Pathway || n.Role == NetRole.PathwayWide) len += math.distance(n.A.xz, n.D.xz);
            ctx.Stat(Keys.StatPathLength, System.Math.Round(len), Keys.UnitM);
        }
    }
}
