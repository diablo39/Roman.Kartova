# Slice 4 — Catalog UI: First Cut Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Land the first real UI for Kartova — master shell + catalog list + Application detail + Register Application modal — wired against slice-3's existing endpoints, using KeyCloak SPA-direct OIDC, OpenAPI codegen, TanStack Query, react-hook-form + zod, and shadcn/ui. No new backend endpoints.

**Architecture:** Frontend-heavy slice. Add `kartova-web` public OIDC client to the KeyCloak realm seed and CORS to `Kartova.Api`. In `web/`, install TanStack Query / react-hook-form / zod / oidc-client-ts / react-oidc-context / openapi-fetch / openapi-typescript / Vitest. Generate a typed API client from the live OpenAPI document at build/dev time (snapshot fallback for offline dev). Compose features under `web/src/features/catalog/` and `web/src/shared/auth/`, with route-level auth gating and a global ProblemDetails → field-error mapping helper. Verify end-to-end via Playwright MCP per ADR-0084.

**Tech Stack:** React 19 + Vite 6 + TypeScript strict, Tailwind CSS v4, shadcn/ui (new-york style, Radix primitives, lucide-react), react-router 7, @tanstack/react-query 5, react-hook-form 7, zod 3, oidc-client-ts 3, react-oidc-context 3, openapi-fetch / openapi-typescript, Vitest 2 + @testing-library/react 16, sonner. .NET 10 / ASP.NET Core 10, KeyCloak 26.1, Testcontainers 4, xUnit, FluentAssertions.

**Spec:** `docs/superpowers/specs/2026-04-30-slice-4-catalog-ui-first-cut-design.md`

---

## Pre-flight

- [ ] **Branch.** `git checkout -b feat/slice-4-catalog-ui-first-cut`. Verify `git branch --show-current` outputs the new branch.
- [ ] **Working tree clean.** `git status --short` reports nothing material (only unrelated `.claude/` artefacts permitted).
- [ ] **Build green from start.** Run `cmd //c "dotnet build Kartova.slnx -c Debug --nologo -v minimal"`. Expect `0 Warning(s) 0 Error(s)`.
- [ ] **Existing tests green.** Run `cmd //c "dotnet test Kartova.slnx --no-build --filter \"FullyQualifiedName!~IntegrationTests\" --nologo -v minimal"`. Expect all unit + arch tests pass.
- [ ] **Web project boots.** From `web/`: `npm install` (ensure lockfile clean), then `npm run build`. Expect successful Vite build of the existing placeholder app.
- [ ] **Docker reachable** (will be needed for Task 23). `cmd //c "docker version"` returns server version.
- [ ] **Mockups present.** `ls docs/ui-screens/` shows `master_shell_expanded`, `catalog_home_dashboard_fixed_navigation`, `entity_detail_payment_gateway_navigation_sync`, `create-application`.

---

## Task 1: Backlog updates — EPICS, CHECKLIST, STITCH note

**Goal:** Capture the three Section 7 backlog items in product docs before any code lands. Pure documentation.

**Files:**
- Modify: `docs/product/EPICS-AND-STORIES.md`
- Modify: `docs/product/CHECKLIST.md`
- Modify: `docs/design/STITCH-PROMPTS.md`

- [ ] **Step 1: Add `E-01.F-04.S-05` to `EPICS-AND-STORIES.md`** under feature `E-01.F-04 — Authentication`. Insert as the last story:

  ```
  - **E-01.F-04.S-05 — BFF cookie-session auth (security hardening, post-MVP).**
    Replace SPA-direct OIDC token handling with a backend-for-frontend pattern:
    ASP.NET sets an HttpOnly session cookie and exchanges the cookie for a JWT
    server-side when proxying to the API. Eliminates token exposure in browser
    memory and removes client-side refresh logic.
    Acceptance: SPA never sees the access token; cookie HttpOnly + Secure +
    SameSite=Lax; CSRF protection on state-changing requests; existing JWT
    bearer scheme remains for non-browser clients (CLI, agents, webhooks).
  ```

- [ ] **Step 2: Add `E-01.F-02.S-03` to `EPICS-AND-STORIES.md`** under `E-01.F-02 — CI/CD`:

  ```
  - **E-01.F-02.S-03 — End-to-end test infrastructure (checked-in Playwright suite).**
    CI-friendly Playwright spec suite that boots `docker compose up` with seeded
    data and drives KeyCloak login → catalog flows → entity creation → detail
    navigation. Runs in GitHub Actions on PRs. Mirrors the backend integration
    tier and extends ADR-0083's five-tier pyramid on the frontend.
    Acceptance: `npm run test:e2e` runs locally and in CI; KeyCloak token
    bootstrapping handled via realm-admin API or test-only password grant;
    deterministic test data via per-run tenant; flaky-test budget ≤ 1%.
  ```

- [ ] **Step 3: Add both story rows to `CHECKLIST.md`** as un-ticked under matching feature sections. Match existing row format exactly. Example:

  ```
  - [ ] E-01.F-04.S-05 — BFF cookie-session auth (security hardening)
  - [ ] E-01.F-02.S-03 — End-to-end test infrastructure (Playwright suite)
  ```

- [ ] **Step 4: Add parity note to `STITCH-PROMPTS.md` Screen 10.** Open the Screen 10 prompt block and append, **at the top of the consistency block** (right after the existing rules), this line:

  ```
  - Modal content only is canonical for this screen.
    The sidebar/topbar in the rendered mockup is a Stitch nav-drift
    artefact and must be ignored — implementation follows DESIGN.md
    and Screen 1 (Navigation Reference).
  ```

- [ ] **Step 5: Verify** — `git diff --stat` shows three files modified; no other changes.

- [ ] **Step 6: Commit.**

  ```bash
  git add docs/product/EPICS-AND-STORIES.md docs/product/CHECKLIST.md docs/design/STITCH-PROMPTS.md
  git commit -m "docs(slice-4): backlog stories + Stitch Screen 10 parity note"
  ```

---

## Task 2: KeyCloak realm seed — `kartova-web` public client

**Goal:** Add a dedicated public OIDC client for the SPA so the browser never reuses the API's audience client. Cover with a realm-import architecture test.

**Files:**
- Modify: `deploy/keycloak/kartova-realm.json`
- Create: `tests/Kartova.ArchitectureTests/KeycloakRealmSeedRules.cs` (or extend existing seed-rules file if present)

- [ ] **Step 1: Write the failing test.** In `tests/Kartova.ArchitectureTests/KeycloakRealmSeedRules.cs` (create if absent):

  ```csharp
  using System.IO;
  using System.Text.Json;
  using FluentAssertions;
  using Xunit;

  namespace Kartova.ArchitectureTests;

  public class KeycloakRealmSeedRules
  {
      private static readonly string SeedPath =
          Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..",
              "deploy", "keycloak", "kartova-realm.json");

      [Fact]
      public void RealmSeed_RegistersKartovaWebPublicClientWithPkce()
      {
          using var doc = JsonDocument.Parse(File.ReadAllText(SeedPath));
          var clients = doc.RootElement.GetProperty("clients");
          var web = clients.EnumerateArray()
              .FirstOrDefault(c => c.GetProperty("clientId").GetString() == "kartova-web");

          web.ValueKind.Should().NotBe(JsonValueKind.Undefined,
              "slice-4 spec §4.5 requires a kartova-web public client.");
          web.GetProperty("publicClient").GetBoolean().Should().BeTrue();
          web.GetProperty("standardFlowEnabled").GetBoolean().Should().BeTrue();
          web.GetProperty("directAccessGrantsEnabled").GetBoolean().Should().BeFalse(
              "password grant is forbidden — PKCE only.");

          var attrs = web.GetProperty("attributes");
          attrs.GetProperty("pkce.code.challenge.method").GetString()
              .Should().Be("S256");

          var redirects = web.GetProperty("redirectUris").EnumerateArray()
              .Select(e => e.GetString()).ToList();
          redirects.Should().Contain("http://localhost:5173/callback");

          var origins = web.GetProperty("webOrigins").EnumerateArray()
              .Select(e => e.GetString()).ToList();
          origins.Should().Contain("http://localhost:5173");
      }
  }
  ```

- [ ] **Step 2: Run the test, expect failure.**

  ```
  cmd //c "dotnet test tests/Kartova.ArchitectureTests/Kartova.ArchitectureTests.csproj --filter KeycloakRealmSeedRules --nologo"
  ```

  Expect: `Failed`.

