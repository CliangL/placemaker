// In-game test suite: verdicts. Every check is "pass" only when it was measured and is right, "fail" when measured
// and wrong, "unknown" when it could not be measured (with the reason). Which checks a scenario MUST have depends on
// what it did (a build expects the census checks, a delete expects the leftover checks, ...): an expected check that
// produced no measurement is "unknown", never silently missing. The scenario's result is PASS only when every
// expected check passed, UNKNOWN when nothing failed but something was not measured, FAIL otherwise.

using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Placemaker.Core.Suite;

namespace Placemaker.Game.Suite
{
    public sealed partial class SuiteRunner
    {
        /// <summary>Judges a scenario now (called right after it ran, and again after a grow).</summary>
        private void Verdicts(ScenarioState st, string phase = "after run")
        {
            var r = st.Result;
            var v = new JArray();
            var expected = new List<string>();
            void Check(string id, bool? pass, string detail)
            {
                foreach (var x in v.OfType<JObject>().Where(x => (string)x["id"] == id).ToList()) x.Remove();
                v.Add(new JObject { ["id"] = id, ["result"] = pass == null ? "unknown" : pass.Value ? "pass" : "fail", ["detail"] = detail ?? "" });
            }
            void Expect(string id) { if (!expected.Contains(id)) expected.Add(id); }
            JObject SuiteErr(JToken t) => t is JObject o && o["suiteError"] != null ? o : null;

            string status = (string)r["status"] ?? "";
            var steps = (r["steps"] as JArray)?.OfType<JObject>().ToList() ?? new List<JObject>();
            bool scripted = st.S.Steps != null;
            bool wantsBuild = !scripted || st.S.Steps.Any(x => (string)x["do"] == "build");
            bool built = st.BuildCount > 0;
            bool deleted = steps.Any(x => x["leftBehind"] != null);

            // the scenario ran, on the block it asked for
            Expect("ran");
            if (status == "refused" || status == "tool-not-idle") Check("ran", false, (string)r["refused"]);
            else if (status == "no-block") Check("ran", false, (string)r["siteNote"] ?? "no block");
            else if (status == "error") Check("ran", false, "suite error: " + (string)r["exception"]);
            else Check("ran", true, "");
            if (st.S.Block != null)
            {
                Expect("siteAsAsked");
                var want = SuiteFixture.KindOf(st.S.Site);
                bool same = !want.HasValue || st.S.Block.Kind == want.Value;
                Check("siteAsAsked", same, same ? $"{st.S.Block.Id} {st.S.Block.Kind}" : $"wanted {st.S.Site}, got {st.S.Block.Id} {st.S.Block.Kind}: {st.S.SiteNote}");
            }

            // every step did what it was asked (a step error from the suite itself is 'unknown', not Placemaker's failure)
            foreach (var s in steps.Where(x => x["error"] != null))
            {
                bool suite = (bool?)s["suiteError"] ?? false;
                Check("step" + s["i"] + ":" + s["do"], suite ? (bool?)null : false, (string)s["error"]);
            }
            foreach (var s in steps.Where(x => x["outline"] is JObject))
            {
                Expect("outlineAsIntended");
                var o = (JObject)s["outline"];
                Check("outlineAsIntended", (bool?)o["ok"], (string)o["detail"]);
            }

            Expect("noTimeouts");
            var timeouts = (r["timeouts"] as JArray ?? new JArray()).Select(x => (string)x).Where(x => !x.StartsWith("shot ")).ToList();
            Check("noTimeouts", timeouts.Count == 0, string.Join("; ", timeouts));
            if (r["shotsSettled"] is JObject ss && ss.Count > 0)
            {
                Expect("shotsSettled");
                var unsettled = ss.Properties().Where(p => !(bool)p.Value).Select(p => p.Name).ToList();
                Check("shotsSettled", unsettled.Count == 0, unsettled.Count == 0 ? $"{ss.Count} shots settled" : "UNRELIABLE EVIDENCE, unsettled: " + string.Join(", ", unsettled));
            }

            if (wantsBuild)
            {
                Expect("built");
                var vis = r["visibility"] as JObject;
                int members = (int?)r["builds"]?.Last?["members"] ?? 0;
                int seen = (int?)vis?["visible"] ?? 0;
                bool materialized = built && members > 0 && (vis == null || (int)vis["objects"] == 0 || seen > 0);
                Check("built", built ? materialized : false, built ? (materialized ? $"built ({members} members, path {r["buildPath"]})" : $"built but nothing materialized ({members} members, {seen} seen)") :
                    status + (r["failure"] != null ? ": " + r["failure"] : "") + (r["unready"] != null ? ": " + r["unready"] : ""));
            }
            if (built)
            {
                // seen, not just alive
                Expect("plannedVisible");
                var vis = r["visibility"] as JObject;
                if (SuiteErr(vis) is JObject e1) Check("plannedVisible", null, (string)e1["suiteError"]);
                else if (vis == null) Check("plannedVisible", null, "visibility not measured");
                else
                {
                    int objects = (int)vis["objects"], visible = (int)vis["visible"];
                    Check("plannedVisible", objects == 0 ? (bool?)null : visible == objects,
                        objects == 0 ? "no member objects that are meant to be seen" : $"{visible} of {objects} built objects seen (overridden {vis["overridden"]}, hidden {vis["hidden"]}, not rendered {vis["notRendered"]}) {vis["notSeen"]?.ToString(Formatting.None)}");
                }
                if (r["visibilityAtEnd"] is JObject ve && ve["objects"] != null && vis != null && vis["objects"] != null)
                {
                    Expect("noLaterDamage");
                    int before = (int)vis["visible"], after = (int)ve["visible"];
                    Check("noLaterDamage", after >= before && (int)ve["objects"] >= (int)vis["objects"], $"seen right after the build {before} of {vis["objects"]}, after all later scenarios {after} of {ve["objects"]} {ve["notSeen"]?.ToString(Formatting.None)}");
                }
                if (r["visibilityAfterGrow"] is JObject vg && vg["objects"] != null)
                {
                    Expect("plannedVisibleAfterSim");
                    int objects = (int)vg["objects"], visible = (int)vg["visible"];
                    Check("plannedVisibleAfterSim", objects == 0 ? (bool?)null : visible == objects, $"{visible} of {objects} seen after the simulation ran {vg["notSeen"]?.ToString(Formatting.None)}");
                }
                if (st.Ghost != null)
                {
                    Expect("previewMatchesResult");
                    var pvr = r["previewVsResult"] as JObject;
                    if (SuiteErr(pvr) is JObject e2) Check("previewMatchesResult", null, (string)e2["suiteError"]);
                    else if (pvr == null) Check("previewMatchesResult", null, "not measured");
                    else if ((bool?)pvr["stale"] == true) Check("previewMatchesResult", null, (string)pvr["detail"]);
                    else
                    {
                        int miss = (int)pvr["missing"];
                        int left = (int?)r["census"]?["audit"]?["leftOut"] ?? 0;
                        Check("previewMatchesResult", miss <= left, $"{pvr["seenObjects"]} objects seen after the build, {pvr["ghostObjects"]} in the ghost; {miss} shown in the ghost but not seen after the build (left out with a reason: {left}) {pvr["short"]?.ToString(Formatting.None)}");
                    }
                }
                Expect("census");
                var census = r["census"] as JObject;
                if (SuiteErr(census) is JObject e3) Check("census", null, (string)e3["suiteError"]);
                else if (census == null) Check("census", null, "no census was taken (no outline snapshot)");
                else
                {
                    Check("census", true, $"{census["members"]} members");
                    var audit = census["audit"] as JObject;
                    // Placemaker's own report (absent for types that keep none): judged when present, never required
                    if (audit != null) Check("nothingLeftOut", (int)audit["leftOut"] == 0, $"Placemaker says: left out {audit["leftOut"]}, kept despite collisions {audit["kept"]} {audit["reasons"]?.ToString(Formatting.None)}");
                    var match = census["match"] as JObject;
                    Expect("nothingMissing");
                    if (match == null || match["error"] != null) Check("nothingMissing", null, (string)match?["error"] ?? "no plan to match");
                    else
                    {
                        int missObj = (int)match["missingObjects"];
                        float netMiss = (float)match["netMissingM"];
                        int admitted = audit != null ? (int)audit["leftOut"] : 0;
                        Check("nothingMissing", missObj <= admitted && netMiss < 1f, $"independent match: {match["matchedObjects"]} of {match["plannedObjects"]} planned objects within 0.75 m, {missObj} missing (left out with a reason: {admitted}), nets short by {netMiss} m {match["missing"]?.ToString(Formatting.None)}");
                    }
                    var orph = r["orphans"] as JObject;
                    Expect("noUntrackedPieces");
                    if (SuiteErr(orph) is JObject e4) Check("noUntrackedPieces", null, (string)e4["suiteError"]);
                    else if (orph == null) Check("noUntrackedPieces", null, "not measured");
                    else Check("noUntrackedPieces", (int)orph["count"] == 0, (int)orph["count"] == 0 ? "every new piece in the neighbourhood belongs to the build" : $"{orph["count"]} new pieces belong to no member: {orph["byPrefab"]?.ToString(Formatting.None)} at {orph["at"]?.ToString(Formatting.None)}");
                    bool farm = (bool?)census["farmType"] ?? false;
                    if (!farm)
                    {
                        Expect("noFarmland");
                        var fl = census["farmland"] as JObject;
                        Check("noFarmland", fl == null ? (bool?)null : (int)fl["extractorAreas"] == 0, fl == null ? "not measured" : (int)fl["extractorAreas"] == 0 ? "no extractor (crop/forestry) areas" + ((fl["nameHints"] as JObject)?.Count > 0 ? "; name hints only: " + fl["nameHints"].ToString(Formatting.None) : "") : $"{fl["extractorAreas"]} extractor areas: {fl["prefabs"]?.ToString(Formatting.None)}");
                    }
                    var fences = census["fences"] as JObject;
                    Expect("noForeignFences");
                    if (fences == null) Check("noForeignFences", null, "fence lanes not measured");
                    else
                    {
                        float foreign = (float)fences["foreignVisibleM"];
                        float member = (float)fences["memberVisibleM"];
                        bool planHas = (bool)fences["planHasFences"];
                        Check("noForeignFences", foreign < 0.5f && (planHas || member < 0.5f), $"visible fence: {member} m of the build's own (plan has fences: {planHas}), {foreign} m foreign {fences["byOwner"]?.ToString(Formatting.None)}");
                    }
                    var parking = census["parking"] as JObject;
                    if (parking != null && (int)parking["plannedStalls"] > 0)
                    {
                        Expect("stallsBuilt");
                        Check("stallsBuilt", (int)parking["seenStalls"] >= (int)parking["plannedStalls"], $"planned {parking["plannedStalls"]}, alive {parking["liveStalls"]}, seen {parking["seenStalls"]}");
                        Expect("stallsUsable");
                        if (parking["stallsReachableByCar"] == null) Check("stallsUsable", null, "the stalls' parking lanes were not measured");
                        else Check("stallsUsable", (int)parking["stallsReachableByCar"] >= (int)parking["plannedStalls"], $"{parking["stallsReachableByCar"]} of {parking["plannedStalls"]} planned stalls have a parking lane a car reaches from the city ({parking["stallsWithParkingLane"]} have a parking lane)");
                    }
                    var nets = census["nets"] as JObject;
                    if (nets != null && (int)nets["edges"] > 0)
                    {
                        Expect("netsConnected");
                        if (nets["isolatedEdges"] == null) Check("netsConnected", null, "lanes not measured");
                        else Check("netsConnected", (int)nets["isolatedEdges"] == 0, $"{nets["isolatedEdges"]} of {nets["withLanes"]} member nets with lanes not reachable from a city street through lanes {nets["isolatedPrefabs"]?.ToString(Formatting.None)}");
                    }
                    var zoning = census["zoning"] as JObject;
                    if (zoning != null && (int)zoning["plannedZonedCells"] > 0)
                    {
                        Expect("zoningLive");
                        int planned = (int)zoning["plannedZonedCells"], matched = (int)zoning["matchedPlanned"];
                        Check("zoningLive", matched >= 0.9 * planned, $"{matched} of {planned} planned zone cells are newly zoned ({zoning["newlyZonedCells"]} newly zoned in all)");
                        Expect("noZoningSpill");
                        Check("noZoningSpill", (int)zoning["unplannedZonedOutsideOutline"] == 0, $"{zoning["unplannedZonedOutsideOutline"]} newly zoned cells outside the outline the plan did not zone ({zoning["unplannedZoned"]} unplanned in all)");
                    }
                    var ex = census["existingBuildings"] as JObject;
                    if (ex != null && (int)ex["before"] > 0)
                    {
                        Expect("existingSurvive");
                        Check("existingSurvive", (int)ex["lost"] + (int)ex["hidden"] + (int)ex["moved"] == 0, $"{ex["before"]} buildings in the site and 30 m around: lost {ex["lost"]}, hidden {ex["hidden"]}, moved {ex["moved"]} {ex["detail"]?.ToString(Formatting.None)}");
                    }
                    if (r["existingAfterGrow"] is JObject eg && (int)eg["before"] > 0)
                    {
                        Expect("existingSurviveAfterSim");
                        Check("existingSurviveAfterSim", (int)eg["lost"] + (int)eg["hidden"] + (int)eg["moved"] == 0, $"after the simulation: lost {eg["lost"]}, hidden {eg["hidden"]}, moved {eg["moved"]}");
                    }
                }
                if (st.Markers.Count > 0)
                {
                    Expect("entrancesUsable");
                    var ents = r["entrances"] as JArray;
                    if (ents == null || ents.Count == 0) Check("entrancesUsable", null, "entrances placed but not measured");
                    else
                    {
                        var bad = ents.OfType<JObject>().Where(e => !((bool?)e["usable"] ?? false)).ToList();
                        Check("entrancesUsable", bad.Count == 0, bad.Count == 0 ? string.Join("; ", ents.OfType<JObject>().Select(e => (string)e["detail"])) :
                            string.Join("; ", bad.Select(e => $"{e["mode"]} entrance at {e["at"]?.ToString(Formatting.None)}: {e["detail"]}")));
                    }
                }
                var terrain = r["terrain"] as JObject;
                Expect("noPits");
                if (SuiteErr(terrain) is JObject e5) Check("noPits", null, (string)e5["suiteError"]);
                else if (terrain == null || (int?)terrain["samples"] == 0) Check("noPits", null, "no ground samples");
                else
                {
                    Check("noPits", (int)terrain["pitCells"] == 0, $"{terrain["pitCells"]} samples (2 m grid, outline + 20 m) more than 1.5 m below anything planned ({terrain["pitCellsNearRoads"]} near roads, {terrain["pitCellsOutsideOutline"]} outside the outline), deepest {terrain["maxPitDepth"]} m at {terrain["worstPitAt"]?.ToString(Formatting.None)}");
                    Expect("noMounds");
                    Check("noMounds", (int)terrain["moundCells"] == 0, $"{terrain["moundCells"]} samples more than 1.5 m above anything planned, highest {terrain["maxMoundHeight"]} m at {terrain["worstMoundAt"]?.ToString(Formatting.None)}");
                }
                // growth facing the road the plan meant (judged on the growables of the shared grow or a grow step)
                var zplan = (int?)r["census"]?["zoning"]?["plannedZonedCells"] ?? 0;
                var gr = (r["growablesAfterGrow"] ?? r["growablesAtStep"]) as JObject;
                bool grew = r["growablesAfterGrow"] != null || r["growablesAtStep"] != null;
                if (zplan > 0 && grew)
                {
                    Expect("growablesFaceIntendedRoad");
                    if (gr == null || SuiteErr(gr) != null) Check("growablesFaceIntendedRoad", null, "not measured");
                    else if ((int)gr["onPlannedCells"] == 0) Check("growablesFaceIntendedRoad", null, $"unknown: nothing grew on the plan's {zplan} zoned cells ({gr["new"]} new growables nearby; fast spawn bypasses demand)");
                    else Check("growablesFaceIntendedRoad", (int)gr["onPlannedFacingCity"] == 0 && (int)gr["noRoad"] == 0,
                        $"{gr["onPlannedCells"]} grew on the plan's cells: {gr["onPlannedFacingOwn"]} face the build's own road, {gr["onPlannedFacingCity"]} face a city street, {gr["noRoad"]} no road");
                }
            }
            if (deleted)
            {
                foreach (var s in steps.Where(x => x["leftBehind"] != null))
                {
                    var lb = s["leftBehind"] as JObject;
                    Expect("deleteLeavesNothing");
                    if (SuiteErr(lb) is JObject e6) { Check("deleteLeavesNothing", null, (string)e6["suiteError"]); continue; }
                    if (lb == null) { Check("deleteLeavesNothing", null, "not measured"); continue; }
                    int n = (int)lb["count"], cells = (int?)lb["zoneCellsChanged"] ?? 0;
                    Check("deleteLeavesNothing", n == 0 && cells == 0, $"in the whole block + 10 m: {n} pieces of no city street left that were not there before the build {lb["byPrefab"]?.ToString(Formatting.None)} at {lb["at"]?.ToString(Formatting.None)}; zoned cells {lb["zonedCellsBefore"]} before, {lb["zonedCellsAfter"]} after");
                    Expect("deleteRestoresStreets");
                    int removed = (int?)lb["removedFromCity"] ?? 0, rebuilt = (int?)lb["cityRebuilt"] ?? 0;
                    Check("deleteRestoresStreets", removed == 0 && rebuilt == 0, removed == 0 && rebuilt == 0 ? "the streets around are as before the build" :
                        $"the streets around changed: {removed} pieces that stood before are gone {lb["removedByPrefab"]?.ToString(Formatting.None)}, {rebuilt} street pieces stand elsewhere now {lb["cityRebuiltByPrefab"]?.ToString(Formatting.None)} (a street split for the build stays split)");
                    if (lb["ground"] is JObject g && g["changed"] != null)
                    {
                        bool perm = (bool?)lb["permanentTerraform"] ?? false;
                        int changed = (int)g["changed"];
                        // permanent terraform is meant to stay: then the ground is reported, not judged
                        if (!perm) { Expect("deleteRestoresGround"); Check("deleteRestoresGround", changed == 0, $"{changed} of {g["samples"]} ground samples off by more than 0.25 m after the delete (worst {g["worstM"]} m at {g["worstAt"]?.ToString(Formatting.None)})"); }
                    }
                }
            }

            Expect("noErrors");
            var errs = r["errors"] as JObject;
            Check("noErrors", errs == null ? (bool?)null : (int)errs["errors"] + (int)errs["exceptions"] + ((int?)errs["otherErrors"] ?? 0) == 0,
                errs == null ? "error log not read" : $"{errs["errors"]} Placemaker errors, {errs["exceptions"]} exceptions, {errs["otherErrors"]} other errors (game or other mods) during the scenario {errs["lines"]?.ToString(Formatting.None)}");
            Expect("noHang");
            float worstFrame = 0f;
            string worstAt = "";
            int steps250 = 0;
            foreach (var s in steps)
                if (s["frames"] is JObject f && f["maxMs"] != null && (string)s["do"] != "wait" && (string)s["do"] != "grow")
                {
                    steps250 += (int?)f["over250"] ?? 0;
                    if ((float)f["maxMs"] > worstFrame) { worstFrame = (float)f["maxMs"]; worstAt = $"step {s["i"]} {s["do"]} (frame {f["maxFrame"]}, suite work {f["maxSuiteMs"]} ms excluded)"; }
                }
            Check("noHang", worstFrame < 1000f, $"worst frame {worstFrame:F0} ms at {worstAt}; {steps250} frames over 250 ms");

            // the scenario's one result
            foreach (var id in expected) if (!v.OfType<JObject>().Any(x => (string)x["id"] == id)) Check(id, null, "expected for this scenario but not measured");
            r["verdicts"] = v;
            r["verdictPhase"] = phase;
            r["failed"] = new JArray(v.Where(x => (string)x["result"] == "fail").Select(x => (string)x["id"]));
            r["unknown"] = new JArray(v.Where(x => (string)x["result"] == "unknown").Select(x => (string)x["id"]));
            r["result"] = ((JArray)r["failed"]).Count > 0 ? "FAIL" : ((JArray)r["unknown"]).Count > 0 ? "UNKNOWN" : "PASS";
        }
    }
}
