# Teardown 02: forensics of the 2026-10-15 session (11:34–11:54)

Sources (all in `.forensics/`):
* `Placemaker-20261015-1152.log`: the mod log, 3489 lines, 11:34:25 → 11:52:15. Referenced below as `L<line>`.
* `Player-20261015.log`: the Unity log, 584 lines, runs a little past the mod log (to ~11:54). Referenced as `P<line>`.
* `group-{0,1,2,5,6}-20261015-*.json`: the build audits. Groups 3, 4 and 7 have no file because the audit writes one only when it has entries (`BuildService.Audit.cs:365`). Failed builds never write one.

Code lines are quoted **at the installed commit `4d62ea8`**, exported with `git archive 4d62ea8` (read-only). "HEAD" means `650be0f`.
Decompiled game: `%TEMP%\pm-decomp\game`.

Labels used below:
* **[P] proven**: read directly from the log, JSON or code.
* **[I] inference**: follows from the evidence but is not logged directly.
* **[H] hypothesis**: needs an offline reproduction.

---

## 0. Executive summary

1. **[P] 5 of 13 build attempts failed outright, and the message named the wrong cause.** All five end with `build rejected by the game: InvalidShape` (L2485, L2702, L2731, L2760, L3028). Each one spent all 6 exclude-and-retry rounds (`MaxAutoExcludeAttempts = 6`, `BuildService.cs:50`). The errors left at the end were **1 object error without an icon** (5 in the 349-part build), not a net. "InvalidShape" is only the type of the *first notification icon* found (`BuildService.Adopt.cs:110-121`). The InvalidShape icons on nets (`Invisible Road Path - 2xTwoway` ×8/×15, `Alley` ×3, `Invisible Car Path - 1xTwoway` ×2) were never among the error entities the loop could act on. In every round, every anarchy tally is an object category, and no net was ever left out ("nets 0").
2. **[P] Before failing, each loop deleted the plan's own pieces:** 56 of 221 parts (stall markings included) in each of the four 221-part attempts, and 142 of 349 in the 349-part attempt. Each **failed** attempt also left its terraform in the city: the next attempt "reached its target in 0 pass(es)" (L2680, L2709, L2738).
3. **[P] Shops were lost after Apply, at the zoning re-snap, not in the plan.** In every shop build (groups 1–4) the plan sent to Build contained the cutout plots (20/12/8/8 cells in 5/4/4/4 plots). `ZoneGuard.Refresh` (run 40 frames after Apply) found no live cells for 3 of 9 plots in groups 1–2 and for **all 8 plots of groups 3–4**. For those plots it still emitted the *original* quad as Marquee zoning (`ZoneGuard.cs:451`), and the live result was 0 cells.
4. **[P] Groups 3 and 4 lost all their shops without a word in the audit.** `VerifyZoning` returns before the per-plot audit when no planned cell survives the refresh (`BuildService.Terrain.cs:646`). The result: no "plot … differs" lines, no warning, audit `left out 0` (L2787, L2811), and no JSON file. Both behaviours are **unchanged at HEAD** (`Terrain.cs:646`, `ZoneGuard.cs:467`).
5. **[P] "planned 0 cutout cell(s)" in groups 5, 6 and 7 means the plan had no zoning plots at all** ("0 planned cell(s) in 0 plot(s)", L3053/L3261/L3474). That is a property of those types (parking lots and a park). It is not a lost shop.
6. **[P] There is no Placemaker exception in Player.log.** It holds only Placemaker WARN lines that duplicate the mod log, plus errors from other mods (UltraPainter, C2VM settings) and messages from AutoIndustryArea.
7. **Effect of the uninstalled commits:**
   * `ff46338` alone would very likely have let all 5 failed builds and the decal losses of groups 1–2 through in round 1 (all their error tallies become ignorable).
   * `s59` (`6c76383`/`650be0f`) removes the loop, the stall-marking drop, the wrong-cause message path and the hidden-object losses.
   * **None of the three** fixes the silent shop loss of groups 3–4, the raw-quad emission, the preview-vs-live zoning divergence, the undone-terraform leak or the audit gaps. `s60` (`ed57b50`/`98cb703`) *might* recover some plots (§5.4).

---

## 1. Session timeline: every ghost and every build