- [ ] **Step 3: Add the `kartova-web` client to `deploy/keycloak/kartova-realm.json`.** Insert as the second entry in the `clients` array (after `kartova-api`):

  ```json
  {
    "clientId": "kartova-web",
    "enabled": true,
    "publicClient": true,
    "standardFlowEnabled": true,
    "directAccessGrantsEnabled": false,
    "serviceAccountsEnabled": false,
    "redirectUris": [
      "http://localhost:5173/callback",
      "http://localhost:5173/silent-callback"
    ],
    "webOrigins": ["http://localhost:5173"],
    "attributes": {
      "pkce.code.challenge.method": "S256",
      "post.logout.redirect.uris": "http://localhost:5173/*",
      "access.token.lifespan": "900"
    },
    "protocolMappers": [
      {
        "name": "tenant_id",
        "protocol": "openid-connect",
        "protocolMapper": "oidc-usermodel-attribute-mapper",
        "consentRequired": false,
        "config": {
          "user.attribute": "tenant_id",
          "claim.name": "tenant_id",
          "jsonType.label": "String",
          "id.token.claim": "true",
          "access.token.claim": "true",
          "userinfo.token.claim": "true"
        }
      },
      {
        "name": "audience-kartova-api",
        "protocol": "openid-connect",
        "protocolMapper": "oidc-audience-mapper",
        "consentRequired": false,
        "config": {
          "included.client.audience": "kartova-api",
          "id.token.claim": "false",
          "access.token.claim": "true"
        }
      }
    ]
  }
  ```

  > Note the audience-mapper: tokens issued to `kartova-web` carry `aud=kartova-api`, so the existing API JWT validation accepts them unchanged.

- [ ] **Step 4: Run the test, expect pass.**

  ```
  cmd //c "dotnet test tests/Kartova.ArchitectureTests/Kartova.ArchitectureTests.csproj --filter KeycloakRealmSeedRules --nologo"
  ```

  Expect: `Passed: 1`.

- [ ] **Step 5: Commit.**

  ```bash
  git add deploy/keycloak/kartova-realm.json tests/Kartova.ArchitectureTests/KeycloakRealmSeedRules.cs
  git commit -m "feat(keycloak): add kartova-web public client (slice-4 §4.5)"
  ```

---

## Task 3: API — CORS configuration

**Goal:** Add a CORS policy that allows configured web origins; cover with an integration test.

**Files:**
- Modify: `src/Kartova.Api/Program.cs`
- Modify: `src/Kartova.Api/appsettings.json`
- Modify: `src/Kartova.Api/appsettings.Development.json`
- Create: `tests/Kartova.Api.IntegrationTests/Cors/CorsTests.cs`

- [ ] **Step 1: Write the failing integration test.** Create `tests/Kartova.Api.IntegrationTests/Cors/CorsTests.cs`:

  ```csharp
  using System.Net;
  using System.Net.Http;
  using FluentAssertions;
  using Kartova.Testing.Auth;
  using Xunit;

  namespace Kartova.Api.IntegrationTests.Cors;

  [Collection(KartovaApiFixtureCollection.Name)]
  public class CorsTests(KartovaApiFixture fixture)
  {
      private readonly HttpClient _client = fixture.CreateRawClient();

      [Fact]
      public async Task Preflight_FromConfiguredOrigin_AllowsRequest()
      {
          var req = new HttpRequestMessage(HttpMethod.Options, "/api/v1/version");
          req.Headers.Add("Origin", "http://localhost:5173");
          req.Headers.Add("Access-Control-Request-Method", "GET");

          var resp = await _client.SendAsync(req);

          resp.StatusCode.Should().Be(HttpStatusCode.NoContent);
          resp.Headers.GetValues("Access-Control-Allow-Origin")
              .Should().ContainSingle().Which.Should().Be("http://localhost:5173");
      }

      [Fact]
      public async Task Preflight_FromUnknownOrigin_RejectsRequest()
      {
          var req = new HttpRequestMessage(HttpMethod.Options, "/api/v1/version");
          req.Headers.Add("Origin", "https://evil.example");
          req.Headers.Add("Access-Control-Request-Method", "GET");

          var resp = await _client.SendAsync(req);

          resp.Headers.Contains("Access-Control-Allow-Origin").Should().BeFalse(
              "the API must not echo origins outside the configured allowlist.");
      }
  }
  ```

  > `CreateRawClient()` returns an `HttpClient` with no auth header (used for preflight). If only an authenticated client factory exists, add a parallel raw helper to `KartovaApiFixture` in this task.

- [ ] **Step 2: Run the tests, expect failure.**

  ```
  cmd //c "dotnet test tests/Kartova.Api.IntegrationTests/Kartova.Api.IntegrationTests.csproj --filter CorsTests --nologo"
  ```

  Expect: `Failed: 2`.

- [ ] **Step 3: Add CORS to `Program.cs`.** Wire **before** `UseAuthentication`:

  Add at registration (services section, near the other AddX calls):

  ```csharp
  builder.Services.AddCors(options =>
  {
      var origins = builder.Configuration
          .GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
      options.AddPolicy("KartovaWeb", policy => policy
          .WithOrigins(origins)
          .AllowAnyHeader()
          .AllowAnyMethod()
          .AllowCredentials());
  });
  ```

  And in the pipeline, before `UseAuthentication`:

  ```csharp
  app.UseCors("KartovaWeb");
  ```

- [ ] **Step 4: Configure dev origins.** In `src/Kartova.Api/appsettings.Development.json` add:

  ```json
  "Cors": {
    "AllowedOrigins": ["http://localhost:5173"]
  }
  ```

  In `src/Kartova.Api/appsettings.json` add:

  ```json
  "Cors": {
    "AllowedOrigins": []
  }
  ```

  > Production origins are supplied via Helm/values when slice-4 is deployed; empty default makes "no CORS in prod by default" the safe baseline.

- [ ] **Step 5: Configure the integration fixture to set a dev origin.** Open `tests/Kartova.Testing.Auth/KartovaApiFixtureBase.cs` (or equivalent) and ensure the test host is launched with `Cors:AllowedOrigins:0=http://localhost:5173` in its in-memory configuration. If the fixture already merges `appsettings.Development.json`, no further change.

- [ ] **Step 6: Run CORS tests, expect pass.**

  ```
  cmd //c "dotnet test tests/Kartova.Api.IntegrationTests/Kartova.Api.IntegrationTests.csproj --filter CorsTests --nologo"
  ```

  Expect: `Passed: 2`.

- [ ] **Step 7: Run full test suite, expect green.**

  ```
  cmd //c "dotnet test Kartova.slnx --nologo -v minimal"
  ```

- [ ] **Step 8: Commit.**

  ```bash
  git add src/Kartova.Api/Program.cs src/Kartova.Api/appsettings.json src/Kartova.Api/appsettings.Development.json tests/Kartova.Api.IntegrationTests/Cors/CorsTests.cs tests/Kartova.Testing.Auth/
  git commit -m "feat(api): add CORS for SPA dev origin (slice-4 §4.5)"
  ```

---

## Task 4: Frontend dependencies + Vitest scaffold

**Goal:** Install every runtime + dev dependency the slice needs, in one commit, with Vitest configured and one passing smoke test.

**Files:**
- Modify: `web/package.json`
- Modify: `web/package-lock.json`
- Create: `web/vitest.config.ts`
- Create: `web/src/test/setup.ts`
- Create: `web/src/__smoke__/smoke.test.ts`
- Modify: `web/tsconfig.app.json` (path alias `@/*` if not yet present)

- [ ] **Step 1: Install runtime deps** (run from `web/`):

  ```bash
  npm install \
    @tanstack/react-query \
    react-hook-form \
    zod \
    @hookform/resolvers \
    oidc-client-ts \
    react-oidc-context \
    openapi-fetch \
    sonner \
    @radix-ui/react-dialog \
    @radix-ui/react-dropdown-menu \
    @radix-ui/react-avatar \
    @radix-ui/react-label \
    @radix-ui/react-slot
  ```

- [ ] **Step 2: Install dev deps:**

  ```bash
  npm install -D \
    openapi-typescript \
    vitest \
    @vitest/coverage-v8 \
    @testing-library/react \
    @testing-library/jest-dom \
    @testing-library/user-event \
    jsdom \
    msw
  ```

- [ ] **Step 3: Create `web/vitest.config.ts`:**

  ```ts
  import { defineConfig } from "vitest/config";
  import react from "@vitejs/plugin-react";
  import path from "node:path";

  export default defineConfig({
    plugins: [react()],
    resolve: { alias: { "@": path.resolve(__dirname, "src") } },
    test: {
      environment: "jsdom",
      globals: true,
      setupFiles: ["./src/test/setup.ts"],
      coverage: {
        provider: "v8",
        include: [
          "src/features/**/api/**",
          "src/features/**/schemas/**",
          "src/shared/auth/**",
        ],
        thresholds: { lines: 80, statements: 80, functions: 80, branches: 75 },
      },
    },
  });
  ```

- [ ] **Step 4: Create `web/src/test/setup.ts`:**

  ```ts
  import "@testing-library/jest-dom/vitest";
  ```

- [ ] **Step 5: Add scripts to `web/package.json`:**

  ```json
  "scripts": {
    "dev": "vite",
    "build": "tsc -b && vite build",
    "typecheck": "tsc -b --noEmit",
    "lint": "eslint .",
    "preview": "vite preview",
    "test": "vitest run",
    "test:watch": "vitest",
    "test:coverage": "vitest run --coverage"
  }
  ```

- [ ] **Step 6: Smoke test.** Create `web/src/__smoke__/smoke.test.ts`:

  ```ts
  import { describe, it, expect } from "vitest";

  describe("vitest smoke", () => {
    it("runs", () => {
      expect(1 + 1).toBe(2);
    });
  });
  ```

- [ ] **Step 7: Verify.**

  ```bash
  cd web && npm run typecheck && npm run test
  ```

  Expect: tsc clean; Vitest reports 1 passing test.

