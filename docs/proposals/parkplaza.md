# Park / plaza / split / add-ons — contract proposals and game-layer notes

Workstream: park-plaza (branch `parkplaza`). Nothing here required editing `Contracts.cs`,
`Composition.cs` or `AssetCategories.cs`; every item below has a working workaround in
`Core/Layers/**`. Ordered by impact.

## A. Contract proposals (lead)

1. **Object footprints in `Composer.Reserve`.** Reserve uses `DefaultRadius(role)` (centerpiece
   6 m, furniture 1 m). Real footprints differ: a "grand" fountain basin is 12 m, restrooms ~4 m,
   playground items ~3 m, campground pitches 5–8 m. A later sibling can therefore build into them.
   Proposal: `PlannedObject.Radius` (float, 0 = role default) used by `Reserve`, or `Reserve` looks up
   `AssetInfo` bounds. Workaround: layers place their own items with real clearances, and recipes
   put large features in their own region (`core`) where siblings rarely go.
2. **Reserve terrain/water.** `Reserve` ignores `PlannedTerraform` and water surfaces, so an
   add-on targeting the park's whole region could place into the lake. Proposal: reserve rings of
   terraforms with `Permanent` or a `WaterLevel` as polygons. Workaround: park lawns (`green`,
   `core`) exclude the lake, and shipped recipes target those.
3. **Category availability in `SiteContext`.** Core cannot see which categories have installed
   assets. Workaround: `Core/Layers/Common/Assets.cs` → `AssetAvailability.Publish(IDictionary<string,int>)`;
   **the game catalog must call it** whenever its lists change (thread-safe snapshot swap; unknown
   = available). Proposal: `SiteContext.CategoryCounts` instead of the static.
4. **Automatic asset choice.** Placements with `AssetRef.Id == ""` mean "automatic": the catalog
   should pick an item of the role's category using `PlannedObject.Variation % count`, so
   automatic mixes vary per object yet stay stable across re-plans. Groves/belt sections share one
   Variation (single-species stands). When Id is empty, `Fallbacks` carry vanilla names to try first
   (`"Pedestrian Area"`, `"Pedestrian Access Location"`, `"Forest Animal Spawner"`, `"Pavement Surface 01"`).
   Please document that `Resolve` tries Fallbacks even when Id is empty.
5. **Split marker pairing.** `EdgeMarker` has no group id; the split layer pairs `split` markers by
   list order (0–1, 2–3…). Proposal: `EdgeMarker.Group` (int) so the tool can edit one divider.
6. **Recipe description.** `RecipeInfo.description` exists in `contract.ts` but not in
   `Core.Recipe`; shipped JSON carries `descriptionKey`. Proposal: `Recipe.DescriptionKey`.
7. **AssetCategories.** Camping mixes tents/caravans and campfires in one category, so automatic
   campfires may pick a tent. Proposal: `camping.pitch` and `camping.fire` (keep `camping` as the
   union). The campground uses `Camping` for both today.
8. **Units.** Sliders use `perHa`, `per100m` and stats use `m2` besides the documented units.
   en-US text for them: `Placemaker.Unit.perHa` ("/ha"), `Placemaker.Unit.per100m` ("/100 m").
9. **Issue arguments that are keys.** `Common.Issue.NoAssets {0}` gets a category key
   (`Placemaker.Category.<category>`), `Common.Issue.NoSpace {0}` a layer name key. The UI should
   translate any string argument that starts with `Placemaker.`.
10. **Lang file.** Added `Lang/en-US.split.json` (new area file, no overlap with others). Shared
    `Placemaker.Stat.*`, `Placemaker.Common.*`, `Placemaker.Category.*`, `Placemaker.Group.*` and
    `Placemaker.Unit.*` keys live in `en-US.park.json`.
