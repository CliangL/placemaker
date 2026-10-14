// s14 Commercial types: the shared kit. A commercial site is a composition of
//   • lanes the type plans itself (vanilla "Alley" 8 m, or "Pedestrian Street" for outdoor malls), joined to
//     an EXISTING street at their ends (a street a parent planned cannot take a mid-segment junction);
//   • shop rows = zoning cutouts, whole 8 m cells deep, each touching a street or one of those lanes so CS2's
//     zone blocks (which belong to a road) reach them — zoned with the edgeZoning planner itself;
//   • parking lots planned by the Parking layer (aisles, bays, lamps, planting, chargers) on a cut of the
//     site, entered from the street (connector to the kerb) or from a lane ("alley" entrance kind: a short
//     alley stub from the lane's centre line to the lot, sharing a node with the lane — PLT's Gasse);
//   • the rest planned by the Park / Plaza / Market square planners as sub-plans on what is left.
// Everything is kept apart by construction (one "Taken" list of convex pieces), the cutouts are tagged
// "shops" + "place:shops" so siblings keep out and Cutouts.Enforce guarantees they stay empty.

using System;
using System.Collections.Generic;
using Placemaker.Core.Geometry;
using Placemaker.Core.Layers.AddOns;
using Placemaker.Core.Layers.Common;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Commercial
{
    public sealed class Mall
    {
        public const float Cell = StreetFrontage.Cell;
        public const float AlleyHalf = StreetShops.AlleyHalf;
        /// <summary>Lane junctions keep this far from a street corner (m).</summary>
        public const float CornerClear = 12f;
        /// <summary>Synthetic NearbyNet ids for planned lanes (only inside parking sub-plans; never built).</summary>
        private const long SyntheticBase = -700000;

        public static AssetRef AlleyAsset => new AssetRef(StreetShops.AlleyAsset, StreetShops.AlleyFallback);

        /// <summary>
        /// (s26) The lane shops FRONT: zoned plots need the lane's zone blocks. The vanilla Alley is used when it carries
        /// zone blocks (or nobody knows: tests, older catalogs); when the game's catalog says it has none
        /// (AssetInfo.ZoneBlocks = 2), the Two-Lane Road (16 m) — playtest s26: every shop grew off the street behind
        /// the plaza, none off its alley ring. Service lanes behind shops keep <see cref="AlleyAsset"/>.
        /// </summary>
        public static (AssetRef asset, float half) ShopLane(LayerContext ctx)
        {
            var assets = ctx?.Site?.Assets;
            if (assets != null && assets.TryGetValue(StreetShops.AlleyAsset, out var alley) && alley.ZoneBlocks == 2)
            {
                float w = assets.TryGetValue(StreetShops.AlleyFallback, out var road) && road.NetWidth > 0.5f ? road.NetWidth : 16f;
                return (new AssetRef(StreetShops.AlleyFallback), w * 0.5f);
            }
            return (AlleyAsset, AlleyHalf);
        }
        public static AssetRef PedestrianStreet => new AssetRef("Pedestrian Street", "Pedestrian Street Small", StreetShops.AlleyAsset);
        public static AssetRef PedestrianStreetSmall => new AssetRef("Pedestrian Street Small", "Pedestrian Street", StreetShops.AlleyAsset);

        public sealed class Lane
        {
            public readonly List<float2> Points = new List<float2>();
            public NetRole Role = NetRole.Alley;
            public AssetRef Asset;
            public float Half = AlleyHalf;
            public long StartAttach = -1, EndAttach = -1;
            public float StartT, EndT;
            /// <summary>Points on the lane where something joins it (lot alley stubs, paths): the lane is split there.</summary>
            public readonly List<float2> Joins = new List<float2>();
            internal int Index;
        }

        public readonly LayerContext Ctx;
        public readonly LayerRegion Region;
        /// <summary>Everything planned so far (convex pieces): shops, lots, lane corridors, places.</summary>
        public readonly List<float2[]> Taken = new List<float2[]>();
        public readonly List<float2[]> Shops = new List<float2[]>();
        public readonly List<Lane> Lanes = new List<Lane>();
        public readonly List<float2[]> Lots = new List<float2[]>();
        public int Stalls, EvStalls;
        /// <summary>Extra outline points green pieces should open towards (lane dead ends of a shop layout).</summary>
        public readonly List<float2> Doors = new List<float2>();
        private readonly Dictionary<long, (Lane lane, int seg)> _synthetic = new Dictionary<long, (Lane, int)>();
        private readonly List<(float2[] ring, Func<float2, float> height)> _lotGround = new List<(float2[], Func<float2, float>)>();
        /// <summary>(s25) Off: the mall keeps the ground as it is (tests compare against the ungraded plan).</summary>
        public bool Grading = true;
        /// <summary>(s25) Planned ground height of every lane at its centre line (after grading); empty before Finish.</summary>
        public readonly Dictionary<Lane, float[]> LaneProfiles = new Dictionary<Lane, float[]>();

        public Mall(LayerContext ctx)
        {
            Ctx = ctx;
            Region = ctx.LayerRegion;
        }

        // ───────────── frontage ─────────────

        /// <summary>Street-facing outline runs (existing streets only can take lane junctions), longest first.</summary>
        public List<StreetShops.Run> Runs()
        {
            var roads = StreetFrontage.Roads(Ctx.Input);
            var runs = roads.Count == 0 ? new List<StreetShops.Run>() : StreetShops.Runs(Region, roads, null, Ctx.Site?.WaterDepthAt);
            runs.Sort((a, b) => b.Length != a.Length ? b.Length.CompareTo(a.Length) : a.A.x != b.A.x ? a.A.x.CompareTo(b.A.x) : a.A.y.CompareTo(b.A.y));
            return runs;
        }

        /// <summary>How far the area reaches inward from a run point (ray march, m).</summary>
        public float ReachAt(StreetShops.Run r, float s, float max = 600f)
        {
            float extent = 0f;
            for (float t = 2f; t < max; t += 2f)
            {
                var p = r.At(s, t);
                if (!Region.Contains(p) || (Ctx.Site?.WaterDepthAt != null && Ctx.Site.WaterDepthAt(p) > 0.05f)) break;
                extent = t;
            }
            return extent;
        }

        /// <summary>Smallest reach across [s0, s1] (5 samples).</summary>
        public float Reach(StreetShops.Run r, float s0, float s1)
        {
            float m = float.MaxValue;
            for (int k = 0; k <= 4; k++) m = math.min(m, ReachAt(r, math.lerp(s0, s1, k / 4f)));
            return m;
        }

        public static float2[] Rect(StreetShops.Run r, float s0, float s1, float t0, float t1) =>
            Poly.EnsureCcw(new[] { r.At(s0, t0), r.At(s1, t0), r.At(s1, t1), r.At(s0, t1) });

        // ───────────── taking land ─────────────

        public bool Free(float2[] poly)
        {
            foreach (var t in Taken) if (StreetShops.ConvexOverlap(t, poly, 0.05f)) return false;
            return true;
        }

        public bool Fits(float2[] poly) => StreetShops.Fits(Region, poly) && Free(poly);

        /// <summary>One shop plot (whole cells; must touch a street or a lane — the caller's geometry guarantees it).</summary>
        public bool Shop(float2[] rect)
        {
            if (!Fits(rect) || !Faces(rect)) return false;
            Shops.Add(rect);
            Taken.Add(rect);
            return true;
        }

        /// <summary>
        /// True when one side of the plot runs along a street or a lane of this mall (as the edgeZoning planner tests it):
        /// zone blocks belong to a road, so a plot facing none would stay an empty cutout.
        /// </summary>
        public bool Faces(float2[] rect)
        {
            var roads = _roads ?? (_roads = StreetFrontage.Roads(Ctx.Input));
            bool Near(float2 p)
            {
                foreach (var r in roads)
                    if (math.sqrt(SegmentIndex.DistSq(p, r.A, r.B)) <= r.HalfWidth + StreetFrontage.Tolerance) return true;
                foreach (var l in Lanes)
                    for (int i = 0; i + 1 < l.Points.Count; i++)
                        if (math.sqrt(SegmentIndex.DistSq(p, l.Points[i], l.Points[i + 1])) <= l.Half + StreetFrontage.Tolerance) return true;
                return false;
            }
            for (int i = 0; i < rect.Length; i++)
            {
                float2 a = rect[i], b = rect[(i + 1) % rect.Length];
                if (Near(math.lerp(a, b, 0.5f)) && Near(math.lerp(a, b, 0.2f)) && Near(math.lerp(a, b, 0.8f))) return true;
            }
            return false;
        }

        private List<StreetFrontage.RoadLine> _roads;

        /// <summary>
        /// A row of shop plots along a run between s0 and s1, facing the line t = t0 (a street or lane edge) and reaching
        /// <paramref name="cells"/> cells further (sign = direction). Pieces of ≤ 3 cells; each as deep as fits.
        /// </summary>
        public int ShopRow(StreetShops.Run r, float s0, float s1, float t0, int cells, float dir = 1f)
        {
            int made = 0;
            float len = s1 - s0;
            if (len < Cell - 0.01f) return 0;
            int n = math.max(1, (int)math.ceil(len / (3 * Cell)));
            for (int k = 0; k < n; k++)
            {
                float a = math.lerp(s0, s1, (float)k / n), b = math.lerp(s0, s1, (float)(k + 1) / n);
                for (int d = cells; d >= 1; d--)
                    if (Shop(Rect(r, a, b, t0, t0 + dir * d * Cell))) { made++; break; }
            }
            return made;
        }

        /// <summary>Plot depth across (perpendicular) rows: whole cells, clamped 1–6.</summary>
        public static int Cells(float metres) => math.clamp((int)math.floor(metres / Cell + 0.01f), 1, 6);

        // ───────────── lanes ─────────────

        /// <summary>
        /// A lane from the street at outline point <paramref name="start"/> through <paramref name="inner"/> points,
        /// optionally back to the street at <paramref name="end"/>. Null when a street end finds no existing street.
        /// </summary>
        public Lane AddLane(float2 start, float2 startOutward, IList<float2> inner, float2? end, float2 endOutward, NetRole role, AssetRef asset, float half)
        {
            if (!StreetShops.StreetEnd(Ctx.Input, start, startOutward, out var hitA, out long idA, out float tA)) return null;
            var lane = new Lane { Role = role, Asset = asset, Half = half, StartAttach = idA, StartT = tA };
            lane.Points.Add(hitA);
            foreach (var p in inner) lane.Points.Add(p);
            if (end.HasValue)
            {
                if (!StreetShops.StreetEnd(Ctx.Input, end.Value, endOutward, out var hitB, out long idB, out float tB)) return null;
                lane.Points.Add(hitB);
                lane.EndAttach = idB; lane.EndT = tB;
            }
            var corridors = Corridors(lane);
            foreach (var c in corridors) if (!Free(c)) return null;
            lane.Index = Lanes.Count;
            Lanes.Add(lane);
            Taken.AddRange(corridors);
            return lane;
        }

        /// <summary>A lane planned elsewhere (StreetShops layouts); its land is already in Taken.</summary>
        public void AdoptLane(Lane lane)
        {
            lane.Index = Lanes.Count;
            Lanes.Add(lane);
        }

        /// <summary>Lane footprint pieces (the road width; plots may start right at its edge, where its zone blocks begin).</summary>
        private List<float2[]> Corridors(Lane lane)
        {
            var list = new List<float2[]>();
            float h = lane.Half;
            for (int i = 0; i + 1 < lane.Points.Count; i++)
            {
                var a = lane.Points[i]; var b = lane.Points[i + 1];
                var d = math.normalizesafe(b - a); var n = new float2(-d.y, d.x);
                // Square ends overlap at bends so the union is closed; at an attached end only the part inside the area matters.
                float ea = i == 0 ? 0f : h, eb = i + 2 == lane.Points.Count ? (lane.EndAttach >= 0 ? 0f : 0.5f) : h;
                list.Add(Poly.EnsureCcw(new[] { a - d * ea - n * h, b + d * eb - n * h, b + d * eb + n * h, a - d * ea + n * h }));
            }
            return list;
        }

        // ───────────── parking ─────────────

        /// <summary>
        /// A parking lot on <paramref name="ring"/> (∩ the site), with entrances at the given outline points: "entrance"
        /// for one facing an existing street, "alley" for one facing a lane of this mall. Merged into the plan.
        /// </summary>
        public bool Lot(float2[] ring, List<(float2 at, string kind)> entrances, SettingsBag parking, uint salt)
        {
            var gen = GeneratorRegistry.Get("parking");
            if (gen == null || ring == null) return false;
            float2[] lot;
            try { lot = Largest(Poly.Intersection(Region.Ring, Poly.EnsureCcw(ring)), 300f); }
            catch (Exception) { lot = null; }
            if (lot == null || !Free(Shrink(lot, 0.3f))) return false;
            var bag = gen.Defaults();
            foreach (var kv in parking.Values) bag.Values[kv.Key] = kv.Value;
            // The lot's own frontage zoning stays off: the mall zones its shop rows itself.
            bag.Values["edgeZoning"] = "none";
            var markers = new List<EdgeMarker>();
            foreach (var (at, kind) in entrances) markers.Add(new EdgeMarker { Kind = kind, Position = Ctx.P3(at) });
            var site = Ctx.Site.WithHeight(Ctx.Site.HeightAt);
            site.NearbyNets = new List<NearbyNet>(Ctx.Site.NearbyNets);
            _synthetic.Clear();
            foreach (var lane in Lanes)
                for (int i = 0; i + 1 < lane.Points.Count; i++)
                {
                    long id = SyntheticBase - lane.Index * 100 - i;
                    _synthetic[id] = (lane, i);
                    float3 a = Ctx.P3(lane.Points[i]), b = Ctx.P3(lane.Points[i + 1]);
                    site.NearbyNets.Add(new NearbyNet { ExternalId = id, A = a, B = math.lerp(a, b, 1f / 3f), C = math.lerp(a, b, 2f / 3f), D = b, Width = lane.Half * 2f, Category = NetCategory.Road, HasSidewalk = true });
                }
            var input = new AreaInput
            {
                Shape = new AreaShape { Ring = Poly.To3D(lot, Ctx.G), Source = AreaSource.Drawn },
                Site = site, Markers = markers, Settings = bag, Seed = Hashing.Mix(Ctx.Seed, salt),
                RegionTags = new[] { RegionTags.Lot }, Reserved = Ctx.Input.Reserved,
            };
            AreaPlan sub;
            try { sub = gen.Plan(input, Ctx.Cancel); }
            catch (OperationCanceledException) { throw; }
            catch (Exception) { sub = null; }
            if (sub == null || sub.HasErrors) return false;
            sub.Facts.TryGetValue("capacity", out var cap);
            sub.Facts.TryGetValue("evCapacity", out var ev);
            if (cap <= 0) return false;
            // Alley stubs into a lane: CS2 joins nets only at identical nodes — drop the synthetic attachment and
            // split the lane at the stub's end instead.
            foreach (var n in sub.Nets)
            {
                if (_synthetic.TryGetValue(n.StartAttach, out var s0)) { s0.lane.Joins.Add(n.A.xz); n.StartAttach = -1; }
                if (_synthetic.TryGetValue(n.EndAttach, out var s1)) { s1.lane.Joins.Add(n.D.xz); n.EndAttach = -1; }
            }
            sub.Regions.Clear();
            sub.Issues.RemoveAll(i => i.Severity == IssueSeverity.Error || i.Key == "Placemaker.Parking.Issue.NoEntrance");
            Merge(sub);
            // (s25) The lot graded itself (level tiers on a slope): the rest of the mall plans on that ground.
            if (sub.HeightAfter != null) _lotGround.Add((lot, sub.HeightAfter));
            Stalls += (int)cap; EvStalls += (int)ev;
            Lots.Add(lot);
            Taken.Add(lot);
            return true;
        }

        /// <summary>A lot on a frontage run (centred, up to <paramref name="width"/> wide), entered from that street; shrinks to fit.</summary>
        public bool FrontLot(StreetShops.Run r, float width, int aisles, SettingsBag parking, uint salt)
        {
            float from = r.From + (r.StartCorner ? CornerClear : 2f), to = r.To - (r.EndCorner ? CornerClear : 2f);
            float c = 0.5f * (from + to);
            for (float w = math.min(width, to - from); w >= 28f; w -= 8f)
                for (int a = aisles; a >= 1; a--)
                {
                    float d = LotDepth(a);
                    var rect = Rect(r, c - w * 0.5f, c + w * 0.5f, 0f, d);
                    if (!Fits(rect)) continue;
                    var e = new List<(float2, string)>();
                    if (w > 90f) { e.Add((r.At(c - w * 0.25f, 0f), "entrance")); e.Add((r.At(c + w * 0.25f, 0f), "entrance")); }
                    else e.Add((r.At(c, 0f), "entrance"));
                    if (Lot(rect, e, parking, salt)) return true;
                }
            return false;
        }

        public static SettingsBag Parking(LayerContext ctx)
        {
            var b = new SettingsBag();
            b.Values["stallAngle"] = ctx.S("stallAngle");
            b.Values["greenery"] = ctx.S("lotGreen");
            b.Values["evShare"] = (double)ctx.Raw("evShare");
            b.Values["lanterns"] = ctx.B("lamps");
            b.Values["perimeterRoad"] = false;
            b.Values["setback"] = 1.0;
            return b;
        }

        /// <summary>Lot depth for a number of double-loaded aisles (two 5.9 m bays + a 7 m aisle, 2.5 m medians).</summary>
        public static float LotDepth(int aisles) => aisles <= 0 ? 0f : aisles * 18.8f + (aisles - 1) * 2.5f + 2.5f;

        public static int Aisles(string size) => size == "small" ? 1 : size == "large" ? 3 : 2;

        // ───────────── green / plaza sub-plans ─────────────

        /// <summary>The site minus everything taken: pieces of at least <paramref name="minArea"/>, largest first.</summary>
        public List<float2[]> Rest(float minArea)
        {
            var list = new List<float2[]>();
            if (Taken.Count == 0) { list.Add(Region.Ring); return list; }
            var clip = new List<IReadOnlyList<float2>>();
            foreach (var t in Taken) clip.Add(Poly.EnsureCcw(t));
            List<Region> pieces;
            try { pieces = Poly.Boolean(new[] { (IReadOnlyList<float2>)Region.Ring }, clip, BoolOp.Difference); }
            catch (Exception) { return list; }
            list.AddRange(Simple(pieces, minArea));
            list.Sort((a, b) => Poly.Area(b).CompareTo(Poly.Area(a)));
            return list;
        }

        /// <summary>Simple rings of boolean pieces (holes slit open), at least <paramref name="minArea"/>.</summary>
        private static List<float2[]> Simple(List<Region> pieces, float minArea)
        {
            var list = new List<float2[]>();
            // A piece wrapping something (a lot with green all round) has a hole: one ring cannot say that, and a
            // planner given the outer ring would plan over the hole. Slit such pieces through the hole until simple.
            var queue = new Queue<(Region r, int depth)>();
            foreach (var p in pieces) queue.Enqueue((p, 0));
            var simple = new List<Region>();
            while (queue.Count > 0)
            {
                var (p, depth) = queue.Dequeue();
                if (p?.Outer == null || p.Outer.Length < 3) continue;
                if (p.Holes.Count == 0 || depth >= 6) { if (p.Holes.Count == 0) simple.Add(p); continue; }
                var hc = Poly.Centroid(p.Holes[0]);
                float2 lo = new float2(float.MaxValue), hi = new float2(float.MinValue);
                foreach (var v in p.Outer) { lo = math.min(lo, v); hi = math.max(hi, v); }
                var slit = new[] { new float2(lo.x - 1f, hc.y - 0.15f), new float2(hi.x + 1f, hc.y - 0.15f), new float2(hi.x + 1f, hc.y + 0.15f), new float2(lo.x - 1f, hc.y + 0.15f) };
                var cut = new List<IReadOnlyList<float2>> { slit };
                foreach (var h in p.Holes) cut.Add(Poly.EnsureCcw(h));
                try { foreach (var q in Poly.Boolean(new[] { (IReadOnlyList<float2>)p.Outer }, cut, BoolOp.Difference)) queue.Enqueue((q, depth + 1)); }
                catch (Exception) { }
            }
            foreach (var p in simple)
            {
                var ring = Poly.EnsureCcw(Poly.Clean(p.Outer, 0.05f));
                if (ring.Length >= 3 && Poly.Area(ring) >= minArea) list.Add(ring);
            }
            return list;
        }

        /// <summary>
        /// The rest split into wide parts (a morphological opening: shrink by <paramref name="half"/>, grow back) and the thin
        /// arms left over (strips beside lane legs, behind rows) — parks on the wide parts, planted buffers on the arms, so
        /// no park path runs along a thin arm hugging the outline.
        /// </summary>
        public List<(float2[] ring, bool strip)> RestSplit(float minArea, float half = 9f)
        {
            var result = new List<(float2[], bool)>();
            foreach (var piece in Rest(minArea))
            {
                List<Region> core;
                try { core = Poly.Offset(Poly.Offset(piece, half, OffsetJoin.Miter), -half, OffsetJoin.Miter); }
                catch (Exception) { core = null; }
                var wide = new List<float2[]>();
                if (core != null)
                    foreach (var c in core)
                    {
                        if (c?.Outer == null || c.Outer.Length < 3) continue;
                        List<Region> clipped;
                        try { clipped = Poly.Boolean(new[] { (IReadOnlyList<float2>)piece }, new[] { (IReadOnlyList<float2>)Poly.EnsureCcw(c.Outer) }, BoolOp.Intersection); }
                        catch (Exception) { continue; }
                        wide.AddRange(Simple(clipped, minArea));
                    }
                if (wide.Count == 0) { result.Add((piece, true)); continue; }
                foreach (var w in wide) result.Add((w, false));
                var clip = new List<IReadOnlyList<float2>>();
                foreach (var w in wide) clip.Add(w);
                try { foreach (var t in Simple(Poly.Boolean(new[] { (IReadOnlyList<float2>)piece }, clip, BoolOp.Difference), 40f)) result.Add((t, true)); }
                catch (Exception) { }
            }
            result.Sort((a, b) => a.Item2 != b.Item2 ? a.Item2.CompareTo(b.Item2) : Poly.Area(b.Item1).CompareTo(Poly.Area(a.Item1)));
            return result;
        }

        /// <summary>
        /// Plans <paramref name="ring"/> with another type's planner (park, plaza, marketSquare…) and merges the result.
        /// <paramref name="doors"/>: outline points where the piece should open (a lane end, the lot) besides its own suggestions.
        /// </summary>
        public bool Fill(float2[] ring, string layerId, string preset, IReadOnlyList<float2> doors, uint salt, bool suggested = true, Action<SettingsBag> edit = null)
        {
            var gen = GeneratorRegistry.Get(layerId) as LayerBase;
            var region = ring == null ? null : LayerRegion.From(ring);
            if (gen == null || region == null || region.Area < gen.MinArea) return false;
            var settings = gen.Defaults();
            if (!string.IsNullOrEmpty(preset))
                foreach (var p in gen.Presets)
                    if (p.Id == preset) { settings = p.Values.Clone(); break; }
            edit?.Invoke(settings);
            var input = new AreaInput
            {
                Shape = new AreaShape { Ring = Poly.To3D(ring, Ctx.G), Source = AreaSource.Drawn },
                Site = Ctx.Site, Settings = settings, Seed = Hashing.Mix(Ctx.Seed, salt), RegionTags = new[] { RegionTags.Green },
                Reserved = Ctx.Input.Reserved, Markers = new List<EdgeMarker>(),
            };
            string kind = null;
            if (suggested)
                foreach (var m in gen.SuggestMarkers(input) ?? new List<EdgeMarker>())
                {
                    input.Markers.Add(m);
                    kind = kind ?? m.Kind;
                }
            if (doors != null)
            {
                if (kind == null)
                    foreach (var c in gen.Controls)
                        if (c.Kind == ControlKind.Markers && c.MarkerKinds != null && c.MarkerKinds.Length > 0) { kind = c.MarkerKinds[0]; break; }
                if (kind == EntranceKinds.Main || kind == EntranceKinds.Service || kind == EntranceKinds.Bike) kind = EntranceKinds.Pedestrian;
                foreach (var d in doors)
                {
                    var p = region.ClosestOnBoundary(d, out int seg, out float t);
                    if (math.distance(p, d) > 6f) continue;
                    bool near = false;
                    foreach (var m in input.Markers) if (math.distance(m.Position.xz, p) < 8f) near = true;
                    if (!near) input.Markers.Add(new EdgeMarker { Kind = kind ?? "entrance", RingSegment = seg, RingT = t, Position = Ctx.P3(p) });
                }
            }
            AreaPlan sub;
            try { sub = gen.Plan(input, Ctx.Cancel); }
            catch (OperationCanceledException) { throw; }
            catch (Exception) { sub = null; }
            if (sub == null || sub.HasErrors) return false;
            // Its places / cores stay its own business: only plain green / paved tags are offered to the mall's features.
            sub.Regions.RemoveAll(g => Composer.IsPlace(g) || Array.IndexOf(g.Tags, StreetFrontage.ShopsTag) >= 0);
            // CS2 joins nets only at identical nodes: a path ending at a door ends exactly on it (a lane's end node).
            if (doors != null)
                foreach (var n in sub.Nets)
                {
                    if (!PathJoins.IsPath(n)) continue;
                    foreach (var d in doors)
                    {
                        if (n.StartAttach < 0 && math.distancesq(n.A.xz, d) < 6.25f) { var dd = Ctx.P3(d) - n.A; n.A += dd; n.B += dd; }
                        if (n.EndAttach < 0 && math.distancesq(n.D.xz, d) < 6.25f) { var dd = Ctx.P3(d) - n.D; n.D += dd; n.C += dd; }
                    }
                }
            sub.Issues.RemoveAll(i => i.Severity == IssueSeverity.Error);
            Merge(sub);
            Taken.Add(Poly.EnsureCcw(ring));
            return true;
        }

        /// <summary>A leftover narrower than ~18 m (deepest point under 9 m from its edge).</summary>
        public bool IsStrip(float2[] ring)
        {
            var r = LayerRegion.From(ring);
            if (r == null) return true;
            r.DeepestPoint(out float clearance, null, 16);
            return clearance < 9f;
        }

        /// <summary>A planted buffer: lawn and a loose line of trees, no paths (strips beside lanes and rows).</summary>
        public void Buffer(float2[] ring, uint salt)
        {
            var region = LayerRegion.From(ring);
            if (region == null) return;
            Ctx.Surface(ring, SurfaceRole.Grass, new AssetRef(""), AssetCategories.SurfaceGrass);
            var rng = Hashing.Rng(Hashing.Mix(Ctx.Seed, salt), "buffer");
            var placed = new List<float2>();
            float step = 4f;
            for (float y = region.Min.y + step * 0.5f; y < region.Max.y; y += step)
                for (float x = region.Min.x + step * 0.5f; x < region.Max.x; x += step)
                {
                    var p = new float2(x, y) + new float2(rng.NextFloat(-1.2f, 1.2f), rng.NextFloat(-1.2f, 1.2f));
                    if (!region.Contains(p) || region.BoundaryDistance(p, 4f) < 3.2f) continue;
                    bool near = false;
                    foreach (var q in placed) if (math.distancesq(q, p) < 81f) { near = true; break; }
                    if (near) continue;
                    placed.Add(p);
                    Ctx.Object(p, rng.NextFloat(0f, 6.283f), ObjectRole.Tree, new AssetRef(""), Hashing.Mix(Ctx.Seed, salt + (uint)placed.Count), 1 + (int)(rng.NextUInt() % 2));
                }
            Taken.Add(Poly.EnsureCcw(ring));
        }

        private void Merge(AreaPlan sub)
        {
            var plan = Ctx.Plan;
            int offset = plan.Nets.Count;
            plan.Nets.AddRange(sub.Nets);
            foreach (var o in sub.Objects)
            {
                if (o.AttachedNet >= 0) o.AttachedNet += offset;
                plan.Objects.Add(o);
            }
            plan.Surfaces.AddRange(sub.Surfaces);
            plan.Areas.AddRange(sub.Areas);
            plan.Terraform.AddRange(sub.Terraform);
            plan.Paint.AddRange(sub.Paint);
            plan.Zoning.AddRange(sub.Zoning);
            plan.Regions.AddRange(sub.Regions);
            plan.Stats.AddRange(sub.Stats);
            plan.Issues.AddRange(sub.Issues);
            foreach (var kv in sub.Facts)
                plan.Facts[kv.Key] = plan.Facts.TryGetValue(kv.Key, out var v) && kv.Key != "parking.fee" ? v + kv.Value : kv.Value;
        }

        // ───────────── finish: zoning, lanes, regions ─────────────

        /// <summary>Zones every shop plot from the street / lane it faces, emits the lanes and tags the cutouts.</summary>
        public void Finish(string zoneId)
        {
            var plan = Ctx.Plan;
            if (Grading) MallGrading.Grade(this, _lotGround);
            var zoning = new EdgeZoningAddOn();
            var reserved = new ReservedSpace();
            foreach (var lane in Lanes)
                for (int i = 0; i + 1 < lane.Points.Count; i++)
                    reserved.AddCorridor(lane.Points[i], lane.Points[i + 1], lane.Half, lane.Role == NetRole.Alley ? nameof(NetRole.Alley) : nameof(NetRole.Street));
            int cells = 0;
            for (int i = 0; i < Shops.Count; i++)
            {
                var ring = Shops[i];
                var bag = new SettingsBag();
                bag.Values[EdgeZoningAddOn.ZoneControl] = zoneId;
                bag.Values[EdgeZoningAddOn.CutoutSetting] = true;
                var input = new AreaInput
                {
                    Shape = new AreaShape { Ring = Poly.To3D(ring, Ctx.G), Source = AreaSource.Drawn },
                    Site = Ctx.Site, Settings = bag, Reserved = reserved, RegionTags = new[] { StreetFrontage.ShopsTag },
                };
                var z = zoning.Plan(input, Ctx.Cancel);
                plan.Zoning.AddRange(z.Zoning);
                foreach (var q in z.Zoning)
                    cells += (int)math.round(Pricing.PolygonArea(new[] { q.A, q.B, q.C, q.D }) / (Cell * Cell));
                Ctx.AddRegion(ring, StreetFrontage.ShopsTag + ":" + i, StreetFrontage.ShopsTag, LayerBase.ShopsPlace);
            }
            foreach (var lane in Lanes) EmitLane(lane);
            plan.Facts["shopPlots"] = Shops.Count;
            plan.Facts["lanes"] = Lanes.Count;
            plan.Facts["lots"] = Lots.Count;
            plan.Facts["capacity"] = Stalls;
            plan.Facts["evCapacity"] = EvStalls;
            Ctx.Stat("Placemaker.Commercial.Stat.Plots", Shops.Count, Keys.UnitCount);
            Ctx.Stat("Placemaker.Commercial.Stat.ZoneCells", cells, Keys.UnitCount);
        }

        private void EmitLane(Lane lane)
        {
            // Split points per segment (joins projected onto the segment they lie on).
            var pts = new List<float2>();
            for (int i = 0; i + 1 < lane.Points.Count; i++)
            {
                var a = lane.Points[i]; var b = lane.Points[i + 1];
                pts.Add(a);
                var cut = new List<float>();
                float len = math.distance(a, b);
                foreach (var j in lane.Joins)
                {
                    float s = math.dot(j - a, math.normalizesafe(b - a));
                    if (s <= 1f || s >= len - 1f) continue;
                    if (math.distance(a + math.normalizesafe(b - a) * s, j) > 0.5f) continue;
                    cut.Add(s);
                }
                cut.Sort();
                foreach (var s in cut) pts.Add(a + math.normalizesafe(b - a) * s);
            }
            pts.Add(lane.Points[lane.Points.Count - 1]);
            // Joins exactly on a node move that node onto the join (identical nodes).
            for (int k = 0; k < pts.Count; k++)
                foreach (var j in lane.Joins)
                    if (math.distance(pts[k], j) < 1.01f && !(k == 0 && lane.StartAttach >= 0) && !(k == pts.Count - 1 && lane.EndAttach >= 0)) pts[k] = j;
            for (int i = 0; i + 1 < pts.Count; i++)
            {
                if (math.distance(pts[i], pts[i + 1]) < 0.3f) continue;
                var net = Ctx.StraightNet(pts[i], pts[i + 1], lane.Role, lane.Asset);
                if (i == 0 && lane.StartAttach >= 0) { net.StartAttach = lane.StartAttach; net.StartAttachT = lane.StartT; }
                if (i + 2 == pts.Count && lane.EndAttach >= 0) { net.EndAttach = lane.EndAttach; net.EndAttachT = lane.EndT; }
            }
        }

        // ───────────── helpers ─────────────

        public static float2[] Largest(List<Region> pieces, float minArea)
        {
            float2[] best = null; float bestArea = minArea;
            if (pieces == null) return null;
            foreach (var r in pieces)
            {
                if (r?.Outer == null || r.Outer.Length < 3) continue;
                float a = Poly.Area(r.Outer);
                if (a > bestArea) { bestArea = a; best = r.Outer; }
            }
            return best == null ? null : Poly.EnsureCcw(Poly.Clean(best, 0.05f));
        }

        public static float2[] Shrink(float2[] q, float by)
        {
            var c = Poly.Centroid(q);
            var r = new float2[q.Length];
            for (int i = 0; i < q.Length; i++) r[i] = q[i] + math.normalizesafe(c - q[i]) * by;
            return r;
        }
    }
}
