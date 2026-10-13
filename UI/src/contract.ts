// UI ↔ C# binding contract. Authoritative: C# (Game/UI/PlacemakerUISystem.cs + UiModels.cs)
// mirrors these shapes exactly. Group: "Placemaker". All JSON values are sent via
// RawValueBinding / ValueBinding<string> (never string[] — that broke mod load in PLT).
//
// Few, structured bindings instead of hundreds: the UI is schema-driven, so a new layer type
// needs zero UI code.

export const GROUP = "Placemaker";

// ─────────────────────────── Values (C# → UI) ───────────────────────────

/** Workflow stages. The player can always click back to an earlier stage; nothing is lost. */
export type Stage = "area" | "design" | "review" | "building" | "manage";
export type AreaMode = "draw" | "block" | "paint" | "perimeter";
export type BrushShape = "round" | "square";

export interface LocText { key: string; args?: (string | number)[] }

export interface PrimaryAction {
  /** What pressing the main button (or Enter) does now. */
  action: "none" | "toDesign" | "toReview" | "build" | "placeMarkers" | "cancelBuild" | "finishEdit";
  label: LocText;
  enabled: boolean;
  /** Why disabled, shown as tooltip and inline hint. */
  reason?: LocText;
}

export interface AreaInfo {
  valid: boolean;
  closed: boolean;
  pointCount: number;
  areaM2: number;
  perimeterM: number;
  /** Problem with the current shape, e.g. self-intersection or too small for the recipe. */
  problem?: LocText;
}

export interface Hint { text: LocText; keys?: string[] }   // e.g. keys ["Ctrl","Z"]

/** state — changes often (every interaction). */
export interface ToolState {
  open: boolean;           // tool active
  stage: Stage;
  /** Stages the player may jump to right now (others are greyed). */
  reachable: Stage[];
  areaMode: AreaMode;
  brush: { shape: BrushShape; size: number; angle: number };
  area: AreaInfo;
  primary: PrimaryAction;
  canUndo: boolean;
  canRedo: boolean;
  undoLabel?: LocText;
  redoLabel?: LocText;
  /** Marker placement mode active (entrances/access points/split line) and for which step path. */
  markerMode?: { stepPath: string; kind: string; label: LocText };
  hints: Hint[];
  /** Non-null while the composer is planning in the background. */
  planning: boolean;
  /** Build progress 0..1 while stage === "building". */
  buildProgress?: number;
  /** When editing an existing build, its group id. */
  editingGroup?: number;
  /** Anarchy mod detected → show its toggle. */
  anarchy?: { available: boolean; enabled: boolean };
  panelCollapsed: boolean;
  /** "sketch" = fast overlay lines/dots; "ghost" = real translucent game models + surfaces (Review uses ghost by default). */
  previewMode?: "sketch" | "ghost";
  /** True while the ghost is being (re)built after a change. */
  ghostPending?: boolean;
}

/** One control, mirrors Core.ControlSchema. */
export interface Control {
  id: string;
  kind: "slider" | "toggle" | "choice" | "asset" | "assetMulti" | "markers" | "arrangement";
  tier: "essential" | "more";
  group: string;
  label: string;            // localisation key
  tooltip?: string;         // localisation key
  icon?: string;
  min?: number; max?: number; step?: number; unit?: string;
  options?: { value: string; label: string; icon?: string; tooltip?: string }[];
  assetCategory?: string;
  markerKinds?: string[];
  visibleWhen?: string;     // "otherId=value"
  cheap?: boolean;
  /** Default value (for per-control reset). */
  default?: SettingValue;
}

export interface LayerType {
  id: string;
  name: string;             // localisation key
  icon: string;
  kind: "fill" | "split" | "addOn";
  accepts: string[];        // region tags it can run in ([] = any)
  emits: string[];          // region tags it produces
  controls: Control[];
  presets: { id: string; name: string; icon?: string }[];
  /** Not offered as its own card in the Make row (split, pond): used by recipes / features only. */
  hidden?: boolean;
}

