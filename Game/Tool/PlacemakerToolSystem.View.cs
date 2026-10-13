// What the panel and the cursor tooltips show: phase-aware hints and the contract's ToolState.
// One right-click rule everywhere: right-click removes the thing you would add with a click
// (corner, node, block, paint, marker) — never anything else.

using System.Collections.Generic;
using Placemaker.Core.Tool;

namespace Placemaker.Game.Tool
{
    public sealed partial class PlacemakerToolSystem
    {
        /// <summary>Hints for the current situation, most important first (tooltips show the first ones).</summary>
        public List<HintInfo> Hints()
        {
            var h = new List<HintInfo>(8);
            var flash = FlashText;
            if (flash != null) h.Add(new HintInfo(flash.Key) { Text = flash });

            if (_stage == ToolStage.Building)
            {
                h.Add(new HintInfo("Placemaker.Hint.Building"));
                return h;
            }
            if (_markerKind != null)
            {
                h.Add(new HintInfo("Placemaker.Hint.MarkerPlace"));
                h.Add(new HintInfo("Placemaker.Hint.MarkerRemove"));
                h.Add(new HintInfo("Placemaker.Hint.MarkerDone", "Esc"));
                return h;
            }
            if (_stage == ToolStage.Manage)
            {
                h.Add(new HintInfo("Placemaker.Hint.ManagePick"));
                if (_managedGroup != 0) h.Add(new HintInfo("Placemaker.Hint.ManageDeselect"));
                h.Add(new HintInfo("Placemaker.Hint.Back", "Esc"));
                return h;
            }

            switch (_mode)
            {
                case SelectionMode.Draw:
                    if (!_draw.Closed)
                    {
                        h.Add(new HintInfo(_draw.Count >= PolygonEditor.MinPoints ? "Placemaker.Hint.DrawAddOrClose" : "Placemaker.Hint.DrawAdd"));
                        if (_draw.Count > 0) h.Add(new HintInfo("Placemaker.Hint.DrawRemoveLast"));
                        h.Add(new HintInfo("Placemaker.Hint.DrawNoSnap", "Alt"));
                    }
                    else
                    {
                        if (_gesture == Gesture.DragEdge) h.Add(new HintInfo("Placemaker.Hint.DrawEdgeDragging"));
                        else if (_hoverPoint >= 0) { h.Add(new HintInfo("Placemaker.Hint.DrawMovePoint")); h.Add(new HintInfo("Placemaker.Hint.DrawRemovePoint")); }
                        else if (_hoverEdge >= 0)
                        {
                            h.Add(new HintInfo("Placemaker.Hint.DrawInsertDrag"));
                            h.Add(new HintInfo("Placemaker.Hint.DrawInsertClick", "Ctrl"));
                            h.Add(new HintInfo("Placemaker.Hint.DrawMoveEdge", "Shift"));
                        }
                        else h.Add(new HintInfo("Placemaker.Hint.DrawEdit"));
                    }
                    break;
                case SelectionMode.Block:
                    h.Add(new HintInfo(_blocks.Faces.Count == 0 ? "Placemaker.Hint.BlockPick" : "Placemaker.Hint.BlockReplace"));
                    h.Add(new HintInfo("Placemaker.Hint.BlockAdd", "Shift"));
                    if (_blocks.Faces.Count > 0) h.Add(new HintInfo("Placemaker.Hint.BlockRemove"));
                    break;
                case SelectionMode.Perimeter:
                    if (_perimeter.Closed) h.Add(new HintInfo("Placemaker.Hint.PerimeterReopen"));
                    else
                    {
                        h.Add(new HintInfo(_perimeter.Nodes.Count >= 2 ? "Placemaker.Hint.PerimeterAddOrClose" : "Placemaker.Hint.PerimeterAdd"));
                        if (_perimeter.Nodes.Count > 0) h.Add(new HintInfo("Placemaker.Hint.PerimeterRemoveLast"));
                    }
                    break;
                case SelectionMode.Paint:
                    h.Add(new HintInfo("Placemaker.Hint.PaintPaint"));
                    h.Add(new HintInfo("Placemaker.Hint.PaintErase"));
                    if (_brushSquare) h.Add(new HintInfo("Placemaker.Hint.PaintRotate", "Shift"));
                    break;
            }
            if (_history.CanUndo) h.Add(new HintInfo("Placemaker.Hint.Undo", "Ctrl", "Z"));
            var p = StageMachine.Primary(_stage, Facts());
            if (p.Enabled && p.Kind != PrimaryKind.None) h.Add(new HintInfo("Placemaker.Hint.Primary", "Enter"));
            h.Add(new HintInfo(HasSelection() || _stage != ToolStage.Area ? "Placemaker.Hint.Back" : "Placemaker.Hint.Close", "Esc"));
            return h;
        }

        public ToolStateInput StateInput()
        {
            var facts = Facts();
            var s = new ToolStateInput
            {
                Open = IsActive || _building,
                Stage = _stage,
                Reachable = StageMachine.Reachable(facts),
                Mode = _mode,
                BrushSquare = _brushSquare,
                BrushSize = _brushSize,
                BrushAngle = _brushAngle,
                AreaValid = _shape != null && facts.AreaProblem == null,
                AreaClosed = AreaClosed,
                PointCount = PointCount,
                AreaM2 = AreaM2,
                PerimeterM = PerimeterM,
                AreaProblem = facts.AreaProblem,
                Primary = StageMachine.Primary(_stage, facts),
                CanUndo = CanUndo,
                CanRedo = CanRedo,
                UndoLabel = UndoLabel,
                RedoLabel = RedoLabel,
                MarkerStepPath = _markerPath,
                MarkerKind = _markerKind,
                MarkerLabel = _markerKind != null ? new LocText("Placemaker.Marker." + _markerKind) : null,
                Hints = Hints(),
                Planning = facts.Planning,
                BuildProgress = _building ? BuildProgress : (float?)null,
                EditingGroup = _editingGroup,
                AnarchyAvailable = AnarchyAvailable,
                AnarchyEnabled = _anarchy,
                PanelCollapsed = _panelCollapsed,
            };
            return s;
        }
    }
}
