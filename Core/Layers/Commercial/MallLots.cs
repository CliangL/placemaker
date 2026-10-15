// s50 — Lot first. A commercial site is a parking lot with shops round it, not a park with shops beside it: what the
// type's own composition leaves over becomes more lot (planned on the leftover piece itself, whatever its shape),
// reached from the street or a lane, and the street frontage in front of it carries shop rows with an access alley
// (shops on both sides) running through to the lot. Landscaping is the lot's own (islands, edges, planted strips); a
// park is only planted where the type has one by design or the "Green" extra is on.

using System;
using System.Collections.Generic;
using Placemaker.Core.Geometry;
using Placemaker.Core.Layers.Common;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Commercial
{
    public sealed partial class Mall
    {
        /// <summary>Boundary samples within this distance of a street's kerb count as street contact (the outline sits ~2 m off it).</summary>
        private const float StreetContact = 6.5f;
        /// <summary>… and within this of a lane's edge as lane contact.</summary>
        private const float LaneContact = 3.5f;

        /// <summary>
        /// Where a leftover piece can be entered from: runs of its outline that lie along a street ("entrance") or a lane of the
        /// mall ("alley"), the middle of each (two on a long run), longest first. A piece with none is shut in by shops.
        /// </summary>
        public List<(float2 at, string kind)> Entries(float2[] piece, int max = 4)
        {
            var roads = _roads ?? (_roads = StreetFrontage.Roads(Ctx.Input));
            var pts = new List<float2>();
            var kinds = new List<int>();
            for (int i = 0; i < piece.Length; i++)
            {
                float2 a = piece[i], b = piece[(i + 1) % piece.Length];
                int n = math.max(1, (int)math.ceil(math.distance(a, b) / 2f));
                for (int s = 0; s < n; s++)
                {
                    var p = math.lerp(a, b, (float)s / n);
                    pts.Add(p);
                    kinds.Add(Classify(roads, p));
                }
            }
            var found = new List<(float len, float2 at, float2 at2, string kind)>();
            int count = pts.Count;
            if (count == 0) return new List<(float2, string)>();
            // Start at a sample whose kind differs from its predecessor's (so no run wraps the start).
            int first = 0;
            for (int i = 0; i < count; i++) if (kinds[i] != kinds[(i + count - 1) % count]) { first = i; break; }
            int k0 = 0;
            while (k0 < count)
            {
                int kind = kinds[(first + k0) % count];
                int k1 = k0;
                while (k1 + 1 < count && kinds[(first + k1 + 1) % count] == kind) k1++;
                if (kind != 0)
                {
                    float len = (k1 - k0 + 1) * 2f;
                    if (len >= (kind == 1 ? 12f : 5f))
                    {
                        var m = pts[(first + (k0 + k1) / 2) % count];
                        var q1 = pts[(first + k0 + (k1 - k0) / 4) % count];
                        var q2 = pts[(first + k0 + (k1 - k0) * 3 / 4) % count];
                        found.Add((len, m, kind == 1 && len > 90f ? q1 : m, kind == 1 ? "entrance" : "alley"));
                        if (kind == 1 && len > 90f) found.Add((len, q2, q2, "entrance"));
                    }
                }
                k0 = k1 + 1;
            }
            found.Sort((x, y) => y.len.CompareTo(x.len));
            var list = new List<(float2, string)>();
            foreach (var f in found)
            {
                if (list.Count >= max) break;
                bool near = false;
                foreach (var (p, _) in list) if (math.distance(p, f.at2) < 14f) near = true;
                if (!near) list.Add((f.at2, f.kind));
            }
            return list;
        }

        private int Classify(List<StreetFrontage.RoadLine> roads, float2 p)
        {
            foreach (var l in Lanes)
                for (int i = 0; i + 1 < l.Points.Count; i++)
                    if (math.sqrt(SegmentIndex.DistSq(p, l.Points[i], l.Points[i + 1])) - l.Half <= LaneContact) return 2;
            foreach (var r in roads)
                if (math.sqrt(SegmentIndex.DistSq(p, r.A, r.B)) - r.HalfWidth <= StreetContact) return 1;
            return 0;
        }


        /// <summary>Direction of the outline at a boundary point (the chord between the points 10 m either side along it).</summary>
        public static float2 TangentAt(float2[] ring, float2 p)
        {
            Poly.ClosestPointOnBoundary(ring, p, out int seg, out float t);
            float2 Walk(int s, float tt, float dist, int dir)
            {
                float2 a = ring[s], b = ring[(s + 1) % ring.Length];
                float2 q = math.lerp(a, b, tt);
                float left = dist;
                int cur = s;
                float2 pos = q;
                for (int guard = 0; guard < ring.Length + 2; guard++)
                {
                    float2 target = dir > 0 ? ring[(cur + 1) % ring.Length] : ring[cur];
                    float d = math.distance(pos, target);
                    if (d >= left) return pos + math.normalizesafe(target - pos) * left;
                    left -= d; pos = target;
                    cur = dir > 0 ? (cur + 1) % ring.Length : (cur + ring.Length - 1) % ring.Length;
                }
                return pos;
            }
            var fwd = Walk(seg, t, 10f, 1); var back = Walk(seg, t, 10f, -1);
            return math.normalizesafe(fwd - back, new float2(1f, 0f));
        }

        /// <summary>Lot ideal: share of its land that is stalls (a stall with its aisle share takes ~26 m²); below this the lot reads as a lawn with a few bays.</summary>
        private const float GoodFill = 0.3f;
        /// <summary>The parts of the piece that are wider than 2 × <paramref name="r"/> (arms, notches, slivers and necks dropped), at least 300 m², largest first.</summary>
        public static List<float2[]> Smooth(float2[] piece, float r = 3.5f)
        {
            var list = new List<float2[]>();
            try
            {
                var core = Poly.Offset(Poly.Offset(piece, r, OffsetJoin.Miter), -r, OffsetJoin.Miter);
                foreach (var c in core)
                {
                    if (c?.Outer == null || c.Outer.Length < 3) continue;
                    var ring = Poly.EnsureCcw(Poly.Clean(c.Outer, 0.3f));
                    if (ring.Length >= 3 && Poly.Area(ring) >= 200f && c.Holes.Count == 0) list.Add(ring);
                }
            }
            catch (Exception) { }
            list.Sort((x, y) => Poly.Area(y).CompareTo(Poly.Area(x)));
            return list;
        }

        /// <summary>
        /// A parking lot on a leftover piece of the site, entered from the street or lane it touches. The piece is smoothed
        /// first (the planner folds on notches and arms thinner than its setback; a neck splits it in two), and its rows are
        /// laid parallel and across the entrance's own edge as well as along its longest straight run (on a curved piece that
        /// may be nowhere near the entrance); the variant with the most stalls is kept. False when nothing it touches can
        /// enter it or no variant holds a useful number of stalls.
        /// </summary>
        public bool PolyLot(float2[] piece, SettingsBag parking, uint salt)
        {
            var shapes = new List<float2[]>();
            foreach (var clear in KeepOffPlots(piece))
            {
                shapes.AddRange(Smooth(clear, 2f));
                foreach (var s35 in Smooth(clear)) if (shapes.Count < 5) shapes.Add(s35);
            }
            for (int si = 0; si < shapes.Count && si < 5; si++)
            {
                LotTry best = null; float bestScore = 0f;
                var shape = shapes[si];
                var entries = Entries(shape);
                if (entries.Count == 0) continue;
                var tan = TangentAt(shape, entries[0].at);
                float ang = math.degrees(math.atan2(tan.y, tan.x));
                foreach (float rot in new[] { float.NaN, 0f, 90f })
                {
                    var bag = new SettingsBag();
                    foreach (var kv in parking.Values) bag.Values[kv.Key] = kv.Value;
                    // Flush to the leftover piece: a surface 1 m in from a cell block would leave a strip the game cannot use (s48 snug rule).
                    bag.Values["setback"] = 0.25;
                    if (!float.IsNaN(rot))
                    {
                        bag.Values["rowAngleMode"] = "fixed";
                        bag.Values["rowAngle"] = (double)(ang + rot);
                    }
                    var t = PlanLot(shape, entries, bag, salt + (uint)(si * 3), true);
                    if (t == null || NetFit.Violations(t.Sub.Nets, t.Site).Count > 0) continue;   // a connector the game would fold (InvalidShape)
                    float fill = t.Capacity * 26f / math.max(1f, Poly.Area(t.Ring));
                    float score = t.Capacity * math.min(1f, fill / GoodFill);
                    if (score > bestScore) { bestScore = score; best = t; }
                }
                if (best != null && best.Capacity >= 4f) return CommitLot(best, true);
            }
            return false;
        }

        /// <summary>
        /// The land nothing has taken, as simple rings (largest first, each at least <paramref name="minArea"/>). A piece that
        /// wraps something (a lot round an island of plots) is cut in two through the hole — a clean seam, not a slit — so each
        /// half is a plain polygon a lot can be planned on.
        /// </summary>
        public List<float2[]> RestPieces(float minArea)
        {
            var list = new List<float2[]>();
            if (Taken.Count == 0) { list.Add(Region.Ring); return list; }
            var clip = new List<IReadOnlyList<float2>>();
            foreach (var t in Taken) clip.Add(Poly.EnsureCcw(t));
            List<Region> pieces;
            try { pieces = Poly.Boolean(new[] { (IReadOnlyList<float2>)Region.Ring }, clip, BoolOp.Difference); }
            catch (Exception) { return list; }
            var queue = new Queue<(Region r, int depth)>();
            foreach (var p in pieces) queue.Enqueue((p, 0));
            while (queue.Count > 0)
            {
                var (p, depth) = queue.Dequeue();
                if (p?.Outer == null || p.Outer.Length < 3) continue;
                if (p.Holes.Count == 0 || depth >= 6)
                {
                    if (p.Holes.Count > 0) continue;
                    var ring = Poly.EnsureCcw(Poly.Clean(p.Outer, 0.05f));
                    if (ring.Length >= 3 && Poly.Area(ring) >= minArea) list.Add(ring);
                    continue;
                }
                // Cut across the hole's centroid, along the piece's longer side.
                var hc = Poly.Centroid(p.Holes[0]);
                float2 lo = new float2(float.MaxValue), hi = new float2(float.MinValue);
                foreach (var v in p.Outer) { lo = math.min(lo, v); hi = math.max(hi, v); }
                bool vertical = (hi.x - lo.x) >= (hi.y - lo.y);
                float2[] a, b;
                if (vertical)
                {
                    a = new[] { new float2(lo.x - 1f, lo.y - 1f), new float2(hc.x, lo.y - 1f), new float2(hc.x, hi.y + 1f), new float2(lo.x - 1f, hi.y + 1f) };
                    b = new[] { new float2(hc.x, lo.y - 1f), new float2(hi.x + 1f, lo.y - 1f), new float2(hi.x + 1f, hi.y + 1f), new float2(hc.x, hi.y + 1f) };
                }
                else
                {
                    a = new[] { new float2(lo.x - 1f, lo.y - 1f), new float2(hi.x + 1f, lo.y - 1f), new float2(hi.x + 1f, hc.y), new float2(lo.x - 1f, hc.y) };
                    b = new[] { new float2(lo.x - 1f, hc.y), new float2(hi.x + 1f, hc.y), new float2(hi.x + 1f, hi.y + 1f), new float2(lo.x - 1f, hi.y + 1f) };
                }
                var holes = new List<IReadOnlyList<float2>>();
                foreach (var h in p.Holes) holes.Add(Poly.EnsureCcw(h));
                foreach (var half in new[] { a, b })
                {
                    try
                    {
                        var cut = Poly.Boolean(new[] { (IReadOnlyList<float2>)p.Outer }, new[] { (IReadOnlyList<float2>)half }, BoolOp.Intersection);
                        foreach (var q in cut)
                        {
                            var rest = Poly.Boolean(new[] { (IReadOnlyList<float2>)q.Outer }, holes, BoolOp.Difference);
                            foreach (var r in rest) queue.Enqueue((r, depth + 1));
                        }
                    }
                    catch (Exception) { }
                }
            }
            list.Sort((x, y) => Poly.Area(y).CompareTo(Poly.Area(x)));
            return list;
        }

        /// <summary>
        /// A leftover piece shut in by shop plots (no street or lane reaches it) gets its way in: the smallest plot that touches
        /// both the piece and a street or lane is given back (its land becomes the lot's driveway). False when there is none.
        /// </summary>
        public bool OpenAccess(float2[] piece)
        {
            var roads = _roads ?? (_roads = StreetFrontage.Roads(Ctx.Input));
            float2[] pick = null; float pickArea = float.MaxValue;
            foreach (var shop in Shops)
            {
                if (CellPlots.Contains(shop)) continue;
                int touch = 0, street = 0;
                foreach (var v in shop)
                {
                    if (Poly.DistanceToBoundary(piece, v) < 1.5f) touch++;
                    if (Classify(roads, v) != 0) street++;
                }
                if (touch < 2 || street < 2) continue;
                float a = Poly.Area(shop);
                if (a < pickArea) { pickArea = a; pick = shop; }
            }
            if (pick == null) return false;
            Shops.Remove(pick);
            Taken.Remove(pick);
            ShopZone.Remove(pick);
            return true;
        }

        /// <summary>
        /// How far a lot's aisles keep from a shop plot: an aisle (7 m) that ends beside a zone cell closes it (the game blocks a
        /// cell a net touches; ZoneGuard drops it), so the lot stays this far from every plot — the strip between is the lot's
        /// planted edge. Half an aisle plus the cell clearance and the repair margin of the outline.
        /// </summary>
        public const float PlotClear = 4.2f;

        /// <summary>The piece minus everything within <see cref="PlotClear"/> of a shop plot (simple parts, largest first).</summary>
        public List<float2[]> KeepOffPlots(float2[] piece, float clear = PlotClear)
        {
            var result = new List<float2[]>();
            if (Shops.Count == 0) { result.Add(piece); return result; }
            var grown = new List<IReadOnlyList<float2>>();
            foreach (var s in Shops)
            {
                var c = Poly.Centroid(s);
                bool near = false;
                foreach (var v in s) if (Poly.DistanceToBoundary(piece, v) < clear + 1f || Poly.Contains(piece, v)) { near = true; break; }
                if (near) grown.Add(GrowConvex(s, clear));
            }
            if (grown.Count == 0) { result.Add(piece); return result; }
            try
            {
                foreach (var r in Poly.Boolean(new[] { (IReadOnlyList<float2>)piece }, grown, BoolOp.Difference))
                {
                    if (r?.Outer == null || r.Outer.Length < 3 || r.Holes.Count > 0) continue;
                    var ring = Poly.EnsureCcw(Poly.Clean(r.Outer, 0.05f));
                    if (ring.Length >= 3 && Poly.Area(ring) >= 300f) result.Add(ring);
                }
            }
            catch (Exception) { result.Add(piece); }
            result.Sort((a, b) => Poly.Area(b).CompareTo(Poly.Area(a)));
            return result;
        }
    }
}
