// Plug-in point for asset classification. Each type folder can ship its own rules
// (a class implementing ICategoryRules) without editing the shared classifiers; rules are
// discovered by reflection and run before the built-in leisure/general heuristics.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Placemaker.Core.Build
{
    public interface ICategoryRules
    {
        /// <summary>Higher runs first. Built-in heuristics run after all rules.</summary>
        int Priority { get; }

        /// <summary>
        /// Add categories for a visible static object (lower = lower-cased prefab name).
        /// Return true to stop further classification, false to let others add more.
        /// </summary>
        bool Classify(string lower, AssetTraits traits, List<string> categories);

        /// <summary>Automatic-pool eligibility for a category this rule owns; null = no opinion.</summary>
        bool? AutoEligible(string category, string lower, AssetTraits traits);
    }

    public static class CategoryRules
    {
        private static List<ICategoryRules> _all;

        public static IReadOnlyList<ICategoryRules> All
        {
            get
            {
                if (_all != null) return _all;
                var list = new List<ICategoryRules>();
                foreach (var t in typeof(CategoryRules).Assembly.GetTypes())
                {
                    if (t.IsAbstract || t.IsInterface || !typeof(ICategoryRules).IsAssignableFrom(t)) continue;
                    try { list.Add((ICategoryRules)Activator.CreateInstance(t)); } catch (Exception) { /* skip broken rule */ }
                }
                _all = list.OrderByDescending(r => r.Priority).ToList();
                return _all;
            }
        }

        public static bool Classify(string lower, AssetTraits traits, List<string> categories)
        {
            foreach (var rule in All)
            {
                try { if (rule.Classify(lower, traits, categories)) return true; }
                catch (Exception) { /* a faulty rule must never break the catalog */ }
            }
            return false;
        }

        public static bool? AutoEligible(string category, string lower, AssetTraits traits)
        {
            foreach (var rule in All)
            {
                try { var v = rule.AutoEligible(category, lower, traits); if (v.HasValue) return v; }
                catch (Exception) { }
            }
            return null;
        }
    }
}
