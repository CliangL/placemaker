# Placemaker in-game suite

One game launch: a new game on the vanilla map Plains, the fixture area reshaped in place (water removed, trees and
nets bulldozed, flat / slope / rough ground easing into the map), a grid of vanilla roads, low-rise housing grown on
the zoned row, two plopped parks that must survive, then many scenarios, each on its own block. Every action is done
the way a person does it, every result is judged right away, every picture is taken only once the scene is provably
still, and a contact sheet shows all of it in order.

The tool exists so an architect can check a Placemaker change in the real game without the user. Read
"What PASS means" before trusting a result.

## The loop

1. **Write a scenario** (below). The vocabulary is in `suite/schema.json`; the types, styles and option ids of this
   build are in `suite/catalog.json` (written by every run). Unknown keys, steps, targets and values are refused
   before anything runs.
2. **Run it**: a set in one launch, or keep the game open and send scenarios one after another:

   ```
   powershell -File tools/suite/Run-Suite.ps1 -Set core                  # a set, then quit
   powershell -File tools/suite/Run-Suite.ps1 -Set queue -Queue          # fixture only, then wait for scenarios
   powershell -File tools/suite/Send-Suite.ps1 -File my-scenario.json    # run it in the open session, print the result
   powershell -File tools/suite/Send-Suite.ps1 -Json '{"command":"grow","seconds":40}'
   powershell -File tools/suite/Send-Suite.ps1 -Quit
   ```
3. **Read the result**: Send-Suite prints each scenario's result (PASS / FAIL / UNKNOWN), every check that did not
   pass with its reason, the PNGs and the result file; exit code 0 = all PASS, 1 = a FAIL, 3 = only UNKNOWN,
   2 = schema error, 4 = the game went away. In the run folder: `summary.md` (headline, one row per scenario),
   `contact.html` (every shot in order, captioned), `scenarios/NN-<id>.json` (everything measured).
4. **Compare** with a run before the change: `Compare-Suite.ps1 -Old <runId> -New <runId>` (or `Run-Suite -Baseline
   <runId>`): FIXED / NEW FAIL / STILL FAIL / CHANGED NUMBERS, scenarios that moved block, per-shot pixel differences.
