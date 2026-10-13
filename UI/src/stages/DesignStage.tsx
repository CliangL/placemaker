// Stage 2 — what to make, then every option of it.
//   "What to make": type cards (icon, name, one line), built-in mixes grouped under "Mixes".
//   "Places inside": reserved slot for round 3 (parametric places inside the area), hidden for now.
//   Options: one section per step of the recipe — a mix simply shows each layer's controls under its
//   own heading ("Roads", "Park in each block"). All options visible, grouped; typed entrances get their
//   own section. Playtest 1 removed the layer-tree editor and "Save as my recipe" from the UI (the
//   engine keeps both).
import React from "react";
import classNames from "classnames";
import { Catalog, DesignState, LayerType, RecipeInfo, StepView, ToolState } from "../contract";
import { act } from "../lib/bindings";
import { entityIcon, layerColor } from "../lib/icons";
import { humanize, TFunc, useT } from "../lib/l10n";
import { isOverridePath, layerOf, parentOf, regionLabelArgs, walk } from "../lib/schema";
import { ErrorBoundary } from "../lib/errorBoundary";
import { Tip } from "../lib/tips";
import { Btn, Icon, Section, Switch } from "../components/common";
import { ControlList } from "../controls/Controls";
import s from "./design.module.scss";

export const layerName = (t: TFunc, l: LayerType) => t(l.name, humanize(l.name || l.id));

export function targetLabel(t: TFunc, target: string): string {
    if (!target) return t("Placemaker.UI.Target.Same", "whole area");
    const { tag, index } = regionLabelArgs(target);
    const tagName = t("Placemaker.UI.Tag." + tag, humanize(tag).toLowerCase());
    if (index !== null) return t("Placemaker.UI.Target.One", "{0} {1}", [tagName, index]);
    return t("Placemaker.UI.Target.Each", "each {0}", [tagName]);
}

export function regionName(t: TFunc, key: string): string {
    const { tag, index } = regionLabelArgs(key);
    const tagName = t("Placemaker.UI.Tag." + tag, humanize(tag).toLowerCase());
    const cap = tagName.charAt(0).toUpperCase() + tagName.slice(1);
    return index !== null ? `${cap} ${index}` : cap;
}

/** Section heading of a step: "Roads", "Park in each block", "Plaza — Part 2", "Block 3 — Plaza". */
export function stepHeading(t: TFunc, catalog: Catalog, root: StepView | null, step: StepView): string {
    const name = layerName(t, layerOf(catalog, step.layer));
    const ov = isOverridePath(root, step.path);
    if (ov) return t("Placemaker.UI.Heading.Override", "{0} — {1}", [regionName(t, ov), name]);
    if (!step.target) return name;
    const { tag, index } = regionLabelArgs(step.target);
    if (index !== null) return t("Placemaker.UI.Heading.InOne", "{0} — {1}", [name, regionName(t, step.target)]);
    return t("Placemaker.UI.Heading.InEach", "{0} in each {1}", [name, t("Placemaker.UI.Tag." + tag, humanize(tag).toLowerCase())]);
}

// ───────────── marker kinds (entrances) ─────────────

const MARKER_FALLBACK: Record<string, [string, string, string]> = {
    entrance: ["Placemaker.UI.Marker.TwoWay", "Two-way", "twoWay"],
    twoway: ["Placemaker.UI.Marker.TwoWay", "Two-way", "twoWay"],
    entry: ["Placemaker.UI.Marker.Entry", "Entry only", "entry"],
    exit: ["Placemaker.UI.Marker.Exit", "Exit only", "exit"],
    alley: ["Placemaker.UI.Marker.Alley", "Alley", "alley"],
    footpath: ["Placemaker.UI.Marker.Footpath", "Footpath", "footpath"],
};

type MarkerKind = { kind: string; label: string; icon?: string; tooltip?: string };

export function markerLabel(t: TFunc, k: MarkerKind): string {
    const fb = MARKER_FALLBACK[k.kind.toLowerCase()];
    if (k.label) return t(k.label, fb ? fb[1] : humanize(k.label));
    return fb ? t(fb[0], fb[1]) : humanize(k.kind);
}

export function markerIcon(k: { kind: string; icon?: string }): string {
    if (k.icon) return k.icon;
    return MARKER_FALLBACK[k.kind.toLowerCase()]?.[2] ?? "marker";
}

