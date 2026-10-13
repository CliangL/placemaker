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
