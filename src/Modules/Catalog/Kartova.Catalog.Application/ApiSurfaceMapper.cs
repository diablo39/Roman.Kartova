using Kartova.Catalog.Contracts;
using Kartova.Catalog.Domain;

namespace Kartova.Catalog.Application;

/// <summary>Pure shaping of an entity's API surface (no EF). The handler prepares the four inputs
/// from RLS-scoped queries; this class does dedupe (direct-wins), origin assembly, and metadata join.</summary>
public static class ApiSurfaceMapper
{
    /// <summary>A provides/exposes edge target before metadata join. <paramref name="ViaApplicationId"/>
    /// is set only for derived edges (the application the API is exposed through).</summary>
    public sealed record ProvidesEdge(Guid ApiId, ApiSurfaceOrigin Origin, Guid? ViaApplicationId);

    /// <summary>Metadata for one API, batch-loaded from <c>catalog_apis</c> + <c>catalog_api_specs</c>.</summary>
    public sealed record ApiMeta(string DisplayName, ApiStyle Style, string Version, bool HasSpec);

    public static ApiSurfaceResponse Build(
        IReadOnlyList<ProvidesEdge> provides,
        IReadOnlyList<Guid> consumesApiIds,
        IReadOnlyDictionary<Guid, ApiMeta> apis,
        IReadOnlyDictionary<Guid, string> appNames)
    {
        // Provides: group by API id, DIRECT wins over DERIVED when an API appears both ways.
        var providesItems = provides
            .GroupBy(p => p.ApiId)
            .Select(g =>
            {
                var chosen = g.FirstOrDefault(p => p.Origin == ApiSurfaceOrigin.Direct) ?? g.First();
                return chosen;
            })
            .Where(p => apis.ContainsKey(p.ApiId)) // defensive: skip if metadata missing
            .Select(p =>
            {
                var meta = apis[p.ApiId];
                var derived = p.Origin == ApiSurfaceOrigin.Derived;
                return new ApiSurfaceItem(
                    p.ApiId, meta.DisplayName, meta.Style, meta.Version, meta.HasSpec, p.Origin,
                    derived ? p.ViaApplicationId : null,
                    derived && p.ViaApplicationId is { } via && appNames.TryGetValue(via, out var n) ? n : null);
            })
            .ToList();

        var consumesItems = consumesApiIds
            .Distinct()
            .Where(id => apis.ContainsKey(id))
            .Select(id =>
            {
                var meta = apis[id];
                return new ApiSurfaceItem(
                    id, meta.DisplayName, meta.Style, meta.Version, meta.HasSpec,
                    ApiSurfaceOrigin.Direct, null, null);
            })
            .ToList();

        return new ApiSurfaceResponse(providesItems, consumesItems);
    }
}
