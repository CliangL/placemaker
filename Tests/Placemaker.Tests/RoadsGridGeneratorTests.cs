using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Placemaker.Core.Layers.Roads.Generators;
using Unity.Mathematics;
using Xunit;

namespace Placemaker.Tests
{
    /// <summary>
    /// Vérifie le pré-découpage de la grille aux croisements internes :
    ///  - chaque croisement intérieur au polygone est une extrémité partagée par
    ///    exactement 4 sous-segments (2 par ligne) ;
    ///  - un croisement au bord du polygone (une ou deux lignes s'y terminent) est
    ///    partagé par 2 ou 3 sous-segments ;
    ///  - aucun croisement ne se trouve en milieu de sous-segment ;
    ///  - les extrémités qui se rejoignent sont EXACTEMENT identiques (mêmes floats),
    ///    condition pour que le jeu les fusionne en un seul nœud ;
    ///  - aucun sous-segment plus court que MinSegmentLength.
    /// </summary>
    public class RoadsGridGeneratorTests
    {
        // ------------------------------------------------------------------
        // Cas 1 : carré axis-aligned, mode "ajuster à l'aire" 3×3
        // → 9 croisements internes, chacun partagé par exactement 4 sous-segments.
        // ------------------------------------------------------------------

        [Fact]
        public void RemoveBacktracks_DropsShortReversals_KeepsRealCorners()
        {
            // Vécu sur un vrai périmètre : les échantillons d'une courbe dépassaient le nœud
            // suivant de ~3 m, puis le contour revenait en arrière (virage de 179°).
            var loop = new List<float3>
            {
                new float3(0f, 1f, 0f), new float3(300f, 2f, 0f), new float3(303f, 2f, 0f), new float3(300.5f, 2f, 0.2f),
                new float3(300f, 3f, 300f), new float3(0f, 4f, 300f),
            };
            GridGenerator.RemoveBacktracks(loop);
            Assert.Equal(5, loop.Count); // seul le point qui dépassait (303 m) part
            Assert.DoesNotContain(loop, p => p.x > 301f);
            Assert.Contains(loop, p => p.x == 300f && p.z == 300f && p.y == 3f); // coin à 90° gardé, hauteur conservée
        }

        [Fact]
        public void Square_FitToArea_EveryInternalCrossingSharedByExactlyFourSubSegments()
        {
            var nodes = new List<float3>
            {
                new float3(0f, 0f, 0f),
                new float3(300f, 0f, 0f),
                new float3(300f, 0f, 300f),
                new float3(0f, 0f, 300f),
            };
            var parameters = new GridParameters
            {
                Mode = SpacingMode.FitToArea,
                Rows = 3,
                Columns = 3,
                SpacingMeters = 60f,
            };

            var segments = GridGenerator.GenerateGrid(nodes, parameters);

            // 3 lignes u × 4 tronçons + 3 lignes v × 4 tronçons.
            Assert.Equal(24, segments.Count);

            var shared = SharedEndpoints(segments);
            Assert.Equal(9, shared.Count);
            foreach (var pair in shared)
            {
                Assert.Equal(4, pair.Value);
            }

            AssertCommonInvariants(segments);
        }

        // ------------------------------------------------------------------
        // Cas 2 : polygone concave en L, espacement fixe
        // → 12 croisements, tous intérieurs, chacun partagé par 4 sous-segments.
        // ------------------------------------------------------------------

        [Fact]
        public void ConcaveLShape_FixedSpacing_CrossingsSharedByFourSubSegments()
        {
            var nodes = new List<float3>
            {
                new float3(0f, 0f, 0f),
                new float3(300f, 0f, 0f),
                new float3(300f, 0f, 150f),
                new float3(150f, 0f, 150f),
                new float3(150f, 0f, 300f),
                new float3(0f, 0f, 300f),
            };
            var parameters = new GridParameters
            {
                Mode = SpacingMode.FixedSpacing,
                Rows = 3,
                Columns = 3,
                SpacingMeters = 60f,
            };

            var segments = GridGenerator.GenerateGrid(nodes, parameters);

            // Lignes u : 60 et 120 traversent tout le L (5 tronçons), 180 et 240
            // seulement la branche basse (3 tronçons) ; symétrique pour v.
            Assert.Equal(32, segments.Count);

            var shared = SharedEndpoints(segments);
            Assert.Equal(12, shared.Count);
            foreach (var pair in shared)
            {
                Assert.Equal(4, pair.Value);
            }

            AssertCommonInvariants(segments);
        }

        // ------------------------------------------------------------------
        // Cas 3 : croisement AU BORD du polygone. L'encoche en V du polygone a son
        // apex exactement sur le croisement (200, 150) : la ligne u s'y termine
        // (clippée par l'encoche) pendant que la ligne v passe de part et d'autre.
        // → jonction en T : le point est partagé par exactement 3 sous-segments.
        // ------------------------------------------------------------------

        [Fact]
        public void VNotchApexOnCrossing_BorderCrossingSharedByExactlyThreeSubSegments()
        {
            var nodes = new List<float3>
            {
                new float3(0f, 0f, 0f),
                new float3(400f, 0f, 0f),
                new float3(400f, 0f, 300f),
                new float3(250f, 0f, 300f),
                new float3(200f, 0f, 150f), // apex de l'encoche = croisement u=200 × v=150
                new float3(150f, 0f, 300f),
                new float3(0f, 0f, 300f),
            };
            var parameters = new GridParameters
            {
                Mode = SpacingMode.FitToArea,
                Rows = 1,
                Columns = 1,
                SpacingMeters = 60f,
            };

            var segments = GridGenerator.GenerateGrid(nodes, parameters);

            // Ligne u=200 : 1 tronçon (arrêtée à l'apex) ; ligne v=150 : 2 tronçons
            // (un sous chaque bras), qui se rejoignent à l'apex.
            Assert.Equal(3, segments.Count);

            var shared = SharedEndpoints(segments);
            var apex = Assert.Single(shared);
            Assert.Equal(3, apex.Value);
            Assert.Equal(200f, apex.Key.x, 3);
            Assert.Equal(150f, apex.Key.z, 3);

            AssertCommonInvariants(segments);
        }

        // ------------------------------------------------------------------
        // Invariants communs
        // ------------------------------------------------------------------

        /// <summary>Extrémités partagées par au moins 2 sous-segments, avec leur multiplicité.</summary>
        private static Dictionary<float3, int> SharedEndpoints(List<RoadSegmentDef> segments)
        {
            var counts = new Dictionary<float3, int>();
            foreach (var segment in segments)
            {
                counts[segment.Start] = counts.TryGetValue(segment.Start, out int s) ? s + 1 : 1;
                counts[segment.End] = counts.TryGetValue(segment.End, out int e) ? e + 1 : 1;
            }
            return counts.Where(p => p.Value >= 2).ToDictionary(p => p.Key, p => p.Value);
        }

        internal static void AssertCommonInvariants(List<RoadSegmentDef> segments)
        {
            var counts = new Dictionary<float3, int>();
            foreach (var segment in segments)
            {
                counts[segment.Start] = counts.TryGetValue(segment.Start, out int s) ? s + 1 : 1;
                counts[segment.End] = counts.TryGetValue(segment.End, out int e) ? e + 1 : 1;
            }
            var endpoints = counts.Keys.ToList();

            // 1) Pas de segment sous la longueur minimale.
            foreach (var segment in segments)
            {
                Assert.True(math.distance(segment.Start.xz, segment.End.xz) >= GridGenerator.MinSegmentLength - 1e-3f,
                    $"Sous-segment trop court : {segment.Start} → {segment.End}");
            }

            // 2) Deux extrémités très proches doivent être EXACTEMENT le même point
            //    (mêmes floats) — sinon le jeu créerait deux nœuds au lieu d'un.
            for (int i = 0; i < endpoints.Count; i++)
            {
                for (int j = i + 1; j < endpoints.Count; j++)
                {
                    Assert.True(math.distance(endpoints[i], endpoints[j]) >= 0.5f,
                        $"Extrémités distinctes quasi confondues (fusion impossible) : {endpoints[i]} vs {endpoints[j]}");
                }
            }

            // 3) Aucune extrémité partagée ne se trouve en MILIEU d'un autre segment :
            //    tout croisement doit avoir découpé les deux lignes.
            foreach (var pair in counts.Where(p => p.Value >= 2))
            {
                float3 point = pair.Key;
                foreach (var segment in segments)
                {
                    if (point.Equals(segment.Start) || point.Equals(segment.End))
                        continue;
                    float distance = DistanceToSegmentXZ(point.xz, segment.Start.xz, segment.End.xz);
                    Assert.True(distance > 0.5f,
                        $"Croisement {point} en plein milieu du segment {segment.Start} → {segment.End}");
                }
            }
        }

        private static float DistanceToSegmentXZ(float2 point, float2 a, float2 b)
        {
            float2 ab = b - a;
            float t = math.clamp(math.dot(point - a, ab) / math.dot(ab, ab), 0f, 1f);
            return math.distance(point, a + t * ab);
        }
    }

    /// <summary>Vérifie le paramètre AngleOffsetDegrees : 0° = comportement inchangé, 90° = axes échangés, clipping correct à tout angle.</summary>
    public class GridGeneratorAngleTests
    {
        private static readonly List<float3> SquareNodes = new List<float3>
        {
            new float3(0f, 0f, 0f),
            new float3(300f, 0f, 0f),
            new float3(300f, 0f, 300f),
            new float3(0f, 0f, 300f),
        };

        [Fact]
        public void AngleZero_MatchesPreviousBehaviorExactly()
        {
            var parameters = new GridParameters
            {
                Mode = SpacingMode.FitToArea,
                Rows = 3,
                Columns = 3,
                SpacingMeters = 60f,
                AngleOffsetDegrees = 0f,
            };

            var segments = GridGenerator.GenerateGrid(SquareNodes, parameters);

            // Même résultat que le test de régression sans angle (24 segments,
            // 3 lignes u × 4 tronçons + 3 lignes v × 4 tronçons).
            Assert.Equal(24, segments.Count);
        }

        [Fact]
        public void Angle90_SwapsAxisOfVariation()
        {
            // Carré axis-aligned, origine (0,0), arête la plus longue = (0,0)->(300,0)
            // donc uDir=(1,0) à 0°. Une seule ligne de chaque jeu (Rows=1, Columns=1)
            // pour isoler la géométrie sans ambiguïté de tri.
            var parameters0 = new GridParameters
            {
                Mode = SpacingMode.FitToArea,
                Rows = 1,
                Columns = 1,
                SpacingMeters = 60f,
                AngleOffsetDegrees = 0f,
            };
            var parameters90 = parameters0;
            parameters90.AngleOffsetDegrees = 90f;

            var segments0 = GridGenerator.GenerateGrid(SquareNodes, parameters0);
            var segments90 = GridGenerator.GenerateGrid(SquareNodes, parameters90);

            // La ligne des colonnes croise celle des rangées en plein milieu du carré
            // et se retrouve donc pré-découpée en 2 sous-segments (Chantier 1) : chaque
            // morceau reste individuellement droit, First() suffit pour lire son axe.

            // À 0° : la ligne des colonnes est verticale en monde (X constant, Z varie).
            var columnLine0 = segments0.First(s => !s.IsHorizontal);
            Assert.Equal(columnLine0.Start.x, columnLine0.End.x, 2);
            Assert.NotEqual(columnLine0.Start.z, columnLine0.End.z, 2);

            // À 90° : la même ligne des colonnes devient horizontale en monde
            // (Z constant, X varie) — les axes ont basculé.
            var columnLine90 = segments90.First(s => !s.IsHorizontal);
            Assert.Equal(columnLine90.Start.z, columnLine90.End.z, 2);
            Assert.NotEqual(columnLine90.Start.x, columnLine90.End.x, 2);
        }

