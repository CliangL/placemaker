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
