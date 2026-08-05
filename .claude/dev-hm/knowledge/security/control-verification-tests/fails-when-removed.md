# Control-verification tests — Fails when removed

Section of `knowledge/security/control-verification-tests.md`.


A control test is meaningful when disabling the control makes it fail. This is the definition
to design against, and the property the security gate spot-checks.

Spot-check procedure — a scratch-run experiment, never committed:

1. In a scratch working copy, disable the control the diff touches: invert the guard, detach
   the middleware, switch the verification setting off, or swap the bound parameter for
   concatenation.
2. Run the mapped control tests. At least one must fail.
3. Restore the working copy. Record the location, what was disabled, which test failed, and
   the failing test's output line in the handoff (see the map format above). The output line is
   what lets the gate verify the failure happened rather than trusting that it did; for
   controls mapped to S0 entries the security gate additionally reproduces one spot-check.

One spot-check per protective-control family the diff touches is sufficient; the technique is
the same as the QUA-014 mutation spot-check, aimed at controls instead of logic branches. A
spot-check that produces no failing test means the tests assert something other than the
control — a mock, a copy of the logic, or a path the control does not guard — and the mapping
is not satisfied regardless of coverage numbers.
