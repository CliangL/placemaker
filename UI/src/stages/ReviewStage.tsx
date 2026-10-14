// Stage 3 — "you're told the price, it's all there", compact (playtest: Review was too wordy and its
// text overlapped): one key-numbers row (cost · upkeep) and one row of stat chips; issues as single
// lines (ellipsis, the full text in a tooltip, click to locate, first few with "N more"); the per-layer
// breakdown folded by default; park stats (s17) as the last thing to tune; an explicit acknowledgement for
// permanent effects before Build.
import React, { useState } from "react";
import classNames from "classnames";
import { Catalog, Issue, Summary, ToolState } from "../contract";
import { act } from "../lib/bindings";
import { area as fmtArea, formatValue, Money } from "../lib/format";
import { entityIcon, layerColor, statIcon } from "../lib/icons";
import { humanize, useT } from "../lib/l10n";
import { layerOf } from "../lib/schema";
import { Btn, Check, Collapsible, Icon } from "../components/common";
import { Tip } from "../lib/tips";
import { layerName, targetLabel } from "./DesignStage";
import { ParkStatsSection } from "./ParkStatsSection";
import s from "./stages.module.scss";

const SEVERITY: Record<Issue["severity"], { icon: string; tint: "danger" | "warn" | "accent" }> = {
    error: { icon: "error", tint: "danger" as const },
    warning: { icon: "warning", tint: "warn" as const },
    info: { icon: "info", tint: "accent" as const },
};

export const StatChip = ({ k, value, unit }: { k: string; value: number; unit: string }) => {
    const { t } = useT();
    return (
        <div className={s.stat} data-testid={`stat-${k}`}>
            <Icon name={statIcon(k)} size={12} className={s.statIcon} />
            <div className={s.statValue}>{unit === "m2" || unit === "area" ? fmtArea(value) : formatValue(value, unit === "count" ? "count" : unit)}</div>
            <div className={s.statLabel}>{t(k, humanize(k).toLowerCase())}</div>
        </div>
    );
};

export const ReviewStage = ({
    catalog, state, summary, ack, setAck,
}: {
    catalog: Catalog; state: ToolState; summary: Summary; ack: boolean; setAck: (v: boolean) => void;
}) => {
    const { t, tl } = useT();
    const pending = summary.stale || state.planning;
    const errors = summary.issues.filter((i) => i.severity === "error").length;
    return (
        <div className={s.stage} data-testid="stage-review">
            {!summary.ready ? (
                <div className={classNames(s.emptyNote, s.shimmer)} data-testid="review-planning">
                    {t("Placemaker.UI.Review.Planning", "Planning…")}
                </div>
            ) : (
                <>
                    <div className={classNames(s.bigRow, { [s.shimmer]: pending })} data-testid="review-numbers">
                        <div className={s.big}>
                            <div className={s.bigLabel}>{t("Placemaker.UI.Review.Cost", "Cost")}</div>
                            <div className={classNames(s.bigValue, { [s.bad]: !summary.canAfford })} data-testid="review-cost">
                                <Money value={summary.cost} />
                            </div>
                        </div>
                        <div className={s.bigSep} />
                        <div className={s.big}>
                            <div className={s.bigLabel}>{t("Placemaker.UI.Review.Upkeep", "Upkeep")}</div>
                            <div className={s.bigValue} data-testid="review-upkeep">
                                <Money value={summary.upkeep} perMonth />
                            </div>
                        </div>
                    </div>
                    {!summary.canAfford ? (
                        <div className={classNames(s.callout, s.calloutBad)} data-testid="cant-afford">
                            <Icon name="cost" size={13} tint="danger" />
                            <div className={s.calloutText}>{t("Placemaker.UI.Review.CantAfford", "Not enough money")}</div>
                        </div>
                    ) : null}
                    <div className={classNames(s.stats, { [s.shimmer]: pending })} data-testid="review-stats">
                        {summary.stats.some((st) => /(^|.)area$/i.test(st.key)) ? null : <StatChip k="Placemaker.Stat.Area" value={state.area.areaM2} unit="m2" />}
                        {summary.stats.map((st) => (
                            <StatChip key={st.key + st.unit} k={st.key} value={st.value} unit={st.unit} />
                        ))}
                    </div>
                    <Issues issues={summary.issues} />
                    {summary.layers.length > 1 ? (
                        <Collapsible
                            id="review:breakdown"
                            title={t("Placemaker.UI.Review.Breakdown", "Breakdown")}
                            summary={String(summary.layers.length)}
                            testId="breakdown"
                        >
                            {summary.layers.map((l, n) => {
                                const layer = layerOf(catalog, l.layer);
                                return (
                                    <div key={n} className={s.breakRow}>
                                        <div className={s.treeBar} style={{ backgroundColor: layerColor(layer.id, layer.kind) }} />
                                        <Icon name={entityIcon(layer.id, layer.icon)} size={13} color={layerColor(layer.id, layer.kind)} />
                                        <div className={s.breakName}>
                                            {layerName(t, layer)}
                                            {l.regionKey && l.regionKey !== "root" ? <span className={s.breakRegion}>{" · " + targetLabel(t, l.regionKey)}</span> : null}
                                        </div>
                                        <div className={s.breakStats}>
                                            {l.stats.slice(0, 2).map((st) => formatValue(st.value, st.unit === "count" ? "count" : st.unit) + " " + t(st.key, humanize(st.key).toLowerCase())).join(" · ")}
                                        </div>
                                        <div className={s.breakCost}>
                                            <Money value={l.cost} />
                                        </div>
                                    </div>
                                );
                            })}
                        </Collapsible>
                    ) : null}
                    {summary.parkStats && summary.parkStats.stats && summary.parkStats.stats.length ? (
                        <ParkStatsSection ps={summary.parkStats} pending={pending} />
                    ) : null}
                    {summary.permanent.length ? (
                        <div className={classNames(s.callout, s.calloutWarn)} data-testid="permanent">
                            <Icon name="warning" size={13} tint="warn" />
                            <div className={s.calloutBody}>
                                <div className={s.calloutTitle}>{t("Placemaker.UI.Review.Permanent", "Permanent changes")}</div>
                                {summary.permanent.map((p, n) => (
                                    <Tip key={n} title={tl(p)}>
                                        <div className={s.calloutLine} data-ellipsis="tooltip">
                                            {tl(p)}
                                        </div>
                                    </Tip>
                                ))}
                                <Check
                                    testId="ack-permanent"
                                    checked={ack}
                                    onChange={setAck}
                                    label={t("Placemaker.UI.Review.Ack", "I understand")}
                                />
                            </div>
                        </div>
                    ) : null}
                    {errors === 0 && summary.issues.length === 0 && !pending ? (
                        <div className={s.allGood} data-testid="all-good">
                            <Icon name="check" size={12} tint="ok" />
                            <div className={s.allGoodText}>{t("Placemaker.UI.Review.AllGood", "No problems found")}</div>
                        </div>
                    ) : null}
                </>
            )}
        </div>
    );
};

