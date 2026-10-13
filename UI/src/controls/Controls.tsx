// Schema-driven controls: renders any layer's Catalog.layers[].controls. A new layer needs no UI code.
//   slider · toggle · choice (segmented, icons) · asset · assetMulti · markers · arrangement
// Essential tier is shown; More tier sits in a "More options" foldout; visibleWhen is honoured.
// Each row shows a lock when the player set the value explicitly (Randomize keeps it) and a reset
// button only when the value differs from the default.
import React from "react";
import classNames from "classnames";
import { LayerType, SettingValue, StepView, ToolState } from "../contract";
import { act } from "../lib/bindings";
import { humanize, TFunc, useT } from "../lib/l10n";
import { ControlEx, defaultFor, isVisible, valueOf, valuesEqual } from "../lib/schema";
import { Tip } from "../lib/tips";
import { Btn, Foldout, Icon, Segmented, Switch } from "../components/common";
import { Thumb, useAssetList, useAssetName, usePicker } from "../components/picker";
import { Slider } from "./Slider";
import s from "./controls.module.scss";

type RowCtx = { step: StepView; layer: LayerType; state: ToolState };

const label = (t: TFunc, c: ControlEx) => t(c.label, humanize(c.label || c.id));

export const ControlRow = ({ c, ctx }: { c: ControlEx; ctx: RowCtx }) => {
    const { t } = useT();
    const { step } = ctx;
    const value = valueOf(step, c);
    const locked = step.locked.includes(c.id);
    const def = c.default !== undefined && c.default !== null ? c.default : undefined;
    const changed = def !== undefined && !valuesEqual(value, def);
    const commit = (v: SettingValue) => act.setValue(step.path, c.id, v);
    const tip = c.tooltip ? t(c.tooltip, "") : "";
    const stacked = c.kind === "arrangement" || c.kind === "assetMulti";

    const affordances = (
        <div className={s.affordances}>
            {locked ? (
                <Btn
                    icon="lock"
                    iconSize={13}
                    className={s.mini}
                    testId={`lock-${c.id}`}
                    title={t("Placemaker.UI.Locked", "Kept when randomizing")}
                    description={t("Placemaker.UI.LockedHint", "You set this yourself, so Randomize leaves it alone. Click to let Randomize change it again.")}
                    onClick={() => act.unlockValue(step.path, c.id)}
                />
            ) : (
                <div className={s.miniSpacer} />
            )}
            {changed ? (
                <Btn
                    icon="reset"
                    iconSize={13}
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
        <div className={classNames(s.row, { [s.rowStacked]: stacked })} data-testid={`control-${c.id}`} data-kind={c.kind}>
            <div className={s.rowHead}>
                <Tip title={label(t, c)} description={tip || undefined}>
                    <div className={s.label}>
                        {c.icon ? <Icon name={c.icon} size={15} className={s.labelIcon} /> : null}
                        <div className={s.labelText}>{label(t, c)}</div>
                    </div>
                </Tip>
                {stacked ? <div className={s.flex} /> : <div className={s.editor}>{renderEditor(c, value, commit, ctx, t)}</div>}
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
            return <Switch testId={`toggle-${c.id}`} on={value === true} onChange={(v) => commit(v)} />;
        case "choice":
            return <ChoiceEditor c={c} value={String(value)} commit={commit} />;
        case "asset":
            return <AssetEditor c={c} value={typeof value === "string" ? value : ""} commit={commit} />;
        case "assetMulti":
            return <AssetMultiEditor c={c} value={Array.isArray(value) ? value : []} commit={commit} />;
        case "arrangement":
            return <ArrangementEditor c={c} value={Array.isArray(value) ? value : []} commit={commit} />;
        case "markers":
            return <MarkersEditor c={c} ctx={ctx} />;
        default:
            return <div className={s.unknown}>{t("Placemaker.UI.UnknownControl", "Unsupported control")}</div>;
    }
}

const ChoiceEditor = ({ c, value, commit }: { c: ControlEx; value: string; commit: (v: SettingValue) => void }) => {
    const { t } = useT();
    const opts = c.options ?? [];
    const withIcons = opts.every((o) => !!o.icon);
    const iconOnly = withIcons && opts.length > 4;
    if (opts.length > 5 && !withIcons) {
        return (
            <div className={s.chips} data-testid={`choice-${c.id}`}>
                {opts.map((o) => (
                    <button
                        type="button"
                        key={o.value}
                        data-testid={`choice-${c.id}-${o.value}`}
                        aria-pressed={o.value === value ? "true" : "false"}
                        className={classNames(s.chip, { [s.chipOn]: o.value === value })}
                        onClick={(e) => {
                            e.stopPropagation();
                            if (o.value !== value) commit(o.value);
                        }}
                    >
                        {t(o.label, humanize(o.label || o.value))}
                    </button>
                ))}
            </div>
        );
    }
    return (
        <Segmented
            testId={`choice-${c.id}`}
            iconOnly={iconOnly}
            value={value}
            onChange={(v) => commit(v)}
            options={opts.map((o) => ({
                value: o.value,
                icon: o.icon || undefined,
                label: t(o.label, humanize(o.label || o.value)),
                description: o.tooltip ? t(o.tooltip, "") : undefined,
            }))}
        />
    );
};

const AssetEditor = ({ c, value, commit }: { c: ControlEx; value: string; commit: (v: SettingValue) => void }) => {
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
                });
            }}
        >
            {value ? <Thumb item={item ?? { id: value, name: name(c.assetCategory, value), icon: "" }} size={22} /> : <Icon name="auto" size={16} className={s.autoGlyph} />}
            <div className={s.assetName}>{value ? name(c.assetCategory, value) : t("Placemaker.UI.Picker.Auto", "Automatic")}</div>
            <Icon name="chevronUp" size={12} />
        </button>
    );
};