The log never records the **type/preset** of a build (gap G1, §7). The types below are inferred from layers, parts and zoning, and marked [I].
"Area" is the terraform raster (`raster W×H @ 1.75 m`), which is the outline's bounding box plus a margin. Where it exists, the zone-blocking m² (footprint minus cutouts) is also given.

| # | time | group | inferred type [I] | ghost (parts / rejected / optional dropped) | area | parts built (verified line) | retry rounds | errors (by type → prefab) | outcome | zoning (preview cells → refreshed; plots) |
|---|---|---|---|---|---|---|---|---|---|---|
| A | 11:44:22 | 0 | road grid ("Roads", hillside terraces; loose build) | ghost 26: 65 / 2 / 0 (L1781) | capture box 748×782 m (L1780) | edges 64 (L1788) | 1 (L1784) | SteepSlope ×2 → net `Small Road` ×2; anarchy `no-plan-object x2` (L1783) | **built, partial**: planned 65, built 63, left out 2 `Small Road` (L1790; JSON group-0) | n/a |
| B | 11:45:19 | 1 | park with shops (12 layers, leaves/puddle decals, park companion, 5 EU Commercial Low plots) | ghost 32: 162 / 33 / 0 (L1983) | 62×54 → ≈108×94 m; paint 5960 m² | objects 116, areas 4, edges 20, nodes 16 (L2028) | 5 (L1992–L2004) | OverlapExisting 36→4 on `Placemaker Decal 15 - G87 … Puddles`, `… Dead Leaves8`, `… Weeds01`; InvalidShape ×1 + OverlapExisting ×2 on net `Pavement Path` in every round | **built, partial**: planned 162, built 138, left out 24 (22 decals + 2 zoning plots), "+16 kept" (L2033) | 20 cells / 5 plots → 12 cells. Plots 0 (1145,-1331) and 2 (1162,-1263): preview 4 → planned 0 → live 0 (L2021, L2023). 3 plots zoned (12/12). Buildings already on plots 1 and 3 |
| C | 11:45:41 | 2 | same type as B (park with shops) | ghost 34: 158 / 19 / 0 (L2230) | 63×52 → ≈110×91 m; paint 6750 m² | objects 128, areas 5, edges 13, nodes 11 (L2257) | 1 (L2237) | OverlapExisting ×12 on decals (`… Puddles 093`, `… Leaves8`, `… Leaves5`) | **built, partial**: planned 158, built 145, left out 13 (12 decals + 1 plot), "+9 kept" (L2262) | 12 cells / 4 plots → 8. Plot 3 (1174,-1138): 4 → 0 (L2254). Other 3 plots zoned (8/8) |
| D | 11:46:15 | – | commercial lot with shops on an Alley (StreetShops `Alley` + parking aisles + stall markings) | ghost 37: 221 / 100 / 7 (L2460) | 66×66 → ≈115×115 m | – | 6 (L2466–L2482) | round 1: InvalidShape ×13 (net `Invisible Road Path - 2xTwoway` ×8, `Alley` ×3, `Invisible Car Path - 1xTwoway` ×2); OverlapExisting ×64 (`ParkingLotDecal01` ×42, `GreenBushWild02` ×6, `FlowerBushWild01` ×5) | **FAILED** `InvalidShape (4 markers)` (L2485–2486); left out 26+15+5+4+3+3 = 56 before failing, 15 of them stall markings/chargers/arrows (L2469) | never reached zoning |
| E | 11:46:27 | – | same plan as D (ghost 39, L2677) | 221 / 100 / 7 | same | – | 6 | identical to D | **FAILED** (L2702). Terraform 0 passes: ground already graded by D (L2680) | – |
| F | 11:47:03 | – | same (ghost 40, L2706) | 221 / 100 / 7 | same | – | 6 | identical | **FAILED** (L2731) | – |
| G | 11:47:09 | – | same (ghost 41, L2735) | 221 / 100 / 7 | same | – | 6 | identical | **FAILED** (L2760) | – |
| H | 11:47:48 | 3 | small shop lot (1 layer, 3 zoneable road edges = Alley/connectors, parking carrier, 4 plots of 2 cells on `Small Road`) | none logged | 42×50 → ≈73×87 m; blocking 1994 m² | objects 29, areas 9, edges 3, nodes 2 (L2784) | 0 (no anarchy line) | none | **built**: planned 41, built 41, left out 0 (L2787) | **preview 16 cells**; Refresh counts 8 in 4 plots → **0** (all 4 plots 2→0, L2776). 4 raw quads emitted (L2783), live result unknown, **no per-plot audit, no verification line** |
| I | 11:48:39 | 4 | same type as H | none logged | 44×52 → ≈77×91 m; blocking 1944 m² | objects 28, areas 9, edges 3, nodes 2 (L2809) | 0 | none; WARN `terraform incomplete (73/750 cells still off after 4 passes)` (L2795) | **built**: 40/40, left out 0 (L2811) | same as H: 16 → 8 in 4 plots → **0** (L2801). 4 raw quads (L2808). Silent |
| J | 11:49:22 | – | larger commercial/parking lot (parking aisles, stall markings, bushes) | ghost 53: 349 / 219 / 2 (L3004) | 66×68 → ≈115×119 m | – | 6 (L3010–L3025) | InvalidShape ×15 → net `Invisible Road Path - 2xTwoway` ×15 (constant). OverlapExisting 176→19 (`ParkingLotDecal01` ×128, `GreenBushWild02` ×13, `FlowerBushWild01` ×10) | **FAILED** `InvalidShape (15 markers)` (L3028–3029); left out 50+1+53+19+12+7 = **142 of 349** before failing (the 53 in round 3 are stall markings, see §3.1) | – |
| – | 11:50:54–57 | – | two outlines being designed | ghosts 55–58: 299–314 / 190–212 / 2–7 (L3033–L3042); 2× "skipped a surface ring the game would reject" | – | – | – | – | preview only | – |
| K | 11:51:11 | 5 | parking lot (122 stalls, chargers, StreetlightDouble02/Single02, London plane trees, bushes) | (ghost 55/56 earlier; the plan was re-captured at 11:51:06) | 66×66; blocking 5620 m² | objects 284, areas 21, edges 14, nodes 11 (L3056); 122/122 stall markings (L3051) | 0 | none at validation | **built, partial**: planned 319, built 295, left out 24 **hidden by the game after Apply** (143 hidden, 119 shown again, 24 stay hidden, L3058) | plan has **0 plots** ("planned 0", L3052–3053) |
| L | 11:51:38 | 6 | parking lot (as K) | (ghost 57/58) | 66×66; 5620 m² | objects 267, areas 19, edges 15, nodes 12 (L3264); 121/121 stalls | 0 | none | **built, partial**: 301 → 273, 28 hidden (127 hidden, 99 shown again) (L3266–3267) | 0 plots |
| M | 11:51:58 | 7 | park with parking (4 layers, park companion, attractiveness 12) | none logged | 68×64; 6253 m² | objects 304, areas 3, edges 18, nodes 19 (L3477) | 0 | WARN `1 of 325 planned parts did not materialise` (L3469) | **built**: audit claims 325/325, left out 0 (L3482). Wrong: 1 part was missing (§6, S10) | 0 plots |
| N | after 11:52:15 | ? | ? | – | – | – | – | P584 `6 of 320 planned parts did not materialise; continuing.` | unknown (the mod log copy ends at L3489) | – |

