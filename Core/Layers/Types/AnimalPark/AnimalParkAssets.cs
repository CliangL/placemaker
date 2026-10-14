// Asset vocabulary and classification for the animal park: animal shelters (stables, hutches,
// coops, field shelters), water troughs and feeders, hay, aviaries and show jumps. Real prefab
// names are unknown until the catalog dump exists; the rules match broad vanilla-style words and
// the layer always falls back (shelter → barn → shed → pavilion) or skips with an Info issue.

using System.Collections.Generic;
using Placemaker.Core.Build;

namespace Placemaker.Core
{
    public static partial class AssetCategories
    {
        public const string AnimalParkShelter = "animalpark.shelter";   // stables, hutches, coops, sties, field shelters
        public const string AnimalParkTrough = "animalpark.trough";     // water troughs, feeders, drinkers
        public const string AnimalParkHay = "animalpark.hay";           // hay bales, straw, hay racks
        public const string AnimalParkAviary = "animalpark.aviary";     // aviaries, bird cages, dovecotes
        public const string AnimalParkJump = "animalpark.jump";         // show jumps, cavaletti, horse jump poles
    }
}

namespace Placemaker.Core.Layers.Types.AnimalPark
{
    public sealed class AnimalParkCategoryRules : ICategoryRules
    {
        public int Priority => 41;

        private static bool Has(string s, params string[] parts) => AssetHeuristics.ContainsAny(s, parts);

        private static bool Junk(string lower) =>
            Has(lower, "advert", "billboard", "logo", "placeholder", "signboard", "poster", "banner");

        public bool Classify(string lower, AssetTraits traits, List<string> categories)
        {
            if (Junk(lower)) return false;
            if (Has(lower, "aviary", "birdcage", "bird cage", "dovecote", "dove cote", "birdhouse", "bird house", "pigeonloft", "pigeon loft", "volary"))
            { categories.Add(AssetCategories.AnimalParkAviary); return true; }
            if (Has(lower, "showjump", "show jump", "horsejump", "horse jump", "oxer", "cavaletti", "jumppole", "jump pole", "equestrianjump"))
            { categories.Add(AssetCategories.AnimalParkJump); return true; }
            if (Has(lower, "trough", "drinker", "animalfeeder", "animal feeder", "livestockfeeder", "feedbunk", "feed bunk"))
            { categories.Add(AssetCategories.AnimalParkTrough); return true; }
            if (Has(lower, "haybale", "hay bale", "hayrack", "hay rack", "haystack", "hay stack", "straw", "silagebale", "silage bale") ||
                lower.StartsWith("hay", System.StringComparison.Ordinal))
            { categories.Add(AssetCategories.AnimalParkHay); return true; }
            if (Has(lower, "stable", "hutch", "chickencoop", "chicken coop", "henhouse", "hen house", "pigsty", "pig sty", "pigpen", "pig pen",
                    "fieldshelter", "field shelter", "animalshelter", "animal shelter", "goathouse", "goat house", "loosebox", "loose box") ||
                lower.EndsWith("coop", System.StringComparison.Ordinal))
            { categories.Add(AssetCategories.AnimalParkShelter); return true; }
            return false;
        }

        public bool? AutoEligible(string category, string lower, AssetTraits traits)
        {
            switch (category)
            {
                case AssetCategories.AnimalParkShelter:
                case AssetCategories.AnimalParkTrough:
                case AssetCategories.AnimalParkHay:
                case AssetCategories.AnimalParkAviary:
                case AssetCategories.AnimalParkJump:
                    return !Junk(lower) && !Has(lower, "broken", "burnt", "ruin", "random");
                default:
                    return null;
            }
        }
    }
}
