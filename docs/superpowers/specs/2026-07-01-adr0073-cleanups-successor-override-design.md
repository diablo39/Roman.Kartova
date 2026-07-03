# Slice — ADR-0073 cleanups: sunset-date admin override + successor reference (+ FU-1 cursor hardening)

**Date:** 2026-07-01
**Stories:** E-02.F-01.S-04 follow-ups — **§15.1** (sunset-date admin override) + **§15.7** (successor reference), both registered in `2026-05-22-slice-7-rbac-roles-and-reverse-lifecycle-design.md`; plus **FU-1** (cursor tamper → 400) from `2026-06-01-cursorcodec-filter-generalization-design.md` §12.
**Phase:** 1 — Core Catalog & Notifications
**Branch (proposed):** `feat/catalog-adr0073-cleanups`
**Governing ADRs:** ADR-0073 (entity lifecycle — the override + successor clauses this slice closes), **ADR-0110 (new — successor as a dedicated App→App field)**, ADR-0096 (verb policy: POST for actions, PUT for replacement), ADR-0091 (ProblemDetails), ADR-0095/0107 (list surface / field-addition trigger), ADR-0018 (audit), ADR-0090 (tenant scope), ADR-0098 (uuid ids).

---

## 1. Goal

Close the two remaining ADR-0073 gaps on the Application aggregate and fix one latent pagination bug:

1. **§15.1 Sunset-date admin override.** ADR-0073: "Deprecated → Decommissioned … may not occur before `sunset_date` **unless an admin overrides** (logged in audit)." The before-sunset *check* already ships (`Application.Decommission`, throws `before-sunset-date`); the **override path does not exist** — today no one can decommission early. Add an OrgAdmin-gated override, audited.
2. **§15.7 Successor reference.** ADR-0073: Deprecated entities "MUST include a `sunset_date` **and a successor reference** (where applicable)." Sunset shipped in slice 5; successor was deferred. Add an optional **Application → Application** successor (ADR-0110), settable at Deprecate and editable while Deprecated, cleared on Reactivate, surfaced on the detail page.
3. **FU-1.** A tampered cursor whose sort-value is the wrong type makes `ConvertCursorValue` throw a raw `FormatException`/`InvalidCastException`/`OverflowException` → **500 (+ stack leak)** instead of the intended **400 `invalid-cursor`**. Map it.

## 2. Scope & sequencing

One slice, three **independently shippable** sub-slices (writing-plans sequences them so an early stop still leaves a green, mergeable state):

| # | Sub-slice | Est. prod LOC | Mutation gate |
|---|-----------|---------------|---------------|
| A | FU-1 cursor 500→400 | ~10 + 1 test | thin (note) |
| B | §15.1 sunset override | ~180 | **applies** (Domain/App) |
| C | §15.7 successor reference (+ ADR-0110) | ~340 | **applies** (Domain/App) |

Total ≈ **530 LOC production** (excl. tests, generated client, migration) — under the ~800 ceiling. Order A → B → C (cheapest/lowest-risk first; C carries the migration + ADR).

## 3. Pre-requisites (already on master)

