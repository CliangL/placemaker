// Vanilla game UI components resolved from the cohtml module registry.
// Adapted from Grid Road Generator (Ti4goc); vanilla.ts pattern from CS2-NetworkTools
// (Luca Rager, MIT, https://github.com/lucarager/CS2-NetworkTools).
//
// cs2/ui does not export Slider/Checkbox/ToolButton/Scrollable, so they are looked up by module
// path once at register(). Every lookup may fail (game update, renamed module): callers must
// treat VC.X as optional and render their own fallback — never assume presence.
//
// Deliberately NOT used: the editor widgets (game-ui/editor/widgets/fields/*). Grid Road Generator
// measured them as slow in-game and their spacing ignores CSS; our own controls are lighter.
import { ModuleRegistry } from "cs2/modding";

type ModulePath = { path: string; components: string[] };
type ThemePath = { path: string; name: string };

const modulePaths: ModulePath[] = [
    { path: "game-ui/common/input/slider/slider.tsx", components: ["Slider"] },
    { path: "game-ui/common/input/toggle/checkbox/checkbox.tsx", components: ["Checkbox"] },
    { path: "game-ui/common/tooltip/tooltip.tsx", components: ["Tooltip"] },
    { path: "game-ui/common/scrolling/scrollable.tsx", components: ["Scrollable"] },
    { path: "game-ui/game/components/tool-options/tool-button/tool-button.tsx", components: ["ToolButton"] },
    { path: "game-ui/game/components/tool-options/mouse-tool-options/mouse-tool-options.tsx", components: ["Section"] },
    {
        path: "game-ui/game/components/selected-info-panel/shared-components/info-section/info-section.tsx",
        components: ["InfoSection"],
    },
    {
        path: "game-ui/game/components/selected-info-panel/shared-components/info-row/info-row.tsx",
        components: ["InfoRow"],
    },
];

const themePaths: ThemePath[] = [
    { path: "game-ui/common/tooltip/description-tooltip/description-tooltip.module.scss", name: "descriptionTooltip" },
    { path: "game-ui/game/components/tool-options/tool-button/tool-button.module.scss", name: "toolButton" },
    { path: "game-ui/game/components/selected-info-panel/shared-components/info-row/info-row.module.scss", name: "infoRow" },
];

export const VC: Record<string, any> = {};
export const VT: Record<string, Record<string, string>> = {};
export const VF: { FOCUS_DISABLED?: unknown; FOCUS_AUTO?: unknown } = {};

export const initializeVanilla = (moduleRegistry: ModuleRegistry) => {
    modulePaths.forEach(({ path, components }) => {
        let module: Record<string, any> | undefined;
        try {
            module = moduleRegistry.registry.get(path);
        } catch {
            module = undefined;
        }
        components.forEach((component) => (VC[component] = module?.[component]));
    });
    themePaths.forEach(({ path, name }) => {
        try {
            VT[name] = moduleRegistry.registry.get(path)?.classes ?? {};
        } catch {
            VT[name] = {};
        }
    });
    try {
        const focusKey = moduleRegistry.registry.get("game-ui/common/focus/focus-key.ts");
        VF.FOCUS_DISABLED = focusKey?.FOCUS_DISABLED;
        VF.FOCUS_AUTO = focusKey?.FOCUS_AUTO;
    } catch {
        /* optional */
    }
};
