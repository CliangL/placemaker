# Parking layer (`parking`) — notes and proposals

Owner: parking workstream. Code: `Core/Layers/Parking/`. Engine ported from ParkingLotTool
(KruemelGames, GPL-3.0); file headers say which PLT file each part comes from.

## What the layer emits

| Plan part | Content | Game layer |
|---|---|---|
| `Surfaces` | asphalt (`SurfaceRole.Asphalt`, default `Pavement Surface 01`) and green (`SurfaceRole.Grass`, default `Grass Surface 01`). Every ring went through `SurfaceAcceptance.MakeAcceptable`; when the game model trims a big ring, the same cells are regrouped into smaller rings (48/16 edges) or emitted cell by cell — whichever covers more. | surface areas, owned per gamebuild.md |
| `Areas` | one `AreaRole.Lot` ring = the cleaned outline (largest accepted piece) | selectable owner (RESEARCH-NOTES 7) |
| `Nets` | `DriveAisle` = ring lane, aisles, cross lanes; `DriveConnector` = entrance lanes; `Alley` = outer piece of an entrance in *junction* style (`StartAttach`/`EndAttach` = road `ExternalId`, `t`); `Pathway` = footpaths beside one-way entrances. All straight Béziers; **every junction is split** (an endpoint lying on another course is always one of its ends), endpoints closer than 0.75 m are welded, no course < 0.5 m. | invisible drivable paths |
| `Objects` | `ParkingBay` per stall (centre, yaw), `Charger`, `Lamp`, `Tree`/`Bush`, `Arrow` (one-way entrances only) | |
| `Regions` | `lot:N` {lot} = asphalt rings, `green:N` {green} = planted rings | add-ons |
| `Facts` | `capacity`, `evCapacity`, `disabledCapacity`, `parking.fee` | economy companion |
| `Stats` | `Placemaker.Parking.Stat.{Stalls,EvStalls,AccessibleStalls,Area,AisleLength,Trees}` | summary card |

### Conventions the game layer must follow (from PLT's measured build)

* **Bays.** Object at the stall centre, terrain height. Yaw is radians around +Y with 0 = +Z, as
  `quaternion.RotateY(yaw)`; **+Z points from the aisle to the back of the stall** — PLT
  `ParkingLotBayObjects.cs` measured `BayDecalFacesAisle = false` (the opposite was 180° wrong).
  Assets: `ParkingLotDecal01`, `ParkingLotDisabledDecal01`, `ParkingLotElectricDecal01` (vanilla
  built-ins; their `SubLane` spawns "Invisible Parking Lane - Perpendicular 2.9x5.9", RESEARCH-NOTES 16).
  Accessible stalls are 5.0 m wide (decal 5.0 x 6.2, lane 4.7 x 5.9). Decals must be owned by a bare
  carrier whose `SubNet` holds the aisles (gamebuild.md), else stalls do not link to lanes.
  Stall width/length sliders change the layout only; the decal keeps its size (scale is 1).
* **Drive paths.** `Asset.Id` names the vanilla path whose drivable core fits the width (PLT
  `ParkingLotFahrregeln.cs`): 3 m `Invisible Car Path - 1xTwoway`, 4 m `Invisible Road Path - 1xTwoway`,
  6 m `Invisible Car Path - 2xTwoway`, 7 m `Invisible Road Path - 2xTwoway`; one-way
  `Invisible Road Path - 1xOneway`. Never `... 2xPerpendicular` (its own parking lanes would double the
  stalls). PLT built runtime clones ("PLT " + name: 25 km/h, CarPathfind `m_DrivingCost`
  (0, 0.25, 0.01, 0.05), UI/placeholder components stripped).
* **One-way.** For `DriveConnector` with `Invisible Road Path - 1xOneway`, **A→B is the driving
  direction** (entry: street → lot, exit: lot → street).
* **Entrance style "connect" (default).** The connector runs from the junction inside the lot to the
  **kerb** of the adjacent road (centre distance − width/2), leaving a free dead end for
  LocalConnect (RESEARCH-NOTES 15; PLT: `PathwayPrefab` gets `Layer.Road`, `m_SearchDistance = 4`,
  roads get `m_LocalConnectLayers |= Pathway | MarkerPathway`, `LocalConnectFlags.RequireDeadend`,
  connect range = road/2 + path/2 + 4 m). No road found within 40 m → issue `EntranceNoRoad`.
