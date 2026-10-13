// Bindings to Game/UI/PlacemakerUISystem (shapes: ../contract.ts, which is authoritative).
//
// Robustness rules:
//  * every value tolerates a missing/late C# binding (fallback = null → defaults below);
//  * values may arrive as raw JSON objects (RawValueBinding) or as JSON strings (ValueBinding<string>);
//    both are accepted and parsing is guarded — a malformed payload yields defaults, never a crash;
//  * partial payloads are merged over defaults, so a C# side that lags one field behind still renders.
import { useMemo } from "react";
import { bindValue, trigger, useValue, ValueBinding } from "cs2/api";
import {
    AssetLists, Catalog, DesignState, GROUP, ManagedGroup, Stage, StepView, Summary, ToolState, Triggers, Values,
    AreaMode, BrushShape, SettingValue,
} from "../contract";

const LOG = "[Placemaker]";

function bindRaw(name: string): ValueBinding<unknown> | null {
    try {
        return bindValue<unknown>(GROUP, name, null);
    } catch (e) {
        console.warn(LOG, "binding unavailable", name, e);
        return null;
    }
}

export const bindings = {
    state: bindRaw(Values.state),
    catalog: bindRaw(Values.catalog),
    design: bindRaw(Values.design),
    summary: bindRaw(Values.summary),
    assets: bindRaw(Values.assets),
    managed: bindRaw(Values.managed),
};

const NULL_BINDING: ValueBinding<unknown> = {
    value: null,
    subscribe: () => ({ value: null, setChangeListener: () => undefined, dispose: () => undefined }),
    dispose: () => undefined,
};

export function parseJson(raw: unknown): unknown {
    if (typeof raw !== "string") return raw;
    if (raw === "") return null;
    try {
        return JSON.parse(raw);
    } catch (e) {
        console.warn(LOG, "bad JSON from C#", e);
        return null;
    }
}

const isObj = (v: unknown): v is Record<string, any> => v != null && typeof v === "object" && !Array.isArray(v);
function arr<T>(v: unknown): T[] {
    return Array.isArray(v) ? (v as T[]) : [];
}

// ───────────── defaults & normalisers ─────────────

export const DEFAULT_STATE: ToolState = {
    open: false,
    stage: "area",
    reachable: ["area"],
    areaMode: "draw",
    brush: { shape: "round", size: 40, angle: 0 },
    area: { valid: false, closed: false, pointCount: 0, areaM2: 0, perimeterM: 0 },
    primary: { action: "none", label: { key: "Placemaker.UI.Primary.SelectArea" }, enabled: false },
    canUndo: false,
    canRedo: false,
    hints: [],
    planning: false,
    panelCollapsed: false,
};

export function normalizeState(raw: unknown): ToolState {
    const v = parseJson(raw);
    if (!isObj(v)) return DEFAULT_STATE;
    return {
        ...DEFAULT_STATE,
        ...v,
        reachable: arr<Stage>(v.reachable).length ? arr<Stage>(v.reachable) : ["area"],
        brush: { ...DEFAULT_STATE.brush, ...(isObj(v.brush) ? v.brush : {}) },
        area: { ...DEFAULT_STATE.area, ...(isObj(v.area) ? v.area : {}) },
        primary: { ...DEFAULT_STATE.primary, ...(isObj(v.primary) ? v.primary : {}) },
        hints: arr(v.hints),
    } as ToolState;
}

export const EMPTY_CATALOG: Catalog = { layers: [], recipes: [] };

export function normalizeCatalog(raw: unknown): Catalog {
    const v = parseJson(raw);
    if (!isObj(v)) return EMPTY_CATALOG;
    return {
        layers: arr<any>(v.layers).filter(isObj).map((l) => ({
            ...l,
            accepts: arr(l.accepts),
            emits: arr(l.emits),
            controls: arr<any>(l.controls).filter(isObj).map((c) => ({ ...c, options: arr(c.options) })),
            presets: arr(l.presets),
        })),
        recipes: arr<any>(v.recipes).filter(isObj),
    } as Catalog;
}

export const EMPTY_DESIGN: DesignState = { recipeId: "", modified: false, root: null, focus: "0" };

export function normalizeDesign(raw: unknown): DesignState {
    const v = parseJson(raw);
    if (!isObj(v)) return EMPTY_DESIGN;
    return { ...EMPTY_DESIGN, ...v, root: isObj(v.root) ? normalizeStep(v.root) : null } as DesignState;
}

