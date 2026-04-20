# ADR-0063: User-Count Metering Per Billing Period

**Status:** Accepted
**Date:** 2026-04-20
**Deciders:** Roman Głogowski (solo developer)
**Category:** Billing
**Related:** ADR-0062 (external billing provider), ADR-0008 (five fixed RBAC roles), ADR-0009 (service account JWT), ADR-0010 (status page auth)

## Context

Kartova's pricing model is per-user/month (PRD §6.2, ADR-061 pending). This requires a reliable mechanism to count *billable* users per org per billing period — distinct from total registered users. The definition of "billable user" has material revenue and fairness implications: service accounts shouldn't be billed (they're automation, not seats), and public status-page viewers aren't users at all.

## Decision

Each org's **active user count** is metered **per billing period** (typically monthly) and reported to the billing provider (ADR-0062) at period close.

**Billable users are:**
- Human users with any of the five RBAC roles (ADR-0008) who had **at least one authenticated session** during the billing period.

**Explicitly excluded from the count:**
- **Service accounts** (ADR-0009) — these are machine identities for CI/CD and agents; counting them would discourage automation.
- **Public status page viewers** (ADR-0010) — unauthenticated; not users in any product sense.
- **Internal (authenticated) status page subscribers** who have no other role in the platform — pure read-only consumers of incident data; considered covered by the base subscription of their org.
- **Suspended / disabled users** — no platform access during the period.

Metering mechanism:
- User activity is tracked via authenticated session events (already captured for audit — ADR-0018).
- A daily job updates a `billing_period_active_users` materialized count per org.
- At period close, the final count is pushed to the billing provider via its metered-usage API; the provider prices and invoices.
- Mid-period adds/removes are reflected via usage reporting (provider handles proration).

## Rationale

- Per-active-user metering is fairer than named-seat billing: customers don't pay for ghosts.
- Excluding service accounts aligns incentives — automation should be encouraged, not taxed.
- Using authentication events (already instrumented) avoids building a separate metering pipeline.
- Delegating proration to the billing provider avoids reimplementing calendar math.

## Alternatives Considered

- **Named seats (pay whether used or not)** — simpler but penalizes infrequent users and creates dead-seat bloat.
- **Peak concurrent users** — cheaper for customers with bursty usage, harder to explain, easier to game.
- **Daily-active-user average** — smoother but more expensive to compute and explain; marginal fairness improvement vs "touched once this period."
- **Include service accounts** — disincentivizes automation, which is core to product value.
- **Per-API-call or per-entity metering** — incompatible with chosen pricing model (ADR-061).

## Consequences

**Positive:**
- Fair, legible billing aligned with actual usage.
- Automation (CI, agents) is free to scale without billing penalties.
- Reuses existing auth-event infrastructure.

**Negative / Trade-offs:**
- "Once per period" threshold creates a cliff: a user who logs in once costs the same as a power user. Mitigated by the chosen per-user flat rate.
- Requires the billing provider to support metered/usage-based subscription items (most do).
- Edge case: users deleted mid-period still counted if they logged in. Documented behavior.

**Neutral:**
- Future pricing experiments (e.g., tiered by role) are possible without changing the metering substrate — just the reporting slicing.

## References

- PRD §6.2 (Pricing & Monetization)
- Phase 5: E-14a.F-01.S-01
- Related ADRs: ADR-0062 (billing provider), ADR-0008 (RBAC roles), ADR-0009 (service accounts), ADR-0010 (status page auth), ADR-0018 (audit log)
