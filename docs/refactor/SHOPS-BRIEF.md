# Shops & squares: the user's critique (2026-10-16, playing the merged build)

Pictures in `docs/refactor/shops-ref/`:
- `game-stripmall-is-a-park.webp`: what our "strip mall" builds today, a big park with small lots and shops.
- `real-1-lifestyle-center.webp`, `real-2-strip-mall.webp`, `real-3-office-retail-park.webp`: real places to aim for.

## Verbatim

> I think strip malls could be a lot, lot, lot better. We do them now, but they kind of play out like a little parking
> lot with some buildings on it, and then just like a huge park area before another little building in a parking lot
> area. [...] It's fine, but it doesn't seem like it's doing what it wants to do. This could be like a park slash strip
> mall, but we could go better. I want it to look more like real life where it's basically all parking. [...] Outdoor
> mall is like 80% trees and park. Maybe we just need to rethink it completely. It seems to just be a mainly grass and
> park and not mall. Here, let me send you some real Google Maps images of some amazing looking strip malls. And let's
> test them on bigger blocks. Like right now, the blocks we test them on make them look kind of okay, but what about a
> block that's four times as big or a rectangle block or like an L shape? And you can test circles and whatnot for
> strip malls and all that.
>
> And we can cut this down. There's a lot of shops and squares. I kind of like mall park. I really like mall park
> family park. I really like mall park commons. I really like mall park garden square, but it doesn't seem to fully
> work always. The garden kind of stops maybe. That needs a little bit of cleaning up. Strip mall is good, but it's not
> a strip mall. It's like a park mall. They all need to be redone and we need to cut down on them by maybe one third or
> one quarter of the amount. I like big box. Big box is good, but again, it's not a big box. It's like a park mall. We
> need a real strip mall. Megastores is a lot better. Megastores shouldn't be called megastores. It should just be more
> what the regular strip mall is. But again, it's not perfect. It gets like an A minus. Retail park and default are
> okay. Strip malls are basically all Fs. Outlet Village has the same problem as strip malls, where there's just giant
> park areas. Fairground doesn't belong in shops and squares. Get rid of all the fairground ones. Those are all covered
> in other things. Amphitheater, don't we have a park amphitheater already? Amphitheater's nice. Bandstand Town,
> Amphitheater. Those belong in parks, not shops. Get rid of the fairground ones. And then clean up the mall ones.
> Mall District gets like nearly an A. That's what I'm looking for. That should be front and center. Almost all these
> are amazing. If anything, we just get rid of strip malls and we make mall districts the main thing. Downtown is like
> nearly an A. It needs more parking though, intermingled. Shopping village: I don't like the ones with the pedestrian
> or the civilian ones. Shopping village is okay. Outdoor malls, not great. They're very park heavy. If the user wants
> a park next to a mall, they can just draw a park next to a mall. They don't need a split thing, unless it's a cool
> incorporation like some of the mall parks do: the Garden Square, the Family Park. I want more of that. Market squares
> are all bad. Big market is good, but shouldn't it fill the entire area and have more intermingled other things?
> Anyways, cut down by maybe 25% and keep the best of what I told you. A lot of these seem like they're not malls.
> They're like park plus buildings, or a little tiny parking lot plus a giant park and one shop. If I zone a big area
> and I say I want a strip mall, I should get a giant strip mall with multiple rows of buildings and a labyrinth of
> parking, right?

## Correction (the user, relayed by the lead, same day)

> I didn't say 25% less. I said 25% remaining, 75% less. Perfect the best. Ditch the rest.

So about a **quarter** of the shops and squares types and styles stay; the rest is hidden through the shelf.

## Reading, as a checklist (after the correction)

| Item | Verdict | Do |
|---|---|---|
| Mall district | nearly A | **Front and centre**: the first shops type. Keep **Shopping district** and **Downtown**; hide car-free quarter (pedestrian), mixed use and shopping village. |
| Mall district: Downtown | nearly A | Needs more parking mixed in. |
| Mall park: Family park, Commons, Garden square | liked (cool park and mall integrations) | Keep all three. Garden square: the garden "stops", so fix it so it always works. |
| Strip mall | F | **One real strip mall**, built from the Megastores look (Megastores is renamed away): mostly parking, rows of buildings facing the lot, several rows and a parking "labyrinth" on big areas (`real-2`). Not a park. Corner, neighbourhood and rear alley hidden. |
| Big box | good idea, wrong result | A real big box (a large store and a big lot), **only if it earns its place**; else hidden. |
| Retail park, default | okay | Only if among the very best; hidden for now (its Megastores look becomes the strip mall). |
| Market square: Big market | the only good one | Keep **only if it fills the whole area well**, mixed with other things. Weekly and farmers hidden. |
| Outlet village, outdoor mall, shopping village, high street, plaza, shopping plaza, shops & park | ditched | Hidden. |
| Fairground | doesn't belong | **Hidden** (every style). |
| Amphitheatre, Bandstand lawn | nice, but parks | **Moved to Parks** (Parks has no amphitheatre type of its own). |
| Overall | | **About 25 % remains**; spend the effort perfecting those. |

**Testing:** big areas (four times the test block), long rectangles, L shapes and circles, beside the small block.
Every kept type and style must build correctly every time and look like a real place (compare with `real-1..3`).
