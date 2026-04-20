# ADR-0002: Elasticsearch for Search

**Status:** Accepted
**Date:** 2026-04-17
**Deciders:** Roman Głogowski (solo developer)
**Category:** Data Platform
**Related:** ADR-0001 (PostgreSQL), ADR-0013 (index strategy, pending)

## Context

Kartova must provide full-text entity search, faceted filtering (by type, owner, tags, maturity), and documentation search across millions of entities and large documentation corpora (PRD §4.1, §4.9). Performance SLO is p95 < 500ms at the stated scale (PRD §7.2, ADR-0075). PostgreSQL full-text search is adequate for small catalogs but not at 10k services × 1000 tenants.

## Decision

Use Elasticsearch as the search engine for entity catalog search, documentation full-text search, and faceted filtering. PostgreSQL remains the system of record; Elasticsearch is a derived projection kept in sync via application-level indexing (event-driven).

## Rationale

- Best-in-class full-text relevance, faceting, aggregations, and multi-tenant filtering.
- Handles the scale envelope (ADR-0074) with sharding / routing strategies.
- Strong .NET client ecosystem (Elastic.Clients.Elasticsearch) aligns with ADR-0027.
- Documentation search (Phase 3) and entity search (Phase 0) share the same engine — single operational investment.

## Alternatives Considered

- **OpenSearch** — viable fork; re-evaluate if Elastic licensing terms become an issue. Functionally equivalent for MVP.
- **Meilisearch / Typesense** — excellent DX but weaker faceting/aggregations and less proven at multi-tenant 1000+ scale.
- **PostgreSQL FTS + pg_trgm** — insufficient relevance/faceting at target scale; would offload pressure onto primary DB.
- **Azure AI Search** — vendor lock-in conflicts with cloud-agnostic strategy (ADR-0022).

## Consequences

**Positive:**
- Rich search UX from Phase 0 (suggestions, facets, highlighting)
- Keeps heavy read traffic off PostgreSQL

**Negative / Trade-offs:**
- Two datastores to operate, back up, and keep in sync
- Indexing pipeline needed (eventual consistency; drift risk)
- Memory-hungry — cluster sizing is a real cost line item

**Neutral:**
- Tenant isolation strategy (shared vs per-tenant index) deferred to ADR-0013

## References

- PRD §8, §11 (Resolved Decision #3)
- Phase 0: E-01.F-08.S-02
- Phase 3: E-11.F-05.S-02
