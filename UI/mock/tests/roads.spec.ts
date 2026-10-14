// s16 (real Core): roads say what they are — "Automatic · Two-Lane Road", a road legend by role, road role
// descriptions, and the road mix cards that set every road pick at once.
import { expect, test } from "@playwright/test";
import { drawPolygon, noTextOverflow, open, panelShot, shot, waitPlanned } from "./helpers";

const SITE: [number, number][] = [
    [380, 200],
    [900, 190],
    [920, 560],
    [400, 580],
];

async function waitCore(page: import("@playwright/test").Page) {
    await waitPlanned(page);
    await page.waitForFunction(() => (window as any).pm.backend.plan?.source === "core");
}

test("roads: Automatic names its road, legend by role, road mixes", async ({ page }) => {
    await open(page, { real: true });
    await page.waitForFunction(() => (window as any).pm.backend.hostUp !== undefined);
    await page.waitForTimeout(300);
    const layers: string[] = await page.evaluate(() => Array.from((window as any).pm.backend.hostLayers as Set<string>));
    test.skip(!layers.includes("roads"), "mock host has no real roads layer");

    await drawPolygon(page, SITE);
    await page.getByTestId("primary").click();
    const all = page.getByTestId("make-group-all");
    if (await all.count()) await all.click();
    await page.getByTestId("recipe-roads").click();
    await waitCore(page);
    const step = page.getByTestId("step-0");

    // Road types open: the mix cards, the street pick saying what Automatic is, a plain description per role.
    const group = step.getByTestId("group-roadTypes");
    if ((await group.getAttribute("data-open")) !== "true") await step.getByTestId("group-roadTypes-toggle").click();
    await expect(step.getByTestId("choice-roadMix-auto")).toHaveAttribute("aria-pressed", "true");
    await expect(step.getByTestId("choice-roadMix-downtown").getByTestId("road-swatch")).toBeVisible();
    await expect(step.getByTestId("asset-name-streetAsset")).toHaveText("Automatic · Two-Lane Road");
    // No avenues in the default grid: still says what Automatic would place, marked as not in this layout.
    await expect(step.getByTestId("asset-name-avenueAsset")).toHaveText("Automatic · Four-Lane Road");
    await expect(step.getByTestId("asset-unused-avenueAsset")).toBeVisible();
    await expect(step.getByTestId("asset-unused-streetAsset")).toHaveCount(0);
    await expect(step.getByTestId("description-streetAsset")).toHaveText("The ordinary streets between blocks.");

    // The legend lists only the roles of this plan.
    const legend = step.getByTestId("net-legend");
    await expect(legend).toBeVisible();
    await expect(legend.getByTestId("legend-street")).toContainText("Two-Lane Road");
    await expect(legend.getByTestId("legend-roundabout")).toHaveCount(0);
    await noTextOverflow(page, "roads design");
    await panelShot(page, "s16-roads-auto");
    await step.getByTestId("control-roundaboutAsset").scrollIntoViewIfNeeded();
    await panelShot(page, "s16-roads-auto-types");

    // A mix writes the picks; changing one pick makes it Custom.
    await step.getByTestId("choice-roadMix-downtown").click();
    await waitCore(page);
    await expect(step.getByTestId("choice-roadMix-downtown")).toHaveAttribute("aria-pressed", "true");
    await expect(step.getByTestId("asset-name-streetAsset")).not.toContainText("Automatic");
    await expect(legend.getByTestId("legend-street")).toContainText("Four-Lane Road");
    await step.getByTestId("control-streetAsset").scrollIntoViewIfNeeded();
    await panelShot(page, "s16-roads-downtown");
    await shot(page, "s16-roads-map");
    await step.getByTestId("choice-roadMix-auto").click();
    await waitCore(page);
    await expect(step.getByTestId("asset-name-streetAsset")).toHaveText("Automatic · Two-Lane Road");
});
