// Toolbar launcher (GameTopLeft) — one click = toggle the tool; selected state follows state.open, so
// it stays in sync with the hotkey and Esc. Always mounted, so it sits behind its own ErrorBoundary
// with a null fallback: it must never take the top bar down with it.
import React from "react";
import { act, useToolState } from "../lib/bindings";
import { useT } from "../lib/l10n";
import { icon } from "../lib/icons";
import { ErrorBoundary } from "../lib/errorBoundary";
import { SafeFloatingButton } from "../lib/safeButton";
import { Tip } from "../lib/tips";
import { Keys } from "./common";
import s from "./launcher.module.scss";

const LauncherInner = () => {
    const { t } = useT();
    const state = useToolState();
    return (
        <Tip
            direction="down"
            title={t("Placemaker.Name", "Placemaker")}
            description={t("Placemaker.UI.LauncherTip", "Choose an area and turn it into a park, plaza, parking lot, streets… or your own mix.")}
            footer={<Keys keys={["Ctrl", "Shift", "A"]} />}
        >
            <div className={s.wrap} data-testid="launcher" data-open={state.open ? "true" : "false"}>
                <SafeFloatingButton src={icon("logo")} selected={state.open} onSelect={act.toggleTool} className={s.button} />
            </div>
        </Tip>
    );
};

export const Launcher = () => (
    <ErrorBoundary name="launcher" fallback={null}>
        <LauncherInner />
    </ErrorBoundary>
);
