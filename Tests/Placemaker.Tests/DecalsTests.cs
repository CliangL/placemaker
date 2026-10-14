// s21 decal overhaul: decals are a complement of every feature, take no space, and are dressed by the
// composer (Core/Decals). docs/DECALS.md has the census and the rules.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Placemaker.Core;
using Placemaker.Core.Build;
using Placemaker.Core.Decals;
using Placemaker.Core.Geometry;
using Placemaker.Core.Layers.Common;
using Placemaker.Core.Tool;
using Unity.Mathematics;
using Xunit;
using Xunit.Abstractions;

namespace Placemaker.Tests
{
    public class DecalsTests
    {
        private readonly ITestOutputHelper _out;
        public DecalsTests(ITestOutputHelper output) { _out = output; }

        private static readonly DesignEditor Editor = new DesignEditor(GeneratorRegistry.Get);

        private static Recipe Recipe(string layer, uint seed = 7, Action<Recipe> edit = null)
        {
            var r = Editor.SingleLayerRecipe(GeneratorRegistry.Get(layer), seed);
            Editor.ApplyDefaults(r);
            uint n = 0;
            Editor.AddDefaultFeatures(r, () => ++n);
            edit?.Invoke(r);
            uint k = 0;
            foreach (var kv in RecipePaths.Walk(r)) kv.Value.Seed = 1000u + (++k);
            return r;
        }

        private static ComposedPlan Compose(Recipe r, float3[] ring, SiteContext site = null)
        {
            site = site ?? new SiteContext();
            TestKit.Entered(r, ring, site);
            return Composer.Compose(r, new AreaShape { Ring = ring, Source = AreaSource.Drawn }, site, CancellationToken.None);
        }

        private static List<PlannedObject> Decals(ComposedPlan p, bool functional = false) =>
            p.Layers.SelectMany(l => l.Objects).Where(o => o.Role == ObjectRole.Decal && DecalCatalog.IsFunctionalCategory(o.Category) == functional).ToList();

        private static float3[] Site => TestKit.Rect(120, 100);

        // ───────────────────────── census / classifier ─────────────────────────

