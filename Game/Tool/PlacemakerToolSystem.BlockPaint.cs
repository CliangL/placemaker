// Block paint mode (the user's "paint, but only in the area it's currently in"): pressing the mouse
// picks the city block under the cursor (same face tracing as Block mode); the stroke then paints
// with the normal brush, but every stamp is fenced to that block's curb outline minus obstacles
// (zoned cells, building lots, other lot areas, water) — Core/Geometry/BlockBrush does the geometry.
//
// No closed block under the cursor (open land, a dead-end road, an unfinished grid, a road ring that
// does not close): the mode still works — a work box around the stroke start where the real road
// footprints (centre curve ± half composition width, i.e. up to the curb) are obstacles too, fenced
// to the side of the roads the stroke started on, so it never hops over a road.
//
// Gestures (same as Paint): drag = paint, right-drag = erase, plus Shift+drag = erase; Shift + move
// turns a square brush. A paint stroke that starts outside the current block / road side starts a
// new selection there (the old one is one undo step away): across a road the union could never be
// one outline anyway. Erase strokes never switch.

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
        /// <summary>The block (or open-land side) being painted, its brush fence and the obstacles shown as "won't paint here".</summary>
        public sealed class BlockPaintContext
        {
            public List<HalfEdge> Face;
            public bool Open;
            public float2 Seed;
            /// <summary>Curb outline of the block; null in the open-land fallback.</summary>
            public float3[] Block;
            public BlockBrush Brush;
            public List<float3[]> Obstacles = new List<float3[]>();
            public BrushRules Rules;
            public int IgnoreGroup;
            public float BuiltAt;
        }

        /// <summary>Obstacles are re-read at most this often (s) between strokes: the city keeps growing.</summary>
        private const float BlockPaintRefresh = 5f;

        private readonly BrushMask _bpMask = new BrushMask();
        private List<HalfEdge> _bpFace = new List<HalfEdge>();
        private bool _bpOpen;
        private float2 _bpSeed;
        private BlockPaintContext _bpCtx;
        private BlockObstacles _bpObstacles;
        private bool _bpEraseWithLeft;

        public BrushMask BlockPaintMask => _bpMask;
        private bool BlockPaintHasTarget => _bpOpen || _bpFace.Count > 0;
        public BlockPaintContext BlockPaintCurrent => _bpCtx != null && IsCurrentTarget(_bpCtx) ? _bpCtx : null;

        private bool IsCurrentTarget(BlockPaintContext c) =>
            _bpOpen ? c.Open && math.all(c.Seed == _bpSeed) : !c.Open && BlockPaintState.SameFace(c.Face, _bpFace);

        /// <summary>The player's brush rules (Options / the panel's Rules row); plain Paint never reads them.</summary>
        public BrushRules BrushRules => _settings != null ? _settings.GetBrushRules() : BrushRules.Defaults();

        public void SetBrushRules(BrushRules rules)
        {
            if (rules == null) return;
            rules = rules.Normalized();
            if (rules.Equals(BrushRules)) return;
            if (_settings != null) { _settings.SetBrushRules(rules); _settings.ApplyAndSave(); }
            // The fence and the outline depend on the rules: rebuild them (not an undo step — it is a setting).
            if (_mode == SelectionMode.BlockPaint && !_bpMask.IsEmpty) ShapeChanged();
            Touch();
        }

        public void ResetBrushRules() => SetBrushRules(BrushRules.Defaults());

        /// <summary>A new stroke at p continues the current selection (same block / same side of the roads).</summary>
        private static bool Continues(BlockPaintContext c, float2 p) =>
            c?.Brush != null && (c.Open ? c.Brush.CanPaintAt(p) : c.Brush.Contains(p));

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
                        int v = _bpMask.Version;
                        _bpMask.Stroke(_lastStroke, _in.Hit.xz, radius, _gesture == Gesture.Erase, _brushSquare, _brushAngle);
                        _lastStroke = _in.Hit.xz;
                        // s15: only a stroke that changed cells is an input change; the fenced outline
                        // (booleans + offsets) is rebuilt on mouse-up, not while dragging.
                        if (_bpMask.Version != v) ShapeChanged(throttled: true);
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
            if (_in.HasHit && BrushRules.LockOn)
            {
                bool inCurrent = Continues(BlockPaintCurrent, _in.Hit.xz);
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

        /// <summary>
        /// Makes the block (or, without a closed block, the road side) under p the painted one — a new
        /// selection if it is another one. False = nothing can be painted here.
        /// </summary>
        private bool StartBlockPaintStroke(float2 p)
        {
            var cur = EnsureBlockPaintContext(false);
            if (!Continues(cur, p))
            {
                // "Stay in one block" off: never lock to a block, only the stops limit the brush.
                var face = !BrushRules.LockOn ? null
                    : _hoverFace != null && _hoverFace.Area != null && Poly.Contains(_hoverFace.Area.Ring2D(), p) ? _hoverFace
                    : PickFace(p, out _hoverFaceProblem);
                bool switched;
                if (face != null)
                {
                    switched = _bpOpen || !BlockPaintState.SameFace(face.Loop, _bpFace);
                    if (switched) { _bpMask.Clear(); _bpFace = new List<HalfEdge>(face.Loop); _bpOpen = false; }
                }
                else
                {
                    // Not inside a closed block: free painting fenced by road footprints, on this side of them.
                    switched = true;
                    _bpMask.Clear();
                    _bpFace = new List<HalfEdge>();
                    _bpOpen = true;
                    _bpSeed = p;
                }
                _hoverFace = null;
                cur = EnsureBlockPaintContext(true);
                LocText refused = cur == null ? new LocText("Placemaker.Problem.NoBlockHere")
                                : cur.Brush.Full ? new LocText(cur.Open ? "Placemaker.Problem.CannotPaintHere" : "Placemaker.Problem.BlockFull") : null;
                if (refused != null)
                {
                    // Nothing was painted: go back to the committed selection.
                    if (switched && _history.Current != null) RestoreSelectionOnly(_history.Current);
                    Flash(refused);
                    return false;
                }
            }
            else if (UnityEngine.Time.realtimeSinceStartup - cur.BuiltAt > BlockPaintRefresh) cur = EnsureBlockPaintContext(true) ?? cur;

            if (cur.Brush.Full) { Flash(new LocText(cur.Open ? "Placemaker.Problem.CannotPaintHere" : "Placemaker.Problem.BlockFull")); return false; }
            cur.Brush.Attach(_bpMask);
            return true;
        }

        /// <summary>
        /// Fence + obstacles for the current target; rebuilt when the target, the zoning option or the
        /// edited group changed (or when forced). Null when there is no target or it can't be traced.
        /// </summary>
        private BlockPaintContext EnsureBlockPaintContext(bool refresh)
        {
            if (!BlockPaintHasTarget) return null;
            var rules = BrushRules;
            var c = _bpCtx;
            if (!refresh && c != null && IsCurrentTarget(c) && rules.Equals(c.Rules) && c.IgnoreGroup == _editingGroup)
            {
                if (_bpMask.Limit != c.Brush.Allowed) c.Brush.Attach(_bpMask);
                return c;
            }
            _bpObstacles ??= new BlockObstacles(EntityManager, World);
            c = _bpOpen ? BuildOpenContext(rules) : BuildBlockContext(rules);
            if (c == null) return null;
            c.Rules = rules;
            c.IgnoreGroup = _editingGroup;
            c.BuiltAt = UnityEngine.Time.realtimeSinceStartup;
            _bpCtx = c;
            c.Brush.Attach(_bpMask);
            return c;
        }

        private BlockPaintContext BuildBlockContext(BrushRules rules)
        {
            AreaShape area;
            try { area = _roads.BuildArea(_bpFace, AreaSource.Painted); }
            catch (Exception e) { Mod.Log.Warn("Placemaker: block paint outline failed: " + e.Message); area = null; }
            if (area == null || area.Ring.Length < 3) return null;

            var ring = area.Ring2D();
            Poly.Bounds(ring, out var min, out var max);
            var obstacles = _bpObstacles.Gather(min - 1f, max + 1f, _editingGroup);
            var water = rules.WaterOn ? _bpObstacles.Water(min, max, _bpMask.CellSize) : null;
            // A block target was chosen with the lock on: keep it a block even if the lock is switched off since.
            var blockRules = rules.Clone();
            blockRules.StayInBlock = true;
            BlockBrush brush;
            try { brush = blockRules.Build(Poly.Centroid(ring), ring, obstacles, water, _bpMask.CellSize); }
            catch (Exception e) { Mod.Log.Warn("Placemaker: block paint fence failed: " + e.Message); return null; }

            var c = new BlockPaintContext { Face = new List<HalfEdge>(_bpFace), Block = area.Ring, Brush = brush };
            // Shown as "won't paint here": only what lies in this block (zone cells across the road are noise).
            foreach (var o in brush.Obstacles)
                if (ShowsInBlock(o, ring)) c.Obstacles.Add(Poly.To3D(o, q => _site.SampleHeight(q)));
            return c;
        }

        private BlockPaintContext BuildOpenContext(BrushRules rules)
        {
            float h = BlockBrush.OpenHalfSize + 1f;
            float2 min = _bpSeed - h, max = _bpSeed + h;
            var obstacles = _bpObstacles.Gather(min, max, _editingGroup);
            // Road footprints up to the curb: centre curve ± half the composition width (sidewalks included),
            // round caps over the nodes — the same corridors Keep-off-roads uses, without its extra margin.
            try
            {
                var box = new[] { new float3(min.x, 0, min.y), new float3(max.x, 0, min.y), new float3(max.x, 0, max.y), new float3(min.x, 0, max.y) };
                var nets = _site.NetsAround(box, 0f);
                foreach (var r in AreaClip.Corridors(nets, min, max, 0f))
                {
                    var ring = new float2[r.Count];
                    for (int i = 0; i < ring.Length; i++) ring[i] = r[i];
                    obstacles.Add(new BrushObstacle(BrushObstacleKind.Road, ring));
                }
            }
            catch (Exception e) { Mod.Log.Warn("Placemaker: block paint could not read roads: " + e.Message); }
            var water = rules.WaterOn ? _bpObstacles.Water(min, max, _bpMask.CellSize) : null;
            BlockBrush brush;
            try { brush = rules.Build(_bpSeed, null, obstacles, water, _bpMask.CellSize); }
            catch (Exception e) { Mod.Log.Warn("Placemaker: block paint fence failed: " + e.Message); return null; }

            var c = new BlockPaintContext { Face = new List<HalfEdge>(), Open = true, Seed = _bpSeed, Block = null, Brush = brush };
            foreach (var o in brush.Obstacles) c.Obstacles.Add(Poly.To3D(o, q => _site.SampleHeight(q)));
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
            return new BlockPaintState { Face = new List<HalfEdge>(_bpFace), Mask = mask, Open = _bpOpen, Seed = _bpSeed };
        }

        private void RestoreBlockPaint(BlockPaintState s)
        {
            if (s?.Mask != null) _bpMask.Restore(s.Mask); else _bpMask.Clear();
            _bpSnap = s?.Mask;
            _bpSnapVersion = _bpMask.Version;
            _bpFace = s?.Face != null ? new List<HalfEdge>(s.Face) : new List<HalfEdge>();
            _bpOpen = s != null && s.Open;
            _bpSeed = s?.Seed ?? default;
        }

        private void ClearBlockPaint()
        {
            _bpMask.Clear();
            _bpFace = new List<HalfEdge>();
            _bpOpen = false;
        }

        private AreaShape BuildBlockPaintShape()
        {
            if (_bpMask.IsEmpty || !BlockPaintHasTarget) return null;
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
