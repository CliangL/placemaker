# Teardown 04: Shops, from preview to city

Question: why does the preview show shops when the build ends up with none?

Scope: the commercial / strip mall / lot-first types (and every other type that hosts "shops", such as Shops & park or the Zoning feature on parks and plazas).
Code: `master` = 650be0f. Evidence: the user's session today, which ran build 4d62ea8 (so s59, s60 and ff46338 were not installed).
Files: `.forensics/Placemaker-20261015-1152.log` (called LOG below, cited by line), the `group-*.json` audits, and the decompiled 1.6.2f1 game (called DECOMP below, path `C:\Users\jack\AppData\Local\Temp\pm-decomp\game`).

Labels: **[P]** means proven from code or the log. **[H]** means a hypothesis, with what would prove it.

---

## 0. Short answer

1. **Today a "shop" is not an object.** It is a *zoning cutout*: an empty plot along a road. The build paints the plot with the game's zoning (Marquee, commercial zone) and then waits for the city's simulation to grow a commercial building on it. The preview draws coloured **zone cells** and never shows a building. [P]
2. **In today's session, 11 of the 17 previewed shop plots got no zone cell at all (65 %).** Of those 11, **8 were lost without any per-plot audit entry**, and the audits for those two builds report "left out 0". [P, LOG 2010–2027, 2242–2256, 2775–2787, 2800–2811]
3. **The loss happens between our own Apply and the zoning step.** When Placemaker builds its roads, paths and lot, the game rebuilds and re-blocks the zone cells around them, and the cells the plan was drawn on are no longer there or are Blocked. The post-Apply "refresh" (s26/s54/s56) moves a plot by at most 4 m. If it finds nothing, the plot is lost. Our physical "Zone Block" lots then cover that land permanently. [P]
4. **Even a plot that zones correctly is not a shop yet.** The game grows a commercial building only while commercial building demand is above 0. It spawns at most one location per block chunk per 16-frame tick, and only on a lot big enough for some building prefab. The preview presents zone cells as if they were shops. [P, DECOMP ZoneSpawnSystem]
5. **The design is fundamentally fragile.** The preview is a *prediction* of what roughly six game systems will do after Apply. The game itself cannot compute that before Apply, because `BlockSystem` ignores Temp (ghost) roads. Every mismatch between the prediction and the game turns into a missing shop, and the blocker lots then make the loss permanent. Patches s22 → s26 → s36 → s40 → s48 → s54 → s56 → s60 each fixed one way the prediction diverged, in about 30 hours. [P]
6. **The robust fix:** place the shops as real buildings in the same Apply as everything else (the game's own spawn path is a `CreationDefinition` + `ObjectDefinition`), using a runtime prefab clone that the zone check ignores (`SignatureBuildingData`). Zoning then becomes an optional extra that the preview labels honestly ("land the city may grow on"). See §6.

---

## 1. What a "shop" is in today's Placemaker

| Concept | Where | What it is |
|---|---|---|
| Shop plot / cutout | `Mall.Shops`, `Mall.Shop()` (Core/Layers/Commercial/Mall.cs:149), `ShopRow` (:187); `LayerBase` street shops (Core/Layers/Common/LayerBase.cs:763) | A rectangle of whole 8 m cells along a street or a lane, tagged `shops` + `place:shops`, optionally also `zoneCells` |
| Zoning of a plot | `Mall.Finish` (Mall.cs:614) → `EdgeZoningAddOn.Plan` (Core/Layers/AddOns/EdgeZoningAddOn.cs:93) | One `PlannedZoning` quad per plot (the cell rectangle shrunk by 1 m for the Marquee centre test), zone `EU/NA Commercial Low/High` |
| Cutouts stay empty | `Cutouts.Enforce` (Core/Layers/Common/Cutouts.cs:52), run from `Composer.Compose` (Core/Composition.cs:183) | Every object, net and surface of every other layer is removed or clipped out of the plot |
| Plot = live cells | `ZoneGuard.Apply` (Core/Build/ZoneGuard.cs:140), `ZoneLots.Snap` (Core/Build/ZoneLots.cs), `Mall.SnapStreetPlotsToCells` (Mall.cs:707) | Plots are snapped to the captured (existing roads) or predicted (our lanes, `ZoneBlockPredictor`) zone grids. The rules applied are ≥ 2 rows, ≥ 2 columns and a port of the game's depth rule (s36) |
| Not-zonable plots | ZoneGuard.cs:178–197 (s26) | A plot with no cell on a known grid is dropped with `Placemaker.Issue.CutoutNotZonable`. Its land falls to the blockers. Street plots without cells become paved "pads" (Mall.cs:765–775, s46) |
| No street at all | `CommercialLayer.PlanCore` (CommercialLayer.cs:93–100) | The whole site becomes a pocket park ("collapse to park") |
| Blockers | `ZoneGuard.Compute` step 3 (ZoneGuard.cs:851–864), built by `BuildService.ZoneBlock.cs` | Invisible physical lots "Placemaker Zone Block" = footprint minus zoned cells minus unsnapped quads minus existing building lots. They keep the city's zoned buildings off the build |
| Kiosks ("Shops" add-on) | `ShopsAddOn` (Core/Layers/AddOns/ShopsAddOn.cs) | Kiosk **props** beside paths in parks and plazas. They are not involved here; they are placed objects and build like any prop |

What the preview shows for shops: `plan.ZoneCells` with state `Zoned`, drawn as a fill in the zone's colour (`PlacemakerOverlaySystem.DrawZoneCells`, Game/Overlay/PlacemakerOverlaySystem.cs:703; legend `cutouts`, Core/Build/PreviewLegend.cs:30). Cells the blockers will block are drawn grey. **No building is ever drawn.** [P]

## 2. The pipeline, end to end

```
DESIGN (worker thread, pure Core)
  SiteCapture.CaptureZoneBlocks  (Game/Preview/SiteCapture.cs:299)
     live blocks of EXISTING roads: position, direction, size, per-cell Visible/Blocked
     (NOT captured: cell zone type, BuildOrder, Updating, Shared/Redundant)
  Layer plans plots  ->  Mall.SnapStreetPlotsToCells (street plots := cells of the captured grid)
                         lane plots keep their rectangles (lane grids only predicted)
  Composer: Cutouts.Enforce -> SiteHarmony -> SiteSafety -> ZoneGuard.Apply
     ZoneGuard.Compute(..., ownBlockedOpen: TRUE)   <- cells the game blocks INSIDE the footprint count as OPEN
     -> plan.ZoneCells (preview), plan.ZoneQuads (original plots), plan.ZoneBlockers
BUILD (Game/Build/BuildService*)
  DoPrepare: SnapshotPreviewZoning (what the preview showed)
  main Apply (nets, lots, objects, owner lot)   <- the game now rebuilds blocks of split streets,
                                                    creates blocks for our lanes, blocks cells under
                                                    our nets, re-runs overlap/depth rules
  +40 frames  DoZoneRefresh (BuildService.ZoneBlock.cs:80): capture live blocks again,
              ZoneGuard.Refresh (ZoneGuard.cs:368): grow <= 4 m, shift <= 4 m along the road
              (s60: move to nearest open run within 3 cells - NOT in today's build)
              plots that find no cell: their ORIGINAL quad is still painted (ZoneGuard.cs:467)
              blockers recomputed from the refreshed result
  ZoneBlock: emit the physical "Zone Block" lots, validate, apply
  ZoningEmit: Zoning definitions, flags Zone|Marquee (BuildService.Terrain.cs:571/594), no Overwrite
  ZoningApply +40 frames: VerifyZoning (BuildService.Terrain.cs:639) + AuditZonePlots
CITY (simulation, minutes later)
  ZoneSpawnSystem: growable spawns only if commercial building demand > 0 and a building lot fits
  ZoneCheckSystem: a spawnable building is Condemned if any lot cell stops being Visible with its zone
```

## 3. The game's rules that decide whether a cell exists (DECOMP, 1.6.2f1)

- **Blocks are created only for permanent, non-elevated, enabled-zoning road edges.** `Game.Zones.BlockSystem` queries edges with `Updated|Deleted` and **`None = Temp`** (BlockSystem.cs:1152–1165). `CreateBlocks` returns early when the road composition lacks `RoadFlags.EnableZoning`, when any owner up the chain is a `Building`, for elevated or tunnel edges, and for raised or lowered sides (BlockSystem.cs:164–190). **Consequence: the ghost (Temp) never has zone blocks. The game cannot show us the real cells before Apply.** [P]
  - Placemaker's nets are deliberately *not* owned (Game/Build/BuildService.Owner.cs:8–9), so the "one bulldozable object" design does **not** remove zone blocks. Only props are owned, by the parking carrier (BuildService.Adopt.cs:229, 275). [P]
- **What blocks a cell** (`Game.Zones.CellBlockJobs.BlockCellsJob`): any ground net piece whose composition has `CompositionState.BlockZone` (CellBlockJobs.cs:165). Every `RoadPrefab` gets it, and so does every **non-marker** `PathwayPrefab` (Game.Prefabs/NetInitializeSystem.cs:1951, 2102). Physical or protected areas also block (CellBlockJobs.cs:345). Then `CleanBlockedCells` blocks **every cell behind** a blocked one, and row 0 when row 1 is blocked (CellBlockJobs.cs:587–602). [P]
  - "Invisible Road Path - 2xTwoway" is a **PathwayPrefab** (icon `PathwayPrefab.…` in `ModsData/Placemaker/catalog-dump.json`, line ~181100). Whether it blocks cells depends on whether it carries `MarkerNetData`. [H: the next session's s60 cause log, or a prefab-flag dump, settles it.]
- **Overlapping blocks** (`CellOverlapJobs.CheckPriority`, CellOverlapJobs.cs:562–590):
  - (a) If the other block is not being updated, **its visible cell wins** (:564–566).
  - (b) Only for nearly opposite blocks (dot < −0.69, :385): a zoned cell beats an unzoned one, then the shallower depth wins.
  - (c) Otherwise the visible cell wins, then the **older build order** wins.
  - The loser becomes `Redundant` and, in the blocking pass, `Blocked` (:551, :705). [P]
  - ZoneGuard ranks blocks the *opposite* way. Ties go to **our own** lane (ZoneGuard.cs:667–669: "Ties … go to the build's own roads"). Our lanes are always the newest roads, so for every lane that runs at an angle to an existing street within the street's 48 m reach, the game gives the overlap to the street. Example: the s50 lot-first "access alleys straight in from the street" (CommercialLayer.cs:201–233). [P for the rule; H for how often it hits]
- **Depth rule** (`CellReduction.CalculateLeft/RightDepth` + `ReduceDepth`, CellOverlapJobs.cs ~ :180–300 of the struct): a lone column is cut to its neighbours' depth (0). ZoneGuard ports this (s36). [P]
- **Visible** = not Blocked and not Redundant (`CellCheckHelpers.UpdateBlocksJob.SetVisible`, CellCheckHelpers.cs:475–490). [P]
- **Marquee zoning** (`GenerateZonesSystem.MarqueeIterator`, GenerateZonesSystem.cs:38–98): a cell is taken only if it is `Visible`, **its centre is inside the quad**, and (without `Overwrite`) **its zone is None**. Painting a quad over blocked cells does nothing. Painting over cells the player already zoned keeps their zone. [P]
- **Growth** (`Game.Simulation.ZoneSpawnSystem`): commercial spawning runs only if `CommercialDemandSystem.buildingDemand > 0` (ZoneSpawnSystem.cs:1102, `m_MinDemand = 1`, :1117). It evaluates **one best location per chunk** (:162–229) every 16 frames (:1047–1050). A building is chosen only if its `m_LotSize` fits the vacant lot (:309–312) and its per-resource demand ≥ 1 (:318–319). Vacant lots need zoned, unblocked, unoccupied cells from the front row (`LotSizeJobs`, :109–113, :179). [P]
- **Survival** (`Game.Buildings.ZoneCheckSystem.ValidateZoneBlocks`, :334ff and :206): a spawnable, **non-signature** building (:52) is Condemned unless every lot cell is Visible with its zone type and the front is roadside. [P]

## 4. What happened today (LOG)

| Group (type unknown, see note) | Plots previewed | Plots that got cells | Lost | Audited? |
|---|---|---|---|---|
| 1 (12 layers, plots on city "Small Road") | 5 (20 cells) | 3 (12 cells) | 2: "4 open, 6 blocked" / "6 open, 7 blocked" nearby (LOG 2011) | yes, 2021–2026 |
| 2 (12 layers) | 4 (12 cells) | 3 (8 cells) | 1: "6 open, 12 blocked" (LOG 2243) | yes, 2254–2255 |
| 3 (1 layer, small lot-first) | 4 (16 cells drawn, 8 inside the plots) | **0** | 4: "2/3/4/6 open, 20/24/18/27 blocked" (LOG 2776) | **no**; audit "planned 41, built 41, left out 0" (LOG 2787) |
| 4 (1 layer, next to group 3) | 4 (16/8) | **0** | 4: "3/2/6/4 open, 21/25/24/32 blocked" (LOG 2801) | **no**; "left out 0" (LOG 2811) |
| 5–7 (parking lots) | 0 | — | — | (no plots planned, LOG 3052–3053) |
| **Total** | **17** | **6** | **11 (65 %)** | **8 silent** |

Note: the log does not record which type the user picked. Groups 3 and 4 are one-layer builds with a lot ("27 props share the parking carrier owner"), which points to a commercial lot-first type.

What the log proves:
- **The back half works once cells exist.** Where the refresh found cells, Marquee and the blockers zoned all of them: "zoned 12 of 12", "zoned 8 of 8" (LOG 2027, 2256), and no blocker touched a kept cell ("keeping 12 cutout cell(s) free" with no "(bug)" suffix, LOG 2017). **So the loss is at the plan → live step, not at painting.** [P]
- **Most of the lost cells are Blocked by the time the refresh runs.** The plots had 2–6 open cells near them, and the refresh could not reach them within its 4 m grow/shift budget. [P, LOG 2011, 2243, 2776, 2801]. *Which* object blocked them is not in this build's log. The s60 classification (`LogBlockedPlanCells`, BuildService.ZoneCause.cs) is in HEAD but was not installed.
- **"Zoned N of N" and the audit headline compare against the refreshed plan, not the preview.** Group 1 says "zoned 12 of 12" although 20 cells were previewed (LOG 2010 vs 2027). [P, BuildService.Terrain.cs:640–705]
- **"buildings 1" (LOG 2022, 2024)** is measured about 1 s after zoning and counts *any* building whose centre is in the plot (BuildService.Audit.cs:249–275). It is not evidence that a shop grew. [P]

## 5. Every way a previewed shop vanishes, ranked

### V1. Cells under the plot are blocked or moved by our own Apply. Most frequent; proven in the log.
Mechanism [P]:
- Our nets are BlockZone (roads, non-marker paths). `CleanBlockedCells` then kills each column from the first blocked cell back. The build splits city street edges at its entrances, and the game re-creates those streets' blocks on a shifted grid (LOG 2010: "4 on a moved grid"). Our lanes get new blocks that lose overlaps to the older street blocks (rule (c) in §3).
- The preview is computed before any of this, and in a way that cannot see it:
  - `ownBlockedOpen: true` (ZoneGuard.cs:715) treats cells the game blocks *inside the footprint* as open. That includes cells already blocked by neighbouring Placemaker builds, or by Zone Block lots of an earlier group next door (groups 3 → 4 sit about 30 m apart).
  - Our lanes' blocks are only predicted. `ZoneBlockPredictor` models free ends only, and its header says the prediction "only has to be right for the preview and the plan" (Core/Build/ZoneBlockPredictor.cs:8–11).
  - The capture records no `BuildOrder` and no `Updating` state, so the overlap winner cannot be predicted (SiteCapture.cs:323–333).
  - Host content keeps only **0.2 m** off the cells (`ZoneLots.Clearance`, ZoneLots.cs:93; s48 changed it from 2.5 m). The game blocks a cell when the net's real edge or node geometry touches the cell shrunk by 0.02 m. Node caps, curve sampling and kerb aprons easily exceed 0.2 m; s60's "walkway ends close more cells" is one example.
- `ZoneGuard.Refresh` then grows or shifts a plot by ≤ 4 m (ZoneGuard.cs:353–356) and gives up beyond that.

Evidence: 11 of 17 plots, LOG lines above.

### V2. The loss is silent and reported as success. Proven in the log and the code.
- `VerifyZoning` returns at `if (planned.Count == 0) return;` (BuildService.Terrain.cs:646), *before* `AuditZonePlots` (:687). When every plot of a build lost its cells, there is no per-plot audit, no "shown in the preview but not zoned" warning, no `zoning` entry in the audit JSON, and the audit headline says "left out 0" (LOG 2787, 2811). That is why no `group-3`/`group-4` JSON exists in `.forensics`: `FinishAudit` writes the file only when there are entries.
- At the same time the log prints "4 zoning quad(s) emitted" (LOG 2783, 2808). These are the plots' *original* quads (ZoneGuard.cs:467), painted with Marquee over cells that are not Visible, so they zone nothing (GenerateZonesSystem.cs:84–87).

### V3. Blockers make every mismatch permanent. Proven by code.
After the refresh, the blockers are rebuilt as footprint minus *whatever did zone* (ZoneGuard.cs:851–864, applied by BuildService.ZoneBlock.cs). The open live cells that sat next to a lost plot ("4 open", "6 open", …) end up under a physical Zone Block lot. Nothing can ever grow there, not even if the player zones by hand later.

### V4. Zoned is not a shop. Proven by code; the share in today's session is unmeasured.
`ZoneSpawnSystem` needs commercial building demand > 0 (ZoneSpawnSystem.cs:1102), a building whose `m_LotSize` fits (:309–312) and per-resource demand ≥ 1 (:318–319). It picks one location per chunk per 16-frame tick. With low commercial demand, a perfectly zoned plot stays an empty coloured patch indefinitely. The preview presents that patch as a shop, and Placemaker logs no demand figure.
[H: in groups 1 and 2, 4 of the 6 zoned plots showed "buildings 0" 1 s after zoning (LOG 2025, 2251–2253). Logging `CommercialDemandSystem.buildingDemand` at build time and re-auditing after N in-game minutes would measure this.]

### V5. Existing zoning wins. Proven by code; not observed in this log.
Marquee without `Overwrite` takes only cells whose zone is None (GenerateZonesSystem.cs:87). The capture does not record cell zones (SiteCapture.cs:323–333), so the preview cannot know. `VerifyZoning` counts *any* non-None zone as "zoned" (BuildService.Terrain.cs:672–675), so a residential cell reads as a successful commercial shop.
Today's obstacle line shows little existing zoning around the site ("zonedBuilt=5 … zonedUnbuilt=0", LOG 2765), so this is probably not today's cause, but it is a silent path.

### V6. The lane a shop faces is refused or missing. Proven in this session as whole-build failures.
Ghost builds 37–42 were refused with `InvalidShape` on `'Alley' ×3` and invisible paths (LOG 2486–2761). With 4d62ea8 the whole build failed. With s59, anarchy force-applies InvalidShape nets. If the game cannot make a valid edge, the lane gets no blocks and the plots facing it get no cells. [H: needs one session on s59 with an Alley InvalidShape.]

### V7. Overlap order: city street versus our lane. Proven by code; frequency unknown.
See §3. ZoneGuard's ranking (own lane first) contradicts `CheckPriority` (older or non-updating block first, depth rule only for opposite blocks). This hits plots on access alleys perpendicular to a street within 48 m, and corner plots.

### V8. Smaller code-level mismatches. Proven by code.
- The preview count includes the s36 "widened" neighbour column outside the plot quad (preview 4 cells, quad 2 cells). The refresh targets only the cells inside the quad (`planned[i]`, ZoneGuard.cs:376–382), so the refresh is satisfied with fewer cells than were drawn (LOG 2775: "planned 16" vs 2776: "8 planned").
- The "zones from" log lines are indexed by zoning rectangle, not by plot. Plot numbers in LOG 2012–2016 do not match the MISMATCH and audit numbers, which makes diagnosis harder.
- `DirectCause` (BuildService.Terrain.cs) blames any planned net within `DefaultHalfWidth(role)` without checking BlockZone or real width. The s60 cause log can therefore misattribute.

## 6. Judgement on the design, and a robust alternative

### Is "zone blocks + growables + blocker lots" fundamentally fragile? Yes.
1. **No ground truth before Apply.** Temp roads get no zone blocks (BlockSystem.cs:1164). Every cell the preview draws is our prediction of BlockSystem, CellBlockJobs, CellOverlapJobs (including build order), CleanBlockedCells, the depth rule and Marquee's None-only rule. These run on data we cannot capture (BuildOrder, Updating, prefab BlockZone flags, real edge and node geometry).
2. **Every divergence is a lost shop**, and the blockers turn it into permanently dead land (V1, V3).
3. **Even perfect cells are not shops** (V4). The preview promises an outcome that the simulation grants later, maybe never.
4. **Fragile even after success**: the grown building is Condemned as soon as any lot cell stops being Visible with its zone (ZoneCheckSystem.cs:206), for example when a later edit splits a road again.
5. **History confirms it.** Before s19 (10-14 10:38) shops were plain Marquee quads with no blockers: zoning always appeared, but city buildings also grew over the park (the s19 complaint). Since then the pipeline gained cutouts (s14), blockers (s19), cell snapping (s22), refresh and lane prediction (s26), the depth rule (s36), plots = cells (s40), 0.2 m clearance (s48), lot-first (s50), shifted re-snap (s54/s56) and nearest-run (s60). Each patch narrows one divergence, none removes the cause, and each adds a new way to lose a plot. "We didn't used to have problems" refers to 10-13 and early 10-14, before any of this.

### Options compared

| Option | Preview = result? | Independent of demand? | Survives later edits? | Effort / risk |
|---|---|---|---|---|
| A. Keep zoning, more prediction fixes | No (no truth before Apply) | No | No | Endless patching (s22…s60) |
| B. PLT-style: shops only on our **own** zoning road, laid out to be grid-aligned and clear of city blocks; cells written directly (PLT's `ZpSetzeZonentyp` writes `Cell.m_Zone` on Visible, unblocked cells + `Updated`, `ParkingLotTool/Tools/ParkingLotZoningProbe.Report.cs:302–333`) | Better (we choose geometry with no overlaps), but still predicted | No | Partly | Medium; still needs demand |
| **C. Place the shop buildings ourselves** | **Yes**: the ghost shows the exact building | **Yes**, the building appears on Build | **Yes** if it is exempt from the zone check | Medium; see below |

**Recommendation: C, with zoning kept as an optional, honestly labelled extra.**
- **How**: per plot, pick a vanilla commercial *spawnable* prefab whose `BuildingData.m_LotSize` fits the plot. Register a runtime clone with `SignatureBuildingData` added. `ZoneCheckSystem` skips signature buildings (ZoneCheckSystem.cs:52), so the clone survives without zone cells. Emit it like any object, as a `CreationDefinition` + `ObjectDefinition` at `ZoneUtils.GetPosition`-style lot placement. That is exactly how the game itself spawns a growable (ZoneSpawnSystem.cs:614–680, flags `Permanent|Construction`).
- Placemaker already registers runtime prefab clones: the park companion from PocketPark01 (LOG 2040/2042; `ParkCompanionSystem`) and the Zone Block lot (`OwnerAreaPrefabSystem`). The ghost then shows the real model, and the build puts that same model in the same transaction. Preview equals result by construction.
- **Blockers stay**, to keep the city's growables off the rest of the build, and they are cut around our own building lots (the existing `Protected` mechanism).
- **Zoning becomes optional**: "also zone the frontage so the city can add more shops". The preview draws it as "zonable land (grows with demand)", never as shops. When it is on: write cells directly instead of Marquee None-only, never convert a failed plot into blocker land, and always audit per plot, including the zone type.
- **To verify in a session the user starts anyway** [H]:
  - companies move into a signature-flagged commercial clone;
  - the building finds its road edge from the lot front;
  - level-up does not replace it;
  - behaviour when the save loads without the mod (same caveat as the park companions).

### Minimum fixes if zoning stays for now (each applies to every type with cutouts)
1. `VerifyZoning`: never return before `AuditZonePlots`. Audit against the *preview* snapshot (`_previewZoned`), not the refreshed plan. Check that the zone equals the planned zone.
2. A plot that loses all cells after the refresh must not be painted (ZoneGuard.cs:467) and must not be swallowed by blockers. Leave its land unblocked and say so in the UI.
3. Design time: `ownBlockedOpen` must not count cells blocked by *other* groups' lots or nets as open.
4. ZoneGuard's overlap ranking should follow `CheckPriority`: older or non-updating block first, depth only for opposite blocks.
5. Log `CommercialDemandSystem.buildingDemand` with every build that has plots, and label preview cells "zoned (grows with demand)".

## 7. Verifying offline (no game launch in the loop)

- **Game code that can run headless**: the pure statics of `Game.dll` (`Game.Zones.ZoneUtils`: `GetCellPosition`, `CalculateCorners`, `GetCellWidth`, `CanShareCells`, `IsNeighbor`, `GetRoadDirection`). They depend only on `Colossal.Mathematics` and `Unity.Mathematics` (ZoneUtils.cs:1–2), so a net48 test host that references the Managed DLLs can call them and replace our hand ports of these functions.
- **Game code that cannot run headless**: `BlockSystem.CreateBlocks`, `CellBlockJobs`, `CellOverlapJobs.CellReduction` and `GenerateZonesSystem` are private job structs over `BufferLookup`/`ComponentLookup`/`NativeArray`. These need Unity's native allocator (`UnsafeUtility`, an internal call into the engine), so they cannot run outside the player. For these we need faithful ports with **differential tests against live captures**.
- **Make every session the user plays produce test data**: at `DoZoneRefresh`, write to the audit JSON:
  - the live blocks (position, direction, size, per-cell `m_State` and `m_Zone`, `BuildOrder`);
  - our built nets (prefab, real `EdgeGeometry`/node bounds, composition `BlockZone`);
  - the predicted grids and the preview cells.

  An xUnit test replays each file through `ZoneBlockPredictor` + `ZoneGuard.Compute` and fails on any cell difference. This turns today's 11 lost plots into regression cases without anyone launching the game.
- **For option C**, the offline part is pure: lot-size fitting against a prefab dump (add `BuildingData.m_LotSize` and `SpawnableBuildingData` zone to `catalog-dump.json`). The in-game part rides on the next user session: a preview-only self-check that changes nothing in the city, plus the per-build audit "planned N shops, built N".
