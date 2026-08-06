import { test, expect } from "@playwright/test";
import { login } from "../fixtures/auth";

/**
 * TEMPORARY gate-9 probe for the FU-A boundary band — not a regression spec.
 * Answers the question the automated suite structurally cannot: with the
 * "Include external dependencies" toggle ON, does a NON-member render inside
 * the band's rectangle? Reports geometry numerically and screenshots both states.
 */
const SYSTEM_ID = process.env.GATE9_SYSTEM_ID!;
const OUT = "../docs/superpowers/verification/2026-08-05-catalog-system-graph-nodes";

// Members of the seeded fixture vs the externals they depend on.
const MEMBERS = ["Ledger", "Fees", "Checkout Service"];
const EXTERNALS = ["Auth Service", "Notifier Service"];

async function geometry(page: import("@playwright/test").Page) {
  return page.evaluate(() => {
    const band = document.querySelector<HTMLElement>(".react-flow__node-systemBoundary");
    const bandRect = band?.getBoundingClientRect();
    const nodes = [...document.querySelectorAll<HTMLElement>(".react-flow__node-entity")].map((n) => {
      const r = n.getBoundingClientRect();
      return {
        label: (n.querySelector("div > div")?.textContent ?? "").trim(),
        left: Math.round(r.left), top: Math.round(r.top),
        right: Math.round(r.right), bottom: Math.round(r.bottom),
      };
    });
    return {
      band: bandRect
        ? { left: Math.round(bandRect.left), top: Math.round(bandRect.top),
            right: Math.round(bandRect.right), bottom: Math.round(bandRect.bottom) }
        : null,
      nodes,
    };
  });
}

test("gate 9 — boundary band vs external nodes", async ({ page }) => {
  const consoleErrors: string[] = [];
  page.on("console", (m) => { if (m.type() === "error") consoleErrors.push(m.text()); });
  page.on("pageerror", (e) => consoleErrors.push(String(e)));

  await login(page);
  await page.goto(`/catalog/systems/${SYSTEM_ID}?tab=members`);
  await expect(page.getByRole("region", { name: /system diagram/i })).toBeVisible();
  // Wait for the band to exist — proves layout ran and members were classified.
  await expect(page.locator(".react-flow__node-systemBoundary")).toBeVisible();

  const before = await geometry(page);
  console.log("GEOMETRY_OFF " + JSON.stringify(before));
  await page.screenshot({ path: `${OUT}/gate9-band-toggle-off.png` });

  // NOTE: a plain .click() on the switch fails — its inner thumb div and the tab panel both
  // "intercept pointer events" per Playwright's actionability check. Keyboard activation is the
  // accessible path and is what a real user's Space press does. Recorded as a gate-9 finding.
  const toggle = page.getByRole("switch", { name: /include external dependencies/i });
  await toggle.focus();
  await page.keyboard.press("Space");
  await expect(toggle).toBeChecked();
  // The external nodes must appear before we measure.
  await expect(page.getByText(EXTERNALS[0]!, { exact: true })).toBeVisible();

  const after = await geometry(page);
  console.log("GEOMETRY_ON " + JSON.stringify(after));
  await page.screenshot({ path: `${OUT}/gate9-band-toggle-on.png` });

  // The actual question: is any external node's box inside the band's box?
  const band = after.band!;
  const intersects = (n: { left: number; top: number; right: number; bottom: number }) =>
    n.left < band.right && n.right > band.left && n.top < band.bottom && n.bottom > band.top;
  // NOTE the matcher: a node's text content is displayName + kind concatenated
  // ("Notifier ServiceService"), so an exact-equality match against the display name silently
  // matches nothing and makes this whole check vacuously pass. Substring, deliberately.
  const named = (labels: string[]) => (n: { label: string }) => labels.some((l) => n.label.startsWith(l));
  const externalsInsideBand = after.nodes.filter((n) => named(EXTERNALS)(n) && intersects(n));
  const membersOutsideBand = after.nodes.filter((n) => named(MEMBERS)(n) && !intersects(n));
  // Guard against the matcher silently matching nothing again.
  expect(after.nodes.filter(named(EXTERNALS)), "both externals must be on the canvas").toHaveLength(2);
  expect(after.nodes.filter(named(MEMBERS)), "all three members must be on the canvas").toHaveLength(3);
  console.log("EXTERNALS_INSIDE_BAND " + JSON.stringify(externalsInsideBand));
  console.log("MEMBERS_OUTSIDE_BAND " + JSON.stringify(membersOutsideBand));
  console.log("CONSOLE_ERRORS " + JSON.stringify(consoleErrors));
});
