// The Placemaker panel: docked bottom-centre above the vanilla toolbar, never over the work area.
//   header  — stage bar [1 Area] → [2 Design] → [3 Review] (click any reachable stage to go back),
//             undo/redo with labels, collapse, close
//   body    — the current stage (each region behind its own ErrorBoundary)
//   footer  — what you get / why you can't continue yet + the primary button (always the next step, Enter)
//   hints   — shortcut strip from state.hints
// While drawing, C# collapses it to one compact row (state.panelCollapsed); the player can too.
import React, { useEffect, useMemo, useState } from "react";
import classNames from "classnames";
import { Stage, Summary, ToolState } from "../contract";
import { act, useCatalog, useDesign, useManaged, useSummary, useToolState } from "../lib/bindings";
import { area as fmtArea, compactMoney, formatValue } from "../lib/format";
import { useT } from "../lib/l10n";
import { ErrorBoundary } from "../lib/errorBoundary";
import { useRtl } from "../lib/rtl";
import { Tip } from "../lib/tips";
import { statIcon } from "../lib/icons";
import { AreaReadout, AreaStage, modeOptions } from "../stages/AreaStage";
import { DesignStage } from "../stages/DesignStage";
import { BuildingStage, ReviewStage } from "../stages/ReviewStage";
import { ManageStage } from "../stages/ManageStage";
import { Btn, Icon, Keys, Segmented } from "./common";
import { AssetPickerCard, PickerContext, PickerRequest } from "./picker";
import s from "./panel.module.scss";

const STAGES: { id: Stage; key: string; en: string }[] = [
    { id: "area", key: "Placemaker.UI.Stage.Area", en: "Area" },
    { id: "design", key: "Placemaker.UI.Stage.Design", en: "Design" },
    { id: "review", key: "Placemaker.UI.Stage.Review", en: "Review" },
];

const StageBar = ({ state }: { state: ToolState }) => {
    const { t } = useT();
    const currentIndex = STAGES.findIndex((x) => x.id === (state.stage === "building" ? "review" : state.stage));
    const locked = state.stage === "building";
    return (
        <div className={s.stageBar} data-testid="stage-bar">
            {STAGES.map((st, i) => {
                const current = i === currentIndex;
                const done = currentIndex > i;
                const reachable = !locked && !current && state.reachable.includes(st.id);
                const el = (
                    <button
                        type="button"
                        data-testid={`stagebar-${st.id}`}
                        data-state={current ? "current" : reachable ? "reachable" : "locked"}
                        className={classNames(s.stageItem, { [s.stageCurrent]: current, [s.stageReachable]: reachable, [s.stageLocked]: !current && !reachable })}
                        onClick={(e) => {
                            e.stopPropagation();
                            if (reachable) act.goToStage(st.id);
                        }}
                    >
                        <div className={classNames(s.stageNum, { [s.stageNumDone]: done })}>{done ? <Icon name="check" size={11} /> : i + 1}</div>
                        <div className={s.stageLabel}>{t(st.key, st.en)}</div>
                    </button>
                );
                return (
                    <React.Fragment key={st.id}>
                        {i > 0 ? <div className={classNames(s.stageLink, { [s.stageLinkDone]: done || current })} /> : null}
                        {reachable && done ? (
                            <Tip title={t("Placemaker.UI.Stage.Back", "Go back to {0}", [t(st.key, st.en)])} description={t("Placemaker.UI.Stage.BackTip", "Nothing is lost: your area and design are kept.")}>
                                {el}
                            </Tip>
                        ) : (
                            el
                        )}
                    </React.Fragment>
                );
            })}
        </div>
    );
};

