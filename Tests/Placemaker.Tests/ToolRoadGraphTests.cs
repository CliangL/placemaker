using System.Collections.Generic;
using Placemaker.Core.Geometry;
using Placemaker.Core.Tool;
using Unity.Mathematics;
using Xunit;

namespace Placemaker.Tests
{
    public class ToolRoadGraphTests
    {
        /// <summary>Grid of n×n nodes spaced 100 m, node id = x + 10*z + 1, edge ids unique.</summary>
        private sealed class GridGraph : IRoadGraph
        {
            public readonly Dictionary<long, float2> Pos = new Dictionary<long, float2>();
            private readonly Dictionary<long, List<RoadLink>> _links = new Dictionary<long, List<RoadLink>>();
            private long _nextEdge = 1000;
            public readonly Dictionary<(long, long), long> EdgeOf = new Dictionary<(long, long), long>();

            public GridGraph(int n)
            {
                for (int z = 0; z < n; z++)
                    for (int x = 0; x < n; x++)
                        Pos[Id(x, z)] = new float2(x * 100, z * 100);
                for (int z = 0; z < n; z++)
                    for (int x = 0; x < n; x++)
                    {
                        if (x + 1 < n) Connect(Id(x, z), Id(x + 1, z));
                        if (z + 1 < n) Connect(Id(x, z), Id(x, z + 1));
                    }
            }

            public static long Id(int x, int z) => x + 10 * z + 1;

            public long Connect(long a, long b)
            {
                long e = _nextEdge++;
                Add(a, b, e);
                Add(b, a, e);
                EdgeOf[(a, b)] = e; EdgeOf[(b, a)] = e;
                return e;
            }

            public void AddNode(long id, float2 p) => Pos[id] = p;

            private void Add(long from, long to, long e)
            {
                if (!_links.TryGetValue(from, out var l)) _links[from] = l = new List<RoadLink>();
                var d = Pos[to] - Pos[from];
                l.Add(new RoadLink { Edge = e, Other = to, Direction = math.normalize(d), Length = math.length(d) });
            }

            public IReadOnlyList<RoadLink> Links(long node) => _links.TryGetValue(node, out var l) ? l : new List<RoadLink>();

            public float2[] Ring(List<HalfEdge> loop)
            {
                var r = new float2[loop.Count];
                for (int i = 0; i < loop.Count; i++) r[i] = Pos[loop[i].From];
                return r;
            }
        }

        [Fact]
        public void TracesTheBlockOnTheLeftSide()
        {
            var g = new GridGraph(3);
            // Edge (1,1)->(2,1) eastwards: block on the left is the upper-right one (x 100..200, z 100..200).
            long a = GridGraph.Id(1, 1), b = GridGraph.Id(2, 1);
            var loop = new List<HalfEdge>();
            Assert.Equal(FaceResult.Ok, RoadGraphAlgorithms.TraceLeftFace(g, new HalfEdge(g.EdgeOf[(a, b)], a, b), 100, loop));
            Assert.Equal(4, loop.Count);
            var ring = g.Ring(loop);
            Assert.True(Poly.SignedArea(ring) > 0);
            Assert.True(Poly.Contains(ring, new float2(150, 150)));
            // Reverse direction gives the block below instead.
            Assert.Equal(FaceResult.Ok, RoadGraphAlgorithms.TraceLeftFace(g, new HalfEdge(g.EdgeOf[(a, b)], b, a), 100, loop));
            Assert.True(Poly.Contains(g.Ring(loop), new float2(150, 50)));
        }

        [Fact]
        public void OuterFaceIsClockwise()
        {
            var g = new GridGraph(2);
            long a = GridGraph.Id(0, 0), b = GridGraph.Id(1, 0);
            var loop = new List<HalfEdge>();
            // Walking west along the bottom edge keeps the OUTSIDE on the left.
            Assert.Equal(FaceResult.Ok, RoadGraphAlgorithms.TraceLeftFace(g, new HalfEdge(g.EdgeOf[(a, b)], b, a), 100, loop));
            Assert.True(Poly.SignedArea(g.Ring(loop)) < 0);
        }

        [Fact]
        public void DeadEndSpurInsideBlockIsRemoved()
        {
            var g = new GridGraph(2);
            g.AddNode(99, new float2(50, 50));
            g.Connect(GridGraph.Id(0, 0), 99);   // cul-de-sac into the block
            long a = GridGraph.Id(0, 0), b = GridGraph.Id(1, 0);
            var loop = new List<HalfEdge>();
            Assert.Equal(FaceResult.Ok, RoadGraphAlgorithms.TraceLeftFace(g, new HalfEdge(g.EdgeOf[(a, b)], a, b), 100, loop));
            Assert.Equal(4, loop.Count);
            foreach (var h in loop) Assert.NotEqual(99, h.To);
        }

