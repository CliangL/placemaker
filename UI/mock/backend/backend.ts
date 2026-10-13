// Mock of Game/UI/PlacemakerUISystem + the tool's stage machine, faithful enough to click through the
// whole flow in a browser: area → design → review → build, roll back to any reachable stage, one undo
// stack for shape + design edits, previews while dragging, markers, per-region overrides, presets,
// randomize (respecting locks), player recipes, built groups with manage/edit/remove.
// It publishes exactly the six values of contract.ts and handles exactly its triggers.
import {
    AreaMode, BrushShape, Catalog, DesignState, GROUP, Hint, LocText, ManagedGroup, PrimaryAction, RecipeInfo, SettingValue, Stage, StepView,
    Summary, ToolState,
} from "../../src/contract";
import { publish, setTriggerHandler } from "../cs2/api";
import { addStrings } from "../cs2/l10n";
import { BLOCKS, blocksRing, Block, cellsOutline, NODES, paintCells } from "./city";
import { FAKE_FEATURES, FAKE_LAYERS, FAKE_RECIPES, FAKE_STRINGS, fakeAssets, FeatureDef, MockLayer, MockRecipe, RecipeTemplate } from "./fakeCatalog";
import { composeFake, FAKE_LAYER_IDS } from "./fakePlanner";
import { area, closestOnRing, dist, perimeter, selfIntersects, simplify, V2 } from "./geometry";
import { GRID } from "./city";

/** Midpoint of the outline edge closest to a street (≤ 30 m away), or null. */
function suggestEntrance(ring: V2[]): V2 | null {
    let best: { p: V2; d: number } | null = null;
    for (let i = 0; i < ring.length; i++) {
        const a = ring[i], b = ring[(i + 1) % ring.length];
        if (dist(a, b) < 12) continue;
        const m: V2 = [(a[0] + b[0]) / 2, (a[1] + b[1]) / 2];
        let d = Infinity;
        for (let k = 0; k < GRID.nx; k++) d = Math.min(d, Math.abs(m[0] - (GRID.x0 + k * GRID.dx)));
        for (let j = 0; j < GRID.nz; j++) d = Math.min(d, Math.abs(m[1] - (GRID.z0 + j * GRID.dz)));
        if (d <= 30 && (!best || d < best.d)) best = { p: m, d };
    }
    return best ? best.p : null;
}
import { ComposeResult, StepModel } from "./types";

const EN: Record<string, string> = { ...FAKE_STRINGS };
const en = (key: string, fallback?: string) => EN[key] ?? fallback ?? key.split(".").pop() ?? key;

interface Snapshot {
    ring: V2[];
    closed: boolean;
    cells: string[];
    picked: number[];
    recipeId: string;
    root: StepModel | null;
    focus: string;
    modified: boolean;
}

interface Entry {
    label: LocText;
    before: Snapshot;
    after?: Snapshot;
    coalesce?: string;
    at: number;
}

export interface BuiltGroup {
    id: number;
    ring: V2[];
    root: StepModel;
    recipeId: string;
    recipeName: LocText;
    customName: string;
    plan: ComposeResult;
    stats: { key: string; value: number; unit: string }[];
    upkeep: number;
}

type Listener = () => void;

const clone = <T,>(v: T): T => JSON.parse(JSON.stringify(v));

export class MockBackend {
    // ── tool ──
    open = true;
    stage: Stage = "area";
    mode: AreaMode = "draw";
    brush = { shape: "round" as BrushShape, size: 40, angle: 0 };
    userCollapsed: boolean | null = null;
    anarchy = { available: true, enabled: false };
    money = 250000;

    // ── area ──
    ring: V2[] = [];
    closed = false;
    cells = new Set<string>();
    picked: number[] = [];
    hoverBlock = -1;

    // ── design ──
    layers: MockLayer[] = FAKE_LAYERS.map((l) => ({ ...l }));
    recipes: MockRecipe[] = FAKE_RECIPES.slice();
    recipeId = "park";
    root: StepModel | null = null;
    focus = "0";
    modified = false;
    preview = new Map<string, SettingValue>();
    markerMode: { stepPath: string; kind: string } | null = null;
    ackPermanent = false;

    // ── plan ──
    plan: ComposeResult | null = null;
    planning = false;
    stale = false;
    private planVersion = 0;
    private planTimer: number | null = null;
    readiness = new Map<string, { ready: boolean; nextStep?: LocText }>();

    // ── build / manage ──
    buildProgress = 0;
    private buildTimer: number | null = null;
    groups: BuiltGroup[] = [];
    managedId: number | null = null;
    editingGroup: number | null = null;
    selectedGroup: number | null = null; // vanilla selection while the tool is closed
    private nextGroupId = 1;

    // ── history ──
    undoStack: Entry[] = [];
    redoStack: Entry[] = [];
    private gesture: Entry | null = null;

    // ── host ──
    hostUp = false;
    hostLayers = new Set<string>();
    hostMs = 0;
    assets: Record<string, unknown[]> = {};
    private listeners = new Set<Listener>();
    toast: string | null = null;

    constructor() {
        addStrings(EN);
        setTriggerHandler((group, name, args) => {
            if (group === GROUP) this.onTrigger(name, args);
        });
        this.root = this.instantiate(this.recipes.find((r) => r.info.id === this.recipeId)!.root);
        this.publishAll();
    }

    onChange(l: Listener) {
        this.listeners.add(l);
        return () => this.listeners.delete(l);
    }

    // ───────────── host integration ─────────────

