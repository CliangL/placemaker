// In-game test suite: run configuration (written by tools/suite/Run-Suite.ps1), scenario sets (suite/scenarios/*.json)
// and the scenario list the runner expands from them. Results are written as JObjects (SuiteRunner / SuiteProbe).

using System;
using System.Collections.Generic;

namespace Placemaker.Game.Suite
{
    public sealed class SuiteRunConfig
    {
        public string runId = "";
        public string set = "smoke";
        /// <summary>Absolute folder the runner writes heartbeat, results and PNGs into.</summary>
        public string outDir = "";
        /// <summary>Absolute path of the scenario set JSON.</summary>
        public string scenarioFile = "";
        public string git = "";
        public string expiresUtc = "";
        /// <summary>Simulation speed while growing (4 reaches the 8 steps/frame cap; 8 is no faster).</summary>
        public float speed = 4f;
        public float pregrowSeconds = 75f;
        public float growSeconds = 100f;
        /// <summary>Only the first N scenarios (0 = all).</summary>
        public int maxScenarios;
        /// <summary>Skip the switching scenario.</summary>
        public bool noSwitching;
        /// <summary>Stop after the fixture (bootstrap check).</summary>
        public bool fixtureOnly;
        public bool quit = true;
        /// <summary>Keep the map's terrain as it is (no heightmap replacement: no flat, slope or rough blocks).</summary>
        public bool noTerrain;
        /// <summary>"map:&lt;display name&gt;": the vanilla map the new game starts on (the fixture area is reshaped in place).</summary>
        public string world = "map:Plains";
        /// <summary>Road fixture: "single" (one apply; ends merge on exact positions), "batches" (vertical lines, then cross streets onto their nodes), "vertical", "none".</summary>
        public string roads = "single";
        /// <summary>Seconds to idle after the roads (diagnostics).</summary>
        public float idleAfterRoads;
        /// <summary>Log every frame while the roads apply (diagnostics).</summary>
        public bool trace;
        public int shotWidth = 1280, shotHeight = 720;
    }

    public sealed class ValueOverride
    {
        public string path = "0";
        public string id = "";
        public string json = "";
    }

    public sealed class ScenarioSpec
    {
        public string id;
        /// <summary>Type (generator) id; with expand = "presets" every built-in preset of it.</summary>
        public string layer;
        /// <summary>Preset id; "" or null = the type's defaults.</summary>
        public string preset;
        /// <summary>"presets" (all presets of layer or group), "types" (every visible type, default preset).</summary>
        public string expand;
        /// <summary>Make-row group for expand ("commercial", "parks"...).</summary>
        public string group;
        /// <summary>Preferred block kind: flat, zoned, slope, rough, survive, any.</summary>
        public string site = "any";
        /// <summary>Draw a rectangle of this size (m) at the block centre instead of picking the whole block.</summary>
        public float[] draw;
        public List<ValueOverride> values;
        public uint seed = 1;
        public string note;
        /// <summary>Skip these preset ids when expanding.</summary>
        public List<string> skip;
        /// <summary>Limit an expansion to the first N items (0 = all).</summary>
        public int limit;
    }

    public sealed class SwitchingSpec
    {
        public string site = "flat";
        /// <summary>Patient cycle: these steps, each followed by the plan and a settle wait.</summary>
        public List<ScenarioSpec> steps = new List<ScenarioSpec>();
        public float settleSeconds = 2.5f;
        /// <summary>Rapid cycle (clicks every rapidMs) in Review, over the same steps.</summary>
        public int rapidMs = 300;
        public bool rapid = true;
    }

    public sealed class ScenarioSet
    {
        public string set = "";
        public string description = "";
        public List<ScenarioSpec> scenarios = new List<ScenarioSpec>();
        public SwitchingSpec switching;
        public bool grow = true;
    }

    /// <summary>One concrete scenario after expansion.</summary>
    public sealed class Scenario
    {
        public string Id = "";
        public string Layer = "";
        public string Preset = "";
        public string Site = "any";
        public float[] Draw;
        public List<ValueOverride> Values = new List<ValueOverride>();
        public uint Seed = 1;
        public string Note = "";
        public Placemaker.Core.Suite.SuiteBlock Block;
    }
}
