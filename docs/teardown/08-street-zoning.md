# Teardown 08: Turning off street zoning so shops face our alley

Question (the user, 12:12): *"What if the streets in whatever you're touching could turn off their zoning, or we could turn off
their zoning? The houses are just going the wrong way around. The zones are on the wrong way."*

Scope: every type with shop plots or zoning cutouts (commercial, strip mall, lot-first, high street, mall district, shops
park, and the Zoning add-on on parks and plazas). It also covers our own roads, not only city streets.
Code: `master` 650be0f. Decompiled game 1.6.2f1 = DECOMP (`C:\Users\jack\AppData\Local\Temp\pm-decomp\game`).
Logs: `.forensics/Placemaker-20261015-master.log` = LOG.
Prototype: `.forensics/scratch-08/CellOverlapPort` (output in `.forensics/scratch-08/prototype-output.txt`).
Labels: **[P]** = proven from code or the log. **[H]** = hypothesis, with the check that would settle it.

This report does not repeat 04-shops-zoning.md (the preview → live loss, blockers, demand). It answers two questions:
*which road gets the cells*, and *how to switch a road side off*.

---

## 0. Short answer

1. **The game already has the user's idea built in.** CS2 1.6.x has a per-side "no zoning" road flag:
   `CompositionFlags.Side.ZonesDisabled` in the edge's vanilla `Upgraded` component. The vanilla service upgrade "Road
   Zones" (prefab `RoadZones`, icon `ZoneToggle.svg`) sets it.
   - Every frame that cells are checked, `Game.Zones.ZoneToggleJob` marks **every cell of a disabled side Blocked**. This
     happens before the overlap pass, so those cells cannot claim anything and the other road's block wins all of that
     land. [P]
   - The flag is plain vanilla data. It is saved with the city, loads without Placemaker, and the player can undo it
     with the vanilla tool. [P]
2. **Why the shops faced the city street.** Two reasons:
   - (a) By design, the plan put most shop plots on the city street's cells (s32/s50 "StreetRows on free frontage").
   - (b) Where our lane competes with a city street for the same land, the game's overlap rules make the **older,
     already-visible city block win**.

   The log shows 23 of 28 plots "zone from 'Small Road' (city)" (LOG 2179–2182, 2974–2992, 3047–3050). A port of the
   overlap rule reproduces shot-107: in a 28 m strip between a city Small Road and our parallel Alley, the city takes
   **every** cell and the lane gets **none** (§6). [P for the rule and the log; the port is a faithful simplification]
3. **Our lane cannot win by geometry or build order.**
   - Perpendicular overlaps: a new block always loses to an existing visible one.
   - Opposite (parallel) overlaps: rows 0 and 1 count as equal depth, ties go to the older block, and one lost row-1
     cell takes row 0 with it (`CellReduction.Perform`).

   The only deterministic fix is to **remove the other road's cells**: switch the side off, or block its row 0 with a
   physical lot. [P]
4. **Recommendation** (§7):
   - **(A) Our own roads** get `ZonesDisabled` on every side that should not host shops (the lot side, the park side).
     This uses the existing `Upgrades` → `Upgraded` path, so it is a one-line plan change, and it removes most of the
     need for blocker lots.
   - **(B) City street sides that face the build** get one of two treatments:
     - the vanilla flag, when that edge side lies along the build and has no zoned or occupied cells elsewhere;
     - otherwise a **1 m "kerb blocker"**: a physical Zone Block lot strip inside the city block's row 0, only along the
       footprint. It kills those columns and nothing beyond the build.
   - **(C)** A user control: **Street zoning: Keep / Off along this build (default when the build has shops) / Off on
     the whole street side.**
   - Never split a city street just to toggle it.

## 1. Evidence from today's session

| Build | Plots | Zone from the city street | Zone from our own road | LOG |
|---|---|---|---|---|
| group 1 (12:10, shot-106 area) | 4 | 4 ('Small Road' (city)) | 0 | 2179–2182 |
| group 2 (12:11) | 19 listed | 15 | 4 ('Pedestrian Street Small' (built by Placemaker)) | 2974–2992 |
| group 3 (12:12, third screenshot) | 4 | 4 | 0 | 3047–3050 |

- In group 1, 26 of the cells next to the plots are blocked by "our DriveAisle net Invisible Road Path – 2xTwoway at row 0
  (CleanBlockedCells: the column behind it is blocked)", and 6 by "our Alley net" (LOG 2176). Our own lanes and aisles
  kill whole columns of the city block. That is the same row-0 propagation we can use on purpose (§4.3). [P]
- The intent comes from the code. `CommercialLayer.LotFirst` puts rows on the free street frontage
  (`mall.StreetRows(runs, …)`, Core/Layers/Commercial/CommercialLayer.cs:169 and :201). The access alleys run straight in
  from the street (:206–235). Their rows lie inside the street's 48 m block reach, and the street wins there (§3). [P]

