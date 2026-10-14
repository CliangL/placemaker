// Placemaker — what Block paint must not paint over, gathered around one city block.
//
// Read-only, bounded queries on the game's search trees (never a scan of the map), main thread only:
//   * building lots (8 m lot cells from BuildingData, like SiteCapture / SnapCollector),
//   * lot-type areas (other Placemaker areas, parks, extractor lots…) as their node polygons,
//   * zone grid cells: zoned ones (m_Zone != None) always, empty visible ones when the player asked
//     for it. Cells are merged into runs along the block depth to keep the polygon count low,
//   * water: a point predicate (WaterUtils depth) evaluated by Core at brush cell centres.
// Members of the group being edited are skipped, so its own lot does not block repainting it.

using System;
using System.Collections.Generic;
using Colossal.Collections;
using Colossal.Mathematics;
using Game.Common;
using Game.Prefabs;
using Game.Simulation;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Placemaker.Game.Selection
{
    public sealed class BlockObstacles
    {
        /// <summary>Water deeper than this (m) cannot be painted.</summary>
        public const float MaxWaterDepth = 0.3f;
        private const int MaxObstacles = 4000;

        private readonly EntityManager _em;
        private readonly global::Game.Objects.SearchSystem _objectSearch;
        private readonly global::Game.Areas.SearchSystem _areaSearch;
        private readonly global::Game.Zones.SearchSystem _zoneSearch;
        private readonly WaterSystem _water;

        public BlockObstacles(EntityManager em, World world)
        {
            _em = em;
            _objectSearch = world.GetOrCreateSystemManaged<global::Game.Objects.SearchSystem>();
            _areaSearch = world.GetOrCreateSystemManaged<global::Game.Areas.SearchSystem>();
            _zoneSearch = world.GetOrCreateSystemManaged<global::Game.Zones.SearchSystem>();
            _water = world.GetOrCreateSystemManaged<WaterSystem>();
        }

        private struct EntityIterator : INativeQuadTreeIterator<Entity, QuadTreeBoundsXZ>
        {
            public Bounds2 Bounds;
            public NativeList<Entity> Results;
            public bool Intersect(QuadTreeBoundsXZ bounds) => MathUtils.Intersect(bounds.m_Bounds.xz, Bounds);
            public void Iterate(QuadTreeBoundsXZ bounds, Entity e)
            {
                if (MathUtils.Intersect(bounds.m_Bounds.xz, Bounds)) Results.Add(e);
            }
        }

        private struct AreaIterator : INativeQuadTreeIterator<global::Game.Areas.AreaSearchItem, QuadTreeBoundsXZ>
        {
            public Bounds2 Bounds;
            public NativeList<Entity> Results;
            public bool Intersect(QuadTreeBoundsXZ bounds) => MathUtils.Intersect(bounds.m_Bounds.xz, Bounds);
            public void Iterate(QuadTreeBoundsXZ bounds, global::Game.Areas.AreaSearchItem item)
            {
                if (MathUtils.Intersect(bounds.m_Bounds.xz, Bounds)) Results.Add(item.m_Area);
            }
        }

        private struct ZoneIterator : INativeQuadTreeIterator<Entity, Bounds2>
        {
            public Bounds2 Bounds;
            public NativeList<Entity> Results;
            public bool Intersect(Bounds2 bounds) => MathUtils.Intersect(bounds, Bounds);
            public void Iterate(Bounds2 bounds, Entity e)
            {
                if (MathUtils.Intersect(bounds, Bounds)) Results.Add(e);
            }
        }

        /// <summary>
        /// Obstacle polygons (XZ) touching the box [min, max]. emptyZoneCells: also avoid visible zone
        /// cells nobody zoned yet. ignoreGroup: Placemaker group whose members are not obstacles (0 = none).
        /// </summary>
        public List<float2[]> Gather(float2 min, float2 max, bool emptyZoneCells, int ignoreGroup)
        {
            var list = new List<float2[]>();
            var bounds = new Bounds2(min, max);
            try { Buildings(bounds, ignoreGroup, list); }
            catch (Exception e) { Mod.Log.Warn("Placemaker: block paint could not read buildings: " + e.Message); }
            try { Lots(bounds, ignoreGroup, list); }
            catch (Exception e) { Mod.Log.Warn("Placemaker: block paint could not read areas: " + e.Message); }
            try { ZoneCells(bounds, emptyZoneCells, list); }
            catch (Exception e) { Mod.Log.Warn("Placemaker: block paint could not read zoning: " + e.Message); }
            return list;
        }

        /// <summary>
        /// Predicate "under water" for points in the box, or null when the box is dry. Sampled once into
        /// a grid (the water surface data must not be held across frames).
        /// </summary>
        public Func<float2, bool> Water(float2 min, float2 max, float step)
        {
            try
            {
                if (_water == null) return null;
                step = math.max(2f, step);
                int nx = (int)math.ceil((max.x - min.x) / step) + 1, nz = (int)math.ceil((max.y - min.y) / step) + 1;
                if ((long)nx * nz > 250_000) return null;
                var wet = new bool[nx * nz];
                var data = _water.GetSurfacesData(out var deps);
                deps.Complete();
                bool any = false;
                for (int j = 0; j < nz; j++)
                    for (int i = 0; i < nx; i++)
                    {
                        float d = WaterUtils.SampleDepth(ref data, new float3(min.x + (i + 0.5f) * step, 0f, min.y + (j + 0.5f) * step));
                        if (d > MaxWaterDepth) { wet[j * nx + i] = true; any = true; }
                    }
                if (!any) return null;
                return p =>
                {
                    int i = (int)math.floor((p.x - min.x) / step), j = (int)math.floor((p.y - min.y) / step);
                    return i >= 0 && j >= 0 && i < nx && j < nz && wet[j * nx + i];
                };
            }
            catch (Exception e)
            {
                Mod.Log.Warn("Placemaker: block paint water check failed (" + e.Message + "); assuming dry ground");
                return null;
            }
        }

        private bool Skip(Entity e, int ignoreGroup)
        {
            if (_em.HasComponent<Temp>(e) || _em.HasComponent<Deleted>(e)) return true;
            if (ignoreGroup != 0)
            {
                try { if (Services.Groups != null && Services.Groups.GroupOf(e) == ignoreGroup) return true; }
                catch (Exception) { }
            }
            return false;
        }

        private void Buildings(Bounds2 bounds, int ignoreGroup, List<float2[]> into)
        {
            if (_objectSearch == null) return;
            var tree = _objectSearch.GetStaticSearchTree(true, out var deps);
            deps.Complete();
            var results = new NativeList<Entity>(64, Allocator.Temp);
            try
            {
                var it = new EntityIterator { Bounds = bounds, Results = results };
                tree.Iterate(ref it);
                var seen = new HashSet<Entity>();
                for (int i = 0; i < results.Length && into.Count < MaxObstacles; i++)
                {
                    var e = results[i];
                    if (!seen.Add(e)) continue;
                    if (!_em.HasComponent<global::Game.Buildings.Building>(e) || !_em.HasComponent<global::Game.Objects.Transform>(e)
                        || !_em.HasComponent<PrefabRef>(e) || Skip(e, ignoreGroup)) continue;
                    var prefab = _em.GetComponentData<PrefabRef>(e).m_Prefab;
                    if (!_em.HasComponent<BuildingData>(prefab)) continue;
                    var lot = (float2)_em.GetComponentData<BuildingData>(prefab).m_LotSize * 8f;
                    var tr = _em.GetComponentData<global::Game.Objects.Transform>(e);
                    var q = global::Game.Objects.ObjectUtils.CalculateBaseCorners(tr.m_Position, tr.m_Rotation, lot);
                    into.Add(new[] { q.a.xz, q.b.xz, q.c.xz, q.d.xz });
                }
            }
            finally { results.Dispose(); }
        }

        private void Lots(Bounds2 bounds, int ignoreGroup, List<float2[]> into)
        {
            if (_areaSearch == null) return;
            var tree = _areaSearch.GetSearchTree(true, out var deps);
            deps.Complete();
            var results = new NativeList<Entity>(32, Allocator.Temp);
            try
            {
                var it = new AreaIterator { Bounds = bounds, Results = results };
                tree.Iterate(ref it);
                var seen = new HashSet<Entity>();
                for (int i = 0; i < results.Length && into.Count < MaxObstacles; i++)
                {
                    var e = results[i];
                    if (e == Entity.Null || !seen.Add(e)) continue;
                    if (!_em.HasComponent<global::Game.Areas.Lot>(e) || !_em.HasBuffer<global::Game.Areas.Node>(e) || Skip(e, ignoreGroup)) continue;
                    var nodes = _em.GetBuffer<global::Game.Areas.Node>(e, true);
                    if (nodes.Length < 3) continue;
                    var ring = new float2[nodes.Length];
                    for (int n = 0; n < nodes.Length; n++) ring[n] = nodes[n].m_Position.xz;
                    into.Add(ring);
                }
            }
            finally { results.Dispose(); }
        }

        private void ZoneCells(Bounds2 bounds, bool emptyCells, List<float2[]> into)
        {
            if (_zoneSearch == null) return;
            var tree = _zoneSearch.GetSearchTree(true, out var deps);
            deps.Complete();
            var results = new NativeList<Entity>(32, Allocator.Temp);
            try
            {
                var it = new ZoneIterator { Bounds = bounds, Results = results };
                tree.Iterate(ref it);
                var seen = new HashSet<Entity>();
                for (int i = 0; i < results.Length && into.Count < MaxObstacles; i++)
                {
                    var e = results[i];
                    if (!seen.Add(e)) continue;
                    if (!_em.HasComponent<global::Game.Zones.Block>(e) || !_em.HasBuffer<global::Game.Zones.Cell>(e)
                        || _em.HasComponent<Temp>(e) || _em.HasComponent<Deleted>(e)) continue;
                    var block = _em.GetComponentData<global::Game.Zones.Block>(e);
                    var cells = _em.GetBuffer<global::Game.Zones.Cell>(e, true);
                    int2 size = block.m_Size;
                    if (size.x <= 0 || size.y <= 0 || cells.Length < size.x * size.y) continue;
                    float2 dir = block.m_Direction, right = MathUtils.Right(dir);
                    // Cell index y runs from the road (0) into the block; merge consecutive cells of a column.
                    for (int x = 0; x < size.x; x++)
                    {
                        int start = -1;
                        for (int y = 0; y <= size.y; y++)
                        {
                            bool take = y < size.y && Avoid(cells[x + y * size.x], emptyCells);
                            if (take && start < 0) start = y;
                            if (!take && start >= 0)
                            {
                                var a = global::Game.Zones.ZoneUtils.GetCellPosition(block, new int2(x, start)).xz;
                                var b = global::Game.Zones.ZoneUtils.GetCellPosition(block, new int2(x, y - 1)).xz;
                                // GetCellPosition steps against m_Direction as y grows: a is the road end.
                                float2 front = a + dir * 4f, back = b - dir * 4f;
                                into.Add(new[] { front + right * 4f, front - right * 4f, back - right * 4f, back + right * 4f });
                                start = -1;
                            }
                        }
                    }
                }
            }
            finally { results.Dispose(); }
        }

        private static bool Avoid(global::Game.Zones.Cell cell, bool emptyCells)
        {
            var s = cell.m_State;
            // Hidden or blocked cells (under a road, a lot, an area) are not part of the visible grid.
            if ((s & global::Game.Zones.CellFlags.Visible) == 0 || (s & global::Game.Zones.CellFlags.Blocked) != 0) return false;
            if (!cell.m_Zone.Equals(global::Game.Zones.ZoneType.None)) return true;
            return emptyCells;
        }
    }
}
