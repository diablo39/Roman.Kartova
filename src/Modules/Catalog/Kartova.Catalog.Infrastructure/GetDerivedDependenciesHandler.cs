using Kartova.Catalog.Application;
using Kartova.Catalog.Contracts;
using Kartova.Catalog.Domain;

namespace Kartova.Catalog.Infrastructure;

/// <summary>Computes a focus service's derived depends-on relationships ON READ (ADR-0111 §Decision 5), split
/// into Dependencies (services the focus derives a depends-on TO — focus is the edge source) and Dependents
/// (services that derive a depends-on ON the focus — focus is the edge target). Reuses
/// <see cref="DerivedEdgeLoader"/> (tenant-wide RLS-scoped derivation, explicit-wins already applied) and
/// <see cref="DerivedProvenanceNames"/>; the other-service display name + team come from
/// <see cref="ICatalogEntityLookup"/>. Every id involved is in-tenant by construction (RLS-scoped fetch).</summary>
public sealed class GetDerivedDependenciesHandler
{
    public async Task<DerivedDependenciesResponse> Handle(
        GetDerivedDependenciesQuery q, CatalogDbContext db, ICatalogEntityLookup lookup, CancellationToken ct)
    {
        var all = await DerivedEdgeLoader.LoadAsync(db, ct);

        var dependencyEdges = all.Where(e => e.SourceServiceId == q.ServiceId).ToList(); // focus depends on TargetServiceId
        var dependentEdges = all.Where(e => e.TargetServiceId == q.ServiceId).ToList();  // SourceServiceId depends on focus

        var names = await DerivedProvenanceNames.LoadAsync(
            dependencyEdges.Concat(dependentEdges).SelectMany(e => e.Paths), db, ct);

        // Resolve the "other" service's display name + team. Bounded set (one service's derived neighbours) →
        // per-id lookup, mirroring GraphTraversalHandler's node enrichment.
        var otherIds = dependencyEdges.Select(e => e.TargetServiceId)
            .Concat(dependentEdges.Select(e => e.SourceServiceId))
            .Distinct()
            .ToList();

        var svc = new Dictionary<Guid, EntityLookupResult?>();
        foreach (var id in otherIds)
            svc[id] = await lookup.Find(EntityKind.Service, id, ct);

        DerivedDependencyItem ToItem(Guid otherServiceId, IReadOnlyList<DerivedDependencies.Path> paths)
        {
            var info = svc.GetValueOrDefault(otherServiceId);
            return new DerivedDependencyItem(
                otherServiceId,
                info?.DisplayName ?? string.Empty,
                info?.TeamId,
                paths.Select(names.Map).ToList());
        }

        var dependencies = dependencyEdges.Select(e => ToItem(e.TargetServiceId, e.Paths)).ToList();
        var dependents = dependentEdges.Select(e => ToItem(e.SourceServiceId, e.Paths)).ToList();
        return new DerivedDependenciesResponse(dependencies, dependents);
    }
}
