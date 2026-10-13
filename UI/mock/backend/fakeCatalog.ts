// Fallback catalog for the mock: plausible layers covering every control kind, used whenever the
// mock host has no real layer with that id (other workstreams write the real ones in parallel).
// Shapes follow contract.ts plus the proposed `default` on controls and preset `values`.
import { AssetItem, Control, LayerType, RecipeInfo, SettingValue } from "../../src/contract";

export type MockControl = Control & { default?: SettingValue };
export type MockLayer = Omit<LayerType, "controls" | "presets"> & {
    controls: MockControl[];
    presets: { id: string; name: string; icon?: string; values?: Record<string, SettingValue> }[];
    minArea?: number;
    /** Marker kinds that must have at least one marker before Build. */
    requires?: string;
    real?: boolean;
};

const opt = (value: string, label: string, icon?: string, tooltip?: string) => ({ value, label, icon, tooltip });

export const FAKE_LAYERS: MockLayer[] = [
    {
        id: "park", name: "Placemaker.Park.Name", icon: "park", kind: "fill", accepts: [], emits: ["green", "core"],
        controls: [
            { id: "style", kind: "choice", tier: "essential", group: "", label: "Placemaker.Park.Style", tooltip: "Placemaker.Park.StyleTip",
              options: [opt("formal", "Placemaker.Park.Style.Formal", "plaza"), opt("natural", "Placemaker.Park.Style.Natural", "leaf"), opt("meadow", "Placemaker.Park.Style.Meadow", "tree")], default: "natural" },
            { id: "pathWidth", kind: "slider", tier: "essential", group: "", label: "Placemaker.Park.PathWidth", min: 2, max: 8, step: 0.5, unit: "m", default: 4, cheap: false },
            { id: "trees", kind: "slider", tier: "essential", group: "", label: "Placemaker.Park.TreeDensity", tooltip: "Placemaker.Park.TreeDensityTip", min: 0, max: 100, step: 5, unit: "%", default: 60, cheap: true },
            { id: "furniture", kind: "toggle", tier: "essential", group: "", label: "Placemaker.Park.Furniture", default: true, cheap: true },
            { id: "pathAsset", kind: "asset", tier: "more", group: "paths", label: "Placemaker.Park.PathAsset", assetCategory: "net.pathway", default: "" },
            { id: "treeSpecies", kind: "assetMulti", tier: "more", group: "planting", label: "Placemaker.Park.TreeSpecies", assetCategory: "tree", default: [] },
            { id: "entrances", kind: "markers", tier: "more", group: "paths", label: "Placemaker.Park.Entrances", markerKinds: ["entrance"] },
            { id: "lake", kind: "toggle", tier: "more", group: "water", label: "Placemaker.Park.Lake", tooltip: "Placemaker.Park.LakeTip", default: false },
            { id: "lakeSize", kind: "slider", tier: "more", group: "water", label: "Placemaker.Park.LakeSize", min: 10, max: 40, step: 5, unit: "%", default: 20, visibleWhen: "lake=true" },
            { id: "fence", kind: "toggle", tier: "more", group: "edge", label: "Placemaker.Park.Fence", default: false },
            { id: "fenceAsset", kind: "asset", tier: "more", group: "edge", label: "Placemaker.Park.FenceAsset", assetCategory: "fence", default: "", visibleWhen: "fence=true" },
        ],
        presets: [
            { id: "formal", name: "Placemaker.Park.Preset.Formal", values: { style: "formal", pathWidth: 5, trees: 40, furniture: true } },
            { id: "wild", name: "Placemaker.Park.Preset.Wild", values: { style: "meadow", pathWidth: 2.5, trees: 85, furniture: false } },
            { id: "pond", name: "Placemaker.Park.Preset.Pond", values: { style: "natural", trees: 55, lake: true, lakeSize: 25 } },
        ],
        minArea: 400,
    },
    {
        id: "plaza", name: "Placemaker.Plaza.Name", icon: "plaza", kind: "fill", accepts: [], emits: ["paved", "core", "edge"],
        controls: [
            { id: "pattern", kind: "choice", tier: "essential", group: "", label: "Placemaker.Plaza.Pattern",
              options: [opt("grid", "Placemaker.Plaza.Pattern.Grid", "block"), opt("radial", "Placemaker.Plaza.Pattern.Radial", "fountain"), opt("open", "Placemaker.Plaza.Pattern.Open", "area")], default: "radial" },
            { id: "centerpiece", kind: "asset", tier: "essential", group: "", label: "Placemaker.Plaza.Centerpiece", assetCategory: "centerpiece", default: "" },
            { id: "furniture", kind: "slider", tier: "essential", group: "", label: "Placemaker.Plaza.Furniture", min: 0, max: 100, step: 10, unit: "%", default: 50 },
            { id: "surface", kind: "asset", tier: "more", group: "", label: "Placemaker.Plaza.Surface", assetCategory: "surface.paving", default: "" },
            { id: "arrangement", kind: "arrangement", tier: "more", group: "", label: "Placemaker.Plaza.Arrangement", assetCategory: "bench", default: ["bench-wood", "lamp-classic", "bench-wood", "planter-square"] },
            { id: "edgeTrees", kind: "toggle", tier: "more", group: "", label: "Placemaker.Plaza.EdgeTrees", default: true },
        ],
        presets: [
            { id: "market", name: "Placemaker.Plaza.Preset.Market", values: { pattern: "open", furniture: 30 } },
            { id: "civic", name: "Placemaker.Plaza.Preset.Civic", values: { pattern: "radial", furniture: 60, edgeTrees: true } },
        ],
        minArea: 200,
    },
    {
        id: "parking", name: "Placemaker.Parking.Name", icon: "parking", kind: "fill", accepts: [], emits: ["lot", "edge"],
        controls: [
            { id: "angle", kind: "choice", tier: "essential", group: "", label: "Placemaker.Parking.StallAngle",
              options: [opt("90", "Placemaker.Parking.Angle90"), opt("60", "Placemaker.Parking.Angle60"), opt("45", "Placemaker.Parking.Angle45")], default: "90" },
            { id: "entrances", kind: "markers", tier: "essential", group: "", label: "Placemaker.Parking.Entrances", markerKinds: ["entrance"] },
            { id: "surface", kind: "asset", tier: "essential", group: "", label: "Placemaker.Parking.Surface", assetCategory: "surface.asphalt", default: "" },
            { id: "greenery", kind: "toggle", tier: "essential", group: "", label: "Placemaker.Parking.Greenery", default: true },
            { id: "aisle", kind: "slider", tier: "more", group: "layout", label: "Placemaker.Parking.AisleWidth", min: 5, max: 8, step: 0.5, unit: "m", default: 6 },
            { id: "medians", kind: "toggle", tier: "more", group: "layout", label: "Placemaker.Parking.Medians", default: false },
            { id: "lanterns", kind: "toggle", tier: "more", group: "extras", label: "Placemaker.Parking.Lanterns", default: true },
            { id: "chargers", kind: "slider", tier: "more", group: "extras", label: "Placemaker.Parking.Chargers", min: 0, max: 20, step: 1, unit: "count", default: 0 },
            { id: "fee", kind: "slider", tier: "more", group: "extras", label: "Placemaker.Parking.Fee", min: 0, max: 20, step: 1, unit: "money", default: 5 },
        ],
        presets: [],
        minArea: 600,
        requires: "entrance",
    },
    {
        id: "roads", name: "Placemaker.Roads.Name", icon: "roads", kind: "fill", accepts: [], emits: ["block", "edge"],
        controls: [
            { id: "pattern", kind: "choice", tier: "essential", group: "", label: "Placemaker.Roads.Pattern",
              options: [opt("grid", "Placemaker.Roads.Pattern.Grid", "roads"), opt("loop", "Placemaker.Roads.Pattern.Loop", "perimeter"), opt("organic", "Placemaker.Roads.Pattern.Organic", "path")], default: "grid" },
            { id: "blockSize", kind: "slider", tier: "essential", group: "", label: "Placemaker.Roads.BlockSize", min: 50, max: 200, step: 5, unit: "m", default: 90 },
            { id: "street", kind: "asset", tier: "essential", group: "", label: "Placemaker.Roads.Street", assetCategory: "net.road", default: "" },
            { id: "angle", kind: "slider", tier: "more", group: "", label: "Placemaker.Roads.Angle", min: -45, max: 45, step: 1, unit: "deg", default: 0 },
            { id: "perimeter", kind: "toggle", tier: "more", group: "", label: "Placemaker.Roads.Perimeter", default: true },
            { id: "culDeSacs", kind: "toggle", tier: "more", group: "", label: "Placemaker.Roads.CulDeSacs", default: false },
        ],
        presets: [
            { id: "dense", name: "Placemaker.Roads.Preset.Dense", values: { blockSize: 70 } },
            { id: "suburb", name: "Placemaker.Roads.Preset.Suburb", values: { blockSize: 140, culDeSacs: true } },
        ],
        minArea: 4000,
    },
    {
        id: "split", name: "Placemaker.Split.Name", icon: "split", kind: "split", accepts: [], emits: ["part"],
        controls: [
            { id: "ratio", kind: "slider", tier: "essential", group: "", label: "Placemaker.Split.Ratio", min: 20, max: 80, step: 5, unit: "%", default: 50 },
            { id: "direction", kind: "choice", tier: "essential", group: "", label: "Placemaker.Split.Direction",
              options: [opt("vertical", "Placemaker.Split.Vertical"), opt("horizontal", "Placemaker.Split.Horizontal")], default: "vertical" },
            { id: "divider", kind: "toggle", tier: "more", group: "", label: "Placemaker.Split.Divider", default: true },
        ],
        presets: [],
    },
    {
        id: "campground", name: "Placemaker.Campground.Name", icon: "campground", kind: "fill", accepts: [], emits: ["green"],
        controls: [
            { id: "pitches", kind: "slider", tier: "essential", group: "", label: "Placemaker.Campground.Pitches", min: 4, max: 40, step: 1, unit: "count", default: 14 },
            { id: "campfires", kind: "toggle", tier: "essential", group: "", label: "Placemaker.Campground.Campfires", default: true },
        ],
        presets: [],
        minArea: 1500,
    },
    {
        id: "playground", name: "Placemaker.Playground.Name", icon: "playground", kind: "addOn", accepts: ["green", "core", "root", "block", "part", "paved"], emits: [],
        controls: [
            { id: "size", kind: "choice", tier: "essential", group: "", label: "Placemaker.Playground.Size",
              options: [opt("s", "Placemaker.Size.Small"), opt("m", "Placemaker.Size.Medium"), opt("l", "Placemaker.Size.Large")], default: "m" },
            { id: "equipment", kind: "assetMulti", tier: "more", group: "", label: "Placemaker.Playground.Equipment", assetCategory: "playground", default: [] },
        ],
        presets: [],
    },
    {
        id: "fountain", name: "Placemaker.Fountain.Name", icon: "fountain", kind: "addOn", accepts: ["paved", "core", "green"], emits: [],
        controls: [{ id: "asset", kind: "asset", tier: "essential", group: "", label: "Placemaker.Fountain.Asset", assetCategory: "centerpiece", default: "" }],
        presets: [],
    },
    {
        id: "restrooms", name: "Placemaker.Restrooms.Name", icon: "restrooms", kind: "addOn", accepts: ["green", "paved", "core"], emits: [],
        controls: [{ id: "count", kind: "slider", tier: "essential", group: "", label: "Placemaker.Restrooms.Count", min: 1, max: 4, step: 1, unit: "count", default: 1 }],
        presets: [],
    },
    {
        id: "bigTrees", name: "Placemaker.BigTrees.Name", icon: "bigTrees", kind: "addOn", accepts: ["green", "edge", "root", "block", "core"], emits: [],
        controls: [
            { id: "count", kind: "slider", tier: "essential", group: "", label: "Placemaker.BigTrees.Count", min: 1, max: 30, step: 1, unit: "count", default: 8 },
            { id: "species", kind: "assetMulti", tier: "essential", group: "", label: "Placemaker.BigTrees.Species", assetCategory: "tree", default: [] },
        ],
        presets: [],
    },
    {
        id: "edgeZoning", name: "Placemaker.EdgeZoning.Name", icon: "edgeZoning", kind: "addOn", accepts: ["edge", "block", "root", "lot", "paved"], emits: [],
        controls: [
            { id: "zone", kind: "choice", tier: "essential", group: "", label: "Placemaker.EdgeZoning.Zone",
              options: [opt("resLow", "Placemaker.Zone.ResidentialLow"), opt("resHigh", "Placemaker.Zone.ResidentialHigh"), opt("commercial", "Placemaker.Zone.Commercial"), opt("mixed", "Placemaker.Zone.Mixed"), opt("office", "Placemaker.Zone.Office"), opt("industry", "Placemaker.Zone.Industry")], default: "mixed" },
            { id: "depth", kind: "slider", tier: "essential", group: "", label: "Placemaker.EdgeZoning.Depth", min: 1, max: 6, step: 1, unit: "count", default: 4 },
        ],
        presets: [],
    },
    {
        id: "picnic", name: "Placemaker.Picnic.Name", icon: "picnic", kind: "addOn", accepts: ["green"], emits: [],
        controls: [{ id: "tables", kind: "slider", tier: "essential", group: "", label: "Placemaker.Picnic.Tables", min: 2, max: 16, step: 1, unit: "count", default: 6 }],
        presets: [],
    },
];