    async connectHost() {
        try {
            const [cat, lang] = await Promise.all([fetch("/api/catalog").then((r) => r.json()), fetch("/api/lang").then((r) => r.json())]);
            this.hostUp = true;
            if (lang && typeof lang === "object") {
                Object.assign(EN, lang);
                addStrings(lang);
            }
            // Real layers replace fake ones with the same id (case-insensitive: "bigtrees" vs "bigTrees").
            const real: MockLayer[] = (cat?.layers ?? []).map((l: MockLayer) => ({ ...l, real: true }));
            for (const l of real) {
                this.hostLayers.add(l.id);
                this.layers = this.layers.filter((x) => x.id.toLowerCase() !== l.id.toLowerCase());
            }
            this.layers = [...real, ...this.layers];
            // Shipped recipes first; keep fake recipes only where no real layer covers their root.
            const shipped: MockRecipe[] = (cat?.recipes ?? []).map((r: any) => ({
                info: { id: r.id, name: r.name, icon: r.icon, custom: false, description: r.description },
                root: r.root ?? { layer: r.id },
            }));
            if (shipped.length) {
                const ids = new Set(shipped.map((r) => r.info.id));
                const fake = this.recipes.filter((r) => !ids.has(r.info.id) && !this.hostLayers.has(r.root.layer) && !r.info.custom);
                this.recipes = [...shipped, ...fake, ...this.recipes.filter((r) => r.info.custom)];
                const cur = this.recipes.find((r) => r.info.id === this.recipeId) ?? this.recipes[0];
                this.recipeId = cur.info.id;
                this.root = this.instantiate(cur.root);
                this.focus = "0";
            }
        } catch {
            this.hostUp = false;
        }
        this.publishAll();
        this.schedulePlan(0);
    }

    // ───────────── helpers ─────────────

    layer(id: string): MockLayer | undefined {
        return this.layers.find((l) => l.id === id);
    }

    defaults(layerId: string): Record<string, SettingValue> {
        const out: Record<string, SettingValue> = {};
        const l = this.layer(layerId) as (MockLayer & { defaults?: Record<string, SettingValue> }) | undefined;
        if (l?.defaults) return clone(l.defaults);
        for (const c of l?.controls ?? []) if (c.default !== undefined && c.default !== null && c.kind !== "markers") out[c.id] = clone(c.default);
        return out;
    }

    instantiate(t: RecipeTemplate): StepModel {
        const step: StepModel = {
            layer: t.layer,
            target: t.target ?? "",
            enabled: true,
            values: { ...this.defaults(t.layer), ...(t.values ?? {}) },
            locked: [],
            seed: Math.floor(Math.random() * 1e9),
            markers: [],
            children: (t.children ?? []).map((c) => this.instantiate(c)),
            overrides: Object.fromEntries(Object.entries(t.overrides ?? {}).map(([k, v]) => [k, this.instantiate(v)])),
        };
        for (const f of this.featuresOf(t.layer)) if (f.defaultOn) step.children.push(this.featureStep(f));
        return step;
    }

    /** Features a layer offers (Core IHasFeatures), limited to feature layers this catalog has. */
    featuresOf(layerId: string): FeatureDef[] {
        const defs = FAKE_FEATURES[layerId] ?? FAKE_FEATURES[layerId.toLowerCase()] ?? [];
        return defs.filter((f) => !!this.layer(f.layer));
    }

    featureStep(f: FeatureDef): StepModel {
        const c = this.instantiate({ layer: f.layer, target: f.target ?? "", values: f.preset });
        c.feature = f.layer;
        return c;
    }

    toTemplate(s: StepModel): RecipeTemplate {
        return { layer: s.layer, target: s.target, values: clone(s.values), children: s.children.map((c) => this.toTemplate(c)) };
    }

    /** Same addressing as Core/Tool/RecipePaths: "0", "0/1", "0/1@block:3" (override held by "0"), ".../0". */
    get(path: string): StepModel | null {
        if (!this.root) return null;
        const segs = path.split("/");
        if (segs[0] !== "0") return null;
        let node: StepModel | undefined = this.root;
        for (let i = 1; i < segs.length && node; i++) {
            const at = segs[i].indexOf("@");
            node = at >= 0 ? node.overrides[segs[i].slice(at + 1)] : node.children[Number(segs[i])];
        }
        return node ?? null;
    }

    /** Index of the child an override replaces (informative part of "i@key"). */
    overrideIndex(parent: StepModel, key: string): number {
        const i = parent.children.findIndex((c) => c.target && (c.target === key || key.startsWith(c.target + ":") || c.target === key.split(":")[0]));
        return i < 0 ? 0 : i;
    }

    parentPath(path: string): string | null {
        const slash = path.lastIndexOf("/");
        return slash < 0 ? null : path.slice(0, slash);
    }

    layerName(id: string) {
        const l = this.layer(id);
        return l ? en(l.name, id) : id;
    }

    recipeInfo(): RecipeInfo | undefined {
        return this.recipes.find((r) => r.info.id === this.recipeId)?.info;
    }

    recipeDisplayName() {
        const r = this.recipeInfo();
        if (!r) return this.root ? this.layerName(this.root.layer) : "";
        return r.custom ? r.name : en(r.name, r.id);
    }

    areaValid() {
        return this.closed && this.ring.length >= 3 && !this.problem();
    }

    problem(): LocText | undefined {
        if (!this.closed || this.ring.length < 3) return undefined;
        if (selfIntersects(this.ring)) return { key: "Placemaker.Problem.Crossing" };
        if (area(this.ring) < 150) return { key: "Placemaker.Problem.TooSmall", args: [150] };
        return undefined;
    }

    // ───────────── history ─────────────

    snapshot(): Snapshot {
        return {
            ring: clone(this.ring), closed: this.closed, cells: Array.from(this.cells), picked: this.picked.slice(),
            recipeId: this.recipeId, root: clone(this.root), focus: this.focus, modified: this.modified,
        };
    }

    restore(s: Snapshot) {
        this.ring = clone(s.ring);
        this.closed = s.closed;
        this.cells = new Set(s.cells);
        this.picked = s.picked.slice();
        this.recipeId = s.recipeId;
        this.root = clone(s.root);
        this.focus = s.focus;
        this.modified = s.modified;
        if (!this.areaValid() && (this.stage === "design" || this.stage === "review")) this.stage = "area";
    }

    /** Run an undoable edit. Same `coalesce` key within 1.2 s merges into one undo step (slider nudges). */
    edit(label: LocText, fn: () => void, coalesce?: string) {
        const now = Date.now();
        const last = this.undoStack[this.undoStack.length - 1];
        const merge = coalesce && last && last.coalesce === coalesce && now - last.at < 1200;
        const before = merge ? last.before : this.snapshot();
        fn();
        if (merge) {
            last.at = now;
        } else {
            this.undoStack.push({ label, before, coalesce, at: now });
            if (this.undoStack.length > 200) this.undoStack.shift();
        }
        this.redoStack = [];
        this.changed();
    }

    /** Gestures (dragging a corner, a brush stroke) record one entry at the end. */
    beginGesture(label: LocText) {
        this.gesture = { label, before: this.snapshot(), at: Date.now() };
    }

