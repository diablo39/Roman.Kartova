# MSTest migration — mutation baseline (Phase 0)

**Date:** 2026-05-08
**Stryker version:** 4.14.1 (per `dotnet tool list -g | grep dotnet-stryker`; same version that produced the May 7 reports).
**Source of baseline data:** Reused from per-project mutation reports captured on 2026-05-07 (manifest: `StrykerOutput/mutation-sentinel-gh-last-run.manifest`, run started `2026-05-07T20:36:56Z`, exit_code=0). Phase 0 of this migration touches only documentation/CPM/ADRs — no production code — so May 7 reports remain a valid pre-migration baseline. Stryker's mutation targets are the `src/**` assemblies enumerated in `mutation-targets.json`; none of them have been modified between 2026-05-07 and 2026-05-08 (Phase 0 commits change only `Directory.Packages.props`, the new `Directory.Build.props`, ADR markdown, README index, and CLAUDE.md), so any fresh run would mutate the identical compilation units and produce the same Killed/Survived counts. Timeout nondeterminism is moot here because every project has Timeout = 0 in the May 7 reports.

## Why not a fresh run?

Two attempted fresh runs on 2026-05-08 hit terminal failures:
- Root `stryker-config.json` invocation triggers a Stryker.NET-internal compile error rolling back a mutation in `Microsoft.AspNetCore.OpenApi.SourceGenerators\OpenApiXmlCommentSupport.generated.cs` (CS9234: interceptor file not found). Stryker self-diagnoses as a tool bug.
- Per-module fallback (`-f src/Modules/Catalog/stryker-config.json --test-project ...`) still discovers the whole solution as mutation targets via the config's `"solution": "Kartova.slnx"` field and trips the same source-generator issue.

The repo's working pattern is per-project orchestration via `mutation-targets.json` + the `mutation-sentinel` skill, which produced the reports we're reusing here. Phase 4 / 5 / 12 will follow the same per-project pattern when capturing post-migration scores.

## Baseline scores (xUnit, pre-migration; from 2026-05-07 reports)

| Project | Mutation score | Killed | Survived | No coverage | Timeout | Ignored | Compile error |
|---|---|---|---|---|---|---|---|
| Kartova.Catalog.Application | n/a | 0 | 0 | 0 | 0 | 0 | 0 |
| Kartova.Catalog.Contracts | n/a | 0 | 0 | 0 | 0 | 0 | 0 |
| Kartova.Catalog.Domain | 100.00% | 39 | 0 | 0 | 0 | 47 | 0 |
| Kartova.Catalog.Infrastructure | 100.00% | 30 | 0 | 0 | 0 | 124 | 8 |
| Kartova.Organization.Application | n/a | 0 | 0 | 0 | 0 | 0 | 0 |
| Kartova.Organization.Contracts | n/a | 0 | 0 | 0 | 0 | 0 | 0 |
| Kartova.Organization.Domain | 81.82% | 9 | 2 | 0 | 0 | 11 | 0 |
| Kartova.Organization.Infrastructure | n/a | 0 | 0 | 0 | 0 | 45 | 0 |
| Kartova.Organization.Infrastructure.Admin | 33.33% | 1 | 2 | 0 | 0 | 21 | 0 |
| Kartova.SharedKernel | 75.00% | 9 | 3 | 0 | 0 | 40 | 39 |
| Kartova.SharedKernel.AspNetCore | 100.00% | 3 | 0 | 0 | 0 | 145 | 110 |
| Kartova.SharedKernel.Postgres | 94.74% | 36 | 2 | 0 | 0 | 144 | 24 |

Mutation score = `killed / (killed + survived + no-coverage + timeout) × 100`. CompileError and Ignored are excluded from the denominator per Stryker's standard formula. `n/a` indicates the project produced 0 evaluable mutants in this run (every candidate site was filtered or excluded).

## Mutation gate (per spec §7.2)

Phases 4, 5, and 12 must keep the relevant per-project mutation score within **±1 percentage point** of the baseline above. Investigate any deviation > 1pt before merging the offending phase.

**Merge gate:** if a project's mutation score drops by > 1pt vs the baseline above, the offending phase PR cannot merge until either (a) the regression is fixed and the score recovers within ±1pt, or (b) the surviving mutants are enumerated in this doc and the new floor is signed off by the project owner. Score *increases* > 1pt do not block merge but should be sanity-checked to confirm the gain is from real coverage and not from mutants reclassifying to CompileError (which would silently leave the denominator and inflate the score — see CompileError-delta check below).

**Secondary check (CompileError delta):** Phase 4 / 5 / 12 reviewers should additionally verify the per-project CompileError count is within ±5 of the baseline. A large CompileError swing — even when the headline score looks fine — is a signal that test coverage instrumentation has shifted in a way that's reclassifying Killed mutants as uncompilable, which silently inflates the mutation score. If CompileError swings by more than ±5, dig into the raw report and confirm no Killed→CompileError reclassification.

- **Phase 4** (`Kartova.Catalog.Tests` migration): regression check against `Kartova.Catalog.{Domain, Application, Infrastructure, Contracts}` baselines.
- **Phase 5** (`Kartova.Organization.Tests` migration): regression check against `Kartova.Organization.{Domain, Application, Infrastructure, Infrastructure.Admin, Contracts}` baselines.
- **Phase 12** (cleanup): full re-run; all 12 projects within ±1pt.

## Notes on the May 7 baseline run

