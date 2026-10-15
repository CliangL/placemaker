# Teardown 03: why our nets fail with `InvalidShape`, and what applying them anyway does

Scope: lot nets of parking lots and lot-first commercial types (`Invisible Road Path - 2xTwoway`,
`Invisible Car Path - 1xTwoway`, `Alley`, `Invisible Pedestrian Path`, connectors). Game version 1.6.2f1 (decompiled at
`%TEMP%\pm-decomp\game`). Repo state: master `650be0f`. Evidence log: `.forensics/Placemaker-20261015-1152.log` (build `4d62ea8`).

Labels used below: **[proven]** means read in the game or repo source, or measured by running code.
**[hypothesis]** means plausible, but not checked against an in-game position.

---

## 0. Short answer

1. **[proven]** There are only two sources of net `InvalidShape` in the game
   (`Game.Net/ValidationHelpers.cs` `ValidateEdge`):
   - (a) the edge's left/right outline, or a node piece of it, **folds back on itself** (`CheckSegmentShape`);
   - (b) a `FixedNodeSize` node whose edge tangent is misaligned.

   We only hit (a). The cause is always the same: **an edge is shorter than the room its two junctions take out of
   it**, or a curve is tighter than its half width. Length rules (`ShortDistance`) never fire for our road-type nets,
   because their `m_EdgeLengthRange.min` is 0. So `InvalidShape` is the game's only "too short" signal.
2. **[proven, offline]** Our planner produces such edges on ordinary sites. I ran the real planners over
   9 types × presets × 6 sites (306 plans, 4 369 nets) and applied the game's cut-back rule.
   - 257 pieces fold with bare widths alone, and 386 once the default 0.5 m node offset is added.
   - 32 of 36 strip-mall plans contain at least one such piece.
   - The worst offenders are `Invisible Road Path - 2xTwoway` (192), `Alley` (70), `Invisible Car Path - 1xTwoway` (68)
     and `Invisible Pedestrian Path` (52). These are exactly the prefabs in today's log.
3. **[proven, offline]** Since `082afac` (10‑13 21:10), the build step itself makes more such pieces. It splits lot nets
   wherever the ground deviates 0.35 m from the course, which creates short pieces next to junctions.
   - With the residual ground roughness the terraform log reports (0.43–0.70 m worst), this adds 23–102 folding pieces
     per 24 plans.
   - With the old settings it added 0–19.
   - This happens after the plan, so the plan tests never see it.
4. **[proven]** Applying anyway (s59 strips `Error` before Apply) does not crash and does not create zero-length edges.
   - The game applies errored temporaries whenever the tool allows it. The debug "ignore errors" switch does the same.
   - **But the folded geometry is permanent.** `GeometrySystem` recomputes the same fold every time the edge updates.
   - The fold feeds rendering (visible on `Alley`), terrain flattening/clipping, zone blocks (Alley carries zoning),
     node lanes and raycasts.
   - **Anarchy is crash-safe for nets, not result-safe.** The planner must produce valid geometry. Anarchy can only be
     a last-resort net, and every net it keeps despite `InvalidShape` is a planner bug.
5. Concrete rules with numbers are in §6. An offline verification path that runs the game's own math is in §7. I checked
   that the game's `NetUtils` and `Colossal.Mathematics` run headless on .NET 9; the ECS world does not.

---

## 1. Evidence from today's session

| log line | build | net InvalidShape | other |
|---|---|---|---|
| 1992–2004 | 11:45 (park/plaza) | `Pavement Path` ×1 | decal overlaps |
| 2466–2486 | 11:46 (1 layer, stall markings → parking lot type) | `Invisible Road Path - 2xTwoway` ×8, `Alley` ×3, `Invisible Car Path - 1xTwoway` ×2 (13 errors, "4 markers") | 6 retries, then `build Failed: InvalidShape` |
| 2683–2761 | same lot rebuilt 3× | identical 13 | identical failure |
| 3010–3029 | 11:49 parking lot | `Invisible Road Path - 2xTwoway` ×15 | `ghost 53: 349/349 parts, 219 rejected`, failure |

- Every retry line says `nets 0` left out. The old loop never excluded nets, so a single net `InvalidShape` always ended
  the build (pre-s59 behaviour at `4d62ea8`).
- The terraform lines right before these builds report residual errors of `worst 0.49 m` (2464) and `worst 0.43 m`
  (3007). That matters for §3.3 C.
- The log has no positions for the net errors. Only the count of "markers" is printed. §7 asks for that to be logged.

