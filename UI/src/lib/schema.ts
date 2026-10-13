// Pure helpers over the catalog/design contract: no React, unit-testable in the mock.
import { Catalog, Control, LayerType, RecipeInfo, SettingValue, StepView } from "../contract";

/** Control with the optional default value (proposed contract addition, see docs/proposals/ui.md). */
export type ControlEx = Control & { default?: SettingValue };

export function layerOf(catalog: Catalog, id: string): LayerType {
    const l = catalog.layers.find((x) => x.id === id);
    if (l) return l;
    return { id, name: id, icon: "", kind: "fill", accepts: [], emits: [], controls: [], presets: [] };
}

export function recipeOf(catalog: Catalog, id: string): RecipeInfo | undefined {
    return catalog.recipes.find((r) => r.id === id);
}

/**
 * "fence=true", "mode!=auto", "style=a|b", several joined with "&" or ",".
 * Unknown ids evaluate against undefined (hidden for "=", shown for "!=").
 */
export function isVisible(visibleWhen: string | undefined, values: Record<string, SettingValue>): boolean {
    if (!visibleWhen) return true;
    const clauses = visibleWhen.split(/&&|&|,/).map((s) => s.trim()).filter(Boolean);
    return clauses.every((clause) => {
        const neg = clause.includes("!=");
        const [id, rhs = "true"] = clause.split(neg ? "!=" : "=").map((s) => s.trim());
        const v = values[id];
        const actual = Array.isArray(v) ? v.join("|") : v === undefined ? "" : String(v);
        const match = rhs.split("|").some((alt) => alt === actual || (alt === "true" && v === true) || (alt === "false" && v === false));
        return neg ? !match : match;
    });
}

export function valuesEqual(a: SettingValue | undefined, b: SettingValue | undefined): boolean {
    if (Array.isArray(a) || Array.isArray(b)) {
        if (!Array.isArray(a) || !Array.isArray(b) || a.length !== b.length) return false;
        return a.every((x, i) => x === b[i]);
    }
    if (typeof a === "number" && typeof b === "number") return Math.abs(a - b) < 1e-9;
    return a === b;
}

export function defaultFor(c: ControlEx): SettingValue {
    if (c.default !== undefined && c.default !== null) return c.default;
    switch (c.kind) {
        case "slider":
            return c.min ?? 0;
        case "toggle":
            return false;
        case "choice":
            return c.options?.[0]?.value ?? "";
        case "assetMulti":
        case "arrangement":
            return [];
        default:
            return "";
    }
}

export function valueOf(step: StepView, c: ControlEx): SettingValue {
    const v = step.values[c.id];
    return v === undefined || v === null ? defaultFor(c) : v;
}

/** Depth-first over children and region overrides. */
export function walk(step: StepView | null, fn: (s: StepView, depth: number, parent: StepView | null) => void, depth = 0, parent: StepView | null = null) {
    if (!step) return;
    fn(step, depth, parent);
    step.children.forEach((c) => walk(c, fn, depth + 1, step));
    Object.keys(step.overrides).forEach((k) => walk(step.overrides[k], fn, depth + 1, step));
}

export function findStep(root: StepView | null, path: string): StepView | null {
    let found: StepView | null = null;
    walk(root, (s) => {
        if (!found && s.path === path) found = s;
    });
    return found;
}

export function parentOf(root: StepView | null, path: string): StepView | null {
    let found: StepView | null = null;
    walk(root, (s, _d, p) => {
        if (!found && s.path === path) found = p;
    });
    return found;
}

export function isOverridePath(root: StepView | null, path: string): string | null {
    const p = parentOf(root, path);
    if (!p) return null;
    for (const k of Object.keys(p.overrides)) if (p.overrides[k].path === path) return k;
    return null;
}

/** Single layer, nothing added: the panel shows no tree chrome at all. */
export function isSimple(root: StepView | null): boolean {
    return !root || (root.children.length === 0 && Object.keys(root.overrides).length === 0);
}

const intersects = (a: string[], b: string[]) => a.some((x) => b.includes(x));

/** Tags of the region a step runs in. */
export function regionTagsOf(step: StepView): string[] {
    if (!step.target) return step.path.includes("/") ? [] : ["root"];
    const i = step.target.indexOf(":");
    return i > 0 ? [step.target.slice(0, i)] : [step.target];
}

