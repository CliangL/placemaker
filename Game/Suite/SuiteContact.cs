// In-game test suite: the run's contact sheet (contact.html beside summary.md): every shot in scenario and step order,
// each captioned with its step, shot mode, whether it settled (and how long that took) and the camera actually
// rendered, and each scenario headed by its result and the checks that did not pass. The raw PNGs stay untouched.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Placemaker.Game.Suite
{
    public sealed partial class SuiteRunner
    {
        private sealed class ShotRow
        {
            public int Scenario, Step, SettleMs;
            public string Id, Do, Name, File, Mode, Error;
            public bool Settled;
            public JObject Camera;
        }

        private readonly List<ShotRow> _contact = new List<ShotRow>();

        private static string H(string s) => WebUtility.HtmlEncode(s ?? "");

        private void WriteContact()
        {
            try
            {
                var sb = new StringBuilder();
                sb.Append("<!doctype html><html><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">");
                sb.Append("<title>Suite " + H(_cfg.runId) + "</title><style>");
                sb.Append(":root{--bg:#f6f6f4;--fg:#1d1d1b;--dim:#6b6b66;--card:#fff;--line:#dcdcd6;--pass:#1f7a3a;--fail:#b3261e;--unk:#8a6d00}");
                sb.Append("@media (prefers-color-scheme:dark){:root{--bg:#161615;--fg:#ececea;--dim:#a3a39d;--card:#21211f;--line:#3a3a37;--pass:#5cc27a;--fail:#ff8a80;--unk:#e6c34a}}");
                sb.Append("body{background:var(--bg);color:var(--fg);font:14px/1.4 system-ui,sans-serif;margin:0 16px 40px}h1{font-size:20px}h2{font-size:16px;margin:28px 0 6px}");
                sb.Append(".sc{border-top:1px solid var(--line);padding-top:6px}.r-PASS{color:var(--pass)}.r-FAIL{color:var(--fail)}.r-UNKNOWN{color:var(--unk)}");
                sb.Append(".row{display:flex;flex-wrap:wrap;gap:10px}.shot{background:var(--card);border:1px solid var(--line);border-radius:6px;padding:6px;width:min(400px,100%);box-sizing:border-box}");
                sb.Append(".shot img{width:100%;height:auto;display:block;border-radius:3px}.cap{font-size:12px;color:var(--dim);margin-top:4px}.bad{color:var(--fail);font-weight:600}");
                sb.Append("ul{margin:4px 0 0 18px;padding:0}li{margin:2px 0}code{font-size:12px}</style></head><body>");
                sb.Append("<h1>Suite run " + H(_cfg.runId) + " (" + H(_cfg.set) + ")</h1>");
                sb.Append("<p>" + H(Headline()) + "</p>");
                if (_summary["look"] is JObject look) sb.Append("<p class=\"cap\">Look: " + H(LookLine(look)) + "</p>");
                var byScenario = _contact.GroupBy(r => r.Scenario).ToDictionary(g => g.Key, g => g.ToList());
                for (int i = 0; i < _states.Count; i++)
                {
                    var st = _states[i];
                    var r = st.Result;
                    string result = (string)r["result"] ?? "NOT JUDGED";
                    sb.Append("<div class=\"sc\"><h2>" + (i + 1) + ". " + H(st.S.Id) + " <span class=\"r-" + H(result) + "\">" + H(result) + "</span></h2>");
                    sb.Append("<div class=\"cap\">" + H($"{st.S.Block?.Id} {st.S.Block?.Kind} (asked {st.S.Site}) · status {r["status"]} · {r["scenarioSec"]} s · pace {r["pace"]}{(r["buildPath"] != null ? " · build path " + r["buildPath"] : "")}") + "</div>");
                    if (r["verdicts"] is JArray v)
                    {
                        var bad = v.OfType<JObject>().Where(x => (string)x["result"] != "pass").ToList();
                        if (bad.Count > 0)
                        {
                            sb.Append("<ul>");
                            foreach (var x in bad) sb.Append("<li><span class=\"r-" + ((string)x["result"] == "fail" ? "FAIL" : "UNKNOWN") + "\">" + H((string)x["result"]) + "</span> <b>" + H((string)x["id"]) + "</b>: " + H(Trunc((string)x["detail"], 400)) + "</li>");
                            sb.Append("</ul>");
                        }
                    }
                    sb.Append("<div class=\"row\">");
                    if (byScenario.TryGetValue(st.FileIndex, out var shots))
                        foreach (var s in shots.OrderBy(x => x.Step))
                        {
                            var cam = s.Camera;
                            var eff = cam?["effective"] as JObject;
                            string camLine = cam == null ? "" : $"{cam["target"]} {cam["view"]}" + (eff != null ? $" · pitch {eff["pitch"]}° yaw {eff["yaw"]}° {eff["distance"]} m fov {eff["fov"]}°" : "");
                            sb.Append("<div class=\"shot\">");
                            if (s.File != null) sb.Append("<a href=\"" + H(s.File) + "\"><img loading=\"lazy\" src=\"" + H(s.File) + "\" alt=\"" + H(s.Name) + "\"></a>");
                            else sb.Append("<div class=\"bad\">no picture: " + H(s.Error) + "</div>");
                            sb.Append("<div class=\"cap\">step " + s.Step + " <b>" + H(s.Do) + "</b> · " + H(s.Name) + " · " + H(s.Mode) + " · "
                                + (s.Settled ? "settled in " + s.SettleMs + " ms" : "<span class=\"bad\">UNSETTLED (" + s.SettleMs + " ms): unreliable</span>")
                                + (camLine.Length > 0 ? "<br>" + H(camLine) : "") + "</div></div>");
                        }
                    sb.Append("</div></div>");
                }
                sb.Append("</body></html>");
                File.WriteAllText(Path.Combine(_out, "contact.html"), sb.ToString());
            }
            catch (Exception e) { Say("contact sheet failed: " + e.Message); }
        }

        private static string Trunc(string s, int n) => s == null ? "" : s.Length <= n ? s : s.Substring(0, n) + "…";

        private static string LookLine(JObject look) =>
            $"{look["mode"]}; sun {look["sunElevation"]}° up, azimuth {look["sunAzimuth"]}° (latitude {look["latitude"]}, day {look["day"]}, {look["time"]} h); exposure EV {look["exposureEv"]}; "
            + $"weather off; wind 0; icons hidden on {look["iconPrefabsHidden"]} prefabs; street names {look["streetLabels"]}; LOD {look["levelOfDetail"]}, cross-fade {look["lodCrossFade"]}; window {look["window"]}";
    }
}
