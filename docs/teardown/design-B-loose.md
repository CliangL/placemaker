# Design B: loose placement. Rebuild the build pipeline the ParkingLotTool way

Author: architect workstream B, 2026-10-15. Read-only analysis of `master`/HEAD `d9f6df6` (650be0f + s61, roads only),
teardowns 00–10, the decompiled game 1.6.2f1 (DECOMP = `%TEMP%\pm-decomp\game`) and the bench prototype
(`.forensics/bench`). Nothing was launched, built, deployed or changed apart from this file.

Labels: **[P]** proven (file:line, log line, decompiled source or a bench run quoted in a teardown). **[H]** hypothesis, with
the offline check or the passive in-session evidence that settles it. No item in this design needs the AI lead to launch
the game. In-game evidence only comes from sessions the user starts anyway, and it changes nothing in the city.

---

## 0. The design in one page

**Decision.** Every piece of a build is an ordinary top-level vanilla entity, exactly what a player would get by placing
it by hand. Placemaker keeps only a **registry**: a tag on each piece plus one headless record per build. That covers the
whole build: nets, props, trees, surfaces, stall markings and the **shops, which become real placed buildings**. The
owner lot, the prop carrier owner, the Zone Block lots, both Harmony patches, the verdict classifier, the zoning
cutout/refresh machinery and the vanilla-rules mode are deleted.

| Goal (priority order) | How this design meets it |
|---|---|
| 1. Parking lot / strip mall **always builds**, no error dialogs | The build is a PLT-style Temp → Apply with **no gate**. The game's own error machinery is **switched off at the source** for our frames, the mechanism Anarchy uses (`ValidationSystem.cs:1219-1226, 1537`). No retry loop and no verdicts, so nothing can refuse, cancel, hide or demolish. The only stops are "not enough money", checked before anything changes, and an exception. |
| 2. **Preview == result**, shops included | One immutable **BuildManifest** per plan (exact courses, objects, buildings, rings, seeds), hashed. The ghost and the build write it through **one** `DefinitionWriter`. The build makes **no** decisions of its own. The audit is a **census** of live entities against the manifest. Shops are buildings, so the ghost shows the actual building. |
| 3. Existing city buildings are **never destroyed** | Without verdicts, Apply cannot turn `Warning` into `Deleted` (`ToolApplySystem.cs:50-57`). A belt-and-braces guard strips `Warning` from non-Temp entities before Apply. The plan keeps off building lots (`SiteSafety`). Zoning changes never touch occupied cells. The bench gate runs the real `CellCheckSystem` + `ZoneCheckSystem` over existing growables. |
| 4. **Easy removal** | "Delete build" in three places: the Placemaker panel's build list, the existing info-panel section on any member (`PlacemakerGroupSection.cs`), and a new "pick a build" tool mode (hover highlights the whole build, one click deletes after a confirmation). The vanilla bulldozer deletes single pieces, as the user asked today. |
| 5. **Verified offline** | The bench from teardown 06 becomes the acceptance gate. It runs the real `GeometrySystem`, `ValidateEdge`, `ValidateObject`, `OverrideSystem`, `BlockSystem`, `CellCheckSystem` and `ZoneCheckSystem`, plus our **real `Placemaker.dll` emitter**, over a type × preset × site × terrain matrix. "Fixed" may only be claimed when the matrix is green for every type that shares the changed path. |

**Why loose and not owner-based.** The owner lot never caused a refusal: it is emitted after validation and owns nothing
(01 §8 P9, 05 §0). It still cost two Harmony patches, a runtime prefab that every save depends on, and the
"select/bulldoze lands on a bare carrier" class of bugs (05 C1–C10). The prop carrier owner is added *after* the game
has judged the pieces, so it did not stop 24/28 props staying hidden (02 S5, 05 C3). ParkManager already ships the
loose + registry model (`EnsureMembersAreTopLevel`, 05 §2.2). PLT "just worked" because of plan-time clearances, no
error gate and preview = temporaries (05 §2.1). Those three properties are kept here; the owner tree is not needed for any
of them.

**What loose does NOT fix by itself.** Folded net geometry (`InvalidShape`) is a property of our courses (03). Building
it anyway is crash-safe but leaves broken lanes and meshes. Shops that are zoning cutouts can never match a preview (04).
These two need planner changes (§4, §5). They are part of this design because goals 1 and 2 cannot be met without
them.

---

## 1. Principles taken from the evidence

1. **The build never decides anything.** Every decision (asset, position, piece split, ring repair, which pieces exist)
   is made once, in the plan/manifest, and is visible in the preview. Today the build decides at least eight things on
   its own:
   - terrain splits of courses (`CourseFitting.Fit` at `BuildService.Emit.cs:142`);
   - dropping pieces < 1 m (`:148`);
   - ring repair (`:424-464`);
   - the decal clone or the original "until ready" (`:336-346`);
   - verdicts (`BuildService.cs:494`);
   - ProtectBuildings (`:500`);
   - zoning re-snap after Apply (`ZoneGuard.Refresh`);
   - ghost sampling above 6,000 definitions (`BuildService.Ghost.cs:239-247`).

   Each of these is a way for the result to differ from the preview.
2. **The game must not be asked for permission.** Apply ignores `Error` (`ToolApplySystem.cs:50-71`). Our own gate on the
   game's verdicts produced every refusal since `f7fa1c1` (01 §0.3). We stop asking, and we stop the game from producing
   verdicts for our frames at all.
3. **Correct by construction, checked offline.** Anarchy only guarantees completion. Correct geometry, no hidden props
   and no lost shops are planner properties, proven offline with the game's own code (06).
4. **Vanilla data over runtime machinery.** Prefer vanilla prefabs, vanilla flags (`ZonesDisabled`, `RemoveCrosswalk`,
   dezone) and vanilla relations (`Attached`) over runtime prefabs and ownership tricks, so that saves stay sane without the
   mod.
5. **Every fix covers every type on the path** (user rule). The net rules (§4) run on **all** nets of **all** layers. The
   shop model (§5) covers **every** type that hosts shops. The emitter (§6) covers **every** build and ghost.

---

## 2. Entity model

### 2.1 What one build creates

