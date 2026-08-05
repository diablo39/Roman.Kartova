# PostgreSQL replication, HA, backup, and upgrades — Streaming (physical) replication

Section of `knowledge/postgresql/replication-backup.md`.


A standby replays the primary's WAL byte-for-byte: the whole cluster, all databases, exact copy.
`wal_level = replica` (the default) suffices; hot standby lets the replica serve read-only queries
while replaying.

Setup essentials:

- Seed the standby with `pg_basebackup -R` (writes `standby.signal` and `primary_conninfo`) or a
  pgBackRest restore (preferred at scale — it offloads the copy from the primary).
- Create a physical replication slot and set `primary_slot_name` so the primary retains WAL the
  standby still needs across standby restarts.
- Cascading replication (a standby feeding further standbys) offloads fan-out from the primary.

### Replication slots and their retention risk

A slot pins WAL (and, for logical slots, the xmin horizon) until its consumer catches up. An
inactive slot therefore fills the primary's disk and blocks vacuum cluster-wide — the classic
self-inflicted outage.

- Set `max_slot_wal_keep_size` so a dead slot invalidates instead of filling the disk (the default
  is unbounded retention). An invalidated slot means re-seeding that standby — the survivable
  failure mode.
- Monitor `pg_replication_slots`: `active`, `wal_status` (`reserved`/`extended`/`unreserved`/
  `lost`), `safe_wal_size`. Alert on inactive slots, not just lag.
- Drop slots for decommissioned consumers immediately; they do not clean themselves up.

### Synchronous vs asynchronous

Asynchronous is the default: commits return without waiting for the standby, so a failover can
lose the last transactions (RPO > 0). Synchronous replication trades commit latency for
durability. `synchronous_commit` levels, weakest to strongest:

| Level | Waits for | Guarantee on failover |
|---|---|---|
| `off` | Nothing (not even local flush) | Can lose recent commits even without failover |
| `local` | Local WAL flush only | Standard single-node durability, async replicas |
| `remote_write` | Standby received + wrote (not fsynced) | Loses data only if primary and standby fail together |
| `on` | Standby flushed WAL to disk | No committed-transaction loss on single failure |
| `remote_apply` | Standby replayed (visible to reads) | Read-your-writes on the standby |

`synchronous_standby_names` picks who must confirm: `FIRST 1 (s1, s2)` (priority order) or
`ANY 1 (s1, s2)` (quorum — better availability, same guarantee). With exactly one synchronous
standby, that standby's outage blocks all commits — use a quorum of two or more candidates, and
know the emergency escape (clear `synchronous_standby_names` and reload) before you need it.
`synchronous_commit` is settable per transaction — reserve `remote_apply` for the writes that need
it rather than paying the round trip everywhere.

### Monitoring and standby behaviour

- Primary: `pg_stat_replication` — `write_lag`, `flush_lag`, `replay_lag` per standby;
  `sent_lsn` vs `pg_current_wal_lsn()` for byte lag.
- Standby: `pg_stat_wal_receiver` (connection state), `pg_last_wal_replay_lsn()`,
  `pg_is_in_recovery()`.
- Replication conflicts: replay can cancel standby queries (vacuum removed rows a query needs).
  `max_standby_streaming_delay` sets how long replay waits; `hot_standby_feedback = on` makes the
  standby report its xmin to the primary so vacuum spares those rows — at the cost of primary
  bloat driven by long standby queries. Pick per workload: reporting replicas usually want
  feedback on and a bounded delay.
