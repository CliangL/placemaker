// Round 3 (playtest 2): compact "engineering" density, foldable groups collapsed by default and
// remembered, single types only in the Make row, features as switches with indented options, and no
// text that overflows or is cut anywhere in the panel.
import { expect, Page, test } from "@playwright/test";
import { clickWorld, drawPolygon, FIELD, open, panelShot, shot, waitPlanned } from "./helpers";

async function toDesign(page: Page, recipe: string) {
    await drawPolygon(page, FIELD);
    await page.getByTestId("primary").click();
    await page.getByTestId(`recipe-${recipe}`).click();
    await waitPlanned(page);
}

/**
 * Every text in the panel fits its box (scrollWidth ≤ clientWidth: nothing cut or ellipsised) and
 * stays inside each of its ancestors horizontally (nothing spills onto a neighbour, as the card
 * descriptions did in Gameface).
 */
export async function noTextOverflow(page: Page, where: string) {
    const bad = await page.evaluate(() => {
        const root = document.querySelector('[data-testid="placemaker-wrap"]') as HTMLElement;
        const out: string[] = [];
        const seen = new Set<Element>();
        const walker = document.createTreeWalker(root, NodeFilter.SHOW_TEXT);
        while (walker.nextNode()) {
            const node = walker.currentNode;
            const text = (node.textContent ?? "").trim();
            const el = node.parentElement;
            if (!text || !el || seen.has(el)) continue;
            seen.add(el);
            const r = el.getBoundingClientRect();
            if (r.width === 0 || r.height === 0) continue;
            const inline = getComputedStyle(el).display === "inline";
            if (!inline && el.scrollWidth > el.clientWidth + 1) out.push(`cut: "${text}" (${el.scrollWidth} > ${el.clientWidth})`);
            for (let a = el.parentElement; a && a !== root.parentElement; a = a.parentElement) {
                const ar = a.getBoundingClientRect();
                if (ar.width === 0) continue;
                if (r.left < ar.left - 1 || r.right > ar.right + 1) {
                    out.push(`spills: "${text}" out of ${String(a.className).split(" ")[0] || a.tagName}`);
                    break;
                }
            }
        }
        return out;
    });
    expect(bad, where).toEqual([]);
}

async function expandAll(page: Page) {
    for (let i = 0; i < 20; i++) {
        const closed = page.locator('[data-testid="placemaker-panel"] button[aria-expanded="false"]');
        if (!(await closed.count())) return;
        await closed.first().click();
    }
}

test("Make row: single types only, three per row, no mixes", async ({ page }) => {
    const errors = await open(page);
    await toDesign(page, "park");
    const ids = await page.locator('[data-testid="recipes"] [data-testid^="recipe-"]').evaluateAll((els) => els.map((e) => e.getAttribute("data-testid")));
    expect(ids).toEqual(["recipe-parking", "recipe-plaza", "recipe-park", "recipe-roads", "recipe-campground"]);
    await expect(page.getByTestId("mixes")).toHaveCount(0);
    await expect(page.getByTestId("placemaker-panel")).not.toContainText("Mixes");
    // icon + name only, three per row, compact
    const boxes = await Promise.all(["parking", "plaza", "park", "roads"].map((id) => page.getByTestId(`recipe-${id}`).boundingBox()));
    expect(Math.abs(boxes[0]!.y - boxes[2]!.y)).toBeLessThan(2);
    expect(boxes[3]!.y).toBeGreaterThan(boxes[0]!.y + 10);
    expect(boxes[0]!.height).toBeLessThanOrEqual(32);
    for (const id of ["parking", "park"]) {
        const card = page.getByTestId(`recipe-${id}`);
        await expect(card.locator("img")).toHaveCount(1);
        expect((await card.innerText()).split("\n").filter(Boolean).length).toBe(1);
    }
    expect(errors).toEqual([]);
});