        [Theory]
        [InlineData("G87 Stains and Puddles Decals Wet Pack Stains G87 Oil 1 Decal", DecalFamily.Oil)]
        [InlineData("G87 Stains and Puddles Decals Wet Pack Puddles G87 Puddle Mirror 17 Band A Wet Sand Decal", DecalFamily.WetSand)]
        [InlineData("G87 Stains and Puddles Decals Wet Pack Puddles G87 Puddle Mirror 11 Band A Decal", DecalFamily.Puddle)]
        [InlineData("G87 Stains and Puddles Decals Wet Pack Puddles G87 Puddle Mirror 20 Wet Track Long Decal", DecalFamily.Tyre)]
        [InlineData("G87 Stains and Puddles Decals Wet Pack Stains G87 Stain 42 Track Short Decal", DecalFamily.Tyre)]
        [InlineData("G87 Stains and Puddles Decals Wet Pack Puddles G87 Water Splash 81 A M Decal", DecalFamily.Wet)]
        [InlineData("G87 Trash Decals Dirty Pack Trash G87 Debris 15 Glass Spreaded Decal", DecalFamily.Glass)]
        [InlineData("G87 Trash Decals Dirty Pack Trash G87 Debris 01 Decal", DecalFamily.Rubble)]
        [InlineData("G87 Trash Decals Dirty Pack Trash G87 Seaweed 01 Brown Decal", DecalFamily.BeachWrack)]
        [InlineData("G87 Trash Decals Dirty Pack Trash G87 Litter 05 Papers 01 Large Decal", DecalFamily.Litter)]
        [InlineData("G87 Trash Decals Dirty Pack Leaf G87 Leaf 01 Decal", DecalFamily.LeafLitter)]
        [InlineData("Fallen leaves decals Leaf Dead Leaves1 Yellow lined Decal", DecalFamily.LeafEdge)]
        [InlineData("Fallen leaves decals Leaf Dead Leaves7 Brown scattered Decal", DecalFamily.LeafLitter)]
        [InlineData("Ground Texture Decal Pack Ground LargeMulch01 Decal", DecalFamily.Mulch)]
        [InlineData("ground texture decal pack ground largemulch01 decal", DecalFamily.Mulch)]       // classification plug-ins see lower case
        [InlineData("Ground Texture Decal Pack Ground ScatteredRocks01 Decal", DecalFamily.Rocks)]
        [InlineData("Ground Texture Decal Pack Ground Moss01 Decal", DecalFamily.Moss)]
        [InlineData("Ground Texture Decal Pack Ground Dirt02 Decal", DecalFamily.Dirt)]
        [InlineData("Sand Surfaces and Beach Decals Beach Palm-leaf-01 Decal", DecalFamily.PalmLitter)]
        [InlineData("Sand Surfaces and Beach Decals Beach Tire-Track-01 Decal", DecalFamily.Tyre)]
        [InlineData("Sand Surfaces and Beach Decals Beach Footprint-01-Left Decal", DecalFamily.Footprint)]
        [InlineData("Asphalt Patches Decal Pack RoadAssets Asphalt Patch Dark - 3 Decal", DecalFamily.Patch)]
        [InlineData("Cracks and Damage Decal Pack RoadAssets Crack - Small 1 Decal", DecalFamily.Crack)]
        [InlineData("G87 Dirty Roads Stains G87 Dirt Road Transition 01 Decal", DecalFamily.Mud)]
        [InlineData("RealVision Decals Ground Texture125 - Oil Decal", DecalFamily.Oil)]
        [InlineData("RealVision Decals Ground Texture154 - Castle Wall Decal", DecalFamily.BuildingOnly)]
        [InlineData("RealVision Decals Ground Graff - 03 Decal", DecalFamily.Graffiti)]
        [InlineData("Manicured Grass Texture Pack Ground Manicured Grass - Stripes Large 12m Decal", DecalFamily.LawnPattern)]
        [InlineData("ParkingLotDecal01", DecalFamily.ParkingStall)]
        [InlineData("EU RoadArrow Forward", DecalFamily.Marking)]
        [InlineData("AlphabetDecalA01", DecalFamily.Marking)]
        [InlineData("TennisCourtDecal01", DecalFamily.SportsMarking)]
        [InlineData("PlaygroundDecal02", DecalFamily.SportsMarking)]
        [InlineData("ChirperDecalSmall01", DecalFamily.Logo)]
        public void CensusFamiliesFromItemWords(string name, DecalFamily family) =>
            Assert.Equal(family, DecalCatalog.Describe(name).Family);

        [Fact]
        public void ShapesComeFromRealFootprints()
        {
            Assert.Equal(DecalShape.Strip, DecalCatalog.Describe("G87 Stains and Puddles Decals Wet Pack Puddles G87 Puddle Mirror 11 Band A Decal", 2, 6).Shape);
            Assert.Equal(DecalShape.Blob, DecalCatalog.Describe("G87 Stains and Puddles Decals Wet Pack Stains G87 Oil 1 Decal", 3, 3).Shape);
            Assert.Equal(DecalShape.Directional, DecalCatalog.Describe("Sand Surfaces and Beach Decals Beach Tire-Track-01 Decal", 2, 2).Shape);
            Assert.Equal(DecalShape.Sheet, DecalCatalog.Describe("RealVision Decals Ground Texture20 - Dirt Decal", 40, 40).Shape);
            Assert.Equal(DecalShape.Tiny, DecalCatalog.Describe("G87 Trash Decals Dirty Pack Trash G87 Debris 12 Glass Bottle Green Decal", 0.7f, 0.7f).Shape);
            Assert.Equal(DecalOrientation.AlongHost, DecalCatalog.Describe("G87 Trash Decals Dirty Pack Trash G87 Seaweed 01 Brown Decal", 8, 4).Orientation);
            Assert.True(DecalCatalog.Describe("ParkingLotDecal01").Functional);
            Assert.False(DecalCatalog.Describe("Fallen leaves decals Leaf Dead Leaves2 Yellow light Decal").Functional);
        }

