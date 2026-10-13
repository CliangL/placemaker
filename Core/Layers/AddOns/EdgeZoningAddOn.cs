// Placemaker add-on "edgeZoning": zoning along the road frontage of a region.
// Idea adapted from Grid Road Generator (Ti4goc), Systems/GridRoadToolSystem.Zoning.cs: quads of
// 24 m along each road, painted with the game's zoning tool mechanics (Marquee, never Overwrite —
// cells that are already zoned keep their zone; the game layer applies that flag). Unlike GRG,
// zoning stays inside the region it runs in, so a park in the next block is never zoned over.

using System;
using System.Collections.Generic;
using System.Threading;
using Placemaker.Core.Geometry;
using Placemaker.Core.Layers.Roads;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.AddOns
{
    [AreaGenerator(900)]
    public sealed class EdgeZoningAddOn : IAreaGenerator
    {
        public const string LayerId = "edgeZoning";
        public const string ZoneControl = "zone";
        public const string DepthControl = "depth";

        /// <summary>CS2 zone cells are 8 m; blocks reach 6 cells deep.</summary>
        public const float CellSize = 8f;
        public const float PieceLength = 24f;
        /// <summary>Quads start this far on the road side of the frontage so the first cell row is caught.</summary>
        private const float RoadSideOverlap = 1f;
        /// <summary>Frontage = region edge within road half-width + this of a road centre line.</summary>
        private const float FrontageTolerance = 4f;

        private readonly List<ControlSchema> _controls;

        public EdgeZoningAddOn()
        {
            _controls = new List<ControlSchema>
            {
                new ControlSchema
                {
                    Id = ZoneControl, Kind = ControlKind.Asset, Tier = ControlTier.Essential, Group = "zoning",
                    LabelKey = "Placemaker.EdgeZoning.Control.zone", TooltipKey = "Placemaker.EdgeZoning.Control.zone.Tooltip",
                    AssetCategory = AssetCategories.Zone, Default = "", Cheap = true,
                },
                new ControlSchema
                {
                    Id = DepthControl, Kind = ControlKind.Slider, Tier = ControlTier.Essential, Group = "zoning",
                    LabelKey = "Placemaker.EdgeZoning.Control.depth", TooltipKey = "Placemaker.EdgeZoning.Control.depth.Tooltip",
                    Min = CellSize, Max = 6 * CellSize, Step = CellSize, Unit = "m", Default = 6.0 * CellSize, Cheap = true,
                },
            };
        }

        public string Id => LayerId;
        public string NameKey => "Placemaker.EdgeZoning.Name";
        public string Icon => "Media/Game/Icons/Zones.svg";
        public LayerKind Kind => LayerKind.AddOn;
        public string[] Accepts => new[] { RegionTags.Edge, RegionTags.Block };
        public string[] Emits => Array.Empty<string>();
        public int Version => 1;
        public IReadOnlyList<ControlSchema> Controls => _controls;
        public IReadOnlyList<PresetInfo> Presets => Array.Empty<PresetInfo>();

        public SettingsBag Defaults()
        {
            var b = new SettingsBag();
            foreach (var c in _controls) b.Values[c.Id] = c.Default;
            return b;
        }

        public float MinArea => 50f;
        public float MaxArea => 16_000_000f;

        public Readiness CheckReady(AreaInput input) => new Readiness();
        public List<EdgeMarker> SuggestMarkers(AreaInput input) => new List<EdgeMarker>();

        public SettingsBag Randomize(SettingsBag current, uint seed, ICollection<string> locked) => current?.Clone() ?? Defaults();

        public AreaPlan Plan(AreaInput input, CancellationToken cancel)
        {
            var plan = new AreaPlan();
            var ring3 = input?.Shape?.Ring;
            if (ring3 == null || ring3.Length < 3) return plan;
            var ring = Poly.Clean(Poly.To2D(ring3), 0.05f);
            if (ring.Length < 3) return plan;
            var settings = input.Settings ?? new SettingsBag();
            float depth = (float)Math.Max(CellSize, Math.Min(6 * CellSize, settings.Num(DepthControl, 6 * CellSize)));
            if (!math.isfinite(depth)) depth = 6 * CellSize;
            string zone = settings.S(ZoneControl, "");
            Func<float2, float> h = input.Site?.HeightAt ?? (_ => 0f);

            var roads = CollectRoads(input);
            if (roads.Count == 0) return plan;

            int n = ring.Length;
            int quads = 0;
            for (int i = 0; i < n; i++)
            {
                cancel.ThrowIfCancellationRequested();
                float2 a = ring[i], b = ring[(i + 1) % n];
                float len = math.distance(a, b);
                if (len < 4f) continue;
                int pieces = math.max(1, (int)math.ceil(len / PieceLength));
                for (int k = 0; k < pieces; k++)
                {
                    float2 p0 = math.lerp(a, b, (float)k / pieces), p1 = math.lerp(a, b, (float)(k + 1) / pieces);
                    float2 mid = 0.5f * (p0 + p1);
                    if (!Fronts(mid, roads) || !Fronts(p0 + (p1 - p0) * 0.1f, roads) || !Fronts(p1 - (p1 - p0) * 0.1f, roads)) continue;
                    float2 dir = math.normalizesafe(p1 - p0);
                    float2 inward = new float2(-dir.y, dir.x); // CCW ring: interior on the left
                    float d = FitDepth(ring, p0, p1, inward, depth);
                    if (d < CellSize * 0.75f) continue;
                    if (Occupied(input.Reserved, p0, p1, inward, d)) continue;
                    float2 q0 = p0 - inward * RoadSideOverlap, q1 = p1 - inward * RoadSideOverlap;
                    float2 q2 = p1 + inward * d, q3 = p0 + inward * d;
                    plan.Zoning.Add(new PlannedZoning
                    {
                        A = new float3(q0.x, h(q0), q0.y),
                        B = new float3(q1.x, h(q1), q1.y),
                        C = new float3(q2.x, h(q2), q2.y),
                        D = new float3(q3.x, h(q3), q3.y),
                        ZoneId = zone,
                    });
                    quads++;
                }
            }
            plan.Stats.Add(new PlanStat("Placemaker.EdgeZoning.Stat.Area", Math.Round(ZonedArea(plan)), "m2"));
            plan.Facts["edgeZoning.quads"] = quads;
            if (quads == 0)
                plan.Issues.Add(new PlanIssue { Severity = IssueSeverity.Info, Key = "Placemaker.EdgeZoning.Issue.NoFrontage" });
            return plan;
        }

        private static double ZonedArea(AreaPlan plan)
        {
            double a = 0;
            foreach (var z in plan.Zoning)
                a += Pricing.PolygonArea(new[] { z.A, z.B, z.C, z.D });
            return a;
        }

        /// <summary>Deepest depth (≤ max) whose inner corners stay inside the region and clear of reserved space.</summary>
        private static float FitDepth(float2[] ring, float2 p0, float2 p1, float2 inward, float max)
        {
            for (float d = max; d >= CellSize * 0.75f; d -= CellSize)
            {
                float2 c0 = p0 + inward * d, c1 = p1 + inward * d, cm = 0.5f * (c0 + c1);
                if (Inside(ring, c0) && Inside(ring, c1) && Inside(ring, cm) && !CrossesBoundary(ring, p0, p1, c0, c1)) return d;
            }
            return 0f;
        }

        /// <summary>
        /// True when something an earlier layer placed (objects, claimed lots) sits in the quad: zoned
        /// cells under a park bench or a parking lot would let buildings grow over them.
        /// </summary>
        private static bool Occupied(ReservedSpace reserved, float2 p0, float2 p1, float2 inward, float d)
        {
            if (reserved == null) return false;
            for (int i = 0; i <= 4; i++)
                for (int j = 1; j <= 4; j++)
                {
                    float2 p = math.lerp(p0, p1, i / 4f) + inward * (d * j / 4f);
                    foreach (var disc in reserved.Discs)
                        if (math.distancesq(p, disc.Center) < disc.Radius * disc.Radius) return true;
                    foreach (var poly in reserved.Polygons)
                        if (poly != null && poly.Length >= 3 && Poly.Contains(poly, p)) return true;
                }
            return false;
        }

        private static bool Inside(float2[] ring, float2 p) => Poly.Contains(ring, p) || Poly.DistanceToBoundary(ring, p) < 0.05f;

        /// <summary>The quad's inner side or its two depth sides must not cut through the region outline.</summary>
        private static bool CrossesBoundary(float2[] ring, float2 p0, float2 p1, float2 c0, float2 c1)
        {
            float2 s0 = math.lerp(p0, c0, 0.02f), s1 = math.lerp(p1, c1, 0.02f);
            int n = ring.Length;
            for (int i = 0; i < n; i++)
            {
                float2 a = ring[i], b = ring[(i + 1) % n];
                if (Poly.SegmentsIntersect(c0, c1, a, b) && Seg2DistanceTo(c0, c1, a, b) > 0.05f) return true;
                if (StrictCross(s0, c0, a, b) || StrictCross(s1, c1, a, b)) return true;
            }
            return false;
        }

        private static float Seg2DistanceTo(float2 c0, float2 c1, float2 a, float2 b) =>
            math.min(math.min(Dist(a, c0, c1), Dist(b, c0, c1)), math.min(Dist(c0, a, b), Dist(c1, a, b)));

        private static bool StrictCross(float2 p, float2 q, float2 a, float2 b)
        {
            float d1 = Poly.Cross(b - a, p - a), d2 = Poly.Cross(b - a, q - a);
            float d3 = Poly.Cross(q - p, a - p), d4 = Poly.Cross(q - p, b - p);
            return ((d1 > 1e-4f && d2 < -1e-4f) || (d1 < -1e-4f && d2 > 1e-4f)) && ((d3 > 1e-4f && d4 < -1e-4f) || (d3 < -1e-4f && d4 > 1e-4f));
        }

        private static float Dist(float2 p, float2 a, float2 b)
        {
            float2 ab = b - a;
            float l = math.lengthsq(ab);
            float t = l > 1e-9f ? math.saturate(math.dot(p - a, ab) / l) : 0f;
            return math.distance(p, a + ab * t);
        }

        private struct RoadLine { public float2 A, B; public float HalfWidth; }

        /// <summary>Road centre lines next to the region: nets reserved by the parent layer, and existing roads.</summary>
        private static List<RoadLine> CollectRoads(AreaInput input)
        {
            var list = new List<RoadLine>();
            if (input.Reserved != null)
                foreach (var c in input.Reserved.Corridors)
                    if (IsRoadTag(c.Tag)) list.Add(new RoadLine { A = c.A, B = c.B, HalfWidth = c.HalfWidth });
            if (input.Site?.NearbyNets != null)
                foreach (var nn in input.Site.NearbyNets)
                {
                    if (nn.Category != NetCategory.Road) continue;
                    var curve = new RoadCurve(nn.A, nn.B, nn.C, nn.D);
                    float hw = nn.Width > 0f ? nn.Width * 0.5f : Composer.DefaultHalfWidth(NetRole.Street);
                    float2 prev = curve.A.xz;
                    for (int i = 1; i <= 8; i++)
                    {
                        float2 p = curve.Position(i / 8f).xz;
                        list.Add(new RoadLine { A = prev, B = p, HalfWidth = hw });
                        prev = p;
                    }
                }
            return list;
        }

        private static bool IsRoadTag(string tag) =>
            tag == nameof(NetRole.Street) || tag == nameof(NetRole.Avenue) || tag == nameof(NetRole.CulDeSac)
            || tag == nameof(NetRole.Roundabout) || tag == nameof(NetRole.PerimeterRoad);

        private static bool Fronts(float2 p, List<RoadLine> roads)
        {
            foreach (var r in roads)
                if (Dist(p, r.A, r.B) <= r.HalfWidth + FrontageTolerance) return true;
            return false;
        }
    }
}