| Piece | Entity form | Owner / relation | Tag (`PlacemakerMember`) | Prefab | Survives a save without the mod |
|---|---|---|---|---|---|
| Record | headless `PlacemakerGroup` v3 + `PlacemakerGroupBlob` (receipt: outline, recipe, manifest hash, dezoned cells) | — | — | — | dropped (vanilla ignores it) |
| Roads, lanes, alleys, lot aisles, paths, connectors | top-level edges/nodes via `NetCourse`; `Upgraded` flags from the manifest (`RemoveCrosswalk`, `ZonesDisabled`) | none | Edge/Node; nodes only if every edge on them is ours (today's rule, `Adopt.cs:233-239`) | vanilla | yes |
| Props, trees, lamps, benches, fences-as-props, decals | top-level objects | none (**no carrier**) | Object/Generic | vanilla (decal opacity clones optional, §2.3) | yes (clones: no) |
| Stall markings, chargers, arrows | top-level objects, `Attached(aisle edge, curvePos)` | **no Owner**; `Attached` points at the aisle edge the stall faces | Object/ParkingBay | vanilla `ParkingLotDecal01` etc. | yes |
| Surfaces (asphalt, paving, grass) | top-level areas | none | Area/Surface | vanilla; paved ones via the overlay clone (`SurfaceCloneSystem`) | vanilla: yes, clone: no |
| **Shops** | top-level **buildings** (`ObjectDefinition`) facing our alley/lot | none | Object/**Shop** (new role 11) | runtime **shop clone** of a vanilla commercial growable with the zone-check exemption (§5.2) | no (same class as park companions) |
| Park companion (park types only, optional) | hidden building, **emitted from the manifest** like any building | none | Auxiliary/ParkCompanion | runtime clone (existing) | no |
| Clearing of existing trees/props | vanilla delete definitions (unchanged) | — | — | — | — |
| Terraform, paint | direct terrain API (unchanged) | — | receipt flag `PermanentTerrain` | — | yes |
| Zoning changes | vanilla Marquee zoning: **dezone** of unoccupied footprint cells, optional honest zoning | — | receipt keeps the dezoned cell list | vanilla zone prefabs | yes |

**Gone:**
- the owner lot "Placemaker Area" (`BuildService.Owner.cs`, `OwnerAreaPrefabSystem` creation path);
- the prop carrier owner (`ShareCarrierOwner`, `Adopt.cs:264-279`);
- the Zone Block lots (`BuildService.ZoneBlock.cs`);
- the parking companion (`ParkingCompanionSystem`, off by default and "crashed natively several times");
- the park companion's direct `EntityManager.CreateEntity` (`ParkCompanionSystem.cs:306`).

### 2.2 Stall markings without a carrier

**Why the carrier existed.** `LaneConnectionSystem.FindParkingConnection` looks for the road and pedestrian lanes of the
stall's root owner (05 §3.3; PLT measured "cars drive in, turn and leave" without it).

**[P] It also accepts an `Attached` parent.** Read at `Game.Net/LaneConnectionSystem.cs:471-504`, in order:
1. The search climbs the `Owner` chain of the parking lane. For a top-level stall marking it stops at the marking itself.
2. If that entity is not a building and has `Attached` with a valid parent, the parent becomes the search root
   (`:480-483`).
3. `FindParkingConnection(root)` scans the root's own `SubLane` buffer first (`:542-547`, `:567-570`). An aisle **edge**
   *is* a net with car and pedestrian sub-lanes.

So `Attached(aisleEdge)` on a top-level marking gives the parking lane a road connection with no Owner and no carrier.

**[H] Three things are not yet proven:**
- (a) `AttachSystem` (`Game.Objects/AttachSystem.cs:151`) does not overwrite our parent. It re-parents only prefabs with
  `RoadSide|OwnerSide|Shoreline|Floating` placement flags. Check: read `PlaceableObjectData.m_Flags` of the marking
  prefabs from the prefab capture (§9.4).
- (b) The connection really forms. Check: allow-list `LaneConnectionSystem` in the bench, or read the audit field
  `parkingLanesConnected` from the user's next session.
- (c) Deleting the parent edge leaves an unconnected stall and no crash. `AttachSystem` handles removed parents
  (`RemovedAttached`).

**Fallback.** A build setting keeps today's model (`Owner + Attached(carrier)`, aisles in the carrier `SubNet`,
`Adopt.cs:220-231`), and only for markings. Nothing else is ever carrier-owned again.

**Who owns `Attached`.** The emitter writes it on the temporary right after the census, on the matched aisle temp, before
Apply. Apply converts in place and keeps it, the same way today's tags survive (`Adopt.cs:164-169`).
`ParkingEconomySystem` finds our lanes through the marking's tag, which is unchanged.

### 2.3 Runtime prefabs that remain (save risk, listed honestly)

| Runtime prefab | Why it stays | Without the mod |
|---|---|---|
| Surface overlay clones | parking asphalt must draw over roads (priority −94) | surface lost |
| Decal opacity clones | art feature (see-through decals). **Optional, default off for commercial/parking** until teardown 09's catalog fixes land | decal lost |
| Shop clones (new) | zone-check exemption (§5.2) | shop lost |
| Park companion clones | park stats, park types only, optional | companion lost |
| Legacy: "Placemaker Area", "Placemaker Zone Block", parking companion clone | **kept registered forever** so old saves load (§8) | already lost today |

Each one is registered from a remembered-names file before the save loads (the existing companion pattern,
`ParkCompanionSystem.cs:9-11`). None may trigger a catalog rescan (09 fix 1).

---

## 3. Build path

### 3.1 State machine (replaces `BuildService.cs:62-67`)

```
Prepare      manifest = BuildManifest.From(plan, resolved assets)   (pure Core + name→prefab resolution)
             money check against manifest price (shops included)   — before ANY change; Build button disabled with reason
             edit: request removal of the old group (unchanged)
WaitReplaced (edit only)
ActivateTool BuildToolSystem active, one Clear frame (drops the ghost temporaries)
Terraform    direct brush passes (unchanged), then Paint (unchanged)
Emit         DefinitionWriter.Emit(manifest, heightsNow): nets of each connected component in one frame,
             then objects/areas/buildings ≤ 1500 per frame. Same writer, same seeds as the ghost.
WaitTemps    census by manifest key until all items matched, or stable for 12 frames
             (unmatched items become audit entries "not materialised", one per item)
Tag          PlacemakerMember on every matched temp; Attached(aisle temp) on stall markings
Apply        guard: strip Warning from every non-Temp entity (defence in depth, §3.3); applyMode = Apply
Settle       census of permanent members against the manifest; AdoptReplacements; write the record
Zoning       only if the manifest has zoning ops: ONE Marquee transaction (dezone ops + optional zone ops);
             wait one cell check; verify cell by cell
Finish       audit: census, hidden count, folds (§9.5), parking connections, shops present; receipt; restore tool
Cleanup      exception or tool interruption only: before Apply → Clear; after Apply → rollback via group removal
```

**Deleted steps:**
- `Validate` (the verdict read). Kept only as the census settle.
- `ResetForRetry` and the duplicate retry. The duplicate invariant stays as an assertion: fail and roll back.
- `OwnerLot`.
- `ZoneRefresh`, `ZoneBlock`, `ZoningWait`/`ZoningEmit`/`ZoningApply` (replaced by the single `Zoning` step).
- The instant build from the ghost (`TryBuildFromGhost`, `_instant`).

`RoadPreZone` becomes a producer of zone ops in the manifest for the Roads type (§5.5).

**Failure paths left, all explicit:**
- not enough money: before terraform, the Build button is disabled with a reason;
- the player switches tools between Emit and Apply (≈10 frames): Clear, nothing changed except terraform, which is stated;
- an exception: rollback.

`OriginalDeleted` (an attached city edge vanished meanwhile) no longer fails the build. That course is re-emitted once
with a free end, and the audit says so.

### 3.2 Errors off at the source (the anarchy mechanism)

New `ErrorSuppressionSystem` (Game/Build), two hooks in `SystemUpdatePhase.ModificationEnd`:
- `UpdateBefore<ValidationSystem>`: if (the Placemaker tool is active **and** ghost temporaries exist) **or** a build holds
  the tool:
  1. snapshot `ToolErrorData.m_Flags` of every error prefab;
  2. set `ToolErrorFlags.DisableInGame` on all of them.
- `UpdateAfter<ValidationSystem.Components>`: restore the snapshot exactly.

**[P] Mechanism.** `ValidationSystem` rebuilds its error-prefab table every run and skips disabled types
(`ValidationSystem.cs:1219-1226`). `ProcessError` returns early for a type without a prefab (`:1537`). So no icon, no
`Error`, no `Warning`, no `Override` and no `Cancel` is produced, on our temporaries **or** on city entities. This is how
Anarchy works (05 §2.4).

Consequences:
- **Ghost = build.** No red tint, no cancelled optional objects (the ghost stops using `CreationFlags.Optional`).
  `HideGhostIcons`/`ClearGhostVerdicts` become dead code.
- **No demolition path.** `ToolApplySystem` only deletes `Warning` entities and hides `Override` ones
  (`ToolApplySystem.cs:50-57`). Neither can exist.
- **No vanilla money error.** Covered by the pre-check in Prepare.

### 3.3 Defence in depth (because other mods toggle the same flags)

**[H] Ordering.** Anarchy also sets these flags in Modification5 and restores them in ModificationEnd (05 §2.4). Its
restore could run between our set and the validation in the same phase. Both orders are idempotent except that one.

Guards that hold regardless:
- just before `applyMode = Apply`, remove `Warning` from every non-Temp entity, and `Override`/`Error` from our tagged
  temporaries. This is today's `NeutralizeVerdicts`, cut down to about 20 lines.
- the audit logs `suppressedFrames` and `verdictsSeen` (expected 0) as passive evidence.

### 3.4 Why Temp → Apply and not `CreationFlags.Permanent` (for now)

The permanent path (`GenerateObjectsSystem.cs:1278`; used by the game's `ZoneSpawnSystem.cs:627` and by PLT's background
builder) would skip validation altogether. It is not chosen as the first step:
- **[P]** the game never splits an existing city edge with a permanent course (05 §4.2, `CourseSplitSystem` `:3857`), so
  every entrance into a city street would need a second, Temp transaction;
- **[H]** top-level unowned permanent nets are unproven (PLT's permanent nets are owned);
- the ghost has to be Temp anyway, and one path for both means fewer differences.

With errors suppressed, Temp → Apply has the same "cannot be refused" property. Permanent emission is still used **in
the bench** as the census proxy (§9.3), and stays an option for a later step.

---

## 4. Nets: no `InvalidShape` by construction (all types)

`InvalidShape` on our nets is a fold: an edge shorter than the room its two junctions cut out of it, or a curve tighter
than its half width (03 §2, `Game.Net/ValidationHelpers.cs:1360-1391, 1835-1862`). With errors suppressed the net is
built anyway, but the fold is permanent: broken mesh on `Alley`, kinked lanes, wrong zone blocks (03 §4). So **folds
matter**. They are prevented by one shared Core pass plus parking-layout rules, and the bench proves the count is zero.

### 4.1 `NetTopology`: one type-agnostic pass over all nets of all layers (new, `Core/Build/NetTopology.cs`)

It runs at the end of `Composer.Compose`, after every layer, so it covers all ~49 types. Inputs: all planned nets, the
city nets the plan attaches to, and per prefab: half width `h`, node offset `n`, `m_MinNodeOffset`, crosswalk depth `cw`,
merge layers. The prefab numbers come from the catalog in game and from the prefab capture in the bench. They default to
03's values (n = 0.5 m) until the capture exists.

Steps, in order:
1. **Weld.** Merge end points closer than `L_min(prefab pair)` (R4), not 0.75 m (`PlanBuilder.SplitAtJunctions`
   `:671-718`). Collinear duplicates are merged into one piece.
2. **Node every crossing** (R3). Two planned nets that cross get a shared node at one exact point, because the game never
   joins two new courses (03 §2.4, `CourseSplitSystem` `FindOverlapsJob`). Two edges leaving one node in the same
   direction are rejected.
3. **Exact courses.** Terrain fitting moves **into the plan** (from `BuildService.Emit.cs:142`). Pieces are fitted to the
   *target* ground (`AreaPlan.HeightAfter`). No split may lie within `cut + 2 m` of a junction; split pieces are ≥ 6 m;
   the deviation threshold is 0.6 m (R6; restores the pre-`082afac` behaviour that 03 §3.3 C measured at 0–19 instead of
   23–102 extra folds). The resulting pieces **are** the manifest courses: the emitter never splits.
4. **Cut-back check** (R1). For every piece:

   `len ≥ cut(start) + cut(end) + 0.2 m + 0.5 m`, with
   `cut = max_j (h_j + n + |s|·w/4 + cw_j + h_i·cosφ)/sinφ`,

   floored by `n` for a prefab change, by `|Δgrade|·w` when that exceeds 0.2 m, and by `m_MinNodeOffset` at a non-straight
   join (03 §2.3).
   - This is a **port of `GeometrySystem.CalculateCornerOffset` (`GeometrySystem.cs:824-1170`)**, written with the
     game's own `NetUtils`/`MathUtils` semantics and checked differentially against the real system in the bench (§9.2,
     gate A4-net).
5. **Repair, in this order:**
   - (a) slide a T junction along its host by ≤ 4 m;
   - (b) remove a junction of degree 2 and merge the two pieces;
   - (c) turn a stub into a kerb dead end (LocalConnect: today's s47 fallback);
   - (d) drop the piece and the objects that depend on it, with a plan issue that the preview shows.

   Never drop at emit time (today `MinCourseLength`, `Emit.cs:148`, leaves a gap).
6. **Node heights** (R7). One cached height sample per 2D node, used by every course that ends there. The game merges
   only bit-identical positions (`GenerateNodesSystem.cs:126-163`).
7. **Grades at nodes** (R11). `|Δgrade|·w ≤ 0.2 m` at every node (7 m nets: ≤ 2.8 %), or the cut includes it.
   `LotGrading` puts tier ramps mid-piece, never at a node.
8. **City attach points** (R10). An attach point must be ≥ `cut` from every existing node of that street on both sides;
   otherwise it snaps to the existing node. This keeps the *city's* split pieces valid too (03 §3.3 H).
9. **Curves** (R9). Centre-line radius ≥ `h + n + 1 m`.

`NetFit.cs` and the `MallNetGeometryTests` model are replaced. The tests are rewritten to cover **every** net role on
**every** matrix site (03 §3.4: today aisles, cross lanes and footpaths are never checked, and only on one square site).

### 4.2 Parking layout rules (PLT's, made explicit; apply to every type that runs the Parking planner)

| # | Rule | Number | Source |
|---|---|---|---|
| P1 | One straight course per planned aisle, ring side or cross lane | — | PLT `ParkingLotNetCourse.cs` |
| P2 | Aisle ends aligned when it costs ≤ 1 bay (no "jog" pieces) | offsets ≤ 0.43 m removed | PLT `Ringlos.PlaneGemeinsameEnden` |
| P3 | No end section under 2 bays; a cross lane only where the section carries ≥ 5 bays | — | PLT `Planung.cs:611-615` |
| P4 | Footpaths never T into aisles: they end as dead ends ≥ `h_aisle + 1 m` from the aisle, or join an existing aisle node | — | PLT (paths not T-split), 03 §5 |
| P5 | Ring corners 90°; ring/aisle piece between two T's ≥ 8.7 m (2xTwoway both ends) | 03 §6 table + 0.5 m | 03 R1 |
| P6 | Entrance connector perpendicular (60–90°) to the street; length ≥ `h_street + n + transition + 0.5` (≥ 7.5 m on a 12 m street); `RemoveCrosswalk` on both sides; ≥ 1 per lot; "NoEntrance" is never dropped silently (`Mall.cs:350`) | 7.5 m | 03 R8, PLT entrance branch, 07 R4 |
| P7 | Alley stubs: ≥ 5.2 m between a T on an 8 m Alley and the join into an aisle (today 4.0 m); ≥ 7.2 m + crosswalk at a 12 m street | — | 03 §6 |
| P8 | A lot exists only with an accepted entrance, ≥ 1 full double-loaded bay (≥ 18.8 m deep), ≥ 30 % of its area in stalls and a minimum stall count; otherwise the land is planted buffer | 07 R4/R5 | 07 F8 |
| P9 | No vanilla lot area: the parking sub-plan emits **no** `AreaRole.Lot` (`PlanBuilder.cs:442-450`); `area.lot` is never automatic | — | 07 F1/F2 (the farm fields and fences) |
| P10 | Stall markings keep the s53 scale (2.97 m on a 3.0 m stall), which built 122/122 with 0 errors (01 §7.2) | — | — |

---

## 5. Shops and zoning

### 5.1 The decision

A shop is a **placed building**. It is not a zoning cutout that the simulation may fill later. Grounds:
- no cells exist before Apply (`BlockSystem.cs:1164`);
- every prediction mismatch lost a shop (04 V1–V8: 11 of 17 plots in today's session);
- the city street wins contested cells by build order (08 §3);
- even perfect cells wait for demand (`ZoneSpawnSystem.cs:1102`).

A building in the manifest is in the ghost and in the result. It faces the road we choose, and it is there whatever the
demand.

### 5.2 Shop prefabs (runtime clones, zone-check exempt)

- **[P] Exemption by prefab data.** `ZoneCheckSystem` never condemns a building whose prefab has `SignatureBuildingData`
  (`ZoneCheckSystem.cs:52`). `BuildingUpkeepSystem` never abandons it (`:278`). `CellOccupyJobs` occupies its cells and
  sets their zone to its own (`CellOccupyJobs.cs:93-97`), so no growable can spawn there.
- **`ShopPrefabSystem` (new, Game/Economy).**
  - For each vanilla commercial growable chosen by the palette, it registers a clone "Placemaker Shop – <name>". The
    clone keeps meshes, sub-objects and `BuildingPropertyData`, and gets the exemption.
  - Two candidate recipes, picked by the offline code check in WP5:
    - (a) replace `SpawnableBuilding` with vanilla `SignatureBuilding{ m_ZoneType = same zone, m_XPReward = 0 }`. That
      adds `SignatureBuildingData` + `SpawnableBuildingData`, and `UniqueObject` on instances (`SignatureBuilding.cs`);
    - (b) a mod component that adds `SignatureBuildingData` without `UniqueObject`. `UniqueObject` only drives
      `AlreadyExists` validation (suppressed), info-view colours and markers (`MarkerCreateSystem.cs:738`,
      `ObjectColorSystem.cs:667`).
  - No `UIObject` component. That keeps the clone out of the menus and avoids the "signature unlocked" popup, which needs
    `UIObjectData` (`SignatureBuildingUISystem.cs:61`).
  - Names are remembered in `shop-prefabs.txt` and registered before PrefabInitialize at every start. This is the
    companion pattern (`ParkCompanionSystem.cs:9-14`), and it must not trigger a catalog rescan (09 fix 1).
- **Lot sizes** come from `BuildingData.m_LotSize` in the prefab capture (§9.4; the catalog dump has no buildings today).
- **[H]** Companies rent the clone (it keeps `BuildingPropertyData`), and it shows no persistent "empty" warning.
  Evidence: the audit follow-up 2 in-game minutes after the build records renters per shop (§9.5). This is the single
  in-game unknown of the shop model. Without renters the building still exists and looks right, so goal 2 holds either
  way.

### 5.3 Where shops go (commercial composition, replaces lot-first leftovers; 07 R1–R9)

**Templates** (one per site, chosen up front):

- **Strip centre:**
  - street;
  - one or two driveways (P6);
  - the lot, with aisles parallel to the shop fronts;
  - a **shop-front Alley** (vanilla `Alley`, a real road) along the fronts;
  - a sidewalk;
  - the **row of shop buildings** facing the Alley;
  - an optional service lane behind;
  - planted buffer.

  This is the user's "they should have been put on the alleyway".
- **Shopping alley:** a pedestrian street with shops on both sides facing each other, open ends, and the lot at one end.
- **Street front** is opt-in only.

**[P] Facing.** `RoadConnectionSystem` connects a building to the nearest non-elevated edge with a `ConnectedBuilding`
buffer within **8.4 m of its front position** (`RoadConnectionSystem.cs:77-91`).

Placement rules:
- Front line ≤ 2 m from the Alley's outer edge.
- The front position must be closer to the intended edge than to any other edge by ≥ 3 m.
- Yaw = facing the Alley.
- Lots are filled left to right with the best-fitting lot widths. The remainder is planted.

### 5.4 Zoning, reduced to vanilla data

1. **Our zoning roads get `ZonesDisabled` on both sides by default.** These are `Alley`, `Pedestrian Street (Small)` and
   any street a layer adds; the flag goes through `PlannedNet.Upgrades` (`BuildService.Emit.cs:176-179`, `:277-299`;
   08 §4.1). They create no cells, so they compete with nothing.
2. **Footprint dezone** replaces the Zone Block lots.
   - Mechanism: a vanilla Marquee transaction with `ZoningFlags.Dezone | Overwrite` (`ZoneToolSystem.cs:231-234`), one
     small quad per **visible, unoccupied** cell whose centre lies in the footprint (`GenerateZonesSystem.cs:84-87`).
   - Effect: growables can no longer appear on our plaza or lot. The data is vanilla, survives the mod's removal and is
     undoable with the vanilla zone tool.
   - **Occupied cells are never touched**; a dezoned occupied cell would condemn a building. The site capture must record
     `Cell.m_Zone` + `Occupied` (08 §7, 04 V5).
   - The receipt stores (cell centre, previous zone). "Delete build" re-zones those cells with Marquee, no Overwrite.
3. **City street flags are never touched by default.** `ZonesDisabled` works per whole edge side and condemns growables
   outside the build (08 §4.1). With shops as buildings, the street's cells inside the footprint are simply dezoned and
   those outside stay as they are.
4. **Optional, honestly labelled zoning.** "Let the city grow shops along our lane" (off by default): the lane's chosen
   side keeps zoning (no `ZonesDisabled`) and gets a zone op. The preview draws those cells as "zoned land: buildings
   grow with demand", never as shops. They must be alive in the bench (gate G6).

**Deleted:**

| Item | Lines / location |
|---|---|
| `ZoneGuard` (snap, refresh, nearest run, ranking) | 1,437 |
| `ZoneLots` | 122 |
| `Cutouts` | 409 |
| plot rows in `StreetShops` and `Mall.Shops` | — |
| `EdgeZoningAddOn` as shop source | 304 (kept only as the optional-zoning producer) |
| `BuildService.ZoneBlock.cs` | 355 |
| `ZoneCause.cs` | 76 |
| zoning verify in `Terrain.cs` | `:547-790` |

`ZoneBlockPredictor` stays: it matches the real `BlockSystem` within 5 cm (06 §1.5) and is needed for previewing the
optional zoning and the Roads type.

### 5.5 Roads type (zoned city streets)

`RoadPreZone` produces zone ops for the cells the predictor gives the new roads. They are applied in the one Zoning step
and verified cell by cell against the preview, with no re-snap. This is the only type whose *purpose* is zoning, and its
preview label says "zoned, grows with demand".

---

## 6. Preview == result

### 6.1 `BuildManifest` (new, `Core/Build/BuildManifest.cs`, pure)

```
BuildManifest
  ModelVersion, PlanHash, Seed (from the plan, never from a runtime group id)
  Nodes    : id, xz, kind (free | junction | cityAttach(entityId, t))
  Courses  : id, prefabName, Cubic (exact piece), startNode, endNode, upgrades[], elevation flags, seed
  Objects  : key, prefabName (concrete, clone tier resolved), xz, yaw, scale, age, seed, elevation, attachToCourse?
  Buildings: key, prefabName (shop/companion clone), xz, yaw, lotSize, intendedRoadCourse
  Areas    : key, prefabName (overlay clone resolved), ring (already AreaRings/SurfaceAcceptance-valid)
  Clears   : existing tree/prop entity ids (never buildings, never sub-objects of buildings)
  Zoning   : dezone cells, optional zone quads
  Terraform, Paint
  Hash()   : FNV over all of the above
```

How it is built and used:
- Built on the preview worker right after `Compose` + `ResolveAutomatic`. Teardown 09 fix 6 already moves those to the
  worker.
- Cached by `(outline version, recipe fingerprint, catalog version)`, which is 09 fix 7.
- The ghost revision is the manifest hash, so there is no rebuild without a content change (09 fix 3).

### 6.2 One writer

`DefinitionWriter` (Game/Build). It needs only an `EntityManager`, a name → prefab map and a height function; no tool or
service state. It writes manifest items as `CreationDefinition` + `NetCourse` / `ObjectDefinition` / `Areas.Node`.
- **Ghost:** `Write(manifest, permanent:false)` while our tool is active.
  - 200 items per frame (09 fix 4); nets of each connected component in one frame.
  - **No sampling cap.** The 6,000-definition sampling (`Ghost.cs:239-247`) is deleted. Above that size the ghost takes
    more frames; it never shows a subset.
- **Build:** the same call after terraform. Only `y` is resampled from the live ground: one sample per node (R7) and
  objects snapped as today.
- **Bench:** the same call with `permanent:true` into the bench world (§9.3).

The decal clone tier and the overlay surface clone are resolved **in the manifest**. The writer waits until every clone
the manifest names is registered (prewarm, 09 fix 5) before the first ghost frame, so the ghost never shows "the original
until ready".

### 6.3 Census instead of arithmetic

The audit's "built" becomes a count of live tagged entities matched to manifest keys (prefab + xz ≤ 0.1 m + yaw ≤ 1°).
Today it is `planned − left out` (`Audit.cs:362`, 02 §6).

Per manifest item the audit records one of:
- `built`;
- `notMaterialised`;
- `hiddenByGame` (`Overridden` after Settle);
- `foldedNet` (§9.5);
- `replacedByPlayer` (later).

The UI shows "Built 312 of 312", or names the items. Any non-`built` item is a defect report that carries the manifest
hash.

### 6.4 What the preview shows

| Shown | Note |
|---|---|
| The ghost, i.e. the same definitions | — |
| The terraform overlay | heights change, xz and the set of pieces do not |
| Shops as building models | — |
| Dezoned land, faint hatch | "zoning removed so the city does not build here" |
| Optional zoning, hatched | labelled "grows with demand" |
| Plan issues with their markers | e.g. "3 stalls left out: entrance too close to Mill St node" |

There are no red pieces because there are no verdicts. Anything the plan leaves out is a plan issue visible *before*
Build.

---

## 7. Removal

| Action | Path |
|---|---|
| Delete one piece | vanilla bulldozer, a normal top-level entity. The registry marks the group `Modified`; when the last non-auxiliary member goes, the record cleans itself up (`GroupRegistrySystem.cs:440-502`, unchanged) |
| Delete the whole build | (1) Placemaker panel → Builds → Delete; (2) info-panel section of any clicked member (`PlacemakerGroupSection`, exists); (3) **pick mode** in the Placemaker tool: our own hit test against record outlines (receipt) and member bounds, all members highlighted, click, confirm. No Harmony |
| Removal mechanics | unchanged `GroupRemovalSystem` (PreTool, `RemovalPlanner` order, waits for tool temporaries). After the members, re-zone the receipt's dezoned cells (Marquee, no Overwrite). Terraform and paint stay, as today and as stated in the Review |
| Edit | remove + rebuild (unchanged) |

**Removed:**
- `RaycastLiftPatch` (63 lines) and `BulldozeConfirmationPatch` (110), plus `PlacemakerHarmony` if nothing else patches;
- `TryLiftToOwner`, `IsOwnerlessGroupPart`, the owner cascade in `HandleDeletedMembers`, `EnsureOwnerBuffers`,
  `OwnerAreaOf`.

This **supersedes the playtest-1 rule "one bulldozable object"** by the user's own suggestion of 10-15 ("individually
delete all the assets"). One-click whole-build deletion stays, through Placemaker's UI instead of the vanilla bulldozer.

---

## 8. Migration of existing saves and builds

**Record.** `PlacemakerGroup` v3 appends `int Model` (1 = owner-lot era, 2 = loose). v1/v2 read as Model 1. The `Owner`
field stays in the layout (`GroupComponents.cs:65-97`) and is written `Entity.Null`.

**`LooseMigrationSystem`.** One shot per load, after `PostLoadRepair`. Deletions go through `Deleted` in PreTool
(RESEARCH-NOTES 11–12). For each Model-1 group:
1. **Owner lot** (`Role OwnerArea`): delete it. It owns no member (05 §0), so the cascade is empty. Its attractiveness
   fallback moves to the receipt / companion.
2. **Carrier-owned props** (`Owner == group.Carrier`, role ≠ ParkingBay): `RemoveComponent<Owner>` + `Updated`, then
   clear `Overridden` where the only overlaps are other members. Precedent: ParkManager removes `Owner` from permanent
   members (`ParkPathPlacement.cs:1054-1064`). `RestoreOwnHidden` (`GroupRegistrySystem.cs:656`) runs one last time and is
   then deleted.
3. **Zone Block lots:** kept as legacy members (invisible physical lots that only block cells) until the group is
   deleted. Converting them would change zoning without the user asking.
4. **Stall markings + carrier:** unchanged until §2.2 (H a–c) is confirmed by a user session's audit. Then an optional
   pass re-attaches each marking to its nearest aisle edge and deletes the carrier.
5. Write `Model = 2` (legacy-loose).

**`LegacyPrefabRegistry`.** It keeps registering every runtime prefab any save may reference: "Placemaker Area",
"Placemaker Zone Block", the parking companion clone, decal/surface/companion clones from their remembered lists. Deleting
the systems that *created* them must not delete their registration.

**Saves without the mod.** Strictly better than today. Vanilla pieces stay as plain city content with no bare carrier
above them (05 C5 is gone). Runtime-prefab entities (shops, overlay surfaces, clones, companions) are lost as today. A
setting "Shops: vanilla growables only (grow with demand)" lets a player choose mod-independent saves; it is labelled, and
it is the old zoning-only behaviour without blockers.

---

## 9. Offline verification: the bench as the acceptance gate

### 9.1 Verification ladder (no rung launches the game)

| Rung | What | When | Budget |
|---|---|---|---|
| L0 | Core xUnit: manifest determinism, NetTopology rules, shop row planner, census matching, receipt v3 round trip, migration planner (pure) | every change | ≤ 60 s for touched tests |
| L1 | Bench smoke: all type × preset on S1 flat | every change touching Core/Build, Game/Build or layers | ≤ 30 s |
| L2 | Bench full matrix (§9.3) | before any hand-over | ≤ 6 min |
| L3 | Passive evidence from sessions the user starts anyway (§9.5): prefab capture, audit census, folds, renters | whenever the user plays | 0 for the user |
| — | Teardown 10's stripped-down in-game suite | **not in the loop.** At most an opt-in the user starts; the lead never launches it | — |

### 9.2 Bench projects (productising `.forensics/bench`, teardown 06 §7)

These live under `Tests/` and ship nothing:
- `Placemaker.GameBench.Shim`: icall shims, synchronous jobs, deferred-array patch.
- `Placemaker.GameBench.ShimGen`: Mono.Cecil rewrite into a git-ignored `.shim/<version>`.
- `Placemaker.GameBench.Host`: resolver, neuter allow-list, loads the **real `Placemaker.dll`** and registers its types as
  `ModManager` does (06 §0.2).
- `Placemaker.GameBench` (net48 surface): the oracles and fixtures.
- `Placemaker.Tests/GameBenchGate.cs`, `[Trait("Category","GameBench")]`: skipped when `ManagedPath` is missing.

The allow-list of real game systems grows to:
- `GeometrySystem`, `BlockSystem`, `CellCheckSystem`, the search systems (proven, 06);
- **plus** `GenerateNodesSystem`, `GenerateEdgesSystem`, `GenerateObjectsSystem`, `GenerateAreasSystem`
  (Permanent definitions);
- `Game.Objects.OverrideSystem`, `ZoneCheckSystem`, `RoadConnectionSystem`, and optionally `LaneConnectionSystem` and
  `GenerateZonesSystem`.

**[H]** each of these runs headless as the first four did. The method is the one 06 §1.3 used; the risk is a missing
component on a hand-made archetype, which the archetype capture (§9.4) fixes.

### 9.3 Matrix and gates

**Matrix:** every type × preset (292 plans in 06) × sites S1–S8 × terrain T1–T3 (T4 shore for water types). That is
about 7,000 plans. 06 measured 1.8–4.8 s per 292 plans for net + zone checks, so this is about 2–3 min plus emission.
- S1: 115 m square with a 12 m street loop.
- S2: 70×50.
- S3: 160×90.
- S4: 100×80 rotated 23°.
- S5: L 120.
- S6: an irregular quad.
- S7: a 28–44 m strip between an avenue with residential zoning and a street (the shot-107 case).
- S8: a corner block with 3 existing growables and a service building next to the outline.
- T1: flat. T2: 6 % slope. T3: ±4 m hills (seeded). Heights go into `TerrainSystem.m_CPUHeights` by reflection
  (`Fakes.cs:19-24`). The graded target is applied directly; a "+0.5 m residual" variant tests R6.

**Pipeline per plan:**
1. Compose.
2. Manifest.
3. **`DefinitionWriter.Write(permanent:true)`** (the real mod code) into the bench world.
4. The real Generate → Geometry → Block → CellCheck → Override → RoadConnection → ZoneCheck systems, N frames.
5. Census and gates.

A second, Temp-mode pass feeds the same nets to the real `ValidateEdge`/`ValidateObject` and reads verdicts **as
diagnostics**. In game they are suppressed; the bench uses them to prove "no collision, no fold".

| Gate | Pass condition (per plan) | Goal |
|---|---|---|
| G1 nets | 0 `InvalidShape`, 0 `ShortDistance`, 0 own-vs-own `OverlapExisting`, 0 crossings without a node, 0 near-duplicate nodes, 0 pieces < 1 m, including the city pieces our attachments split | 1, 2 |
| G2 visibility | after the real `OverrideSystem`: 0 own members `Overridden`; 0 own props overlapping a non-overridable own object (shops) | 2 |
| G3 city safety | 0 `Warning`/`Override` verdicts against non-Placemaker entities; 0 existing growables condemned by the real `ZoneCheckSystem`; 0 occupied cells whose zone changed | 3 |
| G4 census | live entities == manifest (prefab, xz ≤ 0.05 m, yaw ≤ 0.5°): 100 %; 0 unmatched live entities | 2 |
| G5 shops | every manifest shop exists; the real `RoadConnectionSystem`'s `m_RoadEdge` = the intended course; not `Condemned` after 64 frames; count ≥ 80 % of the template target, or a plan issue explains why | 1, 2 |
| G6 optional zoning | every previewed zoned cell alive (Visible, zoned) after the real pipeline | 2 |
| G7 parking | every stall `Attached` to an aisle whose lanes reach a city edge in the course graph (and connected by `LaneConnectionSystem` when allow-listed); 0 stalls without a lane; 0 fences across an entrance; P8 holds for every lot | 1 |
| G8 hygiene | no vanilla industry lot area, no `surface.crop*` in non-farm builds, no fence on an access edge (07 F3) | 1 |
| G9 ground | every node within 0.3 m of the graded target; no bowl > 1 m within 10 m of a node in the target raster | 1 |
| G10 runtime | compose p99 ≤ 150 ms (.NET), manifest build ≤ 20 ms p99, main-thread emit planning ≤ 3 ms per 1,000 items | user's hang complaint |

**Today's baseline** is red on G1 (104/292, 06 §5), G4 (ring repair, splits, sampling), G6/zoning (53 % alive) and G8
(farm lots, 07). The first job of WP1 is to freeze that red baseline, so every later WP shows its delta.

**Port differentials (must be 0):**
- the `NetTopology` cut-back port vs the real `GeometrySystem` + `CheckGeometryShape` on 10,000 random junctions (angles
  30–150°, widths of all used prefabs, grade steps);
- `ZoneBlockPredictor` vs `BlockSystem` on 500 curves (06 A4).

**Calibration.** Until the prefab capture exists, every verdict prints "UNCALIBRATED". After the capture: the bench must
reproduce ≥ 95 % of the folds and hidden objects the in-game audits report (06 A6).

**Reporting rule** (user rule, 06 A8):
- "fixed" only when L2 is green for every type that shares the changed path, naming the capture version;
- "installed" only when the user's `Mods/Placemaker/Placemaker.dll` matches the commit **and** an audit from the user's
  session carries that build hash.

### 9.4 Prefab and archetype capture (read-only, rides on the user's next city load)

On city load the mod writes `ModsData/Placemaker/bench-capture-<gameVersion>.json`, once per game version, in about 1 s.
Contents:
- for every net prefab in the catalog: `NetGeometryData`, compositions (`NetCompositionData`, crosswalks, pieces);
- `ObjectGeometryData` + `PlaceableObjectData` for every object prefab;
- `BuildingData.m_LotSize`, `SpawnableBuildingData`, `BuildingPropertyData` for commercial growables;
- the component-type lists of the archetypes the game gives our Temp and applied edges, nodes, objects, buildings and
  areas.

Teardown 06 §8.1 specifies the format. The lead copies the file into the bench fixtures. Nothing in the city changes.

### 9.5 Passive in-session evidence (changes nothing, costs the user nothing)

Every build's audit JSON gains:
- type, preset, outline and manifest hash (02 G1/G4);
- the census per item (§6.3);
- **folds**: the game's own `ValidationHelpers.CheckGeometryShape` (a pure static, 05 Appendix A) run on our live
  `EdgeGeometry`/node geometry after Settle, and on the ghost after it settles, with positions and course ids;
- `Overridden` members;
- `parkingLanesConnected / stalls`;
- `suppressedFrames` and `verdictsSeen` (expected 0);
- a follow-up 2 in-game minutes later: shops present, condemned (expected 0), renters per shop.

`Host replay <audit.json>` replays the manifest offline and diffs against the in-game outcome. That is calibration A6
with no launch.

---

## 10. What to delete (ruthless list)

| Area | Delete | Lines (approx.) | Replaced by |
|---|---|---|---|
| Owner lot | `BuildService.Owner.cs`; owner creation in `OwnerAreaPrefabSystem`; `Core/Build/OwnerRings.cs` + tests | 290 + ~100 + 70 + 53 | registry + Delete build |
| Harmony | `RaycastLiftPatch.cs`, `BulldozeConfirmationPatch.cs`, `PlacemakerHarmony.cs` | 223 | pick mode, info section |
| Registry owner bits | `TryLiftToOwner`, `IsOwnerlessGroupPart`, owner cascade, `EnsureOwnerBuffers`, `OwnerAreaOf`, `_owners`, `RestoreOwnHidden` (after migration) | ~180 | — |
| Verdicts / anarchy v1–v3 | `BuildService.Verdicts.cs`, `BuildService.Protect.cs`, `Core/Build/BuildVerdicts.cs`, `OwnOverlap.cs`, `AnarchyAuditTests`, `BuildVerdictsTests` | 288 + 107 + 155 + 66 + 278 | `ErrorSuppressionSystem` + 20-line guard |
| Retry / exclude / vanilla-rules mode | `ResetForRetry`, `_dupRetried` retry, settings `AutoExcludeCollisions`, `RecoverRejectedPieces`, `IgnoreOwnCollisions` (off mode), ghost `Optional` flag, `HideGhostIcons`, `ClearGhostVerdicts` | ~150 | one mode |
| Carrier for props | `ShareCarrierOwner`, `_carrierOwned`, carrier part of `AuditSurvivors` | ~80 | plan-time clearance (G2) |
| Instant build | `TryBuildFromGhost`, `_instant` branches | ~60 | one emit path |
| Build-time decisions | `CourseFitting.Fit` call in `EmitNets`, the `< 1 m` drop, `EmitPreparedRings` repair, decal "original until ready", ghost sampling | ~120 | manifest |
| Zoning machinery | `ZoneGuard.cs`, `ZoneLots.cs`, `Cutouts.cs`, `BuildService.ZoneBlock.cs`, `ZoneCause.cs`, zoning verify in `Terrain.cs:547-790`, shop plot parts of `StreetShops`/`Mall`/`MallKit`/`EdgeZoningAddOn`, tests `ZoneGuard*`, `ZoneBlockerClip`, `ZoningFirst` | 1,437 + 122 + 409 + 355 + 76 + ~240 + ~600 + 1,510 | shops as buildings, dezone, `ZonesDisabled` |
| Lot-first leftovers | `StreetRows` default, `RestLots`/`PolyLot` leftover parking (`CommercialLayer.cs:169-173, 318`; `MallLots.cs:144-180`) | ~300 | templates (§5.3), planted buffer |
| Vanilla lot area | `PlanBuilder.cs:442-450`; `area.lot` automatic (`AssetHeuristics.cs:99`) | ~15 | nothing |
| Economy | `ParkingCompanionSystem` (keep a 10-line legacy prefab registration); `ParkCompanionSystem` direct `CreateEntity` path | 349 + ~120 | manifest-emitted companion (park types only) |
| `NetFit.cs` | cut-back model | 77 | `NetTopology` (port checked against the game) |

In total about **2,300 lines of Game**, **2,800 of Core** and **1,850 of tests** go. The additions are smaller:
- `NetTopology` (~600);
- `BuildManifest` + `DefinitionWriter` (~700, mostly moved code);
- `ErrorSuppressionSystem` (~80);
- `ShopPrefabSystem` + shop row planner (~600);
- `LooseMigrationSystem` (~200);
- the bench (~3,000, test-only).

**Kept:**
- `GroupRegistrySystem` (minus the owner parts), `GroupRemovalSystem`, `RemovalPlanner`, `ReceiptCodec`;
- `SiteSafety` (with real lot polygons + node reach);
- `Declutter` (driven by captured collision boxes);
- terraform/paint;
- `SurfaceCloneSystem`;
- `DecalOpacitySystem` (09 fixes);
- `ParkingEconomySystem` (reads the tag, not the owner chain);
- `ZoneBlockPredictor`, `RoadPreZone` (Roads type).

---

## 11. Risks and how each is tested

| # | Risk | Test (offline first) | Passive in-session evidence | Fallback |
|---|---|---|---|---|
| K1 | Error suppression races with Anarchy or another mod, so a verdict slips through | bench: real `ValidationSystem` + our suppression system, verdict count 0 and flags restored; unit test of the snapshot/restore | `verdictsSeen` = 0 in every audit | pre-Apply guard strips `Warning`/`Override` (§3.3) |
| K2 | Stall `Attached(edge)` gives no parking connection | code proof `LaneConnectionSystem.cs:471-504`; bench with `LaneConnectionSystem` allow-listed; prefab flags from the capture (AttachSystem) | `parkingLanesConnected == stalls` | setting: carrier for markings only (today's proven model) |
| K3 | Shop clones get no companies, or show an "empty" notification | code read of property/renter systems for `SignatureBuildingData`; bench: `ZoneCheckSystem` does not condemn; `RoadConnectionSystem` connects | renters after 2 in-game minutes | the building still stands (goal 2 holds); "vanilla growables only" setting |
| K4 | Removing `Owner` from legacy props crashes | bench: legacy-shaped entities in the bench world + the real `LooseMigrationSystem` from `Placemaker.dll`; ParkManager precedent | migration log counts | skip step 2 per group on any exception; keep the props owned |
| K5 | Plan-time clearance diverges from the game's collision masks, so props get hidden | G2 with the real `OverrideSystem` on captured `ObjectGeometryData` | `hiddenByGame` = 0 | none needed beyond the planner fix; the audit names each prop |
| K6 | Prefab numbers assumed (node offsets, widths, crosswalks) | capture (§9.4); UNCALIBRATED label until then; port differential | folds per audit (`CheckGeometryShape`) | margins +0.5 m in R1 until calibrated |
| K7 | Repairs (§4.1 step 5) strip lots bare | matrix metrics: stalls per 1,000 m², shops per frontage, baseline diff with an explicit `--accept` | — | plan issue names every drop before Build |
| K8 | Dezone hits an occupied cell, so a building is condemned | G3 (real `CellCheck`+`ZoneCheck`); capture `Occupied`; per-cell quads 1 m around cell centres | condemned = 0 in the follow-up | skip dezone near any building lot (+8 m) |
| K9 | Terraform is permanent and an exception after it leaves bare ground | all checks before terraform; exception rate tracked | audit on failure (new: failed builds write an audit too) | accept, stated in the Review panel |
| K10 | Ghost emission or catalog scans hang the game (09) | G10; 09 fixes 1–5 are a dependency of WP2/WP5 | frame watchdog lines | ghost holds during rapid switching (09 fix 4) |
| K11 | Attaching to city streets folds the city's own split pieces | G1 includes the split city pieces (R10) | folds list names city edges | snap to the existing node |
| K12 | Bench fidelity (Burst floats, archetypes, single-thread jobs) | A1–A3 self-tests, archetype capture, A6 calibration ≥ 95 % | replay diffs | treat uncalibrated verdicts as a screen only |
| K13 | Old saves reference runtime prefabs whose systems were deleted | `LegacyPrefabRegistry` unit test: every name ever written by a release is registered | load log: 0 missing Placemaker prefabs | — |

---

## 12. Work packages

Each WP applies to **every type that shares its path** and lists them. Acceptance is checked offline: L0 tests plus bench
gates, never a game launch. Install order: WP1 → (WP2 + WP3 + WP4) as the first install → WP5 → WP6 with the first
install that drops the owner lot (WP3 and WP6 must ship together).

### WP1: the bench becomes the gate, plus the prefab capture
- **Goal:** the offline acceptance gate of §9, and the read-only capture that removes the assumed prefab numbers.
- **Files:**
  - new `Tests/Placemaker.GameBench{,.Shim,.ShimGen,.Host}/`, `Tests/Placemaker.Tests/GameBenchGate.cs`;
  - new `Game/Diagnostics/BenchCaptureSystem.cs`;
  - `.gitignore` (`.shim/`, outputs).
- **Approach:** port `.forensics/bench` (06 §4) into projects; add the fixtures S1–S8 × T1–T3; allow-list the systems in
  §9.2; gates G1–G10 as a report plus exit code; `Host replay <audit.json>`.
- **Acceptance:**
  - A1–A3 self-tests pass;
  - full matrix ≤ 6 min, smoke ≤ 30 s;
  - a **red baseline** at HEAD is frozen and reproduces 06 §5 (104 plans with InvalidShape; plaza 1,068 → 712 cells);
  - the capture writer has a unit-tested serializer, round-tripped by the bench `PrefabFixture`.
- **Types:** all. **Depends on:** nothing.

### WP2: `BuildManifest` + one `DefinitionWriter` (preview == result)
- **Goal:** a single immutable description that both the ghost and the build write; no build-time decisions; census audit.
- **Files:**
  - new `Core/Build/BuildManifest.cs` and `Core/Build/ManifestCensus.cs`;
  - new `Game/Build/DefinitionWriter.cs`;
  - edit `BuildService.Emit.cs` (becomes the writer), `BuildService.Ghost.cs` (no instant path, no sampling, no
    Optional, 200 per frame), `BuildService.cs` (§3.1 steps), `BuildService.Audit.cs` (census), `PlanWork.cs`
    (seeds from the plan);
  - move `CourseFitting.Fit` and `AreaRings` repair into manifest creation.
- **Approach:** §6. The manifest hash becomes the ghost revision.
- **Acceptance:**
  - L0: the same plan gives the same manifest hash twice, and ghost and build manifests are equal for 292 plans;
    `PlanWork` seeds do not depend on group ids;
  - L1/L2: **G4 = 100 %** on the whole matrix (permanent emission through the real writer);
  - no `CourseFitting` call left in `Game/`.
- **Types:** all (every build and ghost). **Depends on:** WP1 for G4.

### WP3: loose entity model, errors off at the source, Delete build
- **Goal:** the PLT-style build that cannot be refused; no owner coupling.
- **Files:**
  - new `Game/Build/ErrorSuppressionSystem.cs`, `Game/Tool/PlacemakerToolSystem.PickBuild.cs`;
  - edit `BuildService.cs` / `Adopt.cs` (tagging, stall `Attached(edge)`, guard), `GroupRegistrySystem.cs`,
    `GroupComponents.cs` (v3), UI build list;
  - delete the files in §10 rows 1–7.
- **Approach:** §2, §3, §7. The carrier for markings stays behind a setting until K2 is confirmed.
- **Acceptance:**
  - L0: no code path adds `Owner` to a member except the marking fallback (a reflection test on the emitter output in the
    bench); suppression snapshot/restore test;
  - bench: G2 = 0 hidden on all plans; K1 bench test (verdicts 0 with the real `ValidationSystem`, flags restored);
  - grep gate: no `HarmonyPatch` in `Game/Groups`.
- **Types:** all builds. **Depends on:** WP2.

### WP4: `NetTopology` and parking layout rules (no folds anywhere)
- **Goal:** G1 = 0 on the whole matrix.
- **Files:**
  - new `Core/Build/NetTopology.cs`, `Core/Build/NodeCutback.cs` (port);
  - edit `Core/Layers/Parking/PlanBuilder.cs` (SplitAtJunctions → NetTopology, `AlleyStub` P7, P1–P6, P9),
    `LayoutEngine.cs` (P2/P3), `LotGrading.cs` (R11), `Mall*`/`StripMallLayer`/`OutdoorMallLayer` (lane T spacing, stubs,
    `RemoveCrosswalk`), `Composition.cs` (run the pass last);
  - replace `NetFit.cs` and `MallNetGeometryTests` with all-role, all-site tests.
- **Approach:** §4.
- **Acceptance:**
  - the port differential vs the real `GeometrySystem` + `CheckGeometryShape` has **0 disagreements** on 10,000
    junctions;
  - **G1 = 0** for all ~49 types × presets × S1–S8 × T1–T3 (03's list: parking, stripMall, shoppingPlaza, outdoorMall,
    mallPark, mallDistrict, retailPark, highStreet, shopsPark, plus roads, restArea, park, amphitheatre, botanicalGarden,
    beach, hikingTrail, lookoutHill and transitPlaza from 06 §5);
  - G9 = 0;
  - capacity metrics no worse than the baseline by more than 10 % (K7), or explained by plan issues.
- **Types:** every type with nets. **Depends on:** WP1 (WP2 for courses in the manifest).

### WP5: shops as placed buildings; zoning reduced to vanilla data
- **Goal:** what the preview shows as a shop exists after Build, faces our alley and never steals a city building.
- **Files:**
  - new `Game/Economy/ShopPrefabSystem.cs`, `Core/Layers/Commercial/ShopRow.cs`, `Core/Layers/Commercial/Templates.cs`;
  - edit every commercial layer (`StripMallLayer`, `ShoppingPlazaLayer`, `HighStreetLayer`, `OutdoorMallLayer`,
    `MallDistrictLayer`, `MallParkLayer`, `RetailParkLayer`, `ShopsParkLayer`, `CommercialLayer`), `StreetShops.cs`
    (shop rows → buildings), `ShopsAddOn`/street-shop styles, `RoadPreZone`, `SiteCapture.cs` (record `m_Zone` +
    `Occupied`), overlay legend;
  - delete §10 "Zoning machinery" and "Lot-first leftovers".
- **Approach:** §5. Pick clone recipe (a) or (b) by code reading, and record why.
- **Acceptance:**
  - G5 (every shop exists, connects to the intended course, not condemned);
  - G3 (0 condemned, 0 occupied cells rezoned);
  - G6 when optional zoning is on; G8;
  - no `ZoneGuard`/`Cutouts` symbol left;
  - the clone registration does not trigger a catalog scan (unit test on the catalog recheck filter, 09 fix 1).
- **Types:** all 8 commercial types, parks/plazas with the street-shop styles, Roads (zone ops). **Depends on:** WP2,
  WP3 (shop role, tags), WP4 (alley geometry).

### WP6: migration and save safety
- **Goal:** old saves load, old builds become loose and deletable, and nothing is lost.
- **Files:**
  - new `Game/Groups/LooseMigrationSystem.cs`, `Game/Groups/LegacyPrefabRegistry.cs`;
  - edit `GroupComponents.cs` (v3), `GroupRemovalSystem.cs` (legacy roles).
- **Approach:** §8.
- **Acceptance:**
  - L0: serializer round trips v1/v2/v3; migration planner tests over synthetic group snapshots (owner lot, carrier props,
    blockers, markings);
  - bench: migration over legacy-shaped entities built with the real archetypes leaves 0 props with a carrier `Owner`
    and 0 owner lots, and removal of a migrated group removes all members incl. legacy blockers;
  - registry test: every runtime prefab name in any shipped remembered list is registered.
- **Types:** all saved groups. **Depends on:** WP3 (ships with it).

### WP7: passive evidence and calibration
- **Goal:** each session the user plays anyway calibrates the bench and proves the install, without changing the city.
- **Files:** `BuildService.Audit.cs` (fields of §9.5), new `Game/Diagnostics/AuditFollowUpSystem.cs` (2-minute shop
  check), `Game/Diagnostics/FoldProbe.cs` (`CheckGeometryShape` by reflection), build hash in every audit (`Placemaker.csproj`
  `InformationalVersion`).
- **Approach:** read-only queries after Settle and after the ghost settles. No definitions, no Temp.
- **Acceptance:**
  - L0: audit JSON schema tests;
  - bench `Host replay` on synthetic audits;
  - after the first user session, A6 calibration ≥ 95 % or a written list of disagreements. This is the only acceptance
    item that needs data from the user's session, and it is collected passively.
- **Types:** all. **Depends on:** WP2 (manifest hash), WP1 (replay).

---

## 13. Proven vs hypothesis

**Proven** (cited above):
- Apply ignores `Error`.
- `Warning` → `Deleted` and `Override` → `Overridden` are the only Apply verdict effects.
- Disabled error types are dropped before any verdict.
- `OverrideSystem` exempts only a shared top-level owner and also checks nets and areas.
- Parking connections accept an `Attached` parent.
- Signature prefab data skips the zone check and abandonment, and occupies its cells.
- Buildings connect to the nearest edge within 8.4 m of their front.
- Vanilla dezone = `Dezone|Overwrite` on Visible cells.
- `ZonesDisabled` and `RemoveCrosswalk` are vanilla side flags.
- The fold rule and its numbers (03).
- Real game systems and `Placemaker.dll` run headless (06).
- The owner lot never caused a refusal (01, 05).

**Hypotheses, each with its settling check:**
- K1 suppression ordering with Anarchy: bench + audit `verdictsSeen`.
- §2.2 (a–c) the stall `Attached(edge)` connection: capture flags + bench `LaneConnectionSystem` + audit.
- K3 companies in shop clones: audit follow-up.
- The additional allow-listed systems run headless: WP1.
- Top-level permanent nets behave like Temp-applied ones: only needed if §3.4 is revisited.
- Removing `Owner` from legacy props is safe: bench migration run + ParkManager precedent.
