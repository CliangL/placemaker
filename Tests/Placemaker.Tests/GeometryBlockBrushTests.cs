// BlockBrush ("Block paint" mode): a brush fenced to one city block, avoiding obstacles (owner: geometry).

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Placemaker.Core.Geometry;
using Unity.Mathematics;
using Xunit;

namespace Placemaker.Tests
{
    public class GeometryBlockBrushTests
    {
        // A 200 × 120 m block (curb outline), corner at the origin.
        private static readonly float2[] Block = { new float2(0, 0), new float2(200, 0), new float2(200, 120), new float2(0, 120) };

        private static float2[] Rect(float x0, float z0, float x1, float z1) =>
            new[] { new float2(x0, z0), new float2(x1, z0), new float2(x1, z1), new float2(x0, z1) };

        private static BrushMask Paint(BlockBrush bb)
        {
            var m = new BrushMask(bb.CellSize);
            bb.Attach(m);
            return m;
        }

        private static float OverlapArea(float2[] ring, float2[] other) =>
            Poly.Intersection(ring, other).Sum(r => r.Area);

        private static void AssertInsideBlock(float2[] ring, float2[] block)
        {
            foreach (var p in ring)
                Assert.True(Poly.Contains(block, p) || Poly.DistanceToBoundary(block, p) < 0.05f, $"{p} outside the block");
        }

        [Fact]
        public void Stamps_NeverLeaveTheBlock()
        {
            var bb = new BlockBrush(Block);
            var m = Paint(bb);
            // Cursor drifts over the road below the block and beyond its right end.
            m.Stamp(new float2(100, -10), 40f, false);
            m.Stroke(new float2(100, -10), new float2(260, 60), 40f, false);
            Assert.False(m.IsEmpty);
            foreach (var (a, b) in m.FillRuns(1))
            {
                float2 c = (a + b) * 0.5f;
                Assert.True(Poly.Contains(Block, c), $"cell at {c} outside the block");
            }
            // Erasing is never fenced.
            m.Stamp(new float2(150, 30), 500f, true);
            Assert.True(m.IsEmpty);
        }

        [Fact]
        public void Limit_WithAnotherCellSizeIsIgnored()
        {
            var bb = new BlockBrush(Block, null, 4f);
            var m = new BrushMask(2f);
            bb.Attach(m);
            Assert.Null(m.Limit);
            m.Stamp(new float2(-50, -50), 10f, false);
            Assert.False(m.IsEmpty);
        }

        [Fact]
        public void Outline_HugsTheCurbExactly()
        {
            var bb = new BlockBrush(Block);
            var m = Paint(bb);
            m.Stamp(new float2(60, 15), 35f, false);           // reaches over the bottom curb (z = 0)
            m.Stroke(new float2(60, 15), new float2(120, 15), 35f, false);
            var o = bb.Outline(m);
            Assert.NotNull(o);
            G.AssertSimpleCcw(o.Ring);
            AssertInsideBlock(o.Ring, Block);
            Assert.True(o.TouchesCurb);
            // The bottom edge lies exactly on the curb line over (nearly) the whole painted width.
            var onCurb = o.Ring.Where(p => math.abs(p.y) < 0.01f).Select(p => p.x).ToList();
            Assert.True(onCurb.Count >= 2, G.Dump(o.Ring));
            Assert.True(onCurb.Max() - onCurb.Min() > 100f, $"curb frontage only {onCurb.Max() - onCurb.Min():F1} m: {G.Dump(o.Ring)}");
            Assert.True(SurfaceAcceptance.IsAccepted(o.Ring));
        }

        [Fact]
        public void Outline_SmallGapToTheCurbIsClosed()
        {
            var bb = new BlockBrush(Block);
            var m = Paint(bb);
            // Brush edge stops ~3 m short of the left curb (x = 0): close enough to mean "up to the road".
            m.Stamp(new float2(41, 60), 40f, false);
            var o = bb.Outline(m);
            Assert.NotNull(o);
            Assert.True(o.Ring.Count(p => math.abs(p.x) < 0.01f) >= 2, G.Dump(o.Ring));
        }

        [Fact]
        public void Outline_FreeEdgesKeepTheBrushSize()
        {
            var bb = new BlockBrush(Block);
            var m = Paint(bb);
            m.Stamp(new float2(100, 60), 30f, false);         // well away from every curb
            var o = bb.Outline(m);
            Assert.NotNull(o);
            G.AssertSimpleCcw(o.Ring);
            Assert.False(o.TouchesCurb);
            float circle = math.PI * 30f * 30f;
            Assert.InRange(Poly.Area(o.Ring), circle * 0.9f, circle * 1.08f);
        }

