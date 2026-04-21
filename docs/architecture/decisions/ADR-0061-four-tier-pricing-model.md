# ADR-0061: Four-Tier Pricing Model (Free / Starter / Pro / Enterprise)

**Status:** Accepted
**Date:** 2026-04-21
**Deciders:** Roman Głogowski (solo developer)
**Category:** Billing
**Related:** ADR-0016 (MiFID II compliance), ADR-0017 (retention policy), ADR-0062 (external billing provider), ADR-0063 (user-count metering), ADR-0076 (SLA tiers), ADR-0079 (dogfooding + design partners GTM)

## Context

Kartova's original PRD (§6.2) proposed flat per-user monthly pricing: one price, all features, linear scaling. While simple, this approach has weaknesses at scale:

- **No free tier** discourages bottom-up adoption by individual developers and small teams who would become product champions
- **No enterprise plan** makes it hard to capture high-value tenants (MiFID II regulated, fintech, 200+ users) who are willing to pay for premium features (5-year retention, 99.99% SLA, dedicated support) at substantially higher per-user rates
- **Single price point** forces a compromise between too-high (loses SMB) or too-low (under-prices enterprise value)
- **No feature differentiation** means Kartova gives premium features (Status Page, CLI policy engine, DX Score, Risk Score, Tech Radar, Cost Attribution) away at the base price — 2-3x LTV reduction vs tiered models per industry benchmarks (OpenView SaaS pricing data)

A clear tiering strategy is also needed to align with:
- **MiFID II compliance (ADR-0016)** — 5-year retention implies meaningful additional cost, reasonably captured in an Enterprise plan
- **Go-to-market strategy (ADR-0079)** — dogfooding and design partners benefit from a Free tier for validation and community building
- **Target personas** from PRD §2 — developers (individual), DevOps engineers (team), engineering managers (organization/enterprise)

## Decision

Kartova launches with a **four-tier pricing model**: Free, Starter, Pro, Enterprise. Per-user monthly pricing with minimum seat counts on paid tiers. Feature access and retention vary by tier.

**Tier structure:**

| Plan | Price | Minimum seats | Max users | Max entities | Retention | SLA |
|------|-------|---------------|-----------|--------------|-----------|-----|
| **Free** | $0 | — | 5 | 25 | 30 days | Best effort |
| **Starter** | $10 / user / month | 5 | Unlimited | Unlimited | 180 days | 99.5% |
| **Pro** | $25 / user / month | 10 | Unlimited | Unlimited | 180 days | 99.9% |
| **Enterprise** | Custom (contact sales) | Negotiated | Unlimited | Unlimited | 5 years (MiFID II) | 99.99% |

**Feature gating by tier:**

| Feature | Free | Starter | Pro | Enterprise |
|---------|------|---------|-----|------------|
| Core catalog (entities, relationships, hierarchy, tags) | ✓ | ✓ | ✓ | ✓ |
| Auto-import (single repository) | ✓ | ✓ | ✓ | ✓ |
| Documentation sync from Git | ✓ | ✓ | ✓ | ✓ |
| Embedded mini dependency graph | ✓ | ✓ | ✓ | ✓ |
| Scorecards | ✓ (1 scorecard) | ✓ | ✓ (multiple) | ✓ |
| Bulk import + scheduled re-scan | | ✓ | ✓ | ✓ |
| Standalone dependency graph + impact analysis | | ✓ | ✓ | ✓ |
| Slack/Teams notifications | | Webhook only | Native integrations | Native integrations |
| Maturity model | | Basic | Customizable | Customizable |
| **Public Status Page** | | | ✓ | ✓ (with custom domain + SSL) |
| **CLI + Policy enforcement** | | | ✓ | ✓ |
| **DX Score + Risk Score** | | | ✓ | ✓ |
| **Tech Radar + Cost Attribution** | | | ✓ | ✓ |
| Hybrid agent (service discovery) | | | ✓ | ✓ |
| SSO (SAML/OIDC via KeyCloak) | | | Basic | Premium (custom IdP) |
| MiFID II compliance flag (5-year retention) | | | | ✓ |
| Priority support | | Email | Priority email | Dedicated CSM |
| Volume discounts | | | | Negotiated |

