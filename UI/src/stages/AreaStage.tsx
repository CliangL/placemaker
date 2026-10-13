// Stage 1 — choose the area: Draw / Pick block / Paint / Road loop as four cards, brush settings,
// and a card describing the current area (size, perimeter, corners) with what to do next.
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
            short: t("Placemaker.UI.Mode.DrawShort", "Click the corners"),
            title: t("Placemaker.UI.Mode.Draw", "Draw"),
            description: t(
                "Placemaker.UI.Mode.DrawTip",
                "Click corners on the map; click the first corner to close. Snaps to curbs, buildings and right angles. Drag corners or edges to adjust.",
            ),
        },
        {
            value: "block",
            icon: "block",
            label: t("Placemaker.UI.Mode.Block", "Pick block"),
            short: t("Placemaker.UI.Mode.BlockShort", "Take a city block"),
            title: t("Placemaker.UI.Mode.Block", "Pick block"),
            description: t("Placemaker.UI.Mode.BlockTip", "Hover a city block and click to take it. Shift+click adds neighbouring blocks."),
        },
        {
            value: "paint",
            icon: "paint",
            label: t("Placemaker.UI.Mode.Paint", "Paint"),
            short: t("Placemaker.UI.Mode.PaintShort", "Brush over the map"),
            title: t("Placemaker.UI.Mode.Paint", "Paint"),
            description: t("Placemaker.UI.Mode.PaintTip", "Hold left mouse to paint the area, right mouse to erase."),
        },
        {
            value: "perimeter",
            icon: "perimeter",
            label: t("Placemaker.UI.Mode.Perimeter", "Road loop"),
            short: t("Placemaker.UI.Mode.PerimeterShort", "Follow existing roads"),
            title: t("Placemaker.UI.Mode.Perimeter", "Road loop"),
            description: t("Placemaker.UI.Mode.PerimeterTip", "Click road intersections around the area, one after another, to follow existing roads exactly."),
        },
    ];
}

const HOWTO: Record<string, [string, string]> = {
    draw: ["Placemaker.UI.Area.HowDraw", "Click corners on the map, then click the first corner to finish."],
    block: ["Placemaker.UI.Area.HowBlock", "Click a city block. Shift+click adds the next one."],
    paint: ["Placemaker.UI.Area.HowPaint", "Paint over the map with the left mouse button; the right one erases."],
    perimeter: ["Placemaker.UI.Area.HowPerimeter", "Click the intersections around the area, one after another."],
};

/** One-line area readout for the footer. */
export const AreaReadout = ({ state }: { state: ToolState; compact?: boolean }) => {
    const { t, tl } = useT();
    const a = state.area;
    if (a.problem) {
        return (
            <div className={s.readout} data-testid="area-readout">
                <Icon name="warning" size={16} tint="warn" />
                <div className={s.problem}>{tl(a.problem)}</div>
            </div>
        );
    }
    if (!a.closed || a.areaM2 <= 0) {
        return (
            <div className={s.readout} data-testid="area-readout">
                <div className={s.readoutHint}>
                    {a.pointCount > 0
                        ? t("Placemaker.UI.Area.Corners", "{0} corners — close the outline to continue", [a.pointCount])
                        : t("Placemaker.UI.Area.Empty", "No area yet")}
                </div>
            </div>
        );
    }
    return (
        <div className={s.readout} data-testid="area-readout">
            <Icon name="area" size={16} tint="accent" />
            <div className={s.readoutBig}>{fmtArea(a.areaM2)}</div>
            <div className={s.readoutDim}>{t("Placemaker.UI.Area.Perimeter", "{0} m around", [group(a.perimeterM)])}</div>
        </div>
    );
};

export const AreaStage = ({ state }: { state: ToolState }) => {
    const { t } = useT();
    const b = state.brush;
    const a = state.area;
    return (
        <div className={s.stage} data-testid="stage-area">
            <Section title={t("Placemaker.UI.Area.How", "Choose the area")} className={s.first}>
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
                                        <div className={s.modeIcon}>
                                            <Icon name={o.icon} size={22} tint={on ? "dark" : "light"} />
                                        </div>
                                        <div className={s.modeText}>
                                            <div className={s.modeName}>{o.label}</div>
                                            <div className={s.modeDesc}>{o.short}</div>
                                        </div>
                                    </button>
                                </Tip>
                            </div>
                        );
                    })}
                </div>
            </Section>
            {state.areaMode === "paint" ? (
                <Section title={t("Placemaker.UI.Brush.Title", "Brush")} testId="brush">
                    <div className={s.brushRow}>
                        <div className={s.brushLabel}>{t("Placemaker.UI.Brush.Shape", "Shape")}</div>
                        <Segmented
                            testId="brush-shape"
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
                title={t("Placemaker.UI.Area.Selected", "Your area")}
                right={
                    a.pointCount > 0 ? (
                        <Btn
                            icon="trash"
                            iconSize={15}
                            variant="solid"
                            label={t("Placemaker.UI.Area.Clear", "Clear")}
                            onClick={act.clearArea}
                            testId="clear-area"
                        />
                    ) : undefined
                }
            >
                <div className={s.areaCard}>
                    {a.closed && a.areaM2 > 0 ? (
                        <div className={s.areaStats}>
                            <div className={s.areaStat}>
                                <div className={s.areaStatValue}>{fmtArea(a.areaM2)}</div>
                                <div className={s.areaStatLabel}>{t("Placemaker.UI.Area.Size", "size")}</div>
                            </div>
                            <div className={s.areaStat}>
                                <div className={s.areaStatValue}>{group(a.perimeterM) + " m"}</div>
                                <div className={s.areaStatLabel}>{t("Placemaker.UI.Area.Around", "around")}</div>
                            </div>
                            <div className={s.areaStat}>
                                <div className={s.areaStatValue}>{group(a.pointCount)}</div>
                                <div className={s.areaStatLabel}>{t("Placemaker.UI.Area.CornersLabel", "corners")}</div>
                            </div>
                        </div>
                    ) : null}
                    <div className={s.areaFoot}>
                        <Icon name={a.closed ? "check" : "info"} size={16} tint={a.closed ? "ok" : "accent"} />
                        <div className={s.areaHint} data-testid="area-hint">
                            {a.closed
                                ? t("Placemaker.UI.Area.Adjust", "Drag corners to adjust. You can change the shape at any time, even later.")
                                : HOWTO[state.areaMode]
                                  ? t(HOWTO[state.areaMode][0], HOWTO[state.areaMode][1])
                                  : ""}
                        </div>
                    </div>
                </div>
            </Section>
        </div>
    );
};