Counts [P]:
* 13 build attempts in the mod log (A–M), plus one more visible only in Player.log (N).
* **5 failed** (D, E, F, G, J).
* **8 finished**. Of those, 6 left something out: A (2 roads), B (22 decals + 2 plots), C (12 decals + 1 plot), K (24 hidden), L (28 hidden), H/I (all shops, not reported). Only M is reported as clean, and it is not clean (1 part dropped).

Where the shops went, for the user's "no shops in the red circles":

| build | plots shown in the preview | plots zoned live |
|---|---|---|
| B | 5 | 3 |
| C | 4 | 3 |
| H | 4 | 0 |
| I | 4 | 0 |
| D–G, J (failed) | presumably shop plots, never reached | none |

**[P] 9 of 17 previewed shop plots in the finished builds were never zoned. 8 of those 9 left no trace in any audit.**

---

## 2. Why zoning says "planned 0" while the preview showed shops

### 2.1 Which code writes these lines and what "planned" counts [P]

| log line | code (4d62ea8) | what the numbers are |
|---|---|---|
| `zoning refresh: N live zone block(s) …; X cutout cell(s) (planned Y) …, Q zoning quad(s)` | `BuildService.ZoneBlock.cs:106` (`DoZoneRefresh`) | **Y** = `plan.ZoneCells` with `State == Zoned` *before* the refresh (`:87-88`): the cells the Design preview showed. **X** = the same count *after* `ZoneGuard.Refresh` rewrote `plan.ZoneCells` against the live blocks (`:92-99`). **Q** = the quads that will be painted (snapped quads plus unsnapped raw quads). |
| `zoning refresh (s54): P planned cell(s) in K plot(s) -> F on the live blocks; …; MISMATCH (plots #i a->b) #i: live cells in/near it: o open, b blocked, …` | `Core/Build/ZoneGuard.cs:444` (`Refresh`) | **P** = zoned cells whose centre lies inside one of `plan.ZoneQuads` (`:376-381`, first quad wins). **F** = cells the live blocks give those quads. The `#i` causes come from `Diagnose`. |
| `zoning plot q at (x,z) <zone>: preview a, planned b, live zoned c, buildings d  <-- differs` | `BuildService.Audit.cs:332` (`AuditZonePlots`, called from `VerifyZoning`) | **a** = preview cells in the plot quad grown 1.5 m (snapshot `SnapshotPreviewZoning`, `:301-307`). **b** = cells left in the plot *after the refresh*. **c** = those cells that are really zoned in the game (`state == 4`). **d** = buildings standing on the plot. |
| `zoning: N plot(s) shown in the preview but not zoned as shown` | `BuildService.Audit.cs:339` | Plots with `preview > live` or `preview > 0 && planned == 0`. Each one also becomes an audit entry `zoning`. |
| `zoning: group G: zoned z of n cutout cell(s) …` | `BuildService.Terrain.cs:705` (`VerifyZoning`) | n = cells after refresh. **Skipped entirely when n = 0** (`:646`). |

