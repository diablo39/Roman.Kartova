# API UI Surface + List Filters Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the sync `Api` catalog entity visible/usable in the web UI (list/detail/register screens) and build the three deferred ADR-0107 list filters (name typeahead, style multi-select, team multi-select), including their backend query params on `ListApis`.

**Architecture:** Two cohesive halves in one slice. (1) Backend: add `TeamId`/`Style`/`DisplayNameContains` filter params to the existing `ListApis` query/handler/endpoint, a verbatim mirror of `ListServices` (filters applied before pagination + encoded in the cursor f-map). (2) Frontend: mirror the shipped Service UI surface (`api/services.ts`, `RegisterServiceDialog`, `ServicesTable`, `ServicesListPage`, `ServiceDetailPage`) for APIs. Codegen bridge in between: the new filter params are new OpenAPI, so regenerate the client after the backend lands and before the frontend consumes it.

**Tech Stack:** .NET 10 / ASP.NET Core Minimal APIs, EF Core (Npgsql), Wolverine (direct-dispatch handlers per ADR-0093), MSTest v4 + NSubstitute, Postgres 18 + RLS; React 19 + TypeScript, TanStack Query, react-hook-form + zod, react-aria-components + Tailwind v4 (Untitled UI, ADR-0094), openapi-fetch generated client, Vitest + Testing Library, Playwright.

## Global Constraints

- **Solution file:** `Kartova.slnx` (not `.sln`). Build with `TreatWarningsAsErrors=true` — 0 warnings, 0 errors (gate 1).
- **Windows shell:** PowerShell/`cmd //c` for `dotnet`; Git Bash lacks `grep -P` (use `-E`/`Select-String`). Multi-line git messages via PowerShell tool + multiple `-m` flags.
- **Enum wire format:** camelCase (ADR-0109). `ApiStyle` → `rest`, `grpc`, `graphQL` (JsonNamingPolicy.CamelCase lowercases only the leading char; verified against the `graphQL` protocol literal in `registerService.ts`).
- **Cursor list contract (ADR-0095):** every list endpoint exposes `sortBy`/`sortOrder`/`cursor`/`limit`, returns `CursorPage<T>`. Filters applied **before** pagination; every non-empty filter dimension encoded into the cursor **f-map** (sorted → canonical) so a mid-pagination filter change trips `CursorFilterMismatchException` (400). Default sort = `displayName asc`.
- **Sort allowlist (frozen by S-01):** `{ displayName, style, version, createdAt }`. Do not add/remove sort fields.
- **Filter mandate (ADR-0107):** filters render through `<FilterBar>` / `useListFilters` and feed the ADR-0095 `f` map. Registry row updated on completion.
- **DbContext registration:** tenant-owned data via `AddModuleDbContext<T>`; handlers never touch `ITenantScope` (transport middleware does — ADR-0090). Direct-dispatch handlers, not `IMessageBus.InvokeAsync`, for HTTP endpoints (ADR-0093).
- **Coverage exclusion:** all `*Dto`/`*Request`/`*Response`/`*Contracts` types carry `[ExcludeFromCodeCoverage]` (arch test enforces).
- **No new `KartovaPermission`:** reads reuse `catalog.read`; register reuses `catalog.apis.register` (both already shipped in S-01). No 5-sync this slice.
- **react-aria `<Table>`:** exactly one `isRowHeader` column (`displayName`). Missing it blank-pages on heavy re-render (ADR-0084) — assert `getAllByRole("rowheader").length > 0` in tests.
- **Codegen:** generated client is gitignored with a committed `web/openapi-snapshot.json` fallback; `tsc -b` (`npm run build`) is the binding type gate. Regenerate the snapshot from the live API after the backend filter change.
- **Slice size:** ~400 LOC production business code target, ~800 ceiling (excludes tests/generated/migrations/DTOs).

---

## Impact Analysis (codelens/LSP)

Existing C# symbols whose signature/behavior changes this slice:

- **`ListApisQuery` (record ctor)** — adds `Guid[] TeamId`, `ApiStyle[] Style`, `string? DisplayNameContains`. `roslyn-codelens find_references` (2026-07-04): **exactly 2 references** — construction at `CatalogEndpointDelegates.cs:595` (the `ListApisAsync` delegate) and the handler param at `ListApisHandler.cs:18`. Both are modified in Tasks 1–2. No other construction site. (grep for `new ListApisQuery` confirms the single ctor call.)
- **`ListApisHandler.Handle`** — behavior change (adds filter predicates + f-map). `find_references` returned `[]` (delegate invocation under-reported); grep confirms the sole caller is `ListApisAsync` at `CatalogEndpointDelegates.cs:601`, plus the new `ListApisHandlerFilterTests` (Task 1). DI registration `CatalogModule.cs:245` (`AddScoped<ListApisHandler>()`) is unaffected (no signature coupling).
- **No shared-const / cross-module / interface symbol touched.** `ApiStyle` enum is already public and consumed by `ApiResponse` (additive use as a filter type). `ProblemTypes.InvalidStyleFilter` is a **new** const (Task 2), mirroring `InvalidHealthFilter`.
- **Frontend couplings** (generated client, routes, nav) are non-C# and covered by the codegen sequencing (Task 3) — outside codelens scope.

Every caller of every changed symbol is covered by a task in this plan.

---

## Task 1: Backend — `ListApis` filter predicates (query + handler + unit tests)

**Files:**
- Modify: `src/Modules/Catalog/Kartova.Catalog.Application/ListApisQuery.cs`
- Modify: `src/Modules/Catalog/Kartova.Catalog.Infrastructure/ListApisHandler.cs`
- Test (create): `src/Modules/Catalog/Kartova.Catalog.Tests/ListApisHandlerFilterTests.cs`

**Interfaces:**
- Produces: `ListApisQuery(ApiSortField SortBy, SortOrder SortOrder, string? Cursor, int Limit, Guid[] TeamId, ApiStyle[] Style, string? DisplayNameContains = null)`.
- Consumes: `ListApisHandler.Handle(ListApisQuery, CatalogDbContext, CancellationToken)` (unchanged signature); `Api.Create(...)`; `ApiStyle {Rest,Grpc,GraphQL}`; `LikeEscaping.EscapeLike`.

- [ ] **Step 1: Write the failing filter tests**

Create `src/Modules/Catalog/Kartova.Catalog.Tests/ListApisHandlerFilterTests.cs` (mirror `ListServicesHandlerFilterTests`, using EF InMemory):

```csharp
using Kartova.Catalog.Application;
using Kartova.Catalog.Contracts;
using Kartova.Catalog.Domain;
using Kartova.Catalog.Infrastructure;
using Kartova.SharedKernel.Identity;
using Kartova.SharedKernel.Multitenancy;
using Kartova.SharedKernel.Pagination;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using DomainApi = Kartova.Catalog.Domain.Api;

namespace Kartova.Catalog.Tests;

/// <summary>Unit-tier tests for the teamId / style / displayNameContains predicates in
/// <see cref="ListApisHandler"/> (ADR-0107). EF Core InMemory provider — no database.
/// Empty filter ⇒ no predicate (show all). Non-empty ⇒ narrows + is encoded in the cursor f-map.</summary>
[TestClass]
public class ListApisHandlerFilterTests
{
    private static readonly TenantId Tenant = new(Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001"));
    private static readonly Guid Creator = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001");
    private static readonly Guid TeamA = Guid.Parse("cccccccc-0000-0000-0000-000000000001");
    private static readonly Guid TeamB = Guid.Parse("cccccccc-0000-0000-0000-000000000002");
    private static readonly DateTimeOffset BaseTime = new(2026, 7, 4, 12, 0, 0, TimeSpan.Zero);

    private static DomainApi MakeApi(string displayName, Guid teamId, ApiStyle style, int minute = 0) =>
        DomainApi.Create(displayName, "test api", style, "v1", specUrl: null,
            createdByUserId: Creator, teamId: teamId, tenantId: Tenant, createdAt: BaseTime.AddMinutes(minute));

    private static async Task<CatalogDbContext> BuildAsync(params DomainApi[] apis)
    {
        var options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var seed = new CatalogDbContext(options);
        seed.Apis.AddRange(apis);
        await seed.SaveChangesAsync();
        return new CatalogDbContext(options);
    }

    private static IUserDirectory NoOpDirectory()
    {
        var d = Substitute.For<IUserDirectory>();
        d.GetManyAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, UserDisplayInfo>());
        return d;
    }

    private static ListApisQuery Query(Guid[]? teamId = null, ApiStyle[]? style = null, string? name = null) =>
        new(ApiSortField.DisplayName, SortOrder.Asc, Cursor: null, Limit: 50,
            TeamId: teamId ?? Array.Empty<Guid>(),
            Style: style ?? Array.Empty<ApiStyle>(),
            DisplayNameContains: name);

    [TestMethod]
    public async Task No_filters_returns_all()
    {
        await using var db = await BuildAsync(
            MakeApi("Alpha", TeamA, ApiStyle.Rest, 0), MakeApi("Beta", TeamB, ApiStyle.Grpc, 1));
        var page = await new ListApisHandler(NoOpDirectory()).Handle(Query(), db, CancellationToken.None);
        Assert.AreEqual(2, page.Items.Count);
    }

    [TestMethod]
    public async Task TeamId_filter_narrows_to_team()
    {
        await using var db = await BuildAsync(
            MakeApi("Alpha", TeamA, ApiStyle.Rest, 0), MakeApi("Beta", TeamB, ApiStyle.Grpc, 1));
        var page = await new ListApisHandler(NoOpDirectory())
            .Handle(Query(teamId: new[] { TeamB }), db, CancellationToken.None);
        Assert.AreEqual(1, page.Items.Count);
        Assert.AreEqual("Beta", page.Items.Single().DisplayName);
    }

    [TestMethod]
    public async Task Style_filter_narrows_to_style()
    {
        await using var db = await BuildAsync(
            MakeApi("Alpha", TeamA, ApiStyle.Rest, 0),
            MakeApi("Beta", TeamA, ApiStyle.Grpc, 1),
            MakeApi("Gamma", TeamA, ApiStyle.GraphQL, 2));
        var page = await new ListApisHandler(NoOpDirectory())
            .Handle(Query(style: new[] { ApiStyle.Grpc, ApiStyle.GraphQL }), db, CancellationToken.None);
        CollectionAssert.AreEquivalent(new[] { "Beta", "Gamma" }, page.Items.Select(i => i.DisplayName).ToList());
    }

    [TestMethod]
    public async Task Style_filter_excludes_non_matching()
    {
        await using var db = await BuildAsync(MakeApi("Alpha", TeamA, ApiStyle.Rest, 0));
        var page = await new ListApisHandler(NoOpDirectory())
            .Handle(Query(style: new[] { ApiStyle.GraphQL }), db, CancellationToken.None);
        Assert.AreEqual(0, page.Items.Count, "Style:[GraphQL] must exclude the Rest api");
    }

    [TestMethod]
    public async Task DisplayNameContains_is_case_insensitive_substring()
    {
        await using var db = await BuildAsync(
            MakeApi("Orders API", TeamA, ApiStyle.Rest, 0), MakeApi("Payments", TeamA, ApiStyle.Rest, 1));
        var page = await new ListApisHandler(NoOpDirectory())
            .Handle(Query(name: "order"), db, CancellationToken.None);
        Assert.AreEqual(1, page.Items.Count);
        Assert.AreEqual("Orders API", page.Items.Single().DisplayName);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail to compile**

Run: `cmd //c "dotnet test src/Modules/Catalog/Kartova.Catalog.Tests/Kartova.Catalog.Tests.csproj --filter FullyQualifiedName~ListApisHandlerFilterTests"`
Expected: FAIL — `ListApisQuery` has no `TeamId`/`Style`/`DisplayNameContains` (compile error).