        [Fact]
        public void ObliqueAngle_ClipsPolygonCorrectlyOnConcaveShape()
        {
            // L concave, angle 45° : pas de valeurs attendues à la main (trop
            // fastidieux à dériver), on vérifie les invariants structurels du
            // clipping (segments bien formés, aucun croisement en milieu de segment).
            var nodes = new List<float3>
            {
                new float3(0f, 0f, 0f),
                new float3(300f, 0f, 0f),
                new float3(300f, 0f, 150f),
                new float3(150f, 0f, 150f),
                new float3(150f, 0f, 300f),
                new float3(0f, 0f, 300f),
            };
            var parameters = new GridParameters
            {
                Mode = SpacingMode.FixedSpacing,
                Rows = 3,
                Columns = 3,
                SpacingMeters = 60f,
                AngleOffsetDegrees = 45f,
            };

            var segments = GridGenerator.GenerateGrid(nodes, parameters);

            Assert.NotEmpty(segments);
            foreach (var segment in segments)
            {
                Assert.True(math.distance(segment.Start.xz, segment.End.xz) >= GridGenerator.MinSegmentLength - 1e-3f,
                    $"Sous-segment trop court à 45° : {segment.Start} → {segment.End}");
                Assert.False(float.IsNaN(segment.Start.x) || float.IsNaN(segment.Start.z)
                    || float.IsNaN(segment.End.x) || float.IsNaN(segment.End.z),
                    "Coordonnée NaN produite par le clipping à angle oblique.");
            }
        }
    }

    /// <summary>
    /// Vérifie le mode culs-de-sac : les collectrices (lignes parallèles à l'axe
    /// principal, "rangées") restent toujours traversantes ; les colonnes
    /// (perpendiculaires) deviennent des impasses selon CulDeSacRatio, avec
    /// alternance haut/bas si Staggered et profondeur CulDeSacDepth.
    ///
    /// Géométrie commune : carré (0,0)-(300,0)-(300,300)-(0,300). Arête la plus
    /// longue (0,0)-(300,0) ⇒ uDir=(1,0) Est (axe principal), vDir=(0,1) Nord.
    /// Columns=1 place une seule colonne (u=150, monde X=150 constant) ; Rows=N
    /// place N collectrices équiréparties (v=const, monde Z=const). Le point monde
    /// d'une colonne à la coordonnée t vaut donc (150, t) — sert à vérifier
    /// précisément les extrémités des impasses.
    /// </summary>
    public class GridGeneratorCulDeSacTests
    {
        private static readonly List<float3> SquareNodes = new List<float3>
        {
            new float3(0f, 0f, 0f),
            new float3(300f, 0f, 0f),
            new float3(300f, 0f, 300f),
            new float3(0f, 0f, 300f),
        };

        private static GridParameters BaseParameters(int rows) => new GridParameters
        {
            Mode = SpacingMode.FitToArea,
            Rows = rows,
            Columns = 1,
            SpacingMeters = 60f,
            CulDeSacMode = true,
            CulDeSacDepth = 0.75f,
            Staggered = true,
            CulDeSacRatio = 100f,
        };

        /// <summary>Segments de la colonne (u-line) uniquement : IsHorizontal=false leur est propre, les collectrices ont IsHorizontal=true.</summary>
        private static List<RoadSegmentDef> ColumnSegments(List<RoadSegmentDef> segments) =>
            segments.Where(s => !s.IsHorizontal).ToList();

        [Fact]
        public void Disabled_ProducesExactlySameOutputAsWithoutCulDeSac()
        {
            var withFlag = BaseParameters(3);
            withFlag.CulDeSacMode = false;
            withFlag.Columns = 3; // reproduit exactement le test de régression 3x3

            var withoutField = new GridParameters
            {
                Mode = SpacingMode.FitToArea,
                Rows = 3,
                Columns = 3,
                SpacingMeters = 60f,
            };

            var segments1 = GridGenerator.GenerateGrid(SquareNodes, withFlag);
            var segments2 = GridGenerator.GenerateGrid(SquareNodes, withoutField);

            Assert.Equal(24, segments1.Count);
            Assert.Equal(segments2.Count, segments1.Count);
        }

        [Fact]
        public void Ratio100_NoColumnBlockRemainsFullLength()
        {
            // Rows=2 : collectrices à v=100 et v=200. La colonne u=150 traverse
            // [0,300] ⇒ chaîne [0(bord),100(collectrice),200(collectrice),300(bord)]
            // ⇒ 3 blocs. Bloc [0,100] : départ=bord (pas de vraie collectrice) ⇒
            // supprimé. Bloc [100,200] : alternance impaire ⇒ part de 200 vers 100,
            // s'arrête à 200+(100-200)*0.75=125. Bloc [200,300] : alternance paire
            // ⇒ part de 200 vers 300, s'arrête à 200+(300-200)*0.75=275.
            var parameters = BaseParameters(2);

            var segments = GridGenerator.GenerateGrid(SquareNodes, parameters);
            var columns = ColumnSegments(segments);

            Assert.Equal(2, columns.Count); // le bloc de bord est supprimé

            foreach (var segment in columns)
            {
                float length = math.distance(segment.Start.xz, segment.End.xz);
                Assert.True(length < 100f - 1f, $"Impasse pas plus courte que le bloc complet (100 m) : {length}");
                Assert.Equal(75f, length, 1); // 0.75 * 100
            }

            // Les deux impasses partent bien de la collectrice centrale (v=200,
            // monde (150,200)) et s'arrêtent chacune à 75 m dans une direction opposée.
            var endpoints = columns.SelectMany(s => new[] { s.Start, s.End }).ToList();
            Assert.Contains(endpoints, p => math.distance(p.xz, new float2(150f, 200f)) < 0.5f);
            Assert.Contains(endpoints, p => math.distance(p.xz, new float2(150f, 125f)) < 0.5f);
            Assert.Contains(endpoints, p => math.distance(p.xz, new float2(150f, 275f)) < 0.5f);
        }

        [Fact]
        public void CulDeSacBlock_MarksOnlyTheFreeEndAsCulDeSacEnd()
        {
            // Même géométrie que Ratio100_NoColumnBlockRemainsFullLength : 2 impasses,
            // chacune partant d'une vraie collectrice (Start, jamais marqué) et
            // s'arrêtant à son bout libre (End, doit être marqué IsCulDeSacEnd).
            var segments = GridGenerator.GenerateGrid(SquareNodes, BaseParameters(2));
            var columns = ColumnSegments(segments);

            Assert.Equal(2, columns.Count);
            foreach (var segment in columns)
            {
                Assert.True(segment.IsCulDeSacEnd, "Le bout libre d'une impasse doit être marqué IsCulDeSacEnd.");
            }

            // Les collectrices traversantes (lignes horizontales) ne sont jamais des impasses.
            var collectors = segments.Where(s => s.IsHorizontal).ToList();
            Assert.NotEmpty(collectors);
            Assert.All(collectors, s => Assert.False(s.IsCulDeSacEnd));
        }

        [Fact]
        public void StaggeredTrueVersusFalse_ProduceDifferentStartingCollectors()
        {
            // Même géométrie que Ratio100_NoColumnBlockRemainsFullLength. Sans
            // alternance, le bloc [100,200] doit partir de la collectrice basse
            // (v=100) au lieu de la haute (v=200).
            var staggered = BaseParameters(2);
            var notStaggered = BaseParameters(2);
            notStaggered.Staggered = false;

            var columnsStaggered = ColumnSegments(GridGenerator.GenerateGrid(SquareNodes, staggered));
            var columnsPlain = ColumnSegments(GridGenerator.GenerateGrid(SquareNodes, notStaggered));

            var staggeredEndpoints = columnsStaggered.SelectMany(s => new[] { s.Start, s.End }).ToList();
            var plainEndpoints = columnsPlain.SelectMany(s => new[] { s.Start, s.End }).ToList();

            // Avec Staggered=true, une impasse part de la collectrice haute (v=200)
            // vers le bas et s'arrête à v=125.
            Assert.Contains(staggeredEndpoints, p => math.distance(p.xz, new float2(150f, 125f)) < 0.5f);
            // Avec Staggered=false, le même bloc part de la collectrice basse (v=100)
            // vers le haut et s'arrête à v=175 (100+(200-100)*0.75) — un résultat différent.
            Assert.Contains(plainEndpoints, p => math.distance(p.xz, new float2(150f, 175f)) < 0.5f);
            Assert.DoesNotContain(plainEndpoints, p => math.distance(p.xz, new float2(150f, 125f)) < 0.5f);
        }

        [Fact]
        public void Ratio50_AlternatesThroughAndCulDeSacBlocksDeterministically()
        {
            // Rows=4 : collectrices à v=60,120,180,240 ⇒ chaîne à 6 points, 5 blocs
            // de 60 m chacun (idx 0..4). Motif "une fois sur 2" (ratio=50, N=2) :
            // blocs pairs (0,2,4) tentent une impasse, impairs (1,3) restent
            // traversants. Le bloc 0 est en bord (supprimé) ; 2 et 4 réussissent
            // (départ = vraie collectrice) ⇒ 2 impasses (45 m = 0.75*60) + 2
            // traversées complètes (60 m) survivent, soit 4 segments de colonne.
            var parameters = BaseParameters(4);
            parameters.CulDeSacRatio = 50f;

            var columns = ColumnSegments(GridGenerator.GenerateGrid(SquareNodes, parameters));

            Assert.Equal(4, columns.Count);

            int fullLength = columns.Count(s => math.distance(s.Start.xz, s.End.xz) > 59f);
            int stubLength = columns.Count(s =>
            {
                float length = math.distance(s.Start.xz, s.End.xz);
                return length > 44f && length < 46f;
            });

            Assert.Equal(2, fullLength); // blocs 1 et 3 : traversants (60 m)
            Assert.Equal(2, stubLength); // blocs 2 et 4 : impasses (45 m)
        }

        [Fact]
        public void DepthOutOfRange_IsClampedToValidBounds()
        {
            // CulDeSacDepth hors [0.5, 0.9] (ex. valeur non passée par l'UI/settings) :
            // clampé plutôt que de produire une impasse dégénérée (longueur nulle ou négative).
            var tooLow = BaseParameters(2);
            tooLow.CulDeSacDepth = 0f;
            var tooHigh = BaseParameters(2);
            tooHigh.CulDeSacDepth = 1f;

            var columnsLow = ColumnSegments(GridGenerator.GenerateGrid(SquareNodes, tooLow));
            var columnsHigh = ColumnSegments(GridGenerator.GenerateGrid(SquareNodes, tooHigh));

            foreach (var segment in columnsLow.Concat(columnsHigh))
            {
                float length = math.distance(segment.Start.xz, segment.End.xz);
                Assert.InRange(length, 50f - 1f, 90f + 1f); // 0.5..0.9 * bloc de 100 m
            }
        }

        [Fact]
        public void AxisRows_AppliesCulDeSacToRowsInsteadOfColumns()
        {
            // Transposé de Ratio100_NoColumnBlockRemainsFullLength : Columns=2 (au lieu de
            // Rows=2) fournit les 2 croisements internes à l'UNIQUE ligne de rangée
            // (Rows=1), qui devient la candidate aux impasses avec CulDeSacAxis.Rows.
            var parameters = BaseParameters(1);
            parameters.Columns = 2;
            parameters.CulDeSacAxis = CulDeSacAxis.Rows;

            var segments = GridGenerator.GenerateGrid(SquareNodes, parameters);
            var columns = ColumnSegments(segments); // axisIsU (colonnes) : ne doivent JAMAIS être des impasses ici.
            var rows = segments.Except(columns).ToList();

            Assert.NotEmpty(columns);
            Assert.All(columns, s => Assert.False(s.IsCulDeSacEnd));
            Assert.Contains(rows, s => s.IsCulDeSacEnd);
        }

        [Fact]
        public void AxisBoth_AppliesCulDeSacToBothColumnsAndRows()
        {
            // Rows=2 ET Columns=2 : les deux axes ont des croisements internes à
            // proposer comme collectrices de départ pour l'autre axe.
            var parameters = BaseParameters(2);
            parameters.Columns = 2;
            parameters.CulDeSacAxis = CulDeSacAxis.Both;

            var segments = GridGenerator.GenerateGrid(SquareNodes, parameters);
            var columns = ColumnSegments(segments);
            var rows = segments.Except(columns).ToList();

            Assert.Contains(columns, s => s.IsCulDeSacEnd);
            Assert.Contains(rows, s => s.IsCulDeSacEnd);
        }
    }

