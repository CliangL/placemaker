// Schema-driven controls: renders any layer's Catalog.layers[].controls. A new layer needs no UI code.
//   slider · toggle · choice (segmented, icons) · asset · assetMulti · markers · arrangement
// Property-inspector density: label left, editor right, one 26rem line per control. Every
// ControlSchema group is a foldable section whose header summarises its values ("90° · 6.0 m");
// only the first essential group starts open (remembered per layer + group). visibleWhen is honoured.
// Each row shows a lock when the player set the value explicitly (Randomize keeps it) and a reset
// button only when the value differs from the default. Descriptions live in tooltips only.
import React from "react";
import classNames from "classnames";
import { LayerType, SettingValue, StepView, ToolState } from "../contract";
import { act } from "../lib/bindings";
import { formatValue } from "../lib/format";
import { humanize, TFunc, useT } from "../lib/l10n";
import { ControlEx, defaultFor, isVisible, valueOf, valuesEqual } from "../lib/schema";
import { Tip } from "../lib/tips";
import { Btn, Collapsible, Icon, Segmented, Switch } from "../components/common";
import { AutoMosaic, autoPool, Thumb, useAssetList, useAssetName, usePicker } from "../components/picker";
import { Slider } from "./Slider";
import s from "./controls.module.scss";

type RowCtx = { step: StepView; layer: LayerType; state: ToolState };

const label = (t: TFunc, c: ControlEx) => t(c.label, humanize(c.label || c.id));
const optLabel = (t: TFunc, o: { value: string; label: string }) => t(o.label, humanize(o.label || o.value));

export const ControlRow = ({ c, ctx }: { c: ControlEx; ctx: RowCtx }) => {
    const { t } = useT();
    const { step } = ctx;
    const value = valueOf(step, c);
    const locked = step.locked.includes(c.id);
    const def = c.default !== undefined && c.default !== null ? c.default : undefined;
    const changed = def !== undefined && !valuesEqual(value, def);
    const commit = (v: SettingValue) => act.setValue(step.path, c.id, v);
    const tip = c.tooltip ? t(c.tooltip, "") : c.kind === "arrangement" ? t("Placemaker.UI.Arrangement.Hint", "Repeats in this order along the edges") : "";
    // Wide editors get their own line under the label: lists, and choices whose labels wouldn't fit beside it.
    const stacked = c.kind === "arrangement" || c.kind === "assetMulti" || (c.kind === "choice" && choiceLayout(c, t) !== "inline");

    const affordances = (
        <div className={s.affordances}>
            {locked ? (
                <Btn
                    icon="lock"
                    iconSize={11}
                    className={s.mini}
                    testId={`lock-${c.id}`}
                    title={t("Placemaker.UI.Locked", "Kept when randomizing")}
                    description={t("Placemaker.UI.LockedHint", "Click to let Randomize change it again.")}
                    onClick={() => act.unlockValue(step.path, c.id)}
                />
            ) : (
                <div className={s.miniSpacer} />
            )}
            {changed ? (
                <Btn
                    icon="reset"
                    iconSize={11}
                    className={s.mini}
                    testId={`reset-${c.id}`}
                    title={t("Placemaker.UI.ResetValue", "Reset to default")}
                    onClick={() => {
                        act.setValue(step.path, c.id, def as SettingValue);
                        act.unlockValue(step.path, c.id);
                    }}
                />
            ) : (
                <div className={s.miniSpacer} />
            )}
        </div>
    );

    return (
        <div className={classNames(s.row, { [s.rowStacked]: stacked, [s.rowAsset]: c.kind === "asset" })} data-testid={`control-${c.id}`} data-kind={c.kind}>
            <div className={s.rowHead}>
                <Tip title={label(t, c)} description={tip || undefined}>
                    <div className={classNames(s.label, { [s.labelWide]: stacked || c.kind === "toggle" })}>
                        <div className={s.labelText}>{label(t, c)}</div>
                    </div>
                </Tip>
                {stacked ? null : <div className={classNames(s.editor, { [s.editorTight]: c.kind === "toggle" })}>{renderEditor(c, value, commit, ctx, t)}</div>}
                {affordances}
            </div>
            {stacked ? <div className={s.stackedBody}>{renderEditor(c, value, commit, ctx, t)}</div> : null}
        </div>
    );
};

