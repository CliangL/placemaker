// s17 — "Park stats": the last thing to configure before Build (docs/PARK-STATS.md). Each stat shows what
// Placemaker suggests from the design (calibrated to vanilla parks of this size), a one-line meaning, and lets the
// player override it. "Keep within vanilla range" (default on) clamps to what vanilla parks of a comparable size
// use; off allows anything, with a gentle warning. "How it's calculated" opens the formula and similar vanilla parks.
// Edits are per build (kept with the recipe, undoable, saved in the build's receipt) and never re-plan.
import React from "react";
import classNames from "classnames";
import { ParkStatView, ParkStatsView } from "../contract";
import { act } from "../lib/bindings";
import { area as fmtArea, formatValue } from "../lib/format";
import { useT } from "../lib/l10n";
import { Tip } from "../lib/tips";
import { Btn, Check, Collapsible, Icon } from "../components/common";
import { Slider } from "../controls/Slider";
import s from "./parkStats.module.scss";

/** [label key, English], [meaning key, English] per stat id (extract-lang reads these pairs). */
const STAT_TEXT: Record<string, { label: [string, string]; meaning: [string, string] }> = {
    attractiveness: {
        label: ["Placemaker.UI.ParkStats.Attractiveness", "Attractiveness"],
        meaning: ["Placemaker.UI.ParkStats.Attractiveness.Meaning", "Draws tourists: adds to your city's tourism attractiveness."],
    },
    leisure: {
        label: ["Placemaker.UI.ParkStats.Leisure", "Leisure"],
        meaning: ["Placemaker.UI.ParkStats.Leisure.Meaning", "How much fun one visit gives a citizen looking for something to do."],
    },
    coverageRange: {
        label: ["Placemaker.UI.ParkStats.Range", "Reach"],
        meaning: ["Placemaker.UI.ParkStats.Range.Meaning", "How far residents walk to enjoy the park (path cost, about metres)."],
    },
    coverageCapacity: {
        label: ["Placemaker.UI.ParkStats.Capacity", "Capacity"],
        meaning: ["Placemaker.UI.ParkStats.Capacity.Meaning", "How many nearby streets the park can serve before it gets crowded."],
    },
    entertainment: {
        label: ["Placemaker.UI.ParkStats.Entertainment", "Entertainment"],
        meaning: ["Placemaker.UI.ParkStats.Entertainment.Meaning", "Wellbeing boost for residents within reach."],
    },
    upkeep: {
        label: ["Placemaker.UI.ParkStats.Upkeep", "Upkeep"],
        meaning: ["Placemaker.UI.ParkStats.Upkeep.Meaning", "Monthly running cost a vanilla park this good would have."],
    },
};

const fmt = (st: ParkStatView, v: number) => formatValue(v, st.unit === "money" ? "money" : st.unit === "count" ? "count" : "", st.step);

export const ParkStatsSection = ({ ps, pending }: { ps: ParkStatsView; pending: boolean }) => {
    const { t } = useT();
    const anyOverride = ps.stats.some((x) => x.overridden);
    const outside = !ps.keepVanilla && ps.stats.some((x) => x.value < x.bandMin || x.value > x.bandMax);
    const attr = ps.stats.find((x) => x.id === "attractiveness");
    const leisure = ps.stats.find((x) => x.id === "leisure");
    const summary = [attr, leisure]
        .filter((x): x is ParkStatView => !!x)
        .map((x) => t(STAT_TEXT[x.id].label[0], STAT_TEXT[x.id].label[1]) + " " + fmt(x, x.value))
        .join(" · ");
    return (
        <Collapsible
            id="review:parkStats"
            title={t("Placemaker.UI.ParkStats.Title", "Park stats")}
            icon="star"
            summary={summary}
            defaultOpen
            testId="park-stats"
            className={classNames({ [s.shimmer]: pending })}
            right={
                anyOverride ? (
                    <Btn
                        icon="reset"
                        iconSize={12}
                        title={t("Placemaker.UI.ParkStats.ResetAll", "Reset all to suggested")}
                        onClick={() => act.resetParkStat("")}
                        testId="park-stats-reset-all"
                    />
                ) : null
            }
        >
            <div className={s.topRow}>
                <Tip title={t("Placemaker.UI.ParkStats.KeepVanilla.Tip", "Keeps every value inside what vanilla parks of a similar size use.")}>
                    <div className={s.keep}>
                        <Check
                            testId="park-stats-keep-vanilla"
                            checked={ps.keepVanilla}
                            onChange={(v) => act.setParkStatsKeepVanilla(v)}
                            label={t("Placemaker.UI.ParkStats.KeepVanilla", "Keep within vanilla range")}
                        />
                    </div>
                </Tip>
                <div className={s.kind} data-ellipsis>
                    {ps.leisureType === "beach"
                        ? t("Placemaker.UI.ParkStats.Beach", "Beach leisure")
                        : t("Placemaker.UI.ParkStats.Park", "Park leisure")}
                </div>
            </div>
            {outside ? (
                <div className={s.warn} data-testid="park-stats-outside">
                    <Icon name="warning" size={12} tint="warn" />
                    <div className={s.warnText}>
                        {t("Placemaker.UI.ParkStats.Outside", "Some values are beyond what vanilla parks this size have; the city may feel unbalanced.")}
                    </div>
                </div>
            ) : null}
            {ps.stats.map((st) => (
                <StatRow key={st.id} st={st} keepVanilla={ps.keepVanilla} />
            ))}
            <div className={s.note}>
                {t("Placemaker.UI.ParkStats.Note", "Attractiveness reaches the game now. The others are saved with the build for when it gets a park building.")}
            </div>
        </Collapsible>
    );
};

