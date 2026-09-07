using Kartova.Catalog.Application;
using Kartova.Catalog.Domain;
using static Kartova.Catalog.Application.HierarchyAssembler;

namespace Kartova.Catalog.Tests;

[TestClass]
public sealed class HierarchyAssemblerTests
{
    private static readonly Guid TeamA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid TeamB = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid SysX = Guid.Parse("33333333-3333-3333-3333-333333333333");

    [TestMethod]
    public void Member_lands_under_system_steward_team_not_owning_team()
    {
        // System X stewarded by Team A; its member app is OWNED by Team B.
        var app = Guid.NewGuid();
        var systems = new[] { new SystemRow(SysX, "X", TeamA) };
        var components = new[] { new ComponentRow(EntityKind.Application, app, "App", TeamB) };
        var partOf = new Dictionary<(EntityKind, Guid), Guid> { [(EntityKind.Application, app)] = SysX };

        var result = Build(systems, components, partOf, nodeCap: 200);

        var teamA = result.Teams.Single(t => t.TeamId == TeamA);
        var sys = teamA.Systems.Single();
        Assert.AreEqual(SysX, sys.SystemId);
        Assert.AreEqual(1, sys.ComponentCount);
        Assert.AreEqual(app, sys.Members.Single().Id);
        Assert.AreEqual("application", sys.Members.Single().Kind);
        // Team B has no stewarded system and no ungrouped component of its own → absent from the response.
        Assert.IsFalse(result.Teams.Any(t => t.TeamId == TeamB));
        Assert.AreEqual(1, teamA.ComponentCount);
        Assert.AreEqual(1, result.TotalComponentCount);
    }

    [TestMethod]
    public void Component_without_partof_lands_in_owning_team_ungrouped()
    {
        var svc = Guid.NewGuid();
        var components = new[] { new ComponentRow(EntityKind.Service, svc, "Svc", TeamB) };

        var result = Build(Array.Empty<SystemRow>(), components,
            new Dictionary<(EntityKind, Guid), Guid>(), nodeCap: 200);

        var teamB = result.Teams.Single(t => t.TeamId == TeamB);
        Assert.AreEqual(0, teamB.Systems.Count);
        Assert.AreEqual(1, teamB.Ungrouped.ComponentCount);
        Assert.AreEqual(svc, teamB.Ungrouped.Members.Single().Id);
        Assert.AreEqual("service", teamB.Ungrouped.Members.Single().Kind);
        Assert.AreEqual(1, teamB.ComponentCount);
    }

    [TestMethod]
    public void Each_component_appears_exactly_once_and_counts_roll_up()
    {
        var inSys = Guid.NewGuid();
        var free = Guid.NewGuid();
        var systems = new[] { new SystemRow(SysX, "X", TeamA) };
        var components = new[]
        {
            new ComponentRow(EntityKind.Application, inSys, "InSys", TeamA),
            new ComponentRow(EntityKind.Service, free, "Free", TeamA),
        };
        var partOf = new Dictionary<(EntityKind, Guid), Guid> { [(EntityKind.Application, inSys)] = SysX };

        var result = Build(systems, components, partOf, nodeCap: 200);

        var teamA = result.Teams.Single();
        Assert.AreEqual(2, teamA.ComponentCount);                 // 1 in system + 1 ungrouped
        Assert.AreEqual(1, teamA.Systems.Single().ComponentCount);
        Assert.AreEqual(1, teamA.Ungrouped.ComponentCount);
        Assert.AreEqual(2, result.TotalComponentCount);
        var allMemberIds = teamA.Systems.SelectMany(s => s.Members).Concat(teamA.Ungrouped.Members)
            .Select(m => m.Id).ToList();
        CollectionAssert.AreEquivalent(new[] { inSys, free }, allMemberIds);   // no duplication
    }

    [TestMethod]
    public void Members_and_systems_are_ordered_by_display_name_ascending()
    {
        var s1 = Guid.NewGuid(); var s2 = Guid.NewGuid();
        var a = Guid.NewGuid(); var b = Guid.NewGuid();
        var sysB = Guid.NewGuid();
        var systems = new[] { new SystemRow(SysX, "Zeta", TeamA), new SystemRow(sysB, "Alpha", TeamA) };
        var components = new[]
        {
            new ComponentRow(EntityKind.Service, a, "banana", TeamA),
            new ComponentRow(EntityKind.Service, b, "apple", TeamA),
        };
        var partOf = new Dictionary<(EntityKind, Guid), Guid>
        {
            [(EntityKind.Service, a)] = SysX, [(EntityKind.Service, b)] = SysX,
        };

        var result = Build(systems, components, partOf, nodeCap: 200);

        var teamA = result.Teams.Single();
        CollectionAssert.AreEqual(new[] { "Alpha", "Zeta" }, teamA.Systems.Select(s => s.DisplayName).ToArray());
        var zeta = teamA.Systems.Single(s => s.DisplayName == "Zeta");
        CollectionAssert.AreEqual(new[] { "apple", "banana" }, zeta.Members.Select(m => m.DisplayName).ToArray());
    }

    [TestMethod]
    public void Node_cap_sets_truncated_and_stops_adding_members()
    {
        var systems = new[] { new SystemRow(SysX, "X", TeamA) };
        var components = Enumerable.Range(0, 5)
            .Select(i => new ComponentRow(EntityKind.Service, Guid.NewGuid(), $"svc-{i:D2}", TeamA))
            .ToArray();
        var partOf = components.ToDictionary(c => (c.Kind, c.Id), _ => SysX);

        var result = Build(systems, components, partOf, nodeCap: 3);

        Assert.IsTrue(result.Truncated);
        var keptNames = result.Teams.SelectMany(t => t.Systems.SelectMany(s => s.Members))
            .Concat(result.Teams.Select(t => t.Ungrouped).SelectMany(u => u.Members))
            .Select(m => m.DisplayName)
            .ToList();
        Assert.AreEqual(3, keptNames.Count);
        Assert.AreEqual(3, result.TotalComponentCount);
        CollectionAssert.AreEquivalent(new[] { "svc-00", "svc-01", "svc-02" }, keptNames);
    }

    [TestMethod]
    public void Dangling_partof_falls_through_to_owning_team_ungrouped()
    {
        // partOf points at a systemId that does not exist in systems → must not vanish; falls back
        // to the component's own OWNING team's Ungrouped bucket.
        var svc = Guid.NewGuid();
        var danglingSystemId = Guid.NewGuid();
        var components = new[] { new ComponentRow(EntityKind.Service, svc, "Svc", TeamA) };
        var partOf = new Dictionary<(EntityKind, Guid), Guid> { [(EntityKind.Service, svc)] = danglingSystemId };

        var result = Build(Array.Empty<SystemRow>(), components, partOf, nodeCap: 200);

        var teamA = result.Teams.Single(t => t.TeamId == TeamA);
        Assert.AreEqual(0, teamA.Systems.Count);
        Assert.AreEqual(svc, teamA.Ungrouped.Members.Single().Id);
        Assert.AreEqual(1, result.TotalComponentCount);
    }
}
