// Placemaker — engine-free contracts shared by every area generator.
//
// Everything in Core/ is pure .NET + Unity.Mathematics: no Game.*, Colossal.*, Unity.Entities.
// That keeps generators runnable off the main thread and testable without the game.
// The game layer (Game/) turns an AreaPlan into CreationDefinitions; generators never
// touch ECS.

using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace Placemaker.Core
{
    // ───────────────────────────── Input ─────────────────────────────

    public enum AreaSource { Drawn = 0, Block = 1, Painted = 2, Perimeter = 3 }

    /// <summary>
    /// The selected area. All selection modes (draw, block pick, paint, node perimeter)
    /// reduce to this. Ring is counter-clockwise in the XZ plane, implicitly closed
    /// (last point != first), simple (no self intersections), ≥3 points. Y = terrain height.
    /// </summary>
    public sealed class AreaShape
    {
        public float3[] Ring = Array.Empty<float3>();
        public AreaSource Source;

        /// <summary>
        /// Points where existing networks touch or cross the outline. Produced by the game
        /// layer (block pick / perimeter mode know the real road entities; draw/paint get
        /// crossings computed against nearby roads).
        /// </summary>
        public List<BoundaryAttachment> Attachments = new List<BoundaryAttachment>();

        public float2[] Ring2D()
        {
            var r = new float2[Ring.Length];
            for (int i = 0; i < Ring.Length; i++) r[i] = Ring[i].xz;
            return r;
        }
    }

    public enum AttachmentKind { RoadAlongEdge = 0, RoadCrossing = 1, RoadNode = 2 }

    public struct BoundaryAttachment
    {
        public AttachmentKind Kind;
        /// <summary>Opaque id of the existing net element (edge or node) — the game layer maps it back to an Entity.</summary>
        public long ExternalId;
        /// <summary>For edges: curve parameter of the touch point on that edge.</summary>
        public float ExternalT;
        public float3 Position;
        /// <summary>Unit direction of the road at the touch point (XZ, y=0).</summary>
        public float3 Direction;
        public float RoadWidth;
        /// <summary>Index of the ring segment (Ring[i] → Ring[i+1]) and parameter along it.</summary>
        public int RingSegment;
        public float RingT;
    }

    /// <summary>Road/path edge near the area, for entrances, access points and connections.</summary>
    public struct NearbyNet
    {
        public long ExternalId;
        public float3 A, B, C, D;          // cubic Bézier control points
        public float Width;
        public NetCategory Category;
        public bool HasSidewalk;
        public bool IsOneWay;
    }

    public enum NetCategory { Road = 0, Pathway = 1, Track = 2, Other = 3 }

    /// <summary>Footprint of something the generator should avoid (building lot, water, utility).</summary>
    public struct Obstacle
    {
        public float2 A, B, C, D;   // oriented quad
        public ObstacleKind Kind;
    }

    public enum ObstacleKind { Building = 0, Water = 1, Utility = 2, Other = 3 }

    /// <summary>
    /// Thread-safe snapshot of the world around the area, captured on the main thread before
    /// planning. Generators may run on a worker thread; they must only read this.
    /// </summary>
    public sealed class SiteContext
    {
        /// <summary>Terrain height at an XZ point. Must be safe to call from any thread.</summary>
        public Func<float2, float> HeightAt = _ => 0f;
        /// <summary>Water depth at an XZ point (0 = dry). Thread-safe.</summary>
        public Func<float2, float> WaterDepthAt = _ => 0f;
        public List<NearbyNet> NearbyNets = new List<NearbyNet>();
        public List<Obstacle> Obstacles = new List<Obstacle>();
        public bool LeftHandTraffic;
        /// <summary>Asset metadata the generator may need for footprints/spacing (keyed by AssetRef.Id).</summary>
        public Dictionary<string, AssetInfo> Assets = new Dictionary<string, AssetInfo>();
    }

    /// <summary>Static facts about a game asset, resolved by the game-layer catalog.</summary>
    public struct AssetInfo
    {
        public string Id;
        public float3 BoundsMin, BoundsMax;   // object-space bounds
        public float NetWidth;                // nets only
        /// <summary>Construction cost: per item (objects), per metre (nets), per m² (surfaces/areas). 0 = free/unknown.</summary>
        public float Cost;
        /// <summary>Monthly upkeep in the same units as Cost. 0 = none/unknown.</summary>
        public float Upkeep;
    }

    /// <summary>A user-placed marker on the outline (entrance, access point, exit…).</summary>
    public struct EdgeMarker
    {
        public string Kind;        // generator-defined, e.g. "entrance", "exit", "access"
        public int RingSegment;
        public float RingT;
        public float3 Position;
    }

    public sealed class AreaInput
    {
        public AreaShape Shape = new AreaShape();
        public SiteContext Site = new SiteContext();
        public List<EdgeMarker> Markers = new List<EdgeMarker>();
        public SettingsBag Settings = new SettingsBag();
        public uint Seed;

        /// <summary>Tags of the region this layer runs in ("root", "block", "green", "edge"…). See RegionTags.</summary>
        public string[] RegionTags = Array.Empty<string>();
        /// <summary>
        /// Space already used by earlier layers in this recipe (nets, objects, surfaces of the parent
        /// and of sibling add-ons). Layers must not place things that overlap it.
        /// </summary>
        public ReservedSpace Reserved = new ReservedSpace();
    }

    /// <summary>
    /// Footprints already claimed by earlier layers. Pure geometry; the composer fills it and
    /// child layers query it. Kept simple on purpose (linear scans with a coarse grid index
    /// built lazily) — recipes produce hundreds to low thousands of items.
    /// </summary>
    public sealed class ReservedSpace
    {
        public struct Corridor { public float2 A, B; public float HalfWidth; public string Tag; }
        public struct Disc { public float2 Center; public float Radius; public string Tag; }

        public readonly List<Corridor> Corridors = new List<Corridor>();
        public readonly List<Disc> Discs = new List<Disc>();
        /// <summary>Polygons that are fully claimed (e.g. a parking lot inside a mixed recipe).</summary>
        public readonly List<float2[]> Polygons = new List<float2[]>();

        public void AddCorridor(float2 a, float2 b, float halfWidth, string tag = "") =>
            Corridors.Add(new Corridor { A = a, B = b, HalfWidth = halfWidth, Tag = tag });
        public void AddDisc(float2 c, float r, string tag = "") =>
            Discs.Add(new Disc { Center = c, Radius = r, Tag = tag });

        /// <summary>True when a disc at p with radius r would overlap anything reserved.</summary>
        public bool Blocks(float2 p, float r)
        {
            foreach (var d in Discs)
                if (math.distancesq(p, d.Center) < (r + d.Radius) * (r + d.Radius)) return true;
            foreach (var c in Corridors)
                if (DistSqToSegment(p, c.A, c.B) < (r + c.HalfWidth) * (r + c.HalfWidth)) return true;
            foreach (var poly in Polygons)
                if (PointInPolygon(p, poly)) return true;
            return false;
        }

        public ReservedSpace Clone()
        {
            var r = new ReservedSpace();
            r.Corridors.AddRange(Corridors);
            r.Discs.AddRange(Discs);
            r.Polygons.AddRange(Polygons);
            return r;
        }

        internal static float DistSqToSegment(float2 p, float2 a, float2 b)
        {
            var ab = b - a;
            float len = math.lengthsq(ab);
            float t = len > 1e-9f ? math.saturate(math.dot(p - a, ab) / len) : 0f;
            return math.distancesq(p, a + ab * t);
        }

        internal static bool PointInPolygon(float2 p, float2[] poly)
        {
            bool inside = false;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
            {
                if (((poly[i].y > p.y) != (poly[j].y > p.y)) &&
                    (p.x < (poly[j].x - poly[i].x) * (p.y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x))
                    inside = !inside;
            }
            return inside;
        }
    }

    /// <summary>Well-known region tags. Generators may define more; these are the shared vocabulary.</summary>
    public static class RegionTags
    {
        public const string Root = "root";        // the whole selected area
        public const string Block = "block";      // a block enclosed by generated roads
        public const string Green = "green";      // unpaved open space (lawn between paths, medians…)
        public const string Paved = "paved";      // plaza-like hard surface
        public const string Edge = "edge";        // strip along the outer boundary (zoning frontage, hedges)
        public const string Part = "part";        // a user split part ("part:0", "part:1" also emitted)
        public const string Lot = "lot";          // parking lot interior
        public const string Core = "core";        // focal centre (plaza centre, park lawn)
    }

    // ───────────────────────────── Settings & schema ─────────────────────────────

    /// <summary>
    /// Generator settings as plain values so the UI, undo history, presets and the build
    /// receipt can all handle any generator uniformly. Values: double, bool, string, string[].
    /// </summary>
    public sealed class SettingsBag
    {
        public readonly Dictionary<string, object> Values = new Dictionary<string, object>(StringComparer.Ordinal);

        public double Num(string id, double fallback) =>
            Values.TryGetValue(id, out var v) && v is IConvertible c ? c.ToDouble(System.Globalization.CultureInfo.InvariantCulture) : fallback;
        public float F(string id, float fallback) => (float)Num(id, fallback);
        public int I(string id, int fallback) => (int)Math.Round(Num(id, fallback));
        public bool B(string id, bool fallback) => Values.TryGetValue(id, out var v) && v is bool b ? b : fallback;
        public string S(string id, string fallback) => Values.TryGetValue(id, out var v) && v is string s ? s : fallback;
        public string[] L(string id) => Values.TryGetValue(id, out var v) && v is string[] a ? a : Array.Empty<string>();

        public SettingsBag Clone()
        {
            var c = new SettingsBag();
            foreach (var kv in Values)
                c.Values[kv.Key] = kv.Value is string[] a ? (string[])a.Clone() : kv.Value;
            return c;
        }

        public bool ContentEquals(SettingsBag other)
        {
            if (other == null || other.Values.Count != Values.Count) return false;
            foreach (var kv in Values)
            {
                if (!other.Values.TryGetValue(kv.Key, out var o)) return false;
                if (kv.Value is string[] a && o is string[] b)
                {
                    if (a.Length != b.Length) return false;
                    for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
                }
                else if (!Equals(kv.Value, o)) return false;
            }
            return true;
        }
    }

    public enum ControlKind
    {
        Slider = 0,      // numeric: Min/Max/Step/Unit
        Toggle = 1,      // bool
        Choice = 2,      // string: one of Options (segmented buttons / icons)
        Asset = 3,       // string: one asset id from AssetCategory ("" = automatic)
        AssetMulti = 4,  // string[]: several asset ids from AssetCategory (empty = automatic mix)
        Markers = 5,     // edge markers of MarkerKinds — tells the UI/tool this generator uses markers
        Arrangement = 6, // string[]: ordered sequence editor (plaza furniture)
    }

    public enum ControlTier { Essential = 0, More = 1 }

    public sealed class ControlOption
    {
        public string Value = "";
        public string LabelKey = "";
        public string Icon = "";      // coui:// path or "" for text only
        public string TooltipKey = "";
    }

    /// <summary>Declarative description of one control. The UI renders controls purely from this.</summary>
    public sealed class ControlSchema
    {
        public string Id = "";
        public ControlKind Kind;
        public ControlTier Tier;
        public string Group = "";        // section id within the tier, e.g. "layout", "planting"
        public string LabelKey = "";
        public string TooltipKey = "";
        public string Icon = "";
        public double Min, Max = 1, Step = 1;
        public string Unit = "";         // "m", "%", "deg", "count", "money"…
        public object Default;
        public List<ControlOption> Options = new List<ControlOption>();
        public string AssetCategory = "";            // for Asset/AssetMulti/Arrangement
        public string[] MarkerKinds = Array.Empty<string>();
        /// <summary>Show only when another control has a value, e.g. "fence=true" or "angleMode=fixed".</summary>
        public string VisibleWhen = "";
        /// <summary>Changing this control keeps the current seed/layout (true) or needs a full re-plan (false). Hint for preview cost.</summary>
        public bool Cheap;
    }

    public sealed class PresetInfo
    {
        public string Id = "";
        public string LabelKey = "";
        public string Icon = "";
        public SettingsBag Values = new SettingsBag();
    }

    /// <summary>
    /// What the generator needs from the user before Build can be pressed beyond a valid area.
    /// Drives the primary button's label ("Place an entrance") and disabled reason.
    /// </summary>
    public sealed class Readiness
    {
        public bool Ready = true;
        public string NextStepKey = "";        // e.g. "Placemaker.Step.PlaceEntrance"
        public string ReasonKey = "";          // tooltip when not ready
        public object[] ReasonArgs = Array.Empty<object>();
    }

    // ───────────────────────────── Generator ─────────────────────────────

    /// <summary>
    /// What a layer does inside a recipe.
    ///  * Fill     — turns a region into something (park, plaza, parking, roads). May emit sub-regions.
    ///  * Split    — divides a region into parts without building much itself (divider line, ratio, grid).
    ///  * AddOn    — adds to a region another layer already filled (fountain, restrooms, playground,
    ///               big trees, edge zoning). Must respect Reserved.
    /// </summary>
    public enum LayerKind { Fill = 0, Split = 1, AddOn = 2 }

    /// <summary>
    /// A built-in feature a Fill layer offers (playground in a park, trees in a plaza, zoning along
    /// new streets). Turning a feature on adds a child RecipeStep running FeatureLayer on Target;
    /// the UI shows features as toggles with their own (collapsed) options. This replaces free-form
    /// mixing in the UI (decision 2026-10-13: refine each type with features, shelve split/mix).
    /// </summary>
    public sealed class FeatureInfo
    {
        /// <summary>IAreaGenerator.Id of the add-on (or fill) layer that implements the feature.</summary>
        public string Layer = "";
        /// <summary>Region of the parent it runs in ("" = whole parent region, or a tag like "core", "green", "edge").</summary>
        public string Target = "";
        public bool DefaultOn;
        /// <summary>Label key override; empty = the feature layer's NameKey.</summary>
        public string LabelKey = "";
        /// <summary>Settings applied on top of the feature layer's defaults when enabled from this parent.</summary>
        public SettingsBag Preset = new SettingsBag();
    }

    /// <summary>Optional: Fill layers that offer built-in features implement this too.</summary>
    public interface IHasFeatures
    {
        IReadOnlyList<FeatureInfo> Features { get; }
    }

    /// <summary>
    /// A layer of a recipe. Historic name kept: every buildable thing — park, plaza, parking,
    /// roads, campground, playground, restrooms, edge zoning — implements this. Adding a new type
    /// = one class with [AreaGenerator] + its localisation keys; the registry, UI, presets,
    /// undo, receipts and composer pick it up automatically.
    /// </summary>
    public interface IAreaGenerator
    {
        /// <summary>Stable id: "parking", "plaza", "park", "roads". Stored in saves — never change.</summary>
        string Id { get; }
        string NameKey { get; }
        string Icon { get; }
        LayerKind Kind { get; }
        /// <summary>
        /// Region tags this layer can run in. Empty = any. E.g. restrooms: {"green","paved"};
        /// park: {} (anywhere). Used by the UI to offer only sensible add-ons/children.
        /// </summary>
        string[] Accepts { get; }
        /// <summary>Region tags this layer emits for children (so the UI can offer "then fill blocks with…").</summary>
        string[] Emits { get; }
        /// <summary>Order of generator schema/receipt format; bump when settings meaning changes.</summary>
        int Version { get; }

        IReadOnlyList<ControlSchema> Controls { get; }
        IReadOnlyList<PresetInfo> Presets { get; }
        SettingsBag Defaults();

        /// <summary>Minimum/maximum area this generator accepts (m²).</summary>
        float MinArea { get; }
        float MaxArea { get; }

        /// <summary>Cheap check run every frame on the main thread.</summary>
        Readiness CheckReady(AreaInput input);

        /// <summary>
        /// Suggest markers (e.g. parking entrances on adjacent roads) when the user has placed none.
        /// May return an empty list. Must be fast (&lt;5 ms).
        /// </summary>
        List<EdgeMarker> SuggestMarkers(AreaInput input);

        /// <summary>
        /// Randomize settings within control ranges, never overriding ids listed in <paramref name="locked"/>
        /// (choices the user made explicitly). Deterministic for a given seed.
        /// </summary>
        SettingsBag Randomize(SettingsBag current, uint seed, ICollection<string> locked);

        /// <summary>
        /// Plan the area. Pure: may run on a worker thread; must honour cancellation
        /// (check token between phases) and never throw for bad geometry — report issues instead.
        /// Deterministic for identical input.
        /// </summary>
        AreaPlan Plan(AreaInput input, System.Threading.CancellationToken cancel);
    }

    // ───────────────────────────── Output ─────────────────────────────

    /// <summary>
    /// Reference to a game asset. Id is the prefab name; the game catalog resolves it and
    /// falls back through Fallbacks, then to the role default. Empty Id = "automatic".
    /// </summary>
    public struct AssetRef
    {
        public string Id;
        public string[] Fallbacks;
        public AssetRef(string id, params string[] fallbacks) { Id = id ?? ""; Fallbacks = fallbacks ?? Array.Empty<string>(); }
        public override string ToString() => Id;
    }

    public enum SurfaceRole { Ground = 0, Paving = 1, Asphalt = 2, Grass = 3, Plaza = 4, Water = 5, Decorative = 6 }

    public sealed class PlannedSurface
    {
        /// <summary>CCW ring, simple, no holes, already verified to survive CS2 triangulation (see SurfaceAcceptance).</summary>
        public float3[] Ring = Array.Empty<float3>();
        public SurfaceRole Role;
        public AssetRef Asset;
    }

    public enum NetRole
    {
        Street = 0, Avenue = 1, CulDeSac = 2, Roundabout = 3, PerimeterRoad = 4,
        Pathway = 10, PathwayWide = 11,
        DriveAisle = 20, DriveConnector = 21, Alley = 22,
        Fence = 30,
    }

    /// <summary>One net segment, emitted as a NetCourse. Curve is a cubic Bézier (straight = colinear controls).</summary>
    public sealed class PlannedNet
    {
        public float3 A, B, C, D;
        public NetRole Role;
        public AssetRef Asset;
        /// <summary>Connect this end to an existing net (BoundaryAttachment/NearbyNet ExternalId) instead of a free node.</summary>
        public long StartAttach = -1, EndAttach = -1;
        public float StartAttachT, EndAttachT;
        /// <summary>Generic composition upgrades (trees, grass, bike lanes…) by game flag name.</summary>
        public string[] Upgrades = Array.Empty<string>();
        /// <summary>Elevation behaviour: true = follow terrain exactly (split where needed).</summary>
        public bool FollowTerrain = true;
    }

    public enum ObjectRole
    {
        Tree = 0, Bush = 1, Planter = 2,
        Bench = 10, Lamp = 11, Bin = 12, Centerpiece = 13, Furniture = 14, FencePiece = 15,
        ParkingBay = 20, Charger = 21, Arrow = 22,
        Marker = 30, AnimalSpawner = 31, AccessPoint = 32, CulDeSacCap = 33, RoundaboutIsland = 34,
    }

    public sealed class PlannedObject
    {
        public float3 Position;
        /// <summary>Yaw in radians around +Y (0 = facing +Z).</summary>
        public float Yaw;
        public ObjectRole Role;
        public AssetRef Asset;
        public float Scale = 1f;
        /// <summary>Tree growth stage 0..3 (young..old), -1 = n/a.</summary>
        public int Age = -1;
        public uint Variation;
        /// <summary>Optional: which planned net this object belongs to (index into AreaPlan.Nets), -1 none.</summary>
        public int AttachedNet = -1;
        /// <summary>
        /// Asset category (AssetCategories), or null/"" = the role's default (RoleCategories.ForObject).
        /// Generic roles such as Furniture MUST set it (playground equipment, picnic tables, restrooms…);
        /// otherwise automatic resolution falls back to "decoration" (FEEDBACK-2 #1: sandwich boards).
        /// </summary>
        public string Category;
    }

    public enum AreaRole { PedestrianArea = 0, Hangaround = 1, Lot = 2 }

    /// <summary>Non-surface areas (navigation/space/lot polygons).</summary>
    public sealed class PlannedArea
    {
        public float3[] Ring = Array.Empty<float3>();
        public AreaRole Role;
        public AssetRef Asset;
    }

    public enum TerraformKind { Level = 0, Lower = 1, Raise = 2 }

    public sealed class PlannedTerraform
    {
        public float3[] Ring = Array.Empty<float3>();   // footprint
        public TerraformKind Kind;
        public float TargetHeight;
        /// <summary>True = permanent change the player must be warned about (e.g. lake basin).</summary>
        public bool Permanent;
        /// <summary>Optional water source after terraform (lake). NaN = none.</summary>
        public float WaterLevel = float.NaN;
        public float3 WaterAnchor;
    }

    public sealed class PlannedZoning
    {
        public float3 A, B, C, D;   // zoning quad
        public string ZoneId = "";
    }

    public enum IssueSeverity { Info = 0, Warning = 1, Error = 2 }

    public sealed class PlanIssue
    {
        public IssueSeverity Severity;
        public string Key = "";
        public object[] Args = Array.Empty<object>();
        /// <summary>Optional location to mark on the map.</summary>
        public float3? At;
    }

    /// <summary>One line in the summary card, e.g. ("Placemaker.Stat.Stalls", 312, "count").</summary>
    public struct PlanStat
    {
        public string Key;
        public double Value;
        public string Unit;
        public PlanStat(string key, double value, string unit) { Key = key; Value = value; Unit = unit; }
    }

    /// <summary>A sub-polygon a layer hands to its children (blocks between roads, lawns between paths…).</summary>
    public sealed class PlannedRegion
    {
        public float3[] Ring = Array.Empty<float3>();
        public string[] Tags = Array.Empty<string>();
        /// <summary>Stable key within the parent plan (e.g. "block:3") so per-region overrides survive re-plans when possible.</summary>
        public string Key = "";
    }

    public sealed class AreaPlan
    {
        public string GeneratorId = "";
        /// <summary>Path of the recipe step that produced this (set by the composer), e.g. "0/2".</summary>
        public string StepPath = "";
        /// <summary>Seed the step was planned with (set by the composer). Automatic assets pick one palette per step from it.</summary>
        public uint Seed;
        public List<PlannedRegion> Regions = new List<PlannedRegion>();
        public List<PlannedSurface> Surfaces = new List<PlannedSurface>();
        public List<PlannedNet> Nets = new List<PlannedNet>();
        public List<PlannedObject> Objects = new List<PlannedObject>();
        public List<PlannedArea> Areas = new List<PlannedArea>();
        public List<PlannedTerraform> Terraform = new List<PlannedTerraform>();
        public List<PlannedZoning> Zoning = new List<PlannedZoning>();
        public List<PlanStat> Stats = new List<PlanStat>();
        public List<PlanIssue> Issues = new List<PlanIssue>();
        /// <summary>Generator-specific facts the game layer needs (e.g. parking capacity for the economy companion).</summary>
        public Dictionary<string, double> Facts = new Dictionary<string, double>(StringComparer.Ordinal);

        public bool HasErrors
        {
            get { foreach (var i in Issues) if (i.Severity == IssueSeverity.Error) return true; return false; }
        }
    }
}