    endGesture() {
        if (!this.gesture) return;
        this.undoStack.push(this.gesture);
        this.redoStack = [];
        this.gesture = null;
        this.changed();
    }

    undo() {
        const e = this.undoStack.pop();
        if (!e) return;
        e.after = this.snapshot();
        this.restore(e.before);
        this.redoStack.push(e);
        this.changed();
    }

    redo() {
        const e = this.redoStack.pop();
        if (!e || !e.after) return;
        const before = this.snapshot();
        this.restore(e.after);
        this.undoStack.push({ ...e, before, at: 0 });
        this.changed();
    }

    // ───────────── area editing (map input; the game does this in C#) ─────────────

    addPoint(p: V2) {
        if (this.closed) return;
        if (this.ring.length >= 3 && dist(p, this.ring[0]) < 8) {
            this.edit({ key: "Placemaker.Undo.CloseArea" }, () => (this.closed = true));
            return;
        }
        this.edit({ key: "Placemaker.Undo.AddPoint" }, () => this.ring.push(p));
    }

    closeRing() {
        if (this.ring.length >= 3 && !this.closed) this.edit({ key: "Placemaker.Undo.CloseArea" }, () => (this.closed = true));
    }

    removePoint(i: number) {
        if (i < 0 || i >= this.ring.length) return;
        this.edit({ key: "Placemaker.Undo.RemovePoint" }, () => {
            this.ring.splice(i, 1);
            if (this.ring.length < 3) this.closed = false;
        });
    }

    insertPoint(seg: number, p: V2) {
        this.edit({ key: "Placemaker.Undo.InsertPoint" }, () => this.ring.splice(seg + 1, 0, p));
    }

    movePoint(i: number, p: V2) {
        this.ring[i] = p;
        this.changed(true);
    }

    pickBlock(b: Block, additive: boolean) {
        const idx = BLOCKS.indexOf(b);
        this.edit({ key: additive ? "Placemaker.Undo.AddBlock" : "Placemaker.Undo.SelectBlock" }, () => {
            this.picked = additive ? Array.from(new Set([...this.picked, idx])) : [idx];
            this.ring = blocksRing(this.picked.map((i) => BLOCKS[i]));
            this.closed = true;
        });
    }

    paintAt(p: V2, erase: boolean) {
        paintCells(this.cells, p, this.brush.size, this.brush.shape, this.brush.angle, erase);
        const outline = cellsOutline(this.cells);
        this.ring = outline.length >= 4 ? simplify(outline, 2.2) : [];
        this.closed = this.ring.length >= 3;
        this.changed(true);
    }

    pickNode(i: number) {
        const p = NODES[i];
        if (this.closed) return;
        if (this.ring.length >= 3 && dist(p, this.ring[0]) < 1) {
            this.closeRing();
            return;
        }
        this.edit({ key: "Placemaker.Undo.AddNode" }, () => this.ring.push([p[0], p[1]]));
    }

    addMarker(p: V2) {
        if (!this.markerMode) return;
        const step = this.get(this.markerMode.stepPath);
        if (!step) return;
        const q = closestOnRing(p, this.ring);
        if (q.d > 14) return;
        const kind = this.markerMode.kind;
        this.edit({ key: "Placemaker.Undo.AddMarker", args: [en(`Placemaker.Marker.${kind}`, kind)] }, () => step.markers.push({ kind, p: q.q }));
    }

    removeMarkerNear(p: V2) {
        if (!this.markerMode) return;
        const step = this.get(this.markerMode.stepPath);
        if (!step) return;
        const i = step.markers.findIndex((m) => dist(m.p, p) < 10);
        if (i >= 0) this.edit({ key: "Placemaker.Undo.RemoveMarker" }, () => step.markers.splice(i, 1));
    }

    clickGroup(id: number) {
        if (this.open) {
            this.managedId = id;
            this.stage = "manage";
        } else this.selectedGroup = id;
        this.changed();
    }

    /** Test/dev helper: set a ready-made closed area. */
    setArea(ring: V2[]) {
        this.edit({ key: "Placemaker.Undo.CloseArea" }, () => {
            this.ring = ring.map((p) => [p[0], p[1]] as V2);
            this.closed = true;
            this.picked = [];
        });
    }

    // ───────────── triggers (contract.ts Triggers) ─────────────

