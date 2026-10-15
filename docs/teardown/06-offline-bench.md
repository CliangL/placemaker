# Teardown 06: An offline bench that runs the game's own code

Question: how can the lead check a build without launching the game (5 to 10 minutes per launch)?

Answer: run the game's real validation and zoning code headlessly. That works today. Every claim below comes from a throwaway prototype in `.forensics/bench/` (git-ignored) that I built and ran on this machine.

Repo state: the bench compiled `Core/` from the working tree, at HEAD `d9f6df6` (s61 merged during the run; 650be0f + 2 commits). Game: CS2 1.6.2f1, `C:\Program Files (x86)\Steam\steamapps\common\Cities Skylines II\Cities2_Data\Managed`. Decompiled source (DECOMP): `C:\Users\jack\AppData\Local\Temp\pm-decomp\game`.

Labels:
- **[P]** proven by a run (the output is quoted from `.forensics/bench/run-all.log` or the named file).
- **[C]** proven from code.
- **[H]** hypothesis, with what would prove it.

The game was not launched, and nothing under `%USERPROFILE%\AppData\LocalLow\Colossal Order\Cities Skylines II\Mods` was touched.

---

## 0. Short answer

1. **The "ECS world cannot run headless" conclusion of teardowns 03 §7 and 04 §7 is wrong.** [P]
   - The only blocker is that Unity's engine calls ("icalls", `[MethodImpl(InternalCall)]`) all live in `UnityEngine.*Module.dll`. 3,479 of them are in `UnityEngine.CoreModule.dll`, and 8,689 across 55 Unity assemblies. `Game.dll`, `Unity.Entities`, `Unity.Collections`, `Unity.Burst` and `Colossal.*` contain **zero** icalls.
   - I rewrite those modules with Mono.Cecil, so that every icall becomes managed IL. The rewrite takes 1.6 to 2.3 s. About 90 icalls get real managed implementations: malloc/free/memcpy, shared statics, a synchronous job runner, logging and time. The rest throw, or return default and are logged.
   - With that rewrite, on .NET 9, all of the following run unmodified, **including** every Burst job (as managed code):
     - `NativeArray`, `NativeList`, `NativeQueue`, `NativeQuadTree`;
     - `TypeManager.Initialize` (1,992 types);
     - `new World()`;
     - the game's systems `Game.Net.GeometrySystem`, `Game.Zones.BlockSystem`, `Game.Zones.CellCheckSystem` and the Net/Object/Area/Zone search systems;
     - the real validation entry points `Game.Net.ValidationHelpers.ValidateEdge` and `Game.Objects.ValidationHelpers.ValidateObject`.
2. **The mod's own game layer runs offline too.** [P] The built `Placemaker.dll` loads next to `Game.dll`. Its ECS types are registered the way `ModManager` does it (`TypeManager.InitializeAdditionalTypes`, Game.Modding/ModManager.cs:149). `Placemaker.Game.Preview.SiteCapture.CaptureZoneBlocks` then returned the same 16 live blocks as a direct query. The mod's log works too: Colossal logging writes `Placemaker.log` to the bench's persistent path.
3. **Speed.** [P]
   - 292 plans (every type × preset, real Core planners and catalog footprints): **1.8 to 4.8 s**, for both the net validation and the zone-cell check.
   - Whole run: about **25 s wall**, including compiling, shim generation, the one-time Harmony neutering (5.4 s) and all probes.
   - Plan export: 12 s.
4. **First matrix (v0)**, with prefab numbers assumed rather than captured; see §5 for which ones. [P]
   - **104 of 292 plans hit `InvalidShape`** in the game's own `ValidateEdge`, and 133 have at least one net error.
   - The worst prefabs are `Invisible Road Path - 2xTwoway` (268), `Alley` (161), `Pavement Path` (155) and `Invisible Car Path - 1xTwoway` (155). That is the same prefab list as today's log (LOG 2466, 3010).
   - **Of 6,250 cells the preview draws as zoned shop land, 3,331 (53 %) exist as Visible, unblocked cells after the real BlockSystem and CellCheckSystem.** 100 plans lose cells.
5. **A new, concrete root cause, found by the bench.** [P]
   - The planner zones cells that the game's **own capture of the site already reports as Blocked/hidden**. These are the corner cells where two streets' blocks overlap, which `CellOverlapJobs` resolves.
   - Plaza has no nets at all and still loses 356 of 1,068 previewed cells, 24 per plan, all in corner blocks whose captured state is `2` (Blocked).
   - This matches teardown 04 V-list item "`ownBlockedOpen` counts cells blocked inside the footprint as open". The bench now proves it on real game output.
6. **The planner's zone-block port (`ZoneBlockPredictor`) agrees with the real `BlockSystem`** on 9 of 9 cases: isolated, curved and connected, with a maximum position error of 5 cm. The cell openness logic (ZoneGuard) is where the planner and the game diverge. [P]
7. **A zero-launch loop is now possible.**
   - plan → real game code → verdict, in seconds.
   - Plus a one-time, read-only **prefab capture** that rides on a session the user starts anyway. It replaces the assumed widths, node offsets, flags and archetypes with the game's real values.
   - Plus passive per-build and per-preview error dumps for calibration.
8. **Hot-reloading the planner into the running game is feasible but not free.** It needs `Core` split out of `Placemaker.dll`, a serialized plan boundary, and accepting one leaked assembly per reload. See §8.

---

## 1. What ran: experiments and exact errors

