# Placemaker game runner

## See builds: `Show.ps1`

```
powershell -File tools/suite/Show.ps1                                          # the categories, types and styles
powershell -File tools/suite/Show.ps1 -Category parks -Type park -Style formal # the usual: 2 pictures
powershell -File tools/suite/Show.ps1 -Category parking -Type parking -Style all -Views top -Modes clean,zoning
powershell -File tools/suite/Show.ps1 -Category shops -Type all -NoPlan
```

One launch for everything asked (about 70 s, then about 10 s per build; the game must be closed). Each build gets its
own flat test block and is built the way a person does (bucket the block, pick type and style, entrances, Review,
Build). It prints, per build, its pictures and its lines in `Placemaker.log`; `Player.log` has the game's own errors.

**Start with the default two pictures**: `plan` (Review before building: the outline and the ghost) and `45-clean`
(the finished build at 45°). Ask for more only when those did not show what you needed; don't dump pictures.

* `-Type`, `-Style`: one or more (`a,b`), or `all` (with `-Category` for types). Every type x every style is built.
* `-Views`: `45` (default), `top`, `low`, `close`. Framed on the build (`close` looks at its middle).
* `-Modes`: `clean` (default, no overlays), `zoning` (the game's zone grid: zoned cells in their zone colour, the way
  the road and zoning tools show it), `overlay` (Placemaker's and the game's overlays), `ui` (the real screen),
  `terrain` (a map of the ground's height from above, once per build: blue below the outline's typical ground, red
  above, shaded by slope, contour lines every 0.5 m and darker every 2 m, the outline in white; only the ground, no
  buildings, trees or roads. Flat ground is a pale area without lines; pits and mounds are rings. Show.ps1 prints
  the lowest and highest ground in metres).
* Every view x every mode is taken after the build (`<view>-<mode>.png`); `-NoPlan` skips the plan picture.
* `-Site slope|rough|zoned|survive` for a block other than flat.

Anything else (edits, deletes, entrances in given places, growth, several steps) is a scenario file you write
yourself, below.

## What it is

A simple tool: it launches the real game, does the steps you write, the way a person would, takes the pictures you ask
for in the order you ask, and collects the logs. It judges nothing. There are no PASS / FAIL checks; **you read the
logs** and look at the pictures.

* **The logs are the point.** After a run, the run folder has:
  * `logs/Placemaker.log`: everything Placemaker did, including plans, the build audit, left-out pieces, zoning and
    errors.
  * `logs/Player.log`: the game's own errors and exceptions.
  * `suite.log`: what the runner did, one line per step, with timings (step time, worst frame, plan time) and the
    picture each step took.
  * `audit/`: Placemaker's build audits written during the run.
* **Pictures come second**, in order: `contact.html` (every shot, captioned) and `shots/`. Use them for an artistic
  look, or to catch what the logs don't say: things built but never drawn, a fence across an entrance, a pit, shops
  facing the wrong street.
* `summary.md` says what ran (one row per scenario: status, build path, time, shots) and lists any step the runner
  could not do (a click that changed nothing, a button that was greyed out). `scenarios/NN-<id>.json` has every step's
  record.

## Default use: one 45° picture per build, many builds per launch

The most useful picture is the finished build seen from 45° (camera south of the build, noon sun behind it). Queue as
many builds as you like into one launch. Each gets its own block and one final 45° shot.

* **Every preset of one type:** `suite/scenarios/look.json`. Change `"layer"` to the type you want, or replace it with
  `"group"` for a whole group. Groups are the types' own (`parks`, `gardens`, `water`, `commercial`, `squares`… as in
  `suite/catalog.json`), not the folded categories of the Make row; hidden types (industry, transport) can still be
  named here.
* **One specific type and preset:** `suite/scenarios/look-one.json`. Change `layer` and `preset`.
* Type, preset (style) and option ids for this build are listed in `suite/catalog.json`, written by every run.

```
powershell -File tools/suite/Run-Suite.ps1 -Set look        # launch, build them all, one 45° shot each, quit
powershell -File tools/suite/Run-Suite.ps1 -Set look-one
powershell -File tools/suite/Play.ps1                       # the user's playground: the flat grid, controls handed over
```

Or keep the game open and feed it builds one file at a time. This needs no relaunch for a new list; a code change
always needs one:

```
powershell -File tools/suite/Run-Suite.ps1 -Set queue -Queue
powershell -File tools/suite/Send-Suite.ps1 -File my-builds.json
powershell -File tools/suite/Send-Suite.ps1 -Json '{"command":"grow","seconds":40}'
powershell -File tools/suite/Send-Suite.ps1 -Quit
```

