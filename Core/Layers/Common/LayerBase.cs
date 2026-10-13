// Shared plumbing for park/plaza/split/add-on layers so each concrete layer only declares its
// controls, presets and planning logic. Adding a new layer type = subclass + [AreaGenerator] +
// Lang keys; nothing here needs to change.
//
// Localisation key convention (checked by tests):
//   layer name      Placemaker.<Area>.Name
//   control label   Placemaker.<Area>.<controlId>
//   control tooltip Placemaker.<Area>.<controlId>.Tooltip   (only when declared with .Tip())
//   option label    Placemaker.<Area>.<controlId>.<optionValue>
//   preset label    Placemaker.<Area>.Preset.<presetId>
//   issues          Placemaker.<Area>.Issue.<Name>  /  shared ones in Keys
//   stats           Placemaker.Stat.<Name> (shared so the composer can merge them)

using System;
using System.Collections.Generic;
using System.Threading;
using Placemaker.Core.Geometry;
using Unity.Mathematics;
using Random = Unity.Mathematics.Random;

namespace Placemaker.Core.Layers.Common
{
    public static class Keys
    {
        public const string NoAssetsIssue = "Placemaker.Common.Issue.NoAssets";
        public const string InvalidArea = "Placemaker.Common.Issue.InvalidArea";
        public const string AreaTooSmall = "Placemaker.Common.Issue.AreaTooSmall";
        public const string AreaTooLarge = "Placemaker.Common.Issue.AreaTooLarge";
        public const string SurfaceDropped = "Placemaker.Common.Issue.SurfaceDropped";
        public const string NoSpace = "Placemaker.Common.Issue.NoSpace";

        public const string StatArea = "Placemaker.Stat.Area";
        public const string StatPathLength = "Placemaker.Stat.PathLength";
        public const string StatTrees = "Placemaker.Stat.Trees";
        public const string StatBushes = "Placemaker.Stat.Bushes";
        public const string StatBenches = "Placemaker.Stat.Benches";
        public const string StatLamps = "Placemaker.Stat.Lamps";
        public const string StatBins = "Placemaker.Stat.Bins";
        public const string StatFountains = "Placemaker.Stat.Fountains";
        public const string StatPlayItems = "Placemaker.Stat.PlayItems";
        public const string StatRestrooms = "Placemaker.Stat.Restrooms";
        public const string StatPicnicTables = "Placemaker.Stat.PicnicTables";
        public const string StatFlowerbeds = "Placemaker.Stat.Flowerbeds";
        public const string StatPitches = "Placemaker.Stat.Pitches";
        public const string StatParts = "Placemaker.Stat.Parts";
        public const string StatLakeArea = "Placemaker.Stat.LakeArea";

        /// <summary>Category label keys used as issue arguments ("no {0} installed").</summary>
        public static string Category(string category) => "Placemaker.Category." + category;

        public const string UnitM2 = "m2";
        public const string UnitM = "m";
        public const string UnitCount = "count";
    }

    /// <summary>Fluent builder for a layer's control schema and randomisation ranges.</summary>
    public sealed class ControlSet
    {
        private readonly string _area;
        public readonly List<ControlSchema> List = new List<ControlSchema>();
        internal readonly Dictionary<string, double2> RandomRanges = new Dictionary<string, double2>(StringComparer.Ordinal);
        internal readonly HashSet<string> NoRandom = new HashSet<string>(StringComparer.Ordinal);
        internal readonly Dictionary<string, float> ToggleChance = new Dictionary<string, float>(StringComparer.Ordinal);

        public ControlSet(string area) { _area = area; }

        public string Key(string id) => "Placemaker." + _area + "." + id;

        private ControlSchema Add(string id, ControlKind kind, ControlTier tier, string group, object def)
        {
            var c = new ControlSchema { Id = id, Kind = kind, Tier = tier, Group = group, LabelKey = Key(id), Default = def };
            List.Add(c);
            return c;
        }

        public ControlSchema Slider(string id, double def, double min, double max, double step, string unit,
            ControlTier tier = ControlTier.Essential, string group = "")
        {
            var c = Add(id, ControlKind.Slider, tier, group, def);
            c.Min = min; c.Max = max; c.Step = step; c.Unit = unit;
            return c;
        }

        public ControlSchema Toggle(string id, bool def, ControlTier tier = ControlTier.More, string group = "") =>
            Add(id, ControlKind.Toggle, tier, group, def);

