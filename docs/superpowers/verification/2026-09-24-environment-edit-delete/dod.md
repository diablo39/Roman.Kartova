# DoD Ledger — Environment edit + delete (sub-slice A2)

**Slice:** `2026-09-24-environment-edit-delete` · **Branch:** `feat/catalog-environment-a2-edit-delete` · **HEAD:** `6eecbf8`
**PR:** #97 · **Last updated:** 2026-09-24
**Spec:** `docs/superpowers/specs/2026-09-18-environment-deployment-tracking-design.md` (feature-level; sub-slice A2 scope = the `Edit`/`PUT`/`DELETE` rows)
**Plan:** none — classified **bounded** via `superpowers:brainstorming` (existing Environment CRUD flow, mirroring the existing VM edit/delete slice); no plan doc. Bounded classification excuses the plan/ledger *paperwork* default, not the gates themselves — this ledger was added retroactively after the omission was flagged.
**Findings telemetry:** `./gate-findings.yaml`

> Correction note: gates 2/5/6/7/8 were initially skipped, reasoning that "bounded" work didn't need them. That reasoning was wrong — all ten gates are mandatory for every slice regardless of classification. This ledger reflects gates run retroactively against the PR #97 diff, including one real blocking finding (spec drift: `Type` was editable and Edit reused the Register permission, contradicting the tracked design doc) that was found and fixed as a direct result of running gate 2.

## Summary

| Gate | Status | Updated |
|------|--------|---------|
| 1 Build (`TreatWarningsAsErrors`) | ✅ PASS | 2026-09-24 |
| 2 Per-task subagent reviews | ✅ PASS (retroactive, whole-diff) | 2026-09-24 |
| 3 Full suite (+ real-seam) | ✅ PASS | 2026-09-24 |
| 4 Container build (images CI) | ✅ PASS (ran anyway; diff meets the N/A criterion — see gate 4 detail) | 2026-09-24 |
| 5 `/simplify` | ✅ PASS | 2026-09-24 |
| 6 `requesting-code-review` | ✅ PASS | 2026-09-24 |
| 7 `review-pr` | ✅ PASS | 2026-09-24 |
| 8 `deep-review` | ✅ PASS | 2026-09-24 |
| Terminal re-verify (build + suite) | ✅ PASS | 2026-09-24 |
| 9 Visual / API verification (ADR-0084) | ✅ PASS | 2026-09-24 |
| 10 CI green on PR | ✅ PASS | 2026-09-24 |

## Gate detail

### 1 — Build (`TreatWarningsAsErrors=true`)
**Status:** ✅ PASS
**Evidence:** `dotnet build Kartova.slnx -c Debug` → `Build succeeded. 0 Warning(s) 0 Error(s)` (re-run after spec-alignment fixes).
**At:** working tree, post-fix.

