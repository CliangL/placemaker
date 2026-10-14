// Per-plan object budget (s8, "ground before props"). A layer that fills ground with props (a crop
// plant every metre, a hedge piece every 50 cm, a pallet per square metre) produces thousands of
// objects: slow to build, heavy in game, and it hides that the ground should have been a surface
// (GroundCover). LayerBase runs this after every PlanCore:
//   cap = max(MinCap, perHectare × area ha). Above the cap the plan is THINNED, lowest-priority
//   roles first (decals → bushes → planters → bulk furniture → trees), and only "bulk" groups
//   (a category with ≥ BulkGroup objects in this plan) — a barn, a fountain, benches, lamps, bins,
//   fence pieces and functional objects (parking bays, markers…) are never touched. Thinning keeps an
//   even stride in plan order (rows stay rows, just sparser) and never removes more than
//   MaxThin of a group. The Review panel gets a Warning with the numbers.

using System.Collections.Generic;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Common
{
    public static class ObjectBudget
    {
        public const float DefaultPerHectare = 650f;
        public const int MinCap = 300;
        public const int BulkGroup = 12;
        /// <summary>At most this share of one group is removed (the look must survive).</summary>
        public const float MaxThin = 0.75f;
        public const string IssueKey = "Placemaker.Common.Issue.ObjectBudget";

        private static readonly ObjectRole[] ThinOrder =
            { ObjectRole.Decal, ObjectRole.Bush, ObjectRole.Planter, ObjectRole.Furniture, ObjectRole.Tree };

        public static int Cap(float areaM2, float perHectare) =>
            perHectare <= 0f ? int.MaxValue : math.max(MinCap, (int)math.round(areaM2 / 10000f * perHectare));

        /// <summary>Thins the plan's objects down to the cap. Returns the number removed.</summary>
        public static int Enforce(AreaPlan plan, float areaM2, float perHectare)
        {
            if (plan?.Objects == null) return 0;
            int cap = Cap(areaM2, perHectare);
            int before = plan.Objects.Count;
            if (before <= cap) return 0;
            int excess = before - cap;
            var drop = new HashSet<PlannedObject>();
            foreach (var role in ThinOrder)
            {
                if (excess <= 0) break;
                // Bulk groups of this role, by category.
                var groups = new Dictionary<string, List<PlannedObject>>(System.StringComparer.Ordinal);
                foreach (var o in plan.Objects)
                {
                    if (o == null || o.Role != role) continue;
                    var key = o.Category ?? "";
                    if (!groups.TryGetValue(key, out var l)) groups[key] = l = new List<PlannedObject>();
                    l.Add(o);
                }
                int thinnable = 0;
                foreach (var g in groups.Values) if (g.Count >= BulkGroup) thinnable += g.Count;
                if (thinnable == 0) continue;
                int take = math.min(excess, (int)math.floor(thinnable * MaxThin));
                if (take <= 0) continue;
                float keep = 1f - (float)take / thinnable;
                int removed = 0;
                var keys = new List<string>(groups.Keys);
                keys.Sort(System.StringComparer.Ordinal);
                foreach (var k in keys)
                {
                    var g = groups[k];
                    if (g.Count < BulkGroup) continue;
                    // Even stride: keep element i when the running quota crosses an integer.
                    for (int i = 0; i < g.Count; i++)
                        if ((int)math.floor((i + 1) * keep) == (int)math.floor(i * keep)) { drop.Add(g[i]); removed++; }
                }
                excess -= removed;
            }
            if (drop.Count == 0) return 0;
            plan.Objects.RemoveAll(o => drop.Contains(o));
            plan.Issues.Add(new PlanIssue
            {
                Severity = IssueSeverity.Warning,
                Key = IssueKey,
                Args = new object[] { before, cap, drop.Count },
            });
            plan.Facts["objectBudgetThinned"] = drop.Count;
            return drop.Count;
        }
    }
}