- [ ] **Step 3: Extend `ListApisQuery`**

Replace the record in `ListApisQuery.cs` (keep the file's `using`s; update the summary):

```csharp
using Kartova.Catalog.Contracts;
using Kartova.Catalog.Domain;
using Kartova.SharedKernel.Pagination;

namespace Kartova.Catalog.Application;

/// <summary>List APIs visible to the current tenant (RLS-filtered), cursor-paginated (ADR-0095).
/// <para><paramref name="TeamId"/> — ADR-0107 multi-select team filter (<c>Array.Contains(column) → = ANY(@p)</c>).
/// Empty ⇒ no predicate. Encoded in the cursor f-map (sorted <c>Guid.ToString("D")</c>) when non-empty.</para>
/// <para><paramref name="Style"/> — ADR-0107 multi-select style filter (enum-in-set). Empty ⇒ no predicate.
/// Encoded in the f-map (sorted enum names) when non-empty.</para>
/// <para><paramref name="DisplayNameContains"/> — ADR-0107 substring filter (ILIKE + backslash escape).
/// null/blank ⇒ no predicate + no f-map key; trimmed when present.</para></summary>
public sealed record ListApisQuery(
    ApiSortField SortBy,
    SortOrder SortOrder,
    string? Cursor,
    int Limit,
    Guid[] TeamId,
    ApiStyle[] Style,
    string? DisplayNameContains = null);
```

- [ ] **Step 4: Implement the filter predicates + f-map in `ListApisHandler`**

Replace the body of `Handle` in `ListApisHandler.cs` (keep the class/ctor; add `using Kartova.SharedKernel.Postgres.Pagination;` if not present — `LikeEscaping` lives there, as in `ListServicesHandler`):

```csharp
    public async Task<CursorPage<ApiResponse>> Handle(ListApisQuery q, CatalogDbContext db, CancellationToken ct)
    {
        var spec = ApiSortSpecs.Resolve(q.SortBy);

        // Apply filters BEFORE pagination so a hidden row never becomes a cursor boundary
        // (same invariant as ListServicesHandler).
        IQueryable<DomainApi> source = db.Apis;

        if (q.TeamId.Length > 0)
            source = source.Where(a => q.TeamId.Contains(a.TeamId));   // = ANY(@p)

        if (q.Style.Length > 0)
            source = source.Where(a => q.Style.Contains(a.Style));     // = ANY(@p)

        if (q.DisplayNameContains is { } name)
        {
            var pattern = $"%{LikeEscaping.EscapeLike(name)}%";
            source = source.Where(a => EF.Functions.ILike(a.DisplayName, pattern, "\\"));
        }

        // Only non-empty dimensions are encoded so the cursor stays canonical and a
        // mid-pagination change trips CursorFilterMismatchException.
        Dictionary<string, string>? filters = null;
        if (q.TeamId.Length > 0 || q.Style.Length > 0 || q.DisplayNameContains is not null)
        {
            filters = new Dictionary<string, string>(StringComparer.Ordinal);
            if (q.TeamId.Length > 0)
                filters["teamId"] = string.Join(",", q.TeamId.Select(g => g.ToString("D")).Order());
            if (q.Style.Length > 0)
                filters["style"] = string.Join(",", q.Style.Select(s => s.ToString()).Order());
            if (q.DisplayNameContains is { } dn)
                filters["displayNameContains"] = dn;
        }

        var page = await source
            .ToCursorPagedAsync(
                spec, q.SortOrder, q.Cursor, q.Limit,
                ApiSortSpecs.IdSelector, IdExtractor, ct,
                expectedFilters: filters);

        var creatorIds = new HashSet<Guid>(page.Items.Select(a => a.CreatedByUserId));
        var creators = await directory.GetManyAsync(creatorIds, ct);

        var items = page.Items
            .Select(a =>
            {
                var resp = a.ToResponse();
                return creators.TryGetValue(a.CreatedByUserId, out var creator)
                    ? resp with { CreatedBy = creator }
                    : resp;
            })
            .ToList();
        return new CursorPage<ApiResponse>(items, page.NextCursor, page.PrevCursor);
    }
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `cmd //c "dotnet test src/Modules/Catalog/Kartova.Catalog.Tests/Kartova.Catalog.Tests.csproj --filter FullyQualifiedName~ListApisHandlerFilterTests"`
Expected: PASS (5/5).

- [ ] **Step 6: Commit**

```powershell
git add src/Modules/Catalog/Kartova.Catalog.Application/ListApisQuery.cs src/Modules/Catalog/Kartova.Catalog.Infrastructure/ListApisHandler.cs src/Modules/Catalog/Kartova.Catalog.Tests/ListApisHandlerFilterTests.cs
git commit -m "feat(catalog): ListApis team/style/name filter predicates (E-02.F-03 FU-9)" -m "Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 2: Backend — `ListApisAsync` endpoint binding + `InvalidStyleFilter` + real-seam tests

**Files:**
- Modify: `src/Kartova.SharedKernel.AspNetCore/ProblemTypes.cs`
- Modify: `src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogEndpointDelegates.cs` (`ListApisAsync`, ~line 583-603)
- Test: `src/Modules/Catalog/Kartova.Catalog.IntegrationTests/ListApisPaginationTests.cs` (extend)

**Interfaces:**
- Consumes: `ListApisQuery` (Task 1); `CursorListBinding.Bind<ApiSortField>`; `ProblemTypes.InvalidStyleFilter` (new); `ApiStyle`.
- Produces: `GET /api/v1/catalog/apis?...&displayNameContains=&style=&teamId=` — repeated `style` (camelCase enum names) and `teamId` (Guid) params; 400 `invalid-style-filter` for unknown/numeric style tokens.

- [ ] **Step 1: Add the failing real-seam filter tests**

Append to `ListApisPaginationTests.cs` (uses the existing `Seed`/`SeedWithStyle`/`Fx` helpers):

```csharp
    [TestMethod]
    public async Task List_filters_by_displayNameContains()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Api Name Filter Team");
        var unique = Guid.NewGuid().ToString("N");
        await Seed(client, teamId, $"filt-{unique}-orders");
        await Seed(client, teamId, $"filt-{unique}-payments");

        var resp = await client.GetAsync($"/api/v1/catalog/apis?displayNameContains={unique}-ORDERS&limit=200");
        Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
        var page = await resp.Content.ReadFromJsonAsync<CursorPage<ApiResponse>>(KartovaApiFixtureBase.WireJson);
        var names = page!.Items.Select(i => i.DisplayName).Where(n => n.StartsWith($"filt-{unique}", StringComparison.Ordinal)).ToList();
        CollectionAssert.AreEqual(new[] { $"filt-{unique}-orders" }, names, "case-insensitive substring must match only the one row");
    }

    [TestMethod]
    public async Task List_filters_by_style()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Api Style Filter Team");
        var unique = Guid.NewGuid().ToString("N");
        await SeedWithStyle(client, teamId, $"sfilt-{unique}-r", ApiStyle.Rest, "v1");
        await SeedWithStyle(client, teamId, $"sfilt-{unique}-g", ApiStyle.Grpc, "v1");

        var resp = await client.GetAsync($"/api/v1/catalog/apis?style=grpc&limit=200");
        Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
        var page = await resp.Content.ReadFromJsonAsync<CursorPage<ApiResponse>>(KartovaApiFixtureBase.WireJson);
        var seeded = page!.Items.Where(i => i.DisplayName.StartsWith($"sfilt-{unique}", StringComparison.Ordinal)).ToList();
        Assert.IsTrue(seeded.All(i => i.Style == ApiStyle.Grpc), "style=grpc must return only Grpc apis");
        Assert.IsTrue(seeded.Any(i => i.DisplayName == $"sfilt-{unique}-g"));
    }

    [TestMethod]
    public async Task List_rejects_unknown_style_filter_with_400()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var resp = await client.GetAsync("/api/v1/catalog/apis?style=bogus");
        Assert.AreEqual(HttpStatusCode.BadRequest, resp.StatusCode);
        var body = await resp.Content.ReadAsStringAsync();
        StringAssert.Contains(body, "invalid-style-filter");
    }

    [TestMethod]
    public async Task List_style_filter_paginates_and_survives_cursor_roundtrip()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Api Style Page Team");
        var unique = Guid.NewGuid().ToString("N");
        await SeedWithStyle(client, teamId, $"spg-{unique}-1", ApiStyle.Rest, "v1");
        await SeedWithStyle(client, teamId, $"spg-{unique}-2", ApiStyle.Rest, "v1");

        var firstResp = await client.GetAsync("/api/v1/catalog/apis?style=rest&sortBy=displayName&sortOrder=asc&limit=1");
        Assert.AreEqual(HttpStatusCode.OK, firstResp.StatusCode);
        var first = await firstResp.Content.ReadFromJsonAsync<CursorPage<ApiResponse>>(KartovaApiFixtureBase.WireJson);
        Assert.IsNotNull(first!.NextCursor);
        // Same filter on the follow-up page ⇒ 200 (f-map matches).
        var nextResp = await client.GetAsync(
            $"/api/v1/catalog/apis?style=rest&sortBy=displayName&sortOrder=asc&limit=1&cursor={Uri.EscapeDataString(first.NextCursor!)}");
        Assert.AreEqual(HttpStatusCode.OK, nextResp.StatusCode);
        // Changing the filter mid-pagination ⇒ 400 cursor-filter-mismatch.
        var mismatchResp = await client.GetAsync(
            $"/api/v1/catalog/apis?style=grpc&sortBy=displayName&sortOrder=asc&limit=1&cursor={Uri.EscapeDataString(first.NextCursor!)}");
        Assert.AreEqual(HttpStatusCode.BadRequest, mismatchResp.StatusCode);
    }
