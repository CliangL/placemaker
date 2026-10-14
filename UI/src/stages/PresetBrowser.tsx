// One browser for "what to make": Category ▸ Type ▸ Preset, plus the active-preset bar (Save / Save as /
// rename / delete / reset / export). Replaces the Make cards + preset chips (playtest 3: "categories of
// main types, then each has its own subtypes; add your own and save it; a save button, I could name it").
//   • Categories: the 12 groups types declare (RecipeInfo.group, else guessed from the id); collapsible,
//     remembered (lib/collapse). Small catalogs start open, big ones open only the selected type's group.
//   • Type row: icon + name. Click = selectRecipe (the type with its default settings). The selected type
//     is expanded and lists its presets: "Default", built-ins ("edited" when saved over), player presets (★).
//   • Preset row: click = selectPreset(layer, preset).
//   • Search filters categories / types / presets by name; ↑/↓ move between rows, ←/→ fold categories,
//     Enter in the search box picks the first match, Esc clears it.
import React, { KeyboardEvent, useRef, useState } from "react";
import classNames from "classnames";
import { ActivePreset, Catalog, DesignState, LayerType, PresetView, RecipeInfo, StepView } from "../contract";
import { act } from "../lib/bindings";
import { entityIcon, layerColor } from "../lib/icons";
import { humanize, TFunc, useT } from "../lib/l10n";
import { useOpen } from "../lib/collapse";
import { Tip } from "../lib/tips";
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

export const MAKE_GROUPS: { id: string; key: string; en: string }[] = [
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
    { id: "roads", key: "Placemaker.UI.MakeGroup.Roads", en: "Roads" },
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
    if (/parking|car-?park|garage|lot/.test(x)) return "parking";
    if (/road|street|avenue|grid/.test(x)) return "roads";
    if (/plaza|square|market|piazza|amphi|festival|fair/.test(x)) return "squares";
    if (/park|garden|camp|reserve|nature|forest|picnic|arboret|cemetery|green|playground|sport|golf|beach|lido|promenade|allotment|meadow|zoo|dog|botanic|skate/.test(x)) return "parks";
    return "other";
}

/** Up to this many types: every category starts open. More: only the selected type's category. */
const SMALL_CATALOG = 8;

function typeLayer(catalog: Catalog, r: RecipeInfo): LayerType | undefined {
    const id = r.id.toLowerCase();
    return catalog.layers.find((l) => l.id.toLowerCase() === id);
}

export const presetName = (t: TFunc, p: PresetView) => (p.custom ? p.name : t(p.name, humanize(p.name || p.id)));
const recipeName = (t: TFunc, r: RecipeInfo) => (r.custom ? r.name : t(r.name, humanize(r.name || r.id)));
const DEFAULT_ID = "";

// ───────────── rows ─────────────

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

const PresetRow = ({ layer, recipe, p, active, modified }: { layer: LayerType; recipe: RecipeInfo; p: PresetView | null; active: boolean; modified: boolean }) => {
    const { t } = useT();
    const name = p ? presetName(t, p) : t("Placemaker.UI.Preset.Default", "Default");
    const id = p ? p.id : DEFAULT_ID;
    return (
        <button
            type="button"
            data-nav="preset"
            data-testid={p ? `preset-${p.id}` : "preset-default"}
            data-layer={layer.id}
            aria-pressed={active ? "true" : "false"}
            className={classNames(s.row, s.presetRow, { [s.rowOn]: active })}
            onClick={(e) => {
                e.stopPropagation();
                if (active && !modified) return;
                if (p) act.selectPreset(layer.id, id);
                else act.selectRecipe(recipe.id);
            }}
        >
            <div className={classNames(s.radio, { [s.radioOn]: active })} />
            <Tip title={name} description={p?.custom && p.basedOn ? t("Placemaker.UI.Preset.BasedOn", "Your preset, based on {0}", [humanize(p.basedOn)]) : undefined}>
                <div className={s.name} data-ellipsis>
                    {name}
                </div>
            </Tip>
            {p ? <PresetBadges p={p} /> : null}
            {active && modified ? <div className={s.dot} /> : null}
        </button>
    );
};

