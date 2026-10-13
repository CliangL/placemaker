using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Placemaker.Core.Layers.Roads.Generators;
using Unity.Mathematics;
using Xunit;

namespace Placemaker.Tests
{
    public class RoadsOrganicGeneratorTests
    {
        private static GridParameters Organic(float block = 80f, float curviness = 60f, int seed = 1, float loops = 30f)
        {
            GridParameters p = GridParameters.Default;
            p.OrganicMode = true;
            p.OrganicStreetSpacing = block;
            p.OrganicCurviness = curviness;
            p.OrganicSeed = seed;
            p.OrganicLoopShare = loops;
            return p;
        }

        private static List<float3> Rectangle(float width, float height)
        {
            return new List<float3> { new float3(0, 0, 0), new float3(width, 0, 0), new float3(width, 0, height), new float3(0, 0, height) };
        }

        private static List<float3> LoadRealPerimeter()
        {
            string path = Path.Combine(RoadsTestUtil.FixtureDir(), "real-perimeter-backtrack.txt");
            return File.ReadAllLines(path).Where(l => l.Trim().Length > 0).Select(l => l.Trim().Split(' '))
                .Select(q => new float3(float.Parse(q[0], CultureInfo.InvariantCulture), 0f, float.Parse(q[1], CultureInfo.InvariantCulture))).ToList();
        }

        private static float DistanceToPolygon(float2 p, List<float3> polygon)
        {
            float best = float.MaxValue;
            for (int i = 0; i < polygon.Count; i++)
            {
                float2 a = polygon[i].xz, b = polygon[(i + 1) % polygon.Count].xz, ab = b - a;
                float t = math.saturate(math.dot(p - a, ab) / math.lengthsq(ab));
                best = math.min(best, math.distance(p, a + t * ab));
            }
            return best;
        }

        private static bool Inside(float2 p, List<float3> polygon)
        {
            bool inside = false;
            for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
            {
                float2 a = polygon[i].xz, b = polygon[j].xz;
                if ((a.y > p.y) != (b.y > p.y) && p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x) inside = !inside;
            }
            return inside;
        }

