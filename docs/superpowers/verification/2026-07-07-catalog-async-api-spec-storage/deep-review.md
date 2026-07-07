# Deep Review — E-02.F-03.S-02 Catalog AsyncAPI Spec Storage

**Reviewer:** deep-review (gate 9) · **Range:** `8f0696e..4caccdf` · **Date:** 2026-07-07
**Read against:** spec `2026-07-07-catalog-async-api-spec-storage-design.md`, plan `2026-07-07-catalog-async-api-spec-storage.md`, ADR-0111 (amended)/0112/0090/0091/0093/0095/0103/0108, ADR-0097 taxonomy, CLAUDE.md DoD.

### Overview
The slice lands the unified-API model cleanly: `ApiStyle.AsyncApi` appended (values stable), `catalog_api_specs` RLS-scoped 1:1 table, `PUT/GET /apis/{id}/spec` raw-body sub-resource reusing `catalog.apis.register`/`catalog.read`, and a computed `HasSpec` flag that never carries the blob. Every §3 decision maps to code; the two ADRs are authored and cross-linked correctly. Code is correct against its passing real-seam suite; the gaps are process/evidence (unpopulated DoD ledger) and one named test deliverable (audit-row assertion) that was specified but not written.

### Blocking-class issues
None. (FU-F concurrent double-PUT → 500 is consciously deferred per gate 7; not re-flagged.)

### Should-fix issues

1. **DoD ledger is entirely `PENDING` at HEAD — no gate carries citable evidence.** `docs/superpowers/verification/2026-07-07-catalog-async-api-spec-storage/dod.md:15-28` shows all 12 rows `⏳ PENDING`, and the header still reads `HEAD: 360578f` (actual HEAD is `4caccdf`, three commits later incl. the mutation gate). CLAUDE.md mandates updating each row *the moment its gate runs* and that completion/merge claims cite the ledger; the `dod-check.js` stop hook blocks claims lacking evidence. Impact: the "gates 1-7 + mutation + terminal-reverify green" status is not reconstructable from the artifact of record. Fix: backfill gates 1-7 + mutation rows with command/output/commit and bump the HEAD ref before the merge claim; mark gates 8/9 as they complete.

2. **`api.spec.updated` audit write has no test at any tier — a spec-named gate-5 deliverable.** Spec §6 Testing strategy explicitly lists "`api.spec.updated` row written in the same transaction" as an integration artifact; `UpsertApiSpecHandler.cs:37-45` writes it fail-closed, but a grep of `Kartova.Catalog.IntegrationTests` for `ApiSpecUpdated|api.spec.updated|AppendAsync` returns nothing, and the unit `ApiSpecTests` don't touch the handler. Impact: the audit contract (action string, `mediaType`/`created` metadata, in-transaction fail-closed behavior) is unverified — a silent regression here is invisible. Fix: see Missing tests. (Also strengthens gate-6: the handler's audit branch is a mutation target.)

### Nits (cap 5)