Host: a .NET 9 console (`.forensics/bench/probe`) that resolves assemblies in this order: shim directory → bench directory → game `Managed`. Game-facing code (`.forensics/bench/benchgame`) is compiled exactly like the mod: `net48`, `NoStdLib`, the game's own `mscorlib`/`netstandard`, and references to `Game.dll` etc. with `Private=false`. The host loads it with `AssemblyLoadContext`.

### 1.1 Without any shim (plain references to the game DLLs) [P]

| Call | Result |
|---|---|
| `Unity.Mathematics` (`math.cross`, `quaternion.LookRotation`) | OK |
| `Colossal.Mathematics.MathUtils.Length(Bezier4x2)` | OK (100) |
| `Game.Net.NetUtils.FitCurve` / `OffsetCurveLeftSmooth` / `CalculateCurviness` / `StraightCurve` | OK |
| `Game.Net.ValidationHelpers.CheckSegmentShape` (private, via reflection; Game.Net/ValidationHelpers.cs:1835) | OK. Good segment → `false`; folded segment → `true` with error bounds |
| `Game.Zones.ZoneUtils.CalculateCorners`, `GetCellPosition`, `CanShareCells`, `GetCellWidth(16)=2` | OK |
| `Game.Objects.ObjectUtils.CalculateBounds`, `CalculateBaseCorners`; `Game.Areas.AreaUtils.GetMinNodeDistance(Lot)=8`; `Game.Net.ValidationHelpers.QuadIntersect` | OK |
| `UnsafeUtility.SizeOf<float3>` | OK (12: IL-implemented) |
| `UnsafeUtility.Malloc` | `SecurityException: ECall methods must be packaged into a system module.` |
| `new NativeArray<int>(8, Temp/Persistent)` | same `SecurityException`, at `NativeArray'1.Allocate` |
| `NativeList<int>` / `NativeQueue<int>` | same, at `Unity.Collections.Memory.Unmanaged.Array.Resize` |
| `Colossal.Collections.NativeQuadTree` | same, in its constructor |
| `BurstCompiler.IsEnabled` | `TypeInitializationException` (static constructor uses BurstCompilerService icalls) |
| `TypeManager.Initialize()` | `NullReferenceException` at `TypeManager.Shutdown` (follow-on of a failed allocation) |
| `new World("bench")` | `SecurityException` (as above) |

Icall census (Mono.Cecil over Managed) [P]:
- `UnityEngine.CoreModule`: 3,479 icalls.
- `Unity.Burst`, `Unity.Collections`, `Colossal.Collections`: 0.
- `Unity.Entities`: 0 icalls, 3 P/Invokes.
- `Colossal.Core`: 23 P/Invokes.
- `Game`: 20 P/Invokes.
- None of the P/Invokes were hit by anything below.

### 1.2 Workaround: icall rewrite ("shim") [P]

- `shimgen` (Mono.Cecil 0.11.5) rewrites each `InternalCall` method body:
  - It forwards to `PmShim.Impl.{Type}__{Method}` when a method with the same signature exists (94 do).
  - Profiler icalls become no-ops (81).
  - Everything else either throws `NotSupportedException("PmShim: engine icall X::Y is not shimmed")` (strict mode), or returns `default` and records the call in `PmShim.Impl.Touched` (permissive mode, 55 assemblies, 8,689 icalls).
- All Unity assemblies are unsigned (`PublicKeyToken=null`), so a rewritten copy loads in place of the original.
- `PmShim` (net9) implements:
  - `UnsafeUtility.Malloc/Free/MallocTracked/FreeTracked/MemCpy/MemMove/MemSet/MemCmp/MemCpyStride/MemCpyReplicate/SizeOf(Type)/IsUnmanaged/IsBlittable/GetFieldOffsetInStruct` and pinning. Memory comes from `NativeMemory.AlignedAlloc`, zeroed.
  - `BurstCompilerService.GetOrCreateSharedMemory`, a Hash128 → block map. `SharedStatic<T>` needs this; Entities uses it everywhere.
  - Burst "not initialised", so every `[BurstCompile]` job runs its managed body.
  - `JobsUtility.CreateJobReflectionData` / `Schedule_Injected` / `ScheduleParallelFor_Injected` / `ScheduleParallelForDeferArraySize_Injected` / `GetWorkStealingRange`: **jobs run synchronously at schedule time** on the calling thread, as one batch covering the whole range.
  - `JobHandle.*` no-ops; `ThreadIndexCount=1`, `ThreadIndex=0`.
  - `Debug` log handler → console; `Application.*` paths; `Time.*`.
- Result after the rewrite: **every probe in 1.1 passes**. `TypeManager.Initialize` → 1,992 types; `new World` + `CreateEntity` OK; `BurstCompiler.IsEnabled` = False. (run-all.log lines `[OK] UnsafeUtility.Malloc` … `[OK] Unity.Entities new World`.)

### 1.3 Creating the game's systems [P]

`World.GetOrCreateSystemManaged(type)` for game systems, in order of what failed and what fixed it:

