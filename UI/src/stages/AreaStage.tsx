// Stage 1 — choose the area (s42): four tools as one row of equal cards (icon tile + name + a short line):
//   Points (draw) · Paint bucket (block) · Paint area (paint + block paint: the "Smart edges" switch picks the
//   mode) · Road nodes (perimeter). The tool's settings sit under it, always visible (no folds), then one summary
//   line: Area · Edge · Corners · Clear. Mode ids and saved settings are unchanged (a saved "blockPaint" opens
//   Paint area with Smart edges on).
import React from "react";
import classNames from "classnames";
import { AreaMode, BrushRules, BrushShape, BucketWalls, LocText, ToolState } from "../contract";
import { act } from "../lib/bindings";
import { area as fmtArea, group } from "../lib/format";
import { TFunc, useT } from "../lib/l10n";
import { usePanelLayout } from "../lib/layout";
import { Btn, Icon, Segmented, Switch } from "../components/common";
import { Slider } from "../controls/Slider";
import { Tip } from "../lib/tips";
import { Tile } from "./PresetBrowser";
import s from "./area.module.scss";

type Tool = "draw" | "block" | "paint" | "perimeter";

/** The tool a mode belongs to (block paint and paint are one tool). */
export const toolOf = (m: AreaMode): Tool => (m === "blockPaint" || m === "paint" ? "paint" : m === "block" ? "block" : m === "perimeter" ? "perimeter" : "draw");

export function modeOptions(t: TFunc) {
    return [
        {
            value: "draw" as Tool,
            icon: "points",
            label: t("Placemaker.UI.Tool.Points", "Points"),
            line: t("Placemaker.UI.Tool.PointsLine", "Click corners"),
            description: t("Placemaker.UI.Tool.PointsTip", "Click corners, then the first one to close. Snaps to curbs and right angles."),
        },
        {
            value: "block" as Tool,
            icon: "bucket",
            label: t("Placemaker.UI.Tool.Bucket", "Paint bucket"),
            line: t("Placemaker.UI.Tool.BucketLine", "Click to fill"),
            description: t("Placemaker.UI.Tool.BucketTip", "Click a city block to fill it. Shift+click adds more."),
        },
        {
            value: "paint" as Tool,
            icon: "brush",
            label: t("Placemaker.UI.Tool.Paint", "Paint area"),
            line: t("Placemaker.UI.Tool.PaintLine", "Brush it on"),
            description: t("Placemaker.UI.Tool.PaintTip", "Brush the area on. Right-click erases."),
        },
        {
            value: "perimeter" as Tool,
            icon: "nodes",
            label: t("Placemaker.UI.Tool.Nodes", "Road nodes"),
            line: t("Placemaker.UI.Tool.NodesLine", "Click road corners"),
            description: t("Placemaker.UI.Tool.NodesTip", "Click road corners around the area; the roads between are followed."),
        },
    ];
}

/** A toggle chip: icon + word, accent outline when on. */
const ToggleChip = ({ icon, label, on, disabled, tip, onClick, testId }: { icon: string; label: string; on: boolean; disabled?: boolean; tip?: string; onClick: () => void; testId: string }) => (
    <Tip title={label} description={tip}>
        <button
            type="button"
            data-testid={testId}
            aria-pressed={on && !disabled ? "true" : "false"}
            aria-disabled={disabled ? "true" : "false"}
            className={classNames(s.chip, { [s.chipOn]: on && !disabled, [s.chipOff]: disabled })}
            onClick={(e) => {
                e.stopPropagation();
                if (!disabled) onClick();
            }}
        >
            <Icon name={icon} size={12} tint={on && !disabled ? "light" : "dim"} />
            <div className={s.chipText}>{label}</div>
        </button>
    </Tip>
);

const Row = ({ label, children, off, testId, tip }: { label: string; children: React.ReactNode; off?: boolean; testId?: string; tip?: string }) => (
    <div className={classNames(s.row, { [s.rowOff]: off })} data-testid={testId} data-disabled={off ? "true" : undefined}>
        <Tip title={label} description={tip}>
            <div className={s.label}>{label}</div>
        </Tip>
        <div className={s.rowBody}>{children}</div>
    </div>
);

