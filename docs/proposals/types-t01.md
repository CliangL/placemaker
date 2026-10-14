# Type wave — builder t01 (scrapyard, landfill): shared-file proposals

Both types work today without these; each item makes them look better in game.

1. **Variety for the industrial categories** (`Core/Build/AutoAssets.cs`, `VarietyCategories`): add
   `industrial.scrap`, `industrial.wreck`, `industrial.tire`, `industrial.container`, `industrial.pallet`,
   `industrial.barrel`, `industrial.garbage`, `industrial.pile`, `industrial.bale`. They are heaps and rows,
   not furniture: one automatic prefab per step makes a scrap mountain of 200 identical meshes.
   (Machines, cabins, barriers, skips, vents can stay uniform.)
2. **Litter decals** (`AssetHeuristics.DecalTheme`): decals are classified before `ICategoryRules` run, so a
   type cannot add a decal theme. Proposal: `decal.litter` for names containing litter/trash/garbage/rubbish/
   paper/wrapper; the landfill face and fly-tip would use it before `decal.dirt`.
3. **Gull/bird spawners**: mesh-less spawners return before `ICategoryRules`, so "seagulls over the landfill"
   needs a `BuildCategories.AnimalSpawner` sub-pool by species name (e.g. `spawner.gull`).
4. **Object height on terraform**: yards place heaps on terraformed mounds and set `PlannedObject.Position.y`
   to the planned ground (`YardKit.Settle`). Please confirm the build snaps objects to the final terrain
   (terraform is applied before objects) so props never float/sink if the preview height differs.
5. `YardKit` (Core/Layers/Types/Scrapyard) — height-field sculptor with edge blending, haul strips, heaps,
   rows, material bays, gate compound, broken fences, dominant-edge grid frame — is reusable by the other
   industrial builders (construction site, quarry, abandoned lot, logistics yard); consider moving it to
   `Core/Layers/Common` once the wave lands.
