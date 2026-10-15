# Design A: keep and harden

The rebuilt Placemaker build pipeline, from the "keep the entity model, fix every root cause at its source" angle.

- Inputs: teardowns 00–10 in this folder, master `d9f6df6` (= `650be0f` s59 + s61 roads), the decompiled 1.6.2f1 game
  (DECOMP = `%TEMP%\pm-decomp\game`), and the offline bench prototype in `.forensics/bench/` (06).
- Labels: **[P]** proven (code, log or bench run cited), **[H]** hypothesis, with the check that settles it.
- **Nothing in this document is installed.** The user's game runs `650be0f` (09:18 DLL). Under the reporting rule in §9.6,
  nothing here may be called "fixed" until the bench gate is green for every type on the changed path **and** an in-game
  audit carries the build hash.

---

## 0. The design in one page

**Stance.** None of the forensic reports found that the entity model causes the failures.
- The owner lot never refused anything; it is emitted after validation and owns no part (01 §0.5, 05 §0) [P].
- The zone-block lots blocked **0** of the lost shop cells (07 §3(h)) [P].
- s59's one-pass anarchy made builds complete in the user's 12:09 session (00: "built 229, left out 7") [P].

What broke is upstream and downstream of the model:
1. **The planner emits geometry the game cannot represent.** Nets fold (`InvalidShape`), and lots are wrapped in vanilla
   farm fields.
2. **The shop zoning is a prediction of a contest our lane loses** against older city streets.
3. **Post-Apply heuristics silently rewrite or drop what the preview showed.**

So this design keeps the model and makes each of those three impossible by construction. Each guarantee is proven
offline by running the game's own code over a type × preset × site × terrain matrix before anything reaches the user.

| Goal | How it is met | Proven offline by |
|---|---|---|
| 1. A parking lot or shop always builds, with no error dialogs | s59 one pass stays. Every remaining exit that can fail is checked **before** the first permanent change (money, catalog, prefabs), and any later failure rolls the terrain back. The planner emits only game-valid nets (NetLegalizer, rules N1–N12), so anarchy is a safety net that should never fire. The ghost mutes error icons at the source. | G-NET, G-OBJ and G-AREA on the real `ValidateEdge`, `ValidateObject` and `ValidateArea` |
| 2. Preview = result, shops included | One plan revision feeds both the ghost and the build. No game step drops or hides a piece (§6). No post-Apply heuristic changes the plan. Shop land cannot be contested (own non-shop sides use `ZonesDisabled`; city sides along the build get kerb strips). The first shop building of every plot is placed by us on the verified cells, and the ghost shows that building. | G-ZONE on the real `BlockSystem` + `CellCheckSystem`, G-SEED, census audit |
| 3. Existing buildings never destroyed | The Apply-time guard stays (s59 `NeutralizeVerdicts`). **New:** a guard against simulation-time condemnation. No net, blocker, kerb strip or side toggle may block a cell of an existing lot. A post-build watch logs any `Condemned` building. | G-CITY on the real `CellCheckSystem` |
| 4. Easy removal | The owner lot, one-click bulldoze and `GroupRemovalSystem` stay. They are extended to kerb strips, seeded shops and recorded edge flags. **New:** "Release build" keeps the pieces as plain city content, as the user asked today. | removal planner tests |
| 5. Verifiable offline | The bench prototype (06) is promoted to `Tests/Placemaker.GameBench*`. A matrix gate is calibrated against one read-only prefab capture and against in-game audits replayed offline. | gates A1–A8 (§9) |

**What goes** (§10): the exclude/retry remnants, the instant-from-ghost path, vanilla-rules mode, build-time terrain
splitting, every zoning re-snap heuristic, the raw-quad fallback, the silent zoning return, the separate blocker
transaction, the parking planner's farm-field lot area, lot-first leftover lots, street-facing shop rows by default, and
the `NetFit` model with its narrow test.

**What stays:** the group record and `PlacemakerMember` tags, the owner lot + `RaycastLiftPatch` +
`BulldozeConfirmationPatch`, the parking carrier, s58's carrier owner for props (made unnecessary in most cases by plan
clearance, but kept), Zone Block lots (now with a narrower job), the park companion, s59 one-pass verdicts, the receipts,
and the edit flow.

---

## 1. Root causes, and where each one is fixed

| # | Root cause (evidence) | Fixed at the source by | WP |
|---|---|---|---|
| RC1 | Nets shorter than their junction cut-backs fold (`InvalidShape`). 386 folding pieces over 306 plans; 104 of 292 plans fail in the game's own `ValidateEdge` (03 §3.2, 06 §5) [P] | NetLegalizer rules N1–N12, enforced inside each planner and checked by a Composer stage | WP2 |
| RC2 | The build-time terrain split (0.35 m / 6 m since `082afac`) adds short pieces next to junctions: 23–102 new folds per 24 plans (03 §3.3 C) [P] | Course fitting moves to plan time, against the planned ground. Lot nets are never split. Emit sends the planned courses verbatim | WP2 |
| RC3 | Two planned courses that cross never get a node (`CourseSplitSystem` only joins existing nets, 03 §2.4) [P] | Rule N3: every crossing becomes a shared node in the plan | WP2 |
| RC4 | Alley stubs have no margin and no `RemoveCrosswalk` (03 §3.3 B; PLT removes crosswalks) [P] | Rule N8 | WP2 |
| RC5 | Shop plots face city streets **by design** (`StreetRows`, `outerRow` on by default; 07 §3(a)) [P] | Plots face only our lane or lot; street rows are opt-in | WP3, WP4 |
| RC6 | Our lane loses contested cells to the older visible city block (`CellOverlapJobs.CheckPriority`, 08 §3) [P] | No contest: own non-shop sides get `ZonesDisabled`; city sides along the footprint get kerb strips (08 §4) | WP3 |
| RC7 | The preview counts cells the game has already blocked as open (`ownBlockedOpen: true`, `ZoneGuard.cs:715`). The bench showed plaza losing 356 of 1,068 cells with no nets at all (06 F2) [P] | One openness model, the game's, checked against the real `CellCheckSystem` | WP3 |
| RC8 | Post-Apply re-snap moves or drops plots: grow/shift ≤ 4 m, `NearestOpenRun` onto *any* road (`ZoneGuard.cs:430–441, 486–522`). It paints the raw quad when the snap fails (`:467`) and returns silently when nothing is left (`BuildService.Terrain.cs:646`) (02 §2, 07 §3(a)3) [P] | All of it deleted. A plot is verified, never moved; a miss is reported per plot | WP3 |
| RC9 | Zoned is not a shop: growth needs commercial demand > 0 (`ZoneSpawnSystem.cs:1102`) (04 V4) [P] | Seed one shop building per plot through the game's own spawn shape, on the verified cells | WP3 |
| RC10 | Every parking lot is wrapped in a vanilla "Agriculture Lot". `PlanBuilder.cs:448` emits a Lot area with an empty asset, `Mall.cs:600` merges it, and `AutoAssets` fills it from the `area.lot` pool. This gives farmland and fences (07 §3(b)(c)) [P for the chain; H for the fence] | The area emission is deleted, and `area.lot` is never automatic | WP4 |
| RC11 | Leftover strips become parking lots (`CommercialLayer.cs:172–173, 318`). A 1-stall lot is accepted (`Mall.cs:334`), and "NoEntrance" is dropped silently (`Mall.cs:350`) (07 §3(d)(e)) [P] | One lot rule; leftovers become planted buffer | WP4 |
| RC12 | Failed builds keep their terrain changes (01 §7.3, 02 S12) [P] | Every fail-able check runs before the terraform. A terrain snapshot gives rollback. Paint moves after Apply | WP5 |
| RC13 | Live bowls after Apply are only logged, never repaired (`BuildService.Terrain.cs:160–176`, 07 §3(f)) [P] | Plan-time node heights (N7, N11), plus one corrective pass and a UI note | WP2, WP5 |
| RC14 | The audit "built" is planned minus left out; parts that never materialise are uncounted; type and preset are never logged (02 §6) [P] | The audit becomes a census, with a replay section for the bench | WP5 |
| RC15 | Our own prefab clones trigger full catalog rescans and a second ghost rebuild, freezing the game for ≤ ~2 s on a switch (09) [P for the chain] | No rescan for own prefabs, the dump moved off the critical path, ghost revision tied to the content hash | WP6 |
| RC16 | No guard against condemning city growables whose cells our nets or blockers block (`ZoneCheckSystem.cs:334–367`, 05 §1.5) [P for the rule] | Plan rule plus gate G-CITY | WP3 |

