# De-dup API edges from Relationships list — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** On Application/Service detail pages, stop the Relationships list from re-listing `providesApiFor` / `consumesApiFrom` edges (the API-surface section covers them richer); leave the API detail page unchanged.

**Architecture:** Server-side exclusion, opt-in via a new `excludeApiEdges` query flag on `GET /catalog/relationships`. Full-variant callers (App/Service `RelationshipsSection`) set it; the API page's `incoming-only` variant does not. Client-side filtering is rejected — it would desync the cursor pager.

**Tech Stack:** .NET 10 / ASP.NET Core Minimal API · EF Core · Wolverine · PostgreSQL 18 (RLS) · React + TypeScript · openapi-typescript codegen · MSTest v4 (integration, real seam) · Vitest + Testing Library (frontend).

**Spec:** `docs/superpowers/specs/2026-07-13-relationships-api-edge-dedup-design.md`

## Global Constraints

- Solution file: `Kartova.slnx`. Windows shell: `cmd //c` or PowerShell wrappers for `dotnet`.
- `TreatWarningsAsErrors=true` — 0 warnings, 0 errors (gate 1).
- New/changed C# on the wire path needs real-seam integration tests: `KartovaApiFixtureBase`, real Postgres/RLS + real JWT (gate 3/5).
- `.cs` files are LF (`.gitattributes eol=lf`); don't introduce CRLF.
- Cursor list contract (ADR-0095) — do not break `CursorPage<T>` shape; exclusion applies pre-pagination.
- Edge model authority: ADR-0111 (unchanged — this slice is display-only). Tabbed layout: ADR-0114 (amended: tab-order convention, doc-only).
- Default `excludeApiEdges = false` ⇒ endpoint backward-compatible; existing callers/tests must stay green untouched.

---

## Impact Analysis (codelens)

**Method:** roslyn-codelens (`find_callers` / `find_references`). **codelens MCP is NOT loaded this session** — table grounded with `Grep` as a stopgap. **Re-run `find_callers`/`find_references` at execution time before editing; add a task for any caller not in this table.**

| Changed symbol | Change | Tool run | Callers / refs | Notable call sites | Covered by task |
|----------------|--------|----------|----------------|--------------------|-----------------|
| `Kartova.Catalog.Application.ListRelationshipsForEntityQuery` | signature (add `bool ExcludeApiEdges = false` positional param) | `grep — codelens unavailable` | 1 construction | `CatalogEndpointDelegates.cs:833` (Catalog.Infrastructure) | Task 1 |
| `Kartova.Catalog.Infrastructure.ListRelationshipsForEntityHandler.Handle` | behavior (conditional `Where`) | `grep — codelens unavailable` | 1 call | `CatalogEndpointDelegates.cs:839` | Task 1 |
| `Kartova.Catalog.Infrastructure.CatalogEndpointDelegates.ListRelationshipsAsync` | signature (add `[FromQuery] bool? excludeApiEdges`) | `grep — codelens unavailable` | 1 map | `CatalogModule.cs:153` (`MapGet("/relationships", …)`) | Task 1 |
| FE `useRelationshipsList` | signature (optional `excludeApiEdges` in params) | `grep` | 2 calls | `RelationshipsSection.tsx:45,49` | Task 3 |

**Blast-radius notes:** All C# callers are inside `Kartova.Catalog.Infrastructure` — no cross-module reach, no interface implementors, no event/handler fan-out. Adding a trailing defaulted positional param to the record is safe: the sole construction site uses named args for the trailing fields. FE hook is consumed only by `RelationshipsSection`; the new param is optional.

**Coverage check:** every caller/reference above is handled — Task 1 updates the C# construction + handler + endpoint; Task 3 updates the FE hook and its two call sites. No gaps.

---

## Task 1: Backend — `excludeApiEdges` flag end-to-end (query · handler · endpoint) + real-seam tests

**Files:**
- Modify: `src/Modules/Catalog/Kartova.Catalog.Application/ListRelationshipsForEntityQuery.cs`
- Modify: `src/Modules/Catalog/Kartova.Catalog.Infrastructure/ListRelationshipsForEntityHandler.cs:22-32`
- Modify: `src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogEndpointDelegates.cs:807-841`
- Test: `src/Modules/Catalog/Kartova.Catalog.IntegrationTests/ListRelationshipsTests.cs`