        [Fact]
        public void DressingPoolsClassifyAndAreEligible()
        {
            const AssetTraits decal = AssetTraits.StaticObject | AssetTraits.HasMesh | AssetTraits.DecalMesh;
            var oil = AssetHeuristics.Classify("G87 Stains and Puddles Decals Wet Pack Stains G87 Oil 2 Decal", decal);
            Assert.Contains(DecalPools.Oil, oil);
            Assert.True(AssetHeuristics.AutoEligible(DecalPools.Oil, "G87 Stains and Puddles Decals Wet Pack Stains G87 Oil 2 Decal", decal));
            var lined = AssetHeuristics.Classify("Fallen leaves decals Leaf Dead Leaves3 Red lined Decal", decal);
            Assert.Contains(DecalPools.LeavesEdge, lined);
            // Building-only and functional decals never join a dressing pool.
            Assert.DoesNotContain(AssetHeuristics.Classify("RealVision Decals Ground Texture161 - Castle Window Decal", decal), c => DecalPools.IsDressingPool(c));
            Assert.DoesNotContain(AssetHeuristics.Classify("ParkingIconDecal01", decal | AssetTraits.Vanilla), c => DecalPools.IsDressingPool(c));
            // A 0.3 m tyre-curve bit is no tyre mark; 30 m stains are no stains.
            Assert.False(AssetCuration.FitsSize(DecalPools.Tyre, 0.3f));
            Assert.True(AssetCuration.FitsSize(DecalPools.Tyre, 6f));
            Assert.False(AssetCuration.FitsSize(DecalPools.Stain, 20f));
            // Variety: decals of a pool mix per object.
            Assert.False(AutoAssets.IsUniform(DecalPools.Oil));
        }

        // ───────────────────────── decals take no space ─────────────────────────

        [Fact]
        public void DeclutterNeverDropsOrMovesDecals()
        {
            var plan = new ComposedPlan();
            var l = new AreaPlan();
            plan.Layers.Add(l);
            l.Objects.Add(new PlannedObject { Position = new float3(10, 0, 10), Role = ObjectRole.Bench, Asset = new AssetRef("Bench") });
            for (int i = 0; i < 20; i++)
                l.Objects.Add(new PlannedObject { Position = new float3(10 + i * 0.1f, 0, 10), Role = ObjectRole.Decal, Asset = new AssetRef("Leaves"), Category = AssetCategories.DecalLeaves });
            l.Nets.Add(new PlannedNet { A = new float3(0, 0, 10), B = new float3(7, 0, 10), C = new float3(14, 0, 10), D = new float3(20, 0, 10), Role = NetRole.Pathway, Asset = new AssetRef("Path") });
            var assets = new Dictionary<string, AssetInfo>
            {
                ["Bench"] = new AssetInfo { Id = "Bench", BoundsMin = new float3(-1, 0, -0.4f), BoundsMax = new float3(1, 1, 0.4f) },
                ["Leaves"] = new AssetInfo { Id = "Leaves", BoundsMin = new float3(-3, -0.5f, -3), BoundsMax = new float3(3, 0.5f, 3) },
                ["Path"] = new AssetInfo { Id = "Path", NetWidth = 3f },
            };
            var before = l.Objects.Where(o => o.Role == ObjectRole.Decal).Select(o => o.Position).ToList();
            var report = Declutter.Run(plan, assets);
            var after = l.Objects.Where(o => o.Role == ObjectRole.Decal).Select(o => o.Position).ToList();
            Assert.Equal(before, after);
            Assert.False(report.Dropped.ContainsKey(ObjectRole.Decal));
            Assert.DoesNotContain(plan.Issues, i => i.Key == Declutter.IssueKey(ObjectRole.Decal));
            Assert.False(Declutter.Participates(ObjectRole.Decal));
        }

