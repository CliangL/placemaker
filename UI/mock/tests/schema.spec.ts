import { expect, test } from "@playwright/test";
import { clickWorld, drawPolygon, FIELD, open, panelShot, waitPlanned } from "./helpers";

async function design(page: import("@playwright/test").Page, recipe: string) {
    await drawPolygon(page, FIELD);
    await page.getByTestId("primary").click();
    await page.getByTestId(`recipe-${recipe}`).click();
    await waitPlanned(page);
}

test("every control kind renders from the schema (essential + More options)", async ({ page }) => {
    const errors = await open(page);
    await design(page, "parking");
    await expect(page.getByTestId("control-angle")).toHaveAttribute("data-kind", "choice");
    await expect(page.getByTestId("control-entrances")).toHaveAttribute("data-kind", "markers");
    await expect(page.getByTestId("control-surface")).toHaveAttribute("data-kind", "asset");
    await expect(page.getByTestId("control-greenery")).toHaveAttribute("data-kind", "toggle");
    // More tier is folded away
    await expect(page.getByTestId("control-aisle")).toHaveCount(0);
    await page.getByTestId("more-options").getByRole("button").first().click();
    await expect(page.getByTestId("control-aisle")).toHaveAttribute("data-kind", "slider");
    await expect(page.getByTestId("slider-aisle-value")).toHaveText("6.0 m");
    await expect(page.getByTestId("slider-chargers-value")).toHaveText("0");
    await expect(page.getByTestId("slider-fee-value")).toHaveText("¢5");
    await panelShot(page, "panel-parking-more");

    await page.getByTestId("recipe-plaza").click();
    await page.getByTestId("more-options").getByRole("button").first().click();
    await expect(page.getByTestId("control-arrangement")).toHaveAttribute("data-kind", "arrangement");
    await page.getByTestId("recipe-park").click();
    await page.getByTestId("more-options").getByRole("button").first().click();
    await expect(page.getByTestId("control-treeSpecies")).toHaveAttribute("data-kind", "assetMulti");
    expect(errors).toEqual([]);
});

test("visibleWhen, lock indicator, reset-to-default, randomize keeps locked values", async ({ page }) => {
    await open(page);
    await design(page, "park");
    await page.getByTestId("more-options").getByRole("button").first().click();
    await expect(page.getByTestId("control-lakeSize")).toHaveCount(0);
    await page.getByTestId("toggle-lake").click();
    await expect(page.getByTestId("control-lakeSize")).toBeVisible();

    // no reset/lock until the player changes a value
    await expect(page.getByTestId("reset-pathWidth")).toHaveCount(0);
    await expect(page.getByTestId("lock-pathWidth")).toHaveCount(0);
    const track = page.getByTestId("slider-pathWidth").locator("div").first();
    await track.scrollIntoViewIfNeeded();
    const box = (await track.boundingBox())!;
    await page.mouse.click(box.x + box.width * 0.9, box.y + box.height / 2);
    await expect(page.getByTestId("slider-pathWidth-value")).toHaveText("7.5 m");
    await expect(page.getByTestId("lock-pathWidth")).toBeVisible();
    await expect(page.getByTestId("reset-pathWidth")).toBeVisible();
    await panelShot(page, "panel-lock-reset");

    // randomize twice: the locked value never moves
    await page.getByTestId("randomize").click();
    await page.getByTestId("randomize").click();
    await expect(page.getByTestId("slider-pathWidth-value")).toHaveText("7.5 m");

    await page.getByTestId("reset-pathWidth").click();
    await expect(page.getByTestId("slider-pathWidth-value")).toHaveText("4.0 m");
    await expect(page.getByTestId("lock-pathWidth")).toHaveCount(0);
});

