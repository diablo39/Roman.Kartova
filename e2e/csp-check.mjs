// Ad-hoc CSP gate-9 probe (E-01.F-04.S-06b): drives the real web container in headless Chromium,
// walks the SPA surfaces most likely to trip CSP, and reports any Content-Security-Policy
// (Report-Only) violations. A clean run = the policy is safe to flip from Report-Only to enforcing.
// Not part of the committed suite — a manual verification aid. Run: node e2e/csp-check.mjs
import { chromium } from "@playwright/test";

const BASE = process.env.CSP_BASE ?? "http://localhost:4173";
const USER = process.env.E2E_USER ?? "admin@orga.kartova.local";
const PASS = process.env.E2E_PASS ?? "dev_password_12";

const violations = [];
const consoleErrors = [];

const browser = await chromium.launch();
const ctx = await browser.newContext();
const page = await ctx.newPage();

// Fire on every navigation, before page scripts, so report-only violations are captured document-wide.
await ctx.addInitScript(() => {
  document.addEventListener("securitypolicyviolation", (e) => {
    // eslint-disable-next-line no-console
    console.log(
      `CSPVIOLATION dir=${e.violatedDirective} blocked=${e.blockedURI} disp=${e.disposition}`,
    );
  });
});

let currentStep = "(startup)";
const perStep = {};
function note(dir) {
  perStep[currentStep] ??= {};
  perStep[currentStep][dir] = (perStep[currentStep][dir] ?? 0) + 1;
}

page.on("console", (m) => {
  const t = m.text();
  if (t.startsWith("CSPVIOLATION")) {
    violations.push(`[${currentStep}] ${t}`);
    note(t.split(" ")[1]); // dir=...
  } else if (/content security policy/i.test(t)) {
    violations.push(`[${currentStep}][console] ${t}`);
  } else if (m.type() === "error") consoleErrors.push(t);
});
page.on("pageerror", (e) => consoleErrors.push(String(e)));

async function step(label, fn) {
  currentStep = label;
  process.stdout.write(`--> ${label} ... `);
  try {
    await fn();
    console.log("ok");
  } catch (e) {
    console.log(`FAILED: ${e.message}`);
  }
}

await step("login (OIDC redirect + KeyCloak form)", async () => {
  await page.goto(`${BASE}/`);
  await page.locator("#username").fill(USER);
  await page.locator("#password").fill(PASS);
  await page.locator("#kc-login").click();
  await page.waitForURL(/\/catalog\/applications/, { timeout: 30000 });
});

await step("applications list", async () => {
  await page.goto(`${BASE}/catalog/applications`);
  await page.getByRole("heading", { name: /applications/i }).waitFor();
});

await step("application detail (Overview)", async () => {
  const href = await page
    .locator('a[href^="/catalog/applications/"]')
    .first()
    .getAttribute("href");
  await page.goto(`${BASE}${href}`);
  await page.waitForLoadState("networkidle");
});

await step("APIs list", async () => {
  await page.goto(`${BASE}/catalog/apis`);
  await page.getByRole("heading", { name: /apis/i }).waitFor();
});

await step("API Definition tab (Scalar spec render — top CSP risk)", async () => {
  const href = await page.locator('a[href^="/catalog/apis/"]').first().getAttribute("href");
  await page.goto(`${BASE}${href}?tab=definition`);
  await page.waitForLoadState("networkidle");
  await page.waitForTimeout(2500); // let the lazy Scalar chunk mount + render
});

await step("systems list", async () => {
  await page.goto(`${BASE}/catalog/systems`);
  await page.waitForLoadState("networkidle");
});

await step("organization settings (LogoUploader — blob: img-src)", async () => {
  await page.goto(`${BASE}/settings/organization`);
  await page.waitForLoadState("networkidle");
});

await step("graph explorer", async () => {
  await page.goto(`${BASE}/graph`);
  await page.waitForLoadState("networkidle");
});

await browser.close();

console.log("\n================ CSP PROBE RESULT ================");
console.log(`CSP violations: ${violations.length}`);
console.log("\nPer-surface violated directives (count):");
for (const [s, dirs] of Object.entries(perStep)) {
  console.log(`  ${s}: ${Object.entries(dirs).map(([d, c]) => `${d}×${c}`).join(", ")}`);
}
console.log(`\nOther console errors: ${consoleErrors.length}`);
for (const e of consoleErrors.slice(0, 20)) console.log("  " + e);
console.log("=================================================");
process.exit(violations.length === 0 ? 0 : 2);
