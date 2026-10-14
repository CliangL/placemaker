# Art pass a3: shared issues found (beach, waterfront, wetland, nature reserve, greenway, hiking trail, lookout hill, botanical garden)

Everything below was worked around inside the a3 type folders with explicit vanilla asset ids. The shared
pools/rules themselves are not mine to change; these are the fixes I'd ask the owners of `Core/Build`
(CategoryRules / AutoAssets curation) and the gallery to make.

## Automatic pools that place the wrong thing (catalog-dump 2026-10-13)

| category | pool today | problem | proposal |
|---|---|---|---|
| `lookout` | `RuralFireStation_WatchTowerUpgrade` (56 x 32 m, mod), `WaterTower01 Viewing Deck` (32 x 32 x 48 m) | building upgrades/lots, not props. Every "lookout"/"tower"/hide fallback planted a fire station on hills, beaches (lifeguard → lookout), wetlands, reserves, trails. | exclude building upgrades and lots (`… Upgrade`, `Viewing Deck`, anything > ~12 m footprint) from `lookout`; leave the pool empty in vanilla. |
| `beach.hut` | `BicycleParkingHall01/02 Changing Rooms` (40 m buildings) | "changing room" matched a building extension. | exclude extensions/buildings. |
| `camping`, `camping.pitch` | `MakeshiftTent01-04` | homeless tents; `camping.fire` falls back to them, so fire pits became tents. | keep tents out of the `camping.fire` fallback chain. |
| `play.nature` | `Citizen_Logger_Chainsaw01`, `LogPileLarge/Medium/Small` (35 m), forwarder log piles | forestry machinery/stock in parks. | exclude `Citizen_*`, `ForestForwarder*`, `LogPile*`. |
| `fence` | `EU_FenceResidentialHigh01…` first | 2 m garden fences as park/promenade boundaries. | rank low fences/hedges first for leisure. |
| `surface.natural` | `Sand Surface 01` first | meadows, trailheads, viewpoint platforms got sand. | rank `Forestry Surface 01` / grass first. |
| `sports` (volleyball fallback) | skate ramps, goals, `CityHall01 Court` (128 m) | random sports props on a beach; a city-hall courtyard in a sports pool. | exclude `CityHall01 Court`; beach volleyball now uses a 0.7-scaled `TennisNet01`. |

No vanilla kite, telescope, panorama board, reed, bird hide, lifeguard tower, beach hut, boat or fire pit
exists; the a3 types now build or substitute those from vanilla props (stone rings, campground shelters,
gazebos, park info signs, picnic blankets, grassy wild bushes) or skip them.

## Gallery

* `--water` (added, test-only): lake fixture for shore types.
* The gallery's `Catalog.Pool` walks dotted parents for every category (`lookout.kite` → `lookout`), the game
  only for `surface.*` (`AssetCatalogSystem.AutomaticPool`). Gallery renders therefore showed watchtowers
  wherever an empty sub-category was asked for. Proposal: mirror the game (surface-only parent walk).
* The gallery never publishes `AssetAvailability`, so "unknown = available" everywhere; publishing the
  fixture's category counts would make fallbacks render as in game.

## Terrain

Hiking-trail gorge/ridge and lookout-hill terraform are emitted as flat-topped square cells (16 m), which
read as steps in renders; if the game does not blend them, a smoother emission (TerrainShaper-wide) would help.
