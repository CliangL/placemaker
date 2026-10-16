# Parks: the cut and the survivors (rf-parks, 2026-10-16)

The user's brief (docs/refactor/GRADES.md): Parks B- to A-. Cut 13 types to about 3 to 5 types with about 4 excellent
styles each. Favourites: Park Urban Pocket, Garden Walk, Natural. Picnic grounds folds into Park. Dog park: hide.
Botanical garden: only Flower show and Shrubbery. Zen garden: okay. Memorial park A- but mundane. Adventure
playground A-. Then make every survivor awesome.

Everything below was judged in the game runner (`tools/suite/Show.ps1`, clean profile, flat 150 x 140 m block unless
said otherwise). Grades are mine, from the BEFORE pictures and logs.

- BEFORE: `show-62-builds-1016-062604-a794` (every type and style; 45-degree, top, plan, terrain). The grid ran out of
  blocks for the five zen styles; they were built separately in `show-6-builds-1016-070019-2a26` (same request otherwise;
  that build already had the picnic preset added, which does not touch the zen garden).
- AFTER: `show-22-builds-1016-081901-f54e` (parks-it3, commit 2a041e4; every kept type and style, flat blocks), with
  `show-4-builds-1016-081925-0eb7` (park styles on a circle) and `show-5-builds-1016-081925-bd4b` (memorial styles on a
  pinched plot). The four commits after 2a041e4 have NOT been seen in the game (see "Not done").

## What is kept

| Type | Kept styles | Hidden styles |
|---|---|---|
| Park | Natural, Garden walk, Urban pocket, Picnic lawn (new: picnic grounds folded in) | formal, woodland, meadow, pocket, victorian, landscape, modern, wildflower, riverside, greenway |
| Botanical garden | Flower show, Shrubbery | mixed, arboretum |
| Memorial park | Memorial garden, War memorial, Reflection garden, Sculpture park | square (monument square) |
| Adventure playground | Adventure, Toddler, Nature, Jungle | water |
| Zen garden | Dry garden (karesansui), Stroll garden, Tea garden | moss, bamboo |

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

| Style | Grade | Keep? | Why |
|---|---|---|---|
| karesansui | C+ | keep | The signature dry garden; but its painted gravel read as a patchy dead lawn and the rock groups were specks. |
| stroll | C | keep (fixed) | The pond garden; on a city block its 22 % pond found no room (NoPond), leaving a bare oval. |
| tea | C+ | keep | Tea house, basin, stepping stones; planted so densely (70 pruned trees per ha) it read as a forest. |
| moss | C | hide | Same dense forest as tea with a small pond; nothing of its own in a clean game. |
| bamboo | D+ | hide | Vanilla has no bamboo: the grove is scattered shrubs. |

The zen garden depends on Japanese assets (lanterns, pruned pines, bamboo, basins) that come from asset packs; in a clean
game those categories are empty (NoAssets zen.basin and friends), so the clean pictures undersell it. The user's
"okay" is from their own playset.

## What changed (to make the survivors better)

### Park
- **Picnic lawn** (new style `picnic`, ParkStyle `Picnic`): picnic grounds folded into the park. A gentle circuit walk
  round a big open lawn (clearing factor 1), broad shade-tree clumps round the edge, eight picnic groups (tables,
  shelters, grills, bins), a playground and restrooms; no loose flowerbeds or street shops. The first try planted the
  lawn full before the picnic feature ran (one group showed); the park now keeps seven more open spots for it
  (`picnicSpots`).
- **Garden walk** gets its own planting (ParkStyle `garden`): flowering shrub borders line the walks (55 % of the shrubs
  beside the paths) with a few specimen trees, on top of the garden meander and the grass surface.
- **Street shops off by default** for the park (`StreetShops.Defaults["park"]`): they zoned seven commercial plots into
  every natural or garden park's edge (the pale rectangles). The Urban pocket turns them on with its kiosk and plaza.
