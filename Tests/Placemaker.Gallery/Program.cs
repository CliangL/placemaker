// Placemaker visual review gallery (FEEDBACK-4 decision 5: "nobody looks at the result").
//
// For every shipped recipe (Recipes/*.json) and every fill type without one: the default design,
// each built-in preset, and feature combos (all features on / none). Each is composed with the REAL
// Core (Composer + AutoAssets with the catalog's automatic pools and park palettes) on a fixed
// irregular site, then drawn top-down:
//   surfaces in role colours, path/road nets at their real width, objects at their real footprint
//   (catalog bounds × scale, rotated by yaw) coloured by role, tree crowns at real crown size,
//   terraform as height tint + 1 m contours, plan issues as red/amber rings, a 10 m scale bar.
// Writes <out>/<type>/<variant>.png, <out>/index.html and <out>/gallery.json (metrics per image).
//
//   dotnet run -c Release --project Tests/Placemaker.Gallery -- --out docs/gallery [--only park,memorialPark]

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Placemaker.Core;
using Placemaker.Core.Build;
using Placemaker.Core.Tool;
using Unity.Mathematics;

namespace Placemaker.Gallery
{
    public static class Program
    {
        public static int Main(string[] args)
        {
            string outDir = "docs/gallery", repo = null, fixture = null;
            HashSet<string> only = null;
            for (int i = 0; i < args.Length; i++)
            {
                string next = i + 1 < args.Length ? args[i + 1] : null;
                switch (args[i])
                {
                    case "--out": outDir = next; i++; break;
                    case "--repo": repo = next; i++; break;
                    case "--catalog": fixture = next; i++; break;
                    case "--only": only = new HashSet<string>(next.Split(','), StringComparer.OrdinalIgnoreCase); i++; break;
                    case "--scale": Renderer.LayoutScale = float.Parse(next, System.Globalization.CultureInfo.InvariantCulture); i++; break;
                }
            }
            repo ??= FindRepo();
            if (!Path.IsPathRooted(outDir)) outDir = Path.GetFullPath(Path.Combine(repo, outDir));
            fixture ??= Path.Combine(repo, "Tests", "Placemaker.Tests", "Fixtures", "catalog-bounds.json");
            var catalog = Catalog.Load(fixture);
            Directory.CreateDirectory(outDir);

            var jobs = Variants.All(repo, only);
            Console.WriteLine($"Gallery: {jobs.Count} images, {GeneratorRegistry.All.Count} layers, catalog {catalog.Items.Count} items → {outDir}");
            var results = new Result[jobs.Count];
            var sw = Stopwatch.StartNew();
            Parallel.For(0, jobs.Count, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 1) }, i =>
            {
                results[i] = Renderer.Run(jobs[i], catalog, outDir);
                Console.WriteLine($"  {jobs[i].Type}/{jobs[i].Variant}: {results[i].Summary}");
            });
            Report.Write(outDir, jobs, results);
            Console.WriteLine($"Done in {sw.Elapsed.TotalSeconds:0.0}s. Open {Path.Combine(outDir, "index.html")}");
            return 0;
        }

        private static string FindRepo()
        {
            var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "Placemaker.csproj"))) return dir.FullName;
                dir = dir.Parent;
            }
            dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "Placemaker.csproj"))) return dir.FullName;
                dir = dir.Parent;
            }
            throw new DirectoryNotFoundException("Repository root (Placemaker.csproj) not found; pass --repo.");
        }
    }

    // ───────────────────────── catalog fixture ─────────────────────────

    public sealed class Catalog
    {
        public Dictionary<string, List<string>> Pools = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        public Dictionary<string, Dictionary<string, List<string>>> Palettes = new Dictionary<string, Dictionary<string, List<string>>>(StringComparer.Ordinal);
        public Dictionary<string, AssetInfo> Items = new Dictionary<string, AssetInfo>(StringComparer.Ordinal);
        public List<string> PaletteNames = new List<string>();

        public static Catalog Load(string path)
        {
            var c = new Catalog();
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;
            foreach (var p in root.GetProperty("pools").EnumerateObject())
                c.Pools[p.Name] = p.Value.EnumerateArray().Select(x => x.GetString()).ToList();
            foreach (var p in root.GetProperty("palettes").EnumerateObject())
            {
                var byCat = new Dictionary<string, List<string>>(StringComparer.Ordinal);
                foreach (var q in p.Value.EnumerateObject()) byCat[q.Name] = q.Value.EnumerateArray().Select(x => x.GetString()).ToList();
                c.Palettes[p.Name] = byCat;
            }
            c.PaletteNames = c.Palettes.Keys.OrderBy(x => x, StringComparer.Ordinal).ToList();
            foreach (var p in root.GetProperty("items").EnumerateObject())
            {
                var info = new AssetInfo { Id = p.Name };
                if (p.Value.TryGetProperty("b", out var b))
                {
                    var a = b.EnumerateArray().Select(x => (float)x.GetDouble()).ToArray();
                    info.BoundsMin = new float3(a[0], 0, a[1]);
                    info.BoundsMax = new float3(a[2], a[4], a[3]);
                }
                if (p.Value.TryGetProperty("w", out var w)) info.NetWidth = (float)w.GetDouble();
                c.Items[p.Name] = info;
            }
            return c;
        }

        public IReadOnlyList<string> Pool(string category)
        {
            while (!string.IsNullOrEmpty(category))
            {
                if (Pools.TryGetValue(category, out var l)) return l;
                int dot = category.LastIndexOf('.');
                if (dot <= 0) break;
                category = category.Substring(0, dot);
            }
            return Array.Empty<string>();
        }

        public AutoAssetSource Source() => new AutoAssetSource
        {
            Pool = Pool,
            Exists = id => Items.ContainsKey(id) || Pools.Values.Any(l => l.Contains(id)),
            Palettes = PaletteNames,
            PaletteItems = (p, cat) => p != null && cat != null && Palettes.TryGetValue(p, out var m) && m.TryGetValue(cat, out var l) ? l : (IReadOnlyList<string>)Array.Empty<string>(),
        };
    }

    // ───────────────────────── what to render ─────────────────────────

    public sealed class Job
    {
        public string Type, Variant, Title;
        public Recipe Recipe;
        public float TargetArea;
    }

    public static class Variants
    {
        public static List<Job> All(string repo, HashSet<string> only)
        {
            var editor = new DesignEditor(GeneratorRegistry.Get);
            var recipes = new List<(string id, Recipe r)>();
            var roots = new HashSet<string>(StringComparer.Ordinal);
            foreach (var f in Directory.GetFiles(Path.Combine(repo, "Recipes"), "*.json").OrderBy(x => x, StringComparer.Ordinal))
            {
                try
                {
                    var rf = RecipeJson.Parse(File.ReadAllText(f));
                    recipes.Add((string.IsNullOrEmpty(rf.Recipe.Id) ? Path.GetFileNameWithoutExtension(f) : rf.Recipe.Id, rf.Recipe));
                    roots.Add(rf.Recipe.Root.Layer);
                }
                catch (Exception e) { Console.WriteLine($"Recipe {f}: {e.Message}"); }
            }
            foreach (var g in GeneratorRegistry.All)
            {
                var attr = (AreaGeneratorAttribute)Attribute.GetCustomAttribute(g.GetType(), typeof(AreaGeneratorAttribute));
                if (g.Kind != LayerKind.Fill || (attr?.Hidden ?? false) || roots.Contains(g.Id) || recipes.Any(r => r.id == g.Id)) continue;
                recipes.Add((g.Id, Recipe.Single(g, 1)));
            }

            var jobs = new List<Job>();
            foreach (var (id, template) in recipes)
            {
                if (only != null && !only.Contains(id)) continue;
                var gen = GeneratorRegistry.Get(template.Root.Layer);
                if (gen == null) continue;
                float area = TargetArea(gen);
                Job Make(string variant, string title, Action<Recipe> edit)
                {
                    var r = template.Clone();
                    editor.ApplyDefaults(r);
                    uint n = 0;
                    editor.AddDefaultFeatures(r, () => ++n);
                    edit?.Invoke(r);
                    uint k = 0;
                    foreach (var kv in RecipePaths.Walk(r)) kv.Value.Seed = 1000u + (++k);
                    return new Job { Type = id, Variant = variant, Title = title, Recipe = r, TargetArea = area };
                }
                jobs.Add(Make("default", "Default", null));
                IReadOnlyList<PresetInfo> presets;
                try { presets = gen.Presets ?? Array.Empty<PresetInfo>(); } catch (Exception) { presets = Array.Empty<PresetInfo>(); }
                foreach (var p in presets)
                {
                    if (p == null || string.IsNullOrEmpty(p.Id)) continue;
                    jobs.Add(Make("preset-" + Safe(p.Id), "Preset " + p.Id, r => editor.ApplyPreset(r, RecipePaths.Root, p.Id)));
                }
                var features = Features.Of(gen);
                if (features.Count > 0)
                {
                    jobs.Add(Make("features-all", "All features on", r =>
                    {
                        foreach (var f in features) editor.SetFeature(r, RecipePaths.Root, f.Layer, true, 77);
                    }));
                    jobs.Add(Make("features-none", "No features", r =>
                    {
                        foreach (var f in features) editor.SetFeature(r, RecipePaths.Root, f.Layer, false, 0);
                    }));
                }
            }
            return jobs;
        }

        private static float TargetArea(IAreaGenerator g)
        {
            float min = g.MinArea > 0 ? g.MinArea : 100f, max = g.MaxArea > 0 ? g.MaxArea : 1e7f;
            return math.clamp(math.max(12000f, min * 1.3f), min, max);
        }

        public static string Safe(string s)
        {
            var sb = new StringBuilder();
            foreach (var ch in s) sb.Append(char.IsLetterOrDigit(ch) || ch == '-' || ch == '_' ? ch : '_');
            return sb.ToString();
        }
    }

    // ───────────────────────── compose + draw ─────────────────────────

    public sealed class Result
    {
        public string Summary = "", File = "";
        public double Ms;
        public int Objects, Nets, Surfaces, Errors, Warnings, BenchesOffPath, Overlaps, Outside, Decals;
        public double PathMetres;
        public Dictionary<string, int> ByRole = new Dictionary<string, int>();
        public List<string> IssueKeys = new List<string>();
        public string Failure;
    }

    public static class Renderer
    {
        private static readonly float2[] BaseRing =
        {
            new float2(0, 0), new float2(140, -6), new float2(152, 58), new float2(118, 102), new float2(46, 96), new float2(6, 70),
        };

        public static float3[] Site(float targetArea)
        {
            double a = 0;
            for (int i = 0, j = BaseRing.Length - 1; i < BaseRing.Length; j = i++) a += BaseRing[j].x * BaseRing[i].y - BaseRing[i].x * BaseRing[j].y;
            float s = (float)math.sqrt(targetArea / math.abs(a * 0.5));
            return BaseRing.Select(p => new float3(p.x * s, 0, p.y * s)).ToArray();
        }

        /// <summary>Layout size the gallery renders at (--scale; default = the game option default, 0.5).</summary>
        public static float LayoutScale = SiteContext.DefaultLayoutScale;

        public static Result Run(Job job, Catalog catalog, string outDir)
        {
            var res = new Result();
            var ring = Site(job.TargetArea);
            var shape = new AreaShape { Ring = ring, Source = AreaSource.Drawn };
            var site = new SiteContext { Assets = catalog.Items, LayoutScale = LayoutScale };
            ComposedPlan plan;
            var sw = Stopwatch.StartNew();
            try
            {
                AddMarkersIfNeeded(job.Recipe, ring);
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                plan = Composer.Compose(job.Recipe, shape, site, cts.Token);
                AutoAssets.Resolve(plan, catalog.Source());
            }
            catch (Exception e)
            {
                res.Failure = e.GetType().Name + ": " + e.Message;
                res.Summary = "FAILED " + res.Failure;
                plan = new ComposedPlan();
            }
            res.Ms = sw.Elapsed.TotalMilliseconds;

            var dir = Path.Combine(outDir, job.Type);
            Directory.CreateDirectory(dir);
            res.File = job.Type + "/" + job.Variant + ".png";
            Draw(plan, ring, catalog, Path.Combine(dir, job.Variant + ".png"), res);
            if (res.Failure == null)
                res.Summary = $"{res.Objects} obj, {res.Nets} nets ({res.PathMetres:0} m), {res.Surfaces} surf, {res.Errors} err, {res.Warnings} warn, {res.Ms:0} ms";
            return res;
        }

        /// <summary>A layer that is not ready without markers (parking needs an entrance) gets one on the first edge.</summary>
        private static void AddMarkersIfNeeded(Recipe r, float3[] ring)
        {
            var step = r.Root;
            var gen = GeneratorRegistry.Get(step.Layer);
            if (gen == null) return;
            var input = new AreaInput { Shape = new AreaShape { Ring = ring }, Settings = step.Settings, Markers = step.Markers };
            Readiness ready;
            try { ready = gen.CheckReady(input); } catch (Exception) { return; }
            if (ready == null || ready.Ready) return;
            var kinds = (gen.Controls ?? Array.Empty<ControlSchema>()).Where(c => c.Kind == ControlKind.Markers && c.MarkerKinds != null).SelectMany(c => c.MarkerKinds).Distinct().ToList();
            foreach (var kind in kinds)
            {
                var m = new EdgeMarker { Kind = kind, RingSegment = 0, RingT = 0.5f, Position = (ring[0] + ring[1]) * 0.5f };
                step.Markers.Add(m);
                try { if (gen.CheckReady(input).Ready) return; } catch (Exception) { return; }
                step.Markers.Remove(m);
            }
        }

        // ─── palette ───

        private static readonly Rgba Ground = Rgba.Hex("#ece8dd");
        private static readonly Rgba Outside = Rgba.Hex("#d9d6ce");

        private static Rgba SurfaceColor(PlannedSurface s)
        {
            string cat = s.Category ?? "";
            if (cat.StartsWith("surface.sand")) return Rgba.Hex("#ead9a2");
            if (cat.StartsWith("surface.gravel")) return Rgba.Hex("#c9c2b2");
            if (cat.StartsWith("surface.dirt")) return Rgba.Hex("#b9976a");
            if (cat.StartsWith("surface.bark")) return Rgba.Hex("#93704c");
            if (cat.StartsWith("surface.clay")) return Rgba.Hex("#c88a5e");
            if (cat.StartsWith("surface.rock")) return Rgba.Hex("#9e9e9e");
            if (cat.StartsWith("surface.water")) return Rgba.Hex("#7fb2e5");
            switch (s.Role)
            {
                case SurfaceRole.Grass: return Rgba.Hex("#b5d99a");
                case SurfaceRole.Paving: return Rgba.Hex("#d6cfbf");
                case SurfaceRole.Plaza: return Rgba.Hex("#e2d6bb");
                case SurfaceRole.Asphalt: return Rgba.Hex("#76767c");
                case SurfaceRole.Water: return Rgba.Hex("#7fb2e5");
                case SurfaceRole.Decorative: return Rgba.Hex("#e3aacb");
                default: return Rgba.Hex("#cdb98f");
            }
        }

        private static (Rgba fill, Rgba edge) NetColors(PlannedNet n)
        {
            string cat = n.Category ?? "";
            if (cat == "net.bike") return (Rgba.Hex("#e6b3a8"), Rgba.Hex("#9c5a4c"));
            if (cat.Contains("trail")) return (Rgba.Hex("#d9c39a"), Rgba.Hex("#8c7550"));
            switch (n.Role)
            {
                case NetRole.Avenue: case NetRole.Street: case NetRole.PerimeterRoad: case NetRole.CulDeSac: case NetRole.Roundabout: case NetRole.Alley:
                    return (Rgba.Hex("#8d8d93"), Rgba.Hex("#4f4f55"));
                case NetRole.DriveAisle: case NetRole.DriveConnector: return (Rgba.Hex("#a3adc4"), Rgba.Hex("#56648a"));
                case NetRole.Fence: return (Rgba.Hex("#7a5a3c"), Rgba.Hex("#7a5a3c"));
                default: return (Rgba.Hex("#f4e7c6"), Rgba.Hex("#a88c58"));
            }
        }

        private static Rgba ObjectColor(PlannedObject o, string cat)
        {
            if (cat.StartsWith("hedge")) return Rgba.Hex("#3f7f37");
            if (cat.StartsWith("flowerbed")) return Rgba.Hex("#d470a8");
            if (cat.StartsWith("playground") || cat.StartsWith("play")) return Rgba.Hex("#e8833a");
            if (cat.StartsWith("sports")) return Rgba.Hex("#d65a5a");
            if (cat.StartsWith("picnic")) return Rgba.Hex("#b06f3c");
            if (cat.StartsWith("rock")) return Rgba.Hex("#8a8a8a");
            if (cat.StartsWith("cemetery")) return Rgba.Hex("#9a9aa8");
            if (cat.StartsWith("memorial") || cat.StartsWith("monument")) return Rgba.Hex("#6c63b5");
            if (cat.StartsWith("kiosk") || cat.StartsWith("shelter") || cat.StartsWith("restroom")) return Rgba.Hex("#c0703f");
            switch (o.Role)
            {
                case ObjectRole.Tree: return Rgba.Hex("#3c8a3c");
                case ObjectRole.Bush: return Rgba.Hex("#6cb04c");
                case ObjectRole.Planter: return Rgba.Hex("#a07850");
                case ObjectRole.Bench: return Rgba.Hex("#7b4a1e");
                case ObjectRole.Lamp: return Rgba.Hex("#f0c419");
                case ObjectRole.Bin: return Rgba.Hex("#3a3a3a");
                case ObjectRole.Centerpiece: return Rgba.Hex("#2c78c8");
                case ObjectRole.Decal: return Rgba.Hex("#8f7a5a");
                case ObjectRole.ParkingBay: return Rgba.Hex("#ffffff");
                case ObjectRole.Charger: return Rgba.Hex("#22aa66");
                default: return Rgba.Hex("#7a6fb0");
            }
        }

        // ─── drawing ───

        private static void Draw(ComposedPlan plan, float3[] ring, Catalog cat, string file, Result res)
        {
            var ring2 = ring.Select(p => p.xz).ToArray();
            float2 min = ring2[0], max = ring2[0];
            foreach (var p in ring2) { min = math.min(min, p); max = math.max(max, p); }
            float margin = math.max(10f, math.cmax(max - min) * 0.06f);
            min -= margin; max += margin;
            float2 ext = max - min;
            float ppm = math.min(6f, 880f / math.cmax(ext));
            int w = (int)math.ceil(ext.x * ppm), h = (int)math.ceil(ext.y * ppm);
            var cv = new Canvas(w, h, min, ppm, Outside);
            cv.FillPolygon(ring2, Ground);

            // Terraform: height after all layers (last plan with HeightAfter wins), tinted + 1 m contours.
            Func<float2, float> height = null;
            foreach (var l in plan.Layers) if (l?.HeightAfter != null) height = l.HeightAfter;
            if (height == null && plan.Layers.Any(l => l != null && l.Terraform.Count > 0))
            {
                var tf = plan.Layers.Where(l => l != null).SelectMany(l => l.Terraform).Where(t => t?.Ring != null && t.Ring.Length >= 3).ToList();
                height = p => { float hh = 0; foreach (var t in tf) if (Inside(t.Ring, p)) hh = t.TargetHeight; return hh; };
            }
            if (height != null)
            {
                var hf = height;
                float px = cv.Px(1f);
                cv.Shade(p =>
                {
                    float hh;
                    try { hh = hf(p); } catch (Exception) { return default; }
                    if (math.abs(hh) < 0.05f) return default;
                    float a = math.clamp(math.abs(hh) / 6f, 0.08f, 0.45f);
                    var c = hh > 0 ? new Rgba(0.75f, 0.45f, 0.2f, a) : new Rgba(0.2f, 0.4f, 0.8f, a);
                    // contour every metre
                    float hx = Safe(hf, p + new float2(px * 1.5f, 0)), hz = Safe(hf, p + new float2(0, px * 1.5f));
                    if (math.floor(hh) != math.floor(hx) || math.floor(hh) != math.floor(hz)) return new Rgba(0.25f, 0.2f, 0.15f, 0.55f);
                    return c;
                }, min, max);
            }

            // Surfaces: grouped by colour; translucent fill + crisp darker outline.
            foreach (var l in plan.Layers)
            {
                if (l == null) continue;
                foreach (var s in l.Surfaces)
                {
                    if (s?.Ring == null || s.Ring.Length < 3) continue;
                    res.Surfaces++;
                    var c = SurfaceColor(s);
                    var r2 = s.Ring.Select(p => p.xz).ToArray();
                    cv.FillPolygon(r2, c.WithA(0.85f));
                    cv.StrokePolyline(r2, cv.Px(1f), c.Darker(0.7f), closed: true);
                }
                foreach (var a in l.Areas)
                {
                    if (a?.Ring == null || a.Ring.Length < 3) continue;
                    cv.DashedPolyline(Close(a.Ring.Select(p => p.xz).ToArray()), cv.Px(1f), 2f, 1.5f, Rgba.Hex("#5a7fa8", 0.8f));
                }
                foreach (var t in l.Terraform)
                    if (t?.Ring != null && t.Ring.Length >= 3)
                        cv.DashedPolyline(Close(t.Ring.Select(p => p.xz).ToArray()), cv.Px(1.2f), 3f, 2f, t.Permanent ? Rgba.Hex("#d08a00") : Rgba.Hex("#4f86c6"));
            }

            // Terrain paint (rocky outcrops, natural ground): grey-brown, alpha by brush strength.
            foreach (var l in plan.Layers.Where(l => l != null))
                foreach (var p in l.Paint)
                    if (p?.Ring != null && p.Ring.Length >= 3)
                        cv.FillPolygon(p.Ring.Select(v => v.xz).ToArray(), Rgba.Hex("#8a8172", 0.15f + 0.45f * p.Strength));

            // Nets: edges at real width first (so junctions merge), then fill, then a thin centre line.
            var nets = plan.Layers.Where(l => l != null).SelectMany(l => l.Nets).Where(n => n != null).ToList();
            var polylines = nets.Select(n => (n, pts: Flatten(n), hw: HalfWidth(n, cat))).ToList();
            foreach (var (n, pts, hw) in polylines)
            {
                res.Nets++;
                for (int i = 0; i + 1 < pts.Length; i++) res.PathMetres += math.distance(pts[i], pts[i + 1]);
                if (n.Role == NetRole.Fence) continue;
                cv.StrokePolyline(pts, hw * 2f + cv.Px(1.6f), NetColors(n).edge);
            }
            foreach (var (n, pts, hw) in polylines)
            {
                var (fill, edge) = NetColors(n);
                if (n.Role == NetRole.Fence) { cv.StrokePolyline(pts, math.max(0.4f, cv.Px(1.5f)), fill); continue; }
                cv.StrokePolyline(pts, hw * 2f, fill);
            }
            foreach (var (n, pts, hw) in polylines)
                if (n.Role != NetRole.Fence && hw * 2f * cv.PixelsPerMetre > 8f)
                    cv.DashedPolyline(pts, cv.Px(0.8f), 2f, 2f, NetColors(n).edge.WithA(0.5f));

            // Area outline on top of surfaces.
            cv.StrokePolyline(ring2, cv.Px(2f), Rgba.Hex("#33415c"), closed: true);

            // Objects: decals first (faint), then ground cover, then furniture, then trees (crowns over everything).
            var objs = plan.Layers.Where(l => l != null).SelectMany(l => l.Objects).Where(o => o != null).ToList();
            res.Objects = objs.Count;
            var fps = new List<(PlannedObject o, string cat, float2[] poly, float r)>();
            foreach (var o in objs)
            {
                string c = RoleCategories.ForObject(o) ?? "";
                res.ByRole[o.Role.ToString()] = res.ByRole.TryGetValue(o.Role.ToString(), out var k) ? k + 1 : 1;
                var (poly, r) = Footprint(o, cat);
                fps.Add((o, c, poly, r));
            }
            int Order(PlannedObject o) => o.Role == ObjectRole.Decal ? 0 : o.Role == ObjectRole.ParkingBay ? 1 : o.Role == ObjectRole.Bush ? 3 : o.Role == ObjectRole.Tree ? 5 : 2;
            foreach (var f in fps.OrderBy(f => Order(f.o)))
                DrawObject(cv, f.o, f.cat, f.poly, f.r);

            // Issues
            foreach (var i in plan.AllIssues())
            {
                if (i == null) continue;
                if (i.Severity == IssueSeverity.Error) res.Errors++;
                else if (i.Severity == IssueSeverity.Warning) res.Warnings++;
                res.IssueKeys.Add(i.Severity.ToString()[0] + ":" + i.Key);
                if (i.At.HasValue)
                {
                    var c = i.Severity == IssueSeverity.Error ? Rgba.Hex("#e02020") : Rgba.Hex("#e0a020");
                    cv.StrokeCircle(i.At.Value.xz, 5f, cv.Px(2.5f), c);
                }
            }

            // Scale bar: 10 m, bottom-left.
            var sb = new float2(min.x + margin * 0.3f, min.y + margin * 0.3f);
            cv.FillPolygon(new[] { sb, sb + new float2(10, 0), sb + new float2(10, cv.Px(4)), sb + new float2(0, cv.Px(4)) }, Rgba.Hex("#222222"));
            cv.FillPolygon(new[] { sb + new float2(10, 0), sb + new float2(20, 0), sb + new float2(20, cv.Px(4)), sb + new float2(10, cv.Px(4)) }, Rgba.Hex("#ffffff"));
            cv.StrokePolyline(new[] { sb, sb + new float2(20, 0), sb + new float2(20, cv.Px(4)), sb + new float2(0, cv.Px(4)) }, cv.Px(1), Rgba.Hex("#222222"), closed: true);

            Metrics(res, fps, polylines.Select(p => (p.n, p.pts, p.hw)).ToList(), ring2);
            cv.SavePng(file);
        }

        private static float Safe(Func<float2, float> f, float2 p) { try { return f(p); } catch (Exception) { return 0; } }

        private static float2[] Close(float2[] r) => r.Concat(new[] { r[0] }).ToArray();

        private static float HalfWidth(PlannedNet n, Catalog cat)
        {
            if (!string.IsNullOrEmpty(n.Asset.Id) && cat.Items.TryGetValue(n.Asset.Id, out var info) && info.NetWidth > 0.5f) return info.NetWidth * 0.5f;
            return Composer.DefaultHalfWidth(n.Role);
        }

        private static float2[] Flatten(PlannedNet n)
        {
            int seg = math.clamp((int)(math.distance(n.A.xz, n.D.xz) / 2f), 1, 24);
            if (math.lengthsq(math.cross(n.B - n.A, n.D - n.A)) < 1e-3f && math.lengthsq(math.cross(n.C - n.A, n.D - n.A)) < 1e-3f) seg = 1;
            var pts = new float2[seg + 1];
            for (int i = 0; i <= seg; i++) pts[i] = Composer.Bezier(n.A, n.B, n.C, n.D, i / (float)seg).xz;
            return pts;
        }

        /// <summary>World footprint polygon (catalog bounds × scale, rotated by yaw) and a crown/fallback radius.</summary>
        private static (float2[] poly, float r) Footprint(PlannedObject o, Catalog cat)
        {
            float s = o.Scale > 0.05f ? o.Scale : 1f;
            float2 bmin, bmax;
            if (!string.IsNullOrEmpty(o.Asset.Id) && cat.Items.TryGetValue(o.Asset.Id, out var info) && math.cmax(info.BoundsMax.xz - info.BoundsMin.xz) > 0.05f)
            {
                bmin = info.BoundsMin.xz * s; bmax = info.BoundsMax.xz * s;
                // Same crown estimate as the in-game sketch (PlacemakerOverlaySystem.CrownRadius): tree bounds
                // are near the trunk box, the visible crown is about 0.28 x height.
                if (o.Role == ObjectRole.Tree)
                {
                    float crown = math.clamp(math.max(math.cmax(info.BoundsMax.xz - info.BoundsMin.xz) * 0.5f, (info.BoundsMax.y - info.BoundsMin.y) * 0.28f), 1f, 7f) * s;
                    bmin = new float2(-crown); bmax = new float2(crown);
                }
            }
            else
            {
                float r0 = Composer.DefaultRadius(o.Role) * s;
                bmin = new float2(-r0); bmax = new float2(r0);
            }
            float c = math.cos(o.Yaw), sn = math.sin(o.Yaw);
            float2 Rot(float2 v) => new float2(v.x * c + v.y * sn, -v.x * sn + v.y * c) + o.Position.xz;
            var poly = new[] { Rot(bmin), Rot(new float2(bmax.x, bmin.y)), Rot(bmax), Rot(new float2(bmin.x, bmax.y)) };
            return (poly, math.cmax(bmax - bmin) * 0.5f);
        }

        private static void DrawObject(Canvas cv, PlannedObject o, string cat, float2[] poly, float r)
        {
            var col = ObjectColor(o, cat);
            var p = o.Position.xz;
            float minR = cv.Px(2f);
            switch (o.Role)
            {
                case ObjectRole.Tree:
                    cv.FillCircle(p, math.max(r, minR), col.WithA(0.5f));
                    cv.StrokeCircle(p, math.max(r, minR), cv.Px(1f), col.Darker(0.6f).WithA(0.8f));
                    cv.FillCircle(p, math.max(r * 0.22f, cv.Px(1.5f)), col.Darker(0.45f));
                    return;
                case ObjectRole.Bush:
                    if (cat.StartsWith("hedge")) break;
                    cv.FillCircle(p, math.max(r, minR), col.WithA(0.85f));
                    cv.StrokeCircle(p, math.max(r, minR), cv.Px(0.8f), col.Darker(0.6f));
                    return;
                case ObjectRole.Centerpiece:
                    cv.FillPolygon(poly, col.WithA(0.35f));
                    cv.StrokeCircle(p, math.max(r, cv.Px(4f)), cv.Px(2f), col);
                    cv.FillCircle(p, cv.Px(2f), col.Darker(0.6f));
                    return;
                case ObjectRole.Lamp:
                    cv.FillCircle(p, math.max(r, cv.Px(2.5f)), col);
                    cv.StrokeCircle(p, math.max(r, cv.Px(2.5f)), cv.Px(0.8f), Rgba.Hex("#5a4a00"));
                    return;
                case ObjectRole.Decal:
                    cv.FillPolygon(poly, col.WithA(0.12f));
                    return;
                case ObjectRole.ParkingBay:
                    cv.StrokePolyline(poly, cv.Px(1f), Rgba.Hex("#ffffff", 0.9f), closed: true);
                    return;
                case ObjectRole.Marker: case ObjectRole.AccessPoint: case ObjectRole.AnimalSpawner:
                    cv.FillCircle(p, cv.Px(2f), Rgba.Hex("#c02cc0"));
                    return;
            }
            // Footprint rectangle, enlarged to stay visible.
            if (math.cmax(math.abs(poly[2] - poly[0])) * cv.PixelsPerMetre < 3f)
            {
                cv.FillCircle(p, cv.Px(1.8f), col);
                return;
            }
            cv.FillPolygon(poly, col.WithA(0.9f));
            cv.StrokePolyline(poly, cv.Px(0.8f), col.Darker(0.55f), closed: true);
        }

        private static bool Inside(float3[] ring, float2 p)
        {
            bool c = false;
            for (int i = 0, j = ring.Length - 1; i < ring.Length; j = i++)
                if ((ring[i].z > p.y) != (ring[j].z > p.y) && p.x < (ring[j].x - ring[i].x) * (p.y - ring[i].z) / (ring[j].z - ring[i].z) + ring[i].x) c = !c;
            return c;
        }

        private static bool Inside(float2[] ring, float2 p)
        {
            bool c = false;
            for (int i = 0, j = ring.Length - 1; i < ring.Length; j = i++)
                if ((ring[i].y > p.y) != (ring[j].y > p.y) && p.x < (ring[j].x - ring[i].x) * (p.y - ring[i].y) / (ring[j].y - ring[i].y) + ring[i].x) c = !c;
            return c;
        }

        private static float SegDist(float2 p, float2 a, float2 b)
        {
            var ab = b - a; float t = math.saturate(math.dot(p - a, ab) / math.max(1e-6f, math.lengthsq(ab)));
            return math.distance(p, a + ab * t);
        }

        /// <summary>Quick QA numbers: benches far from any path, overlapping solid footprints, objects outside the area.</summary>
        private static void Metrics(Result res, List<(PlannedObject o, string cat, float2[] poly, float r)> fps, List<(PlannedNet n, float2[] pts, float hw)> nets, float2[] ring)
        {
            var paths = nets.Where(n => n.n.Role != NetRole.Fence).ToList();
            foreach (var f in fps)
            {
                if (f.o.Role == ObjectRole.Decal) { res.Decals++; continue; }
                if (!Inside(ring, f.o.Position.xz)) res.Outside++;
                if (f.o.Role == ObjectRole.Bench)
                {
                    float best = float.MaxValue;
                    foreach (var n in paths)
                        for (int i = 0; i + 1 < n.pts.Length; i++) best = math.min(best, SegDist(f.o.Position.xz, n.pts[i], n.pts[i + 1]) - n.hw);
                    if (best > 3f) res.BenchesOffPath++;
                }
            }
            // Solid objects (not trees: crowns may overlap; not decals/bays) whose cores overlap.
            var solid = fps.Where(f => f.o.Role != ObjectRole.Tree && f.o.Role != ObjectRole.Decal && f.o.Role != ObjectRole.ParkingBay
                                       && f.o.Role != ObjectRole.Marker && f.o.Role != ObjectRole.AccessPoint && f.o.Role != ObjectRole.AnimalSpawner
                                       && !f.cat.StartsWith("hedge") && f.o.Role != ObjectRole.FencePiece).ToList();
            var grid = new Dictionary<(int, int), List<int>>();
            for (int i = 0; i < solid.Count; i++)
            {
                var k = ((int)math.floor(solid[i].o.Position.x / 8f), (int)math.floor(solid[i].o.Position.z / 8f));
                if (!grid.TryGetValue(k, out var l)) grid[k] = l = new List<int>();
                l.Add(i);
            }
            for (int i = 0; i < solid.Count; i++)
            {
                var a = solid[i];
                int gx = (int)math.floor(a.o.Position.x / 8f), gz = (int)math.floor(a.o.Position.z / 8f);
                bool hit = false;
                for (int dx = -1; dx <= 1 && !hit; dx++)
                    for (int dz = -1; dz <= 1 && !hit; dz++)
                        if (grid.TryGetValue((gx + dx, gz + dz), out var l))
                            foreach (var j in l)
                            {
                                if (j == i) continue;
                                var b = solid[j];
                                if (math.distance(a.o.Position.xz, b.o.Position.xz) < (a.r + b.r) * 0.5f) { hit = true; break; }
                            }
                if (hit) res.Overlaps++;
            }
        }
    }

    // ───────────────────────── index.html + gallery.json ─────────────────────────

    public static class Report
    {
        public static void Write(string outDir, List<Job> jobs, Result[] results)
        {
            var sb = new StringBuilder();
            sb.Append("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">");
            sb.Append("<title>Placemaker gallery</title><style>");
            sb.Append(":root{--bg:#f6f5f1;--fg:#1d2330;--muted:#5b6475;--card:#fff;--line:#d9d6ce}");
            sb.Append("@media (prefers-color-scheme:dark){:root{--bg:#15181e;--fg:#e8eaee;--muted:#9aa3b2;--card:#1f232b;--line:#333a46}}");
            sb.Append("body{margin:0;padding:16px;background:var(--bg);color:var(--fg);font:14px/1.45 system-ui,sans-serif}h1{margin:0 0 4px}h2{margin:28px 0 8px;border-bottom:1px solid var(--line)}");
            sb.Append(".grid{display:grid;grid-template-columns:repeat(auto-fill,minmax(300px,1fr));gap:12px}.card{background:var(--card);border:1px solid var(--line);border-radius:8px;padding:8px}");
            sb.Append(".card img{width:100%;height:auto;border-radius:4px;display:block}.m{color:var(--muted);font-size:12px}.bad{color:#d33}.leg span{display:inline-block;margin-right:12px}.sw{display:inline-block;width:12px;height:12px;border-radius:2px;vertical-align:-1px;margin-right:4px;border:1px solid #0003}");
            sb.Append("</style></head><body><h1>Placemaker visual review gallery</h1>");
            sb.Append($"<p class=\"m\">Generated {DateTime.Now:yyyy-MM-dd HH:mm} by Tests/Placemaker.Gallery from the real Core planners and catalog footprints. {jobs.Count} images. See <a href=\"REVIEW.md\">REVIEW.md</a>.</p>");
            sb.Append("<p class=\"leg m\">");
            foreach (var (c, t) in new[] { ("#b5d99a", "grass"), ("#d6cfbf", "paving"), ("#e2d6bb", "plaza"), ("#76767c", "asphalt"), ("#7fb2e5", "water"), ("#cdb98f", "ground/natural"), ("#f4e7c6", "path (real width)"), ("#8d8d93", "road"), ("#3c8a3c", "tree crown"), ("#6cb04c", "bush"), ("#3f7f37", "hedge"), ("#7b4a1e", "bench"), ("#f0c419", "lamp"), ("#3a3a3a", "bin"), ("#2c78c8", "centerpiece ring"), ("#e8833a", "playground"), ("#7a6fb0", "other furniture"), ("#8f7a5a", "decal (faint)"), ("#e02020", "issue") })
                sb.Append($"<span><i class=\"sw\" style=\"background:{c}\"></i>{t}</span>");
            sb.Append("<span>orange/blue tint + contours = terraform raise/lower; scale bar 2×10 m</span></p>");
            var json = new List<object>();
            foreach (var group in jobs.Select((j, i) => (j, r: results[i])).GroupBy(x => x.j.Type))
            {
                sb.Append($"<h2 id=\"{group.Key}\">{group.Key}</h2><div class=\"grid\">");
                foreach (var (j, r) in group)
                {
                    sb.Append($"<div class=\"card\"><a href=\"{r.File}\"><img loading=\"lazy\" src=\"{r.File}\" alt=\"{j.Type} {j.Variant}\"></a><b>{j.Title}</b> <span class=\"m\">({j.Variant})</span><div class=\"m\">");
                    if (r.Failure != null) sb.Append($"<span class=\"bad\">FAILED: {Esc(r.Failure)}</span>");
                    else
                    {
                        sb.Append($"{r.Objects} objects · {r.Nets} nets / {r.PathMetres:0} m · {r.Surfaces} surfaces · {r.Ms:0} ms<br>");
                        sb.Append(string.Join(", ", r.ByRole.OrderByDescending(k => k.Value).Select(k => $"{k.Key} {k.Value}")) + "<br>");
                        var flags = new List<string>();
                        if (r.BenchesOffPath > 0) flags.Add($"{r.BenchesOffPath} benches off-path");
                        if (r.Overlaps > 0) flags.Add($"{r.Overlaps} overlapping objects");
                        if (r.Outside > 0) flags.Add($"{r.Outside} outside area");
                        if (r.Errors > 0) flags.Add($"{r.Errors} errors");
                        if (flags.Count > 0) sb.Append($"<span class=\"bad\">{string.Join(" · ", flags)}</span><br>");
                        if (r.IssueKeys.Count > 0) sb.Append(Esc(string.Join(", ", r.IssueKeys.Distinct().Take(6))));
                    }
                    sb.Append("</div></div>");
                    json.Add(new
                    {
                        type = j.Type, variant = j.Variant, file = r.File, ms = Math.Round(r.Ms), failure = r.Failure, objects = r.Objects, nets = r.Nets,
                        pathMetres = Math.Round(r.PathMetres), surfaces = r.Surfaces, errors = r.Errors, warnings = r.Warnings,
                        benchesOffPath = r.BenchesOffPath, overlaps = r.Overlaps, outside = r.Outside, decals = r.Decals, byRole = r.ByRole, issues = r.IssueKeys.Distinct().ToList(),
                    });
                }
                sb.Append("</div>");
            }
            sb.Append("</body></html>");
            File.WriteAllText(Path.Combine(outDir, "index.html"), sb.ToString());
            File.WriteAllText(Path.Combine(outDir, "gallery.json"), JsonSerializer.Serialize(json, new JsonSerializerOptions { WriteIndented = true }));
        }

        private static string Esc(string s) => System.Net.WebUtility.HtmlEncode(s ?? "");
    }
}
