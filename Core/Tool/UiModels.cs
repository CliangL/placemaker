// Placemaker — JSON shapes of the UI contract (UI/src/contract.ts is authoritative).
//
// Pure builders so the exact field names are unit-tested without the game; the game's
// PlacemakerUISystem only gathers the inputs and pushes the strings when they change.

using System;
using System.Collections.Generic;
using System.Globalization;
using Unity.Mathematics;

namespace Placemaker.Core.Tool
{
    public sealed class HintInfo
    {
        public LocText Text;
        public string[] Keys = Array.Empty<string>();
        public HintInfo(string key, params string[] keys) { Text = new LocText(key); Keys = keys ?? Array.Empty<string>(); }
    }

    public sealed class ToolStateInput
    {
        public bool Open;
        public ToolStage Stage;
        public List<ToolStage> Reachable = new List<ToolStage>();
        public SelectionMode Mode;
        public bool BrushSquare;
        public float BrushSize = 150f;
        public float BrushAngle;
        public bool AreaValid, AreaClosed;
        public int PointCount;
        public double AreaM2, PerimeterM;
        public LocText AreaProblem;
        public PrimaryAction Primary;
        public bool CanUndo, CanRedo;
        public LocText UndoLabel, RedoLabel;
        public string MarkerStepPath, MarkerKind;
        public LocText MarkerLabel;
        public List<HintInfo> Hints = new List<HintInfo>();
        public bool Planning;
        public float? BuildProgress;
        public int EditingGroup;
        public bool AnarchyAvailable, AnarchyEnabled;
        public bool PanelCollapsed;
    }

    public sealed class RecipeListEntry
    {
        public string Id = "";
        public string Name = "";
        public string Icon = "";
        public bool Custom;
        public string DescriptionKey = "";
    }

    public sealed class SummaryInput
    {
        public ComposedPlan Plan;
        public Recipe Recipe;
        public Dictionary<string, AssetInfo> Assets;
        public bool Stale;
        public bool CanAfford = true;
        /// <summary>acknowledgePermanent(true) was given for the current permanent effects.</summary>
        public bool PermanentAcknowledged;
        /// <summary>Extra issues from the tool itself (build failure, timeout…).</summary>
        public List<PlanIssue> ExtraIssues = new List<PlanIssue>();
    }

    public static class UiModels
    {
        // ───────────── state ─────────────

        public static JsonValue State(ToolStateInput s)
        {
            var o = JsonValue.Obj();
            o.Set("open", s.Open);
            o.Set("stage", StageMachine.Id(s.Stage));
            var reach = JsonValue.Arr();
            foreach (var st in s.Reachable) reach.Add(StageMachine.Id(st));
            o.Set("reachable", reach);
            o.Set("areaMode", SelectionModes.Id(s.Mode));
            o.Set("brush", JsonValue.Obj().Set("shape", s.BrushSquare ? "square" : "round").Set("size", Round(s.BrushSize, 1)).Set("angle", Round(s.BrushAngle, 1)));
            var area = JsonValue.Obj()
                .Set("valid", s.AreaValid).Set("closed", s.AreaClosed).Set("pointCount", s.PointCount)
                .Set("areaM2", Round(s.AreaM2, 0)).Set("perimeterM", Round(s.PerimeterM, 1));
            if (s.AreaProblem != null) area.Set("problem", s.AreaProblem.ToJson());
            o.Set("area", area);
            o.Set("primary", Primary(s.Primary));
            o.Set("canUndo", s.CanUndo);
            o.Set("canRedo", s.CanRedo);
            if (s.CanUndo && s.UndoLabel != null) o.Set("undoLabel", s.UndoLabel.ToJson());
            if (s.CanRedo && s.RedoLabel != null) o.Set("redoLabel", s.RedoLabel.ToJson());
            if (!string.IsNullOrEmpty(s.MarkerKind))
                o.Set("markerMode", JsonValue.Obj().Set("stepPath", s.MarkerStepPath ?? "").Set("kind", s.MarkerKind)
                    .Set("label", (s.MarkerLabel ?? new LocText("Placemaker.Marker." + s.MarkerKind)).ToJson()));
            var hints = JsonValue.Arr();
            foreach (var h in s.Hints)
            {
                var ho = JsonValue.Obj().Set("text", h.Text.ToJson());
                if (h.Keys.Length > 0)
                {
                    var k = JsonValue.Arr();
                    foreach (var key in h.Keys) k.Add(key);
                    ho.Set("keys", k);
                }
                hints.Add(ho);
            }
            o.Set("hints", hints);
            o.Set("planning", s.Planning);
            if (s.BuildProgress.HasValue) o.Set("buildProgress", Round(math.saturate(s.BuildProgress.Value), 3));
            if (s.EditingGroup > 0) o.Set("editingGroup", s.EditingGroup);
            if (s.AnarchyAvailable) o.Set("anarchy", JsonValue.Obj().Set("available", true).Set("enabled", s.AnarchyEnabled));
            o.Set("panelCollapsed", s.PanelCollapsed);
            return o;
        }