```

- [ ] **Step 2: Run to verify failure**

Run: `cmd //c "dotnet test src/Modules/Catalog/Kartova.Catalog.IntegrationTests/Kartova.Catalog.IntegrationTests.csproj --filter FullyQualifiedName~ListApisPaginationTests"`
Expected: FAIL — style/name params ignored (filter tests fail) and `invalid-style-filter` not produced. (Requires Docker/Testcontainers.)

- [ ] **Step 3: Add the `InvalidStyleFilter` problem type**

In `src/Kartova.SharedKernel.AspNetCore/ProblemTypes.cs`, add next to `InvalidHealthFilter` (match the existing const style/URL prefix — copy the `InvalidHealthFilter` line and swap `health`→`style`):

```csharp
    public const string InvalidStyleFilter = "https://kartova.dev/problems/invalid-style-filter";
```

(Use the exact base URL the sibling `InvalidHealthFilter` uses — read the line and mirror it.)

- [ ] **Step 4: Bind the filter params in `ListApisAsync`**

Replace `ListApisAsync` in `CatalogEndpointDelegates.cs` (mirror `ListServicesAsync`; `HealthStatus`/`ProblemTypes` already imported at top):

```csharp
    internal static async Task<IResult> ListApisAsync(
        [FromQuery] string? sortBy,
        [FromQuery] string? sortOrder,
        [FromQuery] string? cursor,
        [FromQuery] string? limit,
        [FromQuery] string? displayNameContains,
        [FromQuery] Guid[]? teamId,
        [FromQuery] string[]? style,
        ListApisHandler handler,
        CatalogDbContext db,
        CancellationToken ct)
    {
        var (parsedSortBy, parsedSortOrder, effectiveLimit) =
            CursorListBinding.Bind<ApiSortField>(sortBy, sortOrder, limit, ApiSortSpecs.AllowedFieldNames);

        // Repeated ?style= tokens (wire form: camelCase enum names). Reject numeric/unknown
        // tokens with 400 invalid-style-filter so the contract stays names-only (mirrors the
        // health token parse in ListServicesAsync). HashSet de-dups so the f-map stays canonical.
        var styles = new HashSet<Kartova.Catalog.Domain.ApiStyle>();
        foreach (var raw in style ?? Array.Empty<string>())
        {
            if (int.TryParse(raw, out _)
                || !Enum.TryParse<Kartova.Catalog.Domain.ApiStyle>(raw, ignoreCase: true, out var parsed)
                || !Enum.IsDefined(parsed))
            {
                return Results.Problem(
                    type: ProblemTypes.InvalidStyleFilter,
                    title: "Invalid style filter",
                    detail: $"'{raw}' is not a valid API style. Expected one of: rest, grpc, graphQL.",
                    statusCode: StatusCodes.Status400BadRequest);
            }
            styles.Add(parsed);
        }

        var name = string.IsNullOrWhiteSpace(displayNameContains) ? null : displayNameContains.Trim();

        var query = new ListApisQuery(
            SortBy: parsedSortBy ?? ApiSortField.DisplayName,
            SortOrder: parsedSortOrder ?? SortOrder.Asc,
            Cursor: cursor,
            Limit: effectiveLimit,
            TeamId: (teamId ?? Array.Empty<Guid>()).ToHashSet().ToArray(),
            Style: styles.ToArray(),
            DisplayNameContains: name);

        var page = await handler.Handle(query, db, ct);
        return Results.Ok(page);
    }
```

> If `ApiStyle` is already aliased/imported at the top of the file, drop the `Kartova.Catalog.Domain.` qualifier. Otherwise add `using ApiStyle = Kartova.Catalog.Domain.ApiStyle;` beside the other domain aliases.

- [ ] **Step 5: Run the real-seam tests to verify they pass**

Run: `cmd //c "dotnet test src/Modules/Catalog/Kartova.Catalog.IntegrationTests/Kartova.Catalog.IntegrationTests.csproj --filter FullyQualifiedName~ListApisPaginationTests"`
Expected: PASS (existing + 4 new). If a Docker named-pipe TimeoutException appears, re-run the assembly in isolation before treating it as red (known flake).

- [ ] **Step 6: Full backend build (warnings-as-errors)**

Run: `cmd //c "dotnet build Kartova.slnx -warnaserror"`
Expected: 0 warnings, 0 errors.

- [ ] **Step 7: Commit**

```powershell
git add src/Kartova.SharedKernel.AspNetCore/ProblemTypes.cs src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogEndpointDelegates.cs src/Modules/Catalog/Kartova.Catalog.IntegrationTests/ListApisPaginationTests.cs
git commit -m "feat(catalog): bind team/style/name filters on GET /apis + invalid-style-filter (E-02.F-03 FU-9)" -m "Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 3: Codegen bridge — regenerate OpenAPI snapshot + client

**Files:**
- Modify: `web/openapi-snapshot.json` (regenerated)

**Interfaces:**
- Produces: `operations["ListApis"]["parameters"]["query"]` now includes `displayNameContains`, `teamId`, `style`; `components["schemas"]["ApiResponse"]` (already present from S-01). These types back Tasks 4–9.

- [ ] **Step 1: Rebuild the API image / start the live API**

The snapshot regenerates from the live API (predev/prebuild). Ensure the API reflects the Task 2 change: `cmd //c "docker compose build api"` then bring the stack up (or run the API locally). See project memory "rebuild API image to expose new endpoints".

- [ ] **Step 2: Regenerate the snapshot + generated client**

From `web/`: `cmd //c "npm run codegen"` (or the predev step). This rewrites `web/openapi-snapshot.json` and the gitignored generated client.

- [ ] **Step 3: Confirm the new query params landed**

Run: `Select-String -Path web/openapi-snapshot.json -Pattern "displayNameContains|\"style\"" | Select-Object -First 5` — confirm the `/api/v1/catalog/apis` GET lists `style`, `teamId`, `displayNameContains` query params. (Param-order churn is cosmetic — see project memory; keep the regenerated file.)

- [ ] **Step 4: Commit the snapshot**

```powershell
git add web/openapi-snapshot.json
git commit -m "chore(web): regenerate openapi snapshot for /apis filters (E-02.F-03 FU-9)" -m "Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 4: Frontend — `registerApi` zod schema + style constants

**Files:**
- Create: `web/src/features/catalog/schemas/registerApi.ts`
- Test: `web/src/features/catalog/schemas/__tests__/registerApi.test.ts`

**Interfaces:**
- Produces: `registerApiSchema`, `RegisterApiInput`, `API_STYLES` (`readonly ApiStyleValue[]`), `API_STYLE_LABEL` (`Record<ApiStyleValue,string>`), `ApiStyleValue` type.
- Consumes: `components["schemas"]["ApiResponse"]["style"]` (from Task 3 codegen).

- [ ] **Step 1: Write the failing schema test**

`web/src/features/catalog/schemas/__tests__/registerApi.test.ts`:

```ts
import { describe, it, expect } from "vitest";
import { registerApiSchema, API_STYLES, API_STYLE_LABEL } from "../registerApi";

const valid = {
  displayName: "Orders API",
  description: "Order management",
  style: "rest" as const,
  version: "v1",
  specUrl: "https://example.com/openapi.json",
  teamId: "11111111-1111-1111-1111-111111111111",
};

describe("registerApiSchema", () => {
  it("accepts a valid payload", () => {
    expect(registerApiSchema.safeParse(valid).success).toBe(true);
  });
  it("accepts an omitted/empty specUrl", () => {
    expect(registerApiSchema.safeParse({ ...valid, specUrl: "" }).success).toBe(true);
  });
  it("rejects a relative specUrl", () => {
    expect(registerApiSchema.safeParse({ ...valid, specUrl: "/openapi.json" }).success).toBe(false);
  });
  it("rejects an empty displayName", () => {
    expect(registerApiSchema.safeParse({ ...valid, displayName: "" }).success).toBe(false);
  });
  it("rejects an empty version", () => {
    expect(registerApiSchema.safeParse({ ...valid, version: "" }).success).toBe(false);
  });
  it("exposes all three styles with labels", () => {
    expect(API_STYLES).toEqual(["rest", "grpc", "graphQL"]);
    expect(API_STYLE_LABEL.graphQL).toBe("GraphQL");
  });
});
```

- [ ] **Step 2: Run to verify failure**

Run: `cmd //c "cd web && npx vitest run src/features/catalog/schemas/__tests__/registerApi.test.ts"`
Expected: FAIL — module not found.

