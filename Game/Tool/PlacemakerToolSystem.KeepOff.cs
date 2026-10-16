// s27 (playtest: "it goes into the other parks I've already set up"): outlines of Draw / Roads (perimeter) /
// Block keep off other Placemaker builds — their owner lot outline, zone-block lots and receipt outline
// (Game/Selection/BlockObstacles) — and, with the brush rule "Draw: clip to free land", off every polygon stop
// the shared brush rules name (buildings, lots, zoning). The group being edited never counts. The brushes
// (Paint, Block paint) apply the same rules while painting (PlacemakerToolSystem.Paint / .BlockPaint).
// "Paint over anything" switches all of it off.

using System;
using System.Collections.Generic;
using Placemaker.Core;
using Placemaker.Core.Geometry;
using Placemaker.Core.Tool;
using Placemaker.Game.Selection;
using Unity.Mathematics;

namespace Placemaker.Game.Tool
{
    public sealed partial class PlacemakerToolSystem
    {
        /// <summary>Margin (m) kept from another build's outline (a hair, so the two lots never overlap).</summary>
        private const float BuildMargin = 0.5f;

        private List<BrushObstacle> _clipObstacles = new List<BrushObstacle>();
        private float2 _clipMin = new float2(float.MaxValue), _clipMax = new float2(float.MinValue);
        private float _clipAt = -100f;
        private int _clipGroup = -1;
        /// <summary>The whole outline lies on other builds / stops: no area (AreaProblem says why).</summary>
        private bool _clipCoveredAll;
        private List<float3[]> _clipShown = new List<float3[]>();
        private int _clipShownVersion;

        /// <summary>Other builds / stops the current outline was cut against (overlay: "won't paint" shading).</summary>
        public IReadOnlyList<float3[]> ClippedObstacles => _clipShown;
        public int ClippedObstaclesVersion => _clipShownVersion;

        private AreaShape KeepOffBuilds(AreaShape s)
        {
            _clipCoveredAll = false;
            var shown = new List<float3[]>();
            try
            {
                if (s?.Ring == null || s.Ring.Length < 3) return s;
                if (_mode != SelectionMode.Draw && _mode != SelectionMode.Block && _mode != SelectionMode.Perimeter) return s;
                var rules = BrushRules;
                Func<BrushObstacleKind, bool> only = rules.DrawClip
                    ? (Func<BrushObstacleKind, bool>)(k => k != BrushObstacleKind.Road && k != BrushObstacleKind.EmptyZoneCell)   // "Avoid zoning": every road has empty zone cells along it; only zoned cells, buildings, lots and builds count
                    : k => k == BrushObstacleKind.PlacemakerBuild;
                // s42: "Paint over anything" is the Paint area tool's Smart edges switch (off); the outline tools keep
                // their own Keep off chips whatever the brush does.
                if (rules.Anything)
                {
                    rules = rules.Clone();
                    rules.Anything = false;
                }

                var ring = s.Ring2D();
                Poly.Bounds(ring, out var mn, out var mx);
                // Draw rebuilds ~7×/s while a corner is dragged: one gather per region (and every 5 s), not per rebuild.
                float now = UnityEngine.Time.realtimeSinceStartup;
                if (math.any(mn < _clipMin) || math.any(mx > _clipMax) || now - _clipAt > 5f || _clipGroup != _editingGroup)
                {
                    _bpObstacles ??= new BlockObstacles(EntityManager, World);
                    _clipMin = mn - 100f;
                    _clipMax = mx + 100f;
                    _clipObstacles = _bpObstacles.Gather(_clipMin, _clipMax, _editingGroup);
                    _clipAt = now;
                    _clipGroup = _editingGroup;
                }
                var active = rules.Active(_clipObstacles, only);
                foreach (var o in active)
                    if (ObstacleCut.Overlaps(ring, o)) shown.Add(Poly.To3D(o, p => _site.SampleHeight(p)));
                if (shown.Count == 0) return s;

                var cut = rules.Cut(ring, _clipObstacles, out int touched, BuildMargin, 100f, only);
                if (cut == null)
                {
                    _clipCoveredAll = touched > 0;
                    return null;
                }
                if (ReferenceEquals(cut, ring)) return s;
                // Cut back: a block / road loop outline is no longer all road frontage.
                var shape = new AreaShape
                {
                    Ring = Poly.To3D(cut, p => _site.SampleHeight(p)),
                    Source = s.Source == AreaSource.Drawn ? AreaSource.Drawn : AreaSource.Painted,
                };
                WithFoundAttachments(shape);
                return shape;
            }
            catch (Exception e)
            {
                Mod.Log.Warn("Placemaker: keeping off other builds failed: " + e.Message);
                return s;
            }
            finally
            {
                if (shown.Count > 0 || _clipShown.Count > 0) { _clipShown = shown; _clipShownVersion++; }
            }
        }
    }
}
