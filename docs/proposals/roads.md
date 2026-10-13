# Roads layer (`roads`) and edge zoning (`edgeZoning`)

Owner: roads workstream. Code: `Core/Layers/Roads/*`, `Core/Layers/AddOns/EdgeZoningAddOn.cs`.
Generators are ported from Grid Road Generator (Ti4goc, the user's own mod) into
`Core/Layers/Roads/Generators/` with every static cache removed (thread-safe, deterministic).
Ported GRG tests live in `Tests/Placemaker.Tests/Roads*GeneratorTests.cs`.

## Pipeline (`RoadsPlanner.Plan`)

generate pattern → split at existing roads → remove pieces blocked by water/buildings/utilities →
perimeter road → drop pieces lying on existing roads → attach ends to existing roads → collapse
short edges → prune dead-end stubs (left by obstacle removal) and tiny fragments → heights →
`PlannedNet`s + cul-de-sac caps + roundabout island → `block:N` and `edge:N` regions.
`cancel` is checked between every phase; bad geometry returns `PlanIssue`s, never throws.

## Controls (all shown, grouped; headings = `Placemaker.UI.Group.<group>`)

| Group (heading) | Controls |
|---|---|
| `layout` (Layout) | `pattern` (grid, tree, organic, contour, mixed, loop, superblock, concentric, radial) and that pattern's sizes/angles/curviness/rings (`VisibleWhen "pattern=…"`); grid sizing by distance or count; align with terrain |
| `roadTypes` (Road types) | `streetAsset`, `localAsset`, `avenueAsset`, `roundaboutAsset`, `pathAsset` (superblocks). Empty = automatic |
| `connections` (Edges and connections) | `perimeterRoad` (auto/on/off), `perimeterCornerRadius`, `connectExisting`, `avoidObstacles`, `followTerrain` |
| `culDeSacs` (Cul-de-sacs) | grid cul-de-sacs (on, direction, frequency, depth, staggered), loop cul-de-sacs, turning-circle style and size |
| `avenues` (Avenues) | grid avenue column/row + which one, roundabout where they cross |
| `frontage` (Building frontage) | `edgeDepth` — depth of the `edge` strips |
| `streetUpgrades` (Street upgrades) | trees/grass/wide sidewalks/bike lanes for streets; median trees/grass, trees, verges, bike lanes for avenues |
| `zoning` (Zoning, edgeZoning add-on) | `zone` (zone asset), `depth` (8–48 m, whole 8 m cells) |

Control ids are save format (never renamed). Assets, upgrades and cap choices are `Cheap`
(layout kept); everything else re-plans.

## Regions emitted

| Key | Tags | What |
|---|---|---|
| `block:N` | {block} (+{core} for a roundabout island) | Land between roads (generated + existing, clipped to the area), road bodies subtracted. N is ordered along the area's principal (long) axis, so the same input always yields the same keys. |
| `edge:N` | {edge} | Frontage strips of depth `edgeDepth` along the *generated* roads inside each block, numbered block by block. |

Recipes: `roads.json` (roads only), `roads-with-parks.json` (park in every `block`),
`roads-with-zoning.json` (`edgeZoning` on every `edge`). Edge zoning emits `PlannedZoning`
quads of 24 m along road frontage (Marquee semantics: already-zoned cells are kept), shortened to
avoid `Reserved` space from earlier siblings.

## Attachment semantics (game layer must implement)

Every `PlannedNet` end has `StartAttach`/`EndAttach` (`-1` = free node) and
`StartAttachT`/`EndAttachT`. The id is the `ExternalId` the game supplied either as a
`SiteContext.NearbyNets[i].ExternalId` (an **edge**) or as an `AreaShape.Attachments[i].ExternalId`
of kind `RoadNode` (a **node**). Building the course start/end `CoursePos`:

1. **Id maps to a node entity** (RoadNode attachment): `m_Entity = node`, `m_SplitPosition = 0`.
   Planned end position equals `Attachment.Position` exactly.
2. **Id maps to an edge entity** and `T == 0` or `T == 1`: connect to that edge's start / end
   node: `m_Entity = Edge.m_Start` / `Edge.m_End`, `m_SplitPosition = 0`. (Ends within 1 % of
   an edge end are snapped to it in Core, so no 1-cm split pieces.)
3. **Id maps to an edge entity** and `0 < T < 1`: `m_Entity = edge`, `m_SplitPosition = T`
   (curve parameter of the existing edge's Bézier as given in `NearbyNet`; the planned end point
   is exactly `Bezier(T)` of that curve, height included). The game splits the edge there; several
   planned ends on the same edge carry different `T`s and must be applied against the *original*
   edge (map T → the split piece when creating definitions in order).
4. `-1`: free node at the planned position (`CoursePos.m_Entity = Entity.Null`); interior junctions
   are shared by exact position equality between planned nets.

Ids are only produced when `connectExisting` is on. Existing roads crossing the interior are cut
into: generated edges are split at the crossing so both pieces attach (rule 3) to the existing road.
Generated pieces lying on top of an existing road (parallel, inside both bodies) are dropped.

## Perimeter road

`perimeterRoad = auto` → **on for `Drawn`/`Painted`**, **off for `Block`/`Perimeter`** (those
outlines are already roads; interior streets end on the outline and attach to the bounding roads
via rules 1–3, using `RoadNode` attachments the picker reports plus `NearbyNets`).

When on: a `NetRole.PerimeterRoad` ring follows the outline (corners rounded by
`perimeterCornerRadius` for drawn/painted outlines, GRG free-area behaviour), with forced nodes at
every interior street end and at every point where an existing road crosses the outline (computed
from `NearbyNets`, plus `RoadCrossing` attachments) so it joins those roads instead of passing over
them. Perimeter pieces blocked by obstacles or duplicating an existing road are removed, and the
stubs this leaves are pruned. Nodes on a Block/Perimeter outline count as anchored (never pruned
as dead ends even without an id).

## Contract notes / requests

- `PlannedNet.StartAttach` cannot tell node ids from edge ids by itself; the game layer resolves by
  looking up the id in its capture maps (it owns both). A `StartAttachKind` field would remove that
  lookup — optional, not required.
- Group headings are read from the shared `Placemaker.UI.Group.*` namespace; roads defines its own
  in `Lang/<locale>.roads.json`.
- Pattern icons: the `pattern` options currently carry no icon (text choices) — there is no
  `coui://placemaker/` host and vanilla has no pattern artwork. Request: the UI bundles nine
  pattern icons under its namespaced image folder (e.g. `coui://ui-mods/images/placemaker/roads-pattern-<value>.svg`,
  `<value>` = grid, tree, organic, contour, mixed, loop, superblock, concentric, radial); once the
  path is fixed, roads sets `ControlOption.Icon` to it.
