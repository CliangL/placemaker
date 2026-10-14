// s14: zoning cutouts stay empty. A cutout is land a layer left along a road for zoned buildings (regions
// tagged "shops" + "place:shops": the Zoning feature on parks/plazas, the Commercial types' shop rows).
// Places are reserved for siblings, but features differ in how much of ReservedSpace they honour (ground
// pads of kiosks/restrooms, a camping path, a dog-park lawn were seen inside), and the playtest found
// picnic tables and a tent prop in a strip. This last pass after the whole recipe ran is the guarantee:
// no object, net or ground of any layer is left in a cutout; only zoning lives there.

using System;
using System.Collections.Generic;
using Placemaker.Core.Geometry;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Common
{
    public static class Cutouts
    {
        /// <summary>Layer whose zoning fills cutouts (its plan is never touched).</summary>
        public const string ZoningLayer = "edgeZoning";
        /// <summary>A net or object this far inside a cutout counts (shared edges with the host never do).</summary>
        private const float Inset = 0.3f;

        public static bool IsCutout(PlannedRegion r)
        {
            if (r?.Tags == null || r.Ring == null || r.Ring.Length < 3) return false;
            bool shops = false, place = false;
            foreach (var t in r.Tags)
            {
                if (t == StreetFrontage.ShopsTag) shops = true;
                else if (t == LayerBase.ShopsPlace) place = true;
            }
            return shops && place;
        }

        /// <summary>Every cutout of the composed plan (XZ rings, CCW).</summary>
        public static List<float2[]> Of(ComposedPlan plan)
        {
            var list = new List<float2[]>();
            if (plan?.Layers == null) return list;
            foreach (var l in plan.Layers)
                foreach (var r in l.Regions)
                    if (IsCutout(r)) list.Add(Poly.EnsureCcw(Poly.To2D(r.Ring)));
            return list;
        }

        /// <summary>Removes / clips everything of every layer (but the zoning) that reaches into a cutout.</summary>
        public static int Enforce(ComposedPlan plan) => Enforce(plan, Of(plan));

        /// <summary>(s19) Same for given rings (ZoneGuard: zoned cells reaching past a cutout).</summary>
        public static int Enforce(ComposedPlan plan, List<float2[]> cuts)
        {
            if (plan?.Layers == null || cuts == null || cuts.Count == 0) return 0;
            for (int i = 0; i < cuts.Count; i++) cuts[i] = Poly.EnsureCcw(cuts[i]);
            var inner = new List<float2[]>(cuts.Count);
            var min = new List<float2>(); var max = new List<float2>();
            foreach (var c in cuts)
            {
                var q = Shrink(c, Inset);
                inner.Add(q);
                float2 lo = new float2(float.MaxValue), hi = new float2(float.MinValue);
                foreach (var p in c) { lo = math.min(lo, p); hi = math.max(hi, p); }
                min.Add(lo); max.Add(hi);
            }
            bool In(float2 p, float pad)
            {
                for (int i = 0; i < cuts.Count; i++)
                {
                    if (p.x < min[i].x - pad || p.y < min[i].y - pad || p.x > max[i].x + pad || p.y > max[i].y + pad) continue;
                    if (Poly.Contains(inner[i], p)) return true;
                    if (pad > 0f && (Poly.Contains(cuts[i], p) || Poly.DistanceToBoundary(cuts[i], p) < pad)) return true;
                }
                return false;
            }

            int changed = 0;
            foreach (var l in plan.Layers)
            {
                if (l == null || l.GeneratorId == ZoningLayer) continue;
                // ── objects: footprint (roughly) must stay off the cutout ──
                changed += l.Objects.RemoveAll(o => o != null && In(o.Position.xz, Pad(o.Role)));
                // ── nets: keep only the stretches outside ──
                changed += ClipNets(l, In);
                // ── ground: surfaces and areas minus the cutouts ──
                changed += ClipRings(l.Surfaces, s => s.Ring, (s, ring) => new PlannedSurface { Ring = ring, Role = s.Role, Asset = s.Asset, Category = s.Category }, cuts);
                changed += ClipRings(l.Areas, a => a.Ring, (a, ring) => new PlannedArea { Ring = ring, Role = a.Role, Asset = a.Asset }, cuts);
            }
            return changed;
        }

        /// <summary>How far outside the cutout an object of this role still reaches into it (crowns, benches).</summary>
        private static float Pad(ObjectRole role)
        {
            switch (role)
            {
                case ObjectRole.Decal: case ObjectRole.AccessPoint: return 0f;
                case ObjectRole.Tree: return 1.6f;
                default: return 0.35f;
            }
        }

        private static float2[] Shrink(float2[] q, float by)
        {
            var c = Poly.Centroid(q);
            var r = new float2[q.Length];
            for (int i = 0; i < q.Length; i++) r[i] = q[i] + math.normalizesafe(c - q[i]) * by;
            return r;
        }

        private static int ClipNets(AreaPlan l, Func<float2, float, bool> inside)
        {
            const int samples = 48;
            int changed = 0;
            var remove = new List<int>();
            int count = l.Nets.Count;
            for (int i = 0; i < count; i++)
            {
                var n = l.Nets[i];
                var flags = new bool[samples + 1];
                bool any = false;
                for (int k = 0; k <= samples; k++)
                {
                    flags[k] = inside(Composer.Bezier(n.A, n.B, n.C, n.D, k / (float)samples).xz, 0f);
                    any |= flags[k];
                }
                if (!any) continue;
                changed++;
                // Outside runs [t0, t1] (whole samples), each at least 1 m long.
                var runs = new List<float2>();
                int s = -1;
                for (int k = 0; k <= samples + 1; k++)
                {
                    bool outside = k <= samples && !flags[k];
                    if (outside && s < 0) s = k;
                    if (!outside && s >= 0)
                    {
                        float t0 = s == 0 ? 0f : (s - 0.5f) / samples, t1 = k - 1 == samples ? 1f : (k - 1 + 0.5f) / samples;
                        // pull the cut ends back out of the cutout
                        if (s > 0) t0 = math.min(t1, t0 + 0.5f / samples);
                        if (k - 1 < samples) t1 = math.max(t0, t1 - 0.5f / samples);
                        if (Length(n, t0, t1) >= 1f) runs.Add(new float2(t0, t1));
                        s = -1;
                    }
                }
                if (runs.Count == 0) { remove.Add(i); continue; }
                var original = Copy(n);
                Assign(n, Piece(original, runs[0].x, runs[0].y));
                for (int r = 1; r < runs.Count; r++) l.Nets.Add(Piece(original, runs[r].x, runs[r].y));
            }
            for (int r = remove.Count - 1; r >= 0; r--)
            {
                int idx = remove[r];
                l.Nets.RemoveAt(idx);
                foreach (var o in l.Objects)
                    if (o.AttachedNet == idx) o.AttachedNet = -1;
                    else if (o.AttachedNet > idx) o.AttachedNet--;
            }
            return changed;
        }

        private static float Length(PlannedNet n, float t0, float t1)
        {
            float len = 0f; var prev = Composer.Bezier(n.A, n.B, n.C, n.D, t0).xz;
            for (int k = 1; k <= 8; k++)
            {
                var p = Composer.Bezier(n.A, n.B, n.C, n.D, math.lerp(t0, t1, k / 8f)).xz;
                len += math.distance(prev, p); prev = p;
            }
            return len;
        }

        private static PlannedNet Copy(PlannedNet n) => new PlannedNet
        {
            A = n.A, B = n.B, C = n.C, D = n.D, Role = n.Role, Asset = n.Asset, Category = n.Category,
            StartAttach = n.StartAttach, StartAttachT = n.StartAttachT, EndAttach = n.EndAttach, EndAttachT = n.EndAttachT,
            Upgrades = n.Upgrades != null ? (string[])n.Upgrades.Clone() : Array.Empty<string>(), FollowTerrain = n.FollowTerrain,
        };

        private static void Assign(PlannedNet to, PlannedNet from)
        {
            to.A = from.A; to.B = from.B; to.C = from.C; to.D = from.D;
            to.StartAttach = from.StartAttach; to.StartAttachT = from.StartAttachT;
            to.EndAttach = from.EndAttach; to.EndAttachT = from.EndAttachT;
        }

        /// <summary>The [t0, t1] stretch of a net (attachments kept only at the original ends).</summary>
        private static PlannedNet Piece(PlannedNet original, float t0, float t1)
        {
            var c = Copy(original);
            if (t1 < 1f) PathJoins.Split(c, t1);
            if (t0 > 0f)
            {
                var rest = PathJoins.Split(c, t1 > 1e-4f ? t0 / t1 : 0f);
                rest.StartAttach = -1;
                return rest;
            }
            return c;
        }

        private static int ClipRings<T>(List<T> list, Func<T, float3[]> ringOf, Func<T, float3[], T> make, List<float2[]> cuts)
        {
            int changed = 0;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                var ring3 = ringOf(list[i]);
                if (ring3 == null || ring3.Length < 3) continue;
                var ring = Poly.To2D(ring3);
                var hits = new List<IReadOnlyList<float2>>();
                foreach (var c in cuts) if (Overlap(ring, c) > 0.25f) hits.Add(c);
                if (hits.Count == 0) continue;
                List<Region> rest;
                try { rest = Poly.Boolean(new[] { (IReadOnlyList<float2>)Poly.EnsureCcw(ring) }, hits, BoolOp.Difference); }
                catch (Exception) { continue; }
                changed++;
                var item = list[i];
                list.RemoveAt(i);
                Func<float2, float> h = p => HeightNear(ring3, p);
                foreach (var piece in rest)
                {
                    if (piece?.Outer == null || piece.Outer.Length < 3 || Poly.Area(piece.Outer) < 1f) continue;
                    foreach (var r in SurfaceAcceptance.MakeAcceptable(piece, h))
                        if (r != null && r.Length >= 3 && SurfaceAcceptance.IsAccepted(Poly.To2D(r))) list.Insert(i, make(item, r));
                }
            }
            return changed;
        }

        private static float Overlap(float2[] a, float2[] b)
        {
            float2 alo = new float2(float.MaxValue), ahi = new float2(float.MinValue), blo = alo, bhi = ahi;
            foreach (var p in a) { alo = math.min(alo, p); ahi = math.max(ahi, p); }
            foreach (var p in b) { blo = math.min(blo, p); bhi = math.max(bhi, p); }
            if (alo.x > bhi.x || blo.x > ahi.x || alo.y > bhi.y || blo.y > ahi.y) return 0f;
            try
            {
                float area = 0f;
                foreach (var g in Poly.Boolean(new[] { (IReadOnlyList<float2>)Poly.EnsureCcw(a) }, new[] { (IReadOnlyList<float2>)b }, BoolOp.Intersection))
                {
                    area += Poly.Area(g.Outer);
                    if (g.Holes != null) foreach (var hole in g.Holes) area -= Poly.Area(hole);
                }
                return area;
            }
            catch (Exception) { return 0f; }
        }

        private static float HeightNear(float3[] ring, float2 p)
        {
            float best = float.MaxValue, y = ring[0].y;
            foreach (var v in ring)
            {
                float d = math.distancesq(v.xz, p);
                if (d < best) { best = d; y = v.y; }
            }
            return y;
        }
    }
}
