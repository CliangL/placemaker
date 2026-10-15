// Stage 3 — Review, hyper-compact (s42; playtest: "way too much text bloat, alerts roll off with dot-dot-dot,
// breakdown unreadable"). Top to bottom:
//   1. Money card: Cost · Upkeep (+ area), one stacked bar of the cost by friendly bucket with a one-line legend; the
//      per-step breakdown sits behind the bar (tooltip, click to unfold).
//   2. Stat chips: icon + bold number + one word.
//   3. Alerts as short chips (2–3 words, `<key>.Short` lang keys): amber for permanent changes and warnings, red for
//      errors, blue for info; the full sentence in the tooltip; click shows it on the map / edits the step; "+N more".
//   4. Park stats: one row of five mini tiles (value + a tiny bar against the vanilla range); "Edit" unfolds the
//      sliders and explanations (ParkStatsSection).
// The "OK, permanent" tick sits beside the Build button in the footer (Panel.tsx).
import React, { useState } from "react";
import classNames from "classnames";
import { Catalog, Issue, LocText, Summary, ToolState } from "../contract";
import { act } from "../lib/bindings";
import { area as fmtArea, formatValue, Money } from "../lib/format";
import { entityIcon, layerColor, statIcon } from "../lib/icons";
import { categoryColor } from "../lib/palette";
import { humanize, TFunc, useT } from "../lib/l10n";
import { layerOf } from "../lib/schema";
import { Icon } from "../components/common";
import { Tip } from "../lib/tips";
import { useOpen } from "../lib/collapse";
import { layerName, targetLabel } from "./DesignStage";
import { recipeGroup } from "./PresetBrowser";
import { ParkStatsSection } from "./ParkStatsSection";
import s from "./review.module.scss";

export const StatChip = ({ k, value, unit }: { k: string; value: number; unit: string }) => {
    const { t } = useT();
    const label = t(k + ".Short", t(k, humanize(k)).toLowerCase());
    return (
        <Tip title={t(k, humanize(k))}>
            <div className={s.stat} data-testid={`stat-${k}`}>
                <Icon name={statIcon(k)} size={12} tint="dim" />
                <div className={s.statValue}>{unit === "m2" || unit === "area" ? fmtArea(value) : formatValue(value, unit === "count" ? "count" : unit)}</div>
                <div className={s.statLabel}>{label.toLowerCase()}</div>
            </div>
        </Tip>
    );
};

// ───────────── cost buckets ─────────────

type Bucket = { id: string; label: string; color: string; cost: number };

/** Friendly bucket of a plan layer: the type itself, or what kind of extra it is. */
const BUCKETS: [RegExp, string, string, string][] = [
    [/fountain|pond|lake|shore|splash|water/i, "water", "Placemaker.UI.Bucket.Water", "Water"],
    [/tree|flower|plant|hedge|garden/i, "planting", "Placemaker.UI.Bucket.Planting", "Planting"],
    [/terrain|ground|decal|rock|spoil|heap|paint/i, "ground", "Placemaker.UI.Bucket.Ground", "Ground"],
    [/play|ball|court|pitch|track|skate|fitness|tennis|cricket|baseball|volley|kick|chess|golf/i, "play", "Placemaker.UI.Bucket.Play", "Play"],
    [/restroom|kiosk|shop|bar|picnic|stall|stand|bench|tea/i, "amenities", "Placemaker.UI.Bucket.Amenities", "Amenities"],
    [/zon/i, "zoning", "Placemaker.UI.Bucket.Zoning", "Zoning"],
];
const BUCKET_COLOR: Record<string, string> = {
    water: "#4fb8e8", planting: "#6cc56a", ground: "#c99a6b", play: "#ff8f5a", amenities: "#ff7c9c", zoning: "#b48cff", extras: "#ffa94d",
};

function buckets(t: TFunc, catalog: Catalog, summary: Summary): Bucket[] {
    const out = new Map<string, Bucket>();
    for (const l of summary.layers) {
        if (!(l.cost > 0)) continue;
        const layer = layerOf(catalog, l.layer);
        let b: Bucket;
        if (layer.kind === "fill" && !layer.hidden) {
            const r = catalog.recipes.find((x) => x.id === layer.id);
            b = { id: "type:" + layer.id, label: layerName(t, layer), color: r ? categoryColor(recipeGroup(r)) : layerColor(layer.id, layer.kind), cost: 0 };
        } else {
            const hit = BUCKETS.find(([re]) => re.test(layer.id));
            b = hit
                ? { id: hit[1], label: t(hit[2], hit[3]), color: BUCKET_COLOR[hit[1]], cost: 0 }
                : { id: "extras", label: t("Placemaker.UI.Bucket.Extras", "Extras"), color: BUCKET_COLOR.extras, cost: 0 };
        }
        const cur = out.get(b.id) ?? b;
        cur.cost += l.cost;
        out.set(b.id, cur);
    }
    const list = Array.from(out.values()).sort((a, b) => b.cost - a.cost);
    // Five buckets at most: the rest goes to "Other".
    if (list.length > 5) {
        const rest = list.splice(4);
        list.push({ id: "other", label: t("Placemaker.UI.Bucket.Other", "Other"), color: "#9fb0b9", cost: rest.reduce((n, x) => n + x.cost, 0) });
    }
    return list;
}