    onTrigger(name: string, args: unknown[]) {
        const a = args as any[];
        const json = (s: unknown) => {
            try {
                return JSON.parse(String(s));
            } catch {
                return undefined;
            }
        };
        switch (name) {
            case "toggleTool":
                this.open = !this.open;
                if (this.open) this.selectedGroup = null;
                break;
            case "close":
                this.open = false;
                this.markerMode = null;
                break;
            case "setPanelCollapsed":
                this.userCollapsed = !!a[0];
                break;
            case "goToStage":
                if (this.reachable().includes(a[0])) {
                    this.stage = a[0];
                    this.markerMode = null;
                    if (a[0] === "area") this.managedId = null;
                    this.userCollapsed = null;
                }
                break;
            case "primary":
                this.primaryAction();
                return;
            case "undo":
                this.undo();
                return;
            case "redo":
                this.redo();
                return;
            case "setAreaMode":
                this.mode = a[0];
                this.userCollapsed = null;
                break;
            case "setBrush":
                this.brush = { shape: a[0], size: Number(a[1]), angle: Number(a[2]) };
                break;
            case "clearArea":
                this.edit({ key: "Placemaker.Undo.ClearArea" }, () => {
                    this.ring = [];
                    this.closed = false;
                    this.cells.clear();
                    this.picked = [];
                });
                if (this.stage !== "area") this.stage = "area";
                return;
            case "selectRecipe": {
                const r = this.recipes.find((x) => x.info.id === a[0]);
                if (!r) break;
                this.edit({ key: "Placemaker.Undo.SelectRecipe", args: [r.info.custom ? r.info.name : en(r.info.name)] }, () => {
                    this.recipeId = r.info.id;
                    this.root = this.instantiate(r.root);
                    this.focus = "0";
                    this.modified = false;
                });
                return;
            }
            case "focusStep":
                if (this.get(a[0])) this.focus = a[0];
                break;
            case "addLayer": {
                const parent = this.get(a[0]);
                if (!parent || !this.layer(a[1])) break;
                this.edit({ key: "Placemaker.Undo.AddLayer", args: [this.layerName(a[1])] }, () => {
                    parent.children.push(this.instantiate({ layer: a[1], target: a[2] ?? "" }));
                    this.focus = a[0] + "/" + (parent.children.length - 1);
                    this.modified = true;
                });
                return;
            }
            case "removeStep": {
                const pp = this.parentPath(a[0]);
                const parent = pp ? this.get(pp) : null;
                const step = this.get(a[0]);
                if (!parent || !step) break;
                this.edit({ key: "Placemaker.Undo.RemoveLayer", args: [this.layerName(step.layer)] }, () => {
                    const i = parent.children.indexOf(step);
                    if (i >= 0) parent.children.splice(i, 1);
                    for (const k of Object.keys(parent.overrides)) if (parent.overrides[k] === step) delete parent.overrides[k];
                    this.focus = pp!;
                    this.modified = true;
                });
                return;
            }
            case "moveStep": {
                const pp = this.parentPath(a[0]);
                const parent = pp ? this.get(pp) : null;
                const step = this.get(a[0]);
                if (!parent || !step) break;
                const i = parent.children.indexOf(step);
                const j = i + Number(a[1]);
                if (i < 0 || j < 0 || j >= parent.children.length) break;
                this.edit({ key: "Placemaker.Undo.MoveLayer" }, () => {
                    [parent.children[i], parent.children[j]] = [parent.children[j], parent.children[i]];
                    this.focus = pp + "/" + j;
                    this.modified = true;
                });
                return;
            }
            case "setStepEnabled": {
                const step = this.get(a[0]);
                if (!step) break;
                this.edit({ key: a[1] ? "Placemaker.Undo.EnableLayer" : "Placemaker.Undo.DisableLayer", args: [this.layerName(step.layer)] }, () => {
                    step.enabled = !!a[1];
                    this.modified = true;
                });
                return;
            }
            case "setFeature": {
                const step = this.get(String(a[0]));
                const def = step ? this.featuresOf(step.layer).find((f) => f.layer === a[1]) : undefined;
                if (!step || !def) break;
                const on = !!a[2];
                this.edit({ key: on ? "Placemaker.Undo.EnableLayer" : "Placemaker.Undo.DisableLayer", args: [this.layerName(def.layer)] }, () => {
                    const child = step.children.find((c) => c.feature === def.layer);
                    if (child) child.enabled = on;
                    else if (on) step.children.push(this.featureStep(def));
                    this.modified = true;
                });
                return;
            }
            case "setStepTarget": {
                const step = this.get(a[0]);
                if (!step) break;
                this.edit({ key: "Placemaker.Undo.SetTarget", args: [this.layerName(step.layer)] }, () => {
                    step.target = String(a[1] ?? "");
                    this.modified = true;
                });
                return;
            }
            case "overrideRegion": {
                const parent = this.get(a[0]);
                if (!parent) break;
                const key = String(a[1]);
                const layerId = String(a[2] ?? "");
                this.edit({ key: layerId ? "Placemaker.Undo.OverrideRegion" : "Placemaker.Undo.RemoveOverride", args: [key, layerId ? this.layerName(layerId) : ""] }, () => {
                    if (!layerId) delete parent.overrides[key];
                    else {
                        const child = parent.children.find((c) => c.target && (c.target === key || key.startsWith(c.target + ":")));
                        parent.overrides[key] = this.instantiate({ layer: layerId, target: child?.target ?? "" });
                        this.focus = a[0] + "/" + this.overrideIndex(parent, key) + "@" + key;
                    }
                    this.modified = true;
                });
                return;
            }
            case "setValue": {
                const step = this.get(a[0]);
                const v = json(a[2]);
                if (!step || v === undefined) break;
                const label = this.layer(step.layer)?.controls.find((c) => c.id === a[1])?.label ?? a[1];
                this.preview.delete(a[0] + "|" + a[1]);
                this.edit(
                    { key: "Placemaker.Undo.ChangeValue", args: [en(label, a[1])] },
                    () => {
                        step.values[a[1]] = v;
                        if (!step.locked.includes(a[1])) step.locked.push(a[1]);
                        this.modified = true;
                    },
                    a[0] + "|" + a[1],
                );
                return;
            }
            case "previewValue": {
                const v = json(a[2]);
                if (v === undefined) break;
                this.preview.set(a[0] + "|" + a[1], v);
                this.changed(true);
                return;
            }
            case "endPreview":
                this.preview.clear();
                this.changed(true);
                return;
            case "resetStep": {
                const step = this.get(a[0]);
                if (!step) break;
                this.edit({ key: "Placemaker.Undo.ResetLayer", args: [this.layerName(step.layer)] }, () => {
                    step.values = this.defaults(step.layer);
                    step.locked = [];
                    this.modified = true;
                });
                return;
            }
            case "applyPreset": {
                const step = this.get(a[0]);
                const preset = step ? this.layer(step.layer)?.presets.find((p) => p.id === a[1]) : undefined;
                if (!step || !preset) break;
                this.edit({ key: "Placemaker.Undo.ResetLayer", args: [en(preset.name, preset.id)] }, () => {
                    step.values = { ...this.defaults(step.layer), ...(preset.values ?? {}) };
                    step.locked = [];
                    step.seed = Math.floor(Math.random() * 1e9);
                    this.modified = true;
                });
                return;
            }
            case "randomize":
                void this.randomize(String(a[0] ?? ""));
                return;
            case "unlockValue": {
                const step = this.get(a[0]);
                if (!step) break;
                step.locked = step.locked.filter((x) => x !== a[1]);
                break;
            }
            case "startMarkers":
                if (this.markerMode && this.markerMode.stepPath === a[0] && this.markerMode.kind === a[1]) this.markerMode = null;
                else this.markerMode = { stepPath: a[0], kind: a[1] };
                break;
            case "endMarkers":
                this.markerMode = null;
                break;
            case "setMarkerKind":
                // Which entrance type the next click places; also enters placement for that step.
                if (this.get(a[0])) this.markerMode = { stepPath: a[0], kind: String(a[1]) };
                break;
            case "removeMarker": {
                const step = this.get(a[0]);
                const i = Number(a[1]);
                if (!step || !(i >= 0 && i < step.markers.length)) break;
                const kind = step.markers[i].kind;
                this.edit({ key: "Placemaker.Undo.RemoveMarker", args: [en(`Placemaker.Marker.${kind}`, kind)] }, () => step.markers.splice(i, 1));
                return;
            }
            case "clearMarkers": {
                const step = this.get(a[0]);
                if (!step) break;
                this.edit({ key: "Placemaker.Undo.ClearMarkers" }, () => (step.markers = step.markers.filter((m) => m.kind !== a[1])));
                return;
            }
            case "saveRecipeAs": {
                if (!this.root) break;
                const id = "custom-" + Date.now().toString(36);
                const name = String(a[0]);
                this.recipes.push({ info: { id, name, icon: this.layer(this.root.layer)?.icon ?? "custom", custom: true }, root: this.toTemplate(this.root) });
                this.recipeId = id;
                this.modified = false;
                this.toast = `Saved “${name}”`;
                break;
            }
            case "deleteRecipe": {
                this.recipes = this.recipes.filter((r) => r.info.id !== a[0] || !r.info.custom);
                if (this.recipeId === a[0]) {
                    this.recipeId = "";
                    this.modified = true;
                }
                break;
            }
            case "saveAsDefault":
                this.toast = "Saved as default";
                break;
            case "requestAssets":
                window.setTimeout(() => {
                    this.assets[a[0]] = fakeAssets(String(a[0]));
                    this.publishAll();
                }, 120);
                return;
            case "manageGroup":
                this.clickGroup(Number(a[0]));
                return;
            case "editGroup": {
                const g = this.groups.find((x) => x.id === Number(a[0]));
                if (!g) break;
                this.open = true;
                this.selectedGroup = null;
                this.ring = clone(g.ring);
                this.closed = true;
                this.root = clone(g.root);
                this.recipeId = g.recipeId;
                this.focus = "0";
                this.editingGroup = g.id;
                this.managedId = null;
                this.stage = "design";
                this.undoStack = [];
                this.redoStack = [];
                this.changed();
                return;
            }
            case "removeGroup":
                this.groups = this.groups.filter((g) => g.id !== Number(a[0]));
                if (this.managedId === Number(a[0])) {
                    this.managedId = null;
                    if (this.stage === "manage") this.stage = "area";
                }
                if (this.selectedGroup === Number(a[0])) this.selectedGroup = null;
                break;
            case "renameGroup": {
                const g = this.groups.find((x) => x.id === Number(a[0]));
                if (g) g.customName = String(a[1] ?? "");
                break;
            }
            case "setAnarchy":
                this.anarchy.enabled = !!a[0];
                break;
            case "acknowledgePermanent":
                this.ackPermanent = !!a[0];
                break;
            case "locate":
                // contract: locate(x, y, z)
                this.toast = "Camera moved there";
                this.locateAt = [Number(a[0]) || 0, Number(a[1]) || 0, Number(a[2]) || 0];
                break;
            default:
                console.warn("[mock] unknown trigger", name, args);
        }
        this.publishAll();
    }