        [Fact]
        public void ObjectBudgetNeitherCountsNorThinsDecals()
        {
            var plan = new AreaPlan();
            for (int i = 0; i < 5000; i++)
                plan.Objects.Add(new PlannedObject { Position = new float3(i % 100, 0, i / 100), Role = ObjectRole.Decal, Category = AssetCategories.DecalLeaves });
            for (int i = 0; i < 100; i++)
                plan.Objects.Add(new PlannedObject { Position = new float3(i, 0, 0), Role = ObjectRole.Tree });
            int removed = ObjectBudget.Enforce(plan, 10000f, ObjectBudget.DefaultPerHectare);
            Assert.Equal(0, removed);
            Assert.Equal(5000, plan.Objects.Count(o => o.Role == ObjectRole.Decal));
        }

        [Fact]
        public void ComposerNeverReservesDecalsAndNeverReportsNoRoom()
        {
            // A decals feature in a park packed with objects still places decals, with no "no room" issue.
            var r = Recipe("park", 3, x => Editor.SetFeature(x, RecipePaths.Root, "decals", true, 99));
            var step = RecipePaths.Walk(r).First(kv => kv.Value.Layer == "decals").Value;
            step.Settings.Values["theme"] = "cracks";   // no paving in a lawn park: falls back to any dry ground
            var p = Compose(r, Site);
            Assert.DoesNotContain(p.AllIssues(), i => i.Key.Contains("NoGround") || i.Key == Declutter.IssueKey(ObjectRole.Decal) || i.Key == Keys.NoSpace);
            var addon = p.Layers.First(l => l.GeneratorId == "decals");
            Assert.Contains(addon.Objects, o => o.Role == ObjectRole.Decal);
            var reserved = new ReservedSpace();
            foreach (var l in p.Layers) Composer.Reserve(l, reserved);
            foreach (var o in Decals(p))
                Assert.DoesNotContain(reserved.Discs, d => math.distancesq(d.Center, o.Position.xz) < 1e-6f && d.Tag == ObjectRole.Decal.ToString());
        }

        // ───────────────────────── the dressing pass ─────────────────────────

        [Theory]
        [InlineData("park")]
        [InlineData("plaza")]
        [InlineData("parking")]
        [InlineData("abandonedLot")]
        [InlineData("constructionSite")]
        [InlineData("sportsPark")]
        public void DressingAddsDecalsAndIsDeterministic(string type)
        {
            var a = Compose(Recipe(type), Site);
            var b = Compose(Recipe(type), Site);
            var da = Decals(a); var db = Decals(b);
            _out.WriteLine(type + ": " + da.Count + " decorative decals: " + string.Join(", ", da.GroupBy(o => o.Category).Select(g => g.Key + " " + g.Count())));
            Assert.True(da.Count > 10, type + " has only " + da.Count + " decals");   // s23: few, deliberate
            Assert.Equal(da.Count, db.Count);
            for (int i = 0; i < da.Count; i++)
            {
                Assert.Equal(da[i].Position, db[i].Position);
                Assert.Equal(da[i].Yaw, db[i].Yaw);
                Assert.Equal(da[i].Category, db[i].Category);
                Assert.Equal(da[i].Variation, db[i].Variation);
            }
        }

        [Fact]
        public void DensityScalesWithTheSliders()
        {
            int Count(double master, string family = null, double familyAmount = 100) =>
                Decals(Compose(Recipe("park", 7, r =>
                {
                    r.Root.Settings.Values[DressingControls.AmountId] = master;
                    if (family != null) r.Root.Settings.Values[family] = familyAmount;
                }), Site)).Count;
            int half = Count(50), normal = Count(100), lots = Count(250);
            _out.WriteLine($"50% {half}, 100% {normal}, 250% {lots}");
            Assert.True(half < normal && normal < lots, $"{half} < {normal} < {lots}");
            int noTrees = Count(100, DressingControls.FamilyId(DressFamily.Trees), 0);
            Assert.True(noTrees < normal);
        }

