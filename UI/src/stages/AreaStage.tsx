// Stage 1 — choose the area: Draw / Block / Block paint / Paint / Roads as one row of compact buttons (descriptions
// in tooltips), brush settings, and a one-line readout of the current area with what to do next.
import React from "react";
import classNames from "classnames";
import { AreaMode, BrushShape, ToolState } from "../contract";
import { act } from "../lib/bindings";
import { area as fmtArea, group } from "../lib/format";
import { TFunc, useT } from "../lib/l10n";
import { Btn, Icon, Section, Segmented } from "../components/common";
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
