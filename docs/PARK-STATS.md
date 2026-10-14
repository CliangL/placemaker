# Park stats — how vanilla parks work, and what Placemaker does with it (s17)

Status: research + implementation, 2026-10-14. Source: CS2 **1.6.2f1**, decompiled `Game.dll` (ilspycmd) and the
real prefab values read from the game's data (method in §6). Code: `Core/Build/ParkStats.cs`,
`Core/Build/VanillaParks.cs`, `Game/Build/BuildService.ParkStats.cs`, `UI/src/stages/ParkStatsSection.tsx`.

## 1. In one paragraph

A vanilla park is a **building** (a `BuildingPrefab` with a lot of 8 m cells) whose prefab carries a few components:
**Attraction** (attractiveness → tourism and land value), **LeisureProvider** (how much leisure a visit gives),
**ServiceCoverage** (the park "entertainment" coverage that raises residents' wellbeing), **Park** (maintenance pool,
homeless allowed), **ServiceConsumption** (upkeep), **PlaceableObject** (price, XP). Vanilla prices them by tier:
size and "premium-ness" drive everything, and the other stats follow attractiveness almost linearly. A Placemaker
build is not a building: its one selectable thing is an invisible **lot area** ("Placemaker Area"), which carries none
of these. Placemaker now *estimates* the stats a vanilla park of the same size and content would have, lets the player
tune them in Review, saves them with the build, and applies the one that works without a building —
**attractiveness counts for city tourism**. Leisure, coverage/wellbeing and upkeep are shown and saved, not applied
(they need a real park building; §5).

## 2. What each stat does (formulas as implemented)

Notation: `a` = prefab attractiveness, `m` = park maintenance (0…pool), `eff` = building efficiency (budget etc.).

### 2.1 Attractiveness — `Attraction.m_Attractiveness` → `AttractionData` (prefab)
* Only buildings without `ServiceUpgrade` get the runtime `AttractivenessProvider` (`Attraction.GetArchetypeComponents`).
* **Per building** (`Game.Simulation.AttractionSystem.AttractivenessJob`, every 16 frames, only entities with an
  `UpdateFrame`): `value = a (+ installed upgrades) × eff (not for signature buildings) × (0.8 + 0.2 × m / pool)
  (parks: an unmaintained park keeps 80 %) × (1 + 0.01 × terrain)`, with `terrain = forest + shore + height`
  (`TerrainAttractivenessSystem.EvaluateAttractiveness`): forest 10 % × forest bonus, shore 20 % × shore bonus,
  height `min(10 %, max(0, h − 50 m) × 0.02 %/m)` (`AttractivenessParameters` prefab: ForestEffect 10, ForestDistance
  250, ShoreEffect 20, ShoreDistance 500, HeightBonus (50, 0.02, 10)). A park on a forested shore gets up to **+40 %**.
  The info panel (`AttractivenessSection`) shows the same factors (its maintenance step is coarser: 0.25 … 1).
* **City tourism** (`TourismSystem`): `raw = Σ over every AttractivenessProvider (not Temp/Deleted) of a² / 10 000`;
  `city attractiveness = 200 / (1 + e^(−0.3 × raw)) − 100` (+ city modifiers), 0 … 100. Target tourists
  `= 15 × A` up to A = 100, then `1500 + 100 × log10(1 + A − 100)`; weather and temperature scale the spawn rate
  (×0.5 … 1.5). Because of the square, one great park beats many small ones: a = 10 adds 0.01, a = 100 adds 1.0.
* **Land value** (`ResourceAvailabilitySystem` → `AvailabilityInfoToGridSystem` → `LandValueSystem`): every provider
  is a pathfinding source over the road network; the attractiveness reaching a cell adds
  `min((x − 5) × 5, max bonus)` (LandValue prefab `AttractivenessBonusMultiplier` 5). Terrain attractiveness itself
  also raises land value of open cells the same way.
* **Tourists choose destinations** by attractiveness (`CitizenPathfindSetup`, Buildings only;
  `ResidentAISystem` sums it for sightseeing).

