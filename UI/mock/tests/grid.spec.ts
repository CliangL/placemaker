// Playtest feedback "more vertically space conscious — grids, not a file tree; a little wider, multiple
// columns when it looks decent": grid browser (category chips ▸ type tiles ▸ preset chips), a resizable
// panel (left edge, remembered, 360rem … 45 % of the screen) and 1 ↔ 2 column content.
import { expect, Page, test } from "@playwright/test";
import { drawPolygon, FIELD, noTextOverflow, open, panelShot, waitPlanned } from "./helpers";

async function toDesign(page: Page, recipe = "park") {
    await drawPolygon(page, FIELD);
    await page.getByTestId("primary").click();
    await page.getByTestId(`recipe-${recipe}`).click();
    await waitPlanned(page);
}

const width = async (page: Page) => Number(await page.getByTestId("placemaker-wrap").getAttribute("data-width"));
const box = async (page: Page, id: string) => (await page.getByTestId(id).first().boundingBox())!;

/** Drags the panel's left edge by dx pixels (negative = wider). */
async function dragEdge(page: Page, dx: number) {
    const b = await box(page, "panel-resize");
    const x = b.x + b.width / 2,
        y = b.y + 40;
    await page.mouse.move(x, y);
    await page.mouse.down();
    for (let i = 1; i <= 6; i++) await page.mouse.move(x + (dx * i) / 6, y);
    await page.mouse.up();
}

test("browser is a grid: category chips ▸ type tiles ▸ preset chips, compact", async ({ page }) => {
    const errors = await open(page);
    await toDesign(page, "park");
    expect(await width(page)).toBe(560); // 1920 px wide screen → the wide default

    // types side by side, one tile row at this width
    const tiles = await page.locator('[data-testid="recipes"] [data-testid^="recipe-"]').evaluateAll((els) => els.map((e) => e.getAttribute("data-testid")));
    expect(tiles).toEqual(["recipe-roads", "recipe-park", "recipe-campground", "recipe-plaza", "recipe-parking"]);
    const roads = await box(page, "recipe-roads");
    const parking = await box(page, "recipe-parking");
    expect(Math.abs(roads.y - parking.y)).toBeLessThan(2); // one row
    const chips = await page.locator('[data-testid="make-groups"] > button').evaluateAll((els) => els.map((e) => e.getAttribute("data-testid")));
    expect(chips).toEqual(["make-group-all", "make-group-roads", "make-group-parks", "make-group-squares", "make-group-parking"]);
    await expect(page.getByTestId("recipe-park")).toHaveAttribute("aria-pressed", "true");

    // presets of the selected type as chips, several per row
    const presets = page.getByTestId("presets-park");
    for (const id of ["default", "formal", "wild", "pond"]) await expect(presets.getByTestId(`preset-${id}`)).toBeVisible();
    const d = await box(page, "preset-default");
    const f = await box(page, "preset-formal");
    expect(Math.abs(f.y - d.y)).toBeLessThan(2);
    expect(f.x).toBeGreaterThan(d.x);

    // the whole browser: search + chips + tiles + presets in a few lines
    const browser = await box(page, "type-browser");
    expect(browser.height).toBeLessThan(120);
    await noTextOverflow(page, "grid browser wide");
    await panelShot(page, "grid-browser-wide");

    // a category narrows the tiles; the selected type stays selected and its presets stay listed
    await page.getByTestId("make-group-squares").click();
    await expect(page.getByTestId("make-group-squares")).toHaveAttribute("aria-pressed", "true");
    await expect(page.getByTestId("recipe-plaza")).toBeVisible();
    await expect(page.getByTestId("recipe-park")).toHaveCount(0);
    await expect(page.getByTestId("make-group-parks")).toHaveAttribute("data-has-selected", "true");
    await expect(presets.getByTestId("preset-formal")).toBeVisible();
    await page.getByTestId("recipe-plaza").click();
    await expect(page.getByTestId("presets-plaza").getByTestId("preset-civic")).toBeVisible();
    await expect(page.getByTestId("make-groups")).toBeVisible(); // the strip stays
    await page.getByTestId("make-group-all").click();
    await expect(page.getByTestId("recipe-park")).toBeVisible();

    // search: preset hits of any type, marked with their type
    await page.getByTestId("browser-search").fill("meadow");
    await expect(page.getByTestId("preset-hits").getByTestId("preset-wild")).toBeVisible();
    await expect(page.getByTestId("recipe-park")).toBeVisible();
    await expect(page.getByTestId("recipe-plaza")).toHaveCount(0);
    await panelShot(page, "grid-browser-search");
    await page.getByTestId("browser-search").press("Enter");
    await expect.poll(() => page.evaluate(() => (window as any).pm.backend.active?.id)).toBe("wild");
    expect(errors).toEqual([]);
});

