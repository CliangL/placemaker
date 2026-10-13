// Numeric slider. Dragging sends previewValue (live, throttled to one per frame) and commits once with
// setValue on release, so the C# side regenerates the preview cheaply while dragging and records a
// single undo step. Uses the vanilla game slider when it resolved (native look, sounds, gamepad),
// otherwise a light custom one with the same behaviour.
import React, { useEffect, useRef, useState } from "react";
import { VC, VF } from "../lib/vanilla";
import { formatValue } from "../lib/format";
import { playSound } from "../lib/bindings";
import s from "./controls.module.scss";

type Props = {
    value: number;
    min: number;
    max: number;
    step: number;
    unit?: string;
    onPreview: (v: number) => void;
    onCommit: (v: number) => void;
    onEndPreview?: () => void;
    testId?: string;
};

export function snap(raw: number, min: number, max: number, step: number) {
    const st = step > 0 ? step : 1;
    const v = Math.round((raw - min) / st) * st + min;
    const clamped = v < min ? min : v > max ? max : v;
    return Number(clamped.toFixed(6));
}

export const Slider = (p: Props) => {
    const { value, min, max, step, unit, testId } = p;
    const [drag, setDrag] = useState<number | null>(null);
    const dragRef = useRef<number | null>(null);
    const trackRef = useRef<HTMLDivElement>(null);
    const frame = useRef<number | null>(null);
    const shown = drag ?? value;
    const range = max - min || 1;
    const pct = ((shown - min) / range) * 100;
    const pctClamped = pct < 0 ? 0 : pct > 100 ? 100 : pct;

    const sendPreview = (v: number) => {
        dragRef.current = v;
        if (frame.current != null) return;
        frame.current = requestAnimationFrame(() => {
            frame.current = null;
            if (dragRef.current != null) p.onPreview(dragRef.current);
        });
    };

    useEffect(() => () => {
        if (frame.current != null) cancelAnimationFrame(frame.current);
    }, []);

    const label = <div className={s.sliderValue} data-testid={testId ? testId + "-value" : undefined}>{formatValue(shown, unit, step)}</div>;

    if (VC.Slider) {
        return (
            <div className={s.sliderWrap} data-testid={testId}>
                <div className={s.vanillaSlider}>
                    <VC.Slider
                        focusKey={VF.FOCUS_DISABLED}
                        value={shown}
                        start={min}
                        end={max}
                        gamepadStep={step}
                        valueTransformer={(a: number, b: number, ratio: number) => snap(a + ratio * (b - a), min, max, step)}
                        onDragStart={() => {
                            dragRef.current = value;
                        }}
                        onChange={(v: number) => {
                            if (dragRef.current != null) {
                                setDrag(v);
                                sendPreview(v);
                            } else p.onCommit(v);
                        }}
                        onDragEnd={() => {
                            const v = dragRef.current;
                            dragRef.current = null;
                            setDrag(null);
                            if (v != null && v !== value) p.onCommit(v);
                            p.onEndPreview?.();
                        }}
                    />
                </div>
                {label}
            </div>
        );
    }

    const fromX = (clientX: number) => {
        const r = trackRef.current?.getBoundingClientRect();
        if (!r || r.width <= 0) return shown;
        const ratio = (clientX - r.left) / r.width;
        return snap(min + (ratio < 0 ? 0 : ratio > 1 ? 1 : ratio) * range, min, max, step);
    };

    const onDown = (e: React.MouseEvent) => {
        if (e.button !== 0) return;
        e.preventDefault();
        e.stopPropagation();
        const v0 = fromX(e.clientX);
        setDrag(v0);
        sendPreview(v0);
        playSound("drag-slider");
        const move = (ev: MouseEvent) => {
            const v = fromX(ev.clientX);
            setDrag(v);
            sendPreview(v);
        };
        const up = (ev: MouseEvent) => {
            window.removeEventListener("mousemove", move);
            window.removeEventListener("mouseup", up);
            const v = fromX(ev.clientX);
            dragRef.current = null;
            setDrag(null);
            if (v !== value) p.onCommit(v);
            p.onEndPreview?.();
        };
        window.addEventListener("mousemove", move);
        window.addEventListener("mouseup", up);
    };

    const nudge = (dir: number) => {
        const v = snap(value + dir * (step || 1), min, max, step);
        if (v !== value) p.onCommit(v);
    };

    return (
        <div className={s.sliderWrap} data-testid={testId}>
            <div
                className={s.track}
                ref={trackRef}
                onMouseDown={onDown}
                onWheel={(e) => {
                    e.stopPropagation();
                    nudge(e.deltaY < 0 ? 1 : -1);
                }}
            >
                <div className={s.rail}>
                    <div className={s.fill} style={{ width: pctClamped + "%" }} />
                </div>
                <div className={s.thumb} style={{ left: pctClamped + "%" }} />
            </div>
            {label}
        </div>
    );
};
