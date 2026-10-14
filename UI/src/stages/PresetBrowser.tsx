// One browser for "what to make", as grids (playtest: "more vertically space conscious … why does it not go
// into a grid?"), plus the active-preset bar (Save / Save as / rename / delete / reset / export).
//   • Categories: a wrapping chip strip ("All" + the groups types declare, RecipeInfo.group, else guessed
//     from the id). Small catalogs start on "All"; the choice is kept for the session. A dot marks the
//     category holding the selected type.
//   • Types of the chosen category: a tile grid (icon + name, as many columns as fit, balanced rows).
//     Click = selectRecipe (the type with its default settings).
//   • Presets of the selected type: wrapping chips: "Default", built-ins ("edited" when saved over),
//     player presets (★). Click = selectPreset(layer, preset).
//   • Search filters categories / types / presets by name (preset hits of every type, with their type
//     icon); arrow keys walk the grid, Enter in the search box picks the first match, Esc clears it.
import React, { KeyboardEvent, useRef, useState } from "react";
import classNames from "classnames";
import { ActivePreset, Catalog, DesignState, LayerType, PresetView, RecipeInfo, StepView } from "../contract";
import { act } from "../lib/bindings";
import { entityIcon, layerColor } from "../lib/icons";
import { humanize, TFunc, useT } from "../lib/l10n";
import { Tip } from "../lib/tips";
import { BODY_PAD_REM, tileColumns, usePanelLayout } from "../lib/layout";
import { Btn, Icon } from "../components/common";
import { Confirm } from "../components/dialog";
import s from "./browser.module.scss";

// ───────────── types & groups ─────────────

/**
 * A recipe is a "mix" when its id names more than one layer ("roads-with-parks", "split-park-plaza",
 * "park-with-playground"); a single layer or a variant of one ("parking", "parking-green") is a type.
 */
export function isMix(catalog: Catalog, r: RecipeInfo) {
    if (r.custom) return true;
    const ids = new Set(catalog.layers.map((l) => l.id.toLowerCase()));
    const named = r.id
        .toLowerCase()
        .split(/[-_+|]+/)
        .filter((tok) => ids.has(tok) || (tok.endsWith("s") && ids.has(tok.slice(0, -1))));
    return named.length > 1;
}

/**
 * Types: recipes that ARE a single fill type (id equals a visible fill layer's id). Mixes and player
 * recipes are shelved in the UI. Falls back to the id heuristic when no recipe matches a layer id.
 */
export function makeRecipes(catalog: Catalog): RecipeInfo[] {
    const fills = new Set(catalog.layers.filter((l) => l.kind === "fill" && !l.hidden).map((l) => l.id.toLowerCase()));
    const singles = catalog.recipes.filter((r) => !r.custom && fills.has(r.id.toLowerCase()));
    return singles.length ? singles : catalog.recipes.filter((r) => !isMix(catalog, r));
}

/** Category order of the strip and tiles: Roads first (playtest), Parking and Commercial (s14) last before Other. */
export const MAKE_GROUPS: { id: string; key: string; en: string }[] = [
    { id: "roads", key: "Placemaker.UI.MakeGroup.Roads", en: "Roads" },
    { id: "parks", key: "Placemaker.UI.MakeGroup.Parks", en: "Parks" },
    { id: "gardens", key: "Placemaker.UI.MakeGroup.Gardens", en: "Gardens" },
    { id: "squares", key: "Placemaker.UI.MakeGroup.Squares", en: "Squares" },
    { id: "sports", key: "Placemaker.UI.MakeGroup.Sports", en: "Sports" },
    { id: "water", key: "Placemaker.UI.MakeGroup.Water", en: "Water" },
    { id: "nature", key: "Placemaker.UI.MakeGroup.Nature", en: "Nature" },
    { id: "farms", key: "Placemaker.UI.MakeGroup.Farms", en: "Farms" },
    { id: "events", key: "Placemaker.UI.MakeGroup.Events", en: "Events" },
    { id: "industrial", key: "Placemaker.UI.MakeGroup.Industrial", en: "Industrial" },
    { id: "transport", key: "Placemaker.UI.MakeGroup.Transport", en: "Transport" },
    { id: "parking", key: "Placemaker.UI.MakeGroup.Parking", en: "Parking" },
    { id: "commercial", key: "Placemaker.UI.MakeGroup.Commercial", en: "Commercial" },
    { id: "other", key: "Placemaker.UI.MakeGroup.Other", en: "Other" },
];

