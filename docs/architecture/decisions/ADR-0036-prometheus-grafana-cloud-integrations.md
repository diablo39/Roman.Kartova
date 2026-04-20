# ADR-0036: Prometheus + Grafana Cloud as Mandatory Integrations

**Status:** Accepted
**Date:** 2026-04-17
**Deciders:** Roman Głogowski (solo developer)
**Category:** API & Integration Architecture
**Related:** ADR-0059 (platform metrics exposition)

## Context

Target customers run K8s (ADR-0022) and overwhelmingly use Prometheus for metrics and Grafana for dashboards (PRD §4.8.1, §4.4.2). Status-page uptime rules and SLO computations benefit from pulling real metrics rather than relying solely on synthetic checks.

## Decision

Ship two mandatory MVP integrations:

- **Prometheus** — pull metrics from customer Prometheus endpoints to compute uptime and SLOs; support PromQL-defined uptime rules.
- **Grafana Cloud** — deep-link to customer dashboards, push incident annotations.

Other metric systems (Datadog, New Relic, Dynatrace, cloud-native monitors) are deferred.

## Rationale

- Hits the dominant stack in the target segment.
- Minimizes integrations to build for MVP while covering most customers.
- Aligns with Kartova's own metrics exposition choice (ADR-0059).

## Alternatives Considered

- **OpenTelemetry metrics (OTLP)** — excellent long-term direction; adoption outside of early movers still behind Prometheus.
- **Datadog / New Relic / Dynatrace** — commercial stacks; defer until demand is demonstrated.
- **Azure Monitor / CloudWatch** — tied to specific clouds; provider-generic Prometheus wins.

## Consequences

**Positive:**
- Out-of-the-box value for K8s-native customers
- Foundation for uptime-rule-based status page

**Negative / Trade-offs:**
- Customers using other stacks must wait
- Pull-based Prometheus requires network reachability or an agent proxy (see ADR-0041)

**Neutral:**
- OTLP / other integrations can be added without rework

## References

- PRD §4.8.1, §4.4.2
- Phase 6: Epic E-16
