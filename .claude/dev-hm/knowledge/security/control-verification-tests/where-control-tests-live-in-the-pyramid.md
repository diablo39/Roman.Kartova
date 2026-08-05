# Control-verification tests — Where control tests live in the pyramid

Section of `knowledge/security/control-verification-tests.md`.


Place each control test at the lowest level that can observe the refusal at the layer that
enforces it — test the enforcing layer, never a private copy of its logic:

- Encoding, parsing, validation, and limit checks are unit-level: pure calls in, refusal out.
- Authorization, authentication, and session controls are handler-level integration tests
  through the real middleware stack, since the control is the wiring as much as the check.
- Data-access parameterization runs against a real engine in an ephemeral container.
- Transport verification is an integration test against a local endpoint started by the fixture.
- End-to-end suites may additionally cross-check one critical journey, but a control whose only
  test is end-to-end is under-tested — the refusal belongs lower, where it runs on every build.

Control tests are ordinary members of the standard suite and run in the ordinary CI test gate;
the marker identifies them, it does not segregate them into a separate, skippable job.
