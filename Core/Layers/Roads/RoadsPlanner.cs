// Placemaker roads layer — the planning pipeline.
//
//   generate (Grid Road Generator patterns, Ti4goc) → split at existing roads → avoid obstacles
//   → perimeter road → drop pieces lying on existing roads → connect ends to existing roads
//   → prune stubs → heights → nets/objects → blocks and frontage regions.
//
// Pure and deterministic: no statics, no shared caches; safe to run concurrently.

using System;
using System.Collections.Generic;
using System.Threading;
using Placemaker.Core.Geometry;
using Placemaker.Core.Layers.Roads.Generators;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Roads
{
    internal static class RoadsPlanner
    {
        public const string StatLength = "Placemaker.Roads.Stat.Length";
        public const string StatBlocks = "Placemaker.Roads.Stat.Blocks";
        public const string StatJunctions = "Placemaker.Roads.Stat.Junctions";
        public const string StatConnections = "Placemaker.Roads.Stat.Connections";

        public const string IssueNothing = "Placemaker.Roads.Issue.NothingFits";
        public const string IssueFlat = "Placemaker.Roads.Issue.TerrainFlat";
        public const string IssueObstacles = "Placemaker.Roads.Issue.ObstaclesAvoided";
        public const string IssueFailed = "Placemaker.Roads.Issue.GenerationFailed";
        public const string IssueBadArea = "Placemaker.Roads.Issue.InvalidArea";

        public const string FactLength = "roads.length";
        public const string FactBlocks = "roads.blocks";
        public const string FactJunctions = "roads.junctions";
        public const string FactConnections = "roads.connections";
        public const string FactRemoved = "roads.removedByObstacles";

        private const float MinFragmentLength = 30f;
        private const float RingClipMargin = 1.5f;

        public static AreaPlan Plan(AreaInput input, IReadOnlyList<ControlSchema> controls, CancellationToken cancel)
        {
            var plan = new AreaPlan();
            var site = input.Site ?? new SiteContext();
            var settings = input.Settings ?? new SettingsBag();
            Func<float2, float> heightAt = SafeHeight(site.HeightAt);

            float2[] ring2 = CleanRing(input.Shape?.Ring);
            if (ring2.Length < 3)
            {
                plan.Issues.Add(new PlanIssue { Severity = IssueSeverity.Error, Key = IssueBadArea });
                return plan;
            }
            float meanY = 0f;
            foreach (var p in ring2) meanY += heightAt(p);
            meanY /= ring2.Length;

            GridParameters gp = RoadsSchema.ToGridParameters(settings, controls, out RoadPattern pattern);
            gp.HeightAt = heightAt;
            gp.Cancel = cancel;
            AreaSource source = input.Shape.Source;
            bool perimeter = PerimeterWanted(settings.S(RoadsIds.PerimeterRoad, "auto"), source);
            bool connect = settings.B(RoadsIds.ConnectExisting, true);
            bool avoid = settings.B(RoadsIds.AvoidObstacles, true);
            bool follow = settings.B(RoadsIds.FollowTerrain, true) || pattern == RoadPattern.Contour;
            bool loopFamily = RoadsSchema.IsLoopFamily(pattern);

            // Drawn/painted outlines get rounded corners when a perimeter road follows them (GRG free area).
            var genRing = new List<float3>(ring2.Length);
            foreach (var p in ring2) genRing.Add(new float3(p.x, meanY, p.y));
            float corner = RoadsSchema.Num(settings, controls, RoadsIds.PerimeterCornerRadius);
            if (perimeter && corner > 0.5f && (source == AreaSource.Drawn || source == AreaSource.Painted))
            {
                var smooth = FreeAreaPerimeter.Smooth(genRing, corner);
                var cleaned = CleanRing(ToArray(smooth, meanY));
                if (cleaned.Length >= 3 && Poly.IsSimple(cleaned))
                {
                    genRing.Clear();
                    foreach (var p in cleaned) genRing.Add(new float3(p.x, meanY, p.y));
                }
            }
            float2[] boundary = new float2[genRing.Count];
            for (int i = 0; i < boundary.Length; i++) boundary[i] = genRing[i].xz;

            // ── 1. Generate ─────────────────────────────────────────────
            if (pattern == RoadPattern.Contour && GridGenerator.IsTerrainFlat(genRing, heightAt))
            {
                plan.Issues.Add(new PlanIssue { Severity = IssueSeverity.Warning, Key = IssueFlat });
                return plan;
            }
            List<RoadSegmentDef> segments;
            RoundaboutInfo roundabout = default;
            try
            {
                segments = loopFamily
                    ? GridGenerator.GenerateLoopGrid(genRing, gp)
                    : GridGenerator.GenerateGrid(genRing, gp, out _, out roundabout);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception e)
            {
                plan.Issues.Add(new PlanIssue { Severity = IssueSeverity.Error, Key = IssueFailed, Args = new object[] { e.GetType().Name + ": " + e.Message } });
                return plan;
            }
            cancel.ThrowIfCancellationRequested();

            var net = new RoadNetwork();
            foreach (var s in segments)
            {
                if (!MathEx.Finite(s.Start) || !MathEx.Finite(s.End)) continue;
                if (math.distance(s.Start.xz, s.End.xz) < 0.5f) continue;
                var curve = s.IsArc ? RoadCurve.Fit(s.Start, s.StartTangent, s.EndTangent, s.End) : RoadCurve.Straight(s.Start, s.End);
                net.Edges.Add(new RoadEdge(curve, RoleOf(s, pattern, loopFamily), s.IsCulDeSacEnd));
            }
            net.Weld();

            var existing = connect ? RoadConnections.Collect(site) : new List<ExistingRoad>();
            var attachments = connect ? input.Shape.Attachments : null;
            Func<NetRole, float> hw = HalfWidths(settings, site);

            // ── 2. Existing roads crossing the new streets ──────────────
            if (existing.Count > 0)
            {
                RoadConnections.SplitAtExisting(net, existing);
                net.Weld();
            }
            cancel.ThrowIfCancellationRequested();

            // ── 3. Obstacles (interior) ─────────────────────────────────
            int removedByObstacles = 0;
            RoadObstacles obstacles = null;
            if (avoid)
            {
                Poly.Bounds(ring2, out float2 mn, out float2 mx);
                obstacles = new RoadObstacles(site, mn, mx);
                removedByObstacles += obstacles.RemoveBlocked(net);
                if (removedByObstacles > 0)
                {
                    AnchorOutlineNodes(net, boundary, source);
                    net.PruneStubs();
                }
            }
            cancel.ThrowIfCancellationRequested();

            // ── 4. Perimeter road ────────────────────────────────────────
            bool anyInterior = false;
            foreach (var e in net.Edges) anyInterior |= e.Alive;
            if (perimeter && anyInterior)
            {
                var interior = new List<RoadSegmentDef>();
                foreach (var e in net.Edges) if (e.Alive) interior.Add(new RoadSegmentDef(e.Curve.A, e.Curve.D, false));
                var crossings = RoadConnections.RingCrossings(genRing, existing, attachments);
                foreach (var s in FreeAreaPerimeter.PerimeterRoad(genRing, interior, crossings))
                {
                    if (!MathEx.Finite(s.Start) || !MathEx.Finite(s.End) || math.distance(s.Start.xz, s.End.xz) < 0.5f) continue;
                    var curve = s.IsArc ? RoadCurve.Fit(s.Start, s.StartTangent, s.EndTangent, s.End) : RoadCurve.Straight(s.Start, s.End);
                    net.Edges.Add(new RoadEdge(curve, NetRole.PerimeterRoad));
                }
                net.Weld();
                if (obstacles != null)
                    removedByObstacles += obstacles.RemoveBlocked(net, e => e.Role == NetRole.PerimeterRoad);
            }
            cancel.ThrowIfCancellationRequested();

            // ── 5. Connect to existing roads, drop duplicates of them ───
            if (existing.Count > 0) RoadConnections.RemoveOverlapping(net, existing, hw);
            int attachedCount = 0;
            if (connect && (existing.Count > 0 || HasNodeAttachments(attachments)))
            {
                RoadConnections.Attach(net, existing, attachments);
                net.Weld();
                net.CollapseShortEdges(GridGenerator.MinSegmentLength);
                net.Weld();
            }
            AnchorOutlineNodes(net, boundary, source);
            net.PruneStubs();
            net.DropFragments(MinFragmentLength);
            cancel.ThrowIfCancellationRequested();

            // ── 6. Output nets & objects ─────────────────────────────────
            int[] degree = net.Degrees(true);
            string street = settings.S(RoadsIds.StreetAsset, "");
            var junctions = 0;
            for (int n = 0; n < degree.Length; n++)
            {
                if (degree[n] >= 3) junctions++;
                if (degree[n] > 0 && net.Nodes[n].Attach >= 0) attachedCount++;
            }
            float totalLength = 0f;
            var caps = new List<(float2 p, float r)>();
            string capStyle = settings.S(RoadsIds.CapStyle, "asphalt");
            string capSize = settings.S(RoadsIds.CapSize, "auto");
            foreach (var e in net.Edges)
            {
                if (!e.Alive) continue;
                var a = net.Nodes[e.N0];
                var d = net.Nodes[e.N1];
                float3 pa = Lift(a, existing, attachments, heightAt, follow, meanY);
                float3 pd = Lift(d, existing, attachments, heightAt, follow, meanY);
                var curve = e.Curve.WithEnds(pa, pd);
                curve.B.y = math.lerp(pa.y, pd.y, 1f / 3f);
                curve.C.y = math.lerp(pa.y, pd.y, 2f / 3f);
                long sa = a.Attach, da = d.Attach;
                float st = a.AttachT, dt = d.AttachT;
                bool cap = e.CapAtEnd && degree[e.N1] == 1 && d.Attach < 0;
                if (e.Role == NetRole.Roundabout && site.LeftHandTraffic)
                {
                    // GRG emits roundabout rings counter-clockwise (right-hand traffic).
                    curve = curve.Reversed();
                    (sa, da) = (da, sa); (st, dt) = (dt, st);
                }
                var net0 = new PlannedNet
                {
                    A = curve.A, B = curve.B, C = curve.C, D = curve.D,
                    Role = e.Role,
                    Asset = AssetFor(e.Role, settings, street),
                    StartAttach = sa, EndAttach = da,
                    StartAttachT = sa >= 0 ? st : 0f, EndAttachT = da >= 0 ? dt : 0f,
                    Upgrades = RoadsSchema.Upgrades(settings, e.Role),
                    FollowTerrain = follow,
                };
                plan.Nets.Add(net0);
                totalLength += curve.LengthXZ(12);
                if (cap && capStyle != "none")
                {
                    float width = 2f * hw(e.Role);
                    float2 tangent = e.Role == NetRole.Roundabout && site.LeftHandTraffic ? -curve.TangentXZ(0f) : curve.TangentXZ(1f);
                    float3 tip = e.Role == NetRole.Roundabout && site.LeftHandTraffic ? curve.A : curve.D;
                    plan.Objects.Add(new PlannedObject
                    {
                        Position = tip,
                        Yaw = math.atan2(tangent.x, tangent.y),
                        Role = ObjectRole.CulDeSacCap,
                        Asset = CapAsset(capStyle, capSize, width),
                        AttachedNet = plan.Nets.Count - 1,
                    });
                    caps.Add((tip.xz, CapRadius(width)));
                }
            }
            if (roundabout.HasRoundabout && settings.B(RoadsIds.RoundaboutIsland, true) && MathEx.Finite(roundabout.Center)
                && (obstacles == null || !obstacles.BlockedAt(roundabout.Center.xz)) && IsJunction(net, degree, roundabout.Center.xz))
            {
                float2 c = roundabout.Center.xz;
                plan.Objects.Add(new PlannedObject
                {
                    Position = new float3(c.x, follow ? heightAt(c) : meanY, c.y),
                    Role = ObjectRole.RoundaboutIsland,
                    Asset = IslandAsset(roundabout.Radius),
                    Scale = 1f,
                });
            }
            cancel.ThrowIfCancellationRequested();

            if (plan.Nets.Count == 0)
            {
                plan.Issues.Add(new PlanIssue { Severity = IssueSeverity.Warning, Key = IssueNothing });
            }
            if (removedByObstacles > 0)
                plan.Issues.Add(new PlanIssue { Severity = IssueSeverity.Info, Key = IssueObstacles, Args = new object[] { removedByObstacles } });

            // ── 7. Regions ───────────────────────────────────────────────
            var blocks = new List<RoadBlocks.Block>();
            if (plan.Nets.Count > 0)
            {
                var lines = new List<RoadBlocks.Line>();
                var frontIndex = new SegmentIndex(16f);
                var frontWidths = new List<float>();
                var pts = new List<float3>();
                foreach (var e in net.Edges)
                {
                    if (!e.Alive) continue;
                    pts.Clear();
                    e.Curve.Flatten(pts, null, 4f, 6f);
                    var l = new List<float2>(pts.Count);
                    foreach (var p in pts) l.Add(p.xz);
                    float w = hw(e.Role);
                    lines.Add(new RoadBlocks.Line { Points = l, HalfWidth = w, Kind = RoadBlocks.KindGenerated, Role = e.Role });
                    frontWidths.Add(w);
                    for (int i = 0; i + 1 < l.Count; i++) frontIndex.Add(l[i], l[i + 1], frontWidths.Count - 1);
                }
                foreach (var r in existing) AddClipped(lines, r, boundary);
                var along = input.Shape.Attachments;
                blocks = RoadBlocks.Compute(boundary, lines, caps, along, cancel);
                RoadBlocks.Order(blocks, RoadBlocks.PrincipalAxis(ring2));
                float depth = RoadsSchema.Num(settings, controls, RoadsIds.EdgeDepth);
                int edgeKey = 0;
                for (int b = 0; b < blocks.Count; b++)
                {
                    cancel.ThrowIfCancellationRequested();
                    var tags = blocks[b].RoundaboutIsland ? new[] { RegionTags.Block, RegionTags.Core } : new[] { RegionTags.Block };
                    plan.Regions.Add(new PlannedRegion { Ring = Poly.To3D(blocks[b].Ring, heightAt), Tags = tags, Key = "block:" + b });
                }
                for (int b = 0; b < blocks.Count; b++)
                {
                    cancel.ThrowIfCancellationRequested();
                    foreach (var strip in RoadBlocks.Frontage(blocks[b].Ring, frontIndex, frontWidths, depth))
                        plan.Regions.Add(new PlannedRegion { Ring = Poly.To3D(strip, heightAt), Tags = new[] { RegionTags.Edge }, Key = "edge:" + edgeKey++ });
                }
            }

            plan.Stats.Add(new PlanStat(StatLength, Math.Round(totalLength), "m"));
            plan.Stats.Add(new PlanStat(StatBlocks, blocks.Count, "count"));
            plan.Stats.Add(new PlanStat(StatJunctions, junctions, "count"));
            if (attachedCount > 0) plan.Stats.Add(new PlanStat(StatConnections, attachedCount, "count"));
            plan.Facts[FactLength] = totalLength;
            plan.Facts[FactBlocks] = blocks.Count;
            plan.Facts[FactJunctions] = junctions;
            plan.Facts[FactConnections] = attachedCount;
            plan.Facts[FactRemoved] = removedByObstacles;
            return plan;
        }

        public static bool PerimeterWanted(string mode, AreaSource source)
        {
            if (mode == "on") return true;
            if (mode == "off") return false;
            // Block and perimeter picks are already bounded by roads.
            return source == AreaSource.Drawn || source == AreaSource.Painted;
        }

        private static NetRole RoleOf(RoadSegmentDef s, RoadPattern pattern, bool loopFamily)
        {
            if (s.IsRoundabout) return NetRole.Roundabout;
            if (s.IsPedestrian) return NetRole.Pathway;
            // Contour: one road type only (GRG: uphill links are flagged as avenues internally).
            if (s.IsAvenue && pattern != RoadPattern.Contour) return NetRole.Avenue;
            if (!loopFamily && ((s.IsCulDeSacEnd && !s.KeepNetwork) || s.IsLocal)) return NetRole.CulDeSac;
            return NetRole.Street;
        }

        private static Func<NetRole, float> HalfWidths(SettingsBag settings, SiteContext site)
        {
            string street = settings.S(RoadsIds.StreetAsset, "");
            return role =>
            {
                string id = RoleAssetId(role, settings);
                if (Known(site, id, out float w)) return w;
                if ((role == NetRole.Street || role == NetRole.PerimeterRoad || role == NetRole.CulDeSac) && string.IsNullOrEmpty(id) && Known(site, street, out w)) return w;
                return Composer.DefaultHalfWidth(role);
            };
        }

        private static bool Known(SiteContext site, string id, out float halfWidth)
        {
            halfWidth = 0f;
            if (string.IsNullOrEmpty(id) || site.Assets == null || !site.Assets.TryGetValue(id, out var info)) return false;
            if (!(info.NetWidth > 0.5f) || !math.isfinite(info.NetWidth)) return false;
            halfWidth = info.NetWidth * 0.5f;
            return true;
        }

        private static string RoleAssetId(NetRole role, SettingsBag s)
        {
            switch (role)
            {
                case NetRole.Avenue: return s.S(RoadsIds.AvenueAsset, "");
                case NetRole.Roundabout: return s.S(RoadsIds.RoundaboutAsset, "");
                case NetRole.CulDeSac: return s.S(RoadsIds.LocalAsset, "");
                case NetRole.Pathway: return s.S(RoadsIds.PathAsset, "");
                default: return s.S(RoadsIds.StreetAsset, "");
            }
        }

        private static AssetRef AssetFor(NetRole role, SettingsBag s, string street)
        {
            string id = RoleAssetId(role, s);
            if (role == NetRole.Pathway) return new AssetRef(id);
            if (string.IsNullOrEmpty(id)) return new AssetRef(role == NetRole.PerimeterRoad || role == NetRole.Street ? street : "");
            return string.IsNullOrEmpty(street) || street == id ? new AssetRef(id) : new AssetRef(id, street);
        }

        /// <summary>CS2 ships turning circles named CulDeSac{Small|Medium|Large|XL}{01 asphalt|02 grass|03 trees}.</summary>
        public static AssetRef CapAsset(string style, string size, float roadWidth)
        {
            string code = style == "grass" ? "02" : style == "trees" ? "03" : "01";
            string[] sizes = { "Small", "Medium", "Large", "XL" };
            int s = size == "small" ? 0 : size == "medium" ? 1 : size == "large" ? 2 : size == "xl" ? 3
                : roadWidth < 7f ? 0 : roadWidth < 14f ? 1 : roadWidth < 22f ? 2 : 3;
            if (code == "01" && s == 3) s = 2; // no asphalt XL variant
            var fallbacks = new List<string>();
            for (int k = s - 1; k >= 0; k--) fallbacks.Add("CulDeSac" + sizes[k] + code);
            return new AssetRef("CulDeSac" + sizes[s] + code, fallbacks.ToArray());
        }

        private static float CapRadius(float roadWidth) => math.max(roadWidth * 0.8f, roadWidth * 0.5f + 4f);

        /// <summary>CS2 roundabout islands {Small|Medium|Large|XL}Roundabout01 by radius (GRG thresholds).</summary>
        public static AssetRef IslandAsset(float radius)
        {
            string size = radius < 10f ? "Small" : radius < 15f ? "Medium" : radius < 20f ? "Large" : "XL";
            return new AssetRef(size + "Roundabout01", "MediumRoundabout01", "SmallRoundabout01");
        }

        private static bool IsJunction(RoadNetwork net, int[] degree, float2 p)
        {
            for (int n = 0; n < net.Nodes.Count; n++)
                if (degree[n] >= 3 && math.distance(net.Nodes[n].Position.xz, p) < 2f) return true;
            return false;
        }

        private static bool HasNodeAttachments(List<BoundaryAttachment> attachments)
        {
            if (attachments == null) return false;
            foreach (var a in attachments) if (a.Kind == AttachmentKind.RoadNode) return true;
            return false;
        }

        /// <summary>Nodes on an outline that is itself made of existing roads count as connected.</summary>
        private static void AnchorOutlineNodes(RoadNetwork net, float2[] boundary, AreaSource source)
        {
            bool outlineIsRoad = source == AreaSource.Block || source == AreaSource.Perimeter;
            foreach (var n in net.Nodes)
                if (outlineIsRoad && Poly.DistanceToBoundary(boundary, n.Position.xz) < 1f) n.Anchored = true;
        }

        private static float3 Lift(RoadNode n, List<ExistingRoad> existing, List<BoundaryAttachment> attachments,
            Func<float2, float> heightAt, bool follow, float meanY)
        {
            float3 p = n.Position;
            if (n.Attach >= 0)
            {
                foreach (var r in existing)
                    if (r.Id == n.Attach) return n.AttachT <= 0f ? r.Curve.A : n.AttachT >= 1f ? r.Curve.D : r.Curve.Position(n.AttachT);
                if (attachments != null)
                    foreach (var a in attachments)
                        if (a.ExternalId == n.Attach && a.Kind == AttachmentKind.RoadNode) return a.Position;
            }
            p.y = follow ? heightAt(p.xz) : meanY;
            return p;
        }

        /// <summary>
        /// Existing road clipped to the area. Pieces stop just beyond the outline (so they cross a road
        /// lying on it and get noded there); roads running along the outline within a small margin stay.
        /// </summary>
        private static void AddClipped(List<RoadBlocks.Line> lines, ExistingRoad r, float2[] boundary)
        {
            var pts = new List<float3>();
            r.Curve.Flatten(pts, null, 4f, 6f);
            bool Keep(float2 p) => Poly.Contains(boundary, p) || Poly.DistanceToBoundary(boundary, p) <= RingClipMargin;
            List<float2> current = null;
            void Flush()
            {
                if (current != null && current.Count >= 2)
                    lines.Add(new RoadBlocks.Line { Points = current, HalfWidth = r.HalfWidth, Kind = RoadBlocks.KindExisting, Role = NetRole.Street });
                current = null;
            }
            for (int i = 0; i < pts.Count; i++)
            {
                float2 p = pts[i].xz;
                bool keep = Keep(p);
                if (i > 0)
                {
                    float2 q = pts[i - 1].xz;
                    bool keptBefore = current != null;
                    if (keep != keptBefore && BoundaryCrossing(boundary, q, p, out float t))
                    {
                        // Extend 0.5 m past the outline crossing.
                        float2 dir = math.normalizesafe(p - q);
                        float2 x = math.lerp(q, p, t);
                        if (keep)
                        {
                            current = new List<float2> { x - dir * (RingClipMargin + 0.5f) };
                        }
                        else
                        {
                            current.Add(x + dir * (RingClipMargin + 0.5f));
                            Flush();
                        }
                    }
                }
                if (keep)
                {
                    if (current == null) current = new List<float2>();
                    current.Add(p);
                }
                else Flush();
            }
            Flush();
        }

        private static bool BoundaryCrossing(float2[] ring, float2 a, float2 b, out float t)
        {
            t = 0f;
            float best = float.MaxValue;
            for (int i = 0; i < ring.Length; i++)
                if (Seg.Intersect(a, b, ring[i], ring[(i + 1) % ring.Length], out float ta, out _) && ta < best) { best = ta; t = ta; }
            return best <= 1f;
        }

        private static Func<float2, float> SafeHeight(Func<float2, float> h)
        {
            if (h == null) return _ => 0f;
            return p =>
            {
                float v = h(p);
                return math.isfinite(v) ? v : 0f;
            };
        }

        private static float2[] ToArray(List<float3> pts, float y)
        {
            var r = new float2[pts.Count];
            for (int i = 0; i < r.Length; i++) r[i] = pts[i].xz;
            return r;
        }

        private static float2[] CleanRing(float3[] ring)
        {
            if (ring == null) return Array.Empty<float2>();
            var pts = new List<float2>(ring.Length);
            foreach (var p in ring) if (MathEx.Finite(p)) pts.Add(p.xz);
            return CleanRing(pts.ToArray());
        }

        private static float2[] CleanRing(float2[] ring)
        {
            if (ring == null || ring.Length < 3) return Array.Empty<float2>();
            var c = Poly.Clean(ring, 0.05f);
            return c.Length >= 3 ? c : Array.Empty<float2>();
        }
    }
}
