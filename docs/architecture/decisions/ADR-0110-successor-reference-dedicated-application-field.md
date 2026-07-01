# ADR-0110: Deprecated-Application Successor Is a Dedicated Application→Application Field

**Status:** Accepted
**Date:** 2026-07-01
**Deciders:** Roman Głogowski (solo developer)
**Category:** Domain Model
**Related:** ADR-0073 (enforced entity lifecycle — mandates the successor reference), ADR-0068 (fixed relationship-type vocabulary), ADR-0108 (relationship edge authority), ADR-0098 (UUID-only identifiers), ADR-0018 (audit log)

## Context

ADR-0073 defines the `Deprecated` state as requiring "a `sunset_date` **and a successor reference** (where applicable)." The sunset date shipped in slice 5; the successor reference was deferred through a carry-forward chain (slice-5 §13.4 → slice-7 §15.7) because no consumer existed. E-04 relationships have since shipped, and the successor is now being implemented.

The successor answers "this application is going away — migrate to *that* one." Two structurally different ways to model it are available:

1. A **dedicated field** on the `Application` aggregate.
2. A **new edge type** in the E-04 relationship graph (e.g. `SupersededBy`).

The choice is a contract + domain-model decision (it affects the aggregate, the migration, the API response, and where the "Deprecated ⇒ successor" invariant can live), so it is recorded here rather than made implicitly in a slice.

A second axis is what a successor may point to: another **Application** only, or **any catalog entity** (Application or Service, polymorphically).

## Decision

Model the successor as a **dedicated, nullable, self-referential field on the `Application` aggregate**: `SuccessorApplicationId : Guid?`, an **Application → Application** reference backed by a real self-referential foreign key (`successor_application_id → catalog.applications(id)`).

- **Set at `Deprecate`**, **editable while `Deprecated`** (via `PUT /applications/{id}/successor`), **cleared on `Reactivate`** — successor is deprecation metadata whose lifetime tracks the Deprecated state, exactly like `SunsetDate`.
- **Optional** — "where applicable" (ADR-0073); a Deprecated app need not name a successor.
- **Existence validated at write** (RLS-scoped lookup → 422 `invalid-successor`); **self-reference rejected** (400 `successor-self-reference`). The FK is the integrity backstop; RLS makes a cross-tenant successor unreachable.
- **Not** modelled as a relationship-graph edge; **App→App only** (not polymorphic App/Service).

## Rationale

- **Guidance, not topology.** The relationship graph (ADR-0068) encodes runtime topology — depends-on, part-of, consumes. A successor is *migration guidance* ("replaced by"), a different semantic axis. Overloading the topology graph with a lifecycle concept muddies both — dependency queries, blast-radius, and the mini-graph would have to constantly special-case a `SupersededBy` edge that is not a runtime dependency.
- **The invariant lives with the transition.** "Deprecated *may* carry a successor; Active/Decommissioned must not" is enforceable atomically inside the aggregate's `Deprecate`/`SetSuccessor`/`Reactivate` methods only if the successor is aggregate state. A relationship edge is a separate aggregate with its own lifecycle and its own either-endpoint authority (ADR-0108) — the domain could not keep the successor consistent with the lifecycle state.
- **Integrity for free.** A real self-FK guarantees the successor exists; RLS guarantees same-tenant. A polymorphic `{kind,id}` (App or Service) cannot have a cross-table FK, so it would degrade to write-time-lookup-only integrity (dangling references possible after deletion), exactly the weaker guarantee relationships live with.
- **Smaller blast radius.** A `SupersededBy` edge would expand the creatable relationship-type matrix (`RelationshipTypeRules`), the `AddRelationshipDialog`, and the either-endpoint authority reasoning. A nullable column + one endpoint is far less surface.
- **App→App is the dominant case.** A deprecated application is most often replaced by another application (a v2, a rewrite). App-succeeded-by-Service is plausible (monolith → service) but not yet a demonstrated need; deferring it keeps the real-FK integrity and avoids speculative polymorphism (YAGNI).

## Alternatives Considered

- **`SupersededBy` relationship edge (reuse E-04).** Would appear in the dependency graph and unify all entity-to-entity links under one subsystem. Rejected: cannot enforce the lifecycle invariant atomically; expands the relationship matrix/dialog; conflates migration guidance with runtime topology; `Reactivate` could not auto-clear an edge.
- **Polymorphic App→(App|Service) dedicated field (`{successor_kind, successor_id}`).** More flexible (covers monolith→service succession). Rejected for now: loses the cross-table FK integrity guarantee (dangling refs possible), adds a column and validate-both-tables logic, for a case not yet demonstrated. Recorded as the sanctioned upgrade path (below).
- **Free-text "successor" note.** Rejected: not queryable, not navigable, no integrity — the same catalog-decay failure mode ADR-0073 exists to prevent.

## Consequences

**Positive**
- The "Deprecated ⇒ successor (where applicable)" rule is a domain invariant, not cross-aggregate coordination.
- Referential integrity (self-FK) + tenant isolation (RLS) are guaranteed by the database.
- Minimal surface: one nullable column, one `PUT /successor` endpoint, one detail-page link.
- Clean, navigable migration guidance on the deprecated app's detail page.

**Negative / trade-offs**
- The successor does **not** appear in the dependency graph or relationship tables (it is a field, not an edge) — a deliberate separation, but users looking for "what replaces this" must read the detail page, not the graph.
- **App→Service succession is not expressible.** If it becomes a real need, the upgrade path is to widen the field to a polymorphic `{successor_kind, successor_id}` (dropping the self-FK for write-time lookup validation, as relationships do) via a migration — not to flip to a relationship edge.
- A **Decommissioned (or Deprecated) application may still be named as a successor** — the successor's own lifecycle is not validated. Pointing "migrate to X" at a dead app is nonsensical but not blocked; enforcing it would couple two aggregates' lifecycles. Noted as a soft follow-up.

**Neutral**
- Applies to `Application` only (the sole entity with a lifecycle today). Extending lifecycle + successor to other entity kinds is future scope.

## References

- ADR-0073 (lifecycle states — the mandate); slice-7 §15.7 / slice-5 §13.4 (the deferred follow-up this closes).
- Implementing slice: `docs/superpowers/specs/2026-07-01-adr0073-cleanups-successor-override-design.md`.
- Aggregate: `src/Modules/Catalog/Kartova.Catalog.Domain/Application.cs`.
