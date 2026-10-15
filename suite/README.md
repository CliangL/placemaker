# Placemaker in-game suite

One game launch: a new game on the vanilla map Plains, the fixture area reshaped in place (water removed, trees and
nets bulldozed, flat / slope / rough ground easing into the map), a grid of vanilla roads (avenues, streets,
pedestrian streets), residential zoning grown on row 1, two plopped buildings that must survive, then many
scenarios one after another. Only Placemaker loads (`-disableModding -disableCodeModding-`), in a 1600x900 window.
The user's settings are saved before the run and put back after it.

```
powershell -File tools/suite/Run-Suite.ps1 -Set core            # a set, then quit
powershell -File tools/suite/Run-Suite.ps1 -Set core -Queue     # a set, then keep the game open for more
powershell -File tools/suite/Run-Suite.ps1 -Set queue           # fixture only, then the queue
powershell -File tools/suite/Run-Suite.ps1 -Set core -Grid 12x8 # a bigger grid (default 9x7 = 63 blocks)
powershell -File tools/suite/Send-Suite.ps1 -File my-scenario.json   # run it in the open session, print the result
powershell -File tools/suite/Send-Suite.ps1 -Quit
powershell -File tools/suite/Compare-Suite.ps1 -Old <runId> -New <runId>
```

Results: `.forensics/suite/runs/<runId>/` — `summary.md`, `summary.json`, `scenarios/NN-<id>.json` (every step with
its waits, timeouts, frame times, issues, shots), `shots/<id>.<name>.png`, `logs/`.

## Scenario files

A set (`suite/scenarios/<name>.json`) is `{"scenarios": [ ... ], "switching": {...}, "grow": true}`. A scenario is
either one line (the default steps: Paint bucket on the block — or Points with `draw: [w, h]` — type and preset,
`values`, entrances where the type asks for them, preview with a shot, build; after the shared grow phase an
oblique and a top-down shot) or a step script:

```json
{ "id": "parking-entrance", "site": "flat", "steps": [ { "do": "area", "tool": "bucket" }, ... ] }
```

* `site`: `flat`, `zoned` (row 1, grown housing on the south street), `slope`, `rough`, `survive` (two plopped
  parks), `any`. Blocks are used once; a scenario that deletes its build gives a flat/slope/rough block back
  (ground restored) for later scenarios once the unused blocks run out.
* `on`: the id of an earlier scenario; the steps run on its block, outline, build and entrances (for "zoom in on the
  entrance of X" after the fact).
* `expand: "presets"` with `layer` or `group` (every preset), `expand: "types"` (every type) — combined with `steps`
  the steps are a template (`type` without `layer` takes the expanded type and preset).
* `seed`: the design seed (default 1).

Points are metres **relative to the block centre** (`[dx, dz]`, +x east, +z north) unless the step says `"abs": true`.

## Steps

Every step can add `"shot": "<name>"` (a PNG after the step), `"camera": {...}` (frame before that shot) and
`"shotMode": "grab" | "render"` (grab = the screen with the outline/ghost overlay, UI hidden — the default while the
tool is open; render = the clean scene). The step's result records `ms`, `waits` (ms per wait: `plan`, `ghost`,
`camera`, `streaming`, `input`, `build`, `delete`…), `timeouts` ("timed out waiting for X after N s" — the step
goes on, nothing is shot early), `frames` (worst / p95 frame time during the step), and `shot`.