        [Fact]
        public void MasterZeroRemovesDecorativeDecalsButKeepsFunctionalOnes()
        {
            // Sports park: pitch line markings are functional decals; the decals feature adds decorative ones.
            var off = Compose(Recipe("sportsPark", 5, r =>
            {
                Editor.SetFeature(r, RecipePaths.Root, "decals", true, 99);
                r.Root.Settings.Values[DressingControls.AmountId] = 0.0;
            }), Site);
            var on = Compose(Recipe("sportsPark", 5, r => Editor.SetFeature(r, RecipePaths.Root, "decals", true, 99)), Site);
            Assert.Empty(Decals(off));
            Assert.NotEmpty(Decals(on));
            Assert.Equal(Decals(on, true).Count, Decals(off, true).Count);
            Assert.Equal(on.Layers.Sum(l => l.Objects.Count(o => o.Role == ObjectRole.ParkingBay)), off.Layers.Sum(l => l.Objects.Count(o => o.Role == ObjectRole.ParkingBay)));
        }

        [Fact]
        public void ParkingOilAlignsWithTheBayAndTyreMarksWithTheAisle()
        {
            var p = Compose(Recipe("parking", 7, r => r.Root.Settings.Values[DressingControls.StyleId] = "gritty"), Site);   // tyre marks are Gritty only (s23)
            var lot = p.Layers.First(l => l.GeneratorId == "parking");
            var bays = lot.Objects.Where(o => o.Role == ObjectRole.ParkingBay).ToList();
            var oil = lot.Objects.Where(o => o.Role == ObjectRole.Decal && o.Category == DecalPools.Oil).ToList();
            Assert.True(oil.Count > bays.Count / 5, $"{oil.Count} oil stains for {bays.Count} bays");
            foreach (var o in oil)
            {
                var bay = bays.OrderBy(b => math.distancesq(b.Position.xz, o.Position.xz)).First();
                float d = AngleBetweenAxes(o.Yaw, bay.Yaw);
                Assert.True(d < math.radians(12f), $"oil at {o.Position.xz} is {math.degrees(d):0}° off its bay");
            }
            var aisles = lot.Nets.Where(n => n.Role == NetRole.DriveAisle || n.Role == NetRole.DriveConnector).ToList();
            var tyres = lot.Objects.Where(o => o.Role == ObjectRole.Decal && o.Category == DecalPools.Tyre).ToList();
            Assert.NotEmpty(tyres);
            int aligned = 0;
            foreach (var t in tyres)
            {
                float best = float.MaxValue;
                foreach (var n in aisles) best = math.min(best, AngleBetweenAxes(t.Yaw, math.atan2(n.D.x - n.A.x, n.D.z - n.A.z)));
                foreach (var b in bays) if (math.distancesq(b.Position.xz, t.Position.xz) < 16f) best = math.min(best, AngleBetweenAxes(t.Yaw, b.Yaw));
                if (best < math.radians(10f)) aligned++;
            }
            Assert.True(aligned == tyres.Count, $"{aligned}/{tyres.Count} tyre marks along an aisle or bay");
        }

        [Fact]
        public void PathEdgeLeavesFollowThePath()
        {
            var p = Compose(Recipe("park", 7, r => r.Root.Settings.Values[DressingControls.StyleId] = "gritty"), Site);   // edge drifts are Gritty only (s23)
            int n = 0;
            foreach (var l in p.Layers)
                foreach (var o in l.Objects.Where(o => o.Role == ObjectRole.Decal && o.Category == DecalPools.LeavesEdge))
                {
                    var (dist, tangent) = NearestPath(p, o.Position.xz);
                    if (dist > 6f) continue;   // hedge-side drifts
                    float d = AngleBetweenAxes(o.Yaw, math.atan2(tangent.x, tangent.y));
                    Assert.True(d < math.radians(15f), $"edge leaves at {o.Position.xz} {math.degrees(d):0}° off the path");
                    n++;
                }
            Assert.True(n > 0);
        }

