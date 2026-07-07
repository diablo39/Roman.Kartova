# API Spec UI (attach/view) + Configurable Size Cap — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give the shipped API spec-storage backend a user surface (attach/view a spec on the API detail page across all styles + a "Spec" indicator column on the Apis list) and make the 5 MiB size cap operator-configurable via appsettings.

**Architecture:** Backend — move the hardcoded `ApiSpec.MaxContentBytes` const into `IOptions<CatalogSpecOptions>` (bound from `Catalog:ApiSpec`, default 5 MiB, validated 1 KiB…50 MiB); the streaming endpoint reads the configured value; the domain drops its byte-cap check. Frontend — raw-`fetch` GET/PUT helpers (mirroring `organization.ts`, bypassing openapi-fetch which JSON-parses bodies), a modal `AttachApiSpecDialog` (file + paste + JSON/YAML toggle), an inline `ApiSpecSection` (raw `<pre>` + copy) on `ApiDetailPage`, and a non-sortable `Spec` column on `ApisTable`.

**Tech Stack:** .NET 10 / ASP.NET Core minimal APIs · EF Core · MSTest v4 + NSubstitute · Testcontainers (Postgres/RLS) · React + TypeScript · TanStack Query · react-oidc-context · Untitled UI (react-aria-components) · Vitest + RTL.

**Spec:** `docs/superpowers/specs/2026-07-07-catalog-api-spec-ui-and-configurable-cap-design.md`

## Global Constraints

- **Windows shell:** `cmd //c` or PowerShell wrappers for `dotnet`; multi-line git messages via PowerShell + multiple `-m`.
- **Solution:** `Kartova.slnx`. Build with `TreatWarningsAsErrors=true` (gate 1: 0 warnings).
- **Coverage exclusion:** options POCO, DTOs/Contracts, `IModule` classes carry `[ExcludeFromCodeCoverage]` (arch test `ContractsCoverageRules`).
- **No new `KartovaPermission`** — reuse `catalog.apis.register` (PUT) + `catalog.read` (GET). The 5-sync is **not** triggered.
- **Media-type allowlist:** `application/json`, `application/yaml` (backend `ApiMediaType.IsAllowed`, unchanged).
- **Cap band:** `1024` ≤ `MaxContentBytes` ≤ `50 * 1024 * 1024`; default `5 * 1024 * 1024`. Config section `Catalog:ApiSpec`.
- **Raw-fetch rule (frontend):** spec GET/PUT bypass `apiClient`; compose `${API_BASE_URL}/...` + `Authorization: Bearer` + explicit `Content-Type` (openapi-fetch hard-codes `application/json` + JSON-parses responses → breaks YAML).
- **No client-side size mirror; no client-side membership gate** — backend is authoritative; button gated on the `catalog.apis.register` permission only.
- **react-aria `<Table>`** keeps exactly one `isRowHeader` column (`displayName`, already set) — do not remove it.
- **Enum wire convention:** camelCase (ADR-0109) — `style` values `rest|grpc|graphQL|asyncApi`.
- **Gate 6 (mutation) is BLOCKING** for this slice (Task 2 touches Domain/Application logic).

---

### Task 1: `CatalogSpecOptions` + validator (configurable cap foundation)

**Files:**
- Create: `src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogSpecOptions.cs`
- Create: `src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogSpecOptionsValidator.cs`
- Modify: `src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogModule.cs:237-285` (RegisterServices)
- Modify: `src/Kartova.Api/appsettings.json`
- Test: `src/Modules/Catalog/Kartova.Catalog.Infrastructure.Tests/CatalogSpecOptionsValidatorTests.cs`

**Interfaces:**
- Produces: `CatalogSpecOptions { int MaxContentBytes }` (default `5 * 1024 * 1024`), `const string CatalogSpecOptions.SectionName = "Catalog:ApiSpec"`; `CatalogSpecOptionsValidator : IValidateOptions<CatalogSpecOptions>`. Consumed by Task 2's endpoint.

- [ ] **Step 1: Write the failing validator test**

Create `src/Modules/Catalog/Kartova.Catalog.Infrastructure.Tests/CatalogSpecOptionsValidatorTests.cs`:

```csharp
using Kartova.Catalog.Infrastructure;
using Microsoft.Extensions.Options;

namespace Kartova.Catalog.Infrastructure.Tests;

[TestClass]
public sealed class CatalogSpecOptionsValidatorTests
{
    private static ValidateOptionsResult Validate(int maxBytes)
        => new CatalogSpecOptionsValidator().Validate(null, new CatalogSpecOptions { MaxContentBytes = maxBytes });

    [TestMethod]
    public void Rejects_below_floor()
    {
        Assert.IsTrue(Validate(0).Failed);
        Assert.IsTrue(Validate(1023).Failed);
    }

    [TestMethod]
    public void Rejects_above_ceiling()
        => Assert.IsTrue(Validate(50 * 1024 * 1024 + 1).Failed);

    [TestMethod]
    public void Accepts_within_band()
    {
        Assert.IsTrue(Validate(1024).Succeeded);
        Assert.IsTrue(Validate(5 * 1024 * 1024).Succeeded);
        Assert.IsTrue(Validate(50 * 1024 * 1024).Succeeded);
    }

    [TestMethod]
    public void Default_option_value_is_five_mib_and_valid()
    {
        var opts = new CatalogSpecOptions();
        Assert.AreEqual(5 * 1024 * 1024, opts.MaxContentBytes);
        Assert.IsTrue(new CatalogSpecOptionsValidator().Validate(null, opts).Succeeded);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cmd //c dotnet test src/Modules/Catalog/Kartova.Catalog.Infrastructure.Tests --filter CatalogSpecOptionsValidatorTests`
Expected: FAIL — `CatalogSpecOptions` / `CatalogSpecOptionsValidator` do not exist (compile error).

- [ ] **Step 3: Create the options POCO**

Create `src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogSpecOptions.cs`:

```csharp
using System.Diagnostics.CodeAnalysis;

namespace Kartova.Catalog.Infrastructure;

/// <summary>Operator-tunable bounds for stored API spec documents (ADR-0112).
/// Bound from configuration section <see cref="SectionName"/>; default 5 MiB.</summary>
[ExcludeFromCodeCoverage]
public sealed class CatalogSpecOptions
{
    public const string SectionName = "Catalog:ApiSpec";

    /// <summary>Maximum UTF-8 byte length of a stored spec. Enforced at the upload
    /// endpoint (declared-length pre-check + streamed read cap). Validated into
    /// [1 KiB, 50 MiB] by <see cref="CatalogSpecOptionsValidator"/>.</summary>
    public int MaxContentBytes { get; set; } = 5 * 1024 * 1024;
}
```

