// Placemaker — asset catalog (GPL-3.0).
// Classification adapted from ParkManager's ParkAssetCatalogSystem (tomkpunkt, GPL-3.0),
// including its trick of reading vanilla park palettes from ParkData prefabs' sub-objects.
//
// Phase: UIUpdate. GameSimulation does not tick while paused, and the catalog must be ready in
// the editor-like pause the player uses to plan. Rescans when the prefab count changes (asset
// packs/mods load late) and after every game load.

using System;
using System.Collections.Generic;
using System.Text;
using Colossal.Serialization.Entities;
using Game;
using Game.Common;
using Game.Prefabs;
using Game.UI;
using Placemaker.Core;
using Placemaker.Core.Build;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine.Scripting;

namespace Placemaker.Game.Assets
{
    public sealed partial class AssetCatalogSystem : GameSystemBase, IAssetCatalog
    {
        private const int RecheckFrames = 120;

        private PrefabSystem _prefabs;
        private EntityQuery _prefabQuery;
        private int _lastPrefabCount = -1;
        private int _framesSinceCheck;
        private bool _scanRequested = true;
        private bool _ready;
        private int _version;

        private readonly Dictionary<string, List<CatalogItem>> _byCategory = new Dictionary<string, List<CatalogItem>>(StringComparer.Ordinal);
        private readonly Dictionary<string, CatalogItem> _byName = new Dictionary<string, CatalogItem>(StringComparer.Ordinal);
        private readonly Dictionary<string, Entity> _allPrefabsByName = new Dictionary<string, Entity>(StringComparer.Ordinal);
        private readonly Dictionary<string, Entity> _defaults = new Dictionary<string, Entity>(StringComparer.Ordinal);
        private Dictionary<string, AssetInfo> _infoMap = new Dictionary<string, AssetInfo>(StringComparer.Ordinal);
        private static readonly IReadOnlyList<CatalogItem> Empty = Array.Empty<CatalogItem>();

