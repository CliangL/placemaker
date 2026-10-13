// Pond layer (Fill, hidden from the Make row): turns a basin ring into water — permanent
// terraform plus a water source, with the permanent-change warning. Used by the park's "pond"
// feature: the park shapes the basin between its paths (ParkLakePlanner) and hands it over as
// the "place:pond" region.

using System;
using Placemaker.Core.Layers.Common;

namespace Placemaker.Core.Layers.Park
{
    [AreaGenerator(45, Hidden = true)]
    public sealed class PondLayer : LayerBase
    {
        public const string LayerId = "pond";

        protected override string Area => "Pond";
        public override string Id => LayerId;
        public override string Icon => "Media/Game/Icons/Water.svg";
        public override LayerKind Kind => LayerKind.Fill;
        public override string[] Accepts => new[] { ParkLayer.PondPlace, "water" };
        public override float MinArea => 100f;

        protected override void DefineControls(ControlSet c)
        {
            c.Slider("depth", 3, 1, 8, 0.5, "m", ControlTier.Essential, "water").Tip();
            c.Fixed("depth");
        }

        protected override void PlanCore(LayerContext ctx)
        {
            ParkLayer.EmitLake(ctx, ctx.LayerRegion.Ring, Math.Max(1f, ctx.F("depth")));
            ctx.Plan.Facts["pondArea"] = ctx.LayerRegion.Area;
        }
    }
}
