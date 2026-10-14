// Placemaker — turning automatic asset refs (empty Id) into concrete prefab names (GPL-3.0).
// US5 "honest price": preview, Pricing.Of and the build must use the SAME prefabs, so the choice is
// made once, deterministically, before any of them run. Pure; the game catalog supplies the pools.
//
// FEEDBACK-2 #1 (consistency): an automatic build uses ONE asset per (step, category) — one fence,
// one bench, one lamp, one bin, one surface, one road… Variety (by PlannedObject.Variation) only
// for vegetation and a few "set" categories (playground equipment, tents), and even then drawn
// from one coherent vanilla park palette per step when the palette has that category.

using System;
using System.Collections.Generic;

namespace Placemaker.Core.Build
{
    /// <summary>What the catalog offers the resolver. Every list is already filtered to automatic-eligible items.</summary>
    public sealed class AutoAssetSource
    {
        /// <summary>Ordered automatic candidates of a category (first = category default; null/empty = none).</summary>
        public Func<string, IReadOnlyList<string>> Pool;
        /// <summary>True when a prefab of that name is loaded (null = everything exists).</summary>
        public Func<string, bool> Exists;
        /// <summary>Palette names usable for automatic dressing, in a stable order (null/empty = no palettes).</summary>
        public IReadOnlyList<string> Palettes;
        /// <summary>(palette, category) → ordered eligible items of that category seen in the palette.</summary>
        public Func<string, string, IReadOnlyList<string>> PaletteItems;
    }

    public static class AutoAssets
    {
        /// <summary>Categories where automatic objects vary by PlannedObject.Variation (natural look).</summary>
        public static readonly HashSet<string> VarietyCategories = new HashSet<string>(StringComparer.Ordinal)
        {
            AssetCategories.Tree, AssetCategories.Bush, AssetCategories.Flowerbed,
            // Sets, not furniture: five identical swings or tents read as a copy-paste error.
            AssetCategories.Playground, AssetCategories.CampingPitch,
            // Natural scatter: a rocky outcrop or a leaf/dirt decal scatter of one prefab looks stamped.
            AssetCategories.Rock, AssetCategories.Decal, AssetCategories.DecalLeaves, AssetCategories.DecalDirt,
            AssetCategories.DecalCracks, AssetCategories.DecalPuddles, AssetCategories.DecalPaving, AssetCategories.DecalGraffiti,
        };

        /// <summary>Categories drawn from the step's park palette when the palette has items of them.</summary>
        public static readonly HashSet<string> PaletteCategories = new HashSet<string>(StringComparer.Ordinal)
        {
            AssetCategories.Tree, AssetCategories.Bush, AssetCategories.Flowerbed, AssetCategories.Planter,
            AssetCategories.Bench, AssetCategories.Lamp, AssetCategories.Bin, AssetCategories.Centerpiece,
            AssetCategories.Playground, AssetCategories.Picnic, AssetCategories.Shelter, AssetCategories.Kiosk,
            AssetCategories.Restroom, AssetCategories.Bollard, AssetCategories.Decoration,
        };

        /// <summary>Largest number of distinct automatic assets in a variety category per step.</summary>
        public const int MaxVariety = 6;

        /// <summary>True when every automatic object of this category in a step is the same prefab.</summary>
        public static bool IsUniform(string category) => category == null || (!VarietyCategories.Contains(category) && !VarietyRegistry.Contains(category));

        /// <summary>
        /// Random seed for game objects/edges of a uniform category: the same within a build, so colour
        /// and mesh variations of one prefab do not differ from piece to piece (fence pieces, bench rows).
        /// </summary>
        public static int UniformSeed(string category, uint salt) => (int)Seeds.Mix(salt, "uniform|" + (category ?? ""));

        /// <summary>The palette of a step: stable for a seed, independent of the plan's contents.</summary>
        public static string PickPalette(IReadOnlyList<string> palettes, uint seed)
        {
            if (palettes == null || palettes.Count == 0) return null;
            return palettes[(int)(Seeds.Mix(seed, "palette") % (uint)palettes.Count)];
        }

        /// <summary>Back-compatible overload: pools only, no palettes.</summary>
        public static int Resolve(ComposedPlan plan, Func<string, IReadOnlyList<string>> pool, Func<string, bool> exists = null) =>
            Resolve(plan, new AutoAssetSource { Pool = pool, Exists = exists });