- [ ] **Step 4: Create the validator**

Create `src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogSpecOptionsValidator.cs`:

```csharp
using Microsoft.Extensions.Options;

namespace Kartova.Catalog.Infrastructure;

/// <summary>Fails fast at startup if the configured spec cap is outside a safe band.
/// The streamed read buffers up to the cap, so an absurd value (e.g. 10 GB) is an
/// unbounded-memory vector — bound it to [1 KiB, 50 MiB].</summary>
public sealed class CatalogSpecOptionsValidator : IValidateOptions<CatalogSpecOptions>
{
    private const int Floor = 1024;                 // 1 KiB
    private const int Ceiling = 50 * 1024 * 1024;   // 50 MiB

    public ValidateOptionsResult Validate(string? name, CatalogSpecOptions options)
        => options.MaxContentBytes is >= Floor and <= Ceiling
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(
                $"{CatalogSpecOptions.SectionName}:MaxContentBytes must be between {Floor} and {Ceiling} bytes; got {options.MaxContentBytes}.");
}
```

- [ ] **Step 5: Run test to verify it passes**

Run: `cmd //c dotnet test src/Modules/Catalog/Kartova.Catalog.Infrastructure.Tests --filter CatalogSpecOptionsValidatorTests`
Expected: PASS (4 tests).

- [ ] **Step 6: Register options + validator in CatalogModule**

In `CatalogModule.RegisterServices` (after the `AddModuleDbContext` block, before the handler `AddScoped` calls), add:

```csharp
// Configurable stored-spec size cap (ADR-0112). Default 5 MiB; validated on start
// into a safe band so an operator typo can't create an unbounded-buffer OOM vector.
services.AddOptions<CatalogSpecOptions>()
    .Bind(configuration.GetSection(CatalogSpecOptions.SectionName))
    .ValidateOnStart();
services.AddSingleton<IValidateOptions<CatalogSpecOptions>, CatalogSpecOptionsValidator>();
```

Add `using Microsoft.Extensions.Options;` to the file's usings if not present.

- [ ] **Step 7: Add the default to appsettings.json**

In `src/Kartova.Api/appsettings.json`, add a top-level `Catalog` section (merge if one exists):

```json
"Catalog": {
  "ApiSpec": {
    "MaxContentBytes": 5242880
  }
}
```

(5242880 = 5 MiB. Valid band: 1024 … 52428800.)

- [ ] **Step 8: Build + run the new test file**

Run: `cmd //c dotnet build Kartova.slnx -p:TreatWarningsAsErrors=true`
Then: `cmd //c dotnet test src/Modules/Catalog/Kartova.Catalog.Infrastructure.Tests --filter CatalogSpecOptionsValidatorTests`
Expected: build 0 warnings; 4 tests PASS.

- [ ] **Step 9: Commit**

```bash
git add src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogSpecOptions.cs \
        src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogSpecOptionsValidator.cs \
        src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogModule.cs \
        src/Kartova.Api/appsettings.json \
        src/Modules/Catalog/Kartova.Catalog.Infrastructure.Tests/CatalogSpecOptionsValidatorTests.cs
git commit -m "feat(catalog): configurable API spec size cap (CatalogSpecOptions, default 5 MiB, validated 1 KiB..50 MiB)"
```

---

### Task 2: Rewire endpoint to the configured cap; drop the domain byte-cap

**Files:**
- Modify: `src/Modules/Catalog/Kartova.Catalog.Domain/ApiSpec.cs` (remove const + byte check)
- Modify: `src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogEndpointDelegates.cs` (`UpsertApiSpecAsync` + `SpecTooLarge`)
- Modify: `src/Kartova.SharedKernel.AspNetCore/ProblemTypes.cs:85` (comment)
- Modify: `src/Modules/Catalog/Kartova.Catalog.Tests/ApiSpecTests.cs` (remove 3 cap tests)
- Modify: `src/Modules/Catalog/Kartova.Catalog.IntegrationTests/ApiSpecTests.cs` (config-override boundary test)

**Interfaces:**
- Consumes: `CatalogSpecOptions.MaxContentBytes` (Task 1) via `IOptions<CatalogSpecOptions>` (minimal-API auto-injected).
- Produces: `UpsertApiSpecAsync` behavior — declared-length + streamed cap + 400 message all read the configured value; `ApiSpec` no longer exposes `MaxContentBytes`.

- [ ] **Step 1: Write the failing integration test (configured boundary)**

In `src/Modules/Catalog/Kartova.Catalog.IntegrationTests/ApiSpecTests.cs`, **replace** the `PUT_with_oversized_content_returns_400` method (currently at ~:238, references the removed const) with:

```csharp
[TestMethod]
public async Task PUT_over_configured_cap_returns_400_naming_the_limit()
{
    const int cap = 2048; // within the validator band [1024, 50 MiB]
    using var factory = Fx.WithWebHostBuilder(b =>
        b.ConfigureTestServices(s => s.PostConfigure<CatalogSpecOptions>(o => o.MaxContentBytes = cap)));
    var client = await factory.CreateAuthenticatedClientAsync(OrgAUser);
    var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Spec Team Cap");
    var apiId = await RegisterApiAsync(client, teamId);

    var over = "{\"x\":\"" + new string('a', cap) + "\"}"; // > cap bytes
    var overResp = await client.SendAsync(SpecRequest(HttpMethod.Put, apiId, over));
    Assert.AreEqual(HttpStatusCode.BadRequest, overResp.StatusCode);
    var body = await overResp.Content.ReadAsStringAsync();
    StringAssert.Contains(body, cap.ToString());

    var under = "{\"x\":1}"; // < cap bytes
    var underResp = await client.SendAsync(SpecRequest(HttpMethod.Put, apiId, under));
    Assert.AreEqual(HttpStatusCode.Created, underResp.StatusCode);
}
```

Add `using Kartova.Catalog.Infrastructure;` (for `CatalogSpecOptions`) and `using Microsoft.Extensions.Options;` and `using Microsoft.AspNetCore.TestHost;` (for `ConfigureTestServices`) + `using Microsoft.Extensions.DependencyInjection;` to the file's usings. Remove the now-unused `using Kartova.Catalog.Domain;` only if no other reference remains (it stays — `ApiStyle` is used in `RegisterBody`).

