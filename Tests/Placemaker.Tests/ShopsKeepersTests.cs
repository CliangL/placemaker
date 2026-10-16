// (shops2) The kept Shops & squares types on a big block (four times the test block, streets all round): a strip mall is rows
// of stores round lots (not a park), Downtown mixes parking blocks in, the big market fills the square.

using System.Linq;
using System.Threading;
using Placemaker.Core;
using Placemaker.Core.Build;
using Placemaker.Core.Geometry;
using Placemaker.Core.Layers.Commercial;
using Placemaker.Core.Tool;
using Xunit;

namespace Placemaker.Tests
{
    public class ShopsKeepersTests
    {
        private static readonly DesignEditor Editor = new DesignEditor(GeneratorRegistry.Get);

        private static ComposedPlan Big(string type, string preset, float w = 300f, float d = 280f)
        {
            var ring = TestKit.Rect(w, d);
            var site = ZoningCutoutTests.Streets(ring);
            foreach (var g in ZoneLots.SiteGrids(site, null)) { var c = g; c.Predicted = false; c.Existing = false; site.ZoneGrids.Add(c); }
            var r = Editor.SingleLayerRecipe(GeneratorRegistry.Get(type), 5);
            if (preset != "") Editor.ApplyPreset(r, RecipePaths.Root, preset);
            return Composer.Compose(r, new AreaShape { Ring = ring, Source = AreaSource.Drawn }, site, CancellationToken.None);
        }

        [Theory]
        [InlineData(300f, 280f, 2)]
        [InlineData(360f, 110f, 1)]
        [InlineData(150f, 140f, 1)]
        public void StripMall_IsRowsOfStoresRoundLots_NotAPark(float w, float d, int minRows)
        {
            var plan = Big("stripMall", "classic", w, d);
            var host = plan.Layers.First();
            Assert.True(host.Facts.TryGetValue(StripMallLayer.RowsFact, out var rows) && rows >= minRows, "rows " + rows);
            var share = CommercialIrregularSiteTests.Measure(plan, Poly.To2D(TestKit.Rect(w, d)));
            Assert.True(share.Commercial >= 0.8f, $"commercial share {share.Commercial:0.00}");
            Assert.True(share.Lots >= share.Plots * 0.8f, $"lots {share.Lots:0} plots {share.Plots:0}");
            Assert.DoesNotContain(plan.Issues.Concat(host.Issues), i => i.Key == "Placemaker.Issue.CutoutNotZonable");
        }

        [Fact]
        public void Downtown_MixesParkingBlocksIn()
        {
            var host = Big("mallDistrict", "dense").Layers.First();
            Assert.True(host.Facts.TryGetValue("parkingBlocks", out var n) && n >= 2, "parking blocks " + n);
        }

        [Fact]
        public void BigMarket_FillsTheSquare()
        {
            var host = Big("marketSquare", "bigMarket").Layers.First();
            int stalls = host.Objects.Count(o => o.Category == AssetCategories.MarketStall);
            Assert.True(stalls >= 400, "stalls " + stalls);
            Assert.True(host.Facts.TryGetValue("foodCourts", out var courts) && courts >= 4, "food courts " + courts);
        }
    }
}