## 2. How the game makes zone blocks (DECOMP)

`Game.Zones.BlockSystem.UpdateBlocksJob.CreateBlocks` (BlockSystem.cs:164–260) runs per **road edge**, and only for edges
that are `Updated`/`Deleted` and **not Temp** (query, :1164). So ghosts never have blocks.

Per edge:

| Gate | Code | Effect |
|---|---|---|
| `RoadComposition.m_Flags & RoadFlags.EnableZoning` | :166 | prefab-level: no flag, no blocks on either side |
| any owner up the chain is a `Building` | :170–177 | building sub-nets never zone (our nets are unowned, BuildService.Owner.cs:8–9) |
| composition `Elevated`/`Tunnel` | :180 | no blocks |
| side `Raised`/`Lowered` (retaining wall, quay) | :184–189 | that side gets no blocks |
| cell offset per side | `GetCellWidth(width ∓ 2·middleOffset)` :195/:200; `ceil(w/8 − 0.01)` ZoneUtils.cs:150–153 | where the grid starts across the road |
| build order | `max(BuildOrder.m_Start, m_End)` :190 | stamped on each block (`Game.Zones.BuildOrder`) |
| roundabout node blocks | :204–226 | extra blocks around roundabouts |

Block geometry:
- Each side is cut into blocks of at most 10 columns, all **6 rows deep** (`m_Size.y = 6`, :524), i.e. 48 m.
- The block centre sits `24 m` beyond the road's outer edge (:511).
- `m_Direction` points **toward the road** (:522).
- Row 0 is the roadside row: `ZoneUtils.GetCellPosition` puts z = 0 furthest along the direction (ZoneUtils.cs:67–74).

There is **no per-edge "zoning off" in BlockSystem itself**. The side toggle is applied later, to the cells.

## 3. How cells are claimed: which road wins

`Game.Zones.CellCheckSystem.OnUpdate` (CellCheckSystem.cs:215–360) runs these jobs, in this order (:336–345), on every block
inside updated bounds plus the blocks that overlap them (`FindUpdatedBlocksDoubleIterationJob`, :385–404). So when we build
next to a city street, **the street's blocks are re-evaluated too** and have `Updating` set.

1. **BlockCellsJob** (CellBlockJobs.cs):
   - `ClearBlockStatus` resets the state but **keeps `Visible`** (:546–570).
   - A cell (shrunk by 0.02 m, :425) is Blocked if it touches a ground net piece whose composition has `BlockZone`
     (`CheckSegment`, :163ff). Every road and every non-marker pathway has `BlockZone` (see 04 §3).
   - It is also Blocked if it touches a **physical** area (`PhysicalGeometry`, :345). The "Placemaker Zone Block" lot is one.
   - Then `CleanBlockedCells` (:574–602): **row 0 is blocked if row 1 is**, and **every cell behind a blocked cell is
     blocked**.
2. **ZoneAndOccupyCellsJob**: moves zones over from deleted blocks and marks occupied cells.
3. **ZoneToggleJob** (ZoneToggleJob.cs:37–70), described in §4.1.
4. **CheckBlockOverlapJob** (CellOverlapJobs.cs:615–810):
   - In `CheckOverlapX2`, **a pair is skipped if either cell is Blocked** (:523).
   - Pass A marks the losing cell `Redundant` (:545–552) using `CheckPriority` (:562–590):
     ```
     other cell not Updating           -> other wins if it is Visible
     (blocking pass)                   -> I lose if I am Redundant and the other is not
     opposite blocks (dot < -0.6947)   -> zoned beats unzoned;
                                          both unzoned: max(0,row-1) differs -> the shallower row wins
     Visible differs                   -> the Visible one wins   (a NEW block's cells are not Visible yet)
     otherwise                         -> the OLDER build order wins
     ```
   - `CellReduction.Perform(Redundant)` (:82–125) propagates **like CleanBlockedCells**: row 0 if row 1, and everything
     behind.
   - Pass B turns "I am redundant, the other is not" into **Blocked** and propagates again (:684–741).
5. **UpdateBlocksJob.SetVisible** (CellCheckHelpers.cs:475–493): Visible = not Blocked and not Redundant.

**Which road a growable faces.**
- `ZoneSpawnSystem.Spawn` builds at `ZoneUtils.GetPosition(block, lot)` with `ZoneUtils.GetRotation(block)`
  (ZoneSpawnSystem.cs:630–634), i.e. `LookRotation(block.m_Direction)` (ZoneUtils.cs:27–30).
- **A growable always faces the road of the block that owns its visible, zoned cells.**
- `ZoneCheckSystem.ValidateZoneBlocks` later condemns a non-signature spawnable (:52) unless every lot cell is Visible with
  its zone and the front row is roadside (:334–376, `Roadside` test at :361). [P]