So in the per-plot lines, **"planned 0" does not mean the plan sent to Build had no zoning.** It means the post-Apply re-snap (`ZoneGuard.Refresh`) gave that plot no live cells.

### 2.2 Did the plan sent to Build contain zoning plots? [P]

* **Groups 1–4: yes.**
  * Group 1: "20 planned cell(s) in 5 plot(s)" (L2011). Every plot has `preview 4` (L2021–2025), which comes from the snapshot of the plan's `ZoneCells` taken before the refresh.
  * Group 2: 12 cells in 4 plots (L2243).
  * Groups 3 and 4: "planned 16" cells (L2775, L2800), of which 8 lie inside the 4 plot quads (L2776, L2801).
  * The plans also carried zone prefabs: the zoning step found `EU Commercial Low` for every quad, and there is no "zone prefab not found" warning.
* **Groups 5, 6, 7: no.** "0 planned cell(s) in 0 plot(s)" (L3053, L3261, L3474). Zoning still ran its steps because `GuardsZoning` is also true when there are only `ZoneBlockers` (`ZoneBlock.cs:60-62`). These are the "planned 0" lines quoted in the brief. Nothing was lost there. Those types do not plan shops [I: parking lots and a park, by their parts].

### 2.3 Where the shops were lost [P unless marked]

1. **Design time:** `ZoneGuard` snaps the plots to the zone blocks captured *before* the build. The preview draws `plan.ZoneCells` (`PlacemakerOverlaySystem.cs:648-663`).
2. **Apply:** the build's own nets (paths, lanes, alleys, connectors) split the city's road edges and block cells. The game rebuilds the blocks: group 1 reports "4 on a moved grid (road edges split by the build)" (L2010). Groups 3 and 4 report "6/12 of the build's own roads" carrying live blocks (L2775, L2800).
3. **40 frames later, `ZoneGuard.Refresh`** re-snaps each plot by growing it (up to 4 m) and shifting it (±4 m). Plots that still get no cells:
   * group 1, plots #0 and #2: "4 open, 6 blocked" and "6 open, 7 blocked" near them (L2011);
   * group 2, plot #3: "6 open, 12 blocked" (L2243);
   * groups 3 and 4, all plots: "2–6 open, 18–32 blocked" (L2776, L2801).

   These plots are **not dropped**, because open cells exist nearby, which fails the drop condition (`ZoneGuard.cs:431-432`). They are not snapped either. Their **original design-time quad is emitted as Marquee zoning** (`ZoneGuard.cs:451`, `if (!r.Snapped[i]) zoning.Add(... quads[i] ...)`). Proof in group 1: refreshed "plot 3 at (1145,-1331)" and "plot 4 at (1162,-1263)" (L2015–2016) are exactly audit plots 0 and 2 (L2021, L2023), appended last as `!Snapped`. Painting that quad zoned **0** live cells (`live zoned 0`).
