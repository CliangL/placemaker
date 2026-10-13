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
import { FAKE_LAYERS, FAKE_RECIPES, FAKE_STRINGS, fakeAssets, MockLayer, MockRecipe, RecipeTemplate } from "./fakeCatalog";
import { composeFake, FAKE_LAYER_IDS } from "./fakePlanner";
import { area, closestOnRing, dist, perimeter, selfIntersects, simplify, V2 } from "./geometry";
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
            const real: MockLayer[] = (cat?.layers ?? []).map((l: MockLayer) => ({ ...l, real: true }));
            for (const l of real) {
                this.hostLayers.add(l.id);
                const i = this.layers.findIndex((x) => x.id === l.id);
                if (i >= 0) this.layers[i] = l;
                else this.layers.push(l);
            }
            for (const r of cat?.recipes ?? []) {
                if (!this.recipes.some((x) => x.info.id === r.id))
                    this.recipes.push({ info: { id: r.id, name: r.name, icon: r.icon, custom: false }, root: { layer: r.root?.layer ?? r.id } });
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
        for (const c of this.layer(layerId)?.controls ?? []) if (c.default !== undefined && c.default !== null && c.kind !== "markers") out[c.id] = clone(c.default);
        return out;
    }

    instantiate(t: RecipeTemplate): StepModel {
        return {
            layer: t.layer,
            target: t.target ?? "",
            enabled: true,
            values: { ...this.defaults(t.layer), ...(t.values ?? {}) },
            locked: [],
            seed: Math.floor(Math.random() * 1e9),
            markers: [],
            children: (t.children ?? []).map((c) => this.instantiate(c)),
            overrides: {},
        };
    }

    toTemplate(s: StepModel): RecipeTemplate {
        return { layer: s.layer, target: s.target, values: clone(s.values), children: s.children.map((c) => this.toTemplate(c)) };
    }

    get(path: string): StepModel | null {
        if (!this.root) return null;
        const tokens = path.split("/");
        const [first, firstKey] = tokens[0].split("@");
        if (first !== "0") return null;
        let node: StepModel | undefined = this.root;
        if (firstKey) node = node.overrides[firstKey];
        for (let i = 1; i < tokens.length && node; i++) {
            const [idx, key]: string[] = tokens[i].split("@");
            const next: StepModel | undefined = node.children[Number(idx)];
            node = next && key ? next.overrides[key] : next;
        }
        return node ?? null;
    }

    parentPath(path: string): string | null {
        const at = path.lastIndexOf("@");
        const slash = path.lastIndexOf("/");
        if (at > slash) return path.slice(0, at);
        if (slash < 0) return null;
        return path.slice(0, slash);
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
        if (selfIntersects(this.ring)) return { key: "Placemaker.UI.Problem.SelfIntersect" };
        if (area(this.ring) < 150) return { key: "Placemaker.UI.Problem.TooSmall", args: [150] };
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
            this.edit({ key: "Placemaker.UI.History.CloseArea" }, () => (this.closed = true));
            return;
        }
        this.edit({ key: "Placemaker.UI.History.AddCorner" }, () => this.ring.push(p));
    }

    closeRing() {
        if (this.ring.length >= 3 && !this.closed) this.edit({ key: "Placemaker.UI.History.CloseArea" }, () => (this.closed = true));
    }

    removePoint(i: number) {
        if (i < 0 || i >= this.ring.length) return;
        this.edit({ key: "Placemaker.UI.History.RemoveCorner" }, () => {
            this.ring.splice(i, 1);
            if (this.ring.length < 3) this.closed = false;
        });
    }

    insertPoint(seg: number, p: V2) {
        this.edit({ key: "Placemaker.UI.History.InsertCorner" }, () => this.ring.splice(seg + 1, 0, p));
    }

    movePoint(i: number, p: V2) {
        this.ring[i] = p;
        this.changed(true);
    }

    pickBlock(b: Block, additive: boolean) {
        const idx = BLOCKS.indexOf(b);
        this.edit({ key: additive ? "Placemaker.UI.History.AddBlock" : "Placemaker.UI.History.PickBlock" }, () => {
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
        this.edit({ key: "Placemaker.UI.History.PickNode" }, () => this.ring.push([p[0], p[1]]));
    }

    addMarker(p: V2) {
        if (!this.markerMode) return;
        const step = this.get(this.markerMode.stepPath);
        if (!step) return;
        const q = closestOnRing(p, this.ring);
        if (q.d > 14) return;
        const kind = this.markerMode.kind;
        this.edit({ key: "Placemaker.UI.History.PlaceMarker", args: [en(`Placemaker.Marker.${kind}`, kind)] }, () => step.markers.push({ kind, p: q.q }));
    }

    removeMarkerNear(p: V2) {
        if (!this.markerMode) return;
        const step = this.get(this.markerMode.stepPath);
        if (!step) return;
        const i = step.markers.findIndex((m) => dist(m.p, p) < 10);
        if (i >= 0) this.edit({ key: "Placemaker.UI.History.RemoveMarker" }, () => step.markers.splice(i, 1));
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
        this.edit({ key: "Placemaker.UI.History.Draw" }, () => {
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
                this.edit({ key: "Placemaker.UI.History.ClearArea" }, () => {
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
                this.edit({ key: "Placemaker.UI.History.SwitchRecipe", args: [r.info.custom ? r.info.name : en(r.info.name)] }, () => {
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
                this.edit({ key: "Placemaker.UI.History.AddLayer", args: [this.layerName(a[1])] }, () => {
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
                this.edit({ key: "Placemaker.UI.History.RemoveLayer", args: [this.layerName(step.layer)] }, () => {
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
                this.edit({ key: "Placemaker.UI.History.MoveLayer" }, () => {
                    [parent.children[i], parent.children[j]] = [parent.children[j], parent.children[i]];
                    this.focus = pp + "/" + j;
                    this.modified = true;
                });
                return;
            }
            case "setStepEnabled": {
                const step = this.get(a[0]);
                if (!step) break;
                this.edit({ key: a[1] ? "Placemaker.UI.History.EnableLayer" : "Placemaker.UI.History.DisableLayer", args: [this.layerName(step.layer)] }, () => {
                    step.enabled = !!a[1];
                    this.modified = true;
                });
                return;
            }
            case "setStepTarget": {
                const step = this.get(a[0]);
                if (!step) break;
                this.edit({ key: "Placemaker.UI.History.Retarget", args: [this.layerName(step.layer)] }, () => {
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
                this.edit({ key: layerId ? "Placemaker.UI.History.Override" : "Placemaker.UI.History.ClearOverride", args: [key, layerId ? this.layerName(layerId) : ""] }, () => {
                    if (!layerId) delete parent.overrides[key];
                    else {
                        const child = parent.children.find((c) => c.target && (c.target === key || key.startsWith(c.target + ":")));
                        parent.overrides[key] = this.instantiate({ layer: layerId, target: child?.target ?? "" });
                        this.focus = a[0] + "@" + key;
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
                    { key: "Placemaker.UI.History.ChangeValue", args: [en(label, a[1])] },
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
                this.edit({ key: "Placemaker.UI.History.ResetLayer", args: [this.layerName(step.layer)] }, () => {
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
                this.edit({ key: "Placemaker.UI.History.Preset", args: [en(preset.name, preset.id)] }, () => {
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
            case "clearMarkers": {
                const step = this.get(a[0]);
                if (!step) break;
                this.edit({ key: "Placemaker.UI.History.ClearMarkers" }, () => (step.markers = step.markers.filter((m) => m.kind !== a[1])));
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
                this.toast = "Camera moved to the issue";
                this.locateAt = json(a[0]) ?? null;
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
        this.edit({ key: "Placemaker.UI.History.Randomize" }, () => {
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
                res = { ready: false, path, next: r.nextStep, kind: this.layer(s.layer)?.requires };
                return;
            }
            s.children.forEach((c, i) => visit(c, path + "/" + i));
        };
        if (this.root) visit(this.root, "0");
        return res;
    }

    stepReady(s: StepModel, path: string): { ready: boolean; nextStep?: LocText } {
        const l = this.layer(s.layer);
        if (l?.real) return this.readiness.get(path) ?? { ready: true };
        if (l?.requires && !s.markers.some((m) => m.kind === l.requires)) return { ready: false, nextStep: { key: "Placemaker.Parking.NeedsEntrance" } };
        return { ready: true };
    }

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
        switch (this.stage) {
            case "area":
                if (this.areaValid()) return { action: "toDesign", label: { key: "Placemaker.UI.Primary.ToDesign" }, enabled: true };
                return {
                    action: "toDesign",
                    label: { key: "Placemaker.UI.Primary.ToDesign" },
                    enabled: false,
                    reason: this.problem() ?? (this.ring.length === 0 ? { key: "Placemaker.UI.Reason.NoArea" } : { key: "Placemaker.UI.Reason.NotClosed" }),
                };
            case "design": {
                if (!this.root) return { action: "toReview", label: { key: "Placemaker.UI.Primary.ToReview" }, enabled: false, reason: { key: "Placemaker.UI.Reason.NoRecipe" } };
                const r = this.allReady();
                if (!r.ready && r.kind) {
                    return { action: "placeMarkers", label: { key: "Placemaker.UI.Primary.PlaceMarkers", args: [en(`Placemaker.Marker.${r.kind}.Plural`, r.kind + "s")] }, enabled: true };
                }
                if (!r.ready) return { action: "toReview", label: { key: "Placemaker.UI.Primary.ToReview" }, enabled: false, reason: r.next };
                return { action: "toReview", label: { key: "Placemaker.UI.Primary.ToReview" }, enabled: true };
            }
            case "review": {
                const editing = this.editingGroup != null;
                const base = editing
                    ? { action: "finishEdit" as const, label: { key: "Placemaker.UI.Primary.FinishEdit" } }
                    : { action: "build" as const, label: { key: "Placemaker.UI.Primary.Build", args: [this.recipeDisplayName().toLowerCase()] } };
                if (!summary.ready || summary.stale) return { ...base, enabled: false, reason: { key: "Placemaker.UI.Reason.Planning" } };
                if (!this.allReady().ready) return { ...base, enabled: false, reason: { key: "Placemaker.UI.Reason.NotReady" } };
                if (summary.issues.some((i) => i.severity === "error")) return { ...base, enabled: false, reason: { key: "Placemaker.UI.Reason.Errors" } };
                if (!summary.canAfford) return { ...base, enabled: false, reason: { key: "Placemaker.UI.Reason.Money" } };
                return { ...base, enabled: true };
            }
            case "building":
                return { action: "cancelBuild", label: { key: "Placemaker.UI.Primary.Cancel" }, enabled: true };
            case "manage":
                return { action: "none", label: { key: "Placemaker.UI.Primary.NewArea" }, enabled: false, reason: { key: "Placemaker.UI.Reason.Manage" } };
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
        if (this.markerMode) return [h("Placemaker.UI.Hint.PlaceMarker", ["Click"]), h("Placemaker.UI.Hint.RemoveMarker", ["Right-click"]), h("Placemaker.UI.Hint.Done", ["Esc"])];
        switch (this.stage) {
            case "area":
                switch (this.mode) {
                    case "draw":
                        return this.closed
                            ? [h("Placemaker.UI.Hint.MoveCorner", ["Drag"]), h("Placemaker.UI.Hint.InsertCorner", ["Ctrl", "Click"]), h("Placemaker.UI.Hint.RemoveCorner", ["Right-click"]), h("Placemaker.UI.Hint.Next", ["Enter"])]
                            : [h("Placemaker.UI.Hint.AddCorner", ["Click"]), h("Placemaker.UI.Hint.CloseArea", ["Click first"]), h("Placemaker.UI.Hint.RemoveCorner", ["Right-click"]), h("Placemaker.UI.Hint.Undo", ["Ctrl", "Z"])];
                    case "block":
                        return [h("Placemaker.UI.Hint.PickBlock", ["Click"]), h("Placemaker.UI.Hint.AddBlock", ["Shift", "Click"]), h("Placemaker.UI.Hint.Next", ["Enter"])];
                    case "paint":
                        return [h("Placemaker.UI.Hint.Paint", ["Left drag"]), h("Placemaker.UI.Hint.Erase", ["Right drag"]), h("Placemaker.UI.Hint.Rotate", ["Shift", "Move"]), h("Placemaker.UI.Hint.Next", ["Enter"])];
                    case "perimeter":
                        return [h("Placemaker.UI.Hint.PickNode", ["Click"]), h("Placemaker.UI.Hint.CloseArea", ["Click first"]), h("Placemaker.UI.Hint.Undo", ["Ctrl", "Z"])];
                }
                return [];
            case "design":
                return [h("Placemaker.UI.Hint.Undo", ["Ctrl", "Z"]), h("Placemaker.UI.Hint.Review", ["Enter"]), h("Placemaker.UI.Hint.Close", ["Esc"])];
            case "review":
                return [h("Placemaker.UI.Hint.Build", ["Enter"]), h("Placemaker.UI.Hint.Undo", ["Ctrl", "Z"]), h("Placemaker.UI.Hint.Close", ["Esc"])];
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
            markerMode: this.markerMode
                ? { ...this.markerMode, label: { key: "Placemaker.UI.Markers.BannerFor", args: [en(`Placemaker.Marker.${this.markerMode.kind}.Plural`, this.markerMode.kind + "s")] } }
                : undefined,
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
            layers: this.layers.map(({ minArea, requires, real, ...l }) => l) as any,
            recipes: this.recipes.map((r) => r.info),
        };
    }

    stepView(s: StepModel, path: string): StepView {
        const overrides: Record<string, StepView> = {};
        for (const k of Object.keys(s.overrides)) overrides[k] = this.stepView(s.overrides[k], path + "@" + k);
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
        const r = this.stepReady(s, path);
        const values = { ...s.values };
        for (const [k, v] of this.preview) {
            const [p, id] = k.split("|");
            if (p === path) values[id] = v;
        }
        return {
            path,
            layer: s.layer,
            target: s.target,
            enabled: s.enabled,
            values,
            locked: s.locked.slice(),
            markers: Array.from(kinds).map((k) => ({ kind: k, count: counts.get(k) ?? 0 })),
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
