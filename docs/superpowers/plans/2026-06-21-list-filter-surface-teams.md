# List-Filter Surface (Teams first consumer) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the ADR-0107 standard list-filter surface (`<FilterBar>` + `useListFilters` + `useListUrlState` text-filter support) and make the Teams list its first consumer with a `displayName` text-search filter; flip the Teams default sort to `displayName asc`.

**Architecture:** Backend adds an optional `displayNameContains` query param to `GET /organizations/teams`, applied as a Postgres `ILIKE` substring filter before keyset pagination and recorded in the cursor `f`-map (ADR-0095). Frontend extends `useListUrlState` with URL-backed text filters, adds a spec-driven `useListFilters` hook (300ms debounce) and a shared `<FilterBar>` that renders from `FilterSpec[]`; the Teams page wires one text spec.

**Tech Stack:** .NET 10 / EF Core (Npgsql `EF.Functions.ILike`) · Wolverine handler · React + TypeScript · react-aria-components (Untitled UI `Input`) · TanStack Query · Vitest · MSTest integration (Testcontainers, `KartovaApiFixtureBase`).

## Global Constraints

- **Spec:** `docs/superpowers/specs/2026-06-21-list-filter-surface-teams-design.md` — authoritative; this plan implements it.
- **One identifier everywhere:** the filter key is `displayNameContains` for the browser URL param, the API query param, AND the cursor `f`-map key. Do not rename per layer.
- **Match semantics:** case-insensitive **contains** via `EF.Functions.ILike(DisplayName, "%"+escaped+"%", "\\")`; escape `\` `%` `_`; trim input; **blank/whitespace ⇒ filter absent** (no `f` key, no `WHERE`).
- **Default sort:** `displayName` **ascending** on both the endpoint default and the screen.
- **Cursor consistency:** the filter is applied **before** `ToCursorPagedAsync`; the `f`-map is passed via `expectedFilters:`. The frontend filter value lives in the list `queryKey` so `useCursorList` resets pagination on change.
- **`<FilterBar>` builds the `text` control only** (ADR-0107 clause 1); other `FilterSpec` types are typed but throw if rendered.
- **Build:** `TreatWarningsAsErrors=true` — 0 warnings. Regenerated `web/src/generated/openapi.ts` + `web/openapi-snapshot.json` MUST be committed (web image compiles TS — gate 4).
- **Every commit** ends with the trailer: `Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>`.
- **DoD:** CLAUDE.md → Working agreements → Definition of Done (eight always-blocking gates + conditional mutation). Mutation gate (6) **applies** here (Organization Application/Infrastructure logic changes).

## Pre-requisite (not a TDD task — resolve before Task 1)

Work currently sits on `fix/ci-frontend-tests-and-listservices-openapi` with unrelated uncommitted changes, plus this session's ADR-0107 / registry / spec edits. Create the feature branch off `master` and decide how to carry the docs:

```bash
git stash push -u -m "wip-ci-fix"          # if the ci-fix changes are unrelated and should stay parked
git checkout master && git pull
git checkout -b feat/list-filter-surface-teams
git stash pop                               # only if the ADR/spec docs were stashed and belong here
```
Confirm the ADR-0107 docs + spec + registry are present on the new branch before starting. (Ask the user if the branch strategy is unclear — do not guess.)

---

### Task 1: Backend — `displayName` filter, `f`-map, default-sort flip (+ real-seam tests)

**Files:**
- Modify: `src/Modules/Organization/Kartova.Organization.Application/ListTeamsQuery.cs`
- Modify: `src/Modules/Organization/Kartova.Organization.Infrastructure/ListTeamsHandler.cs`
- Modify: `src/Modules/Organization/Kartova.Organization.Infrastructure/TeamEndpointDelegates.cs`
- Test: `src/Modules/Organization/Kartova.Organization.IntegrationTests/ListTeamsTests.cs`

**Interfaces:**
- Consumes: `TeamSortSpecs.{Resolve, IdSelector, AllowedFieldNames}`, `QueryablePagingExtensions.ToCursorPagedAsync(..., expectedFilters:)`, `CursorListBinding.Bind<TeamSortField>`.
- Produces: `ListTeamsQuery(TeamSortField SortBy, SortOrder SortOrder, string? Cursor, int Limit, string? DisplayNameContains)`; endpoint query param `displayNameContains` on operation `ListTeams`; `f`-map key `displayNameContains`.

- [ ] **Step 1: Write failing integration tests** — append to `ListTeamsTests.cs` (inside the class):

```csharp
    [TestMethod]
    public async Task DisplayNameContains_filters_case_insensitively()
    {
        await Fx.SeedOrganizationAsync(Tenant.Value, "OrgA-Filter");
        await Fx.SeedTeamAsync(Tenant.Value, "Platform");
        await Fx.SeedTeamAsync(Tenant.Value, "Payments");
        await Fx.SeedTeamAsync(Tenant.Value, "Data");
        try
        {
            var client = Fx.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer", Fx.Signer.IssueForTenant(Tenant, new[] { KartovaRoles.Member }));

            var resp = await client.GetAsync("/api/v1/organizations/teams?displayNameContains=pa");
            Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
            var page = await resp.Content.ReadFromJsonAsync<CursorPage<TeamResponse>>(KartovaApiFixtureBase.WireJson);
            CollectionAssert.AreEquivalent(
                new[] { "Payments", "Platform" },
                page!.Items.Select(t => t.DisplayName).ToArray());
        }
        finally { await Fx.DeleteTeamsForTenantAsync(Tenant.Value); }
    }

    [TestMethod]
    public async Task Blank_displayNameContains_returns_all_teams()
    {
        await Fx.SeedOrganizationAsync(Tenant.Value, "OrgA-Blank");
        await Fx.SeedTeamAsync(Tenant.Value, "Alpha");
        await Fx.SeedTeamAsync(Tenant.Value, "Beta");
        try
        {
            var client = Fx.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer", Fx.Signer.IssueForTenant(Tenant, new[] { KartovaRoles.Member }));

            var resp = await client.GetAsync("/api/v1/organizations/teams?displayNameContains=%20");
            var page = await resp.Content.ReadFromJsonAsync<CursorPage<TeamResponse>>(KartovaApiFixtureBase.WireJson);
            Assert.AreEqual(2, page!.Items.Count);
        }
        finally { await Fx.DeleteTeamsForTenantAsync(Tenant.Value); }
    }

    [TestMethod]
    public async Task DisplayNameContains_escapes_like_wildcards()
    {
        await Fx.SeedOrganizationAsync(Tenant.Value, "OrgA-Escape");
        await Fx.SeedTeamAsync(Tenant.Value, "Alpha");
        await Fx.SeedTeamAsync(Tenant.Value, "Beta");
        try
        {
            var client = Fx.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer", Fx.Signer.IssueForTenant(Tenant, new[] { KartovaRoles.Member }));

            // '%' must be treated literally — no team contains it, so zero rows (not all).
            var resp = await client.GetAsync("/api/v1/organizations/teams?displayNameContains=%25");
            var page = await resp.Content.ReadFromJsonAsync<CursorPage<TeamResponse>>(KartovaApiFixtureBase.WireJson);
            Assert.AreEqual(0, page!.Items.Count);
        }
        finally { await Fx.DeleteTeamsForTenantAsync(Tenant.Value); }
    }

    [TestMethod]
    public async Task Changing_filter_mid_pagination_returns_400_cursor_filter_mismatch()
    {
        await Fx.SeedOrganizationAsync(Tenant.Value, "OrgA-Mismatch");
        await Fx.SeedTeamAsync(Tenant.Value, "Apple");
        await Fx.SeedTeamAsync(Tenant.Value, "Apricot");
        try
        {
            var client = Fx.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer", Fx.Signer.IssueForTenant(Tenant, new[] { KartovaRoles.Member }));

            var first = await client.GetFromJsonAsync<CursorPage<TeamResponse>>(
                "/api/v1/organizations/teams?displayNameContains=ap&limit=1", KartovaApiFixtureBase.WireJson);
            Assert.IsNotNull(first!.NextCursor);

            // Reuse the cursor under a DIFFERENT filter → CursorFilterMismatchException → 400.
            var resp = await client.GetAsync(
                $"/api/v1/organizations/teams?displayNameContains=zz&cursor={Uri.EscapeDataString(first.NextCursor!)}");
            Assert.AreEqual(HttpStatusCode.BadRequest, resp.StatusCode);
        }
        finally { await Fx.DeleteTeamsForTenantAsync(Tenant.Value); }
    }

    [TestMethod]
    public async Task DisplayNameContains_does_not_leak_cross_tenant()
    {
        var other = new TenantId(Guid.Parse("aaaaaaaa-0002-0002-0002-000000000099"));
        await Fx.SeedOrganizationAsync(Tenant.Value, "OrgA-RLS");
        await Fx.SeedOrganizationAsync(other.Value, "OrgB-RLS");
        await Fx.SeedTeamAsync(Tenant.Value, "AlphaMine");
        await Fx.SeedTeamAsync(other.Value, "AlphaTheirs");
        try
        {
            var client = Fx.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer", Fx.Signer.IssueForTenant(Tenant, new[] { KartovaRoles.Member }));

            var page = await client.GetFromJsonAsync<CursorPage<TeamResponse>>(
                "/api/v1/organizations/teams?displayNameContains=Alpha", KartovaApiFixtureBase.WireJson);
            CollectionAssert.AreEquivalent(new[] { "AlphaMine" }, page!.Items.Select(t => t.DisplayName).ToArray());
        }
        finally
        {
            await Fx.DeleteTeamsForTenantAsync(Tenant.Value);
            await Fx.DeleteTeamsForTenantAsync(other.Value);
        }
    }

    [TestMethod]
    public async Task Default_sort_is_displayName_ascending()
    {
        await Fx.SeedOrganizationAsync(Tenant.Value, "OrgA-Sort");
        await Fx.SeedTeamAsync(Tenant.Value, "Zeta");
        await Fx.SeedTeamAsync(Tenant.Value, "Alpha");
        await Fx.SeedTeamAsync(Tenant.Value, "Mu");
        try
        {
            var client = Fx.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer", Fx.Signer.IssueForTenant(Tenant, new[] { KartovaRoles.Member }));

            var page = await client.GetFromJsonAsync<CursorPage<TeamResponse>>(
                "/api/v1/organizations/teams", KartovaApiFixtureBase.WireJson);
            CollectionAssert.AreEqual(
                new[] { "Alpha", "Mu", "Zeta" },
                page!.Items.Select(t => t.DisplayName).ToArray());
        }
        finally { await Fx.DeleteTeamsForTenantAsync(Tenant.Value); }
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test src/Modules/Organization/Kartova.Organization.IntegrationTests --filter "FullyQualifiedName~ListTeamsTests"`
Expected: the six new tests FAIL (filter ignored → wrong counts; default sort is `createdAt desc` not `displayName asc`).

- [ ] **Step 3: Add the query field** — `ListTeamsQuery.cs`:

```csharp
public sealed record ListTeamsQuery(
    TeamSortField SortBy,
    SortOrder SortOrder,
    string? Cursor,
    int Limit,
    string? DisplayNameContains);
```

- [ ] **Step 4: Apply the filter + `f`-map in the handler** — `ListTeamsHandler.cs`, replace the `Handle` body's query build:

```csharp
    public async Task<CursorPage<TeamResponse>> Handle(
        ListTeamsQuery q,
        OrganizationDbContext db,
        CancellationToken ct)
    {
        var spec = TeamSortSpecs.Resolve(q.SortBy);

        // Apply the displayName filter BEFORE pagination so a hidden row never
        // becomes a cursor boundary (same invariant as ListApplicationsHandler).
        IQueryable<Team> source = db.Teams;
        Dictionary<string, string>? filters = null;
        if (q.DisplayNameContains is { } name)
        {
            var pattern = $"%{EscapeLike(name)}%";
            source = source.Where(t => EF.Functions.ILike(t.DisplayName, pattern, "\\"));
            // The owning module owns the f-map keys/values; the shared codec treats
            // them as opaque. A change mid-pagination trips CursorFilterMismatchException.
            filters = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["displayNameContains"] = name,
            };
        }

        var page = await source
            .ToCursorPagedAsync(
                spec, q.SortOrder, q.Cursor, q.Limit,
                TeamSortSpecs.IdSelector, IdExtractor, ct,
                expectedFilters: filters);

        var items = page.Items
            .Select(t => new TeamResponse(t.Id.Value, t.DisplayName, t.Description, t.CreatedAt))
            .ToList();

        return new CursorPage<TeamResponse>(items, page.NextCursor, page.PrevCursor);
    }

    // Escapes LIKE/ILIKE metacharacters so user input matches literally (ESCAPE '\').
    // Backslash first, so the escapes added for % and _ are not re-escaped.
    private static string EscapeLike(string raw) =>
        raw.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
