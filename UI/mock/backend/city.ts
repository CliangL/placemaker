// The fake city under the mock map: a street grid with blocks (for "Pick block"), intersections
// (for "Road loop") and some buildings for context. World units are metres (x right, z down).
import { rng, V2 } from "./geometry";

export const GRID = { x0: 40, z0: 30, dx: 150, dz: 115, nx: 10, nz: 8, street: 16 };

export interface Block {
    k: number;
    j: number;
    ring: V2[];
    buildings: V2[][];
    empty: boolean;
}

function rect(x0: number, z0: number, x1: number, z1: number): V2[] {
    return [[x0, z0], [x1, z0], [x1, z1], [x0, z1]];
}

export const BLOCKS: Block[] = (() => {
    const out: Block[] = [];
    const R = rng(7);
    const h = GRID.street / 2;
    for (let k = 0; k < GRID.nx - 1; k++)
        for (let j = 0; j < GRID.nz - 1; j++) {
            const x0 = GRID.x0 + k * GRID.dx + h, x1 = GRID.x0 + (k + 1) * GRID.dx - h;
            const z0 = GRID.z0 + j * GRID.dz + h, z1 = GRID.z0 + (j + 1) * GRID.dz - h;
            const empty = (k + j * 3) % 5 === 1 || (k >= 3 && k <= 5 && j >= 2 && j <= 4);
            const buildings: V2[][] = [];
            if (!empty) {
                for (let x = x0 + 4; x < x1 - 14; x += 16 + R() * 6)
                    for (const side of [0, 1]) {
                        const d = 12 + R() * 10;
                        const w = 10 + R() * 4;
                        buildings.push(side === 0 ? rect(x, z0 + 3, x + w, z0 + 3 + d) : rect(x, z1 - 3 - d, x + w, z1 - 3));
                    }
            }
            out.push({ k, j, ring: rect(x0, z0, x1, z1), buildings, empty });
        }
    return out;
})();

export const NODES: V2[] = (() => {
    const out: V2[] = [];
    for (let k = 0; k < GRID.nx; k++) for (let j = 0; j < GRID.nz; j++) out.push([GRID.x0 + k * GRID.dx, GRID.z0 + j * GRID.dz]);
    return out;
})();

export const WORLD = { w: GRID.x0 * 2 + (GRID.nx - 1) * GRID.dx, h: GRID.z0 * 2 + (GRID.nz - 1) * GRID.dz };

export function blockAt(p: V2): Block | undefined {
    return BLOCKS.find((b) => p[0] >= b.ring[0][0] && p[0] <= b.ring[1][0] && p[1] >= b.ring[0][1] && p[1] <= b.ring[2][1]);
}

/** Union of picked blocks as their bounding rectangle (streets between them are taken in). */
export function blocksRing(picked: Block[]): V2[] {
    const x0 = Math.min(...picked.map((b) => b.ring[0][0]));
    const z0 = Math.min(...picked.map((b) => b.ring[0][1]));
    const x1 = Math.max(...picked.map((b) => b.ring[2][0]));
    const z1 = Math.max(...picked.map((b) => b.ring[2][1]));
    return rect(x0, z0, x1, z1);
}

// ───────────── paint mask → outline ─────────────

export const CELL = 4;

export function paintCells(cells: Set<string>, c: V2, size: number, shape: "round" | "square", angleDeg: number, erase: boolean) {
    const r = size / 2;
    const a = (-angleDeg * Math.PI) / 180;
    const ca = Math.cos(a), sa = Math.sin(a);
    for (let i = Math.floor((c[0] - r * 1.5) / CELL); i <= Math.ceil((c[0] + r * 1.5) / CELL); i++)
        for (let j = Math.floor((c[1] - r * 1.5) / CELL); j <= Math.ceil((c[1] + r * 1.5) / CELL); j++) {
            const px = (i + 0.5) * CELL - c[0], pz = (j + 0.5) * CELL - c[1];
            let hit: boolean;
            if (shape === "round") hit = px * px + pz * pz <= r * r;
            else {
                const u = px * ca - pz * sa, v = px * sa + pz * ca;
                hit = Math.abs(u) <= r && Math.abs(v) <= r;
            }
            if (!hit) continue;
            const key = i + "," + j;
            if (erase) cells.delete(key);
            else cells.add(key);
        }
}

/** Longest boundary loop of the painted cells (outer outline of the biggest patch; holes dropped). */
export function cellsOutline(cells: Set<string>): V2[] {
    const edges = new Map<string, V2[]>();
    const push = (a: V2, b: V2) => {
        const k = a[0] + "," + a[1];
        const l = edges.get(k) ?? [];
        l.push(b);
        edges.set(k, l);
    };
    cells.forEach((key) => {
        const [i, j] = key.split(",").map(Number);
        const has = (di: number, dj: number) => cells.has(i + di + "," + (j + dj));
        if (!has(0, -1)) push([i, j], [i + 1, j]);
        if (!has(1, 0)) push([i + 1, j], [i + 1, j + 1]);
        if (!has(0, 1)) push([i + 1, j + 1], [i, j + 1]);
        if (!has(-1, 0)) push([i, j + 1], [i, j]);
    });
    let best: V2[] = [];
    while (edges.size) {
        const startKey = edges.keys().next().value as string;
        const start = startKey.split(",").map(Number) as V2;
        const loop: V2[] = [start];
        let cur = start;
        for (let guard = 0; guard < 200000; guard++) {
            const k = cur[0] + "," + cur[1];
            const l = edges.get(k);
            if (!l || !l.length) break;
            const next = l.pop()!;
            if (!l.length) edges.delete(k);
            if (next[0] === start[0] && next[1] === start[1]) break;
            loop.push(next);
            cur = next;
        }
        if (loop.length > best.length) best = loop;
    }
    return best.map(([i, j]) => [i * CELL, j * CELL] as V2);
}