> `CreateAuthenticatedClientAsync` / `SeedTeamInOrganizationAsync` / `TenantIdForEmail` / `WithWebHostBuilder` are on the shared fixture (`KartovaApiFixture` / `KartovaApiFixtureBase`) — same pattern as `Kartova.Organization.IntegrationTests/InvitationFailureWiringTests.cs:61`.

- [ ] **Step 2: Run it to verify it fails**

Run: `cmd //c dotnet test src/Modules/Catalog/Kartova.Catalog.IntegrationTests --filter PUT_over_configured_cap_returns_400_naming_the_limit`
Expected: FAIL — endpoint still uses the hardcoded const (does not honor the overridden 2048), so the "over" body is accepted (201, not 400) OR the message lacks "2048".

- [ ] **Step 3: Drop the byte-cap from the domain**

In `src/Modules/Catalog/Kartova.Catalog.Domain/ApiSpec.cs`:
- Delete line 10: `public const int MaxContentBytes = 5 * 1024 * 1024;   // 5 MiB hard cap`.
- In `Validate` (lines 53-61) delete the byte-count check:

```csharp
        if (System.Text.Encoding.UTF8.GetByteCount(content) > MaxContentBytes)
            throw new ArgumentException($"API spec content must be <= {MaxContentBytes} bytes.", nameof(content));
```

So `Validate` keeps only the non-empty and media-type checks. Update the class-doc (lines 5-7) to note: "Size is bounded at the upload endpoint (configurable `Catalog:ApiSpec:MaxContentBytes`), not here."

- [ ] **Step 4: Read the configured cap at the endpoint**

In `src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogEndpointDelegates.cs`:

Add to the `UpsertApiSpecAsync` signature (after `IAuditWriter audit,`):

```csharp
        Microsoft.Extensions.Options.IOptions<CatalogSpecOptions> specOptions,
```

Replace the two `ApiSpec.MaxContentBytes` uses (`:672`, `:679`) and the `SpecTooLarge()` calls with the configured value:

```csharp
        var maxBytes = specOptions.Value.MaxContentBytes;
        // Declared-length pre-check (cheap early-out); the streamed read below is
        // capped independently so a chunked-transfer request cannot bypass the limit.
        if (request.ContentLength is { } declaredLength && declaredLength > maxBytes)
            return SpecTooLarge(maxBytes);

        var gate = await LoadAndAuthorizeApiAsync(id, db, auth, caller, ct);
        if (gate is not null) return gate;

        using var reader = new StreamReader(request.Body, Encoding.UTF8);
        var content = await ReadCappedAsync(reader, maxBytes, ct);
        if (content is null) return SpecTooLarge(maxBytes);
```

Change `SpecTooLarge` (`:926-931`) to take the limit:

```csharp
    private static IResult SpecTooLarge(int maxBytes)
        => Results.Problem(
            type: ProblemTypes.SpecTooLarge,
            title: "Spec too large",
            detail: $"API spec content must not exceed {maxBytes} bytes.",
            statusCode: StatusCodes.Status400BadRequest);
```

Update its doc-comment (`:924-925`) to drop the `ApiSpec.MaxContentBytes` `<see>` reference (say "the configured `Catalog:ApiSpec:MaxContentBytes`").

- [ ] **Step 5: Update the ProblemTypes comment**

In `src/Kartova.SharedKernel.AspNetCore/ProblemTypes.cs:85`, change the trailing comment on `SpecTooLarge` from `... exceeded ApiSpec.MaxContentBytes.` to `... exceeded the configured Catalog:ApiSpec:MaxContentBytes.`

- [ ] **Step 6: Remove the domain cap tests**

In `src/Modules/Catalog/Kartova.Catalog.Tests/ApiSpecTests.cs`, delete the three tests that assert the byte cap (the behavior no longer lives in the domain):
- `Create_rejects_oversized_content` (:28-34)
- `Create_rejects_content_over_cap_by_utf8_byte_count` (:36-44)
- `Create_accepts_content_exactly_at_cap` (:79-85)

Keep all other tests (empty, media-type, replace, createdBy, `IsAllowed`). No remaining reference to `ApiSpec.MaxContentBytes` should exist in this file.

- [ ] **Step 7: Verify no stale references remain**

Run: `cmd //c dotnet build Kartova.slnx -p:TreatWarningsAsErrors=true`
Expected: build succeeds, 0 warnings. (A leftover `ApiSpec.MaxContentBytes` reference anywhere would fail compile — grep to be sure: `grep -rn "MaxContentBytes" src/ | grep -v CatalogSpecOptions` should return nothing.)

- [ ] **Step 8: Run domain + integration tests to verify pass**

Run: `cmd //c dotnet test src/Modules/Catalog/Kartova.Catalog.Tests --filter ApiSpecTests`
Run: `cmd //c dotnet test src/Modules/Catalog/Kartova.Catalog.IntegrationTests --filter ApiSpecTests`
Expected: both PASS (domain: 8 tests remain; integration: happy/415/403/404/401/RLS + the new configured-boundary test).

- [ ] **Step 9: Commit**

```bash
git add src/Modules/Catalog/Kartova.Catalog.Domain/ApiSpec.cs \
        src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogEndpointDelegates.cs \
        src/Kartova.SharedKernel.AspNetCore/ProblemTypes.cs \
        src/Modules/Catalog/Kartova.Catalog.Tests/ApiSpecTests.cs \
        src/Modules/Catalog/Kartova.Catalog.IntegrationTests/ApiSpecTests.cs
git commit -m "feat(catalog): enforce configurable spec cap at upload endpoint; drop domain byte-cap const"
```

> **Gate 6 (mutation) applies to this task** — run `/misc:mutation-sentinel` on `ApiSpec.cs`, `CatalogEndpointDelegates.UpsertApiSpecAsync`, `CatalogSpecOptionsValidator.cs` at slice close; target ≥80%.

---

### Task 3: Frontend data layer — `useApiSpec` + `useUpsertApiSpec`

**Files:**
- Modify: `web/src/features/catalog/api/apis.ts`
- Test: `web/src/features/catalog/api/__tests__/apis.test.tsx`

