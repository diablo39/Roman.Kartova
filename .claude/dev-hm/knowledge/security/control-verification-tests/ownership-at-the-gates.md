# Control-verification tests — Ownership at the gates

Section of `knowledge/security/control-verification-tests.md`.


The two gates look at the same tests through different lenses, with no double ownership:

- The security gate verifies the security semantics: the criterion-to-test map is complete,
  each test asserts the refusal, the fails-when-removed spot-check is recorded, and no existing
  control test was silently removed or weakened.
- The quality gate judges the same tests as tests: assertions present (QUA-011), isolation
  (QUA-013), no retry masking (QUA-015), mutation-worthiness (QUA-014).

A control without a test is an unverified control: the gate reports it and never passes it on
inspection alone.
