# Data and schema migration safety — Reversibility

Section of `knowledge/quality/data-migration-safety.md`.


Every migration carries a way back, decided before it runs — not improvised during the
incident that needs it:

- Expand steps are naturally reversible: the new structure is unused until reads switch, so
  the down path is "drop what was added". Write that down migration anyway; generated stubs
  that were never run do not count.
- Data-transforming steps are reversible while both representations exist — one more reason
  dual-write windows stay open through the observation period.
- Contract steps are the genuinely irreversible ones: a dropped column's data is gone. That
  is acceptable exactly because the sequence made the data redundant first; the handoff
  records the irreversibility and points at the verification that proved redundancy.
- Where a true down migration is impossible (destructive transform, merged records), the
  rollback path is a restore procedure: which backup or snapshot, restored how, with how much
  data loss, taking how long. A recorded irreversibility justification plus a restore
  procedure satisfies the gate; silence does not.
- Roll-forward ("we fix forward, we never roll back") is a legitimate stance for specific
  failure classes, but it is a per-migration recorded decision with the fix path stated — not
  a standing excuse for skipping down paths.

Release-level rollback planning consumes this: a release is only as reversible as its least
reversible migration.