const UndoRedo = ({ state }: { state: ToolState }) => {
    const { t, tl } = useT();
    const undo = state.undoLabel ? tl(state.undoLabel) : "";
    const redo = state.redoLabel ? tl(state.redoLabel) : "";
    return (
        <div className={s.undoRedo}>
            <Btn
                icon="undo"
                iconSize={16}
                disabled={!state.canUndo}
                testId="undo"
                label={undo ? <span className={s.undoLabel}>{undo}</span> : undefined}
                title={undo ? t("Placemaker.UI.UndoWhat", "Undo: {0}", [undo]) : t("Placemaker.UI.Undo", "Undo")}
                description={<Keys keys={["Ctrl", "Z"]} />}
                onClick={act.undo}
            />
            <Btn
                icon="redo"
                iconSize={16}
                disabled={!state.canRedo}
                testId="redo"
                title={redo ? t("Placemaker.UI.RedoWhat", "Redo: {0}", [redo]) : t("Placemaker.UI.Redo", "Redo")}
                description={<Keys keys={["Ctrl", "Y"]} />}
                onClick={act.redo}
            />
        </div>
    );
};

function quickStats(summary: Summary) {
    return summary.stats.filter((x) => x.unit === "count").slice(0, 1);
}

const Footer = ({ state, summary, blockedReason }: { state: ToolState; summary: Summary; blockedReason: string | null }) => {
    const { t, tl } = useT();
    const p = state.primary;
    const enabled = p.enabled && !blockedReason;
    const reason = blockedReason ?? (p.reason ? tl(p.reason) : "");
    const pending = summary.stale || state.planning;
    const showNumbers = state.stage === "design" && summary.ready;
    const label = tl(p.label, t("Placemaker.UI.Primary.Next", "Next"));
    const button = (
        <button
            type="button"
            data-testid="primary"
            data-action={p.action}
            aria-disabled={enabled ? "false" : "true"}
            className={classNames(s.primary, { [s.primaryOff]: !enabled, [s.primaryCancel]: p.action === "cancelBuild" })}
            onClick={(e) => {
                e.stopPropagation();
                if (enabled) act.primary();
            }}
        >
            <div className={s.primaryLabel}>{label}</div>
            {enabled && p.action !== "cancelBuild" ? <div className={s.enterKey}>{t("Placemaker.UI.Key.Enter", "Enter")}</div> : null}
            {enabled && p.action !== "cancelBuild" ? <Icon name="chevronRight" size={16} /> : null}
        </button>
    );
    return (
        <div className={s.footer}>
            <div className={s.footInfo}>
                {!enabled && reason ? (
                    <div className={s.reason} data-testid="primary-reason">
                        <Icon name="info" size={15} color="#ffb547" />
                        <div className={s.reasonText}>{reason}</div>
                    </div>
                ) : showNumbers ? (
                    <div className={classNames(s.quick, { [s.quickStale]: pending })} data-testid="quick-numbers">
                        <div className={s.quickItem}>
                            <Icon name="area" size={14} />
                            <div className={s.quickText}>{fmtArea(state.area.areaM2)}</div>
                        </div>
                        {quickStats(summary).map((st) => (
                            <div key={st.key} className={s.quickItem}>
                                <Icon name={statIcon(st.key)} size={14} />
                                <div className={s.quickText}>{formatValue(st.value, "count") + " " + t(st.key, "").toLowerCase()}</div>
                            </div>
                        ))}
                        <div className={classNames(s.quickItem, { [s.quickBad]: !summary.canAfford })}>
                            <Icon name="cost" size={14} />
                            <div className={s.quickText}>{compactMoney(summary.cost)}</div>
                        </div>
                        <div className={s.quickItem}>
                            <Icon name="upkeep" size={14} />
                            <div className={s.quickText}>{compactMoney(summary.upkeep) + t("Placemaker.UI.PerMonth", "/mo")}</div>
                        </div>
                    </div>
                ) : state.stage === "area" ? (
                    <AreaReadout state={state} compact />
                ) : state.stage === "review" ? (
                    <div className={s.reassure}>
                        <Icon name="undo" size={14} />
                        <div className={s.reassureText}>{t("Placemaker.UI.Review.Reassure", "Not quite right? Click Design or Area above — nothing is lost.")}</div>
                    </div>
                ) : null}
            </div>
            {!enabled && reason ? (
                <Tip title={label} description={reason}>
                    {button}
                </Tip>
            ) : (
                button
            )}
        </div>
    );
};

