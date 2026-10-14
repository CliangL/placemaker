# Type wave, builder 9 (adventurePlayground, memorialPark): shared-code requests

1. **Surface classification plug-in.** `ICategoryRules.Classify` runs for visible static objects only;
   surfaces return earlier in `AssetHeuristics.Classify` (SurfaceKind + LeisureHeuristics.SurfaceExtra).
   Types therefore cannot add surface categories (e.g. `surface.rubber` play surfaces, `surface.bark`
   mulch). The adventure playground maps "Rubber" to `surface.paving` and "Bark" to `surface.dirt` →
   `surface.natural`. Proposal: an optional `ICategoryRules.ClassifySurface(lower, categories)` (or call
   `CategoryRules.Classify` for surfaces too, behind a trait check), so a type can add `surface.rubber`
   with names like "Rubber", "Playground", "Tartan", "Safety".
2. **Objects on terraformed ground.** `LayerContext.Object` takes Y from the original ground
   (`SiteContext.HeightAt`). Monuments on a raised forecourt and items on a play mound rely on the game
   layer snapping objects to the terrain after terraform. If it does not, `LayerContext` could accept a
   height override (`TerrainResult.HeightAfter`).
3. **Junction-aware paths.** CS2 joins courses only at bit-identical nodes. The memorial park inserts
   junction nodes into its straight axis lines so spurs meet them; `LeisureKit.Path` / `ConnectGates`
   end spurs mid-segment on curved park paths (no join). A shared "split a planned path at p" helper in
   ParkPaths would let every type connect spurs properly.
