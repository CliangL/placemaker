// Placemaker — terraform brushes, lake water and zoning (GPL-3.0).
// Terraform + water adapted from ParkManager's ParkLakeBuild (tomkpunkt, GPL-3.0): the game's
// own level brush is re-emitted every frame for spots not yet at the target height, then a
// constant-level water source fills the basin. Zoning follows GridRoadGenerator's Zoning
// definitions (Ti4goc, GPL-3.0): ZoningFlags.Zone|Marquee without Overwrite never repaints cells
// that are already zoned.

using System;
using System.Collections.Generic;
using Colossal.Mathematics;
using Game.Common;
using Game.Prefabs;
using Game.Simulation;
using Game.Tools;
using Placemaker.Core;
using Placemaker.Core.Build;
using Placemaker.Game.Groups;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Placemaker.Game.Build
{
    public sealed partial class BuildService
    {
        private const float BrushRadius = 12f;
        private const float LevelTolerance = 0.3f;
        private const float BrushTime = 0.1f;
        private const int TerraformSettleFrames = 10;

        private struct Dab { public float2 P; public float Target; }

        private readonly List<Dab> _dabs = new List<Dab>();
        private readonly List<Entity> _brushDefinitions = new List<Entity>();
        private Entity _levelTool, _brushPrefab;
        private EntityQuery _allPrefabs;

        private void ResetTerraform()
        {
            _dabs.Clear();
            _brushDefinitions.Clear();
        }

        private void BeginTerraform()
        {
            _dabs.Clear();
            if (!ResolveTerraformTools())
            {
                _warnings.Add("Placemaker.Build.TerraformUnavailable");
                Go(Step.EmitNets);
                return;
            }
            foreach (var w in _work.Terraform)
            {
                var t = _request.Plan.Layers[w.Layer].Terraform[w.Index];
                if (!math.isfinite(t.TargetHeight)) continue;
                var ring = new float2[t.Ring.Length];
                for (int i = 0; i < ring.Length; i++) ring[i] = t.Ring[i].xz;
                foreach (var p in TerraformDabs.Dabs(ring, BrushRadius)) _dabs.Add(new Dab { P = p, Target = t.TargetHeight });
            }
            if (_dabs.Count == 0) Go(Step.EmitNets);
        }

        private void DoTerraform(int frame)
        {
            DiscardTerraformBrushes();
            _heightsValid = false;
            int elapsed = frame - _stepFrame;
            int pending = 0;
            foreach (var d in _dabs)
            {
                float h = Height(d.P);
                if (math.abs(h - d.Target) <= LevelTolerance) continue;
                pending++;
                EmitBrush(new float3(d.P.x, h, d.P.y), d.Target);
            }
            _status.Progress = 0.1f + 0.1f * (_dabs.Count == 0 ? 1f : 1f - pending / (float)_dabs.Count);
            bool done = pending == 0 && elapsed > TerraformSettleFrames;
            if (done || elapsed > TerraformTimeoutFrames)
            {
                DiscardTerraformBrushes();
                if (!done)
                {
                    _warnings.Add("Placemaker.Build.TerraformIncomplete");
                    Mod.Log.Warn($"Placemaker terraform timed out with {pending}/{_dabs.Count} spots off target.");
                }
                Go(Step.EmitNets);
                return;
            }
            if (pending > 0) _tool.Requested = ApplyMode.Apply;
        }

        private void EmitBrush(float3 position, float target)
        {
            var def = EntityManager.CreateEntity();
            EntityManager.AddComponentData(def, new CreationDefinition { m_Prefab = _brushPrefab });
            EntityManager.AddComponentData(def, new BrushDefinition
            {
                m_Tool = _levelTool,
                m_Line = new Line3.Segment(position, position),
                m_Size = BrushRadius * 2f,
                m_Angle = 0f,
                m_Strength = 1f,
                m_Time = BrushTime,
                m_Target = new float3(position.x, target, position.z),
                m_Start = position,
            });
            EntityManager.AddComponent<Updated>(def);
            _brushDefinitions.Add(def);
        }

        private void DiscardTerraformBrushes()
        {
            foreach (var d in _brushDefinitions)
                if (EntityManager.Exists(d) && !EntityManager.HasComponent<Deleted>(d)) EntityManager.AddComponent<Deleted>(d);
            _brushDefinitions.Clear();
        }

        /// <summary>Vanilla height-level terraform tool and a (preferably soft) brush.</summary>
        private bool ResolveTerraformTools()
        {
            if (_levelTool != Entity.Null && EntityManager.Exists(_levelTool) && _brushPrefab != Entity.Null && EntityManager.Exists(_brushPrefab)) return true;
            _levelTool = _brushPrefab = Entity.Null;
            if (_allPrefabs == default) _allPrefabs = GetEntityQuery(ComponentType.ReadOnly<PrefabData>());
            string brushName = "";
            using var prefabs = _allPrefabs.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < prefabs.Length; i++)
            {
                if (!_prefabs.TryGetPrefab<PrefabBase>(prefabs[i], out var prefab) || prefab == null) continue;
                if (prefab is TerraformingPrefab tf && tf.m_Type == TerraformingType.Level && tf.m_Target == TerraformingTarget.Height)
                {
                    if (_levelTool == Entity.Null) _levelTool = prefabs[i];
                }
                else if (prefab is BrushPrefab && (_brushPrefab == Entity.Null
                    || (prefab.name.IndexOf("soft", StringComparison.OrdinalIgnoreCase) >= 0 && brushName.IndexOf("soft", StringComparison.OrdinalIgnoreCase) < 0)))
                {
                    _brushPrefab = prefabs[i];
                    brushName = prefab.name;
                }
            }
            return _levelTool != Entity.Null && _brushPrefab != Entity.Null;
        }

        // ───────────────────────── after apply: water ─────────────────────────

        private void AfterApply()
        {
            if (_work == null) return;
            foreach (var w in _work.Terraform)
            {
                var t = _request.Plan.Layers[w.Layer].Terraform[w.Index];
                if (!math.isfinite(t.WaterLevel)) continue;
                try { CreateWaterSource(t); }
                catch (Exception e) { Mod.Log.Warn("Placemaker could not add lake water: " + e.Message); _warnings.Add("Placemaker.Build.NoWater"); }
            }
        }

        private void CreateWaterSource(PlannedTerraform t)
        {
            var ring = new float2[t.Ring.Length];
            for (int i = 0; i < ring.Length; i++) ring[i] = t.Ring[i].xz;
            var anchor = TerraformDabs.Anchor(ring, out float innerRadius);
            if (math.all(math.isfinite(t.WaterAnchor.xz)) && math.lengthsq(t.WaterAnchor.xz) > 0 && ReservedSpace.PointInPolygon(t.WaterAnchor.xz, ring))
                anchor = t.WaterAnchor.xz;
            _heightsValid = false;
            float bottom = Height(anchor);
            if (t.WaterLevel - bottom < 0.5f)
            {
                _warnings.Add("Placemaker.Build.NoWater");
                Mod.Log.Warn($"Placemaker lake basin not deep enough (ground {bottom:F2}, water {t.WaterLevel:F2}); no water added.");
                return;
            }
            bool legacy = _water.UseLegacyWaterSources;
            var position = new float3(anchor.x, bottom, anchor.y);
            // The legacy water system reads the level as an absolute elevation, the current one as
            // a height above the source position (PM measured both).
            var source = new global::Game.Simulation.WaterSourceData
            {
                m_ConstantDepth = legacy ? 1 : 0,
                m_Height = legacy ? t.WaterLevel : t.WaterLevel - bottom,
                m_Radius = math.max(5f, innerRadius * 0.9f),
                m_Polluted = 0f,
                m_Id = _water.GetNextSourceId(),
                m_Modifier = 1f,
            };
            // A multiplier of exactly 1 marks a radius too small for the simulation.
            source.m_Multiplier = WaterSystem.CalculateSourceMultiplier(source, position);
            for (int attempt = 0; attempt < 200 && source.m_Multiplier == 1f; attempt++)
            {
                source.m_Radius += 1f;
                source.m_Multiplier = WaterSystem.CalculateSourceMultiplier(source, position);
            }
            var e = EntityManager.CreateEntity();
            EntityManager.AddComponentData(e, source);
            EntityManager.AddComponentData(e, new global::Game.Objects.Transform { m_Position = position, m_Rotation = quaternion.identity });
            EntityManager.AddComponent<Created>(e);
            EntityManager.AddComponent<Updated>(e);
            EntityManager.AddComponentData(e, new PlacemakerMember { GroupId = _groupId, Kind = MemberKind.Auxiliary, Role = MemberRole.WaterSource });
        }

        // ───────────────────────── zoning ─────────────────────────

        private void DoZoningEmit()
        {
            int emitted = 0;
            foreach (var w in _work.Zoning)
            {
                var prefab = PrefabOf(w);
                if (prefab == Entity.Null || !EntityManager.HasComponent<ZoneData>(prefab)) continue;
                var z = _request.Plan.Layers[w.Layer].Zoning[w.Index];
                var def = NewDefinition();
                EntityManager.AddComponentData(def, new CreationDefinition { m_Prefab = prefab });
                EntityManager.AddComponentData(def, new Zoning
                {
                    m_Position = new Quad3(z.A, z.B, z.C, z.D),
                    // No ZoningFlags.Overwrite: cells the player already zoned are kept.
                    m_Flags = ZoningFlags.Zone | ZoningFlags.Marquee,
                });
                EntityManager.AddComponent<Updated>(def);
                NoteUsed(prefab);
                emitted++;
            }
            Go(emitted > 0 ? Step.ZoningApply : Step.Finish);
        }

        private void DoZoningApply(int frame)
        {
            int elapsed = frame - _stepFrame;
            if (elapsed == 2) _tool.Requested = ApplyMode.Apply;
            else if (elapsed >= 4)
            {
                DestroyOwnDefinitions();
                Go(Step.Finish);
            }
        }
    }
}
