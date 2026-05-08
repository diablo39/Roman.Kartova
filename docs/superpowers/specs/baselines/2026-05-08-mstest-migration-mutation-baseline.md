# MSTest migration — mutation baseline (Phase 0)

**Date:** 2026-05-08
**Stryker version:** 4.14.1
**Source of baseline data:** Reused from per-project mutation reports captured on 2026-05-07 (manifest: `StrykerOutput/mutation-sentinel-gh-last-run.manifest`, run started `2026-05-07T20:36:56Z`, exit_code=0). Phase 0 of this migration touches only documentation/CPM/ADRs — no production code — so May 7 reports remain a valid pre-migration baseline.

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

- **Phase 4** (`Kartova.Catalog.Tests` migration): regression check against `Kartova.Catalog.{Domain, Application, Infrastructure, Contracts}` baselines.
- **Phase 5** (`Kartova.Organization.Tests` migration): regression check against `Kartova.Organization.{Domain, Application, Infrastructure, Infrastructure.Admin, Contracts}` baselines.
- **Phase 12** (cleanup): full re-run; all 12 projects within ±1pt.

## Notes on the May 7 baseline run

- Four projects produced **0 evaluable mutants** (`Kartova.Catalog.Application`, `Kartova.Catalog.Contracts`, `Kartova.Organization.Application`, `Kartova.Organization.Contracts`). Their report `files` arrays were populated but every `mutants` array was empty — likely the result of the project-level Stryker config filtering pure-DTO/Contracts assemblies (and Application projects that currently contain only handler scaffolds). For these the `±1pt` gate is degenerate; treat any mutant produced post-migration as a regression worth inspecting on its own merits, and do not require a numeric score match.
- `Kartova.Organization.Infrastructure` produced 45 Ignored mutants but **0 evaluable** ones — same caveat as above. The `Ignored` count being non-zero confirms the run actually visited source; no killed/survived means the live mutation gate doesn't apply numerically until real targets exist.
- `Kartova.Organization.Infrastructure.Admin` shows a low score (33.33%) on a tiny denominator (3 mutants) — one extra survivor would swing the score by ~33pt, so the ±1pt rule is brittle here. Phase 5 should check absolute survivor count rather than score delta for this project until coverage thickens.
- `Kartova.SharedKernel.AspNetCore` and `Kartova.SharedKernel.Postgres` carry large `CompileError` counts (110 and 24). These are mutations Stryker generated but couldn't compile — they're excluded from the score per formula and represent neither a regression risk nor extra coverage. No action needed; flagged here so the numbers don't surprise reviewers.
- All scores are computed from raw mutant-status counts in the JSON reports; Stryker's report schema (v2) does not embed a top-level `score` field, so there is no Stryker-side number to reconcile against.
