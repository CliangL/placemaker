# Teardown 07: every defect in the 12:09–12:13 commercial / shop builds, and what the shop system is trying to be

Question: the user's playtest on master 650be0f (00-user-feedback-1215.md): *"shops on the wrong side… why are we doing
farmland… how are you supposed to even park over there? There's just a fence… I can't even tell what this is trying to be."*

Scope: the three commercial builds of 12:10–12:12 (shot-106, shot-107, the unsaved 12:13 screenshot) and every type that
shares their code: all eight Commercial types, and every type that uses the Parking planner.
Code: master 650be0f, which is installed. The files cited here did not change in d9f6df6 (s61 touched only roads).
Evidence:
- LOG = `.forensics/Placemaker-20261015-master.log`
- PLAYER = `.forensics/Player-20261015-master.log`
- the audits `group-{1,2,3}-20261015-12*.json`
- DECOMP = decompiled 1.6.2f1 game (`%TEMP%\pm-decomp\game`)
- PROBE = an offline planner run, written for this report: `.forensics/scratch-07/probe` (`dotnet run -c Release`). It plans
  the types on a clean block with city streets on all four sides and resolves automatic assets with the game's
  `area.lot` pool order.

Labels: **[P]** proven from code, the log or the probe. **[H]** hypothesis, with the check that would settle it.

This report does not repeat other teardowns:
- 04-shops-zoning.md covers the preview-to-live loss of cells, blocker lots and demand.
- 08-street-zoning.md covers the game's cell-overlap rules and the per-side "ZonesDisabled" fix.
- 10-ingame-suite.md covers the in-game test harness.

Where those reports already prove a cause, this one cites them.

---

## 0. Short answer