| Attempt | Error | Fix |
|---|---|---|
| any `GameSystemBase` | `NullReferenceException at Game.GameSystemBase.OnCreate()` (`GameManager.instance` is a MonoBehaviour singleton, Game/GameSystemBase.cs) | set `GameManager.instance` to an uninitialised object (`RuntimeHelpers.GetUninitializedObject`) |
| `Game.Net.SearchSystem`, `CellCheckSystem` | `NullReferenceException at Game.Tutorials.TutorialSystem..ctor()`: field initialiser `SharedSettings.instance.userState` (TutorialSystem.cs:105) | set `GameManager.settings` to an uninitialised `SharedSettings` |
| `TerrainSystem` (and everything that depends on it: GeometrySystem, ToolSystem, ValidationSystem) | `PmShim: engine icall UnityEngine.ResourcesAPIInternal::Load is not shimmed` (shaders), later NREs | neuter (below) |
| `WaterSystem` | `PmShim: engine icall UnityEngine.GameObject::Find` via `CameraUpdateSystem.OnCreate` → `VolumeHelper.CreateVolume` | neuter |
| dependency fan-out | `AudioManager.OnCreate` → `SecurityException: ECall…` (icall in `UnityEngine.AudioModule`), `LocaleMeshStateSystem`, `PlanetarySystem`, `RenderingSystem`, `TextureStreamingSystem` → `AssetDatabase..ctor` NRE | shim **all** 55 Unity modules (permissive) + neuter |
| Harmony-patching a method that references an icall of an unshimmed module | `SecurityException: ECall methods must be packaged into a system module` at `HarmonyLib.PatchFunctions.UpdateWrapper` | same: shim all modules |

The system graph pulls in rendering and audio through `GetOrCreateSystemManaged` chains, so the workable approach is an allow-list:
- **Every** concrete `ComponentSystemBase` in `Game.dll` and `Colossal.*` gets Harmony prefixes that skip `OnCreate/OnUpdate/OnDestroy/OnStartRunning/OnStopRunning`. That is 2,190 methods, patched in 5.4 s, with Lib.Harmony 2.3.6 on net9.
- Exceptions are the allow-list: `Game.Net.GeometrySystem`, `Game.Zones.BlockSystem`, the `SearchSystem` and `UpdateCollectSystem` of Net/Objects/Areas/Zones, `Game.Zones.CellCheckSystem`, `Game.Prefabs.ZoneSystem` and `Game.City.CityConfigurationSystem`.
- `EntityCommandBufferSystem` barriers (`ModificationBarrier4/5`) are left real.
- 8 generic system types (`AllowBarrier'1`, `PreDeserialize'1`, …) cannot be patched as open generics. That does not matter here.
- The inert shells still exist, so `GetOrCreateSystemManaged` succeeds. Only the allow-listed systems compute anything.

`TerrainSystem` gets a flat 2×2 CPU heightmap through reflection (`m_CPUHeights`, `playableOffset`, `heightScaleOffset`). `TerrainUtils.GetBounds` = [-3584, 3584] m. Before that, every edge got `ExceedsCityLimits` from `Game.Objects.ValidationHelpers.ValidateWorldBounds` (Game.Objects/ValidationHelpers.cs:2041).

### 1.4 Two engine behaviours the synchronous job runner must reproduce [P]

1. **Deferred arrays.** `nativeList.AsDeferredJobArray()` stores `UnsafeList* | 1` in the job struct. The native job system patches it to the list's buffer and length before `Execute`. Without that patch, `GeometrySystem.CalculateEdgeGeometryJob.CalculateOffsets` crashed with an NRE inside `ComponentLookup.get_Item`: it read garbage `Entity` values. Fix: PmShim walks the job struct (boxed, recursive) and patches every `NativeArray<T>` whose `m_Buffer` has bit 0 set.
2. **No safety checks in the release Entities build.** A `ComponentLookup`/`BufferLookup` indexer on an entity without that component is not an exception. It is an **AccessViolation that kills the process**. Example: `ValidateEdge` with edges lacking a `ConnectedNode` buffer (`data.m_ConnectedNodes[entity]`). Consequence: bench archetypes must equal the game's. See the archetype capture in §7.

### 1.5 Real game code, offline results [P]

