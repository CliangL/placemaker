// In-game test suite hooks (docs/teardown/10-ingame-suite.md §4.3). Used only by Game/Suite/SuiteRunner, which exists
// only when the suite gate is open; in a normal game nothing calls these. Each hook replays exactly what the mouse path
// does (Block click, Draw corners + close), minus the raycast.

using System;
using Placemaker.Core;
using Placemaker.Core.Geometry;
using Placemaker.Core.Tool;
using Unity.Mathematics;

namespace Placemaker.Game.Tool
{
    public sealed partial class PlacemakerToolSystem
    {
        /// <summary>Group id of the last finished build (0 = loose build or none yet).</summary>
        public int SuiteLastBuiltGroup { get; private set; }
        /// <summary>Counts finished builds (Done), so the runner can tell a new result from an old one.</summary>
        public int SuiteBuildsDone { get; private set; }

        /// <summary>While set, recipe seeds are deterministic (replaces NewSeed's tick count).</summary>
        public static uint? SuiteSeed;

        public static void SuiteResetSeed(uint seed)
        {
            SuiteSeed = seed;
            _seedCounter = 0;
        }

        private void SuiteNoteBuilt(int groupId)
        {
            SuiteLastBuiltGroup = groupId;
            SuiteBuildsDone++;
        }

        // ───────────── scripted input (the suite's mouse) ─────────────
        // While SuiteInputOnly is set, ReadInput ignores the real mouse and keyboard (a person touching the window cannot
        // disturb a run) and plays one queued SuiteFrame per tool update instead: the cursor at a world point (the raycast
        // result a real mouse would give there), buttons pressed/held/released, modifiers. Every mode's own update code
        // then runs exactly as for a person: Points clicks and snapping, the Paint bucket, Paint strokes, Road node clicks,
        // entrance clicks on the outline.

        public struct SuiteFrame
        {
            public float3 At;
            /// <summary>The entity under the cursor (a road node for Road nodes), Entity.Null for plain ground.</summary>
            public global::Unity.Entities.Entity Entity;
            public bool LeftDown, LeftHeld, RightDown, RightHeld, Shift, Ctrl, Alt;
        }

        public static bool SuiteInputOnly;
        private static readonly System.Collections.Generic.Queue<SuiteFrame> s_suiteFrames = new System.Collections.Generic.Queue<SuiteFrame>();
        private static SuiteFrame s_suiteLast;
        private static bool s_suiteHasLast;

        public static int SuiteFramesPending => s_suiteFrames.Count;

        public static void SuiteQueue(SuiteFrame f) => s_suiteFrames.Enqueue(f);

        /// <summary>A click: press at the point, then release there.</summary>
        public static void SuiteClick(float3 at, global::Unity.Entities.Entity entity = default, bool right = false, bool shift = false, bool ctrl = false)
        {
            SuiteQueue(new SuiteFrame { At = at, Entity = entity, LeftDown = !right, LeftHeld = !right, RightDown = right, RightHeld = right, Shift = shift, Ctrl = ctrl });
            SuiteQueue(new SuiteFrame { At = at, Entity = entity, Shift = shift, Ctrl = ctrl });
        }

        /// <summary>A drag: press at the first point, held through the rest (steps per segment), released at the last.</summary>
        public static void SuiteDrag(float3[] path, int stepsPerSegment, bool right = false)
        {
            if (path == null || path.Length == 0) return;
            SuiteQueue(new SuiteFrame { At = path[0], LeftDown = !right, LeftHeld = !right, RightDown = right, RightHeld = right });
            for (int i = 1; i < path.Length; i++)
                for (int k = 1; k <= math.max(1, stepsPerSegment); k++)
                    SuiteQueue(new SuiteFrame { At = math.lerp(path[i - 1], path[i], k / (float)math.max(1, stepsPerSegment)), LeftHeld = !right, RightHeld = right });
            SuiteQueue(new SuiteFrame { At = path[path.Length - 1] });
        }

        /// <summary>ReadInput for a suite run: the next scripted frame, or the cursor resting where the last one left it.</summary>
        private void ReadSuiteInput()
        {
            _in = default;
            _in.Time = UnityEngine.Time.realtimeSinceStartup;
            _in.OverWorld = true;
            SuiteFrame f;
            if (s_suiteFrames.Count > 0) { f = s_suiteFrames.Dequeue(); s_suiteLast = f; s_suiteHasLast = true; }
            else if (s_suiteHasLast) f = new SuiteFrame { At = s_suiteLast.At, Entity = s_suiteLast.Entity };
            else { _in.LeftReleased = true; return; }
            var at = math.isfinite(f.At.y) ? f.At : WithHeight(new float3(f.At.x, 0f, f.At.z));
            _in.HasHit = true;
            _in.Hit = at;
            _in.HitEntity = f.Entity;
            _in.RawHit = new global::Game.Common.RaycastHit { m_HitEntity = f.Entity, m_Position = at, m_HitPosition = at };
            _in.LeftPressed = f.LeftDown;
            _in.LeftHeld = f.LeftHeld;
            _in.LeftReleased = !f.LeftHeld;
            _in.RightPressed = f.RightDown;
            _in.RightHeld = f.RightHeld;
            _in.Shift = f.Shift;
            _in.Ctrl = f.Ctrl;
            _in.Alt = f.Alt;
        }

