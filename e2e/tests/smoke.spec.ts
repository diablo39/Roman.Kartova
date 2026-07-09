import { test, expect } from "@playwright/test";
import { login } from "../fixtures/auth";

test("smoke: login and the Applications list renders", async ({ page }) => {
  await login(page);
  await expect(page).toHaveURL(/\/catalog\/applications/);
  // DevSeed seeds ~120 apps → at least one data row is present (role=row
  // includes the header row, hence >= 2).
  const rows = page.getByRole("row");
  await expect(rows.first()).toBeVisible();
  expect(await rows.count()).toBeGreaterThanOrEqual(2);
});
