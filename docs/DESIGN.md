# Area Tool — UX design & merge plan (draft for review)

Working title only; name TBD. Status: **proposal, nothing built yet.** Date: 2026-10-13.

One tool for Cities: Skylines II: **choose an area, choose what it becomes** — Parking Lot, Plaza,
Park, Roads (later: more). It merges three mods:

| Source | Author | License | What we take |
|---|---|---|---|
| ParkingLotTool 1.0.10 | KruemelGames | GPL-3.0 | polygon editor + snapping, parking layout engine, CS2 surface-acceptance logic, filled previews, "one lot = one selectable/bulldozable thing", ~25 hard-won engine rules |
| ParkManager 0.7.4 | tomkpunkt | GPL-3.0 | park/plaza planners, asset catalog, "one main next-step button", remove-as-group, offline UI mock + Playwright |
| Grid Road Generator 0.4.1 | Ti4goc (you) | **no LICENSE file yet** — add MIT | 3 area-selection modes, block picking, brush, off-thread live sketch, unified undo, collision display, road patterns |

Consequence: the new mod is **GPL-3.0**, credits all three, keeps the MIT notice for CS2-NetworkTools code.

---

## 0. Decisions (2026-10-13, from the user)

- Ship the real thing, not a prototype: everything all three mods can do, in one new UI, rewritten robustly.
- **Stages you can always roll back**: Area → Design → Review (price, upkeep, stats, issues) → Build. Undo everywhere.
- **Composable, extensible to the extreme**: an area runs a *recipe* of layers — Fill (park, plaza, parking,
  roads, campground…), Split (park | plaza), Add-ons (bigger fountain, playground, restrooms, big trees,
  edge zoning…). Roads + parks, split plaza/park, park with zoning edges — all just recipes.
  Adding a new type = one class. Player-made recipes later = saved recipe files. See ARCHITECTURE.md.
- Working name **Placemaker** (rename later is fine). Panel docked above vanilla tool options.

## 1. UX principles

1. **Area first, then purpose — but never forced order.** The selected area survives switching type;
   the preview just regenerates. Players think "this block → park? no, plaza".