        public ControlSchema Choice(string id, string def, ControlTier tier, string group, params string[] values)
        {
            var c = Add(id, ControlKind.Choice, tier, group, def);
            foreach (var v in values) c.Options.Add(new ControlOption { Value = v, LabelKey = Key(id) + "." + v });
            return c;
        }

        public ControlSchema Asset(string id, string category, ControlTier tier = ControlTier.More, string group = "")
        {
            var c = Add(id, ControlKind.Asset, tier, group, "");
            c.AssetCategory = category;
            NoRandom.Add(id);
            return c;
        }

        public ControlSchema AssetMulti(string id, string category, ControlTier tier = ControlTier.More, string group = "")
        {
            var c = Add(id, ControlKind.AssetMulti, tier, group, Array.Empty<string>());
            c.AssetCategory = category;
            NoRandom.Add(id);
            return c;
        }

        public ControlSchema Markers(string id, ControlTier tier, params string[] kinds)
        {
            var c = Add(id, ControlKind.Markers, tier, "", null);
            c.MarkerKinds = kinds;
            NoRandom.Add(id);
            return c;
        }

        public ControlSchema Arrangement(string id, string category, string[] def, ControlTier tier = ControlTier.More, string group = "")
        {
            var c = Add(id, ControlKind.Arrangement, tier, group, def);
            c.AssetCategory = category;
            return c;
        }

        /// <summary>Restricts Randomize for a slider to a sub-range (keeps rolls tasteful).</summary>
        public void RandomRange(string id, double min, double max) => RandomRanges[id] = new double2(min, max);
        public void Fixed(string id) => NoRandom.Add(id);
        public void Chance(string id, float probabilityTrue) => ToggleChance[id] = probabilityTrue;
    }

    public static class ControlSchemaExtensions
    {
        public static ControlSchema Tip(this ControlSchema c) { c.TooltipKey = c.LabelKey + ".Tooltip"; return c; }
        public static ControlSchema When(this ControlSchema c, string condition) { c.VisibleWhen = condition; return c; }
        public static ControlSchema Cheap(this ControlSchema c) { c.Cheap = true; return c; }
        public static ControlSchema WithIcon(this ControlSchema c, string icon) { c.Icon = icon; return c; }
    }

    /// <summary>Everything a layer's planning step needs, with settings already merged over defaults.</summary>
    public sealed class LayerContext
    {
        public AreaInput Input;
        public Region Region;
        public AreaPlan Plan;
        public SettingsBag Settings;
        public CancellationToken Cancel;
        public uint Seed;

        public SiteContext Site => Input.Site;
        public float H(float2 p) => Site?.HeightAt != null ? Site.HeightAt(p) : 0f;
        public float3 P3(float2 p) => new float3(p.x, H(p), p.y);

        public float F(string id) => (float)Settings.Num(id, 0);
        public int I(string id) => Settings.I(id, 0);
        public bool B(string id) => Settings.B(id, false);
        public string S(string id) => Settings.S(id, "");
        public string[] L(string id) => Settings.L(id);

        public Random Rng(string salt) => Hashing.Rng(Seed, salt);
        public void Check() => Cancel.ThrowIfCancellationRequested();

        public void Issue(IssueSeverity severity, string key, params object[] args) =>
            Plan.Issues.Add(new PlanIssue { Severity = severity, Key = key, Args = args ?? Array.Empty<object>() });

        public void Stat(string key, double value, string unit) => Plan.Stats.Add(new PlanStat(key, value, unit));

        public PlannedObject Object(float2 p, float yaw, ObjectRole role, AssetRef asset, uint variation, int age = -1)
        {
            var o = new PlannedObject { Position = P3(p), Yaw = yaw, Role = role, Asset = asset, Variation = variation, Age = age };
            Plan.Objects.Add(o);
            return o;
        }

        /// <summary>Straight net segment (Bézier controls at thirds).</summary>
        public PlannedNet StraightNet(float2 a, float2 b, NetRole role, AssetRef asset)
        {
            var n = new PlannedNet
            {
                A = P3(a), B = P3(math.lerp(a, b, 1f / 3f)), C = P3(math.lerp(a, b, 2f / 3f)), D = P3(b),
                Role = role, Asset = asset,
            };
            Plan.Nets.Add(n);
            return n;
        }

        public PlannedNet CurvedNet(float2 a, float2 b, float2 c, float2 d, NetRole role, AssetRef asset)
        {
            var n = new PlannedNet { A = P3(a), B = P3(b), C = P3(c), D = P3(d), Role = role, Asset = asset };
            Plan.Nets.Add(n);
            return n;
        }