- [ ] **Step 8: Commit.**

  ```bash
  git add web/package.json web/package-lock.json web/vitest.config.ts web/src/test/setup.ts web/src/__smoke__/smoke.test.ts web/tsconfig.app.json
  git commit -m "chore(web): install slice-4 deps + Vitest scaffold"
  ```

---

## Task 5: shadcn primitives — scaffold all needed components

**Goal:** Add all shadcn components the slice will use, in one commit.

**Files (created by `shadcn` CLI under `web/src/components/ui/`):**
- `dialog.tsx`, `form.tsx`, `label.tsx`, `textarea.tsx`, `table.tsx`, `badge.tsx`, `dropdown-menu.tsx`, `avatar.tsx`, `sonner.tsx`

- [ ] **Step 1: Run scaffold** (from `web/`):

  ```bash
  npx shadcn@latest add dialog form label textarea table badge dropdown-menu avatar sonner
  ```

  Accept defaults; do not overwrite existing components if prompted.

- [ ] **Step 2: Verify build.**

  ```bash
  cd web && npm run typecheck && npm run build
  ```

  Expect: clean build.

- [ ] **Step 3: Commit.**

  ```bash
  git add web/src/components/ui/ web/package.json web/package-lock.json
  git commit -m "chore(web): scaffold shadcn primitives for slice-4"
  ```

---

## Task 6: OpenAPI codegen pipeline

**Goal:** Typed API client generated from the live OpenAPI document, with a snapshot fallback for offline dev.

**Files:**
- Create: `web/scripts/codegen.mjs`
- Create: `web/openapi-snapshot.json` (initial snapshot, committed)
- Modify: `web/package.json` (predev/prebuild scripts)
- Modify: `web/.gitignore` (exclude `src/generated/`)
- Create: `web/.env.development` (default `VITE_API_BASE_URL`)
- Create: `web/.env.example`

- [ ] **Step 1: Boot the API locally.** From repo root:

  ```bash
  cmd //c "docker compose up -d api keycloak postgres"
  ```

  Wait until `curl -s http://localhost:5080/health/ready` returns 200 (adjust port to whatever the API maps).

- [ ] **Step 2: Capture initial snapshot.**

  ```bash
  curl -sS http://localhost:5080/openapi/v1.json -o web/openapi-snapshot.json
  ```

  > Snapshot is committed so frontend devs without Docker can codegen offline.

- [ ] **Step 3: Create `web/scripts/codegen.mjs`:**

  ```js
  // @ts-check
  import { spawnSync } from "node:child_process";
  import { writeFileSync, readFileSync, mkdirSync, existsSync } from "node:fs";
  import { resolve } from "node:path";

  const baseUrl = process.env.VITE_API_BASE_URL ?? "http://localhost:5080";
  const liveUrl = `${baseUrl}/openapi/v1.json`;
  const snapshotPath = resolve("openapi-snapshot.json");
  const outDir = resolve("src/generated");
  const outFile = resolve(outDir, "openapi.ts");

  mkdirSync(outDir, { recursive: true });

  /** @type {string} */
  let spec;
  try {
    const res = await fetch(liveUrl);
    if (!res.ok) throw new Error(`HTTP ${res.status}`);
    spec = await res.text();
    writeFileSync(snapshotPath, spec, "utf8");
    console.log(`codegen: fetched live OpenAPI from ${liveUrl}`);
  } catch (err) {
    if (!existsSync(snapshotPath)) {
      console.error(
         `codegen: live fetch failed (${err}) and no snapshot at ${snapshotPath}.`);
      process.exit(1);
    }
    console.warn(
      `codegen: live fetch failed (${err}); falling back to snapshot ${snapshotPath}.`);
    spec = readFileSync(snapshotPath, "utf8");
  }

  const tmpInput = resolve(outDir, ".live.json");
  writeFileSync(tmpInput, spec, "utf8");

  const result = spawnSync(
    process.platform === "win32" ? "npx.cmd" : "npx",
    ["openapi-typescript", tmpInput, "-o", outFile],
    { stdio: "inherit" }
  );
  if (result.status !== 0) process.exit(result.status ?? 1);
  console.log(`codegen: wrote ${outFile}`);
  ```

- [ ] **Step 4: Wire scripts in `web/package.json`:**

  ```json
  "scripts": {
    "predev": "node scripts/codegen.mjs",
    "prebuild": "node scripts/codegen.mjs",
    "codegen": "node scripts/codegen.mjs",
    ...existing scripts...
  }
  ```

- [ ] **Step 5: Add to `web/.gitignore`:**

  ```
  src/generated/
  ```

- [ ] **Step 6: Create `web/.env.development`:**

  ```
  VITE_API_BASE_URL=http://localhost:5080
  ```

  And `web/.env.example`:

  ```
  VITE_API_BASE_URL=http://localhost:5080
  ```

- [ ] **Step 7: Run codegen.**

  ```bash
  cd web && npm run codegen
  ```

  Expect: `web/src/generated/openapi.ts` exists and contains paths like `"/api/v1/catalog/applications"`.

- [ ] **Step 8: Verify build.**

  ```bash
  cd web && npm run typecheck && npm run build
  ```

- [ ] **Step 9: Commit.**

  ```bash
  git add web/scripts/codegen.mjs web/openapi-snapshot.json web/package.json web/.gitignore web/.env.development web/.env.example
  git commit -m "feat(web): OpenAPI codegen pipeline with snapshot fallback (slice-4 §4.4)"
  ```

---

## Task 7: OIDC config + AuthProvider

**Goal:** Configure react-oidc-context with in-memory storage and PKCE.

**Files:**
- Create: `web/src/shared/auth/authConfig.ts`
- Create: `web/src/shared/auth/AuthProvider.tsx`
- Create: `web/src/shared/auth/__tests__/authConfig.test.ts`

- [ ] **Step 1: Write failing test.** `web/src/shared/auth/__tests__/authConfig.test.ts`:

  ```ts
  import { describe, it, expect } from "vitest";
  import { buildOidcConfig } from "../authConfig";

  describe("buildOidcConfig", () => {
    it("uses PKCE and in-memory storage", () => {
      const cfg = buildOidcConfig({
        authority: "http://kc/realms/kartova",
        clientId: "kartova-web",
        redirectUri: "http://localhost:5173/callback",
      });
      expect(cfg.response_type).toBe("code");
      expect(cfg.scope).toContain("openid");
      expect(cfg.scope).toContain("profile");
      // No localStorage / sessionStorage references — store is provided in code.
      expect(cfg.client_id).toBe("kartova-web");
      expect(cfg.redirect_uri).toBe("http://localhost:5173/callback");
    });
  });
  ```

- [ ] **Step 2: Run test, expect failure.** `npm run test -- authConfig`

- [ ] **Step 3: Implement.** `web/src/shared/auth/authConfig.ts`:

  ```ts
  import type { UserManagerSettings } from "oidc-client-ts";
  import { WebStorageStateStore, InMemoryWebStorage } from "oidc-client-ts";

  export interface AuthConfigInputs {
    authority: string;
    clientId: string;
    redirectUri: string;
  }

  export function buildOidcConfig(i: AuthConfigInputs): UserManagerSettings {
    const memory = new InMemoryWebStorage();
    return {
      authority: i.authority,
      client_id: i.clientId,
      redirect_uri: i.redirectUri,
      post_logout_redirect_uri: window.location.origin,
      response_type: "code",
      scope: "openid profile email",
      automaticSilentRenew: true,
      userStore: new WebStorageStateStore({ store: memory }),
      stateStore: new WebStorageStateStore({ store: memory }),
    };
  }
  ```

- [ ] **Step 4: AuthProvider wrapper.** `web/src/shared/auth/AuthProvider.tsx`:

  ```tsx
  import { AuthProvider as OidcAuthProvider } from "react-oidc-context";
  import { buildOidcConfig } from "./authConfig";

  const config = buildOidcConfig({
    authority: import.meta.env.VITE_OIDC_AUTHORITY ??
      "http://localhost:8080/realms/kartova",
    clientId: import.meta.env.VITE_OIDC_CLIENT_ID ?? "kartova-web",
    redirectUri: `${window.location.origin}/callback`,
  });

  export function AuthProvider({ children }: { children: React.ReactNode }) {
    return (
      <OidcAuthProvider
        {...config}
        onSigninCallback={() => {
          window.history.replaceState({}, document.title, "/");
        }}
      >
        {children}
      </OidcAuthProvider>
    );
  }
  ```

- [ ] **Step 5: Add env vars to `.env.development` and `.env.example`:**

  ```
  VITE_OIDC_AUTHORITY=http://localhost:8080/realms/kartova
  VITE_OIDC_CLIENT_ID=kartova-web
  ```

- [ ] **Step 6: Run test, expect pass.**

- [ ] **Step 7: Commit.**

  ```bash
  git add web/src/shared/auth/ web/.env.development web/.env.example
  git commit -m "feat(web): OIDC AuthProvider with PKCE + in-memory storage"
  ```

---

## Task 8: `useCurrentUser` hook + `RequireAuth` guard

