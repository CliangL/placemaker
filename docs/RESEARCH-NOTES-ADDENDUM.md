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

## Object collisions — what makes CS2 reject a placed object (FEEDBACK-4, decompiled 1.6.2f1)

Sources: `Game.Objects.ValidationHelpers` (ObjectIterator / NetIterator / AreaIterator, ValidateObject),
`Game.Tools.ValidationSystem` (ProcessError, CancelOptional), `Game.Prefabs.ObjectInitializeSystem`
(geometry flags and bounds), `Game.Objects.ObjectUtils.GetCollisionMask`, `Game.Net.NetUtils`,
`Game.Objects.OverrideSystem`.

47. **Collision box = `ObjectGeometryData.m_Bounds`** (what the catalog stores as `AssetInfo.BoundsMin/Max`
    and dumps as `bounds`). Rotated with the object (`ObjectUtils.CalculateBounds`). Refinements:
    `Circular` → cylinder, `Standing` legs → leg cylinders/boxes + the body above the legs,
    `IgnoreBottomCollision` → box clipped at y ≥ 0. Our Declutter uses the oriented box (a superset).
48. **Plants are shrunk.** For `PlantData` prefabs the XZ bounds come from the *child-stage* meshes and are
    then reduced: `size.xz -= size.xz · min(0.5, 1 − potCoverage)` (height likewise, capped). A tree's
    collision core is ~half its young trunk/canopy width (AppleTree01 2.7 m, Alder 3.3 m) — bushes under a
    mature canopy are legal as long as the cores do not meet. The catalog bounds already are the core.
49. **Collision masks.** Every non-marker object collides `OnGround | Overground` (+`ExclusiveGround` with
    `ExclusiveGround`/`BaseCollision`, i.e. buildings/pillars). `Marker` objects never collide in game mode.
    Two objects are tested only if their masks share a bit and their 3D bounds intersect — so props on the
    ground always meet in height; only e.g. a hanging object above a bench passes on height alone.
50. **Who is overridable.** Every static prop/plant/decal that is not a building, net object, utility object
    or pillar gets `Overridable | Brushable` (trees, bushes, benches, lamps, bins, fountains, decals, hedge
    pieces). Decal-only meshes are additionally `WalkThrough` (not `Physical`) but still collide: a leaves
    decal's box is 6×1×6 m (y −0.5…0.5) and meets every ground object it covers.
51. **Object ↔ object.** Overlap → `OverlapExisting`. Severity for a new overridable temp
    (`m_CanOverride` = Overridable && !Essential): **Override** (no icon, not an Error). For a temp with
    `TempFlags.Optional` (our ghost: `CreationFlags.Optional`): `CancelOptional` cancels it
    (`TempFlags.Hidden|Cancel`) — and when the other party is an optional temp too, **both** are cancelled.
    Temps are also checked against each other (`objectList` sweep), so two overlapping planned objects
    vanish together in the ghost (playtest: 480/553 and 3200/3238 "optional objects dropped").
52. **After Apply** (`OverrideSystem`): an overridable object that collides with a non-overridable thing
    (net, building, physical area) gets `Overridden` — hidden, removed from the search tree mask, restored if
    the obstacle goes. Overridable ↔ overridable pairs are resolved by priority: one of each pair is hidden.
    So a real build "succeeds" but silently hides overlapping props — now counted as
    `Placemaker.Build.Hidden.Object` (temps carrying `Game.Tools.Override` at tag time).
53. **Object ↔ net.** Ground nets with a surface collide `OnGround|ExclusiveGround|Overground`; against an
    `Overground` object their height range is extended 10 km downwards, so the object's XZ box must stay off
    the whole net geometry (road incl. sidewalks, path width). Overridable objects → Override (→ hidden after
    Apply; cancelled in the ghost). Exempt: the net the object is attached to (`Attached.m_Parent`, its
    nodes) and nets owned by the same top-level owner. `NoSubCollisions` compositions (markers) never collide.
54. **Object ↔ area.** Only areas with `PhysicalGeometry` or `ProtectedArea` count. **Surfaces are not
    physical** → objects on surfaces are always fine; lots with `m_AllowOverlap` are not physical either.
    MapTile → `ExceedsCityLimits`. Physical overlap of an overridable object → Override.
55. **Other object errors**: `PlacementFlags.Unique` → AlreadyExists; shoreline/water rules (`InWater`,
    `NoWater`, `NotOnShoreline`) for `Standing`/shoreline objects; world bounds → ExceedsCityLimits.
