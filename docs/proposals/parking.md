# Parking layer (`parking`) — notes and proposals

Owner: parking workstream. Code: `Core/Layers/Parking/`. Engine ported from ParkingLotTool
(KruemelGames, GPL-3.0); file headers say which PLT file each part comes from.

## What the layer emits

| Plan part | Content | Game layer |
|---|---|---|
| `Surfaces` | asphalt (`SurfaceRole.Asphalt`, default `Pavement Surface 01`) and green (`SurfaceRole.Grass`, default `Grass Surface 01`). Every ring went through `SurfaceAcceptance.MakeAcceptable`; when the game model trims a big ring, the same cells are regrouped into smaller rings (48/16 edges) or emitted cell by cell — whichever covers more. | surface areas, owned per gamebuild.md |
| `Areas` | one `AreaRole.Lot` ring = the cleaned outline minus zoning bands (largest accepted piece) | selectable owner (RESEARCH-NOTES 7) |
| `Zoning` | `PlannedZoning` quads along the street frontage (see Zoning) | zone the street's existing cells |
| `Nets` | `DriveAisle` = ring lane, aisles, cross lanes; `DriveConnector` = entrance lanes; `Alley` = outer piece of an alley-kind entrance (`StartAttach`/`EndAttach` = road `ExternalId`, `t`); `Pathway` = footpaths beside one-way entrances and footpath entrances (outer piece `EndAttach` = road). All straight Béziers; **every junction is split** (an endpoint lying on another course is always one of its ends), endpoints closer than 0.75 m are welded, no course < 0.5 m. | invisible drivable paths |
| `Objects` | `ParkingBay` per stall (centre, yaw), `Charger`, `Lamp`, `Tree`/`Bush`, `Arrow` (one-way entrances only), `Marker` with asset `Placemaker:BusStop` (bus stops) | |
| `Regions` | `lot:N` {lot} = asphalt rings, `green:N` {green} = planted rings | add-ons |
| `Facts` | `capacity`, `evCapacity`, `disabledCapacity`, `parking.fee`, `zoning.cells` | economy companion |
| `Stats` | `Placemaker.Parking.Stat.{Stalls,EvStalls,AccessibleStalls,Area,AisleLength,Trees,ZoneCells}` | summary card |

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

## Markers (round 2 — PLT entrance parity)

One Markers control (`entrances`, group "entrances", first in the panel) lists every kind in display
order in `ControlSchema.MarkerKinds`, with one `ControlOption` per kind: label
`Placemaker.Parking.Marker.<Kind>` + `.Tip` (Kind in PascalCase, as `Core/Tool/MarkerKinds.Pascal`
builds it), icon from `MarkerKinds.IconFor` (one-way alleys reuse the alley icon; bus stop and zoning
patch use the bundled glyphs `people` / `zone` until the tool has vanilla ones). Markers are matched to
the outline by `Position` (the layer cleans the ring, so segment indices may shift).

| Kind | PLT | Counts for Build | Emits |
|---|---|---|---|
| `entrance` | Zufahrt | two-way | `DriveConnector` junction → kerb (LocalConnect dead end) |
| `entry` / `exit` | Einfahrt / Ausfahrt | way in / way out | one-way `DriveConnector` (A→B = driving direction) + `Arrow` + side `Pathway` |
| `alley` | Gasse | two-way | inner `DriveConnector` + `Alley` (asset `Alley`) from the road centreline to the outline, road end attached |
| `alleyIn` / `alleyOut` | GasseEin / GasseAus | way in / way out | as alley, asset `Alley Oneway` (fallback `Alley`), A→B = driving direction, `Arrow` |
| `footpath` | Fussweg | never (people only) | inner `Pathway` (junction → outline) + outer `Pathway` outline → road centreline, `EndAttach` = road |
| `busStop` | Bushalt (changed) | — | `PlannedObject` Role `Marker`, Asset `Placemaker:BusStop`, on the lot-side sidewalk (road width/2 − 1.5 m from the centre), yaw = travel direction of the kerb lane (left-hand traffic aware) |
| `zoningPatch` | Zoningflaeche (changed) | — | a zoning band `zoningPatchWidth` cells wide centred on the marker (see Zoning) |

* **Readiness** (PLT `FehlenderZugang`, decided by direction, not build type): ready with ≥ 1 two-way
  kind (`entrance`, `alley`) or ≥ 1 inbound (`entry`, `alleyIn`) plus ≥ 1 outbound (`exit`, `alleyOut`).
  Otherwise `NextStepKey = Placemaker.Parking.Step.PlaceEntrance`, reason `Reason.NeedsEntrance` /
  `EntryWithoutExit` / `ExitWithoutEntry`. **Minimum required = 1 vehicle entrance**
  (`ParkingIds.MinVehicleEntrances`); `Readiness.ReasonArgs = [1, vehicleEntrancesPlaced]`, ready or
  not, until the schema has a field (proposal 6).