11. **Sub-region severity.** `LayerBase` reports invalid/too-small areas as **Error only for the root
    region**; for a child region (one lawn, one split part) it is a Warning, so one tiny lawn never
    blocks the whole build. Suggest the composer adopt the same rule for its own issues.

## B. What the game layer must implement for these plans

| Plan element | Meaning / expectation |
|---|---|
| `PlannedNet` `Pathway` / `PathwayWide` | Pedestrian paths (vanilla e.g. `PedestrianPathWide01`; `pathAsset` control overrides). Curves are real cubic Béziers. `StartAttach`/`EndAttach` = road (`BoundaryAttachment.ExternalId` or `NearbyNet.ExternalId`) at a park entrance — connect via LocalConnect / edge split at `*AttachT` (RESEARCH-NOTES 15, 18). |
| `PlannedNet` `Fence` | Fence nets, straight runs ≤ 60 m, 0.2 m inside the outline, gaps at entrances. |
| `PlannedArea` `PedestrianArea` | Plaza walkable navigation area: vanilla `"Pedestrian Area"` (NavigationAreaData), see ParkManager `PlazaAccessPlacement.cs`. |
| `ObjectRole.AccessPoint` | `"Pedestrian Access Location"` (RouteConnectionData marker), yaw faces inward; must connect to the nearest road/sidewalk. |
| `ObjectRole.AnimalSpawner` | Marker object `"Forest Animal Spawner"`. |
| `PlannedTerraform` (park lake) | `Kind=Lower`, `Permanent=true`: excavate inside `Ring` to `TargetHeight` (lowest bank − 3.5 m), then a water source at `WaterAnchor` with level `WaterLevel` (lowest bank − 0.5 m). ParkManager did this with levelling brush dabs (`ParkLakePlanner.ExcavationDabs`, `Tools/ParkLakeBuild.cs`). Show it in `Summary.permanent`; a Warning issue `Placemaker.Park.Issue.LakePermanent` is emitted too. Off by default, never randomised. |
| Surfaces | `Grass` (park ground), `Plaza` (plaza paving), `Paving` (fountain apron), `Decorative` (playground sand, campground pads → category `surface.natural`), `Ground` (campground floor), `Water` (lake bed). Every ring already passes `SurfaceAcceptance`. |
| Objects | `Tree` with `Age` 1–3 (young…mature, map to TreeState); `Bush`; `Bench`/`Lamp`/`Bin`; `Centerpiece`; `Planter` (planters, flowerbeds); `Furniture` (playground items, restrooms, kiosks, shelters, picnic tables, tents, campfires — category from the layer's control, see section C). Bench yaw follows ParkManager's in-game-verified convention. |

## C. Asset categories the catalog must classify

`tree`, `bush`, `bench`, `lamp`, `bin`, `centerpiece` (fountains, statues), `planter`,
`flowerbed`, `playground`, `restroom`, `kiosk`, `shelter`, `picnic`, `camping`, `fence`,
`net.pathway`, `surface.grass`, `surface.paving`, `surface.natural`.
Categories that may be empty in vanilla (`flowerbed`, `restroom`, `shelter`, `kiosk`, `camping`,
`picnic`, `playground`) are handled: the layer reports Info `Common.Issue.NoAssets` and places
nothing of that kind, as long as `AssetAvailability.Publish` reports 0 for them.

## D. Settings formats

* Plaza `arrangement` (`ControlKind.Arrangement`, ≤ 5 items, order matters): tokens `kind` or
  `kind:assetId`; kinds `bench, lamp, bin, tree, bush, planter, flowerbed, kiosk`
  (`PlazaLayer.ItemKinds`, labels `Placemaker.Plaza.Item.<kind>`). A token without asset uses the
  `<kind>Asset` control (automatic when empty). `ControlSchema.AssetCategory` is empty because items mix
  categories; use `PlazaLayer.Describe(kind, …)` for the per-item category.
* Markers: park/campground `entrance`, plaza `access`, split `split` (pairs).
