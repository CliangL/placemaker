# Playtest 1 (2026-10-13) — user feedback, diagnosis, user stories

## What the user said (paraphrased, all must be addressed)

1. Panel should be **movable**; better on the **right side, tall**, wider/less cramped; **less transparent** (buttons hard to read).
2. **Missing ParkingLotTool features**: entrance types, footpaths, alleyways, customization, "placing buildings" (PLT zoning on/around the lot so buildings grow). Did we lose all that? → Must reach PLT parity.
3. **Construction cost shows $0.**
4. **Built result looks broken.** Bulldozing on top "made it look a lot better".
5. Second build **failed**, then **red dots** stayed on the map while starting again.
6. **Parking auto-places entrances** — don't. Entrances shouldn't be a weird control; just **block Build until ≥1 entrance** is placed. Where are **alleyways** etc.?
7. The **Make** section looks weird.
8. **X / close button missing** (asset picker / pavement picker can't be closed).
9. Remove **"Save as my recipe"**. Remove **"Add to this design"** (layer tree editor) from the UI.
10. **More options always expanded** — no collapsing.
11. **Square icon buttons have no icons.**
12. **Not bulldozable as one thing** — it places a bunch of loose assets. PLT made the lot ONE selectable/bulldozable thing. Must do that.
13. UI needs a much cleaner, more usable pass. Think user stories.

## Diagnosis from logs (Player.log / Logs/Placemaker.log)

- Builds 1+2 failed with the game's own validation error `InvalidShape` (an area/surface ring CS2 rejects). BuildService failed the whole build and left ErrorPositions drawn → red markers persisted. Build 3 succeeded (476 members).
- `coui://gameui/Media/Game/Icons/ParkingFacility.svg` → "Invalid host locations map" (wrong host casing/path; vanilla icons are `Media/...` relative or `coui://GameUI/Media/...`).
- Our UI icons are emitted as `coui://ui-mods/images/<generic>.svg` (area.svg, check.svg…). `ui-mods` is shared by ALL mods → name collisions. Namespace them (`images/placemaker/…` or `pm-*.svg`) and verify the rendering technique works in Gameface.
- Cost $0: plans mostly use automatic assets (empty `AssetRef.Id`); `Pricing.Of` only prices explicit ids → 0. Fix: resolve automatic assets to concrete prefabs **before** preview/pricing/build (one resolver, deterministic by Variation) so preview = price = build.
- "Looks broken": likely existing trees/props inside the area untouched, surfaces z-fighting, lingering overlay. Verify; add "clear existing trees/props in area" (default on) like PLT's tree-clearing surface variant; overlay must stop drawing plan preview after build.

## User stories (acceptance criteria)

- **US1 Pick & place:** I open Placemaker, the panel appears docked on the right, readable (≥ 90% opacity), tall, and I can drag it anywhere by its header (position remembered; double-click header resets).
- **US2 Choose what to make:** I see clear cards "Parking lot · Plaza · Park · Roads · Campground · Mixes (Roads + parks, Park | Plaza…)", each with an icon and one line of description. No tree editors, no recipe saving.
- **US3 Parking entrances:** After drawing a lot, the panel says "Place at least one entrance" and Build is disabled until I do. I choose an entrance type — Two-way, Entry only, Exit only, Alley (junction), Footpath (pedestrian) — then click on the outline next to a road. Each placed entrance shows on the map with its type icon; right-click removes. Suggested spots may be shown as faint hints, never placed automatically.
- **US4 Parking customization parity:** everything PLT offered that makes sense — stall angle, sizes, aisle width, perimeter road, cross lanes, medians/green, lanterns, EV/accessible, surfaces, vegetation, fee, **zoning edges / zoning patches inside the lot so buildings can grow**, footpaths, alleys.
- **US5 Honest price:** Review shows a real construction cost and monthly upkeep matching what the game charges.
- **US6 Clean result:** What gets built matches the preview; no leftover vanilla trees poking through; surfaces render cleanly.
- **US7 One thing:** Clicking any part of a built lot/park/plaza selects the whole thing (one info panel). Bulldozing it removes everything at once (with confirmation). Edit reopens it in Placemaker.
- **US8 Failure is recoverable:** If the game rejects part of a build, Placemaker fixes or skips that piece and tells me what was skipped; if it must fail, the map shows where and why, and all red markers vanish as soon as I change anything.
- **US9 Always visible options:** every option of the chosen type is visible (no "More options" collapse), grouped under clear headings; scrolling within the tall panel.
- **US10 Every button has an icon** (or is a text button by design); every picker has a close (X) button and closes on Esc / click outside.
