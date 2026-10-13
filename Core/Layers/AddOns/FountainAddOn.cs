// Fountain add-on: a featured centerpiece with a paved apron and a ring of benches/lamps,
// placed at the most central free spot of its region. "Bigger fountain" = the size control:
// a larger footprint reserve, apron and seating ring (and, for explicit assets, the asset's own
// bounds when larger).

using System;
using Placemaker.Core.Layers.Common;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.AddOns
{
    [AreaGenerator(100)]
    public sealed class FountainAddOn : AddOnLayer
    {
        protected override string Area => "AddOn.Fountain";
        public override string Id => "fountain";
        public override string Icon => "Media/Game/Icons/Water.svg";

        private static readonly string[] Sizes = { "small", "medium", "large", "grand" };
        private static float BasinRadius(string size) => size == "small" ? 3f : size == "large" ? 8f : size == "grand" ? 12f : 5f;

        protected override void DefineControls(ControlSet c)
        {
            c.Choice("size", "medium", ControlTier.Essential, "", Sizes);
            c.Asset("asset", AssetCategories.Centerpiece, ControlTier.Essential);
            c.Toggle("apron", true, ControlTier.More, "ground").Tip();
            c.Asset("apronSurface", AssetCategories.SurfacePaving, ControlTier.More, "ground").When("apron=true");
            c.Slider("benches", 6, 0, 16, 1, "count", ControlTier.More, "seating");
            c.Asset("benchAsset", AssetCategories.Bench, ControlTier.More, "seating");
            c.Toggle("lamps", true, ControlTier.More, "seating");
            c.Asset("lampAsset", AssetCategories.Lamp, ControlTier.More, "seating").When("lamps=true");
            c.Toggle("planters", false, ControlTier.More, "seating");
            c.Asset("planterAsset", AssetCategories.Planter, ControlTier.More, "seating").When("planters=true");
            c.Chance("apron", 0.85f);
            c.Chance("lamps", 0.8f);
            c.Chance("planters", 0.35f);
            c.RandomRange("benches", 2, 10);
        }

        protected override void DefinePresets()
        {
            Preset("simple", ("size", "small"), ("benches", 4.0), ("lamps", false));
            Preset("featured", ("size", "large"), ("benches", 8.0), ("planters", true));
            Preset("grand", ("size", "grand"), ("benches", 12.0), ("planters", true));
        }

        protected override void PlanCore(LayerContext ctx)
        {
            var asset = AssetMix.One(ctx.S("asset"));
            if (!AssetAvailability.Require(ctx.Plan, AssetCategories.Centerpiece, new[] { ctx.S("asset") }, Keys.Category(AssetCategories.Centerpiece)))
                return;
            var occ = Occupancy(ctx);
            int sizeIndex = Math.Max(0, Array.IndexOf(Sizes, ctx.S("size")));
            int benches = ctx.I("benches");
            bool lamps = ctx.B("lamps"), planters = ctx.B("planters");
            // Try the requested size, then smaller ones: a smaller fountain beats none.
            for (int si = sizeIndex; si >= 0; si--)
            {
                float basin = math.max(BasinRadius(Sizes[si]), AssetMix.FootprintRadius(ctx.Site, asset, 0f));
                bool seating = benches > 0 || lamps || planters;
                float apron = basin + (seating ? 4.5f : 2f);
                if (!Spots.Find(occ, apron, Spots.Preference.Centre, ctx.Seed, out var spot)) continue;
                if (si != sizeIndex) ctx.Issue(IssueSeverity.Info, "Placemaker.AddOn.Fountain.Issue.Smaller");
                Build(ctx, occ, spot, basin, apron, asset, benches, lamps, planters);
                return;
            }
            NoSpace(ctx, "Placemaker.AddOn.Fountain.Name");
        }

        private static void Build(LayerContext ctx, Occupancy occ, float2 c, float basin, float apron, AssetRef asset,
            int benches, bool lamps, bool planters)
        {
            ctx.Object(c, Hashing.Unit(ctx.Seed) * 2f * math.PI, ObjectRole.Centerpiece, asset, Hashing.Mix(ctx.Seed, 0xF0u));
            occ.Take(c, basin, 0);
            if (ctx.B("apron")) ctx.Surface(Region.Circle(c, apron, 32), SurfaceRole.Paving, AssetMix.One(ctx.S("apronSurface")));

            float seatRing = apron - 1.6f;
            float phase = Hashing.Unit(Hashing.Mix(ctx.Seed, 0x5Eu)) * 2f * math.PI;
            var bench = AssetMix.One(ctx.S("benchAsset"));
            int placedBenches = 0;
            if (benches > 0)
                foreach (var p in RingPoints(c, seatRing, benches, phase))
                {
                    if (!occ.Free(p, 1.3f, 0.3f, 0.2f)) continue;
                    // Benches face the water.
                    ctx.Object(p, Facing(p, c), ObjectRole.Bench, bench, Hashing.Mix(ctx.Seed, (uint)placedBenches));
                    occ.Take(p, 1.3f, 1);
                    placedBenches++;
                }
            int between = math.max(benches, 4);
            float midPhase = phase + math.PI / between;
            if (lamps)
            {
                var lamp = AssetMix.One(ctx.S("lampAsset"));
                int i = 0;
                foreach (var p in RingPoints(c, seatRing + 0.6f, math.min(between, 8), midPhase))
                {
                    if (!occ.Free(p, 0.5f, 0.3f, 0.2f)) continue;
                    ctx.Object(p, 0f, ObjectRole.Lamp, lamp, Hashing.Mix(ctx.Seed, 0x1A00u + (uint)i++));
                    occ.Take(p, 0.5f, 2);
                }
            }
            if (planters)
            {
                var planter = AssetMix.One(ctx.S("planterAsset"));
                int i = 0;
                foreach (var p in RingPoints(c, basin + 1.6f, math.max(4, between), midPhase))
                {
                    if (!occ.Free(p, 0.8f, 0.3f, 0.2f)) continue;
                    ctx.Object(p, 0f, ObjectRole.Planter, planter, Hashing.Mix(ctx.Seed, 0x9100u + (uint)i++));
                    occ.Take(p, 0.8f, 3);
                }
            }
            ctx.Stat(Keys.StatFountains, 1, Keys.UnitCount);
            ctx.Stat(Keys.StatBenches, placedBenches, Keys.UnitCount);
            ctx.Plan.Facts["fountainRadius"] = basin;
        }
    }
}
