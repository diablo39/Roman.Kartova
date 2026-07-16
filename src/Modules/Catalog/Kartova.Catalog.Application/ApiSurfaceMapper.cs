using Kartova.Catalog.Contracts;
using Kartova.Catalog.Domain;

namespace Kartova.Catalog.Application;

/// <summary>Pure shaping of an entity's API surface (no EF). The handler prepares the four inputs
/// from RLS-scoped queries; this class does dedupe (direct-wins), origin assembly, and metadata join.</summary>
public static class ApiSurfaceMapper
{
    /// <summary>A provides/exposes edge target before metadata join. <paramref name="ViaApplicationId"/>
    /// is set only for derived edges (the application the API is exposed through). <paramref name="RelationshipId"/>
    /// is the underlying direct edge id (null for derived — a derived row has no single owning edge here).</summary>
    public sealed record ProvidesEdge(Guid ApiId, ApiSurfaceOrigin Origin, Guid? ViaApplicationId, Guid? RelationshipId);

    /// <summary>A direct consumes edge: the consumed API plus the owning relationship id (for removal).</summary>
    public sealed record ConsumesEdge(Guid ApiId, Guid RelationshipId);

    /// <summary>Metadata for one API, batch-loaded from <c>catalog_apis</c> + <c>catalog_api_specs</c>.</summary>
    public sealed record ApiMeta(string DisplayName, ApiStyle Style, string Version, bool HasSpec);

    public static ApiSurfaceResponse Build(
        IReadOnlyList<ProvidesEdge> provides,
        IReadOnlyList<ConsumesEdge> consumes,
        IReadOnlyDictionary<Guid, ApiMeta> apis,
        IReadOnlyDictionary<Guid, string> appNames)
    {
        // Provides: group by API id, DIRECT wins over DERIVED when an API appears both ways.
        // Among DERIVED candidates (no direct edge present), pick deterministically by smallest
        // ViaApplicationId — otherwise the surviving via-app is order-dependent when a service is
        // instance-of multiple applications that all provide the same API.
        var providesItems = provides
            .GroupBy(p => p.ApiId)
            .Select(g =>
            {
                var direct = g.FirstOrDefault(p => p.Origin == ApiSurfaceOrigin.Direct);
                if (direct is not null)
                {
                    return direct;
                }

                return g.OrderBy(p => p.ViaApplicationId).First();
            })
            // Currently unreachable: edges validate target existence at creation and there is no API-delete path,
            // so every referenced id is in `apis`. Guards a future delete/soft-delete path.
            .Where(p => apis.ContainsKey(p.ApiId))
            .Select(p =>
            {
                var meta = apis[p.ApiId];
                var derived = p.Origin == ApiSurfaceOrigin.Derived;
                return new ApiSurfaceItem(
                    p.ApiId, meta.DisplayName, meta.Style, meta.Version, meta.HasSpec, p.Origin,
                    derived ? p.ViaApplicationId : null,
                    derived && p.ViaApplicationId is { } via && appNames.TryGetValue(via, out var n) ? n : null,
                    derived ? null : p.RelationshipId);
            })
            .ToList();

        var consumesItems = consumes
            .GroupBy(c => c.ApiId)
            .Select(g => g.First())
            // Currently unreachable: edges validate target existence at creation and there is no API-delete path,
            // so every referenced id is in `apis`. Guards a future delete/soft-delete path.
            .Where(c => apis.ContainsKey(c.ApiId))
            .Select(c =>
            {
                var meta = apis[c.ApiId];
                return new ApiSurfaceItem(
                    c.ApiId, meta.DisplayName, meta.Style, meta.Version, meta.HasSpec,
                    ApiSurfaceOrigin.Direct, null, null, c.RelationshipId);
            })
            .ToList();

        return new ApiSurfaceResponse(providesItems, consumesItems);
    }
}