/** Group of a recipe: the group its type declares, else guessed from the id. */
export function recipeGroup(r: RecipeInfo): string {
    if (r.group && MAKE_GROUPS.some((g) => g.id === r.group)) return r.group;
    return makeGroupOf(r.id);
}

/** Group of a type id (checked in this order: "parking" before "park", "market square" before parks). */
export function makeGroupOf(id: string): string {
    const x = id.toLowerCase();
    if (/mall|retail|commercial|shopping/.test(x)) return "commercial";
    if (/parking|car-?park|garage|lot/.test(x)) return "parking";
    if (/road|street|avenue|grid/.test(x)) return "roads";
    if (/plaza|square|market|piazza|amphi|festival|fair/.test(x)) return "squares";
    if (/park|garden|camp|reserve|nature|forest|picnic|arboret|cemetery|green|playground|sport|golf|beach|lido|promenade|allotment|meadow|zoo|dog|botanic|skate/.test(x)) return "parks";
    return "other";
}

/** Up to this many types: the browser starts on "All". More: on the selected type's category. */
const SMALL_CATALOG = 8;

function typeLayer(catalog: Catalog, r: RecipeInfo): LayerType | undefined {
    const id = r.id.toLowerCase();
    return catalog.layers.find((l) => l.id.toLowerCase() === id);
}

export const presetName = (t: TFunc, p: PresetView) => (p.custom ? p.name : t(p.name, humanize(p.name || p.id)));
const recipeName = (t: TFunc, r: RecipeInfo) => (r.custom ? r.name : t(r.name, humanize(r.name || r.id)));
const DEFAULT_ID = "";

/** "Park · Formal garden" — what the folded Make section reads. */
export function makeSummary(t: TFunc, catalog: Catalog, design: DesignState): string {
    const r = catalog.recipes.find((x) => x.id === design.recipeId);
    if (!r) return "";
    const ap = design.activePreset;
    const layer = typeLayer(catalog, r);
    const p = ap && ap.id && layer ? layer.presets.find((x) => x.id === ap.id) : undefined;
    return p ? recipeName(t, r) + " · " + presetName(t, p) : recipeName(t, r);
}

// ───────────── grid browser ─────────────

/** Category chosen in the strip ("all" = every type), remembered; none chosen yet → a default. */
const CATEGORY_KEY = "placemaker.browserCategory.v1";
const ALL = "all";

function loadCategory(): string | null {
    try {
        return window.localStorage?.getItem(CATEGORY_KEY) || null;
    } catch {
        return null;
    }
}

function saveCategory(c: string) {
    try {
        window.localStorage?.setItem(CATEGORY_KEY, c);
    } catch {
        /* not remembered */
    }
}
/** Estimated tile width in rem for a name (icon, paddings, ~7.4rem per character at 13rem). */
const tileRem = (name: string) => 40 + name.length * 7.4;

/** Badges after a preset name: "edited" for saved-over built-ins, ★ for the player's own. */
const PresetBadges = ({ p }: { p: PresetView }) => {
    const { t } = useT();
    if (p.custom) return <Icon name="star" size={11} tint="warn" className={s.star} />;
    if (p.overridden)
        return (
            <div className={s.badge} data-testid={`preset-edited-${p.id}`}>
                {t("Placemaker.UI.Preset.Edited", "edited")}
            </div>
        );
    return null;
};

