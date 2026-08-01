using Kartova.Catalog.Application;

namespace Kartova.Catalog.Tests;

/// <summary>Unit tests for the at-most-one-System decision (ADR-0111 amended, A1 spec §2).
/// The handler only executes this decision; all branch logic lives here so it is
/// database-free and mutation-testable.</summary>
[TestClass]
public sealed class SystemMembershipTests
{
    private static readonly Guid SysA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid SysB = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Rel1 = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid Rel2 = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002");

    [TestMethod]
    public void Unassigned_component_assigned_to_a_system_inserts_and_deletes_nothing()
    {
        var d = SystemMembership.Decide([], SysA);

        Assert.IsTrue(d.InsertRequested);
        Assert.AreEqual(0, d.RelationshipIdsToDelete.Count);
    }

    [TestMethod]
    public void Component_already_in_the_requested_system_is_a_no_op()
    {
        var d = SystemMembership.Decide([new ExistingMembership(Rel1, SysA)], SysA);

        Assert.IsFalse(d.InsertRequested);
        Assert.AreEqual(0, d.RelationshipIdsToDelete.Count);
    }

    [TestMethod]
    public void Moving_to_another_system_deletes_the_old_edge_and_inserts()
    {
        var d = SystemMembership.Decide([new ExistingMembership(Rel1, SysA)], SysB);

        Assert.IsTrue(d.InsertRequested);
        Assert.AreEqual(1, d.RelationshipIdsToDelete.Count);
        Assert.AreEqual(Rel1, d.RelationshipIdsToDelete[0]);
    }

    [TestMethod]
    public void Clearing_deletes_every_existing_edge_and_does_not_insert()
    {
        var d = SystemMembership.Decide(
            [new ExistingMembership(Rel1, SysA), new ExistingMembership(Rel2, SysB)], null);

        Assert.IsFalse(d.InsertRequested);
        Assert.AreEqual(2, d.RelationshipIdsToDelete.Count);
        Assert.IsTrue(d.RelationshipIdsToDelete.Contains(Rel1));
        Assert.IsTrue(d.RelationshipIdsToDelete.Contains(Rel2));
    }

    [TestMethod]
    public void Clearing_an_unassigned_component_is_a_no_op()
    {
        var d = SystemMembership.Decide([], null);

        Assert.IsFalse(d.InsertRequested);
        Assert.AreEqual(0, d.RelationshipIdsToDelete.Count);
    }

    [TestMethod]
    public void Pre_existing_multi_membership_collapses_keeping_the_requested_edge()
    {
        // Defensive path: S-01's permissive window (and direct DB writes) could leave
        // two PartOf edges. Requesting SysA keeps Rel1 and drops the stray Rel2.
        var d = SystemMembership.Decide(
            [new ExistingMembership(Rel1, SysA), new ExistingMembership(Rel2, SysB)], SysA);

        Assert.IsFalse(d.InsertRequested);
        Assert.AreEqual(1, d.RelationshipIdsToDelete.Count);
        Assert.AreEqual(Rel2, d.RelationshipIdsToDelete[0]);
    }

    [TestMethod]
    public void Duplicate_rows_to_the_requested_system_collapse_to_one()
    {
        var d = SystemMembership.Decide(
            [new ExistingMembership(Rel1, SysA), new ExistingMembership(Rel2, SysA)], SysA);

        Assert.IsFalse(d.InsertRequested);
        Assert.AreEqual(1, d.RelationshipIdsToDelete.Count);
        Assert.AreEqual(Rel2, d.RelationshipIdsToDelete[0], "keeps the first matching edge, drops the duplicate");
    }

    [TestMethod]
    public void Two_non_matching_edges_are_both_deleted_before_inserting()
    {
        var d = SystemMembership.Decide(
            [new ExistingMembership(Rel1, SysB), new ExistingMembership(Rel2, SysB)], SysA);

        Assert.IsTrue(d.InsertRequested);
        Assert.AreEqual(2, d.RelationshipIdsToDelete.Count);
    }
}
