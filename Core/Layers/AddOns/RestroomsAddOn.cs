// Restrooms add-on: one or more restroom buildings beside paths (near where people enter),
// spread apart, facing the path; optional kiosk next to each. Without paths in the region they
// go near the outline (street frontage) instead.

using System.Collections.Generic;
using Placemaker.Core.Layers.Common;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.AddOns
{
    [AreaGenerator(120)]
    public sealed class RestroomsAddOn : AddOnLayer
    {
        protected override string Area => "AddOn.Restrooms";
        public override string Id => "restrooms";
        public override string Icon => "Media/Game/Icons/Sewage.svg";

        protected override void DefineControls(ControlSet c)
        {
            c.Slider("count", 1, 1, 4, 1, "count", ControlTier.Essential, "restrooms");
            c.Asset("asset", AssetCategories.Restroom, ControlTier.Essential, "restrooms");
            c.Toggle("kiosk", false, ControlTier.More, "extras").Tip();
            c.Asset("kioskAsset", AssetCategories.Kiosk, ControlTier.More, "extras").When("kiosk=true");
            c.RandomRange("count", 1, 2);
            c.Chance("kiosk", 0.4f);
        }

        protected override void PlanCore(LayerContext ctx)
        {
            if (!AssetAvailability.Require(ctx.Plan, AssetCategories.Restroom, new[] { ctx.S("asset") }, Keys.Category(AssetCategories.Restroom)))
                return;
            var occ = Occupancy(ctx);
            var asset = AssetMix.One(ctx.S("asset"));
            float radius = AssetMix.FootprintRadius(ctx.Site, asset, 4f);
            bool kiosk = ctx.B("kiosk") && AssetAvailability.Require(ctx.Plan, AssetCategories.Kiosk, new[] { ctx.S("kioskAsset") }, Keys.Category(AssetCategories.Kiosk));
            var kioskAsset = AssetMix.One(ctx.S("kioskAsset"));
            float kioskRadius = AssetMix.FootprintRadius(ctx.Site, kioskAsset, 2.5f);
            int wanted = ctx.I("count");
            float minDistance = math.max(30f, math.sqrt(ctx.LayerRegion.Area) / (wanted + 1));
            var pref = occ.Paths.Count > 0 ? Spots.Preference.NearPaths : Spots.Preference.NearBoundary;
            var spots = Spread(occ, radius + (kiosk ? kioskRadius * 2f + 0.5f : 0f), wanted, pref, ctx.Seed, minDistance);
            int placed = 0;
            foreach (var spot in spots)
            {
                // Face the nearest path (or the region centre): the door should open to where people walk.
                var toward = occ.Paths.Count > 0 && occ.Paths.Nearest(spot, 500f, out var q) >= 0 ? q : ctx.LayerRegion.Centroid;
                float yaw = Facing(spot, toward);
                if (!occ.Free(spot, radius, 0.5f, 0.3f)) continue;
                ctx.Object(spot, yaw, ObjectRole.Furniture, asset, Hashing.Mix(ctx.Seed, (uint)placed), -1, AssetCategories.Restroom);
                occ.Take(spot, radius, 0);
                placed++;
                if (!kiosk) continue;
                var side = new float2(math.cos(yaw), -math.sin(yaw));   // perpendicular to the facing direction
                foreach (float s in new[] { 1f, -1f })
                {
                    var k = spot + side * s * (radius + kioskRadius + 0.5f);
                    if (!occ.Free(k, kioskRadius, 0.5f, 0.3f)) continue;
                    ctx.Object(k, yaw, ObjectRole.Furniture, kioskAsset, Hashing.Mix(ctx.Seed, 0x4B00u + (uint)placed), -1, AssetCategories.Kiosk);
                    occ.Take(k, kioskRadius, 1);
                    break;
                }
            }
            if (placed == 0) NoSpace(ctx, "Placemaker.AddOn.Restrooms.Name");
            else if (placed < wanted) ctx.Issue(IssueSeverity.Info, "Placemaker.AddOn.Restrooms.Issue.Fewer", placed, wanted);
            ctx.Stat(Keys.StatRestrooms, placed, Keys.UnitCount);
        }
    }
}
