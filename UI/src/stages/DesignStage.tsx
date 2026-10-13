// Stage 2 — what to make. "Make" row of recipes, then either just the controls (single-layer recipe,
// the 90 % case: no tree chrome at all) or a compact layer tree for mix-and-match: add-ons, fills for
// emitted regions ("fill each block with…"), per-region overrides, enable/disable, reorder, remove.
import React, { useEffect, useState } from "react";
import classNames from "classnames";
import { Catalog, DesignState, LayerType, StepView, ToolState } from "../contract";
import { act } from "../lib/bindings";
import { area as fmtArea } from "../lib/format";
import { layerColor } from "../lib/icons";
import { humanize, TFunc, useT } from "../lib/l10n";
import {
    addSections, AddSection, findStep, isOverridePath, isSimple, layerOf, overridableRegions, parentOf, regionLabelArgs, targetChoices,
} from "../lib/schema";
import { ErrorBoundary } from "../lib/errorBoundary";
import { Tip } from "../lib/tips";
import { Btn, Icon, Switch } from "../components/common";
import { ControlList } from "../controls/Controls";
import s from "./stages.module.scss";

export const layerName = (t: TFunc, l: LayerType) => t(l.name, humanize(l.name || l.id));

export function targetLabel(t: TFunc, target: string): string {
    if (!target) return t("Placemaker.UI.Target.Same", "whole area");
    const { tag, index } = regionLabelArgs(target);
    const tagName = t("Placemaker.UI.Tag." + tag, humanize(tag).toLowerCase());
    if (index !== null) return t("Placemaker.UI.Target.One", "{0} {1}", [tagName, index]);
    return t("Placemaker.UI.Target.Each", "each {0}", [tagName]);
}

export function regionName(t: TFunc, key: string): string {
    const { tag, index } = regionLabelArgs(key);
    const tagName = t("Placemaker.UI.Tag." + tag, humanize(tag).toLowerCase());
    const cap = tagName.charAt(0).toUpperCase() + tagName.slice(1);
    return index !== null ? `${cap} ${index}` : cap;
}

// ───────────── Make row ─────────────

const RecipeRow = ({ catalog, design }: { catalog: Catalog; design: DesignState }) => {
    const { t } = useT();
    return (
        <div className={s.makeRow} data-testid="recipes">
            {catalog.recipes.map((r) => {
                const on = r.id === design.recipeId;
                const color = layerColor(r.icon || r.id);
                const name = r.custom ? r.name : t(r.name, humanize(r.name || r.id));
                return (
                    <Tip key={r.id} title={name} description={r.description ? t(r.description, "") : undefined}>
                        <button
                            type="button"
                            data-testid={`recipe-${r.id}`}
                            aria-pressed={on ? "true" : "false"}
                            className={classNames(s.recipe, { [s.recipeOn]: on })}
                            onClick={(e) => {
                                e.stopPropagation();
                                if (!on) act.selectRecipe(r.id);
                            }}
                        >
                            <div className={s.recipeIcon} style={on ? { backgroundColor: color } : undefined}>
                                <Icon name={r.icon || r.id} size={20} color={on ? "#0b1d26" : color} />
                            </div>
                            <div className={s.recipeName}>{name}</div>
                            {r.custom ? (
                                <div className={s.recipeBadge}>
                                    <Icon name="custom" size={10} />
                                </div>
                            ) : null}
                        </button>
                    </Tip>
                );
            })}
        </div>
    );
};

// ───────────── Add menu ─────────────

