// Stage 2 — what to make, then its options, inspector-style.
//   Make: one browser, Category ▸ Type ▸ Preset, with search (PresetBrowser.tsx). Mixes are shelved in
//   the UI (playtest 2) — the engine keeps them.
//   Options: the active-preset bar (Save / Save as / ⋯), the type's header (randomize, reset),
//   Entrances (typed markers), then one foldable
//   section per control group (only the first essential one open), then Features: a switch per
//   built-in feature, whose own options fold out underneath when it is on.
//   "Places inside": reserved slot for round 3, hidden for now.
import React from "react";
import classNames from "classnames";
import { Catalog, DesignState, FeatureView, LayerType, StepView, ToolState } from "../contract";
import { act } from "../lib/bindings";
import { entityIcon, layerColor } from "../lib/icons";
import { humanize, TFunc, useT } from "../lib/l10n";
import { findStep, isOverridePath, layerOf, parentOf, regionLabelArgs } from "../lib/schema";
import { ErrorBoundary } from "../lib/errorBoundary";
import { Tip } from "../lib/tips";
import { Btn, Collapsible, Icon, Section, Switch } from "../components/common";
import { ControlList } from "../controls/Controls";
import { makeSummary, PresetBar, TypeBrowser } from "./PresetBrowser";
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

/** Heading of a step: "Roads", "Park in each block", "Plaza — Part 2", "Block 3 — Plaza". */
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

/** Short button caption ("Entry" for "Entry only"), full name in the tooltip. */
const MARKER_SHORT: Record<string, [string, string]> = {
    entrance: ["Placemaker.UI.Marker.TwoWayShort", "2-way"],
    twoway: ["Placemaker.UI.Marker.TwoWayShort", "2-way"],
    entry: ["Placemaker.UI.Marker.EntryShort", "In"],
    exit: ["Placemaker.UI.Marker.ExitShort", "Out"],
    alley: ["Placemaker.UI.Marker.AlleyShort", "Alley"],
    footpath: ["Placemaker.UI.Marker.FootpathShort", "Path"],
};

function markerShort(t: TFunc, k: MarkerKind): string {
    const sh = MARKER_SHORT[k.kind.toLowerCase()];
    return sh ? t(sh[0], sh[1]) : markerLabel(t, k);
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

const Entrances = ({ step, layer, state }: { step: StepView; layer: LayerType; state: ToolState }) => {
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
    const missing = need > 0 && have < need;
    const perKind = new Map<string, number>();
    for (const m of list) perKind.set(m.kind, (perKind.get(m.kind) ?? 0) + 1);
    const summary = list.length
        ? Array.from(perKind.entries())
              .map(([k, n]) => n + " " + markerShort(t, byKind(k)))
              .join(" · ")
        : String(have);
    const numbers = new Map<string, number>();
    return (
        <Collapsible
            id={layer.id + ":entrances"}
            testId={`entrances-${step.path}`}
            title={
                <Tip
                    title={t("Placemaker.UI.Entrances", "Entrances")}
                    description={t("Placemaker.UI.Markers.Note", "Suggested next to roads. Add more by type, or remove any.")}
                >
                    <div>{t("Placemaker.UI.Entrances", "Entrances")}</div>
                </Tip>
            }
            summary={missing ? t("Placemaker.UI.Markers.None", "None yet") : summary}
            defaultOpen
        >
            {missing ? (
                <Tip title={t("Placemaker.UI.Markers.NeedOne", "Place at least one entrance")} description={t("Placemaker.UI.Markers.HowTo", "Choose a type, then click the outline next to a road.")}>
                    <div className={s.required} data-testid="entrance-required">
                        <Icon name="warning" size={13} tint="warn" />
                        <div className={s.requiredText}>
                            {need > 1
                                ? t("Placemaker.UI.Markers.NeedN", "Place at least {0} entrances", [need])
                                : t("Placemaker.UI.Markers.NeedOne", "Place at least one entrance")}
                        </div>
                    </div>
                </Tip>
            ) : null}
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
                                    <Icon name={markerIcon(k)} size={14} tint={on ? "dark" : "light"} />
                                    <div className={s.kindLabel}>{markerShort(t, k)}</div>
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
                                <Icon name={markerIcon(k)} size={12} tint="warn" />
                                <div className={s.markerName}>{markerLabel(t, k) + " " + n}</div>
                                <Btn
                                    icon="eye"
                                    iconSize={12}
                                    className={s.markerBtn}
                                    title={t("Placemaker.UI.Markers.Show", "Show on the map")}
                                    onClick={() => act.locate(m.position)}
                                    testId={`marker-show-${m.index}`}
                                />
                                <Btn
                                    icon="close"
                                    iconSize={12}
                                    className={s.markerBtn}
                                    title={t("Placemaker.UI.Markers.Remove", "Remove")}
                                    onClick={() => act.removeMarker(step.path, m.index)}
                                    testId={`marker-remove-${m.index}`}
                                />
                            </div>
                        );
                    })}
                </div>
            ) : null}
        </Collapsible>
    );
};

