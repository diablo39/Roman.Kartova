using Kartova.Catalog.Application;
using Kartova.Catalog.Contracts;
using Kartova.Catalog.Domain;
using Kartova.SharedKernel.Pagination;
using Kartova.SharedKernel.Postgres.Pagination;
using Microsoft.EntityFrameworkCore;

namespace Kartova.Catalog.Infrastructure;

public sealed class ListRelationshipsForEntityHandler
{
    private static readonly Func<Relationship, Guid> IdExtractor = x => x.Id.Value;

    public async Task<CursorPage<RelationshipResponse>> Handle(
        ListRelationshipsForEntityQuery q,
        CatalogDbContext db,
        ICatalogEntityLookup lookup,
        CancellationToken ct)
    {
        var spec = RelationshipSortSpecs.Resolve(q.SortBy);

        IQueryable<Relationship> source = q.Direction switch
        {
            RelationshipDirection.Outgoing =>
                db.Relationships.Where(r => r.Source.Kind == q.Entity.Kind && r.Source.Id == q.Entity.Id),
            RelationshipDirection.Incoming =>
                db.Relationships.Where(r => r.Target.Kind == q.Entity.Kind && r.Target.Id == q.Entity.Id),
            _ => // All
                db.Relationships.Where(r =>
                    (r.Source.Kind == q.Entity.Kind && r.Source.Id == q.Entity.Id) ||
                    (r.Target.Kind == q.Entity.Kind && r.Target.Id == q.Entity.Id)),
        };

        var page = await source
            .ToCursorPagedAsync(
                spec, q.SortOrder, q.Cursor, q.Limit,
                RelationshipSortSpecs.IdSelector, IdExtractor, ct);

        // Batch distinct entity refs for display-name enrichment (avoid N+1).
        var refSet = new HashSet<(EntityKind Kind, Guid Id)>();
        foreach (var r in page.Items)
        {
            refSet.Add((r.Source.Kind, r.Source.Id));
            refSet.Add((r.Target.Kind, r.Target.Id));
        }

        var displayNames = new Dictionary<(EntityKind Kind, Guid Id), string>();
        foreach (var (kind, id) in refSet)
        {
            var result = await lookup.Find(kind, id, ct);
            if (result is not null)
                displayNames[(kind, id)] = result.DisplayName;
        }

        string GetDisplayName(EntityKind kind, Guid id) =>
            displayNames.TryGetValue((kind, id), out var name) ? name : string.Empty;

        var items = page.Items
            .Select(r =>
            {
                var src = new EntityRefDto(r.Source.Kind, r.Source.Id, GetDisplayName(r.Source.Kind, r.Source.Id));
                var tgt = new EntityRefDto(r.Target.Kind, r.Target.Id, GetDisplayName(r.Target.Kind, r.Target.Id));
                return r.ToResponse(src, tgt);
            })
            .ToList();

        return new CursorPage<RelationshipResponse>(items, page.NextCursor, page.PrevCursor);
    }
}
