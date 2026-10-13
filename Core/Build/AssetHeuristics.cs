// Placemaker — name/trait heuristics for asset classification (GPL-3.0).
// Adapted from ParkManager's ParkAssetCatalogSystem (tomkpunkt, GPL-3.0). Gameplay components
// decide first (the game layer passes them in as AssetTraits); names are only the fallback,
// because custom assets name things in every language and convention.

using System;
using System.Collections.Generic;

namespace Placemaker.Core.Build
{
    [Flags]
    public enum AssetTraits
    {
        None = 0,
        StaticObject = 1 << 0,
        Building = 1 << 1,
        Plant = 1 << 2,
        Tree = 1 << 3,
        BenchActivity = 1 << 4,
        StreetLight = 1 << 5,
        NetFence = 1 << 6,
        Placeholder = 1 << 7,
        HasMesh = 1 << 8,
        HasSubLane = 1 << 9,           // object with sub lanes (parking decal = real stall)
        ParkingLaneSubLane = 1 << 10,  // one of the sub lanes is a parking lane
        HasIcon = 1 << 11,
        HasSubObjects = 1 << 12,
        Surface = 1 << 13,             // area prefab of type Surface
        SpaceArea = 1 << 14,           // area prefab of type Space
        LotArea = 1 << 15,
        Road = 1 << 16,
        Pathway = 1 << 17,
        Zone = 1 << 18,
        Charger = 1 << 19,             // EV charger (ChargingStation-like data)
        Marker = 1 << 20,              // marker object (invisible in game)
        Net = 1 << 21,
    }

    public static class AssetHeuristics
    {
        private static readonly string[] PlanterNames = { "planter", "flowerpot", "flower pot", "plantbox", "plant box", "pot0", "potted" };

        public static bool ContainsAny(string value, params string[] parts)
        {
            foreach (var p in parts) if (value.IndexOf(p, StringComparison.Ordinal) >= 0) return true;
            return false;
        }

