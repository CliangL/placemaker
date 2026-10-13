// Browser stand-in for cs2/ui: a hover Tooltip that looks like the game's bubble, and FloatingButton.
// ConfirmationDialog is deliberately absent so the panel's own fallback dialog is exercised.
import React, { cloneElement, ReactElement, ReactNode, useRef, useState } from "react";
import { createPortal } from "react-dom";

export const Tooltip = ({ tooltip, children, direction = "up" }: { tooltip: ReactNode; children: ReactElement; direction?: string; delayTime?: number }) => {
    const [rect, setRect] = useState<DOMRect | null>(null);
    const timer = useRef<number | null>(null);
    const child = cloneElement(children, {
        onMouseEnter: (e: React.MouseEvent) => {
            children.props.onMouseEnter?.(e);
            const r = (e.currentTarget as HTMLElement).getBoundingClientRect();
            if (timer.current) window.clearTimeout(timer.current);
            timer.current = window.setTimeout(() => setRect(r), 250);
        },
        onMouseLeave: (e: React.MouseEvent) => {
            children.props.onMouseLeave?.(e);
            if (timer.current) window.clearTimeout(timer.current);
            setRect(null);
        },
        onMouseDown: (e: React.MouseEvent) => {
            children.props.onMouseDown?.(e);
            if (timer.current) window.clearTimeout(timer.current);
            setRect(null);
        },
    });
    if (!rect) return child;
    const below = direction === "down";
    const style: React.CSSProperties = {
        position: "fixed",
        left: rect.left + rect.width / 2,
        top: below ? rect.bottom + 8 : rect.top - 8,
        transform: below ? "translateX(-50%)" : "translate(-50%, -100%)",
    };
    return (
        <>
            {child}
            {createPortal(
                <div className="mock-tooltip" style={style} data-testid="tooltip">
                    {tooltip}
                </div>,
                document.body,
            )}
        </>
    );
};

export const FloatingButton = ({ src, selected, onSelect, className }: { src: string; selected?: boolean; onSelect?: () => void; className?: string }) => (
    <button type="button" className={"mock-floating" + (selected ? " selected" : "") + (className ? " " + className : "")} onClick={onSelect}>
        <img src={src} alt="" />
    </button>
);

export const Button = FloatingButton;
