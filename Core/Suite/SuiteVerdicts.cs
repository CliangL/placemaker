// In-game test suite: pure checks on what a scenario measured (docs/teardown/10-ingame-suite.md §4.5).
// The game layer samples the world; these functions decide. Unit-tested offline.

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

        private static readonly string[] CropWords = { "agricultur", "crop", "farm", "field", "extractor", "forestry", "orchard", "vineyard" };

        /// <summary>Farm-looking prefab (crop surface, vanilla industry lot) that has no place in a non-farm build.</summary>
        public static bool IsCropLike(string prefab)
        {
            if (string.IsNullOrEmpty(prefab)) return false;
            var s = prefab.ToLowerInvariant();
            foreach (var w in CropWords) if (s.Contains(w)) return true;
            return false;
        }

        public static bool IsFence(string prefab) => !string.IsNullOrEmpty(prefab) && prefab.IndexOf("fence", StringComparison.OrdinalIgnoreCase) >= 0;

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
    }
}
