# Teardown 09: the game hangs when switching style or type

Master `650be0f`, the 12:09–12:12 playtest (`00-user-feedback-1215.md`: "my game gets hung a lot when I'm switching styles or
switching types"). Evidence: `.forensics/Placemaker-20261015-master.log` (cited as **M:line**), `.forensics/Placemaker-20261015-1152.log`
(**E:line**), the source at master, and offline measurements made with a throwaway harness in
`.forensics/scratch-switchhang/` (section 8). **[P]** = proven (code + log or measurement). **[H]** = hypothesis (plausible, not
measured in game).

## 1. Verdict

* **[P] The Core planner is not the hang.** It already runs off the main thread (`Game/Preview/PreviewWorker.cs:55-99`, Task.Run,
  latest request wins, cancellation). Offline it takes a median of 19 ms and a p99 of 140 ms per type × preset, with a worst case of
  176–531 ms on a 3 ha hilly site (903 compositions, section 4). The work it leaves on the main thread is 1–42 ms per plan.
* **[P] The hang is the asset catalog rebuilding itself on the main thread after each switch.** A switch to a type or preset whose
  plan uses a decal at a new see-through tier makes `DecalOpacitySystem` register new prefabs (`AddPrefab`, `DecalOpacitySystem.cs:224,261`).
  The prefab count changes, so within 120 frames `AssetCatalogSystem.OnUpdate` (`AssetCatalogSystem.cs:77-98`) runs a **full
  synchronous `Scan()`** of every prefab in the game (`:383-475`). One frame of that scan:
  - iterates every prefab (Traits, icon, display name and Core classification per prefab),
  - walks 96 park palettes,
  - rebuilds every automatic pool,
  - serialises and writes the **6.4 MB `catalog-dump.json`** (`AssetCatalogSystem.Dump.cs:25-42`, `MaybeWriteDump(true)` at `:462`).

  The timestamped part of that frame alone (palettes → "asset catalog vN") lasts **124–637 ms** (17 scans across both logs). The
  untimed prefab loop comes before it in the same frame and is bounded at roughly 0.2–4.7 s by the log gaps (section 3).
* **[P] Each catalog rebuild then rebuilds the ghost a second time.** The scan's new clone items change the catalog content, so the
  version goes up (`:452-458`; the dump lists **133 "Placemaker Decal …" clones** as catalog items). The new version re-runs
  `ResolveAssets` (`PlacemakerToolSystem.Review.cs:28-42`), which bumps `_assetsResolvedStamp`. That bumps the ghost revision
  (`PlacemakerToolSystem.Ghost.cs:53-63`), and `PreviewModeMachine.cs:158-160` issues a new Show. `BuildService.BeginGhost`
  (`BuildService.Ghost.cs:222-256`) then destroys and re-emits every temporary of a plan that did not change.
* In the playtest this happened **3 times in 18 s of switching** (12:10:55 → 12:11:13, section 3). The pattern is: freeze 4–6 s
  after a switch, then full ghost churn. Once every tier had been cloned, switching stopped triggering scans. That is why the hangs
  feel random: "a lot", but not on every click.

## 2. What runs on a switch (call chain)

| # | Step | Thread | Code |
|---|---|---|---|
| 1 | UI trigger `selectRecipe` / `selectPreset` / `applyPreset` / `setValue` | main (binding) | `Game/UI/PlacemakerUISystem.cs:64,78,77,73` |
| 2 | `SelectPreset` → `SwitchRecipe` (Instantiate, AddDefaultFeatures) → **`_settings.ApplyAndSave()` on every type switch** → `_presets.Apply` → `DesignChanged` (`_inputRevision++`) → history `Commit` | main | `PlacemakerToolSystem.Presets.cs:60-89`, `.Design.cs:75-104` (save at `:102`), `:22-29` |
| 3 | `UpdatePlanning`: debounce only for drags/hover (`PlanSchedule.Debounce`), `SiteFor` reuses the captured site (no new terrain/net/obstacle capture unless the outline or catalog readiness changed), `_worker.Request(rev, recipe.Clone(), shape, site)` | main | `PlacemakerToolSystem.Plan.cs:187-222`, `:256-278` |
| 4 | `Composer.Compose`: generators → PathJoins → decal Dressing → paint remap → Cutouts → SiteHarmony (terraces / terrain grading) → SiteSafety → ZoneGuard (zoning predictor) → TerrainHeat | **worker** | `Core/Composition.cs:167-195`, `PreviewWorker.cs:68-98` |
| 5 | `PumpPreview` → `ResolveAssets` → `AssetCatalogSystem.ResolveAutomatic`: AutoAssets.Resolve, DecalFit.Orient, **PrewarmSeeThrough** (requests decal clones; `SaveRemembered()` rewrites `decal-opacity.txt` once per new clone, `DecalOpacitySystem.cs:97`), Declutter.Run | main | `PlacemakerToolSystem.Plan.cs:174-185`, `.Review.cs:28-42`, `AssetCatalogSystem.cs:155-182,336-352` |
| 6 | Ghost: revision bump → `Show` → after 0.25 s debounce `BeginGhost`: destroy definitions (one Clear frame that deletes every temporary), ResolveAutomatic again, `PlanWork.Flatten`, `ResolvePrefabs`, **all nets in one frame**, clear definitions, then **800 objects per frame** (`GhostPerFrame`). The game's Temp pipeline (generation, validation, search trees) runs on the same frames. `EmitObject` also calls `CloneFor` per decal | main + game systems | `PlacemakerToolSystem.Ghost.cs:66-103`, `BuildService.Ghost.cs:55-57,124-133,145-195,222-275`, `BuildService.Emit.cs:330-350` |
| 7 | Overlay, new plan: `InfoMap()` copy, **`PaintRaster.Build` (up to 2^18 cells) for the paint preview**, heat and zone-cell previews, then `FilledPreviewSystem.BuildMesh` for every changed layer: EarClip (O(n²) per ring) + subdivision to 8 m + 3 terrain samples per triangle + mesh upload | main | `PlacemakerOverlaySystem.cs:484-516,731-754`, `FilledPreviewSystem.cs:90-100,166-200` |
| 8 | UI push: `StepStatuses` / `Unready` (`DesignEditor.AllUnready`), `Availability.For`, `Modified` (two `RecipeJson.WriteStep` fingerprints), `UiModels.Design`, `UiModels.Summary` (pricing, legend, park stats), JSON strings into ValueBindings, then the React re-render | main | `PlacemakerUISystem.cs:131-230` (PushDesign `:203`), `PlacemakerToolSystem.Plan.cs:312-349`, `.Design.cs:51-71` |
| 9 | `DecalOpacitySystem.OnUpdate`: **up to 24 clones per frame**. Each clone: `SurfaceAsset.LoadProperties` (may read the asset stream), `AssetDatabase.user.AddAsset`, `CopyFrom`, 2 × `AddPrefab` | main | `DecalOpacitySystem.cs:122-151,180-268` |
| 10 | ≤120 frames later: `AssetCatalogSystem.OnUpdate` sees a new prefab count → **full `Scan()`** (section 1) → `_version++` → steps 5 (re-resolve), 6 (second full ghost), 8 (every requested `assets` category re-serialised in `PushAssets`; `AssetLabelOf` cache cleared) | main | `AssetCatalogSystem.cs:84-98,383-475`, `PlacemakerUISystem.cs:295-325` |
| 11 | Every 120 frames, whatever the state: `UpdateIcons` re-queries `ImageSystem.GetIcon` for every non-UI item (thousands) | main | `AssetCatalogSystem.Icons.cs:89-102` |

Obstacle gathering (`BlockObstacles.Gather`) and site capture run **only when the outline changes**, not on a type/preset switch
(**[P]**: every "obstacle set gathered" line follows an outline edit and takes 0.4–6.4 ms, M:1775,1788,2389,2765,2952,2955,
E:1773-1780). The terrain and zoning inputs therefore come from the cached `SiteContext`.

## 3. Log evidence

### 3.1 Every catalog rebuild during play follows new decal clones [P]

`made N see-through copies` is a **cumulative** count (`_made` is never reset, `DecalOpacitySystem.cs:59,132,143`), so the new
clones per switch are the differences.

| Log | Clones made (new) | Scan's palette line | Gap | Scan tail (palettes → vN, same frame) | Catalog |
|---|---|---|---|---|---|
| M:1790 12:09:50.089 | 6 (+6) | M:1791 12:09:56.826 | 6.7 s | 428 ms | v12 (M:1974) |
| M:2391 12:10:55.940 | 8 (+2) | M:2392 12:11:00.415 | 4.5 s | 244 ms | v15 (M:2575) |
| M:2577 12:11:01.198 | 14 (+6) | M:2578 12:11:06.291 | 5.1 s | 335 ms | v16 (M:2761) |
| M:2764 12:11:07.241 | 16 (+2) | M:2767 12:11:12.131 | 4.9 s | 295 ms | v17 (M:2950) |
| E:1796 11:45:05.402 | 8 | E:1797 11:45:10.510 | 5.1 s | 265 ms | v12 (E:1980) |
| E:2273 11:46:06.225 | 11 (+3) | E:2274 11:46:10.587 | 4.4 s | 242 ms | v13 (E:2457) |
| E:2489 11:46:17.820 | 12 (+1) | E:2491 11:46:20.027 | 2.2 s | 246 ms | v14 (E:2674) |

All 17 scans in both logs have a measured tail of **124–637 ms**, and every one of them is a single blocked frame: `Scan()` is one
synchronous call from `OnUpdate`. The prefab loop runs before the palette line, inside the same call, and is not timestamped.
The gap from the clone to the palette line is the wait for the next 120-frame check (≤ 2 s at 60 fps) plus that loop. The gaps
of 2.2–6.7 s therefore leave **roughly 0.2–4.7 s for the loop**. Also measured offline: the pure Core classification the loop
runs per prefab costs 8.7 µs warm per call on .NET 9 (4033 real catalogued names × 6 trait sets, 212 ms). That excludes the ECS
lookups, `ImageSystem.GetIcon` ×2 and the localisation lookup that `Traits` / `ResolveIcon` / `DisplayName` add per prefab, and
it covers only names that are already catalogued, not every PrefabData entity.

Other rebuilds come from other prefab registrations: v11 after "loaded 0 groups" (M:1589→1590), v12 (E:2226) after a park
companion prefab (E:2042), and v2–v10 during loading. A build, a companion or a clone each cost a full scan.

### 3.2 The switching session (M:2389–2960)

```
12:10:55.683 obstacle set (new outline)      M:2389
12:10:55.912 declutter (plan 1 landed)        M:2390   → +2 clones M:2391
12:11:00.415–00.659 CATALOG SCAN v15           M:2392–2575   (≥244 ms frame + loop)
12:11:01.198 +6 clones                         M:2577
12:11:06.291–06.626 CATALOG SCAN v16           M:2578–2761   (≥335 ms frame + loop)
12:11:07.216 declutter (switch)               M:2763   → +2 clones M:2764
12:11:10.078 declutter (switch)               M:2766
12:11:12.131–12.426 CATALOG SCAN v17           M:2767–2950   (≥295 ms frame + loop)
12:11:13.238 / 16.994 / 23.706 / 26.739 declutter (switches; no new tiers → no scan)
12:11:29.728 ghost 23                          M:2960
```

The ghost revision went from 6 (M:1978, 12:09:58) to 23 (M:2960, 12:11:29): 17 ghost rebuilds in 90 s. Each plan and each
catalog/asset bump costs one Clear frame plus a full re-emission.

## 4. Offline measurements (Core per type × preset)

Harness: `.forensics/scratch-switchhang/Bench.cs`. It runs every shipped recipe and fill type × default and every built-in preset
(the gallery's job list without feature combos, 301 variants), sequentially, single thread, warm (second run). It uses the real
catalog fixture plus the player's catalog-dump ids on three sites:
* **default**: the gallery's irregular site at the type's target area,
* **street3ha**: a 3 ha block with streets all round and captured zone grids,
* **slope3ha**: the same block on "hilly" ground.

The Composer is timed stage by stage, followed by every main-thread Core step the tool runs when the plan lands. All numbers are
.NET 9 on a Ryzen 7800X3D (the player's CPU). **[H]** Unity's Mono JIT is typically 2–4× slower for this kind of code. Results:
`full.csv` / `full2.csv`.

| Site | Compose (worker) median / p90 / max | Main-thread Core per plan median / p90 / max |
|---|---|---|
| default | 13 / 32 / 128 ms | 2.2 / 6.6 / 27.6 ms |
| street3ha | 22 / 79 / 151 ms | 2.9 / 9.5 / 24.1 ms |
| slope3ha | 29 / 95 / 176 ms (531 ms outlier in run 1, plaza/cafe) | 3.3 / 10.0 / 42.4 ms |

**Slowest types** (max over presets, ms, run 2):

| Type | Default site | Street | Slope | Main-thread max |
|---|---|---|---|---|
| park | 48 | 151 | 176 | 12 |
| plaza | 59 | 112 | 161 | 2.5 |
| shopsPark | 19 | 95 | 144 | 7.8 |
| mallDistrict | 20 | 130 | 139 | 2.1 |
| outdoorMall | 20 | 94 | 134 | 8.4 |
| animalPark | 128 | 43 | 121 | 22.6 |
| stripMall | 15 | 72 | 105 | 3.7 |
| beach | 40 | 26 | 88 | 42.4 |

Commercial and parking types on the street site compose in 18–130 ms and need 0.8–6.3 ms of main-thread Core.

**Hot spots inside Compose** (sum over 903 runs / max): generators 18.2 s / 146 ms, decal Dressing 3.9 s / 36 ms, SiteHarmony
3.0 s / 77 ms (213 ms in run 1, slope plaza), ZoneGuard 2.7 s / 37 ms, TerrainHeat 0.5 s / 18 ms, Cutouts 0.2 s / 8.5 ms. All of
this runs on the worker.

**Main-thread Core per plan** (max): overlay paint raster **39.5 ms** (beach, adventurePlayground, communityGarden: the only item
above 16 ms), Declutter 9.1–13.7 ms, Summary JSON 12.9 ms, overlay EarClip + subdivision 11.4 ms (up to **46 903 triangles**,
2 990 rings; the game adds 3 terrain samples per triangle and a mesh upload), AutoAssets 4.4 ms, PlanWork.Flatten 4.5 ms, Design
JSON 2.1–8 ms. Readiness, availability, pricing and fingerprints each stay below 4 ms.

**Catalog dump** (offline, the player's real 6.4 MB / 8128-row dump, Newtonsoft from the game's `Managed` folder): build JObject
79–144 ms + indented serialise 69–142 ms + write 6 ms = **187–237 ms per scan** on .NET 9. That is most of the in-game 124–637 ms
scan tail.

**Decal clones needed per plan** (distinct decal × tier below 100 %): median 8, max 86 (scrapyard / landfill / abandonedLot /
urbanRuins). 4 246 (asset, tier) keys over all street-site variants. Switching through presets keeps finding new ones, and each
new one means one `CreateClone` plus a full catalog scan.

## 5. Main-thread work above 16 ms on a switch (ranked)

| Rank | Item | Size | Status |
|---|---|---|---|
| 1 | `AssetCatalogSystem.Scan` triggered by our own clone prefabs | 124–637 ms timed tail + untimed loop (≈0.2–4.7 s) per scan, 2–6 s after the switch | **[P]** |
| 2 | `catalog-dump.json` rebuilt and written on every scan (`MaybeWriteDump(true)`) | 187–237 ms (.NET), inside the scan tail | **[P]** |
| 3 | Second full ghost rebuild per scan (catalog version → `_assetsResolvedStamp` → ghost revision) plus the first rebuild per plan: Clear frame, all nets in one frame, 800 objects per frame through the game's Temp/validation pipeline | not timed; est. 30–150 ms per emit frame, 2–3 frames per rebuild | chain **[P]**, cost **[H]** |
| 4 | Decal clone creation, 24 per frame: LoadProperties, AddAsset, 2 × AddPrefab, then prefab initialisation and batch setup the next frame | not timed; est. 5–20 ms per clone | **[H]** |
| 5 | Overlay paint raster for paint-heavy types | ≤40 ms .NET, est. ≤80–160 ms in game | **[P]** offline |
| 6 | Overlay meshes: EarClip + subdivide + terrain sampling + upload | ≤11 ms .NET plus ~140 k height samples | **[P]/[H]** |
| 7 | `ResolveAutomatic` (AutoAssets + DecalFit + Declutter + PrewarmSeeThrough with one file rewrite per new clone) | ≤14 ms .NET | **[P]** offline |
| 8 | UI after a plan or catalog bump: Design + Summary JSON (≤15 ms .NET), `PushAssets` re-serialising every requested category (decal list 887 items, surfaces 533) on every catalog version, React re-render | not timed | **[H]** |
| 9 | `SwitchRecipe` → `ApplyAndSave` (Apply + async settings save) on every type switch | small | **[H]** |
| 10 | `UpdateIcons` recheck every 120 frames over all non-UI items | small, periodic | **[H]** |

## 6. O(n²) and repeated full passes

* **Whole-catalog rebuild for a delta** (O(all prefabs) per added prefab batch): the structural bug. The catalog should be built
  once per load, and later additions should be applied incrementally.
* **Catalog clones as catalog items**: 133 decal clones and the "Placemaker Overlay (…)" surface clones are classified as
  pickable assets. They grow every pool and picker list, and each one changes the content signature (`SameContent`), so the
  version goes up.
* `BuildAutomatic`: `Candidate()` (two `EntityManager.HasComponent` calls plus allocations) runs inside every sort comparison →
  O(n log n) ECS lookups per category per scan.
* `FilledPreviewSystem`: EarClip is O(n²) per ring. Fine for today's rings (≤11 ms for 2 990 rings), but long curved
  outlines would grow quadratically. `Signature()` hashes every ring vertex of every layer **every frame**.
* `AssetLabelOf` scans a whole category linearly per label. It is cached per catalog version, and every rebuild clears the cache.
* `SaveRemembered` sorts and rewrites the whole list once per newly remembered clone.

No O(n²) hot spot in Core's main-thread steps reached 16 ms in the sweep. Declutter stayed ≤14 ms with 2 016 objects.

## 7. The fix (in order of payoff), with expected timings

1. **Catalog built once; own prefabs never trigger a rescan.**
   * `DecalOpacitySystem`, `SurfaceCloneSystem`, `ParkCompanionSystem`, `ParkingCompanionSystem` and `OwnerAreaPrefabSystem` report
     what they add (`AssetCatalogSystem.NoteOwnPrefabs(n)` → `_lastPrefabCount += n`), or the recheck ignores prefabs whose name
     starts with our prefixes.
   * Clones are **not** catalog items. `ResolveName` maps to them through the clone systems, which already exist for this.
   * A real count change from other mods' late loads gets an **incremental** scan of only the new entities (track the highest
     entity index, or `PrefabData` with a change filter). If a full rescan is ever needed, time-slice it at about 1 500 prefabs per
     frame into a new structure and swap it in at the end.
   * Expected: **0 scans per switch**; scan cost during play ≤ ~5 ms per frame.
2. **Survey dump off the critical path.** Write it only when the setting is on and only once per session (or on demand). Snapshot
   the data on the main thread (≈ the list we already have) and serialise and write on a `Task`. Expected: −190–640 ms per scan.
   Also default `WriteSurveyDump` to off for players.
3. **No ghost rebuild without a content change.** `ResolveAssets` bumps `_assetsResolvedStamp` only when a hash of the resolved
   asset ids (and decal clone choices) actually changed. `GhostRevision` bumps on the plan's content hash, not on stamps. Expected:
   one ghost rebuild per switch instead of two or three.
4. **Gentler ghost emission.**
   * `GhostPerFrame` 800 → ~200, and nets spread over 2–3 frames where junctions allow (nets of one connected component together).
   * While the player is clicking through presets (input changed < 0.4 s ago), hold the ghost and show the sketch
     (`PreviewModeMachine` already has the "Dragging" path; treat preset/type clicks the same, latest wins).
   * Expected: ≤10–15 ms per emit frame, and no Clear frames for intermediate presets.
5. **Clone work bounded and batched.**
   * 2–4 clones per frame instead of 24.
   * `SaveRemembered` deferred to one write per second, or to tool close or build.
   * Prewarm, time-sliced at load, the tiers every shipped preset uses (the bench can list them: 4 246 keys on the street site,
     far fewer distinct tiers × assets after dedup across sites). Then a switch rarely needs a new clone.
6. **Plan-landing work moves to the worker.**
   * The worker request carries an immutable `AutoAssetSource` snapshot (pools and palettes are replaced wholesale on scan, so a
     reference copy is safe). `AutoAssets.Resolve`, `DecalFit.Orient` and `Declutter.Run` run after `Compose` on the worker.
   * The overlay's paint preview rects and the 2D triangulation and subdivision are also computed on the worker into a
     `PreviewGeometry` on the result. The main thread only samples heights and uploads meshes, or samples the captured `HeightAt`
     grid on the worker as well.
   * Main thread on plan landing keeps only the clone requests and the stamps. Expected ≤3 ms (from up to 42 ms .NET / ~100 ms Mono).
7. **Cache per (outline version, recipe fingerprint without seeds/markers, layout options, catalog version).** Keep a small LRU
   (8 plans, already resolved, with their preview geometry). The fingerprint code already exists (`PlacemakerToolSystem.Design.cs:66-71`).
   Switching back to a preset seen before then costs 0 ms of planning.
8. **Debounce the expensive consumers, not the planner.** Compose stays immediate (it cancels stale plans). Ghost emission, UI
   `design`/`summary` pushes and `PushAssets` wait 150–250 ms of quiet after a type/preset click. `PushAssets` re-serialises a
   category only when its content hash changed.
9. Small items:
   * no `ApplyAndSave` per type switch (save `LastRecipe` on close),
   * `UpdateIcons` recheck time-sliced (≤300 items per frame),
   * `FilledPreviewSystem.Signature` cached per plan revision instead of hashed every frame.

**Expected after 1–6:** a type/preset switch costs ≤3 ms on the main thread in the click frame and ≤3 ms when the plan lands. The
ghost is spread over frames of ≤15 ms. The plan is visible after the worker time (median 19 ms, p99 140 ms .NET; ≈ 0.05–0.5 s in
game, shown with the existing spinner). No multi-second freeze remains.

## 8. Proving it in game, and reproducing the bench

* **Instrumentation (needed to turn the [H] rows into [P]):**
  * `AssetCatalogSystem.Scan` logs its prefab count and loop / palettes / automatic / dump ms.
  * `BeginGhost` / `EmitGhostBatch` log ms and definitions per frame.
  * `PumpPreview` logs resolve / overlay ms.
  * A frame watchdog logs any Placemaker system `OnUpdate` above 16 ms with its name and the current tool action.
* **In-game test scenario** (fits the user's "stripped-down game + test suite" idea, see `00-user-feedback-1215.md`): on a fixed
  3 ha test area, click through every type × preset by trigger every 0.5 s. Record the max frame time, the catalog scans and the
  ghost rebuilds per switch to JSON. Assert max frame ≤ 50 ms, 0 scans and ≤1 ghost rebuild per switch.
* **Bench:** `dotnet build .forensics/scratch-switchhang/SwitchBench.csproj -c Release`, then
  `SwitchBench.exe <repo> out.csv [types|-] [default,street3ha,slope3ha]`. The dump timing is in `scratch-switchhang/dump`, the
  classifier timing in `scratch-switchhang/classify`. Results: `full.csv`, `full2.csv` (both git-ignored).

## Skeptic review

Reviewer: second investigator, read-only. Repo HEAD is `d9f6df6`, but `git diff --stat 650be0f HEAD` is empty for `Game/Assets`,
`Game/Build/DecalOpacitySystem.cs`, `Game/Preview`, `Game/Tool` and `Game/UI`, so the code cited above is the code that is installed.
"Nothing is installed" is **CONFIRMED**: `git status` shows only `.gitignore` and `docs/teardown/`.

| # | Claim | Verdict | Evidence |
|---|---|---|---|
| 1 | Compose runs off the main thread, latest request wins | **CONFIRMED** | `PreviewWorker.cs:55-99` (Task.Run, cancels the previous token, drops stale revisions at `:94`). |
| 2 | Bench: median 19, p99 140, worst 176–531 ms; main thread 1–42 ms | **CONFIRMED** (offline .NET only) | Recomputed from `full2.csv`: median 18.9, p99 134, max 175.9; main thread median 2.6, max 42.4. Run 1 (`full.csv`) also has a **273 ms street3ha** case that the table leaves out. The Mono cost in game is not measured. |
| 3 | A clone calls `AddPrefab`, the prefab count changes, and a full `Scan()` follows | **CONFIRMED** | `DecalOpacitySystem.cs:224,261`. Decompiled `Game.Prefabs/PrefabSystem.cs` `AddPrefab` creates the `PrefabData` entity at once (`EntityManager.CreateEntity` and then `SetComponentData(PrefabData)`). The check is at `AssetCatalogSystem.cs:84-92`. All 4 clone batches on master (M:1790, 2391, 2577, 2764) are followed by a scan 4.5–6.7 s later. |
| 4 | Scans are caused by "a decal at a **new** see-through tier" | **WRONG (framing)** | All 16 clones of the session were **already remembered**. `decal-opacity.txt` has 133 lines, mtime 2026-10-14 23:58, and was not rewritten in either session. A script check found 16 of 16 dump clone ids listed in it. The startup prewarm (`RequestRemembered`, `DecalOpacitySystem.cs:126,160-178`) made **0** clones in both sessions: the first "made" lines are M:1790 (12:09:50) and E:1796 (11:45:05), with 0 failed. **[H]** The cause is the 600-frame retry cap (`:126`), which runs out before the asset-pack decals load. The decal count grows 164 → 767 between catalog v1 (M:162, 12:07:17) and v7 (M:1040, 12:08:17). So the scans **come back every session**. |
| 5 | Copies become catalog items: "133 in the dump" | **WRONG count** | `catalog-dump.json` (6 512 095 B) holds **16** distinct `Placemaker Decal …` ids in 57 category rows, and 68 distinct `Placemaker…` ids in total. 133 is the line count of `decal-opacity.txt`. That clones become catalog items and bump the version is **CONFIRMED** (v15, v16 and v17 each follow a clone batch). |
| 6 | Timed tail of 124–637 ms "in all 17 scans" | range **CONFIRMED**, count **WRONG** | There are **33** scans: 16 on master and 17 in the 11:52 log. The clone-triggered tails in play are 242–428 ms. |
| 7 | Untimed prefab loop ≈ 0.2–4.7 s | **WRONG upper bound** | The scan after load gives a tighter bound. `Placemaker loaded 0 groups` (M:1589, 12:08:47.318) is logged by `GroupRegistrySystem.PostLoadRepair` in ModificationEnd on the first frame after load. `AssetCatalogSystem` had `_scanRequested` set in `OnGameLoadingComplete`, and `ModificationSystem` runs before `UIUpdateSystem` in the main loop (decompiled `Game.Common/SystemOrder.cs:60,67`). Its palette line follows at M:1590, 12:08:48.836. So the loop takes **≤ 1.52 s** (≤ 1.42 s at E:1587→1588). **Each freeze is therefore ≤ ~2 s** (loop plus 0.24–0.43 s tail), not 5 s. The rest of each 2.2–6.7 s gap is the wait for the 120-frame recheck. |
| 8 | The 6.4 MB dump is rewritten on every scan and is most of the tail | **CONFIRMED, smaller than claimed** | The dump's `written` = 19:11:12.335Z (12:11:12.335 local) falls between the palette line at 12:11:12.131 and v17 at 12:11:12.426. In game, serialising and writing take **≤ 91 ms**, and the JObject build fits inside the 204 ms before that. The dump is therefore ≤ ~180 of the 295 ms tail. The offline 187–237 ms overstates the write. |
| 9 | Each scan rebuilds the ghost a second time | chain **CONFIRMED**, "each" **WRONG** | `PlacemakerToolSystem.cs:281` (ResolveAssets every frame), `Review.cs:33-39`, `Ghost.cs:56-60`, `BuildService.Ghost.cs:163-167,222-256`. Exception: the rescan at E:2043→2226 kept **v12** (`SameContent`), so it caused no version bump and no ghost rebuild. Only scans whose content changes cause the rebuild. |
| 10 | "17 ghost rebuilds in 90 s" | **UNPROVEN** | These are revision bumps, not emissions. The 0.25 s debounce (`BuildService.Ghost.cs:55,167`) merges quick bumps. The "ghost N:" line is logged only when parts were rejected or dropped (`:297`), so the log cannot count emissions. |
| 11 | "3 times in 18 s of switching" | **WRONG (attribution)** | The window contains outline edits. Obstacle sets, which are gathered only on selection changes, appear at M:2389 (12:10:55.683), M:2765 (12:11:08.980) and M:2952 (12:11:12.840). The first clone batch (M:2391) followed a **new outline**, not a switch. The log never records type or preset switches. "declutter" lines appear only when something moved or was dropped (`AssetCatalogSystem.cs:171-172`), so they are not reliable switch markers. |
| 12 | Other rebuilds come from builds and companions | **CONFIRMED** | v13 (M:1990→1991) and v14 (M:2203→2204) follow builds, and the surface count goes 533 → 534 → 535. |
| 13 | Overlay paint takes 39.5 ms | **CONFIRMED** offline | `full2.csv`: beach slope3ha 39.48 ms. The cost in game is not measured. |
| 14 | About 3 ms per switch after the fixes | **UNPROVEN** | This is only a .NET extrapolation. By the report's own estimate, Mono is 2–4× slower. |

### Corrected conclusions

* **Proven:** registering our own prefabs makes `AssetCatalogSystem` rescan synchronously, blocking one frame for **≤ ~2 s**:
  the loop takes ≤ 1.5 s and the tail 0.24–0.43 s, which includes up to ~180 ms of dump work. The prefabs that do this are decal
  clones on their first use in a session, and build-time surface, lot and companion prefabs. A content change in a scan also
  re-resolves assets and rebuilds the ghost.
* **New finding:** the existing startup prewarm is broken (0 of 133 remembered clones were made at load in both sessions). Every
  session therefore repeats the clone-then-scan hitches, and fixing the prewarm removes them without new design. Fix 5 should read
  "make `RequestRemembered` retry after `OnGameLoadingComplete` (or until the pack prefabs exist)", not "add a prewarm".
* **Still a hypothesis:** that these scans are *the* hang the user felt on switching. The log cannot tie them to switches, and
  ghost re-emission costs are not measured. The §8 instrumentation (per-frame ms for Scan, BeginGhost and PumpPreview, plus a switch
  log line) is needed before ranking fixes 3–8.
* Fix order stays 1 (no scan for own prefabs) and 2 (no forced dump), with the prewarm repair added next to them.
