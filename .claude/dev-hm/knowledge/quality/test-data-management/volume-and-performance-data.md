# Test data management — Volume and performance data

Section of `knowledge/quality/test-data-management.md`.


- Performance and capacity checks need production-shaped volume, not production content:
  generate to the measured cardinalities, skew, and growth curve
  (`knowledge/quality/performance-capacity.md`). A query plan tuned on
  a hundred uniform synthetic rows says nothing about a hundred million skewed ones.
- Migration and backfill rehearsals follow the same rule: rehearse on synthetic data at
  production scale before touching production
  (`knowledge/quality/data-migration-safety.md#backfills`).
- Keep bulk generation deterministic (seeded) and cheap to re-create; a multi-gigabyte binary
  fixture committed to the repository is a build-time tax and a drift magnet — commit the
  generator and its seed instead.
