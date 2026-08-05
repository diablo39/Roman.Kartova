import { test, expect } from "@playwright/test";
import { login } from "../fixtures/auth";

/**
 * Gate-9 verification for the filter-value cap (6947309c) and the ProblemDetails guard
 * (8d5f3804), both follow-ups to the A2 System list surface.
 *
 * The cap makes a 400 `too-many-filter-values` newly reachable on `teamId` (all four list
 * endpoints), so there is a real runtime surface to observe: the list page must render the
 * server's own `detail` — which only happens if `asProblemDetails` narrows the `unknown`
 * error at runtime — and must offer an escape that actually removes the offending params.
 * Without that escape the Reset button replays the identical 400 forever (the gate-7 HIGH
 * finding on the A2 slice, fixed at `1abb9603` for `systemId` and inherited here).
 *
 * 51 distinct values is the smallest over-cap request: MaxFilterValues = 50, and the ids are
 * de-duplicated before the count, so 51 *distinct* values is what trips it.
 */
const OVER_CAP = 51;

function overCapTeamIdQuery(): string {
  return Array.from({ length: OVER_CAP }, () => `teamId=${crypto.randomUUID()}`).join("&");
}

for (const surface of [
  { path: "/catalog/applications", heading: "Applications", label: "applications" },
  { path: "/catalog/services", heading: "Services", label: "services" },
] as const) {
  test(`over-cap teamId filter on ${surface.label} is legible and clearable`, async ({ page }) => {
    // The page deliberately console.error()s the failed query, so a blanket "no console errors"
    // assertion cannot apply here (the existing system-list-surface spec covers the clean path).
    // Track pageerrors only — an uncaught exception would mean the guard did not hold.
    const pageErrors: string[] = [];
    page.on("pageerror", (err) => pageErrors.push(String(err)));

    await login(page);

    // Canonical path, not the `/catalog` alias — the alias drops the query string.
    await page.goto(`${surface.path}?${overCapTeamIdQuery()}`);
    await expect(page.getByRole("heading", { name: surface.heading })).toBeVisible();

    // The card surfaces the server's ProblemDetails detail verbatim, not the generic fallback.
    await expect(page.getByText(`Failed to load ${surface.label}`)).toBeVisible();
    await expect(
      page.getByText(`At most 50 distinct teamId values may be supplied; got ${OVER_CAP}.`),
    ).toBeVisible();
    await expect(page.getByText("Try refreshing or resetting the list.")).toBeHidden();

    await page.screenshot({
      path: `../docs/superpowers/verification/2026-07-30-catalog-system-membership/a2/gate9-${surface.label}-teamid-cap.png`,
      fullPage: false,
    });

    // "Clear filters" must remove the offending params from the URL and let the list load —
    // Reset alone only pops the cursor stack and would replay the same 400.
    await page.getByRole("button", { name: "Clear filters" }).click();
    await expect(page).not.toHaveURL(/teamId=/);
    await expect(page.getByText(`Failed to load ${surface.label}`)).toBeHidden();
    await expect(page.getByRole("rowheader").first()).toBeVisible();

    expect(pageErrors, `page errors: ${pageErrors.join(" | ")}`).toEqual([]);
  });
}
