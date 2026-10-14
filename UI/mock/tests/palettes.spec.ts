// s18 (real Core): the plaza's ground palette chooser — option cards with a generated palette swatch and a
// one-line description; picking a palette lights its card.
import { expect, test } from "@playwright/test";
import { drawPolygon, noTextOverflow, open, panelShot, waitPlanned } from "./helpers";

const SITE: [number, number][] = [
    [420, 220],
    [760, 210],
    [600, 520],
];

async function waitCore(page: import("@playwright/test").Page) {
    await waitPlanned(page);
    await page.waitForFunction(() => (window as any).pm.backend.plan?.source === "core");
}

test("plaza: ground palette cards with swatches", async ({ page }) => {
    await open(page, { real: true });
    await page.waitForFunction(() => (window as any).pm.backend.hostUp !== undefined);
    await page.waitForTimeout(300);
    const layers: string[] = await page.evaluate(() => Array.from((window as any).pm.backend.hostLayers as Set<string>));
    test.skip(!layers.includes("plaza"), "mock host has no real plaza layer");

    await drawPolygon(page, SITE);
    await page.getByTestId("primary").click();
    const all = page.getByTestId("make-group-all");
    if (await all.count()) await all.click();
    await page.getByTestId("recipe-plaza").click();
    await waitCore(page);
    const step = page.getByTestId("step-0");
    const group = step.getByTestId("group-ground");
    if ((await group.getAttribute("data-open")) !== "true") await step.getByTestId("group-ground-toggle").click();

    await expect(step.getByTestId("choice-palette-auto")).toHaveAttribute("aria-pressed", "true");
    await expect(step.getByTestId("choice-palette-classic").getByTestId("palette-swatch")).toBeVisible();
    await expect(step.getByTestId("choice-palette-classic")).toContainText("Classic stone");
    await expect(step.getByTestId("choice-palette-terracotta").getByTestId("palette-swatch")).toBeVisible();
    await noTextOverflow(page, "plaza palettes");
    await panelShot(page, "s18-plaza-palettes");

    await step.getByTestId("choice-palette-cobble").click();
    await waitCore(page);
    await expect(step.getByTestId("choice-palette-cobble")).toHaveAttribute("aria-pressed", "true");
    await panelShot(page, "s18-plaza-palette-cobble");
});
