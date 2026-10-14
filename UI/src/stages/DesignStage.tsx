// Stage 2 — what to make, then its options, inspector-style.
//   Make: one browser, Category ▸ Type ▸ Preset, with search (PresetBrowser.tsx). Mixes are shelved in
//   the UI (playtest 2) — the engine keeps them.
//   Options: the active-preset bar (Save / Save as / ⋯), the type's header (randomize, reset),
//   Entrances (typed markers), then one foldable
//   section per control group (only the first essential one open), then Features: a switch per
//   built-in feature, whose own options fold out underneath when it is on.
//   "Places inside": reserved slot for round 3, hidden for now.
import React, { useRef } from "react";
import classNames from "classnames";
import { Catalog, DesignState, FeatureFit, FeatureView, LayerType, StepView, ToolState } from "../contract";
import { act, playSound } from "../lib/bindings";
import { setOpen as setSectionOpen, useOpen } from "../lib/collapse";
import { scrollToTop, useReveal } from "../lib/reveal";
import { entityIcon, layerColor } from "../lib/icons";
import { humanize, TFunc, useT } from "../lib/l10n";
import { findStep, isOverridePath, layerOf, parentOf, regionLabelArgs } from "../lib/schema";
import { ErrorBoundary } from "../lib/errorBoundary";
import { usePanelLayout } from "../lib/layout";
import { Tip } from "../lib/tips";
import { Btn, Collapsible, Icon, Section, Switch } from "../components/common";
import { ControlList, controlsSummary } from "../controls/Controls";
import { useAssetName } from "../components/picker";
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
    // The footer's "Add at least one entrance" leads here: open, scroll to the top, flash.
    const foldId = layer.id + ":entrances";
    const ref = useRef<HTMLDivElement>(null);
    const flash = useReveal("entrances:" + step.path, () => {
        setSectionOpen(foldId, true);
        window.setTimeout(() => scrollToTop(ref.current), 30);
    });
    return (
        <div ref={ref} className={classNames(s.entrances, { [s.entrancesFlash]: flash })} data-testid={`entrances-wrap-${step.path}`} data-flash={flash ? "true" : "false"}>
        <Collapsible
            id={foldId}
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
                <Tip title={t("Placemaker.UI.Markers.NeedOne", "Add at least one entrance")} description={t("Placemaker.UI.Markers.HowTo", "Choose a type, then click the outline next to a road.")}>
                    <div className={s.required} data-testid="entrance-required">
                        <Icon name="warning" size={13} tint="warn" />
                        <div className={s.requiredText}>
                            {need > 1
                                ? t("Placemaker.UI.Markers.NeedN", "Add at least {0} entrances", [need])
                                : t("Placemaker.UI.Markers.NeedOne", "Add at least one entrance")}
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
        </div>
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

/** Short verdict of a fit status ("No room" / "Won't fit — reduced"); "" for ok. */
function fitLabel(t: TFunc, fit: FeatureFit): string {
    if (fit.kind === "failed") return t("Placemaker.UI.Fit.NoRoom", "No room");
    if (fit.kind === "reduced") return t("Placemaker.UI.Fit.Reduced", "Won't fit — reduced");
    return "";
}

/** Why, from the plan (the feature layer's own issue), or what to do about "no room". */
function fitDetail(t: TFunc, fit: FeatureFit): string {
    if (!fit.text || !fit.text.key) return "";
    if (fit.text.key === "Placemaker.UI.Feature.NoRoom") return t("Placemaker.UI.Fit.NoRoomHint", "Make the area bigger or turn other features off.");
    return t(fit.text.key, humanize(fit.text.key), fit.text.args);
}

/** Enabled features of a step that did not (fully) fit in the latest plan. */
export function misfits(step: StepView): FeatureView[] {
    return (step.features ?? []).filter((f) => f.enabled && f.status && f.status.kind !== "ok");
}

/** Fit badge at the end of a feature row: a green check, or the amber / red warning sign. */
const FitIcon = ({ t, fit, layer }: { t: TFunc; fit: FeatureFit; layer: string }) => {
    const ok = fit.kind === "ok";
    const title = ok ? t("Placemaker.UI.Fit.Ok", "Fits") : fitLabel(t, fit);
    return (
        <Tip title={title} description={ok ? undefined : fitDetail(t, fit) || undefined}>
            <div className={classNames(s.fitIcon, { [s.fitStale]: fit.stale })} data-testid={`feature-fit-${layer}`} data-kind={fit.kind} data-stale={fit.stale ? "true" : "false"}>
                <Icon name={ok ? "check" : fit.kind === "failed" ? "error" : "warning"} size={13} tint={ok ? "ok" : fit.kind === "failed" ? "danger" : "warn"} />
            </div>
        </Tip>
    );
};

const FeatureRow = ({
    catalog, design, state, parent, f, cols = 1,
}: { catalog: Catalog; design: DesignState; state: ToolState; parent: StepView; f: FeatureView; cols?: number }) => {
    const { t } = useT();
    const assetName = useAssetName();
    const layer = layerOf(catalog, f.layer);
    const name = f.label ? t(f.label, layerName(t, layer)) : layerName(t, layer);
    const child = f.enabled ? featureStep(design.root, parent, f) : null;
    const own = f.options ?? [];
    const tip = f.tooltip ? t(f.tooltip, "") : "";
    // Collapsed by default once enabled; remembered per parent layer + feature (FEEDBACK-4b #7).
    const [open, setOpen] = useOpen("feature:" + parent.layer + ">" + f.layer, false);
    // One-line summary of its visible options; none visible → nothing to fold (no empty body).
    const parts: string[] = [];
    if (f.enabled && own.length) parts.push(controlsSummary(parent, layerOf(catalog, parent.layer), t, assetName, own));
    if (child) parts.push(controlsSummary(child, layerOf(catalog, child.layer), t, assetName));
    const filled = parts.filter((p) => p);
    const foldable = f.enabled && filled.length > 0;
    const shown = foldable && open;
    let summary = "";
    if (foldable && !open) {
        const real = filled.filter((p) => p !== t("Placemaker.UI.Picker.Auto", "Automatic"));
        summary = (real.length ? real : filled).join(" · ");
    }
    const fit = f.enabled ? f.status : undefined;
    const bad = fit && fit.kind !== "ok";
    const detail = bad ? fitDetail(t, fit!) : "";
    const head = (
        <>
            {foldable ? <Icon name={open ? "chevronDown" : "chevronRight"} size={12} tint="dim" /> : <div className={s.featureChevronSpace} />}
            <Icon name={entityIcon(f.layer, f.icon || layer.icon)} size={14} tint={f.enabled ? "light" : "dim"} className={s.featureIcon} />
            <Tip title={name} description={tip || undefined}>
                <div className={s.featureName}>{name}</div>
            </Tip>
            {summary ? (
                <Tip title={name} description={summary}>
                    <div className={s.featureSummary} data-ellipsis data-testid={`feature-summary-${f.layer}`}>
                        {summary}
                    </div>
                </Tip>
            ) : (
                <div className={s.flex} />
            )}
        </>
    );
    return (
        <div
            className={classNames(s.feature, { [s.featureOn]: f.enabled, [s.featureOpen]: shown, [s.featureCell]: cols > 1 })}
            // Grid (wide panel): a cell per feature; one whose options are open spans the whole row.
            style={cols > 1 ? { width: shown ? "100%" : 100 / cols + "%" } : undefined}
            data-testid={`feature-${f.layer}`}
            data-enabled={f.enabled ? "true" : "false"}
            data-open={shown ? "true" : "false"}
            data-fit={fit?.kind ?? ""}
        >
            <div className={s.featureHead}>
                {foldable ? (
                    <button
                        type="button"
                        className={s.featureFold}
                        aria-expanded={open ? "true" : "false"}
                        data-testid={`feature-fold-${f.layer}`}
                        onClick={(e) => {
                            e.stopPropagation();
                            playSound(open ? "toggle-off" : "toggle-on");
                            setOpen(!open);
                        }}
                    >
                        {head}
                    </button>
                ) : (
                    <div className={s.featureFold}>{head}</div>
                )}
                {fit ? <FitIcon t={t} fit={fit} layer={f.layer} /> : null}
                <Switch on={f.enabled} onChange={(v) => act.setFeature(parent.path, f.layer, v)} testId={`feature-toggle-${f.layer}`} />
            </div>
            {bad ? (
                <Tip title={fitLabel(t, fit!)} description={detail || undefined}>
                    <div
                        className={classNames(s.fitLine, fit!.kind === "failed" ? s.fitLineFailed : s.fitLineReduced, { [s.fitStale]: fit!.stale })}
                        data-testid={`feature-fit-line-${f.layer}`}
                        data-kind={fit!.kind}
                    >
                        <div className={s.fitLabel}>{fitLabel(t, fit!)}</div>
                        {detail ? (
                            <div className={s.fitDetail} data-ellipsis>
                                {detail}
                            </div>
                        ) : null}
                    </div>
                </Tip>
            ) : null}
            {shown ? (
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
    const layout = usePanelLayout();
    const list = step.features ?? [];
    if (!list.length) return null;
    const on = list.filter((f) => f.enabled).length;
    const cols = Math.min(layout.featureCols, list.length);
    const rows = list.map((f) => <FeatureRow key={f.layer} catalog={catalog} design={design} state={state} parent={step} f={f} cols={cols} />);
    return (
        <Section title={t("Placemaker.UI.Features", "Features")} count={on || undefined} testId="features" className={s.features}>
            {cols > 1 ? (
                <div className={s.featureGrid} data-testid="feature-grid" data-cols={cols}>
                    {rows}
                </div>
            ) : (
                rows
            )}
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

/** "2 didn't fit" on a type's header: its enabled features that the latest plan had to shrink or drop. */
const MisfitChip = ({ step, catalog }: { step: StepView; catalog: Catalog }) => {
    const { t } = useT();
    const list = misfits(step);
    if (!list.length) return null;
    const failed = list.some((f) => f.status!.kind === "failed");
    const names = list
        .map((f) => {
            const l = layerOf(catalog, f.layer);
            return (f.label ? t(f.label, layerName(t, l)) : layerName(t, l)) + ": " + fitLabel(t, f.status!);
        })
        .join("\n");
    return (
        <Tip title={t("Placemaker.UI.Fit.CountTitle", "Not everything fits")} description={names}>
            <div
                className={classNames(s.misfit, failed ? s.misfitFailed : s.misfitReduced)}
                data-testid={`misfits-${step.path}`}
                data-count={list.length}
            >
                <Icon name="warning" size={12} tint={failed ? "danger" : "warn"} />
                <div className={s.misfitText}>
                    {list.length === 1 ? t("Placemaker.UI.Fit.CountOne", "1 didn't fit") : t("Placemaker.UI.Fit.Count", "{0} didn't fit", [list.length])}
                </div>
            </div>
        </Tip>
    );
};

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
                {step.enabled ? <MisfitChip step={step} catalog={catalog} /> : null}
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