        public static JsonValue Primary(PrimaryAction p)
        {
            p = p ?? new PrimaryAction { Kind = PrimaryKind.None, Label = new LocText("Placemaker.Primary.SelectArea") };
            var o = JsonValue.Obj()
                .Set("action", PrimaryAction.KindId(p.Kind))
                .Set("label", (p.Label ?? new LocText("")).ToJson())
                .Set("enabled", p.Enabled);
            if (!p.Enabled && p.Reason != null) o.Set("reason", p.Reason.ToJson());
            return o;
        }

        // ───────────── catalog ─────────────

        public static JsonValue Catalog(IEnumerable<IAreaGenerator> generators, IEnumerable<RecipeListEntry> recipes)
        {
            var layers = JsonValue.Arr();
            foreach (var g in generators)
            {
                if (g == null) continue;
                try { layers.Add(Layer(g)); }
                catch (Exception) { /* a broken generator must not hide the others */ }
            }
            var list = JsonValue.Arr();
            foreach (var r in recipes)
            {
                var ro = JsonValue.Obj().Set("id", r.Id).Set("name", r.Name).Set("icon", IconPath(r.Icon)).Set("custom", r.Custom);
                if (!string.IsNullOrEmpty(r.DescriptionKey)) ro.Set("description", r.DescriptionKey);
                list.Add(ro);
            }
            return JsonValue.Obj().Set("layers", layers).Set("recipes", list);
        }

        public static JsonValue Layer(IAreaGenerator g)
        {
            var o = JsonValue.Obj()
                .Set("id", g.Id ?? "")
                .Set("name", g.NameKey ?? "")
                .Set("icon", IconPath(g.Icon))
                .Set("kind", g.Kind == LayerKind.Split ? "split" : g.Kind == LayerKind.AddOn ? "addOn" : "fill")
                .Set("accepts", Strings(g.Accepts))
                .Set("emits", Strings(g.Emits));
            var controls = JsonValue.Arr();
            if (g.Controls != null) foreach (var c in g.Controls) if (c != null) controls.Add(Control(c));
            o.Set("controls", controls);
            var presets = JsonValue.Arr();
            if (g.Presets != null)
                foreach (var p in g.Presets)
                {
                    if (p == null) continue;
                    var po = JsonValue.Obj().Set("id", p.Id ?? "").Set("name", p.LabelKey ?? "");
                    if (!string.IsNullOrEmpty(p.Icon)) po.Set("icon", IconPath(p.Icon));
                    presets.Add(po);
                }
            o.Set("presets", presets);
            return o;
        }

        public static string ControlKindId(ControlKind k)
        {
            switch (k)
            {
                case ControlKind.Toggle: return "toggle";
                case ControlKind.Choice: return "choice";
                case ControlKind.Asset: return "asset";
                case ControlKind.AssetMulti: return "assetMulti";
                case ControlKind.Markers: return "markers";
                case ControlKind.Arrangement: return "arrangement";
                default: return "slider";
            }
        }