// ───────────── places inside (round 3 slot) ─────────────

/**
 * Reserved slot for "Places inside" (docs/DESIGN.md §9). Off until the engine supports it
 * (window.__pmPlacesInside = true shows the placeholder in the mock).
 */
const PlacesInside = () => {
    const { t } = useT();
    if (!(window as any).__pmPlacesInside) return null;
    return (
        <Section
            title={t("Placemaker.UI.Places.Title", "Places inside")}
            icon="layers"
            testId="places-inside"
            right={<Btn icon="plus" iconSize={12} variant="solid" label={t("Placemaker.UI.Places.Add", "Add")} disabled testId="places-add" />}
        >
            <div className={s.placesEmpty}>{t("Placemaker.UI.Places.Empty", "Coming soon")}</div>
        </Section>
    );
};

// ───────────── features ─────────────

function featureStep(root: StepView | null, parent: StepView, f: FeatureView): StepView | null {
    if (f.path) return findStep(root, f.path);
    return parent.children.find((c) => c.layer === f.layer) ?? null;
}

const FeatureRow = ({ catalog, design, state, parent, f }: { catalog: Catalog; design: DesignState; state: ToolState; parent: StepView; f: FeatureView }) => {
    const { t } = useT();
    const layer = layerOf(catalog, f.layer);
    const name = f.label ? t(f.label, layerName(t, layer)) : layerName(t, layer);
    const child = f.enabled ? featureStep(design.root, parent, f) : null;
    const own = f.options ?? [];
    const tip = f.tooltip ? t(f.tooltip, "") : "";
    return (
        <div className={classNames(s.feature, { [s.featureOn]: f.enabled })} data-testid={`feature-${f.layer}`} data-enabled={f.enabled ? "true" : "false"}>
            <div className={s.featureHead}>
                <Icon name={entityIcon(f.layer, f.icon || layer.icon)} size={14} tint={f.enabled ? "light" : "dim"} />
                <Tip title={name} description={tip || undefined}>
                    <div className={s.featureName}>{name}</div>
                </Tip>
                <Switch on={f.enabled} onChange={(v) => act.setFeature(parent.path, f.layer, v)} testId={`feature-toggle-${f.layer}`} />
            </div>
            {f.enabled && (child || own.length) ? (
                <div className={s.featureBody} data-testid={`feature-options-${f.layer}`}>
                    <ErrorBoundary name={"feature-" + f.layer}>
                        {own.length ? <ControlList step={parent} layer={layerOf(catalog, parent.layer)} state={state} only={own} flat /> : null}
                        {child ? <ControlList step={child} layer={layerOf(catalog, child.layer)} state={state} scope={parent.layer + ">" + f.layer} allFolded /> : null}
                    </ErrorBoundary>
                </div>
            ) : null}
        </div>
    );
};

const Features = ({ catalog, design, state, step }: { catalog: Catalog; design: DesignState; state: ToolState; step: StepView }) => {
    const { t } = useT();
    const list = step.features ?? [];
    if (!list.length) return null;
    const on = list.filter((f) => f.enabled).length;
    return (
        <Section title={t("Placemaker.UI.Features", "Features")} count={on || undefined} testId="features" className={s.features}>
            {list.map((f) => (
                <FeatureRow key={f.layer} catalog={catalog} design={design} state={state} parent={step} f={f} />
            ))}
        </Section>
    );
};

/** Parent controls that belong to a feature: shown under its toggle, never in the parent's groups. */
function featureOptionIds(step: StepView): Set<string> {
    const out = new Set<string>();
    for (const f of step.features ?? []) for (const id of f.options ?? []) out.add(id);
    return out;
}

/** Paths of children that are shown as features (not as separate steps). */
function featurePaths(root: StepView | null, step: StepView): Set<string> {
    const out = new Set<string>();
    for (const f of step.features ?? []) {
        const c = f.path ? findStep(root, f.path) : step.children.find((x) => x.layer === f.layer);
        if (c) out.add(c.path);
    }
    return out;
}

// ───────────── step options ─────────────

