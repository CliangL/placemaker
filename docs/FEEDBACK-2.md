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
