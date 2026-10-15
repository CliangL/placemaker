// s42 — the Design stage as picker + editing bar + Shape / Extras grids, the compact Review and Area stages.
// Screenshots of real Core types at the default panel width land in mock/screenshots/s42-*.png.
import { expect, Page, test } from "@playwright/test";
import { drawPolygon, FIELD, noTextOverflow, open, panelShot, waitPlanned } from "./helpers";

async function realDesign(page: Page) {
    const errors = await open(page, { real: true });
    await page.waitForFunction(() => (window as any).pm.backend.hostUp !== undefined);
    await page.waitForTimeout(300);
    await page.evaluate(() => {
        localStorage.removeItem("placemaker.sections.v1");
        localStorage.removeItem("placemaker.browserCategory.v2");
        localStorage.removeItem("placemaker.categoryUse.v1");
    });
    await drawPolygon(page, FIELD);
    await page.getByTestId("primary").click();
    return errors;
}

async function pick(page: Page, group: string, recipe: string) {
    const chip = page.getByTestId(`make-group-${group}`);
    if (!(await chip.count())) await page.getByTestId("make-groups-more").click();
    await page.getByTestId(`make-group-${group}`).click();
    await page.getByTestId(`recipe-${recipe}`).click();
    await waitPlanned(page);
    await page.waitForTimeout(150);
}

test("s42 design: picker rows, editing bar, two-column Shape and Extras for real types", async ({ page }) => {
    const errors = await realDesign(page);
    test.skip(!(await page.evaluate(() => (window as any).pm.backend.hostLayers.has("waterfront"))), "no real Core layers");

    for (const [group, recipe] of [
        ["parks", "park"],
        ["water", "waterfront"],
        ["roads", "roads"],
        ["commercial", "stripMall"],
        ["squares", "plaza"],
    ]) {
        await pick(page, group, recipe);
        await expect(page.getByTestId(`recipe-${recipe}`)).toHaveAttribute("aria-pressed", "true");
        await expect(page.getByTestId("preset-bar")).toBeVisible();
        await expect(page.getByTestId("preset-default")).toBeVisible();
        await noTextOverflow(page, "design " + recipe);
        await panelShot(page, `s42-design-${recipe}`);
    }

    // categories: most used first, the rest behind "N more"
    const more = page.getByTestId("make-groups-more");
    if (await more.count()) {
        await more.click();
        await panelShot(page, "s42-design-categories-all");
        await more.click();
    }

    // search filters the three rows
    await page.getByTestId("browser-search-toggle").click();
    await page.getByTestId("browser-search").fill("garden");
    await expect(page.getByTestId("preset-hits")).toBeVisible();
    await noTextOverflow(page, "search");
    await panelShot(page, "s42-design-search");
    await page.getByTestId("browser-clear").click();

    // an open Shape section and an open extra with options
    await pick(page, "parks", "park");
    const first = page.locator('[data-testid="step-0"] [data-testid$="-toggle"][aria-expanded="false"]').first();
    await first.click();
    if (await page.getByTestId("feature-toggle-plaza").count()) {
        if ((await page.getByTestId("feature-plaza").getAttribute("data-enabled")) !== "true") await page.getByTestId("feature-toggle-plaza").click();
        await waitPlanned(page);
        await page.getByTestId("feature-fold-plaza").click();
        await expect(page.getByTestId("feature-options-plaza")).toBeVisible();
    }
    await noTextOverflow(page, "open sections");
    await panelShot(page, "s42-design-park-open");
    expect(errors).toEqual([]);
});

test("s42 editing bar: unsaved changes, undo changes, save as new from the + chip", async ({ page }) => {
    const errors = await open(page);
    await drawPolygon(page, FIELD);
    await page.getByTestId("primary").click();
    await page.getByTestId("recipe-park").click();
    await waitPlanned(page);
    await expect(page.getByTestId("preset-revert")).toHaveAttribute("aria-disabled", "true");
    await page.getByTestId("randomize").click();
    await waitPlanned(page);
    await expect(page.getByTestId("preset-modified")).toContainText("unsaved changes");
    await panelShot(page, "s42-editing-modified");
    await page.getByTestId("preset-revert").click();
    await expect(page.getByTestId("preset-modified")).toHaveCount(0);
    await page.getByTestId("preset-new").click();
    await page.getByTestId("preset-name-input").fill("My park");
    await page.getByTestId("preset-name-save").click();
    await expect(page.getByTestId("preset-bar-name")).toHaveText("My park");
    expect(errors).toEqual([]);
});