/** One preset as a chip: [name ★|edited •]. `typeIcon` marks whose preset it is in search results. */
const PresetChip = ({
    layer, recipe, p, active, modified, typeIcon,
}: { layer: LayerType; recipe: RecipeInfo; p: PresetView | null; active: boolean; modified: boolean; typeIcon?: boolean }) => {
    const { t } = useT();
    const name = p ? presetName(t, p) : t("Placemaker.UI.Preset.Default", "Default");
    const id = p ? p.id : DEFAULT_ID;
    const desc = p?.custom && p.basedOn ? t("Placemaker.UI.Preset.BasedOn", "Your preset, based on {0}", [humanize(p.basedOn)]) : undefined;
    return (
        <Tip title={typeIcon ? recipeName(t, recipe) + " · " + name : name} description={desc}>
            <button
                type="button"
                data-nav="preset"
                data-testid={p ? `preset-${p.id}` : "preset-default"}
                data-layer={layer.id}
                aria-pressed={active ? "true" : "false"}
                className={classNames(s.chip, s.presetChip, { [s.chipOn]: active, [s.chipDefault]: !p })}
                onClick={(e) => {
                    e.stopPropagation();
                    if (active && !modified) return;
                    if (p) act.selectPreset(layer.id, id);
                    else act.selectRecipe(recipe.id);
                }}
            >
                {typeIcon ? (
                    <div className={s.chipType} style={{ backgroundColor: layerColor(recipe.id) }}>
                        <Icon name={entityIcon(recipe.id, recipe.icon)} size={9} tint="dark" />
                    </div>
                ) : null}
                <div className={s.chipName} data-ellipsis>
                    {name}
                </div>
                {p ? <PresetBadges p={p} /> : null}
                {active && modified ? <div className={s.dot} /> : null}
            </button>
        </Tip>
    );
};

/** A type tile: colour-coded icon + name. Click = selectRecipe (the type with its default settings). */
const TypeTile = ({ catalog, recipe, selected, width }: { catalog: Catalog; recipe: RecipeInfo; selected: boolean; width: string | null }) => {
    const { t } = useT();
    const layer = typeLayer(catalog, recipe);
    const name = recipeName(t, recipe);
    const desc = recipe.description ? t(recipe.description, "") : "";
    const count = layer ? layer.presets.length : 0;
    return (
        <div className={classNames(s.tileCell, { [s.tileGrow]: !width })} style={width ? { width } : undefined} data-testid={`type-${recipe.id}`}>
            <Tip
                title={name}
                description={[desc, count ? t("Placemaker.UI.Browser.PresetCount", "{0} presets", [count]) : ""].filter((x) => x).join("\n") || undefined}
            >
                <button
                    type="button"
                    data-nav="type"
                    data-testid={`recipe-${recipe.id}`}
                    aria-pressed={selected ? "true" : "false"}
                    className={classNames(s.tile, { [s.tileOn]: selected })}
                    onClick={(e) => {
                        e.stopPropagation();
                        if (!selected) act.selectRecipe(recipe.id);
                    }}
                >
                    <div className={s.typeIcon} style={{ backgroundColor: layerColor(recipe.id) }}>
                        <Icon name={entityIcon(recipe.id, recipe.icon)} size={11} tint="dark" />
                    </div>
                    <div className={s.tileName} data-ellipsis>
                        {name}
                    </div>
                </button>
            </Tip>
        </div>
    );
};

type CatView = { id: string; title: string; types: RecipeInfo[]; hasSelected: boolean };

/**
 * Category strip (chips, wrapping) ▸ type tiles of the chosen category (grid) ▸ preset chips of the
 * selected type (wrapping). Search filters all three: matching categories / types as tiles, matching
 * presets of any type as chips (with their type's icon).
 */
