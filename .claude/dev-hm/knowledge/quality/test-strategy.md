# Test strategy

Cross-language test strategy for every dev-hm stack: what to test at which level, coverage
policy, flakiness control, and the CI gates that enforce it. Oracle entries QUA-002 – QUA-006
and QUA-010 – QUA-015 point here. Stack-specific tooling lives in the per-stack testing files
(`knowledge/<stack>/testing.md`); this file is the policy they share. Standards editions cited
are anchored in `knowledge/shared/versions.md`. Adequacy beyond coverage — mutation tooling,
assertion strength, contract tests — lives in `knowledge/quality/test-adequacy.md`; tests that
verify protective controls follow `knowledge/security/control-verification-tests.md`.

## Sections

Read the section you need, not the file. Each row is a separate file.

| Section | File |
|---|---|
| The test pyramid | `knowledge/quality/test-strategy/the-test-pyramid.md` |
| ISTQB CTFL principles applied | `knowledge/quality/test-strategy/istqb-ctfl-principles-applied.md` |
| What to test at which level | `knowledge/quality/test-strategy/what-to-test-at-which-level.md` |
| Edge cases | `knowledge/quality/test-strategy/edge-cases.md` |
| Coverage policy | `knowledge/quality/test-strategy/coverage-policy.md` |
| Flakiness control | `knowledge/quality/test-strategy/flakiness-control.md` |
| CI test gates | `knowledge/quality/test-strategy/ci-test-gates.md` |
