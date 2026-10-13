// Paint mode (Grid Road Generator's brush): drag to paint, right-drag to erase, Shift + move the
// mouse to rotate a square brush. Size comes from the panel (1–1000 m). The largest painted
// island becomes the outline (holes filled — CS2 areas cannot have holes).

using Placemaker.Core;
using Placemaker.Core.Geometry;
using Placemaker.Core.Tool;
using Unity.Mathematics;

namespace Placemaker.Game.Tool
{
    public sealed partial class PlacemakerToolSystem
    {
        private float2 _lastStroke;

        public PaintMask Paint => _paint;
        public bool Painting => _gesture == Gesture.Paint || _gesture == Gesture.Erase;

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
                        _paint.Stroke(_lastStroke, _in.Hit.xz, radius, _gesture == Gesture.Erase, _brushSquare, _brushAngle);
                        _lastStroke = _in.Hit.xz;
                        ShapeChanged(throttled: true);
                    }
                    return;
                }
                var g = _gesture;
                _gesture = Gesture.None;
                ShapeChanged();
                Commit(g == Gesture.Erase ? "Placemaker.Undo.Erase" : "Placemaker.Undo.Paint");
                if (!_paint.IsEmpty && _shape == null) Flash(new LocText("Placemaker.Problem.PaintTooSmall"));
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
                _paint.Stamp(_in.Hit.xz, radius, _gesture == Gesture.Erase, _brushSquare, _brushAngle);
                ShapeChanged(throttled: true);
            }
        }

        private AreaShape BuildPaintShape()
        {
            if (_paint.IsEmpty) return null;
            var outline = _paint.Outline();
            if (outline.Count < 3) return null;
            var ring = Poly.To3D(outline, p => _site.SampleHeight(p));
            return new AreaShape { Ring = ring, Source = AreaSource.Painted };
        }
    }
}
