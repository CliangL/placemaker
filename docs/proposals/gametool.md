# Game-tool workstream — integration notes and contract proposals

Owner: game-tool. Branch `gametool`. Lead-owned files were not edited; everything below is either
a request or a description of how the tool works around a gap.

## 1. Wiring (lead)

- `Mod.OnLoad`: `Try("tool", () => Game.Tool.ToolRegistration.Register(updateSystem, this));`
  (after build registration). `Mod.OnDispose`: `Game.Tool.ToolRegistration.Unregister();`
  Register creates the settings (Options page + key bindings + build options forwarded to the
  build workstream's statics), loads `Lang/<locale>*.json`, and schedules:
  `PlacemakerToolSystem` (ToolUpdate), `PlacemakerUISystem` (UIUpdate),
  `PlacemakerTooltipSystem` (UITooltip), `PlacemakerOverlaySystem` + `FilledPreviewSystem` (Rendering).
- **csproj: `Recipes/*.json` is not copied to the output/deploy folder.** Please add
  `<None Include="Recipes\*.json" CopyToOutputDirectory="PreserveNewest" LinkBase="Recipes" />`
  next to the Lang line. Without it only synthesized one-layer recipes appear in the Make row.

## 2. UI binding contract (for the UI workstream)

- All six values (`state`, `catalog`, `design`, `summary`, `assets`, `managed`) are
  `ValueBinding<string>` carrying **JSON text** → the UI must `JSON.parse` them
  (`managed` is the string `"null"` when nothing is selected). Proposal: state this in contract.ts
  ("values are JSON strings").
- Numeric trigger arguments are read as `double` (`moveStep`, `manageGroup`, `editGroup`,
  `removeGroup`, `renameGroup`, `setBrush` size/angle). Booleans and strings as typed.
- `state.open` stays true while a build runs (the build service temporarily activates its own tool).
- `primary` with `action: "placeMarkers"`: enters marker mode for `UnreadyStepPath`; while marker
  mode is active the action is still `placeMarkers` with label `Placemaker.Primary.DoneMarkers`
  and pressing it leaves marker mode. Esc does the same.
- Step paths: `"0"`, `"0/1"`; region overrides are addressed as `"<parentPath>/<childIndex>@<regionKey>"`
  and stored in the parent's `RegionOverrides` (composer semantics). `overrideRegion(parentPath, key, layer)`
  only has an effect when some child of `parentPath` targets that region (composer replaces a
  matching child) — the UI should offer overrides only on regions listed in a step that has a
  targeting child.
- `LocText.args` are positional (`{0}`); `Hint.keys` are key names ("Ctrl", "Shift", "Alt", "Esc", "Enter", "Z").
- `assets`: a requested category missing from the object means "catalog still loading".
- Undo/redo labels: `Placemaker.Undo.*` are short verbs ("move corner"); the UI composes "Undo: …".

## 3. Contract proposals

1. **`IBuildService.RequestedApplyMode` instead of the tool switch** (agrees with gamebuild.md §3.1):
   the tool would forward it as its `applyMode`, so no foreign tool is activated mid-build. Today the
   tool keeps all state across the switch, polls the build from the UI system while inactive,
   refuses toggle/close during a build, and keeps Anarchy's `ignoreErrors` alive for the build.
2. **`BuildRequest.IgnoreErrors`** (Anarchy toggle in `state.anarchy`): today the tool sets
   `ToolSystem.ignoreErrors` while it is active and keeps it during its build.
3. **`AreaShape.Holes`** (geometry.md §1): until then paint mode fills holes and block picking
   ignores interior road islands (`FacePick.Holes`).
4. **Recipe description / order in `Core.Recipe`**: `descriptionKey` and `order` live in the game-side
   `RecipeStore` today.
5. `PlanStat` units: the tooltip maps `"m2"|"area"`, `"m"|"length"`, `"money"`, `"%"`; everything
   else is shown as an integer. A shared unit vocabulary in Contracts.cs would help.

## 4. Behaviour summary (for reviewers)

- Stage machine: `Core/Tool/StageMachine.cs` (pure, tested). Reachable: Area always, Design with
  an area, Review with area + recipe, Manage always; only Building while a build runs. Esc steps
  back one level (marker mode → drag → Review → Design → clear selection → close).
- History: one stack (`Core/Tool/History.cs`) of `ToolSnapshot`s (selection of every mode, shape,
  recipe, focus). Gestures commit once on release; slider drags preview live and commit on
  `endPreview`; repeated `setValue` of one control coalesces within 1.5 s.
- Recipes: shipped files are overrides on generator defaults (+ player-saved defaults from
  `ModsData/Placemaker/defaults.json`); files with a newer `formatVersion` are skipped with a log
  line; every Fill layer without a file gets a synthesized one-layer recipe; player recipes in
  `ModsData/Placemaker/Recipes/` (delete keeps a `.deleted` backup).

## 5. Round 2 (playtest 1) — what changed, and requests

### Price = build: automatic asset resolver (dedupe with gamebuild)
- `Game/Preview/AutoAssets.cs` is a **local** resolver because `IAssetCatalog.ResolveAutomatic`
  is not in this branch yet. When a plan arrives (and again when the catalog version changes, and
  right before Build) it writes concrete prefab names into every `AssetRef.Id`:
  nets/surfaces/areas/functional objects via `IAssetCatalog.Resolve(asset, RoleCategories.ForX(role))`
  (exactly what BuildService.ResolvePrefabs does), trees/bushes/flowerbeds without explicit id or
  fallbacks via `Items(category)[Variation % count]`. The build receives the resolved plan, so
  `Pricing.Of` prices what is placed. **Lead:** once gamebuild ships `ResolveAutomatic`, replace the
  body of `AutoAssets.Resolve` with a call to it (one resolver). Surfaces/areas still price at 0
  (no PlaceableObjectData) — gamebuild's catalog would need a per-m² cost if the game charges one.

### Contract notes / small extensions (please mirror in contract.ts)
- `summary.permanentAcknowledged: boolean` (extension): true when `permanent` is empty or
  `acknowledgePermanent(true)` covers the current effects. The acknowledgement is bound to the
  fingerprint of the permanent effects (kind + rounded area); any change of those effects resets it.
  Build/Enter is refused with reason `Placemaker.Reason.AcknowledgePermanent` until then.
- `setMarkerKind(path, kind)`: enters placement for that step with that kind (or just switches the
  kind while already placing on that step); `""` leaves placement. Left-click on the outline places,
  right-click removes the nearest marker of the step **of any kind**.
- `removeMarker(path, index)`: index into `StepView.markerList` (placement order). Undoable.
- `StepView.markerKinds[].label`: the generator's option label when its Markers control lists the
  kind (parking: `Placemaker.Parking.Marker.Entrance`), else `Placemaker.<Layer>.Marker.<Kind>`
  (PascalCase; English keys added to `Lang/en-US.tool.json` for park/campground/plaza/split).
  `icon` is a vanilla `Media/...` path. `markersRequired` = 1 when the layer is not ready with no
  markers (parking), else 0.
- `Control.default` is emitted for every control except `markers`.
- `locate(x, y, z)`: moves the gameplay camera pivot (switching from orbit/cinematic camera first).

### Markers (lead decision overriding FEEDBACK-1 US3)
- Untouched marker steps that run on the whole outline (root, and `target: ""` add-ons under it)
  get `SuggestMarkers` as real markers; re-suggested when the outline changes; filled when empty
  after design changes. Any player add/remove/clear marks the step "edited" (in the undo snapshot)
  and it is never auto-filled again until the area is cleared or a build finishes. Steps that
  target regions (blocks) are left to their layer. Build is blocked only when a required step has
  none (no road for a suggestion, or the player removed them all).
- **parking workstream:** `PlanBuilder` still auto-suggests entrances internally when the step has
  none; with the tool applying suggestions that branch only matters when the player removed all —
  and then CheckReady already blocks Build. Consider dropping the internal fallback so the preview
  shows "no entrance" honestly. Alley/footpath kinds (US3) need `MarkerKinds` + `CheckReady`
  updates in ParkingLayer; the tool/overlay already have icons/colours for `alley` and `footpath`.

### Build failure (US8)
- `Core/Tool/BuildFeedback.cs` owns the failure message + red markers: cleared on any area/design
  edit, stage change, build start and success. Failure lands in Review (or Area if the outline is
  gone) with the message plus `Placemaker.Issue.BuildFailedNext`; Build stays enabled for a retry.
  Tests: `Tests/ToolBuildFlowTests.cs`.
- FilledPreviewSystem: layers hidden by EndFrame forgot to reset their signature, so the same
  rings shown again later drew an empty mesh — fixed.

### Icons (verified against Cities2_Data/Content/Game/UI/Media, 1.6.2f1)
- `Game/UI/IconPaths.cs` filters every icon the catalog/design JSON emits: accepts `Media/...` and
  `coui://GameUI/Media/...` (normalised to `Media/...`), maps known renames, drops (with one log
  line) vanilla paths that do not exist. Other hosts pass through.
- Wrong icons owned by others:
  - `Core/Layers/Parking/ParkingLayer.cs:23` `Media/Game/Icons/ParkingFacility.svg` — **does not
    exist**; use `Media/Game/Icons/Parking.svg` (parking workstream). Filter maps it meanwhile.
    `Recipes/parking*.json` fixed here.
  - roads branch: `coui://placemaker/roads/pattern-…` — needs a registered `placemaker` UI host
    (`UIManager.defaultUISystem.AddHostLocation`), otherwise Gameface logs "Invalid host".
  - All other layer icons (Forest, ParksAndRecreation, Water, Sewage, Pathways, Roads, Zones,
    `Media/Tools/Snap Options/ExistingGeometry.svg`) exist.