        public static JsonValue Control(ControlSchema c)
        {
            var o = JsonValue.Obj()
                .Set("id", c.Id ?? "")
                .Set("kind", ControlKindId(c.Kind))
                .Set("tier", c.Tier == ControlTier.More ? "more" : "essential")
                .Set("group", c.Group ?? "")
                .Set("label", c.LabelKey ?? "");
            if (!string.IsNullOrEmpty(c.TooltipKey)) o.Set("tooltip", c.TooltipKey);
            if (!string.IsNullOrEmpty(c.Icon)) o.Set("icon", IconPath(c.Icon));
            if (c.Kind == ControlKind.Slider)
            {
                o.Set("min", c.Min).Set("max", c.Max).Set("step", c.Step);
            }
            if (!string.IsNullOrEmpty(c.Unit)) o.Set("unit", c.Unit);
            if (c.Options != null && c.Options.Count > 0)
            {
                var opts = JsonValue.Arr();
                foreach (var op in c.Options)
                {
                    if (op == null) continue;
                    var oo = JsonValue.Obj().Set("value", op.Value ?? "").Set("label", op.LabelKey ?? "");
                    if (!string.IsNullOrEmpty(op.Icon)) oo.Set("icon", IconPath(op.Icon));
                    if (!string.IsNullOrEmpty(op.TooltipKey)) oo.Set("tooltip", op.TooltipKey);
                    opts.Add(oo);
                }
                o.Set("options", opts);
            }
            if (!string.IsNullOrEmpty(c.AssetCategory)) o.Set("assetCategory", c.AssetCategory);
            if (c.MarkerKinds != null && c.MarkerKinds.Length > 0) o.Set("markerKinds", Strings(c.MarkerKinds));
            if (!string.IsNullOrEmpty(c.VisibleWhen)) o.Set("visibleWhen", c.VisibleWhen);
            if (c.Cheap) o.Set("cheap", true);
            if (c.Default != null && c.Kind != ControlKind.Markers) o.Set("default", DesignEditor.ToJson(c.Default));
            return o;
        }

        // ───────────── design ─────────────

        public sealed class StepStatus
        {
            public bool Ready = true;
            public LocText NextStep;
        }

        /// <param name="status">Readiness per step path (missing = ready).</param>
        /// <param name="plan">Latest finished plan (for the regions each step produced), may be null.</param>
        public static JsonValue Design(Recipe recipe, string recipeId, bool modified, string focus,
            IReadOnlyDictionary<string, StepStatus> status, ComposedPlan plan, Func<string, IAreaGenerator> generators = null)
        {
            var o = JsonValue.Obj().Set("recipeId", recipeId ?? "").Set("modified", modified);
            o.Set("root", recipe?.Root != null ? Step(recipe, recipe.Root, RecipePaths.Root, status, plan, 0, generators ?? GeneratorRegistry.Get) : JsonValue.Null);
            o.Set("focus", focus ?? RecipePaths.Root);
            return o;
        }

