// Placemaker — runtime see-through clones of decal prefabs (s23 decal art pass, s28 fix). GPL-3.0.
//
// User (in game): "much, much, much more transparent would solve everything". CS2 has no per-instance decal opacity
// (docs/RESEARCH-NOTES-ADDENDUM 82): a decal is a box mesh with a decal material, and its strength is MATERIAL data.
// So we clone the decal per opacity tier: StaticObjectPrefab → RenderPrefab → an in-memory SurfaceAsset copy
// (SurfaceAsset.CopyFrom; our reference keeps its data ref count ≥ 1 so ManagedBatchSystem's LoadProperties never
// re-reads a stream it does not have — the in-memory path ExtraAssetsImporter uses to build pack decals). Geometry and
// textures are shared with the original.
//
// Which property: pack decals use CS2's "DefaultDecal" material (HDRP decal shader graph). Its full property list (a
// pack's exported decal.json, e.g. Ground Texture Decal Pack Dirt01) has _BaseColor — stored as a VECTOR (x,y,z,w) in the
// SurfaceAsset, not as a colour — plus _NormalOpacity and _MetallicOpacity; there is no _DecalBlend. The decal's alpha is
// _BaseColor.w × the base map's alpha, and with _NormalAlphaSource / _MetallicAlphaSource = 0 normal and mask (AO,
// smoothness) blending use that alpha too, each further scaled by their own opacity. s23 scaled _DecalBlend and a
// _BaseColor COLOUR, neither of which the material reads — the copies drew exactly like the originals ("I don't think
// this is any more transparent than before"). s28 scales the _BaseColor vector's w, and _NormalOpacity/_MetallicOpacity
// (the dark "black oil" comes as much from the normal/AO channels as from albedo), keeping the other keys harmless.
//
// Engine rules (as SurfaceCloneSystem, RESEARCH-NOTES 1–3): AddPrefab only in PrefabUpdate before
// PrefabInitializeSystem; a clone is used only a few frames after registration; saves reference prefabs by name, so every
// clone ever used is remembered (ModsData/Placemaker/decal-opacity.txt, "tier|Type|name") and recreated at startup.
// Until a clone is ready the build uses the original (full strength) and the build log says so.

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

        /// <summary>Channel opacities of the decal material, scaled by the tier (normal and mask/AO blend).</summary>
        private static readonly string[] ChannelOpacities = { "_NormalOpacity", "_MetallicOpacity" };
        /// <summary>Opacity floats of other decal shaders (HDRP/Decal's _DecalBlend…): scaled when present, never added.</summary>
        private static readonly string[] OtherOpacities = { "_DecalBlend", "_BaseColorOpacity", "_Opacity" };

        private PrefabSystem _prefabs;
        private readonly Dictionary<(Entity, int), Entity> _cloneOf = new Dictionary<(Entity, int), Entity>();
        private readonly Dictionary<Entity, int> _registeredFrame = new Dictionary<Entity, int>();
        private readonly HashSet<(Entity, int)> _requested = new HashSet<(Entity, int)>();
        private readonly List<(Entity original, int tier)> _queue = new List<(Entity, int)>();
        private readonly HashSet<string> _remembered = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _rememberedDone = new HashSet<string>(StringComparer.Ordinal);
        private int _rememberedRetries;
        // Keeps every cloned surface referenced (ref count never drops to 0 → never re-read from a non-existent stream).
        private readonly List<SurfaceAsset> _surfaces = new List<SurfaceAsset>();
        private int _made, _failed, _warned, _noAlpha;
        private readonly SortedSet<int> _tiersMade = new SortedSet<int>();
        private bool _reportPending;

        public static string CloneName(string original, int tier) => NamePrefix + tier + "% (" + original + ")";

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            _prefabs = World.GetOrCreateSystemManaged<PrefabSystem>();
            LoadRemembered();
            Mod.Log.Info($"Placemaker decal opacity: see-through copies {(FeatureEnabled ? "on" : "off")}, {_remembered.Count} remembered from earlier builds.");
        }

        /// <summary>
        /// The ready see-through clone of a decal prefab for an opacity, else the original (and a request).
        /// <paramref name="why"/> says why the original is used ("" when the clone is used).
        /// </summary>
        public Entity CloneFor(Entity original, float opacity, out string why)
        {
            why = "";
            int tier = Core.Decals.DecalLook.Tier(opacity);
            if (tier >= 100) { why = "full-strength tier"; return original; }
            if (!FeatureEnabled) { why = "see-through decals off"; return original; }
            if (original == Entity.Null) { why = "no prefab"; return original; }
            try
            {
                if (_cloneOf.TryGetValue((original, tier), out var clone))
                {
                    if (IsReady(clone)) return clone;
                    why = "copy not ready yet";
                    return original;
                }
                if (!_prefabs.TryGetPrefab<StaticObjectPrefab>(original, out var prefab) || prefab == null) { why = "not a static object prefab"; return original; }
                if (prefab.name.StartsWith(NamePrefix, StringComparison.Ordinal)) return original;
                Request(original, tier);
                if (_remembered.Add(tier + "|" + prefab.GetType().Name + "|" + prefab.name)) SaveRemembered();
                why = _requested.Contains((original, tier)) && !_cloneOf.ContainsKey((original, tier)) ? "copy requested (first use)" : "copy failed";
            }
            catch (Exception e)
            {
                why = "lookup failed";
                if (_warned++ < 5) Mod.Log.Warn("Placemaker decal opacity lookup failed: " + e);
            }
            return original;
        }

        private void Request(Entity original, int tier)
        {
            if (_requested.Add((original, tier))) _queue.Add((original, tier));
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
                while (_queue.Count > 0 && budget-- > 0)
                {
                    var next = _queue[0];
                    _queue.RemoveAt(0);
                    if (CreateClone(next.original, next.tier, out string why)) { _made++; _tiersMade.Add(next.tier); }
                    else
                    {
                        _failed++;
                        if (_warned++ < 8) Mod.Log.Warn($"Placemaker decal opacity: no {next.tier}% copy of {Describe(next.original)}: {why}");
                    }
                    _reportPending = true;
                }
                if (_reportPending && _queue.Count == 0)
                {
                    _reportPending = false;
                    Mod.Log.Info($"Placemaker decal opacity: made {_made} see-through copies (tiers {string.Join("/", _tiersMade)} %), {_failed} failed"
                        + (_noAlpha > 0 ? $", {_noAlpha} materials had no _BaseColor (alpha added)" : "") + ".");
                }
            }
            catch (Exception e)
            {
                Mod.Log.Error(e, "Placemaker see-through decal copies failed; decals are built at full strength");
                FeatureEnabled = false;
            }
        }

        private string Describe(Entity e)
        {
            try { return _prefabs.TryGetPrefab<PrefabBase>(e, out var p) && p != null ? p.GetType().Name + " " + p.name : e.ToString(); }
            catch (Exception) { return e.ToString(); }
        }

        private void RequestRemembered()
        {
            _rememberedRetries++;
            bool all = true;
            foreach (var line in _remembered)
            {
                if (_rememberedDone.Contains(line)) continue;
                var parts = line.Split(new[] { '|' }, 3);
                if (parts.Length < 2 || !int.TryParse(parts[0], out int tier)) { _rememberedDone.Add(line); continue; }
                string type = parts.Length == 3 ? parts[1] : nameof(StaticObjectPrefab), name = parts[parts.Length - 1];
                if (_prefabs.TryGetPrefab(new PrefabID(type, name), out var p) && p != null && _prefabs.TryGetEntity(p, out var entity))
                {
                    Request(entity, tier);
                    _rememberedDone.Add(line);
                }
                else all = false;
            }
            if (all) _rememberedRetries = int.MaxValue;
        }

        private bool CreateClone(Entity originalEntity, int tier, out string why)
        {
            why = "";
            if (!_prefabs.TryGetPrefab<StaticObjectPrefab>(originalEntity, out var original) || original == null) { why = "prefab not found"; return false; }
            if (original.m_Meshes == null || original.m_Meshes.Length == 0) { why = "no meshes"; return false; }
            var type = original.GetType();
            var cloneName = CloneName(original.name, tier);
            if (_prefabs.TryGetPrefab(new PrefabID(type.Name, cloneName), out var existing) && existing != null && _prefabs.TryGetEntity(existing, out var existingEntity))
            {
                _cloneOf[(originalEntity, tier)] = existingEntity;
                if (!_registeredFrame.ContainsKey(existingEntity)) _registeredFrame[existingEntity] = UnityEngine.Time.frameCount;
                return true;
            }
            float opacity = tier / 100f;
            var meshes = new ObjectMeshInfo[original.m_Meshes.Length];
            int decalMeshes = 0;
            for (int i = 0; i < meshes.Length; i++)
            {
                var mi = original.m_Meshes[i];
                RenderPrefabBase mesh = mi?.m_Mesh;
                if (mi?.m_Mesh is RenderPrefab rp && rp.GetComponent<DecalProperties>() != null)
                {
                    var rc = CloneRender(rp, cloneName + " #" + i, opacity, out why);
                    if (rc == null) return false;   // keep the original; never half a clone
                    mesh = rc;
                    decalMeshes++;
                }
                meshes[i] = new ObjectMeshInfo { m_Mesh = mesh, m_Position = mi.m_Position, m_Rotation = mi.m_Rotation, m_RequireState = mi.m_RequireState };
            }
            if (decalMeshes == 0) { why = "no decal mesh (no DecalProperties)"; return false; }
            // CreateInstance of the original's own class + AddComponentFrom, never Object.Instantiate (shares ComponentBase instances — PLT).
            var clone = (StaticObjectPrefab)ScriptableObject.CreateInstance(type);
            clone.name = cloneName;
            clone.m_Meshes = meshes;
            clone.m_Circular = original.m_Circular;
            foreach (var component in original.components)
            {
                if (component == null || component is UIObject || component is ObsoleteIdentifiers) continue;
                clone.AddComponentFrom(component);
            }
            if (!_prefabs.AddPrefab(clone)) { UnityEngine.Object.Destroy(clone); why = "AddPrefab refused the object"; return false; }
            var entity = _prefabs.GetEntity(clone);
            _cloneOf[(originalEntity, tier)] = entity;
            _registeredFrame[entity] = UnityEngine.Time.frameCount;
            return true;
        }

        private RenderPrefab CloneRender(RenderPrefab rp, string name, float opacity, out string why)
        {
            why = "";
            var surfaces = new List<SurfaceAsset>();
            for (int s = 0; s < rp.materialCount; s++)
            {
                var src = rp.GetSurfaceAsset(s);
                if (src == null) { why = "render prefab " + rp.name + " has no surface " + s; return null; }
                src.LoadProperties(useVT: true);   // our own reference on the original: keeps its data loaded
                var path = AssetDataPath.Create("Placemaker/DecalOpacity", name + " " + s, EscapeStrategy.None);
                var copy = AssetDatabase.user.AddAsset<SurfaceAsset>(path, Colossal.Hash128.CreateGuid(name + " " + s));
                copy.CopyFrom(src);
                ApplyOpacity(copy, opacity);
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
            if (!_prefabs.AddPrefab(r)) { UnityEngine.Object.Destroy(r); why = "AddPrefab refused the render prefab"; return null; }
            return r;
        }

        /// <summary>Scales every opacity a decal material reads (see the header): _BaseColor.w (vector or colour), channel opacities.</summary>
        private void ApplyOpacity(SurfaceAsset copy, float opacity)
        {
            var vectors = new Dictionary<string, Vector4>(StringComparer.Ordinal);
            if (copy.vectors != null) foreach (var kv in copy.vectors) vectors[kv.Key] = kv.Value;
            var colors = new Dictionary<string, Color>(StringComparer.Ordinal);
            if (copy.colors != null) foreach (var kv in copy.colors) colors[kv.Key] = kv.Value;
            bool found = false;
            if (vectors.TryGetValue("_BaseColor", out var bv)) { bv.w *= opacity; vectors["_BaseColor"] = bv; found = true; }
            if (colors.TryGetValue("_BaseColor", out var bc)) { bc.a *= opacity; colors["_BaseColor"] = bc; found = true; }
            if (!found)
            {
                // Decal materials default _BaseColor to white; the material key is read as a vector by the decal graph.
                vectors["_BaseColor"] = new Vector4(1f, 1f, 1f, opacity);
                _noAlpha++;
            }
            copy.UpdateVectors(vectors);
            copy.UpdateColors(colors);

            var floats = new Dictionary<string, float>(StringComparer.Ordinal);
            foreach (var kv in copy.floats) floats[kv.Key] = kv.Value;
            foreach (var key in ChannelOpacities) floats[key] = (floats.TryGetValue(key, out var v) ? v : 1f) * opacity;
            foreach (var key in OtherOpacities) if (floats.TryGetValue(key, out var v)) floats[key] = v * opacity;
            copy.UpdateFloats(floats);
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
