# Teardown 10: a stripped-down in-game test suite the lead can run unattended

> The user (12:14, verbatim in `00-user-feedback-1215.md`): "boot up a super stripped-down version of the game and make a test
> suite in the game ... cut down on the game's textures and load time and just load up images of what it looks like. That
> would be ultimate."

This report covers how to boot CS2 1.6.2f1 with as little as possible loaded, how the mod runs scenarios through the same
code path as the UI and records JSON and PNG results, how to make the test save, how to keep the suite away from the user's
own game, and how long one loop takes. Every claim is marked **[P]** (proven: decompiled source, file, or log line quoted
here) or **[H]** (hypothesis: needs the one-time bootstrap run in section 8).

Sources: decompiled `Game.dll` in `%TEMP%\pm-decomp\game`, plus four assemblies I decompiled read-only into
`.forensics/scratch-suite/decomp/` (`Colossal.TestFramework`, `Colossal.Core.TestScenarios`, `Colossal.PSI.Steamworks`,
`ExecutableAsset` and `EnvPath`). Also the user data folder (read-only) and the 12:05–12:13 session logs in
`%USERPROFILE%\AppData\LocalLow\Colossal Order\Cities Skylines II\Logs` (that session is the master playtest; Placemaker log
copy: `.forensics/Placemaker-20261015-master.log`). Nothing was launched, deployed or modified.

---

## 0. Short answer

1. **The suite is feasible, and the game already has most of the hooks.** None of the following needs a patch:
   * `-disablePdxSdk` loads **only local mods**. Our `Mods/Placemaker` is the only local mod. The user's ~60 Paradox Mods
     subscriptions and their playset are not touched.
   * `-noSplash` skips the splash screens.
   * A `runOnce.txt` in the user data folder adds command-line toggles for **one** launch, and the game deletes the file
     itself.
   * `ZoneSpawnSystem.debugFastSpawn` makes growables spawn regardless of demand.
   * `ScreenCaptureHelper.CaptureScreenshot` renders the main camera into a RenderTexture without the UI or overlays.
   * `GameManager.QuitGame()` is the game's own clean quit.
2. **Boot time is dominated by asset caching, mod init and prefab loading, not by graphics.** The measured master session
   took **102 s** to reach the main menu: 29.7 s loading 9,904 prefabs, ~32 s in mod preload plus system creation, and 8 s
   finishing after the prefabs. A new game on a full map took **16.7 s** to load **[P]**. Without PDX mods I estimate
   **60–80 s** to the menu and **15–20 s** for a small save **[H]**. Texture settings matter for frame rate (simulation
   speed, screenshots) but hardly for boot.
3. **One run of 10 scenarios takes about 12–17 minutes** end to end. Each scenario takes 20 s (build only) to 80 s (build,
   then let shops spawn). The lead reads one `summary.json`, a diff against a baseline, and 2–3 PNGs per scenario.
