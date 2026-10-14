// Art direction for the waterfront (art pass a3): explicit vanilla assets per style, used whenever the
// matching asset control is on automatic. The fence pool starts with 2 m residential fences and the paving
// pool mixes tiles and concrete at random, both wrong for a quay; benches and lamps stay one model per run so
// a promenade reads as designed street furniture.

namespace Placemaker.Core.Layers.Types.Waterfront
{
    public static class WaterfrontArt
    {
        public static string[] Paving(string style)
        {
            switch (style)
            {
                case "harbor": return new[] { "Concrete Surface 02", "Concrete Surface 01" };
                case "riverSteps": return new[] { "Tiles Surface 02", "Pavement Surface 02" };
                default: return new[] { "Pavement Surface 02", "Pavement Surface 01" };
            }
        }

        /// <summary>Waist-high railing nets (never the 2 m residential fence of the pool).</summary>
        public static readonly string[] Railings = { "EU_FenceResidentialLow02", "FenceResidentialLow01", "EU_FenceResidentialLow01" };
        public static readonly string[] Benches = { "Pack10-Bench02", "Pack10-Bench01", "GardenBench02" };
        public static readonly string[] Lamps = { "NA_LightpolePark02", "EU_LightpolePark02", "LightpolePark02" };
        public static readonly string[] Bins = { "Trashbin03", "Trashbin01" };
        public static readonly string[] Kiosks = { "PierKiosk02", "PierKiosk01", "Kiosk02" };
        public static readonly string[] Moorings = { "MooringBollard02", "MooringBollard01" };
        public static readonly string[] Nautical = { "LifeBuoyStation01" };
        public static readonly string[] Shrubs = { "GreenBushWild01", "GreenBushWildAdult01", "FlowerBushWild02" };
        public static readonly string[] AnglerBenches = { "CampgroundBench01", "CampgroundBench02" };

        public static string[] Trees(string style)
        {
            switch (style)
            {
                case "harbor": return new[] { "EU_ChestnutTree01", "NA_LindenTree01" };
                case "riverSteps": return new[] { "NA_LondonPlaneTree01", "EU_PoplarTree01" };
                case "fishing": return new[] { "EU_AlderTree01", "BirchTree01", "EU_PoplarTree01" };
                default: return new[] { "NA_LondonPlaneTree01", "NA_LindenTree01" };
            }
        }

        public static string[] Or(string[] picked, string[] curated) => picked != null && picked.Length > 0 ? picked : curated;
        public static string[] Or(string picked, string[] curated) => string.IsNullOrEmpty(picked) ? curated : new[] { picked };
    }
}
