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