```

Add `using Microsoft.EntityFrameworkCore;` to the file's usings if not present (for `EF.Functions`).

- [ ] **Step 5: Bind the param + flip the default sort** — `TeamEndpointDelegates.cs`, `ListTeamsAsync`:

```csharp
    internal static async Task<IResult> ListTeamsAsync(
        [FromQuery] string? sortBy,
        [FromQuery] string? sortOrder,
        [FromQuery] string? cursor,
        [FromQuery] string? limit,
        [FromQuery] string? displayNameContains,
        ListTeamsHandler handler,
        OrganizationDbContext db,
        CancellationToken ct)
    {
        var (parsedSortBy, parsedSortOrder, effectiveLimit) = CursorListBinding.Bind<TeamSortField>(
            sortBy, sortOrder, limit, TeamSortSpecs.AllowedFieldNames);

        // Blank/whitespace ⇒ no filter (filter-absent must equal today's unfiltered cursor).
        var name = string.IsNullOrWhiteSpace(displayNameContains) ? null : displayNameContains.Trim();

        var query = new ListTeamsQuery(
            SortBy: parsedSortBy ?? TeamSortField.DisplayName,   // default flips: was CreatedAt
            SortOrder: parsedSortOrder ?? SortOrder.Asc,          // default flips: was Desc
            Cursor: cursor,
            Limit: effectiveLimit,
            DisplayNameContains: name);

        var page = await handler.Handle(query, db, ct);
        return Results.Ok(page);
    }
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test src/Modules/Organization/Kartova.Organization.IntegrationTests --filter "FullyQualifiedName~ListTeamsTests"`
Expected: PASS (all, including the original two). If `EF.Functions.ILike` 3-arg overload is unresolved, confirm `Npgsql.EntityFrameworkCore.PostgreSQL` is referenced (it is, via the module) and the `using Microsoft.EntityFrameworkCore;` is present.

- [ ] **Step 7: Commit**

```bash
git add src/Modules/Organization
git commit -m "feat(organization): displayName filter + displayName-asc default on Teams list"
```

---

### Task 2: Regenerate OpenAPI client + snapshot

**Files:**
- Modify: `web/src/generated/openapi.ts` (generated)
- Modify: `web/openapi-snapshot.json` (generated)
- Verify: `tests/Kartova.Api.IntegrationTests/OpenApiTests.cs`

**Interfaces:**
- Produces: `operations["ListTeams"]["parameters"]["query"].displayNameContains?: string` in the generated TS client (consumed by Task 6).

- [ ] **Step 1: Run codegen against the running API**

Run (per the repo's codegen workflow): start the API, then `cd web && npm run codegen`.
Expected: `web/src/generated/openapi.ts` and `web/openapi-snapshot.json` now show `displayNameContains` on the `ListTeams` operation query params.

- [ ] **Step 2: Run the OpenAPI snapshot test**

Run: `dotnet test tests/Kartova.Api.IntegrationTests --filter "FullyQualifiedName~OpenApiTests"`
Expected: if the test pins a committed snapshot, it FAILS until the snapshot is regenerated/accepted; follow the test's documented update path (regenerate the asserted document), then it PASSES. `displayNameContains` is a plain `string` query param — no `CursorListQueryParameterTransformer` change needed.

- [ ] **Step 3: Commit**

```bash
git add web/src/generated/openapi.ts web/openapi-snapshot.json tests/Kartova.Api.IntegrationTests
git commit -m "chore(web): regenerate OpenAPI client for Teams displayNameContains param"
```

---

### Task 3: `useListUrlState` — URL-backed text filters

**Files:**
- Modify: `web/src/lib/list/useListUrlState.ts`
- Test: `web/src/lib/list/__tests__/use-list-url-state.test.tsx`

**Interfaces:**
- Produces: config `textFilters?: readonly TTextFilter[]`; return `textFilters: Readonly<Record<TTextFilter,string>>` (default `""`) and `setTextFilter: (name: TTextFilter, value: string) => void` (blank/whitespace removes the param). Consumed by Task 4.

- [ ] **Step 1: Write failing tests** — append to `use-list-url-state.test.tsx`:

```tsx
const textConfig = {
  ...config,
  textFilters: ["displayNameContains"] as const,
};

