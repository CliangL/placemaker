# Art pass — brief (lead, 2026-10-13)

User: "spin up an agent for each cluster… tell them they're artists; now that they can get the preview, go through
and clean it all up and make it awesome. The default textures need to be reviewed across the board."

You are the art director for your cluster. Make every preset of your types look intentional, beautiful and true to
its name, using only assets the player actually has.

Rules
1. Look first: `dotnet run -c Release --project Tests/Placemaker.Gallery -- --showcase --only <your types> --out docs/art-<you>`
   (square site, streets around, suggested entrances; real planners + real footprints). Read the PNGs, critique like an
   art director (composition, hierarchy, rhythm, spacing/breathing room, edges, focal points, density, colour of
   surfaces), fix, re-render, repeat. Don't commit renders.
2. Textures & assets: choose explicit, verified defaults per type/preset from the player's real catalog
   (C:\Users\jack\AppData\LocalLow\Colossal Order\Cities Skylines II\ModsData\Placemaker\catalog-dump.json; node for
   analysis). Put explicit asset ids in your presets/defaults (AssetRef with fallbacks) rather than relying on shared
   automatic pools. Never hazard/caution/road-marking decals in leisure places; no farm/industrial machinery in parks.
   Prefer vanilla assets; modded surface packs are fine as first choice with vanilla fallbacks.
3. Ground before props: surfaces/paint for large areas; props as accents. Stay well under ~600 objects per 1.2 ha unless
   the type is meant to be dense.
4. Stay in your lane: edit only your types' folders (+ their Lang/recipes/tests) and type-specific CategoryRules in
   those folders. Don't touch Core/Layers/Common, Core/Build, path routing, Game/, UI/ — other builders are changing
   those. Need something shared? docs/proposals/art-<you>.md.
5. Keep tests green (update expectations you intentionally change). Report < 200 words: what you changed per preset.
