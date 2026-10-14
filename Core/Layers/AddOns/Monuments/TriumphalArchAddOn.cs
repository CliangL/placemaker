// Triumphal gateway: a gate you walk through. A wide walk runs the length of the rectangle (from the
// end people arrive at to the other) and the gateway stands astride it in the middle on a paved
// square: a triumphal arch (scaled), a pair of pylons (obelisks / columns) framing the walk, or a
// propylaea — a screen of columns across the axis with the walk through its central opening. Statues
// on the wings, paired flags or lamps along the walk and clipped hedges framing the long sides.
// No arch installed → pylons; no columns → statues (Info issue, never nothing).

using System.Collections.Generic;
using Placemaker.Core.Layers.Common;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.AddOns.Monuments
{
    [AreaGenerator(173)]
    public sealed class TriumphalArchAddOn : MonumentAddOn
    {
        public const string LayerId = "triumphalArch";
        protected override string Area => "AddOn.TriumphalArch";
        public override string Id => LayerId;

        protected override void DefineControls(ControlSet c)
        {
            c.Choice("kind", "arch", ControlTier.Essential, "gateway", "arch", "pylons", "propylaea").Tip();
            c.Slider("size", 200, 100, 400, 25, "%", ControlTier.Essential, "gateway").Tip();
            c.Asset("archAsset", AssetCategories.MonumentArch, ControlTier.Essential, "gateway").When("kind=arch");
            c.Asset("columnAsset", AssetCategories.MonumentColumn, ControlTier.More, "gateway");
            c.Toggle("wings", true, ControlTier.More, "gateway").Tip();
            c.Choice("avenue", "flags", ControlTier.More, "layout", "flags", "lamps", "trees", "none");
            c.Toggle("square", true, ControlTier.More, "layout").Tip();
            c.Toggle("hedges", true, ControlTier.More, "layout");
            c.Asset("pavingAsset", AssetCategories.SurfacePaving, ControlTier.More, "ground");
            c.Asset("pathAsset", AssetCategories.Pathway, ControlTier.More, "ground");
            c.RandomRange("size", 150, 300);
        }

        protected override void DefinePresets()
        {
            Preset("triumphal", ("kind", "arch"), ("size", 250.0), ("wings", true), ("avenue", "flags"));
            Preset("pylons", ("kind", "pylons"), ("size", 250.0), ("wings", false), ("avenue", "lamps"), ("hedges", true));
            Preset("propylaea", ("kind", "propylaea"), ("size", 175.0), ("wings", true), ("avenue", "trees"), ("square", true));
        }

        protected override void Build(LayerContext ctx, Occupancy occ, OrientedRect site, float2 toward)
        {
            var entry = MonumentKit.Entry(occ, site, toward);
            float e = EndSign(site, entry);
            var d = site.Axis * -e;                       // walking direction: from the entry end through the gate
            var side = new float2(-d.y, d.x);
            float hl = site.HalfLength, hw = site.HalfWidth;
            var C = site.Center;
            var A = site.Local(e * hl, 0f); var B = site.Local(-e * hl, 0f);
            var pathAsset = AssetMix.One(ctx.S("pathAsset"));
            float scale = ctx.F("size") / 100f;

            // Ground first: the paved square under the gate.
            float sqHalf = math.min(hl * 0.35f, 7f);
            if (ctx.B("square"))
                ctx.Surface(new OrientedRect { Center = C, Axis = d, HalfLength = sqHalf, HalfWidth = hw - 0.6f }.Ring, SurfaceRole.Paving,
                    AssetMix.One(ctx.S("pavingAsset")), AssetCategories.SurfacePaving);

            // The walk: A → B through the centre, joined to the entry (shared node) when it arrives from a side.
            var nodes = new List<float2>();
            float la = math.dot(entry - C, d), lat = math.dot(entry - C, side);
            if (math.abs(lat) < 1.5f) { nodes.Add(entry); nodes.Add(B); }
            else
            {
                var knee = C + d * math.clamp(la, -hl + 1f, hl - 1f);
                if (math.abs(math.dot(knee - C, d)) < sqHalf + 1f) knee = C - d * (sqHalf + 1.5f);
                MonumentKit.Path(ctx, occ, entry, knee, NetRole.Pathway, pathAsset);
                nodes.Add(A); nodes.Add(knee); nodes.Add(B);
            }
            for (int i = 0; i + 1 < nodes.Count; i++) MonumentKit.Path(ctx, occ, nodes[i], nodes[i + 1], NetRole.PathwayWide, pathAsset);
            float walk = Composer.DefaultHalfWidth(NetRole.PathwayWide);

            string kind = ctx.S("kind");
            int pieces = 0;
            if (kind == "arch")
            {
                string chosen = ctx.S("archAsset");
                string cat = MonumentKit.Quiet(chosen, AssetCategories.MonumentArch);
                if (cat != null)
                {
                    // Astride the walk: an arch is meant to be walked through (it is not checked against the path).
                    var arch = ctx.Object(C, MonumentKit.Yaw(d), ObjectRole.Furniture, AssetMix.One(chosen), Hashing.Mix(ctx.Seed, 0xA4C0u), -1, cat);
                    arch.Scale = scale;
                    float span = math.max(walk + 1f, AssetMix.FootprintRadius(ctx.Site, AssetMix.One(chosen), 3f) * scale);
                    occ.Take(C + side * span, 1f, 0); occ.Take(C - side * span, 1f, 0);
                    pieces = 1;
                    ctx.Plan.Facts["arch"] = 1;
                }
                else { ctx.Issue(IssueSeverity.Info, K + "Issue.NoArch"); kind = "pylons"; }
            }
            if (kind == "pylons")
            {
                string cat = !string.IsNullOrEmpty(ctx.S("columnAsset")) ? AssetCategories.MonumentColumn
                    : MonumentKit.Quiet("", AssetCategories.MonumentObelisk, AssetCategories.MonumentColumn, AssetCategories.MemorialMonument, AssetCategories.CenterpieceStatue);
                if (cat == null) { AssetAvailability.Require(ctx.Plan, AssetCategories.MonumentObelisk, null, Keys.Category(AssetCategories.MonumentObelisk)); return; }
                for (int s = -1; s <= 1; s += 2)
                    if (MonumentKit.Put(ctx, occ, C + side * s * (walk + 2.2f), MonumentKit.Yaw(d), ObjectRole.Centerpiece, AssetMix.One(ctx.S("columnAsset")), cat, 1.2f,
                        Hashing.Mix(ctx.Seed, 0x9170u), scale * 1.2f) != null) pieces++;
            }
            else if (kind == "propylaea")
            {
                string cat = MonumentKit.Columns(ctx, ctx.S("columnAsset"), out var role);
                if (cat == null) return;
                float spacing = 2.6f * math.max(1f, scale * 0.6f);
                for (int k = 0; k < 4; k++)
                    for (int s = -1; s <= 1; s += 2)
                    {
                        float off = walk + 1.4f + k * spacing;
                        if (off > hw - 1.2f) break;
                        // Two rows (front and back) make the screen read as a porch, not a fence.
                        for (int row = -1; row <= 1; row += 2)
                            if (MonumentKit.Put(ctx, occ, C + side * s * off + d * row * 1.6f, MonumentKit.Yaw(d), role, AssetMix.One(ctx.S("columnAsset")), cat, 0.6f,
                                Hashing.Mix(ctx.Seed, 0xC010u), role == ObjectRole.Tree ? 1f : scale * 0.8f, 0.3f, 0.1f) != null) pieces++;
                    }
            }
            ctx.Plan.Facts["gatePieces"] = pieces;

            if (ctx.B("wings"))
            {
                string sc = MonumentKit.Quiet("", MonumentKit.StatueChain);
                if (sc != null)
                    for (int s = -1; s <= 1; s += 2)
                        for (int row = -1; row <= 1; row += 2)
                        {
                            var q = C + side * s * math.min(hw - 1.5f, walk + 5.5f) + d * row * (sqHalf - 1.2f);
                            MonumentKit.Put(ctx, occ, q, MonumentKit.Yaw(row < 0 ? -d : d), ObjectRole.Centerpiece, new AssetRef(""), sc, 1.1f, Hashing.Mix(ctx.Seed, 0x3170u), math.max(1f, scale * 0.5f));
                        }
            }

            // Avenue pairs along the walk on both sides of the square.
            string avenue = ctx.S("avenue");
            if (avenue != "none")
            {
                ObjectRole role = avenue == "trees" ? ObjectRole.Tree : avenue == "lamps" ? ObjectRole.Lamp : ObjectRole.Furniture;
                string cat = avenue == "trees" ? MonumentKit.Quiet("", AssetCategories.Tree) : avenue == "lamps" ? MonumentKit.Quiet("", AssetCategories.Lamp)
                    : MonumentKit.Quiet("", AssetCategories.MemorialFlag, AssetCategories.Lamp);
                if (cat == AssetCategories.Lamp) role = ObjectRole.Lamp;
                float r = role == ObjectRole.Tree ? 2f : 0.5f;
                float off = walk + (role == ObjectRole.Tree ? 2.8f : 1.2f);
                if (cat != null && off + r < hw - 0.3f)
                    for (int dir = -1; dir <= 1; dir += 2)
                        for (float t = sqHalf + 3f; t < hl - 1.5f; t += role == ObjectRole.Tree ? 8f : 6f)
                        {
                            var a = C + d * dir * t + side * off; var b = C + d * dir * t - side * off;
                            if (!occ.Free(a, r, 0.3f) || !occ.Free(b, r, 0.3f)) continue;
                            MonumentKit.Put(ctx, occ, a, MonumentKit.Yaw(-side), role, new AssetRef(""), cat, r, Hashing.Mix(ctx.Seed, 0xA7E0u + (uint)(t * 3)));
                            MonumentKit.Put(ctx, occ, b, MonumentKit.Yaw(side), role, new AssetRef(""), cat, r, Hashing.Mix(ctx.Seed, 0xA7E1u + (uint)(t * 3)));
                        }
            }
            if (ctx.B("hedges"))
            {
                string hc = MonumentKit.Quiet("", AssetCategories.Hedge, AssetCategories.Bush);
                if (hc != null)
                    for (int s = -1; s <= 1; s += 2)
                        for (float t = -hl + 1.5f; t <= hl - 1.5f; t += 1.8f)
                        {
                            if (math.abs(t) < sqHalf + 0.5f) continue;
                            MonumentKit.Put(ctx, occ, C + d * t + side * s * (hw - 0.9f), MonumentKit.Yaw(d), hc == AssetCategories.Bush ? ObjectRole.Bush : ObjectRole.Furniture,
                                new AssetRef(""), hc, 0.6f, Hashing.Mix(ctx.Seed, 0x4ED0u), 1f, 0.2f, 0.1f);
                        }
            }
            ctx.Stat("Placemaker.Stat.Monuments", 1, Keys.UnitCount);
        }
    }
}