**`BlockSystem` vs `Core/Build/ZoneBlockPredictor`** (the planner's port). Each case runs the real `BlockSystem.Update` plus barrier playback, against the port with the same curve and width:

| Case | Real | Port | Verdict |
|---|---|---|---|
| straight 96 m, 16 m road | 4 blocks / 24 frontage cells | 4 / 24 | MATCH, 0.00 m |
| straight 40 m, 12 m | 2 / 10 | 2 / 10 | MATCH |
| diagonal 120 m, 8 m alley | 4 / 32 | 4 / 32 | MATCH |
| gentle curve 150 m | 6 / 36 | 6 / 36 | MATCH, max 0.05 m |
| 90° curve r40 | 6 / 15 | 6 / 15 | MATCH, 0.04 m |
| short 14 m | 0 | 0 | MATCH |
| 2 collinear 48 m edges sharing a node | 4 / 24 | 4 / 24 | 4 exact |
| L corner 64 + 64 m | 4 / 32 | 4 / 32 | 4 exact |
| closed 96 m square loop | 16 / 96 (528 open cells) | 16 / 96 | 16 exact |

`FindContinuousEdge` (BlockSystem.cs:761) only changes the end extension for half-aligned ends: for every cell width `math.select(0,4,odd^half)` equals the free-end formula. So the port's omission is harmless except for half-aligned ends, which are untested. [C]

**`GeometrySystem` + `CheckGeometryShape`, and the real `ValidateEdge`.** Hand-made prefab: 4 m path, node offset 2 m, `m_EdgeLengthRange.min` 1 m.

| Scenario | Game verdict |
|---|---|
| straight 40 m | no errors |
| 0.5 m stub | `ShortDistance` |
| V-junction 90°, legs 3 m | `ShortDistance` + `InvalidShape` |
| V 60°, legs 3 m | `InvalidShape` on both edges |
| V 30° / 60° / 90° / 120°, legs 12 m | 30° `ShortDistance` (the node eats the edge); the rest clean |
| two parallel 4 m aisles 2 m apart + rung | `OverlapExisting` on 4 edges, `InvalidShape` on 3 |
| the same, 8 m apart | only `ShortDistance` on the rung |
| two paths crossing without a node | `OverlapExisting` on both |

The `InvalidShape` envelope moves with node offset and width. In the sweep in run-all.log ("V-junction w=…"), 90° corners fold at 3 m legs with a 2 m node offset but not with 1 m. **The verdict depends on prefab data**, which is why §7 starts with the prefab capture.

**`ValidateObject`** (Game.Objects/ValidationHelpers.cs:1521), with an existing object in the real `Objects.SearchSystem` static tree:

| Scenario | Verdict |
|---|---|
| new 2 m bush 0.5 m / 1.5 m from an existing 2 m bush | `OverlapExisting/Error` |
| … 2.5 m away | none |
| prefab flagged `Overridable` | `OverlapExisting/**Override**` (Apply would delete the existing object) |
| `Marker` flag (decal-like) | none |
| bush vs Temp 4 m path, centre 0 m / 2.5 m away | `OverlapExisting/Error`; 4 m away → none |

**Zone pipeline `GeometrySystem → BlockSystem → search trees → CellCheckSystem`.** This exercises `CellBlockJobs`, `CellOverlapJobs` and `CellOccupyJobs`. Setup: one 96 m, 16 m zoning road (144 cells, all Visible), plus a parallel non-zoning path:

| Path position (centre from road axis) | Visible / Blocked |
|---|---|
| path without `CompositionState.BlockZone` | 144 / 0: such nets never block (CellBlockJobs.cs:165) |
| `BlockZone`, 12 or 20 m | 72 / 72: the whole side's columns die (CleanBlockedCells) |
| `BlockZone`, 36 m | 108 / 36 |
| `BlockZone`, 52 m | 132 / 12 |

**Mod code:** `Placemaker.Game.Preview.SiteCapture.CaptureZoneBlocks` (real `Placemaker.dll`, bin/Release 12:14) → "mod captured 16 blocks, direct query 16 blocks".

Permissive-mode icalls actually touched over the whole run: `Shader.PropertyToID` ×220, `ResourcesAPIInternal.Load` ×4, `GameObject.Internal_CreateGameObject` ×2, `Internal_AddComponentWithType` ×2, `Object.DontDestroyOnLoad` ×2. All of them came from system constructors and probes, none from an allow-listed system's update. [P, run-all.log last lines]

---

## 2. What the decisive rules are, and how the bench gets them

| Rule (what the forensics care about) | Game source | Bench status |
|---|---|---|
| Net `InvalidShape` (fold) / `ShortDistance` / `LongDistance` / `SteepSlope` / `OverlapExisting` between nets | `GeometrySystem` (all 9 jobs) + `Game.Net.ValidationHelpers.ValidateEdge` (ValidationHelpers.cs:1126) | **real code** [P]. Needs real `NetGeometryData` / `NetCompositionData` / buffers (§7 capture). |
| Object `OverlapExisting` (object vs object, object vs net), `Override` vs `Error` severity | `Game.Objects.ValidationHelpers.ValidateObject` (:1521) with the real Objects/Net search trees | **real code** [P]. Needs real `ObjectGeometryData` flags (`Overridable`, `Marker`, `ExclusiveGround`, `Circular`, …). |
| Area `InvalidShape` (lots, our Zone Block lots, surfaces) | `Game.Areas.ValidationHelpers.ValidateArea` (:661) / `ValidateTriangle` (:964); triangulation by `Game.Areas.GeometrySystem` | callable with the same pattern (same `EntityData`, trees); **not run yet**. Port: `Core/Build/AreaRings.cs`. |
| Zone blocks from roads | `Game.Zones.BlockSystem` | **real code** [P] |
| Cell Blocked / Visible / overlap / depth | `CellCheckSystem`: `CellBlockJobs`, `CellOverlapJobs`, `CellOccupyJobs` | **real code** [P]. Objects and areas (our Zone Block lots, buildings) are not fed yet. |
| Zoning paint (Marquee None-only), `Cell.m_Zone` | `GenerateZonesSystem` / ZoneTool | not run. Candidate for the allow-list. |
| Growable spawn (demand, lot fit) | `ZoneSpawnSystem` (stochastic, needs demand) | not a deterministic gate. The bench can check "a spawnable prefab's lot fits the live cells", from captured `BuildingData.m_LotSize`. |
| Apply semantics (what is deleted / overridden / applied despite errors) | `Game.Tools` apply systems | not run. Candidate once `GenerateNodes/GenerateEdges/ApplyNet/ApplyObjects` are allow-listed. Teardown 03 §4 has the code-level rules. |

The game's own primitives (`NetUtils`, `MathUtils`, `ZoneUtils`, `ObjectUtils`, private helpers through reflection) also run without the shim. They are the cheap tier for unit tests.

---

## 3. Inventory: what Placemaker already ports, and how faithful it is

| Port | Game rule | Faithfulness (evidence) |
|---|---|---|
| `Core/Build/ZoneBlockPredictor.cs` | `BlockSystem.CreateBlocks` for one edge | **Equal** to the real system within 5 cm on 9 cases, including connected and loop cases [P §1.5]. Untested: half-aligned ends, roundabout nodes, edges cut by node geometry. |
| `Core/Build/ZoneGuard.cs` (`CellOpen` :1322, `Compute` :609, `ReduceDepths` :1118 = CellOverlapJobs depth rule, CleanBlockedCells :879/:1242) | `CellBlockJobs` / `CellOverlapJobs` | **Diverges.** "On road" is a distance test from the cell centre, not a quad intersection. `ownBlockedOpen` treats game-Blocked cells inside the footprint as open, which the bench shows produces previewed shop cells the game has already blocked [P §5]. |
| `Core/Layers/Commercial/NetFit.cs` (s50 cut-back model) | `GeometrySystem` node cut-back → `CheckSegmentShape` | Model only. The real code now runs, so this can be replaced by, or differentially tested against, `GeometrySystem` + `ValidateEdge`. |
| `Core/Build/AreaRings.cs` | `Game.Areas.ValidationHelpers.ValidateArea` (InvalidShape rules 1–3) | Port with provenance in its header; not differentially tested yet. |
| `Core/Build/OwnOverlap.cs`, `BuildVerdicts.cs`, `Declutter` footprints | object collision boxes (`ObjectUtils.CalculateBounds`, ValidateObject iterators) | Approximation from catalog bounds. The real `ValidateObject` is now available as the reference. |
| `Core/Build/SiteSafety.cs` | Warning ⇒ Apply deletes existing buildings | Rule from code; the bench can assert "no Warning/Override against non-Placemaker entities". |
| `.forensics/scratch-08/CellOverlapPort` | a CellOverlapJobs port attempt | superseded: the real job runs. |

---

## 4. How the prototype works (for the lead to re-run)

```
.forensics/bench/
  pmshim/      net9 lib  PmShim.dll: managed icall implementations + synchronous job runner + deferred-array patch
  shimgen/     net9 exe  Mono.Cecil: rewrites UnityEngine.CoreModule (strict -> shim/) or all 55 Unity modules
                         (PM_PERMISSIVE=1 -> shim-permissive/)
  probe/       net9 exe  host: assembly resolver (shim -> bench -> Managed), Lib.Harmony "skip" patcher exposed
                         via AppDomain data "pm.skip", optional PM_MODDLL preload, runs BenchGame P.Main
  benchgame/   net48-surface lib, game mscorlib, Core/** compiled in
               Probes.cs (primitives, containers, World)  SystemProbes.cs  ZoneBlockBench.cs  NetShapeBench.cs
               NetValidateBench.cs  ObjectValidateBench.cs  ZoneCellBench.cs (+ NeuterAllExcept allow-list)
               MatrixBench.cs (city capture, matrix, mod SiteCapture, connected-road differential)  Fakes.cs
  planexport/  net9 exe  references Tests/Placemaker.Gallery: every type x preset -> plans.txt / sites.txt
  loop.ps1     build shim + shimgen + benchgame + host, then run (flags PM_* select experiments)
  run-all.log, plans.txt, sites.txt, sites.grids.txt, plans.matrix.tsv   outputs of the last full run
```

Matrix pipeline (all offline):
1. `planexport --sites` writes each job's ring plus city streets. The streets form a noded square loop, 12 m wide, 8 m outside the ring.
2. `PM_SITES=sites.txt` runs the **real** BlockSystem and CellCheckSystem on the streets alone. It writes `sites.grids.txt` in the same encoding `SiteCapture` uses (Visible=1, Blocked=2).
3. `planexport --plans plans.txt sites.grids.txt` composes each plan with those captured grids as `site.ZoneGrids`, exactly as the game layer feeds Core.
4. `PM_MATRIX=plans.txt` does two things:
   - (A) all planned nets go in as Temp entities, split at T-ends; the real GeometrySystem runs, then the real ValidateEdge on each.
   - (B) the same nets go in applied (non-Temp, a higher build order than the city), with the real Geometry → Block → CellCheck pipeline; every preview "Zoned" cell is then looked up in the live blocks.

   Output: `plans.matrix.tsv`.

The bench also writes, for the build from the Gallery project, `Tests/Placemaker.Gallery/bin|obj` (git-ignored). Nothing tracked was modified.

---

## 5. Matrix v0 (292 plans, square sites, flat ground)

Assumptions until the prefab capture exists. These are what make v0 a **screen**, not a verdict:
- width from `ZoneGuard.NetWidth` (catalog fixture `w`);
- composition node offset 0.5 m (teardown 03's default);
- `m_EdgeLengthRange.min` 0;
- `BlockZone` on every net;
- `EnableZoning` on Street/Avenue/CulDeSac/Roundabout/PerimeterRoad/Alley;
- one merge layer per role group;
- flat ground;
- no objects or areas in the zone pass;
- city = a 4-street loop.

| type | plans | planned nets | plans with InvalidShape | InvalidShape | OverlapExisting | previewed zoned cells | open in the city-only capture | alive after the build |
|---|---|---|---|---|---|---|---|---|
| amphitheatre | 6 | 62 | 4 | 10 | 0 | 164 | 96 | 96 |
| beach | 7 | 65 | 5 | 14 | 0 | – | – | – |
| botanicalGarden | 5 | 115 | 5 | 15 | 0 | – | – | – |
| fairground | 6 | 10 | 0 | 0 | 6 | 308 | 168 | 166 |
| greenwayPark | 3 | 28 | 2 | 2 | 4 | 84 | 48 | 48 |
| highStreet | 6 | 84 | 6 | 36 | 40 | 555 | 175 | 112 |
| hikingTrail | 7 | 58 | 5 | 16 | 0 | – | – | – |
| lookoutHill | 6 | 95 | 4 | 23 | 26 | – | – | – |
| mallDistrict | 6 | 72 | 6 | 68 | 8 | 552 | 216 | 168 |
| mallPark | 5 | 204 | 5 | 144 | 78 | 240 | 60 | 70 |
| marketSquare | 4 | 0 | 0 | 0 | 0 | 432 | 288 | 288 |
| outdoorMall | 6 | 75 | 6 | 37 | 21 | 544 | 242 | 328 |
| park | 14 | 100 | 3 | 13 | 10 | 416 | 252 | 260 |
| **parking** | 5 | 152 | **5** | **126** | 5 | – | – | – |
| plaza | 15 | 14 | 0 | 0 | 0 | 1068 | 712 | 712 |
| restArea | 6 | 87 | 6 | 54 | 12 | – | – | – |
| retailPark | 5 | 87 | 5 | 29 | 31 | 438 | 273 | 300 |
| roads | 17 | 49 | 7 | 32 | 8 | – | – | – |
| shoppingPlaza | 6 | 112 | 6 | 59 | 61 | 280 | 18 | 88 |
| shopsPark | 6 | 211 | 5 | 68 | 40 | 274 | 138 | 164 |
| **stripMall** | 6 | 162 | **6** | **101** | **101** | 349 | 217 | 207 |
| transitPlaza | 6 | 26 | 1 | 19 | 7 | – | – | – |
| waterfront | 6 | 12 | 0 | 0 | 0 | 546 | 324 | 324 |
| wetland / zenGarden / sportsPark / memorialPark / golf / campground | 35 | 623 | 12 | 25 | 20 | – | – | – |
| other 18 types (abandonedLot … urbanRuins) | 98 | 463 | 0 | 0 | 140 (urbanFarm 78, communityGarden 40, orchardPark 16, farmstead 6; cause not examined) | – | – | – |
| **total** | **292** | 2,966 | **104** | **891** | **618** | **6,250** | 3,227 | **3,331** |

How to read the zone columns:
- **previewed** = cells the preview draws as `Zoned`.
- **open in capture** = those cells that are Visible and not Blocked when only the city exists. This is the planner's own input; cells on our own new lanes are absent here by definition.
- **alive** = Visible and not Blocked after our nets are built.

Findings:
- **F1 [P].** The game's own `ValidateEdge` rejects nets in 104 plans: every parking, strip-mall, mall and high-street plan. Prefab names and ranking match today's log. Even under assumed prefab data, this agrees with teardown 03 (port-based: 192 / 70 / 68 for the top three).
- **F2 [P].** Types without their own zoning roads (plaza, marketSquare, waterfront, park, fairground) lose exactly the cells that the city-only capture already reports closed: plaza 1,068 → 712.
  - Diagnosis for `plaza|default`: all 24 lost cells lie in the corner blocks. Their captured string is mostly `2` (e.g. `GRID|plaza|default|85.545 26.7725|1 0|8 6|2222222122…`), and the planner zoned indices (4..5, 0..2) of them.
  - The planner draws shop land on cells its own capture marks Blocked. Hypothesis about the exact line: `ZoneGuard.Compute(..., ownBlockedOpen: true)` (teardown 04 §2). Proof: flip that flag in a bench run and expect plaza previewed = alive.
- **F3 [P, under the v0 assumptions].** Commercial types lose cells both ways:
  - our nets with `BlockZone` cover frontage cells: shoppingPlaza 280 → 88, highStreet 555 → 112;
  - previews include corner cells (F2).

  The objects and Zone Block lots that are not modelled yet can only lower "alive" further.
- **F4 [P].** Overlap between our own nets (`OverlapExisting` with no city object present) shows up in stripMall (101), shoppingPlaza (61), mallPark (78) and in four farm types (cause not examined). The likely causes are nets crossing without a node, or running parallel too close (see the §1.5 scenarios; the TSV does not export per-net positions yet). Under s59 anarchy they get applied anyway. Teardown 03 §4 says the result is permanent folded or overlapping geometry.

Runtime: the matrix itself takes 1.8 to 4.8 s; city capture 2.1 s; plan export 12 s; neutering 5.4 s once per process. A full run with build is about 25 s. Scaling to 6 terrain fixtures × the commercial sites: about 2 to 3 min [H, linear extrapolation].

---

## 6. Fidelity limits (what the bench does not prove yet)

1. **Prefab data is assumed** (widths, node offsets, length ranges, flags, compositions, object geometry flags). This is the biggest gap. It is closed by the capture in §7.
2. **Archetypes are hand-made.** A missing component is an AccessViolation, not an exception. Capture the real Temp/applied archetypes.
3. **Jobs run single-threaded, synchronously, and Burst runs as managed code.** Results are deterministic for these jobs. Order-dependent outputs (parallel queues) can differ in order, but not in content. Float differences between Burst native code and managed RyuJIT are possible in the last ULPs of transcendental functions [H]. Thresholds like `cross.y < 0` are robust; calibration criterion A6 catches the rest.
4. **Environment.** Flat terrain, no water (`WaterSurfaceData` is default: any check that samples water would read invalid memory), no map tiles (`ExceedsCityLimits` from tiles not evaluated), no city objects or areas. The tool/apply systems (`GenerateNodes/Edges`, `ApplyNet`, `ApplyObjects`) are inert, so the bench does not yet prove what Apply does with errors.
5. **Crossings.** Two of our nets crossing without a node are reported as `OverlapExisting`. Whether the game's course application inserts a node there is not verified [H]. Prove it by allow-listing `Game.Tools.GenerateEdgesSystem` and feeding `CreationDefinition` + `NetCourse` like BuildService does.
6. **One crash seen, not reproduced.** A teardown crash (exit 255) happened once, while the neuter list was partial. It did not recur with the full allow-list.

---

## 7. The bench to build (projects, references, acceptance)

### 7.1 Projects to add (all under `Tests/`, so `Placemaker.csproj`'s `Compile Remove="Tests\**"` keeps them out of the mod)

| Project | TFM / compile | Content |
|---|---|---|
| `Tests/Placemaker.GameBench.Shim` | net9 lib | `Impl.cs`: icall implementations, synchronous job runner, deferred-array patch, `Touched` log |
| `Tests/Placemaker.GameBench.ShimGen` | net9 exe, `Mono.Cecil` 0.11.5 | rewrites the Unity modules of a given `ManagedPath` into `Tests/Placemaker.GameBench/.shim/<game version>/` (git-ignored). The game version comes from `Cities2_Data/app.info` plus the hash of `Game.dll`. Strict mode by default; permissive for system tests. |
| `Tests/Placemaker.GameBench.Host` | net9 exe, `Lib.Harmony` 2.3.x | resolver (shim → bench → Managed); neuter service with an allow-list; loads `Placemaker.GameBench.dll` and, optionally, the built `Placemaker.dll` (calling `TypeManager.InitializeAdditionalTypes`). Commands: `probe`, `capture-city`, `matrix`, `replay <audit.json>`, `golden`. Exit code ≠ 0 on any gate failure. |
| `Tests/Placemaker.GameBench` | `net48`, `NoStdLib`, the game's `mscorlib`/`netstandard`/`System`/`System.Core` from `$(ManagedPath)`, `LangVersion 9` (same as `Placemaker.csproj`) | oracles: `NetOracle` (GeometrySystem + ValidateEdge), `ObjectOracle` (ValidateObject), `AreaOracle` (Areas GeometrySystem + ValidateArea), `ZoneOracle` (Block + CellCheck, later GenerateZones); `CityFixture`, `PrefabFixture` (loads the capture JSON), `PlanReplay`; compiles `Core/**` like the tests do |
| `Tests/Placemaker.GameBench.PlanExport` | net9 exe → later folded into Host | the type × preset × site × terrain job list (from the Gallery `Variants`/`Renderer`) → plan text/JSON. The same format as the in-game "bench replay" section, so offline jobs and audits share one reader. |
| `Tests/Placemaker.Tests/GameBenchGate.cs` | existing xUnit (net9) | `[Trait("Category","GameBench")]`: runs `Host matrix` as a child process and asserts the gates. Skipped when `ManagedPath` is missing. Normal `dotnet test` stays fast (filter by category). |

Game DLL references: `<Reference Include="…"><HintPath>$(ManagedPath)\X.dll</HintPath><Private>false</Private></Reference>`. Use the same `ManagedPath` property and default as `Placemaker.csproj`. Nothing is copied into the repo or shipped. At runtime the Host resolves from `.shim/<version>/`, then `$(ManagedPath)`. The `.shim/` and outputs go in `.gitignore`.

### 7.2 Acceptance criteria

- **A1. Primitives.** `Host probe` in strict mode passes the 26 probes of §1.2: math, containers, `TypeManager`, `World`.
- **A2. Icall hygiene.** During any oracle run, `Touched` may contain only an allow-list (`Shader.PropertyToID`, `Resources.Load`, `GameObject` creation, `DontDestroyOnLoad`), and only while systems are being created. Any other icall fails the run with its name.
- **A3. Oracle self-tests (golden).** The scenarios of §1.5 produce exactly the verdicts listed: stub → `ShortDistance`; parallel 2 m → `OverlapExisting`; bush 0.5 m → `OverlapExisting/Error`; `Overridable` → `Override`; BlockZone path at 12 m → 72 / 144 cells. After the prefab capture lands, re-derive them with captured prefabs and freeze them.
- **A4. Port differentials.** `ZoneBlockPredictor` vs `BlockSystem`: 0 blocks off by more than 0.1 m over 500 random curves, including half-aligned and node-cut cases. `ZoneGuard` cell openness vs `CellCheckSystem`: 0 mismatching cells on the matrix sites. `AreaRings` vs `ValidateArea`: 0 mismatches on the plans' lots.
- **A5. Matrix gates.** These are the user's standing requirements as tests, run over every type × preset × site fixture (square, commercial sites, slope, shore):
  - (a) 0 `InvalidShape` / `ShortDistance` on planned nets;
  - (b) 0 `OverlapExisting` between our own nets;
  - (c) 0 `Override` or `Warning` verdicts against non-Placemaker entities (existing buildings are never destroyed);
  - (d) **every previewed zoned cell is alive** after the build (preview = result);
  - (e) every previewed object and building exists in the applied entity set, once Apply systems are allow-listed.

  Today: (a), (b) and (d) fail (§5).
- **A6. Calibration.** The bench replays each in-game audit: plan + site capture + the game's error list from the same session. It must reproduce ≥ 95 % of the in-game errors (type + piece + position within 0.5 m) and add no more than 5 % extra. Until then, every bench verdict prints "UNCALIBRATED (prefab capture <version|missing>)".
- **A7. Runtime.** `Host matrix` on all fixtures completes in ≤ 120 s on this machine, and ≤ 30 s for the default square set.
- **A8. Reporting rule for the lead.**
  - "Fixed" may only be said when A5 is green for every type sharing the changed code path, and the message names the capture version.
  - "Installed" may only be said when an in-game audit carries the build hash.

---

## 8. Zero-cost in-game safety net (rides on sessions the user starts anyway)

All of the following are read-only for the city and opt-in where they create anything.

1. **Prefab and archetype capture** (one-time per game version; on by default, read-only, about 1 s). On city load, write `…/ModsData/Placemaker/bench-capture-<gameVersion>.json`. Dump generically through reflection over `IComponentData` / `IBufferElementData` fields, with entity references written as prefab names, for:
   - every net prefab Placemaker resolves: `NetData`, `NetGeometryData`, `PlaceableNetData`, `NetSubSection`, `NetSectionPiece` and related buffers;
   - every composition entity the city already has for those prefabs (`NetCompositionData` + `NetCompositionLane` / `Crosswalk` / `Piece` / `Area`, keyed by prefab + `CompositionFlags`);
   - object prefabs (`ObjectGeometryData`, `PlaceableObjectData`, `SpawnableObjectData`);
   - buildings (`BuildingData.m_LotSize`, `SpawnableBuildingData` zone);
   - `ZoneBlockData`, `AreaGeometryData`;
   - the component-type lists of the archetypes that the game's tools give Temp/applied edges, nodes, objects and areas.

   The bench rebuilds prefab entities from this, and runs the real `NetCompositionSystem` offline (allow-list it) for compositions the city does not have yet. Replaces every assumption in §5.
2. **Passive preview errors** (free). When the user previews, the Temp entities and their `Error`/`Warning` exist anyway. Dump each `ErrorData` with position, prefab and the planned-piece key. Teardown 03 §7 asks for the same; today only counts are logged.
3. **Per-build audit, extended** (`group-*.json`). Add:
   - the exact plan + site capture input (a "bench replay" section, in the same format as PlanExport);
   - live blocks with per-cell `m_State` + `m_Zone` + `BuildOrder` after Apply;
   - our nets' real `EdgeGeometry` bounds and composition `m_State`;
   - every verdict with severity.

   The lead runs `Host replay` on these files, offline, and gets criterion A6.
4. **Opt-in self-check** (a setting, "Developer: run Placemaker self-check", off by default). When the tool is idle, generate N fixed designs on an empty owned spot. Create only `CreationDefinition`s, exactly like the preview. After `ValidationSystem` has run, read the errors and delete the definitions. Write `selfcheck-<build>.json`.
   - It changes nothing in the city, but it does create Temp entities and may flash ghosts, so it stays opt-in.
   - With items 1 to 3 in place, its main value is a direct engine-vs-bench comparison: Burst floats, real archetypes, water and tiles.
5. **Version stamp in every file**: game version, `Game.dll` hash and Placemaker commit. The bench refuses to compare across versions.

---

## 9. Hot-reloading the planner into the running game

Feasibility: **yes, with a refactor**. Not proven here, because that would need a game launch. Known Mono semantics are marked [C/H].

- Today `Core/**` is compiled into `Placemaker.dll` (`Placemaker.csproj` removes only UI/Tests/docs) [C]. There is nothing separate to reload. Step 1 is a `Placemaker.Core.dll`. It is engine-free; the tests and Gallery already compile it alone.
- Unity's Mono (MonoBleedingEdge, .NET 4.x profile) loads `Assembly.Load(byte[])` as a **new, distinct** assembly each time, even with the same name, and cannot unload it. There is no collectible `AssemblyLoadContext` [C/H]. Every reload leaks the old copy (a few MB), which is fine for dev sessions.
- **Type identity.** The game layer binds to the first Core's types (`ComposedPlan`, `ZoneGrid`, …). A reloaded Core's types are different types. So the boundary must be either:
  - (a) a tiny, never-reloaded `Placemaker.Contracts.dll` holding an interface (`IPlanner.Compose(string recipeJson, SiteSnapshot) → PlanDto`) with plain DTOs; or
  - (b) text: recipe/site in, plan out (`ReceiptCodec`/`RecipeJson` exist; a `PlanCodec` is needed).

  A reload whose contract hash changed must be refused.
- **Statics.** The new Core starts with fresh statics (`ZoneGuard.Enabled`, `SiteHarmony.Disabled`, `Terraces.TerraceField.Inspect`, `LastRefreshReport`). The game layer must push settings into the new instance after every load.
- **Unaffected:** Harmony patches and ECS systems live in the game layer. Core has no Burst and no ECS types, so there is no `TypeManager` registration problem. That problem only exists for assemblies with components, see §0.2.
- **Trigger.** A file the user drops (or approves) into a non-Mods folder, e.g. `%LOCALAPPDATA%\Placemaker\hot\Placemaker.Core.<hash>.dll`, plus a panel button "Reload planner (dev)". Never drop it automatically into the Mods folder while the game runs.
- **Risk.** Preview and build would use different planner versions if a reload lands between them. Pin the planner instance per group: preview, build and refresh use the instance that made the plan.
- **Value vs the bench.** With the bench, most planner fixes are verified without the game at all. Hot reload only helps with the residual "looks right in game" checks. Recommend it after the bench, not instead of it.

---

## 10. Files

- Report: `C:\Users\jack\cs2-area-tool\docs\teardown\06-offline-bench.md`.
- Prototype: `C:\Users\jack\cs2-area-tool\.forensics\bench\`, containing `pmshim\Impl.cs`, `shimgen\Program.cs`, `probe\Program.cs`, the `benchgame\*.cs` files listed in §4, `planexport\Program.cs` and `loop.ps1`.
- Outputs: `.forensics\bench\run-all.log` (full run, 80 OK / 8 expected FAIL = system creation without neutering), `plans.matrix.tsv`, `sites.grids.txt`, `plans.txt`, `sites.txt`.
- Mod copy used for the mod-code test: `.forensics\bench\modbin\Placemaker.dll` (copied from `bin\Release`, 12:14).
- Re-run:

  ```
  $env:PM_PERMISSIVE='1'; $env:PM_ZB='1'; $env:PM_NET='1'; $env:PM_CELLS='1'; $env:PM_VALIDATE='1'; $env:PM_OBJ='1'; $env:PM_CONT='1'; $env:PM_MODDLL='<bench>\modbin\Placemaker.dll'; $env:PM_MATRIX='<bench>\plans.txt'; & <bench>\loop.ps1
  ```

  For a fresh matrix, first run `planexport --sites`, then `PM_SITES`, then `planexport --plans plans.txt sites.grids.txt`.
