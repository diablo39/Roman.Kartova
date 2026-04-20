# ADR-0047: Unified Multi-Channel Notification Dispatch Engine

**Status:** Accepted
**Date:** 2026-04-17
**Deciders:** Roman Głogowski (solo developer)
**Category:** Notification Architecture
**Related:** ADR-0048 (Slack/Teams), ADR-0049 (SMTP/email), ADR-0050 (notification record), ADR-0033 (webhooks, pending)

## Context

Many platform features produce user-facing notifications: scorecard regressions, risk alerts, drift warnings, breaking API changes, status page subscribers, incident updates, and agent events (PRD §4.7). If each feature builds its own delivery code, we get duplicated retry logic, divergent audit trails, and inconsistent formatting across channels. A unified dispatch engine is the obvious abstraction.

## Decision

A single notification dispatch engine handles all outbound notifications across all channels: in-app, email, webhook, Slack, Microsoft Teams, RSS, and SMS (where enabled). Feature modules raise typed notification events; the engine resolves the recipient set, per-event-type routing rules, per-user channel preferences, and per-tenant throttling, then dispatches via channel adapters. Templates and localization live in the engine; every dispatch is logged (ADR-0050).

## Rationale

- Eliminates duplicate retry/backoff/DLQ logic across features.
- Centralizes MiFID II communication-record capture (ADR-0050) — a single place to guarantee retention.
- User notification preferences (per event type × per channel) are meaningful only when centrally orchestrated.
- Adding a new feature that needs notifications reduces to "raise an event + ship a template."

## Alternatives Considered

- **Per-feature notifiers** — rejected: duplication, inconsistent auditability, hard to globally throttle.
- **Third-party service (Courier, Knock, Novu)** — rejected for MVP: data-residency complications, MiFID retention pushes log of record back to us anyway, and we already need in-app + webhook internally.
- **SendGrid Marketing or similar vendor SaaS** — addresses email only, not the multi-channel need.

## Consequences

**Positive:**
- Single chokepoint for retries, DLQ, throttling, compliance logging.
- Consistent templating and localization across features.
- Trivial to add new channels — write one adapter.

**Negative / Trade-offs:**
- The engine becomes a critical path; incidents here impact many features.
- Adapter abstractions must accommodate channel-specific formatting (e.g., adaptive cards, markdown, HTML, plain text).

**Neutral:**
- Template versioning and A/B testing become a future product surface.

## References

- PRD §4.7
- Phase 1: Epic E-06a (feature-level)
- Related ADRs: ADR-0048, ADR-0049, ADR-0050