- [ ] **Step 3: Write the schema**

`web/src/features/catalog/schemas/registerApi.ts`:

```ts
import { z } from "zod";
import type { components } from "@/generated/openapi";

/** Wire-shape style union, sourced from the OpenAPI codegen (single source of truth). */
type ApiStyleValue = components["schemas"]["ApiResponse"]["style"];

/** Style values in wire form (camelCase per JsonStringEnumConverter + JsonNamingPolicy.CamelCase).
 *  The `satisfies` clause fails the build if any literal's casing drifts from the generated client. */
export const API_STYLES = ["rest", "grpc", "graphQL"] as const satisfies readonly ApiStyleValue[];

/** Human-friendly labels for the style <select>, table badge, and detail page.
 *  Typed as a total Record so a missing/extra key fails `tsc`. */
export const API_STYLE_LABEL: Record<ApiStyleValue, string> = {
  rest: "REST",
  grpc: "gRPC",
  graphQL: "GraphQL",
};

function isAbsoluteUrl(value: string): boolean {
  try {
    const u = new URL(value);
    return !!u.protocol && !!u.host;
  } catch {
    return false;
  }
}

export const registerApiSchema = z.object({
  displayName: z.string().min(1, "Display Name must not be empty").max(128, "Display Name must be at most 128 characters"),
  description: z.string().min(1, "Description is required").max(4096, "Description must be at most 4096 characters"),
  style: z.enum(API_STYLES),
  version: z.string().min(1, "Version must not be empty").max(64, "Version must be at most 64 characters"),
  // Optional: empty string ⇒ omitted; when present must be an absolute URL (mirrors Api.Create ValidateSpecUrl).
  specUrl: z
    .string()
    .max(2048, "Spec URL must be at most 2048 characters")
    .refine((v) => v === "" || isAbsoluteUrl(v), "Spec URL must be an absolute URL (include a scheme and host)")
    .optional()
    .or(z.literal("")),
  teamId: z.string().uuid("Team is required"),
});

export type RegisterApiInput = z.infer<typeof registerApiSchema>;
export type { ApiStyleValue };
```

- [ ] **Step 4: Run to verify pass**

Run: `cmd //c "cd web && npx vitest run src/features/catalog/schemas/__tests__/registerApi.test.ts"`
Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add web/src/features/catalog/schemas/registerApi.ts web/src/features/catalog/schemas/__tests__/registerApi.test.ts
git commit -m "feat(web): registerApi zod schema + style constants (E-02.F-03 FU-9)" -m "Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 5: Frontend — `api/apis.ts` (list/detail/register hooks)

**Files:**
- Create: `web/src/features/catalog/api/apis.ts`
- Test: `web/src/features/catalog/api/__tests__/apis.test.tsx`

**Interfaces:**
- Consumes: `apiClient`, `useCursorList`, `throwWithStatus`, `unwrapData`, `RegisterApiInput` (Task 4), generated `components`/`operations` (Task 3).
- Produces: `useApisList(params)`, `useApi(id)`, `useRegisterApi()`, `apiKeys`, `ApiResponse` type; `ApisListParams` type `{ sortBy, sortOrder, limit?, style?: string[], teamId?: string[], displayNameContains?: string }`.

- [ ] **Step 1: Write the failing hook test**

`web/src/features/catalog/api/__tests__/apis.test.tsx` (mirror `services.test.tsx` — mock `./client`, assert the query params passed to `apiClient.GET`):

```tsx
import { describe, it, expect, vi, beforeEach } from "vitest";
import { renderHook, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import React from "react";

const getMock = vi.fn();
vi.mock("../client", () => ({ apiClient: { GET: (...a: unknown[]) => getMock(...a) } }));

import { useApisList } from "../apis";

function wrapper({ children }: { children: React.ReactNode }) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return <QueryClientProvider client={qc}>{children}</QueryClientProvider>;
}

beforeEach(() => {
  getMock.mockReset();
  getMock.mockResolvedValue({ data: { items: [], nextCursor: null, prevCursor: null }, error: undefined });
});

describe("useApisList", () => {
  it("passes style/teamId/displayNameContains only when non-empty", async () => {
    renderHook(() => useApisList({
      sortBy: "displayName", sortOrder: "asc",
      style: ["rest"], teamId: ["t1"], displayNameContains: "ord",
    }), { wrapper });
    await waitFor(() => expect(getMock).toHaveBeenCalled());
    const query = getMock.mock.calls[0][1].params.query;
    expect(query.style).toEqual(["rest"]);
    expect(query.teamId).toEqual(["t1"]);
    expect(query.displayNameContains).toBe("ord");
  });

  it("omits empty filter arrays", async () => {
    renderHook(() => useApisList({ sortBy: "displayName", sortOrder: "asc", style: [], teamId: [] }), { wrapper });
    await waitFor(() => expect(getMock).toHaveBeenCalled());
    const query = getMock.mock.calls[0][1].params.query;
    expect(query.style).toBeUndefined();
    expect(query.teamId).toBeUndefined();
    expect(query.displayNameContains).toBeUndefined();
  });
});
```

- [ ] **Step 2: Run to verify failure**

Run: `cmd //c "cd web && npx vitest run src/features/catalog/api/__tests__/apis.test.tsx"`
Expected: FAIL — module not found.

- [ ] **Step 3: Write the API client hooks**

`web/src/features/catalog/api/apis.ts` (mirror `services.ts`):

```ts
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { apiClient } from "./client";
import { useCursorList } from "@/lib/list/useCursorList";
import { throwWithStatus, unwrapData } from "@/shared/api/openapi-fetch-helpers";
import type { RegisterApiInput } from "../schemas/registerApi";
import type { components, operations } from "@/generated/openapi";

type ApiResponse = components["schemas"]["ApiResponse"];
type ListApisQuery = NonNullable<operations["ListApis"]["parameters"]["query"]>;

type ApisListParams = {
  sortBy: NonNullable<ListApisQuery["sortBy"]>;      // "displayName" | "style" | "version" | "createdAt"
  sortOrder: NonNullable<ListApisQuery["sortOrder"]>;
  limit?: number;
  /** ADR-0107 style multi-select (wire values rest|grpc|graphQL). Empty/undefined ⇒ omitted ⇒ show all. */
  style?: string[];
  /** ADR-0107 team multi-select (team ids). Empty/undefined ⇒ omitted ⇒ show all. */
  teamId?: string[];
  displayNameContains?: string;
};

export const apiKeys = {
  all: ["apis"] as const,
  list: (params?: ApisListParams) =>
    params ? ([...apiKeys.all, "list", params] as const) : ([...apiKeys.all, "list"] as const),
  detail: (id: string) => [...apiKeys.all, "detail", id] as const,
};

export function useApisList(params: ApisListParams) {
  return useCursorList<ApiResponse>({
    queryKey: apiKeys.list(params),
    fetchPage: async (cursor) => {
      const { data, error } = await apiClient.GET("/api/v1/catalog/apis", {
        params: {
          query: {
            sortBy: params.sortBy,
            sortOrder: params.sortOrder,
            limit: params.limit ?? 50,
            cursor,
            ...(params.style?.length ? { style: params.style } : {}),
            ...(params.teamId?.length ? { teamId: params.teamId } : {}),
            ...(params.displayNameContains ? { displayNameContains: params.displayNameContains } : {}),
          },
        },
      });
      if (error) throw error;
      return unwrapData(data);
    },
  });
}

export function useApi(id: string) {
  return useQuery({
    queryKey: apiKeys.detail(id),
    enabled: id !== "",
    queryFn: async () => {
      const { data, error } = await apiClient.GET("/api/v1/catalog/apis/{id}", { params: { path: { id } } });
      if (error) throw error;
      return unwrapData(data);
    },
  });
}

export function useRegisterApi() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (input: RegisterApiInput) => {
      const { data, error, response } = await apiClient.POST("/api/v1/catalog/apis", { body: input });
      if (error) throwWithStatus(error, response);
      return unwrapData(data);
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: apiKeys.all });
    },
  });
}

export type { ApiResponse };
```

> If `POST /apis` body typing rejects an empty-string `specUrl`, map `specUrl: input.specUrl || undefined` (and `version`/`style` pass through) in `mutationFn` to match the `RegisterApiRequest` contract.

- [ ] **Step 4: Run to verify pass**

Run: `cmd //c "cd web && npx vitest run src/features/catalog/api/__tests__/apis.test.tsx"`
Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add web/src/features/catalog/api/apis.ts web/src/features/catalog/api/__tests__/apis.test.tsx
git commit -m "feat(web): apis query/mutation hooks with filter params (E-02.F-03 FU-9)" -m "Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 6: Frontend — `ApisTable` component

**Files:**
- Create: `web/src/features/catalog/components/ApisTable.tsx`
- Test: `web/src/features/catalog/components/__tests__/ApisTable.test.tsx`

**Interfaces:**
- Consumes: `ApiResponse` (Task 5), `API_STYLE_LABEL` (Task 4), `CursorListResult`, `SortDirection`, table primitives.
- Produces: `ApisTable` with props `{ list, sortBy, sortOrder, onSortChange, teamNameById }`; `SortField = "displayName" | "style" | "version" | "createdAt"`.

- [ ] **Step 1: Write the failing table test**

`web/src/features/catalog/components/__tests__/ApisTable.test.tsx` (mirror `ServicesTable.test.tsx`; assert rowheader guard + style label + sortable headers):