### 2.2 Leisure — `LeisureProvider.m_Efficiency`, `m_LeisureType`
* Citizens with a low leisure counter pick a leisure type by age, weather and temperature (`LeisureSystem`; CityPark
  weight `2 × (1 − 0.95 × bad weather)`, CityBeach mostly when warm and dry) and pathfind to a provider of that type.
* Each update at the provider: `leisure counter += ceil(efficiency / 5)` until ≈ 255 (`LeisureSystem`,
  `kUpdateInterval = 5`); the counter feeds wellbeing in `CitizenHappinessSystem` (`(counter − 128) / 16`). Every
  vanilla park is **CityPark**; pools and the water park are **CityBeach**; landmarks with interiors are CityIndoors.

### 2.3 Park coverage — `ServiceCoverage` (`m_Range`, `m_Capacity`, `m_Magnitude`), service `Park`
* `ServiceCoverageSystem` pathfinds **on foot** from the building up to `range` (path cost, ≈ metres walked).
  Per street edge: `coverage = max(0, 1 − (cost / range)²) × magnitude × eff`; each street keeps the best provider.
  Capacity is spent by every covered edge (`length × √density`); as it runs out the coverage fades
  (`1 − (0.99 × used)^8`).
* Maintenance (`ParkAISystem.GetModifiedServiceCoverage`): `k = floor(m / pool / 0.3)` (0…3);
  magnitude `× (0.95 + 0.05 × min(1, k) + 0.1 × max(0, k − 1))` (0.95 … 1.2), range `× (0.95 + 0.05 k)` (0.95 … 1.1).
* **Wellbeing** (`CitizenHappinessSystem.GetEntertainmentBonuses`): `20 × min(1, √(coverage / 1.5))`
  (`EntertainmentWellbeingMultiplier` 20): park coverage is worth up to **+20 wellbeing** at home.
  Parks have no `LocalEffects`: in vanilla only signature buildings and school/university park upgrades carry local
  Wellbeing modifiers (+1 … +10 within 200–2,000 m).

### 2.4 Maintenance pool — `Park.m_MaintenancePool`, `m_AllowHomeless`
* `ParkAISystem`: maintenance drops by `(400 + 50 × homeless renters) / 256` per update (≈ 400 + 50 h a day); a park
  maintenance vehicle is requested while `pool − m − pool/10 > 0`. Pools: 250 (small) … 2,000 (sports fields).
* `m_AllowHomeless` (false on premium plazas and landmarks): homeless may rent the park and drain maintenance faster.

### 2.5 Upkeep, price, XP
* `ServiceConsumption.m_Upkeep` becomes a Money `ServiceUpkeepData` (`CityServiceBuilding`), charged per day
  (= the "per month" of the UI) by `CityServiceUpkeepSystem`, scaled by the service budget. Vanilla parks charge
  **exactly a fifth of their price per month** (Small Park ¢10,000 → ¢2,000/mo, Winding Park ¢70,000 → ¢14,000);
  sports fields ≈ 0.1–0.18.
* `PlaceableObject.m_ConstructionCost` is the price; `m_XPReward` is paid once when built (`XPBuiltSystem`).
* No vanilla park has a `Pollution` or a `CityEffects` component.

### 2.6 Signature / unique
`SignatureBuilding` (zoned signatures) skip the efficiency factor; landmarks are `UniqueObject` (one per city) with
attractiveness 50–175. They are listed for reference and never used for Placemaker's range.

## 3. Vanilla values (52 prefabs read from the game)

Lot = cells (8 m). Coverage = range / capacity / magnitude. Ratios: attractiveness per 1,000 m², upkeep and price
per cell. ¹ free update content (`Prefabs_FreeUpdate02`), ² Modern Architecture pack.

