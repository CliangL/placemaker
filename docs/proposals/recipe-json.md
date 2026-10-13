# Recipe JSON format (shipped recipes in `Recipes/*.json`)

Owner of the format proposal: park-plaza workstream. Mirrors `Core.Recipe` / `Core.RecipeStep`
one to one so the loader is a straight mapping. A reference reader lives in
`Tests/Placemaker.Tests/ParkTestKitTests.cs` (`TestKit.ParseRecipe`) and every shipped file is
loaded, validated against the layer schemas and composed end to end by
`ParkSchemaTests.ShippedRecipesLoadAndCompose`.

## Example

```json
{
  "formatVersion": 1,
  "id": "split-park-plaza",
  "nameKey": "Placemaker.Recipe.SplitParkPlaza",
  "descriptionKey": "Placemaker.Recipe.SplitParkPlaza.Description",
  "icon": "Media/Tools/Snap Options/ExistingGeometry.svg",
  "root": {
    "layer": "split",
    "settings": { "ratio": 60, "angle": 0 },
    "children": [
      { "layer": "park",  "target": "part:0", "settings": { "style": "natural" } },
      { "layer": "plaza", "target": "part:1" }
    ]
  }
}
```

## Recipe object

| Field | Type | Required | Maps to | Notes |
|---|---|---|---|---|
| `formatVersion` | int | yes | `Recipe.FormatVersion` | `1`. Bump only for breaking changes; loaders must reject newer versions with a log line, not crash. |
| `id` | string | yes | `Recipe.Id` | Stable, equals the file name without `.json`. Saved in build receipts — never rename. |
| `nameKey` | string | yes for shipped | `Recipe.NameKey` | Localisation key (`Placemaker.Recipe.*`). |
| `name` | string | player recipes | `Recipe.Name` | Literal name when `nameKey` is empty. |
| `descriptionKey` | string | no | `RecipeInfo.description` (UI) | Make-row tooltip. Not in `Core.Recipe` yet — see parkplaza.md. |
| `icon` | string | no | `Recipe.Icon` | `coui://` or `Media/...` path. |
| `root` | step | yes | `Recipe.Root` | |

## Step object

| Field | Type | Default | Maps to | Notes |
|---|---|---|---|---|
| `layer` | string | — | `RecipeStep.Layer` | `IAreaGenerator.Id`. Unknown layer → composer reports `UnknownLayer`, recipe still loads. |
| `settings` | object | `{}` | `RecipeStep.Settings` | **Only overrides.** The loader starts from `generator.Defaults()` and applies these. Values: number → `double`, `true/false` → `bool`, string → `string`, array of strings → `string[]`. Unknown ids are kept (forward compatibility); values of the wrong type are ignored by the layer (`LayerBase.Merge`). |
| `locked` | string[] | `[]` | `RecipeStep.Locked` | Ids Randomize must not touch. Shipped recipes normally leave it empty. |
| `seed` | uint | `0` | `RecipeStep.Seed` | `0` = loader picks one when the recipe is instantiated (shipped recipes never fix a seed). |
| `target` | string | `""` | `RecipeStep.Target` | `""` = parent's own region (add-ons); a tag (`"green"`, `"core"`, `"edge"`, `"part"`, `"block"`) = every matching region; an exact key (`"part:1"`, `"green:0"`) = that region only. |
| `enabled` | bool | `true` | `RecipeStep.Enabled` | |
| `children` | step[] | `[]` | `RecipeStep.Children` | Run after the parent, in order; later siblings see earlier siblings' objects in `Reserved`. |
| `regionOverrides` | object | `{}` | `RecipeStep.RegionOverrides` | region key → step that replaces the matching child there. |

Markers (`RecipeStep.Markers`) are never stored in recipe files: they belong to a concrete area
(player-placed entrances, dividers) and live in the build receipt.

## Region vocabulary emitted by this workstream

| Layer | Regions (key → tags) |
|---|---|
| park | `green:N` → {green} (largest first; `green:0` also {core}), `edge:N` → {edge} |
| campground | `green:N` → {green}, `green:0` also {core} |
| plaza | `paved` → {paved}, `core` → {core, paved}, `edge:N` → {edge, paved} |
| split | `part:N` → {part, part:N} |

## Loader checklist (game layer)

1. Read every `Recipes/*.json` next to the DLL (and `ModsData/Placemaker/Recipes/*.json` for player recipes).
2. Reject `formatVersion` > supported with a warning; skip files that fail to parse (log, never throw).
3. For each step: `settings = GeneratorRegistry.Get(layer)?.Defaults() ?? new SettingsBag()`, then overlay the JSON values.
4. Assign a fresh seed per instantiation when `seed` is 0.
5. Order of the Make row: shipped recipes by the `[AreaGenerator(order)]` of their root layer, then file name.
