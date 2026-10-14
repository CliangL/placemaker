# Research notes — addendum (2026-10-13, gamebuild round 2)

Decompiled from CS2 1.6.2f1 `Game.dll` with ilspycmd (full source dump was in
`%TEMP%\pm-decomp\game`). Rule numbers continue RESEARCH-NOTES.md.

## Area shape validation — why our builds failed with `InvalidShape`

27. `ErrorType.InvalidShape` for areas comes only from `Game.Areas.ValidationHelpers.ValidateArea`
    (shape checks are skipped for `TempFlags.Delete` and `AreaFlags.Slave`). Tolerance
    **d = `AreaGeometryData.m_SnapDistance` × 0.5**, XZ only. `AreaInitializeSystem` sets
    `m_SnapDistance` from the area **type** (`AreaUtils.GetMinNodeDistance(AreaType)`):

    | type | snap | d |
    |---|---|---|
    | Surface | 0.75 | **0.375 m** |
    | Space (pedestrian/hangaround) | 1 | 0.5 m |
    | Lot | 8 | **4 m** |
    | District | 32 | 16 m |
    | MapTile | 64 | 32 m |

28. For every edge (a,b) of a complete ring:
    * the node **before a and the node after b must be ≥ d from the edge** (unconditional). Hence
      **every edge ≥ d** and an **acute spike fails when (shorter adjacent edge) × sin(angle) < d**;
    * every **non-adjacent edge must stay ≥ d away** (segment–segment distance, confirmed by an
      inward band test `GetEdgeQuad` from −0.1 to −d) → necks/near-touches narrower than d fail;
    * 3-node rings: each node ≥ d from the opposite edge.
    * No limits on node count, area, collinear points (as long as edges are ≥ d).
29. **Triangulation failure is also `InvalidShape`**: if the shape passed but `GeometrySystem`
    produced no triangles (nodes inset 0.1 m, `AreaFlags.NoTriangles`), ValidateArea emits
    InvalidShape (rule 6 "silently dropped" is only true for areas not validated as Temp).
30. **Closing node**: `GenerateAreasSystem` strips a last node that is bitwise equal to the first
    (incl. y) and sets `AreaFlags.Complete` — **required** for new areas (else an open polyline).
    For definitions with `m_Original` and without `CreationFlags.Recreate` the ring is already
    Complete and a duplicate would become a zero-length edge → InvalidShape: pass the plain ring.
31. Other area errors: `LotData.m_MaxRadius` only applies to lots owned by a Transform (→
    LongDistance). Same-type overlap (Surface/Surface, Lot/Lot) → OverlapExisting, but only for
    Physical/Protected areas; **in game, surfaces are not physical** and a lot with
    `m_AllowOverlap` loses `PhysicalGeometry`. Water: InWater/NoWater for physical areas only.

**Root cause in playtest 1:** our `SurfaceAcceptance` policy allowed edges ≥ 0.3 m and did not
model rule 28 (spikes, narrow necks), while the game demands 0.375 m and the spike/neck rules; ring
pieces from parking/park splitting regularly contain such short edges and slivers. Fix:
`Core/Build/AreaRings` (rules 27–28, conservative) is applied to every ring with the prefab's own d
right before emission (`BuildService.EmitPreparedRings`): offending nodes are removed, otherwise the
ring is split via `SurfaceAcceptance.MakeAcceptable2D`, else skipped and reported. Core layers should
call `AreaRings.IsValid(ring, AreaRings.SurfaceMinNodeDistance + AreaRings.Margin)` (or
`PrepareSurface`) themselves so the preview already shows what will be built.

## Nets

32. `Game.Net.ValidationHelpers.ValidateEdge`: InvalidShape when the left/right edge beziers fold
    (curve or node too tight for the road width; edges > 0.1 m, node geometry > 0.05 m) or a
    FixedNodeSize composition has a misaligned end tangent (|dot| < 0.99999). ShortDistance:
    length < `NetGeometryData.m_EdgeLengthRange.min`; LongDistance: > max × 1.1 (default 200 → 220);
    SteepSlope: end slope ≥ 2 × max steepness or overall ≥ max + 0.0005; TightCurve: track lanes only.

## Money

33. `ToolApplySystem` sums `Temp.m_Cost` of all applied temporaries and calls `PlayerMoney.Subtract`
    on the city — **same units as the money display**, int, negative = refund. The same sum drives
    `NotEnoughMoney`.
34. Objects: `m_Cost = PlaceableObjectData.m_ConstructionCost` (trees × `EconomyParameterData.
    m_TreeCostMultipliers` by age). **Areas/surfaces: nothing sets m_Cost → free, no upkeep** (so a
    $0 surface price is correct).
