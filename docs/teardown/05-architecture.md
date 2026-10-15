# 05 — Architecture map and alternatives: "owner building" vs "loose assets"

Teardown workstream 05. Read-only analysis of `master` = `650be0f` (s59 merged), the three source mods, the decompiled game
(1.6.2f1) and today's forensic session. Written 2026-10-15.

**What is installed:** none of what this report proposes. The user's session today (11:35–11:52) ran the build of
`4d62ea8` (10-15 02:26), so it did **not** contain `ff46338`, s60 (`98cb703`) or s59 (`650be0f`). Everything said here about
s59 comes from reading the code. None of it has been seen running in the game.

Legend: **[P]** = proven (the file:line, log line or probe output is cited). **[H]** = hypothesis (reasoned, not observed).

---

## 0. Plain answers

* **Does the "one bulldozable object" (owner lot) refuse builds?** No. **[P]** In today's log there is not a single
  owner-lot line (`grep -c "owner lot"` = 0). The owner lot is emitted only *after* validation, in its own step
  (`BuildService.Owner.cs:183` `DoOwnerLot`), and its overlap errors are ignored. Every refusal today was the
  exclude-and-retry loop meeting `InvalidShape` on **nets** it was not allowed to drop: for example
  `Placemaker-20261015-1152.log:2466-2486` and `:3010-3029` (`Invisible Road Path - 2xTwoway` ×15, `Alley` ×3, six rounds,
  then `build Failed: InvalidShape`). s59 deletes that loop in code. It was not installed today.
* **Where does the owner/building coupling hurt?** It hides and deletes things, and it adds game-visible machinery that
  every later fix has to work around. The s55/s58 carrier owner (all props get `Owner(carrier)`) is added *after* the game
  has already decided which pieces to hide. Today, groups 5 and 6 had 143 and 127 props hidden on Apply, and 24 and 28 of
  those stayed hidden (`log:3058`, `:3266`; `group-5/6-*.json` reason `hidden`). The carrier also becomes the top-level
  thing that vanilla clicks and the vanilla bulldozer land on. The Zone Block lots are real physical lots. The owner lot and
  park companions are runtime prefabs that every save depends on. Section 1.5 lists every case.
* **Why did PLT "just work"?** Five reasons:
  * It builds one kind of thing.
  * Its plan avoids collisions using the game's own clearances.
  * Every part has its owner **before** Apply.
  * It applies without reading errors and has no retry loop.
  * Its preview *is* the temporaries it applies.

  It has also had 232 commits and 10 releases of in-game measurement (section 2.1).
* **Should we go loose?** Yes, and the change is smaller than it sounds. Placemaker already keeps its own registry
  (`PlacemakerMember` tag on every member, a record entity, phase-correct `GroupRemovalSystem`). The owner lot sits on top
  of that and adds only one thing: clicking anywhere selects the whole build. Removing it plus the prop carrier-owner loses
  that click and the attractiveness-on-lot fallback, and nothing else (section 3). Parking bays must keep their bare carrier,
  because cars cannot find the stalls without it.
* **Should we write the world directly?** Yes, but through the game's own permanent path, not raw `EntityManager` writes.
  `CreationDefinition.m_Flags |= CreationFlags.Permanent` makes `GenerateObjects/Edges/Nodes/AreasSystem` create the final
  entities with no `Temp`, no validation and no Apply. The game itself creates buildings, area props and sub-nets this way
  (`ZoneSpawnSystem`, `AreaSpawnSystem`, `PlaceholderSystem`, `ReplacePrefabSystem`), and PLT's background builder does the
  same (section 4).
* **Ranking** (section 5):
  1. Loose + registry + **permanent definitions** (keep `Temp` only for joints into city roads).
  2. Loose + registry + `Temp` pipeline with errors switched off **at the source**, the way Anarchy does it.
  3. Today's s59 model.
  4. Pure vanilla loose.
  5. Raw `EntityManager` writes.

  Option 2 is the cheapest safe first step, and option 1 is where to end up.
* **Offline verification (no game launch):** **[P]** Pure game functions in `Game.dll` run in a plain .NET host. A probe
  called the game's private `Game.Net.ValidationHelpers.CheckGeometryShape` (the `InvalidShape` rule) by reflection and got
  the correct answer: straight edge → `False`, mirrored edge → `True`. ECS itself cannot run outside the Unity player:
  `NativeArray` and `World` throw `SecurityException: ECall methods must be packaged into a system module` (Appendix A).
  So offline testing can cover every pure rule (net shape, area shape, zone-cell math, collision masks) with the game's own
  code. Entity creation can only be checked by audits that ride on builds the user makes anyway.

---

## 1. Today's entity model, end to end

### 1.1 What one build creates

