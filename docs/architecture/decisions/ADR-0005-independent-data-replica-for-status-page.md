# ADR-0005: Independent Data Replica for Status Page

**Status:** Accepted
**Date:** 2026-04-17
**Deciders:** Roman Głogowski (solo developer)
**Category:** Data Platform / HA
**Related:** ADR-0023 (status page topology), ADR-0076 (two-tier SLA)

## Context

Status page SLA target is 99.99%, higher than the main platform's 99.9% (PRD §7.2). To honor this during a platform outage, the status page must not share a failure domain with the main platform's PostgreSQL/Elasticsearch/KeyCloak stack. Eventual consistency of less than 30 seconds is acceptable (PRD §7.5).

## Decision

Status page reads from an independent data replica (or cache) asynchronously replicated from the main platform. The replica holds only the data the status page needs: components, incidents, maintenance windows, uptime history, subscriber lists. Replication is one-way, tolerates brief disconnection, and replays on reconnect.

## Rationale

- Removes coupling between main-platform availability and status page availability.
- Async replication with <30s lag satisfies product requirement while avoiding synchronous-replica cost and complexity.
- Enables status page to run in its own cluster/namespace (ADR-0023).

## Alternatives Considered

- **Shared DB with read replica** — same failure domain if primary cluster/network dies.
- **Redis/KeyDB cache only** — insufficient for incident history, subscriber management, auth.
- **Static snapshot to S3/CDN** — cannot serve real-time incident updates or authenticated internal pages.
- **Event-sourced projection** — viable longer term, over-engineering for MVP.

## Consequences

**Positive:**
- Status page survives main-platform outage (primary goal)
- Isolation of read traffic from primary DB

**Negative / Trade-offs:**
- Additional datastore to operate and monitor
- Replication lag must be observable and bounded
- Writes from status page UI (subscriber opt-in, incident acknowledgements if any) need careful routing

**Neutral:**
- Exact technology (logical replication, change-data-capture, or app-level event stream) is a Phase 4 implementation detail

## References

- PRD §7.5
- Phase 4: E-12.F-05.S-01, E-12.F-05.S-02
