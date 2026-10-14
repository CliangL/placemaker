# Playtest 4 (2026-10-13) — quality problems & fix round

User: green preview fill hides elements; padding is odd; thick paths by default (wants thin); memorial park doesn't
match what it advertises (paved blobs, benches in the grass, no bushes/hedges/decals); "everything is a little too
large — a quarter or half size, across the board".

Diagnosis (lead):
- Build log: "553 parts, 480 optional objects dropped (collisions)" — hedges/bushes/decals planned but silently dropped
  by the game because plans space objects with guessed sizes, not real footprints (AssetInfo bounds exist!).
- Memorial walks are painted surfaces, not path nets → bench placement (expects nets) lands benches on the lawn.
- Wide/mixed path defaults; opaque green surface fill in the sketch preview; stacked fixed margins (edge + belt + road clip).
- No visual QA loop: tests check invariants, nobody looks at the result.

Decisions:
1. Real footprints + a deterministic declutter/resolve pass; decorative objects never silently vanish (count shown in Review).
2. Global **Size** setting (default 50%) shrinking layout distances; props keep real size; exempt real-dimension layouts.
3. Thin paths default everywhere; walks = path nets; one consistent small edge margin (adjustable).
4. Readable preview: faint tint, crisp outline, clear symbols.
5. Visual review loop: render every type/preset top-down to PNG (mock host + real catalog footprints) for review.

## Playtest 4b (same evening)
1. Terrain build still crawls: "stuck at 18%", progress jumps, 2 FPS, purple blobs spammed everywhere (brush previews).
   Need a fundamentally better method than many brush dabs (apply terrain directly, invisible, bounded time).
2. Wrong assets: benches wrong, decals wrong, planter boxes used where bushes/flowerbeds expected; "looks like a nightmare".
3. Rocky outcrop should be mostly terrain PAINT with a few rock clusters, not evenly spaced rocks.
4. Some paths clip into the ground.
5. Deleting a park leaves decals, bushes, benches, lamps behind — removal must delete everything we built.
6. Bike route picks its own entry points; parks need entrance TYPES (pedestrian, bike, service…) chosen from the adjacent
   road/path type, planner routes accordingly.
7. Feature options (Playground, Central plaza) can't be collapsed/hidden.
8. Tell me there's no room for a feature in Design, before the plan/review.
9. Design sketch doesn't show some features' objects (picnic, playground) — only the path to them.
10. More features on by default.
