// Terrain shaping driven by the distance from the shore (beach slope, dunes, quay steps).
//
// Same rules as Common/TerrainShaper (cells ≈ brush size, permanent warning, slope relaxation), but
// the target height is a function of (point, original height, depth from the water) supplied by the
// type, and the blend back to the original ground happens towards the outline AWAY from the water
// only: along the waterline the beach is meant to meet the water, so the shore edge is not a border
// to blend to (offsets there are ~0 by construction: the beach plane starts at the shore height).

using System;
using Placemaker.Core.Geometry;
using Placemaker.Core.Layers.Common;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Types.Beach
{
    public static class ShoreTerrain
    {
        private const float MinChange = 0.2f;

        /// <param name="target">(point, original height, depth from the shore) → absolute target height.</param>
        public static TerrainResult Shape(LayerContext ctx, Shoreline shore, Func<float2, float, float, float> target,
            float falloff, float maxSlopePct, float minCell = 6f)
        {
            var region = shore.Land;
            var result = new TerrainResult { HeightAfter = ctx.H };
            float cell = math.max(minCell, math.sqrt(region.Area / TerrainShaper.MaxCells));
            int nx = math.max(1, (int)math.ceil((region.Max.x - region.Min.x) / cell));
            int ny = math.max(1, (int)math.ceil((region.Max.y - region.Min.y) / cell));
            var offset = new float[nx * ny];
            var baseH = new float[nx * ny];
            var inside = new bool[nx * ny];
            var edge = new float[nx * ny];
            falloff = math.max(cell, falloff);
            float slope = math.max(0.05f, maxSlopePct / 100f);

            for (int iy = 0; iy < ny; iy++)
            {
                if ((iy & 7) == 0) ctx.Check();
                for (int ix = 0; ix < nx; ix++)
                {
                    int i = iy * nx + ix;
                    var c = region.Min + (new float2(ix, iy) + 0.5f) * cell;
                    if (!region.Contains(c)) continue;
                    inside[i] = true;
                    float h0 = ctx.H(c);
                    float e = shore.EdgeDistance(c, falloff * 4f + 10f);
                    edge[i] = e;
                    float d = shore.Depth(c);
                    float t = target(c, h0, d);
                    if (!math.isfinite(t)) t = h0;
                    float off = (t - h0) * Smooth(0f, falloff, e);
                    baseH[i] = h0;
                    offset[i] = math.clamp(off, -slope * e, slope * e);
                }
            }

            // Neighbour relaxation (offsets only): the cell changed more gives way.
            float maxStep = slope * cell;
            for (int pass = 0; pass < 8; pass++)
            {
                bool changed = false;
                for (int iy = 0; iy < ny; iy++)
                    for (int ix = 0; ix < nx; ix++)
                    {
                        int i = iy * nx + ix;
                        if (!inside[i]) continue;
                        float t = baseH[i] + offset[i];
                        for (int k = 0; k < 4; k++)
                        {
                            int jx = ix + (k == 0 ? 1 : k == 1 ? -1 : 0), jy = iy + (k == 2 ? 1 : k == 3 ? -1 : 0);
                            if (jx < 0 || jy < 0 || jx >= nx || jy >= ny) continue;
                            int j = jy * nx + jx;
                            if (!inside[j]) continue;
                            float tj = baseH[j] + offset[j];
                            float allowed = maxStep + math.abs(baseH[i] - baseH[j]);   // the ground's own slope is not ours to fix
                            if (math.abs(t - tj) <= allowed || math.abs(offset[i]) <= math.abs(offset[j])) continue;
                            t = tj + math.sign(t - tj) * allowed;
                            changed = true;
                        }
                        float o = t - baseH[i];
                        offset[i] = math.clamp(o, -slope * edge[i], slope * edge[i]);
                    }
                if (!changed) break;
            }

            float minO = 0f, maxO = 0f; int cells = 0;
            for (int iy = 0; iy < ny; iy++)
                for (int ix = 0; ix < nx; ix++)
                {
                    int i = iy * nx + ix;
                    if (!inside[i] || math.abs(offset[i]) < MinChange) continue;
                    var c = region.Min + (new float2(ix, iy) + 0.5f) * cell;
                    var square = LayerRegion.Rect(c, new float2(1, 0), cell * 0.5f, cell * 0.5f);
                    float2[] ring = square;
                    bool whole = true;
                    foreach (var q in square) if (!region.Contains(q)) { whole = false; break; }
                    if (!whole)
                    {
                        var pieces = Poly.Intersection(square, region.Ring);
                        if (pieces.Count == 0 || pieces[0].Outer == null || pieces[0].Outer.Length < 3) continue;
                        ring = pieces[0].Outer;
                    }
                    ctx.Plan.Terraform.Add(new PlannedTerraform
                    {
                        Ring = Poly.To3D(ring, ctx.H),
                        Kind = offset[i] > 0f ? TerraformKind.Raise : TerraformKind.Lower,
                        TargetHeight = baseH[i] + offset[i],
                        Permanent = true,
                    });
                    minO = math.min(minO, offset[i]); maxO = math.max(maxO, offset[i]);
                    cells++;
                }
            result.Cells = cells; result.MinOffset = minO; result.MaxOffset = maxO;
            if (cells > 0)
            {
                Warn(ctx);
                float cs = cell; int w = nx, hgt = ny; var off = offset; var ins = inside; var min = region.Min;
                result.HeightAfter = p =>
                {
                    int ix = (int)math.floor((p.x - min.x) / cs), iy = (int)math.floor((p.y - min.y) / cs);
                    if (ix < 0 || iy < 0 || ix >= w || iy >= hgt) return ctx.H(p);
                    int i = iy * w + ix;
                    return ctx.H(p) + (ins[i] ? off[i] : 0f);
                };
                ctx.Plan.Facts["terrainCells"] = cells;
                ctx.Plan.Facts["terrainMaxRaise"] = maxO;
                ctx.Plan.Facts["terrainMaxLower"] = -minO;
            }
            return result;
        }

        /// <summary>A shallow hollow (two nested levels) — tide pools, quay step cuts.</summary>
        public static void Hollow(LayerContext ctx, float2[] outer, float2[] inner, float outerTarget, float innerTarget)
        {
            ctx.Plan.Terraform.Add(new PlannedTerraform { Ring = Poly.To3D(outer, ctx.H), Kind = TerraformKind.Lower, TargetHeight = outerTarget, Permanent = true });
            if (inner != null)
                ctx.Plan.Terraform.Add(new PlannedTerraform { Ring = Poly.To3D(inner, ctx.H), Kind = TerraformKind.Lower, TargetHeight = innerTarget, Permanent = true });
            Warn(ctx);
        }

        public static void Warn(LayerContext ctx)
        {
            foreach (var i in ctx.Plan.Issues) if (i.Key == TerrainShaper.PermanentKey) return;
            ctx.Issue(IssueSeverity.Warning, TerrainShaper.PermanentKey);
        }

        public static float Smooth(float e0, float e1, float x)
        {
            if (e1 <= e0) return x >= e0 ? 1f : 0f;
            float t = math.saturate((x - e0) / (e1 - e0));
            return t * t * (3f - 2f * t);
        }
    }
}
