# ADR-0108: Relationship Edge Authority Is Either-Endpoint Team Membership

**Status:** Accepted
**Date:** 2026-06-25
**Deciders:** Roman Głogowski (solo developer)
**Category:** Authentication & Authorization
**Related:** ADR-0101 (team-admin authority via membership), ADR-0067 (relationship origin tracking), ADR-0068 (fixed relationship-type vocabulary), ADR-0056 (manual-relationship precedence / deferred verification), ADR-0103 (application ownership = required team), ADR-0090 (tenant scope)
**Supersedes:** the source-side authority decision in `docs/superpowers/specs/2026-06-24-catalog-relationships-design.md` §3 #7

## Context

The manual-relationships backend (Slice 1a, PR #42) gates relationship **create and delete** on the **source** entity's owning team: a caller must be `OrgAdmin` or a member of the source entity's team (`AuthorizeTargetTeamAsync(caller, sourceInfo.TeamId)` in both `CreateRelationshipAsync` and `DeleteRelationshipAsync`). This mirrors the Backstage/Compass model, where the edge is declared in the **source's** catalog descriptor (`catalog-info.yaml`) and is therefore owned by whoever owns that repo.

Kartova does not store edges in source-owned descriptors — they are rows in a tenant-scoped `relationships` table. The descriptor-ownership rationale therefore does not bind. The source-only rule leaves a **completeness gap**: for `A --depends-on--> B`, only A's team can declare the edge. If A's team never gets around to it, the provider team that owns **B** cannot record that B is depended upon, and B's "Dependents"/consumers view stays empty despite a real, running dependency. The party with the most incentive to keep the graph honest (the provider) is the one forbidden from recording the edge.

## Decision

**Create and delete authority for a relationship edge is conferred by `OrgAdmin`, or membership of the owning team of *at least one* of the two connected entities** — symmetric across create and delete.

- Enforced by a new `AuthorizeEitherTeamAsync(auth, caller, teamA, teamB)` gate that composes the existing per-team membership policy over **both** endpoints' owning teams (allowed if `OrgAdmin`, or member of `teamA`, or member of `teamB`).
- `CreateRelationshipAsync` resolves the **target** entity *before* the authorization gate (it already resolves it for existence + display enrichment) and gates on `EitherTeam(source.TeamId, target.TeamId)` rather than source-only.
- `DeleteRelationshipAsync` additionally resolves the target's team and gates on either. A hard-deleted endpoint (ADR-0102) resolves to no team and contributes only `OrgAdmin` reachability; if **both** endpoints have been deleted, delete is `OrgAdmin`-only.
- The relationship-type directionality matrix (ADR-0068), origin immutability (ADR-0067), the `catalog.relationships.write` permission and its role mapping, and the audit actions (`relationship.created` / `relationship.removed`) are **unchanged**.
- **No approval/confirmation workflow.** An edge is live the moment it is created. Accountability comes from `origin=manual` + `created_by_user_id` + the in-transaction audit row.

This preserves the invariant *"you may only wire or remove an edge that touches a team you belong to"*: a member of **neither** team is still `403` (OrgAdmin excepted), so members cannot fabricate edges between teams they have no stake in.

## Consequences

### Positive

- The provider/target side can record incoming dependencies; graph completeness is no longer hostage to one side's diligence — the core motivation.
- Symmetric authority is coherent: whoever may create an edge may also remove it, and the party an edge is asserted *about* can delete it.
- The "touches a team you own" gate keeps the no-fabrication-between-unrelated-teams property that a flat `catalog.relationships.write`-only rule would have lost.
- Reuses the established membership-derived authorization pattern (ADR-0101) rather than introducing a new role or claim.

### Negative / trade-offs

- **Target-side creation asserts a claim about another team's entity** ("X depends on us"). Mitigated by: (1) audit attribution — every edge records who asserted it; (2) **symmetric delete** — the team an edge is painted onto can remove it.
- **No handshake** means a disputed edge can exist until someone deletes it. Accepted: for manually declared edges, audit-trail accountability plus either-side deletion is the proportionate control. The confirm/verification workflow remains deferred to ADR-0056 (manual-relationship precedence / conflict queue) and the scan-agent era, where machine-discovered edges create genuine conflict pressure.
- The authorization surface for relationship mutations widens from one team to two. Mitigated by real-seam integration coverage that locks the allow/deny matrix (member-of-source allowed, member-of-target allowed, member-of-neither `403`, OrgAdmin allowed, deleted-endpoint fallback).

### Migration

Pre-production (no live tenants), clean break — no data migration. The change is a logic edit in `CatalogEndpointDelegates` (`CreateRelationshipAsync` reorders the target lookup ahead of the gate; both delegates call `AuthorizeEitherTeamAsync`). The Slice 1a integration test asserting "non-member of source → 403" is rewritten to "non-member of **either** → 403," and target-team-member-allowed cases are added.

### Upgrade path

If a confirmation handshake is later required (e.g. once scan/agent origins make cross-team assertions contentious), introduce it as an explicit pending→confirmed edge state under ADR-0056 — not by narrowing write authority back to one side.

## References

- Design spec: `docs/superpowers/specs/2026-06-25-catalog-relationships-ui-surface-design.md`
- Superseded decision: `docs/superpowers/specs/2026-06-24-catalog-relationships-design.md` §3 #7
- `CatalogEndpointDelegates.CreateRelationshipAsync` / `DeleteRelationshipAsync` (`src/Modules/Catalog/Kartova.Catalog.Infrastructure/`)