| Park | Kind | Lot | Price ¢ | Upkeep ¢/mo | Attr. | Leisure | Coverage | Pool | XP | Attr/1000 m² | Upkeep/cell | Price/cell |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| Tiny Park | Pocket | 1×1 (64 m²) | 1,500 | 400 | 1 | 1  | 500 / 200 / 8 | 250 | 10 | 15.62 | 400 | 1,500 |
| Mini Park | Pocket | 1×1 (64 m²) | 1,500 | 400 | 1 | 1  | 500 / 200 / 8 | 250 | 10 | 15.62 | 400 | 1,500 |
| Urban Parkette | Pocket | 2×1 (128 m²) | 2,500 | 600 | 1 | 1  | 1,000 / 400 / 8 | 250 | 10 | 7.81 | 300 | 1,250 |
| Canopy-Covered Parkette | Pocket | 2×1 (128 m²) | 2,500 | 600 | 1 | 1  | 1,000 / 400 / 8 | 250 | 10 | 7.81 | 300 | 1,250 |
| Urban Pocket Park | Pocket | 2×2 (256 m²) | 5,000 | 1,000 | 1 | 1  | 2,000 / 600 / 10 | 250 | 50 | 3.91 | 250 | 1,250 |
| Square Pocket Park | Pocket | 2×2 (256 m²) | 5,000 | 1,000 | 1 | 1  | 2,000 / 600 / 10 | 250 | 50 | 3.91 | 250 | 1,250 |
| Canopy-Covered Pocket Park | Pocket | 4×1 (256 m²) | 5,000 | 1,000 | 1 | 1  | 2,000 / 600 / 10 | 250 | 50 | 3.91 | 250 | 1,250 |
| Wide Pocket Park | Pocket | 6×1 (384 m²) | 7,500 | 1,500 | 1 | 1  | 2,250 / 700 / 10 | 250 | 50 | 2.60 | 250 | 1,250 |
| Modern Pocket Park | Pocket | 1×4 (256 m²) | 5,000 | 1,000 | 1 | 1  | 2,000 / 600 / 10 | 250 | 50 | 3.91 | 250 | 1,250 |
| Canopy-Covered Corridor Park | Pocket | 1×6 (384 m²) | 7,500 | 1,500 | 1 | 1  | 2,250 / 700 / 10 | 250 | 50 | 2.60 | 250 | 1,250 |
| Tiny City Park | Park | 2×3 (384 m²) | 7,500 | 1,500 | 1 | 1  | 2,250 / 700 / 10 | 250 | 50 | 2.60 | 250 | 1,250 |
| Small Park | Park | 4×4 (1,024 m²) | 10,000 | 2,000 | 1 | 1  | 2,500 / 800 / 10 | 250 | 50 | 0.98 | 125 | 625 |
| Bicycle Park | Park | 4×4 (1,024 m²) | 10,000 | 2,000 | 1 | 1  | 2,500 / 800 / 10 | 250 | 50 | 0.98 | 125 | 625 |
| Dog Park | Park | 4×3 (768 m²) | 10,000 | 2,000 | 1 | 2  | 3,500 / 800 / 10 | 500 | 50 | 1.30 | 167 | 833 |
| Ring Park | Park | 5×5 (1,600 m²) | 15,000 | 3,000 | 3 | 2  | 3,200 / 1,000 / 10 | 250 | 50 | 1.88 | 120 | 600 |
| Passage Park | Park | 4×12 (3,072 m²) | 16,800 | 3,360 | 4 | 2  | 2,000 / 1,000 / 10 | 400 | 50 | 1.30 | 70 | 350 |
| Hedge Park | Park | 8×8 (4,096 m²) | 33,600 | 6,750 | 8 | 3  | 2,400 / 1,250 / 12 | 400 | 100 | 1.95 | 105 | 525 |
| Large City Park | Park | 10×8 (5,120 m²) | 42,000 | 8,500 | 10 | 3  | 3,000 / 1,600 / 14 | 500 | 100 | 1.95 | 106 | 525 |
| Multipurpose Park | Park | 8×16 (8,192 m²) | 50,000 | 10,000 | 13 | 5  | 3,600 / 1,800 / 12 | 450 | 150 | 1.59 | 78 | 391 |
| Winding Park | Park | 12×12 (9,216 m²) | 70,000 | 14,000 | 20 | 7  | 5,000 / 2,500 / 14 | 625 | 150 | 2.17 | 97 | 486 |
| Park Cafe ¹ | Park | 6×6 (2,304 m²) | 20,000 | 4,000 | 5 | 2  | 4,000 / 1,200 / 10 | 250 | 50 | 2.17 | 111 | 556 |
| Dog Activity Park ¹ | Park | 10×6 (3,840 m²) | 42,000 | 8,500 | 10 | 3  | 3,000 / 1,600 / 14 | 500 | 100 | 2.60 | 142 | 700 |
| Community City Park ² | Park | 15×15 (14,400 m²) | 63,000 | 15,000 | 18 | 6  | 4,000 / 2,000 / 15 | 600 | 180 | 1.25 | 67 | 280 |
| Small Plaza | Plaza | 6×6 (2,304 m²) | 20,000 | 4,000 | 5 | 2  | 4,000 / 1,200 / 10 | 250 | 50 | 2.17 | 111 | 556 |
| Terrace Plaza | Plaza | 6×8 (3,072 m²) | 70,000 | 14,000 | 20 | 7  | 5,000 / 2,500 / 14 | 625 | 150 | 6.51 | 292 | 1,458 |
| Business Plaza | Plaza | 8×10 (5,120 m²) | 90,000 | 18,000 | 25 | 9  | 6,000 / 3,000 / 16 | 750 | 150 | 4.88 | 225 | 1,125 |
| Large Plaza | Plaza | 12×16 (12,288 m²) | 56,000 | 12,500 | 15 | 5  | 4,000 / 2,000 / 14 | 500 | 150 | 1.22 | 65 | 292 |
| Grand City Plaza ² | Plaza | 28×20 (35,840 m²) | 128,000 | 30,000 | 99 | 66  | 5,000 / 3,500 / 14 | 1000 | 240 | 2.76 | 54 | 229 |
| Small Playground | Playground | 2×2 (256 m²) | 5,000 | 1,000 | 1 | 1  | 2,000 / 600 / 10 | 500 | 50 | 3.91 | 250 | 1,250 |
| Large Playground | Playground | 3×4 (768 m²) | 14,000 | 2,500 | 5 | 5  | 2,500 / 800 / 14 | 500 | 50 | 6.51 | 208 | 1,167 |
| Activity Playground ¹ | Playground | 3×3 (576 m²) | 10,000 | 2,000 | 1 | 1  | 2,500 / 800 / 10 | 1000 | 50 | 1.74 | 222 | 1,111 |
| Play Round ¹ | Playground | 3×3 (576 m²) | 10,000 | 2,000 | 1 | 1  | 2,500 / 800 / 10 | 250 | 50 | 1.74 | 222 | 1,111 |
| Hilly Playground ¹ | Playground | 4×4 (1,024 m²) | 10,000 | 2,000 | 1 | 1  | 2,500 / 800 / 10 | 250 | 50 | 0.98 | 125 | 625 |
| Chirplet Park ¹ | Playground | 2×2 (256 m²) | 5,000 | 1,000 | 1 | 1  | 2,500 / 600 / 10 | 500 | 50 | 3.91 | 250 | 1,250 |
| Chirper Park ¹ | Playground | 4×4 (1,024 m²) | 10,000 | 2,000 | 1 | 1  | 2,500 / 800 / 10 | 250 | 50 | 0.98 | 125 | 625 |
| Chirper Adventure Park ¹ | Playground | 6×8 (3,072 m²) | 33,600 | 6,750 | 8 | 3  | 2,400 / 1,250 / 12 | 400 | 100 | 2.60 | 141 | 700 |
| Chirper Fun Time Complex ¹ | Playground | 8×10 (5,120 m²) | 42,000 | 8,500 | 10 | 3  | 3,000 / 1,600 / 14 | 500 | 100 | 1.95 | 106 | 525 |
| Outdoor Gym | Sports | 2×2 (256 m²) | 15,000 | 3,000 | 5 | 5  | 3,500 / 1,000 / 10 | 1000 | 50 | 19.53 | 750 | 3,750 |
| Basketball Court | Sports | 3×4 (768 m²) | 14,000 | 2,500 | 5 | 5  | 3,500 / 800 / 14 | 1000 | 50 | 6.51 | 208 | 1,167 |
| Tennis Court | Sports | 4×7 (1,792 m²) | 14,000 | 2,500 | 5 | 5  | 3,500 / 800 / 14 | 1000 | 50 | 2.79 | 89 | 500 |
| Skate Park | Sports | 8×4 (2,048 m²) | 15,000 | 3,000 | 5 | 5  | 3,500 / 1,000 / 10 | 1000 | 50 | 2.44 | 94 | 469 |
| Community Pool | Sports | 7×7 (3,136 m²) | 80,000 | 8,000 | 15 | 20 beach | 3,500 / 2,000 / 16 | 2000 | 150 | 4.78 | 163 | 1,633 |
| Basketball Center ¹ | Sports | 8×6 (3,072 m²) | 80,000 | 8,000 | 15 | 20  | 3,500 / 2,000 / 16 | 2000 | 150 | 4.88 | 167 | 1,667 |
| Soccer Field | Sports | 12×15 (11,520 m²) | 160,000 | 15,000 | 25 | 20  | 4,000 / 4,000 / 16 | 2000 | 300 | 2.17 | 83 | 889 |
| American Football Field | Sports | 20×12 (15,360 m²) | 160,000 | 15,000 | 25 | 20  | 4,000 / 4,000 / 16 | 2000 | 300 | 1.63 | 62 | 667 |
| Campfire Site | Nature | 8×14 (7,168 m²) | 30,000 | 6,000 | 10 | 3  | 6,000 / 1,600 / 10 | 500 | 100 | 1.40 | 54 | 268 |
| Large Campfire Site | Nature | 11×10 (7,040 m²) | 30,000 | 6,000 | 10 | 3  | 6,000 / 1,600 / 10 | 500 | 100 | 1.42 | 55 | 273 |
| Fountain Plaza | Unique | 14×14 (12,544 m²) | 1,600,000 | 80,000 | 100 | 50  | 7,500 / 10,000 / 16 | 4000 | 2000 | 7.97 | 408 | 8,163 |
| Botanical Garden | Unique | 12×16 (12,288 m²) | 1,600,000 | 80,000 | 125 | 100  | 10,000 / 15,000 / 20 | 4000 | 2000 | 10.17 | 417 | 8,333 |
| Water Park | Unique | 12×6 (4,608 m²) | 800,000 | 40,000 | 75 | 25 beach | 5,000 / 3,000 / 16 | 3000 | 1000 | 16.28 | 556 | 11,111 |
| Bronze Statue | Unique | 9×9 (5,184 m²) | 3,200,000 | 160,000 | 50 | 25  | 5,000 / 5,000 / 16 | 3000 | 4000 | 9.65 | 1,975 | 39,506 |
| Notre-Dame | Unique | 10×25 (16,000 m²) | 5,600,000 | 280,000 | 175 | 125  | 7,500 / 20,000 / 20 | 3500 | 7000 | 10.94 | 1,120 | 22,400 |

