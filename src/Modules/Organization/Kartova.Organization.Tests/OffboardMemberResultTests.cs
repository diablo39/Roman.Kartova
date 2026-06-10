using Kartova.Organization.Application;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kartova.Organization.Tests;

/// <summary>
/// Shape tests for <see cref="OffboardMemberResult"/> (slice-10 Task 6). One test per static
/// factory — asserts ALL six fields so mutation testing cannot flip a boolean literal (or the
/// AppsReassigned count) without a guaranteed failing observer.
/// </summary>
[TestClass]
public sealed class OffboardMemberResultTests
{
    [TestMethod]
    public void Success_sets_only_Offboarded_and_carries_apps_count()
    {
        var r = OffboardMemberResult.Success(3);

        Assert.IsTrue(r.Offboarded);
        Assert.IsFalse(r.NotFound);
        Assert.IsFalse(r.CannotOffboardSelf);
        Assert.IsFalse(r.LastOrgAdmin);
        Assert.IsFalse(r.InvalidSuccessor);
        Assert.AreEqual(3, r.AppsReassigned);
    }

    [TestMethod]
    public void NotFoundResult_sets_only_NotFound()
    {
        var r = OffboardMemberResult.NotFoundResult;

        Assert.IsFalse(r.Offboarded);
        Assert.IsTrue(r.NotFound);
        Assert.IsFalse(r.CannotOffboardSelf);
        Assert.IsFalse(r.LastOrgAdmin);
        Assert.IsFalse(r.InvalidSuccessor);
        Assert.AreEqual(0, r.AppsReassigned);
    }

    [TestMethod]
    public void SelfResult_sets_only_CannotOffboardSelf()
    {
        var r = OffboardMemberResult.SelfResult;

        Assert.IsFalse(r.Offboarded);
        Assert.IsFalse(r.NotFound);
        Assert.IsTrue(r.CannotOffboardSelf);
        Assert.IsFalse(r.LastOrgAdmin);
        Assert.IsFalse(r.InvalidSuccessor);
        Assert.AreEqual(0, r.AppsReassigned);
    }

    [TestMethod]
    public void LastOrgAdminResult_sets_only_LastOrgAdmin()
    {
        var r = OffboardMemberResult.LastOrgAdminResult;

        Assert.IsFalse(r.Offboarded);
        Assert.IsFalse(r.NotFound);
        Assert.IsFalse(r.CannotOffboardSelf);
        Assert.IsTrue(r.LastOrgAdmin);
        Assert.IsFalse(r.InvalidSuccessor);
        Assert.AreEqual(0, r.AppsReassigned);
    }

    [TestMethod]
    public void InvalidSuccessorResult_sets_only_InvalidSuccessor()
    {
        var r = OffboardMemberResult.InvalidSuccessorResult;

        Assert.IsFalse(r.Offboarded);
        Assert.IsFalse(r.NotFound);
        Assert.IsFalse(r.CannotOffboardSelf);
        Assert.IsFalse(r.LastOrgAdmin);
        Assert.IsTrue(r.InvalidSuccessor);
        Assert.AreEqual(0, r.AppsReassigned);
    }
}
