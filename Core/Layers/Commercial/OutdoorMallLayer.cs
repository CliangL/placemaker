// Outdoor mall / lifestyle centre: a pedestrian shopping street (vanilla "Pedestrian Street", which carries
// zone blocks) leaves the main street with shop plots on both sides and ends on a plaza (Plaza planner:
// fountain, trees, benches); parking lots take the frontage beside the shop blocks; the rest is park.
// Styles (presets):
//   promenade  one straight pedestrian street (8 m), plaza at its end
//   grand      the wide 16 m pedestrian street, deeper plots, bigger lots
//   loop       a pedestrian loop (both ends on the street) with shops on both sides of it — outside facing in,
//              inside facing out — around a central green

using System.Collections.Generic;
using Placemaker.Core.Layers.Common;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Commercial
{
    [AreaGenerator(17, Group = Group)]
    public sealed class OutdoorMallLayer : CommercialLayer
    {
        protected override string Area => "OutdoorMall";
        public override string Id => "outdoorMall";
        public override string Icon => "Media/Game/Icons/Pedestrian.svg";
        public override float MinArea => 4000f;

        public static readonly string[] Styles = { "promenade", "grand", "loop" };

        protected override void DefineControls(ControlSet c)
        {
            c.Choice("style", "promenade", ControlTier.Essential, "layout", Styles).Tip();
            c.Slider("plazaSize", 36, 0, 64, 4, "m", ControlTier.Essential, "layout").Tip();
            c.Slider("spineLength", 120, 40, 240, 10, "m", ControlTier.More, "layout").Tip();
            c.Toggle("sideLots", true, ControlTier.More, "parking").Tip();
            CommonControls(c, ZoneKinds.CommercialLow, 24, "medium");
            c.Fixed("style");
        }

        protected override void DefinePresets()
        {
            Preset("promenade", ("style", "promenade"));
            Preset("grand", ("style", "grand"), ("plotDepth", 32.0), ("lotSize", "large"), ("plazaSize", 48.0), ("zoneKind", ZoneKinds.CommercialHigh));
            Preset("loop", ("style", "loop"), ("plazaSize", 40.0), ("plotDepth", 16.0));
            Preset("village", ("style", "promenade"), ("plotDepth", 16.0), ("lotSize", "small"), ("plazaSize", 28.0), ("zoneKind", ZoneKinds.Mixed));
        }

        protected override void Compose(LayerContext ctx, Mall mall, List<StreetShops.Run> runs)
        {
            var r = runs[0];
            string style = ctx.S("style");
            bool grand = style == "grand";
            float hs = grand ? 8f : Mall.AlleyHalf;
            var asset = grand ? Mall.PedestrianStreet : Mall.PedestrianStreetSmall;
            int cells = Cells(ctx);
            float from = r.From + (r.StartCorner ? Mall.CornerClear : 2f), to = r.To - (r.EndCorner ? Mall.CornerClear : 2f);
            float c = 0.5f * (from + to);
            float blockL, blockR;
            bool ok = style == "loop"
                ? Loop(ctx, mall, r, c, hs, asset, ref cells, out blockL, out blockR)
                : Promenade(ctx, mall, r, c, hs, asset, ref cells, out blockL, out blockR);
            if (!ok)
            {
                mall.ShopRow(r, from, to, 0f, cells);
                ctx.Issue(IssueSeverity.Info, FallbackKey);
                return;
            }
            if (ctx.B("sideLots"))
            {
                float lt = math.min(Mall.LotDepth(Mall.Aisles(ctx.S("lotSize"))), mall.Reach(r, from, to) - 2f);
                uint salt = 0xC10u;
                foreach (var (s0, s1) in new[] { (from, blockL - 2f), (blockR + 2f, to) })
                {
                    if (s1 - s0 < 28f || lt < 18f) continue;
                    var e = new List<(float2, string)>();
                    LotEntrances(e, r, s0, s1, 0f, "entrance");
                    mall.Lot(Mall.Rect(r, s0, s1, 0f, lt), e, Mall.Parking(ctx), salt++);
                }
                if (mall.Lots.Count == 0)
                    foreach (var back in runs)
                        if (math.dot(back.In, r.In) < -0.7f && mall.FrontLot(back, 90f, Mall.Aisles(ctx.S("lotSize")), Mall.Parking(ctx), salt++)) break;
            }
        }

        private static bool Promenade(LayerContext ctx, Mall mall, StreetShops.Run r, float c, float hs, AssetRef asset, ref int cells, out float blockL, out float blockR)
        {
            blockL = blockR = c;
            float plaza = ctx.Raw("plazaSize");
            float reach = mall.Reach(r, c - hs - cells * Mall.Cell, c + hs + cells * Mall.Cell);
            float len = math.min(ctx.Raw("spineLength"), reach - plaza - 2f);
            if (len < 3 * Mall.Cell && plaza > 0f) { plaza = 0f; len = math.min(ctx.Raw("spineLength"), reach - 10f); }
            if (len < 3 * Mall.Cell) return false;
            len = math.floor(len / Mall.Cell) * Mall.Cell;
            var lane = mall.AddLane(r.At(c, 0f), -r.In, new[] { r.At(c, len) }, null, default, NetRole.Street, asset, hs);
            if (lane == null) return false;
            float d = cells * Mall.Cell;
            Leg(mall, r, c - hs, -1f, 0f, len, cells);
            Leg(mall, r, c + hs, 1f, 0f, len, cells);
            blockL = c - hs - d; blockR = c + hs + d;
            if (plaza >= 20f)
            {
                var square = Mall.Rect(r, c - plaza * 0.5f, c + plaza * 0.5f, len, len + plaza);
                if (!mall.Fill(square, "plaza", null, new[] { r.At(c, len) }, 0xC1u))
                    ctx.Issue(IssueSeverity.Info, FallbackKey);
            }
            return true;
        }

        private static bool Loop(LayerContext ctx, Mall mall, StreetShops.Run r, float c, float hs, AssetRef asset, ref int cells, out float blockL, out float blockR)
        {
            blockL = blockR = c;
            float d = cells * Mall.Cell;
            // Inside the loop: a row of plots along each leg (facing out) and a green court between them.
            float inner = math.max(ctx.Raw("plazaSize"), 24f) + 2 * d;
            float half = inner * 0.5f + 2 * hs + d;
            float reach = mall.Reach(r, c - half, c + half);
            float depth = math.min(ctx.Raw("spineLength") * 0.6f, reach - 2 * hs - d - 2f);
            depth = math.floor(depth / Mall.Cell) * Mall.Cell;
            if (depth < 4 * Mall.Cell || c - half < r.From || c + half > r.To) return false;
            float l = c - inner * 0.5f - hs, rr = c + inner * 0.5f + hs;
            var lane = mall.AddLane(r.At(l, 0f), -r.In, new[] { r.At(l, depth + hs), r.At(rr, depth + hs) }, r.At(rr, 0f), -r.In, NetRole.Street, asset, hs);
            if (lane == null) return false;
            // Outside the loop, facing in.
            Leg(mall, r, l - hs, -1f, 0f, depth + 2 * hs, cells);
            Leg(mall, r, rr + hs, 1f, 0f, depth + 2 * hs, cells);
            mall.ShopRow(r, l - hs, rr + hs, depth + 2 * hs, cells);
            // Inside the loop, facing out; the court between them opens to the street.
            Leg(mall, r, l + hs, 1f, 0f, depth, cells);
            Leg(mall, r, rr - hs, -1f, 0f, depth, cells);
            mall.ShopRow(r, l + hs + d, rr - hs - d, depth, cells, -1f);
            var court = Mall.Rect(r, l + hs + d, rr - hs - d, 0f, depth - d);
            if (!mall.Fill(court, "plaza", null, null, 0xC2u)) mall.Fill(court, "park", "pocket", null, 0xC3u);
            blockL = l - hs - d; blockR = rr + hs + d;
            return true;
        }

        /// <summary>Plots along a lane side at s = edge, reaching away (dir), from t0 to t1.</summary>
        private static void Leg(Mall mall, StreetShops.Run r, float edge, float dir, float t0, float t1, int cells)
        {
            float len = t1 - t0;
            int n = math.max(1, (int)math.ceil(len / (3 * Mall.Cell)));
            for (int k = 0; k < n; k++)
            {
                float a = t0 + len * k / n, b = t0 + len * (k + 1) / n;
                for (int dd = cells; dd >= 1; dd--)
                    if (mall.Shop(Mall.Rect(r, edge, edge + dir * dd * Mall.Cell, a, b))) break;
            }
        }
    }
}
