# Test strategy — CI test gates

Section of `knowledge/quality/test-strategy.md`.


The pipeline enforces what the oracle checks, so the gate agents verify configuration once
instead of re-running everything (QUA-002 evidence comes from these runs):

- Test gate: full relevant suite on every change; fail on any test failure and on any newly
  skipped test. Quarantined tests run in a separate non-blocking job with a tracked list, so
  quarantine is visible, not silent.
- Coverage gate: changed-line coverage computed against the floor; publish the number in the
  run output so handoffs can cite it (QUA-010).
- Determinism aids: fixed seeds logged, test order shuffling enabled where the framework
  supports it (detects order dependence early), timeouts on every suite so a hang fails fast
  instead of stalling the pipeline.
- Speed budget: the unit-test job stays fast enough that developers run it before handoff —
  under ten minutes as a rule of thumb; slower integration/E2E tiers run as separate stages.
  A suite nobody runs locally catches defects one stage too late (P3).
- The order of gates within the pipeline and the non-test gates (lint, type, dependency) are
  defined in `knowledge/quality/review-method.md#gate-order`.
