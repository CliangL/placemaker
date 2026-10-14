// Paint mode (Grid Road Generator's brush): drag to paint, right-drag to erase, Shift + move the
// mouse to rotate a square brush. Size comes from the panel (1–1000 m). The largest painted
// island becomes the outline (holes filled — CS2 areas cannot have holes).
//
// s15: (1) "Paint: keep off buildings" (Options, default on) cuts building lots out of the outline
// (Core/Geometry/ObstacleCut; enclosed lots are opened with a 1 m corridor like Block paint). Off = the
// legacy outline; the build never touches buildings either way (Core/Build/SiteSafety). Buildings
// near the brush are shaded red while painting. (2) Performance: the outline (marching squares +
// smoothing + keep-off-roads booleans + attachment search) is rebuilt once on mouse-up instead of
// ~7×/s while dragging; a stamp only counts as a change when it changed cells.

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
        private float2 _lastStroke;

        public BrushMask Paint => _paint;
        public bool Painting => _gesture == Gesture.Paint || _gesture == Gesture.Erase;

        /// <summary>
        /// (s15) The brush takes input and its cursor / Block paint shading are drawn: Area stage, brush mode, tool
        /// active, not placing markers, mouse over the world — or a stroke still running (Core/Tool/BrushCursor).
        /// </summary>
        public bool BrushLive => BrushCursor.Live(IsActive, _stage, _mode, _markerKind != null, _in.OverWorld, Painting);

        /// <summary>Plain Paint cuts building lots out of its outline (Options, default on).</summary>
        public bool PaintAvoidsBuildings => _settings == null || _settings.PaintAvoidBuildings;

        private void UpdatePaint()
        {
            _cursorValid = _in.HasHit;
            if (_in.HasHit) _cursor = _in.Hit;
            float radius = _brushSize * 0.5f;

            if (_gesture == Gesture.Paint || _gesture == Gesture.Erase)
            {
                bool held = _gesture == Gesture.Paint ? _in.LeftHeld : _in.RightHeld;
                if (held)
                {
                    if (_in.HasHit)
                    {
                        int v = _paint.Version;
                        _paint.Stroke(_lastStroke, _in.Hit.xz, radius, _gesture == Gesture.Erase, _brushSquare, _brushAngle);
                        _lastStroke = _in.Hit.xz;
                        // Only a stroke that changed cells is an input change (holding still costs nothing).
                        if (_paint.Version != v) ShapeChanged(throttled: true);
                        if (PaintAvoidsBuildings) EnsurePaintBuildings(_in.Hit.xz, radius);
                    }
                    return;
                }
                var g = _gesture;
                _gesture = Gesture.None;
                ShapeChanged();
                Commit(g == Gesture.Erase ? "Placemaker.Undo.Erase" : "Placemaker.Undo.Paint");
                if (!_paint.IsEmpty && _shape == null)
                    Flash(new LocText(_paintOnlyBuildings ? "Placemaker.Problem.OnlyBuildings" : "Placemaker.Problem.PaintTooSmall"));
                return;
            }

            // Shift + mouse movement turns a square brush (the cursor stays where it is visually).
            if (_in.Shift && _brushSquare && !_in.LeftHeld && !_in.RightHeld)
            {
                if (math.abs(_in.MouseDelta.x) > 0.01f)
                {
                    _brushAngle = ((_brushAngle + _in.MouseDelta.x * 0.5f) % 360f + 360f) % 360f;
                    Touch();
                }
                return;
            }

            if (!_in.HasHit) return;
            if (_in.LeftPressed || _in.RightPressed)
            {
                _gesture = _in.LeftPressed ? Gesture.Paint : Gesture.Erase;
                _lastStroke = _in.Hit.xz;
                if (PaintAvoidsBuildings) EnsurePaintBuildings(_in.Hit.xz, radius, strokeStart: true);
                _paint.Stamp(_in.Hit.xz, radius, _gesture == Gesture.Erase, _brushSquare, _brushAngle);
                ShapeChanged(throttled: true);
            }
        }

        // ───────────── buildings for plain Paint (s15) ─────────────

        /// <summary>Building lots/footprints around the paint (gathered per stroke, not per frame).</summary>
        private List<float2[]> _paintBuildings = new List<float2[]>();
        private float2 _paintBuildingsMin, _paintBuildingsMax;
        private float _paintBuildingsAt = -100f;
        private int _paintBuildingsVersion;
        private bool _paintOnlyBuildings;

        /// <summary>Buildings gathered around the current paint (overlay: shaded while painting).</summary>
        public IReadOnlyList<float2[]> PaintBuildings => _paintBuildings;
        public int PaintBuildingsVersion => _paintBuildingsVersion;

        /// <summary>Re-reads buildings when the brush leaves the gathered box (or at a stroke start after 5 s).</summary>
        private void EnsurePaintBuildings(float2 at, float radius, bool strokeStart = false)
        {
            float now = UnityEngine.Time.realtimeSinceStartup;
            bool inside = math.all(at - radius >= _paintBuildingsMin) && math.all(at + radius <= _paintBuildingsMax);
            if (inside && !(strokeStart && now - _paintBuildingsAt > 5f)) return;
            float pad = math.max(150f, radius * 2f);
            float2 min = at - radius - pad, max = at + radius + pad;
            var o = _shape?.Ring;
            if (o != null && o.Length > 0)
            {
                // Keep covering what is already painted (the outline is cut against all of it).
                foreach (var p in o) { min = math.min(min, p.xz - 10f); max = math.max(max, p.xz + 10f); }
            }
            ReadPaintBuildings(min, max);
        }

        private void ReadPaintBuildings(float2 min, float2 max)
        {
            _bpObstacles ??= new BlockObstacles(EntityManager, World);
            _paintBuildings = _bpObstacles.BuildingsIn(min, max, _editingGroup);
            _paintBuildingsMin = min;
            _paintBuildingsMax = max;
            _paintBuildingsAt = UnityEngine.Time.realtimeSinceStartup;
            _paintBuildingsVersion++;
        }

        private AreaShape BuildPaintShape()
        {
            _paintOnlyBuildings = false;
            if (_paint.IsEmpty) return null;
            var outline = _paint.Outline();
            if (outline.Count < 3) return null;
            var ring2 = new float2[outline.Count];
            for (int i = 0; i < ring2.Length; i++) ring2[i] = outline[i].xz;
            if (PaintAvoidsBuildings)
            {
                // The gathered box must cover the whole outline (undo/redo restores paint without a stroke).
                Poly.Bounds(ring2, out var mn, out var mx);
                if (math.any(mn < _paintBuildingsMin) || math.any(mx > _paintBuildingsMax)) ReadPaintBuildings(mn - 10f, mx + 10f);
                var cut = ObstacleCut.Subtract(ring2, ObstacleCut.Grow(_paintBuildings, Placemaker.Core.Build.SiteSafety.Margin), out int touched, 100f);
                if (cut == null) { _paintOnlyBuildings = touched > 0; return null; }
                ring2 = cut;
            }
            // y = terrain height. Largest island, holes filled (CS2 areas cannot have holes).
            var ring = Poly.To3D(ring2, p => _site.SampleHeight(p));
            var shape = new AreaShape { Ring = ring, Source = AreaSource.Painted };
            WithFoundAttachments(shape);
            return shape;
        }
    }
}
