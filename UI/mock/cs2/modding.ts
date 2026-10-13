// Browser stand-in for the cohtml module registry: collects append()s per target and supports
// get/override for the selected-info-panel section map (so registration is exercised for real).
import { ComponentType } from "react";

export const SELECTED_INFO_SECTIONS = "game-ui/game/components/selected-info-panel/selected-info-sections/selected-info-sections.tsx";

export function createRegistry() {
    const appended: Record<string, ComponentType<any>[]> = {};
    const registry = new Map<string, Record<string, any>>();
    registry.set(SELECTED_INFO_SECTIONS, {
        selectedInfoSectionComponents: { "Game.UI.InGame.GeneralSection": () => null },
    });
    const api = {
        registry,
        appended,
        append(target: string, component: ComponentType<any>) {
            (appended[target] = appended[target] ?? []).push(component);
        },
        get(path: string, name: string) {
            return registry.get(path)?.[name];
        },
        override(path: string, name: string, value: unknown) {
            const m = registry.get(path) ?? {};
            registry.set(path, { ...m, [name]: value });
        },
        extend() {
            /* not used */
        },
        add(path: string, module: Record<string, any>) {
            registry.set(path, module);
        },
        hasAppend: (target: string) => !!appended[target],
        find: () => [],
        reset() {
            /* not used */
        },
    };
    return api;
}

export type MockRegistry = ReturnType<typeof createRegistry>;