56. **Placemaker consequences.** (a) `Core/Build/Declutter` runs once per plan inside
    `AssetCatalogSystem.ResolveAutomatic` (tool preview/price, ghost and build all call it): rows of
    hedge/fence pieces are re-spaced without overlap, then objects are accepted by role priority
    (centerpiece > furniture > trees > bushes/planters > decals), nudged ≤ 2 m or dropped, and drops are
    PlanIssues per role. (b) Prop-fence pieces on fence nets use `Fences.SpacedPieceCount` (no overlap) and
    run along the prefab's longer axis (vanilla hedge pieces are long in Z). (c) Whatever the game still
    rejects is counted: ghost cancellations → `Placemaker.Issue.GhostObjectsDropped` in Review; build
    Override temps → `Placemaker.Build.Hidden.Object`; missing parts → `PartsDropped`; Errors → `Skipped.*`.

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

## Doubled build and crash on bulldozing it (fix round s4b)

Log: `left out 2 rejected parts (InvalidShape, attempt 1); retrying`, then `tagged 1340`, then
`objects 1066 tagged, only 532 planned`. The user saw a double path, then got a native crash when
removing it.

- Root cause of the doubling: `Go(step)` stamped `_stepFrame` with the CURRENT frame, but steps start
  on the NEXT `OnUpdate`. So every `if (frame == _stepFrame)` first-frame branch was dead code:
  - ResetForRetry never destroyed the first attempt's definitions or requested Clear. The retry
    re-emitted on top, and Apply applied both sets.
  - Cleanup never cleared the temporaries or rolled back after Apply.
  - Paint never collected its dabs, so painting was silently skipped.
  Fix: `_stepFresh`. The first `OnUpdate` of a step sets `_stepFrame` to that frame. Before this fix
  every auto-excluded retry built doubles, which also explains earlier "removal leaves things behind"
  (the second set was partly untagged).
- Hard invariant (`BuildVerification.Duplicated`): before Apply, if tagged objects or areas exceed the
  emitted ones by more than max(4, 5 %) or max(2, 10 %), the build clears everything and re-emits
  once, then fails with `Placemaker.Build.Duplicated`. After Apply, the same check rolls the group back.
- Crash candidates hardened (no managed stack, so unconfirmed):
  1. The bulldozer stays active after the owner lot is deleted, and its hover temporaries have
     `Temp.m_Original` set to our members. Removal now waits until no temporary references a member.
     After 30 frames it switches to the default tool once.
  2. The `AreaBatchSystem` query widening for textured ghost surfaces (reflection) is now OFF by
     default (setting "Textured surfaces in preview (experimental)").
  3. Duplicate parallel networks: prevented by the invariant above.
  4. Removal skips already-Deleted entities (idempotent) and stays at ≤ 32 per pass.
- Bench rotation: CS2 benches and lamps face local +Z, and ParkManager added a quarter turn to its
  tangent yaw at emission. Our port kept the tangent yaw but dropped the turn, so path benches stood
  sideways. `ParkPlanting.PathSideYaw` now plans the facing direction (toward the path), which matches
  `PlannedObject.Yaw` (0 = facing +Z) and `quaternion.RotateY` at emission (`ObjectYawTests`). Other
  layers already used facing yaws.

## Owner lot rejections, loose road builds, bridges (fix round s4c)

- Owner lot "rejected" (groups 4–6): `Game.Areas.ValidationHelpers.AreaIterator` gives a TOP-LEVEL area an
  OverlapExisting error for any overlap with an area of the same `AreaType`. So the owner Lot collided with a
  neighbouring build's owner lot or with a planned Lot area, and the exclusion pass dropped it. The owner is
  now emitted AFTER the plan validated (`Step.OwnerLot`), and its own errors are judged separately:
  - Overlap is accepted. Apply is gated only by our tool; the Apply systems build errored temporaries anyway.
  - Shape errors (InvalidShape/SmallArea/LongDistance) retry with simpler rings (`OwnerRings`: repaired →
    simplified 3 m → convex hull → minimum-area rectangle).
  - After the last ring the temporary is cancelled (`TempFlags.Cancel`).
- No owner lot: bulldozing any object or area of the group asks for confirmation and then removes the whole
  group (`IsOwnerlessGroupPart`, bulldoze confirmation patch, `HandleDeletedMembers`).
- Loose builds: `[AreaGenerator(Loose = true)]` (roads, edgeZoning) build ordinary city content with no owner,
  record or tags. Mixed plans tag only the non-loose parts. Claims are still counted for the exactly-once and
  duplicate checks and for costs.
- Bridges sank: `CourseSplitSystem` re-derives a top-level course's heights as terrain + a linear blend of
  the end elevations, and the lake has no water yet at build time. Non-terrain nets are now ≤ 12 m pieces
  at the planned heights. Every node carries `m_Elevation` = planned − terrain, plus
  ForceElevatedNode/ForceElevatedEdge where the deck is > 0.75 m above the ground.

