# Quality oracle — Migration safety

Section of `oracles/quality-oracle.md`. Verdict grammar: `knowledge/shared/defense-in-depth.md`. Severities and waivers: `knowledge/shared/severity-tiers.md`.


| ID | Check | Pass criterion | Sev | Refs | Remediation |
|---|---|---|---|---|---|
| QUA-090 | Expand-contract migrations | Schema changes are backward compatible with the running version (additive first); destructive steps (drop, rename, type narrowing) ship in a separate later change with the sequence stated in the handoff | S1 | ISO compatibility (co-existence) | knowledge/quality/data-migration-safety/expand-contract.md |
| QUA-091 | Migration reversibility | Every migration carries a rollback path (down migration or restore procedure) or a recorded irreversibility justification | S2 | ISO reliability (recoverability) | knowledge/quality/data-migration-safety/reversibility.md |
| QUA-092 | Backfill discipline | Data backfills are idempotent (re-runnable without double effects), batched or rate-limited, and record progress; all three visible in the code | S1 | ISO reliability (fault tolerance) | knowledge/quality/data-migration-safety/backfills.md |
| QUA-093 | Migration verification recorded | Migrations that transform data record a verification check (row counts, checksums, or invariant queries) run before and after; results in the handoff | S2 | ISO functional suitability (functional correctness) | knowledge/quality/data-migration-safety/verification.md |
| QUA-094 | Constraints on new required data | New columns holding required business data (data the application reads assuming presence, with no null-or-absent branch) carry matching constraints (NOT NULL with a default strategy, foreign keys, checks, uniqueness) or the handoff states why not | S2 | ISO functional suitability (functional correctness) | knowledge/quality/data-migration-safety/constraints.md |