test("asset picker: search, pick, automatic; multi-select chips; arrangement editor", async ({ page }) => {
    await open(page);
    await design(page, "parking");
    await page.getByTestId("asset-button-surface").click();
    await expect(page.getByTestId("asset-picker")).toBeVisible();
    await expect(page.getByTestId("asset-concrete")).toBeVisible();
    await page.getByTestId("asset-search").fill("conc");
    await expect(page.getByTestId("asset-asphalt")).toHaveCount(0);
    await page.getByTestId("asset-concrete").click();
    await expect(page.getByTestId("asset-picker")).toHaveCount(0);
    await expect(page.getByTestId("asset-button-surface")).toContainText("Concrete");
    // recently used shows up next time
    await page.getByTestId("asset-button-surface").click();
    await expect(page.getByTestId("asset-picker")).toContainText("Recently used");
    await panelShot(page, "panel-asset-picker");
    await page.getByTestId("asset-automatic").click();
    await expect(page.getByTestId("asset-button-surface")).toContainText("Automatic");

    await page.getByTestId("recipe-park").click();
    await page.getByTestId("more-options").getByRole("button").first().click();
    await page.getByTestId("assetmulti-add-treeSpecies").click();
    await page.getByTestId("asset-linden").click();
    await page.getByTestId("asset-ginkgo").click();
    await page.getByTestId("asset-picker-done").click();
    await expect(page.getByTestId("chip-linden")).toBeVisible();
    await expect(page.getByTestId("chip-ginkgo")).toBeVisible();
    await page.getByTestId("chip-remove-linden").click();
    await expect(page.getByTestId("chip-linden")).toHaveCount(0);

    await page.getByTestId("recipe-plaza").click();
    await page.getByTestId("more-options").getByRole("button").first().click();
    const arr = page.getByTestId("arrangement-arrangement");
    await expect(arr).toContainText("Wooden Bench");
    await page.getByTestId("arrangement-add-arrangement").click();
    await page.getByTestId("asset-bollard").click();
    await expect(arr).toContainText("Bollard");
    await page.getByTestId("arr-remove-0").click();
    await panelShot(page, "panel-arrangement");
});

test("disabled primary explains why; markers flow; permanent effects need acknowledgement", async ({ page }) => {
    await open(page);
    const primary = page.getByTestId("primary");
    await expect(primary).toHaveAttribute("aria-disabled", "true");
    await expect(page.getByTestId("primary-reason")).toContainText("Draw, pick or paint");
    await primary.hover();
    await expect(page.getByTestId("tooltip")).toContainText("Draw, pick or paint");
    await panelShot(page, "panel-disabled-reason");

    // parking needs an entrance: the primary turns into "Place entrances"
    await design(page, "parking");
    await expect(primary).toHaveAttribute("data-action", "placeMarkers");
    await expect(page.getByTestId("next-step")).toContainText("entrance");
    await primary.click();
    await expect(page.getByTestId("marker-banner")).toBeVisible();
    await panelShot(page, "panel-markers");
    await clickWorld(page, (FIELD[0][0] + FIELD[1][0]) / 2, (FIELD[0][1] + FIELD[1][1]) / 2);
    await expect(page.getByTestId("markers-entrance")).toContainText("1 placed");
    await page.getByTestId("marker-done").click();
    await expect(page.getByTestId("marker-banner")).toHaveCount(0);
    await expect(primary).toHaveAttribute("data-action", "toReview");

    // a pond is permanent: Build waits for the tick
    await page.getByTestId("recipe-park").click();
    await page.getByTestId("preset-pond").click();
    await primary.click();
    await waitPlanned(page);
    await expect(page.getByTestId("permanent")).toBeVisible();
    await expect(primary).toHaveAttribute("aria-disabled", "true");
    await expect(page.getByTestId("primary-reason")).toContainText("Tick the box");
    await panelShot(page, "panel-permanent");
    await page.getByTestId("ack-permanent").click();
    await expect(primary).toHaveAttribute("aria-disabled", "false");
});

test("not enough money: cost turns red, Build is disabled with the reason", async ({ page }) => {
    await open(page);
    await page.evaluate(() => {
        const b = (window as any).pm.backend;
        b.money = 1000;
    });
    await drawPolygon(page, FIELD);
    await page.getByTestId("primary").click();
    await page.getByTestId("primary").click();
    await waitPlanned(page);
    await expect(page.getByTestId("cant-afford")).toBeVisible();
    await expect(page.getByTestId("primary")).toHaveAttribute("aria-disabled", "true");
    await expect(page.getByTestId("primary-reason")).toContainText("Not enough money");
});