**What this means for our lane next to a city street** [P, from the rules above]:
- *Perpendicular overlap* (an access alley running in from the street; corners): no depth rule. On the first check our
  cells are not Visible while the street's are, so the **street wins**. On later checks both are visible, so the **older
  (the street) wins**.
- *Parallel overlap* (a frontage lane or ring road beside the street):
  - The depth rule only separates "row 0–1" from "row 2+".
  - Equal classes fall to Visible, then to age, and the **street wins**.
  - Our lane cells that lose at row 1 also lose row 0 (propagation), so the street ends up with the whole column. §6 shows
    this.
- *Zoned beats unzoned* (opposite blocks only): cells the player already zoned on the street side win. Marquee zoning
  can only paint cells that are already Visible (GenerateZonesSystem.cs:84–87), so we cannot "zone first" to win.

## 4. Every way to stop a road segment from zoning

### 4.1 Vanilla per-side flag `ZonesDisabled` (CS2's own "Road Zones" upgrade) — **use this** [P]

- **Flag**: `CompositionFlags.Side.ZonesDisabled = 0x40000000` (Game.Prefabs/CompositionFlags.cs:77), in
  `Game.Net.Upgraded.m_Flags.m_Left/m_Right` on the edge.
- **Effect** (`ZoneToggleJob`, ZoneToggleJob.cs:37–70):
  - The side of each block of the edge is decided geometrically: `dot(block.m_Direction, Right(edge chord))`. Ties go by
    traffic handedness.
  - If that side's flag is set, every cell of the block is set Blocked.
  - It runs after BlockCells and **before the overlap job**, which skips Blocked cells (CellOverlapJobs.cs:523). The other
    road's cells there are uncontested.
- **Prefab support**:
  - `NetInitializeSystem` adds `ZonesDisabled` to `NetData.m_SideFlagMask` for every road prefab with `EnableZoning`
    (NetInitializeSystem.cs:194–197).
  - `NetPieceRequirements.ToggleZones` maps to the flag (NetCompositionHelpers.cs:338–339). This is the requirement the
    upgrade prefab sets.
  - The vanilla upgrade is in our catalog dump: `"id": "RoadZones"`, `"displayName": "Road Zones"`, icon
    `Media/Game/Icons/ZoneToggle.svg` (ModsData/Placemaker/catalog-dump.json:114135–114146).
- **Composition**: kept on the edge composition (CompositionSelectSystem.cs:477–478). It is dropped only on elevated,
  tunnel, raised or lowered sides (:353–367), which have no zoning anyway.
- **Preview for free**: while an upgrade is Temp, `ZoneCellHighlightJob` sets `CellFlags.Highlight` on exactly the cells a
  disabled side will lose (ZoneCellHighlightJob.cs:45–110; CellCheckSystem.cs:199–224 "TempUpgradeQuery"). So the game
  shows the truth in the ghost.
- **Persistence**:
  - `Upgraded` is `ISerializable` (Game.Net/Upgraded.cs:7–19).
  - Edge replacements that are not upgrades carry the original's `Upgraded` over, inverted if the edge is inverted
    (GenerateEdgesSystem.cs:1408–1416).
  - Cells keep `m_Zone` while Blocked. Switching the side back on restores the player's zoning.
- **Without the mod**: nothing changes. The flag is vanilla, and the player sees the "Road Zones" upgrade on that street
  and can remove it with the vanilla tool.
- **Cost**:
  - The flag works **per whole edge side**. A city edge usually runs intersection to intersection, longer than our
    footprint.
  - Cells of that side outside the build are lost too. The prototype shows 72 cells lost outside the footprint (§6).
  - Existing growables on them are **condemned** (ZoneCheckSystem.cs:334–376).
  - Hence the guard in §7.

**How to write it on an existing city edge.** Two ways:

- **Vanilla tool path** (NetToolSystem.CreateUpgrade(edge…), NetToolSystem.cs:3487–3560):
  - one `CreationDefinition` { `m_Original = edge`, `m_Prefab = PrefabRef(edge)`,
    `m_Flags = Align | SubElevation | Upgrade | Parent` };
  - an `Upgraded` component with the new flags;
  - the current `SubReplacement` buffer;
  - a `NetCourse` over the full edge curve (start/end node entities, rotations, `IsFirst/IsLast`, `m_FixedIndex`);
  - `Updated`.

  `CreationFlags.Upgrade` is required; without it the original's flags are copied instead (GenerateEdgesSystem.cs:1408).
  This rides on Placemaker's normal definition → Temp → Apply path. [P for the game code; H that our transaction accepts
  it unchanged. Settle it with one session building one toggled edge and logging `Upgraded` read back.]