function renderEditor(c: ControlEx, value: SettingValue, commit: (v: SettingValue) => void, ctx: RowCtx, t: TFunc) {
    switch (c.kind) {
        case "slider":
            return (
                <Slider
                    testId={`slider-${c.id}`}
                    value={typeof value === "number" ? value : Number(value) || 0}
                    min={c.min ?? 0}
                    max={c.max ?? 1}
                    step={c.step ?? 1}
                    unit={c.unit}
                    onPreview={(v) => act.previewValue(ctx.step.path, c.id, v)}
                    onCommit={(v) => commit(v)}
                    onEndPreview={() => act.endPreview()}
                />
            );
        case "toggle":
            return (
                <div className={s.toggleCell}>
                    <Switch testId={`toggle-${c.id}`} on={value === true} onChange={(v) => commit(v)} />
                </div>
            );
        case "choice":
            return <ChoiceEditor c={c} value={String(value)} commit={commit} />;
        case "asset":
            return <AssetEditor c={c} value={typeof value === "string" ? value : ""} commit={commit} path={ctx.step.path} />;
        case "assetMulti":
            return <AssetMultiEditor c={c} value={Array.isArray(value) ? value : []} commit={commit} path={ctx.step.path} />;
        case "arrangement":
            return <ArrangementEditor c={c} value={Array.isArray(value) ? value : []} commit={commit} />;
        case "markers":
            return <MarkersEditor c={c} ctx={ctx} />;
        default:
            return <div className={s.unknown}>{t("Placemaker.UI.UnknownControl", "Unsupported")}</div>;
    }
}

/**
 * How a choice is laid out, so labels never get cut: up to 3 short options inline beside the label;
 * up to 3 longer ones as a full-width segmented row under it; more than 3 as wrapping chips under it.
 */
export function choiceLayout(c: ControlEx, t: TFunc): "inline" | "stacked" | "chips" {
    const opts = c.options ?? [];
    if (opts.length > 3) return "chips";
    const lens = opts.map((o) => optLabel(t, o).length + (o.icon ? 3 : 0));
    const total = lens.reduce((n, x) => n + x, 0);
    return total <= 15 && lens.every((x) => x <= 7) ? "inline" : "stacked";
}

const ChoiceEditor = ({ c, value, commit }: { c: ControlEx; value: string; commit: (v: SettingValue) => void }) => {
    const { t } = useT();
    const opts = c.options ?? [];
    if (choiceLayout(c, t) === "chips") {
        return (
            <div className={s.chips} data-testid={`choice-${c.id}`}>
                {opts.map((o) => {
                    const on = o.value === value;
                    const chip = (
                        <button
                            type="button"
                            key={o.value}
                            data-testid={`choice-${c.id}-${o.value}`}
                            aria-pressed={on ? "true" : "false"}
                            className={classNames(s.chip, { [s.chipOn]: on })}
                            onClick={(e) => {
                                e.stopPropagation();
                                if (!on) commit(o.value);
                            }}
                        >
                            {o.icon ? <Icon name={o.icon} size={13} tint={on ? "light" : "dim"} /> : null}
                            <div className={s.chipText}>{optLabel(t, o)}</div>
                        </button>
                    );
                    return o.tooltip ? (
                        <Tip key={o.value} title={optLabel(t, o)} description={t(o.tooltip, "")}>
                            {chip}
                        </Tip>
                    ) : (
                        chip
                    );
                })}
            </div>
        );
    }
    return (
        <Segmented
            testId={`choice-${c.id}`}
            stretch
            value={value}
            onChange={(v) => commit(v)}
            options={opts.map((o) => ({
                value: o.value,
                icon: o.icon || undefined,
                label: optLabel(t, o),
                title: optLabel(t, o),
                description: o.tooltip ? t(o.tooltip, "") : undefined,
            }))}
        />
    );
};

/**
 * Map preview while hovering picker tiles: previewValue with what the value would become; endPreview
 * reverts a hover (C# treats asset previews as a look only); a click commits with setValue.
 */
function previewHandlers(path: string, id: string, toValue: (ids: string[]) => SettingValue) {
    return {
        onPreview: (ids: string[]) => act.previewValue(path, id, toValue(ids)),
        onPreviewEnd: () => act.endPreview(),
    };
}

