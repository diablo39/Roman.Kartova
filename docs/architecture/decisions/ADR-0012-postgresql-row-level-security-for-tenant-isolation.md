# ADR-0012: PostgreSQL Row-Level Security for Tenant Isolation

**Status:** Accepted
**Date:** 2026-04-20
**Deciders:** Roman Głogowski (solo developer)
**Category:** Multi-Tenancy
**Related:** ADR-0001 (PostgreSQL), ADR-0011 (one org = one tenant), ADR-0014 (tenant claim from JWT), ADR-0074 (scale targets), ADR-0075 (performance SLOs)

## Context

Kartova targets 1000+ tenants with up to 10k services and 5k users per tenant, totaling millions of entities (ADR-0074). Every tenant's data must be isolated from every other tenant's — a data leak would be catastrophic for compliance (GDPR/MiFID II per ADR-0015/0016) and business reputation. Operationally, a solo developer cannot manage 1000 separate databases or schemas. Hot-path API latency target is p95 < 200ms (ADR-0075). The tenant claim is extracted from the JWT per ADR-0014.

## Decision

Use PostgreSQL Row-Level Security. Every tenant-owned table gets a `tenant_id UUID NOT NULL` column and an RLS policy enforcing `tenant_id = current_setting('app.current_tenant_id')::uuid`. Composite indexes always start with `tenant_id` (e.g., `(tenant_id, name)`, `(tenant_id, team_id)`). API middleware sets `app.current_tenant_id` via `SET LOCAL` within each request transaction. EF Core Global Query Filters provide a defense-in-depth app-layer filter on top of the DB-level RLS policy. RLS is `FORCE`d on all tenant tables so even superuser queries respect the policy during normal operation.

## Rationale

- **Defense-in-depth security** — PostgreSQL enforces isolation at the engine level; a missing `WHERE tenant_id` in application code cannot cause a leak.
- **Scale** — a single cluster, single DB scales to millions of rows across 1000+ tenants without PostgreSQL catalog bloat (the problem that kills schema-per-tenant around 500 schemas).
- **Solo-dev operations** — one backup, one migration run, one monitoring target, one connection pool.
- **GDPR cascade deletion (ADR-0015)** — becomes `DELETE ... WHERE tenant_id = @id CASCADE` — one statement across all tables.
- **MiFID II compliance flag (ADR-0016)** — single column on `tenants` table + conditional retention jobs — trivial.
- **EF Core support** — Global Query Filters auto-inject the tenant filter transparently; combined with RLS provides two independent layers.
- **Proven pattern** — used by GitLab, Supabase, and many SaaS platforms at this scale.

## Alternatives Considered

- **Schema-per-tenant:** PostgreSQL starts degrading around 500+ schemas (catalog bloat, planner slowdown, pg_dump issues); migrations must run N times; connection pooling becomes complex. Does not scale to 1000+ tenants.
- **Database-per-tenant:** Operational horror at 1000 databases; massive RAM/connection overhead; cross-tenant analytics impossible; backup/restore orchestration nightmare.
- **Hybrid (shared RLS + dedicated DB for enterprise):** Viable future evolution but unnecessary for MVP. RLS alone can handle all current and targeted tenants. Can migrate the largest tenants to dedicated DBs later without changing the RLS model for the rest.
- **App-layer filtering only:** Security depends entirely on developer discipline; one missed `WHERE tenant_id` in any repository causes a leak; auditing every query forever is not sustainable for a solo developer.

## Consequences

**Positive:**
- Cross-tenant data leaks are physically impossible at the DB layer, even if app code has bugs
- Single-cluster operations match solo-developer capacity
- GDPR erasure and MiFID II retention jobs simplify to per-tenant-id operations
- EF Core transparent filtering keeps application code clean
- Matches ADR-0001, ADR-0011, ADR-0014 naturally

**Negative / Trade-offs:**
- All database access must set `app.current_tenant_id` at transaction start — missing this causes queries to return zero rows (must be caught in integration tests)
- PostgreSQL planner can sometimes produce suboptimal plans with complex RLS policies — hot-path queries require `EXPLAIN ANALYZE` review
- Testing requires multi-tenant scenarios to verify isolation
- Background jobs need a pattern for elevated (cross-tenant) access — typically a dedicated DB role that bypasses RLS
- Migrations and admin tooling need a superuser or `BYPASSRLS` role for cross-tenant maintenance

**Neutral:**
- Future evolution to hybrid (dedicated DB for largest tenants) remains possible without changing the RLS model for the rest
- Secondary stores (Elasticsearch per ADR-0013, Kafka per ADR-0003, MinIO per ADR-0004) need their own tenant isolation strategies — RLS only governs PostgreSQL

## Implementation notes

```sql
-- Enable on every tenant-owned table
ALTER TABLE entities ENABLE ROW LEVEL SECURITY;
ALTER TABLE entities FORCE ROW LEVEL SECURITY;
CREATE POLICY tenant_isolation ON entities
  USING (tenant_id = current_setting('app.current_tenant_id')::uuid);

-- Composite indexes always start with tenant_id
CREATE INDEX idx_entities_tenant_name ON entities(tenant_id, name);
```

## References

- PRD §7.1 (scale targets), §7.3 (tenant isolation requirement)
- Phase 0 E-01.F-03.S-01 (multi-tenant schema), E-01.F-08.S-01 (indexing strategy), E-01.F-08.S-03 (tenant isolation strategy)
- PostgreSQL RLS docs: https://www.postgresql.org/docs/current/ddl-rowsecurity.html
- EF Core Global Query Filters: https://learn.microsoft.com/en-us/ef/core/querying/filters
