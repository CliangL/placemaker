# Art pass a2 — shared requests (plaza, market square, fairground, amphitheatre, transit plaza)

Things I worked around in my own folders that belong in shared code.

1. **Square ground = a tiling, not stacked surfaces.** Every paved surface draws at the same −94 overlay
   priority, so a paved piece on a paved field z-fights and a grass piece under paving is hidden.
   `Core/Layers/Plaza/PavingKit.cs` (`PavingPlan`: elements claim in order, the field gets the rest,
   holes opened by one cut per row) is generic; worth moving to Common for parks/courts/aprons.
   Known overlap left in shared code: the fountain add-on's apron on a paved parent (plaza turns
   `apron` off in its feature preset), playground sand / court surfaces on paved parents.
2. **`LeisureKit.Frame` on a square** returns a diagonal axis (degenerate principal axes) — stalls,
   courts, step lines and grids came out at 45°. `PlazaDressing.SquareAxis` (direction carrying the most
   outline length, exact edge angle) fixes it; Common should offer it.
3. **Polluted automatic pools** (catalog-dump 2026-10-13) — classification rules should drop these:
   `bench` ← bus/taxi shelters, brick planters, a school wing; `kiosk`, `kiosk.stall`, `stage`,
   `seating.cafe` ← billboards/posters/pylons and 128 m supermarkets; `centerpiece.statue` ← clock hands;
   `water.jets` ← a farm sprinkler; `transport.bikeRack` ← 40 m bicycle parking halls (the racks are in
   `parking.decal`); `transport.shelter` empty; `events.tent` ← a 17 m branded marquee; `picnic` holds
   grills/blankets but no tables. My types now use explicit ids (`PlazaArt`) with vanilla fallbacks.
4. **Gallery**: colour paved surfaces by asset (I used a local, uncommitted tint by id) — otherwise a
   paving pattern renders as one flat colour and cannot be reviewed.
5. `plazatrees` add-on grid can land within 5 m of a parent tree near corners (FeatureTests
   PlazaWithTrees with the plaza's own allée on) — its clearance should use the reserved disc radius.
