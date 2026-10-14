// Placemaker — automatic decal pools for dressing (s21). GPL-3.0.
//
// The catalog's theme pools (decal.leaves, decal.dirt, …) are word themes: "dirt" held stains, rust,
// sand and oil alike. Dressing needs to say WHAT it wants where — oil in a parking bay, a lined leaf
// drift along a kerb, mulch under a shrub — and in a size that fits the host. These pools are
// classified from DecalCatalog (family by item words, never by pack prefix), capped by real footprint
// (AssetCuration.MaxFootprint → DecalPools.MaxFootprint, applied when the game builds its pools), vary
// per object, and fall back to a broader pool when a playset has none (CategoryFallbacks).
//
// Orientation convention (all decorative pools): PlannedObject.Yaw is the direction of the decal's
// LONG axis. DecalFit turns the resolved prefab 90° when its long side is X, so strips lie along the
// path, bay or shore whatever pack the player has.

using System;
using System.Collections.Generic;
using Placemaker.Core.Build;

namespace Placemaker.Core.Decals
{
    public static class DecalPools
    {
        public const string LeavesEdge = "decal.leaves.edge";   // leaves drifted against an edge (kerbs, path sides, hedges)
        public const string Mulch = "decal.mulch";              // bark mulch / needle beds (shrub beds, conifers)
        public const string Moss = "decal.moss";                // moss, weeds, grass coming through (shade, ruins)
        public const string Wear = "decal.wear";                // worn ground: dirt patches, scuffs (benches, play, goalmouths)
        public const string Mud = "decal.mud";                  // mud, dust, dirt-road wear (works, yards)
        public const string Sand = "decal.sand";                // sand patches, sandy dust
        public const string WetSand = "decal.wetsand";          // wet sand / wet bands at the waterline
        public const string Wet = "decal.wet";                  // wet patches and splashes (fountains, puddled ground)
        public const string Rocks = "decal.rocks";              // gravel, stones, rubble grit
        public const string Stain = "decal.stain";              // grime and stains on paving
        public const string Oil = "decal.oil";                  // oil blots and drips (bays, machinery)
        public const string Patch = "decal.patch";              // asphalt / tar repairs
        public const string Tyre = "decal.tyre";                // tyre tracks and wet/stained wheel lines
        public const string Glass = "decal.glass";              // broken glass, bottles
        public const string Beach = "decal.beach";              // seaweed, shells, foam, sea trash, palm litter
        public const string Footprints = "decal.footprints";    // footprints in sand

        public sealed class Spec
        {
            public string Category, Parent;
            /// <summary>Largest footprint (m) an automatic pick may have.</summary>
            public float MaxFootprint;
            /// <summary>Smallest footprint (m) an automatic pick may have (0 = none): 0.3 m tyre-curve bits are not tyre marks.</summary>
            public float MinFootprint;
            /// <summary>Typical edge length (m) for spacing.</summary>
            public float Typical;
            public DecalFamily[] Families;
        }

        private static readonly Dictionary<string, Spec> Specs = Build();

