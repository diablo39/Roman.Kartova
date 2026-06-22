# Catalog List-Filter Surface Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a `displayName` text search to the Catalog **Services** and **Applications** lists (mirroring Teams), fold the Applications `includeDecommissioned` toggle into a new `<FilterBar>` boolean control, make `<FilterBar>` a collapsible disclosure panel for all consumers, and standardize both lists' default sort to `displayName asc`.

**Architecture:** Backend mirrors the proven `ListTeamsHandler` ILIKE-before-paging + cursor `f`-map pattern (ADR-0095). A shared `EscapeLike` helper replaces three inline copies. Frontend extends the existing `useListUrlState` / `useListFilters` / `<FilterBar>` stack (ADR-0107) with a submit-driven boolean control and a collapsible disclosure shell. The filter key `displayNameContains` is one identifier across URL param, API query param, and `f`-map.

**Tech Stack:** .NET 10 / ASP.NET Core minimal APIs · EF Core (`EF.Functions.ILike`) · Wolverine handlers · React + TypeScript · react-aria-components (Untitled UI) · TanStack Query · MSTest (real-seam integration via Testcontainers) · Vitest.

**Spec:** `docs/superpowers/specs/2026-06-22-list-filter-surface-catalog-design.md`. **Branch:** `feat/list-filter-surface-catalog` (already created).

## Global Constraints