Send-Suite prints each scenario's status, any step the runner could not do, its pictures and where the logs are. Exit
code 0 = done, 1 = the runner reported an error or timed out, 2 = schema error, 4 = the game went away.

Timings and limits:
* About 60 s from launch to a ready test world, then about 5–12 s per build.
* The default grid holds about 75 flat blocks; a block is used once. For more, split by `group`, run several launches,
  or pass a bigger `-Grid`.
* Water and slope types look odd on flat blocks; use `"site": "slope"` where that matters.
* Commercial types show empty shop plots unless you add `{"do":"grow","seconds":40}` before the camera step. That
  grows the whole city.

If a run is cut off, its logs are not copied into the run folder. Read them in
`%USERPROFILE%\AppData\LocalLow\Colossal Order\Cities Skylines II\Logs\` before the next launch overwrites them. Then
run `Run-Suite.ps1 -RestoreOnly` so the user's game settings are put back.

Only one game at a time: Run-Suite takes `.forensics/suite/lock`, and stops when a Cities2.exe it did not start is
running (a runner game it left behind is asked to quit through its inbox; the user's own game is never touched).

Other Run-Suite switches: `-AllMods` (the user's whole playset; slow boot), `-UserModSettings` (keep the user's
Placemaker options), `-Look game` (the map's own light and weather), `-Pace settled|user`, `-Grid 20x9`,
`-MaxScenarios N`, `-NoBuild`, `-RestoreOnly` (put the user's settings back after a cut-off run).

Sets in the repo: `look`, `look-one` (above), `smoke` (4 quick builds), `core` (every parking and commercial style,
plazas, parks, roads, both build paths, user pace), `full` (every type), `steps` (step scripts of every tool),
`examples` (the scenarios in `suite/scenarios/examples/`). Any file works with `-Set path\to\set.json`.

# Reference

One game launch: a new game on the vanilla map Plains, the fixture area reshaped in place (water removed, trees and
nets bulldozed, flat / slope / rough ground easing into the map), a grid of vanilla roads, low-rise housing grown on
the zoned row, two plopped parks, then the scenarios, each on its own block. Every action is done the way a person does
it, and every picture is taken only once the scene is still.

What a run does to the game, so you know what the pictures are not:
* **Clean profile**: only Placemaker loads (`-disableModding`); no asset packs, no other code mods. Decals, surfaces,
  fountains/statues and park dressing differ from the user's game. `Run-Suite -AllMods` loads the user's playset.
* **Readability mode**: the light, weather and icons are the runner's, not the user's (see Look).
* Placemaker's options are **pinned to its defaults** for the run (summary.json `placemakerSettings`, with every option
  that differs from the user's); `-UserModSettings` keeps the user's. The user's settings and ModsData/Placemaker
  files are saved before the run and put back after it.
* Money is unlimited and everything is unlocked.

## Scenario files

The vocabulary is in `suite/schema.json`. Unknown keys, steps, targets and values are refused before anything runs
(Send-Suite checks a file before sending it; the game checks every set and step).

A set (`suite/scenarios/<name>.json`, or any file with `-Set path\to\set.json`) is
`{"scenarios": [...], "settings": {"Option": value}}`.

| key | meaning |
|---|---|
| `id` | the scenario's name (results and PNGs are named after it) |
| `layer`, `preset` | a one-line scenario: bucket the block (or `draw` a rectangle), pick the type and style, entrances where the button asks, preview (an overlay shot and a clean ghost shot), build, built oblique and top shots |
| `draw` | `[w, h]` metres: the Points tool draws this rectangle at the block centre instead of the bucket |
| `values` | `[{"path":"0","id":"aisleWidth","json":"7"}]`: options set before the preview |
| `steps` | a step script (below) instead of the one-line steps |
| `expand` | `"presets"` (each style of `layer` or `group`; `limit` = the first N styles of each type, `skip` = style ids) or `"types"` (every type in the Make row); with `steps` the steps are a template (`type` without `layer` takes the expanded type and style). Ids become `<layer>-<preset>` (`-small` with `draw`), a duplicate gets `+` |
| `site` | `flat`, `zoned` (row 1: low-rise housing on its south street, grown before the first zoned scenario), `slope`, `rough`, `survive` (two plopped parks), `any`. **Never replaced by another kind**: when none is left the scenario is not run (`no-block`) unless `siteFallback: true` |
| `block`, `streets` | a given block (`"B3-0"`), or a street signature (`{"south":"Medium Road","north":"Small Road"}`) |
| `on` | runs on an earlier scenario's standing build: camera, shot, select, delete, bulldoze, infoEdit, infoRemove, wait, grow; any design step needs `{"do":"edit","build":true}` first. Refused when that build was deleted or never built — so put it before the delete |
| `seed` | design seed (default 1). A type pick starts a fresh design with this seed (as after opening the game) unless the step says `"fresh": false` (the UI's next build of the same type keeps the design) |
| `pace` | `human` (default: the cursor glides to every click, clicks at least 0.4 s apart, sliders dragged, brush strokes at 30 m/s as at 60 Hz), `settled` (no glide, fast strokes), `user` (no waiting for "Planning…": presses on) |
| `buildWhen` | one-liners: `settled` (Build once the ghost is shown: the instant path), `planReady` (Next then Build at once: the build service plans it), `ghostPending` |
| `previewMode` | one-liners: `sketch` sets Review to Sketch before building |
| `note` | free text |

Keys starting with `_` are comments.

## Steps

Every step can add `"shot": "<name>"` (a settled PNG after the step), `"camera": {...}` (the framing of that shot;
without one a build/preview/delete/edit/bulldoze/area step frames the build, oblique), `"shotMode"` and `"zones": true`
(the game's zone grid in the picture, as the road and zoning tools show it).

Panel actions click the panel's real element when it is on the screen (recorded per action in `ui`: `click`), else
fire the panel's own trigger through the UI view (`trigger`), and only when the UI view does not answer call the tool
(`direct`). A greyed-out button is pressed like a person would; when that does nothing the step says so (no trigger
behind its back). Design controls (type, style, options, entrances) live in Design: a script in Review goes back to
Design first, recorded under `implicit`. When an action changed nothing, the step's record and `suite.log` say why;
after a failed `area`, `type`, `preview` or `build` the scenario's remaining steps are skipped.

| step | keys (defaults) | what a person does |
|---|---|---|
| `area` | `tool`: `bucket` (default), `points`/`draw`, `paint` (smart edges), `paintAnything`, `nodes`; `at` `[dx,dz]`; `points` `[[dx,dz],…]` or `rect` `[w,h]`; `stroke` `[[dx,dz],…]` with `size` (m, default 45 % of the block), `square`, `angle`; `nodes` `[[dx,dz],…]` (default the four junctions); `abs`; `cameraDistance` (320) | looks at the block from 320 m, picks the area mode, then: one click (bucket); a click per corner and the first again (points); a brush drag (paint); a click per junction and the first again (nodes). Paint ends with Next |
| `clear` | – | Clear (in Area) |
| `type` | `layer`, `preset` (style), `fresh` (true) | the type tile of the Make row, then the style chip; refuses a type the Make row does not offer; lists the valid ids on a typo |
| `preset` | `layer` (the current type), `preset` | the style chip (Design) |
| `value` | `path` ("0"), `id`, `value` or `json` | a slider is dragged (previewValue each frame, then setValue and endPreview, clamped and snapped to the control's range); a picker hovered then clicked; a toggle or choice set; refused when the panel greys the control out; the value is read back |
| `entrance` | `side` (`south`/`east`/`north`/`west`, `t` 0.5 along it west→east or south→north) or `at`; `path`, `kind` | presses "Add an entrance" (or picks the marker kind), clicks the outline at the point, Esc. The tool's own (snapped) marker is recorded |
| `entrances` | – | while the button asks for entrances: one on the south street, then east, north, west |
| `moveEntrance` | `index` (0: the script's n-th entrance, matched by position), `side`/`t`/`at` | in placement mode: right-click the marker, left-click the new spot, Esc |
| `removeEntrance` | `index` | right-click the marker |
| `preview` | – | Next to Review; waits for the plan and the ghost; records what the Review panel lists |
| `previewMode` | `mode`: `sketch`/`ghost` | the preview switch |
| `edit` | `build` (false) | back to Design; with `build: true`: clicks the build (Manage) and Edit |
| `build` | `when` (`settled`; `planReady`; `ghostPending`), `via` (`panel`; `enter`) | (Next if needed,) OK for permanent changes, Build; waits until it is done and the game has no temporaries left; records the build path (`instant` from the ghost or `start`) |
| `select` | – | the Manage stage, the game's raycast click on the build |
| `delete` | – | select, Remove, OK; waits until the build is gone |
| `bulldoze` | `target`: `owner` (the owner lot), `part` | the bulldozer's delete definition on that member (applied through the runner's fixture tool, not the bulldozer UI), then the group's own cascade |
| `infoEdit` / `infoRemove` | – | the Placemaker tool closed, the build selected the vanilla way, its info panel's Edit / Remove |
| `camera` | `target`, `view`, `index` (or `"all"`), `yaw`, `pitch`, `distance`, `fov`, `at`, `abs` | frames the camera (below); a target that does not exist is an error and no picture is taken |
| `shot` | `name`, `shotMode`, `labels` (false: street names stay off) | a settled PNG of the current view |
| `terrain` | `name` (`terrain`), `margin` (20 m) | not a camera picture: a map of the ground's height over the outline and the margin, from the game's height data (see `-Modes terrain` above); the step's record has the typical ground level and the lowest and highest ground |
| `wait` | `seconds` (1) or `frames` | a plain wait |
| `grow` | `seconds` (30), `fast` (true) | the simulation with debug fast spawn at speed 4 (`fast: false` = speed 1 with real demand) until the building count is still for 8 s; grows the whole city |
| `undo` / `redo` | `via`: `key` (Ctrl+Z / Ctrl+Y, default), `panel` | says so when there is nothing to undo or redo |
| `key` | `key`: `enter`, `esc`, `undo`, `redo` | the key, through the tool's own key handling |
| `anarchy` | `on` | the panel's anarchy switch (not there in a clean run: the Anarchy mod is not loaded) |
| `setting` | `name`, `value` | a Placemaker option (e.g. `IgnoreOwnCollisions`), read back |

World clicks: the point is projected to the screen; off the screen or under the panel the camera pans to it like a
person (recorded as `pan`), else the step says "not clickable". The game's own raycast with the tool's masks decides
what is hit (recorded `hit`, `hitEntity`, `hitOffM`), the cursor glides there, then clicks. Between steps the cursor
is over the panel (no hover on the map).

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
it; the grid is made wide enough for the set (summary.json `capacity`, `blocksNeeded`).

## Camera

`target`: `block` (default), `area`/`build` (the outline's bounding box), `entrance` (`index`: the n-th entrance
the script placed), `issue` (the n-th issue the plan marks on the map), `point` (`at`, `abs`), `overview`.
`index: "all"` frames every one, shots named `<shot>-<n>`.

`view`: `oblique` (yaw 25, pitch 60; on `build`/`area` the camera comes as close as it can with the whole outline, and
12 m above it, in the picture; elsewhere 3.4 x the target's radius; `distance` overrides), `top` (yaw 0, pitch 89.5, field of view 18°:
nearly orthographic), `closeup` (yaw 30, pitch 38, 45-60 m), `low` (pitch 20). The camera sits south of its target
and looks north; the noon sun is behind it. The game lifts the eye over terrain and clamps tilt: the rendered pitch
and distance are read back and the request corrected (up to 3 passes).

## Shots

* `shotMode`: `render` (the clean scene: no outline, no icons, no street names), `overlay` (with Placemaker's outline,
  ghost symbols and markers; the default while the tool is open outside Manage), `ui` (the real screen with the
  panel). All modes have the window's size (1600x900), so pairs line up pixel for pixel.
* A picture waits until the scene is still: a short floor after the last event (camera move, ghost, build, delete,
  grow), the engine quiet (camera still, no meshes or textures loading, full level of detail, nothing fading), then
  two pairs of captures that barely differ. A scene that is still moving after 10 s is taken anyway and its caption
  says so.
* Names: `shots/<NN>-<id>.<step>-<name>.png` (NN = the scenario's number in this run); never overwritten.

## Look (readability mode; `-Look game` turns it off)

Noon (12:00 local solar time) on day 172 at latitude 45° N (the Plains map is at 44° S, where June is midwinter):
the sun about 68° up, due south. A fixed exposure calibrated at the start of the run, indirect light x1.3, wind 0. No
rain, clouds, fog, aurora or snow. Notification icons off (tool-error icons stay), street names off, full level of
detail without LOD cross-fade, no edge scrolling. The fixture's own growth is low-rise housing; tall buildings in a
shot come from the types being built. Everything is recorded in summary.json `look`.

## Results

`runs/<runId>/`: `logs/`, `suite.log`, `summary.md`, `summary.json`, `contact.html` (and `contact.jpg`), `shots/`,
`scenarios/NN-<id>.json` (each step with its waits, frame times, presses, UI path), `audit/`, `host.log`,
`catalog.json` (also copied to `suite/catalog.json`), `dirty.patch` when the tree was dirty. The run is labelled with
the git hash and the deployed DLL's hash; the game checks it loaded that DLL ("wrong build" stops the batch).

If the game is closed early the quit hook writes what is known, marked incomplete; the host says how it ended
(`quit`, `closed-externally`, `crashed`, `killed`, `hung`, `boot-timeout`) at the top of summary.md.

## Live queue

`Run-Suite -Queue` keeps the game open (after the set, or right after the fixture with `-Set queue`). Send-Suite drops
a file into `<run>/inbox`; the result comes back as `<file>.result.json`. `{"command":"grow","seconds":40}` grows the
whole city. The session ends with `-Quit`, after 15 idle minutes, or at the host timeout (8 h).