        [Fact]
        public void DecalFitTurnsLongXPrefabsAlongThePlannedAxis()
        {
            var plan = new ComposedPlan();
            var l = new AreaPlan();
            plan.Layers.Add(l);
            float yaw = 0.7f;
            var band = new PlannedObject { Role = ObjectRole.Decal, Category = DecalPools.Tyre, Asset = new AssetRef("LongZ"), Yaw = yaw };
            var tar = new PlannedObject { Role = ObjectRole.Decal, Category = DecalPools.Tyre, Asset = new AssetRef("LongX"), Yaw = yaw };
            var mark = new PlannedObject { Role = ObjectRole.Decal, Category = AssetCategories.DecalMarkings, Asset = new AssetRef("LongX"), Yaw = yaw };
            l.Objects.AddRange(new[] { band, tar, mark });
            var assets = new Dictionary<string, AssetInfo>
            {
                ["LongZ"] = new AssetInfo { BoundsMin = new float3(-1, -0.5f, -3), BoundsMax = new float3(1, 0.5f, 3) },
                ["LongX"] = new AssetInfo { BoundsMin = new float3(-5, -0.5f, -2.5f), BoundsMax = new float3(5, 0.5f, 2.5f) },
            };
            Assert.Equal(1, DecalFit.Orient(plan, assets));
            var want = new float2(math.sin(yaw), math.cos(yaw));
            foreach (var o in new[] { band, tar })
            {
                var info = assets[o.Asset.Id];
                var axis = DecalFit.LongAxis(o.Yaw, info.BoundsMin, info.BoundsMax);
                Assert.True(math.abs(math.dot(axis, want)) > 0.999f, o.Asset.Id + " long axis " + axis + " vs " + want);
            }
            Assert.Equal(yaw, mark.Yaw);   // functional markings are placed exactly by their layer
        }

        [Fact]
        public void NothingOnWaterBuildingLotsOrCutouts()
        {
            // Water in the south strip, a building lot in the north-west corner.
            var site = new SiteContext { WaterDepthAt = p => p.y < 15f ? 2f : 0f, HeightAt = p => p.y < 15f ? -2f : 0f };
            site.Obstacles.Add(new Obstacle { Kind = ObstacleKind.Building, A = new float2(0, 80), B = new float2(25, 80), C = new float2(25, 100), D = new float2(0, 100) });
            var p = Compose(Recipe("park", 9, r => r.Root.Settings.Values[DressingControls.AmountId] = 300.0), Site, site);
            var decals = Decals(p);
            Assert.NotEmpty(decals);
            foreach (var o in decals)
            {
                Assert.True(o.Position.z >= 15f, $"{o.Category} decal on water at {o.Position.xz}");
                Assert.False(o.Position.x < 25f && o.Position.z > 80f, $"{o.Category} decal on a building lot at {o.Position.xz}");
            }
        }

        [Fact]
        public void BeachGetsAWaterline()
        {
            var ring = TestKit.Rect(120, 90);
            var site = Types.WaterTypesKit.WaterInside(18f);
            var p = Compose(Recipe("beach", 7, r => r.Root.Settings.Values[DressingControls.StyleId] = "gritty"), ring, site);   // wrack is Gritty only (s23)
            var d = Decals(p);
            _out.WriteLine(string.Join(", ", d.GroupBy(o => o.Category).Select(g => g.Key + " " + g.Count())));
            Assert.Contains(d, o => o.Category == DecalPools.WetSand);
            Assert.Contains(d, o => o.Category == DecalPools.Beach);
            foreach (var o in d) Assert.True(site.WaterDepthAt(o.Position.xz) <= 0f);
        }

        [Fact]
        public void WithoutDecalPacksDesignSaysSoAndNothingDecorativeIsPlaced()
        {
            var none = new Dictionary<string, int> { [AssetCategories.Tree] = 5 };   // a known catalog without any decal
            using (AssetAvailability.Scope(none))
            {
                Assert.False(DecalPools.PacksAvailable());
                var r = Recipe("park");
                var p = Compose(r, Site);
                Assert.DoesNotContain(Decals(p), o => DecalPools.IsDressingPool(o.Category));   // the pass placed nothing
                Assert.Contains(p.Issues, i => i.Key == DecalPools.NoPacksKey && i.Severity == IssueSeverity.Info);
                var why = Availability.For(r, new AreaShape { Ring = Site }, new SiteContext(), GeneratorRegistry.Get);
                Assert.Equal(DecalPools.NoPacksKey, why[RecipePaths.Root][DressingControls.AmountId].Key);
            }
            Assert.True(DecalPools.PacksAvailable());   // no snapshot: assume packs
        }

