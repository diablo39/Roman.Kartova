using Kartova.Catalog.Application;
using Kartova.Catalog.Contracts;
using Kartova.Catalog.Domain;
using Kartova.SharedKernel.Identity;
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
        IUserDirectory directory,
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

        // Filter state the cursor is issued under (ADR-0095). Every row-set-narrowing filter
        // applied above/below MUST be recorded here per QueryablePagingExtensions' caller
        // contract (QueryablePagingExtensions.cs:48-54) — an omitted filter silently breaks
        // keyset consistency, since the cursor can't detect the filter changing mid-pagination.
        // Null/absent ⇒ the cursor's f-map stays empty, byte-identical to a filterless cursor.
        Dictionary<string, string>? filters = null;

        if (q.ExcludeApiEdges)
        {
            source = source.Where(r =>
                r.Type != RelationshipType.ProvidesApiFor &&
                r.Type != RelationshipType.ConsumesApiFrom);
            filters ??= new Dictionary<string, string>(StringComparer.Ordinal);
            filters["excludeApiEdges"] = "true";
        }

        // Type filter (task 4d). Applied before paging so a hidden row never becomes
        // a cursor boundary — same discipline as the other list handlers' filters
        // (e.g. ListApplicationsHandler's lifecycle/teamId filters).
        if (q.Type is { } filterType)
            source = source.Where(r => r.Type == filterType);

        if (q.Type is { } t)
        {
            filters ??= new Dictionary<string, string>(StringComparer.Ordinal);
            filters["type"] = t.ToString();
        }

        var page = await source
            .ToCursorPagedAsync(
                spec, q.SortOrder, q.Cursor, q.Limit,
                RelationshipSortSpecs.IdSelector, IdExtractor, ct,
                expectedFilters: filters);

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

        // Batch-resolve creators for the "Added by" column (avoid N+1); unresolved
        // ids (system actor / offboarded creator) are simply absent → CreatedBy null.
        var creatorIds = page.Items.Select(r => r.CreatedByUserId).Distinct().ToList();
        var creators = await directory.GetManyAsync(creatorIds, ct);

        var items = page.Items
            .Select(r =>
            {
                var src = new EntityRefDto(r.Source.Kind, r.Source.Id, GetDisplayName(r.Source.Kind, r.Source.Id));
                var tgt = new EntityRefDto(r.Target.Kind, r.Target.Id, GetDisplayName(r.Target.Kind, r.Target.Id));
                var createdBy = creators.TryGetValue(r.CreatedByUserId, out var info) ? info : null;
                return r.ToResponse(src, tgt) with { CreatedBy = createdBy };
            })
            .ToList();

        return new CursorPage<RelationshipResponse>(items, page.NextCursor, page.PrevCursor);
    }
}
