# UI workstream — notes & requests for the C# side

Owner: ui. The panel consumes `UI/src/contract.ts` as is and tolerates any field below being absent.
The browser mock (`UI/mock`, `npm run mock`) implements all of it, so behaviour can be tried there first.

## 0. Round 2 (playtest 1) — what the UI now expects from C#

| # | C# must provide | Used for |
|---|---|---|
| 1 | `StepView.markerKinds` for parking: `entrance` (two-way), `entry`, `exit`, `alley`, `footpath` — each `{kind, label, icon?, tooltip?}`. `label` is a loc key; the UI has English fallbacks per kind. `icon` optional (bundled glyphs exist for all five). | "Entrances" section: one type button per kind. When present, the step's `markers` **control** is hidden (the section replaces it). |
| 2 | `StepView.markerList` (`{kind, index, position:[x,y,z]}` in placement order) incl. **auto-suggested** entrances (they are real, removable markers). | Entrance list with type icon, "Show" (→ `locate`) and X (→ `removeMarker`). |
| 3 | `StepView.markersRequired` (parking: 1). | "Place at least one entrance" line + primary disabled with that reason while the count is **0** (`markerList.length < markersRequired`). C# should also keep `primary.enabled=false` in that case (Enter). |
| 4 | Triggers `setMarkerKind(path, kind)`, `removeMarker(path, index)`, `locate(x, y, z)` (three numbers), `acknowledgePermanent(ack)`. | Clicking a type button calls `startMarkers(path, kind)` (only when not already placing for that step) **then** `setMarkerKind(path, kind)`; clicking the active type again calls `primary` (Done). |
| 5 | Primary in Design: **no longer** `placeMarkers` for missing entrances — use `toReview` disabled with reason "Place at least one entrance" (`Placemaker.Reason.NeedEntrance`). `placeMarkers` stays the "Done placing" action while marker mode is on. | Matches US3: Build is blocked, not hijacked. |
| 6 | `Control.default` for every control (UiModels) and `Control.group` for all parking/park controls (e.g. `layout`, `zoning`, `extras`, `paths`, `planting`, `water`, `edge`) + loc keys `Placemaker.UI.Group.<group>`. | All options are always visible, grouped under headings; groups with essentials first. Ungrouped controls show under "Basics". |
| 7 | `RecipeInfo.description` (one line) for every shipped recipe. | Type cards: icon, name, one line (full text in the tooltip). Recipes whose id is not a non-add-on layer id are listed under **Mixes**. |
| 8 | `Summary.cost/upkeep` real values (resolver fix) — UI shows them as is. | Review. |

### Icon paths (C#-owned) — fix list

The UI no longer builds `coui://GameUI/…` (hosts are lower-cased → `coui://gameui` → "Invalid host
locations map"); `Media/…` strings are now passed **relative**, exactly like vanilla. Checked against
`Cities2_Data/Content/Game/UI/Media` (1.6.2f1):

| File | Icon | Status |
|---|---|---|
| `Core/Layers/Parking/ParkingLayer.cs:23` | `Media/Game/Icons/ParkingFacility.svg` | **does not exist** → use `Media/Game/Icons/Parking.svg` |
| `Recipes/parking.json`, `parking-compact.json`, `parking-green.json` | same | **does not exist** → `Media/Game/Icons/Parking.svg` |
| `BigTreesAddOn`, `CampgroundLayer` | `Media/Game/Icons/Forest.svg` | ok |
| `FlowerbedsAddOn`, `PicnicAddOn`, `PlaygroundAddOn`, `ParkLayer` | `Media/Game/Icons/ParksAndRecreation.svg` | ok |
| `PlazaLayer` | `Media/Game/Icons/Pathways.svg` | ok |
| `RestroomsAddOn` | `Media/Game/Icons/Sewage.svg` | ok (odd choice) |
| `FountainAddOn` | `Media/Game/Icons/Water.svg` | ok |
| `SplitLayer`, `Recipes/split-park-plaza.json` | `Media/Tools/Snap Options/ExistingGeometry.svg` | ok |

Never prefix with `coui://gameui/`. The type cards and step headings prefer the UI's own bundled role
glyphs by id anyway; game icons show where no bundled glyph matches, and fall back to ours on load error.

## 1. Earlier contract notes (still valid)

- `ManagedGroup.missingAssets?`, `modified?`, `removing?` would let the Manage card show what the
  info section shows.
- **Override paths**: `parent/childIndex@regionKey` as Core/Tool/RecipePaths; the UI only echoes `StepView.path`.
  Overrides still render (as their own option section) but the UI no longer creates them.
- **`panelCollapsed`**: set while the player draws an open outline, cleared when it closes or the stage
  changes; respect `setPanelCollapsed` until the next stage change. Collapsed = header + area-mode
  icons + primary + a labelled "Show options" button.
- **Selected-info section** (`Placemaker.Game.Groups.PlacemakerGroupSection`): reads `groupId, name,
  recipeNameKey, stats[], upkeep, memberCount, missingAssets, modified, removing` (+ optional `canEdit`).
- **Values**: JSON strings or raw objects are accepted; partial objects merge over defaults; malformed
  JSON yields defaults, never a crash.

## 2. Removed from the UI in round 2 (engine untouched)

"Save as my recipe", delete recipe, the layer-tree editor ("Add to this design", reorder, retarget,
per-region override chips) and the "More options" foldout. The triggers (`addLayer`, `moveStep`,
`removeStep`, `setStepTarget`, `overrideRegion`, `saveRecipeAs`, `deleteRecipe`) stay in `bindings.ts`
unused. Add-on children of a mix keep their on/off switch (`setStepEnabled`).

## 3. Rendering rules learned (Gameface)

- Icons are plain `<img>` of SVGs with the colour baked in at build time (`tools/svg-tint-loader.js`,
  tints in `tools/icon-tints.json`), emitted as `images/placemaker/<name>-<tint>.<hash>.svg`. CSS
  `mask-image` + `currentColor` rendered nothing in game; `tools/css-lint.js` now rejects CSS masks.
- Percentage-width flex cells carry no padding (gutters are the child's margin) so layouts don't depend
  on `box-sizing`.
- Panel background = dark base `rgba(12,18,23,.84)` under `--panelColorNormal` → ≥ 0.92 combined alpha.
- Tooltips: body tips open to the side away from the screen edge (left when docked right), header tips
  open upwards — none can cover the stage bar.

## 4. Build / repo

- `UI/mock/host` targets net9.0 with `RollForward=Major`.
- `npm run build` regenerates `Lang/en-US.ui.json`, fails on Gameface-incompatible CSS, and cleans stale
  `images/` in the output folder (nothing else there is touched).

## 5. Open UX questions

1. Header tooltips open upwards over the game's top bar; acceptable?
2. Should "Places inside" (round 3) reuse the type cards in a flyout like the asset picker?
