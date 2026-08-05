# Test adequacy — Coverage is not adequacy

Section of `knowledge/quality/test-adequacy.md`.


Line coverage measures execution, not verification. A test that calls a function and asserts
nothing produces the same coverage as one that pins every output — and catches nothing. Two
ISTQB CTFL 4.0 principles frame the gap: testing shows the presence of defects, not their
absence (P1), so a green suite is only as strong as its assertions; and tests wear out (P5),
so a suite that was adequate when written decays as the code around it changes. The coverage
floor (QUA-010) stays as the entry ticket; adequacy is judged on top of it, never inferred
from it.

Weak-adequacy signals visible in review, beyond the assertion-free tests QUA-011 already
fails:

- Assertions on mock interactions where an observable outcome exists to assert instead — the
  test verifies that the code calls what it calls, which survives any behavior change.
- One happy-path assertion on a function with several branches; the other branches are covered
  by execution but verified by nothing.
- Snapshot or golden-file assertions that were regenerated and approved without being read — a
  snapshot is an assertion only if a human validated its content once.
- Error-path tests that assert "an error is raised" without asserting which one; the wrong
  failure passes.
- Tests that keep passing when the tested function returns early — the setup exercises the
  code, the asserts only touch the setup.

Generated tests deserve extra suspicion here: suites written by tooling or AI assistance
commonly execute much and assert little, and stay perpetually green precisely because nothing
in them pins behavior. Adequacy checks are the counterweight.
