# Deep Review — PR #97 (`feat/catalog-environment-a2-edit-delete` → `master`, HEAD `9d75599`)

**Reviewer:** deep-review (in-session), 2026-09-24
**Status:** OPEN — pre-merge gate. This is DoD gate 8 for the A2 slice.

## Overview

PR #97 adds sub-slice A2 of E-02.F-05 (Environment & Deployment Tracking): a metadata-only `PUT /api/v1/catalog/environments/{id}` and a hard `DELETE /api/v1/catalog/environments/{id}`, each gated by a new dedicated permission (`CatalogEnvironmentsEdit` — Member+OrgAdmin; `CatalogEnvironmentsDelete` — OrgAdmin-only) and an `If-Match`/`Xmin` optimistic-concurrency contract mirroring the existing VM edit/delete slice. The branch's second commit (`9d75599`) is itself a fix-up: it removes `Type` from the edit path and swaps a reused `CatalogEnvironmentsRegister` claim for the spec's dedicated `CatalogEnvironmentsEdit` permission, in response to a retroactive gate-2 review documented in the slice's DoD ledger.

## Blocking-class issues

None. The two spec-drift defects flagged by the retroactive gate-2 review (`Type` editable; Edit reusing the Register permission) are verifiably fixed in this diff:
- `src/Modules/Catalog/Kartova.Catalog.Domain/CatalogEnvironment.cs:378` — `Edit(string displayName, string description, string? region, string resourceDetailsJson)` takes no `type` parameter.
- `src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogModule.cs:566` — `tenant.MapPut(...).RequireAuthorization(KartovaPermissions.CatalogEnvironmentsEdit)`, a dedicated permission, not `CatalogEnvironmentsRegister`.
- All 5 permission-sync touchpoints are present and consistent: `src/Kartova.SharedKernel/Multitenancy/KartovaPermissions.cs:254-255,263-264`; `KartovaRolePermissions.cs:276,284-285`; `web/src/shared/auth/permissions.snapshot.json:14-15`; `web/src/shared/auth/permissions.ts:16-17`; `web/src/shared/auth/__tests__/usePermissions.test.tsx:80-81`.
- Test counts cross-checked by direct count, not taken on faith: `CatalogEnvironmentTests.cs` has exactly 22 executed test cases (16 `[TestMethod]`, 4 of which carry `[DataRow]` pairs) matching the DoD ledger's "22"; `EnvironmentEndpointsTests.cs` has exactly 35 `[TestMethod]` (no `DataRow`), matching the ledger's "35 (was 31, +4)".

## Should-fix issues

**Title:** `CHECKLIST.md` documents the pre-fix permission model this same PR shipped a fix for.
**Evidence:** `docs/product/CHECKLIST.md:54` (as changed by this diff) reads: *"Edit reuses the `CatalogEnvironmentsRegister` claim; Delete gated by new OrgAdmin-only `catalog.environments.delete` permission..."* This directly contradicts the shipped code: `CatalogModule.cs:566` gates `PUT` on `KartovaPermissions.CatalogEnvironmentsEdit` (a dedicated, newly-added permission, per `KartovaPermissions.cs:254` and the 5-sync touchpoints above), and the slice's own DoD ledger (`dod.md:38`) records this exact "Edit reused Register" defect as found-and-fixed.
**Impact:** A future reader (dev, auditor, or an agent doing a permission-matrix grep against `CHECKLIST.md` instead of the code) is told Edit and Register share a claim when they no longer do — the opposite of what actually shipped. Since this line was authored in the same commit as the fix, it's not stale drift accumulated later; it's a docs/code mismatch introduced by this PR.
**Fix:** Update `docs/product/CHECKLIST.md:54` to state Edit is gated by its own `catalog.environments.edit` permission (Member+OrgAdmin), matching the DoD ledger's and the code's account.

## Nits

**Title:** Gate 4 (container build) run as a required gate when the diff meets the documented N/A criterion.
**Evidence:** `docs/superpowers/verification/2026-09-24-environment-edit-delete/dod.md:58-61` marks gate 4 "✅ PASS" as if it applies unconditionally. The diff (`git diff master..HEAD --stat`) touches no `Dockerfile`, no `COPY`/`ADD` input, and no restore surface (no `.csproj`, `Directory.Packages.props`, or `nuget.config` changes) — per CLAUDE.md's own gate-4 criterion this slice qualifies for "N/A-with-reason," not a required run.
**Impact:** None functionally (running it anyway is harmless and does provide real evidence), but the ledger's characterization doesn't match the documented policy, which is meant to save the cost of this gate specifically for slices like this one.
**Fix:** Either re-label gate 4 "N/A (no Dockerfile/restore-surface changes) — ran anyway for extra confidence" or leave the evidence as-is but note the N/A qualification explicitly.