export const TypeBrowser = ({ catalog, design }: { catalog: Catalog; design: DesignState }) => {
    const { t } = useT();
    const layout = usePanelLayout();
    const [query, setQuery] = useState("");
    const list = makeRecipes(catalog);
    const small = list.length <= SMALL_CATALOG;
    const selectedRecipe = list.find((r) => r.id === design.recipeId);
    // Not chosen yet: small catalogs show everything, big ones the selected type's category.
    const [chosen, setChosen] = useState<string | null>(loadCategory);
    const category = chosen ?? (small || !selectedRecipe ? ALL : recipeGroup(selectedRecipe));
    const setCategory = (c: string) => {
        saveCategory(c);
        setChosen(c);
    };
    const root = useRef<HTMLDivElement>(null);
    const q = query.trim().toLowerCase();
    const has = (text: string) => text.toLowerCase().includes(q);

    // categories → their types (search: the types that match by category, name or one of their presets)
    const cats: CatView[] = [];
    const presetHits: { r: RecipeInfo; layer: LayerType; p: PresetView }[] = [];
    let firstType: { recipe: string; layer: string } | null = null;
    for (const g of MAKE_GROUPS) {
        const members = list.filter((r) => recipeGroup(r) === g.id);
        if (!members.length) continue;
        const title = t(g.key, g.en);
        const catHit = !q || has(title);
        const types: RecipeInfo[] = [];
        for (const r of members) {
            const layer = typeLayer(catalog, r);
            const hits = q && layer ? layer.presets.filter((p) => has(presetName(t, p))) : [];
            if (layer) for (const p of hits) presetHits.push({ r, layer, p });
            if (catHit || has(recipeName(t, r)) || hits.length) {
                types.push(r);
                if (q && !firstType && (catHit || has(recipeName(t, r)))) firstType = { recipe: r.id, layer: layer?.id ?? r.id };
            }
        }
        if (types.length) cats.push({ id: g.id, title, types, hasSelected: members.some((r) => r.id === design.recipeId) });
    }
    // The chosen category, unless the search left it empty (then everything that matched).
    const current = category !== ALL && cats.some((c) => c.id === category) ? category : ALL;
    const tiles = current === ALL ? cats.flatMap((c) => c.types) : cats.find((c) => c.id === current)!.types;
    const firstHit = presetHits.length ? { recipe: presetHits[0].r.id, layer: presetHits[0].layer.id, preset: presetHits[0].p.id } : firstType;

    // Tiles: one row sharing the width when every name fits; else a grid of equal columns, as many as
    // the longest name allows, balanced rows — names are never cut.
    const inner = layout.width - BODY_PAD_REM - 4;
    const widths = tiles.map((r) => tileRem(recipeName(t, r)) + 4);
    const oneRow = tiles.length >= 3 && widths.reduce((n, w) => n + w, 0) <= inner;
    // one or two tiles (a narrow category, a search hit) keep a third of the row each, not all of it
    const cols = oneRow ? tiles.length : Math.max(tiles.length < 3 ? 3 : 1, tileColumns(tiles.length, inner, Math.max(96, ...widths), 6));
    const tileWidth = oneRow ? null : 100 / cols + "%";

    // presets: the selected type's (Default first), or the search hits of every type
    const ap = design.activePreset;
    const selLayer = selectedRecipe ? typeLayer(catalog, selectedRecipe) : undefined;
    const activeId = selLayer && ap && ap.layer.toLowerCase() === selLayer.id.toLowerCase() ? ap.id : null;
    const modified = !!ap?.modified;

    const rows = () => Array.from(root.current?.querySelectorAll<HTMLElement>("[data-nav]") ?? []);
    /** Arrow keys walk the grid: ←/→ the previous / next item, ↑/↓ the nearest item in the row above / below. */
    const onKey = (e: KeyboardEvent<HTMLDivElement>) => {
        const all = rows();
        const i = all.indexOf(document.activeElement as HTMLElement);
        let next: HTMLElement | undefined;
        if (e.key === "ArrowRight") next = all[i < 0 ? 0 : Math.min(all.length - 1, i + 1)];
        else if (e.key === "ArrowLeft") next = i <= 0 ? undefined : all[i - 1];
        else if (e.key === "ArrowDown" || e.key === "ArrowUp") {
            const down = e.key === "ArrowDown";
            if (i < 0) next = all[0];
            else {
                const r = all[i].getBoundingClientRect();
                const cx = r.left + r.width / 2,
                    cy = r.top + r.height / 2;
                let best: { el: HTMLElement; dy: number; dx: number } | null = null;
                for (const el of all) {
                    const b = el.getBoundingClientRect();
                    const dy = (b.top + b.height / 2 - cy) * (down ? 1 : -1);
                    if (dy < 4) continue;
                    const dx = Math.abs(b.left + b.width / 2 - cx);
                    if (!best || dy < best.dy - 4 || (Math.abs(dy - best.dy) <= 4 && dx < best.dx)) best = { el, dy, dx };
                }
                next = best?.el ?? (down ? undefined : root.current?.querySelector<HTMLElement>("input") ?? undefined);
            }
        } else if (e.key === "Home") next = all[0];
        else if (e.key === "End") next = all[all.length - 1];
        else if (e.key === "Enter") {
            // Enter activates the focused item (native click); keep it from reaching the tool's Enter = primary.
            e.stopPropagation();
            return;
        } else return;
        e.preventDefault();
        e.stopPropagation();
        next?.focus();
    };

    const search = (
        <div className={classNames(s.search, { [s.searchSide]: layout.wide })}>
            <Icon name="search" size={12} tint="dim" />
            <input
                className={s.searchInput}
                data-testid="browser-search"
                value={query}
                placeholder={layout.wide ? t("Placemaker.UI.Browser.SearchShort", "Search") : t("Placemaker.UI.Browser.Search", "Search types and presets")}
                onChange={(e) => setQuery(e.target.value)}
                onKeyDown={(e) => {
                    if (e.key === "Escape" && query) {
                        setQuery("");
                        e.preventDefault();
                    } else if (e.key === "Enter") {
                        // first matching preset, else the first matching type
                        if (firstHit && "preset" in firstHit && firstHit.preset) act.selectPreset(firstHit.layer, firstHit.preset);
                        else if (firstHit && firstHit.recipe !== design.recipeId) act.selectRecipe(firstHit.recipe);
                        e.preventDefault();
                    } else if (e.key === "ArrowDown") {
                        rows()[0]?.focus();
                        e.preventDefault();
                    }
                    e.stopPropagation();
                }}
            />
            {query ? <Btn icon="close" iconSize={11} className={s.clear} title={t("Placemaker.UI.Browser.Clear", "Clear")} onClick={() => setQuery("")} testId="browser-clear" /> : null}
        </div>
    );

    const catChip = (id: string, title: string, count: number, hasSelected: boolean) => (
        <button
            key={id}
            type="button"
            data-nav="cat"
            data-testid={`make-group-${id}`}
            data-has-selected={hasSelected ? "true" : "false"}
            aria-pressed={current === id ? "true" : "false"}
            className={classNames(s.cat, { [s.catOn]: current === id })}
            onClick={(e) => {
                e.stopPropagation();
                setCategory(id);
            }}
        >
            <div className={s.catName}>{title}</div>
            <div className={s.catCount}>{count}</div>
            {hasSelected && current !== id && current !== ALL ? <div className={s.catMark} /> : null}
        </button>
    );

    return (
        <div className={s.browser} data-testid="type-browser" ref={root} onKeyDown={onKey}>
            {layout.wide ? null : search}
            <div className={s.head}>
                <div className={s.cats} data-testid="make-groups">
                    {cats.length > 1 || current !== ALL ? catChip(ALL, t("Placemaker.UI.Browser.All", "All"), cats.reduce((n, c) => n + c.types.length, 0), false) : null}
                    {cats.map((c) => catChip(c.id, c.title, c.types.length, c.hasSelected))}
                </div>
                {layout.wide ? search : null}
            </div>
            {cats.length ? (
                <div className={s.tiles} data-testid="recipes" data-grouped="true" data-cols={cols}>
                    {tiles.map((r) => (
                        <TypeTile key={r.id} catalog={catalog} recipe={r} selected={r.id === design.recipeId} width={tileWidth} />
                    ))}
                </div>
            ) : (
                <div className={s.empty} data-testid="browser-empty">
                    {t("Placemaker.UI.Browser.None", "Nothing matches “{0}”", [query.trim()])}
                </div>
            )}
            {q ? (
                presetHits.length ? (
                    <div className={s.presets} data-testid="preset-hits">
                        {presetHits.map(({ r, layer, p }) => (
                            <PresetChip
                                key={layer.id + "/" + p.id}
                                layer={layer}
                                recipe={r}
                                p={p}
                                active={r.id === design.recipeId && activeId === p.id}
                                modified={modified}
                                typeIcon
                            />
                        ))}
                    </div>
                ) : null
            ) : selectedRecipe && selLayer && selLayer.presets.length ? (
                <div className={s.presets} data-testid={`presets-${selectedRecipe.id}`}>
                    <PresetChip layer={selLayer} recipe={selectedRecipe} p={null} active={activeId === DEFAULT_ID} modified={modified} />
                    {selLayer.presets.map((p) => (
                        <PresetChip key={p.id} layer={selLayer} recipe={selectedRecipe} p={p} active={activeId === p.id} modified={modified} />
                    ))}
                </div>
            ) : null}
        </div>
    );
};

