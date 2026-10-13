// Marker placement (entrances, access points, split lines…): click on the outline to place a
// marker of the requested kind for one recipe step, right-click near one to remove it, Esc or
// the main button when done. Markers live in the recipe step (so they are undoable with the
// design) and are re-projected onto each region's outline by the composer.

using Placemaker.Core;
using Placemaker.Core.Tool;
using Unity.Mathematics;

namespace Placemaker.Game.Tool
{
    public sealed partial class PlacemakerToolSystem
    {
        private bool _markerHoverValid;
        private float3 _markerHover;
        private int _markerHoverSegment;
        private float _markerHoverT;

        public bool MarkerHoverValid => _markerHoverValid;
        public float3 MarkerHover => _markerHover;

        /// <summary>UI or primary button: start placing markers of <paramref name="kind"/> for the step at <paramref name="path"/>.</summary>
        public void StartMarkers(string path, string kind)
        {
            if (_shape == null || string.IsNullOrEmpty(kind) || RecipePaths.Resolve(_recipe, path) == null) return;
            CancelGesture();
            _markerPath = path;
            _markerKind = kind;
            _focus = path;
            if (_stage != ToolStage.Design && _stage != ToolStage.Review) SetStage(ToolStage.Design);
            Touch();
        }

        public void ExitMarkerMode()
        {
            if (_markerKind == null) return;
            _markerKind = null;
            _markerPath = null;
            _markerHoverValid = false;
            Touch();
        }

        public void ClearMarkers(string path, string kind)
        {
            if (_recipe == null) return;
            if (_editor.ClearMarkers(_recipe, path, kind))
            {
                DesignChanged();
                Commit("Placemaker.Undo.ClearMarkers");
            }
        }

        private void UpdateMarkers()
        {
            _markerHoverValid = false;
            _cursorValid = _in.HasHit;
            if (!_in.HasHit || _shape == null) return;
            _cursor = _in.Hit;
            float radius = HandleRadius(_in.Hit) * 2f;
            if (Composer.ClosestSegment(_shape.Ring, _in.Hit.xz, out int seg, out float t, out float d) && d <= radius)
            {
                _markerHoverValid = true;
                _markerHoverSegment = seg;
                _markerHoverT = t;
                _markerHover = math.lerp(_shape.Ring[seg], _shape.Ring[(seg + 1) % _shape.Ring.Length], t);
            }
            if (_in.LeftPressed && _markerHoverValid)
            {
                var m = new EdgeMarker { Kind = _markerKind, RingSegment = _markerHoverSegment, RingT = _markerHoverT, Position = _markerHover };
                if (_editor.AddMarker(_recipe, _markerPath, m))
                {
                    DesignChanged();
                    Commit("Placemaker.Undo.AddMarker");
                }
                return;
            }
            if (_in.RightPressed)
            {
                if (_editor.RemoveMarkerNear(_recipe, _markerPath, _in.Hit.xz, radius, _markerKind))
                {
                    DesignChanged();
                    Commit("Placemaker.Undo.RemoveMarker");
                }
            }
        }
    }
}