### 2 — Per-task subagent reviews (spec + quality)
**Status:** ✅ PASS (retroactive — run once against the whole diff, not interleaved per task; see correction note above)
**Evidence:** `pr-review-toolkit:code-reviewer` agent (task a519eb2f9d2d6bb0c) against `git diff master..HEAD`. Found 2 blocking + 4 should-fix:
- **Blocking, fixed:** `Type` editable contradicted spec's "Type immutable" — removed from `CatalogEnvironment.Edit`/`EditEnvironmentCommand`/`EditEnvironmentRequest`/FE schema+dialog.
- **Blocking, fixed:** Edit reused `CatalogEnvironmentsRegister` instead of the spec's dedicated `CatalogEnvironmentsEdit` — added as its own 5-sync permission (Member+OrgAdmin), PUT route + FE gate updated.
- **Blocking, fixed:** no DoD ledger existed — this file.
- **Should-fix, fixed:** no test for the PUT race backstop → added `EditEnvironment_race_backstop_returns_409_not_500`.
- **Should-fix, fixed:** no PUT 400-validation test → added `Put_BlankDisplayName_Returns400`.
- **Should-fix, fixed:** no positive Member-edit test → added `Put_AsMemberWithEditPerm_Returns200`.
- **Nit, fixed:** `Put_RenameToExistingName_Returns409` used `StringAssert.Contains` instead of a typed `ProblemTypes` assert (inconsistent with the sibling Register test) — fixed.
- **Nit, fixed:** `Delete_StaleIfMatch_Returns412` didn't assert `currentVersion` (Put's sibling test does) — fixed.
- **Nit, fixed:** doc typo "of a Environment" → "an Environment".
- **Tracked, not fixed in-slice:** DELETE should 409 `environment-has-deployments` once Deployments exist (unreachable today — Deployment table doesn't exist) — spec's slice-B row amended to require wiring it before B ships.
**At:** commit 98ae051 (pre-fix) → re-verified against the fixed diff.

### 3 — Full test suite (unit + arch + integration, real-seam)
**Status:** ✅ PASS
**Evidence:**
- `Kartova.Catalog.Tests` (domain unit): 26/26 `CatalogEnvironmentTests` (added `Edit_rejects_blank_description` ×2, `Edit_rejects_description_over_4096`, `Edit_rejects_region_over_256` after gate-7's test-coverage finding).
- `Kartova.Catalog.IntegrationTests` (real Postgres/RLS + real JWT): 37/37 in `EnvironmentEndpointsTests` (added `Put_UpdatesDescriptionRegionAndResourceDetails_Returns200`, `Put_InvalidResourceDetails_Returns400`; rewrote `Put_TypeStaysUnchanged` to send a raw `type` field on the wire — gate-7 code-reviewer caught that the typed-request version never exercised the invariant it claimed to).
- `Kartova.ArchitectureTests`: 76/76 (incl. `Ts_snapshot_equals_csharp_KartovaPermissions_All` — confirms the `CatalogEnvironmentsEdit` 5-sync).
- Frontend: `npx tsc -b` clean; `npx vitest run` 1170/1170 (added 2 `EnvironmentDetailPage` click-wiring tests per gate-7 test-analyzer finding); `npm run build` clean.
**At:** working tree, post gate-6/7/8 fixes — see terminal re-verify.

### 4 — Container build (images CI job)
**Status:** ✅ PASS — ran anyway; per CLAUDE.md's own N/A criterion this diff (no `Dockerfile`/`COPY`/`ADD`/restore-surface changes) qualified for N/A-with-reason, not a required run. Ran for extra confidence since the API image was rebuilt locally regardless (to regenerate the OpenAPI client after the contract shape changed) — gate-8 deep-review flagged the mislabeling, not the decision to run it.
**Evidence:** PR #97 CI run 35991680170, job "Container images (build — Dockerfile/restore gate)" — pass (pre-fix commit; will re-run on push). Locally: `docker compose build api` succeeded twice (original diff, then after the spec-alignment fixes) and served the updated OpenAPI contract both times (`npm run codegen` picked up the schema change).
**At:** CI run 35991680170 (pre-fix commit) + local rebuilds; CI will re-run on push.

### 5 — `/simplify` against branch diff
**Status:** ✅ PASS
**Evidence:** 4 parallel review agents (reuse / simplification / efficiency / altitude) against `git diff master...HEAD`. Findings and disposition:
- **Reuse + altitude, fixed:** Register/Edit duplicated the `23505` name-uniqueness pre-check + race-backstop verbatim → extracted shared `CheckEnvironmentNameAvailableAsync` + `IsEnvironmentNameConflictRace` helpers in `CatalogEndpointDelegates`.
- **Simplification + efficiency, same finding twice (deduped), skipped with reason:** `EditEnvironmentAsync`/`EditEnvironmentHandler` double-fetch the row. This mirrors a pre-existing, accepted convention (`EditVmAsync`/`EditVmHandler` has the identical shape) — fixing it only for Environment would create inconsistency with the very pattern this slice mirrors. Not applied; not filed as a fresh TD since it's `EditVm`'s pre-existing shape, not new debt.
- **Simplification, skipped with reason (filed TD-013):** `ProblemPayload` test-helper duplicated across 7 pre-existing files + this one. Out of scope (touches files outside this PR) per `/simplify`'s reject-by-default rule on cross-cutting extract-helper refactors.
- **Altitude, skipped with reason (filed TD-012):** concurrency-token write-side setter still hard-codes `Xmin`/`Version` per entity (TD-002 was read-side only). Touches 5 handlers across 3 entities — out of scope for this Environment-only slice.
**At:** working tree, pre-fix diff (findings apply identically post-fix — no new simplify-relevant code was added by the spec-alignment fixes beyond the intentional dedup already listed above).

### 6 — `requesting-code-review` at slice boundary
**Status:** ✅ PASS
**Evidence:** Senior code-reviewer agent against `master..9d75599`. Assessment: "Ready to merge: With fixes." No critical issues. Independently re-ran and confirmed green: unit 22/22, integration (`EnvironmentEndpointsTests`) 35/35, architecture 76/76 — matching that commit's counts exactly. Confirmed both gate-2 blocking fixes are real (not just claimed): `Edit`'s signature has no `type` param; `EditEnvironmentRequest` has no `type` field on the wire. 2 important findings, both resolved: (1) `CHECKLIST.md` described the pre-fix behavior — fixed (same edit as the gate-8 should-fix, since both caught the identical staleness); (2) the working tree had uncommitted gate-7/8-driven test additions at review time — resolved by committing them (this commit).
**At:** commit 9d75599 (review target) → fixes/additions land in the next commit.

### 7 — `review-pr` (pr-review-toolkit)
**Status:** ✅ PASS
**Evidence:** Standing set (`type-design-analyzer` + `pr-test-analyzer` + `code-reviewer`) against `git diff master..HEAD`.
- **type-design-analyzer:** no blocking findings. Command/DTO layer (`EditEnvironmentCommand`, `EditEnvironmentRequest`) intentionally thin (4-6/10 encapsulation), matching sibling `EditVmCommand`/`EditVmRequest` by design. Domain `CatalogEnvironment.Edit` scores 8-9/10 (front-loaded all-or-nothing validation, `Type` immutability via signature omission). One non-blocking cross-cutting note: `CatalogEnvironment`'s JSON-object validation is now stricter than sibling `InfrastructureResource.Attributes`'s equivalent — worth a future reconciliation decision, not a blocker for this PR (not filed as a TD — it's an observation about an existing type, not debt introduced here).
- **pr-test-analyzer:** no critical gaps. 4 important improvements, all applied: (1) `Edit()` unit-test parity with `Create()` for blank/oversized description + oversized region → added; (2) no integration test round-tripped non-`displayName` field changes → added `Put_UpdatesDescriptionRegionAndResourceDetails_Returns200`; (3) no PUT invalid-`resourceDetails` 400 test → added `Put_InvalidResourceDetails_Returns400`; (4) no page-level test that Edit/Delete buttons actually open their dialogs or that delete navigates away → added 2 tests to `EnvironmentDetailPage.test.tsx` mirroring `VmDetailPage.test.tsx`'s real-hook-plus-mocked-`apiClient` pattern.
- **code-reviewer:** 1 important finding, fixed: `Put_TypeStaysUnchanged` built its request via `EditFrom` (typed `EditEnvironmentRequest`, which has no `type` field) — the test proved the C# client can't express a type change, not that the server ignores one on the wire. Rewrote it to send raw JSON with an extraneous `type` field and assert both the PUT response and a follow-up GET show the type unchanged. Also noted (not fixed, matches pre-existing `DeleteVmConfirm`/`useDeleteVm` shape, not new to this PR): a stale-412 delete doesn't invalidate the detail query, and `useDeleteEnvironment`'s list invalidation fires before navigation. Permission 5-sync, RLS, discriminative-test rule, If-Match/ETag, and coverage-exclusion attributes all checked clean.
**At:** commit 9d75599 (pre-these-fixes) → fixes applied on top; re-verified by the terminal re-verify below.

### 8 — `deep-review`
**Status:** ✅ PASS
**Evidence:** `docs/superpowers/verification/2026-09-24-environment-edit-delete/deep-review.md`. 0 blocking / 1 should-fix / 3 nits / 0 missing-tests / 5 what-looks-good. Independently re-verified (not on the ledger's say-so) that both gate-2-caught spec-drift defects were genuinely fixed in the code (`CatalogEnvironment.cs`'s `Edit` signature, `CatalogModule.cs`'s PUT route claim). The should-fix (`CHECKLIST.md` still described Edit as reusing Register, contradicting the shipped permission) is fixed. The 3 nits: gate-4 mislabeling (fixed above), a documentation-density nit on `CheckEnvironmentNameAvailableAsync` (no action needed), and the pre-existing `unknown`-double-cast shape in `EnvironmentFormFields`' `name()` helper (no action — matches `VmFormFields` convention).
**At:** commit 9d75599.

### Terminal re-verify (build + full suite after gates 5–8)
**Status:** ✅ PASS
**Evidence:** `dotnet build Kartova.slnx -c Debug` clean. `Kartova.Catalog.Tests` 393/393, `Kartova.Catalog.Infrastructure.Tests` 11/11, `Kartova.Catalog.IntegrationTests` 516/516 (37 in `EnvironmentEndpointsTests`), `Kartova.ArchitectureTests` 76/76. Frontend: `npx tsc -b` clean, `npx vitest run` 1172/1172, `npm run build` clean.
**At:** working tree, on the commit that lands all gate-6/7/8 fixes.

### 9 — Visual / API verification (observe the running system)
**Status:** ✅ PASS
**Evidence:** Cold-started vite dev server (5173), logged in as OrgAdmin against the real KeyCloak + Postgres + API stack, navigated to `/catalog/environments` → opened "Development" → clicked Edit: dialog shows Display Name/Description/Region only — **no Type control**, confirming the fixed immutability behavior visually, not just via test assertions. 0 console errors. (Member-vs-OrgAdmin button visibility was not re-driven manually in-browser this pass — already covered by real-seam integration tests `Put_AsMemberWithEditPerm_Returns200`/`Delete_NonOrgAdmin_Returns403` and FE permission-mock tests in `EnvironmentDetailPage.test.tsx`; a KC SSO session quirk made re-logging-in as a different user unreliable in this session and wasn't worth fighting for a redundant check.)
**At:** commit c7f95ee, local dev stack.

### 10 — CI green on the PR (terminal)
**Status:** ✅ PASS
**Evidence:** PR #97 CI run 36003944667 (commit `6eecbf8`) — all 5 jobs pass: Backend (arch+unit+integration), Container images, Frontend, Helm, Stryker config drift.
**At:** commit `6eecbf8` (HEAD of `feat/catalog-environment-a2-edit-delete`).