- **Direct component write**: `Add/SetComponentData(edge, new Upgraded{…})` plus `Updated`. ZoneTools (River-Mochi, MIT,
  2026, `Systems/ZoneToolSystem.ExistingRoads.Vanilla.cs`, `SyncVanillaZoneFlags`) ships this through an ECB. Placemaker's
  own note says writing `Upgraded` on a finished edge "crashed CS2 natively (PLT)" (Game/Build/BuildService.Emit.cs:177–178).
  The evidence conflicts. **Prefer the definition path.** [H]

**Our own roads**: no new code path is needed.
- `PlannedNet.Upgrades` already accepts `"left:ZonesDisabled"` / `"right:ZonesDisabled"`. `ParseUpgrades` parses
  `CompositionFlags.Side` names with a side prefix (Game/Build/BuildService.Emit.cs:277–299).
- The flags go onto the definition as `Upgraded` (:176–179), and `GenerateEdgesSystem` puts them on the new edge
  (:1521–1529).
- Left and right are in the edge frame (course start → end). Left = the side where `Right(B−A)` points away.
  - [H: the edge keeps the course direction for two-way lanes. Verify with one build log: read `Upgraded` and the side of
    each `SubBlock` back after Apply.]

### 4.2 Other ways the game does not zone (rejected) [P]

| Way | Why not |
|---|---|
| A prefab without `EnableZoning` (swap the street for a non-zoning prefab or a runtime clone, the PLT "Zoningstrasse" idea in reverse) | replaces the player's street, affects both sides, and breaks without the mod (clone prefab missing) |
| Make the street a sub-net of a building (`Owner` chain contains a `Building`, BlockSystem.cs:170–177) | absurd for a city street; ownership changes bulldoze and upgrade behaviour |
| Raised/Lowered side (retaining wall, quay; BlockSystem.cs:184–189) | changes the road's look and terrain |
| Delete or shrink `Block` entities (the original ZoningToolkit approach: its own serialized `ZoningInfo` component on the edge; a system rewrites the blocks of every Created block, zeeshanabid94/ZoningToolkit `ZoningToolkitModSystem.cs`) | BlockSystem rebuilds the blocks whenever the edge is Updated, so the mod must re-apply forever. Without the mod the blocks come back on the next update. Superseded by the vanilla flag; ZoneTools now syncs to it |
| Harmony patch on BlockSystem/ZoneToggleJob | unnecessary since the vanilla flag exists |

### 4.3 A physical lot strip: the "kerb blocker" [P]

- A physical area blocks every cell it touches (CellBlockJobs.cs:345, :425). `CleanBlockedCells` then blocks **row 0 if
  row 1 is blocked, and every cell behind** (:574–602).
- So a **1 m strip inside the city block's row 0 or row 1, along the footprint only,** kills those city columns entirely:
  6 rows, 48 m deep. Columns outside the footprint are not touched.
- Our lane's cells near the strip are that lane's deepest rows. They are already blocked by the street's road band, so we
  lose nothing we wanted.
- Placemaker already builds this object type: the invisible physical "Placemaker Zone Block" lot
  (Game/Groups/OwnerAreaPrefabSystem.cs:3–47; Game/Build/BuildService.ZoneBlock.cs). Its demolition and overlap guards
  apply unchanged.
- **Without the mod** the lot's prefab is missing, so the area is gone and the city cells come back:
  - *Parallel* strips stay ours: our cells are zoned, and zoned beats unzoned for opposite blocks.
  - *Perpendicular* alleys lose: the older street wins again, and our grown shops would be condemned on the next update
    there.
  - So this is the fallback, not the first choice.

## 5. Interaction with today's "Zone Block" blockers

- Today the blockers cover "footprint − zoned cells − existing lots" (ZoneGuard.cs:851–864). They hide the city's cells
  and our lanes' unwanted cells, and they turn every prediction miss into permanent dead land (04 V3).
- With (A) our non-shop road sides off and (B) city sides off or kerb-blocked, the only cells left in the footprint are the
  shop rows of the intended road. The prototype's 4th scenario has **no blocker lots at all** and the same result.
- Blockers then shrink to:
  - land under the build that an *unswitchable* zoning road reaches (city edges where the guard refuses the toggle, a road
    of another build);
  - the kerb strips.
- They must never again cover a shop plot that failed (04 fix 2).

## 6. Offline prototype: before and after

`.forensics/scratch-08/CellOverlapPort/Program.cs` ports BlockSystem geometry (one block per side, 6 rows, row 0 at the road
edge), BlockCells + CleanBlockedCells, ZoneToggleJob, CheckBlockOverlapJob pass A/B with CheckPriority and CellReduction
propagation, SetVisible and Marquee.

Left out: node-end cuts (`CutStart`), shared cells, end-column depth reduction, occupied cells.

Run: `dotnet run -c Release` in that folder. The full maps are in `prototype-output.txt`.

