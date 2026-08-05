# Data and schema migration safety — Backfills

Section of `knowledge/quality/data-migration-safety.md`.


A backfill writes moved or derived data into new structure while the system serves traffic.
Three properties, all visible in the code, are what the gate looks for:

- Idempotent. Re-running after a crash or a partial run causes no double effects: guard
  predicates (`where new_column is null`), deterministic transforms, upsert semantics.
  Restart-from-zero must be safe even when restart-from-checkpoint is available.
- Batched and rate-limited. Small batches — commonly in the 1,000–10,000 row range, tuned to
  the table — each in its own transaction, with a pause between batches. One giant
  transaction holds locks for the duration, bloats the log/undo space, and turns a late
  failure into a total redo. Where replication exists, throttle on measured lag: pause when
  replicas fall behind a threshold rather than degrading every read.
- Progress recorded and resumable. Checkpoint the last processed key (iterate in indexed-key
  order, not offset pagination) so an interrupted run resumes where it stopped, and an
  operator can see how far along it is.

Operational rules around those properties: the backfill runs as a job or operational script,
never inside the schema-migration transaction; dual-write is live before the backfill starts,
otherwise rows written mid-backfill are silently missed; and the backfill logs batch
progress, rate, and error counts so a stuck run is visible before it is a mystery.
