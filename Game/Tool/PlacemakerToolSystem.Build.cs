// Build (via Services.Build, implemented by the build workstream) and the Manage stage
// (hover/click a built group → panel shows it; Edit loads its receipt back into the tool).

using System;
using System.Collections.Generic;
using Game.Common;
using Placemaker.Core;
using Placemaker.Core.Tool;
using Unity.Entities;
using Unity.Mathematics;

namespace Placemaker.Game.Tool
{
    public sealed partial class PlacemakerToolSystem
    {
        private bool _building;
        private BuildStatus _buildStatus;
        private PlanIssue _buildFailure;
        private List<float3> _buildErrors;
        private int _hoverGroup, _managedGroup;
        private readonly List<Entity> _members = new List<Entity>();
        private int _membersGroup = -1;

        public bool Building => _building;
        public float BuildProgress => _building ? math.saturate(_buildStatus.Progress) : 0f;
        public PlanIssue BuildFailure => _buildFailure;
        public IReadOnlyList<float3> BuildErrorPositions => _buildErrors;
        public int HoverGroup => _hoverGroup;
        public int ManagedGroup => _managedGroup;

        private void StartBuild()
        {
            var svc = Services.Build;
            if (svc == null) { Flash(new LocText("Placemaker.Reason.BuildUnavailable")); return; }
            if (_plan == null || _shape == null || _recipe == null) return;
            _buildFailure = null;
            _buildErrors = null;
            var request = new BuildRequest
            {
                Plan = _plan,
                Recipe = _recipe.Clone(),
                Area = _shape,
                ReplaceGroup = _editingGroup,
            };
            bool started;
            try { started = svc.Start(request); }
            catch (Exception e)
            {
                Mod.Log.Error(e, "Placemaker: build service failed to start");
                started = false;
            }
            if (!started)
            {
                BuildStatus st = default;
                try { st = svc.Status; } catch (Exception) { }
                _buildFailure = new PlanIssue
                {
                    Severity = IssueSeverity.Error,
                    Key = string.IsNullOrEmpty(st.MessageKey) ? "Placemaker.Issue.BuildRefused" : st.MessageKey,
                    Args = st.MessageArgs ?? Array.Empty<object>(),
                };
                Flash(new LocText(_buildFailure.Key, _buildFailure.Args));
                return;
            }
            _building = true;
            _buildStatus = default;
            CancelGesture();
            ExitMarkerMode();
            SetStage(ToolStage.Building);
        }

        private void CancelBuild()
        {
            try { Services.Build?.Cancel(); } catch (Exception e) { Mod.Log.Warn("Placemaker: cancel failed: " + e.Message); }
        }

        private void UpdateBuild()
        {
            if (!_building) return;
            var svc = Services.Build;
            if (svc == null)
            {
                _building = false;
                _buildFailure = new PlanIssue { Severity = IssueSeverity.Error, Key = "Placemaker.Reason.BuildUnavailable" };
                SetStage(ToolStage.Review);
                return;
            }
            BuildStatus st;
            try { st = svc.Status; }
            catch (Exception e) { Mod.Log.Warn("Placemaker: build status failed: " + e.Message); return; }
            if (st.Progress != _buildStatus.Progress || st.Phase != _buildStatus.Phase) Touch();
            _buildStatus = st;
            switch (st.Phase)
            {
                case BuildPhase.Done:
                    Acknowledge(svc);
                    _building = false;
                    OnBuilt(st.GroupId);
                    break;
                case BuildPhase.Failed:
                    Acknowledge(svc);
                    _building = false;
                    _buildFailure = new PlanIssue
                    {
                        Severity = IssueSeverity.Error,
                        Key = string.IsNullOrEmpty(st.MessageKey) ? "Placemaker.Issue.BuildFailed" : st.MessageKey,
                        Args = st.MessageArgs ?? Array.Empty<object>(),
                        At = st.ErrorPositions != null && st.ErrorPositions.Count > 0 ? st.ErrorPositions[0] : (float3?)null,
                    };
                    _buildErrors = st.ErrorPositions != null ? new List<float3>(st.ErrorPositions) : null;
                    SetStage(ToolStage.Review);
                    Flash(new LocText(_buildFailure.Key, _buildFailure.Args), 8f);
                    break;
                case BuildPhase.Cancelled:
                    Acknowledge(svc);
                    _building = false;
                    SetStage(ToolStage.Review);
                    Flash(new LocText("Placemaker.Hint.BuildCancelled"));
                    break;
                case BuildPhase.Idle:
                    // Service reset without telling us (e.g. game loaded): give the tool back.
                    _building = false;
                    SetStage(ToolStage.Review);
                    break;
            }
        }

