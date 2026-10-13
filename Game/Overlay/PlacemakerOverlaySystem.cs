// Placemaker — everything the tool draws on the map: outline and handles (vanilla district-tool
// look), snap feedback (PLT colour code), brush cursor, block hover, perimeter path, markers,
// the composed plan (nets by role colour and width, objects as role-coloured discs, surfaces and
// areas filled via FilledPreviewSystem), issues and build errors in red, and built groups in the
// Manage stage.
//
// Scales by construction: only the selection, the plan and bounded queries are drawn — never a
// walk over every node of the map each frame (Grid Road Generator's overlay did that).

using System;
using System.Collections.Generic;
using Colossal.Mathematics;
using Game;
using Game.Net;
using Game.Rendering;
using Game.Tools;
using Placemaker.Core;
using Placemaker.Core.Tool;
using Placemaker.Game.Selection;
using Placemaker.Game.Settings;
using Placemaker.Game.Tool;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Scripting;

namespace Placemaker.Game.Overlay
{
    public sealed partial class PlacemakerOverlaySystem : GameSystemBase
    {
        // Vanilla-like selection palette (district/area tools: light blue outline, white handles).
        private static readonly Color Outline = new Color(0.80f, 0.92f, 1f, 0.95f);
        private static readonly Color OutlineBad = new Color(1f, 0.35f, 0.3f, 0.95f);
        private static readonly Color Fill = new Color(0.30f, 0.62f, 1f, 0.16f);
        private static readonly Color FillHover = new Color(1f, 1f, 1f, 0.10f);
        private static readonly Color FillUnderGhost = new Color(0.30f, 0.62f, 1f, 0.05f);   // barely tint the real surfaces
        private static readonly Color Handle = new Color(0.30f, 0.62f, 1f, 0.95f);
        private static readonly Color HandleHot = new Color(1f, 1f, 1f, 0.95f);
        private static readonly Color Ghost = new Color(1f, 1f, 1f, 0.55f);
        private static readonly Color Brush = new Color(0.30f, 0.62f, 1f, 0.35f);
        private static readonly Color Erase = new Color(1f, 0.45f, 0.35f, 0.35f);
        private static readonly Color Error = new Color(1f, 0.15f, 0.1f, 0.95f);
        private static readonly Color Warning = new Color(1f, 0.75f, 0.15f, 0.95f);
        private static readonly Color Manage = new Color(0.55f, 1f, 0.55f, 0.9f);
        private static readonly Color Marker = new Color(1f, 0.85f, 0.2f, 0.95f);
        private static readonly Color OutlineDim = new Color(0.80f, 0.92f, 1f, 0.35f);
        private static readonly Color ClippedFill = new Color(0.12f, 0.12f, 0.14f, 0.28f);

        private OverlayRenderSystem _overlay;
        private ToolSystem _toolSystem;
        private PlacemakerToolSystem _tool;
        private FilledPreviewSystem _fill;
        private int _paintVersion = -1;
        private List<(float2 a, float2 b)> _paintBoundary = new List<(float2, float2)>();

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            _overlay = World.GetOrCreateSystemManaged<OverlayRenderSystem>();
            _toolSystem = World.GetOrCreateSystemManaged<ToolSystem>();
            _tool = World.GetOrCreateSystemManaged<PlacemakerToolSystem>();
            _fill = World.GetOrCreateSystemManaged<FilledPreviewSystem>();
        }

        [Preserve]
        protected override void OnUpdate()
        {
            if (_toolSystem.activeTool != _tool)
            {
                if (_wasDrawing) { _fill.ClearAll(); _wasDrawing = false; }
                return;
            }
            _wasDrawing = true;
            try
            {
                var buffer = _overlay.GetBuffer(out JobHandle deps);
                deps.Complete();
                _fill.BeginFrame();
                float w = LineWidth();
                var detail = ToolRegistration.Settings?.PreviewDetail ?? PreviewDetail.Normal;

                if (_tool.Stage == ToolStage.Manage) DrawManage(buffer, w);
                else
                {
                    if (_tool.Stage == ToolStage.Design || _tool.Stage == ToolStage.Review || _tool.Stage == ToolStage.Building)
                        DrawPlan(buffer, w, detail);
                    DrawSelection(buffer, w);
                    DrawMarkers(buffer, w);
                    DrawErrors(buffer, w);
                }
                _fill.EndFrame();
            }
            catch (Exception e)
            {
                if (_errors++ < 10) Mod.Log.Error(e, "Placemaker: overlay failed");
            }
        }

