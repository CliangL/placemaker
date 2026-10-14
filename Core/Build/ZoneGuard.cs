// Placemaker — keep the city's zoned buildings off a build, and give its zoning cutouts exactly their cells (s19).
//
// Playtest: zoned apartment blocks grew right over a plaza; only the cutouts were meant for buildings. CS2
// (decompiled 1.6.2f1, RESEARCH-NOTES-ADDENDUM 67–72): every road owns zone blocks of 8 m cells up to 6 deep,
// and a cell only becomes unzonable when Game.Zones.CellBlockJobs marks it Blocked — under ground nets with
// CompositionState.BlockZone and under areas whose prefab has GeometryFlags.PhysicalGeometry (lots without
// m_AllowOverlap) or a native ProtectedArea. Our owner lot allows overlap (not physical), surfaces and spaces
// are never physical, so the road blocks stayed zonable under the whole park and zoning the player had painted
// before (or paints later) grew buildings on it. Cells behind a blocked cell are blocked as well
// (CleanBlockedCells), and row 0 is blocked when row 1 is.
//
// The fix computed here (pure): ZONE BLOCKERS = the build's footprint minus the zoned cells (minus existing
// building lots), emitted by the game layer as invisible physical lots after the build is applied; and the
// zoning snapped to the real cells of the captured road blocks: per block column, rows 0 … the deepest cell
// whose centre lies in a cutout quad (the zoning tool's own Marquee rule), at least 2 rows deep (vanilla blocks
// a lone row 0 behind a blocked row 1). Quads whose road has no captured block (a lane this build adds) keep
// their quad and are kept free of blockers as polygons.

using System;
using System.Collections.Generic;
using Placemaker.Core.Geometry;
using Unity.Mathematics;

namespace Placemaker.Core.Build
{
    public enum ZoneCellState : byte { Zoned = 0, Blocked = 1, Kept = 2 }

    /// <summary>A road zone cell the build zones, blocks, or keeps (existing building) — for the preview.</summary>
    public struct ZoneCellMark
    {
        public float2 A, B, C, D;
        public ZoneCellState State;
        /// <summary>Ground height at the cell centre (set by Apply; 0 from Compute).</summary>
        public float Height;
        /// <summary>Zone prefab of a zoned cell ("" otherwise).</summary>
        public string ZoneId;
    }

    public static class ZoneGuard
    {
        /// <summary>Game setting "Keep zoned buildings off builds" (default on). Off = the s14 behaviour (no blockers, quads as planned).</summary>
        public static bool Enabled = true;

        public const float Cell = 8f;
        /// <summary>The blocker keeps this far inside the footprint (off kerbs and sidewalks at the outline).</summary>
        public const float FootprintInset = 0.5f;
        /// <summary>
        /// Distance from the blocker to zoned cells. The game blocks a cell when its quad shrunk 0.02 m touches a
        /// blocker triangle; ring repair (AreaRings, d = 0.375 m) may move the blocker edge by about 0.4 m.
        /// </summary>
        public const float KeepMargin = 0.5f;
        /// <summary>Distance from the blocker to existing building lots (they must keep their cells).</summary>
        public const float LotMargin = 0.5f;
        /// <summary>The zoning quad of a run of cells is shrunk this much: the Marquee tool takes cells by centre.</summary>
        public const float QuadShrink = 1f;
        /// <summary>Smallest blocker piece worth a lot (m²).</summary>
        public const float MinBlockerArea = 4f;
        /// <summary>Minimum zoned depth in cells (vanilla blocks row 0 when row 1 is blocked).</summary>
        public const int MinRows = 2;
        private const int MaxGridCells = 64 * 64;

        /// <summary>One zoned run: a rectangle of cells of one block, zoned with the zone of a source quad.</summary>
        public struct ZonedRect
        {
            public int Quad;          // index into the input quads (its layer and zone)
            public float2[] Corners;  // marquee quad (cells shrunk by QuadShrink), CCW
        }

        public sealed class Result
        {
            public readonly List<float2[]> Blockers = new List<float2[]>();
            public readonly List<ZoneCellMark> Cells = new List<ZoneCellMark>();
            public readonly List<ZonedRect> Zoning = new List<ZonedRect>();
            /// <summary>Per input quad: true when it was replaced by cell rects (false = keep the quad as planned).</summary>
            public bool[] Snapped = Array.Empty<bool>();
            /// <summary>Squares of the zoned cells (park content must stay off them).</summary>
            public readonly List<float2[]> ZonedSquares = new List<float2[]>();
            /// <summary>Blocker regions (may have holes) — the exact area the blockers cover.</summary>
            public readonly List<Region> BlockerRegions = new List<Region>();
        }