4. **Groups 3 and 4 only:** after the refresh, `plan.ZoneCells` has 0 zoned cells. `VerifyZoning` therefore returns at `Terrain.cs:646` *before* `AuditZonePlots`. Consequences:
   * no per-plot lines;
   * no "shown in the preview but not zoned" warning;
   * no "zoned x of y" line;
   * the zone blocking log says "keeping 0 cutout cell(s) free" (L2781, L2806), so the blockers cover the plot land [I, from `plan.ZoneBlockers = r.Blockers` at `ZoneGuard.cs:452`];
   * audit `left out 0`, so no JSON file.

   The user saw every shop of two builds vanish while the mod reported a clean build.

[H] **Why the re-snap fails.** The open cells near the lost plots exist, but `Compute(plan.Footprints, …)` does not take them. Candidates:
* they lie outside the footprint;
* they are not two rows deep (`MinRows = 2`, `ZoneGuard.cs:71`);
* they are a lone column that the game's depth rule would block (`CellOverlapJobs` port, `ZoneGuard.cs:1048-1056`).

The 2-cell plots of groups 3 and 4 are below the plan's own minimum of 2 columns × 2 rows (`ZoneGuard.cs:71`, `:722`), so they could never have held a 2×2 zoned lot [H]. Also unexplained: groups 3 and 4 previewed 16 zoned cells, but only 8 centres lie inside the 4 plot quads (L2775 vs L2776). Half the preview's shop cells belonged to no plot [H: duplicate cells on overlapping or predicted blocks, or quads shrunk past cell centres]. `ZoneGuard` is pure Core code, so all of this can be replayed offline once the live grids are dumped (§8).

### 2.4 Structural cause [P by design, from the code above]

The preview computes zoning on the **pre-build** zone blocks. The build then changes those blocks (its own nets split edges and block cells). The result is repaired afterwards by a heuristic, and when the heuristic fails the plot's shops silently disappear. This breaks "preview must equal the result" for every type that plans zoning cutouts (`ZoneGuard` is shared by all of them).

---

## 3. The failed builds in detail (D, E, F, G, J)

### 3.1 The loop [P]

Code: `DoValidate`, `BuildService.cs:485-538` (4d62ea8). The sequence:

1. `ClassifyOwnCollisions` (`BuildService.Audit.cs:136-181`) marks as ignorable only errors on **our planned objects** whose icon type is OverlapExisting/NotEnoughClearance **and** which overlap another planned object. Everything else is "real":
   * `no-plan-object` (nets, our owner lot);
   * `no-error-icon` (the partner of a pair: the game puts `Error` on both, the icon on one; see `ValidationSystem.ProcessError`, decomp `Game.Tools/ValidationSystem.cs:1535-1566`, and s59.md);
   * `overlaps-nothing-planned`;
   * `touches-building`.
2. If any real error remains, `ExcludeErroneousItems` (`Adopt.cs:220-282`) maps errors to plan items (exact match, then 2 m probes, then 6 m / 12 m object-only fallback). It leaves them out, re-emits everything (`DoResetForRetry`, `:533-551`) and validates again.
3. Stall markings, chargers and arrows (`PlanWork.Excludable`, `Core/Build/PlanWork.cs:177-183`) are kept in rounds 1–2. From round 3 they are excluded when matched exactly (`Adopt.cs:291`: `exact && _excludeAttempts >= 2`). Otherwise they go through the `_dropFunctional` fallback, which logs "the game still rejects N stall marking(s)… leaving them out" (`BuildService.cs:510-518`).
   * This is why J lost **53 parts in round 3** (L3016) after only 1 in round 2.
   * It is why D–G lost **15 stall markings/chargers/arrows** in round 2 (L2469).
4. After 6 rounds: `build rejected by the game: {_errorTypeName}` (`BuildService.cs:528`), then `BeginCleanup(Failed)` → `build Failed: Placemaker.Build.Error.<type>` (`:673`).