- Urban pocket: no picnic and loose flowerbeds (there was never room for them; the panel said so on every build).
  Its plaza stays an open paved square (no plaza edge trees; on a round site the canopy closed over it).

### Botanical garden
- **Flower show**: carpet beds. Round and long beds of massed flowerbed tiles on a 2.4 m grid, one kind per bed,
  edged with low flowering shrubs, near the walks (`showBeds`, 10 on the flower show preset), with fewer loose tiles around them.
- **Shrubbery**: shrub borders. 60 % of the shrubs massed along the walks (`pathBorders`), the rest in clumps.
- The type's default (picked without a style) is now the flower show, since mixed is hidden.

### Memorial park (breaking the mundaneness)
- **A monument, always**: curated vanilla pieces are placed by name even when the category is empty; the focal piece
  is scaled up (`monumentScale`, 200-300 % per style) so it reads as a monument. Sculpture park sculptures likewise.
- **Open lawns**: lawn trees cut from 20-35 to 6-10 per ha on the axial styles, and the big-trees and loose-flowerbeds
  features are off by default, so the axis, the rooms and the monument read.
- **A signature per style**: Memorial garden gets flower borders along the procession; War memorial
  gets poppy borders, 10 flags, 12 plaques and a 350 % monument on its raised forecourt; every pool gets a fountain rill
  down its middle, which also turns the grey slab into water in a clean game; Sculpture park finally has its sculptures.
- **Memorial groves** (`bosque`, on for garden, war and reflection): the side lawns beyond the hedged rooms are planted
  as regular groves, mirrored across the axis, in place of loose lawn trees (which clumped into a diagonal band and left
  half the site an empty lawn), styled per memorial: the war memorial a solemn double row, the reflection garden a
  quincunx grove, the memorial garden sparse specimen trees. The Memorial garden lost its cross axis (with an end
  monument it ran along the outline).

### Adventure playground
- The splash pad's fountains are placed by name when the fountain category is empty (it was an empty disc).
- A little more equipment: adventure 24 (was 18), toddler 14 (was 10).
- Tighter play zones (item radii 2.1 m, toddler 1.4 m, jungle 2.8 m; were 2.6, 1.6, 3.2) so the sand discs are not
  mostly empty sand.

### Zen garden

- Dry garden: the field is a clean gravel surface (`gravelSurface` on; the painted raking is still there with it off)
  in an organic outline, and the rock groups use vanilla boulders (Boulder01-06) when no rock assets are chosen (the
  automatic rock pool was small stones that read as specks; this replaced an earlier 170 % `rockScale`).
- Stroll garden: a pond that fits a city block (`pondSize` 14 %, was 22 %: NoPond) and no gravel field competing for room.
  Its pond still got no water in it3 ("lake basin not deep enough": the water source stood on the shallow shelf);
  `WaterBodies.EmitBasins` now puts the source in the deep part (also helps the wetland bird reserve).
- Tea garden: half the pruned trees (35 per ha, was 70), so the tea house, basin and stepping stones show.
- Moss and bamboo are hidden.

## Shared code touched (kept minimal)

- `Core/Contracts.cs` `PresetInfo.FeatureValues`, `LayerBase.PresetFeatureValues`, `DesignEditor.FeatureStep` /
  `ApplyFeatureValues`, `PresetStore.Resolve`: a preset can now give its feature children settings (the Picnic lawn
  has eight picnic groups where other park styles have two). Switching to a style that sets none puts the feature's own
  default back. No other type uses it yet.
- `Core/Layers/Common/StreetShops.cs`: only the `park` row of the defaults table (street shops off).
- `Core/Layers/Leisure/LeisureLayer.cs`: only the `memorialPark` row of the default-on features table.
- `Core/Layers/Types/Wetland/WaterBodies.cs` `EmitBasins`: with a shelf, the water source goes in the deep part.
  Affects every pond with a shelf (zen stroll garden, wetland bird reserve), not only parks.

