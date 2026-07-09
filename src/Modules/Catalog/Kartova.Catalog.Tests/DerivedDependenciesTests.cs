using Kartova.Catalog.Application;

namespace Kartova.Catalog.Tests;

[TestClass]
public class DerivedDependenciesTests
{
    private static readonly Guid S = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid T = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid U = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid App = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid App2 = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid Api1 = Guid.Parse("66666666-6666-6666-6666-666666666666");
    private static readonly Guid Api2 = Guid.Parse("77777777-7777-7777-7777-777777777777");

    private static readonly IReadOnlySet<(Guid, Guid)> NoExplicit = new HashSet<(Guid, Guid)>();

    private static IReadOnlyList<DerivedDependencies.Edge> Compute(
        (Guid, Guid)[] consumes = null!, (Guid, Guid)[] serviceProvides = null!,
        (Guid, Guid)[] instanceOf = null!, (Guid, Guid)[] appProvides = null!,
        IReadOnlySet<(Guid, Guid)> explicitDeps = null!) =>
        DerivedDependencies.Compute(
            consumes ?? [], serviceProvides ?? [], instanceOf ?? [], appProvides ?? [], explicitDeps ?? NoExplicit);

    [TestMethod]
    public void direct_provide_path_yields_edge_with_null_via_app()
    {
        // S consumes Api1; T provides Api1 directly → S depends-on T, viaApp null.
        var edges = Compute(consumes: [(S, Api1)], serviceProvides: [(T, Api1)]);
        var e = edges.Single();
        Assert.AreEqual(S, e.SourceServiceId);
        Assert.AreEqual(T, e.TargetServiceId);
        var p = e.Paths.Single();
        Assert.AreEqual(Api1, p.ApiId);
        Assert.IsNull(p.ViaAppId);
    }

    [TestMethod]
    public void via_app_path_yields_edge_with_via_app_populated()
    {
        // S consumes Api1; T instance-of App; App provides Api1 → viaApp = App.
        var edges = Compute(consumes: [(S, Api1)], instanceOf: [(T, App)], appProvides: [(App, Api1)]);
        var p = edges.Single().Paths.Single();
        Assert.AreEqual(Api1, p.ApiId);
        Assert.AreEqual(App, p.ViaAppId);
    }

    [TestMethod]
    public void same_api_direct_and_via_app_dedupes_to_two_distinct_paths()
    {
        // T both provides Api1 directly AND via App. Distinct paths (null via + App via), one edge.
        var edges = Compute(
            consumes: [(S, Api1)], serviceProvides: [(T, Api1)],
            instanceOf: [(T, App)], appProvides: [(App, Api1)]);
        var e = edges.Single();
        Assert.AreEqual(2, e.Paths.Count);
        Assert.IsTrue(e.Paths.Any(p => p.ViaAppId is null));
        Assert.IsTrue(e.Paths.Any(p => p.ViaAppId == App));
    }

    [TestMethod]
    public void multiple_apis_collapse_to_one_edge_with_multiple_paths()
    {
        // S consumes Api1 and Api2; T provides both → one edge, two paths.
        var edges = Compute(consumes: [(S, Api1), (S, Api2)], serviceProvides: [(T, Api1), (T, Api2)]);
        var e = edges.Single();
        Assert.AreEqual(2, e.Paths.Count);
        CollectionAssert.AreEquivalent(new[] { Api1, Api2 }, e.Paths.Select(p => p.ApiId).ToArray());
    }

    [TestMethod]
    public void explicit_depends_on_suppresses_derived_edge_for_that_pair()
    {
        var edges = Compute(
            consumes: [(S, Api1)], serviceProvides: [(T, Api1)],
            explicitDeps: new HashSet<(Guid, Guid)> { (S, T) });
        Assert.AreEqual(0, edges.Count);
    }

    [TestMethod]
    public void self_dependency_is_never_emitted()
    {
        // S consumes Api1 and S also provides Api1 → no S→S edge.
        var edges = Compute(consumes: [(S, Api1)], serviceProvides: [(S, Api1)]);
        Assert.AreEqual(0, edges.Count);
    }

    [TestMethod]
    public void consumer_without_matching_provider_yields_nothing()
    {
        var edges = Compute(consumes: [(S, Api1)], serviceProvides: [(T, Api2)]);
        Assert.AreEqual(0, edges.Count);
    }

    [TestMethod]
    public void two_consumers_of_same_provider_yield_two_edges()
    {
        // S and U both consume Api1 that T provides → S→T and U→T.
        var edges = Compute(consumes: [(S, Api1), (U, Api1)], serviceProvides: [(T, Api1)]);
        Assert.AreEqual(2, edges.Count);
        Assert.IsTrue(edges.Any(e => e.SourceServiceId == S && e.TargetServiceId == T));
        Assert.IsTrue(edges.Any(e => e.SourceServiceId == U && e.TargetServiceId == T));
    }

    [TestMethod]
    public void empty_inputs_yield_empty()
    {
        Assert.AreEqual(0, Compute().Count);
    }

    [TestMethod]
    public void paths_are_deterministically_ordered()
    {
        // Two apps expose the same API for T; order must be stable (by apiId, then viaAppId).
        var edges = Compute(
            consumes: [(S, Api1)], instanceOf: [(T, App2), (T, App)], appProvides: [(App, Api1), (App2, Api1)]);
        var vias = edges.Single().Paths.Select(p => p.ViaAppId).ToList();
        var sorted = vias.OrderBy(v => v).ToList();
        CollectionAssert.AreEqual(sorted, vias);
    }
}
