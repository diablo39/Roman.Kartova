# Deep PR Review — feat/audit-system-actor-sweep (E-01.F-03.S-03 follow-up)

**Date:** 2026-06-18 · **Status:** OPEN (pre-merge gate) · **Reviewer:** deep-review (opus, in-session)
**Spec:** docs/superpowers/specs/2026-06-18-audit-system-actor-sweep-design.md · **Plan:** docs/superpowers/plans/2026-06-18-audit-system-actor-sweep-plan.md

### Overview
This slice adds a `System`-actor audit write path (`IAuditWriter.AppendSystemAsync(TenantId, AuditEntry, ct)` over an extracted private `AppendCoreAsync`, User path byte-for-byte unchanged) and refactors `ExpireInvitationsHostedService` to enumerate due invitations cross-tenant via BYPASSRLS, then process each tenant in its own `ITenantScope` transaction, writing one `System` `invitation.expired` audit row per expiry with per-tenant try/catch isolation. It adds the `OrganizationAuditActions.InvitationExpired` constant, deletes the obsolete InMemory sweep unit tests, and adds real-seam integration tests (happy / multi-tenant / future-skip / accepted-skip / idempotency / fail-closed) plus `AuditWriter` System assertions and a fixture `actor_type` reader.

### Blocking-class issues
None.

### Should-fix issues

**Spec §8 promised chain verification via `IAuditChainVerifier.VerifyAsync`; the integration tests use a local `AssertChainLinked` helper instead — a silent deviation.**
- Evidence: spec §8 artifacts 1 & 2 state "`IAuditChainVerifier.VerifyAsync(tenantA)` reports the chain intact" / "Both chains verify intact." Tests assert linkage manually at `InvitationExpirySweepAuditTests.cs` (`AssertChainLinked`: contiguous seq + `prev_hash == predecessor.row_hash`). The recomputation-based verifier runs only in `AuditWriterTests.cs` (System chain).
- Impact: `AssertChainLinked` checks seq contiguity + prev/row linkage but does not recompute `row_hash` from fields, so it can't catch a hasher omitting a field — the tamper class `VerifyAsync` targets (ADR-0018). The cross-module substitution (avoiding an Organization→Audit.Infrastructure reference, ADR-0082) is legitimate but the spec went stale.
- Fix (applied): amended spec §8 to record that cross-module chain integrity is asserted structurally in the Organization suite while recomputation-based `VerifyAsync` evidence lives in `AuditWriterTests.AppendSystem_writes_System_actor_row_with_null_actor_and_chains`.

### Nits
1. **"no partial state" comment overstates** — `ExpireInvitationsHostedService.cs` KC-error block: the KC user is genuinely deleted before rollback (intended partial *external* effect, spec §3 decision 5). Reworded to "no partial *DB* state". (applied)
2. **`TimeProvider` resolved twice** (enumeration `now` vs per-tenant `now`) — same singleton, negligible; the per-tenant re-read is intentional. (left as-is by design)
3. **`[ExcludeFromCodeCoverage]` on `InvitationExpirySweepFaultTests` but not the sibling integration suites** — convention drift; dropped for consistency. (applied)

### Missing tests
1. **Mutation survivors on `if (expired > 0 || failed > 0)` (summary-log guard)** — cosmetic, not logic-class: the only effect is whether an informational summary log line emits; audit rows + expiry are already committed upstream. Documented in `mutation-report-surviving.md`; no test required (asserting log wording adds no correctness).
2. **Per-tenant isolation across a boundary (tenant A fails, sibling B succeeds in the same run)** — covered compositionally (fail-closed `CommitFailFlag` test proves the catch fires + rolls back; multi-tenant happy test proves multiple tenants processed; the `catch-when` filter logic was mutation-killed at 92.31%). A direct A-fails/B-succeeds test needs a tenant-targeted fail flag (`CommitFailFlag` is a global singleton on the fault host) — noted as a documented follow-up gap, not implemented this slice.

### What looks good
- **`AppendCoreAsync` extraction keeps the User path byte-for-byte** (`AuditWriter.cs`): both public methods delegate with explicit actor tuples → the 10 shipped User wirings untouched (spec §3 decision 1; right call over the rejected actor-provider abstraction).
- **Explicit `TenantId` on `AppendSystemAsync`** (`AuditWriter.cs`, `IAuditWriter.cs`): System path never touches `ICurrentUser`/`ITenantContext`, matching `AuditCheckpointer`'s explicit-tenant style — clean layering for background callers.
- **Structural fidelity to `AuditCheckpointHostedService`**: same per-tenant `CreateAsyncScope` → `BeginAsync` → work → `CommitAsync` shape + `catch (Exception) when (ex is not OperationCanceledException)` isolation; the job correctly acts as the ADR-0090 transport adapter owning Begin/Commit.
- **Domain needed zero change for System rows** (`AuditLogEntry.cs:52-53`): the non-empty-actorId guard is User-only, so `actorId: null` is the honest encoding and the hasher already covers nullable `actorId` (spec §3 decision 2 verified against the actual guard).
- **Re-read through the RLS context with a `Status==Pending` re-filter** (`ExpireInvitationsHostedService.cs`): enumeration is BYPASSRLS but the mutation re-queries inside `SET LOCAL`, so an invitation accepted/revoked between enumeration and processing is correctly skipped; the deleted InMemory unit tests (which couldn't model RLS/SET LOCAL/hash chain) were rightly removed rather than kept as false comfort.
