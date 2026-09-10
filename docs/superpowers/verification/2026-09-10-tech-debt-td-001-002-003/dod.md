# DoD Ledger — tech-debt TD-001/002/003

**Branch:** `chore/tech-debt-td-001-002-003` · **Base:** `master` (`da17dbb`) · **Date:** 2026-09-10
**Scope:** cross-cutting tech-debt — TD-001 (null-safe keyset + cursor `n`-flag, ADR-0095 amended), TD-002 (shared `ConcurrencyTokenCapture`), TD-003 (FE unmapped-400 → toast).
**Commits:** `d7f7977` (impl) · `b483c5b` (/simplify) · `aeced92` (gate 6–8 fixes)

## Summary table

| Gate | Status | Evidence |
|------|--------|----------|
| 1 · Build, TreatWarningsAsErrors | ✅ green | `dotnet build Kartova.slnx -c Debug -p:TreatWarningsAsErrors=true` → exit 0 (final commit) |
| 2 · Per-task subagent reviews | ✅ | interleaved during dev; consolidated at gates 6–8 |
| 3 · Full test suite (unit+arch+integration, real seam) | ✅ green | full suite pre-fix green; final re-verify: SharedKernel 132, AspNetCore 100, Catalog integration 52 (incl. 2 new DESC null-boundary on real Postgres + both concurrency round-trips), FE 1094 |
| 4 · Container build (`images` job) | N/A | no Dockerfile / `COPY` change in the diff |
| 5 · /simplify | ✅ | 4 agents; applied UnwrapKeyConvert dedup, SortSpec-param, MethodInfo cache, zodFieldPaths WeakMap (`b483c5b`) |
| 6 · requesting-code-review | ✅ | ready-to-merge; 0 critical/important, 4 minor (all addressed or in gate-findings) |
| 7 · review-pr (4 lenses) | ✅ | silent-failure caught the **TD-003 mixed-key blocking** gap (fixed); + tests/types/comments findings addressed |
| 8 · deep-review | ✅ | `deep-review.md` — 1 blocking (fixed), should-fix cluster (applied), 1 design item deferred to owner |
| 9 · Visual / API — running system | ✅ green | `gate-9-verification.md` + `gate9-vm-provider-sort-asc.jpg` — TD-001 Provider sort driven live (asc NULLS LAST / desc NULLS FIRST, no truncation across the null boundary, real Postgres) + browser screenshot; TD-002 stale-If-Match → 412 `currentVersion` live; TD-003 has no live-triggerable surface (test-covered) |
| 10 · CI green on PR | ✅ green | PR #88 — all 5 checks pass (Backend arch+unit+integration, Frontend, Container images, Helm, Stryker). Pre-push: fresh audited restore (`--force-evaluate`, no NU1903) + Release build green |

## Terminal re-verify (post gate 5–8 fixes, final commit `aeced92`)

- Build (WAE): exit 0.
- `Kartova.SharedKernel.Tests`: 132/132. `Kartova.SharedKernel.AspNetCore.Tests`: 100/100.
- `Kartova.Catalog.IntegrationTests` (filtered: sort + concurrency): 52/52 on real Postgres.
- FE `tsc -b --noEmit`: exit 0 (caught + fixed an `as never` flow-narrowing regression). Touched FE test files: 27/27.

## Deferred to owner (design decisions, not applied)

- **SortSpec.IsNullable unsafe default** — flagged by the altitude + type-design lenses as the top item (a `false` spec over a runtime-nullable key silently drops rows; nothing enforces it). Options: make `IsNullable` a required positional record param (mechanical churn across all sort specs) **or** an arch test failing a nullable-EF-property spec with `IsNullable=false`. Left as-is (correct + tested today); awaiting owner's call before a ~broad public-type change.
- **Cursor doesn't bind `sortBy`** (pre-existing, out of TD-001 scope) — a `sortBy` change mid-pagination with reused cursor mis-pages silently. Candidate ADR-0095 follow-up / new TD item.

## Honest status

**All ten gates green** on PR #88. Gate 9 driven live on the running stack (TD-001 Provider NULLS-LAST/FIRST paging on real Postgres + browser screenshot; TD-002 412 `currentVersion`); TD-003 has no live-triggerable surface and is test-covered. Ready to merge.
