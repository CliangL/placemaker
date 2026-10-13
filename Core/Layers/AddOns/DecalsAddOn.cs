// Decals feature: scatters flat decal props with a theme, density and clustering. Each theme knows
// where it belongs (playtest wish "decals"):
//   leaves   lawns and paths              dirt     lawns only (paved areas: near the edge)
//   cracks   paving only                  puddles  anywhere free
//   paving   paving only, on a lattice    markings paving only, aligned to aisles/paths
//   graffiti paving near the outline
// "Paving" = reserved path/aisle corridors, or the whole region when it is paved (ground = paved,
// or the region is tagged paved/lot). Decals never go under objects and never reserve space.

using System.Collections.Generic;
using Placemaker.Core.Layers.Common;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.AddOns
{
    [AreaGenerator(150)]
    public sealed class DecalsAddOn : AddOnLayer
    {
        public const string LayerId = "decals";
        public static readonly string[] Themes = { "leaves", "dirt", "cracks", "puddles", "paving", "markings", "graffiti" };

        protected override string Area => "AddOn.Decals";
        public override string Id => LayerId;
        public override string Icon => "Media/Game/Icons/PropsDecals.svg";

        protected override void DefineControls(ControlSet c)
        {
            c.Choice("theme", "leaves", ControlTier.Essential, "decals", Themes);
            c.Slider("density", 2, 0.2, 10, 0.2, "per100m2", ControlTier.Essential, "decals").Tip();
            c.Slider("clustering", 50, 0, 100, 5, "%", ControlTier.Essential, "decals").Tip();
            c.Choice("ground", "auto", ControlTier.More, "decals", "auto", "paved", "lawn").Tip();
            c.AssetMulti("assets", AssetCategories.Decal, ControlTier.More, "decals");
            c.RandomRange("density", 0.6, 3);
            c.Fixed("ground");
        }

        protected override void DefinePresets()
        {
            Preset("autumn", ("theme", "leaves"), ("density", 4.0), ("clustering", 70.0));
            Preset("worn", ("theme", "cracks"), ("density", 1.5), ("clustering", 60.0));
            Preset("rainy", ("theme", "puddles"), ("density", 1.0), ("clustering", 40.0));
            Preset("patterned", ("theme", "paving"), ("density", 1.0), ("clustering", 0.0));
        }

        public static string CategoryFor(string theme)
        {
            switch (theme)
            {
                case "dirt": return AssetCategories.DecalDirt;
                case "cracks": return AssetCategories.DecalCracks;
                case "puddles": return AssetCategories.DecalPuddles;
                case "paving": return AssetCategories.DecalPaving;
                case "markings": return AssetCategories.DecalMarkings;
                case "graffiti": return AssetCategories.DecalGraffiti;
                default: return AssetCategories.DecalLeaves;
            }
        }

        protected override void PlanCore(LayerContext ctx)
        {
            string theme = ctx.S("theme");
            string category = CategoryFor(theme);
            var ids = ctx.L("assets");
            if (!AssetAvailability.Require(ctx.Plan, category, ids, Keys.Category(category))) return;
            var region = ctx.LayerRegion;
            var occ = Occupancy(ctx);
            bool pavedRegion = ctx.S("ground") == "paved" || (ctx.S("ground") == "auto" && HasTag(ctx.Input, RegionTags.Paved, RegionTags.Lot));
            bool lawnOnly = ctx.S("ground") == "lawn";
            int wanted = math.min(3000, (int)math.round(region.Area / 100f * ctx.F("density")));
            float clustering = ctx.F("clustering") / 100f;
            region.PrincipalAxes(out var centre, out var axis, out _, out _);
            float axisYaw = math.atan2(axis.x, axis.y);
            uint clump = Hashing.Mix(ctx.Seed, 0xDEC0u);
            int placed = 0;
            float cell = theme == "paving" ? 3f : 1.2f;
            foreach (var cand in Scatter.Candidates(region, cell, Hashing.Mix(ctx.Seed, 0xDEC1u), math.min(80000, wanted * 12 + 100), ctx.Cancel))
            {
                if (placed >= wanted) break;
                var p = cand;
                float yaw;
                uint v = Hashing.Mix(ctx.Seed, (uint)placed * 2654435761u + 17u);
                if (theme == "paving")
                {
                    // Snap to a 3 m lattice in the principal frame: patterns read as laid, not thrown.
                    var d = p - centre; var side = new float2(-axis.y, axis.x);
                    float u = math.round(math.dot(d, axis) / 3f) * 3f, w = math.round(math.dot(d, side) / 3f) * 3f;
                    p = centre + axis * u + side * w;
                    if (!region.Contains(p)) continue;
                    yaw = axisYaw + (v % 4u) * math.PI * 0.5f;
                }
                else yaw = Hashing.Unit(v) * 6.283f;
                if (clustering > 0f)
                {
                    float chance = Scatter.Clustering(p, 12f, clump, 1f - clustering);
                    if (Hashing.Unit(Hashing.Mix(clump, (int)(p.x * 11f), (int)(p.y * 5f))) > chance) continue;
                }
                if (occ.Discs.Overlaps(p, 0.6f, 0.1f) || occ.HitsPolygon(p, 0.3f) || !region.ContainsDisc(p, 0.3f)) continue;
                bool onCorridor = occ.Corridors.Clearance(p, 1f) < 0f;
                bool paved = pavedRegion || onCorridor;
                float edge = region.BoundaryDistance(p, 6f);
                bool ok;
                switch (theme)
                {
                    case "leaves": case "puddles": ok = !lawnOnly || !onCorridor; break;
                    case "dirt": ok = pavedRegion ? edge < 3f : !onCorridor; break;
                    case "cracks": case "paving": ok = paved && !lawnOnly; break;
                    case "markings":
                        ok = paved && !lawnOnly;
                        int idx = ok ? occ.Corridors.Nearest(p, 3f, out _) : -1;
                        if (idx >= 0)
                        {
                            // Align with the aisle/path under it.
                            var s = occ.Corridors.Segments[idx];
                            var t = math.normalizesafe(s.B - s.A, axis);
                            yaw = math.atan2(t.x, t.y);
                        }
                        else yaw = axisYaw;
                        break;
                    case "graffiti": ok = paved && !lawnOnly && edge < 4f; break;
                    default: ok = true; break;
                }
                if (!ok) continue;
                ctx.Object(p, yaw, ObjectRole.Decal, AssetMix.Pick(ids, v), v, -1, category);
                placed++;
            }
            if (placed == 0) ctx.Issue(IssueSeverity.Info, "Placemaker.AddOn.Decals.Issue.NoGround");
            ctx.Stat("Placemaker.Stat.Decals", placed, Keys.UnitCount);
        }

        private static bool HasTag(AreaInput input, params string[] tags)
        {
            if (input.RegionTags == null) return false;
            foreach (var t in input.RegionTags) foreach (var w in tags) if (t == w) return true;
            return false;
        }
    }
}