## Terrain texture painting (s13, decompiled 1.6.2f1)

Sources: `Game.Rendering.TerrainMaterialSystem` (ApplyBrush / ApplyToSplatmap / Serialize), `Game.Tools.TerrainToolSystem`,
`Game.Tools.ApplyBrushesSystem`, `Game.Prefabs.TerrainMaterialType` / `TerrainMaterialProperties`, the game's `Locale.cok`
(prefab names) and `Content/Game/UI/Media/Game/Icons/Terraforming/*.svg`.

57. **The vanilla "Paint texture" tool** is `TerrainToolSystem` with a `TerraformingPrefab` whose `m_Target =
    TerraformingTarget.Material` (actions "Paint Material" / "Erase Material"). Prefabs and vanilla names:

    | prefab | TerrainMaterialType | channel | vanilla name (icon) | Placemaker layer |
    |---|---|---|---|---|
    | Terrain Material Extra 1 | Extra1 (4) | x | Material Dry (TextureDry) | `TerrainMaterials.Dry` = 1 |
    | Terrain Material Extra 2 | Extra2 (5) | y | Material Lush (TextureLush) | `Lush` = 2 |
    | Terrain Material Extra 3 | Extra3 (6) | z | Material Dirt (TextureDirt) | `Dirt` = 3 |
    | Terrain Material Extra 4 | Extra4 (7) | w | Material Sand (TextureSand) | `Sand` = 4 |
    | Terrain Material Clear | None (0) | all, erase kernel | Texture Eraser | `Erase` = 0 |

    The textures come from the map's `TerrainRenderSettingsPrefab` (`m_Extra1..4Diffuse/Normal`, per-layer DirtOverride,
    BlurDepth, SplatRange, HeightOffset), so custom maps may put other textures in the slots. **There is no rock material**:
    base grass/dirt/rock are procedural (`_SplatRockLimit`, `_SplatGrassLimit`, height variance) — rocky looks come from
    slope, Dirt and props. All five tools exist in game mode (log: "terrain paint layers available: 4, 0, 1, 2, 3").
58. **Stroke application.** `TerrainMaterialSystem.ApplyBrush(Brush, Entity brushPrefab)` (public) blits
    `PrefabSystem.GetPrefab<BrushPrefab>(brushPrefab).m_Texture` into both user splatmaps: `m_UserSplatmap` 4096² R8G8 over
    the playable area (**3.5 m per texel** on a 14.336 km map) and `m_UserSplatmapWorld` 1024² over the world. Compute
    shader `PaintSplatmap`, kernels `PaintDither` (strength > 0) / `EraseDither` (Clear tool, strength forced 0), params
    `_BrushTexture`, `_BrushChannels` (the material's channel), `_BrushOpacity` = `brush.m_Strength`, `_CenterSizeRotation`
    = (centre uv, `m_Size`/max(area)/2, `m_Angle`), `_Range` = texel rect of `ToolUtils.GetBounds(brush)` (floor/ceil,
    inclusive). The command buffer executes inside the call; it also calls `TerrainSystem.OnAreaChanged`. No delta time,
    no brush entity needed. The kernel is *dithered*: per-texel weight = brush texture × opacity, written stochastically,
    then blurred by the terrain shader (BlurDepth/SplatRange) — that is the noisy feathered edge of the vanilla tool.
    The kernel source is not in C#, so whether the dither threshold is fixed per texel or random per call is unknown:
    painting the same texels twice may thicken them. Placemaker therefore paints every texel at most once per build.
59. **Strength / falloff / brush.** The vanilla tool's strength slider is `brush.m_Strength` (passed through unchanged by
    `GenerateBrushesSystem` for Material); falloff is the BrushPrefab texture (soft round brushes); size = `m_Size`; the
    tool re-applies every frame while the mouse is held (so held strokes accumulate). Any `Texture` works as the brush:
    a generated mask carries an arbitrary per-point strength (gradients, feathers, noise) in ONE call per square stamp.
60. **Saved, undo, removal.** Both splatmaps are serialized with the city (`TerrainMaterialSystem : ISerializable`,
    `FormatTags.TerrainUserSplatmap`). There is no undo in game mode; the only way back is the Texture Eraser (Clear),
    which restores the procedural base — and would also wipe the player's own earlier paint under it. So Placemaker paint
    stays permanent (warned, listed as "Paint" in the permanent-changes summary, not undone on removal).
