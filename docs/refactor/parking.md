# Parking: category review (rf-parking, 2026-10-16)

The user's grade: **D-**. "It does its job ... the trees go away ... the fences should be working perfectly ... it's kind of
boring." The brief: no new features; make it work and look tidy. Trees, bushes and lamps stay visible; fences perfect
(never across an entrance, no farm fences); no dips. Keep the styles that are really different, hide near-duplicates.

Runs (all under `C:\Users\jack\cs2-area-tool\.forensics\suite\runs\`):

| What | Run |
|---|---|
| BEFORE: every style, flat 150 x 140 block, plan + 45° + top + terrain (master `a71a842`) | `show-5-builds-1016-062605-103a` |
| it1: tidy lines, every style | `show-5-builds-1016-064107-d6ce` |
| it1: L 170 x 130 on a 7 % slope | `show-3-builds-1016-064133-a020` |
| it1: small 70 x 45 | `show-3-builds-1016-064134-ac9e` |
| it1: strip mall + mall district lots | `show-2-builds-1016-065534-5cbc` |
| it2: caps, fill off the lines, entrance tie | `show-4-builds-1016-075429-c23c` |
| it2: L on a 7 % slope (shows the entrance funnel, reverted) | `show-3-builds-1016-075449-973a` |
| it2: pinch 180 x 110 on hills | `show-2-builds-1016-075449-3979` |
| it2: strip mall + mall district lots | `show-2-builds-1016-075449-e540` |
| latest AFTER (commit `6c1e211`): every shown style, flat block | `show-4-builds-1016-075429-c23c` |

## What was wrong

The core refactor had already fixed the invisible trees (the vanilla farm "field" lot), the farm fences and the
lot-entrance dip. The BEFORE logs confirm it for parking: no vanilla lot, no fence object anywhere, "survivors: no object
hidden" (2-3 bushes hidden by the game and shown again), "pit check: no bowl deeper than 1.0 m", terrain -0.25 to 0 m.

What was still wrong was the **planting**: trees and bushes in clumps over the aisles and the stalls, gaps elsewhere.

* Cause 1: the spacing came from the catalog's plant bounds, which are the game's shrunk collision core (2.5 m for the
  London plane), so trees were spaced 4 m apart while their crowns are 7-10 m across.
* Cause 2: the fill was a jittered grid with value-noise clumping, anywhere in a 2.5 m median (trunks 0.15 m from the
  edge), then thinned at random by the density setting.

## What changed (only `Core/Layers/Parking/`)

* **Planting lines** (`Vegetation.PlanRows`, edges gathered in `PlanBuilder`). The strips behind stall rows (medians, the
  edge strip behind a perimeter row) are found by probing straight back from every stall's back corners while the ground
  is green; the middle of the strip is a planting point. Points of one strip become one straight line, split where a cross
  lane cuts it, and planted symmetrically: a tree on every k-th point (k from the crown and the density: one tree every
  3 stalls at the default 50 %), one species and one growth stage (adult) per line, shrubs between the trees and off the
  trunks. The row-end caps get a line of shrubs down their middle (no trees), and a free strip no stall faces (a strip
  between two lanes, in `compact`) gets its own line of trees and shrubs. Long lines are planted first and lines that meet
  never double up.
* **The old jittered fill** only adds to what is left (the corners of the lot, wide islands, the wedges of angled rows),
  keeps 4 m off any line plant, and its trees need 1.4 m of green round the trunk.
* **Density** sets the spacing of the lines (20 % wide gaps, 50 % a fifth of a crown apart, 100 % a closed canopy) instead
  of deleting random plants.
* **Spacing from the crown**: half the height x 0.56, as `DressingWorld.CrownOf`, at least 7 m between trees; shrubs 2-4 m
  from their own size and height.
* **No plant inside a stall** (a stall corner can reach into the planting on odd outlines; a shrub stood in one on an L).
* Speed: a 25 ha lot plans in about 3 s (the quality test's budget is 5 s); stalls and plants are looked up by grid cell.

Shared code: none. The commercial types plan their parking lots with this layer (`Mall.cs` ->
`GeneratorRegistry.Get("parking")`), so the strip mall's and mall district's lots get the same tidy lines (seen in the
runner, no new pit, no hidden objects). That changes the strip mall fingerprints
(`Tests/Placemaker.Tests/Fixtures/stripmall-fingerprints.txt`), regenerated on purpose with `PM_UPDATE_STRIPMALL=1`;
the integrator may have to regenerate them again after merging the Shops & squares branch.

Tried and **reverted**: tying the lot edge at a car entrance to its street wherever the street is (`LotGrading`). On the
runner's sloped land the street is ~10 m off the outline and the graded lot stood 2.6 m above it at the entrance, so the
connector climbed a bank in its first 6 m ("net sag ... humps 1.1 m", `show-3-builds-1016-064133-a020`). The tie fixed the
connector but pulled a 30 m half-bowl into the lot round the entrance (terrain map of `show-3-builds-1016-075449-973a`),
which reads as a dip. Back to master's grading: lots drawn up to the street (the normal case, the edge within 6 m of the
road) are tied to it as before.

## Types and styles

One type, `parking` (Make row: Parking), four styles.

| Style | What it is | Before | After | Keep / hide | Why |
|---|---|---|---|---|---|
| `standard` | 90° stalls, 2.5 m planted medians, cross lanes every 9 stalls, lane around the lot | D | B- | keep | the default and the reference lot: tree-lined medians and a tree avenue round the edge |
| `compact` | 90° stalls, planting only at the edges, no cross lanes: the most stalls (414 vs 341 on the test block) | D | B- | keep | clearly different: one big open field of stalls ringed by trees, for a stadium or a mall |
| `angled` | 60° stalls, medians | D- | C+ | keep | different geometry (slanted rows, wedge islands); the wedges still carry some loose shrubs |
| `green` | `standard` with 4 m medians, 80 % planting, cross lanes every 8 | D | (hidden) | **hide** | a near-duplicate of `standard` now that every style plants its medians in lines; the planting density slider gives the same leafy look |

`Recipes/shelf/parking.json`: `hiddenStyles: { "parking": ["green"] }`. The style keeps its code and still builds by id.

## Checks (logs, every run)

* Trees, bushes, lamps: "survivors" 0-3 objects hidden by the game per build, all shown again; the build audit leaves out
  1-3 bushes (declutter) where it left out 8-16 before.
* Fences: none in any parking build (no fence object, no vanilla lot).
* Dips: "pit check (after the build): no bowl deeper than 1.0 m" on every build, flat, L on a 7 % slope, pinch on hills;
  "net sag: none" except the known slope entrance below.

## Done

* Parking planting rebuilt in `Core/Layers/Parking/` only (commits `6c1e211`, `c7c85f0`, `d2b6d97`): trees and shrubs in
  straight lines down the medians and edge strips, spaced from the real crown, one species per line, row-end caps and
  free strips planted, no plant in a stall, density sets the spacing instead of deleting random plants.
* The strip mall and mall district lots get the same lines (fingerprints regenerated on purpose).
* Entrance-to-street tie tried and reverted (it made a funnel); grading is master's.
* Kept: `standard`, `compact`, `angled`. Hidden: `green` (near-duplicate of `standard`; the density slider gives its
  look), in `Recipes/shelf/parking.json`.
* Latest before/after: BEFORE `show-5-builds-1016-062605-103a`, AFTER `show-4-builds-1016-075429-c23c`. Pictures (under
  `.forensics\suite\runs\`): `<run>\shots\02-parking-standard.05-45-clean.png` and
  `<run>\shots\03-parking-compact.06-top-clean.png` in both runs.

## Not done / known problems

* The last commit `d2b6d97` (free-strip lines, tie revert) was never run in the game: the work was stopped while the
  shared game queue was backed up. It has no AFTER pictures; the next runner pass should build every parking style and
  the strip mall from this head. The grades above are from `075429-c23c`.
* On a slope, when the street is more than 6 m beyond the lot's edge (the runner's land, not a lot drawn up to the road),
  the entrance connector still climbs the bank between the graded lot and the street (as on master). A gentle ramp would
  need the lot's lowest terrace to step down toward the entrance without a funnel; not attempted further.
* The `angled` style's wedge islands still get a few loose shrubs from the jittered fill.
* Every build logs ~470-550 "OverlapExisting" temporaries (stall decals, bushes, perimeter trees) that the default "ignore
  collisions in my builds" keeps; with that option off they would be left out. Unchanged from master; not investigated.
* The strip mall fingerprints may need regenerating again after the Shops & squares branch merges.
* Lamps were only checked through the "survivors" log line, not looked at closely in the pictures.