function normalizeStep(s: Record<string, any>): StepView {
    const overrides: Record<string, StepView> = {};
    if (isObj(s.overrides)) for (const k of Object.keys(s.overrides)) if (isObj(s.overrides[k])) overrides[k] = normalizeStep(s.overrides[k]);
    return {
        path: String(s.path ?? "0"),
        layer: String(s.layer ?? ""),
        target: String(s.target ?? ""),
        enabled: s.enabled !== false,
        values: isObj(s.values) ? s.values : {},
        locked: arr<string>(s.locked),
        markers: arr(s.markers),
        markerList: arr<any>(s.markerList)
            .filter(isObj)
            .map((m, i) => ({ kind: String(m.kind ?? ""), index: typeof m.index === "number" ? m.index : i, position: (Array.isArray(m.position) ? m.position : [0, 0, 0]) as [number, number, number] })),
        markerKinds: arr<any>(s.markerKinds)
            .map((k) => (typeof k === "string" ? { kind: k, label: "" } : k))
            .filter((k) => isObj(k) && typeof k.kind === "string"),
        markersRequired: Number(s.markersRequired) || 0,
        features: arr<any>(s.features)
            .filter((f) => isObj(f) && typeof f.layer === "string" && f.layer)
            .map((f) => ({ layer: String(f.layer), label: String(f.label ?? ""), icon: String(f.icon ?? ""), enabled: f.enabled === true, path: typeof f.path === "string" && f.path ? f.path : undefined, tooltip: typeof f.tooltip === "string" && f.tooltip ? f.tooltip : undefined, options: arr<any>(f.options).filter((o) => typeof o === "string"), defaultOn: f.defaultOn === true })),
        ready: s.ready !== false,
        nextStep: s.nextStep,
        children: arr<any>(s.children).filter(isObj).map(normalizeStep),
        overrides,
        regions: arr(s.regions),
    };
}

export const EMPTY_SUMMARY: Summary = {
    ready: false, stale: false, stats: [], cost: 0, upkeep: 0, canAfford: true, layers: [], issues: [], permanent: [],
};

export function normalizeSummary(raw: unknown): Summary {
    const v = parseJson(raw);
    if (!isObj(v)) return EMPTY_SUMMARY;
    return {
        ...EMPTY_SUMMARY,
        ...v,
        stats: arr(v.stats),
        layers: arr<any>(v.layers).filter(isObj).map((l) => ({ ...l, stats: arr(l.stats) })),
        issues: arr<any>(v.issues).filter(isObj),
        permanent: arr(v.permanent),
        cost: Number(v.cost) || 0,
        upkeep: Number(v.upkeep) || 0,
        canAfford: v.canAfford !== false,
    } as Summary;
}

export function normalizeAssets(raw: unknown): AssetLists {
    const v = parseJson(raw);
    if (!isObj(v)) return {};
    const out: AssetLists = {};
    for (const k of Object.keys(v)) out[k] = arr<any>(v[k]).filter((a) => isObj(a) && typeof a.id === "string");
    return out;
}

export function normalizeManaged(raw: unknown): ManagedGroup | null {
    const v = parseJson(raw);
    if (!isObj(v) || typeof v.id !== "number") return null;
    return {
        ...v,
        stats: arr(v.stats),
        recipeName: isObj(v.recipeName) ? v.recipeName : { key: String(v.recipeName ?? "") },
    } as ManagedGroup;
}

function useBound<T>(binding: ValueBinding<unknown> | null, normalize: (raw: unknown) => T): T {
    const raw = useValue(binding ?? NULL_BINDING);
    return useMemo(() => normalize(raw), [raw]);
}

export const useToolState = () => useBound(bindings.state, normalizeState);
export const useCatalog = () => useBound(bindings.catalog, normalizeCatalog);
export const useDesign = () => useBound(bindings.design, normalizeDesign);
export const useSummary = () => useBound(bindings.summary, normalizeSummary);
export const useAssets = () => useBound(bindings.assets, normalizeAssets);
export const useManaged = () => useBound(bindings.managed, normalizeManaged);

// ───────────── triggers ─────────────

function fire(name: string, ...args: unknown[]) {
    try {
        trigger(GROUP, name, ...args);
    } catch (e) {
        console.warn(LOG, "trigger failed", name, e);
    }
}

const json = (v: SettingValue) => JSON.stringify(v);

