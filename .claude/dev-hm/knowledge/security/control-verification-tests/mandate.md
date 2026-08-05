# Control-verification tests — Mandate

Section of `knowledge/security/control-verification-tests.md`.


A change that implements or alters a protective control ships, in the same diff, at least one
automated test asserting the control's protective behavior. In scope:

- every S0/S1 security acceptance criterion in the work package, and
- every protective control the diff adds or alters that maps to an S0/S1 security-oracle entry.

The phrasing to hold every such test to: our code does X; this test asserts X holds. A control
verified only by review is verified only until the next diff. A control verified by a test
converts a gate-time assurance ("the control was present when reviewed") into a regression-time
assurance ("a change that weakens the control fails the build").

What counts as a control test:

- a behavior test of our own code, exercised through the layer that enforces the control — the
  real middleware stack, the real repository API, the real client factory;
- asserting the protective outcome observably (see the next section), in the standard test
  suite, runnable by the ordinary test gate.

What does not count:

- allow-path coverage alone — the feature working says nothing about the control holding;
- tests that replace the enforcing layer with a stub and then assert the stub;
- configuration snapshot checks with no exercised behavior — asserting a flag is set proves
  the flag exists, not that the control acts; acceptable only as a supplement.

### Criterion-to-test traceability

The handoff maps each in-scope criterion to test identifiers — file plus test name — so gates
verify coverage mechanically instead of hunting:

```
Control-test map
AC-3  only admins can disable users      -> tests/api/test_users.py::test_disable_refuses_reader_role
AC-3                                     -> tests/api/test_users.py::test_disable_refuses_missing_token
AC-7  exports mask personal identifiers  -> tests/export/test_masking.py::test_export_masks_email
Spot-check: authorization guard inverted in a scratch run -> test_disable_refuses_reader_role failed
```

Zero unmapped criteria. A criterion that genuinely cannot be asserted by an automated test is
declared in the handoff with the reason and the manual verification performed; the security
gate adjudicates whether that is acceptable. The map is checked against the recorded test-run
output attached to the handoff: each mapped test ID appears in that output, which is what
proves the mapped tests actually ran rather than merely existing.
