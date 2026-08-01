using Kartova.Catalog.Application;
using Kartova.Catalog.Contracts;
using Kartova.Catalog.Domain;
using Kartova.SharedKernel.AspNetCore;   // ICurrentUser — cf. CreateRelationshipHandler.cs:4
using Kartova.SharedKernel.Audit;
using Kartova.SharedKernel.Multitenancy;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Kartova.Catalog.Infrastructure;

/// <summary>
/// Executes an at-most-one System membership write over <c>PartOf</c> edges: deletes the
/// edges <see cref="SystemMembership.Decide"/> says to drop, inserts the requested one when
/// asked, and audits each change with the existing relationship audit actions. Runs inside the
/// request's ambient <c>ITenantScope</c> transaction (ADR-0090) — TWO SaveChanges calls (delete,
/// then insert), each its own EF savepoint within that one transaction. Splitting them (rather
/// than one combined call) is what makes each race's exception filter unambiguous: the
/// concurrency catch below is unambiguously about the delete, and the 23505 catch further down is
/// unambiguously about the insert. A single combined SaveChanges would roll BOTH operations back
/// together on either failure, which is wrong for a move — losing the delete race must not also
/// discard an insert that hasn't even been attempted yet.
/// </summary>
public sealed class SetComponentSystemHandler(TimeProvider clock)
{
    public async Task<SystemMembershipResponse> Handle(
        SetComponentSystemCommand cmd,
        string? systemDisplayName,
        CatalogDbContext db,
        ITenantContext tenant,
        ICurrentUser user,
        IAuditWriter audit,
        CancellationToken ct)
    {
        var current = await CurrentMembershipQueries
            .CurrentMembershipOf(db, cmd.Component.Kind, cmd.Component.Id)
            .ToListAsync(ct);

        var decision = SystemMembership.Decide(
            [.. current.Select(r => new ExistingMembership(r.Id.Value, r.Target.Id))],
            cmd.SystemId);

        var removed = current.Where(r => decision.RelationshipIdsToDelete.Contains(r.Id.Value)).ToList();
        db.Relationships.RemoveRange(removed);

        if (removed.Count > 0)
        {
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException concurrencyEx)
            {
                // A concurrent DELETE /relationships/{id} (or a second clear/move) already removed
                // the edge(s) this step wanted gone: the affected-row count came back short, so EF
                // reports the rows it expected to delete as no longer there. That is exactly what
                // this step wanted, so there is nothing to retry — but EF leaves entries that fail
                // a concurrency check tracked in their pre-failure "Deleted" state, so detach them
                // or a later SaveChangesAsync on this same context would retry (and fail) the same
                // delete again. Drop them from the audit list below too: we did not actually remove
                // them, and the other writer's own delete path already wrote its own audit row for
                // that removal.
                var lost = concurrencyEx.Entries.Select(e => e.Entity).ToHashSet();
                foreach (var entry in concurrencyEx.Entries)
                    entry.State = EntityState.Detached;
                removed.RemoveAll(r => lost.Contains(r));

                if (!decision.InsertRequested)
                {
                    // Pure clear (or a second clear): nothing left to insert, so the requested end
                    // state — no membership — already holds. Re-read live state rather than trust
                    // cmd.SystemId (null here) blindly, mirroring the POST path's
                    // re-query-the-winner discipline in CatalogEndpointDelegates.CreateRelationshipAsync.
                    var reconciled = await CurrentMembershipQueries.FindCurrentSystemIdAsync(
                        db, cmd.Component.Kind, cmd.Component.Id, ct);
                    return reconciled is { } stillThere
                        ? new SystemMembershipResponse(stillThere, systemDisplayName)
                        : new SystemMembershipResponse(null, null);
                }
                // A move: the delete lost the race, but the insert below still fulfils the
                // request — fall through rather than return early.
            }
        }

