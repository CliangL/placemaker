# Playtest 2 (2026-10-13) — feedback & decisions

## Decisions
- **Shelve mixing and splitting in the UI.** Make row = single types only: Parking, Plaza, Park, Campground, Roads.
  Engine (recipes/composer/split layer) stays for later. Refine each type deeply instead.
- **Features instead of mixes** (`Core.FeatureInfo` / `IHasFeatures`, UI `StepView.features`, trigger `setFeature`):
  - Park: central plaza (shape circle/square, position centre/corner/edge, size %), playground, pond (lake), picnic, restrooms, fountain, big trees, flowerbeds.
  - Plaza: trees / green islands, playground, fountain (bigger), kiosk.
  - Roads: zoning along new streets.
  - Parking: (planting, lighting, EV… already options).
- **UI: compact, "engineering program" density**, sections collapsible and **collapsed by default** (remember per group), little scrolling. Make cards: icon + name only (done).

## Bugs / issues seen
1. **Inconsistent automatic assets** (screenshot: every fence segment a different fence; stadium seats used as benches;
   sandwich-board signs scattered). Automatic must pick ONE asset per role per build (consistent fence/bench/lamp/bin);
   variety only for vegetation. Catalog must be curated: stadium/spectator seats ≠ benches; signs/clutter not in
   automatic pools; prefer vanilla park palettes.
2. **Entrance markers don't follow the outline** when the border is moved — re-project markers onto the edited outline
   (or re-suggest automatic ones).
3. **Area drawn over a road** (screenshot: park polygon overlapping "Bayview Street", plants on the road): the area
   should be smart — subtract existing road/net corridors (+ sidewalk) from the area before planning (setting
   "Keep off existing roads", default on), entrances snap to the new edge.
4. **Preview of paths/roads looks awkward** (thick yellow bands): prefer GRG-style dashed centre lines with subtle width
   edges; must look good.
5. Too much scrolling in the options list.

## Round 3 (game-tool) — implemented
- **Features**: Park, Plaza, Campground, Roads implement `IHasFeatures` (playground, picnic, restrooms, fountain,
  big trees, flowerbeds; plaza fountain preset `size=large`; roads: edge zoning on `edge`). `StepView.features`
  lists them (enabled = matching child step exists); `setFeature` adds/removes that child as one undo step;
  `selectRecipe` adds DefaultOn features (`DesignEditor.AddDefaultFeatures`).
- **Make row** (`Features.ShowInMakeRow`): shipped recipes are listed only when the root is a Fill layer with no
  children/overrides. Mixes and type+feature recipes (roads+parks, split, park-with-playground, roads-with-zoning)
  are hidden but still load by id; player recipes are always listed.
- **Markers follow the outline** (`Core/Tool/MarkerFollow.cs`): point/edge drag keeps segment+T, other changes
  project to the closest point; automatic markers > 20 m from any road are dropped; untouched steps are then
  re-suggested by `MarkerAuto`. Runs in the shape rebuild, so the undo snapshot holds outline + markers together.
- **Keep off existing roads** (setting, default on; `Core/Tool/AreaClip.cs`): drawn/painted outlines minus road
  corridors (composition width incl. sidewalks + 0.25 m); largest piece kept, Info issue when trimmed/split,
  slivers ignored, pathways ignored, attachments recomputed. Block/perimeter outlines are traced on road edges
  and not clipped. Overlay shades the left-out part.
- **Plan overlay**: dashed centre lines in role colours (white streets, cyan avenues, warm-yellow paths, blue
  parking aisles) + faint edge lines at the real (asset) width; small dots for furniture, soft crown-size discs for
  trees; line width scales with camera distance to the area.