const AssetEditor = ({ c, value, commit, path }: { c: ControlEx; value: string; commit: (v: SettingValue) => void; path: string }) => {
    const { t } = useT();
    const picker = usePicker();
    const list = useAssetList(c.assetCategory);
    const name = useAssetName();
    const item = list?.find((a) => a.id === value);
    return (
        <button
            type="button"
            className={s.assetButton}
            data-testid={`asset-button-${c.id}`}
            onClick={(e) => {
                e.stopPropagation();
                picker.open({
                    category: c.assetCategory ?? "",
                    title: t(c.label, humanize(c.label || c.id)),
                    mode: "single",
                    selected: value ? [value] : [],
                    allowAutomatic: true,
                    onPick: (ids) => commit(ids[0] ?? ""),
                    ...previewHandlers(path, c.id, (ids) => ids[0] ?? ""),
                });
            }}
        >
            {value ? (
                <Thumb item={item ?? { id: value, name: name(c.assetCategory, value), icon: "" }} size={28} testId={`asset-thumb-${c.id}`} />
            ) : (
                <AutoMosaic items={autoPool(list, c.assetCategory, 3)} size={28} testId={`asset-thumb-${c.id}`} />
            )}
            <div className={s.assetName}>{value ? name(c.assetCategory, value) : t("Placemaker.UI.Picker.Auto", "Automatic")}</div>
            <Icon name="chevronRight" size={11} tint="dim" />
        </button>
    );
};

const AssetMultiEditor = ({ c, value, commit, path }: { c: ControlEx; value: string[]; commit: (v: SettingValue) => void; path: string }) => {
    const { t } = useT();
    const picker = usePicker();
    const name = useAssetName();
    const list = useAssetList(c.assetCategory);
    const itemOf = (id: string) => list?.find((a) => a.id === id) ?? { id, name: name(c.assetCategory, id), icon: "" };
    const openPicker = () =>
        picker.open({
            category: c.assetCategory ?? "",
            title: t(c.label, humanize(c.label || c.id)),
            mode: "multi",
            selected: value,
            allowAutomatic: true,
            onPick: (ids) => commit(ids),
            ...previewHandlers(path, c.id, (ids) => ids),
        });
    return (
        <div className={s.chips} data-testid={`assetmulti-${c.id}`}>
            {value.length === 0 ? (
                <button
                    type="button"
                    className={classNames(s.chip, s.chipAuto, s.chipThumb)}
                    data-testid={`assetmulti-auto-${c.id}`}
                    onClick={(e) => {
                        e.stopPropagation();
                        openPicker();
                    }}
                >
                    <AutoMosaic items={autoPool(list, c.assetCategory, 3)} size={20} testId={`asset-thumb-${c.id}`} />
                    <div className={s.chipText}>{t("Placemaker.UI.Picker.Auto", "Automatic")}</div>
                </button>
            ) : (
                value.map((id) => (
                    <div key={id} className={classNames(s.chip, s.chipOn, s.chipThumb)} data-testid={`chip-${id}`}>
                        <Thumb item={itemOf(id)} size={20} testId={`chip-thumb-${id}`} />
                        <div className={s.chipText}>{name(c.assetCategory, id)}</div>
                        <button
                            type="button"
                            className={s.chipX}
                            data-testid={`chip-remove-${id}`}
                            onClick={(e) => {
                                e.stopPropagation();
                                commit(value.filter((x) => x !== id));
                            }}
                        >
                            <Icon name="close" size={10} />
                        </button>
                    </div>
                ))
            )}
            <Btn icon="plus" iconSize={12} label={t("Placemaker.UI.Choose", "Choose")} className={s.chipAdd} onClick={openPicker} testId={`assetmulti-add-${c.id}`} />
        </div>
    );
};

