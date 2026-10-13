// Section in the vanilla selected-info panel for anything built by Placemaker:
//   "Part of <name>" — Edit / Rename / Remove.
// Registered for the C# InfoSectionBase `Placemaker.Game.Groups.PlacemakerGroupSection` (the key is the
// full .NET class name: InfoSectionBase writes GetType().FullName as __Type — ParkingLotTool learned
// that the hard way). Props are whatever that section writes; see docs/proposals/ui.md for the
// expected fields. Unknown/missing fields degrade gracefully.
import React, { useState } from "react";
import { act } from "../lib/bindings";
import { useT } from "../lib/l10n";
import { ErrorBoundary } from "../lib/errorBoundary";
import { VC } from "../lib/vanilla";
import { useGroupActions, RenameField } from "../stages/ManageStage";
import { Btn, Icon } from "./common";
import s from "./infoSection.module.scss";

export const INFO_SECTION_TYPE = "Placemaker.Game.Groups.PlacemakerGroupSection";

type Props = {
    groupId?: number;
    id?: number;
    group?: number | string;
    name?: string;
    customName?: string;
    recipeName?: string | { key: string; args?: (string | number)[] };
    canEdit?: boolean;
};

const Inner = (p: Props) => {
    const { t, tl } = useT();
    const [renaming, setRenaming] = useState(false);
    const id = typeof p.groupId === "number" ? p.groupId : typeof p.id === "number" ? p.id : typeof p.group === "number" ? p.group : -1;
    const recipe = typeof p.recipeName === "string" ? t(p.recipeName, "") : tl(p.recipeName);
    const name = p.customName || p.name || recipe || t("Placemaker.Name", "Placemaker");
    const { askRemove, dialog } = useGroupActions(id, name);
    const content = (
        <div className={s.section} data-testid="info-section">
            <div className={s.head}>
                <Icon name="logo" size={18} color="#8fd1ff" />
                {renaming ? (
                    <RenameField id={id} name={p.customName ?? p.name ?? ""} onDone={() => setRenaming(false)} />
                ) : (
                    <div className={s.title}>
                        <span className={s.partOf}>{t("Placemaker.UI.Info.PartOf", "Part of")}</span> {name}
                    </div>
                )}
            </div>
            {!renaming ? (
                <div className={s.actions}>
                    <Btn variant="solid" icon="edit" iconSize={14} label={t("Placemaker.UI.Manage.Edit", "Edit")} disabled={p.canEdit === false || id < 0} onClick={() => act.editGroup(id)} testId="info-edit" />
                    <Btn variant="solid" label={t("Placemaker.UI.Manage.Rename", "Rename")} disabled={id < 0} onClick={() => setRenaming(true)} testId="info-rename" />
                    <div className={s.flex} />
                    <Btn variant="danger" icon="trash" iconSize={14} label={t("Placemaker.UI.Manage.Remove", "Remove")} disabled={id < 0} onClick={askRemove} testId="info-remove" />
                </div>
            ) : null}
            {dialog}
        </div>
    );
    return VC.InfoSection ? <VC.InfoSection disableFocus={true}>{content}</VC.InfoSection> : content;
};

export const PlacemakerInfoSection = (p: Props) => (
    <ErrorBoundary name="info-section" fallback={null}>
        <Inner {...p} />
    </ErrorBoundary>
);