test("groups are foldable: only the first essential group open, summaries when folded, state remembered", async ({ page }) => {
    await open(page);
    await page.evaluate(() => localStorage.removeItem("placemaker.sections.v1"));
    await toDesign(page, "parking");
    const step = page.getByTestId("step-0");
    await expect(step.getByTestId("group-basics")).toHaveAttribute("data-open", "true");
    for (const g of ["layout", "zoning", "extras"]) await expect(step.getByTestId(`group-${g}`)).toHaveAttribute("data-open", "false");
    // folded groups show their values instead of rows
    await expect(page.getByTestId("control-aisle")).toHaveCount(0);
    await expect(step.getByTestId("group-layout-summary")).toHaveText("6.0 m");
    await expect(step.getByTestId("group-zoning-summary")).toHaveText("Off");
    await expect(step.getByTestId("group-extras-summary")).toContainText("Lanterns");
    await expect(step.getByTestId("group-extras-summary")).toContainText("¢5");

    // open Layout, close Basics → remembered across reloads and recipe switches
    await step.getByTestId("group-layout-toggle").click();
    await expect(page.getByTestId("slider-aisle-value")).toHaveText("6.0 m");
    await step.getByTestId("group-basics-toggle").click();
    await expect(page.getByTestId("control-angle")).toHaveCount(0);
    await expect(step.getByTestId("group-basics-summary")).toContainText("90°");
    await page.getByTestId("recipe-plaza").click();
    await page.getByTestId("recipe-parking").click();
    await expect(page.getByTestId("step-0").getByTestId("group-layout")).toHaveAttribute("data-open", "true");
    await expect(page.getByTestId("step-0").getByTestId("group-basics")).toHaveAttribute("data-open", "false");
    await page.reload();
    await expect(page.getByTestId("placemaker-panel")).toBeVisible();
    await toDesign(page, "parking");
    await expect(page.getByTestId("step-0").getByTestId("group-layout")).toHaveAttribute("data-open", "true");
    await expect(page.getByTestId("step-0").getByTestId("group-basics")).toHaveAttribute("data-open", "false");
    await page.evaluate(() => localStorage.removeItem("placemaker.sections.v1"));
});

test("features: switch adds the feature, its options fold out indented (collapsed), switch off removes it from the plan", async ({ page }) => {
    const errors = await open(page);
    await page.evaluate(() => localStorage.removeItem("placemaker.sections.v1"));
    await toDesign(page, "park");
    const features = page.getByTestId("features");
    await expect(features).toBeVisible();
    for (const id of ["plaza", "playground", "pond", "picnic", "restrooms", "fountain", "bigTrees"]) await expect(page.getByTestId(`feature-${id}`)).toBeVisible();
    await expect(page.getByTestId("feature-plaza")).toContainText("Central plaza");
    await expect(page.getByTestId("feature-playground")).toHaveAttribute("data-enabled", "false");
    await expect(page.getByTestId("feature-options-playground")).toHaveCount(0);

    await page.getByTestId("feature-toggle-playground").click();
    await expect(page.getByTestId("feature-playground")).toHaveAttribute("data-enabled", "true");
    const opts = page.getByTestId("feature-options-playground");
    await expect(opts).toBeVisible();
    // indented under the feature row, collapsed by default with a summary
    const rowBox = (await page.getByTestId("feature-playground").boundingBox())!;
    const optBox = (await opts.boundingBox())!;
    expect(optBox.x).toBeGreaterThan(rowBox.x + 8);
    await expect(opts.getByTestId("group-basics")).toHaveAttribute("data-open", "false");
    await expect(opts.getByTestId("group-basics-summary")).toContainText("Medium");
    await waitPlanned(page);
    let layers = await page.evaluate(() => (window as any).pm.backend.plan.layers.map((l: any) => l.layer));
    expect(layers).toContain("playground");
    // its options edit the feature step
    await opts.getByTestId("group-basics-toggle").click();
    await opts.getByTestId("choice-size-l").click();
    await expect(opts.getByTestId("choice-size-l")).toHaveAttribute("aria-pressed", "true");

    // a centre plaza (fill feature on the park's core) and a pond (permanent)
    await page.getByTestId("feature-toggle-plaza").click();
    await page.getByTestId("feature-toggle-pond").click();
    await waitPlanned(page);
    layers = await page.evaluate(() => (window as any).pm.backend.plan.layers.map((l: any) => l.layer));
    expect(layers).toEqual(expect.arrayContaining(["plaza", "pond"]));
    await expect(page.getByTestId("features")).toContainText("3");
    await panelShot(page, "panel-features-park");

    // off: kept settings, gone from the plan, options hidden; undo brings it back
    await page.getByTestId("feature-toggle-playground").click();
    await expect(page.getByTestId("feature-options-playground")).toHaveCount(0);
    await waitPlanned(page);
    layers = await page.evaluate(() => (window as any).pm.backend.plan.layers.map((l: any) => l.layer));
    expect(layers).not.toContain("playground");
    await page.getByTestId("undo").click();
    await expect(page.getByTestId("feature-playground")).toHaveAttribute("data-enabled", "true");
    expect(await page.evaluate(() => (window as any).pm.backend.root.children.find((c: any) => c.feature === "playground").values.size)).toBe("l");

    // plaza: trees on by default; roads: zoning along streets
    await page.getByTestId("recipe-plaza").click();
    await expect(page.getByTestId("feature-bigTrees")).toHaveAttribute("data-enabled", "true");
    await expect(page.getByTestId("feature-bigTrees")).toContainText("Trees");
    await expect(page.getByTestId("feature-kiosk")).toBeVisible();
    await page.getByTestId("recipe-roads").click();
    await expect(page.getByTestId("feature-edgeZoning")).toContainText("Zoning along streets");
    await page.getByTestId("recipe-parking").click();
    await expect(page.getByTestId("features")).toHaveCount(0);
    expect(errors).toEqual([]);
});

