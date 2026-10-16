# Beach (category agent, branch `rf-beach`)

Beach is its own category now (out of Parks): the `water` group, the **beach** and **waterfront** types. Before this
work no beach or waterfront had ever been built in the game runner on real water: the runner's test land had none.

## 1. Testing on real shorelines (shared runner code, kept minimal)

The game runner removes every water source from the map, so a shore type could only be built on dry land. I added one
land option, `water`, to land made to order (`Show.ps1 -Land file.json`, `suite/schema.json` "land"):

```json
{"size": [180, 80], "water": {"side": "north", "width": 90}}
{"size": [180, 90], "ground": "slope", "grade": 6, "toward": "south", "water": {"side": "north"}}
{"size": [160, 90], "water": {"side": "north", "freeboard": 7, "bank": 120}}      // a cliff
{"size": [160, 110], "water": {"side": "north", "shore": 20}}                      // the area drawn 20 m out over the water
```

A lake lies on that side of the land: the land meets a bank (`bank` grade, default 12 %) down to the water `freeboard`
metres below it (default 2 m), the lake floor falls at 10 % to `depth` (default 4 m) and comes up again `width` metres
out. The ring road leaves the water side out (its legs stop on the land), and entrances are only put on sides with a
street. The runner adds constant-level water sources the way Placemaker's own lakes do and runs the simulation until
the water by the shore is as deep as the lake bed asks (it fills in 2–3 s; `suite.log`: `water for P1: 5 source(s),
full after 3 s; depth/expected at the shore: 1.49/1.5 ...`).

Files: `Core/Suite/SuitePlot.cs` (lake geometry, ring road, block), `Game/Suite/SuiteLand.cs` (parsing),
`Game/Suite/SuiteFixture.Water.cs` (water sources, depth probe), `Game/Suite/SuiteRunner.Land.cs` + `SuiteRunner.cs`
(fill after the land is laid out), `Game/Suite/SuiteSteps.Input.cs` (entrances only on street sides),
`suite/schema.json`, test `SuitePlotTests.Lake_BesideTheLand_BankFloorAndNoRoadOnItsSide`.

## 2. Grades and decisions

BEFORE runs (master code, my runner): every type and style on a 180 x 80 m lakeshore,
`.forensics/suite/runs/show-13-builds-1016-063315-5e76` (11 builds; river steps and natural bank had no room left on
the map) and `show-2-builds-1016-073707-36a4` (river steps, natural bank). Shore variants (slope, area drawn over the
water, cliff): `beach-v1-before`.

| Type | Style (id) | Grade | Keep? | Why |
|---|---|---|---|---|
| beach | Family beach (`sandy`, also the default) | C+ | **keep** | Clean and works: sand painted into the water, umbrella groups, lifeguard posts, a promenade with landings, pines behind. Plain. |
| beach | Promenade beach (`resort`) | C | **keep** | Tidy umbrella rows and a wide promenade, but the lawn behind was a neon-green rectangle (the grass surface) with a few palms. |
| beach | Wild dunes (`cove`) | C | **keep** | The one beach without crowds: dunes, marram grass, fire rings. The dunes barely read and pines covered everything. |
| beach | Rocky cove (`rocky`) | D | hide | A strip of dirt along the water; the vanilla rocks are tiny, the tide pools invisible. Nothing to look at. |
| beach | River beach (`river`) | D | hide | A narrower family beach with one umbrella and the neon lawn. Nothing the family beach does not do. |
| beach | Sports beach (`volleyball`) | D | hide | The courts are 0.7-scaled tennis nets: thin lines on the sand, no court. Courts stay a control on the other beaches. |
| waterfront | Promenade boardwalk (`promenade`, also the default) | B- | **keep** | The best of the category: timber boardwalk with railings along the water, a paved promenade with benches and lamps, a park behind. Neon lawn. |
| waterfront | Pier park (`pier`) | F | **keep** (fixed) | Promised piers and built none: piers needed the area drawn out over the water. The rest was the promenade again. |
| waterfront | Harbour quay (`harbor`) | D+ | **keep** (fixed) | A wide quay and a big paved apron, but its pier was never built (the quay blocked it) and the lawn behind was bare neon. Distinct, so worth fixing. |
| waterfront | River steps (`riverSteps`) | C- | hide | The "steps" are flat paving patches on the bank; the yellow paving reads as sand. |
| waterfront | Natural bank (`fishing`) | D | hide | A gravel walk on a lawn; the fishing spots and riprap don't show. |