const AddMenu = ({ catalog, step, onDone }: { catalog: Catalog; step: StepView; onDone: () => void }) => {
    const { t } = useT();
    const sections = addSections(catalog, step);
    const parentName = layerName(t, layerOf(catalog, step.layer));
    if (sections.length === 0) {
        return <div className={s.addMenuEmpty}>{t("Placemaker.UI.Add.Nothing", "Nothing can be added to this layer.")}</div>;
    }
    const heading = (sec: AddSection) =>
        sec.kind === "addOn"
            ? t("Placemaker.UI.Add.AddTo", "Add to the {0}", [parentName.toLowerCase()])
            : t("Placemaker.UI.Add.FillWith", "Fill {0} with", [targetLabel(t, sec.target)]);
    return (
        <div className={s.addMenu} data-testid="add-menu">
            {sections.map((sec) => (
                <div key={sec.kind + sec.target} className={s.addSection}>
                    <div className={s.addHeading}>{heading(sec)}</div>
                    <div className={s.addOptions}>
                        {sec.options.map((o) => (
                            <button
                                type="button"
                                key={o.layer.id}
                                data-testid={`add-${o.layer.id}${sec.target ? "-" + sec.target : ""}`}
                                className={s.addOption}
                                onClick={(e) => {
                                    e.stopPropagation();
                                    act.addLayer(step.path, o.layer.id, o.target);
                                    onDone();
                                }}
                            >
                                <Icon name={o.layer.icon || o.layer.id} size={16} color={layerColor(o.layer.id, o.layer.kind)} />
                                <div className={s.addOptionName}>{layerName(t, o.layer)}</div>
                            </button>
                        ))}
                    </div>
                </div>
            ))}
        </div>
    );
};

// ───────────── Layer tree ─────────────

const TreeRow = ({
    catalog, step, depth, focus, parent, overrideKey,
}: {
    catalog: Catalog; step: StepView; depth: number; focus: string; parent: StepView | null; overrideKey?: string;
}) => {
    const { t } = useT();
    const layer = layerOf(catalog, step.layer);
    const color = layerColor(layer.id, layer.kind);
    const on = focus === step.path;
    const siblings = parent ? parent.children : [];
    const idx = siblings.indexOf(step);
    return (
        <div
            className={classNames(s.treeRow, { [s.treeRowOn]: on, [s.treeRowOff]: !step.enabled })}
            data-testid={`tree-${step.path}`}
            style={{ paddingLeft: 6 + depth * 18 + "rem" }}
            onClick={(e) => {
                e.stopPropagation();
                if (!on) act.focusStep(step.path);
            }}
        >
            {depth > 0 ? <div className={s.treeElbow} /> : null}
            <div className={s.treeBar} style={{ backgroundColor: color }} />
            <Icon name={layer.icon || layer.id} size={16} color={color} />
            <div className={s.treeName}>{overrideKey ? regionName(t, overrideKey) + " → " + layerName(t, layer) : layerName(t, layer)}</div>
            {parent && !overrideKey ? <div className={s.treeTarget}>{targetLabel(t, step.target)}</div> : null}
            {!step.ready ? (
                <Tip title={t("Placemaker.UI.NeedsInput", "Needs your input")} description={step.nextStep ? t(step.nextStep.key, "", step.nextStep.args) : undefined}>
                    <div className={s.treeWarn}>
                        <Icon name="warning" size={13} color="#ffb547" />
                    </div>
                </Tip>
            ) : null}
            <div className={s.flex} />
            {parent ? (
                <div className={s.treeActions}>
                    {!overrideKey ? (
                        <>
                            <Btn icon="arrowUp" iconSize={13} className={s.treeBtn} disabled={idx <= 0} title={t("Placemaker.UI.MoveUp", "Move up")} onClick={() => act.moveStep(step.path, -1)} testId={`up-${step.path}`} />
                            <Btn icon="arrowDown" iconSize={13} className={s.treeBtn} disabled={idx < 0 || idx >= siblings.length - 1} title={t("Placemaker.UI.MoveDown", "Move down")} onClick={() => act.moveStep(step.path, 1)} testId={`down-${step.path}`} />
                            <Tip title={step.enabled ? t("Placemaker.UI.Disable", "Turn off (keeps settings)") : t("Placemaker.UI.Enable", "Turn on")}>
                                <div className={s.treeSwitch}>
                                    <Switch on={step.enabled} onChange={(v) => act.setStepEnabled(step.path, v)} testId={`enable-${step.path}`} />
                                </div>
                            </Tip>
                        </>
                    ) : null}
                    <Btn
                        icon="trash"
                        iconSize={13}
                        className={s.treeBtn}
                        title={overrideKey ? t("Placemaker.UI.RemoveOverride", "Back to the same as the others") : t("Placemaker.UI.Remove", "Remove")}
                        onClick={() => (overrideKey && parent ? act.overrideRegion(parent.path, overrideKey, "") : act.removeStep(step.path))}
                        testId={`remove-${step.path}`}
                    />
                </div>
            ) : null}
        </div>
    );
};

