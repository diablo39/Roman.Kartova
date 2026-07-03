# Task 6 report — Endpoint delegates + routes + `ApiNotFound`

## What was added

### `EndpointResultExtensions.cs`
Added `internal static IResult ApiNotFound()` immediately after `ServiceNotFound()`, calling the shared `ResourceNotFound("API", "No API with that id is visible in the current tenant.")` helper — same shape as `ApplicationNotFound()` / `ServiceNotFound()`.

### `CatalogEndpointDelegates.cs`
Added three delegates immediately after `ListServicesAsync` (before `CreateRelationshipAsync`):

- `RegisterApiAsync` — mirrors `RegisterServiceAsync`: 422 invalid-team via `IOrganizationTeamExistenceChecker.ExistsAsync`, then `AuthorizeTargetTeamAsync(auth, caller, request.TeamId)` 403 gate, then dispatches `RegisterApiHandler.Handle(new RegisterApiCommand(...))` and returns `Results.Created($"/api/v1/catalog/apis/{response.Id}", response)`.
- `GetApiByIdAsync` — dispatches `GetApiByIdHandler.Handle(new GetApiByIdQuery(id))`; returns `EndpointResultExtensions.ApiNotFound()` on null, else `Results.Ok(resp)`. **No `.WithEtag(...)` call** — confirmed by diff, matches the "no concurrency token exposed this slice" requirement (unlike `GetServiceByIdAsync`, which does call `.WithEtag(resp.Version)`).
- `ListApisAsync` — binds only `sortBy/sortOrder/cursor/limit` via `CursorListBinding.Bind<ApiSortField>(sortBy, sortOrder, limit, ApiSortSpecs.AllowedFieldNames)`; no `displayNameContains`/`teamId`/`health` filter params (unlike `ListServicesAsync`). Default sort `ApiSortField.DisplayName` / `SortOrder.Asc`. Dispatches `ListApisHandler.Handle(new ListApisQuery(...))`, returns `Results.Ok(page)`.

No new `using` directives were needed — `Kartova.Catalog.Application`, `Kartova.Catalog.Contracts`, and `SortOrder` alias were already imported at the top of the file.

### `CatalogModule.cs`
Added three route mappings in `MapEndpoints`, immediately after the `ListServices` mapping and before the `PUT .../applications/{id:guid}/team` block:

- `POST /apis` → `RegisterApiAsync`, `.RequireAuthorization(KartovaPermissions.CatalogApisRegister)`, `.WithName("RegisterApi")`, `Produces<ApiResponse>(201)`, `ProducesProblem(400/403/422)`.
- `GET /apis/{id:guid}` → `GetApiByIdAsync`, `.RequireAuthorization(KartovaPermissions.CatalogRead)`, `.WithName("GetApiById")`, `Produces<ApiResponse>(200)`, `ProducesProblem(404)`.
- `GET /apis` → `ListApisAsync`, `.RequireAuthorization(KartovaPermissions.CatalogRead)`, `.WithName("ListApis")`, `Produces<CursorPage<ApiResponse>>(200)`, `ProducesProblem(422)`.

`KartovaPermissions.CatalogApisRegister` and all contract/handler/query types (`RegisterApiRequest`, `ApiResponse`, `RegisterApiHandler`, `RegisterApiCommand`, `GetApiByIdHandler`, `GetApiByIdQuery`, `ListApisHandler`, `ListApisQuery`, `ApiSortField`, `ApiSortSpecs`) were confirmed to already exist from Tasks 1–5/2 before wiring (via Grep) — no missing dependencies.

## Full-solution build

```
PowerShell> dotnet build Kartova.slnx -v q
Build succeeded.
    0 Warning(s)
    0 Error(s)
Time Elapsed 00:00:29.77
```

0 warnings / 0 errors, `TreatWarningsAsErrors=true` — gate satisfied.

## Commit

`56bc9f8` — `feat(catalog): wire /api/v1/catalog/apis POST/GET/list endpoints` (3 files changed, 89 insertions), trailer `Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>`.

## Self-review

- **Mirrors Service delegates:** `RegisterApiAsync` structurally identical to `RegisterServiceAsync` (team-exists 422 → target-team 403 → handler dispatch → 201 Created with location header). Confirmed by side-by-side read before writing.
- **No ETag on GET:** confirmed — `GetApiByIdAsync` returns `Results.Ok(resp)` with no `.WithEtag` call, deliberately diverging from `GetServiceByIdAsync`.
- **No filter params on list:** confirmed — `ListApisAsync` signature has only `sortBy/sortOrder/cursor/limit`, no `displayNameContains`/`teamId[]`/`health[]` (which `ListServicesAsync` has). Matches "filters out of scope this slice" (FU-9, Global Constraints).
- **Correct permissions per route:** `POST /apis` gated on `CatalogApisRegister` (write permission, Task 2); both `GET` routes gated on `CatalogRead`. Matches the plan exactly and the existing Service/Application route pattern (register-specific permission for POST, shared read permission for GET/list).
- **Route placement:** inserted after `ListServices` mapping, before the unrelated `PUT .../applications/{id:guid}/team` block — matches plan instruction ("after the ListServices mapping").
- **Line endings:** edits made via the `Edit` tool against an LF-normalized file; no CRLF introduced (repo `.gitattributes` also normalizes on commit).

No issues found; no fixes needed.

## Concerns

- None for this task's scope. Endpoints are compile-gated only — Tasks 7–8 add the integration tests (real seam: 201/200/400/403/404/422 status coverage, round-trip, permission checks) that exercise this wiring end-to-end. Until those tests run green, this remains "staged, verification pending" for the full DoD ledger.