| # | Entity | How it is created | Tag / role | File |
|---|---|---|---|---|
| 1 | **Group record** (headless): `PlacemakerGroup` + `PlacemakerGroupBlob` (receipt bytes) (+`PlacemakerUnderConstruction`) | direct `EntityManager.CreateEntity` | — | `GroupRegistrySystem.CreateRecord` (`GroupRegistrySystem.cs:323`), called from `BuildService.Adopt.cs:506` |
| 2 | **Carrier** (bare): `PrefabRef` = a decal prefab, empty `SubObject/SubNet/SubArea` buffers, **no Transform, no Created/Updated** | direct `EntityManager.CreateEntity` | `Auxiliary/Carrier` | `BuildService.Adopt.cs:527-547` (every build since s58) |
| 3 | **Nets** (paths, lanes, aisles, roads, fences that are nets) | `CreationDefinition` + `NetCourse` (+`Upgraded`) → `CourseSplitSystem` → `Temp` edges/nodes → validation → Apply converts the `Temp` into the permanent entity in place | `Edge/Node` (grouped) or untagged (loose layers, e.g. road grids) | `BuildService.Emit.cs:117-189` |
| 4 | **Objects** (props, trees, lamps, benches, chargers, fence pieces, decals, stall markings) | `CreationDefinition` + `ObjectDefinition` → `GenerateObjectsSystem` (every one `TempFlags.Essential`) → validation → Apply | `Object` / `ParkingBay` | `BuildService.Emit.cs:330-392` |
| 4a | stall markings: `Owner(carrier)` + `Attached(carrier)` added to the **Temp**, after validation | `EntityManager.AddComponentData` | `ParkingBay` | `BuildService.Adopt.cs:224-231` |
| 4b | every other plain overridable object: `Owner(carrier)`, added to the **Temp** after validation | `ShareCarrierOwner` | `Generic` | `BuildService.Adopt.cs:223, 264-279` |
| 5 | **Surfaces / areas** (paving, grass, plaza ground, hedges as areas) | `CreationDefinition` + `Areas.Node` buffer → `Temp` areas → Apply; paved surfaces use a runtime **overlay clone** prefab, decorative decals a runtime **opacity clone** | `Area/Surface` | `BuildService.Emit.cs:396-488`, `SurfaceCloneSystem`, `DecalOpacitySystem` |
| 6 | **Owner lot "Placemaker Area"** (runtime `LotPrefab`, `m_AllowOverlap = true`, transparent) | own definition emitted in `Step.OwnerLot` after validation; up to N simpler rings on shape errors; same Apply as the build | `Auxiliary/OwnerArea`, stored in `PlacemakerGroup.Owner` | `BuildService.Owner.cs:110-241`, `OwnerAreaPrefabSystem.cs:125-152` |
| 7 | **Zone Block lots "Placemaker Zone Block"** (runtime `LotPrefab`, *physical*: blocks zone cells) | **separate** Temp transaction after the main Apply (≤ 3 rounds, pieces that would demolish a building are dropped) | `Auxiliary/ZoneBlocker` | `BuildService.ZoneBlock.cs:1-13, 272` |
| 8 | **Zoning** (Marquee zoning quads over the cutout cells) | separate Temp transaction after the blockers | — | `BuildService.ZoneBlock.cs` (`DoZoningEmit/Apply`) |
| 9 | **Park companion** (hidden 1-cell park *building*, runtime clone of PocketPark/CityPark, 38 clones registered at startup today, `log:12-20, 165-172`) | **direct** `EntityManager.CreateEntity(ObjectData.m_Archetype)` + `Hidden` | `Auxiliary/ParkCompanion` | `Game/Economy/ParkCompanionSystem.cs:306` |
| 10 | **Clearing**: existing trees/props inside the outline | vanilla delete definitions (`CreationFlags.Delete`), same Apply | — | `BuildService.Owner.cs:253-288` |
| 11 | Terraform and paint | direct terrain API, permanent (never undone) | `GroupFlags.PermanentTerrain` | `BuildService.Terrain.cs`, `.Paint.cs` |
| 12 | Sub-elements the game generates itself (lanes, net sub-objects such as street lights, parking lanes under stall markings) | game systems | untagged; they live and die with their owner | — |
| 13 | Attractiveness | vanilla `AttractivenessProvider` written onto the **owner lot** (or onto the companion) | — | `BuildService.ParkStats.cs` header and `FinishParkStats` |

