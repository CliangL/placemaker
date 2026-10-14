// s14: the "Commercial" category (Make-row group "commercial"). Each type is a composition built with the
// Mall kit: lanes, zoned shop rows (cutouts), parking lots from the Parking planner and green / plaza pieces
// from the Park, Plaza and Market square planners. Shared controls read the same on every type
// (labels Placemaker.Commercial.<id>, zone type Placemaker.Zone.*).

using System;
using System.Collections.Generic;
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
        protected void CommonControls(ControlSet c, string zone = ZoneKinds.CommercialLow, double depth = 24, string lotSize = "medium", bool lot = true, string lotWhen = "")
        {
            var z = EdgeZoningAddOn.ZoneKindControl("zoneKind", "shops", zone);
            c.List.Add(z);
            Shared(c.Slider("plotDepth", depth, 8, 40, 8, "m", ControlTier.Essential, "shops")).Tip();
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
            var mall = new Mall(ctx);
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
            ctx.Check();
            FillRest(ctx, mall);
            mall.Finish(ZoneKinds.PrefabId(ctx.S("zoneKind")));
            CommonStats(ctx);
        }

        protected abstract void Compose(LayerContext ctx, Mall mall, List<StreetShops.Run> runs);

        /// <summary>What is left becomes small parks (pocket style); slivers stay bare ground.</summary>
        protected virtual void FillRest(LayerContext ctx, Mall mall)
        {
            uint k = 0;
            foreach (var (piece, strip) in mall.RestSplit(150f))
            {
                ctx.Check();
                // Thin arms (beside a lane leg, behind a row) are a planted buffer, not a park with paths hugging the edge.
                if (strip || mall.IsStrip(piece) || !mall.Fill(piece, "park", "pocket", Doors(mall), 0x900u + k)) mall.Buffer(piece, 0x980u + k);
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