Not in the table (same values as a row above, or not standalone): Bicycle Park = Small Park; park lighthouses
(attr 1–10), leisure pier heads (attr 1–3, coverage 1000/500/10), food trucks (attr 5, Meals), amusement rides
(carousels, Ferris wheels: attr 10–20) and the upgrades (school playgrounds, university park, cemetery mausoleum).

**Patterns (the bands Placemaker keeps to):**
* Attractiveness follows size: pocket parks are 1; ordinary parks ≈ **1.9 × (area / 1000 m²)^0.85**
  (Small Park 1 → Community City Park 18 at 1.4 ha). Premium plazas run 2–5× that per area (Terrace Plaza 20 on
  0.3 ha, Business Plaza 25 on 0.5 ha). Top of the regular range: Grand City Plaza 99 (3.6 ha). Landmarks 50–175.
* Leisure ≈ attractiveness / 3 for parks (1, 2, 3, 5, 7, 9) and ≈ attractiveness for sports (5/5, 25/20).
* Capacity ≈ 700 + 90 × attractiveness (800 … 3,000; sports fields 4,000; Grand City Plaza 3,500).
* Range 2,000 … 6,000 for buildings of 4+ cells, 500 … 2,250 for pocket parks.
* Magnitude 8 (one-cell parks) … 10 (small) … 14 (large) … 16 (premium / sports).
* Upkeep ≈ price / 5 ≈ 1,500 + 650 × attractiveness; ≈ 400–600 ¢ per cell for ordinary parks.