const AssetMultiEditor = ({ c, value, commit }: { c: ControlEx; value: string[]; commit: (v: SettingValue) => void }) => {
    const { t } = useT();
    const picker = usePicker();
    const name = useAssetName();
    useAssetList(c.assetCategory);
    const openPicker = () =>
        picker.open({
            category: c.assetCategory ?? "",
            title: t(c.label, humanize(c.label || c.id)),
            mode: "multi",
            selected: value,
            allowAutomatic: true,
            onPick: (ids) => commit(ids),
        });
    return (
        <div className={s.chips} data-testid={`assetmulti-${c.id}`}>
            {value.length === 0 ? (
                <div className={classNames(s.chip, s.chipAuto)}>
                    <Icon name="auto" size={13} />
                    <div className={s.chipText}>{t("Placemaker.UI.Picker.AutoMix", "Automatic mix")}</div>
                </div>
            ) : (
                value.map((id) => (
                    <div key={id} className={classNames(s.chip, s.chipOn)} data-testid={`chip-${id}`}>
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
                            <Icon name="close" size={11} />
                        </button>
                    </div>
                ))
            )}
            <Btn icon="plus" iconSize={14} label={t("Placemaker.UI.Choose", "Choose…")} className={s.chipAdd} onClick={openPicker} testId={`assetmulti-add-${c.id}`} />
        </div>
    );
};

