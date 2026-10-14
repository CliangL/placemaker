// Beach: a shore laid out in bands from the water inland (art pass s13).
//
//   foreshore  the strip between the drawn outline and the real water: sand paint (and a gentle carve) reach
//              into the water, so the sand actually meets it instead of stopping at a grass/rock bank
//   waterline  wet sand: boats pulled up, rocks and tide pools on a rocky shore, lifeguard posts behind it
//   sand       umbrella + lounger sets in loose rows parallel to the water, aisles kept open in front of the
//              accesses, volleyball courts grouped at one end, fire rings on a wild beach
//   promenade  the back path parallel to the water; short access paths drop off it onto small wooden/paved
//              landings on the sand (never grey paths dead-ending mid-sand); kiosks, huts and showers at the
//              back near the path ends, benches facing the sea
//   blend      the sand texture (CS2's terrain material paint, not a surface quad) fades into the ground behind
//              over "Sand blend" metres with a noisy natural edge; beach grass thickens through the fade
//   backdrop   dunes (raised, sand thinning up the slope, marram grass, sand fences, pines behind), a lawn,
//              woods, or nothing
//
// The water side comes from WaterDepthAt (or the "Water side" choice, see Shoreline). The ground is carved
// into a gentle slope down to the waterline (only lowered, never raised) and dunes are raised behind the
// sand, all blended back to the original ground at the outline away from the water. Styles change the
// composition, not only densities: rocky (dirt texture with sand pockets, rock clusters along the waterline,
// tide-pool hollows), river (narrow, low bank, lawn and boats), cove (wild dunes, rocky headlands at both
// ends, fire rings, pines), volleyball (a row of courts).

