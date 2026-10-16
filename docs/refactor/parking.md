# Parking: category review (rf-parking, 2026-10-16)

The user's grade: **D-**. "It does its job ... the trees go away ... the fences should be working perfectly ... it's kind of
boring." The brief: no new features; make it work and look tidy. Trees, bushes and lamps stay visible; fences perfect
(never across an entrance, no farm fences); no dips. Keep the styles that are really different, hide near-duplicates.

## What was wrong (seen in the game runner)

BEFORE run: `show-5-builds-1016-062605-103a` (master `a71a842`, every style on the flat 150 x 140 block, 45°, top, plan,
terrain). The core refactor had already fixed the invisible trees (the farm "field" lot), the farm fences and the
lot-entrance dip; the BEFORE logs confirm it: no vanilla lot, no fence object, "survivors: no object hidden" (2-3 bushes
shown again), "pit check: no bowl deeper than 1.0 m", terrain -0.25 to 0 m.

What was still wrong was the **planting**: trees and bushes in clumps over the aisles and the stalls.

* Cause: the planting spacing came from the catalog's plant bounds, which are the game's shrunk collision core (2.5 m for
  the London plane), so trees were spaced 4 m apart while their crowns are 7-10 m. On top, the fill was a jittered grid
  with value-noise clumping anywhere in a 2.5 m median, then thinned at random by the density setting. Result: lumps of
  overlapping crowns, gaps, bushes on the median edges, crowns over the driving aisles.
* Fix (`Core/Layers/Parking/Vegetation.cs` `PlanRows`, `PlanBuilder.cs`): the green strips behind the stall rows (the
  medians, the edge strip behind a perimeter row) are planted first, in **lines**: the strip's middle is found by probing
  straight back from each stall's back corners, the points of one strip become one straight line, the line is split at
  cross lanes, and trees go on every k-th point (k from the crown size and the density: at 50 % one tree every 3 stalls),
  placed symmetrically, one species and one growth stage per line; shrubs fill between the trees, off the trunks.
  The old jittered fill then only adds to what is left (row-end caps, corners, wide islands), and its trees now need
  1.4 m of green around the trunk (none land in a 2.5 m median any more). Density sets the spacing of the lines instead
  of punching random gaps. Plants are never placed inside a stall (a corner of an odd outline could put a shrub there).
* Tree spacing now comes from the crown (the same estimate as `DressingWorld.CrownOf`: half the height x 0.56, at least
  7 m apart), shrubs from their own size (2-4 m).

Shared code: none outside `Core/Layers/Parking/`. The commercial types (strip mall, mall district, shops park...) plan their
parking lots with this layer (`Mall.cs` -> `GeneratorRegistry.Get("parking")`), so their lots get the same tidy planting.
That is intended ("the parking lots look weird"), and it changes the strip mall fingerprints
(`Tests/Placemaker.Tests/Fixtures/stripmall-fingerprints.txt`, regenerated on purpose with `PM_UPDATE_STRIPMALL=1`).

## Types and styles

One type, `parking` (Make row: Parking), four styles.

| Style | What it is | Grade before | Grade after | Keep / hide | Why |
|---|---|---|---|---|---|
| `standard` | 90° stalls, 2.5 m planted medians, cross lanes every 9 stalls, lane around the lot | D | (filled after the AFTER run) | keep | the default; the reference lot |
| `compact` | 90° stalls, planting only at the edges, no cross lanes: the most stalls | D | | keep | clearly different: a big open lot ringed by trees |
| `green` | `standard` with 4 m medians, 80 % planting, cross lanes every 8 | D | | | |
| `angled` | 60° stalls, medians | D- | | keep | different geometry (one-way feel, herringbone rows) |

## Iterations

(filled below)
