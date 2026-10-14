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

        private struct PaintDab { public Entity Tool; public float3 At; public float Strength; }
        private readonly List<PaintDab> _paintDabs = new List<PaintDab>();
        private int _paintCursor;
        // Frame budget for texture painting (a big area can need thousands of dabs).
        private const int MaxPaintDabsPerFrame = 96;

        /// <summary>Collect every paint dab once, then emit them in frame-budgeted waves (apply each wave).</summary>
        private void DoPaint(int frame)
        {
            if (frame == _stepFrame)
            {
                DiscardTerraformBrushes();
                _paintDabs.Clear();
                _paintCursor = 0;
                if (!ResolveTerraformTools() || !ResolvePaintTools())
                {
                    _warnings.Add("Placemaker.Build.PaintUnavailable");
                    Mod.Log.Warn("Placemaker: no terrain material brush in this game; texture painting skipped.");
                    Go(Step.EmitNets);
                    return;
                }
                _heightsValid = false;
                foreach (var l in _request.Plan.Layers)
                {
                    if (l?.Paint == null) continue;
                    foreach (var paint in l.Paint)
                    {
                        if (paint?.Ring == null || paint.Ring.Length < 3) continue;
                        if (!_paintTools.TryGetValue(math.clamp(paint.Layer, 0, 4), out var tool)) continue;
                        var ring = new float2[paint.Ring.Length];
                        for (int i = 0; i < ring.Length; i++) ring[i] = paint.Ring[i].xz;
                        foreach (var q in TerraformDabs.Dabs(ring, PaintBrushRadius))
                            _paintDabs.Add(new PaintDab { Tool = tool, At = new float3(q.x, Height(q), q.y), Strength = math.saturate(paint.Strength) });
                    }
                }
                if (_paintDabs.Count == 0) { Go(Step.EmitNets); return; }
                Mod.Log.Info($"Placemaker paints the terrain with {_paintDabs.Count} brush dabs.");
            }
            // One wave per two frames: emit + apply, then discard on the next frame.
            if (((frame - _stepFrame) & 1) == 1) { DiscardTerraformBrushes(); return; }
            if (_paintCursor >= _paintDabs.Count)
            {
                DiscardTerraformBrushes();
                _paintDabs.Clear();
                Go(Step.EmitNets);
                return;
            }
            int end = Math.Min(_paintDabs.Count, _paintCursor + MaxPaintDabsPerFrame);
            for (int i = _paintCursor; i < end; i++) EmitPaintBrush(_paintDabs[i].Tool, _paintDabs[i].At, _paintDabs[i].Strength);
            _paintCursor = end;
            _tool.Requested = ApplyMode.Apply;
            _status.Progress = 0.18f + 0.02f * (_paintCursor / (float)_paintDabs.Count);
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
