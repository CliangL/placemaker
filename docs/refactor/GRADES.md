# The user's grades and the refactor brief (2026-10-15)

## Verbatim

> So we're going to do a major refactor. We're going to leave roads alone, and then we're going to perfect more rather
> than less, right? And I'm just going to tell you which ones are kind of okay and which ones are not okay. I'm going to
> go through all of them. Parking gets like a D minus, right? It's all right, but it just doesn't work. The trees go
> away. And I don't know. It's kind of boring. I guess it's all right. It does its job, right? Nothing to write home
> about here. Um, yeah, I don't think it should do any better. It should just get the trees working, and I guess the
> fences should be working perfectly and whatnot, but it's not bad. Um, one of the major bugs happens in parking,
> though, I think, where there's a certain dip, or maybe it's parking in relation to shops and squares. But anyways,
> let's go to the next one. Parks. So there's 13 types in parks. And then each one of those has types. We're going to
> cut that way down to only the best of the best. Maybe three types, and then each one has like four styles. But
> they're the best of the best, right? Um, some of my favorite ones are Park Urban Pocket. Park Garden Walk. And just
> Park Natural. They're okay. Right? They're like B pluses, maybe Bs. Um, those are just the styles in park. I think
> Botanical Garden is fine. Maybe we cut that down to just like flower show and shrubbery. But again, when I note
> these, I still want you to go through them and make them awesome. But we're cutting these down to make it actually a
> reasonable task. We can get rid of dog park. Picnic grounds, okay, but that should just go into park, right? And we
> can expand types to be more inclusive of different styles or whatnot. Zen Gardens, okay. These are okay. Memorial
> Park is like pretty high up there. Maybe it's like an A minus. Almost all of its types are pretty good, but a lot of
> them get kind of mundane. We're going to break away from mundaneness. Adventure Playgrounds, like an A minus as well.
> Most of the parks are kind of A minus or B minus around between those. Beach should move to its own section outside
> of parks. Um, yeah, it should move to its own section outside of parks. And I think we shelved a lot of the beach
> things. They were under like a water section. Let's give them their own thing. Shops and squares. These are
> basically all Fs. Um, mostly due to the fact they just don't work 100% of the time. And they're not clean or
> whatnot. I think you could go through and do a lot of cleanup here with iterations in the game. Tools now that you
> can, you know, modify the thing. Like amphitheater is like an F. It just looks horrible. Market square is like a D.
> Mall district, like it just doesn't work. That's the main problem. And it also, it just looks wrong. And the parking
> lots look weird. There's like farmland. There's like big giant holes in the ground. Not all the zoning zones. It's
> just broken. Shops and squares, most of those get like an F. Nature is mostly an A. Wetlands is probably the only A
> plus in this entire pack. Lookout Hill is maybe an A plus as well. There's some weird stuff with the road on Lookout
> Hill, though. Um, and again, we're going to be spawning agents for each category, or maybe even some of the styles
> will be split up into multiple agents. But no, I think an agent for each category would be good. Yeah, the hills are
> the Lookout Hill is like an A, but it could be. It's just a little weird. Some of the roads get a little weird. Farms.
> I think a lot of those are all right. I think you just got to look through each one. Animal Park and Farmstead are
> much better than the urban gardens. Urban gardens are kind of ugly. And they repeat and they use a lot of assets and
> whatnot. Sports parks are okay. They get like a D. They're just kind of weird. I like how they're unique, though. I
> don't think we want to majorly cut down on the amount. There's just some that just, they're bad and they don't.
> They're not unique. So it's like, why do they exist? You know, that's basically the gist of what I'm saying.

## The lead's reading (for each category agent)

Rules for everyone:
- **Roads: leave alone.**
- Fewer, better: cut each category to the best of the best, then make every survivor **awesome and not mundane**.
- "Cut" means **hide** (shelve), never delete code. Do it through one central shelf, so things can come back.
- Every change is judged **in the game**: the game runner (`Show.ps1`, through the game queue), 45° pictures, and the
  logs read by you.

| Category | Grade | Direction |
|---|---|---|
| Parking | D- | Not more features. Make it **work**: trees, bushes and lamps must stay visible after the build; fences perfect (never across an entrance, never farm-lot fences). Fix the **dip/pit** bug, likely where parking meets shops and squares. |
| Parks | B-..A- | Today 13 types. Cut to about **3 types × about 4 styles**, the best of the best. Favourites, B to B+: the park styles **Urban Pocket, Garden Walk, Natural**. **Picnic grounds folds into Park.** **Dog park: hide.** **Botanical garden: keep, cut to Flower show + Shrubbery.** **Zen gardens: okay.** **Memorial park: A-** (most styles good but mundane). **Adventure playground: A-**. Types may be broadened to take in good styles from cut types. |
| Beach | — | **Its own category**, out of Parks. Bring back the beach and water things that were shelved under "water". |
| Shops & squares | F | Make them **work 100% of the time** and look clean. Fix the farmland lots, the giant holes in the ground and the zoning that isn't applied (shops facing the wrong street). **Amphitheatre F** (looks horrible). **Market square D.** **Mall district** doesn't work and looks wrong. The parking lots inside look weird. Iterate in the game until clean. |
| Nature | A | **Wetlands A+** (best in the pack, don't break it). **Lookout hill A/A+**, but its roads get weird. |
| Farms | ok | Look at each one. **Animal park and Farmstead** are much better than **urban farm / urban gardens** (ugly, repetitive, too many assets). |
| Sports | D | Weird but unique; keep the uniqueness and don't cut much. Cut the ones that are bad **and** not unique. |
