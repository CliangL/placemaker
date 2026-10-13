# Game-build workstream — decisions and contract proposals

Owner: game-build. Status: implemented on branch `gamebuild`; needs in-game verification (list at the end).

## 1. Ownership model: headless record + member tags (chosen)

| | PLT: Lot-area owner + carrier | PM: headless record + tags (**chosen**, improved) |
|---|---|---|
| Select as one | native (lot area is raycastable) but needs a runtime Lot prefab + Harmony raycast lift for paths | members are vanilla nets/objects → already clickable; a `PlacemakerGroupSection` (InfoSectionBase, middle section) shows "Part of …  Edit / Remove" |
| Runtime prefabs | lot prefab (rules 1–3, 7, 8) | none for ownership |
| Vanilla tools on members | children are owned → bulldozer/Move It act on the lot | members stay top level → bulldozer, Move It, upgrades work per element |
| Save without mod | lot area survives as orphan, children owned by it | custom components are dropped; entities stay as plain city objects (no dangling owners) |
| Crash surface | cascade delete through owner lists, carrier SubNet lost on load, RelocateSubObjects | ordered removal (Core `RemovalPlanner`), no owner chains except the parking carrier |

Why: fewer engine hazards, no Harmony, members keep vanilla behaviour, graceful degradation.
The one place we need an owner is parking: decals need `Owner`+`Attached` to a **bare carrier**
whose `SubNet` holds the drive aisles, otherwise `LaneConnectionSystem` cannot link stalls to
aisles (PLT measured). The carrier is a group member (`Auxiliary`, removed last); its `SubNet`
(not persisted by CS2 for owner-less edges) is rebuilt after every load.

### Components (`Game/Groups/GroupComponents.cs`, all version-first ISerializable)
- `PlacemakerMember { GroupId, Kind, Role }` on every member → `GroupOf(e)` is one component read.
- `PlacemakerGroup { GroupId, Flags, Carrier, Companion, BuiltMembers }` + `PlacemakerGroupBlob` (byte buffer) on a headless record entity.
- Blob = `Core/Build/ReceiptCodec` (magic "PMGR", version, append-only fields, recipe and area in separately framed blocks so a broken receipt never loses the group). Contains id, recipe id/name key, custom name, flags, stats, cost, upkeep, facts, prefab names (missing-asset check), Recipe, AreaShape.
- `PlacemakerRemoval` (persisted; removal resumes after load), `PlacemakerUnderConstruction` (not persisted).

## 2. Build transaction

`BuildService` (ToolUpdate) + `BuildToolSystem` (an invisible ToolBaseSystem).

**Why a tool:** CS2 applies/clears *all* temporaries according to the *active tool's* `applyMode`.
A service cannot request Apply, and the caller's tool (usual cadence: `ApplyMode.Clear` every frame)
would wipe our temporaries. The service therefore activates its own tool for the few frames of a
build and restores the previous tool afterwards. While it is active, every temporary is ours.

