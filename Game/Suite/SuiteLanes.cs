// In-game test suite: the lane graph around a build, as the pathfinder sees it (Game.Net.Lane start / middle / end
// PathNodes: a lane continues at its end node; side connections and parking lanes attach to another lane's middle
// node, matched without the curve position — Game.Pathfind's own rule). Used for the physical checks:
//   * entrancesUsable: from the city street's lanes, a car (or pedestrian) reaches the build's lanes through an entry
//     near each entrance marker, and no uncut fence or hedge lies across the first metres of that drive;
//   * netsConnected: every member net is reachable from a city street through lanes (side connections included);
//   * stallsUsable: every parking stall's own ParkingLane is reachable by car from the city;
//   * fences: fence lanes (UtilityLaneData type Fence, not names) by owner, cut spans removed.

using System;
using System.Collections.Generic;
using System.Linq;
using Colossal.Mathematics;
using Game.Common;
using Game.Net;
using Game.Pathfind;
using Game.Prefabs;
using Game.Tools;
using Newtonsoft.Json.Linq;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Placemaker.Game.Suite
{
    internal sealed class SuiteLanes
    {
        [Flags]
        public enum Mode : byte { None = 0, Car = 1, Parking = 2, Pedestrian = 4, Connection = 8 }

        public enum Who : byte { Other = 0, Member = 1, City = 2 }

        public struct LaneInfo
        {
            public Entity E;
            public PathNode S, M, T;
            public Bezier4x3 Curve;
            public Mode Mode;
            public bool Bidir, CarOk, PedOk;
            public Entity Owner, Top;
            public Who Who;
        }

        private readonly EntityManager _em;
        private readonly SuiteProbe _probe;
        public readonly List<LaneInfo> Lanes = new List<LaneInfo>();
        private readonly Dictionary<PathNode, List<int>> _byStart = new Dictionary<PathNode, List<int>>();
        private readonly Dictionary<PathNode, List<int>> _byEnd = new Dictionary<PathNode, List<int>>();
        private readonly Dictionary<PathNode, List<int>> _byMiddle = new Dictionary<PathNode, List<int>>();
        public readonly Dictionary<Entity, int> Index = new Dictionary<Entity, int>();

        public SuiteLanes(EntityManager em, SuiteProbe probe)
        {
            _em = em;
            _probe = probe;
        }

        private static void Add(Dictionary<PathNode, List<int>> d, PathNode n, int i)
        {
            if (!d.TryGetValue(n, out var l)) d[n] = l = new List<int>(2);
            l.Add(i);
        }

        /// <summary>Every lane whose curve touches the box, classified by mode and by who owns it (the build, a city street, other).</summary>
        public void Build(float2 min, float2 max, HashSet<Entity> members, int groupId)
        {
            using (var q = _em.CreateEntityQuery(ComponentType.ReadOnly<Lane>(), ComponentType.ReadOnly<Curve>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>()))
            using (var all = q.ToEntityArray(Allocator.Temp))
            {
                foreach (var e in all)
                {
                    if (_em.HasComponent<global::Game.Net.MasterLane>(e)) continue;
                    var c = _em.GetComponentData<Curve>(e).m_Bezier;
                    var lo = math.min(math.min(c.a.xz, c.b.xz), math.min(c.c.xz, c.d.xz));
                    var hi = math.max(math.max(c.a.xz, c.b.xz), math.max(c.c.xz, c.d.xz));
                    if (hi.x < min.x || lo.x > max.x || hi.y < min.y || lo.y > max.y) continue;
                    var info = new LaneInfo { E = e, Curve = c };
                    if (_em.HasComponent<global::Game.Net.CarLane>(e))
                    {
                        var cl = _em.GetComponentData<global::Game.Net.CarLane>(e);
                        info.Mode |= Mode.Car;
                        info.Bidir = (cl.m_Flags & CarLaneFlags.Twoway) != 0;
                        info.CarOk = (cl.m_Flags & CarLaneFlags.Forbidden) == 0;
                    }
                    if (_em.HasComponent<global::Game.Net.ParkingLane>(e)) { info.Mode |= Mode.Parking; info.CarOk = true; info.Bidir = true; }
                    if (_em.HasComponent<global::Game.Net.PedestrianLane>(e)) { info.Mode |= Mode.Pedestrian; info.PedOk = true; info.Bidir = true; }
                    if (_em.HasComponent<global::Game.Net.ConnectionLane>(e))
                    {
                        var cn = _em.GetComponentData<global::Game.Net.ConnectionLane>(e);
                        info.Mode |= Mode.Connection;
                        info.Bidir = true;
                        if ((cn.m_Flags & (ConnectionLaneFlags.Road | ConnectionLaneFlags.Parking)) != 0) info.CarOk = true;
                        if ((cn.m_Flags & ConnectionLaneFlags.Pedestrian) != 0) info.PedOk = true;
                    }
                    if (info.Mode == Mode.None) continue;   // utility lanes (fences, pipes) and tracks are not walked
                    var lane = _em.GetComponentData<Lane>(e);
                    info.S = lane.m_StartNode.StripCurvePos();
                    info.M = lane.m_MiddleNode.StripCurvePos();
                    info.T = lane.m_EndNode.StripCurvePos();
                    // lanes hung on other lanes (parking spaces of objects, area and building connections): the pathfinder
                    // puts their ends on the connected lanes' middle nodes (LanesModifiedSystem.CheckLaneConnections)
                    if (_em.HasComponent<global::Game.Net.LaneConnection>(e))
                    {
                        var lc = _em.GetComponentData<global::Game.Net.LaneConnection>(e);
                        if (lc.m_StartLane != Entity.Null && _em.HasComponent<Lane>(lc.m_StartLane)) info.S = _em.GetComponentData<Lane>(lc.m_StartLane).m_MiddleNode.StripCurvePos();
                        if (lc.m_EndLane != Entity.Null && _em.HasComponent<Lane>(lc.m_EndLane)) info.T = _em.GetComponentData<Lane>(lc.m_EndLane).m_MiddleNode.StripCurvePos();
                    }
                    info.Owner = _em.HasComponent<Owner>(e) ? _em.GetComponentData<Owner>(e).m_Owner : Entity.Null;
                    info.Who = Classify(e, members, groupId, out info.Top);
                    int i = Lanes.Count;
                    Lanes.Add(info);
                    Index[e] = i;
                    Add(_byStart, info.S, i);
                    Add(_byEnd, info.T, i);
                    Add(_byMiddle, info.M, i);
                    if ((info.Mode & Mode.Parking) != 0)
                    {
                        var pl = _em.GetComponentData<global::Game.Net.ParkingLane>(e);
                        if ((pl.m_Flags & global::Game.Net.ParkingLaneFlags.AdditionalStart) != 0) Add(_byStart, pl.m_AdditionalStartNode.StripCurvePos(), i);
                    }
                }
            }
        }

        private Who Classify(Entity lane, HashSet<Entity> members, int groupId, out Entity top)
        {
            var e = lane;
            top = lane;
            for (int depth = 0; depth < 10 && _em.Exists(e); depth++)
            {
                if (members.Contains(e) || (groupId > 0 && _probe.GroupOf(e) == groupId)) { top = e; return Who.Member; }
                if (!_em.HasComponent<Owner>(e)) break;
                var o = _em.GetComponentData<Owner>(e).m_Owner;
                if (o == Entity.Null || !_em.Exists(o)) break;
                e = o;
            }
            top = e;
            return _probe.CityNet(e) ? Who.City : Who.Other;
        }

        private bool Allowed(int j, bool car) => car ? Lanes[j].CarOk : Lanes[j].PedOk;

        /// <summary>Lanes reachable from the sources: car = directed (one-way lanes forward), pedestrian = both ways. prev[j] = the lane it was entered from.</summary>
        public bool[] Reach(Func<int, bool> source, bool car, out int[] prev)
        {
            int n = Lanes.Count;
            var seen = new bool[n];
            prev = new int[n];
            for (int i = 0; i < n; i++) prev[i] = -1;
            var q = new Queue<int>();
            for (int i = 0; i < n; i++)
                if (Allowed(i, car) && source(i)) { seen[i] = true; q.Enqueue(i); }
            var nodes = new List<PathNode>(3);
            while (q.Count > 0)
            {
                int i = q.Dequeue();
                var L = Lanes[i];
                nodes.Clear();
                nodes.Add(L.T);
                if (L.Bidir || !car) nodes.Add(L.S);
                nodes.Add(L.M);   // side exits and parking lanes hang on the middle node
                foreach (var nd in nodes)
                {
                    Visit(_byStart, nd, i, car, seen, prev, q, false);
                    Visit(_byEnd, nd, i, car, seen, prev, q, true);
                    if (!nd.Equals(L.M)) Visit(_byMiddle, nd, i, car, seen, prev, q, false);   // entering another lane mid-way
                }
            }
            return seen;
        }

        private void Visit(Dictionary<PathNode, List<int>> d, PathNode nd, int from, bool car, bool[] seen, int[] prev, Queue<int> q, bool byEnd)
        {
            if (!d.TryGetValue(nd, out var list)) return;
            foreach (int j in list)
            {
                if (seen[j] || !Allowed(j, car)) continue;
                if (byEnd && car && !Lanes[j].Bidir) continue;   // a one-way lane is not entered at its end
                seen[j] = true;
                prev[j] = from;
                q.Enqueue(j);
            }
        }

        /// <summary>Undirected: lanes connected (any mode) to the sources.</summary>
        public bool[] Connected(Func<int, bool> source)
        {
            int n = Lanes.Count;
            var seen = new bool[n];
            var q = new Queue<int>();
            for (int i = 0; i < n; i++) if (source(i)) { seen[i] = true; q.Enqueue(i); }
            while (q.Count > 0)
            {
                int i = q.Dequeue();
                var L = Lanes[i];
                foreach (var nd in new[] { L.S, L.M, L.T })
                    foreach (var d in new[] { _byStart, _byEnd, _byMiddle })
                        if (d.TryGetValue(nd, out var list))
                            foreach (int j in list) if (!seen[j]) { seen[j] = true; q.Enqueue(j); }
            }
            return seen;
        }

        /// <summary>Where a route crosses from a non-member lane onto a member lane: the member lane and the point.</summary>
        public List<(int Lane, int From, float3 At)> Entries(bool[] reached, int[] prev)
        {
            var r = new List<(int, int, float3)>();
            for (int j = 0; j < Lanes.Count; j++)
            {
                if (!reached[j] || Lanes[j].Who != Who.Member) continue;
                int p = prev[j];
                if (p < 0 || Lanes[p].Who == Who.Member) continue;
                var from = Lanes[p].Curve;
                var c = Lanes[j].Curve;
                float3 end = from.d;
                MathUtils.Distance(c.xz, end.xz, out float t);
                r.Add((j, p, MathUtils.Position(c, t)));
            }
            return r;
        }

        // ───────────── fences ─────────────

        public struct Fence
        {
            public Entity E;
            public string Name;
            public Bezier4x3 Curve;
            public float Length, Visible;
            public List<Bounds1> Cuts;
            public bool Overridden;
            public Entity Top;
            public Who Who;
        }

        /// <summary>Fence lanes (UtilityLaneData has the Fence type) touching the box, with their cut spans.</summary>
        public List<Fence> Fences(float2 min, float2 max, HashSet<Entity> members, int groupId)
        {
            var r = new List<Fence>();
            using (var q = _em.CreateEntityQuery(ComponentType.ReadOnly<global::Game.Net.UtilityLane>(), ComponentType.ReadOnly<Curve>(), ComponentType.ReadOnly<PrefabRef>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>()))
            using (var all = q.ToEntityArray(Allocator.Temp))
            {
                foreach (var e in all)
                {
                    var pf = _em.GetComponentData<PrefabRef>(e).m_Prefab;
                    if (!_em.HasComponent<UtilityLaneData>(pf) || (_em.GetComponentData<UtilityLaneData>(pf).m_UtilityTypes & UtilityTypes.Fence) == 0) continue;
                    var c = _em.GetComponentData<Curve>(e).m_Bezier;
                    var lo = math.min(c.a.xz, c.d.xz);
                    var hi = math.max(c.a.xz, c.d.xz);
                    if (hi.x < min.x || lo.x > max.x || hi.y < min.y || lo.y > max.y) continue;
                    var f = new Fence { E = e, Name = _probe.PrefabName(e), Curve = c, Length = MathUtils.Length(c), Cuts = new List<Bounds1>(), Overridden = _em.HasComponent<Overridden>(e) };
                    float cut = 0f;
                    if (_em.HasBuffer<CutRange>(e))
                    {
                        var cr = _em.GetBuffer<CutRange>(e, true);
                        for (int i = 0; i < cr.Length; i++) { f.Cuts.Add(cr[i].m_CurveDelta); cut += math.max(0f, cr[i].m_CurveDelta.max - cr[i].m_CurveDelta.min); }
                    }
                    f.Visible = f.Overridden ? 0f : f.Length * math.saturate(1f - cut);
                    f.Who = Classify(e, members, groupId, out f.Top);
                    r.Add(f);
                }
            }
            return r;
        }

        /// <summary>The nearest point of an uncut, visible fence to a short path (within the reach), or null.</summary>
        public static (Fence F, float D)? Across(List<Fence> fences, IList<float2> path, float reach)
        {
            (Fence, float)? best = null;
            foreach (var f in fences)
            {
                if (f.Overridden) continue;
                foreach (var p in path)
                {
                    float d = MathUtils.Distance(f.Curve.xz, p, out float t);
                    if (d > reach) continue;
                    bool cut = false;
                    foreach (var b in f.Cuts) if (t >= b.min - 0.01f && t <= b.max + 0.01f) { cut = true; break; }
                    if (cut) continue;
                    if (best == null || d < best.Value.Item2) best = (f, d);
                }
            }
            return best;
        }

        /// <summary>Points along a lane from curve position t onwards (1 m apart, up to the length).</summary>
        public static List<float2> Along(Bezier4x3 c, float t0, float metres)
        {
            var r = new List<float2>();
            float len = math.max(0.5f, MathUtils.Length(c));
            for (float s = 0f; s <= metres; s += 1f)
            {
                float t = t0 + s / len;
                if (t > 1f) break;
                r.Add(MathUtils.Position(c, t).xz);
            }
            return r;
        }

        private static System.Reflection.FieldInfo s_key;

        /// <summary>A PathNode's search key in hex (owner index, lane, segment), for diagnostics.</summary>
        public static string Key(PathNode n)
        {
            try
            {
                s_key ??= typeof(PathNode).GetField("m_SearchKey", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                object boxed = n;
                return ((ulong)s_key.GetValue(boxed)).ToString("x");
            }
            catch (Exception) { return "?"; }
        }

        /// <summary>Diagnostics: the lanes of an entity (its sub-lanes) and what they connect to in this graph.</summary>
        public JArray Explain(Entity owner, Func<Entity, string> name, int max = 6)
        {
            var a = new JArray();
            if (!_em.Exists(owner) || !_em.HasBuffer<global::Game.Net.SubLane>(owner)) { a.Add("no sub-lanes"); return a; }
            var sl = _em.GetBuffer<global::Game.Net.SubLane>(owner, true);
            for (int i = 0; i < sl.Length && a.Count < max; i++)
            {
                var e = sl[i].m_SubLane;
                if (!Index.TryGetValue(e, out int idx)) { a.Add($"{name(e)}: not in the graph (mode none or outside the box)"); continue; }
                var L = Lanes[idx];
                string Links(Dictionary<PathNode, List<int>> d, PathNode n) => d.TryGetValue(n, out var l) ? string.Join(",", l.Where(j => j != idx).Take(4).Select(j => Lanes[j].Who + ":" + name(Lanes[j].Owner) + "/" + Lanes[j].Mode)) : "";
                a.Add(new JObject
                {
                    ["lane"] = name(e), ["mode"] = L.Mode.ToString(), ["who"] = L.Who.ToString(), ["bidir"] = L.Bidir, ["carOk"] = L.CarOk, ["pedOk"] = L.PedOk,
                    ["S"] = Key(L.S), ["M"] = Key(L.M), ["T"] = Key(L.T),
                    ["endsAtS"] = Links(_byEnd, L.S), ["startsAtT"] = Links(_byStart, L.T), ["startsAtM"] = Links(_byStart, L.M), ["endsAtM"] = Links(_byEnd, L.M), ["middleAtS"] = Links(_byMiddle, L.S), ["middleAtT"] = Links(_byMiddle, L.T),
                });
            }
            return a;
        }

        public JObject Stats()
        {
            int member = Lanes.Count(l => l.Who == Who.Member), city = Lanes.Count(l => l.Who == Who.City);
            return new JObject { ["lanes"] = Lanes.Count, ["member"] = member, ["city"] = city };
        }
    }
}
