// s14: the "Commercial" category (Make-row group "commercial"). Each type is a composition built with the
// Mall kit: lanes, zoned shop rows (cutouts), parking lots from the Parking planner and green / plaza pieces
// from the Park, Plaza and Market square planners. Shared controls read the same on every type
// (labels Placemaker.Commercial.<id>, zone type Placemaker.Zone.*).

using System;
using System.Collections.Generic;
using Placemaker.Core.Geometry;
using Placemaker.Core.Layers.AddOns;
using Placemaker.Core.Layers.Common;
using Placemaker.Core.Layers.Leisure;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Commercial
{
    public abstract class CommercialLayer : LeisureLayer
    {
        public const string Group = "commercial";
        protected const string K = "Placemaker.Commercial.";
        public const string NoStreetKey = K + "Issue.NoStreet";
        public const string NoLotKey = K + "Issue.NoLot";
        public const string FallbackKey = K + "Issue.Fallback";
        public const string PadKey = K + "Issue.Pad";
        public const string RestKey = K + "Issue.Rest";

        protected override bool TypedEntrances => false;
        protected override bool HostsStreetShops => false;
        public override float MinArea => 2500f;
        public override string[] Emits => new[] { RegionTags.Green, RegionTags.Paved, StreetFrontage.ShopsTag };

        /// <summary>
        /// Few, cheap features: the composition itself is the type. (s25) The "flatten terrain" feature is gone: one plane
        /// over lanes, lots and plots on a slope dug the plots in and banked the lanes; the type grades itself now
        /// (MallGrading, the lots' LotGrading). Decal sprinkles are off by default: the lots' own dressing (s21) is enough,
        /// the cracks theme put dark blobs all over the lot (playtest).
        /// </summary>
        protected override List<FeatureInfo> BuildFeatures() => new List<FeatureInfo>
        {
            Feature("decals", ("theme", "cracks"), ("ground", "paved"), ("density", 0.4)),
        };

        protected override string[] DefaultFeatures => Array.Empty<string>();

        /// <summary>Zone type, plot depth and the parking options shared by every commercial type.</summary>
        protected void CommonControls(ControlSet c, string zone = ZoneKinds.CommercialLow, double depth = 24, string lotSize = "medium", bool lot = true, string lotWhen = "", double maxDepth = 40)
        {
            var z = EdgeZoningAddOn.ZoneKindControl("zoneKind", "shops", zone);
            c.List.Add(z);
            Shared(c.Slider("plotDepth", depth, 8, maxDepth, 8, "m", ControlTier.Essential, "shops")).Tip();
            if (lot)
            {
                if (lotSize != null) Shared(c.Choice("lotSize", lotSize, ControlTier.Essential, "parking", "small", "medium", "large")).Tip();
                Shared(c.Choice("stallAngle", "90", ControlTier.More, "parking", "90", "60", "45")).When(lotWhen);
                Shared(c.Choice("lotGreen", "medians", ControlTier.More, "parking", "none", "edges", "medians")).Tip().When(lotWhen);
                Shared(c.Slider("evShare", 4, 0, 30, 1, "%", ControlTier.More, "parking")).Tip().When(lotWhen);
                Shared(c.Toggle("lamps", true, ControlTier.More, "parking")).When(lotWhen);
            }
            c.Fixed("zoneKind");
            c.Fixed("plotDepth");
        }

        /// <summary>(s32) Shops on the streets round the site (a row facing each street) and their zone ("big buildings").</summary>
        protected static void OuterRowControls(ControlSet c, string zone, bool on = true)
        {
            Shared(c.Toggle("outerRow", on, ControlTier.Essential, "shops")).Tip();
            var z = EdgeZoningAddOn.ZoneKindControl("outerZone", "shops", zone);
            z.LabelKey = K + "outerZone";
            z.TooltipKey = K + "outerZone.Tooltip";
            z.VisibleWhen = "outerRow=true";
            c.List.Add(z);
            c.Fixed("outerZone");
        }

        /// <summary>Points a control at the shared Placemaker.Commercial.* keys (options too).</summary>
        protected static ControlSchema Shared(ControlSchema c)
        {
            c.LabelKey = K + c.Id;
            foreach (var o in c.Options) o.LabelKey = K + c.Id + "." + o.Value;
            return c;
        }

        protected static ControlSchema Tipped(ControlSchema c) { c.TooltipKey = c.LabelKey + ".Tooltip"; return c; }

        protected override void PlanCore(LayerContext ctx)
        {
            var mall = new Mall(ctx) { GradeTolerance = GradeTolerance };
            var runs = mall.Runs();
            if (runs.Count == 0)
            {
                // Nothing to zone from: a pocket park keeps the site useful until a street is built.
                ctx.Issue(IssueSeverity.Warning, NoStreetKey);
                mall.Fill(ctx.LayerRegion.Ring, "park", "pocket", null, 0x51u);
                return;
            }
            ctx.Check();
            Compose(ctx, mall, runs);
            if (runs[0].Length < CurvedFrontage || mall.TakenArea() < mall.Region.Area * 0.25f) ComposeMore(ctx, mall, runs);
            ctx.Check();
            // (s40) Zoning first: street plots become their real cells before the rest is planned around them.
            mall.SnapStreetPlotsToCells();
            FillRest(ctx, mall);
            mall.Finish(ZoneKinds.PrefabId(ctx.S("zoneKind")));
            CommonStats(ctx);
        }

        /// <summary>(s32) Smallest height change the site grading emits (Mall.GradeTolerance); the s32 types grade finer.</summary>
        protected virtual float GradeTolerance => Sculpt.MinChange;

        protected abstract void Compose(LayerContext ctx, Mall mall, List<StreetShops.Run> runs);

        /// <summary>
        /// (s46) The composition only works one frontage (the longest). When most of the site is still free afterwards (a
        /// block with curved or angled streets, whose straight pieces are short), the same composition is tried on the
        /// other frontages, longest first — whatever fits (lot, lane and shops, or at least the shop row) — so the site is
        /// not left to a park. A piece that finds its land taken is skipped (every kit call checks the land it needs).
        /// </summary>
        private void ComposeMore(LayerContext ctx, Mall mall, List<StreetShops.Run> runs)
        {
            float total = mall.Region.Area;
            for (int k = 1; k < runs.Count; k++)
            {
                ctx.Check();
                if (mall.TakenArea() > total * LeftoverMax) break;
                var r = runs[k];
                if (r.Length < 3 * Mall.Cell + 2f) continue;
                // The frontage must still be free where the middle of the run starts.
                float m = 0.5f * (r.From + r.To);
                if (!mall.Fits(Mall.Rect(r, m - Mall.Cell, m + Mall.Cell, 0f, Mall.Cell))) continue;
                var order = new List<StreetShops.Run> { r };
                for (int i = 0; i < runs.Count; i++) if (i != k) order.Add(runs[i]);
                Compose(ctx, mall, order);
            }
            if (mall.TakenArea() < total * 0.5f) ctx.Issue(IssueSeverity.Info, RestKey);
        }

        /// <summary>Compose another frontage while less than this share of the site is taken.</summary>
        private const float LeftoverMax = 0.6f;
        /// <summary>A longest straight frontage shorter than this (m) is a curved or cut-up block: every frontage gets its composition.</summary>
        private const float CurvedFrontage = 110f;

        /// <summary>What is left becomes small parks (pocket style); slivers stay bare ground.</summary>
        protected virtual void FillRest(LayerContext ctx, Mall mall)
        {
            OverflowLots(ctx, mall);
            uint k = 0;
            foreach (var (piece, strip) in mall.RestSplit(150f))
            {
                ctx.Check();
                // Thin arms (beside a lane leg, behind a row) are a planted buffer, not a park with paths hugging the edge.
                if (strip || mall.IsStrip(piece) || !mall.Fill(piece, "park", "pocket", Doors(mall), 0x900u + k)) mall.Buffer(piece, 0x980u + k);
                k++;
            }
        }

        /// <summary>
        /// (s32) What is left, for the s32 types: wide parts become parks whose walks open onto the lanes beside them (the
        /// first, largest one in <paramref name="mainStyle"/>, with a playground when asked), thin arms planted verges that
        /// keep clear of every net (Mall.Green) — gateway strips beside lane mouths, the gaps behind rows.
        /// </summary>
        protected static void GreenRest(LayerContext ctx, Mall mall, string mainStyle = "pocket", bool playground = false, uint salt = 0x900u)
        {
            uint k = 0;
            bool first = true;
            foreach (var (piece, strip) in mall.RestSplit(150f))
            {
                ctx.Check();
                bool wide = !strip && !mall.IsStrip(piece);
                if (wide && mall.FillOpen(piece, "park", first ? mainStyle : "pocket", mall.LaneDoors(piece), Doors(mall), salt + k))
                {
                    if (first && playground) mall.AddOn(piece, "playground", salt + 0x40u + k, b => b.Values["size"] = "small");
                    first = false;
                }
                else mall.Green(piece, salt + 0x80u + k);
                k++;
            }
        }

        /// <summary>Lane ends and lot corners a green piece should open towards.</summary>
        protected static List<float2> Doors(Mall mall)
        {
            var list = new List<float2>(mall.Doors);
            foreach (var l in mall.Lanes)
                if (l.EndAttach < 0 && l.Points.Count > 1) list.Add(l.Points[l.Points.Count - 1]);
            return list;
        }


        /// <summary>
        /// (s46) Overflow parking: a wide part of what is left that touches a lane (or a street) of the mall becomes one more
        /// car park entered from it — a strip mall is mostly lot, lane and shops, the park only takes the true leftovers
        /// (playtest s46: a 3 ha block came out 95 % park). Pieces under <paramref name="minArea"/> stay for the green fill.
        /// </summary>
        protected static void OverflowLots(LayerContext ctx, Mall mall, uint salt = 0xB00u, float minArea = 1400f)
        {
            for (int pass = 0; pass < 4; pass++)
                if (!OverflowPass(ctx, mall, salt + (uint)pass * 0x10u, minArea)) break;
        }

        private static bool OverflowPass(LayerContext ctx, Mall mall, uint salt, float minArea)
        {
            var roads = StreetFrontage.Roads(ctx.Input);
            bool any = false;
            uint k = 0;
            foreach (var (piece, strip) in mall.RestSplit(150f))
            {
                ctx.Check();
                k++;
                if (strip || mall.IsStrip(piece) || Poly.Area(piece) < minArea) continue;
                // The nearest thing to enter from: a lane of the mall (alley stub) or a street (connector).
                float2 at = default, dir = default; string kind = null; float bestD = float.MaxValue;
                foreach (var lane in mall.Lanes)
                {
                    if (lane.Role != NetRole.Alley) continue;
                    for (int i = 0; i + 1 < lane.Points.Count; i++)
                        Nearest(piece, lane.Points[i], lane.Points[i + 1], lane.Half, 6f, "alley", ref bestD, ref at, ref dir, ref kind);
                }
                if (kind == null)
                    foreach (var r in roads)
                        Nearest(piece, r.A, r.B, r.HalfWidth, 10f, "entrance", ref bestD, ref at, ref dir, ref kind);
                if (kind == null) continue;
                var n = new float2(-dir.y, dir.x);
                if (!Poly.Contains(piece, at + n * 6f)) n = -n;
                if (!Poly.Contains(piece, at + n * 6f)) continue;
                // The biggest rectangle with one side on the entrance line that fits the piece and the free land.
                float2[] best = null; float bestArea = 0f;
                for (float w = 140f; w >= 32f; w -= 12f)
                    for (float t = 96f; t >= 24f; t -= 12f)
                    {
                        if (w * t <= bestArea) continue;
                        for (float off = -w * 0.5f + 8f; off <= w * 0.5f - 8f + 0.01f; off += w * 0.25f)
                        {
                            float s0 = off - w * 0.5f;
                            var rect = Poly.EnsureCcw(new[] { at + dir * s0, at + dir * (s0 + w), at + dir * (s0 + w) + n * t, at + dir * s0 + n * t });
                            if (!mall.Fits(rect)) continue;
                            if (!InsidePiece(piece, rect)) continue;
                            best = rect; bestArea = w * t; break;
                        }
                    }
                if (best == null) continue;
                if (mall.Lot(best, new List<(float2, string)> { (at, kind) }, Mall.Parking(ctx), salt + k)) any = true;
            }
            return any;
        }

        private static bool InsidePiece(float2[] piece, float2[] rect)
        {
            foreach (var p in rect) if (!Poly.Contains(piece, p + math.normalizesafe(Poly.Centroid(rect) - p) * 0.3f)) return false;
            try
            {
                var common = Poly.Boolean(new[] { (IReadOnlyList<float2>)piece }, new[] { (IReadOnlyList<float2>)rect }, BoolOp.Intersection);
                float a = 0f;
                foreach (var g in common) a += Poly.Area(g.Outer);
                return a >= Poly.Area(rect) - 2f;
            }
            catch (Exception) { return false; }
        }

        /// <summary>Is the outline of <paramref name="piece"/> within <paramref name="reach"/> m of the kerb of this segment? Keeps the closest.</summary>
        private static void Nearest(float2[] piece, float2 a, float2 b, float half, float reach, string kind, ref float bestD, ref float2 at, ref float2 dir, ref string bestKind)
        {
            for (int i = 0; i < piece.Length; i++)
            {
                float2 p0 = piece[i], p1 = piece[(i + 1) % piece.Length];
                int n = math.max(1, (int)math.ceil(math.distance(p0, p1) / 2f));
                for (int s = 0; s <= n; s++)
                {
                    var p = math.lerp(p0, p1, (float)s / n);
                    float d = math.sqrt(SegmentIndex.DistSq(p, a, b)) - half;
                    if (d < reach && d < bestD) { bestD = d; at = p; dir = math.normalizesafe(b - a); bestKind = kind; }
                }
            }
        }

        protected static int Cells(LayerContext ctx) => Mall.Cells(ctx.Raw("plotDepth"));

        protected static void LotEntrances(List<(float2, string)> list, StreetShops.Run r, float s0, float s1, float t, string kind)
        {
            float w = s1 - s0;
            if (w > 90f) { list.Add((r.At(s0 + w * 0.25f, t), kind)); list.Add((r.At(s0 + w * 0.75f, t), kind)); }
            else list.Add((r.At(0.5f * (s0 + s1), t), kind));
        }
    }
}