        Relationship? added = null;
        if (decision.InsertRequested && cmd.SystemId is { } systemId)
        {
            added = Relationship.CreateManual(
                cmd.Component, new EntityRef(EntityKind.System, systemId),
                RelationshipType.PartOf, user.UserId, tenant.Id, clock);
            db.Relationships.Add(added);

            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException pg
                && pg.SqlState == "23505"
                && pg.ConstraintName is "ux_relationships_one_system" or "ux_relationships_edge")
            {
                // Lost a concurrent membership-insert race. Constraint-name-scoped, but — unlike
                // the POST /relationships path's sibling catch (CatalogEndpointDelegates.
                // CreateRelationshipAsync, ~CatalogEndpointDelegates.cs:861), scoped to BOTH
                // ux_relationships_one_system AND ux_relationships_edge, not just the former.
                // Empirically (verified against a live Postgres 18 instance): when the row this
                // INSERT collides with is the exact same (source, PartOf, target) tuple — i.e. a
                // concurrent writer already created precisely the membership this request asked
                // for — Postgres reports ux_relationships_edge, not ux_relationships_one_system
                // (ux_relationships_edge was created first, in an earlier migration, and Postgres
                // surfaces the first unique index it finds violated on a single INSERT). That is
                // safe to fold in here specifically because this handler's insert is always this
                // one, fully-determined tuple (cmd.Component, PartOf, cmd.SystemId) — the only way
                // THIS insert can violate ux_relationships_edge is if that exact tuple already
                // exists, which the re-query below confirms by finding winningSystemId == systemId.
                // A different target can only violate ux_relationships_one_system (a different
                // target_id can't collide with ux_relationships_edge's full-tuple key), so the
                // 409 branch below is unaffected. Contrast the POST path, which cannot fold
                // ux_relationships_edge in this way: it has its OWN, earlier, exact-duplicate
                // pre-check (RelationshipAlreadyExists, 409) guarding a DIFFERENT problem type, so
                // catching ux_relationships_edge in its one-system catch would misreport that
                // unrelated conflict as a System-membership one instead.
                //
                // Safe to run this follow-up SELECT: AddModuleDbContextExtensions /
                // EnlistInTenantScopeInterceptor call ctx.Database.UseTransaction(scope.Transaction),
                // and because nothing in this codebase uses EnableRetryOnFailure, EF Core's
                // automatic savepoints are active — SaveChangesAsync wraps itself in a SAVEPOINT
                // and rolls back to that savepoint on failure. So by the time this catch runs, the
                // ambient ITenantScope transaction is already clean and this SELECT still runs
                // inside a live transaction. Re-query rather than assume this request's own
                // cmd.SystemId won, since the winner may have been a third, unrelated writer.
                var winningSystemId = await CurrentMembershipQueries.FindCurrentSystemIdAsync(
                    db, cmd.Component.Kind, cmd.Component.Id, ct);
                if (winningSystemId == systemId)
                {
                    // Two concurrent PUTs named the SAME System (ADR-0096 idempotence): the other
                    // writer's insert already achieved exactly the state this request asked for —
                    // treat it as success rather than reporting a conflict for a state the caller
                    // actually wanted. The POST path already gets this right for the exact-duplicate
                    // case; this mirrors that discipline for the insert race specifically.
                    return new SystemMembershipResponse(systemId, systemDisplayName);
                }
                throw new ComponentAlreadyInSystemException(cmd.Component);
            }
        }

        foreach (var rel in removed)
        {
            await audit.AppendAsync(new AuditEntry(
                CatalogAuditActions.RelationshipRemoved,
                CatalogAuditTargetTypes.Relationship,
                rel.Id.Value.ToString(),
                RelationshipAuditData.For(rel.Source, rel.Target, rel.Type)), ct);
        }

        if (added is not null)
        {
            await audit.AppendAsync(new AuditEntry(
                CatalogAuditActions.RelationshipCreated,
                CatalogAuditTargetTypes.Relationship,
                added.Id.Value.ToString(),
                RelationshipAuditData.For(added.Source, added.Target, added.Type)), ct);
        }

        return cmd.SystemId is { } finalId
            ? new SystemMembershipResponse(finalId, systemDisplayName)
            : new SystemMembershipResponse(null, null);
    }
}