export interface MockRecipe {
    info: RecipeInfo;
    root: RecipeTemplate;
}

export interface RecipeTemplate {
    layer: string;
    target?: string;
    values?: Record<string, SettingValue>;
    children?: RecipeTemplate[];
}

export const FAKE_RECIPES: MockRecipe[] = [
    { info: { id: "parking", name: "Placemaker.Parking.Name", icon: "parking", custom: false, description: "Placemaker.Recipe.ParkingTip" }, root: { layer: "parking" } },
    { info: { id: "plaza", name: "Placemaker.Plaza.Name", icon: "plaza", custom: false, description: "Placemaker.Recipe.PlazaTip" }, root: { layer: "plaza" } },
    { info: { id: "park", name: "Placemaker.Park.Name", icon: "park", custom: false, description: "Placemaker.Recipe.ParkTip" }, root: { layer: "park" } },
    { info: { id: "roads", name: "Placemaker.Roads.Name", icon: "roads", custom: false, description: "Placemaker.Recipe.RoadsTip" }, root: { layer: "roads" } },
    {
        info: { id: "roads-with-parks", name: "Placemaker.Recipe.RoadsParks", icon: "roadsParks", custom: false, description: "Placemaker.Recipe.RoadsParksTip" },
        root: { layer: "roads", children: [{ layer: "park", target: "block" }] },
    },
    {
        info: { id: "park-plaza", name: "Placemaker.Recipe.ParkPlaza", icon: "parkPlaza", custom: false, description: "Placemaker.Recipe.ParkPlazaTip" },
        root: { layer: "split", children: [{ layer: "park", target: "part:0" }, { layer: "plaza", target: "part:1" }] },
    },
    {
        info: { id: "neighbourhood-park", name: "Placemaker.Recipe.NeighbourhoodPark", icon: "playground", custom: false, description: "Placemaker.Recipe.NeighbourhoodParkTip" },
        root: { layer: "park", children: [{ layer: "playground" }, { layer: "restrooms" }] },
    },
    { info: { id: "campground", name: "Placemaker.Campground.Name", icon: "campground", custom: false, description: "Placemaker.Recipe.CampgroundTip" }, root: { layer: "campground" } },
];