Shelf (`Recipes/shelf/beach.json`): hidden styles `beach: rocky, river, volleyball`, `waterfront: riverSteps,
fishing`. Both types stay; three styles each.

Cliffs (a 7 m cliff over the water): both types build a cliff-top walk with lookouts and leave the face alone, as
designed. The beach says so in Review. Fine as it is.

## 3. What changed (in the game runner, see the runs below)

* **Painted lawns, not the grass surface.** The beach's lawn backdrop and the waterfront's lawn and park behind the
  promenade were the vanilla grass surface: a flat neon rectangle with hard edges. They are lush grass painted into the
  terrain with a soft, wandering edge now (like the parks' default lawn). `WaterfrontLayer.Lawn`, used by both types.
* **Piers that get built.** A waterfront drawn along the shore (the usual way) now gets its piers: they reach out past
  the outline over the real water (the site's water snapshot reaches 60 m beyond it). Pier park: two T-headed piers.
  A pier off a wide quay looked for water under the quay's own deck and gave up; it looks past the quay now, so the
  harbour has its piers. Applies to every waterfront style with piers.
* **Jetties on the beach** (new controls `jetties`, `jettyLength`, `jettyHead`, appended): a timber jetty from the
  sand out over the water in line with an access path, rooted where the beach face meets the deck, with a T-head.
  Family beach: one 26 m swimming jetty. Promenade beach: one 44 m pier.
* **Promenade beach:** a palm avenue along the promenade's land side (lawn backdrops), denser umbrella rows.
* **Family beach:** busier (umbrellas 70 → 110 per ha), boats pulled up on the sand.
* **Wild dunes:** dunes 2 → 3.5 m high and deeper, thick marram grass, fewer pines. (No sand fences: vanilla has no
  timber sand fence; the pool picks brick walls.)
* **Harbour:** two T-piers, bollards along the quay, a park behind instead of a bare lawn.

## 4. Runs

* BEFORE: `show-13-builds-1016-063315-5e76`, `show-2-builds-1016-073707-36a4`, `beach-v1-before`.
* Iteration 1: `beach-it1` (8 builds: the kept styles and the two hidden waterfronts after the lawn and pier fixes).
* Commit `0ea2f0f` (harbour pier past the quay, harbour T-piers/bollards/park, resort palm grove) came after
  `beach-it1` and has **not** been run in the game. It builds (Release, `-p:SkipUI=true`).

## Done

Wrapped up early (the shared game queue was backed up; each request waited ~1 h). Branch `rf-beach`, not merged.

* Runner: land made to order can have a lake on one side (`land.water`), filled by water sources before the builds;
  entrances only on street sides (`a5aed62`).
* Beach and waterfront: painted lawns instead of the neon grass surface; piers built when the area is drawn along the
  shore (every pier style); beach jetties (new appended controls `jetties`, `jettyLength`, `jettyHead`); palm avenue and
  denser umbrellas on the promenade beach; busier family beach with boats; taller dunes and marram on wild dunes
  (`fa08501`); harbour piers off the wide quay, bollards, park behind (`0ea2f0f`, untested in game).
* Kept: beach `sandy` (default), `resort`, `cove`; waterfront `promenade` (default), `pier`, `harbor`. Hidden (shelf
  `8b9f954`, matches `Recipes/shelf/beach.json`): beach `rocky`, `river`, `volleyball`; waterfront `riverSteps`,
  `fishing`. Why: see the table in section 2 (nothing to look at, or a copy of a kept style).
* BEFORE: `show-13-builds-1016-063315-5e76`, `show-2-builds-1016-073707-36a4`, `beach-v1-before`.
  AFTER: `beach-it1` (at `fa08501`), e.g. `.forensics/suite/runs/beach-it1/shots/05-wf-pier.05-45-clean.png` (two
  T-piers over the water, painted lawn) and `.../beach-it1/shots/02-beach-resort.05-45-clean.png` (resort pier, palms,
  umbrella rows).

## Not done / known problems

* No in-game run of `0ea2f0f`: the harbour's piers (the fix for "pier never built") and the resort palm grove are
  unverified. First thing to run: `wf-harbor` and `beach-resort` on the lakeshore land.
* No AFTER grades: `beach-it1` was not graded against the table above.
* Shore variants (slope, area drawn over the water, cliff) not re-run after the changes; only `beach-v1-before` exists.
* Promenade beach: the lawn backdrop behind the palm avenue is still a large empty field in `beach-it1`.
* Wild dunes: no sand fences (vanilla has no timber fence asset).
* Hidden styles were not improved (rocky cove, river beach, sports beach, river steps, natural bank); they keep their
  code and still build by id.
