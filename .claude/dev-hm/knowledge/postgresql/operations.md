# PostgreSQL operations: MVCC, vacuum, locks, config, pooling

Runtime health reference: how MVCC creates dead tuples, how to keep vacuum ahead of them, how to
resolve lock contention, and how to size memory, I/O, and connection pooling.

## MVCC and dead tuples

Every `UPDATE` and `DELETE` leaves the old row version in place as a **dead tuple**; `UPDATE` is an
insert-plus-mark-dead. Readers see the version valid for their snapshot (transaction isolation),
so no writer blocks a reader and no reader blocks a writer. Consequences to manage:

- Dead tuples accumulate until `VACUUM` reclaims them; unreclaimed dead tuples are **bloat** —
  wasted space that slows scans and inflates indexes.
- Every row carries `xmin`/`xmax` transaction ids. The 32-bit counter wraps; vacuum "freezes" old
  rows to prevent **transaction-id wraparound**, which would otherwise force a forced shutdown.
- A long-running transaction (or an abandoned one, or an unused replication slot) holds back the
  `xmin` horizon, so vacuum cannot remove tuples newer than it — bloat grows cluster-wide until it
  ends. Watch `pg_stat_activity` for old `xact_start` and `state = 'idle in transaction'`.

## Vacuum and autovacuum

Autovacuum runs `VACUUM` (reclaim dead tuples, update visibility map) and `ANALYZE` (refresh
statistics) automatically. It triggers per table when dead tuples exceed
`autovacuum_vacuum_threshold + autovacuum_vacuum_scale_factor * reltuples`.

| Symptom | Cause | Tune |
|---|---|---|
| Bloat on a large hot table | Scale factor (default 0.2 = 20%) too coarse at scale | Lower per-table: `ALTER TABLE t SET (autovacuum_vacuum_scale_factor = 0.02)` |
| Autovacuum "can't keep up" | Cost limit throttles it | Raise `autovacuum_vacuum_cost_limit`; increase `autovacuum_max_workers` |
| Index-only scans do heap fetches | Visibility map stale | More frequent vacuum; check `pg_stat_user_tables.n_dead_tup` |
| Wraparound warnings in log | Freezing behind | Investigate blockers; manual `VACUUM (FREEZE)`; check `age(datfrozenxid)` |
| Stats-driven bad plans after bulk load | `ANALYZE` hasn't run yet | Run `ANALYZE` explicitly post-load |

- Monitor `pg_stat_user_tables`: `n_dead_tup`, `n_live_tup`, `last_autovacuum`, `last_autoanalyze`.
- `VACUUM` reclaims space for reuse but does not return it to the OS. `VACUUM FULL` rewrites the
  table and does return space, but takes `ACCESS EXCLUSIVE` — use `pg_repack` for online rebuilds.
- Do not disable autovacuum to "save I/O"; that trades a small steady cost for an eventual outage.

## Locks and deadlocks

Postgres locks at table and row level. Reads take `ACCESS SHARE`; writes take `ROW EXCLUSIVE`;
most DDL takes `ACCESS EXCLUSIVE`. Row locks (`FOR UPDATE`, `FOR NO KEY UPDATE`) queue writers on
the same row.

Diagnose live contention:

```sql
-- who is blocking whom
SELECT blocked.pid AS blocked_pid, blocking.pid AS blocking_pid,
       blocked.query AS blocked_query, blocking.query AS blocking_query
FROM pg_stat_activity blocked
JOIN pg_stat_activity blocking ON blocking.pid = ANY(pg_blocking_pids(blocked.pid));
```

- `SELECT * FROM pg_locks WHERE NOT granted;` shows waiters. `log_lock_waits = on` with a
  `deadlock_timeout` logs long waits.
