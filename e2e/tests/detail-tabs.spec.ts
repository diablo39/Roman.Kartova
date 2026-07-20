import { test, expect } from "@playwright/test";
import { login } from "../fixtures/auth";
import { apiDetailPath, FIXTURE_API_NAME } from "../fixtures/nav";

test("detail-tabs: API detail switches tabs, syncs ?tab, mounts only the active panel", async ({ page }) => {
  await login(page);

  // No ?tab → default Overview (URL stays clean; selection defaults to the first tab).
  await page.goto(apiDetailPath());
  await expect(page.getByRole("heading", { name: FIXTURE_API_NAME })).toBeVisible();
  await expect(page.getByRole("tab", { name: "Overview" })).toHaveAttribute("aria-selected", "true");
  await expect(page.getByRole("heading", { name: "Description" })).toBeVisible();
  // Only the active panel mounts: the spec-view toggle (Definition) is absent on Overview.
  await expect(page.getByRole("group", { name: "Spec view" })).toHaveCount(0);
  // Default leaves the URL clean — DetailTabs does not write ?tab when it is absent.
  await expect(page).not.toHaveURL(/[?&]tab=/);
  // Symmetric unmount check: the Dependencies panel is not mounted on the default tab either.
  await expect(page.getByRole("region", { name: "Relationships" })).toHaveCount(0);

  // Switch to Dependencies → ?tab=dependencies, panel swaps (Overview content unmounts).
  await page.getByRole("tab", { name: "Dependencies" }).click();
  await expect(page).toHaveURL(/[?&]tab=dependencies/);
  await expect(page.getByRole("region", { name: "Relationships" })).toBeVisible();
  await expect(page.getByText("Nothing points to this API.")).toBeVisible();
  await expect(page.getByRole("heading", { name: "Description" })).toHaveCount(0);

  // Switch to Definition → ?tab=definition, lazy spec render appears.
  await page.getByRole("tab", { name: "Definition" }).click();
  await expect(page).toHaveURL(/[?&]tab=definition/);
  await expect(page.getByRole("group", { name: "Spec view" })).toBeVisible();
  await expect(page.locator(".scalar-render")).toBeVisible();

  // Switch back to Overview in-place (via the tab, not a reload) → proves the unmount is
  // symmetric client-side: the Definition spec view unmounts and Overview remounts. This also
  // exercises the ?tab=overview write path (DetailTabs writes the key on click even for the
  // default tab — distinct from the clean-URL default-load case above).
  await page.getByRole("tab", { name: "Overview" }).click();
  await expect(page).toHaveURL(/[?&]tab=overview/);
  await expect(page.getByRole("group", { name: "Spec view" })).toHaveCount(0);
  await expect(page.locator(".scalar-render")).toHaveCount(0);
  await expect(page.getByRole("heading", { name: "Description" })).toBeVisible();

  // Invalid ?tab normalizes to the default (Overview). DetailTabs uses { replace: true }; we
  // assert the resulting URL + content here, not browser-history behavior.
  await page.goto(`${apiDetailPath()}?tab=bogus`);
  await expect(page).toHaveURL(/[?&]tab=overview/);
  await expect(page.getByRole("heading", { name: "Description" })).toBeVisible();
});
