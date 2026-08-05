# Quality oracle — Test adequacy

Section of `oracles/quality-oracle.md`. Verdict grammar: `knowledge/shared/defense-in-depth.md`. Severities and waivers: `knowledge/shared/severity-tiers.md`.


| ID | Check | Pass criterion | Sev | Refs | Remediation |
|---|---|---|---|---|---|
| QUA-010 | Coverage floor | Line coverage of changed lines is at least 80%, or the repository's stricter configured threshold; measured value and tool recorded | S1 | P2 | knowledge/quality/test-strategy/coverage-policy.md |
| QUA-011 | Assertions present | Every added test asserts observable behavior; zero tests without assertions or with tautological assertions | S2 | P1 | knowledge/quality/test-strategy/what-to-test-at-which-level.md |
| QUA-012 | No sleep-based synchronization | New tests wait on conditions with timeouts (await, poll-until); zero fixed sleeps used for synchronization unless justified by an inline comment | S2 | — | knowledge/quality/test-strategy/flakiness-control.md |
| QUA-013 | Test isolation | New tests pass when run alone and in a different order; no dependence on shared mutable state or execution order; an isolation run is recorded | S2 | — | knowledge/quality/test-strategy/flakiness-control.md |
| QUA-014 | Mutation-worthiness on critical logic | For each new branch enforcing a protective control mapped under SEC-130 (authorization, payment, and data deletion are the canonical examples): inverting the condition makes at least one test fail; the spot-check is recorded | S2 | P4, P5 | knowledge/quality/test-strategy/coverage-policy.md |
| QUA-015 | No retry masking | Zero test-retry annotations, rerun-on-failure flags, or raised retry counts added to make an unstable test pass; flaky tests are fixed or quarantined with a tracked reason | S2 | P5 | knowledge/quality/test-strategy/flakiness-control.md |
| QUA-016 | Mutation tool on critical modules | When the repository configures a mutation-testing tool, changes to QUA-014-class modules (those enforcing protective controls mapped under SEC-130 — authorization, payment, data deletion) run it and meet the configured threshold; value recorded; n/a when not configured | S2 | P4, P5 | knowledge/quality/test-adequacy/mutation.md |
| QUA-017 | Contract test moves with contract | A diff that changes a wire contract (API schema, message shape, file format) updates the corresponding contract or schema test in the same change; zero contract changes without a matching test change | S1 | ISO compatibility (interoperability) · P1 | knowledge/quality/test-adequacy/contract-tests.md |
| QUA-113 | Contract compatibility check runs | When the repository configures a compatibility check for a wire contract the diff changes (breaking-change linter, schema-registry compatibility mode, or a consumer/N-1 contract suite), the check runs against the last published contract version and its result is recorded in the handoff; n/a when no such check is configured | S1 | ISO compatibility (interoperability) · P1 | knowledge/quality/api-compatibility/tooling.md |
