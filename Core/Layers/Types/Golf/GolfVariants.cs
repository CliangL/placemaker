// The two golf formats that are not routed courses.
//  * Mini golf: a wide promenade through the middle, the ticket kiosk at the entrance, and felt lanes
//    packed in staggered rows on both sides — each lane a paved border, a felt surface, a tee mat, a
//    cup with its flag and an obstacle. Themes dress the gaps: adventure (rocks, a pond, dense bushes),
//    garden (flowerbeds and planters), classic (low fence, lamps, benches).
//  * Driving range: a covered tee line of bays across the near end (away from the entrance the range
//    runs), target greens fanning out down the range at growing distances (flag, front bunker,
//    yardage sign), safety netting round the outline and screen planting at the edges only.

using System.Collections.Generic;
using Placemaker.Core.Geometry;
using Placemaker.Core.Layers.Common;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Types.Golf
{
    public sealed partial class GolfLayer
    {
        private void PlanMini(LayerContext ctx)
        {
            var region = ctx.LayerRegion;
            var occ = new Occupancy(region, ctx.Input.Reserved, ctx.Site);
            var gates = Gates(ctx);
            var pathAsset = AssetMix.One(ctx.S("pathAsset"));
            string theme = ctx.S("miniTheme");
            ctx.Surface(region.Ring, SurfaceRole.Grass, AssetMix.One(""));
            LeisureKit.Frame(region, out var centre, out var axis, out var side, out _, out float minor);

            // Promenade on the long axis, shifted sideways when something reserved stands in the way.
            var spine = new List<(float2 a, float2 b)>();
            float whw = Composer.DefaultHalfWidth(NetRole.PathwayWide);
            foreach (float shift in new[] { 0f, 0.18f, -0.18f, 0.32f, -0.32f })
            {
                var through = centre + side * (minor * shift);
                if (!region.Contains(through) || !LeisureKit.Chord(region, through, axis, 3f, out var s0, out var s1)) continue;
                if (!Clear(region, occ, s0, s1, whw)) continue;
                LeisureKit.Path(ctx, occ, s0, s1, NetRole.PathwayWide, pathAsset);
                spine.Add((s0, s1));
                break;
            }
            float phw = Composer.DefaultHalfWidth(NetRole.Pathway);
            foreach (var g in gates)
            {
                float best = float.MaxValue; float2 foot = g.Position;
                foreach (var (a, b) in spine)
                {
                    // The foot of the perpendicular first, then points along the promenade (outlines with notches).
                    var cands = new List<float2> { SegmentIndex.ClosestPoint(g.Position, a, b) };
                    float len = math.distance(a, b);
                    for (float s = 0f; s <= len; s += 6f) cands.Add(math.lerp(a, b, s / math.max(1f, len)));
                    foreach (var q in cands)
                        if (math.distance(q, g.Position) < best && Clear(region, occ, g.Position, q, phw, whw + phw + 1.5f)) { best = math.distance(q, g.Position); foot = q; }
                }
                if (best < float.MaxValue && best > 1f) LeisureKit.Path(ctx, occ, g.Position, foot, NetRole.Pathway, pathAsset);
            }
            ctx.Check();

            // Ticket kiosk beside the path nearest the main entrance.
            float2 home = gates.Count > 0 ? gates[0].Position : region.Centroid;
            if (ctx.B("clubhouse"))
            {
                string kiosk = LeisureKit.Category(ctx, "", KioskChain);
                if (kiosk != null)
                    foreach (var a in PathAnchors.Pick(PathAnchors.Beside(occ, 3f, 5f, 1f), 1, 10f, true))
                        if (LeisureKit.Put(ctx, occ, a.Position, LeisureKit.Yaw(a.Position, a.Toward), ObjectRole.Furniture, AssetMix.One(""), kiosk, 3f,
                            Hashing.Mix(ctx.Seed, 0x1C1u), 0.5f, 0.3f, 0)) home = a.Position;
            }

            // Lanes: long ones first, then shorter ones, then across the axis for narrow sites.
            int wanted = ctx.I("holes");
            var lanes = new List<OrientedRect>();
            foreach (var (len, alongMajor) in new[] { (13f, true), (10f, true), (10f, false), (7.5f, true) })
            {
                if (lanes.Count >= wanted) break;
                lanes.AddRange(LeisureKit.Pack(region, occ, centre, axis, len, 3.6f, 2.4f, 2.6f, wanted - lanes.Count, 1.2f, true, alongMajor));
            }
            // Number them in walking order: along the promenade from the entrance end.
            var start = spine.Count > 0 ? (math.distance(spine[0].a, home) < math.distance(spine[0].b, home) ? spine[0].a : spine[0].b) : home;
            lanes.Sort((x, y) => math.distance(x.Center, start).CompareTo(math.distance(y.Center, start)));
            if (lanes.Count < wanted) ctx.Issue(IssueSeverity.Info, "Placemaker.Golf.Issue.FewerHoles", lanes.Count, wanted);
            ctx.Check();

            var felt = AssetMix.One(ctx.S("greenSurface"));
            string flagCat = ctx.B("flags") ? LeisureKit.Category(ctx, ctx.S("flagAsset"), AssetCategories.GolfFlag) : null;
            string obstacle = AssetAvailability.Has(AssetCategories.GolfMini) ? AssetCategories.GolfMini
                : theme == "adventure" && AssetAvailability.Has(AssetCategories.Rock) ? AssetCategories.Rock
                : theme == "garden" && AssetAvailability.Has(AssetCategories.Planter) ? AssetCategories.Planter : null;
            if (obstacle == null) AssetAvailability.Require(ctx.Plan, AssetCategories.GolfMini, null, Keys.Category(AssetCategories.GolfMini));
            bool marks = AssetAvailability.Has(AssetCategories.DecalMarkings);
            for (int i = 0; i < lanes.Count; i++)
            {
                var r = lanes[i];
                uint v = Hashing.Mix(ctx.Seed, 0x3100u + (uint)i * 37u);
                bool flip = (i & 1) == 1;   // alternate the playing direction so neighbouring lanes read as a course
                float dirSign = flip ? -1f : 1f;
                ctx.Surface(r.Ring, SurfaceRole.Paving, AssetMix.One(""), AssetCategories.SurfacePaving);
                var inner = new OrientedRect { Center = r.Center, Axis = r.Axis, HalfLength = r.HalfLength - 0.6f, HalfWidth = r.HalfWidth - 0.6f };
                ctx.Surface(inner.Ring, SurfaceRole.Grass, felt, AssetCategories.SurfaceGrass);
                if (marks) ctx.Object(r.Local(-dirSign * (r.HalfLength - 1.3f), 0f), r.Yaw, ObjectRole.Decal, AssetMix.One(""), v, -1, AssetCategories.DecalMarkings);
                if (flagCat != null)
                    ctx.Object(r.Local(dirSign * (r.HalfLength - 1.4f), 0f), 0f, ObjectRole.Furniture, AssetMix.One(ctx.S("flagAsset")), v, -1, flagCat);
                if (obstacle != null)
                {
                    float u = (Hashing.Unit(Hashing.Mix(v, 1u)) - 0.4f) * r.HalfLength * 0.5f;
                    ctx.Object(r.Local(u, 0f), r.Yaw + (flip ? math.PI : 0f), ObjectRole.Furniture, AssetMix.One(""), Hashing.Mix(v, 2u), -1, obstacle);
                }
            }
            ctx.Check();

            // Theme dressing in the gaps.
            int deco = 0;
            switch (theme)
            {
                case "adventure":
                {
                    if (Spots.Find(occ, 6f, Spots.Preference.Open, Hashing.Mix(ctx.Seed, 0x90Du), out var pc) && AssetAvailability.Has(AssetCategories.SurfaceWater))
                    {
                        var pond = LeisureKit.Blob(pc, 5f, Hashing.Mix(ctx.Seed, 0x90Eu), 14, 0.25f);
                        if (ctx.Surface(pond, SurfaceRole.Decorative, AssetMix.One(""), AssetCategories.SurfaceWater)) { occ.Take(pc, 6f, 70); deco++; }
                    }
                    if (AssetAvailability.Has(AssetCategories.Rock))
                        for (int i = 0; i < lanes.Count; i++)
                            foreach (var e in new[] { -1f, 1f })
                            {
                                var p = lanes[i].Local(e * (lanes[i].HalfLength + 1.2f), lanes[i].HalfWidth * 0.6f * e);
                                if (LeisureKit.Put(ctx, occ, p, Hashing.Unit(Hashing.Mix(ctx.Seed, (uint)i * 2u + (uint)(e + 1))) * 6.28f, ObjectRole.Furniture,
                                    AssetMix.One(""), AssetCategories.Rock, 0.9f, Hashing.Mix(ctx.Seed, 0x40C0u + (uint)i), 0.3f, 0.2f)) deco++;
                            }
                    break;
                }
                case "garden":
                    if (AssetAvailability.Has(AssetCategories.Flowerbed))
                        for (int i = 0; i < lanes.Count; i++)
                        {
                            var p = lanes[i].Local(0f, lanes[i].HalfWidth + 1.3f);
                            if (LeisureKit.Put(ctx, occ, p, lanes[i].Yaw, ObjectRole.Furniture, AssetMix.One(""), AssetCategories.Flowerbed, 1f,
                                Hashing.Mix(ctx.Seed, 0xF10u + (uint)i), 0.3f, 0.2f)) deco++;
                        }
                    break;
                default:
                    Fence(ctx, gates, AssetMix.One(ctx.S("netAsset")));
                    break;
            }
            PathBenches(ctx, occ, 16f, AssetMix.One(""), 0xBE70u);
            if (ctx.B("lamps")) PathLamps(ctx, occ, 14f, AssetMix.One(""), 0x1A70u);
            PracticeRegion(ctx, occ, home, 6f);
            Planting(ctx, occ, region, null);

            EmitGreenRegions(ctx, occ);
            CommonStats(ctx);
            ctx.Stat("Placemaker.Stat.Holes", lanes.Count, Keys.UnitCount);
            ctx.Plan.Facts["holes"] = lanes.Count;
            ctx.Plan.Facts["decorations"] = deco;
        }

        private void PlanRange(LayerContext ctx)
        {
            var region = ctx.LayerRegion;
            var occ = new Occupancy(region, ctx.Input.Reserved, ctx.Site);
            var gates = Gates(ctx);
            var pathAsset = AssetMix.One(ctx.S("pathAsset"));
            ctx.Surface(region.Ring, SurfaceRole.Grass, AssetMix.One(ctx.S("fairwaySurface")), AssetCategories.SurfaceGrass);
            LeisureKit.Frame(region, out var centre, out var axis, out var side, out _, out float minor);

            // The range runs away from the main entrance, preferably along the long axis. The tee line goes
            // across the near end; on odd outlines (L-shapes) other directions and offsets are tried.
            var g0 = gates.Count > 0 ? gates[0].Position : centre - axis;
            var away = math.dot(g0 - centre, axis) <= 0f ? axis : -axis;
            var awaySide = math.dot(g0 - centre, side) <= 0f ? side : -side;
            OrientedRect strip = default; bool haveStrip = false; int bays = 0;
            float2 dir = away; float maxA = 0f;
            foreach (var cand in new[] { away, awaySide, -awaySide, -away })
            {
                if (haveStrip) break;
                var perp = new float2(-cand.y, cand.x);
                float lo = float.MaxValue, hi = float.MinValue, plo = float.MaxValue, phi = float.MinValue;
                foreach (var p in region.Ring)
                {
                    float a = math.dot(p - centre, cand), b = math.dot(p - centre, perp);
                    lo = math.min(lo, a); hi = math.max(hi, a); plo = math.min(plo, b); phi = math.max(phi, b);
                }
                for (float inset = 9f; inset < 90f && !haveStrip; inset += 4f)
                {
                    if (hi - (lo + inset) < 60f) break;   // a range needs some length
                    foreach (float lat in new[] { 0.5f, 0.3f, 0.7f, 0.15f, 0.85f })
                    {
                        var c = centre + cand * (lo + inset) + perp * math.lerp(plo, phi, lat);
                        if (!region.Contains(c) || !LeisureKit.Chord(region, c, perp, 3f, out var c0, out var c1)) continue;
                        for (int n = math.min(ctx.I("bays"), (int)(math.distance(c0, c1) / 3.2f)); n >= 3 && !haveStrip; n--)
                        {
                            var r = new OrientedRect { Center = math.lerp(c0, c1, 0.5f), Axis = math.normalizesafe(c1 - c0), HalfLength = n * 1.6f, HalfWidth = 3.5f };
                            if (!LeisureKit.RectFits(region, occ, r, 0.5f)) continue;
                            strip = r; haveStrip = true; bays = n; dir = cand; maxA = hi;
                        }
                        if (haveStrip) break;
                    }
                }
            }
            int targets = 0;
            if (!haveStrip) ctx.Issue(IssueSeverity.Warning, "Placemaker.Golf.Issue.NoHoles");
            else
            {
                float vSign = math.dot(strip.Side, dir) > 0f ? 1f : -1f;
                ctx.Surface(strip.Ring, SurfaceRole.Paving, AssetMix.One(""), AssetCategories.SurfacePaving);
                LeisureKit.TakeRect(occ, strip);
                string roof = LeisureKit.Category(ctx, "", AssetCategories.GolfBay, AssetCategories.Shelter);
                bool marks = AssetAvailability.Has(AssetCategories.DecalMarkings);
                float yaw = LeisureKit.Yaw(strip.Center, strip.Center + dir);
                for (int b = 0; b < bays; b++)
                {
                    float u = -strip.HalfLength + 1.6f + b * 3.2f;
                    uint v = Hashing.Mix(ctx.Seed, 0xBA00u + (uint)b);
                    if (marks) ctx.Object(strip.Local(u, 1.6f * vSign), yaw, ObjectRole.Decal, AssetMix.One(""), v, -1, AssetCategories.DecalMarkings);
                    if (roof != null && b % 3 == 1) ctx.Object(strip.Local(u, -0.8f * vSign), yaw, ObjectRole.Furniture, AssetMix.One(""), v, -1, roof);
                    if (b % 2 == 0)
                    {
                        var p = strip.Local(u, -(strip.HalfWidth + 1.8f) * vSign);
                        LeisureKit.Put(ctx, occ, p, LeisureKit.Yaw(p, strip.Center + dir * 10f), ObjectRole.Bench, AssetMix.One(""), null, 1.2f, v, 0.4f, 0.2f);
                    }
                }
                if (ctx.B("lamps"))
                {
                    string light = LeisureKit.Category(ctx, "", AssetCategories.FloodLight, AssetCategories.Lamp);
                    if (light != null)
                        for (float u = -strip.HalfLength; u <= strip.HalfLength + 0.1f; u += math.max(8f, strip.HalfLength))
                        {
                            var p = strip.Local(u, -(strip.HalfWidth + 0.9f) * vSign);
                            LeisureKit.Put(ctx, occ, p, yaw, ObjectRole.Lamp, AssetMix.One(""), light == AssetCategories.Lamp ? null : light, 0.6f,
                                Hashing.Mix(ctx.Seed, 0xF100u + (uint)(u + 500f)), 0.3f, 0.1f);
                        }
                }

                // Target greens fanning out down the range.
                string flagCat = ctx.B("flags") ? LeisureKit.Category(ctx, ctx.S("flagAsset"), AssetCategories.GolfFlag) : null;
                string signCat = AssetAvailability.Has(AssetCategories.GolfTee) ? AssetCategories.GolfTee : null;
                var green = AssetMix.One(ctx.S("greenSurface"));
                var sand = AssetMix.One(ctx.S("bunkerSurface"));
                bool warned = false;
                float runFrom = math.dot(strip.Center - centre, dir);
                float length = maxA - runFrom - 10f;
                int wanted = ctx.I("targets");
                for (int k = 0; k < wanted; k++)
                {
                    float dist = 30f + (length - 40f) * (k + 0.5f) / wanted;
                    float r = math.clamp(3.5f + dist * 0.035f, 3.5f, 12f);
                    float lateral = ((k & 1) == 0 ? 1f : -1f) * minor * (0.08f + 0.12f * Hashing.Unit(Hashing.Mix(ctx.Seed, (uint)k + 0x7A6u)));
                    float2 c = default; bool ok = false;
                    foreach (float shrink in new[] { 1f, 0.5f, 0f })
                    {
                        c = strip.Center + dir * dist + new float2(-dir.y, dir.x) * lateral * shrink;
                        if (region.ContainsDisc(c, r + 5f) && occ.Free(c, r + 1f, r + 5f, 1f)) { ok = true; break; }
                    }
                    if (!ok) continue;
                    uint v = Hashing.Mix(ctx.Seed, 0x7A70u + (uint)k);
                    var ring = LeisureKit.Blob(c, r, v, 16, 0.18f);
                    ctx.Surface(ring, SurfaceRole.Grass, green, AssetCategories.SurfaceGrass);
                    occ.Take(c, r + 1f, GolfRouting.GreenKind);
                    if (flagCat != null) ctx.Object(c, 0f, ObjectRole.Furniture, AssetMix.One(ctx.S("flagAsset")), v, -1, flagCat);
                    if (ctx.I("bunkers") > 0)
                    {
                        var bc = c - dir * (r + r * 0.45f + 1.5f);
                        float br = r * 0.45f;
                        if (occ.Free(bc, br, br + 3f, 0.3f))
                        {
                            var bunker = LeisureKit.Blob(bc, br, Hashing.Mix(v, 9u), 12, 0.25f);
                            if (ctx.Surface(bunker, SurfaceRole.Decorative, sand, AssetAvailability.Has(AssetCategories.SurfaceSand) ? AssetCategories.SurfaceSand : AssetCategories.SurfaceNatural))
                            {
                                Pad(ctx, region, bunker, bc, -ctx.F("bunkerDepth"), ref warned);
                                occ.Take(bc, br, 60);
                            }
                        }
                    }
                    if (signCat != null)
                        LeisureKit.Put(ctx, occ, c + new float2(-dir.y, dir.x) * (r + 2.5f), LeisureKit.Yaw(c, strip.Center), ObjectRole.Furniture,
                            AssetMix.One(""), signCat, 0.6f, Hashing.Mix(v, 4u), 0.5f, 0.2f);
                    targets++;
                }
            }
            ctx.Check();

            // Clubhouse at the entrance, path to the back of the tee line, netting round the outline.
            float2 home = Clubhouse(ctx, occ, gates, 7f, ClubChain, true);
            float phw = Composer.DefaultHalfWidth(NetRole.Pathway);
            void Link(float2 a, float2 b)
            {
                if (math.distance(a, b) > 2f && Clear(region, occ, a, b, phw)) LeisureKit.Path(ctx, occ, a, b, NetRole.Pathway, pathAsset);
            }
            foreach (var g in gates) Link(g.Position, home - math.normalizesafe(home - g.Position) * (7f + 0.6f + phw));
            if (haveStrip)
            {
                float vSign = math.dot(strip.Side, dir) > 0f ? 1f : -1f;
                var back = strip.Local(0f, -(strip.HalfWidth + phw + 0.6f) * vSign);
                Link(home + math.normalizesafe(back - home) * (7f + 0.6f + phw), back);
            }
            Fence(ctx, gates, AssetMix.One(ctx.S("netAsset")));
            PracticeRegion(ctx, occ, home, 8f);
            ctx.Check();

            // Screen planting along the outline only — the range itself stays open.
            Planting(ctx, occ, region, p => region.BoundaryDistance(p, 14f) > 12f);

            EmitGreenRegions(ctx, occ);
            CommonStats(ctx);
            ctx.Stat("Placemaker.Stat.Bays", bays, Keys.UnitCount);
            ctx.Stat("Placemaker.Stat.Targets", targets, Keys.UnitCount);
            ctx.Plan.Facts["bays"] = bays;
            ctx.Plan.Facts["targets"] = targets;
        }
    }
}
