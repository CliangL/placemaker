// Ported from ParkingLotTool (KruemelGames, GPL-3.0) — the cell engine's planning stages:
// Geometry/Zellen/Layout.cs (Layoutbauer.Baue), Planung.cs (Baender, Querstrassenmitten, Spalten,
// PlaneModule), Randreihe.cs (perimeter row), Ringbandplanung.cs, Ringlos*.cs (layout without a
// perimeter road), Zufahrt.cs (entrances) and Geometrie.cs (LaengsteGerade, Reihenrahmen).
//
// The PLT engine is ~15k lines entangled with zoning, bus stops and utility routing (all out of
// scope here). This is a re-structured port of the same pipeline:
//   frame (rows follow the longest straight edge) → setback / ring-road insets (Inset.cs) →
//   band plan of modules [row | aisle | row] separated by planting → columns of stalls with cross
//   lanes every N stalls → perimeter row along the outline → ring road, aisles, cross lanes,
//   entrance connectors → reachability → convex asphalt pieces for the cell surfaces.
// It never throws for bad geometry: problems become LayoutIssues and the stage is skipped.

using System;
using System.Collections.Generic;
using System.Threading;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Parking
{
    internal sealed class EntranceRequest
    {
        public EntranceKind Kind;
        public double2 World;
    }

    internal sealed class LayoutRequest
    {
        public List<double2> LotWorld = new List<double2>();
        public ParkingSettings Settings;
        public List<EntranceRequest> Entrances = new List<EntranceRequest>();
        public bool LeftHandTraffic;
        /// <summary>World point + radius → true when reserved by an earlier layer.</summary>
        public Func<double2, double, bool> Blocked = (p, r) => false;
    }

    /// <summary>Convex asphalt piece with the zones it may pave (bit mask of Zone).</summary>
    internal struct AsphaltPiece
    {
        public List<double2> Poly;
        public int ZoneMask;
    }

    internal sealed class LayoutEngine
    {
        // Ring zone membership bits for AsphaltPiece.ZoneMask (see SurfaceCells.Zone)
        public const int ZSetback = 1, ZPerimeter = 2, ZRing = 4, ZGrid = 8, ZAll = 15;

        private readonly LayoutRequest _rq;
        private readonly ParkingSettings _s;
        private readonly CancellationToken _cancel;
        private readonly Layout _out = new Layout();

        // contours (local frame); C1 = setback line, C2 = ring outer edge, CA = ring axis, G = grid contour
        private List<double2> _lot;
        private double2[] _c1Raw, _c2Raw, _caRaw, _c3Raw;
        private List<double2> _c1, _c2, _ca, _g;
        private bool _ring;
        private readonly List<int> _ringLines = new List<int>();
        private List<double> _crossCenters = new List<double>();
        private readonly List<AisleBand> _aisles = new List<AisleBand>();
        public readonly List<AsphaltPiece> Pieces = new List<AsphaltPiece>();
        public readonly List<(List<double2> Ring, int Zone)> ZoneRegions = new List<(List<double2>, int)>();
        private int _nextRow;

        private sealed class AisleBand
        {
            public double Y0, Y1;
            public double XL, XR;
            public int Line = -1;
            public int Module;
        }

        private sealed class ModuleSpec { public double Y0; public bool Lower, Upper; }

        private sealed class InteriorResult
        {
            public List<Bay> Bays = new List<Bay>();
            /// <summary>Per module: aisle band + its valid x-intervals; bay.Line = index into this list (temporary).</summary>
            public List<(double Y0, double Y1, int Module, double2 Interval)> AisleIntervals = new List<(double, double, int, double2)>();
            public int Count => Bays.Count;
        }

        private LayoutEngine(LayoutRequest rq, CancellationToken cancel)
        {
            _rq = rq; _s = rq.Settings; _cancel = cancel;
        }

        public static Layout Build(LayoutRequest rq, CancellationToken cancel, out List<AsphaltPiece> pieces, out List<(List<double2> Ring, int Zone)> zones)
        {
            var e = new LayoutEngine(rq, cancel);
            e.Run();
            pieces = e.Pieces;
            zones = e.ZoneRegions;
            return e._out;
        }

        private void Issue(IssueSeverity s, string key, double2? at = null, params object[] args) =>
            _out.Issues.Add(new LayoutIssue(s, key, at, args));

        // ───────────────────────────── pipeline ─────────────────────────────

        private void Run()
        {
            if (!SetupFrame()) return;
            _cancel.ThrowIfCancellationRequested();
            if (!SetupContours()) return;
            _cancel.ThrowIfCancellationRequested();

            var entrances = ProjectEntrances();

            // interior grid
            InteriorResult interior = null;
            if (_g != null && _g.Count >= 3)
            {
                PGeo.Bounds(_g, out var gmin, out var gmax);
                _crossCenters = CrossCenters(gmin.x, gmax.x);
                interior = BestInterior(gmin.y, gmax.y);
            }
            _cancel.ThrowIfCancellationRequested();

            if (_ring) BuildRingLines();
            if (interior != null) BuildAisles(interior);
            BuildCrossLanes();
            if (!_ring) BuildSpines();
            _cancel.ThrowIfCancellationRequested();

            _out.Entrances.AddRange(entrances);
            ConnectEntrances();
            if (_ring) PlacePerimeterRow();
            ClearCorridors();
            RemoveReserved();
            CheckReachability();
            _cancel.ThrowIfCancellationRequested();

            SpecialStalls.Assign(_out, _s, ChargerSpotOk);
            AssignChargers();
            BuildPieces();

            double len = 0;
            foreach (var l in _out.LiveLines)
                if (l.Kind == LineKind.Aisle || l.Kind == LineKind.Ring || l.Kind == LineKind.Cross) len += l.Length;
            _out.AisleLength = len;
            if (_out.StallCount == 0)
                Issue(IssueSeverity.Warning, "Placemaker.Parking.Issue.NoStalls");
        }

        // ───────────────────────────── frame ─────────────────────────────

        private bool SetupFrame()
        {
            var w = _rq.LotWorld;
            if (w == null || w.Count < 3) { Issue(IssueSeverity.Error, "Placemaker.Parking.Issue.BadShape"); return false; }
            double2 dir;
            if (_s.AngleMode == RowAngleMode.Fixed)
            {
                double r = _s.RowAngleDeg * Math.PI / 180;
                dir = new double2(Math.Cos(r), Math.Sin(r));
            }
            else
            {
                dir = LongestStraight(w);
                if (_s.AngleMode == RowAngleMode.Across) dir = PGeo.Perp(dir);
            }
            _out.Frame = new Frame(PGeo.Centroid(w), dir);
            _out.RowAngleDeg = (Math.Atan2(dir.y, dir.x) * 180 / Math.PI + 360) % 180;
            _lot = new List<double2>(w.Count);
            foreach (var p in w) _lot.Add(_out.Frame.ToLocal(p));
            _lot = PGeo.EnsureCcw(PGeo.Simplify(_lot, 1e-4, 1e-6));
            if (_lot.Count < 3 || !PGeo.IsSimple(_lot)) { Issue(IssueSeverity.Error, "Placemaker.Parking.Issue.BadShape"); return false; }
            _out.Lot = _lot;
            _out.LotArea = PGeo.Area(_lot);
            return true;
        }

        /// <summary>
        /// Direction of the longest straight run of the outline, tolerating vertices up to 5 cm off
        /// the line (PLT Geometrie.LaengsteGerade; GeradeToleranz = 0.05). Ties → smaller angle.
        /// </summary>
        internal static double2 LongestStraight(IReadOnlyList<double2> pts)
        {
            const double tol = 0.05;
            double bestLen = -1, bestDeg = 0;
            var best = new double2(1, 0);
            int n = pts.Count;
            for (int i = 0; i < n; i++)
                for (int steps = 1; steps < n; steps++)
                {
                    var d = pts[(i + steps) % n] - pts[i];
                    double len = PGeo.Len(d);
                    if (len < 1e-9) continue;
                    bool straight = true; double prev = 0;
                    for (int s = 1; s < steps; s++)
                    {
                        var v = pts[(i + s) % n] - pts[i];
                        double along = PGeo.Dot(v, d) / len;
                        if (along < prev - 1e-9 || along > len + 1e-9 || Math.Abs(PGeo.Cross(d, v)) / len > tol + 1e-9) { straight = false; break; }
                        prev = along;
                    }
                    if (!straight) break; // longer runs from i cannot be straight either
                    double deg = (Math.Atan2(d.y, d.x) * 180 / Math.PI + 180) % 180;
                    if (len <= bestLen + 1e-9 && !(Math.Abs(len - bestLen) <= 1e-9 && deg < bestDeg)) continue;
                    bestLen = len; bestDeg = deg; best = d / len;
                }
            return best;
        }

        // ───────────────────────────── contours ─────────────────────────────

        private bool SetupContours()
        {
            double es = _s.Setback;
            _c1Raw = es > 1e-6 ? Inset.Of(_lot, es) : _lot.ToArray();
            _c1 = CleanContour(_c1Raw);
            if (_c1 == null)
            {
                Issue(IssueSeverity.Error, "Placemaker.Parking.Issue.TooSmall");
                return false;
            }
            _ring = _s.PerimeterRoad;
            if (_ring)
            {
                double dp = _s.StallLength;
                _c2Raw = Inset.Of(_lot, es + dp);
                _caRaw = Inset.Of(_lot, es + dp + _s.Aisle / 2);
                _c3Raw = Inset.Of(_lot, es + dp + _s.Aisle);
                _c2 = CleanContour(_c2Raw);
                _ca = CleanContour(_caRaw);
                var g = CleanContour(_c3Raw);
                if (_c2 == null || _ca == null || g == null || PGeo.Area(g) < 20)
                {
                    _ring = false;
                    Issue(IssueSeverity.Info, "Placemaker.Parking.Issue.PerimeterRoadDropped");
                }
                else _g = g;
            }
            if (!_ring) _g = _c1;
            _out.PerimeterRoad = _ring;

            ZoneRegions.Add((_c1, _ring ? 1 : 3));
            if (_ring)
            {
                ZoneRegions.Add((_c2, 2));
                ZoneRegions.Add((_g, 3));
            }
            return true;
        }

        private static List<double2> CleanContour(double2[] raw)
        {
            if (raw == null) return null;
            var c = PGeo.EnsureCcw(PGeo.Simplify(raw, 1e-4, 1e-6));
            if (c.Count < 3 || PGeo.Area(c) < 1 || !PGeo.IsSimple(c)) return null;
            return c;
        }

        // ───────────────────────────── entrances ─────────────────────────────

        private List<EntrancePlan> ProjectEntrances()
        {
            var list = new List<EntrancePlan>();
            foreach (var rq in _rq.Entrances)
            {
                var p = _out.Frame.ToLocal(rq.World);
                int bestEdge = -1; double bestD = double.MaxValue, bestT = 0;
                for (int i = 0; i < _lot.Count; i++)
                {
                    var a = _lot[i]; var b = _lot[(i + 1) % _lot.Count];
                    PGeo.ClosestOnSeg(p, a, b, out double t);
                    double d = PGeo.Len(a + (b - a) * t - p);
                    if (d < bestD - 1e-9) { bestD = d; bestEdge = i; bestT = t; }
                }
                if (bestEdge < 0) continue;
                var ea = _lot[bestEdge]; var eb = _lot[(bestEdge + 1) % _lot.Count];
                double len = PGeo.Len(eb - ea);
                var tan = (eb - ea) / len;
                double width = rq.Kind == EntranceKind.TwoWay ? _s.Aisle : ParkingSettings.OneWayWidth;
                double along = bestT * len;
                double margin = width / 2 + 0.5;
                if (len > 2 * margin) along = Math.Max(margin, Math.Min(len - margin, along));
                else along = len / 2;
                // Two markers on the same spot would build two connectors on top of each other.
                bool dup = false;
                foreach (var o in list)
                    if (o.Edge == bestEdge && Math.Abs(o.Along - along) < (o.Width + width) / 2) dup = true;
                if (dup) { Issue(IssueSeverity.Warning, "Placemaker.Parking.Issue.EntrancesTooClose", ea + tan * along); continue; }
                list.Add(new EntrancePlan
                {
                    Kind = rq.Kind, Edge = bestEdge, Along = along,
                    Point = ea + tan * along, Inward = PGeo.Perp(tan), Width = width,
                });
            }
            return list;
        }

        // ───────────────────────────── interior grid ─────────────────────────────

        /// <summary>
        /// Cross lane centres along the rows: N stalls between lanes (plus a cap stall on both sides
        /// when row-end planting is on), lanes centred, the remainder split equally between both end
        /// sections. PLT Planung.Querstrassenmitten — user decision 2026-08-24: "the inner sections
        /// carry exactly N stalls", the ends take what is left; an end section shorter than
        /// min(5, N) stalls is never chosen (two thresholds once vetoed each other: N = 9 gave
        /// no cross lane at all while N = 5 did).
        /// </summary>
        private List<double> CrossCenters(double minX, double maxX)
        {
            var mids = new List<double>();
            int n = _s.CrossEvery;
            double p = _s.Pitch, cw = _s.Cross, len = maxX - minX;
            if (n <= 0 || len <= 0) return mids;
            int reserve = _s.RowEndPlanting ? 2 : 0;
            double inner = (n + reserve) * p;
            int best = 0;
            for (int count = 1; count < 1000; count++)
            {
                double used = (count - 1) * inner + count * cw;
                if (used > len) break;
                double end = (len - used) / 2;
                int endStalls = (int)Math.Floor((end - reserve * p) / p);
                if (endStalls < Math.Min(5, n)) continue;
                best = count;
            }
            if (best == 0) return mids;
            double usedBest = (best - 1) * inner + best * cw;
            double cursor = minX + (len - usedBest) / 2 + cw / 2;
            for (int i = 0; i < best; i++) { mids.Add(cursor); cursor += inner + cw; }
            return mids;
        }

        /// <summary>
        /// Evaluates a handful of band plans (module phase, an extra one-sided module) and keeps the
        /// one with most stalls. PLT centred the modules (Planung.Baender) and later shifted single
        /// modules away from ring edges (Ringbandplanung); trying the phase directly covers both.
        /// </summary>
        private InteriorResult BestInterior(double minY, double maxY)
        {
            double d = _s.RowDepth, ai = _s.Aisle, md = _s.Median;
            double modH = 2 * d + ai, half = d + ai, h = maxY - minY;
            int k = 0;
            while ((k + 1) * modH + k * md <= h + 1e-6) k++;
            var variants = new List<List<ModuleSpec>>();

            void AddVariants(int full, int halfCount, bool halfOnTop, bool halfLower)
            {
                double used = full * modH + halfCount * half + Math.Max(0, full + halfCount - 1) * md;
                if (full + halfCount == 0 || used > h + 1e-6) return;
                double slack = h - used;
                int steps = Math.Min(7, 1 + (int)Math.Floor(slack / 0.5));
                var offsets = new List<double> { slack / 2 };
                for (int j = 0; j < steps; j++) offsets.Add(steps == 1 ? 0 : slack * j / (steps - 1));
                foreach (var off in offsets)
                {
                    var mods = new List<ModuleSpec>();
                    double y = minY + off;
                    if (halfCount > 0 && !halfOnTop)
                    {
                        mods.Add(new ModuleSpec { Y0 = y, Lower = halfLower, Upper = !halfLower });
                        y += half + md;
                    }
                    for (int i = 0; i < full; i++)
                    {
                        mods.Add(new ModuleSpec { Y0 = y, Lower = true, Upper = true });
                        y += modH + md;
                    }
                    if (halfCount > 0 && halfOnTop)
                        mods.Add(new ModuleSpec { Y0 = y, Lower = halfLower, Upper = !halfLower });
                    variants.Add(mods);
                }
            }

            if (k > 0)
            {
                AddVariants(k, 0, false, false);
                if (h - (k * modH + (k - 1) * md) >= half + md - 1e-6)
                {
                    AddVariants(k, 1, true, true);
                    AddVariants(k, 1, false, false);
                }
            }
            else if (h >= half - 1e-6)
            {
                AddVariants(0, 1, false, true);
                AddVariants(0, 1, false, false);
            }

            InteriorResult best = null;
            foreach (var mods in variants)
            {
                _cancel.ThrowIfCancellationRequested();
                var r = PlaceInterior(mods);
                if (best == null || r.Count > best.Count) best = r;
            }
            return best;
        }

        private InteriorResult PlaceInterior(List<ModuleSpec> modules)
        {
            var res = new InteriorResult();
            double d = _s.RowDepth, ai = _s.Aisle, p = _s.Pitch, span = _s.StallSpan;
            double cap = _s.RowEndPlanting ? p : 0;
            var cuts = new List<double2>();
            foreach (var c in _crossCenters) cuts.Add(new double2(c - _s.Cross / 2, c + _s.Cross / 2));
            double sgn = _rq.LeftHandTraffic ? -1 : 1;
            double ca = Math.Cos(_s.AngleRad), sa = Math.Sin(_s.AngleRad);
            if (_s.StallAngleDeg == 90) { ca = 0; sa = 1; }

            for (int mi = 0; mi < modules.Count; mi++)
            {
                var m = modules[mi];
                double y = m.Y0;
                double lowY0 = y; if (m.Lower) y += d;
                double ay0 = y, ay1 = y + ai; y = ay1;
                double upY0 = y;
                var ai_iv = PGeo.StripInside(_g, ay0, ay1);
                var aisleIndexOf = new int[ai_iv.Count];
                for (int k = 0; k < ai_iv.Count; k++)
                {
                    aisleIndexOf[k] = res.AisleIntervals.Count;
                    res.AisleIntervals.Add((ay0, ay1, mi, ai_iv[k]));
                }
                for (int side = 0; side < 2; side++)
                {
                    bool lower = side == 0;
                    if (lower && !m.Lower) continue;
                    if (!lower && !m.Upper) continue;
                    double by0 = lower ? lowY0 : upY0, by1 = by0 + d;
                    var ri = PGeo.Intersect(PGeo.StripInside(_g, by0, by1), ai_iv);
                    ri = PGeo.Subtract(ri, cuts);
                    var axis = lower ? new double2(sgn * ca, -sa) : new double2(-sgn * ca, sa);
                    double cy = (by0 + by1) / 2;
                    int row = _nextRow++;
                    int order = 0;
                    foreach (var iv in ri)
                    {
                        double len = iv.y - iv.x - 2 * cap;
                        if (len < span - 1e-9) continue;
                        int n = (int)Math.Floor((len - span) / p + 1e-9) + 1;
                        double total = (n - 1) * p + span;
                        double x0 = iv.x + cap + (len - total) / 2;
                        int aisleIdx = -1;
                        for (int k = 0; k < ai_iv.Count; k++)
                            if (iv.x >= ai_iv[k].x - 1e-6 && iv.y <= ai_iv[k].y + 1e-6) { aisleIdx = aisleIndexOf[k]; break; }
                        for (int i = 0; i < n; i++)
                        {
                            var c = new double2(x0 + i * p + span / 2, cy);
                            res.Bays.Add(MakeBay(c, axis, new double2(1, 0), BayKind.Inner, row, order++, aisleIdx));
                        }
                        order += 2; // a gap breaks the run
                    }
                }
            }
            return res;
        }

        private Bay MakeBay(double2 c, double2 axis, double2 rowDir, BayKind kind, int row, int order, int line)
        {
            double hl = _s.StallLength / 2, hw = _s.StallWidth / 2;
            var w = PGeo.Perp(axis);
            return new Bay
            {
                Center = c, Axis = axis, RowDir = rowDir, Width = _s.StallWidth, Length = _s.StallLength,
                Kind = kind, Row = row, Order = order, Line = line,
                Quad = new[] { c - axis * hl - w * hw, c - axis * hl + w * hw, c + axis * hl + w * hw, c + axis * hl - w * hw },
            };
        }

        // ───────────────────────────── drive network ─────────────────────────────

        private int AddLine(double2 a, double2 b, LineKind kind, double width, int entrance = -1, bool oneWay = false)
        {
            var l = new DriveLine { Id = _out.Lines.Count, A = a, B = b, Kind = kind, Width = width, Entrance = entrance, OneWay = oneWay };
            _out.Lines.Add(l);
            return l.Id;
        }

        private List<double2> _caNet;

        private void BuildRingLines()
        {
            // Merge very short ring edges: courses under 1 m are dropped by the game layer (PLT NetCourse: < 1 m skipped).
            _caNet = PGeo.EnsureCcw(PGeo.Simplify(_ca, 1.0, 0.02));
            if (_caNet.Count < 3 || !PGeo.IsSimple(_caNet)) _caNet = _ca;
            for (int i = 0; i < _caNet.Count; i++)
                _ringLines.Add(AddLine(_caNet[i], _caNet[(i + 1) % _caNet.Count], LineKind.Ring, _s.Aisle));
        }

        private void BuildAisles(InteriorResult interior)
        {
            // Only aisles that serve at least one stall.
            var used = new bool[interior.AisleIntervals.Count];
            foreach (var b in interior.Bays) if (b.Line >= 0) used[b.Line] = true;

            // extents per aisle interval (extended to the ring axis when there is one)
            var ext = new double2[interior.AisleIntervals.Count];
            for (int i = 0; i < ext.Length; i++)
            {
                var a = interior.AisleIntervals[i];
                double ym = (a.Y0 + a.Y1) / 2, xa = a.Interval.x, xb = a.Interval.y;
                if (_ring && used[i])
                {
                    double reach = 3 * _s.Aisle + 20;
                    double tl = PGeo.RayHit(new double2(xa, ym), new double2(-1, 0), _caNet);
                    double tr = PGeo.RayHit(new double2(xb, ym), new double2(1, 0), _caNet);
                    if (tl <= reach) xa -= tl;
                    if (tr <= reach) xb += tr;
                }
                ext[i] = new double2(xa, xb);
            }

            // merge overlapping extents of the same module into one aisle line
            var lineOf = new int[ext.Length];
            for (int i = 0; i < lineOf.Length; i++) lineOf[i] = -1;
            var groups = new List<List<int>>();
            for (int i = 0; i < ext.Length; i++)
            {
                if (!used[i]) continue;
                List<int> g = null;
                foreach (var grp in groups)
                {
                    var f = interior.AisleIntervals[grp[0]];
                    if (f.Module != interior.AisleIntervals[i].Module) continue;
                    foreach (int j in grp)
                        if (ext[j].x <= ext[i].y + 1e-6 && ext[i].x <= ext[j].y + 1e-6) { g = grp; break; }
                    if (g != null) break;
                }
                if (g == null) { g = new List<int>(); groups.Add(g); }
                g.Add(i);
            }
            foreach (var g in groups)
            {
                var a = interior.AisleIntervals[g[0]];
                double xl = double.MaxValue, xr = double.MinValue;
                foreach (int j in g) { xl = Math.Min(xl, ext[j].x); xr = Math.Max(xr, ext[j].y); }
                double ym = (a.Y0 + a.Y1) / 2;
                int id = AddLine(new double2(xl, ym), new double2(xr, ym), LineKind.Aisle, _s.Aisle);
                foreach (int j in g) lineOf[j] = id;
                _aisles.Add(new AisleBand { Y0 = a.Y0, Y1 = a.Y1, XL = xl, XR = xr, Line = id, Module = a.Module });
            }
            foreach (var b in interior.Bays)
            {
                if (b.Line < 0 || lineOf[b.Line] < 0) continue;
                b.Line = lineOf[b.Line];
                _out.Bays.Add(b);
            }
        }

        private struct Stop { public double Y; public int Line; public bool Ring; }

        private List<Stop> StopsAt(double x)
        {
            var stops = new List<Stop>();
            double hw = _s.Cross / 2;
            foreach (var a in _aisles)
            {
                var l = _out.Lines[a.Line];
                if (x >= l.A.x + hw - 1e-6 && x <= l.B.x - hw + 1e-6)
                    stops.Add(new Stop { Y = l.A.y, Line = l.Id });
            }
            if (_ring)
                foreach (int id in _ringLines)
                {
                    var l = _out.Lines[id];
                    if ((l.A.x - x) * (l.B.x - x) >= 0) continue;
                    double t = (x - l.A.x) / (l.B.x - l.A.x);
                    var dir = PGeo.Norm(l.B - l.A);
                    if (Math.Abs(dir.x) < 0.17) continue; // nearly parallel to the lane: no clean junction
                    stops.Add(new Stop { Y = l.A.y + (l.B.y - l.A.y) * t, Line = id, Ring = true });
                }
            stops.Sort((p, q) => p.Y.CompareTo(q.Y));
            return stops;
        }

        private bool LaneFits(double x, Stop a, Stop b)
        {
            double hw = _s.Cross / 2;
            double y0 = a.Y + _s.Aisle / 2, y1 = b.Y - _s.Aisle / 2;
            if (y1 - y0 < 1e-3)
            {
                var mid = new double2(x, (a.Y + b.Y) / 2);
                return PGeo.Contains(_ring ? _c2 : _g, mid);
            }
            var rect = new List<double2> { new double2(x - hw, y0), new double2(x + hw, y0), new double2(x + hw, y1), new double2(x - hw, y1) };
            return PGeo.ConvexInside(rect, _ring ? _c2 : _g, 1e-3);
        }

        private void BuildCrossLanes()
        {
            foreach (var x in _crossCenters)
            {
                var stops = StopsAt(x);
                for (int i = 0; i + 1 < stops.Count; i++)
                {
                    var a = stops[i]; var b = stops[i + 1];
                    if (a.Ring && b.Ring) continue;
                    if (b.Y - a.Y < 0.5) continue;
                    if (!LaneFits(x, a, b)) continue;
                    AddLine(new double2(x, a.Y), new double2(x, b.Y), LineKind.Cross, _s.Cross);
                }
            }
        }

        /// <summary>
        /// Without a perimeter road the aisles need lanes across them (PLT Ringlos: end paths over
        /// the full width). Greedily adds up to six cross lanes that join the most separate aisles.
        /// </summary>
        private void BuildSpines()
        {
            if (_aisles.Count < 2) return;
            PGeo.Bounds(_g, out var mn, out var mx);
            for (int round = 0; round < 6; round++)
            {
                var uf = LineComponents(false);
                int comps = 0;
                var seen = new HashSet<int>();
                foreach (var a in _aisles) if (seen.Add(uf.Find(a.Line))) comps++;
                if (comps <= 1) return;
                double bestX = double.NaN; int bestGain = 0; double bestMid = double.MaxValue;
                double hw = _s.Cross / 2;
                for (double x = mn.x + hw + 0.5; x <= mx.x - hw - 0.5; x += 1.0)
                {
                    var stops = StopsAt(x);
                    var trial = new Dsu(_out.Lines.Count);
                    int gain = 0;
                    for (int i = 0; i + 1 < stops.Count; i++)
                    {
                        int ra = uf.Find(stops[i].Line), rb = uf.Find(stops[i + 1].Line);
                        if (ra == rb || trial.Find(ra) == trial.Find(rb)) continue;
                        if (!LaneFits(x, stops[i], stops[i + 1])) continue;
                        trial.Union(ra, rb); gain++;
                    }
                    double mid = Math.Abs(x - (mn.x + mx.x) / 2);
                    if (gain > bestGain || (gain == bestGain && gain > 0 && mid < bestMid)) { bestGain = gain; bestX = x; bestMid = mid; }
                }
                if (bestGain == 0) return;
                var st = StopsAt(bestX);
                for (int i = 0; i + 1 < st.Count; i++)
                {
                    int ra = uf.Find(st[i].Line), rb = uf.Find(st[i + 1].Line);
                    if (ra == rb || !LaneFits(bestX, st[i], st[i + 1])) continue;
                    AddLine(new double2(bestX, st[i].Y), new double2(bestX, st[i + 1].Y), LineKind.Cross, _s.Cross);
                    uf.Union(ra, rb);
                }
            }
        }

        private void ConnectEntrances()
        {
            for (int ei = 0; ei < _out.Entrances.Count; ei++)
            {
                var e = _out.Entrances[ei];
                if (!FindJunction(e.Point, e.Inward, out var j, out _))
                {
                    Issue(IssueSeverity.Warning, "Placemaker.Parking.Issue.EntranceNoPath", e.Point, ei + 1);
                    continue;
                }
                e.Junction = j;
                e.Connected = true;
                e.ConnectorLine = AddLine(e.Point, j, LineKind.Connector, e.Width, ei, e.Kind != EntranceKind.TwoWay);
                if (e.Kind != EntranceKind.TwoWay)
                {
                    // PLT Netzerhalt: one-way entrances get a pedestrian path beside the lane.
                    var off = PGeo.Perp(e.Inward) * (e.Width / 2 + 1.0);
                    var fp = e.Point + off;
                    if (PGeo.DistToBoundary(_lot, fp) < 0.5 && FindJunction(fp, e.Inward, out var fj, out _))
                        AddLine(fp, fj, LineKind.Footpath, 2.0, ei);
                }
            }
        }

        /// <summary>Where a lane from boundary point p heading inward meets the drive network.</summary>
        private bool FindJunction(double2 p, double2 inward, out double2 junction, out int line)
        {
            junction = p; line = -1;
            double bestT = double.PositiveInfinity;
            var far = p + inward;
            foreach (var l in _out.Lines)
            {
                if (l.Removed || l.Kind == LineKind.Connector || l.Kind == LineKind.Footpath) continue;
                if (!PGeo.LineLine(p, far, l.A, l.B, out double t, out double u)) continue;
                if (u < -1e-9 || u > 1 + 1e-9 || t <= 1e-6) continue;
                if (t < bestT) { bestT = t; line = l.Id; }
            }
            double limit = _ring ? _s.Setback + _s.StallLength + _s.Aisle + 10 : 80;
            if (line >= 0 && bestT <= limit)
            {
                junction = p + inward * bestT;
                // a cut point closer than 1.5 m to the line's end becomes that end (no sub-metre courses)
                var l = _out.Lines[line];
                if (PGeo.Len(junction - l.A) < 1.5) junction = l.A;
                else if (PGeo.Len(junction - l.B) < 1.5) junction = l.B;
                return SegmentInLot(p, junction);
            }
            // nearest point on any drive line, if the straight path stays inside the lot
            double bestD = double.PositiveInfinity; double2 bestP = p; int bestL = -1;
            foreach (var l in _out.Lines)
            {
                if (l.Removed || l.Kind == LineKind.Connector || l.Kind == LineKind.Footpath) continue;
                var q = PGeo.ClosestOnSeg(p, l.A, l.B, out _);
                double dd = PGeo.Len(q - p);
                if (dd < bestD && SegmentInLot(p, q)) { bestD = dd; bestP = q; bestL = l.Id; }
            }
            if (bestL < 0 || bestD > 120) return false;
            junction = bestP; line = bestL;
            return true;
        }

        private bool SegmentInLot(double2 a, double2 b)
        {
            for (int i = 0; i < _lot.Count; i++)
                if (PGeo.SegmentsCross(a, b, _lot[i], _lot[(i + 1) % _lot.Count], 1e-7)) return false;
            var m = (a + b) * 0.5;
            return PGeo.Contains(_lot, m);
        }

        // ───────────────────────────── perimeter row ─────────────────────────────

        /// <summary>
        /// Stalls along every outline edge between the setback line and the ring road
        /// (PLT Randreihe.cs). The usable length of edge k is the overlap of the projections of the
        /// setback contour's and the road edge contour's edge k. Entrances block their width plus one
        /// stall of air on each side (PLT: luft = buchtbreite). The remainder of a section is pushed
        /// away from an entrance so stalls start right next to it.
        /// </summary>
        private void PlacePerimeterRow()
        {
            double es = _s.Setback, dp = _s.StallLength, sw = _s.StallWidth;
            int n = _lot.Count;
            var accepted = new List<Bay>();
            int ringLine = _ringLines.Count > 0 ? _ringLines[0] : -1;
            for (int k = 0; k < n; k++)
            {
                var o = _lot[k];
                var dir = _lot[(k + 1) % n] - o;
                double len = PGeo.Len(dir);
                if (len < 1e-6) continue;
                var tan = dir / len;
                var nrm = PGeo.Perp(tan);
                double Along(double2 q) => PGeo.Dot(q - o, tan);
                double oA = Along(_c1Raw[k]), oB = Along(_c1Raw[(k + 1) % n]);
                double iA = Along(_c2Raw[k]), iB = Along(_c2Raw[(k + 1) % n]);
                double start = Math.Max(Math.Min(oA, oB), Math.Min(iA, iB));
                double end = Math.Min(Math.Max(oA, oB), Math.Max(iA, iB));
                if (end - start < sw) continue;

                var blocks = new List<(double From, double To)>();
                foreach (var e in _out.Entrances)
                    if (e.Edge == k && e.Connected)
                    {
                        double half = e.Width / 2 + sw;
                        blocks.Add((e.Along - half, e.Along + half));
                    }
                blocks.Sort((p, q) => p.From.CompareTo(q.From));

                var sections = new List<(double From, double To, bool EntranceBefore, bool EntranceAfter)>();
                double run = start; bool before = false;
                foreach (var b in blocks)
                {
                    if (b.To <= run) { before = true; continue; }
                    if (b.From > run) sections.Add((run, Math.Min(b.From, end), before, b.From <= end));
                    run = Math.Max(run, b.To);
                    before = true;
                    if (run >= end) break;
                }
                if (run < end) sections.Add((run, end, before, false));

                int row = _nextRow++;
                int order = 0;
                foreach (var sec in sections)
                {
                    double usable = sec.To - sec.From;
                    int count = (int)Math.Floor(usable / sw + 1e-9);
                    if (count <= 0) continue;
                    double rest = usable - count * sw;
                    double first = sec.EntranceBefore && !sec.EntranceAfter ? sec.From
                        : sec.EntranceAfter && !sec.EntranceBefore ? sec.From + rest
                        : sec.From + rest / 2;
                    for (int i = 0; i < count; i++)
                    {
                        double a0 = first + i * sw, a1 = a0 + sw;
                        var q = new[]
                        {
                            o + tan * a0 + nrm * (es + dp), o + tan * a1 + nrm * (es + dp),
                            o + tan * a1 + nrm * es, o + tan * a0 + nrm * es,
                        };
                        var bay = new Bay
                        {
                            Quad = q, Center = (q[0] + q[2]) * 0.5, Axis = -nrm, RowDir = tan,
                            Width = sw, Length = dp, Kind = BayKind.Perimeter, Row = row, Order = order++, Line = ringLine,
                        };
                        if (!PGeo.ConvexInside(q, _c1, 1e-4)) { order++; continue; }
                        bool clash = false;
                        foreach (var other in accepted)
                            if (math.distance(other.Center, bay.Center) < sw + dp && PGeo.ConvexOverlap(q, other.Quad, 1e-4)) { clash = true; break; }
                        if (clash) { order++; continue; }
                        accepted.Add(bay);
                    }
                    order += 2;
                }
            }
            _out.Bays.AddRange(accepted);
        }

        // ───────────────────────────── clean-up ─────────────────────────────

        private void ClearCorridors()
        {
            var corridors = new List<double2[]>();
            foreach (var l in _out.Lines)
            {
                if (l.Removed) continue;
                if (l.Kind == LineKind.Cross) corridors.Add(PGeo.Corridor(l.A, l.B, l.Width / 2 + 0.05));
                else if (l.Kind == LineKind.Connector)
                {
                    // a little past the junction so no stall pokes into the turning area
                    var ext = l.B + PGeo.Norm(l.B - l.A) * 0.5;
                    corridors.Add(PGeo.Corridor(l.A, ext, l.Width / 2 + 0.3));
                }
                else if (l.Kind == LineKind.Footpath) corridors.Add(PGeo.Corridor(l.A, l.B, 1.0));
            }
            foreach (var b in _out.Bays)
            {
                if (b.Removed) continue;
                foreach (var c in corridors)
                    if (PGeo.ConvexOverlap(b.Quad, c, 1e-4)) { b.Removed = true; break; }
            }
        }

        private void RemoveReserved()
        {
            var f = _out.Frame;
            double r = Math.Min(_s.StallWidth, _s.StallLength) / 2;
            int n = 0;
            foreach (var b in _out.Bays)
                if (!b.Removed && _rq.Blocked(f.ToWorld(b.Center), r)) { b.Removed = true; n++; }
            if (n > 0) Issue(IssueSeverity.Info, "Placemaker.Parking.Issue.ReservedSpace", null, n);
        }

        internal sealed class Dsu
        {
            private readonly int[] _p;
            public Dsu(int n) { _p = new int[Math.Max(1, n)]; for (int i = 0; i < _p.Length; i++) _p[i] = i; }
            public int Find(int x) { while (_p[x] != x) { _p[x] = _p[_p[x]]; x = _p[x]; } return x; }
            public void Union(int a, int b) { a = Find(a); b = Find(b); if (a != b) _p[Math.Max(a, b)] = Math.Min(a, b); }
        }

        /// <summary>Lines joined where an endpoint of one lies on the other (car lines only).</summary>
        private Dsu LineComponents(bool includeConnectors)
        {
            var uf = new Dsu(_out.Lines.Count);
            var ls = _out.Lines;
            for (int i = 0; i < ls.Count; i++)
            {
                var a = ls[i];
                if (a.Removed || a.Kind == LineKind.Footpath || (!includeConnectors && a.Kind == LineKind.Connector)) continue;
                for (int j = 0; j < ls.Count; j++)
                {
                    if (i == j) continue;
                    var b = ls[j];
                    if (b.Removed || b.Kind == LineKind.Footpath || (!includeConnectors && b.Kind == LineKind.Connector)) continue;
                    if (PGeo.DistPointSeg(a.A, b.A, b.B) < 1e-3 || PGeo.DistPointSeg(a.B, b.A, b.B) < 1e-3) uf.Union(i, j);
                }
            }
            return uf;
        }

        /// <summary>
        /// Every stall must be reachable from an entrance over the drive lines. With at least one
        /// connected entrance, unreachable lines and their stalls are dropped (and reported).
        /// </summary>
        private void CheckReachability()
        {
            var uf = LineComponents(true);
            var good = new HashSet<int>();
            foreach (var e in _out.Entrances)
                if (e.Connected && e.ConnectorLine >= 0) good.Add(uf.Find(e.ConnectorLine));
            if (good.Count == 0) return;
            foreach (var l in _out.Lines)
                if (!l.Removed && l.Kind != LineKind.Footpath && !good.Contains(uf.Find(l.Id))) l.Removed = true;
            int lost = 0;
            foreach (var b in _out.Bays)
            {
                if (b.Removed) continue;
                if (b.Line < 0 || _out.Lines[b.Line].Removed) { b.Removed = true; lost++; }
            }
            if (lost > 0)
                Issue(IssueSeverity.Warning, "Placemaker.Parking.Issue.UnreachableStalls", null, lost);
            // footpaths whose lane went away go too
            foreach (var l in _out.Lines)
                if (l.Kind == LineKind.Footpath && !l.Removed)
                {
                    var e = _out.Entrances[l.Entrance];
                    if (!e.Connected || _out.Lines[e.ConnectorLine].Removed) l.Removed = true;
                }
        }

        // ───────────────────────────── chargers ─────────────────────────────

        /// <summary>
        /// One charger per EV pair, centred on the pair's back edge and set back into the planting
        /// (PLT ParkingBayDecals: midpoint of the two decals minus facing·Sl/2; the game then steps
        /// it back until it clears the decals and must stay inside the green strip).
        /// </summary>
        private void AssignChargers()
        {
            foreach (var pair in _out.ElectricPairs)
            {
                var a = _out.Bays[pair.x]; var b = _out.Bays[pair.y];
                var pos = SpecialStalls.ChargerSpot(a, b, out var facing);
                _out.Chargers.Add(new Charger { Position = pos, Facing = facing, BayA = pair.x, BayB = pair.y });
            }
        }

        /// <summary>A charger needs planting behind the pair: inside the lot, off every stall and lane.</summary>
        private bool ChargerSpotOk(double2 p)
        {
            if (!PGeo.Contains(_lot, p) || PGeo.DistToBoundary(_lot, p) < 0.3) return false;
            foreach (var b in _out.Bays)
                if (!b.Removed && math.distance(b.Center, p) < b.Length && PGeo.StrictlyInsideConvex(b.Quad, p, -0.3)) return false;
            foreach (var l in _out.Lines)
                if (!l.Removed && l.Kind != LineKind.Footpath && PGeo.DistPointSeg(p, l.A, l.B) < l.Width / 2 + 0.5) return false;
            return true;
        }

        // ───────────────────────────── asphalt pieces ─────────────────────────────

        private void BuildPieces()
        {
            foreach (var a in _aisles)
            {
                var l = _out.Lines[a.Line];
                if (l.Removed) continue;
                Pieces.Add(new AsphaltPiece
                {
                    Poly = new List<double2> { new double2(l.A.x, a.Y0), new double2(l.B.x, a.Y0), new double2(l.B.x, a.Y1), new double2(l.A.x, a.Y1) },
                    ZoneMask = ZGrid | ZRing,
                });
            }
            foreach (var l in _out.Lines)
            {
                if (l.Removed) continue;
                if (l.Kind == LineKind.Cross)
                    Pieces.Add(new AsphaltPiece { Poly = new List<double2>(PGeo.Corridor(l.A, l.B, l.Width / 2)), ZoneMask = ZGrid | ZRing });
                else if (l.Kind == LineKind.Connector)
                {
                    var outward = l.A - PGeo.Norm(l.B - l.A) * 0.05;
                    Pieces.Add(new AsphaltPiece { Poly = new List<double2>(PGeo.Corridor(outward, l.B, l.Width / 2)), ZoneMask = ZAll });
                }
            }
            // stall runs: hull of consecutive stalls in a row (the triangles between angled stalls are paved too)
            var byRow = new Dictionary<int, List<Bay>>();
            foreach (var b in _out.Bays)
            {
                if (b.Removed) continue;
                if (!byRow.TryGetValue(b.Row, out var list)) byRow[b.Row] = list = new List<Bay>();
                list.Add(b);
            }
            var rows = new List<int>(byRow.Keys);
            rows.Sort();
            foreach (var r in rows)
            {
                var list = byRow[r];
                var dir = list[0].RowDir;
                list.Sort((p, q) => PGeo.Dot(p.Center, dir).CompareTo(PGeo.Dot(q.Center, dir)));
                var run = new List<double2>();
                Bay prev = null;
                int mask = list[0].Kind == BayKind.Perimeter ? ZPerimeter : ZGrid;
                foreach (var b in list)
                {
                    if (prev != null)
                    {
                        double gap = PGeo.Dot(b.Center - prev.Center, dir) - HalfPitch(prev, dir) - HalfPitch(b, dir);
                        if (gap > 0.05)
                        {
                            Pieces.Add(new AsphaltPiece { Poly = PGeo.ConvexHull(run), ZoneMask = mask });
                            run = new List<double2>();
                        }
                    }
                    run.AddRange(b.Quad);
                    prev = b;
                }
                if (run.Count > 0) Pieces.Add(new AsphaltPiece { Poly = PGeo.ConvexHull(run), ZoneMask = mask });
            }
        }

        /// <summary>Half the distance along the row that one stall occupies (its pitch).</summary>
        internal static double HalfPitch(Bay b, double2 rowDir)
        {
            double s = Math.Abs(PGeo.Dot(PGeo.Perp(b.Axis), rowDir));
            return b.Width / Math.Max(0.2, s) / 2;
        }
    }
}