---

## 2. Every game condition that yields `InvalidShape` for a net

### 2.1 Where it is raised **[proven]**

`Game.Net/ValidationHelpers.cs`, `ValidateEdge` (line 1126). It is the only net source. The other `ErrorType.InvalidShape`
hits are in `Game.Areas/ValidationHelpers.cs`, for areas and surfaces.

**(a) Folded outline, lines 1360–1391.** The check runs only if the temp is not `TempFlags.Delete`.
- `CheckGeometryShape(edgeGeometry)` runs unless *both* end nodes are "internal".
- `CheckGeometryShape(startNodeGeometry.m_Geometry)` runs unless the start node is internal; the same holds for the end.
- `IsInternal` (1574–1599) is true only when the edge has an owner whose top-level owner is a **Building**, and every edge
  at that node has that same top owner. Vanilla building sub-nets (for example parking-lot assets with internal invisible
  paths) are exempt this way. Our nets have no owner at validation time, so they are always checked.
- The edge test runs only if `any(m_Start.m_Length + m_End.m_Length > 0.1)` (1819). The node-piece test runs only if a
  left or right length is greater than 0.05 m (1828).
- `CheckSegmentShape` (1835–1862) walks each segment (left/right Bezier pair) in 8 steps. It reports a fold when a
  sub-quad's left side runs backwards relative to the cross-section, `cross(d−a, b−a).y < 0`, or its right side does,
  `cross(c−b, c−d).y < 0`. In plain words: **the outline curve moves backwards somewhere**.

**(b) Fixed-size nodes, lines 1507–1534.** If the start or end node composition has `CompositionFlags.General.FixedNodeSize`,
the result is `InvalidShape` when:
- `|dot(edge tangent, node forward)| < 0.99999`, which is a misalignment of about 0.26°, or
- the node is an `Intersection`.

This applies to special prefabs with fixed node objects. I found nothing suggesting our lot prefabs use it. It is listed
for completeness.

### 2.2 Related checks that are *not* `InvalidShape` **[proven]**

These come from the same function, ValidationHelpers 1392–1558.

| check | rule | our nets |
|---|---|---|
| `ShortDistance` | `left+right length < m_EdgeLengthRange.min` (×0.1 for `HalfLength`) | `min` is never set for roads (only fixed segments and tracks set it, NetInitializeSystem 2321) → **0, never fires** |
| `LongDistance` | `> max × 1.1`; default max 200 (NetInitializeSystem 875) → 220 m | lot nets are short |
| `SteepSlope` | end slope ≥ 2 × max steepness, or overall ≥ max + 0.0005 | separate error |
| `TightCurve` | track lanes only (`ValidateLane`) | n/a |
| `OverlapExisting` | edge vs edge without a shared node (the `edgeList` Temp-vs-Temp loop 1267–1359, the net search tree 1208) | **crossings of two planned nets** land here |
| `InWater` / `NoWater` | `CheckSurface` | n/a |

### 2.3 How geometry gets folded: `Game.Net/GeometrySystem.cs` **[proven by reading; not ported numerically]**

Each edge end is cut back at its node by an "offset" computed in `CalculateCornerOffset` (824–1170):

- **Geometric part.** It finds where this edge's left/right outline meets each neighbour's outline. The neighbour is
  **widened** first:
  - the node composition width grows by `2·num3`, where `num3 = max(edge, node m_NodeOffset) + |slope|·nodeWidth·0.25`
    (lines 864–866 and 963–967);
  - `m_WidthOffset` is added as well.

  A rounded node cap (`nodeCurve`, 1001–1032) is intersected too. For straight nets this is in practice
  `(h_j' + h_i·cos φ) / sin φ`, where φ is the angle between the two rays leaving the node and `h_j' = h_j + num3`.
  That is the formula `NetFit.Cutback` uses, *without* the widening.
- **Floors (`float9`).** These are minimum cut-backs, applied even to straight continuations:
  - `num2` (the node offset) when the neighbour is a **different prefab** (950–953);
  - `|slope_i − slope_j|·nodeWidth` when it exceeds 0.2 m (941, 946–949);
  - a lane/width mismatch term `x` (930–939);
  - **crosswalk depth** when the node composition has crosswalks (`CheckCrosswalks`, 1301–1310, used at 932–939);
  - `m_MinNodeOffset` when the junction is not straight (dot < 0.9995, about 1.8°) or the layers differ, e.g. road meets
    pathway (954–958, 969–975, 1084–1088).
