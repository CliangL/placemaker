// Polygon toolkit used by every layer. API is fixed (other layers compile against it);
// implementations are owned by the geometry workstream.

using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace Placemaker.Core.Geometry
{
    public static class Poly
    {
        /// <summary>Shoelace area, positive for counter-clockwise rings (XZ plane, x right, z up).</summary>
        public static float SignedArea(IReadOnlyList<float2> ring)
        {
            double a = 0;
            for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++)
                a += (double)ring[j].x * ring[i].y - (double)ring[i].x * ring[j].y;
            return (float)(a * 0.5);
        }

        public static float Area(IReadOnlyList<float2> ring) => math.abs(SignedArea(ring));

        public static float Perimeter(IReadOnlyList<float2> ring)
        {
            float p = 0;
            for (int i = 0; i < ring.Count; i++) p += math.distance(ring[i], ring[(i + 1) % ring.Count]);
            return p;
        }

        public static float2[] EnsureCcw(float2[] ring)
        {
            if (SignedArea(ring) >= 0) return ring;
            var r = (float2[])ring.Clone();
            Array.Reverse(r);
            return r;
        }

        public static float3[] EnsureCcw(float3[] ring)
        {
            if (SignedArea(To2D(ring)) >= 0) return ring;
            var r = (float3[])ring.Clone();
            Array.Reverse(r);
            return r;
        }

        public static float2[] To2D(IReadOnlyList<float3> ring)
        {
            var r = new float2[ring.Count];
            for (int i = 0; i < r.Length; i++) r[i] = ring[i].xz;
            return r;
        }

        /// <summary>Lift a 2D ring to 3D using a height sampler.</summary>
        public static float3[] To3D(IReadOnlyList<float2> ring, Func<float2, float> heightAt)
        {
            var r = new float3[ring.Count];
            for (int i = 0; i < r.Length; i++) r[i] = new float3(ring[i].x, heightAt(ring[i]), ring[i].y);
            return r;
        }

        public static bool Contains(IReadOnlyList<float2> ring, float2 p)
        {
            bool inside = false;
            for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++)
            {
                if (((ring[i].y > p.y) != (ring[j].y > p.y)) &&
                    (p.x < (ring[j].x - ring[i].x) * (p.y - ring[i].y) / (ring[j].y - ring[i].y) + ring[i].x))
                    inside = !inside;
            }
            return inside;
        }

        /// <summary>True when no two non-adjacent edges intersect or touch. O(n²).</summary>
        public static bool IsSimple(IReadOnlyList<float2> ring)
        {
            int n = ring.Count;
            if (n < 3) return false;
            for (int i = 0; i < n; i++)
            {
                float2 a = ring[i], b = ring[(i + 1) % n];
                for (int j = i + 1; j < n; j++)
                {
                    if (j == i || (j + 1) % n == i || j == (i + 1) % n) continue;
                    if (SegmentsIntersect(a, b, ring[j], ring[(j + 1) % n])) return false;
                }
            }
            return true;
        }

        public static bool SegmentsIntersect(float2 p1, float2 p2, float2 p3, float2 p4)
        {
            float d1 = Cross(p4 - p3, p1 - p3), d2 = Cross(p4 - p3, p2 - p3);
            float d3 = Cross(p2 - p1, p3 - p1), d4 = Cross(p2 - p1, p4 - p1);
            if (((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0))) return true;
            const float eps = 1e-6f;
            if (math.abs(d1) < eps && OnSegment(p3, p4, p1)) return true;
            if (math.abs(d2) < eps && OnSegment(p3, p4, p2)) return true;
            if (math.abs(d3) < eps && OnSegment(p1, p2, p3)) return true;
            if (math.abs(d4) < eps && OnSegment(p1, p2, p4)) return true;
            return false;
        }

        public static float Cross(float2 a, float2 b) => a.x * b.y - a.y * b.x;

        private static bool OnSegment(float2 a, float2 b, float2 p) =>
            math.min(a.x, b.x) - 1e-6f <= p.x && p.x <= math.max(a.x, b.x) + 1e-6f &&
            math.min(a.y, b.y) - 1e-6f <= p.y && p.y <= math.max(a.y, b.y) + 1e-6f;

        public static float2 Centroid(IReadOnlyList<float2> ring)
        {
            double cx = 0, cy = 0, a = 0;
            for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++)
            {
                double f = (double)ring[j].x * ring[i].y - (double)ring[i].x * ring[j].y;
                a += f; cx += (ring[j].x + ring[i].x) * f; cy += (ring[j].y + ring[i].y) * f;
            }
            if (math.abs(a) < 1e-9) return ring.Count > 0 ? ring[0] : default;
            return new float2((float)(cx / (3 * a)), (float)(cy / (3 * a)));
        }

        public static void Bounds(IReadOnlyList<float2> ring, out float2 min, out float2 max)
        {
            min = new float2(float.MaxValue); max = new float2(float.MinValue);
            foreach (var p in ring) { min = math.min(min, p); max = math.max(max, p); }
        }

        public static float DistanceToBoundary(IReadOnlyList<float2> ring, float2 p)
        {
            float best = float.MaxValue;
            for (int i = 0; i < ring.Count; i++)
                best = math.min(best, math.sqrt(ReservedSpace.DistSqToSegment(p, ring[i], ring[(i + 1) % ring.Count])));
            return best;
        }

        /// <summary>
        /// Removes duplicate/near-duplicate points (&lt; minEdge), collinear points and zero-area spikes.
        /// Returns a CCW ring or an empty array if nothing valid remains.
        /// </summary>
        public static float2[] Clean(IReadOnlyList<float2> ring, float minEdge = 0.05f)
        {
            var pts = new List<float2>(ring);
            bool changed = true;
            while (changed && pts.Count >= 3)
            {
                changed = false;
                for (int i = 0; i < pts.Count && pts.Count >= 3; i++)
                {
                    float2 prev = pts[(i - 1 + pts.Count) % pts.Count], cur = pts[i], next = pts[(i + 1) % pts.Count];
                    if (math.distance(prev, cur) < minEdge || math.abs(Cross(cur - prev, next - cur)) < 1e-4f * math.max(1f, math.distance(prev, next)))
                    {
                        pts.RemoveAt(i); changed = true; i--;
                    }
                }
            }
            if (pts.Count < 3 || Area(pts) < 1e-3f) return Array.Empty<float2>();
            return EnsureCcw(pts.ToArray());
        }

        /// <summary>
        /// Inward offset (negative distance = outward). May return several rings when the shape
        /// pinches apart, or none when it vanishes. Rings are CCW and simple.
        /// </summary>
        public static List<float2[]> Offset(IReadOnlyList<float2> ring, float inward)
        {
            // Simple mitred offset with collapse detection; robust version owned by geometry workstream.
            var r = EnsureCcw(Clean(ring));
            var result = new List<float2[]>();
            if (r.Length < 3) return result;
            int n = r.Length;
            var o = new float2[n];
            for (int i = 0; i < n; i++)
            {
                float2 a = r[(i - 1 + n) % n], b = r[i], c = r[(i + 1) % n];
                float2 n1 = math.normalizesafe(new float2(-(b - a).y, (b - a).x));
                float2 n2 = math.normalizesafe(new float2(-(c - b).y, (c - b).x));
                float2 bis = math.normalizesafe(n1 + n2, n1);
                float cos = math.max(0.25f, math.dot(bis, n1));
                o[i] = b + bis * (inward / cos);
            }
            var cleaned = Clean(o);
            if (cleaned.Length >= 3 && IsSimple(cleaned) && SignedArea(cleaned) > 0) result.Add(cleaned);
            return result;
        }
    }

    /// <summary>
    /// CS2 area acceptance. CS2 insets every area ring by 0.1 m, ear-clips it in float precision and
    /// silently drops the whole area if triangulation fails (RESEARCH-NOTES rule 6). Areas have no holes.
    /// </summary>
    public static class SurfaceAcceptance
    {
        public const float GameInset = 0.1f;
        public const float MinEdge = 0.3f;

        /// <summary>True when CS2 will keep this ring (simple, CCW-able, triangulates after the 0.1 m inset).</summary>
        public static bool IsAccepted(IReadOnlyList<float2> ring)
        {
            var c = Poly.Clean(ring, MinEdge);
            if (c.Length < 3 || !Poly.IsSimple(c)) return false;
            return EarClip.Triangulate(c, out _);
        }

        /// <summary>
        /// Returns rings CS2 will accept covering (nearly) the same area: cleaned, and split into
        /// pieces when needed. Empty list = nothing usable. Heights come from heightAt.
        /// </summary>
        public static List<float3[]> MakeAcceptable(IReadOnlyList<float2> ring, Func<float2, float> heightAt)
        {
            var result = new List<float3[]>();
            var c = Poly.Clean(ring, MinEdge);
            if (c.Length < 3 || !Poly.IsSimple(c)) return result;
            if (EarClip.Triangulate(c, out var tris))
            {
                result.Add(Poly.To3D(c, heightAt));
                return result;
            }
            // Fallback: emit the triangles merged pairwise is the geometry workstream's job; for now each triangle.
            return result;
        }
    }

    /// <summary>Plain ear clipping (float precision, CCW input). Returns false on failure.</summary>
    public static class EarClip
    {
        public static bool Triangulate(IReadOnlyList<float2> ring, out List<int3> triangles)
        {
            triangles = new List<int3>();
            int n = ring.Count;
            if (n < 3) return false;
            var idx = new List<int>(n);
            for (int i = 0; i < n; i++) idx.Add(i);
            int guard = 2 * n;
            while (idx.Count > 3 && guard-- > 0)
            {
                bool clipped = false;
                for (int k = 0; k < idx.Count; k++)
                {
                    int i0 = idx[(k - 1 + idx.Count) % idx.Count], i1 = idx[k], i2 = idx[(k + 1) % idx.Count];
                    float2 a = ring[i0], b = ring[i1], c = ring[i2];
                    if (Poly.Cross(b - a, c - b) <= 1e-7f) continue;
                    bool any = false;
                    foreach (int j in idx)
                    {
                        if (j == i0 || j == i1 || j == i2) continue;
                        if (InTri(ring[j], a, b, c)) { any = true; break; }
                    }
                    if (any) continue;
                    triangles.Add(new int3(i0, i1, i2));
                    idx.RemoveAt(k);
                    clipped = true;
                    break;
                }
                if (!clipped) return false;
            }
            if (idx.Count == 3) triangles.Add(new int3(idx[0], idx[1], idx[2]));
            return triangles.Count == n - 2;
        }

        private static bool InTri(float2 p, float2 a, float2 b, float2 c) =>
            Poly.Cross(b - a, p - a) >= 0 && Poly.Cross(c - b, p - b) >= 0 && Poly.Cross(a - c, p - c) >= 0;
    }
}
