// Park stats edits from the Review stage (s17, docs/PARK-STATS.md). They live on the recipe (Recipe.ParkStats)
// so undo, Build (receipt) and Edit keep them, but they never change the plan: no re-plan, only the summary.

using System;
using System.Globalization;
using Placemaker.Core;
using Placemaker.Core.Build;

namespace Placemaker.Game.Tool
{
    public sealed partial class PlacemakerToolSystem
    {
        private int _parkStatsVersion;

        /// <summary>Bumped on every park-stats edit (summary change detection; the plan does not change).</summary>
        public int ParkStatsVersion => _parkStatsVersion;

        private bool EditParkStats(Func<ParkStatsSettings, bool> change, string label, string coalesce)
        {
            EndValuePreview(true);
            if (_recipe == null) return false;
            var working = _recipe.Clone();
            var settings = working.ParkStats?.Clone() ?? new ParkStatsSettings();
            bool changed;
            try { changed = change(settings); }
            catch (Exception e)
            {
                Mod.Log.Warn("Placemaker: park stats edit failed: " + e.Message);
                return false;
            }
            if (!changed) return false;
            working.ParkStats = settings.IsDefault ? null : settings;
            _recipe = working;
            _designVersion++;
            _parkStatsVersion++;
            Commit(label, coalesce);
            return true;
        }

        /// <summary>UI: the player set a park stat (number as JSON or plain text).</summary>
        public void SetParkStat(string id, string valueJson)
        {
            if (Array.IndexOf(ParkStatIds.All, id) < 0) return;
            if (!double.TryParse((valueJson ?? "").Trim().Trim('"'), NumberStyles.Float, CultureInfo.InvariantCulture, out double v)) return;
            if (double.IsNaN(v) || double.IsInfinity(v)) return;
            EditParkStats(s =>
            {
                if (s.Overrides.TryGetValue(id, out var old) && old == v) return false;
                s.Overrides[id] = v;
                return true;
            }, "Placemaker.Undo.ParkStat", "parkStat|" + id);
        }

        /// <summary>UI: back to the suggested value ("" = every stat).</summary>
        public void ResetParkStat(string id)
        {
            EditParkStats(s =>
            {
                if (string.IsNullOrEmpty(id))
                {
                    if (s.Overrides.Count == 0) return false;
                    s.Overrides.Clear();
                    return true;
                }
                return s.Overrides.Remove(id);
            }, "Placemaker.Undo.ParkStatReset", null);
        }

        /// <summary>UI: "Keep within vanilla range".</summary>
        public void SetParkStatsKeepVanilla(bool keep) =>
            EditParkStats(s =>
            {
                if (s.KeepVanilla == keep) return false;
                s.KeepVanilla = keep;
                return true;
            }, "Placemaker.Undo.ParkStatsRange", null);
    }
}