        /// <summary>
        /// The build service activates its own invisible tool while it builds (CS2 applies temporaries
        /// through the ACTIVE tool's applyMode — docs/proposals/gamebuild.md), so this tool is stopped
        /// for those frames. Our state is kept (OnStopRunning resets nothing persistent) and the UI
        /// system keeps polling the build through this, so progress and the result still arrive.
        /// </summary>
        public void PollBuildWhileInactive()
        {
            if (!_building || IsActive) return;
            try
            {
                UpdateBuild();
                // Finished while another tool is active: give ignoreErrors back now.
                if (!_building && !IsActive && _anarchyApplied && !(m_ToolSystem.activeTool is global::Placemaker.Game.Build.BuildToolSystem)) ApplyAnarchy(false);
            }
            catch (Exception e) { Mod.Log.Warn("Placemaker: build poll failed: " + e.Message); }
        }

        /// <summary>True while the build service holds the tool slot for us (we will be reactivated).</summary>
        public bool BuildHoldsTool => _building && Services.Build is global::Placemaker.Game.Build.BuildService bs && bs.HoldsTool;

        private static void Acknowledge(IBuildService svc)
        {
            try { svc.Acknowledge(); } catch (Exception e) { Mod.Log.Warn("Placemaker: acknowledge failed: " + e.Message); }
        }

        /// <summary>Built: the tool stays open with a fresh selection; design and recipe stay for the next one.</summary>
        private void OnBuilt(int groupId)
        {
            ClearSelectionState();
            _roads.InvalidateGraph();   // the new roads must be pickable right away
            if (_recipe != null) foreach (var kv in RecipePaths.Walk(_recipe)) kv.Value.Markers.Clear();
            _editingGroup = 0;
            _plan = null;
            _planRevision = -1;
            _requestedRevision = -1;
            DesignChanged();
            _history.Reset(Capture());
            SetStage(ToolStage.Area);
            Flash(new LocText("Placemaker.Hint.Built"), 5f);
            Mod.Log.Info("Placemaker: built group " + groupId);
        }

        // ───────────── manage ─────────────

        private void UpdateManage()
        {
            _cursorValid = _in.HasHit;
            if (_in.HasHit) _cursor = _in.Hit;
            int group = 0;
            var groups = Services.Groups;
            if (groups != null && _in.HasHit)
            {
                // The raycast may return a sub-element; walk a few owners up.
                var e = _in.HitEntity;
                for (int depth = 0; depth < 4 && e != Entity.Null && group == 0; depth++)
                {
                    try { group = groups.GroupOf(e); } catch (Exception) { group = 0; }
                    if (group != 0) break;
                    e = EntityManager.HasComponent<Owner>(e) ? EntityManager.GetComponentData<Owner>(e).m_Owner : Entity.Null;
                }
            }
            if (group != _hoverGroup) { _hoverGroup = group; Touch(); }
            if (_in.LeftPressed) ManageGroup(_hoverGroup);
            else if (_in.RightPressed && _managedGroup != 0) ManageGroup(0);
        }

