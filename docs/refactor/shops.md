# Shops (category agent "shops", branch rf-shops)

The user's grade: **F** — "they just don't work 100% of the time and they're not clean". Mall district "doesn't work and
looks wrong", "the parking lots look weird", "farmland, giant holes, zoning that isn't applied".

Scope: the eight commercial types of the Shops & squares category (stripMall, shoppingPlaza, outdoorMall, shopsPark,
mallPark, highStreet, retailPark, mallDistrict). Plaza, market square, fairground and amphitheatre are the squares
agent's; both agents edit `Recipes/shelf/shops.json` (this branch only adds the commercial types' entries).

## BEFORE (master a71a842 + the grow census)

Run `show-46-builds-1016-062815-e662`: every type and style on the default flat city block, grow 40 s, 45° and top
pictures (clean and zoning), the plan and the terrain map. 45 builds (mallDistrict-village found no free block).

What the logs say (`grow census`: which road each grown building inside the outline faces; `zoned a of b`; pit check):

| Type | Buildings grown (all styles) | Facing | Pits | Notes |
|---|---|---|---|---|
| stripMall | 2, 2, 4, **0**, 9, 4 | all our lanes | none | a sea of parking round a short shop row; the middle of the row never zoned (a plot straddled the lot's joins); rear alley built no shop at all (its shops are street shops, planted since core) |
| shoppingPlaza | 7–8 | our lanes | none | works; a U lane round a court — the same idea as the outdoor mall loop and the squares' plaza/market |
| outdoorMall | 6, 6, 6, 17, 8, 4 | our lanes | none | a big sand plaza (no paving asset in the clean profile), side lots; lifestyle grows 4 |
| shopsPark | 3–11 | **city streets** (by design) | none | a park with street shops: the Parks category's job |
| mallPark | 4–8 | our lanes | none | the loop had almost no shops on the default block (access lanes cut every side too short); lots everywhere |
| highStreet | 8–10 | our lanes | none | the strongest: a market strip between two pedestrian streets; shared preset puts 5 plots on the street |
| retailPark | 9–15 | **city streets** | none | on the default block the horseshoe never fits: a street-row fallback and a giant lot; trade is industrial zoning (smoke) |
| mallDistrict | 14–16 | our lanes | none | works on the default block, but its outer rows were planted and replaced by odd lots; on a curved block it falls back to a sea of lots |

No farm lots, no fences and no pits on flat ground (fixed by the core refactor). The failures left are the
compositions: too much parking, too few shops, and fallbacks on blocks that are not a plain rectangle.

Offline, before any change (gallery sweep, square 1–4 ha, half-curved and curved blocks): 69 builds fall back
(`Commercial.Issue.Fallback`); retail park falls back on every block up to 1.6 ha; mall district and mall park fall
back on both curved blocks.

## Decisions

| Type | Style | Grade before | Keep / hide | Why |
|---|---|---|---|---|
| stripMall | classic (default) | F | keep | the archetype; fixed (below) |
| | corner | D | keep | distinct L-shaped corner retail |
| | neighbourhood | D+ | keep | small shops, small lot |
| | bigBox | D | keep | big units, high density |
| | rearAlley | F | **hide** | its shops face the street by design; with the street shops planted it builds none |
| shoppingPlaza | all | D+ | **hide type** | works, but it is the outdoor mall loop again, and its plaza/market courts are the squares' types |
| outdoorMall | promenade (default) | D | keep | a pedestrian street ending on a plaza |
| | grand | C- | keep | wide pedestrian street, tall shops |
| | loop | C- | keep | the most shops of the type |
| | village | C- | keep | mixed use, dense |
| | lifestyle | D- | **hide** | four shops for a ring drive, three lots and paseos |
| shopsPark | all | D | **hide type** | a park with street shops: every shop faces the city street by design; Parks covers it |
| mallPark | commons, garden, family | D | keep | shops ring a park — distinct; fixed (below) |
| | promenade | D | **hide** | its pedestrian loop leaves the corners to odd lots |
| highStreet | market (default), farmers, cafe, boulevard | C | keep | the best of the category |
| | shared | D | **hide** | car lanes instead of pedestrian streets, plots on the city street; the weakest variant |
| retailPark | classic, megastore, outlet | D | keep | big boxes round one lot — distinct; fixed (below) |
| | trade | D- | **hide** | industrial zoning in a shops category (factories, smoke) |
| mallDistrict | all five | C- | keep | the user's named type; fixed (below) |

## Changes (all in the commercial layers, `Core/Layers/Commercial/`)

1. **Parking budget** (`CommercialLayer.LotShare`, `Mall.LotBudget`): the composition's own lots always go in; the
   leftover lots ("lot first", s50) stop at 40 % of the site (retail park 60 %). The budget is shared evenly by the big
   leftover pieces, each lot cut by its way in (`Mall.NearEntrance`), so both sides of a composition get the same lot and
   the rest is planted. Leftover lots need ten stalls (a handful of stalls was a stray patch of asphalt) and lay their
   rows square to the main street (`Mall.MainDir`; they used to follow their own entrance edge and sat askew).
2. **Shop rows split at lane joins** (`Mall.ShopRow`): a lot's alley stub or a footpath splits the lane into edges with
   their own cell grids; a plot straddling one was never zoned (the strip mall's middle plots, "CutoutNotZonable").
3. **The main frontage is retried** (`CommercialLayer.PlanCore`): when a composition falls back on the longest run, the
   next longest runs are tried from a clean plan (`PlanMark`), else the old fallback is kept.
4. **The best stretch of a run** (`Mall.BestSpan`): mall district and mall park lay their grid on the stretch with the
   most land straight behind it, not on the whole run with its smallest reach (52 m of 150 on the half-curved block).
5. **Extra compositions** (`ComposeMore`): the street opposite the main one first, then the most free land; a
   composition that does not fit there leaves nothing behind (`Mall.Extra`, no fallback street row). The strip mall
   composes a second strip on the back street when the first leaves half the block (`ComposeMoreBelow`, `MoreReach`,
   free reach in `Classic`): two strips back to back instead of half the block of lot.
6. **Retail park on a normal block**: when the side terraces do not fit, the road hugs the sides and the boxes stand
   along its back run only, as deep as asked (the default city block got a street-row fallback before).
7. **Mall park**: an access lane only crosses a ring side long enough to keep zonable rows either side of it (58 m);
   on the default block the loop had no shop.
8. **Street rows stay planted for every kept type** ("Shops facing the streets" off by default, as the core set it). Tried
   and undone: keeping the mall district's and mall park's outer rows (big buildings on the streets) took the cells of the
   lane rows behind them — the city street's block reaches 48 m, the older block wins (it1: the mall park ring lost 7 of 11
   plots, the mall district 26 of 110 cells). Instead the mall district and mall park lay their lane grid over the whole
   site when street shops are off (no outer rows), and the high street keeps its outer rows planted.
9. **Lane plots keep clear of a city street's first zone row** (`Mall.BacksOntoStreet`, `Mall.KerbMargin`, `GateTrim`): the
   core's kerb strips sit in that row, and a lane plot reaching into it loses its cells (it1: the retail park's back
   terrace, 54 cells planned, 14 zoned; it3: every ring row of the mall park and the outer lane rows of the mall district
   next to the 4-lane Medium Road). Rows stop a cell short of the street (by the street's own width), lanes leaving a
   street start their rows past it, and the mall district/park fit their grid inside that margin.
10. **Grey paving** for the outdoor mall's plazas and the high street's café strip (the sandstone palette read as a sand lot).
11. **Hidden styles** (shelf): mall park promenade (its pedestrian loop leaves the corners to odd lots).

Shared code touched: `Game/Suite/SuiteSteps.Build.cs` and `SuiteLookup.GroupOf` (public): the **grow census** — after a
grow step, Placemaker.log says how many buildings inside the outline face the build's own lanes and how many face city
streets. Measurement only.

## Done

Wrapped up on the user's word (the shared game queue was backed up for hours); no further game runs.

- Changes 1-11 above, all in `Core/Layers/Commercial/`, plus the grow census (measurement only). Test fingerprints
  regenerated on purpose (360a915, 792bfd1).
- **Kept**: stripMall (classic, corner, neighbourhood, bigBox), outdoorMall (promenade, grand, loop, village), mallPark
  (commons, garden, family), highStreet (market, farmers, cafe, boulevard), retailPark (classic, megastore, outlet),
  mallDistrict (all five).
- **Hidden** (`Recipes/shelf/shops.json`, code kept): types shoppingPlaza (repeats the outdoor mall loop and the
  squares' courts) and shopsPark (street shops in a park, Parks' job); styles stripMall rearAlley (builds no shop),
  outdoorMall lifestyle (four shops), highStreet shared (car lanes, plots on the city street), retailPark trade
  (industrial zoning), mallPark promenade (corner lots).
- BEFORE run: `show-46-builds-1016-062815-e662`. Latest AFTER run: `shops-it3-081855` (built from c8130ec-dirty: everything
  up to the grey paving, not 55f4b8b). In it3 every built scenario zoned all its cutout cells, and outside the mall park
  every grown building faces our own lanes (strip mall 8/4/9/10, high street 10/9/1/9, retail park 4/3/4/8 on
  block/small/L/slope).
- Pictures (under `C:\Users\jack\cs2-area-tool\.forensics\suite\runs\`):
  before `show-46-builds-1016-062815-e662\shots\02-stripMall-classic.06-45-clean.png`,
  `show-46-builds-1016-062815-e662\shots\41-mallDistrict-default.06-45-clean.png`;
  after `shops-it3-081855\shots\01-stripMall-default-block.06-45-clean.png`,
  `shops-it3-081855\shots\05-mallDistrict-default-block.06-45-clean.png`.

## Not done / known problems

- **55f4b8b is not tested in the game** (rows backing onto a city street keep clear of its first zone row by the
  street's width; mall park promenade hidden). No run was made after it.
- Mall park (it3, default block): 0 buildings face our lanes, 4 face the city Small Road, 22 cells zoned. The ring rows
  are still lost next to the street; 55f4b8b aims at this.
- Mall district (it3, default block): 20 cells, 6 buildings (14-16 buildings before); its outer lane rows next to the
  4-lane road were lost. 55f4b8b aims at this too; until a run confirms, the mall district is worse than before.
- High street on the L plot: 4 cells, 1 building.
- outdoorMall and mallPark found no small / L / slope plot in it3 (no-block): only the default block was checked.
  Curved blocks were checked offline only.
- Non-default styles were not re-run in the game after the changes (it1 and it3 ran defaults only).
- The offline fallback count (69 in the gallery sweep before) was not re-counted after the last changes.
- The squares agent also edits `Recipes/shelf/shops.json`: merge both hidden lists by hand.
