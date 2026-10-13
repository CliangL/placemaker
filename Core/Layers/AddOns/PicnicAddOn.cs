// Picnic add-on: groups of picnic tables, each around an optional shelter with a few shade
// trees on the outside, spread over the open lawn.

using System.Collections.Generic;
using Placemaker.Core.Layers.Common;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.AddOns
{
    [AreaGenerator(130)]
    public sealed class PicnicAddOn : AddOnLayer
    {
        protected override string Area => "AddOn.Picnic";
        public override string Id => "picnic";
        public override string Icon => "Media/Game/Icons/ParksAndRecreation.svg";

        protected override void DefineControls(ControlSet c)
        {
            c.Slider("groups", 3, 1, 10, 1, "count", ControlTier.Essential);
            c.Slider("tables", 3, 1, 6, 1, "count", ControlTier.Essential).Tip();
            c.AssetMulti("tableAssets", AssetCategories.Picnic, ControlTier.More);
            c.Toggle("shelter", true, ControlTier.More);
            c.Asset("shelterAsset", AssetCategories.Shelter, ControlTier.More).When("shelter=true");
            c.Toggle("shadeTrees", true, ControlTier.More);
            c.AssetMulti("treeSpecies", AssetCategories.Tree, ControlTier.More).When("shadeTrees=true");
            c.RandomRange("groups", 1, 5);
            c.RandomRange("tables", 2, 5);
            c.Chance("shelter", 0.6f);
            c.Chance("shadeTrees", 0.8f);
        }

        protected override void PlanCore(LayerContext ctx)
        {
            var tableIds = ctx.L("tableAssets");
            if (!AssetAvailability.Require(ctx.Plan, AssetCategories.Picnic, tableIds, Keys.Category(AssetCategories.Picnic))) return;
            var occ = Occupancy(ctx);
            float tableRadius = AssetMix.MaxFootprint(ctx.Site, tableIds, 1.6f);
            bool shelter = ctx.B("shelter") && AssetAvailability.Require(ctx.Plan, AssetCategories.Shelter, new[] { ctx.S("shelterAsset") }, Keys.Category(AssetCategories.Shelter));
            var shelterAsset = AssetMix.One(ctx.S("shelterAsset"));
            float shelterRadius = shelter ? AssetMix.FootprintRadius(ctx.Site, shelterAsset, 4f) : 0f;
            int tables = ctx.I("tables");
            float ring = shelter ? shelterRadius + tableRadius + 1.2f : (tables > 1 ? tableRadius * 2.2f : 0f);
            float groupRadius = ring + tableRadius + (ctx.B("shadeTrees") ? 4f : 1f);
            var spots = Spread(occ, groupRadius, ctx.I("groups"), Spots.Preference.Open, ctx.Seed, groupRadius * 2.5f);
            int placedTables = 0, placedTrees = 0;
            var treeIds = ctx.L("treeSpecies");
            bool trees = ctx.B("shadeTrees") && AssetAvailability.Require(ctx.Plan, AssetCategories.Tree, treeIds, Keys.Category(AssetCategories.Tree));
            for (int g = 0; g < spots.Count; g++)
            {
                var c = spots[g];
                uint gs = Hashing.Mix(ctx.Seed, (uint)g + 1u);
                float phase = Hashing.Unit(gs) * 2f * math.PI;
                if (shelter && occ.Free(c, shelterRadius, 0.5f, 0.3f))
                {
                    ctx.Object(c, phase, ObjectRole.Furniture, shelterAsset, gs, -1, AssetCategories.Shelter);
                    occ.Take(c, shelterRadius, 0);
                }
                var positions = tables == 1 || ring <= 0f ? new List<float2> { c } : RingPoints(c, ring, tables, phase);
                for (int t = 0; t < positions.Count; t++)
                {
                    var p = positions[t];
                    if (!occ.Free(p, tableRadius, 0.5f, 0.4f)) continue;
                    uint v = Hashing.Mix(gs, (uint)t);
                    // Tables tangential to the ring read as arranged around the shelter.
                    ctx.Object(p, Facing(p, c) + math.PI * 0.5f, ObjectRole.Furniture, AssetMix.Pick(tableIds, v), v, -1, AssetCategories.Picnic);
                    occ.Take(p, tableRadius, 1);
                    placedTables++;
                }
                if (!trees) continue;
                // Two or three trees on one side of the ring: a shaded edge, while the rest stays open lawn.
                int count = 2 + (int)(Hashing.Mix(gs, 3u) % 2u);
                for (int k = 0; k < count; k++)
                {
                    float a = math.radians(200f + k * 35f) + (Hashing.Unit(Hashing.Mix(gs, (uint)k + 9u)) - 0.5f) * 0.4f;
                    var p = c + new float2(math.cos(a), math.sin(a)) * (ring + tableRadius + 3.2f);
                    if (!occ.Free(p, 2.5f, 1f, 0.5f)) continue;
                    uint v = Hashing.Mix(gs, 0x7000u + (uint)k);
                    ctx.Object(p, Hashing.Unit(v) * 6.283f, ObjectRole.Tree, AssetMix.Pick(treeIds, v), v, 3);
                    occ.Take(p, 2.5f, 2);
                    placedTrees++;
                }
            }
            if (spots.Count == 0) NoSpace(ctx, "Placemaker.AddOn.Picnic.Name");
            ctx.Stat(Keys.StatPicnicTables, placedTables, Keys.UnitCount);
            ctx.Stat(Keys.StatTrees, placedTrees, Keys.UnitCount);
        }
    }
}
