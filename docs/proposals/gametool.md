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