test("no text overflows or is cut anywhere in the panel (every stage, every type, all groups open)", async ({ page }) => {
    await open(page, { width: 1920, height: 1080 });
    await noTextOverflow(page, "area empty");
    await page.getByTestId("area-mode-paint").click();
    await noTextOverflow(page, "area paint");
    await page.getByTestId("area-mode-draw").click();
    await drawPolygon(page, FIELD);
    await noTextOverflow(page, "area closed");
    await page.getByTestId("primary").click();
    for (const id of ["parking", "plaza", "park", "roads", "campground"]) {
        await page.getByTestId(`recipe-${id}`).click();
        await waitPlanned(page);
        await noTextOverflow(page, id + " folded");
        for (const f of await page.locator('[data-testid^="feature-toggle-"]').all()) if ((await f.getAttribute("aria-checked")) === "false") await f.click();
        await expandAll(page);
        await noTextOverflow(page, id + " expanded");
    }
    await page.getByTestId("recipe-parking").click();
    await page.getByTestId("marker-remove-0").click();
    await noTextOverflow(page, "entrance required");
    await page.getByTestId("marker-kind-exit").click();
    await noTextOverflow(page, "placing markers");
    await clickWorld(page, (FIELD[1][0] + FIELD[2][0]) / 2, (FIELD[1][1] + FIELD[2][1]) / 2);
    await page.getByTestId("marker-done").click();
    await page.getByTestId("primary").click();
    await waitPlanned(page);
    await noTextOverflow(page, "review");
    await page.evaluate(() => localStorage.removeItem("placemaker.sections.v1"));
});

test("compact: the whole park design fits without scrolling; full-HD screenshots per stage", async ({ page }) => {
    await open(page, { width: 1920, height: 1080 });
    await page.evaluate(() => localStorage.removeItem("placemaker.sections.v1"));
    await shot(page, "r3-1-area-empty");
    await panelShot(page, "r3-panel-1-area-empty");
    await drawPolygon(page, FIELD);
    await shot(page, "r3-1-area");
    await panelShot(page, "r3-panel-1-area");
    await page.getByTestId("primary").click();
    await page.getByTestId("recipe-parking").click();
    await waitPlanned(page);
    await shot(page, "r3-2-design-parking");
    await panelShot(page, "r3-panel-2-design-parking");
    await page.getByTestId("recipe-park").click();
    await waitPlanned(page);
    // folded by default, the park's options and features need no scrolling
    const body = page.getByTestId("panel-body");
    const fits = await body.evaluate((el) => el.scrollHeight <= el.clientHeight + 1);
    expect(fits).toBe(true);
    await shot(page, "r3-2-design-park");
    await panelShot(page, "r3-panel-2-design-park");
    await page.getByTestId("feature-toggle-playground").click();
    await page.getByTestId("feature-toggle-plaza").click();
    await waitPlanned(page);
    await panelShot(page, "r3-panel-2-design-park-features");
    await page.getByTestId("recipe-plaza").click();
    await waitPlanned(page);
    await panelShot(page, "r3-panel-2-design-plaza");
    await page.getByTestId("recipe-roads").click();
    await waitPlanned(page);
    await panelShot(page, "r3-panel-2-design-roads");
    await page.getByTestId("recipe-park").click();
    await page.getByTestId("primary").click();
    await waitPlanned(page);
    await shot(page, "r3-3-review");
    await panelShot(page, "r3-panel-3-review");
    await page.evaluate(() => localStorage.removeItem("placemaker.sections.v1"));
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

test("no tree editor, no recipe saving, no mix UI", async ({ page }) => {
    await open(page);
    await toDesign(page, "park");
    for (const id of ["add-layer", "layer-tree", "save-recipe", "delete-recipe", "mixes", "recipe-roads-with-parks", "recipe-park-plaza", "recipe-neighbourhood-park"])
        await expect(page.getByTestId(id)).toHaveCount(0);
});