**Goal:** Expose JWT profile claims (sub, name, email, tenant_id) and a route guard that triggers signin if unauthenticated.

**Files:**
- Create: `web/src/shared/auth/useCurrentUser.ts`
- Create: `web/src/shared/auth/RequireAuth.tsx`
- Create: `web/src/shared/auth/__tests__/useCurrentUser.test.tsx`

- [ ] **Step 1: Write failing test.**

  ```tsx
  import { describe, it, expect, vi } from "vitest";
  import { renderHook } from "@testing-library/react";
  import { useCurrentUser } from "../useCurrentUser";

  vi.mock("react-oidc-context", () => ({
    useAuth: () => ({
      isAuthenticated: true,
      user: {
        access_token: "t",
        profile: {
          sub: "u-1",
          name: "Alice Admin",
          email: "alice@orga.kartova.local",
          tenant_id: "11111111-1111-1111-1111-111111111111",
        },
      },
    }),
  }));

  describe("useCurrentUser", () => {
    it("returns mapped claims", () => {
      const { result } = renderHook(() => useCurrentUser());
      expect(result.current).toEqual({
        userId: "u-1",
        displayName: "Alice Admin",
        email: "alice@orga.kartova.local",
        tenantId: "11111111-1111-1111-1111-111111111111",
        accessToken: "t",
      });
    });
  });
  ```

- [ ] **Step 2: Run test, expect failure.**

- [ ] **Step 3: Implement.** `web/src/shared/auth/useCurrentUser.ts`:

  ```ts
  import { useAuth } from "react-oidc-context";

  export interface CurrentUser {
    userId: string;
    displayName: string;
    email: string;
    tenantId: string;
    accessToken: string;
  }

  export function useCurrentUser(): CurrentUser | null {
    const auth = useAuth();
    if (!auth.isAuthenticated || !auth.user) return null;
    const p = auth.user.profile as Record<string, unknown>;
    return {
      userId: String(p.sub ?? ""),
      displayName: String(p.name ?? p.preferred_username ?? p.email ?? ""),
      email: String(p.email ?? ""),
      tenantId: String(p.tenant_id ?? ""),
      accessToken: auth.user.access_token,
    };
  }
  ```

- [ ] **Step 4: RequireAuth.** `web/src/shared/auth/RequireAuth.tsx`:

  ```tsx
  import { useEffect } from "react";
  import { useAuth } from "react-oidc-context";

  export function RequireAuth({ children }: { children: React.ReactNode }) {
    const auth = useAuth();
    useEffect(() => {
      if (!auth.isLoading && !auth.isAuthenticated && !auth.activeNavigator) {
        void auth.signinRedirect();
      }
    }, [auth]);

    if (auth.isLoading || !auth.isAuthenticated) {
      return <div className="p-8 text-sm text-muted-foreground">Signing in…</div>;
    }
    return <>{children}</>;
  }
  ```

- [ ] **Step 5: Run tests, expect pass.**

- [ ] **Step 6: Commit.**

  ```bash
  git add web/src/shared/auth/useCurrentUser.ts web/src/shared/auth/RequireAuth.tsx web/src/shared/auth/__tests__/useCurrentUser.test.tsx
  git commit -m "feat(web): useCurrentUser + RequireAuth route guard"
  ```

---

## Task 9: Typed API client + auth interceptor

**Goal:** Configure `openapi-fetch` against generated types with a Bearer-token middleware that pulls from the active OIDC user.

**Files:**
- Create: `web/src/features/catalog/api/client.ts`
- Create: `web/src/features/catalog/api/__tests__/client.test.ts`

- [ ] **Step 1: Write failing test.** `web/src/features/catalog/api/__tests__/client.test.ts`:

  ```ts
  import { describe, it, expect, vi, beforeEach } from "vitest";
  import { createApiClient, setAccessTokenProvider } from "../client";

  describe("api client middleware", () => {
    beforeEach(() => {
      vi.restoreAllMocks();
    });

    it("attaches Bearer header from provider on API requests", async () => {
      setAccessTokenProvider(() => "tok-123");
      const client = createApiClient("http://api.test");

      const captured: { headers?: Headers } = {};
      vi.stubGlobal("fetch", vi.fn(async (input: RequestInfo, init?: RequestInit) => {
        captured.headers = new Headers(init?.headers);
        return new Response("[]", { status: 200, headers: { "Content-Type": "application/json" } });
      }));

      await client.GET("/api/v1/catalog/applications");
      expect(captured.headers!.get("Authorization")).toBe("Bearer tok-123");
    });

    it("does not attach Authorization when token provider returns null", async () => {
      setAccessTokenProvider(() => null);
      const client = createApiClient("http://api.test");

      const captured: { headers?: Headers } = {};
      vi.stubGlobal("fetch", vi.fn(async (_i: RequestInfo, init?: RequestInit) => {
        captured.headers = new Headers(init?.headers);
        return new Response("[]", { status: 200, headers: { "Content-Type": "application/json" } });
      }));

      await client.GET("/api/v1/catalog/applications");
      expect(captured.headers!.has("Authorization")).toBe(false);
    });
  });
  ```

- [ ] **Step 2: Run test, expect failure.**

- [ ] **Step 3: Implement.** `web/src/features/catalog/api/client.ts`:

  ```ts
  import createClient, { type Middleware } from "openapi-fetch";
  import type { paths } from "@/generated/openapi";

  type TokenProvider = () => string | null;
  let tokenProvider: TokenProvider = () => null;

  export function setAccessTokenProvider(p: TokenProvider) {
    tokenProvider = p;
  }

  let unauthorizedHandler: () => void = () => {};
  export function setUnauthorizedHandler(h: () => void) {
    unauthorizedHandler = h;
  }

  const authMiddleware: Middleware = {
    onRequest({ request }) {
      const tok = tokenProvider();
      if (tok) request.headers.set("Authorization", `Bearer ${tok}`);
      return request;
    },
    onResponse({ response }) {
      if (response.status === 401) unauthorizedHandler();
      return response;
    },
  };

  export function createApiClient(baseUrl: string) {
    const client = createClient<paths>({ baseUrl });
    client.use(authMiddleware);
    return client;
  }

  export const apiClient = createApiClient(
    import.meta.env.VITE_API_BASE_URL ?? "http://localhost:5080"
  );
  ```

- [ ] **Step 4: Run tests, expect pass.**

- [ ] **Step 5: Commit.**

  ```bash
  git add web/src/features/catalog/api/client.ts web/src/features/catalog/api/__tests__/client.test.ts
  git commit -m "feat(web): typed openapi-fetch client + auth middleware"
  ```

---

## Task 10: ProblemDetails → form-error mapper

**Goal:** Helper that translates RFC 7807 ProblemDetails (`errors: { field: [msgs] }`) into react-hook-form `setError` calls.

**Files:**
- Create: `web/src/shared/forms/problemDetails.ts`
- Create: `web/src/shared/forms/__tests__/problemDetails.test.ts`

- [ ] **Step 1: Write failing test.**

  ```ts
  import { describe, it, expect, vi } from "vitest";
  import { applyProblemDetailsToForm } from "../problemDetails";

  describe("applyProblemDetailsToForm", () => {
    it("calls setError per field/message", () => {
      const setError = vi.fn();
      applyProblemDetailsToForm(
        {
          type: "...", title: "Validation failed", status: 400,
          errors: {
            name: ["Name is required"],
            displayName: ["Display name must be at most 128 chars"],
          },
        },
        setError
      );
      expect(setError).toHaveBeenCalledWith("name", { type: "server", message: "Name is required" });
      expect(setError).toHaveBeenCalledWith("displayName", {
        type: "server",
        message: "Display name must be at most 128 chars",
      });
    });

    it("ignores payloads without errors field", () => {
      const setError = vi.fn();
      applyProblemDetailsToForm({ status: 400, title: "x" } as never, setError);
      expect(setError).not.toHaveBeenCalled();
    });

    it("returns true when at least one field error was applied", () => {
      const setError = vi.fn();
      const r = applyProblemDetailsToForm(
        { status: 400, errors: { name: ["bad"] } } as never,
        setError
      );
      expect(r).toBe(true);
    });
  });
  ```

- [ ] **Step 2: Run, expect failure.**

- [ ] **Step 3: Implement.** `web/src/shared/forms/problemDetails.ts`:

  ```ts
  export interface ProblemDetails {
    type?: string;
    title?: string;
    status?: number;
    detail?: string;
    errors?: Record<string, string[]>;
  }

  type SetError = (
    name: string,
    err: { type: string; message: string }
  ) => void;

  export function applyProblemDetailsToForm(
    p: ProblemDetails,
    setError: SetError
  ): boolean {
    if (!p?.errors || typeof p.errors !== "object") return false;
    let any = false;
    for (const [field, msgs] of Object.entries(p.errors)) {
      if (!Array.isArray(msgs)) continue;
      for (const m of msgs) {
        setError(field, { type: "server", message: m });
        any = true;
      }
    }
    return any;
  }
  ```

- [ ] **Step 4: Run tests, expect pass.**

- [ ] **Step 5: Commit.**

  ```bash
  git add web/src/shared/forms/
  git commit -m "feat(web): ProblemDetails → form-error helper"
  ```