const Hints = ({ state }: { state: ToolState }) => {
    const { tl } = useT();
    if (!state.hints.length) return null;
    return (
        <div className={s.hints} data-testid="hints">
            {state.hints.map((h, i) => (
                <div key={i} className={s.hint}>
                    {h.keys && h.keys.length ? <Keys keys={h.keys} /> : null}
                    <div className={s.hintText}>{tl(h.text)}</div>
                </div>
            ))}
        </div>
    );
};

const MarkerBanner = ({ state }: { state: ToolState }) => {
    const { t, tl } = useT();
    if (!state.markerMode) return null;
    return (
        <div className={s.banner} data-testid="marker-banner">
            <Icon name="marker" size={16} color="#ffa94d" />
            <div className={s.bannerText}>{tl(state.markerMode.label, t("Placemaker.UI.Markers.Banner", "Click on the outline to place, right-click to remove"))}</div>
            <Btn variant="accent" label={t("Placemaker.UI.Done", "Done")} onClick={act.endMarkers} testId="marker-done" />
        </div>
    );
};

const CompactRow = ({ state, summary, blockedReason }: { state: ToolState; summary: Summary; blockedReason: string | null }) => {
    const { t } = useT();
    const st = STAGES.find((x) => x.id === state.stage);
    return (
        <div className={s.compact} data-testid="panel-compact">
            <Icon name="logo" size={20} color="#8fd1ff" />
            <div className={s.compactStage}>{st ? t(st.key, st.en) : ""}</div>
            {state.stage === "area" ? <Segmented iconOnly value={state.areaMode} onChange={(v) => act.setAreaMode(v as any)} options={modeOptions(t)} testId="area-mode-compact" /> : null}
            <div className={s.compactInfo}>
                <Footer state={state} summary={summary} blockedReason={blockedReason} />
            </div>
            <Btn icon="expand" iconSize={16} title={t("Placemaker.UI.Expand", "Show options")} onClick={() => act.setPanelCollapsed(false)} testId="expand" />
            <Btn icon="close" iconSize={16} title={t("Placemaker.UI.Close", "Close")} onClick={act.close} testId="close-compact" />
        </div>
    );
};

