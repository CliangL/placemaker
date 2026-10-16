// In-game test suite: the live census of one scenario's site.
// Before the build it snapshots the site AND its neighbourhood (the whole block between the road centre lines plus a
// margin): every object at any owner depth, areas, nets, nodes, zone cells, ground heights, the buildings and city
// street furniture around. After the build and after growth it measures — independently of what Placemaker reports
// about itself — what a player would see: planned vs live vs VISIBLE per prefab (Overridden / Hidden objects are
// built but not seen), the ghost against the result, fence lanes, farmland, zone cells that changed, the road each
// growable faces, pits and mounds; after a delete, everything in the neighbourhood that differs from before the build.

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
using Placemaker.Game.Groups;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Placemaker.Game.Suite
{
    internal sealed class SiteSnapshot
    {
        public float2[] Ring;
        /// <summary>The outline's bounding box (+6 m).</summary>
        public float2 Min, Max;
        /// <summary>The neighbourhood: the block between its road centre lines + 10 m.</summary>
        public float2 NMin, NMax;
        public readonly HashSet<Entity> Buildings = new HashSet<Entity>();
        public readonly Dictionary<Entity, string> BuildingNames = new Dictionary<Entity, string>();
        public readonly Dictionary<Entity, float3> BuildingAt = new Dictionary<Entity, float3>();
        public readonly HashSet<Entity> Areas = new HashSet<Entity>();
        public readonly HashSet<Entity> Objects = new HashSet<Entity>();
        public readonly HashSet<Entity> Edges = new HashSet<Entity>();
        public readonly HashSet<Entity> FenceLanes = new HashSet<Entity>();
        /// <summary>Ground samples over ring + 20 m, 2 m apart, and their heights.</summary>
        public readonly List<float2> Samples = new List<float2>();
        public readonly List<float> Heights = new List<float>();
        public readonly List<bool> Inside = new List<bool>();
        /// <summary>The neighbourhood fingerprint: kind|prefab|x|z -> count.</summary>
        public Dictionary<string, int> Print, PrintNoGrowables;
        /// <summary>Zone cells in the neighbourhood: block|index -> zone (0 = none).</summary>
        public Dictionary<string, int> Cells;
        public Dictionary<string, float2> CellAt;
        public int GrowStamp;
    }

    internal sealed class SuiteProbe
    {
        private readonly World _world;
        private readonly EntityManager _em;
        private readonly PrefabSystem _prefabs;
        private readonly TerrainSystem _terrain;
        public int GrowStamp;

        public SuiteProbe(World world)
        {
            _world = world;
            _em = world.EntityManager;
            _prefabs = world.GetOrCreateSystemManaged<PrefabSystem>();
            _terrain = world.GetOrCreateSystemManaged<TerrainSystem>();
        }

        public EntityManager EM => _em;

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

        public string NameOfPrefab(Entity prefab)
        {
            try { return _prefabs.GetPrefab<PrefabBase>(prefab)?.name ?? ""; } catch (Exception) { return ""; }
        }

        /// <summary>Placemaker's clones carry the original's name in brackets ("Placemaker Overlay (Grass Surface 01)").</summary>
        public static string Normalize(string name)
        {
            if (string.IsNullOrEmpty(name)) return name ?? "";
            if (name.StartsWith("Placemaker ", StringComparison.Ordinal))
            {
                int a = name.IndexOf('('), b = name.LastIndexOf(')');
                if (a > 0 && b > a) return name.Substring(a + 1, b - a - 1);
            }
            return name;
        }

        private Entity PrefabOf(Entity e) => _em.HasComponent<PrefabRef>(e) ? _em.GetComponentData<PrefabRef>(e).m_Prefab : Entity.Null;

        private bool Alive(Entity e) => _em.Exists(e) && !_em.HasComponent<Deleted>(e) && !_em.HasComponent<Temp>(e);

        private static bool InBox(float2 p, float2 min, float2 max) => p.x >= min.x && p.x <= max.x && p.y >= min.y && p.y <= max.y;

        public int GroupOf(Entity e)
        {
            try
            {
                if (_em.Exists(e) && _em.HasComponent<PlacemakerMember>(e)) return _em.GetComponentData<PlacemakerMember>(e).GroupId;
                return Services.Groups?.GroupOf(e) ?? 0;
            }
            catch (Exception) { return 0; }
        }

        /// <summary>A net (edge or node) that is a city street: a road prefab no Placemaker group owns.</summary>
        public bool CityNet(Entity e)
        {
            if (!_em.Exists(e) || (!_em.HasComponent<Edge>(e) && !_em.HasComponent<global::Game.Net.Node>(e))) return false;
            var pf = PrefabOf(e);
            if (pf == Entity.Null) return false;
            bool road = _em.HasComponent<RoadData>(pf) || (_em.HasComponent<global::Game.Net.Node>(e) && HasRoadEdge(e));
            return road && GroupOf(e) == 0;
        }

        private bool HasRoadEdge(Entity node)
        {
            if (!_em.HasBuffer<ConnectedEdge>(node)) return false;
            var ce = _em.GetBuffer<ConnectedEdge>(node, true);
            for (int i = 0; i < ce.Length; i++)
            {
                var pf = PrefabOf(ce[i].m_Edge);
                if (pf != Entity.Null && _em.HasComponent<RoadData>(pf) && GroupOf(ce[i].m_Edge) == 0) return true;
            }
            return false;
        }

        public Entity TopOwner(Entity e)
        {
            for (int i = 0; i < 10 && _em.Exists(e) && _em.HasComponent<Owner>(e); i++)
            {
                var o = _em.GetComponentData<Owner>(e).m_Owner;
                if (o == Entity.Null || !_em.Exists(o)) break;
                e = o;
            }
            return e;
        }

        /// <summary>The member an entity belongs to (itself or an owner up the chain), Entity.Null when none.</summary>
        private Entity MemberOf(Entity e, HashSet<Entity> members, int groupId)
        {
            for (int i = 0; i < 10 && _em.Exists(e); i++)
            {
                if (members.Contains(e) || (groupId > 0 && _em.HasComponent<PlacemakerMember>(e) && _em.GetComponentData<PlacemakerMember>(e).GroupId == groupId)) return e;
                if (!_em.HasComponent<Owner>(e)) break;
                var o = _em.GetComponentData<Owner>(e).m_Owner;
                if (o == Entity.Null) break;
                e = o;
            }
            return Entity.Null;
        }

        private List<Entity> Query<T>() where T : struct
        {
            var r = new List<Entity>();
            using (var q = _em.CreateEntityQuery(ComponentType.ReadOnly<T>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>()))
            using (var a = q.ToEntityArray(Allocator.Temp))
                foreach (var e in a) r.Add(e);
            return r;
        }

        private bool AreaInBox(Entity e, float2 min, float2 max) => _em.HasBuffer<global::Game.Areas.Node>(e) && InBox(AreaCenter(e), min, max) && _em.GetBuffer<global::Game.Areas.Node>(e, true).Length > 0;

        private bool EdgeInBox(Entity e, float2 min, float2 max)
        {
            if (!_em.HasComponent<Curve>(e)) return false;
            var b = _em.GetComponentData<Curve>(e).m_Bezier;
            return InBox(MathUtils.Position(b, 0.5f).xz, min, max);
        }

        public float2 AreaCenter(Entity e)
        {
            if (!_em.HasBuffer<global::Game.Areas.Node>(e)) return float2.zero;
            var nodes = _em.GetBuffer<global::Game.Areas.Node>(e, true);
            float2 c = float2.zero;
            for (int i = 0; i < nodes.Length; i++) c += nodes[i].m_Position.xz;
            return nodes.Length > 0 ? c / nodes.Length : c;
        }

        private bool Mobile(Entity e) =>
            _em.HasComponent<global::Game.Objects.Moving>(e) || _em.HasComponent<global::Game.Creatures.Creature>(e) || _em.HasComponent<global::Game.Vehicles.Vehicle>(e);

        // ───────────── snapshot ─────────────

        public SiteSnapshot Snapshot(float2[] ring, SuiteBlock block, float margin = 6f)
        {
            var s = new SiteSnapshot { Ring = ring, GrowStamp = GrowStamp };
            float2 min = ring[0], max = ring[0];
            foreach (var p in ring) { min = math.min(min, p); max = math.max(max, p); }
            s.Min = min - margin;
            s.Max = max + margin;
            s.NMin = block != null ? new float2(block.X0, block.Z0) - 10f : s.Min - 20f;
            s.NMax = block != null ? new float2(block.X1, block.Z1) + 10f : s.Max + 20f;
            foreach (var e in Query<global::Game.Buildings.Building>())
            {
                var p = _em.GetComponentData<global::Game.Objects.Transform>(e).m_Position;
                if (InBox(p.xz, s.Min - 30f, s.Max + 30f)) { s.Buildings.Add(e); s.BuildingNames[e] = PrefabName(e); s.BuildingAt[e] = p; }
            }
            foreach (var e in Query<global::Game.Areas.Area>())
                if (AreaInBox(e, s.NMin, s.NMax)) s.Areas.Add(e);
            foreach (var e in Query<global::Game.Objects.Object>())
            {
                if (!_em.HasComponent<global::Game.Objects.Transform>(e)) continue;
                if (InBox(_em.GetComponentData<global::Game.Objects.Transform>(e).m_Position.xz, s.NMin, s.NMax)) s.Objects.Add(e);
            }
            foreach (var e in Query<Edge>())
                if (EdgeInBox(e, s.NMin, s.NMax)) s.Edges.Add(e);
            foreach (var e in Query<global::Game.Net.UtilityLane>())
            {
                if (!_em.HasComponent<Curve>(e)) continue;
                var c = _em.GetComponentData<Curve>(e).m_Bezier;
                var lo = math.min(c.a.xz, c.d.xz);
                var hi = math.max(c.a.xz, c.d.xz);
                if (!(hi.x < s.Min.x - 10f || lo.x > s.Max.x + 10f || hi.y < s.Min.y - 10f || lo.y > s.Max.y + 10f)) s.FenceLanes.Add(e);
            }
            s.Print = Fingerprint(s.NMin, s.NMax, false);
            s.PrintNoGrowables = Fingerprint(s.NMin, s.NMax, true);
            s.Cells = ZoneCells(s.NMin, s.NMax, out s.CellAt);
            SampleGround(s);
            return s;
        }

        private void SampleGround(SiteSnapshot s)
        {
            var h = _terrain.GetHeightData(true);
            const float step = 2f;
            var lo = s.Min - 20f;
            var hi = s.Max + 20f;
            for (float x = lo.x; x <= hi.x; x += step)
                for (float z = lo.y; z <= hi.y; z += step)
                {
                    var p = new float2(x, z);
                    s.Samples.Add(p);
                    s.Inside.Add(Poly.Contains(s.Ring, p));
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

        /// <summary>
        /// Everything that stands in a box, at any owner depth: objects (not vehicles or citizens), areas, nets and nodes,
        /// keyed by kind, prefab and position (1 m). Growables (and what they own) are left out when skipGrowables.
        /// </summary>
        public Dictionary<string, int> Fingerprint(float2 min, float2 max, bool skipGrowables)
        {
            var d = new Dictionary<string, int>(StringComparer.Ordinal);
            void Add(string kind, Entity e, float2 p)
            {
                string k = kind + "|" + PrefabName(e) + "|" + math.round(p.x) + "|" + math.round(p.y);
                d[k] = (d.TryGetValue(k, out int n) ? n : 0) + 1;
            }
            foreach (var e in Query<global::Game.Objects.Object>())
            {
                if (!_em.HasComponent<global::Game.Objects.Transform>(e) || Mobile(e)) continue;
                var p = _em.GetComponentData<global::Game.Objects.Transform>(e).m_Position.xz;
                if (!InBox(p, min, max)) continue;
                if (skipGrowables && IsGrowable(TopOwner(e))) continue;
                Add(_em.HasComponent<global::Game.Buildings.Building>(e) ? "building" : "object", e, p);
            }
            foreach (var e in Query<global::Game.Areas.Area>())
            {
                if (!_em.HasBuffer<global::Game.Areas.Node>(e)) continue;
                var c = AreaCenter(e);
                if (InBox(c, min, max) && !(skipGrowables && IsGrowable(TopOwner(e)))) Add(KindOf(e), e, c);
            }
            foreach (var e in Query<Edge>())
                if (EdgeInBox(e, min, max) && !(skipGrowables && IsGrowable(TopOwner(e)))) Add("net", e, MathUtils.Position(_em.GetComponentData<Curve>(e).m_Bezier, 0.5f).xz);
            foreach (var e in Query<global::Game.Net.Node>())
            {
                var p = _em.GetComponentData<global::Game.Net.Node>(e).m_Position.xz;
                if (InBox(p, min, max) && !(skipGrowables && IsGrowable(TopOwner(e)))) Add("node", e, p);
            }
            return d;
        }

        private bool IsGrowable(Entity top)
        {
            var pf = PrefabOf(top);
            return pf != Entity.Null && _em.HasComponent<global::Game.Buildings.Building>(top) && _em.HasComponent<SpawnableBuildingData>(pf);
        }

        /// <summary>Zone cells in a box: "blockEntity|index" -> zone index (0 = none), for visible cells only.</summary>
        public Dictionary<string, int> ZoneCells(float2 min, float2 max, out Dictionary<string, float2> at)
        {
            var d = new Dictionary<string, int>(StringComparer.Ordinal);
            at = new Dictionary<string, float2>(StringComparer.Ordinal);
            foreach (var e in Query<Block>())
            {
                if (!_em.HasBuffer<Cell>(e)) continue;
                var block = _em.GetComponentData<Block>(e);
                if (!InBox(block.m_Position.xz, min - 60f, max + 60f)) continue;
                var cells = _em.GetBuffer<Cell>(e, true);
                for (int y = 0; y < block.m_Size.y; y++)
                    for (int x = 0; x < block.m_Size.x; x++)
                    {
                        int i = y * block.m_Size.x + x;
                        if (i >= cells.Length) continue;
                        var cell = cells[i];
                        if ((cell.m_State & CellFlags.Visible) == 0) continue;
                        var p = ZoneUtils.GetCellPosition(block, new int2(x, y)).xz;
                        if (!InBox(p, min, max)) continue;
                        // keyed by position (a rebuilt block keeps its cells' places, not its entity)
                        string k = math.round(p.x * 2f) / 2f + "|" + math.round(p.y * 2f) / 2f;
                        int z = (cell.m_State & CellFlags.Blocked) != 0 ? -1 : cell.m_Zone.m_Index;
                        d[k] = z;
                        at[k] = p;
                    }
            }
            return d;
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

        /// <summary>A member nobody is meant to see: carriers, companions, owner/blocker lots, aisles, markers, mesh-less prefabs.</summary>
        public string InvisibleByDesign(Entity e)
        {
            if (_em.HasComponent<PlacemakerMember>(e))
            {
                var role = _em.GetComponentData<PlacemakerMember>(e).Role;
                switch (role)
                {
                    case MemberRole.Carrier: case MemberRole.Companion: case MemberRole.ParkCompanion: case MemberRole.ZoneBlocker:
                    case MemberRole.OwnerArea: case MemberRole.WaterSource: case MemberRole.Zoning: case MemberRole.ParkingAisle:
                        return "role " + role;
                }
            }
            var pf = PrefabOf(e);
            if (pf == Entity.Null) return "no prefab";
            if (_em.HasComponent<global::Game.Objects.Marker>(e)) return "marker";
            if (_em.HasComponent<SpawnLocationData>(pf)) return "spawn location";
            if (_em.HasComponent<UtilityObjectData>(pf)) return "utility (underground)";
            if (!_em.HasBuffer<SubMesh>(pf) || _em.GetBuffer<SubMesh>(pf, true).Length == 0) return "no mesh";
            return null;
        }

        // ───────────── visibility: built is not the same as seen ─────────────

        /// <summary>
        /// Whether the game would draw an object from the current camera: inside the view and big enough on the screen
        /// (at least 4 pixels) — the condition under which a missing draw is a defect, not distance culling.
        /// </summary>
        public bool ExpectedDrawn(Entity e, UnityEngine.Plane[] planes, UnityEngine.Camera cam)
        {
            if (cam == null || planes == null || !_em.HasComponent<global::Game.Rendering.CullingInfo>(e)) return false;
            var ci = _em.GetComponentData<global::Game.Rendering.CullingInfo>(e);
            var b = ci.m_Bounds;
            var center = (b.min + b.max) * 0.5f;
            var size = b.max - b.min;
            if (!math.all(math.isfinite(size)) || math.lengthsq(size) < 1e-6f) return false;
            if (!UnityEngine.GeometryUtility.TestPlanesAABB(planes, new UnityEngine.Bounds(center, size))) return false;
            float dist = math.max(1f, math.distance((float3)cam.transform.position, center));
            float px = math.cmax(size) / (2f * dist * math.tan(math.radians(cam.fieldOfView) * 0.5f)) * UnityEngine.Screen.height;
            return px >= 4f;
        }

        /// <summary>Not hidden by the game or a tool (camera-independent).</summary>
        private bool NotHidden(Entity e) => !_em.HasComponent<Overridden>(e) && !_em.HasComponent<Hidden>(e) && !_em.HasComponent<global::Game.Objects.Unspawned>(e);

        private bool Drawn(Entity e) =>
            _em.HasComponent<global::Game.Rendering.CullingInfo>(e) && _em.GetComponentData<global::Game.Rendering.CullingInfo>(e).m_CullingIndex != 0
            && _em.HasBuffer<global::Game.Rendering.MeshBatch>(e) && _em.GetBuffer<global::Game.Rendering.MeshBatch>(e, true).Length > 0;

        /// <summary>
        /// Member objects a player would see, measured at the current camera (the runner frames the build the same
        /// standard way every time). Camera-independent: alive, not Overridden (the overlap verdict of the game), not
        /// Hidden, not Unspawned. Camera-dependent, but only where it must hold: every object that is inside the view
        /// and big enough on the screen is registered for drawing (a culling index and mesh batches). Objects nobody is
        /// meant to see (carriers, markers, mesh-less, underground) are listed apart.
        /// </summary>
        public JObject Visibility(IEnumerable<Entity> members)
        {
            var cam = UnityEngine.Camera.main;
            var planes = cam != null ? UnityEngine.GeometryUtility.CalculateFrustumPlanes(cam) : null;
            int objects = 0, visible = 0, overridden = 0, hidden = 0, deleted = 0, notRendered = 0, unspawned = 0, byDesign = 0, expected = 0, outOfView = 0;
            var rows = new Dictionary<string, int[]>(StringComparer.Ordinal);   // prefab -> [objects, ok, overridden, hidden, notRendered, expected]
            var design = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var m in members)
            {
                if (!_em.Exists(m) || !_em.HasComponent<global::Game.Objects.Object>(m)) continue;
                string why = InvisibleByDesign(m);
                if (why != null) { byDesign++; Bump(design, PrefabName(m) + " (" + why + ")"); continue; }
                objects++;
                string name = Normalize(PrefabName(m));
                if (!rows.TryGetValue(name, out var row)) rows[name] = row = new int[6];
                row[0]++;
                if (_em.HasComponent<Deleted>(m)) { deleted++; continue; }
                if (_em.HasComponent<Overridden>(m)) { overridden++; row[2]++; continue; }
                if (_em.HasComponent<Hidden>(m)) { hidden++; row[3]++; continue; }
                if (_em.HasComponent<global::Game.Objects.Unspawned>(m)) { unspawned++; row[3]++; continue; }
                bool exp = ExpectedDrawn(m, planes, cam);
                if (exp) { expected++; row[5]++; }
                if (exp && !Drawn(m)) { notRendered++; row[4]++; continue; }
                if (!exp) outOfView++;
                visible++;
                row[1]++;
            }
            var notSeen = new JObject();
            foreach (var kv in rows.Where(kv => kv.Value[1] < kv.Value[0]).OrderByDescending(kv => kv.Value[0] - kv.Value[1]).Take(15))
                notSeen[kv.Key] = $"{kv.Value[1]}/{kv.Value[0]} seen (overridden {kv.Value[2]}, hidden {kv.Value[3]}, not drawn although in view {kv.Value[4]})";
            return new JObject
            {
                ["objects"] = objects, ["visible"] = visible, ["overridden"] = overridden, ["hidden"] = hidden, ["unspawned"] = unspawned, ["deleted"] = deleted,
                ["notRendered"] = notRendered, ["expectedInView"] = expected, ["tooSmallOrOutOfView"] = outOfView, ["notSeen"] = notSeen,
                ["invisibleByDesign"] = byDesign, ["invisibleByDesignPrefabs"] = TopDict(design, 10),
                ["camera"] = cam != null ? $"{cam.transform.position} fov {cam.fieldOfView:F1}" : "none",
            };
        }

        /// <summary>Drawn objects per normalized prefab at the current camera: the members and everything they own (stall parts, sub-objects); street furniture left out.</summary>
        public Dictionary<string, int> SeenByPrefab(HashSet<Entity> members, int groupId, float2 min, float2 max)
        {
            var cam = UnityEngine.Camera.main;
            var planes = cam != null ? UnityEngine.GeometryUtility.CalculateFrustumPlanes(cam) : null;
            var d = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var e in Query<global::Game.Objects.Object>())
            {
                if (!_em.HasComponent<global::Game.Objects.Transform>(e) || Mobile(e)) continue;
                if (!InBox(_em.GetComponentData<global::Game.Objects.Transform>(e).m_Position.xz, min, max)) continue;
                if (MemberOf(e, members, groupId) == Entity.Null || StreetFurniture(e)) continue;
                if (InvisibleByDesign(e) != null || _em.HasComponent<Overridden>(e) || _em.HasComponent<Hidden>(e) || _em.HasComponent<global::Game.Objects.Unspawned>(e)) continue;
                if (!ExpectedDrawn(e, planes, cam) || !Drawn(e)) continue;
                Bump(d, Normalize(PrefabName(e)));
            }
            return d;
        }

        /// <summary>An object owned (at any depth) by a net of a city street kind (arrows, markings, street lights of a street).</summary>
        private bool StreetFurniture(Entity e)
        {
            var top = TopOwner(e);
            if (top == e || (!_em.HasComponent<Edge>(top) && !_em.HasComponent<global::Game.Net.Node>(top))) return false;
            string n = PrefabName(top);
            return n == SuiteLayout.SmallRoad || n == SuiteLayout.MediumRoad || n == SuiteLayout.PedestrianStreet;
        }

        /// <summary>
        /// The ghost a person looks at in Review: temporary objects (not hidden, not marked for deletion, not copies of
        /// existing city things, no street furniture) drawn at the current camera, per normalized prefab.
        /// </summary>
        public Dictionary<string, int> GhostByPrefab(float2 min, float2 max, out int total)
        {
            var cam = UnityEngine.Camera.main;
            var planes = cam != null ? UnityEngine.GeometryUtility.CalculateFrustumPlanes(cam) : null;
            var d = new Dictionary<string, int>(StringComparer.Ordinal);
            total = 0;
            using (var q = _em.CreateEntityQuery(ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<global::Game.Objects.Object>(), ComponentType.ReadOnly<global::Game.Objects.Transform>(), ComponentType.Exclude<Deleted>()))
            using (var all = q.ToEntityArray(Allocator.Temp))
            {
                foreach (var e in all)
                {
                    var t = _em.GetComponentData<Temp>(e);
                    if ((t.m_Flags & (TempFlags.Delete | TempFlags.Hidden | TempFlags.Cancel)) != 0) continue;
                    if (_em.HasComponent<Hidden>(e) || Mobile(e)) continue;
                    if (!InBox(_em.GetComponentData<global::Game.Objects.Transform>(e).m_Position.xz, min, max)) continue;
                    var pf = PrefabOf(e);
                    if (pf == Entity.Null || _em.HasComponent<global::Game.Objects.Marker>(e) || _em.HasComponent<SpawnLocationData>(pf) || _em.HasComponent<UtilityObjectData>(pf) || !_em.HasBuffer<SubMesh>(pf) || _em.GetBuffer<SubMesh>(pf, true).Length == 0) continue;
                    if (ReplacesExisting(e) || StreetFurniture(e)) continue;   // a temporary copy of a city street (split for a junction) and its furniture: not the build
                    if (!ExpectedDrawn(e, planes, cam) || !Drawn(e)) continue;
                    total++;
                    Bump(d, Normalize(PrefabName(e)));
                }
            }
            return d;
        }

        /// <summary>A temporary that stands for something that already exists (it or an owner up the chain has an original).</summary>
        private bool ReplacesExisting(Entity e)
        {
            for (int i = 0; i < 10 && _em.Exists(e); i++)
            {
                if (_em.HasComponent<Temp>(e) && _em.GetComponentData<Temp>(e).m_Original != Entity.Null) return true;
                if (!_em.HasComponent<Owner>(e)) break;
                var o = _em.GetComponentData<Owner>(e).m_Owner;
                if (o == Entity.Null) break;
                e = o;
            }
            return false;
        }

        // ───────────── after the build ─────────────

        public JObject Census(SiteSnapshot before, int groupId, ComposedPlan plan, string layer, Placemaker.Core.Build.BuildAudit audit, SuiteLanes lanes, IList<(float3 At, string Kind)> markers)
        {
            var o = new JObject();
            var members = MemberSet(groupId);
            o["groupId"] = groupId;
            o["members"] = members.Count;

            // planned per kind/prefab (normalized)
            var planned = new Dictionary<string, int>(StringComparer.Ordinal);
            int plannedStalls = 0;
            var stallPrefabs = new HashSet<string>(StringComparer.Ordinal);
            if (plan != null)
                foreach (var l in plan.Layers)
                {
                    foreach (var n in l.Nets) Bump(planned, "net|" + Normalize(n.Asset.Id ?? ""));
                    foreach (var ob in l.Objects)
                    {
                        Bump(planned, "object|" + Normalize(ob.Asset.Id ?? ""));
                        if (ob.Role == ObjectRole.ParkingBay) { plannedStalls++; if (!string.IsNullOrEmpty(ob.Asset.Id)) stallPrefabs.Add(Normalize(ob.Asset.Id)); }
                    }
                    foreach (var a in l.Areas) Bump(planned, "area|" + Normalize(a.Asset.Id ?? ""));
                    foreach (var sf in l.Surfaces) Bump(planned, "surface|" + Normalize(sf.Asset.Id ?? ""));
                }

            // live members per kind/prefab: alive, and seen (objects)
            var live = new Dictionary<string, int>(StringComparer.Ordinal);
            var seen = new Dictionary<string, int>(StringComparer.Ordinal);
            var liveByKind = new Dictionary<string, int>(StringComparer.Ordinal);
            int liveStalls = 0, seenStalls = 0;
            var stalls = new List<Entity>();
            var memberEdges = new List<Entity>();
            foreach (var m in members)
            {
                if (!Alive(m)) continue;
                var kind = KindOf(m);
                var name = Normalize(PrefabName(m));
                string key = PlanKind(kind) + "|" + name;
                Bump(live, key);
                Bump(liveByKind, kind);
                bool isObj = kind == "object" || kind == "building";
                if (!isObj || NotHidden(m)) Bump(seen, key);
                if (kind == "net") memberEdges.Add(m);
                if (kind == "object" && stallPrefabs.Contains(name))
                {
                    liveStalls++;
                    if (NotHidden(m)) seenStalls++;
                    stalls.Add(m);
                }
            }
            var kinds = new JArray();
            foreach (var key in planned.Keys.Union(live.Keys).OrderBy(k => k, StringComparer.Ordinal))
            {
                var parts = key.Split('|');
                planned.TryGetValue(key, out int pn);
                live.TryGetValue(key, out int ln);
                seen.TryGetValue(key, out int sn);
                kinds.Add(new JObject { ["kind"] = parts[0], ["prefab"] = parts.Length > 1 ? parts[1] : "", ["planned"] = pn, ["live"] = ln, ["seen"] = sn });
            }
            o["kinds"] = kinds;
            o["liveByKind"] = JObject.FromObject(liveByKind);

            // independent plan vs live match per prefab (objects within 0.5 m, nets by total length, areas/surfaces by count)
            o["match"] = PlanMatch(plan, members, groupId);

            if (audit != null)
            {
                var rows = new JArray();
                foreach (var r in audit.Rows())
                    rows.Add(new JObject { ["reason"] = r.Reason, ["count"] = r.Count, ["byKind"] = JObject.FromObject(r.ByKind), ["topPrefabs"] = JObject.FromObject(r.ByPrefab.OrderByDescending(kv => kv.Value).Take(5).ToDictionary(kv => kv.Key, kv => kv.Value)) });
                o["audit"] = new JObject { ["planned"] = audit.Planned, ["built"] = audit.Built, ["leftOut"] = audit.LeftOut, ["kept"] = audit.Kept, ["reasons"] = rows };
            }

            // what appeared in the site that is not a member (vanilla lots and their sub-areas, sub-objects…)
            var foreign = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var e in Query<global::Game.Areas.Area>())
                if (!members.Contains(e) && !before.Areas.Contains(e) && AreaInBox(e, before.Min, before.Max)) Bump(foreign, KindOf(e) + "|" + PrefabName(e));
            foreach (var e in Query<global::Game.Objects.Object>())
            {
                if (members.Contains(e) || before.Objects.Contains(e) || !_em.HasComponent<global::Game.Objects.Transform>(e) || Mobile(e)) continue;
                if (InBox(_em.GetComponentData<global::Game.Objects.Transform>(e).m_Position.xz, before.Min, before.Max)) Bump(foreign, KindOf(e) + "|" + PrefabName(e));
            }
            o["notMembers"] = TopDict(foreign, 30);

            // farmland: extractor areas (crop fields, forestry) by component, not by name; names stay as a hint
            o["farmland"] = Farmland(before.Min - 10f, before.Max + 10f);
            o["farmType"] = SuiteVerdicts.IsFarmType(layer);

            // fences: fence lanes (by type), visible length minus cut spans, by owner
            if (lanes != null)
            {
                var fences = lanes.Fences(before.Min - 10f, before.Max + 10f, members, groupId);
                float memberLen = 0f, foreignLen = 0f, preLen = 0f;
                var by = new Dictionary<string, int>(StringComparer.Ordinal);
                foreach (var f in fences)
                {
                    if (before.FenceLanes.Contains(f.E)) { preLen += f.Visible; continue; }
                    if (f.Who == SuiteLanes.Who.Member) memberLen += f.Visible; else foreignLen += f.Visible;
                    if (f.Visible > 0f) Bump(by, (f.Who == SuiteLanes.Who.Member ? "member: " : "foreign: ") + f.Name + " (" + PrefabName(f.Top) + ")");
                }
                bool planFences = planned.Keys.Any(k => k.IndexOf("fence", StringComparison.OrdinalIgnoreCase) >= 0 || k.IndexOf("hedge", StringComparison.OrdinalIgnoreCase) >= 0);
                o["fences"] = new JObject
                {
                    ["lanes"] = fences.Count, ["memberVisibleM"] = Math.Round(memberLen, 1), ["foreignVisibleM"] = Math.Round(foreignLen, 1),
                    ["preexistingM"] = Math.Round(preLen, 1), ["planHasFences"] = planFences, ["byOwner"] = TopDict(by, 12),
                };
            }

            // parking: stalls, their own parking lanes, reachable by car from the city
            var parking = new JObject { ["plannedStalls"] = plannedStalls, ["liveStalls"] = liveStalls, ["seenStalls"] = seenStalls, ["stallPrefabs"] = new JArray(stallPrefabs) };
            if (lanes != null && stalls.Count > 0)
            {
                var reach = lanes.Reach(i => lanes.Lanes[i].Who == SuiteLanes.Who.City && (lanes.Lanes[i].Mode & SuiteLanes.Mode.Car) != 0, true, out _);
                int withLane = 0, reachable = 0;
                foreach (var st in stalls)
                {
                    if (!_em.HasBuffer<global::Game.Net.SubLane>(st)) continue;
                    var sl = _em.GetBuffer<global::Game.Net.SubLane>(st, true);
                    bool has = false, ok = false;
                    for (int i = 0; i < sl.Length; i++)
                    {
                        var ln = sl[i].m_SubLane;
                        if (!_em.HasComponent<global::Game.Net.ParkingLane>(ln)) continue;
                        has = true;
                        if (lanes.Index.TryGetValue(ln, out int idx) && reach[idx]) ok = true;
                    }
                    if (has) withLane++;
                    if (ok) reachable++;
                }
                parking["stallsWithParkingLane"] = withLane;
                if (reachable < stalls.Count && stalls.Count > 0) parking["explainFirstStall"] = lanes.Explain(stalls[0], PrefabName, 3);
                parking["stallsReachableByCar"] = reachable;
            }
            o["parking"] = parking;

            // nets of the build reachable from the city through lanes (side connections included)
            if (lanes != null) o["nets"] = NetsConnected(memberEdges, lanes);

            // zone cells: what changed in the neighbourhood, against the plan's zoned cells
            o["zoning"] = Zoning(before, plan);

            // where the build is entered from the city (car, then pedestrian), whether or not it has entrance markers
            if (lanes != null) o["connections"] = Connections(lanes);

            // entrances, physically
            if (lanes != null && markers != null && markers.Count > 0) o["entrances"] = Entrances(markers, lanes, members, groupId);

            // existing buildings in the site and 30 m around: still standing, seen, unmoved
            o["existingBuildings"] = Existing(before);

            // notification icons on the build's members (data, since the shots hide them)
            o["icons"] = Icons(members, groupId);
            return o;
        }

        private JObject PlanMatch(ComposedPlan plan, HashSet<Entity> members, int groupId)
        {
            var o = new JObject();
            if (plan == null) { o["error"] = "no plan"; return o; }
            var liveObjs = new Dictionary<string, List<float3>>(StringComparer.Ordinal);
            var liveNetLen = new Dictionary<string, float>(StringComparer.Ordinal);
            var liveAreas = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var m in members)
            {
                if (!Alive(m)) continue;
                string name = Normalize(PrefabName(m));
                if (_em.HasComponent<Edge>(m) && _em.HasComponent<Curve>(m)) liveNetLen[name] = (liveNetLen.TryGetValue(name, out var l) ? l : 0f) + MathUtils.Length(_em.GetComponentData<Curve>(m).m_Bezier);
                else if (_em.HasComponent<global::Game.Areas.Area>(m)) Bump(liveAreas, name);
                else if (_em.HasComponent<global::Game.Objects.Transform>(m))
                {
                    if (!liveObjs.TryGetValue(name, out var list)) liveObjs[name] = list = new List<float3>();
                    list.Add(_em.GetComponentData<global::Game.Objects.Transform>(m).m_Position);
                }
            }
            var missing = new Dictionary<string, int>(StringComparer.Ordinal);
            var extra = new Dictionary<string, int>(StringComparer.Ordinal);
            int plannedObjects = 0, matchedObjects = 0;
            var plannedNetLen = new Dictionary<string, float>(StringComparer.Ordinal);
            var plannedAreas = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var l in plan.Layers)
            {
                foreach (var ob in l.Objects)
                {
                    string name = Normalize(ob.Asset.Id ?? "");
                    plannedObjects++;
                    if (liveObjs.TryGetValue(name, out var list))
                    {
                        int best = -1; float bd = 0.75f;
                        for (int i = 0; i < list.Count; i++) { float d = math.distance(list[i].xz, ob.Position.xz); if (d < bd) { bd = d; best = i; } }
                        if (best >= 0) { list.RemoveAt(best); matchedObjects++; continue; }
                    }
                    Bump(missing, "object|" + name);
                }
                foreach (var n in l.Nets)
                {
                    string name = Normalize(n.Asset.Id ?? "");
                    var bz = new Bezier4x3(n.A, n.B, n.C, n.D);
                    plannedNetLen[name] = (plannedNetLen.TryGetValue(name, out var pl) ? pl : 0f) + MathUtils.Length(bz);
                }
                foreach (var a in l.Areas) Bump(plannedAreas, Normalize(a.Asset.Id ?? ""));
                foreach (var s in l.Surfaces) Bump(plannedAreas, Normalize(s.Asset.Id ?? ""));
            }
            foreach (var kv in liveObjs) if (kv.Value.Count > 0) extra["object|" + kv.Key] = kv.Value.Count;
            var nets = new JObject();
            float netMissing = 0f;
            foreach (var kv in plannedNetLen)
            {
                liveNetLen.TryGetValue(kv.Key, out float got);
                nets[kv.Key] = $"{Math.Round(got)} of {Math.Round(kv.Value)} m";
                if (got < kv.Value * 0.95f - 2f) { netMissing += kv.Value - got; missing["net|" + kv.Key] = (int)Math.Round(kv.Value - got); }
            }
            foreach (var kv in plannedAreas)
            {
                liveAreas.TryGetValue(kv.Key, out int got);
                if (got < kv.Value) missing["area|" + kv.Key] = kv.Value - got;
            }
            o["plannedObjects"] = plannedObjects;
            o["matchedObjects"] = matchedObjects;
            o["missingObjects"] = plannedObjects - matchedObjects;
            o["netMissingM"] = Math.Round(netMissing, 1);
            o["missing"] = TopDict(missing, 20);
            o["extra"] = TopDict(extra, 12);
            o["netLength"] = nets;
            return o;
        }

        private JObject Farmland(float2 min, float2 max)
        {
            int n = 0;
            var names = new Dictionary<string, int>(StringComparer.Ordinal);
            var hints = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var e in Query<global::Game.Areas.Area>())
            {
                if (!AreaInBox(e, min, max)) continue;
                var pf = PrefabOf(e);
                bool extractor = _em.HasComponent<global::Game.Areas.Extractor>(e) || (pf != Entity.Null && _em.HasComponent<ExtractorAreaData>(pf));
                string name = PrefabName(e);
                if (extractor) { n++; Bump(names, name); }
                else if (SuiteVerdicts.IsCropLike(name)) Bump(hints, name);
            }
            return new JObject { ["extractorAreas"] = n, ["prefabs"] = TopDict(names, 10), ["nameHints"] = TopDict(hints, 10) };
        }

        private JObject NetsConnected(List<Entity> memberEdges, SuiteLanes lanes)
        {
            var o = new JObject { ["edges"] = memberEdges.Count };
            if (memberEdges.Count == 0) return o;
            var conn = lanes.Connected(i => lanes.Lanes[i].Who == SuiteLanes.Who.City);
            int isolated = 0, noLanes = 0;
            var names = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var e in memberEdges)
            {
                bool any = false, ok = false;
                if (_em.HasBuffer<global::Game.Net.SubLane>(e))
                {
                    var sl = _em.GetBuffer<global::Game.Net.SubLane>(e, true);
                    for (int i = 0; i < sl.Length && !ok; i++)
                        if (lanes.Index.TryGetValue(sl[i].m_SubLane, out int idx)) { any = true; ok = conn[idx]; }
                }
                if (!any) { noLanes++; continue; }
                if (!ok) { isolated++; Bump(names, PrefabName(e)); }
            }
            o["withLanes"] = memberEdges.Count - noLanes;
            o["isolatedEdges"] = isolated;
            o["isolatedPrefabs"] = TopDict(names, 8);
            if (isolated > 0) { var first = memberEdges.FirstOrDefault(e => { if (!_em.HasBuffer<global::Game.Net.SubLane>(e)) return false; var sl = _em.GetBuffer<global::Game.Net.SubLane>(e, true); for (int i = 0; i < sl.Length; i++) if (lanes.Index.TryGetValue(sl[i].m_SubLane, out int x) && conn[x]) return false; return sl.Length > 0; }); if (first != Entity.Null) o["explainFirstIsolated"] = lanes.Explain(first, PrefabName, 4); }
            return o;
        }

        /// <summary>
        /// Each entrance, physically: from the city street's lanes a car (pedestrian for path entrances) reaches the
        /// build's lanes, entering within 25 m of the marker, and no uncut fence lies across the first 12 m of that
        /// member lane.
        /// </summary>
        private JArray Entrances(IList<(float3 At, string Kind)> markers, SuiteLanes lanes, HashSet<Entity> members, int groupId)
        {
            var a = new JArray();
            var fences = lanes.Fences(new float2(markers.Min(m => m.At.x), markers.Min(m => m.At.z)) - 40f, new float2(markers.Max(m => m.At.x), markers.Max(m => m.At.z)) + 40f, members, groupId);
            var barrierObjects = BarrierObjects(markers);
            foreach (var (at, kind) in markers)
            {
                bool ped = IsPedestrianKind(kind);
                var o = new JObject { ["at"] = new JArray(Math.Round(at.x, 1), Math.Round(at.z, 1)), ["kind"] = kind ?? "", ["mode"] = ped ? "pedestrian" : "car" };
                var reach = lanes.Reach(i => lanes.Lanes[i].Who == SuiteLanes.Who.City && (ped ? (lanes.Lanes[i].Mode & SuiteLanes.Mode.Pedestrian) != 0 : (lanes.Lanes[i].Mode & SuiteLanes.Mode.Car) != 0), !ped, out var prev);
                int memberLanes = 0, reached = 0;
                for (int i = 0; i < lanes.Lanes.Count; i++)
                    if (lanes.Lanes[i].Who == SuiteLanes.Who.Member && (ped ? lanes.Lanes[i].PedOk : lanes.Lanes[i].CarOk)) { memberLanes++; if (reach[i]) reached++; }
                o["memberLanes"] = memberLanes;
                o["memberLanesReached"] = reached;
                var entries = lanes.Entries(reach, prev);
                var near = entries.Select(x => (x.Lane, x.At, D: math.distance(x.At.xz, at.xz))).Where(x => x.D <= 25f).OrderBy(x => x.D).ToList();
                o["entries"] = entries.Count;
                if (reached == 0) { o["usable"] = false; o["detail"] = memberLanes == 0 ? $"the build has no {(ped ? "path" : "drive")} lanes" : $"no {(ped ? "path" : "drive")} lane of the build is reachable from a city street"; a.Add(o); continue; }
                if (near.Count == 0)
                {
                    o["usable"] = false;
                    var nearest = entries.OrderBy(x => math.distance(x.At.xz, at.xz)).FirstOrDefault();
                    o["detail"] = $"reachable, but not here: the nearest entry is {(entries.Count == 0 ? "none" : Math.Round(math.distance(nearest.At.xz, at.xz)) + " m away")}";
                    a.Add(o);
                    continue;
                }
                var e = near[0];
                var lane = lanes.Lanes[e.Lane];
                MathUtils.Distance(lane.Curve.xz, e.At.xz, out float t0);
                var path = SuiteLanes.Along(lane.Curve, t0, 12f);
                o["entry"] = new JArray(Math.Round(e.At.x, 1), Math.Round(e.At.z, 1));
                o["entryFromMarkerM"] = Math.Round(e.D, 1);
                o["entryLane"] = PrefabName(lane.Owner) + " / " + PrefabName(lane.E);
                var f = SuiteLanes.Across(fences, path, ped ? 1.0f : 1.6f);
                string objBarrier = null;
                foreach (var b in barrierObjects) foreach (var p in path) if (math.distance(b.At, p) < (ped ? 1.0f : 1.6f)) { objBarrier = b.Name; break; }
                if (f.HasValue) { o["usable"] = false; o["barrier"] = f.Value.F.Name; o["barrierM"] = Math.Round(f.Value.D, 2); o["barrierOwner"] = PrefabName(f.Value.F.Top); o["detail"] = $"uncut fence '{f.Value.F.Name}' ({PrefabName(f.Value.F.Top)}) {Math.Round(f.Value.D, 1)} m across the {(ped ? "path" : "drive")}"; }
                else if (objBarrier != null) { o["usable"] = false; o["barrier"] = objBarrier; o["detail"] = $"'{objBarrier}' stands across the {(ped ? "path" : "drive")}"; }
                else { o["usable"] = true; o["detail"] = $"{(ped ? "walkable" : "drivable")} from the street, entry {Math.Round(e.D)} m from the marker, open"; }
                a.Add(o);
            }
            return a;
        }

        private JArray Connections(SuiteLanes lanes)
        {
            var a = new JArray();
            var seen = new List<float3>();
            foreach (bool ped in new[] { false, true })
            {
                var reach = lanes.Reach(i => lanes.Lanes[i].Who == SuiteLanes.Who.City && (ped ? (lanes.Lanes[i].Mode & SuiteLanes.Mode.Pedestrian) != 0 : (lanes.Lanes[i].Mode & SuiteLanes.Mode.Car) != 0), !ped, out var prev);
                foreach (var e in lanes.Entries(reach, prev))
                {
                    if (seen.Any(p => math.distance(p.xz, e.At.xz) < 10f)) continue;
                    seen.Add(e.At);
                    if (a.Count >= 12) break;
                    a.Add(new JObject { ["at"] = new JArray(Math.Round(e.At.x, 1), Math.Round(e.At.z, 1)), ["mode"] = ped ? "pedestrian" : "car", ["lane"] = PrefabName(lanes.Lanes[e.Lane].Owner) + " / " + PrefabName(lanes.Lanes[e.Lane].E) });
                }
            }
            return a;
        }

        private static bool IsPedestrianKind(string kind)
        {
            if (string.IsNullOrEmpty(kind)) return false;
            var k = kind.ToLowerInvariant();
            return k.Contains("ped") || k.Contains("foot") || k.Contains("path") || k.Contains("walk") || k.Contains("bike");
        }

        private List<(string Name, float2 At)> BarrierObjects(IList<(float3 At, string Kind)> markers)
        {
            var r = new List<(string, float2)>();
            var min = new float2(markers.Min(m => m.At.x), markers.Min(m => m.At.z)) - 30f;
            var max = new float2(markers.Max(m => m.At.x), markers.Max(m => m.At.z)) + 30f;
            foreach (var e in Query<global::Game.Objects.Object>())
            {
                if (!_em.HasComponent<global::Game.Objects.Transform>(e) || _em.HasComponent<Overridden>(e) || _em.HasComponent<Hidden>(e)) continue;
                var p = _em.GetComponentData<global::Game.Objects.Transform>(e).m_Position.xz;
                if (!InBox(p, min, max)) continue;
                string n = PrefabName(e);
                if (SuiteVerdicts.IsBarrierObject(n)) r.Add((n, p));
            }
            return r;
        }

        private JObject Zoning(SiteSnapshot site, ComposedPlan plan)
        {
            var now = ZoneCells(site.NMin, site.NMax, out var at);
            int newlyZoned = 0, unzoned = 0, unchangedZoned = 0;
            var newCells = new List<float2>();
            foreach (var kv in now)
            {
                site.Cells.TryGetValue(kv.Key, out int was);
                bool zonedNow = kv.Value > 0, zonedBefore = was > 0;
                if (zonedNow && !zonedBefore) { newlyZoned++; newCells.Add(at[kv.Key]); }
                else if (!zonedNow && zonedBefore) unzoned++;
                else if (zonedNow) unchangedZoned++;
            }
            var planned = new List<float2>();
            if (plan != null)
                foreach (var c in plan.ZoneCells)
                    if (c.State == Placemaker.Core.Build.ZoneCellState.Zoned) planned.Add((c.A + c.B + c.C + c.D) * 0.25f);
            int matched = 0;
            foreach (var p in planned) if (newCells.Any(c => math.distance(c, p) <= 8.5f)) matched++;
            int spill = newCells.Count(c => !planned.Any(p => math.distance(c, p) <= 8.5f));
            int spillOutside = newCells.Count(c => !planned.Any(p => math.distance(c, p) <= 8.5f) && !Poly.Contains(site.Ring, c));
            return new JObject
            {
                ["plannedZonedCells"] = planned.Count, ["newlyZonedCells"] = newlyZoned, ["matchedPlanned"] = matched,
                ["unplannedZoned"] = spill, ["unplannedZonedOutsideOutline"] = spillOutside, ["unzonedCells"] = unzoned, ["zonedBeforeAndNow"] = unchangedZoned,
            };
        }

        private JObject Existing(SiteSnapshot before)
        {
            int lost = 0, hidden = 0, moved = 0;
            var names = new JArray();
            foreach (var b in before.Buildings)
            {
                string n = before.BuildingNames.TryGetValue(b, out var x) ? x : "?";
                if (!Alive(b)) { lost++; if (names.Count < 10) names.Add("lost " + n); continue; }
                if (_em.HasComponent<Overridden>(b) || _em.HasComponent<Hidden>(b)) { hidden++; if (names.Count < 10) names.Add("hidden " + n); continue; }
                var p = _em.GetComponentData<global::Game.Objects.Transform>(b).m_Position;
                if (before.BuildingAt.TryGetValue(b, out var p0) && math.distance(p, p0) > 0.5f) { moved++; if (names.Count < 10) names.Add("moved " + n); }
            }
            return new JObject { ["before"] = before.Buildings.Count, ["lost"] = lost, ["hidden"] = hidden, ["moved"] = moved, ["detail"] = names };
        }

        private JObject Icons(HashSet<Entity> members, int groupId)
        {
            var d = new Dictionary<string, int>(StringComparer.Ordinal);
            int n = 0;
            using (var q = _em.CreateEntityQuery(ComponentType.ReadOnly<global::Game.Notifications.Icon>(), ComponentType.ReadOnly<Owner>(), ComponentType.Exclude<Deleted>()))
            using (var all = q.ToEntityArray(Allocator.Temp))
                foreach (var e in all)
                {
                    var owner = _em.GetComponentData<Owner>(e).m_Owner;
                    if (MemberOf(owner, members, groupId) == Entity.Null) continue;
                    n++;
                    Bump(d, PrefabName(e));
                }
            return new JObject { ["count"] = n, ["byPrefab"] = TopDict(d, 10) };
        }

        /// <summary>Growables in the site's neighbourhood that are new since the snapshot, the road each faces and whether that road is the build's.</summary>
        public JObject Growables(SiteSnapshot site, int groupId, HashSet<Entity> members, ComposedPlan plan)
        {
            var list = new JArray();
            int own = 0, city = 0, none = 0, construction = 0, onPlanned = 0, onPlannedOwn = 0, onPlannedCity = 0;
            var planned = new List<float2>();
            if (plan != null) foreach (var c in plan.ZoneCells) if (c.State == Placemaker.Core.Build.ZoneCellState.Zoned) planned.Add((c.A + c.B + c.C + c.D) * 0.25f);
            foreach (var e in Query<global::Game.Buildings.Building>())
            {
                var t = _em.GetComponentData<global::Game.Objects.Transform>(e).m_Position.xz;
                if (!InBox(t, site.Min, site.Max)) continue;
                var pf = PrefabOf(e);
                if (pf == Entity.Null || !_em.HasComponent<SpawnableBuildingData>(pf) || site.Buildings.Contains(e)) continue;
                var b = _em.GetComponentData<global::Game.Buildings.Building>(e);
                var road = b.m_RoadEdge;
                string faces;
                if (road == Entity.Null) { faces = "none"; none++; }
                else if (members.Contains(road) || (groupId > 0 && GroupOf(road) == groupId)) { faces = "own"; own++; }
                else { faces = "city"; city++; }
                bool onPlan = planned.Any(p => math.distance(p, t) <= 16f);
                if (onPlan) { onPlanned++; if (faces == "own") onPlannedOwn++; else if (faces == "city") onPlannedCity++; }
                bool uc = _em.HasComponent<global::Game.Objects.UnderConstruction>(e);
                if (uc) construction++;
                var zonePf = _em.GetComponentData<SpawnableBuildingData>(pf).m_ZonePrefab;
                if (list.Count < 40)
                    list.Add(new JObject
                    {
                        ["prefab"] = PrefabName(e), ["zone"] = NameOfPrefab(zonePf), ["faces"] = faces, ["road"] = road == Entity.Null ? "" : PrefabName(road),
                        ["onPlannedCells"] = onPlan, ["underConstruction"] = uc, ["x"] = math.round(t.x), ["z"] = math.round(t.y),
                    });
            }
            return new JObject
            {
                ["new"] = own + city + none, ["facingOwnRoad"] = own, ["facingCityRoad"] = city, ["noRoad"] = none, ["underConstruction"] = construction,
                ["onPlannedCells"] = onPlanned, ["onPlannedFacingOwn"] = onPlannedOwn, ["onPlannedFacingCity"] = onPlannedCity, ["plannedZonedCells"] = planned.Count, ["list"] = list,
            };
        }

        /// <summary>Pits and mounds over the outline + 20 m: ground below / above everything the plan or the old ground allows.</summary>
        public JObject Pits(SiteSnapshot site, List<float> after, ComposedPlan plan)
        {
            var heightFns = new List<Func<float2, float>>();
            var terra = new List<(float2[] Ring, float Target, bool Permanent)>();
            if (plan != null)
                foreach (var l in plan.Layers)
                {
                    if (l.HeightAfter != null) heightFns.Add(l.HeightAfter);
                    foreach (var t in l.Terraform)
                    {
                        var ring = new float2[t.Ring.Length];
                        for (int k = 0; k < ring.Length; k++) ring[k] = t.Ring[k].xz;
                        if (ring.Length >= 3) terra.Add((ring, t.TargetHeight, t.Permanent));
                    }
                }
            var plannedLo = new List<float>(site.Samples.Count);
            var plannedHi = new List<float>(site.Samples.Count);
            for (int i = 0; i < site.Samples.Count; i++)
            {
                float b = site.Heights[i], lo = b, hi = b;
                if (site.Inside[i])
                {
                    foreach (var f in heightFns) { try { float v = f(site.Samples[i]); if (math.isfinite(v)) { lo = math.min(lo, v); hi = math.max(hi, v); } } catch (Exception) { } }
                    foreach (var t in terra) if (Poly.Contains(t.Ring, site.Samples[i])) { lo = math.min(lo, t.Target); hi = math.max(hi, t.Target); }
                }
                plannedLo.Add(lo);
                plannedHi.Add(hi);
            }
            var roads = new List<Bezier4x3>();
            foreach (var e in Query<Edge>())
            {
                if (!_em.HasComponent<Curve>(e)) continue;
                var c = _em.GetComponentData<Curve>(e).m_Bezier;
                if (InBox(MathUtils.Position(c, 0.5f).xz, site.Min - 100f, site.Max + 100f)) roads.Add(c);
            }
            // near a road: only asked for samples that are pits (the distance test is the expensive part)
            var near = new List<bool>(site.Samples.Count);
            for (int i = 0; i < site.Samples.Count; i++)
            {
                bool n = false;
                if (i < after.Count && math.isfinite(after[i]) && plannedLo[i] - after[i] > 1.5f)
                    foreach (var c in roads) if (MathUtils.Distance(c.xz, site.Samples[i], out _) <= 14f) { n = true; break; }
                near.Add(n);
            }
            var r = SuiteVerdicts.PitsAndMounds(site.Samples, after, plannedLo, plannedHi, near, site.Inside);
            return new JObject
            {
                ["samples"] = site.Samples.Count, ["grid"] = "2 m over outline + 20 m", ["planned"] = heightFns.Count > 0 ? "HeightAfter of " + heightFns.Count + " layer(s)" : terra.Count > 0 ? "terraform rings" : "old ground",
                ["pitCells"] = r.Pits, ["pitCellsNearRoads"] = r.PitsNearRoads, ["pitCellsOutsideOutline"] = r.PitsOutside, ["maxPitDepth"] = Math.Round(r.MaxDepth, 2),
                ["worstPitAt"] = r.Pits > 0 ? new JArray(Math.Round(r.WorstPit.x), Math.Round(r.WorstPit.y)) : null,
                ["moundCells"] = r.Mounds, ["maxMoundHeight"] = Math.Round(r.MaxRise, 2), ["worstMoundAt"] = r.Mounds > 0 ? new JArray(Math.Round(r.WorstMound.x), Math.Round(r.WorstMound.y)) : null,
                ["permanentTerraform"] = terra.Any(t => t.Permanent),
            };
        }

        /// <summary>
        /// After a delete: the neighbourhood against its fingerprint from before the build — what was added (left behind)
        /// and what was removed (taken from the city), plus zone cells that changed. Growables are skipped when the
        /// simulation ran in between.
        /// </summary>
        public JObject LeftBehind(SiteSnapshot before)
        {
            bool grew = before.GrowStamp != GrowStamp;
            var now = Fingerprint(before.NMin, before.NMax, grew);
            var was = grew ? before.PrintNoGrowables : before.Print;
            var buildLeft = new Dictionary<string, int>(StringComparer.Ordinal);
            var cityRebuilt = new Dictionary<string, int>(StringComparer.Ordinal);
            var missing = new Dictionary<string, int>(StringComparer.Ordinal);
            var at = new JArray();
            int nBuild = 0, nCity = 0, nMissing = 0;
            var extraKeys = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var kv in now)
            {
                was.TryGetValue(kv.Key, out int w);
                if (kv.Value > w) extraKeys[kv.Key] = kv.Value - w;
            }
            // who owns each extra piece: a city street (rebuilt after a split) or nobody / the build
            var cityPrefabs = new HashSet<string>(before.Print.Keys.Select(k => k.Split('|')).Where(p => p[0] == "net" || p[0] == "node").Select(p => p[1]), StringComparer.Ordinal);
            var owners = OwnersOf(extraKeys.Keys, before.NMin, before.NMax, cityPrefabs);
            foreach (var kv in extraKeys)
            {
                var parts = kv.Key.Split('|');
                bool city = owners.TryGetValue(kv.Key, out var c0) && c0;
                if (city) { nCity += kv.Value; Bump(cityRebuilt, parts[0] + "|" + parts[1], kv.Value); }
                else
                {
                    nBuild += kv.Value;
                    Bump(buildLeft, parts[0] + "|" + parts[1], kv.Value);
                    if (at.Count < 12) at.Add(new JArray(int.Parse(parts[2]), int.Parse(parts[3])));
                }
            }
            foreach (var kv in was)
            {
                now.TryGetValue(kv.Key, out int n);
                if (kv.Value > n) { nMissing += kv.Value - n; var parts = kv.Key.Split('|'); Bump(missing, parts[0] + "|" + parts[1], kv.Value - n); }
            }
            // zone cells: zoned cells per zone type before and after (zone blocks are rebuilt with a street: positions shift)
            var cells = ZoneCells(before.NMin, before.NMax, out _);
            var zb = before.Cells.Values.Where(v => v > 0).GroupBy(v => v).ToDictionary(g => g.Key, g => g.Count());
            var za = cells.Values.Where(v => v > 0).GroupBy(v => v).ToDictionary(g => g.Key, g => g.Count());
            int cellsChanged = 0;
            foreach (var k in zb.Keys.Union(za.Keys)) { zb.TryGetValue(k, out int x); za.TryGetValue(k, out int y); cellsChanged += Math.Abs(x - y); }
            return new JObject
            {
                ["count"] = nBuild, ["cityRebuilt"] = nCity, ["removedFromCity"] = nMissing, ["zoneCellsChanged"] = cellsChanged,
                ["zonedCellsBefore"] = zb.Values.Sum(), ["zonedCellsAfter"] = za.Values.Sum(),
                ["byPrefab"] = TopDict(buildLeft, 20), ["cityRebuiltByPrefab"] = TopDict(cityRebuilt, 12), ["removedByPrefab"] = TopDict(missing, 12),
                ["at"] = at, ["box"] = $"block + 10 m ({Math.Round(before.NMin.x)},{Math.Round(before.NMin.y)})..({Math.Round(before.NMax.x)},{Math.Round(before.NMax.y)})", ["growablesSkipped"] = grew,
            };
        }

        /// <summary>For fingerprint keys: whether the piece standing there now belongs to a city street (its top owner is a city net).</summary>
        private Dictionary<string, bool> OwnersOf(IEnumerable<string> keys, float2 min, float2 max, HashSet<string> cityPrefabs)
        {
            var want = new HashSet<string>(keys, StringComparer.Ordinal);
            var r = new Dictionary<string, bool>(StringComparer.Ordinal);
            if (want.Count == 0) return r;
            void Look(string kind, Entity e, float2 p)
            {
                string k = kind + "|" + PrefabName(e) + "|" + math.round(p.x) + "|" + math.round(p.y);
                if (!want.Contains(k) || r.ContainsKey(k)) return;
                // a city street's piece: its net is a city street of a kind that stood here before the build
                var top = TopOwner(e);
                var net = CityNet(top) ? top : CityNet(e) ? e : Entity.Null;
                r[k] = net != Entity.Null && cityPrefabs.Contains(PrefabName(net));
            }
            foreach (var e in Query<global::Game.Objects.Object>())
            {
                if (!_em.HasComponent<global::Game.Objects.Transform>(e) || Mobile(e)) continue;
                var p = _em.GetComponentData<global::Game.Objects.Transform>(e).m_Position.xz;
                if (InBox(p, min, max)) Look(_em.HasComponent<global::Game.Buildings.Building>(e) ? "building" : "object", e, p);
            }
            foreach (var e in Query<Edge>()) if (EdgeInBox(e, min, max)) Look("net", e, MathUtils.Position(_em.GetComponentData<Curve>(e).m_Bezier, 0.5f).xz);
            foreach (var e in Query<global::Game.Net.Node>())
            {
                var p = _em.GetComponentData<global::Game.Net.Node>(e).m_Position.xz;
                if (InBox(p, min, max)) Look("node", e, p);
            }
            return r;
        }

        public string PrintKey(Dictionary<string, int> d)
        {
            unchecked
            {
                long h = d.Count;
                foreach (var kv in d) h = h * 31 + kv.Key.GetHashCode() * 7 + kv.Value;
                return d.Count + ":" + h;
            }
        }

        /// <summary>Ground left changed by a delete (before any restore): samples off the old ground by more than 0.25 m.</summary>
        public JObject GroundChanged(SiteSnapshot before)
        {
            var now = GroundNow(before);
            int changed = 0;
            float worst = 0f;
            float2 worstAt = default;
            for (int i = 0; i < now.Count && i < before.Heights.Count; i++)
            {
                float d = math.abs(now[i] - before.Heights[i]);
                if (d > 0.25f) changed++;
                if (d > worst) { worst = d; worstAt = before.Samples[i]; }
            }
            return new JObject { ["samples"] = now.Count, ["changed"] = changed, ["worstM"] = Math.Round(worst, 2), ["worstAt"] = new JArray(Math.Round(worstAt.x), Math.Round(worstAt.y)) };
        }

        /// <summary>
        /// Pieces in the neighbourhood that are new since the snapshot but belong to no member of the group (not even
        /// through their owners) and are not city street parts or growables: things a build left untracked (or a
        /// rebuild left over from the build it replaced).
        /// </summary>
        public JObject Orphans(SiteSnapshot before, int groupId)
        {
            var members = MemberSet(groupId);
            var d = new Dictionary<string, int>(StringComparer.Ordinal);
            var at = new JArray();
            int n = 0;
            void Add(Entity e, string kind, float2 p)
            {
                n++;
                Bump(d, kind + "|" + PrefabName(e));
                if (at.Count < 10) at.Add(new JArray(Math.Round(p.x), Math.Round(p.y)));
            }
            bool Skip(Entity e)
            {
                if (MemberOf(e, members, groupId) != Entity.Null) return true;
                var top = TopOwner(e);
                return CityNet(top) || IsGrowable(top) || before.Buildings.Contains(top) || before.Objects.Contains(top) || before.Areas.Contains(top) || before.Edges.Contains(top);
            }
            foreach (var e in Query<global::Game.Objects.Object>())
            {
                if (before.Objects.Contains(e) || !_em.HasComponent<global::Game.Objects.Transform>(e) || Mobile(e)) continue;
                var p = _em.GetComponentData<global::Game.Objects.Transform>(e).m_Position.xz;
                if (InBox(p, before.NMin, before.NMax) && !Skip(e)) Add(e, KindOf(e), p);
            }
            foreach (var e in Query<global::Game.Areas.Area>())
                if (!before.Areas.Contains(e) && AreaInBox(e, before.NMin, before.NMax) && !Skip(e)) Add(e, KindOf(e), AreaCenter(e));
            foreach (var e in Query<Edge>())
                if (!before.Edges.Contains(e) && EdgeInBox(e, before.NMin, before.NMax) && !Skip(e)) Add(e, "net", MathUtils.Position(_em.GetComponentData<Curve>(e).m_Bezier, 0.5f).xz);
            return new JObject { ["count"] = n, ["byPrefab"] = TopDict(d, 15), ["at"] = at };
        }

        public HashSet<Entity> MemberSet(int groupId)
        {
            var list = new List<Entity>();
            if (groupId > 0 && Services.Groups != null) { try { Services.Groups.GetMembers(groupId, list); } catch (Exception) { } }
            return new HashSet<Entity>(list);
        }

        private static void Bump(Dictionary<string, int> d, string k, int by = 1) => d[k] = (d.TryGetValue(k, out int n) ? n : 0) + by;

        public static JObject TopDict(Dictionary<string, int> d, int n)
        {
            var o = new JObject();
            foreach (var kv in d.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal).Take(n)) o[kv.Key] = kv.Value;
            return o;
        }
    }
}