    /// <summary>
    /// Vérifie MinNodeDistance : un nœud généré (croisement, ou bout d'impasse) qui
    /// tomberait à moins de MinNodeDistance d'un autre nœud déjà établi est purement
    /// omis, jamais fusionné ni décalé.
    ///
    /// La quasi-coïncidence la plus simple à provoquer de façon déterministe est une
    /// impasse dont la profondeur (proche de la borne haute 90 %) laisse un bloc court
    /// se terminer à moins de MinNodeDistance de la collectrice qu'elle approche sans
    /// jamais l'atteindre : bloc de 70 m à 90 % de profondeur ⇒ écart de 7 m &lt; 8 m.
    /// </summary>
    public class GridGeneratorMinNodeDistanceTests
    {
        private static readonly List<float3> SquareNodes = new List<float3>
        {
            new float3(0f, 0f, 0f),
            new float3(300f, 0f, 0f),
            new float3(300f, 0f, 300f),
            new float3(0f, 0f, 300f),
        };

        [Fact]
        public void NormalGrid_NoOmission()
        {
            var parameters = new GridParameters
            {
                Mode = SpacingMode.FitToArea,
                Rows = 3,
                Columns = 3,
                SpacingMeters = 60f,
            };

            var segments = GridGenerator.GenerateGrid(SquareNodes, parameters, out int omittedNodeCount);

            Assert.Equal(0, omittedNodeCount);
            Assert.Equal(24, segments.Count); // identique au test de régression sans MinNodeDistance
        }

        [Fact]
        public void CulDeSacStubTooCloseToTargetCollector_IsOmittedNotShortened()
        {
            // Rows=3 (collectrices tous les 70 m sur une bande [80,290] volontairement
            // hors-grille — on force plutôt la géométrie via FixedSpacing) : plus simple,
            // on compose directement un rectangle où l'espacement des collectrices vaut
            // exactement 70 m pour isoler un seul bloc du bon gabarit.
            var nodes = new List<float3>
            {
                new float3(0f, 0f, 0f),
                new float3(300f, 0f, 0f),
                new float3(300f, 0f, 105f), // 3 x 35 m
                new float3(0f, 0f, 105f),
            };
            var parameters = new GridParameters
            {
                Mode = SpacingMode.FixedSpacing,
                Columns = 1,
                Rows = 1,
                SpacingMeters = 35f, // collectrices à v=35 et v=70
                CulDeSacMode = true,
                CulDeSacDepth = 0.9f, // plafonné à 0.8 (voir GridGenerator) : écart de 20 % du bloc = 7 m
                Staggered = true,
                CulDeSacRatio = 100f,
            };

            var segments = GridGenerator.GenerateGrid(nodes, parameters, out int omittedNodeCount);
            var columns = segments.Where(s => !s.IsHorizontal).ToList();

            // Bloc [0,35] (bord, départ non-collectrice) : supprimé comme avant.
            // Bloc [35,70] : impair ⇒ part de la collectrice v=70 vers v=35,
            // s'arrêterait à 70+(35-70)*0.8=42, à 7 m de la collectrice v=35
            // (< MinNodeDistance=8) ⇒ omis pour quasi-coïncidence, PAS raccourci.
            // Bloc [70,105] : pair ⇒ part de v=70 vers v=105 (bord, non-collecteur),
            // s'arrête à 70+(105-70)*0.8=98, 7 m du bord — le bord n'étant pas un
            // nœud "déjà établi", ce bloc-ci N'EST PAS concerné par MinNodeDistance.
            Assert.True(omittedNodeCount >= 1, "Le bloc [35,70] aurait dû être omis pour quasi-coïncidence.");

            // Aucun segment de colonne ne doit s'arrêter à moins de MinNodeDistance
            // d'une collectrice sans être en réalité fusionné avec elle (même point
            // bit-exact) : pas de quasi-doublon flottant.
            var collectorPositions = new[] { new float2(150f, 70f), new float2(150f, 140f) };
            foreach (var segment in columns)
            {
                foreach (float2 collector in collectorPositions)
                {
                    float distToStart = math.distance(segment.Start.xz, collector);
                    float distToEnd = math.distance(segment.End.xz, collector);
                    // Soit confondu (segment qui rejoint réellement la collectrice),
                    // soit largement au-delà de MinNodeDistance — jamais entre les deux.
                    Assert.True(distToStart < 0.5f || distToStart >= GridGenerator.MinNodeDistance - 0.01f,
                        $"Extrémité de départ à {distToStart} m d'une collectrice : ni fusionnée, ni assez loin.");
                    Assert.True(distToEnd < 0.5f || distToEnd >= GridGenerator.MinNodeDistance - 0.01f,
                        $"Extrémité d'arrivée à {distToEnd} m d'une collectrice : ni fusionnée, ni assez loin.");
                }
            }
        }

        [Fact]
        public void NearCoincidence_NeverCrashesAndRestOfGridStaysWellFormed()
        {
            // Grille dense sur un polygone tourné à angle oblique, avec culs-de-sac :
            // combine plusieurs sources possibles de quasi-coïncidence (croisements
            // rapprochés, impasses profondes) sans qu'aucune n'entraîne de plantage,
            // de doublon, ni de segment sous MinSegmentLength.
            var nodes = new List<float3>
            {
                new float3(0f, 0f, 0f),
                new float3(280f, 0f, 90f),
                new float3(190f, 0f, 370f),
                new float3(-90f, 0f, 280f),
            };
            var parameters = new GridParameters
            {
                Mode = SpacingMode.FitToArea,
                Columns = 6,
                Rows = 4,
                SpacingMeters = 60f,
                AngleOffsetDegrees = 33f,
                CulDeSacMode = true,
                CulDeSacDepth = 0.9f,
                Staggered = true,
                CulDeSacRatio = 100f,
            };

            List<RoadSegmentDef> segments = null;
            Exception thrown = null;
            try
            {
                segments = GridGenerator.GenerateGrid(nodes, parameters, out int omittedNodeCount);
            }
            catch (Exception e)
            {
                thrown = e;
            }

            Assert.Null(thrown);
            Assert.NotNull(segments);

            // Pas de doublon : toutes les extrémités identiques doivent être EXACTEMENT
            // les mêmes floats (fusion valide), jamais deux points distincts séparés de
            // moins de MinNodeDistance (ce serait la quasi-coïncidence non nettoyée).
            var endpoints = segments.SelectMany(s => new[] { s.Start, s.End }).Distinct().ToList();
            for (int i = 0; i < endpoints.Count; i++)
            {
                for (int j = i + 1; j < endpoints.Count; j++)
                {
                    float distance = math.distance(endpoints[i].xz, endpoints[j].xz);
                    Assert.True(distance >= GridGenerator.MinNodeDistance - 0.5f,
                        $"Deux nœuds distincts quasi confondus non nettoyés : {endpoints[i]} vs {endpoints[j]} ({distance} m).");
                }
            }

            foreach (var segment in segments)
            {
                float length = math.distance(segment.Start.xz, segment.End.xz);
                Assert.True(length >= MinAcceptableLength(parameters),
                    $"Segment sous la longueur minimale acceptable : {length} m.");
            }
        }

        [Fact]
        public void NearCoincidentCrossings_MergeIntoASharedNodeInsteadOfDroppingALine()
        {
            // Bug corrigé (comme le "Super nó" de NetworkTools) : un croisement à moins de
            // MinNodeDistance d'un croisement déjà accepté était auparavant purement omis — sa
            // ligne u ET sa ligne v ne recevaient alors AUCUNE subdivision à cet endroit,
            // contrairement à toutes les autres lignes de la grille (une des deux, voire les
            // deux, "perdue"). Il doit maintenant être FUSIONNÉ avec ce point existant : les
            // DEUX lignes rejoignent le nœud partagé (même position bit-exacte).
            //
            // FitToArea sur un petit périmètre avec beaucoup de colonnes/lignes : l'espacement
            // RÉSULTANT (côté / nombre de colonnes) tombe sous MinNodeDistance (80/12 ≈ 6,7 m
            // < 8 m), donc deux croisements adjacents sur une même ligne (même u ou même v,
            // l'autre coordonnée décalée d'un seul pas de grille) tombent nécessairement à
            // moins de MinNodeDistance l'un de l'autre — contrairement à FixedSpacing avec un
            // espacement normal (>= 10 m côté UI), où deux croisements DISTINCTS ne peuvent
            // jamais être plus proches que l'espacement lui-même (réseau parfaitement régulier).
            // Reproduit ce qu'un joueur peut obtenir via l'UI : petit périmètre + beaucoup de
            // colonnes/lignes (jusqu'à 12, voir gridPanel.tsx), pas besoin d'un espacement
            // inaccessible depuis le panneau (min 10 m).
            var nodes = new List<float3>
            {
                new float3(0f, 0f, 0f),
                new float3(80f, 0f, 0f),
                new float3(80f, 0f, 80f),
                new float3(0f, 0f, 80f),
            };
            var parameters = new GridParameters
            {
                Mode = SpacingMode.FitToArea,
                Columns = 12,
                Rows = 12,
            };

            var segments = GridGenerator.GenerateGrid(nodes, parameters, out int mergedCount);
            Assert.True(mergedCount > 0, "Ce périmètre dense/oblique devrait produire au moins une fusion de croisements.");

            // Regroupe les extrémités par position bit-exacte (une fusion réussie donne des
            // floats identiques, pas juste "proches" — voir BuildSubSegments). Tout nœud
            // partagé par 3 segments ou plus est un vrai croisement multi-branches (ou un
            // croisement fusionné) : il doit alors avoir des segments des DEUX orientations,
            // jamais une seule — la signature exacte du bug (une ligne "oubliée" par l'omission).
            var byPosition = new Dictionary<string, (bool hasHorizontal, bool hasVertical, int count)>();
            void Record(float3 pos, bool isHorizontal)
            {
                string key = $"{pos.x:F3}_{pos.z:F3}";
                (bool hasHorizontal, bool hasVertical, int count) entry = byPosition.TryGetValue(key, out var existing)
                    ? existing
                    : (false, false, 0);
                byPosition[key] = (entry.hasHorizontal || isHorizontal, entry.hasVertical || !isHorizontal, entry.count + 1);
            }
            foreach (RoadSegmentDef s in segments)
            {
                Record(s.Start, s.IsHorizontal);
                Record(s.End, s.IsHorizontal);
            }

            foreach (var kvp in byPosition)
            {
                if (kvp.Value.count >= 3)
                {
                    Assert.True(kvp.Value.hasHorizontal && kvp.Value.hasVertical,
                        $"Nœud à {kvp.Key} avec {kvp.Value.count} segments mais une seule orientation représentée — ligne perdue au lieu de fusionnée.");
                }
            }
        }

        private static float MinAcceptableLength(GridParameters parameters)
        {
            // Une impasse peut être aussi courte que MinSegmentLength ; une traversée
            // normale aussi. On garde une marge de sécurité modeste sur l'assertion.
            return GridGenerator.MinSegmentLength - 0.5f;
        }
    }

    /// <summary>
    /// Chantier 1 (bug prioritaire) : le polygone du périmètre doit suivre la courbe
    /// RÉELLE d'une arête existante entre deux nœuds consécutifs (rond-point, virage...),
    /// pas la corde droite entre eux — sinon la grille générée peut déborder sur la route
    /// courbe elle-même. GridGenerator.SampleCurve (pur, sans dépendance ECS/Colossal.Mathematics
    /// — voir son en-tête) échantillonne cette courbe ; GridRoadToolSystem.BuildCurveAwarePerimeterPositions
    /// (non testable ici, dépend de l'ECS) l'insère dans la liste de points avant génération.
    /// </summary>
    public class GridGeneratorCurveSamplingTests
    {
        // ------------------------------------------------------------------
        // SampleCurve seul : les points échantillonnés doivent suivre la courbe,
        // pas la corde droite entre les extrémités.
        // ------------------------------------------------------------------

