// Placemaker roads layer — drawn-area outline smoothing and perimeter road.
// Adapted from Grid Road Generator (Ti4goc), Core/FreeAreaPerimeter.cs (MIT); part of Placemaker (GPL-3.0).
// Changes for Placemaker: no static caches or global lock (thread-safe, reentrant), cooperative
// cancellation through GridParameters.Cancel, per-call diagnostics objects instead of statics.
using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Roads.Generators
{
    /// <summary>
    /// Mode "Área livre" : le joueur clique des points sur le terrain au lieu de choisir des nœuds
    /// de routes existantes. Le polygone cliqué est arrondi aux coins (Smooth) pour servir de
    /// périmètre aux générateurs, puis une route de périmètre est posée le long de ce contour
    /// (PerimeterRoad), coupée à chaque point où une rue générée le rejoint.
    /// </summary>
    public static class FreeAreaPerimeter
    {
        /// <summary>Rayon (m) des coins arrondis du contour.</summary>
        public const float CornerRadius = 40f;
        /// <summary>Pas (m) des points le long d'un coin arrondi.</summary>
        public const float ArcStep = 8f;
        /// <summary>Distance (m) au premier point sous laquelle un clic referme la zone.</summary>
        public const float CloseDistance = 20f;
        /// <summary>Distance minimale (m) entre deux points cliqués consécutifs.</summary>
        public const float MinPointDistance = 10f;
        /// <summary>Longueur maximale (m) d'un tronçon de la route de périmètre.</summary>
        public const float MaxPieceLength = 90f;
        /// <summary>Virage maximal (degrés) le long d'un tronçon de la route de périmètre.</summary>
        public const float MaxPieceTurnDegrees = 40f;
        /// <summary>Distance (m) sous laquelle une extrémité de rue générée est sur le contour.</summary>
        private const float OnRingDistance = 0.5f;
        /// <summary>Côté du contour (m) au-delà duquel il est droit (les coins arrondis ont des pas de ArcStep).</summary>
        private const float StraightEdgeLength = 20f;
        /// <summary>Distance minimale (m) entre un nœud de transition droit/coin et un autre nœud.</summary>
        private const float MinTransitionGap = 15f;
        /// <summary>Longueur minimale (m) d'un tronçon issu d'une découpe (hors raccords imposés par les rues).</summary>
        private const float MinPieceLength = 40f;

        /// <summary>Vrai si le polygone a au moins 3 points, une aire suffisante et aucun côté qui en croise un autre.</summary>
        public static bool IsSimple(IReadOnlyList<float3> polygon)
        {
            int n = polygon.Count;
            if (n < 3)
            {
                return false;
            }
            float area = 0f;
            for (int i = 0; i < n; i++)
            {
                float2 a = polygon[i].xz, b = polygon[(i + 1) % n].xz;
                area += a.x * b.y - b.x * a.y;
            }
            if (math.abs(area) * 0.5f < 2000f)
            {
                return false;
            }
            for (int i = 0; i < n; i++)
            {
                float2 a1 = polygon[i].xz, a2 = polygon[(i + 1) % n].xz;
                for (int j = i + 1; j < n; j++)
                {
                    // Côtés voisins : partagent un sommet, jamais un vrai croisement.
                    if (j == i + 1 || (i == 0 && j == n - 1))
                    {
                        continue;
                    }
                    if (Cross(a1, a2, polygon[j].xz, polygon[(j + 1) % n].xz))
                    {
                        return false;
                    }
                }
            }
            return true;
        }

        private static bool Cross(float2 a1, float2 a2, float2 b1, float2 b2)
        {
            float Orient(float2 a, float2 b, float2 c) => (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
            float d1 = Orient(b1, b2, a1), d2 = Orient(b1, b2, a2), d3 = Orient(a1, a2, b1), d4 = Orient(a1, a2, b2);
            return ((d1 > 0f) != (d2 > 0f)) && ((d3 > 0f) != (d4 > 0f));
        }

        /// <summary>
        /// Remplissage de l'aperçu d'un polygone (règle pair-impair) : bandes horizontales (xz) espacées
        /// de step m, d'au plus maxLength m. Dessinées en lignes projetées de largeur step, elles
        /// couvrent l'intérieur (même principe que BrushMask.FillRuns).
        /// </summary>
        public static List<(float2 a, float2 b)> FillRuns(IReadOnlyList<float3> polygon, float step = 4f, float maxLength = 64f)
        {
            var result = new List<(float2, float2)>();
            int n = polygon.Count;
            if (n < 3)
            {
                return result;
            }
            float minZ = float.MaxValue, maxZ = float.MinValue;
            foreach (float3 p in polygon)
            {
                minZ = math.min(minZ, p.z);
                maxZ = math.max(maxZ, p.z);
            }
            var crossings = new List<float>();
            for (float z = (math.floor(minZ / step) + 0.5f) * step; z < maxZ; z += step)
            {
                crossings.Clear();
                for (int i = 0; i < n; i++)
                {
                    float2 a = polygon[i].xz, b = polygon[(i + 1) % n].xz;
                    if ((a.y > z) != (b.y > z))
                    {
                        crossings.Add(a.x + (z - a.y) / (b.y - a.y) * (b.x - a.x));
                    }
                }
                crossings.Sort();
                for (int k = 0; k + 1 < crossings.Count; k += 2)
                {
                    float from = crossings[k], to = crossings[k + 1];
                    int pieces = math.max(1, (int)math.ceil((to - from) / maxLength));
                    for (int piece = 0; piece < pieces; piece++)
                    {
                        result.Add((new float2(math.lerp(from, to, (float)piece / pieces), z),
                            new float2(math.lerp(from, to, (float)(piece + 1) / pieces), z)));
                    }
                }
            }
            return result;
        }

        /// <summary>
        /// Contour arrondi : chaque coin est remplacé par un arc de cercle tangent aux deux côtés
        /// (rayon CornerRadius, réduit si les côtés sont trop courts pour le loger), échantillonné
        /// tous les ArcStep mètres. Les côtés droits restent un seul segment.
        /// </summary>
        public static List<float3> Smooth(IReadOnlyList<float3> corners, float radius = CornerRadius, float step = ArcStep)
        {
            int n = corners.Count;
            var result = new List<float3>();
            for (int i = 0; i < n; i++)
            {
                float3 prev = corners[(i + n - 1) % n], cur = corners[i], next = corners[(i + 1) % n];
                float2 toPrev = prev.xz - cur.xz, toNext = next.xz - cur.xz;
                float lenPrev = math.length(toPrev), lenNext = math.length(toNext);
                if (lenPrev < 1e-3f || lenNext < 1e-3f)
                {
                    continue;
                }
                float2 u = toPrev / lenPrev, v = toNext / lenNext;
                float angle = math.acos(math.clamp(math.dot(u, v), -1f, 1f));
                if (angle > math.radians(175f))
                {
                    result.Add(cur); // presque droit : rien à arrondir
                    continue;
                }
                float half = angle * 0.5f;
                float tangentDistance = radius / math.tan(half);
                float maxDistance = 0.45f * math.min(lenPrev, lenNext);
                float r = radius;
                if (tangentDistance > maxDistance)
                {
                    tangentDistance = maxDistance;
                    r = tangentDistance * math.tan(half);
                }
                float2 t1 = cur.xz + u * tangentDistance, t2 = cur.xz + v * tangentDistance;
                float2 centre = cur.xz + math.normalize(u + v) * (r / math.sin(half));
                float a1 = math.atan2(t1.y - centre.y, t1.x - centre.x);
                float a2 = math.atan2(t2.y - centre.y, t2.x - centre.x);
                float sweep = a2 - a1;
                while (sweep > math.PI) sweep -= 2f * math.PI;
                while (sweep < -math.PI) sweep += 2f * math.PI;
                int steps = math.max(1, (int)math.ceil(math.abs(sweep) * r / step));
                for (int k = 0; k <= steps; k++)
                {
                    float a = a1 + sweep * k / steps;
                    result.Add(new float3(centre.x + r * math.cos(a), cur.y, centre.y + r * math.sin(a)));
                }
            }
            // Points confondus (fin d'un arc = début du suivant quand un côté est entièrement arrondi).
            for (int i = result.Count - 1; i > 0 && result.Count > 3; i--)
            {
                if (math.distance(result[i].xz, result[i - 1].xz) < 0.5f)
                {
                    result.RemoveAt(i);
                }
            }
            if (result.Count > 3 && math.distance(result[0].xz, result[result.Count - 1].xz) < 0.5f)
            {
                result.RemoveAt(result.Count - 1);
            }
            return result;
        }

        /// <summary>
        /// Route de périmètre le long du contour fermé `ring` : un nœud à chaque extrémité de rue
        /// générée posée sur le contour (même float3 exact, pour que le jeu les relie), et des
        /// tronçons courbes entre deux nœuds — recoupés pour rester sous MaxPieceLength et
        /// MaxPieceTurnDegrees, afin que la courbe du jeu (NetUtils.FitCurve) colle au contour.
        /// </summary>
        public static List<RoadSegmentDef> PerimeterRoad(IReadOnlyList<float3> ring, IReadOnlyList<RoadSegmentDef> interior,
            IReadOnlyList<float3> extraNodes = null, float maxLength = MaxPieceLength, float maxTurnDegrees = MaxPieceTurnDegrees)
        {
            var result = new List<RoadSegmentDef>();
            int n = ring.Count;
            if (n < 3)
            {
                return result;
            }

            // Points de raccord : extrémités des rues générées posées sur le contour.
            var splitsByEdge = new List<(float t, float3 p)>[n];
            var seen = new HashSet<float3>();
            void AddSplit(float3 p)
            {
                if (seen.Contains(p))
                {
                    return;
                }
                int bestEdge = -1;
                float bestT = 0f, best = OnRingDistance;
                for (int i = 0; i < n; i++)
                {
                    float2 a = ring[i].xz, ab = ring[(i + 1) % n].xz - a;
                    float lengthSq = math.lengthsq(ab);
                    float t = lengthSq > 1e-6f ? math.saturate(math.dot(p.xz - a, ab) / lengthSq) : 0f;
                    float d = math.distance(p.xz, a + t * ab);
                    if (d < best)
                    {
                        best = d;
                        bestEdge = i;
                        bestT = t;
                    }
                }
                if (bestEdge < 0)
                {
                    return;
                }
                seen.Add(p);
                (splitsByEdge[bestEdge] ??= new List<(float, float3)>()).Add((bestT, p));
            }
            foreach (RoadSegmentDef s in interior)
            {
                AddSplit(s.Start);
                AddSplit(s.End);
            }
            // Nœuds imposés (ex. routes existantes qui traversent le contour).
            if (extraNodes != null)
            {
                foreach (float3 p in extraNodes)
                {
                    AddSplit(p);
                }
            }

            // Séquence fermée : sommets du contour + raccords, dans l'ordre.
            var seq = new List<(float3 p, bool node)>();
            for (int i = 0; i < n; i++)
            {
                seq.Add((ring[i], false));
                List<(float t, float3 p)> splits = splitsByEdge[i];
                if (splits == null)
                {
                    continue;
                }
                splits.Sort((a, b) => a.t.CompareTo(b.t));
                foreach ((float _, float3 p) in splits)
                {
                    seq.Add((p, true));
                }
            }
            // Un sommet du contour collé à un raccord ferait un tronçon minuscule : on l'enlève.
            for (int i = seq.Count - 1; i >= 0 && seq.Count > 3; i--)
            {
                if (seq[i].node)
                {
                    continue;
                }
                (float3 p, bool node) before = seq[(i + seq.Count - 1) % seq.Count], after = seq[(i + 1) % seq.Count];
                if ((before.node && math.distance(before.p.xz, seq[i].p.xz) < 1f) || (after.node && math.distance(after.p.xz, seq[i].p.xz) < 1f))
                {
                    seq.RemoveAt(i);
                }
            }
            // Passage côté droit ↔ coin arrondi : un nœud, pour que chaque tronçon soit droit ou un
            // arc régulier — la courbe du jeu suit mal un tronçon moitié droit, moitié courbe.
            int total = seq.Count;
            bool NodeWithin(int i, float distance)
            {
                foreach (int direction in new[] { 1, -1 })
                {
                    float walked = 0f;
                    for (int k = 1; k < total && walked < distance; k++)
                    {
                        int a = (i + direction * (k - 1) + total * k) % total, b = (i + direction * k + total * k) % total;
                        walked += math.distance(seq[a].p.xz, seq[b].p.xz);
                        if (walked < distance && seq[b].node) return true;
                    }
                }
                return false;
            }
            for (int i = 0; i < total; i++)
            {
                if (seq[i].node) continue;
                float before = math.distance(seq[(i + total - 1) % total].p.xz, seq[i].p.xz);
                float after = math.distance(seq[i].p.xz, seq[(i + 1) % total].p.xz);
                if ((before >= StraightEdgeLength) != (after >= StraightEdgeLength) && !NodeWithin(i, MinTransitionGap))
                {
                    seq[i] = (seq[i].p, true);
                }
            }
            int firstNode = seq.FindIndex(e => e.node);
            if (firstNode < 0)
            {
                seq[0] = (seq[0].p, true); // aucune rue ne touche le contour : une boucle seule
                firstNode = 0;
            }
            var rotated = new List<(float3 p, bool node)>(seq.Count + 1);
            for (int i = 0; i <= seq.Count; i++)
            {
                rotated.Add(seq[(firstNode + i) % seq.Count]);
            }

            // Découpe de chaque portion entre deux nœuds : coût = longueur/maxLength + virage/maxTurn.
            float maxTurn = math.radians(maxTurnDegrees);
            var nodes = new List<(float3 p, bool node)>();
            int start = 0;
            for (int i = 1; i < rotated.Count; i++)
            {
                if (!rotated[i].node)
                {
                    continue;
                }
                AppendPortion(rotated, start, i, maxLength, maxTurn, nodes);
                start = i;
            }
            // `nodes` est fermé : le dernier point est le premier (même nœud).
            int count = nodes.Count - 1;
            float3 TangentAt(int k)
            {
                float2 prev = nodes[(k + count - 1) % count].p.xz, cur = nodes[k % count].p.xz, next = nodes[(k + 1) % count].p.xz;
                // Au passage côté droit ↔ coin arrondi : la direction du côté droit (tangente exacte de l'arc).
                bool straightBefore = math.distance(prev, cur) >= StraightEdgeLength, straightAfter = math.distance(cur, next) >= StraightEdgeLength;
                if (straightBefore != straightAfter)
                {
                    float2 edge = straightBefore ? math.normalizesafe(cur - prev) : math.normalizesafe(next - cur);
                    return new float3(edge.x, 0f, edge.y);
                }
                float2 dir = math.normalizesafe(cur - prev) + math.normalizesafe(next - cur);
                dir = math.normalizesafe(dir, math.normalizesafe(next - cur));
                return new float3(dir.x, 0f, dir.y);
            }
            int from = 0;
            for (int k = 1; k <= count; k++)
            {
                if (!nodes[k].node)
                {
                    continue;
                }
                float3 a = nodes[from].p, b = nodes[k].p;
                if (math.distance(a.xz, b.xz) > 0.05f)
                {
                    bool straight = true;
                    float2 chord = math.normalizesafe(b.xz - a.xz);
                    for (int m = from + 1; m < k && straight; m++)
                    {
                        float2 d = nodes[m].p.xz - a.xz;
                        straight = math.abs(chord.x * d.y - chord.y * d.x) < 0.3f;
                    }
                    float3 ta = TangentAt(from), tb = TangentAt(k);
                    straight &= math.dot(ta.xz, chord) > 0.9995f && math.dot(tb.xz, chord) > 0.9995f;
                    result.Add(straight ? new RoadSegmentDef(a, b, isHorizontal: false) : RoadSegmentDef.Arc(a, b, ta, tb));
                }
                from = k;
            }
            return result;
        }

        /// <summary>Ajoute à `output` la portion seq[from..to] (le point `from` seulement si output est vide), avec des nœuds de coupe intermédiaires.</summary>
        private static void AppendPortion(List<(float3 p, bool node)> seq, int from, int to, float maxLength, float maxTurn,
            List<(float3 p, bool node)> output)
        {
            if (output.Count == 0)
            {
                output.Add((seq[from].p, true));
            }
            // Coût cumulé à chaque point : longueur parcourue et virage aux sommets intermédiaires.
            int m = to - from;
            var cost = new float[m + 1];
            for (int i = 1; i <= m; i++)
            {
                float2 a = seq[from + i - 1].p.xz, b = seq[from + i].p.xz;
                cost[i] = cost[i - 1] + math.distance(a, b) / maxLength;
                if (i < m)
                {
                    float2 c = seq[from + i + 1].p.xz;
                    float turn = math.acos(math.clamp(math.dot(math.normalizesafe(b - a), math.normalizesafe(c - b)), -1f, 1f));
                    cost[i] += turn / maxTurn;
                }
            }
            // Pas de tronçon plus court que MinPieceLength : un petit coin arrondi reste d'un seul tenant.
            float portionLength = 0f;
            for (int i = 1; i <= m; i++) portionLength += math.distance(seq[from + i - 1].p.xz, seq[from + i].p.xz);
            // ... sauf si le virage l'exige : au-delà de MaxPieceTurnDegrees d'un seul tenant, la courbe du
            // jeu (NetUtils.FitCurve) s'écarte de plusieurs mètres de l'arc.
            float totalTurn = 0f;
            for (int i = 1; i < m; i++)
            {
                float2 a = seq[from + i - 1].p.xz, b = seq[from + i].p.xz, c = seq[from + i + 1].p.xz;
                totalTurn += math.acos(math.clamp(math.dot(math.normalizesafe(b - a), math.normalizesafe(c - b)), -1f, 1f));
            }
            int pieces = math.max(1, math.min((int)math.ceil(cost[m] - 1e-3f), (int)math.floor(portionLength / MinPieceLength)));
            pieces = math.max(pieces, (int)math.ceil(totalTurn / maxTurn - 1e-3f));
            int next = 1;
            for (int i = 1; i <= m; i++)
            {
                float lengthStart = cost[i - 1];
                float lengthEnd = cost[i - 1] + math.distance(seq[from + i - 1].p.xz, seq[from + i].p.xz) / maxLength;
                bool cutHere = false;
                // Coupes tombant sur le segment (i-1, i) : point interpolé ; sur le virage au sommet i : le sommet.
                while (next < pieces && next * cost[m] / pieces < lengthEnd)
                {
                    float target = next * cost[m] / pieces;
                    float t = lengthEnd > lengthStart ? math.saturate((target - lengthStart) / (lengthEnd - lengthStart)) : 1f;
                    float3 p = math.lerp(seq[from + i - 1].p, seq[from + i].p, t);
                    if (math.distance(p.xz, seq[from + i].p.xz) <= 1f)
                    {
                        cutHere = i < m; // coupe collée au sommet : le sommet lui-même devient le nœud
                    }
                    else if (math.distance(p.xz, output[output.Count - 1].p.xz) > 1f)
                    {
                        output.Add((p, true));
                    }
                    else
                    {
                        // Coupe collée au point précédent : c'est lui qui devient le nœud.
                        output[output.Count - 1] = (output[output.Count - 1].p, true);
                    }
                    next++;
                }
                cutHere |= i < m && next < pieces && next * cost[m] / pieces <= cost[i];
                if (cutHere)
                {
                    next++;
                    while (next < pieces && next * cost[m] / pieces <= cost[i])
                    {
                        next++;
                    }
                }
                output.Add((seq[from + i].p, i == m || cutHere));
            }
        }
    }
}
