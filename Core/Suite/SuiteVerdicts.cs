// In-game test suite: pure checks on what a scenario measured. The game layer samples the world; these functions
// decide. Unit-tested offline (Tests/Placemaker.Tests/Suite).

using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace Placemaker.Core.Suite
{
    public sealed class PitResult
    {
        public int Cells;          // samples that ended below both the old ground and the planned ground
        public int CellsNearRoads; // of those, within the road distance
        public float MaxDepth;     // metres below min(before, planned)
        public float2 WorstAt;
        public float MaxRise;      // largest unplanned rise (after - max(before, planned))
    }

    public sealed class GroundResult
    {
        public int Pits, PitsNearRoads, PitsOutside, Mounds;
        public float MaxDepth, MaxRise;
        public float2 WorstPit, WorstMound;
    }

    public static class SuiteVerdicts
    {
        /// <summary>Ground that ended more than <paramref name="tolerance"/> below both the original ground and the plan's target.</summary>
        public static PitResult Pits(IReadOnlyList<float2> at, IReadOnlyList<float> before, IReadOnlyList<float> after, IReadOnlyList<float> planned,
            IReadOnlyList<bool> nearRoad, float tolerance = 1.5f)
        {
            var r = new PitResult();
            int n = Math.Min(Math.Min(at.Count, before.Count), Math.Min(after.Count, planned.Count));
            for (int i = 0; i < n; i++)
            {
                float b = before[i], a = after[i], p = planned[i];
                if (!math.isfinite(b) || !math.isfinite(a)) continue;
                if (!math.isfinite(p)) p = b;
                float floor = math.min(b, p);
                float depth = floor - a;
                if (depth > tolerance)
                {
                    r.Cells++;
                    if (nearRoad != null && i < nearRoad.Count && nearRoad[i]) r.CellsNearRoads++;
                    if (depth > r.MaxDepth) { r.MaxDepth = depth; r.WorstAt = at[i]; }
                }
                float rise = a - math.max(b, p);
                if (rise > r.MaxRise) r.MaxRise = rise;
            }
            return r;
        }

        /// <summary>
        /// Pits and mounds: ground more than <paramref name="tolerance"/> below the lowest height anything allows there
        /// (the old ground or any layer's planned ground) or above the highest.
        /// </summary>
        public static GroundResult PitsAndMounds(IReadOnlyList<float2> at, IReadOnlyList<float> after, IReadOnlyList<float> plannedLow, IReadOnlyList<float> plannedHigh,
            IReadOnlyList<bool> nearRoad, IReadOnlyList<bool> inside, float tolerance = 1.5f)
        {
            var r = new GroundResult();
            int n = Math.Min(Math.Min(at.Count, after.Count), Math.Min(plannedLow.Count, plannedHigh.Count));
            for (int i = 0; i < n; i++)
            {
                float a = after[i];
                if (!math.isfinite(a)) continue;
                float depth = plannedLow[i] - a;
                if (depth > tolerance)
                {
                    r.Pits++;
                    if (nearRoad != null && i < nearRoad.Count && nearRoad[i]) r.PitsNearRoads++;
                    if (inside != null && i < inside.Count && !inside[i]) r.PitsOutside++;
                    if (depth > r.MaxDepth) { r.MaxDepth = depth; r.WorstPit = at[i]; }
                }
                float rise = a - plannedHigh[i];
                if (rise > tolerance)
                {
                    r.Mounds++;
                    if (rise > r.MaxRise) { r.MaxRise = rise; r.WorstMound = at[i]; }
                }
            }
            return r;
        }

        private static readonly string[] CropWords = { "agricultur", "crop", "farm", "field", "extractor", "forestry", "orchard", "vineyard" };

        /// <summary>Farm-looking prefab name: only a hint now (the check uses the extractor components).</summary>
        public static bool IsCropLike(string prefab)
        {
            if (string.IsNullOrEmpty(prefab)) return false;
            var s = prefab.ToLowerInvariant();
            foreach (var w in CropWords) if (s.Contains(w)) return true;
            return false;
        }

        public static bool IsFence(string prefab) => !string.IsNullOrEmpty(prefab) && prefab.IndexOf("fence", StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>An object that blocks a drive or a path when it stands in it (fence/hedge/wall/barrier objects; not lights).</summary>
        public static bool IsBarrierObject(string prefab)
        {
            if (string.IsNullOrEmpty(prefab)) return false;
            var s = prefab.ToLowerInvariant();
            if (s.Contains("light") || s.Contains("lamp") || s.Contains("sign") || s.Contains("decal")) return false;
            return s.Contains("fence") || s.Contains("hedge") || s.Contains("wall") || s.Contains("barrier") || s.Contains("bollard") || s.Contains("railing");
        }

        /// <summary>Layers whose purpose is farming: crop surfaces are expected there.</summary>
        public static bool IsFarmType(string layer)
        {
            if (string.IsNullOrEmpty(layer)) return false;
            var s = layer.ToLowerInvariant();
            return s.Contains("farm") || s.Contains("orchard") || s.Contains("garden") || s.Contains("animal");
        }

        /// <summary>Max and 95th percentile of frame times in milliseconds.</summary>
        public static (float Max, float P95, int Count) FrameStats(List<float> ms)
        {
            if (ms == null || ms.Count == 0) return (0f, 0f, 0);
            var copy = new List<float>(ms);
            copy.Sort();
            int idx = (int)math.clamp(math.ceil(copy.Count * 0.95f) - 1, 0, copy.Count - 1);
            return (copy[copy.Count - 1], copy[idx], copy.Count);
        }

        /// <summary>Share of growables that face one of the build's own roads (1 = all, NaN = none grew).</summary>
        public static float OwnFacingShare(int own, int city) => own + city == 0 ? float.NaN : (float)own / (own + city);

        // ───────────── outline as intended ─────────────

        public static float Area(IReadOnlyList<float2> ring)
        {
            float a = 0f;
            for (int i = 0; i < ring.Count; i++)
            {
                var p = ring[i];
                var q = ring[(i + 1) % ring.Count];
                a += p.x * q.y - q.x * p.y;
            }
            return math.abs(a) * 0.5f;
        }

        private static bool Inside(IReadOnlyList<float2> ring, float2 p)
        {
            bool c = false;
            for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++)
            {
                var a = ring[i];
                var b = ring[j];
                if ((a.y > p.y) != (b.y > p.y) && p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x) c = !c;
            }
            return c;
        }

        /// <summary>Intersection over union of two polygons, rasterized on a grid (cell m).</summary>
        public static float IoU(IReadOnlyList<float2> a, IReadOnlyList<float2> b, float cell = 1f)
        {
            if (a == null || b == null || a.Count < 3 || b.Count < 3) return 0f;
            float2 lo = a[0], hi = a[0];
            foreach (var p in a) { lo = math.min(lo, p); hi = math.max(hi, p); }
            foreach (var p in b) { lo = math.min(lo, p); hi = math.max(hi, p); }
            int inter = 0, union = 0;
            for (float x = lo.x + cell * 0.5f; x < hi.x; x += cell)
                for (float y = lo.y + cell * 0.5f; y < hi.y; y += cell)
                {
                    var p = new float2(x, y);
                    bool ia = Inside(a, p), ib = Inside(b, p);
                    if (ia || ib) union++;
                    if (ia && ib) inter++;
                }
            return union == 0 ? 0f : inter / (float)union;
        }

        /// <summary>The area a round brush of the given diameter sweeps along a stroke, inside a clip polygon (2 m grid).</summary>
        public static float SweptArea(IReadOnlyList<float2> stroke, float diameter, IReadOnlyList<float2> clip, float cell = 2f)
        {
            if (stroke == null || stroke.Count == 0) return 0f;
            float r = diameter * 0.5f;
            float2 lo = stroke[0], hi = stroke[0];
            foreach (var p in stroke) { lo = math.min(lo, p); hi = math.max(hi, p); }
            lo -= r; hi += r;
            int n = 0;
            for (float x = lo.x + cell * 0.5f; x < hi.x; x += cell)
                for (float y = lo.y + cell * 0.5f; y < hi.y; y += cell)
                {
                    var p = new float2(x, y);
                    if (clip != null && clip.Count >= 3 && !Inside(clip, p)) continue;
                    bool hit = false;
                    for (int i = 0; i < stroke.Count && !hit; i++)
                    {
                        var a = stroke[i];
                        var b = stroke[math.min(i + 1, stroke.Count - 1)];
                        var ab = b - a;
                        float t = math.lengthsq(ab) < 1e-6f ? 0f : math.saturate(math.dot(p - a, ab) / math.lengthsq(ab));
                        hit = math.distance(p, a + ab * t) <= r;
                    }
                    if (hit) n++;
                }
            return n * cell * cell;
        }

        /// <summary>Distance from a point to a closed ring's edges.</summary>
        public static float DistanceToRing(IReadOnlyList<float2> ring, float2 p)
        {
            float best = float.MaxValue;
            for (int i = 0; i < ring.Count; i++)
            {
                var a = ring[i];
                var b = ring[(i + 1) % ring.Count];
                var ab = b - a;
                float t = math.lengthsq(ab) < 1e-6f ? 0f : math.saturate(math.dot(p - a, ab) / math.lengthsq(ab));
                best = math.min(best, math.distance(p, a + ab * t));
            }
            return best;
        }

        // ───────────── shot settle gate ─────────────

        /// <summary>A pair of captures counts as settled when few pixels changed and no tile changed much.</summary>
        public static bool PairSettled(int changed16, float tileMax, int changedLimit, float tileLimit) => changed16 <= changedLimit && tileMax <= tileLimit;
    }
}