---

## Task 11: zod schema — `registerApplicationSchema`

**Goal:** Validation schema mirroring backend domain invariants; reused by RegisterApplicationDialog.

**Files:**
- Create: `web/src/features/catalog/schemas/registerApplication.ts`
- Create: `web/src/features/catalog/schemas/__tests__/registerApplication.test.ts`

- [ ] **Step 1: Write failing test.**

  ```ts
  import { describe, it, expect } from "vitest";
  import { registerApplicationSchema } from "../registerApplication";

  describe("registerApplicationSchema", () => {
    const ok = { name: "payment-gateway", displayName: "Payment Gateway", description: "" };

    it("accepts a valid payload", () => {
      expect(registerApplicationSchema.safeParse(ok).success).toBe(true);
    });

    it("requires name", () => {
      const r = registerApplicationSchema.safeParse({ ...ok, name: "" });
      expect(r.success).toBe(false);
    });

    it("rejects non-kebab-case name", () => {
      const r = registerApplicationSchema.safeParse({ ...ok, name: "PaymentGateway" });
      expect(r.success).toBe(false);
    });

    it("requires displayName", () => {
      const r = registerApplicationSchema.safeParse({ ...ok, displayName: "" });
      expect(r.success).toBe(false);
    });

    it("rejects displayName over 128 chars", () => {
      const r = registerApplicationSchema.safeParse({ ...ok, displayName: "x".repeat(129) });
      expect(r.success).toBe(false);
    });

    it("rejects description over 512 chars", () => {
      const r = registerApplicationSchema.safeParse({ ...ok, description: "y".repeat(513) });
      expect(r.success).toBe(false);
    });
  });
  ```

- [ ] **Step 2: Run, expect failure.**

- [ ] **Step 3: Implement.** `web/src/features/catalog/schemas/registerApplication.ts`:

  ```ts
  import { z } from "zod";

  const kebabCase = /^[a-z][a-z0-9]*(-[a-z0-9]+)*$/;

  export const registerApplicationSchema = z.object({
    name: z.string()
      .min(1, "Name is required")
      .max(64, "Name must be at most 64 chars")
      .regex(kebabCase, "Lowercase kebab-case (e.g. payment-gateway)"),
    displayName: z.string()
      .min(1, "Display name is required")
      .max(128, "Display name must be at most 128 chars"),
    description: z.string().max(512, "Description must be at most 512 chars").optional()
      .or(z.literal("")),
  });

  export type RegisterApplicationInput = z.infer<typeof registerApplicationSchema>;
  ```

- [ ] **Step 4: Run tests, expect pass.**

- [ ] **Step 5: Commit.**

  ```bash
  git add web/src/features/catalog/schemas/
  git commit -m "feat(web): registerApplicationSchema (zod)"
  ```

---

## Task 12: Catalog API hooks — `useApplications`, `useApplication`, `useRegisterApplication`

**Goal:** TanStack Query hooks wrapping the typed client.

**Files:**
- Create: `web/src/features/catalog/api/applications.ts`
- Create: `web/src/features/catalog/api/__tests__/applications.test.tsx`

- [ ] **Step 1: Write failing test (lists + invalidation).**

  ```tsx
  import { describe, it, expect, vi } from "vitest";
  import { renderHook, waitFor } from "@testing-library/react";
  import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
  import { useApplications, useRegisterApplication, applicationKeys } from "../applications";
  import * as clientModule from "../client";

  function wrapper(qc: QueryClient) {
    return ({ children }: { children: React.ReactNode }) =>
      <QueryClientProvider client={qc}>{children}</QueryClientProvider>;
  }

  describe("catalog hooks", () => {
    it("useApplications fetches list and exposes data", async () => {
      const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
      vi.spyOn(clientModule, "apiClient", "get").mockReturnValue({
        GET: vi.fn().mockResolvedValue({
          data: [{ id: "a1", name: "x", displayName: "X" }],
          error: undefined,
        }),
        POST: vi.fn(),
      } as never);

      const { result } = renderHook(() => useApplications(), { wrapper: wrapper(qc) });
      await waitFor(() => expect(result.current.isSuccess).toBe(true));
      expect(result.current.data).toHaveLength(1);
    });

    it("useRegisterApplication invalidates list on success", async () => {
      const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
      const invalidate = vi.spyOn(qc, "invalidateQueries");
      vi.spyOn(clientModule, "apiClient", "get").mockReturnValue({
        GET: vi.fn(),
        POST: vi.fn().mockResolvedValue({
          data: { id: "a2", name: "n", displayName: "N" },
          error: undefined,
        }),
      } as never);

      const { result } = renderHook(() => useRegisterApplication(), { wrapper: wrapper(qc) });
      await result.current.mutateAsync({ name: "n", displayName: "N", description: "" });
      expect(invalidate).toHaveBeenCalledWith({ queryKey: applicationKeys.list() });
    });
  });
  ```

- [ ] **Step 2: Run, expect failure.**

- [ ] **Step 3: Implement.** `web/src/features/catalog/api/applications.ts`:

  ```ts
  import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
  import { apiClient } from "./client";
  import type { RegisterApplicationInput } from "../schemas/registerApplication";

  export const applicationKeys = {
    all: ["applications"] as const,
    list: () => [...applicationKeys.all, "list"] as const,
    detail: (id: string) => [...applicationKeys.all, "detail", id] as const,
  };

  export function useApplications() {
    return useQuery({
      queryKey: applicationKeys.list(),
      queryFn: async () => {
        const { data, error } = await apiClient.GET("/api/v1/catalog/applications");
        if (error) throw error;
        return data!;
      },
    });
  }

  export function useApplication(id: string) {
    return useQuery({
      queryKey: applicationKeys.detail(id),
      enabled: id !== "",
      queryFn: async () => {
        const { data, error } = await apiClient.GET(
          "/api/v1/catalog/applications/{id}",
          { params: { path: { id } } }
        );
        if (error) throw error;
        return data!;
      },
    });
  }

  export function useRegisterApplication() {
    const qc = useQueryClient();
    return useMutation({
      mutationFn: async (input: RegisterApplicationInput) => {
        const { data, error } = await apiClient.POST(
          "/api/v1/catalog/applications",
          { body: input }
        );
        if (error) throw error;
        return data!;
      },
      onSuccess: () => {
        qc.invalidateQueries({ queryKey: applicationKeys.list() });
      },
    });
  }
  ```

  > If the codegen-emitted operation paths or body types differ, adjust call sites — but keep the public hook signatures intact.

- [ ] **Step 4: Run tests, expect pass.**

- [ ] **Step 5: Commit.**

  ```bash
  git add web/src/features/catalog/api/applications.ts web/src/features/catalog/api/__tests__/applications.test.tsx
  git commit -m "feat(web): catalog query/mutation hooks"
  ```

---

## Task 13: MasterShell — Sidebar + Topbar from canonical DESIGN.md

**Goal:** Replace the placeholder layout with a shell matching `master_shell_expanded` mockup. Topbar shows tenant name from `/organizations/me`.

**Files:**
- Create: `web/src/features/organization/api/me.ts` (one-shot tenant fetcher)
- Create: `web/src/features/organization/api/__tests__/me.test.tsx`
- Modify: `web/src/components/layout/Sidebar.tsx` (existing skeleton)
- Modify: `web/src/components/layout/TopBar.tsx`
- Modify: `web/src/components/layout/AppLayout.tsx`

- [ ] **Step 1: Test for `useCurrentOrganization`.**

  ```tsx
  import { describe, it, expect, vi } from "vitest";
  import { renderHook, waitFor } from "@testing-library/react";
  import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
  import { useCurrentOrganization } from "../me";
  import * as clientModule from "@/features/catalog/api/client";

  describe("useCurrentOrganization", () => {
    it("fetches /organizations/me", async () => {
      const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
      const get = vi.fn().mockResolvedValue({
        data: { id: "o1", displayName: "Acme Corp" },
        error: undefined,
      });
      vi.spyOn(clientModule, "apiClient", "get").mockReturnValue({ GET: get, POST: vi.fn() } as never);

      const wrapper = ({ children }: { children: React.ReactNode }) =>
        <QueryClientProvider client={qc}>{children}</QueryClientProvider>;
      const { result } = renderHook(() => useCurrentOrganization(), { wrapper });
      await waitFor(() => expect(result.current.isSuccess).toBe(true));
      expect(result.current.data?.displayName).toBe("Acme Corp");
      expect(get).toHaveBeenCalledWith("/api/v1/organizations/me");
    });
  });
  ```

- [ ] **Step 2: Run, expect failure.**

- [ ] **Step 3: Implement** `web/src/features/organization/api/me.ts`:

  ```ts
  import { useQuery } from "@tanstack/react-query";
  import { apiClient } from "@/features/catalog/api/client";

  export const orgKeys = { me: ["organization", "me"] as const };

  export function useCurrentOrganization() {
    return useQuery({
      queryKey: orgKeys.me,
      queryFn: async () => {
        const { data, error } = await apiClient.GET("/api/v1/organizations/me");
        if (error) throw error;
        return data!;
      },
      staleTime: 5 * 60 * 1000,
    });
  }
  ```

