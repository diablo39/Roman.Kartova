# Test adequacy — Assertion strength

Section of `knowledge/quality/test-adequacy.md`.


The review discipline that keeps adequacy up between mutation runs. For each new or changed
test, ask what would have to break for this test to fail:

- Assert observable outcomes — return values, state changes, emitted events, raised errors —
  not implementation details. Interaction assertions (mock received call X) are appropriate
  only when the interaction is the contract (a notification must be sent); otherwise they pin
  the implementation and miss the behavior.
- Prefer exact expected values over shape checks where the value is deterministic; assert the
  specific error type and the meaningful part of its message, not just that something threw.
- Over-mocked tests verify the mocks: when every collaborator is replaced, the test proves
  the code calls its stubs in order — replace collaborators only at architectural boundaries
  (`knowledge/quality/test-strategy.md`).
- Each test states one behavior; a test asserting twelve things fails with a diagnosis cost,
  and a test asserting zero things is QUA-011's business.