// ───────────── active preset bar ─────────────

type Mode = "idle" | "saveAs" | "rename" | "menu";

const NameField = ({ initial, onSave, onCancel, testId }: { initial: string; onSave: (name: string) => void; onCancel: () => void; testId: string }) => {
    const { t } = useT();
    const [value, setValue] = useState(initial);
    const ok = value.trim().length > 0;
    return (
        <div className={s.nameForm} data-testid={testId}>
            <input
                className={s.nameInput}
                data-testid="preset-name-input"
                autoFocus
                value={value}
                maxLength={48}
                placeholder={t("Placemaker.UI.Preset.NamePlaceholder", "Preset name")}
                onChange={(e) => setValue(e.target.value)}
                onKeyDown={(e) => {
                    e.stopPropagation();
                    if (e.key === "Enter" && ok) onSave(value.trim());
                    if (e.key === "Escape") onCancel();
                }}
            />
            <Btn variant="accent" label={t("Placemaker.UI.Save", "Save")} disabled={!ok} onClick={() => onSave(value.trim())} testId="preset-name-save" />
            <Btn icon="close" iconSize={12} title={t("Placemaker.UI.Cancel", "Cancel")} onClick={onCancel} testId="preset-name-cancel" />
        </div>
    );
};

const MenuItem = ({ icon, label, onClick, testId, danger }: { icon: string; label: string; onClick: () => void; testId: string; danger?: boolean }) => (
    <button
        type="button"
        data-testid={testId}
        className={classNames(s.row, s.menuItem, { [s.menuDanger]: danger })}
        onClick={(e) => {
            e.stopPropagation();
            onClick();
        }}
    >
        <Icon name={icon} size={12} tint={danger ? "danger" : "dim"} />
        <div className={s.name}>{label}</div>
    </button>
);

