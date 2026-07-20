# E2E Spec-Render Read-Only + Tab-Switch Regression Specs — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add two nightly Playwright specs that lock the API-spec read-only render lock (#69 FU-1) and the detail-page tab-switch behaviour (#70 FU-1) against regression, plus the one DevSeed API+spec fixture they need.

**Architecture:** Seed a fixed-id REST API entity + a minimal OpenAPI 3.0 spec doc in `DevSeed.cs` (mirrors the existing sunset-app fixture). Two new `e2e/tests/*.spec.ts` drive that fixture on the real stack (real Keycloak + Postgres/RLS + API/web images) via `e2e/run.sh`. No production component changes.

**Tech Stack:** C# (Npgsql raw SQL in DevSeed), Playwright + TypeScript, Scalar (`@scalar/api-reference-react`), react-aria `Tabs`, PostgreSQL 18 + RLS.

## Global Constraints

- Windows shell: use `cmd //c` / PowerShell wrappers for `dotnet`; Git Bash lacks `grep -P`.
- Dates absolute (`2026-07-20`).
- `.cs` files are LF (repo `.gitattributes eol=lf`); do not introduce CRLF.
- Fixture ids are **fixed GUIDs**, mirrored across DevSeed ↔ `e2e/fixtures/nav.ts` (the "three files must agree" convention already documented in nav.ts). API id = `e2e00000-0000-0000-0000-000000000010`; spec-doc id = `e2e00000-0000-0000-0000-000000000011`.
- API style pinned via enum: `(short)ApiStyle.Rest` — never a magic literal (mirrors the existing `(short)Lifecycle.Deprecated` cast).
- Fixture is **read-only** in tests (never mutated) → stable across nightly runs.
- Playwright baseURL `http://localhost:4173` (web image); `e2e/run.sh` brings the stack up.

## Impact Analysis (codelens)

**N/A — additive only.** The sole C# change adds a self-contained fixture block inside `DevSeed.RunAsync` (the migrator's only seed entry point); no existing C# symbol's signature or behaviour changes, so there is no caller blast radius to analyse. `ApiStyle.Rest` and `Lifecycle.Deprecated` are read, not modified. All other changes are new TypeScript E2E specs + additive `nav.ts` constants (non-C#, outside the codelens graph). Confirmed `DevSeed.RunAsync(IConfiguration, ILogger)` signature is unchanged by this plan.

---

### Task 1: Seed the fixture API + OpenAPI spec in DevSeed

**Files:**
- Modify: `src/Kartova.Migrator/DevSeed.cs` (add a fixture block near the end of `RunAsync`, after the sunset-app `finally`, before the `RunAsync` closing brace)

**Interfaces:**
- Consumes: `OrgATenantId`, `DemoTeamId`, `TeamAdminUserId` (existing private fields); `ExecAsync`; `Kartova.Catalog.Domain.ApiStyle`.
- Produces: rows in `catalog_apis` (id `e2e00000-…-000000000010`, `display_name = "E2E Spec Render Fixture"`) and `catalog_api_specs` (id `…011`, `api_id` = the API id, `content` = a top-level-`openapi` JSON with `info.title = "E2E Fixture API"` and one `/ping` GET operation, `media_type = "application/json"`). These make `GET /catalog/apis/{id}` return `hasSpec: true` and the Definition tab render via Scalar.

- [ ] **Step 1: Add the fixture block to `DevSeed.RunAsync`**

Insert immediately after the closing `finally { … catalog_applications FORCE … }` block (the sunset-app block) and before the end of `RunAsync`:

