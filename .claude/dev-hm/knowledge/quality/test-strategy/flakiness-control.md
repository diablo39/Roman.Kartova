# Test strategy — Flakiness control

Section of `knowledge/quality/test-strategy.md`.


A flaky test is a defect in the test system: it erodes trust in the only signal the gates rely
on (QUA-002). Determinism rules for new tests:

- No fixed sleeps for synchronization (QUA-012) — await completion signals, poll a condition
  with a timeout, or inject a fake clock. A sleep is a race with the scheduler that you
  sometimes lose.
- Isolation (QUA-013): each test creates and tears down its own state; suites pass alone, in
  any order, and in parallel. Reset shared fixtures between tests (transaction rollback,
  container-per-suite, fresh temp dirs). Record an isolation run (single test + shuffled order)
  for new suites.
- Control the nondeterminism sources: time (inject clocks), randomness (seed and log the seed),
  network (stub external services; real engines via Testcontainers are fine — they are local
  and disposable), filesystem (unique temp dirs), ports (ephemeral allocation).
- No retry masking (QUA-015): retry annotations, rerun-on-failure flags, and raised retry
  counts convert a visible defect into an invisible one. A flaky test is fixed, or quarantined
  with a tracked reason and owner — never retried into green silence.
- When a test flakes in CI but not locally, capture the failure artifacts (logs, seed, timing)
  before rerunning; the rerun destroys the evidence.
