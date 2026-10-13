// Stage 1 — choose the area: Draw / Pick block / Paint / Road perimeter, brush settings, live readout.
import React from "react";
import { AreaMode, BrushShape, ToolState } from "../contract";
import { act } from "../lib/bindings";
import { area as fmtArea, group } from "../lib/format";
import { TFunc, useT } from "../lib/l10n";
import { Btn, Icon, Segmented } from "../components/common";
import { Slider } from "../controls/Slider";
import s from "./stages.module.scss";

export function modeOptions(t: TFunc) {
    return [
        {
            value: "draw",
            icon: "draw",
            label: t("Placemaker.UI.Mode.Draw", "Draw"),
            title: t("Placemaker.UI.Mode.Draw", "Draw"),
            description: t("Placemaker.UI.Mode.DrawTip", "Click corners on the map; click the first corner to close. Snaps to curbs, buildings and right angles. Drag corners or edges to adjust."),
        },
        {
            value: "block",
            icon: "block",
            label: t("Placemaker.UI.Mode.Block", "Pick block"),
            title: t("Placemaker.UI.Mode.Block", "Pick block"),
            description: t("Placemaker.UI.Mode.BlockTip", "Hover a city block and click to take it. Shift+click adds neighbouring blocks."),
        },
        {
            value: "paint",
            icon: "paint",
            label: t("Placemaker.UI.Mode.Paint", "Paint"),
            title: t("Placemaker.UI.Mode.Paint", "Paint"),
            description: t("Placemaker.UI.Mode.PaintTip", "Hold left mouse to paint the area, right mouse to erase."),
        },
        {
            value: "perimeter",
            icon: "perimeter",
            label: t("Placemaker.UI.Mode.Perimeter", "Road loop"),
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

export const AreaReadout =({ state, compact }: { state: ToolState; compact?: boolean }) => {
    const { t, tl } = useT();
    const a = state.area;
    if (a.problem) {
        return (
            <div className={s.readout} data-testid="area-readout">
                <Icon name="warning" size={16} color="#ffb547" />
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
            <Icon name="area" size={16} color="#8fd1ff" />
            <div className={s.readoutBig}>{fmtArea(a.areaM2)}</div>
            {!compact ? (
                <div className={s.readoutDim}>
                    {t("Placemaker.UI.Area.Perimeter", "{0} m around", [group(a.perimeterM)])}
                    {a.pointCount > 0 ? " · " + t("Placemaker.UI.Area.CornerCount", "{0} corners", [a.pointCount]) : ""}
                </div>
            ) : null}
        </div>
    );
};

export const AreaStage = ({ state }: { state: ToolState }) => {
    const { t } = useT();
    const b = state.brush;
    return (
        <div className={s.stage} data-testid="stage-area">
            <div className={s.sectionLabel}>{t("Placemaker.UI.Area.How", "How do you want to choose the area?")}</div>
            <Segmented
                testId="area-mode"
                stretch
                value={state.areaMode}
                onChange={(v) => act.setAreaMode(v as AreaMode)}
                options={modeOptions(t)}
            />
            {state.areaMode === "paint" ? (
                <div className={s.brush} data-testid="brush">
                    <div className={s.brushRow}>
                        <div className={s.brushLabel}>{t("Placemaker.UI.Brush.Shape", "Brush")}</div>
                        <Segmented
                            testId="brush-shape"
                            iconOnly
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
                </div>
            ) : null}
            <div className={s.areaFoot}>
                <Icon name={state.area.closed ? "check" : "info"} size={15} color={state.area.closed ? "#6fdc8c" : "#8fd1ff"} />
                <div className={s.areaHint} data-testid="area-hint">
                    {state.area.closed
                        ? t("Placemaker.UI.Area.Adjust", "Drag corners to adjust. You can change the shape at any time, even later.")
                        : HOWTO[state.areaMode]
                        ? t(HOWTO[state.areaMode][0], HOWTO[state.areaMode][1])
                        : ""}
                </div>
                {state.area.pointCount > 0 ? (
                    <Btn icon="trash" iconSize={15} label={t("Placemaker.UI.Area.Clear", "Clear")} onClick={act.clearArea} testId="clear-area" />
                ) : null}
            </div>
        </div>
    );
};
