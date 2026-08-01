# Migration verification — `AddOneSystemPerComponentIndex` collapse step

**Date:** 2026-07-30 · **Slice:** E-03.F-03.S-01 closeout (A1) · **Migration:** `20260730160400_AddOneSystemPerComponentIndex`

## Why this was verified by hand

The migration's `Up()` collapses pre-existing duplicate `PartOf` memberships before creating the partial unique index `ux_relationships_one_system`. Neither the integration suite nor the container build can exercise that collapse: Testcontainers databases are created by applying **all** migrations to an empty database, so the duplicate precondition cannot exist by the time this migration runs. A collapse step that silently matched zero rows would look identical to a working one — and would fail the **deploy** (migrations run as a pre-upgrade Job, ADR-0085), not the app.

The specific hazard: `relationships` carries `FORCE ROW LEVEL SECURITY`, so a bare `DELETE FROM relationships` inside a migration matches **zero rows**, because no `app.current_tenant_id` is set. This was raised by the Task 3 code review.

## Method

A throwaway `postgres:18-alpine` container, a `relationships` table with the production column set, `ENABLE` + `FORCE ROW LEVEL SECURITY`, a `tenant_isolation` policy matching production, and a non-superuser table owner (so `FORCE` actually bites, as it does for the app role). Then the `Up()` SQL **verbatim**. Script: `migration-collapse-verification.sql` (sibling file).

Seed data was chosen to catch the ways a dedupe query goes wrong:

| Row | Purpose |
|---|---|
| 3 × `PartOf` for one Application in tenant A (created 07-06, 07-07, 07-08) | the duplicate case; the 07-06 row must be the survivor |
| 1 × `PartOf`, **same component Guid, different tenant** | must survive — `tenant_id` is in the grouping key |
| 1 × `PartOf`, **same Guid but `source_kind = 'Service'`** | must survive — `source_kind` is in the grouping key |
| 1 × `DependsOn` from the same component | must survive — the index and the collapse are `WHERE type = 'PartOf'` |

## Results

```
=== CONTROL: a bare DELETE under FORCE RLS (the silent-no-op trap) ===
DELETE 0

=== MIGRATION SQL VERBATIM FROM AddOneSystemPerComponentIndex.Up() ===
DELETE 2
CREATE INDEX

=== ASSERTIONS ===
 survivor_is_oldest                     | PASS
 one_row_per_group                       | PASS
 other_tenant_survived                   | PASS
 service_kind_survived                   | PASS
 dependson_untouched                     | PASS
 rls_enabled_and_forced_after_migration  | PASS
 index_exists                            | PASS

=== the index must now REFUSE a second PartOf edge ===
ERROR:  duplicate key value violates unique constraint "ux_relationships_one_system"
DETAIL:  Key (tenant_id, source_kind, source_id)=(aaaaaaaa-…-000a, Application, cccccccc-…-0001) already exists.
```

**The control line is the point of this exercise.** `DELETE 0` under `FORCE` RLS confirms the trap is real: the naive collapse would have reported success while deleting nothing, and the subsequent `CREATE UNIQUE INDEX` would have aborted the deploy. The migration's `DISABLE` → delete → `ENABLE` + `FORCE` sequence deletes exactly the 2 redundant rows.

## Consequences for deployment

- Applying this migration to a database that already holds duplicate memberships is **safe**: duplicates collapse deterministically to the oldest edge per `(tenant_id, source_kind, source_id)`, reproducibly across environments.
- The manual pre-flight `SELECT … HAVING count(*) > 1` is therefore no longer a prerequisite. It remains useful as an *audit* — it tells you whether any real data was collapsed. Run it as a superuser or bypass role; under the app role, RLS hides the rows.
- Duplicates cannot predate 2026-07-05 (`20260705111604_PurgePartOfRelationships` deleted every `PartOf` row). Any that exist were created since by the unguarded `POST /catalog/relationships` path, which Task 4 closes.

## Caveat

This exercises the migration's **SQL** against a faithful reproduction of the table and its RLS posture — not the EF migration pipeline end-to-end against a database with production data. The remaining untested inch is EF's own invocation of that SQL, which the integration suite does cover (every Testcontainers run applies this migration).

---

## Addendum 2026-07-31 — independent PostgreSQL review

A PostgreSQL specialist reviewed this migration and the surrounding write/read paths against the live dev database (`pg_locks`, `EXPLAIN`, catalog queries, and a real `23505` reproduction). Findings, and what changed as a result:

**Confirmed correct, with sharper reasoning than we had:**
- The partial unique index is doing genuinely new work — `ux_relationships_edge` constrains the *full edge*, so a component could hold N `PartOf` edges to different Systems and still satisfy it. It cannot express at-most-one.
- The membership pre-check both handlers run gets a **full 3-column index match** on `ux_relationships_one_system` (verified by `EXPLAIN` as the app role with RLS live).
- No cross-tenant existence leak — but only because `tenant_id` is the **leading** key column. A unique index is checked against the full physical index regardless of the session's RLS visibility, so rows can only collide within a tenant. Now documented in the migration as an invariant.
- `PostgresException.ConstraintName` **is** reliably populated for a partial-unique-index violation (verified by a real duplicate insert), so the 409 mapping does not silently degrade to a 500.
- The 409-then-`COMMIT` path is safe for a better reason than "Postgres turns COMMIT-in-aborted-block into rollback": `UseTransaction` enlistment plus EF Core **automatic savepoints** mean `SaveChangesAsync` already rolled back to its savepoint, so the transaction is clean when the exception surfaces. This depends on nothing using `EnableRetryOnFailure` — now documented at the catch.
- The RLS toggle leaks nothing: `ALTER TABLE ... DISABLE ROW LEVEL SECURITY` takes `AccessExclusiveLock`, so no concurrent session can even `SELECT` the table during the window.

**Changed (`9e14813e`):** the same `AccessExclusiveLock` finding turned the lock question into a real one — the lock spans the entire `Up()` body from the first `ALTER TABLE`, not merely the index build, and Helm `pre-upgrade` hooks run while the previous release still serves traffic. Added `SET LOCAL lock_timeout = '5s'` as the first statement so a contended deploy fails fast instead of queueing every reader behind us.

**Deliberately deferred, reasoning recorded in-file:** splitting the index build into a separate `SuppressTransaction` migration using `CREATE UNIQUE INDEX CONCURRENTLY` (plus a `pg_index.indisvalid` check, since a concurrent build that meets a leftover duplicate leaves an INVALID index). Right once `relationships` holds real volume; wrong now at 21 rows / 88 kB, because `CONCURRENTLY` cannot share the collapse step's transaction and a failed build would leave the collapse committed beside an invalid index.

**Follow-ups outside this slice:**
- `ix_relationships_tenant_source` is a strict column-prefix of `ux_relationships_edge` and the planner never chooses it — maintained on every write for nothing. Safe to drop.
- `direction=all&type=X` list reads do not get a covering index (filter + explicit sort). Not worth an index today; re-measure if that screen becomes hot.
- Re-run `SetComponentSystemTests.PUT_system_replaces_the_previous_membership` after any EF Core/Npgsql bump: the move works because EF currently orders the DELETE before the INSERT, and both share the same partial-unique key — that ordering is a batch-order default, not a guarantee pinned by model metadata (EF cannot see these indexes).
