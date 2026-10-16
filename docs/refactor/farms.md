# Farms: category review (rf-farms)

The user's verdict (GRADES.md): "Animal Park and Farmstead are much better than the urban gardens. Urban gardens are
kind of ugly. And they repeat and they use a lot of assets." The lead's reading: look at each one; fix or cut urban farm;
polish the good ones.

Shelf: `Recipes/shelf/farms.json`. Hidden things keep their code and still build by id (`Show.ps1 -Type urbanFarm`).

## BEFORE

Run `show-19-builds-1016-062555-f02b` (all 3 types x every style, flat 150 x 140 m block, plan + 45 + top + terrain).

## Decisions

| Type | Style | Grade (before) | Keep / hide | Why |
|---|---|---|---|---|
| urbanFarm | default, marketGarden, vineyard, greenhouses, lavenderField, rooftopBeds, cityFarm | F / D | **hide the type** | A grid of pavement tracks cutting the block into 20-60 identical beds; the "crops" are vanilla raised-bed corner pieces (grey boxes) in rows; glasshouse range = empty sand slabs with a shed; raised beds = 50 identical paved squares with benches. Repetitive, path-dominated, many objects for little effect. The lavender field with windbreak trees was the least bad, but it is a field type the farmstead does better. Nothing worth fixing on its own. |
| animalPark | pettingFarm | C | keep, fixed | Pens were 6 bare grey slabs (vanilla "Sand Surface 02" fallback) filling the whole site. |
| animalPark | paddocks | B- | keep | Clean spine walk between grazing paddocks, field shelters, hay. |
| animalPark | horsePaddock | B | keep | Riding arena with jumps: distinctive. |
| animalPark | deerPark | B+ | keep | Wooded deer park with a trail and a tall wooden fence: the most distinctive of the group. |
| animalPark | aviaryGarden | D | **hide** | The "aviaries" are vanilla gazebos (no aviary prop exists), the pens are invisible low fences on grass; reads as a park with gazebos, not as an aviary. |
| farmstead | dairy | B- | keep, fixed | Barns, paddocks, hay meadows: reads as a farm. |
| farmstead | arable | B- | keep, fixed | Big straw fields on a track grid. |
| farmstead | mixed (= default) | B- | keep, fixed | The all-round farm. |
| farmstead | hobby | D | **hide** | Small plots were carpeted with grey raised-bed pieces; once fixed it is just a small copy of mixed. |
| farmstead | ranch | C | keep, fixed | Read as one empty lawn with three fenced squares. |

## Shared defects found (and fixed in the farm code)

1. **Grey boxes as crops** (farmstead, urban farm): the farm crop pool (`farm.crop`) in vanilla holds only
   `GardenBed0xCorner*` raised-bed corners. `FarmCategoryRules` no longer makes garden beds automatic field crops, and the
   farmstead asks for real field crops only (no garden-crop fallback, no "missing" note): without crop plants a field is
   its crop surface plus wheel ruts.
2. **The 16 m irrigation rig parked in the yard as "machinery"** (farmstead): the yard now parks tractor trailers with
   their loads (`FarmArt.Trailers`); the arable farm stands the irrigation rig in its big grain fields instead.
3. **Gazebos as field shelters** (farmstead paddocks): the paddock shelter is the animal park's field shelter
   (installed animal shelter, else the wooden campground shelter).
4. **No silos at all** (vanilla has none): a row of tall water tanks stands in as feed bins (`FarmArt.SiloStandIn`).
5. **Track stubs ending in the grass** (farmstead): the spine/grid track lines are claimed before the pond and the
   paddocks, so they run unbroken from the yard to the outline.
6. **Grey petting pens** (animal park): no grey sand fallback; without a straw/mud surface pack the pens are grass worn
   to earth with terrain paint. Petting pens now line the walk one pen deep (20 m) and the middle of the site is a
   meadow with the duck pond, trees and the features.
7. **Ranch = empty lawn**: the ranch's pastures are fenced with a gate toward the yard.

## Shared code touched

None outside the farm folders (`Core/Layers/Types/Farmstead`, `Core/Layers/Types/AnimalPark`), the shelf file and the
animal park tests. `FarmCategoryRules` is farm code but classifies for every type: the change only stops garden beds
being *automatic field crops* (`farm.crop`); they stay planters and garden crops for the community garden.

## Ponds

The ponds (farmstead, animal park) are dug and get a water source after the build, but the runner's pictures without
`-Grow` show the bare sand bed: water only flows while the simulation runs (the same holds for the wetland). See AFTER.

## Done

Wrapped up on request (the shared game queue was backed up); no further game runs.

- **Kept:** animalPark (pettingFarm, paddocks, horsePaddock, deerPark); farmstead (mixed/default, dairy, arable, ranch).
- **Hidden** (shelf `Recipes/shelf/farms.json`, code kept, still builds by id): type `urbanFarm` (all 7 styles:
  repetitive path grid, grey bed pieces, many objects for little effect); style `animalPark.aviaryGarden` (gazebos as
  aviaries, no real aviary prop); style `farmstead.hobby` (a small copy of mixed).
- **Changed:** the seven shared defects above (no grey bed crops, trailers in the yard and irrigation rigs in the arable
  fields, wooden field shelters not gazebos, water-tank feed bins, unbroken tracks, petting pens one pen deep around a
  meadow and duck pond, fenced ranch pastures with gates). Last commit (639d084): hedgerows are continuous hedge nets
  that never cross a track; the animal park places no gazebo aviaries unless the aviary style asks for them.
- **Runs:** BEFORE `show-19-builds-1016-062555-f02b`. AFTER (build c802cda-dirty, i.e. before 639d084):
  `show-4-builds-1016-074917-09e2` (farmstead, flat block), `show-4-builds-1016-074920-6f38` (animal park, flat block),
  `show-4-builds-1016-074923-4c99` / `-074926-0cc6` (plots).
  Pictures (under `.forensics/suite/runs/`): `show-4-builds-1016-074917-09e2/shots/03-farmstead-mixed.06-45-clean.png`,
  `show-4-builds-1016-074920-6f38/shots/01-animalPark-pettingFarm.06-45-clean.png`; before:
  `show-19-builds-1016-062555-f02b/shots/01-urbanFarm-default.05-45-clean.png`.

## Not done / known problems

- Commit 639d084 (hedge nets, no gazebo aviaries) compiles but was **never seen in game**; the latest AFTER pictures
  predate it (the petting farm there still shows two gazebos at the east edge).
- No after-grade pass written per style; grades in the table are the BEFORE grades.
- No animals anywhere (vanilla has no farm animal props usable here); paddocks and pens read as empty fenced grass.
- Ponds show as bare sand beds in runner pictures without `-Grow` (water flows only while the sim runs); not checked with `-Grow`.
- Without crop plants, fields are crop surface plus ruts only; water-tank feed bins are a stand-in for silos.
- Ranch/dairy on small plots and slopes only checked on flat plots; no sloped-terrain run.
- Animal park tests touched; farmstead has no new unit tests.
- Branch not merged to master.