describe("textFilters", () => {
  it("defaults to empty string when param absent", () => {
    const { result } = renderHook(() => useListUrlState(textConfig), { wrapper: withRouter("/") });
    expect(result.current.textFilters.displayNameContains).toBe("");
  });

  it("reads the raw value from the URL", () => {
    const { result } = renderHook(
      () => useListUrlState(textConfig),
      { wrapper: withRouter("/?displayNameContains=plat") },
    );
    expect(result.current.textFilters.displayNameContains).toBe("plat");
  });

  it("setTextFilter writes the value to the URL", () => {
    let search = "";
    function Inner() { search = useLocation().search; return null; }
    const { result } = renderHook(() => useListUrlState(textConfig), {
      wrapper: ({ children }) => (
        <MemoryRouter initialEntries={["/"]}><Inner />{children}</MemoryRouter>
      ),
    });
    act(() => { result.current.setTextFilter("displayNameContains", "plat"); });
    expect(search).toContain("displayNameContains=plat");
  });

  it("setTextFilter with blank/whitespace removes the param", () => {
    let search = "";
    function Inner() { search = useLocation().search; return null; }
    const { result } = renderHook(() => useListUrlState(textConfig), {
      wrapper: ({ children }) => (
        <MemoryRouter initialEntries={["/?displayNameContains=plat"]}><Inner />{children}</MemoryRouter>
      ),
    });
    act(() => { result.current.setTextFilter("displayNameContains", "   "); });
    expect(search).not.toContain("displayNameContains");
  });

  it("returns empty record when textFilters config absent", () => {
    const { result } = renderHook(() => useListUrlState(config), { wrapper: withRouter("/") });
    expect(result.current.textFilters).toEqual({});
  });
});
```

- [ ] **Step 2: Run to verify failure**

Run: `cd web && npx vitest run src/lib/list/__tests__/use-list-url-state.test.tsx`
Expected: FAIL — `textFilters` undefined / `setTextFilter` not a function.

- [ ] **Step 3: Implement** — `useListUrlState.ts`. Add the generic param + config field, mirroring `booleanFilters`:

```ts
interface Config<TField extends string, TBoolFilter extends string = never, TTextFilter extends string = never> {
  defaultSortBy: TField;
  defaultSortOrder: SortDirection;
  allowedSortFields: readonly TField[];
  booleanFilters?: readonly TBoolFilter[];
  /**
   * Optional free-text URL params (e.g. ["displayNameContains"]). Read as the raw
   * string ("" when absent). Setter writes the trimmed value, or removes the param
   * when blank/whitespace (no empty `=` clutter).
   */
  textFilters?: readonly TTextFilter[];
}