**Interfaces:**
- Consumes: `API_BASE_URL`, `apiClient` (`./client`); `throwWithStatus` (`@/shared/api/openapi-fetch-helpers`); `useAuth` (`react-oidc-context`); `apiKeys` (existing in `apis.ts`).
- Produces:
  - `apiKeys.spec(id: string)` → query key.
  - `useApiSpec(id: string, hasSpec: boolean)` → `{ data?: { content: string; mediaType: string } | null; isLoading; isError }` (TanStack query; `enabled: hasSpec`).
  - `useUpsertApiSpec(id: string)` → mutation; `mutateAsync({ content: string, mediaType: string })`.

- [ ] **Step 1: Write failing tests**

Add to `web/src/features/catalog/api/__tests__/apis.test.tsx` (follow the existing render-hook + fetch-spy pattern already used in this file for `useApisList`/`useRegisterApi`):

```tsx
import { useApiSpec, useUpsertApiSpec } from "@/features/catalog/api/apis";

// ... inside the existing describe, using the file's existing QueryClient wrapper + auth mock ...

it("useApiSpec GETs raw spec and returns content + mediaType", async () => {
  const fetchSpy = vi.spyOn(globalThis, "fetch").mockResolvedValue(
    new Response("channels: {}", { status: 200, headers: { "Content-Type": "application/yaml" } }),
  );
  const { result } = renderHook(() => useApiSpec("api-1", true), { wrapper });
  await waitFor(() => expect(result.current.isLoading).toBe(false));
  expect(fetchSpy).toHaveBeenCalledWith(
    expect.stringContaining("/api/v1/catalog/apis/api-1/spec"),
    expect.objectContaining({ headers: expect.objectContaining({ Authorization: "Bearer test-token" }) }),
  );
  expect(result.current.data).toEqual({ content: "channels: {}", mediaType: "application/yaml" });
});

it("useApiSpec returns null on 404 (no spec yet)", async () => {
  vi.spyOn(globalThis, "fetch").mockResolvedValue(new Response("", { status: 404 }));
  const { result } = renderHook(() => useApiSpec("api-1", true), { wrapper });
  await waitFor(() => expect(result.current.isLoading).toBe(false));
  expect(result.current.data).toBeNull();
});

it("useApiSpec does not fetch when hasSpec is false", () => {
  const fetchSpy = vi.spyOn(globalThis, "fetch");
  renderHook(() => useApiSpec("api-1", false), { wrapper });
  expect(fetchSpy).not.toHaveBeenCalled();
});

it("useUpsertApiSpec PUTs raw body with chosen Content-Type", async () => {
  const fetchSpy = vi.spyOn(globalThis, "fetch").mockResolvedValue(new Response("", { status: 201 }));
  const { result } = renderHook(() => useUpsertApiSpec("api-1"), { wrapper });
  await result.current.mutateAsync({ content: "{}", mediaType: "application/json" });
  expect(fetchSpy).toHaveBeenCalledWith(
    expect.stringContaining("/api/v1/catalog/apis/api-1/spec"),
    expect.objectContaining({
      method: "PUT",
      headers: expect.objectContaining({ "Content-Type": "application/json", Authorization: "Bearer test-token" }),
      body: "{}",
    }),
  );
});
```

> Reuse whatever `wrapper` (QueryClientProvider) and `useAuth` mock the existing tests in this file already define; the file mocks `react-oidc-context` so `auth.user.access_token === "test-token"`. If the existing auth mock uses a different token string, match it in the assertions.

- [ ] **Step 2: Run tests to verify they fail**

Run: `cd web && npx vitest run src/features/catalog/api/__tests__/apis.test.tsx`
Expected: FAIL — `useApiSpec` / `useUpsertApiSpec` are not exported.

- [ ] **Step 3: Implement the data layer**

In `web/src/features/catalog/api/apis.ts`:
- Add imports: `import { useAuth } from "react-oidc-context";` and `import { API_BASE_URL } from "./client";` (extend the existing `./client` import).
- Extend `apiKeys` with: `spec: (id: string) => [...apiKeys.all, "spec", id] as const,`.
- Add the hooks:

```ts
export function useApiSpec(id: string, hasSpec: boolean) {
  const auth = useAuth();
  return useQuery({
    queryKey: apiKeys.spec(id),
    enabled: hasSpec && !!id,
    queryFn: async () => {
      const token = auth.user?.access_token;
      const res = await fetch(`${API_BASE_URL}/api/v1/catalog/apis/${id}/spec`, {
        headers: token ? { Authorization: `Bearer ${token}` } : {},
      });
      if (res.status === 404) return null;
      if (!res.ok) throw new Error(`Failed to load spec: ${res.status}`);
      const content = await res.text();
      const mediaType = res.headers.get("Content-Type")?.split(";")[0]?.trim() ?? "application/json";
      return { content, mediaType };
    },
  });
}

export function useUpsertApiSpec(id: string) {
  const auth = useAuth();
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async ({ content, mediaType }: { content: string; mediaType: string }) => {
      const token = auth.user?.access_token;
      const res = await fetch(`${API_BASE_URL}/api/v1/catalog/apis/${id}/spec`, {
        method: "PUT",
        headers: {
          "Content-Type": mediaType,
          ...(token ? { Authorization: `Bearer ${token}` } : {}),
        },
        body: content,
      });
      if (!res.ok) {
        let body: Record<string, unknown> = {};
        try { body = (await res.json()) as Record<string, unknown>; } catch { /* non-JSON */ }
        throwWithStatus({ ...body, message: typeof body.detail === "string" ? body.detail : `Upload failed: ${res.status}` }, res);
      }
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: apiKeys.detail(id) });
      qc.invalidateQueries({ queryKey: apiKeys.spec(id) });
      qc.invalidateQueries({ queryKey: apiKeys.all });
    },
  });
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `cd web && npx vitest run src/features/catalog/api/__tests__/apis.test.tsx`
Expected: PASS (existing + 4 new).

- [ ] **Step 5: Verify generated types carry `hasSpec`**

Run: `cd web && grep -c "hasSpec" src/generated/openapi.ts 2>/dev/null || echo "MISSING"`
- If ≥1: proceed.
- If `MISSING` or the file is absent: regenerate the client against the running API (`npm run predev` path per the codegen note) and commit the refreshed `web/openapi-snapshot.json`. `web/openapi-snapshot.json` already contains `hasSpec` (line ~3107), so the typed `ApiResponse` will include it after regen.

- [ ] **Step 6: Commit**

```bash
git add web/src/features/catalog/api/apis.ts web/src/features/catalog/api/__tests__/apis.test.tsx
git commit -m "feat(web): useApiSpec + useUpsertApiSpec raw-fetch data layer for API spec"
```

---

### Task 4: `AttachApiSpecDialog` component

**Files:**
- Create: `web/src/features/catalog/components/AttachApiSpecDialog.tsx`
- Test: `web/src/features/catalog/components/__tests__/AttachApiSpecDialog.test.tsx`

**Interfaces:**
- Consumes: `useUpsertApiSpec` (Task 3); `ModalOverlay, Modal, Dialog` (`@/components/application/modals/modal`); `TextArea` (`@/components/base/textarea/textarea`); `Button`; `toast` (`sonner`); `applyProblemDetailsToForm`/`ProblemDetails` (`@/shared/forms/problemDetails`).
- Produces: `AttachApiSpecDialog({ apiId: string; open: boolean; onOpenChange: (open: boolean) => void; hasExistingSpec: boolean })`. Exports helper `inferMediaType(fileName: string | undefined, content: string): "application/json" | "application/yaml"`.

- [ ] **Step 1: Write failing tests**

Create `web/src/features/catalog/components/__tests__/AttachApiSpecDialog.test.tsx`:

```tsx
import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { AttachApiSpecDialog, inferMediaType } from "../AttachApiSpecDialog";