---

## 2. Entity model (kept, with a narrower and hardened role for each part)

| Entity | Today | After | Why the change is safe |
|---|---|---|---|
| Group record (`PlacemakerGroup` + receipt blob) | yes | yes. The receipt gains `plannerVersion`, `type`, `preset` and the plan hash | additive |
| `PlacemakerMember` tag on every member | yes | yes. New kinds: `ShopSeed`, `KerbStrip` | additive |
| Owner lot "Placemaker Area" (selection, one-click bulldoze) | emitted after validation, owns nothing | **unchanged** | proven not to be a cause (01 §8 P9, 05 §0) |
| Bare carrier with stall markings (`Owner` + `Attached`) | yes | **unchanged**. Required by `LaneConnectionSystem.FindParkingConnection` (05 §3.3) | — |
| Carrier owner for overridable props (s58) | yes, added at tag time | **kept**. Combined with s59 stripping `Override` before Apply, it stops our own props hiding each other, at Apply and later in `OverrideSystem` (shared top owner, `OverrideSystem.cs:349, 384`). It is a safety net now: plan clearance (G-OBJ) makes overlaps rare | C3 was the pre-s59 ordering (05 C3) |
| Nets | unowned; may fold | unowned. **Planned courses are final** (WP2); our lanes carry per-side `ZonesDisabled` | `ParseUpgrades` → `Upgraded` on the definition already exists (`BuildService.Emit.cs:174–179`) |
| Zone Block lots | separate Temp transaction after Apply, ≤ 3 rounds, footprint − refreshed cells | **in the main transaction**, computed from the final plan. Footprint − shop cells (with a 0.5 m gap) − existing lots **and their cells**, plus **kerb strips** (1 m, 0.5–1.5 m inside the city row 0 along the frontage) | blockers blocked 0 lost cells today (07 §3(h)). Their errors against our nets are already neutralised by s59 |
| Shop seed (new) | — | one vanilla spawnable commercial building per plot, placed after zoning is verified, tagged `ShopSeed` | it is a real growable on real zoned cells: no runtime prefab, so it survives without the mod |
| Park companion | yes | unchanged | out of scope |
| Vanilla Lot area from the parking planner ("Agriculture Lot") | yes, **untagged** (07 §3(b)4) | **deleted**; migration removes old ones with consent (§8) | — |
| Terraform / paint | permanent before validation; never undone on failure | terraform after the pre-checks, with a snapshot and rollback; paint after Apply | §3 |

Removal, selection and receipts keep working as today (`GroupRegistrySystem`, `GroupRemovalSystem`, `RemovalPlanner`).

---

## 3. Build path

### 3.1 Steps (one main transaction, then a zoning tail)

```
Prepare      plan hash == the previewed hash? (else: re-preview, never build an unseen plan)
             PlanWork.Flatten; ResolvePrefabs; PRE-CHECKS: catalog ready, every work item has a prefab,
             price (Pricing incl. nets) <= city money, plan has no blocking issue
             -> any failure here: nothing in the city changed, clear message, no dialog spam
ActivateTool (Clear frame)
Terraform    snapshot heights of the terraform raster, then the direct brush as today
EmitNets     planned courses verbatim: no split, no drop, identical node positions (N7)
EmitRest     clear-definitions, surfaces, areas, objects, Zone Block lots + kerb strips (moved here)
WaitTemps    census against PlanIndex; a miss is counted by kind and prefab (audit), not just logged
Validate     s59: ONE pass. Every flagged temporary is recorded as a PLANNER DEFECT
             {error type, prefab, position, plan key} -> audit + calibration dump. Nothing is refused.
OwnerLot     unchanged
Apply        NeutralizeVerdicts (unchanged), ApplyMode.Apply
Settle       adopt strays, charge nets, census members
Paint        moved here (cosmetic, cannot fail)
ZoningWait   until the cell check of our new blocks has run (<= 40 frames, polled, not fixed)
ZoningVerify read the LIVE blocks of our own lanes; per plot: cells Visible, owner edge == the intended lane,
             zone None. NO re-snap, NO shift, NO fallback quad.
ZoningApply  Marquee over the verified cells only (no Overwrite) -> wait -> verify zone == the plan's zone
SeedShops    one Temp transaction: per verified plot the planned spawnable at ZoneUtils.GetPosition(live block, lot)
             -> neutralize -> Apply -> tag ShopSeed
Finish       census audit (§6.3), live-bowl repair pass (§5.6), condemnation watch armed (§7)
```

### 3.2 Failure semantics

- **Before Terraform:** a failure changes nothing, so the user sees one clear message with nothing to roll back.
- **Between Terraform and Apply:** the remaining exits are `OriginalDeleted` (the user bulldozed a street mid-build),
  `NothingMaterialized` and `Interrupted`. Cleanup restores the snapshot heights with one brush pass back to the snapshot
  target (`TerraformRaster` already drives toward targets).
  - [H] The direct brush reaches a snapshot target as accurately as it reaches a planned one. Settled by the existing
    "reached its target in N passes / worst" line on the rollback.
- `Duplicated` is a code bug. It becomes an assertion: it fails the build in development and is covered by tests. There
  is no `ResetForRetry`.
- **After Apply:** nothing fails. Zoning or seed shortfalls are **per-plot notes** in the result, never a build failure
  and never silent.
- `ProtectBuildings() < 0 → WouldDemolish` (`BuildService.cs:500`) becomes a logged defect. The building is already
  protected by `NeutralizeVerdicts`, so failing the build protects nothing; only the ugly overlap remains, which G-CITY
  makes a plan defect.

### 3.3 The ghost

- The ghost uses the same `PlanWork`, the same emitters and the same plan revision.
- **New:** while the ghost exists and no build is running, a small `GhostErrorMuteSystem` sets
  `ToolErrorFlags.DisableInGame` on every error prefab right before `ValidationSystem`, and restores the flags right
  after. This is Anarchy's own mechanism (05 §2.4; DECOMP `ValidationSystem.cs:1219` skips flagged prefabs, and
  `ProcessError` drops errors whose prefab is null at `:1537`). The user then sees no red tint **and no error icon**.
  - Today `ClearGhostVerdicts` (`BuildService.Verdicts.cs:274`) removes `Error`/`Warning` but leaves the icons.
  - [H] It coexists with an installed Anarchy mod, because both set and restore every frame. Check: the passive ghost
    self-check (§9.5) logs the icon count, which must be 0.
- The build keeps reading errors (s59) because that is the free in-game oracle for calibration (§9.4).
- The ghost also draws each shop seed as a Temp building (`ShopSeed` plan item) and the zoned cells, labelled
  "faces <lane name>".

---

## 4. Nets: the geometric rules (WP2)

### 4.1 Where the rules live

