// Placemaker roads layer — connecting the planned network to existing roads.
//
// Adapted from Grid Road Generator (Ti4goc), Systems/GridRoadToolSystem.cs: MakeCoursePos,
// FindInteriorExistingEdges, SplitSegmentsCrossingInteriorEdges and RingCrossings. The ECS parts
// (entities, quadtree, CoursePos) stay in the game layer; here they are expressed through
// SiteContext.NearbyNets / AreaShape.Attachments ExternalIds and PlannedNet.StartAttach/EndAttach.
//
// That file also carries code adapted from CS2-NetworkTools, Copyright (c) Luca Rager,
// MIT License (https://github.com/lucarager/CS2-NetworkTools):
//   Permission is hereby granted, free of charge, to any person obtaining a copy of this software
//   and associated documentation files (the "Software"), to deal in the Software without
//   restriction, including without limitation the rights to use, copy, modify, merge, publish,
//   distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the
//   Software is furnished to do so, subject to the following conditions: The above copyright
//   notice and this permission notice shall be included in all copies or substantial portions of
//   the Software. THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND.

using System.Collections.Generic;
using Placemaker.Core.Layers.Roads.Generators;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Roads
{
    /// <summary>An existing road edge the plan may connect to.</summary>
    internal struct ExistingRoad
    {
        public long Id;
        public RoadCurve Curve;
        public float HalfWidth;
    }

    internal static class RoadConnections
    {
        /// <summary>A generated end this close to a selected/attached existing node joins that node (GRG NodeSnapDistance).</summary>
        public const float NodeSnapDistance = GridGenerator.MinNodeDistance;
        /// <summary>Base distance under which an end joins an existing edge (GRG EdgeSnapDistance).</summary>
        public const float EdgeSnapDistance = GridGenerator.MinNodeDistance;
        /// <summary>
        /// Crossings closer than this to a true end are left to end snapping. Deliberately half the
        /// snap distance so there is no dead zone where a crossing is neither split nor snapped (GRG).
        /// </summary>
        public const float CrossingEndSkip = EdgeSnapDistance * 0.5f;
        /// <summary>Parameters this close to 0/1 mean "the edge's start/end node" rather than a split.</summary>
        public const float EndNodeT = 0.01f;

        public static List<ExistingRoad> Collect(SiteContext site)
        {
            var list = new List<ExistingRoad>();
            if (site?.NearbyNets == null) return list;
            foreach (var n in site.NearbyNets)
            {
                if (n.Category != NetCategory.Road) continue;
                if (!MathEx.Finite(n.A) || !MathEx.Finite(n.B) || !MathEx.Finite(n.C) || !MathEx.Finite(n.D)) continue;
                if (math.distance(n.A.xz, n.D.xz) < 0.1f && math.distance(n.B.xz, n.C.xz) < 0.1f) continue;
                list.Add(new ExistingRoad
                {
                    Id = n.ExternalId,
                    Curve = new RoadCurve(n.A, n.B, n.C, n.D),
                    HalfWidth = n.Width > 0f && math.isfinite(n.Width) ? n.Width * 0.5f : Composer.DefaultHalfWidth(NetRole.Street),
                });
            }
            return list;
        }

        /// <summary>Snap distance for an existing road: wide roads catch ends that stop at their curb.</summary>
        public static float SnapDistance(ExistingRoad r) => math.max(EdgeSnapDistance, r.HalfWidth + 2f);

        /// <summary>
        /// Splits generated edges where they cross an existing road away from their ends, so each piece
        /// ends exactly on the road and connects there (GRG SplitSegmentsCrossingInteriorEdges). Curves
        /// are split exactly (de Casteljau) — GRG had to leave arcs unsplit.
        /// </summary>
        public static void SplitAtExisting(RoadNetwork net, List<ExistingRoad> existing)
        {
            if (existing.Count == 0) return;
            var index = BuildIndex(existing, out var chordTs);
            var scratch = new List<int>();
            var seen = new HashSet<int>();
            var pts = new List<float3>();
            var ts = new List<float>();
            var hits = new List<float>();
            int count = net.Edges.Count;
            for (int ei = 0; ei < count; ei++)
            {
                var e = net.Edges[ei];
                if (!e.Alive) continue;
                pts.Clear(); ts.Clear(); hits.Clear();
                e.Curve.Flatten(pts, ts, 2f, 4f);
                for (int i = 0; i + 1 < pts.Count; i++)
                {
                    float2 a = pts[i].xz, b = pts[i + 1].xz;
                    index.Query(math.min(a, b) - 0.5f, math.max(a, b) + 0.5f, scratch, seen);
                    foreach (int sid in scratch)
                    {
                        if (!Seg.Intersect(a, b, index.A[sid], index.B[sid], out float ta, out _)) continue;
                        float t = math.lerp(ts[i], ts[i + 1], math.saturate(ta));
                        hits.Add(t);
                    }
                }
                if (hits.Count == 0) continue;
                hits.Sort();
                float3 start = e.Curve.A, end = e.Curve.D;
                var accepted = new List<float>();
                float3 last = start;
                foreach (float t in hits)
                {
                    float3 p = e.Curve.Position(t);
                    if (math.distance(p.xz, start.xz) < CrossingEndSkip || math.distance(p.xz, end.xz) < CrossingEndSkip) continue;
                    if (math.distance(p.xz, last.xz) < GridGenerator.MinSegmentLength) continue;
                    accepted.Add(t);
                    last = p;
                }
                if (accepted.Count == 0) continue;
                float prev = 0f;
                var original = e.Curve;
                bool cap = e.CapAtEnd;
                for (int k = 0; k <= accepted.Count; k++)
                {
                    float t1 = k < accepted.Count ? accepted[k] : 1f;
                    var piece = original.Sub(prev, t1);
                    if (k == 0)
                    {
                        e.Curve = piece;
                        // Only the last piece keeps the cul-de-sac tip; a mid-street crossing is never one.
                        e.CapAtEnd = false;
                    }
                    else
                    {
                        var copy = e.Clone(piece);
                        copy.CapAtEnd = k == accepted.Count && cap;
                        net.Edges.Add(copy);
                    }
                    prev = t1;
                }
            }
        }

        /// <summary>
        /// Points where existing roads cross the outline: forced nodes of the perimeter road so it
        /// joins those roads instead of passing over them (GRG RingCrossings). Also takes the game's
        /// RoadCrossing attachments when it reports them directly.
        /// </summary>
        public static List<float3> RingCrossings(IReadOnlyList<float3> ring, List<ExistingRoad> existing, List<BoundaryAttachment> attachments)
        {
            var result = new List<float3>();
            int n = ring.Count;
            var pts = new List<float3>();
            foreach (var r in existing)
            {
                pts.Clear();
                r.Curve.Flatten(pts, null, 4f, 6f);
                for (int k = 0; k + 1 < pts.Count; k++)
                {
                    float2 c = pts[k].xz, d = pts[k + 1].xz;
                    for (int i = 0; i < n; i++)
                    {
                        float2 a = ring[i].xz, b = ring[(i + 1) % n].xz;
                        if (Seg.Intersect(a, b, c, d, out float t, out float u) && t > 0f && t < 1f && u >= 0f && u < 1f)
                        {
                            float2 hit = math.lerp(a, b, t);
                            AddUnique(result, new float3(hit.x, math.lerp(ring[i].y, ring[(i + 1) % n].y, t), hit.y), 2f);
                        }
                    }
                }
            }
            if (attachments != null)
                foreach (var a in attachments)
                    if (a.Kind == AttachmentKind.RoadCrossing && MathEx.Finite(a.Position))
                        AddUnique(result, a.Position, 2f);
            return result;
        }

        private static void AddUnique(List<float3> list, float3 p, float tolerance)
        {
            foreach (var q in list) if (math.distance(q.xz, p.xz) < tolerance) return;
            list.Add(p);
        }

        /// <summary>
        /// Marks generated edges that run along an existing road (most of their length inside the two
        /// road bodies, roughly parallel) as removed: the road is already there. Typical case: a drawn
        /// area whose outline follows a street — its perimeter road would be laid on top of it.
        /// </summary>
        public static int RemoveOverlapping(RoadNetwork net, List<ExistingRoad> existing, System.Func<NetRole, float> halfWidth)
        {
            if (existing.Count == 0) return 0;
            int removed = 0;
            foreach (var e in net.Edges)
            {
                if (!e.Alive) continue;
                float hw = halfWidth(e.Role);
                int inside = 0;
                const int samples = 7;
                for (int i = 1; i <= samples; i++)
                {
                    float t = i / (samples + 1f);
                    float2 p = e.Curve.Position(t).xz;
                    float2 dir = e.Curve.TangentXZ(t);
                    foreach (var r in existing)
                    {
                        float d = r.Curve.DistanceXZ(p, out float rt);
                        if (d > (r.HalfWidth + hw) * 0.8f) continue;
                        if (math.abs(math.dot(dir, r.Curve.TangentXZ(rt))) < 0.85f) continue;
                        inside++;
                        break;
                    }
                }
                if (inside >= 5)
                {
                    e.Removed = true;
                    removed++;
                }
            }
            return removed;
        }

        /// <summary>
        /// Decides for every node whether it joins an existing node or edge (GRG MakeCoursePos rules
        /// 1 and 2) and moves it exactly onto that point. Rule 3 (free end) leaves the node alone.
        /// </summary>
        public static int Attach(RoadNetwork net, List<ExistingRoad> existing, List<BoundaryAttachment> attachments)
        {
            int attached = 0;
            int[] degree = net.Degrees(true);
            for (int ni = 0; ni < net.Nodes.Count; ni++)
            {
                if (degree[ni] == 0) continue;
                var node = net.Nodes[ni];
                float2 p = node.Position.xz;
                bool done = false;

                // 1) An existing node the game reported on the outline (block/perimeter pick).
                if (attachments != null)
                {
                    float best = NodeSnapDistance;
                    foreach (var a in attachments)
                    {
                        if (a.Kind != AttachmentKind.RoadNode || !MathEx.Finite(a.Position)) continue;
                        float d = math.distance(a.Position.xz, p);
                        if (d < best)
                        {
                            best = d;
                            node.Attach = a.ExternalId;
                            node.AttachT = 0f;
                            node.Position = a.Position;
                            done = true;
                        }
                    }
                }

                // 2) An existing edge: split it there, or use its end node when the point is at an end.
                if (!done)
                {
                    float best = float.MaxValue;
                    foreach (var r in existing)
                    {
                        float d = r.Curve.DistanceXZ(p, out float t);
                        if (d >= SnapDistance(r) || d >= best) continue;
                        best = d;
                        if (t <= EndNodeT) t = 0f; else if (t >= 1f - EndNodeT) t = 1f;
                        node.Attach = r.Id;
                        node.AttachT = t;
                        node.Position = t == 0f ? r.Curve.A : t == 1f ? r.Curve.D : r.Curve.Position(t);
                        done = true;
                    }
                }
                if (done) attached++;
            }
            // Move curve ends onto the snapped nodes.
            foreach (var e in net.Edges)
            {
                if (e.Discarded) continue;
                e.Curve = e.Curve.WithEnds(net.Nodes[e.N0].Position, net.Nodes[e.N1].Position);
            }
            return attached;
        }

        private static SegmentIndex BuildIndex(List<ExistingRoad> existing, out List<float> chordTs)
        {
            chordTs = new List<float>();
            var index = new SegmentIndex(32f);
            var pts = new List<float3>();
            var ts = new List<float>();
            for (int r = 0; r < existing.Count; r++)
            {
                pts.Clear(); ts.Clear();
                existing[r].Curve.Flatten(pts, ts, 2f, 4f);
                for (int i = 0; i + 1 < pts.Count; i++)
                {
                    index.Add(pts[i].xz, pts[i + 1].xz, r);
                    chordTs.Add(ts[i]);
                }
            }
            return index;
        }
    }
}
