using System.Collections.Generic;
using System.Linq;
using Placemaker.Core.Suite;
using Unity.Mathematics;
using Xunit;

namespace Placemaker.Tests.Suite
{
    public class SuitePlotTests
    {
        [Fact]
        public void Plots_LieEastOfTheGrid_InARow_WithoutOverlap()
        {
            var l = new SuiteLayout();
            var a = l.AddPlot(new SuitePlot { Size = new float2(130, 120) });
            var b = l.AddPlot(new SuitePlot { Size = new float2(900, 700), Ground = "mountains", RoadsAround = false });
            Assert.Equal("P1", a.Name);
            Assert.Equal("P2", b.Name);
            Assert.True(a.OuterMin.x >= l.GridMax.x + SuiteLayout.PlotGap - 0.01f);
            Assert.True(b.OuterMin.x >= a.OuterMax.x + SuiteLayout.PlotGap - 0.01f);
            Assert.True(l.FixtureMax.x >= b.OuterMax.x && l.FixtureMin.y <= b.OuterMin.y);
        }

        [Fact]
        public void Shapes_HaveTheirCorners()
        {
            var p = new SuitePlot { Size = new float2(120, 80) };
            Assert.Equal(4, p.Outline().Length);
            p.Shape = "pinch";
            var pinch = p.Outline();
            Assert.Equal(6, pinch.Length);
            Assert.Equal(80 * 0.5f * 0.3f, pinch[4].y - p.Center.y, 3);   // the waist
            p.Shape = "circle";
            int n = p.Outline().Length;
            Assert.InRange(n, 12, 40);
            p.Shape = "points";
            p.Points = new[] { new float2(10, 10), new float2(70, 10), new float2(40, 90) };
            p.SizeFromPoints();
            Assert.Equal(new float2(60, 80), p.Size);
            Assert.Equal(3, p.Outline().Length);
        }

        [Fact]
        public void Ground_AppliesOverThePlot_AndFadesAway()
        {
            var l = new SuiteLayout();
            var hill = l.AddPlot(new SuitePlot { Size = new float2(200, 200), Ground = "slope", Grade = 0.1f, Toward = "north" });
            float south = l.Height(hill.Center.x, hill.LandMin.y), north = l.Height(hill.Center.x, hill.LandMax.y);
            Assert.Equal(20f, north - south, 1);                           // 10 % over 200 m
            Assert.Equal(l.Base, l.Height(hill.Center.x, hill.Center.y), 2); // centred on the base
            Assert.Equal(l.Base, l.Height(hill.OuterMax.x + 10f, hill.Center.y), 2);
            Assert.Equal(l.Base, l.Height(l.GridMax.x + 100f, l.LineZ(l.NZ)), 2);   // the grid's slope rows stop before the plots
        }

        [Fact]
        public void Heights_StretchOverTheLand_NorthRowFirst()
        {
            var p = new SuitePlot { Size = new float2(100, 100), Ground = "heights", Heights = new[] { new[] { 5f, 5f }, new[] { 0f, 0f } } };
            Assert.Equal(5f, p.Rise(0, 50), 3);
            Assert.Equal(0f, p.Rise(0, -50), 3);
            Assert.Equal(2.5f, p.Rise(0, 0), 3);
        }

        [Fact]
        public void RingRoads_CloseAroundTheLand()
        {
            var p = new SuitePlot { Size = new float2(130, 120) };
            var flat = p.RingRoads();
            Assert.Equal(8, flat.Count);   // 162 m and 152 m sides, at most 150 m a piece on flat ground
            Assert.Equal(flat[0].A, flat[flat.Count - 1].B);
            p.Ground = "hills";
            var hilly = p.RingRoads();
            Assert.True(hilly.Count > 8);
            for (int k = 0; k < hilly.Count; k++) Assert.Equal(hilly[k].B, hilly[(k + 1) % hilly.Count].A);
            p.RoadsAround = false;
            Assert.Empty(p.RingRoads());
            Assert.Equal("", p.Block().South);
        }
    }
}
