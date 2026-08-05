# Test adequacy — Control-verification tests, judged as tests

Section of `knowledge/quality/test-adequacy.md`.


The security gate owns whether protective controls have tests and whether those tests assert
refusal (`knowledge/security/control-verification-tests.md`). The quality view judges the same
tests purely as tests, because a weak control test is worse than a missing one — it certifies
a protection that is not there. Apply the standard adequacy checks: assertions present and
asserting the deny outcome specifically (QUA-011), isolated and order-independent (QUA-013),
no retry masking (QUA-015), and mutation-worthy — disable the guarded control in a scratch
run and confirm the test fails (QUA-014, and the mutation tooling above where configured).
