using System.Collections.Generic;
using System.Linq;
using Placemaker.Core.Suite;
using Unity.Mathematics;
using Xunit;

namespace Placemaker.Tests.Suite
{
    public class SuiteLayoutTests
    {
        [Fact]
        public void Roads_ShareExactJunctionPoints()
        {
            var l = new SuiteLayout();
            var roads = l.Roads();
            Assert.Equal((l.NX + 1) * l.NZ + (l.NZ + 1) * l.NX, roads.Count);
            // every junction is hit by the same float2 from each segment that ends there (nodes merge on exact equality)
            var ends = new Dictionary<float2, int>();
            foreach (var r in roads) { ends[r.A] = ends.TryGetValue(r.A, out int a) ? a + 1 : 1; ends[r.B] = ends.TryGetValue(r.B, out int b) ? b + 1 : 1; }
            Assert.Equal((l.NX + 1) * (l.NZ + 1), ends.Count);
            Assert.True(ends[new float2(l.LineX(4), l.LineZ(3))] == 4);
        }

        [Fact]
        public void Blocks_HaveEveryKind_AndMixedStreets()
        {
            var l = new SuiteLayout();
            var blocks = l.Blocks();
            Assert.Equal(l.NX * l.NZ, blocks.Count);
            foreach (SuiteBlockKind k in System.Enum.GetValues(typeof(SuiteBlockKind))) Assert.Contains(blocks, b => b.Kind == k);
            Assert.Contains(blocks, b => b.West == SuiteLayout.PedestrianStreet || b.East == SuiteLayout.PedestrianStreet);
            Assert.Contains(blocks, b => b.South == SuiteLayout.MediumRoad || b.West == SuiteLayout.MediumRoad);
        }

        [Fact]
        public void Heightmap_FlatSlopeAndRough()
        {
            var l = new SuiteLayout();
            Assert.Equal(SuiteLayout.BaseHeight, l.Height(l.LineX(5) + 40, l.LineZ(2) + 50), 3);
            // slope: 6 % north of LineZ(5), level roads at the start line
            Assert.Equal(SuiteLayout.BaseHeight + 0.06f * 70f, l.Height(100f, l.LineZ(5) + 70f), 3);
            Assert.Equal(SuiteLayout.BaseHeight, l.Height(100f, l.LineZ(5)), 3);
            // rough: bumps inside, level on the road lines
            Assert.Equal(SuiteLayout.BaseHeight, l.Height(l.LineX(1), l.LineZ(4) + 60f), 3);
            float max = 0f;
            for (float x = l.LineX(0); x < l.LineX(3); x += 3f)
                for (float z = l.LineZ(4); z < l.LineZ(5); z += 3f) max = math.max(max, math.abs(l.Height(x, z) - SuiteLayout.BaseHeight));
            Assert.InRange(max, 2.5f, SuiteLayout.RoughAmplitude + 0.01f);
        }

        [Fact]
        public void Heightmap_TexelsMatchHeightFunction()
        {
            var l = new SuiteLayout();
            var data = l.Heightmap();
            int n = SuiteLayout.HeightmapSize;
            foreach (var (px, py) in new[] { (2000, 2000), (1900, 2200), (1880, 2040), (2100, 2100) })
            {
                var w = SuiteLayout.TexelWorld(px, py);
                float h = data[py * n + px] / 65535f * SuiteLayout.HeightScale;
                Assert.InRange(math.abs(l.Height(w.x, w.y) - h), 0f, 0.07f);
            }
            Assert.Equal(SuiteLayout.Encode(SuiteLayout.BaseHeight), data[0]);
            Assert.True(SuiteLayout.BaseHeight > 511.7f + 25f);
        }

        [Fact]
        public void Pits_CountOnlyBelowBothGroundAndPlan()
        {
            var at = new List<float2> { new float2(0, 0), new float2(1, 0), new float2(2, 0) };
            var before = new List<float> { 10f, 10f, 10f };
            var planned = new List<float> { 10f, 7f, 10f };   // a planned 3 m dig at sample 1
            var after = new List<float> { 10f, 7f, 6f };      // sample 2: a 4 m unplanned pit
            var near = new List<bool> { false, false, true };
            var r = SuiteVerdicts.Pits(at, before, after, planned, near);
            Assert.Equal(1, r.Cells);
            Assert.Equal(1, r.CellsNearRoads);
            Assert.Equal(4f, r.MaxDepth, 3);
        }

        [Fact]
        public void CropNames()
        {
            Assert.True(SuiteVerdicts.IsCropLike("Agriculture Lot - Field"));
            Assert.True(SuiteVerdicts.IsCropLike("Agriculture Surface 01"));
            Assert.False(SuiteVerdicts.IsCropLike("Placemaker Area"));
            Assert.True(SuiteVerdicts.IsFence("Wooden Fence 01"));
        }

        [Fact]
        public void FrameStats_P95()
        {
            var ms = Enumerable.Range(1, 100).Select(i => (float)i).ToList();
            var (max, p95, n) = SuiteVerdicts.FrameStats(ms);
            Assert.Equal(100f, max);
            Assert.Equal(95f, p95);
            Assert.Equal(100, n);
        }
    }
}