## Done

- **The cut** (`Recipes/shelf/parks.json`, hide, never delete): Parks went from 11 visible types to 5.
  - Kept: **Park** (Natural, Garden walk, Urban pocket, Picnic lawn), **Botanical garden** (Flower show, Shrubbery),
    **Memorial park** (Memorial garden, War memorial, Reflection garden, Sculpture park), **Adventure playground**
    (Adventure, Toddler, Nature, Jungle), **Zen garden** (Dry garden, Stroll garden, Tea garden). 19 styles.
  - Hidden types: dog park (user), picnic grounds (folded into Park as Picnic lawn), cemetery grounds, orchard park,
    formal garden, community garden (all C or worse; farm-like or repetitive). Hidden styles: 10 park styles (weaker
    copies of the kept ones, or need a river, bike entrances or water to work), botanical mixed and arboretum (user),
    memorial square (C-), adventure water (empty discs in a clean game), zen moss and bamboo (no assets of their own).
- **Improvements** (commits 0ac810a to 08507f4, each listed under "What changed"): Picnic lawn style and per-preset
  feature values; street shops off for parks (on for the Urban pocket); Garden walk shrub borders; flower show carpet
  beds; shrubbery borders; a monument and sculptures always placed, open lawns, per-style memorial groves and pool rills;
  splash pad fountains by name and tighter play zones; zen gravel field, boulders, a pond that fits and gets water.
- **Pictures** (in `C:\Users\jack\cs2-area-tool\.forensics\suite\runs\`):
  - before `show-62-builds-1016-062604-a794` / after `show-22-builds-1016-081901-f54e` (open `contact.html` in each);
  - `show-22-builds-1016-081901-f54e\shots\05-park-picnic.06-top-clean.png` (Picnic lawn),
    `...\shots\07-botanicalGarden-flowerShow.06-top-clean.png` (carpet beds),
    `...\shots\16-memorialPark-war.05-45-clean.png` (war memorial with its monument).
- The branch head builds (`dotnet build Placemaker.csproj -c Release -p:Deploy=false -p:SkipUI=true`, 0 errors).

## Not done / known problems

- **The last four commits were never seen in the game**: c593fc3 (adventure play zones), 0fdd7be (zen boulders,
  organic gravel field, pond water fix in `WaterBodies.EmitBasins`), c1354c6 (per-style memorial groves, larger picnic
  clearings, 10 carpet beds) and 08507f4 (open Urban pocket plaza). Their game runs (parks-it4-adv, parks-it4-zen,
  parks-it5-pb, parks-it5-mem) were dropped or still waiting in the queue when the work was stopped. One flat-block run
  of the 19 kept styles is the next check.
- **Stroll garden pond water**: in the latest seen run (it3) the pond was still dry ("lake basin not deep enough", 3
  times). The fix is in 0fdd7be but unverified; it also changes the wetland bird reserve, which nobody re-ran.
- **Zen garden in a clean game** stays weaker than in the user's playset: lanterns, pruned pines, bamboo and basins come
  from asset packs (NoAssets zen.basin and friends). Not judged with the user's own assets.
- **Grades after**: no fresh grade per style was written for the AFTER run; the user has not looked at it yet. The
  brief's target of about 3 types was not met: 5 types are kept, because the user named all five as keepers.
- **Not tried on hills**: the it1 slope run covered the memorial styles only; parks, botanical, adventure and zen were
  checked on flat blocks, a circle and an L plot, not on slopes.
- **Feature values per preset** (`PresetInfo.FeatureValues`) is new shared code used only by the Park; no record that the UI mock tests
  were re-run for it.
- Hidden styles were left as they were (Victorian and Modern still fall back to the generic path planner, Landscape's
  lake is a dry pit until it fills); not worth fixing while hidden.
