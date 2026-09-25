# DoD Ledger — TD-012 Concurrency Write-Side Generic Setter

**Slice:** `2026-09-25-td-012-concurrency-write-side` · **Branch:** `chore/tech-debt-td-012` · **HEAD:** `d182e96`
**PR:** not yet opened · **Last updated:** 2026-09-25
**Spec/Plan:** none — bounded change per `superpowers:brainstorming` (short in-chat design, approved by user); no `docs/superpowers/specs/` or `docs/superpowers/plans/` file for this slice.
**Findings telemetry:** `./gate-findings.yaml`

> Records the Definition of Done from `CLAUDE.md`. Update each row the moment its gate runs.
> Legend: ✅ PASS · ❌ FAIL · ⏳ PENDING · N/A — FAIL and N/A require a one-line reason.

## Summary

| Gate | Status | Updated |
|------|--------|---------|
| 1 Build (`TreatWarningsAsErrors`) | ✅ PASS | 2026-09-25 |
| 2 Per-task subagent reviews | ⏳ PENDING | — |
| 3 Full suite (+ real-seam if wiring) | ✅ PASS | 2026-09-25 |
| 4 Container build (images CI) | ✅ PASS — ran for real (test-project csproj package ref added) | 2026-09-25 |
| 5 `/simplify` | ✅ PASS — 2 findings applied | 2026-09-25 |
| 6 `requesting-code-review` | ✅ PASS | 2026-09-25 |
| 7 `review-pr` | ✅ PASS — 1 finding applied (unit tests), 1 finding applied (tech-debt.md) | 2026-09-25 |
| 8 `deep-review` | ⏳ PENDING | — |
| Terminal re-verify (build + suite) | ⏳ PENDING | — |
| 9 Visual / API verification (ADR-0084) | ⏳ PENDING (likely N/A — pure refactor, no runtime-surface change; will confirm at closeout) | — |
| 10 CI green on PR | ⏳ PENDING | — |

## Gate detail

### 1 — Build (`TreatWarningsAsErrors=true`)
**Status:** ✅ PASS
**Evidence:** `dotnet build Kartova.slnx -c Debug` — 0 warnings, 0 errors, all 33 projects.
**At:** `45a473e`

### 2 — Per-task subagent reviews (spec + quality)
**Status:** ⏳ PENDING
**Evidence:** single small task, not decomposed via subagent-driven-development — will run one code-quality review pass over the full diff before merge.

### 3 — Full test suite (unit + arch + integration; real-seam if wiring)
**Status:** ✅ PASS
**Evidence:** `dotnet test Kartova.slnx -c Debug` at `d2653fe` (pre-simplify) — all 20 assemblies green, 0 failures. Includes real-seam `Kartova.Catalog.IntegrationTests` (516/516) and `Kartova.Api.IntegrationTests` (22/22), exercising the 5 changed handlers' stale-version → 412 path against real Postgres. Re-run at final commit `d182e96` (after `/simplify` + gate-7 fixes, including the new `ConcurrencyTokenCaptureTests`) — all 20 assemblies green, 0 failures, exit code 0. `Kartova.SharedKernel.AspNetCore.Tests` 109/109 (+3 new: happy-path OriginalValue set, missing-token throw, non-uint-token throw).
**At:** `d182e96`