const ArrangementEditor = ({ c, value, commit }: { c: ControlEx; value: string[]; commit: (v: SettingValue) => void }) => {
    const { t } = useT();
    const picker = usePicker();
    const name = useAssetName();
    useAssetList(c.assetCategory);
    const move = (i: number, d: number) => {
        const j = i + d;
        if (j < 0 || j >= value.length) return;
        const next = value.slice();
        [next[i], next[j]] = [next[j], next[i]];
        commit(next);
    };
    return (
        <div className={s.arrangement} data-testid={`arrangement-${c.id}`}>
            <div className={s.arrHint}>{t("Placemaker.UI.Arrangement.Hint", "Repeats in this order along the edges")}</div>
            <div className={s.chips}>
                {value.map((id, i) => (
                    <div key={i + id} className={s.arrItem}>
                        <div className={s.arrIndex}>{i + 1}</div>
                        <div className={s.chipText}>{name(c.assetCategory, id)}</div>
                        <button type="button" className={s.chipX} disabled={i === 0} onClick={(e) => { e.stopPropagation(); move(i, -1); }}>
                            <Icon name="chevronLeft" size={11} />
                        </button>
                        <button type="button" className={s.chipX} onClick={(e) => { e.stopPropagation(); move(i, 1); }}>
                            <Icon name="chevronRight" size={11} />
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
                            <Icon name="close" size={11} />
                        </button>
                    </div>
                ))}
                <Btn
                    icon="plus"
                    iconSize={14}
                    label={value.length ? t("Placemaker.UI.Add", "Add") : t("Placemaker.UI.Arrangement.First", "Add first item")}
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
                const plural = t(`Placemaker.Marker.${kind}.Plural`, humanize(kind).toLowerCase() + "s");
                return (
                    <div key={kind} className={s.markerRow}>
                        <Btn
                            icon="marker"
                            iconSize={15}
                            variant={active ? "accent" : "solid"}
                            testId={`markers-${kind}`}
                            label={
                                active
                                    ? t("Placemaker.UI.Markers.Active", "Done — {1} placed", [plural, count])
                                    : count
                                    ? t("Placemaker.UI.Markers.PlaceN", "Place {0} ({1})", [plural, count])
                                    : t("Placemaker.UI.Markers.Place", "Place {0}", [plural])
                            }
                            description={t("Placemaker.UI.Markers.Hint", "Click on the outline to add one, right-click one to remove it.")}
                            onClick={() => (active ? act.endMarkers() : act.startMarkers(ctx.step.path, kind))}
                        />
                        {count > 0 && !active ? (
                            <Btn
                                icon="close"
                                iconSize={13}
                                testId={`markers-clear-${kind}`}
                                title={t("Placemaker.UI.Markers.Clear", "Remove all {0}", [plural])}
                                onClick={() => act.clearMarkers(ctx.step.path, kind)}
                            />
                        ) : null}
                    </div>
                );
            })}
            {ctx.step.markers.length === 0 || ctx.step.markers.every((m) => m.count === 0) ? (
                <div className={s.markerAuto}>{t("Placemaker.UI.Markers.Auto", "Auto")}</div>
            ) : null}
        </div>
    );
};

/** Controls of one step: essentials, then "More options" grouped by section. */
export const ControlList = ({ step, layer, state }: RowCtx) => {
    const { t } = useT();
    const ctx = { step, layer, state };
    const visible = (layer.controls as ControlEx[]).filter((c) => isVisible(c.visibleWhen, withDefaults(layer, step)));
    const essential = visible.filter((c) => c.tier !== "more");
    const more = visible.filter((c) => c.tier === "more");
    const groups = groupBy(more);
    if (layer.controls.length === 0) {
        return <div className={s.none}>{t("Placemaker.UI.NoSettings", "Nothing to set up here: it adapts to the area by itself.")}</div>;
    }
    return (
        <div className={s.list} data-testid="control-list">
            {essential.map((c) => (
                <ControlRow key={c.id} c={c} ctx={ctx} />
            ))}
            {more.length ? (
                <Foldout title={t("Placemaker.UI.MoreOptions", "More options")} count={more.length} testId="more-options" memoryKey={"more:" + layer.id}>
                    {groups.map(([g, list]) => (
                        <div key={g || "_"} className={s.group}>
                            {groups.length > 1 && g ? <div className={s.groupTitle}>{t("Placemaker.UI.Group." + g, humanize(g))}</div> : null}
                            {list.map((c) => (
                                <ControlRow key={c.id} c={c} ctx={ctx} />
                            ))}
                        </div>
                    ))}
                </Foldout>
            ) : null}
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
    return out;
}