export const PlacemakerPanel = () => {
    const state = useToolState();
    const catalog = useCatalog();
    const design = useDesign();
    const summary = useSummary();
    const managed = useManaged();
    const rtl = useRtl();
    const { t } = useT();
    const [picker, setPicker] = useState<PickerRequest | null>(null);
    const [ack, setAck] = useState(false);
    const permKey = useMemo(() => JSON.stringify(summary.permanent), [summary.permanent]);

    useEffect(() => setAck(false), [permKey, state.stage === "review"]);
    useEffect(() => {
        if (!state.open || state.stage !== "design") setPicker(null);
    }, [state.open, state.stage]);

    const pickerCtx = useMemo(() => ({ open: (r: PickerRequest) => setPicker(r), close: () => setPicker(null), current: picker }), [picker]);

    if (!state.open) return null;

    const needsAck = state.stage === "review" && summary.permanent.length > 0 && !ack && state.primary.action === "build";
    const blockedReason = needsAck ? t("Placemaker.UI.Review.AckFirst", "Tick the box to confirm the permanent changes first") : null;

    let body: React.ReactNode = null;
    switch (state.stage) {
        case "area":
            body = <AreaStage state={state} />;
            break;
        case "design":
            body = <DesignStage catalog={catalog} design={design} state={state} />;
            break;
        case "review":
            body = <ReviewStage catalog={catalog} state={state} summary={summary} ack={ack} setAck={(v) => {
                setAck(v);
                act.acknowledgePermanent(v);
            }} />;
            break;
        case "building":
            body = <BuildingStage state={state} />;
            break;
        case "manage":
            body = <ManageStage group={managed} />;
            break;
    }

    return (
        <PickerContext.Provider value={pickerCtx}>
            <div className={classNames(s.dock, { [s.rtl]: rtl })} data-testid="placemaker-dock">
                <div className={s.column}>
                    {picker ? (
                        <ErrorBoundary name="picker">
                            <AssetPickerCard req={picker} onClose={() => setPicker(null)} />
                        </ErrorBoundary>
                    ) : null}
                    <MarkerBanner state={state} />
                    <div className={classNames(s.panel, { [s.panelCompact]: state.panelCollapsed })} data-testid="placemaker-panel" data-stage={state.stage}>
                        {state.panelCollapsed ? (
                            <ErrorBoundary name="compact">
                                <CompactRow state={state} summary={summary} blockedReason={blockedReason} />
                            </ErrorBoundary>
                        ) : (
                            <>
                                <div className={s.header}>
                                    <Tip title={t("Placemaker.Name", "Placemaker")} description={t("Placemaker.UI.About", "Choose an area, choose what it becomes.")}>
                                        <div className={s.brand}>
                                            <Icon name="logo" size={22} color="#8fd1ff" />
                                        </div>
                                    </Tip>
                                    {state.stage === "manage" ? (
                                        <div className={s.manageTitle}>{t("Placemaker.UI.Manage.Title", "Built with Placemaker")}</div>
                                    ) : (
                                        <ErrorBoundary name="stagebar">
                                            <StageBar state={state} />
                                        </ErrorBoundary>
                                    )}
                                    {state.editingGroup != null ? <div className={s.editingPill}>{t("Placemaker.UI.Editing", "Editing")}</div> : null}
                                    {state.planning ? (
                                        <Tip title={t("Placemaker.UI.Planning", "Updating the preview…")}>
                                            <div className={s.planningDot} data-testid="planning" />
                                        </Tip>
                                    ) : null}
                                    <div className={s.flex} />
                                    {state.stage === "manage" ? (
                                        <Btn icon="plus" iconSize={15} label={t("Placemaker.UI.Manage.New", "New area")} onClick={() => act.goToStage("area")} testId="manage-new" />
                                    ) : (
                                        <UndoRedo state={state} />
                                    )}
                                    {state.anarchy?.available ? (
                                        <Btn
                                            icon="anarchy"
                                            iconSize={15}
                                            selected={state.anarchy.enabled}
                                            title={t("Placemaker.UI.Anarchy", "Anarchy")}
                                            description={t("Placemaker.UI.AnarchyTip", "Build even where the game reports a collision (Anarchy mod).")}
                                            onClick={() => act.setAnarchy(!state.anarchy?.enabled)}
                                            testId="anarchy"
                                        />
                                    ) : null}
                                    <div className={s.headSep} />
                                    <Btn icon="collapse" iconSize={16} title={t("Placemaker.UI.Collapse", "Make smaller")} onClick={() => act.setPanelCollapsed(true)} testId="collapse" />
                                    <Btn icon="close" iconSize={16} title={t("Placemaker.UI.Close", "Close")} description={<Keys keys={["Esc"]} />} onClick={act.close} testId="close" />
                                </div>
                                <div className={s.bodyWrap}>
                                    <div className={s.body} key={state.stage}>
                                        <ErrorBoundary name={"stage-" + state.stage}>{body}</ErrorBoundary>
                                    </div>
                                </div>
                                <ErrorBoundary name="footer">
                                    <Footer state={state} summary={summary} blockedReason={blockedReason} />
                                </ErrorBoundary>
                                <Hints state={state} />
                            </>
                        )}
                    </div>
                </div>
            </div>
        </PickerContext.Provider>
    );
};

export const PlacemakerRoot = () => (
    <ErrorBoundary name="panel" fallback={(e, reset) => <PanelCrash error={e} reset={reset} />}>
        <PlacemakerPanel />
    </ErrorBoundary>
);

const PanelCrash = ({ error, reset }: { error: unknown; reset: () => void }) => (
    <div className={s.dock}>
        <div className={s.crash} onClick={reset}>
            <strong>Placemaker</strong> — the panel hit an error and was stopped so the rest of the game UI keeps working. Click to try again.
            <div className={s.crashDetail}>{error instanceof Error ? `${error.name}: ${error.message}` : String(error)}</div>
        </div>
    </div>
);
