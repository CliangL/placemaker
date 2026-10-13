// Small building blocks shared by every stage. Plain elements (not vanilla Button) so they behave the
// same in the game and the browser mock; vanilla sounds are played through the audio binding.
import React, { ReactNode, useState } from "react";
import classNames from "classnames";
import { resolveIcon } from "../lib/icons";
import { playSound } from "../lib/bindings";
import { Tip } from "../lib/tips";
import s from "./common.module.scss";

export const Icon = ({
    name,
    size = 18,
    color,
    className,
    fallback,
}: {
    name?: string;
    size?: number;
    color?: string;
    className?: string;
    fallback?: string;
}) => {
    const r = resolveIcon(name, fallback);
    const style: React.CSSProperties = { width: size + "rem", height: size + "rem" };
    if (!r.src) return <div className={classNames(s.icon, className)} style={style} />;
    if (!r.tint) return <img className={classNames(s.icon, className)} style={style} src={r.src} />;
    const url = `url(${r.src})`;
    return (
        <div
            className={classNames(s.icon, s.tinted, className)}
            style={{ ...style, maskImage: url, WebkitMaskImage: url, backgroundColor: color } as React.CSSProperties}
        />
    );
};

type BtnProps = {
    icon?: string;
    label?: ReactNode;
    title?: ReactNode;
    description?: ReactNode;
    onClick?: () => void;
    disabled?: boolean;
    selected?: boolean;
    className?: string;
    iconColor?: string;
    iconSize?: number;
    variant?: "ghost" | "solid" | "accent" | "danger";
    testId?: string;
    children?: ReactNode;
    tipDirection?: "up" | "down" | "left" | "right";
};

export const Btn = ({
    icon, label, title, description, onClick, disabled, selected, className, iconColor, iconSize = 18,
    variant = "ghost", testId, children, tipDirection,
}: BtnProps) => {
    const el = (
        <button
            type="button"
            data-testid={testId}
            aria-disabled={disabled ? "true" : "false"}
            aria-pressed={selected ? "true" : "false"}
            className={classNames(s.btn, s[variant], { [s.selected]: selected, [s.disabled]: disabled, [s.iconOnly]: !label && !children }, className)}
            onMouseEnter={() => !disabled && playSound("hover-item")}
            onClick={(e) => {
                e.stopPropagation();
                if (disabled || !onClick) return;
                playSound("select-item");
                onClick();
            }}
        >
            {icon ? <Icon name={icon} size={iconSize} color={iconColor} /> : null}
            {label ? <div className={s.btnLabel}>{label}</div> : null}
            {children}
        </button>
    );
    return (
        <Tip title={title} description={description} direction={tipDirection}>
            {el}
        </Tip>
    );
};

export interface SegOption {
    value: string;
    label?: ReactNode;
    icon?: string;
    title?: ReactNode;
    description?: ReactNode;
    color?: string;
}

export const Segmented = ({
    options,
    value,
    onChange,
    iconOnly,
    testId,
    stretch,
}: {
    options: SegOption[];
    value: string;
    onChange: (v: string) => void;
    iconOnly?: boolean;
    testId?: string;
    stretch?: boolean;
}) => (
    <div className={classNames(s.segmented, { [s.stretch]: stretch })} data-testid={testId}>
        {options.map((o) => (
            <Tip key={o.value} title={o.title ?? (iconOnly ? o.label : undefined)} description={o.description}>
                <button
                    type="button"
                    data-testid={testId ? `${testId}-${o.value}` : undefined}
                    aria-pressed={o.value === value ? "true" : "false"}
                    className={classNames(s.seg, { [s.segOn]: o.value === value, [s.segIconOnly]: iconOnly || !o.label })}
                    onMouseEnter={() => playSound("hover-item")}
                    onClick={(e) => {
                        e.stopPropagation();
                        if (o.value !== value) {
                            playSound("select-item");
                            onChange(o.value);
                        }
                    }}
                >
                    {o.icon ? <Icon name={o.icon} size={18} color={o.value === value ? o.color : undefined} /> : null}
                    {!iconOnly && o.label ? <div className={s.segLabel}>{o.label}</div> : null}
                </button>
            </Tip>
        ))}
    </div>
);

export const Keys = ({ keys }: { keys: string[] }) => (
    <div className={s.keys}>
        {keys.map((k, i) => (
            <div key={i} className={s.key}>
                {k}
            </div>
        ))}
    </div>
);

export const Check = ({
    checked,
    onChange,
    label,
    testId,
    disabled,
}: {
    checked: boolean;
    onChange: (v: boolean) => void;
    label?: ReactNode;
    testId?: string;
    disabled?: boolean;
}) => (
    <button
        type="button"
        role="checkbox"
        aria-checked={checked ? "true" : "false"}
        data-testid={testId}
        className={classNames(s.check, { [s.checkOn]: checked, [s.disabled]: disabled })}
        onClick={(e) => {
            e.stopPropagation();
            if (disabled) return;
            playSound(checked ? "toggle-off" : "toggle-on");
            onChange(!checked);
        }}
    >
        <div className={s.checkBox}>{checked ? <Icon name="check" size={13} /> : null}</div>
        {label ? <div className={s.checkLabel}>{label}</div> : null}
    </button>
);

export const Switch = ({ on, onChange, testId }: { on: boolean; onChange: (v: boolean) => void; testId?: string }) => (
    <button
        type="button"
        role="switch"
        aria-checked={on ? "true" : "false"}
        data-testid={testId}
        className={classNames(s.switch, { [s.switchOn]: on })}
        onClick={(e) => {
            e.stopPropagation();
            playSound(on ? "toggle-off" : "toggle-on");
            onChange(!on);
        }}
    >
        <div className={s.knob} />
    </button>
);

export const Foldout = ({
    title,
    count,
    children,
    initiallyOpen = false,
    testId,
}: {
    title: ReactNode;
    count?: number;
    children: ReactNode;
    initiallyOpen?: boolean;
    testId?: string;
}) => {
    const [open, setOpen] = useState(initiallyOpen);
    return (
        <div className={s.foldout} data-testid={testId}>
            <button
                type="button"
                className={s.foldoutHead}
                aria-expanded={open ? "true" : "false"}
                onClick={(e) => {
                    e.stopPropagation();
                    playSound("select-item");
                    setOpen(!open);
                }}
            >
                <Icon name={open ? "chevronDown" : "chevronRight"} size={14} />
                <div className={s.foldoutTitle}>{title}</div>
                {count ? <div className={s.foldoutCount}>{count}</div> : null}
            </button>
            {open ? <div className={s.foldoutBody}>{children}</div> : null}
        </div>
    );
};

export const Dot = ({ color }: { color: string }) => <div className={s.dot} style={{ backgroundColor: color }} />;
