# Asset survey — designing park types from real assets

Placemaker writes the classified asset catalog to

    %USERPROFILE%\AppData\LocalLow\Colossal Order\Cities Skylines II\ModsData\Placemaker\catalog-dump.json

every time the catalog is scanned (game load, new assets loaded) and again when icons change
(at most every 10 s). It reflects the player's actual playset (DLC, Paradox mods, local assets).

## Getting a dump
1. Enable Placemaker (and the asset packs/mods you want surveyed, e.g. Asset Icon Library for icons).
2. Load any city (or start a new one) and wait ~10 s on the map.
3. Copy `catalog-dump.json`. Settings hook to turn it off: `AssetCatalogSystem.WriteSurveyDump`.

## Format
```json
{
  "version": 3, "written": "2026-10-13T16:20:00Z",
  "counts": { "bench": 64, "decal": 120, ... },
  "automaticPools": { "bench": ["GardenBench01", ...], ... },   // what "Automatic" picks, best first
  "palettes": ["palette:CityPark03", ...],                       // vanilla park palettes used for dressing
  "categories": {
    "rock": [ { "id": "Boulder03", "displayName": "Boulder", "tags": ["rock","source:vanilla","auto:rock","auto"],
                "auto": true, "vanilla": true, "icon": "coui://…", "iconSource": "ui",
                "bounds": { "min": [..], "max": [..], "size": [x, y, z] }, "cost": 0 } ]
  }
}
```
- `id` is the prefab name used in recipes (`AssetRef.Id`) and saves.
- `tags`: categories, `source:vanilla|mod`, `auto:<category>` (eligible for automatic picks),
  `palette:<park>` (seen in a vanilla park), `theme:<x>` (decal themes).
- `bounds.size` is metres (x width, y height, z depth) — use it for spacing rules.
- `iconSource`: `ui` (game/AIL icon), `swatch` (rendered surface texture), `thumbnail`, `group`, `none`.

## Designing a new park type
1. Pick the categories you need and check `counts` / `automaticPools` — an empty pool means the
   feature needs explicit picks or a new heuristic (`Core/Build/AssetHeuristics.cs`).
2. Look for themed assets by tag/name (e.g. `palette:SkatePark01`, `theme:graffiti`, `rock`).
3. Use `bounds.size` to set spacing/clearances in the layer.
4. Missing classification? Add a name/trait rule + a test in `BuildAutoConsistencyTests` and attach
   the dump excerpt to the issue.

Quick look with PowerShell:
```powershell
$d = Get-Content "$env:USERPROFILE\AppData\LocalLow\Colossal Order\Cities Skylines II\ModsData\Placemaker\catalog-dump.json" | ConvertFrom-Json
$d.counts
$d.categories.'decal.leaves' | Select-Object id, vanilla, @{n='size';e={$_.bounds.size -join ' x '}}
```
