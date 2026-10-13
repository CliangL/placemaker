// Against the REAL Core layers served by the mock host (whatever GeneratorRegistry.All returns).
// Skips itself when the host has no layers, so the suite stays green on a branch without them.
import { expect, test } from "@playwright/test";
import { drawPolygon, FIELD, open, panelShot, shot, waitPlanned } from "./helpers";

async function realLayers(page: import("@playwright/test").Page) {
    await page.waitForFunction(() => (window as any).pm.backend.hostUp !== undefined);
    await page.waitForTimeout(300);
    return page.evaluate(() => Array.from((window as any).pm.backend.hostLayers as Set<string>));
}

async function waitCore(page: import("@playwright/test").Page) {
    await waitPlanned(page);
    await page.waitForFunction(() => (window as any).pm.backend.plan?.source === "core");
}

test("real Core: shipped recipes in the Make row, real plans on the map", async ({ page }) => {
    const errors = await open(page, { real: true });
    const layers = await realLayers(page);
    test.skip(!layers.includes("park"), "mock host has no real park layer on this branch");

    await drawPolygon(page, FIELD);
    await page.getByTestId("primary").click();
    for (const id of ["park", "plaza", "campground", "split-park-plaza", "park-with-playground"])
        if (await page.evaluate((r) => (window as any).pm.backend.recipes.some((x: any) => x.info.id === r), id))
            await expect(page.getByTestId(`recipe-${id}`)).toBeVisible();

    await page.getByTestId("recipe-park").click();
    await waitCore(page);
    await shot(page, "real-01-park-design");
    await panelShot(page, "real-panel-park-design");

    await page.getByTestId("add-layer").click();
    await expect(page.getByTestId("add-menu")).toBeVisible();
    await panelShot(page, "real-panel-add-menu");
    const addOn = (await page.getByTestId("add-fountain").count()) ? "fountain" : "playground";
    await page.getByTestId(`add-${addOn}`).click();
    await waitCore(page);
    const used = await page.evaluate(() => (window as any).pm.backend.plan.layers.map((l: any) => l.layer));
    expect(used).toContain(addOn);
    await panelShot(page, "real-panel-tree");

    await page.getByTestId("primary").click();
    await waitCore(page);
    await expect(page.getByTestId("review-cost")).toContainText("¢");
    await shot(page, "real-02-park-review");
    await panelShot(page, "real-panel-review");

    await page.getByTestId("stagebar-design").click();
    if (await page.getByTestId("recipe-split-park-plaza").count()) {
        await page.getByTestId("recipe-split-park-plaza").click();
        await waitCore(page);
        await shot(page, "real-03-split-park-plaza");
        await panelShot(page, "real-panel-split");
    }
    if (await page.getByTestId("recipe-plaza").count()) {
        await page.getByTestId("recipe-plaza").click();
        await waitCore(page);
        await shot(page, "real-04-plaza");
    }
    expect(errors).toEqual([]);
});
