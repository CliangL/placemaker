// In-game test suite: scenarios as step scripts (suite/README.md has the vocabulary with an example per step).
// A scenario is a list of steps that drive the real tool the way a person does: the area tools get scripted mouse input
// (PlacemakerToolSystem.SuiteQueue: the same per-mode update code a real click or stroke runs, minus the raycast), the
// panel's actions are the same calls the UI makes (type/preset, option values, Next/Build button, Edit, Delete), entrances
// are clicks on the outline. Every wait is on readiness, never a fixed delay: the plan of the current input revision, the
// ghost emitted and settled, the camera arrived and still, textures streamed — each with a generous timeout that is
// recorded ("timed out waiting for X") and each with its duration in the step's result. Any step can take a screenshot.

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Placemaker.Core;
using Placemaker.Core.Suite;
using Placemaker.Core.Tool;
using Placemaker.Game.Tool;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Placemaker.Game.Suite
{
    public sealed partial class SuiteRunner
    {
        private sealed class ScenarioState
        {
            public Scenario S;
            public JObject Result = new JObject();
            public SiteSnapshot Before;
            public ComposedPlan Plan;
            public int Group;
            public bool Built, Deleted;
            public float2[] Ring;
            public float3 Focus;
            public float Radius = 80f;
            public int FileIndex;
            public readonly List<float3> Markers = new List<float3>();
            public JObject Shots => Result["shots"] as JObject ?? (JObject)(Result["shots"] = new JObject());
            /// <summary>Hard failure (no outline, type not selected, build failed): the remaining steps are skipped.</summary>
            public bool Stop;
        }

        private readonly Dictionary<string, ScenarioState> _byId = new Dictionary<string, ScenarioState>(StringComparer.Ordinal);
        private EntityQuery _tempQuery;

        // ───────────── defaults: the old one-line scenarios become step lists ─────────────

        private static List<JObject> DefaultSteps(Scenario s)
        {
            var steps = new List<JObject>();
            if (s.Draw != null && s.Draw.Length >= 2)
                steps.Add(new JObject { ["do"] = "area", ["tool"] = "points", ["rect"] = new JArray(s.Draw[0], s.Draw[1]) });
            else steps.Add(new JObject { ["do"] = "area", ["tool"] = "bucket" });
            steps.Add(new JObject { ["do"] = "type", ["layer"] = s.Layer, ["preset"] = s.Preset ?? "" });
            foreach (var v in s.Values) steps.Add(new JObject { ["do"] = "value", ["path"] = v.path ?? "0", ["id"] = v.id, ["json"] = v.json });
            steps.Add(new JObject { ["do"] = "entrances" });
            steps.Add(new JObject { ["do"] = "preview", ["shot"] = "preview" });
            steps.Add(new JObject { ["do"] = "build" });
            return steps;
        }

        // ───────────── one scenario ─────────────

        private IEnumerator RunScenario(ScenarioState st)
        {
            var s = st.S;
            var res = st.Result;
            var errMark = ErrorMark();
            res["id"] = s.Id;
            if (!string.IsNullOrEmpty(s.Layer)) res["layer"] = s.Layer;
            if (!string.IsNullOrEmpty(s.Preset)) res["preset"] = s.Preset;
            res["note"] = s.Note;
            res["git"] = _cfg.git;
            res["assembly"] = AssemblyVersion();
            if (s.Block == null) { res["status"] = "no-block"; yield break; }
            var b = s.Block;
            res["block"] = new JObject { ["id"] = b.Id, ["kind"] = b.Kind.ToString(), ["west"] = b.West, ["east"] = b.East, ["south"] = b.South, ["north"] = b.North, ["center"] = new JArray(b.Center.x, b.Center.y), ["reused"] = _fixture.TimesUsed(b) > 1 };
            if (st.Focus.Equals(float3.zero)) st.Focus = FocusOf(b);
            Say($"scenario {s.Id} on {b.Id} ({b.Kind}){(s.On != null ? " on scenario " + s.On : "")}");
            _byId[s.Id] = st;

            yield return OpenTool();
            if (s.On == null)
            {
                if (_tool.Stage == ToolStage.Manage) _tool.GoToStage(ToolStage.Area);
                _tool.ClearArea();
                yield return Frames(2);
            }
            PlacemakerToolSystem.SuiteResetSeed(s.Seed);

            var steps = s.Steps ?? DefaultSteps(s);
            var outSteps = new JArray();
            res["steps"] = outSteps;
            res["status"] = "running";
            for (int i = 0; i < steps.Count && !st.Stop; i++)
            {
                var step = steps[i];
                string what = ((string)step["do"] ?? "").Trim();
                var rec = new JObject { ["i"] = i, ["do"] = what };
                foreach (var p in step.Properties()) if (p.Name != "do" && p.Name != "shot" && p.Name != "camera") rec[p.Name] = p.Value.DeepClone();
                outSteps.Add(rec);
                _scenario = s.Id + "#" + i + ":" + what;
                float t0 = Time.realtimeSinceStartup;
                OpenWindow();
                yield return Guard(Step(st, step, rec), e => { rec["error"] = e.GetType().Name + ": " + e.Message; Say($"step {i} {what} threw: {e}"); });
                rec["frames"] = CloseWindow();
                rec["ms"] = Math.Round((Time.realtimeSinceStartup - t0) * 1000f);
                string shot = (string)step["shot"];
                if (!string.IsNullOrEmpty(shot) && what != "shot")
                    yield return Guard(StepShot(st, step, rec, shot), e => rec["shotError"] = e.Message);
                if (rec["error"] != null && IsHard(what)) st.Stop = true;
            }
            _scenario = s.Id;
            res["errors"] = ErrorsSince(errMark);
            if ((string)res["status"] == "running") res["status"] = st.Deleted ? "deleted" : st.Built ? "built" : "designed";
            Totals(res);
        }

        private static bool IsHard(string what) => what == "area" || what == "type" || what == "build" || what == "preview";
        private static bool Passive(string what) => what == "camera" || what == "shot" || what == "wait" || what == "grow";

        /// <summary>Sum of each wait kind over the steps (plan, ghost, camera, streaming, build) for the summary.</summary>
        private static void Totals(JObject res)
        {
            var t = new JObject();
            var timeouts = new JArray();
            if (res["steps"] is JArray steps)
                foreach (var s in steps.OfType<JObject>())
                {
                    if (s["waits"] is JObject w) foreach (var p in w.Properties()) t[p.Name] = Math.Round(((double?)t[p.Name] ?? 0) + (double)p.Value);
                    if (s["timeouts"] is JArray to) foreach (var x in to) timeouts.Add(x);
                    if (s["planComputeMs"] != null) t["planComputeMs"] = Math.Max((double?)t["planComputeMs"] ?? 0, (double)s["planComputeMs"]);
                }
            res["waitsMs"] = t;
            if (timeouts.Count > 0) res["timeouts"] = timeouts;
        }

        private IEnumerator Step(ScenarioState st, JObject step, JObject rec)
        {
            string what = (string)step["do"] ?? "";
            switch (what)
            {
                case "area": yield return StepArea(st, step, rec); break;
                case "clear": _tool.ClearArea(); yield return Frames(3); break;
                case "type": yield return StepType(st, step, rec); break;
                case "value":
                    _tool.SetValue((string)step["path"] ?? "0", (string)step["id"], step["json"] != null ? (string)step["json"] : step["value"]?.ToString(Formatting.None));
                    yield return WaitPlan(rec);
                    break;
                case "entrance": yield return StepEntrance(st, step, rec); break;
                case "entrances": yield return StepEntrancesAuto(st, step, rec); break;
                case "moveEntrance": yield return StepMoveEntrance(st, step, rec, false); break;
                case "removeEntrance": yield return StepMoveEntrance(st, step, rec, true); break;
                case "preview": yield return StepPreview(st, step, rec); break;
                case "issues": rec["issues"] = Issues(); break;
                case "edit": yield return StepEdit(st, step, rec); break;
                case "build": yield return StepBuild(st, step, rec); break;
                case "delete": yield return StepDelete(st, step, rec); break;
                case "restore": yield return StepRestore(st, rec); break;
                case "camera": yield return CameraStep(st, step, rec); break;
                case "shot": yield return StepShot(st, step, rec, (string)step["name"] ?? (string)step["shot"] ?? "shot" + rec["i"]); break;
                case "wait":
                    if (step["frames"] != null) yield return Frames((int)step["frames"]);
                    else yield return Seconds((float?)step["seconds"] ?? 1f);
                    break;
                case "grow":
                    yield return Grow((float?)step["seconds"] ?? 30f, "grow-" + st.S.Id);
                    rec["grow"] = _summary["grow-" + st.S.Id];
                    if (st.Before != null && st.Built) st.Result["growables"] = _probe.Growables(st.Before, st.Group, _probe.MemberSet(st.Group));
                    break;
                case "undo": _tool.Undo(); yield return Frames(2); yield return WaitPlan(rec); break;
                case "redo": _tool.Redo(); yield return Frames(2); yield return WaitPlan(rec); break;
                case "anarchy": _tool.SetAnarchy((bool?)step["on"] ?? true); yield return Frames(2); yield return WaitPlan(rec); break;
                case "preset": _tool.SelectPreset((string)step["layer"] ?? st.S.Layer, (string)step["preset"] ?? ""); yield return Frames(2); yield return WaitPlan(rec); break;
                default: rec["error"] = "unknown step '" + what + "'"; break;
            }
        }

        // ───────────── readiness waits (recorded) ─────────────

        private IEnumerator WaitFor(JObject rec, string name, Func<bool> ready, float timeout)
        {
            float t0 = Time.realtimeSinceStartup;
            yield return Until(ready, timeout, name);
            Waited(rec, name, Time.realtimeSinceStartup - t0, _timedOut ? timeout : 0f);
        }

        private void Waited(JObject rec, string name, float seconds, float timedOutAfter)
        {
            var w = rec["waits"] as JObject ?? (JObject)(rec["waits"] = new JObject());
            w[name] = Math.Round(((double?)w[name] ?? 0) + seconds * 1000.0);
            if (timedOutAfter > 0f)
            {
                var t = rec["timeouts"] as JArray ?? (JArray)(rec["timeouts"] = new JArray());
                t.Add($"timed out waiting for {name} after {timedOutAfter:F0} s");
            }
        }

        private bool PlanReady() => _tool.Shape == null || (!_tool.Planning && _tool.Plan != null && _tool.PlanRevision == _tool.InputRevision);

        /// <summary>The plan for the current input revision is finished (the planner idle, the plan's revision = the input's).</summary>
        private IEnumerator WaitPlan(JObject rec, float timeout = 240f)
        {
            yield return Frames(2);   // the change reaches the planner's input revision on the next tool update
            yield return WaitFor(rec, "plan", PlanReady, timeout);
            if (_tool.Plan != null) rec["planComputeMs"] = Math.Round(_tool.PlanMilliseconds);
            rec["planRevision"] = _tool.PlanRevision;
        }

        private int TempCount()
        {
            if (_tempQuery == default) _tempQuery = _world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<global::Game.Tools.Temp>());
            return _tempQuery.CalculateEntityCount();
        }

        /// <summary>The ghost is emitted (Ghost mode effective, nothing pending) and its temporaries stopped changing.</summary>
        private IEnumerator WaitGhost(JObject rec, float timeout = 120f)
        {
            float t0 = Time.realtimeSinceStartup;
            yield return Until(() => _tool.PreviewModeEffective == PreviewMode.Ghost && !_tool.GhostPending, timeout, "ghost");
            bool timedOut = _timedOut;
            int last = -1, still = 0;
            float until = Time.realtimeSinceStartup + 30f;
            while (!timedOut && still < 10 && Time.realtimeSinceStartup < until)
            {
                yield return null;
                int n = TempCount();
                if (n == last && !_tool.GhostPending) still++; else still = 0;
                last = n;
            }
            if (still < 10 && !timedOut) timedOut = true;
            Waited(rec, "ghost", Time.realtimeSinceStartup - t0, timedOut ? timeout : 0f);
            rec["ghostTemps"] = last;
            rec["ghostDropped"] = _tool.GhostDropped;
        }

        // ───────────── area tools (scripted mouse) ─────────────

        private float2 Pt(JToken t, SuiteBlock b, bool abs) =>
            t is JArray a && a.Count >= 2 ? (abs ? new float2((float)a[0], (float)a[1]) : b.Center + new float2((float)a[0], (float)a[1])) : b.Center;

        private float3 P3(float2 p) => new float3(p.x, _fixture.H(p.x, p.y), p.y);

        private IEnumerator Feed(JObject rec)
        {
            yield return WaitFor(rec, "input", () => PlacemakerToolSystem.SuiteFramesPending == 0, 20f);
            yield return Frames(3);
        }

        private IEnumerator StepArea(ScenarioState st, JObject step, JObject rec)
        {
            var b = st.S.Block;
            bool abs = (bool?)step["abs"] ?? false;
            string tool = ((string)step["tool"] ?? "bucket").ToLowerInvariant();
            // a person looks at what they select (and the tool's grab radius follows the camera distance)
            yield return CameraTo(FocusOf(b), 25f, 55f, 320f, rec);
            if (_tool.Stage == ToolStage.Manage) _tool.GoToStage(ToolStage.Area);
            _tool.ClearArea();
            yield return Frames(2);
            switch (tool)
            {
                case "bucket":
                case "block":
                    _tool.SetAreaMode(SelectionMode.Block);
                    yield return Frames(2);
                    PlacemakerToolSystem.SuiteClick(P3(Pt(step["at"], b, abs)));
                    break;
                case "points":
                case "draw":
                {
                    _tool.SetAreaMode(SelectionMode.Draw);
                    yield return Frames(2);
                    var pts = new List<float2>();
                    if (step["points"] is JArray list) foreach (var p in list) pts.Add(Pt(p, b, abs));
                    else
                    {
                        var wh = step["rect"] as JArray;
                        float w = wh != null ? (float)wh[0] : 60f, h = wh != null ? (float)wh[1] : 40f;
                        var c = Pt(step["at"], b, abs);
                        pts.Add(c + new float2(-w / 2, -h / 2)); pts.Add(c + new float2(w / 2, -h / 2)); pts.Add(c + new float2(w / 2, h / 2)); pts.Add(c + new float2(-w / 2, h / 2));
                    }
                    foreach (var p in pts) PlacemakerToolSystem.SuiteClick(P3(p));
                    PlacemakerToolSystem.SuiteClick(P3(pts[0]));   // click the first corner again: closes the outline
                    rec["clicks"] = pts.Count + 1;
                    break;
                }
                case "paint":
                case "paintanything":
                {
                    _tool.SetAreaMode(tool == "paint" ? SelectionMode.BlockPaint : SelectionMode.Paint);
                    float size = (float?)step["size"] ?? math.min(b.X1 - b.X0, b.Z1 - b.Z0) * 0.45f;
                    _tool.SetBrush((bool?)step["square"] ?? false, size, (float?)step["angle"] ?? 0f);
                    yield return Frames(2);
                    var path = new List<float3>();
                    if (step["stroke"] is JArray stroke) foreach (var p in stroke) path.Add(P3(Pt(p, b, abs)));
                    else
                    {
                        float hw = (b.X1 - b.X0) * 0.25f, hh = (b.Z1 - b.Z0) * 0.2f;
                        foreach (var d in new[] { new float2(-hw, -hh), new float2(hw, -hh), new float2(hw, hh), new float2(-hw, hh) }) path.Add(P3(b.Center + d));
                    }
                    int per = (int)math.ceil(math.max(1f, path.Count > 1 ? math.distance(path[0], path[1]) / math.max(2f, size * 0.25f) : 1f));
                    PlacemakerToolSystem.SuiteDrag(path.ToArray(), per);
                    rec["brush"] = size;
                    break;
                }
                case "nodes":
                case "perimeter":
                {
                    _tool.SetAreaMode(SelectionMode.Perimeter);
                    yield return Frames(2);
                    var targets = new List<float2>();
                    if (step["nodes"] is JArray list) foreach (var p in list) targets.Add(Pt(p, b, abs));
                    else { targets.Add(new float2(b.X0, b.Z0)); targets.Add(new float2(b.X1, b.Z0)); targets.Add(new float2(b.X1, b.Z1)); targets.Add(new float2(b.X0, b.Z1)); }
                    var nodes = new JArray();
                    var first = Entity.Null;
                    float3 firstAt = default;
                    foreach (var p in targets)
                    {
                        var n = NearestNode(p, out var at);
                        if (n == Entity.Null) { rec["error"] = $"no road node near ({p.x:F0},{p.y:F0})"; st.Result["status"] = "no-outline"; yield break; }
                        if (first == Entity.Null) { first = n; firstAt = at; }
                        PlacemakerToolSystem.SuiteClick(at, n);
                        nodes.Add(new JArray(Math.Round(at.x), Math.Round(at.z)));
                    }
                    PlacemakerToolSystem.SuiteClick(firstAt, first);   // back to the first node: closes the loop
                    rec["nodes"] = nodes;
                    break;
                }
                default:
                    rec["error"] = "unknown area tool '" + tool + "'";
                    st.Result["status"] = "no-outline";
                    yield break;
            }
            yield return Feed(rec);
            // Paint area ends in the Area stage: a person presses Next
            yield return WaitFor(rec, "outline", () => _tool.Shape != null && !_tool.Painting, 20f);
            if (_tool.Shape != null && _tool.Stage == ToolStage.Area && _tool.SuitePrimaryInfo().Kind == "toDesign") { _tool.Primary(); yield return Frames(2); }
            if (_tool.Shape == null)
            {
                rec["error"] = "no outline" + (_tool.FlashText != null ? ": " + _tool.FlashText.Key : "");
                st.Result["status"] = "no-outline";
                yield break;
            }
            st.Ring = _tool.Shape.Ring2D();
            rec["mode"] = _tool.Mode.ToString();
            rec["areaM2"] = Math.Round(math.abs(Placemaker.Core.Geometry.Poly.SignedArea(st.Ring)));
            rec["corners"] = st.Ring.Length;
            st.Result["areaM2"] = rec["areaM2"];
            st.Before = _probe.Snapshot(st.Ring);
            st.Result["before"] = new JObject { ["buildings"] = st.Before.Buildings.Count, ["areas"] = st.Before.Areas.Count, ["objects"] = st.Before.Objects.Count, ["edges"] = st.Before.Edges.Count };
            yield return WaitPlan(rec);
        }

        private Entity NearestNode(float2 p, out float3 at)
        {
            at = default;
            var em = _world.EntityManager;
            var best = Entity.Null;
            float bestD = 20f;
            using (var q = em.CreateEntityQuery(ComponentType.ReadOnly<global::Game.Net.Node>(), ComponentType.ReadOnly<global::Game.Net.ConnectedEdge>(),
                ComponentType.Exclude<global::Game.Common.Deleted>(), ComponentType.Exclude<global::Game.Tools.Temp>(), ComponentType.Exclude<global::Game.Common.Owner>()))
            using (var all = q.ToEntityArray(Allocator.Temp))
                foreach (var e in all)
                {
                    var pos = em.GetComponentData<global::Game.Net.Node>(e).m_Position;
                    float d = math.distance(pos.xz, p);
                    if (d < bestD) { bestD = d; best = e; at = pos; }
                }
            return best;
        }

        // ───────────── type, entrances ─────────────

        private IEnumerator StepType(ScenarioState st, JObject step, JObject rec)
        {
            string layer = (string)step["layer"] ?? st.S.Layer;
            string preset = (string)step["preset"] ?? st.S.Preset ?? "";
            FreshType(layer);
            _tool.SelectPreset(layer, preset);
            yield return Frames(2);
            rec["recipeRoot"] = _tool.CurrentRecipe?.Root?.Layer ?? "";
            if ((string)rec["recipeRoot"] != LayerAliasesCanonical(layer))
            {
                rec["error"] = "type not selected" + (_tool.FlashText != null ? ": " + _tool.FlashText.Key : "");
                st.Result["status"] = "type-not-selected";
                yield break;
            }
            if (string.IsNullOrEmpty(st.S.Layer)) st.S.Layer = layer;
            yield return WaitPlan(rec);
            if (_tool.Plan == null && _tool.Shape != null) { rec["error"] = "no plan: " + _tool.SuiteUnreadyText(); st.Result["status"] = "no-plan"; }
        }

        /// <summary>Where an entrance goes: a side of the block (t along it), a point, or the outline nearest the south street.</summary>
        private float2 EntranceTarget(JObject step, SuiteBlock b)
        {
            bool abs = (bool?)step["abs"] ?? false;
            if (step["at"] is JArray) return Pt(step["at"], b, abs);
            float t = (float?)step["t"] ?? 0.5f;
            switch (((string)step["side"] ?? "south").ToLowerInvariant())
            {
                case "north": return new float2(math.lerp(b.X0, b.X1, t), b.Z1);
                case "east": return new float2(b.X1, math.lerp(b.Z0, b.Z1, t));
                case "west": return new float2(b.X0, math.lerp(b.Z0, b.Z1, t));
                default: return new float2(math.lerp(b.X0, b.X1, t), b.Z0);
            }
        }

        private float3 OnOutline(float2 p)
        {
            var ring = _tool.Shape.Ring;
            if (!Composer.ClosestSegment(ring, p, out int seg, out float t, out _)) return P3(p);
            var at = math.lerp(ring[seg], ring[(seg + 1) % ring.Length], t);
            return new float3(at.x, _fixture.H(at.x, at.z), at.z);
        }

        /// <summary>Starts marker placement the way the panel does: an explicit step/kind, else the main button's request.</summary>
        private bool StartPlacement(JObject step, JObject rec)
        {
            string path = (string)step["path"], kind = (string)step["kind"];
            if (path != null) { _tool.SetMarkerKind(path, kind ?? "entrance"); rec["via"] = "panel"; }
            else
            {
                var req = _tool.SuiteMarkerRequest();
                if (req.Path != null) { _tool.Primary(); rec["via"] = "button"; }   // "Add at least one entrance": pressing starts placement
                else if (kind != null || _tool.SuiteMarkers().Count > 0)
                {
                    var last = _tool.SuiteMarkers().LastOrDefault();
                    _tool.SetMarkerKind(last.Path ?? "0", kind ?? last.Kind ?? "entrance");
                    rec["via"] = "panel";
                }
            }
            return _tool.MarkerKind != null;
        }

        private IEnumerator StepEntrance(ScenarioState st, JObject step, JObject rec)
        {
            if (_tool.Shape == null) { rec["error"] = "no outline"; yield break; }
            if (!StartPlacement(step, rec)) { rec["error"] = "entrance placement did not start (no step asks for markers)"; yield break; }
            yield return Frames(2);
            rec["path"] = _tool.MarkerPath;
            rec["kind"] = _tool.MarkerKind;
            int before = _tool.SuiteMarkers().Count;
            var at = OnOutline(EntranceTarget(step, st.S.Block));
            PlacemakerToolSystem.SuiteClick(at);
            yield return Feed(rec);
            _tool.ExitMarkerMode();   // Esc: done placing
            int after = _tool.SuiteMarkers().Count;
            rec["at"] = new JArray(Math.Round(at.x, 1), Math.Round(at.z, 1));
            if (after <= before) { rec["error"] = "the click placed no marker"; yield break; }
            st.Markers.Add(at);
            yield return WaitPlan(rec);
        }

        /// <summary>Default scripts: while the main button asks for entrances, add one on the south street side (then east, north, west).</summary>
        private IEnumerator StepEntrancesAuto(ScenarioState st, JObject step, JObject rec)
        {
            var placed = new JArray();
            string[] sides = { "south", "east", "north", "west" };
            for (int k = 0; k < sides.Length && _tool.SuiteMarkerRequest().Path != null; k++)
            {
                var sub = new JObject { ["side"] = sides[k] };
                var r = new JObject();
                yield return StepEntrance(st, sub, r);
                placed.Add(r);
                if (r["waits"] is JObject w) foreach (var p in w.Properties()) Waited(rec, p.Name, (float)(double)p.Value / 1000f, 0f);
                if (r["error"] != null) { rec["error"] = r["error"]; break; }
            }
            rec["placed"] = placed;
        }

        /// <summary>Move = the UI's way: in placement mode right-click the marker (removes it), left-click the new spot.</summary>
        private IEnumerator StepMoveEntrance(ScenarioState st, JObject step, JObject rec, bool removeOnly)
        {
            var markers = _tool.SuiteMarkers();
            int index = (int?)step["index"] ?? 0;
            if (index < 0 || index >= markers.Count) { rec["error"] = $"no marker {index} (have {markers.Count})"; yield break; }
            var m = markers[index];
            _tool.SetMarkerKind(m.Path, m.Kind);
            yield return Frames(2);
            PlacemakerToolSystem.SuiteClick(m.At, default, right: true);
            float3 to = default;
            if (!removeOnly)
            {
                to = OnOutline(EntranceTarget(step, st.S.Block));
                PlacemakerToolSystem.SuiteClick(to);
            }
            yield return Feed(rec);
            _tool.ExitMarkerMode();
            rec["from"] = new JArray(Math.Round(m.At.x, 1), Math.Round(m.At.z, 1));
            if (!removeOnly) rec["to"] = new JArray(Math.Round(to.x, 1), Math.Round(to.z, 1));
            rec["markers"] = _tool.SuiteMarkers().Count;
            if (index < st.Markers.Count) { if (removeOnly) st.Markers.RemoveAt(index); else st.Markers[index] = to; }
            yield return WaitPlan(rec);
        }

        // ───────────── review, edit, build, delete ─────────────

        private JArray Issues()
        {
            var a = new JArray();
            var plan = _tool.Plan;
            if (plan == null) return a;
            foreach (var i in plan.Issues.Concat(plan.Layers.SelectMany(l => l.Issues)).Take(60))
            {
                var o = new JObject { ["severity"] = i.Severity.ToString(), ["key"] = i.Key };
                if (i.Args != null && i.Args.Length > 0) o["args"] = new JArray(i.Args.Select(x => x?.ToString() ?? ""));
                if (i.At.HasValue) o["at"] = new JArray(Math.Round(i.At.Value.x, 1), Math.Round(i.At.Value.z, 1));
                if (!string.IsNullOrEmpty(i.Cause)) o["cause"] = i.Cause + (string.IsNullOrEmpty(i.CauseValue) ? "" : " = " + i.CauseValue);
                if (i.Dropped) o["dropped"] = true;
                a.Add(o);
            }
            return a;
        }

        private IEnumerator StepPreview(ScenarioState st, JObject step, JObject rec)
        {
            yield return WaitPlan(rec);
            var primary = _tool.SuitePrimaryInfo();
            if (_tool.Stage != ToolStage.Review)
            {
                if (primary.Kind == "toReview" && primary.Enabled) _tool.Primary();
                else
                {
                    rec["error"] = "cannot preview: " + primary.Kind + (primary.Enabled ? "" : " disabled") + " " + _tool.SuiteUnreadyText();
                    st.Result["status"] = "blocked";
                    st.Result["blockedAt"] = "design";
                    st.Result["unready"] = _tool.SuiteUnreadyText();
                    yield break;
                }
            }
            yield return WaitFor(rec, "review", () => _tool.Stage == ToolStage.Review, 15f);
            yield return WaitPlan(rec);
            yield return WaitGhost(rec);
            st.Plan = _tool.Plan;
            rec["issues"] = Issues();
            rec["planCost"] = Math.Round(_tool.PlanCost);
            st.Result["planIssues"] = rec["issues"];
            st.Result["planCost"] = rec["planCost"];
        }

        private IEnumerator StepEdit(ScenarioState st, JObject step, JObject rec)
        {
            if ((bool?)step["build"] ?? false)
            {
                if (st.Group == 0) { rec["error"] = "no built group to edit"; yield break; }
                _tool.EditGroup(st.Group);
                rec["group"] = st.Group;
            }
            else if (!_tool.GoToStage(ToolStage.Design)) { rec["error"] = "cannot go back to Design from " + _tool.Stage; yield break; }
            yield return Frames(2);
            rec["stage"] = _tool.Stage.ToString();
            yield return WaitPlan(rec);
        }

        private IEnumerator StepBuild(ScenarioState st, JObject step, JObject rec)
        {
            var res = st.Result;
            if (_tool.Stage != ToolStage.Review)
            {
                var r = new JObject();
                yield return StepPreview(st, step, r);
                if (r["waits"] is JObject w) foreach (var p in w.Properties()) Waited(rec, p.Name, (float)(double)p.Value / 1000f, 0f);
                if (r["error"] != null) { rec["error"] = r["error"]; yield break; }
            }
            yield return WaitPlan(rec);
            st.Plan = _tool.Plan;
            var primary = _tool.SuitePrimaryInfo();
            if (!primary.Enabled && primary.Reason == "Placemaker.Reason.AcknowledgePermanent")
            {
                _tool.AcknowledgePermanent(true);
                yield return Frames(2);
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
            float t0 = Time.realtimeSinceStartup;
            _tool.Primary();
            yield return WaitFor(rec, "buildStart", () => _tool.Building, 10f);
            yield return WaitFor(rec, "build", () => !_tool.Building, 300f);
            res["buildMs"] = Math.Round((Time.realtimeSinceStartup - t0) * 1000f);
            if (_tool.SuiteBuildsDone == doneBefore)
            {
                var f = _tool.BuildFailure;
                res["status"] = "build-failed";
                res["failure"] = f == null ? (_timedOut ? "timeout" : "unknown") : f.Key + (f.Args != null && f.Args.Length > 0 ? " (" + string.Join(", ", f.Args) + ")" : "");
                rec["error"] = "build failed: " + res["failure"];
                yield break;
            }
            st.Built = true;
            st.Deleted = false;
            st.Group = _tool.SuiteLastBuiltGroup;
            rec["group"] = st.Group;
            res["status"] = "built";
            res["notes"] = new JArray(_tool.BuildNotes.Select(n => n.Key + (n.Args != null && n.Args.Length > 0 ? " (" + string.Join(", ", n.Args) + ")" : "")));
            // the world settles: no temporaries left, frames quick again
            yield return WaitFor(rec, "settle", () => TempCount() == 0 || _lastFrameMs < 50f, 30f);
            yield return Frames(10);
            if (st.Before != null)
            {
                var ground = _probe.GroundNow(st.Before);
                res["terrain"] = _probe.Pits(st.Before, ground, st.Plan);
                res["census"] = _probe.Census(st.Before, st.Group, st.Plan, st.S.Layer, _tool.BuildAudit);
            }
        }

        private IEnumerator StepDelete(ScenarioState st, JObject step, JObject rec)
        {
            if (!st.Built || st.Group == 0) { rec["error"] = "nothing built to delete (loose builds have no group)"; yield break; }
            int g = st.Group;
            _tool.ManageGroup(g);   // a person clicks the build, then Delete in its panel
            yield return Frames(2);
            _tool.RemoveGroup(g);
            yield return WaitFor(rec, "delete", () => Services.Groups == null || Services.Groups.Get(g) == null, 60f);
            yield return WaitFor(rec, "settle", () => TempCount() == 0, 20f);
            yield return Frames(10);
            st.Deleted = true;
            st.Built = false;
            st.Result["status"] = "deleted";
            if (_tool.Stage == ToolStage.Manage) _tool.GoToStage(ToolStage.Area);
            // what the build left behind in its site (it should be nothing)
            if (st.Before != null) rec["leftBehind"] = _probe.LeftBehind(st.Before);
            if ((bool?)step["restore"] ?? true) yield return StepRestore(st, rec);
        }

        private IEnumerator StepRestore(ScenarioState st, JObject rec)
        {
            var b = st.S.Block;
            float t0 = Time.realtimeSinceStartup;
            string err = _fixture.RestoreBlock(b);
            yield return Frames(20);
            Waited(rec, "restore", Time.realtimeSinceStartup - t0, 0f);
            rec["restored"] = err ?? b.Id;
            if (err == null && st.Deleted) _fixture.Release(b);
        }

        // ───────────── camera and screenshots ─────────────

        private float3 FocusOf(SuiteBlock b) => new float3(b.Center.x, _fixture.H(b.Center.x, b.Center.y), b.Center.y);

        /// <summary>
        /// Frames the camera and waits until it arrived: the controller holds the requested pivot and the rendered camera
        /// stopped moving (5 still frames after it was told). The game camera's eye sits a little farther out than the
        /// controller's zoom (it lifts over the terrain), so the distance is recorded, not tested.
        /// </summary>
        private IEnumerator CameraTo(float3 pivot, float yaw, float pitch, float distance, JObject rec)
        {
            float t0 = Time.realtimeSinceStartup;
            SuiteCapture.Aim(pivot, yaw, pitch, distance);
            Vector3 lastP = Vector3.positiveInfinity;
            Quaternion lastR = Quaternion.identity;
            int still = 0, frames = 0;
            bool arrived = false;
            float until = Time.realtimeSinceStartup + 20f;
            while (Time.realtimeSinceStartup < until)
            {
                yield return null;
                frames++;
                var cam = Camera.main;
                if (cam == null) continue;
                var tr = cam.transform;
                bool moved = (tr.position - lastP).sqrMagnitude > 0.0004f || Quaternion.Angle(tr.rotation, lastR) > 0.02f;
                lastP = tr.position;
                lastR = tr.rotation;
                still = moved ? 0 : still + 1;
                if (frames >= 3 && still >= 5 && SuiteCapture.PivotNear(pivot, 1f)) { arrived = true; break; }
            }
            Waited(rec, "camera", Time.realtimeSinceStartup - t0, arrived ? 0f : 20f);
            rec["cameraState"] = SuiteCapture.CameraState();
            if (Camera.main != null) rec["cameraDistance"] = Math.Round(math.distance((float3)Camera.main.transform.position, pivot), 1);
        }

        /// <summary>Resolves a camera request: target (block, area, build, entrance, point, issue, overview) and view.</summary>
        private IEnumerator CameraStep(ScenarioState st, JObject c, JObject rec)
        {
            var b = st.S.Block;
            string target = ((string)c["target"] ?? "block").ToLowerInvariant();
            float3 pivot = FocusOf(b);
            float radius = 85f;
            switch (target)
            {
                case "area":
                case "build":
                    if (st.Ring != null && st.Ring.Length > 0)
                    {
                        float2 lo = st.Ring[0], hi = st.Ring[0];
                        foreach (var p in st.Ring) { lo = math.min(lo, p); hi = math.max(hi, p); }
                        var m = (lo + hi) * 0.5f;
                        pivot = P3(m);
                        radius = math.max(25f, math.length(hi - lo) * 0.5f);
                    }
                    break;
                case "entrance":
                {
                    int i = (int?)c["index"] ?? 0;
                    var markers = st.Markers.Count > 0 ? st.Markers : _tool.SuiteMarkers().Select(x => x.At).ToList();
                    if (i < 0 || i >= markers.Count) { rec["cameraError"] = $"no entrance {i} (have {markers.Count})"; break; }
                    pivot = markers[i];
                    radius = 15f;
                    break;
                }
                case "issue":
                {
                    int i = (int?)c["index"] ?? 0;
                    var at = (_tool.Plan?.Issues.Concat(_tool.Plan.Layers.SelectMany(l => l.Issues)) ?? Enumerable.Empty<PlanIssue>()).Where(x => x.At.HasValue).Select(x => x.At.Value).ToList();
                    if (i < 0 || i >= at.Count) { rec["cameraError"] = $"no issue marker {i} (have {at.Count})"; break; }
                    pivot = at[i];
                    radius = 15f;
                    break;
                }
                case "point":
                    pivot = P3(Pt(c["at"], b, (bool?)c["abs"] ?? false));
                    radius = 20f;
                    break;
                case "overview":
                    pivot = new float3((_fixture.Layout.LineX(0) + _fixture.Layout.LineX(_fixture.Layout.NX)) * 0.5f, _fixture.Layout.Base, (_fixture.Layout.LineZ(0) + _fixture.Layout.LineZ(_fixture.Layout.NZ)) * 0.5f);
                    radius = 600f;
                    break;
            }
            float yaw = 25f, pitch = 55f, dist = radius * 3.4f;
            switch (((string)c["view"] ?? (target == "entrance" || target == "issue" || target == "point" ? "closeup" : "oblique")).ToLowerInvariant())
            {
                case "top": yaw = 0f; pitch = 89.5f; dist = radius * 3.6f; break;
                case "closeup": yaw = 30f; pitch = 38f; dist = math.max(30f, radius * 3f); break;
                case "low": yaw = 25f; pitch = 18f; dist = radius * 2.2f; break;
            }
            if (c["yaw"] != null) yaw = (float)c["yaw"];
            if (c["pitch"] != null) pitch = (float)c["pitch"];
            if (c["distance"] != null) dist = (float)c["distance"];
            rec["camera"] = new JObject { ["target"] = target, ["pivot"] = new JArray(Math.Round(pivot.x, 1), Math.Round(pivot.y, 1), Math.Round(pivot.z, 1)), ["yaw"] = yaw, ["pitch"] = pitch, ["distance"] = Math.Round(dist, 1) };
            yield return CameraTo(pivot, yaw, pitch, dist, rec);
        }

        /// <summary>The textures around the new view have streamed in (virtual texturing quiet for a few frames).</summary>
        private IEnumerator WaitStreaming(JObject rec, float timeout = 20f)
        {
            float t0 = Time.realtimeSinceStartup;
            int quiet = 0;
            long last = -1;
            bool ok = false;
            while (Time.realtimeSinceStartup - t0 < timeout)
            {
                yield return null;
                var (left, reads) = SuiteCapture.Streaming(_world);
                bool calm = left <= 0 && (reads < 0 || reads == last || reads == 0) && _lastFrameMs < 100f;
                last = reads;
                quiet = calm ? quiet + 1 : 0;
                if (quiet >= 6) { ok = true; break; }
            }
            Waited(rec, "streaming", Time.realtimeSinceStartup - t0, ok ? 0f : timeout);
        }

        private IEnumerator StepShot(ScenarioState st, JObject step, JObject rec, string name)
        {
            if (step["camera"] is JObject cam) yield return CameraStep(st, cam, rec);
            yield return WaitStreaming(rec);
            string mode = ((string)step["shotMode"] ?? (string)step["mode"] ?? "").ToLowerInvariant();
            bool grab = mode == "grab" || (mode != "render" && _tool.IsActive && _tool.Stage != ToolStage.Manage);
            string file = Safe(st.S.Id) + "." + Safe(name) + ".png";
            string path = Path.Combine(_shots, file);
            string err;
            if (grab)
            {
                SuiteCapture.UiOff();
                yield return Frames(3);
                yield return new WaitForEndOfFrame();
                err = SuiteCapture.GrabScreen(path, _cfg.shotWidth);
                SuiteCapture.UiOn();
            }
            else
            {
                yield return new WaitForEndOfFrame();
                err = SuiteCapture.RenderPng(path, _cfg.shotWidth, _cfg.shotHeight);
            }
            string rel = err == null ? "shots/" + file : "ERROR " + err;
            rec["shot"] = rel;
            rec["shotMode"] = grab ? "grab" : "render";
            if (st.Built && st.Group > 0) rec["hiddenMembers"] = _probe.HiddenMembers(_probe.MemberSet(st.Group));
            st.Shots[name] = rel;
            if (err != null) Say($"shot {name} failed: {err}");
        }

        private static string Safe(string id)
        {
            foreach (var c in Path.GetInvalidFileNameChars()) id = id.Replace(c, '_');
            return id.Replace('+', 'x');
        }

        // ───────────── after the shared grow phase ─────────────

        private IEnumerator PostGrow(ScenarioState st)
        {
            if (st.Before != null && st.Ring != null && st.Built)
            {
                var members = _probe.MemberSet(st.Group);
                st.Result["growables"] = _probe.Growables(st.Before, st.Group, members);
                int lost = 0;
                foreach (var b in st.Before.Buildings) if (!_world.EntityManager.Exists(b) || _world.EntityManager.HasComponent<global::Game.Common.Deleted>(b)) lost++;
                st.Result["existingLostAfterGrow"] = lost;
            }
            if (st.S.Block != null && st.Built && st.S.Steps == null)
            {
                var r = new JObject();
                yield return StepShot(st, new JObject { ["camera"] = new JObject { ["target"] = "block", ["view"] = "oblique" }, ["shotMode"] = "render" }, r, "oblique");
                var r2 = new JObject();
                yield return StepShot(st, new JObject { ["camera"] = new JObject { ["target"] = "block", ["view"] = "top" }, ["shotMode"] = "render" }, r2, "top");
                st.Result["postShots"] = new JArray(r, r2);
            }
        }

        private void Verdicts(ScenarioState st)
        {
            var r = st.Result;
            var v = new JArray();
            void Check(string id, bool pass, string detail) => v.Add(new JObject { ["id"] = id, ["pass"] = pass, ["detail"] = detail });
            string status = (string)r["status"] ?? "";
            bool scripted = st.S.Steps != null;
            bool wantsBuild = !scripted || st.S.Steps.Any(x => (string)x["do"] == "build");
            if (wantsBuild) Check("built", status == "built" || status == "deleted", status + (r["failure"] != null ? ": " + r["failure"] : "") + (r["unready"] != null ? ": " + r["unready"] : ""));
            if (r["steps"] is JArray steps)
                foreach (var s in steps.OfType<JObject>().Where(x => x["error"] != null))
                    Check("step" + s["i"] + ":" + s["do"], false, (string)s["error"]);
            if (r["timeouts"] is JArray to && to.Count > 0) Check("noTimeouts", false, string.Join("; ", to.Select(x => (string)x)));
            var census = r["census"] as JObject;
            if (census != null)
            {
                var audit = census["audit"] as JObject;
                if (audit != null) Check("nothingLeftOut", (int)audit["leftOut"] == 0, $"left out {audit["leftOut"]}, kept despite collisions {audit["kept"]}");
                bool farm = (bool?)census["farmType"] ?? false;
                int crop = (int?)census["cropLikeCount"] ?? 0;
                if (!farm) Check("noCropOrVanillaLots", crop == 0, crop == 0 ? "" : census["cropLike"].ToString(Formatting.None));
                var fences = census["fences"] as JObject;
                if (fences != null) Check("noForeignFences", (int)fences["notMembers"] == 0, $"fences {fences["total"]}, not members {fences["notMembers"]}");
                var parking = census["parking"] as JObject;
                if (parking != null && (int)parking["plannedStalls"] > 0)
                {
                    Check("stallsBuilt", (int)parking["liveStalls"] >= (int)parking["plannedStalls"], $"planned {parking["plannedStalls"]}, live {parking["liveStalls"]}");
                    Check("stallsOnLanes", (int)parking["stallsFarFromLane"] == 0, $"{parking["stallsFarFromLane"]} stalls > 9 m from any car lane");
                }
                var nets = census["nets"] as JObject;
                if (nets != null && (int)nets["edges"] > 0) Check("netsConnected", (int)(nets["isolatedEdges"] ?? 0) == 0, $"{nets["isolatedEdges"]} of {nets["edges"]} edges cut off from the city");
                var zoning = census["zoning"] as JObject;
                if (zoning != null && ((int)zoning["plannedQuadCells"] > 0 || (int)zoning["plannedPreviewCells"] > 0))
                    Check("zoningLive", (int)zoning["liveZonedCells"] > 0, $"planned {zoning["plannedQuadCells"]} quad cells / {zoning["plannedPreviewCells"]} preview, live {zoning["liveZonedCells"]} (own roads {zoning["zonedFromOwnRoads"]}, city {zoning["zonedFromCityRoads"]})");
                var ex = census["existingBuildings"] as JObject;
                if (ex != null && (int)ex["before"] > 0) Check("existingSurvive", (int)ex["lost"] == 0, $"{ex["lost"]} of {ex["before"]} lost {ex["lostPrefabs"]?.ToString(Formatting.None)}");
            }
            var terrain = r["terrain"] as JObject;
            if (terrain != null) Check("noPits", (int)terrain["pitCellsNearRoads"] == 0, $"pit cells {terrain["pitCells"]} ({terrain["pitCellsNearRoads"]} near roads), max {terrain["maxPitDepth"]} m at {terrain["worstAt"]?.ToString(Formatting.None)}");
            var gr = r["growables"] as JObject;
            if (gr != null && ((int)gr["facingOwnRoad"] + (int)gr["facingCityRoad"]) > 0)
                Check("growablesFaceOwnRoad", (int)gr["facingCityRoad"] == 0, $"{gr["facingOwnRoad"]} face the build's roads, {gr["facingCityRoad"]} face city streets");
            var errs = r["errors"] as JObject;
            if (errs != null) Check("noErrors", (int)errs["errors"] + (int)errs["exceptions"] == 0, $"{errs["errors"]} errors, {errs["exceptions"]} exceptions");
            float worstFrame = 0f;
            string worstAt = "";
            if (r["steps"] is JArray all)
                foreach (var s in all.OfType<JObject>())
                    if (s["frames"] is JObject f && f["maxMs"] != null && (float)f["maxMs"] > worstFrame && !Passive((string)s["do"])) { worstFrame = (float)f["maxMs"]; worstAt = $"step {s["i"]} {s["do"]}"; }
            Check("noHang", worstFrame < 1000f, $"worst frame {worstFrame:F0} ms ({worstAt}; camera/shot/wait/grow steps not counted)");
            int hidden = (int?)census?["hidden"]?["count"] ?? 0;
            foreach (var s in (r["steps"] as JArray ?? new JArray()).OfType<JObject>()) hidden = Math.Max(hidden, (int?)s["hiddenMembers"]?["count"] ?? 0);
            if (census != null) Check("noHiddenMembers", hidden == 0, hidden == 0 ? "" : $"{hidden} built pieces hidden by the game (Overridden) {census["hidden"]?["byPrefab"]?.ToString(Formatting.None)}");
            foreach (var s in (r["steps"] as JArray ?? new JArray()).OfType<JObject>().Where(x => x["leftBehind"] is JObject))
            {
                var lb = (JObject)s["leftBehind"];
                Check("deleteLeavesNothing", (int)lb["count"] == 0, lb.ToString(Formatting.None));
            }
            r["verdicts"] = v;
            r["failed"] = new JArray(v.Where(x => !(bool)x["pass"]).Select(x => (string)x["id"]));
        }

        // ───────────── one scenario, batch or queue ─────────────

        private IEnumerator RunOne(Scenario s)
        {
            ScenarioState st;
            if (s.On != null && _byId.TryGetValue(s.On, out var host))
            {
                st = new ScenarioState { S = s, Ring = host.Ring, Group = host.Group, Built = host.Built, Before = host.Before, Plan = host.Plan, Focus = host.Focus };
                st.Markers.AddRange(host.Markers);
                s.Block = host.S.Block;
            }
            else
            {
                st = new ScenarioState { S = s };
                s.Block = _fixture.Take(s.Site);
            }
            st.FileIndex = _states.Count;
            _states.Add(st);
            _scenario = s.Id;
            float t0 = Time.realtimeSinceStartup;
            yield return Guard(RunScenario(st), e =>
            {
                Say($"scenario {s.Id} threw: {e}");
                st.Result["status"] = "error";
                st.Result["exception"] = e.GetType().Name + ": " + e.Message;
            });
            st.Result["scenarioSec"] = Math.Round(Time.realtimeSinceStartup - t0, 1);
            WriteScenario(st, st.FileIndex);
            WriteSummary();
            _lastRun = st;
        }

        private ScenarioState _lastRun;

        // ───────────── live queue ─────────────

        /// <summary>
        /// Waits for scenario files in &lt;run&gt;/inbox (a scenario, a list {"scenarios": [...]}, or {"command": "quit"}), runs
        /// them in name order, writes &lt;file&gt;.result.json beside them and moves each processed file to inbox/done.
        /// </summary>
        private IEnumerator QueueLoop()
        {
            _phase = "queue";
            string inbox = Path.Combine(_out, "inbox"), done = Path.Combine(inbox, "done");
            Directory.CreateDirectory(done);
            _summary["inbox"] = inbox;
            WriteSummary();
            Say("queue: waiting for scenario files in " + inbox);
            float idleSince = Time.realtimeSinceStartup;
            bool quit = false;
            while (!quit)
            {
                _scenario = "";
                var files = Directory.GetFiles(inbox, "*.json").Where(f => !f.EndsWith(".result.json", StringComparison.OrdinalIgnoreCase)).OrderBy(f => f, StringComparer.Ordinal).ToList();
                if (files.Count == 0)
                {
                    if (Time.realtimeSinceStartup - idleSince > _cfg.queueIdleMinutes * 60f) { Say("queue: idle timeout"); break; }
                    yield return Seconds(0.4f);
                    continue;
                }
                foreach (var file in files)
                {
                    string name = Path.GetFileNameWithoutExtension(file);
                    JObject doc;
                    try
                    {
                        if ((DateTime.UtcNow - File.GetLastWriteTimeUtc(file)).TotalSeconds < 0.3) continue;   // still being written
                        doc = JObject.Parse(File.ReadAllText(file));
                    }
                    catch (Exception e)
                    {
                        File.WriteAllText(Path.Combine(inbox, name + ".result.json"), new JObject { ["file"] = name, ["error"] = "unreadable: " + e.Message }.ToString(Formatting.Indented));
                        MoveDone(file, done);
                        continue;
                    }
                    var report = new JObject { ["file"] = name, ["startedUtc"] = DateTime.UtcNow.ToString("o") };
                    string cmd = (string)doc["command"];
                    if (cmd == "quit") { report["command"] = "quit"; quit = true; }
                    else if (cmd == "grow")
                    {
                        _phase = "grow";
                        yield return Guard(Grow((float?)doc["seconds"] ?? 30f, "grow-" + name), e => report["error"] = e.Message);
                        report["grow"] = _summary["grow-" + name];
                        _phase = "queue";
                    }
                    else
                    {
                        ScenarioSet set;
                        try { set = doc["scenarios"] != null ? doc.ToObject<ScenarioSet>() : new ScenarioSet { scenarios = new List<ScenarioSpec> { doc.ToObject<ScenarioSpec>() } }; }
                        catch (Exception e) { set = null; report["error"] = "bad scenario file: " + e.Message; }
                        var rows = new JArray();
                        if (set != null)
                        {
                            _phase = "queue-run";
                            foreach (var s in Expand(set))
                            {
                                yield return RunOne(s);
                                var st = _lastRun;
                                Verdicts(st);
                                WriteScenario(st, st.FileIndex);
                                rows.Add(new JObject
                                {
                                    ["id"] = s.Id, ["status"] = st.Result["status"], ["failed"] = st.Result["failed"], ["sec"] = st.Result["scenarioSec"],
                                    ["result"] = Path.Combine(_results, $"{st.FileIndex + 1:D2}-{Safe(s.Id)}.json"), ["shots"] = st.Result["shots"], ["waitsMs"] = st.Result["waitsMs"],
                                });
                            }
                            _phase = "queue";
                        }
                        report["scenarios"] = rows;
                    }
                    report["finishedUtc"] = DateTime.UtcNow.ToString("o");
                    File.WriteAllText(Path.Combine(inbox, name + ".result.json"), report.ToString(Formatting.Indented));
                    MoveDone(file, done);
                    WriteSummary();
                    idleSince = Time.realtimeSinceStartup;
                    if (quit) break;
                }
            }
            yield return Guard(CloseTool(), e => Say("close tool: " + e.Message));
        }

        private static void MoveDone(string file, string done)
        {
            try
            {
                var to = Path.Combine(done, Path.GetFileName(file));
                if (File.Exists(to)) File.Delete(to);
                File.Move(file, to);
            }
            catch (Exception) { try { File.Delete(file); } catch (Exception) { } }
        }
    }
}
