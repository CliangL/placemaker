// Placemaker — the runtime "Placemaker Area" lot prefab: the ONE selectable thing of a build (GPL-3.0).
// Model and every setting ported from ParkingLotTool's ParkingLotLotArea (KruemelGames, GPL-3.0),
// which measured them in game (save/load passed 2026-08-11):
//
//  * Lot type: the default tool and the bulldozer raycast AreaTypeMask.Lots (a Surface is never
//    clickable in the default tool, RESEARCH-NOTES 7). All vanilla lots are industry/extractor lots
//    with their own economy, so we register our own.
//  * m_AllowEditing MUST stay true: false sets GeometryFlags.HiddenIngame, and Areas.RaycastJobs
//    discards hidden areas outside the editor → invisible AND unclickable. Invisibility comes from
//    transparent AreaColorData instead; the selection colours stay visible so selection shows.
//  * m_AllowOverlap = true removes GeometryFlags.PhysicalGeometry: the lot never collides with our
//    own surfaces/areas or the city.
//  * No CanOverrideObjects on the lot: PLT measured props of neighbouring zoning vanishing with it.
//  * m_MaxRadius is only checked for lots owned by a Transform (ValidationHelpers): irrelevant here.
//
// Engine rules (RESEARCH-NOTES 1–3): AddPrefab in PrefabUpdate BEFORE PrefabInitializeSystem, never
// use it in the frame it was added, and register it unconditionally at startup so saves that
// reference "Placemaker Area" by name always resolve.
//
// Phase: PrefabUpdate, before Game.Prefabs.PrefabInitializeSystem.

using System;
using Game;
using Game.Prefabs;
using Unity.Entities;
using UnityEngine;
using UnityEngine.Scripting;

namespace Placemaker.Game.Groups
{
    public sealed partial class OwnerAreaPrefabSystem : GameSystemBase
    {
        public const string PrefabName = "Placemaker Area";
        private const int ReadyDelayFrames = 3;

        /// <summary>Setting: build an owner lot so the result is one selectable/bulldozable thing.</summary>
        public static bool FeatureEnabled = true;

        private PrefabSystem _prefabs;
        private Entity _prefab = Entity.Null;
        private int _registeredFrame = -1;
        private bool _failed;
        private bool _announced;

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            _prefabs = World.GetOrCreateSystemManaged<PrefabSystem>();
        }

        [Preserve]
        protected override void OnUpdate()
        {
            if (_prefab != Entity.Null || _failed) return;
            try { Register(); }
            catch (Exception e)
            {
                _failed = true;
                Mod.Log.Error(e, "Placemaker could not register the owner lot prefab; builds stay selectable per part");
            }
        }

        /// <summary>The owner lot prefab when it can be used (archetype built, type Lot), else Entity.Null.</summary>
        public Entity ReadyPrefab
        {
            get
            {
                if (!FeatureEnabled || _prefab == Entity.Null || UnityEngine.Time.frameCount - _registeredFrame < ReadyDelayFrames) return Entity.Null;
                if (!EntityManager.Exists(_prefab) || !EntityManager.HasComponent<AreaData>(_prefab)
                    || !EntityManager.GetComponentData<AreaData>(_prefab).m_Archetype.Valid
                    || !EntityManager.HasComponent<AreaGeometryData>(_prefab)) return Entity.Null;
                var geometry = EntityManager.GetComponentData<AreaGeometryData>(_prefab);
                if (geometry.m_Type != global::Game.Areas.AreaType.Lot) return Entity.Null;
                if (!_announced)
                {
                    _announced = true;
                    if ((geometry.m_Flags & global::Game.Areas.GeometryFlags.CanOverrideObjects) != 0)
                    {
                        geometry.m_Flags &= ~global::Game.Areas.GeometryFlags.CanOverrideObjects;
                        EntityManager.SetComponentData(_prefab, geometry);
                    }
                    Mod.Log.Info($"Placemaker owner lot '{PrefabName}' is ready (flags {geometry.m_Flags}).");
                }
                return _prefab;
            }
        }

        public bool IsOwnerPrefab(Entity prefab) => prefab != Entity.Null && prefab == _prefab;

        private void Register()
        {
            // After a world reload the prefab may already exist: just map it.
            if (_prefabs.TryGetPrefab(new PrefabID(nameof(LotPrefab), PrefabName), out var existing) && existing != null
                && _prefabs.TryGetEntity(existing, out var existingEntity))
            {
                _prefab = existingEntity;
                _registeredFrame = UnityEngine.Time.frameCount;
                return;
            }
            var lot = ScriptableObject.CreateInstance<LotPrefab>();
            lot.name = PrefabName;
            lot.m_MaxRadius = 0f;
            lot.m_AllowOverlap = true;
            lot.m_AllowEditing = true;
            lot.m_OnWater = false;
            lot.m_Color = new Color(0f, 0f, 0f, 0f);
            lot.m_EdgeColor = new Color(0f, 0f, 0f, 0f);
            lot.m_SelectionColor = new Color(1f, 1f, 1f, 0.12f);
            lot.m_SelectionEdgeColor = new Color(1f, 1f, 1f, 0.6f);
            lot.m_RangeColor = new Color(0f, 0f, 0f, 0f);
            if (!_prefabs.AddPrefab(lot))
            {
                _failed = true;
                UnityEngine.Object.Destroy(lot);
                Mod.Log.Warn($"Placemaker could not add the owner lot prefab '{PrefabName}'.");
                return;
            }
            _prefab = _prefabs.GetEntity(lot);
            _registeredFrame = UnityEngine.Time.frameCount;
            Mod.Log.Info($"Placemaker registered the owner lot prefab '{PrefabName}' (usable from the next frames).");
        }
    }
}