5. A code change needs a relaunch (about 60 s to a ready fixture, plus about 10 s to build). Only one game at a time:
   Run-Suite takes `.forensics/suite/lock`, and stops when a Cities2.exe it did not start is running (a suite game it
   left behind is asked to quit through its inbox; the user's own game is never touched).

Other Run-Suite switches: `-AllMods` (the user's whole playset; slow boot), `-UserModSettings` (keep the user's
Placemaker options), `-Look game` (the map's own light and weather), `-Pace settled|user`, `-Grid 20x9`,
`-MaxScenarios N`, `-NoSwitching`, `-NoBuild`, `-RestoreOnly` (put the user's settings back after a cut-off run).

## What PASS means (and what it does not)

* A scenario is **PASS** only when every check it must have was measured and passed. A check that could not be
  measured is **UNKNOWN** (with the reason) and makes the scenario UNKNOWN, never PASS. Which checks a scenario must
  have follows from what it did: a build expects the build checks, a delete the delete checks, a grow the growth
  checks. A check that should exist but produced nothing is reported "expected but not measured".
* The checks measure the world independently of Placemaker's own report: its build audit is shown as
  `nothingLeftOut`, but `nothingMissing`, `previewMatchesResult` and `plannedVisible` count for themselves.
* **Clean profile**: only Placemaker loads (`-disableModding`); no asset packs, no other code mods. Decals, surfaces,
  fountains/statues, park dressing and the switching hang differ from the user's game (summary.md says so on its
  Fidelity line). Visual or decal claims and "switching ok" need a full-playset run (`Run-Suite -AllMods`).
* **Readability mode**: the light, weather and icons are the suite's, not the user's (see Look). Shots show the
  build in clean noon light, not in the user's lighting mods.
* Placemaker's options are **pinned to its defaults** for the run (summary.json `placemakerSettings`, with every option
  that differs from the user's); `-UserModSettings` keeps the user's. The user's settings and ModsData/Placemaker
  files are saved before the run and put back after it.
* Not measured: real traffic (no demand for parked cars), anything that needs days of simulation, other mods'
  interaction, left-hand traffic and other city themes, money (unlimited) and locked items (all unlocked).

## Scenario files

A set (`suite/scenarios/<name>.json`, or any file with `-Set path\to\set.json`) is
`{"scenarios": [...], "switching": {...}, "grow": true, "growFast": true, "settings": {"Option": value}}`.
Sets in the repo: `smoke` (4 one-liners and a short switching run), `core` (every parking and commercial style,
plazas, parks, roads, both build paths, user pace, a second build of the same type), `full` (every type), `steps`
(step scripts of every tool), `examples` (the architect scenarios in `suite/scenarios/examples/`), `selftest`
(things the suite must refuse or fail loudly, and the bucket clicked again after Clear).

| key | meaning |
|---|---|
| `id` | the scenario's name (results and PNGs are named after it) |
| `layer`, `preset` | a one-line scenario: bucket the block (or `draw` a rectangle), pick the type and style, entrances where the button asks, preview (an overlay shot and a clean ghost shot), build, built oblique and top shots |
| `draw` | `[w, h]` metres: the Points tool draws this rectangle at the block centre instead of the bucket |
| `values` | `[{"path":"0","id":"aisleWidth","json":"7"}]`: options set before the preview |
| `steps` | a step script (below) instead of the one-line steps |
| `expand` | `"presets"` (each style of `layer` or `group`; `limit` = the first N styles of each type, `skip` = style ids) or `"types"` (every type in the Make row); with `steps` the steps are a template (`type` without `layer` takes the expanded type and style). Ids become `<layer>-<preset>` (`-small` with `draw`), a duplicate gets `+` |
| `site` | `flat`, `zoned` (row 1: low-rise housing on its south street, grown before the first zoned scenario), `slope`, `rough`, `survive` (two plopped parks), `any`. **Never replaced by another kind**: when none is left the scenario is not run (`no-block`) unless `siteFallback: true` (the `siteAsAsked` check then reports it) |
| `block`, `streets` | a given block (`"B3-0"`), or a street signature (`{"south":"Medium Road","north":"Small Road"}`) |
| `on` | runs on an earlier scenario's standing build: camera, shot, issues, select, delete, bulldoze, infoEdit, infoRemove, wait, grow, compare; any design step needs `{"do":"edit","build":true}` first. Refused when that build was deleted or never built — so put it before the delete |
| `seed` | design seed (default 1). A type pick starts a fresh design with this seed (as after opening the game) unless the step says `"fresh": false` (the UI's next build of the same type keeps the design) |
| `pace` | `human` (default: the cursor glides to every click, clicks at least 0.4 s apart, sliders dragged, brush strokes at 30 m/s as at 60 Hz), `settled` (no glide, fast strokes), `user` (no waiting for "Planning…": presses on) |
| `buildWhen` | one-liners: `settled` (Build once the ghost is shown: the instant path), `planReady` (Next then Build at once: the build service plans it), `ghostPending` |
| `previewMode` | one-liners: `sketch` sets Review to Sketch before building |
| `note` | free text |

Keys starting with `_` are comments.

## Steps

Every step can add `"shot": "<name>"` (a settled PNG after the step), `"camera": {...}` (the framing of that shot;
without one a build/preview/delete/edit/bulldoze/area step frames the build, oblique) and `"shotMode"`.

Panel actions click the panel's real element when it is on the screen (recorded per action in `ui`: `click`), else
fire the panel's own trigger through the UI view (`trigger`: the binding with its argument conversion), and only when
the UI view does not answer call the tool (`direct`). A greyed-out button is pressed like a person would; when that
does nothing the step fails (no trigger behind its back). Design controls (type, style, options, entrances) live in
Design: a script in Review goes back to Design first, recorded under `implicit`. Every action checks its effect and
fails with the reason when nothing changed.

| step | keys (defaults) | what a person does |
|---|---|---|
| `area` | `tool`: `bucket` (default), `points`/`draw`, `paint` (smart edges), `paintAnything`, `nodes`; `at` `[dx,dz]`; `points` `[[dx,dz],…]` or `rect` `[w,h]`; `stroke` `[[dx,dz],…]` with `size` (m, default 45 % of the block), `square`, `angle`; `nodes` `[[dx,dz],…]` (default the four junctions); `abs`; `cameraDistance` (320) | looks at the block from 320 m, picks the area mode, then: one click (bucket); a click per corner and the first again (points); a brush drag (paint); a click per junction and the first again (nodes). Paint ends with Next. The outline is checked against what was meant (`outlineAsIntended`) |
| `clear` | – | Clear (in Area) |
| `type` | `layer`, `preset` (style), `fresh` (true) | the type tile of the Make row, then the style chip; refuses a type the Make row does not offer; lists the valid ids on a typo |
| `preset` | `layer` (the current type), `preset` | the style chip (Design); fails unless it becomes the active style |
| `value` | `path` ("0"), `id`, `value` or `json` | a slider is dragged (previewValue each frame, then setValue and endPreview, clamped and snapped to the control's range); a picker hovered then clicked; a toggle or choice set; refused when the panel greys the control out; the value is read back |
| `entrance` | `side` (`south`/`east`/`north`/`west`, `t` 0.5 along it west→east or south→north) or `at`; `path`, `kind` | presses "Add an entrance" (or picks the marker kind), clicks the outline at the point, Esc. The tool's own (snapped) marker is recorded |
| `entrances` | – | while the button asks for entrances: one on the south street, then east, north, west |
| `moveEntrance` | `index` (0: the script's n-th entrance, matched by position), `side`/`t`/`at` | in placement mode: right-click the marker, left-click the new spot, Esc; fails unless the old spot is empty and the count is right |
| `removeEntrance` | `index` | right-click the marker |
| `preview` | – | Next to Review; waits for the plan and the ghost; records the issues the Review panel lists and a census of the ghost (at a standard framing, camera put back) |
| `previewMode` | `mode`: `sketch`/`ghost` | the preview switch |
| `issues` | – | records what the Review panel lists (plan, layers, build notes, clip, ghost-dropped, failure) |
| `edit` | `build` (false) | back to Design; with `build: true`: clicks the build (Manage) and Edit |
| `build` | `when` (`settled`; `planReady`; `ghostPending`), `via` (`panel`; `enter`) | (Next if needed,) OK for permanent changes, Build; waits for it; records the build path (`instant` from the ghost or `start`); then frames the build and measures everything (Checks) |
| `select` | – | the Manage stage, the game's raycast click on the build; fails unless one click selects it |
| `delete` | – | select, Remove, OK; waits until the group is gone and the block stops changing; records what is left in the whole block (+10 m). The ground is restored only at the end of the scenario (after any after-delete shot) |
| `bulldoze` | `target`: `owner` (the owner lot), `part` | the bulldozer's delete definition on that member (applied through the suite's fixture tool, not the bulldozer UI), then the group's own cascade |
| `infoEdit` / `infoRemove` | – | the Placemaker tool closed, the build selected the vanilla way, its info panel's Edit / Remove |
| `restore` | – | puts the block's ground back, checks it, frees the block only when nothing was left |
| `camera` | `target`, `view`, `index` (or `"all"`), `yaw`, `pitch`, `distance`, `fov`, `at`, `abs` | frames the camera (below); a target that does not exist is an error and no picture is taken |
| `shot` | `name`, `shotMode`, `labels` (false: street names stay off) | a settled PNG of the current view |
| `wait` | `seconds` (1) or `frames` | a plain wait |
| `grow` | `seconds` (30), `fast` (true) | the simulation with debug fast spawn at speed 4 (`fast: false` = speed 1 with real demand) until the building count is still for 8 s; grows the whole city; measures growables, visibility and existing buildings again |
| `undo` / `redo` | `via`: `key` (Ctrl+Z / Ctrl+Y, default), `panel` | fails when there is nothing to undo or redo, or the history did not move |
| `key` | `key`: `enter`, `esc`, `undo`, `redo` | the key, through the tool's own key handling |
| `anarchy` | `on` | the panel's anarchy switch — fails "Anarchy mod not loaded" in a clean run |
| `setting` | `name`, `value` | a Placemaker option (e.g. `IgnoreOwnCollisions`), read back |
| `compare` | `a` (1), `b` (2) | compares two builds of this scenario (members, seen objects per prefab) |

World clicks: the point is projected to the screen; off the screen or under the panel the camera pans to it like a
person (recorded as `pan`), else the step fails "not clickable". The game's own raycast with the tool's masks decides
what is hit (recorded `hit`, `hitEntity`, `hitOffM`), the cursor glides there, then clicks. Between steps the cursor
is over the panel (no hover on the map). Every step starts with an empty input queue; frames the tool never took
fail the step.

## The grid

Blocks are 150 m (x) by 140 m (z) between road centre lines, named `B<i>-<j>` (i west→east, j south→north). Vertical
streets (along z, line i): Medium Road (4 lanes) at i = 0, 4, 8…, Pedestrian Street at i = 3, 7, 11…, Small Road
otherwise. Horizontal streets (along x, line j): Medium Road at j = 0, 3, 6…, Small Road otherwise. A block's
streets are its West (line i), East (i+1), South (j) and North (j+1) — recorded per scenario.

```
 j=NZ-1  S S S S S S S S S …    S = slope (6 % up to the north, roads included)
 j=NZ-2  S S S S S S S S S …
 j=NZ-3  R R R f f f f f f …    R = rough (±4 m hills, roads level), columns 0-2
 …       f f f f f f f f f …    f = flat
 j=2     f f f f V f f f f …    V = survive (two plopped parks), column 4
 j=1     Z Z Z Z Z Z Z Z Z …    Z = zoned (low-rise housing on the south street)
 j=0     f f f f f f f f f …
```

Flat blocks are used on one colour of a checkerboard (`spacing: checker`) so no scenario's neighbours hide or shade
it; the grid is made wide enough for the set (summary.json `capacity`, `blocksNeeded`). A block is used once; a
deleted build's block is freed only when nothing was left behind and the ground is back, else it is quarantined.

## Camera

`target`: `block` (default), `area`/`build` (the outline's bounding box), `entrance` (`index`: the n-th entrance
the script placed), `connection` (where the build is entered from a city street, from the lane graph — also for types
without entrance markers), `issue` (the n-th issue with a map position), `pit` / `mound` (the worst sample measured),
`growable` (the n-th new growable), `point` (`at`, `abs`), `overview`. `index: "all"` frames every one, shots named
`<shot>-<n>`.

`view`: `oblique` (yaw 25, pitch 60, 3.4 x the target's radius), `top` (yaw 0, pitch 89.5, field of view 18°:
nearly orthographic; the record gives metres per pixel), `closeup` (yaw 30, pitch 38, 45-60 m), `low` (pitch 20).
The camera sits south of its target and looks north; the noon sun is behind it. The game lifts the eye over terrain
and clamps tilt: the rendered pitch and distance are measured and the request corrected (up to 3 passes); the record
keeps `asked` and `effective`, and `clamped` when the target could not be reached.

## Shots

* `shotMode`: `render` (the clean scene: no outline, no icons, no street names), `overlay` (with Placemaker's outline,
  ghost symbols and markers; the default while the tool is open outside Manage; `grab` is an old name for it), `ui`
  (the real screen with the panel). All modes have the window's size (1600x900), so pairs line up pixel for pixel.
* **Settle gate**: (1) a wall-clock floor since the last event (camera move 0.35 s, ghost 0.35 s, build, delete and
  restore 0.6 s, grow 1 s); (2) the engine quiet for at least 10 frames and 150 ms (camera still, no mesh loading,
  full level of detail, virtual texturing idle with no tile counter moving, no fading batches, the ghost's
  temporaries stable); (3) the pixel proof: clean render captures at least 250 ms and 15 frames apart until two
  consecutive pairs differ by less than the run's noise floor (changed pixels and the worst 32x32 tile; the far top
  15 % of a non-top view is left out). Then the picture is taken in the asked mode (the overlay and the panel may
  animate). A shot that does not settle in 10 s is saved, gets a heat map (`*.unsettled-heat.png`) and fails
  `shotsSettled`; summary.md lists it under "Unreliable evidence". The record has every phase's time, the pairs and
  the engine signals.
* Captures use SMAA (deterministic) instead of the game's TAA; the noise floor is measured at the start of the run
  (summary.json `noiseFloor`).
* Names: `shots/<NN>-<id>.<step>-<name>.png` (NN = the scenario's number in this run); never overwritten.

## Look (readability mode; `-Look game` turns it off)

Noon (12:00 local solar time) on day 172 at latitude 45° N (the Plains map is at 44° S, where June is midwinter):
the sun about 68° up, due south. A look volume above the game's lighting: a fixed exposure calibrated at the start of
the run to a mean brightness of 122/255, indirect light x1.3, wind 0. No rain, clouds, fog, aurora or snow; the
climate's warmest date. Notification icons off (tool-error icons stay; icons on the build are counted in the census as
data), street names off, full level of detail without LOD cross-fade, no edge scrolling. The fixture's own growth is
low-rise housing; tall buildings in a shot come from the types under test, and the checkerboard keeps them off a
target's neighbouring blocks. Everything is recorded in summary.json `look`.

## Checks

| check | PASS means | it does NOT prove |
|---|---|---|
| `ran` | the scenario ran on a block (not refused, no suite error) | |
| `siteAsAsked` | the block is of the kind asked | |
| `step<i>:<do>` | (only listed when failing) what that step could not do; a suite bug there is UNKNOWN, never FAIL | |
| `outlineAsIntended` | the outline matches the clicks: points ≥75 % overlap and area within 15 %; bucket and nodes ≥80 % overlap with the block's land between the kerbs (50 % on zoned/survive blocks, whose houses stay out); paint ≥70 % of the area the brush swept | |
| `noTimeouts` | no readiness wait timed out (shots are judged apart) | |
| `shotsSettled` | every picture was proven still | that it shows the right thing |
| `built` | the build finished, has members and at least one seen object | |
| `plannedVisible` | every member object meant to be seen is alive, not Overridden/Hidden/Unspawned, and drawn wherever it is in view and big enough (measured at a standard oblique framing of the build) | small props off the screen |
| `plannedVisibleAfterSim` | the same after the simulation ran | |
| `previewMatchesResult` | per prefab, as many objects drawn after the build as the ghost drew (same framing), minus what Placemaker left out with a reason. UNKNOWN when no ghost of the built plan was shown (planReady, Sketch, edits after the preview) | positions |
| `census` | the census ran | |
| `nothingLeftOut` | (only when Placemaker reports an audit) Placemaker says it left nothing out | anything (self-report) |
| `nothingMissing` | independent: every planned object stands within 0.75 m, nets have their planned length, areas their count; misses beyond what Placemaker admits fail | |
| `noUntrackedPieces` | every new piece in the block (+10 m) belongs to the build (after a rebuild: nothing of the first build is left) | |
| `noFarmland` | no extractor (crop / forestry) area in the site (names are only hints) | |
| `noForeignFences` | no visible fence lane (found by type; cut spans removed) the plan did not ask for | |
| `stallsBuilt` | as many stalls seen as planned | that cars can reach them |
| `stallsUsable` | every planned stall's own parking lane is reachable by car from a city street (lane graph) | that cars will park there |
| `netsConnected` | every member net with lanes is reachable from a city street through lanes (side connections included) | traffic |
| `entrancesUsable` | each entrance: a car (a pedestrian for path entrances) reaches the build's lanes from the street, entering within 25 m of the marker, and no uncut fence or barrier lies across the first 12 m | that it looks like a driveway (see the close-ups) |
| `zoningLive` / `noZoningSpill` | ≥90 % of the plan's zone cells are newly zoned / no newly zoned cell outside the outline the plan did not zone | |
| `existingSurvive` / `existingSurviveAfterSim` | buildings in the site and 30 m around still stand, seen, unmoved | |
| `noPits` / `noMounds` | the ground over the outline + 20 m (2 m grid) is not more than 1.5 m below / above anything the plan or the old ground allows | |
| `growablesFaceIntendedRoad` | growables on the plan's zone cells face the build's own road; UNKNOWN when nothing grew there | real demand (fast spawn bypasses it) |
| `deleteLeavesNothing` | after a delete: nothing new in the whole block + 10 m that is not a city street's, and as many zoned cells as before | |
| `deleteRestoresStreets` | the streets around are as before the build (a street split for the build and left split fails) | |
| `deleteRestoresGround` | the ground is back within 0.25 m (not judged when the plan declared permanent terraform) | |
| `noErrors` | no error or exception of any origin during the scenario (+30 frames) | |
| `noHang` | no frame over 1 s, minus the suite's own work in it, charged to the step running when the frame started | |

## Results

`runs/<runId>/`: `summary.md` (host status line, headline, fidelity, look, settings, one row per scenario, the style
switches of scenarios that change style several times, unreliable shots, every check that did not pass),
`summary.json` (all of it, plus timeline, catalog, noise floor, quarantined blocks), `contact.html`,
`scenarios/NN-<id>.json` (each step with its waits, timeouts, frame stats, presses, UI path and effect; `builds[]`
with the census of each build; `verdicts`, `failed`, `unknown`, `result`), `shots/`, `logs/`, `audit/` (Placemaker's
build audits written during the run), `host.log`, `catalog.json` (also copied to `suite/catalog.json`), `dirty.patch`
when the tree was dirty. The run is labelled with the git hash and the deployed DLL's hash; the game checks it loaded
that DLL ("wrong build" stops the batch).

If the game is closed early the quit hook writes what is known, marked incomplete; the host classifies the end
(`quit`, `closed-externally`, `crashed`, `killed`, `hung`, `boot-timeout`) and puts it at the top of summary.md.

## Live queue

`Run-Suite -Queue` keeps the game open (after the set, or right after the fixture with `-Set queue`). Send-Suite drops
a file into `<run>/inbox`; the result comes back as `<file>.result.json`. A set sent this way runs its switching
block too; its `grow` does not run (it grows the whole city): send `{"command":"grow","seconds":40}` — every
standing build is then measured again and re-judged (`rejudged` in the result). The session ends with `-Quit`,
after 15 idle minutes, or at the host timeout (8 h).

## Speed

About 60 s from launch to a ready fixture (plus about 10 s to build), 15 s more for the pre-grow when a scenario asks
for a zoned block, and about 8 s for the look and noise calibration. A one-line scenario takes about 12-25 s (four
settled shots of about 1.1 s each, visibility settling 3 s, the probes under a second). See the latest run's
timeline in summary.md.