const MARKER_TIPS: Record<string, [string, string]> = {
    entrance: ["Placemaker.UI.Marker.TwoWayTip", "Cars drive in and out here."],
    entry: ["Placemaker.UI.Marker.EntryTip", "Cars can only drive in here."],
    exit: ["Placemaker.UI.Marker.ExitTip", "Cars can only drive out here."],
    alley: ["Placemaker.UI.Marker.AlleyTip", "A small road junction through the edge of the lot."],
    footpath: ["Placemaker.UI.Marker.FootpathTip", "A pedestrian path joins the lot here."],
};

const Entrances = ({ step, state }: { step: StepView; state: ToolState }) => {
    const { t } = useT();
    const kinds = step.markerKinds ?? [];
    const list = step.markerList ?? [];
    const need = step.markersRequired ?? 0;
    const have = list.length || step.markers.reduce((n, m) => n + m.count, 0);
    const placing = state.markerMode?.stepPath === step.path ? state.markerMode.kind : null;
    const byKind = (kind: string) => kinds.find((k) => k.kind === kind) ?? { kind, label: "" };
    const choose = (kind: string) => {
        if (placing === kind) {
            act.endMarkers();
            return;
        }
        if (!placing) act.startMarkers(step.path, kind);
        act.setMarkerKind(step.path, kind);
    };
    const numbers = new Map<string, number>();
    return (
        <Section title={t("Placemaker.UI.Entrances", "Entrances")} icon="entrance" count={have} testId={`entrances-${step.path}`} className={s.entrances}>
            {need > 0 && have < need ? (
                <div className={s.required} data-testid="entrance-required">
                    <Icon name="warning" size={18} tint="warn" />
                    <div className={s.requiredText}>
                        <div className={s.requiredTitle}>
                            {need > 1
                                ? t("Placemaker.UI.Markers.NeedN", "Place at least {0} entrances", [need])
                                : t("Placemaker.UI.Markers.NeedOne", "Place at least one entrance")}
                        </div>
                        <div className={s.requiredSub}>
                            {t("Placemaker.UI.Markers.HowTo", "Choose a type below, then click the lot outline next to a road.")}
                        </div>
                    </div>
                </div>
            ) : (
                <div className={s.note}>
                    {t("Placemaker.UI.Markers.Note", "Entrances next to roads are suggested for you. Add more by type, or remove any you don't want.")}
                </div>
            )}
            <div className={s.kindRow} data-testid="marker-kinds">
                {kinds.map((k) => {
                    const on = placing === k.kind;
                    const tip = k.tooltip ? t(k.tooltip, "") : MARKER_TIPS[k.kind] ? t(MARKER_TIPS[k.kind][0], MARKER_TIPS[k.kind][1]) : "";
                    return (
                        <div key={k.kind} className={s.kindCell}>
                            <Tip title={markerLabel(t, k)} description={tip || undefined}>
                                <button
                                    type="button"
                                    data-testid={`marker-kind-${k.kind}`}
                                    aria-pressed={on ? "true" : "false"}
                                    className={classNames(s.kindBtn, { [s.kindBtnOn]: on })}
                                    onClick={(e) => {
                                        e.stopPropagation();
                                        choose(k.kind);
                                    }}
                                >
                                    <Icon name={markerIcon(k)} size={22} tint={on ? "dark" : "light"} />
                                    <div className={s.kindLabel}>{markerLabel(t, k)}</div>
                                </button>
                            </Tip>
                        </div>
                    );
                })}
            </div>
            {list.length ? (
                <div className={s.markerList} data-testid="marker-list">
                    {list.map((m) => {
                        const k = byKind(m.kind);
                        const n = (numbers.get(m.kind) ?? 0) + 1;
                        numbers.set(m.kind, n);
                        return (
                            <div key={m.index} className={s.markerItem} data-testid={`marker-${m.index}`} data-kind={m.kind}>
                                <div className={s.markerBadge}>
                                    <Icon name={markerIcon(k)} size={16} tint="warn" />
                                </div>
                                <div className={s.markerName}>{markerLabel(t, k) + " " + n}</div>
                                <div className={s.flex} />
                                <Btn
                                    icon="eye"
                                    iconSize={15}
                                    title={t("Placemaker.UI.Markers.Show", "Show on the map")}
                                    onClick={() => act.locate(m.position)}
                                    testId={`marker-show-${m.index}`}
                                />
                                <Btn
                                    icon="close"
                                    iconSize={15}
                                    title={t("Placemaker.UI.Markers.Remove", "Remove this entrance")}
                                    onClick={() => act.removeMarker(step.path, m.index)}
                                    testId={`marker-remove-${m.index}`}
                                />
                            </div>
                        );
                    })}
                </div>
            ) : null}
        </Section>
    );
};

