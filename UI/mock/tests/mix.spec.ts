import { expect, test } from "@playwright/test";
import { drawPolygon, FIELD, open, panelShot, shot, waitPlanned } from "./helpers";

async function toDesign(page: import("@playwright/test").Page, recipe?: string) {
    await drawPolygon(page, FIELD);
    await page.getByTestId("primary").click();
    if (recipe) await page.getByTestId(`recipe-${recipe}`).click();
    await waitPlanned(page);
}

test("Make: type cards with icon + one line, mixes grouped; no tree editor, no recipe saving", async ({ page }) => {
    const errors = await open(page);
    await toDesign(page, "park");
    const cards = page.getByTestId("recipes");
    await expect(cards.getByTestId("mixes")).toHaveText("Mixes");
    // every card: icon + name + description
    for (const id of ["parking", "plaza", "park", "roads", "campground", "roads-with-parks", "park-plaza"]) {
        const card = page.getByTestId(`recipe-${id}`);
        await expect(card.locator("img")).toHaveCount(1);
        expect((await card.innerText()).split("\n").filter(Boolean).length).toBeGreaterThanOrEqual(2);
    }
    // mixes come after the "Mixes" heading
    const mixY = (await cards.getByTestId("mixes").boundingBox())!.y;
    expect((await page.getByTestId("recipe-roads-with-parks").boundingBox())!.y).toBeGreaterThan(mixY);
    expect((await page.getByTestId("recipe-park").boundingBox())!.y).toBeLessThan(mixY);
    // removed in playtest 1
    for (const id of ["add-layer", "layer-tree", "save-recipe", "delete-recipe"]) await expect(page.getByTestId(id)).toHaveCount(0);
    expect(errors).toEqual([]);
});

test("a mix shows each layer's options under its own heading", async ({ page }) => {
    await open(page);
    await toDesign(page, "roads-with-parks");
    await expect(page.getByTestId("step-0")).toContainText("Roads");
    await expect(page.getByTestId("step-0/0")).toContainText("Park in each block");
    await expect(page.getByTestId("step-0").getByTestId("control-blockSize").first()).toBeVisible();
    await expect(page.getByTestId("step-0/0").getByTestId("control-trees")).toBeVisible();
    await shot(page, "08-roads-parks");
    await panelShot(page, "panel-mix-roads-parks");

    await page.getByTestId("recipe-park-plaza").click();
    await waitPlanned(page);
    await expect(page.getByTestId("step-0/0")).toContainText("Park — Part 1");
    await expect(page.getByTestId("step-0/1")).toContainText("Plaza — Part 2");
    await shot(page, "09-park-plaza-split");
});

test("a mix's add-on can be switched off and on", async ({ page }) => {
    await open(page);
    await toDesign(page, "neighbourhood-park");
    await expect(page.getByTestId("step-0/0")).toContainText("Playground");
    await page.getByTestId("enable-0/0").click();
    await waitPlanned(page);
    let layers = await page.evaluate(() => (window as any).pm.backend.plan.layers.map((l: any) => l.layer));
    expect(layers).not.toContain("playground");
    await expect(page.getByTestId("step-0/0").getByTestId("control-list")).toHaveCount(0);
    await page.getByTestId("enable-0/0").click();
    await waitPlanned(page);
    layers = await page.evaluate(() => (window as any).pm.backend.plan.layers.map((l: any) => l.layer));
    expect(layers).toContain("playground");
});

test("places-inside slot is reserved under the type cards (hidden until round 3)", async ({ page }) => {
    await open(page);
    await toDesign(page, "park");
    await expect(page.getByTestId("places-inside")).toHaveCount(0);
    await page.evaluate(() => ((window as any).__pmPlacesInside = true));
    await page.getByTestId("recipe-plaza").click();
    await expect(page.getByTestId("places-inside")).toBeVisible();
    const placesY = (await page.getByTestId("places-inside").boundingBox())!.y;
    expect(placesY).toBeGreaterThan((await page.getByTestId("recipes").boundingBox())!.y);
    expect(placesY).toBeLessThan((await page.getByTestId("options").boundingBox())!.y);
});