**Billable user definition (consistent with ADR-0063 user metering):**
- Active user = any human who authenticated during the billing period
- **Excluded from billing:** service accounts (ADR-0009), public status page visitors, users with Viewer role on the Free tier only

**Grace and overage behavior:**
- Free tier exceeding 5 users → prompt to upgrade to Starter; no hard lockout for 7 days, then read-only mode until upgrade or user removal
- Paid tier exceeding minimum seats → billed for actual count that month
- Free tier exceeding 25 entities → new entity creation blocked; existing data remains accessible

**Migration path from flat pricing (PRD original model):**
Since Kartova has not yet launched, no existing customers to migrate. Design partners (ADR-0079) receive complimentary Pro tier access during the design-partner stage; at GA they either continue on a negotiated Enterprise plan or transition to the published pricing.

## Rationale

- **Free tier drives bottom-up adoption** — developers experiment on personal projects, become champions inside their companies, generate leads for Pro/Enterprise conversions
- **Starter serves self-service small teams** — 5-20 person engineering teams can pay by credit card, no sales cycle; captures revenue Kartova would otherwise leave on the table between Free and Pro
- **Pro is the revenue backbone** — differentiators (Status Page, CLI policy engine, DX Score, Risk Score, Tech Radar, Cost Attribution, hybrid agent) gate behind Pro to justify the price jump from Starter; this is Kartova's main ARR driver for mid-market
- **Enterprise captures MiFID II value** — 5-year retention (ADR-0017) imposes real storage cost and legal risk for Kartova; Enterprise pricing must reflect this; dedicated support and custom SLA justify premium pricing to fintech and regulated-industry customers
- **Per-user + minimum seats** — simple mental model (counting people) combined with revenue floor protection (a 1-person team on Starter still pays for 5 seats)
- **Tier isolation via feature flags** — implementation complexity is low; feature-gate decisions map to an entitlements table keyed by tenant plan; EF Core interceptors and React feature-flag library enforce at both layers
- **Prevents premature pricing compromise** — flat pricing at any single price point under-monetizes enterprise and over-prices SMB; tiered model optimizes both ends

## Alternatives Considered

- **Flat per-user pricing (original PRD §6.2):** Simple to implement and explain but leaves 2-3x LTV on the table per industry benchmarks. No free tier kills bottom-up adoption. No enterprise tier caps the top of the price curve. Rejected in favor of tiered model.
- **Three-tier model (Free / Pro / Enterprise) — no Starter:** Simpler to maintain but creates a large price jump from $0 to $25/user/mo, losing small teams (5-20 users) who find Pro overkill and can't stay on Free. Starter captures this segment. Considered for MVP simplification but rejected — the Starter tier adds meaningful revenue with minimal engineering cost (same entitlement table, one more row).
- **Usage-based hybrid (base + usage metrics):** Unpredictable monthly bills alienate enterprise procurement teams; SaaS data shows most enterprises reject pure consumption billing for infrastructure tools. Could work for analytics/AI products; not for catalog tools. Rejected.
- **Per-entity pricing:** Anti-pattern — disincentivizes users from registering entities, the exact behavior Kartova needs to encourage for product success. Rejected.
- **Flat-rate per-org (unlimited users, one price per tenant):** Great for customers but unpredictable revenue for Kartova — large tenants pay the same as small ones. Could make enterprise sense (Enterprise tier effectively becomes this) but not for Starter/Pro. Rejected as a universal model.

## Consequences

