// rf-parking (2026-10-16): on a slope the entrance must not climb a bank. The game runner (show-3-builds-1016-064133-a020,
// 170 x 130 L on a 7 % slope, the street ~10 m off the outline) logged "net sag: Invisible Road Path humps 1.1 m ... a 493.5
// b 496.1": the graded lot stood 2.6 m above the street at its entrance and the connector climbed it in its first 6 m.
// The lot edge is tied to a road only within 6 m of the road's edge (LotGrading.TieReach); a car entrance now ties the
// edge to its street wherever the street is (the connector runs there anyway).

using System;
using System.Collections.Generic;
using System.Linq;
using Placemaker.Core;
using Placemaker.Core.Layers.Parking;
using Unity.Mathematics;
using Xunit;
using Xunit.Abstractions;

namespace Placemaker.Tests
{
    public class ParkingEntranceRampTests
    {
        private readonly ITestOutputHelper _out;
        public ParkingEntranceRampTests(ITestOutputHelper o) { _out = o; }

        private static float Ground(float2 p) => 0.07f * p.y;

        /// <summary>Streets all round, their centre <paramref name="gap"/> m outside the outline, on the ground.</summary>
        private static SiteContext Site(float3[] ring, float gap)
        {
            var site = new SiteContext { HeightAt = Ground };
            for (int i = 0; i < ring.Length; i++)
            {
                float3 p0 = ring[i], p1 = ring[(i + 1) % ring.Length];
                float3 d = math.normalize(p1 - p0);
                float3 n = new float3(d.z, 0, -d.x) * gap;
                float3 a = p0 - d * 30f + n, b = p1 + d * 30f + n;
                float3 Y(float3 q) => new float3(q.x, Ground(q.xz), q.z);
                site.NearbyNets.Add(new NearbyNet { ExternalId = 700 + i, A = Y(a), B = Y(math.lerp(a, b, 1f / 3f)), C = Y(math.lerp(a, b, 2f / 3f)), D = Y(b), Width = 12f, Category = NetCategory.Road, HasSidewalk = true });
            }
            return site;
        }

        [Theory]
        [InlineData(8f)]    // the street at the outline (the city block case)
        [InlineData(16f)]   // the street 10 m off the outline (the runner's land)
        [InlineData(22f)]
        public void EntranceOnTheLowSideIsAGentleRamp(float gap)
        {
            var ring = ParkingKit.Rect(140, 110);
            var r = ParkingKit.Run(ring, null, new List<EdgeMarker> { ParkingKit.Marker(ring, 0, 70) }, Site(ring, gap));
            Assert.False(r.Plan.HasErrors);
            var conn = r.Plan.Nets.Where(n => n.Role == NetRole.DriveConnector).ToList();
            Assert.NotEmpty(conn);
            float worst = 0;
            foreach (var n in conn)
            {
                var pts = new[] { n.A, n.B, n.C, n.D };
                for (int i = 1; i < 4; i++)
                {
                    float run = math.distance(pts[i].xz, pts[i - 1].xz);
                    if (run < 0.5f) continue;
                    worst = math.max(worst, math.abs(pts[i].y - pts[i - 1].y) / run);
                }
                _out.WriteLine($"gap {gap}: connector {string.Join(" ", pts.Select(p => $"({p.x:F0},{p.z:F0}) {p.y:F2}"))}");
            }
            _out.WriteLine($"gap {gap}: steepest connector piece {worst:P0}");
            Assert.True(worst <= 0.12f, $"the entrance climbs {worst:P0}");
        }
    }
}