/** English for the fake layers' keys (the real layers ship their own Lang files). */
export const FAKE_STRINGS: Record<string, string> = {
    "Placemaker.Park.Name": "Park", "Placemaker.Park.Style": "Style", "Placemaker.Park.StyleTip": "Overall look: formal lines, natural curves or an open meadow.",
    "Placemaker.Park.Style.Formal": "Formal", "Placemaker.Park.Style.Natural": "Natural", "Placemaker.Park.Style.Meadow": "Meadow",
    "Placemaker.Park.PathWidth": "Path width", "Placemaker.Park.TreeDensity": "Trees", "Placemaker.Park.TreeDensityTip": "How much of the lawn is planted with trees.",
    "Placemaker.Park.Furniture": "Benches & lamps", "Placemaker.Park.PathAsset": "Path type", "Placemaker.Park.TreeSpecies": "Tree species",
    "Placemaker.Park.Entrances": "Entrances", "Placemaker.Park.Lake": "Pond", "Placemaker.Park.LakeTip": "Digs a pond. Terrain changes are permanent.",
    "Placemaker.Park.LakeSize": "Pond size", "Placemaker.Park.Fence": "Fence", "Placemaker.Park.FenceAsset": "Fence type",
    "Placemaker.Park.Preset.Formal": "Formal garden", "Placemaker.Park.Preset.Wild": "Wild meadow", "Placemaker.Park.Preset.Pond": "Pond park",
    "Placemaker.Plaza.Name": "Plaza", "Placemaker.Plaza.Pattern": "Layout", "Placemaker.Plaza.Pattern.Grid": "Grid", "Placemaker.Plaza.Pattern.Radial": "Radial",
    "Placemaker.Plaza.Pattern.Open": "Open", "Placemaker.Plaza.Centerpiece": "Centerpiece", "Placemaker.Plaza.Furniture": "Furniture",
    "Placemaker.Plaza.Surface": "Paving", "Placemaker.Plaza.Arrangement": "Edge sequence", "Placemaker.Plaza.EdgeTrees": "Trees along the edge",
    "Placemaker.Plaza.Preset.Market": "Market square", "Placemaker.Plaza.Preset.Civic": "Civic square",
    "Placemaker.Parking.Name": "Parking", "Placemaker.Parking.StallAngle": "Stall angle", "Placemaker.Parking.Angle90": "90°", "Placemaker.Parking.Angle60": "60°",
    "Placemaker.Parking.Angle45": "45°", "Placemaker.Parking.Entrances": "Entrances", "Placemaker.Parking.Surface": "Surface", "Placemaker.Parking.Greenery": "Greenery",
    "Placemaker.Parking.AisleWidth": "Aisle width", "Placemaker.Parking.Medians": "Planted medians", "Placemaker.Parking.Lanterns": "Lanterns",
    "Placemaker.Parking.Chargers": "EV chargers", "Placemaker.Parking.Fee": "Parking fee", "Placemaker.Parking.NeedsEntrance": "Place at least one entrance on the outline, where cars come in.",
    "Placemaker.Parking.TooSmall": "Too small for a parking lot (at least 600 m²).",
    "Placemaker.Roads.Name": "Roads", "Placemaker.Roads.Pattern": "Pattern", "Placemaker.Roads.Pattern.Grid": "Grid", "Placemaker.Roads.Pattern.Loop": "Loops",
    "Placemaker.Roads.Pattern.Organic": "Organic", "Placemaker.Roads.BlockSize": "Block size", "Placemaker.Roads.Street": "Street type", "Placemaker.Roads.Angle": "Rotation",
    "Placemaker.Roads.Perimeter": "Road along the edge", "Placemaker.Roads.CulDeSacs": "Cul-de-sacs", "Placemaker.Roads.Preset.Dense": "Dense grid", "Placemaker.Roads.Preset.Suburb": "Suburb",
    "Placemaker.Split.Name": "Split", "Placemaker.Split.Ratio": "First part", "Placemaker.Split.Direction": "Direction", "Placemaker.Split.Vertical": "Side by side",
    "Placemaker.Split.Horizontal": "Stacked", "Placemaker.Split.Divider": "Path between parts",
    "Placemaker.Campground.Name": "Campground", "Placemaker.Campground.Pitches": "Pitches", "Placemaker.Campground.Campfires": "Campfires",
    "Placemaker.Playground.Name": "Playground", "Placemaker.Playground.Size": "Size", "Placemaker.Playground.Equipment": "Equipment",
    "Placemaker.Size.Small": "Small", "Placemaker.Size.Medium": "Medium", "Placemaker.Size.Large": "Large",
    "Placemaker.Fountain.Name": "Fountain", "Placemaker.Fountain.Asset": "Fountain",
    "Placemaker.Restrooms.Name": "Restrooms", "Placemaker.Restrooms.Count": "How many",
    "Placemaker.BigTrees.Name": "Big trees", "Placemaker.BigTrees.Count": "How many", "Placemaker.BigTrees.Species": "Species",
    "Placemaker.EdgeZoning.Name": "Edge zoning", "Placemaker.EdgeZoning.Zone": "Zone", "Placemaker.EdgeZoning.Depth": "Depth (cells)",
    "Placemaker.Zone.ResidentialLow": "Low res.", "Placemaker.Zone.ResidentialHigh": "High res.", "Placemaker.Zone.Commercial": "Commercial",
    "Placemaker.Zone.Mixed": "Mixed", "Placemaker.Zone.Office": "Office", "Placemaker.Zone.Industry": "Industry",
    "Placemaker.Picnic.Name": "Picnic area", "Placemaker.Picnic.Tables": "Tables",
    "Placemaker.Recipe.RoadsParks": "Roads + parks", "Placemaker.Recipe.ParkPlaza": "Park | plaza", "Placemaker.Recipe.NeighbourhoodPark": "Family park",
    "Placemaker.Recipe.ParkingTip": "A surface parking lot with stalls, aisles and entrances.",
    "Placemaker.Recipe.PlazaTip": "A paved square with a centrepiece, seating and lights.",
    "Placemaker.Recipe.ParkTip": "Paths, lawns, trees and benches.",
    "Placemaker.Recipe.RoadsTip": "A street pattern fitted to the area.",
    "Placemaker.Recipe.RoadsParksTip": "Streets, and every block between them becomes a park.",
    "Placemaker.Recipe.ParkPlazaTip": "Half park, half plaza.",
    "Placemaker.Recipe.NeighbourhoodParkTip": "A park with a playground and restrooms.",
    "Placemaker.Recipe.CampgroundTip": "Tent pitches around a loop path.",
    "Placemaker.Stat.Trees": "trees", "Placemaker.Stat.Benches": "benches", "Placemaker.Stat.Lamps": "lamps", "Placemaker.Stat.Stalls": "stalls",
    "Placemaker.Stat.PathLength": "of paths", "Placemaker.Stat.RoadLength": "of road", "Placemaker.Stat.Blocks": "blocks", "Placemaker.Stat.Playgrounds": "playgrounds",
    "Placemaker.Stat.Fountains": "fountains", "Placemaker.Stat.Restrooms": "restrooms", "Placemaker.Stat.ZoneCells": "zone cells", "Placemaker.Stat.Tents": "tents",
    "Placemaker.Stat.Chargers": "EV chargers", "Placemaker.Stat.PavedArea": "paved",
    "Placemaker.Issue.PlazaLarge": "Large plazas feel empty. Consider Park | plaza, or more furniture.",
    "Placemaker.Issue.TreesSkipped": "{0} trees were left out where they would block a path.",
    "Placemaker.Issue.BlockTooSmall": "Block {0} is too small to fill and stays empty.",
    "Placemaker.Issue.NoRoom": "{0}: no free spot left, skipped.",
    "Placemaker.Marker.entrance.Plural": "entrances",
};

