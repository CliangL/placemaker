using System.Collections.Generic;
using System.Threading;
using Placemaker.Core;
using Placemaker.Core.Layers.AddOns;
using Placemaker.Core.Layers.Park;
using Placemaker.Core.Layers.Plaza;
using Placemaker.Core.Tool;
using Xunit;

namespace Placemaker.Tests
{
    public class ToolFeatureTests
    {
        /// <summary>Fill layer with one DefaultOn feature, delegating everything else to the fake generator.</summary>
        private sealed class FeatureHost : IAreaGenerator, IHasFeatures
        {
            private readonly ToolFakeGenerator _inner = new ToolFakeGenerator { Id = "host" };
            public IReadOnlyList<FeatureInfo> Features { get; } = new List<FeatureInfo>
            {
                new FeatureInfo { Layer = "playground", Target = "core", DefaultOn = true },
                new FeatureInfo { Layer = "fountain", Target = "" },
            };
            public string Id => "host";
            public string NameKey => _inner.NameKey;
            public string Icon => "";
            public LayerKind Kind => LayerKind.Fill;
            public string[] Accepts => _inner.Accepts;
            public string[] Emits => _inner.Emits;
            public int Version => 1;
            public IReadOnlyList<ControlSchema> Controls => _inner.Controls;
            public IReadOnlyList<PresetInfo> Presets => _inner.Presets;
            public SettingsBag Defaults() => _inner.Defaults();
            public float MinArea => 10;
            public float MaxArea => 1e7f;
            public Readiness CheckReady(AreaInput input) => new Readiness();
            public List<EdgeMarker> SuggestMarkers(AreaInput input) => new List<EdgeMarker>();
            public SettingsBag Randomize(SettingsBag current, uint seed, ICollection<string> locked) => current.Clone();
            public AreaPlan Plan(AreaInput input, CancellationToken cancel) => new AreaPlan();
        }

        private readonly Dictionary<string, IAreaGenerator> _gens = new Dictionary<string, IAreaGenerator>
        {
            ["park"] = new ParkLayer(),
            ["plaza"] = new PlazaLayer(),
            ["playground"] = new PlaygroundAddOn(),
            ["fountain"] = new FountainAddOn(),
            ["restrooms"] = new RestroomsAddOn(),
            ["host"] = new FeatureHost(),
        };

        private DesignEditor Editor() => new DesignEditor(id => id != null && _gens.TryGetValue(id, out var g) ? g : null);

        [Fact]
        public void SetFeatureAddsAndRemovesChildStep()
        {
            var e = Editor();
            var r = e.SingleLayerRecipe(_gens["park"], 1);
            Assert.True(e.SetFeature(r, "0", "playground", true, 5));
            Assert.Single(r.Root.Children);
            Assert.Equal("playground", r.Root.Children[0].Layer);
            Assert.Equal(RegionTags.Core, r.Root.Children[0].Target);
            Assert.Equal("medium", r.Root.Children[0].Settings.S("size", ""));
            Assert.False(e.SetFeature(r, "0", "playground", true, 6));    // already on
            Assert.False(e.SetFeature(r, "0", "nonsense", true, 6));      // not a feature of park
            Assert.True(e.SetFeature(r, "0", "restrooms", true, 7));
            Assert.True(e.SetFeature(r, "0", "playground", false, 0));
            Assert.Single(r.Root.Children);
            Assert.Equal("restrooms", r.Root.Children[0].Layer);
            Assert.False(e.SetFeature(r, "0", "playground", false, 0));
        }

        [Fact]
        public void FeaturePresetIsAppliedOverLayerDefaults()
        {
            var e = Editor();
            var r = e.SingleLayerRecipe(_gens["plaza"], 1);
            Assert.True(e.SetFeature(r, "0", "fountain", true, 2));
            Assert.Equal("large", r.Root.Children[0].Settings.S("size", ""));
            Assert.Equal("", r.Root.Children[0].Target);
        }

        [Fact]
        public void DisabledFeatureChildIsReEnabledNotDuplicated()
        {
            var e = Editor();
            var r = e.SingleLayerRecipe(_gens["park"], 1);
            e.SetFeature(r, "0", "playground", true, 2);
            r.Root.Children[0].Enabled = false;
            Assert.True(e.SetFeature(r, "0", "playground", true, 3));
            Assert.Single(r.Root.Children);
            Assert.True(r.Root.Children[0].Enabled);
        }

        [Fact]
        public void DefaultOnFeaturesAreAddedOnce()
        {
            var e = Editor();
            var r = e.SingleLayerRecipe(_gens["host"], 1);
            Assert.True(e.AddDefaultFeatures(r, () => 9u));
            Assert.Single(r.Root.Children);
            Assert.Equal("playground", r.Root.Children[0].Layer);
            Assert.False(e.AddDefaultFeatures(r, () => 9u));
        }

        [Fact]
        public void StepViewPublishesFeatures()
        {
            var e = Editor();
            var r = e.SingleLayerRecipe(_gens["park"], 1);
            e.SetFeature(r, "0", "restrooms", true, 2);
            var json = UiModels.Design(r, "park", false, "0", null, null, id => _gens.TryGetValue(id ?? "", out var g) ? g : null);
            var features = json["root"]["features"];
            Assert.NotNull(features);
            bool sawRestrooms = false, sawPlayground = false;
            foreach (var f in features.Items)
            {
                if (f["layer"].AsString() == "restrooms") { sawRestrooms = true; Assert.True(f["enabled"].AsBool()); Assert.Equal("0/0", f["path"].AsString()); }
                if (f["layer"].AsString() == "playground") { sawPlayground = true; Assert.False(f["enabled"].AsBool()); }
            }
            Assert.True(sawRestrooms && sawPlayground);
        }

        [Fact]
        public void MakeRowShowsSingleTypesOnly()
        {
            var e = Editor();
            System.Func<string, IAreaGenerator> res = id => _gens.TryGetValue(id ?? "", out var g) ? g : null;
            var plain = e.SingleLayerRecipe(_gens["park"], 1);
            Assert.True(Features.ShowInMakeRow(plain, false, res));
            var withFeature = plain.Clone();
            e.SetFeature(withFeature, "0", "playground", true, 2);
            Assert.False(Features.ShowInMakeRow(withFeature, false, res));
            Assert.True(Features.ShowInMakeRow(withFeature, true, res));      // player recipes always listed
            var addOnRoot = e.SingleLayerRecipe(_gens["playground"], 1);
            Assert.False(Features.ShowInMakeRow(addOnRoot, false, res));
        }
    }
}
