// Placemaker — turning automatic asset refs (empty Id) into concrete prefab names (GPL-3.0).
// US5 "honest price": preview, Pricing.Of and the build must use the SAME prefabs, so the choice is
// made once, deterministically, before any of them run. Pure; the game catalog supplies the pools.

using System;
using System.Collections.Generic;

namespace Placemaker.Core.Build
{
    public static class AutoAssets
    {
        /// <summary>Categories where automatic objects vary by PlannedObject.Variation (natural look).</summary>
        public static readonly HashSet<string> VarietyCategories = new HashSet<string>(StringComparer.Ordinal)
        {
            AssetCategories.Tree, AssetCategories.Bush, AssetCategories.Flowerbed,
        };

        /// <summary>
        /// Resolves every automatic ref of the plan in place. <paramref name="pool"/> returns the ordered
        /// candidate ids of a category (first = the category default; null/empty = nothing available).
        /// Variety categories pick pool[hash(variation) % count]; everything else (roads, paths,
        /// surfaces, furniture, parking decals) takes the default so a design stays consistent.
        /// Returns the number of refs resolved.
        /// </summary>
        public static int Resolve(ComposedPlan plan, Func<string, IReadOnlyList<string>> pool, Func<string, bool> exists = null)
        {
            if (plan?.Layers == null || pool == null) return 0;
            int resolved = 0;
            var cache = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
            IReadOnlyList<string> Pool(string category)
            {
                if (category == null) return null;
                if (!cache.TryGetValue(category, out var list)) cache[category] = list = pool(category);
                return list;
            }
            string Pick(string category, uint variation, bool variety)
            {
                var list = Pool(category);
                if (list == null || list.Count == 0) return null;
                if (!variety || list.Count == 1) return list[0];
                uint h = Seeds.Mix(variation, category);
                return list[(int)(h % (uint)list.Count)];
            }
            foreach (var l in plan.Layers)
            {
                if (l == null) continue;
                foreach (var n in l.Nets)
                    if (n != null && IsAuto(n.Asset)) resolved += Set(ref n.Asset, Pick(RoleCategories.ForNet(n.Role), 0, false), exists);
                foreach (var s in l.Surfaces)
                    if (s != null && IsAuto(s.Asset)) resolved += Set(ref s.Asset, Pick(RoleCategories.ForSurface(s.Role), 0, false), exists);
                foreach (var a in l.Areas)
                    if (a != null && IsAuto(a.Asset)) resolved += Set(ref a.Asset, Pick(RoleCategories.ForArea(a.Role), 0, false), exists);
                foreach (var o in l.Objects)
                {
                    if (o == null || !IsAuto(o.Asset)) continue;
                    var cat = RoleCategories.ForObject(o.Role);
                    resolved += Set(ref o.Asset, Pick(cat, o.Variation, VarietyCategories.Contains(cat)), exists);
                }
            }
            return resolved;
        }

        public static bool IsAuto(AssetRef a) => string.IsNullOrEmpty(a.Id);

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