const StatRow = ({ st, keepVanilla }: { st: ParkStatView; keepVanilla: boolean }) => {
    const { t, tl } = useT();
    const text = STAT_TEXT[st.id] ?? { label: [st.id, st.id], meaning: ["", ""] };
    const label = t(text.label[0], text.label[1]);
    const meaning = text.meaning[0] ? t(text.meaning[0], text.meaning[1]) : "";
    const lo = keepVanilla ? st.bandMin : st.min;
    const hi = keepVanilla ? Math.max(st.bandMax, st.bandMin + st.step) : st.max;
    const outside = st.value < st.bandMin || st.value > st.bandMax;
    const similar = st.similar
        .map((v) => t(v.nameKey, v.name) + " (" + fmtArea(v.areaM2) + ") " + fmt(st, v.value))
        .join(" · ");
    return (
        <div className={s.stat} data-testid={`park-stat-${st.id}`}>
            <div className={s.head}>
                <Tip title={meaning}>
                    <div className={s.label} data-ellipsis="tooltip">
                        {label}
                    </div>
                </Tip>
                <Tip
                    title={
                        st.applied
                            ? t("Placemaker.UI.ParkStats.Applied.Tip", "The game uses this value for the build.")
                            : t("Placemaker.UI.ParkStats.Shown.Tip", "Needs a real park building; saved with the build for later.")
                    }
                >
                    <div className={classNames(s.tag, { [s.tagOn]: st.applied })}>
                        {st.applied ? t("Placemaker.UI.ParkStats.Applied", "In game") : t("Placemaker.UI.ParkStats.Shown", "Shown only")}
                    </div>
                </Tip>
                <div className={s.flex} />
                <div className={classNames(s.value, { [s.valueOut]: outside, [s.valueSet]: st.overridden })} data-testid={`park-stat-${st.id}-value`}>
                    {fmt(st, st.value)}
                </div>
                {st.overridden ? (
                    <Btn
                        icon="reset"
                        iconSize={11}
                        className={s.reset}
                        title={t("Placemaker.UI.ParkStats.Reset", "Reset to suggested ({0})", [fmt(st, st.suggested)])}
                        onClick={() => act.resetParkStat(st.id)}
                        testId={`park-stat-${st.id}-reset`}
                    />
                ) : null}
            </div>
            {meaning ? (
                <div className={s.meaning} data-ellipsis>
                    {meaning}
                </div>
            ) : null}
            <Slider
                value={Math.min(hi, Math.max(lo, st.value))}
                min={lo}
                max={hi}
                step={st.step}
                unit={st.unit === "money" ? "money" : st.unit === "count" ? "count" : ""}
                onPreview={() => undefined}
                onCommit={(v) => act.setParkStat(st.id, v)}
                testId={`park-stat-${st.id}-slider`}
            />
            <Collapsible
                id={`review:parkStats:${st.id}`}
                title={t("Placemaker.UI.ParkStats.How", "How it's calculated")}
                summary={t("Placemaker.UI.ParkStats.Suggested", "Suggested {0}", [fmt(st, st.suggested)])}
                nested
                testId={`park-stat-${st.id}-how`}
            >
                {st.formula.map((f, n) => (
                    <div key={n} className={s.formula}>
                        {tl(f)}
                    </div>
                ))}
                {similar ? (
                    <div className={s.similar}>
                        {t("Placemaker.UI.ParkStats.Similar", "Similar to: {0}", [similar])}
                    </div>
                ) : null}
            </Collapsible>
        </div>
    );
};