export interface ListUrlState<TField extends string, TBoolFilter extends string = never, TTextFilter extends string = never> {
  sortBy: TField;
  sortOrder: SortDirection;
  setSort: (field: TField, order: SortDirection) => void;
  booleanFilters: Readonly<Record<TBoolFilter, boolean>>;
  setBooleanFilter: (name: TBoolFilter, value: boolean) => void;
  textFilters: Readonly<Record<TTextFilter, string>>;
  setTextFilter: (name: TTextFilter, value: string) => void;
}
```

Add to the function signature `<TField, TBoolFilter = never, TTextFilter extends string = never>` and `config: Config<TField, TBoolFilter, TTextFilter>`. After the `booleanFilters` block add:

```ts
  const textFilterNames = useMemo(
    () => (config.textFilters ?? []) as readonly TTextFilter[],
    [config.textFilters],
  );

  const textFilters = useMemo(() => {
    const out = {} as Record<TTextFilter, string>;
    for (const name of textFilterNames) {
      out[name] = params.get(name) ?? "";
    }
    return out;
  }, [params, textFilterNames]);

  const setTextFilter = useCallback(
    (name: TTextFilter, value: string) => {
      setParams(prev => {
        const next = new URLSearchParams(prev);
        const trimmed = value.trim();
        if (trimmed) {
          next.set(name, trimmed);
        } else {
          next.delete(name);
        }
        return next;
      });
    },
    [setParams],
  );
```

Add `textFilters, setTextFilter` to the returned object.

- [ ] **Step 4: Run to verify pass**

Run: `cd web && npx vitest run src/lib/list/__tests__/use-list-url-state.test.tsx`
Expected: PASS (new + existing).

- [ ] **Step 5: Commit**

```bash
git add web/src/lib/list/useListUrlState.ts web/src/lib/list/__tests__/use-list-url-state.test.tsx
git commit -m "feat(web): add text-filter URL support to useListUrlState"
```

---

### Task 4: `FilterSpec` types + `useListFilters` hook

**Files:**
- Create: `web/src/lib/list/filters/types.ts`
- Create: `web/src/lib/list/filters/useListFilters.ts`
- Test: `web/src/lib/list/filters/__tests__/useListFilters.test.tsx`

**Interfaces:**
- Consumes: `ListUrlState` from Task 3 (`textFilters`, `setTextFilter`).
- Produces:
  - `FilterSpec` = `{ key: string; type: "text"; label: string; placeholder?: string }` (union members `"single-select" | "multi-select" | "boolean" | "date-range"` reserved).
  - `useListFilters(specs: FilterSpec[], urlState): { values: Record<string,string>; bind: (key: string) => { value: string; onChange: (v: string) => void }; clearAll: () => void; isActive: boolean; queryFilters: Record<string, string | undefined> }`. Consumed by Tasks 5 + 6.

- [ ] **Step 1: Write `types.ts`**

```ts
/** Declarative filter descriptor rendered by <FilterBar> (ADR-0107). */
export type FilterSpec =
  | { key: string; type: "text"; label: string; placeholder?: string }
  // Reserved per ADR-0107 clause 1 — typed now, built when a screen needs them.
  | { key: string; type: "single-select" | "multi-select" | "boolean" | "date-range"; label: string };