        [Fact]
        public void SampleCurve_StraightBezier_ReturnsPointsOnTheChord()
        {
            // Points de contrôle alignés : la "courbe" est en fait une droite.
            var a = new float3(0f, 0f, 0f);
            var b = new float3(33f, 0f, 0f);
            var c = new float3(66f, 0f, 0f);
            var d = new float3(100f, 0f, 0f);

            List<float3> samples = GridGenerator.SampleCurve(a, b, c, d);

            Assert.NotEmpty(samples);
            foreach (float3 p in samples)
            {
                // Doit rester sur la droite Y=0, Z=0, avec X strictement entre 0 et 100.
                Assert.True(p.x > 0f && p.x < 100f);
                Assert.Equal(0f, p.z, 3);
            }
        }

        [Fact]
        public void SampleCurve_BulgingBezier_PointsDeviateSignificantlyFromTheChord()
        {
            // Courbe qui s'écarte fortement de la corde a→d (renflement de 75 m sur 200 m
            // de long) : simule une arête de rond-point entre deux nœuds sélectionnés.
            var a = new float3(200f, 0f, 0f);
            var b = new float3(100f, 0f, 0f);
            var c = new float3(100f, 0f, 200f);
            var d = new float3(200f, 0f, 200f);

            List<float3> samples = GridGenerator.SampleCurve(a, b, c, d);

            Assert.NotEmpty(samples);
            // La corde droite a→d serait X=200 partout : au moins un point échantillonné
            // doit s'en écarter nettement (le renflement, pas une corde).
            Assert.Contains(samples, p => p.x < 170f);
            // Le point le plus proche du milieu (z≈100) doit être proche du renflement
            // maximal théorique (x=125, cf. formule du point médian d'une cubique).
            float3 middle = samples.OrderBy(p => math.abs(p.z - 100f)).First();
            Assert.True(middle.x < 150f, $"Point médian attendu proche du renflement (x<150), obtenu x={middle.x}.");
        }

        [Fact]
        public void SampleCurve_ShortEdge_NeverExceedsMaxSamples()
        {
            // Courbe très longue (garde-fou MaxCurveSamplesPerEdge) : ne doit jamais
            // produire un nombre de points disproportionné.
            var a = new float3(0f, 0f, 0f);
            var b = new float3(0f, 0f, 1000f);
            var c = new float3(2000f, 0f, 1000f);
            var d = new float3(2000f, 0f, 2000f);

            List<float3> samples = GridGenerator.SampleCurve(a, b, c, d);

            Assert.True(samples.Count <= GridGenerator.MaxCurveSamplesPerEdge);
        }

        // ------------------------------------------------------------------
        // Intégration : un polygone dont un côté est échantillonné le long d'une
        // courbe (au lieu de la corde droite entre ses deux extrémités) doit exclure
        // la zone entre la corde et la courbe — la grille générée ne doit pas y
        // déborder, contrairement à ce qui se produirait avec la corde seule.
        // ------------------------------------------------------------------

        [Fact]
        public void CurveSampledBoundary_ExcludesBulgeArea_ComparedToStraightChord()
        {
            // Carré 200×200, mais le côté droit (x=200, de z=0 à z=200) est en réalité
            // une arête de rond-point qui se renfonce jusqu'à x≈125 vers z=100 (voir
            // SampleCurve_BulgingBezier ci-dessus pour la même courbe) — comme si la
            // route courbe mordait sur ce qui serait autrement une zone constructible.
            var a = new float3(200f, 0f, 0f);
            var b = new float3(100f, 0f, 0f);
            var c = new float3(100f, 0f, 200f);
            var d = new float3(200f, 0f, 200f);
            List<float3> curveSamples = GridGenerator.SampleCurve(a, b, c, d);

            var straightChordPerimeter = new List<float3>
            {
                new float3(0f, 0f, 0f),
                new float3(200f, 0f, 0f),
                new float3(200f, 0f, 200f),
                new float3(0f, 0f, 200f),
            };
            var curveAwarePerimeter = new List<float3> { new float3(0f, 0f, 0f), a };
            curveAwarePerimeter.AddRange(curveSamples);
            curveAwarePerimeter.Add(d);
            curveAwarePerimeter.Add(new float3(0f, 0f, 200f));

            var parameters = new GridParameters
            {
                Mode = SpacingMode.FitToArea,
                Rows = 9,
                Columns = 9,
                SpacingMeters = 60f,
            };

            var straightSegments = GridGenerator.GenerateGrid(straightChordPerimeter, parameters);
            var curvedSegments = GridGenerator.GenerateGrid(curveAwarePerimeter, parameters);

            Assert.NotEmpty(straightSegments);
            Assert.NotEmpty(curvedSegments);

            // Zone du renflement (autour de z=100) : la corde droite laisse la génération
            // atteindre x proche de 200 ; le polygone qui suit la courbe doit rester
            // nettement en retrait de cette limite dans la même zone.
            float MaxXNearBulge(List<RoadSegmentDef> segments) => segments
                .SelectMany(s => new[] { s.Start, s.End })
                .Where(p => math.abs(p.z - 100f) < 20f)
                .Select(p => p.x)
                .DefaultIfEmpty(float.MinValue)
                .Max();

            float straightMaxX = MaxXNearBulge(straightSegments);
            float curvedMaxX = MaxXNearBulge(curvedSegments);

            Assert.True(curvedMaxX < straightMaxX - 30f,
                $"Le polygone qui suit la courbe devrait exclure le renflement (x max attendu nettement < {straightMaxX}), obtenu {curvedMaxX}.");
        }
    }

    // ------------------------------------------------------------------
    // Avenue (troisième niveau : colonne/rangée choisie librement, jamais de
    // cul-de-sac, rotonde optionnelle au croisement des deux) — carré 300×300,
    // grille 5×5 fixe (spacing 60), axis-aligned pour des coordonnées simples.
    // ------------------------------------------------------------------
    public class GridGeneratorAvenueTests
    {
        private static readonly List<float3> SquareNodes = new List<float3>
        {
            new float3(0f, 0f, 0f),
            new float3(300f, 0f, 0f),
            new float3(300f, 0f, 300f),
            new float3(0f, 0f, 300f),
        };

        private static GridParameters BaseParameters() => new GridParameters
        {
            Mode = SpacingMode.FixedSpacing,
            Rows = 5,
            Columns = 5,
            SpacingMeters = 60f,
        };

        [Fact]
        public void AvenueColumnEnabled_MarksOnlyThatColumnsSegmentsAsAvenue()
        {
            var parameters = BaseParameters();
            parameters.AvenueColumnEnabled = true;
            // DistributeFixed(0, 300, 60) → lignes u = [60, 120, 180, 240] ; index 2 = u=180.
            parameters.AvenueColumnIndex = 2;

            var segments = GridGenerator.GenerateGrid(SquareNodes, parameters);

            var avenueSegments = segments.Where(s => s.IsAvenue).ToList();
            Assert.NotEmpty(avenueSegments);
            foreach (var seg in avenueSegments)
            {
                // Colonne = ligne u constante → segments verticaux (X constant, non horizontaux).
                Assert.False(seg.IsHorizontal);
                Assert.Equal(180f, seg.Start.x, 1);
                Assert.Equal(180f, seg.End.x, 1);
            }
            // Aucun autre segment ne doit être marqué avenue.
            Assert.All(segments.Except(avenueSegments), s => Assert.False(s.IsAvenue));
        }

        [Fact]
        public void AvenueLine_NeverBecomesCulDeSac_EvenWhenCulDeSacModeCoversItsAxis()
        {
            var parameters = BaseParameters();
            parameters.AvenueColumnEnabled = true;
            parameters.AvenueColumnIndex = 2;
            parameters.CulDeSacMode = true;
            parameters.CulDeSacAxis = CulDeSacAxis.Columns;
            parameters.CulDeSacRatio = 100f; // toutes les colonnes seraient des culs-de-sac sans l'exception avenue
            parameters.Staggered = false;

            var segments = GridGenerator.GenerateGrid(SquareNodes, parameters);

            var avenueSegments = segments.Where(s => s.IsAvenue).ToList();
            Assert.NotEmpty(avenueSegments);
            Assert.DoesNotContain(avenueSegments, s => s.IsCulDeSacEnd);

            // Les autres colonnes, elles, sont bien devenues des culs-de-sac (le motif marche
            // toujours ailleurs — seule la colonne avenue y échappe).
            Assert.Contains(segments, s => s.IsCulDeSacEnd);
        }

        [Fact]
        public void OutOfRangeAvenueIndex_IsSilentlyANoOp()
        {
            var parameters = BaseParameters();
            parameters.AvenueColumnEnabled = true;
            parameters.AvenueColumnIndex = 999;
            parameters.AvenueRowEnabled = true;
            parameters.AvenueRowIndex = -1;

            var segments = GridGenerator.GenerateGrid(SquareNodes, parameters);

            Assert.DoesNotContain(segments, s => s.IsAvenue);
            RoadsGridGeneratorTests.AssertCommonInvariants(segments);
        }

        [Fact]
        public void BothAvenuesEnabled_ReportsRoundaboutCenterAndRadius_WithoutTrimmingTheArms()
        {
            // Comme le cercle de retournement d'un cul-de-sac : plus de rognage des bras ni de
            // boucle en arcs générée côté géométrie — les deux avenues se croisent normalement
            // en +, et seul centre/rayon (pour poser l'asset décoratif complet côté ECS) sont
            // renvoyés via RoundaboutInfo.
            var parameters = BaseParameters();
            parameters.AvenueColumnEnabled = true;
            parameters.AvenueColumnIndex = 2; // u = 180
            parameters.AvenueRowEnabled = true;
            parameters.AvenueRowIndex = 2; // v = 180
            var center = new float2(180f, 180f);
            float expectedRadius = math.min(parameters.SpacingMeters * 0.25f, 25f);

            var segments = GridGenerator.GenerateGrid(SquareNodes, parameters, out _, out RoundaboutInfo roundabout);
            var avenueSegments = segments.Where(s => s.IsAvenue).ToList();

            Assert.True(roundabout.HasRoundabout);
            Assert.Equal(center, roundabout.Center.xz);
            Assert.Equal(expectedRadius, roundabout.Radius, 2);

            // Le croisement en + normal : au moins un bras d'avenue passe exactement par le
            // centre (aucun rognage), contrairement à l'ancien comportement.
            Assert.Contains(avenueSegments, s =>
                math.distance(s.Start.xz, center) < 0.5f || math.distance(s.End.xz, center) < 0.5f);

            RoadsGridGeneratorTests.AssertCommonInvariants(segments);
        }

        [Fact]
        public void OnlyOneAvenueAxisEnabled_NeverAddsARoundabout()
        {
            var parameters = BaseParameters();
            parameters.AvenueColumnEnabled = true;
            parameters.AvenueColumnIndex = 2;
            // AvenueRowEnabled reste false : un seul axe, pas de croisement à traiter.

            var segments = GridGenerator.GenerateGrid(SquareNodes, parameters, out _, out RoundaboutInfo roundabout);
            var avenueSegments = segments.Where(s => s.IsAvenue).ToList();

            Assert.False(roundabout.HasRoundabout);
            Assert.NotEmpty(avenueSegments);
            RoadsGridGeneratorTests.AssertCommonInvariants(segments);
        }

        [Fact]
        public void OutOfRangeAvenueCrossing_ReportsNoRoundabout()
        {
            var parameters = BaseParameters();
            parameters.AvenueColumnEnabled = true;
            parameters.AvenueColumnIndex = 999;
            parameters.AvenueRowEnabled = true;
            parameters.AvenueRowIndex = -1;

            GridGenerator.GenerateGrid(SquareNodes, parameters, out _, out RoundaboutInfo roundabout);

            Assert.False(roundabout.HasRoundabout);
        }
    }

    // ------------------------------------------------------------------
    // Loop (collectrices éparses + laço interne par super-îlot) — voir
    // GridGenerator.GenerateLoopGrid/EmitLoopBlock.
    // ------------------------------------------------------------------
    public class GridGeneratorLoopGridTests
    {
        private static GridParameters LoopParams(float collectorSpacing, float culDeSacRatio = 0f) => new GridParameters
        {
            CollectorSpacingMeters = collectorSpacing,
            LoopCulDeSacRatio = culDeSacRatio,
            CulDeSacDepth = 0.75f,
        };

