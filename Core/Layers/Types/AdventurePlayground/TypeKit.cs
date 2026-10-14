// Small helpers shared by the builder-9 types (adventure playground, memorial park): category
// fallback chains resolved once per plan (one "no assets" Info per chain, not one per item), and
// round terraformed mounds / raised platforms built from concentric rings that always end at the
// original ground at their rim (the outer ring changes the ground by only a fraction of the height).

using System.Collections.Generic;
using Placemaker.Core.Geometry;
using Placemaker.Core.Layers.Common;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Types.AdventurePlayground
{
    /// <summary>Resolves category chains once per plan; null = nothing installed (Info issue added once).</summary>
    public sealed class CategoryChains
    {
        private readonly LayerContext _ctx;
        private readonly Dictionary<string, string> _cache = new Dictionary<string, string>(System.StringComparer.Ordinal);

        public CategoryChains(LayerContext ctx) { _ctx = ctx; }

        public string Get(bool chosen, params string[] chain)
        {
            string key = (chosen ? "1|" : "0|") + string.Join("|", chain);
            if (_cache.TryGetValue(key, out var c)) return c;
            c = LeisureKit.Category(_ctx, chosen ? "x" : "", chain);
            _cache[key] = c;
            return c;
        }

        /// <summary>First available category of the chain without reporting anything (optional dressing).</summary>
        public static string Quiet(params string[] chain)
        {
            foreach (var c in chain) if (AssetAvailability.Has(c)) return c;
            return null;
        }
    }

    public static class Mounds
    {
        /// <summary>
        /// A round mound (or flat-topped platform when <paramref name="topRadius"/> &gt; 0) centred at c:
        /// rings from the rim inwards, each raised a little more (smoothstep). The rim must lie inside the
        /// region with a margin; height is limited to a gentle slope. Returns the height actually used.
        /// </summary>
        public static float Raise(LayerContext ctx, LayerRegion region, float2 c, float rim, float topRadius, float height, float maxSlope = 0.25f)
        {
            float edge = region.BoundaryDistance(c, rim + 4f);
            rim = math.min(rim, edge - 2f);
            topRadius = math.clamp(topRadius, 0f, rim * 0.7f);
            if (rim < 3f || height < 0.2f) return 0f;
            height = math.min(height, (rim - topRadius) * maxSlope);
            if (height < 0.2f) return 0f;
            float baseH = ctx.H(c);
            for (int i = 0; i < 8; i++)
            {
                float a = i * math.PI * 0.25f;
                baseH = math.max(baseH, ctx.H(c + new float2(math.cos(a), math.sin(a)) * rim));
            }
            int rings = math.clamp((int)math.ceil((rim - topRadius) / 2.5f), 3, 8);
            for (int k = 0; k < rings; k++)
            {
                float t = (k + 1f) / rings;
                float r = math.lerp(rim, math.max(topRadius, rim * 0.18f), (float)k / rings);
                float s = t * t * (3f - 2f * t);
                var ring = LayerRegion.Circle(c, r, 20);
                ctx.Plan.Terraform.Add(new PlannedTerraform
                {
                    Ring = Poly.To3D(ring, ctx.H), Kind = TerraformKind.Raise, TargetHeight = baseH + height * s, Permanent = true,
                });
            }
            bool warned = false;
            foreach (var i in ctx.Plan.Issues) if (i.Key == TerrainShaper.PermanentKey) { warned = true; break; }
            if (!warned) ctx.Issue(IssueSeverity.Warning, TerrainShaper.PermanentKey);
            return height;
        }
    }
}