        private bool _wasDrawing;
        private int _errors;

        /// <summary>Line width that stays visible from far away without being fat up close.</summary>
        private float LineWidth()
        {
            var cam = Camera.main;
            if (cam == null) return 1f;
            float3 camPos = cam.transform.position;
            // Distance to what is being edited (the outline), else height above it — not the absolute
            // altitude, so mountain cities do not get fat lines.
            float h = camPos.y;
            var shape = _tool.Shape;
            if (shape != null && shape.Ring.Length > 0)
            {
                float3 c = float3.zero;
                foreach (var p in shape.Ring) c += p;
                c /= shape.Ring.Length;
                h = math.distance(camPos, c);
            }
            return math.clamp(math.max(10f, h) * 0.0022f, 0.25f, 6f);
        }

        // ───────────── selection ─────────────

        private void DrawSelection(OverlayRenderSystem.Buffer buffer, float w)
        {
            var shape = _tool.Shape;
            bool bad = _tool.AreaProblem() != null;
            if (shape != null && shape.Ring.Length >= 3)
            {
                var rings = new[] { shape.Ring };
                _fill.SetLayer("selection", rings, _tool.SketchObjects == SketchObjects.Hidden ? FillUnderGhost : Fill, -94f, FilledPreviewSystem.Signature(rings));
            }

            // Keep off existing roads: the parts on roads are left out — shaded grey, the outline used is drawn solid.
            var away = _tool.ClippedAway;
            bool clipped = away != null && away.Count > 0 && shape != null;
            if (clipped)
            {
                var list = new List<float3[]>(away);
                _fill.SetLayer("clipped", list, ClippedFill, -93.9f, FilledPreviewSystem.Signature(list));
            }

            switch (_tool.Mode)
            {
                case SelectionMode.Draw: DrawDrawMode(buffer, w, bad, clipped); break;
                case SelectionMode.Block: DrawBlockMode(buffer, w, shape); break;
                case SelectionMode.Perimeter: DrawPerimeterMode(buffer, w, shape); break;
                case SelectionMode.Paint: DrawPaintMode(buffer, w, shape); break;
            }
            if (clipped && _tool.Mode == SelectionMode.Draw) Ring(buffer, shape.Ring, Outline, w);
        }

        private void Ring(OverlayRenderSystem.Buffer buffer, float3[] ring, Color c, float w, bool dashed = false)
        {
            for (int i = 0; i < ring.Length; i++)
            {
                var seg = new Line3.Segment(ring[i], ring[(i + 1) % ring.Length]);
                if (dashed) buffer.DrawDashedLine(c, seg, w, w * 4f, w * 3f);
                else buffer.DrawLine(c, seg, w);
            }
        }

        private void DrawDrawMode(OverlayRenderSystem.Buffer buffer, float w, bool bad, bool clipped)
        {
            var draw = _tool.Draw;
            var pts = draw.Points;
            int n = pts.Count;
            if (n == 0)
            {
                if (_tool.CursorValid) buffer.DrawCircle(Ghost, _tool.Cursor, w * 6f);
                DrawSnap(buffer, w);
                return;
            }
            var line = bad ? OutlineBad : clipped ? OutlineDim : Outline;
            for (int i = 0; i + 1 < n; i++)
            {
                var c = _tool.HoverEdge == i ? HandleHot : line;
                buffer.DrawLine(c, new Line3.Segment(pts[i], pts[i + 1]), _tool.HoverEdge == i ? w * 1.6f : w);
            }
            if (draw.Closed)
            {
                var c = _tool.HoverEdge == n - 1 ? HandleHot : line;
                buffer.DrawLine(c, new Line3.Segment(pts[n - 1], pts[0]), _tool.HoverEdge == n - 1 ? w * 1.6f : w);
            }
            else if (_tool.CursorValid)
            {
                // Rubber band to the cursor and the closing edge preview.
                buffer.DrawDashedLine(Ghost, new Line3.Segment(pts[n - 1], _tool.Cursor), w, w * 4f, w * 3f);
                if (n >= 2) buffer.DrawDashedLine(new Color(1f, 1f, 1f, 0.25f), new Line3.Segment(_tool.Cursor, pts[0]), w * 0.7f, w * 4f, w * 3f);
            }
            float d = w * 5f;
            for (int i = 0; i < n; i++)
            {
                bool hot = i == _tool.HoverPoint || (i == 0 && _tool.HoverClose);
                buffer.DrawCircle(hot ? HandleHot : Outline, hot ? HandleHot : Handle, w * 0.6f, 0, new float2(0, 1), pts[i], hot ? d * 1.4f : d);
            }
            if (_tool.EdgeDragging) buffer.DrawCircle(HandleHot, _tool.EdgeGuidePoint, d);
            DrawSnap(buffer, w);
        }

