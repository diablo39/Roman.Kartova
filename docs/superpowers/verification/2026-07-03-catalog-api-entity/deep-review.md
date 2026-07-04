# Deep PR Review — Catalog API entity (E-02.F-03.S-01)

**Branch:** `feat/catalog-api-entity` vs `master` · **Status:** OPEN (pre-merge gate)
**Reviewer pass:** `/deep-review` (gate 9) · **Date:** 2026-07-03
**Read against:** spec `2026-07-03-catalog-api-entity-design.md`, plan `2026-07-03-catalog-api-entity.md`, ADR-0111 (+0090/0093/0095/0103/0109/0097), DoD ledger.

### Overview

The slice lands the sync `Api` catalog entity end-to-end (POST/GET-by-id/cursor-list) as a faithful sibling of `Service`, with domain aggregate, EF mapping + RLS migration, direct-dispatch handlers, permission 5-sync, and real-seam integration tests. It matches ADR-0111's scope discipline exactly — provider/instance FK fields and consumer edges are deliberately absent, and the `xmin`/`Version` collision is resolved and documented. Implementation is faithful to spec and plan; the only gaps are test-coverage refinements (two enumerated spec §7.3 sort/cursor criteria and the `CreatedBy` enrichment) plus one copied OpenAPI-doc inaccuracy.

### Blocking-class issues

None. All three public endpoints are covered at the architecture (permission matrix), unit (domain), and integration (real-seam Postgres/RLS + real JwtBearer) tiers required by ADR-0097; RLS, identity-from-context, required-team, and audit are each proven by a passing real-seam test.

### Should-fix issues

**1. `GET /api/v1/catalog/apis` advertises `422` but its documented error paths return `400`**
- Evidence: `src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogModule.cs:200-204` maps `ListApis` with only `.ProducesProblem(StatusCodes.Status422UnprocessableEntity)`, but the endpoint's real error responses are **400** — `ListApisPaginationTests.List_rejects_unknown_sortBy_with_400` and `List_rejects_out_of_range_limit_with_400_invalid_limit` both assert `HttpStatusCode.BadRequest`. Spec §6 (corrected) confirms bad limit = 400 `invalid-limit` and bad sort = 400. So the published OpenAPI contract for this endpoint documents a status it never emits and omits the one it does.
- Impact: generated clients / API consumers (FU-9 UI, external) get a wrong problem-status contract for list validation errors; `OpenApiTests` is name-keyed/live so it won't catch the mismatch.
- Fix: add `.ProducesProblem(StatusCodes.Status400BadRequest)` to the `MapGet("/apis")` chain (and drop or justify the 422). Note this mirrors the sibling `ListServices`/`ListApplications` mappings (same 422 at CatalogModule.cs:179-187) — the accurate fix is arguably repo-wide, but this slice re-introduces it. A test asserting the 400 problem-type in the published doc would catch it.

### Nits (cap 5)

**1. Inconsistent `sealed` on new/edited test classes**
- Evidence: `ListApisPaginationTests.cs:12` is `public sealed class`, but `RegisterApiTests.cs:11` and the edited `AuditWiringTests.cs:14` are `public class`. Gate-7 sealed one test class; the siblings added in the same slice were left unsealed.
- Impact: cosmetic inconsistency. Fix: seal `RegisterApiTests` (and, if in scope, `AuditWiringTests`).

**2. `Api.Create` explicit-`createdAt` overload is public but production-unused**
- Evidence: `src/Modules/Catalog/Kartova.Catalog.Domain/Api.cs:329-345` — the `DateTimeOffset createdAt` overload is only consumed by tests/seed (`RegisterApiHandler` uses the `TimeProvider` overload). It is `public` on the aggregate surface.
- Impact: minor surface exposure; mirrors the shipped `Service` pattern so it is intentional. Fix: none required (documented "seed/test fixtures").

**3. `POST /apis` mapping omits `401` from `Produces`**
- Evidence: `CatalogModule.cs:188-194` declares 201/400/403/422 but not 401, though `RegisterApiTests.POST_without_token_returns_401` proves anonymous callers get 401.
- Impact: OpenAPI completeness only; consistent with siblings. Fix: optional `.ProducesProblem(401)`.

### Missing tests

