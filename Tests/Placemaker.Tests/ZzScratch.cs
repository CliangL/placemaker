using System;
using System.Linq;
using System.Threading;
using Placemaker.Core;
using Placemaker.Core.Build;
using Placemaker.Core.Geometry;
using Placemaker.Core.Layers.Common;
using Placemaker.Core.Tool;
using Unity.Mathematics;
using Xunit;
using Xunit.Abstractions;

namespace Placemaker.Tests
{
    public class ZzScratch
    {
        private readonly ITestOutputHelper _out;
        public ZzScratch(ITestOutputHelper o) { _out = o; }
        private static readonly DesignEditor Editor = new DesignEditor(GeneratorRegistry.Get);
        [Theory]
        [InlineData("mallPark", "")]
        [InlineData("shopsPark", "row")]
        [InlineData("highStreet", "")]
        public void Dump(string type, string preset)
        {
            var ring = TestKit.Rect(160, 160);
            var site = ZoningFirstTests.Captured(ring);
            var r = Editor.SingleLayerRecipe(GeneratorRegistry.Get(type), 5);
            if (preset != "") Editor.ApplyPreset(r, RecipePaths.Root, preset);
            uint n = 0; Editor.AddDefaultFeatures(r, () => ++n);
            var plan = Composer.Compose(r, new AreaShape { Ring = ring, Source = AreaSource.Drawn }, site, CancellationToken.None);
            var zoned = plan.ZoneCells.Where(c => c.State == ZoneCellState.Zoned).ToList();
            _out.WriteLine("== " + type + "/" + preset + " issues " + string.Join(",", plan.AllIssues().Select(i => i.Key.Substring(i.Key.LastIndexOf('.') + 1))));
            foreach (var g in plan.Layers.SelectMany(l => l.Regions).Where(Cutouts.IsCutout))
            {
                var q = Poly.To2D(g.Ring);
                int cells = zoned.Count(c => Poly.Contains(q, 0.25f * (c.A + c.B + c.C + c.D)));
                if (cells * 64 >= Poly.Area(q) - 1f || !g.Tags.Contains("zoneCells")) continue;
                _out.WriteLine($"LOST {Poly.Centroid(q)} area {Poly.Area(q):0} cells {cells}");
                foreach (var l in plan.Layers)
                {
                    foreach (var nn in l.Nets)
                    {
                        float best = 99f;
                        for (int i = 0; i <= 16; i++) best = math.min(best, Poly.DistanceToBoundary(q, Composer.Bezier(nn.A, nn.B, nn.C, nn.D, i / 16f).xz));
                        if (best < 5f) _out.WriteLine($"   net {l.GeneratorId} {nn.Role} {nn.A.xz}->{nn.D.xz} d {best:0.0}");
                    }
                    foreach (var s in l.Surfaces)
                    {
                        float d = Poly.DistanceToBoundary(Poly.To2D(s.Ring), Poly.Centroid(q));
                        var sr = Poly.To2D(s.Ring);
                        float md = 99f; foreach (var p in q) md = math.min(md, Poly.Contains(sr, p) ? 0f : Poly.DistanceToBoundary(sr, p));
                        if (md < 0.7f) _out.WriteLine($"   surf {l.GeneratorId} {s.Role} md {md:0.0}");
                    }
                    foreach (var a in l.Areas) { var sr = Poly.To2D(a.Ring); float md = 99f; foreach (var p in q) md = math.min(md, Poly.Contains(sr, p) ? 0f : Poly.DistanceToBoundary(sr, p)); if (md < 1f) _out.WriteLine($"   area {l.GeneratorId} {a.Role} md {md:0.0}"); }
                }
            }
            foreach (var g in plan.Layers.SelectMany(l => l.Regions).Where(Cutouts.IsCutout))
            {
                var q = Poly.To2D(g.Ring);
                int cells = zoned.Count(c => Poly.Contains(q, 0.25f * (c.A + c.B + c.C + c.D)));
                _out.WriteLine($"PLOT {Poly.Centroid(q)} area {Poly.Area(q):0} cells {cells}*64={cells * 64} tags {string.Join(",", g.Tags)}");
            }
        }
    }
}
