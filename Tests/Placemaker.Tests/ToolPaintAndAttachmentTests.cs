using System.Collections.Generic;
using Placemaker.Core;
using Placemaker.Core.Geometry;
using Placemaker.Core.Tool;
using Unity.Mathematics;
using Xunit;

namespace Placemaker.Tests
{
    public class ToolPaintAndAttachmentTests
    {
        [Fact]
        public void RoundStampOutlineIsSimpleAndRoughlyCircular()
        {
            var m = new PaintMask();
            m.Stamp(new float2(100, 100), 40, false);
            Assert.InRange(m.Area, 4300, 5800);   // π·40² ≈ 5027 on a 4 m grid
            var ring = m.Outline();
            Assert.True(ring.Count >= 8);
            Assert.True(Poly.IsSimple(ring));
            Assert.True(Poly.SignedArea(ring) > 0);
            Assert.InRange(Poly.Area(ring), 4000, 6000);
        }

        [Fact]
        public void EraseAndHolesAreFilled()
        {
            var m = new PaintMask();
            m.Stamp(new float2(0, 0), 60, false, square: true);
            m.Stamp(new float2(0, 0), 15, true);   // hole in the middle
            Assert.False(m.IsPaintedAt(new float2(1, 1)));
            var ring = m.Outline();
            Assert.True(Poly.Contains(ring, new float2(1, 1)));   // the hole is part of the outline
            Assert.True(Poly.IsSimple(ring));
        }

        [Fact]
        public void LargestIslandWins()
        {
            var m = new PaintMask();
            m.Stamp(new float2(0, 0), 50, false);
            m.Stamp(new float2(500, 0), 10, false);
            var ring = m.Outline();
            Assert.True(Poly.Contains(ring, new float2(0, 0)));
            Assert.False(Poly.Contains(ring, new float2(500, 0)));
        }

        [Fact]
        public void RotatedSquareStamp()
        {
            var m = new PaintMask();
            m.Stamp(new float2(0, 0), 40, false, square: true, angleDegrees: 45);
            Assert.True(m.IsPaintedAt(new float2(0, 50)));    // diagonal of a 45° square reaches ~56 m
            Assert.False(m.IsPaintedAt(new float2(38, 38)));
        }

        [Fact]
        public void SnapshotRestoreRebuildsBoundary()
        {
            var m = new PaintMask();
            m.Stroke(new float2(0, 0), new float2(200, 0), 20, false);
            var snap = m.Save();
            var boundary = m.RawBoundary().Count;
            m.Clear();
            Assert.True(m.IsEmpty);
            m.Restore(snap);
            Assert.Equal(boundary, m.RawBoundary().Count);
            Assert.Equal(snap.Count, m.CellCount);
        }

        [Fact]
        public void EmptyMaskHasNoOutline()
        {
            Assert.Empty(new PaintMask().Outline());
        }

        private static NearbyNet Straight(long id, float3 a, float3 d, float width = 8) => new NearbyNet
        {
            ExternalId = id, A = a, B = math.lerp(a, d, 1f / 3), C = math.lerp(a, d, 2f / 3), D = d, Width = width,
        };

        [Fact]
        public void CrossingRoadIsFound()
        {
            var ring = ToolTestData.Square(100);
            var nets = new List<NearbyNet> { Straight(7, new float3(50, 0, -50), new float3(50, 0, 50)) };
            var atts = AttachmentFinder.Find(ring, nets);
            var c = atts.Find(a => a.Kind == AttachmentKind.RoadCrossing);
            Assert.Equal(7, c.ExternalId);
            Assert.Equal(0, c.RingSegment);
            Assert.Equal(0.5f, c.RingT, 3);
            Assert.Equal(0.5f, c.ExternalT, 2);
            Assert.Equal(50f, c.Position.x, 2);
        }

        [Fact]
        public void RoadAlongEdgeIsFound()
        {
            var ring = ToolTestData.Square(100);
            // Road 5 m outside the right edge (x = 105), running its full length.
            var nets = new List<NearbyNet> { Straight(9, new float3(105, 0, -20), new float3(105, 0, 120)) };
            var atts = AttachmentFinder.Find(ring, nets);
            Assert.Contains(atts, a => a.Kind == AttachmentKind.RoadAlongEdge && a.ExternalId == 9 && a.RingSegment == 1);
            Assert.DoesNotContain(atts, a => a.Kind == AttachmentKind.RoadCrossing);
        }

        [Fact]
        public void FarRoadIsIgnored()
        {
            var ring = ToolTestData.Square(100);
            var nets = new List<NearbyNet> { Straight(9, new float3(150, 0, -20), new float3(150, 0, 120)) };
            Assert.Empty(AttachmentFinder.Find(ring, nets));
        }
    }
}