test("narrow screen: 400rem default, tiles wrap, one column", async ({ page }) => {
    const errors = await open(page, { width: 1440, height: 900 });
    await toDesign(page, "park");
    expect(await width(page)).toBe(400);
    await expect(page.getByTestId("placemaker-wrap")).toHaveAttribute("data-cols", "1");
    // 5 tiles in room for 3 → 3 + 2
    await expect(page.getByTestId("recipes")).toHaveAttribute("data-cols", "3");
    const park = await box(page, "recipe-park");
    const roads = await box(page, "recipe-roads");
    const parking = await box(page, "recipe-parking");
    expect(Math.abs(roads.y - park.y)).toBeLessThan(2);
    expect(roads.x).toBeLessThan(park.x); // Roads first
    expect(parking.y).toBeGreaterThan(park.y + 10);
    await expect(page.getByTestId("feature-grid")).toHaveCount(0);
    await expect(page.getByTestId("control-grid")).toHaveCount(0);
    await noTextOverflow(page, "narrow");
    expect(errors).toEqual([]);
});

test("resize by the left edge: wider / narrower, clamped, remembered; double-click resets", async ({ page }) => {
    await open(page);
    await toDesign(page, "parking");
    const panel0 = await box(page, "placemaker-panel");
    // narrower
    await dragEdge(page, 180);
    expect(await width(page)).toBeLessThan(400);
    const panel1 = await box(page, "placemaker-panel");
    expect(Math.abs(panel1.x + panel1.width - (panel0.x + panel0.width))).toBeLessThan(2); // right edge stays docked
    await expect(page.getByTestId("placemaker-wrap")).toHaveAttribute("data-cols", "1");
    // never below the minimum
    await dragEdge(page, 400);
    expect(await width(page)).toBe(360);
    await noTextOverflow(page, "360rem");
    // never above 45 % of the screen
    await dragEdge(page, -800);
    expect(await width(page)).toBe(Math.floor(1920 * 0.45));
    await expect(page.getByTestId("placemaker-wrap")).toHaveAttribute("data-cols", "2");
    await noTextOverflow(page, "max width");
    // remembered
    await page.reload();
    await expect(page.getByTestId("placemaker-panel")).toBeVisible();
    expect(await width(page)).toBe(Math.floor(1920 * 0.45));
    // double-click: back to the default for this screen
    await page.getByTestId("panel-resize").dblclick();
    expect(await width(page)).toBe(560);
});

test("resize a floating panel: its right edge stays where it is", async ({ page }) => {
    await open(page);
    const h = await box(page, "panel-header");
    await page.mouse.move(h.x + 120, h.y + h.height / 2);
    await page.mouse.down();
    await page.mouse.move(h.x - 300, h.y + 100, { steps: 6 });
    await page.mouse.up();
    await expect(page.getByTestId("placemaker-wrap")).toHaveAttribute("data-docked", "false");
    const before = await box(page, "placemaker-panel");
    await dragEdge(page, -120);
    const after = await box(page, "placemaker-panel");
    expect(after.width).toBeGreaterThan(before.width + 100);
    expect(Math.abs(after.x + after.width - (before.x + before.width))).toBeLessThan(3);
});