        [Fact]
        public void TooLargeFaceStopsAtStepLimit()
        {
            var g = new GridGraph(3);
            long a = GridGraph.Id(0, 0), b = GridGraph.Id(1, 0);
            var loop = new List<HalfEdge>();
            Assert.Equal(FaceResult.TooLarge, RoadGraphAlgorithms.TraceLeftFace(g, new HalfEdge(g.EdgeOf[(a, b)], b, a), 3, loop));
        }

        [Fact]
        public void ShortestPathFollowsGrid()
        {
            var g = new GridGraph(4);
            var path = new List<HalfEdge>();
            Assert.True(RoadGraphAlgorithms.ShortestPath(g, GridGraph.Id(0, 0), GridGraph.Id(3, 2), 1000, path));
            Assert.Equal(5, path.Count);
            Assert.Equal(GridGraph.Id(0, 0), path[0].From);
            Assert.Equal(GridGraph.Id(3, 2), path[path.Count - 1].To);
            for (int i = 1; i < path.Count; i++) Assert.Equal(path[i - 1].To, path[i].From);
            Assert.False(RoadGraphAlgorithms.ShortestPath(g, GridGraph.Id(0, 0), 12345, 1000, path));
        }

        [Fact]
        public void UnionOfAdjacentBlocksIsOneCycle()
        {
            var g = new GridGraph(3);
            var f1 = new List<HalfEdge>();
            var f2 = new List<HalfEdge>();
            RoadGraphAlgorithms.TraceLeftFace(g, Half(g, 0, 0, 1, 0), 100, f1);   // block x 0..100
            RoadGraphAlgorithms.TraceLeftFace(g, Half(g, 1, 0, 2, 0), 100, f2);   // block x 100..200
            Assert.True(RoadGraphAlgorithms.SharesEdge(f1, f2));
            var cycle = new List<HalfEdge>();
            Assert.True(RoadGraphAlgorithms.UnionBoundary(new[] { f1, f2 }, cycle));
            Assert.Equal(6, cycle.Count);
            Assert.Equal(20000f, Poly.Area(g.Ring(cycle)), 1);
        }

        [Fact]
        public void UnionTouchingAtCornerOnlyIsRefused()
        {
            var g = new GridGraph(3);
            var f1 = new List<HalfEdge>();
            var f2 = new List<HalfEdge>();
            RoadGraphAlgorithms.TraceLeftFace(g, Half(g, 0, 0, 1, 0), 100, f1);
            RoadGraphAlgorithms.TraceLeftFace(g, Half(g, 1, 1, 2, 1), 100, f2);
            Assert.False(RoadGraphAlgorithms.SharesEdge(f1, f2));
            Assert.False(RoadGraphAlgorithms.UnionBoundary(new[] { f1, f2 }, new List<HalfEdge>()));
        }

        [Fact]
        public void UnionEnclosingAHoleIsRefused()
        {
            var g = new GridGraph(4);
            var faces = new List<List<HalfEdge>>();
            for (int z = 0; z < 3; z++)
                for (int x = 0; x < 3; x++)
                {
                    if (x == 1 && z == 1) continue;   // leave the centre block out
                    var f = new List<HalfEdge>();
                    RoadGraphAlgorithms.TraceLeftFace(g, Half(g, x, z, x + 1, z), 100, f);
                    faces.Add(f);
                }
            Assert.False(RoadGraphAlgorithms.UnionBoundary(faces, new List<HalfEdge>()));
        }

        [Fact]
        public void ClockwiseAngleOrdersTurns()
        {
            var south = new float2(0, -1);
            Assert.True(RoadGraphAlgorithms.ClockwiseAngle(south, new float2(-1, 0)) < RoadGraphAlgorithms.ClockwiseAngle(south, new float2(0, 1)));
            Assert.True(RoadGraphAlgorithms.ClockwiseAngle(south, new float2(0, 1)) < RoadGraphAlgorithms.ClockwiseAngle(south, new float2(1, 0)));
            Assert.True(RoadGraphAlgorithms.ClockwiseAngle(south, south) > 6f);
        }

        [Fact]
        public void StitchDropsDuplicateJoints()
        {
            var ring = RoadGraphAlgorithms.Stitch(new IReadOnlyList<float3>[]
            {
                new[] { new float3(0, 0, 0), new float3(10, 0, 0) },
                new[] { new float3(10, 0, 0), new float3(10, 0, 10) },
                new[] { new float3(10, 0, 10), new float3(0, 0, 0) },
            });
            Assert.Equal(3, ring.Count);
        }

        private static HalfEdge Half(GridGraph g, int x0, int z0, int x1, int z1)
        {
            long a = GridGraph.Id(x0, z0), b = GridGraph.Id(x1, z1);
            return new HalfEdge(g.EdgeOf[(a, b)], a, b);
        }
    }
}
