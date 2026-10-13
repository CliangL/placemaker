// Pure helpers over the catalog/design contract: no React, unit-testable in the mock.
import { Catalog, Control, LayerType, SettingValue, StepView } from "../contract";

/** Control with the optional default value (proposed contract addition, see docs/proposals/ui.md). */
export type ControlEx = Control & { default?: SettingValue };

export function layerOf(catalog: Catalog, id: string): LayerType {
    const l = catalog.layers.find((x) => x.id === id);
    if (l) return l;
    return { id, name: id, icon: "", kind: "fill", accepts: [], emits: [], controls: [], presets: [] };
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

export function regionLabelArgs(key: string): { tag: string; index: number | null } {
    const i = key.indexOf(":");
    if (i < 0) return { tag: key, index: null };
    const n = Number(key.slice(i + 1));
    return { tag: key.slice(0, i), index: Number.isNaN(n) ? null : n + 1 };
}
