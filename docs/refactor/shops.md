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
| mallPark | commons, garden, family, promenade | D | keep | shops ring a park — distinct; fixed (below) |
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
8. **Mall district and mall park keep their outer rows** ("Shops facing the streets" on by default): the big buildings
   facing the streets are the design ("big outside, little inside"); every inner plot still faces our lanes. Their kerb
   strips spare those columns (ZoneGuard.KerbStrips).

Shared code touched: `Game/Suite/SuiteSteps.Build.cs` and `SuiteLookup.GroupOf` (public): the **grow census** — after a
grow step, Placemaker.log says how many buildings inside the outline face the build's own lanes and how many face city
streets. Measurement only.

## AFTER

(filled in below after the runs)
