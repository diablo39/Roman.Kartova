# Deep PR review — `feat/mstest-migration-phase-1`

**Date:** 2026-05-09
**Status:** OPEN (pre-merge gate)
**Reviewer:** in-session deep-review against spec / plan / ADRs / mutation report / DoD
**Spec:** `docs/superpowers/specs/2026-05-08-xunit-to-mstest-migration-design.md` (+ baselines: `2026-05-08-mstest-migration-mutation-baseline.md`, `2026-05-08-mstest-migration-beequivalentto-audit.md`)
**Plan:** `docs/superpowers/plans/2026-05-08-xunit-to-mstest-migration-plan.md`
**ADRs cited:** ADR-0097 (this migration's governing ADR), ADR-0083 (superseded), ADR-0095 (cursor pagination — referenced in tests)
**Mutation report:** `mutation-report-surviving.md` (repo root; gitignored working artifact)
**DoD reference:** `CLAUDE.md` §"Definition of Done"
**Branch range:** `master..feat/mstest-migration-phase-1` — 32 commits (24 new for Phase 1, 8 inherited from Phase 0; `git log --oneline master..HEAD | wc -l` = 32), 19 files, +750/−653

## Overview

Phase 1 of a 13-phase xUnit → MSTest v4 migration. The target project `tests/Kartova.SharedKernel.Tests` is the canonical-pattern phase: 7 test files (88 tests) translated from xUnit + FluentAssertions to MSTest v4 + native asserts, with the `Kartova.SharedKernel.Tests.csproj` package list updated (xUnit + FluentAssertions removed; three MSTest packages remain). One production-code change rode along: a single-line `(object)i` cast in `src/Kartova.SharedKernel/Pagination/CursorCodec.cs` that suppresses C# `?:` common-type widening of `long` → `double` for integer cursor sort-values, surfaced when MSTest's strict `Object.Equals` exposed FA's silent numeric coercion. Spec §9.1 was added in this branch to govern the production-fix-during-migration exception with five gates, and the codec fix is the precedent. All four slice-boundary skills (`superpowers:requesting-code-review`, `/simplify`, `/pr-review-toolkit:review-pr`, this `/deep-review`) have been run; findings from the first three were applied across commits `3343196` (3 nits) and `ba825e0` (5 doc improvements). Phase 1's mutation gate met: 100 % score (9 killed / 9 total) on the changed-file scope, 0 survivors, 0 NoCoverage. 398/398 tests pass solution-wide. Build green with `TreatWarningsAsErrors=true` (most recent at `ba825e0`).

## Blocking-class issues

None.

By the time `/deep-review` runs, three earlier slice-boundary lenses have already passed and fed back. Every finding rated Important by the prior reviewers has been applied (see commits `a5d692d` for the spec §9.1 amendment, `3343196` for /simplify nits, `ba825e0` for /pr-review-toolkit improvements). DoD evidence is cited per bullet:

- **DoD #1** Build green: `dotnet build Kartova.slnx -warnaserror` → `0 Warning(s), 0 Error(s)` confirmed at `ba825e0` (per the post-cleanup verifier subagent).
- **DoD #2** Per-task subagent reviews: turn-by-turn record shows spec-compliance + code-quality reviewers dispatched for every Phase 1 commit (`fcdf43f`, `6407837`, `a140840`, `161e626`, `8923438`, `bb3482b`, `e598111`, `c6a0c17`, `19f52dd`, `841f528`, `a5d692d`, `3343196`, `ba825e0`).
- **DoD #3** `superpowers:requesting-code-review`: run; one Important fix required (codec-fix bundling vs spec §9), addressed by §9.1 amendment in `a5d692d`.
- **DoD #4** Test suite: `dotnet test Kartova.slnx --no-build` → 398/398 pass across 10 dll-producing projects, last verified at `841f528` and again at `ba825e0`.
- **DoD #5** HTTP/auth/DB/middleware happy + negative path: n/a — Phase 1 changes are test-file translations + one production-code line in `CursorCodec.UnwrapJsonElement`. The full integration-test suite (`Kartova.Catalog.IntegrationTests` + `Kartova.Organization.IntegrationTests` + `Kartova.Api.IntegrationTests` = 103 tests) ran green via Testcontainers in DoD #4 and exercises the cursor codec via real HTTP keyset-paging endpoints. The relevant evidence path for the codec change is already covered by item #4.
- **DoD #6** `/simplify`: run; 3 nits applied in `3343196`.
- **DoD #7** Mutation gate: `mutation-report-surviving.md` PASS, 100 %, 0 survivors. Phase 1's gate-owning project per the per-phase ownership table is `Kartova.SharedKernel`; on the changed-file scope (`CursorCodec.cs`) all 9 mutants are killed.
- **DoD #8** `/pr-review-toolkit:review-pr`: run; 5 doc improvements applied in `ba825e0`.
- **DoD #9** `/deep-review`: this document.

## Should-fix issues

### S-1. Plan Task 1.6's mutation step ran outside the prescribed per-project Stryker config; result is correct but the precedent contradicts the in-plan note.

- **Evidence:** `docs/superpowers/plans/2026-05-08-xunit-to-mstest-migration-plan.md` Task 1.6 Step 4 (added during the I-1 fix in commit `bba2d9c`) prescribes:
  ```
  dotnet stryker -f src/Kartova.SharedKernel/stryker-config.json
  ```
  Phase 1's actual mutation regression check ran the **`mutation-sentinel` skill orchestrator** (`bash ./.claude/skills/mutation-sentinel/scripts/ms-detect-and-run.sh`), which executed all 12 per-project Stryker invocations chained with `&&` rather than just the SharedKernel one. The orchestrator chain takes ~5 hours wall-clock; the prescribed single-project run takes ~5 minutes.
- **Plan reference:** Task 1.6 Step 4 (mutation-regression command). The user explicitly chose option B (use the mutation-sentinel skill) when an earlier per-project run was killed, but the plan's prescribed command was never re-aligned to the orchestrator pattern.
- **Impact:** Future Phase 2 / 4 / 5 / 9 / 10 / 11 reviewers reading their mutation-step task will execute the documented per-project command. That's faster but produces a single per-project report, not the 12-project corpus the `mutation-sentinel` skill maintains. The two paths produce the same gate result for the owning phase, but only the orchestrator path keeps `mutation-report-surviving.md` and `mutation-sentinel-gh-last-run.manifest` synchronized for downstream consumers (`/test-generator`).
- **Fix:** Update the plan's mutation steps in Phases 1 / 2 / 9 / 10 / 11 to either (a) prefer the `mutation-sentinel` skill orchestrator (matches Phase 1's actual execution path, keeps `mutation-report-surviving.md` canonical, but ~5 hours wall-clock), or (b) keep the per-project invocation as the per-phase gate but note that the orchestrator must run at least once per phase to refresh the manifest. The Phase 2+ optimization note added in commit `3343196` (concurrency / parallel projects) hints at the same tension; resolving it before Phase 2 starts saves a re-litigation.

