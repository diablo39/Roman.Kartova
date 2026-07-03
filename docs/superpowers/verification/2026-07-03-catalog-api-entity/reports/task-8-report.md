# Task 8 report — Api list/pagination tests + permission-matrix rows

## Files created / changed

- **Created** `src/Modules/Catalog/Kartova.Catalog.IntegrationTests/ListApisPaginationTests.cs`
- **Changed** `src/Modules/Catalog/Kartova.Catalog.IntegrationTests/CatalogPermissionMatrixTests.cs`
  - Added 3 `Endpoints` rows: `POST /api/v1/catalog/apis` → `CatalogApisRegister`, `GET /api/v1/catalog/apis` → `CatalogRead`, `GET /api/v1/catalog/apis/{apiId}` → `CatalogRead`.
  - Added an API seed block (as OrgAdmin) so `{apiId}` substitution resolves for per-role calls.
  - Added `.Replace("{apiId}", apiId.ToString())` to the URL substitution chain.
  - Added an `AttachShapeValidBody` branch for `POST /api/v1/catalog/apis`.
  - Existing app/service/relationship rows and `Team_scope_matrix_for_metadata_edit` untouched.

## Sibling-envelope adjustments

1. **`sortBy=bogus` → 400.** Matches the plan draft as-is; confirmed against `ListServicesPaginationTests.GET_with_invalid_sortBy_returns_400`.
2. **`limit=99999` out-of-range.** The plan's draft asserted `422 UnprocessableEntity`. Checked siblings: `ListServicesPaginationTests` has no limit-range test at all; `ListApplicationsPaginationTests.LimitOutOfRange_returns_400` asserts **400 BadRequest** with an RFC 7807 body containing `"invalid-limit"` (the shared `CursorListBinding` never returns 422 for this case). Renamed the test to `List_rejects_out_of_range_limit_with_400_invalid_limit` and adjusted the assertion to 400 + body contains `"invalid-limit"`.
3. **Test-isolation fragility (discovered, fixed, not in the plan draft).** `CatalogIntegrationTestBase.Fx` is assembly-scoped — one shared Postgres DB/tenant for the whole `Kartova.Catalog.IntegrationTests` assembly, no per-class reset. The plan's literal draft test asserted `page.Items[0].DisplayName == "alpha-api"` off an *unscoped* `GET /apis?limit=2` call. This broke under a filtered run because `CatalogPermissionMatrixTests` seeds an API named `"Matrix Api"` into the same org-A tenant, and `'M'` (77) sorts before lowercase `'a'` (97) under default Postgres/ordinal collation — so "Matrix Api" pushed "alpha-api" out of the first page. Diagnosed this as a test-setup problem, not a product sort bug (the sibling `ListServicesPaginationTests` avoids the exact same latent hazard from its own "Matrix Svc" seed by never asserting exact identity off an unscoped call). Fixed by splitting the one test into two, mirroring `ListServicesPaginationTests` exactly:
   - `List_paginates_forward_with_cursor` — envelope/paging shape only (counts, `NextCursor` non-null), tolerant of any other tenant data, using `Guid.NewGuid():N`-prefixed names.
   - `List_default_sort_is_displayName_ascending` — exact-order assertion via `Guid.NewGuid():N`-prefixed names (`dsort-{unique}-{zzz,aaa,mmm}`), fetched at `limit=200` and filtered down to the known names before `CollectionAssert.AreEqual`.
   Also switched `List_is_tenant_isolated`'s seeded name to a `Guid.NewGuid():N`-suffixed unique value for the same defensive reason (no `/apis` route supports a `teamId`/attribute filter this slice per `ListApisQuery`'s doc comment — FU-9 — so unique-name + `.Where`/`.Any` filtering is the only available isolation mechanism, matching the established sibling pattern).

## Full-suite result

```
dotnet test src/Modules/Catalog/Kartova.Catalog.IntegrationTests -v q
Passed!  - Failed: 0, Passed: 227, Skipped: 0, Total: 227, Duration: 41 s
```

All 227 tests green, including:
- Filtered pre-check (`ListApisPaginationTests` + `CatalogPermissionMatrixTests`): 8/8 passing (5 new list tests + `Team_scope_matrix_for_metadata_edit` + the matrix's own per-role-cell test + a related matrix test), confirming the new API seed/rows/body branch did not disturb existing app/service/relationship matrix cells.
- Full-suite run confirms no regression across the whole Catalog integration assembly (applications, services, relationships, teams, apis, permission matrix).

No Testcontainers/Docker flake encountered — both runs were clean on first attempt.

## Self-review

- List tests assert real behavior, not just 200: exact ascending-order sequence (`CollectionAssert.AreEqual` on filtered unique names), cursor-pagination counts/`NextCursor` presence, 400 for unknown `sortBy`/out-of-range `limit` with body content check, and tenant isolation via `.Any()` absence check on a uniquely-named row.
- Matrix additions verified not to disturb existing cells: ran the matrix test class filtered together with the new list tests before the full run, and the full-suite run afterward — both green.
- All new tests use unique (`Guid.NewGuid():N`-prefixed/suffixed) seed data or defensive filtering, consistent with the sibling `ListServicesPaginationTests`/`ListApplicationsPaginationTests` pattern, given the assembly-scoped shared-DB fixture has no per-class reset.

## Concerns

- The assembly-scoped fixture (no per-class DB reset) is a standing hazard for any future test in this assembly that asserts exact identity/order off an unscoped list call — worth calling out for future slices (already true today for `ListServicesPaginationTests`/`ListApplicationsPaginationTests`, which independently arrived at the same defensive pattern this task now follows for Apis). No action taken beyond this file since it's a pre-existing, consistently-worked-around pattern, not a regression introduced here.
- `/apis` has no `teamId`/attribute filter this slice (by design, FU-9/deferred) — unlike `/services`, which supports `teamId=`. This is expected per `ListApisQuery`'s doc comment, not a gap in this task's scope.