/**
 * "Victorian · modified   [Save] [Save as…] [⋯]" above the options. Save overwrites the player's preset
 * or saves over a built-in (which then shows "edited" and can be reset); Save as names a new one (★).
 */
export const PresetBar = ({ catalog, design, step }: { catalog: Catalog; design: DesignState; step: StepView }) => {
    const { t } = useT();
    const [mode, setMode] = useState<Mode>("idle");
    const [confirm, setConfirm] = useState<"delete" | "reset" | null>(null);
    const [exported, setExported] = useState(false);
    const ap: ActivePreset | undefined = design.activePreset;
    const layer = catalog.layers.find((l) => l.id === step.layer);
    if (!ap || !layer) return null;
    const p = ap.id ? layer.presets.find((x) => x.id === ap.id) ?? null : null;
    const name = p ? presetName(t, p) : t("Placemaker.UI.Preset.Default", "Default");
    const canSave = ap.modified && ap.canSave && !!p;
    const close = () => setMode("idle");

    const dialog = confirm ? (
        <Confirm
            title={
                confirm === "delete"
                    ? t("Placemaker.UI.Preset.DeleteTitle", "Delete “{0}”?", [name])
                    : t("Placemaker.UI.Preset.ResetTitle", "Reset “{0}” to default?", [name])
            }
            message={
                confirm === "delete"
                    ? t("Placemaker.UI.Preset.DeleteText", "Your preset is removed. What you already built stays as it is.")
                    : t("Placemaker.UI.Preset.ResetText", "Your saved changes to this preset are discarded and it goes back to the shipped settings.")
            }
            confirm={confirm === "delete" ? t("Placemaker.UI.Delete", "Delete") : t("Placemaker.UI.Preset.Reset", "Reset")}
            danger
            onConfirm={() => {
                setConfirm(null);
                act.deletePreset(layer.id, ap.id);
            }}
            onCancel={() => setConfirm(null)}
        />
    ) : null;

    return (
        <div
            className={s.bar}
            data-testid="preset-bar"
            data-modified={ap.modified ? "true" : "false"}
            onKeyDown={(e) => {
                // Enter on a focused bar button presses that button, not the tool's primary action.
                if (e.key === "Enter") e.stopPropagation();
            }}
        >
            {mode === "saveAs" ? (
                <NameField
                    testId="preset-save-as-form"
                    initial={p ? t("Placemaker.UI.Preset.CopyName", "{0} (mine)", [name]) : ""}
                    onSave={(n) => {
                        act.savePresetAs(step.path, n);
                        close();
                    }}
                    onCancel={close}
                />
            ) : mode === "rename" && p ? (
                <NameField
                    testId="preset-rename-form"
                    initial={p.name}
                    onSave={(n) => {
                        act.renamePreset(layer.id, p.id, n);
                        close();
                    }}
                    onCancel={close}
                />
            ) : (
                <div className={s.barRow}>
                    {p?.custom ? <Icon name="star" size={11} tint="warn" className={s.barStar} /> : null}
                    <Tip title={name}>
                        <div className={s.barName} data-testid="preset-bar-name" data-ellipsis>
                            {name}
                        </div>
                    </Tip>
                    {p?.overridden && !ap.modified ? <div className={s.badge}>{t("Placemaker.UI.Preset.Edited", "edited")}</div> : null}
                    {ap.modified ? (
                        <div className={s.modified} data-testid="preset-modified">
                            {t("Placemaker.UI.Preset.Modified", "modified")}
                        </div>
                    ) : null}
                    <div className={s.flex} />
                    {ap.id ? (
                        <Btn
                            icon="save"
                            iconSize={12}
                            variant={canSave ? "accent" : "ghost"}
                            label={t("Placemaker.UI.Save", "Save")}
                            title={p && !p.custom ? t("Placemaker.UI.Preset.SaveBuiltIn", "Save over this preset") : t("Placemaker.UI.Preset.SaveTip", "Save changes to this preset")}
                            description={p && !p.custom ? t("Placemaker.UI.Preset.SaveBuiltInTip", "Your version replaces it; Reset to default brings the original back.") : undefined}
                            disabled={!canSave}
                            onClick={() => act.savePreset(step.path, ap.id)}
                            testId="preset-save"
                            className={s.barBtn}
                        />
                    ) : null}
                    <Btn
                        label={t("Placemaker.UI.Preset.SaveAs", "Save as…")}
                        title={t("Placemaker.UI.Preset.SaveAsTip", "Save these settings as a new preset of your own")}
                        onClick={() => setMode("saveAs")}
                        testId="preset-save-as"
                        className={s.barBtn}
                    />
                    <Btn
                        icon="more"
                        iconSize={13}
                        selected={mode === "menu"}
                        title={t("Placemaker.UI.Preset.More", "More")}
                        onClick={() => setMode(mode === "menu" ? "idle" : "menu")}
                        testId="preset-more"
                    />
                </div>
            )}
            {mode === "menu" ? (
                <div className={s.menu} data-testid="preset-menu">
                    {p?.custom ? (
                        <>
                            <MenuItem icon="edit" label={t("Placemaker.UI.Preset.Rename", "Rename")} onClick={() => setMode("rename")} testId="preset-rename" />
                            <MenuItem
                                icon="trash"
                                danger
                                label={t("Placemaker.UI.Delete", "Delete")}
                                onClick={() => {
                                    close();
                                    setConfirm("delete");
                                }}
                                testId="preset-delete"
                            />
                        </>
                    ) : null}
                    {p?.overridden ? (
                        <MenuItem
                            icon="reset"
                            label={t("Placemaker.UI.Preset.ResetDefault", "Reset to default")}
                            onClick={() => {
                                close();
                                setConfirm("reset");
                            }}
                            testId="preset-reset"
                        />
                    ) : null}
                    <MenuItem
                        icon="save"
                        label={t("Placemaker.UI.Preset.Export", "Export presets")}
                        onClick={() => {
                            act.exportPresets();
                            setExported(true);
                            close();
                        }}
                        testId="preset-export"
                    />
                </div>
            ) : null}
            {exported && design.presetsFile ? (
                <div className={s.exported} data-testid="presets-file">
                    <div className={s.exportedHead}>
                        <Icon name="info" size={12} tint="accent" />
                        <div className={s.exportedNote}>{t("Placemaker.UI.Preset.ExportNote", "Send this file to get your presets built into the mod")}</div>
                        <Btn icon="close" iconSize={11} title={t("Placemaker.UI.Close", "Close")} onClick={() => setExported(false)} testId="presets-file-close" />
                    </div>
                    <div className={s.path} data-testid="presets-file-path">
                        {design.presetsFile}
                    </div>
                </div>
            ) : null}
            {dialog}
        </div>
    );
};
