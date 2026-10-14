# Grand park wave — brief (lead, 2026-10-13)

User: "A lot of parks are a lot more intricate: baseball fields, lakes plus islands on the lakes, a bike path going
through it, a smaller walking path, a dirt patch, a grass patch, a rocky area, a track field, a tennis court, a soccer
field built in. A terrace garden with a circle and spokes and an oval, a big statue — statues could be big. A little
amphitheatre built into the terraces. Self-aware about water: if I draw an area half in water, half out, make a pier,
build the terrain into a wharf / boat wharf. Palace of Fine Arts type stuff — not a big building, but statues, a
walkthrough thing (colonnade/rotunda)." Get crazy, but it must look designed, not random.

Definition of done = docs/TYPE-WAVE.md rules (pure Core, tests, en-US strings, groups, presets, blended terrain,
graceful degradation, respect Reserved, features with "Path to it" + position). Each builder owns only its listed files.

| Builder | Scope (owns) |
|---|---|
| g1 Sports fields | Add-ons in `Core/Layers/AddOns/Fields/`: baseball diamond, soccer/football pitch, running track (oval with infield), tennis courts (1–4, fenced), basketball full court, cricket pitch, skate bowl pad. Real dimensions scaled to fit, orientation (N–S preference or along the long axis), painted lines via decals/paint, levelled pads (blended), fences/backstops/benches/floodlights/stands. Offered as park & sportsPark features ("Path to it", position). |
| g2 Water & shore | `Core/Layers/Common/Shore/` + add-ons `Core/Layers/AddOns/Water/`: a shared ShoreKit that detects water inside/next to an area (WaterDepthAt) and offers shoreline treatments any type can call — pier, boat wharf/quay (terraformed fill + quay edge + bollards + boats-on-water props if any), boardwalk, seawall promenade, beach edge, reed edge. Upgrade the park lake: islands (1–3), bridges to islands, fountain jets, boathouse/rowboats, lakeside promenade. Park/plaza: a "Shore" feature that auto-activates when the area is partly in water. |
| g3 Paths & ground | Park path HIERARCHY in `Core/Layers/Park/` (owner of park path code only): bike path (through-route, wide, separate from footpaths; use bike/pathway net categories with graceful fallback), main walk, small footpaths, dirt trails, desire lines; plus ground patches feature in `Core/Layers/AddOns/Ground/`: dirt patch, grass patch (mown vs meadow), rocky area, gravel, sand — painted/surfaced with blended borders. |
| g4 Monuments & follies | `Core/Layers/AddOns/Monuments/`: big statue / monument (scale up statue props, plinth, steps, axis), "Palace of Fine Arts" style folly made of props (rotunda ring of columns/arches/statues + lagoon in front + colonnade walkways), obelisk, triumphal gateway, sculpture walk. Terrace garden feature: circle + radial spokes + oval rings, terraced levels, central statue/fountain, integrated mini amphitheatre carved into the terraces facing a stage/feature. |
| g5 Integration | Shared-platform items from docs/proposals/types-*.md: objects placed on terraformed ground get the NEW height (a TerrainField/height override passed to children & placement); terraforming parents pass the new ground height to their features; surface rules can add custom surface categories (rubber, bark, clay, track); path joins helper (split a planned path where a spur meets it); dedupe duplicate features (apiary vs honeyHives, farmStand vs farmStall → one each, keep both ids working via alias); declare variety categories (IVarietyCategories) for piles/junk/wrecks/crops/rocks across types; make flaky timing budgets robust. Owns Core/Layers/Common (except Shore/), Core/Build, Core/Contracts.cs additions (additive only), and edits to type folders strictly limited to these items. |
