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

        // s34
        public const string StatWalkways = "Placemaker.Roads.Stat.Walkways";
        public const string StatSteepest = "Placemaker.Roads.Stat.Steepest";
        public const string IssueIslands = "Placemaker.Roads.Issue.IslandsDropped";
        public const string IssueNotConnected = "Placemaker.Roads.Issue.NotConnected";
        public const string IssueSteep = "Placemaker.Roads.Issue.Steep";
        public const string FactWalkways = "roads.walkways";
        public const string FactLinks = "roads.links";
        public const string FactIslandsDropped = "roads.islandsDropped";
        public const string FactAccessCuts = "roads.accessCuts";
        public const string FactLanes = "roads.lanes";
        public const string FactSteepest = "roads.steepestGrade";
        private const int MaxSteepIssues = 6;
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
            // (s34) Follow contours: the long streets of a grid / superblocks are laid onto contour lines.
            bool followContours = settings.S(RoadsIds.TerrainFit, "auto") == "follow";
            if (followContours && follow && (pattern == RoadPattern.Grid || pattern == RoadPattern.Superblock)
                && GridGenerator.TryContourDirection(new List<float2>(boundary), heightAt, out float2 contourAxis, out float slopeAnisotropy) && slopeAnisotropy > 0.3f)
            {
                float rot = math.radians(gp.AngleOffsetDegrees);
                float2 axis = new float2(contourAxis.x * math.cos(rot) - contourAxis.y * math.sin(rot), contourAxis.x * math.sin(rot) + contourAxis.y * math.cos(rot));
                float pitch = pattern == RoadPattern.Grid ? gp.SpacingMeters : gp.SuperblockZoneMeters / 3f;
                RoadContourWarp.Apply(net, heightAt, axis, pitch, boundary);
            }

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
                removedByObstacles += obstacles.RemoveBlocked(net, null, e => hw(e.Role) + 1f);
                // (s34) No stub pruning here: on tree-like layouts (organic, superblocks) it cascaded through whole
                // networks (the rural playtest site lost every road). Dead ends are handled once the network is final
                // (RoadConnectivity.CleanDeadEnds: short stubs go, long ones become cul-de-sacs).
                if (removedByObstacles > 0) AnchorOutlineNodes(net, boundary, source);
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
                    removedByObstacles += obstacles.RemoveBlocked(net, e => e.Role == NetRole.PerimeterRoad, e => hw(e.Role) + 1f);
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
            net.DropFragments(MinFragmentLength);
            cancel.ThrowIfCancellationRequested();

            // ── 5b. (s34) Network design: access, connectivity, dead ends, lanes, junctions, walkways, grades ──
            var ctx = new RoadDesign.Context
            {
                Boundary = boundary, Existing = existing, Obstacles = obstacles, Height = follow ? heightAt : null, HalfWidth = hw,
                Perimeter = perimeter, JunctionSpacing = RoadsSchema.Num(settings, controls, RoadsIds.JunctionSpacing),
                MaxGrade = RoadsSchema.Num(settings, controls, RoadsIds.MaxGrade) / 100f,
                Walkways = RoadsSchema.Num(settings, controls, RoadsIds.Walkways) / 100f,
                WalkwayStyle = settings.S(RoadsIds.WalkwayStyle, "paved"),
                BackLanes = RoadsSchema.Num(settings, controls, RoadsIds.BackLanes) / 100f,
                LaneStyle = settings.S(RoadsIds.LaneStyle, "alley"),
                TipStyle = settings.S(RoadsIds.TipStyle, "same"),
                Seed = input.Seed == 0 ? 1u : input.Seed,
                FollowContours = followContours && follow,
            };
            if (!connect) ctx.Existing = new List<ExistingRoad>();
            var design = RoadDesign.Run(net, ctx, cancel);
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
            float totalLength = 0f, walkLength = 0f;
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
                    Asset = AssetOf(e, settings, street),
                    Control = RoadRoles.ControlOf(e.Role),
                    StartAttach = sa, EndAttach = da,
                    StartAttachT = sa >= 0 ? st : 0f, EndAttachT = da >= 0 ? dt : 0f,
                    Upgrades = e.AssetOverride.Length > 0 || e.Walk ? Array.Empty<string>() : RoadsSchema.Upgrades(settings, e.Role),
                    FollowTerrain = follow,
                    Category = string.IsNullOrEmpty(e.Category) ? null : e.Category,
                };
                plan.Nets.Add(net0);
                float len = curve.LengthXZ(12);
                if (e.Walk) walkLength += len; else totalLength += len;
                if (e.Walk && e.Trail) TrailPaint(plan, curve, heightAt, input.Seed);
                if (cap && capStyle != "none")
                {
                    float width = e.AssetOverride == RoadRoles.Gravel ? 8f : 2f * hw(e.Role);
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
            // (s34) What the network design did that the player should know.
            var conn = design.Connectivity;
            if (conn.Dropped > 0)
            {
                float3? at = null;
                if (conn.DroppedAt.Count > 0) at = new float3(conn.DroppedAt[0].x, heightAt(conn.DroppedAt[0]), conn.DroppedAt[0].y);
                plan.Issues.Add(new PlanIssue { Severity = IssueSeverity.Info, Key = IssueIslands, Args = new object[] { conn.Dropped, Math.Round(conn.DroppedLength) }, At = at });
            }
            if (conn.CityUnreached && plan.Nets.Count > 0)
                plan.Issues.Add(new PlanIssue { Severity = IssueSeverity.Warning, Key = IssueNotConnected });
            if (follow)
            {
                int shown = 0;
                foreach (var (g, at) in design.Steep)
                {
                    if (shown++ >= MaxSteepIssues) break;
                    plan.Issues.Add(new PlanIssue { Severity = IssueSeverity.Warning, Key = IssueSteep, Args = new object[] { Math.Round(g * 100f) }, At = at });
                }
            }

            // ── 6b. (s34) Pre-zone every new road ───────────────────────
            string zoneId = RoadPreZone.ZoneId(settings.S(RoadsIds.PreZone, RoadPreZone.None));
            if (zoneId.Length > 0 && plan.Nets.Count > 0)
            {
                plan.Zoning.AddRange(RoadPreZone.Quads(plan.Nets, site, zoneId, heightAt));
                plan.Facts[RoadPreZone.Fact] = 1;
            }

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
                    // Walkways cross blocks without dividing them (they are footpaths, not streets).
                    if (!e.Alive || e.Walk) continue;
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
            if (walkLength > 0f) plan.Stats.Add(new PlanStat(StatWalkways, Math.Round(walkLength), "m"));
            if (follow && design.SteepestGrade > 0.005f) plan.Stats.Add(new PlanStat(StatSteepest, Math.Round(design.SteepestGrade * 100f), "%"));
            plan.Facts[FactWalkways] = walkLength;
            plan.Facts[FactLinks] = conn.Links + conn.CityLinks;
            plan.Facts[FactIslandsDropped] = conn.Dropped;
            plan.Facts[FactAccessCuts] = design.AccessCuts;
            plan.Facts[FactLanes] = design.Lanes;
            plan.Facts[FactSteepest] = design.SteepestGrade;
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
                // The width of what will really be placed: the pick, else (automatic) the street pick for
                // street-like roles, else the first automatic prefab this playset has (Four-Lane avenues are 24 m).
                string id = RoleAssetId(role, settings);
                if (Known(site, id, out float w)) return w;
                if (string.IsNullOrEmpty(id))
                {
                    if ((role == NetRole.Street || role == NetRole.PerimeterRoad || role == NetRole.CulDeSac) && Known(site, street, out w)) return w;
                    foreach (var pick in RoadRoles.AutoPicks(role))
                        if (Known(site, pick, out w)) return w;
                }
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
                case NetRole.Alley: return s.S(RoadsIds.LaneAsset, "");
                case NetRole.Avenue: return s.S(RoadsIds.AvenueAsset, "");
                case NetRole.Roundabout: return s.S(RoadsIds.RoundaboutAsset, "");
                case NetRole.CulDeSac: return s.S(RoadsIds.LocalAsset, "");
                case NetRole.Pathway: return s.S(RoadsIds.PathAsset, "");
                case NetRole.PerimeterRoad:
                    string p = s.S(RoadsIds.PerimeterAsset, "");
                    return p.Length > 0 ? p : s.S(RoadsIds.StreetAsset, "");
                default: return s.S(RoadsIds.StreetAsset, "");
            }
        }

        /// <summary>
        /// The road of a role. An explicit pick falls back to the street pick, then the role's automatic
        /// prefabs. Automatic (empty id) carries the role's preferred vanilla prefabs as fallbacks
        /// (RoadRoles.AutoPicks: Two-Lane streets, Four-Lane avenues, Two-Lane One-Way rings) — AutoAssets
        /// takes the first one this playset has, else the category pool.
        /// </summary>
        public static AssetRef AssetFor(NetRole role, SettingsBag s, string street)
        {
            string id = RoleAssetId(role, s);
            if (role == NetRole.Pathway) return new AssetRef(id);
            var picks = RoadRoles.AutoPicks(role);
            var fb = new List<string>(picks.Length + 1);
            bool streetLike = role == NetRole.PerimeterRoad || role == NetRole.Street || role == NetRole.CulDeSac;
            if (string.IsNullOrEmpty(id))
            {
                // Automatic local / perimeter roads follow an explicit street pick (a gravel village stays gravel).
                if (streetLike && !string.IsNullOrEmpty(street)) return new AssetRef(street, picks);
                return new AssetRef("", picks);
            }
            if (!string.IsNullOrEmpty(street) && street != id) fb.Add(street);
            foreach (var p in picks) if (p != id && !fb.Contains(p)) fb.Add(p);
            return new AssetRef(id, fb.ToArray());
        }

        /// <summary>(s34) The asset of a planned piece: its explicit override (gravel tips / lanes), a walkway's path, else the role's.</summary>
        private static AssetRef AssetOf(RoadEdge e, SettingsBag s, string street)
        {
            if (e.Walk)
            {
                string path = s.S(RoadsIds.PathAsset, "");
                return e.Trail && path.Length == 0 ? Layers.Park.ParkPathNets.Trail("") : new AssetRef(path);
            }
            var normal = AssetFor(e.Role, s, street);
            if (string.IsNullOrEmpty(e.AssetOverride)) return normal;
            var fb = new List<string>();
            if (!string.IsNullOrEmpty(normal.Id) && normal.Id != e.AssetOverride) fb.Add(normal.Id);
            foreach (var f in normal.Fallbacks ?? Array.Empty<string>()) if (!string.IsNullOrEmpty(f) && !fb.Contains(f) && f != e.AssetOverride) fb.Add(f);
            return new AssetRef(e.AssetOverride, fb.ToArray());
        }

        /// <summary>
        /// (s34) Worn ground along a dirt walkway: a feathered dirt paint corridor 5 m wide with a ragged edge.
        /// Extension point for s33's terrain paint toolkit (painted trails): replace this with its trail brush when merged.
        /// </summary>
        private static void TrailPaint(AreaPlan plan, RoadCurve curve, Func<float2, float> heightAt, uint seed)
        {
            var pts = new List<float3>();
            curve.Flatten(pts, null, 3f, 8f);
            if (pts.Count < 2) return;
            const float half = 2.5f;
            var left = new List<float2>();
            var right = new List<float2>();
            for (int i = 0; i < pts.Count; i++)
            {
                float2 a = pts[math.max(0, i - 1)].xz, b = pts[math.min(pts.Count - 1, i + 1)].xz;
                float2 d = math.normalizesafe(b - a);
                float2 n = new float2(-d.y, d.x);
                left.Add(pts[i].xz + n * half);
                right.Add(pts[i].xz - n * half);
            }
            right.Reverse();
            left.AddRange(right);
            var ring = Poly.Clean(left.ToArray(), 0.3f);
            if (ring.Length < 3 || !Poly.IsSimple(ring)) return;
            if (Poly.SignedArea(ring) < 0f) Array.Reverse(ring);
            plan.Paint.Add(new PlannedPaint
            {
                Ring = Poly.To3D(ring, heightAt), Layer = TerrainMaterials.Dirt, Strength = 0.8f, Feather = 1.6f, Noise = 0.9f, NoiseScale = 5f,
                Seed = seed ^ math.hash(new float2(curve.A.x, curve.A.z)),
            });
            // Terrain paint stays when the roads are removed (the shared warning every painting type gives).
            bool warned = false;
            foreach (var i in plan.Issues) warned |= i.Key == Layers.Common.TerrainPaint.PermanentKey;
            if (!warned) plan.Issues.Add(new PlanIssue { Severity = IssueSeverity.Warning, Key = Layers.Common.TerrainPaint.PermanentKey });
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
