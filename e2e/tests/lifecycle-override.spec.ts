import { test, expect } from "@playwright/test";
import { login } from "../fixtures/auth";
import { APP_DETAIL_URL, findFixtureAppLink } from "../fixtures/nav";

test("lifecycle: override-holder can reach the sunset-override checkbox before sunset", async ({ page }) => {
  await login(page);

  // In-SPA navigate to the deprecated+future-sunset fixture app (see nav.ts for
  // why filter-then-click rather than a deep-link goto).
  const link = await findFixtureAppLink(page);
  await link.click();
  await expect(page).toHaveURL(APP_DETAIL_URL);

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
