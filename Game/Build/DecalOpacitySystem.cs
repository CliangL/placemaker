// Placemaker — runtime see-through clones of decal prefabs (s23 decal art pass). GPL-3.0.
//
// User (in game): "They need to be a lot more transparent, blend with the terrain a lot more … most of them ~80 %
// transparent". CS2 has no per-instance decal opacity: a decal is a box mesh with an HDRP decal material, and the
// per-instance properties BatchDataSystem writes (colour masks from MeshColor, state, LOD fade…) are not read by the
// decal shaders (docs/RESEARCH-NOTES-ADDENDUM "Decal opacity"). Opacity is a MATERIAL property: HDRP's decal
// "_DecalBlend" (global opacity, 1 by default in every decal material) and the alpha of "_BaseColor". So we clone the
// decal per opacity tier: StaticObjectPrefab → RenderPrefab → an in-memory SurfaceAsset copy (SurfaceAsset.CopyFrom;
// our reference keeps its ref count ≥ 1 so ManagedBatchSystem's LoadProperties never re-reads a stream it does not have —
// the same in-memory path ExtraAssetsImporter uses to build pack decals) with _DecalBlend = tier. Geometry and textures
// are shared with the original.
//
// Engine rules (as SurfaceCloneSystem, RESEARCH-NOTES 1–3): AddPrefab only in PrefabUpdate before
// PrefabInitializeSystem; a clone is used only a few frames after registration; saves reference prefabs by name, so every
// clone ever used is remembered (ModsData/Placemaker/decal-opacity.txt) and recreated at startup. Until a clone is ready
// the build uses the original (full strength) — nothing breaks, it is just stronger.

using System;
using System.Collections.Generic;
using System.IO;
using Colossal.IO.AssetDatabase;
using Game;
using Game.Prefabs;
using Unity.Entities;
using UnityEngine;
using UnityEngine.Scripting;

namespace Placemaker.Game.Build
{
    public sealed partial class DecalOpacitySystem : GameSystemBase
    {
        public const string NamePrefix = "Placemaker Decal ";
        private const int ReadyDelayFrames = 3;

        /// <summary>Setting "See-through decals" (default on).</summary>
        public static bool FeatureEnabled = true;

        private PrefabSystem _prefabs;
        private readonly Dictionary<(Entity, int), Entity> _cloneOf = new Dictionary<(Entity, int), Entity>();
        private readonly Dictionary<Entity, int> _registeredFrame = new Dictionary<Entity, int>();
        private readonly HashSet<string> _requested = new HashSet<string>(StringComparer.Ordinal);
        private readonly List<(string name, int tier)> _queue = new List<(string, int)>();
        private readonly HashSet<string> _remembered = new HashSet<string>(StringComparer.Ordinal);
        private int _rememberedRetries;
        // Keeps every cloned surface referenced (ref count never drops to 0 → never re-read from a non-existent stream).
        private readonly List<SurfaceAsset> _surfaces = new List<SurfaceAsset>();

        public static string CloneName(string original, int tier) => NamePrefix + tier + "% (" + original + ")";

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            _prefabs = World.GetOrCreateSystemManaged<PrefabSystem>();
            LoadRemembered();
        }

        /// <summary>The ready see-through clone of a decal prefab for an opacity, else the original (and a request).</summary>
        public Entity CloneFor(Entity original, float opacity)
        {
            int tier = Core.Decals.DecalLook.Tier(opacity);
            if (!FeatureEnabled || original == Entity.Null || tier >= 100) return original;
            try
            {
                if (_cloneOf.TryGetValue((original, tier), out var clone) && IsReady(clone)) return clone;
                if (_prefabs.TryGetPrefab<StaticObjectPrefab>(original, out var prefab) && prefab != null && !prefab.name.StartsWith(NamePrefix, StringComparison.Ordinal))
                {
                    Request(prefab.name, tier);
                    if (_remembered.Add(tier + "|" + prefab.name)) SaveRemembered();
                }
            }
            catch (Exception e) { Mod.Log.Warn("Placemaker decal opacity lookup failed: " + e.Message); }
            return original;
        }

        private void Request(string name, int tier)
        {
            if (_requested.Add(tier + "|" + name)) _queue.Add((name, tier));
        }

        private bool IsReady(Entity clone)
        {
            if (clone == Entity.Null || !EntityManager.Exists(clone)) return false;
            if (!_registeredFrame.TryGetValue(clone, out var frame) || UnityEngine.Time.frameCount - frame < ReadyDelayFrames) return false;
            return EntityManager.HasComponent<ObjectGeometryData>(clone);
        }

        [Preserve]
        protected override void OnUpdate()
        {
            if (!FeatureEnabled) return;
            try
            {
                if (_rememberedRetries < 600 && _remembered.Count > 0) RequestRemembered();
                int budget = 24;
                while (_queue.Count > 0 && budget-- > 0) { var next = _queue[0]; _queue.RemoveAt(0); CreateClone(next.name, next.tier); }
            }
            catch (Exception e)
            {
                Mod.Log.Error(e, "Placemaker see-through decal clones failed; decals are built at full strength");
                FeatureEnabled = false;
            }
        }

        private void RequestRemembered()
        {
            _rememberedRetries++;
            bool all = true;
            foreach (var line in _remembered)
            {
                int bar = line.IndexOf('|');
                if (bar <= 0 || !int.TryParse(line.Substring(0, bar), out int tier)) continue;
                string name = line.Substring(bar + 1);
                if (_requested.Contains(line)) continue;
                if (_prefabs.TryGetPrefab(new PrefabID(nameof(StaticObjectPrefab), name), out var p) && p != null) Request(name, tier);
                else all = false;
            }
            if (all) _rememberedRetries = int.MaxValue;
        }

