# Shops, pass 3: "like a true amazing architect was the developer" (2026-10-16)

## The user, verbatim (after playing the shops rebuild)

> Okay, but I still want to see more. They all need to be kind of more organic. I get that they're square. They're not
> bad. They're kind of all a B minus or a C plus. What about pedestrian paths, cutting things through, pedestrian paths
> going to the street? What if I could place an entrance that would connect to the alleyway? Look at some of the
> images I sent. They're much more organic, like an architect actually made them. Can you do another pass and make it
> more like an architect made it? Like a true amazing architect was the developer. And you could use the game's tool
> calls to iterate his process. And he added more ideas from other things, like terrain painting rather than just
> drawing, or incorporating terrain painting, incorporating decals, desire paths. He could draw dirt where the
> buildings haven't been built yet. He could draw dirt where each building grid would be. He could add a lake, or all
> the different things you can think of that a nice mall would have. Think of hundreds of things to add, more shapes,
> more extras, more everything. Bike paths, bike parking stalls. That's supported in the game.

The reference pictures are in `docs/refactor/shops-ref/` (`real-1..3`). Look at how the real ones are composed:
- buildings clustered and stepped, not in one straight row;
- curving drives and roundabouts;
- landscaped islands and tree-lined medians;
- pedestrian spines and crosswalks from the street to the front doors;
- pocket plazas with seating, and a pad building (restaurant or bank) out by the road;
- service lanes behind the stores;
- planted edges along the road, and a variety of parking-lot shapes.

Today's grade: **B- / C+**. Target: **A**. Every kept style should look like a place a good architect designed.

## Scope

- The 8 kept shops styles: Mall district (district, downtown); Mall park (commons, garden square, family park); Strip
  mall (classic, big box); Market square (big market).
- Make them organic and designed, and add many more extras.
- New styles only if one is exceptional, and then hide a weaker one; the user wants fewer, better things.

## Ideas (start here, then think of more; the user said "hundreds")

**Movement**
- A pedestrian spine through the site. Footpaths from every street to the shopfronts, with crosswalks across the lot.
- Desire paths (worn dirt or footprint decals) where people would cut corners.
- Entrances the user places that connect to the alley or service lane (typed entrances: car, pedestrian, alley,
  service).
- Bike paths through and around the site. Bike parking stalls by the doors (transport.bikeRack).
- Drop-off loops. A transit stop with a shelter on the main street.

**Layout**
- Curved and organic drive aisles, not only a grid.
- Clustered and stepped buildings with varied setbacks. Outparcel "pad" buildings by the road.
- A roundabout at the main entrance.
- Service and loading lanes behind the stores, kept apart from customers.
- Angled parking on some aisles. Tree islands at every row end. Planted medians.
- Corners and L-shapes used well, instead of leftover grass.

**Ground**
- Terrain paint instead of hard surfaces where it reads better: dirt, gravel, mulch under trees.
- Soft blended edges. Dirt or gravel pads on plots that haven't grown yet, so they read as building sites.
- Wear decals at entrances and oil stains on aisles, kept subtle.

**Water and green**
- A retention pond or small lake as a landscape feature, using the hidden pond layer or a water surface.
- Rain gardens and bioswales between aisles. Flower beds at the entrances. Lawns with tree groves at the edges.
- Hedges screening the lot from the street.

**Life**
- Café seating, kiosks, food trucks.
- A fountain or sculpture in a pocket plaza, with benches, planters, lamps and bollards.
- A playground in family-oriented styles. Outdoor dining terraces.
- EV chargers. Cart corrals. Bins and recycling. Vending. Signage and wayfinding.
- An events lawn or a market corner.

**Shapes**
- Every style must look designed on: the normal block, a block four times bigger, a long rectangle, an L, a circle,
  and a slope.

## Must still hold (from earlier passes)

- Builds work every time, shops face our lanes, no pits, no farm lots, and trees and props stay visible.
- Fix the open problem: on the normal-size block the strip mall zones only 14 of 46 planned cells (big box 24 of 66).
  Find the real cause.
- Hide things through `Recipes/shelf`; never delete code.

# The architect's concepts (rf-shops3)

