import { type Page, type Locator, expect } from "@playwright/test";

/**
 * Deterministic DevSeed fixture application (deprecated, far-future sunset) that
 * the E2E specs drive. Keep these in sync with the seed row in
 * `src/Kartova.Migrator/DevSeed.cs` (same id + display name) — three files
 * (this, the seed, and any spec) must agree.
 */
export const FIXTURE_APP_ID = "e2e00000-0000-0000-0000-000000000001";
export const FIXTURE_APP_NAME = "E2E Sunset Override Fixture";

/** Detail route for a catalog application (id is a GUID). */
export const APP_DETAIL_URL = /\/catalog\/applications\/[0-9a-f-]+$/;

/**
 * In-SPA navigate toward a catalog application via the Applications list search
 * filter, returning the row link **without clicking it** (the caller clicks, so
 * a spec can register a `waitForResponse` before the navigation fires).
 *
 * Uses the list's FilterBar text control (aria-label "Search applications") to
 * locate the row by name, then an in-SPA click. Deep-link `page.goto` would also
 * work (the #47 returnTo round-trip is in place), and the fixture currently sits
 * on list page 1 — but filtering by name is pagination- and sort-order-
 * independent, so it stays reliable as DevSeed's app set grows or reorders.
 */
export async function findFixtureAppLink(page: Page, name: string = FIXTURE_APP_NAME): Promise<Locator> {
  await page.getByRole("textbox", { name: "Search applications" }).fill(name);
  await page.keyboard.press("Enter");
  const link = page.getByRole("link", { name });
  await expect(link).toBeVisible();
  return link;
}

/**
 * Deterministic DevSeed fixture API (fixed id + OpenAPI spec doc) that the
 * spec-render and tab-switch specs drive. Keep in sync with the seed rows in
 * `src/Kartova.Migrator/DevSeed.cs` (same id + display name).
 */
export const FIXTURE_API_ID = "e2e00000-0000-0000-0000-000000000010";
export const FIXTURE_API_NAME = "E2E Spec Render Fixture";

/** Detail route for a catalog API (id is a GUID). */
export const API_DETAIL_URL = /\/catalog\/apis\/[0-9a-f-]+$/;

/** Deep-link path to the fixture API detail page (baseURL-relative). */
export function apiDetailPath(id: string = FIXTURE_API_ID): string {
  return `/catalog/apis/${id}`;
}
