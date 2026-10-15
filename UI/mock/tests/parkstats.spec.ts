// s17 — Park stats in Review (real Core estimator through the mock host): suggested values, override,
// reset, "keep within vanilla range" clamping and the "how it's calculated" expander.
import { expect, Page, test } from "@playwright/test";
import { drawPolygon, FIELD, open, panelShot, waitPlanned, pickType } from "./helpers";

/** Draw, pick a type from the "All" grid, wait for the real Core plan, go to Review. */
async function toReview(page: Page, recipe: string) {
    await open(page, { real: true });
    await page.waitForFunction(() => (window as any).pm.backend.hostUp !== undefined);
    await page.waitForTimeout(300);
    const layers: string[] = await page.evaluate(() => Array.from((window as any).pm.backend.hostLayers as Set<string>));
    test.skip(!layers.includes("park"), "mock host has no real layers");
    await drawPolygon(page, FIELD);
    await page.getByTestId("primary").click();
    const all = page.getByTestId("make-group-all");
    if (await all.count()) await all.click();
    await pickType(page, recipe);
    await waitCore(page);
    // Straight to Review (entrances etc. are not what this test is about).
    await page.evaluate(() => (window as any).pm.backend.onTrigger("goToStage", ["review"]));
    await waitCore(page);
    await expect(page.getByTestId("stage-review")).toBeVisible();
}

async function waitCore(page: Page) {
    await waitPlanned(page);
    await page.waitForFunction(() => (window as any).pm.backend.plan?.source === "core");
}

test("park stats: suggested, override, clamp to vanilla range, reset", async ({ page }) => {
    await toReview(page, "park");

    const section = page.getByTestId("park-stats");
    await expect(section).toBeVisible();
    // folded: five tiles; Edit unfolds the sliders
    await expect(page.getByTestId("park-tile-attractiveness")).toBeVisible();
    await page.getByTestId("park-stats-toggle").click();
    const attr = page.getByTestId("park-stat-attractiveness-value");
    await expect(attr).not.toHaveText("");
    await expect(page.getByTestId("park-stat-attractiveness")).toContainText("In game");
    await expect(page.getByTestId("park-stat-leisure")).toContainText("Shown only");
    // the mock site has no roads: no frontage for the companion park building
    await expect(page.getByTestId("park-stats-no-road")).toBeVisible();
    await page.getByTestId("park-stat-attractiveness-how-toggle").click();
    await expect(page.getByTestId("park-stat-attractiveness-how")).toContainText("Similar to");
    await panelShot(page, "panel-park-stats");

    const suggested = Number((await attr.textContent())?.replace(/[^\d]/g, ""));
    // Far above the vanilla range: clamped while "keep within vanilla range" is on.
    await page.evaluate(() => {
        const be = (window as any).pm.backend;
        be.parkStats.overrides.attractiveness = 400;
        be.schedulePlan(0);
    });
    await waitCore(page);
    const clamped = Number((await attr.textContent())?.replace(/[^\d]/g, ""));
    expect(clamped).toBeLessThan(400);
    expect(clamped).toBeGreaterThanOrEqual(suggested);
    await expect(page.getByTestId("park-stat-attractiveness-reset")).toBeVisible();

    await page.getByTestId("park-stats-keep-vanilla").click();
    await waitPlanned(page);
    await expect(attr).toHaveText("400");
    await expect(page.getByTestId("park-stats-outside")).toBeVisible();
    await panelShot(page, "panel-park-stats-off");

    await page.getByTestId("park-stats-reset-all").click();
    await waitPlanned(page);
    await expect(attr).toHaveText(String(suggested));
});

test("park stats: not offered for parking", async ({ page }) => {
    await toReview(page, "parking");
    await expect(page.getByTestId("park-stats")).toHaveCount(0);
});
