// Shops feature: kiosks, food stands and ice-cream carts on the way — beside the paths near the
// entrances first (where people arrive), then spread along the paths; in a paved square or a lot
// without paths, along the outline. Optional cafe seating beside each stand. Category kiosk
// (cafe seating: seating.cafe, else picnic tables, else benches).

using Placemaker.Core.Layers.Common;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.AddOns
{
    [AreaGenerator(126)]
    public sealed class ShopsAddOn : AddOnLayer
    {
        public const string LayerId = "shops";

        protected override string Area => "AddOn.Shops";
        public override string Id => LayerId;
        public override string Icon => "Media/Game/Icons/Commercial.svg";

        protected override void DefineControls(ControlSet c)
        {
            c.Slider("count", 3, 1, 10, 1, "count", ControlTier.Essential, "shops");
            c.Choice("where", "entrances", ControlTier.Essential, "shops", "entrances", "paths", "edges").Tip();
            c.AssetMulti("assets", AssetCategories.Kiosk, ControlTier.Essential, "shops");
            c.Toggle("seating", true, ControlTier.More, "shops").Tip();
            c.Asset("seatingAsset", AssetCategories.CafeSeating, ControlTier.More, "shops").When("seating=true");
            c.RandomRange("count", 1, 4);
            c.Chance("seating", 0.7f);
        }

        protected override void DefinePresets()
        {
            Preset("snacks", ("count", 2.0), ("where", "entrances"), ("seating", false));
            Preset("foodcourt", ("count", 6.0), ("where", "paths"), ("seating", true));
        }

        protected override void PlanCore(LayerContext ctx)
        {
            var ids = ctx.L("assets");
            if (!AssetAvailability.Require(ctx.Plan, AssetCategories.Kiosk, ids, Keys.Category(AssetCategories.Kiosk))) return;
            var occ = Occupancy(ctx);
            float radius = AssetMix.MaxFootprint(ctx.Site, ids, 2.5f);
            int wanted = ctx.I("count");
            string where = ctx.S("where");
            bool seating = ctx.B("seating");
            string seatCat = seating ? LeisureKit.Category(ctx, ctx.S("seatingAsset"), AssetCategories.CafeSeating, AssetCategories.Picnic, AssetCategories.Bench) : null;
            var seatAsset = AssetMix.One(ctx.S("seatingAsset"));
            float minDistance = math.max(14f, math.sqrt(ctx.LayerRegion.Area) / (wanted + 2));

            var anchors = where == "edges" ? PathAnchors.Beside(new Occupancy(ctx.LayerRegion, null, ctx.Site), radius, 6f) : PathAnchors.Beside(occ, radius, 5f);
            if (where == "edges")
            {
                // Edge anchors from a path-less occupancy must still be free in the real one.
                anchors.RemoveAll(a => !occ.Free(a.Position, radius, 0.5f, 0.3f));
            }
            var picks = PathAnchors.Pick(anchors, wanted, minDistance, where == "entrances");
            int placed = 0, seats = 0;
            foreach (var a in picks)
            {
                uint v = Hashing.Mix(ctx.Seed, (uint)placed + 1u);
                float yaw = LeisureKit.Yaw(a.Position, a.Toward);
                if (!LeisureKit.Put(ctx, occ, a.Position, yaw, ObjectRole.Furniture, AssetMix.Pick(ids, v), AssetCategories.Kiosk, radius, v, 0.5f, 0.3f, 0)) continue;
                placed++;
                if (seatCat == null) continue;
                // Two seats beside the stand, along the path, facing it.
                for (int k = -1; k <= 1; k += 2)
                {
                    var p = a.Position + a.Tangent * k * (radius + 2.2f);
                    var role = seatCat == AssetCategories.Bench ? ObjectRole.Bench : ObjectRole.Furniture;
                    if (LeisureKit.Put(ctx, occ, p, LeisureKit.Yaw(p, a.Toward), role, seatAsset, seatCat, 1.4f, Hashing.Mix(v, (uint)(k + 3)))) seats++;
                }
            }
            if (placed == 0) NoSpace(ctx, "Placemaker.AddOn.Shops.Name");
            else if (placed < wanted) ctx.Issue(IssueSeverity.Info, "Placemaker.AddOn.Shops.Issue.Fewer", placed, wanted);
            ctx.Stat("Placemaker.Stat.Kiosks", placed, Keys.UnitCount);
            ctx.Stat("Placemaker.Stat.Seats", seats, Keys.UnitCount);
        }
    }
}
