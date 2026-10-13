// Browser stand-in for cs2/l10n: translate() over Lang/en-US*.json (repo files + /api/lang from the
// mock host), and a LocalizedNumber that formats like the game (¢ money, digit grouping).
import React, { useSyncExternalStore } from "react";
import uiStrings from "../../../Lang/en-US.ui.json";
import coreStrings from "../../../Lang/en-US.core.json";

let dict: Record<string, string> = { ...(coreStrings as Record<string, string>), ...(uiStrings as Record<string, string>) };
let version = 0;
const listeners = new Set<() => void>();

export function addStrings(more: Record<string, string>) {
    dict = { ...more, ...dict, ...(uiStrings as Record<string, string>) };
    // Layer-owned files (from the host) win for their own keys.
    for (const k of Object.keys(more)) if (!k.startsWith("Placemaker.UI.")) dict[k] = more[k];
    version++;
    listeners.forEach((l) => l());
}

/** Mock-only: show raw keys instead of English to spot missing localisation. */
export let showKeys = false;
export function setShowKeys(v: boolean) {
    showKeys = v;
    version++;
    listeners.forEach((l) => l());
}

const translate = (id: string, fallback?: string | null) => {
    if (showKeys) return id;
    const v = dict[id];
    return v !== undefined ? v : fallback ?? null;
};

export function useLocalization() {
    useSyncExternalStore(
        (l) => {
            listeners.add(l);
            return () => listeners.delete(l);
        },
        () => version,
    );
    return { translate, unitSettings: {} };
}

export const useCachedLocalization = useLocalization;

function group(n: number) {
    return Math.round(n)
        .toString()
        .replace(/\B(?=(\d{3})+(?!\d))/g, ",");
}

export const LocalizedNumber = ({ value, unit }: { value: number; unit?: string }) => {
    switch (unit) {
        case "money":
            return <>{"¢" + group(value)}</>;
        case "moneyPerMonth":
            return <>{"¢" + group(value) + "/month"}</>;
        default:
            return <>{group(value)}</>;
    }
};

export const Unit = { Money: "money", MoneyPerMonth: "moneyPerMonth", Integer: "integer" };
