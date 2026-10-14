# Art pass a6 (shared add-ons): asks for shared code

Found while curating the add-ons against the real catalog dump (2026-10-13). Worked around inside
`Core/Layers/AddOns/*` (`AmenityCategoryRules`, `MonumentCategoryRules`, `AddOnAssets`); the root causes
live in shared files I did not touch.

1. **`LeisureHeuristics` ("sprinkler", "spray" → `water.jets`)** tags `AgricultureSprinklerSystem` and
   `Prop_TractorSprayer01`. a6 makes them auto-ineligible for `water.jets`; the splash pad no longer uses
   the category unless the player picks a jet. Suggest excluding farm names at the heuristic.
2. **`monument.column`** is filled entirely by net supports (`Pier01Pillar*`, `Pillar Top *`,
   `SubwayStationElevated02Pillar`). a6 rejects them for auto; follies fall back to a poplar colonnade.
3. **`centerpiece.statue`** holds only clock hands (`NotreDameClockPointers`, `TrainStation01ClockPointers01`).
   a6 rejects them; statue chains now end in a sculpture, then a fountain, and stand-ins are never scaled up.
4. **Uniform pools hide variety**: `sports.fitness` knows one chin-up bar (the other `GymPark*` stations
   classify as `decoration`), `picnic` leads with blankets, `kiosk` contains ShortMarket billboards. The
   add-ons name curated vanilla lists (`AddOnAssets`); the classifier should still be fixed.
5. **Decal densities are set by hosts** (`PlazaLayer` feature preset: cracks 0.8/100 m²). The decals add-on
   now scales counts by the theme's real decal size (leaves/dirt are 6 m squares) and keeps them apart, so
   host presets need no change. Re-check host presets that assumed the old density.
