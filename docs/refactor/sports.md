# Sports: the category agent's decisions (rf-sports, 2026-10-16)

The user's grade: **D**, "weird, but I like how they're unique". Brief: don't cut much; cut only what is bad **and** not
unique; make the rest work cleanly.

Sports holds three types: **Sports park** (`sportsPark`), **Golf** (`golf`) and **Urban ruins** (`urbanRuins`, the skate park
with a broken highway). Shelf: `Recipes/shelf/sports.json`.

All judgements below come from the game runner in the clean profile (the game's own assets only), plus the gallery
renderer for layout. Run ids are under `.forensics/suite/runs/`.

- BEFORE: `show-19-builds-1016-062550-d800` (every type and style, 45° + top + plan + terrain, flat 150 x 140 m block).
- AFTER: see the end of this file.

## Decisions

| Type | Style | Before | Keep / hide | Why |
|---|---|---|---|---|
| Sports park | community | D | keep | The everyday sports park. Broken before: pitch without lines, courts plain grey slabs, tennis "nets" were red benches. |
| Sports park | stadium | F | keep | Unique (running track with a pitch inside, stands, floodlights). Broken before: on a city block the track never fit, so it was a community park with a car park. |
| Sports park | tennisClub | D | keep | Unique (a club of tennis blocks). Broken before: bench "nets", garden picket fences, no lines. Now on clay. |
| Sports park | campus | F | **hide** | Not unique (community + baseball + volleyball + skate) and bad: on a 1.5 ha block five of its facilities never fit (baseball, skate bowl, volleyball, fitness, playground). |
| Sports park | neighbourhood | D+ | keep | Street sports without a big pitch: courts, skate, table tennis. Distinct from community. |
| Golf | parkland | C+ | keep | Reads as golf: fairways, greens, bunkers, ponds, trees. |
| Golf | links | C+ | keep | Treeless, gorse and deep bunkers, out-and-back routing: a different look from parkland. |
| Golf | pitchPutt | C | keep | The small-course format that fits a city block; short holes. |
| Golf | miniGolf | F | keep | Unique. Broken before: every lane was a white slab (the felt drew under the kerb). |
| Golf | drivingRange | D | keep | Unique (tee line, target greens). Broken before: the target greens vanished under the range's grass, only their bunkers showed. |
| Golf | discGolf | C | keep | Unique woodland course with tee pads and baskets. |
| Urban ruins | diySpot | D | keep | The skate spot. Before: two huge bright white slabs with a dozen identical boxes. |
| Urban ruins | highwayRuins | D- | keep | The most unique style. Before: white slabs, yellow no-parking hatching on the "carriageway", nothing that said highway. |
| Urban ruins | graffitiPark | F | **hide** | Not unique (the DIY spot plus a wall row) and bad: its point, graffiti, needs decal packs; without them it was one bare white slab. |
| Urban ruins | parkour | D | keep | Block field on gravel: different from the skate spot. |
| Urban ruins | dirtJumps | D | keep | BMX lines are unique. Before: the jump mounds were built but painted "dry grass", so the track was invisible. |

## What changed (all seen in the game runner; see the runs below)

_Filled in after the iterations._

## Shared code touched (kept minimal)

- `Core/Layers/AddOns/Fields/*` (the sports-field add-ons, also used by parks' amenities): the fixes apply everywhere a
  field is built, as the brief asks ("fix every bug everywhere it occurs").
  - Tennis: the game's tennis net, tennis benches and umpire chair by default (the automatic tennis pool starts with a
    bench, so every net was a bench); a chain-link cage instead of the automatic garden fence; a host can ask for clay
    or grass through a region tag (`courtSurface:clay`).
  - Whole-field marking decals of the game (`TennisCourtDecal02`, `FootballFieldDecal01`, `BasketballCourtDecal01`)
    named explicitly. The basketball decal used an automatic ref of a category without an automatic pool, so it was
    dropped on every build; tennis and pitches had no lines at all without a line-piece pack.
  - `FieldAddOn.Build`: a host-sized place smaller than the smallest format gets the smallest format laid out to the
    place (only the sports park's compact running track asks for that).
