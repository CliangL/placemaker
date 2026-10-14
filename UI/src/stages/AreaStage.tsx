// Stage 1 — choose the area: Draw / Block / Block paint / Paint / Roads as one row of compact buttons (descriptions
// in tooltips), brush settings, and a one-line readout of the current area with what to do next.
import React from "react";
import classNames from "classnames";
import { AreaMode, BrushRules, BrushShape, ToolState } from "../contract";
import { act } from "../lib/bindings";
import { area as fmtArea, group } from "../lib/format";
import { TFunc, useT } from "../lib/l10n";
import { Btn, Collapsible, Icon, Section, Segmented } from "../components/common";
import { Slider } from "../controls/Slider";
import { Tip } from "../lib/tips";
import s from "./stages.module.scss";

export function modeOptions(t: TFunc) {
    return [
        {
            value: "draw",
            icon: "draw",
            label: t("Placemaker.UI.Mode.Draw", "Draw"),
            title: t("Placemaker.UI.Mode.Draw", "Draw"),
            description: t("Placemaker.UI.Mode.DrawTip", "Click corners; click the first one to close. Snaps to curbs and right angles."),
        },
        {
            value: "block",
            icon: "block",
            label: t("Placemaker.UI.Mode.Block", "Block"),
            title: t("Placemaker.UI.Mode.Block", "Block"),
            description: t("Placemaker.UI.Mode.BlockTip", "Click a city block. Shift+click adds neighbours."),
        },
        {
            value: "blockPaint",
            icon: "blockPaint",
            label: t("Placemaker.UI.Mode.BlockPaint", "Block paint"),
            title: t("Placemaker.UI.Mode.BlockPaint", "Block paint"),
            description: t(
                "Placemaker.UI.Mode.BlockPaintTip",
                "Paint; roads stop the brush at the curb (in a closed block it stays in that block), and it skips zoned cells, buildings and other areas. Right mouse or Shift erases."
            ),
        },
        {
            value: "paint",
            icon: "paint",
            label: t("Placemaker.UI.Mode.Paint", "Paint"),
            title: t("Placemaker.UI.Mode.Paint", "Paint"),
            description: t("Placemaker.UI.Mode.PaintTip", "Left mouse paints, right mouse erases."),
        },
        {
            value: "perimeter",
            icon: "perimeter",
            label: t("Placemaker.UI.Mode.Perimeter", "Roads"),
            title: t("Placemaker.UI.Mode.Perimeter", "Roads"),
            description: t("Placemaker.UI.Mode.PerimeterTip", "Click the intersections around the area to follow its roads."),
        },
    ];
}

type StopKey = "roads" | "buildings" | "lots" | "zoned" | "emptyZones" | "water" | "stayInBlock";

