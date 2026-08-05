# Test strategy — Coverage policy

Section of `knowledge/quality/test-strategy.md`.


- Floor: 80% line coverage of changed lines per diff (QUA-010), or the repository's stricter
  configured threshold. Changed-line coverage keeps the gate proportional to the change and
  independent of legacy debt.
- Coverage is a floor, not a target (P1, P7): high coverage with weak assertions is worthless.
  That is why QUA-011 (assertions present) and QUA-014 (mutation-worthiness) exist alongside
  the number.
- Mutation spot-check (QUA-014): for each new branch guarding authorization, payment, or data
  deletion, invert the condition mentally or in a scratch run and name the test that fails. No
  failing test means the branch is untested regardless of line coverage. Full mutation-testing
  tools (mutmut, Stryker, PIT, cargo-mutants) are worthwhile on critical modules but are not a
  per-diff gate.
- What does not count toward adequacy: tests skipped or weakened to reach green (QUA-002 fails
  on sight), coverage from tests without assertions, generated code counted into the
  denominator when the repo excludes it.
- Record the measured value and tool in the handoff (`pytest --cov`, `cargo llvm-cov`, JaCoCo,
  Coverlet, nyc/v8, `flutter test --coverage`).