### S-2. The mutation-sentinel translator script (`ms-translate-stryker-results.ps1`) crashes on zero-survivor input; mutation-report-surviving.md was hand-written.

- **Evidence:** When running the translator after the orchestrator finished, the script exited with `Get-Content : Cannot find path '...\ms-survivors.tsv' because it does not exist.` at `.claude/skills/mutation-sentinel/scripts/ms-translate-stryker-results.ps1:325`. The script writes `ms-counts.json` (50 bytes — `{"Ignored":705,"CompileError":181,"Killed":9}`) and creates the `analysis/` directory, then tries to read a survivor TSV that was never written because there are zero survivors. The `mutation-report-surviving.md` at repo root was hand-written to satisfy the skill's output schema.
- **Skill reference:** `.claude/skills/mutation-sentinel/SKILL.md` Step 7 ("Write `mutation-report-surviving.md` exactly to contract") and Step 8 ("close the loop"). The output-schema contract was honored, but via a manual write because the canonical script path failed.
- **Impact:** The "all-mutants-killed" PASS path is the *least*-likely-to-fail post-migration outcome (every translation phase that doesn't introduce regressions hits this branch). Today the skill's report-writing path silently fails on success. Future Phase 2 / 4 / 5 / 9 / 10 / 11 will hit the same zero-survivor edge case unless the script is fixed.
- **Fix:** Patch `ms-translate-stryker-results.ps1` line 325 region to handle the empty-survivor case explicitly: when `ms-survivors.tsv` is absent (the upstream extraction step finds zero rows), skip the per-row analysis loop and write a `mutation-report-surviving.md` that says "no survivors" plus the summary counts from `ms-counts.json`. This is a 5-line fix and keeps the skill's promise that the canonical script writes the canonical report.

### S-3. Plan §"Stryker invocation note" Phase 12 exception clause now contradicts the orchestrator's actual behavior.

- **Evidence:** `docs/superpowers/plans/2026-05-08-xunit-to-mstest-migration-plan.md` (the Stryker invocation note added in commit `4ae1136`, line 41 area):
  > **Phase 12 (Task 12.6 — final mutation regression check) is the exception:** Phase 12 deliberately re-runs the root config as the post-migration full-suite gate. If the root invocation still trips CS9234 at that point, fall back to per-project runs and aggregate the scores manually.
  
  The Phase 1 mutation gate just *did* run the orchestrator pattern (12 per-project Stryker invocations chained with `&&`). The orchestrator does not use `stryker-config.json` (root); it builds 12 per-project invocations. So the "Phase 12 deliberately re-runs the root config" promise is at odds with the orchestrator that exists today. Either Phase 12 will not actually run the root config (the orchestrator is what works), or the root config has a different execution path that's not yet documented.
- **Plan reference:** Same section as S-1 above.
- **Impact:** Phase 12's mutation step will surface this discrepancy. Likely outcome: Phase 12 reviewer also uses the orchestrator (because the root-config still trips CS9234), and the "exception" clause is dead text.
- **Fix:** Replace the "Phase 12 deliberately re-runs the root config" framing with "Phase 12 deliberately re-runs the full per-project orchestrator post-migration; the per-phase incremental gate captures only changed-file scope, while Phase 12's full mode captures the full picture." Aligns the documented exception with the working tooling.

## Nits

### N-1. `mutation-report-surviving.md` is at the repo root but is gitignored — reviewers without local repo access cannot see DoD #7's evidence.

- **Evidence:** `.gitignore` excludes `mutation-report-surviving.md`. The file is the canonical artifact for DoD #7 (mutation gate). PR reviewers reading only the GitHub diff will not see it; they have to clone the branch + run the orchestrator (~5 hours) or find it referenced elsewhere.
- **Impact:** Low operationally — the per-task subagent reviews all cited the score in this conversation. But a future reviewer 6 months from now who is trying to retro-validate Phase 1's mutation evidence will have no on-repo trail.
- **Fix:** Either (a) un-ignore the file (it's small, ~4 KB, low-noise), or (b) add the headline score to the PR description / commit message of the slice-closing commit, or (c) commit a `mutation-report-summary-2026-05-09.md` snapshot to `docs/superpowers/specs/baselines/` that records the Phase 1 result. (c) parallels the Phase 0 mutation-baseline pattern.

### N-2. Codec fix commit (`19f52dd`) predates spec §9.1 amendment (`a5d692d`) by 5 commits. Future spec §9.1 readers grepping for the precedent will not find a back-reference.

- **Evidence:** Commit order is `…6407837 (KartovaConn) → … → 19f52dd (codec fix) → … → a5d692d (§9.1 amendment with precedent paragraph)`. The §9.1 precedent paragraph cites commit `19f52dd`; the commit message of `19f52dd` does not yet know §9.1 exists, so it doesn't mention §9.1 gate compliance.
- **Impact:** Trivial — `git show 19f52dd` shows the fix; `git show a5d692d` documents the rule it precedented. Bidirectional discoverability requires the §9.1 reader to know the precedent SHA.
- **Fix:** Optional. If a future fix-up touches `19f52dd`'s commit body (rare), add a one-liner referencing §9.1. As-is, the §9.1 → `19f52dd` direction is sufficient.

### N-3. The `Kartova.SharedKernel.Tests.csproj` line ordering after package removal leaves an asymmetric ItemGroup.

- **Evidence:** `tests/Kartova.SharedKernel.Tests/Kartova.SharedKernel.Tests.csproj:11-19` — after removing 3 xUnit/FA lines, the ItemGroup orders as `coverlet.collector / EFCore.Sqlite / Microsoft.NET.Test.Sdk / MSTest.{Analyzers,TestAdapter,TestFramework}`. Casing is mixed (lowercase `coverlet.collector` first, then PascalCase). Original xUnit-included file had the same casing pattern, so this is preserved style.
- **Impact:** Cosmetic.
- **Fix:** None recommended — preserves repo convention.

### N-4. The mutation report's `Total mutants: 895` is the sum of Killed (9) + Ignored (705) + CompileError (181), which sums to 895. But the schema's "Total mutants" semantically usually means evaluable + ignored + compile-error. Reading the report cold, "9 killed / 9 total" + "Total mutants: 895" looks like a contradiction.

- **Evidence:** `mutation-report-surviving.md` lines 6 (`9 killed / 9 total`) and 12 (`Total mutants: 895`).
- **Impact:** A reader's first reaction is "wait, are there 9 mutants or 895?" The score line `9/9` is correct (denominator excludes Ignored + CompileError); the summary's `895` is the raw total. Both are right but coexist awkwardly.
- **Fix:** Add a one-line note in the Summary section: "**Total mutants** counts all mutant slots (including those Ignored or excluded for compile errors); **Score** uses only the evaluable subset per Stryker's standard formula." Or rename `Total mutants` → `Total mutant slots`.

### N-5. Plan Task 1.6 prescribes the per-phase mutation step with `--since:master`, which is implicit in the per-project config; the orchestrator already injects `--since:master` per project. The plan's command form is non-canonical.

- **Evidence:** `docs/superpowers/plans/2026-05-08-xunit-to-mstest-migration-plan.md` Task 1.6 Step 4 (the mutation-regression-check step). It cites `dotnet stryker -f src/Kartova.SharedKernel/stryker-config.json` without `--since:master`. The orchestrator runs each project with `--since:master --solution Kartova.slnx --project <single>.csproj`. Without `--since`, Stryker runs full mode (~30+ min for SharedKernel) instead of incremental (~5 min).
- **Impact:** A future reviewer copying the plan command verbatim runs full mode and waits much longer than necessary.
- **Fix:** Update the prescribed command to include `--since:master` (or note that the per-phase mutation gate should always run incremental, not full).

## Missing tests

Phase 1 is a 1:1 mechanical translation per spec §1 Goal 6 ("Translate test count and behavior 1:1. No new tests, no removed coverage"). The codec fix tightened existing tests rather than adding new ones — `Assert.IsInstanceOfType<long>` plus `Assert.AreEqual(42L, …)` replaced a `Convert.ToInt64`-based shim.

The pr-test-analyzer reviewer flagged one suggestion the deep lens corroborates:

- **No precision-edge regression test for `Encode → Decode` integer roundtrip past `2^53`.** A concrete missing case is: encode `9_007_199_254_740_993L` (one above `2^53`, exactly the boundary where `double` loses precision), decode, assert `Assert.AreEqual(9_007_199_254_740_993L, decoded.SortValue)` and `Assert.IsInstanceOfType<long>`. This test would *also* have caught the original bug independently (the buggy `?:` widening would round large Int64 cursor values via `double`), and it pins the user-visible consequence of preserving `Int64`. The current 42L test is sufficient evidence that the fix works at small magnitudes; it's not sufficient evidence the fix protects against the precision class the bug actually threatened.
  - Out-of-scope for Phase 1 by spec §1 Goal 6 (translation only). In-scope under §9.1 gate #2 ("tightened test that locks in the corrected runtime type or behaviour"). The existing test is *runtime-type-tightened*; it is not *precision-class-tightened*. A small follow-up under §9.1 — single new test method, no production change, clearly pins the actual user-visible contract — would close the gap.

No other missing-test findings.

## What looks good

### G-1. `Directory.Packages.props` migration comment is self-expiring; Task 12.4 plans the cleanup explicitly.

`Directory.Packages.props:41` (preserved unchanged in Phase 1):
```
<!-- MSTest v4 — added during xUnit→MSTest migration; xUnit lines are removed in Phase 12 -->
```

…and the plan's Task 12.4 Step 3 (added in commit `4ae1136`) explicitly tells Phase 12 executors to trim this comment after removing the xUnit lines. The comment is honest about its transitional nature *and* the cleanup is scheduled. Avoids the comment-rot pattern the comment-analyzer reviewer warned about.

### G-2. Spec §9.1's amendment cycle is exemplary process discipline.

The flow was: codec fix landed (commit `19f52dd`) → slice-boundary review flagged "this violates spec §9 — production code change is out of scope" → user chose to keep the fix → spec §9.1 amended (commit `a5d692d`) with five gates and the Phase 1 commit as the precedent. The governance text caught up with the engineering reality without rewriting history. Future Phase 4 / 5 / 9 / 10 / 11 reviewers landing on a similar migration-surfaced bug now have a documented precedent to apply or reject the gates against. Visible at `docs/superpowers/specs/2026-05-08-xunit-to-mstest-migration-design.md` §9.1 lines 427–448.

### G-3. Three independent corroborating signals all point to translation correctness.

- **Build:** `dotnet build Kartova.slnx -warnaserror` → 0 warnings, 0 errors at `ba825e0`.
- **Tests:** `dotnet test Kartova.slnx --no-build` → 398/398 pass at `ba825e0`.
- **Mutation:** `mutation-report-surviving.md` PASS, 100 % score on changed-file scope, 0 survivors, 0 NoCoverage.

The mutation evidence is the strongest because it tests the tests: every translated assertion in `CursorCodecTests.cs` killed every generated mutant, including ones the FA-coerced pre-migration tests would have allowed to survive (the `Assert.IsInstanceOfType<long>` guard alone kills any mutation flipping the long arm of the switch to the double arm).

### G-4. Comments preserved 100% across the translation, with migration-aware updates where the assertion API changed.

Spot-verified by the comment-analyzer reviewer + this lens:
- ADR-0095 §5 reference at `SortSpecTests.cs:28-30` — preserved verbatim.
- Mutation-killing rationale at `SortSpecTests.cs:38-40` — preserved with trailing line correctly updated `BeSameAs fails` → `AreSame fails`.
- MC/DC pair table at `TenantContextAccessorTests.cs:11-21` — preserved verbatim including the unreachability note.
- Stable-diagnostic-shape at `KartovaConnectionStringsTests.cs:29-30` — preserved.
- Mutation-killing comments in `CursorCodecTests.cs:107, 122, 133, 177-179` — preserved.

Lossy patterns (FluentAssertions `because:` strings) were uniformly converted to adjacent code comments, preserving source-level rationale even when the runtime failure-message richness was lost.

### G-5. Helpers are appropriately scoped and named to make their intent self-evident.

After the `/simplify` rename pass:
- `CaptureArgumentExceptionOrDerived` (`CursorFilterMismatchExceptionTests.cs:13-25`) — name conveys the base-type-or-derived permissiveness; comment block above explains the `ArgumentException.ThrowIfNullOrWhiteSpace` BCL-implementation-detail rationale.
- `AssertAscending<T>` / `AssertDescending<T>` (`QueryablePagingExtensionsTests.cs:93-111`) — `where T : IComparable<T>` constraint is the right level (works for `Guid`, `string`, `DateTime`, `int`); failure messages include offending pair; private to the file (no premature extraction).

The rename + comment together force a future maintainer toward the right behavior: tightening the helper to `Assert.ThrowsExactly<ArgumentException>` would visibly contradict both the name and the comment, surfacing the wrong-direction edit immediately.

---

## Summary line

**Verdict: ready to merge.** No blocking findings. Three Should-fix items (S-1 through S-3) are about plan/skill alignment for Phases 2+; they don't gate Phase 1 itself but should land before Phase 2 starts so the next mutation gate doesn't re-litigate the same questions. Five Nits and one Missing-test suggestion are non-blocking. The branch represents a high-quality canonical pattern that subsequent phases (2, 3, 4, 5, 6, 7, 8, 9, 10, 11) can copy without re-deriving the conventions.