        private static readonly List<float3> SquareNodes300 = new List<float3>
        {
            new float3(0f, 0f, 0f),
            new float3(300f, 0f, 0f),
            new float3(300f, 0f, 300f),
            new float3(0f, 0f, 300f),
        };

        // Cellule intérieure plus grande (300×300 à l'espacement 300, contre 100×100 pour
        // SquareNodes300/100) : nécessaire pour les tests liés au cul-de-sac, dont le segment du
        // haut (voir EmitLoopBlock) doit dépasser 2×MinSegmentLength pour pouvoir être coupé en
        // deux sans que les deux moitiés ne soient éliminées comme trop courtes.
        private static readonly List<float3> SquareNodes900 = new List<float3>
        {
            new float3(0f, 0f, 0f),
            new float3(900f, 0f, 0f),
            new float3(900f, 0f, 900f),
            new float3(0f, 0f, 900f),
        };

        [Fact]
        public void DegenerateInput_FewerThanTwoNodes_ReturnsEmptyWithoutThrowing()
        {
            var segments = GridGenerator.GenerateLoopGrid(new List<float3> { new float3(0f, 0f, 0f) }, LoopParams(100f));
            Assert.Empty(segments);
        }

        [Fact]
        public void TooSmallForAnyCollector_ReturnsEmptyWithoutThrowing()
        {
            var tiny = new List<float3>
            {
                new float3(0f, 0f, 0f),
                new float3(20f, 0f, 0f),
                new float3(20f, 0f, 20f),
                new float3(0f, 0f, 20f),
            };
            var segments = GridGenerator.GenerateLoopGrid(tiny, LoopParams(100f));
            Assert.Empty(segments);
        }

        [Fact]
        public void SingleInteriorSuperblock_ProducesOneLoopWithFourArcsAndNoCulDeSac()
        {
            // 300×300, collectrices tous les 100 m -> lignes internes à 100 et 200 sur chaque
            // axe (DistributeFixed exclut les bords) -> 1 super-îlot pleinement intérieur (entre
            // 100 et 200) ET 8 bandes de bord (entre le périmètre et la collectrice la plus
            // proche, voir GenerateLoopGrid — v1 les ignorait totalement, retour en jeu :
            // "demasiados perímetros vazios sem laços") -> 3×3 = 9 super-îlots au total -> 4 arcs
            // par laço rectangulaire (EmitSimpleLoopBlock, 4 coins) -> 36 arcs.
            var segments = GridGenerator.GenerateLoopGrid(SquareNodes300, LoopParams(100f, culDeSacRatio: 0f));

            var arcs = segments.Where(s => s.IsArc).ToList();
            var culDeSacs = segments.Where(s => s.IsCulDeSacEnd).ToList();

            Assert.Equal(36, arcs.Count);
            Assert.Empty(culDeSacs);
        }

        [Fact]
        public void SingleInteriorSuperblock_FullCulDeSacRatio_AddsOneCulDeSacSpurPerBlock()
        {
            // Espacement 300 (pas 100) : le segment du haut du laço doit dépasser
            // 2×MinSegmentLength pour que la coupure en deux (voir EmitLoopBlock) laisse un
            // cul-de-sac raccordé plutôt que de le supprimer comme orphelin trop court.
            // 900×900 à l'espacement 300 -> 3×3 = 9 super-îlots (intérieur + bandes de bord,
            // voir GenerateLoopGrid) -> 9 rayons à 100% de fréquence.
            var segments = GridGenerator.GenerateLoopGrid(SquareNodes900, LoopParams(300f, culDeSacRatio: 100f));

            var culDeSacs = segments.Where(s => s.IsCulDeSacEnd).ToList();
            Assert.Equal(9, culDeSacs.Count);
        }

        [Fact]
        public void CulDeSacSpur_StartPointIsAnExactSharedVertexWithTheTopSegments()
        {
            // Bug rapporté en jeu : "os alley não estão mesmo ligados ao principal" — le rayon
            // cul-de-sac partait d'un point milieu du segment du haut, jamais l'extrémité d'AUCUN
            // segment émis (coordonnée coïncidente, mais pas un vrai sommet partagé côté jeu).
            // Corrigé en coupant le segment du haut en deux AU point de jonction exact. Ce test
            // vérifie, pour CHAQUE rayon (9, voir test précédent), que son point de départ est
            // EXACTEMENT (mêmes floats, pas juste proche) l'extrémité d'au moins un des segments
            // non-arc, non-cul-de-sac du laço.
            var segments = GridGenerator.GenerateLoopGrid(SquareNodes900, LoopParams(300f, culDeSacRatio: 100f));

            var spurs = segments.Where(s => s.IsCulDeSacEnd).ToList();
            var loopStraightPieces = segments.Where(s => !s.IsArc && !s.IsCulDeSacEnd && !s.IsAvenue).ToList();

            Assert.NotEmpty(spurs);
            foreach (RoadSegmentDef spur in spurs)
            {
                bool sharesExactVertex = loopStraightPieces.Any(s =>
                    math.all(s.Start == spur.Start) || math.all(s.End == spur.Start));

                Assert.True(sharesExactVertex,
                    $"Le point de départ du rayon ({spur.Start}) devrait être EXACTEMENT l'extrémité d'un segment droit du laço, pas une simple coïncidence de coordonnée.");
            }
        }

        [Fact]
        public void LoopConnectorAttachPoint_IsAnExactSharedVertexOfTheLoopEdge()
        {
            // Retour utilisateur en jeu : "colisões entre a via que acede ao loop e o loop" —
            // l'embranchement (EmitSimpleLoopBlock) touchait le MILIEU d'un côté du rectangle
            // du laço (uMid) sans jamais couper ce côté à cet endroit exact : deux tronçons du
            // même laço (embranchement + bord du rectangle) se croisaient sans partager de
            // sommet réel — même bug structurel que celui déjà corrigé entre collectrice et
            // laço (voir LoopLegAttachPoints_AreExactSharedVerticesWithAvenueSegments), mais À
            // L'INTÉRIEUR même du réseau du laço. Corrigé en coupant TOUJOURS le côté de
            // l'embranchement en deux, exactement au point de jonction. Ce test vérifie
            // qu'aucun point d'un tronçon droit non-avenue (laço/embranchement/cul-de-sac) ne
            // tombe au MILIEU (t strictement entre 0 et 1) d'un AUTRE tronçon droit non-avenue,
            // sans en être une extrémité exacte — un vrai croisement structurel, pas juste une
            // coïncidence de bord.
            var segments = GridGenerator.GenerateLoopGrid(SquareNodes900, LoopParams(300f, culDeSacRatio: 100f));
            var straightLoopPieces = segments.Where(s => !s.IsArc && !s.IsAvenue).ToList();
            Assert.NotEmpty(straightLoopPieces);

            for (int ti = 0; ti < straightLoopPieces.Count; ti++)
            {
                RoadSegmentDef target = straightLoopPieces[ti];
                float2 a = target.Start.xz;
                float2 ab = target.End.xz - a;
                float lenSq = math.lengthsq(ab);
                if (lenSq < 1e-4f)
                {
                    continue;
                }
                for (int oi = 0; oi < straightLoopPieces.Count; oi++)
                {
                    if (oi == ti)
                    {
                        continue;
                    }
                    RoadSegmentDef other = straightLoopPieces[oi];
                    foreach (float3 point in new[] { other.Start, other.End })
                    {
                        float t = math.dot(point.xz - a, ab) / lenSq;
                        if (t <= 0.01f || t >= 0.99f)
                        {
                            continue; // à/au-delà d'une extrémité de CETTE cible : rien à signaler ici
                        }
                        float2 projected = a + t * ab;
                        bool onThisLine = math.distance(projected, point.xz) < 0.5f;
                        Assert.False(onThisLine,
                            $"Point {point} tombe au milieu du tronçon {target.Start} -> {target.End} sans être un sommet partagé (t={t:F3}).");
                    }
                }
            }
        }

        [Fact]
        public void SuperblockMode_ZonesFillTheSelectionWithACollectorEveryThirdLine()
        {
            // Retour utilisateur : zones de taille réglable (SuperblockZoneMeters, min 100 m),
            // ajustées pour remplir exactement la sélection ("o tamanho das células adapta-se ao
            // perímetro"), avec une collectrice toutes les 3 zones ("coletor > pedonal > pedonal
            // > coletor"). Jamais de cul-de-sac ; CollectorSpacingMeters sans effet ici.
            foreach (float spacing in new[] { 200f, 400f })
            {
                // 900 m / zones de 100 m = 9 zones par axe -> lignes 100..800, collectrices à 300/600.
                var parameters = LoopParams(spacing, culDeSacRatio: 100f);
                parameters.SuperblockMode = true;
                parameters.SuperblockZoneMeters = 100f;
                var segments = GridGenerator.GenerateLoopGrid(SquareNodes900, parameters);

                Assert.DoesNotContain(segments, s => s.IsCulDeSacEnd);
                Assert.All(segments, s => Assert.True(s.IsAvenue != s.IsPedestrian, $"Chaque tronçon est soit collectrice soit piéton : {s.Start} -> {s.End}"));

                var avenueU = segments.Where(s => s.IsAvenue && !s.IsHorizontal).Select(s => (float)math.round(s.Start.x)).Distinct().OrderBy(x => x).ToList();
                var pedU = segments.Where(s => s.IsPedestrian && !s.IsHorizontal).Select(s => (float)math.round(s.Start.x)).Distinct().OrderBy(x => x).ToList();
                var avenueV = segments.Where(s => s.IsAvenue && s.IsHorizontal).Select(s => (float)math.round(s.Start.z)).Distinct().OrderBy(x => x).ToList();
                var pedV = segments.Where(s => s.IsPedestrian && s.IsHorizontal).Select(s => (float)math.round(s.Start.z)).Distinct().OrderBy(x => x).ToList();
                Assert.Equal(new[] { 300f, 600f }, avenueU);
                Assert.Equal(new[] { 300f, 600f }, avenueV);
                Assert.Equal(new[] { 100f, 200f, 400f, 500f, 700f, 800f }, pedU);
                Assert.Equal(new[] { 100f, 200f, 400f, 500f, 700f, 800f }, pedV);
            }
        }

        [Fact]
        public void SuperblockMode_ThreeZonesOrFewer_IsASingleSuperblockAdaptedToTheArea()
        {
            // Zone ≈ 1/3 de la sélection -> un seul super-quarteirão 3×3, sans collectrice
            // interne : le périmètre (route existante) le ferme. La taille réelle des zones
            // s'adapte à la zone (ici 506 m / 170 m -> 3 zones de 168,7 m).
            var square506 = new List<float3>
            {
                new float3(0f, 0f, 0f), new float3(506f, 0f, 0f), new float3(506f, 0f, 506f), new float3(0f, 0f, 506f),
            };
            var parameters = LoopParams(300f, culDeSacRatio: 100f);
            parameters.SuperblockMode = true;
            parameters.SuperblockZoneMeters = 170f;
            var segments = GridGenerator.GenerateLoopGrid(square506, parameters);

            Assert.DoesNotContain(segments, s => s.IsAvenue);
            Assert.All(segments, s => Assert.True(s.IsPedestrian));
            Assert.Equal(12, segments.Count); // 2 lignes par axe, chacune coupée en 3
            var uLines = segments.Where(s => !s.IsHorizontal).Select(s => (float)math.round(s.Start.x)).Distinct().OrderBy(x => x).ToList();
            Assert.Equal(new[] { 169f, 337f }, uLines);
        }