// ───────────── fake assets ─────────────

const PALETTE: Record<string, string> = {
    tree: "#4f9a4c", "net.pathway": "#c7a35a", "net.road": "#6d7a86", centerpiece: "#4fb8e8", bench: "#a0754b", fence: "#8a8f96",
    "surface.paving": "#b9a58c", "surface.asphalt": "#4a5258", playground: "#e2774f", "surface.grass": "#5ea85a",
};

const NAMES: Record<string, string[]> = {
    tree: ["English Oak", "Silver Birch", "Linden", "Red Maple", "Scots Pine", "Japanese Cherry", "Horse Chestnut", "London Plane", "Weeping Willow", "Ginkgo"],
    "net.pathway": ["Gravel Path", "Paved Path", "Wide Pedestrian Street", "Boardwalk", "Cobblestone Path"],
    "net.road": ["Two-Lane Road", "Two-Lane Road with Trees", "Four-Lane Road", "Small Avenue", "Gravel Road"],
    centerpiece: ["Classic Fountain", "Modern Fountain", "Founder Statue", "Obelisk", "Bronze Horse", "Abstract Sculpture"],
    bench: ["Wooden Bench", "Classic Lamp", "Modern Bench", "Square Planter", "Bin", "Bollard"],
    fence: ["Iron Fence", "Hedge", "Low Wall", "Picket Fence"],
    "surface.paving": ["Light Pavers", "Herringbone Brick", "Granite Slabs", "Terracotta Tiles"],
    "surface.asphalt": ["Asphalt", "Concrete", "Gravel Lot"],
    playground: ["Swing Set", "Slide Tower", "Sandbox", "Climbing Frame", "Seesaw"],
};

