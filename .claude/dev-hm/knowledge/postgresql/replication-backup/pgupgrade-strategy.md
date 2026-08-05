# PostgreSQL replication, HA, backup, and upgrades — pg_upgrade strategy

Section of `knowledge/postgresql/replication-backup.md`.


Minor releases: swap binaries and restart — always apply, no data format change. Major releases
need one of:

| Method | Downtime | Notes |
|---|---|---|
| `pg_upgrade --link` | Minutes | Hard links; old cluster unusable once the new one starts |
| `pg_upgrade --clone` | Minutes | Reflink copies on XFS/Btrfs/APFS; old cluster stays intact |
| `pg_upgrade --copy` | Hours at scale | Full copy; safest rollback, slowest |
| `pg_upgrade --swap` (18) | Fastest | Moves directories into place; least rollback, fastest cutover |
| Logical-replication blue-green | Near zero | New-major subscriber, sync, cut over; needs the logical caveats above |

Method choice: `--link` or `--clone` for a planned window; `--swap` when the window is tightest
and a verified backup covers rollback; blue-green via logical replication when downtime must be
seconds — build the new-major cluster as a subscriber (`pg_createsubscriber` from a standby),
let it catch up, quiesce writes, resync sequences, switch the application, and keep a reverse
subscription if you need a rollback path.

Checklist around the mechanics:

1. `pg_upgrade --check` first; it validates without changing anything. Install the new major's
   binaries and matching extension packages before the window.
2. Take a verified backup immediately before; note that after `--link`/`--swap` cutover, rollback
   is restore-from-backup, not "start the old cluster".
3. Statistics: pg_upgrade preserves planner statistics (18) — extended statistics still need
   rebuilding; run `vacuumdb --all --analyze-only --missing-stats-only` to fill gaps. On older
   floors, run a full `--analyze-only` pass before opening traffic, or plans will be terrible.
4. Standbys do not survive a major upgrade — re-seed them (pgBackRest delta restore) after the
   primary is up.
5. Post-upgrade: `ALTER EXTENSION ... UPDATE` for each extension, re-run application smoke
   queries, and re-enable the failover manager only when replicas are back in sync.
6. Logical slots: on 17+ the upgrade preserves logical slots and subscription state; on older
   floors plan to recreate slots and resynchronize subscribers.