```csharp
        // E2E fixture: a fixed-id REST API with a minimal OpenAPI 3.0 spec doc, seeded so the
        // spec-render read-only-lock and tab-switch E2E specs have a Definition tab to drive.
        // Mirrors the sunset-app fixture pattern (fixed id, idempotent, runs every invocation,
        // owned by the demo team). The API id + name are mirrored in e2e/fixtures/nav.ts.
        // catalog_apis + catalog_api_specs are owned by the migrator role (no BYPASSRLS) →
        // toggle FORCE off around the inserts, same as every block above.
        try
        {
            await ExecAsync(conn, "ALTER TABLE catalog_apis NO FORCE ROW LEVEL SECURITY;");
            await ExecAsync(conn, "ALTER TABLE catalog_api_specs NO FORCE ROW LEVEL SECURITY;");

            var apiId = Guid.Parse("e2e00000-0000-0000-0000-000000000010");

            await using var apiCmd = conn.CreateCommand();
            apiCmd.CommandText = """
                INSERT INTO catalog_apis
                    (id, tenant_id, display_name, description, style, version, spec_url, team_id, created_by_user_id, created_at)
                VALUES ($1, $2, $3, $4, $5, $6, NULL, $7, $8, now())
                ON CONFLICT (id) DO NOTHING;
                """;
            apiCmd.Parameters.AddWithValue(apiId);
            apiCmd.Parameters.AddWithValue(OrgATenantId);
            apiCmd.Parameters.AddWithValue("E2E Spec Render Fixture");
            apiCmd.Parameters.AddWithValue("Fixed-id REST API seeded for the E2E spec-render + tab-switch journeys.");
            apiCmd.Parameters.AddWithValue((short)Kartova.Catalog.Domain.ApiStyle.Rest);
            apiCmd.Parameters.AddWithValue("1.0.0");
            apiCmd.Parameters.AddWithValue(DemoTeamId);
            apiCmd.Parameters.AddWithValue(TeamAdminUserId);
            var apiRows = await apiCmd.ExecuteNonQueryAsync();
            logger.LogInformation("Dev seed: E2E spec-render fixture API {Result}.", apiRows == 1 ? "inserted" : "already present");

            // Minimal valid OpenAPI 3.0 doc: top-level `openapi` key → detectSpecKind = "rendered"
            // (Scalar renders by default, not the raw fallback); one operation so the per-operation
            // "Test Request" surface actually exists to prove it is suppressed.
            const string openApiDoc = """
                {
                  "openapi": "3.0.3",
                  "info": { "title": "E2E Fixture API", "version": "1.0.0" },
                  "paths": {
                    "/ping": {
                      "get": {
                        "operationId": "ping",
                        "summary": "Ping the fixture",
                        "responses": { "200": { "description": "OK" } }
                      }
                    }
                  }
                }
                """;

            await using var specCmd = conn.CreateCommand();
            specCmd.CommandText = """
                INSERT INTO catalog_api_specs
                    (id, api_id, tenant_id, content, media_type, created_by_user_id, created_at)
                VALUES ($1, $2, $3, $4, $5, $6, now())
                ON CONFLICT (id) DO NOTHING;
                """;
            specCmd.Parameters.AddWithValue(Guid.Parse("e2e00000-0000-0000-0000-000000000011"));
            specCmd.Parameters.AddWithValue(apiId);
            specCmd.Parameters.AddWithValue(OrgATenantId);
            specCmd.Parameters.AddWithValue(openApiDoc);
            specCmd.Parameters.AddWithValue("application/json");
            specCmd.Parameters.AddWithValue(TeamAdminUserId);
            var specRows = await specCmd.ExecuteNonQueryAsync();
            logger.LogInformation("Dev seed: E2E spec-render fixture spec {Result}.", specRows == 1 ? "inserted" : "already present");
        }
        finally
        {
            await ExecAsync(conn, "ALTER TABLE catalog_apis FORCE ROW LEVEL SECURITY;");
            await ExecAsync(conn, "ALTER TABLE catalog_api_specs FORCE ROW LEVEL SECURITY;");
        }
```

- [ ] **Step 2: Build the migrator project**

Run: `cmd //c "dotnet build src/Kartova.Migrator/Kartova.Migrator.csproj -warnaserror"`
Expected: Build succeeded, 0 warnings, 0 errors.

- [ ] **Step 3: Build the full solution (no regression)**

Run: `cmd //c "dotnet build Kartova.slnx -warnaserror"`
Expected: Build succeeded, 0 warnings, 0 errors. (No test change here — DevSeed is dev-fixture wiring, not business logic; there is no unit-test seam. The E2E specs in Tasks 3–4 are its test.)

- [ ] **Step 4: Commit**

```bash
git add src/Kartova.Migrator/DevSeed.cs
git commit -m "test(e2e): seed fixed-id API + OpenAPI spec fixture in DevSeed"
```

