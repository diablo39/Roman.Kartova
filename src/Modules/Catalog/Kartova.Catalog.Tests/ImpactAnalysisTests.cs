using Kartova.Catalog.Application;
using Kartova.Catalog.Domain;

namespace Kartova.Catalog.Tests;

[TestClass]
public sealed class ImpactAnalysisTests
{
    private static EntityRef Svc(int n) => new(EntityKind.Service, Guid.Parse($"00000000-0000-0000-0001-0000000000{n:D2}"));
    private static EntityRef App(int n) => new(EntityKind.Application, Guid.Parse($"00000000-0000-0000-0002-0000000000{n:D2}"));

    // Edge (Source, Target) = "Source depends on Target".
    private static (EntityRef, EntityRef) Dep(EntityRef source, EntityRef target) => (source, target);

    [TestMethod]
    public void Direct_dependent_is_tier_1()
    {
        var f = Svc(0);
        var a = Svc(1); // a depends on f
        var r = ImpactAnalysis.Compute(f, [Dep(a, f)], nodeCap: 200);

        var node = r.Impacted.Single();
        Assert.AreEqual(a, node.Ref);
        Assert.AreEqual(1, node.Tier);
        Assert.IsFalse(r.Truncated);
    }

    [TestMethod]
    public void Chain_tiers_by_hop_distance()
    {
        var f = Svc(0);
        var b = Svc(1); // b depends on f
        var a = Svc(2); // a depends on b
        var r = ImpactAnalysis.Compute(f, [Dep(b, f), Dep(a, b)], nodeCap: 200);

        Assert.AreEqual(1, r.Impacted.Single(n => n.Ref == b).Tier);
        Assert.AreEqual(2, r.Impacted.Single(n => n.Ref == a).Tier);
        Assert.AreEqual(2, r.Impacted.Count);
    }

    [TestMethod]
    public void Diamond_assigns_min_tier_once()
    {
        var f = Svc(0);
        var b = Svc(1);
        var c = Svc(2);
        var a = Svc(3); // a depends on both b and c; b,c depend on f
        var r = ImpactAnalysis.Compute(f, [Dep(b, f), Dep(c, f), Dep(a, b), Dep(a, c)], nodeCap: 200);

        Assert.AreEqual(3, r.Impacted.Count); // a counted once
        Assert.AreEqual(2, r.Impacted.Single(n => n.Ref == a).Tier);
    }

    [TestMethod]
    public void Cycle_terminates()
    {
        var f = Svc(0);
        var a = Svc(1);
        var b = Svc(2);
        // a↔b cycle, a depends on f
        var r = ImpactAnalysis.Compute(f, [Dep(a, f), Dep(a, b), Dep(b, a)], nodeCap: 200);

        Assert.AreEqual(1, r.Impacted.Single(n => n.Ref == a).Tier);
        Assert.AreEqual(2, r.Impacted.Single(n => n.Ref == b).Tier);
        Assert.AreEqual(2, r.Impacted.Count);
    }

    [TestMethod]
    public void Leaf_focus_has_no_dependents()
    {
        var f = Svc(0);
        var other = Svc(1); // f depends on other (outgoing only) — not a dependent of f
        var r = ImpactAnalysis.Compute(f, [Dep(f, other)], nodeCap: 200);

        Assert.AreEqual(0, r.Impacted.Count);
        Assert.IsFalse(r.Truncated);
    }

    [TestMethod]
    public void Mixed_kinds_traverse_across_app_and_service()
    {
        var f = Svc(0);
        var app = App(1); // app depends on f
        var svc = Svc(2); // svc depends on app
        var r = ImpactAnalysis.Compute(f, [Dep(app, f), Dep(svc, app)], nodeCap: 200);

        Assert.AreEqual(1, r.Impacted.Single(n => n.Ref == app).Tier);
        Assert.AreEqual(2, r.Impacted.Single(n => n.Ref == svc).Tier);
    }

    [TestMethod]
    public void Node_cap_truncates()
    {
        var f = Svc(0);
        var edges = new[] { Dep(Svc(1), f), Dep(Svc(2), f), Dep(Svc(3), f) };
        var r = ImpactAnalysis.Compute(f, edges, nodeCap: 2);

        Assert.AreEqual(2, r.Impacted.Count);
        Assert.IsTrue(r.Truncated);
    }

    [TestMethod]
    public void Focus_never_appears_in_impacted()
    {
        var f = Svc(0);
        var a = Svc(1);
        var r = ImpactAnalysis.Compute(f, [Dep(a, f), Dep(f, a)], nodeCap: 200);

        Assert.IsFalse(r.Impacted.Any(n => n.Ref == f));
    }
}