1. **The farmland, the fences and the "no way in" have one cause, and it is not a surface rule.**
   - Every parking lot the Parking planner makes carries a lot "owner area" with an empty asset (`PlanBuilder.cs:448`).
   - The commercial kit merges it into the build (`Mall.cs:600`).
   - The automatic asset resolver fills an empty `AreaRole.Lot` from the `area.lot` pool (`PlanWork.cs:96`,
     `AutoAssets.cs:163`). Every entry in that pool is a vanilla industry lot: "Agriculture Lot", "Agriculture Lot - Field",
     "Extractor Lot", "Forestry Lot"…
   - So every parking lot is built inside a **vanilla farm-field lot**. In game this lot is named "Agriculture Lot - Field"
     (LOG 2971, 3044). It is physical (it blocks zone cells), and the city does not count it as part of our build
     (PLAYER 571–626 shows another mod treating it as an industry area).
   - The striped crop texture is the field. The wooden rail fence around each lot and strip is almost certainly the field's
     own border fence.
   - Proof that the planner itself does not make them: in PROBE, no commercial type plans a crop surface or a single fence
     net. **[P for the lot and its pick; H, but strongly supported, for the fence being the field prefab's border object]**
2. **The shops face the outer street for three reasons that add up.**
   - (i) By design, the s32/s50 planner puts many plots on the city streets' frontage.
   - (ii) Where a lane plot sits inside a city street's 48 m block reach, the game gives the cells to the older street
     (08 §3).
   - (iii) **New finding:** when a lane plot has lost its cells, the s60 zoning refresh moves it onto the *nearest open cells
     of any road*, including the outer street (`ZoneGuard.cs:430–441, 486–522`).

   Result: 23 of the 27 surviving plots zone from a city "Small Road" (LOG 2179–2182, 2974–2992, 3047–3050). **[P]**
3. **The parking strips with no access** come from the s50 "lot first" rule: every leftover piece of at least 220–400 m²
   becomes a parking lot (`CommercialLayer.cs:172–173, 318`; `MallLots.cs:144–180`).
   - PROBE: a Strip mall on a 160 m block makes two 747 m² lots, each 12 m wide, with 6 stalls each, wedged between our
     lane and the city street.
   - In game, the farm-field fence then closes them off. **[P for the planner; H for the fence]**
4. **The empty asphalt lot (12:13).** Group 3 planned only 6 stall markings for the whole build (LOG 3042). The game built
   all 6, so the loss is in the planner. **[P]**
   - PROBE plans 68–75 stalls for the same type on a clean block, flat or on a 6 % slope. The difference therefore comes from
     the real site's inputs. **[H; needs the 06 site replay]**
   - Why such a lot is still built: `Mall.Lot` accepts any lot with at least one stall (`Mall.cs:334`). Only leftover lots
     need 4 stalls and a fill ratio (`MallLots.cs:173–177`). **[P]**
5. **The terrain pit** was not dug by Placemaker's terraform.
   - The target there is 895.63 m, and the terraform reached its target before Apply (LOG 1982).
   - After the build the live ground was 889.54 m (LOG 2193, "the game's ground differs from the target").
   - The pit guard only edits our own target (`BuildService.Terrain.cs:144–154`). The live check only *logs* bowls and never
     repairs them (`:160–176`). So no guard exists against ground that the game moves after Apply. **[P]**
   - What dug it: most likely a net (lot kerb connector or lane end) pinned to the lower city street. **[H]**
6. **Our lanes get real street names** ("Summerfield Lane", "Brookhaven Lane", "Moore Street"…). They are vanilla
   `Alley` / `Pedestrian Street Small` roads, and the game puts every edge of a road prefab with an aggregate type into a
   named aggregate (DECOMP `AggregateSystem.cs:642`, `NetGeometryPrefab.cs:22`). This is a design choice, not a bug: our
   lanes are public city roads. **[P]**
7. **Zoning 36 → 22 / 12.** The cells near the plots are blocked mostly by **our own build**:
   - our pedestrian lanes: 68–70 % of the classified blocked cells, with an over-attribution caveat;
   - our lot aisles: 15 %;
   - our farm-field lots: 9–11 %;
   - the game's depth rule / city nets: 4–7 %.

   None are blocked by Zone Block lots. The cells that do survive are the outer street's. **[P for the counts]**
8. **Verdict on the design.**
   - A user who asks for "a strip mall" expects one row of shops whose fronts face one shared parking lot, with a driveway
     from the street.
   - Today's planner produces something else: a public road loop, lots filling every leftover (inside farm fields), and
     empty zoned plots that the city fills from whichever street wins the cells. Those are usually the outer streets,
     facing away.
   - The composition rules (§6) and the asset contract (fixes F1–F3) both need to change; patching the zoning further will
     not fix it.

---

## 1. What was built, 12:09–12:13

The log never records the type or preset, so builds are identified from their signature in the log. That gap is itself a
defect (F12).

| Group (LOG) | Signature in the log | Identified as | Stalls | Plots → cells | Zones from |
|---|---|---|---|---|---|
| 0, 12:09:38 (1777–1787) | 123 road edges, "loose build: 124 parts are ordinary city content" (1784) | **Roads** type: the city streets Vermont, Mill, Mulberry, Summit… (built by Placemaker, handed to the city) | – | – | – |
| 1, 12:10:00 (1979–2203) | 1 layer, lanes `Alley` (2176), lot aisles, 49 stall markings (1988), 3 plots of up to 9 cells in one row (2187–2189), EU Commercial Low | **Strip mall "Classic"** (the default) **[H]**: street → lot → U-shaped Alley → shop row behind it (`StripMallLayer.cs:68–93`). Shopping plaza "Court" has the same geometry. **shot-106** | 49 | preview 17 → 20 (2177) | 4 of 4 city 'Small Road' (2179–2182) |
| 2, 12:11:35 (2961–3026) | `Pedestrian Street Small` lanes (2971), 20 plots of 1–2 cells (fine grain), side lots, 7 stalls (2969), Commercial Low | **High street, default "Market"** **[H]** (`HighStreetLayer.cs:46`: pedestrian lanes, grain "fine" = 1-column plots, side lots, outer rows on) **shot-107** (plots 16, 17, 19 have buildings, 3012–3015) | 7 | 36 → 22 (2972) | 15 city / 4 ours (2974–2992) |
| 3, 12:12:23 (3035–3073) | `Pedestrian Street Small`, **EU Residential Mixed** (3054), 8 lane plots of 2–3 cells in two rows 34 m apart, 6 stalls (3042), on a slope ("terraform incomplete", 3037) | **High street "Farmers' market"** (`HighStreetLayer.cs:47`, the only Mixed preset with pedestrian lanes, side lots and a tree-lined market strip) or **Outdoor mall "Village"** (`OutdoorMallLayer.cs:49`) **[H]**. **12:13 screenshot** | 6 | 36 → 12 (3045) | 4 of 4 city (3047–3050); all 8 lane plots lost (3054–3061) |

PROBE rules out Mall district "Mixed use": it plans no lot at all.

## 2. What the shop system is trying to produce

In plain words, the planner tries to produce this:

> A shopping site is a parking lot with shops around it. The type lays out its own lanes (real city roads: vanilla
> "Alley", or "Pedestrian Street Small" for the pedestrian types) joined to the existing streets. Along the lanes, and along
> the city streets, it leaves **empty plots** and paints them with commercial zoning. **The city's simulation is meant to
> grow the actual shops** on those plots later. Every piece of leftover land becomes another parking lot (s50 "lot first",
> `MallLots.cs:1–5`), and what no lot can use becomes lawn and trees.

What each type intends, in the code's own words:

- **Strip mall classic** (`StripMallLayer.cs:1–8`): "street → parking lot → a lot lane → a row of shops facing the lot
  across the lane → a green buffer behind."
- **High street** (`HighStreetLayer.cs:1–7`): two pedestrian streets with a market strip between them, small shops on the
  outer side of each lane facing the market, side lots on the main street, plus a row of shops on every city street around
  the site (`outerRow` defaults on, `:38`, `:152`).
- **Lot first** (`CommercialLayer.cs:158–174`): every free street frontage gets shop rows facing the **street**
  (`StreetRows`, `:169`; `MallKit.cs:353–357`, "so the street — not the lane — zones them"), plus access alleys with shops
  on both sides. The land behind becomes lots of whatever shape is left.

What it does not produce: **a single shop building**. Each "shop" is a zoning cutout (04 §1). Which way the building faces
is decided by whichever road the game gives the cells to, not by the planner.

---

## 3. The defects, one by one

### (a) Growables face the outer city street instead of our lane — planner design + planner bug + game rule

What the user saw: in shot-106 the buildings under construction sit between our "Summerfield Lane" and Summit Street and
face Summit Street, with a strip of lawn left between them and our lane. In shot-107 they face the outer street.

Proof: 23 of the 27 plots that kept cells "zone from 'Small Road' (city)" (LOG 2179–2182, 2974–2992, 3047–3050). In
group 3, *every* lane plot was lost (3054–3061), and the only zoning left came from the outer street. **[P]**

Causes, in the order they act:

1. **Design (planner).** Plots facing the city street are planned on purpose:
   - Lot first: `mall.StreetRows(runs, …)` on all free frontage (`CommercialLayer.cs:169`, `:201`).
   - High street: outer rows on by default (`HighStreetLayer.cs:38`, `:152`).
   - `StreetRows` keeps those plots "a whole cell away from every lane … so the street — not the lane — zones them"
     (`MallKit.cs:353–357`).

   PROBE on a clean block, counting zoning quads whose edge lies on the city street versus on our lane:

   | Type | City street only | Our lane only |
   |---|---|---|
   | Strip mall | 8 | 11 |
   | High street | 13 | 30 |
   | Farmers' market | 13 | 16 |
   | Outdoor mall village | 14 | 10 (+3 on both) |
   | Shopping plaza court | 14 | 10 (+1 on both) |

   Roughly half the shops face outward **by design**. **[P]**
2. **Plot acceptance (planner).** `Mall.Faces` accepts a plot if *any* side runs along *any* road (`Mall.cs:161–176`).
   - The Strip mall's shop row behind the U lane (`StripMallLayer.cs:91`) is accepted even when the far city street is
     within 48 m of it.
   - In the game, that street's existing, visible block wins every overlapping cell (DECOMP `CellOverlapJobs.cs:562–566`,
     detailed in 08 §3).
   - ZoneGuard predicts the opposite: "ties … go to the build's own roads" (`ZoneGuard.cs:666–668`). The preview therefore
     shows lane shops that the game will not give to the lane. **[P]**