- [ ] **Step 4: Update `Sidebar.tsx`** to render the canonical nav from `DESIGN.md` (active item = "Catalog"). Read existing `master_shell_expanded/code.html` for visual reference. Items: Catalog, Health, Documentation, Status, Settings (anchor only, no routes yet besides Catalog). Use `lucide-react` icons: `LayoutGrid`, `Activity`, `FileText`, `CircleDot`, `Settings`. Wrap with shadcn `Sidebar` if appropriate; otherwise plain Tailwind. Active state: blue background, white text. Width 260px expanded. **Single source of truth for items**: define `const NAV_ITEMS = [...]` at top of the file — no inline nav rendering elsewhere.

- [ ] **Step 5: Update `TopBar.tsx`** to render: left side product mark "Kartova" + selected tenant pill (from `useCurrentOrganization()` — show `Skeleton` while loading, hide on error). Right side: bell icon, settings icon (anchors), user avatar (`Avatar` with initials from `useCurrentUser().displayName`) → `DropdownMenu` with "Sign out" → calls `useAuth().signoutRedirect()`.

- [ ] **Step 6: Update `AppLayout.tsx`** to compose `Sidebar` + `TopBar` + `<main>{children}</main>` matching mockup grid.

- [ ] **Step 7: Run tests, build, expect pass.**

  ```bash
  cd web && npm run test && npm run build
  ```

- [ ] **Step 8: Commit.**

  ```bash
  git add web/src/features/organization/ web/src/components/layout/
  git commit -m "feat(web): master shell + tenant name from /organizations/me"
  ```

---

## Task 14: ApplicationsTable component

**Goal:** Render a list of applications using the shadcn `Table` primitive; loading state via `Skeleton`; empty state with CTA.

**Files:**
- Create: `web/src/features/catalog/components/ApplicationsTable.tsx`
- Create: `web/src/features/catalog/components/__tests__/ApplicationsTable.test.tsx`

- [ ] **Step 1: Test (render with rows + empty state).**

  ```tsx
  import { describe, it, expect } from "vitest";
  import { render, screen } from "@testing-library/react";
  import { MemoryRouter } from "react-router-dom";
  import { ApplicationsTable } from "../ApplicationsTable";

  function withRouter(ui: React.ReactNode) {
    return <MemoryRouter>{ui}</MemoryRouter>;
  }

  describe("ApplicationsTable", () => {
    it("renders rows", () => {
      render(withRouter(
        <ApplicationsTable
          isLoading={false}
          applications={[
            { id: "a1", name: "n1", displayName: "N One", description: "" },
            { id: "a2", name: "n2", displayName: "N Two", description: "" },
          ]}
        />
      ));
      expect(screen.getByText("N One")).toBeInTheDocument();
      expect(screen.getByText("N Two")).toBeInTheDocument();
    });

    it("shows empty state when list is empty", () => {
      render(withRouter(<ApplicationsTable isLoading={false} applications={[]} />));
      expect(screen.getByText(/no applications yet/i)).toBeInTheDocument();
    });

    it("shows skeletons while loading", () => {
      const { container } = render(withRouter(<ApplicationsTable isLoading={true} applications={undefined} />));
      expect(container.querySelectorAll('[data-testid="row-skeleton"]').length).toBeGreaterThan(0);
    });
  });
  ```

- [ ] **Step 2: Run, expect failure.**

- [ ] **Step 3: Implement** `web/src/features/catalog/components/ApplicationsTable.tsx`. Use shadcn `Table`, `Skeleton`, `Badge`. Each row links to `/catalog/applications/{id}` via `react-router-dom` `<Link>`. Empty state is centered Card with "No applications yet" + a hint that the "+ Register Application" button is in the page header. Mark skeleton row with `data-testid="row-skeleton"`. Type the props as `Application[] | undefined` matching the codegen response type (use `import type`).

- [ ] **Step 4: Run tests, expect pass.**

- [ ] **Step 5: Commit.**

  ```bash
  git add web/src/features/catalog/components/ApplicationsTable.tsx web/src/features/catalog/components/__tests__/ApplicationsTable.test.tsx
  git commit -m "feat(web): ApplicationsTable with empty/loading states"
  ```

---

## Task 15: CatalogListPage

**Goal:** Page wiring `useApplications()` + `ApplicationsTable` + "+ Register Application" header button.

**Files:**
- Create: `web/src/features/catalog/pages/CatalogListPage.tsx`
- Create: `web/src/features/catalog/pages/__tests__/CatalogListPage.test.tsx`

- [ ] **Step 1: Test — header button + table presence.**

  ```tsx
  import { describe, it, expect, vi } from "vitest";
  import { render, screen } from "@testing-library/react";
  import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
  import { MemoryRouter } from "react-router-dom";
  import { CatalogListPage } from "../CatalogListPage";
  import * as clientModule from "@/features/catalog/api/client";

  describe("CatalogListPage", () => {
    it("renders page title and Register Application button", () => {
      const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
      vi.spyOn(clientModule, "apiClient", "get").mockReturnValue({
        GET: vi.fn().mockResolvedValue({ data: [], error: undefined }),
        POST: vi.fn(),
      } as never);

      render(
        <QueryClientProvider client={qc}>
          <MemoryRouter><CatalogListPage /></MemoryRouter>
        </QueryClientProvider>
      );
      expect(screen.getByRole("heading", { name: /catalog/i })).toBeInTheDocument();
      expect(screen.getByRole("button", { name: /register application/i })).toBeInTheDocument();
    });
  });
  ```

- [ ] **Step 2: Run, expect failure.**

- [ ] **Step 3: Implement.** Page renders:
  - Header `<h2>Catalog</h2>` with right-aligned `Button` "+ Register Application" that opens the dialog (state hook `useState<boolean>` for now — wired to `RegisterApplicationDialog` in Task 17).
  - Body: `<ApplicationsTable isLoading={query.isLoading} applications={query.data} />`.
  - On query error: render an inline error card (no toast — toast is for transient errors).

- [ ] **Step 4: Run tests, expect pass.**

- [ ] **Step 5: Commit.**

  ```bash
  git add web/src/features/catalog/pages/CatalogListPage.tsx web/src/features/catalog/pages/__tests__/CatalogListPage.test.tsx
  git commit -m "feat(web): CatalogListPage"
  ```

---

## Task 16: ApplicationDetailPage

**Goal:** Detail view for a single Application, sourced from `useApplication(id)`. Header + metadata only — no tabs.

**Files:**
- Create: `web/src/features/catalog/pages/ApplicationDetailPage.tsx`
- Create: `web/src/features/catalog/pages/__tests__/ApplicationDetailPage.test.tsx`

- [ ] **Step 1: Test.**

  ```tsx
  import { describe, it, expect, vi } from "vitest";
  import { render, screen, waitFor } from "@testing-library/react";
  import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
  import { MemoryRouter, Route, Routes } from "react-router-dom";
  import { ApplicationDetailPage } from "../ApplicationDetailPage";
  import * as clientModule from "@/features/catalog/api/client";

  describe("ApplicationDetailPage", () => {
    it("renders application metadata", async () => {
      const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
      vi.spyOn(clientModule, "apiClient", "get").mockReturnValue({
        GET: vi.fn().mockResolvedValue({
          data: { id: "a1", name: "payment-gateway", displayName: "Payment Gateway", description: "Handles charges" },
          error: undefined,
        }),
        POST: vi.fn(),
      } as never);

      render(
        <QueryClientProvider client={qc}>
          <MemoryRouter initialEntries={["/catalog/applications/a1"]}>
            <Routes>
              <Route path="/catalog/applications/:id" element={<ApplicationDetailPage />} />
            </Routes>
          </MemoryRouter>
        </QueryClientProvider>
      );
      await waitFor(() => expect(screen.getByText("Payment Gateway")).toBeInTheDocument());
      expect(screen.getByText("Handles charges")).toBeInTheDocument();
      expect(screen.getByText("payment-gateway")).toBeInTheDocument();
    });
  });
  ```

- [ ] **Step 2: Run, expect failure.**

- [ ] **Step 3: Implement.** Reads `id` via `useParams<{id: string}>()`. Renders `Card` with: large `displayName` heading, `name` rendered in JetBrains Mono badge, "Active" lifecycle badge (Emerald), description paragraph (or italic muted "No description" when empty), small grid of metadata (Owner — placeholder for now if `ownerUserId` not present in DTO; ID; created date). Use `Skeleton` while loading, error card on error.

- [ ] **Step 4: Run tests, expect pass.**

- [ ] **Step 5: Commit.**

  ```bash
  git add web/src/features/catalog/pages/ApplicationDetailPage.tsx web/src/features/catalog/pages/__tests__/ApplicationDetailPage.test.tsx
  git commit -m "feat(web): ApplicationDetailPage"
  ```

---

## Task 17: RegisterApplicationDialog

**Goal:** Modal form matching `docs/ui-screens/create-application/`. Submits via `useRegisterApplication()`, shows field-level errors from ProblemDetails, toasts on success, closes and re-fetches list.

**Files:**
- Create: `web/src/features/catalog/components/RegisterApplicationDialog.tsx`
- Create: `web/src/features/catalog/components/__tests__/RegisterApplicationDialog.test.tsx`