type StopKey = "roads" | "buildings" | "builds" | "lots" | "zoned" | "allZoned" | "emptyZones" | "water";

/** Paint area settings: Smart edges + what the brush stops at, one block, snap, smooth; then the brush. */
const PaintSettings = ({ state, rules }: { state: ToolState; rules: BrushRules }) => {
    const { t, tl } = useT();
    // Smart edges = the block paint mode; a DLL without it greys the switch with the reason (s15).
    const smartAv = modeAvailability(state, "blockPaint", t, tl);
    const b = state.brush;
    const smart = state.areaMode === "blockPaint";
    const offWhy = t("Placemaker.UI.Why.SmartOff", "Smart edges is off");
    const stops: { key: StopKey; icon: string; label: string; tip: string }[] = [
        { key: "roads", icon: "roads", label: t("Placemaker.UI.Rules.Roads", "Roads"), tip: t("Placemaker.UI.Rules.RoadsTip", "Stop at the curb.") },
        { key: "buildings", icon: "building", label: t("Placemaker.UI.Rules.Buildings", "Buildings"), tip: t("Placemaker.UI.Rules.BuildingsTip", "Keep off building lots.") },
        { key: "builds", icon: "otherBuilds", label: t("Placemaker.UI.Rules.MyBuilds", "My builds"), tip: t("Placemaker.UI.Rules.BuildsTip", "Keep off what you built with Placemaker.") },
        { key: "lots", icon: "area", label: t("Placemaker.UI.Rules.Lots", "Lots"), tip: t("Placemaker.UI.Rules.LotsTip2", "Keep off other lots (other mods, extractors).") },
        { key: "zoned", icon: "edgeZoning", label: t("Placemaker.UI.Rules.Zoned", "Zoned"), tip: t("Placemaker.UI.Rules.ZonedTip2", "Keep off zoned cells that have a building. Empty zoned land can be painted.") },
        { key: "allZoned", icon: "edgeZoning", label: t("Placemaker.UI.Rules.AllZoned", "All zoned"), tip: t("Placemaker.UI.Rules.AllZonedTip", "Also keep off zoned cells nobody has built on yet.") },
        { key: "emptyZones", icon: "zoneEmpty", label: t("Placemaker.UI.Rules.EmptyZones", "Empty zones"), tip: t("Placemaker.UI.Rules.EmptyZonesTip", "Keep the empty zone grid free.") },
        { key: "water", icon: "water", label: t("Placemaker.UI.Rules.Water", "Water"), tip: t("Placemaker.UI.Rules.WaterTip", "Keep off deep water.") },
    ];
    const setSmart = (on: boolean) => {
        if (on && !smartAv.enabled) return;
        act.setBrushRules({ anything: !on });
        act.setAreaMode(on ? "blockPaint" : "paint");
    };
    return (
        <div className={s.settings} data-testid="brush">
            <div className={s.smartRow}>
                <Tip
                    title={t("Placemaker.UI.Rules.Smart", "Smart edges")}
                    description={t("Placemaker.UI.Rules.SmartTip", "On: the brush stops at what you tick below. Off: it paints over everything.")}
                >
                    <div className={s.smartLabel}>{t("Placemaker.UI.Rules.Smart", "Smart edges")}</div>
                </Tip>
                <Tip title={t("Placemaker.UI.Rules.Smart", "Smart edges")} description={smartAv.enabled ? undefined : smartAv.reason}>
                    <div className={smartAv.enabled ? s.switchCell : classNames(s.switchCell, s.rowOff)} data-testid="smart-edges-wrap" data-disabled={smartAv.enabled ? undefined : "true"} data-reason={smartAv.reason}>
                        <Switch on={smart} onChange={setSmart} testId="smart-edges" />
                    </div>
                </Tip>
                <div className={s.flex} />
                <Btn icon="reset" iconSize={11} title={t("Placemaker.UI.Rules.Reset", "Reset edges")} onClick={act.resetBrushRules} testId="brush-rules-reset" />
            </div>
            <Row label={t("Placemaker.UI.Rules.Stops", "Stops at")} off={!smart} testId="brush-rules">
                <div className={s.chips} data-testid="brush-rules-stops">
                    {stops.map((x) => (
                        <ToggleChip
                            key={x.key}
                            icon={x.icon}
                            label={x.label}
                            on={x.key === "allZoned" ? rules.allZoned === true : rules[x.key] !== false}
                            disabled={!smart}
                            tip={smart ? x.tip : offWhy}
                            onClick={() => act.setBrushRules({ [x.key]: !(x.key === "allZoned" ? rules.allZoned === true : rules[x.key] !== false) })}
                            testId={`brush-rule-${x.key}`}
                        />
                    ))}
                </div>
            </Row>
            <Row label={t("Placemaker.UI.Rules.Block", "One block")} off={!smart} tip={t("Placemaker.UI.Rules.BlockTip", "A stroke stays in the block where it started.")}>
                <div className={s.switchCell}>
                    <Switch on={smart && rules.stayInBlock !== false} onChange={(v) => smart && act.setBrushRules({ stayInBlock: v })} testId="brush-rule-stayInBlock" />
                </div>
            </Row>
            <Row label={t("Placemaker.UI.Rules.Snap", "Snap")} off={!smart} tip={t("Placemaker.UI.Rules.SnapTip", "Pulls painted edges this close onto a curb.")}>
                <Slider testId="brush-rule-snap" value={rules.snap} min={0} max={20} step={1} unit="m" onPreview={() => undefined} onCommit={(v) => smart && act.setBrushRules({ snap: v })} />
            </Row>
            <Row label={t("Placemaker.UI.Rules.Smooth", "Smooth")} off={!smart} tip={t("Placemaker.UI.Rules.SmoothTip", "How smooth the painted edge is.")}>
                <Slider testId="brush-rule-smoothing" value={rules.smoothing} min={0} max={6} step={1} onPreview={() => undefined} onCommit={(v) => smart && act.setBrushRules({ smoothing: v })} />
            </Row>
            <div className={s.rule} />
            <Row label={t("Placemaker.UI.Brush.Shape", "Shape")}>
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
            </Row>
            <Row label={t("Placemaker.UI.Brush.Size", "Size")}>
                <Slider testId="brush-size" value={b.size} min={5} max={200} step={1} unit="m" onPreview={(v) => act.setBrush(b.shape, v, b.angle)} onCommit={(v) => act.setBrush(b.shape, v, b.angle)} />
            </Row>
            {b.shape === "square" ? (
                <Row label={t("Placemaker.UI.Brush.Angle", "Angle")} testId="brush-angle-row">
                    <Slider testId="brush-angle" value={b.angle} min={0} max={90} step={1} unit="deg" onPreview={(v) => act.setBrush(b.shape, b.size, v)} onCommit={(v) => act.setBrush(b.shape, b.size, v)} />
                </Row>
            ) : null}
        </div>
    );
};