        /// <summary>
        /// Resolves every automatic ref of the plan in place and returns how many refs were resolved.
        /// Rules (deterministic: same plan + same source → same ids):
        /// • Steps: a layer belongs to the step of its parent layer (add-ons share their park's step);
        ///   a top-level layer, or a child of a split, starts a step. Each step picks one palette
        ///   from its seed.
        /// • Uniform categories (fences, benches, lamps, bins, centerpieces, surfaces, nets, chargers,
        ///   signs…): one id per (step, category) — the palette's best item when it has the category,
        ///   else the pool default.
        /// • Variety categories: pick by hash(Variation) among the first <see cref="MaxVariety"/> palette
        ///   items (or pool items when the palette lacks the category).
        /// • An explicit id that is not loaded falls back to its first loaded Fallback, else as automatic.
        /// </summary>
        public static int Resolve(ComposedPlan plan, AutoAssetSource source)
        {
            if (plan?.Layers == null || source?.Pool == null) return 0;
            var exists = source.Exists;
            int resolved = 0;
            var poolCache = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
            var candCache = new Dictionary<(string, string), IReadOnlyList<string>>();
            var steps = StepKeys(plan);
            var stepPalette = new Dictionary<string, string>(StringComparer.Ordinal);

            IReadOnlyList<string> Pool(string category)
            {
                if (category == null) return null;
                if (!poolCache.TryGetValue(category, out var list)) poolCache[category] = list = source.Pool(category);
                return list;
            }
            IReadOnlyList<string> Candidates(string step, string category)
            {
                if (category == null) return null;
                if (candCache.TryGetValue((step, category), out var list)) return list;
                list = null;
                if (PaletteCategories.Contains(category) && source.PaletteItems != null
                    && stepPalette.TryGetValue(step, out var palette) && palette != null)
                {
                    var items = source.PaletteItems(palette, category);
                    if (items != null && items.Count > 0)
                    {
                        var ok = new List<string>(items.Count);
                        foreach (var id in items) if (!string.IsNullOrEmpty(id) && (exists == null || exists(id))) ok.Add(id);
                        if (ok.Count > 0) list = ok;
                    }
                }
                list = list ?? Pool(category);
                candCache[(step, category)] = list;
                return list;
            }
            string Pick(string step, string category, uint variation)
            {
                var list = Candidates(step, category);
                // An empty custom category (no "rubber" surface installed) walks to its parent (g5).
                if (list == null || list.Count == 0)
                    foreach (var parent in CategoryFallbacks.Chain(category))
                    {
                        if (parent == category) continue;
                        list = Candidates(step, parent);
                        if (list != null && list.Count > 0) break;
                    }
                if (list == null || list.Count == 0) return null;
                if (IsUniform(category) || list.Count == 1) return list[0];
                int n = Math.Min(list.Count, MaxVariety);
                return list[(int)(Seeds.Mix(variation, category) % (uint)n)];
            }

            for (int li = 0; li < plan.Layers.Count; li++)
            {
                var l = plan.Layers[li];
                if (l == null) continue;
                var step = steps[li];
                if (!stepPalette.ContainsKey(step)) stepPalette[step] = PickPalette(source.Palettes, l.Seed);
                foreach (var n in l.Nets)
                    if (n != null && Needs(n.Asset, exists))
                        resolved += Set(ref n.Asset, n.Role == NetRole.Alley && exists != null && exists(AlleyPrefab)
                            ? AlleyPrefab : Pick(step, RoleCategories.ForNet(n), 0), exists);
                foreach (var s in l.Surfaces)
                    if (s != null && Needs(s.Asset, exists)) resolved += Set(ref s.Asset, Pick(step, RoleCategories.ForSurface(s), 0), exists);
                foreach (var a in l.Areas)
                    if (a != null && Needs(a.Asset, exists)) resolved += Set(ref a.Asset, Pick(step, RoleCategories.ForArea(a.Role), 0), exists);
                foreach (var o in l.Objects)
                {
                    if (o == null || !Needs(o.Asset, exists)) continue;
                    resolved += Set(ref o.Asset, Pick(step, RoleCategories.ForObject(o), o.Variation), exists);
                }
            }
            return resolved;
        }

        /// <summary>
        /// Step key per layer (same order as plan.Layers). A layer joins the step of the layer at its
        /// parent recipe path unless that parent is a split (whose children are separate steps).
        /// </summary>
        public static string[] StepKeys(ComposedPlan plan)
        {
            var keys = new string[plan.Layers.Count];
            var byPath = new Dictionary<string, (string step, string gen)>(StringComparer.Ordinal);
            for (int i = 0; i < plan.Layers.Count; i++)
            {
                var l = plan.Layers[i];
                string path = l?.StepPath ?? "";
                int at = path.IndexOf('@');
                if (at >= 0) path = path.Substring(0, at);
                string step = path.Length > 0 ? path : "#" + i;
                int slash = path.LastIndexOf('/');
                if (slash > 0 && byPath.TryGetValue(path.Substring(0, slash), out var parent) && parent.gen != SplitGeneratorId)
                    step = parent.step;
                keys[i] = step;
                if (path.Length > 0 && !byPath.ContainsKey(path)) byPath[path] = (step, l?.GeneratorId ?? "");
            }
            return keys;
        }

        private const string SplitGeneratorId = "split";

        /// <summary>Vanilla two-way alley (PLT: never "Alley Oneway", it has 2.5 m parking strips).</summary>
        public const string AlleyPrefab = "Alley";

        public static bool IsAuto(AssetRef a) => string.IsNullOrEmpty(a.Id);

        /// <summary>Automatic, or an explicit id this playset does not have (then: fallbacks → default).</summary>
        private static bool Needs(AssetRef a, Func<string, bool> exists) => IsAuto(a) || (exists != null && !exists(a.Id));

        private static int Set(ref AssetRef asset, string id, Func<string, bool> exists)
        {
            // An automatic ref with explicit fallbacks keeps the catalog order: first existing fallback wins.
            if (exists != null && asset.Fallbacks != null)
                foreach (var f in asset.Fallbacks)
                    if (!string.IsNullOrEmpty(f) && exists(f)) { id = f; break; }
            if (string.IsNullOrEmpty(id)) return 0;
            // Keep the original fallbacks: they still apply if the chosen asset disappears later.
            asset = new AssetRef(id, asset.Fallbacks ?? Array.Empty<string>());
            return 1;
        }
    }
}