export type FilterValues = Record<string, string>;
```

- [ ] **Step 2: Write failing tests** — `web/src/lib/list/filters/__tests__/useListFilters.test.tsx`:

```tsx
import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { renderHook, act } from "@testing-library/react";
import { useListFilters } from "../useListFilters";
import type { FilterSpec } from "../types";

const specs: FilterSpec[] = [
  { key: "displayNameContains", type: "text", label: "Search teams", placeholder: "Search by name…" },
];

function fakeUrlState(initial: Record<string, string> = {}) {
  const state = { textFilters: { displayNameContains: "", ...initial } };
  const setTextFilter = vi.fn((name: string, value: string) => {
    state.textFilters = { ...state.textFilters, [name]: value.trim() };
  });
  return { state, setTextFilter };
}

beforeEach(() => vi.useFakeTimers());
afterEach(() => vi.useRealTimers());

describe("useListFilters", () => {
  it("debounces the commit to setTextFilter (300ms)", () => {
    const u = fakeUrlState();
    const { result } = renderHook(() =>
      useListFilters(specs, { textFilters: u.state.textFilters, setTextFilter: u.setTextFilter } as never));

    act(() => { result.current.bind("displayNameContains").onChange("pl"); });
    expect(u.setTextFilter).not.toHaveBeenCalled();        // immediate value only
    expect(result.current.values.displayNameContains).toBe("pl");

    act(() => { vi.advanceTimersByTime(300); });
    expect(u.setTextFilter).toHaveBeenCalledWith("displayNameContains", "pl");
  });

  it("queryFilters reflects committed values; undefined when empty", () => {
    const u = fakeUrlState({ displayNameContains: "pl" });
    const { result } = renderHook(() =>
      useListFilters(specs, { textFilters: u.state.textFilters, setTextFilter: u.setTextFilter } as never));
    expect(result.current.queryFilters.displayNameContains).toBe("pl");

    const empty = fakeUrlState();
    const { result: r2 } = renderHook(() =>
      useListFilters(specs, { textFilters: empty.state.textFilters, setTextFilter: empty.setTextFilter } as never));
    expect(r2.current.queryFilters.displayNameContains).toBeUndefined();
  });

  it("isActive true only when a committed value is non-empty", () => {
    const empty = fakeUrlState();
    const { result } = renderHook(() =>
      useListFilters(specs, { textFilters: empty.state.textFilters, setTextFilter: empty.setTextFilter } as never));
    expect(result.current.isActive).toBe(false);
  });

  it("clearAll resets every spec via setTextFilter('')", () => {
    const u = fakeUrlState({ displayNameContains: "pl" });
    const { result } = renderHook(() =>
      useListFilters(specs, { textFilters: u.state.textFilters, setTextFilter: u.setTextFilter } as never));
    act(() => { result.current.clearAll(); });
    expect(u.setTextFilter).toHaveBeenCalledWith("displayNameContains", "");
    expect(result.current.values.displayNameContains).toBe("");
  });
});
```

- [ ] **Step 3: Run to verify failure**

Run: `cd web && npx vitest run src/lib/list/filters/__tests__/useListFilters.test.tsx`
Expected: FAIL — module not found.

- [ ] **Step 4: Implement `useListFilters.ts`**

```ts
import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import type { ListUrlState } from "@/lib/list/useListUrlState";
import type { FilterSpec } from "./types";

const DEBOUNCE_MS = 300;

/**
 * Spec-driven filter state for list pages (ADR-0107). Composes useListUrlState:
 * the controlled input echoes the immediate local value, while the committed
 * value (URL + query) is debounced so the cursor does not reset on every
 * keystroke. `queryFilters` is what the list query hook spreads — committed
 * values only, undefined when empty (so the unfiltered query key matches the
 * pre-filter key).
 */
export function useListFilters(
  specs: FilterSpec[],
  urlState: Pick<ListUrlState<string, never, string>, "textFilters" | "setTextFilter">,
) {
  const textSpecs = useMemo(() => specs.filter(s => s.type === "text"), [specs]);
  const committed = urlState.textFilters;

  const seed = useCallback(
    () => Object.fromEntries(textSpecs.map(s => [s.key, committed[s.key] ?? ""])) as Record<string, string>,
    [textSpecs, committed],
  );

  const [local, setLocal] = useState<Record<string, string>>(seed);
  const timers = useRef<Record<string, ReturnType<typeof setTimeout>>>({});

  // Adopt the committed value when it changes from outside this hook (back/forward,
  // shared link, clearAll). After our own debounced commit, committed === local so
  // this is a no-op.
  useEffect(() => {
    setLocal(prev => {
      let changed = false;
      const next = { ...prev };
      for (const s of textSpecs) {
        const c = committed[s.key] ?? "";
        if (c !== prev[s.key]) { next[s.key] = c; changed = true; }
      }
      return changed ? next : prev;
    });
  }, [committed, textSpecs]);

  const onChange = useCallback(
    (key: string) => (value: string) => {
      setLocal(prev => ({ ...prev, [key]: value }));
      clearTimeout(timers.current[key]);
      timers.current[key] = setTimeout(() => urlState.setTextFilter(key, value), DEBOUNCE_MS);
    },
    [urlState],
  );

  const bind = useCallback(
    (key: string) => ({ value: local[key] ?? "", onChange: onChange(key) }),
    [local, onChange],
  );

  const clearAll = useCallback(() => {
    for (const s of textSpecs) {
      clearTimeout(timers.current[s.key]);
      urlState.setTextFilter(s.key, "");
    }
    setLocal(Object.fromEntries(textSpecs.map(s => [s.key, ""])));
  }, [textSpecs, urlState]);

  const queryFilters = useMemo(() => {
    const out: Record<string, string | undefined> = {};
    for (const s of textSpecs) out[s.key] = (committed[s.key] ?? "") || undefined;
    return out;
  }, [textSpecs, committed]);

  const isActive = useMemo(
    () => textSpecs.some(s => (committed[s.key] ?? "") !== ""),
    [textSpecs, committed],
  );

  return { values: local, bind, clearAll, isActive, queryFilters };
}
```

- [ ] **Step 5: Run to verify pass**

Run: `cd web && npx vitest run src/lib/list/filters/__tests__/useListFilters.test.tsx`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add web/src/lib/list/filters
git commit -m "feat(web): FilterSpec types + useListFilters hook (ADR-0107)"
```

