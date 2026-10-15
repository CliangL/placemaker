// In-game test suite: builds the fixture world fresh every run (docs/teardown/10-ingame-suite.md §5, revised): the game's
// flat default world, one heightmap replacement (slope rows, rough blocks), a grid of vanilla roads, residential zoning
// on the zoned row's south street, two plopped park buildings in the "survive" block. Roads, zoning and buildings go in
// as ordinary vanilla definitions through SuiteFixtureToolSystem — nothing of Placemaker's own build code is used, so the
// fixture does not depend on the code under test.

using System;
using System.Collections;
using System.Collections.Generic;
using Colossal.Mathematics;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.Simulation;
using Game.Tools;
using Game.Zones;
using Placemaker.Core.Suite;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Placemaker.Game.Suite
{
    internal sealed class SuiteFixture
    {
        public readonly SuiteLayout Layout = new SuiteLayout();
        public List<SuiteBlock> Blocks;
        public readonly Dictionary<string, Entity> Prefabs = new Dictionary<string, Entity>(StringComparer.Ordinal);
        public readonly Dictionary<string, float> HalfWidths = new Dictionary<string, float>(StringComparer.Ordinal);
        public readonly List<Entity> PlantedBuildings = new List<Entity>();
        public readonly List<string> Log = new List<string>();

        private readonly World _world;
        private readonly EntityManager _em;
        private PrefabSystem _prefabSystem;

        public SuiteFixture(World world)
        {
            _world = world;
            _em = world.EntityManager;
            Blocks = Layout.Blocks();
        }

        private void Note(string s)
        {
            Log.Add(s);
            Mod.Log.Info("Placemaker suite fixture: " + s);
        }

        public float HalfWidth(string road) => HalfWidths.TryGetValue(road ?? "", out var w) ? w : 8f;

        // ───────────── prefabs ─────────────

        public void IndexPrefabs()
        {
            _prefabSystem = _world.GetOrCreateSystemManaged<PrefabSystem>();
            using (var q = _em.CreateEntityQuery(ComponentType.ReadOnly<PrefabData>()))
            using (var all = q.ToEntityArray(Allocator.Temp))
            {
                foreach (var e in all)
                {
                    string name;
                    try { name = _prefabSystem.GetPrefab<PrefabBase>(e)?.name; } catch (Exception) { continue; }
                    if (string.IsNullOrEmpty(name) || Prefabs.ContainsKey(name)) continue;
                    Prefabs[name] = e;
                }
            }
            foreach (var road in new[] { SuiteLayout.SmallRoad, SuiteLayout.MediumRoad, SuiteLayout.PedestrianStreet })
            {
                if (Prefabs.TryGetValue(road, out var p) && _em.HasComponent<NetGeometryData>(p))
                {
                    var g = _em.GetComponentData<NetGeometryData>(p);
                    HalfWidths[road] = g.m_DefaultWidth * 0.5f;
                    Note($"road '{road}' width {g.m_DefaultWidth:F1} m, edge length {g.m_EdgeLengthRange.min:F0}-{g.m_EdgeLengthRange.max:F0} m");
                }
                else Note($"road '{road}' NOT FOUND");
            }
            Note($"indexed {Prefabs.Count} prefabs");
        }

        public string NameOf(Entity prefab)
        {
            try { return _prefabSystem.GetPrefab<PrefabBase>(prefab)?.name ?? ""; } catch (Exception) { return ""; }
        }

        private Entity Road(string name)
        {
            if (Prefabs.TryGetValue(name, out var e) && _em.HasComponent<NetGeometryData>(e)) return e;
            return Prefabs.TryGetValue(SuiteLayout.SmallRoad, out var s) ? s : Entity.Null;
        }

        // ───────────── terrain ─────────────

        /// <summary>
        /// Takes the live map's terrain encoding and playable area, and sets the fixture's Base to the median ground of the
        /// fixture area (least earth moved), at least 8 m above the sea.
        /// </summary>
        public void ConfigureFromWorld()
        {
            var terrain = _world.GetOrCreateSystemManaged<TerrainSystem>();
            Layout.HeightScaleM = terrain.heightScaleOffset.x;
            Layout.HeightOffset = terrain.heightScaleOffset.y;
            Layout.MapOffset = terrain.playableOffset;
            Layout.MapExtent = terrain.playableArea;
            var heights = new List<float>();
            var lo = Layout.FixtureMin;
            var hi = Layout.FixtureMax;
            for (float x = lo.x; x <= hi.x; x += 20f)
                for (float z = lo.y; z <= hi.y; z += 20f)
                    heights.Add(SampleHeight(new float2(x, z)));
            heights.Sort();
            float median = SuiteLayout.Median(heights);
            float sea = _world.GetOrCreateSystemManaged<WaterSystem>().SeaLevel;
            Layout.Base = math.max(median, sea + 8f);
            Note($"map terrain: scale {Layout.HeightScaleM:F0} m, offset {Layout.HeightOffset:F1} m, playable {Layout.MapExtent.x:F0} m at {Layout.MapOffset}; "
                + $"fixture ground {heights[0]:F1}..{heights[heights.Count - 1]:F1} m, median {median:F1} m, sea {sea:F1} m -> base {Layout.Base:F1} m");
        }

        /// <summary>Removes every water source (the flattened fixture must never flood); the water itself is reset after the heightmap.</summary>
        public int RemoveWaterSources()
        {
            using (var q = _em.CreateEntityQuery(ComponentType.ReadOnly<global::Game.Simulation.WaterSourceData>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>()))
            {
                int n = q.CalculateEntityCount();
                if (n > 0) _em.AddComponent<Deleted>(q);
                Note($"water sources removed: {n}");
                return n;
            }
        }

        public void ResetWater()
        {
            var water = _world.GetOrCreateSystemManaged<WaterSystem>();
            water.Reset();
            Note("water reset (no water left on the map)");
        }

        /// <summary>
        /// Bulldozes what the map has in the fixture area (top-level nets and static objects: trees, props, buildings) through
        /// vanilla delete definitions, the way the bulldozer does.
        /// </summary>
        public int EmitClear(SuiteFixtureToolSystem tool, out string detail)
        {
            var lo = Layout.FixtureMin - 20f;
            var hi = Layout.FixtureMax + 20f;
            bool Inside(float3 p) => p.x >= lo.x && p.x <= hi.x && p.z >= lo.y && p.z <= hi.y;
            int edges = 0, objects = 0;
            using (var q = _em.CreateEntityQuery(ComponentType.ReadOnly<Edge>(), ComponentType.ReadOnly<Curve>(), ComponentType.Exclude<Owner>(),
                ComponentType.Exclude<Temp>(), ComponentType.Exclude<Deleted>()))
            using (var all = q.ToEntityArray(Allocator.Temp))
            {
                foreach (var e in all)
                {
                    var c = _em.GetComponentData<Curve>(e).m_Bezier;
                    bool hit = false;
                    for (int k = 0; k <= 8 && !hit; k++) hit = Inside(MathUtils.Position(c, k / 8f));
                    if (!hit) continue;
                    var edge = _em.GetComponentData<Edge>(e);
                    var def = tool.NewDefinition();
                    _em.AddComponentData(def, new CreationDefinition { m_Original = e, m_Flags = CreationFlags.Delete });
                    _em.AddComponent<Updated>(def);
                    _em.AddComponentData(def, new NetCourse
                    {
                        m_Curve = c, m_Length = MathUtils.Length(c), m_FixedIndex = -1,
                        m_StartPosition = new CoursePos { m_Entity = edge.m_Start, m_Position = c.a, m_Rotation = NetUtils.GetNodeRotation(MathUtils.StartTangent(c)), m_CourseDelta = 0f, m_ParentMesh = -1 },
                        m_EndPosition = new CoursePos { m_Entity = edge.m_End, m_Position = c.d, m_Rotation = NetUtils.GetNodeRotation(MathUtils.EndTangent(c)), m_CourseDelta = 1f, m_ParentMesh = -1 },
                    });
                    edges++;
                }
            }
            using (var q = _em.CreateEntityQuery(ComponentType.ReadOnly<global::Game.Objects.Static>(), ComponentType.ReadOnly<global::Game.Objects.Transform>(),
                ComponentType.Exclude<Owner>(), ComponentType.Exclude<Temp>(), ComponentType.Exclude<Deleted>()))
            using (var all = q.ToEntityArray(Allocator.Temp))
            {
                foreach (var e in all)
                {
                    var t = _em.GetComponentData<global::Game.Objects.Transform>(e);
                    if (!Inside(t.m_Position)) continue;
                    var def = tool.NewDefinition();
                    _em.AddComponentData(def, new CreationDefinition { m_Original = e, m_Flags = CreationFlags.Delete });
                    _em.AddComponent<Updated>(def);
                    _em.AddComponentData(def, new ObjectDefinition { m_Position = t.m_Position, m_Rotation = t.m_Rotation, m_LocalPosition = t.m_Position, m_LocalRotation = t.m_Rotation, m_ParentMesh = -1, m_Scale = new float3(1f), m_Intensity = 1f, m_PrefabSubIndex = -1 });
                    objects++;
                }
            }
            detail = $"{edges} edges, {objects} static objects";
            Note("clear fixture area: " + detail);
            return edges + objects;
        }

        public int CountInFixture()
        {
            var lo = Layout.FixtureMin;
            var hi = Layout.FixtureMax;
            int n = 0;
            using (var q = _em.CreateEntityQuery(ComponentType.ReadOnly<global::Game.Objects.Static>(), ComponentType.ReadOnly<global::Game.Objects.Transform>(),
                ComponentType.Exclude<Owner>(), ComponentType.Exclude<Temp>(), ComponentType.Exclude<Deleted>()))
            using (var all = q.ToEntityArray(Allocator.Temp))
                foreach (var e in all)
                {
                    var p = _em.GetComponentData<global::Game.Objects.Transform>(e).m_Position;
                    if (p.x >= lo.x && p.x <= hi.x && p.z >= lo.y && p.z <= hi.y) n++;
                }
            return n;
        }

        public string ApplyHeightmap()
        {
            var terrain = _world.GetOrCreateSystemManaged<TerrainSystem>();
            ushort[] existing = null;
            var hd = terrain.GetHeightData(true);
            int n = SuiteLayout.HeightmapSize;
            if (hd.isCreated && hd.heights.Length == n * n) existing = hd.heights.ToArray();
            else Note($"live heights unavailable ({(hd.isCreated ? hd.heights.Length : 0)} texels): the whole map becomes the layout");
            var data = Layout.Heightmap(existing);
            var tex = new Texture2D(n, n, UnityEngine.Experimental.Rendering.GraphicsFormat.R16_UNorm,
                UnityEngine.Experimental.Rendering.TextureCreationFlags.None);
            try
            {
                tex.SetPixelData(data, 0);
                tex.Apply(false, false);
                terrain.ReplaceHeightmap(tex);
            }
            finally { UnityEngine.Object.Destroy(tex); }
            return null;
        }

        /// <summary>Live ground height (equals the layout once the heightmap is in; the default world otherwise).</summary>
        public float H(float x, float z) => SampleHeight(new float2(x, z));

        public float SampleHeight(float2 p)
        {
            var terrain = _world.GetOrCreateSystemManaged<TerrainSystem>();
            var h = terrain.GetHeightData(true);
            return TerrainUtils.SampleHeight(ref h, new float3(p.x, 0f, p.y));
        }

        /// <summary>Compares sampled terrain with the layout at a few probe points. Returns the worst error (m).</summary>
        public float CheckTerrain(out string detail)
        {
            var probes = new[]
            {
                new float2(Layout.LineX(5) + 40f, Layout.LineZ(2) + 50f),                    // flat
                new float2(Layout.LineX(3) + 75f, Layout.LineZ(6) + 70f),                    // slope
                new float2(Layout.LineX(3) + 75f, Layout.LineZ(7)),                          // slope top road
                new float2(Layout.LineX(1) + 75f, Layout.LineZ(4) + 70f),                    // rough
                new float2(Layout.LineX(0) + 60f, Layout.LineZ(4) + 52f),                    // rough
            };
            float worst = 0f;
            var sb = new System.Text.StringBuilder();
            foreach (var p in probes)
            {
                float want = Layout.Height(p.x, p.y), got = SampleHeight(p);
                worst = math.max(worst, math.abs(want - got));
                sb.Append($"({p.x:F0},{p.y:F0}) want {want:F2} got {got:F2}; ");
            }
            detail = sb.ToString();
            return worst;
        }

        // ───────────── roads ─────────────

        /// <summary>
        /// Emits the road segments the filter picks. Ends that land on an existing node attach to it (CoursePos.m_Entity, as the
        /// vanilla net tool snaps), so the grid goes in as batches: vertical lines first, then the cross streets onto their nodes.
        /// </summary>
        public int EmitRoads(SuiteFixtureToolSystem tool, Func<SuiteRoadSegment, bool> filter)
        {
            int courses = 0;
            var random = new Unity.Mathematics.Random(0x5EED5u);
            var nodes = ExistingNodes();
            foreach (var seg in Layout.Roads())
            {
                if (filter != null && !filter(seg)) continue;
                var prefab = Road(seg.Prefab);
                if (prefab == Entity.Null) continue;
                var g = _em.GetComponentData<NetGeometryData>(prefab);
                float len = math.distance(seg.A, seg.B);
                float max = g.m_EdgeLengthRange.max > 1f ? g.m_EdgeLengthRange.max : 200f;
                int pieces = math.max(1, (int)math.ceil(len / (max * 0.95f)));
                for (int k = 0; k < pieces; k++)
                {
                    float2 a2 = math.lerp(seg.A, seg.B, k / (float)pieces), b2 = math.lerp(seg.A, seg.B, (k + 1) / (float)pieces);
                    if (k == 0) a2 = seg.A;
                    if (k == pieces - 1) b2 = seg.B;
                    var a = new float3(a2.x, H(a2.x, a2.y), a2.y);
                    var b = new float3(b2.x, H(b2.x, b2.y), b2.y);
                    var curve = NetUtils.StraightCurve(a, b);
                    var start = new CoursePos
                    {
                        m_Entity = Entity.Null, m_Position = a, m_Rotation = NetUtils.GetNodeRotation(MathUtils.StartTangent(curve)),
                        m_CourseDelta = 0f, m_Elevation = float2.zero, m_Flags = CoursePosFlags.IsFirst | CoursePosFlags.IsLeft | CoursePosFlags.IsRight,
                        m_ParentMesh = -1, m_SplitPosition = 0f,
                    };
                    var end = new CoursePos
                    {
                        m_Entity = Entity.Null, m_Position = b, m_Rotation = NetUtils.GetNodeRotation(MathUtils.EndTangent(curve)),
                        m_CourseDelta = 1f, m_Elevation = float2.zero, m_Flags = CoursePosFlags.IsLast | CoursePosFlags.IsLeft | CoursePosFlags.IsRight,
                        m_ParentMesh = -1, m_SplitPosition = 0f,
                    };
                    Attach(ref start, nodes);
                    Attach(ref end, nodes);
                    var def = tool.NewDefinition();
                    _em.AddComponentData(def, new CreationDefinition { m_Prefab = prefab, m_RandomSeed = random.NextInt() });
                    _em.AddComponent<Updated>(def);
                    _em.AddComponentData(def, new NetCourse
                    {
                        m_Curve = curve, m_Length = MathUtils.Length(curve), m_FixedIndex = -1, m_Elevation = float2.zero,
                        m_StartPosition = start, m_EndPosition = end,
                    });
                    courses++;
                }
            }
            return courses;
        }

        private static long Key(float2 p) => ((long)math.round(p.x * 4f) << 32) ^ (uint)(int)math.round(p.y * 4f);

        private Dictionary<long, Entity> ExistingNodes()
        {
            var r = new Dictionary<long, Entity>();
            using (var q = _em.CreateEntityQuery(ComponentType.ReadOnly<global::Game.Net.Node>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>()))
            using (var all = q.ToEntityArray(Allocator.Temp))
                foreach (var e in all) r[Key(_em.GetComponentData<global::Game.Net.Node>(e).m_Position.xz)] = e;
            return r;
        }

        private void Attach(ref CoursePos pos, Dictionary<long, Entity> nodes)
        {
            if (!nodes.TryGetValue(Key(pos.m_Position.xz), out var node)) return;
            pos.m_Entity = node;
            pos.m_Position = _em.GetComponentData<global::Game.Net.Node>(node).m_Position;
        }

        public int CountEdges(out int temps)
        {
            temps = 0;
            using (var q = _em.CreateEntityQuery(ComponentType.ReadOnly<Edge>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>()))
            using (var t = _em.CreateEntityQuery(ComponentType.ReadOnly<Edge>(), ComponentType.ReadOnly<Temp>()))
            {
                temps = t.CalculateEntityCount();
                return q.CalculateEntityCount();
            }
        }

        // ───────────── zoning ─────────────

        public const string ZonePrefab = "EU Residential Low";
        public const float ZoneDepth = 17f;

        /// <summary>Residential zoning along the south street of every Zoned block (2 cells deep).</summary>
        public int EmitZoning(SuiteFixtureToolSystem tool)
        {
            if (!Prefabs.TryGetValue(ZonePrefab, out var zone) && !Prefabs.TryGetValue("NA Residential Low", out zone)) { Note("zone prefab not found"); return 0; }
            int n = 0;
            foreach (var b in Blocks)
            {
                if (b.Kind != SuiteBlockKind.Zoned) continue;
                float z0 = b.Z0 + HalfWidth(b.South) + 0.5f, z1 = z0 + ZoneDepth;
                float x0 = b.X0 + HalfWidth(b.West) + 0.5f, x1 = b.X1 - HalfWidth(b.East) - 0.5f;
                float3 P(float x, float z) => new float3(x, H(x, z), z);
                var def = tool.NewDefinition();
                _em.AddComponentData(def, new CreationDefinition { m_Prefab = zone });
                _em.AddComponentData(def, new Zoning { m_Position = new Quad3(P(x0, z0), P(x1, z0), P(x1, z1), P(x0, z1)), m_Flags = ZoningFlags.Zone | ZoningFlags.Marquee | ZoningFlags.Overwrite });
                _em.AddComponent<Updated>(def);
                n++;
            }
            return n;
        }

        public int CountZonedCells()
        {
            int zoned = 0;
            using (var q = _em.CreateEntityQuery(ComponentType.ReadOnly<Block>(), ComponentType.ReadOnly<Cell>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>()))
            using (var all = q.ToEntityArray(Allocator.Temp))
            {
                foreach (var e in all)
                {
                    var cells = _em.GetBuffer<Cell>(e, true);
                    for (int i = 0; i < cells.Length; i++)
                        if (cells[i].m_Zone.m_Index != 0 && (cells[i].m_State & CellFlags.Visible) != 0) zoned++;
                }
            }
            return zoned;
        }

        // ───────────── plopped buildings ─────────────

        /// <summary>Two small park buildings along the south street of the Survive block, fronts on the street.</summary>
        public int EmitBuildings(SuiteFixtureToolSystem tool)
        {
            var parks = new List<(string Name, Entity Prefab, int2 Lot)>();
            foreach (var kv in Prefabs)
            {
                var e = kv.Value;
                if (!_em.HasComponent<ParkData>(e) || !_em.HasComponent<BuildingData>(e)) continue;
                var lot = _em.GetComponentData<BuildingData>(e).m_LotSize;
                if (lot.x < 2 || lot.y < 2 || lot.x > 5 || lot.y > 5) continue;
                parks.Add((kv.Key, e, lot));
            }
            parks.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
            if (parks.Count == 0) { Note("no small park building prefab found"); return 0; }
            int n = 0;
            foreach (var b in Blocks)
            {
                if (b.Kind != SuiteBlockKind.Survive) continue;
                float roadEdge = b.Z0 + HalfWidth(b.South);
                float[] xs = { b.X0 + HalfWidth(b.West) + 30f, b.X1 - HalfWidth(b.East) - 30f };
                for (int k = 0; k < 2; k++)
                {
                    var park = parks[(k * 7) % parks.Count];
                    float z = roadEdge + park.Lot.y * 4f + 0.5f;
                    var pos = new float3(xs[k], H(xs[k], z), z);
                    var rot = quaternion.RotateY(math.PI);   // front (+Z local) faces the south street
                    var def = tool.NewDefinition();
                    _em.AddComponentData(def, new CreationDefinition { m_Prefab = park.Prefab, m_RandomSeed = 1234 + k });
                    _em.AddComponent<Updated>(def);
                    _em.AddComponentData(def, new ObjectDefinition
                    {
                        m_Position = pos, m_LocalPosition = pos, m_Rotation = rot, m_LocalRotation = rot, m_Scale = new float3(1f),
                        m_Intensity = 1f, m_Age = 0.5f, m_Probability = 100, m_PrefabSubIndex = -1, m_ParentMesh = -1, m_Elevation = 0f,
                    });
                    Note($"plop '{park.Name}' lot {park.Lot.x}x{park.Lot.y} at ({pos.x:F0},{pos.z:F0}) in {b.Id}");
                    n++;
                }
            }
            return n;
        }

        /// <summary>Buildings standing in a rectangle (not temp, not deleted).</summary>
        public List<Entity> BuildingsIn(float2 min, float2 max)
        {
            var r = new List<Entity>();
            using (var q = _em.CreateEntityQuery(ComponentType.ReadOnly<global::Game.Buildings.Building>(), ComponentType.ReadOnly<global::Game.Objects.Transform>(),
                ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>()))
            using (var all = q.ToEntityArray(Allocator.Temp))
            {
                foreach (var e in all)
                {
                    var p = _em.GetComponentData<global::Game.Objects.Transform>(e).m_Position;
                    if (p.x >= min.x && p.x <= max.x && p.z >= min.y && p.z <= max.y) r.Add(e);
                }
            }
            return r;
        }

        public int CountAllBuildings()
        {
            using (var q = _em.CreateEntityQuery(ComponentType.ReadOnly<global::Game.Buildings.Building>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>()))
                return q.CalculateEntityCount();
        }

        // ───────────── block assignment ─────────────

        private readonly HashSet<string> _used = new HashSet<string>(StringComparer.Ordinal);

        public SuiteBlock Take(string site)
        {
            SuiteBlockKind? want = null;
            switch ((site ?? "any").ToLowerInvariant())
            {
                case "flat": want = SuiteBlockKind.Flat; break;
                case "zoned": want = SuiteBlockKind.Zoned; break;
                case "slope": want = SuiteBlockKind.Slope; break;
                case "rough": want = SuiteBlockKind.Rough; break;
                case "survive": want = SuiteBlockKind.Survive; break;
            }
            SuiteBlock pick = null;
            if (want.HasValue)
                foreach (var b in Blocks) if (b.Kind == want.Value && !_used.Contains(b.Id)) { pick = b; break; }
            if (pick == null)
                foreach (var b in Blocks) if (b.Kind == SuiteBlockKind.Flat && !_used.Contains(b.Id)) { pick = b; break; }
            if (pick == null)
                foreach (var b in Blocks) if (!_used.Contains(b.Id)) { pick = b; break; }
            if (pick != null) _used.Add(pick.Id);
            return pick;
        }
    }
}
