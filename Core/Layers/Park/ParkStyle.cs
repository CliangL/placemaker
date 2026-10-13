// Composition parameters per park style. The "style" control picks one row; densities and
// assets stay separate controls so a player can have e.g. a dense formal park.
//
// Natural row = ParkManager's tuned values (grove coverage 28 %, grove/belt/solitaire shares
// 55/30/12 % for trees). Other rows are variations measured against the same 1–5 ha test
// shapes so the tree count still follows the density slider.

namespace Placemaker.Core.Layers.Park
{
    public sealed class ParkStyle
    {
        public string Id;
        /// <summary>Share of the park area covered by grove outlines.</summary>
        public float GroveCoverage;
        public float TreeGroveShare, TreeBeltShare, TreeSolitaireCap;
        public float BushGroveShare, BushBeltShare;
        /// <summary>Formal planting: trees as avenues along paths, bushes as hedges along the boundary.</summary>
        public bool Formal;
        /// <summary>Clearing radius relative to the largest lawn's inscribed radius (0 = no protected clearing).</summary>
        public float ClearingFactor;
        /// <summary>Default for curved paths when the player has not set it.</summary>
        public bool Curved;

        public static readonly ParkStyle Natural = new ParkStyle
        {
            Id = "natural", GroveCoverage = 0.28f, TreeGroveShare = 0.55f, TreeBeltShare = 0.30f, TreeSolitaireCap = 0.12f,
            BushGroveShare = 0.5f, BushBeltShare = 0.4f, ClearingFactor = 0.7f, Curved = true,
        };

        public static readonly ParkStyle Woodland = new ParkStyle
        {
            Id = "woodland", GroveCoverage = 0.6f, TreeGroveShare = 0.72f, TreeBeltShare = 0.2f, TreeSolitaireCap = 0.08f,
            BushGroveShare = 0.65f, BushBeltShare = 0.3f, ClearingFactor = 0.45f, Curved = true,
        };

        public static readonly ParkStyle Meadow = new ParkStyle
        {
            Id = "meadow", GroveCoverage = 0.1f, TreeGroveShare = 0.35f, TreeBeltShare = 0.25f, TreeSolitaireCap = 0.4f,
            BushGroveShare = 0.35f, BushBeltShare = 0.45f, ClearingFactor = 0.85f, Curved = true,
        };

        public static readonly ParkStyle Pocket = new ParkStyle
        {
            Id = "pocket", GroveCoverage = 0.15f, TreeGroveShare = 0.3f, TreeBeltShare = 0.55f, TreeSolitaireCap = 0.15f,
            BushGroveShare = 0.3f, BushBeltShare = 0.6f, ClearingFactor = 0.6f, Curved = true,
        };

        public static readonly ParkStyle FormalStyle = new ParkStyle
        {
            Id = "formal", Formal = true, ClearingFactor = 0.8f, Curved = false,
        };

        public static ParkStyle Get(string id)
        {
            switch (id)
            {
                case "formal": return FormalStyle;
                case "woodland": return Woodland;
                case "meadow": return Meadow;
                case "pocket": return Pocket;
                default: return Natural;
            }
        }
    }
}
