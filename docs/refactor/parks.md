# Parks: the cut and the survivors (rf-parks, 2026-10-16)

The user's brief (docs/refactor/GRADES.md): Parks B- to A-. Cut 13 types to about 3 to 5 types with about 4 excellent
styles each. Favourites: Park Urban Pocket, Garden Walk, Natural. Picnic grounds folds into Park. Dog park: hide.
Botanical garden: only Flower show and Shrubbery. Zen garden: okay. Memorial park A- but mundane. Adventure
playground A-. Then make every survivor awesome.

Everything below was judged in the game runner (`tools/suite/Show.ps1`, clean profile, flat 150 x 140 m block unless
said otherwise). Grades are mine, from the BEFORE pictures and logs.

- BEFORE: `show-62-builds-1016-062604-a794` (every type and style; 45-degree, top, plan, terrain). The grid ran out of
  blocks for the five zen styles; they were built separately in `ZEN_BEFORE_RUN`.
- AFTER: `AFTER_RUN`.

## What is kept

| Type | Kept styles | Hidden styles |
|---|---|---|
| Park | Natural, Garden walk, Urban pocket, Picnic lawn (new: picnic grounds folded in) | formal, woodland, meadow, pocket, victorian, landscape, modern, wildflower, riverside, greenway |
| Botanical garden | Flower show, Shrubbery | mixed, arboretum |
| Memorial park | Memorial garden, War memorial, Reflection garden, Sculpture park | square (monument square) |
| Adventure playground | Adventure, Toddler, Nature, Jungle | water |
| Zen garden | ZEN_KEPT | ZEN_HIDDEN |