61. **Performance (estimated, not measured in game).** One `ApplyBrush` = two compute dispatches over the stamp's texel rect (≤ 256² texels for a 512-cell, 896 m stamp
    on the 4096² map) plus our texture upload (RGBA32, ≤ 512² = 1 MB). Both should be well under a
    frame. The previous FPS stalls came from hundreds of Temp `Brush` entities per frame (old dab path), not from the
    material blit.
62. **Why our paint looked wrong before.** (a) The layer numbers were treated as arbitrary "map textures": most layers
    defaulted to 1 = *Dry grass* (pale/greyish), the beach to 2 = *Lush grass* — so "sand" and "mud" painted grass. (b) The
    purple blobs were the Temp brush overlays of the old dab path (fixed in s4). (c) Patches were two flat polygons
    (outline at half strength + core) with a fixed 3 m edge, so they read as stepped discs.

**Verdict: yes, per-point strength from code works.** Granularity = one splatmap texel (3.5 m on vanilla maps; the shader's
dither + blur smooths below that), strength 8-bit per texel. Limits: square stamps (we tile), one material per call,
the four map-defined textures only (no rock), permanent, dither semantics on repeated paint unknown (we never repaint a
texel within a build; repainting the same area in a later build may thicken partial strengths a little).

What Placemaker does now (`Core/Build/PaintRaster.cs`, `Game/Build/BuildService.Paint.cs`):
- `PlannedPaint` is a field: ring + `Strength` × edge(`Feather`, `Noise`) × gradient(`GradientFrom` polyline, `GradientFull`,
  `GradientFade`, `GradientMin`). Defaults keep the round-4 behaviour (±1.5 m edge on the ring).
- `PaintRaster.Build` (pure, worker thread) rasterises per material layer on a grid aligned to the splatmap texel (cells
  half a texel), merges paints of a layer by max, cuts square stamps ≤ 512 cells, drops empty ones, and doubles the cell
  when > 48 stamps or > 4 M cells. Order: erase first, then layers by first appearance (later wins on overlap).
- The build applies **one stamp per frame** (cursor advanced before the call: a stamp is never applied twice), waits two
  frames, frees the textures. The Paint step is entered only after terraform and never on retries; `_paintStarted`
  guards re-entry. Raster failure → the old dab path; raster > 15 s or no material tools → `Placemaker.Build.PaintUnavailable`.
- Preview: the overlay shows the paint as tinted strength bands (4 levels, material colour) from a coarse raster; the ghost
  never paints. Gallery: `DrawPaint` renders the real stamps with alpha = strength.
- Unverified in game: the stamp texture orientation (we reuse the terraform calibration, default identity: u = +x,
  v = +z — an asymmetric gradient would show on the wrong side if this is mirrored), the channel the kernel samples
  (all four set equal), and whether `m_Strength = 1` with a mask equals the vanilla look at the same strength.

## Existing buildings — why builds destroyed them (s15, decompiled 1.6.2f1)

Sources: `Game.Prefabs.BuildingInitializeSystem`, `Game.Net/Areas/Objects.ValidationHelpers`, `Game.Tools.ValidationSystem`,
`Game.Zones.CellBlockJobs` (BlockCellsJob, CleanBlockedCells), `Game.Buildings.ZoneCheckSystem`.

63. **Spawnable buildings are "delete when overridden".** `BuildingInitializeSystem` gives every building with
    `SpawnableBuildingData` (zoned houses, shops, offices, industry; not signature/service buildings)
    `GeometryFlags.Overridable | DeleteOverridden`; other buildings lose `Overridable` and get `OverrideZone`.
64. **Who removes them.** A new **net** (Net.ValidationHelpers.CheckOverlap) or a **physical area**
    (Areas.ValidationHelpers, `PhysicalGeometry`/`ProtectedArea` lots) overlapping such a building reports
    `ErrorSeverity.Warning` with `m_PermanentEntity = building`; `ValidationSystem` adds `Game.Tools.Warning` to the
    building and Apply deletes it (that is how vanilla roads demolish houses). A new **object** that is not overridable
    itself (a building-like asset) does the same; overridable props (`m_CanOverride`, i.e. trees, benches, decals)
    only get `Override` (hidden themselves). Non-spawnable buildings give an Error instead (our exclude-and-retry drops
    the piece). Surfaces are not physical: they never remove anything. Our owner lot has `m_AllowOverlap` (not physical).
65. **Condemned by zoning.** `BlockCellsJob` blocks zone cells under nets with `CompositionState.BlockZone` (ground roads
    and paths) and under physical/protected areas; `CleanBlockedCells` also blocks every cell BEHIND a blocked one (deeper
    from the road). `ZoneCheckSystem` marks a spawnable building `Condemned` (later demolished) when its lot cells are no
    longer Visible with its zone type. So a path through a building's lot (or rezoning its cells) kills it even
    without touching the model. Keeping off the whole LOT (BuildingData.m_LotSize × 8 m) avoids both mechanisms.
