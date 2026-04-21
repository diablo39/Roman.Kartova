# ADR-0013: Elasticsearch Shared Index With Tenant Routing

**Status:** Accepted
**Date:** 2026-04-20
**Deciders:** Roman Głogowski (solo developer)
**Category:** Multi-Tenancy / Search
**Related:** ADR-0002 (Elasticsearch), ADR-0012 (PostgreSQL RLS for tenants), ADR-0015 (GDPR erasure), ADR-0017 (retention policy), ADR-0074 (scale targets), ADR-0075 (performance SLOs)

## Context

Kartova uses Elasticsearch (ADR-0002) for full-text search over entities, documentation, API specs, and runbooks. At scale target 1000+ tenants (ADR-0074) with millions of documents, index strategy directly impacts query latency (p95 < 500ms per ADR-0075), cluster health, and tenant isolation. Unlike PostgreSQL (ADR-0012 uses RLS), Elasticsearch has no row-level enforcement mechanism — isolation must be enforced at the index/alias/application layer. Index-per-tenant does not scale beyond a few hundred indexes in a cluster (cluster state bloat).

## Decision

Use shared indexes per document type (e.g., `kartova-entities-*`, `kartova-docs-*`, `kartova-deployments-*`). Every document carries a `tenant_id` field (UUID). Use Elasticsearch routing `routing=tenant_id` so all documents for a given tenant hash to the same shard, which keeps per-tenant queries on a single shard and improves cache hit rate. The application layer always includes `{"term": {"tenant_id": "..."}}` as the first filter in every query. A **per-tenant filtered alias** (e.g., `kartova-entities-tenant-<uuid>`) is created for each tenant with a hard-coded filter on their `tenant_id`; read paths use the alias as the search target to provide an additional isolation layer. Use Index Lifecycle Management (ILM) for retention and warm/cold/delete tiers per ADR-0017.

## Rationale

- **Scale to 1000+ tenants** — industry-standard SaaS pattern per official Elastic guidance ("one index per type, tenant as field, use routing"); avoids cluster-state bloat that kills index-per-tenant at scale.
- **Latency** — routing by `tenant_id` deterministically targets a single shard, keeping per-tenant searches fast even when total index size is large.
- **Defense-in-depth isolation:**
  1. `.NET ITenantScopedSearch` API enforces tenant parameter at compile time
  2. Query pipeline always appends `tenant_id` term filter
  3. Read path uses per-tenant filtered alias — even if filter is accidentally omitted at higher layers, alias filter applies
  4. Index template + default ingest pipeline enforces `tenant_id` presence at index time
- **GDPR cascade deletion (ADR-0015)** — `_delete_by_query` with `tenant_id` term plus `routing` is fast (single shard scope).
- **Retention (ADR-0017)** — ILM rolls shared indexes; warm/cold tiers apply uniformly; older documents transition automatically; MiFID II tenants flagged with extended ILM policy.
- **Backup** — single snapshot policy for all tenants.
- **Future hybrid** — if a "noisy neighbor" tenant dominates a shard, it can be migrated to a dedicated index by updating alias mapping without application changes.

## Alternatives Considered

- **Index-per-tenant:** Kills cluster state at 1000+ indexes; each index has overhead (metadata, shard allocation, cache); lifecycle management becomes 1000× more complex; reindex operations per tenant are expensive. Rejected.
- **Shared index + filter only (no routing):** Works but every query scans all shards; latency degrades as cluster grows; no deterministic data locality. Rejected in favor of routing variant.
- **Shared index + alias only (no routing):** Same scan-all-shards problem; filtered alias provides isolation but not performance. Rejected.
- **Alias-per-tenant on shared indexes without routing:** Isolation via alias filter but scan-all-shards at query time — slow at scale.
- **Data streams + tenant routing:** Good for time-series data with ILM; overkill for entity/doc indexes which are not strictly time-series; can be adopted later for specific indexes (e.g., audit events) where time-ordered retention dominates.
- **Hybrid tiered (dedicated index for large tenants, shared for rest):** Reasonable future evolution but unnecessary for MVP. Migration path is preserved by the alias abstraction.

## Consequences

**Positive:**
- Scales to 1000+ tenants in single cluster
- Per-tenant query latency benefits from shard locality (routing)
- Triple-layer defense-in-depth prevents accidental cross-tenant leaks
- GDPR erasure and retention jobs simplify to per-tenant-id operations
- Single ILM policy manages retention across all tenants
- Can evolve to hybrid (dedicated index for large tenants) without application refactor

**Negative / Trade-offs:**
- Routing on `tenant_id` creates shard imbalance if tenant sizes vary dramatically — large tenants concentrate on specific shards
- Requires disciplined query construction — application code must always pass tenant context; enforced via `ITenantScopedSearch` abstraction
- Per-tenant filtered alias creation adds tenant onboarding step (cheap: single API call per tenant)
- Schema changes (mapping updates) affect all tenants simultaneously — reindex operations are large
- Noisy-neighbor risk: a tenant with extreme data volume or query load can affect neighbors on the same shard
- Monitoring shard balance and top-tenant usage becomes an operational responsibility

**Neutral:**
- Does not affect ADR-0002 (Elasticsearch as search engine) — refines its deployment pattern
- Secondary stores (PostgreSQL per ADR-0012, Kafka per ADR-0003, MinIO per ADR-0004) have their own tenant isolation mechanisms

## Implementation notes

```json
// Index template applied to kartova-entities-*
{
  "index_patterns": ["kartova-entities-*"],
  "template": {
    "settings": {
      "number_of_shards": 6,
      "number_of_replicas": 1,
      "index.default_pipeline": "enforce-tenant-id"
    },
    "mappings": {
      "properties": {
        "tenant_id": { "type": "keyword" },
        "entity_id": { "type": "keyword" },
        "name": { "type": "text" }
      }
    }
  }
}

// Per-tenant filtered alias (created at tenant onboarding)
POST /_aliases
{
  "actions": [{
    "add": {
      "index": "kartova-entities-*",
      "alias": "kartova-entities-tenant-{tenantId}",
      "filter": { "term": { "tenant_id": "{tenantId}" } },
      "routing": "{tenantId}"
    }
  }]
}
```

## References

- PRD §7.1 (scale), §7.2 (search p95 < 500ms)
- Phase 0 E-01.F-08.S-02 (Elasticsearch index strategy)
- Phase 3 E-11.F-05.S-02 (documentation full-text search)
- Elastic guidance: https://www.elastic.co/blog/found-multi-tenancy-with-elasticsearch