const mutateAsync = vi.fn();
vi.mock("@/features/catalog/api/apis", () => ({
  useUpsertApiSpec: () => ({ mutateAsync, isPending: false }),
}));
vi.mock("sonner", () => ({ toast: { success: vi.fn(), error: vi.fn() } }));

function renderDialog(props: Partial<Parameters<typeof AttachApiSpecDialog>[0]> = {}) {
  const qc = new QueryClient();
  return render(
    <QueryClientProvider client={qc}>
      <AttachApiSpecDialog apiId="api-1" open onOpenChange={() => {}} hasExistingSpec={false} {...props} />
    </QueryClientProvider>,
  );
}

beforeEach(() => { mutateAsync.mockReset().mockResolvedValue(undefined); });

describe("inferMediaType", () => {
  it("maps extensions and sniffs content", () => {
    expect(inferMediaType("openapi.yaml", "")).toBe("application/yaml");
    expect(inferMediaType("openapi.yml", "")).toBe("application/yaml");
    expect(inferMediaType("openapi.json", "")).toBe("application/json");
    expect(inferMediaType(undefined, "{\"a\":1}")).toBe("application/json");
    expect(inferMediaType(undefined, "channels: {}")).toBe("application/yaml");
  });
});

describe("AttachApiSpecDialog", () => {
  it("submits pasted content with the selected media type", async () => {
    renderDialog();
    await userEvent.type(screen.getByLabelText(/paste/i), "{}");
    await userEvent.click(screen.getByRole("button", { name: /attach spec/i }));
    await waitFor(() => expect(mutateAsync).toHaveBeenCalledWith({ content: "{}", mediaType: "application/json" }));
  });

  it("rejects empty content without calling the mutation", async () => {
    renderDialog();
    await userEvent.click(screen.getByRole("button", { name: /attach spec/i }));
    expect(mutateAsync).not.toHaveBeenCalled();
    expect(screen.getByText(/must not be empty/i)).toBeInTheDocument();
  });

  it("surfaces a 415 problem inline", async () => {
    mutateAsync.mockRejectedValue({ status: 415, detail: "Only JSON or YAML specs are supported." });
    renderDialog();
    await userEvent.type(screen.getByLabelText(/paste/i), "nope");
    await userEvent.click(screen.getByRole("button", { name: /attach spec/i }));
    await waitFor(() => expect(screen.getByText(/only json or yaml/i)).toBeInTheDocument());
  });

  it("titles Replace when a spec exists", () => {
    renderDialog({ hasExistingSpec: true });
    expect(screen.getByRole("heading", { name: /replace spec/i })).toBeInTheDocument();
  });
});
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `cd web && npx vitest run src/features/catalog/components/__tests__/AttachApiSpecDialog.test.tsx`
Expected: FAIL — component does not exist.

- [ ] **Step 3: Implement the component**

Create `web/src/features/catalog/components/AttachApiSpecDialog.tsx`:

```tsx
import { useEffect, useState } from "react";
import { toast } from "sonner";
import { ModalOverlay, Modal, Dialog } from "@/components/application/modals/modal";
import { TextArea } from "@/components/base/textarea/textarea";
import { Button } from "@/components/base/buttons/button";
import { useUpsertApiSpec } from "@/features/catalog/api/apis";
import type { ProblemDetails } from "@/shared/forms/problemDetails";

type MediaType = "application/json" | "application/yaml";

export function inferMediaType(fileName: string | undefined, content: string): MediaType {
  const ext = fileName?.toLowerCase().split(".").pop();
  if (ext === "yaml" || ext === "yml") return "application/yaml";
  if (ext === "json") return "application/json";
  return content.trimStart().startsWith("{") ? "application/json" : "application/yaml";
}

interface Props {
  apiId: string;
  open: boolean;
  onOpenChange: (open: boolean) => void;
  hasExistingSpec: boolean;
}

export function AttachApiSpecDialog({ apiId, open, onOpenChange, hasExistingSpec }: Props) {
  const mutation = useUpsertApiSpec(apiId);
  const [content, setContent] = useState("");
  const [mediaType, setMediaType] = useState<MediaType>("application/json");
  const [fileName, setFileName] = useState<string | undefined>(undefined);
  const [error, setError] = useState("");

  useEffect(() => {
    if (!open) {
      // eslint-disable-next-line react-hooks/set-state-in-effect
      setContent(""); setMediaType("application/json"); setFileName(undefined); setError("");
    }
  }, [open]);

  const onFile = async (file: File | undefined) => {
    if (!file) return;
    const text = await file.text();
    setFileName(file.name);
    setContent(text);
    setMediaType(inferMediaType(file.name, text));
  };

  const onSubmit = async () => {
    if (content.trim().length === 0) { setError("Spec content must not be empty."); return; }
    setError("");
    try {
      await mutation.mutateAsync({ content, mediaType });
      toast.success(hasExistingSpec ? "Spec replaced" : "Spec attached");
      onOpenChange(false);
    } catch (err) {
      const problem = err as ProblemDetails & { message?: string };
      setError(problem.detail ?? problem.message ?? "Failed to save spec.");
    }
  };

  const title = hasExistingSpec ? "Replace spec" : "Attach spec";

  return (
    <ModalOverlay isOpen={open} onOpenChange={onOpenChange} isDismissable={!mutation.isPending}>
      <Modal className="max-w-[640px]">
        <Dialog aria-label={title} className="bg-primary rounded-xl shadow-xl p-6 outline-none">
          <div className="w-full space-y-5">
            <div className="space-y-1">
              <h2 className="text-lg font-semibold text-primary">{title}</h2>
              <p className="text-sm text-tertiary">Upload a file or paste an OpenAPI / AsyncAPI document (JSON or YAML).</p>
            </div>

            <div className="flex flex-col gap-1">
              <label htmlFor="spec-file" className="text-sm font-medium text-secondary">File</label>
              <input id="spec-file" type="file" accept=".json,.yaml,.yml" data-testid="spec-file-input"
                onChange={(e) => void onFile(e.target.files?.[0])} disabled={mutation.isPending}
                className="text-sm text-secondary" />
            </div>

            <TextArea label="…or paste" rows={10} value={content}
              onChange={(v) => { setContent(v); setMediaType(inferMediaType(fileName, v)); }}
              isDisabled={mutation.isPending} />

            <div className="flex flex-col gap-1">
              <label htmlFor="spec-media-type" className="text-sm font-medium text-secondary">Format</label>
              <select id="spec-media-type" data-testid="spec-media-type-select"
                className="rounded-md border border-secondary px-3 py-2 text-sm bg-primary text-primary"
                value={mediaType} onChange={(e) => setMediaType(e.target.value as MediaType)} disabled={mutation.isPending}>
                <option value="application/json">JSON</option>
                <option value="application/yaml">YAML</option>
              </select>
            </div>

            {error && <p className="text-xs text-error-primary" role="alert">{error}</p>}

            <div className="flex justify-end gap-2 pt-2">
              <Button type="button" color="secondary" size="sm" onClick={() => onOpenChange(false)}>Cancel</Button>
              <Button type="button" color="primary" size="sm" isLoading={mutation.isPending} onClick={() => void onSubmit()}>
                {title}
              </Button>
            </div>
          </div>
        </Dialog>
      </Modal>
    </ModalOverlay>
  );
}
```

