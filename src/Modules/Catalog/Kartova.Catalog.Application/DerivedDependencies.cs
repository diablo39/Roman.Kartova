namespace Kartova.Catalog.Application;

/// <summary>Pure derivation of service→service depends-on edges (ADR-0111 §Decision 5):
/// S depends-on T when S consumes an API in T's provided surface (T provides directly, or T is
/// instance-of an application that provides it). Service↔Service only; no self-edge; explicit
/// depends-on pairs are suppressed. Names are joined later by the handler.</summary>
public static class DerivedDependencies
{
    /// <summary>One provenance path: the linking API and (for a via-app exposure) the application.
    /// <paramref name="ViaAppId"/> is null when the provider provides the API directly.</summary>
    public sealed record Path(Guid ApiId, Guid? ViaAppId);

    /// <summary>A derived S→T edge with every provenance path linking them.</summary>
    public sealed record Edge(Guid SourceServiceId, Guid TargetServiceId, IReadOnlyList<Path> Paths);

    public static IReadOnlyList<Edge> Compute(
        IReadOnlyCollection<(Guid ServiceId, Guid ApiId)> consumes,
        IReadOnlyCollection<(Guid ServiceId, Guid ApiId)> serviceProvides,
        IReadOnlyCollection<(Guid ServiceId, Guid AppId)> instanceOf,
        IReadOnlyCollection<(Guid AppId, Guid ApiId)> appProvides,
        IReadOnlySet<(Guid Source, Guid Target)> explicitDependsOn)
    {
        // providersByApi: apiId → set of (providerServiceId, viaAppId?) — T's provided surface.
        var providersByApi = new Dictionary<Guid, HashSet<(Guid Provider, Guid? ViaApp)>>();

        void Add(Guid apiId, Guid provider, Guid? viaApp)
        {
            if (!providersByApi.TryGetValue(apiId, out var set))
                providersByApi[apiId] = set = [];
            set.Add((provider, viaApp));
        }

        foreach (var (svc, api) in serviceProvides) Add(api, svc, null);

        // instance-of ⋈ app-provides: service T instance-of App A, A provides X → T exposes X via A.
        var appProvidesByApp = appProvides
            .GroupBy(x => x.AppId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.ApiId).ToList());
        foreach (var (svc, app) in instanceOf)
            if (appProvidesByApp.TryGetValue(app, out var apis))
                foreach (var api in apis)
                    Add(api, svc, app);

        // For each (S consumes X) and each provider (T, viaApp) of X with S != T, collect a path.
        var pathsByPair = new Dictionary<(Guid S, Guid T), List<Path>>();
        foreach (var (consumer, api) in consumes)
        {
            if (!providersByApi.TryGetValue(api, out var providers)) continue;
            foreach (var (provider, viaApp) in providers)
            {
                if (provider == consumer) continue;                          // no self-edge
                if (explicitDependsOn.Contains((consumer, provider))) continue; // explicit wins
                var key = (consumer, provider);
                if (!pathsByPair.TryGetValue(key, out var list))
                    pathsByPair[key] = list = [];
                list.Add(new Path(api, viaApp));
            }
        }

        return pathsByPair
            .Select(kv => new Edge(
                kv.Key.S, kv.Key.T,
                kv.Value
                    .Distinct()
                    .OrderBy(p => p.ApiId).ThenBy(p => p.ViaAppId)
                    .ToList()))
            .OrderBy(e => e.SourceServiceId).ThenBy(e => e.TargetServiceId)
            .ToList();
    }
}
