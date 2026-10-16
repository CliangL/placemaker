# Nature: grades, cuts and fixes (rf-nature)

The user's grade: **Nature A**. Wetlands A+ (the best in the pack: do not break it), Lookout hill A/A+ but "some of
the roads get a little weird". Campground, nature reserve, greenway and hiking trail: review, keep what is great.

Everything below was judged in the game runner (`Show.ps1` through the game queue), logs first, pictures second.
Run folders are under `.forensics/suite/runs/`.

## Runs

| What | Run |
|---|---|
| BEFORE, every type and style, flat 150 x 140 block, 45 + top + plan + terrain | `show-30-builds-1016-062609-9c9f` |
| BEFORE, lookout hill on an L, 6 % slope | `show-6-builds-1016-063711-72fa` |
| Iteration 1: campground, lookout hill (block) | `show-10-builds-1016-071135-d9d3` |
| Iteration 1: hiking trail on 360 x 280 m hills | `show-7-builds-1016-071200-0e69` |
| Iteration 1: wetland, 200 x 170, grown 40 s (water filled) | `show-6-builds-1016-071211-64b0` |
| Iteration 1: greenway on a 320 x 200 L | `show-3-builds-1016-071231-503f` |
| Iteration 2 and AFTER | see the end of this file |

Note on the BEFORE run: campground built nothing (`type-not-selected`, see below) and wetland bird reserve got no
block (the grid ran out at 30 builds); both were built in iteration 1.

## Decisions

| Type | Style | Grade | Keep / hide | Why |
|---|---|---|---|---|
| **Wetland** | wetlandPark | A+ | keep | Ponds, boardwalks, reeds, woodland: the best of the pack. Untouched. |
| | waterGarden | A | keep | Several ponds and zig-zag boardwalks; distinct. |
| | reedMarsh | A | keep | Big open water, long boardwalk across it, deer by the shore. |
| | rainGarden | A- | keep | Long narrow basins with straight boardwalks; distinct. |
| | birdReserve | B+ | keep | Woodland, one pond with islands, fenced; the weakest wetland but distinct. |
| **Lookout hill** | hilltop | A | keep | After the path fixes: one smooth spiral from the street to a gazebo plaza. |
| | sledding | B+ | keep | The open run down one side and the bales make it its own thing. |
| | viewpointMound | B- | hide | A small, low hilltop with a plaza: hilltop does the same better. |
| | terraces | A- | keep | Was the worst "weird road": an 8 m wide walk that cut retaining walls into the steps. Now a narrow serpentine. |
| | kiteHill | B+ | keep | Open dome, blankets, cairn; after the fix the path walks straight up instead of looping round the top. |
| **Campground** | forest | A- | keep | Gravel pitches with tents, tables and fires among pines; reads as a campsite. |
| | meadow | C+ | hide | A lawn with tents dotted about: mundane. |
| | caravan | C | hide | 8 m wide walks that read as roads, and no caravans (vanilla has none: tents again). |
| **Nature reserve** | forest | A- | keep | Dense mixed forest, glades, trails. |
| | heath | B+ | keep | Shrubland with a few copses; clearly different from the forest. |
| | wildwood | B- | hide | The forest again, denser, with the trails buried under it. |
| **Greenway** | leafy | B | keep | A bike spine under an avenue with a footpath beside it. Best on long strips. |
| | open | C | hide | The same spine on a bare lawn: mundane. |
| **Hiking trail** | forestTrail | B+ | keep | A loop through forest with rest spots; the classic. |
| | meadowWalk | B | keep | Open meadow, worn trail, picnic spots; distinct from the forest. |
| | ridgeHike | B | keep | Lifts a ridge under the trail; was a staircase of flat squares with a 15 m pit on hills (fixed). |
| | canyonTrail | B | keep | Carves a gorge for the trail; same terrain fix. |
| | summitClimb | C+ | hide | Lookout hill does summits better; on hills it left an 80 m pit beside the trail (fixed anyway). |
| | lakeshoreLoop | C | hide | Needs an existing lake; on dry land it is a forest loop. |

Industry and Transport stay hidden (not this category). Roads untouched.

## What was wrong, and what changed

### Lookout hill: "the roads get weird" (`Core/Layers/Types/LookoutHill/`)

Seen in BEFORE (block and L-slope runs):
1. **The path hooked round the summit plaza.** The walker could never step inside the reserved plaza disc, so near the
   top it turned along it: an S-bend on the hilltop, a loop round the cairn on the kite hill. Now the walker stops
   at a short arrival radius and walks straight onto the plaza (`HillPaths`).
2. **Terraces had an 8 m wide walk** (the vanilla wide pedestrian path) that cut retaining walls into the terraces and
   read as a road up the hill. The preset now uses the narrow path (the wide one is still an option).
3. **The path was a chain of straight 4-20 m chords** with kinks at every corner. Now smooth curves (Catmull-Rom
   pieces through the walker's corners, straight only where a curve would leave the outline), and the walker's
   corners are rounded first (a hill on a square block gave a square spiral with sharp elbows).
4. **The path did not attach to the street.** Its first piece now carries the entrance's attachment, like every
   park path (and the second entrance's spur too).
5. **The summit plaza was barely wider than the gazebo** (9 m plateau, 7.6 x 6.5 m gazebo, benches jammed against it;
   the game flagged them as overlapping). The plateau now grows to fit its centrepiece plus a walk round it.
6. **The hill was a staircase of levelled 5 m squares** (stepped contours in every terrain map, and the steps were
   where paths cut walls). It is now emitted as sloped patches sharing their corners (the s20 sculpt surface, already
   used by the water types), held to the edge band at the outline.

### Campground was never offered (`Recipes/campground.json`)

The shipped recipe had a child step (a picnic layer), and the Make row only lists single types
(`Features.ShowInMakeRow`), so the panel never showed it; the game runner reported `type-not-selected`. The child is
gone (picnic is an optional campground feature anyway; pitches have their own tables).

### Hiking trail (`Core/Layers/Types/HikingTrail/HikingTrailLayer.cs`)

1. **Gorge and ridge were flat 12 m squares**, each levelled to the ground at its own centre: on any ground that was
   not flat they became a staircase, and in the game the steps left pits beside the trail (15 m on the ridge hike,
   80 m on the summit climb, `show-7-builds-1016-071200-0e69`). Now shaped on the sloped node grid (Common/Sculpt):
   the change follows the ground, dies out towards the outline, is never steeper than 30 %, and the trail, its
   furniture and planting are put back onto the shaped ground (`Sculpt.Resnap`).
2. **The trailhead "car park" was a bare sand rectangle** by the entrance on every build: no driveway reaches it.
   Off by default now (the slider still adds one).

### Not changed

Wetland (A+): only judged; nothing changed. Nature reserve and greenway: only the shelf.

## Shared code touched

None outside this category's layers, recipes, tests and shelf file. The lookout-hill and hiking-trail terraform now
use the existing `Sculpt`/`PlannedTerraform.Sloped` mechanism; nothing in it changed.

## Tests

`LookoutHillTests` (new: the path starts on the street and walks straight onto the plaza, on the 6 ha L and on the
runner's 150 x 140 block; the hill is sloped patches; terraces use the narrow path), `HikingTrailTests` (new: gorge
and ridge are smooth on egg-crate hills and the trail stands on the shaped ground; the car park is off by default),
`AddOnTests.CampgroundRecipeEndToEnd` (the recipe is a single type the Make row offers). Pre-existing failure, not
ours: `FeatureTests.EveryControlHasAGroupShortLabelAndEssentialsComeFirst` (strip mall's "Shops facing the streets"
label, from the core merge).
