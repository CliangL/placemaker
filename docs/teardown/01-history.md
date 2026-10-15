# Teardown 01 — History forensics: "What changed since it worked?"

Scope: how the BUILD pipeline and the parking / commercial (shops, strip mall, lot-first) / zoning features evolved from
the first game-layer commit (10-13 12:12) to master (`650be0f`, 10-15 09:17), which state "just worked", why, and what
differs today. Read-only investigation (git history, playtest docs, today's logs, decompiled 1.6.2f1, ParkingLotTool source).

Legend: **[P]** = proven (code/log/decompile cited), **[H]** = hypothesis (supported, not proven).

---

## 0. Short answer

1. **The project is two days old.** First commit 10-13 11:38, first game-layer commit 10-13 12:12. "A couple of days ago"
   can only mean 10-13 evening / 10-14 morning. There is no commit in which parking *and* shops really worked in game.
2. **What "worked" then was partly an illusion [P]:**
   * Flat parking lots took the **instant build from the ghost**. The ghost emits objects with `CreationFlags.Optional`. The game's
     `ValidationSystem.CancelOptional` silently cancels **both** optional pieces of a colliding pair, and the instant build adopted
     the ghost as it was. Lots built "fine" with most of their pieces gone. FEEDBACK-4 has "553 parts, 480 optional objects
     dropped". s25 logged `ghost 71: 1823/1823 parts, 0 rejected, 1095 optional objects dropped` → `tagged 796`, and the user saw
     **no stall lines at all**.
   * Until `6e2e5fc` (10-13 22:39), **every exclude-and-retry built a doubled set** (RESEARCH-NOTES-ADDENDUM "s4b").
   * Until `b585f8e` (10-14 09:32), builds **demolished existing buildings** (the game's `Warning` becomes `Deleted` on Apply). s15:
     "when it builds, it destroys the buildings".
   * Shops: until s14 `0d513fb` (10-14 09:58), the street-shop zoning had an empty `ZoneId` and **zoned nothing in game** (s14 §1).
     From s14 until s19 `fb13787` (10:38), cutouts were plain Marquee quads with no blockers and no snapping. Shops grew, but
     buildings also grew **over the plaza** (s19 playtest).
3. **The deciding change in how pieces reach the game [P]:** Placemaker has always emitted every piece as a separate top-level
   `CreationDefinition` through its own tool and then **gated Apply on the game's `Error` flags** (`f7fa1c1`). The game's Apply
   never reads `Error` (`Game.Tools.Apply{Objects,Net,Areas}System` have no reference to it). ParkingLotTool applies with no error
   gate (§6). So Placemaker refused things the game would have built. For a day the refusal was hidden: optional pieces were
   cancelled silently and the retry loop dropped pieces quietly. Once s25 (`eb0f1f3`, 10-14 14:31) made stall markings
   **non-optional and non-excludable** and lots got **graded terraform** (which disables the instant path), the loop met pieces it
   could not remove. Builds began to fail ("60 parts overlap").
4. **Today's session (11:34–11:53) ran `4d62ea8` (DLL 02:26) [P]:** 14 builds, 5 failed. All 5 failures are the 6-round loop
   ending on **InvalidShape nets that the loop never excluded** (`nets 0` in every round): parking aisles
   `Invisible Road Path - 2xTwoway` ×15, and commercial `Alley` ×3 + connectors. The 4 successful shop/commercial builds zoned
   **0 of 16**, **12 of 20** and **8 of 12** cells. Zoning *causes* were not logged by that build (cause logging is s60).
5. **New finding from today's log [P]:** the same Parking type validated with **zero errors** at two sites. Group 5 had
   330 temporaries and 122 stall markings; group 6 had 313 and 121. Groups 3, 4 and 7 (41–344 parts) also had zero. At another
   site the same type had 190 errors and failed. So "our own pieces always collide pairwise" — the premise of s25, s53 and s59 —
   **does not hold** in the played build. Errors depend on the site and plan: InvalidShape aisles, decals, and alleys or
   connectors at the street (§7.2).
6. **Install state:** the 11:34 session ran `4d62ea8`, so it had none of `ff46338`, s60 or s59. While this report was being
   written, `Mods/Placemaker/Placemaker.dll` changed to a **09:18** timestamp (= master `650be0f`, merged 09:17), and the game
   restarted at **12:06**. No build has run on master yet, so **master's fixes are unverified in game**.

---

## 1. Sources and caveats

* Git: `C:\Users\jack\cs2-area-tool`, 545 commits, master `650be0f`. Commit list used: `git log --reverse`.
* Today's mod log: `.forensics/Placemaker-20261015-1152.log` (3489 lines, 11:34:25–11:52:00).
  * The live `Logs/Placemaker.log` also held an 11:53:33 build (5 layers, `tagged 332`, no validation errors, no ghost line
    before it).
  * **That file was overwritten when the game restarted at 12:06.** The .forensics copy, plus the excerpt quoted here, is all
    that remains.
* `.forensics/group-*-20261015-11*.json` are **0-byte copies**. The originals are intact in
  `%LOCALAPPDATA%Low\Colossal Order\Cities Skylines II\ModsData\Placemaker\audit\` (e.g. `group-5-20261015-115113.json`:
  planned 319, built 295, 24 × reason `hidden`).
* Unity log: `.forensics/Player-20261015.log`. It has no Placemaker exceptions. Lines 564–575 show "the game still rejects 15 stall
  marking(s)… leaving them out instead of failing" followed by "build rejected by the game: InvalidShape".
* **Confounders in the user's game [P]:** the logs show ParkingLotTool, Platter and Yenyang Anarchy loaded in the same session.
  * ParkingLotTool: `ParkingLotTool.Mod.log` written 11:52.
  * Platter: Anarchy log 11:35:25, "Found Platter.Components.ParcelPlaceholderData". s19 §2 notes that Platter runs its own copy
    of the vanilla cell-check pipeline.
  * Yenyang Anarchy: PreventOverride, RemoveOverridden and DisableToolErrors systems.
  * Also loaded: Move It, EasyZoning, DrivewayEntries and others.
  * None of these are present in any offline test.

---

## 2. How a piece reaches the game (master, for reference)

`BuildService` (Game/Build) drives an invisible `BuildToolSystem` (vanilla `ToolBaseSystem`):

1. **Prepare.** Flatten the plan (`PlanWork.Flatten`) and resolve prefabs.
2. **Record and carrier.** Create the record and a **bare carrier** entity.
   * Since s58 `44b32fe` every build gets a carrier, with the PrefabRef of the first object prefab.
3. **ActivateTool** (Clear frame).
4. **Terraform.** Direct `TerrainSystem.ApplyBrush`, permanent at once.
5. **Paint.** Terrain material brush, also permanent.
6. **EmitNets.** Every course goes out in one frame as a `CreationDefinition` + `NetCourse`.
7. **EmitRest.** Surfaces, areas and objects as `CreationDefinition`s (decals sunk to −0.01 m).
8. **WaitTemps.** The census matches temporaries to the plan.
9. **Validate.** Read the game's `Error`/`Warning`/`Override` flags.
   * ≤ 4d62ea8: exclude-and-retry, up to 6 rounds.
   * master: one pass, `BuildVerdicts`.
10. **TagTemporaries.** Add `PlacemakerMember` tags. Parking bays get `Owner+Attached(carrier)`. Overridable props get
    `Owner(carrier)` (s55/s58). This happens **after validation, before Apply** — not "after Apply" as s59 says
    (`4d62ea8:Game/Build/BuildService.Adopt.cs:366,407-422`).
11. **OwnerLot.** One "Placemaker Area" lot with `m_AllowOverlap`, non-physical, **not the Owner of any member**. It is emitted
    after validation with its own error handling (`d0ce7a6`).
12. **Apply.** The game's `ToolApplySystem` turns `Warning` into `Deleted` and `Override` into `Overridden`. It ignores `Error`.
13. **Settle and charge.** Charge the nets.
14. **ZoneRefresh.** Capture the live zone blocks and re-snap the plan's cutouts (`ZoneGuard.Refresh`).
15. **RoadPreZone.**
16. **ZoneBlock.** Emit invisible **physical** "Placemaker Zone Block" lots over the footprint minus the cutouts. This is its own
    validate/apply with ≤ 3 retry rounds (`Game/Build/BuildService.ZoneBlock.cs:41,257`).
17. **ZoningWait** (24 frames), then **ZoningEmit.** Marquee zoning quads, no Overwrite.
18. **Finish.**
    * Verify the members.
    * Run `AuditSurvivors`, which un-hides `Overridden` objects.
    * Request the park companion: a hidden Tiny Park clone **building** at the road frontage (s17 `3929b85`).

---

## 3. Timeline of architecturally significant changes

Each row records the change to how pieces reach the game, and what was observed afterwards.

| sha / time | Change in how pieces reach the game | Observed effect (doc / log) |
|---|---|---|
| `f7fa1c1` 10-13 12:12 | **First game layer.** Per-piece top-level `CreationDefinition`s through our own tool. Apply gated on `Error` (`DoValidate`: any `Error` → fail). `AutoExcludeCollisions`: exclude and retry ≤ 4 times, default **off**. Parking: drive aisles are invisible nets; stall decals get `Owner+Attached` to a bare carrier whose `SubNet` holds the aisles (PLT model). | FEEDBACK-1 (13:30): "Builds 1+2 failed with InvalidShape … Build 3 succeeded (476 members)"; "built result looks broken"; "not bulldozable as one thing". |
| `378b624` / `0e9cfe0` 10-13 13:46–13:51 | **Owner lot "Placemaker Area"** (`m_AllowOverlap = true`, not physical, **not the Owner of members** — gamebuild.md §6 "decision"), with a Harmony raycast lift and bulldoze confirmation. **`RecoverRejectedPieces` default ON**: map errors to plan items by exact match or 2 m proximity, exclude, re-emit, ≤ 6 rounds. Clear existing trees/props in the same transaction. Ring repair (`AreaRings`). | "One bulldozable thing" (US7). Builds stop failing on single errors. Pieces disappear without a visible trace. |
| `f08ce83` 10-13 13:52 | `NetRole.Alley` → vanilla "Alley". Footpaths may attach to road ends. | — |
| `ee16b30` 10-13 16:42 | **Ghost preview** with the same emission code. Ghost objects are `CreationFlags.Optional`. **Instant build from the ghost** when the ghost is shown, complete, unchanged, **with no terraform and no paint** (`4d62ea8:…Ghost.cs:195-199`). | FEEDBACK-4 (20:46): "553 parts, 480 optional objects dropped (collisions)". Lots look built; most pieces are gone. |
| `af1edd2` 10-13 21:04 | **Direct terraform** (`TerrainSystem.ApplyBrush`). Permanent the moment it runs, before validation. | — |
| `6e2e5fc` 10-13 22:39 | Fixed **doubled builds**: `_stepFrame` meant the first-frame branches never ran, so every retry kept the old temporaries and applied both sets. Adds a duplicate invariant. | ADDENDUM s4b: "Before this fix every auto-excluded retry built doubles". |
| `6fe146f` 10-13 22:53 | **Loose builds.** Road grids become ordinary city roads (no owner, no tags). | — |
| `d0ce7a6` 10-13 22:58 | Owner lot emitted **after** validation and never refused (overlap accepted, shape → simpler rings, else cancelled). ADDENDUM s4c already says: "Apply is gated only by our tool; the Apply systems build errored temporaries anyway." | The team knew Apply ignores `Error` 34 h before s59 removed the gate. |
| `b585f8e` 10-14 09:32 | **SiteSafety** (plan: drop what touches building lots). **ProtectBuildings**: a live building with `Warning` → exclude the touching items and retry. | s15: the user's earlier build "destroys the buildings". |
| `0d513fb` (s14) 10-14 09:58 | **Zoning cutouts** with a real `ZoneId` (before this, nothing was zoned). **Commercial category**: Alley / pedestrian lanes joined to existing streets, Parking-planner lots with alley-stub entrances (lane split at the stub), shop plots zoned by Marquee quads. No blockers. | Shops grow, including over the plaza (s19 playtest). |
| `fb13787` (s19) 10-14 10:38 | **ZoneGuard** snaps cutouts to the captured road cells (≥ 2 rows). **"Placemaker Zone Block"**: invisible **physical** lots emitted after the main Apply over the footprint minus the cutouts (own ≤ 3-round retry). | s26 (14:56): "zoned 4 of 16 cutout cell(s) … blocked 12". Our paths split street edges, the grid moved, and the blockers covered the cutouts. |
| `3929b85` (s17) 10-14 11:22 | **Companion park building.** A hidden Tiny Park clone (a real building) at the best road frontage, created after the build. | s22: "The park companion sat in a cutout" (fixed by obstacle points). |
| `cc4bf56` (s21) 10-14 11:46 | **Decal overhaul.** Much more dressing (oil, tyres, stains, leaves). **Decals sunk −0.01 m** so they "take no space" — marked "Needs in-game confirmation" (`Game/Build/BuildService.Emit.cs:30-40`). | Today: `OverlapExisting` on `Placemaker Decal 15 – …` decals ×12–36 per build. Sunk decals still collide, at least with each other: two sunk decals share the Underground mask [H]. |
| `a330350` (s23) 13:53, `533f85f` (s28) 15:25, `1f025b4` (s37) | **Runtime prefab clones** of decals per opacity tier ("Placemaker Decal 15 – …"). | s37: a '%' in clone names "threw every frame". |
| `eb0f1f3` (s25) 10-14 14:31 | **Stall markings, chargers and arrows never Optional and never excluded** by a neighbour's error (only after 2 rounds, if exact). **Lot grading** (`LotGrading`, sloped terraform polygons) **on by default**. Any terraform → **no instant ghost path**. `MallGrading` for commercial. | s59 table: "the first pieces the loop could not remove → refusals start ('60 parts overlap', playtest 77)". |
| `f353081` (s26) 10-14 15:30 | **ZoneRefresh** after Apply (40 frames): live blocks are re-captured and the plan re-snapped. **ZoneBlockPredictor** (port of `BlockSystem.CreateBlocks`, free ends only) for the build's own lanes. | Preview cells become a *prediction*; the build zones what the live grid allows. |
| `932cea8` (s29) 15:34 | Lot edges tied to the adjacent roads' heights. | — |
| `a256036` (s32) 16:38 | Commercial pass: lane networks (T/X junctions, loops), more types. | More nets and junctions near the frontage. |
| `9092bac` (s36) 18:39 | Port of the game's column depth rule (`CellOverlapJobs.ReduceDepth`): a lone column is blocked, so lone columns are widened or dropped. | s36 playtest: "zoned 0 of 2 … row 0 with row 1 blocked". |
| `9550e00` (s40) 20:41 | **Zoning-first.** Street plots *are* the predicted cells, and the host plans around them (clearance 2.5 m). | — |
| `aec7f2c` 21:37 | Retry: when no rejected spot matches, leave out objects within **6–12 m**. | Silent holes ("empty patch", s59 screenshot 97). |
| `c693be6` (s44) 21:48 → `2b97af4` 22:04 | Stall markings sunk → **reverted** ("sunk markings were rejected (3 of 18 built); plain markings built 401/401 before", `Emit.cs:43`). Kept: a **safety net** that drops functional pieces when only they remain. | — |
| `fce5f5b` (s47) 22:24 | Alley stubs long enough for their nodes (net InvalidShape model). Noted: "parking planner's own aisles … also short for their nodes … left alone". | Today: InvalidShape on `Alley` ×3 and parking aisles ×15 persists. |
| `64ec0e5` (s48) 22:34 | `ZoneLots.Clearance` **2.5 → 0.2 m**: host nets and content flush to the cells. | [H] Junction node geometry now reaches cells the model calls open. s60 adds `NodeReach` 1 m for exactly this ("7 blocked cells beside a park's connector"). |
| `7c884ae` (s50) 10-14 23:35 | **Lot-first commercial.** Lots first, access alleys, polygon lots on leftovers. Merged as "WIP: some share targets unmet". | More connectors and alleys at the frontage. |
| `5a7e7e3` (s53) 23:57 | **"Anarchy" v1**: ignore an error only if the piece overlaps *another planned object*. Stall marking **scaled 0.928**. Build audit (log, JSON, "Left out" chip). Ghost: self-overlapping objects non-optional. | The pair partner (no icon) and nets stay "real", so the loop still fails (s59 numbers). The ghost now shows red "rejected" counts. |
| `f253ba3`/`66a57cc`/`3caff19` (s54/56/57) 10-15 00:06–00:33 | Refresh grows and shifts plots to match live cells. Plots with no room are **dropped**. Helper lots are not obstacles. | — |
| `29879d0` (s55) 00:51 | **Shared carrier owner** for props on parking builds (`Owner(carrier)` at tag time). `AuditSurvivors` (un-hide own-overlap `Overridden`). Declutter relaxed under anarchy. Pit guard. | Today groups 5/6: 143/127 hidden after Apply, 119/99 shown again, **24/28 stay hidden**. |
| `44b32fe` (s58) 01:31 | **Every** build gets a carrier, and all its overridable objects get `Owner(carrier)`. | Today: "116 props/trees/chargers share the parking carrier owner" on a non-parking build. |
| `0ce8946` (s58) 01:56 | **Kerb apron** for dead-end lot connectors (street height, graded strip). | [H] More connector geometry at the kerb, where shop cells start. |
| `4d62ea8` 10-15 02:26 | Pit guard leaves Roads alone. | **The build the user played 08:52–09:07 and 11:34–11:53.** |
| `ff46338` 08:58 *(not played)* | Anarchy: ignore any error on our planned objects unless it touches a building. | — |
| `ed57b50`/`98cb703` (s60) 09:04 *(not played)* | Refresh moves empty plots onto the nearest open live run. Walkway ends close cells (`NodeReach`). **Cause logging** of blocked cells before refresh (`BuildService.ZoneCause.cs`). | — |
| `6c76383`/`650be0f` (s59) 09:17 *(not played)* | **One validation pass, anarchy builds everything.** The retry loop is removed. `NeutralizeVerdicts` strips `Warning`/`Override`/`Error` from our temporaries before Apply. Existing buildings lose `Warning` in every mode. The ghost is non-optional and untinted under anarchy. New gate: `_tool.OriginalDeleted`. | Untested in game. |

Code growth, from `git ls-tree` line counts:

| area | 10-14 00:18 (`a4c1e7b`) | 10-15 02:26 (`4d62ea8`) |
|---|---|---|
| Game/Build | 3,821 | 6,026 |
| Core/Build | 4,278 | 10,945 |
| Commercial | 0 | 4,348 |

`git diff --stat a4c1e7b 650be0f` over the build/parking/commercial/decal folders: **+18,876 / −299 lines**.

---

## 4. Parking path — how it evolved

| | 10-13 night (e.g. `a4c1e7b`) | 10-14 after s25 | 4d62ea8 (played today) | master `650be0f` |
|---|---|---|---|---|
| Path on flat ground | **instant from ghost** (adopts the ghost's temporaries) | normal; with grading: always normal | normal | normal |
| Stall markings | optional in the ghost → cancelled in pairs; excludable by 2 m proximity | non-optional, non-excludable | same + scaled 0.928 + safety-net drop after 2 rounds | same, never dropped |
| Validation gate | Error → exclude/retry ≤ 6 | same; retry meets non-excludable pieces | same + s53 classifier (partner / net errors stay real) | **none for collisions/shape** (anarchy); fails only on money, OriginalDeleted, Duplicated, WouldDemolish |
| Terraform | none on lots | graded tiers (LotGrading) | + edge ties, pit guard, kerb apron | same |
| Decals | dressing modest, not sunk | s21 dressing + sink; s25 calmer | + see-through clones | same |
| Props' owner | none | none | `Owner(carrier)` at tag time | same |
| After Apply | — | Zone Block lot (s19) over the lot | Zone Block + AuditSurvivors (own overlaps only) | un-hide everything unless it touches a building |
| Owner lot | after validation (`d0ce7a6`) | same | same | same |

**Why parking "worked" then [P]:** fewer pieces, no terraform (so the instant path applied), and the game's `CancelOptional`
removed every colliding pair silently. The build reported success with markings missing. s25 is the commit that made this
visible: markings became non-optional, so they could no longer vanish, and they could no longer be excluded.

**Why parking fails in the played build [P]** (11:49:22–24, log lines 3005–3028):

1. Ghost 53 showed 349 parts and 219 rejected.
2. The build ran 6 rounds. Its histogram held **`InvalidShape×15 [net 'Invisible Road Path - 2xTwoway' ×15]` unchanged in every
   round**, with `nets 0` left out each round. The loop only removed objects, among them stall markings after round 2:
   ParkingLotDecal01 errors went 128 → 81 → 32 → 20 → 11.
3. Then "build rejected by the game: InvalidShape (15 markers)".

Why the aisles never matched a plan item, so they could never be excluded, is unexplained [H]: the errored edges are pieces the
plan index did not recognise. The same plan type built at 11:51:11 elsewhere with **zero errors**, 122/122 markings
(§7.2).

**After a "successful" parking build [P]** (groups 5/6, 11:51): `survivors: 143 … hidden … 119 shown again, 24 stay hidden
[FlowerBushWild01 …; StreetlightDouble02 …]`. These are props the game hid against something other than our objects — our aisle
nets or the physical Zone Block lot [H]. The played build counted them as "left out"; master un-hides them.

---

## 5. Commercial / shops / zoning — how it evolved

| step | sha | mechanism | in game |
|---|---|---|---|
| street shops | `a701ccf` 10-13 16:40, s12 `8200700` 10-14 08:19 | shop regions + `PlannedZoning` with `ZoneId = ""` | **nothing zoned** (s14 §1 bug note) [P] |
| s14 | `0d513fb` 10-14 09:58 | real zone prefabs; Marquee quads along the outline; Commercial types (lanes, lots, plots) | shops grew — also over the plaza (no blocking) [P, s19] |
| s19 | `fb13787` 10:38 | snap to captured cells; physical Zone Block lots after Apply | "zoned 4 of 16 … blocked 12": split edges moved the grid under the blockers [P, s26] |
| s22 | `4889dc3` 13:19 | cutouts never on roads; one block per land | — |
| s26 | `f353081` 15:30 | ZoneRefresh after Apply; lane block predictor; one plot per column | preview = prediction, build = live re-snap |
| s36/s39 | `9092bac`, `7931f54` | game depth rule; two whole columns | lone columns dropped |
| s40 | `9550e00` 20:41 | zoning-first: plots are predicted cells, host clearance 2.5 m | — |
| s48 | `64ec0e5` 22:34 | clearance **0.2 m** | [H] node geometry closes cells |
| s50 | `7c884ae` 23:35 | lot-first commercial (lots, access alleys first) | more connectors at the frontage |
| s53–s57 | `2d3038f`…`3caff19` | per-plot audit; refresh grow/shift; drop plots with no room | the "preview N, planned 0, live 0 <-- differs" lines today |
| s58 | `0ce8946` 01:56 | kerb apron for dead-end lot connectors | [H] |
| s60 *(not played)* | `ed57b50` | nearest open run; `NodeReach`; cause logging | untested |

**Today's shop evidence [P]:**

* **Groups 1/2** (12-layer builds, 11:45). Refresh "20 planned cell(s) in 5 plot(s) → 12" and "12 → 8".
  * Per plot: `preview 4, planned 0, live zoned 0  <-- differs` (lines 2021–2025, 2251–2254).
  * The dropped plots had "4 open, 6 blocked" and "6 open, 12 blocked" live cells in or near them.
* **Groups 3/4** (11:47:48, 11:48:39, small 1-layer builds with lanes; 41/40 parts; no validation errors).
  * `0 cutout cell(s) (planned 16)`.
  * s54 line: `8 planned cell(s) in 4 plot(s) -> 0 on the live blocks … #0: live cells in/near it: 2 open, 20 blocked …`
    (lines 2775–2776, 2800–2801).
  * Then `zone blocking: … keeping 0 cutout cell(s) free`: the blocker lot covered the plots too.
  * The refresh runs **before** the Zone Block step (step order in `BuildService.cs:305-325`). So the cells were already blocked
    by something else at refresh time. In CS2 only nets with `CompositionState.BlockZone` and areas with `PhysicalGeometry`
    block cells (`Game.Zones/CellBlockJobs.cs:165,345`), then `CleanBlockedCells` and `ReduceDepth` propagate.
  * Candidates [H]:
    * our lanes, connectors or kerb aprons at the frontage (s50/s58);
    * junction node reach (s48, s60's own diagnosis);
    * 2-cell plots killed by the depth rule;
    * other builds' physical Zone Block lots nearby;
    * Platter's own cell pipeline.
  * The played build cannot tell. Cause logging arrived in s60, which was not installed.

**Why shops "worked" then [P]:** in the s14→s19 window nothing blocked or re-snapped anything. Marquee quads zoned whatever cells
had centres inside them, nothing blocked the rest of the road's cells, and the city's own zoning stayed live under the park. So
buildings appeared, sometimes in the wrong place. Every later step — snapping, blockers, refresh, depth rule, zoning-first,
flush clearance, lot-first — made the preview more *precise*. Each also added one more way for the live game to disagree with
the prediction, and the build answers disagreement by dropping the plot.

---

## 6. ParkingLotTool comparison (the user's "it just worked" reference)

* PLT's interactive build waits until the parts have materialised, then sets `applyMode = ApplyMode.Apply` with **no
  `Error`/`GetAllowApply` gate** (`C:\Users\jack\ParkingLotTool\Tools\ParkingLotBuildStages.cs:293-332` →
  `ParkingLotToolSystem.cs:1772-1843`). PLT is "anarchy" by construction.
* Before Apply, PLT gives every own object `Owner(carrier) + Attached(carrier)` and every own path `Owner(lot)`. Surfaces are
  attached to the lot owner (`ParkingLotLotOwner.cs:328-469`). Our s55/s58 carrier owner copies this, but only for props; the
  owner lot owns nothing.
* PLT's background rebuild uses **`CreationFlags.Permanent` definitions with `m_Owner`**
  (`ParkingLotDefinitionsausgabe.cs:22-60`, `ParkingLotBauarbeiter.cs:95`). That is a direct write that skips the Temp/validation
  path entirely. Placemaker never had such a path.
* Placemaker's error gate came from the ParkManager transaction shape ("definitions → temporaries → game validation → apply",
  `f7fa1c1:Game/Build/BuildService.cs:1-13`), not from PLT.

---

## 7. Today's session, build by build (`4d62ea8`)

### 7.1 Table (from `.forensics/Placemaker-20261015-1152.log`)

A "ghost N:" line is logged only when the ghost has errors or dropped objects. A ghost with zero errors leaves only its
"clears N existing trees/props" line.

| start | ghost before the build | first-round errors | result |
|---|---|---|---|
| 11:44:22 | ghost 26, **instant from ghost** | 2 (SteepSlope, Small Road) | roads, 63/65 |
| 11:45:19 | ghost 32: 33 rejected | 30 | group 1; 24 left out; zoning 12/20 |
| 11:45:41 | ghost 34: 19 rejected | 21 | group 2; zoning 8/12 |
| 11:46:15, 11:46:27, 11:47:03, 11:47:09 | ghosts 37–41: 100 rejected | 77 each | **FAILED** ×4 — InvalidShape `Invisible Road Path - 2xTwoway` ×8, `Alley` ×3, `Invisible Car Path - 1xTwoway` ×2 |
| 11:47:48 | ghost at 11:47:47, 0 errors | **0** | group 3; zoning **0/16** |
| 11:48:39 | ghost at 11:48:33, 0 errors | **0** | group 4; zoning **0/16** |
| 11:49:22 | ghost 53: 219 rejected | 190 | **FAILED** — InvalidShape aisles ×15 |
| 11:51:11 | ghost at 11:51:07, 0 errors. A *different* site from ghosts 55–58: the obstacle set changed (emptyZone 162 → 202, myBuilds 3 → 1) | **0** | group 5; 122/122 markings; 24 props stay hidden |
| 11:51:38 | ghosts at 11:51:29 and 11:51:36, 0 errors | **0** | group 6; 121/121 markings; 28 stay hidden |
| 11:51:58 | ghosts at 11:51:45–55, 0 errors | **0** | group 7; 325/325 |
| 11:53:33 (live log, now overwritten) | ghost(s) from 11:52:14, 0 errors | **0** | group 8; tagged 332 |

### 7.2 Self-overlap is not inherent [P]

* The ghost's error count predicted the build's every time (0 → 0, 19 → 21, 33 → 30, 100 → 77, 219 → 190). The ghost is a
  faithful early warning.
* The Parking type built twice with **zero** validation errors and 122/121 markings, after s53 scaled the marking to
  0.928 (2.97 m on a 3.0 m stall).
* So the claim in s53/s59 that "every neighbour pair of markings collides" (3.2 m box on 3.0 m stalls) no longer holds in the
  played build.
* The errors that failed builds today are tied to the site and plan:
  * InvalidShape on parking aisles: 15, constant through all six rounds.
  * InvalidShape on commercial alley stubs and connectors.
  * Decal-vs-decal overlaps: sunk decals share the Underground mask [H].
  * `overlaps-nothing-planned`: our pieces against nets, either ours or the city's.
* Two things remain unexplained [H]:
  * Why the aisles fold at one site and not at the others. Candidates: the site's grading, the street heights, short pieces at
    T nodes (s47).
  * Why the errored nets never matched a plan item, so the loop could never exclude them.
* Both can be answered offline with the game's `Game.Net.ValidationHelpers.ValidateEdge` run against the planned courses.

### 7.3 Failed builds leave terrain changes behind [P]

* `DoCleanup` only discards pending brush definitions (`4d62ea8:BuildService.cs:680-686`, `Terrain.cs:456-461`). Direct
  `ApplyBrush` passes and paint stamps are not undone.
* Log proof: after the 11:46:17 failure, the retries on the same plan report `terraform reached its target in 0 pass(es)`
  (lines 2256, 2277, 2298). The ground was already graded by the failed attempt.
* A failed lot therefore leaves graded and painted ground with nothing on it. Master has the same code.

---

## 8. "Worked" state vs master — differences that can plausibly cause today's failures

Baseline: the 10-13 night build the user ran on 10-14 morning (s15: "they ran a build older than Block paint" — i.e. before
`9f7d38e` 08:07, most likely ≈ `a4c1e7b` 00:18), plus the s14 zoning window for shops. Master = `650be0f`.

### Parking lot path

| # | Difference | Introduced | Status in master | Evidence |
|---|---|---|---|---|
| P1 | Error gate + 6-round exclude/retry that cannot exclude InvalidShape nets → build fails | `f7fa1c1`/`0e9cfe0` (gate); fatal since `eb0f1f3` | **removed** (s59) | [P] lines 3010–3028 |
| P2 | Stall markings non-optional / non-excludable | `eb0f1f3` | kept (correct), no longer fatal | [P] |
| P3 | Lot grading terraform → no instant path; terraform applied before validation and **not rolled back** on failure | `eb0f1f3`, `932cea8`, `0ce8946` | **unchanged** | [P] §7.3 |
| P4 | InvalidShape on `Invisible Road Path - 2xTwoway` aisles/connectors (short pieces at nodes; s47 left the parking aisles alone) | port `ff6817b` + grading/kerb-apron changes | **built anyway** (folded geometry applied; lane connectivity unverified) | [P] error; cause [H] |
| P5 | Site-specific InvalidShape on aisles at one site (same type: 0 errors at two other sites) | port + grading (s25/s29/s58) | built folded under anarchy | [P] the difference exists; [H] its cause |
| P6 | Much more dressing (decals, see-through clones); sunk decals still collide | `cc4bf56`, `a330350`, `533f85f` | unchanged; collisions ignored | [P] OverlapExisting on `Placemaker Decal 15 – …` |
| P7 | Props hidden after Apply against our nets / Zone Block lot | Zone Block `fb13787`; carrier `29879d0`/`44b32fe` | master un-hides all unless near a building | [P] 24/28 stay hidden |
| P8 | Physical Zone Block lot over every non-loose build (parking included) | `fb13787` | unchanged | [P] "zone blocking: 1 lot ring(s), 5620 m²" on parking builds |
| P9 | Owner lot ("one bulldozable object") | `378b624`, `d0ce7a6` | unchanged | **not a cause** [P]: emitted after validation, owns no member, allows overlap |
| P10 | New master gates: `OriginalDeleted`, Duplicated, WouldDemolish (existing building + our net) | s59 | new, untested | — |

### Shop / strip mall path

| # | Difference | Introduced | Status in master | Evidence |
|---|---|---|---|---|
| S1 | Same gate/loop failure on Alley stubs + lot connectors (InvalidShape) | as P1; stubs `0d513fb`, s47 partial fix | removed; nets built folded | [P] lines 2466–2485 |
| S2 | Preview cells are a *prediction* (captured grids + `ZoneBlockPredictor`, free ends only); the build re-snaps to live blocks after our nets split the street edges, and **drops plots it cannot match** | `fb13787`, `f353081`, `9550e00`, s54/s56 | s60 adds "nearest open run" | [P] "preview 4, planned 0, live 0 <-- differs" |
| S3 | Host nets flush to the cells (0.2 m) → node geometry closes cells | `64ec0e5` | s60 `NodeReach` for walkways ≤ 2.5 m half width only | [H] |
| S4 | Lot-first layout: more connectors/alleys at the frontage; kerb aprons | `7c884ae`, `0ce8946` | unchanged | [H] |
| S5 | Depth rule: 2-cell plots / lone columns blocked | game rule; `9092bac` port | unchanged | [H] groups 3/4 plots are 2 cells |
| S6 | Physical blockers cover dropped plots (zoning lost for good after the build) | `fb13787` | unchanged | [P] "keeping 0 cutout cell(s) free" |
| S7 | No cause logging in the played build | — | s60 `ZoneCause.cs` | [P] |
| S8 | Mod environment (Platter cell pipeline, Anarchy, PLT) | — | — | [H] not covered by any test |

### Honest verdict

* "It worked" meant pieces vanished silently: optional cancel, the retry loop, doubled sets.
* City buildings were demolished.
* Placemaker zoning did not exist (before s14), or was unconstrained (s14→s19).
* The things the user now expects — every stall line, no demolition, preview = result, shops exactly where shown — were never
  achieved together in any commit. The pipeline became stricter about *what* it built, faster than it became right about *how*
  the game accepts it.

---

## 9. What remains unverified on master (relevant to the next session)

1. Which planned courses the game folds (InvalidShape). This can be checked offline with the game's `ValidateEdge`.
2. Whether InvalidShape nets applied under anarchy form working lanes (cars parking, connectors joined) or leave folded,
   broken geometry.
3. Shop zoning on lot-first commercial types: the s60 `ZoneCause` lines will finally name the blocking source.
4. Failed builds still leave terraform and paint (P3) — rarer under s59, but not fixed.
5. New master gates (`OriginalDeleted`, WouldDemolish for nets touching buildings) are untested.