66. **What Placemaker does** (`Core/Build/SiteSafety`, `Game/Build/BuildService.Protect`):
    SiteCapture records lot quads (and collision footprints where they reach past the lot, and building extensions) as
    `ObstacleKind.Building`; at the end of `Composer.Compose` nets touching a lot (centre ± half width + 0.5 m) are dropped
    with their attached objects (indices remapped), objects touching one dropped (markers kept), surfaces / areas /
    terraform / paint rings cut around lots (CS2-acceptable pieces, 1 m corridors instead of holes), zoning quads touching
    one dropped; Declutter re-checks objects after nudging. Review shows "N buildings are in the way. They are kept".
    Before Apply the build queries live buildings carrying `Warning` (a second line of defence: a building grown since the
    capture, or outside its margin); the work items touching them are excluded and the build retries; if none can be
    identified the build stops (`Placemaker.Build.WouldDemolish`). Clearing trees/props skips objects on building lots.

## Zone cells under builds — why buildings grew over a plaza (s19, decompiled 1.6.2f1)

Sources: `Game.Zones.CellBlockJobs` (BlockCellsJob: NetIterator/AreaIterator, CleanBlockedCells), `Game.Zones.ZoneUtils`,
`Game.Prefabs.AreaInitializeSystem`, `Game.Areas/Objects.ValidationHelpers`, `Game.Tools.GenerateZonesSystem` (Marquee),
`Game.Tools.ValidationSystem`, `Game.Tools.ApplyAreasSystem`; Platter (lucarager/CS2-Platter, MIT) for comparison.

67. **What blocks a zone cell.** `BlockCellsJob` recomputes a block's cells whenever the block, a net, a lot or an object
    near it updates: it clears Blocked/Shared/Occupied…, then sets `CellFlags.Blocked` on every cell whose quad (shrunk
    0.02 m) intersects (a) a ground net piece whose composition has `CompositionState.BlockZone` (not tunnels; elevated
    pieces only block via their pillars), or (b) a triangle of an **area whose PREFAB `AreaGeometryData.m_Flags` has
    `PhysicalGeometry`**, or `ProtectedArea` with a `Native` component (map tiles of native land). Nothing else blocks:
    buildings *occupy* cells (CellOccupyJobs, not studied further); surfaces / spaces / districts never block.
68. **Which areas are physical.** `AreaInitializeSystem`: every prefab with `LotData` gets `PhysicalGeometry |
    PseudoRandom`, minus PhysicalGeometry when `LotPrefab.m_AllowOverlap`. Districts, map tiles, spaces, surfaces get no
    PhysicalGeometry in the prefab (`AreaUtils.SetCollisionFlags` adds it for Space/Surface only inside validation
    collision checks, never in the cell jobs). **Our owner lot ("Placemaker Area") has m_AllowOverlap → not physical →
    blocks nothing.** Plaza paving, park lawns, pedestrian/hangaround spaces → nothing. Only our ground pathway nets
    (BlockZone) blocked cells — and only the cells under them (plus everything behind, item 69).
69. **CleanBlockedCells.** Per column: row 0 becomes Blocked if row 1 is; then every cell behind (deeper than) a blocked
    cell is Blocked; `ValidArea` = the bounding rect of the unblocked columns' depths; RoadLeft/RoadRight propagate along
    blocked cells. Consequences: (a) blocking the FRONT cell of a column kills the whole column; (b) a lone zoned row 0
    with a blocked row 1 is itself blocked — cutouts must be ≥ 2 cells deep where the land behind is blocked.
70. **Zoning by Marquee** (`GenerateZonesSystem.MarqueeBlocks`): every block whose corner quad intersects the definition's
    quad; a cell is taken when it is `Visible` (so not blocked) and its CENTRE is inside the quad; without
    `ZoningFlags.Overwrite` only cells whose zone is None. Cell geometry (`ZoneUtils.GetCellPosition`): centre =
    `m_Position + m_Direction·(size.y − 2·row − 1)·4 + Right(m_Direction)·(size.x − 2·col − 1)·4`, `Right(v) = (v.y, −v.x)`,
    row 0 next to the road (`m_Direction` points at it), 8 m cells, ≤ 6 rows.
