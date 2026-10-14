# g3 (paths & ground) — shared-platform requests

Owner of the asks: g3. Needed by `Core/Layers/Park/ParkPathHierarchy.cs`, `ParkPathCategories.cs` and
`Core/Layers/AddOns/Ground/GroundPatchesAddOn.cs`. Everything works today through fallbacks; these make it exact.

1. **Nets reach `ICategoryRules`** (`Core/Build/AssetHeuristics.cs`, net branch). It returns before
   `CategoryRules.Classify`, so `net.bike` / `net.trail` (declared in `ParkPathCategories.cs`, rule
   `ParkPathCategoryRules` already guards on `AssetTraits.Net`) stay empty and their asset pickers list nothing.
   Ask: call `CategoryRules.Classify(lower, t, r)` in the net branch before returning (additive).
2. **Keep bike nets out of the automatic `net.pathway` pool.** Vanilla `TwoWayBikePath`,
   `OneWayBikePath` and `PedestrianPathWithBikeLanes` may carry `PathwayData`; `DefaultScore` gives
   "pedestrian…" +40, so an automatic footpath could become a bike path. Ask: in `DefaultScore(Pathway)`
   subtract a lot for "bike"/"bicycle"; `AutoEligible(net.pathway)` = false for them.
3. **`PlannedNet.Category`** (additive, like `PlannedSurface.Category`), honoured by `AutoAssets` /
   `PlanWork` (`RoleCategories.ForNet`). Today the bike/trail tiers use an automatic `AssetRef` with name
   fallbacks (`TwoWayBikePath`, `PedestrianPathWithBikeLanes`, gravel names…) and otherwise resolve as
   pathway / wide pathway, which is the intended graceful fallback but cannot pick custom bike nets.
4. **Surface categories `surface.gravel`, `surface.rock`** via the surface-rules hook g5 is adding. The ground
   feature currently asks `surface.natural` for gravel and rocky patches.