        public bool Ready => _ready;
        public int Version => _version;

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            _prefabs = World.GetOrCreateSystemManaged<PrefabSystem>();
            _prefabQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<PrefabData>() },
                None = new[] { ComponentType.ReadOnly<Deleted>() },
            });
            Services.Assets = this;
        }

        [Preserve]
        protected override void OnDestroy()
        {
            if (ReferenceEquals(Services.Assets, this)) Services.Assets = null;
            base.OnDestroy();
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);
            _scanRequested = true;
        }

        public void RequestRescan() => _scanRequested = true;

        [Preserve]
        protected override void OnUpdate()
        {
            if (!_scanRequested && ++_framesSinceCheck < RecheckFrames) return;
            _framesSinceCheck = 0;
            int count = _prefabQuery.CalculateEntityCount();
            if (!_scanRequested && count == _lastPrefabCount) return;
            _scanRequested = false;
            _lastPrefabCount = count;
            try
            {
                Scan();
            }
            catch (Exception e)
            {
                Mod.Log.Error(e, "Placemaker asset scan failed; keeping the previous catalog");
            }
        }

        // ───────────────────────── IAssetCatalog ─────────────────────────

        public IReadOnlyList<CatalogItem> Items(string category) =>
            category != null && _byCategory.TryGetValue(category, out var list) ? list : Empty;

        public Entity Resolve(AssetRef asset, string category)
        {
            var e = ResolveName(asset.Id, category);
            if (e != Entity.Null) return e;
            if (asset.Fallbacks != null)
                foreach (var f in asset.Fallbacks)
                {
                    e = ResolveName(f, category);
                    if (e != Entity.Null) return e;
                }
            return DefaultFor(category);
        }

        public Dictionary<string, AssetInfo> InfoMap() => new Dictionary<string, AssetInfo>(_infoMap, StringComparer.Ordinal);

        // ───────────────────────── extra API for build/groups ─────────────────────────

        /// <summary>True when any loaded prefab has this name (missing-asset detection).</summary>
        public bool PrefabExists(string name) =>
            !string.IsNullOrEmpty(name) && _allPrefabsByName.TryGetValue(name, out var e) && Usable(e);

        public bool TryGetItem(string name, out CatalogItem item) => _byName.TryGetValue(name ?? "", out item);

        public string NameOf(Entity prefab)
        {
            if (prefab == Entity.Null) return "";
            try { return _prefabs.TryGetPrefab<PrefabBase>(prefab, out var p) && p != null ? p.name : ""; }
            catch (Exception) { return ""; }
        }

        public Entity DefaultFor(string category)
        {
            if (category == null) return Entity.Null;
            if (_defaults.TryGetValue(category, out var e) && Usable(e)) return e;
            // Sub-categories fall back to their parent ("surface.grass" → "surface").
            int dot = category.LastIndexOf('.');
            if (dot > 0 && category.StartsWith("surface", StringComparison.Ordinal))
                return DefaultFor(category.Substring(0, dot));
            return Entity.Null;
        }

        /// <summary>Maximum edge length of a net prefab (0 = unknown). CS2 rejects longer edges.</summary>
        public float MaxEdgeLength(Entity prefab)
        {
            if (prefab == Entity.Null || !EntityManager.HasComponent<NetGeometryData>(prefab)) return 0f;
            float max = EntityManager.GetComponentData<NetGeometryData>(prefab).m_EdgeLengthRange.max;
            return math.isfinite(max) && max > 1f ? max : 0f;
        }

        private Entity ResolveName(string name, string category)
        {
            if (string.IsNullOrEmpty(name)) return Entity.Null;
            if (_byName.TryGetValue(name, out var item) && Usable(item.Prefab)) return item.Prefab;
            // Not catalogued (e.g. a prefab a layer names explicitly that our heuristics skip):
            // still honour it when it exists and is of a usable kind for the category.
            if (_allPrefabsByName.TryGetValue(name, out var e) && Usable(e) && KindMatches(e, category)) return e;
            return Entity.Null;
        }

        private bool KindMatches(Entity e, string category)
        {
            if (string.IsNullOrEmpty(category)) return true;
            if (category.StartsWith("surface", StringComparison.Ordinal) || category.StartsWith("area.", StringComparison.Ordinal))
                return EntityManager.HasComponent<AreaGeometryData>(e);
            if (category.StartsWith("net.", StringComparison.Ordinal))
                return EntityManager.HasComponent<NetData>(e);
            if (category == AssetCategories.Zone) return EntityManager.HasComponent<ZoneData>(e);
            if (category == AssetCategories.Fence) return EntityManager.HasComponent<NetData>(e) || EntityManager.HasComponent<ObjectGeometryData>(e);
            // Objects: must be a real, non-building static object (mesh-less static objects crash).
            return EntityManager.HasComponent<ObjectGeometryData>(e) && !EntityManager.HasComponent<BuildingData>(e)
                && !EntityManager.HasComponent<PlaceholderObjectData>(e);
        }

        private bool Usable(Entity e) => e != Entity.Null && EntityManager.Exists(e) && !EntityManager.HasComponent<Deleted>(e);

        // ───────────────────────── scan ─────────────────────────

        private void Scan()
        {
            var byCategory = new Dictionary<string, List<CatalogItem>>(StringComparer.Ordinal);
            var byName = new Dictionary<string, CatalogItem>(StringComparer.Ordinal);
            var allByName = new Dictionary<string, Entity>(StringComparer.Ordinal);
            var info = new Dictionary<string, AssetInfo>(StringComparer.Ordinal);
            var tags = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            int failed = 0;
            using var entities = _prefabQuery.ToEntityArray(Allocator.Temp);

            for (int i = 0; i < entities.Length; i++)
            {
                var entity = entities[i];
                // One broken prefab (half-loaded pack, missing DLC reference) must not empty every list (PM).
                try
                {
                    if (!_prefabs.TryGetPrefab<PrefabBase>(entity, out var prefab) || prefab == null || string.IsNullOrWhiteSpace(prefab.name)) continue;
                    if (!allByName.ContainsKey(prefab.name)) allByName[prefab.name] = entity;
                    var traits = Traits(entity, prefab);
                    var categories = AssetHeuristics.Classify(prefab.name, traits);
                    if (categories.Count == 0) continue;
                    if (byName.ContainsKey(prefab.name)) continue;   // duplicate names: first wins (saves resolve by name too)
                    var item = new CatalogItem
                    {
                        Id = prefab.name,
                        DisplayName = DisplayName(prefab),
                        Icon = Icon(prefab),
                        Prefab = entity,
                        Info = Info(entity, prefab.name),
                    };
                    var t = new List<string>(categories);
                    t.Add(prefab.isBuiltin ? "source:vanilla" : "source:mod");
                    tags[prefab.name] = t;
                    byName[prefab.name] = item;
                    info[prefab.name] = item.Info;
                    foreach (var c in categories)
                    {
                        if (!byCategory.TryGetValue(c, out var list)) byCategory[c] = list = new List<CatalogItem>();
                        list.Add(item);
                    }
                }
                catch (Exception e)
                {
                    if (failed++ < 5) Mod.Log.Warn($"Placemaker skipped prefab {entity} while scanning assets: {e.Message}");
                }
            }

            try { ScanParkPalettes(entities, byName, tags); }
            catch (Exception e) { Mod.Log.Warn("Placemaker could not read vanilla park palettes: " + e.Message); }

            foreach (var kv in byName) kv.Value.Tags = tags.TryGetValue(kv.Key, out var t) ? t.ToArray() : Array.Empty<string>();

            // Prefer native network fences when the game has them (PM): prop pieces stay as fallback.
            var defaults = new Dictionary<string, Entity>(StringComparer.Ordinal);
            foreach (var kv in byCategory)
            {
                kv.Value.Sort((a, b) => string.Compare(a.DisplayName, b.DisplayName, StringComparison.CurrentCultureIgnoreCase));
                CatalogItem best = null; int bestScore = int.MinValue;
                foreach (var it in kv.Value)
                {
                    int s = AssetHeuristics.DefaultScore(kv.Key, it.Id, it.Info.NetWidth);
                    if (Array.IndexOf(it.Tags, "source:vanilla") >= 0) s += 5;   // stable defaults across playsets
                    if (kv.Key == AssetCategories.Fence && EntityManager.HasComponent<NetData>(it.Prefab)) s += 300;
                    if (s > bestScore || (s == bestScore && best != null && string.CompareOrdinal(it.Id, best.Id) < 0)) { best = it; bestScore = s; }
                }
                if (best != null) defaults[kv.Key] = best.Prefab;
            }

            bool changed = !SameContent(byName);
            _byCategory.Clear(); foreach (var kv in byCategory) _byCategory[kv.Key] = kv.Value;
            _byName.Clear(); foreach (var kv in byName) _byName[kv.Key] = kv.Value;
            _allPrefabsByName.Clear(); foreach (var kv in allByName) _allPrefabsByName[kv.Key] = kv.Value;
            _defaults.Clear(); foreach (var kv in defaults) _defaults[kv.Key] = kv.Value;
            _infoMap = info;
            if (changed || !_ready) _version++;
            _ready = true;

            var summary = new StringBuilder();
            foreach (var kv in _byCategory) summary.Append(kv.Key).Append('=').Append(kv.Value.Count).Append(' ');
            Mod.Log.Info($"Placemaker asset catalog v{_version}: {summary}({failed} skipped)");
        }

        private int _lastSignature;

        private bool SameContent(Dictionary<string, CatalogItem> byName)
        {
            unchecked
            {
                int sig = byName.Count;
                foreach (var kv in byName) sig = sig * 31 + kv.Key.GetHashCode() + kv.Value.Prefab.Index;
                bool same = sig == _lastSignature && _ready;
                _lastSignature = sig;
                return same;
            }
        }

        private AssetTraits Traits(Entity e, PrefabBase prefab)
        {
            var t = AssetTraits.None;
            if (EntityManager.HasComponent<PlaceholderObjectData>(e) || EntityManager.HasBuffer<PlaceholderObjectElement>(e)) t |= AssetTraits.Placeholder;
            if (ImageSystem.GetIcon(prefab) != null) t |= AssetTraits.HasIcon;

            if (EntityManager.HasComponent<ZoneData>(e)) return t | AssetTraits.Zone;

            if (EntityManager.HasComponent<AreaGeometryData>(e))
            {
                var type = EntityManager.GetComponentData<AreaGeometryData>(e).m_Type;
                if (type == global::Game.Areas.AreaType.Surface) t |= AssetTraits.Surface;
                else if (type == global::Game.Areas.AreaType.Space) t |= AssetTraits.SpaceArea;
                else if (type == global::Game.Areas.AreaType.Lot) t |= AssetTraits.LotArea;
                // Surfaces/areas need a valid archetype or the definition is silently dropped.
                if (EntityManager.HasComponent<AreaData>(e) && !EntityManager.GetComponentData<AreaData>(e).m_Archetype.Valid) t |= AssetTraits.Placeholder;
                return t;
            }

            if (EntityManager.HasComponent<NetData>(e))
            {
                t |= AssetTraits.Net;
                if (!EntityManager.HasComponent<NetGeometryData>(e)) t |= AssetTraits.Placeholder;
                if (EntityManager.HasComponent<FenceData>(e) || EntityManager.HasComponent<NetFenceData>(e)) t |= AssetTraits.NetFence;
                if (EntityManager.HasComponent<PathwayData>(e)) t |= AssetTraits.Pathway;
                if (EntityManager.HasComponent<RoadData>(e)) t |= AssetTraits.Road;
                return t;
            }

            if (!(prefab is StaticObjectPrefab) || !EntityManager.HasComponent<ObjectGeometryData>(e)) return t;
            t |= AssetTraits.StaticObject;
            if (prefab is BuildingPrefab || EntityManager.HasComponent<BuildingData>(e)) t |= AssetTraits.Building;
            if (prefab is ObjectGeometryPrefab g && g.m_Meshes != null && g.m_Meshes.Length > 0) t |= AssetTraits.HasMesh;
            if (EntityManager.HasComponent<PlantData>(e)) t |= AssetTraits.Plant;
            if (EntityManager.HasComponent<TreeData>(e)) t |= AssetTraits.Tree;
            if (EntityManager.HasComponent<StreetLightData>(e)) t |= AssetTraits.StreetLight;
            if (prefab.TryGet<ObjectSubObjects>(out var subs) && subs?.m_SubObjects != null && subs.m_SubObjects.Length > 0) t |= AssetTraits.HasSubObjects;

            if (EntityManager.HasBuffer<ActivityLocationElement>(e))
            {
                uint sitting = new ActivityMask(ActivityType.BenchSitting).m_Mask;
                var locations = EntityManager.GetBuffer<ActivityLocationElement>(e, true);
                for (int i = 0; i < locations.Length; i++)
                    if ((locations[i].m_ActivityMask.m_Mask & sitting) != 0) { t |= AssetTraits.BenchActivity; break; }
            }

            if (EntityManager.HasBuffer<SubLane>(e))
            {
                t |= AssetTraits.HasSubLane;
                var lanes = EntityManager.GetBuffer<SubLane>(e, true);
                for (int i = 0; i < lanes.Length; i++)
                {
                    var lane = lanes[i].m_Prefab;
                    if (lane != Entity.Null && EntityManager.Exists(lane) && EntityManager.HasComponent<ParkingLaneData>(lane))
                    { t |= AssetTraits.ParkingLaneSubLane; break; }
                }
            }
            return t;
        }

        private AssetInfo Info(Entity e, string name)
        {
            var info = new AssetInfo { Id = name };
            if (EntityManager.HasComponent<ObjectGeometryData>(e))
            {
                var g = EntityManager.GetComponentData<ObjectGeometryData>(e);
                info.BoundsMin = g.m_Bounds.min;
                info.BoundsMax = g.m_Bounds.max;
            }
            if (EntityManager.HasComponent<PlaceableObjectData>(e))
                info.Cost = EntityManager.GetComponentData<PlaceableObjectData>(e).m_ConstructionCost;
            if (EntityManager.HasComponent<NetGeometryData>(e))
                info.NetWidth = EntityManager.GetComponentData<NetGeometryData>(e).m_DefaultWidth;
            if (EntityManager.HasComponent<PlaceableNetData>(e))
            {
                // Nets are charged per started 8 m cell (NetUtils.GetConstructionCost); the UI's
                // "per km" figure is cost × 125. AssetInfo wants per metre.
                var p = EntityManager.GetComponentData<PlaceableNetData>(e);
                info.Cost = p.m_DefaultConstructionCost / 8f;
                info.Upkeep = p.m_DefaultUpkeepCost / 8f;
            }
            return info;
        }

        private static string DisplayName(PrefabBase prefab)
        {
            try
            {
                var dict = global::Game.SceneFlow.GameManager.instance?.localizationManager?.activeDictionary;
                if (dict != null && dict.TryGetValue("Assets.NAME[" + prefab.name + "]", out var value) && !string.IsNullOrWhiteSpace(value))
                    return value;
            }
            catch (Exception) { }
            return prefab.name;
        }

        private static string Icon(PrefabBase prefab)
        {
            try { return ImageSystem.GetThumbnail(prefab) ?? ""; }
            catch (Exception) { return ""; }
        }

        /// <summary>
        /// Walks the managed sub-objects of every ParkData prefab (vanilla parks) and tags each
        /// catalogued asset found there with "palette:&lt;park name&gt;". Layers use these to dress a
        /// park in a consistent vanilla style.
        /// </summary>
        private void ScanParkPalettes(NativeArray<Entity> prefabs, Dictionary<string, CatalogItem> byName, Dictionary<string, List<string>> tags)
        {
            int palettes = 0;
            for (int i = 0; i < prefabs.Length; i++)
            {
                var entity = prefabs[i];
                if (!EntityManager.HasComponent<ParkData>(entity) || !_prefabs.TryGetPrefab<PrefabBase>(entity, out var park) || park == null) continue;
                var tag = AssetHeuristics.PaletteTag(park.name);
                var visited = new HashSet<Entity>();
                int found = 0;
                CollectSubObjects(park, tag, visited, 0, byName, tags, ref found);
                if (found > 0) palettes++;
            }
            Mod.Log.Info($"Placemaker discovered {palettes} vanilla park palettes.");
        }

        private void CollectSubObjects(PrefabBase parent, string tag, HashSet<Entity> visited, int depth,
            Dictionary<string, CatalogItem> byName, Dictionary<string, List<string>> tags, ref int found)
        {
            if (parent == null || depth > 8 || !parent.TryGet<ObjectSubObjects>(out var component) || component?.m_SubObjects == null) return;
            foreach (var sub in component.m_SubObjects)
            {
                var child = sub?.m_Object;
                // GetEntity throws for prefabs the game never registered (content not installed).
                if (child == null || !_prefabs.TryGetEntity(child, out var childEntity)) continue;
                CollectPaletteObject(childEntity, tag, visited, depth + 1, byName, tags, ref found);
            }
        }

        private void CollectPaletteObject(Entity entity, string tag, HashSet<Entity> visited, int depth,
            Dictionary<string, CatalogItem> byName, Dictionary<string, List<string>> tags, ref int found)
        {
            if (entity == Entity.Null || depth > 8 || !visited.Add(entity) || !EntityManager.Exists(entity)) return;
            if (EntityManager.HasBuffer<PlaceholderObjectElement>(entity))
            {
                var variants = EntityManager.GetBuffer<PlaceholderObjectElement>(entity, true);
                var copy = new List<Entity>(variants.Length);
                for (int i = 0; i < variants.Length; i++) copy.Add(variants[i].m_Object);
                foreach (var v in copy) CollectPaletteObject(v, tag, visited, depth + 1, byName, tags, ref found);
            }
            if (!_prefabs.TryGetPrefab<PrefabBase>(entity, out var prefab) || prefab == null) return;
            if (byName.ContainsKey(prefab.name) && tags.TryGetValue(prefab.name, out var list) && !list.Contains(tag))
            {
                list.Add(tag);
                found++;
            }
            CollectSubObjects(prefab, tag, visited, depth, byName, tags, ref found);
        }
    }
}