3. **New: the s60 refresh relocates lost lane plots onto the city street (planner bug).** When a plot finds no cell after
   Apply, `ZoneGuard.Refresh` calls `NearestOpenRun` (`ZoneGuard.cs:430–441`).
   - It searches **every** live grid within 3 cells (`:486–522`) and never checks which road the plot was planned to face.
   - Group 1 says "2 moved to the nearest open cells" (LOG 2178); group 3 says "3 moved" (LOG 3046). Both groups end with
     every plot on the city street.
   - So the refresh does not just fail to fix "wrong side": **it creates it.** **[P]**

Fix:
- F4: plots face the access lane or the lot, never an outer street, unless the user picks "street front".
- F5: `NearestOpenRun` may only use the grid of the plot's planned road; otherwise drop the plot and report it.
- 08 §7: switch off the zoning of every road side that must not host shops (our lot side, and the city street sides along
  the build).

### (b) Farmland crop texture on the strips — preset/asset bug (planner contract × automatic assets)

Which surface or palette rule picks `surface.crop`? **None.**
- No commercial or parking code plans a crop surface. PROBE lists every planned surface of every commercial type: only
  `surface.asphalt` (explicit G87 asphalt), `surface.grass` and `surface.paving`.
- The only crop rule in the code base is `GroundCover` (farm types).

The texture comes from an **area**, not a surface **[P for the chain]**:
1. `PlanBuilder.Emit` adds the lot "owner area": `new PlannedArea { Ring = best, Role = AreaRole.Lot, Asset = new
   AssetRef("") }` (`Core/Layers/Parking/PlanBuilder.cs:448`). This is a leftover of the ParkingLotTool port, from before
   Placemaker made its own build-level owner lot "Placemaker Area".
2. `Mall.Merge` copies it into the commercial plan (`Mall.cs:600`), once per lot. PROBE: Strip mall has 4 such areas,
   Farmers' market 6, Outdoor mall village 4.
3. `RoleCategories.ForArea(AreaRole.Lot)` = `area.lot` (`PlanWork.cs:96`). `AutoAssets.Resolve` fills the empty asset
   from the automatic pool (`AutoAssets.cs:163`). `area.lot` is a "uniform" category, so it takes the pool's first item
   (`:139`).
4. The game's `area.lot` pool is "Agriculture Lot, Agriculture Lot - Field, Extractor Lot, Forestry Lot…" (LOG 1794, the pool listing before group 1;
   `catalog-dump.json` line 234). PROBE resolves every lot to **"Agriculture Lot"**. The blocking-cause classifier finds
   **"lot Agriculture Lot - Field"** next to the plots of groups 2 and 3 (LOG 2971, 3044).
   - It does not say "our lot", so the lot carries no `PlacemakerMember` tag (`BuildService.Terrain.cs:785–786`).
   - It is `PhysicalGeometry`, because the classifier only reports physical areas (`:777`).
5. The obstacle scan sees the same thing. Before group 1: `lots=0`. After group 1: `lots=3 (3487 m2)` (LOG 1788 vs 2389).
   These are lot areas outside any group (`BlockObstacles.cs:291`).
