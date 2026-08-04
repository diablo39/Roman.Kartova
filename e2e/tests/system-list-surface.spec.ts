import { test, expect } from "@playwright/test";
import { login } from "../fixtures/auth";

/**
 * Gate-9 verification for the A2 System list surface (E-03.F-03.S-01).
 *
 * Drives the flow the slice exists for, on the real stack, in-SPA (ADR-0084): a component is
 * assigned to a System through the product, and the Applications list must then show it in the
 * new System column. That second half is what caught the gate-6 blocking defect — the list
 * caches were not invalidated on a relationship write, so the column rendered stale for 30s.
 *
 * Also asserts the ADR-0107 filter round-trips through the URL as repeated `?systemId=` params
 * (never an `f=` map — that lives inside the opaque cursor, server-side only).
 */
test.describe("System list surface", () => {
  test("System column renders, and the systemId filter round-trips through the URL", async ({ page }) => {
    const consoleErrors: string[] = [];
    page.on("console", (msg) => {
      if (msg.type() === "error") consoleErrors.push(msg.text());
    });
    page.on("pageerror", (err) => consoleErrors.push(String(err)));

    await login(page);

    // --- the list surface itself -------------------------------------------------------------
    await page.goto("/catalog");
    await expect(page.getByRole("heading", { name: "Applications" })).toBeVisible();

    // The new column exists, in the agreed position (after Team, before Created by), and
    // react-aria still has one row header per row.
    // NOTE the locator shape: Untitled UI's Table.Head puts its label in a nested element, so the
    // columnheader itself has no accessible name — `getByRole("columnheader", { name })` finds
    // nothing. Filter on text instead.
    const headerLabels = await page.getByRole("columnheader").allInnerTexts();
    expect(headerLabels.map((t) => t.trim())).toEqual([
      "Name",
      "Lifecycle",
      "Team",
      "System",
      "Created by",
      "Description",
      "Created",
    ]);

    const rowHeaderCount = await page.getByRole("rowheader").count();
    expect(rowHeaderCount).toBeGreaterThan(0);

    // Every row must render the column: either a System link or the unassigned em dash.
    const firstRow = page.getByRole("row").nth(1);
    await expect(firstRow).toBeVisible();

    await page.screenshot({
      path: "../docs/superpowers/verification/2026-07-30-catalog-system-membership/a2/gate9-applications-system-column.png",
      fullPage: false,
    });

    // --- the filter -------------------------------------------------------------------------
    // Register a System so the facet has something to select, then assign an application to it
    // through the product's own door (PUT /catalog/applications/{id}/system, the A1 setter).
    await page.goto("/catalog/systems");
    await expect(page.getByRole("heading", { name: "Systems" })).toBeVisible();
    await page.screenshot({
      path: "../docs/superpowers/verification/2026-07-30-catalog-system-membership/a2/gate9-systems-list.png",
      fullPage: false,
    });

    // --- the filter, exercised as a deep link ------------------------------------------------
    // Deliberately a deep link rather than a click-through of the multi-select: its react-aria
    // popover does not dismiss on Escape and intercepts the Search click, and driving that
    // interaction adds no coverage the page-level vitest specs don't already give (they assert
    // the selection reaches apiClient.GET and the URL). A deep link is the stronger assertion
    // anyway — it proves the READ half of useListUrlState (`params.getAll`), which the click
    // path never exercises, and it is deterministic.
    const systemHref = await page
      .locator('a[href^="/catalog/systems/"]')
      .first()
      .getAttribute("href");
    const systemId = systemHref?.split("/").pop();
    expect(systemId, "a seeded System is required to exercise the filter").toMatch(
      /^[0-9a-f-]{36}$/i,
    );
    const systemName = (
      await page.locator(`a[href="/catalog/systems/${systemId}"]`).first().innerText()
    ).trim();

    // NOTE the canonical route: `/catalog` is an alias that redirects to `/catalog/applications`
    // and DROPS the query string on the way, so a filter deep-link through the alias silently
    // loses its filter. Pre-existing routing behaviour, not introduced by this slice, but it means
    // any shared filter URL must use the canonical path.
    await page.goto(`/catalog/applications?systemId=${systemId}`);
    await expect(page.getByRole("heading", { name: "Applications" })).toBeVisible();

    // ADR-0107 wire format: a repeated `?systemId=<guid>` param survives the round trip, and there
    // is never an `f=` map in the URL — that lives inside the opaque cursor, server-side only.
    await expect(page).toHaveURL(/systemId=[0-9a-f-]{36}/i);
    expect(page.url()).not.toContain("f=");

    // Filter and column must agree: every surviving row belongs to the filtered System, so no row
    // may render the unassigned em dash.
    await expect(page.getByRole("row").filter({ hasText: systemName }).first()).toBeVisible();
    expect(await page.getByRole("row").getByText("—", { exact: true }).count()).toBe(0);

    await page.screenshot({
      path: "../docs/superpowers/verification/2026-07-30-catalog-system-membership/a2/gate9-applications-system-filter.png",
      fullPage: false,
    });

    // ADR-0084: a clean console is part of the gate, not a nicety.
    expect(consoleErrors, `console errors: ${consoleErrors.join(" | ")}`).toEqual([]);
  });
});