        // ───────────────────────── cell geometry (Game.Zones.ZoneUtils) ─────────────────────────

        /// <summary>MathUtils.Right: the block's column axis.</summary>
        public static float2 Right(float2 d) => new float2(d.y, -d.x);

        /// <summary>Centre of cell (column x, row y); row 0 is next to the road (ZoneUtils.GetCellPosition).</summary>
        public static float2 CellCentre(ZoneGrid g, int x, int y)
        {
            float2 d = math.normalizesafe(g.Direction);
            float2 o = new float2(g.Size.x - 2 * x - 1, g.Size.y - 2 * y - 1) * (Cell * 0.5f);
            return g.Position + d * o.y + Right(d) * o.x;
        }

        /// <summary>Square of a cell, grown by <paramref name="grow"/> (negative shrinks), CCW.</summary>
        public static float2[] CellSquare(ZoneGrid g, int x, int y, float grow = 0f)
        {
            float2 d = math.normalizesafe(g.Direction), r = Right(d), c = CellCentre(g, x, y);
            float h = Cell * 0.5f + grow;
            return Poly.EnsureCcw(new[] { c + d * h + r * h, c + d * h - r * h, c - d * h - r * h, c - d * h + r * h });
        }

        /// <summary>Rectangle of cells [x0..x1] × [y0..y1] grown by <paramref name="grow"/>, CCW.</summary>
        public static float2[] CellRect(ZoneGrid g, int x0, int x1, int y0, int y1, float grow = 0f)
        {
            float2 d = math.normalizesafe(g.Direction), r = Right(d);
            float h = Cell * 0.5f;
            // Local offsets of the centres (u along Right, v along Direction), see CellCentre.
            float u0 = (g.Size.x - 2 * x1 - 1) * h - h - grow, u1 = (g.Size.x - 2 * x0 - 1) * h + h + grow;
            float v0 = (g.Size.y - 2 * y1 - 1) * h - h - grow, v1 = (g.Size.y - 2 * y0 - 1) * h + h + grow;
            var p = g.Position;
            return Poly.EnsureCcw(new[] { p + r * u0 + d * v0, p + r * u1 + d * v0, p + r * u1 + d * v1, p + r * u0 + d * v1 });
        }

        private static bool Usable(ZoneGrid g) =>
            math.all(math.isfinite(g.Position)) && math.all(math.isfinite(g.Direction)) && math.lengthsq(g.Direction) > 1e-6f
            && g.Size.x > 0 && g.Size.y > 0 && g.Size.x * g.Size.y <= MaxGridCells;

        // ───────────────────────── plan entry ─────────────────────────

        /// <summary>
        /// End of Composer.Compose (after SiteSafety): computes the blockers and the cell preview, replaces the
        /// zoning quads that lie on captured road blocks by exact cell rectangles, and clears park content off
        /// zoned cells that reach past a cutout.
        /// </summary>
        public static void Apply(ComposedPlan plan, SiteContext site)
        {
            if (plan == null) return;
            plan.ZoneBlockers.Clear();
            plan.ZoneCells.Clear();
            if (!Enabled || plan.Footprints == null || plan.Footprints.Count == 0) return;

            var quads = new List<float2[]>();
            var ids = new List<string>();
            var owners = new List<(AreaPlan layer, PlannedZoning z)>();
            foreach (var l in plan.Layers)
            {
                if (l?.Zoning == null) continue;
                foreach (var z in l.Zoning)
                {
                    if (z == null) continue;
                    quads.Add(Poly.EnsureCcw(new[] { z.A.xz, z.B.xz, z.C.xz, z.D.xz }));
                    ids.Add(z.ZoneId ?? "");
                    owners.Add((l, z));
                }
            }
            Result r;
            try { r = Compute(plan.Footprints, quads, ids, plan.Protected, site?.ZoneGrids); }
            catch (Exception)
            {
                // Never fail a plan over the guard: build as before (no blockers, quads unchanged).
                return;
            }

            // Zoning: snapped quads are replaced by their cell rectangles (in the quad's layer, so loose/grouped stays).
            Func<float2, float> h = site?.HeightAt ?? (_ => 0f);
            for (int i = 0; i < owners.Count; i++)
                if (r.Snapped[i]) owners[i].layer.Zoning.Remove(owners[i].z);
            foreach (var zr in r.Zoning)
            {
                var c = zr.Corners;
                owners[zr.Quad].layer.Zoning.Add(new PlannedZoning
                {
                    A = new float3(c[0].x, h(c[0]), c[0].y),
                    B = new float3(c[1].x, h(c[1]), c[1].y),
                    C = new float3(c[2].x, h(c[2]), c[2].y),
                    D = new float3(c[3].x, h(c[3]), c[3].y),
                    ZoneId = ids[zr.Quad],
                });
            }

            // Zoned cells reaching past a cutout (a cell centre inside, its square partly outside; the front row
            // between the outline and the road) must be as empty as the cutout itself.
            var cuts = Layers.Common.Cutouts.Of(plan);
            var extra = new List<float2[]>();
            foreach (var sq in r.ZonedSquares)
            {
                bool inside = false;
                foreach (var c in cuts)
                {
                    bool all = true;
                    foreach (var p in Shrink(sq, 0.3f)) if (!Poly.Contains(c, p)) { all = false; break; }
                    if (all) { inside = true; break; }
                }
                if (!inside) extra.Add(sq);
            }
            if (extra.Count > 0) Layers.Common.Cutouts.Enforce(plan, extra);

            plan.ZoneBlockers.AddRange(r.Blockers);
            foreach (var c in r.Cells)
            {
                var m = c;
                m.Height = h(0.25f * (c.A + c.B + c.C + c.D));
                plan.ZoneCells.Add(m);
            }
        }