1. **`Core/Build/NetProfiles`**: per prefab, the half width, node offset `n`, crosswalk depth, `m_MinNodeOffset`, merge
   layer and flags.
   - Generated from the one-time read-only prefab capture (§9.3).
   - Until the capture exists, conservative defaults apply: `n = 1.0 m` (the field default is 0.5 m, and the bench saw
     folds flip between 1 and 2 m, 06 §1.5), crosswalk 3 m on any node that touches a `Road`.
2. **Generation time:** every planner builds with the shared `NetLegality` API, so legal geometry is the default rather
   than a repair. The parking `LayoutEngine` and `PlanBuilder`, `Mall`/`MallKit` lanes, park and leisure path networks,
   `RoadsPlanner` and plaza walkways all ask `NetLegality.MinPiece(a, b, angles)` before they commit a junction.
3. **Composer stage `NetLegalizer`**, run after `SiteSafety` (which can cut nets) and before `ZoneGuard` (zones depend on
   the final lanes) (`Core/Composition.cs:188–190`).
   - It verifies N1–N12 over **all nets of all layers plus the site's attached city edges**.
   - Where something is still wrong it applies a minimal repair (§4.3) and adds an Info issue.
   - The bench gate counts repairs separately: a planner that relies on repairs is flagged, not passed silently.
4. **Plan-time course fitting** (`CourseFitting`, moved from `BuildService.Emit.cs:139–142` into the Composer after
   `SiteHarmony`). It fits against the **planned ground** (`SiteHarmony` target heights), so each `PlannedNet` carries
   its final courses. `EmitNets` sends them verbatim.

### 4.2 Rules (numbers from 03 §2.3 / §6 and the game code)

Notation: `h` = half width, `n` = node offset, φ = the angle between the two rays leaving a node, `cw` = crosswalk depth,
`cut = max_j (h_j + n + cw_j + h_i·cosφ)/sinφ`, with floors `n` (different prefab), `|Δgrade|·w` (if > 0.2 m) and
`m_MinNodeOffset` (if not straight).

| # | Rule | Number | Game source |
|---|---|---|---|
| N1 | Every edge: `len ≥ cut(start) + cut(end) + 0.2 m + 0.5 m safety` | 90° T on 2xTwoway at both ends: ≥ 8.7 m. A footpath T on a 2xTwoway aisle: ≥ 3.7 m | `GeometrySystem.cs:361–374, 824–1170`; `ValidationHelpers.cs:1360–1391` |
| N2 | Junction angles 60°–90° for nets ≥ 3 m wide, never < 45°. Joins between different prefabs are exactly straight (dot ≥ 0.9995) or ≥ 15° with N1 counted; never 165°–178° | at 45° a 7 m aisle needs 9.2 m per end | `:954–958` |
| N3 | Every crossing is one shared node, split on both nets. No mid-span crossing, no collinear overlap, no two edges leaving a node in the same direction | the game never joins two new courses | `CourseSplitSystem.cs:248–281` |
| N4 | Distinct nodes on one net are at least the N1 length apart. Near-duplicates are merged, never left as a 0.5–1 m edge | replaces the 0.75 m weld (`PlanBuilder.cs:671`) | `GenerateNodesSystem.cs:126–163` |
| N5 | No piece < 1 m exists in the plan. Emit asserts this instead of dropping (`MinCourseLength`) | — | 03 §3.3 G |
| N6 | Lot nets (aisles, cross lanes, connectors, lot footpaths) are **never** terrain-split; profile fit only. Other nets split only at deviation > 0.6 m, both pieces ≥ max(16 m, N1 need), and the split point ≥ `cut + 2 m` from any junction | restores the pre-`082afac` settings, constrained | 03 §3.3 C |
| N7 | Node positions are bit-identical (x, y, z). Core assigns a node id; its height comes once from the planned ground; every course end uses it verbatim. Only ends attached to a city edge snap | — | `GenerateNodesSystem` merges only equal `float3` |
| N8 | Alley/street entrance stubs get `Upgraded{RemoveCrosswalk}` on both sides (PLT). Length ≥ `h_road + n + transition + 0.5`: ≥ 7.5 m on a 12 m street, ≥ 5.7 m on an 8 m alley T. A stub that cannot fit becomes a kerb dead end (LocalConnect), as s47 does where a stall is in the way | today 6.0 m / 4.0 m | PLT `ParkingLotNetCourse.cs` |
| N9 | Curve centre-line radius ≥ `h + n + 1 m` | — | `NetUtils.OffsetCurveLeftSmooth` (proven headless, 03 §2.3) |
| N10 | An attach point on a city edge is ≥ `cut` from every existing node of that edge on both sides, so both split halves of the city edge pass N1. Otherwise use the existing node | protects city roads from folds | 03 §3.3 H |
| N11 | Grade at nodes: `|Δgrade|·w ≤ 0.2 m` (7 m nets: Δgrade ≤ 2.8 %). No node within `cut + 2 m` of a terrace break; ramps are single constant-grade edges | ties into `LotGrading` tiers | `GeometrySystem.cs:941–949` |
| N12 | A node is never more than a kerb (0.3 m) below the graded ground of its surroundings; connector ends at a lower street get a graded apron within the lot, never a pin into the lot | prevents the shot-106 bowl (07 §3(f)) | [H] the cause of that bowl; checked by the post-Apply bowl audit |

### 4.3 Repair actions, in order (Composer stage)

1. Merge near-duplicate nodes (N4).
2. Insert shared nodes at crossings (N3).
3. For a short piece between two junctions, the first that applies:
   - (a) slide the minor junction along its host net until N1 holds, if it moves ≤ 2 m and hits nothing;
   - (b) drop the minor branch (a footpath spur or a cross-lane piece) and end it as a LocalConnect dead end ≥ 1.5 m from
     the host;
   - (c) merge the piece into its neighbour (remove the node) when the angle there is < 1.8°.
4. Stub too short: convert it to a kerb dead end (N8).
5. Anything still illegal: the net is **kept**, the plan gets a `PlanIssue` Warning, and the bench gate fails. Nothing is
   dropped silently.

### 4.4 Layout changes that remove the need for repairs (PLT practice, 03 §5)

- **Parking `LayoutEngine`:**
  - align aisle ends when that costs no bay (PLT `Ringlos.PlaneGemeinsameEnden`), which removes the ring "jogs";
  - no end section under two bays;
  - cross lanes only where a section carries ≥ 5 bays;
  - inner footpaths end as dead ends beside aisles instead of T-splitting them (this removes the 1.6 m pieces s47 left
    alone).
- **Commercial lanes:**
  - T/X junctions at 60°–90°;
  - lane junction spacing ≥ N1 (High street doors every 36 m are fine);
  - stubs per N8.
- **Park and plaza path networks:** spurs join at ≥ 45°, otherwise they end beside the path (the g5 `PathJoins` split
  stays, but under N1).

### 4.5 Why anarchy is still kept for nets

Applying a folded net cannot crash and never makes a zero-length edge. But the fold is permanent and visible, and it
feeds into terrain, zone blocks and lanes (03 §4) [P]. So anarchy stays only as a safety net. A net the game flags at
Validate is a **planner defect**: it is logged with position and plan key, counted in the audit as
`kept: InvalidShape (planner bug)`, and replayed offline as a regression case.

---

## 5. Shops and zoning: making them certain (WP3)

### 5.1 The model (zoning stays the mechanism)

A shop site has an **intended road** for every plot: our lane, or our lot's frontage lane. "Street front" (a city
street) is a user opt-in, never the default. The design removes every other claimant on the plot's land, so the
predicted cells are exact up to our own geometry (08 §7.3). The bench then checks that against the game's real block and
cell code.