/** Chips for every region a targeted child fills; click one to make it different (per-region override). */
const RegionStrip = ({ catalog, step }: { catalog: Catalog; step: StepView }) => {
    const { t } = useT();
    const [open, setOpen] = useState<string | null>(null);
    const regions = overridableRegions(step);
    if (regions.length === 0) return null;
    const fills = catalog.layers.filter((l) => l.kind === "fill");
    const tag = regionLabelArgs(regions[0].key).tag;
    return (
        <div className={s.regions} data-testid={`regions-${step.path}`}>
            <div className={s.regionsHead}>
                <div className={s.regionsTitle}>{t("Placemaker.UI.Regions.Title", "Make one {0} different", [t("Placemaker.UI.Tag." + tag, humanize(tag).toLowerCase())])}</div>
            </div>
            <div className={s.regionChips}>
                {regions.map((r) => {
                    const ov = step.overrides[r.key];
                    const layer = layerOf(catalog, ov ? ov.layer : r.child?.layer ?? "");
                    return (
                        <Tip key={r.key} title={regionName(t, r.key)} description={(ov ? layerName(t, layer) + " · " : "") + (r.areaM2 ? fmtArea(r.areaM2) : "")}>
                            <button
                                type="button"
                                data-testid={`region-${r.key}`}
                                className={classNames(s.regionChip, { [s.regionChipOv]: !!ov, [s.regionChipOpen]: open === r.key })}
                                onClick={(e) => {
                                    e.stopPropagation();
                                    setOpen(open === r.key ? null : r.key);
                                }}
                            >
                                <Icon name={layer.icon || layer.id} size={13} color={layerColor(layer.id, layer.kind)} />
                                <div className={s.regionNum}>{regionLabelArgs(r.key).index ?? r.key}</div>
                            </button>
                        </Tip>
                    );
                })}
            </div>
            {open ? (
                <div className={s.regionMenu} data-testid="region-menu">
                    <div className={s.addHeading}>{t("Placemaker.UI.Regions.Make", "Make {0}", [regionName(t, open)])}</div>
                    <div className={s.addOptions}>
                        <button
                            type="button"
                            className={classNames(s.addOption, { [s.addOptionOn]: !step.overrides[open] })}
                            data-testid="override-none"
                            onClick={(e) => {
                                e.stopPropagation();
                                if (step.overrides[open]) act.overrideRegion(step.path, open, "");
                                setOpen(null);
                            }}
                        >
                            <Icon name="reset" size={14} />
                            <div className={s.addOptionName}>{t("Placemaker.UI.Regions.Same", "Same as the others")}</div>
                        </button>
                        {fills.map((l) => (
                            <button
                                type="button"
                                key={l.id}
                                data-testid={`override-${l.id}`}
                                className={classNames(s.addOption, { [s.addOptionOn]: step.overrides[open]?.layer === l.id })}
                                onClick={(e) => {
                                    e.stopPropagation();
                                    act.overrideRegion(step.path, open, l.id);
                                    setOpen(null);
                                }}
                            >
                                <Icon name={l.icon || l.id} size={16} color={layerColor(l.id, l.kind)} />
                                <div className={s.addOptionName}>{layerName(t, l)}</div>
                            </button>
                        ))}
                    </div>
                </div>
            ) : null}
        </div>
    );
};

const Tree = ({ catalog, root, focus }: { catalog: Catalog; root: StepView; focus: string }) => {
    const rows: React.ReactNode[] = [];
    const visit = (st: StepView, depth: number, parent: StepView | null) => {
        rows.push(<TreeRow key={st.path} catalog={catalog} step={st} depth={depth} focus={focus} parent={parent} />);
        st.children.forEach((c) => visit(c, depth + 1, st));
        Object.keys(st.overrides).forEach((k) =>
            rows.push(<TreeRow key={st.overrides[k].path} catalog={catalog} step={st.overrides[k]} depth={depth + 1} focus={focus} parent={st} overrideKey={k} />),
        );
        if (st.children.some((c) => c.target)) rows.push(<RegionStrip key={st.path + "#regions"} catalog={catalog} step={st} />);
    };
    visit(root, 0, null);
    return (
        <div className={s.tree} data-testid="layer-tree">
            {rows}
        </div>
    );
};

