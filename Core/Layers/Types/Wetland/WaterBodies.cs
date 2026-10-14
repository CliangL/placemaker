// Shallow organic water for the wetland and the zen garden (builder 6): ponds grown as chains of
// overlapping jittered discs in the space the paths left free, islands where a pond is wide enough
// to keep a channel around them, path crossings (boardwalks / garden bridges) and the basins.
//
// Engine notes. CS2 paths cannot float over water, so a crossing is a causeway: the strip under the
// path (and the islands) is cut out of the basin and keeps its original height, while the water
// level sits 0.3 m under the lowest bank — the path reads as a boardwalk across the water and the
// water can never spill over it. Each separated basin piece gets its own terraform + water source
// at the shared level. Basins are a permanent terrain change (warning issue, Permanent = true).
// Every pond keeps EdgeMargin from the outline, so the cut is far inside and the outline's ground
// never changes.

using System;
using System.Collections.Generic;
using Placemaker.Core.Geometry;
using Placemaker.Core.Layers.Common;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Types.Wetland
{
    public sealed class Pond
    {
        public float2[] Ring;
        public float2 Min, Max;
        public float Level;
        public float Area;
        public readonly List<float2[]> Islands = new List<float2[]>();

        public Pond(float2[] ring)
        {
            Ring = ring;
            Poly.Bounds(ring, out Min, out Max);
            Area = Poly.Area(ring);
        }

        public bool Near(float2 p, float margin) =>
            p.x >= Min.x - margin && p.y >= Min.y - margin && p.x <= Max.x + margin && p.y <= Max.y + margin;
    }

    public struct GrowSpec
    {
        public int Ponds;
        /// <summary>Total wanted water area (m²).</summary>
        public float TargetArea;
        public float BlobRadius, MinRadius;
        /// <summary>Clearance from the outline / from paths and placed things / between ponds.</summary>
        public float EdgeMargin, PathMargin, PondGap;
        /// <summary>0 = straight chains, 1 = wandering.</summary>
        public float Wander;
        /// <summary>Blob outline jitter (0.1 round … 0.35 ragged).</summary>
        public float Jitter;
        /// <summary>Chains follow Axis (swales).</summary>
        public bool Linear;
        public float2 Axis;
    }

    public sealed class WaterBodies
    {
        public readonly List<Pond> Ponds = new List<Pond>();
        /// <summary>Causeway strips and platforms kept dry inside the ponds (CCW rings).</summary>
        public readonly List<float2[]> Dry = new List<float2[]>();

        /// <summary>True when p lies in open water (not on an island), or within <paramref name="margin"/> of it.</summary>
        public bool InWater(float2 p, float margin = 0f)
        {
            foreach (var pond in Ponds)
            {
                if (!pond.Near(p, margin)) continue;
                if (Poly.Contains(pond.Ring, p))
                {
                    bool island = false;
                    foreach (var isl in pond.Islands)
                        if (Poly.Contains(isl, p) && Poly.DistanceToBoundary(isl, p) >= margin) { island = true; break; }
                    if (!island) return true;
                    continue;
                }
                if (margin > 0f && Poly.DistanceToBoundary(pond.Ring, p) < margin) return true;
            }
            return false;
        }

        /// <summary>True when p is on an island (with margin from its shore).</summary>
        public bool OnIsland(float2 p, float margin = 0f)
        {
            foreach (var pond in Ponds)
            {
                if (!pond.Near(p, 0f)) continue;
                foreach (var isl in pond.Islands)
                    if (Poly.Contains(isl, p) && Poly.DistanceToBoundary(isl, p) >= margin) return true;
            }
            return false;
        }

        /// <summary>Distance to the nearest shore (0 inside water), capped.</summary>
        public float Distance(float2 p, float cap)
        {
            float best = cap;
            foreach (var pond in Ponds)
            {
                if (!pond.Near(p, best)) continue;
                if (Poly.Contains(pond.Ring, p)) return 0f;
                best = math.min(best, Poly.DistanceToBoundary(pond.Ring, p));
            }
            return best;
        }

        public float TotalArea
        {
            get
            {
                float a = 0f;
                foreach (var p in Ponds) { a += p.Area; foreach (var i in p.Islands) a -= Poly.Area(i); }
                return a;
            }
        }

        // ───────────────────────────── growing ─────────────────────────────

        private struct Blob { public float2 C; public float R; public int Pond; }

        /// <summary>
        /// Grows up to spec.Ponds ponds in the free space of <paramref name="occ"/> (paths, objects,
        /// reserved space) and adds them (with their water level) to this set. Returns how many grew.
        /// </summary>
        public int Grow(LayerContext ctx, Occupancy occ, GrowSpec spec, uint seed, Func<float2, bool> forbidden = null)
        {
            var region = occ.LayerRegion;
            var rng = Hashing.Rng(seed, 0x9E7Du);
            var blobs = new List<Blob>();
            foreach (var p in Ponds) blobs.Add(new Blob { C = (p.Min + p.Max) * 0.5f, R = math.cmax(p.Max - p.Min) * 0.5f, Pond = -1 });
            float perPond = spec.TargetArea / math.max(1, spec.Ponds);
            float j = math.clamp(spec.Jitter, 0.05f, 0.4f);
            int grown = 0;

            bool Fits(float2 c, float r, int pond)
            {
                float reach = r * (1f + j);
                if (region.BoundaryDistance(c, reach + spec.EdgeMargin + 0.01f) < reach + spec.EdgeMargin) return false;
                if (occ.Corridors.Overlaps(c, reach, spec.PathMargin)) return false;
                if (occ.Discs.Overlaps(c, reach, spec.PathMargin * 0.5f)) return false;
                if (occ.HitsPolygon(c, reach + spec.PathMargin)) return false;
                if (forbidden != null && forbidden(c)) return false;
                foreach (var b in blobs)
                {
                    if (b.Pond == pond) continue;
                    float d = reach + b.R * (1f + j) + spec.PondGap;
                    if (math.distancesq(b.C, c) < d * d) return false;
                }
                return true;
            }

            for (int k = 0; k < spec.Ponds; k++)
            {
                ctx.Check();
                int id = 1000 + k;
                float r0 = math.max(spec.MinRadius, spec.BlobRadius * rng.NextFloat(0.8f, 1.2f));
                float2 start = default; bool found = false;
                for (int attempt = 0; attempt < 5 && !found; attempt++)
                {
                    var avoid = new List<float2>();
                    foreach (var b in blobs) avoid.Add(b.C);
                    float reach = r0 * (1f + j);
                    if (Spots.Find(occ, reach + spec.PathMargin, Spots.Preference.Open, Hashing.Mix(seed, (uint)(k * 7 + attempt)), out var c,
                            avoid, reach * 2f + spec.PondGap, math.max(0.5f, spec.EdgeMargin - spec.PathMargin), 900)
                        && Fits(c, r0, id))
                    { start = c; found = true; break; }
                    r0 *= 0.72f;
                    if (r0 < spec.MinRadius) break;
                }
                if (!found) continue;

                var mine = new List<float2[]>();
                float est = 0f;
                float2 dir = spec.Linear ? spec.Axis * (rng.NextBool() ? 1f : -1f) : Rotate(new float2(1, 0), rng.NextFloat(0f, 6.283f));
                var cur = start; float r = r0;
                void AddBlob(float2 c, float rr)
                {
                    blobs.Add(new Blob { C = c, R = rr, Pond = id });
                    mine.Add(Poly.EnsureCcw(LeisureKit.Blob(c, rr, Hashing.Mix(seed, (uint)(blobs.Count * 31 + k)), 14, j)));
                    est += math.PI * rr * rr * (mine.Count == 1 ? 1f : 0.5f);
                }
                AddBlob(cur, r);
                int fails = 0, steps = 0, reversals = 0;
                var origin = start; var originDir = dir;
                while (est < perPond && steps < 48 && fails < 1)
                {
                    steps++;
                    float turn = (rng.NextFloat() * 2f - 1f) * spec.Wander * math.PI * 0.5f;
                    dir = Rotate(dir, turn);
                    if (spec.Linear) dir = math.normalizesafe(math.lerp(dir, spec.Axis * math.sign(math.dot(dir, spec.Axis) + 1e-3f), 0.7f), spec.Axis);
                    bool ok = false;
                    foreach (float da in new[] { 0f, 0.5f, -0.5f, 1f, -1f, 1.6f, -1.6f })
                    {
                        foreach (float shrink in new[] { 1f, 0.75f, 0.55f })
                        {
                            float rr = math.max(spec.MinRadius, r0 * shrink * (0.8f + 0.35f * Hashing.Unit(Hashing.Mix(seed, (uint)(steps * 13 + k)))));
                            var d2 = Rotate(dir, da);
                            var next = cur + d2 * (r + rr) * 0.55f;
                            if (!Fits(next, rr, id)) continue;
                            cur = next; dir = d2; r = rr; AddBlob(cur, rr); ok = true;
                            break;
                        }
                        if (ok) break;
                    }
                    if (!ok)
                    {
                        // Dead end: grow the other way from the start once.
                        if (reversals++ == 0) { cur = origin; dir = -originDir; r = r0; continue; }
                        fails++;
                    }
                }

                var ring = Merge(mine);
                if (ring == null) continue;
                var pond = new Pond(ring);
                float shore = float.MaxValue;
                foreach (var p in Poly.Resample(ring, 4f)) shore = math.min(shore, ctx.H(p));
                if (!math.isfinite(shore)) shore = ctx.H(ring[0]);
                pond.Level = shore - 0.3f;
                Ponds.Add(pond);
                grown++;
            }
            return grown;
        }

        /// <summary>Union of the blobs, lightly smoothed; null when nothing usable remains.</summary>
        private static float2[] Merge(List<float2[]> blobs)
        {
            if (blobs.Count == 0) return null;
            List<Region> merged;
            try { merged = Poly.UnionAll(blobs); } catch (Exception) { return null; }
            if (merged.Count == 0 || merged[0].Outer == null || merged[0].Outer.Length < 3) return null;
            var outer = merged[0].Outer;   // largest; enclosed dry pockets become water (they are tiny)
            var smooth = Poly.Clean(Poly.Chaikin(outer, 2), 0.4f);
            var ring = smooth.Length >= 3 && Poly.IsSimple(smooth) ? smooth : Poly.Clean(outer, 0.4f);
            if (ring.Length < 3 || Poly.Area(ring) < 20f || !Poly.IsSimple(ring)) return null;
            return Poly.EnsureCcw(ring);
        }

        private static float2 Rotate(float2 v, float a) => new float2(v.x * math.cos(a) - v.y * math.sin(a), v.x * math.sin(a) + v.y * math.cos(a));

        // ───────────────────────────── islands ─────────────────────────────

        /// <summary>
        /// Adds up to <paramref name="count"/> islands over the ponds (largest pond first, round robin),
        /// each keeping an open channel to the shore and clear of crossings. Returns how many were added.
        /// </summary>
        public int AddIslands(int count, float channel, float maxRadius, uint seed, SegmentIndex crossings)
        {
            int added = 0;
            if (count <= 0 || Ponds.Count == 0) return 0;
            var order = new List<Pond>(Ponds);
            order.Sort((a, b) => b.Area.CompareTo(a.Area));
            for (int round = 0; round < count && added < count; round++)
            {
                bool any = false;
                foreach (var pond in order)
                {
                    if (added >= count) break;
                    if (!AddIsland(pond, channel, maxRadius, Hashing.Mix(seed, (uint)(round * 17 + added)), crossings)) continue;
                    added++; any = true;
                }
                if (!any) break;
            }
            return added;
        }

        private static bool AddIsland(Pond pond, float channel, float maxRadius, uint seed, SegmentIndex crossings)
        {
            const float jitter = 0.22f;
            float step = math.max(1.5f, math.sqrt(pond.Area / 900f));
            float best = 0f; float2 at = default;
            for (float y = pond.Min.y + step * 0.5f; y < pond.Max.y; y += step)
                for (float x = pond.Min.x + step * 0.5f; x < pond.Max.x; x += step)
                {
                    var p = new float2(x, y);
                    if (!Poly.Contains(pond.Ring, p)) continue;
                    float room = Poly.DistanceToBoundary(pond.Ring, p) - channel;
                    if (crossings != null && crossings.Count > 0) room = math.min(room, crossings.Clearance(p, room + 1f) - 2f);
                    foreach (var isl in pond.Islands)
                    {
                        var ic = Poly.Centroid(isl);
                        float ir = math.sqrt(Poly.Area(isl) / math.PI) * (1f + jitter);
                        room = math.min(room, math.distance(p, ic) - ir - channel);
                    }
                    float r = math.min(maxRadius, room / (1f + jitter));
                    r += Hashing.Unit(Hashing.Mix(seed, (int)(x * 3f), (int)(y * 3f))) * 0.05f;
                    if (r > best) { best = r; at = p; }
                }
            if (best < 2.2f) return false;
            var blob = Poly.EnsureCcw(LeisureKit.Blob(at, best, seed, 12, jitter));
            if (!Poly.IsSimple(blob)) return false;
            pond.Islands.Add(blob);
            return true;
        }

        // ───────────────────────────── crossings ─────────────────────────────

        /// <summary>
        /// A crossing of <paramref name="pond"/> from path to path through its widest part (or a spur
        /// from one path to a platform in the middle when only one side reaches a path). Polyline
        /// from the first path foot to the last point; null when nothing works.
        /// </summary>
        public List<float2> Crossing(Pond pond, Occupancy occ, float maxApproach, bool zigzag, uint seed, out bool spur, IReadOnlyList<float2> avoidNear = null)
        {
            spur = false;
            var region = occ.LayerRegion;
            var shape = LayerRegion.From(pond.Ring);
            if (shape == null) return null;
            var c = shape.DeepestPoint(out float inner, null, 12);
            float reach = math.length(pond.Max - pond.Min) + 2f;
            float phase = Hashing.Unit(seed) * math.PI;
            List<float2> best = null; float bestScore = float.MaxValue; bool bestSpur = false;
            for (int i = 0; i < 12; i++)
            {
                float a = phase + i * math.PI / 12f;
                var dir = new float2(math.cos(a), math.sin(a));
                var pieces = Poly.ClipSegmentToPolygon(pond.Ring, c - dir * reach, c + dir * reach);
                bool got = false; float2 sA = c, sB = c;
                foreach (var (p0, p1) in pieces)
                    if (SegmentIndex.DistSq(c, p0, p1) < 0.25f) { sA = p0; sB = p1; got = true; break; }
                if (!got) continue;
                var d = math.normalizesafe(sB - sA);
                bool hitA = Approach(region, occ, sA, -d, maxApproach, out var footA, out float lenA);
                bool hitB = Approach(region, occ, sB, d, maxApproach, out var footB, out float lenB);
                float chord = math.distance(sA, sB);
                float avoidPenalty = 0f;
                if (avoidNear != null)
                    foreach (var q in avoidNear)
                        avoidPenalty += math.max(0f, 25f - math.sqrt(SegmentIndex.DistSq(q, sA, sB))) * 2f;
                if (hitA && hitB)
                {
                    float score = chord * 0.3f + lenA + lenB + avoidPenalty;
                    if (bestSpur || score < bestScore)
                    {
                        bestScore = score; bestSpur = false;
                        best = Line(footA, sA - d * 1f, sB + d * 1f, footB, pond, zigzag);
                    }
                }
                else if ((hitA || hitB) && (best == null || bestSpur) && inner > 4f)
                {
                    float score = 1000f + (hitA ? lenA : lenB) + avoidPenalty;
                    if (score < bestScore)
                    {
                        bestScore = score; bestSpur = true;
                        best = hitA ? new List<float2> { footA, sA - d * 1f, c } : new List<float2> { footB, sB + d * 1f, c };
                    }
                }
            }
            spur = bestSpur;
            return best;
        }

        /// <summary>Walks from a shore point outwards until it meets a path (within maxApproach), over dry land only.</summary>
        private bool Approach(LayerRegion region, Occupancy occ, float2 shore, float2 dir, float maxApproach, out float2 foot, out float length)
        {
            foot = shore; length = 0f;
            for (float t = 1f; t <= maxApproach; t += 1f)
            {
                var q = shore + dir * t;
                if (!region.ContainsDisc(q, 1.5f)) return false;
                if (t > 1.5f && InWater(q)) return false;
                if (occ.Paths.Nearest(q, 1.5f, out var f) >= 0)
                {
                    foot = f; length = t;
                    return true;
                }
            }
            return false;
        }

        private static List<float2> Line(float2 footA, float2 a, float2 b, float2 footB, Pond pond, bool zigzag)
        {
            var pts = new List<float2> { footA, a };
            float len = math.distance(a, b);
            if (zigzag && len > 14f)
            {
                int n = math.clamp((int)(len / 9f), 2, 7);
                var d = (b - a) / len;
                var side = new float2(-d.y, d.x);
                float amp = math.min(2.5f, len * 0.07f);
                for (int i = 1; i < n; i++)
                {
                    var p = math.lerp(a, b, i / (float)n) + side * ((i & 1) == 0 ? -amp : amp);
                    if (Poly.Contains(pond.Ring, p) && Poly.DistanceToBoundary(pond.Ring, p) > 1.5f) pts.Add(p);
                    else pts.Add(math.lerp(a, b, i / (float)n));
                }
            }
            pts.Add(b);
            pts.Add(footB);
            return pts;
        }

        /// <summary>Keeps a causeway strip dry under every segment of a crossing polyline that touches water.</summary>
        public void KeepDry(IReadOnlyList<float2> polyline, float halfWidth)
        {
            for (int i = 1; i < polyline.Count; i++)
            {
                var a = polyline[i - 1]; var b = polyline[i];
                float len = math.distance(a, b);
                if (len < 0.2f) continue;
                var axis = (b - a) / len;
                Dry.Add(Poly.EnsureCcw(LayerRegion.Rect((a + b) * 0.5f, axis, len * 0.5f + halfWidth, halfWidth)));
            }
        }

        public void KeepDryDisc(float2 c, float r) => Dry.Add(Poly.EnsureCcw(LayerRegion.Circle(c, r, 16)));

        // ───────────────────────────── emission ─────────────────────────────

        /// <summary>
        /// Emits the basins: per dry-separated piece one terraform with a water source (the shallow
        /// shelf), plus a deeper inner terraform when <paramref name="shelfWidth"/> leaves room.
        /// Returns the number of basin pieces.
        /// </summary>
        public int EmitBasins(LayerContext ctx, float depth, float shelfDepth, float shelfWidth)
        {
            int pieces = 0;
            foreach (var pond in Ponds)
            {
                ctx.Check();
                var clip = new List<IReadOnlyList<float2>>();
                foreach (var isl in pond.Islands) clip.Add(isl);
                foreach (var d in Dry)
                {
                    Poly.Bounds(d, out var mn, out var mx);
                    if (mx.x < pond.Min.x || mx.y < pond.Min.y || mn.x > pond.Max.x || mn.y > pond.Max.y) continue;
                    clip.Add(d);
                }
                List<float2[]> rings = new List<float2[]>();
                if (clip.Count == 0) rings.Add(pond.Ring);
                else
                {
                    List<Region> regions;
                    try { regions = Poly.Boolean(new[] { (IReadOnlyList<float2>)pond.Ring }, clip, BoolOp.Difference); }
                    catch (Exception) { regions = new List<Region> { new Region(pond.Ring) }; }
                    foreach (var r in regions)
                        foreach (var piece in r.HasHoles ? Poly.CutHoles(r) : new List<float2[]> { r.Outer })
                            rings.Add(piece);
                }
                foreach (var ring in rings)
                {
                    var clean = Poly.Clean(ring, 0.2f);
                    if (clean.Length < 3 || Poly.Area(clean) < 12f) continue;
                    var lr = LayerRegion.From(clean);
                    var anchor = lr != null ? lr.DeepestPoint(out _, null, 12) : Poly.Centroid(clean);
                    bool shelf = shelfWidth > 0.2f && depth > shelfDepth + 0.1f;
                    ctx.Plan.Terraform.Add(new PlannedTerraform
                    {
                        Ring = Poly.To3D(clean, ctx.H), Kind = TerraformKind.Lower,
                        TargetHeight = pond.Level - (shelf ? shelfDepth : depth), Permanent = true,
                        WaterLevel = pond.Level, WaterAnchor = new float3(anchor.x, pond.Level, anchor.y),
                    });
                    pieces++;
                    if (!shelf) continue;
                    foreach (var deep in Poly.Offset(clean, shelfWidth))
                    {
                        if (deep == null || deep.Length < 3 || Poly.Area(deep) < 8f) continue;
                        ctx.Plan.Terraform.Add(new PlannedTerraform
                        {
                            Ring = Poly.To3D(deep, ctx.H), Kind = TerraformKind.Lower, TargetHeight = pond.Level - depth, Permanent = true,
                        });
                    }
                }
            }
            return pieces;
        }

        /// <summary>Hands each pond to the composer as a claimed place so features keep out of the water.</summary>
        public void EmitPlaces(LayerContext ctx, string tag)
        {
            for (int i = 0; i < Ponds.Count; i++) ctx.AddRegion(Ponds[i].Ring, "water:" + i, tag);
        }

        /// <summary>
        /// Shore points every <paramref name="spacing"/> m with the outward normal (towards dry land),
        /// of the ponds and (outward = into the water) of the islands.
        /// </summary>
        public List<(float2 p, float2 outward, bool island)> Shore(float spacing)
        {
            var r = new List<(float2, float2, bool)>();
            foreach (var pond in Ponds)
            {
                AddShore(pond.Ring, spacing, false, r);
                foreach (var isl in pond.Islands) AddShore(isl, spacing, true, r);
            }
            return r;
        }

        private static void AddShore(float2[] ring, float spacing, bool island, List<(float2, float2, bool)> into)
        {
            var pts = Poly.Resample(ring, spacing);
            int n = pts.Length;
            if (n < 3) return;
            for (int i = 0; i < n; i++)
            {
                var t = math.normalizesafe(pts[(i + 1) % n] - pts[(i + n - 1) % n]);
                into.Add((pts[i], new float2(t.y, -t.x), island));
            }
        }
    }
}
