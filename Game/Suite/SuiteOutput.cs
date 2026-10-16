// In-game test suite: one scenario (batch or queue), the live queue, and the run's outputs (scenario JSON, summary.json,
// summary.md with a headline and a result per scenario, contact.html).

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Placemaker.Core.Suite;
using Placemaker.Core.Tool;
using UnityEngine;

namespace Placemaker.Game.Suite
{
    public sealed partial class SuiteRunner
    {
        private static readonly HashSet<string> OnSafeSteps = new HashSet<string>(StringComparer.Ordinal)
        { "camera", "shot", "issues", "select", "delete", "bulldoze", "infoEdit", "infoRemove", "wait", "grow", "compare" };

        private IEnumerator RunOne(Scenario s)
        {
            ScenarioState st;
            ScenarioState host = null;
            string refused = null;
            if (s.On != null)
            {
                // runs on an earlier scenario's block and build — only while that build stands
                if (!_byId.TryGetValue(s.On, out host)) refused = $"'on' target {s.On} has not run in this session";
                else if (host.Deleted) refused = $"'on' target {s.On} was deleted (put this scenario before the delete)";
                else if (!host.Built) refused = $"'on' target {s.On} has no build (its status: {host.Result["status"]})";
                else if (s.Steps != null)
                {
                    // design steps need the host's design loaded the way a person edits a build
                    bool editFirst = s.Steps.Count > 0 && (string)s.Steps[0]["do"] == "edit" && ((bool?)s.Steps[0]["build"] ?? false);
                    var bad = s.Steps.Select(x => (string)x["do"]).FirstOrDefault(d => !OnSafeSteps.Contains(d));
                    if (bad != null && !editFirst) refused = $"'on' scenarios may only look at or remove the build (camera, shot, issues, select, delete...); '{bad}' needs {{\"do\":\"edit\",\"build\":true}} first";
                }
                st = new ScenarioState { S = s };
                if (host != null)
                {
                    st.Ring = host.Ring; st.Group = host.Group; st.Built = host.Built; st.Before = host.Before; st.Plan = host.Plan; st.Focus = host.Focus;
                    st.Markers.AddRange(host.Markers);
                    // the host's build is judged in the host's result; here only what this scenario does is judged
                    st.BuildCount = 0;
                    st.Ghost = host.Ghost;
                    st.GhostTotal = host.GhostTotal;
                    s.Block = host.S.Block;
                    foreach (var key in new[] { "entrances", "terrain" }) if (host.Result[key] != null) st.Result["host" + key] = host.Result[key].DeepClone();
                    if (host.Result["census"]?["connections"] != null) st.Result["hostconnections"] = host.Result["census"]["connections"].DeepClone();
                }
            }
            else
            {
                st = new ScenarioState { S = s };
                if (string.Equals(s.Site, "zoned", StringComparison.OrdinalIgnoreCase) && !_pregrown) yield return Pregrow();
                s.Block = _fixture.Take(s.Site, s.BlockId, s.Streets, s.SiteFallback, s.Id, out var note);
                s.SiteNote = note;
            }
            st.FileIndex = _states.Count;
            _states.Add(st);
            _scenario = s.Id;
            float t0 = Time.realtimeSinceStartup;
            if (refused != null)
            {
                st.Result["id"] = s.Id;
                st.Result["status"] = "refused";
                st.Result["refused"] = refused;
                Say($"scenario {s.Id} refused: {refused}");
            }
            else if (_abortBatch != null)
            {
                st.Result["id"] = s.Id;
                st.Result["status"] = "refused";
                st.Result["refused"] = "batch stopped: " + _abortBatch;
            }
            else
                yield return Guard(RunScenario(st), e =>
                {
                    Say($"scenario {s.Id} threw: {e}");
                    st.Result["status"] = "error";
                    st.Result["exception"] = e.GetType().Name + ": " + e.Message;
                });
            if (host != null && refused == null)
            {
                // what the steps did to the build belongs to its scenario too
                host.Built = st.Built; host.Deleted = st.Deleted; host.Group = st.Group; host.Ring = st.Ring ?? host.Ring;
                host.Markers.Clear();
                host.Markers.AddRange(st.Markers);
            }
            st.Result["scenarioSec"] = Math.Round(Time.realtimeSinceStartup - t0, 1);
            try { Verdicts(st); } catch (Exception e) { Say($"verdicts {s.Id}: {e}"); st.Result["verdictError"] = e.Message; }
            WriteScenario(st, st.FileIndex);
            WriteSummary();
            _lastRun = st;
        }

