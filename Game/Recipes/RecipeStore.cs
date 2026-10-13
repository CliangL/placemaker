// Placemaker — the "Make" row: shipped recipes (Recipes/*.json in the mod folder), player recipes
// (ModsData/Placemaker/recipes/*.json) and a synthesized one-layer recipe for every registered
// Fill layer that has no shipped file, so a new layer shows up without anyone writing JSON.

using System;
using System.Collections.Generic;
using System.IO;
using Placemaker.Core;
using Placemaker.Core.Tool;
using Placemaker.Game.Settings;

namespace Placemaker.Game.Recipes
{
    public sealed class RecipeStore
    {
        public sealed class Entry
        {
            public Recipe Recipe;
            public bool Custom;
            public string DescriptionKey = "";
            public int Order;
            public string FilePath;   // player recipes only
        }

        private readonly List<Entry> _entries = new List<Entry>();
        private readonly Func<DesignEditor> _editor;

        /// <summary>Bumped on every reload/save/delete so the catalog binding knows to refresh.</summary>
        public int Version { get; private set; }

        public RecipeStore(Func<DesignEditor> editor) { _editor = editor; }

        public IReadOnlyList<Entry> Entries => _entries;

        public Entry Find(string id)
        {
            foreach (var e in _entries) if (e.Recipe.Id == id) return e;
            return null;
        }

        /// <summary>A fresh, editable copy of a recipe (with saved defaults for one-layer recipes).</summary>
        public Recipe Instantiate(string id, uint seed)
        {
            var e = Find(id);
            if (e == null) return null;
            var r = e.Recipe.Clone();
            if (r.Root.Seed == 0) r.Root.Seed = seed;
            return r;
        }

        public void Reload()
        {
            _entries.Clear();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            if (ModPaths.ModFolder != null)
                foreach (var e in LoadFolder(Path.Combine(ModPaths.ModFolder, "Recipes"), false))
                    if (seen.Add(e.Recipe.Id)) _entries.Add(e);

            // Every Fill layer is makeable even without a shipped file.
            var editor = _editor();
            foreach (var g in GeneratorRegistry.All)
            {
                if (g == null || g.Kind != LayerKind.Fill || seen.Contains(g.Id)) continue;
                try
                {
                    var r = editor.SingleLayerRecipe(g, 0);
                    seen.Add(r.Id);
                    _entries.Add(new Entry { Recipe = r, Order = 500 + _entries.Count });
                }
                catch (Exception ex) { Mod.Log.Warn("Placemaker: layer " + g.Id + " has no usable defaults: " + ex.Message); }
            }

            foreach (var e in LoadFolder(ModPaths.PlayerRecipeFolder, true))
                if (seen.Add(e.Recipe.Id)) _entries.Add(e);

            _entries.Sort((a, b) =>
            {
                if (a.Custom != b.Custom) return a.Custom ? 1 : -1;
                int c = a.Order.CompareTo(b.Order);
                return c != 0 ? c : string.CompareOrdinal(a.Recipe.Id, b.Recipe.Id);
            });
            Version++;
        }

        private static IEnumerable<Entry> LoadFolder(string folder, bool custom)
        {
            var list = new List<Entry>();
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return list;
            string[] files;
            try { files = Directory.GetFiles(folder, "*.json"); }
            catch (Exception e) { Mod.Log.Warn("Placemaker: cannot list " + folder + ": " + e.Message); return list; }
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            foreach (var path in files)
            {
                try
                {
                    var file = RecipeJson.Parse(File.ReadAllText(path));
                    var r = file.Recipe;
                    if (string.IsNullOrEmpty(r.Id)) r.Id = Path.GetFileNameWithoutExtension(path);
                    if (file.FormatVersion > RecipeJson.CurrentFormatVersion)
                        Mod.Log.Warn("Placemaker: " + Path.GetFileName(path) + " is from a newer version (format " + file.FormatVersion + "); loading what we understand");
                    if (GeneratorRegistry.Get(r.Root.Layer) == null)
                        Mod.Log.Info("Placemaker: recipe " + r.Id + " uses unknown layer '" + r.Root.Layer + "' (mod missing?) — listed, will report an issue");
                    if (custom && string.IsNullOrEmpty(r.Name) && string.IsNullOrEmpty(r.NameKey)) r.Name = r.Id;
                    list.Add(new Entry { Recipe = r, Custom = custom, DescriptionKey = file.DescriptionKey, Order = file.Order, FilePath = custom ? path : null });
                }
                catch (Exception e)
                {
                    Mod.Log.Warn("Placemaker: skipping recipe " + Path.GetFileName(path) + ": " + e.Message);
                }
            }
            return list;
        }

        /// <summary>Saves the design as a player recipe. Returns its id, or null on failure.</summary>
        public string SaveAs(Recipe design, string name, uint salt)
        {
            if (design?.Root == null) return null;
            name = (name ?? "").Trim();
            if (name.Length == 0) name = "My recipe";
            if (name.Length > 60) name = name.Substring(0, 60);
            var r = design.Clone();
            r.Id = RecipeJson.NewPlayerRecipeId(name, salt);
            r.Name = name;
            r.NameKey = "";
            foreach (var kv in RecipePaths.Walk(r)) kv.Value.Markers.Clear();   // markers belong to one site, not to a recipe
            var path = Path.Combine(ModPaths.PlayerRecipeFolder, r.Id + ".json");
            try
            {
                ModPaths.WriteAllTextAtomic(path, RecipeJson.Write(r).Write(true));
            }
            catch (Exception e)
            {
                Mod.Log.Warn("Placemaker: could not save recipe: " + e.Message);
                return null;
            }
            Reload();
            return r.Id;
        }

        /// <summary>Deletes a player recipe file (shipped/synthesized recipes cannot be deleted).</summary>
        public bool Delete(string id)
        {
            var e = Find(id);
            if (e == null || !e.Custom || string.IsNullOrEmpty(e.FilePath)) return false;
            try
            {
                // Keep a backup next to it instead of destroying the player's work outright.
                var bak = e.FilePath + ".deleted";
                if (File.Exists(bak)) File.Delete(bak);
                File.Move(e.FilePath, bak);
            }
            catch (Exception ex)
            {
                Mod.Log.Warn("Placemaker: could not delete recipe " + id + ": " + ex.Message);
                return false;
            }
            Reload();
            return true;
        }
    }
}