> Verify the `TextArea` prop contract in `@/components/base/textarea/textarea` (some Untitled UI wrappers pass `onChange(value)`, others `onChange(event)`, and the label prop may render as `aria-label`). Adjust `getByLabelText(/paste/i)` / the `onChange` signature to match the real component (check `RegisterApiDialog.tsx:100-105` which already uses `TextArea` with `{...field}` from RHF — mirror that binding).

- [ ] **Step 4: Run tests to verify they pass**

Run: `cd web && npx vitest run src/features/catalog/components/__tests__/AttachApiSpecDialog.test.tsx`
Expected: PASS (5 tests). Fix label/onChange mismatches surfaced here against the real `TextArea`.

- [ ] **Step 5: Commit**

```bash
git add web/src/features/catalog/components/AttachApiSpecDialog.tsx \
        web/src/features/catalog/components/__tests__/AttachApiSpecDialog.test.tsx
git commit -m "feat(web): AttachApiSpecDialog (file + paste + media-type inference)"
```

---

### Task 5: `ApiSpecSection` + wire into `ApiDetailPage`

**Files:**
- Create: `web/src/features/catalog/components/ApiSpecSection.tsx`
- Modify: `web/src/features/catalog/pages/ApiDetailPage.tsx`
- Test: `web/src/features/catalog/components/__tests__/ApiSpecSection.test.tsx`

**Interfaces:**
- Consumes: `useApiSpec` (Task 3); `AttachApiSpecDialog` (Task 4); `usePermissions` (`@/shared/auth/usePermissions`) → `.hasPermission(...)`; `CatalogApisRegister` (`@/shared/auth/permissions`); `Button`, `Badge`, `ApiResponse` type.
- Produces: `ApiSpecSection({ api: ApiResponse })`.

- [ ] **Step 1: Write failing tests**

Create `web/src/features/catalog/components/__tests__/ApiSpecSection.test.tsx`:

```tsx
import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { ApiSpecSection } from "../ApiSpecSection";
import type { ApiResponse } from "@/features/catalog/api/apis";

let specData: { content: string; mediaType: string } | null = null;
vi.mock("@/features/catalog/api/apis", () => ({
  useApiSpec: () => ({ data: specData, isLoading: false, isError: false }),
}));
let perms = new Set<string>(["catalog.apis.register"]);
vi.mock("@/shared/auth/usePermissions", () => ({
  usePermissions: () => ({ hasPermission: (p: string) => perms.has(p) }),
}));

const api = (hasSpec: boolean): ApiResponse =>
  ({ id: "api-1", displayName: "Orders", style: "rest", version: "v1", teamId: "t1", hasSpec } as unknown as ApiResponse);

describe("ApiSpecSection", () => {
  it("shows empty state + Attach when no spec and permitted", () => {
    specData = null; perms = new Set(["catalog.apis.register"]);
    render(<ApiSpecSection api={api(false)} />);
    expect(screen.getByText(/no spec/i)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /attach spec/i })).toBeInTheDocument();
  });

  it("renders spec content + Replace when a spec exists", () => {
    specData = { content: "channels: {}", mediaType: "application/yaml" }; perms = new Set(["catalog.apis.register"]);
    render(<ApiSpecSection api={api(true)} />);
    expect(screen.getByText("channels: {}")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /replace/i })).toBeInTheDocument();
  });

  it("hides the mutate button without permission", () => {
    specData = null; perms = new Set();
    render(<ApiSpecSection api={api(false)} />);
    expect(screen.queryByRole("button", { name: /attach spec/i })).not.toBeInTheDocument();
  });
});
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `cd web && npx vitest run src/features/catalog/components/__tests__/ApiSpecSection.test.tsx`
Expected: FAIL — component does not exist.

- [ ] **Step 3: Implement `ApiSpecSection`**

Create `web/src/features/catalog/components/ApiSpecSection.tsx`:

```tsx
import { useState } from "react";
import { Badge } from "@/components/base/badges/badges";
import { Button } from "@/components/base/buttons/button";
import { useApiSpec } from "@/features/catalog/api/apis";
import type { ApiResponse } from "@/features/catalog/api/apis";
import { AttachApiSpecDialog } from "./AttachApiSpecDialog";
import { usePermissions } from "@/shared/auth/usePermissions";
import { CatalogApisRegister } from "@/shared/auth/permissions";