/**
 * (s43) Paint bucket: what the fill treats as a wall. Lines (roads, paths, bike paths, tracks) trace the block; areas
 * (water, lots & buildings, zoning) cut it and keep the side you clicked. Absent on DLLs before PROTOCOL 22.
 */
const BucketWallChips = ({ walls }: { walls: BucketWalls }) => {
    const { t } = useT();
    const chips: { key: keyof BucketWalls; icon: string; label: string; tip: string }[] = [
        { key: "roads", icon: "roads", label: t("Placemaker.UI.Walls.Roads", "Roads"), tip: t("Placemaker.UI.Walls.RoadsTip", "Every street and road, alleys and gravel too.") },
        { key: "paths", icon: "footpath", label: t("Placemaker.UI.Walls.Paths", "Paths"), tip: t("Placemaker.UI.Walls.PathsTip", "Footpaths split a block like streets do.") },
        { key: "bikes", icon: "bike", label: t("Placemaker.UI.Walls.Bikes", "Bike paths"), tip: t("Placemaker.UI.Walls.BikesTip", "Bike-only paths split a block too.") },
        { key: "tracks", icon: "track", label: t("Placemaker.UI.Walls.Tracks", "Rail & tram"), tip: t("Placemaker.UI.Walls.TracksTip", "Train and tram tracks on the ground.") },
        { key: "water", icon: "water", label: t("Placemaker.UI.Walls.Water", "Water"), tip: t("Placemaker.UI.Walls.WaterTip", "Stop at the water's edge.") },
        { key: "lots", icon: "building", label: t("Placemaker.UI.Walls.Lots", "Lots"), tip: t("Placemaker.UI.Walls.LotsTip", "Stop at buildings, other lots and your builds.") },
        { key: "zoned", icon: "edgeZoning", label: t("Placemaker.UI.Walls.Zoned", "Zoned"), tip: t("Placemaker.UI.Walls.ZonedTip", "Stop at zoned cells.") },
    ];
    return (
        <Row
            label={t("Placemaker.UI.Walls.Label", "Walls")}
            testId="bucket-walls"
            tip={t("Placemaker.UI.Walls.Tip", "What the fill stops at. Lines split blocks; areas keep the side you click.")}
        >
            <div className={s.chips} data-testid="bucket-walls-chips">
                {chips.map((x) => (
                    <ToggleChip
                        key={x.key}
                        icon={x.icon}
                        label={x.label}
                        on={!!walls[x.key]}
                        tip={x.tip}
                        onClick={() => act.setBrushRules({ walls: { [x.key]: !walls[x.key] } })}
                        testId={`bucket-wall-${x.key}`}
                    />
                ))}
            </div>
        </Row>
    );
};