        private void DrawSnap(OverlayRenderSystem.Buffer buffer, float w)
        {
            var s = _tool.LastSnap;
            if (s.Kind == SnapKind.None) return;
            var c = SnapColor(s.Kind);
            if (s.HasGuide) buffer.DrawDashedLine(new Color(c.r, c.g, c.b, 0.6f), new Line3.Segment(s.GuideA, s.GuideB), w * 0.6f, w * 3f, w * 3f);
            buffer.DrawCircle(c, new Color(0, 0, 0, 0), w * 0.8f, 0, new float2(0, 1), s.Position, w * 9f);
        }

        // PLT's colour code: curb orange, building red, neighbour area green, axis violet, guide amber, zone grid teal, two at once white.
        private static Color SnapColor(SnapKind k)
        {
            switch (k)
            {
                case SnapKind.RoadEdge: return new Color(1f, 0.55f, 0.1f, 1f);
                case SnapKind.ObjectSide: return new Color(1f, 0.25f, 0.25f, 1f);
                case SnapKind.AreaEdge: return new Color(0.35f, 0.95f, 0.4f, 1f);
                case SnapKind.Direction: return new Color(0.75f, 0.45f, 1f, 1f);
                case SnapKind.Guide: return new Color(1f, 0.78f, 0.25f, 1f);
                case SnapKind.ZoneGrid: return new Color(0.25f, 0.9f, 0.85f, 1f);
                default: return Color.white;
            }
        }

        private void DrawBlockMode(OverlayRenderSystem.Buffer buffer, float w, AreaShape shape)
        {
            if (shape != null) Ring(buffer, shape.Ring, Outline, w);
            var hover = _tool.HoverFace;
            if (hover?.Area != null && _tool.Stage != ToolStage.Building)
            {
                var rings = new[] { hover.Area.Ring };
                _fill.SetLayer("hover", rings, FillHover, -93.5f, FilledPreviewSystem.Signature(rings));
                Ring(buffer, hover.Area.Ring, HandleHot, w * 0.8f, dashed: true);
            }
        }

        private void DrawPerimeterMode(OverlayRenderSystem.Buffer buffer, float w, AreaShape shape)
        {
            var per = _tool.Perimeter;
            var em = EntityManager;
            foreach (var h in per.Path)
            {
                var e = EntityIds.Resolve(em, h.Edge);
                if (e == Entity.Null || !em.HasComponent<Curve>(e)) continue;
                buffer.DrawCurve(Handle, em.GetComponentData<Curve>(e).m_Bezier, w * 2.5f);
            }
            float d = w * 6f;
            for (int i = 0; i < per.Positions.Count; i++)
                buffer.DrawCircle(Outline, i == 0 ? HandleHot : Handle, w * 0.6f, 0, new float2(0, 1), per.Positions[i], i == 0 ? d * 1.3f : d);
            if (shape != null) Ring(buffer, shape.Ring, Outline, w * 0.8f, dashed: true);
            var node = _tool.HoverNode;
            if (node != Entity.Null && em.Exists(node) && em.HasComponent<Node>(node))
                buffer.DrawCircle(HandleHot, new Color(1, 1, 1, 0.3f), w * 0.6f, 0, new float2(0, 1), em.GetComponentData<Node>(node).m_Position, d * 1.6f);
        }

