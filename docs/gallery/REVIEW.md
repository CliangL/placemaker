# Gallery review — first pass (2026-10-13)

Rendered with `dotnet run -c Release --project Tests/Placemaker.Gallery -- --out docs/gallery`
(317 images: every shipped recipe + every fill type × default / each preset / all features / no features,
on one irregular ~12 000 m² site, flat ground, no nearby roads). Real Core planners, AutoAssets with the
catalog's automatic pools + park palettes, real footprints from `Tests/Placemaker.Tests/Fixtures/catalog-bounds.json`.
Per-image numbers (benches off-path, overlapping solid objects, objects outside) are in `gallery.json` and on each card.
"Looked at" = PNG inspected by eye; the rest is from the metrics only.

## Cross-cutting (fix these first — they show up in almost every type)

1. **Automatic pathway = `PedestrianPathWide01` (8 m).** `automaticPools["net.pathway"][0]` is the wide path, so every
   park walk is an 8 m band (the playtest "thick paths by default"). The thin vanilla `Pavement Path` (3 m) is only 6th,
   behind bridges and a pier → fix pool ranking (AssetHeuristics: Pavement Path first; never bridges/piers/quays for ground walks).
2. **Entrance stubs stick out of the area.** Almost every type's entry path ends 5–8 m *outside* the outline
   (rounded cap below/above the ring), and paths along an edge (formalGarden, fairground, waterfront) run half outside.
3. **Lamps inside the walk.** Lamps are offset for the default half-width (1.5 m) but the resolved path is 8 m wide, so
   lamp rows sit on the paving (memorial, cemetery, botanical, park). Furniture offsets must use the resolved net width.
4. **Benches not on paths.** Off-path benches (> 3 m from any path edge): marketSquare 41, plaza 16, memorialPark 12,
   transitPlaza 7, amphitheatre 6, urbanRuins 6, adventurePlayground 5, skatePark 5, dogPark 4, formalGarden 4, orchardPark 4.
5. **Oversized "furniture" from category pools.** Generic Furniture categories resolve to huge props
   (logistics/industrial/ruins/lookout/glasshouse ≈ 20–60 m) that overlap each other and leave the area:
   logisticsYard, urbanRuins, urbanFarm, lookoutHill, communityGarden, fairground (stage block). Pools need a size cap per
   use, and placement must use the real footprint (overlaps: logisticsYard 53, urbanRuins 47, landfill 31, constructionSite 24, scrapyard 23).
6. **Decal scatters are huge.** Decal patches (ground/leaves/dirt) are 10–15 m squares overlapping everything
   (park features-all, logisticsYard, urbanRuins, landfill) — size or density is off.
7. **Trees:** crowns at real size (estimated 0.28 × height) overlap paths and each other a lot (memorial, picnic, wetland,
   lookoutHill); planting distance is from guessed radii, not the real crown.

## Per type

- **memorialPark** (looked): walks are path nets now but 8 m wide (blobs); 12 benches off-path, lamps on the walk;
  hedged rooms are fine but tiny and unreachable (no spur into them); sculpture preset: statues on 8 m squares overlap the walk
  and each other (purple squares + star patterns); entry walk leaves the area top and bottom.
- **park** (looked): clean layout; walk 8 m; benches/lamps on the path edge — OK. features-all: decal squares everywhere,
  amenities (playground, picnic, courts) dropped as grey slabs with no path link; amenity spurs overlap furniture clusters.
- **formalGarden** (looked): one straight axis only; the beds (hedge-framed, pink flowerbeds) have no paths at all; path runs
  outside the top edge; 4 benches only at the fountain.
- **cemeteryGrounds** (looked): hedge pieces rotated 90° (bars across the row, not along it); grave fields fine; lamps on the cross walks.
- **plaza** (looked): empty paving, only a bench/lamp ring around the fountain (16 benches flagged off-path, no paths exist on a plaza — expected; metric noise).
  Needs trees/planters/dressing in the default.
