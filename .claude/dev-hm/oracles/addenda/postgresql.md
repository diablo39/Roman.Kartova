# Oracle addendum: PostgreSQL (PG)

PostgreSQL-specific, deterministic checks that extend the core `oracles/security-oracle.md` (SEC-*)
and `oracles/quality-oracle.md` (QUA-*). Apply these to SQL, DDL, migration scripts, and ORM/raw
query code that targets PostgreSQL. Only checks whose pass criterion is decidable from the code or
migration alone belong here; anything needing judgment stays in prose in the
`knowledge/postgresql/` files. Severity tiers (S0–S3) are defined in
`knowledge/shared/severity-tiers.md`; the verdict-line format is in
`knowledge/shared/defense-in-depth.md`. Determinism rule: two independent runs over the same code
must yield identical verdict tables. Where a core entry and an addendum entry cover the same
defect, the stricter severity and verdict govern (precedence rule in the core oracle preambles).

Roles: the developer runs the applicable entries as a self-check before handoff; the code reviewer
and the postgresql-expert re-run them independently on the diff; the security and quality gates run
the full set. Scope "new code" to SQL, DDL, and migrations added or modified by the change under
review. SEC-PG-001 is the PostgreSQL projection of core SEC-001 — report it here, not twice.

## Security (SEC-PG)

| ID | Check | Pass criterion | Severity | Remediation |
|---|---|---|---|---|
| SEC-PG-001 | SQL injection / parameter binding | Zero SQL built by concatenating or interpolating external input; every dynamic value is a driver placeholder or ORM bind parameter, and any dynamic identifier is either allow-listed or quoted with `format('%I'/'%L')`/`quote_ident`/`quote_literal` | S0 | `knowledge/security/secure-coding-review/injection.md` |
| SEC-PG-002 | Row-level security on multi-tenant tables | Every table that stores rows for more than one tenant (carries a tenant/org discriminator used for isolation) has `ENABLE ROW LEVEL SECURITY` plus `FORCE ROW LEVEL SECURITY` and at least one policy whose `USING`/`WITH CHECK` predicate is keyed on the tenant, with the tenant supplied via `SET LOCAL`/`current_setting` rather than trusted from the client | S1 | `knowledge/postgresql/schema-design.md#row-level-security-for-multi-tenancy` |
| SEC-PG-003 | `SECURITY DEFINER` pins `search_path` | Every function or procedure declared `SECURITY DEFINER` sets a controlled `search_path` in its definition (`SET search_path = ...`, empty or schema-qualified); zero `SECURITY DEFINER` objects rely on the caller's `search_path` | S1 | `knowledge/postgresql/schema-design.md#security-definer-functions-and-search_path` |
| SEC-PG-004 | No overbroad privilege grants | No `GRANT ALL` on application tables/schemas and no `GRANT ... TO PUBLIC` on non-public objects in the change; grants name the specific role and the specific privileges the workload needs | S2 | `knowledge/security/secure-coding-review/access-control.md` |

## Quality (QUA-PG)

| ID | Check | Pass criterion | Severity | Remediation |
|---|---|---|---|---|
| QUA-PG-001 | No `SELECT *` in production read paths | Zero `SELECT *` (or `RETURNING *`, or `t.*`) in non-test application queries, views, and functions; production reads name their columns. `count(*)` and `EXISTS (SELECT 1 ...)` are exempt | S2 | `knowledge/postgresql/query-optimization.md#query-rewrites` |
| QUA-PG-002 | Reversible migrations | Every migration has a tested reverse path (down migration or equivalent documented rollback), or documents why it is irreversible (projection of core QUA-091) | S2 | `knowledge/postgresql/schema-design.md#lock-safe-migrations` |
| QUA-PG-003 | Lock-safe DDL on populated tables | Index creation/removal on a populated table uses `CREATE INDEX`/`DROP INDEX CONCURRENTLY` (outside a transaction block); `NOT NULL`, foreign-key, and check constraints are added `NOT VALID` then `VALIDATE`; the migration sets an explicit `lock_timeout`; column-type changes that rewrite the table use expand-contract | S1 | `knowledge/postgresql/schema-design.md#lock-safe-migrations` |
| QUA-PG-004 | Indexes justified by an access pattern | Every new index names the query predicate, join, or sort it serves (migration comment or PR note); no new index duplicates the leading-column prefix of an existing index on the same table without a documented reason (covering/partial/expression) | S2 | `knowledge/postgresql/query-optimization.md#index-selection` |
| QUA-PG-005 | Foreign-key columns indexed | Every foreign-key referencing (child) column added by the change has a supporting index; a leading-column composite index counts | S2 | `knowledge/postgresql/schema-design.md#constraints-and-generated-columns` |
| QUA-PG-006 | Time values use `timestamptz` | New columns storing an absolute point in time use `timestamptz`, not `timestamp`/`timestamp without time zone`; `date`/`time`/interval-of-day columns are exempt | S2 | `knowledge/postgresql/schema-design.md#data-types` |
| QUA-PG-007 | Exact/monetary values use `numeric` | New columns holding money or values requiring exact decimal arithmetic use `numeric(p,s)`, not `real`/`double precision`/`float` | S2 | `knowledge/postgresql/schema-design.md#data-types` |
| QUA-PG-008 | Destructive DDL follows expand-contract | Destructive DDL (`DROP COLUMN`/`DROP TABLE`/type narrowing) affecting a shape the running application version still reads ships in a separate later change, additive step first, with the sequence stated in the handoff (projection of core QUA-090); zero same-release drops of a still-read shape | S1 | `knowledge/postgresql/schema-design.md#lock-safe-migrations` |

## Notes

- SEC-PG-002 is decidable only when the schema declares multi-tenancy (a discriminator column such
  as `tenant_id`/`org_id` used for isolation). A single-tenant schema, or one that isolates tenants
  by separate databases/roles, reports this entry as not-applicable in the verdict summary, not as a
  pass.
- QUA-PG-002/003/008 apply to migration files. A change with no schema migration reports them as
  not-applicable. "Reverse path" means a down migration or an equivalent documented rollback, not an
  untested `-- down` stub. QUA-PG-002 grades reversibility (S2, matching core QUA-091); QUA-PG-008
  grades destructive-DDL sequencing (S1, matching core QUA-090) — they are reported separately.
- QUA-PG-006/007 apply to columns added or retyped by the change; pre-existing columns are out of
  scope for the diff under review.
- Where a project has no SQL, no migrations, or no multi-tenant data, the corresponding entries are
  reported as not-applicable in the verdict summary, not as passes.

## Reporting

Report one line per fail or waiver, e.g. `QUA-PG-003 fail db/migrations/0012.sql:8 — CREATE INDEX
without CONCURRENTLY on a populated table`. Report passes and not-applicables as summary counts. S0
is never waivable; S1/S2 waivers need a recorded rationale accepted by the matching gate
(senior-security-engineer for SEC-PG, senior-quality-engineer for QUA-PG).
