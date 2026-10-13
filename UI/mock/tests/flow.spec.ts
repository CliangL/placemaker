import { expect, test } from "@playwright/test";
import { clickWorld, drawPolygon, FIELD, open, panelShot, shot, stage, waitPlanned } from "./helpers";

test("full flow: draw → design → review → build, then manage the result", async ({ page }) => {
    const errors = await open(page);
    await shot(page, "01-open-empty");

    const primary = page.getByTestId("primary");
    await expect(primary).toHaveAttribute("aria-disabled", "true");

    // drawing collapses the panel to one row
    await clickWorld(page, FIELD[0][0], FIELD[0][1]);
    await clickWorld(page, FIELD[1][0], FIELD[1][1]);
    await expect(page.getByTestId("panel-compact")).toBeVisible();
    await shot(page, "02-drawing-compact");
    await clickWorld(page, FIELD[2][0], FIELD[2][1]);
    await clickWorld(page, FIELD[3][0], FIELD[3][1]);
    await clickWorld(page, FIELD[0][0], FIELD[0][1]);
    await expect(page.getByTestId("panel-compact")).toBeHidden();
    await expect(page.getByTestId("area-readout")).toContainText("ha");
    await expect(page.getByTestId("undo")).toContainText("Close outline");
    await shot(page, "03-area-closed");

    await expect(primary).toHaveAttribute("aria-disabled", "false");
    await primary.click();
    expect(await stage(page)).toBe("design");
    await waitPlanned(page);
    await expect(page.getByTestId("step-editor")).toBeVisible();
    // single-layer recipe: no tree chrome
    await expect(page.getByTestId("layer-tree")).toHaveCount(0);
    await shot(page, "04-design-park");
    await panelShot(page, "panel-design-park");

    await page.keyboard.press("Enter");
    expect(await stage(page)).toBe("review");
    await waitPlanned(page);
    await expect(page.getByTestId("review-cost")).toContainText("¢");
    await expect(page.getByTestId("review-stats")).toBeVisible();
    await shot(page, "05-review");
    await panelShot(page, "panel-review");

    await expect(primary).toContainText("Build");
    await primary.click();
    await expect(page.getByTestId("stage-building")).toBeVisible();
    await panelShot(page, "panel-building");
    await expect(page.getByTestId("stage-area")).toBeVisible({ timeout: 10000 });
    // built: area cleared, design kept, group on the map
    expect(await page.evaluate(() => (window as any).pm.backend.groups.length)).toBe(1);
    expect(await page.evaluate(() => (window as any).pm.backend.ring.length)).toBe(0);
    await shot(page, "06-built");

    // manage: click the built group
    await clickWorld(page, 650, 380);
    await expect(page.getByTestId("stage-manage")).toBeVisible();
    await page.getByTestId("manage-rename").click();
    await page.getByTestId("rename-input").fill("Riverside Park");
    await page.getByTestId("rename-save").click();
    await expect(page.getByTestId("manage-name")).toHaveText("Riverside Park");
    await panelShot(page, "panel-manage");
    await page.getByTestId("manage-remove").click();
    await expect(page.getByTestId("confirm-dialog")).toBeVisible();
    await shot(page, "07-remove-confirm");
    await page.getByTestId("confirm-ok").click();
    expect(await page.evaluate(() => (window as any).pm.backend.groups.length)).toBe(0);

    expect(errors).toEqual([]);
});

test("going back to Area keeps the design; switching recipe keeps the area", async ({ page }) => {
    await open(page);
    await drawPolygon(page, FIELD);
    await page.getByTestId("primary").click();
    await page.getByTestId("recipe-plaza").click();
    await waitPlanned(page);
    await page.getByTestId("choice-pattern-grid").click();
    await expect(page.getByTestId("choice-pattern-grid")).toHaveAttribute("aria-pressed", "true");

    // back to Area via the stage bar
    await page.getByTestId("stagebar-area").click();
    expect(await stage(page)).toBe("area");
    // adjust the shape
    const before = await page.evaluate(() => (window as any).pm.backend.ring.length);
    expect(before).toBe(4);
    // forward again: the design is intact
    await page.getByTestId("stagebar-design").click();
    expect(await stage(page)).toBe("design");
    await expect(page.getByTestId("recipe-plaza")).toHaveAttribute("aria-pressed", "true");
    await expect(page.getByTestId("choice-pattern-grid")).toHaveAttribute("aria-pressed", "true");

    // switching recipe keeps the area
    const area = await page.evaluate(() => (window as any).pm.backend.state().area.areaM2);
    await page.getByTestId("recipe-park").click();
    await waitPlanned(page);
    expect(await page.evaluate(() => (window as any).pm.backend.state().area.areaM2)).toBe(area);
    // and undo brings the plaza back
    await page.getByTestId("undo").click();
    await expect(page.getByTestId("recipe-plaza")).toHaveAttribute("aria-pressed", "true");
});

test("stage bar: unreachable stages are locked, reachable ones roll back", async ({ page }) => {
    await open(page);
    await expect(page.getByTestId("stagebar-design")).toHaveAttribute("data-state", "locked");
    await expect(page.getByTestId("stagebar-review")).toHaveAttribute("data-state", "locked");
    await drawPolygon(page, FIELD);
    await expect(page.getByTestId("stagebar-design")).toHaveAttribute("data-state", "reachable");
    await page.getByTestId("stagebar-review").click();
    expect(await stage(page)).toBe("review");
    await expect(page.getByTestId("stagebar-area")).toHaveAttribute("data-state", "reachable");
    await expect(page.getByTestId("stagebar-review")).toHaveAttribute("data-state", "current");
});
