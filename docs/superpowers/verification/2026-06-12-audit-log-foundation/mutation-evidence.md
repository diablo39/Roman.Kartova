# Mutation testing evidence — audit-log-foundation — 2026-06-15

Branch: `feat/audit-log-foundation` · DoD gate 7 (mutation feedback, ≥80% target per `stryker-config.json`).

## Scope

Stryker.NET run against `src/Modules/Audit/Kartova.Audit.Domain` (the tamper-evidence
logic: `AuditCanonicalSerializer`, `AuditRowHasher`, `AuditLogEntry`, `AuditChainInspector`,
`AuditChainVerificationResult`) using the fast unit tests in `Kartova.Audit.Domain.Tests`.

Config: `src/Modules/Audit/stryker-config.json`; project registered in `mutation-targets.json`.
Command: `dotnet stryker --config-file src/Modules/Audit/stryker-config.json --project Kartova.Audit.Domain.csproj`.

`Kartova.Audit.Infrastructure` (`AuditWriter`/`AuditChainVerifier`) is covered by the
Testcontainers integration suite, not mutation-tested (each mutant would re-spin a Postgres
container — prohibitively slow for negligible value over the integration assertions). `AuditModule`
is `[ExcludeFromCodeCoverage]` composition.

## Result

**Final mutation score: 100.00%** — 46 valid mutants, all killed (0 survived, 0 no-coverage).
32 mutants filtered by the configured `ignore-mutations` (Block/String/Linq/Regex/Update);
2 CompileError mutants excluded from the denominator per Stryker.

### Progression (the feedback loop)

| Run | Score | Survivors | Action |
|-----|-------|-----------|--------|
| 1 | 84.78% | 7 | Initial. Survivors: timestamp-truncation arithmetic, null-actor/null-data NoCoverage branches, guard-removal statements. |
| 2 | 95.65% | 2 | Added targeted killing tests (truncation direction, null branches, blank-targetType/targetId + prevHash-null guards). |
| 3 | 100.00% | 0 | Removed a redundant serializer truncation line (`ffffff` already truncates to µs — equivalent-mutant source) and pinned a golden-value hash for the null-actor row. |
| 4 | 100.00% | 0 | Re-confirmed after adding the `Enum.IsDefined(actorType)` guard (each guard mutant killed by `Create_rejects_undefined_actor_type` + existing Create tests). |

≥80% target met (100%). No surviving mutants accepted as low-value — all were either killed
or eliminated as genuinely-equivalent redundant code.

> The per-run `mutation-report-surviving.md` (Stryker translator output) is gitignored as a
> transient artifact; this file is the durable record.
