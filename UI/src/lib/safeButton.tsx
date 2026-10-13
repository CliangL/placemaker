// Crash guard for cs2/ui buttons: present in the typings but not guaranteed at runtime, and a
// missing component throws inside the game's whole React tree. Falls back to a native element.
// Adapted from Grid Road Generator (Ti4goc).
import React from "react";
import * as ui from "cs2/ui";

const FloatingButton: any = (ui as any)?.FloatingButton;
const Button: any = (ui as any)?.Button;

type Props = {
    src: string;
    selected?: boolean;
    className?: string;
    onSelect: () => void;
    tooltipLabel?: React.ReactNode;
};

/** Round floating icon button (top-left launcher row), with the vanilla selected state. */
export const SafeFloatingButton = ({ src, selected, className, onSelect, tooltipLabel }: Props) => {
    if (FloatingButton) {
        return <FloatingButton src={src} selected={selected} className={className} onSelect={onSelect} tooltipLabel={tooltipLabel} />;
    }
    if (Button) {
        return <Button variant="floating" src={src} selected={selected} className={className} onSelect={onSelect} tooltipLabel={tooltipLabel} />;
    }
    return (
        <button type="button" className={className} onClick={onSelect} data-selected={selected ? "true" : "false"}>
            <img src={src} alt="" style={{ width: "100%", height: "100%" }} />
        </button>
    );
};