- **Lifecycle domain** — `Application` (`src/Modules/Catalog/Kartova.Catalog.Domain/Application.cs`): `Deprecate(sunsetDate, clock)` (requires future sunset), `Decommission(clock)` (requires `now >= SunsetDate`, throws `InvalidLifecycleTransitionException(..., reason:"before-sunset-date")`), `Reactivate()` (clears `SunsetDate`), `UnDecommission(newSunsetDate, clock)`. `Lifecycle` enum {Active=1, Deprecated=2, Decommissioned=3} — numeric values load-bearing, pinned by `LifecycleEnumRules`.
- **Lifecycle handlers** — direct-dispatch (ADR-0093): `DeprecateApplicationHandler`, `DecommissionApplicationHandler` (both take `(cmd, CatalogDbContext, IAuditWriter, ct)`, write `CatalogAuditEntries.LifecycleChanged(app, from)`).
- **Endpoints** — `CatalogModule.MapEndpoints`: `POST /applications/{id}/deprecate` and `/decommission` both on `.RequireAuthorization(CatalogApplicationsLifecycleForward)` (**one static permission per endpoint**); reverse transitions (`/reactivate`, `/un-decommission`) on `CatalogApplicationsLifecycleReverse` (OrgAdmin-only). Every mutation delegate runs `LoadAndAuthorizeApplicationAsync` (team-scoped resource gate: OrgAdmin OR member of the app's team) before the handler.
- **Permission model** — `KartovaPermissions` (const + `All` frozen set), `KartovaRolePermissions.Map` (role → permission set); enforced via `.RequireAuthorization(<permission-string-as-policy-name>)`. Mirror files: `web/src/shared/auth/permissions.ts`, `web/permissions.snapshot.json`, `usePermissions` OrgAdmin mock; matrix asserted by `CatalogPermissionMatrixTests`.
- **Existence lookup** — `ICatalogEntityLookup` / `CatalogEntityLookup` registered in `CatalogModule` (RLS-scoped); direct `db.Applications` queries are also RLS-scoped.
- **Audit** — `IAuditWriter.AppendAsync` (sync, in-transaction, fail-closed), `CatalogAuditEntries` factory (per-action `AuditEntry` with a `data` bag).
- **Cursor pagination** — `QueryablePagingExtensions.ToCursorPagedAsync` + `ConvertCursorValue` (`src/Kartova.SharedKernel.Postgres/Pagination/`); `InvalidCursorException` already mapped to 400 `invalid-cursor` by `PagingExceptionHandler`.
- **FE lifecycle** — `DeprecateConfirmDialog` (RHF + zod, `sunsetDate` date input), `DecommissionConfirmDialog` (no body, confirm-only), `LifecycleMenu`, `lifecycle.ts`, `schemas/deprecateApplication.ts` (`sunsetDateField`), `api/applications.ts` mutations, `EntitySearchCombobox` (kind-parameterized server-search typeahead, from slice 1b), `usePermissions`.
- **Detail page** — `ApplicationDetailPage.tsx` loads the app (`useApplication(id)`), renders header + metadata + `<DependencyMiniGraph>` + `<RelationshipsSection>`.

## 4. Decisions

| # | Decision | Rationale / source |
|---|----------|--------------------|
| 1 | **Successor = dedicated nullable `SuccessorApplicationId` self-FK on `Application`; App→App only.** | ADR-0110. Lifecycle *guidance*, not topology; invariant enforceable with the transition; real FK = integrity; avoids expanding the relationship matrix. App→Service deferred. |
| 2 | **Successor settable at `Deprecate`, editable while `Deprecated`, cleared on `Reactivate`; optional ("where applicable").** | ADR-0073 frames it as deprecation metadata alongside sunset. Editable-while-Deprecated avoids a reactivate cycle to correct it. Clearing on Reactivate mirrors `SunsetDate`. |
| 3 | **Successor existence validated at write (RLS-scoped) → 422 `invalid-successor`; self-reference → 400 `successor-self-reference`.** | FK is the integrity backstop; a clean 422/400 is the UX (don't surface a DbUpdateException as 500). |
| 4 | **Successor lifecycle is NOT validated** (a Deprecated/Decommissioned app may be *named* as a successor). | Soft gap noted in ADR-0110; enforcing it couples two aggregates' lifecycles for marginal value. Explicit follow-up. |
| 5 | **Override transport = body flag on the existing endpoint** — `POST /decommission` with `DecommissionApplicationRequest(bool OverrideSunset = false)` (absent body = false = today's behavior). Not a separate `/force-decommission` endpoint. | Override is a *modifier* of one transition, not a new transition (unlike reactivate/un-decommission). One endpoint keeps the API honest; the FE always calls `/decommission`. |
| 6 | **Override authz = new OrgAdmin-only permission `catalog.applications.lifecycle.override`, checked imperatively when `OverrideSunset==true`.** | Since `.RequireAuthorization` is one static permission per endpoint and the override is conditional, the base `lifecycle.forward` gate stays on the endpoint and the delegate does an imperative `IAuthorizationService.AuthorizeAsync(user, <override-policy>)` only when the flag is set. Named permission (not an inline role check) matches the `lifecycle.reverse` house style and is grantable independently. |
| 7 | **Override is audited** — `CatalogAuditEntries.LifecycleChanged` data gains `overrodeSunset: true` + the bypassed `sunsetDate` when an override occurs. | ADR-0073 "logged in audit"; ADR-0018. Reuses the existing lifecycle audit entry (no new action). |
| 8 | **Successor change is audited** — new `application.successor_changed` action (from/to successor id, `null` on clear). | Migration guidance is accountability-relevant; distinct from a lifecycle state change. |
| 9 | **Successor display name enriched on the detail read only** (`GetApplicationByIdHandler` resolves the successor's `DisplayName` via a scoped lookup); write-path responses leave `SuccessorDisplayName = null`. | Mirrors the `CreatedBy` write-null/read-enrich pattern. List path does not carry it (Decision 10). FE detail always comes from the read path, so the link renders in one payload. |
| 10 | **Successor is NOT a column/sort/filter on the Applications list** (field-addition trigger, ADR-0095/0107). | It is deprecation detail, not a list concern. Recorded as explicit opt-out in `docs/design/list-filter-registry.md`. |
| 11 | **FU-1 fix = wrap `ConvertCursorValue` body**, map `FormatException`/`InvalidCastException`/`OverflowException` → `InvalidCursorException`. | Pre-existing latent bug; smallest correct fix; reuses the existing 400 `invalid-cursor` mapping. |

## 5. Architecture

### 5.1 Sub-slice A — FU-1 (cursor hardening)

`QueryablePagingExtensions.ConvertCursorValue` (line ~206): wrap the parse/convert body:

```csharp
try
{
    // existing DateTimeOffset.Parse / DateTime.Parse / Guid.Parse / Convert.ChangeType body
}
catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException)
{
    throw new InvalidCursorException(
        $"Cursor sort value '{value}' is not compatible with expected type {targetType.Name}.", ex);
}
```

`InvalidCursorException` is already mapped to 400 `invalid-cursor` by `PagingExceptionHandler`. No new type, no wire change.

### 5.2 Sub-slice B — §15.1 sunset override

**Domain** (`Application.cs`):
```csharp
public void Decommission(TimeProvider clock, bool allowBeforeSunset = false)
{
    if (Lifecycle != Lifecycle.Deprecated)
        throw new InvalidLifecycleTransitionException(Lifecycle, nameof(Decommission), SunsetDate);
    if (!allowBeforeSunset && clock.GetUtcNow() < SunsetDate!.Value)
        throw new InvalidLifecycleTransitionException(Lifecycle, nameof(Decommission), SunsetDate, reason: "before-sunset-date");
    Lifecycle = Lifecycle.Decommissioned;
}
```
Default `false` keeps every existing caller/behaviour unchanged.

**Contracts:** new `DecommissionApplicationRequest(bool OverrideSunset = false)` `[ExcludeFromCodeCoverage]`.

**Command/handler:** `DecommissionApplicationCommand(ApplicationId Id, bool OverrideSunset)`. `DecommissionApplicationHandler` passes `cmd.OverrideSunset` to `Decommission`; when an override actually bypassed the check (i.e. `OverrideSunset && now < priorSunset`), append `LifecycleChanged` with `overrodeSunset: true` + the bypassed `sunsetDate`.

**Endpoint / authz** (`DecommissionApplicationAsync` in `CatalogEndpointDelegates`, `CatalogModule`): endpoint keeps `.RequireAuthorization(CatalogApplicationsLifecycleForward)`. Delegate binds `[FromBody] DecommissionApplicationRequest? request` (null-tolerant → `OverrideSunset=false`); **if `OverrideSunset==true`, run `IAuthorizationService.AuthorizeAsync(user, CatalogApplicationsLifecycleOverride)`** → 403 `forbidden` on failure, *before* the handler. Add `.ProducesProblem(403)` (already present) and document the override in the endpoint comment. Confirm the override permission auto-registers as a policy from `KartovaPermissions.All` (same mechanism as existing permissions).

**FE:** `DecommissionConfirmDialog` — add an "Override sunset date" checkbox, rendered only when `now < application.sunsetDate` **and** `usePermissions().hasPermission(CatalogApplicationsLifecycleOverride)`; on confirm, POST `{ overrideSunset }`. Non-override path and copy unchanged. `useDecommissionApplication` mutation payload gains optional `{ overrideSunset }`.

### 5.3 Sub-slice C — §15.7 successor reference

**Domain** (`Application.cs`):
- Field: `public Guid? SuccessorApplicationId { get; private set; }`.
- `Deprecate(DateTimeOffset sunsetDate, Guid? successorApplicationId, TimeProvider clock)` — existing Active-guard + future-sunset check, then `RejectSelfSuccessor(successorApplicationId)` (`ArgumentException` if `== _id`), set both. (Existence is a handler concern — the domain has no DB access.)
- New `void SetSuccessor(Guid? successorApplicationId)` — requires `Lifecycle == Deprecated` (else `InvalidLifecycleTransitionException`), `RejectSelfSuccessor`, set (or clear on `null`).
- `Reactivate()` — additionally `SuccessorApplicationId = null` (alongside the existing `SunsetDate = null`).

**Migration / EF** (`EfApplicationConfiguration`, new migration):
- Column `successor_application_id uuid null` on `catalog.applications`.
- Self-FK: `.HasOne<Application>().WithMany().HasForeignKey(a => a.SuccessorApplicationId).OnDelete(DeleteBehavior.Restrict)` (shadow nav — no navigation property on the aggregate). Index on the FK column.
- RLS already on the table; the self-FK + RLS make a cross-tenant successor unreachable.

**Existence validation** (handlers): before setting, `if (successorApplicationId is {} sid && !await db.Applications.AnyAsync(ApplicationSortSpecs.IdEquals(sid), ct)) → 422 invalid-successor`. (RLS-scoped `Any` ⇒ cross-tenant ids are "not found".)

**Contracts:**
- `DeprecateApplicationRequest(DateTimeOffset SunsetDate, Guid? SuccessorApplicationId = null)`.
- new `SetApplicationSuccessorRequest(Guid? SuccessorApplicationId)`.
- `ApplicationResponse` += `Guid? SuccessorApplicationId` (positional, after `TeamId`) — **wait: keep constructor arity stable** by appending as an **init-only** property (like `CreatedBy`): `Guid? SuccessorApplicationId { get; init; }` + `string? SuccessorDisplayName { get; init; }`. (Avoids breaking positional call sites; matches the `CreatedBy` precedent.)

**Commands / handlers:**
- `DeprecateApplicationCommand(Id, SunsetDate, Guid? SuccessorApplicationId)`; `DeprecateApplicationHandler` validates successor existence → 422, then `app.Deprecate(sunsetDate, successorId, clock)`.
- new `SetApplicationSuccessorCommand(Id, Guid? SuccessorApplicationId)` + `SetApplicationSuccessorHandler` — load (RLS) → 404-null; validate existence → 422; `app.SetSuccessor(...)`; audit `application.successor_changed`; return `ToResponse()`.
- `GetApplicationByIdHandler` — after loading, if `SuccessorApplicationId is {}`, resolve the successor's `DisplayName` (scoped `Select`) and return `resp with { SuccessorDisplayName = name }`. (List handler unchanged — Decision 10.)

**Endpoint:** new `PUT /applications/{id}/successor` → `CatalogEndpointDelegates.SetApplicationSuccessorAsync`, `.RequireAuthorization(CatalogApplicationsLifecycleForward)` (Member+, same as deprecate), `LoadAndAuthorizeApplicationAsync` team gate, `[FromBody] SetApplicationSuccessorRequest`. `.Produces<ApplicationResponse>(200)` + `.ProducesProblem(400/403/404/409/422)`. ADR-0096: PUT = idempotent replacement of the successor slot (null clears). Register `SetApplicationSuccessorHandler` in `CatalogModule.RegisterServices`.

**FE:**
- `schemas/deprecateApplication.ts` — add optional `successorApplicationId: z.string().uuid().optional()`.
- `DeprecateConfirmDialog` — add an optional successor picker: `<EntitySearchCombobox kind="application" excludeId={application.id} onSelect=… />` bound to the form field; submit includes `successorApplicationId`.
- new `SetSuccessorDialog.tsx` — opened from the detail page (or `LifecycleMenu`) when the app is Deprecated; reuses `EntitySearchCombobox`; PUT via new `useSetApplicationSuccessor`; clear affordance (submits `null`).
- `ApplicationDetailPage.tsx` — when `successorApplicationId` present, render a "Successor →" row linking to `/catalog/applications/{successorApplicationId}` with `successorDisplayName` (fallback "—"); a "Set/Change successor" control when Deprecated + `canManage`.
- `api/applications.ts` — `useDeprecateApplication` payload += optional `successorApplicationId`; new `useSetApplicationSuccessor(id)` mutation → PUT `/successor`, invalidates the app detail query.

### 5.4 File map

**Created — backend:**

| File | Purpose |
|---|---|
| `Kartova.Catalog.Contracts/DecommissionApplicationRequest.cs` | `{ bool OverrideSunset = false }` |
| `Kartova.Catalog.Contracts/SetApplicationSuccessorRequest.cs` | `{ Guid? SuccessorApplicationId }` |
| `Kartova.Catalog.Application/SetApplicationSuccessorCommand.cs` | command |
| `Kartova.Catalog.Infrastructure/SetApplicationSuccessorHandler.cs` | load → validate → `SetSuccessor` → audit |
| `Kartova.Catalog.Infrastructure/Migrations/*_AddApplicationSuccessor.cs` (+ Designer) | `successor_application_id` column + self-FK + index |

**Modified — backend:**

| File | Change |
|---|---|
| `Kartova.Catalog.Domain/Application.cs` | `SuccessorApplicationId` field; `Decommission(clock, allowBeforeSunset)`; `Deprecate(..., successorId, ...)`; `SetSuccessor`; `Reactivate` clears successor; `RejectSelfSuccessor` guard |
| `Kartova.Catalog.Contracts/DeprecateApplicationRequest.cs` | += `Guid? SuccessorApplicationId = null` |
| `Kartova.Catalog.Contracts/ApplicationResponse.cs` | += init-only `SuccessorApplicationId`, `SuccessorDisplayName` |
| `Kartova.Catalog.Application/DeprecateApplicationCommand.cs` | += `SuccessorApplicationId` |
| `Kartova.Catalog.Application/DecommissionApplicationCommand.cs` | += `bool OverrideSunset` |
| `Kartova.Catalog.Application/ApplicationResponseExtensions.cs` | map `SuccessorApplicationId` (name enrichment stays in the detail handler) |
| `Kartova.Catalog.Infrastructure/DeprecateApplicationHandler.cs` | successor existence → 422; pass successor to domain |
| `Kartova.Catalog.Infrastructure/DecommissionApplicationHandler.cs` | pass `OverrideSunset`; override audit data |
| `Kartova.Catalog.Infrastructure/GetApplicationByIdHandler.cs` | enrich `SuccessorDisplayName` |
| `Kartova.Catalog.Infrastructure/CatalogEndpointDelegates.cs` | decommission override authz; new `SetApplicationSuccessorAsync`; deprecate successor validation |
| `Kartova.Catalog.Infrastructure/CatalogModule.cs` | map `PUT /successor`; register handler; ProducesProblem updates |
| `Kartova.Catalog.Infrastructure/EfApplicationConfiguration.cs` | map successor scalar + self-FK + index |
| `Kartova.Catalog.Infrastructure/CatalogAuditEntries.cs` | override data on `LifecycleChanged`; new `SuccessorChanged` |
| `Kartova.SharedKernel/Multitenancy/KartovaPermissions.cs` | `CatalogApplicationsLifecycleOverride` const + `All` |
| `Kartova.SharedKernel/Multitenancy/KartovaRolePermissions.cs` | grant override to OrgAdmin only |
| `Kartova.SharedKernel.Postgres/Pagination/QueryablePagingExtensions.cs` | FU-1 try/catch |

**Created — frontend:** `components/SetSuccessorDialog.tsx` (+ `__tests__/SetSuccessorDialog.test.tsx`).

**Modified — frontend:** `schemas/deprecateApplication.ts`, `components/DeprecateConfirmDialog.tsx`, `components/DecommissionConfirmDialog.tsx`, `pages/ApplicationDetailPage.tsx`, `api/applications.ts`, `shared/auth/permissions.ts`, `usePermissions` (OrgAdmin mock), `web/permissions.snapshot.json`, regenerated `generated/openapi.ts` + `web/openapi-snapshot.json`.

**Created — tests:** see §8. **Docs:** ADR-0110, README index entry, `docs/design/list-filter-registry.md` (successor opt-out row), `docs/product/CHECKLIST.md` (note §15.1/§15.7 closed).

## 6. Permission touchpoints — `catalog.applications.lifecycle.override`

Per the known 5-sync (+2 test/verify):
1. `KartovaPermissions.CatalogApplicationsLifecycleOverride` const + add to `All`.
2. `KartovaRolePermissions.Map` — add to **OrgAdmin only** (not Member/Viewer).
3. `web/src/shared/auth/permissions.ts` — TS constant (CI-**unguarded** — must not be missed; drift fails the Frontend job).
4. `web/permissions.snapshot.json` — regenerate/add.
5. `usePermissions` OrgAdmin mock — include it.
6. `CatalogPermissionMatrixTests` — assert OrgAdmin has it, Member/Viewer do not.
7. Confirm policies auto-register from `All` so the imperative `AuthorizeAsync(user, "catalog.applications.lifecycle.override")` resolves (verify the registration path in the plan; add a static endpoint smoke if needed).

## 7. Error handling (ProblemDetails, ADR-0091)

| Condition | Response |
|---|---|
| Override requested by non-holder of `lifecycle.override` | 403 `forbidden` (before handler) |
| Decommission before sunset **without** override | 409 `lifecycle-conflict`, `reason="before-sunset-date"` + `sunsetDate` (unchanged) |
| Successor id not in tenant (deprecate or set-successor) | **422 `invalid-successor`** |
| Successor == self | **400 `successor-self-reference`** (from `ArgumentException` → validation mapping; confirm the mapping surfaces a 400, else add a handler) |
| Set-successor on a non-Deprecated app | 409 `lifecycle-conflict` |
| Tampered cursor, wrong-typed sort value (FU-1) | **400 `invalid-cursor`** (was 500) |

New wire strings: `invalid-successor`, `successor-self-reference`. Confirm/route via the existing Catalog ProblemDetails mapping; add mapping if a new exception type is introduced.

## 8. Testing strategy (gates 2/3/4/6)

Per [docs/TESTING-STRATEGY.md](../../TESTING-STRATEGY.md). Wiring changes (new endpoint, override authz, DB column/FK) ⇒ **real-seam** coverage via `KartovaApiFixtureBase` (real `JwtBearer`/KeyCloak + real Postgres/RLS).

**Domain unit** (`ApplicationLifecycleTests` + new `ApplicationSuccessorTests`):
- override: `Decommission(clock, allowBeforeSunset:true)` before sunset → Decommissioned; `false` before sunset → throws `before-sunset-date`; after sunset unaffected either way.
- successor: `Deprecate` with successor sets it; self-successor → `ArgumentException`; `SetSuccessor` only while Deprecated (else throws); `SetSuccessor(null)` clears; `Reactivate` clears successor + sunset.

**Integration real-seam:**
- `DecommissionApplicationTests`: member decommission before sunset → 409; **OrgAdmin `overrideSunset:true` before sunset → 200** + audit row carries `overrodeSunset`; **member `overrideSunset:true` → 403** (lacks the permission); OrgAdmin after sunset (no flag) → 200 (regression).
- `DeprecateApplicationTests`: deprecate with valid successor → 200 (response carries `successorApplicationId`); non-existent/cross-tenant successor → 422 `invalid-successor`; self-successor → 400.
- new `SetApplicationSuccessorTests`: set while Deprecated → 200; clear (`null`) → 200; on Active/Decommissioned → 409; invalid successor → 422; self → 400; non-member → 403.
- `GetApplicationByIdTests`: detail of a Deprecated app with a successor carries `successorDisplayName`.
- `ListApplicationsPaginationTests`: **replay a cursor with a wrong-typed `s` → 400 `invalid-cursor`** (FU-1; not 500).
- `CatalogPermissionMatrixTests`: OrgAdmin has `lifecycle.override`; Member/Viewer do not.

**Frontend (Vitest)** — ≥1 happy + ≥1 negative each:
- `DecommissionConfirmDialog.test`: override checkbox hidden without the permission / when not before-sunset; shown + posts `overrideSunset:true` for OrgAdmin before sunset.
- `DeprecateConfirmDialog.test`: successor picker selects an app → payload carries `successorApplicationId`; omitted → absent.
- new `SetSuccessorDialog.test`: set + clear paths call PUT with the right body; 422 → toast.
- `ApplicationDetailPage.test`: renders "Successor →" link when present; absent otherwise.
- `api/applications` tests: deprecate/decommission/set-successor payloads + invalidation.

**Gate 4 (container build):** regenerated `openapi.ts` **must be committed**; `npm run build` (`tsc -b`) green so the web image type-checks the new client + permission constant.

**Gate 6 (mutation): applies** — Domain/Application logic changes in B and C. Run `/misc:mutation-sentinel` → `/misc:test-generator` over `Application.cs` (override flag, successor guards) + the changed handlers; document survivors. FU-1's `catch-when` is thin for Stryker (note, don't force).

**Manual (ADR-0084):** Playwright MCP, cold-start dev server → Deprecated app: set a successor (picker), see the "Successor →" link; as OrgAdmin decommission before sunset with the override checkbox; console clean. Flag *pending user verification* if the dev stack is unavailable in-session.

## 9. List surface (ADR-0095 / ADR-0107)

Adding `SuccessorApplicationId` to `Application` fires the **field-addition trigger**. Decision for the **Applications** list (the only list on this entity): **column = no · sort = no · filter = no** — successor is deprecation detail surfaced on the detail page, not a list dimension. Recorded as an explicit opt-out row in `docs/design/list-filter-registry.md`. No `sortBy` allowlist change, no `<FilterBar>` facet. Services have no lifecycle/successor — unaffected.

## 10. Definition of Done

The eight always-blocking gates in **CLAUDE.md → Working agreements → Definition of Done** apply verbatim; not restated. Slice-specific notes:
- **Gate 3:** real-seam additions required (override authz, successor endpoint, FU-1 cursor) — §8.
- **Gate 4:** committed regenerated client + green `tsc -b`.
- **Gate 6:** **applies** (Domain/Application logic — B and C).
- **ADR-0110** committed + README index updated; **list-filter-registry** opt-out row added.
- Run `scripts/ci-local.sh` (Release mirror) green before push.

## 11. Out of scope (explicit deferrals)

- **App→Service successors** (polymorphic `{kind,id}`) — ADR-0110 defers; needs a follow-up migration.
- **Successor-lifecycle validation** (blocking a Decommissioned app as a successor) — Decision 4, soft gap.
- **Successor edges in the dependency graph / relationship tables** — successor is a field, not a relationship (ADR-0110).
- **Lifecycle notifications** (deprecation → notify dependents; the *rich* successor consumer) — slice-7 §15.6, blocked on E-06.
- **Services lifecycle** (Services have none today) — separate story.
- **`Application.UpdatedAt`** (slice-7 §15.8) — unrelated orphan, not bundled here.

## 12. Implementation order (rough — finalised by writing-plans)

1. **A — FU-1:** wrap `ConvertCursorValue`; add the tamper test to `ListApplicationsPaginationTests`; green.
2. **B — override:** permission const + role map + 5-sync; domain `Decommission(clock, allowBeforeSunset)`; request DTO + command + handler + delegate authz + audit; integration + domain tests; FE checkbox + mutation payload + tests; codegen.
3. **C — successor:** ADR-0110 (committed after your review); domain field + `Deprecate`/`SetSuccessor`/`Reactivate` + self-guard; migration + EF self-FK; contracts + response fields; commands/handlers + existence validation + detail enrichment; new endpoint + registration; audit `successor_changed`; FE picker + SetSuccessorDialog + detail link + mutations + tests; codegen; list-filter-registry opt-out.
4. Terminal re-verify (build + full suite green); `scripts/ci-local.sh`; Playwright manual; update `docs/product/CHECKLIST.md`; push → PR → DoD gates.

## 13. Self-review

**Spec coverage:** every §4 decision traces to §5–§9; every §8 test artifact is a named file in §5.4 (or an existing test file) that writing-plans turns into a task.

**Placeholder scan:** no TBD/TODO. §5 code is illustrative; final code in executing-plans. The `ApplicationResponse` successor fields are **init-only** (not positional) — resolved inline to keep constructor arity stable (matches `CreatedBy`).

**Internal consistency:** override = one endpoint + body flag + imperative override-permission check, consistent across §4 #5/#6, §5.2, §6, §8. Successor = dedicated App→App field (ADR-0110) consistent across §1, §4 #1, §5.3, §9, §11. FU-1 backend-only/no-wire-change consistent across §1, §4 #11, §5.1, §8.

**Scope check:** three sub-slices, ~530 LOC prod, under the 800 ceiling; sequenced for independent shippability.

**Ambiguity check:** successor optional-vs-required resolved (optional, "where applicable"); edit-while-Deprecated resolved (dedicated PUT `/successor`, Deprecated-only); override authz resolved (named permission, imperative check when flag set); successor-name display resolved (detail read enrich, `CreatedBy` pattern); self-ref + non-existent resolved (400 / 422).

**No blocking issues found.**