### 4 — Container build (images CI job)
**Status:** ✅ PASS
**Evidence:** gate-7 fix added `Microsoft.EntityFrameworkCore.InMemory` `PackageReference` to `tests/Kartova.SharedKernel.AspNetCore.Tests.csproj` — a `*.csproj` package-ref change, so the gate runs for real per the rule (not N/A). `docker compose build migrator api` (matches the `images` CI job's exact command, confirmed via `.github/workflows/ci.yml:69`) — both `kartova/migrator:dev` and `kartova/api:dev` built clean, exit code 0. The changed csproj is a test project; `src/Kartova.Api/Dockerfile` copies only `src/` (confirmed via grep), so it cannot affect the runtime image's build graph regardless — ran anyway per the letter of the rule, consistent with the E-01.F-07.S-01 precedent.
**At:** `d182e96`

### 5 — `/simplify` against branch diff
**Status:** ✅ PASS
**Evidence:** 4 parallel review agents (reuse, simplification, efficiency, altitude) over `git diff master...chore/tech-debt-td-012`. Simplification and efficiency: no findings. Reuse: `SetExpectedVersion` duplicated `TryCaptureCurrentVersionAsync`'s EF-metadata token lookup — extracted shared private `FindConcurrencyTokenProperty`. Altitude: `SetExpectedVersion` boxed `expected` into `OriginalValue` without the sibling method's documented uint-only guard — added an explicit throw on a missing or non-uint token property. Both applied in `45a473e`. Skipped (not applicable/out of scope): `Single()`'s default exception message being non-diagnostic (superseded by the new explicit throw messages); the class's ASP.NET-Core project placement (pre-existing TD-002 precedent, not introduced by this diff).
**At:** `45a473e`

### 6 — `requesting-code-review` at slice boundary
**Status:** ✅ PASS
**Evidence:** Fresh reviewer (general-purpose subagent, `requesting-code-review` template) over `git diff 8528705..45a473e`. Verdict: "Ready to merge: Yes" — 0 Critical, 0 Important, 2 Minor (both explicitly not worth churn: `FindConcurrencyTokenProperty`'s name being slightly generic; the two `InvalidOperationException` messages could merge into one). Independently verified: grep for the old `db.Entry(x).Property(...).OriginalValue` pattern across `src/` confirms all 5 call sites migrated, none orphaned; confirmed the two lifecycle commands without `ExpectedVersion` (Decommission/DeprecateApplication) were correctly left untouched (out of scope, no such field); confirmed all 7 Catalog entities with a concurrency token declare it non-nullable `uint` (the new type-guard throw has no current false-negative surface); confirmed each of the 5 migrated handlers has a real-seam integration test on the stale-version → 412 path.
**At:** `45a473e`

### 7 — `review-pr` (pr-review-toolkit)
**Status:** ✅ PASS
**Evidence:** Standing set per CLAUDE.md's gate-7 tuning — `type-design-analyzer`, `pr-test-analyzer`, `code-reviewer` — plus `silent-failure-hunter` (diff adds new throw/guard error-handling paths in `SetExpectedVersion`, meeting the conditional-agent trigger). `comment-analyzer` skipped (diff isn't comment/doc-heavy). All 4 ran over `git diff 8528705..45a473e`.
- **type-design-analyzer:** encapsulation 7/10, invariant expression 8/10, usefulness 8/10, enforcement 8/10. No required fixes; two optional nits (doc cross-reference, an arch test forbidding `.Property(...).OriginalValue=` outside this file) explicitly declined by the reviewer itself as not worth it for 5 already-migrated sites.
- **pr-test-analyzer:** confirmed the happy-path claim holds with discriminative evidence (existing 412 tests assert the exact `currentVersion` value, not just status). Flagged the same test gap as code-reviewer/silent-failure-hunter (rated 6/10, "worth doing", not Critical).
- **code-reviewer:** 0 bugs, 0 CLAUDE.md/ADR violations. 2 Important: (1) the two new throw branches in `SetExpectedVersion` had no direct test; (2) TD-012 was still marked `open` in `docs/engineering/tech-debt.md`. Both applied — see below.
- **silent-failure-hunter:** traced the full propagation path (handler try/catch → `TenantScopeCommitEndpointFilter` → `TenantScopeBeginMiddleware` → the 6 registered `IExceptionHandler`s → framework default) and confirmed the new `InvalidOperationException` is never caught/swallowed (exact-type catch clauses only match `DbUpdateConcurrencyException`), leaks no sensitive data (only entity/property/type names), and is actionable in server-side logs. One MEDIUM finding — same test gap as above.
- **Applied:** `ConcurrencyTokenCaptureTests.cs` (3 tests: happy-path `OriginalValue` set, missing-token throw, non-uint-token throw) added to `tests/Kartova.SharedKernel.AspNetCore.Tests/` using a throwaway in-memory EF model (new `Microsoft.EntityFrameworkCore.InMemory` test-only package ref — triggered gate 4 to run for real, see above). `docs/engineering/tech-debt.md` TD-012 marked resolved.
**At:** `d182e96`

### 8 — `deep-review`
**Status:** ⏳ PENDING

### Terminal re-verify (build + full suite)
**Status:** ⏳ PENDING

### 9 — Visual / API verification (ADR-0084)
**Status:** ⏳ PENDING

### 10 — CI green on PR
**Status:** ⏳ PENDING
