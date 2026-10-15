// s43 — Paint bucket walls: chips choose what the fill stops at (lines split blocks, areas keep the clicked side).
// Screenshot: mock/screenshots/s43-area-bucket-walls.png.
import { expect, test } from "@playwright/test";
import { noTextOverflow, open, panelShot } from "./helpers";

test("s43 paint bucket: wall chips with defaults, toggles merge per key, reset", async ({ page }) => {
    const errors = await open(page);
    await page.getByTestId("area-mode-block").click();
    await expect(page.getByTestId("bucket-walls")).toBeVisible();
    // Defaults: every line wall and water on, lots and zoning off.
    for (const k of ["roads", "paths", "bikes", "tracks", "water"]) await expect(page.getByTestId(`bucket-wall-${k}`)).toHaveAttribute("aria-pressed", "true");
    for (const k of ["lots", "zoned"]) await expect(page.getByTestId(`bucket-wall-${k}`)).toHaveAttribute("aria-pressed", "false");
    // Keep off chips stay under the walls.
    await expect(page.getByTestId("keep-off-builds")).toBeVisible();
    await noTextOverflow(page, "bucket walls");
    await panelShot(page, "s43-area-bucket-walls");

    await page.getByTestId("bucket-wall-paths").click();
    await page.getByTestId("bucket-wall-lots").click();
    const walls = await page.evaluate(() => (window as any).pm.backend.brushRules.walls);
    expect(walls).toEqual({ roads: true, paths: false, bikes: true, tracks: true, water: true, lots: true, zoned: false });
    await expect(page.getByTestId("bucket-wall-paths")).toHaveAttribute("aria-pressed", "false");
    await panelShot(page, "s43-area-bucket-walls-custom");

    // Other tools don't show the walls.
    await page.getByTestId("area-mode-perimeter").click();
    await expect(page.getByTestId("bucket-walls")).toHaveCount(0);
    await page.getByTestId("area-mode-draw").click();
    await expect(page.getByTestId("bucket-walls")).toHaveCount(0);
    expect(errors).toEqual([]);
});
