# PostgreSQL schema design, partitioning, RLS, and safe migrations

Design decisions that are cheap before data lands and expensive after. Covers types, JSONB,
constraints, declarative partitioning, row-level security for multi-tenancy, `SECURITY DEFINER`
hygiene, and lock-safe DDL.

## Data types

Pick the most specific type that models the value; specificity buys validation, smaller rows, and
better estimates.

| Choice | Prefer | Avoid | Why |
|---|---|---|---|
| Timestamps | `timestamptz` | `timestamp` (no zone) | Stored as UTC; `timestamp` silently drops offset |
| Money/exact | `numeric(p,s)` | `float`/`real` | Binary floats can't represent decimal cents exactly |
| Text | `text` or `varchar` (no limit) | `char(n)`, arbitrary `varchar(n)` caps | No perf gain from a length cap; enforce with `CHECK` |
| Identifiers | `bigint` identity, or `uuid` | `serial` (legacy), `int` for growth | `serial` predates `GENERATED ... AS IDENTITY` |
| Booleans/states | `boolean`, enum, or lookup FK | text flags `'Y'/'N'` | Typed states catch typos and index cleanly |

- **UUIDs**: `uuidv7()` (18) generates timestamp-ordered UUIDs — index-friendly (roughly
  append-order) unlike random `gen_random_uuid()` (v4), which scatters B-tree inserts and bloats.
- **Enums** are fast and compact but adding/reordering values needs DDL; a lookup table with an FK
  is more flexible when the set changes often.
- Prefer `GENERATED ALWAYS AS IDENTITY` over `serial` for surrogate keys.

## JSONB

Use `jsonb` for genuinely schemaless or sparse attributes, external document payloads, and
audit/event bodies. Do not use it as a substitute for columns you filter, join, or constrain on —
relational columns get statistics, constraints, and cheaper indexes.

- Operators: `->` (json), `->>` (text), `#>`/`#>>` (path), `@>` (containment), `?`/`?|`/`?&` (key
  exists), `@?`/`@@` (jsonpath).
- Index containment and key-existence with GIN: `CREATE INDEX ON docs USING gin (body)`. Use
  `jsonb_path_ops` (`gin (body jsonb_path_ops)`) when you only need `@>` — smaller and faster.
- Index a single hot scalar with an expression B-tree: `CREATE INDEX ON docs ((body->>'status'))`.
- Enforce shape with `CHECK (jsonb_typeof(body->'items') = 'array')` where it matters.

## Constraints and generated columns

- Declare `NOT NULL`, `CHECK`, `UNIQUE`, and `FOREIGN KEY` at the schema level; they are
  documentation the planner and every client share, not application-layer duplication.
- Foreign keys need an index on the referencing column for cascade and join performance; Postgres
  indexes the referenced (unique) side automatically but not the child side.
- **Exclusion constraints** (`EXCLUDE USING gist`) enforce "no two rows overlap" for ranges — e.g.
  non-overlapping bookings on `tsrange`.
- **Temporal constraints** (18): `PRIMARY KEY`/`UNIQUE`/`FOREIGN KEY ... WITHOUT OVERLAPS` and
  `PERIOD` give range-aware keys without hand-rolled exclusion logic.
- **Generated columns**: `GENERATED ALWAYS AS (expr)`. In 18 `VIRTUAL` (computed on read) is the
  default; `STORED` persists the value (needed to index it). Choose stored when you index or filter
  on the derived value, virtual when it is read-mostly and cheap.

## Declarative partitioning

Partition when a table is large enough that operations on subsets dominate: time-series retention
(drop old partitions instead of `DELETE`), or a natural tenant/region split. Below ~tens of
millions of rows, a good index usually beats partitioning's overhead.

| Strategy | `PARTITION BY` | Fits |
|---|---|---|
| Range | `RANGE (created_at)` | Time series, sequential ranges, retention by dropping partitions |
| List | `LIST (region)` | Discrete categories with bounded cardinality |
| Hash | `HASH (tenant_id)` | Even spread when no natural range/list key exists |

- **Partition pruning** is the payoff: queries must filter on the partition key so the planner
  touches only relevant partitions. Verify with `EXPLAIN` (expect few partitions scanned).
- Create indexes and constraints on the partitioned parent; they propagate to partitions. A
  `UNIQUE`/`PRIMARY KEY` must include the partition key.
- `ATTACH PARTITION` and `DETACH PARTITION CONCURRENTLY` add/remove partitions online; pre-create
  future range partitions (or use `pg_partman`) so inserts never hit a missing partition.
