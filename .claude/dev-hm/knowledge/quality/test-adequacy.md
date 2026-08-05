# Test adequacy

Whether the suite would actually catch a regression — the question line coverage cannot
answer. `knowledge/quality/test-strategy.md` sets the pyramid, the coverage floor, and the
flakiness rules; this file adds the checks that measure how hard the tests bite: mutation
testing, assertion strength, contract tests at deployment boundaries, and the determinism of
the checks themselves. The quality oracle's adequacy entries point here. Standards editions
cited are anchored in `knowledge/shared/versions.md`.

## Sections

Read the section you need, not the file. Each row is a separate file.

| Section | File |
|---|---|
| Coverage is not adequacy | `knowledge/quality/test-adequacy/coverage-is-not-adequacy.md` |
| Mutation | `knowledge/quality/test-adequacy/mutation.md` |
| Assertion strength | `knowledge/quality/test-adequacy/assertion-strength.md` |
| Contract tests | `knowledge/quality/test-adequacy/contract-tests.md` |
| Control-verification tests, judged as tests | `knowledge/quality/test-adequacy/control-verification-tests-judged-as-tests.md` |
| Two-run determinism | `knowledge/quality/test-adequacy/two-run-determinism.md` |
