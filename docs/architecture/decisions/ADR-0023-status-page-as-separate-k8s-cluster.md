# ADR-0023: Status Page Deployed as Separate K8s Cluster/Namespace

**Status:** Accepted
**Date:** 2026-04-17
**Deciders:** Roman Głogowski (solo developer)
**Category:** Platform Infrastructure
**Related:** ADR-0005 (replica), ADR-0022 (K8s), ADR-0076 (two-tier SLA)

## Context

Status page SLA target is 99.99% vs platform 99.9% (PRD §7.2, ADR-0076). A status page sharing its cluster, database, and identity plane with the platform it monitors cannot meet a stricter SLA.

## Decision

Deploy the status page service to a separate Kubernetes cluster (or, as a minimum, a separate namespace on separate nodes in a separate availability zone) with its own ingress, its own data replica (ADR-0005), and its own scaling. The status page must remain reachable and renderable even if the main platform cluster is fully unreachable.

## Rationale

- Independent failure domain is the only way to credibly exceed the monitored system's SLA.
- Matches customer expectations — a status page that goes down during an incident is worthless.

## Alternatives Considered

- **Same cluster, different nodes** — shares control plane, DNS, ingress failure modes.
- **Fully external static hosting (Netlify / Cloudflare Pages + API)** — viable for public page, insufficient for authenticated internal pages (ADR-0010) and incident updates.
- **Shared infrastructure** — cheapest but defeats the SLA target.

## Consequences

**Positive:**
- Meets the 99.99% SLA aspirationally
- Isolates status-page traffic from platform pressure

**Negative / Trade-offs:**
- Operating two clusters raises baseline cost and ops overhead
- Replication pipeline (ADR-0005) must be monitored separately

**Neutral:**
- Public pages can be additionally fronted by a CDN for further resilience

## References

- PRD §7.5, §8, §11 (Resolved Decision #6)
- Phase 4: E-12.F-05.S-01
