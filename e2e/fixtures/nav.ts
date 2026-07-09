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
 * Why filter-then-click instead of `page.goto`: DevSeed seeds 120 apps
 * ("A App 000".."Z App NNN") sorted displayName asc, so a fixture named
 * "E2E ..." sorts past list page 1; and cold-load deep links bounce (bug #47).
 * The list's FilterBar text control (aria-label "Search applications") plus an
 * in-SPA row click is the reliable path.
 */
export async function findFixtureAppLink(page: Page, name: string = FIXTURE_APP_NAME): Promise<Locator> {
  await page.getByRole("textbox", { name: "Search applications" }).fill(name);
  await page.keyboard.press("Enter");
  const link = page.getByRole("link", { name });
  await expect(link).toBeVisible();
  return link;
}