The city "Small Road" is 16 m (catalog: Small Road netWidth 16) and old (build order 10). Our Alley is 8 m and new (500).
The footprint is x −48..48, and the shop plot is the strip between the roads.
"Lot columns" = columns with ≥ 2 visible rows from row 0, i.e. where a 2-deep commercial lot can grow, facing that road.

| Layout | Mechanism | Shop strip cells: city / our lane | Lot columns facing city / our lane | City cells lost outside the build |
|---|---|---|---|---|
| Parallel, lane 40 m from street (28 m strip) | **today** | **36 / 0** | **10 zoned / 0** | 0 |
| same | city side `ZonesDisabled` | 0 / 33 | 0 / **11 zoned** | 72 (whole edge side) |
| same | 1 m kerb blocker along footprint | 0 / 33 | 0 / **11 zoned** | 0 |
| same | city side off + our lane's lot side off, **no blockers** | 0 / 33 | 0 / 11 zoned | 72 |
| Parallel, lane 56 m (44 m strip) | **today** | 36 / 22 | **10 zoned / 11 zoned (back to back, half face the street)** | 0 |
| same | city side off | 0 / 55 | 0 / 11 zoned | 72 |
| same | kerb blocker | 0 / 55 | 0 / 11 zoned | 0 |
| Access alley straight in (perpendicular) | **today** | **60 / 5+5** | 10 (street) / 1+1 | 0 |
| same | city side off | 0 / 35+35 | 0 / 5+5 zoned | 72 |
| same | kerb blocker (gap at the alley mouth) | 0 / 30+30 | 0 / 5+5 zoned | 0 |

Reading:
- The **today** rows are shot-107: the strip between the outer street and our lane belongs to the outer street, and the
  growables face it.
- In the 28 m strip our lane gets **nothing**. Lane row 1 loses to street row 1 (equal depth class, the street is visible
  and older), and that takes lane row 0 with it (`CellReduction.Perform`, CellOverlapJobs.cs:110–115).
- Either mechanism hands the whole strip to the lane. The vanilla flag costs the rest of the street side (72 cells here).
  The kerb strip costs nothing outside the build but depends on the mod.

Excerpt (28 m strip; UPPER = zoned, lower = visible unzoned, `=` road, `#` blocker, `.` no cell):
```
 today                                         city side ZonesDisabled                       kerb blocker
 35 |cccccc..........................cccccc|   35 |........LLLLLLLLLLLLLLLLLLL.......|   35 |cccccc..LLLLLLLLLLLLLLLLLL..cccccc|
 31 |ccccccccccCCCCCCCCCCCCCCCCCCcccccccccc|   31 |........LLLLLLLLLLLLLLLLLLL.......|   31 |cccccc..LLLLLLLLLLLLLLLLLL..cccccc|
 ...  (CCC down to the city road)              ...  (LLL down to z=13)                       ...
  9 |ccccccccccCCCCCCCCCCCCCCCCCCcccccccccc|    9 |...................................|    9 |cccccc##################....cccccc|
  7 |======================================|    7 |===================================|    7 |==================================|
```

## 7. Recommendation

### 7.1 Mechanism (all types with shops or zoning cutouts, every path)

1. **Our own roads (always, no user choice needed).**
   - When a layer plans a lane or street, it knows which side has shop rows (`Mall.LaneRows(lane, …, side, …)`,
     Core/Layers/Commercial/MallKit.cs:289; street rows; the Zoning add-on).
   - Every zoning side **without** plots gets `"left:ZonesDisabled"` or `"right:ZonesDisabled"` in `PlannedNet.Upgrades`.
     This covers the lot side of a ring road, the park side of a plaza lane, and both sides of a pure access lane.
   - Only roads whose prefab has `EnableZoning` count. Placemaker logs "zone blocks by road: Alley yes, Pedestrian Street
     yes, Pedestrian Street Small yes, Small Road yes, Gravel Road yes" (LOG 163).
   - Effect: our cells exist only where we want shops, so the blocker lots are no longer needed against our own lanes.
2. **City streets touching the build, when "Street zoning" = Off along this build (default when the build has plots).**
   - For each live city edge whose zone band reaches the footprint, take the side facing the footprint (the sign of
     `dot(centroid − chord-mid, Right(chord))`, the same test as ZoneToggleJob).
   - Then:
     - **(a) Vanilla flag** when the side is safe:
       - at least about 80 % of that side's block cells lie inside the footprint or under our nets;
       - **and** no cell of that side outside the footprint is zoned or occupied. This is the same guard as ZoneTools'
         "ProtectOccupiedCells/ProtectZonedCells". It needs `SiteCapture` to also record `Cell.m_Zone` and `Occupied`,
         which 04 V5 asks for too.
     - **(b) Otherwise, a kerb blocker**: a strip 1 m wide, from 0.5 m to 1.5 m inside the street's row 0, along the
       footprint's frontage only, with gaps at our entrances. Build it in the existing ZoneBlock transaction.
   - **Never** split a city edge just to toggle it. Splits rebuild that street's blocks on a shifted grid; see 04 V1 and
     LOG 2177 "20 on a moved grid".
