import { Issue, LocText, SettingValue, Stat } from "../../src/contract";
import { V2 } from "./geometry";

/** A recipe step as the mock backend stores it (mirrors Core.RecipeStep). */
export interface StepModel {
    layer: string;
    target: string;
    enabled: boolean;
    values: Record<string, SettingValue>;
    locked: string[];
    seed: number;
    markers: { kind: string; p: V2 }[];
    children: StepModel[];
    overrides: Record<string, StepModel>;
}

export interface PlanRegion { key: string; tags: string[]; ring: V2[]; areaM2: number }
export interface PlanNet { role: string; pts: V2[]; width: number }
export interface PlanObject { role: string; p: V2; yaw: number; r: number }

/** One executed step × region; same JSON as the mock host's /api/compose. */
export interface PlanLayer {
    layer: string;
    stepPath: string;
    regions: PlanRegion[];
    surfaces: { role: string; ring: V2[] }[];
    areas: { role: string; ring: V2[] }[];
    nets: PlanNet[];
    objects: PlanObject[];
    zoning: { zone: string; quad: V2[] }[];
    terraform: { kind: string; permanent: boolean; ring: V2[] }[];
    issues: Issue[];
    stats?: Stat[];
}

export interface ComposeResult {
    layers: PlanLayer[];
    issues: Issue[];
    stats: Stat[];
    perLayer: { stepPath: string; layer: string; regionKey: string; stats: Stat[]; cost: number; upkeep: number }[];
    cost: number;
    upkeep: number;
    permanent: LocText[];
    ms: number;
    source: "core" | "fake";
}