const TypeBlock = ({
    catalog, recipe, selected, presets, design,
}: { catalog: Catalog; recipe: RecipeInfo; selected: boolean; presets: PresetView[] | null; design: DesignState }) => {
    const { t } = useT();
    const layer = typeLayer(catalog, recipe);
    const name = recipeName(t, recipe);
    const desc = recipe.description ? t(recipe.description, "") : "";
    const ap = design.activePreset;
    const count = layer ? layer.presets.length : 0;
    const activeId = selected && ap && layer && ap.layer.toLowerCase() === layer.id.toLowerCase() ? ap.id : null;
    return (
        <div className={s.type} data-testid={`type-${recipe.id}`}>
            <Tip title={name} description={desc || undefined}>
                <button
                    type="button"
                    data-nav="type"
                    data-testid={`recipe-${recipe.id}`}
                    aria-pressed={selected ? "true" : "false"}
                    aria-expanded={presets ? "true" : "false"}
                    className={classNames(s.row, s.typeRow, { [s.rowOn]: selected })}
                    onClick={(e) => {
                        e.stopPropagation();
                        if (!selected) act.selectRecipe(recipe.id);
                    }}
                >
                    <div className={s.typeIcon} style={{ backgroundColor: layerColor(recipe.id) }}>
                        <Icon name={entityIcon(recipe.id, recipe.icon)} size={11} tint="dark" />
                    </div>
                    <div className={classNames(s.name, s.typeName)} data-ellipsis>
                        {name}
                    </div>
                    {count && !presets ? <div className={s.count}>{count}</div> : null}
                </button>
            </Tip>
            {presets && layer ? (
                <div className={s.presets} data-testid={`presets-${recipe.id}`}>
                    {selected && presets === layer.presets ? (
                        <PresetRow layer={layer} recipe={recipe} p={null} active={activeId === DEFAULT_ID} modified={!!ap?.modified} />
                    ) : null}
                    {presets.map((p) => (
                        <PresetRow key={p.id} layer={layer} recipe={recipe} p={p} active={activeId === p.id} modified={!!ap?.modified} />
                    ))}
                </div>
            ) : null}
        </div>
    );
};

type CatView = { id: string; title: string; items: { r: RecipeInfo; presets: PresetView[] | null }[]; selected?: RecipeInfo; total: number };

const CategoryBlock = ({ c, catalog, design, searching, small }: { c: CatView; catalog: Catalog; design: DesignState; searching: boolean; small: boolean }) => {
    const { t } = useT();
    const [stored, setOpen] = useOpen("browser:" + c.id, small || !!c.selected);
    const open = searching || stored;
    return (
        <div className={s.cat} data-testid={`make-group-${c.id}`} data-open={open ? "true" : "false"}>
            <button
                type="button"
                data-nav="cat"
                data-testid={`make-group-${c.id}-toggle`}
                aria-expanded={open ? "true" : "false"}
                className={classNames(s.row, s.catRow)}
                onClick={(e) => {
                    e.stopPropagation();
                    if (!searching) setOpen(!open);
                }}
                onKeyDown={(e) => {
                    if (searching) return;
                    if (e.key === "ArrowRight" && !open) setOpen(true);
                    else if (e.key === "ArrowLeft" && open) setOpen(false);
                    else return;
                    e.preventDefault();
                    e.stopPropagation();
                }}
            >
                <Icon name={open ? "chevronDown" : "chevronRight"} size={11} tint="dim" />
                <div className={classNames(s.name, s.catName)}>{c.title}</div>
                {!open && c.selected ? (
                    <div className={s.catSel} data-ellipsis>
                        {recipeName(t, c.selected)}
                    </div>
                ) : (
                    <div className={s.count}>{searching ? c.items.length : c.total}</div>
                )}
            </button>
            {open
                ? c.items.map(({ r, presets }) => (
                      <TypeBlock key={r.id} catalog={catalog} recipe={r} selected={r.id === design.recipeId} presets={presets} design={design} />
                  ))
                : null}
        </div>
    );
};

