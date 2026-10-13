// Fake asset lists for the mock (what C# AssetCatalogSystem sends per category): object thumbnails,
// texture-like swatches for surfaces (tagged surface + surface.<kind>), themed decals ("theme:<name>"),
// "auto:<category>" tags on the automatic pool, and one modded surface without a thumbnail.
import { AssetItem } from "../../src/contract";

const PALETTE: Record<string, string> = {
    tree: "#4f9a4c", "net.pathway": "#c7a35a", "net.road": "#6d7a86", centerpiece: "#4fb8e8", bench: "#a0754b", fence: "#8a8f96",
    playground: "#e2774f",
};

const NAMES: Record<string, string[]> = {
    tree: ["English Oak", "Silver Birch", "Linden", "Red Maple", "Scots Pine", "Japanese Cherry", "Horse Chestnut", "London Plane", "Weeping Willow", "Ginkgo"],
    "net.pathway": ["Gravel Path", "Paved Path", "Wide Pedestrian Street", "Boardwalk", "Cobblestone Path"],
    "net.road": ["Two-Lane Road", "Two-Lane Road with Trees", "Four-Lane Road", "Small Avenue", "Gravel Road"],
    centerpiece: ["Classic Fountain", "Modern Fountain", "Founder Statue", "Obelisk", "Bronze Horse", "Abstract Sculpture"],
    bench: ["Wooden Bench", "Classic Lamp", "Modern Bench", "Square Planter", "Bin", "Bollard"],
    fence: ["Iron Fence", "Hedge", "Low Wall", "Picket Fence"],
    playground: ["Swing Set", "Slide Tower", "Sandbox", "Climbing Frame", "Seesaw"],
};

const IDS: Record<string, string[]> = { bench: ["bench-wood", "lamp-classic", "bench-modern", "planter-square", "bin", "bollard"] };

const slug = (name: string) => name.toLowerCase().replace(/[^a-z0-9]+/g, "-").replace(/^-|-$/g, "");

const svgUri = (body: string) =>
    "data:image/svg+xml;utf8," + encodeURIComponent(`<svg xmlns="http://www.w3.org/2000/svg" width="64" height="64" viewBox="0 0 64 64">${body}</svg>`);

function thumb(name: string, color: string) {
    const initials = name.split(/\s+/).slice(0, 2).map((w) => w[0]).join("");
    return svgUri(
        `<defs><linearGradient id="g" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="${color}"/><stop offset="1" stop-color="#1b262c"/></linearGradient></defs><rect width="64" height="64" rx="8" fill="url(#g)"/><text x="32" y="40" font-family="Arial" font-size="22" font-weight="bold" fill="#ffffff" fill-opacity="0.92" text-anchor="middle">${initials}</text>`,
    );
}

/** Deterministic pseudo-random sequence (same swatch every run → stable screenshots). */
function rng(seed: number) {
    let x = seed * 7919;
    return () => {
        x = (x * 9301 + 49297) % 233280;
        return x / 233280;
    };
}

/** Texture-like swatches: grass blades, paving joints, asphalt grain, natural grit. */
function swatch(kind: string, base: string, ink: string, seed: number) {
    const r = rng(seed);
    let body = `<rect width="64" height="64" fill="${base}"/>`;
    if (kind === "grass") {
        for (let i = 0; i < 80; i++) {
            const x = r() * 64, y = r() * 68;
            body += `<path d="M${x.toFixed(1)} ${y.toFixed(1)}l${(r() * 3 - 1.5).toFixed(1)} -${(3 + r() * 4).toFixed(1)}" stroke="${ink}" stroke-width="1.2" stroke-linecap="round"/>`;
        }
    } else if (kind === "paving") {
        const w = seed % 2 ? 16 : 13, h = seed % 2 ? 8 : 13;
        for (let y = 0, row = 0; y < 64; y += h, row++)
            for (let x = -w; x < 64; x += w) body += `<rect x="${x + (row % 2 ? w / 2 : 0)}" y="${y}" width="${w}" height="${h}" fill="none" stroke="${ink}" stroke-width="1.3"/>`;
    } else if (kind === "asphalt") {
        for (let i = 0; i < 170; i++)
            body += `<rect x="${(r() * 64).toFixed(1)}" y="${(r() * 64).toFixed(1)}" width="1.4" height="1.4" fill="${ink}" fill-opacity="${(0.3 + r() * 0.6).toFixed(2)}"/>`;
    } else {
        for (let i = 0; i < 90; i++)
            body += `<circle cx="${(r() * 64).toFixed(1)}" cy="${(r() * 64).toFixed(1)}" r="${(0.6 + r() * 1.8).toFixed(1)}" fill="${ink}" fill-opacity="${(0.35 + r() * 0.5).toFixed(2)}"/>`;
    }
    return svgUri(body);
}

/** Surfaces by kind: [name, base colour, ink colour]. */
const SURFACES: Record<string, [string, string, string][]> = {
    grass: [["Lawn", "#5e9e4a", "#7fc266"], ["Meadow Grass", "#6f9a3e", "#a5c45a"], ["Dry Grass", "#a39a55", "#c9bf74"], ["Clover", "#4f8f4f", "#87c67a"]],
    paving: [["Light Pavers", "#c9bda8", "#9d917c"], ["Herringbone Brick", "#a65f45", "#7a3f2c"], ["Granite Slabs", "#8f9396", "#6c7073"], ["Terracotta Tiles", "#c0784f", "#94553a"]],
    natural: [["Sand", "#d8c08a", "#b39a62"], ["Gravel", "#9b958a", "#6e695f"], ["Forest Floor", "#6a5236", "#3f6b33"], ["Packed Dirt", "#8a6a48", "#5f4630"]],
    asphalt: [["Asphalt", "#3d4247", "#8a9096"], ["Concrete", "#9da3a6", "#c3c8ca"], ["Worn Asphalt", "#55595c", "#a4a39a"]],
};