    locateAt: [number, number, number] | null = null;

    async randomize(path: string) {
        const steps: StepModel[] = [];
        const walk = (s: StepModel) => {
            steps.push(s);
            s.children.forEach(walk);
            Object.values(s.overrides).forEach(walk);
        };
        if (path === "") {
            if (this.root) walk(this.root);
        } else {
            const s = this.get(path);
            if (s) steps.push(s);
        }
        const results = new Map<StepModel, Record<string, SettingValue>>();
        for (const s of steps) {
            const seed = Math.floor(Math.random() * 1e9);
            let values: Record<string, SettingValue> | null = null;
            if (this.hostLayers.has(s.layer)) {
                try {
                    values = await fetch("/api/randomize", {
                        method: "POST",
                        headers: { "Content-Type": "application/json" },
                        body: JSON.stringify({ layer: s.layer, values: s.values, locked: s.locked, seed }),
                    }).then((r) => r.json());
                } catch {
                    values = null;
                }
            }
            if (!values || (values as any).error) values = this.fakeRandomize(s);
            results.set(s, values);
        }
        this.edit({ key: "Placemaker.Undo.Randomize" }, () => {
            for (const [s, v] of results) {
                s.values = { ...s.values, ...v };
                for (const id of s.locked) if (id in s.values) s.values[id] = s.values[id];
                s.seed = Math.floor(Math.random() * 1e9);
            }
            this.modified = true;
        });
    }

    fakeRandomize(s: StepModel): Record<string, SettingValue> {
        const out: Record<string, SettingValue> = { ...s.values };
        for (const c of this.layer(s.layer)?.controls ?? []) {
            if (s.locked.includes(c.id)) continue;
            if (c.kind === "slider") {
                const st = c.step || 1;
                const n = Math.round(((c.max ?? 1) - (c.min ?? 0)) / st);
                out[c.id] = Number(((c.min ?? 0) + Math.floor(Math.random() * (n + 1)) * st).toFixed(4));
            } else if (c.kind === "choice" && c.options?.length) out[c.id] = c.options[Math.floor(Math.random() * c.options.length)].value;
            else if (c.kind === "toggle" && c.tier === "essential") out[c.id] = Math.random() < 0.6;
        }
        return out;
    }

    // ───────────── stage machine ─────────────

    allReady(): { ready: boolean; path?: string; next?: LocText; kind?: string } {
        let res: { ready: boolean; path?: string; next?: LocText; kind?: string } = { ready: true };
        const visit = (s: StepModel, path: string) => {
            if (!res.ready || !s.enabled) return;
            const r = this.stepReady(s, path);
            if (!r.ready) {
                res = { ready: false, path, next: r.nextStep, kind: this.markerKindsOf(s)?.[0]?.kind };
                return;
            }
            s.children.forEach((c, i) => visit(c, path + "/" + i));
        };
        if (this.root) visit(this.root, "0");
        return res;
    }

    stepReady(s: StepModel, path: string): { ready: boolean; nextStep?: LocText } {
        const l = this.layer(s.layer);
        const need = this.markersRequiredOf(s);
        if (need > 0 && s.markers.length < need) return { ready: false, nextStep: { key: "Placemaker.Parking.NeedsEntrance" } };
        if (l?.real) return this.readiness.get(path) ?? { ready: true };
        return { ready: true };
    }

