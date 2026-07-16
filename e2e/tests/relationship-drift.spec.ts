import { test, expect } from "@playwright/test";
import { login } from "../fixtures/auth";
import { insertDriftEdge } from "../fixtures/db";
import { APP_DETAIL_URL, FIXTURE_APP_ID, findFixtureAppLink } from "../fixtures/nav";

test("drift: an unmappable relationship.type does not 500 the relationships surface", async ({ page }) => {
  const cleanup = await insertDriftEdge(FIXTURE_APP_ID, FIXTURE_APP_ID);
  try {
    await login(page);

    // In-SPA navigate toward the fixture app (see nav.ts), landing on the default
    // Overview tab.
    const link = await findFixtureAppLink(page);
    await link.click();
    await expect(page).toHaveURL(APP_DETAIL_URL);

    // Tabbed detail layout (ADR-0114): only the active tab's panel mounts, and the
    // relationships surface lives on the Dependencies tab — so open it. Register the
    // response wait *before* clicking the tab (that click is what fires the request).
    //
    // The relationships list request is what the Task-1 query filter guards. Assert it
    // comes back 200 (not 500) — the most direct proof the drifted 'PartOf' row is
    // excluded rather than blowing up the mapper.
    const relationshipsResponse = page.waitForResponse(
      (res) => res.url().includes("/api/v1/catalog/relationships") && res.request().method() === "GET",
    );
    await page.getByRole("tab", { name: "Dependencies" }).click();
    expect((await relationshipsResponse).status()).toBe(200);

    // RelationshipsSection renders as <section aria-label="Relationships"> with
    // group headings "Outgoing" / "Incoming" (no literal "Relationships" heading).
    await expect(page.getByRole("region", { name: "Relationships" })).toBeVisible();
    await expect(page.getByRole("heading", { name: "Outgoing" })).toBeVisible();
    await expect(page.getByRole("heading", { name: "Incoming" })).toBeVisible();
    await expect(page.getByText(/couldn.?t load relationships|something went wrong|failed to load/i)).toHaveCount(0);

    // Exclusion, not just no-500: the fixture app has no real relationships, and
    // the injected drift edge is a self-referential PartOf row — so it MUST NOT
    // appear as an outgoing row. Assert the empty-state copy is shown. This guards
    // the class where a broken filter maps unknown types to a default (no 500) but
    // still leaks the row into the UI — which the status-200 check alone would miss.
    await expect(page.getByText("No outgoing relationships.")).toBeVisible();
  } finally {
    await cleanup();
  }
});
