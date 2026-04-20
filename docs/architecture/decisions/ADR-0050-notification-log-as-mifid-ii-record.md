# ADR-0050: Notification Log Retained as MiFID II Communication Record

**Status:** Accepted
**Date:** 2026-04-17
**Deciders:** Roman Głogowski (solo developer)
**Category:** Notification Architecture / Compliance
**Related:** ADR-0016 (MiFID II), ADR-0017 (retention), ADR-0018 (audit log), ADR-0047 (notification engine)

## Context

MiFID II Article 16(7) requires investment firms to retain records of all communications "intended to result in a transaction" for 5 years (PRD §7.3). Kartova serves financial-sector tenants; platform-generated notifications (incidents, breaking-change alerts, scorecard regressions delivered to engineers who operate trading systems) can qualify as communication records. Without first-class record-keeping, tenants would have to bolt on external archival — defeating the compliance-from-day-one principle (ADR-0016).

## Decision

Every outbound notification — across all channels (email, webhook, Slack, Teams, SMS, in-app, RSS) — is persisted in a `notification_log` table with:

- Full rendered payload (subject + body, channel-specific formatting preserved).
- Recipient(s), channel, template ID + version, correlation ID, tenant ID.
- Send result (delivered / bounced / failed) with provider-returned message ID.
- Timestamps: queued, sent, delivered.

Retention follows ADR-0017: 180 days default, **5 years for tenants flagged as MiFID II regulated**. Records are append-only and referenced from the audit log (ADR-0018). Log of record is the platform's own store, even when a third-party provider is used for transport (ADR-0049).

## Rationale

- Compliance-from-day-one (ADR-0016) requires the record to exist by design, not by bolt-on.
- Centralizing record-keeping in the notification engine (ADR-0047) ensures 100% coverage across features.
- Own-store log-of-record avoids dependency on provider retention policies that can change.
- Supports GDPR right-of-access (subject's notifications are queryable).

## Alternatives Considered

- **Metadata-only logging** — fails MiFID II requirement to retain content.
- **Offload to external WORM store** — adds a dependency; still requires our indexing for search.
- **Shorter retention for non-financial tenants** — adopted via ADR-0017's tenant flag, not as an alternative to logging itself.
- **Rely on provider archives (SES suppression/activity)** — provider retention is insufficient and not guaranteed.

## Consequences

**Positive:**
- Single compliant record store for all outbound comms.
- Audit and GDPR DSAR can both query the same table.
- Tenants can self-serve communications search.

**Negative / Trade-offs:**
- Storage growth: full payloads × 5 years for MiFID tenants is non-trivial → cold-storage archival after active window (ADR-0020).
- PII in payloads requires encryption at rest and residency tracking (ADR-0021).

**Neutral:**
- Retention policy is per-tenant and configurable within compliance bounds.

## References

- PRD §7.3, §11 (Resolved Decision — MiFID II)
- Phase 0: E-01.F-05.S-07
- Phase 1: E-06a.F-01.S-01
- Related ADRs: ADR-0016, ADR-0017, ADR-0018, ADR-0020, ADR-0021, ADR-0047
