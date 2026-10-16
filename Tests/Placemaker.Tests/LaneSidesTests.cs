// (core) Shops face our lanes: our zoning roads get the vanilla per-side "no zoning" flag on every side no plot faces (LaneSides),
// the plots of the commercial types face our lanes and not the city street by default, and the city street's first zone row along
// the build gets a kerb strip (ZoneGuard.KerbStrips) unless a plot was planned on that street's own cells.

using System.Linq;
using Placemaker.Core;
using Placemaker.Core.Build;
using Placemaker.Core.Geometry;
using Unity.Mathematics;
using Xunit;

namespace Placemaker.Tests
{
    public class LaneSidesTests
    {
        [Theory]
        [InlineData("stripMall", "big")]
        [InlineData("mallDistrict", "big")]
        [InlineData("highStreet", "big")]
        [InlineData("shoppingPlaza", "big")]
        public void NoPlotSideIsSwitchedOffAndOtherSidesInsideAre(string type, string site)
        {
            var plan = CommercialS32Tests.Compose(type, "", site, out var ctx);
            var nets = plan.Layers.SelectMany(l => l.Nets).Where(n => ZoneBlockPredictor.Zones(n.Role)).ToList();
            var plots = plan.Layers.SelectMany(l => l.Zoning).Select(z => 0.25f * (z.A.xz + z.B.xz + z.C.xz + z.D.xz)).ToList();
            Assert.NotEmpty(plots);
            // Every plot whose nearest road is one of our lanes keeps that side zoning.
            foreach (var p in plots)
            {
                PlannedNet best = null; float bd = float.MaxValue; int bs = 0;
                foreach (var n in nets)
                {
                    float d = LaneSides.SideOf(n, p, out int s) - ZoneGuard.NetWidth(n, ctx) * 0.5f;
                    if (d < bd) { bd = d; best = n; bs = s; }
                }
                if (best == null || bd > LaneSides.BlockDepth) continue;
                bool cityNearer = ctx.NearbyNets.Any(c => Enumerable.Range(0, 17).Min(k => math.distance(Composer.Bezier(c.A, c.B, c.C, c.D, k / 16f).xz, p)) - c.Width * 0.5f < bd);
                if (cityNearer) continue;
                Assert.DoesNotContain(bs > 0 ? LaneSides.RightOff : LaneSides.LeftOff, best.Upgrades ?? new string[0]);
            }
        }

        [Fact]
        public void TheStripMallLanesLotSideIsSwitchedOff()
        {
            var plan = CommercialS32Tests.Compose("stripMall", "", "big", out _);
            var lanes = plan.Layers.SelectMany(l => l.Nets).Where(n => n.Role == NetRole.Alley).ToList();
            Assert.NotEmpty(lanes);
            Assert.Contains(lanes, n => n.Upgrades != null && (n.Upgrades.Contains(LaneSides.LeftOff) || n.Upgrades.Contains(LaneSides.RightOff)));
        }

        [Fact]
        public void KerbStripsBlockTheCityRowAlongTheBuildButNotItsOwnStreetPlots()
        {
            // A square build with a city street's block (10 columns, 6 rows) reaching in from the south edge.
            var ring = new[] { new float2(0, 0), new float2(100, 0), new float2(100, 100), new float2(0, 100) };
            var plan = new ComposedPlan();
            plan.Footprints.Add(ring);
            // Direction points toward the road (south, -z); row 0 is the southmost row, its road edge at z = -1.
            var g = new ZoneGrid { Position = new float2(50, 23), Direction = new float2(0, -1), Size = new int2(10, 6) };
            var site = new SiteContext();
            site.ZoneGrids.Add(g);
            // A lane plot somewhere in the middle (not on the street's cells).
            plan.ZoneQuads.Add(new ZoneQuad { Quad = new[] { new float2(40, 40), new float2(56, 40), new float2(56, 56), new float2(40, 56) }, ZoneId = "x" });
            var strips = ZoneGuard.KerbStrips(plan, site);
            Assert.NotEmpty(strips);
            foreach (var s in strips)
                foreach (var p in s)
                {
                    Assert.True(Poly.Contains(ring, p), "a kerb strip leaves the build");
                    Assert.True(p.y < 23f - 8f, "a kerb strip outside the first rows");
                }
            // A plot planned on the street's own cells keeps its columns.
            var cell = ZoneGuard.CellRect(g, 3, 4, 0, 1, -ZoneGuard.QuadShrink);
            plan.ZoneQuads.Add(new ZoneQuad { Quad = cell, ZoneId = "x" });
            var spared = ZoneGuard.KerbStrips(plan, site);
            float covered(System.Collections.Generic.List<float2[]> ss) => ss.Sum(s => Poly.Area(s));
            Assert.True(covered(spared) < covered(strips) - 1f, "the street plot's columns were blocked");
        }
    }
}