**1. `sortBy=createdAt` ordering oracle (spec §7.3 — "each sortBy honored: DisplayName/Style/Version/CreatedAt")**
- `ListApisPaginationTests` proves `displayName` (default), `version`, and `style` ordering, but never exercises `sortBy=createdAt`. The `CreatedAt` sort spec (`ApiSortSpecs.cs:15`, `new("createdAt", x => x.CreatedAt)`) has no ordering assertion.
- Test that should exist: `Kartova.Catalog.IntegrationTests / ListApisPaginationTests / List_honors_sortBy_createdAt` — seed 3 uniquely-prefixed APIs at controlled `CreatedAt` (via distinct clock ticks), GET `?sortBy=createdAt&sortOrder=asc&limit=200`, filter to the prefix, `CollectionAssert.AreEqual` the expected chronological order. Catches a mutant swapping the `CreatedAt` selector.

**2. Backward-cursor / `PrevCursor` traversal (spec §7.3 — "forward/backward cursor")**
- `List_paginates_forward_with_cursor` (`ListApisPaginationTests.cs:27-47`) only walks forward; `PrevCursor` returned by `ListApisHandler` (`new CursorPage<ApiResponse>(items, page.NextCursor, page.PrevCursor)`) is never asserted.
- Test that should exist: same class, `List_paginates_backward_with_prev_cursor` — page forward once, then GET with the second page's `PrevCursor` and assert it returns the first page's items. Catches a mutant nulling `page.PrevCursor`.

**3. `CreatedBy` enrichment on read paths (spec §5.3 — "`CreatedBy` enriched by read handlers via `IUserDirectory`, mirrors ServiceResponse")**
- No test asserts `ApiResponse.CreatedBy` is populated on either read surface. `GetApiByIdHandler.cs:15` (`api.ToResponse() with { CreatedBy = creator }`) and the `ListApisHandler` batched enrichment are unverified; `POST_..._roundtrips` only checks `GET` returns 200.
- Test that should exist: `RegisterApiTests / GET_by_id_populates_CreatedBy_display_name` — register as a user with a known display-name claim (see `AuditWiringTests` "Ada Catalog" pattern), GET-by-id, assert `body.CreatedBy` is non-null with the expected display name; plus a list-path variant asserting the same for a listed item. Catches a mutant dropping the `with { CreatedBy = ... }` enrichment on both handlers (relevant given gate-6 mutation was waived).

### What looks good

**1. RLS proven at the real seam, not just declared** — `Migrations/20260703161759_AddApis.cs:41-47` emits `ENABLE` + `FORCE ROW LEVEL SECURITY` + `tenant_isolation` policy, and cross-tenant isolation is exercised for real by `RegisterApiTests.GET_by_id_from_other_tenant_returns_404` and `ListApisPaginationTests.List_is_tenant_isolated` on Testcontainers Postgres (ADR-0090).

**2. Identity-from-context with a real proof** — `RegisterApiCommand`/`RegisterApiRequest` carry no tenant/creator fields; `RegisterApiHandler.cs:26-28` pulls `user.UserId`/`tenant.Id`, and `RegisterApiTests.POST_sets_CreatedByUserId_to_caller_sub` asserts the response `CreatedByUserId` equals the caller JWT `sub` (ADR-0090, spec §3 #11).

**3. Sort oracles assert real ordering, not just 200** — `ListApisPaginationTests.cs:1085-1103` verifies `sortBy=version asc` yields `1.0,2.0,3.0` and `sortBy=style desc` yields `GraphQL,Grpc,Rest` (correct against the `Rest=0<Grpc=1<GraphQL=2` smallint mapping) — genuinely mutation-resistant for those two specs.

**4. ADR-0111 scope discipline held** — provider FK (`implementedByApplicationId`), instance FK, and consumer edges are all absent (only the standalone node ships); `Style ∈ {Rest,Grpc,GraphQL}`, freeform `Version`, optional strict `SpecUrl`; the `xmin`→`Xmin` rename avoids the domain-`Version` collision (`Api.cs:290-298`, EfApiConfiguration.cs:44-49). No premature FK or dead lifecycle/health columns (spec §3 #9).

**5. Fail-closed audit is asserted end-to-end** — `RegisterApiHandler.cs:31-42` writes `api.registered` in the same transaction, and `AuditWiringTests.RegisterApi_WritesApiRegisteredAuditRow` (diff lines 890-914) verifies action, actor id/display/type, target type, and the `displayName`/`teamId` data payload (ADR-0093, spec §3 #13).
