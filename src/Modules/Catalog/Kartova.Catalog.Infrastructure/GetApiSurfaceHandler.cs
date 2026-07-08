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
        GetApiSurfaceQuery q, CatalogDbContext db, ICatalogEntityLookup lookup, CancellationToken ct)
    {
        // 1. Direct provides (component -> Api).
        var directProvides = await db.Relationships
            .Where(r => r.Source.Kind == q.Kind && r.Source.Id == q.EntityId
                        && r.Type == RelationshipType.ProvidesApiFor
                        && r.Target.Kind == EntityKind.Api)
            .Select(r => r.Target.Id)
            .ToListAsync(ct);

        var provides = directProvides
            .Select(id => new ApiSurfaceMapper.ProvidesEdge(id, ApiSurfaceOrigin.Direct, null))
            .ToList();

        // 2. Derived exposes — Service only: instance-of App(s), then those apps' provided APIs.
        if (q.Kind == EntityKind.Service)
        {
            var instanceAppIds = await db.Relationships
                .Where(r => r.Source.Kind == EntityKind.Service && r.Source.Id == q.EntityId
                            && r.Type == RelationshipType.InstanceOf
                            && r.Target.Kind == EntityKind.Application)
                .Select(r => r.Target.Id)
                .ToListAsync(ct);

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
                    new ApiSurfaceMapper.ProvidesEdge(d.ApiId, ApiSurfaceOrigin.Derived, d.ViaAppId)));
            }
        }

        // 3. Direct consumes.
        var consumesApiIds = await db.Relationships
            .Where(r => r.Source.Kind == q.Kind && r.Source.Id == q.EntityId
                        && r.Type == RelationshipType.ConsumesApiFrom
                        && r.Target.Kind == EntityKind.Api)
            .Select(r => r.Target.Id)
            .ToListAsync(ct);

        // 4. Batch-load API metadata for every referenced id.
        var apiGuids = provides.Select(p => p.ApiId).Concat(consumesApiIds).Distinct().ToList();

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

        var appNames = new Dictionary<Guid, string>();
        foreach (var appId in viaAppIds)
        {
            var found = await lookup.Find(EntityKind.Application, appId, ct);
            if (found is not null) appNames[appId] = found.DisplayName;
        }

        return ApiSurfaceMapper.Build(provides, consumesApiIds, apis, appNames);
    }
}
