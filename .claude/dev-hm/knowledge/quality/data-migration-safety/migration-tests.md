# Data and schema migration safety — Migration tests

Section of `knowledge/quality/data-migration-safety.md`.


Migrations are code, and untested migrations get the same verdict as untested business
logic. What a migration test suite exercises, in CI, against a real engine in an ephemeral
container (in-memory fakes diverge exactly where migrations hurt):

- Every new migration applies cleanly on top of the previous released schema — not just on
  an empty database built from scratch.
- Transforms run against representative seeded data, including the edge rows that break
  transforms in production: nulls, maximum lengths, duplicate candidates, non-ASCII and
  control characters, rows mid-way through any previous migration's state.
- Claimed down paths are executed: up, verify, down, verify the prior state is actually
  restored. A down migration that has never run is documentation, not a rollback path.
- Backfill idempotence is tested directly: run the backfill twice against the same fixture
  and assert identical end state.
- Expand-contract compatibility gets a check: while the sequence is mid-flight, the previous
  application version's critical queries still succeed against the expanded schema — as a
  test where the harness allows it, or minimally as schema assertions that the old structure
  survives until the contract phase.

The verification queries from the section above double as test assertions: the invariants a
migration promises are the invariants its tests check.
