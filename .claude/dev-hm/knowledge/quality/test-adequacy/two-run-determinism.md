# Test adequacy — Two-run determinism

Section of `knowledge/quality/test-adequacy.md`.


Every adequacy verdict in this file rests on check output — suite results, coverage numbers,
mutation scores, contract verifications. Those checks are trustworthy only if they are
deterministic: the same input produces the same verdict, every run. So the checks themselves
carry an adequacy requirement — run twice, agree twice:

- A new or changed suite records a two-run before handoff: two consecutive runs on the same
  code, the second shuffled where the framework supports order shuffling, with identical
  verdicts. A check that flips between runs is a defect in the check
  (`knowledge/quality/test-strategy.md` flakiness rules apply; QUA-012/QUA-013/QUA-015 name
  the usual causes).
- Reported numbers are reproducible: the coverage value cited in a handoff comes out the same
  when the run is repeated; randomized inputs log their seeds so a failure replays exactly.
- Mutation and contract runs are checks too: pin their configuration in the repository so a
  recorded score or verification result means the same thing when re-run at the gate.

This is what lets the gates verify evidence instead of re-deriving it: a deterministic check,
run twice with the same answer, is evidence; anything else is an anecdote.
