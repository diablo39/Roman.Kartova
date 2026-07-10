using Kartova.Catalog.Domain;

namespace Kartova.Catalog.Application;

/// <summary>Pure blast-radius traversal (E-04.F-02.S-06): the transitive set of entities that depend
/// on <paramref name="focus"/>, following depends-on edges in the DEPENDENTS direction (an edge
/// Source→Target means "Source depends on Target"; a dependent of F is any Source on a path that
/// reaches F). Tier = hop distance from focus; focus is tier 0 and excluded from the result. First-seen
/// tier wins (cycle-safe). Stops once <paramref name="nodeCap"/> impacted nodes are collected →
/// <see cref="Result.Truncated"/>. The edge set is explicit ∪ derived depends-on, unioned by the
/// handler; this helper is agnostic to how an edge arose.</summary>
public static class ImpactAnalysis
{
    public sealed record Node(EntityRef Ref, int Tier);

    public sealed record Result(IReadOnlyList<Node> Impacted, bool Truncated);

    public static Result Compute(
        EntityRef focus,
        IReadOnlyCollection<(EntityRef Source, EntityRef Target)> dependsOnEdges,
        int nodeCap)
    {
        // dependentsOf[X] = every Source that depends on X (edges whose Target == X).
        var dependentsOf = new Dictionary<EntityRef, List<EntityRef>>();
        foreach (var (source, target) in dependsOnEdges)
        {
            if (!dependentsOf.TryGetValue(target, out var list))
                dependentsOf[target] = list = [];
            list.Add(source);
        }

        var tier = new Dictionary<EntityRef, int> { [focus] = 0 };
        var impacted = new List<Node>();
        var frontier = new List<EntityRef> { focus };
        var truncated = false;

        for (var level = 1; frontier.Count > 0 && !truncated; level++)
        {
            var next = new List<EntityRef>();
            foreach (var node in frontier)
            {
                if (!dependentsOf.TryGetValue(node, out var dependents)) continue;
                foreach (var dep in dependents)
                {
                    if (tier.ContainsKey(dep)) continue;         // first-seen tier wins (cycle-safe)
                    if (impacted.Count >= nodeCap) { truncated = true; break; }
                    tier[dep] = level;
                    impacted.Add(new Node(dep, level));
                    next.Add(dep);
                }
                if (truncated) break;
            }
            frontier = next;
        }

        return new Result(impacted, truncated);
    }
}