---

### Task 5: `<FilterBar>` component

**Files:**
- Create: `web/src/components/application/filter-bar/FilterBar.tsx`
- Test: `web/src/components/application/filter-bar/__tests__/FilterBar.test.tsx`

**Interfaces:**
- Consumes: `FilterSpec` (Task 4), the `useListFilters` return shape (Task 4).
- Produces: `<FilterBar specs={FilterSpec[]} filters={ReturnType<typeof useListFilters>} />`.

- [ ] **Step 1: Write failing tests** — `__tests__/FilterBar.test.tsx`:

```tsx
import { describe, it, expect, vi } from "vitest";
import { render, screen, fireEvent } from "@testing-library/react";
import { FilterBar } from "../FilterBar";
import type { FilterSpec } from "@/lib/list/filters/types";

const specs: FilterSpec[] = [
  { key: "displayNameContains", type: "text", label: "Search teams", placeholder: "Search by name…" },
];

function filters(over: Partial<ReturnType<typeof makeFilters>> = {}) {
  return { ...makeFilters(), ...over };
}
function makeFilters() {
  return {
    values: { displayNameContains: "" },
    bind: vi.fn((key: string) => ({ value: "", onChange: vi.fn() })),
    clearAll: vi.fn(),
    isActive: false,
    queryFilters: {},
  };
}

describe("FilterBar", () => {
  it("renders a text control from the spec with its label", () => {
    render(<FilterBar specs={specs} filters={filters()} />);
    expect(screen.getByRole("textbox", { name: /search teams/i })).toBeInTheDocument();
  });

  it("typing calls the bound onChange", () => {
    const onChange = vi.fn();
    const f = filters({ bind: vi.fn(() => ({ value: "", onChange })) });
    render(<FilterBar specs={specs} filters={f} />);
    fireEvent.change(screen.getByRole("textbox", { name: /search teams/i }), { target: { value: "pl" } });
    expect(onChange).toHaveBeenCalledWith("pl");
  });

  it("shows Clear all only when active and calls clearAll", () => {
    const f = filters({ isActive: true });
    render(<FilterBar specs={specs} filters={f} />);
    const btn = screen.getByRole("button", { name: /clear all/i });
    fireEvent.click(btn);
    expect(f.clearAll).toHaveBeenCalled();
  });

  it("hides Clear all when inactive", () => {
    render(<FilterBar specs={specs} filters={filters({ isActive: false })} />);
    expect(screen.queryByRole("button", { name: /clear all/i })).toBeNull();
  });

  it("throws for an unbuilt control type", () => {
    const bad: FilterSpec[] = [{ key: "x", type: "boolean", label: "X" }];
    expect(() => render(<FilterBar specs={bad} filters={filters()} />)).toThrow(/not implemented/i);
  });
});
```

- [ ] **Step 2: Run to verify failure**

Run: `cd web && npx vitest run src/components/application/filter-bar/__tests__/FilterBar.test.tsx`
Expected: FAIL — module not found.

- [ ] **Step 3: Implement `FilterBar.tsx`**

```tsx
import { SearchLg } from "@untitledui/icons";
import { Button } from "@/components/base/buttons/button";
import { Input } from "@/components/base/input/input";
import type { FilterSpec } from "@/lib/list/filters/types";
import type { useListFilters } from "@/lib/list/filters/useListFilters";

interface FilterBarProps {
  specs: FilterSpec[];
  filters: ReturnType<typeof useListFilters>;
}

/**
 * Standard list-filter surface (ADR-0107). Renders each FilterSpec above the
 * DataTable. MVP builds the `text` control only; other types throw so misuse
 * fails loudly at dev time until they are implemented.
 */
export function FilterBar({ specs, filters }: FilterBarProps) {
  return (
    <div className="flex flex-wrap items-center gap-3">
      {specs.map(spec => {
        if (spec.type !== "text") {
          throw new Error(
            `FilterBar: "${spec.type}" control not implemented (ADR-0107 clause 1 — text only)`,
          );
        }
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
            />
          </div>
        );
      })}

      {filters.isActive && (
        <Button size="sm" color="link-gray" onClick={filters.clearAll}>
          Clear all
        </Button>
      )}
    </div>
  );
}
```