export interface AddOption {
    layer: LayerType;
    target: string;
}

export interface AddSection {
    /** Where the new layer runs: "" = this step's own region, a tag, or an exact region key. */
    target: string;
    kind: "addOn" | "fill" | "split";
    options: AddOption[];
}

/**
 * What can be added under a step:
 *  - add-ons (kind addOn) whose accepts match what this step makes (its emitted tags or its region),
 *    running in the same region (target "");
 *  - fills for each emitted region tag ("then fill each block with…"), or for each concrete region of
 *    a split ("part:0", "part:1"), so mix-and-match recipes are built by clicking, not configuring.
 */
export function addSections(catalog: Catalog, step: StepView): AddSection[] {
    const layer = layerOf(catalog, step.layer);
    const own = [...layer.emits, ...regionTagsOf(step)];
    const sections: AddSection[] = [];

    const addOns = catalog.layers.filter((l) => l.kind === "addOn" && (l.accepts.length === 0 || intersects(l.accepts, own)));
    if (addOns.length) sections.push({ target: "", kind: "addOn", options: addOns.map((l) => ({ layer: l, target: "" })) });

    const fillsFor = (tag: string) =>
        catalog.layers.filter((l) => (l.kind === "fill" || l.kind === "split") && (l.accepts.length === 0 || l.accepts.includes(tag)));

    const targeted = new Set(step.children.map((c) => c.target));
    if (layer.kind === "split") {
        const keys = step.regions.length ? step.regions.map((r) => r.key) : layer.emits.filter((t) => t.includes(":"));
        const parts = keys.length ? keys : ["part:0", "part:1"];
        for (const key of parts) {
            if (targeted.has(key)) continue;
            const tag = key.split(":")[0];
            const fills = fillsFor(tag);
            if (fills.length) sections.push({ target: key, kind: "fill", options: fills.map((l) => ({ layer: l, target: key })) });
        }
    } else {
        for (const tag of layer.emits) {
            if (targeted.has(tag) || tag === "root") continue;
            if (!ROUTABLE_TAGS.includes(tag)) continue;
            const fills = fillsFor(tag).filter((l) => l.id !== step.layer);
            if (fills.length) sections.push({ target: tag, kind: "fill", options: fills.map((l) => ({ layer: l, target: tag })) });
        }
    }
    return sections;
}

/** Emitted tags worth offering as "fill each … with" (others are add-on vocabulary only). */
export const ROUTABLE_TAGS = ["block", "part", "lot", "green", "paved", "core", "edge"];

/** Targets a non-root step may switch between (setStepTarget). */
export function targetChoices(catalog: Catalog, parent: StepView, step: StepView): string[] {
    const pl = layerOf(catalog, parent.layer);
    const l = layerOf(catalog, step.layer);
    const out = [""];
    for (const tag of pl.emits) if (l.accepts.length === 0 || l.accepts.includes(tag)) out.push(tag);
    for (const r of parent.regions) if (pl.kind === "split") out.push(r.key);
    if (step.target && !out.includes(step.target)) out.push(step.target);
    return Array.from(new Set(out));
}

/** Regions of a parent that a targeted child fills and that can be overridden one by one. */
export function overridableRegions(parent: StepView): { key: string; tags: string[]; areaM2: number; child: StepView | null }[] {
    const targets = parent.children.filter((c) => c.target && c.enabled);
    const regionList = parent.regions.length
        ? parent.regions
        : Object.keys(parent.overrides).map((k) => ({ key: k, tags: [k.split(":")[0]], areaM2: 0 }));
    return regionList
        .map((r) => {
            const child = targets.find((c) => c.target === r.key || r.tags.includes(c.target)) ?? null;
            return { ...r, child };
        })
        .filter((r) => r.child !== null || parent.overrides[r.key] !== undefined);
}

export function regionLabelArgs(key: string): { tag: string; index: number | null } {
    const i = key.indexOf(":");
    if (i < 0) return { tag: key, index: null };
    const n = Number(key.slice(i + 1));
    return { tag: key.slice(0, i), index: Number.isNaN(n) ? null : n + 1 };
}
