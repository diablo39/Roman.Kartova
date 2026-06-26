# Deep PR Review — PR #42 `feat/catalog-relationships` (Slice 1a backend)

**Status:** OPEN (pre-merge gate) · **Date:** 2026-06-24
**Read against:** spec `docs/superpowers/specs/2026-06-24-catalog-relationships-design.md`, plan `docs/superpowers/plans/2026-06-24-catalog-relationships.md`, ADR index `docs/architecture/decisions/README.md`, test taxonomy ADR-0097, mutation report (Stryker `Catalog.Domain` 89.47%), DoD in `CLAUDE.md`.
**Diff:** `af54af8..HEAD` (code commits only).

### Overview
Adds a polymorphic `Relationship` aggregate to the Catalog module with `POST` / `GET`-by-entity / `DELETE` endpoints under tenant RLS, source-side authorization, fail-closed audit, and ADR-0095 cursor pagination. Creatable types are `depends-on` ({App,Service}→{App,Service}) and `part-of` (Service→Application); the other five ADR-0068 types are defined but gated. Real-seam integration tests cover the happy + negative matrix; Domain mutation score is 89.47%.

### Blocking-class issues
**None.** All eight always-blocking DoD gates plus the conditional mutation gate are satisfied with citable evidence (build 0W/0E Debug+Release; per-task reviews; unit+arch 69/69; integration green in isolation; container build green; `/simplify` applied; mutation 89.47% with survivors killed; whole-branch + this review). Spec §3 decisions #1–#15 each trace to implementation; ADR-0090/0095/0096/0091/0098/0067/0068/0107/0082/0104 honored.

### Should-fix issues
1. **Spec error-table lists the wrong status for bad `limit`.**
   - **Evidence:** spec `2026-06-24-catalog-relationships-design.md` §6 ("Bad `limit` on list → 422 `InvalidLimitException`"); actual `PagingExceptionHandler` maps `InvalidLimitException` → **400** (confirmed by `ListRelationshipsTests.GET_with_limit_over_max_returns_400`).
   - **Impact:** doc drift; a future reader trusting the spec would assert the wrong status.
   - **Fix:** update spec §6 to 400. (Fixed in this pass — see below.)
2. **List route advertises `ProducesProblem(422)` it never returns.**
   - **Evidence:** `CatalogModule.cs` `MapGet("/relationships")` declares `.ProducesProblem(422)`, but the list endpoint's only validation failures (`entityKind`/`direction`/`sortBy`/`limit`) all return 400; there is no 422 path (422 belongs to `POST` invalid source/target only).
   - **Impact:** inaccurate generated OpenAPI for the list endpoint.
   - **Fix:** drop `.ProducesProblem(422)` from the list route. **Deferred, not applied** — it was added deliberately to mirror the sibling `MapGet("/services")` which also declares 422; removing it from only the relationships list creates a sibling inconsistency. Correct resolution is to reconcile both list routes (out of this slice's scope). Logged for the list-endpoint OpenAPI cleanup.

### Nits (cap 5)
1. **`Guid.Empty` source-team sentinel** in `DeleteRelationshipAsync` (`CatalogEndpointDelegates.cs`) — works (verified: `ApplicationTeamScopedHandler` forbids non-OrgAdmin for an empty/unmatched team), but `Guid?` would make the deleted-source path explicit. Deferred: changing `AuthorizeTargetTeamAsync`'s signature ripples to the pre-existing Service/Application callers.
2. **Relationship indexes live in the migration, not the EF model snapshot** (`EfRelationshipConfiguration.cs`) — EF 10 can't reference ComplexProperty columns in `HasIndex`; documented with a comment.
3. **List enrichment does O(distinct-refs) sequential `ICatalogEntityLookup.Find` calls** (`ListRelationshipsForEntityHandler.cs`) — bounded by page size, de-duped via `HashSet`; a `FindMany` batch is the future fix. (Note: a `Task.WhenAll` fan-out is **not** a valid fix — concurrent queries on one `CatalogDbContext` are unsafe in EF Core.)
4. **`CatalogEntityLookup.Find` two near-identical switch arms** (Application/Service) — acceptable for two kinds.
5. **Migration column order differs from the spec listing** (EF field-mapping order) — no semantic impact.

### Missing tests
- **Audit fail-closed not exercised** (spec §3 #14): no test forces an `IAuditWriter.AppendAsync` failure to prove the relationship create/delete rolls back. Acceptable to defer — it's shared audit infrastructure tested in the Audit module — but named: `CreateRelationshipTests` / a fault-injected `IAuditWriter` asserting the row is absent after a thrown append. All other spec acceptance criteria have tests (create happy/self-ref/non-creatable/bad-pair/unknown-source-422/unknown-target-422/cross-tenant-422/duplicate-409/403-membership/401/identity-from-context; PartOf 201 + reverse 400; list direction/pagination/isolation; delete 204/404/403/audit; the two mutation-killing domain cases).

### What looks good
1. **Source-side auth is consistent + fail-closed across create and delete**, both reusing the existing `AuthorizeTargetTeamAsync`/`ApplicationTeamScoped` gate on the source team — no bespoke policy (`CatalogEndpointDelegates.cs`). Gate ordering (source lookup → membership → target lookup) prevents target-existence probing by a forbidden caller.
2. **Open string discriminator** for `*_kind` (`EfRelationshipConfiguration.cs` `HasConversion<string>()`, no DB enum/CHECK) — genuinely future-proofs for the Phase-2 custom entity type (ADR-0064) with zero schema change, exactly as spec §3 #2 intends.
3. **Exhaustive table-driven `RelationshipTypeRules` test** (`RelationshipTests.cs`) guards every `(type, sourceKind, targetKind)` triple against silent widening; the default-arm + not-creatable-message cases close the mutation survivors.
4. **`Type` cursor sort on `.ToString()`** (`RelationshipSortSpecs.cs`) with an explanatory comment — a non-obvious EF/cursor correctness detail (raw enum boxes as Int64 the codec can't round-trip) handled deliberately.
5. **Tenant isolation is structural, not a whitelist** — `tenant_id`/`created_by_user_id` from context (no payload field), RLS-scoped lookups make cross-tenant source/target unrepresentable (→422/404), proven by the cross-tenant integration tests.