* **Suggestions** (`SuggestMarkers`) are applied by the tool as real, editable markers when the step has
  none. Small lots: one two-way `entrance` at the best spot (close, minor two-way road, square approach,
  away from junctions and corners). Large lots (> 6000 m² or perimeter > 320 m) with a second spot
  ≥ max(40 m, 15 % of the perimeter) away on the **same street**: `entry` + `exit`; otherwise a second
  two-way entrance on another street. Alleys are never suggested (they change the street).
* **The plan never invents entrances** (round-1 fallback removed): without a vehicle entrance it warns
  `Issue.NoEntrance` and the lot still previews, without connectors.
* At most 15 entrance markers (PLT `MaxEntranceCount`, all kinds share the quota); extras are dropped
  with `Issue.TooManyEntrances`.
* Legacy `entranceStyle = "alley"` (round-1 save format) is still read and turns entrance/entry/exit
  into alley/alleyIn/alleyOut; the control is no longer in the schema.

### What the game layer must emit for the new kinds

* **Alley / alleyIn / alleyOut**: as round 1 (`CoursePos.m_Entity` = edge, `m_SplitPosition` = t; snap
  to the node within 0.5 m of an edge end). PLT built clones named `"PLT Zufahrtsgasse…"`
  (`Tools/ParkingLotGassenPrefab.cs`) from vanilla `Alley` (two-way) and `Alley Oneway` (one-way,
  2026-09-18), with `ZoneBlock = null`, `FlattenTerrain | ClipTerrain` and `RemoveCrosswalk` on both sides.
* **Footpath**: inner piece = vanilla `Invisible Pedestrian Path`; PLT used the clone
  `"PLT Invisible Pedestrian Path"` with LocalConnect search mask 0 (no automatic extension) and, for
  the street-side piece, `"PLT Pedestrian Entrance Path"` (LocalConnect `RequireDeadend`)
  (`Tools/ParkingLotFusswegPrefab.cs`, `Geometry/FusswegAnschluss.cs`). The outer `Pathway` carries
  `EndAttach` = road `ExternalId` / `EndAttachT` and ends on the road centreline: either split the road
  there (what vanilla does when a path is drawn onto a road), or — PLT's choice, no road split — stop it
  at the kerb (road width / 2 from the centre) and let the LocalConnect clone join the sidewalk.
* **Bus stop**: resolve `Placemaker:BusStop` like PLT `ParkingLotBusStops.ResolveBusStopPrefab`: a
  built-in prefab with bus `TransportStopData` (`m_PassengerTransport`), `RouteConnectionData` of type
  Road, name not containing "Integrated", preferring one with "Sign" (theme-dependent). Place it at
  `Position` connected to the nearest road lane on that side. If it cannot be resolved, skip it (do not
  place an invisible marker). Proposal 5 replaces the sentinel with `ObjectRole.BusStop`.

## Zoning ("place buildings")

Controls (group "zoning", heading "Buildings"): `edgeZoning` none / oneSide / all, `zoneType` (Asset,
category `zone`, "" = the game's default zone), `zoningDepth` 1–6 cells (CS2 `BlockSystem` hard-codes a
block depth of 6), `zoningPatchWidth` 1–10 cells, plus the `zoningPatch` marker.

**Redesign (changed from PLT).** PLT built its own 8 m zoning road (`Zoningstrasse` setting, default the
vanilla `Alley`, cloned with zone blocks) around interior rectangles (`Zoningflaeche`) or along a chosen
outline edge (`Randzoning`), and had to weld that road into the drive network (`Zoningknoten`,
`ZoningRandanschluss`, `RinglosZoning*`, `ZoningMuendung`, `ZoningGassenabstand`), which caused most of
PLT's late bugs. Placemaker zones **along an existing street** instead. CS2 already has zone blocks 6
cells deep there; the lot gives up a band and tells the game which cells to zone. No new road, no
junctions. Code: `Core/Layers/Parking/Zoning.cs`.

* Frontage = stretches of an outline edge whose outward ray (≤ 40 m) hits a street running alongside
  (|cos| ≥ 0.9), at least 16 m long. `oneSide` = the longest stretch, `all` = every stretch. A patch
  marker zones `zoningPatchWidth` × 8 m of the stretch it sits on (`Issue.ZoningPatchNoStreet` otherwise).
* Band depth inside the lot = `zoningDepth` × 8 m − gap (outline to street edge), so plots are exactly
  `zoningDepth` cells deep from the street. A band under 4 m gives `Issue.ZoningStreetTooFar`. No
  frontage at all gives `Issue.ZoningNoStreet`.
* Lot = outline − bands (`Poly.Boolean`). The layout, surfaces and the `AreaRole.Lot` owner all run on
  it. If less than `MinArea` remains, nothing is zoned (`Issue.ZoningLeavesNoRoom`).
* Entrances on a zoned edge keep a gap (their width + 3 m each side). The band is not zoned there, and the
  connector, alley or footpath crosses it to the street (the street search extends by the band depth).
* `PlannedZoning` quads are ≤ 48 m along the edge, with A→B on the outline and D→C on the lot side. Each is
  checked to be inside the outline (concave corners are retried cell by cell) and dropped if it touches
  any stall, lane, net or the lot (`Issue.ZoningTrimmed`). `ZoneId` = `zoneType`. Stat `Stat.ZoneCells`,
  fact `zoning.cells`.