// ───────────── type cards ─────────────

/**
 * A recipe is a "mix" when its id names more than one layer ("roads-with-parks", "split-park-plaza",
 * "park-with-playground"); a single layer or a variant of one ("parking", "parking-green") is a type.
 */
export function isMix(catalog: Catalog, r: RecipeInfo) {
    if (r.custom) return true;
    const ids = new Set(catalog.layers.map((l) => l.id.toLowerCase()));
    const named = r.id
        .toLowerCase()
        .split(/[-_+|]+/)
        .filter((tok) => ids.has(tok) || (tok.endsWith("s") && ids.has(tok.slice(0, -1))));
    return named.length > 1;
}

const TypeCard = ({ r, on }: { r: RecipeInfo; on: boolean }) => {
    const { t } = useT();
    const color = layerColor(r.id);
    const name = r.custom ? r.name : t(r.name, humanize(r.name || r.id));
    const desc = r.description ? t(r.description, "") : "";
    return (
        <div className={s.cardCell}>
            <Tip title={name} description={desc || undefined}>
                <button
                    type="button"
                    data-testid={`recipe-${r.id}`}
                    aria-pressed={on ? "true" : "false"}
                    className={classNames(s.card, { [s.cardOn]: on })}
                    onClick={(e) => {
                        e.stopPropagation();
                        if (!on) act.selectRecipe(r.id);
                    }}
                >
                    <div className={s.cardIcon} style={{ backgroundColor: color }}>
                        <Icon name={entityIcon(r.id, r.icon)} size={22} tint="dark" />
                    </div>
                    <div className={s.cardText}>
                        <div className={s.cardName}>{name}</div>
                        {desc ? <div className={s.cardDesc}>{desc}</div> : null}
                    </div>
                </button>
            </Tip>
        </div>
    );
};

const TypeCards = ({ catalog, design }: { catalog: Catalog; design: DesignState }) => {
    const { t } = useT();
    const singles = catalog.recipes.filter((r) => !isMix(catalog, r));
    const mixes = catalog.recipes.filter((r) => isMix(catalog, r));
    return (
        <div data-testid="recipes">
            <div className={s.cards}>
                {singles.map((r) => (
                    <TypeCard key={r.id} r={r} on={r.id === design.recipeId} />
                ))}
            </div>
            {mixes.length ? (
                <>
                    <div className={s.subhead} data-testid="mixes">
                        {t("Placemaker.UI.Mixes", "Mixes")}
                    </div>
                    <div className={s.cards}>
                        {mixes.map((r) => (
                            <TypeCard key={r.id} r={r} on={r.id === design.recipeId} />
                        ))}
                    </div>
                </>
            ) : null}
        </div>
    );
};

// ───────────── places inside (round 3 slot) ─────────────

/**
 * Reserved slot for "Places inside" (docs/DESIGN.md §9): compact rows like
 * "[Plaza] [● circle] [centre] [25 %]" plus "+ Add" which opens the type cards; selecting a row shows
 * that place's options below. Off until the engine supports it (window.__pmPlacesInside = true shows the
 * placeholder in the mock).
 */
const PlacesInside = () => {
    const { t } = useT();
    if (!(window as any).__pmPlacesInside) return null;
    return (
        <Section
            title={t("Placemaker.UI.Places.Title", "Places inside")}
            icon="layers"
            testId="places-inside"
            right={<Btn icon="plus" iconSize={15} variant="solid" label={t("Placemaker.UI.Places.Add", "Add")} disabled testId="places-add" />}
        >
            <div className={s.placesEmpty}>{t("Placemaker.UI.Places.Empty", "Put a plaza, pond or parking lot inside this area — coming soon.")}</div>
        </Section>
    );
};

// ───────────── step options ─────────────