        private void DrawPaintMode(OverlayRenderSystem.Buffer buffer, float w, AreaShape shape)
        {
            var paint = _tool.Paint;
            if (paint.Version != _paintVersion)
            {
                _paintVersion = paint.Version;
                _paintBoundary = paint.RawBoundary();
            }
            // Raw cell boundary while painting; the smoothed outline once the shape exists.
            if (_tool.Painting || shape == null)
            {
                int max = math.min(_paintBoundary.Count, 20000);
                for (int i = 0; i < max; i++)
                {
                    var (a, b) = _paintBoundary[i];
                    var y = _tool.Cursor.y;
                    buffer.DrawLine(Outline, new Line3.Segment(new float3(a.x, y, a.y), new float3(b.x, y, b.y)), w * 0.7f);
                }
            }
            else Ring(buffer, shape.Ring, Outline, w);

            if (!_tool.CursorValid) return;
            var c = _tool.Painting ? Brush : new Color(Brush.r, Brush.g, Brush.b, 0.2f);
            float size = _tool.BrushSize;
            if (!_tool.BrushSquare)
                buffer.DrawCircle(Outline, c, w * 0.6f, 0, new float2(0, 1), _tool.Cursor, size);
            else
            {
                float a = math.radians(_tool.BrushAngle);
                float2 x = new float2(math.cos(a), math.sin(a)) * size * 0.5f, z = new float2(-x.y, x.x);
                var p = _tool.Cursor;
                var corners = new[]
                {
                    new float3(p.x - x.x - z.x, p.y, p.z - x.y - z.y), new float3(p.x + x.x - z.x, p.y, p.z + x.y - z.y),
                    new float3(p.x + x.x + z.x, p.y, p.z + x.y + z.y), new float3(p.x - x.x + z.x, p.y, p.z - x.y + z.y),
                };
                Ring(buffer, corners, Outline, w * 0.8f);
            }
        }

        // ───────────── markers ─────────────

        private void DrawMarkers(OverlayRenderSystem.Buffer buffer, float w)
        {
            var recipe = _tool.CurrentRecipe;
            if (recipe == null || _tool.Shape == null) return;
            float d = w * 7f;
            // Each kind has its own colour (MarkerKinds.ColorFor: two-way yellow, entry green, exit orange…);
            // markers of the step being edited are drawn larger with a white rim.
            foreach (var kv in RecipePaths.Walk(recipe))
            {
                bool stepActive = kv.Key == _tool.MarkerPath;
                foreach (var m in kv.Value.Markers)
                {
                    var c = KindColor(m.Kind, 0.95f);
                    float size = stepActive ? d * 1.3f : d;
                    buffer.DrawCircle(stepActive ? HandleHot : c, c, w * 0.6f, 0, new float2(0, 1), m.Position, size);
                    // Inner dot: entry/exit read at a glance even for colour-blind players (entry filled, exit hollow).
                    if (m.Kind == "exit" || m.Kind == "alleyOut") buffer.DrawCircle(Color.white, new Color(0, 0, 0, 0.6f), w * 0.4f, 0, new float2(0, 1), m.Position, size * 0.45f);
                    else if (m.Kind == "entry" || m.Kind == "alleyIn") buffer.DrawCircle(Color.white, Color.white, w * 0.4f, 0, new float2(0, 1), m.Position, size * 0.35f);
                }
            }
            if (_tool.MarkerKind != null && _tool.MarkerHoverValid)
                buffer.DrawCircle(HandleHot, KindColor(_tool.MarkerKind, 0.4f), w * 0.6f, 0, new float2(0, 1), _tool.MarkerHover, d * 1.3f);
        }

        // ───────────── plan ─────────────

        private ComposedPlan _filledPlan;
        private readonly Dictionary<int, List<float3[]>> _surfaceGroups = new Dictionary<int, List<float3[]>>();
        private readonly Dictionary<int, List<float3[]>> _areaGroups = new Dictionary<int, List<float3[]>>();