/** Issues shown before "N more". */
const ISSUES_SHOWN = 4;

/** One line per issue (worst first): icon, text cut with an ellipsis (full text in the tooltip), edit / locate. */
const Issues = ({ issues }: { issues: Issue[] }) => {
    const { t, tl } = useT();
    const [all, setAll] = useState(false);
    if (!issues.length) return null;
    const rank = { error: 0, warning: 1, info: 2 } as Record<string, number>;
    const sorted = issues.map((i, n) => ({ i, n })).sort((a, b) => (rank[a.i.severity] ?? 3) - (rank[b.i.severity] ?? 3) || a.n - b.n);
    const shown = all ? sorted : sorted.slice(0, ISSUES_SHOWN);
    return (
        <div className={s.issues} data-testid="issues">
            {shown.map(({ i, n }) => {
                const sev = SEVERITY[i.severity] ?? SEVERITY.info;
                const text = tl(i.text);
                return (
                    <div key={n} className={classNames(s.issue, s["issue_" + i.severity])} data-testid={`issue-${i.severity}`}>
                        <Icon name={sev.icon} size={12} tint={sev.tint} />
                        <Tip title={text}>
                            <div className={s.issueText} data-ellipsis="tooltip">
                                {text}
                            </div>
                        </Tip>
                        {i.stepPath ? (
                            <Btn
                                icon="edit"
                                iconSize={12}
                                className={s.issueBtn}
                                title={t("Placemaker.UI.Review.EditLayer", "Edit")}
                                onClick={() => {
                                    act.focusStep(i.stepPath as string);
                                    act.goToStage("design");
                                }}
                            />
                        ) : null}
                        {i.at ? (
                            <Btn
                                icon="marker"
                                iconSize={12}
                                className={s.issueBtn}
                                title={t("Placemaker.UI.Review.Show", "Show")}
                                onClick={() => act.locate(i.at as [number, number, number])}
                                testId="issue-locate"
                            />
                        ) : null}
                    </div>
                );
            })}
            {sorted.length > ISSUES_SHOWN ? (
                <button
                    type="button"
                    className={s.issuesMore}
                    data-testid="issues-more"
                    onClick={(e) => {
                        e.stopPropagation();
                        setAll(!all);
                    }}
                >
                    {all ? t("Placemaker.UI.Review.Fewer", "Show fewer") : t("Placemaker.UI.Review.MoreN", "{0} more", [sorted.length - ISSUES_SHOWN])}
                </button>
            ) : null}
        </div>
    );
};

export const BuildingStage = ({ state }: { state: ToolState }) => {
    const { t } = useT();
    const p = Math.max(0, Math.min(1, state.buildProgress ?? 0));
    return (
        <div className={s.stage} data-testid="stage-building">
            <div className={s.buildHead}>
                <Icon name="building" size={14} tint="accent" />
                <div className={s.buildTitle}>{t("Placemaker.UI.Building.Title", "Building…")}</div>
                <div className={s.flex} />
                <div className={s.buildPct}>{Math.round(p * 100)}%</div>
            </div>
            <div className={s.progress}>
                <div className={s.progressFill} style={{ width: p * 100 + "%" }} />
            </div>
            <div className={s.buildHint}>{t("Placemaker.UI.Building.Hint", "You can keep playing")}</div>
        </div>
    );
};
