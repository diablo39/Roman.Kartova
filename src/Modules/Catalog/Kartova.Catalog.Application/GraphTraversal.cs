// src/Modules/Catalog/Kartova.Catalog.Application/GraphTraversal.cs
using Kartova.Catalog.Domain;

namespace Kartova.Catalog.Application;

public sealed record GraphTraversalEdge(
    EntityRef Source, EntityRef Target, Guid Id, RelationshipType Type, RelationshipOrigin Origin,
    IReadOnlyList<DerivedDependencies.Path>? Provenance = null);

public sealed record GraphTraversalNode(EntityRef Ref, int Depth);

public sealed record GraphTraversalResult(
    IReadOnlyList<GraphTraversalNode> Nodes, IReadOnlyList<GraphTraversalEdge> Edges, bool Truncated);

public static class GraphTraversal
{
    public static async Task<GraphTraversalResult> BuildAsync(
        EntityRef focus,
        int depth,
        RelationshipDirection direction,
        int maxNodes,
        Func<IReadOnlyCollection<EntityRef>, CancellationToken, Task<IReadOnlyList<GraphTraversalEdge>>> fetchEdgesTouching,
        CancellationToken ct)
    {
        static (EntityKind Kind, Guid Id) Key(EntityRef r) => (r.Kind, r.Id);

        var nodeDepth = new Dictionary<(EntityKind, Guid), int> { [Key(focus)] = 0 };
        var keptEdges = new Dictionary<Guid, GraphTraversalEdge>();
        var truncated = false;
        var frontier = new List<EntityRef> { focus };

        for (var level = 0; level < depth && frontier.Count > 0; level++)
        {
            var edges = await fetchEdgesTouching(frontier, ct);
            var next = new List<EntityRef>();
            var inFrontier = frontier.Select(Key).ToHashSet();

            foreach (var e in edges)
            {
                // Decide the neighbour reachable from a frontier node, per direction.
                var srcInFrontier = inFrontier.Contains(Key(e.Source));
                var tgtInFrontier = inFrontier.Contains(Key(e.Target));
                EntityRef? neighbour = direction switch
                {
                    RelationshipDirection.Outgoing => srcInFrontier ? e.Target : null,
                    RelationshipDirection.Incoming => tgtInFrontier ? e.Source : null,
                    _ => srcInFrontier ? e.Target : (tgtInFrontier ? e.Source : null),
                };
                if (neighbour is null) continue;

                var nb = neighbour.Value;
                if (!nodeDepth.ContainsKey(Key(nb)))
                {
                    if (nodeDepth.Count >= maxNodes) { truncated = true; continue; }
                    nodeDepth[Key(nb)] = level + 1;
                    next.Add(nb);
                }
            }
            frontier = next;
        }

        // Materialise nodes, then include only edges whose both endpoints survived.
        var nodes = nodeDepth.Select(kv => new GraphTraversalNode(
            new EntityRef(kv.Key.Item1, kv.Key.Item2), kv.Value)).ToList();

        // Re-scan edges once more over the kept set to capture every edge between two kept
        // nodes (including cross-links between neighbours); edges to capped-out nodes are dropped.
        // DESIGN: `direction` prunes node *discovery* only; edge inclusion here is intentionally
        // undirected-among-kept-nodes — all relationships between any two surviving nodes are shown
        // regardless of direction. This gives a complete picture of their interconnections.
        // A directed-edge filter on the final surface is deferred to S-06 (impact analysis).
        var keptRefs = nodes.Select(n => n.Ref).ToList();
        var allTouching = await fetchEdgesTouching(keptRefs, ct);
        foreach (var e in allTouching)
        {
            if (nodeDepth.ContainsKey(Key(e.Source)) && nodeDepth.ContainsKey(Key(e.Target)))
                keptEdges[e.Id] = e;
        }

        return new GraphTraversalResult(nodes, keptEdges.Values.ToList(), truncated);
    }
}