    /** Typed marker kinds of a step (StepView.markerKinds): declared by the fake layer, or derived from a real layer's markers control. */
    markerKindsOf(s: StepModel): { kind: string; label: string; icon?: string }[] | undefined {
        const l = this.layer(s.layer);
        if (!l) return undefined;
        if (l.markerKinds) return l.markerKinds;
        if (l.real && l.id.toLowerCase().includes("parking")) {
            const c = l.controls.find((x) => x.kind === "markers" && x.markerKinds && x.markerKinds.length);
            if (c) return (c.markerKinds ?? []).map((k) => ({ kind: k, label: "" }));
        }
        return undefined;
    }

    markersRequiredOf(s: StepModel): number {
        const l = this.layer(s.layer);
        if (l?.markersRequired !== undefined) return l.markersRequired;
        return this.markerKindsOf(s) ? 1 : 0;
    }

    /**
     * Like C#: when a step with typed entrances has none yet, suggest real (removable) entrances on the
     * outline edges that run next to a street. Once per outline, so removing them all sticks.
     */
    autoMarkers() {
        if (!this.closed || this.ring.length < 3) return;
        const key = JSON.stringify(this.ring);
        for (const st of this.allSteps()) {
            if (!st.enabled || !this.markerKindsOf(st) || st.markers.length || this.autoFor.get(st) === key) continue;
            this.autoFor.set(st, key);
            const best = suggestEntrance(this.ring);
            if (best) st.markers.push({ kind: this.markerKindsOf(st)![0].kind, p: best, auto: true } as any);
        }
    }

    private autoFor = new WeakMap<StepModel, string>();

    reachable(): Stage[] {
        if (this.stage === "building") return ["building"];
        const r: Stage[] = ["area"];
        if (this.areaValid()) {
            r.push("design");
            if (this.root) r.push("review");
        }
        if (this.managedId != null) r.push("manage");
        return r;
    }

    primary(): PrimaryAction {
        const summary = this.summary();
        if (this.markerMode && this.stage !== "building") return { action: "placeMarkers", label: { key: "Placemaker.Primary.DoneMarkers" }, enabled: true };
        switch (this.stage) {
            case "area":
                if (this.areaValid()) return { action: "toDesign", label: { key: "Placemaker.Primary.Next" }, enabled: true };
                return {
                    action: "toDesign",
                    label: { key: "Placemaker.Primary.Next" },
                    enabled: false,
                    reason: this.problem() ?? (this.ring.length === 0 ? { key: "Placemaker.Reason.NoArea" } : { key: "Placemaker.Reason.CloseArea" }),
                };
            case "design": {
                if (!this.root) return { action: "toReview", label: { key: "Placemaker.Primary.Review" }, enabled: false, reason: { key: "Placemaker.Reason.NoRecipe" } };
                const r = this.allReady();
                if (!r.ready && r.kind) return { action: "toReview", label: { key: "Placemaker.Primary.Review" }, enabled: false, reason: { key: "Placemaker.Reason.NeedEntrance" } };
                if (!r.ready) return { action: "toReview", label: { key: "Placemaker.Primary.Review" }, enabled: false, reason: r.next };
                return { action: "toReview", label: { key: "Placemaker.Primary.Review" }, enabled: true };
            }
            case "review": {
                const editing = this.editingGroup != null;
                const base = editing
                    ? { action: "finishEdit" as const, label: { key: "Placemaker.Primary.ApplyEdit" } }
                    : { action: "build" as const, label: { key: "Placemaker.Primary.Build", args: [this.recipeDisplayName().toLowerCase()] } };
                if (!summary.ready || summary.stale) return { ...base, enabled: false, reason: { key: "Placemaker.Reason.Planning" } };
                if (!this.allReady().ready) return { ...base, enabled: false, reason: { key: "Placemaker.Reason.StepNotReady" } };
                if (summary.issues.some((i) => i.severity === "error")) return { ...base, enabled: false, reason: { key: "Placemaker.Reason.PlanErrors" } };
                if (!summary.canAfford) return { ...base, enabled: false, reason: { key: "Placemaker.Reason.NoMoney" } };
                return { ...base, enabled: true };
            }
            case "building":
                return { action: "cancelBuild", label: { key: "Placemaker.Primary.CancelBuild" }, enabled: true };
            case "manage":
                return { action: "none", label: { key: "Placemaker.Primary.PickBuilt" }, enabled: false, reason: { key: "Placemaker.Reason.PickBuilt" } };
        }
        return { action: "none", label: { key: "" }, enabled: false };
    }

    primaryAction() {
        const p = this.primary();
        if (!p.enabled) return;
        switch (p.action) {
            case "toDesign":
                this.stage = "design";
                break;
            case "toReview":
                this.stage = "review";
                this.markerMode = null;
                break;
            case "placeMarkers": {
                if (this.markerMode) {
                    this.markerMode = null;
                    break;
                }
                const r = this.allReady();
                if (r.path && r.kind) this.markerMode = { stepPath: r.path, kind: r.kind };
                break;
            }
            case "build":
            case "finishEdit":
                if (this.plan && this.plan.permanent.length && !this.ackPermanent) return;
                this.startBuild();
                break;
            case "cancelBuild":
                if (this.buildTimer) window.clearInterval(this.buildTimer);
                this.buildTimer = null;
                this.stage = "review";
                break;
        }
        this.userCollapsed = null;
        this.publishAll();
    }

    /** Test hook: build speed (ms per 10 %). */
    buildStepMs = 90;

    startBuild() {
        this.stage = "building";
        this.buildProgress = 0;
        this.buildTimer = window.setInterval(() => {
            this.buildProgress = Math.min(1, this.buildProgress + 0.1);
            if (this.buildProgress >= 1) {
                window.clearInterval(this.buildTimer!);
                this.buildTimer = null;
                this.finishBuild();
            }
            this.publishAll();
        }, this.buildStepMs);
    }

