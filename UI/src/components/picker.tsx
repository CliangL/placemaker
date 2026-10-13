// Asset picker: searchable grid with thumbnails, "Automatic" tile, recently used, multi-select.
// Opened by Asset / AssetMulti / Arrangement controls through PickerContext and rendered by the panel
// as a card stacked above it, so it is never clipped by the scrolling body.
import React, { createContext, useContext, useEffect, useMemo, useRef, useState } from "react";
import classNames from "classnames";
import { AssetItem } from "../contract";
import { act, useAssets } from "../lib/bindings";
import { useT } from "../lib/l10n";
import { compactMoney } from "../lib/format";
import { Btn, Icon } from "./common";
import s from "./picker.module.scss";

export interface PickerRequest {
    category: string;
    title: string;
    /** single: pick one id ("" = automatic). multi: toggle ids in a set. append: add one id (arrangement). */
    mode: "single" | "multi" | "append";
    selected: string[];
    onPick: (ids: string[]) => void;
    allowAutomatic: boolean;
}

export const PickerContext = createContext<{ open: (r: PickerRequest) => void; close: () => void; current: PickerRequest | null }>({
    open: () => undefined,
    close: () => undefined,
    current: null,
});

export const usePicker = () => useContext(PickerContext);

const RECENT_KEY = "placemaker.recentAssets";

function loadRecent(): Record<string, string[]> {
    try {
        const raw = window.localStorage?.getItem(RECENT_KEY);
        const v = raw ? JSON.parse(raw) : {};
        return v && typeof v === "object" ? v : {};
    } catch {
        return {};
    }
}

function pushRecent(category: string, id: string) {
    if (!id) return;
    try {
        const all = loadRecent();
        const list = [id, ...(all[category] ?? []).filter((x) => x !== id)].slice(0, 8);
        all[category] = list;
        window.localStorage?.setItem(RECENT_KEY, JSON.stringify(all));
    } catch {
        /* storage unavailable */
    }
}

/** Display name for an asset id, or the id itself until the list arrives. */
export function useAssetName() {
    const assets = useAssets();
    return (category: string | undefined, id: string) => assets[category ?? ""]?.find((a) => a.id === id)?.name ?? prettifyId(id);
}

export function prettifyId(id: string) {
    return id.replace(/[_]+/g, " ").replace(/([a-z])([A-Z])/g, "$1 $2").replace(/\s+\d+$/, "").trim();
}

export function useAssetList(category: string | undefined): AssetItem[] | undefined {
    const assets = useAssets();
    const requested = useRef("");
    const list = category ? assets[category] : undefined;
    useEffect(() => {
        if (category && !list && requested.current !== category) {
            requested.current = category;
            act.requestAssets(category);
        }
    }, [category, list]);
    return list;
}

export const Thumb = ({ item, size = 44 }: { item?: AssetItem; size?: number }) => {
    const style = { width: size + "rem", height: size + "rem" };
    if (item?.icon) return <img className={s.thumbImg} style={style} src={item.icon} />;
    const initials = (item?.name ?? "?")
        .split(/\s+/)
        .slice(0, 2)
        .map((w) => w.charAt(0))
        .join("")
        .toUpperCase();
    return (
        <div className={s.thumbBlank} style={style}>
            {initials}
        </div>
    );
};

