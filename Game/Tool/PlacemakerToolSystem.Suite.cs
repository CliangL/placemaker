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