        private static bool Cross(float2 a1, float2 a2, float2 b1, float2 b2)
        {
            float Orient(float2 a, float2 b, float2 c) => (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
            float d1 = Orient(b1, b2, a1), d2 = Orient(b1, b2, a2), d3 = Orient(a1, a2, b1), d4 = Orient(a1, a2, b2);
            return ((d1 > 1e-2f && d2 < -1e-2f) || (d1 < -1e-2f && d2 > 1e-2f)) && ((d3 > 1e-2f && d4 < -1e-2f) || (d3 < -1e-2f && d4 > 1e-2f));
        }

        private static void AssertWellFormed(List<RoadSegmentDef> segments, List<float3> perimeter, string label)
        {
            Assert.True(segments.Count >= 10, $"{label} : {segments.Count} tronçons seulement");
            var curves = segments.Select(s => RoadsConcentricGeneratorTests.GameCurve(s, 24)).ToList();
            var nodes = new Dictionary<float3, List<float2>>(); // tangente sortante de chaque tronçon, par nœud
            for (int k = 0; k < segments.Count; k++)
            {
                RoadSegmentDef s = segments[k];
                float2[] c = curves[k];
                float length = 0f;
                for (int i = 1; i < c.Length; i++) length += math.distance(c[i - 1], c[i]);
                Assert.True(length >= 34f, $"{label} : tronçon de {length:F0} m en {s.Start.xz}");
                bool startOn = DistanceToPolygon(s.Start.xz, perimeter) < 0.5f, endOn = DistanceToPolygon(s.End.xz, perimeter) < 0.5f;
                foreach (float3 q in new[] { s.Start, s.End })
                {
                    float d = DistanceToPolygon(q.xz, perimeter);
                    Assert.True(d < 0.5f || d >= 24f, $"{label} : nœud à {d:F1} m du périmètre en {q.xz}");
                }
                float along = 0f;
                for (int i = 0; i < c.Length; i++)
                {
                    if (i > 0) along += math.distance(c[i - 1], c[i]);
                    Assert.True(Inside(c[i], perimeter) || DistanceToPolygon(c[i], perimeter) < 0.5f, $"{label} : tronçon hors de la forme en {c[i]}");
                    // Hors de la zone d'approche d'un bout sur le périmètre (25 / sin 50° + marge).
                    if ((startOn && along < 42f) || (endOn && length - along < 42f)) continue;
                    Assert.True(DistanceToPolygon(c[i], perimeter) >= 23f, $"{label} : rue à {DistanceToPolygon(c[i], perimeter):F1} m du périmètre en {c[i]}");
                }
                void AddTangent(float3 n, float2 t)
                {
                    if (!nodes.TryGetValue(n, out var list)) nodes[n] = list = new List<float2>();
                    list.Add(math.normalize(t));
                }
                AddTangent(s.Start, c[1] - c[0]);
                AddTangent(s.End, c[c.Length - 2] - c[c.Length - 1]);
            }
            // Angles aux carrefours (tangentes réelles des courbes).
            foreach (var pair in nodes)
            {
                for (int i = 0; i < pair.Value.Count; i++)
                    for (int j = i + 1; j < pair.Value.Count; j++)
                    {
                        float angle = math.degrees(math.acos(math.clamp(math.dot(pair.Value[i], pair.Value[j]), -1f, 1f)));
                        Assert.True(angle >= 38f, $"{label} : angle de {angle:F0}° au nœud {pair.Key.xz}");
                    }
            }
            // Pas de croisement (courbes réelles, hors extrémités partagées).
            var boxes = curves.Select(c => (min: c.Aggregate(new float2(float.MaxValue), math.min), max: c.Aggregate(new float2(float.MinValue), math.max))).ToList();
            for (int a = 0; a < curves.Count; a++)
                for (int b = a + 1; b < curves.Count; b++)
                {
                    if (math.any(boxes[a].max < boxes[b].min) || math.any(boxes[b].max < boxes[a].min)) continue;
                    for (int i = 1; i < curves[a].Length; i++)
                        for (int j = 1; j < curves[b].Length; j++)
                            Assert.False(Cross(curves[a][i - 1], curves[a][i], curves[b][j - 1], curves[b][j]),
                                $"{label} : croisement près de {curves[a][i]}");
                }
            // Bouts libres : seulement des entrées (sur le périmètre) ou des bouts d'impasse, eux-mêmes
            // loin du périmètre et des autres rues (place du cercle de retournement).
            var tips = new HashSet<float3>(segments.Where(s => s.IsCulDeSacEnd).Select(s => s.End));
            Assert.NotEmpty(tips);
            foreach (var pair in nodes.Where(n => n.Value.Count == 1))
            {
                bool entry = DistanceToPolygon(pair.Key.xz, perimeter) < 0.5f;
                Assert.True(entry || tips.Contains(pair.Key), $"{label} : bout libre qui n'est pas une impasse en {pair.Key.xz}");
            }
            foreach (float3 tip in tips)
            {
                Assert.Single(nodes[tip]);
                Assert.True(DistanceToPolygon(tip.xz, perimeter) >= 29f, $"{label} : impasse à {DistanceToPolygon(tip.xz, perimeter):F0} m du périmètre en {tip.xz}");
                for (int k = 0; k < segments.Count; k++)
                {
                    if (segments[k].Start.Equals(tip) || segments[k].End.Equals(tip)) continue;
                    float d = curves[k].Min(q => math.distance(q, tip.xz));
                    Assert.True(d >= 38f, $"{label} : bout d'impasse à {d:F0} m d'une autre rue en {tip.xz}");
                }
            }
            // Deux réseaux : la rue principale (avenue) et toutes les autres rues (cul-de-sac, IsLocal).
            Assert.All(segments, s => Assert.True(s.IsAvenue != s.IsLocal, $"{label} : tronçon ni avenue ni cul-de-sac en {s.Start.xz}"));
            // Tout relié aux entrées.
            var adjacency = new Dictionary<float3, List<float3>>();
            foreach (RoadSegmentDef s in segments)
            {
                if (!adjacency.TryGetValue(s.Start, out var a)) adjacency[s.Start] = a = new List<float3>();
                if (!adjacency.TryGetValue(s.End, out var b)) adjacency[s.End] = b = new List<float3>();
                a.Add(s.End);
                b.Add(s.Start);
            }
            var entries = adjacency.Keys.Where(n => DistanceToPolygon(n.xz, perimeter) < 0.5f).ToList();
            Assert.True(entries.Count >= 2, $"{label} : {entries.Count} entrée(s)");
            var seen = new HashSet<float3>();
            var stack = new Stack<float3>(entries);
            while (stack.Count > 0)
            {
                float3 n = stack.Pop();
                if (!seen.Add(n)) continue;
                foreach (float3 m in adjacency[n]) stack.Push(m);
            }
            Assert.True(seen.Count == adjacency.Count, $"{label} : {adjacency.Count - seen.Count} nœud(s) non reliés aux entrées");
        }

        [Fact]
        public void Rectangle_IsWellFormed()
        {
            var perimeter = Rectangle(900f, 700f);
            foreach (int seed in new[] { 1, 2, 3 })
            {
                AssertWellFormed(GridGenerator.GenerateGrid(perimeter, Organic(seed: seed)), perimeter, $"rectangle graine {seed}");
            }
        }

        [Fact]
        public void RealPerimeter_IsWellFormed()
        {
            var perimeter = LoadRealPerimeter();
            foreach ((GridParameters p, string label) in new[] { (Organic(), "défaut"), (Organic(block: 60f, curviness: 100f, seed: 7, loops: 60f), "serré, très courbe"),
                         (Organic(block: 140f, curviness: 0f, seed: 42, loops: 0f), "large, droit"), (Organic(curviness: 100f, seed: 99), "courbe 99") })
            {
                AssertWellFormed(GridGenerator.GenerateGrid(perimeter, p), perimeter, label);
            }
        }

        [Fact]
        public void RealPerimeter_ManySeeds_AreWellFormed()
        {
            // Motif aléatoire : les garanties doivent tenir pour toutes les variantes, pas seulement une.
            var perimeter = LoadRealPerimeter();
            foreach (float spacing in new[] { 60f, 80f, 110f })
            {
                for (int seed = 10; seed < 22; seed++)
                {
                    AssertWellFormed(GridGenerator.GenerateGrid(perimeter, Organic(block: spacing, curviness: 80f, seed: seed, loops: 40f)), perimeter,
                        $"{spacing} m graine {seed}");
                }
            }
        }

        [Fact]
        public void SameSeed_SameResult_OtherSeed_Different()
        {
            var perimeter = LoadRealPerimeter();
            var a = GridGenerator.GenerateGrid(perimeter, Organic(seed: 5, block: 81f));
            var b = GridGenerator.GenerateGrid(perimeter, Organic(seed: 5, block: 81f));
            var c = GridGenerator.GenerateGrid(perimeter, Organic(seed: 6, block: 81f));
            Assert.Equal(a.Select(s => (s.Start, s.End)), b.Select(s => (s.Start, s.End)));
            Assert.NotEqual(a.Select(s => (s.Start, s.End)), c.Select(s => (s.Start, s.End)));
        }

        [Fact]
        public void RealPerimeter_GeneratesQuickly()
        {
            var perimeter = LoadRealPerimeter();
            GridGenerator.GenerateGrid(perimeter, Organic(seed: 50));
            // Placemaker: generators no longer keep static result/distance-field caches (thread safety);
            // re-plans of an unchanged input are cached by the preview worker instead. Budgets are per
            // uncached generation, Debug build, with headroom for the parallel test run.
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < 5; i++)
            {
                GridGenerator.GenerateGrid(perimeter, Organic(seed: 51 + i)); // graines différentes : hors cache
            }
            stopwatch.Stop();
            Assert.True(stopwatch.ElapsedMilliseconds < 1500, $"5 générations en {stopwatch.ElapsedMilliseconds} ms");
        }
    }
}