---

### Task 2: Add API fixture constants + nav helper to e2e fixtures

**Files:**
- Modify: `e2e/fixtures/nav.ts`

**Interfaces:**
- Consumes: nothing (pure constant/regex additions).
- Produces: `FIXTURE_API_ID`, `FIXTURE_API_NAME`, `API_DETAIL_URL`, `apiDetailPath(id)` for the spec files in Tasks 3–4.

- [ ] **Step 1: Append the API fixture exports to `nav.ts`**

Add below the existing app exports:

```ts
/**
 * Deterministic DevSeed fixture API (fixed id + OpenAPI spec doc) that the
 * spec-render and tab-switch specs drive. Keep in sync with the seed rows in
 * `src/Kartova.Migrator/DevSeed.cs` (same id + display name).
 */
export const FIXTURE_API_ID = "e2e00000-0000-0000-0000-000000000010";
export const FIXTURE_API_NAME = "E2E Spec Render Fixture";

/** Detail route for a catalog API (id is a GUID). */
export const API_DETAIL_URL = /\/catalog\/apis\/[0-9a-f-]+$/;

/** Deep-link path to the fixture API detail page (baseURL-relative). */
export function apiDetailPath(id: string = FIXTURE_API_ID): string {
  return `/catalog/apis/${id}`;
}
```

- [ ] **Step 2: Typecheck the e2e project**

Run: `cd e2e && npx tsc --noEmit`
Expected: no errors. (The consts are referenced by Tasks 3–4; a lone unused export still typechecks clean.)

- [ ] **Step 3: Commit**

```bash
git add e2e/fixtures/nav.ts
git commit -m "test(e2e): add fixture API constants + deep-link helper to nav"
```

---

### Task 3: Spec-render read-only lock spec

**Files:**
- Create: `e2e/tests/spec-render-readonly.spec.ts`

**Interfaces:**
- Consumes: `login` (`../fixtures/auth`); `FIXTURE_API_ID`, `apiDetailPath` (`../fixtures/nav`).
- Produces: nightly spec `spec-render-readonly.spec.ts`.

Selectors (verified against current source):
- Entity heading: `h2` `getByRole("heading", { name: "E2E Spec Render Fixture" })`.
- Tabs: `getByRole("tab", { name: "Definition" })`.
- Rendered/Raw toggle: `getByRole("group", { name: "Spec view" })` (default = "Rendered").
- Scalar container: `.scalar-render` (set in `SpecRender.tsx`); spec title token: `"E2E Fixture API"`.
- Read-only-coupled internals (from `specRender.css`): `.scalar-render .scalar-client`, `.scalar-render [data-addressbar-action="send"]`.

- [ ] **Step 1: Write the spec**

```ts
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
```

- [ ] **Step 2: Run the spec against the live stack**

Run: `e2e/run.sh spec-render-readonly.spec.ts`
Expected: PASS (1 passed). The stack builds/starts, DevSeed seeds the Task-1 fixture, Scalar renders read-only.
(If Docker is unavailable in this environment, flag **pending user verification** and continue — this is a gate-10 deliverable run at slice close.)

- [ ] **Step 3: Commit**

```bash
git add e2e/tests/spec-render-readonly.spec.ts
git commit -m "test(e2e): lock API spec-render read-only (no live client) — #69 FU-1"
```

---

### Task 4: Tab-switch happy-path spec

**Files:**
- Create: `e2e/tests/detail-tabs.spec.ts`

**Interfaces:**
- Consumes: `login` (`../fixtures/auth`); `apiDetailPath` (`../fixtures/nav`).
- Produces: nightly spec `detail-tabs.spec.ts`.

Selectors (verified):
- Tabs: `getByRole("tab", { name: "Overview" | "Dependencies" | "Definition" })`.
- Overview-only marker: `getByRole("heading", { name: "Description" })` (h3, Overview panel).
- Dependencies-only marker: `getByText("Nothing points to this API.")` (incoming-only empty state) inside region `getByRole("region", { name: "Relationships" })`.
- Definition-only marker: `getByRole("group", { name: "Spec view" })`.

- [ ] **Step 1: Write the spec**