        /// <summary>Every marker of the current design, in step order (position, kind, step path).</summary>
        public System.Collections.Generic.List<(float3 At, string Kind, string Path)> SuiteMarkers()
        {
            var r = new System.Collections.Generic.List<(float3, string, string)>();
            if (_recipe == null) return r;
            foreach (var kv in RecipePaths.Walk(_recipe))
                foreach (var m in kv.Value.Markers) r.Add((m.Position, m.Kind, kv.Key));
            return r;
        }

        /// <summary>The primary button's marker request (step path and kind) when it asks for entrances, else nulls.</summary>
        public (string Path, string Kind) SuiteMarkerRequest()
        {
            var p = StageMachine.Primary(_stage, Facts());
            return p.Kind == PrimaryKind.PlaceMarkers ? (p.StepPath, p.MarkerKind) : (null, null);
        }

        /// <summary>Block mode click at a world point (the Paint bucket path without the raycast).</summary>
        public bool SuiteSelectBlockAt(float2 xz, out string problem)
        {
            problem = null;
            if (!IsActive) { problem = "tool not active"; return false; }
            CancelGesture();
            ExitMarkerMode();
            if (_mode != SelectionMode.Block) SetAreaMode(SelectionMode.Block);
            _roads.InvalidateGraph();
            var face = PickFace(xz, out var why);
            if (face == null) { problem = why?.Key ?? "no block"; return false; }
            _hoverFace = face;
            _hoverFaceAt = xz;
            _blocks = new BlockSelectionState();
            _blocks.Faces.Add(new System.Collections.Generic.List<HalfEdge>(face.Loop));
            _blocks.Seeds.Add(face.Seed);
            ShapeChanged();
            Commit("Placemaker.Undo.SelectBlock");
            if (_stage == ToolStage.Area && _shape != null) SetStage(ToolStage.Design);
            if (_shape == null) { problem = "block gave no outline"; return false; }
            return true;
        }

        /// <summary>Draw mode: corners in order, then close (the double-click path).</summary>
        public bool SuiteDrawOutline(float3[] corners, out string problem)
        {
            problem = null;
            if (!IsActive) { problem = "tool not active"; return false; }
            CancelGesture();
            ExitMarkerMode();
            if (_mode != SelectionMode.Draw) SetAreaMode(SelectionMode.Draw);
            ClearSelectionState();
            foreach (var c in corners)
            {
                var p = WithHeight(new float3(c.x, 0f, c.z));
                var bad = _draw.CheckAdd(p.xz);
                if (bad != null) { problem = bad; return false; }
                _draw.AddPoint(p, float2.zero);
            }
            ShapeChanged();
            Commit("Placemaker.Undo.AddPoint");
            CloseOutline();
            if (_shape == null) { problem = "outline did not close"; return false; }
            return true;
        }

        /// <summary>
        /// The "Add at least one entrance" path: the primary button starts marker placement on the unready step, then one
        /// click on the outline at the point closest to <paramref name="near"/> (UpdateMarkers' left click, minus the
        /// raycast). Returns "step-path kind" for the marker placed, or null with the reason.
        /// </summary>
        public string SuiteAddMarker(float2 near, out string problem)
        {
            problem = null;
            var p = StageMachine.Primary(_stage, Facts());
            if (p.Kind != PrimaryKind.PlaceMarkers || string.IsNullOrEmpty(p.StepPath)) { problem = "no step needs markers"; return null; }
            StartMarkers(p.StepPath, p.MarkerKind);
            if (_markerKind == null || _shape == null) { problem = "marker placement did not start"; return null; }
            if (!Composer.ClosestSegment(_shape.Ring, near, out int seg, out float t, out _)) { problem = "no outline segment"; ExitMarkerMode(); return null; }
            var at = math.lerp(_shape.Ring[seg], _shape.Ring[(seg + 1) % _shape.Ring.Length], t);
            var m = new EdgeMarker { Kind = _markerKind, RingSegment = seg, RingT = t, Position = at };
            string placed = _markerPath + " " + _markerKind;
            if (!_editor.AddMarker(_recipe, _markerPath, m)) { problem = "marker refused"; ExitMarkerMode(); return null; }
            MarkMarkersEdited(_markerPath);
            DesignChanged();
            Commit("Placemaker.Undo.AddMarker");
            ExitMarkerMode();
            return placed;
        }

        /// <summary>What the primary button would do now (label kind, enabled, reason key).</summary>
        public (string Kind, bool Enabled, string Reason) SuitePrimaryInfo()
        {
            var p = StageMachine.Primary(_stage, Facts());
            return (PrimaryAction.KindId(p.Kind), p.Enabled, p.Reason?.Key ?? "");
        }

        /// <summary>Reasons that block the primary button, with arguments, for the result file.</summary>
        public string SuiteUnreadyText()
        {
            try
            {
                var p = StageMachine.Primary(_stage, Facts());
                if (p.Enabled) return "";
                var args = p.Reason?.Args != null && p.Reason.Args.Length > 0 ? " (" + string.Join(", ", p.Reason.Args) + ")" : "";
                return (p.Reason?.Key ?? "") + args + (string.IsNullOrEmpty(p.StepPath) ? "" : " at " + p.StepPath);
            }
            catch (Exception e) { return "facts failed: " + e.Message; }
        }
    }
}