6. PLAYER 243, 565, 571–626: the "AutoIndustryArea" mod processes our new lots as industry areas ("could not identify the
   building's original opening; area preserved") right after the builds.
7. The code already knew vanilla lots were the wrong choice: "All vanilla lots are industry/extractor lots with their own
   economy, so we register our own" (`OwnerAreaPrefabSystem.cs:10–11`). The parking sub-plan's lot area bypasses that rule.

On screen, the crop shows where the lot's own surfaces do not cover the field. That is the strip lots and the lot edges.
- On the main lot, the asphalt (the overlay clone, priority −94, `BuildService.Emit.cs:405`) and the grass islands draw
  over it.
- Why the strips are left uncovered is **[H]**: either surface rings the game rejects (LOG 1977, "skipped a surface ring
  the game would reject"), or the planner's surface cells not covering the strip.

[H] Which step turns the planned "Agriculture Lot" into the live "- Field": the catalog name resolution or a game
placeholder swap. Either way it is a vanilla farm lot. Check: log the resolved prefab name of every area work item.

Scope: every type that runs the Parking planner — Parking itself (the 11:51 standalone lots raised `lots=0 → 1 → 2`,
LOG-1152 3250, 3273), every commercial type, Rest area (`TransportKit.cs:114`), Sports park…

Fix:
- F1: the parking sub-plan emits **no** lot area when it is hosted. The build's owner lot already covers selection.
- F2: remove `area.lot` from automatic picking entirely (`AssetHeuristics.cs:99` tags it; make it explicit-only).
- F3: a plan verdict, "no vanilla industry lot, no crop surface in a non-farm build". 10-ingame-suite.md already lists
  `noCropSurfaces`.

### (c) Fences enclosing the lot and the strips — game-layer side effect of (b), not a planner edge rule

Which edge rule emits fences on access sides? **None in the planner.** **[P]**
- There is no fence net or fence piece anywhere in `Core/Layers/Commercial/*` or `Core/Layers/Parking/*`. The only match is
  `NetFit.cs:39`, which *filters out* fences.
- The leisure boundary fence (`LeisureLayer.Fence`, `LeisureLayer.cs:237–240`; the only commercial fence settings switch fences *off*, `MallParkLayer.cs:141`, `OutdoorMallLayer.cs:276`) is never called by a commercial type.
- The park pocket preset that the kit uses for leftovers leaves `fence` off (`ParkLayer.cs:154`, `:202`).
- PROBE: **0 fence nets** in all eleven commercial plans.

What the game does **[P for the mechanism; H that this prefab uses it]**:
- An area prefab can carry border sub-objects. `AreaSubObjectInfo.m_BorderPlacement` becomes `SubObjectFlags.EdgePlacement`
  (DECOMP `AreaInitializeSystem.cs:385–405`, `AreaSubObjectInfo.cs:10`), and the game places them along the area's edge by
  itself.
- The fences in shot-106 match that exactly:
  - wooden rail fences, the vanilla farm look;
  - they run along *every* lot boundary: the main lot's edge on both the street and the lane side, and both sides of each
    strip lot;
  - they have no gaps at our entrances, because the game knows nothing about them.

Check: dump `AreaSubObjects` of "Agriculture Lot - Field" from the prefab system, or build one lot with F1 applied and see
the fence disappear.

Fix: F1/F2 remove them. As a rule (§6, R7), no fence ever on a lot edge that carries an entrance or faces the shop fronts.

### (d) Parking spaces in strips with no lane access — planner design flaw (lot first), made worse by (c)

Proof: PROBE. **[P]**
- **Strip mall 160×160:** two strip lots of 747 m², 12 m wide, 6 stalls each, with 2 street connectors each. Each sits
  between our lane and the city street.
- **Strip mall 160×110:** three strips of 445–833 m² with 4–5 stalls.
- **Farmers' market:** four strips of 466–1220 m² with 4–8 stalls.

Cause:
- `LotFirst` → `RestLots(…, 400f, …, openAccess: true)` (`CommercialLayer.cs:172–173`).
- `FillRest` → `RestLots(…, 220f, …)` (`:318`).
- These plan a lot on **every** leftover piece a street or lane can reach.
- `PolyLot` (`MallLots.cs:144–180`) accepts any piece that has a reachable outline run (`Entries`, `:26–78`: any boundary
  sample within 6.5 m of a kerb or 3.5 m of a lane) and holds at least 4 stalls with 30 % fill.
- A 12 m strip qualifies: one row of bays plus an aisle.
- The planner does give those strips connectors to the city street, but a parking strip squeezed between a lane and a street
  is not a car park anyone would design.
- In game, the farm fence (c) runs around each strip with no gap, so it looks, and acts, fenced off.
- Also: `CommitLot` silently removes the parking planner's "NoEntrance" warning (`Mall.cs:350`). A hosted lot without a car
  entrance is accepted without a word.

Fix: R4/R5. A lot needs a real entrance (a connector the game accepts, 03 §6) and at least one full double-loaded bay
(≥ 18.8 m deep, `Mall.LotDepth(1)`). Leftovers below that are planted buffer. Never drop "NoEntrance".

### (e) An empty asphalt lot with no stall markings (12:13) — planner (site-specific); cause not yet isolated

Proof:
- Group 3 planned **6 stall markings for the whole build**, and all 6 were built (LOG 3042). The audit places them as two
  pairs and two singles spread across the site (group-3 json: `ParkingLotDecal01` at (1100,-1295)/(1099,-1293),
  (1141,-1360)/(1139,-1358), (1090,-1391), (1048,-1325)).
- So the asphalt is a lot surface the planner paved while placing almost no bays. **[P]**
- Group 2 is the same: 7 stalls (LOG 2969).
- PROBE plans the same types on a clean block with **53–124 stalls**: High street 53 flat / 124 on a slope; Farmers' market
  68 / 75. **[P]**
- So the loss comes from inputs the real site has and the clean block does not.

Candidates **[H]**:
- reserved space from the site's existing content blocking stall positions (`rq.Blocked`, `PlanBuilder.cs:107–109`);
- the side lot rectangle being cut down by the paseo hold and the shop rows (`HighStreetLayer.cs:130–145`);
- `LotGrading` dropping cross lanes over terrace edges on the slope ("the gap stays paved", `LotGrading.cs:216`, `:364`).

Check: replay the captured site of group 3 in the 06 bench and print the parking planner's stall rejections.

Why such a lot is still built **[P]**:
- `Mall.Lot → PlanLot` accepts a lot with one stall (`if (cap <= 0) return null;`, `Mall.cs:334`).
- Only `PolyLot` asks for at least 4 stalls and 30 % fill (`MallLots.cs:173–177`).
- The side lots of High street and Outdoor mall (`HighStreetLayer.cs:138`, `:141`; `OutdoorMallLayer.cs:105`) and the
  composition lots of every type have no minimum.

Fix: R4. One fill and capacity rule for every lot (at least 30 % of the area in stalls, a minimum stall count). Otherwise
the land is landscaping, and the plan says why.

In the pre-anarchy session (LOG-1152 2466–2470) stall markings were also *rejected* by the game (`OverlapExisting`), which
produces empty lots too. In this session, s59 anarchy kept them: 91/19/12 kept despite collisions (LOG 1985, 2966, 3039).
**[P]**

### (f) The terrain pit beside Brookhaven Lane — game-layer effect after Apply; the guard cannot see it

Proof **[P]**:
- Group 1's terraform "reached its target in 2 pass(es) … 16/1301 texels off, worst 0.49 m" (LOG 1982). That is our
  target, applied correctly.
- At the end of the build: "bowl 5.78 m deep at (1338, -1150); live ground 889.54 m, target there 895.63 m (the game's
  ground differs from the target)" (LOG 2193; PLAYER 612).
- The pit guard did not fire on the target: there is no "pit guard: raised" line. So the target had no bowl.
- The bowl appeared **after** our terraform: during Apply of the nets, objects and lots, or the later zone and zoning
  steps (`BuildService.cs:62–66` step order; the live check runs at Finish, `:602`).
- Position: about 20–30 m west of the accessible stalls (1358–1367, -1155..-1161 in the audit). In the image it is just
  across Brookhaven Lane from them, on the strip toward Mulberry Street. That is the pit in shot-106.

Why the guard "missed" it **[P]**:
- `GuardPits` raises bowls in **our terraform raster only** (`BuildService.Terrain.cs:144–154`).
- `VerifyLiveHollows` measures the live ground afterwards and **only logs** (`:160–176`). There is no re-terraform, no
  repair and no user warning in the UI.
- So the guard holds for Placemaker's target on every type, and nothing guards ground the game moves.

What dug it **[H]**: CS2 nets conform the ground under them to their own heights. A net whose node is pinned well below the
graded site would cut a hollow. Two candidates:
- a strip lot's kerb connector, whose "dead-end connector ends take the street's own height" (s58, `PlanBuilder.cs:298–321`);
- Brookhaven Lane's end, pinned to the city street (`MallGrading.cs:12–13`, approach 14 m), where the city street (group 0,
  built a minute earlier) sits about 6 m lower.