        // ───────────────────────── the computation (pure, tested) ─────────────────────────

        /// <summary>
        /// Which cells of the captured road blocks are zoned or blocked for a build footprint, its zoning quads and
        /// the existing building lots, and the blocker rings that make the game block exactly the others.
        /// </summary>
        public static Result Compute(IReadOnlyList<float2[]> footprints, IReadOnlyList<float2[]> quads, IReadOnlyList<string> zoneIds,
            IReadOnlyList<float2[]> protectedLots, IReadOnlyList<ZoneGrid> grids)
        {
            var result = new Result();
            quads = quads ?? Array.Empty<float2[]>();
            result.Snapped = new bool[quads.Count];
            var feet = new List<float2[]>();
            if (footprints != null) foreach (var f in footprints) if (f != null && f.Length >= 3) feet.Add(Poly.EnsureCcw(f));
            if (feet.Count == 0) return result;
            var lots = new List<float2[]>();
            if (protectedLots != null) foreach (var p in protectedLots) if (p != null && p.Length >= 3) lots.Add(p);

            Bounds(feet, out var fmin, out var fmax);
            fmin -= 6 * Cell; fmax += 6 * Cell;

            // ── 1. zoned cells per block column ──
            var gridList = new List<ZoneGrid>();
            if (grids != null)
                foreach (var g in grids)
                {
                    if (!Usable(g)) continue;
                    float reach = math.length((float2)g.Size) * Cell * 0.5f + Cell;
                    if (g.Position.x + reach < fmin.x || g.Position.y + reach < fmin.y || g.Position.x - reach > fmax.x || g.Position.y - reach > fmax.y) continue;
                    gridList.Add(g);
                }
            var zoned = new List<bool[]>(gridList.Count);          // per grid: [y * Sx + x]
            var zoneQuad = new List<int[]>(gridList.Count);
            var kept = new List<bool[]>(gridList.Count);
            var quadHasCell = new bool[quads.Count];
            for (int gi = 0; gi < gridList.Count; gi++)
            {
                var g = gridList[gi];
                int sx = g.Size.x, sy = g.Size.y;
                var z = new bool[sx * sy];
                var zq = new int[sx * sy];
                var k = new bool[sx * sy];
                for (int x = 0; x < sx; x++)
                {
                    var inQuad = new int[sy];
                    var inFoot = new bool[sy];
                    int first = -1, deepest = -1;
                    for (int y = 0; y < sy; y++)
                    {
                        var c = CellCentre(g, x, y);
                        inQuad[y] = -1;
                        for (int q = 0; q < quads.Count; q++)
                            if (quads[q] != null && quads[q].Length >= 3 && Poly.Contains(quads[q], c)) { inQuad[y] = q; break; }
                        inFoot[y] = InAny(feet, c);
                        var sq = CellSquare(g, x, y, -0.05f);
                        foreach (var lot in lots) if (ObstacleCut.Overlaps(sq, lot)) { k[y * sx + x] = true; break; }
                        if (inQuad[y] >= 0)
                        {
                            if (first < 0) first = y;
                            deepest = y;
                        }
                    }
                    if (first < 0) continue;
                    // Rows 0 … deepest, at least MinRows; the land in front of the first quad row must be outside the
                    // footprint (the strip between the outline and the road), never park; a building lot ends the run.
                    int last = math.min(sy - 1, math.max(deepest, MinRows - 1));
                    int quad = inQuad[first];
                    int end = -1;
                    for (int y = 0; y <= last; y++)
                    {
                        if (k[y * sx + x]) { if (y <= first) end = -1; break; }
                        if (y < first && inFoot[y]) { end = -1; break; }
                        end = y;
                    }
                    if (end < math.min(MinRows - 1, sy - 1)) continue;
                    for (int y = 0; y <= end; y++)
                    {
                        z[y * sx + x] = true;
                        zq[y * sx + x] = inQuad[y] >= 0 ? inQuad[y] : quad;
                    }
                    for (int y = 0; y < sy; y++) if (inQuad[y] >= 0 && y <= end) quadHasCell[inQuad[y]] = true;
                }
                zoned.Add(z); zoneQuad.Add(zq); kept.Add(k);
            }
            for (int q = 0; q < quads.Count; q++) result.Snapped[q] = quadHasCell[q];

            // ── 2. zoning rectangles: column runs (always from row 0), merged across neighbouring columns ──
            for (int gi = 0; gi < gridList.Count; gi++)
            {
                var g = gridList[gi];
                int sx = g.Size.x, sy = g.Size.y;
                var z = zoned[gi]; var zq = zoneQuad[gi];
                int x = 0;
                while (x < sx)
                {
                    int depth = Depth(z, sx, sy, x);
                    if (depth == 0) { x++; continue; }
                    int q = zq[x];
                    int x1 = x;
                    while (x1 + 1 < sx && Depth(z, sx, sy, x1 + 1) == depth && SameZone(ZoneOf(zoneIds, zq[x1 + 1]), ZoneOf(zoneIds, q))) x1++;
                    result.Zoning.Add(new ZonedRect { Quad = q, Corners = CellRect(g, x, x1, 0, depth - 1, -QuadShrink) });
                    for (int xx = x; xx <= x1; xx++)
                        for (int y = 0; y < depth; y++) result.ZonedSquares.Add(CellSquare(g, xx, y));
                    x = x1 + 1;
                }
            }

            // ── 3. blockers: footprint (inset) minus zoned cells, unsnapped quads and building lots ──
            var subject = new List<IReadOnlyList<float2>>();
            foreach (var f in feet)
            {
                List<float2[]> inner;
                try { inner = Poly.Offset(f, FootprintInset); } catch (Exception) { inner = null; }
                if (inner != null) foreach (var i in inner) if (i != null && i.Length >= 3) subject.Add(i);
            }
            var holes = new List<IReadOnlyList<float2>>();
            foreach (var sq in result.ZonedSquares) holes.Add(Grow(sq, KeepMargin));
            for (int q = 0; q < quads.Count; q++)
                if (!result.Snapped[q] && quads[q] != null && quads[q].Length >= 3) holes.Add(Grow(quads[q], KeepMargin));
            foreach (var lot in ObstacleCut.Grow(lots, LotMargin)) holes.Add(lot);
            if (subject.Count > 0)
            {
                List<Region> regions;
                try { regions = Poly.Boolean(subject, holes, BoolOp.Difference); }
                catch (Exception) { regions = new List<Region>(); }
                foreach (var reg in regions)
                {
                    if (reg?.Outer == null || reg.Outer.Length < 3 || reg.Area < MinBlockerArea) continue;
                    result.BlockerRegions.Add(reg);
                    List<float2[]> rings;
                    try { rings = reg.HasHoles ? Poly.CutHoles(reg) : new List<float2[]> { Poly.EnsureCcw(reg.Outer) }; }
                    catch (Exception) { continue; }
                    foreach (var ring in rings)
                    {
                        if (ring == null || ring.Length < 3 || Poly.Area(ring) < MinBlockerArea) continue;
                        List<float2[]> ok;
                        try { ok = AreaRings.PrepareSurface(ring, AreaRings.SurfaceMinNodeDistance); } catch (Exception) { ok = new List<float2[]>(); }
                        foreach (var o in ok) if (o != null && o.Length >= 3 && Poly.Area(o) >= MinBlockerArea) result.Blockers.Add(Poly.EnsureCcw(o));
                    }
                }
            }

            // ── 4. what the game will do with every cell near the build (preview / tests) ──
            for (int gi = 0; gi < gridList.Count; gi++)
            {
                var g = gridList[gi];
                int sx = g.Size.x, sy = g.Size.y;
                var z = zoned[gi]; var zq = zoneQuad[gi]; var k = kept[gi];
                for (int x = 0; x < sx; x++)
                {
                    bool blocked = false;
                    var col = new bool[sy];
                    for (int y = 0; y < sy; y++)
                    {
                        if (!blocked) blocked = Blocks(result.BlockerRegions, CellSquare(g, x, y, -0.02f));
                        col[y] = blocked;   // CleanBlockedCells: every cell behind a blocked one is blocked
                    }
                    if (sy > 1 && col[1]) col[0] = true;   // … and row 0 when row 1 is
                    for (int y = 0; y < sy; y++)
                    {
                        var sq = CellSquare(g, x, y);
                        ZoneCellState state;
                        if (col[y]) state = ZoneCellState.Blocked;
                        else if (z[y * sx + x]) state = ZoneCellState.Zoned;
                        else if (k[y * sx + x] && OverlapsAny(feet, sq)) state = ZoneCellState.Kept;
                        else continue;
                        result.Cells.Add(new ZoneCellMark
                        {
                            A = sq[0], B = sq[1], C = sq[2], D = sq[3], State = state,
                            ZoneId = state == ZoneCellState.Zoned ? ZoneOf(zoneIds, zq[y * sx + x]) : "",
                        });
                    }
                }
            }
            return result;
        }