Hidden types: dog park, picnic grounds (now the Park's Picnic lawn style), cemetery grounds, orchard park, formal
garden, community garden. All of it is in `Recipes/shelf/parks.json`; nothing was deleted, and every hidden type and
style can still be built by id.

## Every type and style, graded (BEFORE)

### Park (13 styles)

| Style | Grade | Keep? | Why |
|---|---|---|---|
| natural | B | keep | User favourite. Loose groves, curvy walks, a playground clearing: the most park-like of all. |
| garden (Garden walk) | B- | keep | User favourite. Meandering garden walks, grass surface, flowering shrubs. Pale rectangles round the edge were empty street-shop plots (the park zoned 7 commercial plots into its own edge by default). |
| urban (Urban pocket) | B | keep | User favourite. Spoke-and-ring walks to a central plaza, kiosk, dense street trees. The picnic, playground and flowerbed features found no room and said so on every build. |
| formal | C | hide | Axial network that reads like the formal garden; fountain centre missing in a clean game. |
| woodland | C+ | hide | A forest with a path; the Nature category (nature reserve) does this better. |
| meadow | C | hide | Mostly empty grass. |
| pocket | C | hide | Same idea as Urban pocket, weaker. |
| victorian | C+ | hide | Figure-eight walk falls back to the generic planner (PathStyleFallback). |
| landscape | D | hide | The lake basin is a dry sand pit until the water fills; reads as a hole. |
| modern | C | hide | Promenade falls back to the generic planner; tree grid reads as an orchard. |
| wildflower | C | hide | Few trees, painted patches; close to meadow. |
| riverside | C+ | hide | Needs a river; on dry land it is natural with more paths. |
| greenway | C+ | hide | Bike route needs bike entrances (NoBikeEntrances); otherwise natural. |

### The cut types

| Type | Styles | Grade | Why hidden |
|---|---|---|---|
| dog park | neighbourhood, split, agility | C | User: hide. |
| picnic grounds | family, bbq, shady | C | A big empty lawn with blankets you cannot see from above; folded into Park as the Picnic lawn style. |
| cemetery grounds | churchyard, lawn | C | Sparse hedged plots on a bare lawn; not a park. |
| orchard park | apple, cherryBlossom, oliveGrove, citrus, community | C- | Dense tree blobs, not orchard rows; it is farm-like (Farms has animal park / farmstead). |
| formal garden | french, italian, knot | C | Hundreds of identical hedged boxes; repetitive, no fountains in a clean game. |
| community garden | allotments, community, marketGarden | C | Allotments: a farm look, overlaps urban farm. |

### Botanical garden

| Style | Grade | Keep? | Why |
|---|---|---|---|
| mixed | C | hide | User: cut. |
| flowerShow | C | keep | User: keep. Flowerbed tiles scattered like confetti: no beds, no show. |
| arboretum | C+ | hide | User: cut. |
| shrubbery | B- | keep | User: keep. Shrub clumps and accent trees along winding walks, hedge boundary. |

### Memorial park

| Style | Grade | Keep? | Why |
|---|---|---|---|
| garden | B- | keep | Axis, hedged rooms, flower ring; but 30 lawn trees per ha bury the axis. |
| square | C- | hide | Big empty lawn quadrants round a paved L (court + processional strip). |
| war | B | keep | Raised forecourt, flags, plaques, poppies, pool. |
| reflection | B | keep | The strongest composition: long pool, split walks, hedged rooms. |
| sculpture | C+ | keep (fixed) | A wooded trail park; no sculptures were placed at all (below). |

Why it is mundane (log: `NoAssets(memorial.monument)`, `NoAssets(memorial.sculpture)`): in a clean game the monument
and sculpture categories are empty, so **the memorial had no monument** and the sculpture park **no sculptures**. The
curated vanilla pieces (standing stones, stone heads) were only used when the category had items. Every style was then
the same thing: an axis lost under lawn trees, with loose flowerbeds along the outline (the flowerbeds feature) and a
grey slab for a pool (no water-look surface in a clean game).

### Adventure playground

| Style | Grade | Keep? | Why |
|---|---|---|---|
| adventure | A- | keep | Age zones, nature play, picnic hub. Zones a little sparse. |
| toddler | B | keep | Gentle; its splash pad was an empty pale disc (below). |
| water | C | hide | The splash pad and pools were empty discs in a clean game (`NoAssets(centerpiece.fountain)`); its water play lives on in adventure and toddler. |
| nature | A- | keep | Logs, boulders, a mound and a tunnel in a wood. |
| jungle | A- | keep | The densest, most exciting equipment zones. |

### Zen garden

ZEN_TABLE

## What changed (to make the survivors better)

### Park
- **Picnic lawn** (new style `picnic`, ParkStyle `Picnic`): picnic grounds folded into the park. A gentle circuit walk
  round a big open lawn (clearing factor 1), broad shade-tree clumps round the edge, eight picnic groups (tables,
  shelters, grills, bins), a playground and restrooms; no loose flowerbeds or street shops.
- **Street shops off by default** for the park (`StreetShops.Defaults["park"]`): they zoned seven commercial plots into
  every natural or garden park's edge (the pale rectangles). The Urban pocket turns them on with its kiosk and plaza.
- Urban pocket: no picnic and loose flowerbeds (there was never room for them; the panel said so on every build).

### Botanical garden
- **Flower show**: carpet beds. Round and long beds of massed flowerbed tiles on a 2.4 m grid, one kind per bed,
  edged with low flowering shrubs, near the walks (`showBeds`, 6 by default), with fewer loose tiles around them.
- **Shrubbery**: shrub borders. 60 % of the shrubs massed along the walks (`pathBorders`), the rest in clumps.
- The type's default (picked without a style) is now the flower show, since mixed is hidden.

### Memorial park (breaking the mundaneness)
- **A monument, always**: curated vanilla pieces are placed by name even when the category is empty; the focal piece
  is scaled up (`monumentScale`, 200-300 % per style) so it reads as a monument. Sculpture park sculptures likewise.
- **Open lawns**: lawn trees cut from 20-35 to 6-10 per ha on the axial styles, and the big-trees and loose-flowerbeds
  features are off by default, so the axis, the rooms and the monument read.
- **A signature per style**: Memorial garden gets flower borders along the procession and a cross axis; War memorial
  gets poppy borders, 10 flags, 12 plaques and a 300 % monument on its raised forecourt; Reflection garden gets a
  fountain rill down its pool (`poolFountains`), which also turns the grey slab into water in a clean game; Sculpture
  park finally has its sculptures.

### Adventure playground
- The splash pad's fountains are placed by name when the fountain category is empty (it was an empty disc).
- A little more equipment: adventure 24 (was 18), toddler 14 (was 10).

### Zen garden

ZEN_CHANGES

## Shared code touched (kept minimal)

- `Core/Contracts.cs` `PresetInfo.FeatureValues`, `LayerBase.PresetFeatureValues`, `DesignEditor.FeatureStep` /
  `ApplyFeatureValues`, `PresetStore.Resolve`: a preset can now give its feature children settings (the Picnic lawn
  has eight picnic groups where other park styles have two). Switching to a style that sets none puts the feature's own
  default back. No other type uses it yet.
- `Core/Layers/Common/StreetShops.cs`: only the `park` row of the defaults table (street shops off).
- `Core/Layers/Leisure/LeisureLayer.cs`: only the `memorialPark` row of the default-on features table.

## Not done / open

OPEN_ITEMS