        private ScenarioState _lastRun;

        // ───────────── live queue ─────────────

        /// <summary>
        /// Waits for scenario files in &lt;run&gt;/inbox (a scenario, a set {"scenarios": [...]} with optional switching,
        /// {"command":"grow"} or {"command":"quit"}), runs them in name order, writes &lt;file&gt;.result.json beside them and
        /// moves each processed file to inbox/done.
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
                        AtomicWrite(Path.Combine(inbox, name + ".result.json"), new JObject { ["file"] = name, ["error"] = "unreadable: " + e.Message }.ToString(Formatting.Indented));
                        MoveDone(file, done);
                        continue;
                    }
                    var report = new JObject { ["file"] = name, ["startedUtc"] = DateTime.UtcNow.ToString("o") };
                    string cmd = (string)doc["command"];
                    if (cmd == "quit") { report["command"] = "quit"; quit = true; }
                    else if (cmd == "grow")
                    {
                        _phase = "grow";
                        yield return Guard(CloseTool(), e => { });
                        yield return Guard(Grow((float?)doc["seconds"] ?? 30f, "grow-" + name, (bool?)doc["fast"] ?? true), e => report["error"] = e.Message);
                        report["grow"] = _summary["grow-" + name];
                        // every standing build is looked at again and re-judged
                        var rows = new JArray();
                        foreach (var st in _states.Where(x => x.Built))
                        {
                            _scenario = st.S.Id;
                            yield return Guard(PostGrow(st), e => st.Result["postError"] = e.Message);
                            Verdicts(st, "after queue grow " + name);
                            WriteScenario(st, st.FileIndex);
                            rows.Add(Row(st));
                        }
                        report["rejudged"] = rows;
                        _phase = "queue";
                    }
                    else
                    {
                        ScenarioSet set = null;
                        try
                        {
                            CheckSetSchema(doc["scenarios"] != null ? doc : new JObject { ["scenarios"] = new JArray(doc) });
                            set = doc["scenarios"] != null ? doc.ToObject<ScenarioSet>() : new ScenarioSet { scenarios = new List<ScenarioSpec> { doc.ToObject<ScenarioSpec>() }, grow = false };
                        }
                        catch (Exception e) { report["error"] = "bad scenario file: " + e.Message; }
                        var rows = new JArray();
                        if (set != null)
                        {
                            _phase = "queue-run";
                            if (set.switching != null)
                            {
                                var block = _fixture.Take(set.switching.site, null, null, true, "switching", out _);
                                yield return Guard(Switching(set.switching, block), e => report["switchingError"] = e.Message);
                                report["switching"] = _summary["switching"];
                            }
                            foreach (var s in Expand(set))
                            {
                                yield return RunOne(s);
                                rows.Add(Row(_lastRun));
                            }
                            if (set.grow && doc["scenarios"] != null && _states.Any(x => x.Built))
                                report["grow"] = "the set's grow runs only with {\"command\":\"grow\"} in the queue (it grows the whole city)";
                            _phase = "queue";
                        }
                        report["scenarios"] = rows;
                    }
                    report["finishedUtc"] = DateTime.UtcNow.ToString("o");
                    report["contact"] = Path.Combine(_out, "contact.html");
                    AtomicWrite(Path.Combine(inbox, name + ".result.json"), report.ToString(Formatting.Indented));
                    MoveDone(file, done);
                    WriteSummary();
                    idleSince = Time.realtimeSinceStartup;
                    if (quit) break;
                }
            }
            yield return Guard(CloseTool(), e => Say("close tool: " + e.Message));
        }

        private JObject Row(ScenarioState st) => new JObject
        {
            ["id"] = st.S.Id, ["result"] = st.Result["result"], ["status"] = st.Result["status"], ["failed"] = st.Result["failed"], ["unknown"] = st.Result["unknown"],
            ["block"] = st.S.Block?.Id, ["kind"] = st.S.Block?.Kind.ToString(), ["sec"] = st.Result["scenarioSec"],
            ["verdicts"] = new JArray((st.Result["verdicts"] as JArray ?? new JArray()).OfType<JObject>().Where(v => (string)v["result"] != "pass")),
            ["resultFile"] = Path.Combine(_results, $"{st.FileIndex + 1:D2}-{Safe(st.S.Id)}.json"), ["shots"] = st.Result["shots"], ["waitsMs"] = st.Result["waitsMs"],
        };

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

        // ───────────── output ─────────────

        private void WriteScenario(ScenarioState st, int i)
        {
            try { AtomicWrite(Path.Combine(_results, $"{i + 1:D2}-{Safe(st.S.Id)}.json"), st.Result.ToString(Formatting.Indented)); }
            catch (Exception e) { Say("write scenario failed: " + e.Message); }
        }

        private string Headline()
        {
            int pass = 0, fail = 0, unk = 0, nj = 0;
            foreach (var st in _states)
                switch ((string)st.Result["result"]) { case "PASS": pass++; break; case "FAIL": fail++; break; case "UNKNOWN": unk++; break; default: nj++; break; }
            string state = _summary["aborted"] != null ? $"INCOMPLETE: the game quit in phase {_summary["aborted"]["phase"]} without the suite asking"
                : _summary["fatal"] != null ? "FATAL: " + _summary["fatal"]
                : _phase == "done" || _phase == "quit" ? "finished"
                : _phase.StartsWith("queue") ? "queue session (open)" : "in progress (phase " + _phase + ")";
            int unsettled = _contact.Count(c => !c.Settled);
            return $"{state}. {_states.Count} scenarios: {pass} PASS, {fail} FAIL, {unk} UNKNOWN, {nj} not judged. Shots: {_contact.Count}, unsettled {unsettled}.";
        }

        private void WriteSummary()
        {
            try
            {
                _summary["runId"] = _cfg.runId;
                _summary["set"] = _cfg.set;
                _summary["git"] = _cfg.git;
                _summary["assembly"] = AssemblyVersion();
                _summary["phase"] = _phase;
                _summary["headline"] = Headline();
                _summary["timeline"] = _timeline;
                _summary["profile"] = _cfg.profile;
                lock (_errLock)
                {
                    _summary["errorsTotal"] = _errors;
                    _summary["exceptionsTotal"] = _exceptions;
                    _summary["errorLinesFirst"] = new JArray(_errorLines.Take(60));
                    _summary["otherErrors"] = _otherErrors;
                    _summary["otherErrorLinesFirst"] = new JArray(_otherLines.Take(40));
                }
                if (_fixture != null) _summary["quarantined"] = JObject.FromObject(_fixture.Quarantined);
                var rows = new JArray();
                foreach (var st in _states)
                {
                    var r = st.Result;
                    var b = st.S.Block;
                    rows.Add(new JObject
                    {
                        ["id"] = st.S.Id, ["result"] = r["result"] ?? "NOT JUDGED", ["block"] = b?.Id, ["kind"] = b?.Kind.ToString(), ["siteRequested"] = st.S.Site,
                        ["streets"] = b == null ? null : $"W {b.West} / E {b.East} / S {b.South} / N {b.North}",
                        ["status"] = r["status"], ["failed"] = r["failed"], ["unknown"] = r["unknown"], ["sec"] = r["scenarioSec"], ["failure"] = r["failure"], ["unready"] = r["unready"],
                        ["buildPath"] = r["buildPath"], ["pace"] = r["pace"],
                        ["leftOut"] = r["census"]?["audit"]?["leftOut"], ["kept"] = r["census"]?["audit"]?["kept"],
                        ["seen"] = r["visibility"]?["visible"], ["objects"] = r["visibility"]?["objects"], ["pitCells"] = r["terrain"]?["pitCells"], ["maxPitDepth"] = r["terrain"]?["maxPitDepth"],
                        ["planComputeMs"] = r["waitsMs"]?["planComputeMs"], ["shots"] = r["shots"], ["waitsMs"] = r["waitsMs"], ["timeouts"] = r["timeouts"],
                    });
                }
                _summary["scenarios"] = rows;
                _summary["fidelity"] = Fidelity();
                AtomicWrite(Path.Combine(_out, "summary.json"), _summary.ToString(Formatting.Indented));
                AtomicWrite(Path.Combine(_out, "summary.md"), SummaryMarkdown());
                WriteContact();
            }
            catch (Exception e) { Say("write summary failed: " + e.Message); }
        }

        private JObject Fidelity()
        {
            int noPacks = _states.Count(st => (st.Result["planIssues"] as JArray)?.Any(i => ((string)i["key"] ?? "").Contains("NoPacks")) ?? false);
            int items = -1;
            try { items = Services.Assets?.Ready == true ? Services.Assets.Items("").Count : -1; } catch (Exception) { }
            return new JObject
            {
                ["profile"] = _cfg.profile, ["codeMods"] = (_summary["codeMods"] as JArray)?.Count ?? 0, ["catalogItems"] = items, ["plansWithoutAssetPacks"] = noPacks,
                ["caveat"] = _cfg.profile == "full" ? "full playset" :
                    "clean profile: no asset packs or other code mods are loaded. Decals, surfaces, centerpieces/fountains, park dressing and the switching hang differ from the user's game; " +
                    "treat visual and decal claims and 'switching ok' as needing a full-playset run (Run-Suite -AllMods).",
                ["readabilityMode"] = _look?.Enabled ?? false,
            };
        }

        private string SummaryMarkdown()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"# Suite run {_cfg.runId} ({_cfg.set})");
            sb.AppendLine();
            sb.AppendLine("**" + Headline() + "**");
            sb.AppendLine();
            sb.AppendLine("Contact sheet: `contact.html` (every shot in order, captioned). Results per scenario: `scenarios/NN-<id>.json`.");
            sb.AppendLine();
            sb.AppendLine($"Build: git `{_cfg.git}`, assembly `{AssemblyVersion()}`, dll `{OwnDllHash()}`" + (_summary["wrongBuild"] != null ? $" — **WRONG BUILD: {_summary["wrongBuild"]}**" : "") + (_summary["gitMismatch"] != null ? $" — {_summary["gitMismatch"]}" : ""));
            var fid = _summary["fidelity"] as JObject ?? Fidelity();
            sb.AppendLine($"Fidelity: profile **{fid["profile"]}**, {fid["codeMods"]} code mods, catalog {fid["catalogItems"]} items, {fid["plansWithoutAssetPacks"]} plans without asset packs. {fid["caveat"]}");
            if (_summary["look"] is JObject look) sb.AppendLine("Look (readability mode, not the user's look): " + LookLine(look));
            if (_summary["placemakerSettings"] is JObject ps) sb.AppendLine($"Placemaker settings: {ps["source"]}; differs from the user's: {(ps["differsFromUser"] as JObject)?.Count ?? 0} ({Trunc(ps["differsFromUser"]?.ToString(Formatting.None), 300)})");
            sb.AppendLine($"Grid: {_summary["grid"]}; quarantined blocks: {_summary["quarantined"]?.ToString(Formatting.None) ?? "{}"}");
            if (_summary["noiseFloor"] is JObject nf) sb.AppendLine($"Shot noise floor: {nf["changed16"]} px / tile {nf["tile32"]} -> settle limits {nf["limits"]?.ToString(Formatting.None)}");
            sb.AppendLine($"Timeline (s since process start): {_timeline.ToString(Formatting.None)}");
            if (_summary["fatal"] != null) sb.AppendLine($"\n**FATAL:** {_summary["fatal"]}");
            if (_summary["batchStopped"] != null) sb.AppendLine($"\n**BATCH STOPPED:** {_summary["batchStopped"]}");
            if (_summary["schemaErrors"] is JArray se && se.Count > 0) sb.AppendLine($"\n**Schema errors:** {se.ToString(Formatting.None)}");
            if (_summary["switching"] is JObject sw)
                sb.AppendLine($"\nSwitching ({sw["profile"]} profile): status {sw["status"]}, worst frame {sw["worstFrameMs"]} ms, catalog versions during switching {sw["catalogVersions"]}. {sw["note"]}");
            sb.AppendLine();
            sb.AppendLine("| # | scenario | result | block (asked) | status | build path | s | plan ms (max compute) | failed | unknown |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|");
            for (int i = 0; i < _states.Count; i++)
            {
                var st = _states[i];
                var r = st.Result;
                string J(JToken t) => t is JArray a && a.Count > 0 ? string.Join(", ", a.Select(x => (string)x)) : "";
                sb.AppendLine($"| {i + 1} | {st.S.Id} | {r["result"] ?? "NOT JUDGED"} | {st.S.Block?.Id} {st.S.Block?.Kind} ({st.S.Site}) | {r["status"]} | {r["buildPath"]} | {r["scenarioSec"]} | {r["waitsMs"]?["planComputeMs"]} | {J(r["failed"])} | {J(r["unknown"])} |");
            }
            sb.AppendLine();
            // scenarios that switch type or style more than once: one row per switch (what an architect timing styles needs)
            foreach (var st in _states)
            {
                var sws = (st.Result["steps"] as JArray)?.OfType<JObject>().Where(x => (string)x["do"] == "type" || (string)x["do"] == "preset").ToList();
                if (sws == null || sws.Count < 2) continue;
                sb.AppendLine($"### {st.S.Id}: type and style switches");
                sb.AppendLine("| step | do | style | plan compute ms | plan wait ms | worst frame ms | error |");
                sb.AppendLine("|---|---|---|---|---|---|---|");
                foreach (var x in sws) sb.AppendLine($"| {x["i"]} | {x["do"]} | {x["activePreset"] ?? x["preset"]} | {x["planComputeMs"]} | {x["waits"]?["plan"]} | {x["frames"]?["maxMs"]} | {x["error"]} |");
                sb.AppendLine();
            }
            var unsettled = _contact.Where(c => !c.Settled).ToList();
            if (unsettled.Count > 0)
            {
                sb.AppendLine("## Unreliable evidence (shots that did not settle)");
                foreach (var c in unsettled) sb.AppendLine($"- {c.Id} step {c.Step} {c.Name}: {c.File ?? c.Error}");
                sb.AppendLine();
            }
            sb.AppendLine("## Checks that did not pass");
            foreach (var st in _states)
            {
                if (!(st.Result["verdicts"] is JArray v)) { sb.AppendLine($"- **{st.S.Id}**: not judged"); continue; }
                var bad = v.Where(x => (string)x["result"] != "pass").ToList();
                if (bad.Count == 0) continue;
                sb.AppendLine($"- **{st.S.Id}** ({st.S.Block?.Id}): " + string.Join("; ", bad.Select(x => $"{x["id"]} [{x["result"]}]: {Trunc((string)x["detail"], 300)}")));
            }
            return sb.ToString();
        }
    }
}