/** Points / Paint bucket / Road nodes: keep the outline off my builds, optionally clip it to free land. */
const KeepOff = ({ rules, bucket }: { rules: BrushRules; bucket?: boolean }) => {
    const { t } = useT();
    const builds = rules.builds !== false;
    const clip = !!rules.drawClip;
    return (
        <div className={s.settings} data-testid="outline-rules">
            {bucket && rules.walls ? <BucketWallChips walls={rules.walls} /> : null}
            <Row label={t("Placemaker.UI.Rules.KeepOff", "Keep off")} testId="keep-off">
                <div className={s.chips}>
                    <ToggleChip
                        icon="otherBuilds"
                        label={t("Placemaker.UI.Rules.MyBuilds", "My builds")}
                        on={builds}
                        tip={t("Placemaker.UI.Rules.BuildsCutTip", "Cut the outline back where it covers what you built.")}
                        onClick={() => act.setBrushRules({ builds: !builds })}
                        testId="keep-off-builds"
                    />
                    <ToggleChip
                        icon="area"
                        label={t("Placemaker.UI.Rules.FreeLand", "Clip to free land")}
                        on={clip}
                        tip={t("Placemaker.UI.Rules.FreeLandTip", "Also cut it back from buildings, lots, zoning and water.")}
                        onClick={() => act.setBrushRules({ drawClip: !clip })}
                        testId="keep-off-free-land"
                    />
                </div>
            </Row>
        </div>
    );
};

const HOWTO: Record<Tool, [string, string]> = {
    draw: ["Placemaker.UI.Area.HowDraw", "Click corners, then the first one"],
    block: ["Placemaker.UI.Area.HowBlock", "Click a block to fill it"],
    paint: ["Placemaker.UI.Area.HowPaint", "Drag to paint, right-drag erases"],
    perimeter: ["Placemaker.UI.Area.HowPerimeter", "Click road corners around it"],
};

/**
 * (s15) Can this mode be used? The DLL lists the modes it handles (state.areaModes). An older DLL sends no list:
 * Block paint arrived together with the brush rules (s11), so without rules the DLL cannot do it — greyed with
 * "restart" instead of silently doing nothing (the UI file reloads without a restart, the DLL does not).
 */
export function modeAvailability(state: ToolState, mode: string, t: TFunc, tl: (l: LocText | undefined, f?: string) => string): { enabled: boolean; reason?: string } {
    const restart = t("Placemaker.UI.Why.RestartForMode", "Restart the game to use this: Placemaker was updated while the game was running.");
    if (state.areaModes) {
        const m = state.areaModes.find((x) => x.mode === mode);
        if (!m) return { enabled: false, reason: restart };
        return m.enabled ? { enabled: true } : { enabled: false, reason: tl(m.reason, t("Placemaker.UI.Why.NotNow", "Not available with the current settings")) };
    }
    if (mode === "blockPaint" && !state.brush.rules) return { enabled: false, reason: restart };
    return { enabled: true };
}