| Claimant on the plot land | Removed by | Proven |
|---|---|---|
| The other side of our own lane (lot side, park side), and both sides of pure access lanes | `PlannedNet.Upgrades += "left/right:ZonesDisabled"`, set by the planner that knows which side has rows (`MallKit.LaneRows`, `EdgeZoningAddOn`) | rule [P] (`ZoneToggleJob.cs:37–70`, before the overlap pass, `CellOverlapJobs.cs:523`). [H] Which side is "left" after Apply: settled offline by the real `ZoneToggleJob` in the bench with `GenerateEdgesSystem` allow-listed |
| City street sides whose 48 m band reaches the footprint | **kerb strip** (default): a physical Zone Block lot 1 m wide, 0.5–1.5 m inside that block's row 0, only along the footprint, with gaps where our nets cross. `CleanBlockedCells` kills those columns and nothing outside the build | rule [P] (`CellBlockJobs.cs:345, 574–602`); prototype [P] (08 §6: 0/33 → our lane gets 11 zoned columns, 0 city cells lost outside) |
| The same, with the user's choice "Off on whole street side" | vanilla `ZonesDisabled` on the city edge, through an upgrade definition in the main transaction, previous flags recorded for removal. Refused where that side has zoned or occupied cells outside the footprint (08 §7.1 guard) | [H] conflict with an attach on the same edge in one transaction: the bench runs `GenerateEdgesSystem` on the pair. If they conflict, use a post-Apply mini-transaction |
| Our own lot aisles, connectors, aprons and node caps reaching row 0/1 | plan rule: our `BlockZone` nets keep ≥ 1 m (real geometry, node caps included) off the plot's cells and off row 0 of its lane band | G-ZONE measures it with the real geometry |
| Another Placemaker group's lane side that is zoned or occupied | never kerb-stripped or toggled. A plot is invalid where such a side reaches; that land becomes buffer | §7 guard |
| Existing zoning (Marquee without Overwrite paints only zone None) | our lanes are new, so their cells are None. The capture now records `m_Zone` per cell, so a plot over zoned city cells is invalid | `GenerateZonesSystem.cs:84–87` [P] |
| The depth rule (a lone column dies) | plot ≥ 2 columns × 2 rows (exists), checked by the real `CellOverlapJobs` | [P] (06) |

### 5.2 ZoneGuard rewrite (Core)

- **One** `Compute`, used by the design, the build verification and the bench.
  - Inputs: our predicted lane blocks (`ZoneBlockPredictor`, matched to the real `BlockSystem` within 5 cm, 06 §1.5),
    with per-side zone flags, and the captured city blocks with per-cell `m_State`, `m_Zone`, `Occupied` and
    `BuildOrder` (the `SiteCapture` extension).
  - It applies kerb strips and blockers as physical rings.
- Openness follows the game:
  - a captured Blocked cell is blocked (`ownBlockedOpen` is deleted);
  - "on a road" is a quad intersection of the cell shrunk by 0.02 m with the real outline of the net, including node caps
    (`CellBlockJobs.cs:425`), instead of a distance from the cell centre;
  - overlap ranking is `CheckPriority` (older or non-updating visible block wins; depth only for opposite blocks), so the
    preview can never promise a cell the game gives away (`ZoneGuard.cs:664–668` reversed).
- Its outputs are per-plot cells, the blocker and kerb rings, and the plot's **intended road id**.
- **Deleted:** `Refresh` and everything it calls (grow, shift, `NearestOpenRun`, raw-quad fallback, `Diagnose` window
  counts).

### 5.3 Plot rules (planner; every cutout host)

- **P1.** A plot is planned only when its intended road is the **only** road whose block can own its cells, after §5.1.
  Corner cells that the capture or prediction reports Blocked are never plot cells (06 F2).
- **P2.** `StreetRows` (`CommercialLayer.cs:169, 201`) and High street `outerRow` (`HighStreetLayer.cs:152`) are off by
  default and exposed as "Shops on the outer streets" (preview label "faces Mill Street").
- **P3.** `Mall.Faces` (`Mall.cs:161–176`) accepts a plot only along its intended road.
- **P4.** Plot depth is `floor(S/8)` rows in a strip of width S between road bands. The remainder is planted, not zoned
  (08 §7.3).

### 5.4 Shop seeds (the preview shows the shop, the build places it)

- **Choice.** At plan time, per plot, `ShopSeeds.Pick` takes a vanilla spawnable whose zone matches the plot and whose
  `BuildingData.m_LotSize` fits (width ≤ plot columns, depth ≤ plot rows), level 1, chosen deterministically from the
  plan seed. The candidates come from the catalog; the bench uses the capture.
- **Pose.** The planner computes it with a port of `ZoneUtils.GetPosition`/`GetRotation` on the predicted block. The
  bench differential runs the real `ZoneUtils` on the real block and must match within 5 cm. It faces the intended road
  by construction (`ZoneSpawnSystem.cs:630–634`).
- **Build.** After `ZoningApply` verifies the cells, the plot gets a Temp definition (`CreationDefinition` +
  `ObjectDefinition`) in one small transaction. The pose comes from `ZoneUtils.GetPosition(live block, lot)`, the same
  call `ZoneSpawnSystem.Spawn` makes (`ZoneSpawnSystem.cs:614–680`). Sub-areas and sub-nets come from the normal tool
  pipeline.
  - [H] Whether the `Construction` flag is honoured on the Temp path (the building appears under construction). Without
    it, the building appears finished. Either is acceptable.
- **Survival.**
  - The building is a normal growable on Visible, zoned cells with a roadside front, so `ZoneCheckSystem` keeps it
    (`:334–376`). It needs no runtime prefab and survives without the mod, as long as the cells stay valid.
  - Without the mod a kerb strip disappears and its city columns come back (08 §4.3). Parallel strips keep our zoned
    cells (zoned beats unzoned for opposite blocks); perpendicular alleys can lose them. This is documented and is the
    reason for the vanilla-flag option.
- **Honest limit [H].** We place the building, but companies move in only with demand. Whether an empty seeded growable
  ever goes abandoned, and how it levels up, is checked by the re-audit hook: each group stores its seed entities, and
  after 10 in-game minutes of a session the user plays anyway, the hook logs their state.
- **Setting:** "Place the first shops on Build" (default on). When off, the preview labels the cells "shop land (grows
  with demand)" and draws no building.

### 5.5 Build-time verification (replaces refresh, re-snap and silent returns)

- `ZoningVerify` reads the live blocks of **our** lane edges only.
  - For every plot it asserts: cells Visible, owned by the intended edge (after splits, the edge whose `SubBlock` holds
    the block), zone None.
  - It paints exactly those cells. A plot with fewer cells than planned is painted partly, seeded only if a lot still
    fits, and reported per plot ("Shop 3: 2 of 4 cells, taken by <road>").
  - The plot is **never moved** and its land is **never** blocked afterwards.
  - Its full live block dump goes into the audit, and the bench replays it as a regression case.
- `VerifyZoning` never returns early. The audit compares against the **preview snapshot** and checks the zone type,
  not "any zone" (`BuildService.Terrain.cs:646, 672–675`).

### 5.6 Terrain

- The node heights (N7, N11, N12) come from the planned ground.
- At Finish, `VerifyLiveHollows` checks for bowls. A bowl deeper than 0.5 m where the target has none gets one
  corrective brush pass toward the target, and the result shows a note.
- Any bowl that remains is a defect in the audit.

---

## 6. Preview = result: the mechanism

### 6.1 Same input
- A single plan revision is both shown and built.
- `Prepare` refuses a plan whose content hash differs from the last ghost's and re-previews it. Under the current code a
  catalog bump between preview and Build can change resolved assets (09 §1).

### 6.2 Nothing the game can drop or hide

