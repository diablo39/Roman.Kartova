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
/// request's ambient <c>ITenantScope</c> transaction (ADR-0090) — one SaveChanges.
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
        var current = await db.Relationships
            .Where(r => r.Type == RelationshipType.PartOf
                        && r.Source.Kind == cmd.Component.Kind
                        && r.Source.Id == cmd.Component.Id)
            .ToListAsync(ct);

        var decision = SystemMembership.Decide(
            [.. current.Select(r => new ExistingMembership(r.Id.Value, r.Target.Id))],
            cmd.SystemId);

        var removed = current.Where(r => decision.RelationshipIdsToDelete.Contains(r.Id.Value)).ToList();
        db.Relationships.RemoveRange(removed);

        Relationship? added = null;
        if (decision.InsertRequested && cmd.SystemId is { } systemId)
        {
            added = Relationship.CreateManual(
                cmd.Component, new EntityRef(EntityKind.System, systemId),
                RelationshipType.PartOf, user.UserId, tenant.Id, clock);
            db.Relationships.Add(added);
        }

        try
        {
            if (removed.Count > 0 || added is not null)
                await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException pg && pg.SqlState == "23505")
        {
            // Lost a concurrent membership race — the other writer's edge stands.
            //
            // Safe to return a 409 and let the commit filter run afterwards: AddModuleDbContextExtensions
            // / EnlistInTenantScopeInterceptor call ctx.Database.UseTransaction(scope.Transaction), and
            // because nothing in this codebase uses EnableRetryOnFailure, EF Core's automatic savepoints
            // are active — SaveChangesAsync wraps itself in a SAVEPOINT and rolls back to that savepoint
            // on failure. So by the time this catch runs, the ambient ITenantScope transaction is already
            // clean; it is not being silently rescued by a COMMIT against an aborted transaction. Adding
            // EnableRetryOnFailure anywhere in this context's configuration would disable automatic
            // savepoints and break this path.
            throw new ComponentAlreadyInSystemException(cmd.Component);
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