## 4. The estimator (Core/Build/ParkStats.cs)

Input (`ParkStatsInput.From(plan, area, recipe)`): area m²; kind from the first fill layer (plaza, squares,
transport → **Plaza**; sports → **Sports**; adventure playground → **Playground**; nature, campground → **Nature**;
other leisure types → **Park**; industrial, parking, roads and loose builds → *not a park: no stats*); trees,
greenery, benches/picnic/cafe seating, centerpieces (fountains, statues, lookouts, water jets), amenities (restrooms,
kiosks, shelters, stages, camping), play and sports items; water m² (water surfaces + lakes) and paved m².

```
quality q = 0.55 + 0.35·trees + 0.30·water + 0.25·landmarks + 0.20·amenities + 0.20·activity + 0.15·seating
   trees     = sat((trees + 0.3·greenery) / 50 per ha)          (plazas: or paved share / 60 %)
   water     = sat(water share / 15 %)        landmarks = sat(centerpieces / 1 per ha)
   amenities = sat(amenities / max(2, 4 per ha))
   activity  = sat(play + sports items / max(3, 10 per ha))     (sports, playground: at least 0.7)
   seating   = sat(benches / max(4, 15 per ha))
attractiveness a = clamp(round(1.9 · (A/1000)^0.85 · q · type), 1, 100)   type: plaza 1.25, nature 0.85, else 1
leisure          = max(1, round(a · (0.33 + 0.67 · activity)))            (beach leisure for water types)
range            = round50(min(6000, 2200 + 150 a) · small)               small = A < 1000 m² ? max(0.3, √(A/1000)) : 1
capacity         = round50((700 + 90 a) · small)
entertainment    = A < 200 m² ? 8 : clamp(9 + 0.28 a (+2 sports), 8, 16)
upkeep           = round50((1500 + 650 a) · (A < 1000 ? max(0.2, A/1000) : 1))
```
An ordinary park scores q ≈ 1 (40 trees and 12 benches per ha, one amenity). q never decreases when trees, water or
features are added, and a bigger park is never less attractive (unit tests).

