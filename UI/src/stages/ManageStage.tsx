// Manage — a built place under the cursor/selected: what it is, what it costs to run, and
// Edit / Rename / Remove (with confirmation). The same actions appear in the vanilla selected-info
// panel via PlacemakerInfoSection.
import React, { useState } from "react";
import { ManagedGroup } from "../contract";
import { act } from "../lib/bindings";
import { Money } from "../lib/format";
import { useT } from "../lib/l10n";
import { Btn, Icon } from "../components/common";
import { Confirm } from "../components/dialog";
import { StatChip } from "./ReviewStage";
import s from "./stages.module.scss";

export function useGroupActions(id: number, displayName: string) {
    const { t } = useT();
    const [confirming, setConfirming] = useState(false);
    const dialog = confirming ? (
        <Confirm
            title={t("Placemaker.UI.Manage.RemoveTitle", "Remove {0}?", [displayName])}
            message={t("Placemaker.UI.Manage.RemoveText", "Everything Placemaker built here (paths, surfaces, objects, roads it added) is bulldozed. Buildings that grew next to it stay.")}
            confirm={t("Placemaker.UI.Manage.RemoveConfirm", "Remove")}
            danger
            onConfirm={() => {
                setConfirming(false);
                act.removeGroup(id);
            }}
            onCancel={() => setConfirming(false)}
        />
    ) : null;
    return { askRemove: () => setConfirming(true), dialog };
}

export const RenameField = ({ id, name, onDone }: { id: number; name: string; onDone: () => void }) => {
    const { t } = useT();
    const [value, setValue] = useState(name);
    const save = () => {
        act.renameGroup(id, value.trim());
        onDone();
    };
    return (
        <div className={s.saveRow} data-testid="rename-form">
            <input
                className={s.textInput}
                data-testid="rename-input"
                autoFocus
                value={value}
                onChange={(e) => setValue(e.target.value)}
                onKeyDown={(e) => {
                    e.stopPropagation();
                    if (e.key === "Enter") save();
                    if (e.key === "Escape") onDone();
                }}
            />
            <Btn variant="accent" label={t("Placemaker.UI.Save", "Save")} onClick={save} testId="rename-save" />
            <Btn icon="close" title={t("Placemaker.UI.Cancel", "Cancel")} onClick={onDone} />
        </div>
    );
};

export const ManageStage = ({ group }: { group: ManagedGroup | null }) => {
    const { t, tl } = useT();
    const [renaming, setRenaming] = useState(false);
    const name = group ? group.customName || tl(group.recipeName, t("Placemaker.Name", "Placemaker")) : "";
    const { askRemove, dialog } = useGroupActions(group?.id ?? -1, name);
    if (!group) {
        return (
            <div className={s.stage} data-testid="stage-manage">
                <div className={s.emptyNote}>{t("Placemaker.UI.Manage.Hover", "Hover or click something you built with Placemaker to edit or remove it.")}</div>
            </div>
        );
    }
    return (
        <div className={s.stage} data-testid="stage-manage">
            <div className={s.manageHead}>
                <Icon name="logo" size={20} tint="accent" />
                {renaming ? (
                    <RenameField id={group.id} name={group.customName ?? ""} onDone={() => setRenaming(false)} />
                ) : (
                    <>
                        <div className={s.manageName} data-testid="manage-name">
                            {name}
                        </div>
                        {group.customName ? <div className={s.manageKind}>{tl(group.recipeName)}</div> : null}
                    </>
                )}
            </div>
            {group.stats.length ? (
                <div className={s.stats}>
                    {group.stats.map((st) => (
                        <StatChip key={st.key + st.unit} k={st.key} value={st.value} unit={st.unit} />
                    ))}
                </div>
            ) : null}
            <div className={s.manageUpkeep}>
                <div className={s.bigLabel}>{t("Placemaker.UI.Review.Upkeep", "Upkeep")}</div>
                <div className={s.manageUpkeepValue}>
                    <Money value={group.upkeep} perMonth />
                </div>
            </div>
            <div className={s.manageActions}>
                <Btn
                    variant="accent"
                    icon="edit"
                    iconSize={15}
                    label={t("Placemaker.UI.Manage.Edit", "Edit")}
                    disabled={!group.canEdit}
                    description={!group.canEdit ? t("Placemaker.UI.Manage.CantEdit", "This was built by an older version and can only be removed.") : undefined}
                    onClick={() => act.editGroup(group.id)}
                    testId="manage-edit"
                />
                <Btn variant="solid" icon="edit" iconSize={15} label={t("Placemaker.UI.Manage.Rename", "Rename")} onClick={() => setRenaming(true)} testId="manage-rename" />
                <div className={s.flex} />
                <Btn variant="danger" icon="trash" iconSize={15} label={t("Placemaker.UI.Manage.Remove", "Remove")} onClick={askRemove} testId="manage-remove" />
            </div>
            {dialog}
        </div>
    );
};
