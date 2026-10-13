# UI workstream — contract proposals & notes for the C# side

Owner: ui. The panel consumes `UI/src/contract.ts` as is; everything below is **optional** — the UI
already tolerates its absence — but each item removes a workaround or a gap. The browser mock
(`UI/mock`, `npm run mock`) implements all of them, so they can be tried there first.

## 1. Contract additions

| # | Change | Why | UI today without it |
|---|---|---|---|
| 1 | `Control.default?: SettingValue` (= `ControlSchema.Default`) | Per-control "reset to default" shown **only when changed**; `visibleWhen` evaluated against defaults for unset values. The mock host already emits it. | No per-control reset; only "reset step". |
| 2 | `LayerType.defaults?: Record<string, SettingValue>` | Mock/editor convenience (fresh step values). | Built from `Control.default`. |
| 3 | Trigger `locate(atJson: string)` (not registered yet) — `[x,y,z]` | "Show" button on review issues with `at` → move camera there. | Button fires it; no-op if unbound. |
| 4 | ~~`endMarkers()`~~ — resolved: in marker mode the primary is `placeMarkers` (Done) and the UI's Done calls `primary`. | | |
| 5 | Trigger `acknowledgePermanent(ack: boolean)` | The permanent-effects tick box gates Build in the UI, but **Enter** goes straight to C#: C# should refuse `primary` (`build`/`finishEdit`) while `summary.permanent.length > 0` and not acknowledged, and reset the flag whenever the plan changes. | UI-side gating only (button). |
| 6 | `ManagedGroup.missingAssets?: boolean`, `modified?: boolean`, `removing?: boolean` | Same facts the info section already shows. | Not shown in the Manage card. |

## 2. Conventions the UI relies on (please keep)

- **Override paths**: `parent/childIndex@regionKey` exactly as Core/Tool/RecipePaths (the mock follows it);
  its children continue with `/i`. The UI never builds paths itself — it only echoes `StepView.path`.
- **`StepView.regions`**: regions produced by *that* step in the latest plan (plan layers whose
  `StepPath` before `@` equals the view's path). Drives "Make one block different" and the split part
  targets.
- **LocText keys** sent in `state`/`summary` are the tool workstream's (`Lang/en-US.tool.json`); the mock uses the same keys. Undo labels are lowercase ("add corner"); the UI capitalises them.
- **Control.default** is not emitted by UiModels yet → per-control reset is hidden in game until #1 lands.
- **`panelCollapsed`**: set it while the player draws an open outline (any click on the map in Draw /
  Road loop mode) and clear it when the outline closes or the stage changes; respect
  `setPanelCollapsed` from the player until the next stage change. The mock does exactly this.
- **Primary actions** (as the mock implements them): area → `toDesign` (disabled: no area / not
  closed / shape problem); design → `placeMarkers` when a step needs markers (label "Place {kinds}"),
  else `toReview`; review → `build` ("Build {recipe}") or `finishEdit` when editing, disabled while
  planning/stale, on error issues, or when `!canAfford`; building → `cancelBuild`.
- **Selected-info section** (`Placemaker.Game.Groups.PlacemakerGroupSection`): the component reads
  `groupId, name, recipeNameKey, stats[], upkeep, memberCount, missingAssets, modified, removing`
  exactly as `OnWriteProperties` writes them. Optional extra: `canEdit` (false hides Edit).
- **Values**: JSON strings or raw objects are both accepted; partial objects are merged over defaults,
  malformed JSON yields defaults (logged once), never a crash.

## 3. Build / repo

- `Tools/**` is **not** excluded by `Placemaker.csproj`; anything placed there gets compiled into the
  mod. Proposal: add `Tools\**` to the `<Compile Remove>`/`<None Remove>` lists. The mock host therefore
  lives in `UI/mock/host/` (already excluded with `UI\**`).
- `UI/mock/host` targets net9.0 with `RollForward=Major` (this machine only has the .NET 10 runtime).
- `npm run build` regenerates `Lang/en-US.ui.json` and fails on Gameface-incompatible CSS
  (`UI/tools/css-lint.js`).

## 4. Open UX questions (for the user)

1. Dock position: bottom-centre above the toolbar (current) vs. left column next to vanilla tool
   options. Bottom-centre keeps it under the eye; it covers ~40 % of the screen height in Design.
2. Should switching recipe after customising ask before discarding the custom values (today: one undo
   step brings them back, no dialog)?
3. Presets that equal a "Style" choice (real park layer) duplicate controls — keep presets for
   multi-value looks only?
4. Hotkey: the launcher advertises Ctrl+Shift+A (free per RESEARCH-NOTES 26) — confirm.
