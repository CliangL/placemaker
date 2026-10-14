// Placemaker — the owner lot of a build (US7) and clearing existing trees/props (US6) (GPL-3.0).
//
// Owner lot: a "Placemaker Area" lot (OwnerAreaPrefabSystem) is emitted over the build outline in the
// same transaction as everything else. It is tagged PlacemakerMember{Auxiliary, OwnerArea} and its
// entity is stored in the record (PlacemakerGroup.Owner). It is the ONE selectable thing: the lot is
// raycastable by the default tool and the bulldozer (AreaTypeMask.Lots), the RaycastLiftPatch lifts
// hits on any member to it, and bulldozing it cascades to the whole group (GroupRegistrySystem →
// GroupRemovalSystem, phase-correct). Members are deliberately NOT given an Owner:
//   * nets with an area owner lose zone blocks and vanilla upgrade/connection behaviour;
//   * surfaces/objects owned by a lot would be deleted with it when a save is loaded without the mod
//     (unknown prefab) — without Owner they degrade to plain city parts (RESEARCH-NOTES 4);
//   * objects owned by an area get scattered by SubObjectSystem.RelocateSubObjects (PLT measured).
// Parking bays keep their bare carrier (Owner+Attached), which LaneConnectionSystem needs.
//
// Clearing: trees, plants and loose props/decals inside the outline get vanilla delete definitions
// (exactly what BulldozeToolSystem.AddEntity emits for a top-level object), so they disappear on the
// same Apply as the build appears — never earlier, never without the build.

