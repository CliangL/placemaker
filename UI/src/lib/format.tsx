// Number formatting. Where the game has a matching unit we render vanilla LocalizedNumber so money,
// lengths and areas look exactly like the rest of the game (currency sign, digit grouping, metric /
// imperial). Without it (missing at runtime, or the browser mock) we fall back to plain formatting.
import React from "react";
import * as l10n from "cs2/l10n";

const VanillaNumber: any = (l10n as any)?.LocalizedNumber;

export function group(n: number, decimals = 0): string {
    const neg = n < 0;
    const fixed = Math.abs(n).toFixed(decimals);
    const [int, frac] = fixed.split(".");
    let out = "";
    for (let i = 0; i < int.length; i++) {
        if (i > 0 && (int.length - i) % 3 === 0) out += ",";
        out += int[i];
    }
    return (neg ? "-" : "") + out + (frac ? "." + frac : "");
}

export function decimalsFor(step?: number): number {
    if (!step || step >= 1) return 0;
    const s = String(step);
    const i = s.indexOf(".");
    return i < 0 ? 0 : Math.min(3, s.length - i - 1);
}

export function money(v: number): string {
    return "¢" + group(Math.round(v));
}

export function compactMoney(v: number): string {
    const a = Math.abs(v);
    if (a >= 1e6) return "¢" + (v / 1e6).toFixed(a >= 1e7 ? 0 : 1) + "M";
    if (a >= 1e4) return "¢" + (v / 1e3).toFixed(0) + "k";
    return money(v);
}

export function area(m2: number): string {
    if (m2 >= 10000) return (m2 / 10000).toFixed(m2 >= 100000 ? 1 : 2) + " ha";
    return group(Math.round(m2)) + " m²";
}

/** Plain-text value with unit (sliders, chips, tooltips). */
export function formatValue(v: number, unit?: string, step?: number): string {
    const d = decimalsFor(step);
    switch (unit) {
        case "m":
            return group(v, d) + " m";
        case "m2":
        case "area":
            return area(v);
        case "%":
            return group(v, d) + "%";
        case "deg":
            return group(v, d) + "°";
        case "money":
            return money(v);
        case "moneyPerMonth":
            return money(v) + "/mo";
        case "count":
            return group(Math.round(v));
        case "ha":
            return group(v, 2) + " ha";
        case "perHa":
            return group(v, d) + " /ha";
        case "per100m":
            return group(v, d) + " /100 m";
        case "perKm":
            return group(v, d) + " /km";
        case "s":
            return group(v, d) + " s";
        default:
            return group(v, d) + (unit ? " " + unit : "");
    }
}

const GAME_UNITS: Record<string, string> = {
    money: "money",
    moneyPerMonth: "moneyPerMonth",
    count: "integer",
};

/** Number with the game's own formatting where possible. */
export const Num = ({ value, unit, step }: { value: number; unit?: string; step?: number }) => {
    const gameUnit = unit ? GAME_UNITS[unit] : undefined;
    if (VanillaNumber && gameUnit) {
        try {
            return <VanillaNumber value={value} unit={gameUnit} />;
        } catch {
            /* fall through */
        }
    }
    return <>{formatValue(value, unit, step)}</>;
};

export const Money = ({ value, perMonth }: { value: number; perMonth?: boolean }) => (
    <Num value={Math.round(value)} unit={perMonth ? "moneyPerMonth" : "money"} />
);
