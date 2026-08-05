# Test strategy — What to test at which level

Section of `knowledge/quality/test-strategy.md`.


- Unit (component): every new public function or business-logic branch (QUA-003) — outputs,
  effects, and error paths, with collaborators replaced only at architectural boundaries.
  Assert observable behavior, not implementation details: a test that breaks on a rename
  without a behavior change is testing the wrong thing. Zero assertion-free or tautological
  tests (QUA-011).
- Integration (component integration): adapters against real technology — repositories against
  a real database, consumers against a real broker, HTTP clients against a stub server. Use
  Testcontainers (or the stack's equivalent) for real engines in ephemeral containers instead
  of in-memory fakes whose SQL/semantics differ from production; per-stack setup lives in
  `knowledge/<stack>/testing.md`.
- Contract: where two services evolve independently, pin the wire contract (schema tests,
  consumer-driven contracts) so integration breakage surfaces before deployment.
- End-to-end (system/acceptance): the handful of user journeys whose failure is an incident —
  login, checkout, the primary happy path. Everything else belongs lower.
- Regression with fix (QUA-005): write the test first against the broken code, observe it fail,
  fix, observe it pass; record both observations in the handoff.
- Refactor parity (QUA-006): a refactor is validated by the pre-existing suite passing
  unmodified. Needing to change assertions during "a refactor" means the change is not a
  refactor — retitle it and test the new behavior.