// ───────────── Step editor ─────────────

const StepEditor = ({ catalog, design, state, step, simple }: { catalog: Catalog; design: DesignState; state: ToolState; step: StepView; simple: boolean }) => {
    const { t } = useT();
    const [adding, setAdding] = useState(false);
    useEffect(() => setAdding(false), [step.path]);
    const layer = layerOf(catalog, step.layer);
    const parent = parentOf(design.root, step.path);
    const overrideKey = isOverridePath(design.root, step.path);
    const targets = parent && !overrideKey ? targetChoices(catalog, parent, step) : [];
    const canAdd = !overrideKey && addSections(catalog, step).length > 0;
    return (
        <div className={s.editor} data-testid="step-editor">
            <div className={s.editorHead}>
                {!simple ? (
                    <div className={s.editorTitle}>
                        <Icon name={layer.icon || layer.id} size={16} color={layerColor(layer.id, layer.kind)} />
                        <div className={s.editorName}>{overrideKey ? regionName(t, overrideKey) + " · " + layerName(t, layer) : layerName(t, layer)}</div>
                    </div>
                ) : null}
                <div className={s.presets} data-testid="presets">
                    {layer.presets.map((p) => (
                        <Btn
                            key={p.id}
                            variant="solid"
                            icon={p.icon || undefined}
                            iconSize={15}
                            label={t(p.name, humanize(p.name || p.id))}
                            onClick={() => act.applyPreset(step.path, p.id)}
                            testId={`preset-${p.id}`}
                            className={s.presetBtn}
                        />
                    ))}
                </div>
                <div className={s.flex} />
                <Btn
                    icon="dice"
                    iconSize={16}
                    label={t("Placemaker.UI.Randomize", "Randomize")}
                    title={t("Placemaker.UI.Randomize", "Randomize")}
                    description={t("Placemaker.UI.RandomizeTip", "Rolls a new variation. Values you set yourself (marked with a lock) are kept.")}
                    onClick={() => act.randomize(step.path)}
                    testId="randomize"
                />
                <Btn
                    icon="reset"
                    iconSize={15}
                    title={t("Placemaker.UI.ResetStep", "Reset all to defaults")}
                    onClick={() => act.resetStep(step.path)}
                    testId="reset-step"
                />
            </div>
            {!step.ready && step.nextStep ? (
                <div className={s.nextStep} data-testid="next-step">
                    <Icon name="info" size={15} color="#8fd1ff" />
                    <div className={s.nextStepText}>{t(step.nextStep.key, humanize(step.nextStep.key), step.nextStep.args)}</div>
                </div>
            ) : null}
            {targets.length > 1 ? (
                <div className={s.targetRow} data-testid="target-row">
                    <div className={s.targetLabel}>{t("Placemaker.UI.Target.Where", "Where")}</div>
                    <div className={s.chipsInline}>
                        {targets.map((tg) => (
                            <button
                                type="button"
                                key={tg || "_"}
                                data-testid={`target-${tg || "same"}`}
                                className={classNames(s.targetChip, { [s.targetChipOn]: tg === step.target })}
                                onClick={(e) => {
                                    e.stopPropagation();
                                    if (tg !== step.target) act.setStepTarget(step.path, tg);
                                }}
                            >
                                {targetLabel(t, tg)}
                            </button>
                        ))}
                    </div>
                </div>
            ) : null}
            <ErrorBoundary name="controls">
                <ControlList step={step} layer={layer} state={state} />
            </ErrorBoundary>
            {canAdd ? (
                <div className={s.addWrap}>
                    <Btn
                        icon={adding ? "close" : "plus"}
                        iconSize={15}
                        variant="solid"
                        label={adding ? t("Placemaker.UI.Cancel", "Cancel") : simple ? t("Placemaker.UI.Add.Mix", "Add to this design…") : t("Placemaker.UI.Add.Here", "Add here…")}
                        description={t("Placemaker.UI.Add.Tip", "Mix and match: add a playground, a fountain, edge zoning… or fill parts of the area with something else.")}
                        onClick={() => setAdding(!adding)}
                        testId="add-layer"
                    />
                    {adding ? <AddMenu catalog={catalog} step={step} onDone={() => setAdding(false)} /> : null}
                </div>
            ) : null}
        </div>
    );
};