        /// <summary>
        /// Every category this prefab belongs to (an asset can be several, e.g. a bush is also a
        /// planter candidate). Empty list = not useful to Placemaker.
        /// </summary>
        public static List<string> Classify(string name, AssetTraits t)
        {
            var r = new List<string>(2);
            if (string.IsNullOrWhiteSpace(name)) return r;
            var lower = name.ToLowerInvariant();
            if ((t & AssetTraits.Placeholder) != 0 || lower.Contains("placeholder")) return r;

            if ((t & AssetTraits.Zone) != 0) { r.Add(AssetCategories.Zone); return r; }

            if ((t & AssetTraits.Surface) != 0)
            {
                r.Add(AssetCategories.SurfaceAny);
                var s = SurfaceKind(lower);
                if (s != null) r.Add(s);
                return r;
            }
            if ((t & AssetTraits.SpaceArea) != 0)
            {
                if (ContainsAny(lower, "hangaround", "hang around")) r.Add(BuildCategories.AreaHangaround);
                else r.Add(BuildCategories.AreaPedestrian);
                return r;
            }
            if ((t & AssetTraits.LotArea) != 0) { r.Add(BuildCategories.AreaLot); return r; }

            if ((t & AssetTraits.Net) != 0)
            {
                if ((t & AssetTraits.NetFence) != 0) { r.Add(AssetCategories.Fence); return r; }
                if ((t & AssetTraits.Pathway) != 0)
                {
                    if (ContainsAny(lower, "invisible", "marker")) r.Add(AssetCategories.InvisiblePath);
                    else r.Add(AssetCategories.Pathway);
                    return r;
                }
                if ((t & AssetTraits.Road) != 0)
                {
                    if (ContainsAny(lower, "invisible")) r.Add(AssetCategories.InvisiblePath);
                    else r.Add(AssetCategories.Road);
                }
                return r;
            }

            if ((t & AssetTraits.StaticObject) == 0 || (t & AssetTraits.Building) != 0) return r;

            // Parking decals: a static object whose sub lane is a parking lane spawns a working stall.
            if ((t & AssetTraits.ParkingLaneSubLane) != 0)
            {
                r.Add(AssetCategories.ParkingDecal);
                return r;
            }
            if ((t & AssetTraits.Charger) != 0 || (lower.Contains("charger") && lower.Contains("parking")))
            {
                r.Add(AssetCategories.Charger);
                return r;
            }

            // Name-identified special objects used by roads (mesh-less markers are allowed here).
            if (lower.StartsWith("culdesac", StringComparison.Ordinal)) { r.Add(BuildCategories.CulDeSacCap); return r; }
            if (lower.Contains("roundabout")) { r.Add(BuildCategories.RoundaboutIsland); return r; }
            if (ContainsAny(lower, "animal spawner", "animalspawner", "wildlife spawn")) { r.Add(BuildCategories.AnimalSpawner); return r; }

            // Everything below must be visible: mesh-less static objects crash when spawned mid-game.
            if ((t & AssetTraits.HasMesh) == 0) return r;
            if (ContainsAny(lower, "random", "source", "effect", "particle")) return r;

            if ((t & AssetTraits.Plant) != 0)
            {
                if (ContainsAny(lower, PlanterNames)) { r.Add(AssetCategories.Planter); return r; }
                if (ContainsAny(lower, "flowerbed", "flower bed")) { r.Add(AssetCategories.Flowerbed); return r; }
                if ((t & AssetTraits.Tree) != 0)
                {
                    if (ContainsAny(lower, "stump", "dead", "fallen", "log")) return r;
                    r.Add(AssetCategories.Tree);
                }
                else { r.Add(AssetCategories.Bush); r.Add(AssetCategories.Planter); }
                return r;
            }
            if ((t & AssetTraits.BenchActivity) != 0 || IsBenchName(lower)) { r.Add(AssetCategories.Bench); return r; }
            if ((t & AssetTraits.StreetLight) != 0 || ContainsAny(lower, "lamp", "lantern", "streetlight", "street light"))
            {
                r.Add(AssetCategories.Lamp);
                return r;
            }
            if (ContainsAny(lower, "fence", "railing", "hedge", "barrier")) { r.Add(AssetCategories.Fence); return r; }
            if (ContainsAny(lower, "bollard")) { r.Add(AssetCategories.Bollard); return r; }
            if (ContainsAny(lower, "trashbin", "trash bin", "trashcan", "trash can", "wastebin", "waste bin",
                "garbagebin", "garbage bin", "litterbin", "litter bin", "recycling bin")) { r.Add(AssetCategories.Bin); return r; }
            if (ContainsAny(lower, PlanterNames)) { r.Add(AssetCategories.Planter); return r; }
            if (ContainsAny(lower, "flowerbed", "flower bed")) { r.Add(AssetCategories.Flowerbed); return r; }
            if (ContainsAny(lower, "playground", "swing", "slide", "seesaw", "sandbox", "climbing", "carousel", "spring rider"))
            { r.Add(AssetCategories.Playground); return r; }
            if (ContainsAny(lower, "basketball", "tennis", "football", "soccer", "volleyball", "fitness", "goal", "court", "skate", "pingpong", "table tennis", "outdoor gym"))
            { r.Add(AssetCategories.Sports); return r; }
            if (ContainsAny(lower, "toilet", "restroom", "wc", "portapotty", "porta potty")) { r.Add(AssetCategories.Restroom); return r; }
            if (ContainsAny(lower, "picnic", "bbq", "barbecue", "grill")) { r.Add(AssetCategories.Picnic); return r; }
            if (ContainsAny(lower, "tent", "campfire", "camp fire", "caravan", "camping")) { r.Add(AssetCategories.Camping); return r; }
            if (ContainsAny(lower, "kiosk", "food stand", "foodstand", "ice cream", "icecream", "hotdog", "vendor", "stall", "foodtruck", "food truck"))
            { r.Add(AssetCategories.Kiosk); return r; }
            if (ContainsAny(lower, "pavilion", "gazebo", "shelter", "pergola", "canopy", "bandstand")) { r.Add(AssetCategories.Shelter); return r; }
            if ((t & AssetTraits.HasSubObjects) == 0 && (t & AssetTraits.HasIcon) != 0
                && ContainsAny(lower, "fountain", "statue", "monument", "sculpture", "obelisk"))
            { r.Add(AssetCategories.Centerpiece); return r; }
            if (ContainsAny(lower, "sign", "poster", "billboard", "advert", "decal", "shadow", "broken", "ruin", "graffiti")) return r;
            if ((t & AssetTraits.HasIcon) != 0 && ContainsAny(lower, "park", "plaza", "garden", "decor", "flag", "bird", "rock", "stone", "boulder"))
                r.Add(AssetCategories.Decoration);
            return r;
        }

        public static string SurfaceKind(string lower)
        {
            if (ContainsAny(lower, "grass", "lawn", "meadow", "turf")) return AssetCategories.SurfaceGrass;
            if (ContainsAny(lower, "asphalt", "tarmac", "parking")) return AssetCategories.SurfaceAsphalt;
            if (ContainsAny(lower, "pavement", "paving", "tile", "cobble", "brick", "concrete", "plaza", "stone")) return AssetCategories.SurfacePaving;
            if (ContainsAny(lower, "sand", "gravel", "dirt", "soil", "forest", "mud", "rock", "clay", "earth")) return AssetCategories.SurfaceNatural;
            return null;
        }

