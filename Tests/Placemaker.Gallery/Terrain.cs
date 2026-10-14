// s20 — terrain-aware gallery: shore sites with real slopes, the ground the game would build (the real
// TerraformRaster over the plan's terraform, sloped patches included), hillshade, an oblique 3D view and
// cross-sections. Playtests rated the water types "blocky": a flat top-down tint never showed terraces,
// pyramids or a beach stopping short of the water. Now every water render does.
//
//   --water beach,waterfront --shores flat,bank,headland,cliff

using System;
using System.Collections.Generic;
using System.Linq;
using Placemaker.Core;
using Placemaker.Core.Build;
using Placemaker.Core.Layers.Common;
using Unity.Mathematics;

namespace Placemaker.Gallery
{
    public static class ShoreSites
    {
        public static readonly string[] Kinds = { "flat", "bank", "headland", "cliff" };
        public const float Level = -0.6f;

        /// <summary>Coast line z(x) of a site: a gently curving shore 18 % up the site.</summary>
        public static Func<float, float> Coast(float3[] ring)
        {
            float zMin = ring.Min(p => p.z), zMax = ring.Max(p => p.z);
            float baseZ = zMin + (zMax - zMin) * 0.18f;
            return x => baseZ + 5f * math.sin(x / 41f) + 2.5f * math.sin(x / 17f + 1.3f);
        }

        private static float S(float e0, float e1, float x)
        {
            float t = math.saturate((x - e0) / (e1 - e0));
            return t * t * (3f - 2f * t);
        }

        /// <summary>Ground height of a shore site (u = metres inland of the coast line, negative = under water).</summary>
        public static Func<float2, float> Ground(float3[] ring, string kind)
        {
            var coast = Coast(ring);
            float xMin = ring.Min(p => p.x), xMax = ring.Max(p => p.x), w = xMax - xMin;
            float Low(float u, float2 p) => u >= 0f
                ? Level + 0.45f + 0.014f * u + 0.25f * (ValueNoise.Clumps(p, 28f, 7u) - 0.5f) * S(0f, 12f, u)
                : math.max(Level - 5f, Level + 0.45f + 0.09f * u);
            float Bank(float u, float2 p) => u >= 0f
                ? Level + 0.4f + 6.5f * S(3f, 30f, u) + 0.012f * u + 0.6f * (ValueNoise.Clumps(p, 24f, 9u) - 0.5f) * S(2f, 20f, u)
                : math.max(Level - 6f, Level + 0.4f + 0.16f * u);
            float Cliff(float u, float2 p) => u >= 0f
                ? Level + 0.5f + 13f * S(0f, 9f, u) + 0.02f * u + 1.2f * (ValueNoise.Clumps(p, 18f, 11u) - 0.5f) * S(0f, 6f, u)
                : math.max(Level - 8f, Level + 0.5f + 0.45f * u);
            return p =>
            {
                float u = p.y - coast(p.x);
                switch (kind)
                {
                    case "bank": return Bank(u, p);
                    case "cliff": return Cliff(u, p);
                    case "headland":
                        float t = S(xMin + 0.42f * w, xMin + 0.62f * w, p.x);
                        return math.lerp(Low(u, p), Cliff(u, p), t);
                    default: return Low(u, p);
                }
            };
        }