        private static string ZoneOf(IReadOnlyList<string> zoneIds, int q) => zoneIds != null && q >= 0 && q < zoneIds.Count ? zoneIds[q] ?? "" : "";

        private static bool SameZone(string a, string b) => string.Equals(a, b, StringComparison.Ordinal);

        /// <summary>Zoned rows of a column (they always start at row 0).</summary>
        private static int Depth(bool[] z, int sx, int sy, int x)
        {
            int d = 0;
            while (d < sy && z[d * sx + x]) d++;
            return d;
        }

        /// <summary>True when the (shrunk) cell square overlaps the blocker area — the game's BlockCellsJob test.</summary>
        public static bool Blocks(IReadOnlyList<Region> regions, float2[] quad)
        {
            foreach (var reg in regions)
            {
                if (!ObstacleCut.Overlaps(quad, reg.Outer)) continue;
                bool inHole = false;
                foreach (var hole in reg.Holes)
                    if (InsideRing(hole, quad)) { inHole = true; break; }
                if (!inHole) return true;
            }
            return false;
        }

        /// <summary>Every corner inside <paramref name="ring"/> and no edge crossing it.</summary>
        private static bool InsideRing(float2[] ring, float2[] quad)
        {
            foreach (var p in quad) if (!Poly.Contains(ring, p)) return false;
            for (int i = 0; i < quad.Length; i++)
                for (int j = 0; j < ring.Length; j++)
                    if (Poly.SegmentsIntersect(quad[i], quad[(i + 1) % quad.Length], ring[j], ring[(j + 1) % ring.Length])) return false;
            return true;
        }

        private static bool InAny(List<float2[]> rings, float2 p)
        {
            foreach (var r in rings) if (Poly.Contains(r, p)) return true;
            return false;
        }

        private static bool OverlapsAny(List<float2[]> rings, float2[] quad)
        {
            foreach (var r in rings) if (ObstacleCut.Overlaps(quad, r)) return true;
            return false;
        }

        private static float2[] Grow(float2[] ring, float by)
        {
            try
            {
                var g = Poly.Offset(ring, -by);
                if (g != null && g.Count > 0) return g[0];
            }
            catch (Exception) { }
            return ring;
        }

        private static float2[] Shrink(float2[] q, float by)
        {
            var c = Poly.Centroid(q);
            var r = new float2[q.Length];
            for (int i = 0; i < q.Length; i++) r[i] = q[i] + math.normalizesafe(c - q[i]) * by;
            return r;
        }

        private static void Bounds(List<float2[]> rings, out float2 min, out float2 max)
        {
            min = new float2(float.MaxValue); max = new float2(float.MinValue);
            foreach (var r in rings) foreach (var p in r) { min = math.min(min, p); max = math.max(max, p); }
        }
    }
}