```tsx
import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { ApisTable } from "../ApisTable";
import type { ApiResponse } from "@/features/catalog/api/apis";

const item: ApiResponse = {
  id: "a1", tenantId: "t", displayName: "Orders API", description: "d",
  style: "graphQL", version: "v2", specUrl: null, teamId: "team1",
  createdByUserId: "u1", createdAt: "2026-07-04T10:00:00Z", createdBy: null,
} as ApiResponse;

const baseList = {
  items: [item], isLoading: false, isError: false, error: null,
  hasNext: false, hasPrev: false, goNext: vi.fn(), goPrev: vi.fn(), reset: vi.fn(),
};

function renderTable() {
  return render(
    <MemoryRouter>
      <ApisTable list={baseList as never} sortBy="displayName" sortOrder="asc"
        onSortChange={vi.fn()} teamNameById={new Map([["team1", "Platform"]])} />
    </MemoryRouter>,
  );
}

describe("ApisTable", () => {
  it("renders at least one rowheader (ADR-0084 blank-page guard)", () => {
    renderTable();
    expect(screen.getAllByRole("rowheader").length).toBeGreaterThan(0);
  });
  it("shows the human style label and the team name", () => {
    renderTable();
    expect(screen.getByText("GraphQL")).toBeInTheDocument();
    expect(screen.getByText("Platform")).toBeInTheDocument();
  });
});
```

- [ ] **Step 2: Run to verify failure**

Run: `cmd //c "cd web && npx vitest run src/features/catalog/components/__tests__/ApisTable.test.tsx"`
Expected: FAIL — module not found.

- [ ] **Step 3: Write the table**

`web/src/features/catalog/components/ApisTable.tsx` (mirror `ServicesTable.tsx`; columns displayName/style/version/team/createdBy/createdAt, all sort-allowlist cols use `SortableHead`):

```tsx
import { Link } from "react-router-dom";
import { Table } from "@/components/application/table/table";
import { Card, CardContent } from "@/components/base/card/card";
import { SortableHead, TablePager, TableSkeleton, fromSort, toSort } from "@/components/application/data-table/data-table";
import { CreatedByLink } from "@/features/users/components/CreatedByLink";
import { API_STYLE_LABEL } from "@/features/catalog/schemas/registerApi";
import type { CursorListResult, SortDirection } from "@/lib/list/types";
import type { ApiResponse } from "@/features/catalog/api/apis";

type SortField = "displayName" | "style" | "version" | "createdAt";
const SORT_FIELDS: readonly SortField[] = ["displayName", "style", "version", "createdAt"];

interface Props {
  list: CursorListResult<ApiResponse>;
  sortBy: SortField;
  sortOrder: SortDirection;
  onSortChange: (field: SortField, order: SortDirection) => void;
  teamNameById: Map<string, string>;
}

export function ApisTable({ list, sortBy, sortOrder, onSortChange, teamNameById }: Props) {
  if (list.isLoading) {
    return (
      <Table aria-label="APIs">
        <Table.Header>
          <Table.Head id="displayName" isRowHeader>Name</Table.Head>
          <Table.Head id="style">Style</Table.Head>
          <Table.Head id="version">Version</Table.Head>
          <Table.Head id="team">Team</Table.Head>
          <Table.Head id="createdBy">Created by</Table.Head>
          <Table.Head id="createdAt">Created</Table.Head>
        </Table.Header>
        <TableSkeleton rows={5} cells={6} />
      </Table>
    );
  }

  if (list.items.length === 0) {
    return (
      <Card className="mx-auto max-w-md text-center">
        <CardContent className="space-y-2 p-8">
          <p className="text-base font-medium text-primary">No APIs yet</p>
          <p className="text-sm text-tertiary">
            Use the &quot;+ Register API&quot; button in the header to add your first one.
          </p>
        </CardContent>
      </Card>
    );
  }

  const handleSortChange = (descriptor: Parameters<typeof toSort>[0]) => {
    const { field, order } = toSort(descriptor);
    if ((SORT_FIELDS as readonly string[]).includes(field)) {
      onSortChange(field as SortField, order);
    }
  };

  return (
    <div className="overflow-hidden rounded-xl bg-primary shadow-xs ring-1 ring-secondary">
      <Table aria-label="APIs" sortDescriptor={fromSort(sortBy, sortOrder)} onSortChange={handleSortChange}>
        <Table.Header>
          <SortableHead id="displayName" isRowHeader>Name</SortableHead>
          <SortableHead id="style">Style</SortableHead>
          <SortableHead id="version">Version</SortableHead>
          <Table.Head id="team">Team</Table.Head>
          <Table.Head id="createdBy">Created by</Table.Head>
          <SortableHead id="createdAt">Created</SortableHead>
        </Table.Header>
        <Table.Body>
          {list.items.map((api) => (
            <Table.Row key={api.id} id={api.id}>
              <Table.Cell>
                <Link to={`/catalog/apis/${api.id}`} className="block font-medium text-primary hover:underline">
                  {api.displayName}
                </Link>
              </Table.Cell>
              <Table.Cell className="text-sm">
                <span className="inline-flex items-center rounded-md bg-secondary px-2 py-0.5 text-xs font-medium text-secondary ring-1 ring-inset ring-secondary">
                  {API_STYLE_LABEL[api.style]}
                </span>
              </Table.Cell>
              <Table.Cell className="font-mono text-sm text-tertiary">{api.version}</Table.Cell>
              <Table.Cell className="text-sm">
                <Link to={`/teams/${api.teamId}`} className="text-primary hover:underline">
                  {teamNameById.get(api.teamId) ?? "Unknown team"}
                </Link>
              </Table.Cell>
              <Table.Cell className="text-sm">
                <CreatedByLink user={api.createdBy} />
              </Table.Cell>
              <Table.Cell className="text-sm text-tertiary">
                {api.createdAt ? new Date(api.createdAt).toLocaleDateString() : ""}
              </Table.Cell>
            </Table.Row>
          ))}
        </Table.Body>
      </Table>
      <TablePager hasPrev={list.hasPrev} hasNext={list.hasNext} onPrev={list.goPrev} onNext={list.goNext} pageSize={list.items.length} />
    </div>
  );
}
```

- [ ] **Step 4: Run to verify pass**

Run: `cmd //c "cd web && npx vitest run src/features/catalog/components/__tests__/ApisTable.test.tsx"`
Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add web/src/features/catalog/components/ApisTable.tsx web/src/features/catalog/components/__tests__/ApisTable.test.tsx
git commit -m "feat(web): ApisTable with sortable columns + style badge (E-02.F-03 FU-9)" -m "Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 7: Frontend — `RegisterApiDialog` component

**Files:**
- Create: `web/src/features/catalog/components/RegisterApiDialog.tsx`
- Test: `web/src/features/catalog/components/__tests__/RegisterApiDialog.test.tsx`

**Interfaces:**
- Consumes: `registerApiSchema`, `RegisterApiInput`, `API_STYLES`, `API_STYLE_LABEL` (Task 4); `useRegisterApi` (Task 5); `useTeamsList`; `useCurrentUser`; `applyProblemDetailsToForm`; modal/form/input primitives.
- Produces: `RegisterApiDialog` with props `{ open, onOpenChange }`.

- [ ] **Step 1: Write the failing dialog test**

`web/src/features/catalog/components/__tests__/RegisterApiDialog.test.tsx` (mirror `RegisterServiceDialog.test.tsx` — mock `useRegisterApi`/`useTeamsList`/`useCurrentUser`, fill fields, assert `mutateAsync` called with the payload):

```tsx
import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";

const mutateAsync = vi.fn().mockResolvedValue({ id: "new-api" });
vi.mock("@/features/catalog/api/apis", () => ({ useRegisterApi: () => ({ mutateAsync, isPending: false }) }));
vi.mock("@/features/teams/api/teams", () => ({
  useTeamsList: () => ({ items: [{ id: "team1", displayName: "Platform" }], isLoading: false, isError: false }),
}));
vi.mock("@/shared/auth/useCurrentUser", () => ({ useCurrentUser: () => ({ displayName: "Dev", email: "d@x.io" }) }));
vi.mock("sonner", () => ({ toast: { success: vi.fn(), error: vi.fn() } }));

import { RegisterApiDialog } from "../RegisterApiDialog";

beforeEach(() => mutateAsync.mockClear());

describe("RegisterApiDialog", () => {
  it("submits displayName/description/style/version/teamId", async () => {
    const user = userEvent.setup();
    render(<RegisterApiDialog open onOpenChange={vi.fn()} />);
    await user.type(screen.getByLabelText(/Display Name/i), "Orders API");
    await user.type(screen.getByLabelText(/Description/i), "Order mgmt");
    await user.type(screen.getByLabelText(/Version/i), "v1");
    await user.selectOptions(screen.getByTestId("register-api-team-select"), "team1");
    await user.click(screen.getByRole("button", { name: /Register API/i }));
    await waitFor(() => expect(mutateAsync).toHaveBeenCalledTimes(1));
    expect(mutateAsync.mock.calls[0][0]).toMatchObject({
      displayName: "Orders API", description: "Order mgmt", version: "v1", teamId: "team1", style: "rest",
    });
  });
});
```

- [ ] **Step 2: Run to verify failure**

Run: `cmd //c "cd web && npx vitest run src/features/catalog/components/__tests__/RegisterApiDialog.test.tsx"`
Expected: FAIL — module not found.

- [ ] **Step 3: Write the dialog**

`web/src/features/catalog/components/RegisterApiDialog.tsx` (mirror `RegisterServiceDialog.tsx`; replace endpoints editor with style `<select>` + version + specUrl inputs; RHF covers all scalar fields incl. style default `rest`):

