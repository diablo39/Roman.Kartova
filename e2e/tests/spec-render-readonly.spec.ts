import { test, expect } from "@playwright/test";
import { login } from "../fixtures/auth";
import { apiDetailPath } from "../fixtures/nav";

test("spec-render: API Definition tab renders the spec read-only (no live client)", async ({ page }) => {
  const consoleErrors: string[] = [];

  await login(page);

  // Registered after login() so Keycloak-login/app-shell console noise doesn't trip the
  // strict toEqual([]) assertion below.
  page.on("console", (msg) => {
    // Narrow to the SpecRender error-boundary signal ("[SpecRender] spec render failed…").
    // A blanket error check false-reds the nightly on unrelated Scalar/CSP/telemetry noise
    // (Scalar #7741 territory); this targets the actual render-failure regression.
    if (msg.type() === "error" && msg.text().includes("[SpecRender]")) consoleErrors.push(msg.text());
  });

  // Deep-link straight to the Definition tab (the #47 returnTo round-trip supports cold
  // deep-loads; login already established the session, so this loads authenticated).
  await page.goto(`${apiDetailPath()}?tab=definition`);
  await expect(page.getByRole("heading", { name: "E2E Spec Render Fixture" })).toBeVisible();

  // Rendered view is the default; the Scalar container mounts.
  const render = page.locator(".scalar-render");
  await expect(render).toBeVisible();

  // Proves it is the *rendered* spec, not the raw <pre> fallback (which would also hide the
  // client and pass the read-only checks for the wrong reason). The fixture's info.title.
  await expect(render.getByText("E2E Fixture API").first()).toBeVisible();

  // The error-boundary degrade banner must NOT be shown.
  await expect(page.getByText("Couldn't render this spec — showing source.")).toHaveCount(0);

  // READ-ONLY LOCK (the regression core): the live API client and its send action are
  // suppressed by specRender.css even though Scalar mounts them (Scalar #7741). Assert both
  // Scalar-internal hooks are not visible, and no "Send/Test Request" control is reachable.
  // Suppressed by specRender.css (display:none) even though Scalar mounts them (#7741):
  // present-but-hidden, so assert on VISIBILITY. `:visible` count is 0 while hidden and
  // >0 if the CSS lock regresses — toHaveCount(0) on the bare selector would false-red
  // (elements exist) and wouldn't change when the lock is removed.
  await expect(render.locator(".scalar-client:visible")).toHaveCount(0);
  await expect(render.locator('[data-addressbar-action="send"]:visible')).toHaveCount(0);
  await expect(
    render.getByRole("button", { name: /send request|test request|^send$/i }),
  ).toHaveCount(0);

  expect(consoleErrors, `SpecRender console errors: ${consoleErrors.join(" | ")}`).toEqual([]);
});