        [Fact]
        public void SuperblockMode_InteriorGridFormsAConsistentAlternatingSwirl()
        {
            // 900×900 -> un seul super-quarteirão, lignes internes à 300/600 sur chaque axe
            // (SuperblockSubdivisions). Chaque cellule (iu,iv) tourne horaire si iu+iv est pair,
            // antihoraire sinon (voir ApplyOneWaySwirl).
            var parameters = LoopParams(300f, culDeSacRatio: 0f);
            parameters.SuperblockMode = true;
            parameters.SuperblockZoneMeters = 300f;
            var segments = GridGenerator.GenerateLoopGrid(SquareNodes900, parameters);

            var interior = segments.Where(s => s.IsPedestrian && !s.IsArc).ToList();
            Assert.NotEmpty(interior);

            // Ligne horizontale (v=300) entre u=[0,300] : cellule (iu=0,iv=1), parité impaire =
            // antihoraire -> u décroissant.
            var segsA = interior.Where(s => s.IsHorizontal
                && math.abs(s.Start.z - 300f) < 1f && math.min(s.Start.x, s.End.x) > -1f && math.max(s.Start.x, s.End.x) < 301f).ToList();
            Assert.NotEmpty(segsA);
            Assert.All(segsA, s => Assert.True(s.Start.x > s.End.x, $"Segment A attendu u décroissant : {s.Start} -> {s.End}"));

            // Ligne horizontale (v=300) entre u=[300,600] : cellule (iu=1,iv=1), parité paire =
            // horaire -> u croissant.
            var segsB = interior.Where(s => s.IsHorizontal
                && math.abs(s.Start.z - 300f) < 1f && math.min(s.Start.x, s.End.x) > 299f && math.max(s.Start.x, s.End.x) < 601f).ToList();
            Assert.NotEmpty(segsB);
            Assert.All(segsB, s => Assert.True(s.Start.x < s.End.x, $"Segment B attendu u croissant : {s.Start} -> {s.End}"));

            // Ligne verticale (u=300) entre v=[0,300] : cellule (iu=1,iv=0), parité impaire =
            // antihoraire -> v croissant.
            var segsC = interior.Where(s => !s.IsHorizontal
                && math.abs(s.Start.x - 300f) < 1f && math.min(s.Start.z, s.End.z) > -1f && math.max(s.Start.z, s.End.z) < 301f).ToList();
            Assert.NotEmpty(segsC);
            Assert.All(segsC, s => Assert.True(s.Start.z < s.End.z, $"Segment C attendu v croissant : {s.Start} -> {s.End}"));

            // Ligne verticale (u=300) entre v=[300,600] : cellule (iu=1,iv=1), parité paire =
            // horaire -> v décroissant.
            var segsD = interior.Where(s => !s.IsHorizontal
                && math.abs(s.Start.x - 300f) < 1f && math.min(s.Start.z, s.End.z) > 299f && math.max(s.Start.z, s.End.z) < 601f).ToList();
            Assert.NotEmpty(segsD);
            Assert.All(segsD, s => Assert.True(s.Start.z > s.End.z, $"Segment D attendu v décroissant : {s.Start} -> {s.End}"));
        }

        /// <summary>
        /// Vrai si `point` (xz) tombe sur l'un des côtés du périmètre choisi (avec tolérance) —
        /// miroir du helper interne GridGenerator.IsPointOnPolygonEdge, utilisé ici pour que les
        /// tests de connectivité acceptent qu'une extrémité piétonne posée sur le VRAI périmètre
        /// n'est pas une impasse (MakeCoursePos la raccorde en jeu, voir GenerateSuperblockInterior).
        /// </summary>
        private static bool IsOnPerimeterBoundary(float3 point, IReadOnlyList<float3> perimeter, float tolerance)
        {
            float2 p = point.xz;
            int n = perimeter.Count;
            for (int i = 0; i < n; i++)
            {
                float2 a = perimeter[i].xz;
                float2 b = perimeter[(i + 1) % n].xz;
                float2 ab = b - a;
                float lenSq = math.lengthsq(ab);
                if (lenSq < 1e-6f) continue;
                float t = math.clamp(math.dot(p - a, ab) / lenSq, 0f, 1f);
                float2 projected = a + t * ab;
                if (math.distance(projected, p) < tolerance) return true;
            }
            return false;
        }

        [Fact]
        public void LoopAndSuperblock_IgnoreClassicGridOnlySettings()
        {
            // Retour utilisateur en jeu (log) : "só estão 6 áreas construtivas em vez de 9" —
            // l'avenue sur colonne/rangée N et le mode culs-de-sac du mode Grelha, restés
            // actifs, contaminaient BuildSubSegments (partagé) : une ligne piétonne devenait
            // collectrice et d'autres disparaissaient. Le résultat doit être identique avec ou
            // sans ces réglages.
            foreach (bool superblock in new[] { true, false })
            {
                var clean = LoopParams(300f);
                clean.SuperblockMode = superblock;
                var polluted = clean;
                polluted.AvenueColumnEnabled = true;
                polluted.AvenueColumnIndex = 1;
                polluted.AvenueRowEnabled = true;
                polluted.AvenueRowIndex = 0;
                polluted.CulDeSacMode = true;
                polluted.CulDeSacRatio = 100f;

                var expected = GridGenerator.GenerateLoopGrid(SquareNodes900, clean);
                var actual = GridGenerator.GenerateLoopGrid(SquareNodes900, polluted);

                Assert.Equal(expected.Count, actual.Count);
                for (int i = 0; i < expected.Count; i++)
                {
                    Assert.Equal(expected[i].Start, actual[i].Start);
                    Assert.Equal(expected[i].End, actual[i].End);
                    Assert.Equal(expected[i].IsAvenue, actual[i].IsAvenue);
                    Assert.Equal(expected[i].IsPedestrian, actual[i].IsPedestrian);
                    Assert.Equal(expected[i].IsCulDeSacEnd, actual[i].IsCulDeSacEnd);
                }
            }
        }

        [Fact]
        public void SuperblockMode_EveryInteriorEndpointIsAnExactSharedVertex_NeverADeadEnd()
        {
            // Retour utilisateur en jeu : "Ainda tem os cul de sac" — vérifie que la grille fine
            // intérieure (voir GenerateSuperblockInterior) est bien ENTIÈREMENT connectée : CHAQUE
            // extrémité d'un tronçon piéton doit être soit un sommet EXACTEMENT partagé avec un
            // autre tronçon (piéton OU collectrice), soit posée sur le VRAI périmètre choisi (que
            // MakeCoursePos raccorde en jeu, voir GenerateSuperblockInterior — retour utilisateur :
            // "as estradas pedonais também têm que se conectar à estrada que serve de base") —
            // jamais un bout libre qui apparaîtrait en jeu comme une impasse. Scénario réaliste à
            // plusieurs super-quarteirões (900×900 / 300 -> grille 3×3 de super-quarteirões,
            // chacun subdivisé en 3×3 zones) pour ne pas se limiter au cas trivial à un seul
            // super-quarteirão des tests précédents.
            var parameters = LoopParams(300f, culDeSacRatio: 0f);
            parameters.SuperblockMode = true;
            var segments = GridGenerator.GenerateLoopGrid(SquareNodes900, parameters);

            var interior = segments.Where(s => s.IsPedestrian && !s.IsArc).ToList();
            var all = segments.Where(s => !s.IsArc).ToList();
            Assert.NotEmpty(interior);

            foreach (RoadSegmentDef segment in interior)
            {
                foreach (float3 endpoint in new[] { segment.Start, segment.End })
                {
                    bool sharedWithAnother = all.Any(other =>
                        !(math.all(other.Start == segment.Start) && math.all(other.End == segment.End)) // pas le même tronçon
                        && (math.all(other.Start == endpoint) || math.all(other.End == endpoint)));
                    bool onPerimeter = IsOnPerimeterBoundary(endpoint, SquareNodes900, 0.5f);
                    Assert.True(sharedWithAnother || onPerimeter,
                        $"Extrémité {endpoint} du tronçon piéton {segment.Start} -> {segment.End} n'est ni partagée ni sur le périmètre — impasse.");
                }
            }
        }

        [Fact]
        public void SuperblockMode_IrregularPerimeter_NeverLeavesADeadEndPedestrianSegment()
        {
            // Retour utilisateur en jeu, répété : "Ainda tem os cul de sac... retira por
            // completo" — un périmètre IRRÉGULIER peut faire clipper une ligne interne en plein
            // milieu par le vrai contour (ClipLineToPolygon), pas seulement à ses 4 coins —
            // exactement le genre de cas que PruneDeadEndPedestrianSegments doit rattraper. Même
            // octogone bruité que IrregularRealPerimeter_... (non-régression Loop), réutilisé ici
            // pour Superblock : si la purge fonctionne, AUCUN tronçon piéton restant ne doit avoir
            // une extrémité qui ne soit ni partagée ni sur le vrai périmètre, quel que soit
            // l'espacement de collectrice essayé.
            var rnd = new System.Random(7);
            var perimeter = new List<float3>();
            const int nodeCount = 10;
            for (int i = 0; i < nodeCount; i++)
            {
                float angle = i * 2f * math.PI / nodeCount;
                float r = 250f * (1f + (float)(rnd.NextDouble() - 0.5) * 0.5f);
                perimeter.Add(new float3(r * math.cos(angle), 0f, r * math.sin(angle)));
            }

            foreach (float spacing in new[] { 200f, 250f, 300f })
            {
                var parameters = LoopParams(spacing, culDeSacRatio: 0f);
                parameters.SuperblockMode = true;
                var segments = GridGenerator.GenerateLoopGrid(perimeter, parameters);

                var interior = segments.Where(s => s.IsPedestrian && !s.IsArc).ToList();
                var all = segments.Where(s => !s.IsArc).ToList();

                foreach (RoadSegmentDef segment in interior)
                {
                    foreach (float3 endpoint in new[] { segment.Start, segment.End })
                    {
                        bool sharedWithAnother = all.Any(other =>
                            !(math.all(other.Start == segment.Start) && math.all(other.End == segment.End))
                            && (math.all(other.Start == endpoint) || math.all(other.End == endpoint)));
                        bool onPerimeter = IsOnPerimeterBoundary(endpoint, perimeter, 0.5f);
                        Assert.True(sharedWithAnother || onPerimeter,
                            $"Extrémité {endpoint} du tronçon piéton {segment.Start} -> {segment.End} (spacing={spacing}) n'est ni partagée ni sur le périmètre — impasse.");
                    }
                }
            }
        }

        [Fact]
        public void CollectorSegments_AreMarkedAvenue_LoopAndSpurAreNot()
        {
            // 3 réseaux distincts (voir RoadSegmentDef.IsAvenue/IsCulDeSacEnd) : collectrices
            // éparses -> réseau avenue, laço -> réseau principal, rayon cul-de-sac -> réseau
            // secondaire. Voir GenerateLoopGrid. 9 super-îlots (intérieur + bandes de bord).
            var segments = GridGenerator.GenerateLoopGrid(SquareNodes900, LoopParams(300f, culDeSacRatio: 100f));

            var spur = segments.Where(s => s.IsCulDeSacEnd).ToList();
            var collectors = segments.Where(s => !s.IsCulDeSacEnd && s.IsAvenue).ToList();
            var loopPieces = segments.Where(s => !s.IsCulDeSacEnd && !s.IsAvenue).ToList();

            Assert.NotEmpty(collectors);
            Assert.NotEmpty(loopPieces);
            Assert.Equal(9, spur.Count);
            Assert.All(spur, s => Assert.False(s.IsAvenue, $"Rayon cul-de-sac attendu IsAvenue=false (réseau secondaire) : {s.Start} -> {s.End}"));
            // Le laço (loopPieces) doit contenir 4 arcs par super-îlot (9 x 4 = 36).
            Assert.Equal(36, loopPieces.Count(s => s.IsArc));
        }

        [Fact]
        public void ArcTangents_AreNeverBackwardsAlongTheChord()
        {
            // Même garde-fou que pour l'ancien mode Adaptativo (voir historique du mod) :
            // une tangente d'arc qui pointe à l'opposé du sens de parcours produit une
            // courbe aberrante côté jeu ("Forma inválida"). Ici les tangentes sont FIXES
            // par construction ((0,1)/(1,0) en repère local, jamais dérivées d'un nuage de
            // points), donc ce test est une garantie de non-régression plutôt qu'une
            // découverte, mais reste peu coûteux à vérifier.
            var segments = GridGenerator.GenerateLoopGrid(SquareNodes300, LoopParams(100f));
            var arcs = segments.Where(s => s.IsArc).ToList();
            Assert.NotEmpty(arcs);

            foreach (RoadSegmentDef arc in arcs)
            {
                float2 chord = math.normalize(arc.End.xz - arc.Start.xz);
                float startDot = math.dot(math.normalize(arc.StartTangent.xz), chord);
                float endDot = math.dot(math.normalize(arc.EndTangent.xz), chord);
                Assert.True(startDot > 0.3f, $"Tangente de départ quasi retournée : {arc.Start} -> {arc.End}, dot={startDot:F2}");
                Assert.True(endDot > 0.3f, $"Tangente d'arrivée quasi retournée : {arc.Start} -> {arc.End}, dot={endDot:F2}");
            }
        }