**Vanilla range** (`ParkStatsEstimator.Band`): the regular vanilla parks within ×3 of the area (else the 4 nearest),
each value scaled to this area by `(A / A_vanilla)^e` (e = 0.85 attractiveness/leisure/upkeep, 0.5 capacity,
0.25 range, 0 entertainment); min…max, widened ±15 %, attractiveness capped at 100. The suggested value is always
inside it; "Keep within vanilla range" (default on) clamps the player's values to it too. Off allows up to hard
limits (attractiveness 500, leisure 300, …) with a gentle warning.

Headline (unit tests): a **1 ha ordinary park → attractiveness 14** (vanilla neighbours Multipurpose Park 13, Large
Plaza 15, Community City Park 18, Winding Park 20), leisure 6, range 4,300, capacity 1,950, entertainment 13, upkeep
¢10,600/mo. At vanilla sizes: Small Park 2 (vanilla 1), Hedge Park 7 (8), Large City Park 9 (10), Multipurpose Park 12 (13), Winding Park 13 (20),
Community City Park 19 (18) — all within ×2 of the real park. Beyond the largest vanilla park (3.6 ha) the curve is
extrapolated: 5 ha ≈ 53, 10 ha ≈ 96 (capped at 100, Grand City Plaza level).

## 5. The owner lot, and giving a build real park stats

**Today** the owner lot is a runtime `LotPrefab` "Placemaker Area" (`OwnerAreaPrefabSystem`): Lot type (raycast by
the default tool and the bulldozer), transparent colours, `m_AllowOverlap`. Archetype: Area, Node, Triangle, Lot,
Geometry, SubObject, PseudoRandomSeed (+ our tags). None of the park components, no Transform, no UpdateFrame,
no Building.

**Which systems read what:**

| Stat | Read from | Needs | On the lot? |
|---|---|---|---|
| City tourism | entity `AttractivenessProvider` (any entity) | nothing else | **yes** |
| AttractionSystem recompute | prefab `AttractionData`, entity `UpdateFrame` | building archetype | skipped (no UpdateFrame), keeps our value |
| Land value from attractiveness | `ResourceAvailabilitySystem` → pathfinding targets of the entity | spawn locations / sub lanes / road edge | no (0 targets, ignored) |
| Tourist destinations | `CitizenPathfindSetup` | `Building` | no |
| Leisure | `LeisureProvider` + pathfinding to the building | Building + road connection | no |
| Coverage / wellbeing | prefab `CoverageData`, pathfinding from the building | Building, `ParkAISystem` | no |
| Upkeep | `ServiceUpkeepData` via `CityServiceUpkeepSystem` | CityServiceBuilding | no |

