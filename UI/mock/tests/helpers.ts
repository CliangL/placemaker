// Shared helpers for the mock UI tests.
import { expect, Page } from "@playwright/test";
import fs from "fs";
import path from "path";

export const SHOTS = path.join(__dirname, "..", "screenshots");

export async function open(page: Page, opts: { width?: number; height?: number } = {}) {
    if (opts.width && opts.height) await page.setViewportSize({ width: opts.width, height: opts.height });
    const errors: string[] = [];
    page.on("pageerror", (e) => errors.push(String(e)));
    page.on("console", (m) => {
        if (m.type() === "error") errors.push(m.text());
    });
    await page.goto("/");
    await expect(page.getByTestId("placemaker-panel")).toBeVisible();
    // make builds quick and deterministic for tests
    await page.evaluate(() => ((window as any).pm.backend.buildStepMs = 40));
    return errors;
}

/** World metres → page pixels (via the map's own transform). */
export async function screen(page: Page, x: number, z: number): Promise<[number, number]> {
    return page.evaluate(([wx, wz]) => (window as any).pm.toScreen([wx, wz]), [x, z]);
}

export async function clickWorld(page: Page, x: number, z: number, opts: { button?: "left" | "right"; modifiers?: ("Shift" | "Control")[] } = {}) {
    const [px, py] = await screen(page, x, z);
    for (const m of opts.modifiers ?? []) await page.keyboard.down(m);
    await page.mouse.click(px, py, { button: opts.button ?? "left" });
    for (const m of opts.modifiers ?? []) await page.keyboard.up(m);
}

/** Draws a closed polygon by clicking its corners and then the first corner again. */
export async function drawPolygon(page: Page, pts: [number, number][]) {
    for (const [x, z] of pts) await clickWorld(page, x, z);
    await clickWorld(page, pts[0][0], pts[0][1]);
}

/** An empty field in the fake city (blocks 3..5 × 2..4 are empty). */
export const FIELD: [number, number][] = [
    [520, 290],
    [770, 270],
    [800, 470],
    [540, 500],
];

export async function waitPlanned(page: Page) {
    await page.waitForFunction(() => {
        const b = (window as any).pm.backend;
        return !!b.plan && !b.planning && !b.stale;
    });
}

export async function shot(page: Page, name: string) {
    fs.mkdirSync(SHOTS, { recursive: true });
    await page.mouse.move(5, 300);
    await page.waitForTimeout(250);
    await page.screenshot({ path: path.join(SHOTS, name + ".png") });
}

export async function panelShot(page: Page, name: string) {
    fs.mkdirSync(SHOTS, { recursive: true });
    await page.waitForTimeout(250);
    const dock = page.getByTestId("placemaker-panel");
    const box = await dock.boundingBox();
    if (!box) throw new Error("panel not visible");
    const pad = 24;
    const top = Math.max(0, box.y - 420);
    await page.screenshot({
        path: path.join(SHOTS, name + ".png"),
        clip: { x: Math.max(0, box.x - pad), y: top, width: box.width + pad * 2, height: box.y + box.height - top + pad },
    });
}

export function stage(page: Page) {
    return page.getByTestId("placemaker-panel").getAttribute("data-stage");
}