export function ApiSpecSection({ api }: { api: ApiResponse }) {
  const spec = useApiSpec(api.id, api.hasSpec);
  const [dialogOpen, setDialogOpen] = useState(false);
  const canWrite = usePermissions().hasPermission(CatalogApisRegister);

  const formatLabel = (m: string) => (m.includes("yaml") ? "YAML" : "JSON");

  return (
    <section className="space-y-3">
      <div className="flex items-center justify-between gap-3">
        <h3 className="text-sm font-medium text-tertiary">Spec document</h3>
        {canWrite && (
          <Button type="button" color="secondary" size="sm" onClick={() => setDialogOpen(true)}>
            {api.hasSpec ? "Replace" : "Attach spec"}
          </Button>
        )}
      </div>

      {api.hasSpec ? (
        <div className="space-y-2">
          {spec.data && (
            <>
              <div className="flex items-center gap-2">
                <Badge type="pill-color" color="gray" size="sm">{formatLabel(spec.data.mediaType)}</Badge>
                <CopyButton text={spec.data.content} />
              </div>
              <pre className="max-h-[480px] overflow-auto rounded-md border border-secondary bg-secondary/30 p-3 font-mono text-xs text-primary whitespace-pre-wrap break-all">
                {spec.data.content}
              </pre>
            </>
          )}
          {spec.isLoading && <p className="text-sm text-tertiary">Loading spec…</p>}
        </div>
      ) : (
        <p className="text-sm text-tertiary italic">No spec attached.</p>
      )}

      <AttachApiSpecDialog apiId={api.id} open={dialogOpen} onOpenChange={setDialogOpen} hasExistingSpec={api.hasSpec} />
    </section>
  );
}

function CopyButton({ text }: { text: string }) {
  const [copied, setCopied] = useState(false);
  return (
    <Button type="button" color="tertiary" size="sm"
      onClick={() => { void navigator.clipboard.writeText(text); setCopied(true); setTimeout(() => setCopied(false), 1500); }}>
      {copied ? "Copied" : "Copy"}
    </Button>
  );
}
```

> Confirm `Button` supports `color="tertiary"` in this codebase; if not, use `"secondary"`. Confirm `CatalogApisRegister` is exported from `@/shared/auth/permissions` (it is — `permissions.ts:11`).

- [ ] **Step 4: Wire into `ApiDetailPage`**

In `web/src/features/catalog/pages/ApiDetailPage.tsx`:
- Add import: `import { ApiSpecSection } from "@/features/catalog/components/ApiSpecSection";`
- Between the metadata `<section>` (ends line 91) and the following `<hr />` + `RelationshipsSection` (lines 93-101), insert:

```tsx
        <hr className="border-secondary" />

        <ApiSpecSection api={api} />
```

(Keep the existing "Spec" field showing `specUrl` in the grid — it stays as external provenance, distinct from the stored spec.)

- [ ] **Step 5: Run tests to verify they pass**

Run: `cd web && npx vitest run src/features/catalog/components/__tests__/ApiSpecSection.test.tsx src/features/catalog/pages/__tests__/ApiDetailPage.test.tsx`
Expected: PASS. If `ApiDetailPage.test.tsx` breaks because the mocked `useApi` fixture lacks `hasSpec`, add `hasSpec: false` to its fixture object.

- [ ] **Step 6: Commit**

```bash
git add web/src/features/catalog/components/ApiSpecSection.tsx \
        web/src/features/catalog/components/__tests__/ApiSpecSection.test.tsx \
        web/src/features/catalog/pages/ApiDetailPage.tsx \
        web/src/features/catalog/pages/__tests__/ApiDetailPage.test.tsx
git commit -m "feat(web): ApiSpecSection on API detail page (view + attach/replace + copy)"
```

---

### Task 6: `Spec` indicator column on the Apis list + registry note

**Files:**
- Modify: `web/src/features/catalog/components/ApisTable.tsx`
- Test: `web/src/features/catalog/components/__tests__/ApisTable.test.tsx`
- Modify: `docs/design/list-filter-registry.md:27` (APIs row)

**Interfaces:**
- Consumes: `ApiResponse.hasSpec`. No new exports.

- [ ] **Step 1: Write the failing test**

Add to `web/src/features/catalog/components/__tests__/ApisTable.test.tsx` (mirror the existing render helper in that file):

```tsx
it("renders the Spec column: check when hasSpec, dash otherwise", () => {
  renderTable({
    items: [
      { id: "a1", displayName: "Has", style: "rest", version: "v1", teamId: "t1", hasSpec: true, createdAt: "2026-07-07T00:00:00Z", createdBy: null },
      { id: "a2", displayName: "None", style: "rest", version: "v1", teamId: "t1", hasSpec: false, createdAt: "2026-07-07T00:00:00Z", createdBy: null },
    ] as unknown as ApiResponse[],
  });
  expect(screen.getByRole("columnheader", { name: /spec/i })).toBeInTheDocument();
  expect(screen.getByTestId("api-hasspec-a1")).toBeInTheDocument();   // check badge
  expect(screen.getByTestId("api-hasspec-a2")).toHaveTextContent("—"); // dash
});
```

> Match `renderTable`'s existing signature in this file (it already builds a `CursorListResult<ApiResponse>` with `items`, `isLoading`, pager fns). Reuse it; only add the two items.

- [ ] **Step 2: Run it to verify it fails**

Run: `cd web && npx vitest run src/features/catalog/components/__tests__/ApisTable.test.tsx`
Expected: FAIL — no `columnheader` named "Spec".

- [ ] **Step 3: Add the column**

In `web/src/features/catalog/components/ApisTable.tsx`:
- Loading skeleton header (lines 26-33): add `<Table.Head id="hasSpec">Spec</Table.Head>` after the `createdAt` head, and change `<TableSkeleton rows={5} cells={6} />` → `cells={7}`.
- Real header (lines 62-69): add `<Table.Head id="hasSpec">Spec</Table.Head>` after the `createdAt` `SortableHead` (plain `Table.Head`, **not** `SortableHead` — presence is not sortable).
- Body row (after the `createdAt` cell, ~line 94): add

```tsx
              <Table.Cell data-testid={`api-hasspec-${api.id}`} className="text-sm">
                {api.hasSpec
                  ? <Badge type="pill-color" color="success" size="sm">Spec</Badge>
                  : <span className="text-tertiary">—</span>}
              </Table.Cell>