- Keep partition count reasonable (hundreds, not tens of thousands) — planning cost grows with it.

## Row-level security for multi-tenancy

RLS enforces per-row visibility in the database, so a missed `WHERE tenant_id = ?` in application
code cannot leak across tenants.

```sql
ALTER TABLE invoices ENABLE ROW LEVEL SECURITY;
ALTER TABLE invoices FORCE ROW LEVEL SECURITY;   -- also apply to the table owner
CREATE POLICY tenant_isolation ON invoices
  USING (tenant_id = current_setting('app.tenant_id')::bigint);
```

- The app sets `SET LOCAL app.tenant_id = '...'` per transaction (session-scoped setting), or uses
  a distinct DB role per tenant. `SET LOCAL` is preferred with transaction-pooled connections.
- `USING` filters reads and the pre-image of writes; add `WITH CHECK` to constrain inserted/updated
  rows so a tenant cannot write another tenant's id.
- `FORCE ROW LEVEL SECURITY` closes the gap that table owners and superusers bypass policies by
  default.
- Performance: the policy predicate is `AND`-ed into every query — index the tenant column
  (`(tenant_id, ...)`), and keep the predicate `current_setting`-based (a constant per statement)
  rather than a subquery so the planner can prune partitions and use indexes.
- Any table that stores rows for more than one tenant is a candidate; see the RLS entry in
  `oracles/addenda/postgresql.md`.

## SECURITY DEFINER functions and search_path

A `SECURITY DEFINER` function executes with its owner's privileges, but unqualified names inside
its body still resolve through the caller's `search_path`. A caller who can create objects in any
schema on that path (a writable `public`, or the implicit temp schema) can shadow a table,
function, or operator the body references — and have their own code run with the owner's rights.
Pin the path in the definition:

```sql
CREATE FUNCTION app.transfer_credit(from_id bigint, to_id bigint, amount numeric)
RETURNS void
LANGUAGE plpgsql SECURITY DEFINER
SET search_path = ''   -- safest: forces schema-qualified references throughout the body
AS $$ ... $$;
```

- `SET search_path = ''` is the safe default — every object reference in the body must then be
  schema-qualified. A fixed schema list (`SET search_path = app, pg_temp`) also qualifies, but
  must name `pg_temp` explicitly and last: when unlisted, the temp schema is searched first.
- Lock down execution: functions are executable by `PUBLIC` by default. `REVOKE EXECUTE ... FROM
  PUBLIC`, then `GRANT EXECUTE` to the specific roles that need the privilege bridge.
- Prefer `SECURITY INVOKER` (the default) everywhere else; keep definer functions few, small, and
  reviewed. The deterministic check is SEC-PG-003 in `oracles/addenda/postgresql.md`.

## Lock-safe migrations

DDL takes locks. The danger is `ACCESS EXCLUSIVE` (blocks all reads and writes) held while a slow
operation runs, or acquired behind a long transaction. Set guards before DDL:

```sql
SET lock_timeout = '3s';      -- fail fast rather than queue behind a long txn
SET statement_timeout = '0';  -- but let the DDL itself finish
```

| Operation | Lock / cost | Safe approach |
|---|---|---|
| Add nullable column | Brief `ACCESS EXCLUSIVE`, metadata only | Safe directly |
| Add column with default | Metadata only since PG11 (no rewrite) | Safe; volatile defaults still rewrite |
| Add `NOT NULL` | Full scan to validate | Add `CHECK (col IS NOT NULL) NOT VALID`, `VALIDATE`, then set `NOT NULL` (18 can use a valid CHECK to skip the scan) |
| Add `FOREIGN KEY` | Locks both tables to validate | `ADD CONSTRAINT ... NOT VALID` then `VALIDATE CONSTRAINT` (only `SHARE UPDATE EXCLUSIVE`) |
| Create index | `SHARE` lock blocks writes | `CREATE INDEX CONCURRENTLY` (outside a txn; verify it didn't leave an `INVALID` index) |
| Drop index | Brief exclusive | `DROP INDEX CONCURRENTLY` |
| Change column type | Table rewrite + exclusive lock | Add new column, backfill in batches, swap; or expand-contract |
| Backfill data | Long txn = bloat + lock risk | Update in bounded batches with commits between, not one statement |

Follow **expand-contract** for breaking changes: add the new shape, deploy code that writes both,
backfill, switch reads, then drop the old shape in a later release. Every migration should have a
tested reverse (down) path — see the reversible-migration entry in `oracles/addenda/postgresql.md`.