export const TypeBrowser = ({ catalog, design }: { catalog: Catalog; design: DesignState }) => {
    const { t } = useT();
    const [query, setQuery] = useState("");
    const root = useRef<HTMLDivElement>(null);
    const q = query.trim().toLowerCase();
    const list = makeRecipes(catalog);
    const small = list.length <= SMALL_CATALOG;
    const has = (text: string) => text.toLowerCase().includes(q);

    const cats: CatView[] = [];
    for (const g of MAKE_GROUPS) {
        const members = list.filter((r) => recipeGroup(r) === g.id);
        if (!members.length) continue;
        const title = t(g.key, g.en);
        const catHit = !q || has(title);
        const items: CatView["items"] = [];
        for (const r of members) {
            const layer = typeLayer(catalog, r);
            const all = layer?.presets ?? [];
            const selected = r.id === design.recipeId;
            if (!q) {
                items.push({ r, presets: selected && layer ? all : null });
                continue;
            }
            const hits = all.filter((p) => has(presetName(t, p)));
            if (hits.length) items.push({ r, presets: hits.length === all.length ? all : hits });
            else if (catHit || has(recipeName(t, r))) items.push({ r, presets: selected && layer ? all : null });
        }
        if (items.length) cats.push({ id: g.id, title, items, selected: members.find((r) => r.id === design.recipeId), total: members.length });
    }

    const rows = () => Array.from(root.current?.querySelectorAll<HTMLElement>("[data-nav]") ?? []);
    const onKey = (e: KeyboardEvent<HTMLDivElement>) => {
        const all = rows();
        const i = all.indexOf(document.activeElement as HTMLElement);
        let next: HTMLElement | undefined;
        if (e.key === "ArrowDown") next = all[i < 0 ? 0 : Math.min(all.length - 1, i + 1)];
        else if (e.key === "ArrowUp") next = i <= 0 ? root.current?.querySelector<HTMLElement>("input") ?? undefined : all[i - 1];
        else if (e.key === "Home") next = all[0];
        else if (e.key === "End") next = all[all.length - 1];
        else if (e.key === "Enter") {
            // Enter activates the focused row (native click); keep it from reaching the tool's Enter = primary.
            e.stopPropagation();
            return;
        } else return;
        e.preventDefault();
        e.stopPropagation();
        next?.focus();
    };

    return (
        <div className={s.browser} data-testid="type-browser" ref={root} onKeyDown={onKey}>
            <div className={s.search}>
                <Icon name="search" size={12} tint="dim" />
                <input
                    className={s.searchInput}
                    data-testid="browser-search"
                    value={query}
                    placeholder={t("Placemaker.UI.Browser.Search", "Search types and presets")}
                    onChange={(e) => setQuery(e.target.value)}
                    onKeyDown={(e) => {
                        if (e.key === "Escape" && query) {
                            setQuery("");
                            e.preventDefault();
                        } else if (e.key === "Enter") {
                            const first = root.current?.querySelector<HTMLElement>(q ? '[data-nav="preset"], [data-nav="type"]' : '[data-nav="type"]');
                            first?.click();
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
            <div className={s.tree} data-testid="recipes" data-grouped="true">
                {cats.map((c) => (
                    <CategoryBlock key={c.id} c={c} catalog={catalog} design={design} searching={!!q} small={small} />
                ))}
                {!cats.length ? (
                    <div className={s.empty} data-testid="browser-empty">
                        {t("Placemaker.UI.Browser.None", "Nothing matches “{0}”", [query.trim()])}
                    </div>
                ) : null}
            </div>
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
        <div className={s.bar} data-testid="preset-bar" data-modified={ap.modified ? "true" : "false"}>
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
