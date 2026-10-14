# Type wave: transport (restArea, transitPlaza)

Owner: builder 10 (branch t10). Files: `Core/Layers/Types/RestArea/*`, `Core/Layers/Types/TransitPlaza/*`,
`Recipes/restArea.json`, `Recipes/transitPlaza.json`, `Lang/en-US.restArea.json`, `Lang/en-US.transitPlaza.json`,
`Tests/Placemaker.Tests/Types/{RestArea,TransitPlaza}Tests.cs`.

## Requests to the lead (shared files, not edited)

1. **Make-row group "transport"** (UI/src/stages/DesignStage.tsx). Both layers declare
   `[AreaGenerator(…, Group = "transport")]`, but the UI still groups by id words: `transitPlaza` lands in
   "Squares" (matches `plaza`) and `restArea` in "Other". Please add
   `{ id: "transport", key: "Placemaker.UI.MakeGroup.Transport", en: "Transport" }` and prefer the catalog's
   generator group over the id regex. The en-US key is already in `Lang/en-US.restArea.json`.
2. **Real bus stops on the street** (optional, later): the plaza's bus bays are paved lay-bys with marking
   decals and shelters. Making them functional needs the parking layer's street bus-stop placement
   (`PlanBuilder.EmitBusStops`, internal) exposed as a Common helper.

## Notes

- Both types reuse the Parking planner unchanged via the registry (`TransportKit.PlanLot`): the host cuts a
  lot polygon touching its outline at the vehicle entrance, runs `parking` on it with one `entrance`
  marker, merges the sub-plan (surfaces, aisles, stalls, chargers, lot area, stats, `capacity` facts) and plans
  the rest of the outline without it.
- New categories (`transport.shelter`, `transport.bikeRack`, `transport.sign`, `transport.vending`,
  `transport.fuel`) are classified by `TransportCategoryRules` (name patterns, narrow on purpose; curate
  against catalog-dump.json when available). Bus/taxi shelters are also tagged `shelter` (never automatic there).
