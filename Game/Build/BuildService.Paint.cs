// Placemaker — terrain texture painting (PlannedPaint) (GPL-3.0).
//
// CS2 (decompiled 1.6.2f1): a TerraformingPrefab with m_Target = TerraformingTarget.Material and a
// TerrainMaterialProperties component (m_Type = Extra1..Extra4, None = erase) is the terrain material
// brush. A BrushDefinition with that tool → GenerateBrushesSystem → Temp Brush (for Material the
// strength is passed through unchanged) → on Apply, ApplyBrushesSystem calls
// TerrainMaterialSystem.ApplyBrush, which blits the BrushPrefab texture into the user splatmaps
// (4096² playable + 1024² world, R8G8) that TerrainMaterialSystem serializes with the save. The four
// extra layers are textured by the map's terrain material (colossal_TerrainExtra1..4Diffuse), so the
// look per layer is map dependent; the base grass/dirt/rock stays procedural (height/slope).
//
// The build paints in one Apply frame right after terraform (before nets), with the same invisible
// build tool, so it never mixes with other temporaries. Painting is permanent (not undone on removal).

using System;
using System.Collections.Generic;
using Colossal.Mathematics;
using Game.Common;
using Game.Prefabs;
using Game.Tools;
using Placemaker.Core;
using Placemaker.Core.Build;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Placemaker.Game.Build
{
    public sealed partial class BuildService
    {
        private const float PaintBrushRadius = 6f;
        private readonly Dictionary<int, Entity> _paintTools = new Dictionary<int, Entity>();

        private bool HasPaint()
        {
            var plan = _request?.Plan;
            if (plan == null) return false;
            foreach (var l in plan.Layers) if (l?.Paint != null && l.Paint.Count > 0) return true;
            return false;
        }

        private Step AfterTerraform() => HasPaint() ? Step.Paint : Step.EmitNets;

        /// <summary>Frame 0: emit every paint dab and request Apply. Frame 2: drop the brushes, go on.</summary>
        private void DoPaint(int frame)
        {
            if (frame == _stepFrame)
            {
                DiscardTerraformBrushes();
                if (!ResolveTerraformTools() || !ResolvePaintTools())
                {
                    _warnings.Add("Placemaker.Build.PaintUnavailable");
                    Mod.Log.Warn("Placemaker: no terrain material brush in this game; texture painting skipped.");
                    Go(Step.EmitNets);
                    return;
                }
                _heightsValid = false;
                int dabs = 0;
                foreach (var l in _request.Plan.Layers)
                {
                    if (l?.Paint == null) continue;
                    foreach (var paint in l.Paint)
                    {
                        if (paint?.Ring == null || paint.Ring.Length < 3) continue;
                        if (!_paintTools.TryGetValue(math.clamp(paint.Layer, 0, 4), out var tool)) continue;
                        var ring = new float2[paint.Ring.Length];
                        for (int i = 0; i < ring.Length; i++) ring[i] = paint.Ring[i].xz;
                        foreach (var p in TerraformDabs.Dabs(ring, PaintBrushRadius))
                        {
                            var at = new float3(p.x, Height(p), p.y);
                            EmitPaintBrush(tool, at, math.saturate(paint.Strength));
                            dabs++;
                        }
                    }
                }
                if (dabs == 0) { Go(Step.EmitNets); return; }
                _tool.Requested = ApplyMode.Apply;
                _status.Progress = 0.18f;
                Mod.Log.Info($"Placemaker paints the terrain with {dabs} brush dabs.");
                return;
            }
            if (frame - _stepFrame < 2) return;
            DiscardTerraformBrushes();
            Go(Step.EmitNets);
        }

        private void EmitPaintBrush(Entity tool, float3 position, float strength)
        {
            var def = EntityManager.CreateEntity();
            EntityManager.AddComponentData(def, new CreationDefinition { m_Prefab = _brushPrefab });
            EntityManager.AddComponentData(def, new BrushDefinition
            {
                m_Tool = tool,
                m_Line = new Line3.Segment(position, position),
                m_Size = PaintBrushRadius * 2f,
                m_Angle = 0f,
                m_Strength = strength,
                m_Time = BrushTime,
                m_Target = position,
                m_Start = position,
            });
            EntityManager.AddComponent<Updated>(def);
            _brushDefinitions.Add(def);
        }

        /// <summary>Material brushes by layer: 0 = erase (TerrainMaterialType.None), 1..4 = Extra1..Extra4.</summary>
        private bool ResolvePaintTools()
        {
            if (_paintTools.Count > 0)
            {
                bool valid = true;
                foreach (var e in _paintTools.Values) if (!EntityManager.Exists(e)) valid = false;
                if (valid) return true;
                _paintTools.Clear();
            }
            if (_allPrefabs == default) _allPrefabs = GetEntityQuery(ComponentType.ReadOnly<PrefabData>());
            using var prefabs = _allPrefabs.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < prefabs.Length; i++)
            {
                if (!_prefabs.TryGetPrefab<PrefabBase>(prefabs[i], out var prefab) || !(prefab is TerraformingPrefab tf)) continue;
                if (tf.m_Target != TerraformingTarget.Material || !tf.TryGet<TerrainMaterialProperties>(out var mat) || mat == null) continue;
                int layer;
                switch (mat.m_Type)
                {
                    case TerrainMaterialType.None: layer = 0; break;
                    case TerrainMaterialType.Extra1: layer = 1; break;
                    case TerrainMaterialType.Extra2: layer = 2; break;
                    case TerrainMaterialType.Extra3: layer = 3; break;
                    case TerrainMaterialType.Extra4: layer = 4; break;
                    default: continue;
                }
                if (!_paintTools.ContainsKey(layer)) _paintTools[layer] = prefabs[i];
            }
            if (_paintTools.Count > 0)
                Mod.Log.Info($"Placemaker terrain paint layers available: {string.Join(", ", _paintTools.Keys)}.");
            return _paintTools.Count > 0;
        }
    }
}
