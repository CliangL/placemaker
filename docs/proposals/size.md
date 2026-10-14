# Layout size, thin walks, one edge margin (FEEDBACK-4)

Playtest 4: "everything is a little too large — a quarter or half size, across the board"; thick paths by
default; odd padding; memorial walks painted blobs with benches on the lawn.

## 1. Layout size — the mechanism

**Contract (additive):** `SiteContext.LayoutScale` (1 = the design size the generators were written at;
read through `SiteContext.Scale`, clamped to 0.25–1.5) and `SiteContext.EdgeMargin` (design metres, default
2). `WithHeight` / `WithLayout` copy them, so features of a terraforming parent see the same scale. The game
passes the Options settings **Layout size** (25–150 %, default **50 %**) and **Edge margin** (0–10 m,
default 2 m); changing them re-plans. Core's default stays 1 so recipes, tests and saved plans are unchanged
unless a host asks for a size.

**What scales:** layout *distances* — path loop sizes, path-planner spacing and boundary clearances, meander
amplitudes, spacing of benches/lamps/bins along paths, plaza / feature / place footprints, clearings, grove
and belt sizes, parterre beds, memorial rooms and forecourt, grid pitches, margins. Counts chosen by area
(loops, spokes) use the *design area* `area / scale²`, so a smaller layout gets more, smaller loops.

**What never scales:** prop and net sizes (assets are real size), net widths, and real-dimension layouts:
parking stalls and aisles, roads, regulation sports fields and courts (amenity rectangles), golf hole
lengths, greens and tees, stairs/steps, heights and depths.

**Why this design (and not a composer transform).** Planning in a scaled "design space" and mapping the
result back would shrink every clearance with it: a bench placed 2.8 m from a path centre would land 1.4 m
from it, trees would stand in each other. Collisions must stay footprint-based (radius + radius), so the
scale has to be applied *to the distances a layer chooses*, not to the geometry. Three entry points, one
value:

- `LayerContext.Scale`, `ctx.M(design)` / `ctx.M(design, realFloor)` and `ctx.EdgeMargin` for layer code;
- sliders declared `.Layout(realFloor)` are read scaled by `ctx.F(id)` (floored at a real size, e.g. tree
  spacing never below two crowns). One declaration per control, so no read site can forget it; widths,
  heights and regulation sizes are simply not flagged. `ctx.Raw(id)` reads the design value;
- deep static helpers that never see a context (park path planner, path figures, planting) use the ambient
  `Layout.M(...)`. `LayerBase.Plan` opens the scope from the input's `SiteContext` around `PlanCore`, so it
  always equals `ctx.Scale`; it is `[ThreadStatic]` and nests, so parallel previews stay independent.

Helpers take **design metres** (`PathBenches`, `PathLamps`, `PathBins`, `NaturalPaths`, `LeisureKit.Plant`
clump size) and scale inside; callers holding an already-scaled slider pass `ctx.Raw`.

**Densities.** Furniture per 100 m of path scales with 1/scale (closer along the paths). Areal planting counts
(trees/bushes per ha) scale with 1/scale — between keeping the density (too sparse at 50 %) and keeping the
spacing (4× the trees at 50 %, real-size crowns would carpet the park). Tree-to-tree and object-to-path
clearances stay footprint-based; a 1 ha park at 50 % has ~45 trees, ~80 bushes, ~13 benches, ~350 m of path
(`LayoutSizeTests.OneHectareParkAtHalfSizeHasAReasonableObjectDensity`).

## 2. Thin walks

- The automatic ground walk is the thin vanilla "Pavement Path" (3 m): `AutoAssets.PickWalk` never picks a
  bridge, pier or quay for a ground walk and prefers thin for `Pathway`; `AssetHeuristics` ranks Pavement
  Path first in the game's pool. `PathwayWide` takes the wide walk and its planning half width is now its real
  4 m, so lamps and benches stand beside it, not on it.
- Defaults: park `pathWidth = narrow` (presets narrow; *urban* keeps "mixed" main boulevards; the *modern*
  promenade avenue stays wide), formal garden / greenway / zen garden narrow, memorial procession thin unless
  ≥ 8 m (monument square). Wide stays where it is the point: promenades (park, sports park, amphitheatre,
  waterfront), triumphal-arch approach, fairground streets, golf cart paths, the split "wide" divider.

## 3. Walks are path nets

- Memorial: the procession band is no longer painted (optional **Paved procession** toggle, on in the
  monument-square preset); rooms get an inner walk from their spur, benches stand beside it; sculpture pads
  only with paving; benches and bins along every net (`PathBenches`, new `PathBins`).
- Formal garden: the main axis is always built, cross walks run between the parterre columns as path nets
  (T-joined on the axis), benches/lamps/bins along the nets; the walk gap fits a path plus hedges.

## 4. One edge margin

`EdgeMargin × scale` replaces stacked fixed insets where paths keep off the outline: park path planner
(`max(scaled 5 m, edge margin + path half width + 1)`), park/leisure meander and formal-axis checks. The
park's planting belt and edge strips scale with the layout; road clipping stays 0.25 m. Object clearances
from the outline remain footprint-based on top. Free path ends on the selected outline (entrances without a
road, edge-to-edge walks) are pulled in by their half width so the end cap stays inside the area.

## Tests

`LayoutSizeTests`: clamping and copying, ambient scope nesting, scaled sliders with floors, thin park paths,
more benches/path at 50 %, 1 ha density, every fill type deterministic and inside at 50 % (with timing),
furniture clear of paths, benches beside paths (memorial, formal garden), memorial paving only at the
forecourt.
