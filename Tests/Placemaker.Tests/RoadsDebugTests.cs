using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Placemaker.Core;
using Placemaker.Core.Layers.Roads;
using Unity.Mathematics;
using Xunit;
using Xunit.Abstractions;
using System.Linq;
using static Placemaker.Tests.RoadsTestUtil;

namespace Placemaker.Tests
{
    public class RoadsDebugTests
    {
        private readonly ITestOutputHelper _out;
        public RoadsDebugTests(ITestOutputHelper o) { _out = o; }

        static string P(float x, float z) => "[" + x.ToString("F2", CultureInfo.InvariantCulture) + "," + z.ToString("F2", CultureInfo.InvariantCulture) + "]";
        static string Ring(IEnumerable<float3> r) => "[" + string.Join(",", r.Select(p => P(p.x, p.z))) + "]";

        public static void Dump(string path, AreaInput input, AreaPlan plan)
        {
            var sb = new StringBuilder();
            sb.Append("{\"ring\":").Append(Ring(input.Shape.Ring));
            sb.Append(",\"nets\":[");
            var nets = new List<string>();
            foreach (var n in plan.Nets)
            {
                var c = new RoadCurve(n.A, n.B, n.C, n.D);
                var pts = Enumerable.Range(0, 9).Select(i => c.Position(i / 8f));
                nets.Add("{\"pts\":" + Ring(pts) + ",\"hw\":" + Composer.DefaultHalfWidth(n.Role).ToString(CultureInfo.InvariantCulture) + ",\"role\":\"" + n.Role + "\"}");
            }
            foreach (var n in input.Site.NearbyNets)
            {
                var c = new RoadCurve(n.A, n.B, n.C, n.D);
                var pts = Enumerable.Range(0, 9).Select(i => c.Position(i / 8f));
                nets.Add("{\"pts\":" + Ring(pts) + ",\"hw\":" + (n.Width / 2).ToString(CultureInfo.InvariantCulture) + ",\"role\":\"Existing\"}");
            }
            sb.Append(string.Join(",", nets)).Append("]");
            sb.Append(",\"blocks\":[").Append(string.Join(",", plan.Regions.Where(r => r.Tags.Contains("block")).Select(r => Ring(r.Ring)))).Append("]");
            sb.Append(",\"edges\":[").Append(string.Join(",", plan.Regions.Where(r => r.Tags.Contains("edge")).Select(r => Ring(r.Ring)))).Append("]");
            sb.Append(",\"zoning\":[").Append(string.Join(",", plan.Zoning.Select(z => Ring(new[] { z.A, z.B, z.C, z.D })))).Append("]");
            sb.Append(",\"objects\":[").Append(string.Join(",", plan.Objects.Select(o => "[" + o.Position.x.ToString(CultureInfo.InvariantCulture) + "," + o.Position.z.ToString(CultureInfo.InvariantCulture) + ",6]"))).Append("]");
            sb.Append("}");
            File.WriteAllText(path, sb.ToString());
        }