const IDS: Record<string, string[]> = { bench: ["bench-wood", "lamp-classic", "bench-modern", "planter-square", "bin", "bollard"] };

function thumb(name: string, color: string) {
    const initials = name.split(/\s+/).slice(0, 2).map((w) => w[0]).join("");
    const svg = `<svg xmlns="http://www.w3.org/2000/svg" width="64" height="64"><defs><linearGradient id="g" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="${color}"/><stop offset="1" stop-color="#1b262c"/></linearGradient></defs><rect width="64" height="64" rx="8" fill="url(#g)"/><text x="32" y="40" font-family="Arial" font-size="22" font-weight="bold" fill="#ffffff" fill-opacity="0.92" text-anchor="middle">${initials}</text></svg>`;
    return "data:image/svg+xml;utf8," + encodeURIComponent(svg);
}

export function fakeAssets(category: string): AssetItem[] {
    const names = NAMES[category] ?? ["Sample A", "Sample B", "Sample C"];
    const color = PALETTE[category] ?? "#557";
    return names.map((n, i) => ({
        id: IDS[category]?.[i] ?? n.toLowerCase().replace(/[^a-z0-9]+/g, "-"),
        name: n,
        icon: thumb(n, color),
        cost: 50 + ((i * 137) % 400),
        tags: [category],
    }));
}