Check: log the planned node heights of every net within 15 m of a reported bowl next to the live street heights, or replay
in the bench.

Fix (F9):
- After Apply, re-measure, and if a bowl exists where the target has none, run one corrective terraform pass on the bowl.
- Report it in the build result.
- Plan connectors and lane ends so that no node is below the graded ground by more than a kerb (R10).

### (g) Our ring and alley roads become named city streets — design consequence (lanes are public roads)

Proof **[P]**:
- Our lanes are the vanilla `Alley` (`Mall.AlleyAsset`, `Mall.cs:31`; `ShopLane`, `:39–48`) and
  `Pedestrian Street Small` (`MallKit.cs:43–55`).
- These are `RoadPrefab`s with an aggregate type (`NetGeometryPrefab.m_AggregateType`, DECOMP `NetGeometryPrefab.cs:22`).
- `Game.Net.AggregateSystem` groups the edges of such a road into an aggregate (`AggregateSystem.cs:642`), and the name
  system gives it a random localized street name.
- Hence "Summerfield Lane", "Brookhaven Lane", "Moore Street", "Birch Street".
- Our lot aisles are invisible paths with no aggregate, so they stay unnamed.

What this tells the user, correctly: our "mall lane" is a public through road, with zone blocks on both sides (lot side
included, which then needs blockers), and through traffic.