        [Fact]
        public void DumpAll()
        {
            string dir = Environment.GetEnvironmentVariable("PLACEMAKER_DUMP");
            if (string.IsNullOrEmpty(dir)) return;
            var shapes = new (string, List<float3>, AreaSource)[]
            {
                ("rect", Poly(0, 0, 600, 0, 600, 400, 0, 400), AreaSource.Drawn),
                ("L", Poly(0, 0, 600, 0, 600, 300, 300, 300, 300, 600, 0, 600), AreaSource.Block),
                ("tri", Poly(0, 0, 700, 0, 200, 600), AreaSource.Drawn),
            };
            {
                // Block pick: outline = existing road centre lines, plus an existing diagonal road inside.
                var site = new SiteContext { HeightAt = Hills };
                long id = 100;
                void Road(float x0, float z0, float x1, float z1, float w = 12f)
                {
                    var a = new float3(x0, 0, z0); var d = new float3(x1, 0, z1);
                    site.NearbyNets.Add(new NearbyNet { ExternalId = id++, A = a, B = math.lerp(a, d, 1 / 3f), C = math.lerp(a, d, 2 / 3f), D = d, Width = w, Category = NetCategory.Road });
                }
                Road(0, 0, 400, 0); Road(400, 0, 400, 300); Road(400, 300, 0, 300); Road(0, 300, 0, 0); Road(-50, 50, 450, 260, 16f);
                foreach (var pat in new[] { "grid", "organic", "loop" })
                {
                    var input = Input(Poly(0, 0, 400, 0, 400, 300, 0, 300), AreaSource.Block, Settings((RoadsIds.Pattern, pat)), site);
                    var plan = Run(input);
                    Dump(Path.Combine(dir, $"block-{pat}.json"), input, plan);
                    _out.WriteLine($"block-{pat}: attached ends {plan.Nets.Count(n => n.StartAttach >= 0) + plan.Nets.Count(n => n.EndAttach >= 0)} nets {plan.Nets.Count}");
                }
                // Drawn area crossed by an existing road: perimeter road joins it.
                var site2 = new SiteContext { HeightAt = Hills };
                site = site2; Road(-100, 150, 700, 250, 16f);
                var input2 = Input(Poly(0, 0, 600, 0, 600, 400, 0, 400), AreaSource.Drawn, Settings((RoadsIds.Pattern, "grid")), site2);
                var plan2 = Run(input2);
                Dump(Path.Combine(dir, "drawn-crossed.json"), input2, plan2);
                _out.WriteLine($"drawn-crossed: attached ends {plan2.Nets.Count(n => n.StartAttach >= 0) + plan2.Nets.Count(n => n.EndAttach >= 0)}");
            }
            foreach (var (name, shape, src) in shapes)
                foreach (var pat in RoadsSchema.PatternValues)
                {
                    var input = Input(shape, src, Settings((RoadsIds.Pattern, pat)));
                    input.Site.HeightAt = Hills;
                    var plan = Run(input);
                    Dump(Path.Combine(dir, $"{name}-{pat}.json"), input, plan);
                }
        }
    }
}
namespace Placemaker.Tests
{
    public class RoadsDebug2Tests
    {
        private readonly Xunit.Abstractions.ITestOutputHelper _out;
        public RoadsDebug2Tests(Xunit.Abstractions.ITestOutputHelper o) { _out = o; }
        [Xunit.Fact]
        public void Island()
        {
            var input = RoadsTestUtil.Input(RoadsTestUtil.Poly(0, 0, 600, 0, 600, 400, 0, 400), AreaSource.Drawn, RoadsTestUtil.Settings((RoadsIds.Pattern, "radial")));
            var plan = RoadsTestUtil.Run(input);
            foreach (var n in plan.Nets.Where(n => n.Role == NetRole.Roundabout)) _out.WriteLine($"rb ({n.A.x:F1},{n.A.z:F1}) ({n.B.x:F1},{n.B.z:F1}) ({n.C.x:F1},{n.C.z:F1}) ({n.D.x:F1},{n.D.z:F1})");
            var core = plan.Regions.First(r => r.Tags.Contains("core"));
            _out.WriteLine(string.Join(" ", core.Ring.Select(p => $"({p.x:F1},{p.z:F1})")));
        }
    }
}
namespace Placemaker.Tests
{
    public class RoadsDebug3Tests
    {
        private readonly Xunit.Abstractions.ITestOutputHelper _out;
        public RoadsDebug3Tests(Xunit.Abstractions.ITestOutputHelper o) { _out = o; }
        [Xunit.Fact]
        public void LoopCounts()
        {
            var shape = RoadsTestUtil.Poly(0, 0, 400, 0, 400, 300, 0, 300);
            var segs = Placemaker.Core.Layers.Roads.Generators.GridGenerator.GenerateLoopGrid(shape, Placemaker.Core.Layers.Roads.Generators.GridParameters.Default);
            _out.WriteLine("raw loop default params: " + segs.Count);
            var p = Placemaker.Core.Layers.Roads.Generators.GridParameters.Default; p.CollectorSpacingMeters = 250;
            _out.WriteLine("raw loop 250: " + Placemaker.Core.Layers.Roads.Generators.GridGenerator.GenerateLoopGrid(shape, p).Count);
            foreach (var src in new[] { AreaSource.Block, AreaSource.Drawn })
            {
                var input = RoadsTestUtil.Input(shape, src, RoadsTestUtil.Settings((RoadsIds.Pattern, "loop")));
                var plan = RoadsTestUtil.Run(input);
                _out.WriteLine($"{src}: nets {plan.Nets.Count} " + string.Join(",", plan.Issues.Select(i => i.Key)));
            }
        }
    }
}
