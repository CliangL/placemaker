// Placemaker — resolve "automatic" assets (empty AssetRef.Id, or ids missing from this playset)
// to concrete prefab names BEFORE pricing and Build, so the price shown is what Build places
// (FEEDBACK-1: "construction cost shows $0" — Pricing only prices explicit ids).
//
// Local resolver of the game-tool workstream. The build workstream plans an
// IAssetCatalog.ResolveAutomatic(ComposedPlan); when it lands, Resolve() below should just call it
// (docs/proposals/gametool.md §5 — lead dedupes).
//
// Rules (deterministic — same plan + same catalog → same ids):
// • Nets, surfaces, areas and functional objects: exactly what the build service does —
//   IAssetCatalog.Resolve(asset, category) (Id → Fallbacks → category default).
// • Natural-variety objects (trees, bushes, flowerbeds) without an explicit id: one of
//   Items(category) picked by PlannedObject.Variation, so a park is not one cloned tree.
// The resolved name is written into the plan's AssetRef.Id (fallbacks kept), and the build then
// resolves that name to the same prefab.

using System;
using System.Collections.Generic;
using Placemaker.Core;
using Placemaker.Core.Build;
using Unity.Entities;

namespace Placemaker.Game.Preview
{
    public static class AutoAssets
    {
        private static readonly HashSet<string> VarietyCategories = new HashSet<string>(StringComparer.Ordinal)
        {
            AssetCategories.Tree, AssetCategories.Bush, AssetCategories.Flowerbed,
        };

        /// <summary>Resolves every asset of <paramref name="plan"/> in place. Returns how many refs changed.</summary>
        public static int Resolve(ComposedPlan plan, IAssetCatalog catalog)
        {
            if (plan == null || catalog == null || !catalog.Ready) return 0;
            var cache = new Dictionary<string, string>(StringComparer.Ordinal);
            var names = new Dictionary<Entity, string>();
            int changed = 0;
            foreach (var l in plan.Layers)
            {
                if (l == null) continue;
                foreach (var n in l.Nets)
                    if (n != null && Fix(ref n.Asset, RoleCategories.ForNet(n.Role), 0, false, catalog, cache, names)) changed++;
                foreach (var s in l.Surfaces)
                    if (s != null && Fix(ref s.Asset, RoleCategories.ForSurface(s.Role), 0, false, catalog, cache, names)) changed++;
                foreach (var a in l.Areas)
                    if (a != null && Fix(ref a.Asset, RoleCategories.ForArea(a.Role), 0, false, catalog, cache, names)) changed++;
                foreach (var o in l.Objects)
                {
                    if (o == null) continue;
                    var cat = RoleCategories.ForObject(o.Role);
                    if (Fix(ref o.Asset, cat, o.Variation, VarietyCategories.Contains(cat ?? ""), catalog, cache, names)) changed++;
                }
            }
            return changed;
        }

        private static bool Fix(ref AssetRef asset, string category, uint variation, bool variety, IAssetCatalog catalog,
            Dictionary<string, string> cache, Dictionary<Entity, string> names)
        {
            if (string.IsNullOrEmpty(category)) return false;
            string id = asset.Id ?? "";
            bool automatic = id.Length == 0 && (asset.Fallbacks == null || asset.Fallbacks.Length == 0);
            if (automatic && variety)
            {
                IReadOnlyList<CatalogItem> items = null;
                try { items = catalog.Items(category); } catch (Exception) { }
                if (items != null && items.Count > 0)
                {
                    var pick = items[(int)(variation % (uint)items.Count)];
                    if (pick != null && !string.IsNullOrEmpty(pick.Id))
                    {
                        asset = new AssetRef(pick.Id, asset.Fallbacks);
                        return true;
                    }
                }
            }
            string key = category + "|" + id + "|" + (asset.Fallbacks != null ? string.Join(",", asset.Fallbacks) : "");
            if (!cache.TryGetValue(key, out var resolved))
            {
                resolved = "";
                try
                {
                    var e = catalog.Resolve(asset, category);
                    if (e != Entity.Null) resolved = NameOf(e, category, catalog, names);
                }
                catch (Exception) { resolved = ""; }
                cache[key] = resolved;
            }
            if (string.IsNullOrEmpty(resolved) || resolved == id) return false;
            asset = new AssetRef(resolved, asset.Fallbacks);
            return true;
        }

        private static string NameOf(Entity e, string category, IAssetCatalog catalog, Dictionary<Entity, string> names)
        {
            if (names.TryGetValue(e, out var n)) return n;
            n = "";
            if (catalog is Assets.AssetCatalogSystem sys) n = sys.NameOf(e);
            if (string.IsNullOrEmpty(n))
            {
                var items = catalog.Items(category);
                if (items != null)
                    foreach (var it in items)
                        if (it != null && it.Prefab == e) { n = it.Id; break; }
            }
            names[e] = n ?? "";
            return names[e];
        }
    }
}
