using Kartova.Catalog.Application;
using Kartova.Catalog.Domain;
using Kartova.SharedKernel.Audit;
using Microsoft.EntityFrameworkCore;

namespace Kartova.Catalog.Infrastructure;

public sealed class DeleteRelationshipHandler
{
    public async Task<bool> Handle(
        Relationship rel,
        CatalogDbContext db,
        IAuditWriter audit,
        CancellationToken ct)
    {
        db.Relationships.Remove(rel);
        await db.SaveChangesAsync(ct);
        await audit.AppendAsync(new AuditEntry(
            CatalogAuditActions.RelationshipRemoved,
            CatalogAuditTargetTypes.Relationship,
            rel.Id.Value.ToString(),
            new Dictionary<string, string?>
            {
                ["sourceKind"] = rel.Source.Kind.ToString(),
                ["sourceId"]   = rel.Source.Id.ToString(),
                ["type"]       = rel.Type.ToString(),
                ["targetKind"] = rel.Target.Kind.ToString(),
                ["targetId"]   = rel.Target.Id.ToString(),
            }), ct);
        return true;
    }
}