**Interfaces:**
- Consumes: existing `ListRelationshipsForEntityQuery(EntityRef, RelationshipDirection, RelationshipSortField, SortOrder, string?, int)`; `RelationshipType.{ProvidesApiFor,ConsumesApiFrom}`.
- Produces: `ListRelationshipsForEntityQuery` with trailing `bool ExcludeApiEdges = false`; `GET /catalog/relationships` accepts `&excludeApiEdges=true|false` (default false).

- [ ] **Step 1: Write the failing integration tests**

Add to `ListRelationshipsTests.cs` (before the final `}`). Reuses the file's existing `SeedServiceAsync` / `Rel` helpers. Seeds two services + an API, wires a `dependsOn` and a `providesApiFor` edge, then asserts the flag filters correctly and the default does not.

```csharp
    private static async Task<Guid> SeedApiAsync(HttpClient client, Guid teamId, string name)
    {
        var resp = await client.PostAsJsonAsync("/api/v1/catalog/apis", new
        {
            displayName = name, description = "x", teamId, style = "Rest", version = "v1",
        });
        Assert.AreEqual(HttpStatusCode.Created, resp.StatusCode, $"SeedApi '{name}' failed: {resp.StatusCode}");
        var body = await resp.Content.ReadFromJsonAsync<ApiResponse>(KartovaApiFixtureBase.WireJson);
        return body!.Id;
    }

    [TestMethod]
    public async Task GET_outgoing_with_excludeApiEdges_omits_provide_and_consume_edges()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Rel Excl Team");
        var svc = await SeedServiceAsync(client, teamId, "excl-svc");
        var dep = await SeedServiceAsync(client, teamId, "excl-dep");
        var api = await SeedApiAsync(client, teamId, "excl-api");
        await client.PostAsJsonAsync("/api/v1/catalog/relationships",
            Rel(EntityKind.Service, svc, RelationshipType.DependsOn, EntityKind.Service, dep));
        await client.PostAsJsonAsync("/api/v1/catalog/relationships",
            Rel(EntityKind.Service, svc, RelationshipType.ProvidesApiFor, EntityKind.Api, api));

        var page = await (await client.GetAsync(
            $"/api/v1/catalog/relationships?entityKind=Service&entityId={svc}&direction=outgoing&excludeApiEdges=true"))
            .Content.ReadFromJsonAsync<CursorPage<RelationshipResponse>>(KartovaApiFixtureBase.WireJson);

        Assert.AreEqual(1, page!.Items.Count);
        Assert.AreEqual(RelationshipType.DependsOn.ToString(), page.Items[0].Type, ignoreCase: true);
    }

    [TestMethod]
    public async Task GET_outgoing_default_still_returns_api_edges()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Rel Incl Team");
        var svc = await SeedServiceAsync(client, teamId, "incl-svc");
        var dep = await SeedServiceAsync(client, teamId, "incl-dep");
        var api = await SeedApiAsync(client, teamId, "incl-api");
        await client.PostAsJsonAsync("/api/v1/catalog/relationships",
            Rel(EntityKind.Service, svc, RelationshipType.DependsOn, EntityKind.Service, dep));
        await client.PostAsJsonAsync("/api/v1/catalog/relationships",
            Rel(EntityKind.Service, svc, RelationshipType.ProvidesApiFor, EntityKind.Api, api));

        var page = await (await client.GetAsync(
            $"/api/v1/catalog/relationships?entityKind=Service&entityId={svc}&direction=outgoing"))
            .Content.ReadFromJsonAsync<CursorPage<RelationshipResponse>>(KartovaApiFixtureBase.WireJson);

        Assert.AreEqual(2, page!.Items.Count);
    }
```

Add `using` if not present: the file already imports `Kartova.Catalog.Contracts` (has `ApiResponse`) and `Kartova.Catalog.Domain`. Confirm `ApiResponse` exists in `Kartova.Catalog.Contracts`; if the property/type name differs, read `Kartova.Catalog.Contracts/ApiResponse.cs` and adjust the deserialize target.

- [ ] **Step 2: Run the new tests to verify they fail**

