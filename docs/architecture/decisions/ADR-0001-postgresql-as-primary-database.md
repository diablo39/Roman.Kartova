# ADR-0001: PostgreSQL as Primary Database

**Status:** Accepted
**Date:** 2026-04-17
**Deciders:** Roman Głogowski (solo developer)
**Category:** Data Platform
**Related:** ADR-0012 (tenant isolation, pending), ADR-0018 (audit log)

## Context

Kartova is a multi-tenant SaaS developer portal targeting 1000+ tenants, 10k services/tenant, and millions of entities overall (PRD §7.1). The primary transactional datastore must support strong multi-tenancy, flexible entity metadata (nine entity types with heterogeneous attributes), relational integrity for graph-like relationships, and GDPR/MiFID II compliance features (row-level security, audit trails, cascade deletion). As a solo developer, operational simplicity and a broad community are essential.

## Decision

Use PostgreSQL (currently v16+) as the primary transactional datastore for all tenant data, including catalog entities, relationships, users/RBAC, audit log, notifications log, billing metering, and tenant configuration. Row-level security (RLS) with `tenant_id` columns and composite indexes will be used for isolation (see ADR-0012).

## Rationale

- Strong JSONB support enables flexible entity metadata and per-tenant extensibility without a document store.
- Mature RLS mechanism fits multi-tenant isolation at 1000+ tenant scale where schema-per-tenant breaks down.
- Proven at scale, open-source, cloud-agnostic (AWS RDS / Azure Flexible Server / GCP CloudSQL / self-hosted), aligning with ADR-0022 cloud-agnostic K8s strategy.
- Rich ecosystem (migrations, EF Core provider, pgBouncer, logical replication) reduces solo-developer operational burden.
- LISTEN/NOTIFY and logical replication open future options for outbox patterns and independent replicas (ADR-0005).

## Alternatives Considered

- **MySQL/MariaDB** — weaker JSON type, no true RLS, inferior at analytic workloads.
- **SQL Server** — licensing cost, reduced cloud portability.
- **CockroachDB / YugabyteDB / Spanner** — distributed SQL overkill for MVP; operational cost too high for solo dev.
- **Aurora** — locks Kartova to AWS, conflicting with cloud-agnostic strategy.

## Consequences

**Positive:**
- Single proven datastore across all transactional concerns
- JSONB enables schema evolution without migrations for metadata fields
- RLS + tenant_id provides enforced isolation at the database layer

**Negative / Trade-offs:**
- Vertical scaling limits eventually require sharding strategy (future)
- Elasticsearch still needed for search workloads (ADR-0002)
- RLS adds query-plan complexity; requires disciplined connection-scoped `SET app.current_tenant`

**Neutral:**
- Operational ownership (backups, PITR, version upgrades) falls to whichever cloud's managed offering is selected at deploy time

## References

- PRD §8, §11 (Resolved Decision #2)
- Phase 0: E-01.F-01.S-03
