# PostgreSQL extensions: pgvector, PostGIS, pg_partman, pg_stat_statements

When to reach for the major extensions and how to use each well. Current extension state (which
lines are current, what ships where) is pinned in `knowledge/shared/versions.md`; this file states
mechanics and decision criteria only.

## Extension hygiene

- `CREATE EXTENSION name;` installs into the current database; the binary package must already be
  on the server (managed clouds ship an allowlist — check `pg_available_extensions` for what the
  platform offers and at which version).
- After a package upgrade run `ALTER EXTENSION name UPDATE;` per database — the SQL-level objects
  do not upgrade themselves. After a major-version `pg_upgrade`, do this for every extension.
- Extensions that hook the server (`pg_stat_statements`, `pg_partman`'s background worker,
  TimescaleDB) must be listed in `shared_preload_libraries`, which needs a restart — plan it.
- Trusted extensions can be created by non-superusers with `CREATE` on the database; everything
  else needs a superuser or the cloud platform's grant mechanism.
- Treat extension DDL as migration code: versioned, reviewed, reversible like any schema change.

## When to reach for each

| Need | Extension | Not the right tool when |
|---|---|---|
| "Which queries cost the most?" | pg_stat_statements | You need per-execution traces (use `auto_explain`) |
| Similarity search over embeddings | pgvector | Exact top-k over small data — a plain scan with the distance operator is simpler and exact |
| Geospatial types, indexes, predicates | PostGIS | One "points within radius" feature — see the carve-out below |
| Automated partition lifecycle | pg_partman | A handful of static partitions you can manage by hand (`schema-design.md`) |
| Time-series compression/aggregation at scale | TimescaleDB | Ordinary time-indexed tables that declarative partitioning already serves |

## pg_stat_statements

The first observability extension to enable on any production cluster: normalized per-statement
execution statistics.

- Enable via `shared_preload_libraries`, then `CREATE EXTENSION pg_stat_statements;`.
- Start every performance investigation here: top by `total_exec_time` (aggregate cost), by
  `mean_exec_time` (slow individually), and by `calls` (chatty). `rows`, `shared_blks_read` vs
  `shared_blks_hit`, and `temp_blks_written` separate I/O-bound from cache-served from
  spilling-to-disk.
- Queries are normalized (constants stripped) and keyed by `queryid` — correlate with
  `EXPLAIN ANALYZE` on representative parameters (`query-optimization.md`), and with
  `compute_query_id` the same id appears in `pg_stat_activity` and logs.
- `pg_stat_statements_reset()` after a fix gives a clean before/after window; snapshot the view
  periodically if you need history, the extension keeps only cumulative counters.
- Caveats: bounded entry count (`pg_stat_statements.max`) evicts rare queries; distinct schemas
  or search_paths produce distinct entries for the same text.

## pgvector

Vector similarity search in the database: `vector` (single-precision), `halfvec` (half-precision,
half the storage), `sparsevec` (sparse), and `bit` types, with distance operators — `<->` (L2),
`<#>` (negative inner product), `<=>` (cosine distance), and Hamming/Jaccard for `bit`.

- Exact search needs no index: `ORDER BY embedding <=> $1 LIMIT k` scans and is 100%-recall
  correct — the right answer for small tables and for validating index recall.
- Approximate indexes trade recall for speed. HNSW is the default choice: better recall/latency,
  no training step, works on an empty table, incremental inserts are fine. Build cost is the
  price — parallel builds and a large `maintenance_work_mem` matter on big tables.

| | HNSW (default) | IVFFlat (carve-out) |
|---|---|---|
| Build | Slower, memory-hungry | Fast, needs data present first (trains centroids) |
| Query | Better recall at speed; tune `hnsw.ef_search` | Tune `lists` at build, `ivfflat.probes` at query |
| Data churn | Handles inserts/updates well | Recall degrades as data drifts from trained centroids; periodic reindex |
| Choose when | New work, evolving data | Bulk-load-then-query datasets where build time or memory is the binding constraint |

- Create: `CREATE INDEX ON items USING hnsw (embedding vector_cosine_ops);` — the opclass must
  match the query operator or the index is ignored. Tuning: `m` and `ef_construction` at build
  (recall ceiling), `hnsw.ef_search` per session (recall/latency dial).
- Filtered search (`WHERE tenant_id = ...` plus nearest-neighbor) is the classic trap: the index
  returns k candidates before the filter, so selective filters starve results. Iterative index
  scans (`hnsw.iterative_scan = strict_order | relaxed_order`) keep scanning until the filter is
  satisfied; alternatives are partial indexes per hot filter value or partitioning by the filter
  key so each partition's index is filter-local.
- Indexed vector dimensions are capped (2000 for `vector`; higher for `halfvec`) — reduce
  dimensionality or switch types for larger embeddings. `halfvec` indexes cut memory roughly in
  half with minimal recall loss and are worth testing first at scale.
- Measure recall before shipping: run the exact scan on a sample, compare the approximate top-k,
  and record the achieved recall next to the chosen parameters.

## PostGIS

Full geospatial support: spatial types, GiST/SP-GiST indexes, and hundreds of predicates and
transforms.

- `geometry` computes on a flat plane in a chosen SRID — fast, correct for projected/local data.
  `geography` computes on the spheroid in meters — correct for global lat/lon distances at extra
  CPU cost. Default to `geography` for global point data in lat/lon, `geometry` with a suitable
  projection for regional datasets and heavy geometric processing.
- Index with GiST: `CREATE INDEX ON places USING gist (geom);`. Index-assisted predicates:
  `ST_DWithin`, `ST_Intersects`, `ST_Contains`, and the `&&` bounding-box operator.
- The classic anti-pattern: `WHERE ST_Distance(geom, $1) < r` computes distance for every row and
  cannot use the index — write `ST_DWithin(geom, $1, r)` instead. Same result, index-driven.
- Keep SRIDs consistent (type modifiers like `geometry(Point, 4326)` enforce it); transform with
  `ST_Transform` deliberately, not implicitly.
- Carve-out for the trivial case: a single "points within radius" feature on modest data can use
  `geography` types only, or even a bounding-box prefilter on plain numeric columns — adopt full
  PostGIS when real spatial predicates, joins, or datasets arrive. The `earthdistance`/`cube`
  approach is legacy: only where it already exists; PostGIS `geography` is the migration path.

## pg_partman

Automation on top of declarative partitioning (`schema-design.md`): pre-creates future
partitions, detaches/drops expired ones on a retention policy, and manages a template table for
per-partition settings.

- Reach for it when partitions follow a schedule (time-based ranges) — the failure it prevents is
  inserts hitting a missing future partition at 02:00.
- Run `run_maintenance()` from the bundled background worker (`shared_preload_libraries`) or
  `pg_cron`; monitor that maintenance actually ran — a stalled maintenance job recreates the
  missing-partition failure it was installed to prevent.
- Retention (`retention` + `retention_keep_table`) turns "delete old data" into a partition
  detach/drop — no bloat, no long DELETE. Detach uses `CONCURRENTLY` semantics per
  `schema-design.md`; verify with `\d+ parent` that the expected partition set exists.

## Where the other files pick up

- Partitioning design and lock-safe DDL under pg_partman: `schema-design.md`
- Reading the plans pg_stat_statements points you at: `query-optimization.md`
- shared_preload_libraries restarts, autovacuum interplay on large index builds: `operations.md`
- Extension updates during major upgrades: `replication-backup.md`
