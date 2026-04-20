# ADR-0049: Configurable SMTP / Email Provider for Outbound Email

**Status:** Accepted
**Date:** 2026-04-17
**Deciders:** Roman Głogowski (solo developer)
**Category:** Notification Architecture
**Related:** ADR-0047 (notification engine), ADR-0050 (notification record)

## Context

Email remains the most universal notification channel (incident alerts, digest emails, invitations, MiFID-relevant records). Kartova must deliver reliable transactional email with bounce/complaint handling and unsubscribe mechanics (PRD §4.7, Phase 1 E-06a.F-01.S-03). Locking into a single vendor creates portability risk and complicates on-prem deployments.

## Decision

The email channel adapter targets a configurable SMTP/email-provider interface. A thin abstraction supports:

- **Generic SMTP** (default, works with any provider and on-prem relays).
- **Native provider adapters** (AWS SES, SendGrid, Mailgun, Postmark) where vendor-specific features are beneficial (event webhooks for bounces/complaints).

Per deployment (and, eventually, per tenant), administrators select the provider and configure credentials. Templates are rendered by the notification engine (ADR-0047); the adapter only deals with transport, bounce feedback, and suppression lists.

## Rationale

- Avoids vendor lock-in; supports on-prem/sovereign-cloud deployments.
- Generic SMTP gives zero-config fallback for small deployments.
- Native adapters preserve bounce/complaint signals that are lost over pure SMTP.
- Keeps the engine testable with a local MailHog/Papercut in dev.

## Alternatives Considered

- **Pick one vendor (e.g., SES-only)** — cheap and simple, but blocks on-prem and non-AWS deployments.
- **Self-hosted SMTP (Postfix)** — unacceptable deliverability/reputation management burden for solo dev.
- **SNS + vendor** — adds AWS dependency for no clear gain.
- **No email (webhook/chat-only)** — fails for invitations, password flows, and regulated communication records.

## Consequences

**Positive:**
- Deployment flexibility (cloud / on-prem / customer-provided SMTP).
- Bounce/complaint handling still possible via provider adapters.
- Unsubscribe and list hygiene handled centrally in the engine.

**Negative / Trade-offs:**
- Maintaining multiple adapters means testing against each provider.
- DMARC/DKIM/SPF configuration is customer's responsibility for their domain.

**Neutral:**
- Provider choice may influence deliverability — we document recommended providers.

## References

- Phase 1: E-06a.F-01.S-03
- Related ADRs: ADR-0047, ADR-0050
