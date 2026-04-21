# ADR-0022: Kubernetes, Cloud-Agnostic Deployment

**Status:** Accepted
**Date:** 2026-04-17
**Deciders:** Roman Głogowski (solo developer)
**Category:** Platform Infrastructure
**Related:** ADR-0023 (status page topology), ADR-0043 (agent deployment), ADR-0085 (migration Jobs), ADR-0086 (Helm chart location)

## Context

Kartova's customers are largely K8s-native (PRD §4.6, §8). Hosting strategy affects customer trust (data residency, sovereignty), pricing flexibility, and the ability to support private-cloud deployments if customers later demand it.

## Decision

Deploy Kartova on Kubernetes using cloud-agnostic building blocks (standard K8s APIs, Helm charts, Ingress, cert-manager). Avoid managed services that lock the platform to a single cloud — prefer self-hosted or portable alternatives (e.g., PostgreSQL via operator or the cloud's managed flavor behind a consistent interface).

## Rationale

- K8s matches customer environments — same skills apply to Kartova's own agent work (ADR-0043).
- Cloud portability opens future options (EU-sovereign clouds, on-prem enterprise deployments).
- Standardized artifacts (Helm charts) usable by customers for self-hosted tier if that ever becomes a product line.

## Alternatives Considered

- **Single cloud + managed services (Azure Container Apps, AWS ECS, GCP Cloud Run)** — fast to ship, but vendor lock-in and limited portability.
- **Serverless (Lambda/Functions)** — cold-start latency hurts p95 targets (ADR-0075); limits long-running workloads (scans, agent ingestion).
- **Nomad** — smaller ecosystem; no competitive advantage over K8s.

## Consequences

**Positive:**
- Cloud portability preserved
- Skills transfer between product and customer deployments

**Negative / Trade-offs:**
- K8s operational burden on a solo dev — must lean heavily on managed control planes (EKS/AKS/GKE) while keeping workloads portable
- Some operational concerns (DB HA, backup, logging pipelines) must still be solved

**Neutral:**
- The status page is deliberately deployed separately (ADR-0023)

## References

- PRD §7.5, §8, §11 (Resolved Decision #5)
