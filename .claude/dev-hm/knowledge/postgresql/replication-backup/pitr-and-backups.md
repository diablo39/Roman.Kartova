# PostgreSQL replication, HA, backup, and upgrades — PITR and backups

Section of `knowledge/postgresql/replication-backup.md`.


Point-in-time recovery = a base backup + the continuous WAL archive replayed to a chosen point.
This is the recovery mechanism for "we dropped the table at 14:02" — restore the base backup,
then:

```
restore_command = '...'            # how to fetch archived WAL
recovery_target_time = '2026-07-14 14:01:00+00'   # or _lsn / _xid / _name
recovery_target_action = 'promote' # or 'pause' to inspect first
```

with `recovery.signal` present. Named restore points (`pg_create_restore_point`) before risky
changes make targets precise.

### Tooling

pgBackRest is the tier-1 choice for self-managed clusters (tool state pinned in
`knowledge/shared/versions.md`): parallel full/differential/incremental backups, block-level
incrementals, compression and client-side encryption, S3/Azure/GCS repositories, delta restore
(rewrites only changed files — fastest re-seed), backup from a standby, asynchronous
`archive-push`, retention policies, and a `verify` command. Point `archive_command` at
`pgbackrest archive-push` and both archiving and backups share one audited repository. Barman is
the main alternative; WAL-G is object-store-native and minimal. `pg_basebackup` is fine for
seeding a replica or backing up a small cluster, but has no incrementals, retention, or
verification. Managed-cloud platforms own this layer — use their PITR, and test it the same way.

Not a substitute: `pg_dump`/`pg_dumpall` are logical exports — good for seeding, migrations, and
archival of a schema snapshot, but they cannot replay to a point in time and restore at
data-volume speed, not WAL speed. Hand-rolled `archive_command` scripts (`cp` to an NFS mount)
are the dominated option: no verification, no retention, and silent failure — `pg_stat_archiver`
shows archiving broken only after WAL has already piled up. Only acceptable where already in
place; migrate to a managed repository tool.

### Backup discipline

- A backup that has not been restored is a hope, not a backup: schedule restore drills that
  recover to a scratch instance and validate application-level reads.
- Monitor `pg_stat_archiver` (`failed_count`, `last_failed_time`) and repository-side backup age;
  alert on archive lag, since PITR granularity is bounded by the newest archived segment.
- Define RPO (data-loss tolerance → archive/replication mode) and RTO (restore time → backup type
  mix and delta restore) explicitly; size full-vs-incremental cadence from them.
