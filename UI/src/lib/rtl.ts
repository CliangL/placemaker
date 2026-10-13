// Right-to-left detection (Arabic / Persian localisation mods mirror the game UI by filling their
// <style> element; the dir="rtl" attribute they set is not reliable in-game).
// Adapted from Grid Road Generator (Ti4goc).
import { useEffect, useState } from "react";

const RTL_STYLE_IDS = ["arabic-locale-rtl-style", "persian-localization-rtl-style"];

const isRtlActive = () => {
    try {
        return RTL_STYLE_IDS.some((id) => !!document.getElementById(id)?.textContent);
    } catch {
        return false;
    }
};

export function useRtl(): boolean {
    const [rtl, setRtl] = useState(isRtlActive);
    useEffect(() => {
        if (typeof MutationObserver === "undefined") return;
        const observer = new MutationObserver(() => setRtl(isRtlActive()));
        observer.observe(document.head, { childList: true, subtree: true, characterData: true });
        return () => observer.disconnect();
    }, []);
    return rtl;
}
