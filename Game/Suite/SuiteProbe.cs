// In-game test suite: the live census of one scenario's site (docs/teardown/10-ingame-suite.md §4.5).
// Before the build it snapshots what stands in the site (buildings, areas, objects, nets, ground heights); after the
// build and after growth it compares: planned vs live per kind/prefab, things the build added that are not its members
// (vanilla lots, their fences), crop-looking assets, zone cells and which road owns them, growables and the road they
// face, parking stalls far from any lane, nets of the build cut off from the city, unplanned pits, buildings lost.

using System;
using System.Collections.Generic;
using System.Linq;
using Colossal.Mathematics;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.Simulation;
using Game.Tools;
using Game.Zones;
using Newtonsoft.Json.Linq;
using Placemaker.Core;
using Placemaker.Core.Geometry;
using Placemaker.Core.Suite;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Placemaker.Game.Suite
{
    internal sealed class SiteSnapshot
    {
        public float2[] Ring;
        public float2 Min, Max;
        public readonly HashSet<Entity> Buildings = new HashSet<Entity>();
        public readonly Dictionary<Entity, string> BuildingNames = new Dictionary<Entity, string>();
        public readonly HashSet<Entity> Areas = new HashSet<Entity>();
        public readonly HashSet<Entity> Objects = new HashSet<Entity>();
        public readonly HashSet<Entity> Edges = new HashSet<Entity>();
        public readonly List<float2> Samples = new List<float2>();
        public readonly List<float> Heights = new List<float>();
    }

    internal sealed class SuiteProbe
    {
        private readonly World _world;
        private readonly EntityManager _em;
        private readonly PrefabSystem _prefabs;
        private readonly TerrainSystem _terrain;

        public SuiteProbe(World world)
        {
            _world = world;
            _em = world.EntityManager;
            _prefabs = world.GetOrCreateSystemManaged<PrefabSystem>();
            _terrain = world.GetOrCreateSystemManaged<TerrainSystem>();
        }

        public string PrefabName(Entity e)
        {
            try
            {
                if (!_em.Exists(e) || !_em.HasComponent<PrefabRef>(e)) return "";
                var p = _em.GetComponentData<PrefabRef>(e).m_Prefab;
                return _prefabs.GetPrefab<PrefabBase>(p)?.name ?? "";
            }
            catch (Exception) { return ""; }
        }

        private Entity PrefabOf(Entity e) => _em.HasComponent<PrefabRef>(e) ? _em.GetComponentData<PrefabRef>(e).m_Prefab : Entity.Null;

        private bool Alive(Entity e) => _em.Exists(e) && !_em.HasComponent<Deleted>(e) && !_em.HasComponent<Temp>(e);

        private static bool InBox(float2 p, float2 min, float2 max) => p.x >= min.x && p.x <= max.x && p.y >= min.y && p.y <= max.y;

        // ───────────── snapshot ─────────────

        public SiteSnapshot Snapshot(float2[] ring, float margin = 6f)
        {
            var s = new SiteSnapshot { Ring = ring };
            float2 min = ring[0], max = ring[0];
            foreach (var p in ring) { min = math.min(min, p); max = math.max(max, p); }
            s.Min = min - margin;
            s.Max = max + margin;
            foreach (var e in Query<global::Game.Buildings.Building>())
            {
                var p = _em.GetComponentData<global::Game.Objects.Transform>(e).m_Position.xz;
                if (InBox(p, s.Min, s.Max) && Poly.Contains(ring, p)) { s.Buildings.Add(e); s.BuildingNames[e] = PrefabName(e); }
            }
            foreach (var e in Query<global::Game.Areas.Area>())
                if (AreaInBox(e, s.Min, s.Max)) s.Areas.Add(e);
            foreach (var e in Query<global::Game.Objects.Object>())
            {
                if (!_em.HasComponent<global::Game.Objects.Transform>(e)) continue;
                var p = _em.GetComponentData<global::Game.Objects.Transform>(e).m_Position.xz;
                if (InBox(p, s.Min, s.Max)) s.Objects.Add(e);
            }
            foreach (var e in Query<Edge>())
                if (EdgeInBox(e, s.Min, s.Max)) s.Edges.Add(e);
            SampleGround(s);
            return s;
        }

        private List<Entity> Query<T>() where T : struct
        {
            var r = new List<Entity>();
            using (var q = _em.CreateEntityQuery(ComponentType.ReadOnly<T>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>()))
            using (var a = q.ToEntityArray(Allocator.Temp))
                foreach (var e in a) r.Add(e);
            return r;
        }

        private bool AreaInBox(Entity e, float2 min, float2 max)
        {
            if (!_em.HasBuffer<global::Game.Areas.Node>(e)) return false;
            var nodes = _em.GetBuffer<global::Game.Areas.Node>(e, true);
            if (nodes.Length == 0) return false;
            float2 c = float2.zero;
            for (int i = 0; i < nodes.Length; i++) c += nodes[i].m_Position.xz;
            c /= nodes.Length;
            return InBox(c, min, max);
        }

        private bool EdgeInBox(Entity e, float2 min, float2 max)
        {
            if (!_em.HasComponent<Curve>(e)) return false;
            var b = _em.GetComponentData<Curve>(e).m_Bezier;
            return InBox(MathUtils.Position(b, 0.5f).xz, min, max);
        }

        private void SampleGround(SiteSnapshot s)
        {
            var h = _terrain.GetHeightData(true);
            const float step = 4f;
            for (float x = s.Min.x; x <= s.Max.x; x += step)
                for (float z = s.Min.y; z <= s.Max.y; z += step)
                {
                    var p = new float2(x, z);
                    if (!Poly.Contains(s.Ring, p)) continue;
                    s.Samples.Add(p);
                    s.Heights.Add(TerrainUtils.SampleHeight(ref h, new float3(x, 0f, z)));
                }
        }

        public List<float> GroundNow(SiteSnapshot s)
        {
            var h = _terrain.GetHeightData(true);
            var r = new List<float>(s.Samples.Count);
            foreach (var p in s.Samples) r.Add(TerrainUtils.SampleHeight(ref h, new float3(p.x, 0f, p.y)));
            return r;
        }

        // ───────────── classification ─────────────

        public string KindOf(Entity e)
        {
            if (_em.HasComponent<Edge>(e)) return "net";
            if (_em.HasComponent<global::Game.Net.Node>(e)) return "node";
            if (_em.HasComponent<global::Game.Areas.Area>(e))
            {
                var p = PrefabOf(e);
                if (p != Entity.Null && _em.HasComponent<SurfaceData>(p)) return "surface";
                if (p != Entity.Null && _em.HasComponent<LotData>(p)) return "lot";
                return "area";
            }
            if (_em.HasComponent<global::Game.Buildings.Building>(e)) return "building";
            if (_em.HasComponent<global::Game.Objects.Object>(e)) return "object";
            if (_em.HasComponent<Block>(e)) return "zoneblock";
            return "other";
        }

        private static string PlanKind(string census) => census == "lot" ? "area" : census;

        // ───────────── after the build ─────────────

        public JObject Census(SiteSnapshot before, int groupId, ComposedPlan plan, string layer, Placemaker.Core.Build.BuildAudit audit)
        {
            var o = new JObject();
            var members = new List<Entity>();
            if (groupId > 0 && Services.Groups != null)
            {
                try { Services.Groups.GetMembers(groupId, members); } catch (Exception e) { o["membersError"] = e.Message; }
            }
            var memberSet = new HashSet<Entity>(members);
            o["groupId"] = groupId;
            o["members"] = members.Count;

            // planned per kind/prefab
            var planned = new Dictionary<string, int>(StringComparer.Ordinal);
            int plannedStalls = 0;
            var stallPrefabs = new HashSet<string>(StringComparer.Ordinal);
            if (plan != null)
                foreach (var l in plan.Layers)
                {
                    foreach (var n in l.Nets) Bump(planned, "net|" + (n.Asset.Id ?? ""));
                    foreach (var ob in l.Objects)
                    {
                        Bump(planned, "object|" + (ob.Asset.Id ?? ""));
                        if (ob.Role == ObjectRole.ParkingBay) { plannedStalls++; if (!string.IsNullOrEmpty(ob.Asset.Id)) stallPrefabs.Add(ob.Asset.Id); }
                    }
                    foreach (var a in l.Areas) Bump(planned, "area|" + (a.Asset.Id ?? ""));
                    foreach (var sf in l.Surfaces) Bump(planned, "surface|" + (sf.Asset.Id ?? ""));
                }

            // live members per kind/prefab
            var live = new Dictionary<string, int>(StringComparer.Ordinal);
            var liveByKind = new Dictionary<string, int>(StringComparer.Ordinal);
            int liveStalls = 0;
            var stallPositions = new List<float2>();
            var memberEdges = new List<Entity>();
            foreach (var m in members)
            {
                if (!Alive(m)) continue;
                var kind = KindOf(m);
                var name = PrefabName(m);
                Bump(live, PlanKind(kind) + "|" + name);
                Bump(liveByKind, kind);
                if (kind == "net") memberEdges.Add(m);
                if ((kind == "object") && stallPrefabs.Contains(name))
                {
                    liveStalls++;
                    stallPositions.Add(_em.GetComponentData<global::Game.Objects.Transform>(m).m_Position.xz);
                }
            }
            var kinds = new JArray();
            foreach (var key in planned.Keys.Union(live.Keys).OrderBy(k => k, StringComparer.Ordinal))
            {
                var parts = key.Split('|');
                planned.TryGetValue(key, out int pn);
                live.TryGetValue(key, out int ln);
                kinds.Add(new JObject { ["kind"] = parts[0], ["prefab"] = parts.Length > 1 ? parts[1] : "", ["planned"] = pn, ["live"] = ln });
            }
            o["kinds"] = kinds;
            o["liveByKind"] = JObject.FromObject(liveByKind);
            o["hidden"] = HiddenMembers(members);

            // audit
            if (audit != null)
            {
                var rows = new JArray();
                foreach (var r in audit.Rows())
                    rows.Add(new JObject { ["reason"] = r.Reason, ["count"] = r.Count, ["byKind"] = JObject.FromObject(r.ByKind), ["topPrefabs"] = JObject.FromObject(r.ByPrefab.OrderByDescending(kv => kv.Value).Take(5).ToDictionary(kv => kv.Key, kv => kv.Value)) });
                o["audit"] = new JObject { ["planned"] = audit.Planned, ["built"] = audit.Built, ["leftOut"] = audit.LeftOut, ["kept"] = audit.Kept, ["reasons"] = rows };
            }

            // what appeared in the site that is not a member (vanilla lots, their border fences, sub-objects…)
            var foreign = new Dictionary<string, int>(StringComparer.Ordinal);
            var crop = new Dictionary<string, int>(StringComparer.Ordinal);
            int fences = 0, foreignFences = 0;
            void Consider(Entity e, bool member)
            {
                var name = PrefabName(e);
                var kind = KindOf(e);
                if (!member) Bump(foreign, kind + "|" + name);
                if (SuiteVerdicts.IsCropLike(name)) Bump(crop, kind + "|" + name + (member ? "" : " (not a member)"));
                if (SuiteVerdicts.IsFence(name)) { fences++; if (!member) foreignFences++; }
            }
            foreach (var m in members) if (Alive(m)) Consider(m, true);
            foreach (var e in Query<global::Game.Areas.Area>())
                if (!memberSet.Contains(e) && !before.Areas.Contains(e) && AreaInBox(e, before.Min, before.Max)) Consider(e, false);
            foreach (var e in Query<global::Game.Objects.Object>())
            {
                if (memberSet.Contains(e) || before.Objects.Contains(e) || !_em.HasComponent<global::Game.Objects.Transform>(e)) continue;
                var p = _em.GetComponentData<global::Game.Objects.Transform>(e).m_Position.xz;
                if (InBox(p, before.Min, before.Max)) Consider(e, false);
            }
            foreach (var e in Query<Edge>())
                if (!memberSet.Contains(e) && !before.Edges.Contains(e) && EdgeInBox(e, before.Min, before.Max)) Consider(e, false);
            o["notMembers"] = TopDict(foreign, 30);
            o["cropLike"] = JObject.FromObject(crop);
            o["cropLikeCount"] = crop.Values.Sum();
            o["farmType"] = SuiteVerdicts.IsFarmType(layer);
            o["fences"] = new JObject { ["total"] = fences, ["notMembers"] = foreignFences };

            // parking: stalls and their lanes
            // every car lane near the site: street lanes, lot aisles (sub-lanes of areas and buildings), drive paths
            var drivable = new List<Bezier4x3>();
            foreach (var e in Query<global::Game.Net.CarLane>())
            {
                if (!_em.HasComponent<Curve>(e)) continue;
                var c = _em.GetComponentData<Curve>(e).m_Bezier;
                var mid = MathUtils.Position(c, 0.5f).xz;
                if (!InBox(mid, before.Min - 60f, before.Max + 60f)) continue;
                drivable.Add(c);
            }
            int farStalls = 0;
            foreach (var p in stallPositions)
            {
                float best = float.MaxValue;
                foreach (var c in drivable) best = math.min(best, MathUtils.Distance(c.xz, p, out _));
                if (best > 9f) farStalls++;
            }
            o["parking"] = new JObject { ["plannedStalls"] = plannedStalls, ["liveStalls"] = liveStalls, ["stallsFarFromLane"] = farStalls, ["stallPrefabs"] = new JArray(stallPrefabs) };

            // nets of the build cut off from the city
            o["nets"] = Reachability(memberEdges, memberSet);

            // zone cells in the site
            o["zoning"] = Zoning(before, groupId, memberSet, plan);

            // existing buildings
            int lost = 0;
            var lostNames = new JArray();
            foreach (var b in before.Buildings)
                if (!Alive(b)) { lost++; if (lostNames.Count < 10) lostNames.Add(before.BuildingNames.TryGetValue(b, out var n) ? n : "?"); }
            o["existingBuildings"] = new JObject { ["before"] = before.Buildings.Count, ["lost"] = lost, ["lostPrefabs"] = lostNames };
            return o;
        }

        private JObject Reachability(List<Entity> edges, HashSet<Entity> members)
        {
            var o = new JObject { ["edges"] = edges.Count };
            if (edges.Count == 0) return o;
            // Union of member edges connected through shared nodes; a component is "connected" when one of its nodes
            // also carries a non-member edge (a city street).
            var visited = new HashSet<Entity>();
            int components = 0, isolatedComponents = 0, isolatedEdges = 0;
            foreach (var start in edges)
            {
                if (visited.Contains(start)) continue;
                components++;
                bool touchesCity = false;
                int size = 0;
                var stack = new Stack<Entity>();
                stack.Push(start);
                visited.Add(start);
                while (stack.Count > 0)
                {
                    var e = stack.Pop();
                    size++;
                    var edge = _em.GetComponentData<Edge>(e);
                    foreach (var node in new[] { edge.m_Start, edge.m_End })
                    {
                        if (!_em.Exists(node) || !_em.HasBuffer<ConnectedEdge>(node)) continue;
                        var ce = _em.GetBuffer<ConnectedEdge>(node, true);
                        for (int i = 0; i < ce.Length; i++)
                        {
                            var other = ce[i].m_Edge;
                            if (other == e || !Alive(other)) continue;
                            if (!members.Contains(other)) { touchesCity = true; continue; }
                            if (visited.Add(other)) stack.Push(other);
                        }
                    }
                }
                if (!touchesCity) { isolatedComponents++; isolatedEdges += size; }
            }
            o["components"] = components;
            o["isolatedComponents"] = isolatedComponents;
            o["isolatedEdges"] = isolatedEdges;
            return o;
        }

        private JObject Zoning(SiteSnapshot site, int groupId, HashSet<Entity> members, ComposedPlan plan)
        {
            int zoned = 0, blocked = 0, empty = 0;
            var byRoad = new Dictionary<string, int>(StringComparer.Ordinal);
            int own = 0, city = 0;
            foreach (var e in Query<Block>())
            {
                if (!_em.HasBuffer<Cell>(e)) continue;
                var block = _em.GetComponentData<Block>(e);
                if (!InBox(block.m_Position.xz, site.Min - 60f, site.Max + 60f)) continue;
                var owner = _em.HasComponent<Owner>(e) ? _em.GetComponentData<Owner>(e).m_Owner : Entity.Null;
                bool ownRoad = owner != Entity.Null && (members.Contains(owner) || (groupId > 0 && SafeGroupOf(owner) == groupId));
                string road = (ownRoad ? "own:" : "city:") + PrefabName(owner);
                var cells = _em.GetBuffer<Cell>(e, true);
                for (int y = 0; y < block.m_Size.y; y++)
                    for (int x = 0; x < block.m_Size.x; x++)
                    {
                        int i = y * block.m_Size.x + x;
                        if (i >= cells.Length) continue;
                        var cell = cells[i];
                        if ((cell.m_State & CellFlags.Visible) == 0) continue;
                        var p = ZoneUtils.GetCellPosition(block, new int2(x, y)).xz;
                        if (!Poly.Contains(site.Ring, p)) continue;
                        if ((cell.m_State & CellFlags.Blocked) != 0) { blocked++; continue; }
                        if (cell.m_Zone.m_Index == 0) { empty++; continue; }
                        zoned++;
                        Bump(byRoad, road);
                        if (ownRoad) own++; else city++;
                    }
            }
            int plannedPreview = 0, plannedQuadCells = 0;
            if (plan != null)
            {
                foreach (var c in plan.ZoneCells) if (c.State == Placemaker.Core.Build.ZoneCellState.Zoned) plannedPreview++;
                foreach (var l in plan.Layers)
                    foreach (var z in l.Zoning)
                    {
                        float area = math.abs(Cross(z.B.xz - z.A.xz, z.D.xz - z.A.xz));
                        plannedQuadCells += (int)math.round(area / 64f);
                    }
            }
            return new JObject
            {
                ["plannedPreviewCells"] = plannedPreview, ["plannedQuadCells"] = plannedQuadCells,
                ["liveZonedCells"] = zoned, ["blockedCells"] = blocked, ["emptyCells"] = empty,
                ["zonedFromOwnRoads"] = own, ["zonedFromCityRoads"] = city, ["byRoad"] = JObject.FromObject(byRoad),
            };
        }

        private static float Cross(float2 a, float2 b) => a.x * b.y - a.y * b.x;

        private int SafeGroupOf(Entity e)
        {
            try { return Services.Groups?.GroupOf(e) ?? 0; } catch (Exception) { return 0; }
        }

        /// <summary>Growables in the site after the grow phase and the road each one faces.</summary>
        public JObject Growables(SiteSnapshot site, int groupId, HashSet<Entity> members)
        {
            var list = new JArray();
            int own = 0, city = 0, none = 0, construction = 0, preexisting = 0;
            foreach (var e in Query<global::Game.Buildings.Building>())
            {
                var t = _em.GetComponentData<global::Game.Objects.Transform>(e).m_Position.xz;
                if (!InBox(t, site.Min, site.Max) || !Poly.Contains(site.Ring, t)) continue;
                var pf = PrefabOf(e);
                if (pf == Entity.Null || !_em.HasComponent<SpawnableBuildingData>(pf)) continue;
                bool old = site.Buildings.Contains(e);
                if (old) preexisting++;
                var b = _em.GetComponentData<global::Game.Buildings.Building>(e);
                var road = b.m_RoadEdge;
                string faces;
                if (road == Entity.Null) { faces = "none"; none++; }
                else if (members.Contains(road) || (groupId > 0 && SafeGroupOf(road) == groupId)) { faces = "own"; if (!old) own++; }
                else { faces = "city"; if (!old) city++; }
                bool uc = _em.HasComponent<global::Game.Objects.UnderConstruction>(e);
                if (uc) construction++;
                if (list.Count < 40)
                    list.Add(new JObject { ["prefab"] = PrefabName(e), ["faces"] = faces, ["road"] = road == Entity.Null ? "" : PrefabName(road), ["underConstruction"] = uc, ["preexisting"] = old, ["x"] = math.round(t.x), ["z"] = math.round(t.y) });
            }
            float share = SuiteVerdicts.OwnFacingShare(own, city);
            return new JObject
            {
                ["new"] = own + city + none, ["facingOwnRoad"] = own, ["facingCityRoad"] = city, ["noRoad"] = none,
                ["underConstruction"] = construction, ["preexisting"] = preexisting,
                ["ownShare"] = float.IsNaN(share) ? null : (JToken)Math.Round(share, 2), ["list"] = list,
            };
        }

        /// <summary>Unplanned pits: ground below both the old ground and the plan's ground.</summary>
        public JObject Pits(SiteSnapshot site, List<float> after, ComposedPlan plan)
        {
            var planned = new List<float>(site.Samples.Count);
            Func<float2, float> heightAfter = null;
            if (plan != null) foreach (var l in plan.Layers) if (l.HeightAfter != null) { heightAfter = l.HeightAfter; break; }
            for (int i = 0; i < site.Samples.Count; i++)
            {
                float p = site.Heights[i];
                if (heightAfter != null) { try { p = heightAfter(site.Samples[i]); } catch (Exception) { } }
                else if (plan != null)
                    foreach (var l in plan.Layers)
                        foreach (var t in l.Terraform)
                        {
                            var ring = new float2[t.Ring.Length];
                            for (int k = 0; k < ring.Length; k++) ring[k] = t.Ring[k].xz;
                            if (ring.Length >= 3 && Poly.Contains(ring, site.Samples[i])) p = math.min(p, t.TargetHeight);
                        }
                planned.Add(p);
            }
            var roads = new List<Bezier4x3>();
            foreach (var e in Query<Edge>())
            {
                if (!_em.HasComponent<Curve>(e)) continue;
                var c = _em.GetComponentData<Curve>(e).m_Bezier;
                if (InBox(MathUtils.Position(c, 0.5f).xz, site.Min - 80f, site.Max + 80f)) roads.Add(c);
            }
            var near = new List<bool>(site.Samples.Count);
            foreach (var p in site.Samples)
            {
                bool n = false;
                foreach (var c in roads) if (MathUtils.Distance(c.xz, p, out _) <= 14f) { n = true; break; }
                near.Add(n);
            }
            var r = SuiteVerdicts.Pits(site.Samples, site.Heights, after, planned, near);
            float maxChange = 0f;
            for (int i = 0; i < after.Count && i < site.Heights.Count; i++) maxChange = math.max(maxChange, math.abs(after[i] - site.Heights[i]));
            return new JObject
            {
                ["samples"] = site.Samples.Count, ["pitCells"] = r.Cells, ["pitCellsNearRoads"] = r.CellsNearRoads,
                ["maxPitDepth"] = Math.Round(r.MaxDepth, 2), ["worstAt"] = r.Cells > 0 ? new JArray(Math.Round(r.WorstAt.x), Math.Round(r.WorstAt.y)) : null,
                ["maxUnplannedRise"] = Math.Round(r.MaxRise, 2), ["maxGroundChange"] = Math.Round(maxChange, 2), ["plannedFrom"] = heightAfter != null ? "HeightAfter" : "Terraform",
            };
        }

        /// <summary>
        /// After a delete: what stands in the site now that was not there before the build (top-level objects, areas and
        /// nets; road furniture owned by city streets and zoning growables outside the outline are not counted).
        /// </summary>
        public JObject LeftBehind(SiteSnapshot before)
        {
            var left = new Dictionary<string, int>(StringComparer.Ordinal);
            int n = 0;
            foreach (var e in Query<global::Game.Areas.Area>())
                if (!before.Areas.Contains(e) && !_em.HasComponent<Owner>(e) && AreaInBox(e, before.Min, before.Max)) { Bump(left, KindOf(e) + "|" + PrefabName(e)); n++; }
            foreach (var e in Query<global::Game.Objects.Object>())
            {
                if (before.Objects.Contains(e) || _em.HasComponent<Owner>(e) || !_em.HasComponent<global::Game.Objects.Transform>(e)) continue;
                if (_em.HasComponent<global::Game.Objects.Moving>(e) || !_em.HasComponent<global::Game.Objects.Static>(e)) continue;
                var p = _em.GetComponentData<global::Game.Objects.Transform>(e).m_Position.xz;
                if (InBox(p, before.Min, before.Max) && Poly.Contains(before.Ring, p)) { Bump(left, KindOf(e) + "|" + PrefabName(e)); n++; }
            }
            foreach (var e in Query<Edge>())
                if (!before.Edges.Contains(e) && !_em.HasComponent<Owner>(e) && EdgeInBox(e, before.Min, before.Max)) { Bump(left, "net|" + PrefabName(e)); n++; }
            return new JObject { ["count"] = n, ["byPrefab"] = TopDict(left, 20) };
        }

        /// <summary>Members the game hides (Overridden: an overlap verdict) — built but invisible to the player.</summary>
        public JObject HiddenMembers(IEnumerable<Entity> members)
        {
            var by = new Dictionary<string, int>(StringComparer.Ordinal);
            int n = 0;
            foreach (var m in members)
                if (Alive(m) && _em.HasComponent<Overridden>(m)) { n++; Bump(by, PrefabName(m)); }
            return new JObject { ["count"] = n, ["byPrefab"] = TopDict(by, 10) };
        }

        public HashSet<Entity> MemberSet(int groupId)
        {
            var list = new List<Entity>();
            if (groupId > 0 && Services.Groups != null) { try { Services.Groups.GetMembers(groupId, list); } catch (Exception) { } }
            return new HashSet<Entity>(list);
        }

        private static void Bump(Dictionary<string, int> d, string k) => d[k] = (d.TryGetValue(k, out int n) ? n : 0) + 1;

        private static JObject TopDict(Dictionary<string, int> d, int n)
        {
            var o = new JObject();
            foreach (var kv in d.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal).Take(n)) o[kv.Key] = kv.Value;
            return o;
        }
    }
}