- **marketSquare** (looked): stalls only along the bottom edge, 41 bench pieces as a perimeter ring, no centre activity; tree ring hugging the outline.
- **transitPlaza** (looked): big purple shelter block hangs outside the area (bottom-left); regular tree grid fine; benches loose in the field.
- **parking** (looked): good; lamps on aisle edges OK; 4 overlaps (lamps on bays); entrance marker outside → warning EntranceNoRoad (expected without a road).
- **botanicalGarden** (looked): 130 lamps lining the paths (both sides, ~4 m spacing) — far too many; glasshouse lonely; flowerbeds scattered without paths.
- **urbanFarm** (looked): 4 744 crop props — the plots are solid purple carpets; the glasshouse prop is 20 m; hedges outside path.
- **adventurePlayground** (looked): the purple building cluster (glasshouse/shelter) overlaps itself; 5 benches off-path; nice zones otherwise.
- **amphitheatre** (looked): seating bowl is a paved semicircle with benches in wedges — reads OK; two centerpieces on the stage overlap each other.
- **zenGarden** (looked): perimeter loop path is 8 m and eats the garden; rock row (purple) is a dense line of overlapping rocks; inner gravel field empty.
- **sportsPark** (looked): courts and pitch present but not connected to the path; goals at pitch ends OK.
- **campground** (looked): one spur path ends in the middle, pitches straddle it; most of the area is tree scatter on bare ground (no grass surface).
- **golf** (looked): no grass surface (ground colour), two disconnected cart paths; greens/fairways sit apart; "FewerHoles" at 12 000 m² (needs more area in the gallery).
- **dogPark** (looked): huge empty lawn; agility props tiny and lost in the dirt patch; 4 benches off-path; no paths inside.
- **picnicGrounds** (looked): picnic sets are scattered loose pieces far from the path; no shelter; OK tree canopy.
- **waterfront** (looked): no water on the gallery site (NoWater) → boardwalk along the bottom edge half outside, path to the top leaves the area; grass covers 90 %.
- **logisticsYard** (looked): containers/warehouses overlap massively and leave the area (see cross-cutting 5).
- **wetland** (looked): ponds + reeds read well; trees overlap paths; paths cross between ponds OK.
- **fairground** (looked): stage block 18 m square stands alone on the lawn; stalls in rows along the path fine; path runs outside the top edge.
- **urbanRuins** (looked): ruin walls/slabs as overlapping 20 m purple blocks hanging out of the bottom; decal squares.
- **lookoutHill** (looked): terraform is visible as a stepped grid (dab cells), the tower prop is two overlapping 25 m rotated squares on the summit; trees cover the path.
- **abandonedLot**: 12 overlaps, 73 decals (metrics).
- **beach**: NoWater on the gallery site; 5 overlaps (metrics).
- **communityGarden**: 1 579 crop props, 58 plot surfaces (metrics: dense, check plot paths).
- **constructionSite**: 24 overlaps (metrics).
- **farmstead**: fine by metrics (2 overlaps).
- **greenwayPark**: fine by metrics.
- **hikingTrail**: 9 overlaps, 1 bench off-path (metrics).
- **landfill**: 31 overlaps, 172 decals (metrics).
- **natureReserve**: 2 benches off-path (metrics).
- **orchardPark**: 4 benches off-path (metrics).
- **quarry**: FewerBenches info (metrics).
- **restArea**: 2 benches off-path (metrics).
- **roads**: renders nothing on the default 1.2 ha site — use `--roads` (s34: 30 ha road sites with two existing streets, a farm and flat / gentle / hill / valley / steep ground; per-card RoadsAudit line). Known (s34): organic layouts leave a round hilltop empty; access management keeps a junction when cutting it would strand a branch; the old-town preset is a radial circus with organic lanes, not a dense medieval mesh.
- **scrapyard**: 23 overlaps, 422 scrap props (metrics).
- **skatePark**: 5 benches off-path (metrics).

## Gallery limitations (next steps for the tool)

- Flat ground, no nearby roads/water: water-dependent types (beach, waterfront, wetland lake) and roads under-report.
  Add site variants (a road along one edge, a lake edge) to the job list.
- Tree crowns use the same estimate as the in-game sketch (`max(bounds/2, 0.28 × height)`); real crown sizes would need a crown dump.
- No text in the PNGs; titles/metrics are in `index.html`.

## Second pass — s2 fix round (layout size 50 %, thin walks)

Gallery now renders at the game default Layout size (`--scale`, default 0.5). Looked at park, memorialPark,
formalGarden, plaza, marketSquare, greenwayPark, amphitheatre:
- Automatic walk = thin "Pavement Path" (`AutoAssets.PickWalk`, pool ranking); wide only for PathwayWide
  (planning half width now its real 4 m). Free entrance ends are pulled inside the outline.
- park: loops back at 50 % (coverage-loop anchor retry, missing loops penalised), benches/lamps beside thin walks.
- memorialPark: off-path benches 85 → 14 over all variants (rooms have inner walks), no painted procession band.
- formalGarden: main axis + cross walks between the parterres; off-path benches 24 → 0.
- greenwayPark: seats at the path edge; lamps every N m along chained curve segments (no lamp per sample segment).
- Remaining "off-path" counts on plaza / marketSquare / amphitheatre are seats on paving or in seating rows (expected).
- plaza default still under-dressed; marketSquare tree ring hugs the outline (next pass).

## s20 — water types on sloped shores (`--water beach,waterfront --shores flat,bank,headland,cliff`)

Looked at every beach and waterfront preset on all four shore sites (top-down + `-3d` oblique with sections).
- Terraces gone: sections show one smooth graded surface; beach faces run under the water; the 6.5 m bank is cut back to a
  ≤ 25 % embankment behind one level promenade; dunes are low smooth ridges (on a strongly rising bank they are absorbed by the cut).
- Headland: the low shore gets the beach / promenade, the cliff face stays natural, a cliff-top walk with a lookout runs on the
  plateau; the entrance in the steep transition reaches whichever walk it can at ≤ 12 % (a walk it cannot reach gently gets no
  connector — known limit, place an entrance near it).
- Harbour on a low shore raises the apron to deck level (1.8 m) with a long gentle bank behind: reads as a built-up quay (intended).
- Known: terraform outlines in the top-down view show where the change exceeds 0.12 m (patchy rectangles on gentle sand); quay
  benches follow the coast's curvature (slightly rotated on bends).

## s18 — plaza sizes and palettes

Checked: `--only plaza --shapes triangle,sliver,square,l --areas 0.02,0.03,0.05,0.15,0.6,1.5 --no-features [--no-shops] [--palettes]`.
Micro/small squares are one idea (field + band + focal pad, bench ring, corner trees or a strip allée, edge lamps); the
fountain sits on the pad's centre. Known: field pieces show thin seam lines in the render where the field is split
round holes (same stone in game); street shops (on by default) still take most of a medium square on a site with
streets on every side; the sunken-court level and the fountain are not concentric on medium squares.
