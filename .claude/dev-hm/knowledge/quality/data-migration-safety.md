# Data and schema migration safety

Migrations are the highest-blast-radius change class we ship: a bad code deploy rolls back in
minutes, a bad migration can corrupt or destroy data that no redeploy brings back. This file
is the cross-engine policy for evolving schemas and data safely while the system serves
traffic — sequencing, reversibility, backfills, verification, constraints, and the tests that
prove a migration before it touches production. The quality oracle's migration entries point
here. Engine-specific mechanics (lock behavior, `NOT VALID` validation, concurrent index
builds) live in `knowledge/postgresql/schema-design.md#lock-safe-migrations`; other engines'
specifics belong in their stack files.

## Sections

Read the section you need, not the file. Each row is a separate file.

| Section | File |
|---|---|
| Expand-contract | `knowledge/quality/data-migration-safety/expand-contract.md` |
| Reversibility | `knowledge/quality/data-migration-safety/reversibility.md` |
| Backfills | `knowledge/quality/data-migration-safety/backfills.md` |
| Verification | `knowledge/quality/data-migration-safety/verification.md` |
| Constraints | `knowledge/quality/data-migration-safety/constraints.md` |
| Migration tests | `knowledge/quality/data-migration-safety/migration-tests.md` |
