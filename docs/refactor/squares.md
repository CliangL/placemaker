# Squares & events: the cut and the survivors (rf-squares, 2026-10-16)

The user's brief (docs/refactor/GRADES.md): Shops & squares are "basically all Fs", mostly because "they just don't work
100% of the time" and "they're not clean". Amphitheatre F ("it just looks horrible"), market square D. This branch covers
the four square and event types that fold into Shops & squares: plaza, market square, amphitheatre, fairground. Make the
kept ones work every time and look clean and distinctive; cut (hide) what can't be made good.

Everything below was judged in the game runner (`tools/suite/Show.ps1`, clean profile, a flat 150 x 140 m city block
unless said otherwise). Grades are mine, from the pictures and the logs.

- BEFORE: `show-31-builds-1016-062611-025f` (every type and style: plan, 45-degree, top, terrain).
- AFTER: AFTER_RUN

## What was wrong (BEFORE), common to all four types

1. **Street shops ate the squares.** Every one of the four types had "Shops along the street" on by default (plaza
   Row/Some, market square Row/Lots, fairground Row/Few, amphitheatre Pads/Few). On a city block that carved 5 to 12
   rectangles of zoned land out of the edge:
   - plots that did not zone stayed as empty grass holes in the paving (log: `MISMATCH`, "3 plot(s) shown in the
     preview but not zoned as shown");
   - the carved outline is a cross, so the plaza's and the market's tree ring (an inset of the outline) failed: no
     trees at all on plaza fountain, garden, green, sunken, cafe, piazza and on every market square; the plaza
     "green" and "garden" lost their lawns the same way (reproduced offline in the gallery with `--sites square`);
   - the shops that grew faced the city street with their backs and car parks to the square (the core agent's
     "plazas and market squares: their shops face the city streets").
2. **No centrepiece in a clean game.** The clean profile has no fountains or statues (`centerpiece=0` in the asset
   catalog line of Placemaker.log; they come with asset packs). Plaza fountain, sunken court, piazza, cafe and the
   market squares left an empty paved disc in the middle; the plan reported `NoAssets centerpiece`. The user's own game
   has them (`centerpiece.fountain=9`, `centerpiece.statue=2` in `.forensics/Placemaker-20261015-master.log`).
3. **Fixed counts on big sites.** Market stalls, fair stalls and the amphitheatre's rows were absolute numbers sized
   for a small site. On a city block the market was 11 to 16 stalls in a corner of an empty paved field, the fair a
   few stalls in an empty meadow, the bowl a small fan in a big lawn.
4. **Assets the user does not have.** No `events.ride`, `events.festoon` or `events.lightTower` in the clean game
   *or* in the user's catalog; only one `events.tent`. The funfair (rides) and the festival (tents, light towers,
   festoons) build an empty field for everyone.

## Every type and style, graded (BEFORE)

### Plaza (14 styles + default)

| Style | Grade | Keep? | Why |
|---|---|---|---|
| default | F | (type default) | Bare paving with an empty rondel, five shop cutouts, no trees. |
| civic | D | keep | Four dense tree bosques round an empty centre; the best plaza structure, but shop cutouts all round. |
| fountain | F | keep | Empty rondel (no fountain in a clean game), no trees (ring failed on the carved outline), shop holes. The user's game has fountains: this is the signature square. |
| market | D- | hide | A small stall cluster on a big slab: the market square type does this. |
| garden | F | hide | Sand-coloured slab, lawns and trees missing; "green" is the same idea done better. |
| sunken | F | hide | 2 m dig that reads as flat from above, nothing in it. |
| amphitheatre | D- | hide | A few bench arcs facing a stage on a slab: the amphitheatre type does this. |
| terraced | F | hide | Flat-looking slab with one statue and stray paths. |
| cafe | D | keep | Kiosks, pergolas and tables on a gridded warm paving: the most lived-in plaza. |
| splitLevel | F | hide | Empty slab with a row of shrubs. |
| cascade | F | hide | Empty slab, stray benches, odd diagonal paving bands. |
| podium | F | hide | Empty gridded slab. |
| sunkenCourt | C | keep | Tree groves, pavilions, a central court: the best-looking plaza BEFORE. |
| piazza | D | keep | Gridded sandstone with kiosks; reads Italian. |
| green | F | keep | Should be lawns and a tree ring; lost both to the shop cutouts. |

### Market square (3 styles + default)

| Style | Grade | Keep? | Why |
|---|---|---|---|
| default / weekly | D | keep | 11 stalls in the middle of an empty slab; tree ring and statue missing. |
| bigMarket | D+ | keep | 44 stalls: the only one that reads as a market. |
| farmers | D | keep | 10 stalls on a sandstone slab; fountain missing. |

