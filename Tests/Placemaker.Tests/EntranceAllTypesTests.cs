// s58: the entrance rule of s41/s47 for EVERY type, not only the mall family: wherever a net of any layer attaches to an existing
// street (a lane, a drive connector, a path), it leaves the street at the street's own height (street on a fill, in a cut, on a
// cross slope), and the ground round the join has no hollow.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Placemaker.Core;
using Placemaker.Core.Geometry;
using Placemaker.Core.Tool;
using Unity.Mathematics;
using Xunit;

namespace Placemaker.Tests
{
    public class EntranceAllTypesTests
    {
        private static readonly DesignEditor Editor = new DesignEditor(GeneratorRegistry.Get);

        public static IEnumerable<object[]> Cases()
        {
            foreach (var g in GeneratorRegistry.All)
            {
                if (GeneratorRegistry.IsHidden(g) || GeneratorRegistry.IsLoose(g.Id)) continue;
                foreach (var kind in new[] { "fill", "cut", "cross" })
                    yield return new object[] { g.Id, kind };
            }
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public void EveryNetAttachedToAStreetLeavesItFlushWithoutAHollow(string gen, string kind)
        {
            var ring = TestKit.Rect(140, 130);
            var site = ZoningCutoutTests.Streets(ring);
            Func<float2, float> ground = kind == "cross" ? LotGradingTests.Ground(kind) : (_ => 0f);
            float off = kind == "fill" ? 1.2f : kind == "cut" ? -1f : 0f;
            site.HeightAt = ground;
            for (int k = 0; k < site.NearbyNets.Count; k++)
            {
                var n = site.NearbyNets[k];
                n.A.y = ground(n.A.xz) + off; n.B.y = ground(n.B.xz) + off; n.C.y = ground(n.C.xz) + off; n.D.y = ground(n.D.xz) + off;
                site.NearbyNets[k] = n;
            }
            var r = Editor.SingleLayerRecipe(GeneratorRegistry.Get(gen), 5);
            uint q = 0;
            Editor.AddDefaultFeatures(r, () => ++q);
            ComposedPlan plan;
            try { plan = Composer.Compose(r, new AreaShape { Ring = ring, Source = AreaSource.Drawn }, site, CancellationToken.None); }
            catch (Exception e) { throw new Exception($"{gen}/{kind}: {e.Message}", e); }
            string ctx = $"{gen}/{kind}";
            var final = LotGradingTests.Final(plan, ground);
            var joins = new List<float2>();

            foreach (var l in plan.Layers.Where(l => l != null))
                foreach (var n in l.Nets)
                {
                    if (n.Role == NetRole.Fence) continue;
                    for (int end = 0; end < 2; end++)
                    {
                        long id = end == 0 ? n.StartAttach : n.EndAttach;
                        if (id < 0) continue;
                        var street = site.NearbyNets.FirstOrDefault(s => s.ExternalId == id);
                        if (street.Width <= 0f) continue;
                        float t = end == 0 ? n.StartAttachT : n.EndAttachT;
                        float hs = Composer.Bezier(street.A, street.B, street.C, street.D, t).y;
                        var p0 = end == 0 ? n.A.xz : n.D.xz;
                        var p1 = end == 0 ? n.D.xz : n.A.xz;
                        var d = math.normalizesafe(p1 - p0);
                        float edge = street.Width * 0.5f;
                        float atKerb = final(p0 + d * (edge + 0.5f));
                        Assert.True(math.abs(atKerb - hs) <= 0.35f, $"{ctx}: {n.Role} in {l.GeneratorId} at the kerb {atKerb:0.00} m, street {hs:0.00} m");
                        joins.Add(p0 + d * (edge + 1f));
                    }
                }

            foreach (var j in joins)
                for (float dy = -10f; dy <= 10f; dy += 2f)
                    for (float dx = -10f; dx <= 10f; dx += 2f)
                    {
                        var p = j + new float2(dx, dy);
                        bool streetNear = false;
                        foreach (var s in site.NearbyNets)
                            if (math.sqrt(SegmentIndex.DistSq(p, s.A.xz, s.D.xz)) < s.Width * 0.5f + 0.5f) { streetNear = true; break; }
                        if (streetNear) continue;
                        float h = final(p), low = float.MaxValue;
                        for (int k = 0; k < 8; k++) { float a = k * math.PI / 4f; low = math.min(low, final(p + 3f * new float2(math.cos(a), math.sin(a)))); }
                        Assert.False(h < low - 0.15f, $"{ctx}: a hollow at {p} next to a street join ({low - h:0.00} m below its surroundings)");
                    }
        }
    }
}
