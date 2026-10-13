// Native hover tooltips. Same bubble as the game's tool tooltips (vanilla Tooltip resolved in
// vanilla.ts, or cs2/ui Tooltip) with the vanilla DescriptionTooltip title/description styling.
// Adapted from Grid Road Generator (Ti4goc). Note: a plain HTML title attribute shows nothing in
// Gameface, so every hint must go through <Tip>.
import React, { ReactElement, ReactNode } from "react";
import * as ui from "cs2/ui";
import { VC, VT } from "./vanilla";
import styles from "../styles/tip.module.scss";

const UiTooltip: any = (ui as any)?.Tooltip;

export const TipContent = ({ title, description, footer }: { title?: ReactNode; description?: ReactNode; footer?: ReactNode }) => {
    const c = VT.descriptionTooltip ?? {};
    return (
        <div className={styles.tip}>
            {title ? (
                <div className={c.header || styles.header}>
                    <div className={c.title || styles.title}>{title}</div>
                </div>
            ) : null}
            {description ? <div className={c.description || styles.description}>{description}</div> : null}
            {footer ? <div className={styles.footer}>{footer}</div> : null}
        </div>
    );
};

type TipProps = {
    title?: ReactNode;
    description?: ReactNode;
    footer?: ReactNode;
    direction?: "up" | "down" | "left" | "right";
    /** One DOM element: the tooltip attaches a ref to it. */
    children: ReactElement;
};

export const Tip = ({ title, description, footer, direction = "up", children }: TipProps) => {
    if (!title && !description && !footer) return children;
    const Impl = VC.Tooltip ?? UiTooltip;
    if (!Impl) return children;
    return (
        <Impl tooltip={<TipContent title={title} description={description} footer={footer} />} direction={direction} delayTime={250}>
            {children}
        </Impl>
    );
};