const StepOptions = ({ catalog, design, state, step }: { catalog: Catalog; design: DesignState; state: ToolState; step: StepView }) => {
    const { t } = useT();
    const layer = layerOf(catalog, step.layer);
    const parent = parentOf(design.root, step.path);
    const color = layerColor(layer.id, layer.kind);
    const typed = !!(step.markerKinds && step.markerKinds.length);
    const right = (
        <div className={s.stepActions}>
            {parent && !isOverridePath(design.root, step.path) ? (
                <Tip title={step.enabled ? t("Placemaker.UI.Disable", "Turn off (keeps settings)") : t("Placemaker.UI.Enable", "Turn on")}>
                    <div className={s.stepSwitch}>
                        <Switch on={step.enabled} onChange={(v) => act.setStepEnabled(step.path, v)} testId={`enable-${step.path}`} />
                    </div>
                </Tip>
            ) : null}
            <Btn
                icon="dice"
                iconSize={16}
                title={t("Placemaker.UI.Randomize", "Randomize")}
                description={t("Placemaker.UI.RandomizeTip", "Rolls a new variation. Values you set yourself (marked with a lock) are kept.")}
                onClick={() => act.randomize(step.path)}
                testId={step.path === "0" ? "randomize" : `randomize-${step.path}`}
            />
            <Btn
                icon="reset"
                iconSize={16}
                title={t("Placemaker.UI.ResetStep", "Reset all to defaults")}
                onClick={() => act.resetStep(step.path)}
                testId={step.path === "0" ? "reset-step" : `reset-step-${step.path}`}
            />
        </div>
    );
    return (
        <div className={classNames(s.step, { [s.stepOff]: !step.enabled })} data-testid={`step-${step.path}`}>
            <div className={s.stepHead}>
                <div className={s.stepBar} style={{ backgroundColor: color }} />
                <Icon name={entityIcon(layer.id, layer.icon)} size={20} />
                <div className={s.stepName}>{stepHeading(t, catalog, design.root, step)}</div>
                <div className={s.flex} />
                {right}
            </div>
            {step.enabled ? (
                <div className={s.stepBody}>
                    {layer.presets.length ? (
                        <div className={s.presets} data-testid="presets">
                            {layer.presets.map((p) => (
                                <Btn
                                    key={p.id}
                                    variant="solid"
                                    icon={p.icon || "layers"}
                                    iconSize={15}
                                    label={t(p.name, humanize(p.name || p.id))}
                                    onClick={() => act.applyPreset(step.path, p.id)}
                                    testId={`preset-${p.id}`}
                                    className={s.presetBtn}
                                />
                            ))}
                        </div>
                    ) : null}
                    {!step.ready && step.nextStep && !typed ? (
                        <div className={s.nextStep} data-testid="next-step">
                            <Icon name="info" size={16} tint="accent" />
                            <div className={s.nextStepText}>{t(step.nextStep.key, humanize(step.nextStep.key), step.nextStep.args)}</div>
                        </div>
                    ) : null}
                    {typed ? <Entrances step={step} state={state} /> : null}
                    <ErrorBoundary name="controls">
                        <ControlList step={step} layer={layer} state={state} />
                    </ErrorBoundary>
                </div>
            ) : null}
        </div>
    );
};

// ───────────── stage ─────────────

export const DesignStage = ({ catalog, design, state }: { catalog: Catalog; design: DesignState; state: ToolState }) => {
    const { t } = useT();
    const root = design.root;
    const steps: StepView[] = [];
    walk(root, (st) => steps.push(st));
    return (
        <div className={s.stage} data-testid="stage-design">
            <Section title={t("Placemaker.UI.Make", "What to make")} className={s.first}>
                <ErrorBoundary name="recipes">
                    <TypeCards catalog={catalog} design={design} />
                </ErrorBoundary>
            </Section>
            <PlacesInside />
            {!root ? (
                <div className={s.emptyNote}>{t("Placemaker.UI.PickRecipe", "Pick what this area should become.")}</div>
            ) : (
                <Section title={t("Placemaker.UI.Options", "Options")} testId="options">
                    {steps.map((st) => (
                        <ErrorBoundary key={st.path} name={"step-" + st.path}>
                            <StepOptions catalog={catalog} design={design} state={state} step={st} />
                        </ErrorBoundary>
                    ))}
                </Section>
            )}
        </div>
    );
};