(This is `shops-architect.md`: on Windows that name and this brief's are the same file, so the concepts live here.)

The shops themselves are grown by the game on zoned cells facing a road, so a building's footprint is the game's. The
design is everything round them: which roads the shops face, how wide and deep each plot is (anchors, inline shops,
pads), the lots, the walks, the ground and the green. Organic here means: a road that follows the site instead of a
grid laid on it, soft corners, designed edges instead of leftover lawn, walks where people go, groves instead of tree
lines, water, ground that shows use, and a focal place (a plaza, a pond, a square) the layout points at.

## Three rules from the user (2026-10-16, while this pass ran)

1. **Entrances like ParkingLotTool.** Every shops style waits for the player's entrances (two-way, way in, way out,
   driveway, driveway in/out, footpath: the parking type's kinds, icons and words) and builds nothing to the streets
   unless the player placed it. The drives, lots and walks are planned from them (`Core/Layers/Commercial/MallEntrances.cs`,
   `MallRoute.cs`).
2. **Round and odd sites are a real case.** The baseline circle was "a park, basically no parking": the land's outline
   sat 8 m off the kerb (Show.ps1's default gap), so the strip mall found no street and planted a pocket park. Every
   style must build a shopping place on a circle, an L, a site touching no street: its own ring drive, reached through
   the entrances (`MallRing.cs`).
3. **Trees and paint on the plots, never surfaces.** A plot drawn 3 x 3 may grow a 2 x 2 building: trees at the back of
   each plot, lawn painted there, the building site (dirt) painted at the front; the growing building clears what is in
   its way (to verify in the game).

## The kit (shared; `Core/Layers/Commercial/MallDesign.cs`)

Every piece is an extra (panel group "Extras") with a per-style default: walkways (a spine through the lot to the
shops with a crosswalk, a meandering promenade through the planted frontage, walks from footpath entrances), a pocket
plaza at the shopfront (café tables, benches, planters, a fountain on big ones), bikes (racks, or bike paths), food
trucks, pad sites, landscape (groves with shrubs and mulch, flowerbeds at the drives, never a bare lawn), hedges in
front of the lots, building sites (dirt on unbuilt plots), plot trees, worn ground (desire paths, worn walk ends),
water (a retention pond, rain gardens with lush ground and wet trees).

What is built (control ids `Placemaker.Commercial.<id>`, group "Extras" unless said):

| Extra (id) | Builds | On by default |
|---|---|---|
| Entrances (`entrances`, group Entrances) | two-way, way in, way out (invisible drives from the kerb, arrows), driveway / in / out (real junctions, also through a row of shops: a service alley), footpath (a walk from the sidewalk, joining the spine when one is near), bus stop (on the street in front) | the player places them; Build waits |
| Walkways (`walkways`) | the spine: a tree-lined walk from the ring's front drive through the lot to the first row of shops (lamps, a bench pair, desire lines across its planted strips, worn ground at its street end); the frontage promenade, meandering through the anchors, benches and low lights along it; desire lines from its dead ends to the street | all |
| Pocket plaza (`pocketPlaza`) | paving, a fountain on big plazas (else a tree in a ring of benches), café patios, planters, lamps, a vending machine | strip mall |
| Bikes (`bikes`: none / racks / paths) | racks at the plaza and at footpath entrances; paths: the promenade as a bike path | racks; big box paths |
| Food trucks (`foodTrucks`) | carts at the plaza, the park gates, the district's café squares | 1-3 |
| Pad sites (`pads`) | 3 x 3-cell plots inside the ring's front corners (a restaurant, a bank by the road) | strip mall |
| Planting (`landscape`: plain / designed) | every leftover as groves with shrubs, mulch, flowerbeds at drives and lane mouths; boulevard trees along the front drive; a pergola on long frontages; islands from a ring's inner corners | designed |
| Hedges (`hedges`) | a low hedge between the frontage and the lot | all |
| Building sites (`buildingSites`) | dirt painted on the front of every plot | all |
| Trees on plots (`plotTrees`) | trees and shrubs at the back of every plot, on painted lawn; a growing building hides what is in its way | all |
| Worn ground (`wornGround`) | desire lines, mulch under groves, worn patches; subtle ground decals (worn patches, leaves, stains) where the catalog has them | all |
| Water (`water`: none / pond / rainGardens / both) | a retention pond in a front corner of the big box (rain-garden margin, lush ground); the mall park's pond (Garden square, Family park); rain gardens: wet trees and lush ground on strips along the lots | strip mall rain gardens; big box both; garden and family pond |
| Lot medians (`lotMedian`, group Parking) | wider planted strips between the bays | big box 3.5 m |
| Service yards (with designed planting) | a dumpster behind every other deep shop | with designed planting |
| Corner courts (ring styles) | a small car park turned to each soft corner of the ring drive, entered from the chamfer (the street corners were lawn) | strip mall back corners, mall park, district |
| Gate courts (mall park) | a car park off the alley loop on the park side, straight on from each car entrance (two at most) | mall park with an alley loop |

## The styles

**Strip mall, Classic: "the neighbourhood centre" (real-2).** A ring drive follows the site: close to the main street
behind a planted frontage (promenade, groves, flowerbeds at the drives, a hedge in front of the bays), a row deep in
from the back and, on wide sites, the sides. The shops stand outside the ring facing in, their backs to the
neighbours; the lot fills the ring. On deep sites rungs cross the ring with rows of shops back to back and lots
between (the "labyrinth" the user asked for). A tree-lined spine runs from the front drive through the lot to the
shops, ending in a pocket plaza; pads stand in the front corners. Cars come in where the player put the entrances
(drives from the kerb into the ring, driveways as real junctions); people on the footpath entrance's walk, which joins
the spine.

**Strip mall, Big box: "the power centre" (real-1, real-3).** The same frame with deep, six-cell-wide boxes, bigger
lots with planted medians, a pad on one front corner and a retention pond with a rain-garden margin on the other, food
trucks and a bike shelter at the plaza.

**Mall district, Shopping district: "the lanes".** A ring drive round the quarter (shops on both sides where it is
deep enough), a grid of lanes and pedestrian streets inside joined to the ring, courtyard gardens, the square at the
central crossing (café tables, a fountain, food trucks); walks in from the footpath entrances through planted
gateways, bike racks at the gateways. On a normal block (no room for lanes inside) the ring gets soft 7 m corners so each
side holds a row either side of a walk, rows face it from both sides, a corner court sits outside each soft corner, and
the middle is the quarter's square.

**Mall district, Downtown.** Denser, taller, alleys, and parking courts mixed through the grid (landscaped, EV
chargers).

**Mall park, Commons / Garden square / Family park.** The loop now follows the site (rounded corners; round round a
circle, an L round an L), small shops outside it facing the park, the park inside; entrances become access lanes
(cars) or walks that cross the loop into the park. Commons: an urban park with a fountain plaza and food trucks on the
loop. Garden square: the Garden walk park with a lily pond. Family park: the natural park with a playground and a pond.
Car entrances on an alley loop open onto gate courts (a car park off the loop on the park side), the corners outside
the loop are corner courts: a round mall park is shops round a park with parking at its gates, not a park alone.

**Market square, Big market: "the market quarter".** A market filling the whole site whatever its shape: tree-lined
market streets planned between the gates, food courts at the crossings, planting islands breaking up the paving, food
carts and bike racks at the gates, a car park at a car entrance.

## Iterations

Every run: the styles on the normal block (bucket), a block four times bigger, a long rectangle, an L and a circle (both
with a ring road that follows them), a 6 % slope, and one circle inside a block that touches no street (gap 16); the
entrances placed with the runner's steps the way a user would (strip mall: two-way S 0.3 and 0.72, footpath S 0.5,
driveway N 0.5, way out E 0.55; mall district: two-way S and N, footpaths E and W, driveway E 0.25; mall park: two-way
S, driveway E, footpaths W and N), grow 20-30 s, plan / 45 / zoning / close pictures; scripts in
`.forensics/shops3/` of the worktree (gen.js makes the set, analyze.js reads the logs). Runs are under
`C:\Users\jack\cs2-area-tool\.forensics\suite\runs\`.

- **Baseline** `show-48-builds-1016-143454-12e0` (master): the lands sat 8 m off the kerb (Show.ps1 gap 16): the
  circle, the L and most of the big rectangle found no street and became pocket parks; no entrance placed anywhere.
- **it1** `shops3-it1` (strip mall centre, zoning fix): every entrance step placed; strip mall block 38/38 cells
  zoned (was 14/46), big box block 48/48 (was 24/66), all builds 97-100 %, every grown shop faces our lanes, no pits;
  the circle inside a block builds a centre. Seen: the circle's east half was planted (a lot shape test too strict
  inside a ring: relaxed); the block's back row was two wide plots (the loop started mid-edge: now on a corner; rows
  are rounded to the piece size with anchors at the ends).
- **it2** `shops3-it2` (rings for the mall park and the district, gates routed from the entrances, plot trees): 49 builds,
  every one built, all 245 entrance steps placed, zoning 98-100 % in every build, every grown shop faces the build's own
  lanes but 3 (the strip mall's L: the street at the L's inner corner zoned three plots of the ring's row there).
  **Plot trees, verified in the game:** in every build the growing shops hid the trees in their way (the game's
  Overridden flag: strip mall big 117 of 787, district big 124 of 390) and none stood within 7 m of a grown building, so
  trees and the dirt / lawn paint stay on the plots. Seen: strip mall big, district L / circle / big and mall park garden
  big read as designed places; the normal block was the weak case: the district's ring was skewed (the bucket draws the
  block with its kerb returns cut, eight corners; dropping one end of each cut slanted a whole side, so its rows backed
  onto the street margin and became car parks), its corners and the mall park's were lawn, the big box had two boxes and
  lawn. Fixed for it3: cut corners restored before the ring is laid; rows and holds start at the drive's zone offset; a
  gate whose way straight in is blocked bends to the free lane point cutting the fewest rows (the big box's way out on
  the east failed); rings too small for deep rows retry with two-cell rows; corner courts (a small car park turned to
  each soft corner, entered from the chamfer); the big box pond from an 80 m front.