        private static Dictionary<string, Spec> Build()
        {
            var d = new Dictionary<string, Spec>(StringComparer.Ordinal);
            void Add(string c, string parent, float max, float typical, params DecalFamily[] f) =>
                d[c] = new Spec { Category = c, Parent = parent, MaxFootprint = max, Typical = typical, Families = f };
            Add(LeavesEdge, AssetCategories.DecalLeaves, 8f, 6f, DecalFamily.LeafEdge);
            Add(Mulch, AssetCategories.DecalLeaves, 6f, 3f, DecalFamily.Mulch);
            Add(Moss, Wear, 7f, 2.5f, DecalFamily.Moss, DecalFamily.Overgrowth);
            Add(Wear, AssetCategories.DecalDirt, 7f, 4f, DecalFamily.Dirt, DecalFamily.Dust);
            Add(Mud, Wear, 14f, 8f, DecalFamily.Mud, DecalFamily.Dust);
            Add(Sand, Wear, 12f, 6f, DecalFamily.Sand);
            Add(WetSand, Wet, 12f, 4f, DecalFamily.WetSand);
            Add(Wet, AssetCategories.DecalPuddles, 10f, 4f, DecalFamily.Wet);
            Add(Rocks, Wear, 10f, 3f, DecalFamily.Rocks, DecalFamily.Rubble);
            Add(Stain, AssetCategories.DecalDirt, 7f, 4f, DecalFamily.Stain);
            Add(Oil, Stain, 6f, 2.5f, DecalFamily.Oil, DecalFamily.Leak);
            Add(Patch, AssetCategories.DecalCracks, 5f, 2f, DecalFamily.Patch);
            Add(Tyre, Stain, 12f, 5f, DecalFamily.Tyre);
            Add(Glass, AssetCategories.DecalLitter, 3f, 1f, DecalFamily.Glass);
            Add(Beach, Sand, 9f, 1.5f, DecalFamily.BeachWrack, DecalFamily.PalmLitter);
            Add(Footprints, null, 1.5f, 0.3f, DecalFamily.Footprint);
            d[Tyre].MinFootprint = 1.5f;
            d[Oil].MinFootprint = 0.8f;
            d[Wear].MinFootprint = 1.5f;
            d[Mud].MinFootprint = 2f;
            return d;
        }

        /// <summary>Caps for the older theme pools too: 10–15 m foliage sheets and 15 m road cracks are not scatter.</summary>
        private static readonly Dictionary<string, float> ThemeCaps = new Dictionary<string, float>(StringComparer.Ordinal)
        {
            [AssetCategories.DecalLeaves] = 8f, [AssetCategories.DecalDirt] = 8f, [AssetCategories.DecalCracks] = 8f,
            [AssetCategories.DecalPuddles] = 8f, [AssetCategories.DecalLitter] = 6f, [AssetCategories.DecalGraffiti] = 10f,
        };

        private static readonly Dictionary<string, float> ThemeTypical = new Dictionary<string, float>(StringComparer.Ordinal)
        {
            [AssetCategories.DecalLeaves] = 6f, [AssetCategories.DecalDirt] = 6f, [AssetCategories.DecalCracks] = 1.6f,
            [AssetCategories.DecalPuddles] = 3f, [AssetCategories.DecalLitter] = 1.5f, [AssetCategories.DecalGraffiti] = 6f,
            [AssetCategories.DecalPaving] = 4f,
        };

        public static IEnumerable<string> All => Specs.Keys;

        public static bool IsDressingPool(string category) => category != null && Specs.ContainsKey(category);

        public static Spec Of(string category) => category != null && Specs.TryGetValue(category, out var s) ? s : null;

        /// <summary>Footprint cap of a decal pool (0 = none / not a decal pool).</summary>
        public static float MaxFootprint(string category)
        {
            if (category == null) return 0f;
            if (Specs.TryGetValue(category, out var s)) return s.MaxFootprint;
            return ThemeCaps.TryGetValue(category, out var m) ? m : 0f;
        }

        /// <summary>Smallest footprint of a decal pool's automatic items (0 = none).</summary>
        public static float MinFootprint(string category) => category != null && Specs.TryGetValue(category, out var s) ? s.MinFootprint : 0f;

        /// <summary>Typical edge length (m) of a decal pool's items, for spacing (2 m when unknown).</summary>
        public static float Typical(string category)
        {
            if (category == null) return 2f;
            if (Specs.TryGetValue(category, out var s)) return s.Typical;
            return ThemeTypical.TryGetValue(category, out var t) ? t : 2f;
        }

        /// <summary>Decorative decal categories that follow the long-axis yaw convention (DecalFit may turn them 90°).</summary>
        public static bool FollowsLongAxis(string category)
        {
            if (string.IsNullOrEmpty(category)) return false;
            if (Specs.ContainsKey(category)) return true;
            return category == AssetCategories.DecalLeaves || category == AssetCategories.DecalDirt || category == AssetCategories.DecalCracks
                || category == AssetCategories.DecalPuddles || category == AssetCategories.DecalLitter;
        }