```ts
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

  // Invalid ?tab normalizes to the default (Overview), replace (no history spam).
  await page.goto(`${apiDetailPath()}?tab=bogus`);
  await expect(page).toHaveURL(/[?&]tab=overview/);
  await expect(page.getByRole("heading", { name: "Description" })).toBeVisible();
});
```

- [ ] **Step 2: Run the spec against the live stack**

Run: `e2e/run.sh detail-tabs.spec.ts`
Expected: PASS (1 passed).
(If Docker is unavailable, flag **pending user verification** — gate-10 deliverable.)

- [ ] **Step 3: Commit**

```bash
git add e2e/tests/detail-tabs.spec.ts
git commit -m "test(e2e): lock detail-page tab-switch + ?tab sync — #70 FU-1"
```

---

### Task 5: Run both specs together + record DoD

**Files:**
- Create: `docs/superpowers/verification/2026-07-20-e2e-spec-render-tabs/dod.md` (copy `docs/superpowers/templates/dod-ledger-template.md`)
- Create: `docs/superpowers/verification/2026-07-20-e2e-spec-render-tabs/gate-findings.yaml` (copy `docs/superpowers/templates/gate-findings-template.yaml`)

- [ ] **Step 1: Run the two new specs together (nightly-shape)**

Run: `e2e/run.sh spec-render-readonly.spec.ts detail-tabs.spec.ts`
Expected: 2 passed. Confirms they coexist under the shared serialized stack (`workers: 1`).

- [ ] **Step 2: Confirm the live API reports the fixture spec**

Verify the fixture data-shape via the live API (gate-10 API check): the fixture API GET returns `hasSpec: true` and the spec GET returns the OpenAPI doc.
Run (with a valid bearer token from the running Keycloak): `curl -s -H "Authorization: Bearer $TOKEN" http://localhost:8080/api/v1/catalog/apis/e2e00000-0000-0000-0000-000000000010 | grep -o '"hasSpec":[a-z]*'`
Expected: `"hasSpec":true`.

- [ ] **Step 3: Fill the DoD ledger + findings log**

Populate `dod.md`: gate 1 (build green), 3 (backend suite green; E2E is tier-5), 4 (migrator image builds the fixture — runs on PR), 6 (N/A — no Domain/Application logic), 10 (the two specs PASS on the real stack, `curl` hasSpec check), plus 2/5/7/8/9/11 as run. Record each finding in `gate-findings.yaml`.

- [ ] **Step 4: Commit**

```bash
git add docs/superpowers/verification/2026-07-20-e2e-spec-render-tabs/
git commit -m "docs(e2e): DoD ledger + findings for spec-render/tab-switch E2E slice"
```

---

## Self-Review

**Spec coverage** (design → task):
- §2 fixture Approach A → Task 1 ✅
- §3.1 DevSeed API+spec rows → Task 1 ✅
- §3.2 nav.ts constants/deep-link → Task 2 ✅
- §3.3 read-only-lock spec → Task 3 ✅
- §3.4 tab-switch spec → Task 4 ✅
- §5 edge cases (idempotency, RLS toggle, raw-fallback masking guard, Scalar-internals coupling) → Task 1 SQL (`ON CONFLICT`, FORCE toggle) + Task 3 title-token assertion + comment ✅
- §6 testing / gate-10 local run → Tasks 3–5 `e2e/run.sh` ✅
- §7 DoD ledger → Task 5 ✅

**Placeholder scan:** no TBD/TODO; all SQL, JSON, and selectors are concrete.

**Type/name consistency:** `FIXTURE_API_ID`/`apiDetailPath` defined in Task 2, used verbatim in Tasks 3–4. API id `…010` / spec id `…011` consistent across Task 1 and Task 2. Tab labels ("Overview"/"Dependencies"/"Definition"), region name ("Relationships"), group name ("Spec view"), Scalar selectors (`.scalar-render`, `.scalar-client`, `[data-addressbar-action="send"]`), and fixture title ("E2E Fixture API") all match the current source read during planning.

**Known environment caveat:** Tasks 3–5 need Docker (`e2e/run.sh` brings up compose). If unavailable locally, those runs are **pending user verification** and the specs land verified on CI/nightly.
