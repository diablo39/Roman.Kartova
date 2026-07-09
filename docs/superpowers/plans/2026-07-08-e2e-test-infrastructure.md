# E2E Test Infrastructure Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Stand up the project's first checked-in Playwright E2E suite (compose-orchestrated, rootless web container, real-UI-login-per-test), harden the relationship read path against drifted `type` values, and retarget DoD gate 10.

**Architecture:** A top-level `e2e/` npm project drives a browser against the full stack brought up by `docker-compose` (postgres + keycloak + migrator + api + a new rootless `web` container). Three journeys: smoke, lifecycle-override regression, relationship-drift graceful-degrade. The drift journey is backed by an EF global query filter that excludes unmappable-`type` rows from every read path. CI runs it nightly + on manual dispatch, not per-PR.

**Tech Stack:** Playwright `@playwright/test` + `pg` (Node 20), .NET 10 / EF Core 10, Docker Compose, nginx-unprivileged, GitHub Actions.

## Global Constraints

- **Solution file:** `Kartova.slnx` (XML). Windows shell: `cmd //c` or PowerShell for `dotnet`.
- **Build gate:** full solution builds with `TreatWarningsAsErrors=true` — 0 warnings, 0 errors.
- **Coverage exclusion:** every `*Dto`/`*Request`/`*Response`/`[BoundedListResult]` + design-time factories + `*Module.cs` carry `[ExcludeFromCodeCoverage]` (ContractsCoverageRules arch test). *(No new contracts in this slice.)*
- **Line endings:** repo is LF; `.gitattributes eol=lf` normalizes on commit. Do not introduce CRLF.
- **Tenant/DB:** tenant scoping is Postgres RLS via `ITenantScope` (`SET LOCAL app.current_tenant_id`), **not** EF query filters. Adding a *non-tenant* query filter (known-type) does not conflict.
- **Enum wire values (verified):** `Lifecycle { Active=1, Deprecated=2, Decommissioned=3 }` (stored `smallint`). `RelationshipType { DependsOn, ProvidesApiFor, ConsumesApiFrom, PublishesTo, SubscribesFrom, DeployedOn, InstanceOf }` (stored `string`, `HasConversion<string>`). `RelationshipOrigin { Manual, Scan, Agent }` (string). `EntityKind { Application, Service, Api }` (string).
- **OrgA tenant id (verified):** `11111111-1111-1111-1111-111111111111` (DevSeed `OrgATenantId`, mirrored in `tests/Kartova.Testing.Auth/SeededOrgs.cs`).
- **Compose facts (verified):** postgres `postgres/postgres@localhost:5432/kartova`; RLS-bypass role `kartova_bypass_rls`/`dev_only`; api `:8080`, health `GET /health/ready`; keycloak `:8180` realm `kartova`, login `admin@orga.kartova.local`/`dev_password_12`.
- **SPA quirk (verified):** deep-link cold-loads bounce (bug #47). Always `goto('/')`, authenticate, navigate in-SPA. `/` → `/catalog` → `/catalog/applications`.
- **Web config (verified):** vite build-time env; defaults `VITE_API_BASE_URL=http://localhost:8080`, `VITE_OIDC_AUTHORITY=http://localhost:8180/realms/kartova`, `VITE_OIDC_CLIENT_ID=kartova-web`. oidc-client-ts stores user in **sessionStorage**.

---

## Impact Analysis (codelens/LSP)

**Scope:** Only **one existing C# behavior** changes — adding `HasQueryFilter` to `EfRelationshipConfiguration` (Task 1). Everything else is net-new (e2e project, CI job) or non-C# infra (Dockerfile, nginx, compose) → N/A for impact analysis.

**Blast radius of the `Relationship` query filter** — enumerated from all `.Relationships` LINQ sites (grep of `src/Modules/Catalog/**/*.cs`, non-test):

| Read site (file) | Effect of the added filter |
|---|---|
| `ListRelationshipsForEntityHandler.cs:25-29` | reads now exclude unknown-`type` rows — **desired** (the drift fix). Known-type reads unchanged. |
| `GraphTraversalHandler.cs:24` | same — traversal skips unmappable edges. Desired. |
| `GetApiSurfaceHandler.cs:19,41` | same — api-surface skips unmappable edges. Desired. |
| `CatalogEndpointDelegates.cs:748` (`AnyAsync` existence check) | predicate is `type == X` for a known type; filter adds `AND type IN (known)` — no behavior change (X is always known). |
| `CreateRelationshipHandler.cs:24` / `DeleteRelationshipHandler.cs:16` | writes; filter applies only to the Delete *load-by-id*. A pre-existing unknown-`type` row becomes non-loadable for delete — acceptable (it's quarantined data). No write-path behavior change for known types. |

**Why additive, not a signature change:** `HasQueryFilter` is a model-level predicate; **no method signatures, DTOs, or public APIs change**, so no caller must be updated. Existing relationship tests all use known types → unaffected (verified: `ListRelationshipsTests.cs` seeds `DependsOn`). New coverage is added in Task 1. Codelens/`find_references` on the handlers is unnecessary — the blast radius is exactly the six `.Relationships` LINQ sites above, and the change is transparent to every known-type path.

---

## File Structure

- **Backend (C#):** `EfRelationshipConfiguration.cs` (+ query filter), new integration test in `Kartova.Catalog.IntegrationTests`.
- **Web image:** `web/Dockerfile`, `web/nginx.conf` (rootless).
- **Compose/CI:** `docker-compose.yml` (+web service), `.github/workflows/ci.yml` (+e2e job), `scripts/ci-local.sh` (+e2e subcommand).
- **Seed:** `src/Kartova.Migrator/DevSeed.cs` (+1 fixture app).
- **E2E project (new, top-level `e2e/`):** `package.json`, `playwright.config.ts`, `fixtures/auth.ts`, `fixtures/db.ts`, `tests/{smoke,lifecycle-override,relationship-drift}.spec.ts`, `run.sh`.
- **Docs:** `CLAUDE.md` (gate 10), `docs/product/CHECKLIST.md`, new ADR.

---

### Task 1: Backend — relationship read-hardening (global query filter)

**Files:**
- Modify: `src/Modules/Catalog/Kartova.Catalog.Infrastructure/EfRelationshipConfiguration.cs`
- Test: `src/Modules/Catalog/Kartova.Catalog.IntegrationTests/RelationshipTypeHardeningTests.cs` (create)

**Interfaces:**
- Produces: `EfRelationshipConfiguration` now filters `Relationship` reads to `KnownRelationshipTypes.Contains(r.Type)`. No signature change.
- Consumes: existing `CatalogIntegrationTestBase`, `Fx.CreateAuthenticatedClientAsync`, `Fx.SeedTeamInOrganizationAsync`, `Fx.TenantIdForEmail`, `KartovaApiFixtureBase.WireJson`.

- [ ] **Step 1: Write the failing integration test**

Create `RelationshipTypeHardeningTests.cs`. Seed one known edge via the API, then insert a raw `type='PartOf'` row directly (RLS-bypass connection — copy the raw-SQL/bypass-connection helper pattern from a sibling integration test that inserts raw rows; the fixture exposes a bypass connection string per compose role `kartova_bypass_rls`). Assert the relationships surface returns 200 and excludes the drift row.

```csharp
using System.Net;
using System.Net.Http.Json;
using Kartova.Catalog.Contracts;
using Kartova.Catalog.Domain;
using Kartova.SharedKernel.Pagination;
using Kartova.Testing.Auth;
using Npgsql;

namespace Kartova.Catalog.IntegrationTests;

[TestClass]
public class RelationshipTypeHardeningTests : CatalogIntegrationTestBase
{
    private const string OrgAUser = "admin@orga.kartova.local";

    private static object Rel(EntityKind sk, Guid sid, RelationshipType t, EntityKind tk, Guid tid) =>
        new { sourceKind = sk, sourceId = sid, type = t, targetKind = tk, targetId = tid };

    // Insert a relationship row whose `type` is not in the RelationshipType enum,
    // simulating drifted/legacy data (the removed 'PartOf' value). Uses the
    // RLS-bypass connection so we can write the row for OrgA's tenant directly.
    private async Task InsertDriftRowAsync(Guid tenantId, Guid sourceId, Guid targetId)
    {
        await using var conn = new NpgsqlConnection(Fx.BypassRlsConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO relationships
              (id, tenant_id, source_kind, source_id, target_kind, target_id, type, origin, created_by_user_id, created_at)
            VALUES (gen_random_uuid(), $1, 'Application', $2, 'Application', $3, 'PartOf', 'Manual', gen_random_uuid(), now());
            """;
        cmd.Parameters.AddWithValue(tenantId);
        cmd.Parameters.AddWithValue(sourceId);
        cmd.Parameters.AddWithValue(targetId);
        await cmd.ExecuteNonQueryAsync();
    }

    [TestMethod]
    public async Task Unknown_type_row_is_excluded_and_does_not_500_the_relationships_surface()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var tenantId = Fx.TenantIdForEmail(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(tenantId, "Hardening Team");

        // A known edge that MUST still be returned.
        var svcA = await SeedServiceAsync(client, teamId, "harden-a");
        var svcB = await SeedServiceAsync(client, teamId, "harden-b");
        await client.PostAsJsonAsync("/api/v1/catalog/relationships",
            Rel(EntityKind.Service, svcA, RelationshipType.DependsOn, EntityKind.Service, svcB));

        // A drift row that would previously throw at EF materialization → 500.
        await InsertDriftRowAsync(tenantId, svcA, svcB);

        var resp = await client.GetAsync(
            $"/api/v1/catalog/relationships?entityKind=Service&entityId={svcA}&direction=all");

        Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode, "unknown-type row must not 500 the surface");
        var page = await resp.Content.ReadFromJsonAsync<CursorPage<RelationshipResponse>>(KartovaApiFixtureBase.WireJson);
        Assert.AreEqual(1, page!.Items.Count, "only the known DependsOn edge is returned; drift row excluded");
        Assert.AreEqual(RelationshipType.DependsOn, page.Items[0].Type);
    }

    private static async Task<Guid> SeedServiceAsync(HttpClient client, Guid teamId, string name)
    {
        var resp = await client.PostAsJsonAsync("/api/v1/catalog/services", new
        {
            displayName = name, description = "x", teamId, endpoints = Array.Empty<object>(),
        });
        Assert.AreEqual(HttpStatusCode.Created, resp.StatusCode, $"SeedService '{name}': {resp.StatusCode}");
        var body = await resp.Content.ReadFromJsonAsync<ServiceResponse>(KartovaApiFixtureBase.WireJson);
        return body!.Id;
    }
}
```

> If `Fx.BypassRlsConnectionString` does not yet exist on the fixture, add it: expose the Testcontainers connection string built with the `kartova_bypass_rls` role (mirror how the fixture builds the app-role string). Confirm the accessor name against `KartovaApiFixture.cs` before finalizing; keep the raw-SQL insert identical.

- [ ] **Step 2: Run test to verify it fails**

Run: `cmd //c "dotnet test src/Modules/Catalog/Kartova.Catalog.IntegrationTests --filter FullyQualifiedName~RelationshipTypeHardeningTests"`
Expected: FAIL — the `GET /relationships` call 500s (EF cannot materialize `type='PartOf'`).

- [ ] **Step 3: Add the query filter**

In `EfRelationshipConfiguration.cs`, add a static known-type set and a global query filter. Place the field at the top of the class and the filter call at the end of `Configure`:

```csharp
public sealed class EfRelationshipConfiguration : IEntityTypeConfiguration<Relationship>
{
    internal const string IdFieldName = "_id";

    // Drift hardening: relationships.type is persisted as a string. A row whose value
    // is not a current RelationshipType member (e.g. a legacy 'PartOf' left by data
    // drift) would throw at EF materialization and 500 every read over the tenant's
    // relationships. Excluding unmappable rows at the SQL layer (type IN (...)) makes
    // all read paths (list / graph / api-surface) tolerant. Insert-time validation
    // still prevents new unknown types; this guards against pre-existing drift.
    private static readonly RelationshipType[] KnownRelationshipTypes = Enum.GetValues<RelationshipType>();

    public void Configure(EntityTypeBuilder<Relationship> b)
    {
        b.ToTable("relationships");
        // ... existing property mappings unchanged ...

        b.HasQueryFilter(r => KnownRelationshipTypes.Contains(r.Type));
    }
}
```

(Insert the `HasQueryFilter` line after the existing `b.Property(x => x.CreatedAt)...` line, before the closing brace.)

- [ ] **Step 4: Run test to verify it passes**

Run: `cmd //c "dotnet test src/Modules/Catalog/Kartova.Catalog.IntegrationTests --filter FullyQualifiedName~RelationshipTypeHardeningTests"`
Expected: PASS — 200, one item, drift row excluded.

- [ ] **Step 5: Regenerate EF model snapshot + run full Catalog suite**

The query filter changes the model snapshot. Run: `cmd //c "dotnet build Kartova.slnx"` then `cmd //c "dotnet test src/Modules/Catalog/Kartova.Catalog.IntegrationTests"` and `... Kartova.Catalog.Tests`.
Expected: all green (known-type reads unaffected). If `CatalogDbContextModelSnapshot` needs regen, add the migration/snapshot update. A query filter typically does **not** produce a schema migration (no DDL) — confirm no unintended migration is generated.

- [ ] **Step 6: Commit**

```bash
git add src/Modules/Catalog/Kartova.Catalog.Infrastructure/EfRelationshipConfiguration.cs \
        src/Modules/Catalog/Kartova.Catalog.IntegrationTests/RelationshipTypeHardeningTests.cs
git commit -m "feat(catalog): tolerate drifted relationship.type via global query filter"
```

---

### Task 2: Web — rootless container image

**Files:**
- Modify: `web/Dockerfile`, `web/nginx.conf`

**Interfaces:**
- Produces: `kartova/web` image runs as non-root (UID 101), listens on **8080**.

- [ ] **Step 1: Switch nginx to listen on 8080**

`web/nginx.conf`:
```nginx
server {
  listen 8080;
  root /usr/share/nginx/html;
  index index.html;

  location / {
    try_files $uri /index.html;
  }

  location ~* \.(js|css|woff2?|svg|png|jpg|ico)$ {
    expires 1y;
    add_header Cache-Control "public, immutable";
  }
}
```

- [ ] **Step 2: Switch the runtime base to nginx-unprivileged**

`web/Dockerfile` runtime stage:
```dockerfile
# ─── runtime ─────────────────────────────────────────────────────────────
FROM nginxinc/nginx-unprivileged:1.27-alpine AS runtime
COPY --from=build /app/dist /usr/share/nginx/html
COPY nginx.conf /etc/nginx/conf.d/default.conf

EXPOSE 8080
```
(Build stage unchanged.)

- [ ] **Step 3: Build + run + verify non-root and serving**

Run:
```bash
docker build -f web/Dockerfile -t kartova/web:e2e-check web
docker run -d --name web-check -p 4173:8080 kartova/web:e2e-check
sleep 2
curl -sSf http://localhost:4173/ | grep -qi "<!doctype html" && echo SERVING_OK
docker exec web-check id -u   # expect 101 (non-root)
docker rm -f web-check
```
Expected: `SERVING_OK`; `id -u` prints `101`.

- [ ] **Step 4: Confirm CI images job still builds**

Run: `docker build -f web/Dockerfile -t kartova/web:ci web`
Expected: success (this is the exact command in ci.yml's `images` job).

- [ ] **Step 5: Commit**

```bash
git add web/Dockerfile web/nginx.conf
git commit -m "feat(web): rootless nginx-unprivileged image (listen 8080)"
```

---

### Task 3: Compose — add the web service

**Files:**
- Modify: `docker-compose.yml`

**Interfaces:**
- Produces: `web` compose service serving the SPA at host `http://localhost:4173`.

- [ ] **Step 1: Add the web service**

Append to the `services:` map (mirror the `api` build/context style; publish 4173→8080):
```yaml
  web:
    build:
      context: web
      dockerfile: Dockerfile
    image: kartova/web:dev
    depends_on:
      api:
        condition: service_started
      keycloak:
        condition: service_healthy
    ports:
      - "4173:8080"
```
(No env needed — build-time defaults target `localhost:8080`/`8180`, reachable from the host browser.)

- [ ] **Step 2: Verify the stack comes up and serves**

Run:
```bash
docker compose up -d --build postgres keycloak-db keycloak migrator api web
# wait for readiness
curl -sSf http://localhost:8080/health/ready && echo API_READY
curl -sSf http://localhost:4173/ | grep -qi "<!doctype html" && echo WEB_OK
```
Expected: `API_READY`, `WEB_OK`. Leave the stack up for later tasks or `docker compose down`.

- [ ] **Step 3: Commit**

```bash
git add docker-compose.yml
git commit -m "feat(compose): add rootless web service (4173:8080)"
```

---

### Task 4: DevSeed — deterministic lifecycle-override fixture app

**Files:**
- Modify: `src/Kartova.Migrator/DevSeed.cs`

**Interfaces:**
- Produces: an OrgA application named **"E2E Sunset Override Fixture"**, lifecycle `Deprecated` (=2), `sunset_date` far-future (`2099-01-01`), so the override checkbox is reachable. Read-only in the E2E test (never decommissioned) → stable across runs.

- [ ] **Step 1: Seed the fixture app after the 120-app block**

In the app-seeding section of `DevSeed.RunAsync`, after the `for (var i = 0; i < 120; i++)` loop, insert the fixture row (reuse the demo team id already seeded above; the loop already sets `team_id`). Add:

```csharp
// Deterministic fixture for the E2E lifecycle-override journey: a Deprecated app
// with a far-future sunset date so an override-holder (OrgAdmin) sees the
// "Override sunset date" checkbox in the Decommission dialog. Read-only in the test.
var fixtureCmd = conn.CreateCommand();
fixtureCmd.CommandText = """
    INSERT INTO catalog_applications
      (id, tenant_id, display_name, description, created_by_user_id, team_id, created_at, lifecycle, sunset_date)
    VALUES ($1, $2, $3, $4, $5, $6, now(), 2, TIMESTAMPTZ '2099-01-01T00:00:00Z')
    ON CONFLICT (id) DO NOTHING;
    """;
fixtureCmd.Parameters.AddWithValue(Guid.Parse("e2e00000-0000-0000-0000-0000000f1x01"));   // fixed id → deterministic
fixtureCmd.Parameters.AddWithValue(OrgATenantId);
fixtureCmd.Parameters.AddWithValue("E2E Sunset Override Fixture");
fixtureCmd.Parameters.AddWithValue("Deprecated app with future sunset for the E2E override journey.");
fixtureCmd.Parameters.AddWithValue(SeededUserId);   // reuse the seeded team-admin user id constant
fixtureCmd.Parameters.AddWithValue(SeededTeamId);   // reuse the seeded demo team id constant
await fixtureCmd.ExecuteNonQueryAsync();
```

> Confirm the exact names of the seeded user-id and team-id constants/locals in `DevSeed.cs` (the demo-team block above defines them) and the loop's `conn` variable; reuse them rather than re-deriving. The fixed id must be a valid GUID — if `e2e0...f1x01` is not valid hex, use `Guid.Parse("e2e00000-0000-0000-0000-000000000001")`.

- [ ] **Step 2: Run the migrator/DevSeed and verify the row**

Run:
```bash
docker compose up -d --build postgres migrator
docker compose run --rm migrator   # or however DevSeed is triggered in dev
docker compose exec postgres psql -U postgres -d kartova -c \
  "SELECT display_name, lifecycle, sunset_date FROM catalog_applications WHERE display_name='E2E Sunset Override Fixture';"
```
Expected: one row, `lifecycle=2`, `sunset_date=2099-01-01…`.

- [ ] **Step 3: Commit**

```bash
git add src/Kartova.Migrator/DevSeed.cs
git commit -m "feat(devseed): deterministic deprecated+future-sunset fixture app for E2E"
```

---

### Task 5: E2E project scaffold

**Files:**
- Create: `e2e/package.json`, `e2e/playwright.config.ts`, `e2e/.gitignore`

**Interfaces:**
- Produces: `@playwright/test` config with `baseURL=http://localhost:4173`, `retries: 2`, `workers: 1`, HTML reporter + trace-on-first-retry. `pg` available for the drift fixture.

- [ ] **Step 1: Create `e2e/package.json`**

```json
{
  "name": "kartova-e2e",
  "private": true,
  "type": "module",
  "scripts": {
    "test": "playwright test",
    "report": "playwright show-report"
  },
  "devDependencies": {
    "@playwright/test": "^1.48.0",
    "pg": "^8.13.0",
    "@types/pg": "^8.11.0"
  }
}
```

- [ ] **Step 2: Create `e2e/.gitignore`**

```
node_modules/
playwright-report/
test-results/
```

- [ ] **Step 3: Create `e2e/playwright.config.ts`**

```ts
import { defineConfig, devices } from "@playwright/test";

export default defineConfig({
  testDir: "./tests",
  timeout: 60_000,
  expect: { timeout: 10_000 },
  retries: 2,          // absorb known compose/Keycloak saturation flake
  workers: 1,          // shared stack + shared DB → serialize
  reporter: [["html", { open: "never" }], ["list"]],
  use: {
    baseURL: process.env.E2E_BASE_URL ?? "http://localhost:4173",
    trace: "on-first-retry",
    screenshot: "only-on-failure",
  },
  projects: [{ name: "chromium", use: { ...devices["Desktop Chrome"] } }],
});
```

- [ ] **Step 4: Install + verify Playwright resolves**

Run:
```bash
cd e2e && npm install && npx playwright install --with-deps chromium && npx playwright test --list
```
Expected: lists 0 tests (no specs yet) without config errors.

- [ ] **Step 5: Commit**

```bash
git add e2e/package.json e2e/package-lock.json e2e/.gitignore e2e/playwright.config.ts
git commit -m "chore(e2e): scaffold Playwright project (config, deps)"
```

---

### Task 6: E2E — auth fixture + smoke spec

**Files:**
- Create: `e2e/fixtures/auth.ts`, `e2e/tests/smoke.spec.ts`

**Interfaces:**
- Produces: `login(page)` — lands on `/`, drives the Keycloak form, awaits return to the SPA. Consumed by all specs.

- [ ] **Step 1: Write `e2e/fixtures/auth.ts`**

```ts
import { type Page, expect } from "@playwright/test";

const KC_USER = process.env.E2E_USER ?? "admin@orga.kartova.local";
const KC_PASS = process.env.E2E_PASS ?? "dev_password_12";

/**
 * Log in through the real Keycloak login page. Lands on "/" (never deep-links —
 * cold-load deep links bounce, bug #47), submits the KC form, and waits for the
 * SPA to land on the catalog list.
 */
export async function login(page: Page): Promise<void> {
  await page.goto("/");
  // OIDC redirect → Keycloak login form.
  await page.getByLabel(/username or email/i).fill(KC_USER);
  await page.getByLabel(/password/i).fill(KC_PASS);
  await page.getByRole("button", { name: /sign in|log in/i }).click();
  // Back in the SPA: "/" → /catalog → /catalog/applications.
  await page.waitForURL(/\/catalog\/applications/, { timeout: 30_000 });
  await expect(page.getByRole("heading", { name: /applications/i })).toBeVisible();
}
```

> The Keycloak field labels/button text depend on the realm login theme. Confirm against a live login (the login page HTML) and adjust the locators; keep the "land on /, submit, waitForURL" shape.

- [ ] **Step 2: Write `e2e/tests/smoke.spec.ts`**

```ts
import { test, expect } from "@playwright/test";
import { login } from "../fixtures/auth";

test("smoke: login and the Applications list renders", async ({ page }) => {
  await login(page);
  await expect(page).toHaveURL(/\/catalog\/applications/);
  // DevSeed seeds ~120 apps → at least one row is present.
  await expect(page.getByRole("row").first()).toBeVisible();
});
```

- [ ] **Step 3: Run against the up stack**

Run (stack from Task 3 up + DevSeed applied):
```bash
cd e2e && npx playwright test smoke.spec.ts
```
Expected: PASS. If the row locator is wrong, inspect the list markup and use the DataTable's row role/testid.

- [ ] **Step 4: Commit**

```bash
git add e2e/fixtures/auth.ts e2e/tests/smoke.spec.ts
git commit -m "test(e2e): auth fixture + login smoke journey"
```

---

### Task 7: E2E — lifecycle-override regression spec

**Files:**
- Create: `e2e/tests/lifecycle-override.spec.ts`

**Interfaces:**
- Consumes: `login()`; the DevSeed fixture app "E2E Sunset Override Fixture".

- [ ] **Step 1: Write the spec**

Drives the fixture app's detail page → opens the lifecycle menu → Decommission → asserts the override checkbox is reachable. Selectors verified from `LifecycleMenu.tsx` / `DecommissionConfirmDialog.tsx`.

```ts
import { test, expect } from "@playwright/test";
import { login } from "../fixtures/auth";

test("lifecycle: override-holder can reach the sunset-override checkbox before sunset", async ({ page }) => {
  await login(page);

  // Navigate in-SPA to the seeded deprecated+future-sunset fixture app.
  await page.getByRole("link", { name: "E2E Sunset Override Fixture" }).click();
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
```

> If the fixture app isn't visible on the first list page (120+ apps, paginated), navigate directly after login via an in-SPA link/search, or bump the fixture name so it sorts onto page 1 (default sort = displayName asc). Simplest: use the app's fixed id — `await page.goto('/')` then in-SPA `page.evaluate` router push is disallowed (bug #47); instead filter/search the list for "E2E Sunset Override Fixture". Confirm the list has a search box; if not, raise the fixture onto page 1 by name.

- [ ] **Step 2: Run**

Run: `cd e2e && npx playwright test lifecycle-override.spec.ts`
Expected: PASS. Trace-on-retry captures selector misses.

- [ ] **Step 3: Regression-proof it (manual verification of the tripwire)**

Temporarily set `canOverride={false}` hard-coded in `ApplicationDetailPage.tsx`, rebuild the web image, re-run the spec → it must **FAIL** (menu item disabled / checkbox absent). Revert. Note the result in the DoD ledger (success criterion 3). Do not commit the revert.

- [ ] **Step 4: Commit**

```bash
git add e2e/tests/lifecycle-override.spec.ts
git commit -m "test(e2e): lifecycle sunset-override regression journey"
```

---

### Task 8: E2E — drift db fixture + relationship-drift spec

**Files:**
- Create: `e2e/fixtures/db.ts`, `e2e/tests/relationship-drift.spec.ts`

**Interfaces:**
- Produces: `withDriftEdge(sourceAppId, targetAppId)` — inserts a `type='PartOf'` row (RLS-bypass) for OrgA and returns a cleanup fn.

- [ ] **Step 1: Write `e2e/fixtures/db.ts`**

```ts
import { Client } from "pg";

const ORG_A_TENANT = "11111111-1111-1111-1111-111111111111";
const CONN =
  process.env.E2E_PG_URL ??
  "postgresql://kartova_bypass_rls:dev_only@localhost:5432/kartova";

/**
 * Insert a drifted relationship row (type='PartOf' — not a current RelationshipType)
 * for OrgA, bypassing RLS. Returns a cleanup fn that deletes exactly this row.
 * Isolated so it cannot 500 other tests.
 */
export async function insertDriftEdge(sourceId: string, targetId: string): Promise<() => Promise<void>> {
  const client = new Client({ connectionString: CONN });
  await client.connect();
  const id = crypto.randomUUID();
  await client.query(
    `INSERT INTO relationships
       (id, tenant_id, source_kind, source_id, target_kind, target_id, type, origin, created_by_user_id, created_at)
     VALUES ($1, $2, 'Application', $3, 'Application', $4, 'PartOf', 'Manual', gen_random_uuid(), now())`,
    [id, ORG_A_TENANT, sourceId, targetId],
  );
  await client.end();
  return async () => {
    const c = new Client({ connectionString: CONN });
    await c.connect();
    await c.query(`DELETE FROM relationships WHERE id = $1`, [id]);
    await c.end();
  };
}
```

- [ ] **Step 2: Write `e2e/tests/relationship-drift.spec.ts`**

Uses the fixture app as both endpoints (self-referential drift edge is fine — the point is the unknown `type`). Loads the app's relationships surface; asserts it renders without 500.

```ts
import { test, expect } from "@playwright/test";
import { login } from "../fixtures/auth";
import { insertDriftEdge } from "../fixtures/db";

const FIXTURE_APP_ID = "e2e00000-0000-0000-0000-000000000001"; // must match DevSeed fixed id

test("drift: an unmappable relationship.type does not 500 the relationships surface", async ({ page }) => {
  const cleanup = await insertDriftEdge(FIXTURE_APP_ID, FIXTURE_APP_ID);
  try {
    await login(page);
    // In-SPA to the fixture app detail (which renders RelationshipsSection).
    await page.getByRole("link", { name: "E2E Sunset Override Fixture" }).click();
    await expect(page).toHaveURL(/\/catalog\/applications\/[0-9a-f-]+$/);

    // The relationships section renders (query filter excludes the drift row);
    // no error boundary / 500 toast. Assert a stable relationships-surface anchor.
    await expect(page.getByRole("heading", { name: /relationships/i })).toBeVisible();
    // No error surface.
    await expect(page.getByText(/something went wrong|failed to load/i)).toHaveCount(0);
  } finally {
    await cleanup();
  }
});
```

> Confirm the fixed id matches DevSeed (Task 4). Confirm the relationships-surface heading text against `RelationshipsSection`; adjust the anchor if different. If the app has no relationships UI section by default, target the same page's network: assert `GET /relationships` returns 200 via `page.waitForResponse`.

- [ ] **Step 3: Run + tripwire-proof**

Run: `cd e2e && npx playwright test relationship-drift.spec.ts` → PASS (filter excludes the row).
Then temporarily revert the Task-1 `HasQueryFilter` line, rebuild api image, re-run → must **FAIL** (500). Revert-of-revert (restore the filter). Record in the ledger (success criterion 4).

- [ ] **Step 4: Commit**

```bash
git add e2e/fixtures/db.ts e2e/tests/relationship-drift.spec.ts
git commit -m "test(e2e): relationship-drift graceful-degrade journey"
```

---

### Task 9: E2E — orchestration script

**Files:**
- Create: `e2e/run.sh`

- [ ] **Step 1: Write `e2e/run.sh`**

```bash
#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."   # repo root

echo "==> Bringing up the stack (pg, keycloak, migrator, api, web)"
docker compose up -d --build postgres keycloak-db keycloak migrator api web

echo "==> Waiting for API readiness"
for i in $(seq 1 60); do
  curl -sf http://localhost:8080/health/ready >/dev/null && break
  sleep 2
  [ "$i" = 60 ] && { echo "API not ready"; docker compose logs api; exit 1; }
done

echo "==> Waiting for web"
for i in $(seq 1 30); do
  curl -sf http://localhost:4173/ >/dev/null && break
  sleep 2
  [ "$i" = 30 ] && { echo "web not ready"; exit 1; }
done

echo "==> Running Playwright"
cd e2e
npm ci
npx playwright install --with-deps chromium
npx playwright test "$@"
```

- [ ] **Step 2: Make executable + run end-to-end (all 3 specs)**

Run:
```bash
chmod +x e2e/run.sh
./e2e/run.sh
```
Expected: all 3 specs PASS from a cold stack.

- [ ] **Step 3: Commit**

```bash
git add e2e/run.sh
git commit -m "chore(e2e): run.sh stack-up + wait + playwright orchestration"
```

---

### Task 10: CI — nightly e2e job + ci-local subcommand

**Files:**
- Modify: `.github/workflows/ci.yml`, `scripts/ci-local.sh`

- [ ] **Step 1: Add the `e2e` job to `ci.yml`**

Add a top-level trigger for schedule + dispatch, and the job. (If `on:` currently only has push/pull_request, add `schedule` and `workflow_dispatch` — this does not make e2e run on PRs because the job itself is gated to those events.)

```yaml
# under `on:`
  schedule:
    - cron: "17 3 * * *"   # nightly 03:17 UTC (off the :00 fleet)
  workflow_dispatch:
```
```yaml
# under `jobs:`
  e2e:
    name: E2E (Playwright, nightly)
    if: github.event_name == 'schedule' || github.event_name == 'workflow_dispatch'
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-node@v4
        with:
          node-version: "20"
      - name: Run E2E
        run: ./e2e/run.sh
      - name: Upload Playwright report
        if: always()
        uses: actions/upload-artifact@v4
        with:
          name: playwright-report
          path: e2e/playwright-report
          retention-days: 7
```

- [ ] **Step 2: Add `e2e` subcommand to `scripts/ci-local.sh`**

Mirror the existing `job_frontend` pattern; add:
```bash
job_e2e() {   # nightly parity: full stack + playwright
  ./e2e/run.sh
}
want e2e && run_job e2e job_e2e
```
And add `e2e` to the jobs list comment (do **not** add it to the default `JOBS=(backend images stryker frontend helm)` — it's opt-in).

- [ ] **Step 3: Validate YAML + local dispatch parity**

Run: `docker run --rm -v "$PWD:/w" -w /w rhysd/actionlint:latest` (or existing lint step) → no errors.
Run: `./scripts/ci-local.sh e2e` → all 3 specs pass locally.

- [ ] **Step 4: Commit**

```bash
git add .github/workflows/ci.yml scripts/ci-local.sh
git commit -m "ci(e2e): nightly + workflow_dispatch Playwright job (not per-PR)"
```

---

### Task 11: Docs — gate-10 retarget + CHECKLIST + ADR

**Files:**
- Modify: `CLAUDE.md`, `docs/product/CHECKLIST.md`
- Create: `docs/architecture/decisions/ADR-NNNN-e2e-suite-compose-nightly.md` (number = next free; **preview to the user before saving**)

- [ ] **Step 1: Reword gate 10 in `CLAUDE.md`**

Change gate 10 to center exploratory + data-shape observation, and note the E2E suite owns deterministic-flow regressions. Replace the gate-10 bullet body with:

> 10. **Visual / API verification — observe the running system (exploratory + data-shape).** Drive the change on the real stack to surface what automated tests structurally cannot: drifted/legacy production data, unknown-unknowns, and first-time visual surfaces. UI slices → cold-start, authenticate, navigate in-SPA (ADR-0084), screenshot the changed surface; API slices → exercise the live endpoint (real auth + DB), capture request/response. **Deterministic user-flow regressions belong in the nightly Playwright E2E suite** (`e2e/`), not here — converting a gate-10 finding into an E2E spec is the expected follow-up ("any bug it finds becomes a regression test"). Gate 10 stays a per-slice human/MCP pass and does **not** fold into E2E (different lenses — the no-folding rule). Evidence under `verification/<date>-<topic>/`. N/A (with reason) only when the diff has no runtime surface.

- [ ] **Step 2: Tick the story in `CHECKLIST.md`**

Change `- [ ] E-01.F-02.S-03 — End-to-end test infrastructure (checked-in Playwright suite)` to `- [x]` with a one-line summary: compose-orchestrated Playwright suite (rootless web container, real-UI-login-per-test, per-test pg drift fixture); 3 journeys; nightly+dispatch CI; relationship read-hardening (query filter); gate 10 retargeted.

- [ ] **Step 3: Draft the ADR and PREVIEW it (do not save yet)**

Draft `ADR-NNNN` (Nygard template) capturing: option-B compose environment, rootless `nginx-unprivileged` web image, real-UI-login-per-test, per-test drift fixture, `Relationship` query-filter hardening, nightly/dispatch cadence (not per-PR) + rationale, and the boundary vs ADR-0084 (MCP dev-time) and ADR-0097 (tier-5 realized). Present the full draft to the user for approval **before** writing the file (per the ADR working agreement). On approval, save + add to the ADR index `README.md`.

- [ ] **Step 4: Commit (after ADR approval)**

```bash
git add CLAUDE.md docs/product/CHECKLIST.md docs/architecture/decisions/ADR-NNNN-*.md docs/architecture/decisions/README.md
git commit -m "docs: retarget DoD gate 10 to exploratory/data-shape; tick E-01.F-02.S-03; ADR-NNNN"
```

---

## Self-Review

**1. Spec coverage:** smoke/override/drift journeys → Tasks 6/7/8; env option-B → Tasks 2/3; rootless image → Task 2; auth → Task 6; drift fixture → Task 8; read-hardening (decision 10) → Task 1; nightly CI → Task 10; gate-10 retarget + CHECKLIST + ADR → Task 11. Success criteria 1–6 → Tasks 9/2/7/8/10/11. All spec sections mapped.

**2. Placeholder scan:** No TBD/TODO. Harness-specific unknowns (fixture bypass-connection accessor, Keycloak login-theme locators, DevSeed constant names, list search affordance, relationships-section heading) are each flagged with a concrete confirm-and-adjust note + the sibling to copy — not left blank. Code blocks are complete.

**3. Type/name consistency:** Fixture app fixed id used identically in Task 4 (DevSeed) and Task 8 (spec) — flagged to match. `KnownRelationshipTypes`, `HasQueryFilter`, health path `/health/ready`, ports 4173/8080/8180, enum values — consistent across tasks and with Global Constraints.

## DoD note

Gates per spec §7: gate 6 (mutation) **applies** (Task 1 backend logic — run `/misc:mutation-sentinel` on `EfRelationshipConfiguration.cs`); gate 3 real-seam = Task 1 integration test + the E2E suite; gate 11 E2E is **nightly/dispatch**, so this slice's E2E verification is a manual `workflow_dispatch` run + local `e2e/run.sh` evidence in the ledger, not an auto PR gate. Create the DoD ledger at `docs/superpowers/verification/2026-07-08-e2e-test-infrastructure/dod.md` (copy the template) at execution start.
