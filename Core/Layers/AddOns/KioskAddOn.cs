// Kiosk add-on: food stands / kiosks along the edges of a square or park, spread apart, facing
// the open middle, each with a couple of benches in front.

using Placemaker.Core.Layers.Common;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.AddOns
{
    [AreaGenerator(125)]
    public sealed class KioskAddOn : AddOnLayer
    {
        public const string LayerId = "kiosk";

        protected override string Area => "AddOn.Kiosk";
        public override string Id => LayerId;
        public override string Icon => "Media/Game/Icons/Commercial.svg";

        protected override void DefineControls(ControlSet c)
        {
            c.Slider("count", 2, 1, 6, 1, "count", ControlTier.Essential, "kiosks");
            c.Asset("asset", AssetCategories.Kiosk, ControlTier.Essential, "kiosks");
            c.Toggle("benches", true, ControlTier.More, "kiosks").Tip();
            c.Asset("benchAsset", AssetCategories.Bench, ControlTier.More, "kiosks").When("benches=true");
            c.RandomRange("count", 1, 3);
            c.Chance("benches", 0.7f);
        }

        protected override void PlanCore(LayerContext ctx)
        {
            if (!AssetAvailability.Require(ctx.Plan, AssetCategories.Kiosk, new[] { ctx.S("asset") }, Keys.Category(AssetCategories.Kiosk))) return;
            var occ = Occupancy(ctx);
            var asset = AssetMix.One(ctx.S("asset"));
            float radius = AssetMix.FootprintRadius(ctx.Site, asset, 2.5f);
            int wanted = ctx.I("count");
            bool benches = ctx.B("benches");
            float reserve = radius + (benches ? 3f : 0.5f);
            float minDistance = math.max(12f, math.sqrt(ctx.LayerRegion.Area) / (wanted + 1));
            var spots = Spread(occ, reserve, wanted, Spots.Preference.NearBoundary, ctx.Seed, minDistance);
            var centre = ctx.LayerRegion.Centroid;
            var bench = AssetMix.One(ctx.S("benchAsset"));
            int placed = 0, seats = 0;
            foreach (var p in spots)
            {
                if (!occ.Free(p, radius, 0.5f, 0.3f)) continue;
                float yaw = Facing(p, centre);
                uint v = Hashing.Mix(ctx.Seed, (uint)placed + 1u);
                ctx.Object(p, yaw, ObjectRole.Furniture, asset, v);
                occ.Take(p, radius, 0);
                placed++;
                if (!benches) continue;
                var fwd = math.normalizesafe(centre - p, new float2(0, 1));
                var side = new float2(-fwd.y, fwd.x);
                for (int k = -1; k <= 1; k += 2)
                {
                    var b = p + fwd * (radius + 2f) + side * (k * 1.8f);
                    if (!occ.Free(b, 1.2f, 0.3f, 0.2f)) continue;
                    ctx.Object(b, Facing(b, p), ObjectRole.Bench, bench, Hashing.Mix(v, (uint)(k + 5)));
                    occ.Take(b, 1.2f, 1);
                    seats++;
                }
            }
            if (placed == 0) NoSpace(ctx, "Placemaker.AddOn.Kiosk.Name");
            else if (placed < wanted) ctx.Issue(IssueSeverity.Info, "Placemaker.AddOn.Kiosk.Issue.Fewer", placed, wanted);
            ctx.Stat("Placemaker.Stat.Kiosks", placed, Keys.UnitCount);
            ctx.Stat(Keys.StatBenches, seats, Keys.UnitCount);
        }
    }
}
