// Shared craft for the add-ons (art pass a6): a feature should look laid out, not dropped on the lawn.
//   • Link: a paved strip from the nearest path to the feature's own ground, so every kiosk,
//     restroom, bandstand or fountain is reached by a hard surface (surfaces only — no extra nets,
//     so the parent's path network and its prefab stay untouched).
//   • Arrival: where people come from (the park's spur end at the place, else the nearest path point).

using Placemaker.Core.Layers.Common;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.AddOns
{
    public static class AddOnKit
    {
        /// <summary>Nearest point on a reserved path within <paramref name="cap"/> metres, or false.</summary>
        public static bool NearestPath(Occupancy occ, float2 p, float cap, out float2 onPath)
        {
            onPath = p;
            return occ.Paths.Count > 0 && occ.Paths.Nearest(p, cap, out onPath) >= 0;
        }

        /// <summary>
        /// A paved strip from the path point <paramref name="from"/> to the edge of a feature's ground
        /// (a disc of <paramref name="radius"/> round <paramref name="centre"/>). Skipped when the feature
        /// already touches the path or the path is far (then it is not "on the way").
        /// </summary>
        public static bool Link(LayerContext ctx, float2 from, float2 centre, float radius, float halfWidth, AssetRef asset, string category,
            float maxLength = 18f)
        {
            float d = math.distance(from, centre);
            float len = d - radius;
            if (len < 0.6f || len > maxLength) return false;
            var dir = (centre - from) / math.max(1e-4f, d);
            // From inside the path (half a metre past its centre line) to a metre into the feature's ground.
            var a = from - dir * 0.5f;
            var b = centre - dir * math.max(0f, radius - 1f);
            var mid = (a + b) * 0.5f;
            return ctx.Surface(LayerRegion.Rect(mid, dir, math.distance(a, b) * 0.5f, halfWidth), SurfaceRole.Paving, asset, category);
        }

        /// <summary>Link to the nearest path (if any within reach).</summary>
        public static bool LinkToPath(LayerContext ctx, Occupancy occ, float2 centre, float radius, float halfWidth, AssetRef asset, string category,
            float maxLength = 18f)
        {
            if (!NearestPath(occ, centre, radius + maxLength, out var q)) return false;
            return Link(ctx, q, centre, radius, halfWidth, asset, category, maxLength);
        }
    }
}