test("two columns when wide: paired property rows, folded groups side by side, feature grid; one column when narrow", async ({ page }) => {
    const errors = await open(page);
    await page.evaluate(() => localStorage.removeItem("placemaker.sections.v1"));
    await toDesign(page, "park");
    const step = page.getByTestId("step-0");
    // Basics is open: its sliders pair up (same line, side by side)
    await expect(step.getByTestId("control-grid").first()).toBeVisible();
    const rows = step.locator('[data-testid="group-basics"] [data-testid^="control-"][data-kind="slider"]');
    const n = await rows.count();
    expect(n).toBeGreaterThanOrEqual(2);
    const a = (await rows.nth(0).boundingBox())!;
    const b = (await rows.nth(1).boundingBox())!;
    expect(Math.abs(a.y - b.y)).toBeLessThan(2);
    expect(b.x).toBeGreaterThan(a.x + a.width - 2);
    // folded groups sit side by side
    const halves = step.locator('[data-testid^="groupcell-"][data-half="true"]');
    expect(await halves.count()).toBeGreaterThanOrEqual(2);
    const g0 = (await halves.nth(0).boundingBox())!;
    const g1 = (await halves.nth(1).boundingBox())!;
    expect(Math.abs(g0.y - g1.y)).toBeLessThan(2);
    // opening a folded group gives it the whole row
    await step.getByTestId("group-water-toggle").click();
    await expect(step.getByTestId("groupcell-water")).toHaveAttribute("data-half", "false");
    await step.getByTestId("group-water-toggle").click();
    await expect(step.getByTestId("groupcell-water")).toHaveAttribute("data-half", "true");

    // features in a 2-column grid; an open feature spans the row
    await expect(page.getByTestId("feature-grid")).toHaveAttribute("data-cols", "2");
    const f0 = await box(page, "feature-playground");
    const feats = page.locator('[data-testid="feature-grid"] > [data-testid^="feature-"]');
    const f1 = (await feats.nth(1).boundingBox())!;
    expect(Math.abs((await feats.nth(0).boundingBox())!.y - f1.y)).toBeLessThan(2);
    expect(f0.width).toBeLessThan(300);
    await page.getByTestId("feature-toggle-playground").click();
    await waitPlanned(page);
    const fold = page.getByTestId("feature-fold-playground");
    if (await fold.count()) {
        await fold.click();
        await expect(page.getByTestId("feature-playground")).toHaveAttribute("data-open", "true");
        expect((await box(page, "feature-playground")).width).toBeGreaterThan(450);
        await noTextOverflow(page, "park wide, feature open");
        await panelShot(page, "grid-park-wide-open");
        await fold.click();
    }
    await noTextOverflow(page, "park wide");
    await panelShot(page, "grid-park-wide");

    // narrow again → one column everywhere
    await dragEdge(page, 200);
    await expect(page.getByTestId("placemaker-wrap")).toHaveAttribute("data-cols", "1");
    await expect(page.getByTestId("feature-grid")).toHaveCount(0);
    await expect(page.getByTestId("control-grid")).toHaveCount(0);
    await noTextOverflow(page, "park narrow");
    await panelShot(page, "grid-park-narrow");
    await page.getByTestId("recipe-parking").click();
    await waitPlanned(page);
    await noTextOverflow(page, "parking narrow");
    await panelShot(page, "grid-parking-narrow");
    await dragEdge(page, -200);
    await expect(page.getByTestId("placemaker-wrap")).toHaveAttribute("data-cols", "2");
    await noTextOverflow(page, "parking wide");
    await panelShot(page, "grid-parking-wide");
    expect(errors).toEqual([]);
});