using System;
using System.Collections.Generic;
using Game.Common;
using Game.Objects;
using Game.Prefabs;
using Game.Tools;
using Placemaker.Core;
using Placemaker.Core.Build;
using Placemaker.Core.Geometry;
using Placemaker.Game.Groups;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Placemaker.Game.Build
{
    public sealed partial class BuildService
    {
        /// <summary>Setting "Clear existing trees and props" (default on).</summary>
        public static bool ClearExistingObjects = true;

        private const long OwnerKey = 1L << 61;
        private const int MaxClearedObjects = 20000;

        private OwnerAreaPrefabSystem _ownerPrefabs;
        private Entity _ownerPrefab;
        private bool _ownerSuppressed;     // the owner ring itself was rejected: build without it
        private Entity _ownerTemp;
        private int _cleared;
        private EntityQuery _clearQuery;

        private void InitOwner()
        {
            _ownerPrefabs = World.GetOrCreateSystemManaged<OwnerAreaPrefabSystem>();
            _clearQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<global::Game.Objects.Object>(), ComponentType.ReadOnly<Transform>(), ComponentType.ReadOnly<PrefabRef>() },
                Any = new[] { ComponentType.ReadOnly<Plant>(), ComponentType.ReadOnly<Static>() },
                None = new[]
                {
                    ComponentType.ReadOnly<Owner>(), ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Deleted>(),
                    ComponentType.ReadOnly<global::Game.Buildings.Building>(), ComponentType.ReadOnly<global::Game.Objects.UtilityObject>(),
                    ComponentType.ReadOnly<Attached>(), ComponentType.ReadOnly<global::Game.Routes.TransportStop>(),
                    ComponentType.ReadOnly<PlacemakerMember>(), ComponentType.ReadOnly<Secondary>(),
                },
            });
        }

        private void ResetOwner()
        {
            _ownerPrefab = Entity.Null;
            _ownerTemp = Entity.Null;
            _cleared = 0;
        }

        /// <summary>Outline of the build in XZ (drawn area, else hull of everything planned). Null if none.</summary>
        private float2[] BuildOutline()
        {
            var ring = _request.Area?.Ring;
            if (ring != null && ring.Length >= 3) return Poly.To2D(ring);
            var pts = new List<float2>();
            foreach (var l in _request.Plan.Layers)
            {
                foreach (var s in l.Surfaces) if (s?.Ring != null) foreach (var p in s.Ring) pts.Add(p.xz);
                foreach (var a in l.Areas) if (a?.Ring != null) foreach (var p in a.Ring) pts.Add(p.xz);
                foreach (var n in l.Nets) if (n != null) { pts.Add(n.A.xz); pts.Add(n.D.xz); }
                foreach (var o in l.Objects) if (o != null) pts.Add(o.Position.xz);
            }
            if (pts.Count < 3) return null;
            var hull = AreaRings.ConvexHull(pts);
            return hull.Length >= 3 ? hull : null;
        }

        /// <summary>Emits the owner lot definition (first EmitRest frame). No-op when unavailable.</summary>
        private void EmitOwnerArea()
        {
            if (_ownerSuppressed || _ownerPrefabs == null) return;
            _ownerPrefab = _ownerPrefabs.ReadyPrefab;
            if (_ownerPrefab == Entity.Null) return;
            var outline = BuildOutline();
            if (outline == null) return;
            var ring = AreaRings.MakeValid(outline, AreaRings.LotMinNodeDistance);
            if (ring == null)
            {
                Mod.Log.Warn("Placemaker: the build outline is not a valid lot shape; the build stays selectable per part.");
                return;
            }
            var ring3 = new float3[ring.Length];
            for (int i = 0; i < ring.Length; i++) ring3[i] = new float3(ring[i].x, 0f, ring[i].y);
            var w = new WorkItem { Kind = WorkKind.Area, Layer = -1, Index = -1, Category = BuildCategories.AreaLot };
            int areas = _emittedAreas;
            EmitAreaDefinition(_ownerPrefab, ring3, OwnerKey, w);
            _emittedAreas = areas;   // the owner lot is not a planned area (verification)
            _itemByKey.Remove(OwnerKey);   // never part of the census or exclusion: optional by design
        }

        /// <summary>Tags the materialised owner lot (called from TagTemporaries).</summary>
        private void AdoptOwnerTemp(Entity e)
        {
            _ownerTemp = e;
            _registry.EnsureOwnerBuffers(e);
            Tag(e, MemberKind.Auxiliary, MemberRole.OwnerArea);
            if (_record != Entity.Null && EntityManager.Exists(_record))
            {
                var g = EntityManager.GetComponentData<PlacemakerGroup>(_record);
                g.Owner = e;   // Apply converts the Temp in place, so the entity stays valid
                EntityManager.SetComponentData(_record, g);
            }
        }

        // ───────────────────────── clearing (US6) ─────────────────────────

        private void EmitClearDefinitions()
        {
            _cleared = 0;
            if (!ClearExistingObjects || _clearQuery == null || _clearQuery.IsEmptyIgnoreFilter) return;
            var outline = BuildOutline();
            if (outline == null) return;
            Poly.Bounds(outline, out var min, out var max);
            using var entities = _clearQuery.ToEntityArray(Allocator.Temp);
            using var transforms = _clearQuery.ToComponentDataArray<Transform>(Allocator.Temp);
            for (int i = 0; i < entities.Length && _cleared < MaxClearedObjects; i++)
            {
                var t = transforms[i];
                var p = t.m_Position.xz;
                if (p.x < min.x || p.y < min.y || p.x > max.x || p.y > max.y) continue;
                if (!Poly.Contains(outline, p)) continue;
                var e = entities[i];
                if (EntityManager.HasComponent<Elevation>(e)) continue;   // stacked/elevated props belong to something
                var def = NewDefinition();
                EntityManager.AddComponentData(def, new CreationDefinition { m_Original = e, m_Flags = CreationFlags.Delete });
                EntityManager.AddComponent<Updated>(def);
                EntityManager.AddComponentData(def, new ObjectDefinition
                {
                    m_Position = t.m_Position,
                    m_Rotation = t.m_Rotation,
                    m_LocalPosition = t.m_Position,
                    m_LocalRotation = t.m_Rotation,
                    m_ParentMesh = -1,
                    m_Probability = 100,
                    m_PrefabSubIndex = -1,
                });
                _cleared++;
            }
            if (_cleared > 0) Mod.Log.Info($"Placemaker clears {_cleared} existing trees/props inside the outline.");
        }
    }
}
