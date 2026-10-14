// Block paint overlay: the block the brush is fenced to (faint dashed curb line), what will not be
// painted (zoned cells, lots, areas in a faint red tint), the block a new stroke would start in
// (dashed white), the painted cells / smoothed outline (as Paint) and the brush (red while erasing).

using System.Collections.Generic;
using Colossal.Mathematics;
using Game.Rendering;
using Placemaker.Core;
using Placemaker.Core.Tool;
using Placemaker.Game.Tool;
using Unity.Mathematics;
using UnityEngine;

namespace Placemaker.Game.Overlay
{
    public sealed partial class PlacemakerOverlaySystem
    {
        private static readonly Color Blocked = new Color(1f, 0.3f, 0.25f, 0.18f);
        private static readonly Color HoverBlock = new Color(1f, 1f, 1f, 0.45f);

        private int _bpVersion = -1;
        private List<(float2 a, float2 b)> _bpBoundary = new List<(float2, float2)>();
        private PlacemakerToolSystem.BlockPaintContext _bpSigFor;
        private long _bpSig;
        private List<float3[]> _bpShown;
        private List<float3[]> _bpNear = new List<float3[]>();
        private PlacemakerToolSystem.BlockPaintContext _bpNearFor;
        private float2 _bpNearAt = new float2(float.MaxValue);

        /// <summary>
        /// Obstacles within reach of the brush (open-land fallback: the work box holds hundreds of road
        /// pieces and zone cells). Re-filtered only after the cursor moved 16 m, so the fill mesh is not
        /// rebuilt every frame.
        /// </summary>
        private List<float3[]> NearBrush(PlacemakerToolSystem.BlockPaintContext cur)
        {
            float2 at = _tool.CursorValid ? _tool.Cursor.xz : cur.Seed;
            if (ReferenceEquals(_bpNearFor, cur) && math.distance(at, _bpNearAt) < 16f) return _bpNear;
            _bpNearFor = cur;
            _bpNearAt = at;
            float reach = _tool.BrushSize * 0.5f + 60f;
            var list = new List<float3[]>();
            foreach (var o in cur.Obstacles)
            {
                float2 mn = new float2(float.MaxValue), mx = new float2(float.MinValue);
                foreach (var p in o) { mn = math.min(mn, p.xz); mx = math.max(mx, p.xz); }
                float2 d = math.max(math.max(mn - at, at - mx), 0f);
                if (math.length(d) <= reach) list.Add(o);
            }
            _bpNear = list;
            return list;
        }

        private void DrawBlockPaintMode(OverlayRenderSystem.Buffer buffer, float w, AreaShape shape)
        {
            var cur = _tool.BlockPaintCurrent;
            // s15: shading and hover outline only while the brush is live (Area stage, over the world, no markers).
            bool selecting = _tool.BrushLive;
            if (cur != null)
            {
                // Open-land fallback: no block line; the shading shows the roads/obstacles near the brush.
                if (cur.Block != null) Ring(buffer, cur.Block, OutlineDim, w * 0.8f, dashed: true);
                if (selecting && cur.Obstacles.Count > 0)
                {
                    var shown = cur.Open ? NearBrush(cur) : cur.Obstacles;
                    if (!ReferenceEquals(_bpSigFor, cur) || !ReferenceEquals(_bpShown, shown))
                    {
                        _bpSigFor = cur;
                        _bpShown = shown;
                        _bpSig = FilledPreviewSystem.Signature(shown);
                    }
                    if (shown.Count > 0) _fill.SetLayer("blockPaintBlocked", shown, Blocked, -93.8f, _bpSig);
                }
            }

            var hover = _tool.HoverFace;
            if (hover?.Area != null && !_tool.Painting && _tool.BrushLive)
                Ring(buffer, hover.Area.Ring, HoverBlock, w * 0.7f, dashed: true);

            var mask = _tool.BlockPaintMask;
            if (mask.Version != _bpVersion)
            {
                _bpVersion = mask.Version;
                _bpBoundary = mask.RawBoundary();
            }
            // Raw cells while painting; the smoothed, curb-snapped outline once the shape exists.
            if (_tool.Painting || shape == null)
            {
                int max = math.min(_bpBoundary.Count, 20000);
                float y = _tool.Cursor.y;
                for (int i = 0; i < max; i++)
                {
                    var (a, b) = _bpBoundary[i];
                    buffer.DrawLine(Outline, new Line3.Segment(new float3(a.x, y, a.y), new float3(b.x, y, b.y)), w * 0.7f);
                }
            }
            else Ring(buffer, shape.Ring, Outline, w);

            var c = _tool.Erasing ? Erase : Brush;
            if (_tool.BrushLive) DrawBrushCursor(buffer, w, _tool.Painting ? c : new Color(c.r, c.g, c.b, 0.2f));
        }
    }
}