- Four projects produced **0 evaluable mutants** (`Kartova.Catalog.Application`, `Kartova.Catalog.Contracts`, `Kartova.Organization.Application`, `Kartova.Organization.Contracts`). Their report `files` arrays were populated but every `mutants` array was empty — likely the result of the project-level Stryker config filtering pure-DTO/Contracts assemblies (and Application projects that currently contain only handler scaffolds). For these the `±1pt` gate is degenerate; treat any mutant produced post-migration as a regression worth inspecting on its own merits, and do not require a numeric score match.
- `Kartova.Organization.Infrastructure` produced 45 Ignored mutants but **0 evaluable** ones — same caveat as above. The `Ignored` count being non-zero confirms the run actually visited source; no killed/survived means the live mutation gate doesn't apply numerically until real targets exist.
- `Kartova.Organization.Infrastructure.Admin` shows a low score (33.33%) on a tiny denominator (3 mutants) — one extra survivor would swing the score by ~33pt, so the ±1pt rule is brittle here. Phase 5 should check absolute survivor count rather than score delta for this project until coverage thickens.
- `Kartova.SharedKernel.AspNetCore` and `Kartova.SharedKernel.Postgres` carry large `CompileError` counts (110 and 24). These are mutations Stryker generated but couldn't compile — they're excluded from the score per formula and represent neither a regression risk nor extra coverage. No action needed; flagged here so the numbers don't surprise reviewers.
- All scores are computed from raw mutant-status counts in the JSON reports; Stryker's report schema (v2) does not embed a top-level `score` field, so there is no Stryker-side number to reconcile against.

## Stryker × MTP compatibility probe

**Date:** 2026-05-08
**Stryker version:** 4.14.1 (`dotnet tool list -g`)
**MSTest.Sdk version:** 4.2.2 (matches `Directory.Packages.props`)
**Probe location:** ad-hoc throwaway project in `C:\temp\stryker-mtp-probe` (cleaned up after probe; not committed). Layout: `calc/` class library (mutation target — `Calculator.Add`, `Calculator.Multiply`) + `probe/` MSTest.Sdk/4.2.2 test project on `net10.0` with `<UseMicrosoftTestingPlatformRunner>true</UseMicrosoftTestingPlatformRunner>` referencing `calc`.

**Result:** **FAIL**

**Notes:**
- Native MTP run (`dotnet run --no-build` against the test project, since `dotnet test` on .NET 10 SDK rejects VSTest path) reported `total: 2 / failed: 0 / succeeded: 2`, exit code 0 — the probe project itself is healthy under MTP.
- `dotnet stryker` (4.14.1) explicitly rejected the project with:
  - `[ERR] TestDiscoverer: Test discovery has been aborted!`
  - `[WRN] Project 'C:\temp\stryker-mtp-probe\probe\probe.csproj' is using Microsoft.Testing.Platform which is not yet supported by Stryker, see https://github.com/stryker-mutator/stryker-net/issues/3094`
  - Final message: `No test result reported. Make sure your test project contains test and is compatible with VsTest. Project '...probe.csproj' is using Microsoft.Testing.Platform which is not yet supported by Stryker, see https://github.com/stryker-mutator/stryker-net/issues/3094`
- 0 mutants generated, 0 tests run; Stryker bailed during analysis after the test-discovery abort.
- Stryker process exit code: 0 (Stryker exits 0 even on this failure mode — the failure is in the human-readable output and absence of mutants, not the process exit code; downstream tooling cannot rely on exit code alone to detect the incompatibility).
- The cause is **structural**: Stryker.NET 4.14.1 drives test runs through the legacy VSTest console, which MTP-only projects no longer expose. This is independent of the OpenAPI source-generator/interceptor bug that derailed Task 0.4's fresh runs against the real solution — that bug is a separate pre-existing Kartova.Api issue. The probe directly proves Stryker × MTP itself is broken at this version pair.

**Implication for Phase 12:** Phase 12 (the universal `MSTest.Sdk` + MTP flip) **cannot** include the Stryker target projects (`Kartova.Catalog.Tests`, `Kartova.Organization.Tests`, plus any other test project Stryker drives through the per-project `mutation-targets.json`) until Stryker.NET ships MTP support (issue [stryker-mutator/stryker-net#3094](https://github.com/stryker-mutator/stryker-net/issues/3094)). Concrete options for the Phase 12 plan:
1. **Hold the MTP flip on Stryker-driven test projects** — keep `Kartova.Catalog.Tests` and `Kartova.Organization.Tests` on the classic `Microsoft.NET.Sdk` + MSTest packages + VSTest path until Stryker.NET upstream lands MTP, then flip them in a follow-up phase. Other test projects (architecture, integration, contract) can flip in Phase 12 unchanged.
2. **Pin Stryker.NET to a version that worked for us pre-MTP and stay on it** — not viable here because the failing version *is* current (4.14.1) and there is no known earlier version that supports MTP either; this option exists only if a future Stryker version regresses MTP support after gaining it.
3. **Wrap MTP execution manually** — write a custom test runner adapter or use Stryker's `--test-runner-command` flag (if/when added) to invoke MTP entrypoints. Speculative; would need a spike.

The plan-of-record recommendation is option (1): leave the two Stryker-driven test projects on VSTest in Phase 12, complete the rest of the migration, and revisit once stryker-net#3094 closes. Add a note to the Phase 12 plan and a tracking item to the post-MVP backlog.
