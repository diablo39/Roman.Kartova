# PostgreSQL query optimization

Plan-reading and indexing reference for diagnosing slow queries. Measure with `EXPLAIN
(ANALYZE, BUFFERS)` before and after every change; never optimize from the query text alone.

## Version baseline

Feature floors below are tagged with the release that introduced them; the current major and the
supported-release window live in `knowledge/shared/versions.md`. Optimizer-relevant additions in
PostgreSQL 18: B-tree **skip scan** (a multicolumn index can serve queries that omit a leading
column), and `EXPLAIN ANALYZE` reports **BUFFERS by default**. Everything below applies to every
supported release unless a feature is tagged with a version.

## EXPLAIN workflow

| Command | Runs query? | Use for |
|---|---|---|
| `EXPLAIN` | no | Estimated plan and row estimates only |
| `EXPLAIN (ANALYZE)` | yes | Actual rows, loops, and timing per node |
| `EXPLAIN (ANALYZE, BUFFERS)` | yes | Adds shared/local/temp block hits, reads, writes |
| `EXPLAIN (ANALYZE, SETTINGS)` | yes | Shows non-default planner GUCs affecting the plan |
| `EXPLAIN (ANALYZE, WAL)` | yes | WAL volume for write statements |
| `EXPLAIN (ANALYZE, FORMAT JSON)` | yes | Machine-readable; feed to a plan visualizer |

`ANALYZE` executes the statement — wrap `INSERT`/`UPDATE`/`DELETE` in a transaction and roll back,
or use `GENERIC_PLAN` (16) to see a parameterized plan without executing.

### Reading a plan

- Read inner-to-outer, bottom-up. Each node reports `cost=startup..total rows=est width=bytes`;
  with `ANALYZE` it adds `actual time=first..last rows=N loops=L`.
- Multiply per-node cost by `loops`: a node showing `actual time=0.1..0.2 rows=1 loops=90000` cost
  0.2ms each but 18s total. Loops usually come from a nested loop's outer side.
- The dominant node is where startup-to-total time jumps most, not the top node.

### Red flags

| Symptom in plan | Likely cause | First move |
|---|---|---|
| `Seq Scan` on large table with selective filter | Missing/unused index, or stats stale | Add matching index; `ANALYZE` the table |
| `actual rows` ≫ or ≪ `estimated rows` (10x+) | Stale or insufficient statistics | `ANALYZE`; raise `default_statistics_target`; extended stats |
| `Nested Loop` with high `loops` on a large inner scan | Bad row estimate favored nested loop | Fix estimate; a hash/merge join may be correct |
| `Sort Method: external merge Disk: NNkB` | `work_mem` too small for the sort/hash | Raise `work_mem` for the session or add an ordered index |
| `Rows Removed by Filter: large` | Index not selective / filter not indexable | Partial or expression index; rewrite predicate |
| `Heap Fetches: large` on Index Only Scan | Table not vacuumed; visibility map cold | `VACUUM`; see operations.md |
| `Bitmap Heap Scan` re-check discards most rows | Lossy bitmap or non-selective index | Consider a more selective composite index |

## Index selection

Index the columns in `WHERE`, `JOIN`, and `ORDER BY` that filter or sort the most rows. An index
that the planner never chooses is write overhead with no benefit — verify use with `EXPLAIN`.

| Type | Best for | Notes |
|---|---|---|
| B-tree (default) | Equality and range on scalars, sorting, uniqueness | Only index that satisfies `ORDER BY`; supports skip scan (18) |
| GIN | Multi-value columns: `jsonb`, arrays, full-text `tsvector`, `pg_trgm` | Slower writes; `fastupdate` batches; use for containment `@>`, `?` |
| GiST | Ranges, geometry, nearest-neighbor (`<->`), exclusion constraints | Lossy; balanced read/write |
| BRIN | Very large, naturally-ordered tables (append-only, time series) | Tiny index; effective only when physical order tracks the column |
| Hash | Equality only, large values | Rarely beats B-tree; no range or sort support |
| SP-GiST | Non-balanced structures: quadtrees, tries, IP prefixes | Niche |

### Composite and specialized indexes

- **Column order** in a composite B-tree: leading columns must match the query's equality
  predicates; put the equality columns first, the range/sort column last. `(a, b)` serves
  `WHERE a = ? AND b = ?` and `WHERE a = ?`, not `WHERE b = ?` (before skip scan).
- **Covering** (`INCLUDE`): add non-key columns so the scan is index-only —
  `CREATE INDEX ON orders (customer_id) INCLUDE (status, total)`.
- **Partial**: index a subset to shrink it and target hot predicates —
  `CREATE INDEX ON jobs (created_at) WHERE state = 'pending'`.
- **Expression**: index the exact expression the query uses —
  `CREATE INDEX ON users (lower(email))` for `WHERE lower(email) = ?`.
- Build on live tables with `CREATE INDEX CONCURRENTLY` (no `ACCESS EXCLUSIVE` lock); it is slower
  and cannot run inside a transaction block. See schema-design.md for migration safety.

## Join strategies

| Strategy | Planner picks when | Failure mode |
|---|---|---|
| Nested Loop | Outer side small, inner has an index on the join key | Explodes when outer rows are underestimated |
| Hash Join | Both sides large, no useful ordering, hash fits `work_mem` | Spills to disk (batches) when `work_mem` too small |
| Merge Join | Both inputs already sorted on the join key | Adds a sort if inputs aren't ordered |

Wrong join type almost always traces back to a wrong row estimate. Correct the statistics before
forcing behavior; use `enable_*` GUCs only to diagnose, never in production code.

## Query rewrites

- Select only needed columns; `SELECT *` blocks index-only scans and moves dead weight. Production
  read paths name their columns.
- Prefer `EXISTS`/`NOT EXISTS` over `IN`/`NOT IN` with subqueries; `NOT IN` also mishandles `NULL`.
- `LATERAL` joins let a subquery reference the outer row — good for top-N-per-group with an index.
- CTEs (`WITH`) are inlined and optimized across the boundary since PG12 unless marked
  `MATERIALIZED` or referenced more than once; add `MATERIALIZED` only to force a barrier
  deliberately.
- Keyset (seek) pagination `WHERE (created_at, id) < (?, ?) ORDER BY created_at DESC, id DESC LIMIT
  n` scales; `OFFSET n` re-scans and discards n rows every page.
- Window functions and `DISTINCT ON` often replace self-joins and correlated subqueries.

## Statistics and plan caching

- `ANALYZE` refreshes per-column stats; autovacuum runs it, but run it manually after bulk loads.
- **Extended statistics** capture cross-column correlation the planner otherwise assumes away:
  `CREATE STATISTICS s (dependencies, ndistinct) ON city, region FROM addresses; ANALYZE
  addresses;`. Use when a two-column filter estimate is far off.
- `default_statistics_target` (default 100) controls histogram granularity; raise per-column with
  `ALTER TABLE ... ALTER COLUMN ... SET STATISTICS n` for skewed columns.
- Prepared statements cache plans. After 5 executions the planner may switch from a custom
  (per-parameter) plan to a generic plan; a generic plan on skewed data can regress. Inspect with
  `EXPLAIN (GENERIC_PLAN)` and set `plan_cache_mode` if needed.
- Parameterized queries are the norm for both safety and plan reuse — see the SQL-injection and
  parameter-binding entries in `oracles/addenda/postgresql.md`.
