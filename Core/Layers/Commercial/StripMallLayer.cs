// Strip mall: street → parking lot → a lot lane → a row of shops facing the lot across the lane → a green
// buffer behind. Styles (presets):
//   classic    the lot fills the frontage; a U-shaped lane (both legs on the street) runs behind it and the
//              shops line the lane's far side, facing the lot
//   corner     the lot holds a street corner; an L-shaped lane wraps it from one street to the other and the
//              shops wrap the lane in an L (corner retail)
//   rearAlley  shops on the street; a service lane loops behind them and a lot behind the lane is entered
//              from it — the street keeps its shop fronts, the cars go round the back
// The lot has street entrances (connector to the kerb, as the Parking layer builds them) and entrances on the
// lane (short alley stubs sharing a node with the lane).

using System.Collections.Generic;
using Placemaker.Core.Layers.Common;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Commercial
{
    [AreaGenerator(15, Group = Group)]
    public sealed class StripMallLayer : CommercialLayer
    {
        protected override string Area => "StripMall";
        public override string Id => "stripMall";
        public override string Icon => "Media/Game/Icons/ZoneCommercial.svg";

        public static readonly string[] Styles = { "classic", "corner", "rearAlley" };

        protected override void DefineControls(ControlSet c)
        {
            c.Choice("style", "classic", ControlTier.Essential, "layout", Styles).Tip();
            c.Slider("frontage", 140, 60, 240, 10, "m", ControlTier.More, "layout").Tip().When("style!=corner");
            CommonControls(c);
            c.Fixed("style");
        }

        protected override void DefinePresets()
        {
            Preset("classic", ("style", "classic"));
            Preset("corner", ("style", "corner"), ("lotSize", "medium"));
            Preset("rearAlley", ("style", "rearAlley"), ("lotSize", "medium"), ("plotDepth", 16.0));
            Preset("neighbourhood", ("style", "classic"), ("lotSize", "small"), ("frontage", 90.0), ("plotDepth", 16.0), ("lotGreen", "medians"));
            Preset("bigBox", ("style", "classic"), ("lotSize", "large"), ("frontage", 220.0), ("plotDepth", 40.0), ("zoneKind", ZoneKinds.CommercialHigh), ("evShare", 10.0));
        }

        protected override void Compose(LayerContext ctx, Mall mall, List<StreetShops.Run> runs)
        {
            string style = ctx.S("style");
            if (style == "corner" && Corner(ctx, mall, runs)) return;
            if (style == "rearAlley" && RearAlley(ctx, mall, runs[0])) return;
            if (style != "classic") ctx.Issue(IssueSeverity.Info, FallbackKey);
            if (!Classic(ctx, mall, runs[0]))
            {
                // No room for a lot and a lane: plots straight on the street.
                var r = runs[0];
                mall.ShopRow(r, r.From + 2f, r.To - 2f, 0f, Cells(ctx));
                ctx.Issue(IssueSeverity.Info, NoLotKey);
            }
        }

        /// <summary>The frontage span used: corners kept clear for lane junctions, capped and centred.</summary>
        private static void Span(LayerContext ctx, StreetShops.Run r, out float s0, out float s1)
        {
            s0 = r.From + (r.StartCorner ? Mall.CornerClear : 2f);
            s1 = r.To - (r.EndCorner ? Mall.CornerClear : 2f);
            float max = math.max(60f, ctx.Raw("frontage"));
            if (s1 - s0 > max) { float c = 0.5f * (s0 + s1); s0 = c - max * 0.5f; s1 = c + max * 0.5f; }
        }

        private static bool Classic(LayerContext ctx, Mall mall, StreetShops.Run r)
        {
            Span(ctx, r, out float s0, out float s1);
            if (s1 - s0 < 50f) return false;
            int cells = Cells(ctx), aisles = Mall.Aisles(ctx.S("lotSize"));
            float reach = mall.Reach(r, s0, s1);
            while (aisles > 0 && Mall.LotDepth(aisles) + 2 * Mall.AlleyHalf + cells * Mall.Cell > reach - 1f)
                if (cells > 2) cells--; else aisles--;
            if (aisles == 0) return false;
            float lt = Mall.LotDepth(aisles), h = Mall.AlleyHalf;
            var lane = mall.AddLane(r.At(s0 + h, 0f), -r.In, new[] { r.At(s0 + h, lt + h), r.At(s1 - h, lt + h) }, r.At(s1 - h, 0f), -r.In, NetRole.Alley, Mall.AlleyAsset, h);
            if (lane == null) return false;
            var entrances = new List<(float2, string)>();
            LotEntrances(entrances, r, s0 + 2 * h, s1 - 2 * h, 0f, "entrance");
            // Lane entrances between and beside the street ones, so cars can circulate lot ↔ shops.
            entrances.Add((r.At(math.lerp(s0, s1, 0.5f), lt), "alley"));
            if (s1 - s0 > 120f) { entrances.Add((r.At(math.lerp(s0, s1, 0.12f) + 4f, lt), "alley")); entrances.Add((r.At(math.lerp(s0, s1, 0.88f) - 4f, lt), "alley")); }
            // (s25) Walkways from the lot to the shop fronts across the lane (a footpath meeting the lane at a node).
            Walkways(entrances, r, s0, s1, lt);
            mall.Lot(Mall.Rect(r, s0 + 2 * h, s1 - 2 * h, 0f, lt), entrances, Mall.Parking(ctx), 0xA1u);
            // The shop row faces the lot across the lane's back run (and wraps the legs' ends).
            mall.ShopRow(r, s0, s1, lt + 2 * h, cells);
            return true;
        }

        private static bool RearAlley(LayerContext ctx, Mall mall, StreetShops.Run r)
        {
            Span(ctx, r, out float s0, out float s1);
            if (s1 - s0 < 50f) return false;
            int cells = Cells(ctx), aisles = Mall.Aisles(ctx.S("lotSize"));
            float h = Mall.AlleyHalf;
            float reach = mall.Reach(r, s0, s1);
            while (aisles > 0 && cells * Mall.Cell + 2 * h + Mall.LotDepth(aisles) > reach - 1f)
                if (cells > 1) cells--; else aisles--;
            float back = cells * Mall.Cell;
            var lane = mall.AddLane(r.At(s0 + h, 0f), -r.In, new[] { r.At(s0 + h, back + h), r.At(s1 - h, back + h) }, r.At(s1 - h, 0f), -r.In, NetRole.Alley, Mall.AlleyAsset, h);
            if (lane == null) return false;
            // Shop fronts on the street, backs (and service) on the lane.
            mall.ShopRow(r, s0 + 2 * h, s1 - 2 * h, 0f, cells);
            if (aisles > 0)
            {
                float t0 = back + 2 * h, lt = Mall.LotDepth(aisles);
                var entrances = new List<(float2, string)>();
                LotEntrances(entrances, r, s0, s1, t0, "alley");
                Walkways(entrances, r, s0, s1, t0);
                if (!mall.Lot(Mall.Rect(r, s0, s1, t0, t0 + lt), entrances, Mall.Parking(ctx), 0xA2u)) ctx.Issue(IssueSeverity.Info, NoLotKey);
            }
            else ctx.Issue(IssueSeverity.Info, NoLotKey);
            return true;
        }

        /// <summary>Corner retail: lot on a street corner, an L lane round it, shops round the lane.</summary>
        private static bool Corner(LayerContext ctx, Mall mall, List<StreetShops.Run> runs)
        {
            // The corner whose two streets are longest (the shorter of the two counts).
            int best = -1, next = -1; float bestLen = 0f;
            for (int i = 0; i < runs.Count; i++)
            {
                if (!runs[i].EndCorner) continue;
                for (int j = 0; j < runs.Count; j++)
                {
                    if (i == j || !runs[j].StartCorner || math.distance(runs[i].At(runs[i].To, 0f), runs[j].At(runs[j].From, 0f)) > 1f) continue;
                    float len = math.min(runs[i].Length, runs[j].Length);
                    if (len > bestLen) { bestLen = len; best = i; next = j; }
                }
            }
            if (best < 0 || bestLen < 70f) return false;
            var a = runs[best];
            float h = Mall.AlleyHalf;
            int cells = Cells(ctx), aisles = Mall.Aisles(ctx.S("lotSize"));
            float lot = Mall.LotDepth(aisles);
            float la = math.min(lot + 8f, a.Length * 0.55f), lb = math.min(lot, runs[next].Length * 0.55f);
            if (la < 24f || lb < 20f) return false;
            // A frame: u back along street A from the corner, t inward (= along street B).
            float2 P(float u, float t) => a.At(a.To - u, t);
            float legU = la + h, backT = lb + h;
            var lane = mall.AddLane(P(legU, 0f), -a.In, new[] { P(legU, backT) }, P(0f, backT), a.Dir, NetRole.Alley, Mall.AlleyAsset, h);
            if (lane == null) return false;
            var entrances = new List<(float2, string)>
            {
                (P(la * 0.5f, 0f), "entrance"),
                (P(0f, lb * 0.5f), "entrance"),
                (P(la, lb * 0.5f), "alley"),
                (P(la * 0.5f, lb), "alley"),
            };
            var ring = Poly4(P(la, 0f), P(0f, 0f), P(0f, lb), P(la, lb));
            if (!mall.Lot(ring, entrances, Mall.Parking(ctx), 0xA3u)) ctx.Issue(IssueSeverity.Info, NoLotKey);
            // Shops: along the leg (facing it, backs to the rest of the block) and along the back run, meeting in an L.
            float d = cells * Mall.Cell;
            int pieces = math.max(1, (int)math.ceil((backT + h) / (3 * Mall.Cell)));
            for (int k = 0; k < pieces; k++)
            {
                float t0 = (backT + h) * k / pieces, t1 = (backT + h) * (k + 1) / pieces;
                for (int dd = cells; dd >= 1; dd--)
                    if (mall.Shop(Poly4(P(legU + h + dd * Mall.Cell, t0), P(legU + h, t0), P(legU + h, t1), P(legU + h + dd * Mall.Cell, t1)))) break;
            }
            // The back row ends where the lane does: a plot beyond it would face no road (no zone block reaches it).
            float uEnd = legU + h;
            pieces = math.max(1, (int)math.ceil(uEnd / (3 * Mall.Cell)));
            for (int k = 0; k < pieces; k++)
            {
                float u0 = uEnd * k / pieces, u1 = uEnd * (k + 1) / pieces;
                for (int dd = cells; dd >= 1; dd--)
                    if (mall.Shop(Poly4(P(u1, backT + h), P(u0, backT + h), P(u0, backT + h + dd * Mall.Cell), P(u1, backT + h + dd * Mall.Cell)))) break;
            }
            // Shops continue along both streets beyond the L where the frontage allows (a row on the street).
            float rest = a.To - (legU + h + d + 4f);
            if (rest - a.From > 2 * Mall.Cell) mall.ShopRow(a, a.From + (a.StartCorner ? 2f : 0f), rest, 0f, cells);
            var b = runs[next];
            float bFrom = b.From + backT + h + d + 4f;
            if (b.To - bFrom > 2 * Mall.Cell) mall.ShopRow(b, bFrom, b.To - (b.EndCorner ? 2f : 0f), 0f, cells);
            return true;
        }

        /// <summary>Two pedestrian entrances (PLT Fussweg) on the lot edge at t, between the car entrances.</summary>
        private static void Walkways(List<(float2, string)> list, StreetShops.Run r, float s0, float s1, float t)
        {
            if (s1 - s0 < 60f) return;
            list.Add((r.At(math.lerp(s0, s1, 0.32f), t), "footpath"));
            list.Add((r.At(math.lerp(s0, s1, 0.68f), t), "footpath"));
        }

        private static float2[] Poly4(float2 a, float2 b, float2 c, float2 d) => Geometry.Poly.EnsureCcw(new[] { a, b, c, d });
    }
}