| step | example | what it does |
|---|---|---|
| `area` bucket | `{"do":"area","tool":"bucket","at":[0,0]}` | Paint bucket: one click in the block |
| `area` points | `{"do":"area","tool":"points","points":[[-40,-30],[35,-30],[35,30],[-40,30]]}` or `"rect":[44,32]` | Points: a click per corner (snapping as for a person), then the first corner again to close |
| `area` paint | `{"do":"area","tool":"paint","size":40,"stroke":[[-35,-25],[35,-25],[35,25]]}` | Paint area (smart edges): press, drag along the stroke, release; Next. `paintAnything` = smart edges off |
| `area` nodes | `{"do":"area","tool":"nodes"}` or `"nodes":[[-75,-70],[75,-70],[75,70],[-75,70]]` | Road nodes: click the block's four junctions (or the nodes nearest the points), then the first again |
| `clear` | `{"do":"clear"}` | Clear the selection |
| `type` | `{"do":"type","layer":"parking","preset":"compact"}` | Pick the type in the Make row and a preset; waits for the plan |
| `preset` | `{"do":"preset","layer":"plaza","preset":"market"}` | Switch preset (Design) |
| `value` | `{"do":"value","path":"0","id":"aisleWidth","value":7}` | Set an option (`json` for a raw value string) |
| `entrance` | `{"do":"entrance","side":"south","t":0.3}` or `"at":[0,-70]` | "Add at least one entrance": press the button (or `path`/`kind` for the panel), click the outline nearest the point, Esc |
| `entrances` | `{"do":"entrances"}` | While the button asks for entrances: one each on the south, east, north, west street |
| `moveEntrance` | `{"do":"moveEntrance","index":0,"side":"east"}` | In placement mode: right-click the marker (removes), left-click the new spot |
| `removeEntrance` | `{"do":"removeEntrance","index":0}` | Right-click the marker |
| `preview` | `{"do":"preview","shot":"preview"}` | Next to Review; waits for the plan, the ghost emitted and settled; records the review issues/markers |
| `issues` | `{"do":"issues"}` | Records the current issues (severity, key, args, map position, cause) |
| `edit` | `{"do":"edit"}` / `{"do":"edit","build":true}` | Back to Design / Edit the built group (its receipt) |
| `build` | `{"do":"build"}` | Preview if needed, acknowledge permanent pieces, Build; waits for it; census (planned vs live, left out, lots, fences, stalls, nets, zoning, pits, existing buildings) |
| `delete` | `{"do":"delete","restore":true}` | Click the build, Delete; waits until it is gone; records what is left behind; restores the block's ground |
| `restore` | `{"do":"restore"}` | Puts the block's ground back to the fixture's |
| `camera` | `{"do":"camera","target":"entrance","index":0,"view":"closeup","shot":"entrance"}` | Frames the camera and waits until it arrived (see below) |
| `shot` | `{"do":"shot","name":"now","shotMode":"render"}` | A PNG of the current view |
| `wait` | `{"do":"wait","seconds":2}` / `{"do":"wait","frames":30}` | Plain wait (for watching a simulation, not for readiness) |
| `grow` | `{"do":"grow","seconds":40}` | Simulation at speed 4 with fast spawn; records growables and the road each one faces |
| `undo` / `redo` | `{"do":"undo"}` | Ctrl+Z / Ctrl+Y |
| `anarchy` | `{"do":"anarchy","on":true}` | The panel's anarchy switch |

## Camera

`{"do":"camera", "target": ..., "view": ..., "yaw": deg, "pitch": deg, "distance": m}`

* `target`: `block` (default), `area` / `build` (the outline's bounding box), `entrance` (`index`: the n-th entrance
  the script placed, else the design's markers), `issue` (`index`: the n-th issue with a map position), `point`
  (`at: [dx, dz]`, `abs`), `overview` (the whole grid).
* `view`: `oblique` (yaw 25, pitch 55, 3.4 x the target radius), `top` (pitch 89.5), `closeup` (yaw 30, pitch 38,
  ~45 m; the default for entrance/issue/point), `low` (pitch 18). `yaw`, `pitch`, `distance` override.
* Arrival = the controller holds the pivot and the rendered camera has not moved for 5 frames; then before a shot
  the texture streaming must be quiet for 6 frames. Both are recorded (`waits.camera`, `waits.streaming`,
  `cameraState`, `cameraDistance`).

## Live queue

`Run-Suite.ps1 -Queue` keeps the game open after the set: the runner polls `<run>/inbox/*.json` (a scenario, a set
`{"scenarios":[...]}`, `{"command":"grow","seconds":30}` or `{"command":"quit"}`), runs each file, writes
`<file>.result.json` beside it (status, failed checks, seconds, waits, PNG paths, result file) and moves the file to
`inbox/done/`. `Send-Suite.ps1` does all of that and prints the result. Same DLL: a code change needs a relaunch
(~40 s to a ready fixture). The session ends on quit, after 60 idle minutes, or at the host timeout (8 h).

Example — zoom in on the entrance of a scenario that already ran:

```json
{ "id": "zoom", "on": "parking-entrance", "steps": [
  { "do": "camera", "target": "entrance", "index": 0, "view": "closeup", "distance": 30, "shot": "entrance", "shotMode": "render" } ] }
```

## Checks (verdicts per scenario)

`built`, every failed step, `noTimeouts`, `nothingLeftOut`, `noCropOrVanillaLots` (non-farm types), `noForeignFences`,
`stallsBuilt`, `stallsOnLanes` (stall > 9 m from any car lane), `netsConnected`, `zoningLive`, `existingSurvive`,
`noPits` (ground below both the old ground and the plan near roads), `growablesFaceOwnRoad` (after a grow),
`noErrors` (Placemaker errors/exceptions in the log during the scenario), `noHang` (a frame over 1 s in any step),
`deleteLeavesNothing`.
