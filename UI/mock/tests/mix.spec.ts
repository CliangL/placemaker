import { expect, test } from "@playwright/test";
import { drawPolygon, FIELD, open, panelShot, shot, waitPlanned } from "./helpers";

async function toDesign(page: import("@playwright/test").Page, recipe?: string) {
    await drawPolygon(page, FIELD);
    await page.getByTestId("primary").click();
    if (recipe) await page.getByTestId(`recipe-${recipe}`).click();
    await waitPlanned(page);
}

test("mix and match: add an add-on to a single-layer recipe", async ({ page }) => {
    const errors = await open(page);
    await toDesign(page, "park");
    await expect(page.getByTestId("layer-tree")).toHaveCount(0);
    await page.getByTestId("add-layer").click();
    await expect(page.getByTestId("add-menu")).toBeVisible();
    await panelShot(page, "panel-add-menu");
    await page.getByTestId("add-playground").click();
    // the tree appears only now, with the new layer focused
    await expect(page.getByTestId("layer-tree")).toBeVisible();
    await expect(page.getByTestId("tree-0/0")).toContainText("Playground");
    await expect(page.getByTestId("step-editor")).toContainText("Playground");
    await waitPlanned(page);
    const layers = await page.evaluate(() => (window as any).pm.backend.plan.layers.map((l: any) => l.layer));
    expect(layers).toContain("playground");
    await expect(page.getByTestId("undo")).toContainText("Add layer");
    await panelShot(page, "panel-tree-addon");
    expect(errors).toEqual([]);
});

test("per-region override: one block of a road grid becomes a plaza", async ({ page }) => {
    await open(page);
    await toDesign(page, "roads-with-parks");
    await expect(page.getByTestId("layer-tree")).toBeVisible();
    const strip = page.getByTestId("regions-0");
    await expect(strip).toBeVisible();
    await strip.getByTestId("region-block:1").click();
    await expect(page.getByTestId("region-menu")).toBeVisible();
    await panelShot(page, "panel-region-menu");
    await page.getByTestId("override-plaza").click();
    await expect(page.getByTestId("tree-0/0@block:1")).toContainText("Plaza");
    await waitPlanned(page);
    const plaza = await page.evaluate(() => (window as any).pm.backend.plan.layers.filter((l: any) => l.layer === "plaza").map((l: any) => l.stepPath));
    expect(plaza).toEqual(["0/0@block:1"]);
    await shot(page, "08-roads-parks-override");
    await panelShot(page, "panel-region-override");
    // remove the override again
    await page.getByTestId("remove-0/0@block:1").click();
    await expect(page.getByTestId("tree-0/0@block:1")).toHaveCount(0);
});

test("tree: enable/disable, reorder, retarget, remove", async ({ page }) => {
    await open(page);
    await toDesign(page, "neighbourhood-park");
    await expect(page.getByTestId("tree-0/0")).toContainText("Playground");
    await expect(page.getByTestId("tree-0/1")).toContainText("Restrooms");
    await page.getByTestId("enable-0/0").click();
    await waitPlanned(page);
    let layers = await page.evaluate(() => (window as any).pm.backend.plan.layers.map((l: any) => l.layer));
    expect(layers).not.toContain("playground");
    await page.getByTestId("enable-0/0").click();
    await page.getByTestId("down-0/0").click();
    await expect(page.getByTestId("tree-0/1")).toContainText("Playground");
    // retarget the playground to the park's lawns
    await page.getByTestId("tree-0/1").click();
    await expect(page.getByTestId("target-row")).toBeVisible();
    await page.getByTestId("target-green").click();
    await expect(page.getByTestId("tree-0/1")).toContainText("each lawn");
    await page.getByTestId("remove-0/1").click();
    await expect(page.getByTestId("tree-0/1")).toHaveCount(0);
    await waitPlanned(page);
    layers = await page.evaluate(() => (window as any).pm.backend.plan.layers.map((l: any) => l.layer));
    expect(layers).toEqual(["park", "restrooms"]);
});

test("split recipe offers fills per part; save as my recipe", async ({ page }) => {
    await open(page);
    await toDesign(page, "park-plaza");
    await expect(page.getByTestId("tree-0/0")).toContainText("part 1");
    await expect(page.getByTestId("tree-0/1")).toContainText("Plaza");
    await shot(page, "09-park-plaza-split");
    await page.getByTestId("save-recipe").click();
    await page.getByTestId("save-recipe-name").fill("Half and half");
    await page.getByTestId("save-recipe-confirm").click();
    const custom = page.locator('[data-testid^="recipe-custom-"]');
    await expect(custom).toHaveCount(1);
    await expect(custom).toHaveAttribute("aria-pressed", "true");
    await expect(custom).toContainText("Half and half");
    await panelShot(page, "panel-custom-recipe");
    await page.getByTestId("delete-recipe").click();
    await expect(custom).toHaveCount(0);
});
