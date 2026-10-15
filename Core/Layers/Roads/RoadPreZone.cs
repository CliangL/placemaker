// Placemaker roads layer (s34) — "Pre-zone all new roads".
//
// Playtest (image 62): the Roads type zoned its frontage through the edge-zoning cutout feature — quads clipped by
// whatever the layer reserved, then snapped to cells — and the result was a scattered pattern of zoned and empty
// cells. The player wanted the obvious thing: every cell of every new road's zone blocks zoned with one zone type.
// The plan carries one zoning quad per predicted block (Build/ZoneBlockPredictor: the blocks CS2 will give each road);
// after the build the game layer re-reads the live blocks of the roads it just built and zones each one whole
// (FromLive), so splits and junctions the prediction cannot know are exact too. Cells the game blocks (under roads,
// buildings) simply stay empty; Marquee zoning never overwrites what the player zoned before.

using System;
using System.Collections.Generic;
using Placemaker.Core.Build;
using Placemaker.Core.Geometry;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Roads
{
    public static class RoadPreZone
    {
        public const string None = "none";
        /// <summary>AreaPlan fact: the plan's zoning quads are "zone the new roads whole" (the game re-snaps them to the live blocks).</summary>
        public const string Fact = "roads.preZone";

        /// <summary>Choice values of the pre-zone control: none, then the zone types.</summary>
        public static string[] Values()
        {
            var v = new List<string> { None };
            v.AddRange(Common.ZoneKinds.All);
            return v.ToArray();
        }

        /// <summary>Zone prefab of a pre-zone choice ("" = none).</summary>
        public static string ZoneId(string choice) =>
            string.IsNullOrEmpty(choice) || choice == None ? "" : Common.ZoneKinds.PrefabId(choice);

        /// <summary>One zoning quad per predicted zone block of every zoning road in <paramref name="nets"/>.</summary>
        public static List<PlannedZoning> Quads(IEnumerable<PlannedNet> nets, SiteContext site, string zoneId, Func<float2, float> height)
        {
            var r = new List<PlannedZoning>();
            if (string.IsNullOrEmpty(zoneId) || nets == null) return r;
            height = height ?? (_ => 0f);
            foreach (var n in nets)
            {
                if (n == null || !ZoneBlockPredictor.Zones(n.Role)) continue;
                if (site?.Assets != null && !string.IsNullOrEmpty(n.Asset.Id) && site.Assets.TryGetValue(n.Asset.Id, out var ai) && ai.ZoneBlocks == 2) continue;
                foreach (var g in ZoneBlockPredictor.Blocks(n.A, n.B, n.C, n.D, ZoneGuard.NetWidth(n, site)))
                    r.Add(Whole(g, zoneId, height));
            }
            return r;
        }

        /// <summary>The whole block as a Marquee quad (1 m inside the outer cell edges: the tool takes cells by centre).</summary>
        public static PlannedZoning Whole(ZoneGrid g, string zoneId, Func<float2, float> height)
        {
            var q = ZoneGuard.CellRect(g, 0, g.Size.x - 1, 0, g.Size.y - 1, -1f);
            float3 P(float2 v) => new float3(v.x, height(v), v.y);
            return new PlannedZoning { A = P(q[0]), B = P(q[1]), C = P(q[2]), D = P(q[3]), ZoneId = zoneId };
        }

        /// <summary>A live zone block and the curve of the road edge that owns it (game layer capture).</summary>
        public struct LiveBlock
        {
            public ZoneGrid Grid;
            public float3 OwnerA, OwnerB, OwnerC, OwnerD;
        }

        /// <summary>
        /// The live blocks that belong to roads of this plan (their owner edge lies on a planned zoning road: both ends and the
        /// middle within <paramref name="tolerance"/> of the same planned curve), each zoned whole.
        /// </summary>
        public static List<ZoneGuard.ZoningQuad> FromLive(IReadOnlyList<PlannedNet> nets, IReadOnlyList<LiveBlock> blocks, string zoneId, float tolerance = 1.5f)
        {
            var r = new List<ZoneGuard.ZoningQuad>();
            if (nets == null || blocks == null || string.IsNullOrEmpty(zoneId)) return r;
            var curves = new List<(RoadCurve c, float2 mn, float2 mx)>();
            foreach (var n in nets)
            {
                if (n == null || !ZoneBlockPredictor.Zones(n.Role)) continue;
                var c = new RoadCurve(n.A, n.B, n.C, n.D);
                float2 mn = math.min(math.min(n.A.xz, n.B.xz), math.min(n.C.xz, n.D.xz)) - tolerance;
                float2 mx = math.max(math.max(n.A.xz, n.B.xz), math.max(n.C.xz, n.D.xz)) + tolerance;
                curves.Add((c, mn, mx));
            }
            foreach (var b in blocks)
            {
                var owner = new RoadCurve(b.OwnerA, b.OwnerB, b.OwnerC, b.OwnerD);
                float2 a = owner.A.xz, m = owner.Position(0.5f).xz, d = owner.D.xz;
                bool ours = false;
                foreach (var (c, mn, mx) in curves)
                {
                    if (!In(a, mn, mx) || !In(d, mn, mx)) continue;
                    if (c.DistanceXZ(a, out _) <= tolerance && c.DistanceXZ(m, out _) <= tolerance && c.DistanceXZ(d, out _) <= tolerance) { ours = true; break; }
                }
                if (!ours || b.Grid.Size.x <= 0 || b.Grid.Size.y <= 0) continue;
                r.Add(new ZoneGuard.ZoningQuad { Corners = ZoneGuard.CellRect(b.Grid, 0, b.Grid.Size.x - 1, 0, b.Grid.Size.y - 1, -1f), ZoneId = zoneId });
            }
            return r;
        }

        private static bool In(float2 p, float2 mn, float2 mx) => p.x >= mn.x && p.y >= mn.y && p.x <= mx.x && p.y <= mx.y;
    }
}
