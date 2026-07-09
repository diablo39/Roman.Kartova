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
        var result = await GraphTraversal.BuildAsync(
            q.Focus, q.Depth, q.Direction, maxNodes ?? DefaultNodeCap,
            async (frontier, token) =>
            {
                var ids = frontier.Select(f => f.Id).ToList();
                var rows = await db.Relationships
                    .Where(r => ids.Contains(r.Source.Id) || ids.Contains(r.Target.Id))
                    .ToListAsync(token);
                IReadOnlyList<GraphTraversalEdge> edges = rows
                    .Select(r => new GraphTraversalEdge(
                        new EntityRef(r.Source.Kind, r.Source.Id),
                        new EntityRef(r.Target.Kind, r.Target.Id),
                        r.Id.Value, r.Type, r.Origin))
                    .ToList();
                return edges;
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

        var nodes = result.Nodes.Select(n =>
        {
            var found = info[(n.Ref.Kind, n.Ref.Id)];
            return new GraphNodeDto(n.Ref.Kind, n.Ref.Id, found?.DisplayName ?? string.Empty, n.Depth, found?.TeamId);
        }).ToList();

        var edges = result.Edges.Select(e => new GraphEdgeDto(
            e.Id,
            new GraphEndpointDto(e.Source.Kind, e.Source.Id),
            new GraphEndpointDto(e.Target.Kind, e.Target.Id),
            e.Type, e.Origin)).ToList();

        return new GraphResponse(nodes, edges, Array.Empty<DerivedEdgeDto>(), result.Truncated);
    }
}
