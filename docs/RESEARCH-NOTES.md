# Research notes (2026-10-13)

Sources cloned in `C:\Users\jack\`: `ParkingLotTool`, `ParkManager`, `GridRoadGenerator`.
Game: CS2 1.6.2f1, Coherent Gameface 2.2.1. Toolchain not installed yet (Options → Modding).

## CS2 engine rules (mostly from ParkingLotTool's measured comments — treat as a checklist)

Prefabs & frames
1. Runtime prefabs must be added **before `PrefabInitializeSystem` (PrefabUpdate)**; using one the same frame it was added = native crash. `AddPrefab` alone builds no archetype.
2. A `CreationDefinition` becomes a Temp entity only **next frame** → builds are ≥2 frames (define → materialize → own → apply).
3. Saves reference prefabs **by name**: runtime clones must be recreated before a save loads (persist the clone list).
4. Saving without the mod drops custom component types; prefab-backed areas survive.
5. ECS serializer has **no per-component length** → write a version first, only append fields.

Areas & surfaces
6. CS2 insets every area ring 0.1 m, ear-clips it, and **silently drops the whole surface** if triangulation fails (tiny edges, needles). Areas cannot have holes.
7. Default tool raycasts `AreaTypeMask.Lots` only → a Surface is never clickable; use a Lot-type owner area (`m_AllowEditing` must stay true).
8. Owners need `SubNet`/`SubObject` buffers even when empty (Burst job crash otherwise). Info panel needs a `PrefabRef`.
9. Overlay can't fill polygons and `AreaBatchSystem` skips Temp → filled previews need instanced meshes with the game's overlay material (linear colour).
10. Surface draw priority −100…−95 vanilla; −94 wins; default decal layer is Terrain-only (needs Terrain|Roads over roads).

Deletion & ordering
11. Mark deletions in **PreTool**; `ReferencesSystem` runs in Modification2B — later deletes leave dangling refs → native crash.
12. Delete nodes only after all their edges, same frame; never a node with a foreign edge. ≤32 parts per pass.
13. Objects owned by a net edge get culled by `SubObjectSystem`; `Attached` objects get moved → hang objects on a bare carrier entity.
14. `SetComponentData` on a missing type can corrupt memory — add first.

Nets & parking
15. Invisible pathways join roads via **LocalConnect** (dead-end), not intersections.
16. A decal prefab with a `SubLane` spawns a real `ParkingLane` (= working stall). Mesh-less static objects crash.
17. `ParkingLaneDataSystem` zeroes comfort on area-owned lanes — write after it.
18. Endpoint snapping to a road mid-edge: `CoursePos.m_Entity = edge`, `m_SplitPosition = t` (GRG).

Input, UI, runtime
19. Modifier keys mask `ProxyAction`s → read `Mouse.current` for Ctrl/Shift/Alt clicks.
20. `NameSystem.SetCustomName` only works in UIUpdate.
21. `Assembly.Location` is empty for CS2 mods; mod is instantiated without field initializers running.
22. `ValueBinding<string[]>` broke mod load; use JSON / RawValueBinding.
23. Gameface CSS: no grid, no `hsla()` (disable cssnano colormin), no `var()` in shorthands, no `:not()`/`:disabled`, transitions need property names, no `clamp/min/max`. `Logs/UI.log` lists rejected CSS.
24. `cs2/ui` lacks Slider/ToolButton/Section → resolve via module registry (`VanillaComponentResolver`). No `GameMainToolbar` target; valid: `Game`, `GameTopLeft`, `GameTopRight`, `GameBottomRight`, `UniversalModMenu`, …
25. Debug UI: launch with `--uiDeveloperMode`, Chrome at `localhost:9444`.
26. Hotkeys taken: Ctrl+P (Find It/Platter), Ctrl+Shift+P (PLT), Ctrl+G (GRG), M (Move It).

## Reuse verdicts (condensed)

| Piece | From | Verdict |
|---|---|---|
| Polygon editor + snapping + edge drag | PLT (PM has a copy) | adapt into shared DrawEditor |
| Tab×mode×action click-ownership table | PLT `Werkzeugzustand` | reuse pattern |
| Block picking, Dijkstra fill, face trace | GRG `NetworkGraph` | reuse, add cursor-aware pick |
| Brush → outline | GRG `BrushMask` | adapt (islands/holes later) |
| Unified undo (shape+settings, gesture coalescing) | GRG History | adapt, typed state |
| Off-thread preview worker | GRG SketchWorker / PLT async build | adapt, proper hashing |
| Filled preview meshes | PLT `Flaechennetz` | adapt |
| Collision preview + auto-exclude | GRG | adapt |
| Build transaction (define/materialize/validate/apply/tag) | PM (cleanest), PLT | one shared engine |
| Ownership: lot area + carrier + raycast lift + cascade delete | PLT | adapt |
| Remove-as-group UI | PM | reuse |
| Surface acceptance (triangulation rules) | PLT | **reimplement** (PLT's is decompiled code) |
| Parking layout engine (Geometry/Zellen) | PLT | isolate as ParkingGenerator; drop 3.8k dead lines |
| Vegetation planting | PLT `ParkingVegetation`, PM decoration planner | merge into Park generator |
| Park paths / decoration / plaza planners | PM | reuse (move off main thread) |
| Road patterns, perimeter road, connect-to-existing | GRG Core | RoadsGenerator behind interface |
| Asset catalog (palettes from vanilla parks) | PM | adapt, add search + localized names |
| UI defensive shims (vanilla.ts, safeButton, errorBoundary, rtl, tips) | GRG | reuse |
| Prefab picker with search/recent | GRG (from NetworkTools, MIT) | adapt for all asset types |
| Offline UI mock + Playwright | PM | adapt — phase 0 |
| Late community-locale registration | GRG | reuse |
| PLT background sync / Hintergrund / utilities / bus stops / diagnostics | PLT | drop for v1 |

## UX pain points observed (don't repeat)

- PLT: 7 tabs incl. Debug/Report for everyone; ~38 tiny reset buttons; jargon ("Synchronize", "Apron", "Median depth"); 9 click modes with different right-click meanings; live debug hotkeys (Shift+P changes build ownership); preview up to 10 s with no progress.
- PM: status messages never rendered; silent failed builds; outline click wipes entrances/plan; "Variant" overwrites explicit choices; raw prefab names; abstract % sliders; lake default-on and permanent; "Finish" unclear.
- GRG: draw mode has no snapping/insert; block double-click can't pick which block; pattern chosen before area; undo keys hard-coded; ~100 bindings pushed per frame; overlay walks every road node each frame.
