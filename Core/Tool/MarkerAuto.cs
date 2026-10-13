// Placemaker — automatic markers (entrances, access points…).
//
// Playtest 1 decision (lead, overriding FEEDBACK-1 US3): a step whose markers the player has not
// touched gets the layer's SuggestMarkers as real, editable markers. They are re-suggested when
// the outline changes. As soon as the player adds, removes or clears a marker of that step, the
// step is "edited" and never auto-filled again (until the area is cleared) — removing every
// suggested entrance is a deliberate choice and makes the step unready ("Place an entrance").
// The edited flags live in the tool snapshot, so undo restores them with the markers.

using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace Placemaker.Core.Tool
{
    public static class MarkerAuto
    {
        /// <summary>
        /// Applies suggestions to every enabled, untouched step that runs on the whole outline.
        /// <paramref name="replace"/>: the outline changed — replace auto markers by fresh suggestions;
        /// otherwise only fill steps that have none. Returns true when any step changed.
        /// </summary>
        public static bool Apply(Recipe recipe, AreaShape shape, SiteContext site, ICollection<string> edited, bool replace,
            Func<string, IAreaGenerator> generators)
        {
            if (recipe?.Root == null || shape?.Ring == null || shape.Ring.Length < 3 || generators == null) return false;
            bool changed = false;
            foreach (var kv in new List<KeyValuePair<string, RecipeStep>>(RecipePaths.Walk(recipe)))
            {
                var path = kv.Key;
                var step = kv.Value;
                if (step == null || !step.Enabled || (edited != null && edited.Contains(path))) continue;
                if (!WholeOutline(recipe, path)) continue;
                if (!replace && step.Markers.Count > 0) continue;
                IAreaGenerator gen;
                try { gen = generators(step.Layer); } catch (Exception) { gen = null; }
                if (gen == null) continue;
                var kinds = MarkerKinds.For(gen);
                if (kinds.Count == 0) continue;
                List<EdgeMarker> suggested;
                try
                {
                    suggested = gen.SuggestMarkers(new AreaInput
                    {
                        Shape = shape,
                        Site = site ?? new SiteContext(),
                        Settings = step.Settings,
                        Seed = step.Seed,
                        RegionTags = new[] { RegionTags.Root },
                    }) ?? new List<EdgeMarker>();
                }
                catch (Exception) { suggested = new List<EdgeMarker>(); }
                var accepted = new List<EdgeMarker>(suggested.Count);
                foreach (var m in suggested)
                    foreach (var k in kinds)
                        if (k.Kind == m.Kind) { accepted.Add(m); break; }
                if (Same(step.Markers, accepted)) continue;
                step.Markers = accepted;
                changed = true;
            }
            return changed;
        }

        /// <summary>The step and all its ancestors (below the root) run in their parent's own region, i.e. on the whole outline.</summary>
        public static bool WholeOutline(Recipe recipe, string path)
        {
            if (path == RecipePaths.Root) return true;
            if (path.IndexOf('@') >= 0) return false;
            for (var p = path; p != null && p != RecipePaths.Root; p = RecipePaths.ParentOf(p))
            {
                var s = RecipePaths.Resolve(recipe, p);
                if (s == null || !string.IsNullOrEmpty(s.Target)) return false;
            }
            return true;
        }

        public static bool Same(List<EdgeMarker> a, List<EdgeMarker> b)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
                if (a[i].Kind != b[i].Kind || math.distancesq(a[i].Position, b[i].Position) > 0.01f) return false;
            return true;
        }
    }
}