const StepOptions = ({ catalog, design, state, step }: { catalog: Catalog; design: DesignState; state: ToolState; step: StepView }) => {
    const { t } = useT();
    const layer = layerOf(catalog, step.layer);
    const parent = parentOf(design.root, step.path);
    const color = layerColor(layer.id, layer.kind);
    const typed = !!(step.markerKinds && step.markerKinds.length);
    const hidden = featurePaths(design.root, step);
    const subSteps = [...step.children.filter((c) => !hidden.has(c.path)), ...Object.keys(step.overrides).map((k) => step.overrides[k])];
    const scope = parent ? parent.layer + ">" + layer.id : layer.id;
    return (
        <div className={classNames(s.step, { [s.stepOff]: !step.enabled, [s.stepChild]: !!parent })} data-testid={`step-${step.path}`}>
            <div className={s.stepHead}>
                <div className={s.stepBar} style={{ backgroundColor: color }} />
                <Icon name={entityIcon(layer.id, layer.icon)} size={15} />
                <div className={s.stepName}>{stepHeading(t, catalog, design.root, step)}</div>
                {parent && !isOverridePath(design.root, step.path) ? (
                    <Tip title={step.enabled ? t("Placemaker.UI.Disable", "Turn off") : t("Placemaker.UI.Enable", "Turn on")}>
                        <div className={s.stepSwitch}>
                            <Switch on={step.enabled} onChange={(v) => act.setStepEnabled(step.path, v)} testId={`enable-${step.path}`} />
                        </div>
                    </Tip>
                ) : null}
                <Btn
                    icon="dice"
                    iconSize={14}
                    title={t("Placemaker.UI.Randomize", "Randomize")}
                    description={t("Placemaker.UI.RandomizeTip", "New variation; locked values stay.")}
                    onClick={() => act.randomize(step.path)}
                    testId={step.path === "0" ? "randomize" : `randomize-${step.path}`}
                />
                <Btn
                    icon="reset"
                    iconSize={14}
                    title={t("Placemaker.UI.ResetStep", "Reset all")}
                    onClick={() => act.resetStep(step.path)}
                    testId={step.path === "0" ? "reset-step" : `reset-step-${step.path}`}
                />
            </div>
            {step.enabled ? (
                <div className={s.stepBody}>
                    {!step.ready && step.nextStep && !typed ? (
                        <Tip title={t(step.nextStep.key, humanize(step.nextStep.key), step.nextStep.args)}>
                            <div className={s.nextStep} data-testid="next-step">
                                <Icon name="info" size={13} tint="accent" />
                                <div className={s.nextStepText}>{t(step.nextStep.key, humanize(step.nextStep.key), step.nextStep.args)}</div>
                            </div>
                        </Tip>
                    ) : null}
                    {typed ? <Entrances step={step} layer={layer} state={state} /> : null}
                    <ErrorBoundary name="controls">
                        <ControlList step={step} layer={layer} state={state} scope={scope} exclude={featureOptionIds(step)} />
                    </ErrorBoundary>
                    <Features catalog={catalog} design={design} state={state} step={step} />
                    {subSteps.map((c) => (
                        <ErrorBoundary key={c.path} name={"step-" + c.path}>
                            <StepOptions catalog={catalog} design={design} state={state} step={c} />
                        </ErrorBoundary>
                    ))}
                </div>
            ) : null}
        </div>
    );
};

// ───────────── stage ─────────────

export const DesignStage = ({ catalog, design, state }: { catalog: Catalog; design: DesignState; state: ToolState }) => {
    const { t } = useT();
    const root = design.root;
    return (
        <div className={s.stage} data-testid="stage-design">
            <Collapsible
                id="design:make"
                testId="make"
                title={t("Placemaker.UI.Make", "Make")}
                summary={<div data-ellipsis>{makeSummary(t, catalog, design)}</div>}
                defaultOpen
                className={s.make}
            >
                <ErrorBoundary name="recipes">
                    <TypeBrowser catalog={catalog} design={design} />
                </ErrorBoundary>
            </Collapsible>
            <PlacesInside />
            {!root ? (
                <div className={s.emptyNote}>{t("Placemaker.UI.PickRecipe", "Pick a type")}</div>
            ) : (
                <div className={s.options} data-testid="options">
                    <ErrorBoundary name="preset-bar">
                        <PresetBar catalog={catalog} design={design} step={root} />
                    </ErrorBoundary>
                    <ErrorBoundary name={"step-" + root.path}>
                        <StepOptions catalog={catalog} design={design} state={state} step={root} />
                    </ErrorBoundary>
                </div>
            )}
        </div>
    );
};