// ───────────── Save as recipe ─────────────

const SaveRecipe = ({ design }: { design: DesignState }) => {
    const { t } = useT();
    const [editing, setEditing] = useState(false);
    const [name, setName] = useState("");
    const save = () => {
        const n = name.trim();
        if (!n) return;
        act.saveRecipeAs(n);
        setEditing(false);
        setName("");
    };
    if (!editing) {
        return (
            <Btn
                icon="save"
                iconSize={15}
                label={t("Placemaker.UI.SaveRecipe", "Save as my recipe")}
                description={t("Placemaker.UI.SaveRecipeTip", "Keeps this whole design (layers and settings) as a new entry in the Make row.")}
                onClick={() => setEditing(true)}
                testId="save-recipe"
                className={design.modified ? s.saveHot : undefined}
            />
        );
    }
    return (
        <div className={s.saveRow} data-testid="save-recipe-form">
            <input
                className={s.textInput}
                data-testid="save-recipe-name"
                autoFocus
                value={name}
                placeholder={t("Placemaker.UI.SaveRecipe.Placeholder", "Name, e.g. Corner park with café")}
                onChange={(e) => setName(e.target.value)}
                onKeyDown={(e) => {
                    e.stopPropagation();
                    if (e.key === "Enter") save();
                    if (e.key === "Escape") setEditing(false);
                }}
            />
            <Btn variant="accent" label={t("Placemaker.UI.Save", "Save")} disabled={!name.trim()} onClick={save} testId="save-recipe-confirm" />
            <Btn icon="close" title={t("Placemaker.UI.Cancel", "Cancel")} onClick={() => setEditing(false)} />
        </div>
    );
};

// ───────────── stage ─────────────

export const DesignStage = ({ catalog, design, state }: { catalog: Catalog; design: DesignState; state: ToolState }) => {
    const { t } = useT();
    const root = design.root;
    const simple = isSimple(root);
    const focus = (root && findStep(root, design.focus)) || root;
    const recipe = catalog.recipes.find((r) => r.id === design.recipeId);
    return (
        <div className={s.stage} data-testid="stage-design">
            <div className={s.sectionHead}>
                <div className={s.sectionLabel}>{t("Placemaker.UI.Make", "Make")}</div>
                {design.modified ? <div className={s.modified}>{t("Placemaker.UI.Modified", "modified")}</div> : null}
                <div className={s.flex} />
                {recipe?.custom ? (
                    <Btn icon="trash" iconSize={14} title={t("Placemaker.UI.DeleteRecipe", "Delete this recipe")} onClick={() => act.deleteRecipe(recipe.id)} testId="delete-recipe" />
                ) : null}
            </div>
            <ErrorBoundary name="recipes">
                <RecipeRow catalog={catalog} design={design} />
            </ErrorBoundary>
            {!root ? (
                <div className={s.emptyNote}>{t("Placemaker.UI.PickRecipe", "Pick what this area should become.")}</div>
            ) : (
                <>
                    {!simple ? (
                        <ErrorBoundary name="tree">
                            <div className={s.treeHead}>
                                <div className={s.sectionLabel}>{t("Placemaker.UI.Layers", "Layers")}</div>
                                <div className={s.flex} />
                                <Btn icon="dice" iconSize={14} label={t("Placemaker.UI.RandomizeAll", "Randomize all")} onClick={() => act.randomize("")} testId="randomize-all" />
                            </div>
                            <Tree catalog={catalog} root={root} focus={focus ? focus.path : ""} />
                        </ErrorBoundary>
                    ) : null}
                    {focus ? (
                        <ErrorBoundary name="editor">
                            <StepEditor catalog={catalog} design={design} state={state} step={focus} simple={simple} />
                        </ErrorBoundary>
                    ) : null}
                    <div className={s.designFoot}>
                        <SaveRecipe design={design} />
                    </div>
                </>
            )}
        </div>
    );
};