2. **One primary button, always telling you the next step.** (ParkManager's best idea.)
   `Select an area` → `Build park` → done. Disabled state explains *why* (tooltip).
3. **See it before you build it.** Live preview on the map that updates while dragging sliders
   (GRG's sketch worker), conflicts shown in vanilla red *before* pressing Build.
4. **Feels native.** Vanilla sliders, tool buttons, description tooltips, CSS variables, cursor hints
   relabelled like the vanilla area tool ("Add corner", "Complete area"). No 7-tab custom window.
5. **Progressive disclosure.** 3–5 controls per type visible; everything else in "More options".
   Sensible defaults + presets + 🎲 Randomize so most players never open it.
6. **Numbers that mean something.** "312 stalls · ¢48k · ¢1.2k/mo", "41 trees · 9 benches", not "Density 100 %".
7. **Every gesture is discoverable.** A shortcut strip in the panel footer + cursor tooltips.
8. **Built things stay manageable.** Click any built lot/park → vanilla info panel → *Edit* / *Remove*.
9. **No debug in front of players.** Diagnostics behind a dev setting; one "Report a problem" zip in Options.

## 2. The flow

```
 Ctrl+Shift+A (rebindable) or toolbar button
        │
        ▼
 ┌─ AREA ──────────────────────────┐   ┌─ MAKE ─────────────────────────────┐
 │ [✎ Draw] [▦ Pick block] [◍ Paint]│ → │ [🅿 Parking] [◫ Plaza] [🌳 Park] [⊞ Roads]│
 └─────────────────────────────────┘   └────────────────────────────────────┘
        │ area closed / picked / painted        │ preview appears immediately
        ▼                                       ▼
   adjust shape anytime            tune 3–5 essentials · preset · 🎲
        └──────────────► Summary: area · key numbers · cost · upkeep ──► [ Build park ] (Enter)
                                                                          │
                                                   built → tool stays open with area cleared
```

**Area modes** (all produce the same closed outline):
- **Draw** — click corners, click first point to close. Snaps to curbs, buildings, other areas, right angles,
  zone grid (PLT engine). Drag points/edges anytime, Ctrl+click edge = insert point, right-click = remove.
- **Pick block** — hover inside a city block, it highlights; click to take it (improves GRG's
  "double-click a node", which can't choose *which* block at an X junction). Shift+click adds blocks.
  Click road nodes to trace a custom perimeter (GRG behaviour) as the advanced path.
- **Paint** — round/square brush, right-click erases (GRG). Keep holes/islands as an option later.

**Per-type essentials** (rest under "More options"):

| Type | Essentials | More options |
|---|---|---|
| Parking | stall angle (90/60/45), entrances (auto-suggested on adjacent roads, click to add/remove), surface, greenery on/off | aisle width, perimeter road, medians, lanterns, EV chargers, fee |
| Plaza | style preset, centerpiece, surface, furniture density | arrangement editor, fence, access points |
| Park | style preset (vanilla park palettes), path style, planting density, furniture | lake (off by default, warns: permanent), fence, entrances |
| Roads | pattern (9), street type, block size | cul-de-sacs, avenues, upgrades, auto-zoning |

## 3. Panel layout

Docked **above the vanilla tool-options panel** (bottom centre, Platter-style), so it sits where players
already look for tool settings, never covers the work area, and inherits the game's transparency/scale.
Collapses to a single row while drawing.

```
┌──────────────────────────────────────────────────────────────────────────────┐
│ Area  [✎][▦][◍]   Make  [🅿 Parking][◫ Plaza][🌳 Park][⊞ Roads]    ↶ ↷  ⚙  ✕ │
├──────────────────────────────────────────────────────────────────────────────┤
│ Style  [Formal ▾]  🎲     Paths ○ narrow ● wide     Planting ━━━●━━  Furniture ━━●━ │
│ ▸ More options                                                                │
├──────────────────────────────────────────────────────────────────────────────┤
│ 1.8 ha · 41 trees · 9 benches · ¢24,300 · ¢310/mo          [   Build park   ] │
│ Click = add corner · Right-click = remove · Ctrl+Z undo · Enter build         │
└──────────────────────────────────────────────────────────────────────────────┘
```

Open questions to validate in the mock: bottom-docked vs. floating/draggable (GRG) vs. top bar (ParkManager).

## 4. Architecture

```
AreaToolSystem (one ToolBaseSystem, explicit state enum: Idle/Selecting/Editing/Previewing/Building)
 ├─ Selection: DrawEditor (PLT editor+snapping) | BlockPicker (GRG NetworkGraph) | Brush (GRG BrushMask)
 │      └──► AreaSelection { Ring, Source, AttachedNodes/Edges, Crossings }
 ├─ History (GRG design: one stack for shape + settings, gesture-coalesced; typed, not reflection)
 ├─ PreviewWorker (off-thread, latest-request-wins, height snapshot, stale-revision guard)
 ├─ Overlay (outline/handles/snap feedback) + FilledPreview (PLT instanced-mesh fills)
 └─ BuildService (definitions → materialize → validate/collisions → own → apply; one engine, not 3 copies)

IAreaGenerator  — Plan(AreaInput, Settings, seed) → AreaPlan { Surfaces, NetCourses, Objects, Zoning, Summary }
 ├─ ParkingGenerator  (PLT Geometry/Zellen, isolated)
 ├─ PlazaGenerator    (PM PlazaPlanner + VariantRoller)
 ├─ ParkGenerator     (PM path/decoration/lake planners + PLT vegetation)
 └─ RoadsGenerator    (GRG Core patterns + perimeter road + connect-to-existing)

Shared: SurfaceAcceptance (CS2 triangulation rules), AssetCatalog, Ownership (one group entity per build,
        raycast lift, cascade delete, versioned receipt for Edit), Localization (keyed JSON, late locales)

UI: schema-driven settings (each generator declares its controls → UI renders them) over ~10 structured
    bindings instead of PLT's 226 / GRG's 194 / PM's 61.
```

## 5. Explicitly dropped for v1

PLT background sync/migration machinery (main crash source), utilities auto-routing, bus stops, zoning
inside lots, parking economy statistics, ~20k lines of diagnostics, debug hotkeys; GRG dead native panel;
ParkManager's lake default-on. Some may return later, one at a time.

## 6. Build order

| Phase | Deliverable | Why this order |
|---|---|---|
| 0 | Toolchain installed; repo + licences; **browser UI mock** (from ParkManager's harness) running real planners | iterate UX in minutes, not game restarts |
| 1 | Shared core (selection ×3, history, preview, build, ownership) + **Plaza** | smallest generator proves the whole pipeline |
| 2 | **Park** | reuses Plaza path/decor pipeline |
| 3 | **Roads** | GRG Core plugs in; selection already done |
| 4 | **Parking** | largest, most fragile; lands on a proven core |
| 5 | Edit-after-build, presets sharing, functional park effects (leisure/wellbeing via companion building), Paradox page | |

## 7. Legal / licensing to-dos

- Add a LICENSE (MIT) to GridRoadGenerator — needs your co-author's OK.
- PLT's `Cs2Triangulierung.cs` is a port of decompiled game code — reimplement from documented behaviour
  (0.1 m inset, ear-clip, reject on failure) rather than copying.
- Credit KruemelGames, tomkpunkt, Ti4goc + friend, Luca Rager (MIT) in README and Paradox page.
- Consider reaching out to KruemelGames and tomkpunkt — they're active this month; collaboration beats competing.

## 8. Market note

Paradox's **Park Adventures** DLC (later 2026) adds official drawable parks. Plan for it: keep effects
behind an "effect provider" so we can emit the DLC's park areas when they exist; our edge is one tool
for four outcomes + parking + roads.

## 9. Places inside (round 3) — user request 2026-10-13, revised

Inner places are **described, not drawn** (user: "you wouldn't be drawing on the board again, you'd be describing
how the inner layers act, behave"). A recipe like "park with a round plaza in the centre" works on any area.

Each place inside is a rule:
- **What:** any layer (Plaza, Parking, Pond, Playground, Campground…), with its own options.
- **Shape:** circle · square · rectangle (aspect) · follows the outline (inset ring) · strip along an edge.
- **Where:** centre · a corner (nearest the best road / chosen corner) · along an edge · next to an entrance ·
  at the end of the main path · spread (several copies).
- **Size:** % of the area or fixed metres, with min/max; shrinks to fit, reported if it can't fit.
- **Connection:** how it joins the parent (paths to it, driveway through to the nearest road for parking,
  open edge vs fenced).

UX: in Design, under the type cards, a **"Places inside"** list: "+ Add" → pick a type card → a compact row
"[Plaza] [● circle] [centre] [25 %]" with its options below when selected. Preview updates live on the map;
nothing is drawn by hand. Shipped recipes use it ("Park with central plaza", "Park & ride" = park with parking
in the corner nearest the road, "Town square" = plaza with a ring of trees).

Engine:  = list of PlaceRule {Key, Layer step, Shape, Anchor, Size, Connection}. A pure
PlaceSolver turns rules into rings inside the parent region (respecting each other and the outline); the
composer reserves them for the parent (Reserved.Polygons + connection markers on their outlines) and runs each
place's step on its ring with tag "place:<key>". Parents route paths/drives to place entrances.
