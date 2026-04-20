# ADR-0044: Centrally Managed Agent Config (Pull-Based)

**Status:** Accepted
**Date:** 2026-04-17
**Deciders:** Roman Głogowski (solo developer)
**Category:** Agent Architecture
**Related:** ADR-0041 (.NET agent), ADR-0042 (agent comms, pending)

## Context

Agents run inside customer clusters and may number in the hundreds across a single tenant. Pushing config changes requires inbound connectivity — forbidden by ADR-0042's outbound-only stance. Customers must be able to adjust agent behavior (scope, intervals, enabled modules) without redeploying (PRD §4.6.2).

## Decision

The agent polls the platform on a configurable interval (e.g., 60s) for its config document. Config changes apply without agent restart where possible (hot-reload for log level, intervals, enabled modules). Config is authored in the platform UI/API and versioned per agent or per agent-group.

## Rationale

- Pull model is firewall-friendly and matches ADR-0042.
- No agent restart needed for most changes — operationally gentle.
- Centralized config keeps fleet behavior consistent.

## Alternatives Considered

- **Push via long-lived gRPC stream** — requires persistent inbound or long-lived outbound stream; more complex failure handling.
- **ConfigMap + operator reload** — forces customer to manage config in their own GitOps flow.
- **Env-only config** — requires agent restart for any change.
- **GitOps (ArgoCD-owned)** — valid for sophisticated customers; too much friction for default path.

## Consequences

**Positive:**
- Firewall-friendly
- Hot reload minimizes disruption
- Centrally auditable

**Negative / Trade-offs:**
- Small delay between config change and effect (bounded by poll interval)
- Agent must be robust to partial/invalid configs

**Neutral:**
- Config delivery channel is the same outbound HTTPS used for telemetry

## References

- PRD §4.6.2
- Phase 6: E-15.F-01.S-03