    finishBuild() {
        const plan = this.plan!;
        const g: BuiltGroup = {
            id: this.editingGroup ?? this.nextGroupId++,
            ring: clone(this.ring),
            root: clone(this.root!),
            recipeId: this.recipeId,
            recipeName: this.recipeInfo()?.custom ? { key: "Placemaker.UI.Literal", args: [this.recipeDisplayName()] } : { key: this.recipeInfo()?.name ?? this.root!.layer },
            customName: this.groups.find((x) => x.id === this.editingGroup)?.customName ?? "",
            plan,
            stats: plan.stats,
            upkeep: plan.upkeep,
        };
        this.groups = this.groups.filter((x) => x.id !== g.id).concat([g]);
        this.money -= plan.cost;
        this.editingGroup = null;
        this.ring = [];
        this.closed = false;
        this.cells.clear();
        this.picked = [];
        for (const s of this.allSteps()) s.markers = [];
        this.stage = "area";
        this.undoStack = [];
        this.redoStack = [];
        this.ackPermanent = false;
        this.toast = "Built!";
        this.changed();
    }

    allSteps(): StepModel[] {
        const out: StepModel[] = [];
        const walk = (s: StepModel) => {
            out.push(s);
            s.children.forEach(walk);
            Object.values(s.overrides).forEach(walk);
        };
        if (this.root) walk(this.root);
        return out;
    }

    // ───────────── planning ─────────────

    changed(cheap = false) {
        this.autoMarkers();
        this.stale = true;
        this.schedulePlan(cheap ? 30 : 60);
        this.publishAll();
    }

    effectiveRoot(): StepModel | null {
        if (!this.root) return null;
        if (!this.preview.size) return this.root;
        const r = clone(this.root);
        const saved = this.root;
        this.root = r;
        for (const [k, v] of this.preview) {
            const [path, id] = k.split("|");
            const s = this.get(path);
            if (s) s.values[id] = v;
        }
        this.root = saved;
        return r;
    }

    schedulePlan(delay: number) {
        if (this.planTimer) window.clearTimeout(this.planTimer);
        // Bump now so a plan already in flight can't land after this edit (latest request wins).
        const version = ++this.planVersion;
        this.planTimer = window.setTimeout(() => void this.runPlan(version), delay);
    }

    stepToJson(s: StepModel): unknown {
        const ov: Record<string, unknown> = {};
        for (const k of Object.keys(s.overrides)) ov[k] = this.stepToJson(s.overrides[k]);
        return { layer: s.layer, target: s.target, enabled: s.enabled, values: s.values, locked: s.locked, seed: s.seed, markers: s.markers, children: s.children.map((c) => this.stepToJson(c)), overrides: ov };
    }

    async runPlan(version: number) {
        const root = this.effectiveRoot();
        if (!root || !this.areaValid()) {
            this.plan = null;
            this.planning = false;
            this.stale = false;
            this.publishAll();
            return;
        }
        this.planning = true;
        this.publishAll();
        const layersUsed = this.allSteps().map((s) => s.layer);
        const useHost = this.hostUp && layersUsed.every((l) => this.hostLayers.has(l));
        let plan: ComposeResult;
        if (useHost) {
            try {
                const t0 = performance.now();
                plan = await fetch("/api/compose", {
                    method: "POST",
                    headers: { "Content-Type": "application/json" },
                    body: JSON.stringify({ recipe: this.stepToJson(root), ring: this.ring }),
                }).then((r) => r.json());
                plan.source = "core";
                this.hostMs = performance.now() - t0;
                await this.refreshReadiness(root);
            } catch (e) {
                console.warn("[mock] host compose failed, using fake planner", e);
                plan = composeFake(root, this.ring);
            }
        } else {
            plan = composeFake(root, this.ring);
            await new Promise((r) => window.setTimeout(r, 40));
        }
        if (version !== this.planVersion) return;
        this.plan = plan;
        this.planning = false;
        this.stale = false;
        this.publishAll();
    }

    async refreshReadiness(root: StepModel) {
        const jobs: Promise<void>[] = [];
        const visit = (s: StepModel, path: string) => {
            if (this.hostLayers.has(s.layer))
                jobs.push(
                    fetch("/api/ready", {
                        method: "POST",
                        headers: { "Content-Type": "application/json" },
                        body: JSON.stringify({ layer: s.layer, values: s.values, ring: this.ring, markers: s.markers }),
                    })
                        .then((r) => r.json())
                        .then((r) => {
                            this.readiness.set(path, { ready: r.ready !== false, nextStep: r.nextStep ? { key: r.nextStep, args: r.args } : r.reason ? { key: r.reason, args: r.args } : undefined });
                        })
                        .catch(() => undefined),
                );
            s.children.forEach((c, i) => visit(c, path + "/" + i));
        };
        visit(root, "0");
        await Promise.all(jobs);
    }

    // ───────────── published values ─────────────

    collapsed(): boolean {
        if (this.userCollapsed !== null) return this.userCollapsed;
        return this.stage === "area" && this.mode === "draw" && this.ring.length > 0 && !this.closed;
    }

    hints(): Hint[] {
        const h = (key: string, keys?: string[]): Hint => ({ text: { key }, keys });
        if (this.markerMode) return [h("Placemaker.Hint.MarkerPlace", ["Click"]), h("Placemaker.Hint.MarkerRemove", ["Right-click"]), h("Placemaker.Hint.MarkerDone", ["Esc"])];
        switch (this.stage) {
            case "area":
                switch (this.mode) {
                    case "draw":
                        return this.closed
                            ? [h("Placemaker.Hint.DrawEdit", ["Drag"]), h("Placemaker.Hint.DrawInsertClick", ["Ctrl", "Click"]), h("Placemaker.Hint.DrawRemoveLast", ["Right-click"]), h("Placemaker.Hint.Primary", ["Enter"])]
                            : [h("Placemaker.Hint.DrawAdd", ["Click"]), h("Placemaker.Hint.DrawAddOrClose", ["Click first"]), h("Placemaker.Hint.DrawRemoveLast", ["Right-click"]), h("Placemaker.Hint.Undo", ["Ctrl", "Z"])];
                    case "block":
                        return [h("Placemaker.Hint.BlockPick", ["Click"]), h("Placemaker.Hint.BlockAdd", ["Shift", "Click"]), h("Placemaker.Hint.Primary", ["Enter"])];
                    case "paint":
                        return [h("Placemaker.Hint.PaintPaint", ["Left drag"]), h("Placemaker.Hint.PaintErase", ["Right drag"]), h("Placemaker.Hint.PaintRotate", ["Shift", "Move"]), h("Placemaker.Hint.Primary", ["Enter"])];
                    case "perimeter":
                        return [h("Placemaker.Hint.PerimeterAdd", ["Click"]), h("Placemaker.Hint.DrawAddOrClose", ["Click first"]), h("Placemaker.Hint.Undo", ["Ctrl", "Z"])];
                }
                return [];
            case "design":
                return [h("Placemaker.Hint.Undo", ["Ctrl", "Z"]), h("Placemaker.Hint.Primary", ["Enter"]), h("Placemaker.Hint.Close", ["Esc"])];
            case "review":
                return [h("Placemaker.Hint.Primary", ["Enter"]), h("Placemaker.Hint.Undo", ["Ctrl", "Z"]), h("Placemaker.Hint.Close", ["Esc"])];
            default:
                return [];
        }
    }

