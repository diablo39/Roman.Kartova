using Kartova.Catalog.Application;
using Kartova.Catalog.Contracts;
using Kartova.Catalog.Domain;
using Microsoft.EntityFrameworkCore;

namespace Kartova.Catalog.Infrastructure;

public sealed class GraphTraversalHandler
{
    public const int DefaultNodeCap = 200;

    public async Task<GraphResponse> Handle(
        GraphTraversalQuery q,
        CatalogDbContext db,
        ICatalogEntityLookup lookup,
        CancellationToken ct,
        int? maxNodes = null)
    {
        // Precompute tenant's full set of derived service->service depends-on edges ONCE (RLS-scoped).
        // Keyed by source AND target for O(1) frontier lookup; provenance carried on the synthetic edge.
        var derivedAll = await DerivedEdgeLoader.LoadAsync(db, ct);
        var derivedBySource = derivedAll.ToLookup(e => e.SourceServiceId);
        var derivedByTarget = derivedAll.ToLookup(e => e.TargetServiceId);

        var result = await GraphTraversal.BuildAsync(
            q.Focus, q.Depth, q.Direction, maxNodes ?? DefaultNodeCap,
            async (frontier, token) =>
            {
                var ids = frontier.Select(f => f.Id).ToList();
                var rows = await db.Relationships
                    .Where(r => ids.Contains(r.Source.Id) || ids.Contains(r.Target.Id))
                    .ToListAsync(token);

                var edges = rows
                    .Select(r => new GraphTraversalEdge(
                        new EntityRef(r.Source.Kind, r.Source.Id),
                        new EntityRef(r.Target.Kind, r.Target.Id),
                        r.Id.Value, r.Type, r.Origin))
                    .ToList();

                // Merge in derived depends-on edges touching this frontier so they drive discovery
                // too (source OR target in the current frontier), deduped by (source, target) pair.
                // Every hit already touches a frontier service by construction (it came from that
                // service's source- or target-keyed lookup), so no extra frontier filter is needed.
                // NOTE: a derived edge is smuggled through the persisted GraphTraversalEdge type with a
                // synthetic Id + a placeholder RelationshipOrigin (never surfaced — it is re-split out
                // by `Provenance is not null` below into DerivedEdgeDto). This keeps GraphTraversal.BuildAsync's
                // signature unchanged; a typed derived channel would be the deeper fix if BuildAsync ever changes.
                var derivedHits = frontier
                    .Where(f => f.Kind == EntityKind.Service)
                    .SelectMany(f => derivedBySource[f.Id].Concat(derivedByTarget[f.Id]))
                    .DistinctBy(e => (e.SourceServiceId, e.TargetServiceId))
                    .Select(e => new GraphTraversalEdge(
                        new EntityRef(EntityKind.Service, e.SourceServiceId),
                        new EntityRef(EntityKind.Service, e.TargetServiceId),
                        SyntheticEdgeId(e.SourceServiceId, e.TargetServiceId),
                        RelationshipType.DependsOn, RelationshipOrigin.Manual,
                        e.Paths))
                    .ToList();

                edges.AddRange(derivedHits);
                return (IReadOnlyList<GraphTraversalEdge>)edges;
            },
            ct);

        // NOTE (N+1 enrichment): ICatalogEntityLookup.Find is called once per distinct node ref
        // (up to the 200-node cap = up to ~200 sequential queries). This is a conscious deferral —
        // bounded by the node cap, consistent with ListRelationshipsForEntityHandler's per-ref
        // enrichment pattern. Batch with grouped IN-queries if this endpoint ever shows latency at
        // the cap (e.g. replace with a single bulk-lookup overload on ICatalogEntityLookup).
        // Enrich displayName + teamId for every node (batched over distinct refs).
        var info = new Dictionary<(EntityKind, Guid), EntityLookupResult?>();
        foreach (var n in result.Nodes)
        {
            var key = (n.Ref.Kind, n.Ref.Id);
            if (!info.ContainsKey(key))
                info[key] = await lookup.Find(n.Ref.Kind, n.Ref.Id, ct);
        }

        // Per-node explicit-relationship degree (RLS-scoped). Boundary nodes have no fetched
        // neighbours, so degree cannot come from result.Edges — one batched count over the
        // returned node ids (≤ node cap). Ids are globally unique, so counting by Id is exact.
        var nodeIds = result.Nodes.Select(n => n.Ref.Id).Distinct().ToList();
        var degreeRows = await db.Relationships
            .Where(r => nodeIds.Contains(r.Source.Id) || nodeIds.Contains(r.Target.Id))
            .Select(r => new { SourceId = r.Source.Id, TargetId = r.Target.Id })
            .ToListAsync(ct);
        var outDeg = degreeRows.GroupBy(x => x.SourceId).ToDictionary(g => g.Key, g => g.Count());
        var inDeg = degreeRows.GroupBy(x => x.TargetId).ToDictionary(g => g.Key, g => g.Count());

        var nodes = result.Nodes.Select(n =>
        {
            var found = info[(n.Ref.Kind, n.Ref.Id)];
            return new GraphNodeDto(
                n.Ref.Kind, n.Ref.Id, found?.DisplayName ?? string.Empty, n.Depth, found?.TeamId,
                outDeg.GetValueOrDefault(n.Ref.Id), inDeg.GetValueOrDefault(n.Ref.Id));
        }).ToList();

        // Persisted edges (Provenance == null) -> GraphEdgeDto; derived edges (Provenance != null) -> DerivedEdgeDto.
        var persisted = result.Edges.Where(e => e.Provenance is null)
            .Select(e => new GraphEdgeDto(
                e.Id, new GraphEndpointDto(e.Source.Kind, e.Source.Id),
                new GraphEndpointDto(e.Target.Kind, e.Target.Id), e.Type, e.Origin))
            .ToList();

        var derivedKept = result.Edges.Where(e => e.Provenance is not null).ToList();
        var derivedEdges = await MapDerivedEdges(derivedKept, db, ct);

        return new GraphResponse(nodes, persisted, derivedEdges, result.Truncated);
    }

    private static Guid SyntheticEdgeId(Guid source, Guid target)
    {
        // Deterministic GUID from an ordered pair (XOR bytes) — stable across the two BFS scans.
        var a = source.ToByteArray();
        var b = target.ToByteArray();
        var mixed = new byte[16];
        for (var i = 0; i < 16; i++) mixed[i] = (byte)(a[i] ^ b[(i + 7) % 16]);
        return new Guid(mixed);
    }

    private static async Task<IReadOnlyList<DerivedEdgeDto>> MapDerivedEdges(
        IReadOnlyList<GraphTraversalEdge> derivedKept, CatalogDbContext db, CancellationToken ct)
    {
        if (derivedKept.Count == 0) return Array.Empty<DerivedEdgeDto>();

        var names = await DerivedProvenanceNames.LoadAsync(derivedKept.SelectMany(e => e.Provenance!), db, ct);

        return derivedKept.Select(e => new DerivedEdgeDto(
            new GraphEndpointDto(e.Source.Kind, e.Source.Id),
            new GraphEndpointDto(e.Target.Kind, e.Target.Id),
            e.Provenance!.Select(names.Map).ToList()))
            .ToList();
    }
}
