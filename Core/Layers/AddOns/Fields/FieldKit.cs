// Sports fields (grand park wave, g1): shared kit. Every field is laid out in a local frame (u along the
// playing axis, v across) at real dimensions; the kit turns that into the plan:
//
//  * LinePainter — painted lines as chains of marking decals along segments / arcs, all with the same
//    variation so the whole field resolves to one decal asset (lines look like one paint job).
//  * FieldGround — a levelled pad: the playing area is one Level terraform at the pad's median height,
//    and an apron of small Level quads blends it back to the original ground at the place edge (no
//    cliffs). Skipped when the ground is already flat. Objects, surfaces and fences of the field are
//    then given the levelled height (HeightAfter).
//  * Dressing — floodlights, stands, benches, fence runs, the "Path to it" spur, the free-room probe
//    used to skip sizes that cannot fit.

using System;
using System.Collections.Generic;
using Placemaker.Core.Geometry;
using Placemaker.Core.Layers.Common;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.AddOns.Fields
{
    /// <summary>Local frame of a field: u along the playing axis, v across (v = +Side of the axis).</summary>
    public struct Frame
    {
        public float2 O, U, V;

        public Frame(float2 origin, float2 axis)
        {
            O = origin;
            U = math.normalizesafe(axis, new float2(1, 0));
            V = new float2(-U.y, U.x);
        }

        public static Frame Of(OrientedRect r) => new Frame(r.Center, r.Axis);
        public float2 P(float u, float v) => O + U * u + V * v;
        public float2 P(float2 l) => O + U * l.x + V * l.y;
        public float2 Dir(float2 l) => U * l.x + V * l.y;
        public float2 Local(float2 p) => new float2(math.dot(p - O, U), math.dot(p - O, V));
        /// <summary>Yaw of the +u direction (0 = +Z).</summary>
        public float Yaw => math.atan2(U.x, U.y);
        public float YawOf(float2 localDir) { var d = Dir(localDir); return math.atan2(d.x, d.y); }

        /// <summary>CCW world ring of a local box.</summary>
        public float2[] Box(float u0, float v0, float u1, float v1) =>
            Poly.EnsureCcw(new[] { P(u0, v0), P(u1, v0), P(u1, v1), P(u0, v1) });

        public OrientedRect Rect(float u, float v, float halfLength, float halfWidth) =>
            new OrientedRect { Center = P(u, v), Axis = U, HalfLength = halfLength, HalfWidth = halfWidth };
    }

    /// <summary>Painted lines: polylines and spots, emitted as marking decals at an even spacing.</summary>
    public sealed class LinePainter
    {
        private readonly List<List<float2>> _lines = new List<List<float2>>();
        private readonly List<(float2 p, float yaw)> _spots = new List<(float2, float)>();

        public int Lines => _lines.Count;

        public void Seg(Frame f, float u0, float v0, float u1, float v1) => _lines.Add(new List<float2> { f.P(u0, v0), f.P(u1, v1) });

        public void Box(Frame f, float u0, float v0, float u1, float v1)
        {
            _lines.Add(new List<float2> { f.P(u0, v0), f.P(u1, v0), f.P(u1, v1), f.P(u0, v1), f.P(u0, v0) });
        }

        /// <summary>Arc round a local centre from angle a0 to a1 (radians, 0 = +u, π/2 = +v).</summary>
        public void Arc(Frame f, float2 c, float r, float a0, float a1)
        {
            if (r <= 0.05f) return;
            int n = math.max(2, (int)math.ceil(math.abs(a1 - a0) * r / 1.2f));
            n = math.min(n, 96);
            var pts = new List<float2>(n + 1);
            for (int i = 0; i <= n; i++)
            {
                float a = math.lerp(a0, a1, i / (float)n);
                pts.Add(f.P(c + new float2(math.cos(a), math.sin(a)) * r));
            }
            _lines.Add(pts);
        }

        public void Circle(Frame f, float2 c, float r) => Arc(f, c, r, 0f, 2f * math.PI);

        /// <summary>A world polyline (closed when <paramref name="closed"/>).</summary>
        public void Path(IReadOnlyList<float2> world, bool closed)
        {
            if (world == null || world.Count < 2) return;
            var l = new List<float2>(world);
            if (closed) l.Add(world[0]);
            _lines.Add(l);
        }

        /// <summary>Dashes along a local segment (dash and gap in metres).</summary>
        public void Dashed(Frame f, float u0, float v0, float u1, float v1, float dash, float gap)
        {
            var a = new float2(u0, v0); var b = new float2(u1, v1);
            float len = math.distance(a, b);
            if (len < 0.1f) return;
            var d = (b - a) / len;
            for (float t = 0f; t < len - 0.05f; t += dash + gap)
                _lines.Add(new List<float2> { f.P(a + d * t), f.P(a + d * math.min(len, t + dash)) });
        }

        public void Spot(Frame f, float u, float v, float yaw = float.NaN) =>
            _spots.Add((f.P(u, v), float.IsNaN(yaw) ? f.Yaw : yaw));

        public float Length()
        {
            float sum = 0f;
            foreach (var l in _lines) for (int i = 1; i < l.Count; i++) sum += math.distance(l[i - 1], l[i]);
            return sum;
        }

        /// <summary>
        /// Emits the decals: at most <paramref name="max"/> (the spacing grows to stay under it). Silent when no
        /// marking decals are installed. Returns how many were placed.
        /// </summary>
        public int Emit(LayerContext ctx, float spacing, uint seed, int max = 1800)
        {
            if (!AssetAvailability.Has(AssetCategories.DecalMarkings)) return 0;
            float total = Length();
            spacing = math.max(0.4f, spacing);
            if (total / spacing + _spots.Count > max) spacing = total / math.max(1, max - _spots.Count);
            uint variation = Hashing.Mix(seed, 0x11E5u);   // one asset for every line of the field
            int n = 0;
            foreach (var l in _lines)
            {
                float carry = 0f;
                for (int i = 1; i < l.Count; i++)
                {
                    var a = l[i - 1]; var b = l[i];
                    float len = math.distance(a, b);
                    if (len < 1e-3f) continue;
                    var d = (b - a) / len;
                    float yaw = math.atan2(d.x, d.y);
                    float t = carry;
                    for (; t <= len + 1e-3f; t += spacing)
                    {
                        ctx.Object(a + d * math.min(t, len), yaw, ObjectRole.Decal, new AssetRef(""), variation, -1, AssetCategories.DecalMarkings);
                        n++;
                    }
                    carry = t - len;
                }
            }
            foreach (var (p, yaw) in _spots)
            {
                ctx.Object(p, yaw, ObjectRole.Decal, new AssetRef(""), variation, -1, AssetCategories.DecalMarkings);
                n++;
            }
            return n;
        }
    }

    /// <summary>
    /// A levelled pad blended into the original ground. The pad and the outer rectangle share centre and axis;
    /// the apron between them steps back to the original ground in bands of Level quads.
    /// </summary>
    public sealed class FieldGround
    {
        public bool Levelled;
        public float PadHeight;
        public int Pieces;
        public float MaxChange;

        private Frame _f;
        private float _pl, _pw, _ol, _ow;
        private Func<float2, float> _h;

        public float HeightAfter(float2 p)
        {
            float h = _h(p);
            if (!Levelled) return h;
            var l = _f.Local(p);
            float tu = (math.abs(l.x) - _pl) / math.max(0.1f, _ol - _pl);
            float tv = (math.abs(l.y) - _pw) / math.max(0.1f, _ow - _pw);
            float t = math.max(tu, tv);
            if (t <= 0f) return PadHeight;
            if (t >= 1f) return h;
            return math.lerp(PadHeight, h, Smooth(t));
        }

        private static float Smooth(float x) { x = math.saturate(x); return x * x * (3f - 2f * x); }

        public static FieldGround Level(LayerContext ctx, OrientedRect pad, OrientedRect outer, bool enabled)
        {
            var g = new FieldGround
            {
                _f = Frame.Of(pad), _pl = pad.HalfLength, _pw = pad.HalfWidth,
                _ol = math.max(outer.HalfLength, pad.HalfLength + 0.5f), _ow = math.max(outer.HalfWidth, pad.HalfWidth + 0.5f), _h = ctx.H,
            };
            // Median height of the pad (robust against a bump in one corner).
            var samples = new List<float>();
            int nu = math.clamp((int)(pad.HalfLength / 4f), 2, 14), nv = math.clamp((int)(pad.HalfWidth / 4f), 2, 14);
            for (int i = 0; i <= nu; i++)
                for (int j = 0; j <= nv; j++)
                    samples.Add(ctx.H(pad.Local(-pad.HalfLength + 2f * pad.HalfLength * i / nu, -pad.HalfWidth + 2f * pad.HalfWidth * j / nv)));
            samples.Sort();
            g.PadHeight = samples[samples.Count / 2];
            float dev = 0f;
            foreach (var s in samples) dev = math.max(dev, math.abs(s - g.PadHeight));
            g.MaxChange = dev;
            if (!enabled || dev < 0.25f) return g;
            g.Levelled = true;

            ctx.Plan.Terraform.Add(new PlannedTerraform { Ring = Poly.To3D(Poly.EnsureCcw(pad.Ring), ctx.H), Kind = TerraformKind.Level, TargetHeight = g.PadHeight, Permanent = true });
            g.Pieces++;
            // Apron: bands from the pad to the outer edge, each side cut into quads ~6 m long.
            float apron = math.min(g._ol - g._pl, g._ow - g._pw);
            int bands = math.clamp((int)math.ceil(apron / 2.2f), 1, 4);
            var f = g._f;
            for (int k = 0; k < bands; k++)
            {
                float t0 = k / (float)bands, t1 = (k + 1) / (float)bands;
                float il = math.lerp(g._pl, g._ol, t0), iw = math.lerp(g._pw, g._ow, t0);
                float ol = math.lerp(g._pl, g._ol, t1), ow = math.lerp(g._pw, g._ow, t1);
                float blend = Smooth((t0 + t1) * 0.5f);
                // Long sides (cover the corners), then the short ends between them.
                for (int s = -1; s <= 1; s += 2)
                {
                    int segs = math.max(1, (int)math.ceil(2f * ol / 6f));
                    for (int q = 0; q < segs; q++)
                    {
                        float u0 = -ol + 2f * ol * q / segs, u1 = -ol + 2f * ol * (q + 1) / segs;
                        g.Quad(ctx, f.Box(u0, s * iw, u1, s * ow), f.P((u0 + u1) * 0.5f, s * (iw + ow) * 0.5f), blend);
                    }
                    int ends = math.max(1, (int)math.ceil(2f * iw / 6f));
                    for (int q = 0; q < ends; q++)
                    {
                        float v0 = -iw + 2f * iw * q / ends, v1 = -iw + 2f * iw * (q + 1) / ends;
                        g.Quad(ctx, f.Box(s * il, v0, s * ol, v1), f.P(s * (il + ol) * 0.5f, (v0 + v1) * 0.5f), blend);
                    }
                }
            }
            bool warned = false;
            foreach (var i in ctx.Plan.Issues) if (i.Key == TerrainShaper.PermanentKey) { warned = true; break; }
            if (!warned) ctx.Issue(IssueSeverity.Warning, TerrainShaper.PermanentKey);
            return g;
        }

        private void Quad(LayerContext ctx, float2[] ring, float2 centre, float blend)
        {
            float h0 = ctx.H(centre);
            float target = math.lerp(PadHeight, h0, blend);
            if (math.abs(target - h0) < 0.15f) return;
            ctx.Plan.Terraform.Add(new PlannedTerraform { Ring = Poly.To3D(ring, ctx.H), Kind = TerraformKind.Level, TargetHeight = target, Permanent = true });
            Pieces++;
        }
    }

    /// <summary>Everything one field build needs; handed to FieldAddOn.Lay.</summary>
    public sealed class FieldJob
    {
        public LayerContext Ctx;
        public Occupancy Occ;
        /// <summary>The whole place (host place or own spot).</summary>
        public OrientedRect Site;
        /// <summary>The part of the place this format uses (centred; equals Site when the format fills it).</summary>
        public OrientedRect Place;
        public float2 Toward;
        public string Format;
        public FieldGround Ground;
        public readonly LinePainter Lines = new LinePainter();
        public uint Seed;
        public int Equipment, Lights, Stands, Benches;

        public uint V(uint salt) => Hashing.Mix(Seed, salt);
        /// <summary>+1/−1: the long side of the place facing where people arrive from.</summary>
        public float Side => math.dot(Toward - Place.Center, Place.Side) >= 0f ? 1f : -1f;
        /// <summary>+1/−1: the short end facing where people arrive from.</summary>
        public float End => math.dot(Toward - Place.Center, Place.Axis) >= 0f ? 1f : -1f;
        public LayerContext C => Ctx;
    }

    public static class FieldKit
    {
        /// <summary>Category chain helper: the first installed category (or the first when the player chose an asset).</summary>
        public static string Cat(LayerContext ctx, string chosen, params string[] chain) => LeisureKit.Category(ctx, chosen, chain);

        /// <summary>Floodlight masts at the given points, aimed at <paramref name="aim"/>.</summary>
        public static int Floodlights(FieldJob j, IEnumerable<float2> points, float2 aim)
        {
            var ctx = j.Ctx;
            string cat = Cat(ctx, ctx.S("lightAsset"), AssetCategories.FloodLight, AssetCategories.Lamp);
            if (cat == null) return 0;
            var asset = AssetMix.One(ctx.S("lightAsset"));
            int n = 0, i = 0;
            foreach (var p in points)
            {
                if (LeisureKit.Put(ctx, j.Occ, p, LeisureKit.Yaw(p, aim), ObjectRole.Lamp, asset, cat == AssetCategories.Lamp ? null : cat, 0.5f,
                        j.V(0x1170u + (uint)i++), 0.3f, 0.1f)) n++;
            }
            j.Lights += n;
            return n;
        }

        /// <summary>A row of stands (bleachers; benches when no stands are installed) from a to b, facing <paramref name="face"/>.</summary>
        public static int Stands(FieldJob j, float2 a, float2 b, float2 face, float pitch, float radius = 1.8f)
        {
            var ctx = j.Ctx;
            string cat = Cat(ctx, ctx.S("standAsset"), AssetCategories.SportsStand, AssetCategories.Bench);
            if (cat == null) return 0;
            var role = cat == AssetCategories.Bench ? ObjectRole.Bench : ObjectRole.Furniture;
            var asset = AssetMix.One(cat == AssetCategories.Bench ? ctx.S("benchAsset") : ctx.S("standAsset"));
            int n = 0;
            float len = math.distance(a, b);
            int count = math.max(1, (int)math.floor(len / pitch) + 1);
            for (int i = 0; i < count; i++)
            {
                var p = count == 1 ? math.lerp(a, b, 0.5f) : math.lerp(a, b, i / (float)(count - 1));
                // Stands face the field square-on (perpendicular to their row), not each a different way.
                var dir = b - a;
                var normal = math.normalizesafe(new float2(-dir.y, dir.x));
                if (math.dot(face - p, normal) < 0f) normal = -normal;
                if (LeisureKit.Put(ctx, j.Occ, p, LeisureKit.Yaw(p, p + normal), role, asset, role == ObjectRole.Bench ? null : cat, radius,
                        j.V(0x57A0u + (uint)(j.Stands + i)), 0.3f, 0.2f)) n++;
            }
            j.Stands += n;
            return n;
        }

        /// <summary>Benches (team benches, spectators) at the given points, facing <paramref name="face"/> square-on along <paramref name="normal"/>.</summary>
        public static int Benches(FieldJob j, IEnumerable<float2> points, float2 face)
        {
            var ctx = j.Ctx;
            var asset = AssetMix.One(ctx.S("benchAsset"));
            int n = 0, i = 0;
            foreach (var p in points)
                if (LeisureKit.Put(ctx, j.Occ, p, LeisureKit.Yaw(p, face), ObjectRole.Bench, asset, null, 1.1f, j.V(0xBE00u + (uint)(j.Benches + i++)), 0.3f, 0.2f)) n++;
            j.Benches += n;
            return n;
        }

        /// <summary>One piece of equipment (goal, net, hoop…) at an exact spot; it takes its disc in the occupancy.</summary>
        public static bool Equip(FieldJob j, float2 p, float yaw, string cat, string chosen, float radius, uint salt)
        {
            if (cat == null) return false;
            if (!LeisureKit.Put(j.Ctx, j.Occ, p, yaw, ObjectRole.Furniture, AssetMix.One(chosen), cat, radius, j.V(salt), 0.1f, 0.1f)) return false;
            j.Equipment++;
            return true;
        }

        /// <summary>A fence run through the points (open polyline).</summary>
        public static int FenceRun(LayerContext ctx, IReadOnlyList<float2> pts, AssetRef fence)
        {
            int n = 0;
            for (int i = 1; i < pts.Count; i++)
                if (math.distance(pts[i - 1], pts[i]) > 0.3f) { ctx.StraightNet(pts[i - 1], pts[i], NetRole.Fence, fence); n++; }
            return n;
        }

        /// <summary>A surface over a world ring from the first installed category of the chain.</summary>
        public static bool Surface(LayerContext ctx, float2[] ring, SurfaceRole role, string chosen, params string[] chain)
        {
            string cat = LeisureKit.Category(ctx, chosen, chain);
            if (cat == null) return false;
            return ctx.Surface(Poly.EnsureCcw(ring), role, AssetMix.One(chosen), cat);
        }

        /// <summary>The largest free clearance on a coarse grid (a cheap "can anything of this size fit?" probe).</summary>
        public static float MaxClearance(LayerRegion region, Occupancy occ, float cap, float step = 4f)
        {
            if (region == null) return 0f;
            float best = 0f;
            var size = region.Max - region.Min;
            step = math.max(step, math.sqrt(size.x * size.y / 6000f));
            for (float y = region.Min.y + step * 0.5f; y < region.Max.y; y += step)
                for (float x = region.Min.x + step * 0.5f; x < region.Max.x; x += step)
                {
                    var p = new float2(x, y);
                    if (!region.Contains(p)) continue;
                    float c = occ.Clearance(p, cap);
                    if (c > best) { best = c; if (best >= cap) return best; }
                }
            return best;
        }

        /// <summary>True when the probe says a rectangle of this size may fit (the grid underestimates by ≤ step·√2/2).</summary>
        public static bool MayFit(float room, float length, float width) => room >= math.min(length, width) * 0.45f - 3f;

        /// <summary>A free rectangle with a fixed axis near <paramref name="target"/> (north–south / east–west fields).</summary>
        public static bool FixedAxis(LayerRegion region, Occupancy occ, float2 target, float2 axis, float length, float width, out OrientedRect rect)
        {
            rect = default;
            float hl = length * 0.5f, hw = width * 0.5f;
            const float step = 3f;
            float reach = math.min(math.cmax(region.Max - region.Min), 90f);
            int rings = (int)math.ceil(reach / step), tests = 0;
            for (int ring = 0; ring <= rings; ring++)
            {
                int side = ring * 2, count = ring == 0 ? 1 : side * 4;
                for (int k = 0; k < count; k++)
                {
                    int ix, iy;
                    if (ring == 0) { ix = 0; iy = 0; }
                    else
                    {
                        int edge = k / side, o = k % side;
                        switch (edge)
                        {
                            case 0: ix = -ring + o; iy = -ring; break;
                            case 1: ix = ring; iy = -ring + o; break;
                            case 2: ix = ring - o; iy = ring; break;
                            default: ix = -ring; iy = ring - o; break;
                        }
                    }
                    var c = target + new float2(ix, iy) * step;
                    if (!region.Contains(c) || occ.Clearance(c, hw) < hw * 0.9f) continue;
                    var r = new OrientedRect { Center = c, Axis = axis, HalfLength = hl, HalfWidth = hw };
                    if (++tests > 500) return false;
                    if (LeisureKit.RectFits(region, occ, r, 1f)) { rect = r; return true; }
                }
            }
            return false;
        }

        /// <summary>
        /// "Path to it" for a field that found its own spot: a straight path from the nearest path to the
        /// nearest point of the field's edge (when it stays inside the area and does not cross the field).
        /// </summary>
        public static bool Spur(LayerContext ctx, Occupancy occ, OrientedRect site, AssetRef path)
        {
            if (occ.Paths.Count == 0) return false;
            if (occ.Paths.Nearest(site.Center, 220f, out var onPath) < 0) return false;
            // Closest point of the site's edge to the path point.
            var ring = site.Ring;
            float best = float.MaxValue; float2 end = site.Center;
            for (int i = 0; i < 4; i++)
            {
                var q = SegmentIndex.ClosestPoint(onPath, ring[i], ring[(i + 1) % 4]);
                float d = math.distance(q, onPath);
                if (d < best) { best = d; end = q; }
            }
            if (best < 2f) return false;   // a path already runs past the field
            var region = ctx.LayerRegion;
            if (region.CrossesBoundary(onPath, end, true) || !region.Contains(math.lerp(onPath, end, 0.5f))) return false;
            // Keep clear of trees, furniture and buildings on the way.
            float len = math.distance(onPath, end);
            for (float t = 2.5f; t < len - 1f; t += 2f)
            {
                var p = math.lerp(onPath, end, t / len);
                if (occ.Discs.Overlaps(p, 1.2f, 0f) || occ.HitsPolygon(p, 1.2f)) return false;
            }
            ctx.StraightNet(onPath, end, NetRole.Pathway, path);
            occ.TakeCorridor(onPath, end, Composer.DefaultHalfWidth(NetRole.Pathway), 100);
            ctx.Plan.Facts["fieldSpur"] = 1;
            return true;
        }
    }
}
