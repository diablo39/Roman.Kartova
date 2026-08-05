# Data and schema migration safety — Verification

Section of `knowledge/quality/data-migration-safety.md`.


A migration is believed when it is measured. Data-transforming migrations record checks run
before and after, with results in the handoff — the point is a number someone can compare,
not an assurance:

- Row counts: source versus target, broken down per partition or per tenant on large sets so
  an offsetting pair of errors cannot hide in a global total.
- Checksums and aggregates: sums, min/max, or hashes over migrated columns (normalized
  first — encoding and null handling differ across representations); on very large tables,
  computed over deterministic samples.
- Invariant queries: zero rows violating the end-state invariant — remaining nulls in the
  backfilled column, orphaned references, dual-written pairs that disagree. Keep these
  queries in the repository next to the migration so they are re-runnable, not pasted once
  into a terminal.
- Dual-read comparison, the strongest signal: during the transition window, read both
  representations, compare, and log mismatches. Switching reads only after a period of zero
  mismatches converts "the backfill should be complete" into "the backfill is observed
  complete".
