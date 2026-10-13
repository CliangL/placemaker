namespace Placemaker.Core
{
    /// <summary>
    /// Shared asset-category vocabulary between layers (which ask for "a bench") and the game
    /// catalog (which classifies prefabs into these). Add new categories here; the catalog maps
    /// unknown categories to an empty list instead of failing.
    /// </summary>
    public static class AssetCategories
    {
        // Surfaces (area prefabs of type Surface)
        public const string SurfaceAny = "surface";
        public const string SurfaceGrass = "surface.grass";
        public const string SurfacePaving = "surface.paving";
        public const string SurfaceAsphalt = "surface.asphalt";
        public const string SurfaceNatural = "surface.natural";   // sand, gravel, dirt, forest floor

        // Vegetation
        public const string Tree = "tree";
        public const string Bush = "bush";
        public const string Planter = "planter";
        public const string Flowerbed = "flowerbed";

        // Furniture & features
        public const string Bench = "bench";
        public const string Lamp = "lamp";
        public const string Bin = "bin";
        public const string Centerpiece = "centerpiece";   // fountains, statues, monuments
        public const string Playground = "playground";
        public const string Sports = "sports";             // courts, goals, fitness
        public const string Restroom = "restroom";
        public const string Kiosk = "kiosk";               // food stands, ice cream, small service props
        public const string Shelter = "shelter";           // pavilions, gazebos, picnic shelters
        public const string Picnic = "picnic";             // picnic tables, BBQ
        public const string Camping = "camping";           // tents, campfires, caravans
        public const string CampingPitch = "camping.pitch"; // automatic pitch contents: tents, caravans (subset of camping)
        public const string CampingFire = "camping.fire";   // automatic campfires / fire pits (subset of camping)
        public const string Decoration = "decoration";     // misc props
        public const string Fence = "fence";               // net fences or fence props
        public const string Bollard = "bollard";

        // Networks
        public const string Road = "net.road";
        public const string Pathway = "net.pathway";
        public const string InvisiblePath = "net.invisiblePath";

        // Parking
        public const string ParkingDecal = "parking.decal";
        public const string Charger = "parking.charger";

        // Zoning
        public const string Zone = "zone";
    }
}