        /// <summary>
        /// Emits a surface only in a form CS2 keeps (SurfaceAcceptance). Returns false and adds a
        /// warning when the ring cannot be made acceptable — never emits something the game drops silently.
        /// </summary>
        public bool Surface(IReadOnlyList<float2> ring, SurfaceRole role, AssetRef asset)
        {
            var pieces = SurfaceAcceptance.MakeAcceptable(ring, H);
            int added = 0;
            foreach (var r in pieces)
            {
                if (r == null || r.Length < 3 || !SurfaceAcceptance.IsAccepted(Poly.To2D(r))) continue;
                Plan.Surfaces.Add(new PlannedSurface { Ring = r, Role = role, Asset = asset });
                added++;
            }
            if (added == 0) Issue(IssueSeverity.Warning, Keys.SurfaceDropped);
            return added > 0;
        }

        public bool NavigationArea(IReadOnlyList<float2> ring, AreaRole role, AssetRef asset)
        {
            var pieces = SurfaceAcceptance.MakeAcceptable(ring, H);
            int added = 0;
            foreach (var r in pieces)
            {
                if (r == null || r.Length < 3 || !SurfaceAcceptance.IsAccepted(Poly.To2D(r))) continue;
                Plan.Areas.Add(new PlannedArea { Ring = r, Role = role, Asset = asset });
                added++;
            }
            if (added == 0) Issue(IssueSeverity.Warning, Keys.SurfaceDropped);
            return added > 0;
        }

        public void AddRegion(IReadOnlyList<float2> ring, string key, params string[] tags)
        {
            var clean = Poly.Clean(ring, 0.05f);
            if (clean.Length < 3) return;
            Plan.Regions.Add(new PlannedRegion { Ring = Poly.To3D(clean, H), Key = key, Tags = tags });
        }
    }

    public abstract class LayerBase : IAreaGenerator
    {
        private readonly List<ControlSchema> _controls;
        private readonly List<PresetInfo> _presets = new List<PresetInfo>();
        private readonly ControlSet _set;

        protected LayerBase()
        {
            _set = new ControlSet(Area);
            DefineControls(_set);
            _controls = _set.List;
            DefinePresets();
        }

        /// <summary>Localisation area ("Park", "Plaza", "AddOn.Fountain"…).</summary>
        protected abstract string Area { get; }
        public abstract string Id { get; }
        public virtual string NameKey => "Placemaker." + Area + ".Name";
        public abstract string Icon { get; }
        public abstract LayerKind Kind { get; }
        public virtual string[] Accepts => Array.Empty<string>();
        public virtual string[] Emits => Array.Empty<string>();
        public virtual int Version => 1;
        public virtual float MinArea => 50f;
        public virtual float MaxArea => 4_000_000f;

        public IReadOnlyList<ControlSchema> Controls => _controls;
        public IReadOnlyList<PresetInfo> Presets => _presets;

        protected abstract void DefineControls(ControlSet c);
        protected virtual void DefinePresets() { }

        /// <summary>Declares a preset; values not given keep the layer defaults.</summary>
        protected void Preset(string id, params (string key, object value)[] values)
        {
            var p = new PresetInfo { Id = id, LabelKey = "Placemaker." + Area + ".Preset." + id, Values = Defaults() };
            foreach (var (k, v) in values) p.Values.Values[k] = v;
            _presets.Add(p);
        }

        public ControlSchema Control(string id)
        {
            foreach (var c in _controls) if (c.Id == id) return c;
            return null;
        }

        public SettingsBag Defaults()
        {
            var b = new SettingsBag();
            foreach (var c in _controls)
            {
                if (c.Kind == ControlKind.Markers || c.Default == null) continue;
                b.Values[c.Id] = c.Default is string[] a ? (string[])a.Clone() : c.Default;
            }
            return b;
        }

        /// <summary>Defaults overlaid with the given values (unknown ids are kept, wrong types fall back).</summary>
        public SettingsBag Merge(SettingsBag values)
        {
            var b = Defaults();
            if (values == null) return b;
            foreach (var kv in values.Values)
            {
                var c = Control(kv.Key);
                if (c == null) { b.Values[kv.Key] = kv.Value; continue; }
                if (TypeMatches(c, kv.Value)) b.Values[kv.Key] = kv.Value is string[] a ? (string[])a.Clone() : kv.Value;
            }
            return b;
        }