/** Every UI → C# call, typed after contract.ts Triggers. */
export const act = {
    toggleTool: () => fire(Triggers.toggleTool),
    close: () => fire(Triggers.close),
    setPanelCollapsed: (collapsed: boolean) => fire(Triggers.setPanelCollapsed, collapsed),
    goToStage: (stage: Stage) => fire(Triggers.goToStage, stage),
    primary: () => fire(Triggers.primary),
    undo: () => fire(Triggers.undo),
    redo: () => fire(Triggers.redo),
    setAreaMode: (mode: AreaMode) => fire(Triggers.setAreaMode, mode),
    setBrush: (shape: BrushShape, size: number, angle: number) => fire(Triggers.setBrush, shape, size, angle),
    clearArea: () => fire(Triggers.clearArea),
    selectRecipe: (id: string) => fire(Triggers.selectRecipe, id),
    focusStep: (path: string) => fire(Triggers.focusStep, path),
    addLayer: (parentPath: string, layerId: string, target: string) => fire(Triggers.addLayer, parentPath, layerId, target),
    removeStep: (path: string) => fire(Triggers.removeStep, path),
    moveStep: (path: string, delta: number) => fire(Triggers.moveStep, path, delta),
    setStepEnabled: (path: string, enabled: boolean) => fire(Triggers.setStepEnabled, path, enabled),
    setStepTarget: (path: string, target: string) => fire(Triggers.setStepTarget, path, target),
    overrideRegion: (parentPath: string, regionKey: string, layerId: string) =>
        fire(Triggers.overrideRegion, parentPath, regionKey, layerId),
    setValue: (path: string, id: string, v: SettingValue) => fire(Triggers.setValue, path, id, json(v)),
    previewValue: (path: string, id: string, v: SettingValue) => fire(Triggers.previewValue, path, id, json(v)),
    endPreview: () => fire(Triggers.endPreview),
    resetStep: (path: string) => fire(Triggers.resetStep, path),
    applyPreset: (path: string, presetId: string) => fire(Triggers.applyPreset, path, presetId),
    randomize: (path: string) => fire(Triggers.randomize, path),
    unlockValue: (path: string, id: string) => fire(Triggers.unlockValue, path, id),
    startMarkers: (path: string, kind: string) => fire(Triggers.startMarkers, path, kind),
    clearMarkers: (path: string, kind: string) => fire(Triggers.clearMarkers, path, kind),
    saveRecipeAs: (name: string) => fire(Triggers.saveRecipeAs, name),
    deleteRecipe: (id: string) => fire(Triggers.deleteRecipe, id),
    saveAsDefault: (path: string) => fire(Triggers.saveAsDefault, path),
    requestAssets: (category: string) => fire(Triggers.requestAssets, category),
    manageGroup: (id: number) => fire(Triggers.manageGroup, id),
    editGroup: (id: number) => fire(Triggers.editGroup, id),
    removeGroup: (id: number) => fire(Triggers.removeGroup, id),
    renameGroup: (id: number, name: string) => fire(Triggers.renameGroup, id, name),
    setAnarchy: (enabled: boolean) => fire(Triggers.setAnarchy, enabled),
    /** Move the camera to a world position (issue, marker). */
    locate: (at: [number, number, number]) => fire(Triggers.locate, Number(at[0]) || 0, Number(at[1]) || 0, Number(at[2]) || 0),
    setMarkerKind: (path: string, kind: string) => fire(Triggers.setMarkerKind, path, kind),
    removeMarker: (path: string, index: number) => fire(Triggers.removeMarker, path, index),
    /** Leave marker placement: in marker mode the C# primary action is "placeMarkers" (Done), which toggles it off. */
    endMarkers: () => fire(Triggers.primary),
    /** The player ticked the permanent-effects box (C# also guards Enter with it). */
    acknowledgePermanent: (ack: boolean) => fire(Triggers.acknowledgePermanent, ack),
    /** Turn a built-in feature of a fill step on/off (adds/enables its child step in C#). */
    setFeature: (path: string, featureLayerId: string, enabled: boolean) => fire(Triggers.setFeature, path, featureLayerId, enabled),
    /** Map preview: fast sketch overlay or the real translucent ghost (models + surfaces). */
    setPreviewMode: (mode: "sketch" | "ghost") => fire(Triggers.setPreviewMode, mode),
};

/** Vanilla UI sound through the game's audio binding; silently ignored where it doesn't exist. */
export function playSound(sound: string) {
    try {
        trigger("audio", "playSound", sound, 1);
    } catch {
        /* no audio binding (mock) */
    }
}