35. Nets: `Game.Net.CostSystem` prices only Temps with `TempFlags.Essential`, which
    `GenerateEdgesSystem` sets only for definitions with `CreationFlags.SubElevation | Stamping`.
    Vanilla NetToolSystem always sets SubElevation; **GRG/PM/PLT and our round 1 did not → their
    roads were built for free.** Formula `NetUtils.GetConstructionCost = max(1, round(len/8)) ×
    (PlaceableNetComposition.m_ConstructionCost + elevationSteps × m_ElevationCost)` (composition
    from `Composition.m_Edge`); upkeep `max(1, round(cells × m_UpkeepCost))` is charged by the
    budget for every permanent edge anyway. SubElevation also changes splitting/intersections in
    `CourseSplitSystem` and node elevation in `GenerateNodesSystem`, so Placemaker keeps its
    definitions as they were and **charges the nets itself right after Apply with that formula**
    (`BuildService.Cost.cs`, refused up-front with NotEnoughMoney when the city cannot pay).

## Selection, bulldozer, clearing

36. `public bool ToolRaycastSystem.GetRaycastResult(out RaycastResult result)` (instance);
    `RaycastResult { RaycastHit m_Hit; Entity m_Owner; }`. The in-game **bulldozer raycasts
    `Areas` with `Lots | Surfaces`** (+ StaticObjects | Net); the default tool Lots only.
37. Deleting an owner cascades through its buffers: `SubElementDeleteSystem` deletes SubAreas and
    SubNets (a node still used by a foreign edge just loses its Owner), `SubObjectSystem` deletes
    owned SubObjects.
38. `BulldozeToolSystem` asks for confirmation only for (non-spawnable) buildings:
    `EventConfirmationRequested` → UI → `ConfirmAction(bool)` → `State.Confirmed` → `Apply`.
39. **No vanilla flag clears world trees.** `ClearArea` (prefab component → `GeometryFlags.ClearArea`)
    only clears a building's own sub-elements. `CanOverrideObjects` on a *physical* area merely hides
    Overridable trees/props (`Overridden`) while the area exists. To remove them, emit the
    bulldozer's delete definition per object: `CreationDefinition { m_Original = e, m_Flags = Delete }`
    + `Updated` + `ObjectDefinition { position/rotation, m_ParentMesh = -1, m_Probability = 100,
    m_PrefabSubIndex = -1 }` (`BulldozeToolSystem.AddEntity`).
40. Unknown prefabs in a save (mod removed): `ResolvePrefabsSystem` keeps the loaded prefab entity
    disabled under an "obsolete" id; entities referencing it keep their PrefabRef. Children owned by
    such an entity are at risk, therefore Placemaker members are never owned by the owner lot.

## Ghost preview, decals, icons, terrain painting (gamebuild round 4, decompiled 1.6.2f1)

41. **Tool cadence.** `ToolOutputSystem` runs the `ClearTool` phase (only `ToolClearSystem`: Deleted on
    every Temp, un-hides Hidden originals) when the *active* tool's `applyMode` is Clear, `ApplyTool`
    when Apply, nothing when None. Vanilla tools (ObjectToolSystem.Update) set Clear + `DestroyDefinitions`
    (all `CreationDefinition` *without* Updated) + new definitions in the same update when something
    changed, None otherwise — temporaries then persist unchanged. `Generate*Systems` only consume
    definitions that carry `Updated`, so definitions emitted on later frames under None ADD temporaries.
    `ToolSystem.activeTool` switch: old tool disabled (OnStopRunning), temporaries survive until the new
    tool clears.