/** Block paint "Rules": what the brush stops at (icon toggles), snap and smoothing. Mirrors Options → Block paint brush. */
const BrushRulesRow = ({ rules }: { rules: BrushRules }) => {
    const { t } = useT();
    const stops: { key: StopKey; icon: string; title: string; tip: string }[] = [
        { key: "roads", icon: "roads", title: t("Placemaker.UI.Rules.Roads", "Roads"), tip: t("Placemaker.UI.Rules.RoadsTip", "Stop at the curb and never paint across a road.") },
        { key: "buildings", icon: "building", title: t("Placemaker.UI.Rules.Buildings", "Buildings"), tip: t("Placemaker.UI.Rules.BuildingsTip", "Keep off building lots.") },
        { key: "lots", icon: "area", title: t("Placemaker.UI.Rules.Lots", "Areas"), tip: t("Placemaker.UI.Rules.LotsTip", "Keep off parks, other Placemaker areas and lots.") },
        { key: "zoned", icon: "edgeZoning", title: t("Placemaker.UI.Rules.Zoned", "Zoned"), tip: t("Placemaker.UI.Rules.ZonedTip", "Keep off zoned cells.") },
        { key: "emptyZones", icon: "zoneEmpty", title: t("Placemaker.UI.Rules.EmptyZones", "Empty zones"), tip: t("Placemaker.UI.Rules.EmptyZonesTip", "Also keep the empty zone grid free.") },
        { key: "water", icon: "water", title: t("Placemaker.UI.Rules.Water", "Water"), tip: t("Placemaker.UI.Rules.WaterTip", "Keep off deep water.") },
        { key: "stayInBlock", icon: "lock", title: t("Placemaker.UI.Rules.Block", "One block"), tip: t("Placemaker.UI.Rules.BlockTip", "Keep a stroke inside the block where it started.") },
    ];
    const on = stops.filter((x) => rules[x.key]).length;
    return (
        <Collapsible
            id="brushRules"
            testId="brush-rules"
            icon="layers"
            title={t("Placemaker.UI.Rules.Title", "Rules")}
            summary={t("Placemaker.UI.Rules.Summary", "{0} of {1} on", [on, stops.length])}
            right={
                <Btn
                    icon="reset"
                    iconSize={12}
                    title={t("Placemaker.UI.Rules.Reset", "Reset rules")}
                    description={t("Placemaker.UI.Rules.ResetTip", "Put every brush rule back to its default.")}
                    onClick={act.resetBrushRules}
                    testId="brush-rules-reset"
                />
            }
        >
            <div className={s.brushRow}>
                <div className={s.brushLabel}>{t("Placemaker.UI.Rules.Stops", "Stops at")}</div>
                <div className={s.ruleIcons} data-testid="brush-rules-stops">
                    {stops.map((x) => (
                        <Btn
                            key={x.key}
                            icon={x.icon}
                            iconSize={14}
                            selected={rules[x.key]}
                            title={x.title}
                            description={x.tip}
                            onClick={() => act.setBrushRules({ [x.key]: !rules[x.key] })}
                            testId={`brush-rule-${x.key}`}
                        />
                    ))}
                </div>
            </div>
            <div className={s.brushRow}>
                <div className={s.brushLabel}>{t("Placemaker.UI.Rules.Snap", "Snap")}</div>
                <Slider
                    testId="brush-rule-snap"
                    value={rules.snap}
                    min={0}
                    max={20}
                    step={1}
                    unit="m"
                    onPreview={() => undefined}
                    onCommit={(v) => act.setBrushRules({ snap: v })}
                />
            </div>
            <div className={s.brushRow}>
                <div className={s.brushLabel}>{t("Placemaker.UI.Rules.Smooth", "Smooth")}</div>
                <Slider
                    testId="brush-rule-smoothing"
                    value={rules.smoothing}
                    min={0}
                    max={6}
                    step={1}
                    onPreview={() => undefined}
                    onCommit={(v) => act.setBrushRules({ smoothing: v })}
                />
            </div>
        </Collapsible>
    );
};

const HOWTO: Record<string, [string, string]> = {
    draw: ["Placemaker.UI.Area.HowDraw", "Click corners; click the first to finish"],
    block: ["Placemaker.UI.Area.HowBlock", "Click a block; Shift+click adds more"],
    paint: ["Placemaker.UI.Area.HowPaint", "Paint with left mouse; right erases"],
    blockPaint: ["Placemaker.UI.Area.HowBlockPaint", "Paint; roads and zoning stop the brush"],
    perimeter: ["Placemaker.UI.Area.HowPerimeter", "Click intersections around the area"],
};

/** One-line area readout for the footer. */
export const AreaReadout = ({ state }: { state: ToolState; compact?: boolean }) => {
    const { t, tl } = useT();
    const a = state.area;
    if (a.problem) {
        return (
            <div className={s.readout} data-testid="area-readout">
                <Icon name="warning" size={13} tint="warn" />
                <div className={s.problem}>{tl(a.problem)}</div>
            </div>
        );
    }
    if (!a.closed || a.areaM2 <= 0) {
        return (
            <div className={s.readout} data-testid="area-readout">
                <div className={s.readoutHint}>
                    {a.pointCount > 0
                        ? t("Placemaker.UI.Area.Corners", "{0} corners · close the outline", [a.pointCount])
                        : t("Placemaker.UI.Area.Empty", "No area yet")}
                </div>
            </div>
        );
    }
    return (
        <div className={s.readout} data-testid="area-readout">
            <Icon name="area" size={13} tint="accent" />
            <div className={s.readoutBig}>{fmtArea(a.areaM2)}</div>
            <div className={s.readoutDim}>{t("Placemaker.UI.Area.Perimeter", "{0} m edge", [group(a.perimeterM)])}</div>
        </div>
    );
};