const MoneyCard = ({ catalog, state, summary, pending }: { catalog: Catalog; state: ToolState; summary: Summary; pending: boolean }) => {
    const { t } = useT();
    const [open, setOpen] = useOpen("review:breakdown", false);
    const list = buckets(t, catalog, summary);
    const total = list.reduce((n, b) => n + b.cost, 0);
    const pct = (b: Bucket) => Math.max(1, Math.round((b.cost / total) * 100));
    const detail = summary.layers
        .map((l) => {
            const layer = layerOf(catalog, l.layer);
            const where = l.regionKey && l.regionKey !== "root" ? " · " + targetLabel(t, l.regionKey) : "";
            return layerName(t, layer) + where + ": ¢" + formatValue(Math.round(l.cost), "count");
        })
        .join("\n");
    return (
        <div className={classNames(s.money, { [s.shimmer]: pending })} data-testid="review-numbers">
            <div className={s.moneyRow}>
                <div className={s.moneyLabel}>{t("Placemaker.UI.Review.Cost", "Cost")}</div>
                <div className={classNames(s.moneyValue, { [s.bad]: !summary.canAfford })} data-testid="review-cost">
                    <Money value={summary.cost} />
                </div>
                <div className={s.moneyLabel}>{t("Placemaker.UI.Review.Upkeep", "Upkeep")}</div>
                <div className={s.moneyValue} data-testid="review-upkeep">
                    <Money value={summary.upkeep} perMonth />
                </div>
                <div className={s.flex} />
                {!summary.canAfford ? (
                    <div className={s.cantAfford} data-testid="cant-afford">
                        <Icon name="cost" size={11} tint="danger" />
                        <div className={s.cantText}>{t("Placemaker.UI.Review.CantAffordShort", "Too expensive")}</div>
                    </div>
                ) : (
                    <div className={s.area}>{fmtArea(state.area.areaM2)}</div>
                )}
            </div>
            {total > 0 && list.length ? (
                <Tip title={t("Placemaker.UI.Review.Breakdown", "Cost breakdown")} description={detail}>
                    <button
                        type="button"
                        className={s.barBtn}
                        data-testid="breakdown"
                        data-open={open ? "true" : "false"}
                        onClick={(e) => {
                            e.stopPropagation();
                            setOpen(!open);
                        }}
                    >
                        <div className={s.bar} data-testid="cost-bar">
                            {list.map((b) => (
                                <div key={b.id} className={s.barPart} style={{ width: (b.cost / total) * 100 + "%", backgroundColor: b.color }} />
                            ))}
                        </div>
                        <div className={s.legend}>
                            {list.map((b) => (
                                <div key={b.id} className={s.legendItem} data-testid={`bucket-${b.id}`}>
                                    <div className={s.legendDot} style={{ backgroundColor: b.color }} />
                                    <div className={s.legendText}>{b.label}</div>
                                    <div className={s.legendPct}>{pct(b) + "%"}</div>
                                </div>
                            ))}
                        </div>
                    </button>
                </Tip>
            ) : null}
            {open ? (
                <div className={s.breakdown} data-testid="breakdown-list">
                    {summary.layers.map((l, n) => {
                        const layer = layerOf(catalog, l.layer);
                        return (
                            <div key={n} className={s.breakRow}>
                                <Icon name={entityIcon(layer.id, layer.icon)} size={12} color={layerColor(layer.id, layer.kind)} />
                                <div className={s.breakName}>
                                    {layerName(t, layer)}
                                    {l.regionKey && l.regionKey !== "root" ? " · " + targetLabel(t, l.regionKey) : ""}
                                </div>
                                <div className={s.breakCost}>
                                    <Money value={l.cost} />
                                </div>
                            </div>
                        );
                    })}
                </div>
            ) : null}
        </div>
    );
};

// ───────────── alerts ─────────────

/** Alert chips shown before "+N more". */
const ALERTS_SHOWN = 5;

