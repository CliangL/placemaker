using System.Linq;
using Placemaker.Core;
using Xunit;
using Xunit.Abstractions;
using static Placemaker.Tests.Types.WaterTypesKit;
namespace Placemaker.Tests.Types
{
    public class ShoreDebugTests
    {
        private readonly ITestOutputHelper _out;
        public ShoreDebugTests(ITestOutputHelper o) { _out = o; }
        [Fact]
        public void Dump()
        {
            foreach (var (preset, ring) in new[] { ("islandLake", TestKit.Blob(55f, 48, 3)), ("grandLake", TestKit.Blob(75f, 48, 5)) })
            {
                var plan = PlanPreset("pond", TestKit.Gen("pond").Presets.Single(p => p.Id == preset), ring, new SiteContext());
                _out.WriteLine(preset + ": " + string.Join(", ", plan.Facts.Select(kv => kv.Key + "=" + kv.Value)));
                _out.WriteLine("  issues: " + string.Join(", ", plan.Issues.Select(i => i.Key + "(" + string.Join("/", i.Args) + ")")));
                _out.WriteLine("  objects: " + string.Join(", ", plan.Objects.GroupBy(o => o.Role + "/" + o.Category).Select(g => g.Key + "x" + g.Count())));
                _out.WriteLine("  nets: " + string.Join(", ", plan.Nets.Select(n => n.Role + (n.FollowTerrain ? "" : "~") )));
            }
        }
    }
}