        [Fact]
        public void ZonedCells_AreAvoidedAndTheEdgeSnapsToThem()
        {
            // Zone grid along the bottom road: 25 cells of 8 m, 6 deep (48 m), as single-cell obstacles.
            var cells = new List<float2[]>();
            for (int i = 0; i < 25; i++)
                for (int d = 0; d < 6; d++)
                    cells.Add(Rect(i * 8, d * 8, i * 8 + 8, d * 8 + 8));
            var bb = new BlockBrush(Block, cells);
            var m = Paint(bb);
            m.Stamp(new float2(100, 60), 45f, false);         // overlaps the zoned strip below z = 48
            var o = bb.Outline(m);
            Assert.NotNull(o);
            G.AssertSimpleCcw(o.Ring);
            var zone = Rect(0, 0, 200, 48);
            Assert.True(OverlapArea(o.Ring, zone) < 0.5f, $"overlaps zoning by {OverlapArea(o.Ring, zone):F1} m²");
            // The cut follows the zone line exactly.
            Assert.True(o.Ring.Count(p => math.abs(p.y - 48f) < 0.01f) >= 2, G.Dump(o.Ring));
            Assert.True(SurfaceAcceptance.IsAccepted(o.Ring));
        }

        [Fact]
        public void EnclosedObstacle_IsCutOutWithoutAHole()
        {
            var building = Rect(90, 50, 110, 70);
            var bb = new BlockBrush(Block, new[] { building });
            var m = Paint(bb);
            m.Stamp(new float2(100, 60), 40f, false);
            Assert.False(m.IsPaintedAt(new float2(100, 60)));
            var o = bb.Outline(m);
            Assert.NotNull(o);
            Assert.True(o.OpenedAroundObstacle);
            G.AssertSimpleCcw(o.Ring);
            Assert.True(OverlapArea(o.Ring, building) < 0.5f);
            float expected = math.PI * 40f * 40f - 400f;
            Assert.InRange(Poly.Area(o.Ring), expected * 0.8f, expected * 1.08f);
        }

        [Fact]
        public void PaintedGaps_AreFilledLikePaintMode()
        {
            var bb = new BlockBrush(Block);
            var m = Paint(bb);
            m.Stamp(new float2(100, 60), 45f, false);
            m.Stamp(new float2(100, 60), 10f, true);           // an erased spot inside: not an obstacle
            var o = bb.Outline(m);
            Assert.NotNull(o);
            Assert.False(o.OpenedAroundObstacle);
            Assert.True(Poly.Contains(o.Ring, new float2(100, 60)));
        }

        [Fact]
        public void ObstacleSplit_KeepsTheLargestPiece()
        {
            // A lot cuts the block from curb to curb; paint on both sides.
            var lot = Rect(95, -5, 105, 125);
            var bb = new BlockBrush(Block, new[] { lot });
            var m = Paint(bb);
            m.Stamp(new float2(60, 60), 30f, false);
            m.Stroke(new float2(60, 60), new float2(140, 60), 30f, false);
            m.Stamp(new float2(150, 60), 40f, false);
            var o = bb.Outline(m);
            Assert.NotNull(o);
            G.AssertSimpleCcw(o.Ring);
            Assert.True(OverlapArea(o.Ring, lot) < 0.5f);
            Assert.True(o.Ring.All(p => p.x >= 104.9f) || o.Ring.All(p => p.x <= 95.1f));
        }

        [Fact]
        public void NarrowUnpaintedStripsFarAway_AreNotSweptIn()
        {
            // Two lots with a 4 m alley between them, far from the paint.
            var a = Rect(150, 10, 170, 110);
            var b = Rect(174, 10, 194, 110);
            var bb = new BlockBrush(Block, new[] { a, b });
            var m = Paint(bb);
            m.Stamp(new float2(50, 60), 30f, false);
            var o = bb.Outline(m);
            Assert.NotNull(o);
            Assert.True(o.Ring.All(p => p.x < 100f));
        }

        [Fact]
        public void BlockedPoints_AreNotPainted()
        {
            var bb = new BlockBrush(Block, null, BrushMask.DefaultCellSize, p => p.x > 150f);   // water on the right
            var m = Paint(bb);
            m.Stamp(new float2(150, 60), 50f, false);
            Assert.False(m.IsPaintedAt(new float2(170, 60)));
            Assert.True(m.IsPaintedAt(new float2(130, 60)));
        }

        [Fact]
        public void FullyBlockedBlock_HasNothingToPaint()
        {
            var bb = new BlockBrush(Block, new[] { Rect(-10, -10, 210, 130) });
            Assert.True(bb.Full);
            var m = Paint(bb);
            m.Stamp(new float2(100, 60), 80f, false);
            Assert.True(m.IsEmpty);
            Assert.Null(bb.Outline(m));
        }

