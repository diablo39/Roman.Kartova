# ADR-0079: Dogfooding + Design Partners Go-to-Market Strategy

**Status:** Accepted
**Date:** 2026-04-20
**Deciders:** Roman Głogowski (solo developer)
**Category:** Non-Functional / Go-to-Market
**Related:** ADR-0026 (fully proprietary), ADR-0025 (CI/CD), ADR-0074 (scale targets)

## Context

A dev portal product is only credible if it has been used to manage real services. A solo founder cannot fake this — onboarding real users too early exposes brutal UX gaps; onboarding too late means the product is validated only against imagined use cases. Kartova needs a staged validation strategy that produces real feedback at each maturity tier without burning credibility on a rough early product (PRD §11 Resolved Decision #10, §12).

## Decision

**Three-phase go-to-market validation:**

1. **Dogfooding (Phases 0–2):** Kartova is the first production user of Kartova. The platform catalogs its own services, documentation, dependencies, and incidents from the moment the catalog MVP boots. Every friction point surfaces to the one person who can fix it.

2. **Design Partners (Phases 2–3, 2–3 partners):** Once Phases 2–3 are functional (catalog + scan + docs + basic notifications), recruit 2–3 design partners in target mid-market segments (ADR-0074 envelope). Terms:
   - Free or heavily discounted usage in exchange for structured feedback.
   - Named product roadmap influence; biweekly sync cadence.
   - Willingness to be referenced publicly (case study) at GA.
   - Self-selected for "developer-heavy, multi-service, frustrated with their current portal (or lack thereof)."

3. **Closed Beta (post-Phase 3):** Expand to 10–20 orgs via invite. Pricing becomes real. Onboarding must be self-service. This is the transition from "product-building mode" to "product-selling mode."

4. **General Availability:** Triggered by closed-beta customers renewing for a second term with money and attainment of Phases 4–5 scope.

**Not doing:**
- **Open beta from day one** — burns reputation on a product that isn't ready.
- **Paid beta** — friction too high for a product without a reference customer.
- **Enterprise-pilot-only** — enterprise procurement cycles are incompatible with solo-dev iteration velocity.

## Rationale

- Dogfooding compresses the feedback loop to hours — the fastest possible learning signal.
- 2–3 design partners is the empirically correct number: enough to validate generalization beyond self, few enough to personally support as a solo dev.
- Closed beta with pricing is the only way to validate willingness-to-pay, which is the only commercial metric that matters pre-Series-A.
- Staged rollout limits blast radius while the product is still rough.
- Matches category-standard playbooks (Linear, PostHog, Supabase all shipped with ~similar validation ladders).

## Alternatives Considered

- **Public open beta from day one** — optimizes for top-of-funnel metrics at the cost of retention; wrong trade for a complex dev-tool product.
- **Paid beta (100% price from day one)** — effectively enterprise-sales motion with no enterprise infrastructure; grinds to a halt.
- **Enterprise pilot only (one big customer)** — concentrates all product-direction risk in one customer's biases; classic v1 failure mode.
- **No formal staging (build for general audience from day one)** — under-informed by real usage; generic-by-default product.

## Consequences

**Positive:**
- Real production usage informs the product from Phase 0 — UX gaps surface immediately.
- Design-partner references become early sales collateral.
- Pricing is pressure-tested on real buyers before public launch.
- Conservative rollout protects brand reputation during the inevitable rough early months.

**Negative / Trade-offs:**
- Revenue is intentionally minimal pre-GA — plan runway accordingly.
- Design-partner acquisition requires solo-dev networking / outbound effort (non-trivial time cost).
- Dogfooding requires treating one's own toolchain seriously from day one — no "I'll migrate later."

**Neutral:**
- Exact transition triggers (Phase 2 → design partners, Phase 3 → closed beta) are directional; gated by subjective "is this actually usable?" judgment.
- Design partners may churn or underperform; recruitment pipeline assumes 3x top-of-funnel vs target partner count.

## References

- PRD §11 (Resolved Decisions, #10 Beta strategy)
- PRD §12 (Go-to-Market plan)
- Related ADRs: ADR-0026 (proprietary product), ADR-0074 (scale targets), ADR-0025 (CI/CD for iteration velocity)
