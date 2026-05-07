# ADR-0073: Enforced Entity Lifecycle States (Active → Deprecated → Decommissioned)

**Status:** Accepted
**Date:** 2026-04-17
**Deciders:** Roman Głogowski (solo developer)
**Category:** Domain Model
**Related:** ADR-0066 (multi-ownership quorum), ADR-0018 (audit log), ADR-0019 (soft delete)

## Context

Services and components have predictable end-of-life dynamics: active → marked for retirement → retired. Without an enforced lifecycle, the catalog becomes littered with "is this still a thing?" ambiguity, and downstream behaviors (notifications, scorecards, dependency alerts) cannot reliably treat retirement events. Free-form status strings consistently produce inconsistent data and broken automation (PRD §4.1.1).

## Decision

Entities carry a mandatory `lifecycle` field with exactly three states, progressing **linearly**:

1. **Active** — in production use; default on creation.
2. **Deprecated** — slated for removal; still operational but consumers should migrate. Must include a `sunset_date` and a `successor` reference (where applicable).
3. **Decommissioned** — no longer operational. Metadata is preserved (audit/historical queries) but filtered out of default views.

Transition rules:

- Active → Deprecated: standard edit (or quorum-approved for multi-owner components — ADR-0066).
- Deprecated → Decommissioned: requires explicit action; may not occur before `sunset_date` unless an admin overrides (logged in audit — ADR-0018).
- **Skipping states is forbidden** (no direct Active → Decommissioned).
- **Backward transitions require Org Admin** (e.g., resurrecting a deprecated service), fully audited.

Notifications fire automatically on state transitions (ADR-0047) to dependents discovered via relationships (ADR-0068).

## Rationale

- Linear, enforced progression makes downstream automation reliable (graph queries, scorecards, alerts).
- Deprecation metadata (sunset date, successor) turns the lifecycle field into actionable migration guidance.
- Forbidding skip transitions forces a deliberate deprecation window — dependents get time to migrate.
- Audit-logged overrides preserve operational flexibility without losing accountability.

## Alternatives Considered

- **Free-form states** — inconsistent; breaks automation; common source of catalog decay.
- **User-defined lifecycle states per tenant** — trades reliability for flexibility that tenants don't actually need; maturity model (ADR-0071) already covers custom dimensions.
- **Hide/show toggle only** — loses the crucial "deprecated" state where migration is in progress.

## Consequences

**Positive:**
- Reliable automation of deprecation notifications and dependent alerts.
- Migration guidance (sunset date, successor) is structured and queryable.
- Historical analysis remains possible via decommissioned-but-preserved entities.

**Negative / Trade-offs:**
- Tenants must model lifecycle intentionally; teams that skip states via override should be flagged.
- UI must handle lifecycle-aware filtering across all list/graph views.
- Soft delete (ADR-0019) and decommissioned state are related but distinct — requires clear UX.

**Neutral:**
- Additional states can be added in future platform versions if real needs emerge (e.g., "incubating"), but MVP ships with exactly three.

### Implementation note (slice 6, 2026-05-07)

The "filtered out of default views" rule lands on `GET /api/v1/catalog/applications` as a default-false `?includeDecommissioned=true` query parameter (slice 6, PR #<n>). Filter state is encoded in the cursor JSON (`CursorCodec.ic`); cursor with mismatched filter returns 400 `cursor-filter-mismatch`. Legacy cursors lacking the field decode as `false` for backward-compatibility with in-flight clients. SPA `ApplicationsTable` exposes a "Show decommissioned" checkbox in the toolbar wired through `useListUrlState`. The pattern carries forward to every future entity list (Service, API, Infrastructure, Broker) — captured as slice-6 spec §13.8.

## References

- PRD §4.1.1
- Phase 1: E-02.F-01.S-04
- Related ADRs: ADR-0018, ADR-0019, ADR-0047, ADR-0066, ADR-0068
