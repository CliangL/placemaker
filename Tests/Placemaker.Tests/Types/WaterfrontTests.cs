// Waterfront type: presets, promenade along the water, railings, steps, piers over water, styles.

using System.Collections.Generic;
using System.Linq;
using Placemaker.Core;
using Placemaker.Core.Geometry;
using Placemaker.Core.Layers.Common;
using Placemaker.Core.Tool;
using Unity.Mathematics;
using Xunit;
using Xunit.Abstractions;
using static Placemaker.Tests.Types.WaterTypesKit;

namespace Placemaker.Tests.Types
{
    public class WaterfrontTests
    {
        private readonly ITestOutputHelper _out;
        public WaterfrontTests(ITestOutputHelper output) { _out = output; }

        public static IEnumerable<object[]> PresetIds() => TestKit.Gen("waterfront").Presets.Select(p => new object[] { p.Id });
        private static PresetInfo Preset(string id) => TestKit.Gen("waterfront").Presets.Single(p => p.Id == id);

        [Fact]
        public void HasFourToSixPresetsAndDeepControls()
        {
            var g = TestKit.Gen("waterfront");
            Assert.InRange(g.Presets.Count, 4, 6);
            Assert.InRange(g.Controls.Count, 15, 40);
            Assert.InRange(g.Controls.Select(c => c.Group).Distinct().Count(), 4, 10);
            Assert.Equal("water", GeneratorGroups.Of(g));
        }

        [Theory]
        [MemberData(nameof(PresetIds))]
        public void PresetPlansOneHectareAndSixHectareL(string preset)
        {
            foreach (var (ring, site) in new[] { (OneHa, SeaSouth(0.05f)), (SixHaL, SeaSouth(0.03f)), (OneHa, new SiteContext()), (FiveHa, WaterInside(50f)) })
            {
                var plan = PlanPreset("waterfront", Preset(preset), ring, site);
                Assert.False(plan.HasErrors, preset + ": " + string.Join(",", plan.Issues.Select(i => i.Key)));
                Assert.True(plan.Objects.Count > 10, preset + " placed only " + plan.Objects.Count);
                Assert.Contains(plan.Nets, n => n.Role == NetRole.Pathway || n.Role == NetRole.PathwayWide);
                AssertInside(plan, ring);
                AssertNothingOnPaths(plan);
                TestKit.AssertSurfacesAccepted(plan);
                AssertTerrainBlended(plan, ring, site, p => math.abs(p.y - (ring == FiveHa ? 50f : 0f)), 10f);
                var again = PlanPreset("waterfront", Preset(preset), ring, site);
                Assert.Equal(TestKit.Fingerprint(plan), TestKit.Fingerprint(again));
            }
        }

        [Theory]
        [MemberData(nameof(PresetIds))]
        public void PresetRespectsReserved(string preset)
        {
            var reserved = SomeReserved();
            var plan = PlanPreset("waterfront", Preset(preset), OneHa, SeaSouth(), 7, reserved);
            TestKit.AssertClearOfReserved(plan, reserved);
        }

        [Fact]
        public void FastOnFiveHectares()
        {
            foreach (var p in TestKit.Gen("waterfront").Presets)
                foreach (var site in new[] { SeaSouth(0.04f), WaterInside(50f) })
                {
                    long ms = Time(() => PlanPreset("waterfront", p, FiveHa, site));
                    _out.WriteLine($"{p.Id}: {ms} ms");
                    Assert.True(ms < 250, $"waterfront/{p.Id} took {ms} ms for 5 ha");
                }
        }

        [Fact]
        public void PromenadeRunsAlongTheWaterWithRailingsBenchesAndLamps()
        {
            var plan = PlanPreset("waterfront", Preset("promenade"), FiveHa, SeaSouth(0.02f));
            // Walk parallel to the edge, 6–7 m in.
            Assert.Contains(plan.Nets, n => n.Role == NetRole.PathwayWide && math.abs(n.A.z - n.D.z) < 1f && n.A.z > 5f && n.A.z < 8f);
            // Railings hug the edge, with gaps (the step flights).
            var rails = plan.Nets.Where(n => n.Role == NetRole.Fence).ToList();
            Assert.True(rails.Count >= 3);
            Assert.All(rails, n => Assert.InRange(n.A.z, 0.3f, 1.2f));
            Assert.True(rails.Sum(n => math.distance(n.A.xz, n.D.xz)) < 245f, "railings leave openings");
            // Benches between railing and walk face the water; a tree row behind.
            var benches = plan.Objects.Where(o => o.Role == ObjectRole.Bench).ToList();
            Assert.True(benches.Count >= 8);
            Assert.All(benches.Where(b => b.Position.z < 5f), b => Assert.True(math.cos(b.Yaw) < -0.7f));
            Assert.Contains(plan.Objects, o => o.Role == ObjectRole.Lamp && o.Position.z < 3f);
            Assert.Contains(plan.Objects, o => o.Role == ObjectRole.Tree && o.Position.z > 12f && o.Position.z < 16f);
            Assert.Equal(2, plan.Facts["steps"]);
            Assert.Contains(plan.Terraform, t => t.Kind == TerraformKind.Level);
            Assert.Contains(plan.Issues, i => i.Key == "Placemaker.Waterfront.Issue.PiersNeedWater");
        }

