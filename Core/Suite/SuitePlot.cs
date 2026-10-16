// Game runner: a plot, test land made to order for one build (its "land" in the scenario). Plots lie in a row east of
// the block grid, each with the shape the build is drawn as (rectangle, circle, pinch, L or any outline), any size,
// the ground it asks for (flat, a slope, hills, bumps, mountains or its own heights) and, unless it asks for none, a
// ring road around it so entrances have a street. Pure geometry: the game layer writes the ground into the heightmap
// and lays the ring roads like the grid's.

using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace Placemaker.Core.Suite
{
    public sealed class SuitePlot
    {
        public string Name = "P1";
        /// <summary>rect | circle | pinch | l | points</summary>
        public string Shape = "rect";
        /// <summary>The shape's bounding size (m): width (x) and depth (z). A circle of this size is an ellipse.</summary>
        public float2 Size = new float2(130f, 120f);
        /// <summary>For shape "points": the outline relative to the plot's centre (m).</summary>
        public float2[] Points;
        /// <summary>For shape "pinch": the waist's width as a share of the depth.</summary>
        public float Neck = 0.3f;

        /// <summary>flat | slope | hills | bumpy | mountains | heights</summary>
        public string Ground = "flat";
        /// <summary>Slope: rise per metre (0.06 = 6 %), climbing towards north | south | east | west.</summary>
        public float Grade = 0.06f;
        public string Toward = "north";
        /// <summary>Hills / bumpy / mountains: height of the bumps (m) and their spacing (m); 0 = the ground's default.</summary>
        public float Amplitude, Wavelength;
        /// <summary>Ground "heights": metres above the base, rows from north to south, columns west to east, stretched over the shape's bounding box.</summary>
        public float[][] Heights;

        /// <summary>A ring road around the land (so entrances have a street), or open land.</summary>
        public bool RoadsAround = true;
        public string Road = SuiteLayout.SmallRoad;
        /// <summary>From the shape's bounding box to the ring road's centre line (m).</summary>
        public float Gap = 16f;

        /// <summary>Placed by the layout.</summary>
        public float2 Center;

        /// <summary>The ground follows the plot's own heights this far beyond the ring, then eases back to the base over Fade.</summary>
        public const float Hold = 20f, Fade = 50f;

        public float2 LandMin => Center - Size * 0.5f;
        public float2 LandMax => Center + Size * 0.5f;
        /// <summary>The ring road's centre lines (also the plot's block bounds).</summary>
        public float2 RingMin => LandMin - Gap;
        public float2 RingMax => LandMax + Gap;
        /// <summary>Everything the plot changes.</summary>
        public float2 OuterMin => RingMin - (Hold + Fade);
        public float2 OuterMax => RingMax + (Hold + Fade);
        public float OuterWidth => OuterMax.x - OuterMin.x;

        public float DefaultAmplitude => Ground == "bumpy" ? 1.5f : Ground == "mountains" ? 30f : 4f;
        public float DefaultWavelength => Ground == "bumpy" ? 18f : Ground == "mountains" ? 320f : 60f;

        // ───────────── shape ─────────────

        /// <summary>The outline the build is drawn as, in world coordinates, counter-clockwise.</summary>
        public float2[] Outline()
        {
            float2 h = Size * 0.5f;
            var r = new List<float2>();
            switch (Shape)
            {
                case "circle":
                {
                    // corners about 9 m apart (12 to 40 of them): a person clicking a round outline
                    float perimeter = math.PI * (h.x + h.y);
                    int n = math.clamp((int)math.round(perimeter / 9f), 12, 40);
                    for (int k = 0; k < n; k++)
                    {
                        float a = 2f * math.PI * k / n;
                        r.Add(new float2(math.cos(a) * h.x, math.sin(a) * h.y));
                    }
                    break;
                }
                case "pinch":
                {
                    float w = h.y * math.clamp(Neck, 0.05f, 1f);
                    r.Add(new float2(-h.x, -h.y)); r.Add(new float2(0f, -w)); r.Add(new float2(h.x, -h.y));
                    r.Add(new float2(h.x, h.y)); r.Add(new float2(0f, w)); r.Add(new float2(-h.x, h.y));
                    break;
                }
                case "l":
                    r.Add(new float2(-h.x, -h.y)); r.Add(new float2(h.x, -h.y)); r.Add(new float2(h.x, 0f));
                    r.Add(new float2(0f, 0f)); r.Add(new float2(0f, h.y)); r.Add(new float2(-h.x, h.y));
                    break;
                case "points":
                    if (Points != null) r.AddRange(Points);
                    break;
                default:
                    r.Add(new float2(-h.x, -h.y)); r.Add(new float2(h.x, -h.y)); r.Add(new float2(h.x, h.y)); r.Add(new float2(-h.x, h.y));
                    break;
            }
            for (int k = 0; k < r.Count; k++) r[k] += Center;
            return r.ToArray();
        }

        /// <summary>For shape "points": the bounding size of the given outline (so Size need not be given).</summary>
        public void SizeFromPoints()
        {
            if (Points == null || Points.Length < 3) return;
            float2 lo = Points[0], hi = Points[0];
            foreach (var p in Points) { lo = math.min(lo, p); hi = math.max(hi, p); }
            Size = math.max(hi - lo, 10f);
            // centre the outline on the plot
            var mid = (lo + hi) * 0.5f;
            for (int k = 0; k < Points.Length; k++) Points[k] -= mid;
        }

        // ───────────── ground ─────────────

        /// <summary>1 where the plot's ground applies (the ring + Hold), easing to 0 over Fade beyond it.</summary>
        public float Weight(float x, float z)
        {
            float2 lo = RingMin - Hold, hi = RingMax + Hold;
            float dx = math.max(math.max(lo.x - x, x - hi.x), 0f), dz = math.max(math.max(lo.y - z, z - hi.y), 0f);
            float d = math.sqrt(dx * dx + dz * dz);
            return 1f - math.smoothstep(0f, Fade, d);
        }

        /// <summary>The plot's ground above (or below) the base at (x, z).</summary>
        public float Rise(float x, float z)
        {
            float u = x - Center.x, v = z - Center.y;
            float a = Amplitude > 0f ? Amplitude : DefaultAmplitude;
            float l = math.max(4f, Wavelength > 0f ? Wavelength : DefaultWavelength);
            float k = 2f * math.PI / l;
            switch (Ground)
            {
                case "slope":
                {
                    float along = Toward == "south" ? -v : Toward == "east" ? u : Toward == "west" ? -u : v;
                    float reach = (Toward == "east" || Toward == "west" ? RingMax.x - RingMin.x : RingMax.y - RingMin.y) * 0.5f + Hold;
                    return Grade * math.clamp(along, -reach, reach);
                }
                case "hills":
                    return a * math.sin(u * k) * math.cos(v * k);
                case "bumpy":
                    return a * (0.7f * math.sin(u * k) * math.cos(v * k * 1.3f) + 0.3f * math.sin((u * 0.6f + v * 0.8f) * k * 2.1f));
                case "mountains":
                    return a * (0.55f * math.sin((u * 0.8f + v * 0.6f) * k) + 0.3f * math.sin((-u * 0.5f + v * 0.9f) * k * 2f + 1.3f)
                        + 0.15f * math.sin((u * 0.3f - v) * k * 4f + 0.7f));
                case "heights":
                    return Sample(u, v);
            }
            return 0f;
        }

        /// <summary>Bilinear sample of Heights over the land's bounding box (clamped at its edges).</summary>
        private float Sample(float u, float v)
        {
            if (Heights == null || Heights.Length == 0 || Heights[0] == null || Heights[0].Length == 0) return 0f;
            int rows = Heights.Length, cols = Heights[0].Length;
            float fx = math.saturate(u / Size.x + 0.5f) * (cols - 1);
            float fy = math.saturate(0.5f - v / Size.y) * (rows - 1);   // row 0 is the north edge
            int x0 = (int)math.floor(fx), y0 = (int)math.floor(fy);
            int x1 = math.min(cols - 1, x0 + 1), y1 = math.min(rows - 1, y0 + 1);
            float tx = fx - x0, ty = fy - y0;
            float At(int r, int c) => Heights[r] != null && c < Heights[r].Length ? Heights[r][c] : 0f;
            return math.lerp(math.lerp(At(y0, x0), At(y0, x1), tx), math.lerp(At(y1, x0), At(y1, x1), tx), ty);
        }

        // ───────────── ring road ─────────────

        /// <summary>The ring road around the land as segments (short ones on uneven ground, so the road follows it).</summary>
        public List<SuiteRoadSegment> RingRoads()
        {
            var r = new List<SuiteRoadSegment>();
            if (!RoadsAround) return r;
            float piece = Ground == "flat" ? 150f : 30f;
            float2 a = RingMin, b = RingMax;
            var corners = new[] { new float2(a.x, a.y), new float2(b.x, a.y), new float2(b.x, b.y), new float2(a.x, b.y) };
            for (int side = 0; side < 4; side++)
            {
                float2 p = corners[side], q = corners[(side + 1) % 4];
                int n = math.max(1, (int)math.ceil(math.distance(p, q) / piece));
                for (int k = 0; k < n; k++)
                    r.Add(new SuiteRoadSegment
                    {
                        Prefab = Road, Vertical = side % 2 == 1, Line = -1,
                        A = k == 0 ? p : math.lerp(p, q, k / (float)n), B = k == n - 1 ? q : math.lerp(p, q, (k + 1) / (float)n),
                    });
            }
            return r;
        }

        /// <summary>The plot as a block: its ring road's centre lines as the bounds, the ring's road on every side.</summary>
        public SuiteBlock Block() => new SuiteBlock
        {
            Name = Name, Kind = SuiteBlockKind.Plot, I = -1, J = -1,
            X0 = RingMin.x, X1 = RingMax.x, Z0 = RingMin.y, Z1 = RingMax.y,
            West = RoadsAround ? Road : "", East = RoadsAround ? Road : "", South = RoadsAround ? Road : "", North = RoadsAround ? Road : "",
        };
    }
}