        private void DrawPlan(OverlayRenderSystem.Buffer buffer, float w, PreviewDetail detail)
        {
            var plan = _tool.Plan;
            if (plan == null) return;
            bool stale = _tool.PlanStale;
            float alpha = stale ? 0.45f : 1f;
            // Ghost on the map (real models/surfaces): the sketch steps back — hidden when the ghost shows this plan,
            // dimmed while it catches up. Fills keep their meshes (alpha 0) so switching back is instant.
            var objects = _tool.SketchObjects;
            if (objects == SketchObjects.Dimmed) alpha *= 0.3f;
            bool content = objects != SketchObjects.Hidden;
            float fillAlpha = content ? alpha : 0f;

            if (!ReferenceEquals(plan, _filledPlan))
            {
                _filledPlan = plan;
                try { _assetInfo = Services.Assets?.InfoMap(); } catch (Exception) { _assetInfo = null; }
                _surfaceGroups.Clear();
                _areaGroups.Clear();
                foreach (var l in plan.Layers)
                {
                    if (l == null) continue;
                    foreach (var s in l.Surfaces)
                        if (s?.Ring != null && s.Ring.Length >= 3) Group(_surfaceGroups, (int)s.Role).Add(s.Ring);
                    foreach (var a in l.Areas)
                        if (a?.Ring != null && a.Ring.Length >= 3) Group(_areaGroups, (int)a.Role).Add(a.Ring);
                }
            }
            foreach (var kv in _surfaceGroups)
                _fill.SetLayer("surface:" + kv.Key, kv.Value, WithAlpha(SurfaceColor((SurfaceRole)kv.Key), 0.55f * fillAlpha), -93f + kv.Key * 0.01f, FilledPreviewSystem.Signature(kv.Value));
            foreach (var kv in _areaGroups)
                _fill.SetLayer("area:" + kv.Key, kv.Value, WithAlpha(AreaColor((AreaRole)kv.Key), 0.3f * fillAlpha), -93.5f, FilledPreviewSystem.Signature(kv.Value));

            int netBudget = detail == PreviewDetail.Detailed ? 20000 : 6000;
            int objBudget = detail == PreviewDetail.Simple ? 0 : detail == PreviewDetail.Detailed ? 30000 : 8000;
            int objCount = 0;
            foreach (var l in plan.Layers) objCount += l?.Objects.Count ?? 0;
            int stride = objBudget <= 0 ? int.MaxValue : math.max(1, (int)math.ceil(objCount / (float)objBudget));
            int k = 0;
            foreach (var l in plan.Layers)
            {
                if (l == null) continue;
                foreach (var n in l.Nets)
                {
                    if (n == null || !content || netBudget-- <= 0) break;
                    DrawNet(buffer, n, w, alpha);
                }
                foreach (var o in l.Objects)
                {
                    if (o == null || !content || (k++ % stride) != 0) continue;
                    DrawObject(buffer, o, w, alpha);
                }
                foreach (var t in l.Terraform)
                    if (t?.Ring != null) Ring(buffer, t.Ring, t.Permanent ? Warning : new Color(0.4f, 0.7f, 1f, 0.8f), w, dashed: true);
                foreach (var z in l.Zoning)
                    if (z != null) Ring(buffer, new[] { z.A, z.B, z.C, z.D }, new Color(0.5f, 1f, 0.5f, 0.6f * alpha), w * 0.6f);
            }
            foreach (var issue in plan.AllIssues())
            {
                if (issue?.At == null) continue;
                buffer.DrawCircle(issue.Severity == IssueSeverity.Error ? Error : Warning, new Color(1f, 0f, 0f, 0.15f), w, 0, new float2(0, 1), issue.At.Value, w * 14f);
            }
        }

        private static List<float3[]> Group(Dictionary<int, List<float3[]>> d, int key)
        {
            if (!d.TryGetValue(key, out var l)) d[key] = l = new List<float3[]>();
            return l;
        }

        private void DrawErrors(OverlayRenderSystem.Buffer buffer, float w)
        {
            // What the last build skipped (amber) or repaired (blue), until the player changes something.
            foreach (var n in _tool.BuildNotes)
                if (n?.At != null)
                    buffer.DrawCircle(n.Severity == IssueSeverity.Warning ? Warning : new Color(0.4f, 0.7f, 1f, 0.9f), new Color(1f, 1f, 1f, 0.1f), w, 0, new float2(0, 1), n.At.Value, w * 14f);
            var errors = _tool.BuildErrorPositions;
            if (errors == null) return;
            foreach (var p in errors) buffer.DrawCircle(Error, new Color(1f, 0f, 0f, 0.2f), w, 0, new float2(0, 1), p, w * 16f);
        }

