import { test, expect } from "@playwright/test";
import { login } from "../fixtures/auth";
import { apiDetailPath } from "../fixtures/nav";

test("detail-tabs: API detail switches tabs, syncs ?tab, mounts only the active panel", async ({ page }) => {
  await login(page);

  // No ?tab → default Overview (URL stays clean; selection defaults to the first tab).
  await page.goto(apiDetailPath());
  await expect(page.getByRole("heading", { name: "E2E Spec Render Fixture" })).toBeVisible();
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

  // Invalid ?tab normalizes to the default (Overview). DetailTabs uses { replace: true }; we
  // assert the resulting URL + content here, not browser-history behavior.
  await page.goto(`${apiDetailPath()}?tab=bogus`);
  await expect(page).toHaveURL(/[?&]tab=overview/);
  await expect(page.getByRole("heading", { name: "Description" })).toBeVisible();
});
