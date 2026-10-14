# Type wave — creative brief (creative director: lead, 2026-10-13)

The user: "We've created this awesome foundation… let's add 20 more types… each with its own custom subsections,
customizations… at least two, maybe five presets… get crazy with it. Industrial zones (not buildings): rubble piles,
garbage, decals of garbage and dirt and rocks; construction zone; ugly big lake construction zone; abandoned
building zone; golf course; skate park with broken-down highway… everything you can think of."

## What "a type" must be (definition of done)

1. A Fill layer `[AreaGenerator(order, Group = "<group>")]` in `Core/Layers/Types/<TypeName>/` (your folder only).
2. **Subtypes = presets.** 4–6 presets that look genuinely different (not just density tweaks), each with a name
   a player understands ("Scrapyard", "Salvage yard", "Container depot"). Plus a `subtype`/`style` Choice control.
3. **Deep but tidy options**: 15–40 controls in 4–8 groups (essentials first), labels ≤ 3 words, explanations in
   tooltip keys. Sliders with meaningful units (count/ha, m, %).
4. **Features** (IHasFeatures): reuse existing add-ons (playground, restrooms, shops, decals, terrain, rocky outcrop,
   picnic, fountain, flowerbeds, bigtrees, edgeZoning…) and add type-specific ones in your folder.
5. **Composition, not soup**: real spatial structure (zones, rows, clusters, focal points, circulation, edges,
   transitions). Use PlannedTerraform (pits, mounds, terraces, berms — always blended to the original height at the
   outline), PlannedPaint (terrain textures), surfaces, decals, nets (paths/tracks/roads), objects. Look at how
   Core/Layers/Park, Plaza, Leisure do it and reuse Core/Layers/Common helpers (PlaceSolver, Scatter, Occupancy,
   TerrainShaper, StreetFrontage, Fences, Entrances…). Respect `input.Reserved`.
6. **Assets**: compose existing game assets only (no new models). Use categories; add new ones as
   `public static partial class AssetCategories` in your folder, and classification rules as a class implementing
   `Placemaker.Core.Build.ICategoryRules` in your folder (name patterns + exclusions + AutoEligible). Real asset
   names are still unknown (catalog-dump.json not produced yet) — use broad, well-chosen name patterns from vanilla
   CS2 naming conventions (CamelCase, e.g. "Pallet", "Container", "Barrier", "Cone", "Pile", "Rubble", "Crate",
   "Barrel", "Tire", "Scrap"), always with graceful degradation (Info issue + skip) when a category is empty.
