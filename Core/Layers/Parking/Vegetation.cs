// Ported from ParkingLotTool (KruemelGames, GPL-3.0) — Geometry/ParkingVegetation.cs.
// Translated. The static density factor became a parameter; the "line" mode was dropped (unused
// by the parking layer); placement is deterministic per seed and position.
//
// Plants fill the green rings on a jittered grid; trees keep away from the strip edge more than
// bushes, clumping follows a smooth value noise, and spacing is enforced through a 16 m bucket grid.

using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Parking
{
    internal sealed class Species
    {
        public bool Tree;
        public double Spacing;
        public double Height;
    }

    internal static class Vegetation
    {
        /// <summary>Hard cap on candidate positions (PLT Kandidatengrenze).</summary>
        private const int CandidateLimit = 400000;

        public static List<Plant> Plan(List<List<double2>> rings, IReadOnlyList<Species> species, int density, uint seed,
            IReadOnlyList<Lantern> lanterns, Func<double2, bool> blocked)
        {
            var plants = new List<Plant>();
            if (density <= 0 || species.Count == 0 || rings == null) return plants;
            var occupied = new Dictionary<long, List<int>>();
            var anchor = new double2(double.MaxValue, double.MaxValue);
            foreach (var r in rings) foreach (var p in r) anchor = math.min(anchor, p);
            int candidates = 0;

            foreach (var ring in rings)
            {
                if (ring == null || ring.Count < 3) continue;
                var origin = ring[0];
                var axis = new double2(1, 0); double longest = 0;
                for (int i = 0; i < ring.Count; i++)
                {
                    var d = ring[(i + 1) % ring.Count] - ring[i];
                    double l = PGeo.Dot(d, d);
                    if (l > longest) { longest = l; axis = PGeo.Norm(d); }
                }
                if (longest < 0.01) continue;
                var offset = origin - anchor;
                uint areaSeed = Hash(seed ^ Hash((uint)(int)Math.Round(offset.x * 16)) ^ Hash((uint)(int)Math.Round(offset.y * 16) + 7919u));
                var across = PGeo.Perp(axis);
                var local = new List<double2>(ring.Count);
                var lo = new double2(double.MaxValue, double.MaxValue); var hi = new double2(double.MinValue, double.MinValue);
                foreach (var p in ring)
                {
                    var d = p - origin;
                    var l = new double2(PGeo.Dot(d, axis), PGeo.Dot(d, across));
                    local.Add(l); lo = math.min(lo, l); hi = math.max(hi, l);
                }
                for (int pass = 0; pass < 2; pass++)
                {
                    var pool = new List<int>();
                    for (int i = 0; i < species.Count; i++) if (species[i].Tree == (pass == 0)) pool.Add(i);
                    if (pool.Count == 0) continue;
                    double narrowest = double.MaxValue;
                    foreach (int i in pool) narrowest = Math.Min(narrowest, Spacing(species[i]));
                    double step = Math.Max(2 / Math.Sqrt(3), narrowest * 0.5);
                    int nx = Math.Max(1, (int)Math.Floor((hi.x - lo.x) / step));
                    int ny = Math.Max(1, (int)Math.Floor((hi.y - lo.y) / step));
                    var cells = GridCells(local, lo, hi, nx, ny);
                    long total = cells.Count * 2L;
                    if (total == 0) continue;
                    long stride = 65537;
                    while (Gcd(stride, total) != 1) stride += 2;
                    for (long k = 0; k < total; k++)
                    {
                        if (++candidates > CandidateLimit) return Thin(plants, density);
                        var cell = cells[(int)(k * stride % total / 2)];
                        int ix = cell.x, iy = cell.y;
                        uint s = Hash((uint)(ix + iy * 65537 + pass * 104729 + 1));
                        s = Hash(s ^ areaSeed ^ Hash((uint)(k * stride % total % 2) + 37u));
                        int which = pool[(int)(Hash(s) % (uint)pool.Count)];
                        var kind = species[which];
                        var p = new double2(lo.x + (hi.x - lo.x) * (ix + .5) / nx, lo.y + (hi.y - lo.y) * (iy + .5) / ny);
                        p += new double2(Unit(Hash(s + 7)) - .5, Unit(Hash(s + 11)) - .5) * (hi - lo) / new double2(nx, ny);
                        if (!PGeo.Contains(local, p)) continue;
                        double edge = PGeo.DistToBoundary(local, p);
                        if (edge < (kind.Tree ? .15 : .10)) continue;
                        double chance = kind.Tree ? Lerp(.3, 1, Sat(edge / 4)) : Lerp(1, .7, Sat(edge / 4));
                        var world = origin + axis * p.x + across * p.y;
                        double group = GroupDensity((world - anchor) / (kind.Tree ? 18.0 : 7.0), seed + (uint)pass * 313u);
                        chance *= Lerp(.35, 1, group);
                        if (Unit(Hash(s + 23)) > chance) continue;
                        if (lanterns != null)
                        {
                            bool hit = false;
                            foreach (var l in lanterns)
                            {
                                // a tree taller than the mast must keep 3 m off the lantern (PLT LaternenKoerper)
                                double free = kind.Tree ? 3.0 : 0.5;
                                if (math.distancesq(world, l.Position) < free * free) { hit = true; break; }
                            }
                            if (hit) continue;
                        }
                        if (blocked != null && blocked(world)) continue;
                        int gx = (int)Math.Floor(world.x / 16), gy = (int)Math.Floor(world.y / 16);
                        bool clear = true;
                        for (int dy = -1; dy <= 1 && clear; dy++)
                            for (int dx = -1; dx <= 1 && clear; dx++)
                                if (occupied.TryGetValue(Key(gx + dx, gy + dy), out var near))
                                    foreach (int n in near)
                                    {
                                        var other = plants[n];
                                        double dist = (Spacing(kind) + Spacing(species[other.Species])) * .5;
                                        if (math.distancesq(world, other.Position) < dist * dist - .001) { clear = false; break; }
                                    }
                        if (!clear) continue;
                        long key = Key(gx, gy);
                        if (!occupied.TryGetValue(key, out var list)) occupied[key] = list = new List<int>();
                        list.Add(plants.Count);
                        plants.Add(new Plant { Position = world, Species = which, Seed = s, Tree = kind.Tree });
                    }
                }
            }
            return Thin(plants, density);
        }

        private static List<Plant> Thin(List<Plant> plants, int density)
        {
            double keep = Math.Max(0, Math.Min(100, density)) / 100.0;
            plants.RemoveAll(p => Unit(Hash(p.Seed + 97)) >= keep);
            return plants;
        }

        private static List<int2> GridCells(List<double2> ring, double2 lo, double2 hi, int nx, int ny)
        {
            var cells = new List<int2>();
            double width = hi.x - lo.x;
            if (width <= 0) return cells;
            for (int iy = 0; iy < ny; iy++)
            {
                double py = lo.y + (hi.y - lo.y) * (iy + .5) / ny;
                foreach (var iv in PGeo.IntervalsAt(ring, py))
                {
                    int from = Math.Max(0, (int)Math.Ceiling((iv.x - lo.x) * nx / width - .5));
                    int to = Math.Min(nx - 1, (int)Math.Floor((iv.y - lo.x) * nx / width - .5));
                    for (int ix = from; ix <= to; ix++) cells.Add(new int2(ix, iy));
                }
            }
            return cells;
        }

        public static double Spacing(Species s) => Math.Max(0.5, s.Spacing);
        private static double Lerp(double a, double b, double t) => a + (b - a) * t;
        private static double Sat(double v) => Math.Max(0, Math.Min(1, v));
        private static long Gcd(long a, long b) { while (b != 0) { long n = a % b; a = b; b = n; } return a; }
        private static long Key(int x, int y) => ((long)x << 32) ^ (uint)y;
        internal static uint Hash(uint x) { unchecked { x ^= x >> 16; x *= 0x7feb352d; x ^= x >> 15; x *= 0x846ca68b; return x ^ (x >> 16); } }
        internal static double Unit(uint x) => (x & 0xffffff) / 16777216.0;

        private static double GroupDensity(double2 p, uint seed)
        {
            int x = (int)Math.Floor(p.x), y = (int)Math.Floor(p.y);
            var t = p - new double2(x, y);
            t = t * t * (3 - 2 * t);
            double V(int a, int b) => Unit(Hash(unchecked((uint)a * 73856093u ^ (uint)b * 19349663u ^ seed)));
            return Lerp(Lerp(V(x, y), V(x + 1, y), t.x), Lerp(V(x, y + 1), V(x + 1, y + 1), t.x), t.y);
        }
    }
}