Not used (off by default): a parking companion building (`ParkingCompanionSystem.CompanionEnabled = false`, "crashed natively
several times").

### 1.2 The transaction (`BuildService.cs`, one state machine)

`Prepare → (WaitReplaced) → ActivateTool (BuildToolSystem becomes the active tool, one Clear) → Terraform → Paint → EmitNets
(all in one frame so junctions merge) → EmitRest (clearing definitions, then surfaces/areas/objects, 1500 per frame) →
WaitTemps (census by prefab + position, PlanIndex) → Validate (ONE pass, s59) → OwnerLot → Apply → Settle (adopt strays,
charge nets) → ZoneRefresh → ZoneBlock → ZoningWait/Emit/Apply → Finish (verify, survivors audit, receipt)`.

Key code: `DoValidate` `BuildService.cs:483` calls `ApplyVerdicts` (:494), then `ProtectBuildings` (:500), then
`TagTemporaries` (:501). `DoApply` (:547) calls `NeutralizeVerdicts` (:550), then `_tool.Requested = ApplyMode.Apply`.
`BuildToolSystem.cs:36` exposes vanilla's `GetAllowApply()`, but the build never uses it as a gate.

Up to **four** separate Apply transactions per build: main, blockers (each round), zoning, plus terraform/paint applies
(`BuildService.Terrain.cs:434, 613`, `.Paint.cs:293`).

### 1.3 How the game treats these entities (decompiled 1.6.2f1)

| Game system (phase) | What it does to us | Evidence |
|---|---|---|
| `GenerateObjectsSystem` (Modification1) | Every object made from a definition gets `TempFlags.Essential`, so a collision is an **Error**, never a soft Override/Warning. With `CreationFlags.Permanent` it adds **no `Temp`** at all | `Game.Tools/GenerateObjectsSystem.cs:981`, `:1278` |
| `ValidationSystem` (ModificationEnd) | Checks our temporaries against each other and the city. Pieces are exempt from each other only when they share a top-level owner. Ours have none at that point (4a/4b are added later). Error types whose prefab is flagged `DisableInGame` are skipped **entirely** (no icon, no Error, no Warning, no Override, no Cancel) | `ValidationSystem.cs:1219`, `:1535-1566`; s59.md |
| Net `InvalidShape` | Raised when the *generated geometry* of an edge or node twists (`CheckSegmentShape`: the left/right quads flip). This is a property of our course geometry, not of ownership. Building it anyway gives a twisted mesh | `Game.Net/ValidationHelpers.cs:1363-1390, 1817-1860` |
| `ToolApplySystem` (ApplyTool) | `Warning` → `Deleted`; `Override` → `Overridden` + `Updated`; charges the sum of `Temp.m_Cost` of non-cancelled temporaries. **It never reads `Error`** | `Game.Tools/ToolApplySystem.cs:50-71` |
| `ApplyObjects/Net/AreasSystem` (ApplyTool) | `TempFlags.Cancel` → removed; no original → **Create in place**: remove `Temp`/`Animation`/`BackSide`, add `Applied`+`Created`+`Updated`. Our tag (`PlacemakerMember`) and `Owner` survive | `ApplyObjectsSystem.cs:337-339, 670-686, 924` |
| `Game.Objects.OverrideSystem` (Modification5, permanent world) | For objects that are `Overridable` and **not** `DeleteOverridden` only: hides the losing one of an overlapping pair (`Overridden`). Skips pairs that share a top-level owner. Never deletes | `Game.Objects/OverrideSystem.cs:349, 384, 1576` |
| Raycast (default tool / bulldozer) | Walks the `Owner` chain and returns the **top-level owner**. For a carrier-owned bush that is the bare carrier | `Game.Objects/RaycastJobs.cs:357-405` |
| `ZoneCheckSystem` (ModificationEnd) + `CondemnedBuildingSystem` | A spawnable (zoned) building whose lot cells are no longer `Visible` with its zone type gets `Condemned`, then `Deleted` at random (1 in 4 per update). **This happens without any tool or validation** | `Game.Buildings/ZoneCheckSystem.cs:285-312, 334-367`; `Game.Simulation/CondemnedBuildingSystem.cs:30-39` |

### 1.4 Selection and deletion today

* **Click / select**: `RaycastLiftPatch` (Harmony postfix on `ToolRaycastSystem.GetRaycastResult`) lifts a hit on any member,
  or on anything up to 4 owner levels above it, to the group's owner lot (`GroupRegistrySystem.cs:245`).
* **Bulldoze**: `BulldozeConfirmationPatch` (Harmony prefix on `BulldozeToolSystem.Apply`) shows the vanilla confirmation.
  Deleting the owner lot is noticed in `HandleDeletedMembers` (`GroupRegistrySystem.cs:440`), which calls `RemoveInternal`, which
  adds `PlacemakerRemoval` to the record. `GroupRemovalSystem` (PreTool, ≤ 32 parts per pass, `RemovalPlanner` order, waits
  for tool temp copies, refreshes surviving nodes) deletes everything tagged.
* **Ownerless groups** (the owner lot was rejected or is missing): bulldozing any object or area part removes the whole group
  (`IsOwnerlessGroupPart`).
* Edit = remove the old group, then build again. A failure after Apply rolls back through the same removal
  (`BuildService.cs:647` `DoCleanup`).
* Maintenance: empty groups clean themselves up. Split or replaced edges are adopted (`AdoptReplacements`). After a load,
  tags without a record are dropped, carrier `SubNet` entries are rebuilt, and props hidden by our own pieces are shown again
  (`PostLoadRepair` :588, `RestoreOwnHidden` :656).

### 1.5 Every place the coupling has caused a bug

| # | Bug | Cause in the model | Fix it forced | Evidence |
|---|---|---|---|---|
| C1 | "The game rejected the owner lot", so builds 4–6 could not be selected or bulldozed as one | a top-level Lot area errors on **any** overlap with another lot (a neighbouring owner lot, a planned lot), and the error dropped it | owner lot moved out of validation; shape retries on simpler rings; ownerless fallback + 2nd confirmation path | `BuildService.Owner.cs:94-101`, `d0ce7a6` |
| C2 | Props, trees, lamps and chargers vanish after a "successful" lot build (396/396 markings built, everything else hidden) | `OverrideSystem` pairs every overridable object with the stall-marking boxes | s55: `ShareCarrierOwner` gives all props `Owner(carrier)` | `docs/proposals/s55.md`, `29879d0`, `44b32fe` |
| C3 | Today: 143 / 127 props hidden on Apply, 24 / 28 stay hidden although "share the carrier owner" | the carrier owner is added in `TagTemporaries` *after* the game computed its verdicts on ownerless pieces, and `ToolApplySystem` turns `Override` into `Overridden`. **[H]** The ones that stay hidden overlap city nets (the restore only re-shows "own" overlaps) | s59 `NeutralizeVerdicts` strips `Override` before Apply (not installed today) | `log:3058, 3266`; audits `group-5/6` (reason `hidden` ×24/×28); `Adopt.cs:223`; `ToolApplySystem.cs:54-57` |
| C4 | Hidden props come back hidden after load | stale `Overridden` on carrier-owned props | s58 `RestoreOwnHidden` runs at every load | `GroupRegistrySystem.cs:656` |
| C5 | Clicking a carrier-owned prop selects or bulldozes a bare entity (no Transform, decal prefab) | the raycast returns the top-level owner (C2's carrier). The lift patch hides this only while the owner lot exists. **[H]** Without the mod, or without an owner lot, the vanilla bulldozer targets the carrier and its `SubObject` cascade removes all props but leaves the nets and surfaces | none (masked by `RaycastLiftPatch`) | `RaycastJobs.cs:357-405`; PLT note "the bulldozer walks the Owner chain up and clears the whole tree" (`ParkingLotAreaDiagnostics.cs:90-96`) |
| C6 | Objects owned by the lot area would be scattered; edge-owned decals culled | `SubObjectSystem.RelocateSubObjects` treats area children like scatter; edge owners cull | members deliberately **not** owned by the lot; bays on a bare carrier | `BuildService.Owner.cs:8-13`, RESEARCH-NOTES 13, PLT `ParkingLotLotOwner.cs` (comment above `AttachPartsToLotOwner`) |
| C7 | Removal leaves decals, bushes, benches and lamps behind; untagged objects survive removal; native crash after bulldozing a group while a tool held temp copies; carrier `SubNet` lost on load | registry tagging gaps plus dependence on the carrier `SubNet`, which the game does not persist | adopt-everything, strays, leftover report, wait-for-temporaries, post-load `SubNet` rebuild | FEEDBACK-4 #5; `6b5e748`, `6e2e5fc`; `GroupRemovalSystem.cs:236-247`; `GroupRegistrySystem.cs:588-648` |
| C8 | Zone Block lots: physical, so they report overlaps with our own nets, would demolish spawnable buildings (Warning), block paint and brushes, and block other builds' cells | they are real lots in the city | own transaction with ≤ 3 rounds; s27/s57 obstacle exemptions; s54 "blockers never touch kept cells" | `BuildService.ZoneBlock.cs:1-13`; `fd7f9e7`, `3caff19`, `f253ba3` |
| C9 | Shops planned but not built ("red circles"): `8 planned cell(s) -> 0 on the live blocks`, `0 cutout cell(s) (planned 16)`, with 20–25 blocked live cells per plot | **[H]** Two candidate causes, both untested: the build's own lanes, or the physical blocker of the **previous adjacent build** (groups 3 and 4 are about 50 m apart, built 50 s apart), block the plot cells. s60 adds the cause classifier, not installed today | s60 (not installed) | `log:2775-2777, 2800-2801` |
| C10 | Attractiveness needs the owner lot; real park stats need a hidden companion building per build; 38 runtime prefab names must be registered at every start | stats piggyback on game entities | `park-companions.txt` list, load repair | `ParkCompanionSystem.cs` header; `log:12-20` |
| C11 | Builds refused ("parts overlap", `InvalidShape`) | **not owner coupling**: the validate → exclude → re-emit loop, plus nets with twisted geometry | s59 single pass (not installed) | `docs/proposals/s59.md`; `log:2466-2486, 3010-3029` |

**Cross-cutting risk, missing from today's protection [P, consequence H]:** `NeutralizeVerdicts` / `ProtectBuildings`
only stop the *Apply-time* demolition (`Warning`). A zoned building whose lot cells become hidden or blocked by our nets or
our physical Zone Block lots is condemned and deleted later by the simulation (`ZoneCheckSystem` and
`CondemnedBuildingSystem`). That would happen no matter which option below we pick. The plan has to keep nets and blockers
off zoned building cells. `ZoneGuard` already excludes building lots from the blockers; nets are not checked.

---

## 2. How the three source mods create, validate and delete

### 2.1 ParkingLotTool (KruemelGames; 232 commits, release 1.0.10, 10-12)

* **Create**: a preview made of real temporaries (area, net and object definitions). Enter → `TryApplyAreaPreview`
  (`Tools/ParkingLotToolSystem.cs:1771-1843`):
  * `AttachSurfacesToLotOwner` (the PLT lot area or the largest own area becomes the owner; surfaces get `Owner` + a
    `SubArea` entry, `ParkingLotAreaDiagnostics.cs:106-237`);
  * `CreateLotCarrier`;
  * `AttachPartsToLotOwner` (nets owned by the lot; decals `Attached` to a bare carrier, `ParkingLotLotOwner.cs:328-460`);
  * then `applyMode = Apply`.

  **Owners are set on the temporaries before Apply. No error gate. No retry.**
* **Collisions** are avoided at plan time with the game's own numbers (lamp vs plant clearance "exactly where the game would
  hide them", stall decal radii measured from `AreaUtils.IntersectObjects`), plus a later sync step that re-checks hidden lot
  parts once they have their owner (commit `b417c26`).
* **Permanent path**: PLT already has a background builder ("Bauarbeiter") that rebuilds lots with
  `CreationFlags.Permanent` definitions (`ParkingLotDefinitionsausgabe.cs:22-45`: `m_Flags |= Permanent; m_Owner = owner`)
  and pre-split permanent net courses (`ParkingLotDauerkursSystem.cs` header: `CourseSplit.CanConnect` rejects mixed-layer
  joints, so PLT builds already-split courses). It records `ParkingLotPartRelation {Lot, Carrier}` on each part (its
  registry).
* **Delete**: lot area = owner; the game's own sub-element cascade (`SubArea`/`SubNet` lists filled by hand), Harmony raycast
  lift and bulldoze confirmation (Placemaker's patches are ports of these).
* **Why it "just worked"**: the five reasons in section 0. The owner tree is complete before validation, so PLT's own pieces
  never error against each other. Months of single-purpose tuning did the rest.

### 2.2 ParkManager (tomkpunkt; 29 commits)

* **Create**: separate Temp transactions for paths, then decorations, then the lake (`ParkPathPlacement.cs:368`,
  `ParkDecorationPlacement.cs:435`, `ParkLakeBuild.cs:116`).
* **Validation**: "Errors are recorded, not acted on … the build goes ahead and a failure afterwards is explained with the
  error" (`Tools/ParkBuildValidation.cs:108-125`).
* **Collisions**: decorations are spaced at plan time by collision radius (`Geometry/ParkDecorationPlanner.cs:217-270`).
* **Membership**: `ParkPathMember {Park, ElementId, Kind}`, described as "Persistent logical membership without
  Game.Common.Owner. Keeping the generated Vanilla entity top-level makes it selectable by Move It and other network tools"
  (`ParkBuildComponents.cs`). `EnsureMembersAreTopLevel` even *removes* `Owner` (`ParkPathPlacement.cs:1054-1064`).
* **Delete**: its own remove mode; click any element, the park is resolved through the tag (`ParkRemoval.cs`).
  **This is exactly the user's "loose assets + registry" option.**

### 2.3 Grid Road Generator (Ti4goc; 70 commits)

* Roads only, top-level, untagged. It respects vanilla's gate: applies only when `GetAllowApply()` is true
  (`Systems/GridRoadToolSystem.cs:827-834, 989-994`). The red preview is the answer. Deletion is vanilla, per segment.

### 2.4 Anarchy and Move It (decompiled locally from the subscribed DLLs, read only)

* **Anarchy** stays inside the tool → `Temp` → Apply pipeline. `DisableToolErrorsSystem` (Modification5) sets
  `ToolErrorFlags.DisableInGame` on the allowed error-type prefabs. `EnableToolErrorsSystem` (ModificationEnd) clears the
  flags again, every frame. `ValidationSystem.ProcessError` then drops those errors before they become an icon, `Error`,
  `Warning` or `Override` (`ValidationSystem.cs:1537`). Afterwards `AnarchyPlopSystem` adds its own `PreventOverride` tag to
  placed objects and removes `Overridden`. Anarchy creates no entities itself.
* **Move It** edits existing permanent entities in place (Transform/curve writes, then `Updated` + `BatchesUpdated`,
  `QAccessor/QObject.UpdateAll`). It creates no city entities from scratch.

---

## 3. Option "loose assets" (no owner lot), with or without a registry

### 3.1 Two variants

* **L0, pure vanilla**: each piece is a normal top-level object or net. No tag, no record. Deletion is the vanilla bulldozer,
  piece by piece.
* **L1, loose + registry** (recommended): the same entities, still tagged `PlacemakerMember {GroupId, Kind, Role}` (exists
  today). The record entity stays. Placemaker offers **"Delete this build"** in its panel and in the info-panel section shown
  for *any* member (`PlacemakerGroupSection` can attach to any tagged entity). It also offers a ParkManager-style "pick a
  build" mode: hover highlights all members, click asks to confirm. The vanilla bulldozer still deletes single pieces, as the
  user suggested.

### 3.2 What gets simpler (code that goes away)

* the owner lot: `Step.OwnerLot`, `EmitOwnerArea`, `OwnerRings` levels, `FindOwnerTemp`, owner-lot error handling
  (`BuildService.Owner.cs:94-241`), and the "Placemaker Area" runtime prefab;
* `RaycastLiftPatch` and `BulldozeConfirmationPatch`, i.e. **both Harmony patches** (C5 goes away);
* `ShareCarrierOwner`, `RestoreOwnHidden`, and the carrier-owner part of `AuditSurvivors` (C2–C5);
* the ownerless-group fallback (`IsOwnerlessGroupPart`) and the owner-lot cascade in `HandleDeletedMembers`;
* obstacle rules that treat "my builds' owner lots" specially (s27/s57); the receipt outline already exists as their source.

### 3.3 What breaks or must be replaced

| Lost | Replacement | Note |
|---|---|---|
| One click anywhere in the outline selects the whole build (FEEDBACK-1 #12, US7) | info-panel section on every member, "pick a build" mode, Placemaker build list | surfaces are never clickable in vanilla (RESEARCH-NOTES 7), so a click on bare grass between pieces selects nothing |
| Vanilla bulldoze of the lot removes everything | Placemaker "Delete build" (registry → `GroupRemovalSystem`, unchanged) | the user explicitly accepts per-piece bulldozing |
| Props shared an owner, so `OverrideSystem` did not pair them | (a) plan-time declutter for overridable-vs-overridable pairs (PLT/ParkManager way), **and/or** (b) a small "keep own pieces visible" system: remove `Overridden` from tagged members whose only overlaps are other members (Anarchy `PreventOverride` pattern) | (b) is per-frame upkeep, but it is the only way to make "preview == result" hold under anarchy when the plan overlaps on purpose |
| Attractiveness on the owner lot (fallback) | the companion building (default on) carries all park stats already | a build without a companion keeps its stats in the receipt only |

**Must stay as it is:** the parking **carrier for stall markings** (`Owner` + `Attached`) and the aisles in its `SubNet`.
`LaneConnectionSystem.FindParkingConnection` looks for a road connection only in the buffers of the owner/attached parent
(PLT quotes the decompiled lines, `ParkingLotLotOwner.cs` comment above `HefteObjekteAnTraeger`). Without it cars drive in,
turn and leave (PLT measured 2026-08-17). This is a *technical* owner that nobody clicks; the props simply stop using it.

### 3.4 Other effects

* **Saves without the mod:** vanilla-prefab pieces stay as ordinary city content. `PlacemakerMember` is dropped
  (RESEARCH-NOTES 4). **Better than today**, because no prop hangs under a bare carrier any more, so a vanilla click on a bush
  selects the bush (C5). Still at risk in every option: entities using **runtime prefabs** (overlay surface clones, decal
  opacity clones, park companions, Zone Block lots) reference names the vanilla game cannot resolve.
* **Park stats / companion:** unaffected. It is already its own tagged member.
* **Zoning blockers:** unaffected by the owner choice. They are separate physical lots and stay tagged members; C8 and the
  condemnation risk remain.
* **Move It / other tools:** members are top-level vanilla entities, as ParkManager intended. The registry already follows
  edge splits and replacements (`AdoptReplacements`).

---

## 4. Option "direct world write"

### 4.1 Two ways to write final entities

**(a) The game's own permanent definitions (recommended).** Set `CreationFlags.Permanent` on the same
`CreationDefinition` + `ObjectDefinition` / `NetCourse` / `Areas.Node` we emit today.

* `GenerateObjectsSystem` creates the entity from `ObjectData.m_Archetype` and skips `Temp`/`Animation` (`:1229, :1278`).
  Prefab archetypes already contain `Created` + `Updated` (`Game.Prefabs/ObjectPrefab.cs` `RefreshArchetype`), so every
  normal Modification system (references, search trees, sub-objects, lanes, rendering batches, pathfinding) runs as for a
  spawned building.
* Nets: `GenerateNodesSystem` / `GenerateEdgesSystem` honour `Permanent` (`GenerateEdgesSystem.cs:1291`,
  `GenerateNodesSystem.cs:659, 1007, 1587`). Areas: `GenerateAreasSystem.cs:224, 261, 384, 405`.
* The game itself uses this path: `ZoneSpawnSystem.cs:627` (every grown building), `BuildingConstructionSystem.cs:378/445`,
  `AreaSpawnSystem.cs:453/524/575`, `PlaceholderSystem.cs:117`, `ReplacePrefabSystem.cs:372/489` (nets and areas). So does
  PLT's background builder (2.1).
* **No `Temp` means no `ValidationSystem`, no `ToolApplySystem`, no `Warning → Deleted`, no `Override → Overridden` at
  Apply, no red icons, no "parts overlap".** A build cannot be refused.

**(b) Raw `EntityManager` writes** (`CreateEntity(archetype)` + `PrefabRef`/`Transform`/`PseudoRandomSeed`/…). This is what
our `ParkCompanionSystem` does for one hidden building (`ParkCompanionSystem.cs:306`). Acceptable for single objects. For nets
it would mean re-implementing nodes, `ConnectedEdge`, compositions, elevation, lanes and zone blocks, which the Generate
systems already do correctly. **Not recommended.**

### 4.2 What the permanent path must still handle itself

| Concern | Handling | Status |
|---|---|---|
| Joining a city road mid-edge (split) | the game never splits an existing edge with a permanent course (`CourseSplitSystem.FindOriginalEdge` skips `Permanent`, `:3857`); PLT pre-splits its permanent courses and found mixed-layer joints rejected (`ParkingLotDauerkursSystem.cs`). Keep a **tiny Temp transaction only for joints into city roads** (or end at existing nodes / LocalConnect dead ends, as our paths already do, RESEARCH-NOTES 15) | **[H]** needs one audited in-game build |
| Money | `ToolApplySystem` does not charge; Placemaker already charges nets itself (`log:2774` "charged 360 for 3 road/path edges") and has `Pricing` for the rest | small |
| Clearing existing trees/props | add `Deleted` in PreTool (as `GroupRemovalSystem` already does) instead of delete definitions | small |
| `OverrideSystem` still runs on permanent objects | same as 3.3: plan-time declutter + keep-own-visible | unchanged need |
| Existing buildings | with no validation there is no `Warning`, so nothing is demolished on Apply. Our pieces may overlap a house (anarchy look) unless the plan avoids it (`SiteSafety` already does). Condemnation through blocked zone cells remains (1.5) | plan-time guard needed in every option |
| Preview | keep the ghost as `Temp` from the *same* definitions without `Permanent`; switch the error prefabs off during ghost frames (2.4) or keep s59's `ClearGhostVerdicts`. Same Generate systems + same inputs = same entities, so **preview == result by construction** | ghost check can run preview-only |
| Elevated / bridge courses, top-level permanent nets with no owner | precedent exists only for owned or zero-elevation courses (PLT, `ReplacePrefabSystem`) | **[H]** medium risk; audit the first builds |
| `Applied` / `Recent` (refund on bulldoze) not added | cosmetic (no refund when bulldozing a fresh piece) | accept or add `Recent` ourselves |

### 4.3 Risks

* Lower than it sounds for objects, surfaces and decals: these are exactly what `ZoneSpawnSystem`, `AreaSpawnSystem` and
  `PlaceholderSystem` create every minute in every city.
* Medium for free-standing nets (terrain fit, elevation, joints). That is the reason for the hybrid in 4.2.
* We give up the safety net of vanilla validation. Under anarchy we do that already (s59). With "vanilla rules" (setting
  off), the build would have to run the ported rules offline (section 6) instead of asking the game.

---

## 5. Ranking for "always works, never shows errors, preview == result, safe for the city"

| Rank | Option | Always works | No errors shown | Preview == result | City safety | Cost / risk |
|---|---|---|---|---|---|---|
| **1** | **Loose + registry (L1) + permanent definitions** for objects, surfaces, decals and internal nets; Temp only for joints into city roads; carrier kept only for stall markings; keep-own-visible guard | yes: nothing validates the bulk of the build | yes: the ghost has errors switched off at the source | yes: same definitions, same generator systems | no Apply-time demolition; condemnation guard needed (as everywhere) | largest change; net permanence must be confirmed by audits of the user's own builds |
| 2 | **L1 + Temp pipeline with errors switched off at the source** (Anarchy's mechanism, only while `BuildToolSystem` or the ghost is active, flags restored at ModificationEnd) | yes: no `Error`/`Warning`/`Override`/`Cancel` is ever produced | yes | yes (instant build adopts the ghost's temporaries) | no `Warning` on houses, so nothing demolished; condemnation guard needed | small and incremental (replaces s59's after-the-fact clean-up); must coexist with Anarchy toggling the same flags (both restore every frame) |
| 3 | Today's s59 model (owner lot + prop carrier + read verdicts once, strip them before Apply) | yes, by code reading; **never run in game** | ghost tint cleared after the fact | mostly; Override→Overridden timing (C3) handled by stripping | `Warning` stripped; condemnation not covered | keeps C1–C10 coupling and two Harmony patches |
| 4 | L0 pure vanilla loose with vanilla gate (GRG style) | **no**: any overlap blocks the whole build | no: red is the message | yes | yes | simplest, but fails the main requirement |
| 5 | Raw `EntityManager` writes for everything | yes | yes | only if we re-implement the generators exactly | unknown | high crash risk for nets (RESEARCH-NOTES 11–14) |

**Recommended path:** do **2 now**, then move objects, surfaces and decals to **1**. Step 2 removes the owner lot, the prop
carrier owner and both Harmony patches, and replaces "read errors then strip them" with "the game never produces them". It
touches only the emit/validate/apply edges of `BuildService`. Step 1 then removes validation and Apply from everything that
does not join a city road. Both steps keep the registry, removal, receipts and edit flow unchanged.

**InvalidShape is independent of all five options.** It is twisted *geometry* (`CheckSegmentShape`). Building it anyway gives
a visibly broken net. Fix it in Core (course fitting) and test it offline with the game's own function (section 6).

---

## 6. Offline verification (no game launch), per option

**Proven feasible [P]** (Appendix A): a net9 console host that references `Game.dll`, `Colossal.Mathematics.dll` and
`Unity.Mathematics.dll` from the install folder can call the game's pure static helpers, private ones via reflection.
`Game.Net.ValidationHelpers.CheckGeometryShape(EdgeGeometry, ref Bounds3)` returned `False` for a straight 2 m × 10 m edge
and `True` for the mirrored one. **Not feasible:** anything that allocates native memory or an ECS `World`
(`SecurityException: ECall methods must be packaged into a system module`). Systems, jobs and quad trees cannot run headless.

So offline we can test:

1. **Differential tests against the game's own functions** for every pure rule we already port: net segment shape
   (`InvalidShape`), area ring rules (`Game.Areas.ValidationHelpers` / `AreaUtils` pure parts), zone-cell math
   (`ZoneUtils.GetCellIndex/GetPosition`), collision masks (`ObjectUtils.GetCollisionMask`,
   `ValidationHelpers.Check3DCollisionMasks`).

   To do this, build `EdgeGeometry` from our planned courses with a port of `Game.Net.GeometrySystem`'s offset math. Then
   assert with the real `CheckGeometryShape` that no planned edge or node twists. That would have caught all 15
   `Invisible Road Path` / `Alley` failures before the user saw them.
2. **Our Core tests** (planning, ZoneGuard, declutter, verdict decisions) as today.
3. **In-game evidence that changes nothing in the city:**
   * a preview-only self-check on the ghost the user opens anyway: after one ghost, count temporaries with
     `Error`/`Warning`/`Override`/`Cancel` (option 2 must give 0) and confirm the error flags were restored;
   * the per-build audit on builds the user makes anyway: every planned piece exists, none is `Temp`, none is `Overridden`,
     counts match the plan, no city building got `Condemned` or `Deleted` in the next N frames (new check). Options 1–3
     need this to confirm what the decompiled code predicts.

---

## 7. Proven facts vs hypotheses

**Proven** (file:line, log line or probe):

* Apply ignores `Error`: it converts `Warning`/`Override` and drops `Cancel` only (`ToolApplySystem.cs:50-71`,
  `ApplyObjectsSystem.cs:337, 670-686`).
* Definition objects are `Essential` (`GenerateObjectsSystem.cs:981`). `Permanent` definitions create final entities without
  `Temp` (`:1278`), and the game uses that path in 5 of its own systems (4.1).
* Disabled error prefabs are dropped before any icon or verdict (`ValidationSystem.cs:1219, 1537`). This is Anarchy's
  mechanism (decompiled `DisableToolErrorsSystem` / `EnableToolErrorsSystem`).
* `OverrideSystem` only hides (`Overridden`), only `Overridable & !DeleteOverridden` objects, and skips a shared top-level
  owner (`OverrideSystem.cs:349, 384`).
* The raycast returns the top-level owner, i.e. the carrier for carrier-owned props (`RaycastJobs.cs:357-405`).
* Zoned buildings with invisible or wrong-zone cells are condemned and then deleted by the simulation
  (`ZoneCheckSystem.cs:334-367`, `CondemnedBuildingSystem.cs:30-39`).
* Today's refusals came from the retry loop meeting net `InvalidShape` (`log:2466-2486, 3010-3029`). No owner-lot line in
  the whole session.
* Today's hidden props: 143/127 hidden, 24/28 kept hidden despite the carrier owner (`log:3058, 3266`; audits).
* Shops: plot cells were blocked at refresh time (`log:2775-2777, 2800-2801`).
* PLT owns its parts before Apply and applies without an error gate. ParkManager records errors and applies. GRG uses
  vanilla's gate (2.1–2.3).
* Pure `Game.dll` functions run headless, ECS does not (Appendix A).

**Hypotheses** (to confirm through audits of the user's own builds, never by launching the game in the loop):

* C3: the props that stay hidden overlap city nets.
* C5: the vanilla bulldozer on a carrier-owned prop (without the mod or without an owner lot) deletes the carrier and its
  sub-objects only.
* C9: what blocked the shop cells (own lanes vs the neighbouring build's Zone Block lot).
* Permanent nets: no owner, terrain-following and elevated courses behave like Temp-applied ones. City-road joints need Temp.
* Option 2 coexists with an installed Anarchy because both restore the flags at ModificationEnd.
* s59 behaves in game as its code says (not installed in any session yet).

---

## Appendix A — headless probe (scratchpad, not in the repo)

```csharp
// net9 console; references Game.dll, Colossal.Mathematics.dll, Unity.Mathematics.dll, Unity.Collections.dll,
// Unity.Entities.dll, UnityEngine.CoreModule.dll from ...\Cities2_Data\Managed
var m = typeof(Game.Net.ValidationHelpers).GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
    .First(x => x.Name == "CheckGeometryShape" && x.GetParameters()[0].ParameterType == typeof(Game.Net.EdgeGeometry));
// straight 2 m x 10 m edge, m_Length = (5,5) per half  -> False
// same edge with left/right swapped (mirrored)          -> True
new Unity.Collections.NativeArray<int>(4, Allocator.Persistent); // SecurityException: ECall methods must be packaged into a system module.
new Unity.Entities.World("probe");                               // same SecurityException
```

Output:
```
straight edge invalid? False
twisted edge invalid? True
NativeArray: SecurityException: ECall methods must be packaged into a system module.
World: SecurityException: ECall methods must be packaged into a system module.
```

## Appendix B — main references

* Placemaker: `Game/Build/BuildService.cs:420, 483-550, 647`; `BuildService.Adopt.cs:170-279, 506-548`;
  `BuildService.Owner.cs:94-288`; `BuildService.Emit.cs:117-488`; `BuildService.Verdicts.cs:138-286`;
  `BuildService.ZoneBlock.cs:1-13`; `BuildToolSystem.cs:36`; `Game/Groups/GroupComponents.cs:18-62`;
  `GroupRegistrySystem.cs:245, 440, 588, 656`; `GroupRemovalSystem.cs`; `OwnerAreaPrefabSystem.cs:125-152`;
  `Patches/RaycastLiftPatch.cs`, `Patches/BulldozeConfirmationPatch.cs`; `Game/Economy/ParkCompanionSystem.cs:306`;
  `docs/proposals/s55.md`, `s59.md`.
* Game (decompiled 1.6.2f1): `Game.Tools/{ToolApplySystem, ApplyObjectsSystem, GenerateObjectsSystem, GenerateEdgesSystem,
  GenerateNodesSystem, GenerateAreasSystem, CourseSplitSystem, ValidationSystem, CreationFlags, ErrorType, ErrorSeverity}.cs`;
  `Game.Net/ValidationHelpers.cs`; `Game.Objects/{OverrideSystem, RaycastJobs, PlaceholderSystem}.cs`;
  `Game.Prefabs/{ObjectPrefab, ReplacePrefabSystem}.cs`; `Game.Simulation/{ZoneSpawnSystem, AreaSpawnSystem,
  BuildingConstructionSystem, CondemnedBuildingSystem}.cs`; `Game.Buildings/ZoneCheckSystem.cs`;
  `Game.Common/SystemOrder.cs:95, 111, 215-220, 258, 264, 713-724`.
* Source mods: PLT `Tools/ParkingLotToolSystem.cs:1771-1843`, `ParkingLotAreaDiagnostics.cs:89-260`,
  `ParkingLotLotOwner.cs`, `ParkingLotDefinitionsausgabe.cs:22-45`, `ParkingLotBauarbeiter.cs`,
  `ParkingLotDauerkursSystem.cs`; ParkManager `Tools/ParkBuildValidation.cs`, `ParkBuildComponents.cs`,
  `ParkPathPlacement.cs:1054`, `ParkRemoval.cs`, `Geometry/ParkDecorationPlanner.cs:217-270`; GRG
  `Systems/GridRoadToolSystem.cs:827-834, 989-994`.
* Forensics: `.forensics/Placemaker-20261015-1152.log` lines 1781-3483 (cited above); `group-{0,1,2,5,6}-*.json`.