7. **Shipped recipe** `Recipes/<typeId>.json` (single layer, nameKey, icon that exists under the game's UI Media —
   check `C:\Program Files (x86)\Steam\steamapps\common\Cities Skylines II\Cities2_Data\Content\Game\UI\` or
   similar — and `descriptionKey`), and `Lang/en-US.<typeId>.json` with every key.
8. **Tests** `Tests/Placemaker.Tests/Types/<TypeName>Tests.cs`: every preset plans a 1 ha and a 6 ha L-shape,
   deterministic, nothing outside the area / on paths / in Reserved, surfaces accepted, terraform blended at the
   edge, < 250 ms for 5 ha, features compose, all control labels exist in en-US.
9. Stay inside your files: your type folders, your recipe/lang/test files. Don't edit shared files (Contracts,
   Composition, Common, Park/Plaza/etc., AssetHeuristics, LeisureHeuristics, csproj, UI). Need something shared?
   Write `docs/proposals/types-<you>.md`.

## Groups (Make row)
parks · gardens · squares · sports · water · nature · farms · events · industrial · transport · parking · roads

## The 20 new types (two per builder)

| # | Type id | Group | Brief / subtypes (presets) |
|---|---|---|---|
| 1a | scrapyard | industrial | Scrapyard, Salvage yard, Container depot, Tire dump, Car graveyard: piles, stacks, rows, crane/forklift props, oil & dirt decals, rough fences/gates, dirt/gravel paint |
| 1b | landfill | industrial | Active landfill, Capped landfill (grassy mound), Compost yard, Recycling yard: terraformed mound/cells, garbage decals, dirt paint, access track, fences, gulls? |
| 2a | constructionSite | industrial | Foundation dig, Road works, Site compound, Demolition site, Earthworks: pits (terraform), material piles, barriers/cones, cabins, scaffold props, mud decals, fences with gates |
| 2b | quarry | industrial | Open-pit quarry (terraced pit), Gravel pit, Flooded quarry lake ("ugly big lake construction"), Sand pit, Abandoned quarry: stepped benches terraform, rock props, haul road spiral, water option (permanent) |
| 3a | abandonedLot | industrial | Overgrown lot, Rubble field, Demolished block, Squatter camp, Fenced wasteland: rubble piles, weeds/bushes, cracked paving decals, graffiti, broken fences, debris |
| 3b | urbanRuins | sports | DIY skate spot under the overpass, Broken highway ruins, Graffiti park, Parkour yard, BMX dirt jumps: concrete slabs, ramps, rubble, graffiti decals, pillars, dirt mounds |
| 4a | golf | sports | Golf course (holes: tee/fairway/green/bunkers/water), Pitch & putt, Mini golf, Driving range, Disc golf: grass surfaces/paint by role, sand bunkers, flags, paths, ponds (terraform) |
| 4b | lookoutHill | nature | Hilltop lookout (spiral path to a viewpoint), Sledding hill, Viewpoint mound, Observation terraces, Kite hill: big blended terraform hill, switchback paths, benches at the top, trees on slopes |
| 5a | beach | water | Sand beach, Lido (pool deck look), River beach, Volleyball beach, Nudist cove?→ "Quiet cove": sand paint/surfaces to the waterline, umbrellas/loungers, lifeguard, boardwalk paths, showers |
| 5b | waterfront | water | Promenade, Pier & boardwalk, Harbor walk, River steps, Fishing spot: linear layout along the area's water/longest edge, railings, lamps, benches, terraced steps down |
| 6a | wetland | nature | Wetland park, Water garden, Reed marsh, Rain garden, Bird reserve: shallow ponds (terraform + water, permanent), boardwalks, reeds/bushes, hides, islands |
| 6b | zenGarden | gardens | Karesansui (dry gravel), Stroll garden, Tea garden, Moss garden, Bamboo grove: raked gravel paint/surface, rocks, pond + bridges, lanterns, pruned trees, stepping-stone paths |
| 7a | urbanFarm | farms | Orchard, Vineyard, Market garden, Greenhouses row, Lavender field: rows (oriented), crop surfaces/paint, sheds, barns props, fences, tracks |
| 7b | animalPark | farms | Petting farm, Paddocks, Horse paddock, Deer park, Aviary garden: fenced enclosures, shelters, troughs, hay, animal spawners, visitor paths |
| 8a | fairground | events | Fairground, Festival field, Funfair, Christmas market, Food truck rally: stage, stalls grid, tents, light towers, temporary fences, portable toilets, open fields |
| 8b | amphitheatre | events | Amphitheatre (stepped bowl, terraform), Open-air cinema, Bandstand lawn, Concert meadow, Speakers' corner |
| 9a | adventurePlayground | parks | Adventure playground, Toddler garden, Water play, Nature play, Jungle gym: zones by age, sand/rubber surfaces, equipment clusters, parent seating, shade |
| 9b | memorialPark | parks | Memorial garden, Monument square, War memorial, Reflection garden, Sculpture park: axes, focal monument, reflecting pool, hedges, solemn lighting |
| 10a | restArea | transport | Highway rest stop, Picnic stop, Truck stop, Scenic overlook, EV charging stop: parking + picnic + restrooms + vending/kiosk + trees |
| 10b | transitPlaza | transport | Park & ride, Bus interchange plaza, Bike hub, Taxi rank, Station forecourt: bays (as paved areas + markers), shelters, bike racks, plaza seating |

Builder 11 (park & plaza depth): more path styles (≥ 6 new: riverside meander, figure-eight, spoke & ring,
woodland trail, boardwalk loop, promenade avenue…), more park presets (≥ 6 new subtypes: Victorian park, English
landscape, Modern minimal, Wildflower meadow, Urban pocket, Riverside park…), plaza: more levels/terrace variants,
and anything the previous rounds left shallow. Owns Core/Layers/Park, Core/Layers/Plaza, Core/Layers/AddOns only.
