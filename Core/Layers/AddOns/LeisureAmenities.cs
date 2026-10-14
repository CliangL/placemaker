// Leisure amenities: outdoor chess, splash pad, bandstand / gazebo, dog run, community noticeboard.

using System;
using System.Collections.Generic;
using Placemaker.Core.Layers.Common;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.AddOns
{
    [AreaGenerator(164)]
    public sealed class ChessAddOn : AmenityAddOn
    {
        protected override string Area => "AddOn.Chess";
        public override string Id => "chess";

        protected override void DefineControls(ControlSet c)
        {
            c.Slider("tables", 4, 1, 8, 1, "count", ControlTier.Essential, "chess");
            c.Asset("tableAsset", AssetCategories.GamesChess, ControlTier.Essential, "chess");
            c.Toggle("giant", false, ControlTier.Essential, "chess").Tip();
            c.Toggle("paving", true, ControlTier.More, "ground");
            c.Asset("pavingAsset", AssetCategories.SurfacePaving, ControlTier.More, "ground").When("paving=true");
            c.Toggle("shade", true, ControlTier.More, "extras").Tip();
            c.Asset("shelterAsset", AssetCategories.Shelter, ControlTier.More, "extras").When("shade=true");
            c.Toggle("lamps", true, ControlTier.More, "extras");
            c.RandomRange("tables", 2, 6);
            c.Chance("giant", 0.3f);
        }

        protected override void DefinePresets()
        {
            Preset("corner", ("tables", 4.0), ("giant", false), ("shade", true));
            Preset("giant", ("tables", 2.0), ("giant", true), ("shade", false));
            Preset("club", ("tables", 8.0), ("giant", false), ("shade", true));
        }

        protected override void Build(LayerContext ctx, Occupancy occ, OrientedRect site, float2 toward)
        {
            string cat = LeisureKit.Category(ctx, ctx.S("tableAsset"), AssetCategories.GamesChess, AssetCategories.CafeSeating);
            bool giant = ctx.B("giant");
            if (cat == null && !giant) return;
            if (ctx.B("paving"))
                Pad(ctx, LayerRegion.Rect(site.Center, site.Axis, site.HalfLength - 0.5f, site.HalfWidth - 0.5f), SurfaceRole.Paving, ctx.S("pavingAsset"), AssetCategories.SurfacePaving);
            float hl = site.HalfLength - 1.2f, hw = site.HalfWidth - 1.2f;
            float tableEnd = hl;
            if (giant)
            {
                // A 5 × 5 m board (decorative paving square) at the end away from the path; the
                // players' benches face it from the open side.
                float e = -EndSign(site, toward);
                float half = math.min(2.5f, hw - 0.3f);
                var board = Sub(site, new float2(e * (hl - half), 0f), half, half);
                ctx.Surface(board.Ring, SurfaceRole.Decorative, new AssetRef(""), AssetCategories.SurfacePaving);
                LeisureKit.TakeRect(occ, board, 2);
                ctx.Plan.Facts["giantBoard"] = 1;
                tableEnd = hl - half * 2f - 1.5f;
                var bench = AssetMix.One(ctx.S("benchAsset"));
                for (int k = -1; k <= 1; k += 2)
                {
                    var p = site.Local(e * (hl - half), k * (half + 1.4f));
                    if (k * (half + 1.4f) > hw + 0.6f || -k * (half + 1.4f) > hw + 0.6f) continue;
                    Put(ctx, occ, p, LeisureKit.Yaw(p, board.Center), ObjectRole.Bench, bench, null, 1.2f, Hashing.Mix(ctx.Seed, 0xB0Au + (uint)(k + 1)));
                }
            }
            int placed = 0;
            if (cat != null)
            {
                // Tables (with their seats) on a 3.2 m grid in the remaining part, centred.
                const float pitch = 3.2f;
                float spanU = giant ? (tableEnd + hl) : hl * 2f;
                float u0 = giant ? (-EndSign(site, toward) > 0 ? -hl : hl - spanU) : -hl;
                int cols = math.max(1, (int)math.floor(spanU / pitch) + 1), rows = math.max(1, (int)math.floor(hw * 2f / pitch) + 1);
                int n = math.min(ctx.I("tables"), cols * rows);
                rows = math.min(rows, (n + cols - 1) / cols);
                var asset = AssetMix.One(ctx.S("tableAsset"));
                for (int i = 0; i < n; i++)
                {
                    int cx = i % cols, cy = i / cols;
                    int inRow = math.min(cols, n - cy * cols);
                    float u = u0 + spanU * 0.5f + (cx - (inRow - 1) * 0.5f) * pitch, v = (cy - (rows - 1) * 0.5f) * pitch;
                    if (Put(ctx, occ, site.Local(u, v), site.Yaw, ObjectRole.Furniture, asset, cat, 1.1f, Hashing.Mix(ctx.Seed, (uint)i + 1u))) placed++;
                }
            }
            if (ctx.B("shade"))
            {
                // A shelter over the tables when one fits, else two shade trees on the far long side.
                string shelterCat = string.IsNullOrEmpty(ctx.S("shelterAsset")) && !AssetAvailability.Has(AssetCategories.Shelter) ? null : AssetCategories.Shelter;
                float side = -SideSign(site, toward);
                bool shaded = false;
                if (shelterCat != null)
                {
                    var p = site.Local(0f, side * (site.HalfWidth - 2.2f));
                    shaded = Put(ctx, occ, p, site.Yaw, ObjectRole.Furniture, AssetMix.One(ctx.S("shelterAsset")), shelterCat, 2f, Hashing.Mix(ctx.Seed, 0x5E1u));
                }
                if (!shaded && AssetAvailability.Has(AssetCategories.Tree))
                    for (int k = -1; k <= 1; k += 2)
                        Put(ctx, occ, site.Local(k * (site.HalfLength - 2.5f), side * (site.HalfWidth - 2.5f)), 0f, ObjectRole.Tree, new AssetRef(""), null, 2.4f, Hashing.Mix(ctx.Seed, 0x7E0u + (uint)(k + 1)));
            }
            if (ctx.B("lamps"))
                for (int k = -1; k <= 1; k += 2)
                    Put(ctx, occ, site.Local(k * (site.HalfLength - 0.9f), SideSign(site, toward) * (site.HalfWidth - 0.9f)), 0f, ObjectRole.Lamp, new AssetRef(""), null, 0.5f, Hashing.Mix(ctx.Seed, 0x1A0u + (uint)(k + 1)));
            ctx.Stat("Placemaker.Stat.GameTables", placed, Keys.UnitCount);
        }
    }

    [AreaGenerator(166)]
    public sealed class SplashPadAddOn : AmenityAddOn
    {
        protected override string Area => "AddOn.SplashPad";
        public override string Id => "splashPad";

        private static readonly string[] Sizes = { "small", "medium", "large" };

        protected override void DefineControls(ControlSet c)
        {
            c.Choice("size", "medium", ControlTier.Essential, "splashPad", Sizes);
            c.Slider("jets", 10, 3, 24, 1, "count", ControlTier.Essential, "splashPad");
            c.Asset("jetAsset", AssetCategories.WaterJets, ControlTier.More, "splashPad");
            c.Asset("padAsset", AssetCategories.SurfacePaving, ControlTier.More, "ground");
            c.Toggle("benches", true, ControlTier.More, "extras");
            c.Asset("benchAsset", AssetCategories.Bench, ControlTier.More, "extras").When("benches=true");
            c.Toggle("shade", true, ControlTier.More, "extras").Tip();
            c.RandomRange("jets", 6, 16);
        }

        protected override void DefinePresets()
        {
            Preset("toddler", ("size", "small"), ("jets", 6.0), ("benches", true), ("shade", true));
            Preset("plaza", ("size", "large"), ("jets", 18.0), ("benches", true), ("shade", false));
        }

        protected override void Build(LayerContext ctx, Occupancy occ, OrientedRect site, float2 toward)
        {
            string jetCat = LeisureKit.Category(ctx, ctx.S("jetAsset"), AssetCategories.WaterJets, AssetCategories.CenterpieceFountain);
            if (jetCat == null) return;
            int si = math.max(0, Array.IndexOf(Sizes, ctx.S("size")));
            bool benches = ctx.B("benches");
            float room = math.min(site.HalfLength, site.HalfWidth) - (benches ? 2.6f : 0.8f);
            float r = math.min(si == 0 ? 4f : si == 1 ? 5.5f : 7f, room);
            if (r < 2.5f) { NoSpace(ctx, NameKey); return; }
            ctx.Surface(Disc(site.Center, r), SurfaceRole.Decorative, AssetMix.One(ctx.S("padAsset")), AssetCategories.SurfacePaving);
            // Jets in a sunflower pattern: even coverage, a dense middle, nothing on the rim (splash zone).
            int wanted = ctx.I("jets");
            var asset = AssetMix.One(ctx.S("jetAsset"));
            int placed = 0;
            float inner = r - 0.9f;
            float spacing = math.max(1.1f, inner / math.sqrt(math.max(1, wanted)) * 1.6f);
            const float golden = 2.39996323f;
            for (int i = 0; i < wanted * 3 && placed < wanted; i++)
            {
                float rr = spacing * 0.55f * math.sqrt(i + 0.5f);
                if (rr > inner) break;
                var p = site.Center + new float2(math.cos(i * golden), math.sin(i * golden)) * rr;
                if (Put(ctx, occ, p, 0f, ObjectRole.Furniture, asset, jetCat, 0.35f, Hashing.Mix(ctx.Seed, (uint)i + 11u))) placed++;
            }
            int seats = 0;
            if (benches)
            {
                // Parents' benches round the rim, leaving the side people arrive from open.
                var bench = AssetMix.One(ctx.S("benchAsset"));
                float br = r + 1.5f;
                int n = math.clamp((int)math.floor(2f * math.PI * br / 5.5f), 3, 10);
                float gapAngle = math.atan2(toward.y - site.Center.y, toward.x - site.Center.x);
                for (int i = 0; i < n; i++)
                {
                    float a = gapAngle + (i + 0.5f) * 2f * math.PI / (n + 1) + math.PI * 2f / (n + 1) * 0.5f;
                    var p = site.Center + new float2(math.cos(a), math.sin(a)) * br;
                    if (Put(ctx, occ, p, LeisureKit.Yaw(p, site.Center), ObjectRole.Bench, bench, null, 1.2f, Hashing.Mix(ctx.Seed, 0xBE0u + (uint)i))) seats++;
                }
            }
            if (ctx.B("shade") && AssetAvailability.Has(AssetCategories.Tree))
                for (int k = -1; k <= 1; k += 2)
                {
                    float side = -SideSign(site, toward);
                    Put(ctx, occ, site.Local(k * (site.HalfLength - 2.5f), side * (site.HalfWidth - 2.5f)), 0f, ObjectRole.Tree, new AssetRef(""), null, 2.4f, Hashing.Mix(ctx.Seed, 0x7E0u + (uint)(k + 1)));
                }
            ctx.Stat("Placemaker.Stat.Jets", placed, Keys.UnitCount);
            ctx.Stat(Keys.StatBenches, seats, Keys.UnitCount);
            ctx.Plan.Facts["padRadius"] = r;
        }
    }

    [AreaGenerator(167)]
    public sealed class BandstandAddOn : AmenityAddOn
    {
        protected override string Area => "AddOn.Bandstand";
        public override string Id => "bandstand";

        protected override void DefineControls(ControlSet c)
        {
            c.Choice("kind", "bandstand", ControlTier.Essential, "bandstand", "bandstand", "gazebo").Tip();
            c.Asset("asset", AssetCategories.StageBandstand, ControlTier.Essential, "bandstand");
            c.Slider("rows", 2, 0, 4, 1, "count", ControlTier.Essential, "seating").Tip();
            c.Asset("benchAsset", AssetCategories.Bench, ControlTier.More, "seating");
            c.Toggle("paving", true, ControlTier.More, "ground");
            c.Asset("pavingAsset", AssetCategories.SurfacePaving, ControlTier.More, "ground").When("paving=true");
            c.Toggle("lamps", true, ControlTier.More, "extras");
            c.Toggle("flowers", true, ControlTier.More, "extras").Tip();
            c.Fixed("kind");
        }

        protected override void DefinePresets()
        {
            Preset("victorian", ("kind", "bandstand"), ("rows", 3.0), ("lamps", true), ("flowers", true));
            Preset("gazebo", ("kind", "gazebo"), ("rows", 0.0), ("lamps", false), ("flowers", true));
            Preset("concert", ("kind", "bandstand"), ("rows", 4.0), ("lamps", true), ("flowers", false));
        }

        protected override void Build(LayerContext ctx, Occupancy occ, OrientedRect site, float2 toward)
        {
            bool gazebo = ctx.S("kind") == "gazebo";
            string cat = gazebo
                ? LeisureKit.Category(ctx, ctx.S("asset"), AssetCategories.Shelter, AssetCategories.StageBandstand)
                : LeisureKit.Category(ctx, ctx.S("asset"), AssetCategories.StageBandstand, AssetCategories.Stage, AssetCategories.Shelter);
            if (cat == null) return;
            var asset = AssetMix.One(ctx.S("asset"));
            float sr = math.clamp(AssetMix.FootprintRadius(ctx.Site, asset, 4f), 2.5f, 6f);
            int rows = ctx.I("rows");
            // A bandstand plays to the lawn: it stands at the far end of the place, the audience in arcs
            // towards the arrival side. A gazebo is the centre of its own little garden.
            float2 focal; float2 facing;
            if (gazebo || rows == 0) { focal = site.Center; facing = math.normalizesafe(toward - site.Center, site.Axis); }
            else
            {
                float e = -EndSign(site, toward);
                focal = site.Local(e * (site.HalfLength - sr - 1.5f), 0f);
                facing = -e * site.Axis;
            }
            if (ctx.B("paving"))
                ctx.Surface(Disc(focal, sr + 1.5f), SurfaceRole.Paving, AssetMix.One(ctx.S("pavingAsset")), AssetCategories.SurfacePaving);
            if (!Put(ctx, occ, focal, math.atan2(facing.x, facing.y), ObjectRole.Furniture, asset, cat, sr, Hashing.Mix(ctx.Seed, 0xBA0u)))
            { NoSpace(ctx, NameKey); return; }
            int flowers = 0;
            if (ctx.B("flowers") && AssetAvailability.Has(AssetCategories.Flowerbed))
            {
                // A ring of beds round the plinth, open towards the audience / the path.
                float fr = sr + 2.6f;
                int n = math.clamp((int)math.floor(2f * math.PI * fr / 3.2f), 4, 14);
                float open = math.atan2(facing.y, facing.x);
                for (int i = 0; i < n; i++)
                {
                    float a = open + (i + 1) * 2f * math.PI / (n + 1);
                    var p = focal + new float2(math.cos(a), math.sin(a)) * fr;
                    if (math.abs(math.dot(math.normalizesafe(p - focal), facing)) > 0.92f) continue;
                    if (Put(ctx, occ, p, a, ObjectRole.Planter, new AssetRef(""), AssetCategories.Flowerbed, 1f, Hashing.Mix(ctx.Seed, 0xF10u + (uint)i))) flowers++;
                }
            }
            int benches = 0;
            var bench = AssetMix.One(ctx.S("benchAsset"));
            float baseAngle = math.atan2(facing.y, facing.x);
            for (int row = 0; row < rows; row++)
            {
                float r = sr + 5f + row * 3.2f;
                float half = gazebo ? math.PI : 1.05f;   // ±60° in front of a bandstand, all round a gazebo
                float step = 3.1f / r;
                for (float a = -half + step * 0.5f; a <= half; a += step)
                {
                    if (!gazebo && math.abs(a) < step * 0.6f) continue;   // central aisle
                    var p = focal + new float2(math.cos(baseAngle + a), math.sin(baseAngle + a)) * r;
                    if (Put(ctx, occ, p, LeisureKit.Yaw(p, focal), ObjectRole.Bench, bench, null, 1.2f, Hashing.Mix(ctx.Seed, (uint)(row * 64 + benches) + 0xBE00u))) benches++;
                }
            }
            if (ctx.B("lamps"))
                for (int k = -1; k <= 1; k += 2)
                {
                    var side = new float2(-facing.y, facing.x);
                    Put(ctx, occ, focal + side * k * (sr + 1.8f) + facing * 0.5f, 0f, ObjectRole.Lamp, new AssetRef(""), null, 0.5f, Hashing.Mix(ctx.Seed, 0x1A0u + (uint)(k + 1)));
                }
            ctx.Stat(Keys.StatBenches, benches, Keys.UnitCount);
            ctx.Stat(Keys.StatFlowerbeds, flowers, Keys.UnitCount);
            ctx.Plan.Facts["bandstand"] = 1;
        }
    }

    [AreaGenerator(168)]
    public sealed class DogRunAddOn : AmenityAddOn
    {
        protected override string Area => "AddOn.DogRun";
        public override string Id => "dogRun";

        protected override void DefineControls(ControlSet c)
        {
            c.Slider("agility", 4, 0, 8, 1, "count", ControlTier.Essential, "dogRun").Tip();
            c.Choice("ground", "grass", ControlTier.Essential, "dogRun", "grass", "dirt", "gravel");
            c.Toggle("smallDogs", false, ControlTier.Essential, "dogRun").Tip();
            c.AssetMulti("agilityAssets", AssetCategories.DogAgility, ControlTier.More, "dogRun");
            c.Toggle("fence", true, ControlTier.More, "boundary");
            c.Asset("fenceAsset", AssetCategories.Fence, ControlTier.More, "boundary").When("fence=true");
            c.Toggle("amenities", true, ControlTier.More, "extras").Tip();
            c.Toggle("benches", true, ControlTier.More, "extras");
            c.Asset("benchAsset", AssetCategories.Bench, ControlTier.More, "extras").When("benches=true");
            c.Fixed("fence");
            c.Chance("smallDogs", 0.3f);
        }

        protected override void DefinePresets()
        {
            Preset("agility", ("agility", 8.0), ("ground", "dirt"), ("smallDogs", false));
            Preset("simple", ("agility", 0.0), ("ground", "grass"), ("smallDogs", false));
            Preset("twoZones", ("agility", 3.0), ("ground", "gravel"), ("smallDogs", true));
        }

        protected override void Build(LayerContext ctx, Occupancy occ, OrientedRect site, float2 toward)
        {
            var run = new OrientedRect { Center = site.Center, Axis = site.Axis, HalfLength = site.HalfLength - 0.6f, HalfWidth = site.HalfWidth - 0.6f };
            if (run.HalfLength < 6f || run.HalfWidth < 4f) { NoSpace(ctx, NameKey); return; }
            switch (ctx.S("ground"))
            {
                case "dirt": Pad(ctx, LayerRegion.Rect(run.Center, run.Axis, run.HalfLength - 0.3f, run.HalfWidth - 0.3f), SurfaceRole.Ground, "", AssetCategories.SurfaceDirt, AssetCategories.SurfaceNatural); break;
                case "gravel": Pad(ctx, LayerRegion.Rect(run.Center, run.Axis, run.HalfLength - 0.3f, run.HalfWidth - 0.3f), SurfaceRole.Ground, "", AssetCategories.SurfaceNatural); break;
            }
            float2 gate = run.Local(0f, SideSign(run, toward) * run.HalfWidth);
            // The fence is a net course: keep everything inside it 1 m clear of the line.
            if (ctx.B("fence"))
            {
                var fence = AssetMix.One(ctx.S("fenceAsset"));
                gate = RectFence(ctx, run, toward, 2f, fence);
                for (int i = 0; i < 4; i++) occ.TakeCorridor(run.Ring[i], run.Ring[(i + 1) % 4], 0.6f, 3);
                if (ctx.B("smallDogs"))
                {
                    // A divider a third of the way along, with a 1.5 m gap: a small-dog paddock beside the big one.
                    float u = -EndSign(run, toward) * run.HalfLength / 3f;
                    var a = run.Local(u, -run.HalfWidth); var b = run.Local(u, run.HalfWidth);
                    var mid = run.Local(u, -SideSign(run, toward) * run.HalfWidth * 0.5f);
                    var dir = math.normalizesafe(b - a);
                    ctx.StraightNet(a, mid - dir * 0.75f, NetRole.Fence, fence);
                    ctx.StraightNet(mid + dir * 0.75f, b, NetRole.Fence, fence);
                    occ.TakeCorridor(a, b, 0.6f, 3);
                    ctx.Plan.Facts["dogZones"] = 2;
                }
            }
            var inward = math.normalizesafe(run.Center - gate);
            int extras = 0;
            if (ctx.B("amenities") && AssetAvailability.Has(AssetCategories.DogAmenity))
            {
                var side = new float2(-inward.y, inward.x);
                for (int k = -1; k <= 1; k += 2)
                    if (Put(ctx, occ, gate + inward * 1.6f + side * k * 2.2f, LeisureKit.Yaw(gate, run.Center), ObjectRole.Furniture, new AssetRef(""), AssetCategories.DogAmenity, 0.6f, Hashing.Mix(ctx.Seed, 0xD06u + (uint)(k + 1))))
                        extras++;
            }
            int benches = 0;
            if (ctx.B("benches"))
            {
                var side = new float2(-inward.y, inward.x);
                for (int k = -1; k <= 1; k += 2)
                {
                    var p = gate + inward * 3.4f + side * k * 5f;
                    if (Put(ctx, occ, p, LeisureKit.Yaw(p, run.Center), ObjectRole.Bench, AssetMix.One(ctx.S("benchAsset")), null, 1.2f, Hashing.Mix(ctx.Seed, 0xBE0u + (uint)(k + 1)))) benches++;
                }
            }
            int agility = 0;
            int wanted = ctx.I("agility");
            var ids = ctx.L("agilityAssets");
            if (wanted > 0 && AssetAvailability.Require(ctx.Plan, AssetCategories.DogAgility, ids, Keys.Category(AssetCategories.DogAgility)))
            {
                // A loose course: a jittered lattice in the big paddock, every other item turned across.
                var cands = new List<float2>();
                for (float u = -run.HalfLength + 3f; u <= run.HalfLength - 3f; u += 5f)
                    for (float v = -run.HalfWidth + 3f; v <= run.HalfWidth - 3f; v += 5f)
                    {
                        uint h = Hashing.Mix(ctx.Seed, (int)(u * 10), (int)(v * 10));
                        cands.Add(run.Local(u + (Hashing.Unit(h) - 0.5f) * 1.5f, v + (Hashing.Unit(Hashing.Mix(h, 1u)) - 0.5f) * 1.5f));
                    }
                cands.Sort((a, b) => Hashing.Mix(ctx.Seed, (int)(a.x * 7), (int)(a.y * 7)).CompareTo(Hashing.Mix(ctx.Seed, (int)(b.x * 7), (int)(b.y * 7))));
                foreach (var p in cands)
                {
                    if (agility >= wanted) break;
                    if (math.distance(p, gate) < 5f) continue;
                    uint v = Hashing.Mix(ctx.Seed, (uint)agility + 0xA61u);
                    if (Put(ctx, occ, p, run.Yaw + (agility & 1) * math.PI * 0.5f, ObjectRole.Furniture, AssetMix.Pick(ids, v), AssetCategories.DogAgility, 1.3f, v)) agility++;
                }
            }
            ctx.Stat("Placemaker.Stat.AgilityItems", agility, Keys.UnitCount);
            ctx.Stat(Keys.StatBenches, benches, Keys.UnitCount);
            ctx.Plan.Facts["dogAmenities"] = extras;
        }
    }

    [AreaGenerator(169)]
    public sealed class NoticeboardAddOn : AmenityAddOn
    {
        protected override string Area => "AddOn.Noticeboard";
        public override string Id => "noticeboard";

        protected override void DefineControls(ControlSet c)
        {
            c.Slider("boards", 1, 1, 3, 1, "count", ControlTier.Essential, "noticeboard");
            c.Asset("boardAsset", AssetCategories.SignNoticeboard, ControlTier.Essential, "noticeboard");
            c.Toggle("bench", true, ControlTier.More, "extras");
            c.Asset("benchAsset", AssetCategories.Bench, ControlTier.More, "extras").When("bench=true");
            c.Toggle("bin", true, ControlTier.More, "extras");
            c.Toggle("lamp", true, ControlTier.More, "extras");
            c.Toggle("paving", true, ControlTier.More, "ground");
        }

        protected override void DefinePresets()
        {
            Preset("community", ("boards", 2.0), ("bench", true), ("bin", true), ("lamp", true));
            Preset("trailhead", ("boards", 1.0), ("bench", false), ("bin", true), ("lamp", false));
        }

        protected override void Build(LayerContext ctx, Occupancy occ, OrientedRect site, float2 toward)
        {
            string cat = LeisureKit.Category(ctx, ctx.S("boardAsset"), AssetCategories.SignNoticeboard);
            if (cat == null) return;
            if (ctx.B("paving"))
                ctx.Surface(LayerRegion.Rect(site.Center, site.Axis, site.HalfLength - 0.3f, site.HalfWidth - 0.3f), SurfaceRole.Paving, new AssetRef(""), AssetCategories.SurfacePaving);
            // Boards in a row on the far side, reading towards the path.
            float side = SideSign(site, toward);
            int n = math.min(ctx.I("boards"), math.max(1, (int)math.floor(site.HalfLength * 2f / 1.8f) - 1));
            var asset = AssetMix.One(ctx.S("boardAsset"));
            int placed = 0;
            float yaw = math.atan2(site.Side.x * side, site.Side.y * side);
            for (int i = 0; i < n; i++)
            {
                var p = site.Local((i - (n - 1) * 0.5f) * 1.8f, -side * (site.HalfWidth - 1.1f));
                if (Put(ctx, occ, p, yaw, ObjectRole.Furniture, asset, cat, 0.7f, Hashing.Mix(ctx.Seed, (uint)i + 1u))) placed++;
            }
            float end = site.HalfLength - 0.9f;
            if (ctx.B("bench"))
                Put(ctx, occ, site.Local(end - 0.4f, side * 0.6f), yaw, ObjectRole.Bench, AssetMix.One(ctx.S("benchAsset")), null, 1.2f, Hashing.Mix(ctx.Seed, 0xBE0u));
            if (ctx.B("bin")) Put(ctx, occ, site.Local(-end, side * 0.4f), 0f, ObjectRole.Bin, new AssetRef(""), null, 0.5f, Hashing.Mix(ctx.Seed, 0xB1u));
            if (ctx.B("lamp")) Put(ctx, occ, site.Local(-end, -side * (site.HalfWidth - 0.7f)), 0f, ObjectRole.Lamp, new AssetRef(""), null, 0.45f, Hashing.Mix(ctx.Seed, 0x1Au));
            ctx.Stat("Placemaker.Stat.Noticeboards", placed, Keys.UnitCount);
        }
    }
}
