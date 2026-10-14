// Farm features shared by the farm types: a bee yard (rows of hives in a small fenced plot with a
// wildflower fringe) and a farm stall (produce stand by the way in, with crates and a picnic table).
// Both are ordinary add-ons: they only place into free space and degrade to an Info issue when the
// installed assets have nothing of their category.

using System.Collections.Generic;
using Placemaker.Core.Layers.AddOns;
using Placemaker.Core.Layers.Common;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Types.UrbanFarm
{
    [AreaGenerator(132)]
    public sealed class HoneyHivesAddOn : AddOnLayer
    {
        public const string LayerId = "honeyHives";

        protected override string Area => "AddOn.HoneyHives";
        public override string Id => LayerId;
        public override string Icon => "Media/Game/Resources/Vegetables.svg";

        protected override void DefineControls(ControlSet c)
        {
            c.Slider("count", 6, 2, 16, 1, "count", ControlTier.Essential, "hives");
            c.Choice("layout", "rows", ControlTier.Essential, "hives", "rows", "circle").Tip();
            c.Toggle("fence", true, ControlTier.More, "hives").Tip();
            c.Toggle("wildflowers", true, ControlTier.More, "hives").Tip();
            c.Asset("hiveAsset", AssetCategories.CityFarmBeehive, ControlTier.More, "hives");
            c.RandomRange("count", 4, 10);
        }

        protected override void PlanCore(LayerContext ctx)
        {
            var chosen = ctx.S("hiveAsset");
            if (!AssetAvailability.Require(ctx.Plan, AssetCategories.CityFarmBeehive, new[] { chosen }, Keys.Category(AssetCategories.CityFarmBeehive))) return;
            var occ = Occupancy(ctx);
            int count = ctx.I("count");
            bool circle = ctx.S("layout") == "circle";
            int perRow = math.max(2, (int)math.ceil(math.sqrt(count)));
            float radius = circle ? 2.2f + count * 0.35f : math.max(perRow * 1.8f, (count + perRow - 1) / perRow * 2.4f) * 0.5f + 1.5f;
            if (!Spots.Find(occ, radius + 1.5f, Spots.Preference.NearBoundary, ctx.Seed, out var c, null, 0f, 1f))
            {
                NoSpace(ctx, "Placemaker.AddOn.HoneyHives.Name");
                return;
            }
            var hive = AssetMix.One(chosen);
            var toCentre = math.normalizesafe(ctx.LayerRegion.Centroid - c, new float2(0, 1));
            var side = new float2(-toCentre.y, toCentre.x);
            int placed = 0;
            for (int i = 0; i < count; i++)
            {
                float2 p; float yaw;
                if (circle)
                {
                    float a = i * 2f * math.PI / count;
                    p = c + new float2(math.cos(a), math.sin(a)) * (radius - 1.6f);
                    yaw = Facing(c, p);   // entrances face out
                }
                else
                {
                    int row = i / perRow, col = i % perRow;
                    int rows = (count + perRow - 1) / perRow;
                    p = c + side * ((col - (perRow - 1) * 0.5f) * 1.8f) + toCentre * ((row - (rows - 1) * 0.5f) * 2.4f);
                    yaw = Facing(p, p + toCentre);
                }
                if (!occ.Free(p, 0.6f, 0.3f, 0.1f)) continue;
                ctx.Object(p, yaw, ObjectRole.Furniture, hive, Hashing.Mix(ctx.Seed, (uint)i + 7u), -1, AssetCategories.CityFarmBeehive);
                occ.Take(p, 0.6f, 1);
                placed++;
            }
            if (ctx.B("fence"))
            {
                // Small closed plot with the gate towards the middle of the host area.
                float fr = radius + 0.6f;
                foreach (var run in Fences.Ring(c, fr, 10, toCentre, 2f))
                    if (ctx.LayerRegion.ContainsDisc(run.c0, 0.3f) && ctx.LayerRegion.ContainsDisc(run.c1, 0.3f))
                        ctx.StraightNet(run.c0, run.c1, NetRole.Fence, AssetMix.One(""));
            }
            if (ctx.B("wildflowers") && AssetAvailability.Has(AssetCategories.Flowerbed))
            {
                int n = 0;
                for (int i = 0; i < 14; i++)
                {
                    float a = i * 2f * math.PI / 14f + 0.2f;
                    var p = c + new float2(math.cos(a), math.sin(a)) * (radius + 2.4f);
                    if (math.dot(p - c, toCentre) > radius * 0.8f) continue;   // keep the gate side open
                    if (!occ.Free(p, 0.7f, 0.3f, 0.1f)) continue;
                    ctx.Object(p, a, ObjectRole.Planter, AssetMix.One(""), Hashing.Mix(ctx.Seed, (uint)i + 99u), -1, AssetCategories.Flowerbed);
                    occ.Take(p, 0.7f, 1);
                    n++;
                }
            }
            if (placed == 0) NoSpace(ctx, "Placemaker.AddOn.HoneyHives.Name");
            ctx.Stat("Placemaker.Stat.Beehives", placed, Keys.UnitCount);
        }
    }

    [AreaGenerator(133)]
    public sealed class FarmStallAddOn : AddOnLayer
    {
        public const string LayerId = "farmStall";

        protected override string Area => "AddOn.FarmStall";
        public override string Id => LayerId;
        public override string Icon => "Media/Game/Icons/Commercial.svg";

        protected override void DefineControls(ControlSet c)
        {
            c.Slider("stalls", 1, 1, 4, 1, "count", ControlTier.Essential, "kiosks");
            c.Toggle("crates", true, ControlTier.More, "kiosks").Tip();
            c.Slider("tables", 2, 0, 6, 1, "count", ControlTier.More, "kiosks");
            c.Asset("stallAsset", AssetCategories.MarketStall, ControlTier.More, "kiosks");
            c.RandomRange("stalls", 1, 2);
        }

        protected override void PlanCore(LayerContext ctx)
        {
            var chosen = ctx.S("stallAsset");
            string cat = LeisureKit.Category(ctx, chosen, AssetCategories.MarketStall, AssetCategories.Kiosk);
            if (cat == null) return;
            var occ = Occupancy(ctx);
            var stall = AssetMix.One(chosen);
            int wanted = ctx.I("stalls");
            // On the way in: beside a path near an entrance; without paths, near the outline.
            var anchors = PathAnchors.Pick(PathAnchors.Beside(occ, 3.2f, 5f, 0.8f), 12, 6f, true);
            var spots = new List<(float2 p, float2 face)>();
            foreach (var a in anchors) spots.Add((a.Position, a.Toward));
            if (spots.Count == 0 && Spots.Find(occ, 6f, Spots.Preference.NearBoundary, ctx.Seed, out var s0))
                spots.Add((s0, ctx.LayerRegion.Centroid));
            int placed = 0, extras = 0;
            float2 first = default; float2 firstFace = default;
            foreach (var (p, face) in spots)
            {
                if (placed >= wanted) break;
                if (placed > 0 && math.distance(p, first) > 25f) continue;   // stalls stand together
                if (!occ.Free(p, 2.6f, 0.5f, 0.3f)) continue;
                uint v = Hashing.Mix(ctx.Seed, (uint)placed + 11u);
                ctx.Object(p, Facing(p, face), ObjectRole.Furniture, stall, v, -1, cat);
                occ.Take(p, 2.6f, 0);
                if (placed == 0) { first = p; firstFace = face; }
                placed++;
            }
            if (placed == 0) { NoSpace(ctx, "Placemaker.AddOn.FarmStall.Name"); return; }
            var fwd = math.normalizesafe(firstFace - first, new float2(0, 1));
            var side = new float2(-fwd.y, fwd.x);
            if (ctx.B("crates") && AssetAvailability.Has(AssetCategories.CityFarmProp))
                for (int k = 0; k < 4; k++)
                {
                    var p = first + side * ((k < 2 ? -1f : 1f) * (3.6f + (k & 1) * 1.1f)) + fwd * 0.6f;
                    if (!occ.Free(p, 0.6f, 0.3f, 0.1f)) continue;
                    ctx.Object(p, Facing(p, firstFace), ObjectRole.Furniture, AssetMix.One(""), Hashing.Mix(ctx.Seed, (uint)k + 31u), -1, AssetCategories.CityFarmProp);
                    occ.Take(p, 0.6f, 1);
                    extras++;
                }
            int tables = ctx.I("tables");
            if (tables > 0 && AssetAvailability.Has(AssetCategories.Picnic))
                for (int k = 0, n = 0; k < tables * 4 && n < tables; k++)
                {
                    float a = 2.2f + k * 0.55f;
                    var p = first - fwd * (5.5f + (k / 6) * 3f) + side * math.sin(a) * 5f;
                    if (!occ.Free(p, 1.4f, 0.5f, 0.3f)) continue;
                    ctx.Object(p, Facing(p, first), ObjectRole.Furniture, AssetMix.One(""), Hashing.Mix(ctx.Seed, (uint)k + 51u), -1, AssetCategories.Picnic);
                    occ.Take(p, 1.4f, 1);
                    n++;
                }
            ctx.Stat("Placemaker.Stat.Kiosks", placed, Keys.UnitCount);
        }
    }
}
