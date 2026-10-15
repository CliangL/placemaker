# Design C: direct world writes and minimal game coupling

Design angle: the strongest guarantee that **the preview equals the result** and that **nothing is ever refused**, while
the city stays safe. The method is to write final entities through the game's own permanent spawn path, to keep the
game's tool/validation pipeline only where the game does work we must not re-implement (joining city roads), to make shops
real buildings instead of zoning, and to prove everything offline by running the game's own code on the bench from
teardown 06.

Inputs: teardowns 00–10 in this folder (all read), master `650be0f` / HEAD `d9f6df6`, decompiled 1.6.2f1
(`%TEMP%\pm-decomp\game`, cited as DECOMP), ParkingLotTool (PLT). No game was launched and nothing was installed.
Labels: **[P]** proven (the code, log or bench output is cited); **[H]** hypothesis, with the gate or audit that settles it.

---

## 0. The design in one page

| Goal | Mechanism | How the AI lead proves it offline |
|---|---|---|
| 1. A parking lot or strip mall **always builds**, with no error dialogs | There is no validation gate anywhere. **Bulk pieces** (surfaces, areas, props, decals, stall markings, shop buildings, blocker lots) are written as `CreationDefinition` with `CreationFlags.Permanent`. That is the path the game uses for every grown building: no `Temp`, no `ValidationSystem`, no Apply. **Nets** go through one small Temp transaction with no error gate. The game's Apply never reads `Error`, so nets cannot be refused either. Plan-time drops are shown in the preview, so they are not refusals. | G-EQ-1: the bench feeds the real definitions to the real `Generate*` systems and checks that every planned piece exists. |
| 2. **Preview == result**, shops included | One `BuildProgram` (the ordered list of definitions with seeds) is written by one `DefinitionWriter` in two modes: *ghost* (everything Temp) and *build* (nets Temp, the rest Permanent). The same generator systems run on the same inputs, so they produce the same entities. Shops are **real buildings** (signature-flagged runtime clones of vanilla commercial growables), so the ghost shows the actual building. Every system that changes things *after* creation (OverrideSystem, zone cells, road connection, terrain flattening) is gated offline to have nothing to do. | G-EQ-1 (ghost census == build census == plan), G-OBJ-2 (OverrideSystem hides nothing), G-SHOP-1 (real `RoadConnectionSystem` connects each shop to the road it faces). |
| 3. City buildings are **never destroyed** | Bulk writes never pass through Apply, so there is no `Warning → Deleted`. The net transaction strips `Warning` from city entities before Apply. Plan-time `SiteSafety` keeps us off building lots. A condemnation gate keeps our nets and blockers off the zone cells of existing growables. | G-CITY-1 (the city fixture is unchanged except planned joints and clearings), G-ZONE-1 (no existing growable's lot cell changes state). |
| 4. **Easy removal** | Loose top-level pieces, each tagged `PlacemakerMember` (the registry exists today). "Remove build" sits on the info panel of *any* piece and in the panel's build list. The vanilla bulldozer still removes single pieces, as the user asked. The owner lot, the prop carrier and both Harmony patches are deleted. | Bench ECS test: build → remove → the city fixture is identical, apart from terrain. |
| 5. **Verifiable offline**, before the user sees it | The 06 bench, productised, runs the real `GeometrySystem`, `ValidateEdge`, `ValidateObject`, `Generate{Nodes,Edges,Objects,Areas}System`, `CourseSplitSystem`, `OverrideSystem`, `BlockSystem`, `CellCheckSystem`, `RoadConnectionSystem` and the mod's own `DefinitionWriter` over a type × preset × site × terrain matrix. A one-time read-only prefab capture rides on a session the user starts anyway. | §9 gates. "Fixed" may be said only when the full matrix rows of every type that shares the changed code path are green. |

**What gets deleted** (§10): about 5,000 lines. That covers the verdict machinery, the owner lot, the prop carrier owner,
both Harmony patches, the whole zoning-cutout/refresh/zone-block-retry chain, the instant-from-ghost path, the Optional
flags, the disabled parking companion, lot-first leftover lots, and the parking sub-plan's vanilla "Agriculture Lot" area.

**What the user sees.** The preview shows the real parts: roads, aisles, stall lines, trees and the shop buildings
themselves. Anything the planner could not fit is simply absent from the preview, with a calm note ("2 bushes left out:
too close to the aisle"). Build takes about 2–4 s and produces exactly that. No red, no failure dialog. Any piece's info
panel offers "Remove this build".

---

## 1. Ground truth this design stands on

### 1.1 Proven (code or measurement)

| # | Fact | Evidence |
|---|---|---|
| F1 | A `CreationDefinition` with `Permanent` creates the final object: no `Temp` component, no `Animation`, so nothing for ValidationSystem or ToolApplySystem | DECOMP `Game.Tools/GenerateObjectsSystem.cs:970` (`flag2 = Permanent ∥ …`), `:1128`, `:1278` (`if ((m_Flags & Permanent) == 0) AddComponent(temp)`) |
| F2 | The same flag is honoured for nodes, edges and areas | `GenerateNodesSystem.cs:659`; `GenerateEdgesSystem.cs:1291`; `GenerateAreasSystem.cs:224, 261, 384, 405` |
| F3 | The game spawns its own growables this way: definition archetype `{CreationDefinition, ObjectDefinition, Updated, Deleted}`, flags `Permanent \| Construction`, created in GameSimulation and consumed by Modification1 in the same frame | `Game.Simulation/ZoneSpawnSystem.cs:622-670`, `:1090`; `AreaSpawnSystem.cs:777` |
| F4 | A permanent definition can carry an owner and an attachment: `m_Owner` becomes `Owner`, and `CreationFlags.Attach` + `m_Attached` becomes `Attached`. Both are present from the first frame | `GenerateObjectsSystem.cs:1248`, `:1265`. PLT's background builder does exactly this (`ParkingLotTool/Tools/ParkingLotDefinitionsausgabe.cs:22-60`: `m_Flags \|= Permanent; m_Owner = owner; Attach`) |
| F5 | A permanent course can end on an existing **node** by entity | `GenerateEdgesSystem.cs:1256-1262` (`TryGetNode`: `isPermanent && NodeData.Has(coursePos.m_Entity)`) |
| F6 | A permanent course never takes over an existing edge as its original (`FindOriginalEdge` skips `Permanent`), so splitting a city road mid-edge is the Temp tool path's job | `Game.Tools/CourseSplitSystem.cs:3857` |
| F7 | The game's Apply never reads `Error`. It turns `Warning` into `Deleted` and `Override` into `Overridden`, and drops `Cancel` | 05 §1.3 (`ToolApplySystem.cs:50-71`, `ApplyObjectsSystem.cs:337, 670-686`) |
| F8 | `OverrideSystem` runs on permanent objects. It hides an overridable object that collides, but skips pairs that share a top-level (non-building) owner (`OverrideSystem.cs:344-350`). Areas hide objects only with `CanOverrideObjects` (`:1439`), which our Zone Block lot clears (`Game/Groups/OwnerAreaPrefabSystem.cs:115-117`) | DECOMP + repo |
| F9 | `Created`/`Updated` are removed in the Cleanup phase, so a system in ModificationEnd sees the entities created that frame | `Game.Common/SystemOrder.cs:54`, `CleanUpSystem.cs:26-53` |
| F10 | Only `RoadPrefab` edges can host buildings (`ConnectedBuilding` buffer), and `RoadConnectionSystem` connects a building to the nearest such edge from its front position | `Game.Prefabs/RoadPrefab.cs:68, 75`; `Game.Buildings/RoadConnectionSystem.cs:343-372` |
| F11 | A **signature** building is exempt from zone checks and is never condemned | `Game.Buildings/ZoneCheckSystem.cs:52` |
| F12 | A signature building is level 5 (no level-up replacement), never abandoned, and gets its commercial company **created directly**. `BuildingProperties` puts `PropertyToBeOnMarket` on the new building; `PropertyProcessingSystem`'s signature branch creates the company whose output equals `m_AllowedSold`. Companies also rate signature properties +5000 | `Game.Prefabs/SignatureBuilding.cs:13-49` (`kStatLevel = 5`); `BuildingUpkeepSystem.cs:278`; `Game.Prefabs/BuildingProperties.cs:55-81`; `Game.Simulation/PropertyProcessingSystem.cs:137-150`; `Game.Buildings/PropertyUtils.cs:277-280` |
| F13 | A condemned growable is un-condemned when its cells become valid again | `ZoneCheckSystem.cs:299-305` |
| F14 | Placing XP is granted once per signature prefab, from `PlaceableObjectData.m_XPReward` | `Game.Simulation/XPBuiltSystem.cs:52-66` |
| F15 | Nets fold (`InvalidShape`) only from geometry. Applied anyway, the fold is permanent. Lot nets fold on ordinary sites, and terrain splitting adds more folds | 03 §2–§4; 06 §5 (104/292 plans) |
| F16 | Today's shop loss happens in the preview-vs-live zone-cell prediction; even correct cells grow nothing without demand | 04 §5–§6; 06 F2/F3; 07 §3(a); 08 §3 |
| F17 | The game's ECS, systems, jobs and validation run headless on .NET 9 after the icall rewrite, along with the mod's own DLL | 06 §1 (run-all.log: 80 OK) |

### 1.2 Hypotheses this design depends on (each has a gate or a passive audit)

| # | Hypothesis | Settled by |
|---|---|---|
| H1 | Permanent objects, areas and buildings made from **our** definitions behave in game exactly like the ones the bench creates (archetypes, Burst floats) | G-EQ-1 on the bench + live census in every user build audit (§6.4) + archetype capture (06 §8.1) |
| H2 | Signature clones of commercial growables get a company, connect to our lane and render like the original | G-SHOP-1/2 on the bench (real `RoadConnectionSystem`, ported company rule); deferred audit "renters" at +5 game-minutes in the user's next session |
| H3 | The net transaction (Temp, no gate) can be followed in the next frames by permanent writes that reference its node and edge entities | Bench: allow-list `ApplyNetSystem`/`ApplyObjectsSystem` (06 §6.4 names them as candidates); in-game audit |
| H4 | The game builds a carrier-owned and carrier-attached permanent stall marking into a working `ParkingLane`, the same as today's tagged Temp | Bench: lane census under the carrier; PLT already ships it (F4) |
| H5 | A save without the mod loses the runtime clones (shops, overlay surfaces, decal tiers, blocker lots) | Documented limitation + "Prepare save for uninstall" (WP7) |

---

## 2. Entity model

| Thing | How it is written | Owner / attachment | Tag (`PlacemakerMember`) | Notes |
|---|---|---|---|---|
| Group **record** | `EntityManager.CreateEntity` (as today) | – | record (`PlacemakerGroup` v3: `+Model = 2` "direct", `+Flags.Building` serialised) | Receipt blob (recipe + plan) unchanged |
| **Nets** (roads, lanes, alleys, aisles, connectors, paths, net fences) | **Net pass**: `CreationDefinition + NetCourse (+Upgraded)` → Temp → Apply, **no gate** | none (top-level) | Edge/Node, tagged on the Temp before Apply (the proven in-place mechanism, `BuildService.cs:5-9`) | The only place the game's tool path is kept: it joins city roads, merges nodes and applies upgrades |
| City **joints** (our stub splits a city street; upgrades on city edges) | inside the net pass | – | the split city pieces stay city content; our stub is tagged | v1 plans **no** upgrades on city edges (§5.5) |
| **Clearing** existing trees/props in the outline | vanilla `CreationFlags.Delete` definitions in the net pass (moved from `BuildService.Owner.cs:253-288`) | – | – | Shown hidden in the ghost the same way |
| **Surfaces** (asphalt, paving, grass) | **Permanent** area definitions (overlay clone −94 where paved, as today) | none | Area/Surface | Rings pre-validated against the real `Areas.GeometrySystem` (G-AREA-1) |
| **Areas** (pedestrian/hangaround) | Permanent | none | Area | The parking sub-plan's `AreaRole.Lot` is **removed** (07 F1) |
| **Parking carrier** | `EntityManager.CreateEntity` (as today), with `SubObject/SubNet/SubArea` buffers | – | Carrier | `SubNet` is filled with the aisle edges **after the net pass and before** the stall markings are written |
| **Stall markings / chargers / arrows** | Permanent, `m_Owner = carrier`, `Attach`, `m_Attached = carrier` | Owner + Attached = carrier (PLT model, F4) | ParkingBay | They share a top-level owner, so OverrideSystem never pairs them with each other (F8) |
| **Props, trees, decals, lamps, benches** | Permanent | **none** (loose) | Object | Plan-time declutter against nets, buildings and each other, verified with the real `ValidateObject` (G-OBJ-1) |
| **Shop buildings** | Permanent `ObjectDefinition` of a **signature clone** of a vanilla commercial growable, without `Construction` (it appears finished) | none | Object/**Shop** (new role 11) | §5 |
| **Zone blocker** ("Placemaker Zone Block", physical, `CanOverrideObjects` cleared) | Permanent area, one ring set = footprint minus protected land | none | Auxiliary/ZoneBlocker | No retry rounds and no separate transaction |
| **Park companion** (hidden park building for park stats) | unchanged (`ParkCompanionSystem`) | – | Companion | Out of scope; kept |
| Terraform / paint | direct terrain API (as today) | – | `GroupFlags.PermanentTerrain` | Runs before every write. Not undone (unchanged limitation, §11 R10) |
| ~~Owner lot "Placemaker Area"~~ | **deleted** for new builds (legacy groups keep theirs, §8) | | | |
| ~~Props owned by the carrier~~ | **deleted** (C2–C5 in 05 §1.5) | | | |
| ~~Zoning cutouts / Marquee zoning~~ | **deleted** (shops are buildings) | | | Roads-type pre-zoning is kept, out of scope |

Ownership principle: **only one technical owner remains, the bare parking carrier for stall markings.** Without it cars
do not find the stalls (`LaneConnectionSystem.FindParkingConnection`, 05 §3.3). Everything else is top-level vanilla
content found through the tag, as ParkManager does.

---

## 3. Build path

### 3.1 One program, one writer

* **`Core/Build/BuildProgram.cs`** (pure, replaces most of `PlanWork.Flatten`). This is the ordered list of write
  operations with **all** randomness resolved: per-op random seed, resolved prefab id, final position/rotation/curve,
  node heights, owner role, and pass (`Net` or `Bulk`). It is deterministic in (plan, catalog version, group seed).
* **`Game/Build/DefinitionWriter.cs`** (new, about 400 lines, mostly moved from `BuildService.Emit.cs`). It writes ops
  as `CreationDefinition`s and depends only on `EntityManager` and the prefab lookup, so the bench can load and call it.
  Modes:
  * `Ghost`: every op as Temp (nets, bulk, clearing deletes). No `Optional` flag ever.
  * `NetPass`: net ops + clearing deletes as Temp.
  * `Permanent`: bulk ops with `CreationFlags.Permanent`, plus `Deleted` + `Updated` on the definition (the F3 archetype),
    so the definition is consumed in the same frame and never lingers.

Preview and build differ **only in the mode**, never in the inputs. That is the core of "preview == result" (§6).

### 3.2 State machine (replaces `BuildService.cs:62-67` and `:283-760`)

```
Idle
→ Prepare        program = BuildProgram(plan); resolve prefabs; price; affordability (preview already showed it)
→ [WaitReplaced] edit = remove the old group first (unchanged)
→ Terraform      direct brush, as today (BuildService.Terrain.cs), then wait for the heightmap
→ Paint          as today
→ NetPass.Emit   activate BuildToolSystem; 1 Clear frame; DefinitionWriter(NetPass)
→ NetPass.Tag    wait for the Temp census (≤180 frames, existing PlanIndex matching); tag PlacemakerMember on our Temps
→ NetPass.Apply  StripWarningsOnCity()  (every non-Temp entity loses Warning: nothing can be demolished)
                 _tool.Requested = Apply   (no Error check; F7)
→ NetPass.Settle temps gone → map plan key → live edge/node entity; carrier.SubNet ← aisle edges;
                 restore the player's previous tool (the rest needs no tool)
→ Bulk.Write     DefinitionWriter(Permanent), ≤ 400 ops/frame, in this order:
                 surfaces → areas → blockers → stall markings (owner/attach carrier) → props/decals → shops
→ Bulk.Census    CreationCensusSystem (ModificationEnd, F9): match Created & !Temp entities to ops by prefab + position
                 (≤ 5 cm) and tag them. Done when matched == expected, or 30 frames stable
→ Charge         money: the price the preview showed, minus what ToolApplySystem already charged for the net pass
→ Verify         +60..120 frames: live census of every member; Overridden members; Condemned buildings within
                 outline + 64 m; each shop's Building.m_RoadEdge; terrain bowls next to our nets (07 F9)
→ Finish         audit JSON (live counts only, §6.4); clear Flags.Building
```

Removed steps: `Validate`, `ResetForRetry`, `OwnerLot`, `Apply` (bulk), `Settle` adoption of strays, `ZoneRefresh`,
`ZoneBlock` (its own transaction), `ZoningWait/Emit/Apply`, and the instant-from-ghost branch (`BuildService.cs:380-390`).

### 3.3 Why nets keep a Temp pass (and why that is not a refusal risk)

* The game has to split city edges where an alley or road stub joins them. A permanent course does not do this (F6), and
  re-implementing it is the raw-write path 05 ranks last.
* Nets are few (14–200 per build, versus 300–2,000 bulk pieces). Every refusal and hide that happened was on objects and
  on the retry loop, never on Apply itself (02 §3.2).
* The pass has **no gate**: Apply builds errored temporaries (F7). Nothing is `Optional`, so nothing is cancelled. The
  ghost uses the identical Temp path, so the nets the user saw are the nets that get built.
* City safety in this pass: `StripWarningsOnCity()` (about 15 lines, kept from s59's `NeutralizeVerdicts`, restricted to
  non-Temp entities), plus `SiteSafety` at plan time, plus G-CITY-1 on the bench.
* **v2 option, gated, not v1:** internal nets that touch no city entity could also go Permanent (F5 lets them end on the
  net pass's nodes by entity; invisible paths reach roads through LocalConnect, which permanent creation supports,
  `GenerateEdgesSystem.cs:213-217`). That would remove the last validation coupling. Switch it on only if G-EQ-1 shows
  identical lanes and connectivity on the full matrix.

### 3.4 What can still go wrong, and what happens

| Event | Before the net Apply | After the net Apply |
|---|---|---|
| A prefab is missing | impossible: resolution happens at plan time, and the preview shows the substitute or omits the piece | – |
| The player switches tools | temps cleared, nothing built (terraform stays, unchanged limitation) | irrelevant: bulk needs no tool |
| An op does not materialise | – | the census lists it; **one** re-emission of the missing keys (materialisation recovery, never a refusal loop); still missing → audit entry `notMaterialised` with prefab + position, counted as a planner/bench defect |
| An exception | cleanup temps | roll back the group through `GroupRemovalSystem` (as today) |
| Save mid-build | – | `Flags.Building` is serialised; on load the group is rolled back and the panel says so once |
| Not enough money | the preview disables Build and says why; this is not an error dialog after the click | – |

---

## 4. Nets: InvalidShape

### 4.1 Why it no longer blocks, and why it still matters

It no longer refuses anything: there is no gate (F7, §3.3). But a fold is **permanent geometry**. It shows as twisted
asphalt on visible nets (`Alley`, `Pavement Path`), distorts node lanes, terrain flattening and zone blocks, and can
fold the **city's** street where our stub splits it (03 §4). So the planner must produce valid geometry, and the bench
proves it with the real `GeometrySystem` + `ValidateEdge` (G-NET-1). Any fold the ghost still shows in game is logged
passively as a planner defect with its position (§6.4).

### 4.2 `Core/Build/NetTopology.cs` — one pass over every type's nets after composition

It runs in `Composer.Compose` after `PathJoins`, for every type: parking, commercial, parks, plazas, roads. Inputs:
planned nets plus `PrefabFacts` (§9.3: half width `h`, node offset `n`, crosswalk depth `cw`, merge layers, edge length
range). Until the capture exists, it uses defaults `n = 0.5 m`, `cw = 2 m` at street T's and widths from the catalog
fixture, and the bench stamps results UNCALIBRATED.

1. **Weld.** Build a planar graph. Endpoints within `r_w = max(1.0 m, L_min(prefab))` become **one node with one 3D
   position**. `y` is sampled once from the graded target surface (R7: `GenerateNodesSystem` merges only bit-identical
   positions, 03 §2.4).
2. **Cross.** Every intersection of two nets that share connect layers gets an explicit shared node (R3; the game never
   joins two new courses, 03 §2.4). Nets that must not connect (a fence over a path) must stay `h_i + h_j + 0.5 m`
   apart, or the lower-priority one is dropped.
3. **Cut-back.** Per node and incident edge:
   `cut = max_k (h_k + n + cw_k + |s|·w/4 + h_i·cos φ) / sin φ`, floored by `n` on a prefab change, by `|Δslope|·w`
   when that exceeds 0.2 m, and by `m_MinNodeOffset` when the join is not straight (03 §2.3).
4. **Length rule (R1).** Every edge needs `len ≥ cut_start + cut_end + 0.2 m + 0.5 m`. Violations are resolved in this
   order:
   * (a) merge a straight degree-2 continuation of the same prefab;
   * (b) slide a T along its host line to align aisle ends (PLT's `PlaneGemeinsameEnden`, 03 §5);
   * (c) collapse the edge into one node if the resulting angles pass R2;
   * (d) drop the lower-priority piece (footpath > cross lane > nothing else), with a PlanIssue the preview shows.

   Iterate to a fixpoint, at most 8 rounds.
5. **Angles (R2).** T/X junctions 60°–90°. Straight joins are exactly straight (`dot ≥ 0.9995`). Never 165°–178°
   between different widths.
6. **Curves (R9).** Centre radius `≥ h + n + 1 m`.
7. **Entrances (R8, R10).** Alley/road stubs into a street get `"left:RemoveCrosswalk","right:RemoveCrosswalk"` (PLT)
   and `len ≥ h_street + n + transition + 0.5`, which is 7.5 m for a 12 m street. The attach point must lie ≥ `cut`
   from every existing node of that street, or snap to that node. Invisible car/pedestrian paths never attach: they end
   0.5 m short of the kerb and join by LocalConnect (RESEARCH-NOTES 15).
8. **Emit (R5, R6).** One course per graph edge. **No terrain splitting inside graded lots** (lot tiers are planar, so
   the profile comes from node heights). Elsewhere, split only at points ≥ `cut + 2 m` from both ends, with pieces
   ≥ 6 m. `CourseFitting`'s 0.35 m / 6 m splitting is deleted for lot nets (03 §3.3 C). A piece shorter than 1 m never
   reaches the writer (resolved in 4).

Minimum straight lengths at 90° (n = 0.5 m, before the 0.5 m safety) that the parking and commercial layouts must
respect (03 §6):

| Piece | Between | Min length |
|---|---|---|
| `Invisible Road Path - 2xTwoway` aisle | two aisle T's | 8.2 m |
| `Invisible Car Path - 1xTwoway` cross lane | two aisle T's | 8.2 m |
| aisle | two footpath T's | 3.2 m |
| `Alley` stub | T on an Alley + join into an aisle | ≈ 5.2 m (today 4.0) |
| `Alley` stub | 12 m street + join into an aisle, crosswalk removed | ≈ 7.2 m (today 6.0) |

### 4.3 Parking layout rules (PLT's, applied in `LayoutEngine`/`PlanBuilder` for every Parking-planner host)

* A cross lane only where a section carries ≥ 5 bays. Never an end section under two bays (PLT `Planung.cs:611-615`).
* Aisle ends aligned whenever that costs no bay (removes the "jog" pieces of 03 §3.3 A).
* The ring jog at concave corners becomes one node (rule 4c above), never a 1.6 m piece.
* The lot's entrance is an invisible-path LocalConnect end, or an Alley stub that meets the §4.2 lengths. A lot without
  an accepted entrance is **not a lot** (07 R4); its land becomes planting.

---

## 5. Shops: real buildings, no zoning

### 5.1 Why

Zoning cutouts cannot be predicted before Apply: Temp roads get no zone blocks, older blocks win overlaps, and demand
gates growth (F16, 04 §6, 08 §3). Every patch from s22 to s60 moved the loss somewhere else. A building written in the
same build either exists or does not, and the census says which. So **a shop is a building**. This applies to every type
that hosts shops: all eight commercial types, the Zoning/"shops" feature on parks and plazas (`StreetShops`,
`EdgeZoningAddOn`), and Parking's "place buildings" band (`Core/Layers/Parking/Zoning.cs`). They all go through one
placer (fix-scope rule).

### 5.2 `Core/Layers/Common/ShopPlacer.cs`

* **Input.** Frontage runs. Each run is one side of a `RoadPrefab` edge the plan builds, or, only when the user picks
  "Street front", of an existing city street. Each run has a usable depth, a zone theme (EU/NA Commercial Low/High) and
  a seed.
* **Catalogue.** `PrefabFacts.Shops`: vanilla commercial spawnables with `BuildingData.m_LotSize`, footprint bounds,
  zone and level-1 variants (captured, §9.3).
* **Packing.** Run length L in 8 m cells. A seeded DP picks lot widths (1–4 cells) and depths (2–4 cells, ≤ the depth
  budget) that fill L with ≤ 4 m slack, avoid repeating one prefab twice in a row, and keep one depth per row (a tidy
  frontage).
* **Placement.** The lot's front edge lies on the road's outer edge line (half width from `PrefabFacts`), which is the
  same line the game's zone block starts from (08 §2). Yaw faces the road (`ZoneUtils.GetRotation` semantics).
* **Output.** A new contract element `PlannedBuilding { Position, Yaw, Prefab, Zone, AllowedSold, Seed, FacingNetKey }`
  in `AreaPlan.Buildings`. Each lot rectangle is registered as a **hard reservation** for every other layer, which
  replaces `Cutouts.Enforce`.
* **Design rule (07 R2).** By default shops face **our** lane or alley, which runs between the lot and the shop row
  (strip centre: street → driveway → lot with aisles parallel to the fronts → frontage lane → shops). Street-front rows
  are opt-in.

### 5.3 `Game/Build/ShopPrefabSystem.cs` (signature clones)

* **Clone.** One clone per used growable: "Placemaker Shop – <original>". It adds the vanilla `SignatureBuilding` prefab
  component, which brings level 5 (F12) and condemnation immunity (F11), and sets:
  * `m_XPReward = 0`, so there is no XP farming (F14);
  * `m_ZoneType` = the original's zone;
  * `BuildingPropertyData.m_AllowedSold` = **one** resource picked per shop for variety. F12's company rule needs an
    exact match with a commercial company prefab, and G-SHOP-2 checks it against captured company data;
  * no UI object, so it never appears in the signature menu;
  * no `PlacementFlags.Unique`, so the ghost does not flag the second identical shop as `AlreadyExists`
    (`Game.Objects/ValidationHelpers.cs:1586-1593`).
* **Registration.** Clones are registered before a save loads, from a persisted list (RESEARCH-NOTES 1/3, the pattern
  of `park-companions.txt`). They are prewarmed at load. **Our own prefab registrations never trigger a catalog rescan**
  (09 fix 1), and clones are not catalog items. Without that, every new shop clone would cause a freeze of up to 2 s
  (09).

### 5.4 What stops the city from growing inside our build

* Our own lanes: `ZonesDisabled` on **both** sides (`PlannedNet.Upgrades`, `BuildService.Emit.cs:176-179`). Shops are
  buildings, so our roads need no zone cells at all.
* City streets whose blocks reach the footprint: a **permanent "Zone Block" area** over
  `footprint − existing building lots − every cell column holding an occupied cell − our shop lots`. A physical area
  blocks the cells it touches (08 G10). Columns that hold existing growables are left alone, so `CleanBlockedCells` can
  never cut a growable's deeper rows. That is the **condemnation guard**, checked by G-ZONE-1 and repaired after the fact
  if needed (F13: trimming the offending blocker piece un-condemns).
* `Core/Build/BlockerPlan.cs` (≈150 lines) replaces `ZoneGuard.Compute` step 3. Its inputs are the captured grids, which
  `SiteCapture` must extend with per-cell `Occupied` and `m_Zone` (04 V5, 08 §7.1).

### 5.5 City street zoning (the user's idea, 08)

v1 leaves city street zoning **unchanged**: no city edge upgrades. Shops face our lane by construction, and the blocker
handles the land under the build. The vanilla per-side `ZonesDisabled` toggle on city edges (08 §4.1) stays a
documented v2 option behind the 08 §7 guard, because it costs the whole edge side and can condemn growables outside the
build.

### 5.6 Zoning as an extra

Removed in v1. If it returns later, it comes back as "also zone this frontage so the city may add more", drawn in the
preview as land that may grow with demand, never as shops (04 §6).

---

## 6. Preview == result

### 6.1 By construction

`Ghost` and `Build` run the same `BuildProgram` through the same `DefinitionWriter`, into the same game generator
systems, with the same seeds. That makes these identical: object transforms and variations (seed), net curves and node
merges (identical Temp net path in both), area rings, and the shop building's model.

### 6.2 What could still differ, and how each is made a no-op

| Post-creation modifier | Ghost (Temp) | Build | Neutralised by |
|---|---|---|---|
| `OverrideSystem` hides colliding overridable objects | not run on Temp | runs (F8) | Plan-time `Declutter` against **nets, buildings and own objects** with captured collision geometry. Gate: the real `ValidateObject` gives 0 Override/Error among our objects (G-OBJ-1), and the real `OverrideSystem` hides 0 of our members after permanent creation (G-OBJ-2). Stall markings share the carrier owner, so they are exempt among themselves |
| Validation tint/cancel | runs | does not exist | Nothing is Optional. Ghost verdicts are **read and logged** as planner defects (§6.4), then stripped (s59's `ClearGhostVerdicts`, kept), so no red reaches the user |
| Zone cells | none on Temp | real | Shops do not depend on cells (§5). The blocker and condemnation rules are gated (G-ZONE-1/2) |
| Road connection of buildings | not computed | real | G-SHOP-1: the real `RoadConnectionSystem` connects each shop to its `FacingNetKey` road |
| Terrain flattening under nets | previewed | real | G-TERRAIN-1: every node within 0.3 m of the graded target, so the nets do not dig (07 f) |
| Area border sub-objects (the farm-lot fences, 07 c) | shown | built | G-EQ-1 lists every unplanned top-level entity. Vanilla lot areas are banned (07 F1/F2) |

### 6.3 Plan-time drops are not refusals

Declutter and NetTopology may leave a piece out. They do it **before** the preview, the preview shows the absence, and a
PlanIssue says why. The build never removes anything the preview showed.

### 6.4 Passive in-game confirmation (rides on sessions the user starts anyway; nothing in the city changes)

1. **Ghost verdict dump.** Every ghost already runs the game's validation. Each `Error`/`Override` is logged with its
   prefab, position and plan key. It should be 0. Anything else is free calibration data for the bench (06 A6). This is
   the automatic preview-only self-check.
2. **Build audit = live census.** Planned vs **live** count per prefab. Every op is matched to an entity, and none is
   `Overridden`, `Hidden` or `Temp`. It never uses `planned − leftOut` arithmetic (02 §6). Every audit also logs the
   type, preset and template id (07 F12).
3. **Deferred checks** at +120 frames and at +5 game-minutes: newly `Condemned` buildings within outline + 64 m
   (should be 0), shops with `m_RoadEdge` = the planned road, and shops with a renter.
4. **Replay section.** The plan, site capture and capture version go into the audit JSON. `Host replay <audit.json>`
   reproduces the build on the bench (06 §8.3).
5. **Prefab and archetype capture** on city load: read-only, about 1 s, once per game version (06 §8.1).

---

## 7. Removal

* **Remove build.** "Remove this build" sits on `PlacemakerGroupSection` for **any** tagged member, and in the panel's
  build list. It calls the existing `GroupRemovalSystem` (PreTool, ≤ 32 parts per pass, nodes after edges,
  `RemovalPlanner` order, waits for tool temps). No Harmony patches.
* **Single pieces.** The vanilla bulldozer removes single pieces (the user's request). The registry notices, and
  `BuiltMembers` vs live members drives the "modified" badge.
* **Joints.** When our stub is removed, the city street heals through vanilla node regeneration (existing
  `GroupRemovalSystem` node refresh).
* **What stays.** Terrain changes remain (`PermanentTerrain`, as today). Removing the blocker returns the city's cells.
* "One action removes it all" stays true (the playtest-1 rule), without an owner object.

---

## 8. Migration

* **Save format.** `PlacemakerGroup` v3 appends `Model` (1 = legacy owner-lot model, 2 = direct) and serialises
  `Flags.Building`. Old saves read as `Model = 1`. `PlacemakerMember` is unchanged.
* **Runtime prefabs.** Registration of every legacy runtime prefab stays forever ("Placemaker Area", "Placemaker Zone
  Block", decal tiers, overlay surfaces, park companions), so old saves keep resolving them. Only their creation for
  new builds stops.
* **Legacy groups are left structurally untouched.** Rewiring carrier-owned props would change what OverrideSystem hides
  in a built city, which is a visible change on load.
  * The owner lot remains a member. Clicking it shows the group section, and bulldozing it still cascades through
    `HandleDeletedMembers` (registry code, not a Harmony patch).
  * The C5 limitation (a click on a carrier-owned prop selects the carrier) remains for legacy groups only.
  * Each legacy group offers **"Rebuild with the new engine"**: preview the receipt re-planned, then remove and build.
* **Farm-field lots** (07 b). On load, untagged `area.lot` areas whose ring matches a legacy receipt's planned lot ring
  within 0.5 m are adopted into that group, so Remove takes them along. The panel offers "Remove N farm-field lots left
  by old builds" (never automatic).
* **Settings.** `AutoExcludeCollisions`, `RecoverRejectedPieces`, `IgnoreOwnCollisions` and `SinkStallMarkings` are
  dropped. Readers ignore the unknown keys.
* **Uninstall helper (WP7 stretch).** "Prepare save for removing Placemaker" swaps shop clones back to their vanilla
  prefab and Marquee-zones their lots, warning that they may then be condemned.

---

## 9. Offline bench and the acceptance matrix

### 9.1 Projects (from 06 §7.1, productised)

These live under `Tests/`, which `Placemaker.csproj` already excludes. They reference the game DLLs from `$(ManagedPath)`
with `Private=false`, keep the rewritten Unity modules git-ignored, and ship nothing:

* `Placemaker.GameBench.Shim` and `Placemaker.GameBench.ShimGen`;
* `Placemaker.GameBench.Host` (net9, resolver + neuter allow-list + commands `probe`, `capture-city`, `matrix`,
  `replay`, `golden`);
* `Placemaker.GameBench` (net48 surface, game mscorlib, compiles `Core/**`, loads the built `Placemaker.dll` for
  `DefinitionWriter` and `CreationCensusSystem`);
* `Placemaker.Tests/GameBenchGate.cs` (`[Trait("Category","GameBench")]`).

### 9.2 Real game code vs faithful ports

| Concern | Bench uses | Status |
|---|---|---|
| Net geometry and InvalidShape / ShortDistance / net overlap | real `Game.Net.GeometrySystem` + `ValidationHelpers.ValidateEdge` | runs [P] |
| Temp nets → applied (net pass) | real `CourseSplitSystem`, `GenerateNodes/EdgesSystem`, `ApplyNetSystem` | **to allow-list** (WP1) [H3] |
| Permanent objects, areas, buildings | real `GenerateObjectsSystem`, `GenerateAreasSystem` (+ `Areas.GeometrySystem` triangulation) | **to allow-list** (WP1) |
| Object collisions / hides | real `ValidateObject` [P]; real `OverrideSystem` | ValidateObject runs; OverrideSystem to allow-list |
| Zone blocks and cells, condemnation | real `BlockSystem`, `CellCheckSystem` [P]; real `ZoneCheckSystem` or its pure predicate | cells run; occupied-cell fixtures to add |
| Shop road connection | real `RoadConnectionSystem` | to allow-list |
| Shop company creation | **port** of `PropertyProcessingSystem` signature rule (F12) over captured company data | port (pure) |
| Stall → parking lane connection | real `LaneSystem` if allow-listable, else a **port** of `FindParkingConnection` reach + graph connectivity to a city road | port first |
| Terrain flattening by nets | **pure check** of node heights vs the graded target | pure |
| Our emitter and census | **real** `Placemaker.dll` (`DefinitionWriter`, `CreationCensusSystem`) | the mod already loads [P] |
| Prefab numbers (widths, node offsets, crosswalks, lot sizes, flags, archetypes) | `bench-capture-<gameVersion>.json` from the passive capture | until it exists: conservative defaults, every verdict stamped UNCALIBRATED |

### 9.3 `PrefabFacts`

One reader (Core) of the capture JSON. It feeds NetTopology, ShopPlacer, Declutter and the bench, so the planner and the
bench use the **same** numbers. A copy of the capture is committed as a test fixture per game version.

### 9.4 The matrix

* **Types × presets.** Every shipped type × every preset (292 variants today, 06 §5).
* **Sites (8).**
  * S1 rect 115²; S2 70×50; S3 160×90; S4 rotated 100×80; S5 L-shape 120; S6 irregular quad.
  * S7 block between a 4-lane avenue and a 2-lane street, with residential zoning on the avenue side (the shot-107 case).
  * S8 corner lot with existing zoned growables and one service building on two sides (condemnation and safety case).
  * Each site has city streets around it. The city fixture is captured with the real BlockSystem and CellCheckSystem.
* **Terrain (4).** T1 flat; T2 6 % slope; T3 rolling ±4 m; T4 shore edge (water depth > 0 on one side). The plan's
  terraform target is applied to the bench heightmap before emission (pure `TerraformRaster`).
* **Full matrix** = 292 × 8 × 4 ≈ 9,300 runs. Budget ≤ 12 min on this machine (06 measured 292 plans in 1.8–4.8 s for
  nets + cells; generators and override add an estimated 3–5×) [H].
* **PR smoke** = 292 × {S1-T1, S7-T1, S5-T2} ≤ 90 s.

### 9.5 Gates (all must be 0 unless stated)

| Id | Gate | Code |
|---|---|---|
| G-NET-1 | `InvalidShape` + `ShortDistance` on our net pieces | real |
| G-NET-2 | `OverlapExisting` between our own nets; crossings without a node | real |
| G-NET-3 | pieces < 1 m at emit; node pairs closer than `L_min`; collinear overlaps; `\|Δgrade\|·w > 0.2 m` at nodes | pure |
| G-OBJ-1 | `ValidateObject` Error/Override on our objects/buildings vs our nets, our objects (except pairs sharing the carrier) and the city | real |
| G-OBJ-2 | our members `Overridden` after `OverrideSystem` on the permanent world | real |
| G-AREA-1 | surface/area/blocker rings that fail triangulation or get `InvalidShape` | real |
| G-EQ-1 | ghost census ≠ build census ≠ program: count per prefab, position > 1 cm, curve > 1 cm, any unplanned top-level entity | real |
| G-CITY-1 | city fixture entities deleted, hidden or changed, except planned joint splits and clearing deletes; **any** change to a building | real |
| G-ZONE-1 | existing growables with a lot cell that stops being Visible-with-zone (would be condemned) | real |
| G-ZONE-2 | Visible cells inside the footprint, outside our shop lots (the city could grow on our lot) | real |
| G-SHOP-1 | planned shop not created, or `m_RoadEdge` ≠ its `FacingNetKey`, or facing off by > 2° | real |
| G-SHOP-2 | shop clone whose `AllowedSold` matches no commercial company prefab | port |
| G-PARK-1 | stall without Owner/Attached = carrier; aisle missing from carrier `SubNet`; stall farther than lane reach from an aisle; aisle component not connected to a city road (node or LocalConnect) | port |
| G-THEME-1 | vanilla `area.lot`, crop surface or farm prop in a non-farm type; a fence on an entrance or shop-front edge; a lot without an entrance or < 1 full bay | pure |
| G-TERRAIN-1 | net node > 0.3 m from the graded target; a bowl in the target (existing pit guard) | pure |
| G-PERF-1 | Compose p99 ≤ 150 ms and plan-landing main-thread Core ≤ 5 ms (.NET) | pure timing |
| G-CAL | the bench reproduces ≥ 95 % of the ghost verdicts dumped in game (type + piece + 0.5 m), ≤ 5 % extra | replay |

### 9.6 Reporting rule for the lead (06 A8, sharpened)

* "Fixed" may only be said when the **full** matrix rows of every type that shares the changed path are green. The
  shared paths are: Parking planner hosts, Mall kit hosts, ShopPlacer hosts, NetTopology (all types with nets), and
  BuildService/DefinitionWriter (all types). The message names the capture version.
* "Installed" may only be said when an in-game audit carries the build hash.

---

## 10. What to delete (ruthless)

| Area | File(s), lines today | Fate |
|---|---|---|
| Verdict machinery | `Game/Build/BuildService.Verdicts.cs` (288), `BuildService.Protect.cs` (107), `Core/Build/BuildVerdicts.cs` (155), `Core/Build/OwnOverlap.cs` (66) | **delete**. Keep only `StripWarningsOnCity` (~15 lines) and `ClearGhostVerdicts` (+ dump) |
| Temp census / tagging / exclusion / prop carrier owner | `BuildService.Adopt.cs` (616) | **delete**. Net-pass tagging (~150) + `CreationCensusSystem` (~200) |
| Owner lot | `BuildService.Owner.cs` (290), `Core/Build/OwnerRings.cs` (70), owner-lot creation in `OwnerAreaPrefabSystem` | **delete** (legacy prefab registration stays). Clearing definitions move to the net pass |
| Harmony | `Game/Groups/Patches/RaycastLiftPatch.cs` (63), `BulldozeConfirmationPatch.cs` (110), `PlacemakerHarmony.cs` (50) | **delete** |
| Registry legacy aids | `GroupRegistrySystem`: `RestoreOwnHidden`, prop part of the carrier `SubNet` rebuild, ownerless-group fallback | **delete** (keep the owner-lot cascade for `Model = 1`) |
| Zoning cutouts | `BuildService.ZoneBlock.cs` (355), `BuildService.ZoneCause.cs` (76), zoning parts of `BuildService.Terrain.cs` (`VerifyZoning`, `AuditZonePlots`, `DirectCause`, ~250), `Core/Build/ZoneGuard.cs` (1,437), `ZoneLots.cs` (122), `ZoneBlockPredictor.cs` (134), `Core/Layers/AddOns/EdgeZoningAddOn.cs` (304), `Core/Layers/Common/Cutouts.cs` (409), cell parts of `StreetShops.cs` (645) and `Parking/Zoning.cs` (350), `Mall.SnapStreetPlotsToCells`, `Mall.Faces`, zone-cell drawing in the overlay and legend | **delete**. Keep only the cell math `RoadPreZone` (Roads type) needs, moved to `Core/Build/ZoneCells.cs` (~200). Blocker rings → `BlockerPlan.cs` (~150). Shop layouts re-based on `ShopPlacer` |
| Retry / instant / optional | `ResetForRetry`, `_instant`, `CreationFlags.Optional`, `_dupRetried` (`BuildService.cs`, `Ghost.cs` ~150), `PlanWork.Excludable`, `SinkStallMarkings` | **delete** |
| Commercial composition defects (07) | `PlanBuilder.cs:448` lot area, `AssetHeuristics` auto `area.lot`, `MallKit.StreetRows` default, `CommercialLayer` lot-first leftover lots (`RestLots`), `Mall.cs:350` dropping `NoEntrance`, `ZoneGuard.NearestOpenRun` (s60) | **delete** / opt-in |
| Lot-net terrain splitting | `CourseFitting.Fit` splitting for lot nets (`CourseFitting.cs:89-138`) | **delete** for lot nets; keep the R6 split elsewhere |
| Disabled feature | `Game/Economy/ParkingCompanionSystem.cs` (349, off, "crashed natively") | **delete** |
| Settings | `AutoExcludeCollisions`, `RecoverRejectedPieces`, `IgnoreOwnCollisions` | **delete** |
| Tests | `BuildVerdictsTests`, `AnarchyAuditTests`, `CutoutBorderTests`, zoning-cutout tests | **delete** → replaced by bench gates and the new Core tests |

Net effect: about −5,000 lines, +2,500 lines of product code, plus the bench. `BuildService.cs` drops from 761 to about
350 lines.

Kept: every Core planner, `PlanIndex`, `SiteSafety`, `Declutter` (extended), `AreaRings`, `SurfaceAcceptance`, terraform
and paint, `ReceiptCodec`, `GroupRegistry`/`GroupRemoval`, `SiteCapture` (extended), the overlay, `PreviewWorker`, park
companions, and the Roads type including its pre-zoning.

---

## 11. Risks and how each is tested

| # | Risk | Likelihood / impact | Test (offline first) | In-game safety net (passive) |
|---|---|---|---|---|
| R1 | Permanent bulk creation differs in game from the bench (archetypes, Burst floats, systems the bench neuters) | M / H | G-EQ-1 on captured archetypes; golden: one permanent prop, area and building per prefab class | live census in every audit; one materialisation re-emit; defect report |
| R2 | Signature clones: no company, wrong road, odd UI or XP behaviour | M / H | G-SHOP-1 (real RoadConnectionSystem), G-SHOP-2 (company rule port) | deferred audit: renters at +5 min; fallback setting "plain growable + direct zone cells" (§8 helper) |
| R3 | Net pass demolishes a city building | L / H | G-CITY-1; SiteSafety tests | `StripWarningsOnCity`; audit counts city `Deleted` in the frame |
| R4 | Blocker or our nets condemn an existing growable | M / H | G-ZONE-1 on S8 | deferred check; automatic trim of the blocker piece (F13 lifts Condemned) |
| R5 | OverrideSystem hides our props (preview ≠ result) | M / M | G-OBJ-1/2 | audit `Overridden` = 0, else defect with position |
| R6 | Net fold survives | M / M | G-NET-1..3 on the full terrain matrix | ghost verdict dump |
| R7 | Save, crash or tool switch mid-build | L / M | Core state-machine test with injected interruptions; bench rollback test | `Flags.Building` rollback on load |
| R8 | H3 fails: a permanent write cannot reference net-pass entities in time | L / M | bench with ApplyNet allow-listed | fallback: put the carrier `SubNet` bind and stall writes one frame later |
| R9 | Clone registration freezes (09) | H if not fixed / M | G-PERF-1 + catalog unit test "own prefab adds do not change the scanned count" | frame watchdog log (09 §8) |
| R10 | Terrain is not undone on failure or removal | known / L | – | unchanged, documented; "restore ground" is a later WP |
| R11 | Other mods (Anarchy, Platter, PLT, Move It) | L–M / M | not testable offline beyond the clean bench | audits carry the mod list; Anarchy's error toggling cannot affect permanent writes |
| R12 | Bench drifts from the game (shim, single-threaded jobs) | M / M | 06 A1–A3 golden; G-CAL | ghost verdict dump feeds calibration |
| R13 | Save without the mod loses runtime-clone entities (H5) | certain / M | – | documented; uninstall helper |

---

## 12. Work packages

Order: **WP1 → (WP2 ∥ WP3 ∥ WP5) → (WP6, WP4) → (WP7, WP8)**. Parking always builds with preview == result after
WP1+2+3+6. Shops and strip malls follow after WP4+5. Each WP applies to every type sharing its path.

### WP1 — GameBench v1 and prefab capture
* **Goal:** the offline oracle every later WP is accepted against.
* **Files:**
  * `Tests/Placemaker.GameBench.{Shim,ShimGen,Host}`, `Tests/Placemaker.GameBench` (oracles, `CityFixture`,
    `TerrainFixture`, `MatrixRunner`, `PlanReplay`);
  * `Tests/Placemaker.Tests/GameBenchGate.cs`;
  * `Game/Capture/PrefabCaptureSystem.cs` (read-only, on city load);
  * `Core/Build/PrefabFacts.cs`;
  * `.gitignore`.
* **Approach:**
  * Port the 06 prototype.
  * Extend the allow-list: `CourseSplitSystem`, `Generate{Nodes,Edges,Objects,Areas}System`, `Areas.GeometrySystem`,
    `ApplyNetSystem`/`ApplyObjectsSystem` (set `applyMode` by reflection), `OverrideSystem`, `RoadConnectionSystem`,
    `ZoneCheckSystem`.
  * Load a terrain heightmap from fixtures.
  * Add city fixtures S1–S8 with occupied growables.
  * The capture writes `bench-capture-<ver>.json` (06 §8.1, plus lot sizes, `BuildingPropertyData`, company outputs,
    archetypes).
* **Acceptance:**
  * 06 A1–A3 pass.
  * New goldens pass: permanent prop/area/building created with no Temp; Temp net + Apply; a carrier-owned marking gets
    Owner+Attached.
  * The full matrix runs on HEAD and prints every §9.5 gate (expected red), within the §9.4 budgets.
  * Without a capture, every verdict says UNCALIBRATED.
* **Deps:** none.

### WP2 — BuildProgram, DefinitionWriter and the new state machine
* **Goal:** "always builds, preview == result" structurally, for every type.
* **Files:**
  * new: `Core/Build/BuildProgram.cs`, `Game/Build/DefinitionWriter.cs`, `Game/Build/CreationCensusSystem.cs`;
  * rewritten: `Game/Build/BuildService.cs`, `BuildService.Ghost.cs`, `BuildRegistration.cs`;
  * the §10 deletions (Verdicts, Protect, Adopt, Owner, ZoneBlock, ZoneCause, retry/instant/optional,
    ParkingCompanion).
* **Approach:** §3. The ghost and the build call `DefinitionWriter` with the same program. The net pass uses
  BuildToolSystem with no gate. Bulk is permanent at ≤ 400 ops per frame. The census runs in ModificationEnd.
* **Acceptance:**
  * Core tests: the program is deterministic and seed-stable, and ghost and build programs are identical.
  * The state machine survives interruption at every step (fake clock).
  * Bench: **G-EQ-1 = 0** and **G-CITY-1 = 0** on the full matrix (with today's planners, apart from known net folds,
    which are counted but do not block G-EQ).
  * Bench removal test (build → remove → fixture equal) passes.
* **Deps:** WP1.

### WP3 — NetTopology and the parking layout rules
* **Goal:** no folded, overlapping or orphaned net anywhere.
* **Files:**
  * new `Core/Build/NetTopology.cs`;
  * `Core/Composition.cs` (call it after PathJoins);
  * `Core/Build/CourseFitting.cs` (no splitting of lot nets; R6 elsewhere);
  * `Core/Layers/Parking/{LayoutEngine,PlanBuilder}.cs` (§4.3);
  * `Core/Layers/Commercial/{Mall,MallKit,NetFit}.cs` (stubs with RemoveCrosswalk, lengths);
  * `Game/Build/DefinitionWriter.cs` (heights per node from the program).
* **Approach:** §4.2/§4.3 with `PrefabFacts` numbers. `NetFit` becomes a thin wrapper, or is removed.
* **Acceptance:**
  * **G-NET-1/2/3 = 0** and **G-TERRAIN-1 = 0** on the full matrix (all terrains).
  * Core tests: every NetRole, every site, cut-back with node offset, crosswalk and slope terms (replaces
    `MallNetGeometryTests`' aisle blind spot, 03 §3.4).
* **Deps:** WP1 (gate); can run in parallel with WP2.

### WP4 — Shops as buildings
* **Goal:** shops in the preview are shops in the city, facing our lane, independent of zoning and demand.
* **Files:**
  * new: `Core/Layers/Common/ShopPlacer.cs`, `Core/Build/BlockerPlan.cs`, `Game/Build/ShopPrefabSystem.cs`;
  * `Core/Contracts.cs` (`PlannedBuilding`, `AreaPlan.Buildings`, `MemberRole.Shop`);
  * `StreetShops.cs`, `Parking/Zoning.cs`, `EdgeZoningAddOn.cs` and the commercial layers re-based on ShopPlacer;
  * `Game/Preview/SiteCapture.cs` (+ `Occupied`, `m_Zone` per cell);
  * `Game/Assets/AssetCatalogSystem.cs` (own prefab adds never rescan);
  * zoning deletions per §10.
* **Approach:** §5.
* **Acceptance:**
  * **G-SHOP-1/2 = 0**, **G-ZONE-1/2 = 0**, and G-EQ-1 including buildings, on every shop-hosting type × preset × site.
  * Core tests: every shop faces its `FacingNetKey`; no shop lot overlaps a net, lot or other shop; ≥ 1 shop per
    commercial plan on S1–S7.
  * Catalog test: 50 clone registrations cause 0 rescans.
* **Deps:** WP2, WP5 (composition), WP1.

### WP5 — Commercial and parking composition contract (07)
* **Goal:** one legible composition per site (street → driveway → lot → frontage lane → shops), no farm lots, no
  fenced-off strips, no empty lots.
* **Files:** `Core/Layers/Commercial/*`, `Core/Layers/Parking/PlanBuilder.cs:442-450`, `Core/Build/AssetHeuristics.cs`,
  `AutoAssets.cs`, `Mall.cs:334, 350`, `MallLots.cs`, `CommercialLayer.cs:158-235, 318`.
* **Approach:** 07 R1–R9 and F1–F4/F8/F10:
  * no lot area; `area.lot` explicit-only;
  * leftovers → planting;
  * a lot needs an accepted entrance + ≥ 1 full double-loaded bay + ≥ 30 % stall fill;
  * no fence on access or front edges;
  * our lanes get `ZonesDisabled` on both sides and one custom name.
* **Acceptance:**
  * **G-THEME-1 = 0** and **G-PARK-1 = 0** on the full matrix.
  * Core tests per commercial type: one composition template is logged, every lot has an entrance, and no stall strip
    lies between a lane and a street.
* **Deps:** WP1. Parallel with WP2/WP3.

### WP6 — Objects: collision-free plans, loose props, carrier for stalls only
* **Goal:** OverrideSystem has nothing to hide, so preview == result for every prop.
* **Files:**
  * `Core/Build/Declutter.cs` (obstacles = planned and city nets with real widths, buildings, shop lots; collision
    geometry from `PrefabFacts`; decal-vs-decal rule);
  * `Core/Build/BuildProgram.cs` (carrier owner/attach only for ParkingBay/Charger/Arrow);
  * `GroupRegistrySystem.cs`;
  * deletes: prop carrier owner, owner lot, Harmony patches.
* **Approach:** §2 and §6.2.
* **Acceptance:**
  * **G-OBJ-1 = 0** and **G-OBJ-2 = 0** on the full matrix.
  * Declutter tests against the real `ValidateObject` on 500 random object pairs (0 disagreements).
* **Deps:** WP2, WP1.

### WP7 — Registry, removal and migration
* **Goal:** removal from any piece; old saves keep working.
* **Files:** `Game/Groups/{GroupComponents,GroupRegistrySystem,GroupRemovalSystem,PlacemakerGroupSection}.cs`, the UI
  build list, `Core/Build/ReceiptCodec.cs` (no format change needed), the farm-lot adoption tool, the uninstall helper
  (stretch).
* **Approach:** §7 and §8.
* **Acceptance:**
  * Bench ECS tests:
    * build → Remove → the city fixture is identical, apart from terrain, for every type on S1;
    * a legacy-shaped fixture group (owner lot + carrier-owned props) loads and is removed fully, with no Harmony patch;
    * interrupted builds (`Flags.Building`) roll back on load.
  * Core tests: `PlacemakerGroup` v2 → v3 deserialisation round trip.
* **Deps:** WP2.

### WP8 — Audit and passive in-game verification
* **Goal:** every session the user plays produces bench-replayable truth. No launches by the lead.
* **Files:** `Game/Build/BuildService.Audit.cs` (rewritten), `Core/Build/BuildAudit.cs`, the ghost verdict dump in
  `BuildService.Ghost.cs`, the `Host replay` command, `PrefabCaptureSystem` (shared with WP1).
* **Approach:** §6.4. Live census, type/preset/template, replay section, deferred checks, version stamp (game version +
  `Game.dll` hash + Placemaker commit).
* **Acceptance:**
  * Core tests: `Built` = live census and never arithmetic; zero-cell and not-materialised entries are present
    (02 §6).
  * `Host replay` of a bench-produced audit reproduces its census exactly.
  * G-CAL machinery runs on a dumped ghost-verdict file.
* **Deps:** WP2 (can start in parallel for the format).

---

## 13. Proven vs open, at a glance

* **Proven by code:**
  * permanent creation bypasses Temp and validation (F1–F3);
  * owner and attach at creation (F4);
  * Apply ignores Error (F7);
  * OverrideSystem rules (F8);
  * the census timing (F9);
  * only roads host buildings (F10);
  * signature exemption, level 5, direct company creation (F11–F12);
  * un-condemn (F13).
* **Proven by run:** the game's systems and validation run headless with the mod's DLL (F17); today's planners fold
  nets and lose shop cells (F15, F16).
* **Open, with their gates:**
  * H1 (bench == game for permanent writes): G-EQ-1 + live census;
  * H2 (clones get companies and connect): G-SHOP-1/2 + deferred audit;
  * H3 (net pass → permanent references): WP1 golden;
  * H4 (stall lanes from permanent carrier-owned markings): G-PARK-1 + PLT precedent;
  * H5 (save without the mod): documented, helper.
* Not designed here, but compatible: the switch-hang fixes (09, partly required by WP4) and the in-game suite (10),
  which this design does **not** need for its acceptance.