const MODDED_SURFACE = { id: "mod-mossy-cobbles", name: "Mossy Cobbles (mod)", color: "#6f7a5c" };

function fakeSurfaces(): AssetItem[] {
    const out: AssetItem[] = [];
    let seed = 1;
    for (const kind of Object.keys(SURFACES)) {
        SURFACES[kind].forEach(([name, base, ink], i) => {
            const cat = "surface." + kind;
            out.push({
                id: slug(name),
                name,
                icon: swatch(kind, base, ink, seed++),
                cost: 2 + i,
                tags: ["surface", cat, "source:vanilla", ...(i < 3 ? ["auto", "auto:surface", "auto:" + cat] : [])],
            });
        });
    }
    // A modded surface without a thumbnail: the UI must show initials, never an empty box.
    out.push({ id: MODDED_SURFACE.id, name: MODDED_SURFACE.name, icon: "", cost: 4, tags: ["surface", "surface.paving", "source:mod"] });
    return out;
}

/** Decals with themes (tagged "theme:<name>"): the picker shows the themes as tabs. */
const DECALS: Record<string, [string, string][]> = {
    leaves: [["Autumn Leaves", "#d08a3a"], ["Fallen Leaves", "#b5652d"], ["Pine Needles", "#6d7b3a"]],
    cracks: [["Hairline Cracks", "#2d3236"], ["Patched Cracks", "#4b5054"]],
    puddles: [["Puddle", "#5c8db0"], ["Wet Patch", "#46627a"]],
    markings: [["Hopscotch", "#f2f2f2"], ["Chalk Drawing", "#f5a6c9"], ["Arrow", "#f2d64b"]],
    paving: [["Mosaic Rosette", "#c99a5b"], ["Compass Inlay", "#b8a17a"]],
};

function decalThumb(theme: string, color: string, seed: number) {
    const r = rng(seed);
    let body = `<rect width="64" height="64" fill="#7d7a73"/>`;
    if (theme === "leaves")
        for (let i = 0; i < 14; i++)
            body += `<ellipse cx="${(r() * 64).toFixed(1)}" cy="${(r() * 64).toFixed(1)}" rx="4.5" ry="2.2" fill="${color}" transform="rotate(${Math.round(r() * 180)} 32 32)"/>`;
    else if (theme === "cracks") body += `<path d="M4 30 L18 26 L26 36 L40 28 L52 38 L62 34 M26 36 L30 52 M40 28 L44 12" stroke="${color}" stroke-width="1.8" fill="none"/>`;
    else if (theme === "puddles") body += `<ellipse cx="30" cy="34" rx="22" ry="12" fill="${color}" fill-opacity="0.85"/><ellipse cx="46" cy="20" rx="8" ry="4" fill="${color}" fill-opacity="0.6"/>`;
    else if (theme === "markings") body += `<path d="M14 50 L32 14 L50 50 Z" stroke="${color}" stroke-width="3" fill="none"/>`;
    else body += `<circle cx="32" cy="32" r="22" fill="none" stroke="${color}" stroke-width="4"/><circle cx="32" cy="32" r="10" fill="${color}"/>`;
    return svgUri(body);
}

function fakeDecals(): AssetItem[] {
    const out: AssetItem[] = [];
    let seed = 40;
    for (const theme of Object.keys(DECALS))
        DECALS[theme].forEach(([name, color], i) =>
            out.push({
                id: "decal-" + slug(name),
                name,
                icon: decalThumb(theme, color, seed++),
                tags: ["decal", "theme:" + theme, ...(i === 0 ? ["auto", "auto:decal"] : [])],
            }),
        );
    return out;
}

/** Base colour of a fake surface id (the mock map paints the real-preview ghost with it). */
export function surfaceColor(id: string): string | null {
    for (const kind of Object.keys(SURFACES)) for (const [name, base] of SURFACES[kind]) if (slug(name) === id) return base;
    return id === MODDED_SURFACE.id ? MODDED_SURFACE.color : null;
}

/** First automatic surface of a kind ("" category → any). */
export function defaultSurface(category: string): string {
    const all = fakeSurfaces();
    return all.find((a) => (a.tags ?? []).includes("auto:" + category))?.id ?? all[0].id;
}

export function fakeAssets(category: string): AssetItem[] {
    if (category === "surface" || category.startsWith("surface.")) {
        const all = fakeSurfaces();
        return category === "surface" ? all : all.filter((a) => (a.tags ?? []).includes(category));
    }
    if (category === "decal") return fakeDecals();
    const names = NAMES[category] ?? ["Sample A", "Sample B", "Sample C"];
    const color = PALETTE[category] ?? "#557";
    return names.map((n, i) => ({
        id: IDS[category]?.[i] ?? slug(n),
        name: n,
        icon: thumb(n, color),
        cost: 50 + ((i * 137) % 400),
        tags: [category, ...(i < 3 ? ["auto", "auto:" + category] : [])],
    }));
}