    state(): ToolState {
        const last = this.undoStack[this.undoStack.length - 1];
        const next = this.redoStack[this.redoStack.length - 1];
        return {
            open: this.open,
            stage: this.stage,
            reachable: this.reachable(),
            areaMode: this.mode,
            brush: this.brush,
            area: {
                valid: this.areaValid(),
                closed: this.closed,
                pointCount: this.ring.length,
                areaM2: this.closed && this.ring.length >= 3 ? Math.round(area(this.ring)) : 0,
                perimeterM: this.ring.length >= 2 ? Math.round(perimeter(this.ring)) : 0,
                problem: this.problem(),
            },
            primary: this.primary(),
            canUndo: this.undoStack.length > 0 && this.stage !== "building",
            canRedo: this.redoStack.length > 0 && this.stage !== "building",
            undoLabel: last?.label,
            redoLabel: next?.label,
            markerMode: this.markerMode ? { ...this.markerMode, label: { key: "Placemaker.Marker." + this.markerMode.kind } } : undefined,
            hints: this.hints(),
            planning: this.planning,
            buildProgress: this.stage === "building" ? this.buildProgress : undefined,
            editingGroup: this.editingGroup ?? undefined,
            anarchy: this.anarchy,
            panelCollapsed: this.collapsed(),
        };
    }

    catalog(): Catalog {
        return {
            layers: this.layers.map(({ minArea, markerKinds, markersRequired, real, ...l }) => l) as any,
            recipes: this.recipes.map((r) => r.info),
        };
    }

    stepView(s: StepModel, path: string): StepView {
        const overrides: Record<string, StepView> = {};
        for (const k of Object.keys(s.overrides)) overrides[k] = this.stepView(s.overrides[k], path + "/" + this.overrideIndex(s, k) + "@" + k);
        const regions: StepView["regions"] = [];
        const seen = new Set<string>();
        for (const l of this.plan?.layers ?? []) {
            if (l.stepPath.split("@")[0] !== path) continue;
            for (const r of l.regions) if (!seen.has(r.key)) {
                seen.add(r.key);
                regions.push({ key: r.key, tags: r.tags, areaM2: Math.round(r.areaM2) });
            }
        }
        const counts = new Map<string, number>();
        for (const m of s.markers) counts.set(m.kind, (counts.get(m.kind) ?? 0) + 1);
        const kinds = new Set<string>();
        for (const c of this.layer(s.layer)?.controls ?? []) if (c.kind === "markers") (c.markerKinds ?? [c.id]).forEach((k) => kinds.add(k));
        for (const k of this.markerKindsOf(s) ?? []) kinds.add(k.kind);
        const r = this.stepReady(s, path);
        const values = { ...s.values };
        for (const [k, v] of this.preview) {
            const [p, id] = k.split("|");
            if (p === path) values[id] = v;
        }
        const features = this.featuresOf(s.layer).map((f) => {
            const i = s.children.findIndex((c) => c.feature === f.layer);
            const on = i >= 0 && s.children[i].enabled;
            return { layer: f.layer, label: f.labelKey ?? "", icon: this.layer(f.layer)?.icon ?? f.layer, enabled: on, path: on ? path + "/" + i : undefined };
        });
        return {
            path,
            layer: s.layer,
            features: features.length ? features : undefined,
            target: s.target,
            enabled: s.enabled,
            values,
            locked: s.locked.slice(),
            markers: Array.from(kinds).map((k) => ({ kind: k, count: counts.get(k) ?? 0 })),
            markerList: s.markers.map((m, index) => ({ kind: m.kind, index, position: [m.p[0], 0, m.p[1]] as [number, number, number] })),
            markerKinds: this.markerKindsOf(s),
            markersRequired: this.markersRequiredOf(s),
            ready: r.ready,
            nextStep: r.nextStep,
            children: s.children.map((c, i) => this.stepView(c, path + "/" + i)),
            overrides,
            regions,
        };
    }

    design(): DesignState {
        return { recipeId: this.recipeId, modified: this.modified, root: this.root ? this.stepView(this.root, "0") : null, focus: this.focus };
    }

    summary(): Summary {
        const p = this.plan;
        if (!p) return { ready: false, stale: this.stale, stats: [], cost: 0, upkeep: 0, canAfford: true, layers: [], issues: [], permanent: [] };
        const issues = [...p.issues, ...p.layers.flatMap((l) => l.issues)];
        return {
            ready: true,
            stale: this.stale,
            stats: p.stats,
            cost: p.cost,
            upkeep: p.upkeep,
            canAfford: p.cost <= this.money,
            layers: p.perLayer,
            issues,
            permanent: p.permanent,
        };
    }

    managed(): ManagedGroup | null {
        const g = this.groups.find((x) => x.id === this.managedId);
        if (!g) return null;
        return { id: g.id, recipeName: g.recipeName, customName: g.customName || undefined, stats: g.stats, upkeep: g.upkeep, canEdit: true };
    }

    publishAll() {
        // JSON strings, as ValueBinding<string> would deliver them.
        publish(GROUP, "state", JSON.stringify(this.state()));
        publish(GROUP, "catalog", JSON.stringify(this.catalog()));
        publish(GROUP, "design", JSON.stringify(this.design()));
        publish(GROUP, "summary", JSON.stringify(this.summary()));
        publish(GROUP, "assets", JSON.stringify(this.assets));
        publish(GROUP, "managed", JSON.stringify(this.managed()));
        this.listeners.forEach((l) => l());
    }
}

export const FAKE_IDS = FAKE_LAYER_IDS;
