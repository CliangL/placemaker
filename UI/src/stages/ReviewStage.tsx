// Stage 3 — "you're told the price, it's all there": cost, upkeep, what you get, per-layer breakdown,
// issues (click to locate), and an explicit acknowledgement for permanent effects before Build.
import React from "react";
import classNames from "classnames";
import { Catalog, Issue, Summary, ToolState } from "../contract";
import { act } from "../lib/bindings";
import { area as fmtArea, formatValue, Money } from "../lib/format";
import { entityIcon, layerColor, statIcon } from "../lib/icons";
import { humanize, useT } from "../lib/l10n";
import { layerOf } from "../lib/schema";
import { Btn, Check, Icon, Section } from "../components/common";
import { layerName, targetLabel } from "./DesignStage";
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
            <Icon name={statIcon(k)} size={16} className={s.statIcon} />
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
                    {t("Placemaker.UI.Review.Planning", "Planning your design…")}
                </div>
            ) : (
                <>
                    <div className={classNames(s.bigRow, { [s.shimmer]: pending })} data-testid="review-numbers">
                        <div className={s.big}>
                            <div className={s.bigLabel}>{t("Placemaker.UI.Review.Cost", "Construction cost")}</div>
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
                            <Icon name="cost" size={16} tint="danger" />
                            <div className={s.calloutText}>{t("Placemaker.UI.Review.CantAfford", "You can't afford this yet. Make it smaller or simpler, or wait for more money.")}</div>
                        </div>
                    ) : null}
                    <Section title={t("Placemaker.UI.Review.What", "What you get")}>
                        <div className={classNames(s.stats, { [s.shimmer]: pending })} data-testid="review-stats">
                            {summary.stats.some((st) => /(^|.)area$/i.test(st.key)) ? null : <StatChip k="Placemaker.Stat.Area" value={state.area.areaM2} unit="m2" />}
                            {summary.stats.map((st) => (
                                <StatChip key={st.key + st.unit} k={st.key} value={st.value} unit={st.unit} />
                            ))}
                        </div>
                    </Section>
                    {summary.issues.length ? (
                        <div className={s.issues} data-testid="issues">
                            {summary.issues.map((i, n) => {
                                const sev = SEVERITY[i.severity] ?? SEVERITY.info;
                                return (
                                    <div key={n} className={classNames(s.issue, s["issue_" + i.severity])} data-testid={`issue-${i.severity}`}>
                                        <Icon name={sev.icon} size={16} tint={sev.tint} />
                                        <div className={s.issueText}>{tl(i.text)}</div>
                                        {i.stepPath ? (
                                            <Btn icon="edit" iconSize={13} title={t("Placemaker.UI.Review.EditLayer", "Edit this layer")} onClick={() => { act.focusStep(i.stepPath as string); act.goToStage("design"); }} />
                                        ) : null}
                                        {i.at ? (
                                            <Btn icon="marker" iconSize={14} label={t("Placemaker.UI.Review.Show", "Show")} onClick={() => act.locate(i.at as [number, number, number])} testId="issue-locate" />
                                        ) : null}
                                    </div>
                                );
                            })}
                        </div>
                    ) : null}
                    {summary.layers.length > 1 ? (
                        <Section title={t("Placemaker.UI.Review.Breakdown", "Breakdown")} count={summary.layers.length} testId="breakdown">
                            {summary.layers.map((l, n) => {
                                const layer = layerOf(catalog, l.layer);
                                return (
                                    <div key={n} className={s.breakRow}>
                                        <div className={s.treeBar} style={{ backgroundColor: layerColor(layer.id, layer.kind) }} />
                                        <Icon name={entityIcon(layer.id, layer.icon)} size={15} color={layerColor(layer.id, layer.kind)} />
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
                        </Section>
                    ) : null}
                    {summary.permanent.length ? (
                        <div className={classNames(s.callout, s.calloutWarn)} data-testid="permanent">
                            <Icon name="warning" size={16} tint="warn" />
                            <div className={s.calloutBody}>
                                <div className={s.calloutTitle}>{t("Placemaker.UI.Review.Permanent", "Some changes can't be undone")}</div>
                                {summary.permanent.map((p, n) => (
                                    <div key={n} className={s.calloutText}>
                                        {tl(p)}
                                    </div>
                                ))}
                                <Check
                                    testId="ack-permanent"
                                    checked={ack}
                                    onChange={setAck}
                                    label={t("Placemaker.UI.Review.Ack", "I understand — build it anyway")}
                                />
                            </div>
                        </div>
                    ) : null}
                    {errors === 0 && summary.issues.length === 0 && !pending ? (
                        <div className={s.allGood} data-testid="all-good">
                            <Icon name="check" size={14} tint="ok" />
                            <div className={s.allGoodText}>{t("Placemaker.UI.Review.AllGood", "No problems found. Press Build when you're happy.")}</div>
                        </div>
                    ) : null}
                </>
            )}
        </div>
    );
};

export const BuildingStage = ({ state }: { state: ToolState }) => {
    const { t } = useT();
    const p = Math.max(0, Math.min(1, state.buildProgress ?? 0));
    return (
        <div className={s.stage} data-testid="stage-building">
            <div className={s.buildHead}>
                <Icon name="building" size={18} tint="accent" />
                <div className={s.buildTitle}>{t("Placemaker.UI.Building.Title", "Building…")}</div>
                <div className={s.flex} />
                <div className={s.buildPct}>{Math.round(p * 100)}%</div>
            </div>
            <div className={s.progress}>
                <div className={s.progressFill} style={{ width: p * 100 + "%" }} />
            </div>
            <div className={s.buildHint}>{t("Placemaker.UI.Building.Hint", "Placing roads, surfaces and objects. You can keep playing.")}</div>
        </div>
    );
};
