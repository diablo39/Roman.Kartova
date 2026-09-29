import { type Page, expect } from "@playwright/test";

const KC_USER = process.env.E2E_USER ?? "admin@orga.kartova.local";
const KC_PASS = process.env.E2E_PASS ?? "dev_password_12";

/**
 * Log in through the real Keycloak login page. Starts at "/" (the app root)
 * and drives the live Keycloak login form, then waits for the SPA to land on
 * the catalog applications list. (Deep-link cold-loads are fine — the OIDC
 * returnTo round-trip that fixed #47 predates this work — but this helper only
 * needs the root, so it starts there.)
 *
 * Selectors confirmed against the live Keycloak login page (kartova realm,
 * default theme): the form fields are PatternFly inputs with plain `id`
 * attributes (`#username`, `#password`) and the submit button is
 * `#kc-login`. `getByLabel(/password/i)` is deliberately NOT used — the
 * PatternFly show/hide-password toggle button carries
 * `aria-label="Show password"`, which also matches that regex and makes
 * the locator ambiguous (strict-mode violation observed live).
 */
export async function login(page: Page): Promise<void> {
  await page.goto("/");
  // OIDC redirect → Keycloak login form.
  await page.locator("#username").fill(KC_USER);
  await page.locator("#password").fill(KC_PASS);
  await page.locator("#kc-login").click();
  // Back in SPA via /callback: "/" → /catalog → /catalog/applications.
  await page.waitForURL(/\/catalog\/applications/, { timeout: 30_000 });
  await expect(page.getByRole("heading", { name: /applications/i })).toBeVisible();
}

/** Admin console origin (ADR-0118) — a different origin from baseURL, so admin specs use absolute URLs. */
export const ADMIN_BASE_URL = process.env.E2E_ADMIN_BASE_URL ?? "http://localhost:4174";

/**
 * Log in to the admin console through the real kartova-platform KeyCloak login page (same PatternFly
 * selectors as `login`). Leaves the page wherever the console lands; callers assert the outcome.
 */
export async function adminLogin(page: Page, username: string, password = "dev_password_12"): Promise<void> {
  await page.goto(`${ADMIN_BASE_URL}/`);
  await page.locator("#username").fill(username);
  await page.locator("#password").fill(password);
  await page.locator("#kc-login").click();
}