        // ───────────────────────── controls ─────────────────────────

        [Fact]
        public void EveryDressedTypeHasTheControlsInTheSamePlace()
        {
            var lang = TestKit.Lang();
            foreach (var id in DressingProfiles.LayerIds)
            {
                var g = GeneratorRegistry.Get(id);
                if (g == null) continue;
                var amount = g.Controls.FirstOrDefault(c => c.Id == DressingControls.AmountId);
                var style = g.Controls.FirstOrDefault(c => c.Id == DressingControls.StyleId);
                Assert.NotNull(amount);
                Assert.NotNull(style);
                Assert.Equal(DressingControls.Group, amount.Group);
                Assert.Equal(DressingControls.Group, style.Group);
                Assert.Equal(100.0, Convert.ToDouble(amount.Default));
                foreach (var f in DressingProfiles.For(id).Families)
                {
                    var c = g.Controls.FirstOrDefault(x => x.Id == DressingControls.FamilyId(f));
                    Assert.NotNull(c);
                    Assert.True(lang.ContainsKey(c.LabelKey) && lang.ContainsKey(c.TooltipKey), c.LabelKey);
                }
                foreach (var p in g.Presets)
                    Assert.Equal(DressingControls.Styles[(int)DressingProfiles.StyleFor(id, p.Id)], p.Values.S(DressingControls.StyleId, null));
            }
            Assert.Equal("gritty", GeneratorRegistry.Get("abandonedLot").Defaults().S(DressingControls.StyleId, null));
            Assert.Equal("clean", GeneratorRegistry.Get("formalGarden").Defaults().S(DressingControls.StyleId, null));
            Assert.Null(GeneratorRegistry.Get("roads").Controls.FirstOrDefault(c => c.Id == DressingControls.AmountId));
        }

        [Fact]
        public void OldSavesWithoutDecalSettingsGetTheDefaults()
        {
            var s = DressingControls.Read(GeneratorRegistry.Get("park"), new SettingsBag());
            Assert.NotNull(s);
            Assert.Equal(1f, s.Master);
            Assert.Equal(DressStyle.LivedIn, s.Style);
            Assert.Equal(1f, s.Of(DressFamily.Trees));
            Assert.Equal(0f, s.Of(DressFamily.Works));
            var parking = DressingControls.Read(GeneratorRegistry.Get("parking"), new SettingsBag { Values = { ["dressAmount"] = 0.0 } });
            Assert.Equal(0f, parking.Master);
        }

        // ───────────────────────── helpers ─────────────────────────

        private static float AngleBetweenAxes(float yawA, float yawB)
        {
            var a = new float2(math.sin(yawA), math.cos(yawA)); var b = new float2(math.sin(yawB), math.cos(yawB));
            return math.acos(math.saturate(math.abs(math.dot(a, b))));
        }

        private static (float dist, float2 tangent) NearestPath(ComposedPlan p, float2 q)
        {
            float best = float.MaxValue; var tan = new float2(0, 1);
            foreach (var l in p.Layers)
                foreach (var n in l.Nets.Where(n => n.Role == NetRole.Pathway || n.Role == NetRole.PathwayWide))
                {
                    var prev = n.A.xz;
                    for (int i = 1; i <= 24; i++)
                    {
                        var c = Composer.Bezier(n.A, n.B, n.C, n.D, i / 24f).xz;
                        var ab = c - prev; float t = math.saturate(math.dot(q - prev, ab) / math.max(1e-6f, math.lengthsq(ab)));
                        float d = math.distance(q, prev + ab * t);
                        if (d < best) { best = d; tan = math.normalizesafe(ab); }
                        prev = c;
                    }
                }
            return (best, tan);
        }
    }
}
