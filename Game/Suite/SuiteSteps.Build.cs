// In-game test suite: build, select, delete (panel, bulldozer, info panel), restore, grow — and the probes after each,
// each wrapped on its own so a suite bug becomes an 'unknown (suite error)' verdict, never a Placemaker failure.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Game.Common;
using Game.Tools;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Placemaker.Core;
using Placemaker.Core.Suite;
using Placemaker.Core.Tool;
using Placemaker.Game.Groups;
using Placemaker.Game.Tool;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Placemaker.Game.Suite
{
    public sealed partial class SuiteRunner
    {
        /// <summary>
        /// Runs measurements with the build framed the standard way (oblique, the same distance every time), so what is
        /// "in view and big enough to be drawn" means the same for the ghost, the result and after a grow; the camera
        /// goes back to where it was afterwards.
        /// </summary>
        private IEnumerator Framed(ScenarioState st, JObject rec, params IEnumerator[] body)
        {
            var c = _world.GetExistingSystemManaged<global::Game.Rendering.CameraUpdateSystem>()?.gamePlayController;
            Vector3 pivot = c != null ? c.pivot : Vector3.zero;
            float2 angle = c != null ? c.angle : float2.zero;
            float zoom = c != null ? c.zoom : 0f;
            var crec = new JObject();
            bool ok = false;
            if (st.Ring != null) yield return CameraStep(st, new JObject { ["target"] = "build", ["view"] = "oblique" }, crec, x => ok = x);
            yield return Frames(20);   // culling and batches for the new view
            rec["measuredAt"] = crec["camera"]?["effective"]?.DeepClone();
            foreach (var b in body) yield return b;
            if (c != null)
            {
                SuiteCapture.Fov(null);
                SuiteCapture.Aim(new float3(pivot.x, pivot.y, pivot.z), angle.x, angle.y, zoom);
                Event("camera");
                yield return Frames(5);
            }
        }

        private IEnumerator Do(Action a) { a(); yield break; }

        /// <summary>Suite work measured into the frame-time accounting (it is not Placemaker's).</summary>
        private T Timed<T>(Func<T> f)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try { return f(); }
            finally { _probeMsThisFrame += (float)sw.Elapsed.TotalMilliseconds; }
        }

        /// <summary>Runs one probe; an exception becomes {"suiteError": ...} in the result, never a step failure.</summary>
        private JToken Probe(string what, Func<JToken> f, JObject rec)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try { return f(); }
            catch (Exception e)
            {
                Say($"probe {what} failed: {e}");
                return new JObject { ["suiteError"] = $"{what}: {e.GetType().Name}: {e.Message}" };
            }
            finally
            {
                var p = rec["probeMs"] as JObject ?? (JObject)(rec["probeMs"] = new JObject());
                p[what] = Math.Round(sw.Elapsed.TotalMilliseconds);
                _probeMsThisFrame += (float)sw.Elapsed.TotalMilliseconds;
            }
        }

        private IEnumerator StepBuild(ScenarioState st, JObject step, JObject rec)
        {
            var res = st.Result;
            string when = ((string)step["when"] ?? (st.Pace == "user" ? "planReady" : "settled")).ToLowerInvariant();
            rec["when"] = when;
            if (_tool.Stage != ToolStage.Review)
            {
                var r = new JObject();
                var sub = rec["sub"] as JArray ?? (JArray)(rec["sub"] = new JArray());
                sub.Add(r);
                if (when == "settled") yield return StepPreview(st, step, r);
                else
                {
                    // a person pressing Next then Build at once (Enter, Enter): no wait for the ghost
                    yield return WaitPlan(st, r, always: true);
                    var p = _tool.SuitePrimaryInfo();
                    if (p.Kind == "toReview" && p.Enabled) yield return UiPrimary(st, r, "Next");
                    st.Plan = _tool.Plan;
                    st.PermanentTerraform = st.Plan != null && st.Plan.Layers.Any(l => l.Terraform.Any(t => t.Permanent));
                }
                if (r["error"] != null) { rec["error"] = r["error"]; yield break; }
            }
            if (when == "settled")
            {
                yield return WaitPlan(st, rec, always: true);
                if (_tool.PreviewModeSelected == PreviewMode.Ghost && (_tool.GhostPending || _tool.PreviewModeEffective != PreviewMode.Ghost)) yield return WaitGhost(rec);
            }
            else if (when == "planready") yield return WaitPlan(st, rec, always: true);
            else if (when == "ghostpending")
            {
                yield return Frames(1);
                rec["ghostPendingAtPress"] = _tool.GhostPending;
            }
            st.Plan = _tool.Plan ?? st.Plan;
            var primary = _tool.SuitePrimaryInfo();
            if (!primary.Enabled && primary.Reason == "Placemaker.Reason.AcknowledgePermanent")
            {
                yield return UiAct(st, rec, "OK, permanent", "ack-permanent", "acknowledgePermanent", new object[] { true }, () => _tool.PermanentAcknowledged, () => _tool.AcknowledgePermanent(true));
                primary = _tool.SuitePrimaryInfo();
                rec["acknowledgedPermanent"] = true;
            }
            if ((primary.Kind != "build" && primary.Kind != "finishEdit") || !primary.Enabled)
            {
                rec["error"] = "build button: " + primary.Kind + (primary.Enabled ? "" : " disabled") + " " + _tool.SuiteUnreadyText();
                res["status"] = "blocked";
                res["blockedAt"] = "review";
                res["unready"] = _tool.SuiteUnreadyText();
                yield break;
            }
            int doneBefore = _tool.SuiteBuildsDone;
            st.BuiltRevision = _tool.PlanRevision;
            rec["planRevisionBuilt"] = st.BuiltRevision;
            bool rebuild = _tool.EditingGroup != 0;
            float t0 = Time.realtimeSinceStartup;
            string via = ((string)step["via"] ?? "panel").ToLowerInvariant();
            if (via == "enter") { yield return KeyPress("enter"); rec["ui"] = new JArray(new JObject { ["action"] = "Build", ["path"] = "key Enter" }); }
            else yield return UiPrimary(st, rec, "Build");
            if (rec["error"] != null) { res["status"] = "blocked"; res["blockedAt"] = "review"; yield break; }
            yield return WaitFor(rec, "buildStart", () => _tool.Building || _tool.SuiteBuildsDone != doneBefore, 10f);
            yield return WaitFor(rec, "build", () => !_tool.Building, 300f);
            bool buildTimedOut = _timedOut;
            res["buildMs"] = Math.Round((Time.realtimeSinceStartup - t0) * 1000f);
            rec["buildPath"] = _tool.SuiteLastBuildPath;
            if (_tool.SuiteBuildsDone == doneBefore)
            {
                var f = _tool.BuildFailure;
                res["status"] = "build-failed";
                res["failure"] = f == null ? (buildTimedOut ? "timeout" : "unknown") : f.Key + (f.Args != null && f.Args.Length > 0 ? " (" + string.Join(", ", f.Args) + ")" : "");
                rec["error"] = "build failed: " + res["failure"];
                if (buildTimedOut) _abortBatch = "a build did not finish in 300 s: the rest of the batch would run into it";
                yield break;
            }
            Event("build");
            st.Built = true;
            st.Deleted = false;
            st.BuildCount++;
            st.Group = _tool.SuiteLastBuiltGroup;
            rec["group"] = st.Group;
            res["status"] = "built";
            res["buildPath"] = _tool.SuiteLastBuildPath;
            res["notes"] = new JArray(_tool.BuildNotes.Select(n => n.Key + (n.Args != null && n.Args.Length > 0 ? " (" + string.Join(", ", n.Args) + ")" : "")));
            // the world settles: no temporaries left, the members' visibility unchanged over 5 polls spanning at least 3 s
            yield return WaitFor(rec, "settle", () => TempCount() == 0, 30f);
            yield return Framed(st, rec, BuildVisibleSettled(st, rec), GroundStable(st, rec), Frames(1), Judge(st, rec, rebuild ? "rebuild" : "build"));
        }

        /// <summary>The probes after a build (or rebuild), recorded per build in result.builds[] and as the scenario's latest.</summary>
        private IEnumerator Judge(ScenarioState st, JObject rec, string why)
        {
            var res = st.Result;
            var build = new JObject { ["n"] = st.BuildCount, ["step"] = st.StepIndex, ["why"] = why, ["group"] = st.Group, ["buildPath"] = _tool.SuiteLastBuildPath, ["buildMs"] = res["buildMs"] };
            var members = _probe.MemberSet(st.Group);
            build["members"] = members.Count;
            if (st.Before != null)
            {
                SuiteLanes lanes = null;
                build["lanes"] = Probe("lanes", () =>
                {
                    lanes = new SuiteLanes(_world.EntityManager, _probe);
                    lanes.Build(st.Before.NMin - 50f, st.Before.NMax + 50f, members, st.Group);
                    return lanes.Stats();
                }, rec);
                var markers = st.Markers.Select(m => (m.At, m.Kind)).ToList();
                build["terrain"] = Probe("pits", () => _probe.Pits(st.Before, _probe.GroundNow(st.Before), st.Plan), rec);
                build["orphans"] = Probe("orphans", () => _probe.Orphans(st.Before, st.Group), rec);
                build["census"] = Probe("census", () => _probe.Census(st.Before, st.Group, st.Plan, st.S.Layer, _tool.BuildAudit, lanes, markers), rec);
                if (st.Ghost != null && st.GhostRevision != st.BuiltRevision)
                    build["previewVsResult"] = new JObject { ["stale"] = true, ["detail"] = $"the ghost census was taken for plan revision {st.GhostRevision}, the build was of revision {st.BuiltRevision} (no ghost of the built plan was shown)" };
                else if (st.Ghost != null)
                    build["previewVsResult"] = Probe("previewVsResult", () =>
                    {
                        var (min, max) = Bounds(st.Ring, 12f);
                        var seen = _probe.SeenByPrefab(members, st.Group, min, max);
                        var o = new JObject();
                        var short_ = new JObject();
                        int missing = 0;
                        foreach (var kv in st.Ghost.OrderByDescending(kv => kv.Value))
                        {
                            seen.TryGetValue(kv.Key, out int n);
                            if (n < kv.Value) { missing += kv.Value - n; if (short_.Count < 20) short_[kv.Key] = $"{n} seen of {kv.Value} in the ghost"; }
                        }
                        o["ghostObjects"] = st.GhostTotal;
                        o["seenObjects"] = seen.Values.Sum();
                        o["missing"] = missing;
                        o["short"] = short_;
                        return o;
                    }, rec);
            }
            build["visibility"] = Probe("visibility", () => _probe.Visibility(members), rec);
            st.Builds.Add(build);
            foreach (var key in new[] { "terrain", "census", "visibility", "previewVsResult", "orphans" }) if (build[key] != null) res[key] = build[key].DeepClone();
            if (build["census"] is JObject c && c["entrances"] != null) res["entrances"] = c["entrances"].DeepClone();
            yield return Frames(1);   // the probes' frame is not the next step's
        }

        /// <summary>After a build: the members' visibility (seen / overridden / hidden / not rendered) unchanged over 5 polls, at least 3 s.</summary>
        private IEnumerator BuildVisibleSettled(ScenarioState st, JObject rec)
        {
            float t0 = Time.realtimeSinceStartup;
            string last = null;
            int same = 0;
            bool ok = false;
            var members = _probe.MemberSet(st.Group);
            while (Time.realtimeSinceStartup - t0 < 30f)
            {
                yield return Seconds(0.6f);
                var v = Timed(() => _probe.Visibility(members));
                string key = $"{v["visible"]}/{v["overridden"]}/{v["hidden"]}/{v["notRendered"]}/{v["deleted"]}/{members.Count}";
                same = key == last ? same + 1 : 0;
                last = key;
                if (same >= 4 && Time.realtimeSinceStartup - t0 >= 3f) { ok = true; break; }
            }
            Waited(rec, "visibleSettled", Time.realtimeSinceStartup - t0, ok ? 0f : 30f);
        }

        /// <summary>The ground stops changing (two readings 10 frames apart identical, max 10 s) before the pit check.</summary>
        private IEnumerator GroundStable(ScenarioState st, JObject rec)
        {
            if (st.Before == null) yield break;
            float t0 = Time.realtimeSinceStartup;
            List<float> last = null;
            bool ok = false;
            while (Time.realtimeSinceStartup - t0 < 10f)
            {
                var now = Timed(() => _probe.GroundNow(st.Before));
                if (last != null && now.Count == last.Count && !now.Where((h, i) => math.abs(h - last[i]) > 0.01f).Any()) { ok = true; break; }
                last = now;
                yield return Frames(10);
            }
            Waited(rec, "groundStable", Time.realtimeSinceStartup - t0, ok ? 0f : 10f);
        }

        // ───────────── select (the Manage stage: a click on the build) ─────────────

        /// <summary>A person clicks the build: Manage stage, the game's raycast (lots, nets, objects) at its centre on the screen.</summary>
        private IEnumerator SelectBuild(ScenarioState st, JObject rec)
        {
            if (st.Group == 0 || Services.Groups?.Get(st.Group) == null) { rec["error"] = "no build to select"; yield break; }
            if (_tool.Stage != ToolStage.Manage)
            {
                yield return UiGoToStage(rec, ToolStage.Manage);
                if (_tool.Stage != ToolStage.Manage) { yield return KeyPress("esc"); }
            }
            if (_tool.Stage != ToolStage.Manage) { rec["error"] = "cannot reach the Manage stage from " + _tool.Stage; yield break; }
            yield return Frames(3);   // the Manage raycast masks are in place
            // a person looks at the build to click it: when its centre is not comfortably on the screen, frame it first
            if (st.Ring != null)
            {
                var c3 = P3(st.Ring.Aggregate(float2.zero, (a2, p2) => a2 + p2) / st.Ring.Length);
                yield return ReadPanelRect();
                if (Clickable(c3, out var sc3) != null || sc3.y < Screen.height * 0.2f || sc3.y > Screen.height * 0.8f)
                {
                    var crec = new JObject();
                    bool okc = false;
                    yield return CameraStep(st, new JObject { ["target"] = "build", ["view"] = "oblique" }, crec, x => okc = x);
                    var implicitSteps = rec["implicit"] as JArray ?? (JArray)(rec["implicit"] = new JArray());
                    implicitSteps.Add("camera build oblique (to see the build before clicking it)");
                }
            }
            // aim at a visible member: the owner lot's centre, else the outline's centre
            float3 target = st.Ring != null ? P3(st.Ring.Aggregate(float2.zero, (a, p) => a + p) / st.Ring.Length) : st.Focus;
            int g = st.Group;
            int tries = 0;
            foreach (var cand in SelectTargets(st, target))
            {
                tries++;
                RaycastHit? hit = null;
                yield return Press(st, rec, cand, false, h => hit = h);
                if (rec["error"] != null && tries < 4) { rec.Remove("error"); continue; }
                yield return Frames(3);
                if (_tool.ManagedGroup == g) { rec["selected"] = g; rec["selectClicks"] = tries; yield break; }
                rec["selectMiss" + tries] = $"clicked ({cand.x:F0},{cand.z:F0}): managed {_tool.ManagedGroup}, hover {_tool.HoverGroup}";
                if (tries >= 4) break;
            }
            rec["error"] = $"one click on the build did not select it (group {g}; {tries} tries)";
        }

        private IEnumerable<float3> SelectTargets(ScenarioState st, float3 centre)
        {
            yield return centre;
            if (st.Ring == null) yield break;
            var (min, max) = Bounds(st.Ring, -8f);
            yield return P3(new float2(math.lerp(min.x, max.x, 0.3f), math.lerp(min.y, max.y, 0.3f)));
            yield return P3(new float2(math.lerp(min.x, max.x, 0.7f), math.lerp(min.y, max.y, 0.6f)));
            yield return P3(new float2(math.lerp(min.x, max.x, 0.5f), math.lerp(min.y, max.y, 0.8f)));
        }

        private IEnumerator StepSelect(ScenarioState st, JObject step, JObject rec)
        {
            yield return SelectBuild(st, rec);
        }

        // ───────────── delete ─────────────

        private IEnumerator StepDelete(ScenarioState st, JObject step, JObject rec)
        {
            if (!st.Built || st.Group == 0) { rec["error"] = "nothing built to delete (loose builds have no group)"; yield break; }
            int g = st.Group;
            // a person clicks the build, then Remove in its panel, then OK
            yield return SelectBuild(st, rec);
            if (rec["error"] != null) yield break;
            yield return UiAct(st, rec, "Remove", "manage-remove", null, null, null, null, mustChange: false, effectFrames: 4);
            yield return UiAct(st, rec, "OK (remove)", "confirm-ok", "removeGroup", new object[] { (double)g }, () => Services.Groups?.Get(g) == null || RemovalPending(g), () => _tool.RemoveGroup(g), mustChange: false, effectFrames: 10);
            yield return AfterDelete(st, rec, g, step);
        }

        private bool RemovalPending(int g)
        {
            try { return Services.Groups?.Get(g) == null; } catch (Exception) { return false; }
        }

        /// <summary>Waits until the group is gone and the neighbourhood stopped changing, then records what the delete left (before any restore).</summary>
        private IEnumerator AfterDelete(ScenarioState st, JObject rec, int g, JObject step)
        {
            yield return WaitFor(rec, "delete", () => Services.Groups == null || Services.Groups.Get(g) == null, 60f);
            bool gone = !_timedOut;
            yield return WaitFor(rec, "settle", () => TempCount() == 0, 20f);
            Event("delete");
            // poll the neighbourhood until three prints in a row agree (removal runs over several passes)
            if (st.Before != null)
            {
                string last = null;
                int same = 0;
                float t0 = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - t0 < 15f && same < 2)
                {
                    yield return Frames(10);
                    var k = Timed(() => _probe.PrintKey(_probe.Fingerprint(st.Before.NMin, st.Before.NMax, st.Before.GrowStamp != _probe.GrowStamp)));
                    same = k == last ? same + 1 : 0;
                    last = k;
                }
                Waited(rec, "deleteStable", Time.realtimeSinceStartup - t0, same >= 2 ? 0f : 15f);
            }
            st.Deleted = gone;
            st.Built = !gone;
            st.Result["status"] = gone ? "deleted" : "delete-failed";
            if (!gone) rec["error"] = $"the build (group {g}) is still there 60 s after the delete";
            if (_tool.Stage == ToolStage.Manage) { yield return KeyPress("esc"); }
            if (st.Before != null)
            {
                var lb = Probe("leftBehind", () => _probe.LeftBehind(st.Before), rec) as JObject;
                if (lb != null)
                {
                    lb["ground"] = Probe("groundAfterDelete", () => _probe.GroundChanged(st.Before), rec);
                    lb["permanentTerraform"] = st.PermanentTerraform;
                }
                rec["leftBehind"] = lb;
                st.LastLeftBehind = lb;
            }
        }

        /// <summary>The bulldozer's way: a delete definition for the build's owner lot (what BulldozeToolSystem applies), then the group's cascade.</summary>
        private IEnumerator StepBulldoze(ScenarioState st, JObject step, JObject rec)
        {
            if (!st.Built || st.Group == 0) { rec["error"] = "nothing built to bulldoze"; yield break; }
            int g = st.Group;
            var em = _world.EntityManager;
            var members = _probe.MemberSet(g);
            string target = ((string)step["target"] ?? "owner").ToLowerInvariant();
            Entity victim = Entity.Null;
            foreach (var m in members)
            {
                if (!em.Exists(m) || em.HasComponent<Deleted>(m) || !em.HasComponent<PlacemakerMember>(m)) continue;
                var role = em.GetComponentData<PlacemakerMember>(m).Role;
                if (target == "owner" && role == MemberRole.OwnerArea) { victim = m; break; }
                if (target == "part" && role == MemberRole.Generic && em.HasComponent<global::Game.Objects.Object>(m)) { victim = m; break; }
            }
            if (victim == Entity.Null) { rec["error"] = $"no {target} member to bulldoze"; yield break; }
            rec["bulldozed"] = _probe.KindOf(victim) + " " + _probe.PrefabName(victim);
            yield return CloseTool();
            yield return ApplyWithFixtureTool(t =>
            {
                var def = t.NewDefinition();
                em.AddComponentData(def, new CreationDefinition { m_Original = victim, m_Flags = CreationFlags.Delete });
                em.AddComponent<Updated>(def);
                if (em.HasBuffer<global::Game.Areas.Node>(victim))
                {
                    var src = em.GetBuffer<global::Game.Areas.Node>(victim, true).ToNativeArray(Allocator.Temp);
                    em.AddBuffer<global::Game.Areas.Node>(def).CopyFrom(src);
                    src.Dispose();
                }
                else if (em.HasComponent<global::Game.Objects.Transform>(victim))
                {
                    var tr = em.GetComponentData<global::Game.Objects.Transform>(victim);
                    em.AddComponentData(def, new ObjectDefinition { m_Position = tr.m_Position, m_Rotation = tr.m_Rotation, m_LocalPosition = tr.m_Position, m_LocalRotation = tr.m_Rotation, m_ParentMesh = -1, m_Scale = new float3(1f), m_Intensity = 1f, m_PrefabSubIndex = -1 });
                }
                return 1;
            }, "bulldoze " + rec["bulldozed"]);
            rec["via"] = "the bulldozer's delete definition (applied through the suite's fixture tool, not the bulldozer UI)";
            yield return AfterDelete(st, rec, g, step);
            yield return OpenTool();
        }

        /// <summary>The vanilla selected-info panel's Edit / Remove, with the Placemaker tool closed (how a person finds a build later).</summary>
        private IEnumerator StepInfo(ScenarioState st, JObject step, JObject rec, bool edit)
        {
            if (!st.Built || st.Group == 0) { rec["error"] = "nothing built"; yield break; }
            int g = st.Group;
            yield return CloseTool();
            var em = _world.EntityManager;
            Entity owner = _probe.MemberSet(g).FirstOrDefault(m => em.Exists(m) && em.HasComponent<PlacemakerMember>(m) && em.GetComponentData<PlacemakerMember>(m).Role == MemberRole.OwnerArea);
            if (owner == Entity.Null) owner = _probe.MemberSet(g).FirstOrDefault(m => em.Exists(m) && !em.HasComponent<Deleted>(m));
            var sel = _world.GetExistingSystemManaged<global::Game.UI.InGame.SelectedInfoUISystem>();
            try { sel?.SetSelection(owner); } catch (Exception e) { rec["selectError"] = e.Message; }
            yield return Frames(20);
            rec["selected"] = _probe.KindOf(owner) + " " + _probe.PrefabName(owner);
            if (edit)
            {
                yield return UiAct(st, rec, "info Edit", "info-edit", "editGroup", new object[] { (double)g }, () => _tool.EditingGroup == g, () => _tool.EditGroup(g), mustChange: false, effectFrames: 20);
                yield return Frames(10);
                var state = Bound(rec, "state");
                bool open = (bool?)state?["open"] ?? false;
                rec["toolActive"] = _tool.IsActive;
                rec["panelOpen"] = open;
                rec["stage"] = _tool.Stage.ToString();
                rec["editing"] = _tool.EditingGroup;
                if (_tool.EditingGroup != g) rec["error"] = "Edit in the info panel did not load the build";
                else if (!_tool.IsActive || !open) rec["error"] = $"Edit in the info panel loaded the build but the tool is {(_tool.IsActive ? "active" : "closed")} and the panel {(open ? "open" : "not shown")}: a dead button for the player";
                if (!_tool.IsActive) yield return OpenTool();
                if (_tool.EditingGroup == g) st.Ring = _tool.Shape?.Ring2D() ?? st.Ring;
            }
            else
            {
                yield return UiAct(st, rec, "info Remove", "info-remove", null, null, null, null, mustChange: false, effectFrames: 4);
                yield return UiAct(st, rec, "OK (remove)", "confirm-ok", "removeGroup", new object[] { (double)g }, () => Services.Groups?.Get(g) == null, () => _tool.RemoveGroup(g), mustChange: false, effectFrames: 10);
                yield return AfterDelete(st, rec, g, step);
                yield return OpenTool();
            }
            try { sel?.SetSelection(Entity.Null); } catch (Exception) { }
        }

        // ───────────── restore ─────────────

        private IEnumerator StepRestore(ScenarioState st, JObject rec)
        {
            var b = st.S.Block;
            float t0 = Time.realtimeSinceStartup;
            int frame0 = Time.frameCount;
            string err = _fixture.RestoreBlock(b);
            // the ground is back when the block's heights match the layout (CPU heights arrive by readback), max 10 s
            float worst = float.MaxValue;
            float2 worstAt = default;
            while (err == null && Time.realtimeSinceStartup - t0 < 10f)
            {
                yield return Frames(5);
                float2 wa = default;
                worst = Timed(() => _fixture.BlockGroundError(b, _fixture.HalfWidth, out wa));
                worstAt = wa;
                if (worst <= 0.25f) break;
            }
            yield return Frames(16);   // terrain cascades and splat redraw
            Waited(rec, "restore", Time.realtimeSinceStartup - t0, worst <= 0.25f ? 0f : 10f);
            Event("restore");
            st.Restored = true;
            rec["restored"] = err ?? b.Id;
            rec["groundErrorM"] = worst == float.MaxValue ? null : (JToken)Math.Round(worst, 2);
            rec["restoreFrames"] = Time.frameCount - frame0;
            var lb = st.LastLeftBehind;
            int left = lb != null ? (int?)lb["count"] ?? 0 : -1;
            int cells = lb != null ? (int?)lb["zoneCellsChanged"] ?? 0 : 0;
            if (err == null && st.Deleted && worst <= 0.25f && left == 0 && cells == 0) { _fixture.Release(b); rec["released"] = true; }
            else
            {
                string why = err ?? (worst > 0.25f ? $"ground off by {worst:F2} m at ({worstAt.x:F0},{worstAt.y:F0})" : left != 0 ? $"{left} things left behind" : cells != 0 ? $"{cells} zone cells changed" : "the build was not deleted");
                _fixture.Quarantine(b, why);
                rec["quarantined"] = why;
                if (err != null || worst > 0.25f) rec["error"] = "restore: " + why;
            }
        }

        // ───────────── grow ─────────────

        private IEnumerator StepGrow(ScenarioState st, JObject step, JObject rec)
        {
            bool fast = (bool?)step["fast"] ?? true;
            yield return Grow((float?)step["seconds"] ?? 30f, "grow-" + st.S.Id + "-" + st.StepIndex, fast);
            rec["grow"] = _summary["grow-" + st.S.Id + "-" + st.StepIndex];
            Event("grow");
            if (st.Before != null && st.Built)
            {
                var members = _probe.MemberSet(st.Group);
                var gr = Probe("growables", () => _probe.Growables(st.Before, st.Group, members, st.Plan), rec);
                rec["growables"] = gr;
                st.Result["growablesAtStep"] = gr.DeepClone();
                yield return Framed(st, rec, Do(() => rec["visibilityAfterGrow"] = Probe("visibility", () => _probe.Visibility(members), rec)));
                st.Result["visibilityAfterGrow"] = rec["visibilityAfterGrow"].DeepClone();
                rec["existingAfterGrow"] = Probe("existing", () => _probe.Census(st.Before, st.Group, st.Plan, st.S.Layer, null, null, null)["existingBuildings"], rec);
            }
        }

        /// <summary>Compares two builds of this scenario (result.builds[a] and [b]): members, seen objects, census kinds, entrances.</summary>
        private void StepCompare(ScenarioState st, JObject step, JObject rec)
        {
            int a = (int?)step["a"] ?? 1, b = (int?)step["b"] ?? 2;
            var builds = st.Builds.OfType<JObject>().ToList();
            var A = builds.FirstOrDefault(x => (int)x["n"] == a);
            var B = builds.FirstOrDefault(x => (int)x["n"] == b);
            if (A == null || B == null) { rec["error"] = $"compare: builds {a} and {b} needed (this scenario has {builds.Count})"; return; }
            var d = new JObject
            {
                ["members"] = $"{A["members"]} -> {B["members"]}",
                ["seen"] = $"{A["visibility"]?["visible"]} -> {B["visibility"]?["visible"]}",
                ["notSeen"] = $"{A["visibility"]?["objects"]?.Value<int>() - A["visibility"]?["visible"]?.Value<int>()} -> {B["visibility"]?["objects"]?.Value<int>() - B["visibility"]?["visible"]?.Value<int>()}",
            };
            var ka = (A["census"]?["kinds"] as JArray)?.OfType<JObject>().ToDictionary(x => (string)x["kind"] + "|" + (string)x["prefab"], x => (int)x["seen"]) ?? new Dictionary<string, int>();
            var kb = (B["census"]?["kinds"] as JArray)?.OfType<JObject>().ToDictionary(x => (string)x["kind"] + "|" + (string)x["prefab"], x => (int)x["seen"]) ?? new Dictionary<string, int>();
            var diff = new JObject();
            foreach (var k in ka.Keys.Union(kb.Keys))
            {
                ka.TryGetValue(k, out int x);
                kb.TryGetValue(k, out int y);
                if (x != y && diff.Count < 30) diff[k] = $"{x} -> {y}";
            }
            d["seenByPrefab"] = diff;
            rec["compare"] = d;
        }
    }
}