        /// <summary>The dressing pools a decal of this description belongs to.</summary>
        public static List<string> PoolsFor(DecalSpec spec)
        {
            var r = new List<string>(2);
            if (spec.Functional || spec.Shape == DecalShape.Sheet || spec.Shape == DecalShape.Tile) return r;
            foreach (var kv in Specs)
                if (Array.IndexOf(kv.Value.Families, spec.Family) >= 0) r.Add(kv.Key);
            if (spec.Family == DecalFamily.Puddle) r.Add(AssetCategories.DecalPuddles);
            return r;
        }

        private static readonly string[] NeverAuto = { "random", "placeholder", "test", "chirper", "logo", "advert", "wall", "roof", "building", "curb", "aerial" };

        /// <summary>Automatic eligibility of a prefab for one of the dressing pools (name only; sizes are capped when pools are built).</summary>
        public static bool Eligible(string category, string name)
        {
            if (!Specs.TryGetValue(category ?? "", out var s) || string.IsNullOrEmpty(name)) return false;
            var lower = name.ToLowerInvariant();
            if (AssetHeuristics.ContainsAny(lower, NeverAuto)) return false;
            var spec = DecalCatalog.Describe(name);
            if (spec.Functional || Array.IndexOf(s.Families, spec.Family) < 0) return false;
            // Wet SAND bands belong on beaches only; a leaf "lined" drift is the only edge leaves.
            if (category == Wet && lower.Contains("sand")) return false;
            return true;
        }

        /// <summary>
        /// (s21) False when the catalog is known and has no decorative decal at all (vanilla has none: every leaf, crack or
        /// stain decal comes from asset packs). True when unknown (no snapshot) or any dressing/theme pool has items.
        /// </summary>
        public static bool PacksAvailable()
        {
            bool known = false;
            foreach (var c in DecorativeCategories)
            {
                int n = Layers.Common.AssetAvailability.CountOf(c);
                if (n < 0) return true;
                known = true;
                if (n > 0) return true;
            }
            return !known;
        }

        private static IEnumerable<string> DecorativeCategories
        {
            get
            {
                foreach (var c in Specs.Keys) yield return c;
                yield return AssetCategories.DecalLeaves; yield return AssetCategories.DecalDirt; yield return AssetCategories.DecalCracks;
                yield return AssetCategories.DecalPuddles; yield return AssetCategories.DecalLitter; yield return AssetCategories.DecalGraffiti;
            }
        }

        public const string NoPacksKey = "Placemaker.Dress.NoPacks";

        private static bool _registered;
        private static readonly object Gate = new object();

        /// <summary>Registers the pools' fallbacks (an empty pool walks to its parent). Idempotent, thread-safe.</summary>
        public static void EnsureRegistered()
        {
            if (_registered) return;
            lock (Gate)
            {
                if (_registered) return;
                foreach (var s in Specs.Values)
                    if (!string.IsNullOrEmpty(s.Parent)) CategoryFallbacks.Register(s.Category, s.Parent);
                _registered = true;
            }
        }
    }

    /// <summary>
    /// Classification plug-in (discovered by reflection): adds the dressing pools to every decal by its
    /// DecalCatalog description, owns their automatic eligibility, and makes them vary per object.
    /// </summary>
    public sealed class DecalDressingRules : ICategoryRules, IVarietyCategories
    {
        public DecalDressingRules() { DecalPools.EnsureRegistered(); }

        public int Priority => 5;

        public bool Classify(string lower, AssetTraits traits, List<string> categories)
        {
            if ((traits & AssetTraits.DecalMesh) == 0 || string.IsNullOrEmpty(lower)) return false;
            foreach (var c in DecalPools.PoolsFor(DecalCatalog.Describe(lower)))
                if (!categories.Contains(c)) categories.Add(c);
            return false;
        }

        public bool? AutoEligible(string category, string lower, AssetTraits traits)
        {
            if (!DecalPools.IsDressingPool(category)) return null;
            return DecalPools.Eligible(category, lower);
        }

        public IEnumerable<string> VarietyCategories => DecalPools.All;
    }
}
