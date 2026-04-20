# ADR-0058: Structured JSON Logs with Correlation IDs and Tenant Context

**Status:** Accepted
**Date:** 2026-04-17
**Deciders:** Roman Głogowski (solo developer)
**Category:** Observability & Monitoring
**Related:** ADR-0059 (metrics), ADR-0014 (tenant claim), ADR-0036 (Prometheus/Grafana integrations)

## Context

Kartova is a multi-tenant SaaS serving 1000+ tenants. Debugging a tenant-specific issue requires slicing logs precisely by tenant, correlation ID, and request path (PRD §7.2). Plain-text logs or logs without tenant context make triage painful and risk cross-tenant data exposure when logs are shared during support. Observability must be designed in from day one (Phase 0 E-01.F-07.S-02).

## Decision

All services emit **structured JSON logs** with a mandatory set of fields:

- `timestamp` (RFC 3339 / ISO 8601 UTC)
- `level` (trace/debug/info/warn/error/fatal)
- `service`, `version`, `environment`
- `correlation_id` — propagated across in-process calls and outbound HTTP/gRPC via `traceparent` / custom header; one ID per user request
- `tenant_id` — extracted from the JWT tenant claim (ADR-0014) and attached to the logging scope for the lifetime of the request
- `user_id` (when available), `request_path`, `status`, `duration_ms`
- `message` and arbitrary structured fields

Logs are shipped to a central log store (initial choice: Elasticsearch/OpenSearch, aligning with ADR-0002 for operational consolidation). PII in logs is redacted at source; secrets never reach logs (ADR-0078, pending).

## Rationale

- Structured logs are directly queryable by tenant, correlation ID, or any other field — this is the core debug workflow in SaaS.
- Correlation IDs unify per-request visibility across microservices.
- Tenant-scoped logging prevents accidental cross-tenant exposure when exporting logs for support.
- Alignment with the existing Elasticsearch investment (ADR-0002) reduces operational surface for solo dev.

## Alternatives Considered

- **Plain-text logs** — rejected: cannot slice by tenant; cross-tenant leakage risk on export.
- **OpenTelemetry logs only** — OTel logs spec is maturing; emitting JSON today keeps us portable and OTel-adaptable later via log-to-OTel adapters.
- **Per-tenant log silos** — operational explosion at 1000+ tenants; filtering at query time is the right trade-off.
- **W3C Trace Context only** — traces alone don't carry the business context (tenant, user, feature) we need.

## Consequences

**Positive:**
- Fast, accurate tenant-scoped debugging from the central log store.
- Correlation IDs make distributed tracing narratives trivial to reconstruct.
- Forms the foundation for tenant-aware alerting.

**Negative / Trade-offs:**
- JSON logs are larger than plain text; ingest/storage costs scale accordingly.
- Every service boundary must remember to propagate `correlation_id` and `tenant_id` — enforce via shared logging middleware.
- Structured field discipline must be maintained (avoid unbounded-cardinality fields).

**Neutral:**
- Future migration to OpenTelemetry logs is a drop-in replacement of the sink, not the schema.

## References

- PRD §7.2
- Phase 0: E-01.F-07.S-02
- Related ADRs: ADR-0002, ADR-0014, ADR-0036, ADR-0059
