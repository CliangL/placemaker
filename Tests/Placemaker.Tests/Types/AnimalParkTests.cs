// animalPark: shared farm checks plus enclosures, walks, arena, pond and spawners.

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Placemaker.Core;
using Placemaker.Core.Build;
using Placemaker.Core.Geometry;
using Placemaker.Core.Layers.Common;
using Placemaker.Core.Layers.Types.AnimalPark;
using Unity.Mathematics;
using Xunit;
using Xunit.Abstractions;

namespace Placemaker.Tests.Types
{
    [Collection(Placemaker.Tests.TimingCollection.Name)]
    public class AnimalParkTests : FarmTypeTestBase
    {
        public AnimalParkTests(ITestOutputHelper output) : base(output) { }
        protected override string TypeId => AnimalParkLayer.LayerId;
        protected override string Folder => "AnimalPark";

        private AreaPlan Preset(string id, float3[] ring = null) =>
            PlanPreset(Gen.Presets.Single(p => p.Id == id), ring ?? FiveHa, 5, TestKit.RoadAlongBottom(300));

        private static List<float2[]> Pens(AreaPlan plan) =>
            plan.Regions.Where(r => r.Tags.Contains(AnimalParkLayer.EnclosureTag) && r.Key.StartsWith("pen:")).Select(r => Poly.To2D(r.Ring)).ToList();

        [Fact]
        public void PresetsBuildFencedPensWithShelterTroughAndAnimals()
        {
            foreach (var p in Gen.Presets)
            {
                var plan = Preset(p.Id);
                var pens = Pens(plan);
                Out.WriteLine($"{p.Id}: {pens.Count} pens, {plan.Objects.Count} objects, {plan.Nets.Count(n => n.Role == NetRole.Fence)} fence courses, {plan.Terraform.Count} terraform");
                Assert.True(pens.Count >= System.Math.Min(2, (double)p.Values.Num("pens", 1)), p.Id + " has " + pens.Count + " pens");
                Assert.True(plan.Nets.Count(n => n.Role == NetRole.Fence) >= pens.Count * 3, p.Id + ": pens are not fenced");
                Assert.Contains(plan.Nets, n => n.Role == NetRole.Pathway || n.Role == NetRole.PathwayWide);
                var spawners = plan.Objects.Where(o => o.Role == ObjectRole.AnimalSpawner).ToList();
                Assert.NotEmpty(spawners);
                Assert.All(spawners, s => Assert.Contains(pens, pen => Poly.Contains(pen, s.Position.xz)));
                // No walk runs through a pen.
                foreach (var n in plan.Nets.Where(n => n.Role == NetRole.Pathway))
                    foreach (var pen in pens)
                        Assert.False(Poly.Contains(pen, (n.A.xz + n.D.xz) * 0.5f) && Poly.DistanceToBoundary(pen, (n.A.xz + n.D.xz) * 0.5f) > 2f, p.Id + ": a walk crosses a pen");
            }
            Assert.Contains(Preset("pettingFarm").Objects, o => o.Category == AssetCategories.AnimalParkShelter);
            Assert.Contains(Preset("paddocks").Objects, o => o.Category == AssetCategories.AnimalParkTrough);
        }

        [Fact]
        public void SubtypesLookDifferent()
        {
            var horses = Preset("horsePaddock");
            Assert.Equal(1.0, horses.Facts["arena"]);
            Assert.True(horses.Objects.Count(o => o.Category == AssetCategories.AnimalParkJump) >= 4);
            var deer = Preset("deerPark");
            Assert.True(deer.Facts.ContainsKey("pondArea"));
            Assert.Contains(deer.Terraform, t => !float.IsNaN(t.WaterLevel));
            Assert.True(deer.Objects.Count(o => o.Role == ObjectRole.Tree) > 2 * Preset("paddocks").Objects.Count(o => o.Role == ObjectRole.Tree));
            Assert.True(Preset("aviaryGarden").Objects.Count(o => o.Category == AssetCategories.AnimalParkAviary) >= 6);
            Assert.True(Pens(Preset("pettingFarm")).Count >= 5);
        }

        [Fact]
        public void PondStaysInsideOnePen()
        {
            var plan = Preset("pettingFarm");
            var lake = plan.Terraform.Single(t => !float.IsNaN(t.WaterLevel));
            var c = Poly.Centroid(Poly.To2D(lake.Ring));
            var pen = Pens(plan).Single(r => Poly.Contains(r, c));
            foreach (var v in lake.Ring) Assert.True(Poly.Contains(pen, v.xz), "pond pokes out of its pen");
        }

        [Fact]
        public void MissingCategoriesDegradeToInfo()
        {
            var none = new Dictionary<string, int>
            {
                [AssetCategories.AnimalParkAviary] = 0, [AssetCategories.AnimalParkJump] = 0, [AssetCategories.AnimalParkTrough] = 0,
                [AssetCategories.AnimalParkShelter] = 0, [AssetCategories.CityFarmBarn] = 0, [AssetCategories.GardenShed] = 0, [AssetCategories.Shelter] = 0,
            };
            using (AssetAvailability.Scope(none))
                foreach (var id in new[] { "horsePaddock", "aviaryGarden", "pettingFarm" })
                {
                    var plan = Preset(id);
                    Assert.False(plan.HasErrors);
                    Assert.Contains(plan.Issues, i => i.Key == Keys.NoAssetsIssue && i.Severity == IssueSeverity.Info);
                    Assert.DoesNotContain(plan.Objects, o => none.ContainsKey(o.Category ?? ""));
                    Assert.NotEmpty(Pens(plan));
                }
        }

        [Theory]
        [InlineData("HorseStable01", AssetCategories.AnimalParkShelter)]
        [InlineData("RabbitHutch02", AssetCategories.AnimalParkShelter)]
        [InlineData("ChickenCoop", AssetCategories.AnimalParkShelter)]
        [InlineData("WaterTrough01", AssetCategories.AnimalParkTrough)]
        [InlineData("HayBale03", AssetCategories.AnimalParkHay)]
        [InlineData("GardenAviary01", AssetCategories.AnimalParkAviary)]
        [InlineData("ShowJump01", AssetCategories.AnimalParkJump)]
        public void Classification(string name, string category)
        {
            var t = AssetTraits.StaticObject | AssetTraits.HasMesh | AssetTraits.HasIcon;
            Assert.Contains(category, AssetHeuristics.Classify(name, t));
            Assert.True(AssetHeuristics.AutoEligible(category, name, t), name + " should be automatic for " + category);
        }

        [Fact]
        public void DogJumpsStayDogAgility()
        {
            var t = AssetTraits.StaticObject | AssetTraits.HasMesh | AssetTraits.HasIcon;
            Assert.DoesNotContain(AssetCategories.AnimalParkJump, AssetHeuristics.Classify("DogParkAgilityJump01", t));
        }
    }
}