| Path by which content vanishes | Closed by |
|---|---|
| `CancelOptional` | ghost and build are non-optional (s59) |
| `Cancel` verdicts | vanilla-rules mode deleted; anarchy only |
| `Warning` → `Deleted`, `Override` → `Overridden` at Apply | s59 `NeutralizeVerdicts` |
| `OverrideSystem` after Apply | s58 shared carrier owner, plus plan clearance (G-OBJ: 0 `Override` verdicts on our objects in the real `ValidateObject`), plus `AuditSurvivors` as a safety net that counts any hit as a defect |
| Pieces that never materialise | surfaces checked by the real `ValidateArea`/triangulation (G-AREA); nets legal (G-NET); a census miss is an audit line |
| Folded or rebuilt geometry | G-NET |
| Zone cells | §5 |
| Terrain under nets | planned heights, N12, bowl repair |

### 6.3 Census audit
- Built = the live members per kind and prefab, from `GroupRegistrySystem`, compared with `PlanWork`, plus live zoned
  cells against the preview snapshot and seeds placed against planned.
- The result shows "Built exactly as previewed", or an itemised list of differences with positions.
- Every difference is a defect reason and never an expected outcome.

### 6.4 Offline proof
- A5(e) in the bench: with `GenerateObjects/Nodes/Edges/Areas` and the `Apply*` systems allow-listed, the applied entity
  set equals the plan. This is a stretch gate, after WP1's first release.

---

## 7. Existing buildings and city content (WP3, WP5)

- **Apply time (unchanged):** `NeutralizeVerdicts` strips the `Warning` on existing buildings, edges and nodes in every
  mode (`BuildService.Verdicts.cs:236–241`). `SiteSafety` keeps the plan off building lots.
- **Simulation time (new).** `SiteCapture` records, per captured block cell, `Occupied` and the lot cells of existing
  spawnables. Plan rules:
  - no net with `BlockZone`, no blocker, no kerb strip and no side toggle may make an occupied or zoned cell outside the
    footprint stop being Visible with its zone;
  - inside the footprint, existing zoned but unbuilt cells may be removed only with a preview label
    ("removes city zoning on N cells").
- **Gate G-CITY** (real `CellCheckSystem`): 0 changes to captured occupied cells, and 0 changes to city cells outside
  the footprint except the kerb-strip columns, which lie inside it by construction.
- **Watch:** for 600 frames after Finish, any building within outline + 60 m that gets `Condemned` is logged as a defect
  with its position and the nearest member.
  - [H] Removing the cause at that point is not attempted. Prevention is the guarantee; the watch is evidence.
- **City streets:** N10 keeps both split halves legal. "Never split a city street just to toggle it" (08 §7.1).

---

## 8. Removal and migration (WP7)

### 8.1 Removal
- One-click removal via the owner lot is unchanged (`RaycastLiftPatch`, `BulldozeConfirmationPatch`,
  `GroupRemovalSystem`). It now also covers `KerbStrip` and `ShopSeed` members.
- Recorded city-edge flags (only in the opt-in whole-side mode) are restored through the same upgrade definition. If the
  edge was replaced, it is found by curve and logged.
- Terrain stays, as with vanilla bulldozing (`GroupFlags.PermanentTerrain`).
- **New "Release build"** in the group section. It removes the record, the tags, the owner lot and the blockers and
  keeps everything else as ordinary city content: the existing loose-build path (`LooseBuild`). The user can then delete
  pieces one by one with the vanilla bulldozer, as asked today. Stall markings stay on their carrier, so bulldozing one
  removes the lot's markings together; the dialog says so.

### 8.2 Migration (existing saves; nothing automatic, everything with consent)
- **Groups** load as today (`PostLoadRepair`). The receipt gains `plannerVersion`; groups below the new version are
  marked "built with older rules".
- **Folded nets.** On load, each member edge's `EdgeGeometry` (and its node geometry) goes through the game's own
  `CheckGeometryShape`, which is pure and already proven to run headless (05 Appendix A) and is equally callable in game
  through the same reflection. Groups with folds show "N folded pieces: Rebuild with current rules". That button is the
  existing Edit flow (remove, then rebuild from the receipt with the new planner).
- **Orphaned farm lots** (untagged vanilla Lot areas from `PlanBuilder.cs:448`). Any vanilla `area.lot` area lying ≥ 90 %
  inside a group's outline is listed in that group's section with "Remove leftover farm field (N)". Removal goes through
  `GroupRemovalSystem`'s deletion path.
- **Old Zone Block lots** stay valid. Old lane-side zoning is unchanged until a rebuild.
- **Settings.** The vanilla-rules options (`AutoExcludeCollisions`, `RecoverRejectedPieces`, "Ignore collisions" off)
  are dropped. A stored "off" is read as anarchy and logged once.
- **Tests:** receipt version round-trip; orphan matching on fixtures; `CheckGeometryShape` detection on folded and clean
  `EdgeGeometry` fixtures in the bench.

---

## 9. Offline bench and the acceptance gate (WP1)

### 9.1 Projects (06 §7.1, promoted from `.forensics/bench`)
- `Tests/Placemaker.GameBench.Shim` (icall implementations, synchronous job runner, deferred-array patch).
- `.ShimGen` (Mono.Cecil rewrite into a git-ignored `.shim/<game version>/`).
- `.Host` (resolver, Harmony neuter with an allow-list, commands `probe | capture-city | matrix | replay | golden`).
- `Tests/Placemaker.GameBench` (net48 game-surface oracles; compiles `Core/**`).
- `Tests/Placemaker.Tests/GameBenchGate.cs` (`[Trait("Category","GameBench")]`, skipped without `ManagedPath`).
- Game DLLs are referenced with `Private=false` and never copied or shipped.

### 9.2 Oracles: real code where it runs, faithful ports elsewhere

| Rule | Oracle | Status |
|---|---|---|
| Net fold, length, overlap | real `GeometrySystem` + `Game.Net.ValidationHelpers.ValidateEdge` | runs [P] |
| Object overlap and severity | real `Game.Objects.ValidationHelpers.ValidateObject` with real search trees | runs [P] |
| Area / surface shape | real `Game.Areas` `GeometrySystem` + `ValidateArea` | same pattern, not run yet. WP1 adds it |
| Zone blocks | real `BlockSystem` | runs [P] |
| Cells (block, toggle, overlap, depth) | real `CellCheckSystem` (`CellBlockJobs`, `ZoneToggleJob`, `CellOverlapJobs`) | runs [P]. WP1 feeds it areas (our blockers and kerb strips) and existing building lots |
| Zoning paint | real `GenerateZonesSystem` (allow-list) | WP1 |
| Course → edge (Upgraded copied, crossings, attach splits) | real `GenerateNodes/EdgesSystem` + `CourseSplitSystem` (allow-list) | WP1 stretch. Settles the 08 side-frame [H] and the crossing [H] (06 §6.5) |
| Seed pose | real `ZoneUtils.GetPosition`/`GetRotation` | pure, runs [P] |
| Ports with differentials | `ZoneBlockPredictor` vs `BlockSystem` (0 blocks off by > 0.1 m over 500 random curves); `ZoneGuard` openness vs `CellCheckSystem` (0 mismatching cells on the matrix); `NetLegality` cut model vs `GeometrySystem` (prediction ⇒ fold exactly); `AreaRings` vs `ValidateArea`; `Declutter` bounds vs `ValidateObject` | WP1 + the owning WP |
| Not runnable offline | terrain conforming under nets (TerrainSystem shaders), water, map tiles, simulation (demand, companies, condemnation timing), rendering (the farm look), other mods (Platter, Anarchy, EasyZoning, AutoIndustryArea) | covered by the passive in-game data in §9.5 |

### 9.3 Fixtures and calibration
- **Prefab capture** (WP1 in-mod part, read-only, about 1 s, once per game version, on city load in a session the user
  starts anyway). It writes `ModsData/Placemaker/bench-capture-<ver>.json`:
  - net, composition, crosswalk, object geometry, building lot sizes and spawnable zones, zone block data;
  - the component lists of Temp and applied archetypes (a missing component is a process-killing AccessViolation,
    06 §1.4).
  - It is copied into `Tests/fixtures/`. `NetProfiles` is generated from it.
