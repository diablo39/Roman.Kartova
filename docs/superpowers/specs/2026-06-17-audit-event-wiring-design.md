# Design — Audit event wiring (Phase 2): Organization mutations

**Date:** 2026-06-17
**Author:** Roman Głogowski (AI-assisted)
**Status:** Approved (pre-implementation)
**Story:** E-01.F-03.S-03 (Append-only audit log — MiFID II) — closes `[~]→[x]`
**Builds on:** [2026-06-12-audit-log-foundation-design.md](2026-06-12-audit-log-foundation-design.md) (Phase 1 — the mechanism)
**ADRs:** ADR-0018 (append-only tamper-evident audit log — controlling), ADR-0090 (tenant scope), ADR-0082 (modular monolith — no cross-module Infrastructure refs), ADR-0102 (offboard hard-delete — named this trail as the parked gap), ADR-0093 (direct-dispatch sync handlers)
**Modules touched:** `Kartova.Organization` (Application + Infrastructure), `Kartova.SharedKernel.AspNetCore`

## 1. Why this slice, why now

Phase 1 (2026-06-12) shipped the audit *mechanism* — the `Kartova.Audit` module, the insert-only/RLS `audit_log` table, the synchronous fail-closed `IAuditWriter`, the per-tenant SHA-256 hash chain, and `IAuditChainVerifier`. It deliberately wired **no business events**: the foundation spec §2 defines "Phase 2 — Usage" as the slice where "each existing org/team mutation handler calls `IAuditWriter.AppendAsync(...)` inside its transaction."

This is that slice. ADR-0102 made the dependency explicit — member role changes and **hard-delete offboarding** (slices 9–10) currently mutate identity & access with no audit trail. Building the trail now records provenance for irreversible mutations that already exist in code.

## 2. Scope (resolved during brainstorming)

**In scope — 10 user-initiated Organization HTTP mutations, `User` actor only:**

`member.role_changed`, `member.offboarded`, `team.created`, `team.updated`, `team.deleted`, `team.member_added`, `team.member_removed`, `team.member_role_changed`, `invitation.created`, `org.profile_updated`.

**Explicitly deferred (named, not silently dropped):**

| Deferred | Owner / why |
|----------|-------------|
| **Catalog app events** (register / edit / lifecycle transitions / assign-team) | Foundation spec §2 scopes Phase 2 to org/team handlers. Catalog wiring is a clean follow-up slice — same inline pattern, no new mechanism. |
| **`invitation.expired` + `System` actor** | The hourly `ExpireInvitationsHostedService` runs cross-tenant on the `AdminOrganizationDbContext` **BYPASSRLS** pool, **outside any `ITenantScope`/transaction**, with no HTTP principal. Auditing it needs a `System`-actor path *and* per-tenant scope establishment (`SET LOCAL` + tenant txn) inside the sweep loop so the per-tenant hash chain + RLS `INSERT WITH CHECK` work. That is qualitatively heavier and unused by the 10 HTTP events; it becomes a small, clearly-scoped follow-up ("audit System-actor + sweep"). `invitation.expired` is a low-stakes lifecycle event vs. the access-control events wired here. |

## 3. Decisions