export interface RecipeInfo {
  id: string;
  name: string;             // localisation key, or literal when custom === true
  icon: string;
  custom: boolean;
  /** Short description key for the Make row tooltip. */
  description?: string;
}

/** catalog — changes rarely (load, mods/assets change). */
export interface Catalog {
  layers: LayerType[];
  recipes: RecipeInfo[];    // built-in "types" (park, plaza, parking, roads, roads+parks…) + player recipes
}

export type SettingValue = number | boolean | string | string[];

/** A recipe step as the UI sees it (mirrors Core.RecipeStep, paths address steps: "0", "0/1", "0/1@block:3"). */
/** A built-in feature of a fill layer, shown as a toggle (mirrors Core.FeatureInfo). */
export interface FeatureView {
  layer: string;            // feature layer id
  label: string;            // localisation key
  icon: string;
  enabled: boolean;
  /** Path of the child step when enabled (its controls are shown under the toggle, collapsed by default). */
  path?: string;
  /** Tooltip key (explanation lives here, labels stay short). */
  tooltip?: string;
  /** Ids of the PARENT step's controls that belong to this feature (e.g. park plaza shape / position / size):
   *  render them under the toggle while it is on, and hide them from the parent's own groups. */
  options?: string[];
  /** On by default for new designs. */
  defaultOn?: boolean;
}

export interface StepView {
  /** Built-in features this (fill) step offers. */
  features?: FeatureView[];
  path: string;
  layer: string;
  target: string;           // "" = same region; tag; or region key
  enabled: boolean;
  values: Record<string, SettingValue>;
  locked: string[];         // ids set explicitly by the player
  markers: { kind: string; count: number }[];
  /** Every placed marker in placement order (index = removeMarker index). */
  markerList?: { kind: string; index: number; position: [number, number, number] }[];
  /** Marker kinds this step accepts, in display order (e.g. entrance, entry, exit, alley, footpath). */
  markerKinds?: { kind: string; label: string; icon?: string; tooltip?: string }[];
  /** Minimum markers needed before Build (0 = optional). */
  markersRequired?: number;
  /** Readiness from IAreaGenerator.CheckReady for this step. */
  ready: boolean;
  nextStep?: LocText;
  children: StepView[];
  /** region key → override step */
  overrides: Record<string, StepView>;
  /** Regions this step produced in the latest plan (for "then fill blocks with…" and per-region overrides). */
  regions: { key: string; tags: string[]; areaM2: number }[];
}

/** design — the recipe being edited. */
export interface DesignState {
  recipeId: string;         // which Make entry is selected
  modified: boolean;        // differs from the recipe's shipped/saved version
  root: StepView | null;
  /** Step currently focused in the panel (its controls are shown). */
  focus: string;
}

export interface Stat { key: string; value: number; unit: string }
export interface Issue { severity: "info" | "warning" | "error"; text: LocText; stepPath?: string; at?: [number, number, number] }

/** summary — the review card. Always reflects the latest finished plan. */
export interface Summary {
  ready: boolean;           // a plan exists for the current area + design
  stale: boolean;           // inputs changed, new plan pending
  stats: Stat[];            // merged across layers ("trees", "stalls", "roadLength"…)
  cost: number;             // one-off construction cost (money)
  upkeep: number;           // per month
  canAfford: boolean;
  /** Per-layer breakdown for the review stage. */
  layers: { stepPath: string; layer: string; regionKey: string; stats: Stat[]; cost: number; upkeep: number }[];
  issues: Issue[];
  /** Permanent effects the player must acknowledge (e.g. lake excavation). */
  permanent: LocText[];
  /** True once the player ticked the permanent-effects box for exactly these effects. */
  permanentAcknowledged?: boolean;
}

export interface AssetItem {
  id: string;               // prefab name
  name: string;             // localised display name (already translated by C#)
  icon: string;             // thumbnail url or ""
  tags?: string[];
  cost?: number;
}

/** assets — category → items; filled on demand via requestAssets. */
export type AssetLists = Record<string, AssetItem[]>;