        private void CreateClone(string originalName, int tier)
        {
            if (!_prefabs.TryGetPrefab(new PrefabID(nameof(StaticObjectPrefab), originalName), out var p) || !(p is StaticObjectPrefab original)) return;
            if (!_prefabs.TryGetEntity(original, out var originalEntity) || original.m_Meshes == null || original.m_Meshes.Length == 0) return;
            var cloneName = CloneName(original.name, tier);
            if (_prefabs.TryGetPrefab(new PrefabID(nameof(StaticObjectPrefab), cloneName), out var existing) && existing != null && _prefabs.TryGetEntity(existing, out var existingEntity))
            {
                _cloneOf[(originalEntity, tier)] = existingEntity;
                if (!_registeredFrame.ContainsKey(existingEntity)) _registeredFrame[existingEntity] = UnityEngine.Time.frameCount;
                return;
            }
            float opacity = tier / 100f;
            var meshes = new ObjectMeshInfo[original.m_Meshes.Length];
            for (int i = 0; i < meshes.Length; i++)
            {
                var mi = original.m_Meshes[i];
                RenderPrefabBase mesh = mi?.m_Mesh;
                if (mi?.m_Mesh is RenderPrefab rp && rp.GetComponent<DecalProperties>() != null)
                {
                    var rc = CloneRender(rp, cloneName + " #" + i, opacity);
                    if (rc == null) return;   // keep the original; never half a clone
                    mesh = rc;
                }
                meshes[i] = new ObjectMeshInfo { m_Mesh = mesh, m_Position = mi.m_Position, m_Rotation = mi.m_Rotation, m_RequireState = mi.m_RequireState };
            }
            // CreateInstance + AddComponentFrom, never Object.Instantiate (shares ComponentBase instances — PLT).
            var clone = ScriptableObject.CreateInstance<StaticObjectPrefab>();
            clone.name = cloneName;
            clone.m_Meshes = meshes;
            clone.m_Circular = original.m_Circular;
            foreach (var component in original.components)
            {
                if (component == null || component is UIObject || component is ObsoleteIdentifiers) continue;
                clone.AddComponentFrom(component);
            }
            if (!_prefabs.AddPrefab(clone)) { UnityEngine.Object.Destroy(clone); return; }
            var entity = _prefabs.GetEntity(clone);
            _cloneOf[(originalEntity, tier)] = entity;
            _registeredFrame[entity] = UnityEngine.Time.frameCount;
        }

        private RenderPrefab CloneRender(RenderPrefab rp, string name, float opacity)
        {
            var surfaces = new List<SurfaceAsset>();
            for (int s = 0; s < rp.materialCount; s++)
            {
                var src = rp.GetSurfaceAsset(s);
                if (src == null) return null;
                src.LoadProperties(useVT: true);   // our own reference on the original: keeps its data loaded
                var path = AssetDataPath.Create("Placemaker/DecalOpacity", name + " " + s, EscapeStrategy.None);
                var copy = AssetDatabase.user.AddAsset<SurfaceAsset>(path, Colossal.Hash128.CreateGuid(name + " " + s));
                copy.CopyFrom(src);
                var floats = new Dictionary<string, float>(StringComparer.Ordinal);
                foreach (var kv in copy.floats) floats[kv.Key] = kv.Value;
                floats["_DecalBlend"] = (floats.TryGetValue("_DecalBlend", out var blend) ? blend : 1f) * opacity;
                copy.UpdateFloats(floats);
                if (copy.colors != null && copy.colors.TryGetValue("_BaseColor", out var baseColor))
                {
                    var colors = new Dictionary<string, Color>(StringComparer.Ordinal);
                    foreach (var kv in copy.colors) colors[kv.Key] = kv.Value;
                    baseColor.a *= opacity;
                    colors["_BaseColor"] = baseColor;
                    copy.UpdateColors(colors);
                }
                _surfaces.Add(copy);
                surfaces.Add(copy);
            }
            var r = ScriptableObject.CreateInstance<RenderPrefab>();
            r.name = name;
            r.geometryAsset = rp.geometryAsset;
            r.surfaceAssets = surfaces;
            r.bounds = rp.bounds;
            r.surfaceArea = rp.surfaceArea;
            r.meshCount = rp.meshCount;
            r.vertexCount = rp.vertexCount;
            r.indexCount = rp.indexCount;
            r.isImpostor = rp.isImpostor;
            r.manualVTRequired = rp.manualVTRequired;
            foreach (var component in rp.components)
                if (component != null && !(component is ObsoleteIdentifiers)) r.AddComponentFrom(component);
            if (!_prefabs.AddPrefab(r)) { UnityEngine.Object.Destroy(r); return null; }
            return r;
        }

        // ───────────────────────── remembered clones (saves reference them by name) ─────────────────────────

        private static string RememberedPath => Path.Combine(Application.persistentDataPath, "ModsData", "Placemaker", "decal-opacity.txt");

        private void LoadRemembered()
        {
            try
            {
                if (!File.Exists(RememberedPath)) return;
                foreach (var line in File.ReadAllLines(RememberedPath))
                    if (!string.IsNullOrWhiteSpace(line)) _remembered.Add(line.Trim());
            }
            catch (Exception e) { Mod.Log.Warn("Placemaker could not read the decal opacity list: " + e.Message); }
        }

        private void SaveRemembered()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(RememberedPath));
                var list = new List<string>(_remembered);
                list.Sort(StringComparer.Ordinal);
                File.WriteAllLines(RememberedPath, list);
            }
            catch (Exception e) { Mod.Log.Warn("Placemaker could not write the decal opacity list: " + e.Message); }
        }
    }
}