        /// <summary>
        /// Water depth as the game's snapshot gives it: sampled on an 8 m grid and interpolated bilinearly (so the
        /// zero line is blurred and a cell off, like in game). The sea is everything below the level seaward of the coast.
        /// </summary>
        public static Func<float2, float> Depth(float3[] ring, Func<float2, float> ground)
        {
            var coast = Coast(ring);
            float cell = 8f;
            float2 min = new float2(ring.Min(p => p.x), ring.Min(p => p.z)) - 80f, max = new float2(ring.Max(p => p.x), ring.Max(p => p.z)) + 80f;
            int nx = (int)math.ceil((max.x - min.x) / cell) + 1, ny = (int)math.ceil((max.y - min.y) / cell) + 1;
            var d = new float[nx * ny];
            for (int j = 0; j < ny; j++)
                for (int i = 0; i < nx; i++)
                {
                    var p = min + new float2(i, j) * cell;
                    float u = p.y - coast(p.x);
                    d[j * nx + i] = u < 6f ? math.max(0f, Level - ground(p)) : 0f;
                }
            return q =>
            {
                float fx = math.clamp((q.x - min.x) / cell, 0f, nx - 1.001f), fy = math.clamp((q.y - min.y) / cell, 0f, ny - 1.001f);
                int i = (int)fx, j = (int)fy;
                float tx = fx - i, ty = fy - j;
                return math.lerp(math.lerp(d[j * nx + i], d[j * nx + i + 1], tx), math.lerp(d[(j + 1) * nx + i], d[(j + 1) * nx + i + 1], tx), ty);
            };
        }
    }

    public static class TerrainView
    {
        /// <summary>The ground the build would leave: the plan's terraform through the real TerraformRaster (1.75 m, 4 m bank).</summary>
        public static Func<float2, float> Final(ComposedPlan plan, Func<float2, float> original)
        {
            var items = new List<(float2[] ring, float target, int layer)>();
            var vh = new List<float[]>();
            for (int li = 0; li < plan.Layers.Count; li++)
            {
                var l = plan.Layers[li];
                if (l == null) continue;
                foreach (var t in l.Terraform)
                {
                    if (t?.Ring == null || t.Ring.Length < 3 || !math.isfinite(t.TargetHeight)) continue;
                    items.Add((t.Ring.Select(v => v.xz).ToArray(), t.TargetHeight, li));
                    vh.Add(TerraformSurface.VertexHeights(t));
                }
            }
            if (items.Count == 0) return original;
            var raster = TerraformRaster.Build(items, 1.75f, 4f, 1 << 22, vh, original);
            if (raster == null) return original;
            return p => raster.TrySample(p, original, out float h) ? h : original(p);
        }

        /// <summary>Lambert shade factor of a height field at p (light from the north-west, 45° up).</summary>
        public static float Shade(Func<float2, float> h, float2 p, float e = 0.8f)
        {
            float dx = (h(p + new float2(e, 0)) - h(p - new float2(e, 0))) / (2f * e);
            float dz = (h(p + new float2(0, e)) - h(p - new float2(0, e))) / (2f * e);
            var n = math.normalize(new float3(-dx, 1f, -dz));
            var l = math.normalize(new float3(-0.55f, 0.75f, 0.45f));
            return math.clamp(math.dot(n, l) / math.dot(new float3(0, 1, 0), l), 0.35f, 1.25f);
        }