| # | Decision | Rationale |
|---|----------|-----------|
| 1 | **Inline `AppendAsync` on each handler's success path** (Approach A). After the business `SaveChangesAsync`, the handler calls `auditWriter.AppendAsync(new AuditEntry(action, targetType, targetId, data), ct)`. | The audit row rides the same per-request `ITenantScope` transaction (`AuditDbContext` shares the request connection via `AddModuleDbContext`) → atomic, fail-closed, already proven by Phase 1's mandatory rollback test. Matches the existing direct-dispatch handler style (ADR-0093). Payloads are shaped exactly per handler; "only-on-success" is trivial — the call sits below the early-return guards. |
| 2 | **`actor_display` snapshot sourced from the JWT, via `ICurrentUser.DisplayName`.** Extend `ICurrentUser` with a `DisplayName` property; `HttpContextCurrentUser` resolves it from claims `name` → `preferred_username` → `email` → `sub`. `AuditWriter` sets `actorDisplay: currentUser.DisplayName` (replacing the Phase-1 `null`). | Write-time snapshot **by construction** — satisfies foundation decision 4 (a row must still name who acted even after that actor is later offboarded). No DB round-trip and no cross-module port: the token already carries the display name. Keeps the Audit module from coupling to HTTP (`ICurrentUser` already injected into `AuditWriter`). |
| 3 | **Action taxonomy as constants in `Organization.Application`** (`OrganizationAuditActions`). | Actions are caller-defined strings; the taxonomy belongs to the caller module, keeping `Kartova.Audit` generic. Referenced from Infrastructure (which already references Application). |
| 4 | **`AppendAsync` placed *after* the business `SaveChangesAsync`, on the success path only.** Rejected/conflict/not-found early returns emit no row. | We audit committed *intent to change state*, not rejected attempts. The shared transaction means an audit-write failure still rolls the business change back (fail-closed), but a rejected mutation never reaches the call. |
| 5 | **Offboard captures the *target's* `displayName`/`email` into `data` before `db.Users.Remove(target)`.** | The target row is hard-deleted in the same transaction (ADR-0102); the snapshot must be read before removal so the audit row names *who was offboarded*. (`actor_display` covers *who acted* — a separate field from a separate source.) |

## 4. Action taxonomy & payloads

