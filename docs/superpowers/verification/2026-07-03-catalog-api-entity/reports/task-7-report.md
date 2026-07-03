# Task 7 Report — Register integration tests (real seam)

## Summary
Created `src/Modules/Catalog/Kartova.Catalog.IntegrationTests/RegisterApiTests.cs` exactly per the plan's Task 7 spec. All 10 tests pass on the real seam (Testcontainers Postgres + real JWT). No wiring bugs found — Tasks 3–6 hold up under real-seam verification. Committed as specified.

## Test file created
`src/Modules/Catalog/Kartova.Catalog.IntegrationTests/RegisterApiTests.cs` — copied verbatim from the plan's Task 7 code block (10 `[TestMethod]`s covering: 201 happy path + GET round-trip, null `specUrl` allowed, 401 unauthenticated, 400 empty display name, 400 relative spec URL, 422 unknown team, 403 member not in target team, identity-from-context (`CreatedByUserId` == caller JWT `sub`), 404 unknown id, 404 cross-tenant GET).

## Cross-tenant accessor decision
Verified against sibling files in the same test project before writing the file:
- `GetServiceByIdTests.cs` (`OrgBUser = "admin@orgb.kartova.local"`, used via `Fx.CreateAuthenticatedClientAsync(OrgBUser)` for its `GET_returns_404_for_other_tenants_id` test)
- `RegisterApplicationTests.cs` (uses `Fx.CreateAuthenticatedClientAsync("admin@orgb.kartova.local")` directly)
- Also confirmed present as the standard second-tenant seed across ~15 other integration test files in the project (`CreateRelationshipTests.cs`, `DecommissionApplicationTests.cs`, `ListRelationshipsTests.cs`, etc.)

The plan's test code already uses `Fx.CreateAuthenticatedClientAsync("admin@orgb.kartova.local")` verbatim for the cross-tenant GET test — this matches the fixture's actual second-tenant convention exactly. **No adjustment was needed**; the file was created as specified in the plan with zero modifications.

## Command run + result
```
dotnet test src/Modules/Catalog/Kartova.Catalog.IntegrationTests --filter FullyQualifiedName~RegisterApiTests -v q
```

```
Test run for C:\Projects\Private\Roman.Gig2\src\Modules\Catalog\Kartova.Catalog.IntegrationTests\bin\Debug\net10.0\Kartova.Catalog.IntegrationTests.dll (.NETCoreApp,Version=v10.0)
VSTest version 18.0.2 (x64)

Starting test execution, please wait...
A total of 1 test files matched the specified pattern.

Passed!  - Failed:     0, Passed:    10, Skipped:     0, Total:    10, Duration: 28 s - Kartova.Catalog.IntegrationTests.dll (net10.0)
```

Output pristine — no warnings, no retries needed, no transient Testcontainers timeout encountered on the first run.

## Wiring bugs found/fixed
None. All 10 tests passed on the first run against the live Task 3–6 endpoint/handler wiring and the Task 4 `AddApis` migration (RLS policy included) — no product code changes were required.

## Self-review
- **Real behavior asserted, not mocked:** every test goes through `Fx.CreateAuthenticatedClientAsync` (real `JwtBearer` validation against seeded JWKS) and a real Postgres instance via Testcontainers (`CatalogIntegrationTestBase`/`Fx`), matching the `RegisterServiceTests.cs` pattern exactly.
- **Happy path:** `POST_with_valid_payload_returns_201_and_roundtrips` (also round-trips through GET) + `POST_allows_null_spec_url`.
- **Each negative path covered:** 401 (no token), 400×2 (empty display name, relative spec URL — validator + `Uri.TryCreate(UriKind.Absolute)` per the "Uri validation cross-platform" convention), 422 (unknown team — team-existence check separate from validation), 403 (member not in target team — membership gate).
- **Identity-from-context:** `POST_sets_CreatedByUserId_to_caller_sub` confirms `CreatedByUserId` is derived from `ICurrentUser`/JWT `sub`, never from the request payload (payload has no such field per `Body()`), matching ADR-0090/ADR-0103 guardrails.
- **Cross-tenant RLS:** `GET_by_id_from_other_tenant_returns_404` registers as OrgA then reads as OrgB — 404 (not 403) confirms row-level security hides the row entirely rather than leaking existence, consistent with the `Api` table's `tenant_isolation` policy from Task 4.
- **No assertion weakening:** all assertions match the plan verbatim; none were loosened to make tests pass, since none failed.

## Concerns
None. The file matches the plan's spec exactly, the cross-tenant accessor is confirmed consistent with the rest of the test suite, and the endpoint/handler/migration wiring from Tasks 3–6 is proven correct end-to-end by these real-seam tests.
