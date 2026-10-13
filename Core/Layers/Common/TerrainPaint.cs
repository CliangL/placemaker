// Terrain texture painting (PlannedPaint, built by the game layer right after terraform). Used for
// "natural ground" patches, rocky outcrops and worn ground: the terrain itself changes texture, which
// reads far more natural than a surface polygon. Painting is permanent (warned). Each patch is painted
// as two rings — the full outline at half strength and an inset core at full strength — so the edge
// blends into the surrounding ground instead of ending in a hard line. Layers 1..4 are the map's
// "extra" terrain textures (map dependent; the UI calls them "Terrain layer 1–4").

using System.Collections.Generic;
using Placemaker.Core.Geometry;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Common
{
    public static class TerrainPaint
    {
        public const string PermanentKey = "Placemaker.Paint.Issue.Permanent";

        public static bool Patch(LayerContext ctx, IReadOnlyList<float2> ring, int layer, float strength = 1f, float blend = 2f)
        {
            var clean = Poly.Clean(ring, 0.05f);
            if (clean.Length < 3) return false;
            layer = math.clamp(layer, 1, 4);
            ctx.Plan.Paint.Add(new PlannedPaint { Ring = Poly.To3D(clean, ctx.H), Layer = layer, Strength = math.saturate(strength * 0.5f) });
            var region = LayerRegion.From(clean);
            if (region != null && blend > 0f)
            {
                var inset = region.InsetVertices(blend);
                if (Poly.SignedArea(inset) > 1f && Poly.IsSimple(inset))
                    ctx.Plan.Paint.Add(new PlannedPaint { Ring = Poly.To3D(inset, ctx.H), Layer = layer, Strength = math.saturate(strength) });
            }
            bool warned = false;
            foreach (var i in ctx.Plan.Issues) if (i.Key == PermanentKey) { warned = true; break; }
            if (!warned) ctx.Issue(IssueSeverity.Warning, PermanentKey);
            return true;
        }
    }
}
