# Control-verification tests — Durability

Section of `knowledge/security/control-verification-tests.md`.


Control tests are the durable protection layer, so removing or weakening one is a gated action:
the diff carries a rationale, and the security gate approves it, recorded in the handoff. Zero
silent removals. Weakening includes:

- broadening accepted outcomes (a 403 assertion becoming "any 4xx", an exception-type
  assertion becoming a bare "raises");
- deleting the side-effect absence assertion while keeping the status assertion;
- converting a boundary-level refusal test into a unit test of a stub of the enforcing layer;
- skipping, quarantining, or adding retries around a control test (retry masking, QUA-015);
- narrowing the inputs so the refused case is no longer exercised.

Legitimate maintenance is not weakening: refactoring test internals with assertions preserved,
renaming with the marker kept, moving a test file wholesale. When a control itself is removed
deliberately (a feature is retired), its tests go with it — with the removal stated in the
handoff so the gate sees an intentional retirement, not an erosion.
