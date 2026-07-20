import { test, expect } from "@playwright/test";
import { login } from "../fixtures/auth";
import { apiDetailPath } from "../fixtures/nav";

test("spec-render: API Definition tab renders the spec read-only (no live client)", async ({ page }) => {
  const consoleErrors: string[] = [];
  page.on("console", (msg) => {
    // The SpecRender error-boundary logs "[SpecRender] spec render failed…" on a Scalar
    // regression — treat that (and any other error) as a hard failure signal.
    if (msg.type() === "error") consoleErrors.push(msg.text());
  });

  await login(page);

  // Deep-link straight to the Definition tab (the #47 returnTo round-trip supports cold
  // deep-loads; login already established the session, so this loads authenticated).
  await page.goto(`${apiDetailPath()}?tab=definition`);
  await expect(page.getByRole("heading", { name: "E2E Spec Render Fixture" })).toBeVisible();

  // Rendered view is the default; the Scalar container mounts.
  const render = page.locator(".scalar-render");
  await expect(render).toBeVisible();

  // Proves it is the *rendered* spec, not the raw <pre> fallback (which would also hide the
  // client and pass the read-only checks for the wrong reason). The fixture's info.title.
  await expect(render.getByText("E2E Fixture API")).toBeVisible();

  // The error-boundary degrade banner must NOT be shown.
  await expect(page.getByText("Couldn't render this spec — showing source.")).toHaveCount(0);

  // READ-ONLY LOCK (the regression core): the live API client and its send action are
  // suppressed by specRender.css even though Scalar mounts them (Scalar #7741). Assert both
  // Scalar-internal hooks are not visible, and no "Send/Test Request" control is reachable.
  await expect(render.locator(".scalar-client")).toHaveCount(0);
  await expect(render.locator('[data-addressbar-action="send"]')).toHaveCount(0);
  await expect(
    render.getByRole("button", { name: /send request|test request|^send$/i }),
  ).toHaveCount(0);

  expect(consoleErrors, `unexpected console errors: ${consoleErrors.join(" | ")}`).toEqual([]);
});
