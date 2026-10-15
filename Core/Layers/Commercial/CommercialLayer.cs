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
        protected void CommonControls(ControlSet c, string zone = ZoneKinds.CommercialLow, double depth = 24, string lotSize = "medium", bool lot = true, string lotWhen = "", double maxDepth = 40, bool green = true)
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
            // (s50) The park is not part of a shopping area: an optional extra, off unless the type has a park by design.
            if (green) Shared(c.Toggle("extraGreen", false, ControlTier.More, "layout")).Tip();
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
            // (s50) Lot first: the shop rows, access alleys and lots the composition did not reach.
            LotFirst(ctx, mall, runs);
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
        /// not left over. A piece that finds its land taken is skipped (every kit call checks the land it needs).
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
        }

        /// <summary>Compose another frontage while less than this share of the site is taken.</summary>
        private const float LeftoverMax = 0.6f;
        /// <summary>A longest straight frontage shorter than this (m) is a curved or cut-up block: every frontage gets its composition.</summary>
        private const float CurvedFrontage = 110f;

        // ───────────── (s50) lot first ─────────────

        /// <summary>The planted strip between a street and the first plots of an access alley leaving it (m).</summary>
        public const float AlleyForecourt = 4f;

        /// <summary>Does this type put a park in what is left over (by design, or the "Green" extra)?</summary>
        protected virtual bool KeepsPark(LayerContext ctx) => ctx.B("extraGreen");

        /// <summary>
        /// (s50) The lot is the base of a commercial site: every street frontage the composition left free gets shop rows
        /// round an access alley (shops on both sides, running in from the street to the lot behind), the free frontage
        /// between the alleys gets plots, and the land behind is planned as a parking lot of its own shape, entered from the
        /// alley's end (or a street). What no lot can use stays for <see cref="FillRest"/>.
        /// </summary>
        protected virtual void LotFirst(LayerContext ctx, Mall mall, List<StreetShops.Run> runs)
        {
            int cells = Cells(ctx);
            AccessAlleys(ctx, mall, runs, cells);
            ctx.Check();
            mall.StreetRows(runs, cells, null, 3, 2);
            RestLots(ctx, mall, 0xB00u);
        }

        /// <summary>
        /// Access alleys: on every run, one every ~100 m, a short lane straight in from the street with a row of plots on each
        /// side, ending where the land behind is deep enough for a lot (the lot is planned later from the leftover piece).
        /// </summary>
        protected void AccessAlleys(LayerContext ctx, Mall mall, List<StreetShops.Run> runs, int cells)
        {
            var (asset, h) = Mall.ShopLane(ctx);
            float d = cells * Mall.Cell;
            foreach (var r in runs)
            {
                ctx.Check();
                float from = r.From + (r.StartCorner ? Mall.CornerClear : 2f), to = r.To - (r.EndCorner ? Mall.CornerClear : 2f);
                float span = to - from;
                float need = 2f * (h + d) + 2f * Mall.Cell;
                if (span < need + 8f) continue;
                int n = span > 280f ? 3 : span > 150f ? 2 : 1;
                for (int k = 0; k < n; k++)
                {
                    float m = from + span * (k + 0.5f) / n;
                    float reach = mall.ReachAt(r, m);
                    // The alley needs the rows' depth and a lot (one aisle at least) behind it.
                    float lane = math.min(math.floor((reach - Mall.LotDepth(1) - 2f) / Mall.Cell) * Mall.Cell, 6f * Mall.Cell);
                    if (lane < d + Mall.Cell) continue;
                    var foot = Mall.Rect(r, m - h - d - 1f, m + h + d + 1f, 0f, lane);
                    if (!mall.Fits(foot)) continue;
                    var al = mall.AddLane(r.At(m, 0f), -r.In, new[] { r.At(m, lane) }, null, default, NetRole.Alley, asset, h);
                    if (al == null) continue;
                    mall.LaneRows(al, cells, null, 0, mall.GateTrim(al, true, AlleyForecourt), 0f, 3, 2);
                }
            }
        }

        /// <summary>
        /// Lots on what is left: every piece of at least <paramref name="minArea"/> that a street or lane can reach becomes a
        /// parking lot of its own shape (largest first; each lot changes what is left, so the pieces are found again).
        /// <paramref name="keepLargest"/>: the largest piece stays free (for a park).
        /// </summary>
        protected static void RestLots(LayerContext ctx, Mall mall, uint salt, float minArea = 500f, bool keepLargest = false)
        {
            var failed = new HashSet<long>();
            for (int pass = 0; pass < 24; pass++)
            {
                ctx.Check();
                bool any = false;
                var pieces = mall.RestPieces(minArea);
                int skip = keepLargest ? 1 : 0;
                for (int i = skip; i < pieces.Count; i++)
                {
                    var piece = pieces[i];
                    var c = Poly.Centroid(piece);
                    long key = ((long)(Poly.Area(piece) * 10f) * 73856093L) ^ ((long)(c.x * 100f) * 19349663L) ^ ((long)(c.y * 100f) * 83492791L);
                    if (failed.Contains(key)) continue;
                    bool okLot = mall.PolyLot(piece, Mall.Parking(ctx), salt + (uint)pass * 0x10u + (uint)i);
                    if (Environment.GetEnvironmentVariable("PM_TRACE") != null) Console.WriteLine($"  RESTLOT pass {pass} piece {i} area {Poly.Area(piece):0} entries {mall.Entries(piece).Count} -> {okLot} " + (okLot ? "" : string.Join(" ", System.Linq.Enumerable.Select(piece, q => $"({q.x:0},{q.y:0})"))));
                    if (okLot) { any = true; break; }
                    failed.Add(key);
                }
                if (!any) break;
            }
        }

        /// <summary>The lots' planted edges: what no lot could use is lawn and trees, never paths (a buffer, not a park).</summary>
        protected static void LotEdges(LayerContext ctx, Mall mall, uint salt = 0x980u)
        {
            uint k = 0;
            foreach (var piece in mall.Rest(60f))
            {
                ctx.Check();
                mall.Buffer(piece, salt + k++);
            }
        }

        /// <summary>
        /// (s50) What is left after the lots: more lot, then planted edges. A park only where the type has one by design
        /// (they override this) or the "Green" extra asks for one (the largest wide leftover piece, like the s32 types').
        /// </summary>
        protected virtual void FillRest(LayerContext ctx, Mall mall)
        {
            bool park = KeepsPark(ctx);
            RestLots(ctx, mall, 0xB80u, 500f, park);
            if (park) GreenRest(ctx, mall, "pocket", false, 0x900u);
            LotEdges(ctx, mall);
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


        protected static int Cells(LayerContext ctx) => Mall.Cells(ctx.Raw("plotDepth"));

        protected static void LotEntrances(List<(float2, string)> list, StreetShops.Run r, float s0, float s1, float t, string kind)
        {
            float w = s1 - s0;
            if (w > 90f) { list.Add((r.At(s0 + w * 0.25f, t), kind)); list.Add((r.At(s0 + w * 0.75f, t), kind)); }
            else list.Add((r.At(0.5f * (s0 + s1), t), kind));
        }
    }
}
