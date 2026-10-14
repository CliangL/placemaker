// Park lake upgrade (grand park wave, g2), used by the pond layer (the park's "Pond" feature):
//
//   promenade  a paved ring band inside the basin outline with a walk, benches facing the water and
//              lamps; the water is the inset ring
//   islands    1–3 organic islands at the most open water (each keeps a channel of open water
//              around it), raised above the water level, planted, the largest with a gazebo/statue
//   bridges    arched decks from each island to the nearest free shore (and between close islands)
//   jets       fountain jets in a line, ring or cluster in open water
//   boathouse  a boathouse at the water's edge with a jetty and rowing boats; more boats moored
// Terraform order matters: the basin is lowered first, then the islands are raised back above the
// water. The water surface skips the islands. With nothing enabled it is exactly the old lake.

using System;
using System.Collections.Generic;
using Placemaker.Core.Geometry;
using Placemaker.Core.Layers.Common;
using Placemaker.Core.Layers.Common.Shore;
using Placemaker.Core.Layers.Park;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.AddOns.Water
{
    public static class LakeDressing
    {
        public static readonly string[] JetLayouts = { "line", "ring", "cluster" };

        public sealed class Options
        {
            public float Depth = 3f;
            public int Islands;
            public bool Bridges = true, IslandTrees = true;
            public int Jets;
            public string JetLayout = "line";
            public bool Boathouse;
            public int Rowboats;
            public bool Promenade;
            public float PromWidth = 8f;
            public float BenchSpacing = 14f;
            public bool Lamps = true;
            public bool Connect = true;

            public bool Plain => Islands <= 0 && Jets <= 0 && !Boathouse && Rowboats <= 0 && !Promenade;
        }

        private struct Island { public float2 C; public float R; public float2[] Ring; }

        public static void Plan(LayerContext ctx, float2[] lake, Options o)
        {
            if (o.Plain) { ParkLayer.EmitLake(ctx, lake, o.Depth); return; }
            var outer = LayerRegion.From(lake);
            if (outer == null) { ParkLayer.EmitLake(ctx, lake, o.Depth); return; }
            var occ = new Occupancy(outer, ctx.Input.Reserved, ctx.Site);
            var reserved = ctx.Input.Reserved;
            var walkAsset = AssetMix.One("");
            float hw = Composer.DefaultHalfWidth(NetRole.Pathway);
            var links = new List<float2>();

            // 1. Promenade: the water shrinks to an inset ring.
            float2[] water = lake;
            bool promenade = false;
            if (o.Promenade)
            {
                var inset = Poly.Clean(outer.InsetVertices(o.PromWidth), 0.3f);
                if (inset.Length >= 3 && Poly.IsSimple(inset) && Poly.IsCcw(inset) && Poly.Area(inset) > math.max(250f, outer.Area * 0.3f))
                { water = inset; promenade = true; }
                else ctx.Issue(IssueSeverity.Info, "Placemaker.Pond.Issue.NoPromenade");
            }
            var wr = LayerRegion.From(water) ?? outer;
            float shore = float.MaxValue;
            foreach (var p in water) shore = math.min(shore, ctx.H(p));
            if (!math.isfinite(shore)) shore = 0f;
            float level = shore - 0.5f;
            ctx.Check();

            // 2. Islands at the most open water, each keeping a channel around it.
            var islands = new List<Island>();
            for (int k = 0; k < math.clamp(o.Islands, 0, 3); k++)
            {
                var c = wr.DeepestPoint(out float clr, (p, cap) =>
                {
                    float m = cap;
                    foreach (var isl in islands) m = math.min(m, math.distance(p, isl.C) - isl.R - 2f);
                    return m;
                }, 16);
                float r = math.min(math.min(clr * 0.45f, 4f + math.sqrt(wr.Area) * 0.08f), 22f);
                if (k > 0) r *= 0.75f;
                if (r < 4f || clr - r < 6f) break;
                var ring = Poly.Clean(LeisureKit.Blob(c, r, Hashing.Mix(ctx.Seed, 0x151Au + (uint)k), 14, 0.2f), 0.3f);
                bool ok = ring.Length >= 3 && Poly.IsSimple(ring);
                foreach (var p in ring) ok &= wr.ContainsDisc(p, 3f);
                if (!ok) break;
                islands.Add(new Island { C = c, R = r, Ring = Poly.EnsureCcw(ring) });
            }
            if (o.Islands > islands.Count) ctx.Issue(IssueSeverity.Info, "Placemaker.Pond.Issue.FewerIslands", islands.Count, o.Islands);

            // 3. Basin, islands raised back, water surface around them.
            var anchor = wr.DeepestPoint(out _, (p, cap) =>
            {
                float m = cap;
                foreach (var isl in islands) m = math.min(m, math.distance(p, isl.C) - isl.R - 1f);
                return m;
            }, 16);
            ctx.Plan.Terraform.Add(new PlannedTerraform
            {
                Ring = Poly.To3D(water, ctx.H), Kind = TerraformKind.Lower, TargetHeight = level - o.Depth,
                Permanent = true, WaterLevel = level, WaterAnchor = new float3(anchor.x, level, anchor.y),
            });
            float islandTop = level + 0.8f;
            foreach (var isl in islands)
                ctx.Plan.Terraform.Add(new PlannedTerraform { Ring = Poly.To3D(isl.Ring, ctx.H), Kind = TerraformKind.Raise, TargetHeight = islandTop, Permanent = true });
            float waterArea = Poly.Area(water);
            if (islands.Count == 0) ctx.Surface(water, SurfaceRole.Water, new AssetRef("", Array.Empty<string>()));
            else
            {
                var holes = new List<IReadOnlyList<float2>>();
                foreach (var isl in islands) { holes.Add(isl.Ring); waterArea -= Poly.Area(isl.Ring); }
                try
                {
                    foreach (var r in Poly.Boolean(new[] { (IReadOnlyList<float2>)water }, holes, BoolOp.Difference))
                        foreach (var piece in Poly.CutHoles(r))
                            if (Poly.Area(piece) > 4f) ctx.Surface(piece, SurfaceRole.Water, new AssetRef("", Array.Empty<string>()));
                }
                catch (Exception) { /* the surface is decorative; the terraform makes the water */ }
            }
            ctx.Issue(IssueSeverity.Warning, "Placemaker.Park.Issue.LakePermanent");
            ctx.Stat(Keys.StatLakeArea, Math.Round(waterArea), Keys.UnitM2);
            foreach (var isl in islands) occ.Take(isl.C, isl.R + 0.5f, 8);
            ctx.Check();

            // 4. Promenade dressing: paving strips, the walk loop, benches facing the water, lamps.
            var walk = new List<float2>();
            if (promenade)
            {
                bool paved = AssetAvailability.Has(AssetCategories.SurfacePaving);
                foreach (var strip in outer.BoundaryStrips(o.PromWidth, math.clamp((int)math.round(outer.Perimeter / 60f), 3, 10), ctx.Cancel))
                    if (paved) ctx.Surface(strip, SurfaceRole.Paving, AssetMix.One(""), AssetCategories.SurfacePaving);
                float walkD = hw + 0.6f;
                var loop = Poly.Resample(outer.InsetVertices(walkD), 8f);
                for (int i = 0; i < loop.Length; i++)
                {
                    var a = loop[i]; var b = loop[(i + 1) % loop.Length];
                    if (math.distance(a, b) < 1f) continue;
                    ctx.StraightNet(a, b, NetRole.Pathway, walkAsset);
                    occ.TakeCorridor(a, b, hw, 100);
                    occ.Paths.Add(a, b, hw, 100);
                    walk.Add(a);
                }
                int benches = 0, lamps = 0;
                float benchD = walkD + hw + 0.3f + 1.3f;
                var bench = AssetMix.One("");
                var lampAsset = AssetMix.One("");
                float per = outer.Perimeter;
                if (o.BenchSpacing > 0f && benchD + 1.3f <= o.PromWidth + 0.1f)
                    for (float s = o.BenchSpacing * 0.5f; s < per; s += o.BenchSpacing)
                    {
                        var p0 = outer.PointAt(s, out _, out var inw, out _);
                        var p = p0 + inw * benchD;
                        if (!occ.Free(p, 1.3f, 0.3f, 0.2f)) continue;
                        ctx.Object(p, ShoreKit.Yaw(inw), ObjectRole.Bench, bench, Hashing.Mix(ctx.Seed, 0xBE40u + (uint)benches));
                        occ.Take(p, 1.3f, 1);
                        benches++;
                    }
                if (o.Lamps)
                    for (float s = 0f; s < per; s += 20f)
                    {
                        var p0 = outer.PointAt(s, out _, out var inw, out _);
                        var p = p0 + inw * math.max(walkD + hw + 0.8f, o.PromWidth - 0.7f);
                        if (o.PromWidth - 0.7f < walkD + hw + 0.8f) continue;
                        if (!occ.Free(p, 0.5f, 0.3f, 0.2f)) continue;
                        ctx.Object(p, 0f, ObjectRole.Lamp, lampAsset, Hashing.Mix(ctx.Seed, 0x1A40u + (uint)lamps));
                        occ.Take(p, 0.5f, 2);
                        lamps++;
                    }
                ctx.Stat(Keys.StatBenches, benches, Keys.UnitCount);
                ctx.Plan.Facts["promenadeWidth"] = o.PromWidth;
            }

            // 5. Bridges: island → nearest free shore (on the promenade walk when there is one), and between close islands.
            int bridges = 0;
            if (o.Bridges)
            {
                foreach (var isl in islands)
                {
                    if (Bridge(ctx, occ, reserved, outer, wr, isl, promenade ? walk : null, level, islandTop, walkAsset, links)) bridges++;
                }
                for (int i = 0; i < islands.Count; i++)
                    for (int j = i + 1; j < islands.Count; j++)
                    {
                        var a = islands[i]; var b = islands[j];
                        var dir = math.normalizesafe(b.C - a.C);
                        var pa = a.C + dir * (a.R - 1f); var pb = b.C - dir * (b.R - 1f);
                        if (math.distance(pa, pb) > 35f || math.distance(pa, pb) < 3f) continue;
                        if (occ.Corridors.Overlaps((pa + pb) * 0.5f, 1.5f)) continue;
                        ShoreKit.Net(ctx, pa, pb, islandTop, islandTop, NetRole.Pathway, walkAsset, false, 1.2f);
                        occ.TakeCorridor(pa, pb, hw + 0.3f, 9);
                        bridges++;
                    }
            }
            ctx.Check();

            // 6. Islands: a gazebo or statue on the largest, trees on all.
            int islandTrees = 0;
            for (int k = 0; k < islands.Count; k++)
            {
                var isl = islands[k];
                var ir = LayerRegion.From(isl.Ring);
                if (ir == null) continue;
                var iocc = new Occupancy(ir, null, ctx.Site);
                foreach (var seg in occ.Corridors.Segments) iocc.Corridors.Add(seg.A, seg.B, seg.HalfWidth, seg.Kind);
                if (k == 0 && isl.R >= 8f)
                {
                    string cat = LeisureKit.Category(ctx, "", AssetCategories.Shelter, AssetCategories.CenterpieceStatue, AssetCategories.Centerpiece);
                    if (cat != null && iocc.Free(isl.C, 3f, 0.5f, 0.2f))
                    {
                        var g = ctx.Object(isl.C, Hashing.Unit(Hashing.Mix(ctx.Seed, 0x6A2Eu)) * 6.283f, cat == AssetCategories.Shelter ? ObjectRole.Furniture : ObjectRole.Centerpiece,
                            AssetMix.One(""), Hashing.Mix(ctx.Seed, 0x6A2Fu), -1, cat);
                        g.Position.y = islandTop;
                        iocc.Take(isl.C, 3f, 1);
                    }
                }
                if (!o.IslandTrees || !AssetAvailability.Has(AssetCategories.Tree)) continue;
                int want = math.clamp((int)(Poly.Area(isl.Ring) / 55f), 1, 9);
                for (int t = 0, tries = 0; t < want && tries < want * 12; tries++)
                {
                    uint v = Hashing.Mix(ctx.Seed, 0x7EE0u + (uint)(k * 211 + tries));
                    float ang = Hashing.Unit(v) * 6.283f, rad = math.sqrt(Hashing.Unit(Hashing.Mix(v, 1u))) * isl.R * 0.75f;
                    var p = isl.C + new float2(math.cos(ang), math.sin(ang)) * rad;
                    if (!iocc.Free(p, 2.5f, 1f, 0.3f)) continue;
                    var tree = ctx.Object(p, Hashing.Unit(Hashing.Mix(v, 2u)) * 6.283f, ObjectRole.Tree, AssetMix.One(""), v, 1 + (int)(v % 3u));
                    tree.Position.y = islandTop;
                    iocc.Take(p, 2.5f, 3);
                    occ.Take(p, 2.5f, 3);
                    t++; islandTrees++;
                }
            }

            // 7. Fountain jets in open water.
            int jets = 0;
            if (o.Jets > 0)
            {
                string cat = LeisureKit.Category(ctx, "", AssetCategories.CenterpieceFountain, AssetCategories.WaterJets, AssetCategories.Centerpiece);
                if (cat != null)
                    foreach (var p in JetSpots(wr, islands, anchor, o.Jets, o.JetLayout))
                    {
                        if (!wr.ContainsDisc(p, 4f) || occ.Discs.Overlaps(p, 1.5f, 0.5f) || occ.Corridors.Overlaps(p, 1.5f, 1f)) continue;
                        var j = ctx.Object(p, 0f, ObjectRole.Centerpiece, AssetMix.One(""), Hashing.Mix(ctx.Seed, 0x7E70u + (uint)jets), -1, cat);
                        j.Position.y = level;
                        occ.Take(p, 1.5f, 9);
                        jets++;
                    }
            }

            // 8. Boathouse at the water's edge with a jetty and boats; more rowing boats moored about.
            int boats = 0;
            string boatCat = o.Boathouse || o.Rowboats > 0 ? LeisureKit.Category(ctx, "", AssetCategories.BeachBoat, AssetCategories.ShoreBoat) : null;
            if (o.Boathouse) boats += Boathouse(ctx, occ, reserved, wr, islands, level, boatCat, walkAsset, links, promenade);
            for (int k = 0, tries = 0; boatCat != null && k < o.Rowboats && tries < o.Rowboats * 20; tries++)
            {
                uint v = Hashing.Mix(ctx.Seed, 0xB0D0u + (uint)tries);
                var p = math.lerp(wr.Min, wr.Max, new float2(Hashing.Unit(v), Hashing.Unit(Hashing.Mix(v, 1u))));
                if (!wr.ContainsDisc(p, 3f) || occ.Discs.Overlaps(p, 2.5f, 1f) || occ.Corridors.Overlaps(p, 2.5f, 1f)) continue;
                var b = ctx.Object(p, Hashing.Unit(Hashing.Mix(v, 2u)) * 6.283f, ObjectRole.Furniture, AssetMix.One(""), v, -1, boatCat);
                b.Position.y = level;
                occ.Take(p, 2.5f, 7);
                boats++; k++;
            }

            // 9. "Path to it": the promenade, bridges and boathouse join the park's paths.
            if (o.Connect)
            {
                if (promenade && walk.Count > 0)
                {
                    int joined = 0;
                    var used = new List<float2>();
                    for (int i = 0; i < walk.Count && joined < 3; i += math.max(1, walk.Count / 6))
                    {
                        if (!ShoreKit.NearestReservedPath(reserved, walk[i], o.PromWidth + 14f, out var q)) continue;
                        bool near = false; foreach (var u in used) if (math.distance(u, q) < 25f) near = true;
                        if (near || !ShoreKit.LinkClear(reserved, walk[i], q, 1.2f)) continue;
                        ctx.StraightNet(walk[i], q, NetRole.Pathway, walkAsset);
                        used.Add(q); joined++;
                    }
                }
                foreach (var l in links)
                {
                    if (!ShoreKit.NearestReservedPath(reserved, l, 22f, out var q) || math.distance(l, q) < 1.5f) continue;
                    if (!ShoreKit.LinkClear(reserved, l, q, 1.2f) || Poly.Contains(water, math.lerp(l, q, 0.5f))) continue;
                    ctx.StraightNet(l, q, NetRole.Pathway, walkAsset);
                }
            }

            ctx.Plan.Facts["islands"] = islands.Count;
            ctx.Plan.Facts["bridges"] = bridges;
            ctx.Plan.Facts["jets"] = jets;
            ctx.Plan.Facts["boats"] = boats;
            ctx.Plan.Facts["islandTrees"] = islandTrees;
            ctx.Plan.Facts["lakePromenade"] = promenade ? 1 : 0;
            if (islands.Count > 0) ctx.Stat("Placemaker.Stat.Islands", islands.Count, Keys.UnitCount);
            if (bridges > 0) ctx.Stat("Placemaker.Stat.Bridges", bridges, Keys.UnitCount);
            if (jets > 0) ctx.Stat(Keys.StatFountains, jets, Keys.UnitCount);
            if (boats > 0) ctx.Stat("Placemaker.Stat.Boats", boats, Keys.UnitCount);
        }

        private static bool Bridge(LayerContext ctx, Occupancy occ, ReservedSpace reserved, LayerRegion outer, LayerRegion wr, Island isl,
            List<float2> walk, float level, float islandTop, AssetRef asset, List<float2> links)
        {
            // Candidate landings: walk points (promenade) or just outside the basin, nearest first.
            var cands = new List<(float d, float2 p)>();
            if (walk != null) foreach (var p in walk) cands.Add((math.distance(p, isl.C), p));
            else
            {
                var pts = Poly.Resample(outer.Ring, 4f);
                for (int i = 0; i < pts.Length; i++)
                {
                    var a = pts[i]; var b = pts[(i + 1) % pts.Length];
                    var dir = math.normalizesafe(b - a);
                    var outN = new float2(dir.y, -dir.x);
                    var land = (a + b) * 0.5f + outN * 1.5f;
                    cands.Add((math.distance(land, isl.C), land));
                }
            }
            cands.Sort((x, y) => x.d.CompareTo(y.d));
            float hw = Composer.DefaultHalfWidth(NetRole.Pathway);
            for (int k = 0; k < math.min(40, cands.Count); k++)
            {
                var land = cands[k].p;
                var dir = math.normalizesafe(land - isl.C);
                var start = isl.C + dir * (isl.R - 1f);
                if (math.distance(start, land) > 60f) break;
                if (walk == null && reserved != null && reserved.Blocks(land, 1.5f) && !ShoreKit.NearestReservedPath(reserved, land, 2f, out _)) continue;
                bool clear = true;
                for (int i = 1; i < 10 && clear; i++)
                {
                    var p = math.lerp(start, land, i / 10f);
                    if (math.distance(p, isl.C) > isl.R + hw + 1f && occ.Discs.Overlaps(p, hw, 0.2f)) clear = false;
                }
                if (!clear) continue;
                float landY = ctx.H(land);
                ShoreKit.Net(ctx, start, land, islandTop, landY, NetRole.Pathway, asset, false, 1.2f);
                occ.TakeCorridor(start, land, hw + 0.3f, 9);
                if (walk == null) links.Add(land);
                return true;
            }
            return false;
        }

        private static List<float2> JetSpots(LayerRegion wr, List<Island> islands, float2 anchor, int count, string layout)
        {
            var r = new List<float2>();
            wr.PrincipalAxes(out var centre, out var axis, out float major, out _);
            var c = wr.Contains(centre) ? centre : anchor;
            switch (layout)
            {
                case "ring":
                {
                    float rad = math.max(5f, 2.2f / math.sin(math.PI / math.max(2, count)));
                    for (int i = 0; i < count; i++)
                    {
                        float a = i * 2f * math.PI / count;
                        r.Add(anchor + new float2(math.cos(a), math.sin(a)) * rad);
                    }
                    break;
                }
                case "cluster":
                    r.Add(anchor);
                    for (int i = 1; i < count; i++)
                    {
                        float a = i * 2.39996f;
                        r.Add(anchor + new float2(math.cos(a), math.sin(a)) * 4f * math.sqrt(i));
                    }
                    break;
                default:
                {
                    float spacing = math.clamp(major * 0.6f / math.max(1, count), 5f, 14f);
                    for (int i = 0; i < count; i++) r.Add(c + axis * (i - (count - 1) * 0.5f) * spacing);
                    break;
                }
            }
            r.RemoveAll(p =>
            {
                foreach (var isl in islands) if (math.distance(p, isl.C) < isl.R + 4f) return true;
                return false;
            });
            return r;
        }

        /// <summary>Boathouse on the shore where the bank is free, a jetty into the water, boats tied up. Returns the boats placed.</summary>
        private static int Boathouse(LayerContext ctx, Occupancy occ, ReservedSpace reserved, LayerRegion wr, List<Island> islands, float level,
            string boatCat, AssetRef asset, List<float2> links, bool promenade)
        {
            string cat = LeisureKit.Category(ctx, "", AssetCategories.ShoreBoathouse, AssetCategories.BeachHut, AssetCategories.Shelter);
            var pts = Poly.Resample(wr.Ring, 6f);
            int n = pts.Length;
            uint start = Hashing.Mix(ctx.Seed, 0xB047u) % (uint)math.max(1, n);
            for (int k = 0; k < n; k++)
            {
                int i = (int)((start + k) % n);
                var a = pts[i]; var b = pts[(i + 1) % n];
                var dir = math.normalizesafe(b - a);
                var outN = new float2(dir.y, -dir.x);
                var edge = (a + b) * 0.5f;
                var house = edge + outN * (promenade ? 0.5f : 2.5f);
                var jettyEnd = edge - outN * 10f;
                if (!wr.ContainsDisc(jettyEnd, 3f)) continue;
                bool clear = true;
                foreach (var isl in islands) if (math.distance(jettyEnd, isl.C) < isl.R + 5f) clear = false;
                for (int s = 0; s <= 6 && clear; s++)
                {
                    var p = math.lerp(edge, jettyEnd, s / 6f);
                    if (occ.Discs.Overlaps(p, 2f, 0.3f) || occ.Corridors.Overlaps(p, 1.8f, 0.2f)) clear = false;
                }
                if (!clear) continue;
                if (reserved != null && reserved.Blocks(house, 3f)) continue;
                if (occ.Corridors.Overlaps(house, 3f, 0.2f) || occ.Discs.Overlaps(house, 3f, 0.2f)) continue;
                if (cat != null)
                {
                    ctx.Object(house, ShoreKit.Yaw(-outN), ObjectRole.Furniture, AssetMix.One(""), Hashing.Mix(ctx.Seed, 0xB048u), -1, cat);
                    occ.Take(house, 3f, 1);
                }
                float deck = level + 0.6f;
                ShoreKit.Net(ctx, house - outN * 3.3f, jettyEnd, deck, deck, NetRole.Pathway, asset, false);
                occ.TakeCorridor(edge, jettyEnd, 1.8f, 9);
                links.Add(house + outN * 4f);
                int boats = 0;
                var tan = dir;
                for (int s = 0; boatCat != null && s < 4; s++)
                {
                    float l = 4f + 3f * (s / 2);
                    float sgn = (s & 1) == 0 ? 1f : -1f;
                    var p = edge - outN * l + tan * sgn * 3.6f;
                    if (!wr.ContainsDisc(p, 1.5f) || occ.Discs.Overlaps(p, 1.5f, 0.3f)) continue;
                    var o = ctx.Object(p, ShoreKit.Yaw(-outN), ObjectRole.Furniture, AssetMix.One(""), Hashing.Mix(ctx.Seed, 0xB049u + (uint)s), -1, boatCat);
                    o.Position.y = level;
                    occ.Take(p, 1.5f, 7);
                    boats++;
                }
                ctx.Plan.Facts["boathouse"] = 1;
                return boats;
            }
            ctx.Issue(IssueSeverity.Info, "Placemaker.Pond.Issue.NoBoathouse");
            return 0;
        }
    }
}
