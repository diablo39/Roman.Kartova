using Kartova.Catalog.Application;
using Kartova.Catalog.Contracts;
using Kartova.Catalog.Domain;
using Kartova.SharedKernel.AspNetCore;
using Kartova.SharedKernel.Audit;
using Kartova.SharedKernel.Multitenancy;

namespace Kartova.Catalog.Infrastructure;

public sealed class CreateRelationshipHandler(TimeProvider clock)
{
    public async Task<RelationshipResponse> Handle(
        CreateRelationshipCommand cmd,
        EntityRefDto source,
        EntityRefDto target,
        CatalogDbContext db,
        ITenantContext tenant,
        ICurrentUser user,
        IAuditWriter audit,
        CancellationToken ct)
    {
        var rel = Relationship.CreateManual(cmd.Source, cmd.Target, cmd.Type, user.UserId, tenant.Id, clock);

        db.Relationships.Add(rel);
        await db.SaveChangesAsync(ct);

        await audit.AppendAsync(new AuditEntry(
            CatalogAuditActions.RelationshipCreated,
            CatalogAuditTargetTypes.Relationship,
            rel.Id.Value.ToString(),
            new Dictionary<string, string?>
            {
                ["sourceKind"] = cmd.Source.Kind.ToString(),
                ["sourceId"]   = cmd.Source.Id.ToString(),
                ["type"]       = cmd.Type.ToString(),
                ["targetKind"] = cmd.Target.Kind.ToString(),
                ["targetId"]   = cmd.Target.Id.ToString(),
            }), ct);

        return rel.ToResponse(source, target);
    }
}
