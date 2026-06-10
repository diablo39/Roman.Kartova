# ADR-0102: User Offboarding Is Hard Delete

**Status:** Accepted
**Date:** 2026-06-10
**Deciders:** Roman Głogowski (solo developer)
**Category:** Compliance & Retention
**Related:** ADR-0015 (GDPR compliance), ADR-0018 (append-only audit log), ADR-0019 (soft-delete with 30-day purge), ADR-0100 (one-email-per-tenant identity scope), ADR-0101 (team-admin authority via per-team membership)

## Context

Slice 10 adds member offboarding to the Organization module. ADR-0019 prescribes a soft-delete + 30-day purge window for *catalog entities* (applications, relationships, and similar records that users accidentally delete and may need to recover). The question for this slice is whether that same model applies to an org member being removed by an OrgAdmin.

A member is a **KeyCloak-owned identity**: they exist as a realm user in KeyCloak and are projected locally into the `users` table as a read-cache/index. This is fundamentally different from a catalog entity:

- Their lifecycle is governed by the identity provider, not by the catalog domain model.
- ADR-0015 (GDPR) grants data subjects a right to erasure that favors clean removal.
- ADR-0100 enforces one email per tenant across the platform; a soft-deleted member holding a seat would prevent re-invitation of the same email address.
- Unlike a catalog entity there is no meaningful "recovery window" — if an org admin offboards a member by mistake, the correct remediation is to re-invite them, not to restore a tombstone row.

The org must also retain at least one active OrgAdmin at all times. A member cannot offboard themselves, and the last OrgAdmin cannot be demoted or offboarded.

The ADR-0018 append-only audit log has not yet been built (it is its own future slice). Any audit trail for offboarding and role-change events depends on that infrastructure.

## Decision

**Offboarding is a hard delete.** It hard-deletes the KeyCloak identity and the local `users` projection row, after first reassigning the member's owned catalog applications to a chosen successor (cross-module via the `IApplicationOwnerReassigner` port, within the request's tenant-scope transaction).

This decision sits **outside ADR-0019's scope**: ADR-0019 governs catalog entities, not IdP-owned identities.

Specific rules enforced:

- **Successor required.** The offboard request must name a `successorUserId`; owned applications (`Application.OwnerUserId`) are reassigned to that user before the delete, preserving the required-owner invariant.
- **Last-OrgAdmin guard.** The org must retain ≥ 1 active OrgAdmin at all times. An OrgAdmin cannot be demoted (role change) or offboarded if they are the sole remaining OrgAdmin.
- **No self-offboard.** A member cannot offboard themselves.
- **Traceless until audit-log slice.** Offboarding and role-change events are **not** written to any audit store in this slice. This is a conscious, named gap — the ADR-0018 audit infrastructure is deferred to its own slice. Handlers are structured (single mutation method per action) so an `IAuditLog` emitter can drop in later without reshaping the flow.
- **Token-TTL effect.** Role changes and offboarding take effect on the member's next access-token refresh. No session revocation is performed in this slice (user-confirmed; revisit if security-hardening slice is ever scoped).

The cross-module reassignment is performed via a **DI port** (`IApplicationOwnerReassigner` in `Kartova.SharedKernel.Multitenancy`), implemented by `Kartova.Catalog.Infrastructure`. Both the reassignment (Catalog's `CatalogDbContext`) and the projection/membership delete (Organization's `OrganizationDbContext`) share the single request connection + transaction (ADR-0090), committing atomically. The KeyCloak delete is the non-transactional external call. If it succeeds but the DB commit then fails, the residual stale projection row is resolved by a manual idempotent re-run. This dual-write reality is the same accepted limitation from slice 9 (no durable outbox yet).

## Consequences

### Positive

- Frees the email address and seat immediately (ADR-0100); the same email can be re-invited without any tombstone window.
- Clean removal — no permanent "disabled member" state to manage in the projection or the SPA.
- GDPR erasure-aligned (ADR-0015): the identity and its personal data are deleted from both the IdP and the local projection.
- Successor reassignment keeps the required-owner invariant intact before the delete, so the catalog is never left with orphaned applications.

### Negative / trade-offs

- **No recovery window.** Unlike ADR-0019 catalog entities, there is no 30-day window to undo an accidental offboard. The correct recovery path is re-invitation, which is an existing capability.
- **No audit trail until the audit-log slice (named gap).** Offboarding events and role-change events are intentionally traceless until the ADR-0018 slice ships. This is a known, explicit gap — not a silent omission.
- **Dual-write window.** The KeyCloak-delete-then-DB-commit sequence has no durable outbox. A failure between the two steps can produce a stale projection row; resolution is a manual idempotent re-run of the offboard request.

### Neutral

- Successor reassignment is handled in a single request step (not a separate prior operation), improving UX over a "block-until-reassigned" alternative.
- The `IApplicationOwnerReassigner` port mirrors existing cross-module ports (`IApplicationCountByTeamReader`, `IOrganizationTeamExistenceChecker`) and adds no new architectural pattern.

## Alternatives considered

**Soft-delete / disable members (rejected).** Marking a member `disabled_at` and hiding them from active lists would add an active/disabled state that the `users` projection does not model and that ADR-0019's 30-day purge pipeline is not designed for (it governs catalog entities). It would also keep the email/seat claimed, preventing re-invitation. Revisit only if a first-class "suspend member" capability is ever scoped.

**Block offboard until apps are manually reassigned, returning 409 (rejected).** Requiring a separate prior reassignment step is worse UX than a single offboard-with-successor request. The successor-picker-in-dialog pattern handles it in one operation.

**Build the audit log as part of this slice (rejected).** User confirmed: skip audit-log infrastructure here. The audit log is a meaningful slice of its own (tamper-evident hash chaining, retention integration, MiFID II record requirements per ADR-0016/0018). Building it opportunistically alongside offboarding would under-bake it.

## References

- Slice 10 design spec: `docs/superpowers/specs/2026-06-09-slice-10-member-lifecycle-management-design.md` (§2 Decisions, §7 Critical runtime flows)
- Related story: E-03.F-01.S-07 (offboard member + reassign owned components)
- ADR-0015: GDPR Compliance From Day One
- ADR-0018: Append-Only Tamper-Evident Audit Log
- ADR-0019: Soft Delete with 30-Day Purge
- ADR-0100: Identity Scope — Strict One-Email-Per-Tenant in a Single KeyCloak Realm
- ADR-0101: Team-Admin Authority Is Per-Team Membership, Not a Realm Role