3. **"Off on the whole street side"** (explicit user choice): the vanilla flag on every touching side. The guard still
   reports how many zoned or occupied cells and buildings outside the build would be lost; growables there would be
   condemned. Ask before applying if there are any.
4. **"Keep"**: today's behaviour. Shops may face the street, and the preview must say so per plot ("faces Mill Street").

### 7.2 Where in the build

- **Our roads**: in the main Apply, via `Upgrades`. They are in the ghost too.
- **City edges**: as upgrade definitions in a **post-Apply phase**, before `DoZoneRefresh`.
  - The phase targets the live (possibly split) edge entities.
  - After it, wait for one cell check, so that the refresh, blockers and Marquee see the final cells.
- **Ghost**: emit the same upgrade definitions for city edges the ghost does *not* split. The game then highlights the
  cells it will drop (ZoneCellHighlightJob). [H: an upgrade definition and an attach on the same original edge in one
  transaction may conflict; if they do, highlight our predicted cells instead.]
- **Group record**: store each toggled city edge (entity, side, previous flags, the curve end points as a fallback key).
  - **Removing the build restores the previous flags** through the same upgrade definition. This mirrors the blockers,
    which go with the build.
  - If the edge was replaced, find it by curve and say so in the log. The player can always fix it with the vanilla
    "Road Zones" tool.

### 7.3 Planning and preview (Core)

- `ZoneBlockPredictor` takes a per-side "zones" flag and does not emit grids for disabled sides.
- Captured city grids on toggled sides are dropped from the plan, and the kerb strip is added as a physical blocker.
- With competition removed, **the predicted cells of the intended road are exact up to its own geometry.** No overlap
  ties, build order or `Updating` state is involved; the preview problem of 04 §6.1 disappears for these plots.
- Fix `ZoneGuard`'s ranking (ZoneGuard.cs:645–668, "ties … go to the build's own roads") to follow `CheckPriority`, for the
  "Keep" mode and for city sides we could not switch off.
- Plot depth: the usable rows are the intended road's rows that do not touch another road band, and with "Keep" also do
  not tie with it. In a strip of width S between two roads (edge to edge) that is `floor(S/8)` rows. The remainder
  (4 m in the 28 m example) has no cells and should be planted or paved.

### 7.4 User-facing control

- In every type with shops or zoning: **"Street zoning" — Keep · Off along this build · Off on whole street sides**. The
  default is "Off along this build" whenever the plan has plots. The tooltip says, in plain words, that houses and shops
  will face our lanes, not the street.
- Preview legend:
  - street cells that will be switched off, drawn as hatched grey;
  - each plot's facing road named;
  - with "whole side", the count of city cells and buildings affected outside the build.
- Audit, per plot: "zoned by 'Brookhaven Lane' (ours), faces it". This is an error if it names a road the plan did not
  intend.

### 7.5 To verify in one in-game session (all [H])

1. Our lane built with `"right:ZonesDisabled"`. Log the edge's `Upgraded` and, for each `SubBlock`, its side and Blocked
   count. This confirms the left/right frame after Apply.
2. One city edge toggled through an upgrade definition after Apply. Check:
   - `Upgraded` is present;
   - every cell of that side is Blocked within a few frames;
   - the cells of our lane on the strip are Visible;
   - nothing crashed (this settles the PLT crash note).
3. A kerb strip 1 m wide inside row 0: the city columns along the footprint are all Blocked, and those outside are
   unchanged.
4. Save, then load without Placemaker: the toggled side stays off, and "Road Zones" shows it as set.

## 8. Side observation (not this task's scope)

`catalog-dump.json` tags the vanilla upgrade prefabs `RoadZones`, `Quay01` and `RetainingWall01` as `fence` / `auto:fence`
(lines 114106–114146). They are upgrade prefabs, not placeable fences.
[H: if one is eligible in the fence pool ("fence 153/176", LOG 64), a fence could resolve to an upgrade. Check
`AssetHeuristics`/`AssetCategories` for the exclusion.]

## Sources (web)

- Paradox, "office evolution and city stations available now" (toggle zoning on either or both road sides):
  https://www.paradoxinteractive.com/games/cities-skylines-ii/news/office-evolution-and-city-stations-available-now
- patched.gg, release notes (Toggle Zoning tool, March 2026):
  https://patched.gg/games/cities-skylines-ii/office-evolution-city-stations-available-now
- ZoningToolkit (block-rewriting approach, own `ZoningInfo` component): https://github.com/zeeshanabid94/ZoningToolkit
- ZoneTools (syncs the vanilla `ZonesDisabled` flags, protect-occupied/zoned guards): https://github.com/River-Mochi/ZoneTools