- **Filter key = wire name everywhere:** `displayNameContains` is the URL query param, the API query param, and the cursor `f`-map key. No per-screen translation.
- **Blank ⇒ filter absent:** whitespace/empty `displayNameContains` produces no `WHERE`, no `f`-key (the unfiltered cursor must stay byte-identical to today's). Endpoint trims and maps blank → `null`.
- **Filter applied BEFORE pagination** so a hidden row never becomes a cursor boundary.
- **Boolean filter is submit-driven:** a checkbox edits a draft; the URL + query commit only on Enter / Search — same as text (intentional behavior change from today's immediate toggle).
- **Default sort `displayName asc`** on Services + Applications, on BOTH the screen (`useListUrlState`) and the endpoint fallback.
- **Mutation gate (6) is SKIPPED this slice** (user decision, recorded in spec §7). All other DoD gates apply. Real-seam integration tests (gate 3) remain mandatory: real Postgres/RLS + real JWT, ≥1 happy + ≥1 negative per list.
- **`TreatWarningsAsErrors=true`** — 0 warnings, 0 errors (gate 1).
- **Windows shell:** run `dotnet` via `cmd //c "dotnet …"` or a PowerShell wrapper in Git Bash. Frontend commands run from the `web/` directory.
- **Dates absolute** (`2026-06-22`), epics/stories `E-XX.F-YY.S-ZZ`.

**Parallelism:** Track A (Tasks 1–3, backend) and Track B (Tasks 5–7, frontend infra) are independent and may run concurrently (e.g. separate worktrees). Task 4 (codegen) depends on Tasks 2–3. Tasks 8–10 (frontend wiring) depend on Task 4 + Tasks 6–7. Tasks 11–12 are finalization.

---

## Track A — Backend

### Task 1: Shared `EscapeLike` helper + repoint Teams

**Files:**
- Create: `src/Kartova.SharedKernel.Postgres/Pagination/LikeEscaping.cs`
- Create (test): `tests/Kartova.SharedKernel.Postgres.IntegrationTests/LikeEscapingTests.cs` (a plain `[TestClass]` — no container base; it tests a pure function)
- Modify: `src/Modules/Organization/Kartova.Organization.Infrastructure/ListTeamsHandler.cs` (delete the private `EscapeLike`, call the shared one)

**Interfaces:**
- Produces: `Kartova.SharedKernel.Postgres.Pagination.LikeEscaping.EscapeLike(string raw) → string` — escapes `\`, `%`, `_` for use inside an `ILIKE` pattern with `ESCAPE '\'`. Consumed by Tasks 2 and 3.

- [ ] **Step 1: Write the failing test**

`tests/Kartova.SharedKernel.Postgres.IntegrationTests/LikeEscapingTests.cs`:
```csharp
using Kartova.SharedKernel.Postgres.Pagination;

namespace Kartova.SharedKernel.Postgres.IntegrationTests;

[TestClass]
public sealed class LikeEscapingTests
{
    [TestMethod]
    public void Plain_text_is_unchanged()
        => Assert.AreEqual("payments", LikeEscaping.EscapeLike("payments"));

    [TestMethod]
    public void Percent_underscore_and_backslash_are_escaped()
    {
        // Backslash MUST be escaped first, or the escapes added for % and _ get re-escaped.
        Assert.AreEqual(@"50\% off", LikeEscaping.EscapeLike("50% off"));
        Assert.AreEqual(@"a\_b", LikeEscaping.EscapeLike("a_b"));
        Assert.AreEqual(@"c\\d", LikeEscaping.EscapeLike(@"c\d"));
    }

    [TestMethod]
    public void Combined_metacharacters_escape_in_backslash_first_order()
        => Assert.AreEqual(@"\\\%\_", LikeEscaping.EscapeLike(@"\%_"));
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cmd //c "dotnet test tests/Kartova.SharedKernel.Postgres.IntegrationTests --filter FullyQualifiedName~LikeEscapingTests"`
Expected: FAIL — `LikeEscaping` does not exist (compile error).

- [ ] **Step 3: Write the helper**

`src/Kartova.SharedKernel.Postgres/Pagination/LikeEscaping.cs`:
```csharp
namespace Kartova.SharedKernel.Postgres.Pagination;

/// <summary>
/// Escapes LIKE/ILIKE metacharacters so user input matches literally under
/// <c>ESCAPE '\'</c>. Shared by every list handler that does a contains filter
/// (Teams, Catalog Services/Applications) — one tested implementation.
/// Backslash is escaped first, so the escapes added for <c>%</c> and <c>_</c>
/// are not themselves re-escaped.
/// </summary>
public static class LikeEscaping
{
    public static string EscapeLike(string raw) =>
        raw.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `cmd //c "dotnet test tests/Kartova.SharedKernel.Postgres.IntegrationTests --filter FullyQualifiedName~LikeEscapingTests"`
Expected: PASS (3 tests).

- [ ] **Step 5: Repoint `ListTeamsHandler`**

In `src/Modules/Organization/Kartova.Organization.Infrastructure/ListTeamsHandler.cs`:
- Change the pattern build to use the shared helper:
```csharp
var pattern = $"%{LikeEscaping.EscapeLike(name)}%";
```
- Delete the private method (the whole block):
```csharp
// Escapes LIKE/ILIKE metacharacters so user input matches literally (ESCAPE '\').
// Backslash first, so the escapes added for % and _ are not re-escaped.
private static string EscapeLike(string raw) =>
    raw.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
```
- The file already has `using Kartova.SharedKernel.Postgres.Pagination;` (it uses `ToCursorPagedAsync`), so `LikeEscaping` resolves with no new using.

- [ ] **Step 6: Run Teams tests to verify no regression**

Run: `cmd //c "dotnet test src/Modules/Organization/Kartova.Organization.IntegrationTests --filter FullyQualifiedName~ListTeamsTests"`
Expected: PASS (unchanged behavior).

- [ ] **Step 7: Commit**

```bash
git add src/Kartova.SharedKernel.Postgres/Pagination/LikeEscaping.cs tests/Kartova.SharedKernel.Postgres.IntegrationTests/LikeEscapingTests.cs src/Modules/Organization/Kartova.Organization.Infrastructure/ListTeamsHandler.cs
git commit -m "refactor(postgres): extract shared EscapeLike helper; repoint ListTeamsHandler"
```

---

### Task 2: Services `displayNameContains` filter + `displayName asc` default

**Files:**
- Modify: `src/Modules/Catalog/Kartova.Catalog.Application/ListServicesQuery.cs`
- Modify: `src/Modules/Catalog/Kartova.Catalog.Infrastructure/ListServicesHandler.cs`
- Modify: `src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogEndpointDelegates.cs:378` (`ListServicesAsync`)
- Test: `src/Modules/Catalog/Kartova.Catalog.IntegrationTests/ListServicesPaginationTests.cs`

**Interfaces:**
- Consumes: `LikeEscaping.EscapeLike` (Task 1).
- Produces: `GET /api/v1/catalog/services?displayNameContains=<frag>` (case-insensitive substring); endpoint default sort `displayName asc`. `ListServicesQuery` gains `string? DisplayNameContains`.

- [ ] **Step 1: Write the failing integration tests**

Append to `ListServicesPaginationTests.cs` (inside the class):
```csharp
// Slice — displayName filter (ADR-0107). Real seam: real Postgres/RLS + real JWT.
[TestMethod]
public async Task GET_with_displayNameContains_returns_only_matching_services()
{
    var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
    var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Filter Team");
    foreach (var name in new[] { "alpha-pay-svc", "beta-pay-svc", "gamma-ship-svc" })
    {
        var r = await client.PostAsJsonAsync("/api/v1/catalog/services", new
        {
            displayName = name, description = "f", teamId, endpoints = Array.Empty<object>(),
        });
        Assert.IsTrue(r.IsSuccessStatusCode);
    }

    var resp = await client.GetAsync("/api/v1/catalog/services?displayNameContains=PAY&limit=200");
    Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
    var page = await resp.Content.ReadFromJsonAsync<CursorPage<ServiceResponse>>(KartovaApiFixtureBase.WireJson);
    var names = page!.Items.Select(i => i.DisplayName).ToList();

    Assert.IsTrue(names.Contains("alpha-pay-svc") && names.Contains("beta-pay-svc"),
        "case-insensitive substring must match both *pay* services");
    Assert.IsFalse(names.Contains("gamma-ship-svc"),
        "non-matching service must be excluded");
}

[TestMethod]
public async Task GET_with_blank_displayNameContains_behaves_as_no_filter()
{
    var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
    var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Blank Team");
    await SeedAsync(client, teamId, 2);

    var resp = await client.GetAsync("/api/v1/catalog/services?displayNameContains=%20&limit=200");
    Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
    var page = await resp.Content.ReadFromJsonAsync<CursorPage<ServiceResponse>>(KartovaApiFixtureBase.WireJson);
    Assert.IsTrue(page!.Items.Count >= 2, "whitespace filter must return all rows");
}

[TestMethod]
public async Task GET_changing_displayNameContains_against_a_live_cursor_returns_400()
{
    var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
    var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Cursor Team");
    foreach (var name in new[] { "cur-pay-1", "cur-pay-2", "cur-pay-3" })
    {
        var r = await client.PostAsJsonAsync("/api/v1/catalog/services", new
        {
            displayName = name, description = "c", teamId, endpoints = Array.Empty<object>(),
        });
        Assert.IsTrue(r.IsSuccessStatusCode);
    }

    var first = await client.GetAsync("/api/v1/catalog/services?displayNameContains=pay&limit=2");
    var page1 = await first.Content.ReadFromJsonAsync<CursorPage<ServiceResponse>>(KartovaApiFixtureBase.WireJson);
    Assert.IsNotNull(page1!.NextCursor);

    // Reuse the cursor but drop the filter — the f-map differs ⇒ 400.
    var bad = await client.GetAsync(
        $"/api/v1/catalog/services?limit=2&cursor={Uri.EscapeDataString(page1.NextCursor!)}");
    Assert.AreEqual(HttpStatusCode.BadRequest, bad.StatusCode);
}

[TestMethod]
public async Task GET_default_sort_is_displayName_ascending()
{
    var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
    var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Default Sort Team");
    foreach (var name in new[] { "dsort-zzz", "dsort-aaa", "dsort-mmm" })
    {
        var r = await client.PostAsJsonAsync("/api/v1/catalog/services", new
        {
            displayName = name, description = "s", teamId, endpoints = Array.Empty<object>(),
        });
        Assert.IsTrue(r.IsSuccessStatusCode);
    }

    // No sortBy/sortOrder ⇒ endpoint default must be displayName asc.
    var resp = await client.GetAsync("/api/v1/catalog/services?limit=200");
    var page = await resp.Content.ReadFromJsonAsync<CursorPage<ServiceResponse>>(KartovaApiFixtureBase.WireJson);
    var seeded = page!.Items.Select(i => i.DisplayName)
        .Where(n => n is "dsort-aaa" or "dsort-mmm" or "dsort-zzz").ToList();
    CollectionAssert.AreEqual(new[] { "dsort-aaa", "dsort-mmm", "dsort-zzz" }, seeded,
        "default order must be ascending displayName");
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `cmd //c "dotnet test src/Modules/Catalog/Kartova.Catalog.IntegrationTests --filter FullyQualifiedName~ListServicesPaginationTests"`
Expected: FAIL — filter not applied (matching test sees `gamma-ship-svc`), cursor test returns 200 not 400, default-sort test fails (current default is `createdAt desc`).

- [ ] **Step 3: Add the query field**

`ListServicesQuery.cs`:
```csharp
public sealed record ListServicesQuery(
    ServiceSortField SortBy,
    SortOrder SortOrder,
    string? Cursor,
    int Limit,
    string? DisplayNameContains);
```

- [ ] **Step 4: Apply the filter in the handler**

`ListServicesHandler.cs` — replace the body up to the `ToCursorPagedAsync` call:
```csharp
var spec = ServiceSortSpecs.Resolve(q.SortBy);

// Apply the displayName filter BEFORE pagination so a hidden row never becomes a
// cursor boundary (same invariant as ListApplicationsHandler / ListTeamsHandler).
IQueryable<DomainService> source = db.Services;
Dictionary<string, string>? filters = null;
if (q.DisplayNameContains is { } name)
{
    var pattern = $"%{LikeEscaping.EscapeLike(name)}%";
    source = source.Where(s => EF.Functions.ILike(s.DisplayName, pattern, "\\"));
    // The owning module owns the f-map keys/values; the shared codec treats them
    // as opaque. A change mid-pagination trips CursorFilterMismatchException.
    filters = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["displayNameContains"] = name,
    };
}

var page = await source
    .ToCursorPagedAsync(
        spec, q.SortOrder, q.Cursor, q.Limit,
        ServiceSortSpecs.IdSelector, IdExtractor, ct,
        expectedFilters: filters);
```
Add the required usings at the top if not present:
```csharp
using Kartova.SharedKernel.Postgres.Pagination;
using Microsoft.EntityFrameworkCore;
```
(`Kartova.SharedKernel.Postgres.Pagination` is already imported for `ToCursorPagedAsync`; add `Microsoft.EntityFrameworkCore` for `EF.Functions.ILike`.)

- [ ] **Step 5: Bind the param + flip the default in the endpoint**

`CatalogEndpointDelegates.cs` `ListServicesAsync` (line ~378):
```csharp
internal static async Task<IResult> ListServicesAsync(
    [FromQuery] string? sortBy,
    [FromQuery] string? sortOrder,
    [FromQuery] string? cursor,
    [FromQuery] string? limit,
    [FromQuery] string? displayNameContains,
    ListServicesHandler handler,
    CatalogDbContext db,
    CancellationToken ct)
{
    var (parsedSortBy, parsedSortOrder, effectiveLimit) =
        CursorListBinding.Bind<ServiceSortField>(sortBy, sortOrder, limit, ServiceSortSpecs.AllowedFieldNames);

    // Blank/whitespace ⇒ no filter (filter-absent must equal today's unfiltered cursor).
    var name = string.IsNullOrWhiteSpace(displayNameContains) ? null : displayNameContains.Trim();

    var query = new ListServicesQuery(
        SortBy: parsedSortBy ?? ServiceSortField.DisplayName,   // default flips: was CreatedAt
        SortOrder: parsedSortOrder ?? SortOrder.Asc,            // default flips: was Desc
        Cursor: cursor,
        Limit: effectiveLimit,
        DisplayNameContains: name);

    var page = await handler.Handle(query, db, ct);
    return Results.Ok(page);
}
```

- [ ] **Step 6: Run the integration tests to verify they pass**

Run: `cmd //c "dotnet test src/Modules/Catalog/Kartova.Catalog.IntegrationTests --filter FullyQualifiedName~ListServicesPaginationTests"`
Expected: PASS (all, including the four new tests and the pre-existing `GET_with_sortBy_displayName_asc_returns_items_in_ascending_order`).

- [ ] **Step 7: Commit**

```bash
git add src/Modules/Catalog/Kartova.Catalog.Application/ListServicesQuery.cs src/Modules/Catalog/Kartova.Catalog.Infrastructure/ListServicesHandler.cs src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogEndpointDelegates.cs src/Modules/Catalog/Kartova.Catalog.IntegrationTests/ListServicesPaginationTests.cs
git commit -m "feat(catalog): displayName filter + displayName-asc default on Services list"
```

---

### Task 3: Applications `displayNameContains` filter + `displayName asc` default

**Files:**
- Modify: `src/Modules/Catalog/Kartova.Catalog.Application/ListApplicationsQuery.cs`
- Modify: `src/Modules/Catalog/Kartova.Catalog.Infrastructure/ListApplicationsHandler.cs`
- Modify: `src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogEndpointDelegates.cs:111` (`ListApplicationsAsync`)
- Test: `src/Modules/Catalog/Kartova.Catalog.IntegrationTests/ListApplicationsPaginationTests.cs`

**Interfaces:**
- Consumes: `LikeEscaping.EscapeLike` (Task 1).
- Produces: `GET /api/v1/catalog/applications?displayNameContains=<frag>`; combines with existing `includeDecommissioned` + `createdByUserId`. Endpoint default sort `displayName asc`. `ListApplicationsQuery` gains `string? DisplayNameContains`.

- [ ] **Step 1: Write the failing integration tests**

Append to `ListApplicationsPaginationTests.cs`. Seed via the fixture helper used by the owner-filter tests (`Fx.SeedSingleApplicationAsync(tenantId, creatorUserId, teamId, namePrefix)` — the prefix is the displayName start). Add the usings already present in that test file (`System.Net`, `System.Net.Http.Json`, `Kartova.Catalog.Contracts`, `Kartova.SharedKernel.Pagination`, `Kartova.Testing.Auth`):
```csharp
[TestMethod]
public async Task GET_with_displayNameContains_returns_only_matching_applications()
{
    var unique = $"flt-{Guid.NewGuid():N}";
    var tenantId = Fx.TenantIdForEmail("admin@orga.kartova.local");
    var creator = await Fx.SeedUserInOrganizationAsync(
        tenantId, displayName: "Filter Creator", email: $"{unique}@orga.kartova.local");

    var match1 = await Fx.SeedSingleApplicationAsync(tenantId, creator, teamId: null, namePrefix: $"{unique}-pay-1");
    var match2 = await Fx.SeedSingleApplicationAsync(tenantId, creator, teamId: null, namePrefix: $"{unique}-pay-2");
    var other  = await Fx.SeedSingleApplicationAsync(tenantId, creator, teamId: null, namePrefix: $"{unique}-ship");

    try
    {
        var client = Fx.CreateClientForOrgA();
        var resp = await client.GetAsync($"/api/v1/catalog/applications?displayNameContains={unique}-PAY&limit=200");
        Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
        var page = await resp.Content.ReadFromJsonAsync<CursorPage<ApplicationResponse>>(KartovaApiFixtureBase.WireJson);
        var ids = page!.Items.Select(i => i.Id).ToHashSet();

        Assert.IsTrue(ids.Contains(match1) && ids.Contains(match2), "both *-pay-* apps must match (case-insensitive)");
        Assert.IsFalse(ids.Contains(other), "non-matching *-ship app must be excluded");
    }
    finally
    {
        await Fx.DeleteUserInOrganizationAsync(creator);
        await Fx.DeleteApplicationsByPrefixAsync(tenantId, unique);
    }
}

[TestMethod]
public async Task GET_displayNameContains_combines_with_includeDecommissioned()
{
    // Default view (includeDecommissioned omitted ⇒ false) hides Decommissioned rows
    // even when they match the name filter; includeDecommissioned=true surfaces them.
    var unique = $"fltdec-{Guid.NewGuid():N}";
    var tenantId = Fx.TenantIdForEmail("admin@orga.kartova.local");
    var creator = await Fx.SeedUserInOrganizationAsync(
        tenantId, displayName: "Dec Creator", email: $"{unique}@orga.kartova.local");

    var active = await Fx.SeedSingleApplicationAsync(tenantId, creator, teamId: null, namePrefix: $"{unique}-keep-active");
    var dead = await Fx.SeedDecommissionedApplicationAsync(tenantId, creator, teamId: null, namePrefix: $"{unique}-keep-dead");

    try
    {
        var client = Fx.CreateClientForOrgA();

        var defaultView = await client.GetAsync($"/api/v1/catalog/applications?displayNameContains={unique}-KEEP&limit=200");
        var p1 = await defaultView.Content.ReadFromJsonAsync<CursorPage<ApplicationResponse>>(KartovaApiFixtureBase.WireJson);
        var ids1 = p1!.Items.Select(i => i.Id).ToHashSet();
        Assert.IsTrue(ids1.Contains(active), "active match visible in default view");
        Assert.IsFalse(ids1.Contains(dead), "decommissioned match hidden in default view");

        var withDead = await client.GetAsync(
            $"/api/v1/catalog/applications?displayNameContains={unique}-KEEP&includeDecommissioned=true&limit=200");
        var p2 = await withDead.Content.ReadFromJsonAsync<CursorPage<ApplicationResponse>>(KartovaApiFixtureBase.WireJson);
        var ids2 = p2!.Items.Select(i => i.Id).ToHashSet();
        Assert.IsTrue(ids2.Contains(active) && ids2.Contains(dead), "both visible with includeDecommissioned=true");
    }
    finally
    {
        await Fx.DeleteUserInOrganizationAsync(creator);
        await Fx.DeleteApplicationsByPrefixAsync(tenantId, unique);
    }
}

[TestMethod]
public async Task GET_default_sort_is_displayName_ascending()
{
    var unique = $"dsort-{Guid.NewGuid():N}";
    var tenantId = Fx.TenantIdForEmail("admin@orga.kartova.local");
    var creator = await Fx.SeedUserInOrganizationAsync(
        tenantId, displayName: "Sort Creator", email: $"{unique}@orga.kartova.local");

    await Fx.SeedSingleApplicationAsync(tenantId, creator, teamId: null, namePrefix: $"{unique}-zzz");
    await Fx.SeedSingleApplicationAsync(tenantId, creator, teamId: null, namePrefix: $"{unique}-aaa");
    await Fx.SeedSingleApplicationAsync(tenantId, creator, teamId: null, namePrefix: $"{unique}-mmm");

    try
    {
        var client = Fx.CreateClientForOrgA();
        var resp = await client.GetAsync($"/api/v1/catalog/applications?displayNameContains={unique}&limit=200");
        var page = await resp.Content.ReadFromJsonAsync<CursorPage<ApplicationResponse>>(KartovaApiFixtureBase.WireJson);
        var seeded = page!.Items.Select(i => i.DisplayName).Where(n => n.StartsWith(unique)).ToList();
        var expected = seeded.OrderBy(n => n, StringComparer.Ordinal).ToList();
        CollectionAssert.AreEqual(expected, seeded, "default order must be ascending displayName");
    }
    finally
    {
        await Fx.DeleteUserInOrganizationAsync(creator);
        await Fx.DeleteApplicationsByPrefixAsync(tenantId, unique);
    }
}
```
> **Note for the implementer:** confirm the fixture exposes `SeedDecommissionedApplicationAsync`. If it does not, seed a normal app then drive it to Decommissioned via the lifecycle endpoints (`POST …/{id}/deprecate` then `…/{id}/decommission`), or drop the `includeDecommissioned` combination test down to a unit-level assertion in Step 4's handler test — the happy + default-sort tests already satisfy the gate-3 ≥1-happy/≥1-negative minimum for this endpoint.

- [ ] **Step 2: Run to verify they fail**

Run: `cmd //c "dotnet test src/Modules/Catalog/Kartova.Catalog.IntegrationTests --filter FullyQualifiedName~ListApplicationsPaginationTests"`
Expected: FAIL — filter not applied; default sort still `createdAt desc`.

- [ ] **Step 3: Add the query field**

`ListApplicationsQuery.cs` — add `DisplayNameContains` after `IncludeDecommissioned`, before the defaulted `CreatedByUserId`:
```csharp
public sealed record ListApplicationsQuery(
    ApplicationSortField SortBy,
    SortOrder SortOrder,
    string? Cursor,
    int Limit,
    bool IncludeDecommissioned,
    string? DisplayNameContains = null,
    Guid? CreatedByUserId = null);
```

- [ ] **Step 4: Apply the predicate + extend the f-map in the handler**

`ListApplicationsHandler.cs` — after the `CreatedByUserId` predicate block and before the `var filters = new Dictionary…` block, add:
```csharp
// displayName contains filter (ADR-0107). Applied before paging so a hidden row
// never becomes a cursor boundary.
if (q.DisplayNameContains is { } name)
{
    var pattern = $"%{LikeEscaping.EscapeLike(name)}%";
    source = source.Where(a => EF.Functions.ILike(a.DisplayName, pattern, "\\"));
}
```
Then extend the existing `filters` dictionary (after the `createdByUserId` conditional add):
```csharp
if (q.DisplayNameContains is { } displayName)
{
    filters["displayNameContains"] = displayName;
}
```
Add the using if not already present:
```csharp
using Kartova.SharedKernel.Postgres.Pagination; // LikeEscaping (ToCursorPagedAsync already here)
```
(`Microsoft.EntityFrameworkCore` for `EF.Functions` is already imported in this file via the existing `Lifecycle != Decommissioned` query path; verify and add if missing.)

- [ ] **Step 5: Bind the param + flip the default in the endpoint**

`CatalogEndpointDelegates.cs` `ListApplicationsAsync` (line 111): add the param to the signature (after `limit`, keeping `includeDecommissioned`/`createdByUserId`):
```csharp
[FromQuery] string? limit,
[FromQuery] string? displayNameContains,
[FromQuery] bool? includeDecommissioned,
[FromQuery] Guid? createdByUserId,
```
After the `CursorListBinding.Bind` call, add the trim, and update the query construction (lines 148–154):
```csharp
var name = string.IsNullOrWhiteSpace(displayNameContains) ? null : displayNameContains.Trim();

var query = new ListApplicationsQuery(
    SortBy: parsedSortBy ?? ApplicationSortField.DisplayName,   // default flips: was CreatedAt
    SortOrder: parsedSortOrder ?? SortOrder.Asc,                // default flips: was Desc
    Cursor: cursor,
    Limit: effectiveLimit,
    IncludeDecommissioned: includeDecommissioned ?? false,
    DisplayNameContains: name,
    CreatedByUserId: createdByUserId);
```

- [ ] **Step 6: Run the integration tests to verify they pass**

Run: `cmd //c "dotnet test src/Modules/Catalog/Kartova.Catalog.IntegrationTests --filter FullyQualifiedName~ListApplicationsPaginationTests"`
Expected: PASS. Also run the owner-filter suite to confirm no regression:
`cmd //c "dotnet test src/Modules/Catalog/Kartova.Catalog.IntegrationTests --filter FullyQualifiedName~ListApplicationsOwnerFilterTests"` → PASS.

- [ ] **Step 7: Commit**

```bash
git add src/Modules/Catalog/Kartova.Catalog.Application/ListApplicationsQuery.cs src/Modules/Catalog/Kartova.Catalog.Infrastructure/ListApplicationsHandler.cs src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogEndpointDelegates.cs src/Modules/Catalog/Kartova.Catalog.IntegrationTests/ListApplicationsPaginationTests.cs
git commit -m "feat(catalog): displayName filter + displayName-asc default on Applications list"
```

---

### Task 4: OpenAPI snapshot + `OpenApiTests` + codegen

**Files:**
- Modify: `web/openapi-snapshot.json` (add `displayNameContains` param to ListServices + ListApplications operations)
- Modify: `tests/Kartova.Api.IntegrationTests/OpenApiTests.cs` (assert the new param is published)
- Regenerate: `web/src/generated/openapi.ts` (gitignored — produced by codegen from the snapshot)

**Interfaces:**
- Consumes: the new endpoint params (Tasks 2, 3).
- Produces: `operations["ListServices"]["parameters"]["query"].displayNameContains` and `operations["ListApplications"]…displayNameContains` as `string` in the generated TS types — consumed by Task 8.

- [ ] **Step 1: Add the snapshot param to both Catalog operations**

In `web/openapi-snapshot.json`, find the `parameters` array for `paths./api/v1/catalog/services.get` and `paths./api/v1/catalog/applications.get`. Append this object to each operation's `parameters` array (mirrors the existing ListTeams `displayNameContains` block):
```json
{
  "name": "displayNameContains",
  "in": "query",
  "schema": {
    "type": "string"
  }
}
```

- [ ] **Step 2: Add the OpenApiTests assertions**

In `OpenApiTests.cs`, extend both `ListApplications_query_parameter_schemas_match_runtime_contract` and `ListServices_query_parameter_schemas_match_runtime_contract` with (after the existing limit assertions):
```csharp
var displayNameContainsSchema = ParameterSchema(parameters, "displayNameContains");
Assert.AreEqual("string", displayNameContainsSchema.GetProperty("type").GetString());
```

- [ ] **Step 3: Run OpenApiTests to verify they pass against the live spec**

Run: `cmd //c "dotnet test tests/Kartova.Api.IntegrationTests --filter FullyQualifiedName~OpenApiTests"`
Expected: PASS — the live `/openapi/v1.json` now publishes `displayNameContains` on both endpoints (the backend changes from Tasks 2–3 produce it).

- [ ] **Step 4: Regenerate the TS client from the snapshot**

Run (from `web/`): `npm run codegen`
Expected: `web/src/generated/openapi.ts` regenerates with `displayNameContains?: string` on both query types. (The file is gitignored; CI regenerates it from the committed snapshot — only the snapshot is committed.)

- [ ] **Step 5: Commit**

```bash
git add web/openapi-snapshot.json tests/Kartova.Api.IntegrationTests/OpenApiTests.cs
git commit -m "chore(web): publish displayNameContains query param for Services + Applications lists"
```

---

## Track B — Frontend infrastructure (parallel with Track A)

### Task 5: Widen `useListUrlState.setBooleanFilter` to a `string` key

**Files:**
- Modify: `web/src/lib/list/useListUrlState.ts`
- Test: `web/src/lib/list/__tests__/use-list-url-state.test.tsx`

**Interfaces:**
- Produces: `setBooleanFilter(name: string, value: boolean) => void` (was `name: TBoolFilter`) — lets the string-keyed `useListFilters` (Task 6) drive booleans without a cast. Read-side `booleanFilters` map keeps its narrowed keys.

- [ ] **Step 1: Write the failing test**

Append to `use-list-url-state.test.tsx` (match the file's existing render/hook harness — use the same pattern already present for boolean filters):
```tsx
it("setBooleanFilter accepts a plain string key (generic-consumer widening)", () => {
  const { result } = renderHook(
    () => useListUrlState({
      defaultSortBy: "displayName",
      defaultSortOrder: "asc",
      allowedSortFields: ["createdAt", "displayName"],
      booleanFilters: ["includeDecommissioned"],
    }),
    { wrapper: routerWrapper(["/"]) },
  );
  // Calling with a widened string key must type-check and round-trip.
  act(() => { (result.current.setBooleanFilter as (n: string, v: boolean) => void)("includeDecommissioned", true); });
  expect(result.current.booleanFilters.includeDecommissioned).toBe(true);
});
```
> Reuse whatever `routerWrapper`/`renderHook` helper the existing tests in this file already define; do not introduce a new one.

- [ ] **Step 2: Run to verify it fails (type or behavior)**

Run (from `web/`): `npx vitest run src/lib/list/__tests__/use-list-url-state.test.tsx`
Expected: the new test is the target; if the cast hides a type error today, this still passes at runtime — so primarily verify the type change in Step 3 compiles. Proceed to Step 3.

- [ ] **Step 3: Widen the signature**

In `useListUrlState.ts`:
- In the `ListUrlState` interface, change:
```ts
setBooleanFilter: (name: string, value: boolean) => void;
```
- Update the docstring above it to match the `setTextFilter` rationale:
```ts
/**
 * Accepts any string key so generic consumers (e.g. useListFilters, which is
 * string-keyed via FilterSpec) can drive it without a cast. Read-side literal
 * keys (booleanFilters map) retain their narrowed type.
 */
```
- The `useCallback` implementation already accepts `(name, value)`; only its declared param type needs to widen — change `(name: TBoolFilter, value: boolean)` to `(name: string, value: boolean)`.

- [ ] **Step 4: Run typecheck + tests to verify pass**

Run (from `web/`): `npm run typecheck && npx vitest run src/lib/list/__tests__/use-list-url-state.test.tsx`
Expected: PASS (typecheck clean, tests green).

- [ ] **Step 5: Commit**

```bash
git add web/src/lib/list/useListUrlState.ts web/src/lib/list/__tests__/use-list-url-state.test.tsx
git commit -m "feat(web): widen useListUrlState.setBooleanFilter to a string key"
```

---

### Task 6: `useListFilters` boolean support (submit-driven)

**Files:**
- Modify: `web/src/lib/list/filters/useListFilters.ts`
- Test: `web/src/lib/list/filters/__tests__/useListFilters.test.tsx`

**Interfaces:**
- Consumes: `useListUrlState` `booleanFilters` + `setBooleanFilter` (Task 5).
- Produces: `useListFilters` return now includes `bindBoolean(key) → { value: boolean; onChange: (v: boolean) => void }`. `submit()` commits text **and** boolean drafts; `clearAll()` resets both; `queryFilters[booleanKey]` is `boolean` (always present); `isActive`/`activeCount` count committed text (non-empty) + committed booleans (true). Consumed by Tasks 7, 9, 10.

- [ ] **Step 1: Write the failing tests**

Append to `useListFilters.test.tsx`. Extend `fakeUrlState` to carry booleans:
```tsx
function fakeUrlStateB(text: Record<string, string> = {}, bool: Record<string, boolean> = {}) {
  const state = {
    textFilters: { ...text } as Record<string, string>,
    booleanFilters: { ...bool } as Record<string, boolean>,
  };
  const setTextFilter = vi.fn((name: string, value: string) => {
    state.textFilters = { ...state.textFilters, [name]: value.trim() };
  });
  const setBooleanFilter = vi.fn((name: string, value: boolean) => {
    state.booleanFilters = { ...state.booleanFilters, [name]: value };
  });
  return { state, setTextFilter, setBooleanFilter };
}

const boolSpecs: FilterSpec[] = [
  { key: "displayNameContains", type: "text", label: "Search", placeholder: "…" },
  { key: "includeDecommissioned", type: "boolean", label: "Show decommissioned" },
];

describe("useListFilters — boolean specs", () => {
  it("bindBoolean toggling updates draft but does NOT commit until submit", () => {
    const u = fakeUrlStateB({ displayNameContains: "" }, { includeDecommissioned: false });
    const { result } = renderHook(() => useListFilters(boolSpecs, {
      textFilters: u.state.textFilters, setTextFilter: u.setTextFilter,
      booleanFilters: u.state.booleanFilters, setBooleanFilter: u.setBooleanFilter,
    } as never));

    act(() => { result.current.bindBoolean("includeDecommissioned").onChange(true); });
    expect(u.setBooleanFilter).not.toHaveBeenCalled();
    expect(result.current.bindBoolean("includeDecommissioned").value).toBe(true);

    act(() => { result.current.submit(); });
    expect(u.setBooleanFilter).toHaveBeenCalledWith("includeDecommissioned", true);
  });

  it("queryFilters carries the committed boolean (always present)", () => {
    const u = fakeUrlStateB({ displayNameContains: "" }, { includeDecommissioned: true });
    const { result } = renderHook(() => useListFilters(boolSpecs, {
      textFilters: u.state.textFilters, setTextFilter: u.setTextFilter,
      booleanFilters: u.state.booleanFilters, setBooleanFilter: u.setBooleanFilter,
    } as never));
    expect(result.current.queryFilters.includeDecommissioned).toBe(true);
  });

  it("isActive/activeCount count a committed true boolean", () => {
    const u = fakeUrlStateB({ displayNameContains: "" }, { includeDecommissioned: true });
    const { result } = renderHook(() => useListFilters(boolSpecs, {
      textFilters: u.state.textFilters, setTextFilter: u.setTextFilter,
      booleanFilters: u.state.booleanFilters, setBooleanFilter: u.setBooleanFilter,
    } as never));
    expect(result.current.isActive).toBe(true);
    expect(result.current.activeCount).toBe(1);
  });

  it("clearAll resets booleans to false and commits", () => {
    const u = fakeUrlStateB({ displayNameContains: "x" }, { includeDecommissioned: true });
    const { result } = renderHook(() => useListFilters(boolSpecs, {
      textFilters: u.state.textFilters, setTextFilter: u.setTextFilter,
      booleanFilters: u.state.booleanFilters, setBooleanFilter: u.setBooleanFilter,
    } as never));
    act(() => { result.current.clearAll(); });
    expect(u.setBooleanFilter).toHaveBeenCalledWith("includeDecommissioned", false);
    expect(u.setTextFilter).toHaveBeenCalledWith("displayNameContains", "");
  });
});
```

- [ ] **Step 2: Run to verify they fail**

Run (from `web/`): `npx vitest run src/lib/list/filters/__tests__/useListFilters.test.tsx`
Expected: FAIL — `bindBoolean` is undefined; `queryFilters.includeDecommissioned` undefined.

- [ ] **Step 3: Implement boolean support**

Replace `useListFilters.ts` with:
```ts
import { useCallback, useEffect, useMemo, useState } from "react";
import type { ListUrlState } from "@/lib/list/useListUrlState";
import type { FilterSpec } from "./types";

/**
 * Spec-driven filter state for list pages (ADR-0107). Composes useListUrlState.
 * Text and boolean inputs update local drafts; committed values (URL + query)
 * only change on submit() (Enter or Search). `queryFilters` is what the list
 * query hook spreads — text keys are committed-or-undefined (so the unfiltered
 * key matches the pre-filter key); boolean keys are always present (default
 * false), matching the always-on-the-wire includeDecommissioned dimension.
 */
export function useListFilters(
  specs: FilterSpec[],
  urlState: Pick<ListUrlState<string, string, string>,
    "textFilters" | "setTextFilter" | "booleanFilters" | "setBooleanFilter">,
) {
  const textSpecs = useMemo(() => specs.filter(s => s.type === "text"), [specs]);
  const boolSpecs = useMemo(() => specs.filter(s => s.type === "boolean"), [specs]);
  const committedText = urlState.textFilters;
  const committedBool = urlState.booleanFilters;

  const [draft, setDraftState] = useState<Record<string, string>>(
    () => Object.fromEntries(textSpecs.map(s => [s.key, committedText[s.key] ?? ""])),
  );
  const [boolDraft, setBoolDraftState] = useState<Record<string, boolean>>(
    () => Object.fromEntries(boolSpecs.map(s => [s.key, committedBool?.[s.key] ?? false])),
  );

  // Adopt committed values when they change from outside (back/forward, shared
  // link, clearAll). After our own submit, committed === draft so it's a no-op.
  useEffect(() => {
    setDraftState(prev => {
      let changed = false;
      const next = { ...prev };
      for (const s of textSpecs) {
        const c = committedText[s.key] ?? "";
        if (c !== prev[s.key]) { next[s.key] = c; changed = true; }
      }
      return changed ? next : prev;
    });
  }, [committedText, textSpecs]);

  useEffect(() => {
    setBoolDraftState(prev => {
      let changed = false;
      const next = { ...prev };
      for (const s of boolSpecs) {
        const c = committedBool?.[s.key] ?? false;
        if (c !== prev[s.key]) { next[s.key] = c; changed = true; }
      }
      return changed ? next : prev;
    });
  }, [committedBool, boolSpecs]);

  const setDraft = useCallback(
    (key: string, value: string) => setDraftState(prev => ({ ...prev, [key]: value })), []);
  const setBoolDraft = useCallback(
    (key: string, value: boolean) => setBoolDraftState(prev => ({ ...prev, [key]: value })), []);

  const bind = useCallback(
    (key: string) => ({ value: draft[key] ?? "", onChange: (v: string) => setDraft(key, v) }),
    [draft, setDraft]);
  const bindBoolean = useCallback(
    (key: string) => ({ value: boolDraft[key] ?? false, onChange: (v: boolean) => setBoolDraft(key, v) }),
    [boolDraft, setBoolDraft]);

  const submit = useCallback(() => {
    for (const s of textSpecs) urlState.setTextFilter(s.key, draft[s.key] ?? "");
    for (const s of boolSpecs) urlState.setBooleanFilter(s.key, boolDraft[s.key] ?? false);
  }, [textSpecs, boolSpecs, urlState, draft, boolDraft]);

  const clearAll = useCallback(() => {
    for (const s of textSpecs) urlState.setTextFilter(s.key, "");
    for (const s of boolSpecs) urlState.setBooleanFilter(s.key, false);
    setDraftState(Object.fromEntries(textSpecs.map(s => [s.key, ""])));
    setBoolDraftState(Object.fromEntries(boolSpecs.map(s => [s.key, false])));
  }, [textSpecs, boolSpecs, urlState]);

  const queryFilters = useMemo(() => {
    const out: Record<string, string | boolean | undefined> = {};
    for (const s of textSpecs) out[s.key] = (committedText[s.key] ?? "") || undefined;
    for (const s of boolSpecs) out[s.key] = committedBool?.[s.key] ?? false;
    return out;
  }, [textSpecs, boolSpecs, committedText, committedBool]);

  const isActive = useMemo(
    () => textSpecs.some(s => (committedText[s.key] ?? "") !== "")
       || boolSpecs.some(s => (committedBool?.[s.key] ?? false) === true),
    [textSpecs, boolSpecs, committedText, committedBool]);

  const activeCount = useMemo(
    () => textSpecs.filter(s => (committedText[s.key] ?? "") !== "").length
        + boolSpecs.filter(s => (committedBool?.[s.key] ?? false) === true).length,
    [textSpecs, boolSpecs, committedText, committedBool]);

  return { values: draft, bind, bindBoolean, submit, clearAll, isActive, activeCount, queryFilters };
}
```
> The `committedBool?.` optional chaining keeps the existing text-only tests green (they pass a `urlState` without `booleanFilters`); `boolSpecs` is empty there so the boolean branches never index it.

- [ ] **Step 4: Run all useListFilters tests to verify pass**

Run (from `web/`): `npx vitest run src/lib/list/filters/__tests__/useListFilters.test.tsx`
Expected: PASS (existing text tests + new boolean tests).

- [ ] **Step 5: Commit**

```bash
git add web/src/lib/list/filters/useListFilters.ts web/src/lib/list/filters/__tests__/useListFilters.test.tsx
git commit -m "feat(web): submit-driven boolean filter support in useListFilters"
```

---

### Task 7: `<FilterBar>` collapsible disclosure panel + boolean control

**Files:**
- Modify: `web/src/components/application/filter-bar/FilterBar.tsx`
- Test: `web/src/components/application/filter-bar/__tests__/FilterBar.test.tsx`
- Test (regression): `web/src/features/teams/pages/__tests__/TeamsListPage.test.tsx` (verify only — see Step 6)

**Interfaces:**
- Consumes: `useListFilters` `bindBoolean`, `isActive`, `activeCount` (Task 6).
- Produces: `<FilterBar>` renders a collapsible "Filters" panel (expanded by default) containing the controls; boolean specs render a `<Checkbox>`; `single-select`/`multi-select`/`date-range` still throw.

- [ ] **Step 1: Update the test mock + the throws-test, add boolean + collapse tests**

In `FilterBar.test.tsx`:
- Add `bindBoolean` to `makeFilters()`:
```tsx
bindBoolean: vi.fn((_key: string) => ({ value: false, onChange: vi.fn() })),
```
- Change the "throws for an unbuilt control type" test to use `date-range` (boolean is now built):
```tsx
it("throws for an unbuilt control type", () => {
  const bad: FilterSpec[] = [{ key: "x", type: "date-range", label: "X" }];
  expect(() => render(<FilterBar specs={bad} filters={filters()} />)).toThrow(/not implemented/i);
});
```
- Add new tests:
```tsx
it("renders a boolean control as a checkbox and toggling calls bindBoolean.onChange", () => {
  const onChange = vi.fn();
  const f = filters({ bindBoolean: vi.fn(() => ({ value: false, onChange })) });
  const specsB: FilterSpec[] = [{ key: "includeDecommissioned", type: "boolean", label: "Show decommissioned" }];
  render(<FilterBar specs={specsB} filters={f} />);
  const cb = screen.getByRole("checkbox", { name: /show decommissioned/i });
  fireEvent.click(cb);
  expect(onChange).toHaveBeenCalled();
  expect(f.bindBoolean).toHaveBeenCalledWith("includeDecommissioned");
});

it("is expanded by default — controls are visible", () => {
  render(<FilterBar specs={specs} filters={filters()} />);
  expect(screen.getByRole("search")).toBeInTheDocument();
  expect(screen.getByRole("textbox", { name: /search teams/i })).toBeInTheDocument();
});

it("collapsing hides the controls and flips aria-expanded", () => {
  render(<FilterBar specs={specs} filters={filters()} />);
  const toggle = screen.getByRole("button", { name: /^filters/i });
  expect(toggle).toHaveAttribute("aria-expanded", "true");
  fireEvent.click(toggle);
  expect(toggle).toHaveAttribute("aria-expanded", "false");
  expect(screen.queryByRole("search")).toBeNull();
});

it("shows active count in the header (survives collapse)", () => {
  const f = filters({ isActive: true, activeCount: 2 });
  render(<FilterBar specs={specs} filters={f} />);
  expect(screen.getByRole("button", { name: /filters \(2 active\)/i })).toBeInTheDocument();
});
```

- [ ] **Step 2: Run to verify the new tests fail**

Run (from `web/`): `npx vitest run src/components/application/filter-bar/__tests__/FilterBar.test.tsx`
Expected: FAIL — no "Filters" toggle button; boolean branch throws; no checkbox rendered.

- [ ] **Step 3: Rewrite `FilterBar.tsx`**

```tsx
import { useId, useState } from "react";
import { SearchLg, ChevronDown } from "@untitledui/icons";
import { Button } from "@/components/base/buttons/button";
import { Input } from "@/components/base/input/input";
import { Checkbox } from "@/components/base/checkbox/checkbox";
import { cx } from "@/lib/utils/cx";
import type { FilterSpec } from "@/lib/list/filters/types";
import type { useListFilters } from "@/lib/list/filters/useListFilters";

interface FilterBarProps {
  specs: FilterSpec[];
  filters: ReturnType<typeof useListFilters>;
}

/**
 * Standard list-filter surface (ADR-0107). Renders the controls inside a
 * collapsible "Filters" disclosure panel (expanded by default; the header keeps
 * the active count when collapsed so active filters are never hidden). Submit-
 * driven: text + boolean values are drafts until Enter or the Search button.
 * Builds the `text` and `boolean` controls; other types throw so misuse fails
 * loudly at dev time until they are implemented.
 */
export function FilterBar({ specs, filters }: FilterBarProps) {
  const [open, setOpen] = useState(true);
  const panelId = useId();

  return (
    <div className="rounded-xl bg-primary ring-1 ring-secondary">
      <button
        type="button"
        aria-expanded={open}
        aria-controls={panelId}
        onClick={() => setOpen(o => !o)}
        className="flex w-full items-center justify-between px-4 py-3 text-sm font-medium text-secondary"
      >
        <span>Filters{filters.isActive ? ` (${filters.activeCount} active)` : ""}</span>
        <ChevronDown className={cx("size-4 text-fg-quaternary transition-transform", open && "rotate-180")} />
      </button>

      {open && (
        <form
          id={panelId}
          role="search"
          className="flex flex-wrap items-center gap-3 border-t border-secondary px-4 py-3"
          onSubmit={(e) => { e.preventDefault(); filters.submit(); }}
        >
          {specs.map(spec => {
            if (spec.type === "text") {
              const { value, onChange } = filters.bind(spec.key);
              return (
                <div key={spec.key} className="w-full sm:w-72">
                  <Input
                    aria-label={spec.label}
                    placeholder={spec.placeholder}
                    icon={SearchLg}
                    size="sm"
                    value={value}
                    onChange={onChange}
                    maxLength={128}
                    onKeyDown={(e) => { if (e.key === "Enter") { e.preventDefault(); filters.submit(); } }}
                  />
                </div>
              );
            }
            if (spec.type === "boolean") {
              const { value, onChange } = filters.bindBoolean(spec.key);
              return <Checkbox key={spec.key} isSelected={value} onChange={onChange} label={spec.label} />;
            }
            throw new Error(
              `FilterBar: "${spec.type}" control not implemented (ADR-0107 clause 1 — text + boolean only)`,
            );
          })}

          <Button type="submit" size="sm" color="secondary">Search</Button>

          {filters.isActive && (
            <>
              <span className="text-sm text-tertiary">{filters.activeCount} active</span>
              <Button size="sm" color="link-gray" onClick={filters.clearAll}>Clear all</Button>
            </>
          )}
        </form>
      )}
    </div>
  );
}
```
> `ChevronDown` is from `@untitledui/icons` (same source as `SearchLg`/`Plus` already used across the app). `cx` from `@/lib/utils/cx` (same import the Checkbox base uses).

- [ ] **Step 4: Run FilterBar tests to verify pass**

Run (from `web/`): `npx vitest run src/components/application/filter-bar/__tests__/FilterBar.test.tsx`
Expected: PASS (all, including pre-existing text/Search/Clear-all tests — the panel is open by default so they still find the controls).

- [ ] **Step 5: Run typecheck**

Run (from `web/`): `npm run typecheck`
Expected: clean.

- [ ] **Step 6: Run the Teams page regression suite**

Run (from `web/`): `npx vitest run src/features/teams/pages/__tests__/TeamsListPage.test.tsx`
Expected: PASS — the Teams search box is inside the now-collapsible panel but it's expanded by default, so existing queries still find it. If any assertion is coupled to the old flat structure (e.g. expects the form as a direct child), add a `getByRole("button", { name: /^filters/i })` expand step or relax the query; do not change Teams filtering behavior.

- [ ] **Step 7: Commit**

```bash
git add web/src/components/application/filter-bar/FilterBar.tsx web/src/components/application/filter-bar/__tests__/FilterBar.test.tsx
git commit -m "feat(web): collapsible FilterBar panel + boolean control (ADR-0107 clause 6)"
```

---

## Join — Frontend wiring (depends on Task 4 + Tasks 6–7)

### Task 8: Thread `displayNameContains` through the API hooks

**Files:**
- Modify: `web/src/features/catalog/api/services.ts`
- Modify: `web/src/features/catalog/api/applications.ts`
- Test: `web/src/features/catalog/api/__tests__/services.test.tsx`, `web/src/features/catalog/api/__tests__/applications.test.tsx`

**Interfaces:**
- Consumes: generated `displayNameContains` query type (Task 4).
- Produces: `useServicesList`/`useApplicationsList` accept `displayNameContains?: string`, sent only when set. Consumed by Tasks 9, 10.

- [ ] **Step 1: Write the failing tests**

In `services.test.tsx`, add (match the file's existing apiClient-spy harness):
```tsx
it("sends displayNameContains in the query when provided", async () => {
  // ...arrange the apiClient.GET spy as the existing tests do...
  // render/invoke useServicesList with { sortBy:"displayName", sortOrder:"asc", displayNameContains:"pay" }
  // assert the GET call's params.query.displayNameContains === "pay"
});
it("omits displayNameContains when not set", async () => {
  // assert params.query.displayNameContains is undefined
});
```
Mirror the exact spy/assert structure already used in `services.test.tsx` for the `sortBy` param. Do the same in `applications.test.tsx`.

- [ ] **Step 2: Run to verify they fail**

Run (from `web/`): `npx vitest run src/features/catalog/api/__tests__/services.test.tsx src/features/catalog/api/__tests__/applications.test.tsx`
Expected: FAIL — `displayNameContains` not in params.

- [ ] **Step 3: Add the param to `services.ts`**

```ts
type ServicesListParams = {
  sortBy: NonNullable<ListServicesQuery["sortBy"]>;
  sortOrder: NonNullable<ListServicesQuery["sortOrder"]>;
  limit?: number;
  displayNameContains?: string;
};
```
In `useServicesList`'s `query` object add:
```ts
...(params.displayNameContains ? { displayNameContains: params.displayNameContains } : {}),
```

- [ ] **Step 4: Add the param to `applications.ts`**

```ts
type ApplicationsListParams = {
  sortBy: NonNullable<ListApplicationsQuery["sortBy"]>;
  sortOrder: NonNullable<ListApplicationsQuery["sortOrder"]>;
  limit?: number;
  includeDecommissioned?: boolean;
  createdByUserId?: string;
  displayNameContains?: string;
};
```
In `useApplicationsList`'s `query` object add:
```ts
...(params.displayNameContains ? { displayNameContains: params.displayNameContains } : {}),
```

- [ ] **Step 5: Run tests + typecheck to verify pass**

Run (from `web/`): `npm run typecheck && npx vitest run src/features/catalog/api/__tests__/services.test.tsx src/features/catalog/api/__tests__/applications.test.tsx`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add web/src/features/catalog/api/services.ts web/src/features/catalog/api/applications.ts web/src/features/catalog/api/__tests__/services.test.tsx web/src/features/catalog/api/__tests__/applications.test.tsx
git commit -m "feat(web): thread displayNameContains through catalog list API hooks"
```

---

### Task 9: Wire `<FilterBar>` into the Services list page

**Files:**
- Modify: `web/src/features/catalog/pages/ServicesListPage.tsx`
- Test: `web/src/features/catalog/pages/__tests__/ServicesListPage.test.tsx`

**Interfaces:**
- Consumes: `FilterBar` (Task 7), `useListFilters` (Task 6), `useServicesList` `displayNameContains` (Task 8).

- [ ] **Step 1: Write the failing tests**

Add to `ServicesListPage.test.tsx` (match the existing harness/mocks):
```tsx
it("renders the Filters search box", () => {
  // arrange apiClient GET → pageOf([])
  // render <ServicesListPage/>
  expect(screen.getByRole("textbox", { name: /search services/i })).toBeInTheDocument();
});

it("defaults sort to displayName asc (sends it to useServicesList)", () => {
  // spy useServicesList; render at "/"; expect called with { sortBy:"displayName", sortOrder:"asc", ... }
});

it("shows a filtered empty-state when a search yields no rows", async () => {
  // render at "/?displayNameContains=zzz" with GET → pageOf([])
  expect(await screen.findByText(/no services match your search/i)).toBeInTheDocument();
});
```

- [ ] **Step 2: Run to verify they fail**

Run (from `web/`): `npx vitest run src/features/catalog/pages/__tests__/ServicesListPage.test.tsx`
Expected: FAIL — no search box; default sort still `desc`; no filtered empty-state.

- [ ] **Step 3: Rewrite `ServicesListPage.tsx`**

```tsx
import { useMemo, useState, useEffect } from "react";
import { Plus } from "@untitledui/icons";
import { Button } from "@/components/base/buttons/button";
import { Card, CardContent } from "@/components/base/card/card";
import { FilterBar } from "@/components/application/filter-bar/FilterBar";
import { useListFilters } from "@/lib/list/filters/useListFilters";
import type { FilterSpec } from "@/lib/list/filters/types";
import { useServicesList } from "@/features/catalog/api/services";
import { useTeamsList } from "@/features/teams/api/teams";
import { useListUrlState } from "@/lib/list/useListUrlState";
import { ServicesTable } from "@/features/catalog/components/ServicesTable";
import { RegisterServiceDialog } from "@/features/catalog/components/RegisterServiceDialog";
import { usePermissions } from "@/shared/auth/usePermissions";
import { KartovaPermissions } from "@/shared/auth/permissions";

const ALLOWED_SORT_FIELDS = ["createdAt", "displayName"] as const;
const TEXT_FILTERS = ["displayNameContains"] as const;
const FILTER_SPECS: FilterSpec[] = [
  { key: "displayNameContains", type: "text", label: "Search services", placeholder: "Search by name…" },
];

export function ServicesListPage() {
  const urlState = useListUrlState({
    defaultSortBy: "displayName",
    defaultSortOrder: "asc",
    allowedSortFields: ALLOWED_SORT_FIELDS,
    textFilters: TEXT_FILTERS,
  });
  const filters = useListFilters(FILTER_SPECS, urlState);

  const list = useServicesList({
    sortBy: urlState.sortBy,
    sortOrder: urlState.sortOrder,
    displayNameContains: filters.queryFilters.displayNameContains as string | undefined,
  });
  const teamsList = useTeamsList({ sortBy: "displayName", sortOrder: "asc", limit: 200 });
  const teamNameById = useMemo(
    () => new Map<string, string>((teamsList.items ?? []).map((t) => [t.id, t.displayName])),
    [teamsList.items],
  );
  const [dialogOpen, setDialogOpen] = useState(false);

  const { hasPermission, isLoading: permissionsLoading } = usePermissions();
  const canRegister = !permissionsLoading && hasPermission(KartovaPermissions.CatalogServicesRegister);

  useEffect(() => {
    if (list.isError) console.error("ServicesListPage list error", list.error);
  }, [list.isError, list.error]);

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <h2 className="text-2xl font-semibold text-primary">Services</h2>
        {canRegister && (
          <Button onClick={() => setDialogOpen(true)} size="sm" color="primary" iconLeading={Plus}>
            Register Service
          </Button>
        )}
      </div>

      <FilterBar specs={FILTER_SPECS} filters={filters} />

      {list.isError ? (
        <Card className="mx-auto max-w-md">
          <CardContent className="space-y-3 p-6 text-center">
            <p className="text-base font-medium text-error-primary">Failed to load services</p>
            <p className="text-sm text-tertiary">Try refreshing or resetting the list.</p>
            <Button size="sm" onClick={() => list.reset()}>Reset</Button>
          </CardContent>
        </Card>
      ) : !list.isLoading && list.items.length === 0 && filters.isActive ? (
        <Card className="mx-auto max-w-md text-center">
          <CardContent className="space-y-2 p-8">
            <p className="text-base font-medium text-primary">No services match your search</p>
            <p className="text-sm text-tertiary">Try a different name.</p>
          </CardContent>
        </Card>
      ) : (
        <ServicesTable
          list={list}
          sortBy={urlState.sortBy}
          sortOrder={urlState.sortOrder}
          onSortChange={urlState.setSort}
          teamNameById={teamNameById}
        />
      )}

      {canRegister && <RegisterServiceDialog open={dialogOpen} onOpenChange={setDialogOpen} />}
    </div>
  );
}
```
> The `ServicesTable` already renders its own "No services yet" empty card for the unfiltered case; the page only intercepts the **filtered** empty case so the message distinguishes "no matches" from "none yet" (ADR-0107 clause 5).

- [ ] **Step 4: Run tests + typecheck to verify pass**

Run (from `web/`): `npm run typecheck && npx vitest run src/features/catalog/pages/__tests__/ServicesListPage.test.tsx`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add web/src/features/catalog/pages/ServicesListPage.tsx web/src/features/catalog/pages/__tests__/ServicesListPage.test.tsx
git commit -m "feat(web): Services list search via FilterBar + displayName-asc default"
```

---

### Task 10: Wire `<FilterBar>` into the Applications (Catalog) list page

**Files:**
- Modify: `web/src/features/catalog/pages/CatalogListPage.tsx`
- Test: `web/src/features/catalog/pages/__tests__/CatalogListPage.test.tsx`

**Interfaces:**
- Consumes: `FilterBar` (Task 7), `useListFilters` (Task 6), `useApplicationsList` `displayNameContains` (Task 8).

- [ ] **Step 1: Update the existing tests for submit-driven booleans + add new tests**

In `CatalogListPage.test.tsx`:
- The "Show decommissioned checkbox" tests that asserted **immediate** URL writes must now click **Search** after toggling (boolean is submit-driven). Update:
```tsx
it("toggling the checkbox then Search writes the URL param to true", async () => {
  const user = userEvent.setup();
  const get = vi.fn().mockResolvedValue({ data: pageOf([]), error: undefined });
  vi.spyOn(clientModule, "apiClient", "get").mockReturnValue({ GET: get, POST: vi.fn() } as never);
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(<></>, { wrapper: harnessWithRoutes(qc) });

  await user.click(screen.getByRole("checkbox", { name: /show decommissioned/i }));
  await user.click(screen.getByRole("button", { name: /^search$/i }));
  expect(screen.getByTestId("probe").textContent).toContain("includeDecommissioned=true");
});

it("toggling off then Search removes the URL param (no =false clutter)", async () => {
  const user = userEvent.setup();
  const get = vi.fn().mockResolvedValue({ data: pageOf([]), error: undefined });
  vi.spyOn(clientModule, "apiClient", "get").mockReturnValue({ GET: get, POST: vi.fn() } as never);
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(<></>, { wrapper: harnessWithRoutes(qc, ["/?includeDecommissioned=true"]) });

  await user.click(screen.getByRole("checkbox", { name: /show decommissioned/i }));
  await user.click(screen.getByRole("button", { name: /^search$/i }));
  expect(screen.getByTestId("probe").textContent).not.toContain("includeDecommissioned");
});
```
(Keep the "unchecked by default / hydrates to checked from URL" tests — those still hold: the checkbox lives in the now-expanded-by-default panel and hydrates from the committed URL value.)
- Add new tests:
```tsx
it("renders the Filters search box", () => {
  const get = vi.fn().mockResolvedValue({ data: pageOf([]), error: undefined });
  vi.spyOn(clientModule, "apiClient", "get").mockReturnValue({ GET: get, POST: vi.fn() } as never);
  render(<CatalogListPage />, { wrapper: harness(new QueryClient({ defaultOptions: { queries: { retry: false } } })) });
  expect(screen.getByRole("textbox", { name: /search applications/i })).toBeInTheDocument();
});

it("defaults sort to displayName asc", () => {
  useApplicationsListSpy = vi.spyOn(applicationsModule, "useApplicationsList").mockReturnValue(stubListResult);
  render(<></>, { wrapper: harnessWithApp(["/"]) });
  expect(useApplicationsListSpy).toHaveBeenCalledWith(
    expect.objectContaining({ sortBy: "displayName", sortOrder: "asc" }),
  );
});
```
> Move/declare `useApplicationsListSpy` in the relevant `describe` block as the existing file does for the "API hook receives correct query params" suite.

- [ ] **Step 2: Run to verify they fail**

Run (from `web/`): `npx vitest run src/features/catalog/pages/__tests__/CatalogListPage.test.tsx`
Expected: FAIL — no search box; default still `createdAt desc`; toggling writes URL immediately (old behavior) so the new submit-driven tests fail.

- [ ] **Step 3: Rewrite `CatalogListPage.tsx`**

```tsx
import { useMemo, useState, useEffect } from "react";
import { Plus } from "@untitledui/icons";
import { Button } from "@/components/base/buttons/button";
import { Card, CardContent } from "@/components/base/card/card";
import { FilterBar } from "@/components/application/filter-bar/FilterBar";
import { useListFilters } from "@/lib/list/filters/useListFilters";
import type { FilterSpec } from "@/lib/list/filters/types";
import { useApplicationsList } from "@/features/catalog/api/applications";
import { useTeamsList } from "@/features/teams/api/teams";
import { useListUrlState } from "@/lib/list/useListUrlState";
import { ApplicationsTable } from "@/features/catalog/components/ApplicationsTable";
import { RegisterApplicationDialog } from "@/features/catalog/components/RegisterApplicationDialog";
import { usePermissions } from "@/shared/auth/usePermissions";
import { KartovaPermissions } from "@/shared/auth/permissions";

const ALLOWED_SORT_FIELDS = ["createdAt", "displayName"] as const;
const BOOLEAN_FILTERS = ["includeDecommissioned"] as const;
const TEXT_FILTERS = ["displayNameContains"] as const;
const FILTER_SPECS: FilterSpec[] = [
  { key: "displayNameContains", type: "text", label: "Search applications", placeholder: "Search by name…" },
  { key: "includeDecommissioned", type: "boolean", label: "Show decommissioned" },
];

export function CatalogListPage() {
  const urlState = useListUrlState({
    defaultSortBy: "displayName",
    defaultSortOrder: "asc",
    allowedSortFields: ALLOWED_SORT_FIELDS,
    booleanFilters: BOOLEAN_FILTERS,
    textFilters: TEXT_FILTERS,
  });
  const filters = useListFilters(FILTER_SPECS, urlState);

  const list = useApplicationsList({
    sortBy: urlState.sortBy,
    sortOrder: urlState.sortOrder,
    displayNameContains: filters.queryFilters.displayNameContains as string | undefined,
    includeDecommissioned: filters.queryFilters.includeDecommissioned as boolean,
  });
  const teamsList = useTeamsList({ sortBy: "displayName", sortOrder: "asc", limit: 200 });
  const teamNameById = useMemo(
    () => new Map<string, string>((teamsList.items ?? []).map(t => [t.id, t.displayName])),
    [teamsList.items],
  );
  const [dialogOpen, setDialogOpen] = useState(false);

  const { hasPermission, isLoading: permissionsLoading } = usePermissions();
  const canRegister = !permissionsLoading && hasPermission(KartovaPermissions.CatalogApplicationsRegister);

  useEffect(() => {
    if (list.isError) console.error("CatalogListPage list error", list.error);
  }, [list.isError, list.error]);

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <h2 className="text-2xl font-semibold text-primary">Catalog</h2>
        {canRegister && (
          <Button onClick={() => setDialogOpen(true)} size="sm" color="primary" iconLeading={Plus}>
            Register Application
          </Button>
        )}
      </div>

      <FilterBar specs={FILTER_SPECS} filters={filters} />

      {list.isError ? (
        <Card className="mx-auto max-w-md">
          <CardContent className="space-y-3 p-6 text-center">
            <p className="text-base font-medium text-error-primary">Failed to load applications</p>
            <p className="text-sm text-tertiary">Try refreshing or resetting the list.</p>
            <Button size="sm" onClick={() => list.reset()}>Reset</Button>
          </CardContent>
        </Card>
      ) : !list.isLoading && list.items.length === 0 && filters.isActive ? (
        <Card className="mx-auto max-w-md text-center">
          <CardContent className="space-y-2 p-8">
            <p className="text-base font-medium text-primary">No applications match your filters</p>
            <p className="text-sm text-tertiary">Try a different name or clear the filters.</p>
          </CardContent>
        </Card>
      ) : (
        <ApplicationsTable
          list={list}
          sortBy={urlState.sortBy}
          sortOrder={urlState.sortOrder}
          onSortChange={urlState.setSort}
          teamNameById={teamNameById}
        />
      )}

      {canRegister && <RegisterApplicationDialog open={dialogOpen} onOpenChange={setDialogOpen} />}
    </div>
  );
}
```
> The standalone `<Checkbox>`/`<div className="flex items-center justify-end">` block and the `Checkbox` import are **removed** — the checkbox now lives in `<FilterBar>` via the `includeDecommissioned` boolean spec.

- [ ] **Step 4: Run tests + typecheck to verify pass**

Run (from `web/`): `npm run typecheck && npx vitest run src/features/catalog/pages/__tests__/CatalogListPage.test.tsx`
Expected: PASS (updated + new tests).

- [ ] **Step 5: Commit**

```bash
git add web/src/features/catalog/pages/CatalogListPage.tsx web/src/features/catalog/pages/__tests__/CatalogListPage.test.tsx
git commit -m "feat(web): Applications list search + includeDecommissioned folded into FilterBar"
```

---

## Finalization

### Task 11: ADR-0107 amendment + registry + memory + CHECKLIST

**Files:**
- Modify: `docs/architecture/decisions/ADR-0107-list-filtering-consideration-and-filterbar-ui.md`
- Modify: `docs/design/list-filter-registry.md`
- Modify: `docs/product/CHECKLIST.md`
- Modify: `C:\Users\roman.glogowski\.claude\projects\C--Projects-Private-Roman-Gig2\memory\feedback_default_list_sort.md` + `MEMORY.md` pointer

- [ ] **Step 1: Append the ADR-0107 clause-6 amendment**

At the end of the ADR-0107 Decision section (or as a dated amendment note near clause 6), paste the text staged in spec §7:
> **Amendment (2026-06-22) — clause 6 (collapse).** `<FilterBar>` renders its controls inside a collapsible disclosure panel on all viewports… expanded by default… header retains `Filters (N active)`… standard shell for every consumer (Teams included). Small-viewport drawer/sheet remains the responsive form. Open/closed state is ephemeral; persistence deferred.

- [ ] **Step 2: Update the registry**

In `docs/design/list-filter-registry.md`:
- Services row → `Filter fields: displayNameContains` · `Status: built` · `Notes: text search; team/health/createdBy facets deferred → E-05`.
- Applications row → `Filter fields: displayNameContains + includeDecommissioned (FilterBar)` · `Status: built` (drop "(pre-standard)") · `Notes: lifecycle/team/createdBy facets deferred → E-05`.
- Teams row note: shell is now a collapsible disclosure panel (expanded by default), standard across consumers.
- "How to update" / control-availability note: **text + boolean controls built**; single/multi-select + date-range still reserved.

- [ ] **Step 3: Update CHECKLIST**

In `docs/product/CHECKLIST.md`, annotate E-02.F-01 and E-02.F-02 lines with: "+ list filter (displayName search) + displayName-asc default sort + FilterBar collapsible panel (list-filter-surface-catalog, 2026-06-22)".

- [ ] **Step 4: Update the default-list-sort memory**

Edit `feedback_default_list_sort.md` body to: default list sort is **`displayName asc`**, standardized across Teams/Services/Applications (supersedes the earlier "displayName desc / Applications left as-is"). Keep the `MEMORY.md` one-line pointer in sync.

- [ ] **Step 5: Commit**

```bash
git add docs/architecture/decisions/ADR-0107-list-filtering-consideration-and-filterbar-ui.md docs/design/list-filter-registry.md docs/product/CHECKLIST.md
git commit -m "docs: ADR-0107 clause-6 amendment + registry/CHECKLIST for Catalog filter slice"
```
(The memory files live outside the repo — they are not part of this commit.)

---

### Task 12: Full verification (DoD gates) + push

- [ ] **Step 1: Full solution build (gate 1)**

Run: `cmd //c "dotnet build Kartova.slnx -warnaserror"`
Expected: 0 warnings, 0 errors.

- [ ] **Step 2: Full backend test suite incl. architecture + integration (gate 3)**

Run: `cmd //c "dotnet test Kartova.slnx"`
Expected: all green. (If an integration assembly trips a transient Docker named-pipe timeout under saturation, re-run that single assembly in isolation before treating it as red — known flake.)

- [ ] **Step 3: Frontend suite + typecheck + lint (gate 3, frontend)**

Run (from `web/`): `npm run typecheck && npm run lint && npx vitest run`
Expected: all green.

- [ ] **Step 4: Pre-push CI mirror (gates 1/3/4 in Release)**

Run: `scripts/ci-local.sh` (or `scripts/ci-local.sh backend` + `scripts/ci-local.sh frontend`)
Expected: green — this is the one place the Release build+test, the web image, and helm run as CI will. **Gate 4 (container build)** runs here. If Docker is unavailable in-session, flag gate 4 as *pending user verification*.

- [ ] **Step 5: Manual verification (ADR-0084) — pending-user if no dev stack**

Cold-start the dev stack, then Playwright MCP: `/catalog` and `/catalog/services` → expand/collapse the Filters panel → type a name + toggle "Show decommissioned" → click Search → list narrows + URL carries `?displayNameContains=…&includeDecommissioned=true` → "Clear all" restores → default order A→Z → console clean. If the stack/Docker is unavailable, record as *pending user verification*.

- [ ] **Step 6: Gates 5/7/8/9 (review passes)**

Run `/simplify`, `/superpowers:requesting-code-review`, `/pr-review-toolkit:review-pr`, `/deep-review` against the branch diff with the spec + plan as context. Address Blocking + Should-fix; triage nits. **Mutation gate (6) is skipped this slice (recorded in spec §7).**

- [ ] **Step 7: Terminal re-verify + push/PR**

After review fixes, re-run Steps 1–3 to confirm still green, then push and open the PR. Until all eight always-blocking gates are green and citable, the honest status is "implementation staged, verification pending".

```bash
git push -u origin feat/list-filter-surface-catalog
```

---

## Self-Review

**1. Spec coverage:**
- §3 #1 (displayName mirror) → Tasks 2, 3. · #2 (boolean control + fold-in) → Tasks 7, 10. · #3 (submit-driven boolean) → Task 6 + Task 10 tests. · #4 (shared EscapeLike) → Task 1. · #5 (displayName-asc default, screen + endpoint) → Tasks 2, 3 (backend), 9, 10 (screen). · #6 (f-map conditional) → Tasks 2, 3. · #7 (ILIKE/blank-absent) → Tasks 2, 3. · #9 (other controls throw) → Task 7. · #10 (no new ProblemDetails) → nothing to build. · #11 (collapsible panel) → Task 7.
- §4.2 file map: every file has an owning task. §6 gate-5 artifacts: shared-helper test (T1), both handler/integration suites (T2, T3), OpenApiTests (T4), useListFilters/FilterBar/api/page tests (T6–T10), Teams regression (T7 Step 6). §7 amendment/registry/memory/CHECKLIST → Task 11. DoD gates → Task 12.

**2. Placeholder scan:** One intentional implementer-note in Task 3 Step 1 (fixture method `SeedDecommissionedApplicationAsync` to confirm, with a concrete fallback that still satisfies the gate-3 minimum). Tasks 8/9/10 reuse each test file's existing apiClient-spy / render harness rather than re-deriving it (those harnesses already exist in-file). All production code is shown in full; no "TBD"/"add error handling"/"similar to Task N" placeholders remain.

**3. Type consistency:** `displayNameContains` (string) and `includeDecommissioned` (boolean) are used identically across query records (T2/T3), endpoints (T2/T3), generated types (T4), API hooks (T8), `useListFilters.queryFilters` (T6), and pages (T9/T10). `bindBoolean` defined in T6 is consumed with the same `{ value, onChange }` shape in T7. `LikeEscaping.EscapeLike` defined in T1 is called identically in T2/T3. `queryFilters.includeDecommissioned` is typed `string | boolean | undefined` and narrowed with `as boolean` at the one always-present call site (T10) — consistent with the hook returning the committed boolean (default false).

No blocking issues found.