```tsx
import { useEffect, useState } from "react";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { toast } from "sonner";

import { ModalOverlay, Modal, Dialog } from "@/components/application/modals/modal";
import { HookForm, FormField } from "@/components/base/form/hook-form";
import { Input } from "@/components/base/input/input";
import { TextArea } from "@/components/base/textarea/textarea";
import { Button } from "@/components/base/buttons/button";
import { Avatar } from "@/components/base/avatar/avatar";

import {
  registerApiSchema,
  API_STYLES,
  API_STYLE_LABEL,
  type RegisterApiInput,
} from "@/features/catalog/schemas/registerApi";
import { useRegisterApi } from "@/features/catalog/api/apis";
import { useTeamsList } from "@/features/teams/api/teams";
import { applyProblemDetailsToForm, type ProblemDetails } from "@/shared/forms/problemDetails";
import { useCurrentUser } from "@/shared/auth/useCurrentUser";
import { initialsOf } from "@/shared/auth/initials";

interface Props {
  open: boolean;
  onOpenChange: (open: boolean) => void;
}

export function RegisterApiDialog({ open, onOpenChange }: Props) {
  const user = useCurrentUser();
  const mutation = useRegisterApi();
  const teamsList = useTeamsList({ sortBy: "displayName", sortOrder: "asc", limit: 200 });
  const [selectedTeamId, setSelectedTeamId] = useState<string>("");
  const [teamError, setTeamError] = useState<string>("");

  const form = useForm<RegisterApiInput>({
    resolver: zodResolver(registerApiSchema),
    defaultValues: { displayName: "", description: "", style: "rest", version: "", specUrl: "", teamId: "" },
  });

  useEffect(() => {
    if (!open) {
      form.reset({ displayName: "", description: "", style: "rest", version: "", specUrl: "", teamId: "" });
      // eslint-disable-next-line react-hooks/set-state-in-effect
      setSelectedTeamId("");
      setTeamError("");
    }
  }, [open, form]);

  const onSubmit = form.handleSubmit(async (values) => {
    if (!selectedTeamId) {
      setTeamError("Team is required");
      return;
    }
    setTeamError("");
    const payload: RegisterApiInput = { ...values, teamId: selectedTeamId, specUrl: values.specUrl || undefined };
    try {
      await mutation.mutateAsync(payload);
      toast.success("API registered");
      onOpenChange(false);
    } catch (err) {
      const problem = err as ProblemDetails;
      const handled = applyProblemDetailsToForm(problem, (name, error) =>
        form.setError(name as Parameters<typeof form.setError>[0], error),
      );
      if (!handled) toast.error(problem.detail ?? problem.title ?? "Failed to register API");
    }
  });

  const initials = initialsOf(user?.displayName);
  const teams = teamsList.items ?? [];
  const noTeams = !teamsList.isLoading && teams.length === 0;

  return (
    <ModalOverlay isOpen={open} onOpenChange={onOpenChange} isDismissable={!mutation.isPending}>
      <Modal className="max-w-[640px]">
        <Dialog aria-label="Register API" className="bg-primary rounded-xl shadow-xl p-6 outline-none">
          <div className="w-full">
            <div className="space-y-1 mb-4">
              <h2 className="text-lg font-semibold text-primary">Register API</h2>
              <p className="text-sm text-tertiary">Add a new API to your catalog</p>
            </div>

            <HookForm form={form} onSubmit={onSubmit} className="space-y-5">
              <FormField name="displayName" control={form.control}>
                {({ field, fieldState }) => (
                  <Input label="Display Name" placeholder="Orders API"
                    hint={fieldState.error?.message ?? "Human-friendly name shown in UI."}
                    isInvalid={!!fieldState.error} isRequired {...field} />
                )}
              </FormField>
              <FormField name="description" control={form.control}>
                {({ field, fieldState }) => (
                  <TextArea label="Description" rows={3} placeholder="Short summary..."
                    hint={fieldState.error?.message} isInvalid={!!fieldState.error} isRequired {...field} />
                )}
              </FormField>

              <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
                <FormField name="style" control={form.control}>
                  {({ field }) => (
                    <div className="flex flex-col gap-1">
                      <label htmlFor="register-api-style" className="text-sm font-medium text-secondary">Style</label>
                      <select id="register-api-style" data-testid="register-api-style-select"
                        className="rounded-md border border-secondary px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-brand-500 bg-primary text-primary"
                        value={field.value} onChange={field.onChange} disabled={mutation.isPending}>
                        {API_STYLES.map((s) => (<option key={s} value={s}>{API_STYLE_LABEL[s]}</option>))}
                      </select>
                    </div>
                  )}
                </FormField>
                <FormField name="version" control={form.control}>
                  {({ field, fieldState }) => (
                    <Input label="Version" placeholder="v1" hint={fieldState.error?.message}
                      isInvalid={!!fieldState.error} isRequired {...field} />
                  )}
                </FormField>
              </div>

              <FormField name="specUrl" control={form.control}>
                {({ field, fieldState }) => (
                  <Input label="Spec URL" placeholder="https://example.com/openapi.json"
                    hint={fieldState.error?.message ?? "Optional. Absolute URL to the API spec."}
                    isInvalid={!!fieldState.error} {...field} />
                )}
              </FormField>

              <div className="flex flex-col gap-1">
                <label htmlFor="register-api-team" className="text-sm font-medium text-secondary">
                  Team <span className="text-error-primary">*</span>
                </label>
                <select id="register-api-team" data-testid="register-api-team-select"
                  className="rounded-md border border-secondary px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-brand-500 disabled:opacity-60 bg-primary text-primary"
                  value={selectedTeamId}
                  onChange={(e) => { setSelectedTeamId(e.target.value); if (e.target.value) setTeamError(""); }}
                  disabled={teamsList.isLoading || mutation.isPending} aria-invalid={!!teamError}>
                  <option value="">Select a team…</option>
                  {teams.map((t) => (<option key={t.id} value={t.id}>{t.displayName}</option>))}
                </select>
                {teamError && <p className="text-xs text-error-primary">{teamError}</p>}
                {noTeams && (
                  <p className="text-xs text-tertiary">No teams available — create a team first before registering an API.</p>
                )}
              </div>

              <div>
                <p className="text-xs uppercase tracking-wide text-tertiary">Created by</p>
                <div className="mt-1 inline-flex items-center gap-2 rounded-md border border-secondary bg-secondary/40 px-2 py-1.5">
                  <Avatar size="xs" initials={initials} />
                  <div className="min-w-0">
                    <div className="text-sm font-medium text-primary truncate">{user?.displayName ?? "—"}</div>
                    <div className="text-xs text-tertiary truncate">{user?.email ?? ""}</div>
                  </div>
                </div>
              </div>

              <div className="flex justify-end gap-2 pt-2">
                <Button type="button" color="secondary" size="sm" onClick={() => onOpenChange(false)}>Cancel</Button>
                <Button type="submit" color="primary" size="sm" isLoading={mutation.isPending} isDisabled={noTeams}>
                  Register API
                </Button>
              </div>
            </HookForm>
          </div>
        </Dialog>
      </Modal>
    </ModalOverlay>
  );
}
```

- [ ] **Step 4: Run to verify pass**

Run: `cmd //c "cd web && npx vitest run src/features/catalog/components/__tests__/RegisterApiDialog.test.tsx"`
Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add web/src/features/catalog/components/RegisterApiDialog.tsx web/src/features/catalog/components/__tests__/RegisterApiDialog.test.tsx
git commit -m "feat(web): RegisterApiDialog (E-02.F-03 FU-9)" -m "Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 8: Frontend — `ApiDetailPage` (header + metadata only)

**Files:**
- Create: `web/src/features/catalog/pages/ApiDetailPage.tsx`
- Test: `web/src/features/catalog/pages/__tests__/ApiDetailPage.test.tsx`

**Interfaces:**
- Consumes: `useApi` (Task 5), `useTeamsList`, `API_STYLE_LABEL` (Task 4), card/skeleton primitives, `CreatedByLink`.
- Produces: `ApiDetailPage` (route component; reads `:id`). No relationships section (FU-1/3/5).

- [ ] **Step 1: Write the failing detail test**

`web/src/features/catalog/pages/__tests__/ApiDetailPage.test.tsx`:

```tsx
import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { MemoryRouter, Routes, Route } from "react-router-dom";

const api = {
  id: "a1", tenantId: "t", displayName: "Orders API", description: "Order management",
  style: "graphQL", version: "v2", specUrl: "https://example.com/spec.json", teamId: "team1",
  createdByUserId: "u1", createdAt: "2026-07-04T10:00:00Z", createdBy: null,
};
vi.mock("@/features/catalog/api/apis", () => ({ useApi: () => ({ data: api, isLoading: false, isError: false }) }));
vi.mock("@/features/teams/api/teams", () => ({
  useTeamsList: () => ({ items: [{ id: "team1", displayName: "Platform" }], isLoading: false, isError: false }),
}));

import { ApiDetailPage } from "../ApiDetailPage";

function renderPage() {
  return render(
    <MemoryRouter initialEntries={["/catalog/apis/a1"]}>
      <Routes><Route path="/catalog/apis/:id" element={<ApiDetailPage />} /></Routes>
    </MemoryRouter>,
  );
}

describe("ApiDetailPage", () => {
  it("renders name, style label, version and a spec-url external link", () => {
    renderPage();
    expect(screen.getByRole("heading", { name: "Orders API" })).toBeInTheDocument();
    expect(screen.getByText("GraphQL")).toBeInTheDocument();
    expect(screen.getByText("v2")).toBeInTheDocument();
    const link = screen.getByRole("link", { name: /spec/i });
    expect(link).toHaveAttribute("href", "https://example.com/spec.json");
    expect(link).toHaveAttribute("rel", expect.stringContaining("noopener"));
  });
});
```

- [ ] **Step 2: Run to verify failure**

Run: `cmd //c "cd web && npx vitest run src/features/catalog/pages/__tests__/ApiDetailPage.test.tsx"`
Expected: FAIL — module not found.

- [ ] **Step 3: Write the detail page**

`web/src/features/catalog/pages/ApiDetailPage.tsx` (mirror `ServiceDetailPage` header/metadata; drop endpoints/graph/relationships):

