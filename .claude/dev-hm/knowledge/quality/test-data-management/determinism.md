# Test data management — Determinism

Section of `knowledge/quality/test-data-management.md`.


Test data is a nondeterminism source alongside time and scheduling
(`knowledge/quality/test-strategy.md#flakiness-control`):

- Seed every generator and log the seed, so a failure replays exactly
  (`knowledge/quality/test-adequacy.md#two-run-determinism`).
- Generated values must respect the assertion: a random string that occasionally collides with
  a uniqueness constraint, or a random date that occasionally lands on a DST boundary, is a
  flake generator. Constrain generation ranges deliberately — and cover the boundary cases as
  explicit named tests (QUA-004), not as lottery tickets.
- Relative dates ("now minus 30 days") beat absolute dates that silently arrive; where an
  absolute date is required, pair it with a fixed test clock.
- Golden and snapshot files are fixtures too: a regenerated golden is an assertion only if a
  human read the new content (`knowledge/quality/test-adequacy.md`).