        public void ManageGroup(int groupId)
        {
            if (groupId != 0 && Services.Groups?.Get(groupId) == null) groupId = 0;
            if (groupId == _managedGroup) return;
            _managedGroup = groupId;
            _membersGroup = -1;
            if (_stage != ToolStage.Manage && groupId != 0) SetStage(ToolStage.Manage);
            Touch();
        }

        /// <summary>Members of the hovered/managed group for highlighting (cached per group).</summary>
        public IReadOnlyList<Entity> GroupMembers(int groupId)
        {
            if (groupId == 0 || Services.Groups == null) { _members.Clear(); _membersGroup = -1; return _members; }
            if (_membersGroup == groupId) return _members;
            _members.Clear();
            try { Services.Groups.GetMembers(groupId, _members); } catch (Exception) { _members.Clear(); }
            _membersGroup = groupId;
            return _members;
        }

        /// <summary>Loads a built group's receipt (recipe + area) for editing; building then replaces the group.</summary>
        public void EditGroup(int groupId)
        {
            var groups = Services.Groups;
            if (groups == null) { Flash(new LocText("Placemaker.Reason.GroupsUnavailable")); return; }
            GroupReceipt receipt;
            try { receipt = groups.GetReceipt(groupId); }
            catch (Exception e) { Mod.Log.Warn("Placemaker: receipt failed: " + e.Message); receipt = null; }
            if (receipt?.Recipe == null || receipt.Area?.Ring == null || receipt.Area.Ring.Length < 3)
            {
                Flash(new LocText("Placemaker.Problem.CannotEdit"));
                return;
            }
            CancelGesture();
            ExitMarkerMode();
            ClearSelectionState();
            // Whatever the original selection mode, the outline comes back as editable corners.
            _mode = SelectionMode.Draw;
            _draw.SetRing(receipt.Area.Ring);
            _shape = receipt.Area;
            _shapeDirty = false;
            ShapeChanged(alreadyBuilt: true);
            _recipe = receipt.Recipe.Clone();
            _recipeId = _recipes.Find(receipt.Recipe.Id) != null ? receipt.Recipe.Id : "";
            _focus = RecipePaths.Root;
            _editingGroup = groupId;
            _managedGroup = 0;
            DesignChanged();
            _history.Reset(Capture());
            SetStage(ToolStage.Design);
        }

        public void RemoveGroup(int groupId)
        {
            var groups = Services.Groups;
            if (groups == null) { Flash(new LocText("Placemaker.Reason.GroupsUnavailable")); return; }
            bool ok;
            try { ok = groups.RequestRemove(groupId); }
            catch (Exception e) { Mod.Log.Warn("Placemaker: remove failed: " + e.Message); ok = false; }
            if (!ok) { Flash(new LocText("Placemaker.Problem.RemoveFailed")); return; }
            if (_managedGroup == groupId) _managedGroup = 0;
            if (_hoverGroup == groupId) _hoverGroup = 0;
            if (_editingGroup == groupId) _editingGroup = 0;
            _membersGroup = -1;
            Flash(new LocText("Placemaker.Hint.Removed"));
            Touch();
        }

        public void RenameGroup(int groupId, string name)
        {
            try { Services.Groups?.Rename(groupId, (name ?? "").Trim()); }
            catch (Exception e) { Mod.Log.Warn("Placemaker: rename failed: " + e.Message); }
            Touch();
        }

        /// <summary>Options page: remove every group Placemaker built.</summary>
        public int RemoveEverything()
        {
            var groups = Services.Groups;
            if (groups == null) return 0;
            int n = 0;
            try
            {
                foreach (var id in new List<int>(groups.AllGroups))
                    if (groups.RequestRemove(id)) n++;
            }
            catch (Exception e) { Mod.Log.Error(e, "Placemaker: remove everything failed"); }
            _managedGroup = _hoverGroup = _editingGroup = 0;
            Touch();
            Mod.Log.Info("Placemaker: requested removal of " + n + " groups");
            return n;
        }
    }
}
