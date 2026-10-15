# User playtest on master 650be0f (2026-10-15 12:09–12:12) — read this before designing

Installed: master 650be0f (s59 one-pass anarchy + s60 zoning fallback). Builds now complete (audits: "built 229, left out 7
(+91 kept despite collisions)", "built 133, left out 8", "built 113, left out 11"). Evidence:
`.forensics/Placemaker-20261015-master.log`, `.forensics/Player-20261015-master.log`, `.forensics/group-*-20261015-12*.json`.
Screenshots: `docs/teardown/shot-106-commercial.webp` (commercial / shops build), `docs/teardown/shot-107-shops.webp`.

## The user, verbatim
"These are pretty horrendous. This is the shop running. I see the shops got put on the wrong side. They should have been put on
the alleyway. Why are we doing farmland? How are you supposed to even park over there? There's just a fence. Whoever made this
shop thing did a horrible job. Also, my game gets hung a lot when I'm switching styles or switching types. I also have an idea:
what if the streets themselves in whatever you're touching could turn off their zoning, or we could turn off their zoning.
Because the houses are just going the wrong way around. The zones are on the wrong way."

## What the screenshots show (lead's reading)
* shot-106: our build made a ring road (named "Summerfield Lane" / "Brookhaven Lane" — real roads) around a central parking lot
  (stalls, green islands, accessible stalls). Wooden fences on both sides of the ring and around the lot — the lot looks
  fenced off from the ring. Between the ring and the city streets (Vermont, Mill, Mulberry): strips textured like **farmland**
  (striped dry crop surface) holding white rectangles with car/parking icons (parking spaces with no access — fenced from both
  roads). A large **terrain pit/crater** beside Brookhaven Lane (pit guard failed). Growables under construction (grey
  construction walls) at top right face **Summit Street**, i.e. the outer city street, not our lane.
* shot-107: two growables under construction between an outer street and Vermont Street, facing the outer street — the shop
  cells were taken by the city street's zone blocks, so buildings face away from the alley/lot.
* Log: zoning refresh "22 cutout cell(s) (planned 36)", "12 (planned 36), 4 plot(s) cannot zone (land blocked)", most plots
  "4 open, 18–44 blocked"; MISMATCH on most plots.

## Requirements this adds
1. Shops face OUR alley/lane (the shopping street of the build), never the outer city street.
2. The user's idea — **turn off zoning on the streets touching the area** (city streets and/or our own roads) so only the
   intended road zones the shop cells — must be evaluated as a first-class option (game internals: how zone blocks are made per
   road edge side, how other mods toggle zoning per side, persistence in saves, saves without the mod).
3. No farmland-looking crop textures in commercial/parking builds unless chosen.
4. Every parking space and the lot itself must be reachable: no fences between parking and its access road; no stalls in
   strips without a lane.
5. No terrain pits next to roads (pit guard must hold on every type).
6. Switching style or type must never hang the game (planning off the main thread / debounced / cached).

## Third screenshot (12:13, not saved to disk — lead's description)
Block between Rosewood, Mulberry, Mill and Birdsong Streets. Our build: a long **empty asphalt lot with no stall markings**,
fenced on all sides by wooden fences (no visible entrance), next to a wide strip of **rough dirt/rock "crop" texture** inside the
fence; a paved pedestrian strip ("Moore Street"/"Birch Street" — our own named roads) with a row of large trees; at the back a
growable under construction with a tower crane and another growable (construction walls) facing **Birdsong Street** (the outer
city street). User: "I can't even tell what this is trying to be. It's so horrible."

## The user on testing (12:14)
"If we have a way to boot up a super stripped-down version of the game and make a test suite in the game, you could actually
start to test these. The test suite for the game would be the ultimate way to do it. You could cut down on the game's textures
and load time and just load up images of what it looks like." => besides the offline bench, design a **stripped-down in-game
test suite**: minimal boot (only Placemaker, lowest graphics, tiny test map/save, no intro), auto-run scenarios, screenshot each
result to PNG, JSON results, clean quit — run by the lead unattended when the user is not playing.