/** manage — a built group under the cursor/selected (stage "manage"), or null. */
export interface ManagedGroup {
  id: number;
  recipeName: LocText;
  customName?: string;
  stats: Stat[];
  upkeep: number;
  canEdit: boolean;
}

export const Values = {
  state: "state",           // ToolState
  catalog: "catalog",       // Catalog
  design: "design",         // DesignState
  summary: "summary",       // Summary
  assets: "assets",         // AssetLists
  managed: "managed",       // ManagedGroup | null
} as const;

// ─────────────────────────── Triggers (UI → C#) ───────────────────────────
// Arguments are primitives; structured args are JSON strings.

export const Triggers = {
  toggleTool: "toggleTool",                 // ()
  close: "close",                           // ()
  setPanelCollapsed: "setPanelCollapsed",   // (collapsed: boolean)

  goToStage: "goToStage",                   // (stage: Stage) — only if in state.reachable
  primary: "primary",                       // () — same as Enter
  undo: "undo",                             // ()
  redo: "redo",                             // ()

  setAreaMode: "setAreaMode",               // (mode: AreaMode)
  setBrush: "setBrush",                     // (shape: BrushShape, size: number, angle: number)
  clearArea: "clearArea",                   // ()

  selectRecipe: "selectRecipe",             // (recipeId: string)
  focusStep: "focusStep",                   // (path: string)
  addLayer: "addLayer",                     // (parentPath: string, layerId: string, target: string)
  removeStep: "removeStep",                 // (path: string)
  moveStep: "moveStep",                     // (path: string, delta: number)
  setStepEnabled: "setStepEnabled",         // (path: string, enabled: boolean)
  setStepTarget: "setStepTarget",           // (path: string, target: string)
  overrideRegion: "overrideRegion",         // (parentPath: string, regionKey: string, layerId: string | "")  "" removes override

  setValue: "setValue",                     // (path: string, controlId: string, valueJson: string) — commit (undoable)
  previewValue: "previewValue",             // (path: string, controlId: string, valueJson: string) — live while dragging
  endPreview: "endPreview",                 // ()
  resetStep: "resetStep",                   // (path: string)
  applyPreset: "applyPreset",               // (path: string, presetId: string)
  randomize: "randomize",                   // (path: string) — "" = whole recipe; respects locked values
  unlockValue: "unlockValue",               // (path: string, controlId: string)
  startMarkers: "startMarkers",             // (path: string, kind: string) — enter marker placement
  clearMarkers: "clearMarkers",             // (path: string, kind: string)

  saveRecipeAs: "saveRecipeAs",             // (name: string) — player recipe from current design
  deleteRecipe: "deleteRecipe",             // (recipeId: string)
  saveAsDefault: "saveAsDefault",           // (path: string) — this layer's values become defaults

  requestAssets: "requestAssets",           // (category: string)

  manageGroup: "manageGroup",               // (groupId: number)
  editGroup: "editGroup",                   // (groupId: number)
  removeGroup: "removeGroup",               // (groupId: number)
  renameGroup: "renameGroup",               // (groupId: number, name: string)
  setAnarchy: "setAnarchy",                 // (enabled: boolean)
  locate: "locate",                         // (x: number, y: number, z: number) — move camera to an issue
  acknowledgePermanent: "acknowledgePermanent", // (ack: boolean) — required before Build when summary.permanent is non-empty (Enter included)
  setMarkerKind: "setMarkerKind",           // (path: string, kind: string) — which marker type the next click places (entrance types etc.)
  removeMarker: "removeMarker",             // (path: string, index: number)
  setFeature: "setFeature",                 // (path: string, featureLayerId: string, enabled: boolean)
  setPreviewMode: "setPreviewMode",         // (mode: "sketch" | "ghost")
} as const;

// ─────────────────────────── Localisation ───────────────────────────
// All keys are registered with the game's LocalizationManager by C# (Lang/<locale>.json,
// late community locales supported). UI uses translate(key, fallback). Key prefix: "Placemaker.".