### 3.2 What the final errors really were [P]

Last anarchy tallies before each failure:
* D–G: `2 of 3 error(s) ignorable; the rest stay real: no-error-icon x1` (L2484, L2701, L2730, L2759).
* J: `9 of 14 …: no-error-icon x5` (L3027).

So each failure was caused by **1 (or 5) of our own objects** that carried an `Error` but no icon. These are the partners of self-overlapping pairs.

The tally categories add up to the error-entity total in every round. For example L2465: 43 + 27 + 7 = 77, and every category is an object category (`no-plan-object` is never tallied in D–J). So **no net was in the `Temp+Error` set in any round**, and in fact "nets 0" appears in every left-out line. The constant `InvalidShape×13` / `×15` on nets comes from `ErrorHistogram()` (`Adopt.cs:134-186`). That function counts **notification icons** whose owner is (or was) a temporary.

The failure label "InvalidShape" is `_errorTypeName`, the type of the *first* icon in the icon query (`Adopt.cs:110-121`). It is not the type of the errors that stopped the build.

[H] **Why the InvalidShape icons do not match error entities.** Most likely they belong to temp edges the game replaced or deleted. `BelongsToTemporary` accepts an icon whose owner still `Exists` with `Temp`, and does not check `Deleted`. The error query excludes `Deleted` (`BuildService.cs:156-160`). The game's InvalidShape on an edge is a geometry fold check (`Game.Net/ValidationHelpers.cs:1371-1390`, `CheckGeometryShape` on edge/node geometry, `ErrorSeverity.Error`).

In any case, **the reported cause was not the cause.** Earlier diagnosis effort aimed at `Invisible Road Path`/`Alley` shape was probably misdirected [I].

### 3.3 Side effects of a failed build [P]

* **Terraform is not undone.** The first attempt (D) reached its target in 1 pass (L2463). E, F and G each "reached its target in 0 pass(es)" on the same outline (L2680, L2709, L2738): the ground was already graded. `BeginCleanup` only deletes the brush *definitions* (`DiscardTerraformBrushes`, `Terrain.cs:456-461`). The "direct brush" terraform has already been applied to the live heightmap.
* No audit JSON is written for failed builds (`FinishAudit` only runs on Finish).
* The user retried the same plan 4 times (D–G) with an identical, deterministic outcome each time: the same tallies, the same 56 exclusions and the same failure.

---

## 4. Player.log [P]

* **No exception or stack trace involves Placemaker.** No line contains a `Placemaker.` frame. The Placemaker lines are WARN copies of the mod log (P105–109, P408, P562–584).
* P584 `Placemaker: 6 of 320 planned parts did not materialise; continuing.` is a build after the mod-log copy ended (row N). Its outcome is not in the evidence.
* Other mods in the session:
  * `UltraPainter` failed to initialise (`MissingMethodException`, P174).
  * `C2VM.TrafficToolEssentials` settings parse error (P47).
  * `AutoIndustryArea` (P≈553, Portuguese: "could not identify the building's original opening; area preserved") ran twice, near builds K and L.

  None is shown to interact with Placemaker. AutoIndustryArea edits areas near buildings [H: worth excluding in a clean profile].
* Startup WARNs (cosmetic): 5 duplicate locale keys (L5–9) and the missing icon `Media/Game/Icons/Pedestrian.svg` (L19).

---

## 5. Failure signatures: builds affected, code path, and effect of the uninstalled commits

`ff46338` = "Anarchy: any error on our own planned objects is ignored unless it touches an existing building" (`Audit.cs` classifier only).
`s60` = `ed57b50` (merged in `98cb703`): zoning refresh. `s59` = `6c76383` (merged in `650be0f`): one validation pass, no loop.