        [Fact]
        public void PiersReachOutOverWaterInsideTheArea()
        {
            var site = WaterInside(60f);
            var plan = PlanPreset("waterfront", Preset("pier"), FiveHa, site);
            Assert.Equal(3, plan.Facts["piers"]);
            var decks = plan.Nets.Where(n => !n.FollowTerrain).ToList();
            Assert.True(decks.Count >= 3 + 2, "piers with T-heads");
            Assert.All(decks, n => Assert.True(n.A.y > 0f && n.D.y > 0f, "deck above the water"));
            Assert.Contains(decks, n => site.WaterDepthAt(n.D.xz) > 0f && n.D.z < 30f);
            // Nothing placed in the water.
            Assert.All(plan.Objects, o => Assert.False(site.WaterDepthAt(o.Position.xz) > 0f, $"{o.Role} in the water at {o.Position.xz}"));
            AssertInside(plan, FiveHa);
        }

        [Fact]
        public void StylesLookDifferent()
        {
            AreaPlan P(string preset) => PlanPreset("waterfront", Preset(preset), FiveHa, SeaSouth(0.03f));
            var harbor = P("harbor");
            Assert.True(harbor.Objects.Count(o => o.Category == AssetCategories.WaterfrontMooring) > 15);
            Assert.DoesNotContain(harbor.Nets, n => n.Role == NetRole.Fence);
            Assert.Contains(harbor.Objects, o => o.Category == AssetCategories.WaterfrontNautical);
            Assert.True(harbor.Facts["kiosks"] >= 2);

            var steps = P("riverSteps");
            Assert.True(steps.Facts["steps"] >= 8, "ghats along the whole edge");
            Assert.True(steps.Terraform.Count(t => t.Kind == TerraformKind.Level) >= 24);
            Assert.True(steps.Objects.Count(o => o.Role == ObjectRole.Tree) > P("promenade").Objects.Count(o => o.Role == ObjectRole.Tree));

            var fishing = P("fishing");
            Assert.True(fishing.Objects.Count(o => o.Category == AssetCategories.WaterfrontFishing) >= 8);
            Assert.True(fishing.Objects.Count(o => o.Category == AssetCategories.Rock) > 25, "riprap edge");
            Assert.Contains(fishing.Surfaces, s => s.Category == AssetCategories.SurfaceNatural);
        }

        [Fact]
        public void WaterSideChoice()
        {
            var west = Preset("promenade").Values.Clone();
            west.Values["waterSide"] = "west";
            var plan = TestKit.Plan("waterfront", TestKit.Input(FiveHa, west, 3));
            var rails = plan.Nets.Where(n => n.Role == NetRole.Fence).ToList();
            Assert.NotEmpty(rails);
            Assert.All(rails, n => Assert.InRange(n.A.x, 0.3f, 1.2f));
        }

        [Fact]
        public void MissingCategoriesDegradeToInfo()
        {
            var none = new Dictionary<string, int>
            {
                [AssetCategories.WaterfrontMooring] = 0, [AssetCategories.Bollard] = 0, [AssetCategories.WaterfrontNautical] = 0, [AssetCategories.Decoration] = 0,
                [AssetCategories.WaterfrontFishing] = 0, [AssetCategories.Kiosk] = 0, [AssetCategories.Rock] = 0,
            };
            using (AssetAvailability.Scope(none))
                foreach (var preset in new[] { "harbor", "fishing" })
                {
                    var plan = PlanPreset("waterfront", Preset(preset), FiveHa, SeaSouth());
                    Assert.False(plan.HasErrors);
                    Assert.Contains(plan.Issues, i => i.Key == Keys.NoAssetsIssue && i.Severity == IssueSeverity.Info);
                    Assert.DoesNotContain(plan.Objects, o => none.ContainsKey(o.Category ?? ""));
                    Assert.NotEmpty(plan.Nets);
                }
        }

        [Fact]
        public void EveryFeatureComposes()
        {
            var features = Features.Of(TestKit.Gen("waterfront")).Select(f => f.Layer).ToArray();
            var plan = Compose(With("waterfront", features), FiveHa, SeaSouthRoadNorth(250, 200));
            Assert.True(string.IsNullOrEmpty(Errors(plan)), Errors(plan));
            foreach (var l in plan.Layers) AssertInside(l, FiveHa, 1.2f);
            foreach (var f in features) Assert.Contains(plan.Layers, l => l.GeneratorId == f);
            Assert.Contains(plan.Layers[0].Regions, r => r.Tags.Contains(RegionTags.Core));
        }

        [Fact]
        public void LabelsAndRecipe()
        {
            AssertLabels("waterfront");
            AssertSourceKeys("Waterfront");
            AssertRecipe("waterfront");
            Assert.False(GeneratorRegistry.IsHidden(TestKit.Gen("waterfront")));
            Assert.Contains(AssetCategories.WaterfrontMooring, Placemaker.Core.Build.AssetHeuristics.Classify("MooringBollard01", Placemaker.Core.Build.AssetTraits.StaticObject | Placemaker.Core.Build.AssetTraits.HasMesh));
        }
    }
}
