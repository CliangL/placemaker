// In-game test suite runner (docs/teardown/10-ingame-suite.md §4.2, revised for one launch / many scenarios):
//   boot → main menu → new game on the game's own flat default world (no map) → fixture (heightmap, road grid,
//   zoning, plops, pre-grow) → the switching scenario → every scenario on its own block (outline → type/preset/values →
//   preview + ghost shot → Build → census) → one shared grow phase (debugFastSpawn, speed 4) → growables census,
//   oblique and top shots per block → summary.json/summary.md → the game's own clean quit.
// A MonoBehaviour with coroutines (not an ECS system): the steps wait on frames, the end of frame (screen grab) and
// on Tasks, and read top to bottom. Every scenario runs inside a guard, so an exception fails that scenario only.
// Exists only when SuiteGate opened (command-line token + armed run.json).

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Colossal.IO.AssetDatabase;
using Colossal.Logging;
using Colossal.Serialization.Entities;
using Game;
using Game.City;
using Game.SceneFlow;
using Game.Simulation;
using Game.Tools;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Placemaker.Core;
using Placemaker.Core.Suite;
using Placemaker.Core.Tool;
using Placemaker.Game.Tool;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Placemaker.Game.Suite
{
    public sealed class SuiteRunner : MonoBehaviour
    {
        private SuiteRunConfig _cfg;
        private string _out, _shots, _results;
        private string _phase = "boot", _scenario = "";
        private int _index, _total;
        private DateTime _processStart;
        private float _lastBeat = -10f;
        private bool _started;
        private readonly JObject _timeline = new JObject();
        private readonly JObject _summary = new JObject();
        private readonly List<ScenarioState> _states = new List<ScenarioState>();
        private StreamWriter _log;

        // frame times
        private readonly List<float> _window = new List<float>(4096);
        private bool _windowOpen;
        private float _lastFrameMs, _worstRecentMs;
        private float _worstRecentAt;

        // errors (UnityLogger events can come from worker threads)
        private readonly object _errLock = new object();
        private int _errors, _exceptions;
        private readonly List<string> _errorLines = new List<string>();

        private World _world;
        private PlacemakerToolSystem _tool;
        private SuiteFixture _fixture;
        private SuiteProbe _probe;

        private sealed class ScenarioState
        {
            public Scenario S;
            public JObject Result = new JObject();
            public SiteSnapshot Before;
            public ComposedPlan Plan;
            public int Group;
            public bool Built;
            public float2[] Ring;
            public float3 Focus;
            public float Radius = 80f;
        }

        // ───────────── lifecycle ─────────────

        private void Start()
        {
            _cfg = SuiteGate.Config;
            _out = _cfg.outDir;
            _shots = Path.Combine(_out, "shots");
            _results = Path.Combine(_out, "scenarios");
            Directory.CreateDirectory(_shots);
            Directory.CreateDirectory(_results);
            try { _log = new StreamWriter(Path.Combine(_out, "suite.log"), true) { AutoFlush = true }; } catch (Exception) { }
            try { _processStart = System.Diagnostics.Process.GetCurrentProcess().StartTime.ToUniversalTime(); } catch (Exception) { _processStart = DateTime.UtcNow; }
            _timeline["processStartUtc"] = _processStart.ToString("o");
            _timeline["runnerStartSec"] = Since();
            UnityLogger.OnErrorOrHigher += OnLogError;
            UnityLogger.OnException += OnLogException;
            Say($"runner up: run {_cfg.runId}, set {_cfg.set}, git {_cfg.git}, assembly {AssemblyVersion()}");
        }

        private void OnDestroy()
        {
            UnityLogger.OnErrorOrHigher -= OnLogError;
            UnityLogger.OnException -= OnLogException;
            try { _log?.Dispose(); } catch (Exception) { }
        }

        private void OnLogError(ILog log, Level level, string message, Exception e, UnityEngine.Object context)
        {
            lock (_errLock)
            {
                _errors++;
                if (_errorLines.Count < 400) _errorLines.Add($"[{_scenario}] {log?.name}: {Trim(message)}{(e != null ? " | " + e.GetType().Name + ": " + Trim(e.Message) : "")}");
            }
        }

        private void OnLogException(Exception e, UnityEngine.Object context)
        {
            lock (_errLock)
            {
                _exceptions++;
                if (_errorLines.Count < 400) _errorLines.Add($"[{_scenario}] EXCEPTION {e?.GetType().Name}: {Trim(e?.Message)}");
            }
        }

        private static string Trim(string s) => s == null ? "" : s.Length > 300 ? s.Substring(0, 300) : s.Replace("\n", " | ");

        private void Update()
        {
            float ms = Time.unscaledDeltaTime * 1000f;
            _lastFrameMs = ms;
            if (_windowOpen) _window.Add(ms);
            if (ms > _worstRecentMs || Time.realtimeSinceStartup - _worstRecentAt > 10f) { _worstRecentMs = ms; _worstRecentAt = Time.realtimeSinceStartup; }
            if (!_started && GameManager.instance != null)
            {
                _started = true;
                StartCoroutine(Guard(Main(), e => Fatal("main", e)));
            }
            if (Time.realtimeSinceStartup - _lastBeat >= 2f) { _lastBeat = Time.realtimeSinceStartup; Beat(); }
        }

        private float Since() => (float)Math.Round((DateTime.UtcNow - _processStart).TotalSeconds, 1);

        private void Mark(string key) { _timeline[key] = Since(); Say($"timeline {key} = {_timeline[key]} s"); }

        private void Say(string s)
        {
            Mod.Log.Info("Placemaker suite: " + s);
            try { _log?.WriteLine($"{DateTime.UtcNow:HH:mm:ss.fff} [{_phase}/{_scenario}] {s}"); } catch (Exception) { }
        }

        private void Beat()
        {
            try
            {
                int err, exc;
                lock (_errLock) { err = _errors; exc = _exceptions; }
                var o = new JObject
                {
                    ["runId"] = _cfg.runId, ["phase"] = _phase, ["scenario"] = _scenario, ["index"] = _index, ["total"] = _total,
                    ["utc"] = DateTime.UtcNow.ToString("o"), ["sinceStart"] = Since(), ["frame"] = Time.frameCount,
                    ["frameMs"] = Math.Round(_lastFrameMs, 1), ["worstFrameMs10s"] = Math.Round(_worstRecentMs, 1), ["errors"] = err, ["exceptions"] = exc,
                    ["gameMode"] = GameManager.instance != null ? GameManager.instance.gameMode.ToString() : "",
                };
                File.WriteAllText(Path.Combine(_out, "heartbeat.json"), o.ToString(Formatting.None));
            }
            catch (Exception) { }
        }

        private static string AssemblyVersion()
        {
            try
            {
                var a = typeof(SuiteRunner).Assembly;
                var info = (System.Reflection.AssemblyInformationalVersionAttribute)Attribute.GetCustomAttribute(a, typeof(System.Reflection.AssemblyInformationalVersionAttribute));
                return info?.InformationalVersion ?? a.GetName().Version.ToString();
            }
            catch (Exception) { return "?"; }
        }

        // ───────────── coroutine helpers ─────────────

        /// <summary>Runs a coroutine tree (nested IEnumerators), turning an exception into onError instead of a dead coroutine.</summary>
        private IEnumerator Guard(IEnumerator root, Action<Exception> onError)
        {
            var stack = new Stack<IEnumerator>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                var top = stack.Peek();
                bool moved;
                object current = null;
                try
                {
                    moved = top.MoveNext();
                    if (moved) current = top.Current;
                }
                catch (Exception e)
                {
                    onError(e);
                    yield break;
                }
                if (!moved) { stack.Pop(); continue; }
                if (current is IEnumerator nested) { stack.Push(nested); continue; }
                yield return current;
            }
        }

        private static IEnumerator Frames(int n)
        {
            for (int i = 0; i < n; i++) yield return null;
        }

        private static IEnumerator Seconds(float s)
        {
            float until = Time.realtimeSinceStartup + s;
            while (Time.realtimeSinceStartup < until) yield return null;
        }

        private bool _timedOut;

        private IEnumerator Until(Func<bool> cond, float timeout, string what)
        {
            _timedOut = false;
            float until = Time.realtimeSinceStartup + timeout;
            while (true)
            {
                bool ok;
                try { ok = cond(); } catch (Exception) { ok = false; }
                if (ok) yield break;
                if (Time.realtimeSinceStartup > until)
                {
                    _timedOut = true;
                    Say($"timeout after {timeout:F0} s waiting for {what}");
                    yield break;
                }
                yield return null;
            }
        }

        private void OpenWindow() { _window.Clear(); _windowOpen = true; }

        private JObject CloseWindow()
        {
            _windowOpen = false;
            var (max, p95, n) = SuiteVerdicts.FrameStats(_window);
            int over250 = 0, over1000 = 0;
            foreach (var f in _window) { if (f > 250f) over250++; if (f > 1000f) over1000++; }
            return new JObject { ["frames"] = n, ["maxMs"] = Math.Round(max, 1), ["p95Ms"] = Math.Round(p95, 1), ["over250"] = over250, ["over1000"] = over1000 };
        }

        private (int Errors, int Exceptions, int Lines) ErrorMark()
        {
            lock (_errLock) return (_errors, _exceptions, _errorLines.Count);
        }

        private JObject ErrorsSince((int Errors, int Exceptions, int Lines) mark)
        {
            lock (_errLock)
            {
                var lines = new JArray();
                for (int i = mark.Lines; i < _errorLines.Count && lines.Count < 15; i++) lines.Add(_errorLines[i]);
                return new JObject { ["errors"] = _errors - mark.Errors, ["exceptions"] = _exceptions - mark.Exceptions, ["lines"] = lines };
            }
        }

        private void Fatal(string where, Exception e)
        {
            Say($"FATAL in {where}: {e}");
            _summary["fatal"] = where + ": " + e.GetType().Name + ": " + e.Message;
            WriteSummary();
            if (_cfg.quit) StartCoroutine(Quit());
        }

        // ───────────── main ─────────────

        private IEnumerator Main()
        {
            _phase = "menu";
            yield return Until(() => GameManager.instance.gameMode == GameMode.MainMenu && !GameManager.instance.isGameLoading, 900f, "main menu");
            Mark("mainMenuSec");
            yield return Frames(10);
            _world = World.DefaultGameObjectInjectionWorld;
            _tool = _world.GetExistingSystemManaged<PlacemakerToolSystem>();
            if (_tool == null) throw new Exception("PlacemakerToolSystem missing");

            _phase = "newgame";
            yield return NewFlatGame();
            if (GameManager.instance.gameMode != GameMode.Game) throw new Exception("new game did not load (mode " + GameManager.instance.gameMode + ")");

            _phase = "fixture";
            _fixture = new SuiteFixture(_world);
            _probe = new SuiteProbe(_world);
            yield return Guard(BuildFixture(), e => { Say("fixture failed: " + e); _summary["fixtureError"] = e.Message; });
            WriteSummary();

            if (!_cfg.fixtureOnly)
            {
                var set = LoadSet();
                var scenarios = Expand(set);
                if (_cfg.maxScenarios > 0 && scenarios.Count > _cfg.maxScenarios) scenarios = scenarios.Take(_cfg.maxScenarios).ToList();
                _total = scenarios.Count;
                _summary["scenarioCount"] = scenarios.Count;
                Say($"{scenarios.Count} scenarios: {string.Join(", ", scenarios.Select(s => s.Id))}");

                if (set.switching != null && !_cfg.noSwitching)
                {
                    _phase = "switching";
                    var block = _fixture.Take(set.switching.site);
                    yield return Guard(Switching(set.switching, block), e => { Say("switching failed: " + e); _summary["switchingError"] = e.Message; });
                    Mark("switchingDoneSec");
                    WriteSummary();
                }

                _phase = "scenarios";
                for (int i = 0; i < scenarios.Count; i++)
                {
                    _index = i + 1;
                    var s = scenarios[i];
                    s.Block = _fixture.Take(s.Site);
                    var st = new ScenarioState { S = s };
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
                    WriteScenario(st, i);
                    WriteSummary();
                }
                _scenario = "";
                Mark("scenariosDoneSec");
                yield return Guard(CloseTool(), e => Say("close tool: " + e.Message));

                if (set.grow && _states.Any(x => x.Built))
                {
                    _phase = "grow";
                    yield return Guard(Grow(_cfg.growSeconds, "grow"), e => Say("grow failed: " + e));
                    Mark("growDoneSec");
                }

                _phase = "post";
                for (int i = 0; i < _states.Count; i++)
                {
                    var st = _states[i];
                    _index = i + 1;
                    _scenario = st.S.Id;
                    yield return Guard(PostGrow(st), e => { Say($"post {st.S.Id}: {e}"); st.Result["postError"] = e.Message; });
                    Verdicts(st);
                    WriteScenario(st, i);
                }
                _scenario = "";
                Mark("postDoneSec");
            }
            _phase = "done";
            Mark("doneSec");
            WriteSummary();
            if (_cfg.quit) yield return Quit();
        }

        private IEnumerator Quit()
        {
            _phase = "quit";
            Mark("quitRequestedSec");
            WriteSummary();
            Beat();
            yield return Frames(2);
            GameManager.QuitGame();
        }

        // ───────────── new game on the flat default world ─────────────

        private IEnumerator NewFlatGame()
        {
            var city = _world.GetOrCreateSystemManaged<CityConfigurationSystem>();
            city.overrideLoadedOptions = true;
            city.overrideCityName = "Placemaker Suite";
            city.overrideThemeName = null;
            city.overrideLeftHandTraffic = false;
            city.overrideNaturalDisasters = false;
            city.overrideUnlockAll = true;
            city.overrideUnlimitedMoney = true;
            city.overrideUnlockMapTiles = true;
            try { SharedSettingsTweaks(); } catch (Exception e) { Say("settings tweaks: " + e.Message); }
            float t0 = Time.realtimeSinceStartup;
            Say("loading a new game on the default (flat) world, no map");
            var task = GameManager.instance.Load(GameMode.Game, Purpose.NewGame, (IAssetData)null);
            yield return Until(() => task.IsCompleted, 600f, "new game load task");
            bool ok = task.IsCompleted && !task.IsFaulted && task.Result;
            yield return Until(() => GameManager.instance.gameMode == GameMode.Game && !GameManager.instance.isGameLoading, 300f, "game mode");
            _timeline["newGameLoadSec"] = Math.Round(Time.realtimeSinceStartup - t0, 1);
            Mark("worldReadySec");
            Say($"new game load: ok={ok} mode={GameManager.instance.gameMode}");
            yield return Frames(30);
            var sim = _world.GetOrCreateSystemManaged<SimulationSystem>();
            sim.selectedSpeed = 0f;
        }

        private static void SharedSettingsTweaks()
        {
            var s = global::Game.Settings.SharedSettings.instance;
            if (s == null) return;
            s.gameplay.showTutorials = false;
            s.gameplay.pausedAfterLoading = true;
        }

        // ───────────── fixture ─────────────

        private SuiteFixtureToolSystem FixtureTool => _world.GetExistingSystemManaged<SuiteFixtureToolSystem>();

        /// <summary>Activates the fixture tool, runs <paramref name="emit"/> inside its update, applies, gives the slot back.</summary>
        private IEnumerator ApplyWithFixtureTool(Func<SuiteFixtureToolSystem, int> emit, string what)
        {
            var ft = FixtureTool;
            var ts = ft.Tools;
            ts.activeTool = ft;
            yield return Until(() => ts.activeTool == ft && ft.Updates > 0, 10f, "fixture tool active");
            yield return Frames(2);
            ft.Requested = ApplyMode.Clear;
            yield return Frames(3);
            int emitted = 0;
            ft.Enqueue(() => emitted = emit(ft));
            yield return Frames(6);
            ft.Requested = ApplyMode.Apply;
            yield return Frames(6);
            int destroyed = ft.DestroyDefinitions();
            yield return Frames(2);
            ts.activeTool = ft.DefaultTool;
            yield return Frames(30);
            Say($"{what}: {emitted} definitions applied ({destroyed} cleaned up)");
        }

        private IEnumerator BuildFixture()
        {
            float t0 = Time.realtimeSinceStartup;
            _fixture.IndexPrefabs();
            var fx = new JObject();
            _summary["fixture"] = fx;

            // terrain
            string err = _fixture.ApplyHeightmap();
            yield return Frames(40);
            float worst = _fixture.CheckTerrain(out var detail);
            Say($"terrain check: worst error {worst:F2} m: {detail}");
            if (worst > 1f) { yield return Frames(60); worst = _fixture.CheckTerrain(out detail); Say($"terrain re-check: worst {worst:F2} m: {detail}"); }
            fx["terrainWorstErrorM"] = Math.Round(worst, 2);
            fx["terrainProbes"] = detail;
            Mark("heightmapDoneSec");

            // roads
            yield return ApplyWithFixtureTool(t => _fixture.EmitRoads(t), "roads");
            yield return Frames(30);
            int edges = _fixture.CountEdges(out int temps);
            fx["roadEdges"] = edges;
            fx["roadTemps"] = temps;
            Say($"roads: {edges} edges ({temps} still temporary)");
            Mark("roadsDoneSec");

            // zoning (zone blocks exist only once the roads did)
            yield return Frames(30);
            yield return ApplyWithFixtureTool(t => _fixture.EmitZoning(t), "zoning");
            yield return Frames(40);
            fx["zonedCells"] = _fixture.CountZonedCells();
            Say($"zoned cells: {fx["zonedCells"]}");

            // plopped buildings
            yield return ApplyWithFixtureTool(t => _fixture.EmitBuildings(t), "plops");
            yield return Frames(20);
            fx["buildingsAfterPlop"] = _fixture.CountAllBuildings();
            Mark("plopsDoneSec");

            // overview shot of the fixture
            var mid = new float2((_fixture.Layout.LineX(0) + _fixture.Layout.LineX(_fixture.Layout.NX)) * 0.5f, (_fixture.Layout.LineZ(0) + _fixture.Layout.LineZ(_fixture.Layout.NZ)) * 0.5f);
            SuiteCapture.Aim(new float3(mid.x, SuiteLayout.BaseHeight, mid.y), 0f, 70f, 1500f);
            yield return Frames(40);
            yield return Seconds(1f);
            var e1 = SuiteCapture.RenderPng(Path.Combine(_shots, "fixture-overview.png"), _cfg.shotWidth, _cfg.shotHeight);
            fx["overviewShot"] = e1 ?? "shots/fixture-overview.png";
            Say("camera: " + SuiteCapture.CameraState());

            // pre-grow the zoned row so the city street side has buildings before the scenarios run
            if (_cfg.pregrowSeconds > 0 && (int)fx["zonedCells"] > 0)
            {
                yield return Grow(_cfg.pregrowSeconds, "pregrow");
                fx["buildingsAfterPregrow"] = _fixture.CountAllBuildings();
            }
            fx["fixtureSec"] = Math.Round(Time.realtimeSinceStartup - t0, 1);
            Mark("fixtureDoneSec");
            fx["log"] = new JArray(_fixture.Log);
        }

        /// <summary>Fast spawn at the configured speed until the building count settles (or the time is up).</summary>
        private IEnumerator Grow(float seconds, string what)
        {
            var sim = _world.GetOrCreateSystemManaged<SimulationSystem>();
            var spawn = _world.GetOrCreateSystemManaged<ZoneSpawnSystem>();
            int start = _fixture.CountAllBuildings();
            spawn.debugFastSpawn = true;
            sim.selectedSpeed = _cfg.speed;
            float t0 = Time.realtimeSinceStartup, lastChange = t0;
            int last = start;
            uint f0 = sim.frameIndex;
            while (Time.realtimeSinceStartup - t0 < seconds)
            {
                yield return Seconds(2.5f);
                int n = _fixture.CountAllBuildings();
                if (n != last) { last = n; lastChange = Time.realtimeSinceStartup; }
                if (n > start && Time.realtimeSinceStartup - lastChange > 20f && Time.realtimeSinceStartup - t0 > 30f) break;
            }
            sim.selectedSpeed = 0f;
            spawn.debugFastSpawn = false;
            yield return Frames(10);
            var g = new JObject
            {
                ["seconds"] = Math.Round(Time.realtimeSinceStartup - t0, 1), ["buildingsBefore"] = start, ["buildingsAfter"] = _fixture.CountAllBuildings(),
                ["simFrames"] = sim.frameIndex - f0, ["speed"] = _cfg.speed,
            };
            _summary[what] = g;
            Say($"{what}: {g.ToString(Formatting.None)}");
        }

        // ───────────── scenario sets ─────────────

        private ScenarioSet LoadSet()
        {
            if (string.IsNullOrEmpty(_cfg.scenarioFile) || !File.Exists(_cfg.scenarioFile)) throw new Exception("scenario file missing: " + _cfg.scenarioFile);
            return JsonConvert.DeserializeObject<ScenarioSet>(File.ReadAllText(_cfg.scenarioFile));
        }

        private List<Scenario> Expand(ScenarioSet set)
        {
            var r = new List<Scenario>();
            var catalog = new JArray();
            foreach (var g in GeneratorRegistry.All)
            {
                if (GeneratorRegistry.IsHidden(g)) continue;
                var grp = GeneratorGroups.Of(g);
                if (string.IsNullOrEmpty(grp)) continue;
                catalog.Add(new JObject { ["layer"] = g.Id, ["group"] = grp, ["presets"] = new JArray(PresetIds(g)) });
            }
            _summary["catalog"] = catalog;
            foreach (var spec in set.scenarios)
            {
                var items = new List<(string Layer, string Preset)>();
                switch ((spec.expand ?? "").ToLowerInvariant())
                {
                    case "presets":
                        foreach (var g in Generators(spec))
                            foreach (var p in PresetIds(g))
                                if (spec.skip == null || !spec.skip.Contains(p)) items.Add((g.Id, p));
                        break;
                    case "types":
                        foreach (var g in Generators(spec)) items.Add((g.Id, spec.preset ?? ""));
                        break;
                    default:
                        items.Add((spec.layer, spec.preset ?? ""));
                        break;
                }
                if (spec.limit > 0 && items.Count > spec.limit) items = items.Take(spec.limit).ToList();
                foreach (var (layer, preset) in items)
                {
                    if (string.IsNullOrEmpty(layer)) continue;
                    string id = spec.id != null && items.Count == 1 ? spec.id : layer + "-" + (string.IsNullOrEmpty(preset) ? "default" : preset);
                    if (spec.draw != null && spec.id == null) id += "-small";
                    while (r.Any(x => x.Id == id)) id += "+";
                    r.Add(new Scenario { Id = id, Layer = layer, Preset = preset, Site = spec.site ?? "any", Draw = spec.draw, Values = spec.values ?? new List<ValueOverride>(), Seed = spec.seed, Note = spec.note ?? "" });
                }
            }
            return r;
        }

        private static IEnumerable<IAreaGenerator> Generators(ScenarioSpec spec)
        {
            foreach (var g in GeneratorRegistry.All)
            {
                if (GeneratorRegistry.IsHidden(g)) continue;
                var grp = GeneratorGroups.Of(g);
                if (!string.IsNullOrEmpty(spec.layer)) { if (g.Id == spec.layer) yield return g; continue; }
                if (!string.IsNullOrEmpty(spec.group)) { if (grp == spec.group) yield return g; continue; }
                if (!string.IsNullOrEmpty(grp)) yield return g;   // "types": every type offered in the Make row
            }
        }

        private static List<string> PresetIds(IAreaGenerator g)
        {
            var r = new List<string>();
            try { if (g.Presets != null) foreach (var p in g.Presets) if (p != null && !string.IsNullOrEmpty(p.Id) && !r.Contains(p.Id)) r.Add(p.Id); }
            catch (Exception) { }
            return r;
        }

        // ───────────── the tool, as the UI drives it ─────────────

        private IEnumerator OpenTool()
        {
            if (!_tool.IsActive) _tool.Toggle();
            yield return Until(() => _tool.IsActive, 10f, "tool open");
            yield return Frames(3);
        }

        private IEnumerator CloseTool()
        {
            if (_tool.IsActive) _tool.Close();
            yield return Until(() => !_tool.IsActive, 5f, "tool closed");
            if (_tool.IsActive) _tool.Close();
            yield return Frames(5);
        }

        /// <summary>The type's design starts fresh (as a first pick does): if the root already is this type, pick another type first.</summary>
        private void FreshType(string layer)
        {
            var root = _tool.CurrentRecipe?.Root?.Layer;
            if (root == null || root != LayerAliasesCanonical(layer)) return;
            foreach (var e in _tool.Recipes.Entries)
                if (!e.Custom && e.Recipe?.Root != null && e.Recipe.Root.Layer != root) { _tool.SelectRecipe(e.Recipe.Id); return; }
        }

        private static string LayerAliasesCanonical(string layer)
        {
            try { return Placemaker.Core.Layers.Common.LayerAliases.Canonical(layer); } catch (Exception) { return layer; }
        }

        private float3[] DrawRing(SuiteBlock b, float w, float h)
        {
            var c = b.Center;
            return new[]
            {
                new float3(c.x - w / 2, 0, c.y - h / 2), new float3(c.x + w / 2, 0, c.y - h / 2),
                new float3(c.x + w / 2, 0, c.y + h / 2), new float3(c.x - w / 2, 0, c.y + h / 2),
            };
        }

        private float3 FocusOf(SuiteBlock b) => new float3(b.Center.x, _fixture.Layout.Height(b.Center.x, b.Center.y), b.Center.y);

        // ───────────── one scenario ─────────────

        private IEnumerator RunScenario(ScenarioState st)
        {
            var s = st.S;
            var res = st.Result;
            var errMark = ErrorMark();
            res["id"] = s.Id;
            res["layer"] = s.Layer;
            res["preset"] = s.Preset;
            res["note"] = s.Note;
            res["git"] = _cfg.git;
            res["assembly"] = AssemblyVersion();
            if (s.Block == null) { res["status"] = "no-block"; yield break; }
            res["block"] = new JObject { ["id"] = s.Block.Id, ["kind"] = s.Block.Kind.ToString(), ["west"] = s.Block.West, ["east"] = s.Block.East, ["south"] = s.Block.South, ["north"] = s.Block.North, ["center"] = new JArray(s.Block.Center.x, s.Block.Center.y) };
            st.Focus = FocusOf(s.Block);
            st.Radius = 85f;
            var timings = new JObject();
            res["timings"] = timings;
            Say($"scenario {s.Id} on {s.Block.Id} ({s.Block.Kind})");

            SuiteCapture.Aim(st.Focus, 25f, 55f, 300f);
            yield return OpenTool();
            PlacemakerToolSystem.SuiteResetSeed(s.Seed);

            // 1. outline
            OpenWindow();
            float t0 = Time.realtimeSinceStartup;
            string problem;
            bool selected = s.Draw != null && s.Draw.Length >= 2
                ? _tool.SuiteDrawOutline(DrawRing(s.Block, s.Draw[0], s.Draw[1]), out problem)
                : _tool.SuiteSelectBlockAt(s.Block.Center, out problem);
            res["select"] = s.Draw != null ? $"draw {s.Draw[0]}x{s.Draw[1]}" : "block";
            if (!selected) { res["status"] = "no-outline"; res["problem"] = problem; CloseWindow(); yield break; }
            yield return Frames(2);
            st.Ring = _tool.Shape?.Ring2D();
            if (st.Ring == null || st.Ring.Length < 3) { res["status"] = "no-outline"; res["problem"] = "empty shape"; CloseWindow(); yield break; }
            res["areaM2"] = Math.Round(math.abs(Placemaker.Core.Geometry.Poly.SignedArea(st.Ring)));
            st.Before = _probe.Snapshot(st.Ring);
            res["before"] = new JObject { ["buildings"] = st.Before.Buildings.Count, ["areas"] = st.Before.Areas.Count, ["objects"] = st.Before.Objects.Count, ["edges"] = st.Before.Edges.Count };

            // 2. type / preset / values — the UI triggers
            FreshType(s.Layer);
            _tool.SelectPreset(s.Layer, s.Preset ?? "");
            foreach (var v in s.Values) _tool.SetValue(v.path ?? "0", v.id, v.json);
            yield return Frames(2);
            res["recipeRoot"] = _tool.CurrentRecipe?.Root?.Layer ?? "";
            if (res["recipeRoot"].ToString() != LayerAliasesCanonical(s.Layer)) { res["status"] = "type-not-selected"; res["flash"] = _tool.FlashText?.Key ?? ""; CloseWindow(); yield break; }

            // 3. plan
            yield return Until(() => !_tool.Planning && _tool.Plan != null && !_tool.PlanStale, 90f, "plan");
            timings["planWaitMs"] = Math.Round((Time.realtimeSinceStartup - t0) * 1000f);
            timings["planMs"] = Math.Round(_tool.PlanMilliseconds);
            res["design"] = CloseWindow();
            if (_timedOut || _tool.Plan == null) { res["status"] = "no-plan"; res["unready"] = _tool.SuiteUnreadyText(); yield break; }
            st.Plan = _tool.Plan;
            res["planIssues"] = new JArray(st.Plan.Issues.Concat(st.Plan.Layers.SelectMany(l => l.Issues)).Take(20).Select(i => $"{i.Severity} {i.Key}"));
            res["planCost"] = Math.Round(_tool.PlanCost);

            // 4. review: the ghost
            OpenWindow();
            float t1 = Time.realtimeSinceStartup;
            var primary = _tool.SuitePrimaryInfo();
            if (primary.Kind == "toReview" && primary.Enabled) _tool.Primary();
            else if (primary.Kind != "build") { res["status"] = "blocked"; res["blockedAt"] = "design"; res["unready"] = _tool.SuiteUnreadyText(); res["review"] = CloseWindow(); yield return Shot(st, "preview", true); yield break; }
            yield return Until(() => _tool.Stage == ToolStage.Review, 10f, "review stage");
            yield return Until(() => _tool.PreviewModeEffective == PreviewMode.Ghost && !_tool.GhostPending, 45f, "ghost");
            res["ghostShown"] = !_timedOut;
            res["ghostDropped"] = _tool.GhostDropped;
            yield return Frames(15);
            timings["ghostMs"] = Math.Round((Time.realtimeSinceStartup - t1) * 1000f);
            yield return Shot(st, "preview", true);
            res["review"] = CloseWindow();

            // 5. build
            primary = _tool.SuitePrimaryInfo();
            if (!primary.Enabled && primary.Reason == "Placemaker.Reason.AcknowledgePermanent")
            {
                _tool.AcknowledgePermanent(true);
                yield return Frames(2);
                primary = _tool.SuitePrimaryInfo();
                res["acknowledgedPermanent"] = true;
            }
            if (primary.Kind != "build" || !primary.Enabled) { res["status"] = "blocked"; res["blockedAt"] = "review"; res["unready"] = _tool.SuiteUnreadyText(); yield break; }
            int doneBefore = _tool.SuiteBuildsDone;
            OpenWindow();
            float t2 = Time.realtimeSinceStartup;
            _tool.Primary();
            yield return Until(() => _tool.Building, 5f, "build start");
            yield return Until(() => !_tool.Building, 240f, "build end");
            timings["buildMs"] = Math.Round((Time.realtimeSinceStartup - t2) * 1000f);
            res["build"] = CloseWindow();
            if (_tool.SuiteBuildsDone == doneBefore)
            {
                var f = _tool.BuildFailure;
                res["status"] = "build-failed";
                res["failure"] = f == null ? (_timedOut ? "timeout" : "unknown") : f.Key + (f.Args != null && f.Args.Length > 0 ? " (" + string.Join(", ", f.Args) + ")" : "");
                yield return Frames(5);
            }
            else
            {
                st.Built = true;
                st.Group = _tool.SuiteLastBuiltGroup;
                res["status"] = "built";
                res["notes"] = new JArray(_tool.BuildNotes.Select(n => n.Key + (n.Args != null && n.Args.Length > 0 ? " (" + string.Join(", ", n.Args) + ")" : "")));
            }
            yield return Frames(30);
            var ground = _probe.GroundNow(st.Before);
            res["terrain"] = _probe.Pits(st.Before, ground, st.Plan);
            res["census"] = _probe.Census(st.Before, st.Group, st.Plan, s.Layer, _tool.BuildAudit);
            res["errors"] = ErrorsSince(errMark);
        }

        private IEnumerator Shot(ScenarioState st, string kind, bool grab)
        {
            string file = Safe(st.S.Id) + "." + kind + ".png";
            string path = Path.Combine(_shots, file);
            float yaw = 25f, pitch = 55f, dist = st.Radius * 3.4f;
            if (kind == "top") { yaw = 0f; pitch = 89.5f; dist = st.Radius * 3.6f; }
            SuiteCapture.Aim(st.Focus, yaw, pitch, dist);
            yield return Frames(20);
            yield return Seconds(0.4f);
            string err;
            if (grab)
            {
                SuiteCapture.UiOff();
                yield return Frames(3);
                yield return new WaitForEndOfFrame();
                err = SuiteCapture.GrabScreen(path, _cfg.shotWidth);
                SuiteCapture.UiOn();
            }
            else err = SuiteCapture.RenderPng(path, _cfg.shotWidth, _cfg.shotHeight);
            var shots = st.Result["shots"] as JObject ?? new JObject();
            shots[kind] = err == null ? "shots/" + file : "ERROR " + err;
            st.Result["shots"] = shots;
            if (err != null) Say($"shot {kind} failed: {err}");
        }

        private static string Safe(string id)
        {
            foreach (var c in Path.GetInvalidFileNameChars()) id = id.Replace(c, '_');
            return id.Replace('+', 'x');
        }

        // ───────────── after the shared grow phase ─────────────

        private IEnumerator PostGrow(ScenarioState st)
        {
            if (st.Before != null && st.Ring != null)
            {
                var members = _probe.MemberSet(st.Group);
                st.Result["growables"] = _probe.Growables(st.Before, st.Group, members);
                int lost = 0;
                foreach (var b in st.Before.Buildings) if (!_world.EntityManager.Exists(b) || _world.EntityManager.HasComponent<global::Game.Common.Deleted>(b)) lost++;
                st.Result["existingLostAfterGrow"] = lost;
            }
            if (st.S.Block != null)
            {
                yield return Shot(st, "oblique", false);
                yield return Shot(st, "top", false);
            }
        }

        private void Verdicts(ScenarioState st)
        {
            var r = st.Result;
            var v = new JArray();
            void Check(string id, bool pass, string detail) => v.Add(new JObject { ["id"] = id, ["pass"] = pass, ["detail"] = detail });
            string status = (string)r["status"] ?? "";
            Check("built", status == "built", status + (r["failure"] != null ? ": " + r["failure"] : "") + (r["unready"] != null ? ": " + r["unready"] : ""));
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
                    Check("stallsOnLanes", (int)parking["stallsFarFromLane"] == 0, $"{parking["stallsFarFromLane"]} stalls > 9 m from any drivable lane");
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
            foreach (var w in new[] { "design", "review", "build" }) if (r[w] is JObject o && o["maxMs"] != null) worstFrame = math.max(worstFrame, (float)o["maxMs"]);
            Check("noHang", worstFrame < 1000f, $"worst frame {worstFrame:F0} ms (design/review/build)");
            r["verdicts"] = v;
            r["failed"] = new JArray(v.Where(x => !(bool)x["pass"]).Select(x => (string)x["id"]));
        }

        // ───────────── switching ─────────────

        private IEnumerator Switching(SwitchingSpec spec, SuiteBlock block)
        {
            var res = new JObject();
            _summary["switching"] = res;
            if (block == null) { res["status"] = "no-block"; yield break; }
            var errMark = ErrorMark();
            _scenario = "switching";
            res["block"] = block.Id;
            var steps = new List<(string Layer, string Preset)>();
            foreach (var sp in spec.steps)
            {
                if ((sp.expand ?? "") == "presets")
                {
                    foreach (var g in Generators(sp))
                    {
                        var ids = PresetIds(g);
                        if (sp.limit > 0) ids = ids.Take(sp.limit).ToList();
                        foreach (var p in ids) steps.Add((g.Id, p));
                    }
                }
                else if ((sp.expand ?? "") == "types") foreach (var g in Generators(sp)) steps.Add((g.Id, ""));
                else steps.Add((sp.layer, sp.preset ?? ""));
            }
            res["steps"] = steps.Count;
            var focus = FocusOf(block);
            SuiteCapture.Aim(focus, 25f, 55f, 300f);
            yield return OpenTool();
            PlacemakerToolSystem.SuiteResetSeed(7);
            if (!_tool.SuiteSelectBlockAt(block.Center, out var problem)) { res["status"] = "no-outline"; res["problem"] = problem; yield break; }
            yield return Frames(3);

            // patient: one click, wait for the plan, then the settle time (catalog rescans land up to 120 frames later)
            var patient = new JArray();
            float worst = 0f;
            foreach (var (layer, preset) in steps)
            {
                OpenWindow();
                float t0 = Time.realtimeSinceStartup;
                _tool.SelectPreset(layer, preset);
                yield return Frames(1);
                yield return Until(() => !_tool.Planning, 30f, "switch plan");
                float planned = Time.realtimeSinceStartup - t0;
                yield return Seconds(spec.settleSeconds);
                var w = CloseWindow();
                worst = math.max(worst, (float)w["maxMs"]);
                patient.Add(new JObject { ["layer"] = layer, ["preset"] = preset, ["planWaitMs"] = Math.Round(planned * 1000f), ["maxMs"] = w["maxMs"], ["p95Ms"] = w["p95Ms"], ["over250"] = w["over250"] });
            }
            res["patient"] = patient;
            res["patientWorstMs"] = Math.Round(worst, 1);

            // rapid: in Review (ghost on), a click every rapidMs like a player flicking through the cards
            if (spec.rapid)
            {
                var p = _tool.SuitePrimaryInfo();
                if (p.Kind == "toReview" && p.Enabled) _tool.Primary();
                yield return Until(() => !_tool.Planning, 30f, "plan before rapid");
                OpenWindow();
                float t0 = Time.realtimeSinceStartup;
                foreach (var (layer, preset) in steps)
                {
                    _tool.SelectPreset(layer, preset);
                    yield return Seconds(spec.rapidMs / 1000f);
                }
                yield return Until(() => !_tool.Planning && !_tool.GhostPending, 60f, "rapid settle");
                yield return Seconds(3f);
                var w = CloseWindow();
                w["seconds"] = Math.Round(Time.realtimeSinceStartup - t0, 1);
                res["rapid"] = w;
                worst = math.max(worst, (float)w["maxMs"]);
            }
            res["worstFrameMs"] = Math.Round(worst, 1);
            res["errors"] = ErrorsSince(errMark);
            res["status"] = worst < 1000f ? "ok" : "hang";
            _tool.ClearArea();
            yield return Frames(5);
            yield return CloseTool();
            _scenario = "";
            Say($"switching: worst frame {worst:F0} ms over {steps.Count} switches");
        }

        // ───────────── output ─────────────

        private void WriteScenario(ScenarioState st, int i)
        {
            try { File.WriteAllText(Path.Combine(_results, $"{i + 1:D2}-{Safe(st.S.Id)}.json"), st.Result.ToString(Formatting.Indented)); }
            catch (Exception e) { Say("write scenario failed: " + e.Message); }
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
                _summary["timeline"] = _timeline;
                lock (_errLock)
                {
                    _summary["errorsTotal"] = _errors;
                    _summary["exceptionsTotal"] = _exceptions;
                    _summary["errorLinesFirst"] = new JArray(_errorLines.Take(60));
                }
                var rows = new JArray();
                foreach (var st in _states)
                {
                    var r = st.Result;
                    rows.Add(new JObject
                    {
                        ["id"] = st.S.Id, ["block"] = st.S.Block?.Id, ["kind"] = st.S.Block?.Kind.ToString(), ["status"] = r["status"],
                        ["failed"] = r["failed"], ["sec"] = r["scenarioSec"], ["failure"] = r["failure"], ["unready"] = r["unready"],
                        ["leftOut"] = r["census"]?["audit"]?["leftOut"], ["kept"] = r["census"]?["audit"]?["kept"],
                        ["shots"] = r["shots"],
                    });
                }
                _summary["scenarios"] = rows;
                File.WriteAllText(Path.Combine(_out, "summary.json"), _summary.ToString(Formatting.Indented));
                File.WriteAllText(Path.Combine(_out, "summary.md"), SummaryMarkdown());
            }
            catch (Exception e) { Say("write summary failed: " + e.Message); }
        }

        private string SummaryMarkdown()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"# Suite run {_cfg.runId} ({_cfg.set})");
            sb.AppendLine();
            sb.AppendLine($"Build: git `{_cfg.git}`, assembly `{AssemblyVersion()}`. Phase: {_phase}.");
            sb.AppendLine($"Timeline (s since process start): {_timeline.ToString(Formatting.None)}");
            if (_summary["fatal"] != null) sb.AppendLine($"\n**FATAL:** {_summary["fatal"]}");
            if (_summary["switching"] is JObject sw)
                sb.AppendLine($"\nSwitching: status {sw["status"]}, worst frame {sw["worstFrameMs"]} ms, patient worst {sw["patientWorstMs"]} ms, rapid {sw["rapid"]?.ToString(Formatting.None)}");
            sb.AppendLine();
            sb.AppendLine("| # | scenario | block | status | failed checks | shots |");
            sb.AppendLine("|---|---|---|---|---|---|");
            for (int i = 0; i < _states.Count; i++)
            {
                var st = _states[i];
                var r = st.Result;
                string failed = r["failed"] is JArray fa && fa.Count > 0 ? string.Join(", ", fa.Select(x => (string)x)) : "";
                string shots = r["shots"] is JObject so ? string.Join(" ", so.Properties().Select(p => (string)p.Value)) : "";
                sb.AppendLine($"| {i + 1} | {st.S.Id} | {st.S.Block?.Id} {st.S.Block?.Kind} | {r["status"]} | {failed} | {shots} |");
            }
            sb.AppendLine();
            sb.AppendLine("## Failures in detail");
            foreach (var st in _states)
            {
                if (!(st.Result["verdicts"] is JArray v)) continue;
                var bad = v.Where(x => !(bool)x["pass"]).ToList();
                if (bad.Count == 0) continue;
                sb.AppendLine($"- **{st.S.Id}** ({st.S.Block?.Id}): " + string.Join("; ", bad.Select(x => $"{x["id"]}: {x["detail"]}")));
            }
            return sb.ToString();
        }
    }
}
