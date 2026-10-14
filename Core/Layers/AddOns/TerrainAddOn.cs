// Terrain feature: gentle mounds, rolling hills, terraces, sunken lawn, raised platform, an
// amphitheatre bowl, berms along the roads, or flatten. Always permanent (warned), always blended
// back to the original ground at the outline (TerrainShaper). Places and reserved polygons (a
// park's pond basin or plaza) are left untouched.

using Placemaker.Core.Layers.Common;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.AddOns
{
    [AreaGenerator(140)]
    public sealed class TerrainAddOn : AddOnLayer
    {
        public const string LayerId = "terrain";

        protected override string Area => "AddOn.Terrain";
        public override string Id => LayerId;
        public override string Icon => "Media/Game/Icons/TerrainHeight.svg";
        public override float MinArea => 200f;

        protected override void DefineControls(ControlSet c)
        {
            c.Choice("shape", "mounds", ControlTier.Essential, "terrain",
                "mounds", "hills", "terraces", "terracedDown", "sunken", "raised", "bowl", "berms", "flatten").Tip();
            c.Slider("height", 2, 0.5, 8, 0.5, "m", ControlTier.Essential, "terrain").Tip();
            // (mounds: the noise humps reach the full height only at their peaks)
            c.Slider("scale", 30, 10, 80, 5, "m", ControlTier.More, "terrain").Layout(8).Tip();
            c.Slider("maxSlope", 20, 5, 50, 5, "%", ControlTier.More, "terrain").Tip();
            c.Slider("falloff", 18, 6, 60, 2, "m", ControlTier.More, "terrain").Layout(4).Tip();
            c.Slider("levels", 3, 2, 4, 1, "count", ControlTier.More, "terrain");
            c.RandomRange("height", 1, 3);
            c.Fixed("shape");
        }

        protected override void DefinePresets()
        {
            Preset("gentle", ("shape", "mounds"), ("height", 1.5), ("scale", 25.0));
            Preset("rolling", ("shape", "hills"), ("height", 3.0), ("scale", 45.0));
            Preset("terraced", ("shape", "terraces"), ("height", 3.0), ("levels", 3.0));
            Preset("amphitheatre", ("shape", "bowl"), ("height", 3.0));
            Preset("flat", ("shape", "flatten"));
        }

        protected override void PlanCore(LayerContext ctx)
        {
            var region = ctx.LayerRegion;
            string shape = ctx.S("shape");
            var reserved = ctx.Input.Reserved;
            var spec = new TerrainSpec
            {
                Mode = TerrainSpec.Parse(shape), Down = shape == "terracedDown", Height = ctx.F("height"), Scale = ctx.F("scale"),
                MaxSlopePct = ctx.F("maxSlope"), Falloff = ctx.F("falloff"), Levels = ctx.I("levels"), Seed = Hashing.Mix(ctx.Seed, 0x7E44u),
                RowSpacing = 4f,
                Skip = p =>
                {
                    if (reserved == null) return false;
                    foreach (var poly in reserved.Polygons) if (ReservedSpace.PointInPolygon(p, poly)) return true;
                    return false;
                },
            };
            if (spec.Mode == TerrainMode.Bowl) spec.Focal = BowlFocal(region, spec.Falloff);
            if (spec.Mode == TerrainMode.Berms)
            {
                spec.Frontage = StreetFrontage.Edges(region.Ring, StreetFrontage.Roads(ctx.Input));
                if (spec.Frontage.Count == 0) ctx.Issue(IssueSeverity.Info, "Placemaker.AddOn.Terrain.Issue.NoRoads");
            }
            var r = TerrainShaper.Shape(ctx, region, spec);
            if (r.Cells == 0) ctx.Issue(IssueSeverity.Info, "Placemaker.AddOn.Terrain.Issue.Nothing");
            ctx.Stat("Placemaker.Stat.TerrainChange", System.Math.Round(math.max(r.MaxOffset, -r.MinOffset), 1), Keys.UnitM);
        }

        /// <summary>The bowl's stage: the middle of the longest outline edge, nudged inside.</summary>
        public static float2 BowlFocal(LayerRegion region, float inset)
        {
            int best = 0; float len = 0;
            for (int i = 0; i < region.Ring.Length; i++)
            {
                float l = math.distance(region.Ring[i], region.Ring[(i + 1) % region.Ring.Length]);
                if (l > len) { len = l; best = i; }
            }
            var mid = math.lerp(region.Ring[best], region.Ring[(best + 1) % region.Ring.Length], 0.5f);
            return mid + region.InwardNormal(best) * math.max(4f, inset * 0.5f);
        }
    }
}