/** The short chip label of a long text: its `.Short` key, else its first clause, cut to whole words (never "…"). */
export function shortText(t: TFunc, loc: LocText | undefined): string {
    if (!loc || !loc.key) return t("Placemaker.UI.Review.Issue", "Issue");
    const short = t(loc.key + ".Short", "", loc.args);
    if (short) return short;
    const full = t(loc.key, humanize(loc.key), loc.args);
    const clause = full.split(/[.:;—(,]| - /)[0].trim();
    let words = clause.split(/\s+/);
    if (words.length > 4) words = words.slice(0, 3);
    // never end on a filler word ("153 trees were" → "153 trees")
    while (words.length > 1 && STOP.has(words[words.length - 1].toLowerCase())) words.pop();
    return words.join(" ");
}
const STOP = new Set(["a", "an", "the", "of", "to", "in", "on", "for", "with", "and", "or", "is", "are", "was", "were", "be", "been", "has", "have", "will", "can", "not", "no", "at", "by", "from", "this", "that", "it"]);

type Alert = { kind: "error" | "warn" | "info"; text: string; full: string; issue?: Issue; permanent?: boolean };

const ALERT_ICON = { error: ["error", "danger"], warn: ["warning", "warn"], info: ["info", "accent"] } as const;

const Alerts = ({ summary }: { summary: Summary }) => {
    const { t, tl } = useT();
    const [all, setAll] = useState(false);
    const alerts: Alert[] = [];
    const paintOnly = summary.permanentNeedsAck === false;
    for (const p of summary.permanent)
        alerts.push({
            kind: paintOnly ? "info" : "warn",
            text: shortText(t, p),
            full: tl(p) + (paintOnly ? " " + t("Placemaker.UI.Review.PaintOnly", "Ground paint stays when the build is removed.") : ""),
            permanent: true,
        });
    const rank = { error: 0, warning: 1, info: 2 } as Record<string, number>;
    const issues = summary.issues.map((i, n) => ({ i, n })).sort((a, b) => (rank[a.i.severity] ?? 3) - (rank[b.i.severity] ?? 3) || a.n - b.n);
    for (const { i } of issues)
        alerts.push({ kind: i.severity === "error" ? "error" : i.severity === "warning" ? "warn" : "info", text: shortText(t, i.text), full: tl(i.text), issue: i });
    // errors first, then permanent changes, warnings, info
    const order = (a: Alert) => (a.kind === "error" ? 0 : a.permanent ? 1 : a.kind === "warn" ? 2 : 3);
    alerts.sort((a, b) => order(a) - order(b));
    if (!alerts.length) return null;
    const shown = all ? alerts : alerts.slice(0, ALERTS_SHOWN);
    return (
        <div className={s.alerts} data-testid="issues">
            {shown.map((a, n) => {
                const [icon, tint] = ALERT_ICON[a.kind];
                const i = a.issue;
                const action = i?.at ? t("Placemaker.UI.Review.ClickShow", "Click: show on map") : i?.stepPath ? t("Placemaker.UI.Review.ClickEdit", "Click: edit") : undefined;
                return (
                    <Tip key={n} title={a.full} description={action}>
                        <button
                            type="button"
                            className={classNames(s.alert, s["alert_" + a.kind])}
                            data-testid={a.permanent ? "permanent" : `issue-${i?.severity}`}
                            onClick={(e) => {
                                e.stopPropagation();
                                if (i?.at) act.locate(i.at as [number, number, number]);
                                else if (i?.stepPath) {
                                    act.focusStep(i.stepPath);
                                    act.goToStage("design");
                                }
                            }}
                        >
                            <Icon name={icon} size={11} tint={tint} />
                            <div className={s.alertText}>{a.text}</div>
                            {i?.at ? <Icon name="marker" size={10} tint="dim" /> : null}
                        </button>
                    </Tip>
                );
            })}
            {alerts.length > ALERTS_SHOWN ? (
                <button
                    type="button"
                    className={classNames(s.alert, s.alertMore)}
                    data-testid="issues-more"
                    onClick={(e) => {
                        e.stopPropagation();
                        setAll(!all);
                    }}
                >
                    <div className={s.alertText}>{all ? t("Placemaker.UI.Review.Fewer", "Fewer") : t("Placemaker.UI.Review.PlusN", "+{0} more", [alerts.length - ALERTS_SHOWN])}</div>
                </button>
            ) : null}
        </div>
    );
};

// ───────────── stage ─────────────

export const ReviewStage = ({ catalog, state, summary }: { catalog: Catalog; state: ToolState; summary: Summary }) => {
    const { t } = useT();
    const pending = summary.stale || state.planning;
    const clean = summary.issues.length === 0 && summary.permanent.length === 0 && !pending;
    return (
        <div className={s.stage} data-testid="stage-review">
            {!summary.ready ? (
                <div className={classNames(s.emptyNote, s.shimmer)} data-testid="review-planning">
                    {t("Placemaker.UI.Review.Planning", "Planning…")}
                </div>
            ) : (
                <>
                    <MoneyCard catalog={catalog} state={state} summary={summary} pending={pending} />
                    <div className={classNames(s.stats, { [s.shimmer]: pending })} data-testid="review-stats">
                        {summary.stats.map((st) => (
                            <StatChip key={st.key + st.unit} k={st.key} value={st.value} unit={st.unit} />
                        ))}
                    </div>
                    <Alerts summary={summary} />
                    {clean ? (
                        <div className={s.allGood} data-testid="all-good">
                            <Icon name="check" size={11} tint="ok" />
                            <div className={s.allGoodText}>{t("Placemaker.UI.Review.AllGood", "No problems")}</div>
                        </div>
                    ) : null}
                    {summary.parkStats && summary.parkStats.stats && summary.parkStats.stats.length ? <ParkStatsSection ps={summary.parkStats} pending={pending} /> : null}
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