**Positive:**
- Captures revenue across the full spectrum from individual developers ($0) to regulated enterprises (custom pricing)
- Free tier seeds top-of-funnel and builds community
- Pro and Enterprise tiers differentiate Kartova's high-value features (Status Page, CLI, DX Score, MiFID II) at appropriate price points
- Clear upsell path: Free → Starter → Pro → Enterprise; each jump is justified by visible additional value
- MiFID II retention cost is recovered at Enterprise tier rather than subsidized by all customers
- Matches the three-stage go-to-market (ADR-0079): dogfood (Free internal) → design partners (complimentary Pro) → closed beta → public launch with full pricing

**Negative / Trade-offs:**
- Four feature matrices to maintain: more code for entitlement checks, more complex product documentation, more ways pricing pages can confuse prospects
- Customer support must understand which tier a caller is on to answer questions about feature availability
- Feature flag infrastructure becomes load-bearing (though same infrastructure ADR-0081 future if implemented)
- Free tier abuse risk (tenants creating multiple free orgs to circumvent user limits) — mitigated by enforcing one Free org per billable email domain and abuse-detection heuristics
- Minimum-seat pricing can be a friction point for very small teams — documented clearly on pricing page with FAQ
- Pro-to-Enterprise conversion requires a sales motion; solo developer must build enterprise sales capability (or partner with an agency) to capture Enterprise revenue

**Neutral:**
- Requires an entitlements table (`tenant_id`, `plan`, `features`, `limits`) in PostgreSQL — trivial schema addition
- Pricing pages, billing dashboard (ADR-0063), invoice generation (ADR-0062), and subscription management UI all need tier-aware rendering — expected scope
- PRD §6.2 must be updated from "simple per-user pricing" to reflect the tiered model
- Phase 5 E-14a (Billing & Subscription Management) stories expand to cover plan selection, upgrades, downgrades, overage handling

## Implementation Notes

**Entitlements table (PostgreSQL):**

```sql
CREATE TABLE plans (
  id TEXT PRIMARY KEY,                    -- 'free' | 'starter' | 'pro' | 'enterprise'
  display_name TEXT NOT NULL,
  price_per_user_cents INT,               -- NULL for enterprise (custom)
  minimum_seats INT NOT NULL DEFAULT 0,
  max_users INT,                          -- NULL = unlimited
  max_entities INT,                       -- NULL = unlimited
  retention_days INT NOT NULL,            -- 30, 180, 1825 (5 years)
  sla_target NUMERIC(5,3) NOT NULL,       -- 0.995, 0.999, 0.9999
  features JSONB NOT NULL                 -- { "status_page": true, "cli_policy": true, ... }
);

CREATE TABLE tenant_subscriptions (
  tenant_id UUID PRIMARY KEY REFERENCES tenants,
  plan_id TEXT NOT NULL REFERENCES plans,
  mifid_ii_flag BOOLEAN NOT NULL DEFAULT FALSE,  -- Enterprise only
  custom_sla_percent NUMERIC(5,3),               -- Enterprise overrides
  seat_count INT NOT NULL,
  billing_cycle_anchor TIMESTAMPTZ NOT NULL,
  stripe_subscription_id TEXT
);
```

**Feature gating pattern (C#):**

```csharp
[RequiresFeature("status_page")]
public async Task<IActionResult> GetStatusPage(Guid tenantId) { ... }

// Filter reads entitlements; returns 402 Payment Required with upgrade prompt
```

**Pricing page structure (React):**
- Four tier cards side-by-side, comparison matrix below
- "Start Free" CTA on Free tier (email-only signup)
- "Start 14-day Pro trial" CTA on paid tiers (credit card required)
- "Contact Sales" CTA on Enterprise

## References

- PRD §6.2 (billing model — to be updated), §2 (target personas), §11 (Resolved Decision #10 — beta/early access strategy)
- Phase 5 E-14a (Billing & Subscription Management)
- OpenView SaaS pricing benchmarks: https://openviewpartners.com/pricing
- SaaS tiered vs flat pricing analysis: https://www.priceintelligently.com/blog/saas-pricing-models