        public static bool IsBenchName(string lower)
        {
            // "bank" alone also matches advertised financial brands (PM measured this).
            if (!ContainsAny(lower, "bench", "seat", "parkbank", "gardenbank", "sitzbank", "streetbank")) return false;
            return !ContainsAny(lower, "advert", "billboard", "poster", "banner", "display", "sign", "adstand",
                "commercial", "logo", "screen", "adboard", "car seat", "toilet");
        }

        /// <summary>Score for picking the automatic default of a category (higher = better).</summary>
        public static int DefaultScore(string category, string name, float netWidth = 0f)
        {
            if (string.IsNullOrEmpty(name)) return int.MinValue;
            var lower = name.ToLowerInvariant();
            int score = 0;
            if (ContainsAny(lower, "placeholder")) score -= 500;
            switch (category)
            {
                case AssetCategories.SurfaceAny:
                case AssetCategories.SurfaceGrass:
                    if (lower.Contains("grass")) score += 100;
                    if (lower.Contains("01")) score += 10;
                    if (ContainsAny(lower, "pavement", "sand")) score -= 30;
                    break;
                case AssetCategories.SurfacePaving:
                    if (lower.Contains("pavement")) score += 100;
                    if (lower.Contains("01")) score += 10;
                    break;
                case AssetCategories.SurfaceAsphalt:
                    if (lower.Contains("asphalt")) score += 100;
                    if (lower.Contains("01")) score += 10;
                    break;
                case AssetCategories.SurfaceNatural:
                    if (lower.Contains("gravel")) score += 60;
                    if (lower.Contains("sand")) score += 40;
                    break;
                case AssetCategories.Tree:
                    if (ContainsAny(lower, "deciduous", "oak", "linden", "maple")) score += 40;
                    break;
                case AssetCategories.Bush:
                    if (lower.Contains("bush")) score += 50;
                    break;
                case AssetCategories.Bench:
                    if (lower.Contains("bench")) score += 50;
                    if (lower.Contains("park")) score += 30;
                    break;
                case AssetCategories.Lamp:
                    if (lower.Contains("park")) score += 30;
                    if (lower.Contains("pathway") || lower.Contains("pedestrian")) score += 20;
                    break;
                case AssetCategories.Fence:
                    if (lower.Contains("fence")) score += 50;
                    if (lower.Contains("residentialpiecelow")) score += 250;
                    else if (lower.Contains("piece")) score += 100;
                    break;
                case AssetCategories.Bin:
                    if (ContainsAny(lower, "trash", "waste")) score += 50;
                    if (lower.Contains("park")) score += 25;
                    break;
                case AssetCategories.Centerpiece:
                    if (lower.Contains("fountain")) score += 100;
                    if (lower.Contains("statue")) score += 80;
                    if (ContainsAny(lower, "plaza", "central")) score += 30;
                    break;
                case AssetCategories.Pathway:
                    if (lower.Contains("gravel")) score -= 10;
                    if (lower.StartsWith("pedestrian", StringComparison.Ordinal) || lower.Contains("pathway")) score += 40;
                    score -= (int)(netWidth * 2);   // narrow by default
                    break;
                case AssetCategories.InvisiblePath:
                    // Drivable invisible road paths for parking (PLT used these vanilla names).
                    if (lower.StartsWith("invisible road path", StringComparison.Ordinal)) score += 100;
                    if (lower.StartsWith("invisible car path", StringComparison.Ordinal)) score += 80;
                    if (lower.Contains("1xtwoway")) score += 20;
                    if (lower.Contains("oneway")) score -= 30;
                    break;
                case AssetCategories.Road:
                    if (lower.Contains("small road") || lower.Contains("small")) score += 50;
                    if (lower.Contains("highway") || lower.Contains("highway")) score -= 200;
                    if (ContainsAny(lower, "oneway", "one-way", "tram", "bus", "gravel", "alley")) score -= 40;
                    break;
                case AssetCategories.ParkingDecal:
                    if (lower == "parkinglotdecal01") score += 200;
                    if (ContainsAny(lower, "disabled", "electric", "ev")) score -= 50;
                    break;
                case AssetCategories.Charger:
                    if (lower.Contains("parkinglotcharger")) score += 100;
                    break;
                case BuildCategories.CulDeSacCap:
                    if (lower.Contains("medium")) score += 20;
                    if (lower.EndsWith("02", StringComparison.Ordinal)) score += 10;
                    break;
                case BuildCategories.RoundaboutIsland:
                    if (lower.Contains("medium")) score += 20;
                    break;
            }
            if (ContainsAny(lower, "invisible", "marker") && category != AssetCategories.InvisiblePath) score -= 200;
            return score;
        }

        /// <summary>Palette tag for an asset seen in a vanilla park's sub-objects.</summary>
        public static string PaletteTag(string parkName) => "palette:" + (parkName ?? "");
    }
}
