# Task 9 report — OpenAPI snapshot regen + gates 1 & 3

**Completed by:** controller (the dispatched implementer punted mid-run — it backgrounded the full backend test suite and returned without final results or a report; controller took over and finished deterministically).

## OpenAPI snapshot regen
- `web/openapi-snapshot.json` regenerated (+294 lines) — the three new `/api/v1/catalog/apis` paths (POST/GET-by-id/GET-list) + the `ApiResponse`/`RegisterApiRequest`/`CursorPage<ApiResponse>` component schemas now appear in the wire contract.
- Route used by the agent: rebuilt the `kartova/api:dev` image from this branch's code and ran `web/scripts/codegen.mjs` (the `prebuild`/`predev` hook) against its live spec — so the snapshot reflects the NEW endpoints, not the stale running container.
- Type-gate: `npm run typecheck` (`tsc -b --noEmit`) → **exit 0**. Validated the regenerated snapshot's generated types compile against the web app. (Used `typecheck`, NOT `build`, to avoid a second `prebuild` codegen pass that could churn the snapshot against a differently-versioned running API — memory: OpenAPI snapshot codegen churn.)

## Gate 1 — full-solution build (warnings-as-errors)
`dotnet build Kartova.slnx -v q` → **Build succeeded. 0 Warning(s), 0 Error(s). EXIT=0.**

## Gate 3 — full test suite
Backend `dotnet test Kartova.slnx` → **EXIT=0**, every assembly `Passed! Failed: 0`:
- Kartova.Catalog.IntegrationTests **227**
- Kartova.Organization.IntegrationTests **142**
- Kartova.ArchitectureTests **69**
- Kartova.Audit.Infrastructure.IntegrationTests **35**
- Kartova.SharedKernel.Identity.IntegrationTests **8**
- Kartova.Api.IntegrationTests **6**
- Kartova.Organization.Tests **80**
- + unit projects (SharedKernel.Tests 125, Catalog.Tests 173, …)

Frontend `npm run test` (vitest) → **690/690 passed, 100 files, exit 0**.

### Flake encountered + resolved
The first frontend run was launched **concurrently** with the backend Testcontainers suite. Under host saturation it reported 1 failure (`SetSuccessorDialog.test.tsx`, an unrelated prior-slice test) timing out at 5000ms, plus 2 vitest **worker-startup timeouts** ("Failed to start forks worker" / "Timeout waiting for worker to respond") — classic resource-contention signatures, not logic failures (676/677 otherwise passing, tsc green, Task 2's permission tests green in isolation). Re-run **alone** after the backend suite finished → **690/690 clean**. Per CLAUDE.md flake protocol: a failure that doesn't reproduce on isolated re-run is contention → fix determinism/isolation, don't re-push blindly; here it was pure concurrency load.

## Controller-recovery notes
- The implementer's `dod.md` had marked Gate 3 ✅ **without citing totals** while its final message said it was still waiting for the suite — an unverified PASS. Corrected to RE-VERIFYING, then re-ran both gates myself and recorded real evidence.
- Orphaned background `dotnet test` (15 processes from the punted agent) killed before the clean re-run to avoid Testcontainers contention.
- Bash tool has no `cmd`/`git`/`cat` on PATH on this host — all shell work done via the PowerShell tool with `dotnet` invoked directly.

## Not done here (Task 9 Step 6 — controller-orchestrated gates)
Gates 4 (container build), 5 (/simplify), 6 (mutation — blocking, Domain touched), 7 (requesting-code-review = SDD final whole-branch review), 8 (pr-review), 9 (/deep-review), + terminal re-verify + ci-local + PR remain.

## Final-review fixes

Applied fixes from the final whole-branch review (test-strengthening + one doc fix, to preempt surviving mutants before the mutation gate). No production code touched.

**FIX 1 — domain boundary-accept tests (`ApiTests.cs`):** added three `[TestMethod]`s mirroring `Create_accepts_display_name_of_exactly_128`, asserting exactly-at-limit values are ACCEPTED (not just over-limit rejected): `Create_accepts_description_of_exactly_4096`, `Create_accepts_version_of_exactly_64`, `Create_accepts_spec_url_of_exactly_2048` (constructs a valid `https://x.example.com/` + padding URL, asserts total length 2048 both before and after `Create`).

**FIX 2 — sort ordering oracle (`ListApisPaginationTests.cs`):** replaced the 200-only `List_honors_sortBy_version_and_style` with a real ordering assertion mirroring `List_default_sort_is_displayName_ascending`'s unique-prefix pattern. Added a `SeedWithStyle` helper (Seed now delegates to it with `ApiStyle.Rest`); seeds 3 rows with a unique `vsort-{guid}-` prefix, distinct versions (1.0/2.0/3.0) and distinct styles (Grpc/Rest/GraphQL — confirmed enum declaration order `Rest=0, Grpc=1, GraphQL=2` from `ApiStyle.cs`). Fetches `sortBy=version&sortOrder=asc` and `sortBy=style&sortOrder=desc`, filters to the seeded prefix, and asserts exact order with `CollectionAssert.AreEqual` (`1.0,2.0,3.0` and `GraphQL,Grpc,Rest` respectively). Test fails if either `ApiSortSpecs.Version`/`Style` were swapped or broken — confirmed by reasoning through the `ApiSortSpecs.Resolve` mapping and enum ordinal values before finalizing.

**FIX 3 — audit-row assertion for `api.registered` — DONE (established pattern found):** `AuditWiringTests.cs` already has a clean, established pattern (`Fx.ReadAuditLogAsync(tenantId)` + `CatalogAuditActions`/`CatalogAuditTargetTypes` + `Fx.GetSubClaimAsync`) demonstrated by `Register_WritesApplicationRegisteredAuditRow`. Added `RegisterApi_WritesApiRegisteredAuditRow` mirroring it 1:1 for the API entity: registers via `POST /api/v1/catalog/apis`, reads the tenant's audit log, asserts the single `api.registered` row's actor id/display/type, `TargetType == "Api"`, and the `displayName`/`teamId` fields in `DataJson` — matching exactly what `RegisterApiHandler` writes. No new fixture infrastructure needed.

**FIX 4 (trivial):** `ListApisPaginationTests` made `sealed`; added `<summary>` XML doc ("Cursor-pagination and sort-order integration tests for the APIs list endpoint (ADR-0095).") matching the `ListApplicationsPaginationTests`/`ListServicesPaginationTests` convention.

**FIX 5 (doc, no code):** `docs/superpowers/specs/2026-07-03-catalog-api-entity-design.md` §6 error table — corrected the bad-`limit` row from `422 InvalidLimitException` to `400 invalid-limit` (matches the actual shared `CursorListBinding` behavior used by Applications/Services, and the sibling code comment already in `ListApisPaginationTests.List_rejects_out_of_range_limit_with_400_invalid_limit`). ADR-0095 reference kept.

### Verification

- `dotnet test src/Modules/Catalog/Kartova.Catalog.Tests -v q` → **176/176 passed** (FIX 1).
- `dotnet test src/Modules/Catalog/Kartova.Catalog.IntegrationTests --filter "FullyQualifiedName~ListApisPaginationTests|FullyQualifiedName~RegisterApiTests|FullyQualifiedName~RegisterApi_WritesApiRegisteredAuditRow" -v q` → **17/17 passed** (7 `ListApisPaginationTests` + 10 `RegisterApiTests`, including the new `RegisterApi_WritesApiRegisteredAuditRow` from `AuditWiringTests`, and the strengthened `List_honors_sortBy_version_and_style`).