71. **Diagnosis of the playtest (plaza between three streets, buildings on the paving).** The streets' blocks reach 6 cells
    (48 m) into the block. Under the plaza nothing was physical (67/68), so every cell stayed Visible and keeps whatever zone
    it had: the zoning the player painted over the block before placing the plaza (Marquee without Overwrite never
    unzones), plus our own cutout quads. Spawnable buildings need only Visible zoned cells of a block → the brick
    apartment blocks (residential/mixed: not our low-commercial cutout zone) grew over the paving, the plaza "wrapped
    round them" because surfaces never collide with buildings (item 64). Secondary: cutout quads were not aligned with the
    road's cell grid (cells picked by centre: a cutout could gain or lose a column), and cells in front of a cutout set
    back from the road (outline drawn off the kerb) were never zoned, so those columns could not grow at all. Timing was
    not the cause (zoning runs 24 frames after Apply; Marquee only needs the blocks, not the park objects).
72. **A physical lot as zone blocker — side effects.** A physical lot takes part in validation: `Areas.ValidationHelpers`
    reports OverlapExisting against ground nets, non-overridable objects and other lots (`ObjectIterator` ignores
    overridable props/trees unless the area has `CanOverrideObjects`), errors for water, and gives an overlapped spawnable
    building (Overridable|DeleteOverridden) a **Warning** → Apply deletes it. `ApplyAreasSystem` builds errored
    temporaries (only the vanilla tools refuse to apply), `ValidationSystem` later removes the Error/Warning components
    from permanent entities again. After it exists, NEW objects/nets the player places across it get OverlapExisting
    (Objects/Net ValidationHelpers area iterators; expected from the code, not measured) — like a vanilla building lot. Removing the lot re-runs BlockCellsJob:
    the cells become Visible again **with the zone they had** (blocked cells keep `m_Zone`).

