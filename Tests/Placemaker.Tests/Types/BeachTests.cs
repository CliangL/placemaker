// Beach type: presets, water side detection, carving, styles, features, localisation.

using System.Collections.Generic;
using System.Linq;
using System.Threading;
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
    [Collection(Placemaker.Tests.TimingCollection.Name)]
    public class BeachTests
    {
        private readonly ITestOutputHelper _out;
        public BeachTests(ITestOutputHelper output) { _out = output; }

        public static IEnumerable<object[]> PresetIds() => TestKit.Gen("beach").Presets.Select(p => new object[] { p.Id });
        private static PresetInfo Preset(string id) => TestKit.Gen("beach").Presets.Single(p => p.Id == id);

        [Fact]
        public void HasFourToSixPresetsAndDeepControls()
        {
            var g = TestKit.Gen("beach");
            Assert.InRange(g.Presets.Count, 4, 6);
            Assert.InRange(g.Controls.Count, 15, 42);
            Assert.InRange(g.Controls.Select(c => c.Group).Distinct().Count(), 4, 10);
            Assert.Equal("water", GeneratorGroups.Of(g));
            Assert.Contains(g.Controls, c => c.Id == "style" && c.Kind == ControlKind.Choice);
        }

        [Theory]
        [MemberData(nameof(PresetIds))]
        public void PresetPlansOneHectareAndSixHectareL(string preset)
        {
            foreach (var (ring, site) in new[] { (OneHa, SeaSouth(0.05f)), (SixHaL, SeaSouth(0.03f)), (OneHa, new SiteContext()) })
            {
                var plan = PlanPreset("beach", Preset(preset), ring, site);
                Assert.False(plan.HasErrors, preset + ": " + string.Join(",", plan.Issues.Select(i => i.Key)));
                Assert.True(plan.Objects.Count > 15, preset + " placed only " + plan.Objects.Count);
                Assert.NotEmpty(plan.Nets);
                AssertInside(plan, ring);
                AssertNothingOnPaths(plan);
                TestKit.AssertSurfacesAccepted(plan);
                AssertTerrainBlended(plan, ring, site, p => p.y, 4f);
                // Deterministic.
                var again = PlanPreset("beach", Preset(preset), ring, site);
                Assert.Equal(TestKit.Fingerprint(plan), TestKit.Fingerprint(again));
            }
        }

        [Theory]
        [MemberData(nameof(PresetIds))]
        public void PresetRespectsReserved(string preset)
        {
            var reserved = SomeReserved();
            var plan = PlanPreset("beach", Preset(preset), OneHa, SeaSouth(), 7, reserved);
            TestKit.AssertClearOfReserved(plan, reserved);
        }

        [Fact]
        public void FastOnFiveHectares()
        {
            foreach (var p in TestKit.Gen("beach").Presets)
            {
                long ms = Time(() => PlanPreset("beach", p, FiveHa, SeaSouth(0.04f)));
                _out.WriteLine($"{p.Id}: {ms} ms");
                Assert.True(ms < TimingBudget.Ms(250), $"beach/{p.Id} took {ms} ms for 5 ha");
            }
        }

        [Fact]
        public void FindsTheSeaAndLaysOutBandsFromIt()
        {
            var plan = PlanPreset("beach", Preset("sandy"), FiveHa, SeaSouth(0.02f));
            Assert.DoesNotContain(plan.Issues, i => i.Key == "Placemaker.Shore.Issue.NoWater");
            Assert.Equal(250, plan.Facts["shoreLength"], 0);
            // Back path parallel to the water near the beach depth (60 m since the art pass).
            var paths = plan.Nets.Where(n => n.Role == NetRole.Pathway).ToList();
            Assert.Contains(paths, n => math.abs(n.A.z - 60f) < 2f && math.abs(n.D.z - 60f) < 2f && math.abs(n.A.x - n.D.x) > 5f);
            // Umbrellas and lifeguards on the sand between the water and the path; huts behind it.
            var umbrellas = plan.Objects.Where(o => o.Category == AssetCategories.BeachUmbrella).ToList();
            Assert.NotEmpty(umbrellas);
            Assert.All(umbrellas, o => Assert.InRange(o.Position.z, 2f, 60f));
            Assert.All(plan.Objects.Where(o => o.Category == AssetCategories.BeachLifeguard), o => Assert.InRange(o.Position.z, 4f, 15f));
            Assert.All(plan.Objects.Where(o => o.Category == AssetCategories.BeachHut), o => Assert.True(o.Position.z > 60f));
            // Umbrellas face the sea (yaw ≈ π: towards −Z).
            Assert.All(umbrellas, o => Assert.True(math.cos(o.Yaw) < -0.7f, "umbrella yaw " + o.Yaw));
            // Showers at the bottom of the beach accesses.
            Assert.Contains(plan.Objects, o => o.Category == AssetCategories.BeachShower);
            Assert.True(plan.Facts["accessPaths"] >= 2);
        }

        [Fact]
        public void WaterSideChoiceAndRoadFallback()
        {
            // No water: away from the road along the bottom → sea at the top.
            var input = TestKit.Input(FiveHa, Preset("sandy").Values.Clone(), 3, null, TestKit.RoadAlongBottom(250));
            var plan = TestKit.Plan("beach", input);
            Assert.Contains(plan.Issues, i => i.Key == "Placemaker.Shore.Issue.NoWater" && i.Severity == IssueSeverity.Info);
            Assert.All(plan.Objects.Where(o => o.Category == AssetCategories.BeachUmbrella), o => Assert.True(o.Position.z > 200f - 61f));
            // Explicit east.
            var east = Preset("sandy").Values.Clone();
            east.Values["waterSide"] = "east";
            plan = TestKit.Plan("beach", TestKit.Input(FiveHa, east, 3));
            Assert.DoesNotContain(plan.Issues, i => i.Key == "Placemaker.Shore.Issue.NoWater");
            var u = plan.Objects.Where(o => o.Category == AssetCategories.BeachUmbrella).ToList();
            Assert.NotEmpty(u);
            Assert.All(u, o => Assert.True(o.Position.x > 250f - 61f));
        }

        [Fact]
        public void WaterInsideTheAreaIsLeftAlone()
        {
            var site = WaterInside(60f);
            var plan = PlanPreset("beach", Preset("sandy"), FiveHa, site);
            Assert.False(plan.HasErrors);
            Assert.Equal((double)(int)Placemaker.Core.Layers.Types.Beach.ShoreMode.Inside, plan.Facts["shoreMode"]);
            Assert.All(plan.Objects, o => Assert.False(site.WaterDepthAt(o.Position.xz) > 0f, $"{o.Category} in the water at {o.Position.xz}"));
            Assert.All(plan.Nets, n => Assert.True(n.A.z > 58f && n.D.z > 58f, "net in the water"));
            Assert.Contains(plan.Objects, o => o.Category == AssetCategories.BeachUmbrella && o.Position.z < 95f);
        }

        [Fact]
        public void CarvesAGentleSlopeIntoABankAndRaisesDunes()
        {
            var site = SeaSouth(0.2f);   // a 20 % bank rising from the water
            var plan = PlanPreset("beach", Preset("sandy"), FiveHa, site);
            var lowered = plan.Terraform.Where(t => t.Kind == TerraformKind.Lower).ToList();
            var raised = plan.Terraform.Where(t => t.Kind == TerraformKind.Raise).ToList();
            Assert.NotEmpty(lowered);
            Assert.NotEmpty(raised);   // dunes
            // Beach cells sit on a gentle plane: at most ~6 % + slack above the waterline.
            foreach (var t in lowered)
            {
                var c = Poly.Centroid(Poly.To2D(t.Ring));
                if (c.y < 26f && c.x > 30f && c.x < 220f) Assert.True(t.TargetHeight <= 0.06f * c.y + 0.8f, $"beach at {c} is {t.TargetHeight:0.00} m high");
            }
            // Dunes behind the back path.
            Assert.All(raised, t => Assert.True(Poly.Centroid(Poly.To2D(t.Ring)).y > 30f));
            AssertTerrainBlended(plan, FiveHa, site, p => p.y, 4f);
            // Carving off: no lowering.
            var keep = Preset("sandy").Values.Clone();
            keep.Values["carve"] = false;
            var flat = TestKit.Gen("beach").Plan(TestKit.Input(FiveHa, keep, 5, null, site), CancellationToken.None);
            Assert.DoesNotContain(flat.Terraform, t => t.TargetHeight < site.HeightAt(Poly.Centroid(Poly.To2D(t.Ring))) - 0.01f);
        }

        [Fact]
        public void StylesLookDifferent()
        {
            AreaPlan P(string preset) => PlanPreset("beach", Preset(preset), FiveHa, SeaSouth(0.03f));
            var rocky = P("rocky");
            Assert.True(rocky.Objects.Count(o => o.Category == AssetCategories.Rock) > 30);
            Assert.True(rocky.Facts["tidePools"] >= 2);
            Assert.Contains(rocky.Terraform, t => t.Kind == TerraformKind.Lower);
            Assert.DoesNotContain(rocky.Surfaces, s => s.Category == AssetCategories.SurfaceSand);
            Assert.NotEmpty(rocky.Paint);

            var volley = P("volleyball");
            Assert.True(volley.Objects.Count(o => o.Category == AssetCategories.BeachVolleyball) >= 3);

            var cove = P("cove");
            Assert.Contains(cove.Objects, o => o.Category == AssetCategories.Rock && o.Position.x < 25f);
            Assert.Contains(cove.Objects, o => o.Category == AssetCategories.Rock && o.Position.x > 225f);
            Assert.True(cove.Facts["firePits"] >= 1, "stone fire rings");   // built from small rocks: no vanilla fire pit
            Assert.True(cove.Objects.Count(o => o.Role == ObjectRole.Tree) > P("sandy").Objects.Count(o => o.Role == ObjectRole.Tree));

            var river = P("river");
            Assert.Contains(river.Objects, o => o.Category == AssetCategories.BeachBoat);
            Assert.Contains(river.Surfaces, s => s.Role == SurfaceRole.Grass);

            var resort = P("resort");
            Assert.DoesNotContain(resort.Objects, o => o.Category == AssetCategories.BeachHut);   // no vanilla beach hut (pool = a 40 m building)
            Assert.Contains(resort.Surfaces, s => s.Role == SurfaceRole.Grass);
            Assert.True(resort.Objects.Count(o => o.Category == AssetCategories.BeachUmbrella) > P("sandy").Objects.Count(o => o.Category == AssetCategories.BeachUmbrella));

            var sandy = P("sandy");
            Assert.Contains(sandy.Nets, n => n.Role == NetRole.Fence);   // sand fences on the dunes
            Assert.True(sandy.Objects.Count(o => o.Role == ObjectRole.Bush) > 50, "dune grass");
        }

        [Fact]
        public void MissingCategoriesDegradeToInfo()
        {
            var none = new Dictionary<string, int>
            {
                [AssetCategories.BeachUmbrella] = 0, [AssetCategories.CafeSeating] = 0, [AssetCategories.BeachLifeguard] = 0, [AssetCategories.Lookout] = 0,
                [AssetCategories.BeachShower] = 0, [AssetCategories.BeachHut] = 0, [AssetCategories.GardenShed] = 0, [AssetCategories.BeachVolleyball] = 0,
                [AssetCategories.Sports] = 0, [AssetCategories.SurfaceSand] = 0,
            };
            using (AssetAvailability.Scope(none))
                foreach (var preset in new[] { "sandy", "volleyball", "resort" })
                {
                    var plan = PlanPreset("beach", Preset(preset), FiveHa, SeaSouth());
                    Assert.False(plan.HasErrors);
                    Assert.Contains(plan.Issues, i => i.Key == Keys.NoAssetsIssue && i.Severity == IssueSeverity.Info);
                    Assert.DoesNotContain(plan.Objects, o => none.ContainsKey(o.Category ?? ""));
                    Assert.DoesNotContain(plan.Surfaces, s => s.Category == AssetCategories.SurfaceSand);
                    Assert.NotEmpty(plan.Nets);
                }
        }

        [Fact]
        public void EveryFeatureComposes()
        {
            var features = Features.Of(TestKit.Gen("beach")).Select(f => f.Layer).ToArray();
            Assert.Contains("beachBar", features);
            var plan = Compose(With("beach", features), FiveHa, SeaSouthRoadNorth(250, 200));
            Assert.True(string.IsNullOrEmpty(Errors(plan)), Errors(plan));
            foreach (var l in plan.Layers) AssertInside(l, FiveHa, 1.2f);
            foreach (var f in features) Assert.Contains(plan.Layers, l => l.GeneratorId == f);
            Assert.Contains(plan.Layers[0].Regions, r => r.Tags.Contains(RegionTags.Core));
            var bar = plan.Layers.Single(l => l.GeneratorId == "beachBar");
            Assert.Contains(bar.Objects, o => o.Category == AssetCategories.Kiosk);
        }

        [Fact]
        public void LabelsRecipeAndClassification()
        {
            AssertLabels("beach");
            AssertLabels("beachBar");
            AssertSourceKeys("Beach");
            AssertRecipe("beach");
            Assert.False(GeneratorRegistry.IsHidden(TestKit.Gen("beach")));
            Assert.True(GeneratorRegistry.IsHidden(TestKit.Gen("beachBar")));
            Assert.Contains(AssetCategories.BeachLounger, Placemaker.Core.Build.AssetHeuristics.Classify("SunLounger01", Placemaker.Core.Build.AssetTraits.StaticObject | Placemaker.Core.Build.AssetTraits.HasMesh));
            Assert.Contains(AssetCategories.BeachLifeguard, Placemaker.Core.Build.AssetHeuristics.Classify("LifeguardTower01", Placemaker.Core.Build.AssetTraits.StaticObject | Placemaker.Core.Build.AssetTraits.HasMesh));
            var parasol = Placemaker.Core.Build.AssetHeuristics.Classify("BeachUmbrella02", Placemaker.Core.Build.AssetTraits.StaticObject | Placemaker.Core.Build.AssetTraits.HasMesh);
            Assert.Contains(AssetCategories.BeachUmbrella, parasol);
        }
    }
}
