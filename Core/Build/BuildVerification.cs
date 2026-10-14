// Placemaker — post-build member verification (GPL-3.0).
//
// Playtest 4b: deleting a park left decals, bushes, benches and lamps behind — they had been built
// but never tagged as group members, so removal could not see them. The build now tags every entity
// it creates and, after Apply, compares the tagged members per kind against the plan. This pure part
// decides what counts as a mismatch so it can be unit tested; the game layer logs the result.

using System.Collections.Generic;

namespace Placemaker.Core.Build
{
    public struct MemberCounts
    {
        public int Objects, Areas, Edges, Nodes, Auxiliary;

        public int Total => Objects + Areas + Edges + Nodes + Auxiliary;

        public void Add(MemberKind kind, int n = 1)
        {
            switch (kind)
            {
                case MemberKind.Object: Objects += n; break;
                case MemberKind.Area: Areas += n; break;
                case MemberKind.Edge: Edges += n; break;
                case MemberKind.Node: Nodes += n; break;
                default: Auxiliary += n; break;
            }
        }

        public override string ToString() => $"objects {Objects}, areas {Areas}, edges {Edges}, nodes {Nodes}, other {Auxiliary}";
    }

    /// <summary>What the build expected to end up with (after exclusions and known drops).</summary>
    public struct PlannedCounts
    {
        /// <summary>Objects planned and emitted (excluded/skipped items already removed).</summary>
        public int Objects;
        /// <summary>Optional objects the game dropped for collisions (decorative; allowed to be missing).</summary>
        public int OptionalDropped;
        /// <summary>Surfaces and areas planned (the owner lot not included).</summary>
        public int Areas;
        /// <summary>Planned net pieces (courses). CS2 splits/merges edges, so only "none built" is a mismatch.</summary>
        public int NetPieces;
    }

    public static class BuildVerification
    {
        /// <summary>
        /// Human-readable mismatches between plan and tagged members (empty = consistent). Objects and
        /// areas must match exactly up to the known optional drops; more members than planned means
        /// something was adopted twice or foreign entities were tagged; fewer means parts were built
        /// untagged (they would survive removal) or vanished.
        /// </summary>
        public static List<string> Compare(PlannedCounts planned, MemberCounts actual, int ownerAreas = 0)
        {
            var issues = new List<string>();
            int minObjects = planned.Objects - planned.OptionalDropped;
            if (actual.Objects < minObjects)
                issues.Add($"objects: {actual.Objects} tagged, {minObjects}..{planned.Objects} planned ({minObjects - actual.Objects} missing or untagged)");
            else if (actual.Objects > planned.Objects)
                issues.Add($"objects: {actual.Objects} tagged, only {planned.Objects} planned ({actual.Objects - planned.Objects} extra)");
            int areas = actual.Areas - ownerAreas;
            if (areas < planned.Areas)
                issues.Add($"areas: {areas} tagged, {planned.Areas} planned ({planned.Areas - areas} missing or untagged)");
            else if (areas > planned.Areas)
                issues.Add($"areas: {areas} tagged, only {planned.Areas} planned ({areas - planned.Areas} extra)");
            if (planned.NetPieces > 0 && actual.Edges == 0)
                issues.Add($"nets: {planned.NetPieces} pieces planned, no edge tagged");
            if (actual.Edges > 0 && actual.Nodes == 0)
                issues.Add("nets: edges tagged without any node (removal would leave nodes behind)");
            return issues;
        }
    }
}