```tsx
import { useMemo } from "react";
import { Link, useParams } from "react-router-dom";
import { Card, CardContent, CardHeader } from "@/components/base/card/card";
import { Skeleton } from "@/components/base/skeleton/skeleton";
import { CreatedByLink } from "@/features/users/components/CreatedByLink";
import { useApi } from "@/features/catalog/api/apis";
import { useTeamsList } from "@/features/teams/api/teams";
import { API_STYLE_LABEL } from "@/features/catalog/schemas/registerApi";

export function ApiDetailPage() {
  const { id } = useParams<{ id: string }>();
  const query = useApi(id ?? "");
  const teamsList = useTeamsList({ sortBy: "displayName", sortOrder: "asc", limit: 200 });
  const teamNameById = useMemo(
    () => new Map<string, string>((teamsList.items ?? []).map((t) => [t.id, t.displayName])),
    [teamsList.items],
  );

  if (query.isLoading) {
    return (
      <Card data-testid="api-detail-skeleton">
        <CardHeader><Skeleton className="h-7 w-64" /><Skeleton className="mt-2 h-4 w-32" /></CardHeader>
        <CardContent className="space-y-4"><Skeleton className="h-20 w-full" /><Skeleton className="h-12 w-2/3" /></CardContent>
      </Card>
    );
  }

  if (query.isError || !query.data) {
    return (
      <Card className="mx-auto max-w-md">
        <CardContent className="space-y-2 p-6 text-center">
          <p className="text-base font-medium text-error-primary">API not found</p>
          <p className="text-sm text-tertiary">It may have been deleted, or you may not have access in this tenant.</p>
        </CardContent>
      </Card>
    );
  }

  const api = query.data;

  return (
    <Card>
      <CardHeader className="space-y-3">
        <div className="flex flex-wrap items-center gap-3">
          <h2 className="text-2xl font-semibold text-primary">{api.displayName}</h2>
          <span className="inline-flex items-center rounded-md bg-secondary px-2 py-0.5 text-xs font-medium text-secondary ring-1 ring-inset ring-secondary">
            {API_STYLE_LABEL[api.style]}
          </span>
        </div>
      </CardHeader>
      <CardContent className="space-y-6">
        <section>
          <h3 className="text-sm font-medium text-tertiary">Description</h3>
          <p className="mt-1 text-sm text-secondary">
            {api.description ? api.description : <span className="italic">No description</span>}
          </p>
        </section>

        <hr className="border-secondary" />

        <section className="grid grid-cols-1 gap-4 sm:grid-cols-3">
          <Field label="ID" value={api.id} mono />
          <Field label="Version" value={api.version} mono />
          <div>
            <div className="text-xs uppercase tracking-wide text-tertiary">Team</div>
            <div className="mt-1 text-sm">
              <Link to={`/teams/${api.teamId}`} className="text-primary hover:underline">
                {teamNameById.get(api.teamId) ?? "View team"}
              </Link>
            </div>
          </div>
          <div>
            <div className="text-xs uppercase tracking-wide text-tertiary">Created by</div>
            <div className="mt-1 text-sm"><CreatedByLink user={api.createdBy} /></div>
          </div>
          <Field label="Created" value={api.createdAt ? new Date(api.createdAt).toLocaleString() : "—"} />
          <div>
            <div className="text-xs uppercase tracking-wide text-tertiary">Spec</div>
            <div className="mt-1 text-sm">
              {api.specUrl ? (
                <a href={api.specUrl} target="_blank" rel="noopener noreferrer" className="text-primary hover:underline break-all">
                  View spec
                </a>
              ) : (
                <span className="text-tertiary italic">No spec URL</span>
              )}
            </div>
          </div>
        </section>
      </CardContent>
    </Card>
  );
}

function Field({ label, value, mono = false }: { label: string; value: string; mono?: boolean }) {
  return (
    <div>
      <div className="text-xs uppercase tracking-wide text-tertiary">{label}</div>
      <div className={mono ? "mt-1 font-mono text-sm text-primary break-all" : "mt-1 text-sm text-primary"}>{value}</div>
    </div>
  );
}
```

- [ ] **Step 4: Run to verify pass**

Run: `cmd //c "cd web && npx vitest run src/features/catalog/pages/__tests__/ApiDetailPage.test.tsx"`
Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add web/src/features/catalog/pages/ApiDetailPage.tsx web/src/features/catalog/pages/__tests__/ApiDetailPage.test.tsx
git commit -m "feat(web): ApiDetailPage metadata surface (E-02.F-03 FU-9)" -m "Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 9: Frontend — `ApisListPage` with FilterBar (name + style + team)

**Files:**
- Create: `web/src/features/catalog/pages/ApisListPage.tsx`
- Test: `web/src/features/catalog/pages/__tests__/ApisListPage.test.tsx`

**Interfaces:**
- Consumes: `useApisList` (Task 5), `useTeamsList`, `ApisTable` (Task 6), `RegisterApiDialog` (Task 7), `useListUrlState`, `useListFilters`, `FilterBar`, `FilterSpec`, `usePermissions`, `KartovaPermissions`, `API_STYLES`/`API_STYLE_LABEL` (Task 4).
- Produces: `ApisListPage` (route component).

- [ ] **Step 1: Write the failing list-page test**

`web/src/features/catalog/pages/__tests__/ApisListPage.test.tsx` (mirror `ServicesListPage.test.tsx` — mock the hooks; assert the table renders and the register button is permission-gated):

```tsx
import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";

const listResult = {
  items: [{ id: "a1", displayName: "Orders API", style: "rest", version: "v1", teamId: "team1", createdBy: null, createdAt: "2026-07-04T00:00:00Z" }],
  isLoading: false, isError: false, error: null, hasNext: false, hasPrev: false,
  goNext: vi.fn(), goPrev: vi.fn(), reset: vi.fn(),
};
vi.mock("@/features/catalog/api/apis", () => ({ useApisList: () => listResult }));
vi.mock("@/features/teams/api/teams", () => ({ useTeamsList: () => ({ items: [{ id: "team1", displayName: "Platform" }], isError: false }) }));
vi.mock("@/shared/auth/usePermissions", () => ({
  usePermissions: () => ({ hasPermission: () => true, isLoading: false }),
}));

import { ApisListPage } from "../ApisListPage";

describe("ApisListPage", () => {
  it("renders the APIs heading, the register button, and a row", () => {
    render(<MemoryRouter><ApisListPage /></MemoryRouter>);
    expect(screen.getByRole("heading", { name: "APIs" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /Register API/i })).toBeInTheDocument();
    expect(screen.getByText("Orders API")).toBeInTheDocument();
  });
});
```

- [ ] **Step 2: Run to verify failure**

Run: `cmd //c "cd web && npx vitest run src/features/catalog/pages/__tests__/ApisListPage.test.tsx"`
Expected: FAIL — module not found.

- [ ] **Step 3: Write the list page**

`web/src/features/catalog/pages/ApisListPage.tsx` (mirror `ServicesListPage.tsx`; filters: text `displayNameContains`, static `style` multi-select, dynamic `teamId` multi-select; sort allowlist all four fields):

```tsx
import { useMemo, useState, useEffect } from "react";
import { Plus } from "@untitledui/icons";
import { Button } from "@/components/base/buttons/button";
import { Card, CardContent } from "@/components/base/card/card";
import { FilterBar } from "@/components/application/filter-bar/FilterBar";
import { useListFilters } from "@/lib/list/filters/useListFilters";
import type { FilterSpec } from "@/lib/list/filters/types";
import { useApisList } from "@/features/catalog/api/apis";
import { useTeamsList } from "@/features/teams/api/teams";
import { useListUrlState } from "@/lib/list/useListUrlState";
import { ApisTable } from "@/features/catalog/components/ApisTable";
import { RegisterApiDialog } from "@/features/catalog/components/RegisterApiDialog";
import { usePermissions } from "@/shared/auth/usePermissions";
import { KartovaPermissions } from "@/shared/auth/permissions";
import { API_STYLES, API_STYLE_LABEL } from "@/features/catalog/schemas/registerApi";

const ALLOWED_SORT_FIELDS = ["displayName", "style", "version", "createdAt"] as const;
const TEXT_FILTERS = ["displayNameContains"] as const;
const MULTI_FILTERS = ["style", "teamId"] as const;
const STYLE_OPTIONS = API_STYLES.map((s) => ({ label: API_STYLE_LABEL[s], value: s }));

export function ApisListPage() {
  const urlState = useListUrlState({
    defaultSortBy: "displayName",
    defaultSortOrder: "asc",
    allowedSortFields: ALLOWED_SORT_FIELDS,
    textFilters: TEXT_FILTERS,
    multiFilters: MULTI_FILTERS,
  });

  const teamsList = useTeamsList({ sortBy: "displayName", sortOrder: "asc", limit: 200 });
  const teamNameById = useMemo(
    () => new Map<string, string>((teamsList.items ?? []).map((t) => [t.id, t.displayName])),
    [teamsList.items],
  );

  const filterSpecs: FilterSpec[] = useMemo(
    () => [
      { key: "displayNameContains", type: "text", label: "Search APIs", placeholder: "Search by name…" },
      { key: "style", type: "multi-select", label: "Style", placeholder: "Any style", options: STYLE_OPTIONS },
      {
        key: "teamId",
        type: "multi-select",
        label: "Team",
        placeholder: "All teams",
        options: (teamsList.items ?? []).map((t) => ({ label: t.displayName, value: t.id })),
      },
    ],
    [teamsList.items],
  );
  const filters = useListFilters(filterSpecs, urlState);

  const list = useApisList({
    sortBy: urlState.sortBy,
    sortOrder: urlState.sortOrder,
    displayNameContains: filters.textValues.displayNameContains,
    style: filters.multiValues.style,
    teamId: filters.multiValues.teamId,
  });

  const [dialogOpen, setDialogOpen] = useState(false);
  const { hasPermission, isLoading: permissionsLoading } = usePermissions();
  const canRegister = !permissionsLoading && hasPermission(KartovaPermissions.CatalogApisRegister);

  useEffect(() => {
    if (list.isError) console.error("ApisListPage list error", list.error);
  }, [list.isError, list.error]);
  useEffect(() => {
    if (teamsList.isError) console.error("ApisListPage teams error", teamsList.error);
  }, [teamsList.isError, teamsList.error]);

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <h2 className="text-2xl font-semibold text-primary">APIs</h2>
        {canRegister && (
          <Button onClick={() => setDialogOpen(true)} size="sm" color="primary" iconLeading={Plus}>
            Register API
          </Button>
        )}
      </div>

      <FilterBar specs={filterSpecs} urlState={urlState} />

      {list.isError ? (
        <Card className="mx-auto max-w-md">
          <CardContent className="space-y-3 p-6 text-center">
            <p className="text-base font-medium text-error-primary">Failed to load APIs</p>
            <p className="text-sm text-tertiary">Try refreshing or resetting the list.</p>
            <Button size="sm" onClick={() => list.reset()}>Reset</Button>
          </CardContent>
        </Card>
      ) : !list.isLoading && list.items.length === 0 && filters.isActive ? (
        <Card className="mx-auto max-w-md text-center">
          <CardContent className="space-y-2 p-8">
            <p className="text-base font-medium text-primary">No APIs match your filters</p>
            <p className="text-sm text-tertiary">Try a different name or clear the filters.</p>
          </CardContent>
        </Card>
      ) : (
        <ApisTable
          list={list}
          sortBy={urlState.sortBy}
          sortOrder={urlState.sortOrder}
          onSortChange={urlState.setSort}
          teamNameById={teamNameById}
        />
      )}

      {canRegister && <RegisterApiDialog open={dialogOpen} onOpenChange={setDialogOpen} />}
    </div>
  );
}
```