export const AreaStage = ({ state }: { state: ToolState }) => {
    const { t, tl } = useT();
    const layout = usePanelLayout();
    const rules = state.brush.rules;
    const a = state.area;
    const tool = toolOf(state.areaMode);
    // Paint area opens in Smart edges (block paint) unless the DLL cannot, then plain paint.
    const paintMode = (): AreaMode => (modeAvailability(state, "blockPaint", t, tl).enabled && !rules?.anything ? "blockPaint" : "paint");
    const cols = layout.width >= 480 ? 4 : 2;
    return (
        <div className={s.stage} data-testid="stage-area">
            <div className={s.tools} data-testid="area-mode">
                {modeOptions(t).map((o) => {
                    const on = o.value === tool;
                    const mode: AreaMode = o.value === "paint" ? (on ? state.areaMode : paintMode()) : o.value;
                    const av = modeAvailability(state, o.value === "paint" ? "paint" : o.value, t, tl);
                    return (
                        <div key={o.value} className={s.toolCell} style={{ width: 100 / cols + "%" }}>
                            <Tip title={o.label} description={av.enabled ? o.description : av.reason + " — " + o.description}>
                                <button
                                    type="button"
                                    data-testid={`area-mode-${o.value}`}
                                    aria-pressed={on ? "true" : "false"}
                                    aria-disabled={av.enabled ? undefined : "true"}
                                    data-reason={av.enabled ? undefined : av.reason}
                                    className={classNames(s.tool, { [s.toolOn]: on, [s.toolOff]: !av.enabled && !on })}
                                    onClick={(e) => {
                                        e.stopPropagation();
                                        if (!on && av.enabled) act.setAreaMode(mode);
                                    }}
                                >
                                    <Tile icon={o.icon} color={on ? "#8fd1ff" : "#9fb0b9"} size={20} />
                                    <div className={s.toolText}>
                                        <div className={s.toolName}>{o.label}</div>
                                        <div className={s.toolLine}>{o.line}</div>
                                    </div>
                                </button>
                            </Tip>
                        </div>
                    );
                })}
            </div>
            {tool === "paint" && rules ? <PaintSettings state={state} rules={rules} /> : null}
            {tool === "paint" && !rules ? (
                <div className={s.settings} data-testid="brush">
                    <Row label={t("Placemaker.UI.Brush.Size", "Size")}>
                        <Slider testId="brush-size" value={state.brush.size} min={5} max={200} step={1} unit="m" onPreview={(v) => act.setBrush(state.brush.shape, v, state.brush.angle)} onCommit={(v) => act.setBrush(state.brush.shape, v, state.brush.angle)} />
                    </Row>
                </div>
            ) : null}
            {tool !== "paint" && rules ? <KeepOff rules={rules} bucket={tool === "block"} /> : null}
            <div className={s.summary} data-testid="area-summary">
                {a.closed && a.areaM2 > 0 ? (
                    <>
                        <div className={s.sumLabel}>{t("Placemaker.UI.Area.Selected", "Area")}</div>
                        <div className={s.sumValue}>{fmtArea(a.areaM2)}</div>
                        <div className={s.sumLabel}>{t("Placemaker.UI.Area.Around", "Edge")}</div>
                        <div className={s.sumValue}>{group(a.perimeterM) + " m"}</div>
                        <div className={s.sumLabel}>{t("Placemaker.UI.Area.CornersLabel", "Corners")}</div>
                        <div className={s.sumValue}>{group(a.pointCount)}</div>
                        <div className={s.sumHint} data-testid="area-hint">
                            {t("Placemaker.UI.Area.Adjust", "drag corners anytime")}
                        </div>
                    </>
                ) : (
                    <>
                        <Icon name="info" size={12} tint="accent" />
                        <div className={s.sumHint} data-testid="area-hint">
                            {t(HOWTO[tool][0], HOWTO[tool][1])}
                        </div>
                    </>
                )}
                <div className={s.flex} />
                {a.pointCount > 0 || a.closed ? <Btn icon="trash" iconSize={11} label={t("Placemaker.UI.Area.Clear", "Clear")} onClick={act.clearArea} testId="clear-area" className={s.clear} /> : null}
            </div>
        </div>
    );
};
