using System.Collections.Generic;
using System.Linq;
using Placemaker.Core.Layers.Parking;
using Unity.Mathematics;
using Xunit;
using Xunit.Abstractions;
namespace Placemaker.Tests
{
    public class ParkingScratchTests
    {
        private readonly ITestOutputHelper _o;
        public ParkingScratchTests(ITestOutputHelper o) { _o = o; }
        [Fact]
        public void Scratch()
        {
            var rect = ParkingKit.Rect(160, 110);
            var b = new ParkingLayer().Defaults(); b.Values["stallAngle"] = "45";
            var run = ParkingKit.Run(rect, b, new List<Placemaker.Core.EdgeMarker> { ParkingKit.Marker(rect, 0, 60) });
            var L = run.Layout;
            PGeo.Bounds(L.Lot, out var mn, out var mx);
            int shown = 0;
            for (double y = mn.y + 0.3; y < mx.y; y += 0.7)
                for (double x = mn.x + 0.3; x < mx.x; x += 0.7)
                {
                    var p = new double2(x + 0.0123, y + 0.0177);
                    var hits = L.Surfaces.Where(s => PGeo.Contains(s.Ring, p)).ToList();
                    if (hits.Count > 1 && shown++ < 4)
                    {
                        _o.WriteLine($"overlap at {p}");
                        foreach (var h in hits) _o.WriteLine($"   {(h.Asphalt ? "A" : "G")} n={h.Ring.Count} area={PGeo.SignedArea(h.Ring):F2} simple={PGeo.IsSimple(h.Ring)} cells={h.Cells.Count} cellsArea={h.Cells.Sum(c => PGeo.Area(c)):F2}");
                    }
                }
            _o.WriteLine("shown " + shown);
        }
    }
}
