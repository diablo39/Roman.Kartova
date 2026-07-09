import { test, expect } from "@playwright/test";
import { login } from "../fixtures/auth";
import { insertDriftEdge } from "../fixtures/db";

const FIXTURE_APP_ID = "e2e00000-0000-0000-0000-000000000001"; // must match DevSeed fixed id

test("drift: an unmappable relationship.type does not 500 the relationships surface", async ({ page }) => {
  const cleanup = await insertDriftEdge(FIXTURE_APP_ID, FIXTURE_APP_ID);
  try {
    await login(page);

    // Same in-SPA navigation pattern as lifecycle-override.spec.ts (Task 7):
    // the fixture app isn't on list page 1, so use the list's search filter
    // and click the resulting row link — never page.goto the detail deep-link
    // (cold-load deep links bounce, bug #47).
    await page.getByRole("textbox", { name: "Search applications" }).fill("E2E Sunset Override Fixture");
    await page.keyboard.press("Enter");

    const link = page.getByRole("link", { name: "E2E Sunset Override Fixture" });
    await expect(link).toBeVisible();

    // The relationships list request is what the Task-1 query filter guards.
    // Assert it comes back 200 (not 500) — the most direct proof the drifted
    // 'PartOf' row is excluded rather than blowing up the mapper.
    const relationshipsResponse = page.waitForResponse(
      (res) => res.url().includes("/api/v1/catalog/relationships") && res.request().method() === "GET",
    );
    await link.click();
    await expect(page).toHaveURL(/\/catalog\/applications\/[0-9a-f-]+$/);
    expect((await relationshipsResponse).status()).toBe(200);

    // RelationshipsSection renders as <section aria-label="Relationships">
    // (there is no literal "Relationships" text heading — the group headings
    // are just "Outgoing" / "Incoming") — so anchor on the region landmark
    // plus a group heading, and assert no error surface. The drift row is a
    // self-referential edge on the fixture app so it would show up (as an
    // extra outgoing+incoming row) if the query filter failed to exclude it;
    // "No outgoing relationships." / no error text confirms it's excluded.
    await expect(page.getByRole("region", { name: "Relationships" })).toBeVisible();
    await expect(page.getByRole("heading", { name: "Outgoing" })).toBeVisible();
    await expect(page.getByRole("heading", { name: "Incoming" })).toBeVisible();
    await expect(page.getByText(/couldn.?t load relationships|something went wrong|failed to load/i)).toHaveCount(0);
  } finally {
    await cleanup();
  }
});