Fix (F10):
- Give the access lane one deliberate name (vanilla `NameSystem.SetCustomName`, DECOMP `NameSystem.cs:221`, e.g. "Summit
  Plaza Drive").
- Use `ZonesDisabled` on its non-shop side (08 §7).
- Where the lane is only an internal drive aisle, use an unnamed, non-zoning path.

### (h) Zoning: planned 36 → 22 / 12, "4 open, 18–44 blocked" — what blocks them

The "before refresh" classification of blocked live cells within 6 m of a planned cutout cell (LOG 2176, 2971, 3044):

| Blocker | Group 1 (51 cells) | Group 2 (165) | Group 3 (162) |
|---|---|---|---|
| Our own lane (`Alley` / `Pedestrian Street Small`), direct or at row 0 → column | 6 | **113** (23 + 84 + 6) | **114** (22 + 92) |
| Our lot aisles `Invisible Road/Car Path` at row 0 → column | **28** | 25 | 24 |
| Our farm-field lots "Agriculture Lot - Field" (b) | – | 15 | 18 |
| Game depth rule or a city net | 17 | 12 | 6 |
| Our Zone Block lots | 0 | 0 | 0 |

**[P counts]** The answer to "what blocks them" is **mostly our own build**, not the city:
- **Zone Block lots block nothing here.** They are emitted after the refresh (`BuildService.ZoneBlock.cs`), and the
  classifier names them first when they overlap (`BuildService.Terrain.cs:752–753`). They do make the loss permanent
  afterwards (04 V3).
- **Our lot aisles and our farm lots close the row-0 cells** of blocks the plots needed. `CleanBlockedCells` then kills
  the whole column (DECOMP `CellBlockJobs.cs:587–602`).
- **Our lanes.**
  - Caveat **[P]**: the classifier blames any net within `DefaultHalfWidth(role)` of the cell square
    (`BuildService.Terrain.cs:761`). For a `Street` that is 8 m (`Composition.cs:376`), but a Pedestrian Street Small is 8 m
    wide (half 4 m). So *every* row-0 cell of the lane's own block is blamed on the lane, whatever really closed it.
  - The real closer of those row-0 cells is **[H]**: the junction nodes where the High street's market doors and paseos join
    the lane every 36 m (`HighStreetLayer.cs:117–121`). Node geometry closes extra cells; s60 added `NodeReach` for this.
  - Check: classify against the real edge and node geometry instead of the role's default width.
- **The city street keeps the overlaps** (08 §3), so the cells that survive are the city street's (§3(a)).

**"4 open, 18–44 blocked"** is a window count, not a plot count **[P]**:
- `Diagnose` counts every cell of every block whose centre lies within the plot grown by 4 m (`ZoneGuard.cs:554–575`). With
  4–10 blocks overlapping there, 18–44 "blocked" is mostly crossing blocks under our own nets.
- The "4 open" is one 2×2 run, and neighbouring plots compete for it. Group 2's MISMATCH shows the pattern (LOG 2973):
  `#0 2->4, #1 2->0, #2 2->0, #3 2->0`, `#9 2->4, #10 2->0`, `#16 1->2, #17 1->0`. The first plot grows over the open run and
  its neighbours get nothing.
- Fine-grain 1-column plots (High street default) are the worst case. The game blocks a lone column (depth rule, 04 §3), and
  `NearestOpenRun` needs at least 2 columns × 2 rows (`ZoneGuard.cs:483–485`).
- Group 3 also dropped 4 plots as "cannot zone (land blocked)" (LOG 3046). Their land goes to the blockers
  (`ZoneGuard.cs:444–453`).

---

## 4. Offline probe (reproducible, no game)

`.forensics/scratch-07/probe/Program.cs` compiles `Core/**` and composes each type on a rectangle with 12 m city streets on
all sides (the same site as the tests' `ZoningCutoutTests.Streets`). Seed 5.

| Type / preset | Lanes | Lot areas (`AreaRole.Lot`, asset as planned → resolved) | Stalls | Strip lots ≤ 12–14 m wide | Fence nets | Crop surfaces | Quads on city street / our lane |
|---|---|---|---|---|---|---|---|
| Strip mall classic 160×160 | Alley ×17 | 4 × '' → "Agriculture Lot" | 235 | 2 (747 m², 6 stalls each) | 0 | 0 | 8 / 11 |
| Strip mall classic 160×110 | Alley ×17 | 4 × '' → "Agriculture Lot" | 132 | 3 (445–833 m², 4–5 stalls) | 0 | 0 | 7 / 5 |
| Shopping plaza court | Alley ×3 | 3 × '' → "Agriculture Lot" | 237 | 0 | 0 | 0 | 14 / 10 (+1 both) |
| High street (market) | Ped St Small ×4 | 4 × '' → "Agriculture Lot" | 53 | 2 (466 m²) | 0 | 0 | 13 / 30 (+2 both) |
| High street farmers | Ped St Small ×6, Alley ×2 | 6 × '' → "Agriculture Lot" | 68 | 4 (466–1220 m²) | 0 | 0 | 13 / 16 |
| Outdoor mall village | Ped St Small ×1 | 4 × '' → "Agriculture Lot" | 246 | 0 | 0 | 0 | 14 / 10 (+3 both) |
| Mall district mixed use | Alley ×6, Ped ×5 | 0 | 0 | – | 0 | 0 | 8 / 20 |

On a 6 %/3 % slope the numbers barely change: High street 124 stalls, Farmers' market 75, Strip mall 232.

---

## 5. Judgement: what a user expects, and how far today's output is

**What "strip mall / shops" means to a player** (real-world strip centre, and how CS2 players build one by hand):
- **One** row of shop units (a single long building, or 3–8 small growables back to back) set back from the street.
- Its **fronts face a shared parking lot** that lies between the shops and the street, with a sidewalk or arcade along the
  fronts.
- **One or two driveways** from the street into the lot; a drive aisle runs parallel to the shop fronts.
- Optionally a service lane behind the shops, on their back side.
- Landscaping on the lot's edges and islands; a sign pylon at the driveway.
- No fences between the lot and its street, its shops or its driveway. No farm textures. Nothing left over.
- The **shopping-alley variant**: a short pedestrian lane with shops on **both** sides facing each other, open at its ends,
  with parking at one end or behind.

**What today's planner gives instead** (shot-106 and shot-107, the 12:13 screenshot, PROBE):

| Expected | Today |
|---|---|
| Shops are the point | Shops are empty zoned cells; most get no cells (36 → 22 / 12), the rest wait for demand (04) |
| Shop fronts face the lot / alley | Fronts face the outer street. By design about half the plots are street plots (§3(a)1). The game gives the lane plots to the street. The s60 refresh moves the rest there too |
| One lot with a driveway | Several lots: the main one plus a lot in every leftover strip, each inside a vanilla farm field with a fence and no gap |
| A private drive aisle | A named public road loop (the U lane) with through traffic, zone blocks on both sides and city signage |
| Landscaped edges | Crop rows (the field) and lawn "buffers" where plots were lost; pits where the ground was changed after our terraform |
| Preview = result | Preview shows coloured cells and our lane's shops; the result shows other roads' buildings, fences and fields that never appeared in the plan |

**Distance:** the geometric skeleton (street → lot → lane → row) exists only in Strip mall classic and Shopping plaza court.
Everything after the composition works against it:
- lot-first fills the leftovers with parking;
- the lot areas turn into farms;
- the zoning hands the shops to the wrong road;
- the refresh moves the survivors to the wrong road too.

The user's sentence "I can't even tell what this is trying to be" is accurate. No single rule produces "a row of shops
facing a lot". Several independent rules each add their own pieces.

## 6. Planner rules a correct design needs

These apply to every commercial type, and to every type that hosts shops or the Parking planner (the fix-scope rule).

- **R1 One composition per site, chosen up front.**
  - Strip centre: [street] → [driveway(s)] → [lot with aisles parallel to the shop fronts] → [sidewalk] → [shop row]
    → [optional service lane].
  - Shopping alley: [street] → [pedestrian lane, shops on both sides] → [lot at the end or behind].
  - The rest of the site is landscaping, never a second composition by default.
- **R2 Shops face the lot or the alley.**
  - A shop plot is valid only if the road it is meant to face is the **only** road whose zone block can reach it. Either it
    is more than 48 m from every other road, or every other road side along it is switched off (`ZonesDisabled`, 08 §4.1).
  - "Street front" is a user choice, never the default.
- **R3 The zoning refresh never changes a plot's facing road** (F5). A plot that loses its cells is reported as lost (04
  fix 2), not moved.
- **R4 A lot exists only if it is a real lot.** That means:
  - a car entrance the game accepts (03 §6);
  - at least one full double-loaded bay deep (≥ 18.8 m);
  - at least N stalls and 30 % of its area in stalls;
  - and the same rule for composition lots, side lots and leftover lots.
  - Never drop the "NoEntrance" warning.
- **R5 Leftovers become planted buffer** (lawn, trees, hedge), never parking and never a park unless asked.
- **R6 No vanilla lot areas.** A hosted lot emits no owner area; `area.lot` is never automatic (F1, F2). No vanilla industry
  or extractor area in any non-industry build.
- **R7 No fences on access edges.** No fence or hedge on a lot edge that carries an entrance, faces the shop fronts or faces
  the driveway. Boundary fences only on service edges, and only when asked.
- **R8 Theme hygiene.** No crop surface, farm prop or farm lot in a commercial or parking build unless the user picks it (a
  plan verdict, F3).
- **R9 Lanes are named on purpose or not at all.** An access lane gets one custom name for the site, and its non-shop side
  gets no zoning. Internal drives use unnamed paths.
- **R10 Ground follows the plan.** Every net node sits within a kerb of the graded ground. After Apply, a live bowl where
  the target has none gets a corrective pass and a visible warning (F9).
- **R11 The preview shows the result.** Either shops are placed as buildings (04 §6 option C), or the preview labels plots as
  "land the city may build on" and shows which road they will face, with the farm lot, fences and parking strips visible if
  they would be built.
- **R12 Every build logs its type, preset and composition template** (F12), so a session log identifies what was built.

## 7. Fix list

| # | Fix | Class | Where | Scope |
|---|---|---|---|---|
| F1 | Hosted Parking sub-plans emit no `AreaRole.Lot` (or `Mall.Merge` drops it); the standalone Parking type uses the build's owner lot only | asset contract (planner) | `PlanBuilder.cs:442–450`, `Mall.cs:600`, `TransportKit.cs:114` | every Parking-planner host |
| F2 | `area.lot` never automatic; an empty Lot asset resolves to nothing, or to the Placemaker owner prefab | palette / asset resolution | `AssetHeuristics.cs:99`, `AutoAssets.cs:163`, catalog pool | all types |
| F3 | Verdict + test: no vanilla industry lot, no `surface.crop*`, no fence on an entrance edge in commercial/parking plans | test | Tests + 10-ingame-suite verdicts | all commercial and parking presets |
| F4 | Shop plots face the lot or alley only; street rows opt-in; `StreetRows` off in lot-first by default | design (planner) | `CommercialLayer.cs:169, 201`; `HighStreetLayer.cs:38`; `MallKit.cs:358` | all commercial types |
| F5 | `NearestOpenRun` limited to the plot's planned road grid; otherwise report as lost | planner bug | `ZoneGuard.cs:430–441, 486–522` | every cutout host |
| F6 | ZoneGuard ranking follows the game (older visible block wins) so the preview never promises lane cells the game gives away | planner bug | `ZoneGuard.cs:666–668` | every cutout host |
| F7 | Switch off zoning on road sides that must not host shops (our lot side; city sides along the build) | game layer | 08 §7 | every cutout host |
| F8 | One lot rule (entrance accepted, ≥ 1 full bay, minimum stalls and fill); leftovers become buffer; keep "NoEntrance" | design (planner) | `Mall.cs:334, 350`; `MallLots.cs:144–180`; `CommercialLayer.cs:172–173, 318` | all lot hosts |
| F9 | After Apply: live bowl where the target has none → corrective terraform pass + UI warning; plan connector and lane-end heights within a kerb of the graded ground | game layer + planner | `BuildService.Terrain.cs:160–176`; `PlanBuilder.cs:298–321`; `MallGrading.cs` | every terraforming type |
| F10 | Access lanes get one custom name, or are unnamed internal drives | game layer | build step after Apply, `NameSystem.SetCustomName` | every type that builds roads |
| F11 | Cause classifier uses the real edge and node geometry, not `DefaultHalfWidth(role)` | diagnostics | `BuildService.Terrain.cs:747–788` | all |
| F12 | Log and audit record type, preset and template; per-area resolved prefab names | diagnostics | build start, audit JSON | all |

Priority:
1. F1 + F2, then F3. These remove the farmland, the fences and most "no way in" at once. Low risk, one contract change.
2. F5 + F7, then F4. These are the "shops on the wrong side" fixes.
3. F8. Strips and empty lots.
4. F9 and F10.
5. F6, F11 and F12 make the next session self-explaining.

## 8. Open points (not proven here)

- The exact prefab step that turns the planned "Agriculture Lot" into the live "Agriculture Lot - Field". The field's border
  fence object. (Dump the area prefab's `AreaSubObjects`; log resolved area prefabs.)
- Why group 3 (and group 2) planned only 6 (7) stalls when the clean-block probe plans 53–124. (Replay the captured site in
  the 06 bench.)
- Which net dug the pit at (1338, -1150). (Log net node heights near reported bowls.)
- Whether the farm lots also cause the `OverlapExisting` flags on stall markings and bushes (91 / 19 / 12, LOG 1985, 2966,
  3039). They overlap physically, but the lots are created in the same Apply. (Build one lot with F1 and compare the
  validation lines.)

---

## Skeptic review

This review checks the report's key claims against the code (master 650be0f; `git diff 650be0f d9f6df6` touches only
Roads/UI files, so the cited code is unchanged), the session log, the audits, both screenshots and DECOMP.
I re-ran the existing probe binary (`dotnet .forensics/scratch-07/probe/bin/Release/net9.0/Probe.dll`, read-only).
Its output matches §4 line for line.

Verdicts: **CONFIRMED** = holds against the evidence. **WRONG** = contradicted. **UNPROVEN** = plausible, but the cited
evidence does not prove it.

| # | Claim | Verdict | Evidence / correction |
|---|---|---|---|
| 1 | Hosted parking lots emit an empty `AreaRole.Lot` area, which resolves to "Agriculture Lot" | **CONFIRMED** | `PlanBuilder.cs:448` → `Mall.cs:600` → `PlanWork.cs:96` → `AutoAssets.cs:163`. `area.lot` is uniform, so the first pool item is taken (`:139`). Areas are not palette categories (`AutoAssets.cs:42–47`), so no palette can override this. The probe re-run resolves every type's lots to "Agriculture Lot". |
| 2 | The live "Agriculture Lot - Field" **is** that area, so every parking lot sits inside a farm field (stated as [P]) | **UNPROVEN** | Evidence against: the classifier prints "lot …", not "our lot …" (`BuildService.Terrain.cs:785–786`), so the entity has no `PlacemakerMember` tag. The obstacle scan also counts these as ungrouped `Lot`s (`BlockObstacles.cs:291`, `GroupOf(e) == 0`). The prefab name also differs from the resolved one. The link is a correlation: `lots=0` → `lots=3 (3487 m2)` (LOG 1788 / 2389, in different boxes) and the Field sits next to groups 2/3 (LOG 2971, 3044). **Missed consequence:** if these lots really are untagged, bulldozing the build leaves the fields behind. Check: bulldoze a test build, then look for orphan lots. |
| 3 | PLAYER 571–626 shows AutoIndustryArea "treating it as an industry area" | **UNPROVEN** | The lines only say "could not identify the building's original opening; area preserved". They show that the mod reacted to an area change, nothing more. |
| 4 | `catalog-dump.json` line 234 lists the pool | **WRONG citation** | The only dump (`.forensics/scratch-switchhang/dump/catalog-dump.json`, written 19:19Z) lists `area.lot` at lines 21 onward. LOG 1794 is the right source and is enough. |
| 5 | The planner makes no fence; the fence is the field's border object | **CONFIRMED** (no planner fence) / **UNPROVEN** (field border) | Probe: 0 fence nets. The audits contain no fence. The automatic `fence` pool starts with `EU_FenceResidentialHigh01` (session catalog listing), which looks nothing like the wooden rail in shot-106. Only `ParkLayer` "victorian" / `Chance("fence",0.2)` can make fences, and commercial leftovers use "pocket". The border-object mechanism is still H. |
| 6 | "Roughly half the shops face outward by design" | **OVERSTATED** | Probe shares of city-only quads: Strip mall 8/19 (42 %), **High street market 13/45 (29 %)**, Farmers 13/29, Village 14/27, Plaza 14/25. Group 2 was High street, and in game 15 of its 19 quads went to the city. So for group 2 the game's overlap rule, not the design, did most of the damage. |
| 7 | The s60 refresh moves lost plots onto any road, including the city street | **CONFIRMED** (mechanism) | `ZoneGuard.cs:434–441` and `:486–522` search every `Usable` grid within 3 cells and never check the planned road. |
| 8 | "The refresh … creates it" / "23 of 27 plots zone from the city" | **OVERSTATED** / wording | Moves: 2 + 1 + 3 = **6** (LOG 2178, 2973, 3046), against **23** city quads. In groups 1 and 3, the plot that was *not* moved also zones from the city (2179–2182, 3047–3050). The "plot N" lines index **zoning quads** (group 1: 3 plots, 4 quads). Correct wording: "23 of 27 zoning quads". Of those, at most 6 come from relocation, so F5 alone fixes little; F7 (switching zoning off per road side) is the main lever. |
| 9 | ZoneGuard gives ties to our lanes; the game gives them to the existing visible block | **CONFIRMED** | `ZoneGuard.cs:667–669`. DECOMP `CellOverlapJobs.cs:562–566`: a non-updating cell wins if it is `Visible`. |
| 10 | Strip lots: 12 m wide, 4–8 stalls | **CONFIRMED** | Probe re-run: 747 m², half-width 6.0 m, 6 stalls, 2 connectors. `CommitLot` dropping "NoEntrance" is confirmed (`Mall.cs:350`). |
| 11 | `PolyLot` needs "at least 4 stalls **and 30 % fill**" (repeated in (e), F8 and R4) | **WRONG** | `GoodFill = 0.3` only weights the variant score (`MallLots.cs:117`, `:174`). Acceptance is `Capacity >= 4` and nothing else (`:177`). No lot in the planner has a fill floor today. |
| 12 | Group 3 planned only 6 stalls; `Mall.Lot` accepts a lot with 1 stall | **CONFIRMED** | `StallMarkings` counts one `ParkingBay` per stall (`PlanWork.cs:186`, `PlanBuilder.cs:563–579`), and LOG 3042 shows 6 of 6. The group-3 audit lists exactly those 6 because all 6 collided (LOG 3039). The `cap <= 0` rule is at `Mall.cs:334`. |
| 13 | The 12:13 asphalt is a parking-lot surface paved with almost no bays | **UNPROVEN** | The screenshot is not on disk. The surface could also be the market strip's or a plaza's paving. |
| 14 | The pit appeared after our terraform | **CONFIRMED** (stronger than argued) | `TrySample` returned a target at the bowl (895.63 m), which needs texel weight > 0. A 6 m gap at the end of the terraform could not coexist with "worst 0.49 m inside, rms 0.07 m" (LOG 1982, `TerraformRaster.cs:226–250`). The guard only edits the target and the live check only logs (`BuildService.Terrain.cs:144–176`). |
| 15 | What dug it: a net node pinned to the lower street | **UNPROVEN**, with a competing hypothesis | The group-1 audit has planned content *at* the bowl, at the planned height: a G87 stain decal (1341, −1147, y 895.6), a leaf decal (1337, −1144), and EV stalls and a tree at (1355–1357, −1138..−1143). So the bowl lies inside a planned lot or strip, not only beside a lane end. Competing cause: a vanilla `LotPrefab` with a `TerrainArea` component, which offsets the terrain with noise (DECOMP `Game.Prefabs/TerrainArea.cs`). Check whether "Agriculture Lot" or "- Field" has it. |
| 16 | Our lanes get city names via aggregates | **CONFIRMED** | DECOMP `AggregateSystem.cs:639–642` (`m_AggregateType`), and the labels on our ring in shot-106. |
| 17 | Blocker counts 51 / 165 / 162 | **CONFIRMED** (sums) / **one correction** | 6+28+17 = 51; 113+25+15+12 = 165; 114+24+18+6 = 162. "Lot aisles 15 %" holds for groups 2/3 only: in group 1, aisles are **28/51 = 55 %** and lanes 6/51. The classifier tries nets before areas (`BuildService.Terrain.cs:751–787`), so the Field share is a lower bound. The over-attribution caveat holds (`Composition.cs:376`: Street half-width 8 m). |
| 18 | Group 3 = Farmers' market or Outdoor mall Village | **UNPROVEN, incomplete** | Other Mixed presets exist: MallPark "garden" (`MallParkLayer.cs:42`), ShoppingPlaza "green" (`ShoppingPlazaLayer.cs:40`), ShopsPark "row" (`ShopsParkLayer.cs:56`). The user could also have changed `zoneKind` by hand. Only F12 logging can settle it. |

**Net effect on the fix list:**
- F1/F2 stay first. They remove the only code path that places a vanilla lot, whatever turns it into "- Field".
- F3 must also run **in game** ("no ungrouped Lot appears after a build"), because the offline plan never contains "- Field".
- F5 covers at most 6 of the 23 wrong-side quads. F7 (zoning off per road side) and F4 do the real work.
- F8/R4 must *add* the fill floor that the report assumed already exists.
- F9 should first identify the digger (net vs `TerrainArea`) before planning connector heights.