        private static Color WithAlpha(Color c, float a) => new Color(c.r, c.g, c.b, a);

        private static Color KindColor(string kind, float a)
        {
            var (r, g, b) = MarkerKinds.ColorFor(kind);
            return new Color(r, g, b, a);
        }

        // ───────────── plan: nets and objects (sketch style) ─────────────
        //
        // Grid Road Generator's sketch look: a thin dashed centre line in the role colour plus two faint
        // edge lines at the real width — readable over terrain and never hiding it. Line width and dash
        // length follow the camera height (w), so it stays crisp close up and visible from far away.
        // Objects are small, subtle dots; trees are soft discs at crown size.

        private Dictionary<string, AssetInfo> _assetInfo;

        private float HalfWidth(PlannedNet n)
        {
            if (_assetInfo != null && !string.IsNullOrEmpty(n.Asset.Id) && _assetInfo.TryGetValue(n.Asset.Id, out var info) && info.NetWidth > 0.5f)
                return info.NetWidth * 0.5f;
            return Composer.DefaultHalfWidth(n.Role);
        }

        private void DrawNet(OverlayRenderSystem.Buffer buffer, PlannedNet n, float w, float alpha)
        {
            var curve = new Bezier4x3(n.A, n.B, n.C, n.D);
            var col = NetColor(n.Role);
            if (n.Role == NetRole.Fence)
            {
                buffer.DrawCurve(WithAlpha(col, 0.7f * alpha), curve, w * 0.6f);
                return;
            }
            float hw = HalfWidth(n);
            // Edges: faint, at the real width (skipped when narrower than the centre line would make them readable).
            if (hw > w * 1.5f)
            {
                var edge = WithAlpha(col, 0.3f * alpha);
                buffer.DrawCurve(edge, OffsetCurve(curve, hw), w * 0.45f);
                buffer.DrawCurve(edge, OffsetCurve(curve, -hw), w * 0.45f);
            }
            float dash = w * 5f;
            buffer.DrawDashedCurve(WithAlpha(col, 0.95f * alpha), curve, w * 0.8f, dash, dash * 0.7f);
        }

        /// <summary>Approximate parallel curve: end points and handles shifted along the end normals (fine for road-like curvature).</summary>
        private static Bezier4x3 OffsetCurve(Bezier4x3 c, float d)
        {
            float2 t0 = math.normalizesafe(c.b.xz - c.a.xz, math.normalizesafe(c.d.xz - c.a.xz, new float2(1, 0)));
            float2 t1 = math.normalizesafe(c.d.xz - c.c.xz, t0);
            float3 n0 = new float3(-t0.y, 0, t0.x) * d, n1 = new float3(-t1.y, 0, t1.x) * d;
            return new Bezier4x3(c.a + n0, c.b + n0, c.c + n1, c.d + n1);
        }

        private void DrawObject(OverlayRenderSystem.Buffer buffer, PlannedObject o, float w, float alpha)
        {
            var col = ObjectColor(o.Role);
            float scale = math.max(0.3f, o.Scale);
            if (o.Role == ObjectRole.Tree || o.Role == ObjectRole.Bush)
            {
                float crown = Composer.DefaultRadius(o.Role);
                if (_assetInfo != null && !string.IsNullOrEmpty(o.Asset.Id) && _assetInfo.TryGetValue(o.Asset.Id, out var info))
                {
                    float r = math.cmax((info.BoundsMax - info.BoundsMin).xz) * 0.5f;
                    if (r > 0.3f) crown = r;
                }
                float dia = math.max(crown * 2f * scale, w * 2f);
                buffer.DrawCircle(WithAlpha(col, 0.35f * alpha), WithAlpha(col, 0.16f * alpha), w * 0.3f, 0, new float2(0, 1), o.Position, dia);
                return;
            }
            if (o.Role == ObjectRole.Centerpiece)
            {
                float dia = Composer.DefaultRadius(o.Role) * 2f * scale;
                buffer.DrawCircle(WithAlpha(col, 0.6f * alpha), WithAlpha(col, 0.12f * alpha), w * 0.4f, 0, new float2(0, 1), o.Position, dia);
                return;
            }
            // Small furniture / bays: a subtle dot, a little larger than a line so it reads from afar.
            float dot = math.clamp(w * 1.6f, 0.5f, Composer.DefaultRadius(o.Role) * 2f);
            buffer.DrawCircle(WithAlpha(col, 0.7f * alpha), o.Position, dot);
        }

