// Block paint mode (the user's "paint, but only in the area it's currently in"): pressing the mouse
// picks the city block under the cursor (same face tracing as Block mode); the stroke then paints
// with the normal brush, but every stamp is fenced to that block's curb outline minus obstacles
// (zoned cells, building lots, other lot areas, water) — Core/Geometry/BlockBrush does the geometry.
//
// Gestures (same as Paint): drag = paint, right-drag = erase, plus Shift+drag = erase; Shift + move
// turns a square brush. A paint stroke that starts in a different block starts a new selection
// there (the old block is one undo step away): two blocks are always separated by a road, so their
// union could never be one outline anyway. Erase strokes never switch blocks.

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
        /// <summary>The block being painted, its brush fence and the obstacles shown as "won't paint here".</summary>
        public sealed class BlockPaintContext
        {
            public List<HalfEdge> Face;
            public float3[] Block;
            public BlockBrush Brush;
            public List<float3[]> Obstacles = new List<float3[]>();
            public bool EmptyZones;
            public int IgnoreGroup;
            public float BuiltAt;
        }

        /// <summary>Obstacles are re-read at most this often (s) between strokes: the city keeps growing.</summary>
        private const float BlockPaintRefresh = 5f;

        private readonly BrushMask _bpMask = new BrushMask();
        private List<HalfEdge> _bpFace = new List<HalfEdge>();
        private BlockPaintContext _bpCtx;
        private BlockObstacles _bpObstacles;
        private bool _bpEraseWithLeft;

        public BrushMask BlockPaintMask => _bpMask;
        public BlockPaintContext BlockPaintCurrent => _bpFace.Count > 0 && _bpCtx != null && BlockPaintState.SameFace(_bpCtx.Face, _bpFace) ? _bpCtx : null;

        private bool BlockPaintAvoidEmptyZones => _settings != null && _settings.BlockPaintAvoidEmptyZones;

        private void UpdateBlockPaint()
        {
            _cursorValid = _in.HasHit;
            if (_in.HasHit) _cursor = _in.Hit;
            float radius = _brushSize * 0.5f;

            if (_gesture == Gesture.Paint || _gesture == Gesture.Erase)
            {
                bool held = _gesture == Gesture.Paint || _bpEraseWithLeft ? _in.LeftHeld : _in.RightHeld;
                if (held)
                {
                    if (_in.HasHit)
                    {
                        _bpMask.Stroke(_lastStroke, _in.Hit.xz, radius, _gesture == Gesture.Erase, _brushSquare, _brushAngle);
                        _lastStroke = _in.Hit.xz;
                        ShapeChanged(throttled: true);
                    }
                    return;
                }
                var g = _gesture;
                _gesture = Gesture.None;
                ShapeChanged();
                Commit(g == Gesture.Erase ? "Placemaker.Undo.Erase" : "Placemaker.Undo.Paint");
                if (!_bpMask.IsEmpty && _shape == null) Flash(new LocText("Placemaker.Problem.PaintTooSmall"));
                return;
            }

            // Hover: the block a new stroke would paint in (only looked up when the cursor leaves it).
            if (_in.HasHit)
            {
                var cur = BlockPaintCurrent;
                bool inCurrent = cur?.Brush != null && cur.Brush.Contains(_in.Hit.xz);
                bool inHover = _hoverFace?.Area != null && Poly.Contains(_hoverFace.Area.Ring2D(), _in.Hit.xz);
                if (inCurrent) _hoverFace = null;
                else if (!inHover && math.distance(_in.Hit.xz, _hoverFaceAt) > 1f)
                {
                    _hoverFaceAt = _in.Hit.xz;
                    _hoverFace = PickFace(_in.Hit.xz, out _hoverFaceProblem);
                }
            }
            else _hoverFace = null;

            // Shift + mouse movement turns a square brush (as in Paint).
            if (_in.Shift && _brushSquare && !_in.LeftHeld && !_in.RightHeld)
            {
                if (math.abs(_in.MouseDelta.x) > 0.01f)
                {
                    _brushAngle = ((_brushAngle + _in.MouseDelta.x * 0.5f) % 360f + 360f) % 360f;
                    Touch();
                }
                return;
            }

            if (!_in.HasHit || !(_in.LeftPressed || _in.RightPressed)) return;
            bool erase = _in.RightPressed || _in.Shift;
            if (!erase && !StartBlockPaintStroke(_in.Hit.xz)) return;
            if (erase && _bpMask.IsEmpty) return;

            _gesture = erase ? Gesture.Erase : Gesture.Paint;
            _bpEraseWithLeft = erase && !_in.RightPressed;
            _lastStroke = _in.Hit.xz;
            _bpMask.Stamp(_in.Hit.xz, radius, erase, _brushSquare, _brushAngle);
            ShapeChanged(throttled: true);
        }

        /// <summary>Makes the block under p the painted one (a new selection if it is another block). False = cannot paint here.</summary>
        private bool StartBlockPaintStroke(float2 p)
        {
            var cur = EnsureBlockPaintContext(false);
            if (cur == null || !cur.Brush.Contains(p))
            {
                var face = _hoverFace != null && _hoverFace.Area != null && Poly.Contains(_hoverFace.Area.Ring2D(), p)
                    ? _hoverFace : PickFace(p, out _hoverFaceProblem);
                if (face == null) { Flash(_hoverFaceProblem ?? new LocText("Placemaker.Problem.NoBlockHere")); return false; }
                bool switched = !BlockPaintState.SameFace(face.Loop, _bpFace);
                if (switched)
                {
                    _bpMask.Clear();
                    _bpFace = new List<HalfEdge>(face.Loop);
                }
                _hoverFace = null;
                cur = EnsureBlockPaintContext(true);
                LocText refused = cur == null ? new LocText("Placemaker.Problem.NoBlockHere")
                                : cur.Brush.Full ? new LocText("Placemaker.Problem.BlockFull") : null;
                if (refused != null)
                {
                    // Nothing was painted: go back to the committed selection (the previous block).
                    if (switched && _history.Current != null) RestoreSelectionOnly(_history.Current);
                    Flash(refused);
                    return false;
                }
            }
            else if (UnityEngine.Time.realtimeSinceStartup - cur.BuiltAt > BlockPaintRefresh) cur = EnsureBlockPaintContext(true) ?? cur;

            if (cur.Brush.Full) { Flash(new LocText("Placemaker.Problem.BlockFull")); return false; }
            cur.Brush.Attach(_bpMask);
            return true;
        }

        /// <summary>
        /// Block outline + obstacles for the current face; rebuilt when the face, the zoning option or
        /// the edited group changed (or when forced). Null when there is no face or it can't be traced.
        /// </summary>
        private BlockPaintContext EnsureBlockPaintContext(bool refresh)
        {
            if (_bpFace.Count == 0) return null;
            bool emptyZones = BlockPaintAvoidEmptyZones;
            var c = _bpCtx;
            if (!refresh && c != null && BlockPaintState.SameFace(c.Face, _bpFace) && c.EmptyZones == emptyZones && c.IgnoreGroup == _editingGroup)
            {
                if (_bpMask.Limit != c.Brush.Allowed) c.Brush.Attach(_bpMask);
                return c;
            }
            AreaShape area;
            try { area = _roads.BuildArea(_bpFace, AreaSource.Painted); }
            catch (Exception e) { Mod.Log.Warn("Placemaker: block paint outline failed: " + e.Message); area = null; }
            if (area == null || area.Ring.Length < 3) return null;

            var ring = area.Ring2D();
            Poly.Bounds(ring, out var min, out var max);
            _bpObstacles ??= new BlockObstacles(EntityManager, World);
            var obstacles = _bpObstacles.Gather(min - 1f, max + 1f, emptyZones, _editingGroup);
            var water = _bpObstacles.Water(min, max, _bpMask.CellSize);
            BlockBrush brush;
            try { brush = new BlockBrush(ring, obstacles, _bpMask.CellSize, water); }
            catch (Exception e) { Mod.Log.Warn("Placemaker: block paint fence failed: " + e.Message); return null; }

            c = new BlockPaintContext
            {
                Face = new List<HalfEdge>(_bpFace),
                Block = area.Ring,
                Brush = brush,
                EmptyZones = emptyZones,
                IgnoreGroup = _editingGroup,
                BuiltAt = UnityEngine.Time.realtimeSinceStartup,
            };
            // Shown as "won't paint here": only what lies in this block (zone cells across the road are noise).
            foreach (var o in brush.Obstacles)
                if (ShowsInBlock(o, ring)) c.Obstacles.Add(Poly.To3D(o, q => _site.SampleHeight(q)));
            _bpCtx = c;
            brush.Attach(_bpMask);
            return c;
        }

        private static bool ShowsInBlock(float2[] obstacle, float2[] block)
        {
            if (Poly.Contains(block, Poly.Centroid(obstacle))) return true;
            foreach (var p in obstacle) if (Poly.Contains(block, p) && Poly.DistanceToBoundary(block, p) > 0.5f) return true;
            return false;
        }

        /// <summary>The current stroke erases (overlay colours the brush accordingly).</summary>
        public bool Erasing => _gesture == Gesture.Erase;

        private BrushMask.Snapshot _bpSnap;
        private int _bpSnapVersion = -1;

        private BlockPaintState BlockPaintSnapshot()
        {
            BrushMask.Snapshot mask = null;
            if (!_bpMask.IsEmpty)
            {
                if (_bpSnap == null || _bpSnapVersion != _bpMask.Version)
                {
                    _bpSnap = _bpMask.Save();
                    _bpSnapVersion = _bpMask.Version;
                }
                mask = _bpSnap;
            }
            return new BlockPaintState { Face = new List<HalfEdge>(_bpFace), Mask = mask };
        }

        private void RestoreBlockPaint(BlockPaintState s)
        {
            if (s?.Mask != null) _bpMask.Restore(s.Mask); else _bpMask.Clear();
            _bpSnap = s?.Mask;
            _bpSnapVersion = _bpMask.Version;
            _bpFace = s?.Face != null ? new List<HalfEdge>(s.Face) : new List<HalfEdge>();
        }

        private void ClearBlockPaint()
        {
            _bpMask.Clear();
            _bpFace = new List<HalfEdge>();
        }

        private AreaShape BuildBlockPaintShape()
        {
            if (_bpMask.IsEmpty || _bpFace.Count == 0) return null;
            var ctx = EnsureBlockPaintContext(false);
            if (ctx == null) return null;
            var o = ctx.Brush.Outline(_bpMask);
            if (o?.Ring == null) return null;
            var shape = new AreaShape { Ring = Poly.To3D(o.Ring, p => _site.SampleHeight(p)), Source = AreaSource.Painted };
            WithFoundAttachments(shape);
            return shape;
        }
    }
}