using System.Collections.Generic;
using Placemaker.Core.Geometry;
using Placemaker.Core.Layers.Common;
using Placemaker.Core.Layers.Leisure;
using Placemaker.Core.Layers.Park;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Types.Beach
{
    [AreaGenerator(70, Group = "water")]
    public sealed class BeachLayer : LeisureLayer
    {
        public static readonly string[] Styles = { "sandy", "rocky", "river", "cove", "volleyball" };
        private static readonly List<Gate> NoGates = new List<Gate>();
        /// <summary>How far past the outline the sand may reach to find the water (m).</summary>
        public const float ForeshoreReach = 24f;
        /// <summary>Sand is painted this far into the water so its dithered edge sits under the surface.</summary>
        public const float IntoWater = 8f;   // > feather + 2 × edge noise: full strength right at the waterline
        private const float LandingDepth = 4f, LandingHalfWidth = 2.75f;

        protected override string Area => "Beach";
        public override string Id => "beach";
        public override string Icon => "Media/Game/Icons/CityBeach.svg";
        public override float MinArea => 1500f;

        protected override List<FeatureInfo> BuildFeatures() => new List<FeatureInfo>
        {
            // Area-wide features run on the dry land only ("core" = the largest free piece of shore.Land):
            // on the whole outline they planted picnic shelters and trees in the water (art pass a3).
            Feature(BeachBarAddOn.LayerId), Feature("restrooms"), Feature("shops", ("count", 2.0), ("where", "paths")), StreetShopsFeature(Area),
            CoreFeature("playground", ("size", "small")), CoreFeature("picnic"), CoreFeature("rocks", ("size", "medium")),
            CoreFeature("bigtrees"), CoreFeature("decals", ("theme", "dirt")),
        };

        /// <summary>Restrooms only: picnic shelters and dirt decals made the sand read as a lawn (art pass a3).</summary>
        protected override string[] DefaultFeatures => new[] { "restrooms" };

        protected override void DefineControls(ControlSet c)
        {
            c.Choice("style", "sandy", ControlTier.Essential, "beach", Styles).Tip();
            c.Choice("waterSide", "auto", ControlTier.Essential, "beach", Shoreline.Sides).Tip();
            c.Slider("beachDepth", 60, 12, 80, 2, "m", ControlTier.Essential, "beach").Layout(8).Tip();
            c.Slider("sandBlend", 14, 2, 40, 1, "m", ControlTier.Essential, "beach").Tip();
            c.Slider("umbrellas", 50, 0, 160, 5, "perHa", ControlTier.Essential, "beach").Tip();
            c.Choice("layout", "scattered", ControlTier.Essential, "beach", "scattered", "rows").Tip();

            c.Toggle("carve", true, ControlTier.More, "shore").Tip();
            c.Slider("slope", 5, 2, 15, 1, "%", ControlTier.More, "shore").When("carve=true").Tip();
            c.Slider("wetBand", 6, 2, 15, 1, "m", ControlTier.More, "shore").Tip();
            c.Toggle("sandReach", true, ControlTier.More, "shore").Tip();
            c.Toggle("sandSurface", false, ControlTier.More, "shore").Tip();
            c.Asset("sandAsset", AssetCategories.SurfaceSand, ControlTier.More, "shore").When("sandSurface=true");
            c.Toggle("paint", true, ControlTier.More, "shore").Tip();
            c.Slider("paintLayer", TerrainMaterials.Sand, 1, 4, 1, "count", ControlTier.More, "shore").When("paint=true").Tip();

            c.Choice("backdrop", "dunes", ControlTier.More, "backdrop", "dunes", "lawn", "woods", "none").Tip();
            c.Slider("duneHeight", 2, 0.5, 5, 0.5, "m", ControlTier.More, "backdrop").When("backdrop=dunes");
            c.Slider("duneDepth", 20, 8, 50, 2, "m", ControlTier.More, "backdrop").Layout(6).Tip();
            c.Toggle("duneFence", true, ControlTier.More, "backdrop").When("backdrop=dunes").Tip();
            c.Slider("grassDensity", 60, 0, 100, 5, "%", ControlTier.More, "backdrop").Tip();
            c.Slider("treeDensity", 15, 0, 100, 5, "perHa", ControlTier.More, "backdrop");

            c.Toggle("backPath", true, ControlTier.More, "backPath").Tip();
            c.Choice("pathWidth", "narrow", ControlTier.More, "backPath", "narrow", "wide");
            c.Slider("accessSpacing", 60, 20, 200, 10, "m", ControlTier.More, "backPath").Layout(10).Tip();
            c.Toggle("landings", true, ControlTier.More, "backPath").Tip();
            c.Slider("benchSpacing", 25, 0, 80, 5, "m", ControlTier.More, "backPath").Layout(5).Tip();
            c.Toggle("lamps", true, ControlTier.More, "backPath");

            c.Toggle("showers", true, ControlTier.More, "amenities").Tip();
            c.Choice("huts", "few", ControlTier.More, "amenities", "none", "few", "row").Tip();
            c.Slider("lifeguards", 1, 0, 8, 1, "count", ControlTier.More, "amenities").Tip();
            c.Slider("kiosks", 1, 0, 6, 1, "count", ControlTier.More, "amenities");
            c.Slider("courts", 0, 0, 8, 1, "count", ControlTier.More, "amenities").Tip();
            c.Toggle("boats", false, ControlTier.More, "amenities").Tip();
            c.Toggle("fires", false, ControlTier.More, "amenities").Tip();

            c.Slider("rockClusters", 0, 0, 20, 1, "count", ControlTier.More, "rocks").Tip();
            c.Slider("tidePools", 0, 0, 12, 1, "count", ControlTier.More, "rocks").Tip();

            c.Asset("umbrellaAsset", AssetCategories.BeachUmbrella, ControlTier.More, "assets");
            c.Asset("loungerAsset", AssetCategories.BeachLounger, ControlTier.More, "assets");
            c.Asset("lifeguardAsset", AssetCategories.BeachLifeguard, ControlTier.More, "assets");
            c.Asset("hutAsset", AssetCategories.BeachHut, ControlTier.More, "assets");
            c.Asset("pathAsset", AssetCategories.Pathway, ControlTier.More, "assets");
            c.Asset("landingAsset", AssetCategories.SurfacePaving, ControlTier.More, "assets").When("landings=true");
            c.AssetMulti("treeSpecies", AssetCategories.Tree, ControlTier.More, "assets");
            c.AssetMulti("grassSpecies", AssetCategories.Bush, ControlTier.More, "assets");
            EntranceMarkers(c);
            DefineStreetShopControls(c);

            c.Fixed("waterSide");
            c.Fixed("carve");
            c.Fixed("paintLayer");
            c.Fixed("sandReach");
            c.RandomRange("beachDepth", 36, 70);
            c.RandomRange("sandBlend", 8, 24);
            c.RandomRange("umbrellas", 10, 60);
            c.RandomRange("courts", 0, 2);
            c.RandomRange("lifeguards", 0, 2);
            c.RandomRange("rockClusters", 0, 4);
            c.RandomRange("tidePools", 0, 2);
            c.Chance("boats", 0.3f);
            c.Chance("fires", 0.3f);
        }

        protected override void DefinePresets()
        {
            // s13: the sand is terrain texture blending into the ground behind. Preset ids are save format; only labels changed.
            // Family beach: deep sand, loose rows of umbrellas, lifeguard posts, dunes with grass and pines behind.
            Preset("sandy", ("style", "sandy"), ("beachDepth", 60.0), ("sandBlend", 16.0), ("umbrellas", 80.0), ("layout", "scattered"), ("backdrop", "dunes"),
                ("huts", "none"), ("lifeguards", 2.0), ("duneDepth", 24.0), ("grassDensity", 70.0), ("treeDensity", 40.0), ("kiosks", 1.0));
            // Promenade beach: tidy rows, a wide promenade with benches and kiosks, palms on a lawn, a short crisp blend.
            Preset("resort", ("style", "sandy"), ("beachDepth", 56.0), ("sandBlend", 6.0), ("umbrellas", 115.0), ("layout", "rows"), ("backdrop", "lawn"), ("huts", "none"),
                ("pathWidth", "wide"), ("benchSpacing", 15.0), ("kiosks", 2.0), ("lifeguards", 2.0), ("slope", 3.0), ("treeDensity", 30.0), ("accessSpacing", 45.0));
            // Rocky cove: dirt texture with sand pockets, rocks along the waterline, tide pools, a wide rough blend.
            Preset("rocky", ("style", "rocky"), ("umbrellas", 5.0), ("sandSurface", false), ("paintLayer", (double)TerrainMaterials.Dirt), ("backdrop", "none"),
                ("huts", "none"), ("lifeguards", 0.0), ("kiosks", 0.0), ("rockClusters", 8.0), ("tidePools", 5.0), ("beachDepth", 36.0), ("sandBlend", 20.0),
                ("slope", 8.0), ("treeDensity", 30.0), ("benchSpacing", 30.0));
            Preset("river", ("style", "river"), ("beachDepth", 30.0), ("sandBlend", 8.0), ("wetBand", 3.0), ("slope", 3.0), ("umbrellas", 10.0), ("backdrop", "lawn"),
                ("treeDensity", 35.0), ("boats", true), ("lifeguards", 0.0), ("huts", "none"), ("accessSpacing", 40.0));
            // Wild dune beach: no amenities, sand drifting far up the dunes, headlands, fire rings, pines.
            Preset("cove", ("style", "cove"), ("beachDepth", 44.0), ("sandBlend", 28.0), ("umbrellas", 6.0), ("backdrop", "dunes"), ("duneDepth", 26.0),
                ("duneHeight", 3.0), ("treeDensity", 75.0), ("grassDensity", 80.0), ("fires", true), ("lifeguards", 0.0), ("huts", "none"), ("kiosks", 0.0),
                ("lamps", false), ("rockClusters", 3.0), ("boats", true), ("benchSpacing", 0.0), ("accessSpacing", 90.0), ("showers", false));
            Preset("volleyball", ("style", "volleyball"), ("beachDepth", 70.0), ("sandBlend", 10.0), ("courts", 4.0), ("umbrellas", 25.0), ("accessSpacing", 120.0),
                ("lifeguards", 1.0), ("kiosks", 2.0), ("huts", "none"), ("pathWidth", "wide"), ("backdrop", "lawn"), ("treeDensity", 30.0));
        }

        protected override int WantedEntrances(LayerRegion region) => math.min(4, Entrances.WantedByArea(region.Area));

        // ───────────── planning ─────────────

        private sealed class Work
        {
            public LayerContext Ctx;
            public Shoreline Shore;
            public Occupancy Occ;
            public string Style;
            public float Wet, D, Hw, Blend;
            public bool HasPath;
            public List<float2[]> Foreshore = new List<float2[]>();
            public List<float2[]> Lines = new List<float2[]>();
            /// <summary>Access paths: top (on the promenade) and bottom (the path end, at the landing).</summary>
            public List<(float2 top, float2 bottom)> Access = new List<(float2, float2)>();
            public List<float2> Landings = new List<float2>();
            public TerrainResult Terrain;
            public AssetRef Umbrella, Lounger;
            public int Umbrellas, Loungers, Showers, Huts, Guards, Courts, Rocks, Pools, Boats, Fires, Kiosks;
        }

        protected override void PlanCore(LayerContext ctx)
        {
            var shore = Shoreline.Analyze(ctx, ctx.LayerRegion, ctx.S("waterSide"));
            if (shore == null) { ctx.Issue(IssueSeverity.Warning, Keys.NoSpace, NameKey); return; }
            ctx.LayerRegion = shore.Land;
            var b = new Work { Ctx = ctx, Shore = shore, Occ = new Occupancy(shore.Land, ctx.Input.Reserved, ctx.Site), Style = ctx.S("style") };
            b.Wet = math.min(ctx.F("wetBand"), math.max(2f, shore.MaxDepth * 0.2f));
            b.D = math.min(ctx.F("beachDepth"), math.max(b.Wet + 6f, shore.MaxDepth - 6f));
            b.Blend = math.max(1f, ctx.F("sandBlend"));
            var pathRole = ctx.S("pathWidth") == "wide" ? NetRole.PathwayWide : NetRole.Pathway;
            b.Hw = Composer.DefaultHalfWidth(pathRole);
            b.HasPath = ctx.B("backPath") && shore.MaxDepth > b.D + b.Hw + 2f;
            if (ctx.B("sandReach")) b.Foreshore = shore.Foreshore(ForeshoreReach, IntoWater);
            ctx.Plan.Facts["beachDepth"] = b.D;
            ctx.Plan.Facts["sandBlend"] = b.Blend;
            ctx.Plan.Facts["foreshore"] = b.Foreshore.Count;
            PickSetAssets(b);

            Terrain(b);
            ctx.Check();
            Ground(b);
            Paths(b, pathRole);
            ctx.Check();
            Courts(b);
            Rocks(b);
            TidePools(b);
            SandPockets(b);
            Lifeguards(b);
            Showers(b);
            Boats(b);
            ctx.Check();
            UmbrellaSets(b);
            FirePits(b);
            BackPathFurniture(b);
            HutsAndKiosks(b);
            ctx.Check();
            BlendGrass(b);
            Backdrop(b);

            EmitGreenRegions(ctx, b.Occ);
            CommonStats(ctx);
            ctx.Stat("Placemaker.Stat.Shoreline", System.Math.Round(shore.Length), Keys.UnitM);
            ctx.Stat("Placemaker.Stat.Umbrellas", b.Umbrellas, Keys.UnitCount);
            ctx.Stat("Placemaker.Stat.Loungers", b.Loungers, Keys.UnitCount);
            ctx.Stat("Placemaker.Stat.Showers", b.Showers, Keys.UnitCount);
            ctx.Stat("Placemaker.Stat.Huts", b.Huts, Keys.UnitCount);
            ctx.Stat("Placemaker.Stat.Lifeguards", b.Guards, Keys.UnitCount);
            if (b.Courts > 0) ctx.Stat("Placemaker.Stat.Courts", b.Courts, Keys.UnitCount);
            if (b.Rocks > 0) ctx.Stat("Placemaker.Stat.Rocks", b.Rocks, Keys.UnitCount);
            ctx.Plan.Facts["tidePools"] = b.Pools;
            ctx.Plan.Facts["boats"] = b.Boats;
            ctx.Plan.Facts["firePits"] = b.Fires;
            ctx.Plan.Facts["kiosks"] = b.Kiosks;
            ctx.Plan.Facts["accessPaths"] = b.Access.Count;
            ctx.Plan.Facts["landings"] = b.Landings.Count;
            ctx.Plan.Facts["lifeguards"] = b.Guards;
        }

        /// <summary>One umbrella model and one lounger model per build (a beach rents out one kind), unless the player chose.</summary>
        private static void PickSetAssets(Work b)
        {
            var ctx = b.Ctx;
            uint v = Hashing.Mix(ctx.Seed, 0x5E75u);
            b.Umbrella = AssetMix.Pick(BeachArt.Or(ctx.S("umbrellaAsset"), BeachArt.Umbrellas), v);
            b.Lounger = AssetMix.Pick(BeachArt.Or(ctx.S("loungerAsset"), BeachArt.Loungers), Hashing.Mix(v, 1u));
        }

        // ───────────── terrain, ground ─────────────

        private static void Terrain(Work b)
        {
            var ctx = b.Ctx; var shore = b.Shore;
            bool carve = ctx.B("carve");
            bool dunes = ctx.S("backdrop") == "dunes" && ctx.F("duneHeight") > 0f;
            if (!carve && !dunes) return;
            float g = ctx.F("slope") / 100f;
            float D = b.D, bank = math.max(8f, D * 0.4f);
            float duneStart = D + (b.HasPath ? b.Hw + 2.5f : 2f), duneEnd = duneStart + ctx.F("duneDepth");
            float duneH = ctx.F("duneHeight");
            uint noise = Hashing.Mix(ctx.Seed, 0xD00Eu);
            float water = shore.WaterLevel;
            b.Terrain = ShoreTerrain.Shape(ctx, shore, (p, h0, d) =>
            {
                float t = h0;
                if (carve)
                {
                    var q = shore.Nearest(p, out _, out _);
                    float hs = ctx.H(q);
                    if (math.isfinite(water)) hs = math.min(hs, water + 0.3f);
                    float plane = hs + g * d;
                    // Foreshore (d < 0): keep sloping into the water, never more than 0.6 m below it, never raised.
                    if (d < 0f) t = math.isfinite(water) ? math.min(h0, math.max(plane, water - 0.6f)) : h0;
                    else if (d <= D) t = math.min(h0, plane);
                    else if (d <= D + bank) t = math.min(h0, math.lerp(plane, h0, ShoreTerrain.Smooth(0f, 1f, (d - D) / bank)));
                }
                if (dunes && d > duneStart && d < duneEnd)
                {
                    float u = (d - duneStart) / (duneEnd - duneStart);
                    float ridge = math.pow(math.sin(math.PI * u), 1.3f);
                    float hummock = ValueNoise.Clumps(p, 16f, noise);
                    t += duneH * ridge * (0.45f + 0.8f * hummock);
                }
                return t;
            }, 14f, 28f, 6f, carve ? b.Foreshore : null);
        }

        /// <summary>
        /// The sand: one texture field from the water (foreshore included) to the back of the beach at full strength,
        /// fading over "Sand blend" metres into the ground behind with a noisy edge. Dunes get a lighter sand that
        /// thins out up the slope. The old hard-edged sand surface is opt-in only.
        /// </summary>
        private static void Ground(Work b)
        {
            var ctx = b.Ctx; var shore = b.Shore;
            float top = b.HasPath ? b.D : b.D + 2f;
            int layer = math.clamp(ctx.I("paintLayer"), 1, 4);
            bool rocky = b.Style == "rocky";
            if (ctx.B("paint"))
            {
                var rings = new List<IReadOnlyList<float2>>();
                foreach (var r in shore.Band(-3f, top + b.Blend + 3f)) rings.Add(r);
                foreach (var r in b.Foreshore) rings.Add(r);
                // Rocky: the dirt/pebble texture is full only near the water, then frays into the ground.
                float full = rocky ? math.min(top, b.Wet + 6f) : top;
                float fade = rocky ? math.max(b.Blend, top - full + b.Blend * 0.5f) : b.Blend;
                foreach (var piece in Merge(rings))
                    TerrainPaint.Field(ctx, piece, layer, 1f, 3f, math.clamp(b.Blend * 0.12f, 0.8f, 3f), shore.Chain, full, fade, 0f, 0x5A4Du);
                // Dunes are sand too: lighter at the foot, thinning to the vegetated crest.
                if (ctx.S("backdrop") == "dunes" && !rocky)
                {
                    float d0 = top + b.Hw + 0.5f, depth = ctx.F("duneDepth");
                    foreach (var ring in shore.Band(d0, d0 + depth + 2f))
                        TerrainPaint.Field(ctx, ring, layer, 0.65f, 5f, 2.5f, shore.Chain, d0 + depth * 0.2f, depth * 0.8f, 0.1f, 0xD0E5u);
                }
            }
            if (ctx.B("sandSurface") && AssetAvailability.Require(ctx.Plan, AssetCategories.SurfaceSand, new[] { ctx.S("sandAsset") }, Keys.Category(AssetCategories.SurfaceSand)))
                foreach (var ring in shore.Band(0f, top))
                    ctx.Surface(ring, SurfaceRole.Ground, AssetMix.Pick(BeachArt.Or(ctx.S("sandAsset"), BeachArt.Sand), 0u), AssetCategories.SurfaceSand);
            if (ctx.S("backdrop") == "lawn")
            {
                // The lawn starts where the sand has mostly faded (not at the path), so the blend stays visible.
                float from = top + b.Hw + 0.8f + (ctx.B("paint") ? b.Blend * 0.6f : 0f);
                foreach (var ring in shore.Band(from, shore.MaxDepth + 20f))
                    ctx.Surface(ring, SurfaceRole.Grass, AssetMix.One(""));
            }
        }

        private static List<float2[]> Merge(List<IReadOnlyList<float2>> rings)
        {
            var result = new List<float2[]>();
            if (rings.Count == 0) return result;
            try
            {
                foreach (var r in Poly.UnionAll(rings))
                    foreach (var piece in Poly.CutHoles(r, 0.5f))
                    {
                        var c = Poly.Clean(piece, 0.3f);
                        if (c.Length >= 3 && Poly.Area(c) > 8f) result.Add(c);
                    }
            }
            catch (System.Exception)
            {
                result.Clear();
                foreach (var r in rings) result.Add(Poly.Clean(r, 0.3f));
            }
            return result;
        }

        /// <summary>Rocky style: pockets of sand between the rocks near the water (sand texture over the dirt).</summary>
        private static void SandPockets(Work b)
        {
            var ctx = b.Ctx; var shore = b.Shore;
            if (b.Style != "rocky" || !ctx.B("paint")) return;
            int n = math.clamp((int)(shore.Length / 35f), 2, 10);
            var stations = shore.Stations(n, 8f, 0.35f);
            for (int i = 0; i < stations.Count; i++)
            {
                uint v = Hashing.Mix(ctx.Seed, 0x5A0Cu + (uint)i);
                var p = shore.At(stations[i], out var inw, out var tan);
                float r = 4f + Hashing.Unit(v) * 4f;
                var c = p + inw * (b.Wet * 0.5f + Hashing.Unit(Hashing.Mix(v, 1u)) * 4f) + tan * (Hashing.Unit(Hashing.Mix(v, 2u)) - 0.5f) * 10f;
                TerrainPaint.Field(ctx, LeisureKit.Blob(c, r, v, 14, 0.3f), TerrainMaterials.Sand, 0.95f, r * 0.6f, 1f, null, 0f, 0f, 0f, v);
            }
        }

        // ───────────── circulation ─────────────

        private void Paths(Work b, NetRole role)
        {
            var ctx = b.Ctx; var shore = b.Shore; var occ = b.Occ;
            var asset = AssetMix.One(ctx.S("pathAsset"));
            var spine = new List<(float2, float2)>();
            if (b.HasPath)
                foreach (var line in shore.Offset(b.D, b.Hw + 1f, 24f))
                {
                    var curves = Shoreline.Curves(shore.Land, line, role);
                    if (curves.Count == 0) continue;
                    ParkPaths.Index(curves, occ.Corridors, 100);
                    ParkPaths.Index(curves, occ.Paths, 100);
                    ParkPaths.Emit(ctx, curves, NoGates, asset);
                    foreach (var c in curves)
                        for (int i = 1; i < c.Samples.Length; i++) spine.Add((c.Samples[i - 1], c.Samples[i]));
                    b.Lines.Add(line);
                }
            b.HasPath = b.Lines.Count > 0;

            // Entrances from the land side (never across the sand).
            var all = Gates(ctx);
            var gates = new List<Gate>();
            foreach (var g in all) if (shore.Depth(g.Position) > b.D * 0.6f) gates.Add(g);
            if (gates.Count == 0) gates = all;
            if (spine.Count > 0) ConnectGates(ctx, occ, gates, spine, NetRole.Pathway, asset);

            // Short accesses off the promenade, each ending on a landing on the sand.
            float spacing = ctx.F("accessSpacing");
            if (b.HasPath)
                foreach (var line in b.Lines)
                {
                    float len = Shoreline.LengthOf(line);
                    int n = math.max(1, (int)math.floor(len / spacing));
                    for (int k = 0; k < n; k++)
                    {
                        var top = Shoreline.PointOn(line, (k + 0.5f) * len / n, out _);
                        TryAccess(b, top, asset);
                    }
                }
            else
                foreach (var g in gates)
                {
                    var from = g.Position;
                    if (shore.Depth(from) < b.Wet + 8f) continue;
                    TryAccess(b, from, asset);
                }
        }

        /// <summary>
        /// One access: a short path from the promenade towards the water that ends on a small landing (deck or
        /// paving) on the sand — people walk on from there. An aisle from the landing to the water stays free.
        /// </summary>
        private static void TryAccess(Work b, float2 top, AssetRef asset)
        {
            var ctx = b.Ctx; var shore = b.Shore; var occ = b.Occ;
            shore.Nearest(top, out var inw, out _);
            var tan = new float2(inw.y, -inw.x);
            float sand = shore.Depth(top) - b.Wet;
            if (sand < 14f) return;   // a narrow beach is stepped onto straight from the promenade
            float run = math.clamp(sand * 0.28f, 4f, 12f);
            var bottom = top - inw * run;
            var landing = bottom - inw * (LandingDepth * 0.5f);
            if (!shore.Land.ContainsDisc(landing, LandingHalfWidth + 0.5f) || shore.Land.CrossesBoundary(top, bottom, true)) return;
            float len = math.distance(top, bottom);
            for (float s = b.Hw + 2f; s < len; s += 1.5f)
            {
                var q = math.lerp(top, bottom, s / len);
                if (occ.Corridors.Overlaps(q, 1.2f, 0.3f) || occ.Discs.Overlaps(q, 1.2f, 0.2f) || occ.HitsPolygon(q, 1.2f)) return;
            }
            bool landings = ctx.B("landings");
            if (landings && !occ.Free(landing, LandingHalfWidth, 0.3f, 0.2f)) return;
            foreach (var (t0, _) in b.Access) if (math.distance(t0, top) < 25f) return;
            LeisureKit.Path(ctx, occ, top, bottom, NetRole.Pathway, asset);
            b.Access.Add((top, bottom));
            if (landings)
            {
                var rect = new OrientedRect { Center = landing, Axis = tan, HalfLength = LandingHalfWidth, HalfWidth = LandingDepth * 0.5f };
                if (ctx.Surface(rect.Ring, SurfaceRole.Paving, AssetMix.Pick(BeachArt.Or(ctx.S("landingAsset"), BeachArt.Landing), 0u), AssetCategories.SurfacePaving))
                {
                    // Taken as a corridor across the landing (capsule ends seaward/landward), so its sides stay exact for the showers.
                    occ.TakeCorridor(landing + inw * (LandingDepth * 0.5f), landing - inw * (LandingDepth * 0.5f), LandingHalfWidth, 2);
                    b.Landings.Add(landing);
                }
            }
            // Aisle to the water: no umbrella sets in front of the access.
            var shoreP = shore.Nearest(landing, out _, out _);
            occ.TakeCorridor(landing - inw * (LandingDepth * 0.5f), shoreP + inw * 1f, 2.2f, 5);
        }

        // ───────────── on the sand ─────────────

        private static float Yaw(float2 dir) => math.atan2(dir.x, dir.y);

        private static void Courts(Work b)
        {
            var ctx = b.Ctx; var shore = b.Shore;
            int wanted = ctx.I("courts");
            if (wanted == 0 && b.Style == "volleyball") wanted = math.clamp((int)(shore.Length / 40f), 2, 6);
            if (wanted <= 0) return;
            string cat = LeisureKit.Category(ctx, "", AssetCategories.BeachVolleyball, AssetCategories.Sports);
            if (cat == null) return;
            float dc = math.clamp((b.Wet + b.D) * 0.5f, b.Wet + 6f, b.D - 6f);
            if (dc < b.Wet + 5f) { ctx.Issue(IssueSeverity.Info, "Placemaker.Beach.Issue.NoCourtSpace"); return; }
            // Grouped from one end of the beach (a volleyball area), not sprinkled along it.
            int tries = wanted * 8;
            var stations = shore.Stations(tries, 12f);
            for (int i = 0; i < stations.Count && b.Courts < wanted; i++)
            {
                var c0 = shore.At(stations[i], out var inw, out var tan);
                var rect = new OrientedRect { Center = c0 + inw * dc, Axis = tan, HalfLength = 9f, HalfWidth = 5f };
                if (!LeisureKit.RectFits(shore.Land, b.Occ, rect, 0.5f)) continue;
                LeisureKit.TakeRect(b.Occ, rect);
                uint v = Hashing.Mix(ctx.Seed, 0xC0u + (uint)i);
                // No vanilla volleyball net: the tennis net at 0.7 scale (9.2 m) spans the 10 m court, facing
                // along it (yaw = court axis puts the net's long local x across the court). Art pass a3.
                var net = ctx.Object(rect.Center, rect.Yaw, ObjectRole.Furniture, new AssetRef("", "TennisNet01"), v, -1, cat);
                net.Scale = 0.7f;
                b.Courts++;
            }
            if (b.Courts < wanted) ctx.Issue(IssueSeverity.Info, "Placemaker.Beach.Issue.FewerCourts", b.Courts, wanted);
        }

        private static void Rocks(Work b)
        {
            var ctx = b.Ctx; var shore = b.Shore;
            int wanted = ctx.I("rockClusters");
            if (wanted == 0 && b.Style == "rocky") wanted = math.clamp((int)(shore.Length / 30f), 4, 12);
            bool headlands = b.Style == "cove" && !shore.Closed;
            if (wanted <= 0 && !headlands) return;
            if (!AssetAvailability.Require(ctx.Plan, AssetCategories.Rock, null, Keys.Category(AssetCategories.Rock))) return;
            var spots = new List<(float2 c, float r, int n)>();
            if (headlands)
                foreach (float s in new[] { math.min(8f, shore.Length * 0.1f), shore.Length - math.min(8f, shore.Length * 0.1f) })
                {
                    var p = shore.At(s, out var inw, out _);
                    spots.Add((p + inw * math.min(6f, b.D * 0.3f), 7.5f, 16));
                }
            var stations = shore.Stations(wanted, 6f, 0.3f);
            for (int i = 0; i < stations.Count; i++)
            {
                uint v = Hashing.Mix(ctx.Seed, 0x40C0u + (uint)i);
                var p = shore.At(stations[i], out var inw, out var tan);
                bool rocky = b.Style == "rocky";
                // Rocky shores: clusters straddle the waterline; elsewhere a few on the upper sand.
                float d = rocky ? 1.5f + Hashing.Unit(v) * (b.Wet + 3f) : b.Wet + Hashing.Unit(v) * math.max(2f, b.D - b.Wet - 4f);
                float r = rocky ? 2.5f + Hashing.Unit(Hashing.Mix(v, 1u)) * 3f : 2f + Hashing.Unit(Hashing.Mix(v, 1u)) * 2f;
                spots.Add((p + inw * d + tan * (Hashing.Unit(Hashing.Mix(v, 2u)) - 0.5f) * 8f, r, rocky ? 9 : 5));
            }
            const float golden = 2.39996323f;
            for (int k = 0; k < spots.Count; k++)
            {
                var (c, R, n) = spots[k];
                uint cs = Hashing.Mix(ctx.Seed, 0x40D0u + (uint)k * 31u);
                int placed = 0;
                for (int i = 0; i < n * 2 && placed < n; i++)
                {
                    float rr = R * math.sqrt((i + 0.5f) / (n * 1.4f));
                    if (rr > R) break;
                    var p = c + new float2(math.cos(i * golden + cs % 7), math.sin(i * golden + cs % 7)) * rr;
                    uint v = Hashing.Mix(cs, (uint)i);
                    float scale = 0.5f + 1.1f * Hashing.Unit(v) * (1f - rr / (R + 0.01f) * 0.6f);
                    float fr = 1.1f * scale;
                    if (!b.Occ.Free(p, fr, 0.5f, 0.1f)) continue;
                    var o = ctx.Object(p, Hashing.Unit(Hashing.Mix(v, 5u)) * 6.283f, ObjectRole.Furniture, AssetMix.One(""), v, -1, AssetCategories.Rock);
                    o.Scale = scale;
                    b.Occ.Take(p, fr, 2);
                    placed++;
                }
                b.Rocks += placed;
            }
        }

        private static void TidePools(Work b)
        {
            var ctx = b.Ctx; var shore = b.Shore;
            int wanted = ctx.I("tidePools");
            if (wanted == 0 && b.Style == "rocky") wanted = math.clamp((int)(shore.Length / 40f), 2, 6);
            if (wanted <= 0) return;
            bool rocks = AssetAvailability.Has(AssetCategories.Rock);
            var stations = shore.Stations(wanted * 2, 10f, 0.7f);
            for (int i = 0; i < stations.Count && b.Pools < wanted; i++)
            {
                uint v = Hashing.Mix(ctx.Seed, 0x7D00u + (uint)i);
                var p = shore.At(stations[i], out var inw, out _);
                float r = 2.2f + Hashing.Unit(v) * 2f;
                var c = p + inw * (b.Wet * 0.5f + r + 1f);
                if (shore.EdgeDistance(c) < r + 8f || !b.Occ.Free(c, r + 0.6f, 1f, 0.2f)) continue;
                float h = b.Terrain != null ? b.Terrain.HeightAfter(c) : ctx.H(c);
                ShoreTerrain.Hollow(ctx, LeisureKit.Blob(c, r, v, 12, 0.25f), LeisureKit.Blob(c, r * 0.55f, Hashing.Mix(v, 9u), 10, 0.2f), h - 0.35f, h - 0.75f);
                b.Occ.Take(c, r, 2);
                b.Pools++;
                if (!rocks) continue;
                int n = 3 + (int)(Hashing.Unit(Hashing.Mix(v, 3u)) * 3f);
                for (int k = 0; k < n; k++)
                {
                    float a = Hashing.Unit(Hashing.Mix(v, (uint)k + 20u)) * 6.283f;
                    var q = c + new float2(math.cos(a), math.sin(a)) * (r + 1.1f);
                    if (!b.Occ.Free(q, 0.7f, 0.5f, 0.05f)) continue;
                    var o = ctx.Object(q, a, ObjectRole.Furniture, AssetMix.One(""), Hashing.Mix(v, (uint)k + 40u), -1, AssetCategories.Rock);
                    o.Scale = 0.5f + 0.5f * Hashing.Unit(Hashing.Mix(v, (uint)k + 60u));
                    b.Occ.Take(q, 0.7f, 2);
                    b.Rocks++;
                }
            }
        }

        /// <summary>
        /// Lifeguard posts behind the wet sand, evenly spaced and facing the water. Vanilla CS2 has no lifeguard
        /// tower, so unless the player picks one, a post is a tall umpire chair (reads as a lifeguard chair) with a
        /// lifebuoy station and a flag pole beside it.
        /// </summary>
        private static void Lifeguards(Work b)
        {
            var ctx = b.Ctx; var shore = b.Shore;
            int wanted = ctx.I("lifeguards");
            if (wanted <= 0) return;
            string chosen = ctx.S("lifeguardAsset");
            string cat;
            AssetRef chair;
            if (!string.IsNullOrEmpty(chosen)) { cat = AssetCategories.BeachLifeguard; chair = AssetMix.One(chosen); }
            else
            {
                cat = LeisureKit.Category(ctx, "", AssetCategories.SportsTennis, AssetCategories.BeachLifeguard);
                if (cat == null) return;
                chair = cat == AssetCategories.SportsTennis ? new AssetRef(BeachArt.LifeguardChair) : AssetMix.One("");
            }
            bool nautical = AssetAvailability.Has(AssetCategories.WaterfrontNautical);
            foreach (float s in shore.Stations(wanted, 15f))
            {
                for (int k = 0; k < 7; k++)
                {
                    float off = (k == 0 ? 0 : (k % 2 == 1 ? 1 : -1) * ((k + 1) / 2) * 4f);
                    var p = shore.At(s + off, out var inw, out var tan);
                    var q = p + inw * (b.Wet + 3.5f);
                    uint v = Hashing.Mix(ctx.Seed, 0x6A00u + (uint)b.Guards);
                    if (!LeisureKit.Put(ctx, b.Occ, q, Yaw(-inw), ObjectRole.Furniture, chair, cat, 1.2f, v, 0.8f, 0.4f)) continue;
                    b.Guards++;
                    if (nautical) LeisureKit.Put(ctx, b.Occ, q + tan * 2.1f + inw * 0.6f, Yaw(-inw), ObjectRole.Furniture, new AssetRef(BeachArt.Lifebuoy), AssetCategories.WaterfrontNautical, 0.5f, Hashing.Mix(v, 1u), 0.4f, 0f);
                    LeisureKit.Put(ctx, b.Occ, q - tan * 2.1f + inw * 0.8f, 0f, ObjectRole.Furniture, new AssetRef(BeachArt.Flag), AssetCategories.Decoration, 0.3f, Hashing.Mix(v, 2u), 0.4f, 0f);
                    // Keep a little apron around the post (no umbrellas crowding the chair).
                    b.Occ.Take(q, 3f, 6);
                    break;
                }
            }
        }

        /// <summary>Showers beside the landings (the end of each access), where people come off the sand.</summary>
        private static void Showers(Work b)
        {
            var ctx = b.Ctx;
            if (!ctx.B("showers") || b.Access.Count == 0) return;
            string cat = LeisureKit.Category(ctx, "", AssetCategories.BeachShower);
            if (cat == null) return;
            foreach (var (top, bottom) in b.Access)
            {
                var dir = math.normalizesafe(bottom - top);
                var side = new float2(-dir.y, dir.x);
                var at = bottom + dir * (LandingDepth * 0.5f);
                foreach (float sgn in new[] { 1f, -1f })
                {
                    var p = at + side * sgn * (LandingHalfWidth + 1.1f);
                    if (LeisureKit.Put(ctx, b.Occ, p, Yaw(-side * sgn), ObjectRole.Furniture, AssetMix.One(""), cat, 0.6f, Hashing.Mix(ctx.Seed, 0x5400u + (uint)b.Showers), 0.5f, 0.2f))
                    { b.Showers++; break; }
                }
            }
        }

        private static void Boats(Work b)
        {
            var ctx = b.Ctx; var shore = b.Shore;
            if (!ctx.B("boats")) return;
            string cat = LeisureKit.Category(ctx, "", AssetCategories.BeachBoat);
            if (cat == null) return;
            int groups = math.clamp((int)(shore.Length / 50f), 1, 6);
            foreach (float s in shore.Stations(groups, 8f, 0.25f))
            {
                uint v = Hashing.Mix(ctx.Seed, 0xB0A7u + (uint)b.Boats);
                int n = 2 + (int)(Hashing.Unit(v) * 3f);
                for (int k = 0; k < n; k++)
                {
                    var p = shore.At(s + (k - (n - 1) * 0.5f) * 2.6f, out var inw, out _);
                    var q = p + inw * math.clamp(b.Wet * 0.6f, 2.5f, b.Wet + 1f);
                    float yaw = Yaw(-inw) + (Hashing.Unit(Hashing.Mix(v, (uint)k)) - 0.5f) * 0.4f;
                    if (LeisureKit.Put(ctx, b.Occ, q, yaw, ObjectRole.Furniture, AssetMix.One(""), cat, 1.2f, Hashing.Mix(v, (uint)k + 7u), 0.5f, 0.1f)) b.Boats++;
                }
            }
        }

        /// <summary>
        /// Umbrella sets in rows parallel to the water: tidy (resort) or loose (public beach: jittered, with gaps where
        /// the crowd thins out). A set is one umbrella with two loungers beside its canopy, all facing the water; one
        /// umbrella and one lounger model per build.
        /// </summary>
        private static void UmbrellaSets(Work b)
        {
            var ctx = b.Ctx; var shore = b.Shore; var occ = b.Occ;
            float density = ctx.F("umbrellas");
            if (density <= 0f) return;
            string umbrella = LeisureKit.Category(ctx, ctx.S("umbrellaAsset"), AssetCategories.BeachUmbrella, AssetCategories.CafeSeating);
            if (umbrella == null) return;
            string lounger = LeisureKit.Category(ctx, ctx.S("loungerAsset"), AssetCategories.BeachLounger, AssetCategories.Bench);
            float d0 = b.Wet + 3f, d1 = b.D - (b.HasPath ? b.Hw + 3f : 1.5f);
            if (d1 <= d0) return;
            int wanted = (int)math.round(density * shore.Length * (d1 - d0) / 10000f);
            if (wanted <= 0) return;
            bool tidy = ctx.S("layout") == "rows";
            uint clump = Hashing.Mix(ctx.Seed, 0x0C10u);

            bool Set(float2 p, uint v)
            {
                shore.Nearest(p, out var inw, out _);
                float dp = shore.Depth(p);
                if (dp < d0 || dp > d1) return false;
                var toWater = -inw;
                var tan = new float2(inw.y, -inw.x);
                float jitter = tidy ? 0f : (Hashing.Unit(Hashing.Mix(v, 3u)) - 0.5f) * 0.35f;
                if (!occ.Free(p, 1.6f, 0.6f, 0.3f)) return false;
                // Loungers beside the canopy (inside it the game hides one of the two overlapping props).
                var spots = new List<float2>();
                int count = tidy ? 2 : (Hashing.Unit(Hashing.Mix(v, 4u)) < 0.2f ? 1 : 2);
                for (int k = 0; k < count; k++)
                {
                    float side = count == 1 ? (Hashing.Unit(v) < 0.5f ? 1f : -1f) : (k == 0 ? -1f : 1f);
                    var q = p + tan * side * 2.2f + inw * 0.3f;
                    if (lounger != null && occ.Free(q, 0.7f, 0.4f, 0.1f)) spots.Add(q);
                }
                ctx.Object(p, Yaw(toWater) + jitter, ObjectRole.Furniture, b.Umbrella, v, -1, umbrella);
                occ.Take(p, 1.6f, 1);
                b.Umbrellas++;
                foreach (var q in spots)
                {
                    var role = lounger == AssetCategories.Bench ? ObjectRole.Bench : ObjectRole.Furniture;
                    ctx.Object(q, Yaw(toWater) + jitter, role, b.Lounger, Hashing.Mix(v, (uint)b.Loungers + 11u), -1, role == ObjectRole.Bench ? null : lounger);
                    occ.Take(q, 0.7f, 1);
                    b.Loungers++;
                }
                return true;
            }

            float rowGap = tidy ? 6.5f : 7.5f;
            int rows = math.max(1, (int)math.floor((d1 - d0) / rowGap) + 1);
            // Loose rows skip ~35 % of the slots and some slots collide with aisles/posts: a denser grid for the same count.
            float spacing = math.max(tidy ? 6f : 6.5f, rows * shore.Length / math.max(1, wanted) * (tidy ? 0.9f : 0.65f));
            for (int r = 0; r < rows && b.Umbrellas < wanted; r++)
            {
                float d = d0 + 0.5f + r * rowGap;
                if (d > d1) break;
                foreach (var line in shore.Offset(d, 1.5f, 6f, 2f))
                {
                    float len = Shoreline.LengthOf(line);
                    for (float s = (r & 1) == 0 ? spacing * 0.5f : spacing; s < len && b.Umbrellas < wanted; s += spacing)
                    {
                        uint v = Hashing.Mix(ctx.Seed, 0x0B00u + (uint)(r * 997) + (uint)(s * 10f));
                        var p = Shoreline.PointOn(line, s, out var t);
                        if (!tidy)
                        {
                            // Loose: a gap where the crowd thins (value-noise clumps), the rest nudged off the grid.
                            float chance = 0.45f + 0.55f * Scatter.Clustering(p, 26f, clump, 0.2f);
                            if (Hashing.Unit(Hashing.Mix(clump, (int)(p.x * 13f), (int)(p.y * 7f))) > chance) continue;
                            var n = new float2(-t.y, t.x);
                            p += t * (Hashing.Unit(Hashing.Mix(v, 7u)) - 0.5f) * spacing * 0.35f + n * (Hashing.Unit(Hashing.Mix(v, 8u)) - 0.5f) * 2.4f;
                        }
                        // A slot blocked by an aisle or a post slides along the row a little.
                        if (!Set(p, v) && !Set(p + t * 2.6f, v)) Set(p - t * 2.6f, v);
                    }
                }
            }
        }

        private static void FirePits(Work b)
        {
            var ctx = b.Ctx; var shore = b.Shore;
            if (!ctx.B("fires")) return;
            // No vanilla fire pit, and the camping fallback is makeshift tents: build the ring from small rocks instead.
            if (!AssetAvailability.Has(AssetCategories.Rock)) return;
            int wanted = math.clamp((int)(shore.Length / 60f), 1, 4);
            float d = math.clamp((b.Wet + b.D) * 0.55f, b.Wet + 4f, b.D - 4f);
            foreach (float s in shore.Stations(wanted, 10f, 0.75f))
            {
                var p = shore.At(s, out var inw, out _);
                var c = p + inw * d;
                if (!b.Occ.Free(c, 3.6f, 0.8f, 0.3f)) continue;
                // A ring of small beach stones, rough campground benches around it.
                uint v = Hashing.Mix(ctx.Seed, 0xF1E0u + (uint)b.Fires);
                for (int k = 0; k < 7; k++)
                {
                    float a = k * 0.8976f + Hashing.Unit(Hashing.Mix(v, (uint)k + 50u)) * 0.3f;
                    var o = ctx.Object(c + new float2(math.cos(a), math.sin(a)) * 0.85f, a, ObjectRole.Furniture,
                        new AssetRef("", "Rock05", "Rock12", "Rock21"), Hashing.Mix(v, (uint)k + 60u), -1, AssetCategories.Rock);
                    o.Scale = 0.55f;
                }
                b.Occ.Take(c, 1.3f, 1);
                float phase = Hashing.Unit(v) * 6.283f;
                for (int k = 0; k < 3; k++)
                {
                    float a = phase + k * 2.094f;
                    var q = c + new float2(math.cos(a), math.sin(a)) * 2.7f;
                    LeisureKit.Put(ctx, b.Occ, q, LeisureKit.Yaw(q, c), ObjectRole.Bench, new AssetRef("", "CampgroundBench01", "CampgroundBench02"), null, 1.1f, Hashing.Mix(v, (uint)k), 0.5f, 0.1f);
                }
                b.Fires++;
            }
        }

        // ───────────── back path & behind ─────────────

        private static void BackPathFurniture(Work b)
        {
            var ctx = b.Ctx; var shore = b.Shore;
            if (!b.HasPath) return;
            float benchSpacing = ctx.F("benchSpacing");
            bool lamps = ctx.B("lamps");
            int benches = 0, lampsN = 0;
            for (int li = 0; li < b.Lines.Count; li++)
            {
                var line = b.Lines[li];
                float len = Shoreline.LengthOf(line);
                if (benchSpacing > 0f)
                    for (float s = benchSpacing * 0.5f; s < len; s += benchSpacing)
                    {
                        var p = Shoreline.PointOn(line, s, out _);
                        shore.Nearest(p, out var inw, out _);
                        var q = p - inw * (b.Hw + 1.7f);
                        uint v = Hashing.Mix(ctx.Seed, 0xBE00u + (uint)benches + (uint)li * 1000u);
                        if (!LeisureKit.Put(ctx, b.Occ, q, Yaw(-inw), ObjectRole.Bench, AssetMix.Pick(BeachArt.Benches, 0u), null, 1.3f, v, 0.5f, 0.2f)) continue;
                        benches++;
                        if ((benches & 1) == 0)
                        {
                            var tan = new float2(inw.y, -inw.x);
                            LeisureKit.Put(ctx, b.Occ, q + tan * 2.3f, 0f, ObjectRole.Bin, AssetMix.Pick(BeachArt.Bins, 0u), null, 0.5f, Hashing.Mix(v, 1u), 0.4f, 0.2f);
                        }
                    }
                if (lamps)
                    for (float s = 12f; s < len; s += 28f)
                    {
                        var p = Shoreline.PointOn(line, s, out _);
                        shore.Nearest(p, out var inw, out _);
                        if (LeisureKit.Put(ctx, b.Occ, p + inw * (b.Hw + 1f), 0f, ObjectRole.Lamp, AssetMix.Pick(BeachArt.Lamps, 0u), null, 0.5f, Hashing.Mix(ctx.Seed, 0x1A00u + (uint)lampsN), 0.4f, 0.2f))
                            lampsN++;
                    }
            }
        }

        private static void HutsAndKiosks(Work b)
        {
            if (!b.HasPath) return;
            Kiosks(b);   // first: a long hut row would otherwise take every spot behind the path
            Huts(b);
        }

        private static void Kiosks(Work b)
        {
            var ctx = b.Ctx; var shore = b.Shore;
            int kiosks = ctx.I("kiosks");
            if (kiosks <= 0) return;
            string kcat = LeisureKit.Category(ctx, "", AssetCategories.Kiosk);
            if (kcat == null) return;
            var tops = new List<float2>();
            foreach (var (top, _) in b.Access) tops.Add(top);
            if (tops.Count == 0) foreach (var line in b.Lines) tops.Add(Shoreline.PointOn(line, Shoreline.LengthOf(line) * 0.5f, out _));
            for (int i = 0; i < kiosks * 2 && b.Kiosks < kiosks; i++)
            {
                // Spread over the access junctions, then try the remaining ones.
                int idx = i < kiosks ? (int)((i + 0.5f) * tops.Count / kiosks) % tops.Count : (i * 3 + 1) % tops.Count;
                var p = tops[idx];
                shore.Nearest(p, out var inw, out _);
                var tan = new float2(inw.y, -inw.x);
                foreach (float along in new[] { 6f, -6f, 12f, -12f })
                {
                    var q = p + inw * (b.Hw + 4f) + tan * along;
                    if (LeisureKit.Put(ctx, b.Occ, q, Yaw(-inw), ObjectRole.Furniture, AssetMix.Pick(BeachArt.Kiosks, (uint)b.Kiosks), kcat, 2.5f, Hashing.Mix(ctx.Seed, 0x6105u + (uint)b.Kiosks), 0.8f, 0.3f))
                    { b.Kiosks++; break; }
                }
            }
        }

        private static void Huts(Work b)
        {
            var ctx = b.Ctx; var shore = b.Shore;
            string mode = ctx.S("huts");
            string hut = mode == "none" ? null : LeisureKit.Category(ctx, ctx.S("hutAsset"), AssetCategories.BeachHut, AssetCategories.GardenShed);
            if (hut == null) return;
            var hutAsset = AssetMix.One(ctx.S("hutAsset"));
            float hd = b.Hw + 2.8f;
            foreach (var line in b.Lines)
            {
                float len = Shoreline.LengthOf(line);
                var places = new List<float>();
                if (mode == "row")
                    for (float s = 4f; s < len - 4f; s += 3.6f) places.Add(s);
                else
                {
                    int groups = math.max(1, (int)(len / 70f));
                    for (int g = 0; g < groups; g++)
                    {
                        float c = (g + 0.5f) * len / groups + 9f;
                        for (int k = -1; k <= 1; k++) places.Add(c + k * 3.6f);
                    }
                }
                foreach (float s in places)
                {
                    if (s < 0f || s > len) continue;
                    var p = Shoreline.PointOn(line, s, out _);
                    shore.Nearest(p, out var inw, out _);
                    var q = p + inw * hd;
                    if (LeisureKit.Put(ctx, b.Occ, q, Yaw(-inw), ObjectRole.Furniture, hutAsset, hut, 1.5f, Hashing.Mix(ctx.Seed, 0x4070u + (uint)b.Huts), 0.6f, 0.15f))
                        b.Huts++;
                }
            }
        }

        /// <summary>
        /// Beach grass through the sand blend behind the promenade: sparse where the sand is still strong, thicker as
        /// it fades into the ground (dune backdrops plant their own belt).
        /// </summary>
        private static void BlendGrass(Work b)
        {
            var ctx = b.Ctx; var shore = b.Shore;
            if (ctx.S("backdrop") == "dunes" || b.Blend < 3f) return;
            float pct = ctx.F("grassDensity") / 100f;
            if (pct <= 0f) return;
            float from = b.D + (b.HasPath ? b.Hw + 1f : 1f), to = b.D + b.Blend + 4f;
            var grass = BeachArt.Or(ctx.L("grassSpecies"), BeachArt.DuneGrass);
            uint salt = Hashing.Mix(ctx.Seed, 0xB1E9u);
            foreach (var ring in shore.Band(from, to))
            {
                var band = LayerRegion.From(ring);
                if (band == null) continue;
                int wanted = (int)(band.Area / 10000f * 380f * pct);
                LeisureKit.Plant(ctx, b.Occ, band, wanted, ObjectRole.Bush, grass, 0.9f, 7f, 0.35f, 0xB1E5u, p =>
                {
                    float t = math.saturate((shore.Depth(p) - b.D) / b.Blend);
                    return Hashing.Unit(Hashing.Mix(salt, (int)(p.x * 5f), (int)(p.y * 5f))) > 0.25f + 0.75f * t;
                });
            }
        }

        private static void Backdrop(Work b)
        {
            var ctx = b.Ctx; var shore = b.Shore; var occ = b.Occ;
            string backdrop = ctx.S("backdrop");
            float behind = b.D + (b.HasPath ? b.Hw + 2.2f : 1.5f);
            float duneEnd = behind + ctx.F("duneDepth");
            var trees = BeachArt.Or(ctx.L("treeSpecies"), BeachArt.Trees(b.Style, backdrop));
            var grass = BeachArt.Or(ctx.L("grassSpecies"), backdrop == "dunes" ? BeachArt.DuneGrass : BeachArt.Shrubs);
            bool canTrees = trees.Length > 0 || AssetAvailability.Has(AssetCategories.Tree);
            float treeDensity = ctx.F("treeDensity");

            if (backdrop == "dunes")
            {
                // Marram grass in clumps over the dunes: thin on the sandy foot, densest on the crests and behind.
                float pct = ctx.F("grassDensity") / 100f;
                uint salt = Hashing.Mix(ctx.Seed, 0xD6A6u);
                foreach (var ring in shore.Band(behind, duneEnd))
                {
                    var band = LayerRegion.From(ring);
                    if (band == null) continue;
                    int wanted = (int)(band.Area / 10000f * 450f * pct);
                    LeisureKit.Plant(ctx, occ, band, wanted, ObjectRole.Bush, grass, 0.9f, 9f, 0.2f, 0xD6A5u, p =>
                    {
                        float t = math.saturate((shore.Depth(p) - behind) / math.max(1f, (duneEnd - behind) * 0.5f));
                        return Hashing.Unit(Hashing.Mix(salt, (int)(p.x * 5f), (int)(p.y * 5f))) > 0.35f + 0.65f * t;
                    });
                }
                if (ctx.B("duneFence")) SandFences(b, behind - 0.6f);
                if (canTrees && treeDensity > 0f)
                    foreach (var ring in shore.Band(duneEnd, shore.MaxDepth + 20f))
                    {
                        var band = LayerRegion.From(ring);
                        if (band == null) continue;
                        LeisureKit.Plant(ctx, occ, band, (int)(band.Area / 10000f * treeDensity * 1.5f), ObjectRole.Tree, trees, 2.5f, 20f, 0.3f, 0x7E55u, null, 1, 3);
                    }
                return;
            }
            if (backdrop == "none" && treeDensity <= 0f) return;
            float factor = backdrop == "woods" ? 1.8f : 1f;
            // Trees stand back from the sand blend (they grow where the ground is ground again).
            float from = (backdrop == "woods" ? behind : behind + 3f) + b.Blend * 0.5f;
            if (canTrees && treeDensity > 0f)
                foreach (var ring in shore.Band(from, shore.MaxDepth + 20f))
                {
                    var band = LayerRegion.From(ring);
                    if (band == null) continue;
                    LeisureKit.Plant(ctx, occ, band, (int)(band.Area / 10000f * treeDensity * factor), ObjectRole.Tree, trees, 2.5f,
                        backdrop == "lawn" ? 26f : 16f, backdrop == "lawn" ? 0.15f : 0.35f, 0x7E56u, null, 1, 3);
                }
            if (backdrop == "woods")
            {
                float pct = ctx.F("grassDensity") / 100f;
                foreach (var ring in shore.Band(from, from + 6f))
                {
                    var band = LayerRegion.From(ring);
                    if (band == null) continue;
                    LeisureKit.Plant(ctx, occ, band, (int)(band.Area / 10000f * 120f * pct), ObjectRole.Bush, grass, 1f, 10f, 0.2f, 0xB0B5u, null);
                }
            }
        }

        /// <summary>Sand fences along the dune foot: short runs with gaps where paths cross.</summary>
        private static void SandFences(Work b, float d)
        {
            var ctx = b.Ctx; var occ = b.Occ;
            foreach (var line in b.Shore.Offset(d, 1f, 10f, 3f))
            {
                float len = Shoreline.LengthOf(line);
                const float run = 16f, gap = 5f;
                for (float s = 2f; s + 4f < len; s += run + gap)
                {
                    var a = Shoreline.PointOn(line, s, out _);
                    var e = Shoreline.PointOn(line, math.min(len, s + run), out _);
                    if (math.distance(a, e) < 4f) continue;
                    bool blocked = false;
                    for (float t = 0f; t <= 1.001f; t += 0.125f)
                    {
                        var q = math.lerp(a, e, t);
                        if (occ.Paths.Overlaps(q, 0.6f, 0.5f) || occ.Discs.Overlaps(q, 0.3f, 0.1f)) { blocked = true; break; }
                    }
                    if (blocked || b.Shore.Land.CrossesBoundary(a, e, false)) continue;
                    ctx.StraightNet(a, e, NetRole.Fence, AssetMix.One(""));
                    occ.TakeCorridor(a, e, 0.3f, 5);
                }
            }
        }
    }
}
