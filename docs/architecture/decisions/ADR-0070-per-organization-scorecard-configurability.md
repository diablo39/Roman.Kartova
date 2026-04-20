# ADR-0070: Per-Organization Scorecard Configurability

**Status:** Accepted
**Date:** 2026-04-17
**Deciders:** Roman Głogowski (solo developer)
**Category:** Domain Model / Quality
**Related:** ADR-0069 (required fields), ADR-0071 (maturity model), ADR-0038 (plugins deferred)

## Context

Different organizations have different quality bars. A fintech org may require SOC 2 evidence on every service; a media startup may only care about README completeness; a healthcare org may require HIPAA attestations. Platform-fixed scorecards force a lowest-common-denominator or irrelevant rules. Fully open scorecard engines are powerful but push too much complexity onto the tenant for MVP (PRD §4.2.4).

## Decision

Scorecards are **configurable per organization** with a bounded structure:

- **Scorecard**: a named set of rules (e.g., "Production Readiness," "Security Baseline").
- **Category**: groups of rules within a scorecard (e.g., "Documentation," "Ownership," "Monitoring").
- **Rule**: a predicate over entity metadata that yields pass/fail/NA with a **weight**.
- Org admins configure scorecards in-platform; per-entity scores compute on change and on schedule.

The rule engine is **parameter-driven, not code-driven** in MVP (e.g., "has_tag X", "owner_is_set", "has_doc_link", "scorecard_X_passes") — expressive enough for 90% of real quality rules without requiring an external DSL or plugin runtime (ADR-0038 defers plugins to v2).

## Rationale

- Per-org configurability is necessary — no single rule set fits all industries.
- Bounded parameter-driven rules are expressive without becoming a second programming language.
- Weights allow organizations to express priority without inventing scoring math.
- Defers the open-ended policy-DSL question (OPA / Rego / CEL) to v2 alongside plugins (ADR-0038).

## Alternatives Considered

- **Platform-fixed scorecards** — fails the real-use-case test; different orgs need different rules.
- **Template-based with overrides only** — useful as a *starting point* (we'll ship recommended templates), not sufficient on its own.
- **OPA/Rego-driven** — powerful and portable, but increases tenant complexity and operational surface; revisit post-MVP.

## Consequences

**Positive:**
- Each tenant's scorecards reflect their actual quality bar.
- Rule library grows over time — each added primitive benefits every tenant.
- Shipped scorecard templates (e.g., "SOC 2 Readiness," "Incident-Ready Service") provide quick-start value.

**Negative / Trade-offs:**
- Rule engine must be carefully designed so it doesn't creep into a bespoke DSL by accident.
- Score recomputation on changes must be efficient at catalog scale (millions of entities × N rules).
- Support burden: helping tenants design useful scorecards.

**Neutral:**
- Plugin/DSL path remains open for v2 (ADR-0038).

## References

- PRD §4.2.4
- Phase 2: E-10.F-01.S-01
- Related ADRs: ADR-0038, ADR-0069, ADR-0071