const ArrangementEditor = ({ c, value, commit }: { c: ControlEx; value: string[]; commit: (v: SettingValue) => void }) => {
    const { t } = useT();
    const picker = usePicker();
    const name = useAssetName();
    const arrList = useAssetList(c.assetCategory);
    const move = (i: number, d: number) => {
        const j = i + d;
        if (j < 0 || j >= value.length) return;
        const next = value.slice();
        [next[i], next[j]] = [next[j], next[i]];
        commit(next);
    };
    return (
        <div className={s.chips} data-testid={`arrangement-${c.id}`}>
            {value.map((id, i) => (
                <div key={i + id} className={s.arrItem}>
                    <div className={s.arrIndex}>{i + 1}</div>
                    <Thumb item={arrList?.find((a) => a.id === id) ?? { id, name: name(c.assetCategory, id), icon: "" }} size={16} />
                    <div className={s.chipText}>{name(c.assetCategory, id)}</div>
                    <button type="button" className={s.chipX} disabled={i === 0} onClick={(e) => { e.stopPropagation(); move(i, -1); }}>
                        <Icon name="chevronLeft" size={10} />
                    </button>
                    <button type="button" className={s.chipX} onClick={(e) => { e.stopPropagation(); move(i, 1); }}>
                        <Icon name="chevronRight" size={10} />
                    </button>
                    <button
                        type="button"
                        className={s.chipX}
                        data-testid={`arr-remove-${i}`}
                        onClick={(e) => {
                            e.stopPropagation();
                            commit(value.filter((_, k) => k !== i));
                        }}
                    >
                        <Icon name="close" size={10} />
                    </button>
                </div>
            ))}
            <Btn
                icon="plus"
                iconSize={12}
                label={t("Placemaker.UI.Add", "Add")}
                className={s.chipAdd}
                testId={`arrangement-add-${c.id}`}
                onClick={() =>
                    picker.open({
                        category: c.assetCategory ?? "",
                        title: t(c.label, humanize(c.label || c.id)),
                        mode: "append",
                        selected: [],
                        allowAutomatic: false,
                        onPick: (ids) => ids[0] && commit([...value, ids[0]]),
                    })
                }
            />
        </div>
    );
};

const MarkersEditor = ({ c, ctx }: { c: ControlEx; ctx: RowCtx }) => {
    const { t } = useT();
    const kinds = c.markerKinds && c.markerKinds.length ? c.markerKinds : [c.id];
    return (
        <div className={s.markers}>
            {kinds.map((kind) => {
                const count = ctx.step.markers.find((m) => m.kind === kind)?.count ?? 0;
                const active = ctx.state.markerMode?.stepPath === ctx.step.path && ctx.state.markerMode?.kind === kind;
                return (
                    <div key={kind} className={s.markerRow}>
                        <Btn
                            icon="marker"
                            iconSize={12}
                            variant={active ? "accent" : "solid"}
                            testId={`markers-${kind}`}
                            label={
                                active
                                    ? t("Placemaker.UI.Markers.Active", "Done ({0})", [count])
                                    : count
                                    ? t("Placemaker.UI.Markers.PlaceN", "Place ({0})", [count])
                                    : t("Placemaker.UI.Markers.Place", "Place")
                            }
                            description={t("Placemaker.UI.Markers.Hint", "Click the outline to add; right-click one to remove.")}
                            onClick={() => (active ? act.endMarkers() : act.startMarkers(ctx.step.path, kind))}
                        />
                        {count > 0 && !active ? (
                            <Btn
                                icon="close"
                                iconSize={11}
                                testId={`markers-clear-${kind}`}
                                title={t("Placemaker.UI.Markers.Clear", "Remove all")}
                                onClick={() => act.clearMarkers(ctx.step.path, kind)}
                            />
                        ) : null}
                    </div>
                );
            })}
        </div>
    );
};

// ───────────── group summaries ─────────────

/**
 * One-line summary of a group's values for its folded header: "90° · 6.0 m · Concrete". A group led
 * by an off switch reads "Off"; automatic assets, empty lists and zero counts are left out.
 */
export function summarize(
    list: ControlEx[],
    step: StepView,
    t: TFunc,
    assetName: (category: string | undefined, id: string) => string,
): string {
    if (!list.length) return "";
    // A "master" switch leads its group (the rest only shows when it is on): off → the group is "Off".
    const first = list[0];
    const master = first.kind === "toggle" && (list.length === 1 || list.slice(1).some((c) => (c.visibleWhen ?? "").includes(first.id)));
    if (master && valueOf(step, first) !== true) return t("Placemaker.UI.Off", "Off");
    const parts: string[] = [];
    for (const c of list) {
        const v = valueOf(step, c);
        switch (c.kind) {
            case "toggle":
                if (v === true && !(master && c === first)) parts.push(label(t, c));
                break;
            case "slider": {
                const n = typeof v === "number" ? v : Number(v) || 0;
                if (c.unit === "count") {
                    if (n > 0) parts.push(label(t, c) + " " + formatValue(n, "count"));
                } else parts.push(formatValue(n, c.unit, c.step));
                break;
            }
            case "choice": {
                const o = (c.options ?? []).find((x) => x.value === String(v));
                if (o) parts.push(optLabel(t, o));
                break;
            }
            case "asset":
                if (typeof v === "string" && v) parts.push(assetName(c.assetCategory, v));
                break;
            case "assetMulti":
            case "arrangement":
                if (Array.isArray(v) && v.length) parts.push(label(t, c) + " " + v.length);
                break;
            case "markers": {
                const n = step.markers.reduce((k, m) => k + ((c.markerKinds ?? [c.id]).includes(m.kind) ? m.count : 0), 0);
                if (n) parts.push(label(t, c) + " " + n);
                break;
            }
        }
    }
    if (master && !parts.length) return t("Placemaker.UI.On", "On");
    if (!parts.length) return t("Placemaker.UI.Picker.Auto", "Automatic");
    // Keep it to what fits one header line (~30 characters): the first values win.
    let out = parts[0];
    for (const p of parts.slice(1, 3)) {
        if (out.length + 3 + p.length > 30) break;
        out += " · " + p;
    }
    return out;
}

