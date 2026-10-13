using System.Collections.Generic;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Roads.Generators
{
    /// <summary>
    /// Motif "Cul-de-sac em árvore" (famille de la Grelha, LoopMode faux) : une collectrice
    /// (IsAvenue) le long du grand axe de la forme, reliée au périmètre à ses deux bouts ; des
    /// branches perpendiculaires (réseau principal) des deux côtés, décalées d'une demi-distance
    /// (carrefours en T) et qui n'atteignent jamais le périmètre ; des impasses (IsCulDeSacEnd :
    /// réseau cul-de-sac et cercle de retournement) par paires le long de chaque branche. Une
    /// branche se termine à sa dernière paire d'impasses. Tout le trafic passe par la collectrice.
    /// </summary>
    public static partial class GridGenerator
    {
        public const float MinTreeBranchSpacing = 160f;
        public const float MaxTreeBranchSpacing = 400f;
        public const float TreeBranchSpacingDefault = 240f;
        public const float MinTreeCulDeSacSpacing = 60f;
        public const float MaxTreeCulDeSacSpacing = 150f;
        public const float TreeCulDeSacSpacingDefault = 80f;
        public const float MinTreeCulDeSacLength = 40f;
        public const float MaxTreeCulDeSacLength = 150f;
        public const float TreeCulDeSacLengthDefault = 90f;

        /// <summary>Distance minimale (m) entre une rue générée et la route du périmètre, hors raccord (même valeur que le Radial).</summary>
        private const float TreeClearance = 25f;
        /// <summary>Distance (m) entre un bout de rue et le périmètre : place du cercle de retournement.</summary>
        private const float TreeTipClearance = 30f;
        /// <summary>Écart (m) entre les bouts de deux impasses face à face (branches voisines).</summary>
        private const float TreeFacingTipGap = 40f;
        private const float TreeSampleStep = 6f;

        private static float TreeSetting(float value, float fallback, float min, float max) =>
            math.clamp(value > 0f ? value : fallback, min, max);

        private static List<RoadSegmentDef> GenerateTree(List<float2> polygon, float y, GridParameters parameters)
        {
            var segments = new List<RoadSegmentDef>();
            float branchSpacing = TreeSetting(parameters.TreeBranchSpacing, TreeBranchSpacingDefault, MinTreeBranchSpacing, MaxTreeBranchSpacing);
            float culDeSacSpacing = TreeSetting(parameters.TreeCulDeSacSpacing, TreeCulDeSacSpacingDefault, MinTreeCulDeSacSpacing, MaxTreeCulDeSacSpacing);
            float culDeSacLength = TreeSetting(parameters.TreeCulDeSacLength, TreeCulDeSacLengthDefault, MinTreeCulDeSacLength, MaxTreeCulDeSacLength);
            // Impasses de deux branches voisines (même côté, branchSpacing d'écart) face à face.
            culDeSacLength = math.min(culDeSacLength, 0.5f * (branchSpacing - TreeFacingTipGap));

            // Repère : u le long du grand axe (collectrice), v en travers.
            float2 axis = LongestAxis(polygon, parameters.AngleOffsetDegrees);
            float2 side = new float2(-axis.y, axis.x);
            float2 origin = polygon[0];
            var local = new List<float2>(polygon.Count);
            float vMin = float.MaxValue, vMax = float.MinValue;
            foreach (float2 p in polygon)
            {
                var lp = new float2(math.dot(p - origin, axis), math.dot(p - origin, side));
                local.Add(lp);
                vMin = math.min(vMin, lp.y);
                vMax = math.max(vMax, lp.y);
            }

            // Collectrice : la droite (v = const) qui traverse la forme sur la plus grande longueur
            // d'un seul tenant, dans la moitié centrale ; à égalité, la plus centrée.
            float mid = 0.5f * (vMin + vMax);
            float height = vMax - vMin;
            float spineV = mid;
            float2 spine = default;
            float bestScore = float.MinValue;
            const int candidates = 40;
            for (int k = 0; k <= candidates; k++)
            {
                float v = math.lerp(vMin + 0.25f * height, vMax - 0.25f * height, (float)k / candidates);
                foreach (float2 interval in ClipLineToPolygon(local, axisIsU: false, position: v))
                {
                    float score = (interval.y - interval.x) - 0.5f * math.abs(v - mid);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        spineV = v;
                        spine = interval;
                    }
                }
            }
            if (bestScore == float.MinValue || spine.y - spine.x < 2f * MinTreeCulDeSacSpacing)
            {
                return segments;
            }

            // Branches : un côté tous les branchSpacing, l'autre décalé d'une demi-distance (T).
            float margin = math.max(0.5f * branchSpacing, TreeClearance + culDeSacLength);
            var branches = new List<(float u, float dir)>();
            float usable = spine.y - spine.x - 2f * margin;
            if (usable >= 0f)
            {
                int count = (int)math.floor(usable / branchSpacing) + 1;
                float first = 0.5f * (spine.x + spine.y) - 0.5f * (count - 1) * branchSpacing;
                for (int k = 0; k < count; k++)
                {
                    branches.Add((first + k * branchSpacing, 1f));
                }
                for (int k = 0; k <= count; k++)
                {
                    float u = first + (k - 0.5f) * branchSpacing;
                    if (u >= spine.x + margin - 1e-3f && u <= spine.y - margin + 1e-3f)
                    {
                        branches.Add((u, -1f));
                    }
                }
            }

            var edges = new TreeEdgeIndex(local);
            var collectorJunctions = new SortedDictionary<float, float3>();
            float3 World(float u, float v) => ToWorld(origin, axis, side, u, v, y);

            foreach ((float u, float dir) in branches)
            {
                // Longueur utile de la branche : jusqu'au périmètre (bande dégagée), moins la marge.
                if (!TryFreeRun(edges, new float2(u, spineV), new float2(0f, dir), out float branchRun))
                {
                    continue;
                }
                float maxBranch = branchRun - TreeTipClearance;

                // Paires d'impasses le long de la branche.
                var junctions = new List<float3>();
                var culDeSacs = new List<RoadSegmentDef>();
                for (float t = culDeSacSpacing; t <= maxBranch + 1e-3f; t += culDeSacSpacing)
                {
                    var at = new float2(u, spineV + dir * t);
                    float3 junction = World(at.x, at.y);
                    bool any = false;
                    foreach (float along in new[] { 1f, -1f })
                    {
                        if (!TryFreeRun(edges, at, new float2(along, 0f), out float run))
                        {
                            continue;
                        }
                        float length = math.min(culDeSacLength, run - TreeTipClearance);
                        if (length < MinTreeCulDeSacLength)
                        {
                            continue;
                        }
                        culDeSacs.Add(new RoadSegmentDef(junction, World(at.x + along * length, at.y), isHorizontal: true, isCulDeSacEnd: true));
                        any = true;
                    }
                    if (any)
                    {
                        junctions.Add(junction);
                    }
                }
                if (junctions.Count == 0)
                {
                    continue; // branche sans impasse : rien à desservir
                }

                // Branche : de la collectrice à sa dernière paire d'impasses, coupée à chaque paire.
                if (!collectorJunctions.TryGetValue(u, out float3 start))
                {
                    start = World(u, spineV);
                    collectorJunctions[u] = start;
                }
                float3 previous = start;
                foreach (float3 junction in junctions)
                {
                    segments.Add(new RoadSegmentDef(previous, junction, isHorizontal: false));
                    previous = junction;
                }
                segments.AddRange(culDeSacs);
            }

            // Collectrice : d'un bout à l'autre, coupée à chaque branche.
            var stops = new List<float3> { World(spine.x, spineV) };
            stops.AddRange(collectorJunctions.Values);
            stops.Add(World(spine.y, spineV));
            for (int i = 0; i + 1 < stops.Count; i++)
            {
                segments.Add(new RoadSegmentDef(stops[i], stops[i + 1], isHorizontal: true, isAvenue: true));
            }
            return segments;
        }

        /// <summary>
        /// Arêtes du contour rangées par tranches de TreeClearance, sur chacun des deux axes : une
        /// requête sur une droite (u = const ou v = const) ne regarde que les arêtes de sa bande
        /// (retour "lentíssimo" de l'ancien motif Espinha de peixe : parcourir les ~1000 arêtes pour
        /// chaque rue coûtait des centaines de ms par génération).
        /// </summary>
        private sealed class TreeEdgeIndex
        {
            private readonly List<float2> _local;
            private readonly Dictionary<int, List<int>>[] _buckets = { new Dictionary<int, List<int>>(), new Dictionary<int, List<int>>() };

            public TreeEdgeIndex(List<float2> local)
            {
                _local = local;
                for (int i = 0; i < local.Count; i++)
                {
                    float2 a = local[i], b = local[(i + 1) % local.Count];
                    for (int axis = 0; axis < 2; axis++)
                    {
                        int lo = Bucket(math.min(a[axis], b[axis]));
                        int hi = Bucket(math.max(a[axis], b[axis]));
                        for (int k = lo; k <= hi; k++)
                        {
                            if (!_buckets[axis].TryGetValue(k, out List<int> list))
                            {
                                _buckets[axis][k] = list = new List<int>();
                            }
                            list.Add(i);
                        }
                    }
                }
            }

            private static int Bucket(float value) => (int)math.floor(value / TreeClearance);

            public float2 A(int edge) => _local[edge];
            public float2 B(int edge) => _local[(edge + 1) % _local.Count];

            /// <summary>Arêtes dont l'étendue sur l'axe `axis` (0 = u, 1 = v) touche [position − margin, position + margin].</summary>
            public List<int> Near(int axis, float position, float margin)
            {
                var result = new List<int>();
                var seen = new HashSet<int>();
                for (int k = Bucket(position - margin); k <= Bucket(position + margin); k++)
                {
                    if (!_buckets[axis].TryGetValue(k, out List<int> list))
                    {
                        continue;
                    }
                    foreach (int edge in list)
                    {
                        float2 a = A(edge), b = B(edge);
                        if (math.min(a[axis], b[axis]) <= position + margin && math.max(a[axis], b[axis]) >= position - margin && seen.Add(edge))
                        {
                            result.Add(edge);
                        }
                    }
                }
                return result;
            }
        }

        /// <summary>
        /// Longueur (m) de rue possible depuis `from` dans la direction (axe local) `direction` :
        /// jusqu'à la sortie de la forme ou jusqu'au premier point à moins de TreeClearance du
        /// périmètre. Faux si `from` n'est pas dans la forme.
        /// </summary>
        private static bool TryFreeRun(TreeEdgeIndex edges, float2 from, float2 direction, out float run)
        {
            run = 0f;
            bool alongU = direction.x != 0f;
            int across = alongU ? 1 : 0; // axe constant le long de la droite
            int along = 1 - across;
            float position = from[across];
            float t0 = from[along];
            float sign = direction[along];
            List<int> near = edges.Near(across, position, TreeClearance);

            // Sortie de la forme : croisements de la droite avec les arêtes (règle demi-ouverte,
            // comme ClipLineToPolygon), puis parité pour savoir si `from` est dedans.
            var crossings = new List<float>();
            foreach (int edge in near)
            {
                float2 a = edges.A(edge), b = edges.B(edge);
                float c1 = a[across], c2 = b[across];
                if (!((c1 <= position && position < c2) || (c2 <= position && position < c1)))
                {
                    continue;
                }
                float f = (position - c1) / (c2 - c1);
                crossings.Add(a[along] + f * (b[along] - a[along]));
            }
            int before = 0;
            float end = sign > 0f ? float.MaxValue : float.MinValue;
            foreach (float c in crossings)
            {
                if (c < t0) before++;
                if (sign > 0f && c > t0) end = math.min(end, c);
                if (sign < 0f && c < t0) end = math.max(end, c);
            }
            if (before % 2 == 0 || end == float.MaxValue || end == float.MinValue)
            {
                return false; // `from` hors de la forme
            }
            float available = math.abs(end - t0);
            run = available;
            // Étendue de chaque arête le long de la droite, élargie de TreeClearance : un échantillon
            // hors de cette étendue ne peut pas être trop près de l'arête (test bon marché d'abord).
            var lo = new float[near.Count];
            var hi = new float[near.Count];
            for (int i = 0; i < near.Count; i++)
            {
                float2 a = edges.A(near[i]), b = edges.B(near[i]);
                lo[i] = math.min(a[along], b[along]) - TreeClearance;
                hi[i] = math.max(a[along], b[along]) + TreeClearance;
            }
            for (float t = TreeSampleStep; t < available; t += TreeSampleStep)
            {
                float2 q = from + direction * t;
                float qa = q[along];
                for (int i = 0; i < near.Count; i++)
                {
                    if (qa < lo[i] || qa > hi[i])
                    {
                        continue;
                    }
                    if (math.distancesq(ClosestOnSegment(edges.A(near[i]), edges.B(near[i]), q), q) < TreeClearance * TreeClearance)
                    {
                        run = t;
                        return true;
                    }
                }
            }
            return true;
        }

        private static float2 ClosestOnSegment(float2 a, float2 b, float2 p)
        {
            float2 ab = b - a;
            float lengthSq = math.lengthsq(ab);
            float t = lengthSq < 1e-9f ? 0f : math.saturate(math.dot(p - a, ab) / lengthSq);
            return a + t * ab;
        }

        /// <summary>
        /// Grand axe de la forme, tourné de angleOffsetDegrees : la direction le long de laquelle la
        /// forme est la plus ÉTROITE en travers (bande de largeur minimale) — pas la direction de
        /// plus grande étendue, qui pour un rectangle serait la diagonale.
        /// </summary>
        private static float2 LongestAxis(List<float2> polygon, float angleOffsetDegrees)
        {
            float bestAngle = 0f;
            float bestWidth = float.MaxValue;
            for (int tenths = 0; tenths < 1800; tenths += 5)
            {
                float a = math.radians(tenths * 0.1f);
                var across = new float2(-math.sin(a), math.cos(a));
                float lo = float.MaxValue, hi = float.MinValue;
                foreach (float2 p in polygon)
                {
                    float t = math.dot(p, across);
                    lo = math.min(lo, t);
                    hi = math.max(hi, t);
                }
                if (hi - lo < bestWidth - 1e-3f)
                {
                    bestWidth = hi - lo;
                    bestAngle = a;
                }
            }
            float angle = bestAngle + math.radians(angleOffsetDegrees);
            return new float2(math.cos(angle), math.sin(angle));
        }
    }
}