`AuditEntry.Data` is `IReadOnlyDictionary<string, string?>?` — **string values only** (the foundation's jsonb-hash-stability rule; no raw numbers/floats). `targetId` is a string.

| Action constant | `action` string | targetType / targetId | `data` keys | Old-value capture |
|---|---|---|---|---|
| `MemberRoleChanged` | `member.role_changed` | `User` / userId | `oldRole`, `newRole` | old = `user.RealmRole` read before write-through |
| `MemberOffboarded` | `member.offboarded` | `User` / userId | `displayName`, `email` | target snapshot read before `Remove` |
| `TeamCreated` | `team.created` | `Team` / teamId | `displayName` | — |
| `TeamUpdated` | `team.updated` | `Team` / teamId | `displayName`, `description` | — (new state) |
| `TeamDeleted` | `team.deleted` | `Team` / teamId | `displayName` | read before `Remove` |
| `TeamMemberAdded` | `team.member_added` | `Team` / teamId | `userId`, `role` | — |
| `TeamMemberRemoved` | `team.member_removed` | `Team` / teamId | `userId` | — |
| `TeamMemberRoleChanged` | `team.member_role_changed` | `Team` / teamId | `userId`, `oldRole`, `newRole` | old = `membership` role read before `ChangeRole` |
| `InvitationCreated` | `invitation.created` | `Invitation` / invitationId | `email`, `role` | — |
| `OrgProfileUpdated` | `org.profile_updated` | `Organization` / tenantId | `displayName`, `defaultTimeZone` | — (new state) |

`targetType` values are short literals co-located with the taxonomy (`User`, `Team`, `Invitation`, `Organization`). Team-membership events take the **Team** as target (the owning entity), carrying `userId` in `data`.

## 5. Handler wiring

Inject `IAuditWriter` into the 10 handlers (constructor — consistent with how `TimeProvider` / `IKeycloakAdminClient` / `IApplicationCountByTeamReader` are already injected; works for both the Wolverine `Handle(cmd, db, ct)` handlers and the plain `HandleAsync` handlers).

| Handler | File | Notes |
|---|---|---|
| `ChangeMemberRoleHandler` | Organization.Infrastructure | capture `user.RealmRole` before write-through |
| `OffboardMemberHandler` | Organization.Infrastructure | capture target `DisplayName`/`Email` before `Remove`; append after `SaveChangesAsync` |
| `CreateTeamHandler` | Organization.Infrastructure | |
| `UpdateTeamHandler` | Organization.Infrastructure | |
| `DeleteTeamHandler` | Organization.Infrastructure | capture `team.DisplayName` before `Remove`; append only on `Deleted` path (not `ApplicationsAssigned`/`NotFound`) |
| `AddTeamMemberHandler` | Organization.Infrastructure | append only on `Added` (not `AlreadyMember`/`TeamNotFound`) |
| `RemoveTeamMemberHandler` | Organization.Infrastructure | append only on `Removed` |
| `UpdateTeamMemberHandler` | Organization.Infrastructure | capture old role before `ChangeRole`; append only on `Updated` |
| `CreateInvitationHandler` | Organization.Infrastructure | append on `Created` only (after `SaveChangesAsync`, before building the response URL) |
| `UpdateOrgProfileHandler` | Organization.Infrastructure | append only on `Ok` (not `NotFound`/`ConcurrencyConflict`) |

`AuditWriter` change: one line — `actorDisplay: currentUser.DisplayName` instead of `null`. No other mechanism change.

`ICurrentUser` change: add `string DisplayName { get; }`; implement in `HttpContextCurrentUser` with the `name`→`preferred_username`→`email`→`sub` fallback. Any test fake of `ICurrentUser` gains the property.

## 6. Architecture invariants (unchanged from Phase 1)

- **Synchronous, in-transaction, fail-closed** — `AuditDbContext` shares the request connection + transaction; an append failure rolls the business change back. No mutation commits without its audit row.
- **No cross-module Infrastructure reference** (ADR-0082) — handlers see only the SharedKernel `IAuditWriter`; DI binds the Audit impl. NetArchTest stays green.
- **Per-tenant hash chain + RLS** — every wired caller already runs inside an `ITenantScope` txn (`SET LOCAL app.current_tenant_id`, ADR-0090), so the writer's existing chain logic applies untouched.

## 7. Testing strategy (five-tier, ADR-0097; per [docs/TESTING-STRATEGY.md](../../TESTING-STRATEGY.md))

This slice wires a DB-write seam onto existing authenticated HTTP mutations, so gate-5 evidence is **real-seam** integration tests — `KartovaApiFixtureBase`, real Postgres/RLS, real `JwtBearer` validation — not mocked handlers.

**Gate-5 artifacts (named deliverables, `Kartova.Organization.IntegrationTests`):**

1. **Happy — role change writes a correct audit row.** `PUT /users/{id}/role` (real JWT) → assert exactly one `audit_log` row with `action=member.role_changed`, `actor_id` = caller `sub`, `actor_display` = caller `name` claim, `data` carrying `oldRole`+`newRole`; `IAuditChainVerifier.VerifyAsync` reports the tenant chain intact.
2. **Happy — offboard snapshot survives the target's hard-delete.** Offboard a member → the `member.offboarded` row's `data` still carries the target's `displayName`/`email` even though the `users` row was removed in the same transaction.
3. **Negative — rejected mutation writes no row.** A last-OrgAdmin-guarded role change → `409` and **zero** `audit_log` rows. Proves only successful mutations are audited.

**Unit:**

- `HttpContextCurrentUser.DisplayName` claim-fallback chain: `name` present → `name`; absent → `preferred_username`; absent → `email`; all absent → `sub` string.

**Architecture (NetArchTest):** existing rules stay green — `IAuditWriter` in SharedKernel; no Organization → `Audit.Infrastructure` reference; taxonomy constants are not Contracts types (no `[ExcludeFromCodeCoverage]` needed — they carry logic-free literals but are exercised by the integration tests).

**Container build (gate 4):** no Dockerfile/`COPY` change in this slice (no new project/module), so the existing `images` CI job covers it; flag if a new file lands outside an already-copied path.

## 8. Definition of Done

Per CLAUDE.md's eight always-blocking gates + the conditional mutation gate. **Gate 6 (mutation) is should-do** here: the slice adds wiring + constants + one claim-fallback method; it does not touch Domain/Application *business* logic. Run it on the `DisplayName` fallback + payload-shaping if practical, else skip with this note.

Slice size: ~300–380 prod LOC (handlers + `ICurrentUser`/`HttpContextCurrentUser` + taxonomy), within the ~400 target.

On completion, update `docs/product/CHECKLIST.md`: `E-01.F-03.S-03` `[~]→[x]` with a one-line note (Phase 2 — org/people events wired; Catalog + System-actor/sweep deferred).
