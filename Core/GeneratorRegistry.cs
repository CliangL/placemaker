using System;
using System.Collections.Generic;
using System.Linq;

namespace Placemaker.Core
{
    /// <summary>Marks an IAreaGenerator for discovery. Order = position in the "Make" row.</summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class AreaGeneratorAttribute : Attribute
    {
        public int Order { get; }
        public AreaGeneratorAttribute(int order) { Order = order; }
    }

    /// <summary>
    /// Finds every generator in this assembly by attribute, so each generator lives in its own
    /// folder without a central list to edit.
    /// </summary>
    public static class GeneratorRegistry
    {
        private static IReadOnlyList<IAreaGenerator> _all;

        public static IReadOnlyList<IAreaGenerator> All
        {
            get
            {
                if (_all != null) return _all;
                _all = typeof(GeneratorRegistry).Assembly.GetTypes()
                    .Where(t => !t.IsAbstract && typeof(IAreaGenerator).IsAssignableFrom(t))
                    .Select(t => (t, attr: (AreaGeneratorAttribute)Attribute.GetCustomAttribute(t, typeof(AreaGeneratorAttribute))))
                    .Where(x => x.attr != null)
                    .OrderBy(x => x.attr.Order)
                    .Select(x => (IAreaGenerator)Activator.CreateInstance(x.t))
                    .ToList();
                return _all;
            }
        }

        public static IAreaGenerator Get(string id)
        {
            foreach (var g in All) if (g.Id == id) return g;
            return null;
        }
    }
}