Steps: Prepare (flatten, resolve prefabs, remove the replaced group) → ActivateTool (+ one Clear
frame) → Terraform (level brushes until on target) → EmitNets (all courses in ONE frame so
junctions merge) → EmitRest (surfaces/areas/objects, 1500/frame) → WaitTemps (census by prefab +
geometry, stable/timeout) → Validate (vanilla `GetAllowApply`/Error entities; error type from
the notification icon's `ToolErrorData`; optional auto-exclusion and retry ≤4) → tag temporaries →
Apply (vanilla charges `Temp.m_Cost`) → Settle (tags survive: Apply converts a Temp without
original into the permanent entity in place) → water source → zoning (Zone|Marquee, no Overwrite)
→ Finish (record, economy). Any failure before Apply: Clear + delete definitions/record.
After Apply: rollback through the normal group removal.

## 3. Contract proposals (lead-owned files — NOT edited; worked around)

1. **Tool cooperation during a build.** While `Services.Build` is busy the Placemaker tool is
   deactivated (OnStopRunning) and reactivated afterwards. Please don't reset tool state when
   `(Services.Build as BuildService)?.HoldsTool == true`. Alternative for later: add
   `ApplyMode IBuildService.RequestedApplyMode` so the Placemaker tool can forward it itself and no
   tool switch is needed.
2. `BuildRequest.AutoExcludeCollisions` (bool). Today: static `BuildService.AutoExcludeCollisions`.
3. `GroupInfo.MissingAssets` (string[]) and `GroupInfo.Flags` (int, `Core.Build.GroupFlags`).
   Today: `GroupRegistrySystem.MissingAssets(id)/HasMissingAssets(id)/GetRecord(id).Flags`; the info
   section also writes `missingAssets`, `modified`, `removing`.
4. `BuildStatus.Warnings` (string[]). Today: `BuildService.LastWarnings`; `MessageKey` of a Done
   status is the first warning (e.g. `Placemaker.Build.PartsDropped`, arg 0 = count).
5. **Entity id encoding** for `BoundaryAttachment.ExternalId`/`NearbyNet.ExternalId`: use
   `Placemaker.Game.Build.NetRefs.Encode(entity)` = `(Version << 32) | Index`. The build re-checks
   the entity by geometry (≤ 4 m), so stale ids from an old receipt degrade to a free endpoint.
   On Edit/repair the tool should recapture attachments for the receipt's area.
6. Settings to expose (owned by tool workstream): `BuildService.AutoExcludeCollisions`,
   `SurfaceCloneSystem.FeatureEnabled`, `ParkingEconomySystem.FeatureEnabled`, `ParkingEconomySystem.DefaultFee`,
   `ParkingCompanionSystem.CompanionEnabled` (experimental, default off),
   `ParkingCompanionUpkeepSystem.UpkeepFactor`.
7. Mod.cs: call `Placemaker.Game.Build.BuildRegistration.Register(updateSystem)` in `OnLoad`.
   `Services.Reset()` on dispose is already done by each system's OnDestroy.
8. `IGroupRegistry` additions worth promoting: `IsRemoving(id)`, `Version` + `Changed` event.
9. Parking fee per lot: `ParkingEconomySystem.SetFee(groupId, fee)` / `FeeOf` / `CapacityOf` —
   a UI binding could be added to the info section later.

## 4. Repair of missing assets
`GroupRegistrySystem` compares each receipt's prefab names with the catalog after every rescan and
sets `GroupFlags.MissingAssets`. Repair = tool calls `Services.Build.Start(new BuildRequest { Plan =
Compose(receipt.Recipe, receipt.Area…), Recipe, Area, ReplaceGroup = id })`; asset refs that no
longer resolve fall back to category defaults inside the catalog.

## 5. Known limitations / future
- Edit replaces by remove-then-build; if the rebuild fails the old group is gone (the receipt is
  still in the caller's hands for a retry).
- Members replaced by other tools are re-adopted for nets (split/upgrade along the same curve);
  objects replaced by other tools leave the group (it is flagged `Modified`).
- 25 km/h drivable path clones (PLT) not ported: vanilla invisible road paths are used.
- Park leisure/wellbeing effects: `Game/Economy/IEffectProvider` seam only.

## 6. Round 2 (playtest 1) — US5–US8

### US7 one thing: owner lot + Harmony (PLT model, adapted)
- `Game/Groups/OwnerAreaPrefabSystem` registers the runtime `LotPrefab` **"Placemaker Area"** in
  PrefabUpdate before PrefabInitializeSystem, at startup (saves resolve it by name), used ≥ 3 frames
  later once `AreaData.m_Archetype` is valid and the type is Lot. `m_AllowEditing = true` (false =
  HiddenIngame = unclickable), `m_AllowOverlap = true` (no PhysicalGeometry), transparent colours,
  visible selection colours, CanOverrideObjects stripped.
- Each build emits one owner lot over the outline (`BuildRequest.Area.Ring`, else the convex hull of
  the plan), repaired for the lot rule d = 4 m. It is tagged `PlacemakerMember{Auxiliary, OwnerArea}`,
  gets empty SubArea/SubNet/SubObject buffers (rule 8), and its entity is stored in
  `PlacemakerGroup.Owner` (component **v2**, appended field). The group record/receipt stays on the
  headless record entity. `GroupRegistrySystem.OwnerAreaOf(id)`.
- **Members are not Owner'ed by the lot** (decision): roads with an area owner lose zone blocks and
  vanilla upgrade/connect behaviour; owned surfaces/objects would die with the lot when a save is
  loaded without the mod (unknown prefab); area-owned objects get scattered by RelocateSubObjects.
  Parking bays keep their bare carrier. Selection/bulldozing is unified by the raycast lift instead.
- Harmony 2.2.2 shipped like PLT: `Library/Harmony/0Harmony.dll` + LICENSE (+ README with SHA-256),
  csproj `<Reference Include="0Harmony" Private=true>` and the licence deployed as
  `0Harmony-LICENSE.txt` (**lead: minimal csproj edit made, please keep**). Installed from
  `GroupRegistrySystem.OnCreate` (`Game/Groups/Patches/PlacemakerHarmony`), wrapped in try/catch,
  uninstalled in OnDestroy.
  - `RaycastLiftPatch`: postfix on `ToolRaycastSystem.GetRaycastResult`; if the hit (or an owner up to
    3 levels) carries a PlacemakerMember and the group has a live owner lot → `result.m_Owner = lot`.
    Skipped with `RaycastFlags.SubElements`, when the type mask has neither StaticObjects nor Areas
    (net tools/upgrades keep their roads), for Temp hits and transport stops. HarmonyPrepare guard;
    any exception disables the lift for the session.
  - `BulldozeConfirmationPatch` (PLT): before `BulldozeToolSystem.Apply`, if a Temp-Delete area's
    original is an owner lot, the vanilla confirmation dialog is requested (`m_State = Waiting`);
    the OnUpdate prefix lets exactly the confirmed update through. Guards for `m_State`, `OnUpdate`,
    `Apply`, `applyMode` setter.
- Cascade: `GroupRegistrySystem` (ModificationEnd) sees the owner lot `Deleted` → `RemoveInternal`
  → `GroupRemovalSystem` deletes all members in PreTool of the next frame (rules 11–12). Our own
  removal deletes the lot last (Auxiliary).
- Info panel: `PlacemakerGroupSection` resolves the selected owner lot through its tag (unchanged).
  Lang: `Assets.NAME[Placemaker Area]` = "Placemaker build" (+ DESCRIPTION).
- Settings to expose (tool workstream): `OwnerAreaPrefabSystem.FeatureEnabled`,
  `Patches.RaycastLiftPatch.Enabled`, `Patches.BulldozeConfirmationPatch.Enabled` (all default true).

### US8 recoverable builds
- Root cause + exact game rules: `docs/RESEARCH-NOTES-ADDENDUM.md` (27–31). `Core/Build/AreaRings`
  implements them; `BuildService.EmitPreparedRings` repairs (removes offending nodes) or splits
  (`SurfaceAcceptance.MakeAcceptable2D`) or skips every ring with the prefab's own d before emission.
  **Geometry/Core:** `SurfaceAcceptance.MinEdge` (0.3) is below the game's 0.375 m — please raise it
  and add `AreaRings.IsValid` to your acceptance so previews already match.
- Validation errors no longer fail the build: `RecoverRejectedPieces` (default true) maps every
  errored temporary to its plan item (exact by prefab + geometry via the owner chain, else by
  proximity), excludes it, and retries (≤ 6 rounds). An error on the owner lot drops only the lot.
  Fails only when nothing changes, nothing remains (`EverythingCollides`) or money is short.
  `AutoExcludeCollisions` is now default true and only matters when RecoverRejectedPieces is off.
- **New contract (Abstractions.cs, additive):** `BuildStatus.Warnings : List<PlanIssue>` (Severity
  Warning = left out, Info = repaired/cleared; Args[0] = count; `At` = a map position) and
  `IBuildService.ClearErrors()`. Keys: `Placemaker.Build.Skipped.{Surface,Area,Net,Object}`,
  `Placemaker.Build.Repaired.{Surface,Area}`, `Placemaker.Build.Cleared`,
  `Placemaker.Build.OwnerUnavailable`. A Done status's MessageKey/Args = the first Warning, else
  `Placemaker.Build.Done`. `ErrorPositions` are only non-empty for Failed and are cleared by Start,
  Acknowledge, ClearErrors and a successful build. **Tool:** call `ClearErrors()` (and drop your copy)
  on any edit; show `Warnings` in the Done toast; on Done drop the plan preview.

### US6 clean result
- `BuildService.ClearExistingObjects` (default true; setting "Clear existing trees and props"):
  top-level trees/plants/static props/decals inside the outline (not buildings, utility objects,
  attached or elevated things, transport stops, Placemaker members) get the bulldozer's own delete
  definition in the same transaction → removed on the same Apply, never without the build.
- `BuildService.OverlayPavedSurfaces` (default true): paving/asphalt/plaza/decorative surfaces of every
  layer use the −94 Terrain|Roads overlay clone, so they stack above grass instead of z-fighting.

### US5 honest price
- ONE resolver: `IAssetCatalog.ResolveAutomatic(ComposedPlan)` (AssetCatalogSystem → pure
  `Core/Build/AutoAssets`, tested). Fills empty ids — and explicit ids that are not loaded (first
  loaded fallback, else default) — in place: nets/surfaces/areas/functional objects → category default,
  `NetRole.Alley` → vanilla "Alley", trees/bushes/flowerbeds → hash(Variation) over the default plus
  the 7 best-scored vanilla items. `Game/Preview/AutoAssets.Resolve` now just forwards to it;
  `BuildService.Start` calls it again (idempotent).
- What the game charges (addendum 33–35): objects = PlaceableObjectData cost; surfaces/areas = 0
  (correct, not a bug); nets were FREE for every mod (no `SubElevation`) — Placemaker now charges
  them after Apply with `NetUtils.GetConstructionCost` per built edge and refuses with NotEnoughMoney
  beforehand. `Pricing.Of` bills nets in rounded 8 m cells (`Pricing.BillableNetLength`), matching.
  Setting `BuildService.ChargeNetConstruction` (default true). Known gap: trees' age multiplier.

### Parking (generic support)
- `NetRole.Alley` → vanilla "Alley" (no PLT clone yet: zone blocks/crosswalks stay vanilla).
- Footpaths (`NetRole.Pathway`) may now attach to a road end (`StartAttach/EndAttach` = road id)
  like a vanilla pedestrian path; invisible aisles still join via LocalConnect (no attach).
- `PlannedZoning` is applied without Overwrite (unchanged). Bus stops: dropped by parking (§92).

## 7. Round 3 (playtest 2) — FEEDBACK-2 #1 "inconsistent automatic assets"

### What produced the screenshot (root cause)
Build log: 3 layers = park + **playground add-on with `fence=true`** (preset "tots" / 30 % random
chance) + one more add-on. The playground draws a 10-sided fence ring (`Fences.Ring`), benches
on the rim just *outside* the ring, and its equipment as `ObjectRole.Furniture`.
1. **Signs inside the ring:** `RoleCategories.ForObject(Furniture)` = `decoration`, so automatic
   playground equipment (and picnic tables, shelters, restrooms, kiosks, tents, campfires) all
   resolved to the *decoration* default — an arbitrary vanilla "park"-named prop with an icon
   (boards/signs/scoreboards were classified as decoration). Fix: `PlannedObject.Category` (set via
   `LayerContext.Object(..., category)`) on every Furniture placement; new `camping.pitch` /
   `camping.fire` so tents and fires are not the same asset.
2. **Stadium seats as benches:** the bench default scored "bench" +50 and "park" +30, so vanilla
   `SportParkBench01/02` (the yellow/white spectator seat rows) beat `GardenBench01`. Fix: curated
   automatic pools (below) + garden +40 / sport −100.
3. **Every fence segment different:** the fence default was a vanilla residential fence (network
   or `…PieceLow` prop). Its look comes from placeholder lane objects / mesh & colour variations
   picked by `CreationDefinition.m_RandomSeed`, which the build drew fresh per course/piece. Fix:
   `AutoAssets.UniformSeed(category, group)` for fence courses, fence pieces and every object of a
   uniform category (benches, lamps, bins…).

### Rules now (`Core/Build/AutoAssets`, the single resolver for preview, pricing and build)
- One asset per **(step, category)** for everything except `VarietyCategories` = tree, bush,
  flowerbed + the "sets" playground equipment and tents (5 identical swings look like a bug).
- **Steps:** a layer joins its parent layer's step (add-ons share the park's); a top-level layer or
  a split child starts one. Each step picks **one vanilla park palette** from `AreaPlan.Seed`
  (new, set by the composer) and draws `PaletteCategories` (vegetation and furniture) from it when
  it has the category; otherwise the category pool. Fences/nets/surfaces never use palettes.
- Usable palettes: general parks only (`IsGeneralParkPalette`: no sport/skate/dog/gym/water/school…)
  with ≥ 2 of tree/bush/bench/lamp/bin.

### Curation (`AssetHeuristics.AutoEligible`, catalog tags `auto`, `auto:<category>`)
Component data first: `BrandObjectData` (advertising), PlacementFlags Wall/Hanging, RoadSide/
RoadNode/RoadEdge/NetObject (street furniture; lamps/bollards excepted), Shoreline/Floating/…,
exercise activity locations → sports. Then names with exclusion lists (signs, boards, stadium/
spectator/bleacher/stands, construction/cones/barriers, crates/pallets/piles, vehicles, ATMs,
mailboxes, random/placeholder/template…) and per category (bench: sport/tennis/pier/bus stop, and a
"seat" or "chair" is not a bench; fence: industrial/barrier/jump/security…; decoration: only rocks/
boulders/stones/birdbaths/sculptures). Explicit user picks still list the whole category.
Pools: eligible only, vanilla first (modded join only when no vanilla exists; modded network fences
still outrank prop pieces), then score. The catalog logs every category's eligible/total count and
first picks plus the usable palettes at each scan.

### Fences
Network fence preferred (tier before any prop piece); a ring is several courses whose shared
corners are now **bit-identical** (`Fences.Runs` uses the vertex itself for run ends) — CS2 only
merges course nodes at equal positions (`GenerateNodesSystem.NodeKey`), so before this corners
could stay open. Prop fallback: `Fences.PieceCount` = ceil, so pieces reach both corners (no gaps),
one prefab and one seed for the whole ring.

### Verify in game
- Log line "Placemaker automatic pools": bench pool starts with GardenBench*, no SportPark*/Tennis*;
  decoration pool only rocks; fence pool starts with a network fence if the playset has one.
- Park + playground with fence: one fence type, closed corners; one bench type; swings/slides inside.
