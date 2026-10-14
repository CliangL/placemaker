# Type wave — builder t04 (golf, lookoutHill): shared-code requests

1. **Child layers on reshaped ground.** Features of a layer that terraforms (lookout hill: viewpoint spots
   on the summit) plan with `SiteContext.HeightAt` = original ground, so their object Y is the old height.
   The hill drapes its own objects/nets/surfaces onto the planned height (`HillField.HeightAt`). Proposal:
   `AreaPlan.HeightAfter` (Func<float2,float>, optional) that the composer chains into the children's
   `SiteContext.HeightAt`.
2. **Surface subcategories through `ICategoryRules`.** `AssetHeuristics.Classify` returns early for surface
   prefabs, so a type cannot add e.g. `surface.green` (putting-green turf) or `surface.fairway`. Golf uses
   `surface.grass` + separate Asset controls (fairway / green / bunker) until rules can see surfaces.
3. **ParkSchemaTests lists.** Golf/hill tests call `ParkSchemaTests` methods directly with their ids; adding
   "golf", "lookoutHill", "puttingGreen", "viewpoint" to its `Layers` and `RecipeFiles` lists would make the
   shared suite cover them too.
4. **Make-row group labels.** Added `Placemaker.UI.MakeGroup.Sports` / `.Nature` in our lang files; the UI
   may want one shared set for all twelve groups.