| # | signature (log text) | builds | emitted at (4d62ea8) | ff46338 | s60 | s59 |
|---|---|---|---|---|---|---|
| S1 | `build rejected by the game: InvalidShape (N markers)` + `build Failed: Placemaker.Build.Error.InvalidShape` | **5** (D, E, F, G, J) | `BuildService.cs:528`, `:673`; label `Adopt.cs:110-121`; loop limit `:50`, `:505-526` | **Changes (very likely removes).** `no-error-icon` → treated as OverlapExisting and `overlaps-nothing-planned` is dropped. In D–J every tally is one of those or already ignorable, with no `touches-building` and no `no-plan-object`, so round 1 would clear all errors [I from tallies] | no | **Removes**: loop, classifier and this failure path are gone; anarchy applies everything (s59.md) |
| S2 | `left out N rejected parts (…); retrying.` | **8** (A, B, C, D–G, J); 36 rounds in total | `BuildService.cs:522`; `ExcludeErroneousItems` `Adopt.cs:220`; `Exclude` `:289` | **Reduces.** B and C would lose 0 decals (their tallies are `no-error-icon` only). A still loses 2 `Small Road` (SteepSlope nets = `no-plan-object`) | no | **Removes**; SteepSlope roads are built anyway |
| S3 | `the game still rejects N stall marking(s)/charger(s)/arrow(s) … leaving them out` | **4** (D–G); J lost stall markings silently in round 3 | `BuildService.cs:517`; `Adopt.cs:291-294` | removed for these builds [I] | no | **Removes** (`_dropFunctional` deleted) |
| S4 | `anarchy: X of Y error(s) ignorable; the rest stay real: no-error-icon / overlaps-nothing-planned / no-plan-object` | 8 builds, every round | `Audit.cs:176` (classifier `:136-181`) | **Changes** the categories | no | **Removes** (replaced by `Placemaker validation: …`, `BuildService.Verdicts.cs`) |
| S5 | `survivors: N object(s) … hidden by the game (Overridden): R shown again …, H stay hidden` → audit `hidden` | **2** (K: 24, L: 28 stay hidden: FlowerBushWild01, GreenBushWild02, NA_LondonPlaneTree01, StreetlightDouble02/Single02, ParkingLotCharger01) | `Audit.cs:239`; restore requires `SelfOverlaps(w)` (`:215`) | no | no | **Changes**: HEAD `Audit.cs:149-155` restores every hidden planned object unless it touches a building |
| S6 | `zoning refresh (s54): … MISMATCH`; `zoning plot q …: preview 4, planned 0, live zoned 0 <-- differs`; WARN `N plot(s) shown in the preview but not zoned` | **2** (B: 2 plots, C: 1 plot) | `ZoneBlock.cs:106-107`; `ZoneGuard.cs:444`; `Audit.cs:332`, `:339`; raw-quad fallback `ZoneGuard.cs:451` | no | **Might change**: `NearestOpenRun` (HEAD `ZoneGuard.cs:428-444`, `:486-516`) moves a 0-cell plot onto the nearest open run of ≥2 columns × 2 rows within 3 cells. "4 open"/"6 open" near B and C plots may or may not form such a run [H] | no |
| S7 | **silent**: `0 cutout cell(s) (planned 16)`, `8 … in 4 plot(s) -> 0`, `keeping 0 cutout cell(s) free`, `4 zoning quad(s) emitted`, then nothing; audit `left out 0` | **2** (H, I) | early return `Terrain.cs:646`; raw quads `ZoneGuard.cs:451` | no | **Partially at most**: may move some plots (needs a 2×2 open run; these plots planned 2 cells). **Does not fix the silence**: HEAD still returns at `Terrain.cs:646` and still emits raw quads (`ZoneGuard.cs:467`) | no |
| S8 | `0 cutout cell(s) (planned 0) … 0 zoning quad(s)` / `0 planned cell(s) in 0 plot(s)` | 3 (K, L, M) | `ZoneBlock.cs:106-107` | – | – | – (not a failure: the plan has no plots) |
| S9 | audit mislabel / double count: group 1 JSON has 22 decals `rejected:InvalidShape`, but their icons were OverlapExisting (L1992–2004; InvalidShape was on `Pavement Path`); 8 items listed both `anarchy` (kept) and `rejected` at the same spot | 1 (B) | fallback to the round's first icon type, `Audit.cs:126-130`, used by `Adopt.cs:231`; kept-then-excluded `Audit.cs:170` + `Adopt.cs:296` | no | no | **Changes** (Exclude/Classify removed; new verdict audit) |
| S10 | WARN `N of M planned parts did not materialise; continuing.`, but audit `left out 0` | **2** (M: 1/325; N: 6/320, Player.log only) | `BuildService.cs:470-472`. Never added to `Audit`; `Built = Planned − LeftOut` is arithmetic (`Audit.cs:362`) | no | no | no |
| S11 | WARN `terraform incomplete (73/750 cells still off after 4 passes)` | 1 (I) | `Terrain.cs:264` | no | no | no |
| S12 | terraform persists after a **failed** build (next attempt "0 pass(es)") | 3 observed (E, F, G) and likely all 5 fails | `BeginCleanup` → `DiscardTerraformBrushes` `Terrain.cs:456-461` (definitions only) | no | no | no (but fails become rare) |
| S13 | ghost `N/N parts, R rejected, K optional objects dropped (collisions)` showing 33–219 red parts | all ghosts (26, 32, 34, 37–42, 53–58) | `Ghost.cs:296` | no | no | **Changes**: under anarchy the ghost is untinted and non-optional (s59.md §1) |