- **Cap.** At line 361, `num2 = min(1.98, 2 − 0.4 / max(1, length))`, in parameter space over the two edge halves. The
  start and end offsets are clamped, and if their sum exceeds the cap they are **scaled down proportionally**
  (362–374). So an edge always keeps about 0.2 m (or 1 %) of its centre line. **The game never makes a zero-length or
  negative edge.**
- **What folds.** When the sum is scaled down, the edge ends stop short of where the neighbours' outlines cross. The node
  pieces (`CalculateNodeGeometryJob`, 1839 ff.) still join this end to the neighbours' ends, so the outline has to run
  backwards. `CheckSegmentShape` flags that.
- **Tight curves fold the edge itself.** The inner outline is `NetUtils.OffsetCurveLeftSmooth(curve, ±w/2)` (527–530).
  I called the game's own function headless (§7): a quarter turn of about 3 m radius offset by 3.5 m gives an inner curve
  that runs backwards (`c.z = −0.5` after starting at `z = 0`). Our lot nets are straight chords, so this applies only to
  curved roads and paths.

**Summary rule** (exact up to the node offset, which comes from prefab data):

> **len(edge) ≥ cut(start) + cut(end) + 0.2 m**, with
> `cut = max over neighbours j of (h_j + n + |s|·w/4 + crosswalk_j + h_i·cos φ_ij) / sin φ_ij`
> and floors of `n` (different prefab), `|Δs|·w` (if > 0.2) and `m_MinNodeOffset` (non-straight).
> `n = m_NodeOffset` comes from the composition's pieces. `NetPiecePrefab.m_NodeOffset` defaults to 0.5 m
> (`Game.Prefabs/NetPiecePrefab.cs:22`). The real value per prefab is asset data and must be dumped once (§7).

### 2.4 How nodes are made **[proven]**

- **Node merging.** `Game.Tools/GenerateNodesSystem.cs` 126–163 (`UpdateData.Equals`, `NodeKey.Equals`): two course
  ends become one node only when `m_Original` matches, or when **`float3` positions are bit-identical, y included**.
  Nothing merges by distance.
- **Crossings.** `Game.Tools/CourseSplitSystem.cs` intersects each new course only with edges already in the net search
  tree (`FindOverlapsJob` 248–281, `m_SearchTree = GetNetSearchTree` 4101), i.e. existing city nets. **Two planned
  courses that cross never get a junction.** They overlap (`OverlapExisting`) and are not connected.

---

## 3. Our geometry: where it breaks these rules

### 3.1 The pipeline (parking planner; the lot-first commercial types use the same `PlanBuilder` for their lots)

1. `LayoutEngine` produces lines (ring, aisles, cross lanes, footpaths).
2. `PlanBuilder.Emit` (`Core/Layers/Parking/PlanBuilder.cs` 452–560) turns lines into segments: aisles/ring/cross as
   `DriveAisle` with `ParkingAssets.DrivePath(width)` (463), footpaths as `Invisible Pedestrian Path` (466), entrance
   connectors (535–537) and alley stubs (`AlleyStub`, 368–396, used at 500–513).
3. `SplitAtJunctions` (671–718):
   - welds lot-side ends closer than **0.75 m**;
   - splits every segment at other endpoints lying on it;
   - drops pieces < 0.5 m.
4. Each piece becomes a straight `PlannedNet` (`B`/`C` at 1/3 and 2/3).
5. At build time, `BuildService.EmitNets` (`Game/Build/BuildService.Emit.cs` 117–189) runs `CourseFitting.Fit`:
   - pins the ends to the live terrain;
   - **splits** where the course deviates > 0.35 m from the ground, for pieces ≥ 6 m, up to depth 10
     (`Core/Build/CourseFitting.cs` 89–91, 120–138);
   - emits one `CreationDefinition`/`NetCourse` per piece, and **drops pieces < 1 m** (`MinCourseLength`, line 98;
     Emit 148).

The commercial lane networks (`Mall.cs`, `StripMallLayer.cs`, `OutdoorMallLayer.cs`) plan `Alley` / `Pedestrian Street`
lanes with T/X junctions and "short alley stubs sharing a node with the lane" (StripMallLayer.cs:10, Mall.cs:8).

### 3.2 Measured on real plans **[proven, offline]**

Throwaway probe, not committed: `%TEMP%\pm-np\NetProbe.cs`. It compiles the repo's `Core/**` and test helpers read-only.

- **Setup.** 9 types (parking + 8 commercial), default plus every preset, on 6 sites with streets around (rect 115²,
  70×50, 160×90, rotated 100×80, L 120, irregular quad).
