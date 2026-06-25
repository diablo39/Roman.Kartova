using Kartova.Catalog.Application;
using Kartova.Catalog.Domain;
using Kartova.SharedKernel.Audit;
using Microsoft.EntityFrameworkCore;

namespace Kartova.Catalog.Infrastructure;

public sealed class DeleteRelationshipHandler
{
    public async Task Handle(
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
            RelationshipAuditData.For(rel.Source, rel.Target, rel.Type)), ct);
    }
}