If `color="link-gray"` is not a valid `Button` color in this codebase, use the closest tertiary/link variant the `Button` component exposes (confirm against an existing `Button` usage such as `CatalogListPage`'s Reset button); the behavior (onClick → clearAll) is what the test asserts.

- [ ] **Step 4: Run to verify pass**

Run: `cd web && npx vitest run src/components/application/filter-bar/__tests__/FilterBar.test.tsx`
Expected: PASS. (react-aria `TextField` renders a `textbox` named by `aria-label`; its `onChange` passes the string value directly.)

- [ ] **Step 5: Commit**

```bash
git add web/src/components/application/filter-bar
git commit -m "feat(web): shared <FilterBar> text control (ADR-0107)"
```

---

### Task 6: Wire Teams list — API param + page + FilterBar + default asc

**Files:**
- Modify: `web/src/features/teams/api/teams.ts`
- Modify: `web/src/features/teams/pages/TeamsListPage.tsx`
- Test: `web/src/features/teams/api/__tests__/teams.test.tsx`
- Test: `web/src/features/teams/pages/__tests__/TeamsListPage.test.tsx`

**Interfaces:**
- Consumes: `useListUrlState` (Task 3), `useListFilters` + `FilterSpec` (Task 4), `<FilterBar>` (Task 5), generated `displayNameContains` param (Task 2).
- Produces: `TeamsListParams` gains `displayNameContains?: string`; `useTeamsList` forwards it as a query param.

- [ ] **Step 1: Write failing tests**

Append to `web/src/features/teams/api/__tests__/teams.test.tsx` (mirror existing apiClient mock; assert the param flows into the GET query):

```tsx
it("forwards displayNameContains into the request query", async () => {
  const get = vi.fn().mockResolvedValue({ data: { items: [], nextCursor: null, prevCursor: null }, error: undefined });
  vi.spyOn(clientModule, "apiClient", "get").mockReturnValue({ GET: get } as never);

  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  renderHook(
    () => useTeamsList({ sortBy: "displayName", sortOrder: "asc", displayNameContains: "pl" }),
    { wrapper: ({ children }) => <QueryClientProvider client={qc}>{children}</QueryClientProvider> },
  );

  await waitFor(() => expect(get).toHaveBeenCalled());
  expect(get.mock.calls[0][1].params.query).toMatchObject({ displayNameContains: "pl" });
});
```
(Import `renderHook`, `waitFor`, `QueryClient`, `QueryClientProvider`, and `useTeamsList` at the top if the file does not already.)

Append to `web/src/features/teams/pages/__tests__/TeamsListPage.test.tsx`:

```tsx
it("renders the search filter and uses displayName-asc as default sort", async () => {
  const get = vi.fn().mockResolvedValue({ data: pageOf([]), error: undefined });
  vi.spyOn(clientModule, "apiClient", "get").mockReturnValue({
    GET: get, POST: vi.fn(), PUT: vi.fn(), DELETE: vi.fn(),
  } as never);

  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(<TeamsListPage />, { wrapper: harness(qc) });

  expect(screen.getByRole("textbox", { name: /search teams/i })).toBeInTheDocument();
  await waitFor(() => expect(get).toHaveBeenCalled());
  expect(get.mock.calls[0][1].params.query).toMatchObject({ sortBy: "displayName", sortOrder: "asc" });
});

it("shows the no-matches empty state when a filter is active and no rows", async () => {
  const get = vi.fn().mockResolvedValue({ data: pageOf([]), error: undefined });
  vi.spyOn(clientModule, "apiClient", "get").mockReturnValue({
    GET: get, POST: vi.fn(), PUT: vi.fn(), DELETE: vi.fn(),
  } as never);

  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(<TeamsListPage />, {
    wrapper: ({ children }: { children: React.ReactNode }) => (
      <QueryClientProvider client={qc}>
        <MemoryRouter initialEntries={["/?displayNameContains=zzz"]}>{children}</MemoryRouter>
      </QueryClientProvider>
    ),
  });

  await waitFor(() => expect(screen.getByText(/no teams match/i)).toBeInTheDocument());
});
```

- [ ] **Step 2: Run to verify failure**

Run: `cd web && npx vitest run src/features/teams`
Expected: FAIL — `displayNameContains` not forwarded; no search textbox; default still `createdAt`.

- [ ] **Step 3: Add the param to `teams.ts`**

In `TeamsListParams`:
```ts
type TeamsListParams = {
  sortBy: NonNullable<ListTeamsQuery["sortBy"]>;
  sortOrder: NonNullable<ListTeamsQuery["sortOrder"]>;
  limit?: number;
  displayNameContains?: string;
};
```
In `useTeamsList`'s `query` object, add `displayNameContains: params.displayNameContains,` alongside `sortBy`/`sortOrder`/`limit`/`cursor`. (`teamKeys.list(params)` already includes the whole `params` object, so the value lands in the query key and `useCursorList` resets pagination when it changes.)

- [ ] **Step 4: Wire `TeamsListPage.tsx`**

Replace the imports/top of the component:
```tsx
import { useMemo, useState, useEffect } from "react";
import { FilterBar } from "@/components/application/filter-bar/FilterBar";
import { useListFilters } from "@/lib/list/filters/useListFilters";
import type { FilterSpec } from "@/lib/list/filters/types";
// …existing imports (Button, Card, useTeamsList, CreateTeamDialog, useListUrlState, usePermissions, KartovaPermissions)

const ALLOWED_SORT_FIELDS = ["createdAt", "displayName"] as const;
const FILTER_SPECS: FilterSpec[] = [
  { key: "displayNameContains", type: "text", label: "Search teams", placeholder: "Search by name…" },
];

export function TeamsListPage() {
  const urlState = useListUrlState({
    defaultSortBy: "displayName",
    defaultSortOrder: "asc",
    allowedSortFields: ALLOWED_SORT_FIELDS,
    textFilters: ["displayNameContains"] as const,
  });
  const filters = useListFilters(FILTER_SPECS, urlState);

  const list = useTeamsList({
    sortBy: urlState.sortBy,
    sortOrder: urlState.sortOrder,
    displayNameContains: filters.queryFilters.displayNameContains,
  });
  const [dialogOpen, setDialogOpen] = useState(false);
  const { hasPermission, isLoading: permissionsLoading } = usePermissions();
  const canCreate = !permissionsLoading && hasPermission(KartovaPermissions.TeamCreate);

  useEffect(() => {
    if (list.isError) console.error("TeamsListPage list error", list.error);
  }, [list.isError, list.error]);
```

Add `<FilterBar specs={FILTER_SPECS} filters={filters} />` directly below the header `<div>` (above the error/loading/empty/table block). Change the empty-state copy to distinguish matches:
```tsx
) : list.items.length === 0 ? (
  <Card className="mx-auto max-w-md text-center">
    <CardContent className="space-y-2 p-8">
      <p className="text-base font-medium text-primary">
        {filters.isActive ? "No teams match your search" : "No teams yet"}
      </p>
      <p className="text-sm text-tertiary">
        {filters.isActive ? "Try a different name." : "Create your first team."}
      </p>
```
(Keep the rest of the page — table render, Create button, dialog — unchanged.)

- [ ] **Step 5: Run to verify pass**

Run: `cd web && npx vitest run src/features/teams`
Expected: PASS (new + existing TeamsListPage/teams tests).

- [ ] **Step 6: Typecheck + build the web bundle**

Run: `cd web && npm run build`
Expected: tsc + vite succeed (no type errors from the new generic params or `displayNameContains`).

- [ ] **Step 7: Commit**

```bash
git add web/src/features/teams
git commit -m "feat(web): Teams list search via <FilterBar> + displayName-asc default"
```

---

### Task 7: Verification, mutation gate, registry flip, close-out

**Files:**
- Modify: `docs/design/list-filter-registry.md`

- [ ] **Step 1: Full CI mirror**

Run: `bash scripts/ci-local.sh` (or `scripts/ci-local.sh backend` + `scripts/ci-local.sh frontend`).
Expected: Release build (0 warnings), full unit + architecture + integration suites, web image build all green. If Docker/dev-stack is unavailable in-session, flag the integration + web-image legs as *pending user verification*.

- [ ] **Step 2: Mutation gate (6) — APPLIES (C# Application/Infrastructure logic changed)**

Run `/misc:mutation-sentinel` then `/misc:test-generator` scoped to the changed C# files: `ListTeamsHandler.cs`, `ListTeamsQuery.cs`, `TeamEndpointDelegates.cs` (`ListTeamsAsync`). Target ≥80% (`stryker-config.json`). Document any survivors (e.g. the `EscapeLike` branch) and add tests until killed or justified.

- [ ] **Step 3: Manual verification (ADR-0084, Playwright MCP)**

Cold-start the dev server, then: navigate `/teams` → type in the search box → list narrows after the debounce → "Clear all" restores → confirm the URL carries `?displayNameContains=` and is shareable → console clean. Flag *pending user verification* if the stack is unavailable in-session.

- [ ] **Step 4: Flip the registry row**

In `docs/design/list-filter-registry.md`, change the Teams row to:
```
| Teams | `/teams` | `displayName` text search | **built** | E-03.F-02 | Renders via the shared `<FilterBar>` + `useListFilters`; default sort `displayName asc`. First consumer of the ADR-0107 surface. |
```

- [ ] **Step 5: Terminal re-verify + commit**

Re-run build + full suite (gates 5–9 may have applied fixes). Then:
```bash
git add docs/design/list-filter-registry.md
git commit -m "docs(design): mark Teams list filter built (ADR-0107 first consumer)"
```

- [ ] **Step 6: Slice-boundary reviews (DoD gates 7–9)**

Run `/superpowers:requesting-code-review`, `/pr-review-toolkit:review-pr`, `/deep-review` against the full branch diff (spec + this plan as context). Address blocking + should-fix; triage nits.

---

## Self-Review

**Spec coverage:** §3 decisions → Task 1 (#2 f-map key, #3 displayName-only, #4 ILIKE+escape+blank, #6 default asc, #8 no new ProblemDetails), Task 3 (#1 useListUrlState text support), Task 4 (#1 approach A hook, #5 debounce, #7 text-only union), Task 5 (#7 text-only render + throw, #9 responsive `w-full sm:w-72`), Task 6 (Teams consumer, matches-vs-empty), Task 2 (OpenAPI). §6 gate-5 artifacts → every named test file appears as a task test. §7 mutation gate → Task 7 Step 2. Registry flip → Task 7 Step 4.

**Placeholder scan:** no TBD/TODO. Two dev-time confirmations flagged with the exact fallback (`EF.Functions.ILike` 3-arg `using`; `Button` link color variant) — mechanical, not open design questions. The OpenApi snapshot update follows the test's own documented path (it is the established pattern on this branch's `OpenApiTests`).

**Type consistency:** `displayNameContains` identical across URL param, API query param, `f`-map key, `TeamsListParams`, and `ListTeamsQuery.DisplayNameContains`. `useListFilters` return shape (`values`/`bind`/`clearAll`/`isActive`/`queryFilters`) consistent across its definition (Task 4), `<FilterBar>` consumption (Task 5), and `TeamsListPage` (Task 6). Default sort `displayName`/`asc` consistent across Task 1 (endpoint), Task 6 (screen), and their tests.

No issues outstanding.