export const AssetPickerCard = ({ req, onClose }: { req: PickerRequest; onClose: () => void }) => {
    const { t } = useT();
    const list = useAssetList(req.category);
    const [query, setQuery] = useState("");
    const [selected, setSelected] = useState<string[]>(req.selected);
    useEffect(() => setSelected(req.selected), [req]);
    const recentIds = useMemo(() => loadRecent()[req.category] ?? [], [req.category]);

    const q = query.trim().toLowerCase();
    const items = (list ?? []).filter((a) => !q || a.name.toLowerCase().includes(q) || a.id.toLowerCase().includes(q) || (a.tags ?? []).some((x) => x.toLowerCase().includes(q)));
    const recent = q ? [] : recentIds.map((id) => list?.find((a) => a.id === id)).filter((a): a is AssetItem => !!a);

    const pick = (id: string) => {
        if (req.mode === "multi") {
            const next = id === "" ? [] : selected.includes(id) ? selected.filter((x) => x !== id) : [...selected, id];
            setSelected(next);
            pushRecent(req.category, id);
            req.onPick(next);
            return;
        }
        pushRecent(req.category, id);
        req.onPick(id === "" ? [] : [id]);
        onClose();
    };

    const tile = (a: AssetItem, keyPrefix = "") => {
        const on = selected.includes(a.id);
        return (
            <button
                type="button"
                key={keyPrefix + a.id}
                data-testid={`asset-${a.id}`}
                className={classNames(s.tile, { [s.tileOn]: on })}
                onClick={(e) => {
                    e.stopPropagation();
                    pick(a.id);
                }}
            >
                <Thumb item={a} />
                <div className={s.tileName}>{a.name}</div>
                {a.cost ? <div className={s.tileCost}>{compactMoney(a.cost)}</div> : null}
                {on ? (
                    <div className={s.tileCheck}>
                        <Icon name="check" size={12} />
                    </div>
                ) : null}
            </button>
        );
    };

    return (
        <div className={s.card} data-testid="asset-picker" onMouseDown={(e) => e.stopPropagation()}>
            <div className={s.head}>
                <div className={s.title}>{req.title}</div>
                <div className={s.search}>
                    <Icon name="search" size={14} />
                    <input
                        className={s.searchInput}
                        data-testid="asset-search"
                        value={query}
                        placeholder={t("Placemaker.UI.Picker.Search", "Search")}
                        onChange={(e) => setQuery(e.target.value)}
                        onKeyDown={(e) => {
                            e.stopPropagation();
                            if (e.key === "Escape") onClose();
                        }}
                    />
                </div>
                <Btn icon="close" title={t("Placemaker.UI.Close", "Close")} onClick={onClose} testId="asset-picker-close" />
            </div>
            <div className={s.body}>
                {list === undefined ? (
                    <div className={s.empty}>{t("Placemaker.UI.Picker.Loading", "Loading assets…")}</div>
                ) : (
                    <>
                        {recent.length ? (
                            <>
                                <div className={s.section}>{t("Placemaker.UI.Picker.Recent", "Recently used")}</div>
                                <div className={s.grid}>{recent.map((a) => tile(a, "r-"))}</div>
                            </>
                        ) : null}
                        <div className={s.section}>
                            {q
                                ? t("Placemaker.UI.Picker.Results", "{0} results", [items.length])
                                : t("Placemaker.UI.Picker.All", "All ({0})", [items.length])}
                        </div>
                        <div className={s.grid}>
                            {req.allowAutomatic && !q ? (
                                <button
                                    type="button"
                                    data-testid="asset-automatic"
                                    className={classNames(s.tile, s.autoTile, { [s.tileOn]: selected.length === 0 })}
                                    onClick={(e) => {
                                        e.stopPropagation();
                                        pick("");
                                    }}
                                >
                                    <div className={s.autoIcon}>
                                        <Icon name="auto" size={24} />
                                    </div>
                                    <div className={s.tileName}>
                                        {req.mode === "multi" ? t("Placemaker.UI.Picker.AutoMix", "Automatic mix") : t("Placemaker.UI.Picker.Auto", "Automatic")}
                                    </div>
                                </button>
                            ) : null}
                            {items.map((a) => tile(a))}
                        </div>
                        {items.length === 0 ? <div className={s.empty}>{t("Placemaker.UI.Picker.None", "Nothing matches.")}</div> : null}
                    </>
                )}
            </div>
            {req.mode === "multi" ? (
                <div className={s.foot}>
                    <div className={s.footInfo}>
                        {selected.length
                            ? t("Placemaker.UI.Picker.SelectedN", "{0} selected", [selected.length])
                            : t("Placemaker.UI.Picker.AutoMixHint", "None selected: a fitting mix is chosen for you")}
                    </div>
                    <Btn variant="accent" label={t("Placemaker.UI.Done", "Done")} onClick={onClose} testId="asset-picker-done" />
                </div>
            ) : null}
        </div>
    );
};