        [Fact]
        public void MirroredRow_ArcsAndCulDeSacSpurAreValid()
        {
            // 1800x1800 (5 lignes internes par axe à l'espacement 300 -> iu ET iv vont de 0 à 3)
            // : garantit qu'au moins un bloc a iv impair (mirrorV=true, voir GenerateLoopGrid —
            // alternance introduite pour "laços des deux côtés de la collectrice"). Un rectangle
            // dont l'axe court n'a qu'une seule ligne interne aurait iv TOUJOURS 0 et ne
            // testerait jamais mirrorV=true.
            var square1800 = new List<float3>
            {
                new float3(0f, 0f, 0f),
                new float3(1800f, 0f, 0f),
                new float3(1800f, 0f, 1800f),
                new float3(0f, 0f, 1800f),
            };
            var segments = GridGenerator.GenerateLoopGrid(square1800, LoopParams(300f, culDeSacRatio: 100f));

            var arcs = segments.Where(s => s.IsArc).ToList();
            var spurs = segments.Where(s => s.IsCulDeSacEnd).ToList();

            Assert.Equal(144, arcs.Count); // 36 blocs (6x6, intérieur + bandes de bord) x 4 arcs
            Assert.Equal(36, spurs.Count); // 1 par bloc

            foreach (RoadSegmentDef arc in arcs)
            {
                float2 chord = math.normalize(arc.End.xz - arc.Start.xz);
                float startDot = math.dot(math.normalize(arc.StartTangent.xz), chord);
                float endDot = math.dot(math.normalize(arc.EndTangent.xz), chord);
                Assert.True(startDot > 0.3f, $"Tangente de départ quasi retournée : {arc.Start} -> {arc.End}, dot={startDot:F2}");
                Assert.True(endDot > 0.3f, $"Tangente d'arrivée quasi retournée : {arc.Start} -> {arc.End}, dot={endDot:F2}");
            }

            var loopStraightPieces = segments.Where(s => !s.IsArc && !s.IsCulDeSacEnd && !s.IsAvenue).ToList();
            foreach (RoadSegmentDef spur in spurs)
            {
                bool sharesExactVertex = loopStraightPieces.Any(s => math.all(s.Start == spur.Start) || math.all(s.End == spur.Start));
                Assert.True(sharesExactVertex, $"Rayon cul-de-sac ({spur.Start}) pas connecté à un sommet exact du laço.");
            }
        }

        [Fact]
        public void LoopLegAttachPoints_AreExactSharedVerticesWithAvenueSegments()
        {
            // Bug rapporté en jeu : "a estrada principal (laço) não fusiona na coletora" — les
            // jambes du laço (EmitLoopBlock) s'accrochent à la collectrice en retrait de
            // `margin` de ses bords, donc AU MILIEU de son tracé, jamais à ses extrémités —
            // sans division de la collectrice à ce point exact (voir
            // SplitAvenuesAtLoopAttachPoints), les deux se touchent sans jamais partager de
            // sommet réel. Ce test vérifie que TOUT point d'un tronçon non-avenue qui tombe sur
            // la LIGNE d'un tronçon avenue (à l'intérieur de son tracé) est EXACTEMENT l'une des
            // deux extrémités de ce tronçon avenue — jamais un point de milieu.
            var square1800 = new List<float3>
            {
                new float3(0f, 0f, 0f),
                new float3(1800f, 0f, 0f),
                new float3(1800f, 0f, 1800f),
                new float3(0f, 0f, 1800f),
            };
            var segments = GridGenerator.GenerateLoopGrid(square1800, LoopParams(300f, culDeSacRatio: 100f));

            var avenues = segments.Where(s => s.IsAvenue).ToList();
            var nonAvenues = segments.Where(s => !s.IsAvenue).ToList();
            Assert.NotEmpty(avenues);
            Assert.NotEmpty(nonAvenues);

            foreach (RoadSegmentDef nonAvenue in nonAvenues)
            {
                foreach (float3 point in new[] { nonAvenue.Start, nonAvenue.End })
                {
                    foreach (RoadSegmentDef avenue in avenues)
                    {
                        float2 a = avenue.Start.xz;
                        float2 ab = avenue.End.xz - a;
                        float lenSq = math.lengthsq(ab);
                        if (lenSq < 1e-4f)
                        {
                            continue;
                        }
                        float t = math.dot(point.xz - a, ab) / lenSq;
                        if (t <= 0.01f || t >= 0.99f)
                        {
                            continue; // à/au-delà d'une extrémité de CETTE avenue : rien à signaler ici
                        }
                        float2 projected = a + t * ab;
                        bool onThisAvenueLine = math.distance(projected, point.xz) < 0.5f;
                        Assert.False(onThisAvenueLine,
                            $"Point {point} tombe au milieu de l'avenue {avenue.Start} -> {avenue.End} sans être un sommet partagé (t={t:F3}).");
                    }
                }
            }
        }

