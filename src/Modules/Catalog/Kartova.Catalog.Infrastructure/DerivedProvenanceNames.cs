using Kartova.Catalog.Application;
using Kartova.Catalog.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Kartova.Catalog.Infrastructure;

/// <summary>Batch-resolves API + via-application display names for a set of derivation paths, then maps each
/// <see cref="DerivedDependencies.Path"/> to a <see cref="DerivationPathDto"/>. Shared by
/// <see cref="GraphTraversalHandler"/> and <see cref="GetDerivedDependenciesHandler"/> so the name-join lives
/// in exactly one place.</summary>
internal sealed class DerivedProvenanceNames
{
    private readonly IReadOnlyDictionary<Guid, string> _apiNames;
    private readonly IReadOnlyDictionary<Guid, string> _appNames;

    private DerivedProvenanceNames(
        IReadOnlyDictionary<Guid, string> apiNames, IReadOnlyDictionary<Guid, string> appNames)
    {
        _apiNames = apiNames;
        _appNames = appNames;
    }

    public static async Task<DerivedProvenanceNames> LoadAsync(
        IEnumerable<DerivedDependencies.Path> paths, CatalogDbContext db, CancellationToken ct)
    {
        var pathList = paths as ICollection<DerivedDependencies.Path> ?? paths.ToList();
        var apiIds = pathList.Select(p => p.ApiId).Distinct().ToList();
        var appIds = pathList.Where(p => p.ViaAppId is not null).Select(p => p.ViaAppId!.Value).Distinct().ToList();

        var apiNames = apiIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await db.Apis
                .Where(a => apiIds.Contains(EF.Property<Guid>(a, EfApiConfiguration.IdFieldName)))
                .Select(a => new { Id = EF.Property<Guid>(a, EfApiConfiguration.IdFieldName), a.DisplayName })
                .ToDictionaryAsync(x => x.Id, x => x.DisplayName, ct);
        var appNames = appIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await db.Applications
                .Where(a => appIds.Contains(EF.Property<Guid>(a, EfApplicationConfiguration.IdFieldName)))
                .Select(a => new { Id = EF.Property<Guid>(a, EfApplicationConfiguration.IdFieldName), a.DisplayName })
                .ToDictionaryAsync(x => x.Id, x => x.DisplayName, ct);

        return new DerivedProvenanceNames(apiNames, appNames);
    }

    // The empty/null fallbacks below are currently UNREACHABLE: api/app ids are re-derived from a fresh
    // RLS-scoped query on every request, and there is no Api/Application delete path today, so a provenance
    // id can never fail to resolve. A future entity-delete slice MUST revisit this — deleting a referenced
    // Api/Application would otherwise silently render blank provenance instead of surfacing the dangling ref.
    public DerivationPathDto Map(DerivedDependencies.Path p) => new(
        p.ApiId,
        _apiNames.TryGetValue(p.ApiId, out var apiName) ? apiName : string.Empty,
        p.ViaAppId,
        p.ViaAppId is { } via && _appNames.TryGetValue(via, out var appName) ? appName : null);
}
