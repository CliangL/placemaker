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
    /// <summary>(s16) Display name and thumbnail of a prefab (from the game catalog).</summary>
    public sealed class AssetLabel
    {
        public string Name = "";
        public string Icon = "";
    }

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
        /// <summary>Block paint brush rules (null = not sent).</summary>
        public Placemaker.Core.Geometry.BrushRules BrushRules;
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
        /// <summary>Coarse 0..0.95 while planning (0 = queued); not yet in contract.ts (proposed: state.planningProgress?: number).</summary>
        public double? PlanningProgress;
        public float? BuildProgress;
        public int EditingGroup;
        public bool AnarchyAvailable, AnarchyEnabled;
        public bool PanelCollapsed;
        /// <summary>contract: state.previewMode — the player's choice for the current stage.</summary>
        public PreviewMode PreviewMode;
        /// <summary>contract: state.ghostPending.</summary>
        public bool GhostPending;
        /// <summary>(s15) Selection modes this DLL knows but cannot offer right now, with the reason (null = all available).</summary>
        public Dictionary<SelectionMode, LocText> ModeUnavailable;
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
        /// <summary>
        /// (s15) UI ↔ DLL contract version, mirrored by PROTOCOL in UI/src/contract.ts. Bump both together whenever
        /// the UI needs something a previous DLL does not send or handle. The game reloads the UI file from disk but the
        /// DLL only on restart, so after an update they can disagree until the player restarts.
        /// </summary>
        public const int Protocol = 16;

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
            var brush = JsonValue.Obj().Set("shape", s.BrushSquare ? "square" : "round").Set("size", Round(s.BrushSize, 1)).Set("angle", Round(s.BrushAngle, 1));
            if (s.BrushRules != null) brush.Set("rules", BrushRulesJson(s.BrushRules));
            o.Set("brush", brush);
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
            if (s.Planning && s.PlanningProgress.HasValue) o.Set("planningProgress", Round(s.PlanningProgress.Value, 2));
            if (s.BuildProgress.HasValue) o.Set("buildProgress", Round(math.saturate(s.BuildProgress.Value), 3));
            if (s.EditingGroup > 0) o.Set("editingGroup", s.EditingGroup);
            if (s.AnarchyAvailable) o.Set("anarchy", JsonValue.Obj().Set("available", true).Set("enabled", s.AnarchyEnabled));
            o.Set("panelCollapsed", s.PanelCollapsed);
            o.Set("previewMode", PreviewModes.Id(s.PreviewMode));
            o.Set("ghostPending", s.GhostPending);
            // s15: the UI compares this with its own PROTOCOL (contract.ts) to detect a UI / DLL mismatch
            // (the UI file is reloaded by the game, the DLL only on restart) and lists the modes this DLL handles.
            o.Set("protocol", Protocol);
            var modes = JsonValue.Arr();
            foreach (var m in SelectionModes.All)
            {
                var mo = JsonValue.Obj().Set("mode", SelectionModes.Id(m));
                LocText no = null;
                bool off = s.ModeUnavailable != null && s.ModeUnavailable.TryGetValue(m, out no) && no != null;
                mo.Set("enabled", !off);
                if (off) mo.Set("reason", no.ToJson());
                modes.Add(mo);
            }
            o.Set("areaModes", modes);
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
            // Additive: which step / marker kind a placeMarkers action is about (the UI reveals its Entrances).
            if (p.Kind == PrimaryKind.PlaceMarkers && !string.IsNullOrEmpty(p.StepPath))
            {
                o.Set("stepPath", p.StepPath);
                if (!string.IsNullOrEmpty(p.MarkerKind)) o.Set("markerKind", p.MarkerKind);
            }
            return o;
        }

        // ───────────── catalog ─────────────

        /// <param name="presets">Preset list per type (PresetStore.ViewsFor: built-ins, overrides, customs); null = built-ins only.</param>
        public static JsonValue Catalog(IEnumerable<IAreaGenerator> generators, IEnumerable<RecipeListEntry> recipes,
            Func<IAreaGenerator, IReadOnlyList<PresetView>> presets = null)
        {
            var layers = JsonValue.Arr();
            foreach (var g in generators)
            {
                if (g == null) continue;
                try { layers.Add(Layer(g, presets)); }
                catch (Exception) { /* a broken generator must not hide the others */ }
            }
            var list = JsonValue.Arr();
            foreach (var r in recipes)
            {
                var ro = JsonValue.Obj().Set("id", r.Id).Set("name", r.Name).Set("icon", IconPath(r.Icon)).Set("custom", r.Custom);
                if (!string.IsNullOrEmpty(r.DescriptionKey)) ro.Set("description", r.DescriptionKey);
                var grp = GeneratorGroups.Of(GeneratorRegistry.Get(r.Id));
                if (!string.IsNullOrEmpty(grp)) ro.Set("group", grp);
                list.Add(ro);
            }
            return JsonValue.Obj().Set("layers", layers).Set("recipes", list);
        }

        public static JsonValue Layer(IAreaGenerator g, Func<IAreaGenerator, IReadOnlyList<PresetView>> presetViews = null)
        {
            var o = JsonValue.Obj()
                .Set("id", g.Id ?? "")
                .Set("name", g.NameKey ?? "")
                .Set("icon", IconPath(g.Icon))
                .Set("kind", g.Kind == LayerKind.Split ? "split" : g.Kind == LayerKind.AddOn ? "addOn" : "fill")
                .Set("accepts", Strings(g.Accepts))
                .Set("emits", Strings(g.Emits));
            if (GeneratorRegistry.IsHidden(g)) o.Set("hidden", true);
            var group = GeneratorGroups.Of(g);
            if (!string.IsNullOrEmpty(group)) o.Set("group", group);
            var controls = JsonValue.Arr();
            if (g.Controls != null) foreach (var c in g.Controls) if (c != null) controls.Add(Control(c));
            o.Set("controls", controls);
            var presets = JsonValue.Arr();
            IReadOnlyList<PresetView> views = null;
            if (presetViews != null)
            {
                try { views = presetViews(g); } catch (Exception) { views = null; }
            }
            if (views != null)
            {
                foreach (var v in views) if (v != null) presets.Add(Preset(v));
            }
            else if (g.Presets != null)
                foreach (var p in g.Presets)
                {
                    if (p == null) continue;
                    presets.Add(Preset(new PresetView { Id = p.Id ?? "", Name = p.LabelKey ?? "", Icon = p.Icon ?? "" }));
                }
            o.Set("presets", presets);
            return o;
        }

        /// <summary>contract.ts PresetView.</summary>
        public static JsonValue Preset(PresetView p)
        {
            var po = JsonValue.Obj().Set("id", p.Id ?? "").Set("name", p.Name ?? "");
            if (!string.IsNullOrEmpty(p.Icon)) po.Set("icon", IconPath(p.Icon));
            if (p.Custom) po.Set("custom", true);
            if (p.Overridden) po.Set("overridden", true);
            if (p.Custom && !string.IsNullOrEmpty(p.BasedOn)) po.Set("basedOn", p.BasedOn);
            return po;
        }

        /// <summary>contract.ts ActivePreset.</summary>
        public static JsonValue ActivePreset(ActivePresetInfo a)
        {
            a = a ?? new ActivePresetInfo();
            return JsonValue.Obj().Set("id", a.Id ?? "").Set("layer", a.Layer ?? "").Set("modified", a.Modified).Set("canSave", a.CanSave);
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
            if (!string.IsNullOrEmpty(c.DescriptionKey)) o.Set("description", c.DescriptionKey);
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
                    if (!string.IsNullOrEmpty(op.DetailKey)) oo.Set("detail", op.DetailKey);
                    if (op.Swatch != null && op.Swatch.Count > 0)
                    {
                        var sw = JsonValue.Arr();
                        foreach (var st in op.Swatch)
                        {
                            if (st == null) continue;
                            var so = JsonValue.Obj().Set("role", st.Role ?? "").Set("color", st.Color ?? "").Set("width", Round(st.Width, 1));
                            if (st.Green) so.Set("green", true);
                            if (st.Bike) so.Set("bike", true);
                            sw.Add(so);
                        }
                        oo.Set("swatch", sw);
                    }
                    opts.Add(oo);
                }
                o.Set("options", opts);
            }
            if (!string.IsNullOrEmpty(c.AssetCategory)) o.Set("assetCategory", c.AssetCategory);
            if (c.MarkerKinds != null && c.MarkerKinds.Length > 0) o.Set("markerKinds", Strings(c.MarkerKinds));
            if (!string.IsNullOrEmpty(c.VisibleWhen)) o.Set("visibleWhen", c.VisibleWhen);
            // s15: disabled-with-reason (additive): a non-applying control is shown greyed unless hideWhenOff.
            if (c.HideWhenOff) o.Set("hideWhenOff", true);
            if (!string.IsNullOrEmpty(c.EnabledWhen)) o.Set("enabledWhen", c.EnabledWhen);
            if (!string.IsNullOrEmpty(c.DisabledReasonKey)) o.Set("disabledReason", c.DisabledReasonKey);
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
            IReadOnlyDictionary<string, StepStatus> status, ComposedPlan plan, Func<string, IAreaGenerator> generators = null,
            ActivePresetInfo activePreset = null, string presetsFile = null, bool planStale = false,
            IReadOnlyDictionary<string, Dictionary<string, LocText>> unavailable = null, Func<string, string, AssetLabel> assetLabel = null)
        {
            _assetLabel = assetLabel;
            var o = JsonValue.Obj().Set("recipeId", recipeId ?? "").Set("modified", modified);
            if (activePreset != null) o.Set("activePreset", ActivePreset(activePreset));
            if (!string.IsNullOrEmpty(presetsFile)) o.Set("presetsFile", presetsFile);
            o.Set("root", recipe?.Root != null ? Step(recipe, recipe.Root, RecipePaths.Root, status, plan, planStale, 0, generators ?? GeneratorRegistry.Get, unavailable) : JsonValue.Null);
            o.Set("focus", focus ?? RecipePaths.Root);
            return o;
        }

        /// <summary>Display name / thumbnail of a prefab id (game catalog), for resolved automatic picks. Set per Design call.</summary>
        [ThreadStatic] private static Func<string, string, AssetLabel> _assetLabel;

        private static JsonValue Step(Recipe recipe, RecipeStep s, string path, IReadOnlyDictionary<string, StepStatus> status, ComposedPlan plan, bool stale, int depth,
            Func<string, IAreaGenerator> generators, IReadOnlyDictionary<string, Dictionary<string, LocText>> unavailable = null)
        {
            var o = JsonValue.Obj()
                .Set("path", path)
                .Set("layer", s.Layer ?? "")
                .Set("target", s.Target ?? "")
                .Set("enabled", s.Enabled);
            var values = JsonValue.Obj();
            var keys = new List<string>(s.Settings.Values.Keys);
            keys.Sort(StringComparer.Ordinal);
            IAreaGenerator lg = null;
            try { lg = generators?.Invoke(s.Layer); } catch (Exception) { lg = null; }
            var shown = s.Settings;
            if (lg is ILinkedControls linked)
            {
                // Derived controls (the road mix) always show what the picks are, also for old saves.
                shown = s.Settings.Clone();
                try { linked.Derive(shown); } catch (Exception) { shown = s.Settings; }
                keys = new List<string>(shown.Values.Keys);
                keys.Sort(StringComparer.Ordinal);
            }
            foreach (var k in keys) values.Set(k, DesignEditor.ToJson(shown.Values[k]));
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
            Dictionary<string, LocText> why = null;
            unavailable?.TryGetValue(path, out why);
            o.Set("features", FeatureViews(s, path, gen, generators, plan, stale, why));
            if (why != null)
            {
                var dis = JsonValue.Obj();
                var ids = new List<string>(why.Keys);
                ids.Sort(StringComparer.Ordinal);
                foreach (var id in ids) if (!id.StartsWith(Unavailable.FeaturePrefix, StringComparison.Ordinal)) dis.Set(id, why[id].ToJson());
                o.Set("disabled", dis);
            }
            StepStatus st = null;
            if (status != null) status.TryGetValue(path, out st);
            o.Set("ready", st == null || st.Ready);
            if (st != null && !st.Ready && st.NextStep != null) o.Set("nextStep", st.NextStep.ToJson());
            var children = JsonValue.Arr();
            if (depth < Composer.MaxDepth + 2)
                for (int i = 0; i < s.Children.Count; i++)
                    if (s.Children[i] != null) children.Add(Step(recipe, s.Children[i], RecipePaths.Child(path, i), status, plan, stale, depth + 1, generators, unavailable));
            o.Set("children", children);
            var overrides = JsonValue.Obj();
            if (depth < Composer.MaxDepth + 2)
            {
                var ok = new List<string>(s.RegionOverrides.Keys);
                ok.Sort(StringComparer.Ordinal);
                foreach (var k in ok)
                    if (s.RegionOverrides[k] != null)
                        overrides.Set(k, Step(recipe, s.RegionOverrides[k], RecipePaths.Override(path, s, k), status, plan, stale, depth + 1, generators, unavailable));
            }
            o.Set("overrides", overrides);
            o.Set("regions", Regions(plan, path));
            // (s16) What Automatic became, and the roads of the plan by role. A stale plan still answers
            // (best guess while the new one runs); the next push replaces it.
            var resolved = ResolvedAssets(plan, path, lg, _assetLabel, s.Settings);
            if (resolved.Count > 0) o.Set("resolvedAssets", resolved);
            // A control this layout does not use (no avenues in a plain grid) is shown greyed out with s15's
            // disabled-with-reason, still naming what Automatic would place there. An explicit pick never is.
            var notUsed = new List<string>();
            foreach (var kv in resolved.Properties)
                if (kv.Value["unused"].AsBool() && (s.Settings == null || string.IsNullOrEmpty(s.Settings.S(kv.Key, "")))) notUsed.Add(kv.Key);
            if (notUsed.Count > 0)
            {
                var dis = JsonValue.Obj();
                if (why != null)
                {
                    var wids = new List<string>(why.Keys);
                    wids.Sort(StringComparer.Ordinal);
                    foreach (var id in wids) if (!id.StartsWith(Unavailable.FeaturePrefix, StringComparison.Ordinal)) dis.Set(id, why[id].ToJson());
                }
                foreach (var id in notUsed) if (!dis.Has(id)) dis.Set(id, new LocText(NotInLayoutKey).ToJson());
                o.Set("disabled", dis);
            }
            var legend = NetLegend(plan, path, _assetLabel);
            if (legend.Count > 0) o.Set("netLegend", legend);
            return o;
        }

        /// <summary>(s16) Reason for an asset control the current layout does not use (StepView.disabled).</summary>
        public const string NotInLayoutKey = "Placemaker.UI.Why.NotInLayout";

        /// <summary>Built-in features of a step (mirrors UI FeatureView): toggle state, child path and the parent options shown under it.</summary>
        public static JsonValue FeatureViews(RecipeStep s, string path, IAreaGenerator gen, Func<string, IAreaGenerator> generators,
            ComposedPlan plan = null, bool planStale = false, IReadOnlyDictionary<string, LocText> unavailable = null)
        {
            var arr = JsonValue.Arr();
            foreach (var f in Features.Of(gen))
            {
                if (f == null || string.IsNullOrEmpty(f.Layer)) continue;
                IAreaGenerator fg = null;
                try { fg = generators?.Invoke(f.Layer); } catch (Exception) { fg = null; }
                var fo = JsonValue.Obj()
                    .Set("layer", f.Layer)
                    .Set("label", !string.IsNullOrEmpty(f.LabelKey) ? f.LabelKey : fg?.NameKey ?? f.Layer)
                    .Set("icon", IconPath(fg?.Icon ?? ""));
                if (!string.IsNullOrEmpty(f.TooltipKey)) fo.Set("tooltip", f.TooltipKey);
                int index = -1;
                for (int i = 0; i < s.Children.Count; i++)
                    if (s.Children[i] != null && s.Children[i].Layer == f.Layer && (s.Children[i].Target ?? "") == (f.Target ?? "")) { index = i; break; }
                bool on = index >= 0 && s.Children[index].Enabled;
                fo.Set("enabled", on);
                if (unavailable != null && unavailable.TryGetValue(Unavailable.Feature(f.Layer), out var no) && no != null) fo.Set("disabled", no.ToJson());
                if (index >= 0) fo.Set("path", RecipePaths.Child(path, index));
                fo.Set("options", Strings(f.Options ?? Array.Empty<string>()));
                if (f.DefaultOn) fo.Set("defaultOn", true);
                if (on)
                {
                    var fit = FeatureFit(plan, path, RecipePaths.Child(path, index), f.Layer, planStale);
                    if (fit != null) fo.Set("status", fit);
                }
                arr.Add(fo);
            }
            return arr;
        }

        /// <summary>Issues about the build itself (permanent terrain / water effects), not about whether a feature fitted.</summary>
        private static bool IsFitIssue(PlanIssue i) =>
            i != null && !string.IsNullOrEmpty(i.Key) && i.Key.IndexOf("Permanent", StringComparison.Ordinal) < 0;

        private static bool PlacedAnything(AreaPlan l) =>
            l.Objects.Count + l.Nets.Count + l.Surfaces.Count + l.Areas.Count + l.Terraform.Count + l.Paint.Count + l.Zoning.Count + l.Regions.Count > 0;

        /// <summary>
        /// Fit status of an enabled feature from the latest plan (mirrors UI FeatureView.status):
        /// "ok" = placed as asked; "reduced" = placed, but smaller / fewer / only in some regions (text says why);
        /// "failed" = nothing placed (no room, or the region it runs in was never carved out). Null while
        /// there is no run of the parent to judge by. With a stale plan only runs of the same layer count
        /// and a missing run is not judged (the next plan may simply not have run it yet).
        /// </summary>
        public static JsonValue FeatureFit(ComposedPlan plan, string parentPath, string childPath, string layer, bool planStale = false)
        {
            if (plan == null || string.IsNullOrEmpty(childPath)) return null;
            string parentPrefix = PlanPathOf(parentPath) + "@", childPrefix = PlanPathOf(childPath) + "@";
            bool parentRan = false;
            int runs = 0, failed = 0, reduced = 0;
            PlanIssue firstFail = null, firstReduced = null;
            foreach (var l in plan.Layers)
            {
                if (l?.StepPath == null) continue;
                if (l.StepPath.StartsWith(parentPrefix, StringComparison.Ordinal)) { parentRan = true; continue; }
                if (!l.StepPath.StartsWith(childPrefix, StringComparison.Ordinal) || l.GeneratorId != layer) continue;
                runs++;
                PlanIssue worst = null;
                foreach (var i in l.Issues)
                    if (IsFitIssue(i) && (worst == null || i.Severity > worst.Severity)) worst = i;
                if (!PlacedAnything(l) || (worst != null && worst.Severity == IssueSeverity.Error))
                {
                    failed++;
                    if (firstFail == null) firstFail = worst;
                }
                else if (worst != null)
                {
                    reduced++;
                    if (firstReduced == null) firstReduced = worst;
                }
            }
            if (runs == 0)
            {
                if (!parentRan || planStale) return null;
                return JsonValue.Obj().Set("kind", "failed").Set("text", new LocText(NoRoomKey).ToJson());
            }
            var o = JsonValue.Obj();
            if (failed == runs)
                o.Set("kind", "failed").Set("text", (firstFail != null ? new LocText(firstFail.Key, firstFail.Args ?? Array.Empty<object>()) : new LocText(NoRoomKey)).ToJson());
            else if (failed > 0)
                o.Set("kind", "reduced").Set("text", new LocText(SomeMissingKey, runs - failed, runs).ToJson());
            else if (reduced > 0)
                o.Set("kind", "reduced").Set("text", new LocText(firstReduced.Key, firstReduced.Args ?? Array.Empty<object>()).ToJson());
            else o.Set("kind", "ok");
            if (planStale) o.Set("stale", true);
            return o;
        }

        public const string NoRoomKey = "Placemaker.UI.Feature.NoRoom";
        public const string SomeMissingKey = "Placemaker.UI.Feature.SomeMissing";

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

        /// <summary>Layer plans a step produced (all its region runs; an override exactly its own run).</summary>
        public static IEnumerable<AreaPlan> StepLayers(ComposedPlan plan, string stepPath)
        {
            if (plan?.Layers == null || stepPath == null) yield break;
            int slash = stepPath.LastIndexOf('/');
            bool isOverride = stepPath.IndexOf('@', slash + 1) >= 0;
            string prefix = PlanPathOf(stepPath) + "@";
            foreach (var l in plan.Layers)
            {
                if (l?.StepPath == null) continue;
                if (isOverride ? l.StepPath != stepPath : !l.StepPath.StartsWith(prefix, StringComparison.Ordinal)) continue;
                yield return l;
            }
        }

        /// <summary>
        /// (s16) contract.ts StepView.resolvedAssets: per asset control, the prefab the latest plan really uses
        /// (what "Automatic" became, or the fallback of a pick this playset lacks). Elements tagged with the
        /// control (PlannedNet/Object/Surface/Area.Control) decide; untagged layers fall back to the first
        /// element of the control's category. The most used prefab wins (ties: first seen).
        /// </summary>
        public static JsonValue ResolvedAssets(ComposedPlan plan, string stepPath, IAreaGenerator gen, Func<string, string, AssetLabel> label = null,
            SettingsBag settings = null)
        {
            var o = JsonValue.Obj();
            IReadOnlyList<ControlSchema> controls = null;
            try { controls = gen?.Controls; } catch (Exception) { controls = null; }
            if (plan == null || controls == null) return o;
            var tagged = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);
            var byCat = new Dictionary<string, string>(StringComparer.Ordinal);
            var order = new List<string>();
            void Add(string control, string category, AssetRef a)
            {
                string id = a.Id;
                if (string.IsNullOrEmpty(id)) return;
                if (!string.IsNullOrEmpty(control))
                {
                    if (!tagged.TryGetValue(control, out var m)) tagged[control] = m = new Dictionary<string, int>(StringComparer.Ordinal);
                    m.TryGetValue(id, out int n);
                    if (n == 0) order.Add(control + "|" + id);
                    m[id] = n + 1;
                }
                if (!string.IsNullOrEmpty(category) && !byCat.ContainsKey(category)) byCat[category] = id;
            }
            foreach (var l in StepLayers(plan, stepPath))
            {
                foreach (var n in l.Nets) if (n != null) Add(n.Control, Build.RoleCategories.ForNet(n), n.Asset);
                foreach (var ob in l.Objects) if (ob != null) Add(ob.Control, Build.RoleCategories.ForObject(ob), ob.Asset);
                foreach (var sf in l.Surfaces) if (sf != null) Add(sf.Control, Build.RoleCategories.ForSurface(sf), sf.Asset);
                foreach (var a in l.Areas) if (a != null) Add(a.Control, Build.RoleCategories.ForArea(a.Role), a.Asset);
            }
            bool anyTagged = tagged.Count > 0;
            foreach (var c in controls)
            {
                if (c == null || c.Kind != ControlKind.Asset) continue;
                string id = null;
                if (tagged.TryGetValue(c.Id, out var m))
                {
                    int best = -1;
                    foreach (var key in order)
                    {
                        if (!key.StartsWith(c.Id + "|", StringComparison.Ordinal)) continue;
                        string cand = key.Substring(c.Id.Length + 1);
                        if (m[cand] > best) { best = m[cand]; id = cand; }
                    }
                }
                // A layer that tags its elements answers only for tagged controls (two road controls share a category).
                else if (!anyTagged && !string.IsNullOrEmpty(c.AssetCategory)) byCat.TryGetValue(c.AssetCategory, out id);
                AssetLabel al = null;
                bool unused = false;
                if (string.IsNullOrEmpty(id) && gen is IAutoAssetHints hints && (settings == null || string.IsNullOrEmpty(settings.S(c.Id, ""))))
                {
                    // Not in this plan: what Automatic would place (the first candidate the catalog knows).
                    string[] cands = null;
                    try { cands = hints.AutoCandidates(c.Id, settings); } catch (Exception) { cands = null; }
                    foreach (var cand in cands ?? Array.Empty<string>())
                    {
                        if (string.IsNullOrEmpty(cand)) continue;
                        AssetLabel cl = null;
                        try { cl = label?.Invoke(c.AssetCategory ?? "", cand); } catch (Exception) { cl = null; }
                        if (label != null && cl == null) continue;   // the game catalog lacks it
                        id = cand; al = cl; unused = true;
                        break;
                    }
                }
                if (string.IsNullOrEmpty(id)) continue;
                var ro = JsonValue.Obj().Set("id", id);
                if (unused) ro.Set("unused", true);
                else
                    try { al = label?.Invoke(c.AssetCategory ?? "", id); } catch (Exception) { al = null; }
                if (!string.IsNullOrEmpty(al?.Name)) ro.Set("name", al.Name);
                if (!string.IsNullOrEmpty(al?.Icon)) ro.Set("icon", al.Icon);
                o.Set(c.Id, ro);
            }
            return o;
        }

        /// <summary>
        /// (s16) contract.ts StepView.netLegend: the roads of a step's plan by role (street, avenue, local,
        /// roundabout, perimeter, car-free) with colour, segment count, length and main prefab — the panel's
        /// map legend. Empty when the step placed no roads.
        /// </summary>
        public static JsonValue NetLegend(ComposedPlan plan, string stepPath, Func<string, string, AssetLabel> label = null)
        {
            var arr = JsonValue.Arr();
            var nets = new List<PlannedNet>();
            foreach (var l in StepLayers(plan, stepPath)) nets.AddRange(l.Nets);
            foreach (var e in Layers.Roads.RoadRoles.Legend(nets))
            {
                var eo = JsonValue.Obj()
                    .Set("role", e.Role)
                    .Set("label", Layers.Roads.RoadRoles.LabelKey(e.Role))
                    .Set("color", Layers.Roads.RoadRoles.Color(e.Role))
                    .Set("count", e.Count)
                    .Set("lengthM", e.LengthM);
                if (!string.IsNullOrEmpty(e.Control)) eo.Set("control", e.Control);
                if (!string.IsNullOrEmpty(e.AssetId))
                {
                    eo.Set("assetId", e.AssetId);
                    AssetLabel al = null;
                    try { al = label?.Invoke(e.Role == Layers.Roads.RoadRoles.CarFree ? AssetCategories.Pathway : AssetCategories.Road, e.AssetId); } catch (Exception) { al = null; }
                    if (!string.IsNullOrEmpty(al?.Name)) eo.Set("assetName", al.Name);
                }
                arr.Add(eo);
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
            // One plain sentence per kind of change (playtest: dozens of per-patch lines were noise).
            var list = new List<LocText>();
            if (plan == null) return list;
            double ground = 0, water = 0, paint = 0;
            foreach (var l in plan.Layers)
            {
                if (l == null) continue;
                foreach (var t in l.Terraform)
                {
                    if (t == null || !t.Permanent) continue;
                    double area = Pricing.PolygonArea(t.Ring ?? Array.Empty<float3>());
                    if (float.IsNaN(t.WaterLevel)) ground += area; else water += area;
                }
                if (l.Paint != null)
                    foreach (var p in l.Paint)
                        if (p != null) paint += Pricing.PolygonArea(p.Ring ?? Array.Empty<float3>());
            }
            if (ground > 0) list.Add(new LocText("Placemaker.Permanent.Terraform", RoundArea(ground)));
            if (water > 0) list.Add(new LocText("Placemaker.Permanent.Water", RoundArea(water)));
            if (paint > 0) list.Add(new LocText("Placemaker.Permanent.Paint", RoundArea(paint)));
            return list;
        }

        private static double RoundArea(double m2) => m2 < 100 ? Math.Round(m2) : Math.Round(m2 / 10) * 10;

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

        public static JsonValue BrushRulesJson(Placemaker.Core.Geometry.BrushRules r) => JsonValue.Obj()
            .Set("roads", r.Roads).Set("buildings", r.Buildings).Set("lots", r.Lots).Set("zoned", r.Zoned)
            .Set("emptyZones", r.EmptyZones).Set("water", r.Water).Set("stayInBlock", r.StayInBlock)
            .Set("snap", Round(r.Snap, 1)).Set("smoothing", r.Smoothing);

        /// <summary>UI "setBrushRules": fields present in the JSON override <paramref name="current"/>; the result is clamped.</summary>
        public static Placemaker.Core.Geometry.BrushRules ParseBrushRules(string json, Placemaker.Core.Geometry.BrushRules current)
        {
            var r = (current ?? Placemaker.Core.Geometry.BrushRules.Defaults()).Clone();
            JsonValue v;
            try { v = JsonValue.Parse(json ?? ""); } catch (Exception) { return r; }
            if (v == null || !v.IsObject) return r;
            r.Roads = v["roads"].AsBool(r.Roads);
            r.Buildings = v["buildings"].AsBool(r.Buildings);
            r.Lots = v["lots"].AsBool(r.Lots);
            r.Zoned = v["zoned"].AsBool(r.Zoned);
            r.EmptyZones = v["emptyZones"].AsBool(r.EmptyZones);
            r.Water = v["water"].AsBool(r.Water);
            r.StayInBlock = v["stayInBlock"].AsBool(r.StayInBlock);
            r.Snap = (float)v["snap"].AsNumber(r.Snap);
            r.Smoothing = (int)Math.Round(v["smoothing"].AsNumber(r.Smoothing));
            return r.Normalized();
        }

        public static string Invariant(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);
    }
}