Also seen once each, harmless: "skipped a surface ring the game would reject" (L3035, L3041) and "Smart edges fence built" (design).

---

## 6. Proven defects outside the build pipeline that the audits hide

1. **Shop loss is not counted** when all of a build's plots fail. See S7: `Terrain.cs:646`, still present at HEAD.
2. **Parts that never materialise are not counted** (S10). M reports "built 325, left out 0" one line after "1 of 325 planned parts did not materialise" (L3469 vs L3482).
3. **"built" is planned − left out** (`Audit.cs:362`). It is not a census of entities in the city.
4. **Failed builds leave no audit and no undo** of the terraform (S12).
5. **The audit can contradict itself** (S9): kept and rejected for the same object, and an error type taken from another part.
6. **The log never names the type/preset or the outline**, and only sometimes logs the ghost before a build (none for H, I, M). So these forensics have to infer what the user was building.

---

## 7. Gaps in the evidence (what to add so the next session answers itself)

* **G1:** log the type id, preset and outline size in `build started` and in every audit JSON.
* **G2:** log the post-refresh outcome per plot even when 0 cells remain (`Terrain.cs:646`).
* **G3:** dump the live `ZoneGrid`s captured at refresh (and at design time) into the audit JSON. Then `ZoneGuard.Refresh` can be replayed offline in xUnit against the real data (`ZoneGuard` is pure Core).
* **G4:** dump the plan (`ComposedPlan` → JSON) with each audit, so `BuildVerdicts`/`Declutter`/`ZoneGuard` can be re-run headlessly against the exact plan the user built.
* **G5:** log the error entities (kind, prefab, position, severity) rather than icons, and filter `Deleted` owners out of the icon histogram.

---

## 8. Offline verification hooks this evidence enables (no game launch)

* **Zoning (S6/S7):** a Core test that feeds `ZoneGuard.Refresh` the plot quads and live grids from a dump (G3), and asserts that every plot shown in the preview either gets ≥ its planned cells or is reported. The H/I situation (2-cell plots, 2–6 open cells nearby, 18–32 blocked) can be constructed by hand today from the logged Diagnose counts.
* **Validation (S1–S4):** the game's own `Game.Objects.ValidationHelpers` / `Game.Net.ValidationHelpers` and `ValidationSystem.ProcessError` are in the managed DLLs. A test host that loads `Game.dll` can run the overlap/shape checks on the plan's pieces and show which pairs get `Error` without an icon (the `no-error-icon` population). s59's `BuildVerdicts` is already pure and tested.
* **Audit honesty (S9/S10):** unit tests on `BuildAudit` that "built" equals the census, and that materialisation drops and zero-cell plots appear as entries.

---

## 9. Bottom line for "what changed?"

The session ran `4d62ea8`, which **has** the exclude-and-retry loop, the s53 classifier and the s54 refresh, and **lacks** `ff46338`, `s60` and `s59`. Every build failure today matches the mechanism described in s59.md:
* our own pieces flagged against each other;
* the classifier unable to read the icon-less partner;
* 6 rounds of deletion;
* a failure whose label comes from an unrelated icon.

Installing s59 addresses that class.

The **shop** complaint is a separate mechanism that s59 does not touch: the design-time zoning snapped to pre-build blocks, the build re-shaping those blocks, and a post-Apply re-snap that can give a plot nothing and then hide it from the audit. s60 adds one more re-snap heuristic, but keeps both the raw-quad fallback and the silent early return.