- **Until the capture exists,** every bench verdict prints `UNCALIBRATED`, and the lead may only say "bench-green
  (uncalibrated)".
- **Sites:**
  - S1 square block with four 12 m streets (the 06 v0 site);
  - S2 160×160 and S3 160×110 blocks (the 07 probe);
  - S4 a block between a 4-lane avenue and a 2-lane street (the shot-107 case), with residential zoning on the avenue
    side;
  - S5 70×50;
  - S6 rotated 100×80;
  - S7 L-shape;
  - S8 an irregular quad with one curved street;
  - S9 a corner lot with existing growables and zoned cells (the G-CITY case).
  - Plus every replayed audit site, once audits carry the replay section.
- **Terrain:** T1 flat, T2 6 % slope, T3 ±4 m hills, T4 0.5 m residual roughness after grading. The fixture grid is set
  into `TerrainSystem.m_CPUHeights`; the bench already does this for a flat 2×2 grid (06 §1.3). The plan side gets the
  same `HeightAt`. Water and shore are out of scope offline, because `WaterSurfaceData` is not modelled.

### 9.4 Matrix and gates
- **Matrix:** every type × every preset (292 plans today) × S1–S9 × T1–T4.
- **Tiers:**
  - smoke: default preset × S1, S4 × T1, about 100 plans, ≤ 30 s, run on every change;
  - focus: all presets of Parking + the 8 commercial types + every Parking-planner host × all sites × all terrains,
    ≤ 5 min;
  - full: everything, ≤ 10 min, before any install.
- Gates per plan (A5 extended). A plan passes only when all of them are 0 or true:

| Gate | Condition |
|---|---|
| G-NET | `InvalidShape`, `ShortDistance`, own-vs-own `OverlapExisting`, crossings without a node, pieces < 1 m, unequal node positions at a shared node, illegal attach (N10) on the city edge's split halves. Repairs counted and reported |
| G-OBJ | `Override`/`Error` between our overridable objects (outside the carrier), our objects on our nets, any `Warning`/`Override` against a non-Placemaker entity |
| G-AREA | `InvalidShape` on surfaces, lots, blockers and kerb strips; every surface triangulates |
| G-ZONE | every previewed shop cell is Visible, unblocked, owned by its plot's intended road block and zone None before paint and the plan zone after `GenerateZones`; 0 plots facing a city street with default settings; the seed fits |
| G-CITY | 0 captured occupied or zoned city cells outside the footprint changed; 0 city edges with an illegal split |
| G-HYG | no vanilla industry, extractor or farm lot, crop surface or farm prop in a non-farm plan; no fence on an entrance, front or driveway edge; every lot has an entrance (N8-legal), ≥ 1 double-loaded bay (≥ 18.8 m) and ≥ 30 % stall fill; every stall within 3 m of a drivable lane whose graph reaches a city attach; no node more than 0.3 m below the graded ground (N12); terraform target bowl-free |
| G-PERF | compose p99 ≤ 200 ms (.NET); main-thread Core per plan ≤ 5 ms (.NET) |
| A6 calibration | for replayed audits: ≥ 95 % of the in-game error verdicts reproduced (type + piece + 0.5 m), ≤ 5 % extra |

- **Baseline:** the bench's first run on master must reproduce the v0 failures (104 InvalidShape plans; 6,250 → 3,331
  cells). The gate is credible only if it is red on today's code.

### 9.5 In-game data that rides on sessions the user starts anyway (read-only for the city)
1. The prefab and archetype capture (§9.3).
2. **Every build's audit** gains:
   - type, preset, outline and plan hash (G1);
   - the full plan and site input as a replay section (G4);
   - the live blocks per cell (`m_State`, `m_Zone`, `BuildOrder`) at `ZoningVerify` (G3);
   - every game verdict as {type, prefab, position, plan key} (G5);
   - the census;
   - the list of loaded mods.
   - The lead runs `Host replay` on the file offline.
3. A passive ghost check: after each ghost settles, count `Error`/`Warning`/`Override`/`Cancel` and notification icons on
   our temporaries. They must be 0 under the mute, and the count is logged.
4. The opt-in self-check (06 §8.4) stays off by default.
5. The stripped-down in-game suite (10) is **not** part of the loop. It is for release checks only, run with the user's
   OK while they are not playing.

### 9.6 Reporting rule (A8)
- "Fixed" requires the full gate green for every type sharing the changed path, with the capture version named.
- "Installed" requires an in-game audit that carries the build hash.

---

## 10. What to delete (ruthless)

| Delete | Where | Replaced by |
|---|---|---|
| `Step.ResetForRetry`, `DoResetForRetry`, `_dupRetried` | `BuildService.cs:61–65, 308, 504–506, 526–546, 739` | duplicate = assertion and test |
| The instant-from-ghost build (`_instant`) | `BuildService.cs:380–390`, `BuildService.Ghost.cs:73, 207–226` | one path: the build re-emits the same `PlanWork` |
| Vanilla-rules mode: `AutoExcludeCollisions`, `RecoverRejectedPieces`, `CancelTemporary`, `_cancelledTemps`, the "refuse decorative" branch of `BuildVerdicts`, the settings UI | `BuildService.cs:53–60`; `BuildService.Verdicts.cs:35, 162, 188–215, 245–250`; `Core/Build/BuildVerdicts.cs` | anarchy only |
| The icon-based failure label and histogram (`_errorTypeName`, `ErrorHistogram`) | `BuildService.Adopt.cs:31, 109–125` (+ `ErrorHistogram`); `BuildService.Verdicts.cs:152` | entity-based defect records |
| Build-time terrain split and the `< MinCourseLength` drop in `EmitNets` | `BuildService.Emit.cs:139–148` | plan-time fit (N5, N6) |
| `NetFit` and `MallNetGeometryTests` | `Core/Layers/Commercial/NetFit.cs`; `Tests/.../MallNetGeometryTests.cs` | `NetLegality`/`NetLegalizer` + `NetLegalizerTests` + G-NET |
| `ZoneGuard.Refresh` (grow, shift, `NearestOpenRun`, raw-quad fallback, `LastRefreshReport`), `ownBlockedOpen`, the "ties go to our roads" ranking, `Diagnose` | `Core/Build/ZoneGuard.cs:351–522, 554–575, 604–715, 664–668` | §5.2 |
| `DoZoneRefresh`, the separate ≤ 3-round blocker transaction, `RoadPreZone` for shop hosts | `Game/Build/BuildService.ZoneBlock.cs` | blockers in `EmitRest`; `ZoningVerify` |
| The blocked-cell cause classifier (role default widths) | `BuildService.ZoneCause.cs` (whole file); `BuildService.Terrain.cs:747–788` | live-block dump + offline replay |
| `VerifyZoning`'s early return and "any zone" check | `BuildService.Terrain.cs:646, 672–675` | §5.5 |
| The parking planner's Lot "owner area"; `area.lot` as an automatic category | `PlanBuilder.cs:442–450`; `AssetHeuristics.cs:99`; `AutoAssets.cs:163` (via `PlanWork.cs:96`) | none (the owner lot covers selection); `area.lot` explicit-only |
| Leftover lots, 1-stall lots, the silent NoEntrance | `CommercialLayer.cs:172–173, 318`; `Mall.cs:334, 350`; `MallLots.cs:144–180` | the lot rule (WP4) |
| Street rows on by default | `CommercialLayer.cs:169, 201`; `HighStreetLayer.cs:152` | opt-in |
| `ProtectBuildings` as a failure path | `BuildService.cs:500` | a logged defect (§3.2) |
| Catalog rescans for own prefabs; the forced dump per scan | `AssetCatalogSystem.cs:84–98, 462` | WP6 |
| Throwaway probes | `.forensics/bench`, `scratch-07/08/switchhang` | promoted into `Tests/` (WP1), then deleted |