        private static JsonValue Step(Recipe recipe, RecipeStep s, string path, IReadOnlyDictionary<string, StepStatus> status, ComposedPlan plan, int depth,
            Func<string, IAreaGenerator> generators)
        {
            var o = JsonValue.Obj()
                .Set("path", path)
                .Set("layer", s.Layer ?? "")
                .Set("target", s.Target ?? "")
                .Set("enabled", s.Enabled);
            var values = JsonValue.Obj();
            var keys = new List<string>(s.Settings.Values.Keys);
            keys.Sort(StringComparer.Ordinal);
            foreach (var k in keys) values.Set(k, DesignEditor.ToJson(s.Settings.Values[k]));
            o.Set("values", values);
            var locked = new List<string>(s.Locked);
            locked.Sort(StringComparer.Ordinal);
            o.Set("locked", Strings(locked.ToArray()));
            var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
            foreach (var m in s.Markers) { counts.TryGetValue(m.Kind ?? "", out int c); counts[m.Kind ?? ""] = c + 1; }
            var markers = JsonValue.Arr();
            foreach (var kv in counts) markers.Add(JsonValue.Obj().Set("kind", kv.Key).Set("count", kv.Value));
            o.Set("markers", markers);
            var markerList = JsonValue.Arr();
            for (int i = 0; i < s.Markers.Count; i++)
            {
                var p = s.Markers[i].Position;
                markerList.Add(JsonValue.Obj().Set("kind", s.Markers[i].Kind ?? "").Set("index", i)
                    .Set("position", JsonValue.Arr().Add(Round(p.x, 2)).Add(Round(p.y, 2)).Add(Round(p.z, 2))));
            }
            o.Set("markerList", markerList);
            IAreaGenerator gen = null;
            try { gen = generators?.Invoke(s.Layer); } catch (Exception) { gen = null; }
            var kinds = JsonValue.Arr();
            foreach (var k in MarkerKinds.For(gen))
            {
                var ko = JsonValue.Obj().Set("kind", k.Kind).Set("label", k.LabelKey);
                if (!string.IsNullOrEmpty(k.Icon)) ko.Set("icon", IconPath(k.Icon));
                if (!string.IsNullOrEmpty(k.TooltipKey)) ko.Set("tooltip", k.TooltipKey);
                kinds.Add(ko);
            }
            o.Set("markerKinds", kinds);
            o.Set("markersRequired", MarkerKinds.Required(gen, s.Settings));
            var features = Features.Of(gen);
            if (features.Count > 0)
            {
                var fa = JsonValue.Arr();
                foreach (var f in features)
                {
                    if (f == null || string.IsNullOrEmpty(f.Layer)) continue;
                    IAreaGenerator fg = null;
                    try { fg = generators?.Invoke(f.Layer); } catch (Exception) { fg = null; }
                    int idx = Features.ChildIndex(s, f);
                    string label = !string.IsNullOrEmpty(f.LabelKey) ? f.LabelKey : fg?.NameKey ?? f.Layer;
                    var fo = JsonValue.Obj().Set("layer", f.Layer).Set("label", label ?? "")
                        .Set("icon", IconPath(fg?.Icon ?? ""))
                        .Set("enabled", idx >= 0 && s.Children[idx].Enabled);
                    if (idx >= 0) fo.Set("path", RecipePaths.Child(path, idx));
                    fa.Add(fo);
                }
                o.Set("features", fa);
            }
            StepStatus st = null;
            if (status != null) status.TryGetValue(path, out st);
            o.Set("ready", st == null || st.Ready);
            if (st != null && !st.Ready && st.NextStep != null) o.Set("nextStep", st.NextStep.ToJson());
            var children = JsonValue.Arr();
            if (depth < Composer.MaxDepth + 2)
                for (int i = 0; i < s.Children.Count; i++)
                    if (s.Children[i] != null) children.Add(Step(recipe, s.Children[i], RecipePaths.Child(path, i), status, plan, depth + 1, generators));
            o.Set("children", children);
            var overrides = JsonValue.Obj();
            if (depth < Composer.MaxDepth + 2)
            {
                var ok = new List<string>(s.RegionOverrides.Keys);
                ok.Sort(StringComparer.Ordinal);
                foreach (var k in ok)
                    if (s.RegionOverrides[k] != null)
                        overrides.Set(k, Step(recipe, s.RegionOverrides[k], RecipePaths.Override(path, s, k), status, plan, depth + 1, generators));
            }
            o.Set("overrides", overrides);
            o.Set("regions", Regions(plan, path));
            return o;
        }

