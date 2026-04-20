# ADR-0059: Prometheus-Compatible Metrics Exposition

**Status:** Accepted
**Date:** 2026-04-17
**Deciders:** Roman Głogowski (solo developer)
**Category:** Observability & Monitoring
**Related:** ADR-0036 (Prometheus/Grafana integrations), ADR-0058 (structured logs)

## Context

The platform must emit operational metrics (request rate, error rate, p50/p95/p99 latency, scan throughput, queue depth, etc.) to support SLO tracking and customer-facing Prometheus integration (ADR-0036). The exposition format drives tool compatibility, operator familiarity, and the friction of wiring Kartova's internal metrics to the same dashboards customers use.

## Decision

Every Kartova service exposes a **Prometheus-compatible metrics endpoint** (`/metrics`) in the Prometheus text exposition format, with standard instrumentation:

- HTTP request metrics: `http_requests_total`, `http_request_duration_seconds` (histogram) labeled with method, path template, status class, tenant (where safe cardinality-wise).
- Business metrics: `catalog_entities_total`, `scans_total`, `notifications_dispatched_total`, `auth_failures_total`.
- Latency histograms enabling p50/p95/p99 computation.
- Standard process/runtime metrics (CLR for .NET services).

Metrics align with the customer-facing Prometheus/Grafana integration (ADR-0036) so that internal and external dashboards can share queries. Cardinality rules are enforced: `tenant_id` is only labeled where bounded aggregates are meaningful (and sampled/rolled-up where not).

## Rationale

- Prometheus exposition is the de facto standard in cloud-native observability; every Kubernetes ops team knows it.
- Consistency with the customer-facing integration (ADR-0036) means one PromQL skill set covers both sides.
- Histograms make SLO math (error budget burn) straightforward.
- .NET has solid Prometheus client libraries; zero-friction for the stack (ADR-0027).

## Alternatives Considered

- **OpenTelemetry metrics (OTLP) first, Prometheus exporter** — valid long-term direction; today's Prometheus-native story is simpler and aligns with the customer integration. OTLP export can be added as a sink later.
- **StatsD** — outdated; push-based; no histograms natively.
- **Vendor-specific (Datadog / New Relic)** — lock-in; conflicts with cloud-agnostic strategy (ADR-0022).

## Consequences

**Positive:**
- Instantly compatible with the entire Prometheus/Grafana/Alertmanager stack.
- Internal and customer-facing dashboards reuse the same query language.
- Histograms support SLO-driven alerting out of the box.

**Negative / Trade-offs:**
- Cardinality discipline is mandatory — unbounded labels (tenant × path × status) will blow up Prometheus.
- Pull-based scrape model requires service discovery wiring in K8s (mostly automated via `ServiceMonitor`).

**Neutral:**
- Migration to OpenTelemetry is additive (dual-export) when the time comes.

## References

- PRD §7.2, §4.9 (observability integrations)
- Phase 0: E-01.F-07.S-03
- Related ADRs: ADR-0022, ADR-0027, ADR-0036, ADR-0058