**Title:** Doc comment on `CheckEnvironmentNameAvailableAsync` slightly overstates what it excludes.
**Evidence:** `src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogEndpointDelegates.cs:436-442` — comment is accurate but dense; no actual defect, just noting it for a reader unfamiliar with why `EF.Property<Guid>` is used instead of `CatalogEnvironment.Id` (the reasoning is sound and given, this is a nit about readability only).
**Impact:** None — informational.
**Fix:** None required; leaving as-is is fine.

**Title:** `EnvironmentFormFields`'s `name()` helper double-casts through `unknown`.
**Evidence:** `web/src/features/catalog/components/EnvironmentFormFields.tsx:1713-1714` — `path as unknown as TName & FieldPath<TFieldValues>`.
**Impact:** None at runtime; slightly reduces the type-checker's ability to catch a future field-name typo in this one helper. Same shape as the pre-existing `VmFormFields` pattern it mirrors.
**Fix:** No action needed — consistent with established convention.

## Missing tests

None identified against the spec's testing-strategy row for sub-slice A (`docs/superpowers/specs/2026-09-18-environment-deployment-tracking-design.md` §Testing strategy, row "A"). All listed criteria that apply to A2 are covered:
- Edit/delete happy path — `EnvironmentEndpointsTests.cs: Put_UpdatesEnvironment_Returns200`, `Delete_RemovesEnvironment_Returns204`.
- Unique-name 409 (pre-check + race backstop) — `Put_RenameToExistingName_Returns409`, `EditEnvironment_race_backstop_returns_409_not_500`.
- Cross-tenant → not-found (single-cause, status-only is correct per CLAUDE.md's discriminative-test carve-out) — `Put_CrossTenant_Returns404`, `Delete_CrossTenant_Returns404`.
- `delete-with-deployments` 409 — correctly deferred to slice B per the amended spec row (`2026-09-18-environment-deployment-tracking-design.md:67`), not a gap in A2.
- Domain unit coverage for `Edit`'s invariants — `CatalogEnvironmentTests.cs` (8 new cases: replace, type-immutability, region normalization, blank-name x2, over-length, invalid-JSON x2).
- Permission gating, both directions — `Put_WithoutEditPerm_Returns403`, `Put_AsMemberWithEditPerm_Returns200`, `Delete_NonOrgAdmin_Returns403`.
- Frontend: dialog pre-fill, submit, 409/412/400 error mapping, permission-gated button visibility — `EditEnvironmentDialog.test.tsx`, `DeleteEnvironmentConfirm.test.tsx`, `EnvironmentDetailPage.test.tsx`.

## What looks good

- The blocking spec-drift found by the retroactive gate-2 review was genuinely fixed, not just claimed — verified independently in `CatalogEnvironment.cs:378` (no `type` param) and `CatalogModule.cs:566` (dedicated permission), not merely by reading the ledger's say-so.
- `EditEnvironment_race_backstop_returns_409_not_500` (`EnvironmentEndpointsTests.cs:824`) reproduces the concurrent-rename race deterministically via `RaceOnSaveInterceptor` rather than `Task.WhenAll` threading — exactly the pattern CLAUDE.md's gate-5 guidance calls out as the safe alternative to a flaky parallel test.
- The `/simplify` dedup was actually applied in-slice: `CheckEnvironmentNameAvailableAsync` / `IsEnvironmentNameConflictRace` (`CatalogEndpointDelegates.cs:443-459`) replaced two verbatim copies of the 23505-race handling in Register and Edit, with the two *rejected* simplify findings (double-fetch, `Xmin` hard-coding) correctly filed as TD-012/TD-013 with concrete, honest "touches N pre-existing files, out of scope" reasoning rather than silently skipped.
- Tenant-global design is consistently threaded through: no team-scoping was added to either endpoint (`CatalogModule.cs` edit/delete routes have no team gate), matching the deliberate ADR-0103 deviation recorded in ADR-0117 and the design doc's decision #2 — nothing in this diff quietly reintroduces team ownership.
- The FE permission gating is symmetric and defensive: `EnvironmentDetailPage.tsx:2208-2209` gates both the Edit and Delete buttons and the dialogs themselves (`canEdit && <EditEnvironmentDialog .../>`) on their respective permissions, with `!permissionsLoading` guarding against a flash of an incorrectly-gated button while the permission set is still loading.

## Process note (not a code finding)

Per the slice's DoD ledger (`dod.md`), gates 6, 7, 9, and 10 and the terminal re-verify are still `⏳ PENDING` as of this review. That's expected — this review is gate 8 — but merge should wait for those to close, per CLAUDE.md's ten-gates-always-blocking rule.
