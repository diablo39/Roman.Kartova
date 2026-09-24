# DoD Ledger — Environment edit + delete (sub-slice A2)

**Slice:** `2026-09-24-environment-edit-delete` · **Branch:** `feat/catalog-environment-a2-edit-delete` · **HEAD:** (pending final commit)
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
| 4 Container build (images CI) | ✅ PASS | 2026-09-24 |
| 5 `/simplify` | ✅ PASS | 2026-09-24 |
| 6 `requesting-code-review` | ⏳ PENDING | — |
| 7 `review-pr` | ⏳ PENDING | — |
| 8 `deep-review` | ⏳ PENDING | — |
| Terminal re-verify (build + suite) | ⏳ PENDING | — |
| 9 Visual / API verification (ADR-0084) | ⏳ PENDING (re-verify — UI changed since first pass) | — |
| 10 CI green on PR | ⏳ PENDING (re-push required) | — |

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
- `Kartova.Catalog.Tests` (domain unit): 389/389 (22 `CatalogEnvironmentTests`, incl. `Edit_leaves_type_unchanged`).
- `Kartova.Catalog.IntegrationTests` (real Postgres/RLS + real JWT): 514/514 total, 35/35 in `EnvironmentEndpointsTests` (was 31; +4: type-immutability, edit race-backstop, blank-name 400, positive Member-edit).
- `Kartova.ArchitectureTests`: 76/76 (incl. `Ts_snapshot_equals_csharp_KartovaPermissions_All` — confirms the `CatalogEnvironmentsEdit` 5-sync).
- Frontend: `npx tsc -b` clean; `npx vitest run` 1170/1170; `npm run build` clean.
**At:** working tree, post-fix.

### 4 — Container build (images CI job)
**Status:** ✅ PASS
**Evidence:** PR #97 CI run 35991680170, job "Container images (build — Dockerfile/restore gate)" — pass. Also locally: `docker compose build api` succeeded twice (once for the original diff, once after the spec-alignment fixes) and the container served the updated OpenAPI contract both times (verified via `npm run codegen` picking up the schema change).
**At:** CI run 35991680170 (pre-fix commit); local rebuild confirms post-fix too — CI will re-run on push.

### 5 — `/simplify` against branch diff
**Status:** ✅ PASS
**Evidence:** 4 parallel review agents (reuse / simplification / efficiency / altitude) against `git diff master...HEAD`. Findings and disposition:
- **Reuse + altitude, fixed:** Register/Edit duplicated the `23505` name-uniqueness pre-check + race-backstop verbatim → extracted shared `CheckEnvironmentNameAvailableAsync` + `IsEnvironmentNameConflictRace` helpers in `CatalogEndpointDelegates`.
- **Simplification + efficiency, same finding twice (deduped), skipped with reason:** `EditEnvironmentAsync`/`EditEnvironmentHandler` double-fetch the row. This mirrors a pre-existing, accepted convention (`EditVmAsync`/`EditVmHandler` has the identical shape) — fixing it only for Environment would create inconsistency with the very pattern this slice mirrors. Not applied; not filed as a fresh TD since it's `EditVm`'s pre-existing shape, not new debt.
- **Simplification, skipped with reason (filed TD-013):** `ProblemPayload` test-helper duplicated across 7 pre-existing files + this one. Out of scope (touches files outside this PR) per `/simplify`'s reject-by-default rule on cross-cutting extract-helper refactors.
- **Altitude, skipped with reason (filed TD-012):** concurrency-token write-side setter still hard-codes `Xmin`/`Version` per entity (TD-002 was read-side only). Touches 5 handlers across 3 entities — out of scope for this Environment-only slice.
**At:** working tree, pre-fix diff (findings apply identically post-fix — no new simplify-relevant code was added by the spec-alignment fixes beyond the intentional dedup already listed above).

### 6 — `requesting-code-review` at slice boundary
**Status:** ⏳ PENDING

### 7 — `review-pr` (pr-review-toolkit)
**Status:** ⏳ PENDING

### 8 — `deep-review`
**Status:** ⏳ PENDING

### Terminal re-verify (build + full suite after gates 5–8)
**Status:** ⏳ PENDING

### 9 — Visual / API verification (observe the running system)
**Status:** ⏳ PENDING (re-verify — the first browser pass, in the original session, exercised the pre-fix UI: Type was editable and Edit was gated on Register. Must re-verify: Type field absent from Edit dialog, Edit gated on the new `CatalogEnvironmentsEdit` permission, Delete still OrgAdmin-only.)

### 10 — CI green on the PR (terminal)
**Status:** ⏳ PENDING — fixes not yet pushed; PR #97's current CI run (35991680170) reflects the pre-fix commit only.