### Amphitheatre (5 styles + default)

| Style | Grade | Keep? | Why |
|---|---|---|---|
| default | D | (type default) | A small dug bowl in a big lawn, empty shop pads round the edge. |
| classic | D | keep | Stage and a 10-row bench fan dug 5 m into the ground; fine bones, far too small for its site. |
| cinema | F | hide | A billboard far from a wedge of deckchairs. |
| bandstand | C+ | keep | A round bandstand in rings of deckchairs and a tree ring: pleasant and distinctive. |
| meadow | F | hide | An empty lawn bowl with a tiny stage tent. |
| speakers | F | hide | A tiny podium lost in trees. |

### Fairground (5 styles + default)

| Style | Grade | Keep? | Why |
|---|---|---|---|
| default / fairground | D | keep | Stalls along three streets, play pads, food court, stage; small for the block. |
| festival | F | hide | An empty field: its tents, light towers and festoons are not installed (clean game and the user's). |
| funfair | D- | hide | Its rides are not installed anywhere; it builds playgrounds and a sand slab. |
| christmas | C- | keep | Stall grid, Christmas tree, cobbles: distinctive. |
| foodTrucks | D | keep | Food carts round a dining court: distinctive, too sparse. |

## What is kept

| Type | Kept styles | Hidden styles |
|---|---|---|
| Plaza | civic, fountain, cafe, sunkenCourt, green (+ the type's default) | market, garden, sunken, amphitheatre, terraced, splitLevel, cascade, podium, piazza |
| Market square | weekly, bigMarket, farmers | none |
| Amphitheatre | classic, bandstand | cinema, meadow, speakers |
| Fairground | fairground, christmas | festival, funfair, foodTrucks |

All in `Recipes/shelf/shops.json` (`hiddenStyles`, my four types only); no type is hidden, nothing was deleted, and every
hidden style can still be built by id. Why each cut: plaza market and amphitheatre duplicate the market square and the
amphitheatre types; garden and piazza are weaker twins of green and cafe; the level variants (sunken, terraced, split
level, cascade, podium) read as flat empty slabs from any normal camera; cinema, meadow and speakers were the three Fs of
the amphitheatre; festival and funfair need assets nobody has; food trucks stayed a sparse gravel field.

## What changed (all seen in the game runner, see the runs below)

1. **Street shops are off by default** for plaza, market square, amphitheatre and fairground
   (`Core/Layers/Common/StreetShops.cs` `Defaults`, my four rows only; market square's amount when turned on is now
   "some", not "lots"). The feature still works when the player turns it on; then the shops face the city street, by
   design (a square has no lane of its own). Shared code, minimal: four table entries.
2. **A planted centre when no centrepiece is installed** (`PlazaDressing.SpecimenCentre`, used by plaza, compact plaza
   and market square): a round lawn in the rondel with a specimen oak (three on a big rondel), flowering shrubs round its
   rim and benches looking out. Only when the player chose no centrepiece and the catalog has none (the clean game); the
   user's game has fountains and statues and keeps them (asserted in `SquaresTests`).
3. **Counts scale with the site.** Market stalls (`MarketSquareLayer.ScaledStalls`: the setting is for 0.4 ha, up to
   4x), fair stalls (`FairgroundLayer.ScaledStalls`: 0.6 ha, up to 3x) and the amphitheatre's rows
   (`AmphitheatreLayer.ScaledRows`: 0.6 ha, up to 2x, with gentler steps so a scaled bowl stays within 5 m). Tooltips
   say so.
4. **Tree rings survive a city block's corners.** The bucket tool outlines a block with short chamfers at the junctions
   (8 corners); the per-vertex inset (`LayerRegion.InsetVertices`) folds over on edges shorter than about 3.5 m at a
   5.8 m inset, `Poly.IsSimple` failed and the plaza allée and the market tree ring were silently dropped on every
   block. New `LayerRegion.InsetRing` (a true polygon offset, the largest ring) is used for the plaza allée, the plaza
   level planters, the market tree ring and the fair's loop. Shared code, additive: one new method.
5. **Lawns wherever the entrances are.** The plaza's quarter parterres need the walks on the square's axes; the access
   points the tool suggests round a block (6 to 8, anywhere) cross every quarter, so "green" built a bare slab. When no
   quarter fits, the lawns now fill the free ground between the walks, the centre and the allée, shrunk evenly to the
   asked share, with a tree and flowerbeds in the big ones (`PlazaDressing.FreeLawns`).
6. **Market cross aisles** on the square's own axes (two 4.8 m aisles meeting at the centre) instead of aisles to every
   access point, which cut the stall rows at odd angles.
7. **Fair: the front-of-house tent is off by default** (a lone dark dome in the crowd field).