        private static Color NetColor(NetRole r)
        {
            switch (r)
            {
                case NetRole.Avenue: return new Color(0.35f, 0.85f, 1f);                                   // cyan
                case NetRole.Street: case NetRole.PerimeterRoad: case NetRole.CulDeSac: case NetRole.Roundabout: return new Color(0.95f, 0.95f, 0.95f);   // white
                case NetRole.Pathway: case NetRole.PathwayWide: return new Color(1f, 0.82f, 0.45f);          // warm yellow
                case NetRole.DriveAisle: case NetRole.DriveConnector: case NetRole.Alley: return new Color(0.4f, 0.6f, 1f);   // parking aisles: blue
                case NetRole.Fence: return new Color(0.6f, 0.45f, 0.32f);
                default: return Color.white;
            }
        }

        private static Color ObjectColor(ObjectRole r)
        {
            switch (r)
            {
                case ObjectRole.Tree: return new Color(0.2f, 0.65f, 0.25f);
                case ObjectRole.Bush: case ObjectRole.Planter: return new Color(0.45f, 0.8f, 0.35f);
                case ObjectRole.Bench: case ObjectRole.Bin: case ObjectRole.Furniture: return new Color(0.85f, 0.65f, 0.4f);
                case ObjectRole.Lamp: return new Color(1f, 0.95f, 0.6f);
                case ObjectRole.Centerpiece: return new Color(0.4f, 0.75f, 1f);
                case ObjectRole.ParkingBay: case ObjectRole.Charger: case ObjectRole.Arrow: return new Color(0.85f, 0.85f, 0.95f);
                default: return new Color(0.9f, 0.9f, 0.9f);
            }
        }

        private static Color SurfaceColor(SurfaceRole r)
        {
            switch (r)
            {
                case SurfaceRole.Grass: return new Color(0.35f, 0.75f, 0.3f);
                case SurfaceRole.Paving: case SurfaceRole.Plaza: return new Color(0.85f, 0.8f, 0.7f);
                case SurfaceRole.Asphalt: return new Color(0.35f, 0.35f, 0.4f);
                case SurfaceRole.Water: return new Color(0.25f, 0.5f, 0.95f);
                case SurfaceRole.Decorative: return new Color(0.85f, 0.55f, 0.75f);
                default: return new Color(0.6f, 0.5f, 0.35f);
            }
        }

        private static Color AreaColor(AreaRole r) =>
            r == AreaRole.Lot ? new Color(0.9f, 0.9f, 1f) : r == AreaRole.Hangaround ? new Color(1f, 0.85f, 0.5f) : new Color(0.7f, 0.9f, 1f);

        // ───────────── manage ─────────────

        private void DrawManage(OverlayRenderSystem.Buffer buffer, float w)
        {
            int group = _tool.ManagedGroup != 0 ? _tool.ManagedGroup : _tool.HoverGroup;
            if (group == 0) return;
            var em = EntityManager;
            var members = _tool.GroupMembers(group);
            int budget = 5000;
            var c = _tool.ManagedGroup != 0 ? Manage : new Color(1f, 1f, 1f, 0.75f);
            foreach (var e in members)
            {
                if (budget-- <= 0) break;
                if (e == Entity.Null || !em.Exists(e)) continue;
                if (em.HasComponent<Curve>(e)) { buffer.DrawCurve(c, em.GetComponentData<Curve>(e).m_Bezier, w * 2f); continue; }
                if (em.HasBuffer<global::Game.Areas.Node>(e))
                {
                    var nodes = em.GetBuffer<global::Game.Areas.Node>(e, true);
                    for (int i = 0; i < nodes.Length; i++)
                        buffer.DrawLine(c, new Line3.Segment(nodes[i].m_Position, nodes[(i + 1) % nodes.Length].m_Position), w);
                    continue;
                }
                if (em.HasComponent<global::Game.Objects.Transform>(e))
                    buffer.DrawCircle(c, em.GetComponentData<global::Game.Objects.Transform>(e).m_Position, w * 4f);
            }
        }

        [Preserve]
        public PlacemakerOverlaySystem() { }
    }
}
