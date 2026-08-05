# PostgreSQL replication, HA, backup, and upgrades — Failover and HA

Section of `knowledge/postgresql/replication-backup.md`.


The default architecture for self-managed HA: an automated failover manager on a
distributed-consensus store, with Patroni the de facto standard (state pinned in
`knowledge/shared/versions.md`). Patroni keeps cluster state and leader lease in a DCS (etcd,
Consul, or Kubernetes API), demotes/promotes members, and fences the old leader so two primaries
cannot both accept writes. `pg_auto_failover` is a lighter two-plus-monitor design; managed cloud
offerings own this layer for you.

- Promotion mechanics underneath any manager: `pg_ctl promote` or `SELECT pg_promote()`; the new
  primary starts a new timeline; standbys follow with `recovery_target_timeline = 'latest'` (the
  default).
- Reattaching the old primary: `pg_rewind` resyncs it against the new primary without a full
  re-seed — it requires `wal_log_hints = on` or data checksums enabled beforehand, so decide that
  at cluster-init time, not during the incident.
- RPO decision: automated failover with async replication can lose the last commits. If the
  requirement is zero committed-transaction loss, pair the manager with quorum synchronous
  replication (above) and accept the latency cost.
- Split-brain is prevented by fencing plus a single source of truth for leadership (the DCS
  lease), never by "the VIP moved". Client routing follows the manager's health endpoints
  (HAProxy/pgbouncer checking Patroni's REST API, or cloud LB target health).

Legacy carve-out — only where it pre-exists and replacement cost is documented: manual
trigger-file promotion and script-driven failover stacks (including repmgr-style tooling without
consensus). These are dominated: no fencing and no leader lease means operator error or a network
partition can produce two writable primaries. Migration path is Patroni on the same replicas.
