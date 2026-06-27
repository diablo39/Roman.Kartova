// src/Modules/Catalog/Kartova.Catalog.Tests/GraphTraversalTests.cs
using Kartova.Catalog.Application;
using Kartova.Catalog.Domain;

namespace Kartova.Catalog.Tests;

[TestClass]
public class GraphTraversalTests
{
    private static readonly Guid F = Guid.NewGuid();
    private static readonly Guid A = Guid.NewGuid();
    private static readonly Guid B = Guid.NewGuid();
    private static readonly Guid C = Guid.NewGuid();

    private static EntityRef Svc(Guid id) => new(EntityKind.Service, id);

    private static GraphTraversalEdge Edge(Guid src, Guid tgt) =>
        new(Svc(src), Svc(tgt), Guid.NewGuid(), RelationshipType.DependsOn, RelationshipOrigin.Manual);

    // A fetch delegate over a fixed edge list: returns edges touching the frontier (source or target).
    private static Func<IReadOnlyCollection<EntityRef>, CancellationToken, Task<IReadOnlyList<GraphTraversalEdge>>>
        Fetch(params GraphTraversalEdge[] all) =>
        (frontier, _) =>
        {
            var ids = frontier.Select(f => f.Id).ToHashSet();
            IReadOnlyList<GraphTraversalEdge> hit =
                all.Where(e => ids.Contains(e.Source.Id) || ids.Contains(e.Target.Id)).ToList();
            return Task.FromResult(hit);
        };

    [TestMethod]
    public async Task focus_with_no_edges_returns_only_focus()
    {
        var r = await GraphTraversal.BuildAsync(Svc(F), 2, RelationshipDirection.All, 200, Fetch(), CancellationToken.None);
        Assert.AreEqual(1, r.Nodes.Count);
        Assert.AreEqual(F, r.Nodes[0].Ref.Id);
        Assert.AreEqual(0, r.Nodes[0].Depth);
        Assert.AreEqual(0, r.Edges.Count);
        Assert.IsFalse(r.Truncated);
    }

    [TestMethod]
    public async Task depth_2_discovers_two_hops_with_correct_depths()
    {
        // F -> A -> B
        var r = await GraphTraversal.BuildAsync(Svc(F), 2, RelationshipDirection.All, 200,
            Fetch(Edge(F, A), Edge(A, B)), CancellationToken.None);
        Assert.AreEqual(0, r.Nodes.Single(n => n.Ref.Id == F).Depth);
        Assert.AreEqual(1, r.Nodes.Single(n => n.Ref.Id == A).Depth);
        Assert.AreEqual(2, r.Nodes.Single(n => n.Ref.Id == B).Depth);
        Assert.AreEqual(2, r.Edges.Count);
    }

    [TestMethod]
    public async Task depth_1_excludes_the_second_hop()
    {
        var r = await GraphTraversal.BuildAsync(Svc(F), 1, RelationshipDirection.All, 200,
            Fetch(Edge(F, A), Edge(A, B)), CancellationToken.None);
        Assert.IsFalse(r.Nodes.Any(n => n.Ref.Id == B));
        Assert.AreEqual(1, r.Edges.Count); // only F->A; the A->B edge has an excluded endpoint
    }

    [TestMethod]
    public async Task outgoing_only_follows_source_to_target()
    {
        // C -> F (incoming to F) and F -> A (outgoing from F). Outgoing keeps only A.
        var r = await GraphTraversal.BuildAsync(Svc(F), 1, RelationshipDirection.Outgoing, 200,
            Fetch(Edge(C, F), Edge(F, A)), CancellationToken.None);
        Assert.IsTrue(r.Nodes.Any(n => n.Ref.Id == A));
        Assert.IsFalse(r.Nodes.Any(n => n.Ref.Id == C));
    }

    [TestMethod]
    public async Task incoming_only_follows_target_to_source()
    {
        var r = await GraphTraversal.BuildAsync(Svc(F), 1, RelationshipDirection.Incoming, 200,
            Fetch(Edge(C, F), Edge(F, A)), CancellationToken.None);
        Assert.IsTrue(r.Nodes.Any(n => n.Ref.Id == C));
        Assert.IsFalse(r.Nodes.Any(n => n.Ref.Id == A));
    }

    [TestMethod]
    public async Task cycle_terminates_with_one_node_per_entity()
    {
        // F -> A and A -> F (a 2-cycle)
        var r = await GraphTraversal.BuildAsync(Svc(F), 3, RelationshipDirection.All, 200,
            Fetch(Edge(F, A), Edge(A, F)), CancellationToken.None);
        Assert.AreEqual(2, r.Nodes.Count);          // F, A — each once
        Assert.AreEqual(2, r.Edges.Count);          // both edges present
    }

    [TestMethod]
    public async Task cap_truncates_and_flags()
    {
        // F -> A, F -> B, F -> C ; maxNodes=2 keeps focus + one neighbour
        var r = await GraphTraversal.BuildAsync(Svc(F), 1, RelationshipDirection.All, 2,
            Fetch(Edge(F, A), Edge(F, B), Edge(F, C)), CancellationToken.None);
        Assert.AreEqual(2, r.Nodes.Count);
        Assert.IsTrue(r.Truncated);
        Assert.IsTrue(r.Edges.All(e =>
            r.Nodes.Any(n => n.Ref.Id == e.Source.Id) && r.Nodes.Any(n => n.Ref.Id == e.Target.Id)));
    }
}
