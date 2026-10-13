// Flowerbeds add-on: small tight clusters of flowerbed props, by default lining paths (where
// they are seen), otherwise scattered over lawns with value-noise clustering.

using System.Collections.Generic;
using Placemaker.Core.Layers.Common;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.AddOns
{
    [AreaGenerator(150)]
    public sealed class FlowerbedsAddOn : AddOnLayer
    {
        protected override string Area => "AddOn.Flowerbeds";
        public override string Id => "flowerbeds";
        public override string Icon => "Media/Game/Icons/ParksAndRecreation.svg";

        private const float ItemRadius = 0.6f;

        protected override void DefineControls(ControlSet c)
        {
            c.Slider("density", 8, 1, 40, 1, "perHa", ControlTier.Essential, "flowers").Tip();
            c.AssetMulti("assets", AssetCategories.Flowerbed, ControlTier.Essential, "flowers");
            c.Slider("bedSize", 5, 2, 12, 1, "count", ControlTier.More, "flowers").Tip();
            c.Toggle("nearPaths", true, ControlTier.More, "flowers").Tip();
            c.RandomRange("density", 4, 20);
            c.RandomRange("bedSize", 3, 8);
            c.Chance("nearPaths", 0.75f);
        }

        protected override void PlanCore(LayerContext ctx)
        {
            var ids = ctx.L("assets");
            if (!AssetAvailability.Require(ctx.Plan, AssetCategories.Flowerbed, ids, Keys.Category(AssetCategories.Flowerbed))) return;
            var occ = Occupancy(ctx);
            int beds = math.max(1, (int)math.round(ctx.LayerRegion.Area / 10000f * ctx.F("density")));
            int perBed = ctx.I("bedSize");
            var anchors = ctx.B("nearPaths") && occ.Paths.Count > 0 ? PathAnchors(occ, ctx.Seed) : new List<float2>();
            uint clump = Hashing.Mix(ctx.Seed, 0xF1u);
            foreach (var p in Scatter.Candidates(ctx.LayerRegion, 6f, ctx.Seed, 4000, ctx.Cancel))
                if (Hashing.Unit(Hashing.Mix(clump, (uint)anchors.Count)) < Scatter.Clustering(p, 25f, clump, 0.1f)) anchors.Add(p);

            int placedBeds = 0, placedItems = 0;
            var used = new List<float2>();
            float itemRadius = AssetMix.MaxFootprint(ctx.Site, ids, ItemRadius);
            const float golden = 2.39996323f;
            foreach (var a in anchors)
            {
                if (placedBeds >= beds) break;
                bool near = false;
                foreach (var u in used) if (math.distancesq(u, a) < 144f) { near = true; break; }
                if (near || !occ.Free(a, itemRadius, 0.5f, 0.3f)) continue;
                int inBed = 0;
                uint bs = Hashing.Mix(ctx.Seed, (uint)placedBeds + 101u);
                for (int i = 0; i < perBed * 3 && inBed < perBed; i++)
                {
                    float r = itemRadius * 2.1f * math.sqrt(i);
                    var p = a + new float2(math.cos(i * golden), math.sin(i * golden)) * r;
                    if (!occ.Free(p, itemRadius, 0.3f, 0.1f)) continue;
                    uint v = Hashing.Mix(bs, (uint)(inBed % 2));   // two species per bed at most: reads as planted, not random
                    ctx.Object(p, Hashing.Unit(Hashing.Mix(v, (uint)i)) * 6.283f, ObjectRole.Planter, AssetMix.Pick(ids, v), v);
                    occ.Take(p, itemRadius, 0);
                    inBed++;
                }
                if (inBed == 0) continue;
                used.Add(a);
                placedBeds++;
                placedItems += inBed;
            }
            if (placedBeds == 0) NoSpace(ctx, "Placemaker.AddOn.Flowerbeds.Name");
            ctx.Stat(Keys.StatFlowerbeds, placedBeds, Keys.UnitCount);
            ctx.Plan.Facts["flowerbedItems"] = placedItems;
        }

        /// <summary>Points beside reserved paths every ~20 m, alternating sides.</summary>
        private static List<float2> PathAnchors(Occupancy occ, uint seed)
        {
            var result = new List<float2>();
            float carry = 0f;
            int side = 0;
            foreach (var s in occ.Paths.Segments)
            {
                float len = math.distance(s.A, s.B);
                if (len < 0.1f) continue;
                var t = (s.B - s.A) / len;
                var n = new float2(-t.y, t.x);
                for (float d = 20f - carry; d < len; d += 20f)
                {
                    float sign = (side++ & 1) == 0 ? 1f : -1f;
                    result.Add(s.A + t * d + n * sign * (s.HalfWidth + 1.8f));
                }
                carry = (carry + len) % 20f;
            }
            return result;
        }
    }
}