        /// <summary>
        /// Oblique view from the water side (south, 38° down), voxel-space rendering of the final ground textured
        /// with the top-down map (paint, surfaces, paths, footprints), water as a surface at the level; props as
        /// upright blocks/crowns; then three cross-sections (original dashed grey, final brown, water blue).
        /// </summary>
        public static void Oblique(string file, Canvas top, ComposedPlan plan, Catalog cat, Func<float2, float> original, Func<float2, float> final,
            float level, float2 min, float2 max)
        {
            const float A = 38f * math.PI / 180f;
            float sinA = math.sin(A), cosA = math.cos(A);
            float ext = max.x - min.x;
            float s = 1000f / ext;
            float hmin = float.MaxValue, hmax = float.MinValue;
            for (float z = min.y; z <= max.y; z += 4f)
                for (float x = min.x; x <= max.x; x += 4f)
                {
                    float h = math.max(final(new float2(x, z)), math.isfinite(level) ? level : -1e9f);
                    hmin = math.min(hmin, h); hmax = math.max(hmax, h);
                }
            hmax += 12f;
            int w = (int)(ext * s);
            int viewH = (int)(((max.y - min.y) * sinA + (hmax - hmin) * cosA) * s) + 20;
            const int ProfileH = 170;
            int H = viewH + ProfileH + 10;
            var cv = new Canvas(w, H, new float2(0, 0), 1f, Rgba.Hex("#dfe6ee"), 1);
            float V(float z, float h) => ((z - min.y) * sinA + (h - hmin) * cosA) * s;
            var water = Rgba.Hex("#4f8fc4");
            float step = 0.6f / s * 2f;
            for (int x = 0; x < w; x++)
            {
                float wx = min.x + x / s;
                float yb = -1f;
                for (float z = min.y; z <= max.y; z += math.max(0.15f, 1f / s * 0.6f))
                {
                    var p = new float2(wx, z);
                    float h = final(p);
                    bool wet = math.isfinite(level) && h < level - 0.02f;
                    float hs = wet ? level : h;
                    float v = V(z, hs);
                    if (v <= yb) continue;
                    Rgba c;
                    if (wet)
                    {
                        float depth = math.saturate((level - h) / 4f);
                        var bed = top.SampleWorld(p);
                        if (bed.A <= 0f) bed = Rgba.Hex("#c9c2a8");
                        float shade = Shade(final, p, 1.2f);
                        var under = new Rgba(bed.R * shade, bed.G * shade, bed.B * shade);
                        float k = 0.35f + 0.6f * depth;
                        c = new Rgba(math.lerp(under.R, water.R, k), math.lerp(under.G, water.G, k), math.lerp(under.B, water.B, k));
                    }
                    else
                    {
                        var b = top.SampleWorld(p);
                        if (b.A <= 0f) b = Rgba.Hex("#cfd8c0");
                        float shade = Shade(final, p, 1.2f);
                        c = new Rgba(math.saturate(b.R * shade), math.saturate(b.G * shade), math.saturate(b.B * shade));
                    }
                    int y0 = viewH - (int)math.ceil(v), y1 = viewH - (int)math.floor(math.max(0f, yb));
                    for (int y = math.max(0, y0); y <= y1 && y < viewH; y++) cv.SetPixel(x, y, c);
                    yb = v;
                }
            }
            // Props: upright blocks (far first).
            var objs = plan.Layers.Where(l => l != null).SelectMany(l => l.Objects).Where(o => o != null && o.Role != ObjectRole.Decal).OrderByDescending(o => o.Position.z).ToList();
            foreach (var o in objs)
            {
                var p = o.Position.xz;
                if (p.x < min.x || p.x > max.x || p.y < min.y || p.y > max.y) continue;
                float g = final(p);
                float ht = 1.2f, hw = 0.6f;
                if (!string.IsNullOrEmpty(o.Asset.Id) && cat.Items.TryGetValue(o.Asset.Id, out var info) && info.BoundsMax.y > 0.1f)
                {
                    ht = info.BoundsMax.y * (o.Scale > 0.05f ? o.Scale : 1f);
                    hw = math.max(0.3f, math.cmax(info.BoundsMax.xz - info.BoundsMin.xz) * 0.5f * (o.Scale > 0.05f ? o.Scale : 1f));
                }
                float sx = (p.x - min.x) * s;
                // Planned height vs ground: a floating or buried prop shows as a red tick.
                bool off = math.abs(o.Position.y - g) > 0.35f;
                var col = o.Role == ObjectRole.Tree ? Rgba.Hex("#2f6e33") : o.Role == ObjectRole.Bush ? Rgba.Hex("#5b9a44") : o.Role == ObjectRole.Lamp ? Rgba.Hex("#3b3b3b")
                    : o.Role == ObjectRole.Bench ? Rgba.Hex("#6b4423") : Rgba.Hex("#8a7cc0");
                float vb = V(p.y, g), vt = V(p.y, g + ht);
                if (o.Role == ObjectRole.Tree)
                {
                    float r = math.clamp(ht * 0.28f, 1.2f, 6f) * s;
                    Box(cv, sx - 0.25f * s, sx + 0.25f * s, viewH - vt + r, viewH - vb, Rgba.Hex("#5a4632"));
                    Disc(cv, sx, viewH - vt + r, r, col);
                }
                else Box(cv, sx - hw * s, sx + hw * s, viewH - vt, viewH - vb, col);
                if (off) Box(cv, sx - 2, sx + 2, viewH - vt - 8, viewH - vt - 2, Rgba.Hex("#e02020"));
            }
            // Decks over water / explicit-height nets: a thick line at their height.
            foreach (var n in plan.Layers.Where(l => l != null).SelectMany(l => l.Nets).Where(n => n != null && !n.FollowTerrain))
            {
                for (int i = 0; i < 16; i++)
                {
                    var a = Composer.Bezier(n.A, n.B, n.C, n.D, i / 16f); var b = Composer.Bezier(n.A, n.B, n.C, n.D, (i + 1) / 16f);
                    for (int k = 0; k <= 6; k++)
                    {
                        var q = math.lerp(a, b, k / 6f);
                        Box(cv, (q.x - min.x) * s - 2, (q.x - min.x) * s + 2, viewH - V(q.z, q.y) - 2, viewH - V(q.z, q.y) + 2, Rgba.Hex("#8b5a2b"));
                    }
                }
            }
            // Cross-sections at 30 / 50 / 70 % across, from the water inland.
            int pw = (w - 40) / 3;
            for (int k = 0; k < 3; k++)
            {
                float x = min.x + ext * (0.3f + 0.2f * k);
                int ox = 10 + k * (pw + 10), oy = viewH + 10;
                Box(cv, ox, ox + pw, oy, oy + ProfileH - 10, Rgba.Hex("#ffffff"));
                float lo = float.MaxValue, hi = float.MinValue;
                for (float z = min.y; z <= max.y; z += 1f)
                {
                    var p = new float2(x, z);
                    lo = math.min(lo, math.min(original(p), final(p))); hi = math.max(hi, math.max(original(p), final(p)));
                }
                if (math.isfinite(level)) { lo = math.min(lo, level - 1f); hi = math.max(hi, level + 1f); }
                hi = math.max(hi, lo + 6f);
                float X(float z) => ox + (z - min.y) / (max.y - min.y) * pw;
                float Y(float h) => oy + ProfileH - 14 - (h - lo) / (hi - lo) * (ProfileH - 24);
                if (math.isfinite(level))
                    for (float z = min.y; z <= max.y; z += 0.5f)
                        if (final(new float2(x, z)) < level) Box(cv, X(z), X(z) + 1, Y(level), Y(final(new float2(x, z))), Rgba.Hex("#9cc3e6"));
                for (float z = min.y; z < max.y; z += 0.25f)
                {
                    var p = new float2(x, z);
                    if (((int)(z * 2f) & 1) == 0) Box(cv, X(z), X(z) + 1, Y(original(p)) - 1, Y(original(p)) + 1, Rgba.Hex("#9a9a9a"));
                    Box(cv, X(z), X(z) + 1, Y(final(p)) - 1, Y(final(p)) + 1, Rgba.Hex("#7a4b1e"));
                }
                // Props on the section (within 3 m): small ticks at their planned height.
                foreach (var o in objs)
                    if (math.abs(o.Position.x - x) < 3f) Box(cv, X(o.Position.z) - 1, X(o.Position.z) + 1, Y(o.Position.y) - 5, Y(o.Position.y), Rgba.Hex("#7a4cc0"));
            }
            cv.SavePng(file);
        }

        private static void Box(Canvas cv, float x0, float x1, float y0, float y1, Rgba c)
        {
            int a = (int)math.floor(math.min(x0, x1)), b = (int)math.ceil(math.max(x0, x1));
            int t = (int)math.floor(math.min(y0, y1)), u = (int)math.ceil(math.max(y0, y1));
            for (int y = t; y <= u; y++)
                for (int x = a; x <= b; x++) cv.SetPixel(x, y, c);
        }

        private static void Disc(Canvas cv, float cx, float cy, float r, Rgba c)
        {
            for (int y = (int)(cy - r); y <= (int)(cy + r); y++)
                for (int x = (int)(cx - r); x <= (int)(cx + r); x++)
                {
                    float d = math.distance(new float2(x, y), new float2(cx, cy));
                    if (d <= r) cv.SetPixel(x, y, d > r - 1.2f ? c.Darker(0.7f) : c);
                }
        }
    }
}
