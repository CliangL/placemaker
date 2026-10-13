// Rocky outcrop feature: clusters of rock props on a natural-ground patch (irregular blob), a few
// small rocks scattered around each cluster, optional low planting at the rim. Category rock
// (vanilla boulders/stones); the patch uses surface.natural.

using Placemaker.Core.Layers.Common;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.AddOns
{
    [AreaGenerator(145)]
    public sealed class RocksAddOn : AddOnLayer
    {
        public const string LayerId = "rocks";

        protected override string Area => "AddOn.Rocks";
        public override string Id => LayerId;
        public override string Icon => "Media/Game/Icons/PropsStone.svg";

        protected override void DefineControls(ControlSet c)
        {
            c.Slider("clusters", 3, 1, 12, 1, "count", ControlTier.Essential, "rocks");
            c.Choice("size", "medium", ControlTier.Essential, "rocks", "small", "medium", "large");
            c.AssetMulti("assets", AssetCategories.Rock, ControlTier.Essential, "rocks");
            c.Slider("rocks", 6, 2, 16, 1, "count", ControlTier.More, "rocks");
            c.Toggle("patch", true, ControlTier.More, "rocks").Tip();
            c.Asset("patchSurface", AssetCategories.SurfaceNatural, ControlTier.More, "rocks").When("patch=true");
            c.Toggle("rim", true, ControlTier.More, "rocks");
            c.RandomRange("clusters", 2, 5);
        }

        protected override void DefinePresets()
        {
            Preset("scattered", ("clusters", 6.0), ("size", "small"), ("rocks", 4.0));
            Preset("outcrop", ("clusters", 2.0), ("size", "large"), ("rocks", 12.0));
        }

        protected override void PlanCore(LayerContext ctx)
        {
            var ids = ctx.L("assets");
            if (!AssetAvailability.Require(ctx.Plan, AssetCategories.Rock, ids, Keys.Category(AssetCategories.Rock))) return;
            var occ = Occupancy(ctx);
            float R = ctx.S("size") == "small" ? 3.5f : ctx.S("size") == "large" ? 9f : 5.5f;
            int wanted = ctx.I("clusters");
            var spots = Spread(occ, R + 1f, wanted, Spots.Preference.Open, Hashing.Mix(ctx.Seed, 0x40C5u), R * 3f);
            int rocks = 0, clusters = 0;
            float rockR = AssetMix.MaxFootprint(ctx.Site, ids, 1.2f);
            foreach (var c in spots)
            {
                uint cs = Hashing.Mix(ctx.Seed, (uint)clusters * 977u + 3u);
                if (ctx.B("patch")) ctx.Surface(LeisureKit.Blob(c, R * 0.85f, cs, 14, 0.2f), SurfaceRole.Decorative, AssetMix.One(ctx.S("patchSurface")), AssetCategories.SurfaceNatural);
                int n = ctx.I("rocks");
                const float golden = 2.39996323f;
                for (int i = 0; i < n * 2 && i < 40; i++)
                {
                    float rr = R * 0.8f * math.sqrt((i + 0.5f) / (n * 1.2f));
                    if (rr > R) break;
                    var p = c + new float2(math.cos(i * golden + cs % 7), math.sin(i * golden + cs % 7)) * rr;
                    uint v = Hashing.Mix(cs, (uint)i);
                    float scale = 0.6f + 0.9f * Hashing.Unit(v) * (1f - rr / (R + 0.01f) * 0.5f);
                    if (!occ.Free(p, rockR * scale, 0.5f, 0.1f)) continue;
                    var o = ctx.Object(p, Hashing.Unit(Hashing.Mix(v, 5u)) * 6.283f, ObjectRole.Furniture, AssetMix.Pick(ids, v), v, -1, AssetCategories.Rock);
                    o.Scale = scale;
                    occ.Take(p, rockR * scale, 2);
                    rocks++;
                }
                if (ctx.B("rim"))
                    for (int i = 0; i < 5; i++)
                    {
                        float a = Hashing.Unit(Hashing.Mix(cs, (uint)i + 50u)) * 6.283f;
                        var p = c + new float2(math.cos(a), math.sin(a)) * (R + 1.2f);
                        LeisureKit.Put(ctx, occ, p, a, ObjectRole.Bush, AssetMix.One(""), null, 1f, Hashing.Mix(cs, (uint)i + 60u));
                    }
                clusters++;
            }
            if (clusters == 0) NoSpace(ctx, "Placemaker.AddOn.Rocks.Name");
            ctx.Stat("Placemaker.Stat.Rocks", rocks, Keys.UnitCount);
        }
    }
}