**Game layer:** zone every cell of the street's existing zone blocks whose centre lies inside a
`PlannedZoning` quad (`ZoneId`, or the default zone when empty). Do not create roads. The band carries
no surface and must be left out of "clear existing props" and lot ownership so buildings can grow.
Interior zoning with its own road (PLT's rectangles) is left to Roads layer + edge-zoning add-on recipes.

## PLT parity checklist

| PLT feature | Status | Notes |
|---|---|---|
| Two-way / entry / exit entrances | ported | marker kinds `entrance`, `entry`, `exit` |
| Gasse, GasseEin, GasseAus (junction entrances) | ported | `alley`, `alleyIn`, `alleyOut` (round 1 had only a global style) |
| Fussweg (pedestrian entrance) | ported | `footpath`, attached to the street |
| 15-entrance quota; build rule decided by direction | ported | readiness + `TooManyEntrances` |
| Entrance arrows; footpaths beside one-way lanes (Netzerhalt) | ported | round 1 |
| Automatic entrances (`AutomaticEntrances`) | changed | suggestions applied by the tool as editable markers |
| Entrance corner fit (`EntranceCornerFit`) | dropped | entrances are clamped onto the edge; per-kind widths make it rarely needed |
| Randzoning (edge zoning) | changed | frontage bands on existing streets, no zoning road |
| Zoningflaeche (interior parcels with their own road, outer depths) | changed | `zoningPatch` on the frontage; interior blocks → Roads layer |
| Zoningstrasse choice, zoning max-size options | dropped | no zoning road is built |
| Bus stops | changed | on the street in front (PLT: on its own zoning roads); needs the game-layer resolver |
| Stall angle, sizes, aisle width, setback | ported | |
| Ring road on/off (Randstrassen) / ringless layout | ported | `perimeterRoad` |
| Cross lanes every N (Cr), caps at cross lanes (Qk) | ported | `crossEvery`; caps = row-end planting |
| Perimeter rows / inner perimeter rows | ported | |
| Row angle modes (edge, across, fixed) | ported | |
| Align to a picked line (AlignPick, Ausrichtwinkel) | dropped | `fixed` angle covers it; would need a tool pick mode |
| Per-sub-area angles, hand cuts (Teilflaechen*) | dropped | not cheap (own band plan per part); use a Split layer with two parking children |
| Special stalls (accessible, EV + chargers) | ported | |
| Lanterns (single/double heads, spacing, model) | ported | |
| Vegetation (trees, bushes, density) | ported | |
| Material surfaces / switches / repair | dropped | one asphalt + one green asset; SurfaceAcceptance |
| Apron in front of entrances (Vorflaeche) | dropped | connectors reach the kerb; revisit if the gap looks bare in game |
| Tree-clearing surface (Raeumbelag) | moved | game layer "clear existing trees/props" (FEEDBACK-1) |
| Utility auto-routing (Versorgung*) | dropped | out of scope (RESEARCH-NOTES) |
| Background sync, live log, diagnostics | dropped | crash source / developer tooling |
| Lot as one selectable/bulldozable thing | moved | game-build workstream (US7) |

## Proposals (contract changes, not applied)

1. **`PlannedNet.OneWay` (bool).** Today one-way is implied by the asset id
   (`Invisible Road Path - 1xOneway`, `Alley Oneway`) plus A→B order. An explicit flag would let the game
   layer pick its own one-way clone.
2. **Non-additive facts.** `BuildService.FinishRecord` sums facts over layers. That is right for
   `capacity`, wrong for `parking.fee` when a recipe has two parking steps. Proposal: facts whose key
   ends in `.fee` (or a `FactMerge` hint) take the maximum.
3. **`VisibleWhen` with `!=`** (e.g. `greenery!=none` for the planting controls, `edgeZoning!=none` for
   the zone controls). Until then those controls are always visible.
4. **`PlannedObject.Size` (float2, optional).** Stall width/length and accessible stall width
   (5.0 m) are layout facts the game layer could use to pick or scale decals.
5. **`ObjectRole.BusStop`** (optionally `PlannedObject.AttachExternalId/T`). Today a bus stop is
   `ObjectRole.Marker` + sentinel asset `Placemaker:BusStop`.
6. **`ControlSchema.MarkersRequired`** (int). Parking needs 1 vehicle entrance; footpaths, bus stops and
   zoning patches do not count, so "≥ 1 marker" is not the same rule. Meanwhile `Readiness.ReasonArgs[0]`.
7. **`ControlSchema.MarkerMax`** = 15 for parking (PLT quota), so the tool can refuse the 16th click.
8. **Group headings** `Placemaker.UI.Group.{entrances,spaces,lighting,surfaces,zoning,fee}` currently
   live in `Lang/en-US.parking.json`; move them to `en-US.ui.json` if other layers use the groups.

## Recipes

`Recipes/parking.json` (standard), `parking-green.json` (60°, wide planting), `parking-compact.json`
(edges only, no row-end planting, no cross lanes) — format per `docs/proposals/recipe-json.md`
(settings are overrides on `Defaults()`).
