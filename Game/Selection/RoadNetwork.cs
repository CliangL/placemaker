// Placemaker — read-only view of the game's road network for block / perimeter selection.
//
// Adapter from ECS to Core's IRoadGraph (pure face tracing / Dijkstra in Core/Tool/RoadGraph.cs),
// plus the geometry the selection needs: nearest road to the cursor (net search tree, never a
// scan over all nodes — GRG's overlay walked every node each frame), curb-side curves to turn a
// loop of road edges into the block's inner outline, and the attachments for the area.

using System;
using System.Collections.Generic;
using Colossal.Collections;
using Colossal.Mathematics;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.Tools;
using Placemaker.Core;
using Placemaker.Core.Geometry;
using Placemaker.Core.Tool;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Placemaker.Game.Selection
{
    public sealed class RoadNetwork : IRoadGraph
    {
        private readonly EntityManager _em;
        private readonly global::Game.Net.SearchSystem _search;
        private readonly Dictionary<long, List<RoadLink>> _linkCache = new Dictionary<long, List<RoadLink>>();
        private readonly Dictionary<Entity, bool> _boundaryCache = new Dictionary<Entity, bool>();

        public RoadNetwork(EntityManager em, global::Game.Net.SearchSystem search)
        {
            _em = em;
            _search = search;
        }

        /// <summary>Call once per frame (or after the network changed): link lists are cached per frame.</summary>
        public void BeginFrame()
        {
            _linkCache.Clear();
            _boundaryCache.Clear();
        }

        /// <summary>Ground-level road edges bound blocks; tunnels, bridges and building-internal nets do not.</summary>
        public bool IsBoundaryEdge(Entity e)
        {
            if (_boundaryCache.TryGetValue(e, out bool cached)) return cached;
            bool ok = ComputeBoundary(e);
            _boundaryCache[e] = ok;
            return ok;
        }

        private bool ComputeBoundary(Entity e)
        {
            if (e == Entity.Null || !_em.Exists(e)) return false;
            if (!_em.HasComponent<Edge>(e) || !_em.HasComponent<Curve>(e) || !_em.HasComponent<Road>(e)) return false;
            if (_em.HasComponent<Temp>(e) || _em.HasComponent<Deleted>(e)) return false;
            if (_em.HasComponent<Owner>(e))
            {
                var owner = _em.GetComponentData<Owner>(e).m_Owner;
                if (owner != Entity.Null && _em.Exists(owner) && _em.HasComponent<global::Game.Objects.Object>(owner)) return false;
            }
            if (_em.HasComponent<Composition>(e))
            {
                var comp = _em.GetComponentData<Composition>(e).m_Edge;
                if (comp != Entity.Null && _em.HasComponent<NetCompositionData>(comp))
                {
                    var flags = _em.GetComponentData<NetCompositionData>(comp).m_Flags.m_General;
                    if ((flags & (CompositionFlags.General.Elevated | CompositionFlags.General.Tunnel)) != 0) return false;
                }
            }
            return true;
        }

        public IReadOnlyList<RoadLink> Links(long nodeId)
        {
            if (_linkCache.TryGetValue(nodeId, out var cached)) return cached;
            var list = new List<RoadLink>(4);
            _linkCache[nodeId] = list;
            var node = EntityIds.Resolve(_em, nodeId);
            if (node == Entity.Null || !_em.HasComponent<Node>(node) || !_em.HasBuffer<ConnectedEdge>(node)) return list;
            var edges = _em.GetBuffer<ConnectedEdge>(node, true);
            for (int i = 0; i < edges.Length; i++)
            {
                var e = edges[i].m_Edge;
                if (!IsBoundaryEdge(e)) continue;
                var edge = _em.GetComponentData<Edge>(e);
                bool atStart = edge.m_Start == node, atEnd = edge.m_End == node;
                if (!atStart && !atEnd) continue;            // node sits in the middle of this edge (side connection)
                if (atStart && atEnd) continue;              // degenerate loop edge
                var curve = _em.GetComponentData<Curve>(e);
                float2 dir = atStart
                    ? math.normalizesafe(MathUtils.StartTangent(curve.m_Bezier).xz)
                    : math.normalizesafe(-MathUtils.EndTangent(curve.m_Bezier).xz);
                if (math.lengthsq(dir) < 0.5f)
                    dir = math.normalizesafe((atStart ? curve.m_Bezier.d - curve.m_Bezier.a : curve.m_Bezier.a - curve.m_Bezier.d).xz, new float2(1, 0));
                list.Add(new RoadLink
                {
                    Edge = EntityIds.Pack(e),
                    Other = EntityIds.Pack(atStart ? edge.m_End : edge.m_Start),
                    Direction = dir,
                    Length = math.max(0.1f, curve.m_Length),
                });
            }
            return list;
        }

        // ───────────── spatial queries ─────────────

        private struct EdgeIterator : INativeQuadTreeIterator<Entity, QuadTreeBoundsXZ>
        {
            public Bounds2 Bounds;
            public NativeList<Entity> Results;
            public bool Intersect(QuadTreeBoundsXZ bounds) => MathUtils.Intersect(bounds.m_Bounds.xz, Bounds);
            public void Iterate(QuadTreeBoundsXZ bounds, Entity item)
            {
                if (MathUtils.Intersect(bounds.m_Bounds.xz, Bounds)) Results.Add(item);
            }
        }

        /// <summary>Net entities (edges and nodes) whose bounds touch the rectangle.</summary>
        public void Query(float2 min, float2 max, List<Entity> into)
        {
            into.Clear();
            if (_search == null) return;
            var tree = _search.GetNetSearchTree(true, out var deps);
            deps.Complete();
            var results = new NativeList<Entity>(32, Allocator.Temp);
            try
            {
                var it = new EdgeIterator { Bounds = new Bounds2(min, max), Results = results };
                tree.Iterate(ref it);
                for (int i = 0; i < results.Length; i++) into.Add(results[i]);
            }
            finally { results.Dispose(); }
        }

        private readonly List<Entity> _scratch = new List<Entity>();

        // ───────────── block picking (Core PlanarGraph) ─────────────

        private const float GraphRadius = 900f;
        private const float GraphRebuildDistance = 350f;
        private const float GraphMaxAge = 2f;
        private PlanarGraph _planar;
        private float2 _planarCenter;
        private float _planarBuilt = -100f;
        private readonly List<float2> _geomScratch = new List<float2>();

        /// <summary>
        /// Planar graph of block-bounding roads around <paramref name="p"/>. Rebuilt when the cursor
        /// leaves the covered area or every couple of seconds (the network may have changed) —
        /// a bounded query, never the whole map.
        /// </summary>
        private PlanarGraph GraphAround(float2 p, float now)
        {
            if (_planar != null && math.distance(p, _planarCenter) < GraphRebuildDistance && now - _planarBuilt < GraphMaxAge) return _planar;
            var g = new PlanarGraph();
            Query(p - GraphRadius, p + GraphRadius, _scratch);
            var seen = new HashSet<Entity>();
            foreach (var e in _scratch)
            {
                if (!seen.Add(e) || !IsBoundaryEdge(e)) continue;
                var edge = _em.GetComponentData<Edge>(e);
                if (!_em.HasComponent<Node>(edge.m_Start) || !_em.HasComponent<Node>(edge.m_End)) continue;
                long a = EntityIds.Pack(edge.m_Start), b = EntityIds.Pack(edge.m_End);
                if (!g.HasNode(a)) g.AddNode(a, _em.GetComponentData<Node>(edge.m_Start).m_Position.xz);
                if (!g.HasNode(b)) g.AddNode(b, _em.GetComponentData<Node>(edge.m_End).m_Position.xz);
                _geomScratch.Clear();
                foreach (var q in SampleCurve(_em.GetComponentData<Curve>(e).m_Bezier, true, 6)) _geomScratch.Add(q.xz);
                g.AddEdge(EntityIds.Pack(e), a, b, _geomScratch);
            }
            g.Prepare();
            _planar = g;
            _planarCenter = p;
            _planarBuilt = now;
            return g;
        }

        /// <summary>Forget the cached graph (after a build changed the roads).</summary>
        public void InvalidateGraph() => _planar = null;

        /// <summary>The block containing <paramref name="p"/> as a CCW loop of half-edges (face on the left).</summary>
        public FacePickStatus PickBlock(float2 p, float now, List<HalfEdge> loop)
        {
            loop.Clear();
            var g = GraphAround(p, now);
            var pick = g.PickFace(p, GraphRadius, 4000, true);
            if (!pick.Ok) return pick.Status;
            int n = pick.EdgeIds.Count;
            for (int i = 0; i < n; i++)
                loop.Add(new HalfEdge(pick.EdgeIds[i], pick.NodeIds[i], pick.NodeIds[(i + 1) % n]));
            return loop.Count >= 2 ? FacePickStatus.Ok : FacePickStatus.Degenerate;
        }

        // ───────────── loop → area ─────────────

        public float3 NodePosition(long nodeId)
        {
            var n = EntityIds.Resolve(_em, nodeId);
            return n != Entity.Null && _em.HasComponent<Node>(n) ? _em.GetComponentData<Node>(n).m_Position : float3.zero;
        }

        public bool TryCurve(long edgeId, out Bezier4x3 curve, out Entity edge)
        {
            edge = EntityIds.Resolve(_em, edgeId);
            curve = default;
            if (edge == Entity.Null || !_em.HasComponent<Curve>(edge)) return false;
            curve = _em.GetComponentData<Curve>(edge).m_Bezier;
            return true;
        }

        /// <summary>Centerline polygon of a loop (node to node along the curves), for containment and orientation tests.</summary>
        public List<float3> CenterRing(IReadOnlyList<HalfEdge> loop, int samplesPerEdge = 6)
        {
            var pieces = new List<IReadOnlyList<float3>>(loop.Count);
            foreach (var h in loop)
            {
                if (!TryCurve(h.Edge, out var c, out var edge)) { pieces.Add(new[] { NodePosition(h.From), NodePosition(h.To) }); continue; }
                bool forward = _em.GetComponentData<Edge>(edge).m_Start == EntityIds.Unpack(h.From);
                pieces.Add(SampleCurve(c, forward, samplesPerEdge));
            }
            return RoadGraphAlgorithms.Stitch(pieces);
        }

        public float RoadWidth(Entity edge)
        {
            if (_em.HasComponent<Composition>(edge))
            {
                var comp = _em.GetComponentData<Composition>(edge).m_Edge;
                if (comp != Entity.Null && _em.HasComponent<NetCompositionData>(comp)) return _em.GetComponentData<NetCompositionData>(comp).m_Width;
            }
            return 8f;
        }

        /// <summary>
        /// The area enclosed by a CCW loop of road half-edges: the curb lines facing the inside
        /// (so a park never overlaps the road), joined straight across intersections. Falls back
        /// to the centerline polygon when curb geometry is missing or would self-intersect.
        /// Attachments: one RoadAlongEdge per road (at its middle) and one RoadNode per corner node.
        /// </summary>
        public AreaShape BuildArea(IReadOnlyList<HalfEdge> loop, AreaSource source)
        {
            var center = CenterRing(loop);
            var center2 = Poly.To2D(center);
            var curbPieces = new List<IReadOnlyList<float3>>(loop.Count);
            bool curbsOk = true;
            foreach (var h in loop)
            {
                var piece = CurbPiece(h, center2);
                if (piece == null) { curbsOk = false; break; }
                curbPieces.Add(piece);
            }
            float3[] ring = null;
            if (curbsOk)
            {
                var curb = RoadGraphAlgorithms.Stitch(curbPieces, 0.3f);
                var clean = Poly.Clean(Poly.To2D(curb), 0.3f);
                if (clean.Length >= 3 && Poly.IsSimple(clean) && Poly.SignedArea(clean) > 1f)
                    ring = Lift(clean, curb);
            }
            if (ring == null)
            {
                var clean = Poly.Clean(center2, 0.3f);
                if (clean.Length < 3 || !Poly.IsSimple(clean)) return null;
                ring = Lift(clean, center);
            }

            var shape = new AreaShape { Ring = ring, Source = source };
            foreach (var h in loop)
            {
                if (!TryCurve(h.Edge, out var c, out var edge)) continue;
                bool forward = _em.GetComponentData<Edge>(edge).m_Start == EntityIds.Unpack(h.From);
                var mid = MathUtils.Position(c, 0.5f);
                var tangent = math.normalizesafe(MathUtils.Tangent(c, 0.5f).xz) * (forward ? 1f : -1f);
                Composer.ClosestSegment(ring, mid.xz, out int seg, out float st, out _);
                var onRing = seg >= 0 ? math.lerp(ring[seg], ring[(seg + 1) % ring.Length], st) : mid;
                shape.Attachments.Add(new BoundaryAttachment
                {
                    Kind = AttachmentKind.RoadAlongEdge, ExternalId = h.Edge, ExternalT = 0.5f, Position = onRing,
                    Direction = new float3(tangent.x, 0, tangent.y), RoadWidth = RoadWidth(edge), RingSegment = seg, RingT = st,
                });
            }
            foreach (var h in loop)
            {
                var p = NodePosition(h.From);
                Composer.ClosestSegment(ring, p.xz, out int seg, out float st, out _);
                shape.Attachments.Add(new BoundaryAttachment
                {
                    Kind = AttachmentKind.RoadNode, ExternalId = h.From, Position = p, RingSegment = seg, RingT = st,
                });
            }
            return shape;
        }

        private IReadOnlyList<float3> CurbPiece(HalfEdge h, float2[] inside)
        {
            var edgeEntity = EntityIds.Resolve(_em, h.Edge);
            if (edgeEntity == Entity.Null || !_em.HasComponent<EdgeGeometry>(edgeEntity)) return null;
            var g = _em.GetComponentData<EdgeGeometry>(edgeEntity);
            bool forward = _em.GetComponentData<Edge>(edgeEntity).m_Start == EntityIds.Unpack(h.From);
            // Decide the side by geometry instead of trusting left/right naming: the curb whose
            // middle lies inside the loop's centerline polygon faces the block.
            var leftMid = MathUtils.Position(g.m_Start.m_Left, 1f);
            var rightMid = MathUtils.Position(g.m_Start.m_Right, 1f);
            bool leftInside = Poly.Contains(inside, leftMid.xz);
            bool rightInside = Poly.Contains(inside, rightMid.xz);
            bool useLeft;
            if (leftInside != rightInside) useLeft = leftInside;
            else
            {
                // Degenerate (very thin block): fall back to the travel-direction rule.
                var c = _em.GetComponentData<Curve>(edgeEntity).m_Bezier;
                var t = MathUtils.Tangent(c, 0.5f).xz * (forward ? 1f : -1f);
                var toLeft = leftMid.xz - MathUtils.Position(c, 0.5f).xz;
                useLeft = (t.x * toLeft.y - t.y * toLeft.x) > 0f;
            }
            var a = useLeft ? g.m_Start.m_Left : g.m_Start.m_Right;
            var b = useLeft ? g.m_End.m_Left : g.m_End.m_Right;
            var pts = new List<float3>();
            pts.AddRange(SampleCurve(a, true, 6));
            pts.AddRange(SampleCurve(b, true, 6));
            if (!forward) pts.Reverse();
            return pts;
        }

        public static List<float3> SampleCurve(Bezier4x3 c, bool forward, int steps)
        {
            var pts = new List<float3>(steps + 1);
            float chord = math.distance(c.a.xz, c.d.xz);
            float hull = math.distance(c.a.xz, c.b.xz) + math.distance(c.b.xz, c.c.xz) + math.distance(c.c.xz, c.d.xz);
            // Straight segments need only their ends.
            int n = hull - chord < 0.05f ? 1 : math.max(1, steps);
            for (int i = 0; i <= n; i++)
            {
                float t = forward ? (float)i / n : 1f - (float)i / n;
                pts.Add(MathUtils.Position(c, t));
            }
            return pts;
        }

        /// <summary>Heights for cleaned 2D points: nearest source sample's height.</summary>
        private static float3[] Lift(float2[] clean, List<float3> source)
        {
            var ccw = Poly.EnsureCcw(clean);
            var r = new float3[ccw.Length];
            for (int i = 0; i < ccw.Length; i++)
            {
                float best = float.MaxValue, y = 0;
                foreach (var s in source)
                {
                    float d = math.distancesq(s.xz, ccw[i]);
                    if (d < best) { best = d; y = s.y; }
                }
                r[i] = new float3(ccw[i].x, y, ccw[i].y);
            }
            return r;
        }
    }
}
