# ADR-0043: Agent Deployable as Docker Container / K8s Deployment + Helm Chart

**Status:** Accepted
**Date:** 2026-04-17
**Deciders:** Roman Głogowski (solo developer)
**Category:** Agent Architecture
**Related:** ADR-0022 (K8s), ADR-0041 (.NET agent)

## Context

Customers run the agent inside their Kubernetes clusters (PRD §4.6.1). They expect standard packaging: a Docker image and a Helm chart. Non-K8s shapes (systemd, bare binary) are uncommon for the target segment.

## Decision

Primary agent shapes:

- **Docker image** — published to a public or tenant-accessible registry.
- **Helm chart** — deployed as `Deployment` for central discovery, `DaemonSet` when per-node metric collection is needed.

Secondary shapes (systemd, Kustomize, Operator) are considered post-MVP based on customer demand.

## Rationale

- Matches K8s-native customer base (ADR-0022).
- Helm is the dominant K8s package manager; low friction for customers.
- Two shapes (Deployment, DaemonSet) cover the two main workload patterns.

## Alternatives Considered

- **Bare systemd unit** — small fraction of target audience; low priority.
- **Sidecar pattern** — heavy coupling to each workload; rejected as default.
- **Kustomize only** — less common than Helm in enterprise.
- **Operator / CRD** — great long-term; scope for solo-dev MVP is too large.

## Consequences

**Positive:**
- Frictionless install via `helm install`
- Upgrade path via Helm release management

**Negative / Trade-offs:**
- Helm chart quality is a separate work item (values schema, linting, tests)
- Multi-arch image publishing required (linux-x64, arm64)

**Neutral:**
- Future Operator can layer on top of the same image

## References

- PRD §4.6.1
- Phase 6: E-15.F-01.S-01
