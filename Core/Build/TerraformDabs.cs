// Placemaker — brush dab placement for terraforming (GPL-3.0).
// Adapted from ParkManager's ParkLakePlanner.ExcavationDabs/WaterAnchor (tomkpunkt, GPL-3.0).

using System.Collections.Generic;
using Unity.Mathematics;

namespace Placemaker.Core.Build
{
    public static class TerraformDabs
    {
        /// <summary>
        /// Brush centres covering the ring's interior on a grid (spacing 0.6 r), kept at least
        /// inset×r from the shore so the brush falloff shapes a bank instead of cutting outside.
        /// Falls back to a smaller inset for thin shapes; never returns null.
        /// </summary>
        public static List<float2> Dabs(float2[] ring, float brushRadius)
        {
            var result = new List<float2>();
            if (ring == null || ring.Length < 3 || !(brushRadius > 0.5f)) return result;
            float2 min = ring[0], max = ring[0];
            foreach (var p in ring) { min = math.min(min, p); max = math.max(max, p); }
            float spacing = brushRadius * 0.6f;
            foreach (var inset in new[] { 0.7f, 0.4f, 0f })
            {
                float minimum = brushRadius * inset;
                for (float y = min.y + spacing * 0.5f; y < max.y; y += spacing)
                    for (float x = min.x + spacing * 0.5f; x < max.x; x += spacing)
                    {
                        var p = new float2(x, y);
                        if (ReservedSpace.PointInPolygon(p, ring) && DistanceToBoundary(ring, p) >= minimum) result.Add(p);
                    }
                if (result.Count > 0) break;
            }
            if (result.Count == 0) result.Add(Anchor(ring, out _));
            return result;
        }

        /// <summary>Deepest-inside point (grid search) and its distance to the boundary.</summary>
        public static float2 Anchor(float2[] ring, out float innerRadius)
        {
            innerRadius = 0;
            if (ring == null || ring.Length == 0) return 0;
            float2 min = ring[0], max = ring[0], centroid = 0;
            foreach (var p in ring) { min = math.min(min, p); max = math.max(max, p); centroid += p; }
            centroid /= ring.Length;
            float2 best = centroid;
            float bestD = ReservedSpace.PointInPolygon(centroid, ring) ? DistanceToBoundary(ring, centroid) : -1;
            const int steps = 24;
            for (int i = 0; i <= steps; i++)
                for (int j = 0; j <= steps; j++)
                {
                    var p = math.lerp(min, max, new float2(i / (float)steps, j / (float)steps));
                    if (!ReservedSpace.PointInPolygon(p, ring)) continue;
                    float d = DistanceToBoundary(ring, p);
                    if (d > bestD) { bestD = d; best = p; }
                }
            innerRadius = math.max(0, bestD);
            return best;
        }

        public static float DistanceToBoundary(float2[] ring, float2 p)
        {
            float best = float.MaxValue;
            for (int i = 0; i < ring.Length; i++)
                best = math.min(best, math.sqrt(ReservedSpace.DistSqToSegment(p, ring[i], ring[(i + 1) % ring.Length])));
            return best;
        }
    }
}
