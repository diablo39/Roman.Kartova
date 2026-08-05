# Test data management — Fixtures and seeding

Section of `knowledge/quality/test-data-management.md`.


- Prefer builders over shared fixture dumps. A builder (or Object Mother / factory function)
  constructs a valid default entity and lets each test override only the fields it is about.
  Shared fixture files accrete fields for every test that ever touched them, and no reader can
  tell which values are load-bearing.
- The schema under test is produced by the real migration chain, never by a parallel
  create-script that drifts from it
  (`knowledge/quality/data-migration-safety.md#migration-tests`). Run
  migrations into the ephemeral engine (Testcontainers or the stack's equivalent, per
  `knowledge/quality/test-strategy.md`), then seed.
- Seed scripts are code: versioned with the schema they target, idempotent (re-runnable without
  duplicate rows), and reviewed in the same diff as the schema change they accompany.
- Each test creates and owns its data, and tears it down or rolls it back — the isolation rule
  QUA-013 is, in practice, mostly a data rule. Suites that share a pre-seeded database pass in
  one order and fail in another.
- Seeding must be environment-fenced: a seed or reset entry point that can run against a
  production connection string is a data-loss incident waiting for a mistyped variable. Guard it
  by environment check, and treat the guard as a protective control worth a test.