- **Nets counted.** Every planned net except fences.
- **Model.** Junctions are exact shared end points. Cut-back is `(h_j + h_i cos φ)/sin φ`, with half widths from
  `NetFit.Half`.
- **Fold criterion.** `len < cutS + cutE + 0.2 m − 0.05`, the game's cap from §2.3.

| type | plans | plans with a fold (bare) | pieces | plans with a fold (+0.5 m node offset) | pieces |
|---|---|---|---|---|---|
| parking | 30 | 4 | 8 | 5 | 10 |
| stripMall | 36 | **32** | **156** | **32** | **198** |
| shoppingPlaza | 36 | 6 | 7 | 11 | 18 |
| outdoorMall | 36 | 10 | 21 | 15 | 43 |
| mallPark | 30 | 10 | 22 | 10 | 22 |
| mallDistrict | 36 | 0 | 0 | 6 | 18 |
| retailPark | 30 | 8 | 12 | 11 | 24 |
| highStreet | 36 | 3 | 13 | 9 | 19 |
| shopsPark | 36 | 7 | 18 | 12 | 34 |
| **total** | **306** | **80** | **257** | **111** | **386** |

Folding pieces by prefab (+0.5 m model / all pieces of that prefab):

| prefab | folding / all |
|---|---|
| `Invisible Road Path - 2xTwoway` | 192 / 2608 |
| `Alley` | 70 / 690 |
| `Invisible Car Path - 1xTwoway` | 68 / 587 |
| `Invisible Pedestrian Path` | 52 / 136 |
| `Pedestrian Street (Small)` | 0 / 212 |
| `Small Road` | 0 / 23 |

Among the `2xTwoway` aisle folds, 159 are pieces **shorter than 4 m**.

The probe also found:
- 6 crossings without a node (parking, L‑shaped site);
- 6 near-duplicate node pairs (≤ 1 m apart, not merged);
- 1 collinear overlap at a node (angle 0°);
- 1 planned piece < 1 m that the build drops.

### 3.3 The violation classes, with examples from the probe

**A. Short pieces between two junctions.** `SplitAtJunctions` splits at every T and keeps any piece ≥ 0.5 m.
- `parking/default/L120`: `2xTwoway` (43.6,109.6)→(42.0,109.6), **1.60 m** between two 90° T's. Needs 5.0 m bare,
  6.0 m with node offset. This is a ring "jog" at the concave corner.
- Same site: a 5.75 m ring piece needs 7–8 m.
- `stripMall/default/rot23_100x80`: `Invisible Car Path - 1xTwoway`, **5.0 m** between two T's on 7 m aisles; needs
  7.0 / 8.0 m. The same class shows up in parking/compact, retailPark and others.
