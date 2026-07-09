import { test, expect } from "@playwright/test";
import { login } from "../fixtures/auth";
import { insertDriftEdge } from "../fixtures/db";
import { APP_DETAIL_URL, FIXTURE_APP_ID, findFixtureAppLink } from "../fixtures/nav";

test("drift: an unmappable relationship.type does not 500 the relationships surface", async ({ page }) => {
  const cleanup = await insertDriftEdge(FIXTURE_APP_ID, FIXTURE_APP_ID);
  try {
    await login(page);

    // In-SPA navigate toward the fixture app (see nav.ts). The link is returned
    // unclicked so we can register the relationships-response wait before the
    // navigation fires.
    const link = await findFixtureAppLink(page);

    // The relationships list request is what the Task-1 query filter guards.
    // Assert it comes back 200 (not 500) — the most direct proof the drifted
    // 'PartOf' row is excluded rather than blowing up the mapper.
    const relationshipsResponse = page.waitForResponse(
      (res) => res.url().includes("/api/v1/catalog/relationships") && res.request().method() === "GET",
    );
    await link.click();
    await expect(page).toHaveURL(APP_DETAIL_URL);
    expect((await relationshipsResponse).status()).toBe(200);

    // RelationshipsSection renders as <section aria-label="Relationships">
    // (group headings are "Outgoing" / "Incoming", not a literal "Relationships"
    // heading) — anchor on the region landmark + a group heading, and assert no
    // error surface. The drift row is a self-referential edge on the fixture app,
    // so it would surface as an extra row if the filter failed to exclude it.
    await expect(page.getByRole("region", { name: "Relationships" })).toBeVisible();
    await expect(page.getByRole("heading", { name: "Outgoing" })).toBeVisible();
    await expect(page.getByRole("heading", { name: "Incoming" })).toBeVisible();
    await expect(page.getByText(/couldn.?t load relationships|something went wrong|failed to load/i)).toHaveCount(0);
  } finally {
    await cleanup();
  }
});