- [ ] **Step 1: Test — happy path + validation + ProblemDetails mapping.**

  ```tsx
  import { describe, it, expect, vi } from "vitest";
  import { render, screen, waitFor } from "@testing-library/react";
  import userEvent from "@testing-library/user-event";
  import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
  import { Toaster } from "sonner";
  import { RegisterApplicationDialog } from "../RegisterApplicationDialog";
  import * as clientModule from "@/features/catalog/api/client";

  function setup(postImpl: ReturnType<typeof vi.fn>) {
    const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    vi.spyOn(clientModule, "apiClient", "get").mockReturnValue({
      GET: vi.fn(),
      POST: postImpl,
    } as never);
    const onClose = vi.fn();
    render(
      <QueryClientProvider client={qc}>
        <Toaster />
        <RegisterApplicationDialog open onOpenChange={onClose} />
      </QueryClientProvider>
    );
    return { onClose };
  }

  describe("RegisterApplicationDialog", () => {
    it("rejects empty submission with field errors", async () => {
      const post = vi.fn();
      setup(post);
      await userEvent.click(screen.getByRole("button", { name: /register application/i }));
      expect(await screen.findByText(/name is required/i)).toBeInTheDocument();
      expect(post).not.toHaveBeenCalled();
    });

    it("submits valid input and closes on 201", async () => {
      const post = vi.fn().mockResolvedValue({
        data: { id: "a3", name: "p", displayName: "P", description: "" },
        error: undefined,
      });
      const { onClose } = setup(post);
      await userEvent.type(screen.getByLabelText(/^name/i), "payment-gateway");
      await userEvent.type(screen.getByLabelText(/display name/i), "Payment Gateway");
      await userEvent.click(screen.getByRole("button", { name: /register application/i }));
      await waitFor(() => expect(post).toHaveBeenCalled());
      await waitFor(() => expect(onClose).toHaveBeenCalledWith(false));
    });

    it("maps ProblemDetails 400 errors to fields", async () => {
      const post = vi.fn().mockResolvedValue({
        data: undefined,
        error: { status: 400, errors: { name: ["Name already taken"] } },
      });
      setup(post);
      await userEvent.type(screen.getByLabelText(/^name/i), "payment-gateway");
      await userEvent.type(screen.getByLabelText(/display name/i), "Payment Gateway");
      await userEvent.click(screen.getByRole("button", { name: /register application/i }));
      expect(await screen.findByText(/name already taken/i)).toBeInTheDocument();
    });
  });
  ```

- [ ] **Step 2: Run, expect failure.**

- [ ] **Step 3: Implement.** `web/src/features/catalog/components/RegisterApplicationDialog.tsx`:

  Use shadcn `Dialog`, `Form`, `FormField`, `FormItem`, `FormLabel`, `FormControl`, `FormMessage`, `Input`, `Textarea`, `Button`, `Badge`, lucide `Loader2` for the submit spinner, `toast` from `sonner`. The `useForm` instance uses `zodResolver(registerApplicationSchema)`. Submit handler:

  ```ts
  const onSubmit = form.handleSubmit(async (values) => {
    try {
      await mutation.mutateAsync(values);
      toast.success("Application registered");
      onOpenChange(false);
      form.reset();
    } catch (err) {
      const handled = applyProblemDetailsToForm(err as ProblemDetails, form.setError);
      if (!handled) toast.error("Failed to register application");
    }
  });
  ```

  Layout matches `docs/ui-screens/create-application/code.html` (modal max-width 560px). Owner field is a read-only pill rendered from `useCurrentUser().displayName + email`. Lifecycle is a green "Active" `Badge`. Footer: secondary "Cancel" + primary "Register Application" with `<Loader2 className="animate-spin" />` when `mutation.isPending`.

- [ ] **Step 4: Run tests, expect pass.**

- [ ] **Step 5: Commit.**

  ```bash
  git add web/src/features/catalog/components/RegisterApplicationDialog.tsx web/src/features/catalog/components/__tests__/RegisterApplicationDialog.test.tsx
  git commit -m "feat(web): RegisterApplicationDialog"
  ```

---

## Task 18: Wire dialog into CatalogListPage

**Goal:** "+ Register Application" button toggles `RegisterApplicationDialog`.

**Files:**
- Modify: `web/src/features/catalog/pages/CatalogListPage.tsx`
- Modify: `web/src/features/catalog/pages/__tests__/CatalogListPage.test.tsx`

- [ ] **Step 1: Extend test** — clicking the button shows the dialog title.

  Add to existing test file:

  ```tsx
  it("opens dialog on Register Application click", async () => {
    const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    vi.spyOn(clientModule, "apiClient", "get").mockReturnValue({
      GET: vi.fn().mockResolvedValue({ data: [], error: undefined }),
      POST: vi.fn(),
    } as never);

    render(
      <QueryClientProvider client={qc}>
        <MemoryRouter><CatalogListPage /></MemoryRouter>
      </QueryClientProvider>
    );
    await userEvent.click(screen.getByRole("button", { name: /register application/i }));
    expect(await screen.findByRole("dialog", { name: /register application/i })).toBeInTheDocument();
  });
  ```

- [ ] **Step 2: Run, expect failure.**

- [ ] **Step 3: Implement.** Add `const [dialogOpen, setDialogOpen] = useState(false)` and render `<RegisterApplicationDialog open={dialogOpen} onOpenChange={setDialogOpen} />` at end of page.

- [ ] **Step 4: Run, expect pass.**

- [ ] **Step 5: Commit.**

  ```bash
  git add web/src/features/catalog/pages/CatalogListPage.tsx web/src/features/catalog/pages/__tests__/CatalogListPage.test.tsx
  git commit -m "feat(web): wire RegisterApplicationDialog into CatalogListPage"
  ```

---

## Task 19: Router + providers + main wiring

**Goal:** Compose `AuthProvider`, `QueryClientProvider`, router, and `<Toaster />` at the app root. Replace `CatalogPlaceholder` route. Bind the OIDC access-token provider to the API client.

**Files:**
- Create: `web/src/app/router.tsx`
- Create: `web/src/app/providers.tsx`
- Modify: `web/src/main.tsx`
- Modify: `web/src/App.tsx`

- [ ] **Step 1: `providers.tsx`:**

  ```tsx
  import { useEffect } from "react";
  import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
  import { useAuth } from "react-oidc-context";
  import { Toaster } from "sonner";
  import { AuthProvider } from "@/shared/auth/AuthProvider";
  import { setAccessTokenProvider, setUnauthorizedHandler } from "@/features/catalog/api/client";

  const qc = new QueryClient({
    defaultOptions: {
      queries: { retry: 1, refetchOnWindowFocus: false, staleTime: 30_000 },
    },
  });

  function ApiAuthBridge({ children }: { children: React.ReactNode }) {
    const auth = useAuth();
    useEffect(() => {
      setAccessTokenProvider(() => auth.user?.access_token ?? null);
      setUnauthorizedHandler(() => { void auth.signinRedirect(); });
    }, [auth]);
    return <>{children}</>;
  }

  export function Providers({ children }: { children: React.ReactNode }) {
    return (
      <AuthProvider>
        <QueryClientProvider client={qc}>
          <ApiAuthBridge>
            {children}
            <Toaster richColors />
          </ApiAuthBridge>
        </QueryClientProvider>
      </AuthProvider>
    );
  }
  ```

- [ ] **Step 2: `router.tsx`:**

  ```tsx
  import { Navigate, Route, Routes } from "react-router-dom";
  import { RequireAuth } from "@/shared/auth/RequireAuth";
  import { AppLayout } from "@/components/layout/AppLayout";
  import { CatalogListPage } from "@/features/catalog/pages/CatalogListPage";
  import { ApplicationDetailPage } from "@/features/catalog/pages/ApplicationDetailPage";

  export function AppRoutes() {
    return (
      <Routes>
        <Route path="/" element={<Navigate to="/catalog" replace />} />
        <Route path="/callback" element={<div className="p-8">Completing sign-in…</div>} />
        <Route element={<RequireAuth><AppLayout /></RequireAuth>}>
          <Route path="/catalog" element={<CatalogListPage />} />
          <Route path="/catalog/applications/:id" element={<ApplicationDetailPage />} />
        </Route>
        <Route path="*" element={<div className="p-8">Not found</div>} />
      </Routes>
    );
  }
  ```

  > `AppLayout` must render `<Outlet />` from `react-router-dom` for nested routes — update if not already.

- [ ] **Step 3: `App.tsx`:**

  ```tsx
  import { BrowserRouter } from "react-router-dom";
  import { Providers } from "./app/providers";
  import { AppRoutes } from "./app/router";

  export default function App() {
    return (
      <BrowserRouter>
        <Providers>
          <AppRoutes />
        </Providers>
      </BrowserRouter>
    );
  }
  ```

- [ ] **Step 4: `main.tsx`** — confirm it mounts `<App />` at `#root`. No change if already wired.

- [ ] **Step 5: Update `AppLayout.tsx`** to render `<Outlet />` inside the `<main>`.

- [ ] **Step 6: Verify.** `cd web && npm run typecheck && npm run test && npm run build`. Expect all green.

