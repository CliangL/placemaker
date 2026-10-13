// Placemaker roads layer — grid and loop/superblock patterns, shared clipping and node merging.
// Adapted from Grid Road Generator (Ti4goc), Core/GridGenerator.cs; part of Placemaker (GPL-3.0).
// Changes for Placemaker: no static caches or global lock (thread-safe, reentrant), cooperative
// cancellation through GridParameters.Cancel, per-call diagnostics objects instead of statics.
// The "merge with the nearest accepted node" rule follows the "super node" idea of CS2-NetworkTools
// (Luca Rager, MIT).
using System;
using System.Collections.Generic;
using System.Threading;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Roads.Generators
{
    /// <summary>
    /// Représente un segment de route à créer : un point de départ et un point d'arrivée.
    /// Les coordonnées sont en espace monde (X, Z ; Y = hauteur du terrain, gérée ailleurs).
    /// </summary>
    public struct RoadSegmentDef
    {
        public float3 Start;
        public float3 End;
        public bool IsHorizontal; // "horizontal" = parallèle à l'axe principal de la grille
        /// <summary>
        /// Vrai si End est le bout libre d'une impasse (CulDeSacMode) : l'appelant ECS
        /// y pose un objet de cercle de retournement une fois le segment créé.
        /// </summary>
        public bool IsCulDeSacEnd;
        /// <summary>
        /// Vrai pour un tronçon de la colonne/rangée choisie comme avenue (GridParameters.
        /// AvenueColumnIndex/AvenueRowIndex) — traversant comme une collectrice normale (jamais
        /// de cul-de-sac sur une avenue), mais avec un troisième prefab dédié.
        /// </summary>
        public bool IsAvenue;
        /// <summary>
        /// Vrai pour un tronçon de l'intérieur pédonal d'un super-quarteirão (GridParameters.
        /// SuperblockMode, voir GenerateLoopGrid/EmitSimpleLoopBlock) : réutilise le troisième
        /// emplacement de prefab ("secundária", inutilisé par ailleurs en mode Loop — l'Arterial
        /// qui l'occupait a été retiré) pour un réseau piéton dédié, plutôt que le laço/beco
        /// carrossable habituel. Jamais vrai en même temps que IsAvenue.
        /// </summary>
        public bool IsPedestrian;
        /// <summary>
        /// Vrai pour un tronçon COURBE (mode Loop, voir GridGenerator.GenerateLoopGrid) : une
        /// vraie courbe (NetUtils.FitCurve côté ECS, tangentes StartTangent/EndTangent) plutôt
        /// qu'une ligne droite. Jamais vrai pour un tronçon de collectrice ou un rayon de
        /// cul-de-sac (toujours droits). StartTangent/EndTangent n'ont de sens que si IsArc
        /// est vrai.
        /// </summary>
        public bool IsArc;
        /// <summary>
        /// Vrai pour un tronçon d'une rue en impasse qui n'en est pas le dernier (motif Orgânico) :
        /// même réseau cul-de-sac que le bout (IsCulDeSacEnd), sans cercle de retournement — toute
        /// l'impasse garde ainsi un seul réseau.
        /// </summary>
        public bool IsLocal;
        /// <summary>
        /// Bout d'impasse (IsCulDeSacEnd, cercle de retournement) qui garde pourtant le réseau de sa
        /// rue au lieu de passer sur le réseau cul-de-sac (motif Relevo : une rue de niveau qui
        /// finit en impasse reste une rue de niveau).
        /// </summary>
        public bool KeepNetwork;
        /// <summary>
        /// Tronçon de l'anneau de la rotonde centrale du motif Radial : réseau propre, choisi dans
        /// l'onglet Rotunda (voir GridRoadToolSystem.GetRoundaboutRoadPrefab). Toujours émis dans
        /// le sens trigonométrique (circulation à droite) ; retourné côté ECS en circulation à gauche.
        /// </summary>
        public bool IsRoundabout;
        public float3 StartTangent;
        public float3 EndTangent;

        public RoadSegmentDef(float3 start, float3 end, bool isHorizontal, bool isCulDeSacEnd = false, bool isAvenue = false)
        {
            Start = start;
            End = end;
            IsHorizontal = isHorizontal;
            IsCulDeSacEnd = isCulDeSacEnd;
            IsAvenue = isAvenue;
            IsPedestrian = false;
            IsArc = false;
            IsLocal = false;
            KeepNetwork = false;
            IsRoundabout = false;
            StartTangent = default;
            EndTangent = default;
        }

        /// <summary>Facette courbe (voir IsArc) : tangentes unitaires, toutes deux orientées dans le sens de parcours.</summary>
        public static RoadSegmentDef Arc(float3 start, float3 end, float3 startTangent, float3 endTangent, bool isCulDeSacEnd = false)
        {
            var def = new RoadSegmentDef(start, end, isHorizontal: false, isCulDeSacEnd: isCulDeSacEnd);
            def.IsArc = true;
            def.StartTangent = startTangent;
            def.EndTangent = endTangent;
            return def;
        }
    }

    /// <summary>
    /// Rotonde à l'intersection de deux avenues (colonne ET rangée activées, voir
    /// ComputeAvenueRoundabout) : Center/Radius servent uniquement à choisir et positionner
    /// l'asset décoratif complet côté ECS (GridRoadToolSystem.TryResolveRoundaboutIslandPrefab)
    /// — aucun segment de route n'est généré pour la boucle elle-même, les bras d'avenue
    /// traversent normalement (croisement en +), comme un cul-de-sac garde son croisement
    /// simple sous le cercle de retournement décoratif.
    /// </summary>
    public struct RoundaboutInfo
    {
        public bool HasRoundabout;
        public float3 Center;
        public float Radius;
    }

    public enum SpacingMode
    {
        /// <summary>Répartit rows/columns de façon égale pour remplir toute l'emprise.</summary>
        FitToArea,
        /// <summary>Espacement fixe (en mètres) ; le nombre de routes est calculé automatiquement.</summary>
        FixedSpacing
    }

    /// <summary>Quel(s) axe(s) de la grille peuvent devenir des impasses en mode CulDeSacMode.</summary>
    public enum CulDeSacAxis
    {
        /// <summary>Seules les colonnes (axisIsU), perpendiculaires à l'axe principal — comportement historique.</summary>
        Columns = 0,
        /// <summary>Seules les rangées (axisIsV), parallèles à l'axe principal.</summary>
        Rows = 1,
        /// <summary>Les deux axes, chacun selon son propre motif/tirage indépendant.</summary>
        Both = 2
    }

    public struct GridParameters
    {
        public SpacingMode Mode;
        public int Rows;
        public int Columns;
        public float SpacingMeters;
        /// <summary>Rotation additionnelle (degrés, -90..+90) de la grille par rapport à l'arête la plus longue du polygone.</summary>
        public float AngleOffsetDegrees;
        /// <summary>
        /// Grille alignée sur le relief (Grelha et Loop) : un des axes suit les courbes de niveau
        /// dominantes de la zone (HeightAt requis), AngleOffsetDegrees s'ajoute par-dessus.
        /// </summary>
        public bool AlignToTerrain;
        /// <summary>Liaisons piétonnes du bout des impasses vers la rue voisine (voir AddPedestrianLinks).</summary>
        public bool PedestrianLinks;
        /// <summary>Motif "Misto" : Radial dans un cercle central, Orgânico autour (voir GenerateMixed).</summary>
        public bool MixedMode;
        /// <summary>Rayon (m) du cercle central du motif Misto.</summary>
        public float MixedCoreRadius;

        /// <summary>
        /// Mode quartier pavillonnaire : certaines lignes deviennent des impasses au lieu
        /// de collectrices traversantes, selon CulDeSacAxis.
        /// </summary>
        public bool CulDeSacMode;
        /// <summary>Quel(s) axe(s) peuvent devenir des impasses (voir CulDeSacMode).</summary>
        public CulDeSacAxis CulDeSacAxis;
        /// <summary>Profondeur de l'impasse (0.5–0.9), fraction de la longueur réelle (clippée) du tronçon entre deux collectrices.</summary>
        public float CulDeSacDepth;
        /// <summary>Alterne l'origine des impasses entre la collectrice du haut et celle du bas d'un tronçon à l'autre.</summary>
        public bool Staggered;
        /// <summary>Fréquence des impasses (0–100 %) : motif déterministe "une sur N".</summary>
        public float CulDeSacRatio;

        /// <summary>
        /// Avenue (grille classique uniquement, voir GenerateGrid/EmitLine) : la colonne
        /// d'index AvenueColumnIndex (parmi les lignes u effectivement générées, 0-based)
        /// utilise un troisième prefab dédié au lieu du réseau principal, et traverse tout le
        /// périmètre comme une collectrice normale (jamais de cul-de-sac sur une avenue). Index
        /// hors plage = silencieusement sans effet (aucune ligne ne correspond), jamais d'erreur.
        /// </summary>
        public bool AvenueColumnEnabled;
        public int AvenueColumnIndex;
        /// <summary>Même principe qu'AvenueColumnEnabled/AvenueColumnIndex, pour une rangée (ligne v).</summary>
        public bool AvenueRowEnabled;
        public int AvenueRowIndex;

        /// <summary>
        /// Mode "Loop" (voir GenerateLoopGrid) : espacement (m) des collectrices ÉPARSES qui
        /// délimitent les super-îlots — indépendant de SpacingMeters/Rows/Columns (réservés à
        /// GenerateGrid). Généralement bien plus grand qu'un espacement de grille classique :
        /// chaque super-îlot reçoit ensuite un laço interne (voir LoopCulDeSacRatio).
        /// </summary>
        public float CollectorSpacingMeters;
        /// <summary>Fréquence (0–100 %) à laquelle un laço reçoit une ramification cul-de-sac vers son centre (voir GenerateLoopGrid).</summary>
        public float LoopCulDeSacRatio;
        /// <summary>
        /// Mode "super-quarteirão" (mode Loop uniquement, inspiré des superilles de Barcelone) :
        /// la sélection est découpée en zones d'environ SuperblockZoneMeters, séparées par des
        /// rues PIÉTONNES (RoadSegmentDef.IsPedestrian) — une ligne sur SuperblockSubdivisions est
        /// une collectrice, délimitant des super-quarteirões de 3×3 zones. Jamais de cul-de-sac.
        /// Voir GenerateLoopGrid.
        /// </summary>
        public bool SuperblockMode;
        /// <summary>
        /// Taille visée (m) d'une zone constructible en mode super-quarteirão, ajustée pour que
        /// les zones remplissent exactement la sélection (retour utilisateur : "alterar a escala
        /// dos quadrados, mais pequenos com um mínimo tipo 100 m").
        /// </summary>
        public float SuperblockZoneMeters;
        /// <summary>
        /// Mode "Concêntrico" (mode Loop uniquement, exclusif avec SuperblockMode) : anneaux qui
        /// reprennent la forme du périmètre, voir ConcentricGenerator.
        /// </summary>
        public bool ConcentricMode;
        /// <summary>Nombre d'anneaux intérieurs demandés (ConcentricMode) — moins si la forme est trop étroite.</summary>
        public int ConcentricLayers;
        /// <summary>Nombre de rayons amorcés sur chaque anneau le plus intérieur (ConcentricMode).</summary>
        public int ConcentricConnections;
        /// <summary>
        /// Motif "Radial" (avec ConcentricMode) : une rotonde au centre et RadialAvenues avenues
        /// droites jusqu'au périmètre, sans anneaux (voir ConcentricGenerator.GenerateRadial).
        /// </summary>
        public bool RadialMode;
        /// <summary>Nombre d'avenues droites du motif Radial.</summary>
        public int RadialAvenues;
        /// <summary>Rayon demandé (m) de la rotonde centrale du motif Radial (ajusté à la forme).</summary>
        public float RadialRoundaboutRadius;
        /// <summary>Nombre d'anneaux circulaires autour de la rotonde du motif Radial (0 = aucun).</summary>
        public int RadialLayers;
        /// <summary>
        /// Motif "Cul-de-sac em árvore" (famille de la Grelha, LoopMode faux) : collectrice le long du
        /// grand axe, branches perpendiculaires, impasses par paires — voir GridGenerator.GenerateTree.
        /// </summary>
        public bool TreeMode;
        /// <summary>Distance (m) entre deux branches d'un même côté de la collectrice.</summary>
        public float TreeBranchSpacing;
        /// <summary>Distance (m) entre deux paires d'impasses le long d'une branche.</summary>
        public float TreeCulDeSacSpacing;
        /// <summary>Longueur (m) visée des impasses (réduite pour tenir entre deux branches et loin du périmètre).</summary>
        public float TreeCulDeSacLength;
        /// <summary>
        /// Motif "Orgânico" (famille de la Grelha, LoopMode faux) : lotissement à rues sinueuses et
        /// impasses — voir GridGenerator.GenerateOrganic.
        /// </summary>
        public bool OrganicMode;
        /// <summary>Distance visée (m) entre deux rues voisines (des lots des deux côtés).</summary>
        public float OrganicStreetSpacing;
        /// <summary>Courbure des rues (0–100 %).</summary>
        public float OrganicCurviness;
        /// <summary>Part (0–100 %) des branches qui, arrivées près d'une autre rue, la rejoignent en boucle au lieu de finir en impasse.</summary>
        public float OrganicLoopShare;
        /// <summary>Variante du tirage aléatoire (même valeur : même résultat).</summary>
        public int OrganicSeed;
        /// <summary>
        /// Motif "Relevo" (famille de la Grelha, LoopMode faux) : rues le long des courbes de niveau
        /// du terrain, reliées par des montées à pente limitée — voir GridGenerator.GenerateContour.
        /// </summary>
        public bool ContourMode;
        /// <summary>Distance (m) visée entre deux rues de niveau voisines (en plan).</summary>
        public float ContourSpacing;
        /// <summary>Distance (m) visée entre deux montées le long d'une rue de niveau.</summary>
        public float ContourConnectorSpacing;
        /// <summary>
        /// Hauteur du terrain au point (x, z) monde. Fournie par l'appelant (GridRoadToolSystem : le
        /// vrai terrain ; tests : relief synthétique) — jamais sauvegardée dans les réglages. Sans elle,
        /// le motif Relevo ne génère rien.
        /// </summary>
        public Func<float2, float> HeightAt;
        /// <summary>Cancellation of a running generation (never saved). Checked between phases and in long loops.</summary>
        public CancellationToken Cancel;
        public static GridParameters Default => new GridParameters
        {
            Mode = SpacingMode.FitToArea,
            Rows = 3,
            Columns = 3,
            SpacingMeters = 100f,
            AngleOffsetDegrees = 0f,
            CulDeSacMode = false,
            CulDeSacAxis = CulDeSacAxis.Columns,
            CulDeSacDepth = 0.6f,
            Staggered = true,
            CulDeSacRatio = 100f,
            AvenueColumnEnabled = false,
            AvenueColumnIndex = 0,
            AvenueRowEnabled = false,
            AvenueRowIndex = 0,
            CollectorSpacingMeters = 150f,
            LoopCulDeSacRatio = 50f,
        };
    }

    /// <summary>
    /// Génère une grille de routes à l'intérieur d'un périmètre QUELCONQUE (polygone
    /// convexe, concave, tourné, irrégulier) défini par les nœuds sélectionnés dans
    /// leur ordre de clic.
    ///
    /// Algorithme :
    ///  1. Le polygone est fermé à partir des points dans l'ordre de sélection.
    ///  2. Un repère local (u, v) est construit, orienté sur l'arête la plus longue
    ///     du polygone : la grille suit ainsi la "rue principale" du périmètre,
    ///     pas les axes du monde. AngleOffsetDegrees ajoute une rotation
    ///     supplémentaire à cette orientation (u et v tournent ensemble).
    ///  3. Des lignes de grille u = const et v = const sont générées dans la
    ///     bounding box locale, selon le mode d'espacement.
    ///  4. Chaque ligne est découpée aux frontières du polygone par la règle
    ///     pair-impair (even-odd) : les intersections avec les arêtes sont triées
    ///     le long de la ligne et appariées deux à deux. Un polygone concave
    ///     produit donc naturellement plusieurs sous-segments par ligne.
    ///  5. Les croisements internes entre lignes u et lignes v sont pré-calculés,
    ///     et chaque ligne est émise en sous-segments entre croisements consécutifs.
    ///     Le point monde d'un croisement est calculé UNE seule fois et partagé
    ///     (mêmes floats) par les extrémités des sous-segments des deux lignes,
    ///     pour que le jeu fusionne ces extrémités en un seul nœud d'intersection.
    ///  6. Les segments trop courts pour être une route sont éliminés.
    ///
    /// Cas particulier : avec exactement 2 nœuds, ils sont traités comme les coins
    /// opposés d'un rectangle aligné sur les axes (comportement simple et prévisible).
    /// </summary>
    public static partial class GridGenerator
    {
        /// <summary>Longueur minimale d'un segment généré, en mètres (en dessous, pas une route viable).</summary>
        public const float MinSegmentLength = 8f;

        /// <summary>
        /// Distance minimale (m) entre deux nœuds générés distincts (croisements entre
        /// eux). En dessous, le croisement le plus tardif (ordre de balayage colonnes puis
        /// rangées) est FUSIONNÉ avec le croisement déjà accepté le plus proche (comme le
        /// "Super nó" de NetworkTools — voir BuildSubSegments) : les deux lignes qui s'y
        /// croisent rejoignent ce nœud existant au lieu de créer une intersection quasi-
        /// coïncidente séparée. Ce n'est plus une perte de nœud, seulement des lignes de
        /// grille reconnectées à un nœud voisin déjà là. Les culs-de-sac trop proches de leur
        /// collectrice cible restent purement omis (EmitLine, motif distinct — pas de nœud
        /// existant à fusionner à cet endroit).
        /// </summary>
        public const float MinNodeDistance = 8f;

        private const float Epsilon = 1e-4f;

        /// <summary>
        /// Tolérance (m) pour rattacher un croisement à un intervalle clippé : un croisement
        /// à moins de cette distance d'une extrémité d'intervalle est fusionné avec elle
        /// (jonction en T au bord du polygone) au lieu de créer un micro-segment.
        /// </summary>
        private const float JoinTolerance = 0.05f;

        /// <summary>
        /// Index spatial en grille de baldes (côté = MinNodeDistance) pour retrouver le point
        /// déjà accepté le plus proche d'une position, SANS balayer toute la liste — retour
        /// utilisateur (forum du mod) : avec un petit espacement sur une grande zone, le nombre
        /// de croisements explose, et BuildSubSegments balayait alors TOUS les points déjà
        /// acceptés à CHAQUE nouveau croisement (dans une double boucle déjà O(lignes u × lignes
        /// v)) — un vrai O(n²) dans une fonction déjà O(n²), donc O(n⁴) en pratique, rejoué à
        /// CHAQUE FRAME pendant l'aperçu (voir GridRoadToolSystem.OnUpdate, "chaque frame, comme
        /// le NetTool"). Résultat vécu : jeu qui se fige avec un espacement faible sur un grand
        /// périmètre. Seules les baldes voisines (3×3) sont regardées : avec une taille de balde
        /// = MinNodeDistance, aucun point à moins de MinNodeDistance ne peut se trouver plus
        /// loin qu'une balde voisine directe.
        /// </summary>
        private sealed class SpatialPointIndex
        {
            private readonly float _cellSize;
            private readonly Dictionary<(int, int), List<float3>> _buckets = new Dictionary<(int, int), List<float3>>();

            public SpatialPointIndex(float cellSize)
            {
                _cellSize = math.max(cellSize, 0.01f);
            }

            private (int, int) CellOf(float2 xz) => ((int)math.floor(xz.x / _cellSize), (int)math.floor(xz.y / _cellSize));

            public void Add(float3 point)
            {
                var cell = CellOf(point.xz);
                if (!_buckets.TryGetValue(cell, out List<float3> list))
                {
                    list = new List<float3>();
                    _buckets[cell] = list;
                }
                list.Add(point);
            }

            /// <summary>Point accepté le plus proche à moins de maxDistance, s'il y en a un.</summary>
            public bool TryFindNearest(float3 point, float maxDistance, out float3 nearest)
            {
                (int cx, int cy) = CellOf(point.xz);
                float bestDist = float.MaxValue;
                nearest = default;
                bool found = false;
                for (int dx = -1; dx <= 1; dx++)
                {
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        if (!_buckets.TryGetValue((cx + dx, cy + dy), out List<float3> list))
                            continue;
                        foreach (float3 candidate in list)
                        {
                            float d = math.distance(candidate.xz, point.xz);
                            if (d < maxDistance && d < bestDist)
                            {
                                bestDist = d;
                                nearest = candidate;
                                found = true;
                            }
                        }
                    }
                }
                return found;
            }
        }

        /// <summary>Ligne de grille clippée : sa position sur l'axe transverse et ses intervalles intérieurs.</summary>
        private struct GridLine
        {
            public float Position;
            /// <summary>Intervalles [x, y] le long de la ligne, triés, intérieurs au polygone.</summary>
            public List<float2> Intervals;
        }

        /// <summary>Surcharge pratique quand ni le diagnostic d'omission ni la rotonde ne sont nécessaires (ex. tests existants).</summary>
        public static List<RoadSegmentDef> GenerateGrid(IReadOnlyList<float3> selectedNodePositions, GridParameters parameters)
            => GenerateGrid(selectedNodePositions, parameters, out _, out _);

        /// <summary>Surcharge pratique quand seul le diagnostic d'omission est nécessaire (ex. tests existants).</summary>
        public static List<RoadSegmentDef> GenerateGrid(IReadOnlyList<float3> selectedNodePositions,
            GridParameters parameters, out int omittedNodeCount)
            => GenerateGrid(selectedNodePositions, parameters, out omittedNodeCount, out _);

        /// <summary>
        /// Génère la grille. omittedNodeCount compte, agrégés : les croisements FUSIONNÉS avec
        /// un nœud généré déjà accepté trop proche (MinNodeDistance — voir BuildSubSegments,
        /// le nœud lui-même est conservé, seulement fusionné) et les stubs de cul-de-sac
        /// purement omis faute de nœud existant à fusionner (EmitLine) — 0 si aucun des deux.
        /// roundabout : voir RoundaboutInfo/ComputeAvenueRoundabout (HasRoundabout=false si les
        /// deux axes avenue ne sont pas actifs simultanément, ou si leur croisement tombe hors
        /// du polygone).
        /// </summary>
        public static List<RoadSegmentDef> GenerateGrid(IReadOnlyList<float3> selectedNodePositions,
            GridParameters parameters, out int omittedNodeCount, out RoundaboutInfo roundabout)
            => GenerateGridCore(selectedNodePositions, parameters, out omittedNodeCount, out roundabout);

        private static List<RoadSegmentDef> GenerateGridCore(IReadOnlyList<float3> selectedNodePositions,
            GridParameters parameters, out int omittedNodeCount, out RoundaboutInfo roundabout)
        {
            roundabout = default;
            omittedNodeCount = 0;
            if (selectedNodePositions == null || selectedNodePositions.Count < 2)
                throw new ArgumentException("Il faut au moins 2 nœuds sélectionnés.");

            // Hauteur moyenne ; sera reprojetée sur le terrain à la pose.
            float y = 0f;
            foreach (var p in selectedNodePositions) y += p.y;
            y /= selectedNodePositions.Count;

            // Projection 2D (X, Z)
            var pts = new List<float2>(selectedNodePositions.Count);
            foreach (var p in selectedNodePositions) pts.Add(new float2(p.x, p.z));

            // 2 nœuds : rectangle aligné sur les axes, coins opposés.
            if (pts.Count == 2)
            {
                float2 mn = math.min(pts[0], pts[1]);
                float2 mx = math.max(pts[0], pts[1]);
                pts = new List<float2> { mn, new float2(mx.x, mn.y), mx, new float2(mn.x, mx.y) };
            }

            var polygon = RemoveConsecutiveDuplicates(pts);
            if (polygon.Count < 3 || math.abs(SignedArea(polygon)) < 1f)
                return new List<RoadSegmentDef>(); // polygone dégénéré (points alignés/confondus)

            if (parameters.MixedMode)
            {
                return WithPedestrianLinks(GenerateMixed(polygon, y, parameters), polygon, y, parameters);
            }
            if (parameters.TreeMode)
            {
                return WithPedestrianLinks(GenerateTree(polygon, y, parameters), polygon, y, parameters);
            }
            if (parameters.OrganicMode)
            {
                return WithPedestrianLinks(GenerateOrganic(polygon, y, parameters), polygon, y, parameters);
            }
            if (parameters.ContourMode)
            {
                return GenerateContour(polygon, y, parameters);
            }

            // Repère local orienté sur l'arête la plus longue, plus l'angle réglable.
            (float2 origin, float2 uDir, float2 vDir) = BuildLocalFrame(polygon, parameters.AngleOffsetDegrees,
                parameters.AlignToTerrain ? parameters.HeightAt : null);

            // Polygone en coordonnées locales + bounding box locale.
            var local = new List<float2>(polygon.Count);
            float2 lmin = new float2(float.MaxValue), lmax = new float2(float.MinValue);
            foreach (var p in polygon)
            {
                var lp = new float2(math.dot(p - origin, uDir), math.dot(p - origin, vDir));
                local.Add(lp);
                lmin = math.min(lmin, lp);
                lmax = math.max(lmax, lp);
            }

            // Positions des lignes de grille dans le repère local.
            List<float> uPositions, vPositions;
            if (parameters.Mode == SpacingMode.FitToArea)
            {
                uPositions = DistributeEvenly(lmin.x, lmax.x, parameters.Columns);
                vPositions = DistributeEvenly(lmin.y, lmax.y, parameters.Rows);
            }
            else
            {
                uPositions = DistributeFixed(lmin.x, lmax.x, parameters.SpacingMeters);
                vPositions = DistributeFixed(lmin.y, lmax.y, parameters.SpacingMeters);
            }

            // Clipping de chaque ligne au polygone (intervalles intérieurs).
            var uLines = new List<GridLine>(uPositions.Count);
            foreach (var u in uPositions)
                uLines.Add(new GridLine { Position = u, Intervals = ClipLineToPolygon(local, axisIsU: true, position: u) });
            var vLines = new List<GridLine>(vPositions.Count);
            foreach (var v in vPositions)
                vLines.Add(new GridLine { Position = v, Intervals = ClipLineToPolygon(local, axisIsU: false, position: v) });

            List<RoadSegmentDef> grid = BuildSubSegments(uLines, vLines, origin, uDir, vDir, y, parameters, out omittedNodeCount, out roundabout);
            // Rues qui abordent le périmètre presque parallèles à lui (angle aigu), ou bout très court.
            CleanPerimeterEnds(grid, selectedNodePositions, s => !s.IsCulDeSacEnd);
            return WithPedestrianLinks(grid, polygon, y, parameters);
        }

        // ------------------------------------------------------------------
        // Pré-découpage aux croisements internes
        // ------------------------------------------------------------------

        /// <summary>
        /// Calcule les croisements entre lignes u et lignes v (en coordonnées locales, la
        /// ligne u = x croise la ligne v = z au point (x, z), retenu seulement s'il est dans
        /// les intervalles clippés DES DEUX lignes), puis émet chaque ligne en sous-segments
        /// entre croisements consécutifs. Le float3 monde de chaque croisement est calculé
        /// une seule fois et réutilisé tel quel par les deux lignes : les extrémités qui se
        /// rejoignent sont bit-à-bit identiques, condition de la fusion en un seul nœud.
        ///
        /// Un nouveau croisement à moins de MinNodeDistance d'un croisement DÉJÀ accepté
        /// (nécessairement sur une paire de lignes différente : deux croisements sur la
        /// même paire seraient le même point) est FUSIONNÉ avec ce point existant le plus
        /// proche (comme le "Super nó" de NetworkTools) plutôt que purement omis — ses deux
        /// lignes (u et v) rejoignent le nœud déjà accepté, jamais fusionné en retour vers un
        /// barycentre (sa position reste fixe, les splits déjà émis pour lui restent valides
        /// tels quels). Balayage colonnes puis rangées, donc déterministe : c'est toujours le
        /// croisement le plus tardif qui cède sa position au profit du premier accepté.
        /// </summary>
        private static List<RoadSegmentDef> BuildSubSegments(List<GridLine> uLines, List<GridLine> vLines,
            float2 origin, float2 uDir, float2 vDir, float y, GridParameters parameters, out int omittedNodeCount,
            out RoundaboutInfo roundabout)
        {
            roundabout = default;
            var uSplits = new List<(float t, float3 world)>[uLines.Count];
            var vSplits = new List<(float t, float3 world)>[vLines.Count];
            for (int i = 0; i < uLines.Count; i++) uSplits[i] = new List<(float, float3)>();
            for (int i = 0; i < vLines.Count; i++) vSplits[i] = new List<(float, float3)>();

            var acceptedWorldPoints = new SpatialPointIndex(MinNodeDistance);
            // Compte les croisements de grille FUSIONNÉS avec un nœud déjà accepté (ci-dessous)
            // ET les vrais culs-de-sac omis plus loin (EmitLine, motif distinct — un stub
            // presque à distance de sa collectrice est purement supprimé, pas fusionné : rien
            // d'existant à fusionner à cet endroit précis). Un seul compteur agrégé pour les
            // deux, comme avant ce chantier — seule la première catégorie est désormais une
            // vraie fusion (nœud conservé, segments reconnectés) plutôt qu'une perte.
            omittedNodeCount = 0;

            for (int iu = 0; iu < uLines.Count; iu++)
            {
                for (int iv = 0; iv < vLines.Count; iv++)
                {
                    float u = uLines[iu].Position;
                    float v = vLines[iv].Position;
                    if (!ContainsPosition(uLines[iu].Intervals, v) || !ContainsPosition(vLines[iv].Intervals, u))
                        continue;

                    float3 world = ToWorld(origin, uDir, vDir, u, v, y);

                    // Fusionne avec le point DÉJÀ accepté le plus proche (comme le "Super nó" de
                    // NetworkTools) plutôt que d'omettre purement ce croisement — les deux lignes
                    // (u et v) de CE croisement rejoignent alors ce point existant au lieu de créer
                    // un nœud quasi-coïncident séparé. Le point déjà accepté garde sa position
                    // FIXE (jamais déplacée vers un barycentre) : les entrées de split déjà
                    // émises pour de précédents croisements le référencent telles quelles, les
                    // recalculer rétroactivement ajouterait de la complexité sans bénéfice
                    // visible (l'écart est par définition < MinNodeDistance).
                    bool merged = acceptedWorldPoints.TryFindNearest(world, MinNodeDistance, out float3 mergeTarget);

                    if (merged)
                    {
                        omittedNodeCount++;
                        uSplits[iu].Add((v, mergeTarget));
                        vSplits[iv].Add((u, mergeTarget));
                        continue; // ne devient pas lui-même un nouveau point accepté distinct
                    }

                    acceptedWorldPoints.Add(world);
                    uSplits[iu].Add((v, world));
                    vSplits[iv].Add((u, world));
                }
            }

            var segments = new List<RoadSegmentDef>();
            // Culs-de-sac : EmitLine décide, ligne par ligne, si le mode s'applique via
            // AppliesToAxis(parameters.CulDeSacAxis, axisIsU) — colonnes, rangées, ou les deux.
            // isAvenueLine : la ligne d'index i correspond-elle à l'avenue choisie sur cet axe —
            // voir EmitLine (jamais de cul-de-sac sur une avenue, prefab dédié côté ECS).
            for (int i = 0; i < uLines.Count; i++)
            {
                bool isAvenueLine = parameters.AvenueColumnEnabled && i == parameters.AvenueColumnIndex;
                EmitLine(segments, uLines[i], uSplits[i], axisIsU: true, origin, uDir, vDir, y, parameters, ref omittedNodeCount, isAvenueLine);
            }
            for (int i = 0; i < vLines.Count; i++)
            {
                bool isAvenueLine = parameters.AvenueRowEnabled && i == parameters.AvenueRowIndex;
                EmitLine(segments, vLines[i], vSplits[i], axisIsU: false, origin, uDir, vDir, y, parameters, ref omittedNodeCount, isAvenueLine);
            }

            // Rotonde à l'intersection de deux avenues (colonne ET rangée activées) — voir
            // ComputeAvenueRoundabout : les bras d'avenue traversent normalement (croisement en
            // +), un asset décoratif complet est posé par-dessus côté ECS. Sans effet si une
            // seule avenue est activée (une avenue seule traverse simplement tout le périmètre,
            // comme une collectrice normale).
            if (parameters.AvenueColumnEnabled && parameters.AvenueRowEnabled)
            {
                roundabout = ComputeAvenueRoundabout(uLines, vLines, origin, uDir, vDir, y, parameters);
            }

            return segments;
        }

        /// <summary>Vrai si t appartient à l'un des intervalles (tolérance JoinTolerance aux bords).</summary>
        private static bool ContainsPosition(List<float2> intervals, float t)
        {
            foreach (var interval in intervals)
            {
                if (t >= interval.x - JoinTolerance && t <= interval.y + JoinTolerance)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Émet les sous-segments d'une ligne : pour chaque intervalle clippé, la chaîne
        /// [extrémité, croisements internes triés, extrémité] est parcourue par paires
        /// consécutives. Un croisement à moins de JoinTolerance d'une extrémité de
        /// l'intervalle remplace cette extrémité (jonction en T : le point monde partagé
        /// du croisement est réutilisé). Les tronçons &lt; MinSegmentLength sont éliminés.
        ///
        /// Mode culs-de-sac (parameters.CulDeSacMode, sur l'axe ou les deux axes désignés par
        /// CulDeSacAxis) : chaque tronçon entre deux points consécutifs de la chaîne ("bloc")
        /// est, selon CulDeSacRatio (motif déterministe "une fois sur N"), soit laissé
        /// traversant comme d'habitude, soit remplacé par une impasse partant d'une seule
        /// extrémité du bloc (alternée haut/bas si Staggered) et s'arrêtant à CulDeSacDepth de
        /// sa longueur. Un bloc dont
        /// l'extrémité de départ choisie n'est PAS une vraie collectrice (pur point de
        /// clipping au bord du polygone, jamais une entrée de `splits`) est supprimé plutôt
        /// que de créer une impasse suspendue dans le vide.
        /// </summary>
        private static void EmitLine(List<RoadSegmentDef> segments, GridLine line,
            List<(float t, float3 world)> splits, bool axisIsU, float2 origin, float2 uDir, float2 vDir, float y,
            GridParameters parameters, ref int omittedNodeCount, bool isAvenueLine = false)
        {
            splits.Sort((a, b) => a.t.CompareTo(b.t));
            // Une avenue est toujours traversante, jamais un cul-de-sac (prefab dédié côté ECS).
            bool culDeSac = parameters.CulDeSacMode && AppliesToAxis(parameters.CulDeSacAxis, axisIsU) && !isAvenueLine;
            int blockIndex = 0;

            foreach (var interval in line.Intervals)
            {
                float tA = interval.x;
                float tB = interval.y;
                float3 worldA = InterpolateAlongLine(line, axisIsU, tA, origin, uDir, vDir, y);
                float3 worldB = InterpolateAlongLine(line, axisIsU, tB, origin, uDir, vDir, y);

                // isCollector : vrai seulement si ce point de chaîne provient d'un
                // croisement réel (splits), faux pour une pure extrémité de clipping
                // au bord du polygone (aucune route existante à cet endroit).
                var chain = new List<(float t, float3 world, bool isCollector)> { (tA, worldA, false) };
                bool lastIsCollector = false;
                foreach (var split in splits)
                {
                    if (split.t < tA - JoinTolerance || split.t > tB + JoinTolerance)
                        continue; // croisement d'un autre intervalle de la même ligne
                    if (split.t <= tA + JoinTolerance)
                        chain[0] = (tA, split.world, true);
                    else if (split.t >= tB - JoinTolerance)
                    {
                        worldB = split.world;
                        lastIsCollector = true;
                    }
                    else
                        chain.Add((split.t, split.world, true));
                }
                chain.Add((tB, worldB, lastIsCollector));

                for (int i = 0; i + 1 < chain.Count; i++)
                {
                    // Distance RÉELLE entre les points monde, pas l'écart en t : un point fusionné
                    // (voir BuildSubSegments) garde son t d'origine mais son world peut être celui
                    // d'un nœud voisin déjà accepté — les deux ne sont plus forcément proportionnels
                    // comme avant la fusion (world venait alors toujours directement de t).
                    if (math.distance(chain[i].world.xz, chain[i + 1].world.xz) < MinSegmentLength)
                        continue; // bloc trop court

                    if (!culDeSac)
                    {
                        segments.Add(new RoadSegmentDef(chain[i].world, chain[i + 1].world, isHorizontal: !axisIsU, isAvenue: isAvenueLine));
                        continue;
                    }

                    EmitCulDeSacBlock(segments, line, chain[i], chain[i + 1], blockIndex, axisIsU, parameters,
                        origin, uDir, vDir, y, ref omittedNodeCount);
                    blockIndex++;
                }
            }
        }

        /// <summary>
        /// Si la colonne ET la rangée avenue sont actives simultanément, signale une rotonde à
        /// leur croisement — même principe que le cercle de retournement d'un cul-de-sac (voir
        /// GridRoadToolSystem.TryResolveCulDeSacCapPrefab) : les bras d'avenue traversent
        /// normalement (croisement en +, aucun rognage), et un asset décoratif complet
        /// ("&lt;Taille&gt;Roundabout01", déjà une rotonde entière avec anneau pavé et
        /// marquages — pas un simple îlot central) est posé par-dessus côté ECS. Aucun segment
        /// de route en arc n'est plus généré ici : ne renvoie que centre/rayon (pour choisir la
        /// taille de l'asset), via RoundaboutInfo. Sans effet si le croisement tombe hors du
        /// polygone à cet endroit (ContainsPosition) — les index eux-mêmes sont déjà garantis
        /// valides par l'appelant (BuildSubSegments).
        /// </summary>
        private static RoundaboutInfo ComputeAvenueRoundabout(List<GridLine> uLines,
            List<GridLine> vLines, float2 origin, float2 uDir, float2 vDir, float y, GridParameters parameters)
        {
            // Index hors plage (ex. Columns/Rows réduit après avoir choisi un index avenue plus
            // grand) : silencieusement sans effet, comme AvenueColumnIndex/AvenueRowIndex ailleurs.
            if (parameters.AvenueColumnIndex < 0 || parameters.AvenueColumnIndex >= uLines.Count) return default;
            if (parameters.AvenueRowIndex < 0 || parameters.AvenueRowIndex >= vLines.Count) return default;

            GridLine uLine = uLines[parameters.AvenueColumnIndex];
            GridLine vLine = vLines[parameters.AvenueRowIndex];
            float u = uLine.Position;
            float v = vLine.Position;
            if (!ContainsPosition(uLine.Intervals, v) || !ContainsPosition(vLine.Intervals, u))
                return default;

            float3 center = ToWorld(origin, uDir, vDir, u, v, y);
            float radius = math.min(parameters.SpacingMeters * 0.25f, 25f);
            return new RoundaboutInfo { HasRoundabout = true, Center = center, Radius = radius };
        }

        /// <summary>
        /// Un seul bloc en mode culs-de-sac : décide (motif déterministe CulDeSacRatio)
        /// s'il reste traversant ou devient une impasse, choisit l'extrémité de départ
        /// (alternance Staggered), et émet l'impasse si son départ est une vraie
        /// collectrice — sinon le bloc est simplement omis. Une impasse dont le bout
        /// libre tombe à moins de MinNodeDistance de la collectrice visée (profondeur
        /// proche de 1 sur un bloc court) est elle aussi omise : ce serait un nœud
        /// quasiment confondu avec une intersection déjà existante.
        /// </summary>
        private static void EmitCulDeSacBlock(List<RoadSegmentDef> segments, GridLine line,
            (float t, float3 world, bool isCollector) a, (float t, float3 world, bool isCollector) b,
            int blockIndex, bool axisIsU, GridParameters parameters, float2 origin, float2 uDir, float2 vDir, float y,
            ref int omittedNodeCount)
        {
            if (!IsCulDeSacBlock(blockIndex, parameters.CulDeSacRatio))
            {
                // Hors motif : bloc traversant normal, comme sans le mode.
                segments.Add(new RoadSegmentDef(a.world, b.world, isHorizontal: !axisIsU));
                return;
            }

            // Alternance haut/bas : les blocs pairs partent de a, les impairs de b.
            bool startFromA = !parameters.Staggered || blockIndex % 2 == 0;
            var start = startFromA ? a : b;
            var end = startFromA ? b : a;

            if (!start.isCollector)
            {
                return; // départ hors polygone/collectrice réelle : impasse supprimée
            }

            float depth = math.clamp(parameters.CulDeSacDepth, 0.5f, 0.8f);
            float stubT = start.t + (end.t - start.t) * depth;
            float3 stubWorld = InterpolateAlongLine(line, axisIsU, stubT, origin, uDir, vDir, y);

            // Distance RÉELLE à start.world, pas l'écart en t : si start est un point fusionné
            // (voir BuildSubSegments), start.world peut être décalé de son start.t d'origine —
            // stubWorld, lui, vient directement de stubT (jamais affecté par une fusion), donc
            // les deux ne sont plus forcément proportionnels comme avant la fusion.
            if (math.distance(stubWorld.xz, start.world.xz) < MinSegmentLength)
            {
                return; // impasse trop courte pour être une route viable
            }

            if (math.distance(stubWorld.xz, end.world.xz) < MinNodeDistance)
            {
                omittedNodeCount++;
                return; // le bout de l'impasse serait quasi confondu avec la collectrice visée
            }

            segments.Add(new RoadSegmentDef(start.world, stubWorld, isHorizontal: !axisIsU, isCulDeSacEnd: true));
        }

        /// <summary>Vrai si CulDeSacAxis autorise ce mode sur une ligne de cet axisIsU.</summary>
        private static bool AppliesToAxis(CulDeSacAxis axis, bool axisIsU) => axis switch
        {
            CulDeSacAxis.Columns => axisIsU,
            CulDeSacAxis.Rows => !axisIsU,
            CulDeSacAxis.Both => true,
            _ => axisIsU
        };

        /// <summary>Motif déterministe "une fois sur N" : N = round(100/ratio), jamais aléatoire.</summary>
        private static bool IsCulDeSacBlock(int blockIndex, float ratioPercent)
        {
            if (ratioPercent <= 0f) return false;
            if (ratioPercent >= 100f) return true;
            int n = math.max(1, (int)math.round(100f / ratioPercent));
            return blockIndex % n == 0;
        }

        /// <summary>Point monde à la coordonnée t le long d'une ligne de grille (u=const si axisIsU, sinon v=const).</summary>
        private static float3 InterpolateAlongLine(GridLine line, bool axisIsU, float t,
            float2 origin, float2 uDir, float2 vDir, float y)
        {
            return axisIsU
                ? ToWorld(origin, uDir, vDir, line.Position, t, y)
                : ToWorld(origin, uDir, vDir, t, line.Position, y);
        }

        /// <summary>Convertit un point local (u, v) en point monde (X, y, Z).</summary>
        private static float3 ToWorld(float2 origin, float2 uDir, float2 vDir, float u, float v, float y)
        {
            float2 world = origin + u * uDir + v * vDir;
            return new float3(world.x, y, world.y);
        }

        // ------------------------------------------------------------------
        // Repère local
        // ------------------------------------------------------------------

        /// <summary>
        /// Construit le repère (origine, axe u, axe v) : u suit l'arête la plus
        /// longue du polygone puis subit une rotation additionnelle d'angleOffsetDegrees
        /// (sens trigonométrique), v lui reste perpendiculaire.
        /// </summary>
        private static List<RoadSegmentDef> WithPedestrianLinks(List<RoadSegmentDef> segments, List<float2> polygon, float y, GridParameters parameters)
        {
            if (parameters.PedestrianLinks && segments.Count > 0)
            {
                AddPedestrianLinks(segments, polygon, y);
            }
            return segments;
        }

        /// <summary>
        /// Direction dominante des courbes de niveau dans le polygone : axe principal du tenseur des
        /// pentes (somme de g·gᵀ, insensible au signe — sur une colline, les pentes opposées ne
        /// s'annulent pas), tourné de 90°. Faux si la zone est presque plate (pente moyenne &lt; 1 %).
        /// </summary>
        internal static bool TryContourDirection(List<float2> polygon, Func<float2, float> heightAt, out float2 direction)
        {
            direction = new float2(1f, 0f);
            float2 min = new float2(float.MaxValue), max = new float2(float.MinValue);
            foreach (float2 p in polygon)
            {
                min = math.min(min, p);
                max = math.max(max, p);
            }
            const int samples = 16;
            const float h = 8f;
            float sxx = 0f, syy = 0f, sxy = 0f, slope = 0f;
            int count = 0;
            for (int i = 0; i < samples; i++)
            {
                for (int j = 0; j < samples; j++)
                {
                    float2 p = math.lerp(min, max, (new float2(i, j) + 0.5f) / samples);
                    if (!PointInPolygon(p, polygon)) continue;
                    float2 g = new float2(heightAt(p + new float2(h, 0f)) - heightAt(p - new float2(h, 0f)),
                        heightAt(p + new float2(0f, h)) - heightAt(p - new float2(0f, h))) / (2f * h);
                    sxx += g.x * g.x;
                    syy += g.y * g.y;
                    sxy += g.x * g.y;
                    slope += math.length(g);
                    count++;
                }
            }
            if (count == 0 || slope / count < 0.01f)
            {
                return false;
            }
            float gradientAngle = 0.5f * math.atan2(2f * sxy, sxx - syy);
            direction = new float2(-math.sin(gradientAngle), math.cos(gradientAngle)); // perpendiculaire à la pente
            return true;
        }

        private static (float2 origin, float2 uDir, float2 vDir) BuildLocalFrame(List<float2> polygon, float angleOffsetDegrees,
            Func<float2, float> heightAt = null)
        {
            int bestIndex = 0;
            float bestLengthSq = -1f;

            for (int i = 0; i < polygon.Count; i++)
            {
                float2 a = polygon[i];
                float2 b = polygon[(i + 1) % polygon.Count];
                float lenSq = math.lengthsq(b - a);
                if (lenSq > bestLengthSq)
                {
                    bestLengthSq = lenSq;
                    bestIndex = i;
                }
            }

            float2 origin2 = polygon[bestIndex];
            float2 uDir = math.normalize(polygon[(bestIndex + 1) % polygon.Count] - origin2);
            if (heightAt != null && TryContourDirection(polygon, heightAt, out float2 contour))
            {
                uDir = contour;
            }

            if (angleOffsetDegrees != 0f)
            {
                float radians = math.radians(angleOffsetDegrees);
                float cosA = math.cos(radians);
                float sinA = math.sin(radians);
                uDir = new float2(uDir.x * cosA - uDir.y * sinA, uDir.x * sinA + uDir.y * cosA);
            }

            float2 vDir = new float2(-uDir.y, uDir.x); // perpendiculaire (rotation +90°)
            return (origin2, uDir, vDir);
        }

        // ------------------------------------------------------------------
        // Clipping pair-impair d'une ligne de grille dans le polygone
        // ------------------------------------------------------------------

        /// <summary>
        /// Découpe la droite (u = position) ou (v = position) aux frontières du
        /// polygone (en coordonnées locales). Retourne les intervalles INTÉRIEURS
        /// au polygone : chaque float2 est [début, fin] le long de la droite
        /// (coordonnée v si axisIsU, u sinon), triés croissants.
        /// </summary>
        private static List<float2> ClipLineToPolygon(List<float2> local, bool axisIsU, float position)
        {
            // Intersections de la droite avec chaque arête, exprimées par la
            // coordonnée le long de la droite (t = v si axisIsU, sinon t = u).
            var crossings = new List<float>();

            for (int i = 0; i < local.Count; i++)
            {
                float2 p1 = local[i];
                float2 p2 = local[(i + 1) % local.Count];

                // Coordonnée "transverse" (celle qui doit franchir 'position')
                float c1 = axisIsU ? p1.x : p1.y;
                float c2 = axisIsU ? p2.x : p2.y;
                // Coordonnée "le long de la droite"
                float t1 = axisIsU ? p1.y : p1.x;
                float t2 = axisIsU ? p2.y : p2.x;

                // Règle demi-ouverte [c1, c2) : chaque sommet n'est compté qu'une
                // fois, ce qui garantit un nombre pair d'intersections (robustesse
                // du ray-casting quand la droite passe pile sur un sommet).
                bool crosses = (c1 <= position && position < c2) || (c2 <= position && position < c1);
                if (!crosses) continue;

                float denom = c2 - c1;
                if (math.abs(denom) < Epsilon) continue; // arête parallèle à la droite

                float f = (position - c1) / denom;
                crossings.Add(t1 + f * (t2 - t1));
            }

            crossings.Sort();

            var result = new List<float2>();
            // Appariement pair-impair : [entrée, sortie], [entrée, sortie], ...
            for (int i = 0; i + 1 < crossings.Count; i += 2)
            {
                float tA = crossings[i];
                float tB = crossings[i + 1];
                if (tB - tA < MinSegmentLength) continue; // tronçon trop court

                result.Add(new float2(tA, tB));
            }

            return result;
        }

        // ------------------------------------------------------------------
        // Utilitaires
        // ------------------------------------------------------------------

        private static List<float2> RemoveConsecutiveDuplicates(List<float2> pts)
        {
            var result = new List<float2>();
            foreach (var p in pts)
            {
                if (result.Count == 0 || math.distance(result[result.Count - 1], p) > Epsilon)
                    result.Add(p);
            }
            if (result.Count > 1 && math.distance(result[0], result[result.Count - 1]) < Epsilon)
                result.RemoveAt(result.Count - 1);
            return result;
        }

        /// <summary>Aire signée (shoelace). Sert à détecter les polygones dégénérés.</summary>
        private static float SignedArea(List<float2> polygon)
        {
            float area = 0f;
            for (int i = 0; i < polygon.Count; i++)
            {
                float2 a = polygon[i];
                float2 b = polygon[(i + 1) % polygon.Count];
                area += a.x * b.y - b.x * a.y;
            }
            return area * 0.5f;
        }

        private static List<float> DistributeEvenly(float min, float max, int count)
        {
            var result = new List<float>();
            if (count <= 0) return result;
            float step = (max - min) / (count + 1);
            for (int i = 1; i <= count; i++)
                result.Add(min + step * i);
            return result;
        }

        /// <summary>
        /// Positions internes espacées d'ENVIRON `spacing`, réparties ÉGALEMENT sur tout
        /// [min, max] (délègue à DistributeEvenly avec le nombre de cellules le plus proche de
        /// `spacing`) — PAS un pas fixe posé depuis `min` avec le reliquat entier collé sur la
        /// dernière cellule (ancien comportement). Retour utilisateur en jeu : la dernière
        /// cellule d'un axe (le reliquat de span % spacing) pouvait finir nettement plus LARGE
        /// (ou plus étroite) que toutes les autres — "o de cima está mais largo que o de baixo"
        /// — invisible tant qu'un rétrécissement uniforme des cellules en bord de périmètre
        /// masquait la différence, mais bien réel dès qu'une cellule en bord de région (pas
        /// forcément coupée par le périmètre) recevait purement et simplement le reliquat entier.
        /// Répartir la même petite marge sur TOUTES les cellules d'un coup (via DistributeEvenly)
        /// donne des cellules de largeur quasi identique partout, au prix d'un espacement réel
        /// légèrement différent de `spacing` (jamais plus de spacing/2 d'écart).
        /// </summary>
        private static List<float> DistributeFixed(float min, float max, float spacing)
        {
            if (spacing <= 0.5f) return new List<float>();
            float span = max - min;
            int cellCount = (int)math.round(span / spacing);
            if (cellCount <= 1) return new List<float>();
            return DistributeEvenly(min, max, cellCount - 1);
        }

        // ------------------------------------------------------------------
        // Échantillonnage de courbe (périmètre courbe, ex. rond-point)
        // ------------------------------------------------------------------

        /// <summary>Distance cible (m) entre deux points échantillonnés le long d'une arête courbe.</summary>
        public const float CurveSampleSpacing = 5f;

        /// <summary>Garde-fou : nombre max de points insérés pour une seule arête (courbe très longue).</summary>
        public const int MaxCurveSamplesPerEdge = 24;

        /// <summary>
        /// Échantillonne des points STRICTEMENT INTÉRIEURS (t dans ]0, 1[) le long d'une
        /// courbe de Bézier cubique (a = départ, b/c = points de contrôle, d = arrivée),
        /// pour approximer une arête EXISTANTE courbe (rond-point, virage...) dans le
        /// polygone du périmètre — un polygone construit uniquement à partir des nœuds
        /// coupe tout droit à travers la courbe (corde), ce qui peut faire déborder la
        /// grille générée sur la route courbe elle-même. Les extrémités (a et d) ne sont
        /// PAS incluses : ce sont déjà les positions des nœuds sélectionnés côté appelant.
        /// Aucune dépendance à Colossal.Mathematics ici (Bezier4x3 est décomposée en 4
        /// float3 par l'appelant) : Core reste testable sans le SDK du jeu.
        /// </summary>
        /// <summary>Virage (degrés) au-delà duquel un sommet du périmètre est un aller-retour parasite, s'il touche une arête courte.</summary>
        public const float BacktrackTurnDegrees = 150f;
        /// <summary>Longueur (m) d'arête sous laquelle un virage de plus de BacktrackTurnDegrees est traité comme un aller-retour.</summary>
        public const float BacktrackMaxLength = 20f;

        /// <summary>
        /// Retire les allers-retours d'un contour fermé : sommets où le tracé repart presque en
        /// arrière (virage > BacktrackTurnDegrees) sur une arête courte. Vécu sur un vrai
        /// périmètre : au raccord de deux routes, les derniers échantillons d'une courbe
        /// dépassaient le nœud suivant de ~3 m, puis le contour revenait en arrière (virage de
        /// 179°). Un vrai coin de route n'est jamais aussi aigu sur si peu de longueur.
        /// </summary>
        public static void RemoveBacktracks(List<float2> loop)
        {
            bool removed = true;
            while (removed && loop.Count > 3)
            {
                removed = false;
                for (int i = 0; i < loop.Count && loop.Count > 3; i++)
                {
                    int count = loop.Count;
                    float2 prev = loop[(i - 1 + count) % count];
                    float2 curr = loop[i];
                    float2 next = loop[(i + 1) % count];
                    float2 dIn = math.normalizesafe(curr - prev);
                    float2 dOut = math.normalizesafe(next - curr);
                    bool shortEdge = math.distance(prev, curr) < BacktrackMaxLength || math.distance(curr, next) < BacktrackMaxLength;
                    if (shortEdge && math.dot(dIn, dOut) < math.cos(math.radians(BacktrackTurnDegrees)))
                    {
                        loop.RemoveAt(i);
                        removed = true;
                        i--;
                    }
                }
            }
        }

        /// <summary>Même chose que RemoveBacktracks(List&lt;float2&gt;), sur des positions 3D (plan xz).</summary>
        public static void RemoveBacktracks(List<float3> loop)
        {
            var flat = new List<float2>(loop.Count);
            foreach (float3 p in loop)
            {
                flat.Add(p.xz);
            }
            RemoveBacktracks(flat);
            if (flat.Count == loop.Count)
            {
                return;
            }
            // Garde les sommets restants dans l'ordre (avec leur hauteur d'origine).
            var kept = new List<float3>(flat.Count);
            int j = 0;
            foreach (float3 p in loop)
            {
                if (j < flat.Count && math.all(p.xz == flat[j]))
                {
                    kept.Add(p);
                    j++;
                }
            }
            loop.Clear();
            loop.AddRange(kept);
        }

        public static List<float3> SampleCurve(float3 a, float3 b, float3 c, float3 d)
        {
            var result = new List<float3>();
            float length = CubicBezierLength(a, b, c, d);
            int samples = math.clamp((int)math.ceil(length / CurveSampleSpacing), 0, MaxCurveSamplesPerEdge);
            for (int i = 1; i <= samples; i++)
            {
                float t = (float)i / (samples + 1);
                result.Add(CubicBezierPosition(a, b, c, d, t));
            }
            return result;
        }

        private static float3 CubicBezierPosition(float3 a, float3 b, float3 c, float3 d, float t)
        {
            float u = 1f - t;
            return u * u * u * a + 3f * u * u * t * b + 3f * u * t * t * c + t * t * t * d;
        }

        /// <summary>Longueur approchée par ligne brisée sur 16 segments — suffisant pour choisir un nombre d'échantillons.</summary>
        private static float CubicBezierLength(float3 a, float3 b, float3 c, float3 d)
        {
            const int segments = 16;
            float length = 0f;
            float3 prev = a;
            for (int i = 1; i <= segments; i++)
            {
                float3 point = CubicBezierPosition(a, b, c, d, (float)i / segments);
                length += math.distance(prev, point);
                prev = point;
            }
            return length;
        }

        // ------------------------------------------------------------------
        // Mode Loop ("super-quarteirões") : UNE seule grille de collectrices (plus de niveau
        // Arterial séparé — voir doc de GenerateLoopGrid) délimite des quarteirões réguliers,
        // chacun rempli d'un laço UNIFORME à coins fixes (EmitSimpleLoopBlock).
        // ------------------------------------------------------------------

        /// <summary>Rayon d'arrondi des coins du laço (voir EmitSimpleLoopBlock), avant réduction pour un petit quarteirão.</summary>
        private const float LoopCornerRadiusMax = 20f;

        /// <summary>
        /// Largeur/profondeur minimale du laço en dessous de laquelle le cul-de-sac n'est PAS
        /// obligatoire, même si la fréquence choisie l'aurait normalement placé ici. Retour
        /// utilisateur en jeu : "não é obrigatório haver becos sem saída em espaços menos
        /// largos" — dans une cellule trop étroite, la ramification collée aux coins arrondis
        /// produisait une forme dégénérée/invalide plutôt qu'un simple laço sans branche. 3×
        /// LoopCornerRadiusMax : assez de place pour que les coins ET la ramification restent
        /// visuellement distincts.
        /// </summary>
        private const float MinCulDeSacCellWidth = LoopCornerRadiusMax * 3f;

        /// <summary>
        /// Fraction de la plus petite dimension du quarteirão utilisée comme marge entre le
        /// laço et la collectrice (voir EmitSimpleLoopBlock/LoopMarginMin/LoopMarginMax). Une
        /// marge FIXE (essai initial, 10 m) laissait le laço quasi coller aux collectrices dès
        /// que CollectorSpacingMeters dépassait une centaine de mètres — visuellement
        /// indissociable d'une grille classique plus dense (bug rapporté en jeu : "tudo
        /// colado"). Une fraction garantit une cour visible autour du laço, proportionnée à la
        /// taille réelle du quarteirão.
        /// </summary>
        private const float LoopMarginFraction = 0.25f;

        /// <summary>Marge minimale (m) : évite un laço qui touche presque la collectrice sur un petit quarteirão.</summary>
        private const float LoopMarginMin = 15f;

        /// <summary>Marge maximale (m) : évite un laço ridiculement petit et centré sur un très grand quarteirão.</summary>
        private const float LoopMarginMax = 45f;

        /// <summary>
        /// Échelles essayées, dans l'ordre, pour faire rentrer le rectangle du laço dans un
        /// quarteirão de bord coupé par le périmètre (voir EmitSimpleLoopBlock) — 100% d'abord
        /// (taille normale), puis rétrécissement symétrique vers le centre du quarteirão par
        /// paliers de 15 points, jusqu'à 40% (en dessous, le laço deviendrait un point ridicule
        /// plutôt qu'une vraie rue) ; le premier qui rentre entièrement dans le polygone est
        /// utilisé, ou aucun laço si même 40% déborde encore.
        /// </summary>
        private static readonly float[] LoopFitScales = { 1f, 0.85f, 0.7f, 0.55f, 0.4f };

        /// <summary>
        /// Génère le mode "Loop" : une grille UNIQUE de collectrices (mêmes primitives que
        /// GenerateGrid — BuildLocalFrame/DistributeFixed/ClipLineToPolygon/BuildSubSegments,
        /// à CollectorSpacingMeters) découpe le périmètre en quarteirões réguliers ; chaque
        /// quarteirão ENTIÈREMENT intérieur au périmètre (voir PointInPolygon, testé aux 4
        /// coins de son laço) reçoit un laço UNIFORME à coins fixes (EmitSimpleLoopBlock),
        /// relié à la collectrice par un seul embranchement.
        ///
        /// Remplace une ancienne version à 2 niveaux (Arterial épars &gt; Coletora &gt; laço), dont
        /// le laço suivait le contour réel du périmètre par échantillonnage adaptatif
        /// (FindMaxLegDepth) — cause répétée de formes dégénérées en jeu (retours utilisateur
        /// successifs : fond en biais, coins disproportionnés, brisures multiples) ET du coût
        /// dominant en performance (échantillonnage + double appel à SplitSegmentsAtMidSpan-
        /// AttachPoints par quarteirão arterial, voir le journal de performance en jeu montrant
        /// jusqu'à 350 ms/régénération). Le niveau Arterial souffrait aussi d'un bug structurel
        /// séparé : un quarteirão arterial trop étroit ne gardait aucune collectrice interne
        /// (filtre de dégagement contre l'arterial), donc AUCUN laço — "quanto mais se baixa
        /// [Arterial Spacing] mais os loops desaparecem". Un seul niveau, une géométrie fixe
        /// (rectangle à coins arrondis, jamais adaptative) : plus simple, plus robuste, et
        /// nettement moins coûteux à régénérer à chaque frame de prévisualisation.
        ///
        /// Limitation connue : un quarteirão de bord coupé par le contour réel (périmètre non
        /// rectangulaire) tente plusieurs tailles de rectangle rétrécies vers son centre avant
        /// d'abandonner (voir EmitSimpleLoopBlock/LoopFitScales) — reste donc vide seulement si
        /// même la plus petite taille testée (40% de la normale) déborde encore, ou si le centre
        /// du quarteirão lui-même tombe hors du polygone.
        /// </summary>
        public static List<RoadSegmentDef> GenerateLoopGrid(IReadOnlyList<float3> selectedNodePositions, GridParameters parameters)
            => GenerateLoopGridCore(selectedNodePositions, parameters);

        private static List<RoadSegmentDef> GenerateLoopGridCore(IReadOnlyList<float3> selectedNodePositions, GridParameters parameters)
        {
            if (parameters.ConcentricMode)
            {
                return parameters.RadialMode
                    ? ConcentricGenerator.GenerateRadial(selectedNodePositions, parameters.RadialAvenues, parameters.RadialRoundaboutRadius, parameters.RadialLayers,
                        cancel: parameters.Cancel)
                    : ConcentricGenerator.Generate(selectedNodePositions, parameters.ConcentricLayers, parameters.ConcentricConnections,
                        cancel: parameters.Cancel);
            }

            if (selectedNodePositions == null || selectedNodePositions.Count < 2 || parameters.CollectorSpacingMeters <= 0.5f)
                return new List<RoadSegmentDef>();

            // Réglages propres à la grille CLASSIQUE (avenue sur la colonne/rangée N, culs-de-sac
            // par ligne) : BuildSubSegments/EmitLine, partagés avec GenerateGrid, les appliquent
            // à TOUTE grille qu'ils construisent — y compris les collectrices et la grille fine
            // piétonne d'ici. Restés actifs depuis le mode Grelha, ils transformaient une ligne
            // intérieure en collectrice et en coupaient/supprimaient d'autres (retour utilisateur,
            // log en jeu : "só estão 6 áreas construtivas em vez de 9", "tudo misturado"). Le
            // mode Loop a ses propres réglages (LoopCulDeSacRatio, CulDeSacDepth) : neutralisés
            // ici pour tout le reste de la génération.
            parameters.AvenueColumnEnabled = false;
            parameters.AvenueRowEnabled = false;
            parameters.CulDeSacMode = false;

            float y = 0f;
            foreach (var p in selectedNodePositions) y += p.y;
            y /= selectedNodePositions.Count;

            var pts = new List<float2>(selectedNodePositions.Count);
            foreach (var p in selectedNodePositions) pts.Add(new float2(p.x, p.z));

            if (pts.Count == 2)
            {
                float2 mn = math.min(pts[0], pts[1]);
                float2 mx = math.max(pts[0], pts[1]);
                pts = new List<float2> { mn, new float2(mx.x, mn.y), mx, new float2(mn.x, mx.y) };
            }

            var polygon = RemoveConsecutiveDuplicates(pts);
            if (polygon.Count < 3 || math.abs(SignedArea(polygon)) < 1f)
                return new List<RoadSegmentDef>();

            (float2 origin, float2 uDir, float2 vDir) = BuildLocalFrame(polygon, parameters.AngleOffsetDegrees,
                parameters.AlignToTerrain ? parameters.HeightAt : null);

            var local = new List<float2>(polygon.Count);
            float2 lmin = new float2(float.MaxValue), lmax = new float2(float.MinValue);
            foreach (var p in polygon)
            {
                var lp = new float2(math.dot(p - origin, uDir), math.dot(p - origin, vDir));
                local.Add(lp);
                lmin = math.min(lmin, lp);
                lmax = math.max(lmax, lp);
            }

            // Mode super-quarteirão : la sélection est découpée en zones d'environ
            // SuperblockZoneMeters, réparties ÉGALEMENT pour la remplir exactement ("o tamanho das
            // células adapta-se ao perímetro e tamanho da área"). Une ligne sur
            // SuperblockSubdivisions est une collectrice ("coletor > pedonal > pedonal > coletor"),
            // les autres sont piétonnes (voir GenerateSuperblockInterior) ; le périmètre choisi
            // (route existante) ferme le tout. Une petite sélection (≤ 3 zones par axe) n'a donc
            // aucune collectrice interne : un seul super-quarteirão.
            List<float> uPositions;
            List<float> vPositions;
            List<float> uPedestrian = null;
            List<float> vPedestrian = null;
            if (parameters.SuperblockMode)
            {
                SplitSuperblockLines(lmin.x, lmax.x, parameters.SuperblockZoneMeters, out uPositions, out uPedestrian);
                SplitSuperblockLines(lmin.y, lmax.y, parameters.SuperblockZoneMeters, out vPositions, out vPedestrian);
            }
            else
            {
                uPositions = DistributeFixed(lmin.x, lmax.x, parameters.CollectorSpacingMeters);
                vPositions = DistributeFixed(lmin.y, lmax.y, parameters.CollectorSpacingMeters);
            }

            var uLines = new List<GridLine>(uPositions.Count);
            foreach (var u in uPositions)
                uLines.Add(new GridLine { Position = u, Intervals = ClipLineToPolygon(local, axisIsU: true, position: u) });
            var vLines = new List<GridLine>(vPositions.Count);
            foreach (var v in vPositions)
                vLines.Add(new GridLine { Position = v, Intervals = ClipLineToPolygon(local, axisIsU: false, position: v) });

            var segments = BuildSubSegments(uLines, vLines, origin, uDir, vDir, y, parameters, out _, out _);

            // Collectrices = réseau "avenue" (2ᵉ prefab, voir RoadSegmentDef.IsAvenue) — le
            // laço (émis plus bas, jamais IsAvenue) reste sur le réseau PRINCIPAL, le rayon
            // cul-de-sac (IsCulDeSacEnd) partage ce même réseau (voir GridRoadToolSystem).
            for (int i = 0; i < segments.Count; i++)
            {
                RoadSegmentDef collector = segments[i];
                collector.IsAvenue = true;
                segments[i] = collector;
            }

            // uBounds/vBounds ajoutent les extrémités du PÉRIMÈTRE aux positions de collectrices
            // internes, pour que chaque bande — bord compris — soit traitée comme un quarteirão
            // à part entière (sans ça, aucune collectrice interne du tout produit un quarteirão
            // unique = le polygone entier, ce qui reste géré correctement puisque le test des 4
            // coins du laço filtre déjà les cas où ce serait trop grand/hors polygone).
            var uBounds = new List<float> { lmin.x };
            uBounds.AddRange(uPositions);
            uBounds.Add(lmax.x);
            var vBounds = new List<float> { lmin.y };
            vBounds.AddRange(vPositions);
            vBounds.Add(lmax.y);

            int blockIndex = 0;
            for (int iu = 0; iu + 1 < uBounds.Count; iu++)
            {
                for (int iv = 0; iv + 1 < vBounds.Count; iv++)
                {
                    float uMinCell = uBounds[iu];
                    float uMaxCell = uBounds[iu + 1];
                    float vMinCell = vBounds[iv];
                    float vMaxCell = vBounds[iv + 1];

                    // Super-quarteirão (retour utilisateur en jeu, inspiré des superilles de
                    // Barcelone — capture d'écran du modèle réel) : PAS un laço unique, une VRAIE
                    // grille fine à l'intérieur de CHAQUE quarteirão délimité par les collectrices
                    // — voir GenerateSuperblockInterior. Quand uMinCell/uMaxCell/vMinCell/vMaxCell
                    // touchent le bord du POLYGONE choisi (pas une collectrice interne), la grille
                    // fine s'y accroche quand même : ce bord est presque toujours une VRAIE route
                    // existante en jeu (le joueur clique le long d'elle) — MakeCoursePos (règle 2)
                    // la raccorde automatiquement, exactement comme les collectrices le font déjà
                    // (retour utilisateur : "as estradas pedonais também têm que se conectar à
                    // estrada que serve de base, não apenas as coletoras" — laisser un anneau vide
                    // entre le vrai périmètre et la 1re collectrice gaspille tout ce terrain).
                    // Les extrémités qui NE touchent aucune route réelle (cas synthétique sans
                    // route au bord) sont filtrées après coup par PruneDeadEndPedestrianSegments.
                    if (parameters.SuperblockMode)
                    {
                        GenerateSuperblockInterior(segments, local, new float2(uMinCell, vMinCell), new float2(uMaxCell, vMaxCell),
                            WithinRange(uPedestrian, uMinCell, uMaxCell), WithinRange(vPedestrian, vMinCell, vMaxCell),
                            origin, uDir, vDir, y, parameters);
                        continue;
                    }

                    // Aucune collectrice interne du tout sur l'axe V (espacement plus grand que
                    // tout le périmètre) : ni vMin ni vMax de cette unique bande ne sont une VRAIE
                    // collectrice (vBounds ne contient alors que [lmin.y, lmax.y], les bornes du
                    // polygone, pas des routes) — jamais de laço orphelin plutôt qu'un
                    // embranchement raccroché dans le vide.
                    if (vPositions.Count == 0)
                    {
                        continue;
                    }

                    // Côté d'accès (vers vMin ou vMax) : la bande de bord initiale (iv==0, avant
                    // la 1re collectrice interne) DOIT s'accrocher à vMax (la 1re collectrice
                    // réelle) — vMin n'est ici que le bord du polygone, pas une route. Bande de
                    // bord finale (dernière collectrice -> bord) : DOIT s'accrocher à vMin, même
                    // raison. Bandes pleinement intérieures : alterné par rangée réelle — sans ça,
                    // TOUS les laços de la ville s'accrocheraient au même côté absolu de chaque
                    // collectrice (retour en jeu : "os laços deviam estar dos dois lados da
                    // estrada principal").
                    bool isFirstStrip = iv == 0;
                    bool isLastStrip = iv == vBounds.Count - 2;
                    bool connectToMin = isFirstStrip ? false : isLastStrip ? true : (iv & 1) == 0;

                    bool withCulDeSac = IsCulDeSacBlock(blockIndex, parameters.LoopCulDeSacRatio);
                    bool emitted = EmitSimpleLoopBlock(segments, uMinCell, uMaxCell, vMinCell, vMaxCell, local,
                        origin, uDir, vDir, y, withCulDeSac, parameters.CulDeSacDepth, connectToMin);
                    if (emitted)
                    {
                        blockIndex++;
                    }
                }
            }

            // L'embranchement du laço (EmitSimpleLoopBlock) se raccorde à la collectrice EN
            // RETRAIT du bord du quarteirão (jamais à une extrémité de la collectrice
            // elle-même) : BuildSubSegments, appelé AVANT cette boucle, n'a aucune connaissance
            // de ce point d'ancrage (calculé bien après, par quarteirão) — collectrice et laço
            // se touchent visuellement mais ne partagent aucun sommet réel tant que cette
            // post-passe n'a pas tourné (retour en jeu : "a estrada principal (laço) não fusiona
            // na coletora").
            segments = SplitSegmentsAtMidSpanAttachPoints(segments, isTarget: s => s.IsAvenue, isAttachSource: s => !s.IsAvenue);

            // Collectrices qui arrivent au périmètre presque parallèles à lui, ou en bout très court
            // près d'un carrefour : angles aigus et carrefours collés au périmètre (collisions en jeu).
            CleanPerimeterEnds(segments, selectedNodePositions, s => s.IsAvenue);

            // Filet de sécurité final (retour utilisateur en jeu, répété : "Ainda tem os cul de
            // sac... retira por completo") : au-delà du garde-fou uMinReal/uMaxReal/vMinReal/
            // vMaxReal déjà appliqué dans GenerateSuperblockInterior (qui couvre le cas simple —
            // périmètre rectangulaire), un périmètre IRRÉGULIER peut encore faire clipper une
            // ligne interne en plein milieu par ClipLineToPolygon (le vrai contour, pas juste la
            // boîte englobante lmin/lmax) à un endroit qui ne correspond à AUCUN autre tronçon —
            // supprime ITÉRATIVEMENT tout tronçon piéton dont une extrémité n'est partagée par
            // AUCUN autre tronçon (piéton ou collectrice), jusqu'à point fixe (une suppression
            // peut en exposer une autre plus loin dans la même chaîne). Garantit qu'AUCUNE
            // impasse ne peut survivre en mode super-quarteirão, quelle que soit la cause exacte.
            if (parameters.SuperblockMode)
            {
                segments = PruneDeadEndPedestrianSegments(segments, polygon);
            }

            return segments;
        }

        /// <summary>
        /// Supprime itérativement tout tronçon piéton (RoadSegmentDef.IsPedestrian) dont AU MOINS
        /// une extrémité n'est NI partagée par un autre tronçon NI posée sur le vrai périmètre
        /// choisi par le joueur (voir l'appel dans GenerateLoopGrid pour le pourquoi). Une
        /// extrémité "partagée" signifie qu'un AUTRE tronçon a un Start ou un End EXACTEMENT
        /// (bit-à-bit) à cette même position — cohérent avec le reste du mod (voir
        /// SplitSegmentsAtMidSpanAttachPoints). Une extrémité sur le VRAI périmètre n'est PAS une
        /// impasse : en jeu, ce périmètre est presque toujours une route existante, que
        /// MakeCoursePos (règle 2) raccorde automatiquement — la traiter comme une impasse
        /// gaspillait tout le terrain entre le périmètre et la 1re collectrice (retour
        /// utilisateur : "as estradas pedonais também têm que se conectar à estrada que serve de
        /// base"). Itératif car retirer un tronçon peut rendre orpheline l'extrémité d'un AUTRE
        /// tronçon plus loin dans la même chaîne — tourne jusqu'à ce qu'aucune suppression ne soit
        /// plus nécessaire.
        /// </summary>
        private static List<RoadSegmentDef> PruneDeadEndPedestrianSegments(List<RoadSegmentDef> segments, List<float2> polygon)
        {
            var result = new List<RoadSegmentDef>(segments);
            const float boundaryTolerance = 0.05f;
            bool removedAny;
            do
            {
                removedAny = false;
                var pointCounts = new Dictionary<(float, float, float), int>();
                void CountPoint(float3 p)
                {
                    var key = (p.x, p.y, p.z);
                    pointCounts[key] = pointCounts.TryGetValue(key, out int c) ? c + 1 : 1;
                }
                foreach (RoadSegmentDef s in result)
                {
                    CountPoint(s.Start);
                    CountPoint(s.End);
                }
                bool IsDangling(float3 p)
                {
                    if (pointCounts[(p.x, p.y, p.z)] > 1)
                    {
                        return false; // partagé par un autre tronçon : jamais une impasse
                    }
                    // Comparaison en espace MONDE (segment.Start/End sont déjà en espace monde,
                    // tout comme `polygon` ici — voir GenerateLoopGrid) : PAS de conversion vers
                    // l'espace local u/v, qui comparerait des repères différents et ferait
                    // échouer le test même pour un point réellement posé sur le périmètre (bug
                    // corrigé : gaspillait le même anneau de terrain que le défaut d'origine).
                    return !IsPointOnPolygonEdge(p.xz, polygon, boundaryTolerance);
                }
                for (int i = result.Count - 1; i >= 0; i--)
                {
                    RoadSegmentDef s = result[i];
                    if (!s.IsPedestrian)
                    {
                        continue; // ne jamais purger une collectrice : toujours traversante par nature
                    }
                    if (IsDangling(s.Start) || IsDangling(s.End))
                    {
                        result.RemoveAt(i);
                        removedAny = true;
                    }
                }
            } while (removedAny);
            return result;
        }

        /// <summary>
        /// Vrai si `point` (espace local u/v) tombe sur l'un des segments du polygone (avec
        /// tolérance), utilisé par PruneDeadEndPedestrianSegments pour distinguer une extrémité
        /// posée sur le vrai périmètre (jamais une impasse, MakeCoursePos la raccorde en jeu)
        /// d'une extrémité réellement orpheline en plein milieu du terrain.
        /// </summary>
        private static bool IsPointOnPolygonEdge(float2 point, List<float2> polygon, float tolerance)
        {
            int n = polygon.Count;
            for (int i = 0; i < n; i++)
            {
                float2 a = polygon[i];
                float2 b = polygon[(i + 1) % n];
                float2 ab = b - a;
                float lenSq = math.lengthsq(ab);
                if (lenSq < 1e-6f)
                {
                    continue;
                }
                float t = math.clamp(math.dot(point - a, ab) / lenSq, 0f, 1f);
                float2 projected = a + t * ab;
                if (math.distance(projected, point) < tolerance)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Sens unique en "tourbillon" (super-quarteirão, GridParameters.SuperblockMode) : chaque
        /// cellule (iu,iv) de la grille [uBounds,vBounds] tourne en rond sur ses 4 bords, dans le
        /// sens horaire si (iu+iv) est pair, antihoraire sinon — l'alternance garantit qu'un même
        /// tronçon, TOUJOURS partagé par exactement 2 cellules voisines (ou 1 seule en bord de
        /// zone), reçoit un sens unique et cohérent des deux côtés (jamais deux cellules voisines
        /// n'exigent des sens opposés sur leur frontière commune — propriété du damier de parité,
        /// valable pour toute paire de cellules adjacentes horizontalement OU verticalement).
        /// Réoriente chaque tronçon filtré par isTarget (échange Start/End au besoin) SANS
        /// changer sa géométrie : le sens du trafic sur une route à sens unique est déterminé par
        /// l'ordre Start→End du tronçon, pas par un indicateur séparé (le moteur n'a pas de flag
        /// "sens unique" — voir CompositionFlags, seul le PREFAB choisi peut être intrinsèquement
        /// à sens unique, ex. un "Beco"). Appelée à 2 niveaux : la grille fine intérieure d'un
        /// super-quarteirão (GenerateSuperblockInterior, isTarget=IsPedestrian, uBounds/vBounds
        /// LOCAUX à CE super-quarteirão — chaque super-quarteirão redémarre sa propre parité).
        /// Chaque tronçon couvre TOUJOURS exactement un intervalle entre 2 positions consécutives
        /// de uBounds/vBounds (BuildSubSegments scinde à chaque croisement), donc correspond à
        /// exactement UNE cellule de chaque côté — pas de cas où un même tronçon devrait
        /// satisfaire plusieurs cellules à la fois.
        /// </summary>
        private static void ApplyOneWaySwirl(List<RoadSegmentDef> segments, List<float> uBounds, List<float> vBounds,
            float2 origin, float2 uDir, float2 vDir, Func<RoadSegmentDef, bool> isTarget)
        {
            float2 ToLocal(float3 world) => new float2(math.dot(world.xz - origin, uDir), math.dot(world.xz - origin, vDir));

            for (int i = 0; i < segments.Count; i++)
            {
                RoadSegmentDef seg = segments[i];
                if (!isTarget(seg) || seg.IsArc)
                {
                    continue;
                }

                float2 a = ToLocal(seg.Start);
                float2 b = ToLocal(seg.End);
                bool desiredForward;
                if (seg.IsHorizontal)
                {
                    // Ligne à V constant (l'intervalle varie en U) : frontière basse du
                    // quarteirão (iu,iv) situé EN DESSOUS — bord "haut" de ce quarteirão, sens
                    // horaire = +u (voir la doc de la classe pour la convention des 4 bords).
                    int iv = FindExactBoundIndex(vBounds, (a.y + b.y) * 0.5f);
                    int iu = FindIntervalIndex(uBounds, (a.x + b.x) * 0.5f);
                    if (iv <= 0 || iv >= vBounds.Count - 1 || iu < 0 || iu >= uBounds.Count - 1)
                    {
                        continue; // pas une vraie ligne interne (bord du polygone) : jamais réorienté
                    }
                    bool clockwise = ((iu + iv) & 1) == 0;
                    bool wantsIncreasingU = clockwise;
                    desiredForward = wantsIncreasingU == (b.x >= a.x);
                }
                else
                {
                    // Ligne à U constant : bord "gauche" du quarteirão (iu,iv) situé À DROITE,
                    // sens horaire = -v (bord gauche, voir la doc de la classe).
                    int iu = FindExactBoundIndex(uBounds, (a.x + b.x) * 0.5f);
                    int iv = FindIntervalIndex(vBounds, (a.y + b.y) * 0.5f);
                    if (iu <= 0 || iu >= uBounds.Count - 1 || iv < 0 || iv >= vBounds.Count - 1)
                    {
                        continue;
                    }
                    bool clockwise = ((iu + iv) & 1) == 0;
                    bool wantsDecreasingV = clockwise;
                    desiredForward = wantsDecreasingV == (b.y <= a.y);
                }

                if (!desiredForward)
                {
                    (seg.Start, seg.End) = (seg.End, seg.Start);
                    segments[i] = seg;
                }
            }
        }

        /// <summary>Indice EXACT (à 0.5 m près) de value dans bounds (triée croissante), ou -1 si aucune correspondance — utilisé par ApplyOneWayCollectorDirections pour repérer la ligne de collectrice elle-même.</summary>
        private static int FindExactBoundIndex(List<float> bounds, float value)
        {
            for (int i = 0; i < bounds.Count; i++)
            {
                if (math.abs(bounds[i] - value) < 0.5f)
                {
                    return i;
                }
            }
            return -1;
        }

        /// <summary>Indice i tel que bounds[i] &lt;= value &lt;= bounds[i+1] (avec une marge de 0.5 m), ou -1 — utilisé par ApplyOneWayCollectorDirections pour repérer l'intervalle (quarteirão) couvert par le tronçon.</summary>
        private static int FindIntervalIndex(List<float> bounds, float value)
        {
            for (int i = 0; i + 1 < bounds.Count; i++)
            {
                if (value >= bounds[i] - 0.5f && value <= bounds[i + 1] + 0.5f)
                {
                    return i;
                }
            }
            return -1;
        }

        /// <summary>
        /// Nombre de subdivisions par axe à l'intérieur d'un super-quarteirão (voir
        /// GenerateSuperblockInterior) — 3×3 = 9 "zones" édificáveis, exactement le modèle réel
        /// des superilles de Barcelone (capture d'écran fournie par l'utilisateur : quatre
        /// collectrices délimitent le super-quarteirão, une grille fine de rues piétonnes le
        /// subdivise en 9 zones à construire — jamais un laço unique).
        /// </summary>
        private const int SuperblockSubdivisions = 3;

        /// <summary>Plancher (m) de GridParameters.SuperblockZoneMeters — sous ce seuil, les zones deviennent trop petites pour être construites.</summary>
        public const float SuperblockZoneMetersMin = 100f;

        /// <summary>
        /// Découpe [min,max] en zones d'environ zoneMeters réparties également, puis sépare les
        /// lignes internes en collectrices (une sur SuperblockSubdivisions, voir
        /// GridParameters.SuperblockMode) et en rues piétonnes.
        /// </summary>
        private static void SplitSuperblockLines(float min, float max, float zoneMeters, out List<float> collectors, out List<float> pedestrian)
        {
            collectors = new List<float>();
            pedestrian = new List<float>();
            float zone = math.max(zoneMeters, SuperblockZoneMetersMin);
            int zoneCount = math.max(1, (int)math.round((max - min) / zone));
            List<float> lines = DistributeEvenly(min, max, zoneCount - 1);
            for (int i = 0; i < lines.Count; i++)
            {
                // Index de ligne 1-based : 3, 6, 9... = collectrices.
                if ((i + 1) % SuperblockSubdivisions == 0)
                {
                    collectors.Add(lines[i]);
                }
                else
                {
                    pedestrian.Add(lines[i]);
                }
            }
        }

        /// <summary>Positions de `positions` strictement comprises dans ]min, max[.</summary>
        private static List<float> WithinRange(List<float> positions, float min, float max)
        {
            var result = new List<float>();
            foreach (float p in positions)
            {
                if (p > min + JoinTolerance && p < max - JoinTolerance)
                {
                    result.Add(p);
                }
            }
            return result;
        }

        /// <summary>
        /// Remplit l'intérieur d'UN super-quarteirão [regionMin,regionMax] (délimité par les
        /// collectrices, voir GenerateLoopGrid/GridParameters.SuperblockMode) d'une VRAIE grille
        /// fine — SuperblockSubdivisions-1 lignes internes par axe, mêmes primitives que la
        /// grille de collectrices elle-même (DistributeFixed-like/ClipLineToPolygon/
        /// BuildSubSegments) — au lieu du laço unique du mode Loop classique. Chaque tronçon émis
        /// est marqué IsPedestrian (voir RoadSegmentDef) ; jamais de cul-de-sac (retour
        /// utilisateur : "Não há cul de sac" — la grille fine est par nature entièrement
        /// connectée, aucune impasse). Sens unique en tourbillon appliqué localement à CETTE
        /// grille (voir ApplyOneWaySwirl) — chaque super-quarteirão redémarre sa propre parité,
        /// indépendamment de ses voisins.
        ///
        /// Les lignes internes sont clippées contre le VRAI [regionMin,regionMax] du
        /// super-quarteirão, qu'un côté donné soit une collectrice interne ou le bord du
        /// polygone choisi par le joueur : ce bord est presque toujours une VRAIE route
        /// existante en jeu, que MakeCoursePos (règle 2) raccorde automatiquement — retenir les
        /// lignes en retrait de ce bord (comportement précédent) gaspillait tout le terrain entre
        /// le vrai périmètre et la 1re collectrice (retour utilisateur : "as estradas pedonais
        /// também têm que se conectar à estrada que serve de base"). Les extrémités qui NE
        /// touchent aucune route réelle (cas synthétique sans route au bord, ex. tests) sont
        /// filtrées après coup par PruneDeadEndPedestrianSegments (qui exempte spécifiquement les
        /// points sur le vrai périmètre).
        /// </summary>
        private static void GenerateSuperblockInterior(List<RoadSegmentDef> segments, List<float2> polygon, float2 regionMin, float2 regionMax,
            List<float> uPositions, List<float> vPositions,
            float2 origin, float2 uDir, float2 vDir, float y, GridParameters parameters)
        {
            if (uPositions.Count == 0 && vPositions.Count == 0)
            {
                return; // super-quarteirão d'une seule zone : rien à subdiviser
            }
            float2 center = (regionMin + regionMax) * 0.5f;
            if (!PointInPolygon(center, polygon))
            {
                return; // super-quarteirão entièrement hors zone développable
            }

            // Les lignes intérieures sont clippées contre la VRAIE bordure du quarteirão
            // (regionMin/regionMax), pas une bordure rétractée — quand cette bordure coïncide
            // avec le vrai périmètre choisi par le joueur (le cas normal en jeu : le joueur clique
            // le long d'une route existante), MakeCoursePos (règle 2) la raccorde automatiquement
            // à cette route réelle, exactement comme le fait déjà le réseau collecteur. Les
            // extrémités qui ne touchent aucune route réelle sont filtrées ensuite par
            // PruneDeadEndPedestrianSegments.
            var uLines = new List<GridLine>(uPositions.Count);
            foreach (float u in uPositions)
            {
                uLines.Add(new GridLine { Position = u, Intervals = IntersectIntervalsWithRange(ClipLineToPolygon(polygon, axisIsU: true, position: u), regionMin.y, regionMax.y) });
            }
            var vLines = new List<GridLine>(vPositions.Count);
            foreach (float v in vPositions)
            {
                vLines.Add(new GridLine { Position = v, Intervals = IntersectIntervalsWithRange(ClipLineToPolygon(polygon, axisIsU: false, position: v), regionMin.x, regionMax.x) });
            }

            var interior = BuildSubSegments(uLines, vLines, origin, uDir, vDir, y, parameters, out _, out _);
            for (int i = 0; i < interior.Count; i++)
            {
                RoadSegmentDef seg = interior[i];
                seg.IsPedestrian = true;
                interior[i] = seg;
            }

            var uBoundsLocal = new List<float> { regionMin.x };
            uBoundsLocal.AddRange(uPositions);
            uBoundsLocal.Add(regionMax.x);
            var vBoundsLocal = new List<float> { regionMin.y };
            vBoundsLocal.AddRange(vPositions);
            vBoundsLocal.Add(regionMax.y);
            ApplyOneWaySwirl(interior, uBoundsLocal, vBoundsLocal, origin, uDir, vDir, isTarget: s => s.IsPedestrian);

            segments.AddRange(interior);
        }

        /// <summary>
        /// Intersecte une liste d'intervalles (déjà clippés contre le polygone entier, voir
        /// ClipLineToPolygon) avec [rangeMin, rangeMax] — utilisé par GenerateSuperblockInterior
        /// pour confiner une ligne interne à SON super-quarteirão, en plus du polygone entier (le
        /// plus restrictif des deux l'emporte).
        /// </summary>
        private static List<float2> IntersectIntervalsWithRange(List<float2> intervals, float rangeMin, float rangeMax)
        {
            var result = new List<float2>();
            foreach (float2 interval in intervals)
            {
                float a = math.max(interval.x, rangeMin);
                float b = math.min(interval.y, rangeMax);
                if (b - a > MinSegmentLength)
                {
                    result.Add(new float2(a, b));
                }
            }
            return result;
        }

        /// <summary>
        /// Divise chaque tronçon "cible" (isTarget, toujours droit) là où un tronçon "source
        /// d'ancrage" (isAttachSource) s'y raccorde STRICTEMENT AU MILIEU de son tracé, jamais à
        /// ses propres extrémités (celles-ci sont déjà de vrais croisements, gérés par
        /// BuildSubSegments). Utilisé par GenerateLoopGrid pour raccorder collectrice/laço
        /// (cible=IsAvenue, source=tout le reste) : sans cette post-passe, la collectrice et
        /// l'embranchement du laço se touchent visuellement mais ne partagent aucun sommet réel
        /// ("X ne fusiona pas dans Y"). Un point d'ancrage est
        /// cherché en projetant chaque extrémité Start/End d'un tronçon source sur la ligne de
        /// CHAQUE tronçon cible (repère t ∈ ]0, 1[ strictement, et distance à la ligne quasi
        /// nulle — ces points sont calculés pour tomber EXACTEMENT dessus, pas juste "à
        /// proximité", donc une tolérance large signalerait une vraie coïncidence géométrique,
        /// pas un faux positif). Plusieurs sources accrochées au même endroit produisent des
        /// points quasi identiques, dédupliqués via MinSegmentLength.
        /// </summary>
        /// <summary>
        /// Taille de balde (m) pour l'index spatial des points d'ancrage ci-dessous. Retour
        /// utilisateur en jeu (log de performance) : le modo Loop prenait jusqu'à 350 ms par
        /// régénération, quasi tout dans cette fonction, appelée PLUSIEURS fois (une fois par
        /// pâté de maison + une fois globalement, voir GenerateLoopGrid) et comparant TOUS les
        /// tronçons cibles à TOUS les points d'ancrage — O(cibles × points), même défaut
        /// structurel que BuildSubSegments (voir SpatialPointIndex). Une taille généreuse (les
        /// espacements collectrice/artérielle typiques font des dizaines à centaines de mètres)
        /// garde peu de baldes à visiter par tronçon cible sans réduire les vrais candidats.
        /// </summary>
        private const float AttachPointBucketSize = 50f;

        private static List<RoadSegmentDef> SplitSegmentsAtMidSpanAttachPoints(List<RoadSegmentDef> segments,
            Func<RoadSegmentDef, bool> isTarget, Func<RoadSegmentDef, bool> isAttachSource)
        {
            var attachPoints = new List<float3>();
            foreach (RoadSegmentDef s in segments)
            {
                if (isAttachSource(s))
                {
                    attachPoints.Add(s.Start);
                    attachPoints.Add(s.End);
                }
            }
            if (attachPoints.Count == 0)
            {
                return segments;
            }

            // Index spatial par baldes : au lieu de tester TOUS les points d'ancrage contre
            // CHAQUE tronçon cible, on ne visite que les baldes recouvrant la boîte englobante
            // du tronçon (marge = tolérance de projection ci-dessous).
            var buckets = new Dictionary<(int, int), List<float3>>();
            foreach (float3 p in attachPoints)
            {
                var cell = ((int)math.floor(p.x / AttachPointBucketSize), (int)math.floor(p.z / AttachPointBucketSize));
                if (!buckets.TryGetValue(cell, out List<float3> bucket))
                {
                    bucket = new List<float3>();
                    buckets[cell] = bucket;
                }
                bucket.Add(p);
            }

            var result = new List<RoadSegmentDef>(segments.Count);
            foreach (RoadSegmentDef target in segments)
            {
                if (!isTarget(target) || target.IsArc)
                {
                    result.Add(target);
                    continue;
                }

                float2 a = target.Start.xz;
                float2 ab = target.End.xz - a;
                float lenSq = math.lengthsq(ab);
                if (lenSq < 1e-4f)
                {
                    result.Add(target);
                    continue;
                }

                float2 minXz = math.min(target.Start.xz, target.End.xz) - 0.5f;
                float2 maxXz = math.max(target.Start.xz, target.End.xz) + 0.5f;
                int cx0 = (int)math.floor(minXz.x / AttachPointBucketSize);
                int cx1 = (int)math.floor(maxXz.x / AttachPointBucketSize);
                int cz0 = (int)math.floor(minXz.y / AttachPointBucketSize);
                int cz1 = (int)math.floor(maxXz.y / AttachPointBucketSize);

                var hits = new List<(float t, float3 point)>();
                for (int cx = cx0; cx <= cx1; cx++)
                for (int cz = cz0; cz <= cz1; cz++)
                {
                    if (!buckets.TryGetValue((cx, cz), out List<float3> candidates))
                    {
                        continue;
                    }
                    foreach (float3 candidate in candidates)
                    {
                        float2 p = candidate.xz;
                        float t = math.dot(p - a, ab) / lenSq;
                        if (t <= 0f || t >= 1f)
                        {
                            continue; // à ou au-delà d'une extrémité : jamais un vrai raccord de milieu
                        }
                        float2 projected = a + t * ab;
                        if (math.distance(projected, p) > 0.5f)
                        {
                            continue; // pas sur ce tronçon cible
                        }
                        // BUG corrigé ici : recalculer le point via math.lerp(target.Start,
                        // target.End, t) reconstruit une coordonnée INDÉPENDANTE de celle de la
                        // source (jusqu'à 0.5 m d'écart, la tolérance ci-dessus — jamais garanti
                        // bit-à-bit identique, même à tolérance nulle, à cause de l'arrondi flottant
                        // sur deux chemins de calcul différents). Le pipeline ECS en aval
                        // (GridRoadToolSystem.MakeCoursePos) ne connaît QUE des points réels
                        // (nœud/arête déjà existants) ou des points "libres" tout neufs — deux
                        // segments FRAÎCHEMENT générés qui devraient partager LE MÊME nœud neuf n'ont
                        // aucun autre mécanisme pour se reconnaître comme identiques que la
                        // coïncidence EXACTE de leur position ; le moteur natif (GenerateNodesSystem)
                        // fusionne alors deux points quasi identiques mais pas bit-exacts en DEUX
                        // nœuds tout proches au lieu d'un seul — précisément "Objetos sobrepostos"
                        // sans jonction, mais entre deux tronçons qu'on a nous-mêmes générés, jamais
                        // contre une route réelle (donc invisible à tous les diagnostics précédents,
                        // qui ne testaient que le raccord aux routes déjà existantes). Fix : reprendre
                        // TEL QUEL le point de la source (candidate), jamais recalculé — garantit une
                        // coordonnée bit-à-bit identique entre les deux tronçons qui doivent se
                        // rejoindre.
                        float3 point = candidate;
                        if (math.distance(point, target.Start) < MinSegmentLength
                            || math.distance(point, target.End) < MinSegmentLength)
                        {
                            continue; // trop proche d'une extrémité vraie : pièce dégénérée
                        }
                        hits.Add((t, point));
                    }
                }

                if (hits.Count == 0)
                {
                    result.Add(target);
                    continue;
                }

                hits.Sort((x, y) => x.t.CompareTo(y.t));

                float3 pieceStart = target.Start;
                float3 lastAccepted = target.Start;
                foreach ((float _, float3 point) in hits)
                {
                    if (math.distance(point, lastAccepted) < MinSegmentLength)
                    {
                        continue; // quasi-doublon (2 sources accrochées au même endroit)
                    }
                    result.Add(new RoadSegmentDef(pieceStart, point, target.IsHorizontal, isAvenue: target.IsAvenue));
                    pieceStart = point;
                    lastAccepted = point;
                }
                result.Add(new RoadSegmentDef(pieceStart, target.End, target.IsHorizontal, isAvenue: target.IsAvenue));
            }
            return result;
        }

        /// <summary>
        /// Émet un laço UNIFORME à coins arrondis fixes pour UN quarteirão [uMin,uMax]x
        /// [vMin,vMax] — remplace l'ancien EmitLoopBlock (profondeur adaptative par
        /// échantillonnage du contour réel, voir doc de GenerateLoopGrid pour le pourquoi du
        /// remplacement). Rectangle inséré symétriquement par une marge fixe (LoopMarginFraction/
        /// Min/Max) sur les 4 côtés, coins arrondis à rayon fixe (LoopCornerRadiusMax, réduit si
        /// nécessaire) — AUCUN échantillonnage, AUCUNE dépendance à la forme réelle du polygone
        /// au-delà d'un test simple "les 4 coins du rectangle sont-ils à l'intérieur ?" (rejette
        /// tel quel les quarteirões coupés par le bord du périmètre, plutôt que de déformer le
        /// laço pour les épouser — géométrie toujours valide, jamais dégénérée).
        ///
        /// Un seul embranchement relie le laço à la collectrice, sur le côté indiqué par
        /// connectToMin (vMin ou vMax) ; le cul-de-sac, si activé, part du côté OPPOSÉ (le "fond"
        /// du laço, le plus loin de la collectrice) et remonte vers le centre.
        ///
        /// Réservée au mode Loop classique (jamais appelée quand GridParameters.SuperblockMode
        /// est vrai — voir GenerateLoopGrid/GenerateSuperblockInterior, une grille fine remplace
        /// ce laço unique dans ce cas).
        /// </summary>
        /// <summary>Côté minimal (m) d'un laço rétréci pour tenir dans la forme : plus petit, il n'est pas posé.</summary>
        private const float MinLoopSide = 70f;
        /// <summary>Angle minimal (degrés) entre une rue et le périmètre là où elle le rejoint.</summary>
        private const float MinPerimeterJoinAngle = 35f;
        /// <summary>Bout de rue plus court (m) que ceci entre un carrefour et le périmètre : retiré.</summary>
        private const float MinPerimeterStub = 20f;

        /// <summary>
        /// Nettoie les bouts de rue (filtrés par `eligible`) qui rejoignent le périmètre : retire ceux
        /// qui l'abordent à moins de MinPerimeterJoinAngle, ou qui font moins de MinPerimeterStub
        /// depuis un carrefour, puis les rues restées pendantes (bout libre hors périmètre), jusqu'à
        /// point fixe. Un tronçon n'est retiré que si l'autre bout reste relié (carrefour).
        /// </summary>
        internal static void CleanPerimeterEnds(List<RoadSegmentDef> segments, IReadOnlyList<float3> perimeter, Func<RoadSegmentDef, bool> eligible)
        {
            int n = perimeter.Count;
            if (n < 3 || segments.Count == 0)
            {
                return;
            }
            // Distance au périmètre, mémorisée par extrémité (une grande grille partage ses nœuds).
            var info = new Dictionary<float3, (bool on, float2 tangent)>();
            (bool on, float2 tangent) Info(float3 p)
            {
                if (info.TryGetValue(p, out var cached)) return cached;
                float best = float.MaxValue;
                float2 tangent = new float2(1f, 0f);
                for (int i = 0; i < n; i++)
                {
                    float2 a = perimeter[i].xz, ab = perimeter[(i + 1) % n].xz - a;
                    float lengthSq = math.lengthsq(ab);
                    if (lengthSq < 1e-6f) continue;
                    float t = math.saturate(math.dot(p.xz - a, ab) / lengthSq);
                    float d = math.distance(p.xz, a + t * ab);
                    if (d < best)
                    {
                        best = d;
                        tangent = ab / math.sqrt(lengthSq);
                    }
                }
                return info[p] = (best < 0.5f, tangent);
            }

            var incident = new Dictionary<float3, List<int>>();
            var degree = new Dictionary<float3, int>();
            void Link(float3 p, int k)
            {
                if (!incident.TryGetValue(p, out var list)) incident[p] = list = new List<int>();
                list.Add(k);
                degree[p] = degree.TryGetValue(p, out int d) ? d + 1 : 1;
            }
            for (int k = 0; k < segments.Count; k++)
            {
                Link(segments[k].Start, k);
                Link(segments[k].End, k);
            }

            var alive = new bool[segments.Count];
            var queue = new List<int>();
            for (int k = 0; k < segments.Count; k++)
            {
                alive[k] = true;
                if (!eligible(segments[k])) continue;
                // Seuls comptent les tronçons au périmètre ou pendants : le reste n'est jamais retiré d'emblée.
                if (Info(segments[k].Start).on || Info(segments[k].End).on || degree[segments[k].Start] == 1 || degree[segments[k].End] == 1)
                {
                    queue.Add(k);
                }
            }
            while (queue.Count > 0)
            {
                int k = queue[queue.Count - 1];
                queue.RemoveAt(queue.Count - 1);
                if (!alive[k]) continue;
                RoadSegmentDef s = segments[k];
                (bool startOn, float2 startTangent) = Info(s.Start);
                (bool endOn, float2 endTangent) = Info(s.End);
                bool remove = false;
                if (startOn != endOn)
                {
                    float3 on = startOn ? s.Start : s.End, off = startOn ? s.End : s.Start;
                    float2 tangent = startOn ? startTangent : endTangent;
                    // Direction de la rue au point d'arrivée (tangente de l'arc s'il y en a une).
                    float2 direction = math.normalizesafe(off.xz - on.xz);
                    if (s.IsArc)
                    {
                        float3 t = startOn ? s.StartTangent : -s.EndTangent;
                        direction = math.normalizesafe(t.xz, direction);
                    }
                    float angle = math.degrees(math.asin(math.saturate(math.abs(tangent.x * direction.y - tangent.y * direction.x))));
                    float length = math.distance(s.Start.xz, s.End.xz);
                    remove = degree[off] >= 3 && (angle < MinPerimeterJoinAngle || length < MinPerimeterStub);
                }
                else if (!startOn && !endOn && !s.IsCulDeSacEnd && (degree[s.Start] == 1) != (degree[s.End] == 1))
                {
                    // Rue restée pendante après un retrait (bout libre hors périmètre, l'autre bout relié).
                    remove = true;
                }
                if (!remove) continue;
                alive[k] = false;
                foreach (float3 p in new[] { s.Start, s.End })
                {
                    degree[p]--;
                    foreach (int other in incident[p])
                    {
                        if (alive[other] && eligible(segments[other])) queue.Add(other);
                    }
                }
            }
            int write = 0;
            for (int k = 0; k < segments.Count; k++)
            {
                if (alive[k]) segments[write++] = segments[k];
            }
            segments.RemoveRange(write, segments.Count - write);
        }

        private static bool EmitSimpleLoopBlock(List<RoadSegmentDef> segments, float uMin, float uMax, float vMin, float vMax,
            List<float2> polygon, float2 origin, float2 uDir, float2 vDir, float y, bool withCulDeSac, float culDeSacDepth, bool connectToMin)
        {
            float width = uMax - uMin;
            float height = vMax - vMin;
            float uMargin = math.clamp(width * LoopMarginFraction, LoopMarginMin, LoopMarginMax);
            float vMargin = math.clamp(height * LoopMarginFraction, LoopMarginMin, LoopMarginMax);
            if (width < 2f * uMargin + MinSegmentLength || height < 2f * vMargin + MinSegmentLength)
            {
                return false; // quarteirão trop petit pour un laço lisible
            }

            // Les 4 coins du rectangle doivent être à l'intérieur du VRAI polygone (pas juste le
            // centre du quarteirão) — mais au lieu d'abandonner tout de suite dès que le
            // rectangle À TAILLE PLEINE déborde (retour utilisateur en jeu : "que fazer com
            // estes espaços vazios ?", quarteirões de bord entièrement vides sans laço), on
            // essaie plusieurs tailles DÉCROISSANTES, TOUJOURS centrées sur le même point
            // (LoopFitScales, 100% -> 40%) : le rectangle rétrécit symétriquement vers le
            // centre du quarteirão jusqu'à ce que ses 4 coins rentrent, ou jusqu'au plancher
            // (40% — en dessous, le laço deviendrait un point ridicule, mieux vaut laisser
            // vide). AUCUN échantillonnage du contour ici (contrairement à l'ancien
            // EmitLoopBlock) : toujours un simple rectangle, jamais une forme qui épouse le
            // bord — juste sa TAILLE qui s'adapte, en quelques essais bon marché (4 tests
            // PointInPolygon par échelle).
            float centerU = (uMin + uMax) * 0.5f;
            float centerV = (vMin + vMax) * 0.5f;
            if (!PointInPolygon(new float2(centerU, centerV), polygon))
            {
                return false; // le centre lui-même n'appartient pas à la zone développable
            }

            float fullHalfWidth = width * 0.5f - uMargin;
            float fullHalfHeight = height * 0.5f - vMargin;

            float leftU = 0f, rightU = 0f, nearV = 0f, farV = 0f;
            bool fits = false;
            bool shrunk = false;
            foreach (float scale in LoopFitScales)
            {
                float halfW = fullHalfWidth * scale;
                float halfH = fullHalfHeight * scale;
                float l = centerU - halfW, rr = centerU + halfW, n = centerV - halfH, f = centerV + halfH;
                if (PointInPolygon(new float2(l, n), polygon) && PointInPolygon(new float2(rr, n), polygon)
                    && PointInPolygon(new float2(rr, f), polygon) && PointInPolygon(new float2(l, f), polygon))
                {
                    leftU = l; rightU = rr; nearV = n; farV = f;
                    fits = true;
                    shrunk = scale < 1f;
                    break;
                }
            }
            if (!fits)
            {
                return false;
            }
            // Laço rétréci pour tenir dans une forme irrégulière : en dessous de MinLoopSide, ce n'est
            // plus qu'un petit rond de tronçons de 10 m (rejoué depuis une zone libre en jeu). Un laço
            // à pleine taille dans un petit quarteirão régulier reste permis.
            if (shrunk && (rightU - leftU < MinLoopSide || farV - nearV < MinLoopSide))
            {
                return false;
            }

            withCulDeSac = withCulDeSac && (rightU - leftU) >= MinCulDeSacCellWidth && (farV - nearV) >= MinCulDeSacCellWidth;

            float r = math.min(LoopCornerRadiusMax, math.min(rightU - leftU, farV - nearV) * 0.5f);
            if (r < 1f)
            {
                return false; // quarteirão trop étroit pour arrondir les coins de façon visible
            }

            // 4 sommets du rectangle inséré (avant arrondi des coins), sens horaire depuis le
            // coin haut-gauche en U/V local.
            float2 pTL = new float2(leftU, nearV), pTR = new float2(rightU, nearV);
            float2 pBR = new float2(rightU, farV), pBL = new float2(leftU, farV);

            // Points de transition droite/arc, un par coin (entrée et sortie de chaque arc).
            float2 topEnd = new float2(pTR.x - r, pTR.y), rightStart = new float2(pTR.x, pTR.y + r);
            float2 rightEnd = new float2(pBR.x, pBR.y - r), bottomStart = new float2(pBR.x - r, pBR.y);
            float2 bottomEnd = new float2(pBL.x + r, pBL.y), leftStart = new float2(pBL.x, pBL.y - r);
            float2 leftEnd = new float2(pTL.x, pTL.y + r), topStart = new float2(pTL.x + r, pTL.y);

            float3 wU = new float3(uDir.x, 0f, uDir.y);
            float3 wV = new float3(vDir.x, 0f, vDir.y);
            float3 ToW(float2 p) => ToWorld(origin, uDir, vDir, p.x, p.y, y);

            void AddStraight(float2 a, float2 b)
            {
                float3 wa = ToW(a), wb = ToW(b);
                if (math.distance(wa, wb) >= MinSegmentLength)
                {
                    segments.Add(new RoadSegmentDef(wa, wb, isHorizontal: false));
                }
            }
            void AddArc(float2 a, float2 b, float3 tangentIn, float3 tangentOut)
            {
                float3 wa = ToW(a), wb = ToW(b);
                if (math.distance(wa, wb) >= MinSegmentLength)
                {
                    segments.Add(RoadSegmentDef.Arc(wa, wb, tangentIn, tangentOut));
                }
            }

            // L'embranchement (toujours présent) touche le côté connectToMin (nearV=top) ou
            // l'opposé (farV=bottom) EN SON MILIEU (uMid) — le cul-de-sac (si activé) part
            // TOUJOURS du côté opposé à l'embranchement (le "fond" du laço), également en son
            // milieu. uMid coupe donc TOUJOURS le côté de l'embranchement en deux, et EN PLUS
            // le côté du fond si un cul-de-sac y est émis — sans ça, ces branches touchent le
            // milieu d'un côté du laço sans jamais partager de sommet réel avec lui (retour
            // utilisateur en jeu : "colisões entre a via que acede ao loop e o loop" — jonction
            // manquante, exactement le même bug structurel que celui déjà corrigé entre
            // collectrice et laço, voir SplitSegmentsAtMidSpanAttachPoints). Découper
            // INCONDITIONNELLEMENT (au lieu de vérifier d'abord que les 2 moitiés dépassent
            // MinSegmentLength) : AddStraight rejette déjà silencieusement une moitié trop
            // courte tout en gardant l'autre — le sommet de jonction (uMid, ce côté) reste un
            // sommet RÉEL de la moitié survivante dans tous les cas, jamais besoin d'un
            // fallback "jonction approximative".
            float uMid = (leftU + rightU) * 0.5f;
            bool connectorOnTopSide = connectToMin;
            bool spurOnTopSide = !connectToMin;
            bool splitTop = connectorOnTopSide || (withCulDeSac && spurOnTopSide);
            bool splitBottom = !connectorOnTopSide || (withCulDeSac && !spurOnTopSide);
            bool emitSpur = withCulDeSac;

            // Périmètre complet du laço, sens horaire : 4 côtés droits (top/bottom coupés en 2
            // au besoin, voir ci-dessus) + 4 coins arrondis.
            if (splitTop)
            {
                AddStraight(topStart, new float2(uMid, topStart.y));
                AddStraight(new float2(uMid, topStart.y), topEnd);
            }
            else
            {
                AddStraight(topStart, topEnd);
            }
            AddArc(topEnd, rightStart, wU, wV);
            AddStraight(rightStart, rightEnd);
            AddArc(rightEnd, bottomStart, wV, -wU);
            if (splitBottom)
            {
                AddStraight(bottomStart, new float2(uMid, bottomStart.y));
                AddStraight(new float2(uMid, bottomStart.y), bottomEnd);
            }
            else
            {
                AddStraight(bottomStart, bottomEnd);
            }
            AddArc(bottomEnd, leftStart, -wU, -wV);
            AddStraight(leftStart, leftEnd);
            AddArc(leftEnd, topStart, -wV, wU);

            // Embranchement unique vers la collectrice, sur le côté connectToMin (vMin) ou
            // l'opposé (vMax) — point milieu du côté choisi (uMid, désormais un VRAI sommet
            // partagé, voir ci-dessus) jusqu'à la ligne de collectrice réelle (bord du
            // quarteirão AVANT la marge, voir SplitSegmentsAtMidSpanAttachPoints pour le
            // raccord côté collectrice).
            float connectorNearV = connectToMin ? nearV : farV;
            float connectorFarV = connectToMin ? vMin : vMax;
            AddStraight(new float2(uMid, connectorFarV), new float2(uMid, connectorNearV));

            // Cul-de-sac : part du sommet exact où le côté "fond" a été coupé (uMid, ce côté)
            // et remonte vers le centre — spurLength = fraction culDeSacDepth de la profondeur
            // totale du laço, même convention que le reste du mod (CulDeSacDepthUiToReal/
            // GridGenerator ligne classique, plafonné à 0.5-0.8).
            if (emitSpur)
            {
                float farSideV = spurOnTopSide ? nearV : farV;
                float depth = math.abs(farV - nearV);
                float spurLength = depth * math.clamp(culDeSacDepth, 0.5f, 0.8f);
                float spurSign = spurOnTopSide ? 1f : -1f;
                float2 spurStart = new float2(uMid, farSideV);
                float2 spurEnd = new float2(uMid, farSideV + spurSign * spurLength);
                float3 wStart = ToW(spurStart), wEnd = ToW(spurEnd);
                if (math.distance(wStart, wEnd) >= MinSegmentLength)
                {
                    segments.Add(new RoadSegmentDef(wStart, wEnd, isHorizontal: false, isCulDeSacEnd: true));
                }
            }

            return true;
        }

        /// <summary>Vrai si point (coordonnées locales) est intérieur à polygon (règle pair-impair, ray casting horizontal). Utilisé pour valider qu'un super-îlot Loop est entièrement à l'intérieur du périmètre.</summary>
        private static bool PointInPolygon(float2 point, List<float2> polygon)
        {
            bool inside = false;
            int n = polygon.Count;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                float2 a = polygon[i];
                float2 b = polygon[j];
                bool crosses = (a.y > point.y) != (b.y > point.y);
                if (crosses)
                {
                    float t = (point.y - a.y) / (b.y - a.y);
                    float xCross = a.x + t * (b.x - a.x);
                    if (point.x < xCross)
                    {
                        inside = !inside;
                    }
                }
            }
            return inside;
        }

    }
}
