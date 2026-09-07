using Kartova.Catalog.Application;
using Kartova.Catalog.Contracts;
using Kartova.Catalog.Domain;
using Microsoft.EntityFrameworkCore;
using static Kartova.Catalog.Application.HierarchyAssembler;

namespace Kartova.Catalog.Infrastructure;

/// <summary>Assembles the catalog hierarchy (E-03.F-03.S-02) from RLS-scoped Catalog-local data:
/// systems (steward team), App/Service components (owning team), and PartOf membership edges. Returns
/// team IDs only — names are resolved frontend-side (ADR-0082). Node cap + Truncated mirror /graph
/// and /impact. Value objects (Id.Value) and complex Source/Target are read in memory after
/// materialization — EF cannot translate them in a projection (mirrors GraphTraversalHandler / GetImpactAnalysisHandler).</summary>
public sealed class GetCatalogHierarchyHandler
{
    public const int DefaultNodeCap = 200;

    public async Task<CatalogHierarchyResponse> Handle(
        GetCatalogHierarchyQuery q, CatalogDbContext db, CancellationToken ct)
    {
        var systems = (await db.Systems.ToListAsync(ct))
            .Select(s => new SystemRow(s.Id.Value, s.DisplayName, s.TeamId))
            .ToList();

        var apps = (await db.Applications.ToListAsync(ct))
            .Select(a => new ComponentRow(EntityKind.Application, a.Id.Value, a.DisplayName, a.TeamId));
        var services = (await db.Services.ToListAsync(ct))
            .Select(s => new ComponentRow(EntityKind.Service, s.Id.Value, s.DisplayName, s.TeamId));
        var components = apps.Concat(services).ToList();

        // Array .Contains (not a lone `== PartOf`): a lone equality collapses to WHERE FALSE against the
        // KnownRelationshipTypes global query filter — see GetImpactAnalysisHandler / DerivedEdgeLoader.
        var partOfOnly = new[] { RelationshipType.PartOf };
        var partOfEdges = await db.Relationships
            .Where(r => partOfOnly.Contains(r.Type))
            .ToListAsync(ct);

        // PartOf: Source = component, Target = System (SetComponentSystemHandler). At-most-one per component
        // (ux_relationships_one_system) so the last-write-wins dictionary build is safe.
        var partOf = new Dictionary<(EntityKind Kind, Guid Id), Guid>();
        foreach (var r in partOfEdges)
            partOf[(r.Source.Kind, r.Source.Id)] = r.Target.Id;

        return Build(systems, components, partOf, DefaultNodeCap);
    }
}