        /// <summary>Regions produced by the plans of the step at <paramref name="stepPath"/> (all its region runs).</summary>
        public static JsonValue Regions(ComposedPlan plan, string stepPath)
        {
            var arr = JsonValue.Arr();
            if (plan == null) return arr;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            // An override step runs exactly once, as "childPath@regionKey" — its own plan path.
            int slash = stepPath.LastIndexOf('/');
            bool isOverride = stepPath.IndexOf('@', slash + 1) >= 0;
            string prefix = PlanPathOf(stepPath) + "@";
            foreach (var l in plan.Layers)
            {
                if (l?.StepPath == null) continue;
                if (isOverride ? l.StepPath != stepPath : !l.StepPath.StartsWith(prefix, StringComparison.Ordinal)) continue;
                foreach (var r in l.Regions)
                {
                    if (r == null || string.IsNullOrEmpty(r.Key) || !seen.Add(r.Key)) continue;
                    arr.Add(JsonValue.Obj().Set("key", r.Key).Set("tags", Strings(r.Tags))
                        .Set("areaM2", Round(Pricing.PolygonArea(r.Ring ?? Array.Empty<float3>()), 0)));
                }
            }
            return arr;
        }

        /// <summary>UI path → composer path prefix ("0/1@block:3" → "0/1"; overrides run under the child's path).</summary>
        public static string PlanPathOf(string stepPath)
        {
            if (string.IsNullOrEmpty(stepPath)) return "";
            int at = stepPath.LastIndexOf('@');
            if (at < 0) return stepPath;
            int slashAfter = stepPath.IndexOf('/', at);
            // ".../1@block:3/0": children of an override run as ".../1/0" in the composer.
            if (slashAfter > 0) return PlanPathOf(stepPath.Substring(0, at) + stepPath.Substring(slashAfter));
            return stepPath.Substring(0, at);
        }

        // ───────────── summary ─────────────

        public static JsonValue Summary(SummaryInput s)
        {
            var o = JsonValue.Obj();
            var plan = s.Plan;
            o.Set("ready", plan != null);
            o.Set("stale", s.Stale);
            var assets = s.Assets ?? new Dictionary<string, AssetInfo>();
            if (plan == null)
            {
                o.Set("stats", JsonValue.Arr()).Set("cost", 0).Set("upkeep", 0).Set("canAfford", s.CanAfford)
                 .Set("layers", JsonValue.Arr());
                var noIssues = JsonValue.Arr();
                foreach (var i in s.ExtraIssues) noIssues.Add(Issue(i, null, s.Recipe));
                o.Set("issues", noIssues).Set("permanent", JsonValue.Arr()).Set("permanentAcknowledged", true);
                return o;
            }
            o.Set("stats", Stats(plan.MergedStats()));
            var total = Pricing.Of(plan, assets);
            o.Set("cost", Math.Round(total.Cost));
            o.Set("upkeep", Math.Round(total.Upkeep));
            o.Set("canAfford", s.CanAfford);
            var layers = JsonValue.Arr();
            foreach (var l in plan.Layers)
            {
                if (l == null) continue;
                var single = new ComposedPlan();
                single.Layers.Add(l);
                var e = Pricing.Of(single, assets);
                SplitPlanPath(l.StepPath, out string stepPath, out string regionKey);
                layers.Add(JsonValue.Obj()
                    .Set("stepPath", s.Recipe != null ? RecipePaths.OwnerOfPlan(s.Recipe, l.StepPath) ?? stepPath : stepPath)
                    .Set("layer", l.GeneratorId ?? "")
                    .Set("regionKey", regionKey)
                    .Set("stats", Stats(l.Stats))
                    .Set("cost", Math.Round(e.Cost))
                    .Set("upkeep", Math.Round(e.Upkeep)));
            }
            o.Set("layers", layers);
            var issues = JsonValue.Arr();
            foreach (var i in plan.Issues) issues.Add(Issue(i, null, s.Recipe));
            foreach (var l in plan.Layers)
                if (l != null) foreach (var i in l.Issues) issues.Add(Issue(i, l.StepPath, s.Recipe));
            foreach (var i in s.ExtraIssues) issues.Add(Issue(i, null, s.Recipe));
            o.Set("issues", issues);
            var permanent = JsonValue.Arr();
            var effects = Permanent(plan);
            foreach (var p in effects) permanent.Add(p.ToJson());
            o.Set("permanent", permanent);
            // Extension (docs/proposals/gametool.md): whether acknowledgePermanent(true) covers the current effects.
            o.Set("permanentAcknowledged", effects.Count == 0 || s.PermanentAcknowledged);
            return o;
        }