## Skeptic review

Reviewer: independent re-check, 2026-10-15. Read-only. I re-read every cited decompiled file (DECOMP), the log lines, the
audits `group-*-20261015-12*.json`, both screenshots, and the code. I also re-ran the prototype
(`dotnet run -c Release --no-build`); its output is byte-identical to `prototype-output.txt`.
Verdicts: **CONFIRMED** = matches code, log or screenshot. **WRONG** = contradicted. **UNPROVEN** = plausible, but no
evidence here.

### Game rules (DECOMP)

| # | Claim | Verdict | Evidence |
|---|---|---|---|
| G1 | A per-side `ZonesDisabled` flag in `Upgraded` blocks every cell of that side's blocks | CONFIRMED | ZoneToggleJob.cs:36–72. The side test is `dot(block.m_Direction, (x.y,-x.x))`; a tie goes by traffic hand. CompositionFlags.cs:77 `ZonesDisabled = 0x40000000` |
| G2 | It runs before the overlap pass, and the overlap pass skips Blocked cells | CONFIRMED | CellCheckSystem.cs:344 (toggle after BlockCells and Occupy) → :346 (CheckBlockOverlapJob depends on it). CellOverlapJobs.cs:523 |
| G3 | Prefab support: `EnableZoning` roads get `ZonesDisabled` in the side mask; `ToggleZones` maps to the flag | CONFIRMED | NetInitializeSystem.cs:194–197; NetCompositionHelpers.cs:338–339 (it sets `m_Right` only, and the tool handles the side) |
| G4 | The flag is saved with the city and works without the mod | CONFIRMED (code) / UNPROVEN (runtime) | `Upgraded : ISerializable` (Game.Net/Upgraded.cs). A load without the mod is untested, and the report's own §7.5 item 4 says so. The summary's "What's proven" list should not include it |
| G5 | "You can undo it with the vanilla tool" | UNPROVEN | No runtime check. The `RoadZones` prefab exists (catalog-dump.json:114135–114146) |
| G6 | "The ghost highlights the cells that will go" | Partly CONFIRMED | ZoneCellHighlightJob.cs reads `SubBlock` from `temp.m_Original`, so it works **only for an upgrade of an existing edge**. It never works for our new roads (no blocks while Temp). It runs **only on frames with no zone, net, area or object updates** (CellCheckSystem.cs:218–224). Whether the grid is drawn while Placemaker's tool is active is UNPROVEN |
| G7 | CheckPriority order: not-Updating other → Visible → (opposite only) zoned beats unzoned → depth class `max(0,row-1)` → Visible → older build order | CONFIRMED | CellOverlapJobs.cs:562–588. The depth test is set only when `dot(dir1,dir2) < -0.6946584` (:385) |
| G8 | New blocks start with non-Visible cells, so a new perpendicular block loses to a visible existing one | CONFIRMED | BlockSystem.cs:540–544 creates the cells as `default`; CheckPriority :583–585 |
| G9 | Row-0/row-1 propagation (CleanBlockedCells, CellReduction) | CONFIRMED | CellBlockJobs.cs:574–602; CellOverlapJobs.cs:110–130 |
| G10 | A physical area blocks the cells it touches | CONFIRMED | CellBlockJobs.cs:345, :421–430. The log says our Zone Block lot has `PhysicalGeometry` (LOG 2184) |
| G11 | Lost cells condemn growables | CONFIRMED | ZoneCheckSystem.cs:334–376 (`ValidateZoneBlocks`). Removal is gradual (condemned, then demolished) |
| G12 | Our own nets can already carry the flag | CONFIRMED | `ParseUpgrades` BuildService.Emit.cs:277–299; the definition gets `Upgraded` at :179; the crash note is at :177–178. Calling it a "one-line plan change" is optimistic: the commercial lanes (`Mall.AddLane`) would need side information passed through to `PlannedNet.Upgrades` (Core/Contracts.cs:659). Today only RoadsPlanner sets it (RoadsPlanner.cs:346) |
| G13 | ZoneGuard ranks ties to our roads, the opposite of the game | CONFIRMED | Core/Build/ZoneGuard.cs:664–666 versus CheckPriority :587 (older wins) |

### Evidence from the session

