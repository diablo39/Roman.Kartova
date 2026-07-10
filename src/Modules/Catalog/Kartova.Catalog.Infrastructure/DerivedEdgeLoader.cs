using Kartova.Catalog.Application;
using Kartova.Catalog.Domain;
using Microsoft.EntityFrameworkCore;

namespace Kartova.Catalog.Infrastructure;

/// <summary>Loads the tenant's full set of derived service→service depends-on edges (RLS-scoped): fetches the
/// four contributing edge kinds + explicit depends-on in one round-trip, then delegates shaping to the pure
/// <see cref="DerivedDependencies.Compute"/>. Shared by <see cref="GraphTraversalHandler"/> and
/// <see cref="GetDerivedDependenciesHandler"/> so the edge-fetch lives in exactly one place.</summary>
internal static class DerivedEdgeLoader
{
    private static readonly RelationshipType[] RelevantTypes =
    [
        RelationshipType.ConsumesApiFrom,
        RelationshipType.ProvidesApiFor,
        RelationshipType.InstanceOf,
        RelationshipType.DependsOn,
    ];

    public static async Task<IReadOnlyList<DerivedDependencies.Edge>> LoadAsync(
        CatalogDbContext db, CancellationToken ct)
    {
        var rels = await db.Relationships
            .Where(r => RelevantTypes.Contains(r.Type))
            .Select(r => new { SK = r.Source.Kind, SI = r.Source.Id, TK = r.Target.Kind, TI = r.Target.Id, r.Type })
            .ToListAsync(ct);

        var consumes = rels.Where(r => r.Type == RelationshipType.ConsumesApiFrom
                && r.SK == EntityKind.Service && r.TK == EntityKind.Api)
            .Select(r => (r.SI, r.TI)).ToList();
        var serviceProvides = rels.Where(r => r.Type == RelationshipType.ProvidesApiFor
                && r.SK == EntityKind.Service && r.TK == EntityKind.Api)
            .Select(r => (r.SI, r.TI)).ToList();
        var instanceOf = rels.Where(r => r.Type == RelationshipType.InstanceOf
                && r.SK == EntityKind.Service && r.TK == EntityKind.Application)
            .Select(r => (r.SI, r.TI)).ToList();
        var appProvides = rels.Where(r => r.Type == RelationshipType.ProvidesApiFor
                && r.SK == EntityKind.Application && r.TK == EntityKind.Api)
            .Select(r => (r.SI, r.TI)).ToList();
        var explicitDeps = rels.Where(r => r.Type == RelationshipType.DependsOn
                && r.SK == EntityKind.Service && r.TK == EntityKind.Service)
            .Select(r => (r.SI, r.TI)).ToHashSet();

        return DerivedDependencies.Compute(consumes, serviceProvides, instanceOf, appProvides, explicitDeps);
    }
}