        [Fact]
        public void BadInput_DoesNotThrow()
        {
            var bb = new BlockBrush(new[] { new float2(0, 0), new float2(1, 0) }, new[] { new[] { new float2(float.NaN, 0), new float2(1, 1), new float2(0, 1) } });
            Assert.True(bb.Full);
            Assert.Null(bb.Outline(new BrushMask()));
            Assert.Null(bb.Outline(null));
        }

        [Fact]
        public void IrregularBlock_WithManyObstacles_StaysSimpleAndFast()
        {
            // Curved-ish block (octagon-like, 300 m across) with a ring of zoned cells along every side.
            var block = new List<float2>();
            for (int i = 0; i < 24; i++)
            {
                float a = i * 2f * math.PI / 24f;
                block.Add(new float2(150 + 150 * math.cos(a), 150 + 120 * math.sin(a)));
            }
            var obstacles = new List<float2[]>();
            for (int i = 0; i < 24; i++)
            {
                float2 p = block[i], q = block[(i + 1) % 24];
                float2 dir = math.normalize(q - p), inward = new float2(-dir.y, dir.x);
                int n = (int)(math.distance(p, q) / 8f);
                for (int k = 0; k < n; k++)
                {
                    float2 s = p + dir * (k * 8f);
                    obstacles.Add(new[] { s, s + dir * 8f, s + dir * 8f + inward * 16f, s + inward * 16f });
                }
            }
            var sw = Stopwatch.StartNew();
            var bb = new BlockBrush(block, obstacles);
            var m = Paint(bb);
            m.Stamp(new float2(150, 150), 60f, false);
            m.Stroke(new float2(150, 150), new float2(260, 160), 60f, false);
            var o = bb.Outline(m);
            sw.Stop();
            Assert.NotNull(o);
            G.AssertSimpleCcw(o.Ring);
            AssertInsideBlock(o.Ring, block.ToArray());
            foreach (var ob in obstacles) Assert.True(OverlapArea(o.Ring, ob) < 0.5f);
            Assert.True(sw.ElapsedMilliseconds < 3000, $"took {sw.ElapsedMilliseconds} ms");

            // Re-outlining (what happens every few frames while painting) reuses the cached clip regions.
            sw.Restart();
            m.Stroke(new float2(260, 160), new float2(200, 220), 40f, false);
            Assert.NotNull(bb.Outline(m));
            sw.Stop();
            Assert.True(sw.ElapsedMilliseconds < 1500, $"re-outline took {sw.ElapsedMilliseconds} ms");
        }

        [Fact]
        public void Trim_RemovesPaintOutsideANewFence()
        {
            var m = new BrushMask();
            m.Stamp(new float2(100, 60), 80f, false);         // unfenced paint over the whole block and beyond
            var bb = new BlockBrush(Block, new[] { Rect(0, 0, 200, 40) });
            Assert.True(bb.Trim(m) > 0);
            foreach (var (a, b) in m.FillRuns(1))
            {
                float2 c = (a + b) * 0.5f;
                Assert.True(bb.CanPaintAt(c), $"cell at {c} not paintable");
            }
        }

        [Fact]
        public void BlockPaintMode_RoundTripsAndComparesFaces()
        {
            Assert.Equal("blockPaint", Placemaker.Core.Tool.SelectionModes.Id(Placemaker.Core.Tool.SelectionMode.BlockPaint));
            Assert.True(Placemaker.Core.Tool.SelectionModes.TryParse("blockPaint", out var mode));
            Assert.Equal(Placemaker.Core.Tool.SelectionMode.BlockPaint, mode);
            Assert.Equal(Placemaker.Core.AreaSource.Painted, Placemaker.Core.Tool.SelectionModes.Source(mode));

            var a = new List<Placemaker.Core.Tool.HalfEdge> { new(1, 10, 11), new(2, 11, 12), new(3, 12, 10) };
            var rotated = new List<Placemaker.Core.Tool.HalfEdge> { a[1], a[2], a[0] };
            var other = new List<Placemaker.Core.Tool.HalfEdge> { a[0].Reversed, a[1], a[2] };
            Assert.True(Placemaker.Core.Tool.BlockPaintState.SameFace(a, rotated));
            Assert.False(Placemaker.Core.Tool.BlockPaintState.SameFace(a, other));
            Assert.False(Placemaker.Core.Tool.BlockPaintState.SameFace(a, new List<Placemaker.Core.Tool.HalfEdge>()));
        }
    }
}
