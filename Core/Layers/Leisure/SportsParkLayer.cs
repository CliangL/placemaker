// Sports park: a straight promenade along the long axis with the entrances joined to it, and the
// facilities packed beside it — football pitches (grass surface, goals, optional stands and
// floodlights), basketball courts (asphalt, hoops) and tennis courts (asphalt, net, fenced cage).
// Line markings are decals. An outdoor gym line along the promenade, a changing hut near the main
// entrance, trees in what is left.
//
// Sizes include run-off: pitch full 68×46 / small 44×28, tennis 34×17, basketball 30×17 (m).

using System;
using System.Collections.Generic;
using Placemaker.Core.Layers.Common;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Leisure
{
    [AreaGenerator(51)]
    public sealed class SportsParkLayer : LeisureLayer
    {
        protected override string Area => "Sports";
        public override string Id => "sportsPark";
        public override string Icon => "Media/Game/Icons/SportsParks.svg";
        public override float MinArea => 1200f;

        protected override List<FeatureInfo> BuildFeatures() => new List<FeatureInfo>
        {
            Feature("restrooms"), Feature("shops", ("count", 2.0), ("where", "entrances")), CoreFeature("playground", ("size", "small")),
            StreetShopsFeature(Area), Feature("decals", ("theme", "cracks"), ("ground", "auto")), Feature("terrain", ("shape", "flatten")),
        };

        protected override void DefineControls(ControlSet c)
        {
            c.Slider("pitches", 1, 0, 4, 1, "count", ControlTier.Essential, "facilities");
            c.Slider("basketball", 2, 0, 8, 1, "count", ControlTier.Essential, "facilities");
            c.Slider("tennis", 2, 0, 8, 1, "count", ControlTier.Essential, "facilities");
            c.Toggle("fitness", true, ControlTier.Essential, "facilities").Tip();
            c.Choice("pitchSize", "small", ControlTier.More, "facilities", "small", "full");
            c.Toggle("stands", true, ControlTier.More, "facilities").Tip();
            c.Toggle("floodlights", true, ControlTier.More, "facilities");
            c.Toggle("markings", true, ControlTier.More, "facilities").Tip();
            c.Toggle("cages", true, ControlTier.More, "facilities").Tip();
            c.Toggle("changingHut", true, ControlTier.More, "facilities").Tip();
            c.Asset("courtSurface", AssetCategories.SurfaceAsphalt, ControlTier.More, "assets");
            c.Asset("pitchSurface", AssetCategories.SurfaceGrass, ControlTier.More, "assets");
            c.Asset("fenceAsset", AssetCategories.Fence, ControlTier.More, "assets");
            c.Asset("pathAsset", AssetCategories.Pathway, ControlTier.More, "assets");
            c.Asset("benchAsset", AssetCategories.Bench, ControlTier.More, "assets");
            c.Slider("treeDensity", 25, 0, 100, 5, "perHa", ControlTier.More, "planting");
            c.AssetMulti("treeSpecies", AssetCategories.Tree, ControlTier.More, "planting");
            c.Markers("entrances", ControlTier.More, EntranceKind);
            DefineStreetShopControls(c);
            c.RandomRange("pitches", 0, 2);
            c.RandomRange("basketball", 0, 3);
            c.RandomRange("tennis", 0, 3);
        }

        protected override void DefinePresets()
        {
            Preset("community", ("pitches", 1.0), ("basketball", 2.0), ("tennis", 1.0), ("pitchSize", "small"));
            Preset("football", ("pitches", 2.0), ("basketball", 0.0), ("tennis", 0.0), ("pitchSize", "full"), ("stands", true), ("floodlights", true));
            Preset("tennisClub", ("pitches", 0.0), ("basketball", 0.0), ("tennis", 6.0), ("fitness", false));
            Preset("streetball", ("pitches", 0.0), ("basketball", 4.0), ("tennis", 0.0), ("floodlights", true));
        }

        private enum Facility { Pitch, Tennis, Basketball }

        protected override void PlanCore(LayerContext ctx)
        {
            var region = ctx.LayerRegion;
            var occ = new Occupancy(region, ctx.Input.Reserved, ctx.Site);
            var gates = Gates(ctx);
            var pathAsset = AssetMix.One(ctx.S("pathAsset"));
            LeisureKit.Frame(region, out var centre, out var axis, out var side, out _, out float minor);

            // Promenade along the long axis (shifted to one third so one side holds the big pitches).
            var spine = new List<(float2 a, float2 b)>();
            var through = centre - side * (minor * 0.18f);
            if (!region.Contains(through)) through = centre;
            if (LeisureKit.Chord(region, through, axis, 3f, out var s0, out var s1))
            {
                LeisureKit.Path(ctx, occ, s0, s1, NetRole.PathwayWide, pathAsset);
                spine.Add((s0, s1));
            }
            ConnectGates(ctx, occ, gates, spine, NetRole.Pathway, pathAsset);
            ctx.Surface(region.Ring, SurfaceRole.Grass, AssetMix.One(""));
            ctx.Check();

            // Facilities, biggest first.
            bool full = ctx.S("pitchSize") == "full";
            var wanted = new List<(Facility f, int n, float l, float w)>
            {
                (Facility.Pitch, ctx.I("pitches"), full ? 68f : 44f, full ? 46f : 28f),
                (Facility.Tennis, ctx.I("tennis"), 34f, 17f),
                (Facility.Basketball, ctx.I("basketball"), 30f, 17f),
            };
            var placed = new List<(Facility f, OrientedRect r)>();
            foreach (var (f, n, l, w) in wanted)
            {
                if (n <= 0) continue;
                var rects = LeisureKit.Pack(region, occ, centre, axis, l, w, 4f, 4f, n, 1.5f);
                if (rects.Count < n)
                {
                    // Try across the axis too (narrow sites).
                    rects.AddRange(LeisureKit.Pack(region, occ, centre, axis, l, w, 4f, 4f, n - rects.Count, 1.5f, false, false));
                }
                foreach (var r in rects) placed.Add((f, r));
                if (rects.Count < n) ctx.Issue(IssueSeverity.Info, "Placemaker.Sports.Issue.Fewer", rects.Count, n);
            }
            ctx.Check();

            var fence = AssetMix.One(ctx.S("fenceAsset"));
            int equipment = 0;
            for (int i = 0; i < placed.Count; i++)
            {
                var (f, r) = placed[i];
                uint v = Hashing.Mix(ctx.Seed, 0x5A00u + (uint)i * 31u);
                // Playing surface: the real field inside the run-off.
                float fl = f == Facility.Pitch ? r.HalfLength - 3f : f == Facility.Tennis ? 11.9f : 14f;
                float fw = f == Facility.Pitch ? r.HalfWidth - 3f : f == Facility.Tennis ? 5.5f : 7.5f;
                var field = new OrientedRect { Center = r.Center, Axis = r.Axis, HalfLength = fl, HalfWidth = fw };
                if (f == Facility.Pitch) ctx.Surface(field.Ring, SurfaceRole.Grass, AssetMix.One(ctx.S("pitchSurface")), AssetCategories.SurfaceGrass);
                else ctx.Surface(r.Ring, SurfaceRole.Asphalt, AssetMix.One(ctx.S("courtSurface")), AssetCategories.SurfaceAsphalt);
                if (ctx.B("markings") && AssetAvailability.Has(AssetCategories.DecalMarkings))
                    foreach (var (u, w) in new[] { (0f, 0f), (-fl * 0.7f, 0f), (fl * 0.7f, 0f) })
                        ctx.Object(field.Local(u, w), r.Yaw, ObjectRole.Decal, AssetMix.One(""), Hashing.Mix(v, 77u), -1, AssetCategories.DecalMarkings);
                switch (f)
                {
                    case Facility.Pitch:
                    {
                        string goal = LeisureKit.Category(ctx, "", AssetCategories.SportsGoal, AssetCategories.Sports);
                        if (goal != null)
                            for (int e = -1; e <= 1; e += 2)
                            {
                                var p = field.Local(e * (fl - 0.5f), 0f);
                                ctx.Object(p, r.Yaw + (e > 0 ? math.PI : 0f), ObjectRole.Furniture, AssetMix.One(""), v, -1, goal);
                                equipment++;
                            }
                        if (ctx.B("stands"))
                        {
                            string stand = LeisureKit.Category(ctx, "", AssetCategories.SportsStand, AssetCategories.Bench);
                            if (stand != null)
                                for (float u = -fl * 0.5f; u <= fl * 0.5f + 0.1f; u += math.max(6f, fl * 0.5f))
                                {
                                    var p = r.Local(u, r.HalfWidth + 2.5f);
                                    var role = stand == AssetCategories.Bench ? ObjectRole.Bench : ObjectRole.Furniture;
                                    LeisureKit.Put(ctx, occ, p, LeisureKit.Yaw(p, r.Center), role, AssetMix.One(ctx.S("benchAsset")), stand, 1.6f, Hashing.Mix(v, (uint)(u + 100)));
                                }
                        }
                        break;
                    }
                    case Facility.Basketball:
                    {
                        string hoop = LeisureKit.Category(ctx, "", AssetCategories.SportsBasketball, AssetCategories.Sports);
                        if (hoop != null)
                            for (int e = -1; e <= 1; e += 2)
                            {
                                ctx.Object(field.Local(e * (fl - 0.3f), 0f), r.Yaw + (e > 0 ? math.PI : 0f), ObjectRole.Furniture, AssetMix.One(""), v, -1, hoop);
                                equipment++;
                            }
                        break;
                    }
                    default:
                    {
                        string net = LeisureKit.Category(ctx, "", AssetCategories.SportsTennis, AssetCategories.Sports);
                        if (net != null) { ctx.Object(r.Center, r.Yaw + math.PI * 0.5f, ObjectRole.Furniture, AssetMix.One(""), v, -1, net); equipment++; }
                        break;
                    }
                }
                // Cages round the courts (gate gap on the promenade side).
                if (f != Facility.Pitch && ctx.B("cages"))
                {
                    var ring = r.Ring;
                    for (int k = 0; k < 4; k++)
                    {
                        float2 a = ring[k], b = ring[(k + 1) % 4];
                        if (k == 3) { var mid = math.lerp(a, b, 0.5f); var d = math.normalizesafe(b - a); ctx.StraightNet(a, mid - d * 1.5f, NetRole.Fence, fence); ctx.StraightNet(mid + d * 1.5f, b, NetRole.Fence, fence); }
                        else ctx.StraightNet(a, b, NetRole.Fence, fence);
                    }
                }
                if (ctx.B("floodlights"))
                {
                    string light = LeisureKit.Category(ctx, "", AssetCategories.FloodLight, AssetCategories.Lamp);
                    if (light != null)
                        foreach (var (u, w) in new[] { (-1f, -1f), (1f, -1f), (1f, 1f), (-1f, 1f) })
                        {
                            var p = r.Local(u * (r.HalfLength + 1.2f), w * (r.HalfWidth + 1.2f));
                            LeisureKit.Put(ctx, occ, p, LeisureKit.Yaw(p, r.Center), ObjectRole.Lamp, AssetMix.One(""), light == AssetCategories.Lamp ? null : light, 0.6f,
                                Hashing.Mix(v, (uint)(u * 3 + w + 9)), 0.3f, 0.1f);
                        }
                }
            }
            ctx.Check();

            // Outdoor gym along the promenade, changing hut near the first entrance.
            if (ctx.B("fitness") && spine.Count > 0)
            {
                string gym = LeisureKit.Category(ctx, "", AssetCategories.SportsFitness, AssetCategories.Sports);
                if (gym != null)
                {
                    var anchors = PathAnchors.Beside(occ, 1.8f, 9f, 1f);
                    int n = 0;
                    foreach (var a in PathAnchors.Pick(anchors, 8, 7f, false))
                        if (LeisureKit.Put(ctx, occ, a.Position, LeisureKit.Yaw(a.Position, a.Toward), ObjectRole.Furniture, AssetMix.One(""), gym, 1.8f, Hashing.Mix(ctx.Seed, 0x6E00u + (uint)n))) n++;
                    equipment += n;
                }
            }
            if (ctx.B("changingHut") && !ctx.Input.HasChild("restrooms"))
            {
                string hut = LeisureKit.Category(ctx, "", AssetCategories.Restroom, AssetCategories.Shelter);
                if (hut != null)
                {
                    var picks = PathAnchors.Pick(PathAnchors.Beside(occ, 4f, 6f, 1.2f), 1, 10f, true);
                    foreach (var a in picks)
                        LeisureKit.Put(ctx, occ, a.Position, LeisureKit.Yaw(a.Position, a.Toward), ObjectRole.Furniture, AssetMix.One(""), hut, 4f, Hashing.Mix(ctx.Seed, 0x4C7u), 0.5f, 0.3f, 0);
                }
            }
            PathBenches(ctx, occ, 35f, AssetMix.One(ctx.S("benchAsset")), 0xBE50u);

            // Trees in what is left, mostly at the edges.
            int trees = (int)(region.Area / 10000f * ctx.F("treeDensity"));
            if (trees > 0 && AssetAvailability.Require(ctx.Plan, AssetCategories.Tree, ctx.L("treeSpecies"), Keys.Category(AssetCategories.Tree)))
                LeisureKit.Plant(ctx, occ, region, trees, ObjectRole.Tree, ctx.L("treeSpecies"), 2.5f, 18f, 0.5f, 0x7A00u, p => region.BoundaryDistance(p, 20f) > 14f);

            EmitGreenRegions(ctx, occ);
            CommonStats(ctx);
            ctx.Stat("Placemaker.Stat.Facilities", placed.Count, Keys.UnitCount);
            ctx.Stat("Placemaker.Stat.Equipment", equipment, Keys.UnitCount);
            ctx.Plan.Facts["facilities"] = placed.Count;
        }
    }
}
