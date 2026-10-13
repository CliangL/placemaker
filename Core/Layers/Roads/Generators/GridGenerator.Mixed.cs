// Placemaker roads layer — mixed pattern (radial core + organic).
// Adapted from Grid Road Generator (Ti4goc), Core/GridGenerator.Mixed.cs; part of Placemaker (GPL-3.0).
// Changes for Placemaker: no static caches or global lock (thread-safe, reentrant), cooperative
// cancellation through GridParameters.Cancel, per-call diagnostics objects instead of statics.
using System.Collections.Generic;
using System.Linq;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Roads.Generators
{
    /// <summary>
    /// Motif "Misto" (retour : "Radial no centro e Orgânico à volta") : un cercle central porte le
    /// motif Radial (rotonde, avenues, anneaux) ; l'Orgânico remplit le reste de la zone, avec le
    /// cercle comme second bord ; une route circulaire suit le cercle et relie les deux, avec un
    /// nœud à chaque rue qui la rejoint (voir FreeAreaPerimeter.PerimeterRoad).
    /// </summary>
    public static partial class GridGenerator
    {
        public const float MinMixedCoreRadius = 100f;
        public const float MaxMixedCoreRadius = 450f;
        public const float MixedCoreRadiusDefault = 220f;
        /// <summary>Pas (m) entre deux points du cercle central.</summary>
        private const float MixedCircleStep = 12f;

        private static List<RoadSegmentDef> GenerateMixed(List<float2> polygon, float y, GridParameters parameters)
        {
            // Centre : barycentre (s'il est dans la forme), rayon borné par la distance au bord.
            float2 centre = Centroid(polygon);
            if (!PointInPolygon(centre, polygon))
            {
                return GenerateOrganic(polygon, y, parameters);
            }
            float clearance = float.MaxValue;
            for (int i = 0; i < polygon.Count; i++)
            {
                clearance = math.min(clearance, math.distance(centre, ClosestOnSegment(polygon[i], polygon[(i + 1) % polygon.Count], centre)));
            }
            float radius = math.min(parameters.MixedCoreRadius > 0f ? parameters.MixedCoreRadius : MixedCoreRadiusDefault, 0.6f * clearance);
            if (radius < MinMixedCoreRadius)
            {
                return GenerateOrganic(polygon, y, parameters); // forme trop étroite pour un centre
            }

            int count = math.max(24, (int)math.ceil(2f * math.PI * radius / MixedCircleStep));
            var circle = new List<float2>(count);
            for (int i = 0; i < count; i++)
            {
                float a = 2f * math.PI * i / count;
                circle.Add(centre + radius * new float2(math.cos(a), math.sin(a)));
            }
            var circle3 = circle.Select(p => new float3(p.x, y, p.y)).ToList();

            // Cœur : le Radial, avec le cercle pour périmètre.
            List<RoadSegmentDef> core = ConcentricGenerator.GenerateRadial(circle3, parameters.RadialAvenues, parameters.RadialRoundaboutRadius, parameters.RadialLayers,
                cancel: parameters.Cancel);
            // Le Radial simplifie son contour : ses bouts d'avenue tombent à quelques décimètres du
            // cercle. Ramenés exactement dessus (même point pour tous les tronçons qui s'y rejoignent),
            // pour que la route circulaire s'y raccorde.
            var snapped = new Dictionary<float3, float3>();
            float3 Snap(float3 p)
            {
                if (snapped.TryGetValue(p, out float3 known)) return known;
                float best = float.MaxValue;
                float2 closest = p.xz;
                for (int i = 0; i < circle.Count; i++)
                {
                    float2 q = ClosestOnSegment(circle[i], circle[(i + 1) % circle.Count], p.xz);
                    float d = math.distance(q, p.xz);
                    if (d < best)
                    {
                        best = d;
                        closest = q;
                    }
                }
                float3 result = best < 4f ? new float3(closest.x, p.y, closest.y) : p;
                snapped[p] = result;
                return result;
            }
            for (int k = 0; k < core.Count; k++)
            {
                RoadSegmentDef s = core[k];
                s.Start = Snap(s.Start);
                s.End = Snap(s.End);
                core[k] = s;
            }

            // Couronne : l'Orgânico dans la forme percée du cercle (polygone "trou de serrure" : bord
            // extérieur, fente de largeur nulle, cercle parcouru en entier, retour par la fente).
            (List<float2> keyhole, float2 bridgeA, float2 bridgeB) = Keyhole(polygon, circle);
            List<RoadSegmentDef> ring = GenerateOrganic(keyhole, y, parameters);
            // La fente n'est pas une vraie route : rien ne doit s'y raccorder.
            ring.RemoveAll(s => OnBridge(s.Start.xz, bridgeA, bridgeB, circle, polygon) || OnBridge(s.End.xz, bridgeA, bridgeB, circle, polygon));

            var result = new List<RoadSegmentDef>(core.Count + ring.Count);
            result.AddRange(core);
            result.AddRange(ring);
            // Les rues de l'Orgânico s'arrêtent avant tout bord, cercle compris : chaque avenue du
            // centre se prolonge vers l'extérieur jusqu'au nœud de la couronne le plus proche devant
            // elle — les avenues traversent ainsi le quartier au lieu de buter sur le cercle.
            result.AddRange(ExtendSpokes(core, ring, centre, keyhole));
            // Route circulaire : nœud à chaque rue du cœur ou de la couronne qui touche le cercle.
            foreach (RoadSegmentDef road in FreeAreaPerimeter.PerimeterRoad(circle3, result))
            {
                RoadSegmentDef avenue = road;
                avenue.IsAvenue = true;
                result.Add(avenue);
            }
            return result;
        }

        /// <summary>Longueur maximale (m) d'une avenue prolongée du cercle vers la couronne.</summary>
        private const float MixedSpokeMaxLength = 260f;
        private const float MixedSpokeMaxTurn = 30f;

        private static List<RoadSegmentDef> ExtendSpokes(List<RoadSegmentDef> core, List<RoadSegmentDef> ring, float2 centre, List<float2> keyhole)
        {
            var extensions = new List<RoadSegmentDef>();
            // Bouts libres du centre (sur le cercle) et nœuds de la couronne avec leurs directions.
            var coreDegree = new Dictionary<float3, int>();
            foreach (RoadSegmentDef s in core)
            {
                coreDegree[s.Start] = coreDegree.TryGetValue(s.Start, out int a) ? a + 1 : 1;
                coreDegree[s.End] = coreDegree.TryGetValue(s.End, out int b) ? b + 1 : 1;
            }
            var ringNodes = new Dictionary<float3, List<float2>>();
            var polylines = new List<(float2[] points, float2 min, float2 max)>();
            foreach (RoadSegmentDef s in ring)
            {
                float2[] points = Sample(s);
                float2 min = points[0], max = points[0];
                foreach (float2 q in points)
                {
                    min = math.min(min, q);
                    max = math.max(max, q);
                }
                polylines.Add((points, min, max));
                if (!ringNodes.TryGetValue(s.Start, out var la)) ringNodes[s.Start] = la = new List<float2>();
                la.Add(math.normalizesafe(points[1] - points[0]));
                if (!ringNodes.TryGetValue(s.End, out var lb)) ringNodes[s.End] = lb = new List<float2>();
                lb.Add(math.normalizesafe(points[points.Length - 2] - points[points.Length - 1]));
            }
            float maxTurnCos = math.cos(math.radians(MixedSpokeMaxTurn));
            float minSin = math.sin(math.radians(35f));
            foreach (KeyValuePair<float3, int> end in coreDegree)
            {
                if (end.Value != 1) continue;
                float2 from = end.Key.xz;
                float2 outward = math.normalizesafe(from - centre);
                float bestDistance = MixedSpokeMaxLength;
                float3 best = default;
                bool found = false;
                foreach (KeyValuePair<float3, List<float2>> node in ringNodes)
                {
                    float2 delta = node.Key.xz - from;
                    float d = math.length(delta);
                    if (d >= bestDistance || d < 20f) continue;
                    float2 direction = delta / d;
                    if (math.dot(direction, outward) < maxTurnCos) continue;
                    bool open = true;
                    foreach (float2 o in node.Value)
                    {
                        open &= math.abs(o.x * direction.y - o.y * direction.x) >= minSin || math.dot(o, -direction) < 0f;
                    }
                    if (!open || !PathClear(from, node.Key.xz, polylines, keyhole)) continue;
                    bestDistance = d;
                    best = node.Key;
                    found = true;
                }
                if (!found) continue;
                var spoke = new RoadSegmentDef(end.Key, best, isHorizontal: false, isAvenue: true);
                extensions.Add(spoke);
                // La nouvelle avenue compte pour les suivantes (pas de croisement entre deux prolongements).
                float2[] line = { from, best.xz };
                polylines.Add((line, math.min(from, best.xz), math.max(from, best.xz)));
            }
            return extensions;
        }

        private static float2 Centroid(List<float2> polygon)
        {
            float area = 0f;
            float2 sum = float2.zero;
            for (int i = 0; i < polygon.Count; i++)
            {
                float2 a = polygon[i], b = polygon[(i + 1) % polygon.Count];
                float cross = a.x * b.y - b.x * a.y;
                area += cross;
                sum += (a + b) * cross;
            }
            return math.abs(area) < 1e-3f ? polygon[0] : sum / (3f * area);
        }

        /// <summary>
        /// Polygone extérieur percé du cercle : relié par une fente entre le sommet extérieur et le point du
        /// cercle les plus proches. Avec la règle pair-impair (PointInPolygon), le disque est dehors.
        /// </summary>
        private static (List<float2> keyhole, float2 bridgeA, float2 bridgeB) Keyhole(List<float2> outer, List<float2> circle)
        {
            int bestOuter = 0, bestCircle = 0;
            float best = float.MaxValue;
            for (int i = 0; i < outer.Count; i++)
            {
                for (int j = 0; j < circle.Count; j++)
                {
                    float d = math.distancesq(outer[i], circle[j]);
                    if (d < best)
                    {
                        best = d;
                        bestOuter = i;
                        bestCircle = j;
                    }
                }
            }
            var result = new List<float2>(outer.Count + circle.Count + 2);
            for (int i = 0; i <= bestOuter; i++) result.Add(outer[i]);
            for (int k = 0; k <= circle.Count; k++) result.Add(circle[(bestCircle - k + circle.Count) % circle.Count]);
            for (int i = bestOuter; i < outer.Count; i++) result.Add(outer[i]);
            return (result, outer[bestOuter], circle[bestCircle]);
        }

        /// <summary>Point sur la fente (hors de ses deux bouts, qui sont de vrais bords).</summary>
        private static bool OnBridge(float2 p, float2 a, float2 b, List<float2> circle, List<float2> outer)
        {
            float2 closest = ClosestOnSegment(a, b, p);
            if (math.distance(closest, p) > 1f) return false;
            return math.distance(p, a) > 1f && math.distance(p, b) > 1f;
        }
    }
}
