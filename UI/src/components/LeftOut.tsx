// s53 — "Left out: N" chip after a build. Click lists the reasons with counts; hovering a reason marks on the map where
// those pieces would have been (act.hoverAudit → overlay). Data: summary.audit (C# Core/Build/BuildAudit.ToUi).
import React, { useEffect, useState } from "react";
import classNames from "classnames";
import { BuildAudit } from "../contract";
import { act } from "../lib/bindings";
import { useT } from "../lib/l10n";
import { Icon } from "./common";
import s from "../stages/review.module.scss";

const FALLBACK: Record<string, string> = {
    rejected: "Game rejected: {0}",
    anarchy: "Kept despite overlap",
    "site-safety": "Building in the way",
    declutter: "Overlapped another piece",
    budget: "Object budget",
    "asset-missing": "Asset missing",
    "no-fit": "Layout could not fit",
    zoning: "Shown in preview, not zoned",
    other: "Other",
};

export const LeftOut = ({ audit }: { audit?: BuildAudit }) => {
    const { t } = useT();
    const [open, setOpen] = useState(false);
    useEffect(() => () => act.hoverAudit(""), []);
    if (!audit || (audit.leftOut <= 0 && audit.kept <= 0 && audit.reasons.length === 0)) return null;
    const name = (r: BuildAudit["reasons"][number]) => {
        const key = r.id.startsWith("rejected:") ? "rejected" : r.id;
        return t(r.label, FALLBACK[key] ?? r.id, [r.arg]);
    };
    return (
        <div className={s.leftOut} data-testid="left-out">
            <button
                type="button"
                className={classNames(s.alert, s.alert_warn)}
                data-testid="left-out-chip"
                onClick={(e) => {
                    e.stopPropagation();
                    setOpen(!open);
                    act.hoverAudit("");
                }}
            >
                <Icon name="info" size={11} tint="dim" />
                <div className={s.alertText}>{t("Placemaker.UI.LeftOut", "Left out: {0}", [audit.leftOut])}</div>
            </button>
            {open ? (
                <div className={s.leftList} onMouseLeave={() => act.hoverAudit("")}>
                    {audit.reasons.map((r) => (
                        <div key={r.id} className={s.leftRow} data-testid={`left-out-${r.id}`} onMouseEnter={() => act.hoverAudit(r.id)} onMouseLeave={() => act.hoverAudit("")}>
                            <div className={s.leftName}>{name(r)}</div>
                            <div className={s.leftTop}>{r.top}</div>
                            <div className={s.leftCount}>{r.count}</div>
                        </div>
                    ))}
                </div>
            ) : null}
        </div>
    );
};