| # | Claim | Verdict | Evidence / correction |
|---|---|---|---|
| E1 | "23 of 28 plots zoned from the city street" | WRONG count, and it overstates what the log shows | LOG 2179–2182, 2974–2992 and 3047–3050 hold **27** plot lines, 23 of them 'Small Road' (city). More importantly, each line names the road of a *visible cell within 5.7 m of the refreshed plot centre* (BuildService.ZoneBlock.cs:111–163, `RoadOfCell`), not the road the plot was zoned from. Many of those plots got **no cells at all**: group 2 MISMATCH "#1 2->0 … #17 1->0" (LOG 2973); group 3 says "4 plot(s) cannot zone" (LOG 3046); the audits list plots with "live 0" (group-2-…121141.json: 8 plots; group-3-…121226.json: 8 plots). Correct wording: **of the plots, most sat on land that the city street's blocks hold, and many got nothing** |
| E2 | (a) "By design, the plan puts shop plots on the city street's cells" | CONFIRMED | CommercialLayer.cs:169 and :201 `mall.StreetRows(runs, …)`, the s50 summary at :158–162. **This is a planner defect against the user's requirement 1**, and the report does not draw that conclusion (see C1) |
| E3 | "26 cells blocked by our DriveAisle at row 0 … our lanes kill whole columns **of the city block**" | Count CONFIRMED (LOG 2176: x26, Alley x6); ownership UNPROVEN | The log line does not say whose block those cells belong to. They could be our own lanes' blocks |
| E4 | "A 28 m strip between a city Small Road and our parallel Alley … matches shot-107" | UNPROVEN, and partly contradicted | shot-107 shows two fenced construction sites at a corner. They front a **multi-lane avenue** (several lane dash lines and turn arrows; it does not look like a 2-lane Small Road), with Vermont Street at the corner and a paved street with lamps behind ("…er Street", possibly one of our pedestrian streets; the name is cut off). No Alley of ours is visible, and the strip width cannot be measured. The roads Placemaker built next to shops in the log are **Pedestrian Street Small** (LOG 2975–2977), not Alley. The prototype shows the mechanism; it does not reproduce this screenshot |
| E5 | The prototype numbers (36/0, 0/33, 72 lost, …) | CONFIRMED as model output | They were reproduced. "72 lost" depends only on the prototype's chosen edge length (−96..96 against a footprint of −48..48); it is not a measured cost. The table row "Access alley today: 10 (street) / 1+1" quotes *visible* columns. After Marquee the prototype shows **0 ZONED** on every block there (prototype-output.txt:331–345), unlike the other rows, which quote zoned columns |

### Conclusions and recommendations

| # | Claim | Verdict | Note |
|---|---|---|---|
| R1 | "Where our lane and an older street reach the same land, the city wins, parallel or perpendicular" | CONFIRMED when both are unzoned and Updating | Exception from the same rule: for opposite blocks, a zoned cell beats an unzoned one (:574–577) |
| R2 | "Our lane can't win by layout or build order" | WRONG as stated in the summary (build order: CONFIRMED) | Layout does change the outcome. The report's own 44 m case gives the lane 11 zoned columns (prototype-output.txt:149–160). Any `BlockZone` net through the city's row 0/1 kills those city columns, which is what the log shows our aisles doing (LOG 2176). Correct claim: **in a strip narrower than about 40 m, the lane cannot take the city's cells; only removing the city's cells works there** |
| R3 | "The only reliable fix is to remove the other road's cells" | CONFIRMED for contested land | — |
| R4 | The kerb strip "disappears if the mod is removed" | UNPROVEN, but labelled [P] in §4.3 | The behaviour of a save holding a missing runtime prefab ("Placemaker Zone Block") was not tested. The consequences that follow from it ("parallel keeps its land, perpendicular is demolished") are also UNPROVEN, and the parallel case holds only if the city cells were never zoned by the player. When both are zoned, the older city block wins (:583–587) |
| R5 | Kerb strip "kills those columns and nothing beyond the build" | CONFIRMED by rule and model | Not tested in game |
| R6 | Default "Off along this build" whenever a build has shops | Needs a correction (C1) | — |

### Corrections

- **C1 (most important).** Most of the plots that face the city street are put there **on purpose** by `StreetRows`
  (CommercialLayer.cs:169, :201). Switching off the street side along the build, by flag or by kerb strip, would wipe out
  the land of those plots. The fix order must be:
  1. the planner stops placing street-frontage plots, or turns them into lane rows (requirement 1: "never the outer city
     street");
  2. then our own non-shop sides get `ZonesDisabled`;
  3. only then the city side is switched off or kerb-blocked.

  The zoning switch alone, on today's plan, would turn the 23 street plots into dead land.
- **C2.** A kerb blocker that survives without the mod may be possible. A vanilla net with `BlockZone` along the city's
  row 0 (the vanilla `Invisible Road Path` / invisible paths, catalog-dump.json:180291 "source:vanilla") blocks cells the
  same way (LOG 2176). Whether such a path is a usable non-traffic kerb, and whether invisible pedestrian paths carry
  `BlockZone`, is UNPROVEN. It belongs on the in-game test list next to §7.5.
- **C3.** Move "survives a save without the mod" and "undo with the vanilla tool" out of the summary's "What's proven".
  They are code-plausible and runtime-untested.
- **C4.** Unchanged: the whole-edge cost, condemning growables, "never split a city street", the side-frame [H], and the
  definition path over a direct `Upgraded` write.