Run: `cmd //c "dotnet test src/Modules/Catalog/Kartova.Catalog.IntegrationTests --filter FullyQualifiedName~ListRelationshipsTests.GET_outgoing_with_excludeApiEdges_omits_provide_and_consume_edges"`
Expected: FAIL — the `excludeApiEdges` param is ignored today, so 2 items returned, `AreEqual(1, …)` fails. (`GET_outgoing_default_still_returns_api_edges` passes already — it pins the backward-compatible default.)

- [ ] **Step 3: Add `ExcludeApiEdges` to the query record**

`ListRelationshipsForEntityQuery.cs` — add trailing defaulted param:

```csharp
public sealed record ListRelationshipsForEntityQuery(
    EntityRef Entity, RelationshipDirection Direction,
    RelationshipSortField SortBy, SortOrder SortOrder, string? Cursor, int Limit,
    bool ExcludeApiEdges = false);
```

- [ ] **Step 4: Apply the conditional filter in the handler**

`ListRelationshipsForEntityHandler.cs` — after the `source` switch (line 32), before `.ToCursorPagedAsync`:

```csharp
        if (q.ExcludeApiEdges)
            source = source.Where(r =>
                r.Type != RelationshipType.ProvidesApiFor &&
                r.Type != RelationshipType.ConsumesApiFrom);
```

- [ ] **Step 5: Bind the query param in the endpoint delegate**

`CatalogEndpointDelegates.cs::ListRelationshipsAsync` — add `[FromQuery] bool? excludeApiEdges` to the signature (after `limit`), and pass it into the query construction:

```csharp
        [FromQuery] string? cursor,
        [FromQuery] string? limit,
        [FromQuery] bool? excludeApiEdges,
        ListRelationshipsForEntityHandler handler,
```
```csharp
        var query = new ListRelationshipsForEntityQuery(
            new EntityRef(kind, entityId), dir,
            SortBy: parsedSortBy ?? RelationshipSortField.CreatedAt,
            SortOrder: parsedSortOrder ?? SortOrder.Desc,
            Cursor: cursor, Limit: effectiveLimit,
            ExcludeApiEdges: excludeApiEdges ?? false);
```

Also extend the XML `<summary>` on the delegate (line 802-806) to mention `excludeApiEdges` (default false; excludes provide/consume edges).

- [ ] **Step 6: Run the Catalog integration suite + build**

Run: `cmd //c "dotnet test src/Modules/Catalog/Kartova.Catalog.IntegrationTests --filter FullyQualifiedName~ListRelationshipsTests"`
Expected: PASS (all prior tests + the 2 new ones). If Docker named-pipe timeout flakes, re-run the assembly in isolation (see memory: full-suite Docker flake).

- [ ] **Step 7: Commit**

```bash
git add src/Modules/Catalog/Kartova.Catalog.Application/ListRelationshipsForEntityQuery.cs \
        src/Modules/Catalog/Kartova.Catalog.Infrastructure/ListRelationshipsForEntityHandler.cs \
        src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogEndpointDelegates.cs \
        src/Modules/Catalog/Kartova.Catalog.IntegrationTests/ListRelationshipsTests.cs
git commit -m "feat(catalog): excludeApiEdges flag on GET /relationships (E-04.F-01.S-05, #71)"
```

---

## Task 2: Regenerate OpenAPI snapshot + typed client

**Files:**
- Modify (generated): `web/openapi-snapshot.json`
- Modify (generated, gitignored — regenerated): `web/src/generated/openapi.d.ts`

**Interfaces:**
- Consumes: the new `excludeApiEdges` query param on `operations["ListRelationships"]`.
- Produces: TS type `operations["ListRelationships"]["parameters"]["query"].excludeApiEdges?: boolean` available to Task 3.

- [ ] **Step 1: Rebuild the API image / start API so the live OpenAPI includes the new param**

Per project convention (memory: rebuild API image to expose new endpoints). Cold-start the API (or `docker compose build api && docker compose up -d api`) so the `/openapi` document reflects the new query param.

- [ ] **Step 2: Regenerate the snapshot + client**

Run: `cmd //c "cd web && npm run predev"` (predev/prebuild regenerates `openapi-snapshot.json` from the live API, then codegen). If the API isn't reachable, regenerate from the running instance per `project_web_codegen_and_tsc_gate` / `project_openapi_snapshot_codegen` memory notes.