42. **Error icons without spam.** `ValidationSystem.CancelOptional`: an error on a temp with
    `TempFlags.Optional` (from `CreationFlags.Optional`, only honoured by `GenerateObjectsSystem`) cancels
    it — `TempFlags.Hidden | Cancel`, cost removed, **no icon** (icons only for brushes when everything is
    cancelled). Nets/areas have no Optional: their Temp icons can be hidden with `Game.Tools.Hidden`
    (`NotificationIconBufferSystem` excludes Hidden; IconCommandSystem re-adds/removes Hidden only when it
    re-processes that icon's command).
43. **Temp surfaces are never textured.** `AreaBatchSystem.m_UpdatedQuery`/`m_BatchQuery`,
    `AreaColorSystem` (infoviews only) and `Game.Areas.SearchSystem` (culling tree) all have `None Temp`;
    the jobs themselves never read Temp. The area tool shows Temp areas only via `AreaBufferSystem`'s
    overlay fill (when `activeTool.requireAreas` has the type). Fix used by Placemaker
    (`GhostSurfaceSystem`): tag our Temp surfaces (`PlacemakerGhostSurface`, ModificationEnd, while
    Updated) and replace the two AreaBatchSystem queries by a union with `{Batch, Temp, tag}` built by
    that system's own `GetEntityQuery` (reflection). `QueryCullingJob` allocates/marks visible directly
    from the updated query, so untreed temporaries render; Deleted+tag frees them.
44. **Decals** are static object prefabs whose `RenderPrefab`s have `DecalProperties`; `MeshSystem` then
    sets `MeshFlags.Decal` on the mesh's `MeshData.m_State`. Reliable test: every `SubMesh` of the object
    has that flag (`AssetCatalogSystem.IsDecalOnly`). Building decals (court markings) are
    `BuildingExtensionPrefab`s, not static objects.
45. **Icons.** `ImageSystem.GetIcon` = `UIObject.m_Icon` (resolved); `GetThumbnail` falls back to
    `thumbnail://ThumbnailCamera/<id>` which renders nothing useful for areas. Asset Icon Library (79634)
    writes `UIObject.m_Icon` for ~14k prefabs (only 17 SurfacePrefabs) from its `.Thumbnails` folder in
    its first update after `OnGamePreload` — possibly after our scan, hence the periodic recheck.
    Surface textures: `RenderedArea.m_BaseColorMap` (`AssetReference<TextureAsset>`, `Load()`/`Unload()`,
    tinted by `m_BaseColor`); a PNG can be served to the UI with
    `Colossal.UI.UIManager.defaultUISystem.AddHostLocation(host, folder)` → `coui://host/file.png`.
46. **Terrain texture painting exists.** `TerraformingTarget.Material` + `TerrainMaterialProperties.m_Type`
    (`TerrainMaterialType.None` = erase, `Extra1..Extra4`). `GenerateBrushesSystem` passes the strength
    through for Material; `ApplyBrushesSystem` → `TerrainMaterialSystem.ApplyBrush` blits the BrushPrefab
    texture into `m_UserSplatmap` (4096², R8G8, playable area) and `m_UserSplatmapWorld` (1024²), both
    serialized with the save (`TerrainMaterialSystem : ISerializable`). The base grass/dirt/rock is
    procedural (`_SplatRockLimit`, `_SplatGrassLimit`, height variance); the four extra layers use the map's
    `colossal_TerrainExtra{1..4}Diffuse`, so their look is map dependent. Drivable from a plan exactly like
    terraform (BrushDefinition with the material tool + one Apply) → `PlannedPaint` /
    `BuildService.Paint.cs`. Whether the material TerraformingPrefabs are loaded in game mode (not only the
    editor) must be confirmed in-game: the build logs "terrain paint layers available: …" or warns
    `Placemaker.Build.PaintUnavailable`. Painting is permanent (not undone when the group is removed).

## Direct terraform and paint (fix round s4, decompiled 1.6.2f1)

Problem (playtest 4b): the build emitted up to 48 vanilla level-brush `BrushDefinition`s per frame
(plus 96 paint dabs). Each became a Temp `Brush` with an overlay preview (the purple blobs), was applied
only on an Apply frame, and was re-measured every frame. On large areas this ran at 2 FPS, the bar
stalled at 18 %, and it timed out.

Findings (`Game.Simulation.TerrainSystem`, `Game.Rendering.TerrainMaterialSystem`, `Game.Tools.ApplyBrushesSystem`):
- `TerrainSystem.ApplyBrush(TerraformingType type, Bounds2 area, Brush brush, Texture texture)` is public.
  It runs the `AdjustTerrain` compute shader straight on `m_Heightmap` (R16_UNorm, playable area) and
  `m_WorldMapEditable`. It also does every piece of bookkeeping itself: `OnAreaChanged`, `m_TerrainChange`,
  `m_LastWrite`, `UpdateMinMax`, `TriggerAsyncChange` → CPU readback, and `WaterSystem.TerrainWillChangeFromBrush`.
  The texture can be ANY `Texture`. The game maps it onto the square `brush.m_Position ± m_Size/2`
  (`_CenterSizeRotation`, angle `m_Angle`). This is the call `ApplyBrushesSystem.ApplyHeight` makes with
  `BrushPrefab.m_Texture`.
- Step size: `x = clamp(unscaledDeltaTime, 0.05, 1) · strength · toolStrength / heightScale`, with
  `toolStrength` = `TerraformingBrushData.m_ToolStrength` of `brush.m_Tool`, or else
  `GetTerrainAdjustmentSpeed` (Level/Slope 4000, Shift 2000, Soften 1000). |strength| < 0.002 is bent by `pow(·, 0.055)`.
  Shift adds `x · mask`, so it is linear and additive. Level moves toward a SINGLE target height, so it
  cannot reproduce a blended bank that has a different original height at every point.
- `RenderCascades` re-renders the cascade and calls `TriggerAsyncChange` again, so the CPU heights
  (`GetHeightData(true)`) are current a few frames after the dispatch.
- `TerrainMaterialSystem.ApplyBrush(Brush, Entity brushPrefab)` is public too. It blits
  `PrefabSystem.GetPrefab<BrushPrefab>(prefab).m_Texture` into the user splatmaps (opacity = strength,
  channel from `TerrainMaterialProperties` of `brush.m_Tool`) and executes its command buffer inside the
  call. It takes no delta time.

What the build does now (`BuildService.Terrain.cs`, `Core/Build/TerraformRaster.cs`):
1. Rasterise every terraform item into one desired-ground grid (1.75 m cells, which is half a heightmap
   pixel). Later layers and items win. A 4 m smoothstep bank outside the rings blends back to the original ground.
2. Per pass: Δ = desired − current (from the CPU readback). For each square tile of at most 1024 texels,
   build ONE raise mask and ONE lower mask (RGBAHalf, all channels equal) and call
   `ApplyBrush(Shift, …)` with strength = max|Δ| / (clamp(dt)·2000), sign by mask. Strengths are kept
   ≥ 0.0021; below that the mask is scaled down instead. A normal park takes 1–2 dispatches per pass.
3. Wait 6 frames, re-measure, and stop when the inside cells are within 0.3 m (2 % allowed off). There
   are at most 4 passes and a 6 s wall-clock cap. Progress is the drop in RMS residual and only moves forward.
4. Calibration: the texture orientation relative to world x/z is not visible in C#. The first pass
   must cut the RMS to < 0.8×. If it does not, the pass is reverted exactly (Shift is additive: same
   textures, negated strength scaled for the frame's dt) and the next mirror (flipX/flipY) is tried.
   The confirmed orientation is remembered for the session and also used for paint.
5. Fallback: if no orientation converges, or the raster cannot be built, the old level-brush dabs run.
   They now emit only for spots that are still off target.
6. Paint: one `TerrainMaterialSystem.ApplyBrush` per `PlannedPaint`. The resolved brush prefab's
   `m_Texture` is swapped for a soft-edged (3 m) coverage mask during the call and restored right after
   (in `finally`). Everything is applied in one frame. Dabs remain the fallback if this throws.
No brush entities exist on the direct path, so nothing previews, nothing has to be applied with the
tool, and Cancel has nothing to clean up except textures (destroyed on reset). Terraform and paint stay
permanent: removal never restores the ground, and every group with terraform or paint is flagged
`PermanentTerrain`.

Ground for nets/objects after terraform: `GroundHeight` = the raster's desired ground where terraform
reached its target (verified), else the live terrain. Course fitting now re-solves the inner Bézier
control heights so the curve meets the ground at t = 1/3 and 2/3, splits where it deviates by more
than 0.35 m (pieces ≥ 6 m), and fits after the max-length split. Before, the inner control points kept
the plan heights (often the pre-terraform ground) and pieces < 16 m were never split, so short paths
clipped.

Unverified in game (needs a playtest): the Shift kernel's exact linearity and which texture channel it
samples (all channels are set equal). The verify/revert loop and the dab fallback cover a mismatch.

## Removal leaving things behind (fix round s4)

Cause: tags were added only to temporaries that matched a plan item (prefab + position within 0.75 m).
Anything else was applied untagged and removal never saw it. That covers random variants, objects
moved on placement, optional ghost objects whose collision cleared between tagging and Apply, and
decals/bushes matched to nothing. Fixes:
- `TagTemporaries`: every own top-level object or area (Temp without original, no Owner, not a
  delete/cancel) is ours while our tool is active, so unmatched ones are adopted as Generic. Edges stay
  match-only, because pieces of a foreign road split by our junctions belong to the city.
- `TagLateTemporaries` on the Apply frame catches temporaries that changed after validation.
- `AdoptStrays` after Apply: untagged top-level objects inside the outline that did not exist before
  the build (snapshot at record creation) and use a prefab this build placed get adopted.
- `VerifyMembers`: tagged members per kind vs emitted objects/areas/net pieces (`BuildVerification`),
  logged at build end.
- Removal logs and releases anything still tagged after the record goes (`ReportLeftovers`).
Known gap: when the player later splits one of our edges (a road built across a path), the game
replaces the edge with untagged pieces. Propagating tags on splits is future work.
