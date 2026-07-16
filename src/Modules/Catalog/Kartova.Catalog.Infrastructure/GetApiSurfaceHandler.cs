using Kartova.Catalog.Application;
using Kartova.Catalog.Contracts;
using Kartova.Catalog.Domain;
using Microsoft.EntityFrameworkCore;

namespace Kartova.Catalog.Infrastructure;

/// <summary>Computes a component's API surface ON READ (ADR-0111 §Decision 3): direct provides +
/// (Service only) derived exposes via instance-of, plus direct consumes. RLS scopes every query, so
/// cross-tenant edges/APIs never appear. Shaping (dedupe/direct-wins/metadata join) is delegated to
/// the pure <see cref="ApiSurfaceMapper"/>.</summary>
public sealed class GetApiSurfaceHandler
{
    public async Task<ApiSurfaceResponse> Handle(
        GetApiSurfaceQuery q, CatalogDbContext db, CancellationToken ct)
    {
        // Single round-trip: fetch all edges where the focus entity is the source across the three relevant types
        // (provides / instance-of / consumes), then partition in memory below.
        var sourceEdges = await db.Relationships
            .Where(r => r.Source.Kind == q.Kind && r.Source.Id == q.EntityId
                && (r.Type == RelationshipType.ProvidesApiFor
                    || r.Type == RelationshipType.InstanceOf
                    || r.Type == RelationshipType.ConsumesApiFrom))
            .Select(r => new { r.Id, r.Type, TargetKind = r.Target.Kind, TargetId = r.Target.Id })
            .ToListAsync(ct);

        var provides = sourceEdges
            .Where(e => e.Type == RelationshipType.ProvidesApiFor && e.TargetKind == EntityKind.Api)
            .Select(e => new ApiSurfaceMapper.ProvidesEdge(e.TargetId, ApiSurfaceOrigin.Direct, null, e.Id.Value))
            .ToList();

        if (q.Kind == EntityKind.Service)
        {
            var instanceAppIds = sourceEdges
                .Where(e => e.Type == RelationshipType.InstanceOf && e.TargetKind == EntityKind.Application)
                .Select(e => e.TargetId)
                .ToList();

            if (instanceAppIds.Count > 0)
            {
                var derived = await db.Relationships
                    .Where(r => r.Type == RelationshipType.ProvidesApiFor
                                && r.Source.Kind == EntityKind.Application
                                && instanceAppIds.Contains(r.Source.Id)
                                && r.Target.Kind == EntityKind.Api)
                    .Select(r => new { ApiId = r.Target.Id, ViaAppId = r.Source.Id })
                    .ToListAsync(ct);

                provides.AddRange(derived.Select(d =>
                    new ApiSurfaceMapper.ProvidesEdge(d.ApiId, ApiSurfaceOrigin.Derived, d.ViaAppId, null)));
            }
        }

        var consumes = sourceEdges
            .Where(e => e.Type == RelationshipType.ConsumesApiFrom && e.TargetKind == EntityKind.Api)
            .Select(e => new ApiSurfaceMapper.ConsumesEdge(e.TargetId, e.Id.Value))
            .ToList();

        // 4. Batch-load API metadata for every referenced id.
        var apiGuids = provides.Select(p => p.ApiId).Concat(consumes.Select(c => c.ApiId)).Distinct().ToList();

        var apiRows = await db.Apis
            .Where(a => apiGuids.Contains(EF.Property<Guid>(a, EfApiConfiguration.IdFieldName)))
            .Select(a => new
            {
                Id = EF.Property<Guid>(a, EfApiConfiguration.IdFieldName),
                a.DisplayName, a.Style, a.Version,
            })
            .ToListAsync(ct);

        // has-spec: presence of a row in catalog_api_specs (1:1). Mirrors ListApisHandler.
        var apiIdKeys = apiGuids.Select(g => new ApiId(g)).ToList();
        var idsWithSpec = (await db.ApiSpecs
                .Where(s => apiIdKeys.Contains(s.ApiId))
                .Select(s => s.ApiId)
                .ToListAsync(ct))
            .Select(id => id.Value)
            .ToHashSet();

        var apis = apiRows.ToDictionary(
            a => a.Id,
            a => new ApiSurfaceMapper.ApiMeta(a.DisplayName, a.Style, a.Version, idsWithSpec.Contains(a.Id)));

        // 5. `via` application display names (derived rows only).
        var viaAppIds = provides
            .Where(p => p.Origin == ApiSurfaceOrigin.Derived && p.ViaApplicationId is not null)
            .Select(p => p.ViaApplicationId!.Value)
            .Distinct()
            .ToList();

        var appNames = viaAppIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await db.Applications
                .Where(a => viaAppIds.Contains(EF.Property<Guid>(a, EfApplicationConfiguration.IdFieldName)))
                .Select(a => new { Id = EF.Property<Guid>(a, EfApplicationConfiguration.IdFieldName), a.DisplayName })
                .ToDictionaryAsync(x => x.Id, x => x.DisplayName, ct);

        return ApiSurfaceMapper.Build(provides, consumes, apis, appNames);
    }
}
