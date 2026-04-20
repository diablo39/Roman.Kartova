# ADR-0051: Multi-Channel Status Page Subscribers (Email, SMS, Webhook, RSS)

**Status:** Accepted
**Date:** 2026-04-17
**Deciders:** Roman Głogowski (solo developer)
**Category:** Status Page Architecture
**Related:** ADR-0023 (separate status cluster), ADR-0047 (notification engine), ADR-0053 (99.99% SLA)

## Context

Public and internal status pages exist to inform stakeholders when something breaks (PRD §4.4). Those stakeholders consume updates in different ways: executive audiences want email, on-call engineers want SMS, downstream systems want webhooks, aggregator tooling wants RSS. Providing only one channel forces subscribers to build their own scrapers — and feature-parity with Statuspage is a baseline expectation.

## Decision

Status page visitors can subscribe to incident/maintenance updates via four channels:

- **Email** — default; double opt-in.
- **SMS** — opt-in, via a pluggable SMS provider (Twilio or equivalent); restricted to incident severity "major" and above by default to control cost.
- **Webhook** — HMAC-signed payload (see ADR-0033 once resolved); retry + DLQ.
- **RSS/Atom** — anonymous; no subscription needed beyond the feed URL.

All outbound traffic is dispatched via the unified notification engine (ADR-0047) to reuse templating, throttling, and logging.

## Rationale

- Feature parity with Statuspage/Atlassian is table stakes for the status-page product.
- Different stakeholders have non-negotiable channel preferences.
- SMS handles the "urgent, person on-call" scenario where email/webhook are insufficient.
- RSS remains a low-cost, zero-auth integration path that many monitoring tools still prefer.

## Alternatives Considered

- **Email + RSS only** — misses enterprise buyers who expect SMS for major incidents.
- **Add push notifications** — deferred: requires a mobile app or service-worker infra beyond MVP scope.
- **Drop SMS to save cost** — rejected: severity-gated SMS keeps costs bounded while covering the on-call use case.
- **Integrate with PagerDuty only** — useful as a future webhook target, not a replacement for the four channels.

## Consequences

**Positive:**
- Covers all common subscriber preferences out of the box.
- Webhook enables downstream automation (chatops bridges, incident pipelines).
- RSS is effectively free.

**Negative / Trade-offs:**
- SMS introduces a new vendor dependency and per-message cost — requires per-tenant quotas and severity gating.
- Subscription management UI must handle four channels with preference per-channel.

**Neutral:**
- Additional channels (push, Slack-for-status-subs) can be added later via the dispatch engine.

## References

- PRD §4.4.1
- Phase 4: E-12.F-03.S-01
- Related ADRs: ADR-0023, ADR-0047, ADR-0053
