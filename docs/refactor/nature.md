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
| AFTER (iteration 2), lookout hill + hiking trail, block | `show-10-builds-1016-083407-ceaf` |
| AFTER, lookout hill on 360 x 280 m hills | `show-4-builds-1016-083428-e4be` |
| AFTER, hiking trail on 360 x 280 m hills | `show-4-builds-1016-083442-7d55` |

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

## Done

- **Lookout hill**: the path no longer hooks round the summit, starts on the street and is smooth curves; terraces use
  the narrow path; summit plaza sized to its centrepiece; the hill is one continuous slope (no levelled staircase);
  switchback hairpins are round U-turns (`a230877`).
- **Campground**: offered in the Make row again (the recipe is a single type).
- **Hiking trail**: gorge and ridge shaped on the sloped grid (no staircase, no pits beside the trail); trailhead car
  park off by default; sculpt capped at ~2000 cells so a big ridge finishes inside the terraform time (`8e1fe52`).
- **Kept**: all five wetland styles; lookout hilltop, sledding, terraces, kiteHill; campground forest; nature reserve
  forest, heath; greenway leafy; hiking forestTrail, meadowWalk, ridgeHike, canyonTrail.
- **Hidden** (shelf `Recipes/shelf/nature.json`; mundane or a duplicate, see Decisions): lookout viewpointMound;
  campground meadow, caravan; nature reserve wildwood; greenway open; hiking summitClimb, lakeshoreLoop. No type hidden.
- BEFORE `show-30-builds-1016-062609-9c9f`; AFTER `show-10-builds-1016-083407-ceaf` (plus the two hills runs above).
  Pictures (under `.forensics/suite/runs/`): terraces before
  `show-30-builds-1016-062609-9c9f/shots/16-lookoutHill-terraces.05-45-clean.png`, after
  `show-10-builds-1016-083407-ceaf/shots/04-lookoutHill-terraces.05-45-clean.png`; hilltop after
  `show-10-builds-1016-083407-ceaf/shots/02-lookoutHill-hilltop.06-top-clean.png`; ridge on hills after
  `show-4-builds-1016-083442-7d55/shots/03-hikingTrail-ridgeHike.07-terrain.png`.

## Not done / known problems

- The last two commits (`a230877` hairpin U-turns, `8e1fe52` sculpt cap) were never seen in the game: the AFTER runs
  were built from `975778e` plus work in progress. Unit tests only.
- No AFTER game run of campground, wetland, nature reserve or greenway with the final code (campground was last built
  in iteration 1, `show-10-builds-1016-071135-d9d3`).
- AFTER logs still warn: a 6-7 m bowl on a block build (`show-10-builds-1016-083407-ceaf`, at -60, -468), ~2.7 m
  bowls and a 1.1 m path sag on the canyon trail on hills (`show-4-builds-1016-083442-7d55`, plot P8; may be the
  intended gorge, not checked), and rocks/bushes placed overlapping each other (OverlapExisting, kept under anarchy).
- Greenway open and campground caravan are only hidden, not fixed (caravan walks are 8 m wide; vanilla has no
  caravans).
- `suite/catalog.json` changes are committed on this branch (runs regenerate it); take master's copy when merging.
- Pre-existing failing test `FeatureTests.EveryControlHasAGroupShortLabelAndEssentialsComeFirst` (not this category).