**Platter** (github.com/lucarager/CS2-Platter, `Platter/LICENSE.txt` = **MIT**, Luca Rager 2025; installed copy in the
mod cache `pdx_mods/125278_49`, Harmony + its own Burst DLL, ~11 k lines C#): a parcel is a `ParcelPrefab :
StaticObjectPrefab` (lot width × depth) owning ONE extra `Game.Zones.Block` entity (sub-block buffer `ParcelSubBlock`,
`ParcelOwner` on the block) created by `P_ParcelUpdateSystem` with `BuildOrder 0` (wins overlaps), `ValidArea` = the lot,
cells outside the lot `Blocked`, inside `Visible` with the pre-zone. Because the vanilla jobs assume road blocks, it runs a
**copy of the vanilla CellCheck pipeline** after `CellCheckSystem` (`P_NewCellCheckSystem`: NormalizeParcelCells +
CleanBlockedCells + an 845-line overlap job), re-registers vanilla SubBlock serialization, links parcels to the nearest
road edge (`P_RoadConnectionSystem`, ≤ 8.4 m front / 7.8 m sides: buildings need a road for access), has an
"allow spawn" toggle, an uninstall system, Harmony patches on the bulldozer and the object tool. Verdict: license-compatible
(MIT into GPL-3.0 with attribution), but the parcel engine is large, tied to replicating vanilla cell jobs exactly, and
depends on patches and custom serialization — too heavy and fragile to copy or depend on for "keep the park free and zone
the cutouts". Placemaker blocks cells with a vanilla mechanism (physical lots) and zones the road cells that exist
(`Core/Build/ZoneGuard`, `Game/Build/BuildService.ZoneBlock`). Own parcels would only be worth it for plots facing no
road block at all (a lane-less inner court) — not needed by any current type (`docs/proposals/s19.md`).

## Park stats (s17, decompiled 1.6.2f1) — details in docs/PARK-STATS.md

73. Park effects live on the park BUILDING prefab (Attraction, LeisureProvider, ServiceCoverage, Park, ServiceConsumption);
    AttractionSystem, ServiceCoverageSystem and ParkAISystem read the *prefab* data, so per-instance values need per-instance prefabs.
74. TourismSystem sums `AttractivenessProvider` of ANY non-temp entity (Σ a²/10000 → sigmoid): a lot can carry one.
    AttractionSystem only rewrites entities with an `UpdateFrame` (buildings), so a value on a lot stays.
75. Leisure, park coverage (wellbeing up to +20), land value from attractiveness and tourist destinations all need a
    pathfinding source/target: a Building with a road connection. A lot has none (targets = 0, silently ignored).
76. Vanilla park upkeep = price / 5 per month; attractiveness ≈ 1.9 × (area/1000 m²)^0.85 for ordinary parks.

## Quays, piers and sculpted shores (s20, decompiled 1.6.2f1)

Sources: `Game.Prefabs.Bridge` / `BridgeBuildStyle`, `NetInitializeSystem` (Bridge handling ~2312–2358), `Game.Tools.CourseSplitSystem`
(`CalculateElevation` ~3581, `LimitElevation` ~3833), `NetCompositionHelpers.GetElevationFlags`, `NetToolSystem.SnapShoreline` (~1811,
1891–1898, 6351), `Game.Net.ValidationHelpers.CheckSurface` (~1601), `TerrainSystem` (net terrain clip ~1131, raised sides ~1484),
the player's catalog dump.

77. **A quay is an ordinary net with a Bridge component in the Quay build style.** `NetInitializeSystem` gives every Bridge prefab
    StraightEdges|StraightEnds|RequireElevated|SymmetricalEdges|SmoothSlopes; for `BridgeBuildStyle.Quay` it adds `ElevatedIsRaised`,
    clears `RequireElevated`, sets `PlacementFlags.ShoreLine` and `m_MinWaterElevation = Bridge.m_ElevationOnWater`. Vanilla
    pedestrian quays (PathwayPrefab, category `net.pathway`): **QuaySmall01/02** "Narrow Quay With Pedestrian Path" (3 m, cost 0.5),
    **QuaySmallWide01/02** "Wide Quay with Pedestrian Path" (8 m, 0.75), QuaySmallWithBikeLanes01/02 (8 m, bike); road quays
    QuayMedium01 (16 m) and QuayLarge01 (24 m). "Quay01" / "RetainingWall01" are road UPGRADES (fence category, not walks).
    Leisure piers: LeisurePier01Small01 / Medium01 / Large01 (3 / 7 / 9 m, cost 1–1.75), elevated decks with pillars
    (Pier01Pillar01/02 props, 3.7/8.5 m wide).
78. **The wall side is automatic, not the drawing direction.** `CourseSplitSystem.CalculateElevation` samples the terrain at ±half
    width (x = left, y = right of the course); side elevation = deck height − terrain there. With ShoreLine a side whose water depth
    ≥ the elevation limit is forced to at least that limit; `GetElevationFlags` turns a side elevation ≥ limit into
    `CompositionFlags.Side.Raised` — the quay wall (with ElevatedIsRaised even "elevated" heights become Raised on both sides).
    So: lay the quay along the waterline with its deck above the water and the land behind it at deck height → a wall in the water,
    none on the land side. The hand tool snaps to the shoreline and sets y = water surface + max(elevation, m_MinWaterElevation);
    Placemaker places the course itself (explicit heights, FollowTerrain = false) at water level + "Deck height" (default 1.5 m,
    harbour 1.8 m, river 0.9 m) and fills the land behind to that level (sculpt).
79. **Terrain:** no quay-specific terraform; the generic net terrain clip/flatten applies, and on a Raised side the upper terrain
    bound is disabled (the ground beyond the wall is not flattened up to the deck). Raised lanes go into a separate height pass
    (probably why quay walls hold water back). **Placement:** `CheckSurface` raises InWater only for nets that are exactly OnGround
    (ShoreLine excluded) — a quay may stand over water; NoWater only applies to Floating nets; nothing requires water (on dry land it
    simply gets no raised side). No maximum wall height was found (`LimitElevation` clamps to PlaceableNetData.m_ElevationRange).
80. **Connecting:** quays are PathwayPrefab nets and join normal paths at nodes. Placemaker emits the quay as Bézier pieces
    (category `net.quay`, Core/Layers/Types/Waterfront/QuayCategories) and lets the composer's PathJoins split it where a link or a
    pier ends on it; piers start ON the quay deck (a junction), boardwalk links end on a levelled landing at the bank foot with a short
    timber stub to the boardwalk.
81. **Curation:** new net categories `net.quay` (pathway nets named *quay*, bike variants never automatic) and `net.pier`
    (pathway nets named *pier/boardwalk/jetty*, not pillars; marina packs never automatic), both falling back to `net.pathway`.
    The automatic ground walk (`AutoAssets.PickWalk`) still never picks a quay, pier or bridge.

**Sculpted terrain (s20).** Every terraform polygon used to be levelled to one height; ShoreTerrain emitted 6 m flat cells, so banks
were staircases and accesses made cones (playtests 30–34). `PlannedTerraform.Sloped` (appended) makes a patch carry its target in
the ring's vertex heights; `Core/Build/TerraformSurface` interpolates them (mean-value coordinates: exact at vertices, linear on edges,
so neighbouring quads that share corners are continuous). `TerraformRaster.Build` takes the vertex heights (and the original ground:
the 4 m bank around a sloped patch carries its CHANGE, not its absolute height, which made lips on slopes); TerrainField, SiteSafety
cuts, the overlay (one dashed outline instead of a grid) and the gallery read them. `Core/Layers/Common/Sculpt` is the node grid that
produces them; `TerrainSurvey` classifies slopes (flat < 5 %, gentle < 15 %, steep < 45 %, cliff above); `Types/Beach/ShoreGround`
is the water types' terrain step (real waterline, bank kind, sculpt, pads, benches, ramps, earthwork limits, resnap).
**Waterline:** the game's water snapshot was sampled on 16 m cells (now 8 m) and interpolated, so its zero line sat up to a cell off.
`Shoreline` now takes the robust water level (median of ground + depth where depth > 0.3 m) and, when the dry ground next to the
water lies above it, decides wet/dry by the finer terrain heights; the waterline in front of every station is found by probing and
bisection up to 45 m past the outline. Unverified in game: the raster's per-texel MVC on twisted quads, quay walls forming from
explicit-height courses exactly as with the hand tool, the boardwalk (LeisurePier) running parallel to and close over the shore.

## Decal opacity and projection (s23 decal art pass, decompiled 1.6.2f1 + ExtraAssetsImporter 1.x)

82. **No per-instance decal opacity.** A decal object is a box mesh with an HDRP decal material
    (`ManagedBatchSystem`: `DecalProperties` → render queue + `m_RendererPriority`, `TextureArea`, `MeshSize`,
    `decalLayerMask` on the material key). The only per-instance colour data, `MeshColor` → `ObjectProperty.ColorMask1..3`
    (`BatchDataSystem`), feeds `colossal_ColorMask*` of shaders that declare them; decal materials don't. Opacity is a
    material property: HDRP decal **`_DecalBlend`** (global opacity, serialised as 1.0 in every decal material in
    resources.assets) and the alpha of `_BaseColor`.
83. **Runtime see-through clones work in memory.** `SurfaceAsset` is mutable (`CopyFrom`, `UpdateFloats/Colors`); its data
    "load" is a pure ref count (`IncrementRefCount(Data)` returns true only for the first reference), so a copy made with
    `CopyFrom` and kept referenced never re-reads a stream it doesn't have when `ManagedBatchSystem` calls
    `LoadProperties`. ExtraAssetsImporter builds every pack decal exactly like this (in-memory `SurfaceAsset` registered with
    `AddAsset<SurfaceAsset>(path, guid)`, `RenderPrefab` with `geometryAsset`/`surfaceAssets`/`bounds`/counts set,
    `DecalProperties` from the decal.json). Placemaker (`Game/Build/DecalOpacitySystem`) clones StaticObjectPrefab →
    RenderPrefab → SurfaceAsset per opacity tier (20/35/50 %) with `_DecalBlend` × tier and `_BaseColor.a` × tier, sharing
    geometry and textures; AddPrefab in PrefabUpdate before PrefabInitializeSystem; used clones remembered in
    ModsData/Placemaker/decal-opacity.txt for saves. Unverified in game (setting "See-through decals" to switch off).
84. **Pack projection masks are `DecalLayers`.** EAI sets `DecalProperties.m_LayerMask = (DecalLayers)colossal_DecalLayerMask`
    (decal.json). Mask 4/36 decals (84 + 22 RealVision items, incl. "foliage" and "dirt") project onto buildings only and
    never show on the ground; the catalog now marks decal objects without the Terrain bit `WallOrHanging` → never automatic.

## Smooth shores (s30) — what makes a terrain "curtain"

85. **The heightmap is coarse.** The playable area's heightmap has ~3.5 m texels (14 336 m / 4096); terrain is a mesh through
    them. A height jump of more than ~0.6 m between two texels (steeper than ~50 %) renders as a stretched vertical sheet.
    Terraform patches that meet at different heights (a flat `Level` patch inside a slope, a field cut off abruptly, the build
    raster's bank extrapolating the last patch) all produce one. Metric: `Core/Build/TerrainMetrics.Curtains`.
86. **Nets flatten their own width.** A path on a cross slope, or a deck at a different height from the ground beside it,
    leaves a lip/wall along the net's edge; the game draws a concrete retaining wall where a net is cut into higher ground
    (screenshot 56). Every walk must sit on ground level across its width plus ~1.5 m shoulders (`TerrainMetrics.Seats`), or be
    an explicit-height net whose cut is the intended wall (s30 "Hillside: retaining wall").
87. **Quay/bridge prefabs are built straight** (`StraightEdges`): a curved Bézier piece came out as a straight wall at a
    different angle. Emit quays and piers as short straight chords sharing their nodes along a smooth curve.
88. **In-game check:** setting "Debug: dump terrain after build" writes the live heights/water/nets to
    ModsData/Placemaker/debug; `Tests/Placemaker.Gallery --terrain-dump` renders them (docs/proposals/s30.md).