4. **Three things need one supervised bootstrap run** (section 8):
   * whether `Cities2.exe` can start without the Paradox launcher (Steam's `RestartAppIfNecessary` is in the code path);
   * the real boot time with `-disablePdxSdk`;
   * whether `CaptureScreenshot` output reads back correctly to PNG.
5. **Fidelity gap, by design:** the clean profile has no asset-pack mods, so automatic asset picks (surfaces, decals) differ
   from the user's game. The "farmland" surface in shot-106 may come from a surface-pack mod. A second "playset" profile
   (all the user's mods, slower) is for release checks. Geometry, zoning, facing, reachability, pits and hangs are
   profile-independent.

---

## 1. Proven facts the design rests on

| # | Fact | Evidence |
|---|---|---|
| F1 | Command-line options the game parses: `developerMode`, `uiDeveloperMode`, `qaDeveloperMode`, `disableThumbnails`, `disablePdxSdk`, `disableModding`, `disableCodeModding`, `startGame=<guid>`, `startEditor`, `benchmark`, `noSplash`, `continuelastsave`, `logsEffectiveness=`, `captureStdout=`, `profile=` | `Game.SceneFlow/GameManager.cs:354-440` (Mono.Options) |
| F2 | `runOnce.txt` in the user data folder is read, **deleted**, and its tokens are merged into the command line before parsing | `GameManager.cs:443-451` |
| F3 | `GameManager.cmdLine` (merged) is public, so a mod can read our own flag. Unregistered tokens do not stop the parse: today's launch carried `--pdx-launcher-session-token` and `--paradox-account-userid` (not in the OptionSet), and the configuration was still applied (`developerMode:true`) | `GameManager.cs:246, 449-451`; `SceneFlow.log:3-8, 56` |
| F4 | `-noSplash` skips the splash sequence | `Game.SceneFlow/SplashScreenSequence.cs:22` |
| F5 | `-startGame=<guid>` auto-loads a **MapMetadata or SaveGameMetadata** id. A map gives NewGame, a save gives LoadGame | `GameManager.cs:645-648, 1241-1264` |
| F6 | The guid is **not** the `.cok.cid` package id: `Archipelago.cok.cid` = `be2eee70…`, while the log shows `MapMetadata:bf8036b2…` for the same map | `Content/Game/Maps/Archipelago.cok.cid`; `Logs/SceneFlow.log:121` |
| F7 | `-continuelastsave` only fires a UI event (`checkContinueGamePrerequisites`) and returns at once. It depends on `UserState.lastSaveGameMetadata` and the UI | `Game.UI/AppBindings.cs:215-219, 232-240` |
| F8 | Paradox Mods are registered only from the PDX SDK login callbacks (`onNoLogin`, `onLoggedIn`), and the SDK is not created under `-disablePdxSdk` | `GameManager.cs:1325-1394, 1501-1512` |
| F9 | Local mods are those in `AssetDatabase.user` (the user data folder, including `Mods/`), independent of the PDX SDK | decompiled `ExecutableAsset.isLocal => database == AssetDatabase.user` (`.forensics/scratch-suite/decomp/ExecutableAsset.cs:153`), `ModManager.cs:408-429` |
| F10 | The local `Mods` folder holds only `Placemaker/` | directory listing |
| F11 | The user data path is `Application.persistentDataPath` and cannot be redirected by an argument | decompiled `EnvPath` static constructor |
| F12 | `launcher-settings.json` (display mode, resolution, vsync) is applied **after** `Settings.coc` at boot, and both are rewritten on quit | `Game.Settings/SharedSettings.cs:56-64`; `Game.PSI.PdxSdk/LauncherSettings.cs`; `GameManager.cs:795-799` |
| F13 | Clean quit is `GameManager.QuitGame()` → `Application.Quit` → `WantsToQuit` → `TerminateGame` (saves settings, disposes the world). Measured 2.2 s | `GameManager.cs:735-844`; `SceneFlow.log:127` |
| F14 | `ZoneSpawnSystem.debugFastSpawn` (public) ignores demand: `m_MinDemand = 0`, every zone type allowed | `Game.Simulation/ZoneSpawnSystem.cs:1045, 1101-1117`; the debug toggle "Superfast building spawning" is at `Game.Debug/DebugSystem.cs:1365` |
| F15 | A spawned growable records the road it faces: `Building.m_RoadEdge` | `Game.Buildings/Building.cs` |
| F16 | `ScreenCaptureHelper.CaptureScreenshot(Camera, RenderTexture, settings)` renders the camera 8× into an RT with UI, overlay and outlines hidden. `CreateRenderTarget(name, w, h)` makes the RT | `Game.UI/ScreenCaptureHelper.cs:84-148` |
| F17 | Photo mode saves PNGs as follows: hide the UI view, `WaitForEndOfFrame`, then `PlatformManager.TakeScreenshotAsTexture()` and `ImageAsset.Save(...PNG...)` | `Game.UI.InGame/PhotoModeUISystem.cs:655-678` |
| F18 | The game ships a QA test framework (`Colossal.TestFramework`: `TestScenarioSystem`, `AutomationClientSystem`, `-categoryFilter=`, `-automation=`). It registers scenarios by reflection **before** mods load, so mod scenarios are never registered | `GameManager.cs:588` (Create) vs `:611, 624` (ModManager); decompiled `TestScenarioSystem.RegisterScenarios` |
| F19 | Steamworks init calls `SteamAPI.RestartAppIfNecessary(949230)` | decompiled `SteamworksPlatform.cs:511`; `Game.PSI/PlatformSupport.cs:12` |
| F20 | The Paradox launcher starts `../Cities2.exe` with `exeArgs`. Its own "Play without code mods" entry just adds `--disableCodeModding` | `Steam/.../Cities Skylines II/Launcher/launcher-settings.json` |
| F21 | Today's launch arguments were the launcher token, the user id and `-developerMode` (from Steam launch options) | `SceneFlow.log:3-8` |
| F22 | Boot timeline, master session (~60 PDX mods): process start 12:05:44 → ECS world 12:05:48 → "Preload Mods" 12:06:10 → "Creating ECS systems" 12:06:21 → VT init 12:06:42 → "Loaded 9904 prefabs in 29.7 s" 12:07:12 → "Boot completed" 12:07:20 → "MainMenu reached" 12:07:26 (**101.4 s**). New game (Archipelago) 12:08:29.6 → "Loading completed" 12:08:46.3 (**16.7 s**). Quit 2.2 s | `SceneFlow.log:3, 9-14, 61-63, 105-112, 121-127` |
| F23 | Placemaker builds take 5–7 s from "build started" to "built group" | `.forensics/Placemaker-20261015-master.log:1979-2203, 2961-3026, 3035-3073` |
| F24 | `SimulationSystem.selectedSpeed` is settable. Steps per frame are capped at 8 | `Game.Simulation/SimulationSystem.cs:72-82, 255` |
| F25 | Current user files: `Settings.coc` (3840 wide, FullscreenWindow, TAA, volumetric clouds, SSR, DoF, motion blur, shadows 2048, `autoSave:false`, master volume 0.29). `launcher-settings.json` is borderless 3840x2160. `continue_game.json` is Ithaca. `UserState.coc` has `lastSaveGameMetadata` 148d99c6…, unlockAll, unlimitedMoney | files under the user data folder (read-only) |
| F26 | The tool calls `_settings.ApplyAndSave()` on mode, brush and panel changes. **A suite run therefore writes the user's `ModsSettings/Placemaker/Placemaker.coc`** | `Game/Tool/PlacemakerToolSystem.cs:457, 497, 504` |
| F27 | Every UI action is a public method on `PlacemakerToolSystem` (`SelectRecipe`, `ApplyPreset`, `SetValue`, `SetAreaMode`, `Primary`, `RemoveGroup`, ...) reached through `PlacemakerUISystem` triggers. A closed outline goes through `CloseOutline()` → `ShapeChanged` → `RebuildShapeIfDirty` | `Game/UI/PlacemakerUISystem.cs:51-113`; `Game/Tool/PlacemakerToolSystem.Draw.cs:102-110`; `.Plan.cs:51-99` |
| F28 | A new recipe instance is seeded with `NewSeed()`, so automatic asset picks are random per run | `PlacemakerToolSystem.cs:142` |
| F29 | Every launch overwrites the user's `Player.log` (to `Player-prev.log`) and the `Logs/*.log` files | file timestamps; standard Unity behaviour |
| F30 | The installed DLL is master `650be0f` (12:01 deploy). Repo HEAD is already `d9f6df6` (s61 merged). Results must say which build ran | `git log`; `Mods/Placemaker/Placemaker.dll` mtime |

---

## 2. Minimal boot

### 2.1 Launch arguments (clean profile)

```
Cities2.exe -disablePdxSdk -noSplash -disableThumbnails -placemakerSuite=<runId>
```

* `-disablePdxSdk` (F8, F9): no Paradox login, no mod sync, no PDX asset database. Only `Mods/Placemaker` loads. This
  replaces "a dedicated playset": the user's playset is never edited. `-disableModding` must **not** be used, because it
  also disables code mods (`GameManager.cs:396-400`).
* `-noSplash` (F4). There is no intro video beyond the splash sequence. On PC `PlatformManager.requiresEngagement` keeps the
  "press any key" screen away (`GameManager.cs:849`). **[H]** that it is false on Steam PC, to check in section 8.
* `-disableThumbnails`: skips `InitializeThumbnails` work (part of the 8 s between "Loaded prefabs" and "Boot completed").
  **[H]** for the size of the saving.
* `-placemakerSuite=<runId>`: our gate token (section 4.1). The game ignores it (F3).
* Not used:
  * `-developerMode`: not needed, but harmless if the lead wants the dev panel while debugging.
  * `-startGame=<guid>`: possible (F5), but needs the SaveGameMetadata id (F6). The mod loads the save itself (4.3), so the
    guid never has to be found by hand.

**How the arguments reach the game.** Two routes, chosen in the bootstrap run:

* **Route A (direct, preferred).** `Start-Process Cities2.exe` with the arguments and the environment variables
  `SteamAppId=949230` and `SteamGameId=949230`. With Steam running and these set, `RestartAppIfNecessary` is expected to
  return false, so no relaunch through Steam and the launcher **[H, F19]**. If it returns true, the game's PSI init fails
  and Steam starts a second copy through the launcher. The bootstrap run must check for exactly that (two processes, or a
  launcher window).
* **Route B (through Steam, fallback).** Write the arguments into `runOnce.txt` (F2), then start
  `steam://rungameid/949230`. Steam starts the Paradox launcher, the launcher starts the game, and the game consumes and
  deletes `runOnce.txt`. The risk is that the launcher waits for a Play click **[H]**; the bootstrap run checks whether it
  auto-continues. `runOnce.txt` is written **only immediately before launch**, and the runner deletes it if the game has
  not consumed it within 60 s. Otherwise the user's next normal launch would pick it up.

Route A also uses `runOnce.txt` as a belt-and-braces carrier of the same flags. That is harmless because the file is
consumed by this launch.

### 2.2 Suite settings profile (swapped in, restored after)

The game rewrites `Settings.coc` and `launcher-settings.json` on quit (F12, F13), and the tool writes `Placemaker.coc`
(F26). So the runner **backs up, swaps, and restores byte-for-byte with a hash check** (section 3).

`tools/suite/profile/launcher-settings.json` (wins over Settings.coc for window and resolution, F12):

```json
{ "system": { "language": "en-US", "display_mode": "windowed", "vsync": false,
  "fullscreen_resolution": "1280x720", "windowed_resolution": "1280x720", "refreshRate": 60.0, "display_index": "0" } }
```

`tools/suite/profile/Settings.coc`: generate it from the user's file by patching keys. Never hand-write enum names the
game might not parse; the section and key names below are taken from the current file and from `Game.Settings/*.cs`.

* **Audio Settings:** `masterVolume: 0`, `menuVolume: 0`, `radioActive: false`.
* **General Settings:** `autoSave: false`.
* **Gameplay Settings** (`GameplaySettings.cs:13-29`):
  * `dayNightVisual: false` (constant daylight, so screenshots compare);
  * `snowVisual: false`;
  * `pausedAfterLoading: true` (the runner starts time itself);
  * `showTutorials: false`, `edgeScrolling: false`.
* **Interface Settings** (`InterfaceSettings.cs:153-162`): `chirperPopupsEnabled: false`, `blockingPopupsEnabled: false`,
  `showWhatsNewPanel: false`.
* **Graphics Settings:**
  * `displayMode: "Window"` and `resolution: {"width":1280,"height":720}`, `vSync: false`.
  * Quality list:
    * `CloudsQualitySettings` all false; `FogQualitySettings.enabled: false`; `VolumetricsQualitySettings.enabled: false`.
    * `SSAOQualitySettings`, `SSRQualitySettings`, `DepthOfFieldQualitySettings` and `MotionBlurQualitySettings`:
      `enabled: false`.
    * `ShadowsQualitySettings.directionalShadowResolution: 1024`. Keep shadows on; they make oblique shots readable.
    * `LevelOfDetailQualitySettings.levelOfDetail: 0.5`, `lodCrossFade: false`.
    * `DynamicResolutionScaleSettings.enabled: false`, so shots stay sharp and comparable.
    * **`TextureQualitySettings.mipbias` stays at 1.** Raising it blurs ground surfaces, and judging surfaces (the
      "farmland" complaint) is one of the things the shots are for.
  * Leave `dlssQuality` as the user has it.

### 2.3 What load time is made of, and what can be cut

From F22, the master session's 101 s were spent like this:

| Segment | Time | Can the suite cut it? |
|---|---|---|
| Process + GameManager creation | 3 s | no |
| ECS world + priority asset cache + UI init | 18 s | no (game content database) |
| "Preload Mods at boot" → "Creating ECS systems" | 11 s | mostly; it is PDX mod assets (gone with `-disablePdxSdk`) **[H]** |
| ECS systems + mod init → VT init | 21 s | partly (60 mods' `OnLoad`, Harmony patching) **[H]** |
| Prefabs (9,904; 9,034 are vanilla "explicitly referenced") | 29.7 s | ~10 % (mod prefabs) |
| Thumbnails / world ready | 8 s | partly (`-disableThumbnails`) **[H]** |
| Main menu | 6 s | no |

* **Prefab and asset loading cannot be skipped.** The world needs every prefab entity: zoning, growables and road
  compositions all resolve through them. Virtual texturing streams on demand (`TextureStreamingSystem`), so texture quality
  barely affects boot.
* Graphics settings change **frame time**, and that matters twice: the simulation runs at most 8 steps per frame (F24), and
  `CaptureScreenshot` renders 8 frames per shot.
* **Realistic estimate:** 60–80 s to the menu, then 15–20 s to load a small save. To be measured in the bootstrap run.

---

## 3. Isolation, safety and concurrency (host side: `tools/suite/Run-Suite.ps1`)

**Preflight. Refuse to start if any of these hold:**

* `Get-Process Cities2, "Paradox Launcher", dowser -ErrorAction SilentlyContinue` returns anything (the user is playing or
  about to).
* `.forensics/suite/lock` exists and its PID is alive. This makes the suite single-instance.
* `.forensics/suite/restore-pending.json` exists. In that case, **restore first** (a previous run crashed) and stop.

**Snapshot evidence (F29).** Copy `Player.log`, `Player-prev.log` and `Logs/` to `.forensics/suite/<runId>/user-logs/`
before launch. After the run, put them **back**, so the user's last-session evidence stays where the other teardowns expect
it. The suite's own logs go to `<runId>/game-logs/`.

**Back up and swap** (manifest with SHA-256 per file, written as `restore-pending.json` before any change):

* `Settings.coc`, `launcher-settings.json`, `UserState.coc`, `continue_game.json`, and all root `*.coc` files. With
  `cleanupSettings` on by default, I have not proven which `.coc` files the game rewrites, so all of them are restored.
* `ModsSettings/Placemaker/Placemaker.coc` (F26).
* `Mods/Placemaker/` **only when testing a candidate build**. The runner installs the candidate, records its version and
  git hash, and restores the user's installed build afterwards. The result always says which build ran and that the
  user's install was put back.

**Arm:**

* Write `ModsData/Placemaker/suite/run.json`: run id, scenario file, expiry 30 min, output folder.
* Write `runOnce.txt` (section 2.1).

**Launch and watch:**

* Start the game (route A or B) and record **the PID this runner started**.
* The mod writes `ModsData/Placemaker/suite/<runId>/heartbeat.json` every 5 s: phase, scenario, frame time.
* Hard timeout: 10 min + 3 min per scenario.
* No heartbeat for 120 s (hang): the runner kills **only its own PID**, marks the run `hung`, and still restores. A hang is
  itself a result: requirement 6 says switching style must never hang.
* The user's game is never touched, because preflight guarantees no other `Cities2.exe` existed.

**Collect and restore:**

* After exit, copy `ModsData/Placemaker/suite/<runId>/` to `.forensics/suite/<runId>/`.
* Restore every backed-up file and verify the hashes.
* Delete `runOnce.txt` if it is still there, put back the user logs, then delete `restore-pending.json` and the lock.

**Saves.** The test save lives in the user's `Saves/<steamid>/`; the game only lists saves from there. It is named
`Placemaker Test Suite (do not play)`. The suite never saves over it (`autoSave` off; the runner never calls Save in
scenario mode).

**Optional stronger isolation (not recommended first).** F11 rules out a separate user data folder by argument. A
directory junction swap of the whole `Cities Skylines II` folder would isolate everything, saves and caches included. But a
crash mid-run would leave the user facing an empty game until restore runs, so the file-level swap above is safer.

---

## 4. Mod side: a dev-only scenario runner compiled into Release but inert

### 4.1 Gate (`Game/Suite/SuiteGate.cs`)

The runner activates in `Mod.OnLoad` only if **both** hold:

* `GameManager.instance.cmdLine` contains `-placemakerSuite=<id>` (F3), and
* `ModsData/Placemaker/suite/run.json` exists, its `runId` equals `<id>`, and it has not expired.

On activation it renames `run.json` to `run.active.json`, so a crash cannot leave it armed. Neither condition can occur in
a normal user launch, because `runOnce.txt` is consumed by the suite launch itself. If the gate is closed, nothing is
registered: no system and no binding, so there is zero cost.

### 4.2 Runner system (`Game/Suite/SuiteRunnerSystem.cs`, `UpdateAt(SystemUpdatePhase.UIUpdate)`)

It sits in the UIUpdate phase because that phase runs in both the main menu and the game (`ToolRegistration.cs:43` uses
it the same way). The runner is a state machine stepped once per frame, never blocking:

```
WaitMainMenu  -> (GameManager.onGameLoadingComplete with GameMode.MainMenu)
LoadSave      -> find SaveGameMetadata whose displayName == scenarioFile.save (AssetDatabase.global.GetAssets<SaveGameMetadata>)
                 GameManager.instance.Load(GameMode.Game, Purpose.LoadGame, meta)   (same call as MenuUISystem.cs:805)
Settle        -> wait "Loading completed" + 120 frames; TextureStreamingSystem progress ~1; selectedSpeed = 0
for each scenario:
  Setup       -> force Placemaker settings in memory (IgnoreOwnCollisions, DebugTerrainDump=true, KeepOffExistingRoads ...);
                 tool.Toggle() (open); SuiteSeed = scenario.seed (F28)
  Design      -> tool.SelectRecipe(id); tool.ApplyPreset/SelectPreset; tool.SetValue(...) for each override
                 (exactly the UI trigger targets, F27); measure the max frame time while planning (hang check)
  Select      -> tool.SuiteDrawOutline(ring)  [AddPoint per corner + CloseOutline, the Draw path minus the mouse]
                 or tool.SuiteSelectBlockAt(xz) for block mode
  Preview     -> wait until !tool.Planning; shot "preview" (overlay and ghost visible, UI hidden)
  StyleSwitch -> optional: cycle scenario.styleSwitch recipes/presets, record the per-switch max frame time
  Build       -> tool.Primary() until Stage == Building; wait for BuildPhase.Done/Failed (timeout 60 s); keep the audit
  Zoning      -> wait for the zoning refresh line equivalent (BuildService exposes it); capture zone blocks/cells
  Grow        -> if scenario.grow: ZoneSpawnSystem.debugFastSpawn = true; selectedSpeed = 8;
                 run until the spawned count in the site is stable for 10 s or 90 s pass; then speed 0, fastSpawn off
  Census      -> SuiteProbe: live entities vs plan (section 4.5)
  Shots       -> "oblique" (frame the outline, pitch 45°) and "top" (pitch 90°, near-orthographic), PNG
  Teardown    -> either RemoveGroup(groupId) and verify nothing of the group is left,
                 or (isolation: "reload") reload the save before the next scenario
Finish        -> write summary.json; GameManager.QuitGame()   (F13; no save)
```

### 4.3 Tool hooks (`Game/Tool/PlacemakerToolSystem.Suite.cs`, internal, used only by the runner)

* `SuiteDrawOutline(float3[] corners)`:
  * `SetAreaMode(Draw)`, then `_draw.Clear()`;
  * `_draw.AddPoint(p, float2.zero)` per corner;
  * `CloseOutline()`.
* `SuiteSelectBlockAt(float2 xz)`: the block-mode face lookup for a world point. It is the only way to reproduce how the
  user usually selects.
* `SuiteSeed` (nullable int): replaces `NewSeed()` while set, so runs are repeatable (F28).
* Read-only accessors already exist: `Stage`, `Planning`, `Building`, `BuildAudit`, `BuildFailure`.

A later, more faithful step is a synthetic `FrameInput` source in `ReadInput()` (`PlacemakerToolSystem.Input.cs:35-46`).
It would let a scenario "click" corners and roads through the real input code, but it needs a raycast at a world point.
Start with the direct hooks.

### 4.4 Settings writes

The runner sets `PlacemakerSettings` fields **in memory**. The tool still calls `ApplyAndSave()` on mode changes (F26),
which is why `Placemaker.coc` is in the host backup set. An alternative is a `SuiteGate.Active` check in those three
call sites that skips the save.

### 4.5 What is recorded (`Game/Suite/SuiteProbe.cs`; the verdicts are pure in `Core/Suite/SuiteVerdicts.cs`)

**Live census, per scenario:**

* **Planned vs live per kind and prefab:** from the plan's layers vs `GroupRegistrySystem.GetMembers` (group id from
  `BuildStatus.GroupId`). Each member is classified by prefab type: net, object, decal, area, building, zone block.
* **Audit:** `BuildAudit` as already written to `audit/group-*.json` (planned / built / leftOut / kept, with reasons).
* **Zoning:**
  * planned cutout cells (from the plan's plots);
  * live cells in the site, per block: `Block`, `Cell.m_State`, `Cell.m_Zone`, and which net created the block (our
    group's or a city street);
  * cells "taken by a city-street block" (the shot-107 case).
  * 02-logs G3 asks for the same data, so this capture doubles as an offline xUnit fixture for `ZoneGuard`.
* **Growables** in the site after Grow:
  * prefab, position, `UnderConstruction` yes/no;
  * `Building.m_RoadEdge` (F15) resolved to "own road" (`GroupOf(edge) == groupId`) or "city street" with its name.
* **Parking reachability.** Three counts:
  * `unreachableStalls`: parking lanes of our nets whose road edge is not connected (by `ConnectedEdge` walk) to the city
    network;
  * `stallsWithoutLane`: parking-space decals farther than 3 m from any drivable lane;
  * `fencedAccess`: fence segments crossing the line between a parking lane and its access road.
  * These are geometric proxies, not pathfinding **[design]**; they catch shot-106 (stalls in strips behind fences).
* **Pits:** the existing `DebugTerrainDump` before/after grids (`BuildService.Dump.cs`). The verdict counts cells within
  10 m of any road where `before - after > 1.5 m` beyond the planned terraform target, and reports the max depth.
* **Hangs:** the max and p95 of `Time.unscaledDeltaTime` during Design, StyleSwitch and Build (requirement 6).
* **Errors:** count of game errors and exceptions during the scenario. Subscribe to `UnityLogger.OnErrorOrHigher` and
  `OnException`, as `TestScenario` does in the decompiled framework (F18).

**Result file:** `results/<runId>/<scenarioId>.json`.

```json
{ "scenario": "stripMall-alley-A", "build": {"version": "0.1.0.0", "git": "d9f6df6", "installed": false},
  "timings": {"planMs": 1830, "buildMs": 5600, "maxFrameMs": 410, "styleSwitchMaxFrameMs": [380, 2900, 400]},
  "audit": {"planned": 236, "built": 229, "leftOut": 7, "kept": 91},
  "kinds": [{"kind": "net", "prefab": "Alley", "planned": 4, "live": 4}],
  "zoning": {"plannedCells": 36, "liveCells": 22, "takenByCityStreet": 14, "blocked": 18},
  "growables": [{"prefab": "EU_CommercialLow01", "faces": "city", "road": "Summit Street", "underConstruction": true}],
  "parking": {"stalls": 80, "unreachableStalls": 12, "stallsWithoutLane": 6, "fencedAccess": 3},
  "terrain": {"maxPitDepth": 7.8, "pitCellsNearRoads": 41},
  "errors": {"game": 0, "exceptions": 0},
  "verdicts": [{"id": "shopsFaceOwnRoad", "pass": false, "detail": "0 of 2 growables face the build's lane"}],
  "shots": ["stripMall-alley-A.preview.png", "stripMall-alley-A.oblique.png", "stripMall-alley-A.top.png"] }
```

### 4.6 Screenshots (`Game/Suite/SuiteCapture.cs`)

**Framing:**

* Use `CameraUpdateSystem.gamePlayController`: `pivot` = outline centroid, `angle` = (yaw, pitch), `zoom` = bounding radius
  × k. This is the same controller `Locate` uses (`PlacemakerToolSystem.Review.cs:90-113`).
* Wait 60 frames and until texture streaming settles before shooting.

**Result shots** ("oblique" and "top"): `ScreenCaptureHelper.CreateRenderTarget(..., 1280, 720)` +
`CaptureScreenshot(Camera.main, rt, new MenuHelpers.SaveGamePreviewSettings())` (F16). Then `RenderTexture.active = rt`,
`Texture2D.ReadPixels`, `EncodeToPNG`, and write the file. Overlay and UI are hidden by the helper.

**Top-down:**

* Pitch 90° with `Camera.main.fieldOfView` narrowed to about 10° from high altitude, then restored. This is a near-
  orthographic view.
* True `camera.orthographic` under HDRP may break terrain, VT or water rendering. Try it only as an option **[H]**.

**Preview shot** (overlay and ghost visible): the photo-mode pattern (F17):

* `userInterface.view.enabled = false`;
* `WaitForEndOfFrame`;
* `ScreenCapture.CaptureScreenshotAsTexture()`, then PNG;
* re-enable the view.

**Colour check [H]:** the save preview RT is created with `sRGB: false` (`GameManager.cs:970`). Verify in the bootstrap
run that PNGs are not too dark or too light.

### 4.7 Scenario file (`suite/scenarios/*.json`, in the repo)

```json
{ "formatVersion": 1,
  "save": "Placemaker Test Suite (do not play)", "fixture": "pm-suite-v1",
  "settings": {"IgnoreOwnCollisions": true, "DebugTerrainDump": true, "KeepOffExistingRoads": true},
  "defaults": {"seed": 1, "grow": false, "isolation": "delete", "shots": ["preview", "oblique", "top"], "timeoutSec": 120},
  "scenarios": [
    { "id": "stripMall-alley-A", "site": "A-flat-between-avenue-and-street",
      "recipe": "stripMall", "preset": {"layer": "stripMall", "id": "dense"},
      "values": [{"path": "0", "id": "lanes", "json": "\"alley\""}],
      "select": {"mode": "draw", "ring": [[1200, -800], [1290, -800], [1290, -720], [1200, -720]]},
      "grow": true, "isolation": "reload",
      "styleSwitch": [{"recipe": "highStreet"}, {"recipe": "outdoorMall"}, {"recipe": "stripMall"}],
      "expect": {"shopsFaceOwnRoad": true, "maxLeftOut": 0, "maxPitDepth": 1.5, "unreachableStalls": 0,
                 "fencedAccess": 0, "maxFrameMs": 250, "noCropSurfaces": true} },
    { "id": "parking-compact-slope-C", "site": "C-slope-6pct", "recipe": "parking", "preset": {"layer": "parking", "id": "compact"},
      "select": {"mode": "block", "at": [1500, -650]} }
  ] }
```

Recipe ids are the files in `Recipes/` (for example `stripMall`, `parking`, `highStreet`, `mallDistrict`, `outdoorMall`).
Preset ids are the `Preset("...")` names in each layer, for example `MallDistrictLayer.cs:44-45` (`dense`, `village`) and
`ParkingLayer.cs:209` (`compact`). `noCropSurfaces` checks the auto-picked surface prefabs against a deny list (for
example names containing "Farm" or "Crop") unless the scenario asks for them.

---

## 5. Test map and save

**Recommended: a programmatic fixture.** The save is a build product; it is not kept in git.

* `suite/fixtures/pm-suite-v1.json` (in the repo) describes the starting map name and these sites (world XZ), each 150–250
  m apart so scenarios cannot interfere:
  * **A** flat block between a 4-lane avenue and a 2-lane street. This is the shot-107 "alley vs city street" case.
    Residential zoning painted on the avenue side reproduces "city zones take shop cells".
  * **B** flat rectangular block inside a 2-lane grid, 100 x 80 m.
  * **C** 6 % slope; **D** rough terrain (±4 m hills), the pit case of shot-106.
  * **E** irregular block with a curved road; **F** corner lot with existing growables and one service building on one
    side.
  * **G** an empty field with no road access, for the "unreachable" negative case.
* `Game/Suite/SuiteFixtureBuilder.cs` runs when `run.json` says `"mode": "fixture"`. It:
  * starts a new game from the map: `GameManager.instance.Load(GameMode.Game, Purpose.NewGame, mapMetadata)`, with the
    options the menu sets (`MenuUISystem.cs:755-772`): unlimited money, unlock all;
  * flattens the sites;
  * lays streets by emitting net CreationDefinitions through the same path `BuildService.Emit` uses (vanilla road
    prefabs);
  * zones cells on site A;
  * plops the existing buildings of F;
  * saves through the game's own save, `GameManager.instance.Save(name, SaveInfo, db, previewRT)`
    (`GameManager.cs:945-1015`, called the way `MenuUISystem.QuickSave` does at `:945-970`).
* Then it records the SaveGameMetadata guid and a fixture hash into `suite/fixtures/pm-suite-v1.lock.json`, and copies the
  `.cok` to `.forensics/suite/saves/` as a backup.
* Rebuild the save when the game version changes. Scenario runs check `fixture` against the lock and refuse a mismatch.
* **Caveat [H]:** building the streets with our own emitter couples the fixture to code under test. Keep the fixture to
  plain straight and curved vanilla roads, and have the builder verify each road exists before saving.

**Fallback:** the lead builds the save by hand in one supervised session with the user, using the same site list, and
saves it under the same name. It is quicker to get but not reproducible.

**Map choice [H]:** CS2 maps are all full size, so load time is set by the engine, not the city. An empty small city on any
map loads in about the measured 16.7 s. Pick a map with a large dry area near the centre; the builder flattens the sites
anyway.

---

## 6. Loop time

| Step | Estimate | Basis |
|---|---|---|
| Preflight, backups, swap | 5 s | file copies |
| Boot to main menu | 60–80 s | F22 minus PDX mods **[H]** |
| Load test save + settle | 20–25 s | F22: 16.7 s + 120 frames |
| Scenario, build only | 20–30 s | plan 1–5 s, preview shot 3 s, build 5–7 s (F23), census + 2 shots 5 s, delete 3 s |
| Scenario with Grow | 60–90 s | + up to 60 s fast spawn at speed 8 |
| Reload between isolated scenarios | 15–20 s | F22 |
| Quit + restore | 10 s | F22: 2.2 s quit |

**10 scenarios** (4 with Grow and reload, 6 build-only with delete) come to about **12–17 minutes**. A new DLL always
needs a fresh boot, because mods cannot be unloaded. Iterating on scenarios alone (same DLL) can reuse a running game: the
runner can poll a `queue/` folder before quitting. That is a later addition; the first version boots per run.

---

## 7. How the lead reads results

1. **`summary.json`** first: one row per scenario with pass/fail per verdict, timings, errors, and `hung`, `timeout` or
   `crashed` status.
2. **Diff against a baseline.** `tools/suite/Compare-Suite.ps1 <runId> [baselineRunId]`, or a `--suite-diff` mode in
   `Tests/Placemaker.Gallery`, prints only the changes: verdict flips, count deltas over thresholds, new left-out reasons,
   frame-time regressions. Baselines live in `suite/baselines/<scenarioId>.json` and are updated only by an explicit
   `--accept`.
3. **PNGs** with the Read tool. Look at oblique and top for failing scenarios only (each 1280x720 PNG is cheap to read).
   Then send the most telling one to the user with SendUserFile.
4. **Offline re-render.** The Gallery already renders terrain dumps (`BuildService.Dump.cs` header). Add a plan-vs-live
   overlay image from the result JSON (red: planned and missing; amber: built anyway; blue: zone cells taken by a city
   street). It needs no GPU and can be compared exactly.
5. **Captured zone grids and plans** become xUnit fixtures for `ZoneGuard`, `BuildVerdicts` and `Declutter`, as 02-logs §8
   and 04-shops §7 propose. Each suite run then adds offline regression cases.

---

## 8. One-time bootstrap run (supervised, with the user's OK, when they are not playing)

Check, in order:

1. **Route A.** Does `Cities2.exe -disablePdxSdk -noSplash` with `SteamAppId` and `SteamGameId` set start once, without the
   launcher and without a second process? Check `SceneFlow.log` "Command line:" and `Modding.log` (only Placemaker loaded).
   If not, try route B and see whether the launcher needs a click.
2. **Timing.** "GameManager initialized!" duration and the load time of the test save. This replaces the estimates in
   section 6.
3. **No engagement screen**, no tutorials or "what's new" popups with the suite profile.
4. **Capture.** Do `CaptureScreenshot` → PNG and the top-down camera produce correct colours and framing?
5. **Restore.** After quit, do all the backed-up files hash-match the originals once restored, and is the user's
   `Player.log` back?
6. **Fast spawn.** With `debugFastSpawn`, do commercial growables appear in a zoned test block within 60 s at speed 8?

---

## 9. Files and classes to add

| Path | Purpose |
|---|---|
| `Game/Suite/SuiteGate.cs` | gate (cmdLine token + armed `run.json`), registers the suite systems only when open |
| `Game/Suite/SuiteRunnerSystem.cs` | state machine in UIUpdate, heartbeat, save loading, quit |
| `Game/Suite/SuiteProbe.cs` | live census: group members by kind and prefab, zone blocks and cells, growables + `m_RoadEdge`, parking proxies, terrain samples, error counters |
| `Game/Suite/SuiteCapture.cs` | camera framing, RT capture → PNG, preview capture |
| `Game/Suite/SuiteFixtureBuilder.cs` | builds the test save from `suite/fixtures/*.json` |
| `Game/Tool/PlacemakerToolSystem.Suite.cs` | `SuiteDrawOutline`, `SuiteSelectBlockAt`, `SuiteSeed` |
| `Core/Suite/SuiteModels.cs` | scenario, fixture and result JSON models (pure) |
| `Core/Suite/SuiteVerdicts.cs` | pure verdicts (facing, pits, reach, fences, crop surfaces, frame budget) + xUnit tests in `Tests/Placemaker.Tests/Suite/` |
| `tools/suite/Run-Suite.ps1` | preflight, lock, backup and swap, launch, watch, restore |
| `tools/suite/Compare-Suite.ps1` | diff vs baseline |
| `tools/suite/profile/Settings.patch.json`, `tools/suite/profile/launcher-settings.json` | suite profile |
| `suite/scenarios/*.json`, `suite/fixtures/*.json`, `suite/baselines/*.json` | inputs and baselines |
| `Placemaker.csproj` | `AssemblyInformationalVersion` = git hash, so every result names its build (F30) |

**Not to use:** the game's `Colossal.TestFramework` (F18). Its reflection registration runs before mods load. Injecting
into its scenario dictionary and running with `-categoryFilter=` would work but relies on an internal QA tool. It adds only
error counting, which `UnityLogger` events already give us.

---

## 10. Hypotheses still open

* **H1:** route A starts without the launcher (needs `SteamAppId` and Steam running).
* **H2:** boot is 60–80 s with `-disablePdxSdk`.
* **H3:** `-disableThumbnails` saves part of the 8 s after prefab load.
* **H4:** no engagement screen on Steam PC.
* **H5:** near-orthographic shots render correctly; real `orthographic` may not.
* **H6:** capture colour space is correct.
* **H7:** speed 8 with fast spawn produces growables within 60 s.
* **H8:** the root `.coc` files of unloaded mods are left alone by a `-disablePdxSdk` session. Irrelevant in practice,
  since all of them are restored.
* **H9:** route B's launcher can auto-continue.
* **Fidelity:** the clean profile lacks asset-pack mods, so asset picks differ from the user's game. Run a "playset"
  profile (normal launch, all mods, about 100 s boot) before releases.

---

## Skeptic review

Reviewed 2026-10-15 against the decompiled game (`%TEMP%\pm-decomp\game`, `.forensics/scratch-suite/decomp`), the repo at
`d9f6df6`, the user data folder (read-only), the 12:05–12:13 logs and both screenshots. Nothing launched or changed.
Verdicts: **CONFIRMED** (re-checked, holds), **WRONG** (contradicted by evidence), **UNPROVEN** (not settled either way).

### Verdicts on the key claims

| Claim | Verdict | Evidence / correction |
|---|---|---|
| `-disablePdxSdk` loads only local mods; only `Mods/Placemaker` is local | **CONFIRMED** | `GameManager.cs:1325` skips the whole `RegisterPSI(PdxSdkPlatform)` block; `RegisterDatabase()` (`:1501-1512`) is only reached from its callbacks. `ModManager.RegisterMods` (`ModManager.cs:400-429`) enumerates `AssetDatabase.global`, no PDX gate. `Mods/` holds only `Placemaker/`; `.cache/Mods/local` is empty. All ~60 PDX DLLs load from `.cache/Mods/pdx_mods/...` (`Logs/Modding.log:172-212`). |
| Summary wording "Paradox Mods are only registered after a Paradox login" | **WRONG (detail)** | They are also registered on `onNoLogin` (`GameManager.cs:1355`), i.e. offline. The conclusion still holds because neither callback exists without the PDX SDK. |
| "The user's playset is never touched" | **CONFIRMED for route A only** | Route B starts the Paradox launcher, which writes `launcher-v2.sqlite` and `.launcher-cache/` (both had 12:05 mtimes after today's launch). Neither is in the section 3 backup set. Add them, or drop route B. |
| `runOnce.txt` is read, deleted and merged; unknown flags are harmless | **CONFIRMED** | `GameManager.cs:443-451`; merge at `:328-339` (it splits on spaces into a HashSet, so token order is not kept). `SceneFlow.log:3-8,56`: the unregistered `--pdx-launcher-session-token` was ignored and `developerMode:true` still applied. Mono.Options' bundle fallback returns false for `-placemakerSuite=…`, because no 1-letter option `p` exists. |
| `-noSplash` | **CONFIRMED** | `SplashScreenSequence.cs:22-25` returns immediately. |
| `-disableThumbnails` "skips `InitializeThumbnails` work (part of the 8 s)" | **WRONG** | `InitializeThumbnails()` (`GameManager.cs:1898-1903`, called at `:631`) runs unconditionally. The flag is read in one place only: `ImageSystem.GetThumbnail` returns `Media/Placeholder.svg` (`Game.UI/ImageSystem.cs:108`). It saves nothing at boot; at most it saves UI thumbnail rendering. It also turns Placemaker's non-surface catalog icons into placeholders (`AssetCatalogSystem.Icons.cs:43`). Harmless, but drop H3 and the "partly" in the 2.3 table. |
| `-startGame` needs the metadata id, not the `.cid` | **CONFIRMED** | `Archipelago.cok.cid` = `be2eee70…`, while `SceneFlow.log:121` shows `MapMetadata:bf8036b2…`. `AutoLoad(guid)` → `TryGetAsset` → only Map/SaveGame data or metadata loads (`GameManager.cs:1241-1264`). |
| `-continuelastsave` only fires a UI event | **CONFIRMED** | `AppBindings.cs:215-219`. |
| `debugFastSpawn` spawns regardless of demand | **CONFIRMED, with limits** | `ZoneSpawnSystem.cs:1101-1117`. It removes only the demand gate. Lots still need `m_Availabilities` on the owning road (`:241`), and only the single best lot per type per chunk is queued (`:219-229`). In a fresh city with no services, H7 is still open. |
| `selectedSpeed = 8` for Grow | **WRONG (no gain)** | Steps per frame = `min(8, round(speed*num3*2))` (`SimulationSystem.cs:255`), so speed 4 already reaches the cap of 8. Speed 8 is no faster. Also, the setter is ignored while loading (`:78-81`). |
| `Building.m_RoadEdge` = the road the shop faces | **CONFIRMED** | The field is serialized (`Game.Buildings/Building.cs`), and spawn lots are keyed by the block owner road (`ZoneSpawnSystem.cs:198-212`, `owner.m_Owner`), so it identifies the street that shot-107's shops face. |
| `ScreenCaptureHelper.CaptureScreenshot` hides UI and overlays | **CONFIRMED** | `ScreenCaptureHelper.cs:93-147` (it also disables the Outlines pass). The default `SaveGamePreviewSettings` is unstylized (`MenuHelpers.cs:28-33`). Colour (H6) is still UNPROVEN. |
| Photo-mode capture pattern | **CONFIRMED** | `PhotoModeUISystem.cs:655-678` (uses `PlatformManager.TakeScreenshotAsTexture`, not `ScreenCapture.*` as 4.6 says; either works). |
| `QuitGame()` clean, 2.2 s | **CONFIRMED** | `GameManager.cs:735-844`, `SceneFlow.log:127` "GameManager destroyed (2175.6538ms)". |
| QA `TestScenarioSystem` registers before mods | **CONFIRMED** | `GameManager.cs:588` vs `:611/:624`; `TestScenarioSystem.cs:89-94,140-156`. |
| `RestartAppIfNecessary` in the path | **CONFIRMED (risk is real)** | `SteamworksPlatform.cs:511`. There is no `steam_appid.txt` in the install folder, so route A depends entirely on the env-var behaviour (H1 stays open). |
| Boot timeline 101.4 s, 9,904 prefabs in 29.7 s, new game 16.7 s | **CONFIRMED** | `SceneFlow.log:9,13,61-63,105-112,121-126`. Section 0 says "102 s"; the log says 101.4 s. Note that 16.7 s is a **NewGame on an empty map**, not a save load. |
| "~60 PDX mods" in the playset | **WRONG** | The active playset "Main" lists **163** enabled mods (`Modding.log:5-168`); 109 assemblies load and 82 of them carry Burst code. The "~10 %" prefab share (9,904 − 9,034 vanilla) still stands. |
| Attribution of boot segments to PDX mods; 60–80 s without them | **UNPROVEN** | The segments are timestamp gaps only. The PDX attribution is inference. |
| "Builds take 5–7 s" | **WRONG (range)** | `Placemaker-20261015-master.log`: 1979→2203 = 6.8 s, 2961→3026 = 6.4 s, **3035→3073 = 3.9 s**. The range is 4–7 s. |
| Loop time 12–17 min | **UNPROVEN, and inconsistent with its own table** | Summing section 6 (5 + 60–80 + 20–25 + 6×20–30 + 4×(60–90 + 15–20) + 10 s) gives about **8.6–12.3 min**. |
| A run rewrites user files: `Settings.coc`, `launcher-settings.json`, `Placemaker.coc`, `Player.log` | **CONFIRMED but INCOMPLETE** | `TerminateGame` → `LauncherSettings.SaveSettings` + `AssetDatabase.global.SaveAllSettings()` (`GameManager.cs:797-798`). **Missed: `ModsData/Placemaker/`**. A suite run overwrites `catalog-dump.json` with a clean-profile catalog (`AssetCatalogSystem.Dump.cs:36`). It appends to `surface-overlays.txt` (`SurfaceCloneSystem.cs:84,212-219`), `decal-opacity.txt` (`DecalOpacitySystem.cs:97,306-313`) and `park-companions.txt` (`ParkCompanionSystem.cs:147`); these lists are append-only, so there is no data loss, but they change. It adds `audit/group-*.json` (`BuildService.Audit.cs:302-304`), `debug/*.json` (`BuildService.Dump.cs:163-165`) and `thumbs/*.png` (`SurfaceSwatches.cs:124`). Back up and restore `ModsData/Placemaker/` (excluding `suite/`) too. Also back up `launcher-v2.sqlite` and `.launcher-cache/` if route B is used. |
| F26: `Placemaker.coc` written on "mode, brush and panel changes"; 4.4 "skip the save in those three call sites" | **WRONG (incomplete)** | `ApplyAndSave()` is also called from **`SelectRecipe`** (`PlacemakerToolSystem.Design.cs:102`), which every scenario calls, and from `SaveAs` (`:221`), `BlockPaint.cs:70`, `Input.cs:191` and `PlacemakerSettings.cs:167,254`. A `SuiteGate` skip would have to cover at least 8 sites, or go inside `ApplyAndSave` itself. |
| F28: `NewSeed()` at `:142` → `SuiteSeed` makes runs repeatable | **CONFIRMED, wider than cited** | `NewSeed()` (`PlacemakerToolSystem.cs:565`, `TickCount` + static counter) is used in `Design.cs:88,90,116,142,152,210,217` and `Presets.cs:80`. Overriding it inside `NewSeed` works only if the static `_seedCounter` is also reset per scenario. |
| F27: UI actions are public tool methods | **CONFIRMED** | `PlacemakerUISystem.cs:51-113`; `CloseOutline` `Draw.cs:102-110`; `PolygonEditor.AddPoint(float3, float2)` `Core/Tool/PolygonEditor.cs:163`. |
| F30: installed = `650be0f` "(12:01 deploy)"; HEAD `d9f6df6` | **CONFIRMED / WRONG (time)** | The installed `Placemaker.dll` is 3,289,088 B with mtime **09:18:13**, matching the `650be0f` commit at 09:17:57. Only the folder is 12:01. `bin/Release` (12:14, 3,301,888 B) is `d9f6df6` and is **not installed**. |
| Fidelity: "the farmland surface may come from a surface-pack mod" | **UNPROVEN, and evidence leans the other way** | Placemaker's catalog lists **`Agriculture Surface 01`** in `surface.crop` and in the generic `surface` pool (`Placemaker-20261015-master.log:138-139`). The string appears in vanilla `Content/Game/Locale.cok`, so it is a vanilla surface. The packs (`Farming Surfaces & Decals`, `Agriculture surface pack`) are present too. The audits do not record which area prefab was built, so the source of the farmland in shot-106 is unknown. The clean profile can probably still reproduce it. That is good for the suite, but the stated fidelity gap is not the proven cause. |
| "Only Placemaker loads" ⇒ results are representative | **UNPROVEN** | The playset also supplies Anarchy, Better Bulldozer, Traffic, RoadBuilder, EasyZoning and others (`Modding.log:5-168`). These change zoning and validation behaviour in the user's game; for example, Harmony hooks from other mods could affect zone blocks. Clean-profile zoning and facing results are profile-independent only by assumption. |

### Corrected conclusions

1. The suite is still feasible. The boot isolation (`-disablePdxSdk` + `runOnce.txt` + gate token) holds up against the code.
2. Remove `-disableThumbnails` as a boot saving. Do not use speed 8 (use 4). Build time is 4–7 s. By the report's own numbers the loop is about 9–12 min, not 12–17.
3. The restore set is missing `ModsData/Placemaker/` (catalog dump overwritten, remembered-clone lists appended, audit/debug/thumb files added). For route B it is also missing the launcher's `launcher-v2.sqlite` and `.launcher-cache/`. The "skip settings save" alternative must cover `SelectRecipe` and five more call sites.
4. The "farmland comes from a pack" fidelity caveat is unsupported. `Agriculture Surface 01` is vanilla and sits in Placemaker's own surface pools.
5. The installed build is 650be0f from 09:18, not a 12:01 deploy. Every result must still record its build hash.