export const AreaStage = ({ state }: { state: ToolState }) => {
    const { t } = useT();
    const b = state.brush;
    const a = state.area;
    return (
        <div className={s.stage} data-testid="stage-area">
            <Section title={t("Placemaker.UI.Area.How", "Select by")} className={s.first}>
                <div className={s.modes} data-testid="area-mode">
                    {modeOptions(t).map((o) => {
                        const on = o.value === state.areaMode;
                        return (
                            <div key={o.value} className={s.modeCell}>
                                <Tip title={o.title} description={o.description}>
                                    <button
                                        type="button"
                                        data-testid={`area-mode-${o.value}`}
                                        aria-pressed={on ? "true" : "false"}
                                        className={classNames(s.mode, { [s.modeOn]: on })}
                                        onClick={(e) => {
                                            e.stopPropagation();
                                            if (!on) act.setAreaMode(o.value as AreaMode);
                                        }}
                                    >
                                        <Icon name={o.icon} size={14} tint={on ? "dark" : "light"} />
                                        <div className={s.modeName}>{o.label}</div>
                                    </button>
                                </Tip>
                            </div>
                        );
                    })}
                </div>
            </Section>
            {state.areaMode === "paint" || state.areaMode === "blockPaint" ? (
                <Section title={t("Placemaker.UI.Brush.Title", "Brush")} testId="brush">
                    <div className={s.brushRow}>
                        <div className={s.brushLabel}>{t("Placemaker.UI.Brush.Shape", "Shape")}</div>
                        <Segmented
                            testId="brush-shape"
                            stretch
                            value={b.shape}
                            onChange={(v) => act.setBrush(v as BrushShape, b.size, b.angle)}
                            options={[
                                { value: "round", icon: "brushRound", label: t("Placemaker.UI.Brush.Round", "Round") },
                                { value: "square", icon: "brushSquare", label: t("Placemaker.UI.Brush.Square", "Square") },
                            ]}
                        />
                    </div>
                    <div className={s.brushRow}>
                        <div className={s.brushLabel}>{t("Placemaker.UI.Brush.Size", "Size")}</div>
                        <Slider
                            testId="brush-size"
                            value={b.size}
                            min={5}
                            max={200}
                            step={1}
                            unit="m"
                            onPreview={(v) => act.setBrush(b.shape, v, b.angle)}
                            onCommit={(v) => act.setBrush(b.shape, v, b.angle)}
                        />
                    </div>
                    {b.shape === "square" ? (
                        <div className={s.brushRow}>
                            <div className={s.brushLabel}>{t("Placemaker.UI.Brush.Angle", "Angle")}</div>
                            <Slider
                                testId="brush-angle"
                                value={b.angle}
                                min={0}
                                max={90}
                                step={1}
                                unit="deg"
                                onPreview={(v) => act.setBrush(b.shape, b.size, v)}
                                onCommit={(v) => act.setBrush(b.shape, b.size, v)}
                            />
                        </div>
                    ) : null}
                    {state.areaMode === "blockPaint" && b.rules ? <BrushRulesRow rules={b.rules} /> : null}
                </Section>
            ) : null}
            <Section
                title={t("Placemaker.UI.Area.Selected", "Area")}
                right={
                    a.pointCount > 0 ? (
                        <Btn icon="trash" iconSize={12} variant="solid" label={t("Placemaker.UI.Area.Clear", "Clear")} onClick={act.clearArea} testId="clear-area" />
                    ) : undefined
                }
            >
                {a.closed && a.areaM2 > 0 ? (
                    <div className={s.areaStats}>
                        <div className={s.areaStat}>
                            <div className={s.areaStatLabel}>{t("Placemaker.UI.Area.Size", "Size")}</div>
                            <div className={s.areaStatValue}>{fmtArea(a.areaM2)}</div>
                        </div>
                        <div className={s.areaStat}>
                            <div className={s.areaStatLabel}>{t("Placemaker.UI.Area.Around", "Edge")}</div>
                            <div className={s.areaStatValue}>{group(a.perimeterM) + " m"}</div>
                        </div>
                        <div className={s.areaStat}>
                            <div className={s.areaStatLabel}>{t("Placemaker.UI.Area.CornersLabel", "Corners")}</div>
                            <div className={s.areaStatValue}>{group(a.pointCount)}</div>
                        </div>
                    </div>
                ) : null}
                <div className={s.areaFoot}>
                    <Icon name={a.closed ? "check" : "info"} size={12} tint={a.closed ? "ok" : "accent"} />
                    <div className={s.areaHint} data-testid="area-hint">
                        {a.closed
                            ? t("Placemaker.UI.Area.Adjust", "Drag corners to adjust, any time")
                            : HOWTO[state.areaMode]
                              ? t(HOWTO[state.areaMode][0], HOWTO[state.areaMode][1])
                              : ""}
                    </div>
                </div>
            </Section>
        </div>
    );
};