**Implemented (s17):** after Apply, `BuildService.FinishParkStats` stores the stats in the receipt (v2) and adds a
vanilla `AttractivenessProvider { attractiveness × (1 + terrain %) }` to the owner lot (terrain bonus sampled once at
the outline's centre; maintenance taken as full). Setting **"Park attractiveness counts for tourism"** (default on).
`GroupRegistrySystem.SyncParkStats` re-adds it after a load if the lot lost it, and removes it when the setting is off.
Attractiveness also shows in the build's info-panel stats. No Harmony, no new component types, every step in try/catch.
Builds without an owner lot (shape rejected) keep the stats in the receipt only.

**Save compatibility:** `AttractivenessProvider` is a vanilla, serialised component, so saves load with or without
the mod. Without Placemaker the owner lot stays (obsolete prefab, RESEARCH-NOTES 40) and keeps counting for tourism;
to drop it, switch the setting off and load once (or bulldoze the build) before uninstalling. Removing the build with
the mod removes lot and value. Builds made before s17 have no stats (receipt v1 still decodes; older Placemaker
versions ignore the appended v2 block).

**Full park stats (recommended next step, not done): a companion park building.** Leisure, coverage/wellbeing, land
value and real upkeep all key off a `Building` with a road connection. The robust route is the one the parking
companion already uses (`Economy/ParkingCompanionSystem`): per build, place one small, unobtrusive service building
cloned at runtime from a vanilla park prefab (Tiny Park / Small Park: `ParkData`, `CoverageData`,
`LeisureProviderData`, `AttractionData`, `ConsumptionData`) at the best road frontage, with the prefab components set
from the build's stats (then attractiveness moves from the lot to it). Per-build values need **per-build prefabs**
(AttractionSystem, ServiceCoverageSystem and ParkAISystem read the *prefab*): a pool of quantised clones
("Placemaker Park a14 l6 …") registered at startup from the receipts, before `PrefabInitializeSystem`
(RESEARCH-NOTES 1–3). Risks: (a) saves reference the clone by name — without the mod it becomes an obsolete-prefab
building; `ParkAISystem`/`AttractionSystem` check `HasComponent` on the prefab so it should load, but needs a test;
(b) it requests park maintenance vehicles and accepts homeless (set `m_AllowHomeless = false`); (c) it must stand on a
road edge without colliding with our objects, which some drawn areas cannot offer; (d) district policies and
service upgrades apply to it; (e) mesh-less buildings crash (RESEARCH-NOTES 16) — reuse a tiny vanilla mesh.
Adding park components to the lot itself does not work: there is no pathfinding source, and systems iterating
`Game.Buildings.Park` expect `Renter`, `Efficiency` and `UpdateFrame` buffers of a building.

## 6. How the values were read

The base game's prefabs are Unity `MonoBehaviour`/`ScriptableObject`s in `Cities2_Data/resources.assets` (type trees
stripped). A small Python reader (scratch, not in the repo) parsed the SerializedFile object table, mapped each
MonoBehaviour to its script class through the `MonoScript`s in `globalgamemanagers.assets`, walked every
`BuildingPrefab`'s `components` list and read the fields in the declaration order of the decompiled classes
(ComponentBase `active`; `PrefabBase.version`/`m_prefabFormat` each aligned to 4 bytes). DLC and free-update prefabs
are tagged `.Prefab` files inside the `.cok` zips (`Content/Game/Prefabs_FreeUpdate02.cok`, `Content/<pack>/Prefabs.cok`)
that store field names next to typed values. Names come from `Locale.cok/en-US.loc` (`Assets.NAME[<id>]`). The values
are internally consistent (upkeep = price / 5 for every ordinary park); they were not cross-checked in game this round. If a patch
changes them, re-run the reader and regenerate `Core/Build/VanillaParks.cs`; an in-game dump button is not needed.
