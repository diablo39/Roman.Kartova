# ADR-0062: External Billing Provider for Payment Processing

**Status:** Accepted
**Date:** 2026-04-20
**Deciders:** Roman Głogowski (solo developer)
**Category:** Billing
**Related:** ADR-0063 (user-count metering), ADR-0015 (GDPR), ADR-0016 (MiFID II), ADR-0021 (data residency)

## Context

Kartova is a commercial SaaS product with recurring subscription revenue (PRD §6.2). Implementing a billing backend — payment-method tokenization, recurring charges, card network compliance (PCI-DSS), dunning, invoicing, VAT handling across EU jurisdictions, tax reporting — is a massive undertaking, entirely unrelated to the platform's core value proposition. Solo-dev bandwidth cannot absorb this scope, and getting billing wrong creates both legal exposure and revenue leakage.

## Decision

Integrate a **third-party billing provider** (Stripe-style SaaS billing platform) for all payment processing, subscription management, and invoicing. Kartova owns:

- Metering (user counts per org per billing period — ADR-0063).
- Plan configuration and entitlements in the product (feature flags, quotas).
- Customer portal for billing history (delegated to the provider's hosted portal where possible).

The billing provider owns:

- PCI-DSS scope (card data never touches Kartova infrastructure).
- Subscription lifecycle (trials, upgrades, downgrades, proration).
- Invoice generation, delivery, and PDF archival.
- VAT/sales-tax calculation and reporting.
- Dunning (failed-payment retries, notifications, grace periods).

Candidate providers: **Stripe** (default), **Paddle** (merchant-of-record option — simplifies VAT), **Chargebee**, **Recurly**. Final provider selection deferred until closer to Phase 5; the integration is abstracted behind a `IBillingProvider` port so the choice is reversible.

Webhooks from the billing provider drive subscription-state changes in the platform (activation, suspension on payment failure, cancellation).

## Rationale

- PCI-DSS compliance is achieved "for free" via the provider — a solo dev cannot credibly self-attest to PCI-DSS Level 1.
- VAT/tax compliance across ≥27 EU member states is effectively unsolvable in-house; merchant-of-record providers (Paddle) remove this liability entirely.
- Provider feature depth (proration, coupons, seat-based metering APIs, dunning) dwarfs what can be rebuilt.
- Integration effort is measured in weeks, not quarters.
- Abstracting behind a port preserves optionality (Stripe ↔ Paddle switch).

## Alternatives Considered

- **Manual invoicing (PDFs + bank transfer)** — viable for 1–2 design partners; impossible at scale.
- **Build billing in-house** — 6+ months of work, permanent maintenance burden, PCI liability.
- **Open-source billing (Kill Bill)** — self-hosted complexity; still doesn't solve tax compliance.
- **Single-provider lock-in (no abstraction)** — faster initial build but risky given regulatory landscape shifts (DSA, MoR mandates).

## Consequences

**Positive:**
- Zero PCI-DSS scope on Kartova infrastructure.
- VAT/tax compliance largely outsourced (especially under MoR model).
- Solo-dev bandwidth preserved for product differentiation.
- Production-grade dunning, proration, and invoicing from day one.

**Negative / Trade-offs:**
- Revenue-share / transaction fees (typically 2–4% of GMV) are a permanent COGS line.
- Vendor lock-in risk — mitigated by the port abstraction but never eliminated.
- Data-residency constraints for EU customers require choosing providers with EU data hosting (ADR-0021).
- Webhook reliability becomes a critical path for subscription state; must be monitored and idempotent.

**Neutral:**
- Provider choice (Stripe vs Paddle vs Chargebee) deferred until Phase 5 scoping; the abstraction layer is the load-bearing decision now.

## References

- PRD §6.2 (Pricing & Monetization)
- Phase 5: E-14a.F-01.S-02
- Related ADRs: ADR-0063 (metering), ADR-0015 (GDPR), ADR-0016 (MiFID II), ADR-0021 (data residency)