- [ ] **Step 7: Commit.**

  ```bash
  git add web/src/main.tsx web/src/App.tsx web/src/app/ web/src/components/layout/AppLayout.tsx
  git commit -m "feat(web): wire providers + router for slice-4"
  ```

---

## Task 20: ESLint rule — forbid direct `fetch` outside api/auth

**Goal:** Force every HTTP call through the typed client.

**Files:**
- Modify: `web/eslint.config.js` (or `.eslintrc.*` — whatever the project uses)

- [ ] **Step 1: Add rule.** In the project ESLint config, add an override:

  ```js
  {
    files: ["src/**/*.{ts,tsx}"],
    ignores: [
      "src/features/**/api/**",
      "src/shared/auth/**",
      "src/test/**",
      "src/__smoke__/**",
    ],
    rules: {
      "no-restricted-globals": [
        "error",
        { name: "fetch", message: "Use the typed openapi-fetch client (features/*/api)." },
      ],
    },
  }
  ```

- [ ] **Step 2: Run lint.**

  ```bash
  cd web && npm run lint
  ```

  Expect: clean.

- [ ] **Step 3: Verify rule fires.** Temporarily add `await fetch("/x");` to `web/src/App.tsx`, re-run lint, expect the rule to flag it. Revert.

- [ ] **Step 4: Commit.**

  ```bash
  git add web/eslint.config.js
  git commit -m "chore(web): forbid direct fetch outside api/auth modules"
  ```

---

## Task 21: Full backend + frontend verification (build + tests)

**Goal:** Single sweep proving everything is green before manual verification.

- [ ] **Step 1: Backend full test.**

  ```bash
  cmd //c "dotnet build Kartova.slnx -c Debug --nologo -v minimal"
  cmd //c "dotnet test Kartova.slnx --nologo -v minimal"
  ```

  Expect: 0 warnings, 0 errors, all tests pass.

- [ ] **Step 2: Frontend lint + typecheck + tests + build.**

  ```bash
  cd web && npm run lint && npm run typecheck && npm run test && npm run build
  ```

  Expect: clean.

- [ ] **Step 3: Coverage check.**

  ```bash
  cd web && npm run test:coverage
  ```

  Expect: ≥ 80% lines on `features/*/api`, `features/*/schemas`, `shared/auth`.

  If thresholds fail, add tests rather than lowering thresholds.

- [ ] **Step 4: No commit (verification step).**

---

## Task 22: Playwright MCP manual verification (DoD point 5)

**Goal:** End-to-end happy + negative path against `docker compose up`. Capture evidence for the PR description.

- [ ] **Step 1: Cold start stack.**

  ```bash
  cmd //c "docker compose down -v"
  cmd //c "docker compose up -d --build"
  ```

  Wait until `curl -s http://localhost:5080/health/ready` returns 200 and KeyCloak `http://localhost:8080/realms/kartova/.well-known/openid-configuration` returns 200.

- [ ] **Step 2: Codegen against live API.**

  ```bash
  cd web && npm run codegen
  ```

- [ ] **Step 3: Start the SPA.**

  ```bash
  cd web && npm run dev
  ```

  Confirm `http://localhost:5173` serves.

- [ ] **Step 4: Drive the flow via Playwright MCP** (`mcp__playwright__browser_*`):

  1. Navigate to `http://localhost:5173`.
  2. Verify redirect to KeyCloak login. Sign in as `member@orga.kartova.local` / `dev_pass`.
  3. Verify landing on `/catalog`. Empty state visible. Topbar shows Org A's display name.
  4. Click "+ Register Application". Submit empty form → assert "Name is required" / "Display name is required".
  5. Type `payment-gateway` / `Payment Gateway` / `Handles charges`. Submit. Assert toast "Application registered" + dialog closed + row visible.
  6. Click the row → assert `/catalog/applications/{id}` shows the same data.
  7. `mcp__playwright__browser_console_messages` → assert **zero** errors and zero warnings.
  8. `mcp__playwright__browser_take_screenshot` for: catalog list (empty), register dialog with errors, register dialog filled, list with one row, application detail. Save into `docs/superpowers/evidence/2026-04-30-slice-4/` (create folder).
  9. Negative path: `mcp__playwright__browser_navigate` to `/catalog` again — verify no re-login (silent SSO works).
  10. In KeyCloak admin (`http://localhost:8080/admin`), revoke session for the user. Back in SPA, trigger any list re-fetch (e.g., reload). Verify redirect back to KeyCloak login.

- [ ] **Step 5: Tear down (optional).**

  ```bash
  cmd //c "docker compose down -v"
  ```

- [ ] **Step 6: Commit evidence.**

  ```bash
  git add docs/superpowers/evidence/2026-04-30-slice-4/
  git commit -m "docs(slice-4): Playwright MCP verification evidence"
  ```

  > If any step fails: **stop**, file the failure as a finding in the PR description (or a backlog item if endpoint shape mismatch), do not mark slice as done.

---

## Task 23: Mark slice-4 stories complete

**Goal:** Update CHECKLIST.md with the completed work and PR reference (placeholder `PR #?` updated when PR opened).

**Files:**
- Modify: `docs/product/CHECKLIST.md`

- [ ] **Step 1: Tick** the rows touched by slice 4. Suggested edits:
  - `- [x] E-02.F-01.S-01 — Register new application in catalog (slice 4 — UI surface; PR #?, 2026-04-30)`
    - Append "(slice 4 — UI surface; PR #?, 2026-04-30)" *additively* — the existing slice-3 note remains.
  - `- [x] E-02.F-01.S-02 — Application detail page with metadata (slice 4 — PR #?, 2026-04-30)` (header + metadata only; tabs deferred).

  Leave `S-03` and `S-04` un-ticked — they're explicitly out of scope.

- [ ] **Step 2: Commit.**

  ```bash
  git add docs/product/CHECKLIST.md
  git commit -m "docs(checklist): tick slice-4 stories"
  ```

---

## Task 24: Final per-slice review + PR

**Goal:** Slice-boundary code review + PR creation.

- [ ] **Step 1: Push branch.**

  ```bash
  git push -u origin feat/slice-4-catalog-ui-first-cut
  ```

- [ ] **Step 2: Invoke `superpowers:requesting-code-review`** against the full branch diff with this plan + spec as context. Address any blocking findings.

- [ ] **Step 3: Open PR.**

  ```bash
  gh pr create --title "feat(slice-4): catalog UI first cut" --body "$(cat <<'EOF'
  ## Summary
  - Master shell + catalog list + Application detail + Register Application modal.
  - SPA-direct OIDC (PKCE) via `kartova-web` public client; in-memory token storage.
  - OpenAPI codegen pipeline with snapshot fallback.
  - CORS allowlist + integration tests.

  ## Spec
  `docs/superpowers/specs/2026-04-30-slice-4-catalog-ui-first-cut-design.md`

  ## DoD evidence
  - Backend build: 0 warnings, 0 errors.
  - Backend tests: green (unit + arch + integration).
  - Frontend: tsc clean, eslint clean, vitest green, ≥80% coverage on api/schemas/auth.
  - Playwright MCP screenshots + console-clean confirmation: `docs/superpowers/evidence/2026-04-30-slice-4/`.

  ## Backlog surfaced
  - (List any endpoint shape mismatches discovered while building screens — if none, say "no mismatches surfaced; slice-3 endpoints fit the screens.")

  🤖 Generated with [Claude Code](https://claude.com/claude-code)
  EOF
  )"
  ```

- [ ] **Step 4: Update CHECKLIST PR reference** once `gh pr view` returns the PR number — replace `PR #?` with the actual number, amend or follow-up commit.

---

## Self-review notes

Spec coverage matrix (spec section → implementing tasks):

| Spec | Task(s) |
|---|---|
| §3 Decision 1 (scope) | 13–18 |
| §3 Decision 2 (SPA-direct OIDC) | 7, 8 |
| §3 Decision 3 (codegen) | 6 |
| §3 Decision 4 (TanStack/RHF/zod/sonner/lucide) | 4, 11, 12, 17, 19 |
| §3 Decision 5 (Vitest + Playwright MCP) | 4, 22 |
| §3 Decision 6 (CORS + realm seed only) | 2, 3 |
| §3 Decision 7 (tenant in topbar) | 13 |
| §3 Decision 8 (routing) | 19 |
| §3 Decision 9 (DESIGN.md nav canonical) | 13 |
| §3 Decision 10 (ProblemDetails) | 10, 17 |
| §4.1 module layout | 7–18 |
| §4.2 endpoint topology (no change) | (verified by no new endpoint task) |
| §4.3 auth flow | 7, 8, 19 |
| §4.4 codegen pipeline | 6 |
| §4.5 backend changes | 2, 3 |
| §5 components | 13–17 |
| §6.1 Vitest unit | per-task tests |
| §6.2 arch & lint | 20, 21 |
| §6.3 backend integration tests | 2, 3 |
| §6.4 Playwright MCP verify | 22 |
| §7 backlog additions | 1 |
| §8 risks (snapshot fallback, ProblemDetails, silent SSO, Tailwind v4, sidebar drift) | 6, 10, 22, 5, 13 |
| §9 DoD | 21, 22, 24 |
