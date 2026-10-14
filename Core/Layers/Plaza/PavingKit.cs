// Art pass a2 (playtest 3: "a flat grey slab"): squares are laid out in real paving, not one surface.
//
// Why a composition of non-overlapping pieces: every paved surface is drawn through the −94 overlay
// clone (BuildService.OverlayPavedSurfaces), so two paved surfaces that overlap z-fight, and a grass
// surface under a paved one is simply hidden. A square's design (border band, rondel, axes, bands,
// lawns, planting beds) is therefore cut into a tiling: each element claims its shape minus what the
// elements before it claimed, the field gets what is left. CS2 areas have no holes, so annuli and
// frames are opened with Poly.CutHoles.
//
// Palettes name explicit assets from the player's real catalog (modded paving packs first — G87
// Urban Surfaces, EAI Surface Pack — then the vanilla surface that reads closest), so a square never
// depends on the automatic pool. Vanilla look (thumbnails): Tiles 01 light grey setts, Tiles 02 warm
// beige setts, Tiles 03 dark grey slab grid, Concrete 02 pale, Pavement 01 asphalt-dark.

using System;
using System.Collections.Generic;
using Placemaker.Core.Geometry;
using Placemaker.Core.Layers.Common;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Plaza
{
    /// <summary>Four stones of one square: the field, the framing bands, the light accent, the dark inlay.</summary>
    public sealed class PavingPalette
    {
        public string Id;
        public AssetRef Field, Band, Accent, Inlay;
    }

    public static class PavingPalettes
    {
        private const string G87 = "G87 Urban Surfaces Paving Pack ";
        private const string Eai = "EAI - Surface Pack ";

        public const string SlabGrey = G87 + "Tiles G87 Paving Slab A Grey Surface";
        public const string SlabBeige = G87 + "Tiles G87 Paving Slab A Beige Surface";
        public const string SlabWhite = G87 + "Tiles G87 Paving Slab A White Surface";
        public const string SlabBlack = G87 + "Tiles G87 Paving Slab B Black Surface";
        public const string ClayD = G87 + "Brick G87 Clay Paver D Surface";
        public const string ClayF = G87 + "Brick G87 Clay Paver F Surface";
        public const string ClayH = G87 + "Brick G87 Clay Paver H Surface";
        public const string Setts = G87 + "Pavement G87 Cobble Natural Stone 02 Surface";
        public const string WarmCobble = G87 + "Pavement G87 Cobble Natural Stone 03 Surface";
        public const string ArcCobble = G87 + "Pavement G87 Cobble Natural Stone 04 Surface";
        public const string CrazyPaving = G87 + "Pavement G87 Cobble Natural Stone 07 Surface";
        public const string DarkFlags = G87 + "Pavement G87 Cobble Natural Stone 08 Surface";
        public const string LargeSlabs = G87 + "Concrete G87 Regular Blocks 13 Surface";
        public const string LargeSlabsDark = G87 + "Concrete G87 Regular Blocks 14 Surface";
        public const string Blocks = G87 + "Concrete G87 Regular Blocks 12 Surface";
        public const string GreyStone = Eai + "Tiles Grey Paving Stone Surface";
        public const string Herringbone = Eai + "Tiles Herringbone Grey Paving Stone 001 Surface";
        public const string Hexagonal = Eai + "Tiles Hexagonal Dark Grey Paving Stone Surface";
        public const string Polished = Eai + "Concrete Polished Concrete Surface";
        public const string RedBrick = Eai + "Brick Red Brick Surface";
        public const string WalkingStone = Eai + "Tiles Grey Walking stone Surface";

        public const string VTilesLight = "Tiles Surface 01", VTilesWarm = "Tiles Surface 02", VTilesDark = "Tiles Surface 03";
        public const string VConcrete = "Concrete Surface 01", VConcretePale = "Concrete Surface 02", VAsphalt = "Pavement Surface 01";
        public const string VGrass = "Grass Surface 01";

        private static AssetRef R(string id, params string[] fallbacks) => new AssetRef(id, fallbacks);

        public static readonly PavingPalette Civic = new PavingPalette
        {
            Id = "civic",
            Field = R(SlabGrey, GreyStone, VTilesLight), Band = R(DarkFlags, WalkingStone, VTilesDark),
            Accent = R(SlabWhite, Polished, VConcretePale), Inlay = R(ArcCobble, Hexagonal, VTilesDark),
        };
        public static readonly PavingPalette Warm = new PavingPalette
        {
            Id = "warm",
            Field = R(SlabBeige, VTilesWarm), Band = R(ClayD, RedBrick, VTilesDark),
            Accent = R(SlabWhite, Polished, VConcretePale), Inlay = R(ClayF, RedBrick, VTilesDark),
        };
        public static readonly PavingPalette Garden = new PavingPalette
        {
            Id = "garden",
            Field = R(ClayH, Herringbone, VTilesWarm), Band = R(WarmCobble, Setts, VTilesLight),
            Accent = R(CrazyPaving, SlabBeige, VConcretePale), Inlay = R(ClayD, RedBrick, VTilesDark),
        };
        public static readonly PavingPalette Modern = new PavingPalette
        {
            Id = "modern",
            Field = R(LargeSlabs, Polished, VConcretePale), Band = R(SlabBlack, Hexagonal, VTilesDark),
            Accent = R(Polished, SlabWhite, VConcrete), Inlay = R(LargeSlabsDark, WalkingStone, VTilesDark),
        };
        public static readonly PavingPalette Cobble = new PavingPalette
        {
            Id = "cobble",
            Field = R(Setts, GreyStone, VTilesLight), Band = R(DarkFlags, WalkingStone, VTilesDark),
            Accent = R(SlabGrey, Polished, VConcretePale), Inlay = R(ArcCobble, Hexagonal, VTilesDark),
        };
        public static readonly PavingPalette Transit = new PavingPalette
        {
            Id = "transit",
            Field = R(Blocks, GreyStone, VTilesLight), Band = R(SlabBlack, Hexagonal, VTilesDark),
            Accent = R(LargeSlabs, Polished, VConcretePale), Inlay = R(LargeSlabsDark, WalkingStone, VTilesDark),
        };

        public static readonly PavingPalette[] All = { Civic, Warm, Garden, Modern, Cobble, Transit };
        public static readonly string[] Ids = { "civic", "warm", "garden", "modern", "cobble", "transit" };

        public static PavingPalette Get(string id, PavingPalette fallback = null)
        {
            foreach (var p in All) if (p.Id == id) return p;
            return fallback ?? Civic;
        }

        /// <summary>The palette with its field replaced by the player's own surface choice (when set).</summary>
        public static PavingPalette WithField(PavingPalette p, string chosen)
        {
            if (string.IsNullOrEmpty(chosen)) return p;
            return new PavingPalette { Id = p.Id, Field = new AssetRef(chosen, p.Field.Id), Band = p.Band, Accent = p.Accent, Inlay = p.Inlay };
        }
    }

    /// <summary>
    /// A square's ground as a tiling: elements in priority order (first claims first), the field fills
    /// the rest. Emits only pieces CS2 keeps; slivers under ~1.5 m² are left to the field's neighbours.
    /// </summary>
    public sealed class PavingPlan
    {
        private struct Element
        {
            public List<float2[]> Rings;
            public SurfaceRole Role;
            public AssetRef Asset;
            public string Category;
        }

        private readonly List<Element> _elements = new List<Element>();

        public int Count => _elements.Count;

        public void Add(float2[] ring, SurfaceRole role, AssetRef asset, string category = null)
        {
            if (ring == null || ring.Length < 3) return;
            Add(new List<float2[]> { ring }, role, asset, category);
        }

        public void Add(List<float2[]> rings, SurfaceRole role, AssetRef asset, string category = null)
        {
            if (rings == null || rings.Count == 0) return;
            var ccw = new List<float2[]>(rings.Count);
            foreach (var r in rings) if (r != null && r.Length >= 3 && Poly.Area(r) > 0.5f) ccw.Add(Poly.EnsureCcw((float2[])r.Clone()));
            if (ccw.Count == 0) return;
            _elements.Add(new Element { Rings = ccw, Role = role, Asset = asset, Category = category });
        }

        /// <summary>Direction the field's openings are split along (the square's own axis): seams run with the paving.</summary>
        public float2 Axis = new float2(1f, 0f);

        /// <summary>Emits the tiling: every element clipped to the outline minus earlier claims, then the field. Returns surfaces emitted.</summary>
        public int Emit(LayerContext ctx, float2[] outline, SurfaceRole fieldRole, AssetRef field, string fieldCategory = null, bool emitField = true)
        {
            int n = 0;
            // Claims are kept as separate regions (never unioned): a difference against overlapping clip
            // rings is the same under the non-zero rule, and only claims whose bounds meet the element
            // take part — this keeps a square with 60 tree pits well inside the planning budget.
            var claimed = new List<(Region r, float2 min, float2 max)>();
            var outlineList = new[] { (IReadOnlyList<float2>)outline };
            foreach (var e in _elements)
            {
                ctx.Check();
                List<Region> inside;
                try { inside = Poly.Boolean(e.Rings, outlineList, BoolOp.Intersection); }
                catch (Exception) { continue; }
                if (inside.Count == 0) continue;
                Bounds(inside, out var mn, out var mx);
                var clip = new List<Region>();
                foreach (var c in claimed)
                    if (c.min.x <= mx.x && c.max.x >= mn.x && c.min.y <= mx.y && c.max.y >= mn.y) clip.Add(c.r);
                var part = clip.Count == 0 ? inside : Poly.Boolean(inside, clip, BoolOp.Difference);
                n += EmitRegions(ctx, part, e.Role, e.Asset, e.Category);
                foreach (var r in inside)
                {
                    Poly.Bounds(r.Outer, out var rmn, out var rmx);
                    claimed.Add((r, rmn, rmx));
                }
            }
            if (!emitField) return n;
            var all = new List<Region>(claimed.Count);
            foreach (var c in claimed) all.Add(c.r);
            var whole = new Region(Poly.EnsureCcw((float2[])outline.Clone()));
            var rest = all.Count == 0 ? new List<Region> { whole } : Poly.Boolean(new[] { whole }, all, BoolOp.Difference);
            n += EmitRegions(ctx, rest, fieldRole, field, fieldCategory);
            return n;
        }

        private static void Bounds(List<Region> regions, out float2 mn, out float2 mx)
        {
            mn = new float2(float.MaxValue); mx = new float2(float.MinValue);
            foreach (var r in regions)
            {
                Poly.Bounds(r.Outer, out var a, out var b);
                mn = math.min(mn, a); mx = math.max(mx, b);
            }
        }

        private int EmitRegions(LayerContext ctx, List<Region> regions, SurfaceRole role, AssetRef asset, string category)
        {
            int n = 0;
            foreach (var r in regions)
            {
                if (r == null || r.Outer == null || r.Area < 1.5f) continue;
                foreach (var ring in OpenHoles(r))
                {
                    if (ring == null || ring.Length < 3) continue;
                    float area = Poly.Area(ring);
                    if (area < 1.5f || Poly.MeanWidth(ring) < 0.35f) continue;
                    if (ctx.Surface(ring, role, asset, category)) n++;
                }
            }
            return n;
        }

        /// <summary>
        /// Hole-free rings for a region: one straight cut along <see cref="Axis"/> through each row of
        /// openings (a row of tree pits costs one cut, not one per pit), the generic seams for the rest.
        /// </summary>
        private List<float2[]> OpenHoles(Region r)
        {
            var result = new List<float2[]>();
            if (!r.HasHoles) { result.Add(Poly.EnsureCcw(r.Outer)); return result; }
            var axis = math.normalizesafe(Axis, new float2(1f, 0f));
            var side = new float2(-axis.y, axis.x);
            var rows = new List<float>();
            foreach (var h in r.Holes) rows.Add(math.dot(Poly.Centroid(h), side));
            rows.Sort();
            var cuts = new List<float>();
            foreach (var v in rows) if (cuts.Count == 0 || v - cuts[cuts.Count - 1] > 0.6f) cuts.Add(v);
            Poly.Bounds(r.Outer, out var mn, out var mx);
            float reach = math.length(mx - mn) + 10f;
            var c0 = (mn + mx) * 0.5f;
            var pieces = new List<Region> { r };
            foreach (var v in cuts)
            {
                var basePt = c0 + side * (v - math.dot(c0, side));
                var line = new[] { basePt - axis * reach, basePt + axis * reach };
                var next = new List<Region>();
                foreach (var p in pieces)
                {
                    if (!p.HasHoles) { next.Add(p); continue; }
                    List<Region> split;
                    try { split = Poly.Split(p, line); }
                    catch (Exception) { split = null; }
                    if (split == null || split.Count == 0) next.Add(p); else next.AddRange(split);
                }
                pieces = next;
            }
            foreach (var p in pieces)
            {
                if (p.HasHoles) result.AddRange(Poly.CutHoles(p));
                else result.Add(Poly.EnsureCcw(p.Outer));
            }
            return result;
        }
    }

    /// <summary>Shapes that squares are laid out with.</summary>
    public static class PavingShapes
    {
        /// <summary>The frame band: the outline minus its inset (opened into hole-free pieces by the plan).</summary>
        public static List<float2[]> Frame(float2[] outline, float width)
        {
            var result = new List<float2[]>();
            try
            {
                var inner = Poly.Offset(outline, width);
                var band = inner.Count == 0 ? new List<Region> { new Region(outline) } : Poly.Boolean(new[] { (IReadOnlyList<float2>)outline }, ToList(inner), BoolOp.Difference);
                foreach (var r in band) result.AddRange(Poly.CutHoles(r));
            }
            catch (Exception) { }
            return result;
        }

        /// <summary>The outline's inset ring(s) (the field inside a frame).</summary>
        public static List<float2[]> Inset(float2[] outline, float width)
        {
            try { return Poly.Offset(outline, width); }
            catch (Exception) { return new List<float2[]>(); }
        }

        /// <summary>A ring of width w around a disc of radius r (two half-annuli, no hole).</summary>
        public static List<float2[]> Annulus(float2 c, float r, float w, int sides = 48)
        {
            var result = new List<float2[]>();
            if (r <= 0.2f || w <= 0.2f) return result;
            int half = sides / 2;
            for (int k = 0; k < 2; k++)
            {
                var pts = new List<float2>(half * 2 + 2);
                float a0 = k * math.PI;
                for (int i = 0; i <= half; i++)
                {
                    float a = a0 + i * math.PI / half;
                    pts.Add(c + new float2(math.cos(a), math.sin(a)) * (r + w));
                }
                for (int i = half; i >= 0; i--)
                {
                    float a = a0 + i * math.PI / half;
                    pts.Add(c + new float2(math.cos(a), math.sin(a)) * r);
                }
                result.Add(pts.ToArray());
            }
            return result;
        }

        /// <summary>A straight band of width w from a to b (rectangle).</summary>
        public static float2[] Band(float2 a, float2 b, float w)
        {
            var d = b - a;
            float len = math.length(d);
            if (len < 0.5f) return null;
            return LayerRegion.Rect((a + b) * 0.5f, d / len, len * 0.5f, w * 0.5f);
        }

        /// <summary>Bands of width w every `pitch` metres in both directions of a frame, clipped later by the plan.</summary>
        public static List<float2[]> Grid(LayerRegion region, float2 centre, float2 axis, float pitch, float w)
        {
            var result = new List<float2[]>();
            var side = new float2(-axis.y, axis.x);
            float reach = math.length(region.Max - region.Min);
            int n = (int)math.ceil(reach / pitch);
            for (int i = -n; i <= n; i++)
            {
                float o = (i + 0.5f) * pitch;
                result.Add(LayerRegion.Rect(centre + side * o, axis, reach, w * 0.5f));
                result.Add(LayerRegion.Rect(centre + axis * o, side, reach, w * 0.5f));
            }
            return result;
        }

        /// <summary>A regular polygon (octagon by default) — a rondel that reads as laid stone, not a CAD circle.</summary>
        public static float2[] Polygon(float2 c, float r, int sides, float phase = 0f) => LayerRegion.Circle(c, r, sides, phase);

        private static List<IReadOnlyList<float2>> ToList(List<float2[]> rings)
        {
            var l = new List<IReadOnlyList<float2>>(rings.Count);
            foreach (var r in rings) l.Add(r);
            return l;
        }
    }
}