/**
 * Controls of one step as foldable groups (Control.group), groups holding essentials first and
 * essentials first inside each group. Only the first essential group starts open (unless
 * `allFolded`), and each group's state is remembered under "<scope>:<group>".
 * Marker controls are left out when the step declares markerKinds: the Entrances section shows them.
 */
export const ControlList = ({
    step, layer, state, scope, allFolded, only, exclude, flat,
}: RowCtx & {
    scope?: string;
    allFolded?: boolean;
    /** Only these control ids (a feature's options living on the parent step). */
    only?: string[];
    /** Leave these ids out (they are shown under their feature toggle instead). */
    exclude?: Set<string>;
    /** Plain rows, no group sections. */
    flat?: boolean;
}) => {
    const { t } = useT();
    const assetName = useAssetName();
    const ctx = { step, layer, state };
    const typedMarkers = !!(step.markerKinds && step.markerKinds.length);
    const values = withDefaults(layer, step);
    const visible = (layer.controls as ControlEx[]).filter(
        (c) =>
            isVisible(c.visibleWhen, values) &&
            !(typedMarkers && c.kind === "markers") &&
            (!only || only.includes(c.id)) &&
            !(exclude && exclude.has(c.id)),
    );
    if (visible.length === 0) {
        return typedMarkers || only || exclude?.size ? null : <div className={s.none}>{t("Placemaker.UI.NoSettings", "No settings: adapts to the area.")}</div>;
    }
    if (flat) {
        return (
            <div className={s.list} data-testid="control-list-flat">
                {visible.map((c) => (
                    <ControlRow key={c.id} c={c} ctx={ctx} />
                ))}
            </div>
        );
    }
    const groups = groupBy(visible);
    const key = scope ?? layer.id;
    return (
        <div className={s.list} data-testid="control-list">
            {groups.map(([g, list], i) => {
                const name = g || "basics";
                const title = g ? t("Placemaker.UI.Group." + g, humanize(g)) : allFolded ? t("Placemaker.UI.Group.Options", "Options") : t("Placemaker.UI.Group.Basics", "Basics");
                const essential = list.some((c) => c.tier !== "more");
                return (
                    <Collapsible
                        key={name}
                        id={key + ":" + name}
                        testId={"group-" + name}
                        title={title}
                        summary={summarize(list, step, t, assetName)}
                        defaultOpen={!allFolded && i === 0 && essential}
                        nested={allFolded}
                    >
                        {list.map((c) => (
                            <ControlRow key={c.id} c={c} ctx={ctx} />
                        ))}
                    </Collapsible>
                );
            })}
        </div>
    );
};

function withDefaults(layer: LayerType, step: StepView): Record<string, SettingValue> {
    const v: Record<string, SettingValue> = {};
    for (const c of layer.controls as ControlEx[]) v[c.id] = defaultFor(c);
    return { ...v, ...step.values };
}

function groupBy(list: ControlEx[]): [string, ControlEx[]][] {
    const out: [string, ControlEx[]][] = [];
    for (const c of list) {
        const g = c.group || "";
        const hit = out.find((x) => x[0] === g);
        if (hit) hit[1].push(c);
        else out.push([g, [c]]);
    }
    const ess = (l: ControlEx[]) => l.some((c) => c.tier !== "more");
    for (const entry of out) entry[1] = [...entry[1].filter((c) => c.tier !== "more"), ...entry[1].filter((c) => c.tier === "more")];
    // stable: groups with essentials first, the unnamed group first among equals
    return out
        .map((e, i) => ({ e, i }))
        .sort((a, b) => Number(ess(b.e[1])) - Number(ess(a.e[1])) || Number(b.e[0] === "") - Number(a.e[0] === "") || a.i - b.i)
        .map((x) => x.e);
}
