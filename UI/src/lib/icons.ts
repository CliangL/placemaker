// Bundled line icons (src/assets/icons/*.svg, white strokes, tinted with a CSS mask) plus resolution
// of icon strings coming from C# (layer/recipe/option icons): a coui:// or Media/ path is used as is,
// a short name ("park", "fountain") maps to a bundled icon, anything unknown gets a neutral glyph.

declare const require: any;

const ctx = require.context("../assets/icons", false, /\.svg$/);
const BUNDLED: Record<string, string> = {};
ctx.keys().forEach((k: string) => {
    const name = k.replace(/^\.\//, "").replace(/\.svg$/, "");
    const mod = ctx(k);
    BUNDLED[name] = typeof mod === "string" ? mod : mod?.default ?? "";
});

const ALIASES: Record<string, string> = {
    "roads-with-parks": "roadsParks",
    roadswithparks: "roadsParks",
    "roads+parks": "roadsParks",
    "park-plaza": "parkPlaza",
    "park|plaza": "parkPlaza",
    parkplazasplit: "parkPlaza",
    camp: "campground",
    camping: "campground",
    toilets: "restrooms",
    restroom: "restrooms",
    trees: "tree",
    "big-trees": "bigTrees",
    bigtrees: "bigTrees",
    "edge-zoning": "edgeZoning",
    edgezoning: "edgeZoning",
    zoning: "edgeZoning",
    stalls: "car",
    entrances: "entrance",
    benches: "bench",
    lamps: "lamp",
    paths: "path",
    pathways: "path",
    roadlength: "ruler",
    pathlength: "path",
    length: "ruler",
    centerpiece: "fountain",
    statue: "fountain",
};

export function bundledIcon(name: string): string | undefined {
    if (!name) return undefined;
    if (BUNDLED[name]) return BUNDLED[name];
    const lower = name.toLowerCase();
    const alias = ALIASES[lower];
    if (alias && BUNDLED[alias]) return BUNDLED[alias];
    const hit = Object.keys(BUNDLED).find((k) => k.toLowerCase() === lower);
    return hit ? BUNDLED[hit] : undefined;
}

export interface ResolvedIcon {
    src: string;
    /** Bundled monochrome icon → tint with currentColor via mask. External icons keep their colours. */
    tint: boolean;
}

export function resolveIcon(name: string | undefined, fallback = "layers"): ResolvedIcon {
    if (name) {
        if (/^(coui:|https?:|data:|assetdb:|thumbnail:)/i.test(name)) return { src: name, tint: false };
        if (name.startsWith("Media/")) return { src: "coui://GameUI/" + name, tint: false };
        const b = bundledIcon(name);
        if (b) return { src: b, tint: true };
    }
    return { src: BUNDLED[fallback] ?? "", tint: true };
}

export const icon = (name: string) => BUNDLED[name] ?? "";

/** Stat key → icon ("Placemaker.Stat.Trees" → tree). */
export function statIcon(key: string): string {
    const last = (key.split(".").pop() ?? "").toLowerCase();
    const map: Record<string, string> = {
        trees: "tree", tree: "tree", bushes: "leaf", benches: "bench", bench: "bench", lamps: "lamp",
        stalls: "car", parking: "car", spaces: "car", roadlength: "ruler", roads: "roads", pathlength: "path",
        paths: "path", area: "area", greenarea: "leaf", pavedarea: "plaza", blocks: "block", entrances: "entrance",
        playgrounds: "playground", fountains: "fountain", restrooms: "restrooms", zoning: "zone", zonecells: "zone",
        capacity: "people", visitors: "people", tents: "campground", water: "water",
    };
    return map[last] ?? (bundledIcon(last) ? last : "info");
}

/** Role colour of a layer (matches the overlay: paths yellow, roads cyan, green spaces green, parking blue…). */
export function layerColor(id: string, kind?: string): string {
    const k = (id || "").toLowerCase();
    if (k.includes("parking")) return "#5f9dff";
    if (k.includes("plaza")) return "#e2b47e";
    if (k.includes("road") || k.includes("street")) return "#8fe3ff";
    if (k.includes("path")) return "#f5c542";
    if (k.includes("zon")) return "#b48cff";
    if (k.includes("water") || k.includes("lake") || k.includes("fountain")) return "#4fb8e8";
    if (k.includes("split")) return "#c9d3da";
    if (k.includes("park") || k.includes("tree") || k.includes("camp") || k.includes("green") || k.includes("garden")) return "#6cc56a";
    if (kind === "addOn") return "#ffa94d";
    if (kind === "split") return "#c9d3da";
    return "#8fd1ff";
}
