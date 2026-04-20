# ADR-0071: Predefined 5-Level Maturity Model (Customizable Requirements)

**Status:** Accepted
**Date:** 2026-04-17
**Deciders:** Roman Głogowski (solo developer)
**Category:** Domain Model / Quality
**Related:** ADR-0069 (required fields), ADR-0070 (scorecards)

## Context

Platform engineering benefits from a shared vocabulary for "how production-ready is this service?" (PRD §4.13). Fully custom maturity models per tenant lose the cross-org benchmarks and shared vocabulary that make the concept useful. A single fixed model with fixed criteria fails to respect tenant diversity (ADR-0070). The sweet spot is a **fixed progression** with **customizable per-level criteria**.

## Decision

Kartova defines a **five-level maturity model** with opinionated, monotonically increasing progression:

1. **L1 Registered** — entity exists in the catalog with required fields (ADR-0069).
2. **L2 Documented** — description + README/docs linked + ownership confirmed.
3. **L3 Observable** — logs, metrics, and basic monitoring wired; health endpoints exposed.
4. **L4 Operationally Ready** — runbooks, on-call rotation, incident history, backup/restore posture.
5. **L5 Production-Grade** — full SLO + error budget tracking, chaos/DR tested, compliance evidence (where applicable).

The five levels and their order are **fixed**; the **requirements for passing each level** are configurable per organization via the scorecard framework (ADR-0070). Tenants choose which rules gate each level to match their quality bar.

## Rationale

- Fixed progression gives a shared vocabulary across tenants (benchmarks, vendor case studies, hiring).
- Customizable criteria respect the real variance in what "operationally ready" means per org.
- Five levels is the empirical sweet spot in industry practice — fewer collapses meaningful distinctions, more produces analysis paralysis.
- Progression is monotonic — no skipping, each level strictly includes prior levels.

## Alternatives Considered

- **Fully custom levels per org** — loses cross-org vocabulary; harder to benchmark; harder to ship templates.
- **Scorecard-only (no discrete levels)** — scorecards give a continuous score but lose the "is this production-grade?" simple answer teams want.
- **Different axis count (3, 7, 10)** — three too coarse; seven+ becomes ceremony.

## Consequences

**Positive:**
- Simple, opinionated progression the industry can recognize.
- Shared terminology across tenants for "is this ready?"
- Dashboards and reports work uniformly across organizations.

**Negative / Trade-offs:**
- The fixed axis may not match every org's existing maturity program; mapping is required.
- Moving a service down a level (regression) requires clear UX and notifications.

**Neutral:**
- Extension path: additional dimensions (e.g., security maturity) can be layered as separate scorecards.

## References

- PRD §4.13
- Phase 7: Epic E-17 (epic-level)
- Related ADRs: ADR-0069, ADR-0070
