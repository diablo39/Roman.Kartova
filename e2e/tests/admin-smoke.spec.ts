import { test, expect } from "@playwright/test";
import { adminLogin, ADMIN_BASE_URL } from "../fixtures/auth";

test("admin smoke: an operator signs in and lands on the console", async ({ page }) => {
  await adminLogin(page, "platform-admin@kartova.local");

  await expect(page.getByRole("heading", { name: "Platform Admin" })).toBeVisible({ timeout: 30_000 });
  await expect(page).toHaveURL(new RegExp(`^${ADMIN_BASE_URL}/?$`));
  await expect(page.getByText(/Signed in as .*platform-admin@kartova\.local/)).toBeVisible();
  await expect(page.getByRole("link", { name: "Overview" })).toBeVisible();
});

test("admin smoke: a platform-realm user without the role sees No access", async ({ page }) => {
  await adminLogin(page, "operator-norole@kartova.local");

  await expect(page.getByRole("heading", { name: "No access" })).toBeVisible({ timeout: 30_000 });
  await expect(page.getByRole("link", { name: "Overview" })).toHaveCount(0);
});