- `stripMall/rearAlley/rect115`: `2xTwoway` pieces of **1.62 m** and 4.75 m between footpath and alley T's. This is the
  case `docs/proposals/s47.md` "Not done" recorded on 10‑14 ("1.6 m pieces next to footpath T's; not a reported failure,
  left alone").
- `stripMall/default/rect160x90`: a **0.75 m** `2xTwoway` piece between two T's. Two ends exactly 0.75 m apart escape
  the 0.75 m weld (`< 0.75`) and become a 0.75 m edge. The build then **drops** it (< 1 m), which leaves a gap.

**B. Stubs at exactly the limit, with zero margin.**
- `AlleyStub` accepts the first sample `x = 0` when the stub is straight: `stubNeed = roadHalf + turn(0) + margin(0)`
  (PlanBuilder 376–379). A lot edge on the kerb then gives a stub **exactly** the road's half width.
- In the commercial types the probe finds many `Alley` stubs of **4.00 m** with a T on an 8 m `Alley` lane at one end
  and a straight continuation into another prefab at the other end. Examples: `stripMall/default/rect115`
  (57.5,46.6)→(57.5,42.6), and 3 per plan.
- The game then adds more cut-back than the 4.0 m the stub has:
  - +n at the T (widened neighbour);
  - +n at the continuation (different prefab, `float9 ≥ num2`);
  - the width-transition term (8 m → 7 m);
  - the **crosswalk depth** at a T on a street.
- `MallNetGeometryTests` (and `NetFit.Violations`) passes these. It uses bare widths, counts continuations as 0
  (`cos < −0.9659` → 0), and allows `len < need − 0.05`.
- **PLT avoided the crosswalk part:** every alley entrance gets `Upgraded { RemoveCrosswalk }`
  (`ParkingLotTool/Tools/ParkingLotNetCourse.cs`, the `entrance-gasse` branch). Placemaker plans no upgrades on alley
  stubs (`PlanBuilder` 503; `grep RemoveCrosswalk` finds nothing in Core/Game).

**C. The build step creates new short pieces next to junctions.** This is invisible to every Core test and is a
regression since `082afac`.
- `082afac` (10‑13 21:10) changed the terrain fit from deviation 0.6 m / min split 16 m / depth 8 to
  **0.35 m / 6 m / 10**.
- `FitRecursive` splits at the worst of 23 samples (`t = k/24`), so a split piece can be as short as 6/24 = 0.25 m. It
  can sit right next to a T and has no length guard.
- I simulated `EmitNets` with `CourseFitting.Fit` over the plans' nets on smooth residual bumps. This assumes a cubic
  of the planned chord on synthetic terrain; parking + stripMall + retailPark + shoppingPlaza; 24 plans; 492 nets.

| residual bump amplitude | current settings: nets split / new pieces shorter than the T cut-back at their end | pre-`082afac` settings |
|---|---|---|
| 0.15 m | 0 / 0 | 0 / 0 |
| 0.30 m | 81 / **23** | 0 / 0 |
| 0.50 m | 198 / **102** | 73 / 19 |

- Today's terraform lines report a worst residual of 0.43–0.70 m (log 2464, 3007; and 11:51 "worst 0.70 m"). The
  s25/s29 lot grading into **level tiers with ramps** also makes real slope breaks, so splits are expected in game.
- Each split node is also a vertical kink: `|Δslope|·w > 0.2` adds a cut-back there (§2.3). For a 7 m net that is any
  grade change > 2.9 %.

**D. A width change at a slight kink.**
- `NetFit.Cutback` treats anything ≥ 165° as a free continuation.
- The game formula at 170° from a 7 m path into an 8 m alley is `(4 + 3.5·cos170°)/sin170° ≈ 3.2 m`.
- A small kink between different widths is expensive. Only an exact straight join (≤ 1.8°) or a clear angle is cheap.

**E. Crossings without a node** (parking L120, 3 per plan).
- A cross lane (`Car Path`) crosses the ring (`2xTwoway`) mid-span. The ring side has a jog, so the cross lane end does
  not lie on it.
- The game does not intersect new courses with each other (§2.4). This gives `OverlapExisting`, two unconnected nets,
  and under anarchy, traffic that cannot turn there.

**F. Collinear overlap at a node** (`parking/green/L120`): two aisle pieces leave one node in the same direction
(angle 0°). The cut-back is unbounded (the model reports 4 370 m); the result is a guaranteed fold plus overlap.

**G. Pieces < 1 m dropped at the build** (`MinCourseLength`, Emit 148): the network gets a gap with two dead ends
0.5–1 m apart.

**H. City streets** **[hypothesis for the in-game share]**
- Alley stubs attach to the city street with `m_Entity` + split position (Emit 197–233), which splits the city edge.
  If the attach point is closer than the T's cut-back to an existing node of that street, the **city's** split piece
  folds.
- Under anarchy, `BuildVerdicts.Decide` keeps flagged temporaries that are not ours without a word
  (`Core/Build/BuildVerdicts.cs` 99–103), and `NeutralizeVerdicts` strips their `Error`. A modified city road can be
  applied folded. Nothing is demolished, but the city road is damaged.

### 3.4 Why the tests did not catch it **[proven]**

- `MallNetGeometryTests.NoPieceIsTooShortForItsNodes` checks only roles `Alley`, `Street`, `DriveConnector`
  (MallNetGeometryTests.cs 83; NetFit.cs 69). **`DriveAisle` (aisles, ring, cross lanes) and footpaths are never
  checked.** That is 192 + 68 + 52 of the 386 folds.
- It runs only on one 115 m square (line 101), with bare widths, 0.05 m slack, no 0.2 m remainder, no node offset, no
  crosswalk, no slope, and continuations counted as free.
- No test runs `CourseFitting` splits against the plan's junctions (class C), or checks for crossings without a node
  (E), collinear overlaps (F) or < 1 m pieces (G).
- `ParkingKit.Check` and the parking tests check stalls and surfaces, not node fit.

### 3.5 Mapping to today's failures

These are **[hypothesis]** because the log has no per-net positions.

- **11:46 lot**: `Alley` ×3 / `2xTwoway` ×8 / `Car Path` ×2.
  - Three alley entrances fit class B (stub = road half width; crosswalk not removed; prefab change at the stub's inner
    end).
  - Aisle and cross-lane pieces fit classes A and C. Terraform residual 0.49 m; the lot is tiered.
- **11:49 lot**: `2xTwoway` ×15, residual 0.43 m.
  - Mostly class C (build-time splits beside T's) and class A on the outline the user drew.
  - The same type built cleanly at 11:51 on another outline (group 5: 14 edges, no InvalidShape). That fits a site- and
    terrain-dependent cause rather than a systematic one.

---

## 4. What force-applying an `InvalidShape` net does (s59)

**[proven]**

- **The gate is only in the tool.** `ToolBaseSystem.GetAllowApply` (533–540) refuses Apply while an `Error` exists,
  unless `m_ToolSystem.ignoreErrors` is set. That is a vanilla debug switch (`DebugSystem.cs` 3271–3274;
  `ToolSystem.cs` 159). `ApplyNetSystem` does not look at `Error`. Applying errored nets is a code path the game itself
  ships. The Anarchy mod disables the same error type (2 = `InvalidShape`; `docs/proposals/s59.md`).
- **No zero-length or broken edges.** The cap at GeometrySystem 361–374 always leaves about 0.2 m of centre line;
  `CutCurve` (641–675) keeps at least 0.02 in parameter. The edge, its nodes and its lanes are created normally.
- **The fold is permanent.** The permanent edge gets the same `GeometrySystem` pass, with the same inputs and the same
  folded outline, on every update. It changes only when the topology changes (a neighbour removed, an edge
  lengthened). Permanent entities are not re-validated, so there is no lingering error icon. The broken shape is just
  there.
- No exception mentions nets in `Player-20261015.log`. Nothing was force-applied in that session, though, so that is not
  proof.

What uses the folded geometry. Effects come from reading the consumers; their size in game was not measured.

| consumer | file | effect of a fold |
|---|---|---|
| mesh / batches | `Game.Rendering/BatchDataSystem`, `BatchInstanceSystem` | twisted, inside-out asphalt and z-fighting on **visible** nets (`Alley`, streets, `Pavement Path`). Invisible paths have no mesh, so no visual |
| terrain | `Game.Simulation/TerrainSystem`; roads get `FlattenTerrain \| ClipTerrain \| BlockZone` (`NetInitializeSystem.cs` 1951) | flatten/clip polygons built from a self-crossing outline: dents or spikes under the node |
| zoning | `Game.Zones/BlockSystem.cs` 229–245, `CellBlockJobs.cs` 103–127 | zone blocks and blocked cells come from edge and node outlines. A folded `Alley` (it carries zone blocks) gives wrong or degenerate blocks. **This may be linked to "preview showed shops, none grew"** (see 04-shops-zoning) |
| lanes | `Game.Net/LaneSystem.cs` 2766, 4904, 6979–6997, 8028 | node lanes are placed from node outlines: kinked or looping car and pedestrian lanes; connectivity is likely kept |
| objects on nets, raycast | `Game.Objects/SubObjectSystem`, `Game.Net/RaycastJobs` | lamps and props of the net misplaced; selection/hover odd |

**Verdict.**
- Anarchy for nets is safe from crashes and from losing a building, which s59's building guard handles.
- It does **not** give the user a correct result, and the preview shows the same broken net, because the ghost goes
  through the same emit.
- "Preview equals result" holds, but both are wrong.
- The planner has to satisfy §6, with anarchy kept only as a safety net.
- Any kept net `InvalidShape` should be logged as a defect with its position and its planned piece, and counted in the
  audit, not hidden in "kept despite collisions".
- Making nets sub-nets of an owner **Building** would skip the check (`IsInternal`, §2.1). It would not unfold anything.
  PLT also documents that putting an owner on the *CreationDefinition* sent heights 183 m off
  (ParkingLotNetCourse.cs comment at the `FreeHeight` note).

---

## 5. How ParkingLotTool avoided this

From `C:\Users\jack\ParkingLotTool`:

- **One straight course per planned line, no terrain splitting.**
  - `CreateCourseDefinition` (`Tools/ParkingLotNetCourse.cs`) emits `NetUtils.StraightCurve(a, b)` once.
  - It rejects < 1 m ("a rounding remainder; CS2 would make a node without an edge").
  - It never inserts nodes along an aisle, so class C cannot happen.
- **Identical node heights.** `SampleCourseHeight` caches one height per 2.5 cm grid point, so every course end at a
  corner gets the same y. GenerateNodesSystem merges only bit-equal positions.
- **Layouts never make stubs.**
  - End sections under two bays are never chosen (`Geometry/Zellen/Planung.cs` 611–615, "the stub").
  - A cross-lane piece is dropped where a section carries fewer than five bays.
  - Aisle ends are aligned when that costs no bay (`Ringlos.PlaneGemeinsameEnden`, 884–948; measured offsets of
    0.01–0.43 m removed). That removes the "jog" pieces of class A.
- **Alley entrances** get `RemoveCrosswalk` on both sides.
- **Footpaths** are a LocalConnect clone with search mask 0 (`docs/proposals/parking.md` 97–98). Inner paths are not
  T-split into the aisles in the same way.

PLT never ported the cut-back rule. It stayed valid because it made no short pieces, no extra nodes and no crosswalk
nodes. Placemaker added lane networks with T/X junctions, alley stubs, footpath T's and terrain splits, without a
matching geometric guard.

---

## 6. Rules the planner (and emitter) must satisfy

Notation: `h` = half width, `n` = node offset (0.5 m default until dumped), φ = angle between the two rays leaving a
node, `cw` = crosswalk depth at that node.

| # | rule | numbers |
|---|---|---|
| R1 | **Length fits the junctions.** For every emitted edge: `len ≥ cut(start) + cut(end) + 0.2 m + 0.5 m safety`, with `cut = max_j (h_j + n + cw_j + h_i·cos φ)/sin φ` (0 for an exact straight join of equal width and the same prefab), floored by `n` when the neighbour is another prefab | see the table below |
| R2 | **Angles.** T/X junctions 60°–90°; never < 45° for 7 m nets. Straight joins exactly straight (≤ 1.8°, dot ≥ 0.9995), never 165°–178° between different widths | at 45° a 7 m aisle needs 9.2 m per end; at 30°, 14.1 m |
| R3 | **Every crossing is a shared node.** Split both nets at the crossing point, with one identical point. No crossing mid-span, no collinear overlap, no two edges leaving one node in the same direction | the game will not join two new courses itself (§2.4) |
| R4 | **Node spacing.** Distinct nodes on one net are at least the R1 length apart. Weld radius ≥ the minimum legal piece, not 0.75 m. Near-duplicate nodes are merged or moved apart, never left as a 0.5–1 m edge | |
| R5 | **No piece < 1 m reaches the game.** Resolve at plan time: merge into a neighbour, or move the junction. Never drop at emit (that leaves a gap) | `MinCourseLength` = 1 m |
| R6 | **Emit must not add nodes beside junctions.** Lot nets on graded lots: no terrain splitting (fit the profile only), or splits ≥ `cut + 2 m` from any junction end with pieces ≥ 6 m. Restore 0.6 m / 16 m at least for lot nets | §3.3 C: 23–102 vs 0–19 new folds |
| R7 | **Bit-identical node positions, y included.** One height sample per 2D node, cached (PLT), used by every course that ends there | GenerateNodesSystem 126–163 |
| R8 | **Alley and street entrances.** Add `RemoveCrosswalk` on the stub (PLT). Stub length ≥ `h_road + n + (cw if not removed) + transition`; for a 12 m street that is ≥ 7.5 m with 0.5 m safety, not 6.0 m. A stub that cannot fit becomes a kerb dead end (LocalConnect), as s47 already does when a stall is in the way | |
| R9 | **Curves.** Centre-line radius ≥ `h + n + 1 m` on every curved net | the inner outline reverses below `h` (§2.3) |
| R10 | **Attaching to city nets.** The attach point must be ≥ `cut` from every existing node of that street on both sides, so both split pieces of the city edge also satisfy R1. Prefer an existing node when one is within that distance | protects city roads from folds under anarchy |
| R11 | **Grade at nodes.** `|Δgrade| · w ≤ 0.2 m` at every node, or include it in `cut` | 7 m nets: Δgrade ≤ 2.8 %; 8 m alley: ≤ 2.5 % |

Minimum straight piece lengths at 90° junctions from R1 (n = 0.5, cw = 0, without the 0.5 m safety):

| piece (h_i) | between T's on | min length |
|---|---|---|
| `2xTwoway` aisle (3.5) | `2xTwoway` (3.5) both ends | 8.2 m |
| `Car Path 1x` cross lane (1.5) | `2xTwoway` both ends | 8.2 m |
| `2xTwoway` (3.5) | `Car Path 1x` T's (1.5) both ends | 4.2 m |
| `2xTwoway` (3.5) | footpath T's (1.0) both ends | 3.2 m |
| `Alley` stub (4.0) | T on `Alley` lane (4.0) + straight join into `2xTwoway` | 4.5 + 0.5 + transition ≈ 5.2 m (today: **4.0 m**) |
| `Alley` stub (4.0) | T on a 12 m street + join into `2xTwoway` | ≈ 7.2 m + crosswalk (today: **6.0 m**) |

---

## 7. Verifying this offline, without launching the game

**[proven]** What runs headless:
- I built a net9 console that references `Game.dll`, `Colossal.Mathematics.dll` and `Unity.Mathematics.dll` from
  `Cities2_Data\Managed` (`%TEMP%\pm-gc`).
- These ran correctly: `Game.Net.NetUtils.OffsetCurveLeftSmooth`, `NetUtils.FitCurve`, `NetUtils.FindMiddleTangentPos`,
  `Colossal.Mathematics.MathUtils.Length`.
- `new Unity.Entities.World(...)` and `new NativeArray<int>(…)` both fail with
  `SecurityException: ECall methods must be packaged into a system module`. ECS needs Unity's native engine.

So the job code cannot be called as-is. The plan:

1. **A faithful port with the real primitives.** Copy these decompiled bodies into a test-only class, replacing
   `ComponentLookup`/`EdgeIterator` with plain arrays:
   - `GeometrySystem.CalculateEdgeGeometryJob`: `CalculateOffsets`, `CalculateCornerOffset`, `CalculateCutOffset`,
     `Cut`, `ConformLengths`;
   - `CalculateNodeGeometryJob`;
   - `ValidationHelpers.CheckSegmentShape` / `CheckGeometryShape`.

   Call the **game's own** `NetUtils`/`MathUtils` from `Game.dll` for every curve operation. That keeps the porting
   surface to control flow only. Input is our plan's nodes and edges; output is the exact `InvalidShape` verdict and the
   position per edge.
2. **Prefab data, once, from a session the user runs anyway.** At load, extend `catalog-dump.json` with, per net prefab
   we use:
   - `NetGeometryData` (`m_DefaultWidth`, `m_MinNodeOffset`, `m_EdgeLengthRange`, layers, flags);
   - the edge, node and intersection `NetCompositionData` (`m_Width`, `m_NodeOffset`, `m_WidthOffset`, `m_MiddleOffset`,
     `m_Flags`, sync offsets);
   - `NetCompositionCrosswalk` depths.

   Read it from compositions the city already has. Commit the dump as a test fixture.
3. **Differential calibration from passive in-game data.**
   - Log every net error the ghost and the build see: prefab, `m_Position` from `ErrorData`, and the planned piece key.
     Today only a count is logged. A preview-only self-check changes nothing in the city.
   - Replay the same plan offline and require the port to flag the same pieces.
4. **Gates in the test suite.**
   - Run the port over every type × preset × the gallery sites, *after* emulating `CourseFitting` with the build's
     settings and terrain profiles (flat, tiered with ramps, 0.5 m residual).
   - Fail on any fold, crossing without a node, collinear overlap, piece < 1 m, or near-duplicate node.
   - Until the port exists, extend `NetFit.Violations` to all net roles, with `+n` widening, a crosswalk term, floors for
     a prefab change, the 0.2 m remainder and no 165° shortcut. Run it on more than one site.

---

## 8. Open points (not proven here)

- Exact `m_NodeOffset`, `m_WidthOffset`, crosswalk depths and layers of `Alley`, the invisible paths and `Small Road`
  are asset data. 0.5 m is the field default. The numbers in §6 move by ±0.5 m until the dump exists.
- Whether `Invisible Road Path` / `Car Path` share `MergeLayers` with `Alley` and `Small Road` decides whether a join
  between them is treated as "same layer" or "different layer" (the floors differ). This is also asset data.
- The per-error positions of today's 13 + 15 net errors are not in the log. Which class caused each one is a
  hypothesis, ranked by the probe results.
- `CalculateNodeGeometryJob` was read for its structure, not ported. The claim that a capped end folds the node piece
  rests on the cap code and on the fold test, not on a numeric run of that job.

Throwaway files (not in the repo): `%TEMP%\pm-np\NetProbe.cs` (plan probe + split simulation; reports in
`%TEMP%\pm-np\bin\Debug\net9.0\probe-report.txt`, `split-report.txt`), `%TEMP%\pm-gc\Program.cs` (headless `Game.dll`
calls).
