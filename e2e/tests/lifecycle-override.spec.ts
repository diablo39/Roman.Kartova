import { test, expect } from "@playwright/test";
import { login } from "../fixtures/auth";

test("lifecycle: override-holder can reach the sunset-override checkbox before sunset", async ({ page }) => {
  await login(page);

  // DevSeed has 120 apps ("A App 000".."Z App NNN") sorted displayName asc; the
  // fixture ("E2E Sunset Override Fixture") sorts after all "E App ..." rows
  // (space < '2' in ASCII) so it isn't on page 1. Use the list's search filter
  // (FilterBar text control, aria-label "Search applications") to reach it via
  // an in-SPA row click — never page.goto the detail URL directly (cold-load
  // deep links bounce, bug #47).
  await page.getByRole("textbox", { name: "Search applications" }).fill("E2E Sunset Override Fixture");
  await page.keyboard.press("Enter");

  const link = page.getByRole("link", { name: "E2E Sunset Override Fixture" });
  await expect(link).toBeVisible();
  await link.click();
  await expect(page).toHaveURL(/\/catalog\/applications\/[0-9a-f-]+$/);

  // Open the lifecycle dropdown (LifecycleMenu trigger).
  await page.getByRole("button", { name: "Open lifecycle menu" }).click();
  // Decommission stays enabled for an override-holder even before sunset.
  await page.getByRole("menuitem", { name: "Decommission" }).click();

  // The dialog opens and the override checkbox is present (the gate-10 bug: it wasn't reachable).
  const dialog = page.getByRole("dialog", { name: "Decommission Application" });
  await expect(dialog).toBeVisible();
  await expect(dialog.getByText("Override sunset date")).toBeVisible();

  // Do NOT confirm — keep the fixture app deprecated for the next run.
  await dialog.getByRole("button", { name: "Cancel" }).click();
});