- [ ] **Step 3: Verify the new param is present**

Run: `cmd //c "cd web && npx tsc -b"` (the binding type gate). Confirm no type errors, and grep the snapshot for `excludeApiEdges`.
Expected: `excludeApiEdges` present in `web/openapi-snapshot.json` under the relationships GET query params.

- [ ] **Step 4: Commit**

```bash
git add web/openapi-snapshot.json
git commit -m "chore(web): regenerate openapi snapshot for excludeApiEdges param"
```

---

## Task 3: Frontend — plumb `excludeApiEdges`, gate on variant + tests

**Files:**
- Modify: `web/src/features/catalog/api/relationships.ts:13-51`
- Modify: `web/src/features/catalog/components/RelationshipsSection.tsx:37-49`
- Test: `web/src/features/catalog/components/__tests__/RelationshipsSection.test.tsx`

**Interfaces:**
- Consumes: `operations["ListRelationships"]` query type with `excludeApiEdges` (Task 2); `useCursorList`.
- Produces: `RelationshipsSection` requests `excludeApiEdges=true` when `variant="full"`, unset when `variant="incoming-only"`.

- [ ] **Step 1: Write the failing frontend test**

Add to `RelationshipsSection.test.tsx`. Assert the outgoing request carries `excludeApiEdges` for the full variant and omits it for incoming-only. Match the file's existing fetch-mock/apiClient-spy pattern (read the current test to reuse its harness — do not invent a new mock).

```tsx
it("full variant requests relationships with excludeApiEdges", async () => {
  const spy = mockRelationshipsGet(); // reuse existing helper/spy in this file
  renderSection({ variant: "full" }); // reuse existing render helper
  await waitFor(() => expect(spy).toHaveBeenCalled());
  const call = spy.mock.calls.find((c) => c[1]?.params?.query?.direction === "outgoing");
  expect(call?.[1]?.params?.query?.excludeApiEdges).toBe("true");
});

it("incoming-only variant does not set excludeApiEdges", async () => {
  const spy = mockRelationshipsGet();
  renderSection({ variant: "incoming-only" });
  await waitFor(() => expect(spy).toHaveBeenCalled());
  const call = spy.mock.calls.find((c) => c[1]?.params?.query?.direction === "incoming");
  expect(call?.[1]?.params?.query?.excludeApiEdges).toBeUndefined();
});
```

If the existing test file spies differently (e.g. asserts on `apiClient.GET` args or MSW handlers), adapt these two assertions to that mechanism — the behavioral claim (full ⇒ `excludeApiEdges:"true"`, incoming-only ⇒ absent) is what must hold.

- [ ] **Step 2: Run the test to verify it fails**

Run: `cmd //c "cd web && npx vitest run src/features/catalog/components/__tests__/RelationshipsSection.test.tsx"`
Expected: FAIL — `excludeApiEdges` not sent yet.

- [ ] **Step 3: Add the param to the hook**

`relationships.ts` — extend params + forward the query value:

```ts
export type RelationshipsListParams = {
  entityKind: NonNullable<ListQuery["entityKind"]>;
  entityId: string;
  direction: RelationshipDirection;
  limit?: number;
  excludeApiEdges?: boolean;
};
```
In `useRelationshipsList`'s `fetchPage` query object add (only when true, to keep the param absent otherwise):

```ts
          query: {
            entityKind: params.entityKind,
            entityId: params.entityId,
            direction: params.direction,
            limit: String(params.limit ?? 20),
            cursor,
            ...(params.excludeApiEdges ? { excludeApiEdges: "true" } : {}),
          },
```

- [ ] **Step 4: Gate on variant in `RelationshipsSection`**

`RelationshipsSection.tsx` — after `const readOnly = variant === "incoming-only";` derive the flag and pass it to both list hooks:

```tsx
  const excludeApiEdges = variant === "full";

  const outgoing = useRelationshipsList(
    { entityKind, entityId, direction: "outgoing", excludeApiEdges },
    { enabled: variant === "full" },
  );
  const incoming = useRelationshipsList({ entityKind, entityId, direction: "incoming", excludeApiEdges });
```

(For `incoming-only`, `excludeApiEdges` is `false` ⇒ param omitted ⇒ API page providers/consumers preserved.)

