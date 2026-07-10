using Kartova.Catalog.Application;
using Kartova.Catalog.Contracts;
using Kartova.Catalog.Domain;
using Microsoft.EntityFrameworkCore;

namespace Kartova.Catalog.Infrastructure;

/// <summary>Computes a focus entity's blast radius (E-04.F-02.S-06): the transitive set of entities
/// that depend on it over explicit ∪ derived depends-on edges (ADR-0111 §5), tiered by hop distance.
/// Emits the reused <see cref="GraphResponse"/> contract so the explorer merges the impacted nodes/edges
/// via mergeGraphs — tier rides in <see cref="GraphNodeDto.Depth"/>; OutDegree/InDegree are 0 (affordance
/// unused in the impact overlay). Node cap + Truncated mirror the /graph endpoint.</summary>
public sealed class GetImpactAnalysisHandler
{
    public const int DefaultNodeCap = 200;

    public async Task<GraphResponse> Handle(
        GetImpactAnalysisQuery q, CatalogDbContext db, ICatalogEntityLookup lookup, CancellationToken ct)
    {
        // Explicit depends-on relationships (any app/service pair), RLS-scoped. A bare `r.Type == X`
        // predicate collapses to `WHERE FALSE` when combined with the entity's global query filter
        // (KnownRelationshipTypes.Contains(r.Type), see EfRelationshipConfiguration) — every other
        // handler in this module filters relationship type via an array .Contains(), never a lone `==`
        // (see DerivedEdgeLoader.RelevantTypes / GetApiSurfaceHandler); mirror that shape here.
        // Materialize then read the Id value object + complex Source/Target in memory (EF can't
        // translate r.Id.Value in a projection — mirrors GraphTraversalHandler).
        var dependsOnOnly = new[] { RelationshipType.DependsOn };
        var explicitRels = await db.Relationships
            .Where(r => dependsOnOnly.Contains(r.Type))
            .ToListAsync(ct);

        // Derived service→service depends-on edges (shared loader; explicit-wins already applied).
        var derivedAll = await DerivedEdgeLoader.LoadAsync(db, ct);

        // Unified directed edge set for the pure traversal (Source depends on Target).
        var edges = new List<(EntityRef Source, EntityRef Target)>(explicitRels.Count + derivedAll.Count);
        edges.AddRange(explicitRels.Select(r =>
            (new EntityRef(r.Source.Kind, r.Source.Id), new EntityRef(r.Target.Kind, r.Target.Id))));
        edges.AddRange(derivedAll.Select(e =>
            (new EntityRef(EntityKind.Service, e.SourceServiceId), new EntityRef(EntityKind.Service, e.TargetServiceId))));

        var focus = new EntityRef(q.FocusKind, q.FocusId);
        var result = ImpactAnalysis.Compute(focus, edges, DefaultNodeCap);

        // Closure = focus (tier 0) + impacted. tierByRef drives node projection; closure gates edge inclusion.
        var tierByRef = new Dictionary<EntityRef, int> { [focus] = 0 };
        foreach (var n in result.Impacted) tierByRef[n.Ref] = n.Tier;
        var closure = tierByRef.Keys.ToHashSet();

        // Enrich displayName + teamId per node (bounded by cap; per-id, mirrors GraphTraversalHandler).
        var nodes = new List<GraphNodeDto>(tierByRef.Count);
        foreach (var (nodeRef, t) in tierByRef)
        {
            var info = await lookup.Find(nodeRef.Kind, nodeRef.Id, ct);
            nodes.Add(new GraphNodeDto(
                nodeRef.Kind, nodeRef.Id, info?.DisplayName ?? string.Empty, t, info?.TeamId,
                OutDegree: 0, InDegree: 0));
        }

        // Explicit depends-on edges within the closure → GraphEdgeDto (real ids dedupe with /graph edges FE-side).
        var persisted = explicitRels
            .Where(r => closure.Contains(new EntityRef(r.Source.Kind, r.Source.Id))
                     && closure.Contains(new EntityRef(r.Target.Kind, r.Target.Id)))
            .Select(r => new GraphEdgeDto(
                r.Id.Value,
                new GraphEndpointDto(r.Source.Kind, r.Source.Id),
                new GraphEndpointDto(r.Target.Kind, r.Target.Id),
                RelationshipType.DependsOn, r.Origin))
            .ToList();

        // Derived edges within the closure → DerivedEdgeDto (provenance names via the shared loader).
        var derivedKept = derivedAll
            .Where(e => closure.Contains(new EntityRef(EntityKind.Service, e.SourceServiceId))
                     && closure.Contains(new EntityRef(EntityKind.Service, e.TargetServiceId)))
            .ToList();
        var names = await DerivedProvenanceNames.LoadAsync(derivedKept.SelectMany(e => e.Paths), db, ct);
        var derivedEdges = derivedKept
            .Select(e => new DerivedEdgeDto(
                new GraphEndpointDto(EntityKind.Service, e.SourceServiceId),
                new GraphEndpointDto(EntityKind.Service, e.TargetServiceId),
                e.Paths.Select(names.Map).ToList()))
            .ToList();

        return new GraphResponse(nodes, persisted, derivedEdges, result.Truncated);
    }
}