```

`Badge` is already imported. Confirm `color="success"` is a valid Badge color in this codebase; if not, use `color="gray"`.

- [ ] **Step 4: Run tests to verify they pass**

Run: `cd web && npx vitest run src/features/catalog/components/__tests__/ApisTable.test.tsx`
Expected: PASS.

- [ ] **Step 5: Record the ADR-0107 field-addition decision**

In `docs/design/list-filter-registry.md`, append to the APIs row's notes (line 27): a sentence — `hasSpec (2026-07-07, field-addition trigger): column ✓ (Spec indicator), sort ✗ (presence not a meaningful sort key), filter deferred (has-spec/no-spec filter is a follow-up).`

- [ ] **Step 6: Commit**

```bash
git add web/src/features/catalog/components/ApisTable.tsx \
        web/src/features/catalog/components/__tests__/ApisTable.test.tsx \
        docs/design/list-filter-registry.md
git commit -m "feat(web): Spec indicator column on Apis list + registry field-addition note"
```

---

### Task 7: Verification pass (build, suites, browser, ledger)

**Files:**
- Create: `docs/superpowers/verification/2026-07-07-catalog-api-spec-ui/dod.md` (from `docs/superpowers/templates/dod-ledger-template.md`)
- Create: `docs/superpowers/verification/2026-07-07-catalog-api-spec-ui/gate-findings.yaml` (from template)

- [ ] **Step 1: Scaffold the DoD ledger**

Copy `docs/superpowers/templates/dod-ledger-template.md` → `docs/superpowers/verification/2026-07-07-catalog-api-spec-ui/dod.md` and `docs/superpowers/templates/gate-findings-template.yaml` → the sibling `gate-findings.yaml`. Fill the slice header (topic, branch `feat/catalog-api-spec-ui`, spec + plan paths).

- [ ] **Step 2: Backend build (gate 1) + full backend suite (gate 3)**

Run: `cmd //c dotnet build Kartova.slnx -p:TreatWarningsAsErrors=true`
Run: `cmd //c dotnet test Kartova.slnx`
Expected: 0 warnings; all green. (If an integration assembly flakes on a Docker named-pipe timeout, re-run that assembly in isolation before calling it red — known flake.) Record in ledger.

- [ ] **Step 3: Frontend suite (gate 3) + type gate**

Run: `cd web && npm run build` (this is the `tsc -b` binding type gate) and `npx vitest run`
Expected: type-check clean; all tests green. Record.

- [ ] **Step 4: Mutation (gate 6 — BLOCKING this slice)**

Run `/misc:mutation-sentinel` scoped to `ApiSpec.cs`, `CatalogEndpointDelegates.cs` (`UpsertApiSpecAsync`), `CatalogSpecOptionsValidator.cs`; then `/misc:test-generator` on survivors. Target ≥80%. Document survivors in the ledger.

- [ ] **Step 5: Browser verify (ADR-0084)**

Cold-start the dev server (not HMR). Login `admin@orga` / `dev_password_12`, navigate in-SPA (deep-link cold-load bounces — bug #47). DevSeed has no APIs → register one via the Register API dialog first. Then: open its detail page → attach a `.yaml` spec → confirm it renders + copy works → replace with a `.json` spec → back to `/catalog/apis`, confirm the Spec column shows the check for that API. Confirm no blank-page on dialog open (react-aria rowheader) and a clean console. Save a screenshot into the verification folder.

- [ ] **Step 6: Simplify + reviews (gates 5, 7, 8, 9)**

Run `/simplify` on the branch diff; then `/superpowers:requesting-code-review`, `/pr-review-toolkit:review-pr`, `/deep-review` — each for real (no folding), against the full branch diff with spec+plan as context. Address Blocking/Should-fix; triage nits. Log each finding in `gate-findings.yaml`.

- [ ] **Step 7: Pre-push CI mirror + terminal re-verify**

Run: `scripts/ci-local.sh` (Release build+test, web image, helm/stryker as CI runs them). After any gate-5–9 fixes, re-run `cmd //c dotnet build Kartova.slnx -p:TreatWarningsAsErrors=true` + `cmd //c dotnet test Kartova.slnx` + `cd web && npm run build && npx vitest run` on the final commit. Update the ledger summary table to all-green (or waivers with reasons).

- [ ] **Step 8: Update CHECKLIST + finish the branch**

Update `docs/product/CHECKLIST.md` E-02.F-03.S-02 line to note the spec-UI + configurable-cap follow-up shipped. Then use `superpowers:finishing-a-development-branch` to open the PR.

```bash
git add docs/superpowers/verification/2026-07-07-catalog-api-spec-ui/ docs/product/CHECKLIST.md
git commit -m "docs(catalog): DoD ledger + checklist for API spec UI slice"
```

---

## Self-Review

**Spec coverage:**
- Attach/view all styles → Tasks 4, 5. ✓
- File + paste + media-type inference → Task 4 (`inferMediaType` + file input + textarea). ✓
- Raw `<pre>` view + copy → Task 5. ✓
- Raw-fetch GET/PUT, 404→null, no client size mirror → Task 3. ✓
- No client membership gate; perm-gated button → Task 5. ✓
- Spec URL vs stored spec kept separate → Task 5 Step 4 (explicit). ✓
- Spec list column, no sort, filter deferred + registry → Task 6. ✓
- Configurable cap (options + validator + band + appsettings) → Task 1. ✓
- Endpoint reads configured value; domain drops cap → Task 2. ✓
- ADR-0112 amendment → *not a task*; done at slice close alongside the ledger (design §5). **Added reminder:** amend `ADR-0112` text in Task 7 Step 8's docs commit.
- Gate-5 real-seam configured-boundary test → Task 2 Step 1. ✓
- Gate 6 mutation blocking → Task 2 note + Task 7 Step 4. ✓
- Browser verify → Task 7 Step 5. ✓

**Placeholder scan:** No TBD/TODO; every code step shows code; commands have expected output. The three "confirm the real component prop/color" notes (TextArea onChange, Button tertiary, Badge success) are verification instructions with a named fallback, not placeholders. ✓

**Type consistency:** `useApiSpec(id, hasSpec)` and `useUpsertApiSpec(id).mutateAsync({content, mediaType})` used identically in Tasks 3/4/5. `inferMediaType(fileName, content)` signature consistent Task 4 ↔ tests. `apiKeys.spec(id)` defined Task 3, used in invalidation. `ApiResponse.hasSpec` (generated) used in Tasks 5/6, guarded by Task 3 Step 5. ✓

**Fix applied inline:** ADR-0112 amendment folded into Task 7 Step 8 (was unowned).