- [ ] **Step 5: Run the test to verify it passes**

Run: `cmd //c "cd web && npx vitest run src/features/catalog/components/__tests__/RelationshipsSection.test.tsx"`
Expected: PASS.

- [ ] **Step 6: Guard against duplication on the detail pages**

Add/extend one assertion each in `ApplicationDetailPage.test.tsx` and `ServiceDetailPage.test.tsx`: with an entity having a `providesApiFor` edge, the API name appears once (API-surface section) and NOT as a Relationships Outgoing row. Reuse each file's existing mock setup; if these tests don't currently mock both the api-surface and relationships endpoints, extend the existing mock rather than adding a new harness.

Run: `cmd //c "cd web && npx vitest run src/features/catalog/pages/__tests__/ApplicationDetailPage.test.tsx src/features/catalog/pages/__tests__/ServiceDetailPage.test.tsx"`
Expected: PASS.

- [ ] **Step 7: Typecheck + full frontend suite**

Run: `cmd //c "cd web && npx tsc -b && npx vitest run"`
Expected: PASS, 0 type errors.

- [ ] **Step 8: Commit**

```bash
git add web/src/features/catalog/api/relationships.ts \
        web/src/features/catalog/components/RelationshipsSection.tsx \
        web/src/features/catalog/components/__tests__/RelationshipsSection.test.tsx \
        web/src/features/catalog/pages/__tests__/ApplicationDetailPage.test.tsx \
        web/src/features/catalog/pages/__tests__/ServiceDetailPage.test.tsx
git commit -m "feat(web): exclude API edges from Relationships list on App/Service detail (#71)"
```

---

## Task 4: ADR-0114 tab-order convention note + CHECKLIST update

**Files:**
- Modify: `docs/architecture/decisions/ADR-0114-*.md` (tabbed entity-detail)
- Modify: `docs/product/CHECKLIST.md:188` (E-04.F-01.S-05 row)

**Interfaces:** docs only — no code.

- [ ] **Step 1: Amend ADR-0114 with the ordering convention**

Add a dated amendment note: tab order is **Overview → Dependencies → entity-specific content → cross-cutting**, Dependencies fixed at position 2 across all entity kinds. Current sets already comply (App/Service: `Overview · Dependencies`; API: `Overview · Dependencies · Definition`) — documentation only, no code change this slice.

- [ ] **Step 2: Update CHECKLIST**

Mark `E-04.F-01.S-05` done with a one-line summary (excludeApiEdges flag; API surface is the canonical home; API detail page unaffected; ADR-0114 ordering note). Note it resolves issue #71.

- [ ] **Step 3: Commit**

```bash
git add docs/architecture/decisions/ADR-0114-*.md docs/product/CHECKLIST.md
git commit -m "docs(catalog): ADR-0114 tab-order note + CHECKLIST E-04.F-01.S-05 (#71)"
```

---

## Self-review

- **Spec coverage:** backend exclude (Task 1) · snapshot/types (Task 2) · FE plumb + variant gate + dup guard (Task 3) · ADR-0114 note + checklist (Task 4). J1–J6 journeys map to Task 1 tests (J1/J2/J5/J6) + Task 3 dup-guard (J1/J2) + J3 = the backward-compatible default (Task 1 `GET_outgoing_default_still_returns_api_edges` + no change to incoming-only path). All covered.
- **Placeholder scan:** none — every code step shows exact code; test-harness adaptation notes point at reading the existing file, not "TODO".
- **Type consistency:** `ExcludeApiEdges` (C# record) ↔ `excludeApiEdges` (query param / TS) consistent; default `false` everywhere; `RelationshipType.{ProvidesApiFor,ConsumesApiFrom}` match the enum.

## DoD

Ten always-blocking gates (CLAUDE.md). Gate-5 real-seam = Task 1 tests. Gate-6 mutation conditional-blocking (Application/Infra `Where` touched) ⇒ run on changed Catalog files. Gate-10 visual ⇒ browser-verify App/Service Dependencies tab (each API once) + API detail page still lists providers/consumers (flag pending if Playwright MCP unavailable). Ledger: `docs/superpowers/verification/2026-07-13-relationships-api-edge-dedup/dod.md`.
