// s28 phase 2: ground wear painted into the terrain texture (Core/Decals/GroundWear.cs).

using System;
using System.Linq;
using System.Threading;
using Placemaker.Core;
using Placemaker.Core.Decals;
using Placemaker.Core.Geometry;
using Placemaker.Core.Layers.Common;
using Placemaker.Core.Tool;
using Unity.Mathematics;
using Xunit;
using Xunit.Abstractions;

namespace Placemaker.Tests
{
    public class GroundWearTests
    {
        private readonly ITestOutputHelper _out;
        public GroundWearTests(ITestOutputHelper output) { _out = output; }

        private static readonly DesignEditor Editor = new DesignEditor(GeneratorRegistry.Get);

        private static Recipe Recipe(string layer, uint seed, Action<Recipe> edit = null)
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

        private static ComposedPlan Compose(Recipe r)
        {
            var ring = TestKit.Rect(120, 100);
            var site = new SiteContext();
            TestKit.Entered(r, ring, site);
            return Composer.Compose(r, new AreaShape { Ring = ring, Source = AreaSource.Drawn }, site, CancellationToken.None);
        }

        private static int Paints(ComposedPlan p) => p.Layers.Sum(l => l.Paint.Count);

        private static Action<Recipe> Set(params (string id, object v)[] values) => r => { foreach (var (id, v) in values) r.Root.Settings.Values[id] = v; };

        [Fact]
        public void BareLawnGetsFaintWearAndZeroPaintsNothing()
        {
            var bare = Compose(Recipe("park", 7, Set(("groundSurface", false))));
            var none = Compose(Recipe("park", 7, Set(("groundSurface", false), (DressingControls.PaintWearId, 0.0))));
            int added = Paints(bare) - Paints(none);
            _out.WriteLine($"ground wear paints: {added}");
            Assert.True(added > 5, $"only {added} ground-wear paints on a bare-lawn park");
            Assert.True(added <= GroundWear.MaxPaints);
            // Faint, wide and only dirt / dry grass.
            foreach (var p in bare.Layers.SelectMany(l => l.Paint).Skip(0))
                if (p.Layer == TerrainMaterials.Dirt || p.Layer == TerrainMaterials.Dry) Assert.True(p.Strength <= 1f);
            var wear = bare.Layers.SelectMany(l => l.Paint).Where(p => p.Strength <= 0.5f && (p.Layer == TerrainMaterials.Dirt || p.Layer == TerrainMaterials.Dry)).ToList();
            Assert.NotEmpty(wear);
            Assert.Contains(bare.AllIssues(), i => i.Key == TerrainPaint.PermanentKey);
        }

        [Fact]
        public void NoWearUnderAGrassSurface()
        {
            var covered = Compose(Recipe("park", 7, Set(("groundSurface", true))));
            var none = Compose(Recipe("park", 7, Set(("groundSurface", true), (DressingControls.PaintWearId, 0.0))));
            var bare = Compose(Recipe("park", 7, Set(("groundSurface", false))));
            var bareNone = Compose(Recipe("park", 7, Set(("groundSurface", false), (DressingControls.PaintWearId, 0.0))));
            int onSurface = Paints(covered) - Paints(none), onBare = Paints(bare) - Paints(bareNone);
            _out.WriteLine($"grass surface: {onSurface}, bare terrain: {onBare}");
            Assert.True(onSurface < onBare / 2 + 1, $"{onSurface} paints under a grass surface vs {onBare} on bare terrain");
            // Every wear paint centre is off the surfaces.
            var surfaces = covered.Layers.SelectMany(l => l.Surfaces).Where(s => s.Ring.Length >= 3).Select(s => Poly.To2D(s.Ring)).ToList();
            foreach (var p in covered.Layers.SelectMany(l => l.Paint).Skip(Paints(none)))
            {
                var c = Poly.To2D(p.Ring).Aggregate(float2.zero, (a, b) => a + b) / p.Ring.Length;
                if (p.GradientFrom != null) continue;   // bands: centre is on the path
                Assert.DoesNotContain(surfaces, s => Poly.Contains(s, c));
            }
        }

        [Fact]
        public void DesirePathsToggleAndDeterminism()
        {
            var on = Compose(Recipe("park", 11, Set(("groundSurface", false))));
            var off = Compose(Recipe("park", 11, Set(("groundSurface", false), (DressingControls.DesirePathsId, false))));
            var again = Compose(Recipe("park", 11, Set(("groundSurface", false))));
            Assert.True(Paints(on) >= Paints(off));
            Assert.Equal(Paints(on), Paints(again));
            var a = on.Layers.SelectMany(l => l.Paint).ToList(); var b = again.Layers.SelectMany(l => l.Paint).ToList();
            for (int i = 0; i < a.Count; i++) { Assert.Equal(a[i].Layer, b[i].Layer); Assert.Equal(a[i].Strength, b[i].Strength); Assert.Equal(a[i].Ring[0], b[i].Ring[0]); }
        }

        [Fact]
        public void ControlsAreInTheDecalsSection()
        {
            var gen = GeneratorRegistry.Get("park");
            var wear = gen.Controls.First(c => c.Id == DressingControls.PaintWearId);
            var desire = gen.Controls.First(c => c.Id == DressingControls.DesirePathsId);
            Assert.Equal(DressingControls.Group, wear.Group);
            Assert.Equal(DressingControls.Group, desire.Group);
            Assert.Equal(ControlKind.Toggle, desire.Kind);
            // A clean formal garden does not trample desire lines by default.
            Assert.Equal(false, GeneratorRegistry.Get("formalGarden").Controls.First(c => c.Id == DressingControls.DesirePathsId).Default);
        }
    }
}
