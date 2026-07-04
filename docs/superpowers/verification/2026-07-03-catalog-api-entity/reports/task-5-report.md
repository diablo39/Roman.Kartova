# Task 5 Report: Handlers + sort specs + DI registration

## Files created
- `src/Modules/Catalog/Kartova.Catalog.Infrastructure/ApiSortSpecs.cs`
- `src/Modules/Catalog/Kartova.Catalog.Infrastructure/RegisterApiHandler.cs`
- `src/Modules/Catalog/Kartova.Catalog.Infrastructure/GetApiByIdHandler.cs`
- `src/Modules/Catalog/Kartova.Catalog.Infrastructure/ListApisHandler.cs`

## Files modified
- `src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogModule.cs` — added `services.AddScoped<RegisterApiHandler>();`, `services.AddScoped<GetApiByIdHandler>();`, `services.AddScoped<ListApisHandler>();` immediately after `services.AddScoped<ListServicesHandler>();` in `RegisterServices`. No route changes (routes are Task 6).

## Pre-flight verification (before writing code)
Before writing any file, read the actual Task 1–4 artifacts to confirm exact names/signatures used by the plan's pasted code:
- `src/Modules/Catalog/Kartova.Catalog.Domain/Api.cs` — confirmed `Api.Create(displayName, description, style, version, specUrl, createdByUserId, teamId, tenantId, clock)` overload exists (TimeProvider overload), matches `RegisterApiHandler` call exactly.
- `src/Modules/Catalog/Kartova.Catalog.Contracts/ApiSortField.cs` — confirmed enum members `DisplayName, Style, Version, CreatedAt` match `ApiSortSpecs.Resolve` switch.
- `src/Modules/Catalog/Kartova.Catalog.Application/CatalogAuditActions.cs` — confirmed `CatalogAuditActions.ApiRegistered = "api.registered"` and `CatalogAuditTargetTypes.Api = "Api"` already exist (added in Task 3).
- `src/Modules/Catalog/Kartova.Catalog.Application/RegisterApiCommand.cs`, `GetApiByIdQuery.cs`, `ListApisQuery.cs` — confirmed record shapes match handler usage.
- `src/Modules/Catalog/Kartova.Catalog.Application/ApiResponseExtensions.cs` — confirmed `ToResponse(this Api api)` extension exists.
- `src/Modules/Catalog/Kartova.Catalog.Infrastructure/EfApiConfiguration.cs` — confirmed `internal const string IdFieldName = "_id"` exists (Task 4).
- `src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogDbContext.cs` — confirmed `DbSet<Api> Apis` property exists.
- Sibling files read for exact style/pattern: `ServiceSortSpecs.cs`, `RegisterServiceHandler.cs`, `GetServiceByIdHandler.cs`, `ListServicesHandler.cs`.

## SortSpec / ToCursorPagedAsync signature adjustments
**None required.** The plan's pasted code compiled verbatim:
- `SortSpec<Api>` accepted the enum-keyed (`Style`) and string-keyed (`Version`) specs in the same generic instantiation without modification — confirms the plan's note that the key is boxed/`object` (same pattern already used by `ServiceSortSpecs` mixing `string` + `DateTimeOffset`).
- `ToCursorPagedAsync` in `ListApisHandler` compiled with the filter-free call (no `expectedFilters` argument) — an overload without that parameter exists, so no `expectedFilters: null` fallback was needed.

## Build result
```
dotnet build src/Modules/Catalog/Kartova.Catalog.Infrastructure -v q
Build succeeded.
    0 Warning(s)
    0 Error(s)
```
(Run via the PowerShell tool — Bash tool in this environment does not have `cmd`/`git` on PATH; PowerShell tool used for both build and git per environment note.)

## Self-review
- Style match: all 4 new files mirror the Service siblings' structure, doc-comment style, namespace, and using-block ordering exactly (cross-checked line by line against `ServiceSortSpecs.cs` / `RegisterServiceHandler.cs` / `GetServiceByIdHandler.cs` / `ListServicesHandler.cs`).
- No filter logic crept into `ListApisHandler` — confirmed no `Where`, no f-map dictionary, no `expectedFilters` argument (unlike `ListServicesHandler` which has teamId/health/displayName filters). Matches the plan's explicit "no filter block — FU-9" instruction.
- No routes added — `git diff` on `CatalogModule.cs` shows only the 3 `AddScoped` lines inserted in `RegisterServices`; `MapEndpoints` untouched.
- `RegisterApiHandler` writes the audit row in-transaction after `SaveChangesAsync`, sourcing `createdByUserId`/tenant from `ICurrentUser`/`ITenantContext`, never from the command payload — matches ADR-0090/ADR-0093 fail-closed audit pattern.
- `git status` confirms only the intended 4 new files + 1 modified file (plus this verification folder) are untracked/changed — no stray edits.

## Concerns
None. This is a compile-gated task with no unit tests of its own (per task scope); integration tests in Tasks 7–8 will exercise `RegisterApiHandler`/`GetApiByIdHandler`/`ListApisHandler` end-to-end through the endpoint delegates added in Task 6.