        /// <summary>Vrai si a/b (droits) sont colinéaires ET si b.Start ou b.End tombe strictement à l'intérieur de a (chevauchement de tracé, pas un simple croisement).</summary>
        private static bool SegmentsOverlapCollinearly(RoadSegmentDef a, RoadSegmentDef b)
        {
            float2 aDir = math.normalize(a.End.xz - a.Start.xz);
            float2 bDir = math.normalize(b.End.xz - b.Start.xz);
            if (math.abs(aDir.x * bDir.y - aDir.y * bDir.x) > 0.01f)
            {
                return false; // pas colinéaires
            }
            float2 aA = a.Start.xz;
            float2 aAB = a.End.xz - aA;
            float aLenSq = math.lengthsq(aAB);
            if (aLenSq < 1e-4f)
            {
                return false;
            }
            foreach (float3 p3 in new[] { b.Start, b.End })
            {
                float t = math.dot(p3.xz - aA, aAB) / aLenSq;
                float2 proj = aA + t * aAB;
                if (t > 0.02f && t < 0.98f && math.distance(proj, p3.xz) < 0.3f)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>Vrai si a/b (droits, niveaux DIFFÉRENTS) se croisent à un point qui n'est l'extrémité d'AUCUN des deux — jonction manquante.</summary>
        private static bool SegmentsCrossWithoutSharedJunction(RoadSegmentDef a, RoadSegmentDef b)
        {
            float2 a1 = a.Start.xz, a2 = a.End.xz, b1 = b.Start.xz, b2 = b.End.xz;
            float2 r1 = a2 - a1, r2 = b2 - b1;
            float denom = r1.x * r2.y - r1.y * r2.x;
            if (math.abs(denom) < 1e-6f)
            {
                return false;
            }
            float t = ((b1.x - a1.x) * r2.y - (b1.y - a1.y) * r2.x) / denom;
            float u = ((b1.x - a1.x) * r1.y - (b1.y - a1.y) * r1.x) / denom;
            return t > 0.02f && t < 0.98f && u > 0.02f && u < 0.98f;
        }

        [Fact]
        public void MultipleSuperblocks_EachInteriorCellGetsExactlyOneLoop()
        {
            // 500×500, collectrices tous les 100 m -> lignes internes à 100/200/300/400 sur
            // chaque axe -> 3×3 = 9 super-îlots pleinement intérieurs + bandes de bord (voir
            // GenerateLoopGrid) -> 5×5 = 25 super-îlots au total -> 4 arcs par laço rectangulaire
            // (EmitSimpleLoopBlock, 4 coins) -> 100 arcs.
            var square500 = new List<float3>
            {
                new float3(0f, 0f, 0f),
                new float3(500f, 0f, 0f),
                new float3(500f, 0f, 500f),
                new float3(0f, 0f, 500f),
            };
            var segments = GridGenerator.GenerateLoopGrid(square500, LoopParams(100f));
            var arcs = segments.Where(s => s.IsArc).ToList();
            Assert.Equal(100, arcs.Count);
        }

        [Fact]
        public void NarrowCell_CulDeSacIsSkippedRatherThanForcedIntoAnInvalidShape()
        {
            // Bande 100×900 : BuildLocalFrame aligne u sur l'arête la PLUS LONGUE (les côtés de
            // 900), donc sans rotation, u=900 (largeur du laço, pas de problème) et v=100
            // (profondeur, vPositions vide -> rien émis du tout, ne teste pas ce qu'on veut).
            // AngleOffsetDegrees=90° tourne u de 90°, ce qui échange effectivement les deux axes :
            // u=100 (largeur) et v=900 (profondeur, assez pour plusieurs bandes). DistributeFixed
            // (0,100,300) donne cellCount=round(100/300)=0<=1 -> aucune collectrice interne sur u
            // -> uBounds=[0,100] (une seule bande couvrant toute la largeur) -> largeur utile après
            // marge (leftU=25, rightU=75) = 50m, sous MinCulDeSacCellWidth (60m). Le cul-de-sac
            // doit être ignoré ici plutôt que forcé dans un espace trop étroit (retour utilisateur
            // en jeu : "não é obrigatório haver becos sem saída em espaços menos largos" — la
            // ramification collée aux deux arcs presque jointifs produisait une forme
            // dégénérée/invalide).
            var narrowStrip = new List<float3>
            {
                new float3(0f, 0f, 0f),
                new float3(100f, 0f, 0f),
                new float3(100f, 0f, 900f),
                new float3(0f, 0f, 900f),
            };
            GridParameters parameters = LoopParams(300f, culDeSacRatio: 100f);
            parameters.AngleOffsetDegrees = 90f;
            var segments = GridGenerator.GenerateLoopGrid(narrowStrip, parameters);
            Assert.Empty(segments.Where(s => s.IsCulDeSacEnd));
            // Le laço lui-même doit quand même exister, juste sans ramification.
            Assert.NotEmpty(segments.Where(s => s.IsArc));
        }

        [Fact]
        public void IrregularRealPerimeter_NeverThrowsAndKeepsAllSegmentsInsidePerimeter()
        {
            // Périmètre réel irrégulier capturé en jeu (voir l'incident "Forma inválida" du
            // mode Adaptativo) : le test le plus utile n'est pas une forme synthétique
            // parfaite, mais une géométrie réelle bruitée. Ici un octogone irrégulier
            // (rayons variables) sert de test de non-régression pour Loop.
            var rnd = new System.Random(7);
            var perimeter = new List<float3>();
            const int nodeCount = 10;
            for (int i = 0; i < nodeCount; i++)
            {
                float angle = i * 2f * math.PI / nodeCount;
                float r = 250f * (1f + (float)(rnd.NextDouble() - 0.5) * 0.5f);
                perimeter.Add(new float3(r * math.cos(angle), 0f, r * math.sin(angle)));
            }

            foreach (float spacing in new[] { 60f, 90f, 120f, 180f })
            {
                var segments = GridGenerator.GenerateLoopGrid(perimeter, LoopParams(spacing, culDeSacRatio: 50f));
                // Ne doit jamais lever d'exception (déjà garanti par l'exécution du test),
                // et chaque segment doit rester géométriquement raisonnable (longueur non
                // nulle, pas de NaN).
                foreach (RoadSegmentDef s in segments)
                {
                    Assert.False(float.IsNaN(s.Start.x) || float.IsNaN(s.Start.z) || float.IsNaN(s.End.x) || float.IsNaN(s.End.z),
                        $"Segment NaN (spacing={spacing}) : {s.Start} -> {s.End}");
                    Assert.True(math.distance(s.Start.xz, s.End.xz) >= GridGenerator.MinSegmentLength - 0.01f,
                        $"Segment plus court que MinSegmentLength (spacing={spacing}) : {s.Start} -> {s.End}");
                }
            }
        }

        [Fact]
        public void ChamferedCorner_StillGetsADepthAdaptedLoop_AnchoredToTheCollector()
        {
            // Carré 900×900 avec un coin coupé en biais (chanfrein) : le super-îlot le plus
            // proche du coin coupé échoue le test "4 coins intérieurs" strict. Plutôt que de
            // laisser ce coin de terrain entièrement vide (bug rapporté en jeu), un laço à
            // PROFONDEUR ADAPTÉE mais LARGEUR PLEINE doit y apparaître (voir
            // TryFitDepthPreservingWidth — plus de rétrécissement uniforme largeur+profondeur,
            // qui produisait des laços visiblement miniatures), toujours ancré à la collectrice
            // (vMin fixe, jamais rétréci).
            var chamfered = new List<float3>
            {
                new float3(0f, 0f, 0f),
                new float3(900f, 0f, 0f),
                new float3(900f, 0f, 750f),
                new float3(750f, 0f, 900f),
                new float3(0f, 0f, 900f),
            };

            var segments = GridGenerator.GenerateLoopGrid(chamfered, LoopParams(300f, culDeSacRatio: 0f));

            Assert.NotEmpty(segments);
            var arcs = segments.Where(s => s.IsArc).ToList();
            Assert.True(arcs.Count >= 2, $"Attendu au moins 1 laço (2 arcs), obtenu {arcs.Count} arc(s).");

            // Contrôle géométrique DIRECT plutôt qu'une comparaison de décompte d'arcs avec le
            // carré plein : depuis l'ajout des bandes de bord (voir GenerateLoopGrid), le coin
            // chanfreiné peut désormais recevoir un laço à profondeur adaptée là où, avant cet
            // ajout, il n'y avait même pas de super-îlot candidat à cet endroit — le nombre total
            // d'arcs peut donc rester identique au carré plein (l'adaptation ne change jamais le
            // nombre d'arcs émis, seulement la profondeur du laço), ce qui ne prouve plus rien
            // par comparaison de décompte. Le vrai contrat à vérifier : aucun segment ne dépasse
            // la ligne du chanfrein (x+z <= 1650, la diagonale coupée entre (900,750) et
            // (750,900)) — petite tolérance pour l'arrondi en virgule flottante (JoinTolerance de
            // GridGenerator est privé, pas accessible ici).
            const float tolerance = 1f;
            foreach (RoadSegmentDef s in segments)
            {
                Assert.True(s.Start.x + s.Start.z <= 1650f + tolerance,
                    $"Segment déborde du chanfrein (Start) : {s.Start}");
                Assert.True(s.End.x + s.End.z <= 1650f + tolerance,
                    $"Segment déborde du chanfrein (End) : {s.End}");
            }

            foreach (RoadSegmentDef s in segments)
            {
                Assert.False(float.IsNaN(s.Start.x) || float.IsNaN(s.Start.z) || float.IsNaN(s.End.x) || float.IsNaN(s.End.z),
                    $"Segment NaN : {s.Start} -> {s.End}");
            }
        }

        [Fact]
        public void ChamferedCorner_LoopLegsAdaptIndependently_GivingADiagonalBackSegment()
        {
            // Même chanfrein que le test précédent, mais vérifie la vraie nouveauté (retour
            // utilisateur en jeu avec capture d'écran : "a forma mais parecida com a área da
            // célula", une jambe raccourcie par une coupe en diagonale, PAS les deux jambes
            // rétrécies à la même profondeur) : la cellule d'angle (600-900 en u ET v, coupée par
            // x+z<=1650) doit donner une jambe côté u=900 clairement plus courte que côté u=600
            // (plus proche du coin coupé), avec un segment du fond qui suit cette diagonale au
            // lieu de rester horizontal comme ailleurs dans la grille.
            var chamfered = new List<float3>
            {
                new float3(0f, 0f, 0f),
                new float3(900f, 0f, 0f),
                new float3(900f, 0f, 750f),
                new float3(750f, 0f, 900f),
                new float3(0f, 0f, 900f),
            };

            var segments = GridGenerator.GenerateLoopGrid(chamfered, LoopParams(300f, culDeSacRatio: 0f));

            // Segments "fond" candidats : droits, pas une collectrice/laço marqué autrement —
            // angle=0 donc u=x, v=z, la comparaison directe sur .z est valide sans transformer de
            // repère local.
            var backSegments = segments.Where(s => !s.IsArc && !s.IsAvenue && !s.IsCulDeSacEnd).ToList();
            Assert.NotEmpty(backSegments);

            float maxZDelta = backSegments.Max(s => math.abs(s.Start.z - s.End.z));
            Assert.True(maxZDelta > 5f,
                $"Aucun segment du fond n'est diagonal (plus grand écart en z trouvé : {maxZDelta:F2}m) — la jambe côté chanfrein devrait être plus courte que l'autre, donnant un fond incliné plutôt qu'horizontal partout.");

            // Toujours à l'intérieur du chanfrein, même vérification que le test précédent —
            // s'assure que l'adaptation par jambe n'a pas réintroduit de débordement.
            const float tolerance = 1f;
            foreach (RoadSegmentDef s in segments)
            {
                Assert.True(s.Start.x + s.Start.z <= 1650f + tolerance, $"Segment déborde du chanfrein (Start) : {s.Start}");
                Assert.True(s.End.x + s.End.z <= 1650f + tolerance, $"Segment déborde du chanfrein (End) : {s.End}");
            }
        }

    }

    /// <summary>
    /// Régression de performance — retour utilisateur (forum du mod) : "the mod lags hard the
    /// more rows and columns there are... BuildSubSegments runs at O(n^4) complexity and gets
    /// executed every single frame" avec le défaut Spacing=10m. Vérifié fondé : BuildSubSegments
    /// balayait TOUS les points déjà acceptés à chaque nouveau croisement (O(n²) dans une
    /// double boucle déjà O(n²)), et GridRoadToolSystem.OnUpdate régénère bien l'aperçu à CHAQUE
    /// frame tant que ≥2 nœuds sont sélectionnés — un petit espacement sur un grand périmètre se
    /// traduisait en gel du jeu. Remplacé par SpatialPointIndex (grille de baldes, voisinage
    /// 3×3) — ce test verrouille un budget temps généreux mais strict pour empêcher toute
    /// régression future vers un comportement O(n⁴).
    /// </summary>
    public class GridGeneratorPerformanceTests
    {
        [Fact]
        public void LargeAreaWithSmallSpacing_GeneratesWellUnderOneSecond()
        {
            // ~450x450m avec Spacing=10m (le défaut historique visé par le retour utilisateur)
            // -> ~45x45 lignes, ~2000 croisements : négligeable avec un index spatial,
            // plusieurs dizaines de millions d'opérations avec l'ancien balayage linéaire.
            var perimeter = new List<float3>
            {
                new float3(0f, 0f, 0f),
                new float3(450f, 0f, 0f),
                new float3(450f, 0f, 450f),
                new float3(0f, 0f, 450f),
            };
            var parameters = GridParameters.Default;
            parameters.Mode = SpacingMode.FixedSpacing;
            parameters.SpacingMeters = 10f;

            var stopwatch = Stopwatch.StartNew();
            var segments = GridGenerator.GenerateGrid(perimeter, parameters);
            stopwatch.Stop();

            Assert.NotEmpty(segments);
            Assert.True(stopwatch.ElapsedMilliseconds < 1000,
                $"GenerateGrid a pris {stopwatch.ElapsedMilliseconds}ms pour un espacement de 10m sur 450x450m — " +
                "régression vers une complexité quadratique/pire dans la fusion des croisements probable.");
        }

        [Fact]
        public void VeryLargeAreaWithSmallSpacing_StillGeneratesQuickly()
        {
            // Cas extrême : ~200x200 lignes, ~40 000 croisements — scénario exact décrit sur le
            // forum du mod ("the mod lags hard the more rows and columns there are"). L'ancien
            // balayage linéaire (O(n²) DANS une boucle déjà O(n²)) aurait fait des dizaines de
            // millions d'opérations ici ; SpatialPointIndex garde ça quasi linéaire.
            var perimeter = new List<float3>
            {
                new float3(0f, 0f, 0f),
                new float3(2000f, 0f, 0f),
                new float3(2000f, 0f, 2000f),
                new float3(0f, 0f, 2000f),
            };
            var parameters = GridParameters.Default;
            parameters.Mode = SpacingMode.FixedSpacing;
            parameters.SpacingMeters = 10f;

            var stopwatch = Stopwatch.StartNew();
            var segments = GridGenerator.GenerateGrid(perimeter, parameters);
            stopwatch.Stop();

            Assert.NotEmpty(segments);
            Assert.True(stopwatch.ElapsedMilliseconds < 5000,
                $"GenerateGrid a pris {stopwatch.ElapsedMilliseconds}ms pour un espacement de 10m sur 2000x2000m " +
                $"({segments.Count} segments) — ce scénario est celui exact rapporté comme gelant le jeu.");
        }

        [Fact]
        public void LoopMode_LargeAreaWithManyBlocks_GeneratesQuickly()
        {
            // Retour utilisateur (log de performance en jeu, ~31 nœuds sélectionnés, modo Loop) :
            // 325-351ms par régénération complète, contre <1ms pour le modo Grid équivalent.
            // Cause : SplitSegmentsAtMidSpanAttachPoints (O(cibles × points d'ancrage)) sans
            // index spatial, alors même appelée plusieurs fois par régénération avec l'ancienne
            // hiérarchie à 2 niveaux (Arterial > Coletora, depuis supprimée — voir GridGenerator.
            // GenerateLoopGrid). ~2000x2000m / CollectorSpacingMeters=200 (le minimum autorisé
            // par le panneau) -> ~100 quarteirões, un seul appel global à
            // SplitSegmentsAtMidSpanAttachPoints sur TOUS leurs points d'ancrage.
            var perimeter = new List<float3>
            {
                new float3(0f, 0f, 0f),
                new float3(2000f, 0f, 0f),
                new float3(2000f, 0f, 2000f),
                new float3(0f, 0f, 2000f),
            };
            var parameters = new GridParameters
            {
                CollectorSpacingMeters = 200f,
                LoopCulDeSacRatio = 50f,
                CulDeSacDepth = 0.75f,
            };

            var stopwatch = Stopwatch.StartNew();
            var segments = GridGenerator.GenerateLoopGrid(perimeter, parameters);
            stopwatch.Stop();

            Assert.NotEmpty(segments);
            Assert.True(stopwatch.ElapsedMilliseconds < 2000,
                $"GenerateLoopGrid a pris {stopwatch.ElapsedMilliseconds}ms pour ~49 pâtés de maison sur 2000x2000m " +
                $"({segments.Count} segments) — régression probable dans SplitSegmentsAtMidSpanAttachPoints.");
        }

    public class GridAlignToTerrainTests
    {
        [Fact]
        public void AlignedGrid_HasStreetsAlongTheContours()
        {
            // Pente de 10 % vers le nord-est (45°) : les courbes de niveau vont nord-ouest ↔ sud-est.
            var square = new List<float3> { new float3(0, 0, 0), new float3(800, 0, 0), new float3(800, 0, 800), new float3(0, 0, 800) };
            GridParameters p = GridParameters.Default;
            p.HeightAt = q => 0.1f * (q.x + q.y) / math.SQRT2;
            p.AlignToTerrain = true;
            var segments = GridGenerator.GenerateGrid(square, p);
            Assert.NotEmpty(segments);
            float2 contour = math.normalize(new float2(1f, -1f));
            int along = segments.Count(s => math.abs(math.dot(math.normalizesafe(s.End.xz - s.Start.xz), contour)) > 0.99f);
            int across = segments.Count(s => math.abs(math.dot(math.normalizesafe(s.End.xz - s.Start.xz), contour)) < 0.01f);
            Assert.True(along > 0 && along + across == segments.Count, $"{along} le long, {across} en travers sur {segments.Count}");
        }

        [Fact]
        public void FlatArea_KeepsTheUsualOrientation()
        {
            var square = new List<float3> { new float3(0, 0, 0), new float3(800, 0, 0), new float3(800, 0, 800), new float3(0, 0, 800) };
            GridParameters p = GridParameters.Default;
            p.HeightAt = q => 5f;
            p.AlignToTerrain = true;
            var aligned = GridGenerator.GenerateGrid(square, p);
            p.AlignToTerrain = false;
            var usual = GridGenerator.GenerateGrid(square, p);
            Assert.Equal(usual.Count, aligned.Count);
        }
    }
    }
}
