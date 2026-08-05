# PostgreSQL replication, HA, backup, and upgrades

Continuity reference: streaming and logical replication, replication slots, failover and HA
tooling, PITR with pgBackRest, and major-version upgrade strategy. Everything here rides on WAL —
the write-ahead log is both the replication feed and the backup delta. Supported-major window and
ecosystem tool state are pinned in `knowledge/shared/versions.md`. Feature-floor markers like
"(17)" name the major release that introduced a capability.

## Sections

Read the section you need, not the file. Each row is a separate file.

| Section | File |
|---|---|
| Streaming (physical) replication | `knowledge/postgresql/replication-backup/streaming-physical-replication.md` |
| Logical replication | `knowledge/postgresql/replication-backup/logical-replication.md` |
| Failover and HA | `knowledge/postgresql/replication-backup/failover-and-ha.md` |
| PITR and backups | `knowledge/postgresql/replication-backup/pitr-and-backups.md` |
| pg_upgrade strategy | `knowledge/postgresql/replication-backup/pgupgrade-strategy.md` |
| Where the other files pick up | `knowledge/postgresql/replication-backup/where-the-other-files-pick-up.md` |