1. **`HasSpec` field-addition trigger not recorded in the list registry.** `ApiResponse.HasSpec` (`ApiResponse.cs:14`) is a new user-facing field on `Api`, which already has a list screen (`/catalog/apis`). CLAUDE.md's field-addition trigger requires recording a column/sort/filter decision per list in `docs/design/list-filter-registry.md`; the diff adds none. UI is deferred, but the registry note (even "column: defer / sort: no / filter: defer") is the explicit decision the trigger mandates.
2. **`ReadCappedAsync` per-block UTF-8 count can misjudge the exact cap by a few bytes.** `CatalogEndpointDelegates.cs` (~L1000): `Encoding.UTF8.GetByteCount(buffer, 0, read)` is summed per 8192-char block; a surrogate pair split across a block boundary counts a lone surrogate as U+FFFD (3 bytes). On a 5 MiB cap this is a harmless off-by-a-handful at the boundary only — worth a comment, not a rewrite.
3. **`LoadAndAuthorizeApiAsync` authorizes an `Api` via `KartovaTeamPolicies.ApplicationTeamScoped`** (`CatalogEndpointDelegates.cs` ~L980). Works (`Api : ITeamScopedResource`, confirmed), but the policy name now reads as an Application-only misnomer across Application/Service/Api resources; consider a `TeamScoped` rename in a later cleanup.
4. **Two different problem `type` URIs for the same 400.** Oversized body → typed `ProblemTypes.SpecTooLarge`; empty/whitespace body → generic domain-validation 400 via `ArgumentException` (ADR-0091 handler). Both correct, but the empty-body case gets no spec-specific `type` — minor asymmetry for clients keying on `type`.
5. **`GET /apis/{id}/spec` is tenant-wide read (no team gate), unlike the team-gated PUT.** Intentional and consistent with `GET /apis/{id}` (catalog reads are tenant-scoped), but undocumented at the route; a one-line comment on the GET mapping would pre-empt the "why no LoadAndAuthorize on GET?" question.

### Missing tests

1. **`Kartova.Catalog.IntegrationTests.ApiSpecTests` — audit trail on PUT.** Scenario: authed member PUTs a valid spec, then query the tenant's audit log; assert exactly one `api.spec.updated` entry with `target_type = Api`, `target_id = apiId`, metadata `mediaType = application/json` and `created = true`; PUT again and assert a second entry with `created = false`. Follows the `api.registered` audit-assertion pattern. (Named as a gate-5 deliverable in spec §6.)
2. **`Kartova.Catalog.IntegrationTests.ApiSpecTests` — audit is fail-closed / in-transaction.** Scenario: with an audit-writer stub forced to throw, PUT a valid spec; assert the response is 5xx AND no `catalog_api_specs` row persisted (transaction rolled back). Proves the fail-closed claim in `UpsertApiSpecHandler`'s doc-comment. (Optional if fixture can't inject a throwing writer — then note N/A with reason.)

### What looks good

1. **Charset-tolerant Content-Type gating.** `CatalogEndpointDelegates.cs` (~L894) parses via `MediaTypeHeaderValue.TryParse` and gates on the bare media type, with a dedicated regression test at `Kartova.Catalog.IntegrationTests/ApiSpecTests.cs:PUT_with_charset_suffixed_content_type_returns_201...` asserting the `; charset=utf-8` happy path and bare echo — the exact class of bug that shipped as a Critical is now pinned.
2. **Size cap enforced independently of declared length.** `ReadCappedAsync` (`CatalogEndpointDelegates.cs` ~L1000) re-caps the streamed body so a chunked-transfer request with no Content-Length cannot bypass the 5 MiB limit — a genuine defense the declared-length pre-check alone would miss.
3. **`HasSpec` computed without carrying the blob, EF-translatable.** `GetApiByIdHandler.cs:14` (`AnyAsync(s => s.ApiId == api.Id)`) and `ListApisHandler.cs:61-67` (single `IN` query over `ApiId` value objects, no `.Value` in the predicate) honor spec decision 13 and avoid the value-converter translation trap that produced the earlier GET-list Critical.
4. **Migration is a faithful RLS mirror.** `Migrations/20260707121905_AddApiSpec.cs:38-49` applies `ENABLE`+`FORCE ROW LEVEL SECURITY`+`tenant_isolation`, `ON DELETE CASCADE` FK to `catalog_apis`, and `ux_catalog_api_specs_api_id` unique — the 1:1 invariant and tenant isolation are enforced at the DB, matching ADR-0112/ADR-0090/ADR-0012.
5. **Multi-byte cap correctness is unit-proven.** `Kartova.Catalog.Tests/ApiSpecTests.cs:Create_rejects_content_over_cap_by_utf8_byte_count` uses `'é'` (2 bytes) to assert the cap is byte-based, not char-based — plus an exact-at-cap boundary test — closing the mutation survivors called out in gate 6.
