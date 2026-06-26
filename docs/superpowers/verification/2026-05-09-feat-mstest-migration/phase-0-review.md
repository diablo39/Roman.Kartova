# Deep PR review — `feat/mstest-migration-phase-0`

**Date:** 2026-05-08
**Status:** OPEN (pre-merge gate)
**Reviewer:** in-session deep-review against spec / plan / ADRs / DoD
**Spec:** `docs/superpowers/specs/2026-05-08-xunit-to-mstest-migration-design.md`
**Plan:** `docs/superpowers/plans/2026-05-08-xunit-to-mstest-migration-plan.md`
**ADRs cited:** ADR-0083 (testing strategy, now superseded), ADR-0097 (this branch — MSTest v4 supersedes xUnit), ADR-0028, ADR-0080, ADR-0082, ADR-0084, ADR-0095
**DoD reference:** `CLAUDE.md` §"Definition of Done"
**Branch range:** `master..feat/mstest-migration-phase-0` — 13 commits, 10 files, +298 / −285 (net +13 lines after MTP-drop pivot)

## Overview

Phase 0 of a 13-phase xUnit → MSTest v4 migration. This branch lays migration groundwork only — no test code rewritten. Deliverables: MSTest v4 packages registered in CPM (`Directory.Packages.props:42-44`); empty placeholder `Directory.Build.props` for future cross-cutting test settings; new ADR-0097 superseding ADR-0083 (`docs/architecture/decisions/ADR-0097-mstest-supersedes-xunit.md`); ADR catalog index, CLAUDE.md testing bullet, and ADR-0083 status updated; mutation-testing baseline doc reusing the May 7 per-project Stryker reports; FluentAssertions `BeEquivalentTo` audit (12 sites, all flat collections). Two mid-flight pivots absorbed: scope expanded from 5 → 10 test projects after `src/Modules/**/*Tests*` discovery, and Microsoft.Testing.Platform (MTP) dropped from migration scope after the Stryker × MTP probe failed (Stryker.NET 4.14.1 doesn't support MTP per stryker-mutator/stryker-net#3094). Surviving migration scope is xUnit→MSTest framework + FluentAssertions→native asserts only; runner (VSTest), project SDK (`Microsoft.NET.Sdk`), and code-coverage tooling (`coverlet.collector`) all unchanged. Solution build is green with `-warnaserror` (0/0); full test suite green (398 tests across 9 projects).

## Blocking-class issues

None.

Phase 0 does not touch production code or test code; the build is green; the existing test suite still passes; the new ADR is internally consistent and cross-references resolve. No DoD gate is violated by this branch in its own right (Phase 0 has a relaxed DoD per spec §7.1 — no "real HTTP" or mutation-regression gate applies to a doc-and-tooling slice).

## Should-fix issues

### S-1. Per-module Stryker orchestration is the only path that works, but it's not promoted to spec/plan/CLAUDE.md.

- **Evidence:** `docs/superpowers/specs/baselines/2026-05-08-mstest-migration-mutation-baseline.md:7-13` ("Why not a fresh run?") documents that the root `stryker-config.json` invocation triggers a Stryker.NET-internal compile error in `Microsoft.AspNetCore.OpenApi.SourceGenerators\OpenApiXmlCommentSupport.generated.cs` (CS9234). The fallback is per-project orchestration via `mutation-targets.json` + the `mutation-sentinel` skill — captured in the baseline doc as tribal knowledge, not surfaced as a Phase 4/5/12 prerequisite anywhere a future engineer would land first.
- **Plan reference:** `docs/superpowers/plans/2026-05-08-xunit-to-mstest-migration-plan.md` Task 4.4 ("Mutation regression check") still says `dotnet stryker -tp src/Modules/Catalog/Kartova.Catalog.Tests/Kartova.Catalog.Tests.csproj -m "src/Modules/Catalog/**/*.cs"` or `dotnet stryker -f stryker-config.json` (per-module config, in this case from `src/Modules/Catalog/`). The first form is fine; the second form will trip the same source-generator bug because the per-module config also has `"solution": "Kartova.slnx"`.
- **Impact:** A Phase 4 reviewer cold-reading the plan/spec will likely default to one of the broken invocations and lose ~13 minutes per attempt before discovering the source-generator bug. The pattern that does work (per-source-project filter + the `mutation-sentinel` skill that produced the May 7 reports) lives entirely in tribal knowledge.
- **Fix:** Add a Phase 4 task-prelude (or a paragraph in spec §2.1.6) documenting the working invocation pattern explicitly, e.g. "Use `mutation-sentinel` skill or `dotnet stryker -tp <one-test-project> --mutate <one-source-project>` per the May 7 manifest pattern. The root `stryker-config.json` invocation is broken at this Stryker version due to a source-generator/interceptor regression; do not use it." Same prelude for Phase 5 and Phase 12.

### S-2. Mutation-gate language is more nuanced than spec §1 Goal 6 suggests.

- **Evidence:** `docs/superpowers/specs/2026-05-08-xunit-to-mstest-migration-design.md:17` says "Mutation score (Stryker targets `Catalog.Tests` and `Organization.Tests` per repo `stryker-config.json`) must match the pre-migration baseline ±1 percentage point per project." But the baseline doc's "Notes" (`docs/superpowers/specs/baselines/2026-05-08-mstest-migration-mutation-baseline.md:48-51`) flags 5 of 12 baselines as effectively un-gateable: 4 with `n/a` mutation score (zero evaluable mutants) and 1 with a 3-mutant denominator (`Kartova.Organization.Infrastructure.Admin`, where one extra survivor swings the score by ~33pt).
- **Impact:** A Phase 4/5 reviewer reading only the spec's "±1 percentage point per project" line and not the baseline doc's caveats will either (a) consider degenerate-baseline projects passed by default (false negative — any new mutant would actually be a regression) or (b) flag the `Infrastructure.Admin` 33% baseline as a hard floor and block merges over normal score volatility (false positive). Neither matches the implementer's intent in the baseline doc.
- **Fix:** Cross-reference the baseline doc's Notes section from spec §1 Goal 6 and §7.2 risks ("Mutation score regression post-migration in Stryker target projects"). Specifically replace "±1 percentage point per project" with "±1 percentage point per project for projects with non-degenerate baselines; see baseline-doc Notes for the four `n/a`-baseline projects (any new mutant is a regression worth inspecting) and `Organization.Infrastructure.Admin` (use absolute survivor count, not score delta)".

### S-3. ADR-0097 cross-references `ADR-0084 (Playwright MCP for dev-time verification)` even though the ADR is about backend test framework choice.

- **Evidence:** `docs/architecture/decisions/ADR-0097-mstest-supersedes-xunit.md:8` — the `**Related:**` line lists `ADR-0084 (Playwright MCP for dev-time verification — complementary to E2E test tier)`. This was inherited verbatim from ADR-0083's Related list during the supersedes drafting; ADR-0097's actual decision (test framework + assertion library swap, runner unchanged) doesn't intersect ADR-0084 at all. ADR-0084 is about frontend dev-time verification via Playwright MCP, not the backend test framework.
- **Impact:** Low — but the cross-reference graph in the ADR README's keyword index is supposed to be navigable; spurious "Related" entries dilute that signal.
- **Fix:** Remove `ADR-0084` from the `**Related:**` line in ADR-0097. ADR-0083's Related list will continue to cite ADR-0084 (correctly, since ADR-0083 is about the five-tier pyramid which includes E2E). The supersession chain is preserved either way.

## Nits

### N-1. Spec §1 Goal 5 wording is bumpy.

- **Evidence:** `docs/superpowers/specs/2026-05-08-xunit-to-mstest-migration-design.md:16` — "Phase 0 (tooling/ADR/CI) + Phases 1–11 (one project at a time, plus a final cleanup phase)". The parenthetical "(one project at a time, plus a final cleanup phase)" attaches grammatically to "Phases 1–11" but the cleanup is actually Phase 12. ADR-0097 (`docs/architecture/decisions/ADR-0097-mstest-supersedes-xunit.md:62`) says the cleaner "13 phases (Phase 0 + Phases 1–12)".
- **Impact:** None functional; reads slightly off.
- **Fix:** Replace with "Phase 0 (tooling/ADR) + Phases 1–11 (per-project migration) + Phase 12 (cleanup) — each phase mergeable on its own".

### N-2. Plan tech-stack header still mentions `MSTest.Sdk 4.x (Phase 12)` was supposed to be deleted.

- **Evidence:** `docs/superpowers/plans/2026-05-08-xunit-to-mstest-migration-plan.md:9` — the Tech Stack line in the header should not reference `MSTest.Sdk` since that flip is no longer in scope. Quick scan suggests the line was rewritten in commit `55b4990`, but worth verifying once more before merge that no header / Tech Stack references the removed Phase 12 work.
- **Impact:** Low — cosmetic post-pivot residue if any survives.
- **Fix:** Re-grep `MSTest.Sdk\|Microsoft.Testing.Platform\|MTP` across the plan doc; any hit not in the Stryker × MTP probe stub or the explicit "deferred" framing should be removed or reworded.

### N-3. ADR-0097 citation of `migrate-mstest-v3-to-v4` skill is a category-mismatch.

- **Evidence:** `docs/architecture/decisions/ADR-0097-mstest-supersedes-xunit.md:14` lists `migrate-mstest-v3-to-v4` in the dotnet-test skill family that drove the framework selection. That skill is for upgrading from MSTest v3 to v4, not for migrating from xUnit (this repo's actual case). The xUnit→MSTest direction has no dedicated skill.
- **Impact:** Trivial — the skill list is illustrative, not load-bearing.
- **Fix:** Either drop `migrate-mstest-v3-to-v4` from the citation or re-frame as "the MSTest skill family — `writing-mstest-tests`, `migrate-mstest-*`, `test-anti-patterns` — has dedicated MSTest coverage, vs only ad-hoc xUnit coverage" without naming the v3-specific migration skill.

### N-4. Baseline doc's Stryker × MTP probe section uses a `dotnet test` parenthetical that's a side-finding masquerading as a footnote.

- **Evidence:** `docs/superpowers/specs/baselines/2026-05-08-mstest-migration-mutation-baseline.md:64` — the parenthetical "(`dotnet test` on .NET 10 SDK rejects VSTest path)" is a real .NET-10-SDK-specific behavioral change that affects every future mixed VSTest/MTP scenario, not just the probe. It's buried inside the probe-success bullet.
- **Impact:** Low — since the migration drops MTP, no future work in this slice exercises the side-finding. But if a follow-up MTP migration uses this baseline doc as input, the parenthetical may be skimmed past.
- **Fix:** Optional — when the future MTP-revisit ADR is drafted, hoist the parenthetical to its own bullet so the .NET 10 SDK invocation change is unmissable. Not required for this slice.

### N-5. Plan Task 12.3 / 12.5 stub gaps lack a Phase 12 section-opener summary.

- **Evidence:** `docs/superpowers/plans/2026-05-08-xunit-to-mstest-migration-plan.md` — Phase 12 has gaps at task numbers 12.3 (`(Removed) — MSTest.Sdk flip deferred`) and 12.5 (`(Removed) — coverage tool replacement deferred`). The stubs themselves explain why; what's missing is a top-of-Phase-12 marker so a fast reader skimming task IDs sees the gaps as intentional rather than oversights.
- **Impact:** Low.
- **Fix:** Add a sentence to the Phase 12 header: "Tasks 12.3 (MSTest.Sdk flip) and 12.5 (coverlet → Microsoft.Testing.Extensions.CodeCoverage) are removed post-MTP-drop; see inline `(Removed)` notes."

## Missing tests

Phase 0 has no production-code or test-code changes — there are no acceptance criteria that warrant new tests at this phase. The full xUnit suite continues to pass (verified in Phase 0 verification, 398 tests across 9 projects, 0 failures). Phases 1–11 will replace tests one project at a time; Phase 0 specifies the regression baselines (mutation, BeEquivalentTo audit) those phases will measure against.

No missing-test findings.

## What looks good

### G-1. Multi-source audit trail for the MTP-drop pivot.

ADR-0097 §"Note: Microsoft.Testing.Platform (MTP) deferred" (`docs/architecture/decisions/ADR-0097-mstest-supersedes-xunit.md:36-38`), README history line (`docs/architecture/decisions/README.md:524`), baseline doc §"Stryker × MTP compatibility probe" (`docs/superpowers/specs/baselines/2026-05-08-mstest-migration-mutation-baseline.md:54-73`), and plan Task 0.6 stub (`docs/superpowers/plans/2026-05-08-xunit-to-mstest-migration-plan.md:217+`) all converge on the same facts: stryker-net#3094, FAIL, deferred indefinitely. Three independent breadcrumbs make the decision rationale recoverable six months later regardless of which doc a reader lands on first.

### G-2. ADR-0097 file rename via `git mv` preserves git history.

`git log --diff-filter=R --summary 6c77b7a..55b4990` shows a 64% similarity rename for `ADR-0097-mstest-and-mtp-supersedes-xunit.md` → `ADR-0097-mstest-supersedes-xunit.md`. Branch contains the rename rather than a delete+create, so `git log --follow` on the new filename traces the file's lineage through the original "with MTP" version into the post-pivot rewrite. Audit-trail-friendly.

### G-3. Mutation baseline pivot when fresh runs failed.

When two attempts to run Stryker freshly on 2026-05-08 hit a Stryker-internal source-generator bug, the implementer didn't commit `TBD%` placeholders or skip the deliverable. Instead the May 7 per-project reports (captured by mutation-sentinel exit_code=0, stored under `StrykerOutput/<project>/2026-05-07.20-36-42/reports/mutation-report.json`) were aggregated, and the validity claim was explicitly bounded with a falsifiable check: `docs/superpowers/specs/baselines/2026-05-08-mstest-migration-mutation-baseline.md:5` — "Phase 0 commits change only `Directory.Packages.props`, the new `Directory.Build.props`, ADR markdown, README index, and CLAUDE.md, so any fresh run would mutate the identical compilation units and produce the same Killed/Survived counts. Timeout nondeterminism is moot here because every project has Timeout = 0 in the May 7 reports." Reusable signal beats fabricated numbers.

### G-4. CPM comment is self-expiring.

`Directory.Packages.props:41` — `<!-- MSTest v4 — added during xUnit→MSTest migration; xUnit lines are removed in Phase 12 -->` tells a future reader both *why* these entries exist and *when they'll be cleaned up*. The next engineer reading the file knows the entries are transitional, not permanent test-stack policy.

### G-5. Baseline doc's merge-gate language is forward-defensive.

`docs/superpowers/specs/baselines/2026-05-08-mstest-migration-mutation-baseline.md:38-40` defines the mutation gate as a hard block ("the offending phase PR cannot merge until either (a) the regression is fixed and the score recovers within ±1pt, or (b) the surviving mutants are enumerated in this doc and the new floor is signed off") with a secondary CompileError-delta check (±5) that closes the silent "Killed → CompileError reclassification" loophole. The asymmetric handling of score *increases* (don't block, but sanity-check for reclassification) closes a footgun that a naive ±1pt rule would leave open.

### G-6. Phase 0 stays bounded — no production code or test code touched.

Diff stat: `Directory.Build.props` (new, 4 lines), `Directory.Packages.props` (+4), 2 ADR files, README, CLAUDE.md, 2 baseline docs, spec, plan. Zero production source changes, zero test code changes. If Phase 1 work needs to be paused or this branch needs to be reverted, the blast radius is documentation-only.

---

## Pre-merge checklist (DoD against `CLAUDE.md` §"Definition of Done")

| DoD gate | Status | Evidence |
|---|---|---|
| 1. Full build, `TreatWarningsAsErrors=true` | ✅ | Phase 0 Task 0.11 verification: `dotnet build Kartova.slnx -warnaserror` → 0 Warnings, 0 Errors |
| 2. Per-task subagent reviews (spec + code quality) | ✅ | All ~13 commits got both reviewers per turn-by-turn record; one Important finding on the MTP-drop commit was fixed in `d6d03d5` and re-reviewed |
| 3. `superpowers:requesting-code-review` at slice boundary | ⏳ | Not yet run as a separate invocation; this `/deep-review` overlaps the same scope |
| 4. Test suite green (unit + arch + integration) | ✅ | 398 tests across 9 projects, 0 failures (Phase 0 Task 0.11 + separate Kartova.Api.IntegrationTests verification) |
| 5. Real HTTP happy + negative path | n/a | Phase 0 doesn't wire HTTP / auth / DB / middleware; doc-and-tooling only |
| 6. `/simplify` run on diff | ⏳ | Not yet run |
| 7. Mutation sentinel ±1pt | n/a | Phase 0 *establishes* the baseline (Task 0.4); the gate applies at Phases 4 / 5 / 12 |
| 8. `/pr-review-toolkit:review-pr` | ⏳ | Not yet run |
| 9. `/deep-review` | ✅ | This document |

DoD items 3, 6, 8 are user-driven slash commands and remain pending. The rest are satisfied or appropriately n/a for this phase.