        /// <summary>Permanent effects of a plan the player must acknowledge before Build (lake excavation, terraforming).</summary>
        public static List<LocText> Permanent(ComposedPlan plan)
        {
            var list = new List<LocText>();
            if (plan == null) return list;
            foreach (var l in plan.Layers)
            {
                if (l == null) continue;
                foreach (var t in l.Terraform)
                {
                    if (t == null || !t.Permanent) continue;
                    double area = Pricing.PolygonArea(t.Ring ?? Array.Empty<float3>());
                    list.Add(new LocText(float.IsNaN(t.WaterLevel) ? "Placemaker.Permanent.Terraform" : "Placemaker.Permanent.Water", Math.Round(area)));
                }
            }
            return list;
        }

        /// <summary>Identity of a set of permanent effects: an acknowledgement covers exactly these (reset when they change).</summary>
        public static string PermanentFingerprint(List<LocText> effects)
        {
            if (effects == null || effects.Count == 0) return "";
            var sb = new System.Text.StringBuilder();
            foreach (var e in effects) sb.Append(e.ToJson().ToString()).Append('|');
            return sb.ToString();
        }

        /// <summary>
        /// Icon path filter installed by the game (verifies vanilla Media files exist, fixes known
        /// renames). Identity in tests. Never returns null.
        /// </summary>
        public static Func<string, string> IconFilter;

        public static string IconPath(string icon)
        {
            if (string.IsNullOrEmpty(icon)) return "";
            var f = IconFilter;
            if (f == null) return icon;
            try { return f(icon) ?? ""; } catch (Exception) { return icon; }
        }

        public static void SplitPlanPath(string planPath, out string stepPath, out string regionKey)
        {
            planPath = planPath ?? "";
            int at = planPath.LastIndexOf('@');
            stepPath = at < 0 ? planPath : planPath.Substring(0, at);
            regionKey = at < 0 ? "" : planPath.Substring(at + 1);
        }

        public static JsonValue Stats(IEnumerable<PlanStat> stats)
        {
            var a = JsonValue.Arr();
            if (stats == null) return a;
            foreach (var st in stats)
                a.Add(JsonValue.Obj().Set("key", st.Key ?? "").Set("value", Round(st.Value, 2)).Set("unit", st.Unit ?? ""));
            return a;
        }

        public static JsonValue Issue(PlanIssue i, string planPath, Recipe recipe)
        {
            var o = JsonValue.Obj()
                .Set("severity", i.Severity == IssueSeverity.Error ? "error" : i.Severity == IssueSeverity.Warning ? "warning" : "info")
                .Set("text", new LocText(i.Key, i.Args ?? Array.Empty<object>()).ToJson());
            if (!string.IsNullOrEmpty(planPath))
                o.Set("stepPath", recipe != null ? RecipePaths.OwnerOfPlan(recipe, planPath) : planPath);
            if (i.At.HasValue)
            {
                var p = i.At.Value;
                o.Set("at", JsonValue.Arr().Add(Round(p.x, 2)).Add(Round(p.y, 2)).Add(Round(p.z, 2)));
            }
            return o;
        }

        // ───────────── helpers ─────────────

        public static JsonValue Strings(IEnumerable<string> items)
        {
            var a = JsonValue.Arr();
            if (items != null) foreach (var s in items) a.Add(s ?? "");
            return a;
        }

        private static double Round(double v, int digits) => double.IsNaN(v) || double.IsInfinity(v) ? 0 : Math.Round(v, digits);

        public static string Invariant(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);
    }
}
