// Placemaker — how strong a decal is drawn (s23 decal art pass). GPL-3.0.
//
// User: "a lot more transparent, blend with the terrain … most of them ~80 % transparent; some are fine like the sand
// ones". Each decorative pool has an opacity (DecalPools); the game builds see-through clones per opacity TIER
// (Game/Build/DecalOpacitySystem), so opacities are quantised to a few tiers to keep the clone count small.

using Unity.Mathematics;

namespace Placemaker.Core.Decals
{
    public static class DecalLook
    {
        /// <summary>Opacity tiers in percent (100 = the original prefab).</summary>
        public static readonly int[] Tiers = { 10, 15, 25, 35, 50, 100 };

        /// <summary>Nearest tier (percent) of an opacity 0..1.</summary>
        public static int Tier(float opacity)
        {
            if (!(opacity < 0.995f)) return 100;
            int best = Tiers[0];
            float bestD = float.MaxValue;
            foreach (var t in Tiers)
            {
                float d = math.abs(t / 100f - opacity);
                if (d < bestD) { bestD = d; best = t; }
            }
            return best;
        }

        /// <summary>Opacity a planned object is drawn with: decorative decals per their pool, everything else 1.</summary>
        public static float OpacityOf(PlannedObject o)
        {
            if (o == null || o.Role != ObjectRole.Decal || DecalCatalog.IsFunctionalCategory(o.Category)) return 1f;
            return DecalPools.Opacity(o.Category);
        }
    }
}