- **Deadlocks**: two transactions each hold a lock the other needs; Postgres detects the cycle
  after `deadlock_timeout` (default 1s) and aborts one with `40P01`. Fix by ordering writes
  consistently (always touch tables/rows in the same order), keeping transactions short, and
  taking the needed lock strength once rather than escalating mid-transaction.
- Set `lock_timeout` on application transactions so a blocked statement fails fast instead of
  stalling a request thread; set `idle_in_transaction_session_timeout` to kill abandoned
  transactions that pin the xmin horizon.
- `SELECT ... FOR UPDATE SKIP LOCKED` implements a work queue without contention.

## Configuration tuning

Start from workload and RAM; change one setting at a time and measure. Reload most with `SELECT
pg_reload_conf()`; `shared_buffers` needs a restart.

| Setting | Starting point | Notes |
|---|---|---|
| `shared_buffers` | ~25% of RAM | PG's own cache; the OS cache holds the rest |
| `effective_cache_size` | ~50–75% of RAM | Planner hint only (RAM the OS+PG cache can use); not allocated |
| `work_mem` | per-node, per-sort — start 16–64MB | Multiplied by concurrent sorts/hashes × connections; too high risks OOM |
| `maintenance_work_mem` | 256MB–1GB | Speeds index builds, vacuum |
| `max_connections` | keep modest (100–200) | Each backend is a process; pool rather than raise this |
| `random_page_cost` | 1.1 on SSD/NVMe (default 4) | Lower makes the planner prefer index scans on fast storage |
| `effective_io_concurrency` | higher on SSD | Prefetch depth for bitmap heap scans |
| `wal_compression`, `checkpoint_timeout`, `max_wal_size` | tune for write bursts | Spread checkpoints to avoid I/O spikes |

**Asynchronous I/O (18)**: `io_method` (`worker` default, `io_uring` on Linux) lets a backend issue
multiple reads concurrently, speeding sequential scans, bitmap heap scans, and vacuum — reported up
to ~3× read throughput. `io_combine_limit`/`io_max_combine_limit` tune coalescing; `pg_aios` shows
in-flight I/O. Prefer `io_uring` on modern Linux where the kernel supports it.

## Connection pooling

Each Postgres connection is an OS process with fixed memory overhead; thousands of direct
connections exhaust RAM and CPU. Put a pooler in front and keep `max_connections` modest.

| Pooler | Model | Choose when |
|---|---|---|
| PgBouncer | Single-threaded async; battle-tested | Default for most apps; add instances behind a LB to use more cores |
| PgCat | Multi-threaded (Rust/tokio); read/write split, sharding | Need to scale past one core, or want built-in replica routing |
| Odyssey | Multi-threaded | Similar niche to PgCat |

- **Pool modes**: *transaction* mode (connection returned at commit) is the right default for
  stateless web apps and gives the most multiplexing. *Session* mode is required if the app uses
  session state — prepared statements, `SET`/`LISTEN`, advisory locks, temp tables. *Statement*
  mode is rare and forbids multi-statement transactions.
- Size the pool to what the database can serve (start ~20–30 server connections for OLTP), not to
  peak client demand. Monitor `cl_waiting`/`avg_wait_time`; rising waiters means the pool or the DB
  is the bottleneck, not a reason to grow the pool unboundedly.
- Transaction-mode pooling breaks features that need a stable session: use `SET LOCAL` (not `SET`),
  and enable server-side prepared-statement support in the pooler or disable client prepares.

## Health checks to run first

- Slow statements: `pg_stat_statements` (`ORDER BY total_exec_time DESC`) — enable the extension.
- Cache hit ratio, index usage: `pg_stat_user_tables`, `pg_stat_user_indexes` (`idx_scan = 0`
  flags unused indexes — write cost with no benefit).
- Bloat and vacuum lag: `n_dead_tup`, `last_autovacuum`.
- Blocking: `pg_blocking_pids()` join above.
- Replication lag (if replicas): `pg_stat_replication` (`write_lag`, `replay_lag`).