* **Entrance style "alley".** Inner `DriveConnector` (junction → outline) plus an `Alley` from the road
  centreline to the outline with the road end attached (`CoursePos.m_Entity = edge`,
  `m_SplitPosition = t`, RESEARCH-NOTES 18; within 0.5 m of an edge end PLT snapped to the node).
  PLT cloned the vanilla `Alley` (never `Alley Oneway`, which has 2.5 m parking strips), set
  `ZoneBlock = null`, forced `FlattenTerrain | ClipTerrain`, and added `RemoveCrosswalk` on both sides.
* **Chargers** `ParkingLotCharger01` 0.6 m behind the middle of an EV pair's back edge, facing the
  aisle. Pairs are only chosen where that spot is planting (not on a stall or lane).
* **Lamps** default `StreetlightDouble02` between back-to-back rows, `StreetlightSingle02` along
  outer row backs (PLT set "strasse"); a double head's yaw is across the rows.
* **Arrows** `RoadArrow Forward Placeholder` → `NA RoadArrow Forward` → `EU RoadArrow Forward`,
  at the middle of a one-way connector, yaw = driving direction.

## Markers

Kinds `entrance` (two-way), `entry`, `exit`. `CheckReady` is ready with ≥ 1 `entrance` or ≥ 1 `entry`
and ≥ 1 `exit`; otherwise `NextStepKey = Placemaker.Parking.Step.PlaceEntrance`.
`SuggestMarkers` returns 1–2 `entrance` markers on the best adjacent roads (close, minor road, two-way,
square approach, away from junctions and corners; a second one ≥ max(40 m, 15 % of the perimeter)
away for lots > 6000 m² or perimeters > 320 m). They satisfy `CheckReady`, so the tool can auto-apply
them. `Plan` also falls back to the suggestions when no marker is placed (issue `EntrancesSuggested`),
so the preview is complete before the player places anything. Markers are matched to the outline by
`Position` (the layer cleans the ring, so segment indices may shift).

## Proposals (contract changes, not applied)

1. **`PlannedNet.OneWay` (bool).** Today one-way is implied by the asset id
   (`Invisible Road Path - 1xOneway`) plus A→B order. An explicit flag would let the game layer pick
   its own one-way clone.
2. **Non-additive facts.** `BuildService.FinishRecord` sums facts over layers. That is right for
   `capacity`, wrong for `parking.fee` when a recipe has two parking steps. Proposal: facts whose key
   ends in `.fee` (or a `FactMerge` hint) take the maximum.
3. **`VisibleWhen` with `!=`** (e.g. `greenery!=none` for the planting controls). Until then those
   controls are always visible.
4. **`PlannedObject.Size` (float2, optional).** Stall width/length and accessible stall width
   (5.0 m) are layout facts the game layer could use to pick or scale decals.

## Recipes

`Recipes/parking.json` (standard), `parking-green.json` (60°, wide planting), `parking-compact.json`
(edges only, no row-end planting, no cross lanes) — format per `docs/proposals/recipe-json.md`
(settings are overrides on `Defaults()`).

## Ported vs dropped

Ported (translated, restructured): row frame from the longest straight run (`LaengsteGerade`),
inset with edge correspondence + erosion for narrow arms (`Innenrand`/`ErodierterRing`), band plan of
modules, cross-lane placement every N stalls with end sections ≥ min(5, N) (`Querstrassenmitten`),
perimeter row with entrance gaps (`Randreihe`), inner perimeter stalls, ring-less layout with
connecting lanes (`Ringlos` idea), special stalls (`FindDisabledGroups`, EV pairs), charger spot,
lanterns (`ParkingLanterns`), vegetation (`ParkingVegetation`), path catalogue (`Fahrregeln`), the
cell idea for surfaces (material as a label per cell, shared points, no repair).
Dropped: zoning (edge zoning, zoning areas, zoning roads), bus stops, utility routing (`Versorgung*`),
background sync (`Hintergrund*`), diagnostics/live log/reject logs, hand-drawn sub-area cuts and
per-sub-area angles, material switches and the dead material-surface repair path (~3.8k lines),
`Cs2Triangulierung` (decompiled; replaced by `Core/Geometry/SurfaceAcceptance`).
