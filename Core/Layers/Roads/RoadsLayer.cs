// Placemaker roads layer ("roads"): street patterns from Grid Road Generator (Ti4goc) behind the
// IAreaGenerator contract. Emits PlannedNets (with connections to existing roads), cul-de-sac caps,
// roundabout islands, and the city blocks between the roads as "block" regions plus frontage
// strips as "edge" regions, so recipes like "roads + parks" or "roads + zoning" compose.

using System;
using System.Collections.Generic;
using System.Threading;
using Placemaker.Core.Geometry;
using Placemaker.Core.Layers.Roads.Generators;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Roads
{
    [AreaGenerator(400, Group = "roads")]
    public sealed class RoadsLayer : IAreaGenerator, IHasFeatures
    {
        public const string LayerId = "roads";

        private readonly List<ControlSchema> _controls;
        private readonly List<PresetInfo> _presets;

        public RoadsLayer()
        {
            _controls = RoadsSchema.Build();
            _presets = BuildPresets();
        }

        public string Id => LayerId;
        public string NameKey => "Placemaker.Roads.Name";
        public string Icon => "Media/Game/Icons/Roads.svg";
        public LayerKind Kind => LayerKind.Fill;
        public string[] Accepts => Array.Empty<string>();
        public string[] Emits => new[] { RegionTags.Block, RegionTags.Edge, RegionTags.Core };
        public int Version => 1;

        private static readonly FeatureInfo[] FeatureList =
        {
            new FeatureInfo
            {
                Layer = AddOns.EdgeZoningAddOn.LayerId, Target = RegionTags.Edge, LabelKey = "Placemaker.Roads.Feature.edgeZoning",
                TooltipKey = "Placemaker.Roads.Feature.edgeZoning.Tooltip",
            },
        };

        public IReadOnlyList<FeatureInfo> Features => FeatureList;

        public IReadOnlyList<ControlSchema> Controls => _controls;
        public IReadOnlyList<PresetInfo> Presets => _presets;
        public SettingsBag Defaults() => RoadsSchema.Defaults(_controls);

        /// <summary>A few city blocks at least; GRG refused free areas under 2000 m².</summary>
        public float MinArea => 2000f;
        /// <summary>16 km²: the largest painted districts GRG's sweep covers stay interactive.</summary>
        public float MaxArea => 16_000_000f;

        public Readiness CheckReady(AreaInput input)
        {
            var r = new Readiness();
            var ring = input?.Shape?.Ring;
            if (ring == null || ring.Length < 3)
            {
                r.Ready = false;
                r.ReasonKey = "Placemaker.Roads.Reason.NoArea";
                return r;
            }
            var settings = input.Settings ?? new SettingsBag();
            if (RoadsSchema.ParsePattern(settings.S(RoadsIds.Pattern, "grid")) == RoadPattern.Contour)
            {
                Func<float2, float> h = input.Site?.HeightAt;
                if (h == null || GridGenerator.IsTerrainFlat(ring, h))
                {
                    r.Ready = false;
                    r.ReasonKey = RoadsPlanner.IssueFlat;
                }
            }
            return r;
        }

        public List<EdgeMarker> SuggestMarkers(AreaInput input) => new List<EdgeMarker>();

        /// <summary>
        /// Varies the current pattern's geometry (spacings, angle, curviness, variation) within the
        /// control ranges. The pattern itself, assets, upgrades and network options are the player's
        /// choices and stay untouched.
        /// </summary>
        public SettingsBag Randomize(SettingsBag current, uint seed, ICollection<string> locked)
        {
            var result = current?.Clone() ?? Defaults();
            var rng = Seeds.Rng(seed, "roads.randomize");
            string pattern = result.S(RoadsIds.Pattern, "grid");
            foreach (var c in _controls)
            {
                if (c.Kind != ControlKind.Slider || c.Group != "layout") continue;
                if (locked != null && locked.Contains(c.Id)) continue;
                if (c.VisibleWhen != "pattern=" + pattern) continue;
                double steps = Math.Floor((c.Max - c.Min) / c.Step);
                double v = c.Min + c.Step * Math.Floor(rng.NextDouble() * (steps + 1));
                if (c.Unit == "deg") v = Math.Round(rng.NextDouble() * 30 - 15); // keep grids roughly aligned to the area
                result.Values[c.Id] = Math.Max(c.Min, Math.Min(c.Max, v));
            }
            return result;
        }

        public AreaPlan Plan(AreaInput input, CancellationToken cancel)
        {
            if (input == null) return new AreaPlan();
            return RoadsPlanner.Plan(input, _controls, cancel);
        }

        private List<PresetInfo> BuildPresets()
        {
            PresetInfo P(string id, params (string k, object v)[] values)
            {
                var b = Defaults();
                foreach (var (k, v) in values) b.Values[k] = v;
                return new PresetInfo { Id = id, LabelKey = "Placemaker.Roads.Preset." + id, Values = b };
            }
            return new List<PresetInfo>
            {
                P("downtown", (RoadsIds.Pattern, "grid"), (RoadsIds.GridSpacing, 100.0), (RoadsIds.AvenueColumn, true), (RoadsIds.AvenueRow, true),
                    (RoadsIds.StreetTrees, true)),
                P("suburb", (RoadsIds.Pattern, "tree"), (RoadsIds.CapStyle, "grass")),
                P("village", (RoadsIds.Pattern, "organic"), (RoadsIds.OrganicCurviness, 70.0), (RoadsIds.OrganicLoops, 40.0)),
                P("hillside", (RoadsIds.Pattern, "contour")),
                P("garden-city", (RoadsIds.Pattern, "radial"), (RoadsIds.RadialRings, 2.0), (RoadsIds.AvenueMedianTrees, true)),
            };
        }
    }
}