> `KartovaPermissions.CatalogApisRegister` must already exist in `web/src/shared/auth/permissions.ts` (shipped in S-01's 5-sync). If missing, that is an S-01 regression — verify before proceeding.

- [ ] **Step 4: Run to verify pass**

Run: `cmd //c "cd web && npx vitest run src/features/catalog/pages/__tests__/ApisListPage.test.tsx"`
Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add web/src/features/catalog/pages/ApisListPage.tsx web/src/features/catalog/pages/__tests__/ApisListPage.test.tsx
git commit -m "feat(web): ApisListPage with name/style/team filters (E-02.F-03 FU-9)" -m "Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 10: Frontend — routes + nav wiring

**Files:**
- Modify: `web/src/app/router.tsx`
- Modify: `web/src/components/layout/Sidebar.tsx`
- Test: `web/src/components/layout/__tests__/Sidebar.test.tsx` (extend)

**Interfaces:**
- Consumes: `ApisListPage` (Task 9), `ApiDetailPage` (Task 8).
- Produces: routes `/catalog/apis`, `/catalog/apis/:id`; sidebar "APIs" nav item under Catalog.

- [ ] **Step 1: Add the failing nav assertion**

In `web/src/components/layout/__tests__/Sidebar.test.tsx`, add (adapt to the file's existing render helper/permission mock):

```tsx
it("renders an APIs nav link under Catalog", () => {
  renderSidebar(); // use the file's existing render helper
  expect(screen.getByRole("link", { name: "APIs" })).toHaveAttribute("href", "/catalog/apis");
});
```

- [ ] **Step 2: Run to verify failure**

Run: `cmd //c "cd web && npx vitest run src/components/layout/__tests__/Sidebar.test.tsx"`
Expected: FAIL — no "APIs" link.

- [ ] **Step 3: Wire routes**

In `web/src/app/router.tsx`: add imports and routes after the Services routes (lines 54-55):

```tsx
import { ApisListPage } from "@/features/catalog/pages/ApisListPage";
import { ApiDetailPage } from "@/features/catalog/pages/ApiDetailPage";
```
```tsx
        <Route path="/catalog/apis" element={<ApisListPage />} />
        <Route path="/catalog/apis/:id" element={<ApiDetailPage />} />
```

- [ ] **Step 4: Wire the nav item**

In `web/src/components/layout/Sidebar.tsx`, add an APIs `<li>` after the Services item (line 85-87), before the disabled Infrastructure item:

```tsx
            <li>
              <NavItemLink to="/catalog/apis" label="APIs" />
            </li>
```

- [ ] **Step 5: Run to verify pass**

Run: `cmd //c "cd web && npx vitest run src/components/layout/__tests__/Sidebar.test.tsx"`
Expected: PASS.

- [ ] **Step 6: Full frontend gate (typecheck + build + full unit suite)**

Run: `cmd //c "cd web && npm run build && npx vitest run"`
Expected: `tsc -b` clean; all unit tests green (existing + new).

- [ ] **Step 7: Commit**

```powershell
git add web/src/app/router.tsx web/src/components/layout/Sidebar.tsx web/src/components/layout/__tests__/Sidebar.test.tsx
git commit -m "feat(web): wire /catalog/apis routes + APIs nav item (E-02.F-03 FU-9)" -m "Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 11: Docs — filter registry + checklist

**Files:**
- Modify: `docs/design/list-filter-registry.md`
- Modify: `docs/product/CHECKLIST.md`

- [ ] **Step 1: Update the filter registry**

Move the `/catalog/apis` row out of "Planned filtering surfaces (deferred → FU-9)" into the built-filters section: filters = **name typeahead** (`displayNameContains`), **style multi-select** (`style`: rest/grpc/graphQL), **team multi-select** (`teamId`); sort allowlist `{ displayName, style, version, createdAt }`; default `displayName asc`. Match the exact table format of the Services/Applications rows already in the file.

- [ ] **Step 2: Update the checklist**

Under `E-02.F-03` in `docs/product/CHECKLIST.md`, append a note to the S-01 line (or add a FU-9 note): FU-9 shipped 2026-07-04 (API UI surface — list/detail/register screens + name/style/team list filters + `ListApis` filter params). S-02/S-03 and FU-1..FU-8/FU-10/FU-11 remain open. Keep the feature-header parenthetical style used by E-02.F-01/F-02.

- [ ] **Step 3: Commit**

```powershell
git add docs/design/list-filter-registry.md docs/product/CHECKLIST.md
git commit -m "docs: record /catalog/apis filters + FU-9 shipped (E-02.F-03)" -m "Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 12: DoD ledger + verification

**Files:**
- Create: `docs/superpowers/verification/2026-07-04-catalog-api-ui-surface/dod.md` (copy `docs/superpowers/templates/dod-ledger-template.md`)
- Create: `docs/superpowers/verification/2026-07-04-catalog-api-ui-surface/gate-findings.yaml` (copy `docs/superpowers/templates/gate-findings-template.yaml`)

- [ ] **Step 1: Scaffold the ledger**

Copy both templates into `docs/superpowers/verification/2026-07-04-catalog-api-ui-surface/`, fill the header (slice = E-02.F-03 FU-9, branch `feat/catalog-api-ui-surface`).

- [ ] **Step 2: Run the eight blocking DoD gates + conditional mutation**

Per CLAUDE.md DoD (fail-fast order). Record each in `dod.md` as it runs:
1. `cmd //c "dotnet build Kartova.slnx -warnaserror"` — 0/0.
2. Per-task subagent reviews (done during subagent-driven dev).
3. `cmd //c "dotnet test Kartova.slnx"` + `cmd //c "cd web && npx vitest run"` — all green (real-seam Catalog integ tests included).
4. Container build: `cmd //c "docker compose build"` (the `images` CI job) — green.
5. `/simplify` against the branch diff — should-fix addressed or noted.
6. **Mutation (blocking — backend touches Application/Infra filter logic):** `/misc:mutation-sentinel` on `ListApisHandler.cs` + `ListApisQuery.cs` + the `ListApisAsync` delegate → `/misc:test-generator`; target ≥80%, document survivors.
7. `/superpowers:requesting-code-review` against the full branch diff.
8. `/pr-review-toolkit:review-pr` (run for real — do not fold into 7/9).
9. `/deep-review` against the branch diff (spec/plan/ADRs/tests).

- [ ] **Step 3: Pre-push CI mirror**

Stop the vite dev server first (project memory: EPERM on lightningcss). Run: `bash scripts/ci-local.sh` — Release build+test, web image, helm/stryker. Confirm green.

- [ ] **Step 4: Real-browser verification (ADR-0084)**

Cold-start the dev stack (web on 5173, OIDC pinned). Login `admin@orga.kartova.local` / `dev_password_12`, navigate **in-SPA** to `/catalog/apis` (deep-link cold-load bounces — bug #47). DevSeed has no APIs — register one via the dialog. Verify: list renders + rowheader present (dialog open does not blank-page), each filter applies (name/style/team), sort toggles, detail page loads with spec link, console clean. Capture screenshots into the verification folder.

- [ ] **Step 5: Terminal re-verify**

After gates 5–9 fixes, re-run build + full suite on the final commit. Update `dod.md` summary table + `gate-findings.yaml`.

- [ ] **Step 6: Commit the ledger**

```powershell
git add docs/superpowers/verification/2026-07-04-catalog-api-ui-surface/
git commit -m "docs(dod): FU-9 API UI surface verification ledger (E-02.F-03)" -m "Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Self-Review

**Spec coverage:** §4 backend filters → Tasks 1-2; §5 frontend surface → Tasks 4-10; §3 #12 codegen sequencing → Task 3; §6 testing (backend unit + real-seam, frontend, browser) → Tasks 1,2,4-10,12; §7 DoD (mutation blocking) → Task 12; §8 registry+checklist → Task 11. All spec sections mapped.

**Placeholder scan:** no TBD/TODO. Two intentional "read the sibling and mirror" instructions (ProblemTypes base URL in Task 2 Step 3; Sidebar test render helper in Task 10 Step 1) point at concrete existing lines, not vague fills. Every code step ships full code.

**Type consistency:** `ApisListParams`/`useApisList` (Task 5) ↔ `ApisListPage` call (Task 9) ↔ `ListApisQuery` fields (Task 1) ↔ endpoint params (Task 2) all agree on `style`/`teamId`/`displayNameContains`. `SortField = displayName|style|version|createdAt` consistent Task 6 ↔ Task 9 ↔ S-01 allowlist. `API_STYLES`/`API_STYLE_LABEL`/`ApiStyleValue` defined once (Task 4), consumed in Tasks 5-9. `ApiResponse` type re-exported from Task 5, used in Tasks 6/8. Wire values `rest`/`grpc`/`graphQL` consistent across backend (Task 2 detail message) and frontend (Task 4).

**Gaps:** none found.
