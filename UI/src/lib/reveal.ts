// "Show me where": the footer asks a section of the Design stage to scroll into view, open and flash
// (FEEDBACK-4b: pressing the disabled "Add at least one entrance" button leads to the Entrances section).
// A tiny pub/sub so the footer need not know the panel's component tree.
import { useEffect, useState } from "react";

type Listener = (target: string) => void;
const listeners = new Set<Listener>();
let pending: { target: string; at: number } | null = null;

/** Ask the section registered under `target` (e.g. "entrances:0") to reveal itself. */
export function requestReveal(target: string) {
    pending = { target, at: Date.now() };
    listeners.forEach((l) => l(target));
}

/**
 * For the section with key `target`: returns true while it should be highlighted, and calls `onReveal`
 * (open it, scroll to it) when asked. A request made just before the section mounted (stage switch)
 * is honoured on mount.
 */
export function useReveal(target: string, onReveal: () => void, ms = 1600): boolean {
    const [flash, setFlash] = useState(false);
    useEffect(() => {
        let timer: number | undefined;
        const fire = () => {
            onReveal();
            setFlash(true);
            if (timer) window.clearTimeout(timer);
            timer = window.setTimeout(() => setFlash(false), ms);
        };
        const l: Listener = (t) => {
            if (t === target) fire();
        };
        listeners.add(l);
        if (pending && pending.target === target && Date.now() - pending.at < 1000) {
            pending = null;
            fire();
        }
        return () => {
            listeners.delete(l);
            if (timer) window.clearTimeout(timer);
        };
        // onReveal is recreated every render; the target is what matters.
        // eslint-disable-next-line react-hooks/exhaustive-deps
    }, [target]);
    return flash;
}

/** Scrolls the nearest scrollable ancestor so `el` is at the top (Gameface has no scrollIntoView options). */
export function scrollToTop(el: HTMLElement | null) {
    if (!el) return;
    for (let p = el.parentElement; p; p = p.parentElement) {
        if (p.scrollHeight > p.clientHeight + 1) {
            const oy = getComputedStyle(p).overflowY;
            if (oy !== "auto" && oy !== "scroll") continue;
            p.scrollTop += el.getBoundingClientRect().top - p.getBoundingClientRect().top - 6;
            return;
        }
    }
}