Kept on purpose: `NeutralizeVerdicts`, `ClearGhostVerdicts` (as a fallback under the mute), `AuditSurvivors` (safety
net that counts defects), the owner lot, the two Harmony patches, the carrier, the companion, `SiteSafety`, `Declutter`,
`ZoneBlockPredictor`.

Decided by the bench rather than by opinion: the −0.01 m decal sink (`BuildService.Emit.cs:30–40`). It is kept only if
`ValidateObject` shows it changes a verdict.

---

## 11. Risks and how each one is tested

| # | Risk | Test (offline unless stated) | Fallback |
|---|---|---|---|
| K1 | Prefab data is assumed, so verdicts are wrong | A6 calibration on the capture and replays; `UNCALIBRATED` banner | conservative defaults (`n` = 1.0 m) |
| K2 | Stricter geometry leaves fewer stalls or plots on tight sites | bench reports stalls and plots per plan against the master baseline; WP2/WP3 acceptance requires ≥ 90 % of the baseline stall count on S1–S3 and every drop explained | preview shows the truth; "won't fit" in Design |
| K3 | Plan-time course heights differ from the live ground after terraform (residual 0.43–0.70 m) | T4 roughness fixture in G-NET; post-Apply bowl audit | corrective pass (§5.6) |
| K4 | The side frame of `ZonesDisabled` after Apply [H] | real `ZoneToggleJob` + `GenerateEdgesSystem` in the bench; the audit logs `Upgraded` + `SubBlock` sides | the planner sets the side from the read-back on the first audited build |
| K5 | Kerb strips vanish without the mod and seeded shops on perpendicular alleys get condemned | documented; the vanilla-flag option | user choice |
| K6 | Seeded growables: companies, abandonment, level-up [H] | re-audit hook in a user session | setting off, so the preview says "grows with demand" |
| K7 | Condemnation of existing growables | G-CITY; post-build watch | none needed if the gate holds |
| K8 | Other mods (Platter's cell pipeline, Anarchy, EasyZoning, AutoIndustryArea) | not offline; the audit lists loaded mods; A6 replays include them | the in-game suite "playset" profile before release (10) |
| K9 | The ghost mute clashes with the Anarchy mod's toggling [H] | passive ghost check (icons = 0) | keep `ClearGhostVerdicts` only |
| K10 | Blockers in the main transaction clip shop cells | G-ZONE with blockers fed into the real `CellCheckSystem`; 0.5 m gap | — |
| K11 | Terrain rollback inexact [H] | log the rollback's "worst" residual; Core test of the snapshot target | accept and log |
| K12 | Burst vs managed float differences in the bench | thresholds are sign tests; A6 catches drift | — |
| K13 | Compose time grows (legalizer, plan-time fitting) | G-PERF; `SwitchBench` (09) | cache per fingerprint (WP6) |
| K14 | A whole-side `ZonesDisabled` upgrade conflicts with an attach on the same edge [H] | bench with `CourseSplitSystem` allow-listed | post-Apply mini-transaction |

---

## 12. Work packages

Order: WP1 first. WP4 and WP6 can run in parallel with it (pure Core / preview path). WP2 and WP3 need WP1's oracle.
WP5 runs in parallel. WP7 comes last. Every builder brief states "apply to every type that shares this path" and lists
the types (user rule).

### WP1: Bench as the gate
- **Goal:** the offline oracle, the matrix, calibration plumbing and the in-mod read-only capture.
- **Files:**
  - `Tests/Placemaker.GameBench{,.Shim,.ShimGen,.Host}/*`;
  - `Tests/Placemaker.Tests/GameBenchGate.cs`;
  - `Tests/fixtures/{sites,terrain}/*`;
  - new `Game/Diagnostics/BenchCapture.cs` (prefab and archetype dump on city load);
  - `Game/Build/BuildService.Audit.cs` (replay section, verdict records, live blocks, mod list, type/preset);
  - `.gitignore` (`.shim/`, outputs).
- **Approach:**
  - promote the `.forensics/bench` prototype;
  - add the Areas oracle, `GenerateZonesSystem`, and areas and building lots as cell-check inputs;
  - terrain fixtures through `m_CPUHeights`;
  - plan export from the Gallery variants;
  - one JSON plan/site format shared by the export and the audit replay section.
- **Acceptance:**
  - A1–A4 and A7 pass;
  - smoke ≤ 30 s, full ≤ 10 min;
  - on master the gate is **red** and reproduces v0 (104 InvalidShape plans, 6,250 → 3,331 cells) with positions per
    failure;
  - `Host replay` reads an audit file and re-runs it;
  - the capture code passes a fake-world unit test and touches no city entity (read-only queries only).
- **Dependencies:** none.

### WP2: Net legality at the source (every net-emitting planner)
- **Goal:** zero `InvalidShape`, `ShortDistance`, own overlap, unnoded crossings and sub-metre pieces. Courses are final
  in the plan.
- **Files:**
  - new `Core/Build/NetProfiles.cs` (+ a `.g.cs` generated from the capture), `Core/Build/NetLegality.cs`,
    `Core/Build/NetLegalizer.cs`;
  - `Core/Composition.cs` (stages after `SiteSafety`: `CourseFit` → `NetLegalizer`);
  - `Core/Build/CourseFitting.cs` (N6 constraints, plan-time);
  - `Core/Contracts.cs` (`PlannedNet.Courses`, node ids);
  - `Core/Layers/Parking/{LayoutEngine, PlanBuilder}.cs` (aligned ends, no stub sections, footpath dead ends, N4 weld,
    N8 stubs + `RemoveCrosswalk`);
  - `Core/Layers/Commercial/{Mall, MallKit, StripMallLayer, OutdoorMallLayer, HighStreetLayer, …}.cs` (angles, spacing);
  - `Core/Layers/Common/PathJoins.cs`;
  - park, leisure, plaza and `RoadsPlanner` path networks;
  - `LotGrading`/`MallGrading` (N11 tier breaks);
  - `Game/Build/BuildService.Emit.cs` (verbatim emit, N5 assert);
  - delete `NetFit.cs` and `MallNetGeometryTests`.
- **Types:**
  - Parking and every host (the 8 commercial types, Rest area, Sports park, Transit plaza);
  - every park, leisure and plaza type with paths (v0 failures: amphitheatre, beach, botanicalGarden, greenwayPark,
    hikingTrail, lookoutHill, park, restArea, transitPlaza, roads, …).
- **Acceptance:**
  - G-NET 0 on the full matrix (all four terrains);
  - repairs ≤ 1 % of nets, each one reported;
  - `NetLegalizerTests` cover N1–N12 with the 03 §3.3 cases (1.6 m jog, 0.75 m piece, 4.0 m alley stub, L120 crossing,
    collinear pair) and the 06 §1.5 golden scenarios;
  - the stall-count ratio is reported (K2).
- **Dependencies:** WP1 (oracle). Calibrated numbers come from the capture; it can start on the defaults.

### WP3: Shops and zoning made certain (every cutout host)
- **Goal:** every previewed shop cell exists after Build, faces our lane and holds the seeded shop shown in the preview.
  No city side wins, nothing is moved, and nothing is lost silently.
- **Files:**
  - `Core/Build/ZoneGuard.cs` (rewrite §5.2), `ZoneBlockPredictor.cs` (side flags), new `Core/Build/ShopSeeds.cs`;
  - `Core/Layers/Commercial/{CommercialLayer, MallKit, Mall, HighStreetLayer}.cs` (P1–P4, side flags on lanes);
  - `Core/Layers/AddOns/EdgeZoningAddOn.cs`; street shops in `LayerBase`;
  - `Game/Preview/SiteCapture.cs` (`m_Zone`, `Occupied`, `BuildOrder`, building lot cells);
  - `Game/Build/BuildService.ZoneBlock.cs` (rewritten as the `ZoningWait`/`Verify`/`Apply`/`SeedShops` steps; blockers
    and kerb strips to `EmitRest`);
  - `BuildService.Terrain.cs` (`VerifyZoning`);
  - `Game/Overlay/PlacemakerOverlaySystem.cs` (seed ghosts, "faces …", hatched removed street cells);
  - UI setting "Street zoning: Off along this build / Off on whole side / Keep" and "Place the first shops on Build".
- **Types:** the 8 commercial types, the Zoning add-on on parks and plazas, street shops in `LayerBase`.
- **Acceptance:**
  - G-ZONE and G-CITY 0 on the full matrix; `ZoneGuard` vs `CellCheckSystem` differential 0 cells;
  - seed pose vs the real `ZoneUtils` ≤ 5 cm;
  - S4 (the shot-107 case): 0 plots facing the avenue, ≥ 1 seed per planned plot;
  - replaying today's audits (once they carry live blocks) reproduces the old losses on the old plan and shows none on
    the new one;
  - `VerifyZoning` tests: a plot with 0 cells yields an audit entry and a UI note.
- **Dependencies:** WP1, WP2 (final lane geometry), WP4 (plot placement rules share the composition).

### WP4: Commercial and parking composition contract (every lot host)
- **Goal:** no farm lots, no fences on access, no unreachable strips, only real lots, one readable composition per site.
- **Files:**
  - `PlanBuilder.cs:442–450` (delete the area);
  - `AssetHeuristics.cs:99`, `AutoAssets.cs` (`area.lot` explicit-only);
  - `Mall.cs:334, 350, 600`; `MallLots.cs:144–180`; `CommercialLayer.cs:158–174, 318`; `TransportKit.cs:114`;
  - fence rules in `LeisureLayer` and the commercial layers;
  - new `Core/Build/PlanHygiene.cs` (G-HYG verdicts as Core code, also used as plan issues);
  - composition templates "strip centre" (street → driveway → lot with aisles parallel to the fronts → sidewalk → shop
    row → optional service lane) and "shopping alley" (07 §6 R1).
- **Types:** Parking and every host listed in WP2.
- **Acceptance:**
  - G-HYG 0 on the full matrix;
  - the 07 probe re-run shows 0 strip lots, 0 `AreaRole.Lot` areas and 0 vanilla lots;
  - every lot has ≥ 1 entrance and ≥ 30 % fill;
  - a Core test proves a hosted Parking sub-plan emits no Lot area.
- **Dependencies:** none for the plan rules; WP2 for "entrance accepted".

### WP5: Build path hardening (keep the entity model; one main transaction)
- **Goal:** no failure after a permanent change; nothing dropped or hidden; honest census; terrain rollback; bowl repair.
- **Files:**
  - `Game/Build/BuildService.cs` (pre-checks before `Terraform`, plan-hash lock, `Paint` after `Settle`, deletions §10);
  - `BuildService.Verdicts.cs` (defect records; vanilla mode removed);
  - `BuildService.Terrain.cs` (snapshot, rollback, corrective bowl pass);
  - `BuildService.Audit.cs` (census-based `Built`, materialisation misses, per-plot entries);
  - `BuildService.Adopt.cs` (icon label removed);
  - `BuildService.ZoneBlock.cs` (blockers in `EmitRest`, shared with WP3);
  - new `Game/Build/GhostErrorMuteSystem.cs`;
  - `GroupRegistrySystem` / `PlacemakerGroupSection` ("Release build").
- **Types:** all.
- **Acceptance:**
  - Core tests: an audit census mismatch produces entries; a plan-hash mismatch blocks the build; a pre-check failure
    produces no terraform call (fake services);
  - the bench stretch gate A5(e) census = plan on the smoke tier once the Apply systems are allow-listed;
  - in game (passive, not gating): the first audited build of the user's next session shows "built exactly as
    previewed" or itemised defects, with 0 silent gaps.
- **Dependencies:** WP1 (audit format); independent of WP2–WP4.

### WP6: Preview stability and the switch hang
- **Goal:** a type or preset switch never freezes; the ghost is rebuilt only when the content changes; the built plan is
  the previewed plan.
- **Files:**
  - `Game/Assets/AssetCatalogSystem*.cs` (no rescan for own prefabs; incremental rescan for others; dump off the critical
    path and off by default);
  - `Game/Build/DecalOpacitySystem.cs` (prewarm after loading completes, ≤ 4 clones per frame, deferred save);
  - `Game/Tool/PlacemakerToolSystem.{Review,Ghost}.cs` (content-hash revision);
  - `Game/Preview/PreviewWorker.cs` (resolve and declutter on the worker);
  - `BuildService.Ghost.cs` (`GhostPerFrame` ≈ 200; nets by connected component);
  - frame watchdog logging.
- **Types:** all (preview path).
- **Acceptance:**
  - `SwitchBench` (09 §8): main-thread Core per plan ≤ 5 ms .NET;
  - unit tests with fakes: adding clone prefabs causes 0 catalog version bumps; a catalog bump with identical resolved
    ids causes 0 ghost revision bumps;
  - in game (passive): the watchdog logs no Placemaker frame above 50 ms on a switch.
- **Dependencies:** none.

### WP7: Migration and removal
- **Goal:** old saves keep working; old defects are repairable with consent; removal covers the new members; "Release
  build".
- **Files:**
  - `Game/Groups/GroupRegistrySystem.cs` (`PostLoadRepair`: outdated groups, fold scan, orphan farm lots);
  - `PlacemakerGroupSection.cs` (Rebuild / Remove leftover farm field / Release);
  - `GroupRemovalSystem.cs` + `Core/Build/RemovalPlanner.cs` (kerb strips, seeds, recorded edge flags);
  - `Core/Build/ReceiptCodec.cs` (`plannerVersion`, type, preset, plan hash);
  - settings migration.
- **Acceptance:**
  - receipt round-trip tests;
  - orphan matching on fixtures;
  - `RemovalPlanner` tests including seeds and kerb strips;
  - bench: the fold scan flags the folded `EdgeGeometry` fixtures and not the clean ones;
  - no automatic change to any existing group, asserted by a test that load without user action issues 0 definitions.
- **Dependencies:** WP3 and WP5 (new member kinds), WP2 (fold scan reuses the oracle fixtures).

---

## 13. Proven vs hypothesis (summary)

**Proven** (cited above):
- Every rule this design relies on: net cut-back and fold, the crossing behaviour, `ZonesDisabled` and `ZoneToggleJob`
  order, the overlap priority, `CleanBlockedCells` propagation, Marquee only painting zone None, the condemnation rule,
  the `ZoneSpawnSystem.Spawn` shape, `DisableInGame` skipping errors, and Apply ignoring `Error`.
- The root causes RC1–RC14.
- That the game's validation, block and cell code runs offline.

**Hypotheses**, each with its settling check:
- the `ZonesDisabled` side frame after Apply (K4, bench);
- a whole-side toggle alongside an attach (K14, bench);
- the `Construction` flag on the Temp seed path (§5.4, first audited session);
- the life of seeded shops without demand (K6, re-audit);
- the ghost mute coexisting with Anarchy (K9, passive check);
- the accuracy of terrain rollback (K11);
- the cause of the shot-106 bowl (N12, bowl audit);
- other mods' interference (K8, audits and the release suite).

None of these blocks goals 1, 3 or 5. K4, K6 and K14 affect goal 2 only for the shop part, and each has a fallback that
keeps the preview honest.