        private static bool TypeMatches(ControlSchema c, object v)
        {
            switch (c.Kind)
            {
                case ControlKind.Slider: return v is IConvertible && !(v is string) && !(v is bool);
                case ControlKind.Toggle: return v is bool;
                case ControlKind.Choice: return v is string s && c.Options.Exists(o => o.Value == s);
                case ControlKind.Asset: return v is string;
                case ControlKind.AssetMulti:
                case ControlKind.Arrangement: return v is string[];
                default: return false;
            }
        }

        public virtual Readiness CheckReady(AreaInput input) => new Readiness();

        public virtual List<EdgeMarker> SuggestMarkers(AreaInput input) => new List<EdgeMarker>();

        /// <summary>
        /// Rolls every unlocked, randomisable control from its own seeded stream (so locking one
        /// control never changes what the others roll). Locked ids are copied unchanged — the fix
        /// for ParkManager's "Variant overwrites explicit choices".
        /// </summary>
        public virtual SettingsBag Randomize(SettingsBag current, uint seed, ICollection<string> locked)
        {
            var result = Merge(current);
            foreach (var c in _controls)
            {
                if (locked != null && locked.Contains(c.Id)) continue;
                if (_set.NoRandom.Contains(c.Id)) continue;
                var rng = Hashing.Rng(seed, Id + "|" + c.Id);
                if (RandomizeControl(c, ref rng, result)) continue;
                switch (c.Kind)
                {
                    case ControlKind.Slider:
                    {
                        var range = _set.RandomRanges.TryGetValue(c.Id, out var r) ? r : new double2(c.Min, c.Max);
                        double step = c.Step > 0 ? c.Step : 1;
                        int steps = (int)Math.Floor((range.y - range.x) / step + 1e-9);
                        double v = range.x + step * rng.NextInt(0, steps + 1);
                        result.Values[c.Id] = Math.Min(c.Max, Math.Max(c.Min, Math.Round(v / step) * step));
                        break;
                    }
                    case ControlKind.Toggle:
                    {
                        float chance = _set.ToggleChance.TryGetValue(c.Id, out var p) ? p : 0.5f;
                        result.Values[c.Id] = rng.NextFloat() < chance;
                        break;
                    }
                    case ControlKind.Choice:
                        if (c.Options.Count > 0) result.Values[c.Id] = c.Options[rng.NextInt(0, c.Options.Count)].Value;
                        break;
                }
            }
            return result;
        }

        /// <summary>Layer-specific roll for one control. Return true when handled.</summary>
        protected virtual bool RandomizeControl(ControlSchema control, ref Random rng, SettingsBag into) => false;

        public AreaPlan Plan(AreaInput input, CancellationToken cancel)
        {
            var plan = new AreaPlan { GeneratorId = Id };
            if (input == null) { plan.Issues.Add(new PlanIssue { Severity = IssueSeverity.Error, Key = Keys.InvalidArea }); return plan; }
            cancel.ThrowIfCancellationRequested();
            // A bad selected area blocks the build (Error). A bad or tiny sub-region (one lawn, one split
            // part) must not block the whole recipe: that layer just skips it with a Warning.
            var tags = input.RegionTags;
            var severity = tags == null || tags.Length == 0 || Array.IndexOf(tags, RegionTags.Root) >= 0 ? IssueSeverity.Error : IssueSeverity.Warning;
            var region = Common.Region.From(input.Shape?.Ring);
            if (region == null)
            {
                plan.Issues.Add(new PlanIssue { Severity = severity, Key = Keys.InvalidArea });
                return plan;
            }
            if (region.Area < MinArea)
            {
                plan.Issues.Add(new PlanIssue { Severity = severity, Key = Keys.AreaTooSmall, Args = new object[] { Math.Round(region.Area), MinArea } });
                return plan;
            }
            if (region.Area > MaxArea)
            {
                plan.Issues.Add(new PlanIssue { Severity = severity, Key = Keys.AreaTooLarge, Args = new object[] { Math.Round(region.Area), MaxArea } });
                return plan;
            }
            var ctx = new LayerContext
            {
                Input = input, Region = region, Plan = plan, Settings = Merge(input.Settings),
                Cancel = cancel, Seed = Hashing.Mix(input.Seed == 0 ? 1u : input.Seed, Seeds.Hash(Id)),
            };
            PlanCore(ctx);
            return plan;
        }

        protected abstract void PlanCore(LayerContext ctx);
    }
}
