// (core) The central shelf for the mock backend without the host (Recipes/shelf/*.json, the same files the game reads through
// Core/Tool/Shelf.cs). With the host up, its catalog already carries each type's category, the shelved flags and the categories.
import type { CategoryInfo, RecipeInfo } from "../../src/contract";
import roads from "../../../Recipes/shelf/roads.json";
import parking from "../../../Recipes/shelf/parking.json";
import parks from "../../../Recipes/shelf/parks.json";
import beach from "../../../Recipes/shelf/beach.json";
import shops from "../../../Recipes/shelf/shops.json";
import nature from "../../../Recipes/shelf/nature.json";
import farms from "../../../Recipes/shelf/farms.json";
import sports from "../../../Recipes/shelf/sports.json";
import industry from "../../../Recipes/shelf/industry.json";
import transport from "../../../Recipes/shelf/transport.json";

type ShelfFile = {
    id: string;
    name: string;
    key?: string;
    order: number;
    hidden?: boolean;
    groups?: string[];
    types?: string[];
    hiddenTypes?: string[];
    hiddenStyles?: Record<string, string[]>;
};

const FILES: ShelfFile[] = ([roads, parking, parks, beach, shops, nature, farms, sports, industry, transport] as ShelfFile[]).slice().sort((a, b) => a.order - b.order);

function find(typeId: string, group?: string): ShelfFile | undefined {
    return FILES.find((c) => (c.types ?? []).includes(typeId)) ?? (group ? FILES.find((c) => (c.groups ?? []).includes(group)) : undefined);
}

export const SHELF_CATEGORIES: CategoryInfo[] = FILES.map((c) => ({
    id: c.id,
    name: c.key || "Placemaker.UI.MakeGroup." + c.id.charAt(0).toUpperCase() + c.id.slice(1),
    en: c.name,
    order: c.order,
    hidden: c.hidden || undefined,
}));

/** A recipe with the shelf's category and hidden flag (player recipes untouched). */
export function shelve(r: RecipeInfo): RecipeInfo {
    if (r.custom || !r.group || r.category) return r;
    const c = find(r.id, r.group);
    if (!c) return { ...r, category: "other" };
    const shelved = !!c.hidden || (c.hiddenTypes ?? []).includes(r.id);
    return { ...r, category: c.id, shelved: shelved || undefined };
}

export function styleShelved(typeId: string, group: string | undefined, styleId: string): boolean {
    const c = find(typeId, group);
    return !!c && (c.hiddenStyles?.[typeId] ?? []).includes(styleId);
}
